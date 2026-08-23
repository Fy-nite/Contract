using System;
using System.Collections.Generic;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.Semantics
{
    public class SemanticAnalyzer
    {
        private readonly SymbolTable _symbolTable;
        private readonly DiagnosticBag _diagnostics;
        private readonly TypeRegistry _typeRegistry = new();
        private readonly Stack<Dictionary<string, VariableDeclaration>> _scopes = new();
        /// <summary>Parallel to <see cref="_scopes"/>: tracks which variables in each scope are mutable (var) vs immutable (let).</summary>
        private readonly Stack<Dictionary<string, bool>> _varMutability = new();
        /// <summary>In-scope generic type parameters (contract + function bodies).</summary>
        private readonly Stack<HashSet<string>> _typeParamScopes = new();
        private readonly HashSet<string> _definedFunctions = new();
        private readonly Dictionary<string, TypeDescriptor> _functionReturnTypes = new();
        private readonly Dictionary<string, ContractDeclaration> _contractsByName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _shortToFull = new();
        private Program? _program;
        private bool _currentIsInstance;        // context: analyzing an instance method
        private string? _currentContractName;   // context: the contract being analyzed

        // ── Dev-time warning tracking ─────────────────────────────────────────
        private readonly string? _mainSourceFile;                       // null when compiling raw source
        private readonly bool _isExecutable;                            // false → library build (project type "lib")
        private readonly HashSet<string> _usedTypes = new();            // full + short type names referenced
        private readonly HashSet<string> _usedFunctions = new();        // function names that are called
        private readonly HashSet<string> _usedModulePaths = new();      // dotted spines: "IO", "com.lib.Geo"
        private readonly HashSet<int> _usedNamespaceImports = new();    // indices into program.NamespaceImports
        private readonly HashSet<(string Owner, string Field)> _readFields = new();
        private readonly HashSet<(string Owner, string Field)> _writtenFields = new();
        private readonly HashSet<string> _fnReadNames = new();          // vars read in the current function (incl. lambdas)
        private readonly HashSet<string> _fnWrittenNames = new();       // vars assigned in the current function
        private readonly List<(string Name, int Line, int Column)> _fnDeclared = new(); // locals declared in the current function
        private TypeDescriptor? _currentReturnType;                     // the function being analyzed, if any
        private string? _currentSourceFile;                             // the file whose body is being analyzed

        public SemanticAnalyzer(SymbolTable symbolTable, DiagnosticBag diagnostics, string? mainSourceFile = null, bool isExecutable = true)
        {
            _symbolTable = symbolTable;
            _diagnostics = diagnostics;
            _mainSourceFile = mainSourceFile;
            _isExecutable = isExecutable;
        }

        public void Analyze(Program program)
        {
            _program = program;

            // Register namespace imports so short module names resolve.
            foreach (var ns in program.NamespaceImports)
                _symbolTable.ImportNamespace(ns);

            // Index short → qualified names for unique-short-name resolution
            // (mirrors the codegen's index).
            _shortToFull.Clear();
            foreach (var c in program.Contracts) AddShortIndex(c.Name, c.FullName);
            foreach (var s in program.Structs) AddShortIndex(s.Name, s.FullName);
            foreach (var e in program.Enums) AddShortIndex(e.Name, e.FullName);
            foreach (var c in program.Contracts)
            {
                foreach (var m in c.Members)
                {
                    if (m is EnumDeclaration ne) AddShortIndex(ne.Name, ne.FullName);
                    else if (m is StructDeclaration ns) AddShortIndex(ns.Name, ns.FullName);
                }
            }

            // Register contracts as valid types (short + namespace-qualified).
            // Generic contracts register as generic types with their arity so
            // instantiations (Box<int>) validate.
            foreach (var contract in program.Contracts)
            {
                if (contract.IsGeneric)
                {
                    _typeRegistry.RegisterGenericType(contract.Name, contract.TypeParameters.Count);
                    if (contract.FullName != contract.Name)
                        _typeRegistry.RegisterGenericType(contract.FullName, contract.TypeParameters.Count);
                }
                else
                {
                    _typeRegistry.RegisterCustomType(contract.Name);
                    if (contract.FullName != contract.Name)
                        _typeRegistry.RegisterCustomType(contract.FullName);
                }
            }

            // Register structs as valid types (short + namespace-qualified)
            foreach (var structDecl in program.Structs)
            {
                _typeRegistry.RegisterCustomType(structDecl.Name);
                if (structDecl.FullName != structDecl.Name)
                    _typeRegistry.RegisterCustomType(structDecl.FullName);
            }

            // Register enums as valid types (short + namespace-qualified)
            foreach (var enumDecl in program.Enums)
            {
                _typeRegistry.RegisterCustomType(enumDecl.Name);
                if (enumDecl.FullName != enumDecl.Name)
                    _typeRegistry.RegisterCustomType(enumDecl.FullName);
            }
            foreach (var contract in program.Contracts)
            {
                foreach (var member in contract.Members)
                {
                    if (member is EnumDeclaration enumDecl)
                    {
                        _typeRegistry.RegisterCustomType(enumDecl.Name);
                        if (enumDecl.FullName != enumDecl.Name)
                            _typeRegistry.RegisterCustomType(enumDecl.FullName);
                    }
                }
            }
            
            // Register structs defined inside contracts (short + qualified name,
            // mirroring top-level structs and nested enums — otherwise
            // `new Raylib.Color(...)` fails with "Unknown type").
            foreach (var contract in program.Contracts)
            {
                foreach (var member in contract.Members)
                {
                    if (member is StructDeclaration structDecl)
                    {
                        _typeRegistry.RegisterCustomType(structDecl.Name);
                        if (structDecl.FullName != structDecl.Name)
                            _typeRegistry.RegisterCustomType(structDecl.FullName);
                    }
                }
            }

            // Validate every field declaration's type (contracts, structs, and
            // structs nested inside contracts).
            ValidateFieldTypes(program);
            
            // First pass: collect all function definitions and register contracts/structs
            foreach (var contract in program.Contracts)
            {
                _symbolTable.RegisterUserContract(contract);
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func)
                    {
                        _definedFunctions.Add(func.Name);
                        if (func.ReturnType != null)
                            _functionReturnTypes[func.Name] = func.ReturnType;
                    }
                }
            }

            // Register structs so we can look up their fields
            foreach (var structDecl in program.Structs)
            {
                _symbolTable.RegisterUserStruct(structDecl);
            }

            foreach (var func in program.Functions)
            {
                _definedFunctions.Add(func.Name);
                if (func.ReturnType != null)
                    _functionReturnTypes[func.Name] = func.ReturnType;
            }

            // Validate inheritance (base types, cycles) and mark attribute types,
            // then validate every attribute application against those types.
            ValidateInheritanceAndAttributes(program);

            // Validate <NativeBinding>/<ClrImport>/<DllImport> contracts: pure
            // facades — no fields, no constructors, empty-bodied methods whose
            // call sites dispatch to a host module / CLR type / native library.
            ValidateNativeImports(program);

            // Second pass: detailed analysis
            foreach (var contract in program.Contracts)
            {
                AnalyzeContract(contract);
            }

            foreach (var func in program.Functions)
            {
                AnalyzeFunction(func);
            }

            EmitDeadCodeWarnings(program);
        }

        // ── Dead-code & unused-import warnings ───────────────────────────────

        private void EmitDeadCodeWarnings(Program program)
        {
            // ── Entry point ─────────────────────────────────────────────────
            // Computed first: a compilation without any Main is a *library*
            // build — its contracts are API surface included from other paths,
            // so the unused-declaration warnings don't apply (compiling a
            // library file alone would flag "Contract 'X' is never used").
            bool anyEntry = program.Contracts.Any(c => c.Members.OfType<FunctionDeclaration>()
                .Any(f => f.Name == "Main" && f.IsStatic));
            if (!anyEntry && program.Functions.Any(f => f.Name == "Main"))
                anyEntry = true;

            bool isLibrary = !_isExecutable || !anyEntry;
            if (isLibrary)
            {
                // Library mode: declarations are included from other paths.
                // Skip the "never used" contract/struct/enum/static-fn and
                // imported-file warnings; keep intra-function hygiene warnings
                // (unused locals, unused namespace imports, unused fields).
            }

            // ── Unused contracts / structs / enums ──────────────────────────
            foreach (var contract in program.Contracts)
            {
                if (isLibrary) break;
                if (contract.SourceFile == null) continue;              // synthesized from compiled modules
                if (HasEntryPoint(contract)) continue;                  // the runtime calls Program.Main
                if (!_usedTypes.Contains(contract.Name)
                    && !_usedTypes.Contains(contract.FullName)
                    && !contract.Members.OfType<FunctionDeclaration>().Any(f => _usedFunctions.Contains(f.Name)))
                {
                    _diagnostics.AddWarning($"Contract '{contract.Name}' is never used", contract.Line, contract.Column, contract.SourceFile);
                }
            }
            foreach (var structDecl in program.Structs)
            {
                if (isLibrary) break;
                if (structDecl.SourceFile == null) continue;
                if (!_usedTypes.Contains(structDecl.Name) && !_usedTypes.Contains(structDecl.FullName))
                {
                    _diagnostics.AddWarning($"Struct '{structDecl.Name}' is never used", structDecl.Line, structDecl.Column, structDecl.SourceFile);
                }
            }
            foreach (var enumDecl in program.Enums)
            {
                if (isLibrary) break;
                if (enumDecl.SourceFile == null) continue;
                if (!_usedTypes.Contains(enumDecl.Name) && !_usedTypes.Contains(enumDecl.FullName))
                {
                    _diagnostics.AddWarning($"Enum '{enumDecl.Name}' is never used", enumDecl.Line, enumDecl.Column, enumDecl.SourceFile);
                }
            }

            // ── Unused functions (top-level + static contract members) ──────
            foreach (var func in program.Functions)
            {
                if (isLibrary) break;
                if (func.Name == "Main") continue;
                if (func.Body == null) continue;                        // native-style declaration
                if (!_usedFunctions.Contains(func.Name))
                {
                    _diagnostics.AddWarning($"Function '{func.Name}' is never called", func.Line, func.Column, func.SourceFile);
                }
            }
            foreach (var contract in program.Contracts)
            {
                if (isLibrary) break;
                // Sum-type machinery (variant factories, tag fields) is
                // compiler-generated; never report it as dead code.
                if (contract.IsSumTypeBase || contract.SumTypeOf != null) continue;
                foreach (var member in contract.Members)
                {
                    if (member is not FunctionDeclaration func) continue;
                    if (func.Name == "Main" || !func.IsStatic) continue;    // instance methods are API surface
                    if (func.Body == null) continue;
                    if (!_usedFunctions.Contains(func.Name))
                    {
                        _diagnostics.AddWarning($"Static function '{contract.Name}.{func.Name}' is never called", func.Line, func.Column, func.SourceFile);
                    }
                }
            }

            // ── Unused fields (never read; assignments don't count) ─────────
            foreach (var contract in program.Contracts)
            {
                if (contract.IsSumTypeBase || contract.SumTypeOf != null) continue;
                foreach (var field in contract.Fields)
                {
                    bool read = _readFields.Contains((contract.Name, field.Name))
                        || _readFields.Contains((contract.FullName, field.Name));
                    bool written = _writtenFields.Contains((contract.Name, field.Name))
                        || _writtenFields.Contains((contract.FullName, field.Name));
                    if (!read && written)
                    {
                        _diagnostics.AddWarning($"Field '{contract.Name}.{field.Name}' is assigned but never read", field.Line, field.Column, contract.SourceFile);
                    }
                    else if (!read)
                    {
                        _diagnostics.AddWarning($"Field '{contract.Name}.{field.Name}' is never used", field.Line, field.Column, contract.SourceFile);
                    }
                }
            }

            // ── Unused namespace imports ────────────────────────────────────
            for (int i = 0; i < program.NamespaceImports.Count; i++)
            {
                string ns = program.NamespaceImports[i];
                if (_usedNamespaceImports.Contains(i)) continue;
                if (_symbolTable.UsedImportedNamespaces.Contains(ns)) continue;
                if (_usedModulePaths.Any(p => p == ns || p.StartsWith(ns + ".", StringComparison.Ordinal)
                                             || ns.StartsWith(p + ".", StringComparison.Ordinal))) continue;
                _diagnostics.AddWarning($"Namespace import '{ns}' is never used", 1, 1, _mainSourceFile);
            }

            // ── Unused file imports: every declaration in the file is dead ──
            if (_mainSourceFile != null && !isLibrary)
            {
                var byFile = new Dictionary<string, (List<object> Decls, bool HasMain)>();
                void Add(string? file, object decl, bool isMain) { if (file == null) return; if (!byFile.TryGetValue(file, out var e)) { e = (new List<object>(), false); byFile[file] = e; } e.Decls.Add(decl); if (isMain) e.HasMain = true; }

                foreach (var c in program.Contracts)
                {
                    Add(c.SourceFile, c, HasEntryPoint(c));
                    foreach (var m in c.Members)
                        if (m is FunctionDeclaration f) Add(f.SourceFile, f, f.Name == "Main");
                }
                foreach (var s in program.Structs) Add(s.SourceFile, s, false);
                foreach (var e in program.Enums) Add(e.SourceFile, e, false);
                foreach (var f in program.Functions) Add(f.SourceFile, f, f.Name == "Main");

                string mainKey = Contract.Compiler.ImportResolver.NormalizeAbsolutePath(_mainSourceFile);
                StringComparison pathCmp = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                foreach (var (file, entry) in byFile)
                {
                    if (entry.HasMain) continue;
                    if (string.Equals(Contract.Compiler.ImportResolver.NormalizeAbsolutePath(file), mainKey, pathCmp)) continue;
                    bool anyUsed = entry.Decls.Any(d => d switch
                    {
                        ContractDeclaration c => _usedTypes.Contains(c.Name) || _usedTypes.Contains(c.FullName)
                            || c.Members.OfType<FunctionDeclaration>().Any(f => _usedFunctions.Contains(f.Name)),
                        StructDeclaration s => _usedTypes.Contains(s.Name) || _usedTypes.Contains(s.FullName),
                        EnumDeclaration en => _usedTypes.Contains(en.Name) || _usedTypes.Contains(en.FullName),
                        FunctionDeclaration f => _usedFunctions.Contains(f.Name),
                        _ => false
                    });
                    if (!anyUsed)
                    {
                        _diagnostics.AddWarning(
                            $"Imported file '{Path.GetFileName(file)}' is never used — none of its declarations are referenced",
                            1, 1, _mainSourceFile);
                    }
                }
            }

            // ── Entry point info (executable builds only) ───────────────────
            if (_isExecutable && !anyEntry)
            {
                _diagnostics.AddInfo("No static 'Main' entry point found — this module cannot be run directly", 1, 1, _mainSourceFile);
            }
        }

        private static bool HasEntryPoint(ContractDeclaration contract)
            => contract.Members.OfType<FunctionDeclaration>().Any(f => f.Name == "Main" && f.IsStatic);

        private void AnalyzeContract(ContractDeclaration contract)
        {
            _currentContractName = contract.Name;
            PushTypeParams(contract.TypeParameters);
            foreach (var ctor in contract.Constructors)
            {
                _currentIsInstance = true;   // ctors receive `this` as param 0
                AnalyzeConstructor(ctor);
            }

            foreach (var member in contract.Members)
            {
                if (member is FunctionDeclaration func)
                {
                    AnalyzeFunction(func);
                }
            }
            PopTypeParams();
        }

        // ── Field types ──────────────────────────────────────────────

        /// <summary>
        /// Validates every field declaration in the program: the declared type
        /// must be known/valid, must not be <c>void</c>/<c>null</c>, and field
        /// names must be unique within the declaring type.
        /// </summary>
        private void ValidateFieldTypes(Program program)
        {
            foreach (var contract in program.Contracts)
            {
                // Generic contract fields reference the type params (T) — push
                // them so field validation accepts the literal parameter names.
                PushTypeParams(contract.TypeParameters);
                ValidateFields(contract.Fields, "contract");
                foreach (var member in contract.Members)
                {
                    if (member is StructDeclaration nestedStruct)
                        ValidateFields(nestedStruct.Fields, "struct");
                }
                PopTypeParams();
            }

            foreach (var structDecl in program.Structs)
            {
                ValidateFields(structDecl.Fields, "struct");
            }
        }

        private void ValidateFields(IReadOnlyList<StructField> fields, string ownerKind)
        {
            var seen = new HashSet<string>();
            foreach (var field in fields)
            {
                if (!seen.Add(field.Name))
                {
                    _diagnostics.AddError($"Field '{field.Name}' is already declared in this {ownerKind}", field.Line, field.Column);
                }

                if (field.Type.IsEmpty)
                {
                    _diagnostics.AddError($"Field '{field.Name}' must have a type", field.Line, field.Column);
                    continue;
                }

                if (field.Type is TypeDescriptor.Named n
                    && (string.Equals(n.Name, "void", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Name, "null", StringComparison.OrdinalIgnoreCase)))
                {
                    _diagnostics.AddError($"Field '{field.Name}' cannot have type '{n.Name}'", field.Line, field.Column);
                    continue;
                }

                if (field.Type is TypeDescriptor.Named fieldNamed && !fieldNamed.IsEmpty)
                {
                    // Record field types as type usages (dead-code detection).
                    _usedTypes.Add(ResolveTypeName(fieldNamed.Name));
                }

                if (!IsValidTypeInContext(field.Type))
                {
                    _diagnostics.AddError($"Unknown type '{field.Type}' for field '{field.Name}'", field.Line, field.Column);
                }
            }
        }

        /// <summary>
        /// True when a type descriptor is valid in the current context: the
        /// registry accepts it, or it references an in-scope generic type
        /// parameter (contract/fn type params are legal type names inside the
        /// generic body).
        /// </summary>
        private bool IsValidTypeInContext(TypeDescriptor type)
        {
            switch (type)
            {
                case TypeDescriptor.Named n:
                    return _typeRegistry.IsValidTypeName(n.Name) || IsTypeParamInScope(n.Name);
                case TypeDescriptor.ArrayOf a:
                    return IsValidTypeInContext(a.Element);
                case TypeDescriptor.Function f:
                    return f.Parameters.All(IsValidTypeInContext) && IsValidTypeInContext(f.Return);
                case TypeDescriptor.Tuple t:
                    return t.Elements.All(IsValidTypeInContext);
                case TypeDescriptor.GenericInstance g:
                    return _typeRegistry.IsGenericType(g.Name)
                        && g.Arguments.Count == _typeRegistry.GetArity(g.Name)
                        && g.Arguments.All(IsValidTypeInContext);
                default:
                    return false;
            }
        }

        private bool IsTypeParamInScope(string name)
        {
            foreach (var scope in _typeParamScopes)
                if (scope.Contains(name)) return true;
            return false;
        }

        private void PushTypeParams(IEnumerable<string> parameters)
        {
            _typeParamScopes.Push(new HashSet<string>(parameters));
        }

        private void PopTypeParams()
        {
            if (_typeParamScopes.Count > 0) _typeParamScopes.Pop();
        }

        // ── Attributes & inheritance ───────────────────────────────────

        /// <summary>
        /// Validates base types and inheritance cycles across all contracts,
        /// marks contracts that (transitively) inherit from the built-in
        /// <c>Attribute</c> type, then validates every attribute application.
        /// </summary>
        private void ValidateInheritanceAndAttributes(Program program)
        {
            _contractsByName.Clear();
            foreach (var c in program.Contracts)
            {
                _contractsByName[c.Name] = c;
                if (c.FullName != c.Name)
                    _contractsByName[c.FullName] = c;
            }

            foreach (var contract in program.Contracts)
            {
                var chain = new List<ContractDeclaration>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var current = contract;
                bool reachesAttribute = false;

                // All parents: the primary base first, then interface-style parents.
                IEnumerable<string> ParentNames(ContractDeclaration c)
                    => c.BaseTypeName != null
                        ? new[] { c.BaseTypeName }.Concat(c.InterfaceNames)
                        : c.InterfaceNames;

                while (ParentNames(current).Any())
                {
                    if (!seen.Add(current.Name))
                    {
                        _diagnostics.AddError($"Inheritance cycle involving contract '{current.Name}'", current.Line, current.Column);
                        break;
                    }

                    chain.Add(current);
                    var parentNames = ParentNames(current).ToList();

                    // The FIRST name is the primary base; the rest are
                    // interface-style parents (§6).
                    var baseName = parentNames[0];
                    _usedTypes.Add(baseName);   // base type is a usage of that type

                    foreach (var ifaceName in parentNames.Skip(1))
                    {
                        _usedTypes.Add(ifaceName);
                        var iface = _contractsByName.TryGetValue(ifaceName, out var ic) ? ic : FindContract(ifaceName);
                        if (iface == null)
                        {
                            _diagnostics.AddError($"Unknown interface '{ifaceName}' on contract '{current.Name}'", current.Line, current.Column);
                            continue;
                        }
                        ValidateInterfaceParent(current, iface);
                    }

                    if (baseName.Equals("Attribute", StringComparison.OrdinalIgnoreCase))
                    {
                        reachesAttribute = true;
                        break;
                    }

                    if (!_contractsByName.TryGetValue(baseName, out var baseContract))
                    {
                        if (!_typeRegistry.IsValidType(baseName))
                        {
                            _diagnostics.AddError($"Unknown base type '{baseName}' for contract '{current.Name}'", current.Line, current.Column);
                        }
                        break;
                    }
                    current = baseContract;
                }

                if (reachesAttribute)
                {
                    contract.IsAttributeType = true;
                    foreach (var c in chain)
                        c.IsAttributeType = true;
                }
            }

            CollectAbstractRequirements(program);

            foreach (var contract in program.Contracts)
            {
                ValidateAttributes(contract.Attributes, "contract", _contractsByName);
                foreach (var ctor in contract.Constructors)
                    ValidateAttributes(ctor.Attributes, "constructor", _contractsByName);
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func)
                        ValidateAttributes(func.Attributes, "function", _contractsByName);
                    else if (member is StructDeclaration structDecl)
                        ValidateAttributes(structDecl.Attributes, "struct", _contractsByName);
                }
            }

            foreach (var structDecl in program.Structs)
                ValidateAttributes(structDecl.Attributes, "struct", _contractsByName);

            foreach (var func in program.Functions)
                ValidateAttributes(func.Attributes, "function", _contractsByName);
        }

        /// <summary>
        /// Validates an interface-style parent (FEATURE_PROPOSALS §6 —
        /// interfaces expressed as ordinary contracts with multiple
        /// inheritance). Interface parents carry no state: fields and
        /// constructors would have no single layout across all implementors.
        /// </summary>
        private void ValidateInterfaceParent(ContractDeclaration derived, ContractDeclaration iface)
        {
            if (iface.Fields.Any(f => !f.IsStatic))
            {
                _diagnostics.AddError(
                    $"Interface '{iface.Name}' declares instance fields — interface-style parents must be stateless (fields live on the primary base)",
                    derived.Line, derived.Column);
            }
            if (iface.Constructors.Count > 0)
            {
                _diagnostics.AddError(
                    $"Interface '{iface.Name}' declares constructors — interface-style parents cannot be constructed",
                    derived.Line, derived.Column);
            }
        }

        /// <summary>
        /// Computes each contract's <see cref="ContractDeclaration.PendingAbstractMethods"/>:
        /// body-less instance methods inherited from the primary base chain or
        /// declared by any interface-style parent, minus those the contract
        /// implements itself. A contract with pending abstracts is abstract —
        /// it cannot be instantiated.
        /// </summary>
        private void CollectAbstractRequirements(Program program)
        {
            foreach (var contract in program.Contracts)
            {
                var required = new List<FunctionDeclaration>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                void CollectFrom(ContractDeclaration c, bool includeSelfAbstracts)
                {
                    // Primary base chain first... (cycle-guarded: a broken
                    // inheritance cycle must not hang this pass)
                    var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (var cur = includeSelfAbstracts ? c : BaseContract(c);
                         cur != null && walked.Add(cur.Name);
                         cur = BaseContract(cur))
                    {
                        foreach (var m in cur.Members.OfType<FunctionDeclaration>())
                        {
                            if (m.IsInstance && m.Body == null && seen.Add(m.Name))
                                required.Add(m);
                        }
                    }
                    // ...then every interface parent's methods (their own bases too).
                    foreach (var ifaceName in c.InterfaceNames)
                    {
                        var iface = FindContract(ifaceName);
                        if (iface == null) continue;
                        foreach (var m in iface.Members.OfType<FunctionDeclaration>())
                        {
                            if (m.IsInstance && m.Body == null && seen.Add(m.Name))
                                required.Add(m);
                        }
                    }
                }

                CollectFrom(contract, includeSelfAbstracts: false);

                // The contract's own implementations discharge requirements by name.
                var implemented = contract.Members.OfType<FunctionDeclaration>()
                    .Where(f => f.IsInstance && f.Body != null)
                    .Select(f => f.Name)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var req in required.Where(r => !implemented.Contains(r.Name)))
                    contract.PendingAbstractMethods.Add(req);

                foreach (var ifaceName in contract.InterfaceNames)
                {
                    var iface = FindContract(ifaceName);
                    if (iface == null) continue;
                    foreach (var req in iface.PendingAbstractMethods.Where(r => !implemented.Contains(r.Name)))
                        if (!contract.PendingAbstractMethods.Any(p => p.Name == req.Name))
                            contract.PendingAbstractMethods.Add(req);
                }

                if (contract.BaseTypeName != null && _contractsByName.TryGetValue(contract.BaseTypeName, out var primBase))
                {
                    foreach (var req in primBase.PendingAbstractMethods.Where(r => !implemented.Contains(r.Name)))
                        if (!contract.PendingAbstractMethods.Any(p => p.Name == req.Name))
                            contract.PendingAbstractMethods.Add(req);
                }
            }
        }

        private void ValidateAttributes(List<AttributeUsage> attributes, string targetKind, Dictionary<string, ContractDeclaration> contractsByName)
        {            foreach (var attr in attributes)
            {
                // Applying an attribute references its declaring contract.
                _usedTypes.Add(attr.Name);

                // Built-in attributes — no user declaration needed. Only valid
                // on contracts, each taking exactly one string argument:
                //   <NativeBinding("ModuleName")>  host [ClassBinding] module
                //   <ClrImport("System.Math")>     CLR type (no class binding needed)
                //   <DllImport("user32.dll")>      native P/Invoke library
                if (attr.Name.Equals("NativeBinding", StringComparison.OrdinalIgnoreCase)
                    || attr.Name.Equals("ClrImport", StringComparison.OrdinalIgnoreCase)
                    || attr.Name.Equals("DllImport", StringComparison.OrdinalIgnoreCase))
                {
                    if (targetKind != "contract")
                    {
                        _diagnostics.AddError($"{attr.Name} attribute is only valid on contracts", attr.Line, attr.Column);
                    }
                    else if (attr.Arguments.Count != 1)
                    {
                        _diagnostics.AddError($"{attr.Name} expects exactly 1 argument (the binding target name), got {attr.Arguments.Count}", attr.Line, attr.Column);
                    }
                    continue;
                }

                if (!contractsByName.TryGetValue(attr.Name, out var attrContract))
                {
                    _diagnostics.AddError($"Unknown attribute '{attr.Name}'", attr.Line, attr.Column);
                    continue;
                }

                if (!attrContract.IsAttributeType)
                {
                    _diagnostics.AddError($"'{attr.Name}' is not an attribute type — it must inherit from Attribute", attr.Line, attr.Column);
                    continue;
                }

                // Argument count must match one of the attribute type's constructors.
                if (attrContract.Constructors.Count > 0)
                {
                    bool matches = attrContract.Constructors.Any(c => c.Parameters.Count == attr.Arguments.Count);
                    if (!matches)
                    {
                        var expected = string.Join(" or ", attrContract.Constructors.Select(c => c.Parameters.Count.ToString()));
                        _diagnostics.AddError($"Attribute '{attr.Name}' expects {expected} argument(s), got {attr.Arguments.Count}", attr.Line, attr.Column);
                    }
                }
            }
        }

        /// <summary>
        /// Validates <c>&lt;NativeBinding("Module")&gt;</c> contracts: a native
        /// binding is a pure facade — no fields, no constructors, and every
        /// member function is an empty-bodied declaration that dispatches to
        /// <c>Module.Method</c> at runtime. The module must be a registered
        /// host binding and each declared method must exist on it.
        ///
        /// Also validates the two CLR-facing facades:
        /// <c>&lt;ClrImport("System.Math")&gt;</c> (methods map to public static
        /// methods on a CLR type, resolved by reflection — no host binding
        /// class needed) and <c>&lt;DllImport("user32.dll")&gt;</c> (methods
        /// P/Invoke native exports of the named library).
        /// </summary>
        private void ValidateNativeImports(Program program)
        {
            foreach (var contract in program.Contracts)
            {
                var attr = contract.Attributes.FirstOrDefault(a =>
                    a.Name.Equals("NativeBinding", StringComparison.OrdinalIgnoreCase)
                    || a.Name.Equals("ClrImport", StringComparison.OrdinalIgnoreCase)
                    || a.Name.Equals("DllImport", StringComparison.OrdinalIgnoreCase));
                if (attr == null) continue;

                if (attr.Arguments.Count != 1) continue; // arity already reported

                string target = attr.Arguments[0].Trim();
                if (target.Length >= 2 && target[0] == '"' && target[^1] == '"')
                    target = target[1..^1];

                // ── Shared facade shape checks ────────────────────────────
                if (contract.Fields.Count > 0)
                    _diagnostics.AddError($"Native-import contract '{contract.Name}' cannot have fields", contract.Line, contract.Column);
                if (contract.Constructors.Count > 0)
                    _diagnostics.AddError($"Native-import contract '{contract.Name}' cannot declare constructors", contract.Line, contract.Column);

                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func)
                    {
                        if (func.Body != null && func.Body.Statements.Count > 0)
                            _diagnostics.AddError($"Native-import method '{contract.Name}.{func.Name}' must have an empty body", func.Line, func.Column);
                    }
                }

                if (attr.Name.Equals("NativeBinding", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateNativeBindingContract(contract, target, attr);
                }
                else if (attr.Name.Equals("ClrImport", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateClrImportContract(contract, target, attr);
                }
                else
                {
                    contract.DllImportLibrary = target;
                }
            }
        }

        private void ValidateNativeBindingContract(ContractDeclaration contract, string bindingName, AttributeUsage attr)
        {
            if (!_symbolTable.IsBoundModule(bindingName))
            {
                _diagnostics.AddError(
                    $"NativeBinding module '{bindingName}' is not a registered binding (check bindingAssemblies)",
                    attr.Line, attr.Column);
                return;
            }

            contract.NativeBindingName = bindingName;

            // The parser marks non-static members as instance methods only
            // for contracts with fields; native-bound contracts are pure
            // facades (no fields), so mark their methods as instance here.
            foreach (var member in contract.Members)
            {
                if (member is FunctionDeclaration f && !f.IsStatic)
                    f.IsInstance = true;
            }

            foreach (var member in contract.Members)
            {
                if (member is not FunctionDeclaration func) continue;
                if (!_symbolTable.TryGetMethod(bindingName, func.Name, out _))
                    _diagnostics.AddError($"Native binding '{bindingName}' has no method named '{func.Name}'", func.Line, func.Column);
            }
        }

        private void ValidateClrImportContract(ContractDeclaration contract, string clrTypeName, AttributeUsage attr)
        {
            contract.ClrImportType = clrTypeName;

            // The parser marks non-static members as instance methods only
            // for contracts with fields; these facades have none, so treat
            // every member as a static declaration (call sites resolve through
            // the type name).
            foreach (var member in contract.Members)
            {
                if (member is FunctionDeclaration f && !f.IsStatic)
                    f.IsStatic = true;
            }

            // Best-effort compile-time check: the CLR type is only resolvable
            // when it lives in the compiler process (BCL types like
            // System.Math, System.Convert, ... or --bind assemblies). Types
            // loaded by the host process are checked at runtime instead.
            Type? clrType = null;
            try { clrType = Type.GetType(clrTypeName); }
            catch (Exception) { /* malformed assembly-qualified name — runtime will report */ }

            if (clrType == null)
            {
                _diagnostics.AddWarning(
                    $"ClrImport type '{clrTypeName}' is not resolvable at compile time — the runtime host must have it loaded (use a BCL type, an assembly-qualified name, or a --bind assembly)",
                    attr.Line, attr.Column);
                return;
            }

            foreach (var member in contract.Members)
            {
                if (member is not FunctionDeclaration func) continue;

                var overloads = clrType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(m => m.Name.Equals(func.Name, StringComparison.Ordinal))
                    .ToList();

                if (overloads.Count == 0)
                {
                    _diagnostics.AddError($"ClrImport type '{clrTypeName}' has no public static method named '{func.Name}'", func.Line, func.Column);
                    continue;
                }

                if (!overloads.Any(m => m.GetParameters().Length == func.Parameters.Count))
                {
                    var arities = string.Join(" or ", overloads.Select(m => m.GetParameters().Length.ToString()).Distinct());
                    _diagnostics.AddError(
                        $"ClrImport method '{clrTypeName}.{func.Name}' takes {arities} argument(s), got {func.Parameters.Count}",
                        func.Line, func.Column);
                    continue;
                }
            }
        }

        private void AnalyzeConstructor(ConstructorDeclaration ctor)
        {
            _scopes.Clear();
            _varMutability.Clear();
            _scopes.Push(new Dictionary<string, VariableDeclaration>());
            _varMutability.Push(new Dictionary<string, bool>());
            BeginFunctionFrame();
            _currentReturnType = null;
            _currentSourceFile = ctor.SourceFile;
            // Generic contract constructors reference the type params (T).
            if (_currentContractName != null && FindGenericContract(_currentContractName) is { } ctorGenContract)
                PushTypeParams(ctorGenContract.TypeParameters);

            foreach (var param in ctor.Parameters)
            {
                if (param.Type is TypeDescriptor.Named pn && !pn.IsEmpty)
                    _usedTypes.Add(ResolveTypeName(pn.Name));
                if (!param.Type.IsEmpty && !IsValidTypeInContext(param.Type))
                {
                    _diagnostics.AddError($"Unknown type '{param.Type}' for parameter '{param.Name}'", param.Line, param.Column);
                }
                DeclareVariable(param.Name, param.Type, param.Line, param.Column, trackUsage: false);
            }

            if (ctor.Body != null)
            {
                AnalyzeStatement(ctor.Body);
            }

            if (_currentContractName != null && FindGenericContract(_currentContractName) != null)
                PopTypeParams();
            _scopes.Pop();
            _varMutability.Pop();
            EndFunctionFrame();
        }

        private void AnalyzeFunction(FunctionDeclaration func)
        {
            _currentIsInstance = func.IsInstance;
            _currentContractName = func.ContractName;
            _currentSourceFile = func.SourceFile;
            _scopes.Clear();
            _varMutability.Clear();
            _scopes.Push(new Dictionary<string, VariableDeclaration>());
            _varMutability.Push(new Dictionary<string, bool>());
            BeginFunctionFrame();
            PushTypeParams(func.TypeParameters);

            foreach (var param in func.Parameters)
            {
                if (param.Type is TypeDescriptor.Named pn && !pn.IsEmpty)
                    _usedTypes.Add(ResolveTypeName(pn.Name));
                if (!IsValidTypeInContext(param.Type))
                {
                    _diagnostics.AddError($"Unknown type '{param.Type}' for parameter '{param.Name}'", param.Line, param.Column);
                }
                DeclareVariable(param.Name, param.Type, param.Line, param.Column, trackUsage: false);
            }

            // C#-style entry point: `Main()` or `Main(args: string[])`.
            if (func.Name == "Main" && func.IsStatic)
            {
                if (func.Parameters.Count > 1)
                {
                    _diagnostics.AddError("Entry point 'Main' may take at most one parameter (a string[] of command-line arguments)", func.Line, func.Column);
                }
                else if (func.Parameters.Count == 1)
                {
                    var p = func.Parameters[0];
                    bool isStringArray = p.Type is TypeDescriptor.ArrayOf arr
                        && arr.Element is TypeDescriptor.Named en
                        && en.Name.Equals("string", StringComparison.OrdinalIgnoreCase);
                    if (!isStringArray)
                    {
                        _diagnostics.AddError("Entry point 'Main' parameter must be of type 'string[]'", p.Line, p.Column);
                    }
                }
            }

            if (func.ReturnType != null)
            {
                if (func.ReturnType is TypeDescriptor.Named rn && !rn.IsEmpty)
                    _usedTypes.Add(ResolveTypeName(rn.Name));
                if (!IsValidTypeInContext(func.ReturnType))
                {
                    _diagnostics.AddError($"Unknown return type '{func.ReturnType}' for function '{func.Name}'", func.Line, func.Column);
                }
            }

            _currentReturnType = func.ReturnType;
            if (func.Body != null)
            {
                AnalyzeStatement(func.Body);
            }
            _currentReturnType = null;

            EmitFunctionWarnings(func);
            PopTypeParams();
            _scopes.Pop();
            _varMutability.Pop();
            EndFunctionFrame();
        }

        private void DeclareVariable(string name, TypeDescriptor type, int line, int column, bool trackUsage = true, bool warnOnShadow = true, bool isMutable = true)
        {
            // Resolve short/dotted type names through namespaces/imports so
            // annotations like `var t: Terminal.Terminal` validate and match
            // the emitted wire names.
            if (type is TypeDescriptor.Named n && !n.IsEmpty)
            {
                string resolved = ResolveTypeName(n.Name);
                if (resolved != n.Name)
                    type = new TypeDescriptor.Named(resolved);
            }

            if (!IsValidTypeInContext(type))
            {
                _diagnostics.AddError($"Unknown type '{type}'", line, column);
            }

            var currentScope = _scopes.Peek();
            if (currentScope.ContainsKey(name))
            {
                _diagnostics.AddError($"Variable '{name}' is already defined in this scope.", line, column);
            }
            else
            {
                // A declaration that hides a name from an enclosing scope is
                // usually a bug — warn (C#/Java both flag this). Lambda
                // parameters are exempt: `fun x -> x + 1` shadowing an outer
                // `x` is idiomatic.
                if (warnOnShadow && _scopes.Count >= 2 && IsDeclaredInOuterScope(name))
                {
                    Warn($"Variable '{name}' shadows a declaration in an outer scope", line, column);
                }

                // We use a dummy VariableDeclaration for tracking
                currentScope[name] = new VariableDeclaration(name, type, null, line, column);

                // Track mutability for let vs var enforcement.
                _varMutability.Peek()[name] = isMutable;

                if (trackUsage)
                    _fnDeclared.Add((name, line, column));
            }
        }

        private bool IsDeclaredInOuterScope(string name)
        {
            var arr = _scopes.ToArray();   // index 0 = innermost
            for (int i = 1; i < arr.Length; i++)
            {
                if (arr[i].ContainsKey(name)) return true;
            }
            return false;
        }

        /// <summary>Adds a warning attributed to the file currently being analyzed.</summary>
        private void Warn(string message, int line, int column)
            => _diagnostics.AddWarning(message, line, column, _currentSourceFile);

        // ── Function-local usage frames (unused locals, missing returns) ──

        private void BeginFunctionFrame()
        {
            _fnReadNames.Clear();
            _fnWrittenNames.Clear();
            _fnDeclared.Clear();
        }

        private void EndFunctionFrame()
        {
            _fnReadNames.Clear();
            _fnWrittenNames.Clear();
            _fnDeclared.Clear();
        }

        private void EmitFunctionWarnings(FunctionDeclaration func)
        {
            // Unused locals: declared but never read.
            foreach (var (name, line, column) in _fnDeclared)
            {
                if (!_fnReadNames.Contains(name) && _fnWrittenNames.Contains(name))
                {
                    Warn($"Variable '{name}' is assigned but its value is never used", line, column);
                }
                else if (!_fnReadNames.Contains(name))
                {
                    Warn($"Variable '{name}' is declared but never used", line, column);
                }
            }

            // Missing return: a non-void function that can fall off the end.
            // Native-import facade methods are declarations only (empty body,
            // dispatch to a host binding / CLR type / native library) — their
            // declared return types come from the target, so a missing return
            // is expected, not a bug.
            bool isNativeFacade = func.ContractName != null
                && _contractsByName.TryGetValue(func.ContractName, out var declaringContract)
                && (declaringContract.NativeBindingName != null
                    || declaringContract.ClrImportType != null
                    || declaringContract.DllImportLibrary != null);
            if (!isNativeFacade
                && func.Body != null
                && func.ReturnType is TypeDescriptor.Named retNamed
                && !string.Equals(retNamed.Name, "void", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(retNamed.Name, "null", StringComparison.OrdinalIgnoreCase)
                && CanFallThrough(func.Body))
            {
                Warn(
                    $"Function '{func.Name}' declares return type '{retNamed.Name}' but not all code paths return a value",
                    func.Line, func.Column);
            }
        }

        /// <summary>True when control can reach the end of a statement (i.e. it doesn't always return/break/continue).</summary>
        private static bool CanFallThrough(Statement? s) => s switch
        {
            null => true,
            BlockStatement b => b.Statements.Count == 0 || CanFallThrough(b.Statements[^1]),
            ReturnStatement => false,
            BreakStatement => false,
            ContinueStatement => false,
            ExpressionStatement => true,
            VariableDeclaration => true,
            IfStatement i => i.ElseBranch == null
                || CanFallThrough(i.ThenBranch)
                || CanFallThrough(i.ElseBranch),
            WhileStatement w => !IsAlwaysTrueCondition(w.Condition) || CanFallThrough(w.Body),
            ForStatement f => (f.Condition != null && !IsAlwaysTrueCondition(f.Condition)) || CanFallThrough(f.Body),
            SwitchStatement sw => !(sw.Cases.Count > 0
                && sw.Cases.Any(c => !CanFallThrough(c.Statements))
                && sw.Cases.Any(c => (c.Value == null && c.StringValue == null) && !CanFallThrough(c.Statements))),
            ThrowStatement => false,
            TryStatement t => CanFallThrough(t.TryBlock)
                || t.CatchClauses.Count > 0
                || (t.FinallyBlock != null && CanFallThrough(t.FinallyBlock)),
            _ => true,
        };

        private static bool CanFallThrough(IReadOnlyList<Statement> statements)
            => statements.Count == 0 || CanFallThrough(statements[^1]);

        private static bool IsAlwaysTrueCondition(Expression? e)
            => e is LiteralExpression lit && lit.Value switch
            {
                bool b => b,
                int i => i != 0,
                double d => d != 0.0,
                _ => false
            };

        private void AnalyzeStatement(Statement statement)
        {
            switch (statement)
            {
                case BlockStatement block:
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    _varMutability.Push(new Dictionary<string, bool>());
                    bool reachable = true;
                    foreach (var stmt in block.Statements)
                    {
                        if (!reachable)
                        {
                            Warn("Unreachable code — this statement follows a 'return', 'break', or 'continue'", stmt.Line, stmt.Column);
                        }
                        else if (!CanFallThrough(stmt))
                        {
                            reachable = false;
                        }
                        AnalyzeStatement(stmt);
                    }
                    _scopes.Pop();
                    _varMutability.Pop();
                    break;
                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;
                case VariableDeclaration varDecl:
                    // Analyze the initializer first so calls get symbol-linked and
                    // their return types are available for inference.
                    if (varDecl.Initializer != null)
                        AnalyzeExpression(varDecl.Initializer);

                    // Destructuring: var (a, b) = f(); — bind each name to the
                    // corresponding tuple element type.
                    if (varDecl.Names.Count > 0)
                    {
                        var tupleType = varDecl.Initializer != null ? InferType(varDecl.Initializer) : null;
                        if (tupleType is TypeDescriptor.Tuple tuple)
                        {
                            for (int i = 0; i < varDecl.Names.Count; i++)
                            {
                                var elemType = i < tuple.Elements.Count ? tuple.Elements[i] : TypeDescriptor.Empty;
                                DeclareVariable(varDecl.Names[i], elemType, varDecl.Line, varDecl.Column, isMutable: varDecl.IsMutable);
                            }
                        }
                        else
                        {
                            _diagnostics.AddError(
                                $"Cannot destructure a value of type '{(tupleType?.ToString() ?? "unknown")}' — expected a tuple return",
                                varDecl.Line, varDecl.Column);
                        }
                        break;
                    }

                    if (varDecl.Type.IsEmpty)
                    {
                        if (varDecl.Initializer != null)
                        {
                            varDecl.Type = InferType(varDecl.Initializer) ?? TypeDescriptor.Empty;
                        }
                    }
                    else if (varDecl.Type is TypeDescriptor.Named annType && !annType.IsEmpty)
                    {
                        // Resolve explicit annotations through namespaces/imports
                        // so the AST (and thus codegen) carries the wire name.
                        string resolved = ResolveTypeName(annType.Name);
                        if (resolved != annType.Name)
                            varDecl.Type = new TypeDescriptor.Named(resolved);
                    }

                    if (varDecl.Type.IsEmpty)
                    {
                        _diagnostics.AddError($"Variable '{varDecl.Name}' must have an explicit type (using 'var name: type').", varDecl.Line, varDecl.Column);
                    }

                    DeclareVariable(varDecl.Name, varDecl.Type, varDecl.Line, varDecl.Column, isMutable: varDecl.IsMutable);
                    break;
                case IfStatement ifStmt:
                    AnalyzeConditionWarnings("if", ifStmt.Condition, ifStmt.Line, ifStmt.Column);
                    AnalyzeExpression(ifStmt.Condition);
                    AnalyzeStatement(ifStmt.ThenBranch);
                    if (ifStmt.ElseBranch != null)
                        AnalyzeStatement(ifStmt.ElseBranch);
                    if (IsEmptyBlock(ifStmt.ThenBranch) || (ifStmt.ElseBranch != null && IsEmptyBlock(ifStmt.ElseBranch)))
                        Warn("Empty block — the branch does nothing", ifStmt.Line, ifStmt.Column);
                    break;
                case WhileStatement whileStmt:
                    AnalyzeConditionWarnings("while", whileStmt.Condition, whileStmt.Line, whileStmt.Column);
                    AnalyzeExpression(whileStmt.Condition);
                    AnalyzeStatement(whileStmt.Body);
                    if (IsEmptyBlock(whileStmt.Body))
                        Warn("Empty loop body — the loop does nothing", whileStmt.Line, whileStmt.Column);
                    break;
                case ForInStatement forIn:
                    AnalyzeForInStatement(forIn);
                    break;
                case ForStatement forStmt:
                    // The loop variable is scoped to the loop itself (like C),
                    // so two sequential 'for (var i = ...)' loops don't collide.
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    _varMutability.Push(new Dictionary<string, bool>());
                    if (forStmt.Condition != null)
                        AnalyzeConditionWarnings("for", forStmt.Condition, forStmt.Line, forStmt.Column);
                    if (forStmt.Initializer != null)
                    {
                        if (forStmt.Initializer is BlockStatement initBlock)
                        {
                            // foreach desugar: the temp declarations live in a
                            // block initializer — analyze them FLAT into the
                            // loop's own scope so the condition/update can see
                            // the temps (a nested scope would hide them).
                            foreach (var s in initBlock.Statements)
                                AnalyzeStatement(s);
                        }
                        else
                        {
                            AnalyzeStatement(forStmt.Initializer);
                        }
                    }
                    if (forStmt.Condition != null)
                        AnalyzeExpression(forStmt.Condition);
                    AnalyzeStatement(forStmt.Body);
                    if (forStmt.Update != null)
                        AnalyzeExpression(forStmt.Update);
                    _scopes.Pop();
                    _varMutability.Pop();
                    if (IsEmptyBlock(forStmt.Body))
                        Warn("Empty loop body — the loop does nothing", forStmt.Line, forStmt.Column);
                    break;
                case BreakStatement:
                case ContinueStatement:
                    break;
                case ReturnStatement retStmt:
                    if (retStmt.Value != null)
                        AnalyzeExpression(retStmt.Value);
                    else if (_currentReturnType is TypeDescriptor.Named retVoid
                             && !string.Equals(retVoid.Name, "void", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(retVoid.Name, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        Warn(
                            $"'return' with no value in a function returning '{retVoid.Name}'",
                            retStmt.Line, retStmt.Column);
                    }
                    break;
                case SwitchStatement sw:
                    AnalyzeExpression(sw.Expression);
                    foreach (var @case in sw.Cases)
                    {
                        foreach (var stmt in @case.Statements)
                            AnalyzeStatement(stmt);
                    }
                    if (sw.Cases.Count == 0)
                        Warn("Switch statement has no cases", sw.Line, sw.Column);
                    break;
                case TryStatement tryStmt:
                    AnalyzeStatement(tryStmt.TryBlock);
                    foreach (var cc in tryStmt.CatchClauses)
                    {
                        // Register the exception variable in its own scope so
                        // references resolve and unused-var analysis is scoped.
                        _scopes.Push(new Dictionary<string, VariableDeclaration>());
                        _varMutability.Push(new Dictionary<string, bool>());
                        if (!string.IsNullOrEmpty(cc.ExceptionVar))
                        {
                            var decl = new VariableDeclaration(cc.ExceptionVar, new TypeDescriptor.Named("object"), null,
                                cc.Line, cc.Column) { IsMutable = false };
                            _scopes.Peek()[cc.ExceptionVar] = decl;
                            _varMutability.Peek()[cc.ExceptionVar] = false;
                        }
                        AnalyzeStatement(cc.Body);
                        _scopes.Pop();
                        _varMutability.Pop();
                    }
                    if (tryStmt.FinallyBlock != null)
                        AnalyzeStatement(tryStmt.FinallyBlock);
                    break;
                case ThrowStatement throwStmt:
                    AnalyzeExpression(throwStmt.Value);
                    break;
            }
        }

        private void AnalyzeConditionWarnings(string kind, Expression condition, int line, int column)
        {
            // Assignment used as a condition is almost always '==' meant.
            if (condition is BinaryExpression condBin && condBin.Operator == "=")
            {
                Warn($"'{kind}' condition is an assignment — did you mean '=='?", line, column);
            }
            // Literal conditions: if (true), for (; 1;), while (false) — dead logic.
            else if (condition is LiteralExpression condLit)
            {
                bool always = condLit.Value switch
                {
                    bool b => b,
                    int i => i != 0,
                    double d => d != 0.0,
                    _ => false
                };
                if (kind == "while" && always) return;   // while (true) is idiomatic
                Warn($"'{kind}' condition is always {(always ? "true" : "false")}", line, column);
            }
        }

        private static bool IsEmptyBlock(Statement? s)
            => s is BlockStatement b && b.Statements.Count == 0;

        // ── for-in iteration (FEATURE_PROPOSALS §5) ────────────────────

        /// <summary>
        /// Analyzes <c>for x in iterable</c>: scopes the loop variable to the
        /// loop, resolves the iterable's element type (array / List / Dict),
        /// and records it for codegen's index-protocol selection.
        /// </summary>
        private void AnalyzeForInStatement(ForInStatement stmt)
        {
            _scopes.Push(new Dictionary<string, VariableDeclaration>());
            _varMutability.Push(new Dictionary<string, bool>());

            // Ranges never reach InferType as a collection — they desugar to a
            // C-style loop producing ints.
            if (stmt.Iterable is not RangeExpression)
                AnalyzeExpression(stmt.Iterable);
            var iterType = stmt.Iterable is RangeExpression ? new TypeDescriptor.Named("int") : InferType(stmt.Iterable);

            TypeDescriptor? elem = null;
            TypeDescriptor? valueElem = null;
            bool isDict = false;

            if (stmt.Iterable is RangeExpression range)
            {
                AnalyzeExpression(range.Start);
                AnalyzeExpression(range.End);
                if (range.Step != null)
                    AnalyzeExpression(range.Step);
                elem = new TypeDescriptor.Named("int");
                if (stmt.ValueVariable != null)
                {
                    _diagnostics.AddError("Ranges produce single values — use 'for x in range'", stmt.Line, stmt.Column);
                }
            }
            else             switch (iterType)
            {
                case TypeDescriptor.ArrayOf arr:
                    elem = arr.Element;
                    stmt.IsArray = true;
                    break;

                case TypeDescriptor.GenericInstance g:
                    string shortName = g.Name.Contains('.') ? g.Name[(g.Name.LastIndexOf('.') + 1)..] : g.Name;
                    if (shortName.Equals("List", StringComparison.OrdinalIgnoreCase))
                    {
                        if (stmt.ValueVariable != null)
                        {
                            _diagnostics.AddError(
                                "The (key, value) pair form only iterates Dict — use 'for x in list' over a List",
                                stmt.Line, stmt.Column);
                        }
                        elem = g.Arguments.Count > 0 ? g.Arguments[0] : TypeDescriptor.Empty;
                    }
                    else if (shortName.Equals("Dict", StringComparison.OrdinalIgnoreCase))
                    {
                        if (stmt.ValueVariable == null)
                        {
                            _diagnostics.AddError(
                                "Iterating a Dict requires the pair form — use 'for (key, value) in dict'",
                                stmt.Line, stmt.Column);
                        }
                        isDict = true;
                        elem = g.Arguments.Count > 0 ? g.Arguments[0] : TypeDescriptor.Empty;
                        valueElem = g.Arguments.Count > 1 ? g.Arguments[1] : TypeDescriptor.Empty;
                    }
                    else
                    {
                        _diagnostics.AddError(
                            $"Cannot iterate '{iterType}' — for-in supports arrays, List<T>, and Dict<K, V>",
                            stmt.Line, stmt.Column);
                    }
                    break;

                default:
                    _diagnostics.AddError(
                        $"Cannot iterate '{iterType?.ToString() ?? "unknown"}' — for-in supports arrays, List<T>, Dict<K, V>, and ranges",
                        stmt.Line, stmt.Column);
                    break;
            }

            stmt.ResolvedElementType = elem ?? TypeDescriptor.Empty;
            stmt.IsDictionary = isDict;
            if (stmt.Iterable is RangeExpression) stmt.IsArray = false;

            DeclareVariable(stmt.Variable, stmt.ResolvedElementType, stmt.Line, stmt.Column, warnOnShadow: false);
            if (isDict && stmt.ValueVariable != null && valueElem != null)
                DeclareVariable(stmt.ValueVariable, valueElem, stmt.Line, stmt.Column, warnOnShadow: false);

            AnalyzeStatement(stmt.Body);

            if (IsEmptyBlock(stmt.Body))
                Warn("Empty loop body — the loop does nothing", stmt.Line, stmt.Column);

            _scopes.Pop();
            _varMutability.Pop();
        }

        // ── Match expressions (FEATURE_PROPOSALS §1, §2) ───────────────

        /// <summary>
        /// Analyzes a match expression: resolves variant patterns against the
        /// scrutinee's sum type, scopes binding patterns to their arm,
        /// validates guards and results, and — when the scrutinee is a sum
        /// type with no wildcard/binding catch-all — checks exhaustiveness
        /// (every variant must be covered).
        /// </summary>
        private void AnalyzeMatchExpression(MatchExpression match)
        {
            AnalyzeExpression(match.Scrutinee);
            var scrutineeType = InferType(match.Scrutinee);

            // The sum base when the scrutinee is a variant-bearing type.
            ContractDeclaration? sumBase = null;
            if (scrutineeType is TypeDescriptor.Named sn && !sn.IsEmpty)
            {
                var c = FindContract(sn.Name);
                if (c != null)
                {
                    if (c.IsSumTypeBase) sumBase = c;
                    else if (c.SumTypeOf != null) sumBase = FindContract(c.SumTypeOf);
                }
            }
            match.SumTypeName = sumBase?.FullName ?? sumBase?.Name;

            var coveredVariants = new HashSet<string>(StringComparer.Ordinal);
            bool hasCatchAll = false;

            foreach (var arm in match.Arms)
            {
                // A bare identifier that names one of the sum's variants is a
                // fieldless variant pattern (`Unit => ...`), not a binding.
                if (sumBase != null)
                {
                    for (int pi = 0; pi < arm.Patterns.Count; pi++)
                    {
                        if (arm.Patterns[pi] is BindingPattern bp
                            && sumBase.SumVariants.Contains(bp.Name))
                        {
                            arm.Patterns[pi] = new VariantPattern(bp.Name, bp.Line, bp.Column);
                        }
                    }
                }

                foreach (var pattern in arm.Patterns)
                {
                    switch (pattern)
                    {
                        case LiteralPattern lit:
                            break;
                        case WildcardPattern:
                            hasCatchAll = true;
                            break;
                        case BindingPattern bind:
                            hasCatchAll = true;
                            arm.BoundNames[bind.Name] = scrutineeType ?? TypeDescriptor.Empty;
                            break;
                        case VariantPattern vp:
                        {
                            if (sumBase == null)
                            {
                                _diagnostics.AddError(
                                    $"Variant pattern '{vp.VariantName}(' requires a match over a sum type (declared with 'type ... {{ ... }}')",
                                    vp.Line, vp.Column);
                                break;
                            }
                            int idx = sumBase.SumVariants.IndexOf(vp.VariantName);
                            if (idx < 0)
                            {
                                _diagnostics.AddError(
                                    $"'{vp.VariantName}' is not a variant of sum type '{sumBase.Name}'",
                                    vp.Line, vp.Column);
                                break;
                            }
                            var variantContract = FindContract($"{sumBase.Name}.{vp.VariantName}");
                            if (variantContract == null) break;

                            vp.ResolvedVariantName = variantContract.FullName;
                            vp.VariantIndex = idx;
                            coveredVariants.Add(vp.VariantName);

                            var fields = variantContract.Fields.Where(f => !f.IsStatic).ToList();
                            if (vp.Arguments.Count > fields.Count)
                            {
                                _diagnostics.AddError(
                                    $"Variant '{vp.VariantName}' has {fields.Count} field(s), but the pattern binds {vp.Arguments.Count}",
                                    vp.Line, vp.Column);
                            }

                            for (int i = 0; i < vp.Arguments.Count && i < fields.Count; i++)
                            {
                                var sub = vp.Arguments[i];
                                var fieldType = fields[i].Type;
                                switch (sub)
                                {
                                    case WildcardPattern:
                                        break;
                                    case BindingPattern b:
                                        arm.BoundNames[b.Name] = fieldType;
                                        break;
                                    case VariantPattern nested:
                                        _diagnostics.AddError("Nested variant patterns are not supported yet", sub.Line, sub.Column);
                                        break;
                                    case LiteralPattern litSub:
                                        _diagnostics.AddError("Literal patterns inside variants are not supported yet", litSub.Line, litSub.Column);
                                        break;
                                }
                                vp.BoundFields.Add(fields[i].Name);
                            }
                            // A bare variant name with no parens still counts as
                            // covering the variant; with parens it must consume
                            // all fields positionally.
                            if (vp.Arguments.Count == 0)
                            {
                                foreach (var f in fields)
                                    vp.BoundFields.Add(f.Name);
                            }
                            break;
                        }
                    }
                }

                // Bindings + guard + result are analyzed in an arm-local scope.
                _scopes.Push(new Dictionary<string, VariableDeclaration>());
                _varMutability.Push(new Dictionary<string, bool>());
                foreach (var (boundName, boundType) in arm.BoundNames)
                    DeclareVariable(boundName, boundType, arm.Line, arm.Column, warnOnShadow: false);

                if (arm.Guard != null)
                {
                    AnalyzeConditionWarnings("match guard", arm.Guard, arm.Line, arm.Column);
                    AnalyzeExpression(arm.Guard);
                }
                AnalyzeExpression(arm.Result);

                _scopes.Pop();
                _varMutability.Pop();
            }

            // Exhaustiveness: a match over a sum type without a wildcard or
            // binding catch-all must cover every variant.
            if (sumBase != null && !hasCatchAll)
            {
                var missing = sumBase.SumVariants.Where(v => !coveredVariants.Contains(v)).ToList();
                if (missing.Count > 0)
                {
                    _diagnostics.AddError(
                        $"Non-exhaustive match over '{sumBase.Name}' — no arm covers: {string.Join(", ", missing)}" +
                        " (add arms for every variant or a '_' catch-all)",
                        match.Line, match.Column);
                }
            }

            if (match.Arms.All(a => a.Patterns.All(p => p is not WildcardPattern && p is not BindingPattern))
                && sumBase == null && match.Arms.Count > 0)
            {
                // Non-sum matches without a catch-all fall through at runtime
                // to a fault; surface that once per match.
                Warn("Match has no '_' arm — non-matching input faults at runtime", match.Line, match.Column);
            }
        }

        private void AnalyzeExpression(Expression expression)
        {            switch (expression)
            {
                case BinaryExpression bin:
                    // Division/modulo by a constant zero is a runtime crash.
                    if (bin.Operator is "/" or "%"
                        && bin.Right is LiteralExpression zeroLit
                        && IsZeroLiteral(zeroLit))
                    {
                        Warn(
                            $"Division by zero — '{bin.Operator}' with a constant zero denominator will fail at runtime",
                            bin.Line, bin.Column);
                    }

                    if (bin.Operator == "=" && bin.Left is IdentifierExpression assignTarget)
                    {
                        // Local write: record for the unused-variable pass.
                        AnalyzeExpression(bin.Right);
                        _fnWrittenNames.Add(assignTarget.Name);
                        if (IsContractField(assignTarget.Name))
                        {
                            // Locals shadow fields; a bare name that is not a
                            // local resolves to the field — reject constants.
                            if (!IsVariableDefined(assignTarget.Name) && IsCurrentContractConstField(assignTarget.Name))
                            {
                                _diagnostics.AddError(
                                    $"Cannot assign to constant '{assignTarget.Name}' — comptime constants are immutable",
                                    assignTarget.Line, assignTarget.Column);
                            }
                            else
                            {
                                _writtenFields.Add((_currentContractName ?? "", assignTarget.Name));
                            }
                        }
                        else if (IsVariableDefined(assignTarget.Name) && !IsVariableMutable(assignTarget.Name))
                        {
                            _diagnostics.AddError(
                                $"Cannot assign to 'let' variable '{assignTarget.Name}' — it is immutable. Use 'var' instead.",
                                assignTarget.Line, assignTarget.Column);
                        }
                        else if (!IsVariableDefined(assignTarget.Name)
                                 && !_definedFunctions.Contains(assignTarget.Name)
                                 && !_symbolTable.GetBoundClasses().Contains(assignTarget.Name)
                                 && !_symbolTable.IsBoundModule(assignTarget.Name)
                                 && !IsEnumType(assignTarget.Name))
                        {
                            // Bare assignment must target a local or a field of
                            // the CURRENT contract — reaching into another
                            // contract's field without `this` is an error.
                            _diagnostics.AddError($"Undefined variable: '{assignTarget.Name}'", assignTarget.Line, assignTarget.Column);
                        }
                        bin.ResolvedType = InferType(bin);
                        break;
                    }
                    if (bin.Operator is "=" or "+=" or "-=" or "*=" or "/=" or "%="
                        && bin.Left is MemberExpression memWrite)
                    {
                        // obj.field = v / obj.field += v — record the write.
                        AnalyzeExpression(bin.Right);
                        AnalyzeExpression(memWrite.Object);
                        if (ResolveOwnerContract(memWrite.Object) is { } writeOwner)
                        {
                            CheckMemberAccess(writeOwner, memWrite.Property, memWrite.Line, memWrite.Column, checkMethods: false);
                            // Static member write on a comptime constant
                            // (Config.VERSION = 2 / Config.VERSION += 1).
                            if (IsConstField(writeOwner, memWrite.Property))
                            {
                                _diagnostics.AddError(
                                    $"Cannot assign to constant '{writeOwner.Name}.{memWrite.Property}' — comptime constants are immutable",
                                    memWrite.Line, memWrite.Column);
                            }
                        }
                        RecordFieldAccess(memWrite, isWrite: true);
                        bin.ResolvedType = InferType(bin);
                        break;
                    }

                    // Compound assignment to a comptime constant (VERSION += 1).
                    if (bin.Operator is "+=" or "-=" or "*=" or "/=" or "%="
                        && bin.Left is IdentifierExpression constCompound
                        && !IsVariableDefined(constCompound.Name)
                        && IsCurrentContractConstField(constCompound.Name))
                    {
                        AnalyzeExpression(bin.Left);
                        AnalyzeExpression(bin.Right);
                        _diagnostics.AddError(
                            $"Cannot assign to constant '{constCompound.Name}' — comptime constants are immutable",
                            constCompound.Line, constCompound.Column);
                        bin.ResolvedType = InferType(bin);
                        break;
                    }

                    // Compound assignment to an immutable local: +=, -=, etc.
                    if (bin.Operator is "+=" or "-=" or "*=" or "/=" or "%="
                        && bin.Left is IdentifierExpression compoundTarget
                        && IsVariableDefined(compoundTarget.Name)
                        && !IsVariableMutable(compoundTarget.Name))
                    {
                        AnalyzeExpression(bin.Left);
                        AnalyzeExpression(bin.Right);
                        _diagnostics.AddError(
                            $"Cannot assign to 'let' variable '{compoundTarget.Name}' — it is immutable. Use 'var' instead.",
                            compoundTarget.Line, compoundTarget.Column);
                        bin.ResolvedType = InferType(bin);
                        break;
                    }

                    AnalyzeExpression(bin.Left);
                    AnalyzeExpression(bin.Right);
                    bin.ResolvedType = InferType(bin);
                    break;
                case CallExpression call:
                    AnalyzeExpression(call.Callee);
                    foreach (var arg in call.Arguments)
                        AnalyzeExpression(arg);

                    ResolveCall(call);
                    break;
                case ScopedAccessExpression scoped:
                    // Module::member — an stdlib module, a static member on a
                    // user contract (Contract::staticMethod works like the dot form),
                    // or an enum member (Color::Red).
                    _usedModulePaths.Add(scoped.Module);
                    _usedTypes.Add(scoped.Module);
                    if (_symbolTable.IsBoundModule(scoped.Module))
                    {
                        if (!_symbolTable.TryGetMethod(scoped.Module, scoped.Member, out _))
                        {
                            _diagnostics.AddError($"Member '{scoped.Member}' not found in module '{scoped.Module}'", scoped.Line, scoped.Column);
                        }
                    }
                    else if (IsEnumType(scoped.Module))
                    {
                        _usedTypes.Add(FindEnum(scoped.Module)?.FullName ?? scoped.Module);
                        if (!IsEnumMember(scoped.Module, scoped.Member))
                        {
                            _diagnostics.AddError($"'{scoped.Member}' is not a member of enum '{scoped.Module}'", scoped.Line, scoped.Column);
                        }
                    }
                    else if (_symbolTable.IsUserContract(scoped.Module))
                    {
                        // Static members: Contract::Method() or Contract::field.
                        // Inherited statics resolve through the base chain, and
                        // instance methods reached this way are an error.
                        var scopedContract = FindContract(scoped.Module);
                        bool isStaticField = FindContractStaticField(scoped.Module, scoped.Member) != null;
                        if (isStaticField)
                        {
                            if (scopedContract != null)
                                CheckMemberAccess(scopedContract, scoped.Member, scoped.Line, scoped.Column);
                            _readFields.Add((scoped.Module, scoped.Member));
                        }
                        else
                        {
                            var cm = scopedContract != null
                                ? FindMethodIncludingBase(scopedContract, scoped.Member, instanceOnly: false)
                                : null;
                            if (cm == null || !cm.IsStatic)
                            {
                                _diagnostics.AddError($"Member '{scoped.Member}' not found in contract '{scoped.Module}'", scoped.Line, scoped.Column);
                            }
                            else
                            {
                                if (!IsAccessibleFrom(cm.Access, cm.ContractName ?? ""))
                                {
                                    _diagnostics.AddError(
                                        $"Method '{cm.ContractName}.{scoped.Member}' is {AccessName(cm.Access)} — not accessible from '{_currentContractName}'",
                                        scoped.Line, scoped.Column);
                                }
                                _usedFunctions.Add(cm.Name);
                            }
                        }
                    }
                    else
                    {
                        _diagnostics.AddError($"Undefined module: '{scoped.Module}'", scoped.Line, scoped.Column);
                    }
                    break;
                case MemberExpression mem:
                    if (IsModuleAccessChain(mem))
                    {
                        // It's a standard library call, don't analyze the spine as variables.
                        if (TryGetModuleAccessPath(mem, out var modPath))
                            _usedModulePaths.Add(modPath);
                    }
                    else if (IsTypeAccessChain(mem))
                    {
                        // Dotted qualified-type access: com.lib.Geo.staticMember or
                        // com.lib.Direction.North — the spine is a type name, not a
                        // variable. Validate enum members here; static method/field
                        // validation happens in ResolveCall / the scoped branch.
                        if (TryGetModuleAccessPath(mem, out var typePath))
                        {
                            _usedTypes.Add(typePath);
                            if (IsEnumType(typePath))
                            {
                                if (!IsEnumMember(typePath, mem.Property))
                                {
                                    _diagnostics.AddError($"'{mem.Property}' is not a member of enum '{typePath}'", mem.Line, mem.Column);
                                }
                            }
                            else if (FindContract(typePath) is { } staticOwner)
                            {
                                _usedTypes.Add(staticOwner.FullName);
                                if (FindContractStaticField(typePath, mem.Property) != null)
                                {
                                    CheckMemberAccess(staticOwner, mem.Property, mem.Line, mem.Column);
                                    _readFields.Add((typePath, mem.Property));
                                }
                            }
                        }
                    }
                    else if (IsEnumType(GetMemberObjectName(mem)))
                    {
                        // Enum member read: Color.Red — the spine isn't a variable.
                        _usedTypes.Add(FindEnum(GetMemberObjectName(mem))?.FullName ?? GetMemberObjectName(mem));
                        if (!IsEnumMember(GetMemberObjectName(mem), mem.Property))
                        {
                            _diagnostics.AddError($"'{mem.Property}' is not a member of enum '{GetMemberObjectName(mem)}'", mem.Line, mem.Column);
                        }
                    }
                    else
                    {
                        AnalyzeExpression(mem.Object);
                        if (ResolveOwnerContract(mem.Object) is { } readOwner)
                            CheckMemberAccess(readOwner, mem.Property, mem.Line, mem.Column, checkMethods: false);
                        RecordFieldAccess(mem, isWrite: false);
                    }
                    break;
                case IndexExpression indexExpr:
                    AnalyzeExpression(indexExpr.Target);
                    AnalyzeExpression(indexExpr.Index);
                    break;
                case PipeExpression pipe:
                    // Parse-time lowering turns `|>` into plain calls except
                    // when the RHS is a lambda; analyze both sides regardless
                    // so variables and functions used through the pipe count.
                    AnalyzeExpression(pipe.Left);
                    AnalyzeExpression(pipe.Right);
                    break;
                case IdentifierExpression id:
                    if (id.Name == "this") break; // instance context
                    // A variable read (bare field access is also a read of the
                    // declaring contract's field, recorded via IsContractField).
                    if (IsVariableDefined(id.Name))
                        _fnReadNames.Add(id.Name);
                    else if (IsContractField(id.Name))
                    {
                        // Record against the DECLARING contract (an inherited
                        // field read via a derived `this` belongs to the base).
                        string owner = DeclaringContractName(_currentContractName ?? "", id.Name, staticOnly: false);
                        _readFields.Add((owner, id.Name));
                    }

                    if (!IsVariableDefined(id.Name)
                        && !_definedFunctions.Contains(id.Name)
                        && !_symbolTable.GetBoundClasses().Contains(id.Name)
                        && !_symbolTable.IsBoundModule(id.Name)
                        && !IsContractField(id.Name)
                        && !IsEnumType(id.Name))
                    {
                        _diagnostics.AddError($"Undefined variable: '{id.Name}'", id.Line, id.Column);
                    }
                    break;
                case LiteralExpression _:
                    break;
                case TupleLiteralExpression tupleLit:
                    foreach (var element in tupleLit.Elements)
                        AnalyzeExpression(element);
                    break;
                case UnaryExpression unary:
                    AnalyzeExpression(unary.Operand);
                    break;
                case TernaryExpression ternary:
                    AnalyzeExpression(ternary.Condition);
                    AnalyzeExpression(ternary.ThenBranch);
                    AnalyzeExpression(ternary.ElseBranch);
                    break;
                case IfExpression ifExpr:
                    AnalyzeConditionWarnings("if", ifExpr.Condition, ifExpr.Line, ifExpr.Column);
                    AnalyzeExpression(ifExpr.Condition);
                    AnalyzeExpression(ifExpr.ThenBranch);
                    AnalyzeExpression(ifExpr.ElseBranch);
                    break;
                case RangeExpression:
                    // Only produced inside for-in headers; analyzed there.
                    _diagnostics.AddError("Ranges are only valid in a 'for x in a..b' header", expression.Line, expression.Column);
                    break;
                case MatchExpression match:
                    AnalyzeMatchExpression(match);
                    break;
                case ArrayLiteralExpression arrLit:
                    foreach (var element in arrLit.Elements)
                        AnalyzeExpression(element);
                    break;
                case NewExpression newExpr:
                    // Resolve the written type name (short, dotted, or
                    // Module::Type) to its fully-qualified wire name through
                    // namespaces/imports, so validation, the inferred variable
                    // type, and the emitted newobj all agree.
                    newExpr.TypeName = ResolveTypeName(newExpr.TypeName);

                    // Generic instantiation: new Box<int>(5) — validate the
                    // type arguments (arity + each arg valid) and record the
                    // unbound name as a type usage.
                    if (newExpr.TypeArguments.Count > 0)
                    {
                        if (!_typeRegistry.IsGenericType(newExpr.TypeName))
                        {
                            _diagnostics.AddError($"Type '{newExpr.TypeName}' is not generic — cannot supply type arguments", newExpr.Line, newExpr.Column);
                        }
                        else if (newExpr.TypeArguments.Count != _typeRegistry.GetArity(newExpr.TypeName))
                        {
                            _diagnostics.AddError(
                                $"Type '{newExpr.TypeName}' expects {_typeRegistry.GetArity(newExpr.TypeName)} type argument(s), got {newExpr.TypeArguments.Count}",
                                newExpr.Line, newExpr.Column);
                        }
                        foreach (var ta in newExpr.TypeArguments)
                        {
                            if (!IsValidTypeInContext(ta))
                                _diagnostics.AddError($"Unknown type argument '{ta}'", newExpr.Line, newExpr.Column);
                        }
                        _usedTypes.Add(newExpr.TypeName);
                    }
                    else if (newExpr.Size != null)
                    {
                        AnalyzeExpression(newExpr.Size);
                        if (!_typeRegistry.IsValidTypeName(newExpr.TypeName) && !IsTypeParamInScope(newExpr.TypeName))
                        {
                            _diagnostics.AddError($"Unknown type '{newExpr.TypeName}'", newExpr.Line, newExpr.Column);
                        }
                    }
                    else if (!_typeRegistry.IsValidTypeName(newExpr.TypeName))
                    {
                        _diagnostics.AddError($"Unknown type '{newExpr.TypeName}'", newExpr.Line, newExpr.Column);
                    }
                    else if (_typeRegistry.IsGenericType(newExpr.TypeName)
                             && FindGenericContract(newExpr.TypeName) != null)
                    {
                        // A user generic contract used without type arguments
                        // (new Box(5)) is an error — the type parameter must be
                        // supplied.
                        _diagnostics.AddError(
                            $"Generic contract '{newExpr.TypeName}' requires type arguments — use 'new {newExpr.TypeName}<...>(...)'",
                            newExpr.Line, newExpr.Column);
                    }

                    // Native-bound contracts construct through the host module's
                    // Create method: new Window() → binding.Create().
                    if (_contractsByName.TryGetValue(newExpr.TypeName, out var nbContract)
                        && nbContract.NativeBindingName != null
                        && !_symbolTable.TryGetMethod(nbContract.NativeBindingName, "Create", out _))
                    {
                        _diagnostics.AddError($"Native binding '{nbContract.NativeBindingName}' has no method named 'Create' (required for 'new {newExpr.TypeName}')", newExpr.Line, newExpr.Column);
                    }

                    // Sum-type bases are never constructed directly — use a
                    // variant constructor (Shape.Circle(2.0)) or variant.
                    if (_contractsByName.TryGetValue(newExpr.TypeName, out var sumCheck)
                        && sumCheck.IsSumTypeBase)
                    {
                        _diagnostics.AddError(
                            $"'{sumCheck.Name}' is a sum type — create a variant instead (e.g. {sumCheck.Name}.{sumCheck.SumVariants.FirstOrDefault() ?? "Variant"}(...))",
                            newExpr.Line, newExpr.Column);
                    }

                    // Abstract contracts (unimplemented inherited/interface
                    // methods) cannot be instantiated.
                    else if (_contractsByName.TryGetValue(newExpr.TypeName, out var absCheck)
                             && absCheck.PendingAbstractMethods.Count > 0)
                    {
                        _diagnostics.AddError(
                            $"Cannot instantiate '{absCheck.Name}' — it does not implement: {string.Join(", ", absCheck.PendingAbstractMethods.Select(m => m.Name + "()"))}",
                            newExpr.Line, newExpr.Column);
                    }

                    // A plain contract with declared constructors but no match:
                    // the codegen silently skips the ctor, leaving fields default.
                    if (_contractsByName.TryGetValue(newExpr.TypeName, out var ctorContract)
                        && ctorContract.NativeBindingName == null
                        && ctorContract.Constructors.Count > 0
                        && !ctorContract.Constructors.Any(c => c.Parameters.Count == newExpr.Arguments.Count))
                    {
                        Warn(
                            $"No constructor of '{newExpr.TypeName}' takes {newExpr.Arguments.Count} argument(s) — the constructor will not run and fields stay at their defaults",
                            newExpr.Line, newExpr.Column);
                    }

                    // Structs: new Color(a, b, ...) assigns fields positionally;
                    // extra arguments beyond the field count are silently ignored.
                    if (_program != null)
                    {
                        var targetStruct = FindStruct(newExpr.TypeName);
                        if (targetStruct != null && newExpr.Arguments.Count > targetStruct.Fields.Count)
                        {
                            Warn(
                                $"'{newExpr.TypeName}' has {targetStruct.Fields.Count} field(s), but {newExpr.Arguments.Count} argument(s) were supplied — the extra argument(s) will be ignored",
                                newExpr.Line, newExpr.Column);
                        }
                    }
                    break;
                case LambdaExpression lambda:
                    // Validate annotated param types and analyze the body so
                    // calls inside lambdas get symbol-linked and errors surface.
                    for (int i = 0; i < lambda.Parameters.Count; i++)
                    {
                        string pt = i < lambda.ParameterTypes.Count ? lambda.ParameterTypes[i] : "";
                        if (!string.IsNullOrEmpty(pt) && !_typeRegistry.IsValidType(pt) && !IsTypeParamInScope(pt))
                        {
                            _diagnostics.AddError($"Unknown type '{pt}' for lambda parameter '{lambda.Parameters[i]}'", lambda.Line, lambda.Column);
                        }
                    }
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    _varMutability.Push(new Dictionary<string, bool>());
                    foreach (var p in lambda.Parameters)
                    {
                        int pi = lambda.Parameters.IndexOf(p);
                        string pt = pi < lambda.ParameterTypes.Count ? lambda.ParameterTypes[pi] : "";
                        var paramType = !string.IsNullOrEmpty(pt) ? TypeDescriptor.Parse(pt) : new TypeDescriptor.Named("int");
                        DeclareVariable(p, paramType, lambda.Line, lambda.Column, trackUsage: false, warnOnShadow: false);
                    }
                    if (lambda.BlockBody != null)
                    {
                        foreach (var stmt in lambda.BlockBody.Statements)
                            AnalyzeStatement(stmt);
                    }
                    else if (lambda.Body != null)
                    {
                        AnalyzeExpression(lambda.Body);
                    }
                    _scopes.Pop();
                    _varMutability.Pop();
                    break;
            }
        }

        /// <summary>
        /// Records a member read/write against the owning contract type when the
        /// object is a known variable (or <c>this</c>), so dead-field detection
        /// works for instance fields.
        /// </summary>
        private void RecordFieldAccess(MemberExpression mem, bool isWrite)
        {
            if (mem.Object is not IdentifierExpression objId) return;
            if (objId.Name == "this")
            {
                if (_currentContractName != null)
                {
                    if (isWrite) _writtenFields.Add((_currentContractName, mem.Property));
                    else _readFields.Add((_currentContractName, mem.Property));
                }
                return;
            }
            if (FindVariableType(objId.Name) is TypeDescriptor.Named ownerType)
            {
                if (isWrite) _writtenFields.Add((ownerType.Name, mem.Property));
                else _readFields.Add((ownerType.Name, mem.Property));
            }
        }

        // ── Access control ───────────────────────────────────────────

        /// <summary>Human-readable name of an access level for error messages.</summary>
        private static string AccessName(AccessModifier access) => access switch
        {
            AccessModifier.Public => "public",
            AccessModifier.Private => "private",
            AccessModifier.Protected => "protected",
            AccessModifier.Internal => "internal",
            _ => "default"
        };

        /// <summary>
        /// True when a member with <paramref name="access"/> declared on
        /// <paramref name="declaringContractName"/> is reachable from the
        /// current analysis context (the contract whose body is being analyzed).
        /// </summary>
        private bool IsAccessibleFrom(AccessModifier access, string declaringContractName)
        {
            switch (access)
            {
                case AccessModifier.Public:
                case AccessModifier.Default:
                    return true;
                case AccessModifier.Private:
                    return _currentContractName == declaringContractName;
                case AccessModifier.Protected:
                    if (_currentContractName == declaringContractName) return true;
                    var current = FindContract(_currentContractName ?? "");
                    var declaring = FindContract(declaringContractName);
                    return current != null && declaring != null && IsDerivedFrom(current, declaring);
                case AccessModifier.Internal:
                    var cur = FindContract(_currentContractName ?? "");
                    var decl = FindContract(declaringContractName);
                    return cur != null && decl != null && cur.Namespace == decl.Namespace;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Checks that a member (field or method) named <paramref name="member"/>
        /// on <paramref name="ownerContract"/> is accessible from the current
        /// context. Reports an error when the access level forbids it. When
        /// <paramref name="checkMethods"/> is false only field access is
        /// validated (method access is reported by ResolveCall).
        /// </summary>
        private void CheckMemberAccess(ContractDeclaration ownerContract, string member, int line, int column, bool checkMethods = true)
        {
            var fieldDecl = FindFieldDeclaringContract(ownerContract, member, staticOnly: false);
            if (fieldDecl != null)
            {
                var field = fieldDecl.Fields.First(f => f.Name == member);
                if (!IsAccessibleFrom(field.Access, fieldDecl.Name))
                {
                    _diagnostics.AddError(
                        $"Field '{fieldDecl.Name}.{member}' is {AccessName(field.Access)} — not accessible from '{_currentContractName}'",
                        line, column);
                }
                return;
            }

            if (!checkMethods) return;

            var method = FindMethodIncludingBase(ownerContract, member, instanceOnly: false);
            if (method != null && !IsAccessibleFrom(method.Access, method.ContractName ?? ""))
            {
                _diagnostics.AddError(
                    $"Method '{method.ContractName}.{member}' is {AccessName(method.Access)} — not accessible from '{_currentContractName}'",
                    line, column);
            }
        }

        /// <summary>
        /// Resolves the contract that owns a member-access receiver: <c>this</c>
        /// → the current contract, a variable → its declared contract type, a
        /// bare name → a contract by that name. Null when the receiver isn't a
        /// known contract.
        /// </summary>
        private ContractDeclaration? ResolveOwnerContract(Expression obj)
        {
            if (obj is not IdentifierExpression id) return null;
            if (id.Name == "this") return FindContract(_currentContractName ?? "");
            if (FindVariableType(id.Name) is TypeDescriptor.Named n) return FindContract(n.Name);
            return FindContract(id.Name);
        }

        private static bool IsZeroLiteral(LiteralExpression lit)
            => lit.Value switch
            {
                int i => i == 0,
                double d => d == 0.0,
                _ => false
            };

        private static TypeDescriptor? FindBlockReturnType(Contract.Compiler.AST.BlockStatement block)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt is ReturnStatement ret && ret.Value != null)
                {
                    var t = ret.Value switch
                    {
                        LiteralExpression lit => lit.Value switch
                        {
                            int => "int",
                            string => "string",
                            bool => "bool",
                            double => "double",
                            _ => null
                        },
                        _ => "int" // default for other expression forms
                    };
                    if (t != null) return TypeDescriptor.Parse(t);
                }
            }
            return null;
        }

        private bool IsVariableDefined(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.ContainsKey(name)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the variable was declared with <c>var</c> (mutable).
        /// Returns true (safe default) when the name is not found — the
        /// undefined-variable error is raised elsewhere.
        /// </summary>
        private bool IsVariableMutable(string name)
        {
            foreach (var mutability in _varMutability)
            {
                if (mutability.TryGetValue(name, out bool mutable))
                    return mutable;
            }
            return true; // default: don't block assignment for unknowns
        }

        private bool IsContractField(string name)
        {
            // True when the CURRENT contract (or a base) declares a field with
            // this name — bare field access in an instance method must not
            // reach into another contract's fields without `this`. Static
            // fields of the current contract are also reachable bare.
            if (_currentContractName == null) return false;
            var contract = FindContract(_currentContractName);
            if (contract == null) return false;
            return FindFieldDeclaringContract(contract, name, staticOnly: false) != null
                || FindFieldDeclaringContract(contract, name, staticOnly: true) != null;
        }

        /// <summary>
        /// True when 'fieldName' on 'ownerContract' (walking base chains)
        /// resolves to a comptime constant field (<c>let X: T = &lt;const&gt;;</c>,
        /// FEATURE_PROPOSALS §15). Constants are immutable — writes are errors.
        /// </summary>
        private bool IsConstField(ContractDeclaration ownerContract, string fieldName)
        {
            var declaring = FindFieldDeclaringContract(ownerContract, fieldName, staticOnly: true);
            return declaring?.Fields.Any(f => f.Name == fieldName && f.IsConst) == true;
        }

        /// <summary>Const-field check against the contract being analyzed.</summary>
        private bool IsCurrentContractConstField(string name)
        {
            if (_currentContractName == null) return false;
            var contract = FindContract(_currentContractName);
            return contract != null && IsConstField(contract, name);
        }

        // ── Enums ───────────────────────────────────────────────────

        private EnumDeclaration? FindEnum(string name)
        {
            var topLevel = _program?.Enums.FirstOrDefault(e => e.Name == name || e.FullName == name);
            if (topLevel != null) return topLevel;
            foreach (var contract in _program?.Contracts ?? Enumerable.Empty<ContractDeclaration>())
            {
                var nested = contract.Members.OfType<EnumDeclaration>().FirstOrDefault(e => e.Name == name || e.FullName == name);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>Finds a struct by short or namespace-qualified name (top-level or nested in a contract).</summary>
        private StructDeclaration? FindStruct(string name)
        {
            var topLevel = _program?.Structs.FirstOrDefault(s => s.Name == name || s.FullName == name);
            if (topLevel != null) return topLevel;
            foreach (var contract in _program?.Contracts ?? Enumerable.Empty<ContractDeclaration>())
            {
                var nested = contract.Members.OfType<StructDeclaration>().FirstOrDefault(s => s.Name == name || s.FullName == name);
                if (nested != null) return nested;
            }
            return null;
        }

        private bool IsEnumType(string name) => FindEnum(name) != null;

        private bool IsEnumMember(string enumName, string member) =>
            FindEnum(enumName)?.Members.Contains(member) == true;

        private static string GetMemberObjectName(MemberExpression mem) =>
            mem.Object is IdentifierExpression id ? id.Name : "";

        /// <summary>Finds a contract by short or namespace-qualified name.</summary>
        private ContractDeclaration? FindContract(string name)
        {
            if (_program == null) return null;
            return _program.Contracts.FirstOrDefault(c => c.Name == name || c.FullName == name);
        }

        // ── Inheritance (base-chain resolution) ────────────────────────

        /// <summary>The direct base contract of <paramref name="contract"/>, or null.</summary>
        private ContractDeclaration? BaseContract(ContractDeclaration contract)
            => contract.BaseTypeName != null ? FindContract(contract.BaseTypeName) : null;

        /// <summary>True when <paramref name="derived"/> is <paramref name="baseContract"/> or derives from it.</summary>
        private bool IsDerivedFrom(ContractDeclaration derived, ContractDeclaration baseContract)
        {
            for (var c = derived; c != null; c = BaseContract(c))
                if (ReferenceEquals(c, baseContract)) return true;
            return false;
        }

        /// <summary>
        /// Finds a member function on a contract or any of its bases (most-derived
        /// first, so an override hides the base declaration). When
        /// <paramref name="instanceOnly"/> is true, static functions are skipped.
        /// </summary>
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

        /// <summary>
        /// The contract that declares <paramref name="fieldName"/> (walking the
        /// base chain most-derived first), or null when no contract in the chain
        /// declares it. <paramref name="staticOnly"/> restricts to static fields.
        /// </summary>
        private ContractDeclaration? FindFieldDeclaringContract(ContractDeclaration contract, string fieldName, bool staticOnly)
        {
            for (var c = contract; c != null; c = BaseContract(c))
            {
                var f = c.Fields.FirstOrDefault(f => f.Name == fieldName && f.IsStatic == staticOnly);
                if (f != null) return c;
            }
            return null;
        }

        /// <summary>The declaring contract's name for a field, or the original name when unknown.</summary>
        private string DeclaringContractName(string contractName, string fieldName, bool staticOnly)
        {
            var contract = FindContract(contractName);
            return contract != null && FindFieldDeclaringContract(contract, fieldName, staticOnly) is { } decl
                ? decl.Name
                : contractName;
        }

        // ── Type-name resolution (namespaces & imports) ──────────────

        private void AddShortIndex(string shortName, string fullName)
        {
            if (shortName == fullName) return;
            if (!_shortToFull.TryGetValue(shortName, out var list))
                _shortToFull[shortName] = list = new List<string>();
            if (!list.Contains(fullName)) list.Add(fullName);
        }

        /// <summary>The namespace of the contract currently being analyzed, if any.</summary>
        private string? CurrentContractNamespace()
        {
            if (_currentContractName == null || _program == null) return null;
            return _program.Contracts.FirstOrDefault(c => c.Name == _currentContractName)?.Namespace;
        }

        /// <summary>The single qualified name for a short name, or null when ambiguous/absent.</summary>
        private string? UniqueShortMatch(string shortName)
            => _shortToFull.TryGetValue(shortName, out var list) && list.Count == 1 ? list[0] : null;

        /// <summary>
        /// Resolves a possibly-short type name to its fully-qualified wire name
        /// through the current contract's namespace and the program's namespace
        /// imports (Python/Java-style). Records the name (short + full) as a
        /// type usage and marks the resolving import as used. Returns the name
        /// unchanged when nothing resolves.
        /// </summary>
        private string ResolveTypeName(string name)
        {
            string resolved = TypeNameResolver.Resolve(
                name,
                _program?.NamespaceImports ?? new List<string>(),
                candidate => _typeRegistry.IsValidTypeName(candidate),
                CurrentContractNamespace(),
                UniqueShortMatch,
                importIndex => _usedNamespaceImports.Add(importIndex));

            // The registry accepts any case; the emitted wire name must match
            // the declared type exactly, so canonicalize (Raylib.color → Raylib.Color).
            string? canonical = _typeRegistry.CanonicalName(resolved);
            if (canonical != null) resolved = canonical;

            _usedTypes.Add(name);
            if (resolved != name) _usedTypes.Add(resolved);
            return resolved;
        }

        /// <summary>Finds a function declaration by name (top-level or contract member).</summary>
        private FunctionDeclaration? FindFunctionDecl(string name)
        {
            if (_program == null) return null;
            foreach (var f in _program.Functions)
                if (f.Name == name) return f;
            foreach (var c in _program.Contracts)
                foreach (var m in c.Members)
                    if (m is FunctionDeclaration f && f.Name == name) return f;
            return null;
        }

        /// <summary>
        /// True when a bare call to <paramref name="name"/> targets an instance
        /// method that has no implicit receiver here: we're not inside an
        /// instance method, or the method belongs to neither the current contract
        /// nor its bases (a derived method can call an inherited method bare —
        /// `this` is the implicit receiver).
        /// </summary>
        private bool IsInvalidBareInstanceCall(string name)
        {
            var target = FindFunctionDecl(name);
            if (target is not { IsInstance: true }) return false;
            if (!_currentIsInstance || _currentContractName == null) return true;   // no implicit this
            var current = FindContract(_currentContractName);
            if (current == null) return true;
            // Valid when the method is declared on the current contract or any base.
            return FindMethodIncludingBase(current, name, instanceOnly: true) == null;
        }

        /// <summary>Type of a static field on a contract (or its bases), or null when it isn't one.</summary>
        private TypeDescriptor? FindContractStaticField(string contractName, string fieldName)
        {
            var contract = FindContract(contractName);
            var declaring = contract != null ? FindFieldDeclaringContract(contract, fieldName, staticOnly: true) : null;
            return declaring?.Fields.FirstOrDefault(f => f.Name == fieldName && f.IsStatic)?.Type;
        }

        /// <summary>Type of a bare static field access, or null when no contract declares it.</summary>
        private TypeDescriptor? FindStaticFieldTypeAnywhere(string fieldName)
        {
            if (_program == null) return null;
            foreach (var contract in _program.Contracts)
            {
                var field = contract.Fields.FirstOrDefault(f => f.Name == fieldName && f.IsStatic);
                if (field != null) return field.Type;
            }
            return null;
        }

        /// <summary>
        /// Type of a field read where <paramref name="ownerName"/> is either a
        /// contract name (static field) or a variable of a contract type (instance
        /// field). Walks the base chain so inherited fields resolve. Returns null
        /// when it isn't a known field.
        /// </summary>
        private TypeDescriptor? FindFieldType(string ownerName, string fieldName)
        {
            if (_program == null) return null;

            // Try contract fields first
            var contract = FindContract(ownerName);
            if (contract != null)
            {
                var declaring = FindFieldDeclaringContract(contract, fieldName, staticOnly: false);
                if (declaring != null)
                    return declaring.Fields.First(f => f.Name == fieldName).Type;
            }

            // Try struct fields: variable of struct type (e.g. expr.listVal)
            if (FindVariableType(ownerName) is TypeDescriptor.Named n)
            {
                var owner = FindContract(n.Name);
                if (owner != null)
                {
                    var declaring = FindFieldDeclaringContract(owner, fieldName, staticOnly: false);
                    if (declaring != null)
                        return declaring.Fields.First(f => f.Name == fieldName).Type;
                }
                // Struct field: e.g. expr.listVal where expr is of struct Value
                var structDecl = FindStruct(n.Name);
                if (structDecl != null)
                {
                    var field = structDecl.Fields.FirstOrDefault(f => f.Name == fieldName);
                    if (field != null) return field.Type;
                }
            }

            // Try struct fields by direct name (e.g. owner is "Value" itself)
            var structByName = FindStruct(ownerName);
            if (structByName != null)
            {
                var field = structByName.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (field != null) return field.Type;
            }

            return null;
        }

        private TypeDescriptor? InferType(Expression expression)
        {
            switch (expression)
            {
                case LiteralExpression lit:
                    return lit.Value switch
                    {
                        int => new TypeDescriptor.Named("int"),
                        string => new TypeDescriptor.Named("string"),
                        bool => new TypeDescriptor.Named("bool"),
                        double => new TypeDescriptor.Named("double"),
                        _ => null
                    };
                case TupleLiteralExpression tupleLit:
                    return new TypeDescriptor.Tuple(
                        tupleLit.Elements.Select(e => InferType(e) ?? TypeDescriptor.Empty).ToList());
                case IdentifierExpression id:
                {
                    var found = FindVariableType(id.Name);
                    if (found != null) return found;
                    // Bare static field access (shared state on a contract).
                    return FindStaticFieldTypeAnywhere(id.Name);
                }
                case UnaryExpression unary:
                    return InferType(unary.Operand);
                case TernaryExpression ternary:
                    return InferType(ternary.ThenBranch) ?? InferType(ternary.ElseBranch);
                case IfExpression ifExpr:
                    return InferType(ifExpr.ThenBranch) ?? InferType(ifExpr.ElseBranch);
                case MatchExpression match:
                {
                    foreach (var arm in match.Arms)
                    {
                        var t = InferType(arm.Result);
                        if (t != null && !t.IsEmpty) return t;
                    }
                    return null;
                }
                case RangeExpression:
                    // Ranges only exist inside for-in headers.
                    return new TypeDescriptor.Named("int");
                case IndexExpression indexExpr:
                    // arr[i] — the element type of the target's array type.
                    if (indexExpr.Target is IdentifierExpression targetId
                        && FindVariableType(targetId.Name) is TypeDescriptor.ArrayOf arrType)
                    {
                        return arrType.Element;
                    }
                    return new TypeDescriptor.Named("int");
                case NewExpression newExpr:
                    // Resolve through namespaces/imports (defensive — normally
                    // already rewritten by AnalyzeExpression).
                    if (newExpr.TypeArguments.Count > 0)
                    {
                        // new Box<int>(5) — the variable's type is the
                        // instantiation, not the unbound name.
                        return new TypeDescriptor.GenericInstance(
                            ResolveTypeName(newExpr.TypeName),
                            newExpr.TypeArguments);
                    }
                    return newExpr.Size != null
                        ? new TypeDescriptor.ArrayOf(new TypeDescriptor.Named(ResolveTypeName(newExpr.TypeName)))
                        : new TypeDescriptor.Named(ResolveTypeName(newExpr.TypeName));
                case ArrayLiteralExpression arrLit:
                    if (arrLit.Elements.Count == 0)
                    {
                        // Empty array literal: infer object[] (the language's
                        // dynamic array handle) unless a type hint is set.
                        arrLit.ElementType = TypeDescriptor.Empty;
                        return new TypeDescriptor.ArrayOf(new TypeDescriptor.Named("object"));
                    }
                    var element = InferType(arrLit.Elements[0]);
                    if (element == null) return null;
                    foreach (var e in arrLit.Elements)
                    {
                        if (!Equals(InferType(e), element)) return null;
                    }
                    arrLit.ElementType = element;
                    return new TypeDescriptor.ArrayOf(element);
                case LambdaExpression lambda:
                    var lambdaParams = lambda.Parameters
                        .Select((p, i) => (TypeDescriptor)(i < lambda.ParameterTypes.Count && !string.IsNullOrEmpty(lambda.ParameterTypes[i])
                            ? TypeDescriptor.Parse(lambda.ParameterTypes[i])
                            : new TypeDescriptor.Named("int")))
                        .ToList();
                    TypeDescriptor? lambdaReturn;
                    if (lambda.BlockBody != null)
                    {
                        // Block bodies: default to int unless we can find a return.
                        lambdaReturn = FindBlockReturnType(lambda.BlockBody);
                    }
                    else
                    {
                        lambdaReturn = InferType(lambda.Body);
                    }
                    return new TypeDescriptor.Function(lambdaParams, lambdaReturn ?? new TypeDescriptor.Named("int"));
                case CallExpression call:
                    if (call.Symbol is ExternalMethod em)
                    {
                        return MapSystemTypeToLanguageType(em.Info.ReturnType);
                    }
                    // A resolved user function's declared return type (covers
                    // member calls like `obj.makeFn()` returning a function type).
                    if (call.Symbol is FunctionDeclaration symFd
                        && symFd.ReturnType != null
                        && !symFd.ReturnType.IsEmpty)
                    {
                        return symFd.ReturnType;
                    }
                    if (call.Callee is IdentifierExpression calleeIdent &&
                        _functionReturnTypes.TryGetValue(calleeIdent.Name, out var funcReturnType))
                    {
                        return funcReturnType;
                    }
                    // f()(args): the result of invoking a delegate is the
                    // function type's return.
                    if (call.Callee is CallExpression innerCall)
                    {
                        var innerFn = GetCallResultFunctionType(innerCall);
                        if (innerFn != null)
                        {
                            return innerFn.Return;
                        }
                    }
                    return new TypeDescriptor.Named("int");
                case MemberExpression mem:
                    // Field read: Config.count (static), p.count (instance field
                    // of a contract-typed variable), or expr.listVal (struct field).
                    // Falls back to int otherwise.
                    if (mem.Object is IdentifierExpression memObj)
                    {
                        // Enum member: Color.Red → the enum type.
                        if (IsEnumType(memObj.Name)) return new TypeDescriptor.Named(memObj.Name);
                        var fieldType = FindFieldType(memObj.Name, mem.Property);
                        if (fieldType != null) return fieldType;
                    }
                    // Chained: eval(...).listVal — infer the call's return type,
                    // then look up the field on that type.
                    if (mem.Object is CallExpression callObj && callObj.Symbol is FunctionDeclaration callFn
                        && callFn.ReturnType is TypeDescriptor.Named callRet && !callRet.IsEmpty)
                    {
                        var fieldType = FindFieldType(callRet.Name, mem.Property);
                        if (fieldType != null) return fieldType;
                    }
                    return new TypeDescriptor.Named("int");
                case BinaryExpression bin:
                    if (bin.Operator is "+" or "+=")
                    {
                        var leftType = InferType(bin.Left);
                        var rightType = InferType(bin.Right);
                        if (leftType?.IsString == true || rightType?.IsString == true)
                            return new TypeDescriptor.Named("string");
                    }
                    return new TypeDescriptor.Named("int");
                default:
                    // Pipes and other expressions default to int in v1.
                    return new TypeDescriptor.Named("int");
            }
        }

        private TypeDescriptor? FindVariableType(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.TryGetValue(name, out var decl))
                {
                    return decl.Type.IsEmpty ? null : decl.Type;
                }
            }
            return null;
        }

        private static TypeDescriptor MapSystemTypeToLanguageType(System.Type t)
        {
            if (t.IsArray)
            {
                var elementType = t.GetElementType();
                return elementType != null
                    ? new TypeDescriptor.ArrayOf(MapSystemTypeToLanguageType(elementType))
                    : new TypeDescriptor.ArrayOf(new TypeDescriptor.Named("int"));
            }
            if (t == typeof(string)) return new TypeDescriptor.Named("string");
            if (t == typeof(object)) return new TypeDescriptor.Named("object");
            if (t == typeof(bool)) return new TypeDescriptor.Named("bool");
            if (t == typeof(double)) return new TypeDescriptor.Named("double");
            if (t == typeof(float)) return new TypeDescriptor.Named("float");
            if (t == typeof(long)) return new TypeDescriptor.Named("int64");
            if (t == typeof(void)) return new TypeDescriptor.Named("void");
            return new TypeDescriptor.Named("int");
        }

        // ── Generic call-site resolution ─────────────────────────────

        /// <summary>True when a function is a generic target: it declares type
        /// parameters itself, or lives on a generic contract.</summary>
        private bool IsGenericTarget(FunctionDeclaration fn)
            => fn.IsGeneric || (fn.ContractName != null && FindGenericContract(fn.ContractName) != null);

        private ContractDeclaration? FindGenericContract(string name)
        {
            if (_program == null) return null;
            return _program.Contracts.FirstOrDefault(c => c.IsGeneric && (c.Name == name || c.FullName == name));
        }

        /// <summary>
        /// Substitutes type parameters in a descriptor using <paramref name="map"/>.
        /// Only mapped parameters (actual type parameters like T) are substituted;
        /// concrete types like int, string, etc. pass through unchanged.
        /// </summary>
        private static TypeDescriptor SubstituteType(TypeDescriptor type, IReadOnlyDictionary<string, TypeDescriptor> map)
        {
            switch (type)
            {
                case TypeDescriptor.Named n:
                    return map.TryGetValue(n.Name, out var mapped) ? mapped : type;
                case TypeDescriptor.ArrayOf a:
                    return new TypeDescriptor.ArrayOf(SubstituteType(a.Element, map));
                case TypeDescriptor.Function f:
                    return new TypeDescriptor.Function(
                        f.Parameters.Select(p => SubstituteType(p, map)).ToList(),
                        SubstituteType(f.Return, map));
                case TypeDescriptor.GenericInstance g:
                    return new TypeDescriptor.GenericInstance(
                        g.Name,
                        g.Arguments.Select(a => SubstituteType(a, map)).ToList());
                default:
                    return type;
            }
        }

        /// <summary>
        /// A copy of <paramref name="fn"/> with its type parameters (and its
        /// generic contract's parameters) substituted to concrete types, so
        /// inference and codegen see the specialized signature. The concrete
        /// contract type arguments are recorded on the copy's
        /// <see cref="FunctionDeclaration.TypeArguments"/> so the codegen can
        /// emit the materialized declaring name.
        /// </summary>
        private static FunctionDeclaration SubstituteFunction(FunctionDeclaration fn, IReadOnlyDictionary<string, TypeDescriptor> map, ContractDeclaration? genericContract = null)
        {
            var copy = new FunctionDeclaration(fn.Name, fn.Line, fn.Column)
            {
                IsStatic = fn.IsStatic,
                IsInstance = fn.IsInstance,
                Access = fn.Access,
                ContractName = fn.ContractName,
                ReturnType = fn.ReturnType != null ? SubstituteType(fn.ReturnType, map) : null,
                SourceFile = fn.SourceFile,
            };
            if (genericContract != null)
            {
                foreach (var tp in genericContract.TypeParameters)
                    copy.TypeArguments.Add(map.TryGetValue(tp, out var arg) ? arg : new TypeDescriptor.Named("object"));
            }
            foreach (var p in fn.Parameters)
                copy.Parameters.Add(new Parameter(p.Name, SubstituteType(p.Type, map), p.Line, p.Column));
            return copy;
        }

        /// <summary>
        /// The type-parameter map for a generic contract instantiation:
        /// <c>Box&lt;int&gt;</c> → <c>{ T → int }</c>.
        /// </summary>
        private Dictionary<string, TypeDescriptor> TypeParamMap(ContractDeclaration contract, TypeDescriptor.GenericInstance g)
        {
            var map = new Dictionary<string, TypeDescriptor>();
            for (int i = 0; i < contract.TypeParameters.Count && i < g.Arguments.Count; i++)
                map[contract.TypeParameters[i]] = g.Arguments[i];
            return map;
        }

        /// <summary>
        /// Infers the type-parameter substitution for a call to a generic
        /// function: explicit type args seed the map, then each argument's type
        /// is unified against the parameter's type (a parameter typed with a
        /// type parameter binds it). Unbound parameters default to <c>object</c>.
        /// </summary>
        private Dictionary<string, TypeDescriptor> InferSubstitution(FunctionDeclaration fn, CallExpression call, IReadOnlyDictionary<string, TypeDescriptor>? seed = null)
        {
            var map = new Dictionary<string, TypeDescriptor>();
            if (seed != null)
                foreach (var (k, v) in seed) map[k] = v;

            // Explicit type args seed the function's own parameters in order.
            for (int i = 0; i < fn.TypeParameters.Count && i < call.TypeArguments.Count; i++)
                map[fn.TypeParameters[i]] = call.TypeArguments[i];

            // Unify argument types against parameter types.
            for (int i = 0; i < fn.Parameters.Count && i < call.Arguments.Count; i++)
            {
                var paramType = fn.Parameters[i].Type;
                var argType = InferType(call.Arguments[i]);
                if (argType == null) continue;
                BindFrom(paramType, argType, map);
            }

            return map;
        }

        /// <summary>Binds type parameters in <paramref name="paramType"/> from
        /// the concrete <paramref name="argType"/> (e.g. <c>T</c> ← <c>int</c>,
        /// <c>T[]</c> ← <c>int[]</c>).</summary>
        private static void BindFrom(TypeDescriptor paramType, TypeDescriptor argType, Dictionary<string, TypeDescriptor> map)
        {
            switch (paramType)
            {
                case TypeDescriptor.Named n when map.ContainsKey(n.Name):
                    map[n.Name] = argType;
                    break;
                case TypeDescriptor.ArrayOf pa when argType is TypeDescriptor.ArrayOf aa:
                    BindFrom(pa.Element, aa.Element, map);
                    break;
                case TypeDescriptor.Function pf when argType is TypeDescriptor.Function af:
                    for (int i = 0; i < pf.Parameters.Count && i < af.Parameters.Count; i++)
                        BindFrom(pf.Parameters[i], af.Parameters[i], map);
                    BindFrom(pf.Return, af.Return, map);
                    break;
            }
        }

        /// <summary>
        /// Resolves a generic call target: validates explicit type args, infers
        /// the substitution (explicit args + argument unification + seed), and
        /// returns the substituted copy of the declaration. Returns the original
        /// when the target isn't generic.
        /// </summary>
        private FunctionDeclaration? ResolveGenericTarget(FunctionDeclaration fn, CallExpression call, IReadOnlyDictionary<string, TypeDescriptor>? seed = null)
        {
            // Explicit type args must be validated even for non-generic targets
            // (doubleIt<int>(x) is an error), so check them before the
            // IsGenericTarget early return.
            if (call.TypeArguments.Count > 0)
            {
                if (!fn.IsGeneric)
                {
                    _diagnostics.AddError($"Function '{fn.Name}' is not generic — cannot supply type arguments", call.Line, call.Column);
                    return fn;
                }
                if (call.TypeArguments.Count != fn.TypeParameters.Count)
                {
                    _diagnostics.AddError(
                        $"Function '{fn.Name}' expects {fn.TypeParameters.Count} type argument(s), got {call.TypeArguments.Count}",
                        call.Line, call.Column);
                    return fn;
                }
                foreach (var ta in call.TypeArguments)
                {
                    if (!IsValidTypeInContext(ta))
                        _diagnostics.AddError($"Unknown type argument '{ta}'", call.Line, call.Column);
                }
            }

            if (!IsGenericTarget(fn)) return fn;

            var map = InferSubstitution(fn, call, seed);
            var genericContract = fn.ContractName != null ? FindGenericContract(fn.ContractName) : null;
            return SubstituteFunction(fn, map, genericContract);
        }

        private void ResolveCall(CallExpression call)
        {
            if (call.Callee is MemberExpression mem)
            {
                // Collect the dotted base path: IO.Println → "IO",
                // ObjektRT.Stdlib.System.IO.Println → "ObjektRT.Stdlib.System.IO".
                if (TryGetModuleAccessPath(mem, out var moduleName))
                {
                    string methodName = mem.Property;

                    if (_symbolTable.TryResolveMethod(moduleName, methodName, call.Arguments.Count, out var moduleMethod))
                    {
                        // A method reached through a type/module name must be
                        // static — an instance method needs an object created
                        // with `new`. (Stdlib modules resolve as ExternalMethod.)
                        if (moduleMethod is FunctionDeclaration udf && !udf.IsStatic)
                        {
                            _diagnostics.AddError(
                                $"Instance method '{methodName}' cannot be called on type '{moduleName}' — create an instance with 'new {moduleName}()' first.",
                                call.Line, call.Column);
                            return;
                        }
                        if (moduleMethod is FunctionDeclaration moduleFn)
                        {
                            // Generic static on a generic contract: Box.wrap(7)
                            // — the contract's T is inferred from the argument.
                            var genericContract = FindGenericContract(moduleName);
                            var seed = genericContract != null
                                ? new Dictionary<string, TypeDescriptor>()
                                : null;
                            call.Symbol = ResolveGenericTarget(moduleFn, call, seed);
                            _usedFunctions.Add(moduleFn.Name);
                            _usedTypes.Add(moduleName);
                        }
                        else
                        {
                            call.Symbol = moduleMethod;
                        }
                        _usedModulePaths.Add(moduleName);
                        return;
                    }

                    // Inherited static: Dog.species() where species lives on
                    // Animal (the dotted form of Dog::species()).
                    if (FindContract(moduleName) is { } staticOwnerContract
                        && FindMethodIncludingBase(staticOwnerContract, methodName, instanceOnly: false) is { IsStatic: true } inheritedStaticDot)
                    {
                        if (!IsAccessibleFrom(inheritedStaticDot.Access, inheritedStaticDot.ContractName ?? ""))
                        {
                            _diagnostics.AddError(
                                $"Method '{inheritedStaticDot.ContractName}.{methodName}' is {AccessName(inheritedStaticDot.Access)} — not accessible from '{_currentContractName}'",
                                call.Line, call.Column);
                        }
                        call.Symbol = inheritedStaticDot;
                        _usedFunctions.Add(inheritedStaticDot.Name);
                        _usedTypes.Add(moduleName);
                        _usedTypes.Add(staticOwnerContract.FullName);
                        return;
                    }

                    if (moduleName.Contains('.'))
                    {
                        // Dotted chain that isn't a bound module. Try to resolve
                        // the first segment as a variable of struct type with a
                        // field of GenericInstance type that has the method.
                        // e.g. bindings.listVal.Count() where "bindings" is a
                        // Value struct with field listVal: ValueList<Value>.
                        var firstSeg = moduleName;
                        int dotIdx = moduleName.IndexOf('.');
                        if (dotIdx > 0) firstSeg = moduleName.Substring(0, dotIdx);
                        if (IsVariableDefined(firstSeg)
                            && FindVariableType(firstSeg) is TypeDescriptor.Named firstVarType
                            && FindStruct(firstVarType.Name) is { } firstStruct)
                        {
                            foreach (var field in firstStruct.Fields)
                            {
                                if (field.Type is TypeDescriptor.GenericInstance fg
                                    && FindGenericContract(fg.Name) is { } fieldGenContract)
                                {
                                    var fm = FindMethodIncludingBase(fieldGenContract, methodName, instanceOnly: true);
                                    if (fm != null)
                                    {
                                        call.Symbol = ResolveGenericTarget(fm, call, TypeParamMap(fieldGenContract, fg));
                                        _usedFunctions.Add(fm.Name);
                                        _usedTypes.Add(firstVarType.Name);
                                        return;
                                    }
                                }
                            }
                        }
                        _diagnostics.AddError($"External method '{moduleName}.{methodName}' not found.", call.Line, call.Column);
                        return;
                    }

                    // Single-segment base: try the existing instance-method resolution
                    // (c.method() where c is a variable of a contract type).
                    string className = moduleName;

                    // this.method() — an instance method call on the implicit
                    // receiver. Resolves against the current contract (or its
                    // bases); inside a generic contract body the member is the
                    // ORIGINAL declaration (the VM substitutes during
                    // materialization).
                    if (className == "this" && _currentIsInstance && _currentContractName != null)
                    {
                        var thisContract = FindContract(_currentContractName);
                        if (thisContract != null)
                        {
                            var member = FindMethodIncludingBase(thisContract, methodName, instanceOnly: true);
                            if (member != null)
                            {
                                if (!IsAccessibleFrom(member.Access, member.ContractName ?? ""))
                                {
                                    _diagnostics.AddError(
                                        $"Method '{member.ContractName}.{methodName}' is {AccessName(member.Access)} — not accessible from '{_currentContractName}'",
                                        call.Line, call.Column);
                                }
                                call.Symbol = member;
                                _usedFunctions.Add(member.Name);
                                _usedTypes.Add(thisContract.FullName);
                                return;
                            }
                        }
                    }

                    // Chained member access: bindings.listVal.Count() — when
                    // the first segment is a variable of struct type, resolve
                    // the field, then check for methods on the field's type.
                    if (IsVariableDefined(className))
                    {
                        var varType = FindVariableType(className);
                        if (varType is TypeDescriptor.Named n && FindStruct(n.Name) is { } structDecl)
                        {
                            // Check if any struct field of GenericInstance type
                            // has the method — this handles chained access like
                            // bindings.listVal.Count() where "Count" is on the
                            // field's type, not the struct itself.
                            foreach (var field in structDecl.Fields)
                            {
                                if (field.Type is TypeDescriptor.GenericInstance fg
                                    && FindGenericContract(fg.Name) is { } fieldGenContract)
                                {
                                    var fm = FindMethodIncludingBase(fieldGenContract, methodName, instanceOnly: true);
                                    if (fm != null)
                                    {
                                        call.Symbol = ResolveGenericTarget(fm, call, TypeParamMap(fieldGenContract, fg));
                                        _usedFunctions.Add(fm.Name);
                                        _usedTypes.Add(n.Name);
                                        return;
                                    }
                                }
                            }
                        }
                    }

                    // User-contract instance method call: c.method() where c is a
                    // variable whose type is a contract with that method. Walks
                    // the base chain so inherited methods resolve (Dog.speak()
                    // finds Animal.speak).
                    if (IsVariableDefined(className))
                    {
                        var varType = FindVariableType(className);
                        if (varType is TypeDescriptor.Named n
                            && _contractsByName.TryGetValue(n.Name, out var contract))
                        {
                            var member = FindMethodIncludingBase(contract, methodName, instanceOnly: true);
                            if (member != null)
                            {
                                if (!IsAccessibleFrom(member.Access, member.ContractName ?? ""))
                                {
                                    _diagnostics.AddError(
                                        $"Method '{member.ContractName}.{methodName}' is {AccessName(member.Access)} — not accessible from '{_currentContractName}'",
                                        call.Line, call.Column);
                                }
                                call.Symbol = member;
                                _usedFunctions.Add(member.Name);
                                _usedTypes.Add(n.Name);
                                _usedTypes.Add(contract.FullName);
                                return;
                            }
                        }
                        // Generic contract instance: b.get() where b: Box<int>.
                        if (varType is TypeDescriptor.GenericInstance g
                            && FindGenericContract(g.Name) is { } genContract)
                        {
                            var member = FindMethodIncludingBase(genContract, methodName, instanceOnly: true);
                            if (member != null)
                            {
                                if (!IsAccessibleFrom(member.Access, member.ContractName ?? ""))
                                {
                                    _diagnostics.AddError(
                                        $"Method '{member.ContractName}.{methodName}' is {AccessName(member.Access)} — not accessible from '{_currentContractName}'",
                                        call.Line, call.Column);
                                }
                                call.Symbol = ResolveGenericTarget(member, call, TypeParamMap(genContract, g));
                                _usedFunctions.Add(member.Name);
                                _usedTypes.Add(g.Name);
                                _usedTypes.Add(genContract.FullName);
                                return;
                            }
                        }
                        // Fall through to error below.
                    }

                    // Struct field of function type: fn.lambdaFn(args) where
                    // fn is a variable of a struct type with a field named
                    // methodName that has a function type.
                    if (IsVariableDefined(className))
                    {
                        var varType = FindVariableType(className);
                        if (varType is TypeDescriptor.Named n
                            && FindStruct(n.Name) is { } structDecl)
                        {
                            var field = structDecl.Fields.FirstOrDefault(f => f.Name == methodName);
                            if (field != null && field.Type is TypeDescriptor.Function fnType)
                            {
                                // The struct field is a function — treat this as
                                // a call to that function type.  We don't set
                                // call.Symbol because the codegen resolves the
                                // field access + callvirt separately (member
                                // expression on the left is already handled).
                                _usedTypes.Add(n.Name);
                                return;
                            }
                        }
                    }

                    _diagnostics.AddError($"External method '{className}.{methodName}' not found.", call.Line, call.Column);
                }
            }
            else if (call.Callee is ScopedAccessExpression scoped)
            {
                // Module::Method(...) — external stdlib or a static function on a contract.
                if (_symbolTable.TryResolveMethod(scoped.Module, scoped.Member, call.Arguments.Count, out var scopedMethod))
                {
                    if (scopedMethod is FunctionDeclaration scopedFn)
                    {
                        // Box::wrap(7) — generic static on a generic contract;
                        // Box<int>::reset() — explicit type args on the module.
                        var genericContract = FindGenericContract(scoped.Module);
                        var seed = genericContract != null
                            ? new Dictionary<string, TypeDescriptor>()
                            : null;
                        call.Symbol = ResolveGenericTarget(scopedFn, call, seed);
                        _usedFunctions.Add(scopedFn.Name);
                        _usedTypes.Add(scoped.Module);
                    }
                    else
                    {
                        call.Symbol = scopedMethod;
                    }
                    return;
                }
                // Inherited static: Dog::Speak() where Speak lives on Animal.
                // (The undefined-module/member case is reported here too so the
                // symbol links and call.Symbol is set for the codegen.)
                if (FindContract(scoped.Module) is { } scopedContract
                    && FindMethodIncludingBase(scopedContract, scoped.Member, instanceOnly: false) is { IsStatic: true } inheritedStatic)
                {
                    if (!IsAccessibleFrom(inheritedStatic.Access, inheritedStatic.ContractName ?? ""))
                    {
                        _diagnostics.AddError(
                            $"Method '{inheritedStatic.ContractName}.{scoped.Member}' is {AccessName(inheritedStatic.Access)} — not accessible from '{_currentContractName}'",
                            call.Line, call.Column);
                    }
                    call.Symbol = ResolveGenericTarget(inheritedStatic, call);
                    _usedFunctions.Add(inheritedStatic.Name);
                    _usedTypes.Add(scoped.Module);
                    return;
                }
                // (The undefined-module/member case is already reported by
                // AnalyzeExpression's ScopedAccessExpression branch.)
            }
            else if (call.Callee is IdentifierExpression ident)
            {
                // Bare call: a defined function, a lambda, or a function-typed value.
                bool isFunctionValue = ident.Name.StartsWith("__lambda_") || IsVariableDefined(ident.Name);
                if (!_definedFunctions.Contains(ident.Name) && !isFunctionValue)
                {
                    _diagnostics.AddError($"Undefined function: '{ident.Name}'", call.Line, call.Column);
                }
                else if (!isFunctionValue && IsInvalidBareInstanceCall(ident.Name))
                {
                    // An instance method called bare from a context that has no
                    // implicit `this` (or that belongs to another contract).
                    _diagnostics.AddError(
                        $"Instance method '{ident.Name}' requires an instance — call it on a 'new' object or declare it 'static fn'",
                        call.Line, call.Column);
                }
                else if (!isFunctionValue)
                {
                    // Generic function: identity(42) — T inferred from the arg.
                    var fn = FindFunctionDecl(ident.Name);
                    if (fn != null)
                        call.Symbol = ResolveGenericTarget(fn, call);
                    _usedFunctions.Add(ident.Name);
                }
            }
            else if (call.Callee is CallExpression innerCall)
            {
                // f()(args) — calling the result of a call (a delegate/closure).
                var fnType = GetCallResultFunctionType(innerCall);
                if (fnType == null)
                {
                    _diagnostics.AddError($"Expression is not callable: it does not return a function", call.Line, call.Column);
                    return;
                }
                if (call.Arguments.Count != fnType.Parameters.Count)
                {
                    _diagnostics.AddError($"Function expects {fnType.Parameters.Count} argument(s), got {call.Arguments.Count}", call.Line, call.Column);
                    return;
                }
                call.Symbol = innerCall.Symbol;
            }
        }

        /// <summary>
        /// The function type of the delegate that a call produces — the call's
        /// result type when that result is a function type. Used to resolve
        /// <c>f()(args)</c>: the inner call must produce a delegate whose
        /// signature drives the outer invocation.
        /// </summary>
        private TypeDescriptor.Function? GetCallResultFunctionType(CallExpression call)
        {
            // f() where f's declared return type is a function type.
            if (call.Symbol is FunctionDeclaration fd && fd.ReturnType is TypeDescriptor.Function fn)
                return fn;

            // Bare function call by name: f() with f declared elsewhere.
            if (call.Callee is IdentifierExpression id
                && _functionReturnTypes.TryGetValue(id.Name, out var ret)
                && ret is TypeDescriptor.Function fnById)
            {
                return fnById;
            }

            // Calling a function-typed value: the value's type is the function
            // type of the call result (a delegate), so the delegate produced by
            // calling it has the value's function type's return.
            if (call.Callee is IdentifierExpression idVal)
            {
                var vType = FindVariableType(idVal.Name);
                if (vType is TypeDescriptor.Function f1 && f1.Return is TypeDescriptor.Function nested)
                    return nested;
                if (vType is TypeDescriptor.GenericInstance g
                    && g.Name.Equals("Delegate", StringComparison.OrdinalIgnoreCase)
                    && g.Arguments.Count == 1
                    && g.Arguments[0] is TypeDescriptor.Function gf
                    && gf.Return is TypeDescriptor.Function nestedDelegate)
                {
                    return nestedDelegate;
                }
            }

            return null;
        }

        /// <summary>
        /// Collects the dotted identifier spine on the left of a member access:
        /// IO.Println → "IO"; ObjektRT.Stdlib.System.IO.Println → "ObjektRT.Stdlib.System.IO".
        /// The outermost <see cref="MemberExpression.Property"/> is the member, not part of the path.
        /// Returns false when the base is not a pure identifier chain.
        /// </summary>
        private static bool TryGetModuleAccessPath(MemberExpression mem, out string modulePath)
        {
            modulePath = "";
            var segments = new Stack<string>();
            var current = mem.Object;
            while (current is MemberExpression inner)
            {
                segments.Push(inner.Property);
                current = inner.Object;
            }
            if (current is not IdentifierExpression root) return false;
            segments.Push(root.Name);
            modulePath = string.Join(".", segments);
            return true;
        }

        /// <summary>True when the member chain's base is a bound stdlib module (possibly dotted).</summary>
        private bool IsModuleAccessChain(MemberExpression mem)
            => TryGetModuleAccessPath(mem, out var modulePath) && _symbolTable.IsBoundModule(modulePath);

        /// <summary>True when a (possibly dotted) name is a user-declared contract, struct, or enum.</summary>
        private bool IsUserType(string name)
        {
            if (FindContract(name) != null) return true;
            if (FindStruct(name) != null) return true;
            return FindEnum(name) != null;
        }

        /// <summary>True when a member chain's base is a qualified user type reference (com.lib.Geo.Member).</summary>
        private bool IsTypeAccessChain(MemberExpression mem)
            => TryGetModuleAccessPath(mem, out var path) && IsUserType(path);
    }
}
