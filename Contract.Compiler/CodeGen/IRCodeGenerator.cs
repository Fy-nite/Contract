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
    private int _displayCounter = 0;
    private Dictionary<string, LambdaInfo> _lambdaVariableMap = new();
    private readonly Dictionary<string, LambdaInfo> _lambdaInfos = new();
    private Program? _program;
    // Type-name resolution: fully-qualified wire names plus a short-name → full-name
    // index for namespace-qualified lookups.
    private readonly HashSet<string> _qualifiedTypeNames = new();
    private readonly Dictionary<string, List<string>> _shortToFull = new();
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
    // By-reference capture: captured variables are hoisted into a per-function
    // "display" object shared by the function body and every lambda it creates.
    // The display is a local in an ordinary function and the __closure arg in a
    // lambda body; _closureIsArg says which, _closureLocalName the local name.
    private Dictionary<string, TypeDescriptor>? _displayFields;
    private bool _closureIsArg;
    private string? _closureLocalName;
    // Capture sets precomputed per function so the enclosing scope's locals are
    // visible even for lambdas that appear before a variable's declaration.
    private readonly Dictionary<LambdaExpression, List<(string Name, TypeDescriptor Type)>> _precomputedCaptures = new();
    // Instance-method context: the arg index of 'this' and the declaring contract
    // (used for field access like `x = ...` or `this.x`).
    private int? _thisArgIndex;
    private string? _currentContractName;
    // Contract name → host module name for <NativeBinding("Module")> contracts.
    // Call sites and 'new' rewrite to the host module.
    private readonly Dictionary<string, string> _nativeBindings = new(StringComparer.OrdinalIgnoreCase);
    // Contract name → CLR type name for <ClrImport("System.Math")> contracts.
    // Call sites emit Type.Method targets; the runtime resolves via reflection.
    private readonly Dictionary<string, string> _clrImports = new(StringComparer.OrdinalIgnoreCase);
    // In-scope generic type parameters for the contract/function being generated.
    // Contract params map to their literal name (the VM substitutes during
    // materialization); function params erase to object.
    private HashSet<string> _currentTypeParams = new();
    // Function-level type parameters (fn identity<T>) — these ERASE to object
    // (a function is a single runtime entity).
    private HashSet<string> _currentFnTypeParams = new();
    // Contract name (short + full) → its type parameters, for call-site erasure.
    private readonly Dictionary<string, HashSet<string>> _contractTypeParams = new(StringComparer.OrdinalIgnoreCase);

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
        // The function's shared display (by-reference capture): its fields are
        // the union of every lambda's captures; all lambdas share one instance.
        public Dictionary<string, TypeDescriptor>? DisplayFields;
        public bool SharedClosure;                         // closure is the shared display
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
        File.WriteAllText(outputPath, BuildModule().Serialize().DumpToIRCode());
    }

    /// <summary>Returns the generated ObjektIR text, or null when nothing was generated.</summary>
    public string? GetIRText()
        => _builder == null ? null : BuildModule().Serialize().DumpToIRCode();

    /// <summary>
    /// Builds the module and statically links any imported compiled modules
    /// (DLL-style references) into it, deduplicating by fully-qualified name.
    /// </summary>
    private ObjektRT.Core.AST.ModuleNode BuildModule()
    {
        var module = _builder!.Build();
        if (_program != null)
        {
            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in module.Classes) existing.Add(c.Name);
            foreach (var s in module.Structs) existing.Add(s.Name);
            foreach (var i in module.Interfaces) existing.Add(i.Name);
            foreach (var ext in _program.ExternalModules)
            {
                foreach (var cls in ext.Classes)
                    if (existing.Add(cls.Name)) module.Classes.Add(cls);
                foreach (var str in ext.Structs)
                    if (existing.Add(str.Name)) module.Structs.Add(str);
                foreach (var iface in ext.Interfaces)
                    if (existing.Add(iface.Name)) module.Interfaces.Add(iface);
            }
        }
        return module;
    }

    public void Generate(Program program)
    {
        _program = program;
        _sourceLines = _diagnostics.SourceCode?.Split('\n') ?? Array.Empty<string>();
        _builder = new IRBuilder(program.Contracts.Count > 0 ? program.Contracts[0].Name : "Program");

        // Index every declared type by its fully-qualified wire name for
        // short-name → qualified resolution (namespaces).
        _qualifiedTypeNames.Clear();
        _shortToFull.Clear();
        _contractTypeParams.Clear();
        foreach (var c in program.Contracts) AddTypeIndex(c.Name, c.FullName);
        foreach (var s in program.Structs) AddTypeIndex(s.Name, s.FullName);
        foreach (var e in program.Enums) AddTypeIndex(e.Name, e.FullName);

        // Record every generic contract's type parameters up front so call
        // sites (including Program.Main in the Global class) can resolve the
        // materialized names regardless of generation order.
        foreach (var c in program.Contracts)
        {
            if (c.IsGeneric)
            {
                var paramsSet = new HashSet<string>(c.TypeParameters);
                _contractTypeParams[c.Name] = paramsSet;
                if (c.FullName != c.Name)
                    _contractTypeParams[c.FullName] = paramsSet;
            }
        }

        // Nested structs/enums are emitted under their own wire names too, so
        // short-name and dotted resolution finds them (Color → Raylib.Color).
        foreach (var c in program.Contracts)
        {
            foreach (var m in c.Members)
            {
                if (m is StructDeclaration ns) AddTypeIndex(ns.Name, ns.FullName);
                else if (m is EnumDeclaration ne) AddTypeIndex(ne.Name, ne.FullName);
            }
        }

        // Top-level structs.
        foreach (var structDecl in program.Structs)
        {
            if (structDecl.IsExternal) continue;   // statically linked below
            var structBuilder = _builder.Struct(structDecl.FullName);
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

        // Top-level enums: emitted as classes with static int fields so the
        // member list survives in the IR metadata for reflection. Values are
        // never read from those slots — enum member reads fold to their index.
        foreach (var enumDecl in program.Enums)
        {
            if (enumDecl.IsExternal) continue;   // statically linked below
            var enumBuilder = _builder.Class(enumDecl.FullName);
            foreach (var attr in enumDecl.Attributes)
            {
                enumBuilder.Attribute(attr.Name, attr.Arguments.ToArray());
            }
            for (int i = 0; i < enumDecl.Members.Count; i++)
            {
                enumBuilder.Field(enumDecl.Members[i], TypeRef.Int32).Static();
            }
            enumBuilder.EndClass();
        }

        foreach (var cls in program.Contracts)
        {
            if (cls.IsExternal) continue;   // statically linked below

            if (cls.NativeBindingName != null)
                _nativeBindings[cls.Name] = cls.NativeBindingName;
            if (cls.ClrImportType != null)
                _clrImports[cls.Name] = cls.ClrImportType;

            // Record the contract's type parameters for call-site erasure.
            if (cls.IsGeneric)
            {
                var paramsSet = new HashSet<string>(cls.TypeParameters);
                _contractTypeParams[cls.Name] = paramsSet;
                if (cls.FullName != cls.Name)
                    _contractTypeParams[cls.FullName] = paramsSet;
            }

            // Contracts are emitted under their fully-qualified wire name
            // (com.example.Foo), matching structs/enums — the VM keys types,
            // fields, and methods by these exact names.
            var classBuilder = _builder.Class(cls.FullName);

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
            // Generic contracts are emitted as @Generic(T, ...) definitions —
            // the VM clones + substitutes them per concrete instantiation.
            if (cls.IsGeneric)
            {
                classBuilder.Attribute("Generic", cls.TypeParameters.ToArray());
            }
            if (cls.BaseTypeName != null)
            {
                classBuilder.Extends(ResolveTypeName(cls.BaseTypeName));
            }

            // Interface-style parents (§6 multiple inheritance) are recorded as
            // Implements metadata — declarative, like the base type.
            if (cls.InterfaceNames.Count > 0)
            {
                classBuilder.Implements(cls.InterfaceNames
                    .Select(n => _program?.Contracts.FirstOrDefault(c => c.Name == n || c.FullName == n)?.FullName ?? n)
                    .ToArray());
            }

            // Instance fields: emitted as class fields (static ones carry the
            // static flag in the wire metadata). Contract type params are in
            // scope so field types like `value: T` emit literally as T.
            var savedTypeParams = _currentTypeParams;
            _currentTypeParams = new HashSet<string>(cls.TypeParameters);
            foreach (var field in cls.Fields)
            {
                var fieldBuilder = classBuilder.Field(field.Name, MapType(field.Type));
                if (field.IsStatic) fieldBuilder.Static();
            }

            foreach (var ctor in cls.Constructors)
            {
                GenerateConstructor(classBuilder, ctor, cls.Name);
            }

            foreach (var member in cls.Members)
            {
                if (member is FunctionDeclaration func)
                {
                    // NativeBinding/ClrImport methods are declarations only:
                    // their call sites dispatch to the host module / CLR type,
                    // nothing is emitted here. DllImport methods ARE emitted as
                    // empty stubs — the runtime needs their signatures in the
                    // metadata to build the P/Invoke bridge (and LoadModule
                    // strips their bodies to a single ret anyway).
                    if (cls.NativeBindingName == null && cls.ClrImportType == null)
                        GenerateFunction(classBuilder, func);
                }
                else if (member is StructDeclaration structDecl)
                {
                    var structBuilder = _builder.Struct(structDecl.FullName);
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
                else if (member is EnumDeclaration nestedEnum)
                {
                    var enumBuilder = _builder.Class(nestedEnum.FullName);
                    foreach (var attr in nestedEnum.Attributes)
                    {
                        enumBuilder.Attribute(attr.Name, attr.Arguments.ToArray());
                    }
                    for (int i = 0; i < nestedEnum.Members.Count; i++)
                    {
                        enumBuilder.Field(nestedEnum.Members[i], TypeRef.Int32).Static();
                    }
                    enumBuilder.EndClass();
                }
            }
            _currentTypeParams = savedTypeParams;

            classBuilder.EndClass();
        }

        if (program.Functions.Count > 0)
        {
            var globalClass = _builder.Class("Global");
            // Index loop, not foreach: generating a function synthesizes its
            // lambdas and appends them to program.Functions (GenerateLambda),
            // so nested lambdas (a lambda inside a lambda, e.g. inside a
            // Host.Run callback) grow the list while we walk it.
            for (int i = 0; i < program.Functions.Count; i++)
                GenerateFunction(globalClass, program.Functions[i]);
            globalClass.EndClass();
        }
    }

    private void GenerateFunction(ClassBuilder cb, FunctionDeclaration func)
    {
        _lastIsReturn = false;
        _functionTypedParams = new();
        _functionTypedLocals = new();
        _declaredLocals.Clear();
        // Function type params erase to object; the enclosing contract's params
        // stay literal (the VM substitutes them during materialization).
        var savedFnTypeParams = _currentFnTypeParams;
        var savedTypeParams = _currentTypeParams;
        _currentFnTypeParams = new HashSet<string>(func.TypeParameters);
        if (func.ContractName != null && _contractTypeParams.TryGetValue(func.ContractName, out var contractParams))
            _currentTypeParams = new HashSet<string>(contractParams);
        else
            _currentTypeParams = new HashSet<string>();
        foreach (var p in func.Parameters)
        {
            if (p.Type is TypeDescriptor.Function fnType)
                _functionTypedParams[p.Name] = fnType;
            else if (p.Type is TypeDescriptor.GenericInstance g && TryGetDelegateFunctionType(g, out var gf))
                _functionTypedParams[p.Name] = gf;
        }

        // Lambda methods carry capture context; every function gets a fresh
        // variable-type scope (params + any captures registered below).
        var savedVariableTypes = _variableTypes;
        var savedClosureClass = _closureClass;
        var savedClosureArg = _closureArgIndex;
        var savedCaptureNames = _captureNames;
        var savedBodyLocals = _lambdaBodyLocals;
        var savedDisplayFields = _displayFields;
        var savedClosureIsArg = _closureIsArg;
        var savedClosureLocal = _closureLocalName;
        _variableTypes = new Dictionary<string, TypeDescriptor>();

        _lambdaInfos.TryGetValue(func.Name, out var lambdaInfo);
        // Only lambda bodies track their own declared locals (to exclude them
        // from capture); an ordinary function's hoisted locals must remain
        // captured so they live in the shared display.
        _lambdaBodyLocals = lambdaInfo != null ? new HashSet<string>() : null;
        if (lambdaInfo != null && lambdaInfo.HasCaptures)
        {
            // This is a synthesized lambda body. Captured variables live either
            // in the enclosing function's shared display (by-reference, passed
            // in as __closure) or in a fresh per-lambda closure (by-value
            // fallback for nested lambdas capturing parent-lambda locals).
            _closureClass = lambdaInfo.ClosureClass;
            _closureArgIndex = 0; // __closure is the first parameter
            _closureIsArg = true;
            _closureLocalName = null;
            if (lambdaInfo.DisplayFields != null)
            {
                _displayFields = lambdaInfo.DisplayFields;
                _captureNames = _displayFields.Keys.ToHashSet();
                foreach (var (n, t) in _displayFields)
                    _variableTypes[n] = t;
            }
            else
            {
                _displayFields = null;
                _captureNames = lambdaInfo.Captures.Select(c => c.Name).ToHashSet();
                foreach (var c in lambdaInfo.Captures)
                    _variableTypes[c.Name] = c.Type;
            }
        }
        else
        {
            _closureClass = null;
            _closureArgIndex = null;
            _closureIsArg = false;
            _closureLocalName = null;
            _captureNames = null;
            _displayFields = null;
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

        // The contract context drives bare static-field access, so it applies
        // to static AND instance methods (instance ones additionally take
        // 'this' as arg 0 — matching the runtime's positional-arg model).
        _currentContractName = func.ContractName;
        var paramMap = new Dictionary<string, int>();
        int paramOffset = 0;
        if (func.IsInstance)
        {
            mb.Parameter("this", new TypeRef("object"));
            paramMap["this"] = 0;
            _variableTypes["this"] = new TypeDescriptor.Named("object");
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

        // Hoisting pre-pass: every lambda in this function (at any depth) and
        // every local declaration, so capture analysis sees the whole scope.
        var displayFields = new Dictionary<string, TypeDescriptor>();
        if (func.Body != null)
        {
            var locals = new Dictionary<string, TypeDescriptor>();
            var lambdas = new List<LambdaExpression>();
            CollectHoistInfo(func.Body, locals, lambdas);
            var visible = new Dictionary<string, TypeDescriptor>(_variableTypes);
            foreach (var (n, t) in locals)
                if (!visible.ContainsKey(n)) visible[n] = t;
            _precomputedCaptures.Clear();
            foreach (var l in lambdas)
            {
                var caps = AnalyzeCaptures(l, visible);
                _precomputedCaptures[l] = caps;
                foreach (var (n, t) in caps)
                    displayFields[n] = t;
            }
        }
        else
        {
            _precomputedCaptures.Clear();
        }

        // Ordinary functions: create the shared display when anything is
        // captured. (Lambda bodies reuse the enclosing function's display.)
        if (lambdaInfo == null && displayFields.Count > 0)
        {
            _displayFields = displayFields;
            _closureClass = $"__display_{_displayCounter++}";
            _closureIsArg = false;
            _closureLocalName = "__display";
            _captureNames = displayFields.Keys.ToHashSet();
            var classBuilder = _builder.Class(_closureClass);
            foreach (var (n, t) in displayFields)
                classBuilder.Field(n, MapType(t));
            classBuilder.EndClass();

            // Allocate the display before the first statement, and copy any
            // captured parameters (incl. 'this') into it so the lambdas see them.
            ib.Local(_closureLocalName!, TypeRef.Object);
            ib.Newobj(new TypeRef(_closureClass));
            ib.Stloc(_closureLocalName!);
            foreach (var (n, _) in displayFields)
            {
                if (paramMap.TryGetValue(n, out int pidx))
                {
                    LoadClosure(ib);
                    ib.Dup();
                    ib.Ldarg(pidx);
                    ib.Stfld(CaptureFieldReference(n));
                    ib.Pop();
                }
            }
        }

        // Inject requires checks at function entry
        EmitRequiresChecks(ib, func.Requires, paramMap);

        if (func.Body != null)
        {
            GenerateStatement(ib, func.Body, paramMap);
        }

        // Inject ensures checks before implicit return
        if (!_lastIsReturn && func.Ensures.Count > 0)
        {
            EmitEnsuresChecks(ib, func.Ensures, returnType, paramMap);
        }

        // Implicit return safety
        if (!_lastIsReturn)
        {
            // The VM treats a ≤2-byte method body as a native stub
            // (@DllImport placeholder) and routes calls to native dispatch.
            // An empty body is a real no-op, so pad it so it isn't misrouted.
            if (func.Body is { Statements.Count: 0 } && (returnType == TypeRef.Void || returnType == TypeRef.String))
                ib.Ldnull().Pop();
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
        _displayFields = savedDisplayFields;
        _closureIsArg = savedClosureIsArg;
        _closureLocalName = savedClosureLocal;
        _thisArgIndex = savedThisIndex;
        _currentFnTypeParams = savedFnTypeParams;
        _currentTypeParams = savedTypeParams;
    }

    /// <summary>True when 'name' resolves to a (non-static) field of the current
    /// instance or one of its bases (params/locals shadow fields).</summary>
    private bool IsInstanceField(string name)
    {
        if (_thisArgIndex == null || _currentContractName == null) return false;
        if (_variableTypes.ContainsKey(name)) return false;
        var contract = FindContract(_currentContractName);
        return contract != null && HasFieldIncludingBase(contract, name, staticOnly: false);
    }

    /// <summary>True when 'name' is a static field of the current contract or one
    /// of its bases (shared state; params/locals shadow fields).</summary>
    private bool IsStaticField(string name)
    {
        if (_currentContractName == null) return false;
        if (_variableTypes.ContainsKey(name)) return false;
        var contract = FindContract(_currentContractName);
        return contract != null && HasFieldIncludingBase(contract, name, staticOnly: true);
    }

    /// <summary>True when 'contractName.fieldName' is a static field (short or
    /// qualified, walking the base chain).</summary>
    private bool IsStaticField(string contractName, string fieldName)
    {
        var contract = FindContract(contractName);
        return contract != null && HasFieldIncludingBase(contract, fieldName, staticOnly: true);
    }

    /// <summary>
    /// Folds a comptime constant (FEATURE_PROPOSALS §15): a static field with
    /// <see cref="StructField.IsConst"/> — declared by contract-scope
    /// <c>let X: T = &lt;const&gt;;</c> / <c>const</c>. Returns the folded value
    /// so reads emit the literal instead of ldsfld. Walks the base chain so
    /// inherited constants resolve.
    /// </summary>
    private bool TryFoldConstField(string? contractName, string fieldName, out object? value)
    {
        value = null;
        var contract = FindContract(contractName ?? "");
        for (var c = contract; c != null; c = BaseContract(c))
        {
            var f = c.Fields.FirstOrDefault(f => f.Name == fieldName && f.IsStatic && f.IsConst);
            if (f != null)
            {
                value = f.ConstantValue;
                return true;
            }
        }
        return false;
    }

    /// <summary>The static field reference for 'name' on the current contract,
    /// keyed by its DECLARING type so inherited statics resolve.</summary>
    private FieldReference StaticFieldReference(string name)
    {
        var declaring = FieldDeclaringTypeName(_currentContractName ?? "", name, staticOnly: true)
            ?? ResolveTypeName(_currentContractName ?? "TODO");
        var fieldType = FindFieldType(declaring, name);
        return new FieldReference(new TypeRef(declaring), name, fieldType);
    }

    /// <summary>The zero-based index of an enum member, or -1 when not an enum member.</summary>
    private int FindEnumMemberIndex(string enumName, string member)
    {
        var topLevel = _program?.Enums.FirstOrDefault(e => e.Name == enumName || e.FullName == enumName);
        if (topLevel != null) return topLevel.Members.IndexOf(member);
        var nested = _program?.Contracts
            .SelectMany(c => c.Members.OfType<EnumDeclaration>())
            .FirstOrDefault(e => e.Name == enumName || e.FullName == enumName);
        return nested?.Members.IndexOf(member) ?? -1;
    }

    /// <summary>Collects a pure identifier-dot chain (com.lib.Geo) from a member expression, if any.</summary>
    private static bool TryGetTypePath(Expression expr, out string path)
    {
        path = "";
        if (expr is not MemberExpression mem) return false;
        var segments = new Stack<string>();
        var current = mem.Object;
        while (current is MemberExpression inner)
        {
            segments.Push(inner.Property);
            current = inner.Object;
        }
        if (current is not IdentifierExpression root) return false;
        segments.Push(root.Name);
        path = string.Join(".", segments);
        return true;
    }

    /// <summary>
    /// Resolves the declaring type name for a member access object:
    /// <c>this</c> → the current contract; a known variable → its declared type;
    /// anything else (module/static names) → the name itself. The VM keys fields
    /// as <c>Type.field</c>, so using the variable name instead of its type made
    /// <c>p.count</c> fail with "Unresolved field 'p.count'".
    /// </summary>
    private string ResolveMemberObjectType(string name)
    {
        if (name == "this" && _currentContractName != null)
            return ResolveTypeName(_currentContractName);
        if (_variableTypes.TryGetValue(name, out var t))
        {
            if (t is TypeDescriptor.Named n && !n.IsEmpty)
                return ResolveTypeName(n.Name);
            // b: Box<int> — the field's declaring type is the materialized name.
            if (t is TypeDescriptor.GenericInstance g && IsUserGenericContract(g.Name))
                return MaterializedContractName(g);
        }
        return ResolveTypeName(name);
    }

    /// <summary>
    /// Resolves the declaring wire type name of a member-access chain
    /// (<c>r2.origin.x</c> → the type of <c>x</c>'s owner, <c>Rect</c> then
    /// <c>Vec3</c>), recursing so nested struct/contract fields resolve instead
    /// of collapsing to <c>TODO_DYNAMIC_TYPE</c>. Falls back to the old
    /// behavior (name passthrough / TODO) for unknown receivers.
    /// </summary>
    private string ResolveExpressionObjectType(Expression expr)
    {
        switch (expr)
        {
            case IdentifierExpression id:
                return ResolveMemberObjectType(id.Name);
            case MemberExpression mem:
            {
                var owner = ResolveExpressionObjectType(mem.Object);
                return FindFieldDeclaringTypeName(owner, mem.Property) ?? owner;
            }
            case CallExpression call:
                // e.g. eval(...).atomVal or list.Get(i).atomVal — the call's
                // return type determines the field's declaring type.
                if (call.Symbol is FunctionDeclaration fd && fd.ReturnType is TypeDescriptor.Named ret && !ret.IsEmpty)
                    return ResolveTypeName(ret.Name);
                if (call.Symbol is ExternalMethod em)
                {
                    var retType = em.Info.ReturnType;
                    if (retType == typeof(string)) return "string";
                    if (retType == typeof(int)) return "int";
                    if (retType == typeof(bool)) return "bool";
                    if (retType == typeof(double)) return "double";
                    if (retType == typeof(object)) return "object";
                    return retType.Name;
                }
                break;
        }
        return "TODO_DYNAMIC_TYPE";
    }

    /// <summary>The resolved wire type name of a field declared on a contract or
    /// struct type (short or qualified), walking the base chain for contracts.
    /// Returns the field's TYPE (for nested member chains), or null when the
    /// owner isn't known.</summary>
    private string? FindFieldDeclaringTypeName(string typeName, string fieldName)
    {
        if (_program == null || string.IsNullOrEmpty(typeName) || typeName == "TODO_DYNAMIC_TYPE")
            return null;
        var contract = FindContract(typeName);
        if (contract != null)
        {
            var declaring = FindFieldDeclaringContract(contract, fieldName, staticOnly: false)
                ?? FindFieldDeclaringContract(contract, fieldName, staticOnly: true);
            var f = declaring?.Fields.FirstOrDefault(f => f.Name == fieldName);
            if (f?.Type is TypeDescriptor.Named n && !n.IsEmpty) return ResolveTypeName(n.Name);
            return null;
        }
        var structDecl = FindStruct(typeName);
        if (structDecl != null)
        {
            var f = structDecl.Fields.FirstOrDefault(f => f.Name == fieldName);
            if (f?.Type is TypeDescriptor.Named n && !n.IsEmpty) return ResolveTypeName(n.Name);
        }
        return null;
    }

    /// <summary>Finds a struct declaration by short or qualified name (top-level or nested in a contract).</summary>
    private StructDeclaration? FindStruct(string name)
    {
        if (_program == null) return null;
        var topLevel = _program.Structs.FirstOrDefault(s => s.Name == name || s.FullName == name);
        if (topLevel != null) return topLevel;
        foreach (var contract in _program.Contracts)
        {
            var nested = contract.Members.OfType<StructDeclaration>().FirstOrDefault(s => s.Name == name || s.FullName == name);
            if (nested != null) return nested;
        }
        return null;
    }

    // ── Inheritance (base-chain resolution) ────────────────────────

    /// <summary>Finds a contract by short or qualified name.</summary>
    private ContractDeclaration? FindContract(string name)
    {
        if (_program == null || string.IsNullOrEmpty(name)) return null;
        return _program.Contracts.FirstOrDefault(c => c.Name == name || c.FullName == name);
    }

    /// <summary>The direct base contract of <paramref name="c"/>, or null.</summary>
    private ContractDeclaration? BaseContract(ContractDeclaration c)
        => c.BaseTypeName != null ? FindContract(c.BaseTypeName) : null;

    /// <summary>True when <paramref name="contract"/> or any base declares a field
    /// named <paramref name="fieldName"/> with the given static-ness.</summary>
    private bool HasFieldIncludingBase(ContractDeclaration contract, string fieldName, bool staticOnly)
    {
        for (var c = contract; c != null; c = BaseContract(c))
            if (c.Fields.Any(f => f.Name == fieldName && f.IsStatic == staticOnly)) return true;
        return false;
    }

    /// <summary>The contract that declares a field (most-derived first), or null.</summary>
    private ContractDeclaration? FindFieldDeclaringContract(ContractDeclaration contract, string fieldName, bool staticOnly)
    {
        for (var c = contract; c != null; c = BaseContract(c))
        {
            var f = c.Fields.FirstOrDefault(f => f.Name == fieldName && f.IsStatic == staticOnly);
            if (f != null) return c;
        }
        return null;
    }

    /// <summary>The resolved wire name of the contract declaring a field on
    /// <paramref name="typeName"/> (walking bases), or null when unknown.
    /// For a materialized generic (<c>Box&lt;int32&gt;</c>) the declaring type
    /// is the materialized name itself.</summary>
    private string? FieldDeclaringTypeName(string typeName, string fieldName, bool staticOnly)
    {
        if (TrySplitMaterializedName(typeName, out var baseName, out _))
        {
            var contract = FindContract(baseName);
            var declaring = contract != null ? FindFieldDeclaringContract(contract, fieldName, staticOnly) : null;
            return declaring != null ? typeName : null;
        }
        var contract2 = FindContract(typeName);
        var declaring2 = contract2 != null ? FindFieldDeclaringContract(contract2, fieldName, staticOnly) : null;
        return declaring2 != null ? ResolveTypeName(declaring2.Name) : null;
    }

    /// <summary>
    /// Splits a materialized generic wire name (<c>Box&lt;int32&gt;</c>) into its
    /// base contract name and concrete type arguments. Returns false for
    /// non-generic names.
    /// </summary>
    private static bool TrySplitMaterializedName(string name, out string baseName, out List<TypeDescriptor> args)
    {
        baseName = name;
        args = new List<TypeDescriptor>();
        int lt = name.IndexOf('<');
        if (lt <= 0 || !name.EndsWith(">", StringComparison.Ordinal)) return false;
        baseName = name[..lt];
        var inner = name[(lt + 1)..^1];
        foreach (var part in inner.Split(','))
        {
            var t = part.Trim();
            if (t.Length > 0) args.Add(TypeDescriptor.Parse(t));
        }
        return true;
    }

    /// <summary>Substitutes type parameters in a descriptor using <paramref name="map"/>.</summary>
    private static TypeDescriptor SubstituteTypeParams(TypeDescriptor type, IReadOnlyDictionary<string, TypeDescriptor> map)
    {
        switch (type)
        {
            case TypeDescriptor.Named n:
                return map.TryGetValue(n.Name, out var mapped) ? mapped : type;
            case TypeDescriptor.ArrayOf a:
                return new TypeDescriptor.ArrayOf(SubstituteTypeParams(a.Element, map));
            case TypeDescriptor.Function f:
                return new TypeDescriptor.Function(
                    f.Parameters.Select(p => SubstituteTypeParams(p, map)).ToList(),
                    SubstituteTypeParams(f.Return, map));
            case TypeDescriptor.GenericInstance g:
                return new TypeDescriptor.GenericInstance(
                    g.Name,
                    g.Arguments.Select(a => SubstituteTypeParams(a, map)).ToList());
            default:
                return type;
        }
    }

    /// <summary>The type-parameter map for a materialized generic name
    /// (<c>Box&lt;int32&gt;</c> → <c>{ T → int32 }</c>), or null when not generic.</summary>
    private Dictionary<string, TypeDescriptor>? MaterializedTypeParamMap(string typeName)
    {
        if (!TrySplitMaterializedName(typeName, out var baseName, out var args)) return null;
        if (!_contractTypeParams.TryGetValue(baseName, out var paramsSet)) return null;
        var map = new Dictionary<string, TypeDescriptor>();
        int i = 0;
        foreach (var p in paramsSet)
        {
            if (i < args.Count) map[p] = args[i];
            i++;
        }
        return map;
    }

    /// <summary>Finds a member function on a contract or its bases (most-derived first).</summary>
    private FunctionDeclaration? FindMethodIncludingBase(ContractDeclaration contract, string name, bool instanceOnly)
    {
        for (var c = contract; c != null; c = BaseContract(c))
        {
            var m = c.Members.OfType<FunctionDeclaration>()
                .FirstOrDefault(f => f.Name == name && (!instanceOnly || f.IsInstance));
            if (m != null) return m;
        }
        return null;
    }

    /// <summary>The field reference for 'name' on the current instance, keyed by
    /// its DECLARING type so inherited fields resolve (Animal.name, not Dog.name).</summary>
    private FieldReference InstanceFieldReference(string name)
    {
        var declaring = FieldDeclaringTypeName(_currentContractName ?? "", name, staticOnly: false)
            ?? ResolveTypeName(_currentContractName ?? "TODO");
        return new FieldReference(new TypeRef(declaring), name, FindFieldType(declaring, name));
    }

    /// <summary>Looks up a field's type on the given contract/struct type name
    /// (short or qualified), walking the base chain for contracts. For a
    /// materialized generic (<c>Box&lt;int32&gt;</c>) the field's type params are
    /// substituted to the concrete args.</summary>
    private TypeRef FindFieldType(string typeName, string fieldName)
    {
        if (_program != null)
        {
            var typeParamMap = MaterializedTypeParamMap(typeName);
            var lookupName = typeParamMap != null ? (TrySplitMaterializedName(typeName, out var bn, out _) ? bn : typeName) : typeName;
            var contract = FindContract(lookupName);
            if (contract != null)
            {
                var declaring = FindFieldDeclaringContract(contract, fieldName, staticOnly: false)
                    ?? FindFieldDeclaringContract(contract, fieldName, staticOnly: true);
                var f = declaring?.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (f != null)
                {
                    var fieldType = typeParamMap != null ? SubstituteTypeParams(f.Type, typeParamMap) : f.Type;
                    return MapType(fieldType);
                }
            }
            var structDecl = FindStruct(typeName);
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
        var savedClosureClass = _closureClass;
        var savedClosureArg = _closureArgIndex;
        var savedCaptureNames = _captureNames;
        var savedBodyLocals = _lambdaBodyLocals;
        var savedDisplayFields = _displayFields;
        var savedClosureIsArg = _closureIsArg;
        var savedClosureLocal = _closureLocalName;
        _variableTypes = new Dictionary<string, TypeDescriptor>();
        _lambdaBodyLocals = null;   // constructors aren't lambda bodies
        _declaredLocals.Clear();
        _thisArgIndex = 0;
        _currentContractName = contractName;
        _closureClass = null;
        _closureArgIndex = null;
        _closureIsArg = false;
        _closureLocalName = null;
        _captureNames = null;
        _displayFields = null;

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

        // Hoisting pre-pass — same by-reference capture scheme as functions.
        var displayFields = new Dictionary<string, TypeDescriptor>();
        if (ctor.Body != null)
        {
            var locals = new Dictionary<string, TypeDescriptor>();
            var lambdas = new List<LambdaExpression>();
            CollectHoistInfo(ctor.Body, locals, lambdas);
            var visible = new Dictionary<string, TypeDescriptor>(_variableTypes);
            foreach (var (n, t) in locals)
                if (!visible.ContainsKey(n)) visible[n] = t;
            _precomputedCaptures.Clear();
            foreach (var l in lambdas)
            {
                var caps = AnalyzeCaptures(l, visible);
                _precomputedCaptures[l] = caps;
                foreach (var (n, t) in caps)
                    displayFields[n] = t;
            }
        }
        else
        {
            _precomputedCaptures.Clear();
        }

        if (displayFields.Count > 0)
        {
            _displayFields = displayFields;
            _closureClass = $"__display_{_displayCounter++}";
            _closureIsArg = false;
            _closureLocalName = "__display";
            _captureNames = displayFields.Keys.ToHashSet();
            var classBuilder = _builder.Class(_closureClass);
            foreach (var (n, t) in displayFields)
                classBuilder.Field(n, MapType(t));
            classBuilder.EndClass();

            ib.Local(_closureLocalName!, TypeRef.Object);
            ib.Newobj(new TypeRef(_closureClass));
            ib.Stloc(_closureLocalName!);
            foreach (var (n, _) in displayFields)
            {
                if (paramMap.TryGetValue(n, out int pidx))
                {
                    LoadClosure(ib);
                    ib.Dup();
                    ib.Ldarg(pidx);
                    ib.Stfld(CaptureFieldReference(n));
                    ib.Pop();
                }
            }
        }

        // Inject requires checks at constructor entry
        EmitRequiresChecks(ib, ctor.Requires, paramMap);

        if (ctor.Body != null)
        {
            GenerateStatement(ib, ctor.Body, paramMap);
        }

        // Inject invariant checks at constructor exit
        if (_currentContractName != null && _program != null)
        {
            var contract = _program.Contracts.FirstOrDefault(c => c.Name == _currentContractName || c.FullName == _currentContractName);
            if (contract != null && contract.Invariants.Count > 0)
            {
                EmitInvariantChecks(ib, contract, paramMap);
            }
        }

        if (!_lastIsReturn)
        {
            // The VM treats a ≤2-byte method body as a native stub (@DllImport
            // placeholder) and routes calls to native dispatch. An empty
            // constructor is a real no-op, so pad it so it isn't misrouted.
            if (ctor.Body is { Statements.Count: 0 })
                ib.Ldnull().Pop().Ret();
            else
                ib.Ret();
        }

        ib.EndBody().EndMethod();

        _variableTypes = savedVariableTypes;
        _thisArgIndex = savedThisIndex;
        _currentContractName = savedContract;
        _closureClass = savedClosureClass;
        _closureArgIndex = savedClosureArg;
        _captureNames = savedCaptureNames;
        _lambdaBodyLocals = savedBodyLocals;
        _displayFields = savedDisplayFields;
        _closureIsArg = savedClosureIsArg;
        _closureLocalName = savedClosureLocal;
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
        // Precomputed during the function's hoisting pre-pass so the enclosing
        // scope's locals are visible; falls back to the live scope.
        var captures = _precomputedCaptures.TryGetValue(lambda, out var pre)
            ? pre
            : AnalyzeCaptures(lambda, _variableTypes);
        if (captures.Count > 0 && _closureClass != null && _displayFields != null
            && captures.All(c => _displayFields.ContainsKey(c.Name)))
        {
            // By-reference capture: share the function's display object with the
            // enclosing scope (C#-style). No per-lambda copy — the display is
            // already allocated (function body) or is our __closure arg (lambda
            // body), and every lambda in the function shares the same one.
            info.ClosureClass = _closureClass;
            info.DisplayFields = _displayFields;
            info.SharedClosure = true;
            info.Captures = captures;
        }
        else if (captures.Count > 0)
        {
            // Fallback (e.g. a nested lambda capturing a local declared in its
            // parent lambda's body): a fresh per-lambda closure copying current
            // values — the previous by-value behavior.
            info.ClosureClass = $"__closure_{_closureCounter++}";
            info.Captures = captures;
            info.SharedClosure = false;
            EnsureClosureClass(info);
        }

        // Synthesize the lambda method. Capturing lambdas take the closure as
        // their first parameter ("__closure"); the body reads/writes captured
        // vars through it as closure fields. The declaring contract is carried
        // so member access on a captured 'this' (this.count) resolves to the
        // right type inside the lambda body.
        var func = new FunctionDeclaration(info.Name, lambda.Line, lambda.Column)
        {
            IsStatic = true,
            ContractName = _currentContractName,
        };
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
    /// Walks a function body collecting every local declaration (name → type)
    /// and every lambda expression at any depth — the input to the hoisting
    /// pre-pass so capture analysis sees the whole scope, not just the locals
    /// declared before a given lambda.
    /// </summary>
    private static void CollectHoistInfo(Contract.Compiler.AST.Statement stmt, Dictionary<string, TypeDescriptor> locals, List<LambdaExpression> lambdas)
    {
        switch (stmt)
        {
            case Contract.Compiler.AST.BlockStatement b:
                foreach (var s in b.Statements) CollectHoistInfo(s, locals, lambdas);
                break;
            case Contract.Compiler.AST.VariableDeclaration v:
                locals[v.Name] = v.Type;
                if (v.Initializer != null) CollectHoistInfoExpr(v.Initializer, locals, lambdas);
                break;
            case Contract.Compiler.AST.ExpressionStatement es:
                CollectHoistInfoExpr(es.Expression, locals, lambdas);
                break;
            case Contract.Compiler.AST.IfStatement i:
                CollectHoistInfoExpr(i.Condition, locals, lambdas);
                CollectHoistInfo(i.ThenBranch, locals, lambdas);
                if (i.ElseBranch != null) CollectHoistInfo(i.ElseBranch, locals, lambdas);
                break;
            case Contract.Compiler.AST.WhileStatement w:
                CollectHoistInfoExpr(w.Condition, locals, lambdas);
                CollectHoistInfo(w.Body, locals, lambdas);
                break;
            case Contract.Compiler.AST.ForStatement f:
                if (f.Initializer != null) CollectHoistInfo(f.Initializer, locals, lambdas);
                if (f.Condition != null) CollectHoistInfoExpr(f.Condition, locals, lambdas);
                CollectHoistInfo(f.Body, locals, lambdas);
                if (f.Update != null) CollectHoistInfoExpr(f.Update, locals, lambdas);
                break;
            case Contract.Compiler.AST.ReturnStatement r:
                if (r.Value != null) CollectHoistInfoExpr(r.Value, locals, lambdas);
                break;
            case Contract.Compiler.AST.SwitchStatement sw:
                CollectHoistInfoExpr(sw.Expression, locals, lambdas);
                foreach (var c in sw.Cases)
                    foreach (var s in c.Statements) CollectHoistInfo(s, locals, lambdas);
                break;
            case Contract.Compiler.AST.ForInStatement fi:
                CollectHoistInfoExpr(fi.Iterable, locals, lambdas);
                locals[fi.Variable] = fi.ResolvedElementType ?? TypeDescriptor.Empty;
                if (fi.ValueVariable != null) locals[fi.ValueVariable] = TypeDescriptor.Empty;
                CollectHoistInfo(fi.Body, locals, lambdas);
                break;
            case Contract.Compiler.AST.BreakStatement:
            case Contract.Compiler.AST.ContinueStatement:
                break;
        }
    }

    private static void CollectHoistInfoExpr(Expression e, Dictionary<string, TypeDescriptor> locals, List<LambdaExpression> lambdas)
    {
        switch (e)
        {
            case LambdaExpression l:
                lambdas.Add(l);
                if (l.BlockBody != null)
                {
                    foreach (var s in l.BlockBody.Statements) CollectHoistInfo(s, locals, lambdas);
                }
                else if (l.Body != null)
                {
                    CollectHoistInfoExpr(l.Body, locals, lambdas);
                }
                break;
            case TernaryExpression t3:
                CollectHoistInfoExpr(t3.Condition, locals, lambdas);
                CollectHoistInfoExpr(t3.ThenBranch, locals, lambdas);
                CollectHoistInfoExpr(t3.ElseBranch, locals, lambdas);
                break;
            case Contract.Compiler.AST.IfExpression ie:
                CollectHoistInfoExpr(ie.Condition, locals, lambdas);
                CollectHoistInfoExpr(ie.ThenBranch, locals, lambdas);
                CollectHoistInfoExpr(ie.ElseBranch, locals, lambdas);
                break;
            case Contract.Compiler.AST.MatchExpression me:
                CollectHoistInfoExpr(me.Scrutinee, locals, lambdas);
                foreach (var arm in me.Arms)
                {
                    // Pattern-bound names are locals visible to guard/result.
                    foreach (var (boundName, boundType) in arm.BoundNames)
                        locals[boundName] = boundType;
                    if (arm.Guard != null) CollectHoistInfoExpr(arm.Guard, locals, lambdas);
                    CollectHoistInfoExpr(arm.Result, locals, lambdas);
                }
                break;
            case Contract.Compiler.AST.RangeExpression re:
                CollectHoistInfoExpr(re.Start, locals, lambdas);
                CollectHoistInfoExpr(re.End, locals, lambdas);
                if (re.Step != null) CollectHoistInfoExpr(re.Step, locals, lambdas);
                break;
            case BinaryExpression b:
                CollectHoistInfoExpr(b.Left, locals, lambdas);
                CollectHoistInfoExpr(b.Right, locals, lambdas);
                break;
            case UnaryExpression u:
                CollectHoistInfoExpr(u.Operand, locals, lambdas);
                break;
            case CallExpression c:
                CollectHoistInfoExpr(c.Callee, locals, lambdas);
                foreach (var a in c.Arguments) CollectHoistInfoExpr(a, locals, lambdas);
                break;
            case MemberExpression m:
                CollectHoistInfoExpr(m.Object, locals, lambdas);
                break;
            case IndexExpression ix:
                CollectHoistInfoExpr(ix.Target, locals, lambdas);
                CollectHoistInfoExpr(ix.Index, locals, lambdas);
                break;
            case NewExpression ne:
                if (ne.Size != null) CollectHoistInfoExpr(ne.Size, locals, lambdas);
                foreach (var a in ne.Arguments) CollectHoistInfoExpr(a, locals, lambdas);
                break;
            case ArrayLiteralExpression al:
                foreach (var el in al.Elements) CollectHoistInfoExpr(el, locals, lambdas);
                break;
            case PipeExpression p:
                CollectHoistInfoExpr(p.Left, locals, lambdas);
                CollectHoistInfoExpr(p.Right, locals, lambdas);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Collects free variables in the lambda body: identifiers that are not
    /// lambda params (or shadowed by nested lambda params / block locals) and
    /// whose type is known in <paramref name="visibleTypes"/> (the enclosing
    /// scope). These become display fields (by-reference capture).
    /// </summary>
    private List<(string Name, TypeDescriptor Type)> AnalyzeCaptures(LambdaExpression lambda, Dictionary<string, TypeDescriptor> visibleTypes)
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
                    if (visibleTypes.TryGetValue(id.Name, out var t))
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
                case TernaryExpression t3: Collect(t3.Condition, sh); Collect(t3.ThenBranch, sh); Collect(t3.ElseBranch, sh); break;
                case Contract.Compiler.AST.IfExpression ie:
                    Collect(ie.Condition, sh); Collect(ie.ThenBranch, sh); Collect(ie.ElseBranch, sh);
                    break;
                case Contract.Compiler.AST.RangeExpression re:
                    Collect(re.Start, sh); Collect(re.End, sh);
                    if (re.Step != null) Collect(re.Step, sh);
                    break;
                case Contract.Compiler.AST.MatchExpression me:
                    Collect(me.Scrutinee, sh);
                    foreach (var arm in me.Arms)
                    {
                        // Pattern-bound names shadow captures of the same name.
                        var armShadow = new HashSet<string>(sh);
                        foreach (var bound in arm.BoundNames.Keys) armShadow.Add(bound);
                        if (arm.Guard != null) Collect(arm.Guard, armShadow);
                        Collect(arm.Result, armShadow);
                    }
                    break;
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
                case Contract.Compiler.AST.ForInStatement fi:
                    Collect(fi.Iterable, sh);
                    sh.Add(fi.Variable);   // the loop variable shadows captures
                    if (fi.ValueVariable != null) sh.Add(fi.ValueVariable);
                    CollectStmt(fi.Body, sh);
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
    /// store target. By-reference capture: the delegate's closure is the
    /// function's shared display object (no values are copied). Fallback
    /// (fresh per-lambda closure): allocate the delegate, then a closure
    /// copying current values from the enclosing scope. The delegate is left
    /// on the stack as the value.
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

        if (info.SharedClosure)
        {
            // By-reference capture: the closure IS the function's shared
            // display — already allocated (function body) or our __closure arg
            // (lambda body). Just wire the delegate to it; no copies.
            ib.Newobj(new TypeRef(DelegateClassName));   // [d]
            ib.Dup();                                    // [d, d]
            LoadClosure(ib);                             // [d, d, display]
            ib.Stfld(new FieldReference(new TypeRef(DelegateClassName), DelegateClosureField, TypeRef.Object)); // [d]
            ib.Dup();                                    // [d, d]
            ib.Ldstr($"Global.{info.Name}");             // [d, d, s]
            ib.Stfld(new FieldReference(new TypeRef(DelegateClassName), DelegateTargetField, TypeRef.String));  // [d]
            return;
        }

        // Fallback (by-value): fresh closure, copy current values.
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
    /// Pushes the active closure object: the __closure arg in a lambda body, or
    /// the shared display local in an ordinary function body.
    /// </summary>
    private void LoadClosure(InstructionBuilder ib)
    {
        if (_closureIsArg) ib.Ldarg(_closureArgIndex!.Value);
        else ib.Ldloc(_closureLocalName!);
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
                // Destructuring: var (a, b) = f(); — call f (returns object[]),
                // then bind each name to arr[i].
                if (v.Names.Count > 0)
                {
                    if (v.Initializer != null)
                        GenerateExpression(ib, v.Initializer, paramMap);
                    for (int i = 0; i < v.Names.Count; i++)
                    {
                        var name = v.Names[i];
                        _variableTypes[name] = new TypeDescriptor.Named("object");
                        _lambdaBodyLocals?.Add(name);
                        ib.Local(name, TypeRef.Object);
                        ib.Dup();
                        ib.LdcI4(i);
                        ib.Ldelem();
                        ib.Stloc(name);
                    }
                    ib.Pop();   // discard the array reference
                    break;
                }

                _variableTypes[v.Name] = v.Type;
                _lambdaBodyLocals?.Add(v.Name);
                if (v.Type is TypeDescriptor.Function fnLocalType)
                    _functionTypedLocals[v.Name] = fnLocalType;
                else if (v.Type is TypeDescriptor.GenericInstance gLocal && TryGetDelegateFunctionType(gLocal, out var gfLocal))
                    _functionTypedLocals[v.Name] = gfLocal;
                if (IsCaptured(v.Name))
                {
                    // Hoisted variable: it lives in the shared display, not a
                    // local slot. The initializer writes through the display
                    // field; nothing is left on the stack afterwards.
                    if (v.Initializer != null)
                    {
                        LoadClosure(ib);
                        ib.Dup();
                        if (v.Initializer is LambdaExpression lambda)
                        {
                            // A lambda assigned to a variable is a value now: build
                            // the delegate object. The direct-call fast path below
                            // still resolves through _lambdaVariableMap for `inc(5)`.
                            var info = GenerateLambda(lambda, paramMap);
                            _lambdaVariableMap[v.Name] = info;
                            GenerateLambdaValue(ib, info, paramMap);
                        }
                        else
                        {
                            // Array-literal element-type hint: used for empty
                            // literals like 'let x: string[] = []'.
                            var prevHint = _arrayElementTypeHint;
                            if (v.Initializer is Contract.Compiler.AST.ArrayLiteralExpression)
                                _arrayElementTypeHint = v.Type;
                            GenerateExpression(ib, v.Initializer, paramMap);
                            _arrayElementTypeHint = prevHint;
                        }
                        ib.Stfld(CaptureFieldReference(v.Name));
                        ib.Pop();   // discard the leftover display reference
                    }
                    break;
                }

                ib.Local(v.Name, MapType(v.Type));
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
                    {
                        GenerateExpression(body, forStmt.Update, paramMap);
                        // The update is a statement; its expression value must
                        // not leak onto the stack (it would accumulate across
                        // iterations and unbalance the loop).
                        body.Pop();
                    }
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
                    {
                        GenerateExpression(body, forStmt.Update, paramMap);
                        body.Pop();
                    }
                    if (forStmt.Condition != null)
                        GenerateExpression(body, forStmt.Condition, paramMap);
                    else
                        body.LdcI4(1);
                });
                _loopContinueEmitters.Pop();
                ib.Pop();
                break;

            case Contract.Compiler.AST.ForInStatement forIn:
                GenerateForInStatement(ib, forIn, paramMap);
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

            case Contract.Compiler.AST.TryStatement tryStmt:
                ib.Try(
                    tryBody => GenerateStatement(tryBody, tryStmt.TryBlock, paramMap),
                    catchBuilder =>
                    {
                        foreach (var cc in tryStmt.CatchClauses)
                        {
                            catchBuilder.Catch(cc.ExceptionType, cc.ExceptionVar, body =>
                            {
                                // Declare the exception local and store the thrown value
                                body.Local(cc.ExceptionVar, new TypeRef("object"));
                                body.Stloc(cc.ExceptionVar);
                                GenerateStatement(body, cc.Body, paramMap);
                            });
                        }
                    },
                    tryStmt.FinallyBlock != null
                        ? finBody => GenerateStatement(finBody, tryStmt.FinallyBlock!, paramMap)
                        : null
                );
                break;

            case Contract.Compiler.AST.ThrowStatement throwStmt:
                GenerateExpression(ib, throwStmt.Value, paramMap);
                ib.Throw();
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

            case TernaryExpression ternary:
                // cond ? then : else — evaluate cond, branch, both arms leave a value.
                GenerateExpression(ib, ternary.Condition, paramMap);
                ib.If("stack",
                    then => GenerateExpression(then, ternary.ThenBranch, paramMap),
                    els => GenerateExpression(els, ternary.ElseBranch, paramMap));
                break;

            case IfExpression ifExpr:
                // §3: if as a value — same shape as the ternary.
                GenerateExpression(ib, ifExpr.Condition, paramMap);
                ib.If("stack",
                    then => GenerateExpression(then, ifExpr.ThenBranch, paramMap),
                    els => GenerateExpression(els, ifExpr.ElseBranch, paramMap));
                break;

            case MatchExpression matchExpr:
                GenerateMatchExpression(ib, matchExpr, paramMap);
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
                if (IsStaticField(id.Name))
                {
                    // Comptime constant read: emit the folded literal (§15).
                    if (TryFoldConstField(_currentContractName, id.Name, out var bareConst))
                    {
                        EmitLiteralValue(ib, bareConst);
                        break;
                    }
                    // Bare static field access — no receiver to push.
                    ib.Ldsfld(StaticFieldReference(id.Name));
                    break;
                }
                if (IsCaptured(id.Name))
                {
                    // Captured var: read through the shared display's field.
                    LoadClosure(ib);
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
                        // Captured var write: store through the shared display.
                        // stfld pops [value, object], so dup the object first.
                        LoadClosure(ib);                     // object
                        ib.Dup();                            // object, object
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
                    if (bin.Left is IdentifierExpression staticFieldTarget && IsStaticField(staticFieldTarget.Name))
                    {
                        // Bare static field write: stsfld pops just the value, so
                        // dup to leave the assigned value for the statement's pop.
                        GenerateExpression(ib, bin.Right, paramMap);
                        ib.Dup();
                        ib.Stsfld(StaticFieldReference(staticFieldTarget.Name));
                        return;
                    }
                    if (bin.Left is IndexExpression indexTarget)
                    {
                        // arr[i] = rhs. stelem pops [value, index, array], so
                        // dup the array (matching the member-assignment
                        // convention: obj.f = v leaves the receiver) to keep
                        // the expression stack-balanced — the leftover array is
                        // consumed by the statement's trailing pop.
                        GenerateExpression(ib, indexTarget.Target, paramMap);
                        ib.Dup();
                        GenerateExpression(ib, indexTarget.Index, paramMap);
                        GenerateExpression(ib, bin.Right, paramMap);
                        ib.Stelem();
                        return;
                    }
                    else if (bin.Left is MemberExpression memTarget)
                    {
                        var targetName = ResolveExpressionObjectType(memTarget.Object);
                        if (IsStaticField(targetName, memTarget.Property))
                        {
                            // Static field write (Config.count = v) — no receiver.
                            var staticDecl = FieldDeclaringTypeName(targetName, memTarget.Property, staticOnly: true) ?? targetName;
                            GenerateExpression(ib, bin.Right, paramMap);
                            ib.Dup();
                            ib.Stsfld(new FieldReference(new TypeRef(staticDecl), memTarget.Property, FindFieldType(staticDecl, memTarget.Property)));
                        }
                        else
                        {
                            var instDecl = FieldDeclaringTypeName(targetName, memTarget.Property, staticOnly: false) ?? targetName;
                            GenerateExpression(ib, memTarget.Object, paramMap);
                            ib.Dup();   // keep the object for the store (stfld pops value, object)
                            GenerateExpression(ib, bin.Right, paramMap);
                            ib.Stfld(new FieldReference(new TypeRef(instDecl), memTarget.Property, FindFieldType(instDecl, memTarget.Property)));
                        }
                        return;
                    }
                    else if (bin.Left is ScopedAccessExpression scopedWrite && IsStaticField(scopedWrite.Module, scopedWrite.Member))
                    {
                        // Static field write via Contract::field = v.
                        var scopedTarget = FieldDeclaringTypeName(scopedWrite.Module, scopedWrite.Member, staticOnly: true)
                            ?? ResolveTypeName(scopedWrite.Module);
                        GenerateExpression(ib, bin.Right, paramMap);
                        ib.Dup();
                        ib.Stsfld(new FieldReference(new TypeRef(scopedTarget), scopedWrite.Member, FindFieldType(scopedTarget, scopedWrite.Member)));
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
                        // dup the display twice: one for ldfld, one left after stfld.
                        LoadClosure(ib);                 // [c]
                        ib.Dup();                        // [c, c]
                        ib.Dup();                        // [c, c, c]
                        ib.Ldfld(cfieldRef);             // [c, c, val]
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin); // [c, c, newval]
                        ib.Stfld(cfieldRef);             // [c]
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
                    if (bin.Left is IdentifierExpression staticCompound && IsStaticField(staticCompound.Name))
                    {
                        // Bare static field compound: ldsfld, rhs, op, store.
                        var sref = StaticFieldReference(staticCompound.Name);
                        ib.Ldsfld(sref);
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin);
                        ib.Dup();   // leave the new value for the statement's pop
                        ib.Stsfld(sref);
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
                        var compoundMemObjName = ResolveExpressionObjectType(compoundMem.Object);
                        var compoundDecl = FieldDeclaringTypeName(compoundMemObjName, compoundMem.Property, staticOnly: false)
                            ?? compoundMemObjName;
                        var fieldRef = new FieldReference(new TypeRef(compoundDecl), compoundMem.Property, FindFieldType(compoundDecl, compoundMem.Property));
                        if (IsStaticField(compoundMemObjName, compoundMem.Property))
                        {
                            // Static member compound (Config.count += v) — no receiver.
                            ib.Ldsfld(fieldRef);
                            GenerateExpression(ib, bin.Right, paramMap);
                            EmitArithmeticOrConcat(ib, op, bin);
                            ib.Dup();
                            ib.Stsfld(fieldRef);
                        }
                        else
                        {
                            GenerateExpression(ib, compoundMem.Object, paramMap);
                            ib.Dup(); // keep one copy for the store
                            ib.Dup(); // and one left over after stfld
                            ib.Ldfld(fieldRef);
                            GenerateExpression(ib, bin.Right, paramMap);
                            EmitArithmeticOrConcat(ib, op, bin);
                            ib.Stfld(fieldRef);
                        }
                    }
                    else if (bin.Left is ScopedAccessExpression compoundScoped && IsStaticField(compoundScoped.Module, compoundScoped.Member))
                    {
                        // Static field compound via Contract::field += v.
                        var scopedTarget2 = FieldDeclaringTypeName(compoundScoped.Module, compoundScoped.Member, staticOnly: true)
                            ?? ResolveTypeName(compoundScoped.Module);
                        var sref2 = new FieldReference(new TypeRef(scopedTarget2), compoundScoped.Member, FindFieldType(scopedTarget2, compoundScoped.Member));
                        ib.Ldsfld(sref2);
                        GenerateExpression(ib, bin.Right, paramMap);
                        EmitArithmeticOrConcat(ib, op, bin);
                        ib.Dup();
                        ib.Stsfld(sref2);
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

                if (bin.Operator == "&&")
                {
                    // Short-circuit &&: if left is false, result is false
                    // without evaluating right.
                    GenerateExpression(ib, bin.Left, paramMap);
                    ib.If("stack",
                        then => GenerateExpression(then, bin.Right, paramMap),
                        els => els.LdcI4(0));
                    return;
                }

                if (bin.Operator == "||")
                {
                    // Short-circuit ||: if left is true, result is true
                    // without evaluating right.
                    GenerateExpression(ib, bin.Left, paramMap);
                    ib.If("stack",
                        then => then.LdcI4(1),
                        els => GenerateExpression(els, bin.Right, paramMap));
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
                }
                break;

            case CallExpression call:
                // Instance methods declare 'this' as param 0, and the runtime
                // pops call args in reverse into the callee's locals — so the
                // receiver must be pushed FIRST (before the arguments) to land
                // in locals[0]. (Delegates keep the receiver-last convention.)
                bool isInstanceCall = call.Symbol is FunctionDeclaration instCallFunc && instCallFunc.IsInstance;
                bool isExtensionCall = call.Symbol is FunctionDeclaration extCheck && extCheck.IsExtension;

                // A bare call to a sibling instance method (method() inside an
                // instance method of the same contract) implicitly passes
                // `this` as the receiver — C#-style.
                bool implicitThisCall = call.Symbol == null
                    && call.Callee is IdentifierExpression bareTarget
                    && _thisArgIndex != null
                    && FindFunction(bareTarget.Name) is { IsInstance: true } bareFn
                    && bareFn.ContractName == _currentContractName;

                if (isInstanceCall || implicitThisCall)
                {
                    if (call.Callee is MemberExpression recvMem)
                        GenerateExpression(ib, recvMem.Object, paramMap);
                    else if (call.Callee is IdentifierExpression recvSelf)
                        ib.Ldarg(_thisArgIndex!.Value);
                }
                else if (isExtensionCall && call.Callee is MemberExpression extRecv)
                {
                    // Extension method: push the receiver as the first argument
                    GenerateExpression(ib, extRecv.Object, paramMap);
                }

                // Push arguments to stack
                foreach (var arg in call.Arguments)
                    GenerateExpression(ib, arg, paramMap);

                if (call.Callee is CallExpression innerCall)
                {
                    // f()(args): evaluate the function call — it produces a
                    // delegate — then invoke that delegate with the already
                    // pushed arguments. Stack: [args..., delegate] and
                    // callvirt pops the receiver first, matching the VM.
                    GenerateExpression(ib, innerCall, paramMap);
                    var fnType = GetCallFunctionType(innerCall);
                    if (fnType != null)
                    {
                        var pt = fnType.Parameters.Select(MapType).ToList();
                        var ret = MapType(fnType.Return);
                        ib.Callvirt(new MethodReference(new TypeRef(DelegateClassName), DelegateInvokeMethod, ret, pt));
                    }
                    else
                    {
                        var pt = call.Arguments.Select(_ => TypeRef.Int32).ToList();
                        ib.Callvirt(new MethodReference(new TypeRef(DelegateClassName), DelegateInvokeMethod, TypeRef.Int32, pt));
                    }
                    break;
                }

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
                else if (call.Symbol is FunctionDeclaration extFunc && extFunc.IsExtension)
                {
                    // Extension method call: obj.method(args) → Type.Method(obj, args).
                    // The receiver is the first argument to the static method.
                    var returnType = extFunc.ReturnType != null
                        ? MapType(extFunc.ReturnType)
                        : TypeRef.Int32;
                    var paramTypes = extFunc.Parameters.Select(p => MapType(p.Type)).ToList();
                    // The extension method's first parameter is the receiver type;
                    // we already pushed the receiver above via the argument loop.
                    var target = new MethodReference(
                        new TypeRef(extFunc.ExtensionTargetType ?? "TODO"),
                        extFunc.Name,
                        returnType,
                        paramTypes);
                    ib.Call(target);
                }
                else if (call.Symbol is FunctionDeclaration instanceFunc && instanceFunc.IsInstance)
                {
                    // Instance method call: c.method(args). The receiver was
                    // pushed before the arguments above (this is param 0).
                    var returnType = instanceFunc.ReturnType != null
                        ? MapType(instanceFunc.ReturnType)
                        : TypeRef.Int32;
                    var paramTypes = instanceFunc.Parameters.Select(p => MapType(p.Type)).ToList();
                    // Prepend 'this' parameter type so the VM can match the
                    // full method signature (this + params).
                    paramTypes.Insert(0, TypeRef.Object);
                    // Native-bound contracts dispatch to the host module with the
                    // receiver (an external object handle) as argument 0.
                    bool isNativeBinding = _nativeBindings.TryGetValue(instanceFunc.ContractName ?? "", out var instBinding);
                    string instanceTarget = isNativeBinding
                        ? instBinding
                        : ResolveTypeName(instanceFunc.ContractName ?? "TODO");
                    // Generic contract instance: b.get() where b: Box<int> — the
                    // target is the materialized name (Box<int32>).
                    if (call.Callee is MemberExpression recvMem
                        && recvMem.Object is IdentifierExpression recvId
                        && _variableTypes.TryGetValue(recvId.Name, out var recvType)
                        && recvType is TypeDescriptor.GenericInstance recvGen
                        && IsUserGenericContract(recvGen.Name))
                    {
                        instanceTarget = MaterializedContractName(recvGen);
                    }
                    if (isNativeBinding)
                    {
                        // The VM pops args in reverse, so the receiver (pushed
                        // last) lands at the END of the native call's argument
                        // list. Append its type so the call's argc includes it.
                        paramTypes.Add(TypeRef.Object);
                    }
                    // callvirt lets the runtime refine the target through the
                    // RECEIVER's concrete type chain (virtual dispatch) — this
                    // is what makes base/interface-typed variables dispatch to
                    // the most-derived override (FEATURE_PROPOSALS §6).
                    ib.Callvirt(new MethodReference(new TypeRef(instanceTarget), instanceFunc.Name, returnType, paramTypes));
                }
                else if (call.Symbol is FunctionDeclaration staticFunc && staticFunc.IsStatic)
                {
                    // Static method on a contract: Contract::Method(...) or
                    // Contract.Method(...). No receiver to push.
                    var returnType = staticFunc.ReturnType != null
                        ? MapType(staticFunc.ReturnType)
                        : TypeRef.Int32;
                    var paramTypes = staticFunc.Parameters.Select(p => MapType(p.Type)).ToList();
                    // NativeBinding → host module name; ClrImport → CLR type name
                    // (resolved by reflection at runtime); else the contract's
                    // own wire name (a normal module function). Generic statics
                    // (Box::wrap(7)) target the materialized name (Box<int32>).
                    string staticTarget = _nativeBindings.TryGetValue(staticFunc.ContractName ?? "", out var statBinding)
                        ? statBinding
                        : _clrImports.TryGetValue(staticFunc.ContractName ?? "", out var clrTarget)
                            ? clrTarget
                            : staticFunc.TypeArguments.Count > 0
                                ? MaterializedContractName(staticFunc.ContractName ?? "", staticFunc.TypeArguments)
                                : ResolveTypeName(staticFunc.ContractName ?? "Global");
                    ib.Call(new MethodReference(new TypeRef(staticTarget), staticFunc.Name, returnType, paramTypes));
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
                else if (NativeBindingFor(newExpr.TypeName) is string nativeBinding)
                {
                    // Native-bound contract: new Window() constructs the host
                    // object through the module's Create method. The call's
                    // result is an external object handle, pushed on the stack.
                    ib.Call(new MethodReference(new TypeRef(nativeBinding), "Create", TypeRef.Object, new List<TypeRef>()));
                }
                else
                {
                    // new Type() / new Type(args) — allocate, then run the
                    // constructor so field initializers execute. The VM's newobj
                    // only allocates; the ctor (this + params, named ".ctor") is
                    // invoked explicitly. Stack: [obj] → [obj,obj] → [obj,obj,args...]
                    // → call pops in reverse into locals → ret pushes nil → pop.
                    // Generic instantiation (new Box<int>(5)) allocates the
                    // materialized name (Box<int32>) and substitutes the ctor's
                    // type params.
                    var typeParamMap = newExpr.TypeArguments.Count > 0
                        ? BuildTypeParamMap(newExpr.TypeName, newExpr.TypeArguments)
                        : null;
                    var qualifiedNewType = typeParamMap != null
                        ? MaterializedContractName(newExpr.TypeName, newExpr.TypeArguments)
                        : ResolveTypeName(newExpr.TypeName);
                    ib.Newobj(new TypeRef(qualifiedNewType));
                    var contract = _program?.Contracts.FirstOrDefault(c => c.Name == newExpr.TypeName || c.FullName == newExpr.TypeName);
                    var ctor = contract?.Constructors.FirstOrDefault(c => c.Parameters.Count == newExpr.Arguments.Count);
                    if (ctor != null)
                    {
                        ib.Dup();   // the receiver — pushed FIRST (it is param 0)
                        foreach (var arg in newExpr.Arguments)
                            GenerateExpression(ib, arg, paramMap);
                        var argTypes = ctor.Parameters
                            .Select(p => typeParamMap != null ? SubstituteTypeParams(p.Type, typeParamMap) : p.Type)
                            .Select(MapType)
                            .ToList();
                        ib.Call(new MethodReference(
                            new TypeRef(qualifiedNewType),
                            ".ctor",
                            TypeRef.Void,
                            new List<TypeRef> { TypeRef.Object }.Concat(argTypes).ToList()));
                        ib.Pop();
                    }
                    else if (FindStruct(newExpr.TypeName) is StructDeclaration structDecl)
                    {
                        // new Color(255, 0, 0, 255) — allocate, then assign
                        // fields positionally. Stack: [obj] → dup → [obj,obj] →
                        // push value → [obj,obj,v] → stfld → [obj]; the object
                        // stays as the expression result.
                        int n = Math.Min(structDecl.Fields.Count, newExpr.Arguments.Count);
                        for (int fi = 0; fi < n; fi++)
                        {
                            ib.Dup();
                            GenerateExpression(ib, newExpr.Arguments[fi], paramMap);
                            ib.Stfld(new FieldReference(new TypeRef(qualifiedNewType), structDecl.Fields[fi].Name, MapType(structDecl.Fields[fi].Type)));
                        }
                    }
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

            case TupleLiteralExpression tupleLit:
                // A tuple (a, b, c) lowers to an object[] — the multi-value
                // return representation. Build the array like an array literal.
                ib.LdcI4(tupleLit.Elements.Count);
                ib.Newarr(TypeRef.Object);
                for (int elemIdx = 0; elemIdx < tupleLit.Elements.Count; elemIdx++)
                {
                    ib.Dup();
                    ib.LdcI4(elemIdx);
                    GenerateExpression(ib, tupleLit.Elements[elemIdx], paramMap);
                    ib.Stelem();
                }
                break;
                
            case MemberExpression mem:
                {
                    var memObjName = ResolveExpressionObjectType(mem.Object);
                    // Enum member read: Color.Red or com.lib.Direction.North folds
                    // to its zero-based index (try the full dotted path first).
                    var enumIndex = TryGetTypePath(mem, out var typePath)
                        ? FindEnumMemberIndex(typePath, mem.Property)
                        : FindEnumMemberIndex(memObjName, mem.Property);
                    if (enumIndex >= 0)
                    {
                        ib.LdcI4(enumIndex);
                        break;
                    }
                    if (IsStaticField(memObjName, mem.Property))
                    {
                        // Comptime constant read (Config.APP_NAME): folded literal (§15).
                        if (TryFoldConstField(memObjName, mem.Property, out var memConst))
                        {
                            EmitLiteralValue(ib, memConst);
                            break;
                        }
                        // Static field read (Config.count) — no receiver to push.
                        // Key by the DECLARING type so inherited statics resolve.
                        var staticDecl = FieldDeclaringTypeName(memObjName, mem.Property, staticOnly: true) ?? memObjName;
                        ib.Ldsfld(new FieldReference(new TypeRef(staticDecl), mem.Property, FindFieldType(staticDecl, mem.Property)));
                        break;
                    }

                    // Sum-type variant used as a value: `Shape.Unit` — a bare
                    // reference to the zero-arg factory. Emit its call.
                    var sumOwner = _program?.Contracts.FirstOrDefault(c =>
                        (c.Name == memObjName || c.FullName == memObjName) && c.IsSumTypeBase);
                    if (sumOwner != null && sumOwner.SumVariants.Contains(mem.Property))
                    {
                        var factory = sumOwner.Members.OfType<FunctionDeclaration>()
                            .FirstOrDefault(f => f.Name == mem.Property && f.IsStatic);
                        var fRet = factory?.ReturnType != null && !factory.ReturnType.IsEmpty
                            ? MapType(factory.ReturnType)
                            : TypeRef.Object;
                        ib.Call(new MethodReference(
                            new TypeRef(ResolveTypeName(sumOwner.FullName)), mem.Property, fRet, new List<TypeRef>()));
                        break;
                    }
                    GenerateExpression(ib, mem.Object, paramMap);
                    if (mem.Property is "Length" or "Count")
                    {
                        // Array length
                        ib.Ldlen();
                    }
                    else
                    {
                        // Key by the DECLARING type (Animal.name, not Dog.name).
                        var instDecl = FieldDeclaringTypeName(memObjName, mem.Property, staticOnly: false) ?? memObjName;
                        var ftype = FindFieldType(instDecl, mem.Property);
                        ib.Ldfld(new FieldReference(new TypeRef(instDecl), mem.Property, ftype));
                    }
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

            case ScopedAccessExpression scopedExpr:
                // Contract::field — a static field read (contract static methods
                // are resolved through the call path, not here). Key by the
                // DECLARING type so inherited statics (Dog::count → Animal::count) resolve.
                if (IsStaticField(scopedExpr.Module, scopedExpr.Member))
                {
                    // Comptime constant read (Config::APP_NAME): folded literal (§15).
                    if (TryFoldConstField(scopedExpr.Module, scopedExpr.Member, out var scopedConst))
                    {
                        EmitLiteralValue(ib, scopedConst);
                    }
                    else
                    {
                        var scopedTarget = FieldDeclaringTypeName(scopedExpr.Module, scopedExpr.Member, staticOnly: true)
                            ?? ResolveTypeName(scopedExpr.Module);
                        ib.Ldsfld(new FieldReference(new TypeRef(scopedTarget), scopedExpr.Member, FindFieldType(scopedTarget, scopedExpr.Member)));
                    }
                }
                else
                {
                    // Enum member: Color::Red folds to its index.
                    var enumIdx = FindEnumMemberIndex(scopedExpr.Module, scopedExpr.Member);
                    if (enumIdx >= 0)
                        ib.LdcI4(enumIdx);
                }
                break;
        }
    }

    private TypeRef MapType(string type) => MapType(TypeDescriptor.Parse(type));

    private TypeRef MapType(TypeDescriptor type) => type switch
    {
        TypeDescriptor.Named n => MapNamedType(n.Name),
        TypeDescriptor.ArrayOf a => new TypeRef($"{MapType(a.Element).Name}[]"),
        // Function types are represented as object handles in the wire model
        // (a delegate IS an object).
        TypeDescriptor.Function => TypeRef.Object,
        // A tuple return type (bool, object) is an object[] on the wire.
        TypeDescriptor.Tuple => new TypeRef("object[]"),
        // Delegate<T> is the typed delegate wrapper — it materialises as the
        // runtime's Delegate class.
        TypeDescriptor.GenericInstance g when TryGetDelegateFunctionType(g, out _) => new TypeRef(DelegateClassName),
        // User generic contracts (Box<int>) materialize to their concrete name
        // (Box<int32>); stdlib generics (List/Dict) stay erased object handles.
        TypeDescriptor.GenericInstance g when IsUserGenericContract(g.Name) => new TypeRef(MaterializedContractName(g)),
        // Generic instances are type-erased: the runtime sees the object-backed
        // collection class (List/Dict), so the wire type is object.
        TypeDescriptor.GenericInstance => TypeRef.Object,
        _ => TypeRef.Int32
    };

    /// <summary>True when <paramref name="name"/> is a user-declared generic contract.</summary>
    private bool IsUserGenericContract(string name)
        => _contractTypeParams.ContainsKey(name);

    // ── Match expressions (FEATURE_PROPOSALS §1, §2) ───────────────

    private int _matchTempCounter;

    /// <summary>
    /// Lowers <c>for x in iterable</c> (FEATURE_PROPOSALS §5). Ranges desugar
    /// to the C-style loop; arrays use ldlen/ldelem, List&lt;T&gt; uses
    /// List.Count/List.Get, and Dict&lt;K,V&gt; iterates Dict.Keys with Dict.Get
    /// for the pair form. All protocols evaluate the iterable once into temps:
    /// <code>
    /// __coll = iterable;  __len = count(__coll) [or keys];  __i = 0;
    /// while (__i &lt; __len) { item = __coll[__i] (+ v = Get(k)); body; __i += 1 }
    /// </code>
    /// </summary>
    private void GenerateForInStatement(InstructionBuilder ib, Contract.Compiler.AST.ForInStatement forIn, Dictionary<string, int> paramMap)
    {
        int temp = ++_matchTempCounter;
        string collName = $"__forArr_{temp}";
        string lenName = $"__forLen_{temp}";
        string idxName = $"__forIdx_{temp}";
        string dictName = $"__forDict_{temp}";

        // ── Range: desugar straight to the C-style loop ──
        if (forIn.Iterable is RangeExpression range)
        {
            string startName = $"__rngStart_{temp}";
            string endName = $"__rngEnd_{temp}";
            string stepName = $"__rngStep_{temp}";
            ib.Local(startName, TypeRef.Int32);
            ib.Local(endName, TypeRef.Int32);
            ib.Local(stepName, TypeRef.Int32);
            EnsureLocal(ib, forIn.Variable, TypeRef.Int32);

            GenerateExpression(ib, range.Start, paramMap);
            ib.Stloc(startName);
            GenerateExpression(ib, range.End, paramMap);
            ib.Stloc(endName);
            if (range.Step != null)
                GenerateExpression(ib, range.Step, paramMap);
            else
                ib.LdcI4(1);
            ib.Stloc(stepName);

            // var i = start;
            var initBlock = new Contract.Compiler.AST.BlockStatement(forIn.Line, forIn.Column);
            initBlock.Statements.Add(new VariableDeclaration(
                forIn.Variable, new TypeDescriptor.Named("int"),
                new IdentifierExpression(startName, forIn.Line, forIn.Column),
                forIn.Line, forIn.Column));

            // (step > 0) ? i <=|< end : i >=|> end
            int l = forIn.Line, c2 = forIn.Column;
            var stepPositive = new BinaryExpression(
                new IdentifierExpression(stepName, l, c2), ">", new LiteralExpression(0, l, c2), l, c2);
            var forward = new BinaryExpression(
                new IdentifierExpression(forIn.Variable, l, c2),
                range.Inclusive ? "<=" : "<",
                new IdentifierExpression(endName, l, c2), l, c2);
            var backward = new BinaryExpression(
                new IdentifierExpression(forIn.Variable, l, c2),
                range.Inclusive ? ">=" : ">",
                new IdentifierExpression(endName, l, c2), l, c2);
            var condition = new TernaryExpression(stepPositive, forward, backward, l, c2);

            // i += step
            var update = new BinaryExpression(
                new IdentifierExpression(forIn.Variable, l, c2), "+=",
                new IdentifierExpression(stepName, l, c2), l, c2);

            var forStmt = new ForStatement(initBlock, condition, update, forIn.Body, l, c2);
            GenerateStatement(ib, forStmt, paramMap);
            return;
        }

        bool isDict = forIn.IsDictionary;

        // The bound element's local type: typed for arrays of named types,
        // object otherwise.
        TypeRef elemRef = TypeRef.Object;
        if (!isDict && forIn.ResolvedElementType is TypeDescriptor.ArrayOf { Element: TypeDescriptor.Named an } && !an.IsEmpty)
            elemRef = MapType(forIn.ResolvedElementType);

        ib.Local(collName, TypeRef.Object);
        ib.Local(lenName, TypeRef.Int32);
        ib.Local(idxName, TypeRef.Int32);
        if (isDict) ib.Local(dictName, TypeRef.Object);
        EnsureLocal(ib, forIn.Variable, elemRef);
        if (isDict && forIn.ValueVariable != null)
            EnsureLocal(ib, forIn.ValueVariable, TypeRef.Object);

        // __coll = iterable (and __dict = d for the pair form).
        GenerateExpression(ib, forIn.Iterable, paramMap);
        if (isDict)
        {
            ib.Dup();
            ib.Stloc(dictName);
        }
        ib.Stloc(collName);

        // __len = count: arrays → ldlen; List → List.Count; Dict → len(Dict.Keys(d)).
        // Protocol calls go straight to the stdlib bindings — these nodes are
        // synthesized here (post-analysis), so they carry no resolver symbols.
        if (!isDict && forIn.IsArray)
        {
            ib.Ldloc(collName);
            ib.Ldlen();
        }
        else if (!isDict)
        {
            ib.Ldloc(collName);
            ib.Call(ListCountReference);
        }
        else
        {
            ib.Ldloc(dictName);
            ib.Call(DictKeysReference);
            ib.Stloc(collName);   // the temp now holds the keys array
            ib.Ldloc(collName);
            ib.Ldlen();
        }
        ib.Stloc(lenName);

        // __i = 0, then the loop condition.
        ib.LdcI4(0);
        ib.Stloc(idxName);
        ib.Ldloc(idxName);
        ib.Ldloc(lenName);
        ib.Clt();

        _loopContinueEmitters.Push(body =>
        {
            GenerateLoopIncrement(body, idxName);
            body.Ldloc(idxName);
            body.Ldloc(lenName);
            body.Clt();
        });

        ib.While("stack", body =>
        {
            // Runtime while(stack) convention: the condition was duped — drop
            // our copy; the trailing recompute below feeds the next test.
            body.Pop();

            // Bind the iteration variable(s).
            if (!isDict)
            {
                if (forIn.IsArray)
                {
                    body.Ldloc(collName);
                    body.Ldloc(idxName);
                    body.Ldelem();
                }
                else
                {
                    body.Ldloc(collName);
                    body.Ldloc(idxName);
                    body.Call(ListGetReference);
                }
                body.Stloc(forIn.Variable);
            }
            else
            {
                // k = keys[i]; v = Dict.Get(d, k)
                body.Ldloc(collName);
                body.Ldloc(idxName);
                body.Ldelem();
                body.Stloc(forIn.Variable);

                body.Ldloc(dictName);
                body.Ldloc(forIn.Variable);
                body.Call(DictGetReference);
                body.Stloc(forIn.ValueVariable!);
            }

            GenerateStatement(body, forIn.Body, paramMap);
            GenerateLoopIncrement(body, idxName);

            // Recompute the condition for the next test.
            body.Ldloc(idxName);
            body.Ldloc(lenName);
            body.Clt();
        });
        _loopContinueEmitters.Pop();
        ib.Pop();   // consume the loop's final condition value

        // For the Dict path we need the ORIGINAL dict for Get calls — keep it
        // in its own temp alongside the keys array.
    }

    private void GenerateLoopIncrement(InstructionBuilder body, string idxName)
    {
        body.Ldloc(idxName);
        body.LdcI4(1);
        body.Add();
        body.Stloc(idxName);
    }


    /// <summary>
    /// Lowers a match expression (FEATURE_PROPOSALS §1): the scrutinee is
    /// evaluated once into a hidden temp, then arms become a nested if-chain —
    /// each arm's test (pattern + guard, short-circuited) picks between the
    /// arm's value and the next arm. The final arm runs unconditionally.
    /// Every path leaves exactly one value on the stack.
    /// </summary>
    private void GenerateMatchExpression(InstructionBuilder ib, MatchExpression matchExpr, Dictionary<string, int> paramMap)
    {
        string tmpName = $"__match_{_matchTempCounter++}";
        ib.Local(tmpName, TypeRef.Object);

        GenerateExpression(ib, matchExpr.Scrutinee, paramMap);
        ib.Stloc(tmpName);

        EmitMatchArms(ib, matchExpr.Arms, 0, tmpName, paramMap);
    }

    /// <summary>Emits the if-chain for arms[index..]; leaves one value on the stack.</summary>
    private void EmitMatchArms(InstructionBuilder ib, List<MatchArm> arms, int index, string tmpName, Dictionary<string, int> paramMap)
    {
        var arm = arms[index];
        bool isLast = index == arms.Count - 1;

        if (isLast)
        {
            EmitMatchArmBinding(ib, arm, tmpName, paramMap);
            GenerateExpression(ib, arm.Result, paramMap);
            return;
        }

        EmitMatchArmTest(ib, arm, tmpName, paramMap);
        ib.If("stack",
            then =>
            {
                EmitMatchArmBinding(then, arm, tmpName, paramMap);
                GenerateExpression(then, arm.Result, paramMap);
            },
            els => EmitMatchArms(els, arms, index + 1, tmpName, paramMap));
    }

    /// <summary>
    /// Emits an arm's combined test condition: pattern match AND guard,
    /// short-circuited. Leaves exactly one bool on the stack.
    /// </summary>
    private void EmitMatchArmTest(InstructionBuilder ib, MatchArm arm, string tmpName, Dictionary<string, int> paramMap)
    {
        EmitPatternDisjunction(ib, arm.Patterns, tmpName, paramMap);

        if (arm.Guard != null)
        {
            ib.If("stack",
                then =>
                {
                    EmitMatchArmBinding(then, arm, tmpName, paramMap);
                    GenerateExpression(then, arm.Guard!, paramMap);
                },
                els => els.LdcI4(0));
        }
    }

    /// <summary>Or-patterns: any-of disjunction over the same scrutinee temp.</summary>
    private void EmitPatternDisjunction(InstructionBuilder ib, List<MatchPattern> patterns, string tmpName, Dictionary<string, int> paramMap)
    {
        var concrete = patterns.Where(p => p is not WildcardPattern && p is not BindingPattern).ToList();
        bool hasCatchAll = concrete.Count < patterns.Count;

        if (hasCatchAll)
        {
            // A wildcard/binding in the or-group always matches; still bind
            // before the guard via EmitMatchArmBinding.
            ib.LdcI4(1);
            return;
        }

        // p1 || p2 || ... — nested ifs producing a single bool.
        void EmitLevel(InstructionBuilder b, int i)
        {
            EmitSinglePatternTest(b, concrete[i], tmpName, paramMap);
            if (i == concrete.Count - 1) return;
            b.If("stack",
                t => t.LdcI4(1),
                e => EmitLevel(e, i + 1));
        }
        EmitLevel(ib, 0);
    }

    /// <summary>The test for ONE pattern against the scrutinee temp: pushes a bool.</summary>
    private void EmitSinglePatternTest(InstructionBuilder ib, MatchPattern pattern, string tmpName, Dictionary<string, int> paramMap)
    {
        switch (pattern)
        {
            case LiteralPattern lit:
                ib.Ldloc(tmpName);
                EmitLiteralValue(ib, lit.Value);
                ib.Ceq();
                break;

            case VariantPattern vp:
                // Tag test: scrutinee.__tag == variant index.
                ib.Ldloc(tmpName);
                var baseDecl = FindSumBase(vp);
                var tagOwner = baseDecl != null ? ResolveTypeName(baseDecl.FullName) : ResolveTypeName(vp.ResolvedVariantName ?? "");
                ib.Ldfld(new FieldReference(new TypeRef(tagOwner), "__tag", TypeRef.Int32));
                ib.LdcI4(vp.VariantIndex);
                ib.Ceq();
                break;

            default:
                // Binding/wildcard handled by the catch-all path.
                ib.LdcI4(1);
                break;
        }
    }

    /// <summary>
    /// Binds an arm's pattern variables (scrutinee bindings and variant field
    /// destructurings) into locals so the guard/result can read them. No-ops
    /// when the arm binds nothing.
    /// </summary>
    private void EmitMatchArmBinding(InstructionBuilder ib, MatchArm arm, string tmpName, Dictionary<string, int> paramMap)
    {
        foreach (var pattern in arm.Patterns)
        {
            switch (pattern)
            {
                case BindingPattern bind:
                    EnsureLocal(ib, bind.Name, TypeRef.Object);
                    ib.Ldloc(tmpName);
                    ib.Stloc(bind.Name);
                    break;

                case VariantPattern vp when vp.BoundFields.Count > 0:
                    var variantType = ResolveTypeName(vp.ResolvedVariantName ?? "");
                    for (int i = 0; i < vp.Arguments.Count && i < vp.BoundFields.Count; i++)
                    {
                        if (vp.Arguments[i] is BindingPattern fieldBind)
                        {
                            var fieldType = FindVariantFieldType(vp, vp.BoundFields[i]);
                            EnsureLocal(ib, fieldBind.Name, fieldType);
                            ib.Ldloc(tmpName);
                            ib.Ldfld(new FieldReference(new TypeRef(variantType), vp.BoundFields[i], fieldType));
                            ib.Stloc(fieldBind.Name);
                        }
                        else if (vp.Arguments[i] is VariantPattern nested)
                        {
                            _diagnostics.AddError("Nested variant patterns are not supported yet", nested.Line, nested.Column);
                        }
                        else if (vp.Arguments[i] is LiteralPattern badLit)
                        {
                            _diagnostics.AddError("Literal patterns inside variants are not supported yet", badLit.Line, badLit.Column);
                        }
                    }
                    break;
            }
        }
    }

    private void EmitLiteralValue(InstructionBuilder ib, object? value)
    {
        if (value is int i) ib.LdcI4(i);
        else if (value is string s) ib.Ldstr(s);
        else if (value is bool b) ib.LdcI4(b ? 1 : 0);
        else if (value is double d) ib.LdcR8(d);
        else if (value == null) ib.Ldnull();
    }

    /// <summary>Declares a local once per method (match arms can rebind names).</summary>
    private readonly HashSet<string> _declaredLocals = new();

    private void EnsureLocal(InstructionBuilder ib, string name, TypeRef type)
    {
        if (_declaredLocals.Add(name))
            ib.Local(name, type);
    }

    private ContractDeclaration? FindSumBase(VariantPattern vp)
    {
        var variantContract = _program?.Contracts.FirstOrDefault(c =>
            c.FullName == vp.ResolvedVariantName || c.Name == vp.VariantName);
        return variantContract?.SumTypeOf != null
            ? _program?.Contracts.FirstOrDefault(c => c.Name == variantContract.SumTypeOf || c.FullName == variantContract.SumTypeOf)
            : null;
    }

    private TypeRef FindVariantFieldType(VariantPattern vp, string fieldName)
    {
        var variantContract = _program?.Contracts.FirstOrDefault(c =>
            c.FullName == vp.ResolvedVariantName || c.Name == vp.VariantName);
        var field = variantContract?.Fields.FirstOrDefault(f => f.Name == fieldName);
        return field != null ? MapType(field.Type) : TypeRef.Object;
    }

    /// <summary>
    /// The materialized wire name of a generic instantiation: <c>Box&lt;int&gt;</c>
    /// → <c>Box&lt;int32&gt;</c> (wire names use int32/int64/r8, not language names).
    /// </summary>
    private string MaterializedContractName(TypeDescriptor.GenericInstance g)
    {
        var baseName = ResolveTypeName(g.Name);
        var args = g.Arguments.Select(a => MapType(a).Name);
        return $"{baseName}<{string.Join(", ", args)}>";
    }

    /// <summary>Materialized name from a written type name + explicit type args
    /// (<c>new Box&lt;int&gt;(5)</c> → <c>Box&lt;int32&gt;</c>).</summary>
    private string MaterializedContractName(string typeName, List<TypeDescriptor> typeArgs)
    {
        var baseName = ResolveTypeName(typeName);
        var args = typeArgs.Select(a => MapType(a).Name);
        return $"{baseName}<{string.Join(", ", args)}>";
    }

    /// <summary>Type-parameter map from a written type name + explicit type args
    /// (<c>Box&lt;int&gt;</c> → <c>{ T → int }</c>).</summary>
    private Dictionary<string, TypeDescriptor>? BuildTypeParamMap(string typeName, List<TypeDescriptor> typeArgs)
    {
        if (!_contractTypeParams.TryGetValue(typeName, out var paramsSet)) return null;
        var map = new Dictionary<string, TypeDescriptor>();
        int i = 0;
        foreach (var p in paramsSet)
        {
            if (i < typeArgs.Count) map[p] = typeArgs[i];
            i++;
        }
        return map;
    }

    /// <summary>
    /// True when <paramref name="g"/> is <c>Delegate&lt;F&gt;</c>; exposes the
    /// wrapped function type F.
    /// </summary>
    private static bool TryGetDelegateFunctionType(TypeDescriptor.GenericInstance g, out TypeDescriptor.Function functionType)
    {
        functionType = null!;
        if (string.Equals(g.Name, "Delegate", StringComparison.OrdinalIgnoreCase)
            && g.Arguments.Count == 1
            && g.Arguments[0] is TypeDescriptor.Function f)
        {
            functionType = f;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The function type of the delegate a call produces, when that result is a
    /// function type. Drives <c>f()(args)</c>: the inner call must produce a
    /// delegate whose signature the outer invocation uses.
    /// </summary>
    private TypeDescriptor.Function? GetCallFunctionType(CallExpression call)
    {
        // f() where the resolved declaration's return type is a function type.
        if (call.Symbol is FunctionDeclaration fd && fd.ReturnType is TypeDescriptor.Function fn)
            return fn;

        // Calling a function-typed local/param: the value's function type is the
        // type of the call RESULT (a delegate), so the delegate produced by the
        // call has the value type's return.
        if (call.Callee is IdentifierExpression id)
        {
            if (_functionTypedLocals.TryGetValue(id.Name, out var fl) && fl.Return is TypeDescriptor.Function nestedL)
                return nestedL;
            if (_functionTypedParams.TryGetValue(id.Name, out var fp) && fp.Return is TypeDescriptor.Function nestedP)
                return nestedP;
            if (_variableTypes.TryGetValue(id.Name, out var vt) && vt is TypeDescriptor.Function f1 && f1.Return is TypeDescriptor.Function nestedV)
                return nestedV;
            if (_variableTypes.TryGetValue(id.Name, out var vt2)
                && vt2 is TypeDescriptor.GenericInstance g
                && TryGetDelegateFunctionType(g, out var gf)
                && gf.Return is TypeDescriptor.Function nestedG)
            {
                return nestedG;
            }
        }

        return null;
    }

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

    private TypeRef MapNamedType(string type)
    {
        if (string.IsNullOrEmpty(type)) return TypeRef.Int32;
        // Function-level type params erase to object (a function is a single
        // runtime entity); contract type params stay literal (the VM
        // substitutes them during materialization).
        if (_currentFnTypeParams.Contains(type)) return TypeRef.Object;
        if (_currentTypeParams.Contains(type)) return new TypeRef(type);
        return type.ToLower() switch {
            "string" => TypeRef.String,
            "void" => TypeRef.Void,
            "bool" => TypeRef.Bool,
            "int" => TypeRef.Int32,
            "int64" => TypeRef.Int64,
            "long" => TypeRef.Int64,
            "object" => TypeRef.Object,
            "double" => TypeRef.Float64,
            "float" => TypeRef.Float32,
            // Extended integer widths: the VM stores every integer as I4, but the
            // wire name keeps the native C width so DllImport signatures and
            // interop struct fields marshal as their true C types.
            "byte" => new TypeRef("uint8"),
            "sbyte" => new TypeRef("int8"),
            "short" => new TypeRef("int16"),
            "ushort" => new TypeRef("uint16"),
            "uint" => new TypeRef("uint32"),
            _ => new TypeRef(ResolveTypeName(type))
        };
    }

    // ── Namespace resolution ────────────────────────────────────────

    private void AddTypeIndex(string shortName, string fullName)
    {
        _qualifiedTypeNames.Add(fullName);
        if (!_shortToFull.TryGetValue(shortName, out var list))
            _shortToFull[shortName] = list = new List<string>();
        if (!list.Contains(fullName)) list.Add(fullName);
    }

    /// <summary>True when a declared type has this (possibly qualified) name.</summary>
    private bool HasType(string name) => _qualifiedTypeNames.Contains(name);

    /// <summary>
    /// Resolves a possibly-short type name to its fully-qualified wire name:
    /// the current contract's namespace is preferred, then namespace imports
    /// (mapping the first segment of dotted names too — `Terminal.Terminal`
    /// with `import ovh.finite.hello.Terminal;` → `ovh.finite.hello.Terminal.Terminal`),
    /// then a unique short-name match. Falls back to the name unchanged.
    /// </summary>
    private string ResolveTypeName(string name)
    {
        string resolved = TypeNameResolver.Resolve(
            name,
            _program?.NamespaceImports ?? (IReadOnlyList<string>)Array.Empty<string>(),
            HasType,
            CurrentContractNamespace(),
            UniqueShortMatch);

        // The analyzer's registry is case-insensitive; the emitted wire names
        // must match the declared types exactly, so canonicalize case
        // (Raylib.color → Raylib.Color).
        foreach (var t in _qualifiedTypeNames)
            if (string.Equals(t, resolved, StringComparison.OrdinalIgnoreCase))
                return t;
        return resolved;
    }

    /// <summary>The namespace of the contract currently being generated, if any.</summary>
    private string? CurrentContractNamespace()
    {
        if (_currentContractName == null || _program == null) return null;
        return _program.Contracts.FirstOrDefault(c => c.Name == _currentContractName)?.Namespace;
    }

    /// <summary>The single qualified name for a short name, or null when ambiguous/absent.</summary>
    private string? UniqueShortMatch(string shortName)
        => _shortToFull.TryGetValue(shortName, out var list) && list.Count == 1 ? list[0] : null;

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

    /// <summary>The host module name for a native-bound contract type, or null.</summary>
    private string? NativeBindingFor(string typeName)
    {
        if (_program != null)
        {
            foreach (var c in _program.Contracts)
            {
                if ((string.Equals(c.Name, typeName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    && c.NativeBindingName != null)
                    return c.NativeBindingName;
            }
        }
        return null;
    }

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

    // for-in index protocol (§5) — direct stdlib bindings, emitted without
    // resolver symbols because they are synthesized post-analysis.
    private static readonly MethodReference ListCountReference = new(
        new TypeRef("List"), "Count", TypeRef.Int32,
        new List<TypeRef> { TypeRef.Object });
    private static readonly MethodReference ListGetReference = new(
        new TypeRef("List"), "Get", TypeRef.Object,
        new List<TypeRef> { TypeRef.Object, TypeRef.Int32 });
    private static readonly MethodReference DictKeysReference = new(
        new TypeRef("Dict"), "Keys", TypeRef.Object,
        new List<TypeRef> { TypeRef.Object });
    private static readonly MethodReference DictGetReference = new(
        new TypeRef("Dict"), "Get", TypeRef.Object,
        new List<TypeRef> { TypeRef.Object, TypeRef.Object });

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
        // module-level functions live in the generated Global class. The
        // declaring contract name is resolved through namespaces/imports to its
        // wire name (classes are emitted fully qualified).
        var declaringType = func?.ContractName != null
            ? ResolveTypeName(func.ContractName)
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

    // ── Design-by-contract codegen ─────────────────────────────────

    /// <summary>
    /// Emits requires checks at function/constructor entry. Each condition is
    /// evaluated; if false, a fault string is thrown.
    /// </summary>
    private void EmitRequiresChecks(InstructionBuilder ib, List<RequiresClause> requires, Dictionary<string, int> paramMap)
    {
        foreach (var req in requires)
        {
            GenerateExpression(ib, req.Condition, paramMap);
            ib.If("stack",
                then => { /* condition true: continue */ },
                els =>
                {
                    els.Ldstr($"requires: {GetSourceLine(req.Line)?.Trim() ?? "contract violation"}");
                    els.Throw();
                });
        }
    }

    /// <summary>
    /// Emits ensures checks before the implicit return. The return value is
    /// captured in a temp, the ensures condition is checked, and if it fails
    /// a fault is thrown. The temp is then reloaded for the actual return.
    /// </summary>
    private void EmitEnsuresChecks(InstructionBuilder ib, List<EnsuresClause> ensures, TypeRef returnType, Dictionary<string, int> paramMap)
    {
        if (ensures.Count == 0) return;
        if (returnType == TypeRef.Void) return;

        // Capture the default return value in a temp
        string tempName = $"__ensures_result_{_lambdaCounter++}";
        EnsureLocal(ib, tempName, returnType);

        // The implicit return already loaded a default value onto the stack.
        // Store it in the temp.
        ib.Stloc(tempName);

        foreach (var ens in ensures)
        {
            // Set up 'result' local with the temp value for the ensures expression
            EnsureLocal(ib, "result", returnType);
            ib.Ldloc(tempName);
            ib.Stloc("result");

            // Generate the ensures condition
            GenerateExpression(ib, ens.Condition, paramMap);

            ib.If("stack",
                then => { /* condition true: continue */ },
                els =>
                {
                    els.Ldstr($"ensures: {GetSourceLine(ens.Line)?.Trim() ?? "contract violation"}");
                    els.Throw();
                });
        }

        // Reload the temp for the actual return
        ib.Ldloc(tempName);
    }

    /// <summary>
    /// Emits invariant checks for a contract. Each invariant condition is
    /// evaluated with 'this' on the stack; if false, a fault is thrown.
    /// </summary>
    private void EmitInvariantChecks(InstructionBuilder ib, ContractDeclaration contract, Dictionary<string, int> paramMap)
    {
        foreach (var inv in contract.Invariants)
        {
            foreach (var cond in inv.Conditions)
            {
                var endLabel = $"__invariant_end_{_lambdaCounter++}";

                // Load 'this' for the invariant expression
                if (paramMap.TryGetValue("this", out int thisIdx))
                    ib.Ldarg(thisIdx);

                GenerateExpression(ib, cond, paramMap);

                ib.If("stack",
                    then => { /* condition true: continue */ },
                    els =>
                    {
                        els.Ldstr($"invariant on '{contract.Name}' violated");
                        els.Throw();
                    });
            }
        }
    }
}
