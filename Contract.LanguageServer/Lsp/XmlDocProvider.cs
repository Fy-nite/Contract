using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Contract.Compiler.StandardLibrary;

namespace Contract.LanguageServer.Lsp;

/// <summary>Documentation for one XML-doc member (a stdlib method or module).</summary>
public class MemberDoc
{
    public string? Summary { get; set; }
    public Dictionary<string, string> Params { get; } = new();
    public string? Returns { get; set; }
}

/// <summary>
/// Loads the XML documentation file emitted for Contract.Compiler (the stdlib)
/// and resolves member docs by .NET documentation ID — the same pattern the .NET
/// IDE uses: docs live next to the assembly as a .xml file, the editor looks them
/// up. This is the "extern for the IDE" the language server reads.
/// </summary>
public class XmlDocProvider
{
    private readonly Dictionary<string, MemberDoc> _docs = new(StringComparer.Ordinal);

    /// <summary>True when the XML doc file was found and parsed.</summary>
    public bool IsLoaded { get; }

    public XmlDocProvider()
    {
        IsLoaded = TryLoad();
    }

    private bool TryLoad()
    {
        bool any = false;
        foreach (string? xmlPath in FindXmlFiles())
        {
            if (xmlPath == null || !File.Exists(xmlPath)) continue;
            try
            {
                var doc = XDocument.Load(xmlPath);
                foreach (var member in doc.Root?.Descendants("member") ?? Enumerable.Empty<XElement>())
                {
                    string? id = member.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(id)) continue;

                    var md = new MemberDoc
                    {
                        Summary = member.Element("summary")?.Value.Trim() ?? null,
                        Returns = member.Element("returns")?.Value.Trim() ?? null,
                    };
                    foreach (var p in member.Elements("param"))
                    {
                        string? name = p.Attribute("name")?.Value;
                        if (name != null) md.Params[name] = p.Value.Trim();
                    }
                    _docs[id] = md;
                }
                any = true;
            }
            catch
            {
                // Skip a corrupt/unreadable doc file; keep the others.
            }
        }
        return any;
    }

    private static IEnumerable<string?> FindXmlFiles()
    {
        // Contract.Compiler.xml (hand-rolled modules) and ObjektRT.Stdlib.xml
        // (the generic stdlib). Both are copied to the output directory of any
        // project that references them.
        var candidates = new List<string?>
        {
            Path.ChangeExtension(typeof(SymbolTable).Assembly.Location, ".xml"),
            Path.Combine(AppContext.BaseDirectory, "Contract.Compiler.xml"),
            Path.Combine(AppContext.BaseDirectory, "ObjektRT.Stdlib.xml"),
        };
        return candidates;
    }

    /// <summary>Looks up the docs for a stdlib method by its reflection metadata.</summary>
    public MemberDoc? GetMethodDoc(MethodInfo m)
        => _docs.TryGetValue(MethodDocId(m), out var doc) ? doc : null;

    /// <summary>Looks up the class-level docs for a stdlib module type (e.g. IO).</summary>
    public MemberDoc? GetTypeDoc(Type t)
        => _docs.TryGetValue("T:" + (t.FullName ?? t.Name), out var doc) ? doc : null;

    /// <summary>Builds the .NET documentation ID for a method: M:Ns.Type.Name(Params).</summary>
    public static string MethodDocId(MethodInfo m)
    {
        var sb = new StringBuilder();
        sb.Append("M:").Append(m.DeclaringType!.FullName).Append('.').Append(m.Name);
        var ps = m.GetParameters();
        if (ps.Length > 0)
            sb.Append('(').Append(string.Join(",", ps.Select(p => TypeDocId(p.ParameterType)))).Append(')');
        return sb.ToString();
    }

    private static string TypeDocId(Type t)
    {
        if (t.IsArray) return TypeDocId(t.GetElementType()!) + "[]";
        if (t.IsByRef) return TypeDocId(t.GetElementType()!);
        return t.FullName ?? t.Name;
    }
}
