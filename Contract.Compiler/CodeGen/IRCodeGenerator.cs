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
    private Dictionary<string, string> _lambdaVariableMap = new();
    private Program? _program;

    public IRCodeGenerator(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void WriteToFile(string outputPath)
    {
        if (_builder == null) return;
        File.WriteAllText(outputPath, _builder.Build().Serialize().DumpToIRCode());
    }

    public void Generate(Program program)
    {
        _program = program;
        _sourceLines = _diagnostics.SourceCode?.Split('\n') ?? Array.Empty<string>();
        _builder = new IRBuilder(program.Contracts.Count > 0 ? program.Contracts[0].Name : "Program");

        // Custom types from "Types { ... }" blocks become structs.
        foreach (var typesDecl in program.Types)
        {
            foreach (var definition in typesDecl.Definitions)
            {
                var structBuilder = _builder.Struct(definition.Name);
                foreach (var field in definition.Fields)
                {
                    structBuilder.Field(field.Name, MapType(field.Type));
                }
                structBuilder.EndStruct();
            }
        }

        // Top-level structs.
        foreach (var structDecl in program.Structs)
        {
            var structBuilder = _builder.Struct(structDecl.Name);
            foreach (var field in structDecl.Fields)
            {
                structBuilder.Field(field.Name, MapType(field.Type));
            }
            structBuilder.EndStruct();
        }

        foreach (var cls in program.Contracts)
        {
            var classBuilder = _builder.Class(cls.Name);

            foreach (var ctor in cls.Constructors)
            {
                GenerateConstructor(classBuilder, ctor);
            }

            foreach (var member in cls.Members)
            {
                if (member is FunctionDeclaration func)
                    GenerateFunction(classBuilder, func);
                else if (member is StructDeclaration structDecl)
                {
                    var structBuilder = _builder.Struct(structDecl.Name);
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
        // Explicit return type if declared; otherwise guess: Main is void, others are int32 by default in v1
        var returnType = func.ReturnType != null
            ? MapType(func.ReturnType)
            : func.Name == "Main" ? TypeRef.Void : TypeRef.Int32;
        var mb = cb.Method(func.Name, returnType);
        
        if (func.IsStatic) mb.Static();
        
        mb.Access(func.Access switch {
            ContractAccess.Public => IRAccess.Public,
            ContractAccess.Private => IRAccess.Private,
            ContractAccess.Protected => IRAccess.Protected,
            ContractAccess.Internal => IRAccess.Internal,
            _ => IRAccess.Public
        });

        var paramMap = new Dictionary<string, int>();
        for (int i = 0; i < func.Parameters.Count; i++)
        {
            var p = func.Parameters[i];
            mb.Parameter(p.Name, MapType(p.Type));
            paramMap[p.Name] = i;
        }

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
    }

    private void GenerateConstructor(ClassBuilder cb, ConstructorDeclaration ctor)
    {
        _lastIsReturn = false;

        var mb = cb.Constructor();

        var paramMap = new Dictionary<string, int>();
        for (int i = 0; i < ctor.Parameters.Count; i++)
        {
            var p = ctor.Parameters[i];
            mb.Parameter(p.Name, MapType(p.Type));
            paramMap[p.Name] = i;
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
    }

    private string GenerateLambda(LambdaExpression lambda)
    {
        string name = $"__lambda_{_lambdaCounter++}";
        var func = new FunctionDeclaration(name, lambda.Line, lambda.Column) { IsStatic = true };
        foreach (var param in lambda.Parameters)
            func.Parameters.Add(new Parameter(param, TypeDescriptor.Parse("Int"), lambda.Line, lambda.Column));
        var block = new Contract.Compiler.AST.BlockStatement(lambda.Line, lambda.Column);
        block.Statements.Add(new ReturnStatement(lambda.Body, lambda.Line, lambda.Column));
        func.Body = block;
        _program!.Functions.Add(func);
        return name;
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
                if (v.Initializer != null)
                {
                    if (v.Initializer is LambdaExpression lambda)
                    {
                        string lambdaName = GenerateLambda(lambda);
                        _lambdaVariableMap[v.Name] = lambdaName;
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
                ib.While("stack", body => GenerateStatement(body, whileStmt.Body, paramMap));
                break;

            case Contract.Compiler.AST.ForStatement forStmt:
                if (forStmt.Initializer != null)
                    GenerateStatement(ib, forStmt.Initializer, paramMap);
                if (forStmt.Condition != null)
                    GenerateExpression(ib, forStmt.Condition, paramMap);
                else
                    ib.LdcI4(1);
                ib.While("stack", body =>
                {
                    GenerateStatement(body, forStmt.Body, paramMap);
                    if (forStmt.Update != null)
                        GenerateExpression(body, forStmt.Update, paramMap);
                });
                break;

            case Contract.Compiler.AST.BreakStatement:
                ib.Break();
                break;

            case Contract.Compiler.AST.ContinueStatement:
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
                if (paramMap.TryGetValue(id.Name, out int argIdx)) ib.Ldarg(argIdx);
                else ib.Ldloc(id.Name);
                break;

            case BinaryExpression bin:
                if (bin.Operator == "=")
                {
                    if (bin.Left is IndexExpression indexTarget)
                    {
                        GenerateExpression(ib, indexTarget.Target, paramMap);
                        GenerateExpression(ib, indexTarget.Index, paramMap);
                        GenerateExpression(ib, bin.Right, paramMap);
                        ib.Stelem();
                        return;
                    }
                    else if (bin.Left is MemberExpression memTarget)
                    {
                        GenerateExpression(ib, memTarget.Object, paramMap);
                        GenerateExpression(ib, bin.Right, paramMap);
                        var targetName = memTarget.Object is IdentifierExpression targetId ? targetId.Name : "TODO_DYNAMIC_TYPE";
                        ib.Stfld(new FieldReference(new TypeRef(targetName), memTarget.Property, TypeRef.Int32));
                        return;
                    }

                    GenerateExpression(ib, bin.Right, paramMap);
                    if (bin.Left is IdentifierExpression target)
                    {
                        if (paramMap.TryGetValue(target.Name, out int idx)) ib.Starg(idx);
                        else ib.Stloc(target.Name);
                    }
                    return;
                }

                if (bin.Operator is "+=" or "-=" or "*=" or "/=" or "%=")
                {
                    string op = bin.Operator[..^1];

                    if (bin.Left is IdentifierExpression compoundTarget)
                    {
                        if (paramMap.TryGetValue(compoundTarget.Name, out int compoundArgIdx)) ib.Ldarg(compoundArgIdx);
                        else ib.Ldloc(compoundTarget.Name);
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin);
                        if (paramMap.TryGetValue(compoundTarget.Name, out int compoundStoreIdx)) ib.Starg(compoundStoreIdx);
                        else ib.Stloc(compoundTarget.Name);
                    }
                    else if (bin.Left is MemberExpression compoundMem)
                    {
                        GenerateExpression(ib, compoundMem.Object, paramMap);
                        ib.Dup(); // keep the object on the stack for the store
                        var compoundMemObjName = compoundMem.Object is IdentifierExpression compoundMemId ? compoundMemId.Name : "TODO_DYNAMIC_TYPE";
                        var fieldRef = new FieldReference(new TypeRef(compoundMemObjName), compoundMem.Property, TypeRef.Int32);
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
                else if (call.Callee is IdentifierExpression calleeIdent)
                {
                    if (_lambdaVariableMap.TryGetValue(calleeIdent.Name, out string? lambdaName))
                    {
                        var target = new MethodReference(new TypeRef("Global"), lambdaName!, TypeRef.Int32, call.Arguments.Select(_ => TypeRef.Int32).ToList());
                        ib.Call(target);
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
                    ib.Ldfld(new FieldReference(new TypeRef(memObjName), mem.Property, TypeRef.Int32));
                }
                break;

            case IndexExpression indexExpr:
                GenerateExpression(ib, indexExpr.Target, paramMap);
                GenerateExpression(ib, indexExpr.Index, paramMap);
                ib.Ldelem();
                break;

            case LambdaExpression lambda:
                GenerateLambda(lambda);
                break;

            case PipeExpression pipe:
                if (pipe.Right is IdentifierExpression ident)
                {
                    GenerateExpression(ib, pipe.Left, paramMap);
                    if (_lambdaVariableMap.TryGetValue(ident.Name, out string? lambdaName))
                    {
                        var target = new MethodReference(new TypeRef("Global"), lambdaName!, TypeRef.Int32, new List<TypeRef> { TypeRef.Int32 });
                        ib.Call(target);
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
                    string lambdaName = GenerateLambda(lambdaExpr);
                    var target = new MethodReference(new TypeRef("Global"), lambdaName, TypeRef.Int32, new List<TypeRef> { TypeRef.Int32 });
                    ib.Call(target);
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
        // Function types get a textual placeholder until Phase 1 introduces DelegateNode.
        TypeDescriptor.Function f => new TypeRef(f.ToString()),
        _ => TypeRef.Int32
    };

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

    private MethodReference ResolveFunctionReference(string declaringType, string name, int argCount)
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
