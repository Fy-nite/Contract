using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.AST;
using ObjektRT.Core.Builder;
using ObjektRT.Core.Serialization;
using ContractAccess = Contract.Compiler.AST.AccessModifier;
using IRAccess = ObjektRT.Core.AST.AccessModifier;

namespace Contract.Compiler.CodeGen;

public class IRCodeGenerator
{
    private readonly DiagnosticBag _diagnostics;
    private IRBuilder? _builder;
    private string[] _sourceLines = null!;
    private bool _lastIsReturn = false;
    private int _lambdaCounter = 0;
    private int _closureCounter = 0;
    private Dictionary<string, LambdaInfo> _lambdaVariableMap = new();
    private readonly Dictionary<string, LambdaInfo> _lambdaInfos = new();
    private Program? _program;
    // Enclosing loop continue-emitters: when a 'continue' runs, it must leave the
    // next condition on the stack (for-loops also run their update first).
    private readonly Stack<Action<InstructionBuilder>> _loopContinueEmitters = new();
    // Function-typed parameters of the function currently being generated,
    // used to emit callvirt Delegate.Invoke at call sites.
    private Dictionary<string, TypeDescriptor.Function> _functionTypedParams = new();
    // Function-typed locals (e.g. a delegate stored in a variable from a call).
    private Dictionary<string, TypeDescriptor.Function> _functionTypedLocals = new();
    private bool _delegateClassEmitted;
    // Enclosing-scope variable types (params + locals), used for capture analysis
    // and closure field types. Fresh per function.
    private Dictionary<string, TypeDescriptor> _variableTypes = new();
    // Active lambda-capture context (set while generating a capturing lambda body).
    private string? _closureClass;
    private int? _closureArgIndex;
    private HashSet<string>? _captureNames;
    private HashSet<string>? _lambdaBodyLocals;
    // Instance-method context: the arg index of 'this' and the declaring contract
    // (used for field access like `x = ...` or `this.x`).
    private int? _thisArgIndex;
    private string? _currentContractName;

    private const string DelegateClassName = "Delegate";
    private const string DelegateTargetField = "target";
    private const string DelegateClosureField = "closure";
    private const string DelegateInvokeMethod = "Invoke";

    /// <summary>Everything the codegen needs to know about a synthesized lambda.</summary>
    private sealed class LambdaInfo
    {
        public string Name = "";
        public string? ClosureClass;                       // null when no captures
        public List<(string Name, TypeDescriptor Type)> Captures = new();
        public List<TypeDescriptor> ParamTypes = new();
        public TypeDescriptor ReturnType = new TypeDescriptor.Named("int");
        public bool HasCaptures => ClosureClass != null;
    }

    public IRCodeGenerator(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void WriteToFile(string outputPath)
    {
        if (_builder == null) return;
        File.WriteAllText(outputPath, _builder.Build().Serialize().DumpToIRCode());
    }

    /// <summary>Returns the generated ObjektIR text, or null when nothing was generated.</summary>
    public string? GetIRText()
        => _builder?.Build().Serialize().DumpToIRCode();

    public void Generate(Program program)
    {
        _program = program;
        _sourceLines = _diagnostics.SourceCode?.Split('\n') ?? Array.Empty<string>();
        _builder = new IRBuilder(program.Contracts.Count > 0 ? program.Contracts[0].Name : "Program");

        // Top-level structs.
        foreach (var structDecl in program.Structs)
        {
            var structBuilder = _builder.Struct(structDecl.Name);
            foreach (var attr in structDecl.Attributes)
            {
                structBuilder.Attribute(attr.Name, attr.Arguments.ToArray());
            }
            foreach (var field in structDecl.Fields)
            {
                structBuilder.Field(field.Name, MapType(field.Type));
            }
            structBuilder.EndStruct();
        }

        foreach (var cls in program.Contracts)
        {
            var classBuilder = _builder.Class(cls.Name);

            // Type-level attributes; attribute types are additionally marked
            // with the built-in @Attribute annotation so the runtime/host can
            // recognise them without resolving the external base.
            foreach (var attr in cls.Attributes)
            {
                classBuilder.Attribute(attr.Name, attr.Arguments.ToArray());
            }
            if (cls.IsAttributeType)
            {
                classBuilder.Attribute("Attribute");
            }
            if (cls.BaseTypeName != null)
            {
                classBuilder.Extends(cls.BaseTypeName);
            }

            // Instance fields: emitted as class fields.
            foreach (var field in cls.Fields)
            {
                classBuilder.Field(field.Name, MapType(field.Type));
            }

            foreach (var ctor in cls.Constructors)
            {
                GenerateConstructor(classBuilder, ctor, cls.Name);
            }

            foreach (var member in cls.Members)
            {
                if (member is FunctionDeclaration func)
                    GenerateFunction(classBuilder, func);
                else if (member is StructDeclaration structDecl)
                {
                    var structBuilder = _builder.Struct(structDecl.Name);
                    foreach (var attr in structDecl.Attributes)
                    {
                        structBuilder.Attribute(attr.Name, attr.Arguments.ToArray());
                    }
                    foreach (var field in structDecl.Fields)
                    {
                        structBuilder.Field(field.Name, MapType(field.Type));
                    }
                    structBuilder.EndStruct();
                }
            }

            classBuilder.EndClass();
        }

        if (program.Functions.Count > 0)
        {
            var globalClass = _builder.Class("Global");
            foreach (var func in program.Functions)
            {
                GenerateFunction(globalClass, func);
            }
            globalClass.EndClass();
        }
    }

    private void GenerateFunction(ClassBuilder cb, FunctionDeclaration func)
    {
        _lastIsReturn = false;
        _functionTypedParams = new();
        _functionTypedLocals = new();
        foreach (var p in func.Parameters)
        {
            if (p.Type is TypeDescriptor.Function fnType)
                _functionTypedParams[p.Name] = fnType;
        }

        // Lambda methods carry capture context; every function gets a fresh
        // variable-type scope (params + any captures registered below).
        var savedVariableTypes = _variableTypes;
        var savedClosureClass = _closureClass;
        var savedClosureArg = _closureArgIndex;
        var savedCaptureNames = _captureNames;
        var savedBodyLocals = _lambdaBodyLocals;
        _variableTypes = new Dictionary<string, TypeDescriptor>();
        _lambdaBodyLocals = new HashSet<string>();

        _lambdaInfos.TryGetValue(func.Name, out var lambdaInfo);
        if (lambdaInfo != null && lambdaInfo.HasCaptures)
        {
            _closureClass = lambdaInfo.ClosureClass;
            _closureArgIndex = 0; // __closure is the first parameter
            _captureNames = lambdaInfo.Captures.Select(c => c.Name).ToHashSet();
            foreach (var c in lambdaInfo.Captures)
                _variableTypes[c.Name] = c.Type;
        }
        else
        {
            _closureClass = null;
            _closureArgIndex = null;
            _captureNames = null;
        }

        // Explicit return type if declared; otherwise guess: Main is void, others are int32 by default in v1
        var returnType = func.ReturnType != null
            ? MapType(func.ReturnType)
            : func.Name == "Main" ? TypeRef.Void : TypeRef.Int32;
        var mb = cb.Method(func.Name, returnType);

        foreach (var attr in func.Attributes)
        {
            mb.Attribute(attr.Name, attr.Arguments.ToArray());
        }

        if (func.IsStatic) mb.Static();
        
        mb.Access(func.Access switch {
            ContractAccess.Public => IRAccess.Public,
            ContractAccess.Private => IRAccess.Private,
            ContractAccess.Protected => IRAccess.Protected,
            ContractAccess.Internal => IRAccess.Internal,
            _ => IRAccess.Public
        });

        // Instance methods take 'this' as arg 0 (matching the runtime's
        // positional-arg model); the IR stores it as a parameter named 'this'.
        var paramMap = new Dictionary<string, int>();
        int paramOffset = 0;
        if (func.IsInstance)
        {
            mb.Parameter("this", new TypeRef("object"));
            paramMap["this"] = 0;
            _variableTypes["this"] = new TypeDescriptor.Named("object");
            _currentContractName = func.ContractName;
            paramOffset = 1;
        }
        for (int i = 0; i < func.Parameters.Count; i++)
        {
            var p = func.Parameters[i];
            mb.Parameter(p.Name, MapType(p.Type));
            paramMap[p.Name] = i + paramOffset;
            _variableTypes[p.Name] = p.Type;
        }

        var savedThisIndex = _thisArgIndex;
        _thisArgIndex = func.IsInstance ? 0 : (int?)null;

        var ib = mb.Body();
        if (func.Body != null)
        {
            GenerateStatement(ib, func.Body, paramMap);
        }

        // Implicit return safety
        if (!_lastIsReturn)
        {
            if (returnType == TypeRef.Void) ib.Ret();
            else if (returnType == TypeRef.String) ib.Ldnull().Ret();
            else if (returnType == TypeRef.Float32) ib.LdcR4(0).Ret();
            else if (returnType == TypeRef.Float64) ib.LdcR8(0).Ret();
            else ib.LdcI4(0).Ret();
        }

        ib.EndBody().EndMethod();

        _variableTypes = savedVariableTypes;
        _closureClass = savedClosureClass;
        _closureArgIndex = savedClosureArg;
        _captureNames = savedCaptureNames;
        _lambdaBodyLocals = savedBodyLocals;
        _thisArgIndex = savedThisIndex;
    }

    /// <summary>True when 'name' resolves to a field of the current instance.</summary>
    private bool IsInstanceField(string name)
        => _thisArgIndex != null
           && _currentContractName != null
           && _program?.Contracts.Any(c => c.Name == _currentContractName && c.Fields.Any(f => f.Name == name)) == true;

    /// <summary>The field reference for 'name' on the current contract.</summary>
    private FieldReference InstanceFieldReference(string name)
    {
        var fieldType = _program?.Contracts
            .FirstOrDefault(c => c.Name == _currentContractName)?.Fields
            .FirstOrDefault(f => f.Name == name)?.Type ?? new TypeDescriptor.Named("int");
        return new FieldReference(new TypeRef(_currentContractName ?? "TODO"), name, MapType(fieldType));
    }

    /// <summary>Looks up a field's type on the given contract/struct type name.</summary>
    private TypeRef FindFieldType(string typeName, string fieldName)
    {
        if (_program != null)
        {
            var contract = _program.Contracts.FirstOrDefault(c => c.Name == typeName);
            if (contract != null)
            {
                var f = contract.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (f != null) return MapType(f.Type);
            }
            var structDecl = _program.Structs.FirstOrDefault(s => s.Name == typeName);
            if (structDecl != null)
            {
                var f = structDecl.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (f != null) return MapType(f.Type);
            }
        }
        return TypeRef.Int32;
    }

    private void GenerateConstructor(ClassBuilder cb, ConstructorDeclaration ctor, string contractName)
    {
        _lastIsReturn = false;

        var savedVariableTypes = _variableTypes;
        var savedThisIndex = _thisArgIndex;
        var savedContract = _currentContractName;
        _variableTypes = new Dictionary<string, TypeDescriptor>();
        _thisArgIndex = 0;
        _currentContractName = contractName;

        var mb = cb.Constructor();

        foreach (var attr in ctor.Attributes)
        {
            mb.Attribute(attr.Name, attr.Arguments.ToArray());
        }

        mb.Parameter("this", new TypeRef("object"));
        _variableTypes["this"] = new TypeDescriptor.Named("object");

        var paramMap = new Dictionary<string, int>();
        paramMap["this"] = 0;
        for (int i = 0; i < ctor.Parameters.Count; i++)
        {
            var p = ctor.Parameters[i];
            mb.Parameter(p.Name, MapType(p.Type));
            paramMap[p.Name] = i + 1;
            _variableTypes[p.Name] = p.Type;
        }

        var ib = mb.Body();
        if (ctor.Body != null)
        {
            GenerateStatement(ib, ctor.Body, paramMap);
        }

        if (!_lastIsReturn)
        {
            ib.Ret();
        }

        ib.EndBody().EndMethod();

        _variableTypes = savedVariableTypes;
        _thisArgIndex = savedThisIndex;
        _currentContractName = savedContract;
    }

    private LambdaInfo GenerateLambda(LambdaExpression lambda, Dictionary<string, int> enclosingParamMap)
    {
        var info = new LambdaInfo { Name = $"__lambda_{_lambdaCounter++}" };

        // Parameter types: annotations win, else int (v1 default).
        for (int i = 0; i < lambda.Parameters.Count; i++)
        {
            string pt = i < lambda.ParameterTypes.Count ? lambda.ParameterTypes[i] : "";
            info.ParamTypes.Add(string.IsNullOrEmpty(pt) ? new TypeDescriptor.Named("int") : TypeDescriptor.Parse(pt));
        }

        // Return type: expression bodies are inferred, blocks default to int.
        info.ReturnType = lambda.BlockBody != null
            ? new TypeDescriptor.Named("int")
            : InferLambdaReturnType(lambda.Body);

        // Free-variable analysis: which enclosing vars does this lambda touch?
        var captures = AnalyzeCaptures(lambda);
        if (captures.Count > 0)
        {
            info.ClosureClass = $"__closure_{_closureCounter++}";
            info.Captures = captures;
            EnsureClosureClass(info);
        }

        // Synthesize the lambda method. Capturing lambdas take the closure as
        // their first parameter ("__closure"); the body reads/writes captured
        // vars through it as closure fields.
        var func = new FunctionDeclaration(info.Name, lambda.Line, lambda.Column) { IsStatic = true };
        if (info.HasCaptures)
            func.Parameters.Add(new Parameter("__closure", TypeDescriptor.Parse("object"), lambda.Line, lambda.Column));
        for (int i = 0; i < lambda.Parameters.Count; i++)
            func.Parameters.Add(new Parameter(lambda.Parameters[i], info.ParamTypes[i], lambda.Line, lambda.Column));

        if (lambda.BlockBody != null)
        {
            func.Body = lambda.BlockBody;
        }
        else
        {
            var block = new Contract.Compiler.AST.BlockStatement(lambda.Line, lambda.Column);
            block.Statements.Add(new ReturnStatement(lambda.Body, lambda.Line, lambda.Column));
            func.Body = block;
        }

        _program!.Functions.Add(func);
        _lambdaInfos[info.Name] = info;
        return info;
    }

    private static TypeDescriptor InferLambdaReturnType(Expression body)
        => body switch
        {
            LiteralExpression lit => lit.Value switch
            {
                string => new TypeDescriptor.Named("string"),
                bool => new TypeDescriptor.Named("bool"),
                double => new TypeDescriptor.Named("double"),
                _ => new TypeDescriptor.Named("int")
            },
            _ => new TypeDescriptor.Named("int")
        };

    /// <summary>
    /// Collects free variables in the lambda body: identifiers that are not
    /// lambda params (or shadowed by nested lambda params / block locals) and
    /// whose type is known in the enclosing scope. These become closure fields.
    /// </summary>
    private List<(string Name, TypeDescriptor Type)> AnalyzeCaptures(LambdaExpression lambda)
    {
        var result = new List<(string, TypeDescriptor)>();
        var seen = new HashSet<string>();
        var shadowed = new HashSet<string>(lambda.Parameters) { "__closure" };

        void Collect(Expression e, HashSet<string> sh)
        {
            switch (e)
            {
                case IdentifierExpression id:
                    if (sh.Contains(id.Name) || seen.Contains(id.Name)) return;
                    if (_variableTypes.TryGetValue(id.Name, out var t))
                    {
                        result.Add((id.Name, t));
                        seen.Add(id.Name);
                    }
                    break;
                case LiteralExpression:
                    break;
                case BinaryExpression b: Collect(b.Left, sh); Collect(b.Right, sh); break;
                case UnaryExpression u: Collect(u.Operand, sh); break;
                case CallExpression c:
                    Collect(c.Callee, sh);
                    foreach (var a in c.Arguments) Collect(a, sh);
                    break;
                case MemberExpression m: Collect(m.Object, sh); break;
                case IndexExpression ix: Collect(ix.Target, sh); Collect(ix.Index, sh); break;
                case NewExpression ne: if (ne.Size != null) Collect(ne.Size, sh); break;
                case ArrayLiteralExpression al: foreach (var el in al.Elements) Collect(el, sh); break;
                case PipeExpression p: Collect(p.Left, sh); Collect(p.Right, sh); break;
                case LambdaExpression inner:
                    var innerShadow = new HashSet<string>(sh);
                    foreach (var p2 in inner.Parameters) innerShadow.Add(p2);
                    if (inner.BlockBody != null)
                    {
                        foreach (var s in inner.BlockBody.Statements) CollectStmt(s, innerShadow);
                    }
                    else if (inner.Body != null)
                    {
                        Collect(inner.Body, innerShadow);
                    }
                    break;
                case ScopedAccessExpression:
                    break;
            }
        }

        void CollectStmt(Contract.Compiler.AST.Statement s, HashSet<string> sh)
        {
            switch (s)
            {
                case Contract.Compiler.AST.BlockStatement b:
                    foreach (var st in b.Statements) CollectStmt(st, sh);
                    break;
                case VariableDeclaration v:
                    if (v.Initializer != null) Collect(v.Initializer, sh);
                    sh.Add(v.Name); // block-local shadows a capture of the same name
                    break;
                case Contract.Compiler.AST.ExpressionStatement es: Collect(es.Expression, sh); break;
                case Contract.Compiler.AST.IfStatement i:
                    Collect(i.Condition, sh); CollectStmt(i.ThenBranch, sh);
                    if (i.ElseBranch != null) CollectStmt(i.ElseBranch, sh);
                    break;
                case Contract.Compiler.AST.WhileStatement w: Collect(w.Condition, sh); CollectStmt(w.Body, sh); break;
                case Contract.Compiler.AST.ForStatement f:
                    if (f.Initializer != null) CollectStmt(f.Initializer, sh);
                    if (f.Condition != null) Collect(f.Condition, sh);
                    CollectStmt(f.Body, sh);
                    if (f.Update != null) Collect(f.Update, sh);
                    break;
                case Contract.Compiler.AST.ReturnStatement r: if (r.Value != null) Collect(r.Value, sh); break;
                case Contract.Compiler.AST.SwitchStatement sw:
                    Collect(sw.Expression, sh);
                    foreach (var c2 in sw.Cases)
                        foreach (var st in c2.Statements) CollectStmt(st, sh);
                    break;
                case Contract.Compiler.AST.BreakStatement:
                case Contract.Compiler.AST.ContinueStatement:
                    break;
            }
        }

        if (lambda.BlockBody != null)
        {
            foreach (var s in lambda.BlockBody.Statements) CollectStmt(s, shadowed);
        }
        else if (lambda.Body != null)
        {
            Collect(lambda.Body, shadowed);
        }
        return result;
    }

    private void EnsureClosureClass(LambdaInfo info)
    {
        if (_builder == null || info.ClosureClass == null) return;
        var classBuilder = _builder.Class(info.ClosureClass);
        foreach (var (cname, ctype) in info.Captures)
            classBuilder.Field(cname, MapType(ctype));
        classBuilder.EndClass();
    }

    /// <summary>
    /// Emits a delegate value for a lambda. Non-capturing: newobj Delegate +
    /// store target. Capturing: allocate the delegate first, then the closure
    /// object (fields for each captured var, read from the enclosing scope),
    /// store the closure into the delegate, then the target name. The delegate
    /// is left on the stack as the value.
    /// </summary>
    private void GenerateLambdaValue(InstructionBuilder ib, LambdaInfo info, Dictionary<string, int> enclosingParamMap)
    {
        EnsureDelegateClass();

        if (!info.HasCaptures)
        {
            ib.Newobj(new TypeRef(DelegateClassName));
            ib.Dup();
            ib.Ldstr($"Global.{info.Name}");
            ib.Stfld(new FieldReference(new TypeRef(DelegateClassName), DelegateTargetField, TypeRef.String));
            return;
        }

        // Stack convention: stfld pops [value, object], so push object first.
        // The delegate must survive all stores, so keep a spare copy around.
        ib.Newobj(new TypeRef(DelegateClassName));          // [d]
        ib.Dup();                                           // [d, d]
        ib.Newobj(new TypeRef(info.ClosureClass!));         // [d, d, c]
        foreach (var (cname, ctype) in info.Captures)
        {
            ib.Dup();                                       // [d, d, c, c]
            if (enclosingParamMap.TryGetValue(cname, out int cidx)) ib.Ldarg(cidx);
            else ib.Ldloc(cname);                           // [d, d, c, c, v]
            ib.Stfld(new FieldReference(new TypeRef(info.ClosureClass!), cname, MapType(ctype))); // [d, d, c]
        }
        ib.Stfld(new FieldReference(new TypeRef(DelegateClassName), DelegateClosureField, TypeRef.Object)); // [d]
        ib.Dup();                                           // [d, d]
        ib.Ldstr($"Global.{info.Name}");                    // [d, d, s]
        ib.Stfld(new FieldReference(new TypeRef(DelegateClassName), DelegateTargetField, TypeRef.String));  // [d]
    }

    /// <summary>
    /// Emits the compiler-generated Delegate class (fields 'target: string' and
    /// 'closure: object') into the module once, so newobj/stfld/callvirt against
    /// it resolve.
    /// </summary>
    private void EnsureDelegateClass()
    {
        if (_delegateClassEmitted) return;
        _delegateClassEmitted = true;
        if (_builder == null) return;
        var classBuilder = _builder.Class(DelegateClassName);
        classBuilder.Field(DelegateTargetField, TypeRef.String);
        classBuilder.Field(DelegateClosureField, TypeRef.Object);
        classBuilder.EndClass();
    }

    private void GenerateStatement(InstructionBuilder ib, Contract.Compiler.AST.Statement stmt, Dictionary<string, int> paramMap)
    {
        string? source = GetSourceLine(stmt.Line);
        ib.SetLocation(stmt.Line, stmt.Column, source);
        _lastIsReturn = false;

        switch (stmt)
        {
            case Contract.Compiler.AST.BlockStatement block:
                foreach (var s in block.Statements)
                    GenerateStatement(ib, s, paramMap);
                break;

            case Contract.Compiler.AST.VariableDeclaration v:
                ib.Local(v.Name, MapType(v.Type));
                _variableTypes[v.Name] = v.Type;
                _lambdaBodyLocals?.Add(v.Name);
                if (v.Type is TypeDescriptor.Function fnLocalType)
                    _functionTypedLocals[v.Name] = fnLocalType;
                if (v.Initializer != null)
                {
                    if (v.Initializer is LambdaExpression lambda)
                    {
                        // A lambda assigned to a variable is a value now: build the
                        // delegate object. The direct-call fast path below still
                        // resolves through _lambdaVariableMap for `inc(5)`.
                        var info = GenerateLambda(lambda, paramMap);
                        _lambdaVariableMap[v.Name] = info;
                        GenerateLambdaValue(ib, info, paramMap);
                        ib.Stloc(v.Name);
                    }
                    else
                    {
                        // Array-literal element-type hint: used for empty literals
                        // like 'let x: string[] = []' where no element type is inferable.
                        var prevHint = _arrayElementTypeHint;
                        if (v.Initializer is Contract.Compiler.AST.ArrayLiteralExpression)
                            _arrayElementTypeHint = v.Type;

                        GenerateExpression(ib, v.Initializer, paramMap);
                        ib.Stloc(v.Name);

                        _arrayElementTypeHint = prevHint;
                    }
                }
                break;

            case Contract.Compiler.AST.ExpressionStatement exprStmt:
                GenerateExpression(ib, exprStmt.Expression, paramMap);
                // Every expression leaves its result on the stack (assignments and
                // lambdas now push values, C#-style). A statement discards it.
                ib.Pop();
                break;

            case Contract.Compiler.AST.IfStatement ifStmt:
                GenerateExpression(ib, ifStmt.Condition, paramMap);
                ib.If("stack", 
                    then => GenerateStatement(then, ifStmt.ThenBranch, paramMap),
                    ifStmt.ElseBranch != null ? els => GenerateStatement(els, ifStmt.ElseBranch, paramMap) : null
                );
                break;

            case Contract.Compiler.AST.WhileStatement whileStmt:
                GenerateExpression(ib, whileStmt.Condition, paramMap);
                _loopContinueEmitters.Push(body => GenerateExpression(body, whileStmt.Condition, paramMap));
                ib.While("stack", body =>
                {
                    // Runtime 'while (stack)' convention: the loop dups the
                    // condition, so the body starts with TWO copies. Pop the
                    // extra, run the body, then recompute the condition for
                    // the next iteration.
                    body.Pop();
                    GenerateStatement(body, whileStmt.Body, paramMap);
                    GenerateExpression(body, whileStmt.Condition, paramMap);
                });
                _loopContinueEmitters.Pop();
                // The loop leaves one copy of the final condition on the stack;
                // consume it so the loop is stack-neutral.
                ib.Pop();
                break;

            case Contract.Compiler.AST.ForStatement forStmt:
                if (forStmt.Initializer != null)
                    GenerateStatement(ib, forStmt.Initializer, paramMap);
                if (forStmt.Condition != null)
                    GenerateExpression(ib, forStmt.Condition, paramMap);
                else
                    ib.LdcI4(1);
                _loopContinueEmitters.Push(body =>
                {
                    if (forStmt.Update != null)
                        GenerateExpression(body, forStmt.Update, paramMap);
                    if (forStmt.Condition != null)
                        GenerateExpression(body, forStmt.Condition, paramMap);
                    else
                        body.LdcI4(1);
                });
                ib.While("stack", body =>
                {
                    body.Pop();
                    GenerateStatement(body, forStmt.Body, paramMap);
                    if (forStmt.Update != null)
                        GenerateExpression(body, forStmt.Update, paramMap);
                    if (forStmt.Condition != null)
                        GenerateExpression(body, forStmt.Condition, paramMap);
                    else
                        body.LdcI4(1);
                });
                _loopContinueEmitters.Pop();
                ib.Pop();
                break;

            case Contract.Compiler.AST.BreakStatement:
                // Leave a value for the loop's trailing pop so the stack stays
                // balanced whether the loop exits via break or its condition.
                ib.LdcI4(0);
                ib.Break();
                break;

            case Contract.Compiler.AST.ContinueStatement:
                if (_loopContinueEmitters.Count > 0)
                    _loopContinueEmitters.Peek()(ib);
                ib.Continue();
                break;

            case Contract.Compiler.AST.ReturnStatement ret:
                if (ret.Value != null)
                    GenerateExpression(ib, ret.Value, paramMap);
                ib.Ret();
                _lastIsReturn = true;
                break;

            case Contract.Compiler.AST.SwitchStatement switchStmt:
                GenerateExpression(ib, switchStmt.Expression, paramMap);
                ib.Switch("stack", cases => {
                    foreach (var c in switchStmt.Cases)
                    {
                        if (c.StringValue != null)
                        {
                            cases.Case(c.StringValue, body => {
                                foreach (var s in c.Statements)
                                    GenerateStatement(body, s, paramMap);
                            });
                        }
                        else
                        {
                            cases.Case(c.Value, body => {
                                foreach (var s in c.Statements)
                                    GenerateStatement(body, s, paramMap);
                            });
                        }
                    }
                });
                break;
        }
    }

    private bool IsComparison(string op) => op is "==" or "!=" or ">" or ">=" or "<" or "<=";

    private void GenerateExpression(InstructionBuilder ib, Expression expr, Dictionary<string, int> paramMap)
    {
        string? source = GetSourceLine(expr.Line);
        ib.SetLocation(expr.Line, expr.Column, source);

        switch (expr)
        {
            case LiteralExpression lit:
                if (lit.Value is int i) ib.LdcI4(i);
                else if (lit.Value is string s) ib.Ldstr(s);
                else if (lit.Value is bool boolVal) ib.LdcI4(boolVal ? 1 : 0);
                else if (lit.Value is double dVal) ib.LdcR8(dVal);
                else if (lit.Value == null) ib.Ldnull();
                break;

            case UnaryExpression unaryExpr:
                GenerateExpression(ib, unaryExpr.Operand, paramMap);
                if (unaryExpr.Operator == "-") ib.Neg();
                else if (unaryExpr.Operator == "!") ib.Not();
                break;

            case IdentifierExpression id:
                if (id.Name == "this" && _thisArgIndex != null)
                {
                    ib.Ldarg(_thisArgIndex.Value);
                    break;
                }
                if (IsInstanceField(id.Name))
                {
                    // Bare field access in an instance method.
                    ib.Ldarg(_thisArgIndex!.Value);
                    ib.Ldfld(InstanceFieldReference(id.Name));
                    break;
                }
                if (IsCaptured(id.Name))
                {
                    // Captured var: read through the closure object's field.
                    ib.Ldarg(_closureArgIndex!.Value);
                    ib.Ldfld(CaptureFieldReference(id.Name));
                    break;
                }
                if (paramMap.TryGetValue(id.Name, out int argIdx)) ib.Ldarg(argIdx);
                else ib.Ldloc(id.Name);
                break;

            case BinaryExpression bin:
                if (bin.Operator == "=")
                {
                    if (bin.Left is IdentifierExpression capturedTarget && IsCaptured(capturedTarget.Name))
                    {
                        // Captured var write: store through the closure object.
                        // stfld pops [value, object], so dup the object first.
                        ib.Ldarg(_closureArgIndex!.Value);       // object
                        ib.Dup();                                // object, object
                        GenerateExpression(ib, bin.Right, paramMap); // object, object, value
                        ib.Stfld(CaptureFieldReference(capturedTarget.Name)); // object (leftover)
                        return;
                    }
                    if (bin.Left is IdentifierExpression fieldTarget && IsInstanceField(fieldTarget.Name))
                    {
                        // Bare instance field write: store through this.
                        ib.Ldarg(_thisArgIndex!.Value);
                        ib.Dup();
                        GenerateExpression(ib, bin.Right, paramMap);
                        ib.Stfld(InstanceFieldReference(fieldTarget.Name));
                        return;
                    }
                    if (bin.Left is IndexExpression indexTarget)
                    {
                        GenerateExpression(ib, indexTarget.Target, paramMap);
                        GenerateExpression(ib, indexTarget.Index, paramMap);
                        GenerateExpression(ib, bin.Right, paramMap);
                        ib.Dup();   // leave the assigned value on the stack (C#-style)
                        ib.Stelem();
                        return;
                    }
                    else if (bin.Left is MemberExpression memTarget)
                    {
                        GenerateExpression(ib, memTarget.Object, paramMap);
                        ib.Dup();   // keep the object for the store (stfld pops value, object)
                        GenerateExpression(ib, bin.Right, paramMap);
                        var targetName = memTarget.Object is IdentifierExpression targetId ? targetId.Name : "TODO_DYNAMIC_TYPE";
                        if (targetName == "this" && _currentContractName != null)
                            targetName = _currentContractName;
                        ib.Stfld(new FieldReference(new TypeRef(targetName), memTarget.Property, FindFieldType(targetName, memTarget.Property)));
                        return;
                    }

                    GenerateExpression(ib, bin.Right, paramMap);
                    if (bin.Left is IdentifierExpression target)
                    {
                        ib.Dup();   // leave the assigned value on the stack
                        if (paramMap.TryGetValue(target.Name, out int idx)) ib.Starg(idx);
                        else ib.Stloc(target.Name);
                    }
                    return;
                }

                if (bin.Operator is "+=" or "-=" or "*=" or "/=" or "%=")
                {
                    string op = bin.Operator[..^1];

                    if (bin.Left is IdentifierExpression capCompoundTarget && IsCaptured(capCompoundTarget.Name))
                    {
                        var cfieldRef = CaptureFieldReference(capCompoundTarget.Name);
                        // dup the closure twice: one for ldfld, one left after stfld.
                        ib.Ldarg(_closureArgIndex!.Value);   // [c]
                        ib.Dup();                            // [c, c]
                        ib.Dup();                            // [c, c, c]
                        ib.Ldfld(cfieldRef);                 // [c, c, val]
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin); // [c, c, newval]
                        ib.Stfld(cfieldRef);                 // [c]
                        return;
                    }
                    if (bin.Left is IdentifierExpression fieldCompound && IsInstanceField(fieldCompound.Name))
                    {
                        var fref = InstanceFieldReference(fieldCompound.Name);
                        ib.Ldarg(_thisArgIndex!.Value);
                        ib.Dup();
                        ib.Dup();
                        ib.Ldfld(fref);
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin);
                        ib.Stfld(fref);
                        return;
                    }
                    if (bin.Left is IdentifierExpression compoundTarget)
                    {
                        if (paramMap.TryGetValue(compoundTarget.Name, out int compoundArgIdx)) ib.Ldarg(compoundArgIdx);
                        else ib.Ldloc(compoundTarget.Name);
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin);
                        ib.Dup();   // leave the result on the stack
                        if (paramMap.TryGetValue(compoundTarget.Name, out int compoundStoreIdx)) ib.Starg(compoundStoreIdx);
                        else ib.Stloc(compoundTarget.Name);
                    }
                    else if (bin.Left is MemberExpression compoundMem)
                    {
                        GenerateExpression(ib, compoundMem.Object, paramMap);
                        ib.Dup(); // keep one copy for the store
                        ib.Dup(); // and one left over after stfld
                        var compoundMemObjName = compoundMem.Object is IdentifierExpression compoundMemId ? compoundMemId.Name : "TODO_DYNAMIC_TYPE";
                        if (compoundMemObjName == "this" && _currentContractName != null)
                            compoundMemObjName = _currentContractName;
                        var fieldRef = new FieldReference(new TypeRef(compoundMemObjName), compoundMem.Property, FindFieldType(compoundMemObjName, compoundMem.Property));
                        ib.Ldfld(fieldRef);
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin);
                        ib.Stfld(fieldRef);
                    }
                    else if (bin.Left is IndexExpression compoundIndex)
                    {
                        // a[i] = a[i] op rhs (target/index evaluated twice)
                        GenerateExpression(ib, compoundIndex.Target, paramMap);
                        GenerateExpression(ib, compoundIndex.Index, paramMap);
                        ib.Ldelem();
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin);
                        ib.Dup();   // leave the result on the stack
                        GenerateExpression(ib, compoundIndex.Target, paramMap);
                        GenerateExpression(ib, compoundIndex.Index, paramMap);
                        ib.Stelem();
                    }
                    return;
                }

                if (bin.Operator == "+" && bin.ResolvedType?.IsString == true)
                {
                    GenerateExpression(ib, bin.Left, paramMap);
                    GenerateExpression(ib, bin.Right, paramMap);
                    ib.Call(StringConcatReference);
                    return;
                }

                GenerateExpression(ib, bin.Left, paramMap);
                GenerateExpression(ib, bin.Right, paramMap);
                switch (bin.Operator)
                {
                    case "+": ib.Add(); break;
                    case "-": ib.Sub(); break;
                    case "*": ib.Mul(); break;
                    case "/": ib.Div(); break;
                    case "%": ib.Rem(); break;
                    case "==": ib.Ceq(); break;
                    case "!=": ib.Cne(); break;
                    case ">": ib.Cgt(); break;
                    case ">=": ib.Cge(); break;
                    case "<": ib.Clt(); break;
                    case "<=": ib.Cle(); break;
                    case "&&": ib.And(); break;
                    case "||": ib.Or(); break;
                }
                break;

            case CallExpression call:
                // Push arguments to stack
                foreach (var arg in call.Arguments)
                    GenerateExpression(ib, arg, paramMap);

                if (call.Symbol is ExternalMethod em)
                {
                    var target = new MethodReference(
                        new TypeRef(em.ClassName),
                        em.MethodName,
                        MapTypeFromSystemType(em.Info.ReturnType),
                        em.Info.GetParameters().Select(p => MapTypeFromSystemType(p.ParameterType)).ToList()
                    );
                    ib.Call(target);
                }
                else if (call.Symbol is FunctionDeclaration instanceFunc && instanceFunc.IsInstance)
                {
                    // Instance method call: c.method(args). Push the receiver
                    // (as arg 0 / this), then the args, then the call.
                    if (call.Callee is MemberExpression imem)
                    {
                        GenerateExpression(ib, imem.Object, paramMap);
                    }
                    else if (call.Callee is IdentifierExpression selfIdent && selfIdent.Name == "this")
                    {
                        ib.Ldarg(_thisArgIndex!.Value);
                    }
                    var returnType = instanceFunc.ReturnType != null
                        ? MapType(instanceFunc.ReturnType)
                        : TypeRef.Int32;
                    var paramTypes = instanceFunc.Parameters.Select(p => MapType(p.Type)).ToList();
                    ib.Call(new MethodReference(new TypeRef(instanceFunc.ContractName ?? "TODO"), instanceFunc.Name, returnType, paramTypes));
                }
                else if (call.Symbol is FunctionDeclaration staticFunc && staticFunc.IsStatic)
                {
                    // Static method on a contract: Contract::Method(...) or
                    // Contract.Method(...). No receiver to push.
                    var returnType = staticFunc.ReturnType != null
                        ? MapType(staticFunc.ReturnType)
                        : TypeRef.Int32;
                    var paramTypes = staticFunc.Parameters.Select(p => MapType(p.Type)).ToList();
                    ib.Call(new MethodReference(new TypeRef(staticFunc.ContractName ?? "Global"), staticFunc.Name, returnType, paramTypes));
                }
                else if (call.Callee is IdentifierExpression calleeIdent)
                {
                    if (_lambdaVariableMap.TryGetValue(calleeIdent.Name, out var lambdaInfo))
                    {
                        if (lambdaInfo.HasCaptures)
                        {
                            // A capturing lambda has the closure bound inside the
                            // delegate value, so it must be invoked through it.
                            GenerateExpression(ib, calleeIdent, paramMap);
                            var pt = lambdaInfo.ParamTypes.Select(MapType).ToList();
                            ib.Callvirt(new MethodReference(new TypeRef(DelegateClassName), DelegateInvokeMethod, MapType(lambdaInfo.ReturnType), pt));
                        }
                        else
                        {
                            // Direct call to a lambda value whose target is known:
                            // plain call, no delegate object involved (fast path).
                            var target = new MethodReference(new TypeRef("Global"), lambdaInfo.Name, TypeRef.Int32, call.Arguments.Select(_ => TypeRef.Int32).ToList());
                            ib.Call(target);
                        }
                    }
                    else if (_functionTypedParams.TryGetValue(calleeIdent.Name, out var fnType))
                    {
                        // Indirect call through a function-typed value: push the
                        // delegate receiver, then callvirt Delegate.Invoke (C#-style).
                        GenerateExpression(ib, calleeIdent, paramMap);
                        var paramTypes = fnType.Parameters.Select(MapType).ToList();
                        var returnType = MapType(fnType.Return);
                        ib.Callvirt(new MethodReference(new TypeRef(DelegateClassName), DelegateInvokeMethod, returnType, paramTypes));
                    }
                    else if (_functionTypedLocals.TryGetValue(calleeIdent.Name, out var fnLocalType))
                    {
                        // A delegate stored in a local (e.g. returned from a call).
                        GenerateExpression(ib, calleeIdent, paramMap);
                        var paramTypes = fnLocalType.Parameters.Select(MapType).ToList();
                        var returnType = MapType(fnLocalType.Return);
                        ib.Callvirt(new MethodReference(new TypeRef(DelegateClassName), DelegateInvokeMethod, returnType, paramTypes));
                    }
                    else
                    {
                        var target = ResolveFunctionReference("this", calleeIdent.Name, call.Arguments.Count);
                        ib.Call(target);
                    }
                }
                break;
                
            case NewExpression newExpr:
                if (newExpr.Size != null)
                {
                    GenerateExpression(ib, newExpr.Size, paramMap);
                    ib.Newarr(MapType(newExpr.TypeName));
                }
                else
                {
                    ib.Newobj(new TypeRef(newExpr.TypeName));
                }
                break;

            case ArrayLiteralExpression arrLit:
                ib.LdcI4(arrLit.Elements.Count);
                ib.Newarr(ElementTypeOfArrayLiteral(arrLit));
                for (int elemIdx = 0; elemIdx < arrLit.Elements.Count; elemIdx++)
                {
                    ib.Dup();
                    ib.LdcI4(elemIdx);
                    GenerateExpression(ib, arrLit.Elements[elemIdx], paramMap);
                    ib.Stelem();
                }
                break;
                
            case MemberExpression mem:
                GenerateExpression(ib, mem.Object, paramMap);
                if (mem.Property == "Length")
                {
                    // Array length
                    ib.Ldlen();
                }
                else
                {
                    var memObjName = mem.Object is IdentifierExpression memObjId ? memObjId.Name : "TODO_DYNAMIC_TYPE";
                    // `this.field` resolves against the declaring contract so the
                    // runtime's Type.field field map matches.
                    if (memObjName == "this" && _currentContractName != null)
                        memObjName = _currentContractName;
                    var ftype = FindFieldType(memObjName, mem.Property);
                    ib.Ldfld(new FieldReference(new TypeRef(memObjName), mem.Property, ftype));
                }
                break;

            case IndexExpression indexExpr:
                GenerateExpression(ib, indexExpr.Target, paramMap);
                GenerateExpression(ib, indexExpr.Index, paramMap);
                ib.Ldelem();
                break;

            case LambdaExpression lambda:
                // A lambda in expression position (e.g. passed directly) is a value.
                GenerateLambdaValue(ib, GenerateLambda(lambda, paramMap), paramMap);
                break;

            case PipeExpression pipe:
                if (pipe.Right is IdentifierExpression ident)
                {
                    GenerateExpression(ib, pipe.Left, paramMap);
                    if (_lambdaVariableMap.TryGetValue(ident.Name, out var lambdaInfo))
                    {
                        if (lambdaInfo.HasCaptures)
                        {
                            GenerateExpression(ib, ident, paramMap);
                            var pt = lambdaInfo.ParamTypes.Select(MapType).ToList();
                            ib.Callvirt(new MethodReference(new TypeRef(DelegateClassName), DelegateInvokeMethod, MapType(lambdaInfo.ReturnType), pt));
                        }
                        else
                        {
                            var target = new MethodReference(new TypeRef("Global"), lambdaInfo.Name, TypeRef.Int32, new List<TypeRef> { TypeRef.Int32 });
                            ib.Call(target);
                        }
                    }
                    else
                    {
                        var target = ResolveFunctionReference("this", ident.Name, 1);
                        ib.Call(target);
                    }
                }
                else if (pipe.Right is LambdaExpression lambdaExpr)
                {
                    GenerateExpression(ib, pipe.Left, paramMap);
                    var info = GenerateLambda(lambdaExpr, paramMap);
                    if (info.HasCaptures)
                    {
                        GenerateLambdaValue(ib, info, paramMap);
                        var pt = info.ParamTypes.Select(MapType).ToList();
                        ib.Callvirt(new MethodReference(new TypeRef(DelegateClassName), DelegateInvokeMethod, MapType(info.ReturnType), pt));
                    }
                    else
                    {
                        var target = new MethodReference(new TypeRef("Global"), info.Name, TypeRef.Int32, new List<TypeRef> { TypeRef.Int32 });
                        ib.Call(target);
                    }
                }
                else
                {
                    throw new NotSupportedException("Piping to complex expressions is not yet supported.");
                }
                break;
        }
    }

    private TypeRef MapType(string type) => MapType(TypeDescriptor.Parse(type));

    private TypeRef MapType(TypeDescriptor type) => type switch
    {
        TypeDescriptor.Named n => MapNamedType(n.Name),
        TypeDescriptor.ArrayOf a => new TypeRef($"{a.Element}[]"),
        // Function types are represented as object handles in the wire model
        // (a delegate IS an object).
        TypeDescriptor.Function => TypeRef.Object,
        // Generic instances are type-erased: the runtime sees the object-backed
        // collection class (List/Dict), so the wire type is object.
        TypeDescriptor.GenericInstance => TypeRef.Object,
        _ => TypeRef.Int32
    };

    /// <summary>True when 'name' is a captured variable of the active lambda body.</summary>
    private bool IsCaptured(string name)
        => _captureNames != null
           && _captureNames.Contains(name)
           && (_lambdaBodyLocals == null || !_lambdaBodyLocals.Contains(name));

    /// <summary>A field reference on the active closure class for a captured variable.</summary>
    private FieldReference CaptureFieldReference(string name)
    {
        var fieldType = _variableTypes.TryGetValue(name, out var t) ? MapType(t) : TypeRef.Int32;
        return new FieldReference(new TypeRef(_closureClass!), name, fieldType);
    }

    private TypeRef MapNamedType(string type) => string.IsNullOrEmpty(type) ? TypeRef.Int32 : type.ToLower() switch {
        "string" => TypeRef.String,
        "void" => TypeRef.Void,
        "bool" => TypeRef.Bool,
        "int" => TypeRef.Int32,
        "int64" => TypeRef.Int64,
        "long" => TypeRef.Int64,
        "object" => TypeRef.Object,
        "double" => TypeRef.Float64,
        "float" => TypeRef.Float32,
        _ => new TypeRef(type)
    };

    /// <summary>
    /// Resolves the element type for a newarr from an array literal. Prefers the
    /// analyzer-resolved element type, then the declaring variable's array type
    /// (for empty literals), then the first element's literal value.
    /// </summary>
    private TypeRef ElementTypeOfArrayLiteral(ArrayLiteralExpression arrLit)
    {
        if (arrLit.ElementType != null)
        {
            return MapType(arrLit.ElementType);
        }

        if (_arrayElementTypeHint is TypeDescriptor.ArrayOf arrHint)
        {
            return MapType(arrHint.Element);
        }

        if (arrLit.Elements.Count > 0 && arrLit.Elements[0] is LiteralExpression firstLit)
        {
            return firstLit.Value switch
            {
                string => TypeRef.String,
                bool => TypeRef.Bool,
                double => TypeRef.Float64,
                _ => TypeRef.Int32
            };
        }

        return TypeRef.Int32;
    }

    private TypeDescriptor? _arrayElementTypeHint;

    private TypeRef MapTypeFromSystemType(System.Type t)
    {
        if (t.IsArray)
        {
            var elementType = t.GetElementType();
            if (elementType != null)
            {
                return new TypeRef(MapTypeFromSystemType(elementType).Name + "[]");
            }
            return new TypeRef("int[]");
        }
        return t == typeof(string) ? TypeRef.String
            : (t == typeof(object) ? TypeRef.Object
            : (t == typeof(void) ? TypeRef.Void
            : (t == typeof(bool) ? TypeRef.Bool
            : (t == typeof(long) ? TypeRef.Int64
            : (t == typeof(double) ? TypeRef.Float64
            : (t == typeof(float) ? TypeRef.Float32
            : TypeRef.Int32))))));
    }

    private void EmitArithmeticOp(InstructionBuilder ib, string op)
    {
        switch (op)
        {
            case "+": ib.Add(); break;
            case "-": ib.Sub(); break;
            case "*": ib.Mul(); break;
            case "/": ib.Div(); break;
            case "%": ib.Rem(); break;
        }
    }

    /// <summary>
    /// Emits the operation for a compound assignment. String concatenation (op "+"
    /// where the expression resolved to a string) lowers to String.Concat instead of add.
    /// </summary>
    private void EmitArithmeticOrConcat(InstructionBuilder ib, string op, Contract.Compiler.AST.BinaryExpression bin)
    {
        if (op == "+" && bin.ResolvedType?.IsString == true)
        {
            ib.Call(StringConcatReference);
        }
        else
        {
            EmitArithmeticOp(ib, op);
        }
    }

    private static readonly MethodReference StringConcatReference = new(
        new TypeRef("String"),
        "Concat",
        TypeRef.String,
        new List<TypeRef> { TypeRef.String, TypeRef.String }
    );

    private MethodReference ResolveFunctionReference(string fallbackDeclaringType, string name, int argCount)
    {
        var func = FindFunction(name);
        var paramTypes = new List<TypeRef>();
        if (func != null)
        {
            foreach (var p in func.Parameters)
                paramTypes.Add(MapType(p.Type));
        }
        while (paramTypes.Count < argCount) paramTypes.Add(TypeRef.Int32);
        if (paramTypes.Count > argCount) paramTypes = paramTypes.Take(argCount).ToList();

        var returnType = func != null && func.ReturnType != null
            ? MapType(func.ReturnType)
            : TypeRef.Int32;

        // The runtime's FunctionMap keys are qualified names like "Program.sumTo".
        // Emit the real declaring contract so module calls resolve; lambdas and
        // module-level functions live in the generated Global class.
        var declaringType = func?.ContractName != null
            ? func.ContractName
            : func != null ? "Global" : fallbackDeclaringType;

        return new MethodReference(new TypeRef(declaringType), name, returnType, paramTypes);
    }

    private FunctionDeclaration? FindFunction(string name)
    {
        if (_program == null) return null;
        foreach (var func in _program.Functions)
            if (func.Name == name) return func;
        foreach (var contract in _program.Contracts)
            foreach (var member in contract.Members)
                if (member is FunctionDeclaration f && f.Name == name) return f;
        return null;
    }

    private string? GetSourceLine(int line) 
        => (line > 0 && line <= _sourceLines.Length) ? _sourceLines[line - 1] : null;
}
