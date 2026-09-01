using Contract.Compiler.AST;

namespace Contract.Compiler;

/// <summary>
/// Shared logic for loading compiled module references (<c>.orbt</c>/<c>.oil</c>/<c>.oir</c>)
/// as DLL-style includes: parse the module into an IR AST, retain the raw body for
/// static linking, and synthesize Contract declarations so the analyzer can resolve
/// calls/fields/ctors. Used by both the command-line <see cref="CompilerDriver"/> and
/// the language server's in-memory <c>ProgramLoader</c>.
/// </summary>
public static class CompiledReferenceLoader
{
    /// <summary>True when a path is a compiled module reference rather than a .ct source file.</summary>
    public static bool IsCompiledReference(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".orbt" or ".oil" or ".oir";

    /// <summary>Parses a compiled module file into an IR module AST (binary or text).</summary>
    public static ObjektRT.Core.AST.ModuleNode ParseModule(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        ObjektRT.Core.Model.ORBTModule wire;
        if (ext == ".orbt")
        {
            wire = ObjektRT.Core.Serialization.OrbtFileReader.ReadFile(path);
        }
        else
        {
            wire = new ObjektRT.Core.Parsing.ObjectILParser(File.ReadAllText(path)).ParseModule();
        }
        return new ObjektRT.Core.Conversion.ModelToAstConverter().Convert(wire);
    }

    /// <summary>
    /// Records the module for static linking and synthesizes Contract declarations
    /// for its types into <paramref name="program"/>. Deduplicates modules by name.
    /// </summary>
    public static void Synthesize(ObjektRT.Core.AST.ModuleNode module, Program program)
    {
        if (program.ExternalModules.Any(m => m.Name == module.Name)) return;
        program.ExternalModules.Add(module);
        SynthesizeExternalDeclarations(module, program);
    }

    /// <summary>
    /// Turns a compiled module's types into Contract AST declarations so the
    /// analyzer can resolve calls/fields/ctors and the codegen can emit
    /// qualified references. The bodies stay in the retained module.
    /// </summary>
    private static void SynthesizeExternalDeclarations(ObjektRT.Core.AST.ModuleNode module, Program program)
    {
        // The compiled wire format does not retain explicit generic-parameter
        // metadata; the parameter names survive only as the bare, unqualified
        // type strings referenced by a class's own members (e.g. `items: T[]`,
        // `value: V`). Pre-collect the short names of every type declared in
        // this module so we can tell a real type apart from a generic parameter
        // when reconstructing generic contracts below.
        var declaredTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in module.Classes) declaredTypeNames.Add(ShortName(c.Name));
        foreach (var s in module.Structs) declaredTypeNames.Add(ShortName(s.Name));
        foreach (var i in module.Interfaces) declaredTypeNames.Add(ShortName(i.Name));

        foreach (var cls in module.Classes)
        {
            var (ns, shortName) = SplitQualifiedName(cls.Name);

            // Enum heuristic: a class with no base, ctors, or methods whose
            // fields are all static int32 — matches how enums are emitted.
            if (cls.BaseTypes.Count == 0
                && cls.Constructors.Count == 0
                && cls.Methods.Count == 0
                && cls.Fields.Count > 0
                && cls.Fields.All(f => f.IsStatic && f.FieldType.Name == "int32"))
            {
                var enumDecl = new EnumDeclaration(shortName, 1, 1) { Namespace = ns, IsExternal = true };
                foreach (var f in cls.Fields)
                    enumDecl.Members.Add(f.Name);
                program.Enums.Add(enumDecl);
                continue;
            }

            var contract = new ContractDeclaration(shortName, 1, 1) { Namespace = ns, IsExternal = true };
            contract.BaseTypeName = cls.BaseTypes.Count > 0 ? cls.BaseTypes[0] : null;

            // Detect IL-level ShadowBinding (compile-time)
            foreach (var attr in cls.Attributes)
            {
                if (attr.Name.Equals("ShadowBinding", StringComparison.OrdinalIgnoreCase) && attr.Arguments.Count > 0)
                {
                    string sv = attr.Arguments[0].Trim();
                    if (sv.Length >= 2 && sv[0] == '"' && sv[^1] == '"') sv = sv[1..^1];
                    contract.IsShadowed = true;
                    contract.ShadowTarget = sv;
                    break;
                }
                // Also support fully qualified compile-time form via encoded named args not used here
            }

            // Reconstruct generic type parameters. A non-generic class yields an
            // empty list; a generic one (List<T>, Result<V,E>, …) gets the
            // parameter names back so the analyzer can (a) treat its members'
            // bare parameter references as in-scope and (b) register the type as
            // generic with the right arity.
            foreach (var tp in ComputeGenericParameters(module, cls, declaredTypeNames))
                contract.TypeParameters.Add(tp);

            foreach (var f in cls.Fields)
            {
                contract.Fields.Add(new StructField(f.Name, TypeDescriptor.Parse(WireToLanguageType(f.FieldType.Name)), 1, 1)
                {
                    IsStatic = f.IsStatic,
                });
            }
            foreach (var ctor in cls.Constructors)
            {
                var c = new ConstructorDeclaration(1, 1);
                foreach (var p in ctor.Parameters)
                    c.Parameters.Add(new Parameter(p.Name, TypeDescriptor.Parse(WireToLanguageType(p.ParameterType.Name)), 1, 1));
                contract.Constructors.Add(c);
            }
            foreach (var m in cls.Methods)
            {
                var fd = new FunctionDeclaration(m.Name, 1, 1)
                {
                    IsStatic = m.IsStatic,
                    IsInstance = !m.IsStatic,
                    ContractName = contract.FullName,
                    ReturnType = TypeDescriptor.Parse(WireToLanguageType(m.ReturnType.Name)),
                    Access = Contract.Compiler.AST.AccessModifier.Public,
                };
                bool isInstance = !m.IsStatic;
                foreach (var p in m.Parameters)
                {
                    // Instance methods carry an implicit `this` as the first
                    // parameter in the wire format (e.g. `this: object`).
                    // The Contract `FunctionDeclaration` models `this` via
                    // `IsInstance`, not as an explicit parameter, so skip it
                    // to keep call-site arity correct (`t.Name()` is 0 args,
                    // not 1).
                    if (isInstance && p.Name == "this")
                        continue;
                    fd.Parameters.Add(new Parameter(p.Name, TypeDescriptor.Parse(WireToLanguageType(p.ParameterType.Name)), 1, 1));
                }
                contract.Members.Add(fd);
            }
            program.Contracts.Add(contract);
        }

        foreach (var str in module.Structs)
        {
            var (ns, shortName) = SplitQualifiedName(str.Name);
            var structDecl = new StructDeclaration(shortName, 1, 1) { Namespace = ns, IsExternal = true };
            foreach (var f in str.Fields)
                structDecl.Fields.Add(new StructField(f.Name, TypeDescriptor.Parse(WireToLanguageType(f.FieldType.Name)), 1, 1));
            program.Structs.Add(structDecl);
        }
    }

    private static (string? Namespace, string Name) SplitQualifiedName(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot > 0
            ? (fullName[..dot], fullName[(dot + 1)..])
            : (null, fullName);
    }

    /// <summary>The unqualified (short) name portion of a (possibly dotted) type name.</summary>
    private static string ShortName(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot > 0 ? fullName[(dot + 1)..] : fullName;
    }

    /// <summary>
    /// Reconstructs the generic type parameters of a compiled class. The wire
    /// format stores parameters only as bare type strings inside members, so we
    /// collect every simple, unqualified type name referenced by the class's
    /// own members and its nested classes, then drop built-ins and types that
    /// are actually declared in this module — whatever remains are parameters
    /// (T, V, E, …).
    /// </summary>
    private static IEnumerable<string> ComputeGenericParameters(
        ObjektRT.Core.AST.ModuleNode module,
        ObjektRT.Core.AST.ClassNode cls,
        HashSet<string> declaredTypeNames)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectOwnParameters(cls, names, declaredTypeNames);

        // Nested classes (e.g. Result.Ok / Result.Err) carry the outer generic's
        // parameters; fold them into the enclosing contract so `Result<V,E>`
        // resolves with the correct arity.
        string prefix = cls.Name + ".";
        foreach (var nested in module.Classes)
        {
            if (nested.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                CollectOwnParameters(nested, names, declaredTypeNames);
        }

        return names;
    }

    private static void CollectOwnParameters(
        ObjektRT.Core.AST.ClassNode cls,
        HashSet<string> names,
        HashSet<string> declaredTypeNames)
    {
        void Consider(string? wireType)
        {
            if (string.IsNullOrEmpty(wireType)) return;
            CollectLeafTypeNames(TypeDescriptor.Parse(WireToLanguageType(wireType)), names, declaredTypeNames);
        }

        foreach (var f in cls.Fields) Consider(f.FieldType.Name);
        foreach (var ctor in cls.Constructors)
            foreach (var p in ctor.Parameters) Consider(p.ParameterType.Name);
        foreach (var m in cls.Methods)
        {
            Consider(m.ReturnType.Name);
            foreach (var p in m.Parameters) Consider(p.ParameterType.Name);
        }
    }

    /// <summary>Walks a type descriptor and records its simple, unqualified leaf type names.</summary>
    private static void CollectLeafTypeNames(
        TypeDescriptor type,
        HashSet<string> names,
        HashSet<string> declaredTypeNames)
    {
        switch (type)
        {
            case TypeDescriptor.Named n:
                string name = n.Name;
                if (!string.IsNullOrEmpty(name)
                    && name.IndexOf('.') < 0
                    && !BuiltinTypeNames.Contains(name)
                    && !declaredTypeNames.Contains(name))
                {
                    names.Add(name);
                }
                break;
            case TypeDescriptor.ArrayOf a:
                CollectLeafTypeNames(a.Element, names, declaredTypeNames);
                break;
            case TypeDescriptor.GenericInstance g:
                foreach (var arg in g.Arguments) CollectLeafTypeNames(arg, names, declaredTypeNames);
                break;
            case TypeDescriptor.Function f:
                foreach (var p in f.Parameters) CollectLeafTypeNames(p, names, declaredTypeNames);
                CollectLeafTypeNames(f.Return, names, declaredTypeNames);
                break;
            case TypeDescriptor.Tuple t:
                foreach (var e in t.Elements) CollectLeafTypeNames(e, names, declaredTypeNames);
                break;
        }
    }

    /// <summary>Built-in primitive and generic-unbound type names that must never be treated as parameters.</summary>
    private static readonly HashSet<string> BuiltinTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "string", "bool", "double", "float", "object", "int64", "long", "null", "void",
        "byte", "sbyte", "short", "ushort", "uint", "int32", "float32", "float64",
        "uint8", "int8", "int16", "uint16", "uint32", "intptr",
        // Generic unbound names are real types, not parameters.
        "List", "Dict", "Delegate", "Attribute",
        // Root reflection type (C#-style System.Type).
        "Type",
    };

    /// <summary>Maps a wire type name ("int32", "float64[]") to the language type name ("int", "double[]").</summary>
    public static string WireToLanguageType(string wire)
    {
        string element = wire;
        string suffix = "";
        while (element.EndsWith("[]", StringComparison.Ordinal))
        {
            suffix += "[]";
            element = element[..^2];
        }
        string mapped = element switch
        {
            "int32" => "int",
            "float64" => "double",
            "float32" => "float",
            "int64" => "int64",
            "uint8" => "byte",
            "int8" => "sbyte",
            "int16" => "short",
            "uint16" => "ushort",
            "uint32" => "uint",
            "string" => "string",
            "bool" => "bool",
            "object" => "object",
            "void" => "void",
            _ => element,   // user types: keep the qualified name
        };
        return mapped + suffix;
    }
}
