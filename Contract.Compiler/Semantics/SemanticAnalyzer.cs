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
        private readonly HashSet<string> _definedFunctions = new();
        private readonly Dictionary<string, TypeDescriptor> _functionReturnTypes = new();
        private readonly List<ContractDeclaration> _contractsWithFields = new();
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

            // Register contracts as valid types (short + namespace-qualified)
            foreach (var contract in program.Contracts)
            {
                _typeRegistry.RegisterCustomType(contract.Name);
                if (contract.FullName != contract.Name)
                    _typeRegistry.RegisterCustomType(contract.FullName);
                if (contract.Fields.Count > 0)
                    _contractsWithFields.Add(contract);
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
                ValidateFields(contract.Fields, "contract");
                foreach (var member in contract.Members)
                {
                    if (member is StructDeclaration nestedStruct)
                        ValidateFields(nestedStruct.Fields, "struct");
                }
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

                if (!_typeRegistry.IsValid(field.Type))
                {
                    _diagnostics.AddError($"Unknown type '{field.Type}' for field '{field.Name}'", field.Line, field.Column);
                }
            }
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

                while (current.BaseTypeName != null)
                {
                    if (!seen.Add(current.Name))
                    {
                        _diagnostics.AddError($"Inheritance cycle involving contract '{current.Name}'", current.Line, current.Column);
                        break;
                    }

                    chain.Add(current);
                    var baseName = current.BaseTypeName;
                    _usedTypes.Add(baseName);   // base type is a usage of that type

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

        private void ValidateAttributes(List<AttributeUsage> attributes, string targetKind, Dictionary<string, ContractDeclaration> contractsByName)
        {
            foreach (var attr in attributes)
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
            _scopes.Push(new Dictionary<string, VariableDeclaration>());
            BeginFunctionFrame();
            _currentReturnType = null;
            _currentSourceFile = ctor.SourceFile;

            foreach (var param in ctor.Parameters)
            {
                if (param.Type is TypeDescriptor.Named pn && !pn.IsEmpty)
                    _usedTypes.Add(ResolveTypeName(pn.Name));
                if (!param.Type.IsEmpty && !_typeRegistry.IsValid(param.Type))
                {
                    _diagnostics.AddError($"Unknown type '{param.Type}' for parameter '{param.Name}'", param.Line, param.Column);
                }
                DeclareVariable(param.Name, param.Type, param.Line, param.Column, trackUsage: false);
            }

            if (ctor.Body != null)
            {
                AnalyzeStatement(ctor.Body);
            }

            _scopes.Pop();
            EndFunctionFrame();
        }

        private void AnalyzeFunction(FunctionDeclaration func)
        {
            _currentIsInstance = func.IsInstance;
            _currentContractName = func.ContractName;
            _currentSourceFile = func.SourceFile;
            _scopes.Clear();
            _scopes.Push(new Dictionary<string, VariableDeclaration>());
            BeginFunctionFrame();

            foreach (var param in func.Parameters)
            {
                if (param.Type is TypeDescriptor.Named pn && !pn.IsEmpty)
                    _usedTypes.Add(ResolveTypeName(pn.Name));
                if (!_typeRegistry.IsValid(param.Type))
                {
                    _diagnostics.AddError($"Unknown type '{param.Type}' for parameter '{param.Name}'", param.Line, param.Column);
                }
                DeclareVariable(param.Name, param.Type, param.Line, param.Column, trackUsage: false);
            }

            if (func.ReturnType != null)
            {
                if (func.ReturnType is TypeDescriptor.Named rn && !rn.IsEmpty)
                    _usedTypes.Add(ResolveTypeName(rn.Name));
                if (!_typeRegistry.IsValid(func.ReturnType))
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
            _scopes.Pop();
            EndFunctionFrame();
        }

        private void DeclareVariable(string name, TypeDescriptor type, int line, int column, bool trackUsage = true, bool warnOnShadow = true)
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

            if (!_typeRegistry.IsValid(type))
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
                    break;
                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;
                case VariableDeclaration varDecl:
                    // Analyze the initializer first so calls get symbol-linked and
                    // their return types are available for inference.
                    if (varDecl.Initializer != null)
                        AnalyzeExpression(varDecl.Initializer);

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

                    DeclareVariable(varDecl.Name, varDecl.Type, varDecl.Line, varDecl.Column);
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
                case ForStatement forStmt:
                    // The loop variable is scoped to the loop itself (like C),
                    // so two sequential 'for (var i = ...)' loops don't collide.
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    if (forStmt.Condition != null)
                        AnalyzeConditionWarnings("for", forStmt.Condition, forStmt.Line, forStmt.Column);
                    if (forStmt.Initializer != null)
                        AnalyzeStatement(forStmt.Initializer);
                    if (forStmt.Condition != null)
                        AnalyzeExpression(forStmt.Condition);
                    AnalyzeStatement(forStmt.Body);
                    if (forStmt.Update != null)
                        AnalyzeExpression(forStmt.Update);
                    _scopes.Pop();
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

        private void AnalyzeExpression(Expression expression)
        {
            switch (expression)
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
                            _writtenFields.Add((_currentContractName ?? "", assignTarget.Name));
                        bin.ResolvedType = InferType(bin);
                        break;
                    }
                    if (bin.Operator is "=" or "+=" or "-=" or "*=" or "/=" or "%="
                        && bin.Left is MemberExpression memWrite)
                    {
                        // obj.field = v / obj.field += v — record the write.
                        AnalyzeExpression(bin.Right);
                        AnalyzeExpression(memWrite.Object);
                        RecordFieldAccess(memWrite, isWrite: true);
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
                        bool isStaticField = FindContractStaticField(scoped.Module, scoped.Member) != null;
                        if (isStaticField)
                        {
                            _readFields.Add((scoped.Module, scoped.Member));
                        }
                        else if (!_symbolTable.TryGetMethod(scoped.Module, scoped.Member, out var cm)
                            || (cm is FunctionDeclaration cf && !cf.IsStatic))
                        {
                            _diagnostics.AddError($"Member '{scoped.Member}' not found in contract '{scoped.Module}'", scoped.Line, scoped.Column);
                        }
                        else if (cm is FunctionDeclaration scopedFn)
                        {
                            _usedFunctions.Add(scopedFn.Name);
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
                                    _readFields.Add((typePath, mem.Property));
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
                        RecordFieldAccess(mem, isWrite: false);
                    }
                    break;
                case IndexExpression indexExpr:
                    AnalyzeExpression(indexExpr.Target);
                    AnalyzeExpression(indexExpr.Index);
                    break;
                case PipeExpression pipe:
                    // x |> f / x |> fun -> ... — analyze both sides so variables
                    // and functions used through the pipe count as used.
                    AnalyzeExpression(pipe.Left);
                    if (pipe.Right is IdentifierExpression pipeTarget && !IsVariableDefined(pipeTarget.Name))
                    {
                        // Piping into a named static/top-level function: a call.
                        _usedFunctions.Add(pipeTarget.Name);
                        AnalyzeExpression(pipeTarget);
                    }
                    else
                    {
                        AnalyzeExpression(pipe.Right);
                    }
                    break;
                case IdentifierExpression id:
                    if (id.Name == "this") break; // instance context
                    // A variable read (bare field access is also a read of the
                    // declaring contract's field, recorded via IsContractField).
                    if (IsVariableDefined(id.Name))
                        _fnReadNames.Add(id.Name);
                    else if (IsContractField(id.Name))
                        _readFields.Add((_currentContractName ?? "", id.Name));

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
                case UnaryExpression unary:
                    AnalyzeExpression(unary.Operand);
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

                    if (newExpr.Size != null)
                    {
                        AnalyzeExpression(newExpr.Size);
                        if (!_typeRegistry.IsValidType(newExpr.TypeName))
                        {
                            _diagnostics.AddError($"Unknown type '{newExpr.TypeName}'", newExpr.Line, newExpr.Column);
                        }
                    }
                    else if (!_typeRegistry.IsValidType(newExpr.TypeName))
                    {
                        _diagnostics.AddError($"Unknown type '{newExpr.TypeName}'", newExpr.Line, newExpr.Column);
                    }

                    // Native-bound contracts construct through the host module's
                    // Create method: new Window() → binding.Create().
                    if (_contractsByName.TryGetValue(newExpr.TypeName, out var nbContract)
                        && nbContract.NativeBindingName != null
                        && !_symbolTable.TryGetMethod(nbContract.NativeBindingName, "Create", out _))
                    {
                        _diagnostics.AddError($"Native binding '{nbContract.NativeBindingName}' has no method named 'Create' (required for 'new {newExpr.TypeName}')", newExpr.Line, newExpr.Column);
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
                        if (!string.IsNullOrEmpty(pt) && !_typeRegistry.IsValidType(pt))
                        {
                            _diagnostics.AddError($"Unknown type '{pt}' for lambda parameter '{lambda.Parameters[i]}'", lambda.Line, lambda.Column);
                        }
                    }
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    foreach (var p in lambda.Parameters)
                        DeclareVariable(p, new TypeDescriptor.Named("int"), lambda.Line, lambda.Column, trackUsage: false, warnOnShadow: false);
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

        private bool IsContractField(string name)
        {
            // True when a contract has a field with this name (bare field access
            // in an instance method).
            foreach (var contract in _contractsWithFields)
            {
                if (contract.Fields.Any(f => f.Name == name)) return true;
            }
            return false;
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
                candidate => _typeRegistry.IsValidType(candidate),
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
        /// instance method, or the method belongs to a different contract.
        /// </summary>
        private bool IsInvalidBareInstanceCall(string name)
        {
            var target = FindFunctionDecl(name);
            if (target is not { IsInstance: true }) return false;
            // Inside an instance method of the same contract a bare call
            // implicitly passes `this` (the codegen pushes it as the receiver).
            return !(_currentIsInstance && target.ContractName == _currentContractName);
        }

        /// <summary>Type of a static field on a contract, or null when it isn't one.</summary>
        private TypeDescriptor? FindContractStaticField(string contractName, string fieldName)
        {
            var contract = FindContract(contractName);
            return contract?.Fields.FirstOrDefault(f => f.Name == fieldName && f.IsStatic)?.Type;
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
        /// field). Returns null when it isn't a known field.
        /// </summary>
        private TypeDescriptor? FindFieldType(string ownerName, string fieldName)
        {
            if (_program == null) return null;

            var contract = FindContract(ownerName);
            if (contract != null)
            {
                var field = contract.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (field != null) return field.Type;
            }

            if (FindVariableType(ownerName) is TypeDescriptor.Named n)
            {
                var owner = FindContract(n.Name);
                var field2 = owner?.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (field2 != null) return field2.Type;
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
                case IdentifierExpression id:
                {
                    var found = FindVariableType(id.Name);
                    if (found != null) return found;
                    // Bare static field access (shared state on a contract).
                    return FindStaticFieldTypeAnywhere(id.Name);
                }
                case UnaryExpression unary:
                    return InferType(unary.Operand);
                case NewExpression newExpr:
                    // Resolve through namespaces/imports (defensive — normally
                    // already rewritten by AnalyzeExpression).
                    return newExpr.Size != null
                        ? new TypeDescriptor.ArrayOf(new TypeDescriptor.Named(ResolveTypeName(newExpr.TypeName)))
                        : new TypeDescriptor.Named(ResolveTypeName(newExpr.TypeName));
                case ArrayLiteralExpression arrLit:
                    if (arrLit.Elements.Count == 0) return null;
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
                    // Field read: Config.count (static) or p.count (instance field
                    // of a contract-typed variable). Falls back to int otherwise.
                    if (mem.Object is IdentifierExpression memObj)
                    {
                        // Enum member: Color.Red → the enum type.
                        if (IsEnumType(memObj.Name)) return new TypeDescriptor.Named(memObj.Name);
                        var fieldType = FindFieldType(memObj.Name, mem.Property);
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

        private void ResolveCall(CallExpression call)
        {
            if (call.Callee is MemberExpression mem)
            {
                // Collect the dotted base path: IO.Println → "IO",
                // ObjektRT.Stdlib.System.IO.Println → "ObjektRT.Stdlib.System.IO".
                if (TryGetModuleAccessPath(mem, out var moduleName))
                {
                    string methodName = mem.Property;

                    if (_symbolTable.TryGetMethod(moduleName, methodName, out var moduleMethod))
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
                        call.Symbol = moduleMethod;
                        _usedModulePaths.Add(moduleName);
                        if (moduleMethod is FunctionDeclaration moduleFn)
                        {
                            _usedFunctions.Add(moduleFn.Name);
                            _usedTypes.Add(moduleName);
                        }
                        return;
                    }

                    if (moduleName.Contains('.'))
                    {
                        // A dotted chain that isn't a bound module — nothing else to try.
                        _diagnostics.AddError($"External method '{moduleName}.{methodName}' not found.", call.Line, call.Column);
                        return;
                    }

                    // Single-segment base: try the existing instance-method resolution
                    // (c.method() where c is a variable of a contract type).
                    string className = moduleName;

                    // User-contract instance method call: c.method() where c is a
                    // variable whose type is a contract with that method.
                    if (IsVariableDefined(className))
                    {
                        var varType = FindVariableType(className);
                        if (varType is TypeDescriptor.Named n
                            && _contractsByName.TryGetValue(n.Name, out var contract))
                        {
                            var member = contract.Members
                                .OfType<FunctionDeclaration>()
                                .FirstOrDefault(f => f.Name == methodName && f.IsInstance);
                            if (member != null)
                            {
                                call.Symbol = member;
                                _usedFunctions.Add(member.Name);
                                _usedTypes.Add(n.Name);
                                _usedTypes.Add(contract.FullName);
                                return;
                            }
                        }
                        // Fall through to error below.
                    }

                    _diagnostics.AddError($"External method '{className}.{methodName}' not found.", call.Line, call.Column);
                }
            }
            else if (call.Callee is ScopedAccessExpression scoped)
            {
                // Module::Method(...) — external stdlib or a static function on a contract.
                if (_symbolTable.TryGetMethod(scoped.Module, scoped.Member, out var scopedMethod))
                {
                    call.Symbol = scopedMethod;
                    if (scopedMethod is FunctionDeclaration scopedFn)
                    {
                        _usedFunctions.Add(scopedFn.Name);
                        _usedTypes.Add(scoped.Module);
                    }
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
