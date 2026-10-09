using System.Reflection;
using System.Text;
using Contract.Compiler.AST;

namespace Contract.Compiler.Documentation;

/// <summary>
/// Emits Contract facade source (<c>.ct</c>) for a .NET assembly's public
/// types: one <c>&lt;ClrImport(Type: "...", Path: "Asm.dll")&gt;</c> contract
/// per type, with empty-bodied members carrying the real signatures. The
/// synthesis reuses <see cref="ClrReferenceLoader"/> (the assembly-link
/// pre-pass), so the emitted surface matches exactly what assembly-link
/// exposes — including the "bindings own their names" reservations supplied
/// by the caller. The generated files resolve in the language server (the
/// analyzer resolves <c>ClrImport</c> + <c>Path</c>).
///
/// One file is emitted per namespace: Contract's import resolver maps a
/// namespace to a file by its *first* <c>namespace</c> declaration, so a
/// multi-namespace file only ever registers under one name. Each file
/// imports its siblings so cross-namespace type references resolve within a
/// single compile; keep the generated files together in one directory.
/// </summary>
public static class BindingFacadeGenerator
{
    /// <summary>One generated facade file: its target file name, the
    /// namespace its contracts declare (null = global namespace), and the
    /// full source text.</summary>
    public sealed class FacadeFile
    {
        public required string FileName { get; init; }
        public string? Namespace { get; init; }
        public required string Source { get; init; }
    }

    /// <summary>Lexer keywords — a CLR member/parameter with one of these names
    /// is renamed with a trailing underscore so the emitted source parses.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "Contract", "if", "else", "while", "switch", "case", "return", "var", "let",
        "const", "fun", "fn", "static", "public", "private", "protected", "internal",
        "null", "import", "constructor", "struct", "export", "Types", "type", "new",
        "for", "break", "continue", "true", "false", "enum", "namespace", "try",
        "catch", "finally", "throw", "match", "in", "requires", "ensures", "invariant",
        "extend", "is", "as", "host", "this",
    };

    private static string SafeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "arg";
        if (Keywords.Contains(name)) return name + "_";
        if (!char.IsLetter(name[0]) && name[0] != '_') return "_" + name;
        return name;
    }

    /// <summary>
    /// Generates one facade file per namespace for the given target
    /// assemblies, linked in a single pass so cross-assembly type references
    /// stay precise (a <c>V12.Basic.Building</c> facade can name
    /// <c>V12.Core.World</c> in its signatures). Types whose names appear in
    /// <paramref name="reservedNames"/> (registered <c>[ClassBinding]</c>
    /// module names) are skipped — the bound facade owns that name.
    /// <paramref name="pathValue"/> overrides the <c>Path:</c> value emitted in
    /// each <c>ClrImport</c> attribute: an exact value applies to every
    /// contract (single-assembly case); a value ending in a directory
    /// separator is a directory each assembly's file name is appended to
    /// (e.g. <c>../bindings/</c>). Default: each assembly's file name, valid
    /// when the generated files sit next to the assemblies.
    /// </summary>
    public static IReadOnlyList<FacadeFile> EmitFiles(IReadOnlyList<Assembly> assemblies, IEnumerable<string>? reservedNames = null, string? pathValue = null)
    {
        if (assemblies.Count == 0) return Array.Empty<FacadeFile>();

        // Which assembly owns each public type, so each emitted contract gets
        // its own assembly's file name in Path:.
        var typeToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asm in assemblies)
        {
            string file = FileFor(asm);
            foreach (var t in Contract.Compiler.StandardLibrary.TypeLoader.GetLoadableTypes(asm))
            {
                if (t.FullName != null) typeToFile.TryAdd(t.FullName, file);
            }
        }

        bool pathIsDirectory = pathValue != null
            && (pathValue.EndsWith("/") || pathValue.EndsWith("\\"));
        string PathFor(string defaultFile)
        {
            if (string.IsNullOrEmpty(pathValue)) return defaultFile;
            return pathIsDirectory ? pathValue + defaultFile : pathValue;
        }

        var stub = new Program(1, 1);
        ClrReferenceLoader.LinkAssemblies(stub, assemblies, reservedNames);

        string? primaryName = assemblies[0].GetName().Name;

        var groups = stub.Contracts
            .Where(c => c.IsExternal)
            .GroupBy(c => c.Namespace ?? "")
            .OrderBy(g => g.Key.Length)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        // File name per namespace first, so every file can import its siblings.
        var targets = groups.Select(g => new
        {
            Group = g,
            Namespace = g.Key.Length == 0 ? null : g.Key,
            FileName = g.Key.Length == 0 ? $"{primaryName}.Global.ct" : $"{g.Key}.ct",
        }).ToList();
        var allFileNames = targets.Select(t => t.FileName).ToList();

        var files = new List<FacadeFile>();
        foreach (var t in targets)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// <auto-generated> Contract binding facades for {string.Join(" + ", assemblies.Select(a => a.GetName().Name))}"
                + (t.Namespace != null ? $" — namespace {t.Namespace}" : " — global namespace"));
            sb.AppendLine("// Generated by `ccl bindgen`. Keep the generated files together in one");
            sb.AppendLine("// directory: each file imports its siblings so cross-namespace type");
            sb.AppendLine("// references resolve within a single compile.");

            // The namespace statement must precede every other statement —
            // ImportResolver.DeclaredNamespace reads the first declaration and
            // stops at the first non-namespace line.
            if (t.Namespace != null)
                sb.AppendLine($"namespace {t.Namespace};");
            sb.AppendLine();

            foreach (var other in allFileNames.Where(n => n != t.FileName).OrderBy(n => n, StringComparer.Ordinal))
                sb.AppendLine($"import \"{other}\";");
            sb.AppendLine();

            foreach (var contract in t.Group.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                string clrName = contract.ClrImportType ?? contract.FullName;
                string asmFile = typeToFile.TryGetValue(clrName, out var ownerFile) ? ownerFile : PathFor(primaryName + ".dll");
                string path = PathFor(asmFile);
                sb.AppendLine($"<ClrImport(Type: \"{clrName}\", Path: \"{path}\")>");
                sb.AppendLine($"Contract {contract.Name} {{");

                foreach (var field in contract.Fields)
                {
                    string staticMod = field.IsStatic ? "static " : "";
                    sb.AppendLine($"    {staticMod}{SafeName(field.Name)}: {field.Type};");
                }

                foreach (var member in contract.Members)
                {
                    if (member is not FunctionDeclaration fn) continue;
                    string staticMod = fn.IsStatic ? "static " : "";
                    var parameters = string.Join(", ", fn.Parameters.Select(p => $"{SafeName(p.Name)}: {p.Type}"));
                    bool isVoid = fn.ReturnType is TypeDescriptor.Named { Name: "void" };
                    string ret = fn.ReturnType is { } rt && !rt.IsEmpty && !isVoid ? $" -> {rt}" : "";
                    sb.AppendLine($"    {staticMod}fn {SafeName(fn.Name)}({parameters}){ret} {{ }}");
                }

                sb.AppendLine("}");
                sb.AppendLine();
            }

            files.Add(new FacadeFile { FileName = t.FileName, Namespace = t.Namespace, Source = sb.ToString() });
        }

        return files;
    }

    private static string FileFor(Assembly asm)
    {
        string loc = asm.Location;
        return string.IsNullOrEmpty(loc) ? (asm.GetName().Name ?? "assembly") + ".dll" : Path.GetFileName(loc);
    }
}
