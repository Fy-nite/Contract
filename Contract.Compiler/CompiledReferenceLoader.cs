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
                foreach (var p in m.Parameters)
                    fd.Parameters.Add(new Parameter(p.Name, TypeDescriptor.Parse(WireToLanguageType(p.ParameterType.Name)), 1, 1));
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
