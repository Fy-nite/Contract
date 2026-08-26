using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;

namespace Contract.Compiler.Documentation;

/// <summary>
/// Generates HTML documentation from extracted doc blocks.
/// Produces a single-file static site with responsive layout, navigation and search.
/// </summary>
public class DocGenerator
{
    /// <summary>Options for doc generation.</summary>
    public class Options
    {
        /// <summary>Project title displayed in the header.</summary>
        public string Title { get; set; } = "Contract API Documentation";

        /// <summary>Output directory for generated files.</summary>
        public string OutputDir { get; set; } = "docs";

        /// <summary>Output format: "html" or "md".</summary>
        public string Format { get; set; } = "html";

        /// <summary>Project version string.</summary>
        public string? Version { get; set; }

        /// <summary>Project description.</summary>
        public string? Description { get; set; }
    }

    private readonly Options _options;

    public DocGenerator(Options options)
    {
        _options = options;
    }

    /// <summary>
    /// Generates documentation from a list of doc blocks grouped by source file.
    /// </summary>
    public void Generate(Dictionary<string, List<DocCommentExtractor.DocBlock>> fileDocs)
    {
        Directory.CreateDirectory(_options.OutputDir);

        // Flatten all doc blocks and group by namespace
        var allDocs = fileDocs.SelectMany(kvp => kvp.Value).ToList();
        var byNamespace = allDocs
            .Where(d => !string.IsNullOrEmpty(d.Namespace))
            .GroupBy(d => d.Namespace!)
            .OrderBy(g => g.Key)
            .ToList();

        // Also collect root-level declarations (no namespace)
        var rootDocs = allDocs.Where(d => string.IsNullOrEmpty(d.Namespace)).ToList();

        if (_options.Format == "md")
            GenerateMarkdown(allDocs, byNamespace, rootDocs);
        else
            GenerateHtml(allDocs, byNamespace, rootDocs);
    }

    private void GenerateHtml(
        List<DocCommentExtractor.DocBlock> allDocs,
        List<IGrouping<string, DocCommentExtractor.DocBlock>> byNamespace,
        List<DocCommentExtractor.DocBlock> rootDocs)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{WebUtility.HtmlEncode(_options.Title)}</title>");
        sb.AppendLine("  <style>");
        sb.Append(EmbeddedCss());
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Mobile nav toggle
        sb.AppendLine("<button class=\"nav-toggle\" id=\"nav-toggle\" aria-label=\"Toggle navigation\">");
        sb.AppendLine("  <span></span><span></span><span></span>");
        sb.AppendLine("</button>");

        // Overlay for mobile sidebar
        sb.AppendLine("<div class=\"nav-overlay\" id=\"nav-overlay\"></div>");

        // Sidebar
        sb.AppendLine("<nav class=\"sidebar\" id=\"sidebar\">");
        sb.AppendLine("  <div class=\"sidebar-header\">");
        sb.AppendLine($"    <h1 class=\"sidebar-title\">{WebUtility.HtmlEncode(_options.Title)}</h1>");
        if (_options.Version != null)
            sb.AppendLine($"    <span class=\"version\">v{WebUtility.HtmlEncode(_options.Version)}</span>");
        sb.AppendLine("  </div>");
        sb.AppendLine("  <input type=\"text\" id=\"search\" placeholder=\"Search declarations...\" autocomplete=\"off\">");
        sb.AppendLine("  <ul id=\"nav-list\">");

        // Root declarations
        if (rootDocs.Count > 0)
        {
            sb.AppendLine("    <li class=\"nav-section\">Root</li>");
            foreach (var doc in rootDocs.OrderBy(d => d.Name))
                sb.AppendLine($"      <li><a href=\"#-{WebUtility.HtmlEncode(doc.Name)}\" data-kind=\"{doc.Kind}\"><span class=\"kind-dot\"></span>{WebUtility.HtmlEncode(doc.Name)}</a></li>");
        }

        // Namespaces
        foreach (var ns in byNamespace)
        {
            sb.AppendLine($"    <li class=\"nav-section\">{WebUtility.HtmlEncode(ns.Key)}</li>");
            foreach (var doc in ns.OrderBy(d => d.Name))
                sb.AppendLine($"      <li><a href=\"#-{WebUtility.HtmlEncode(doc.Name)}\" data-kind=\"{doc.Kind}\"><span class=\"kind-dot\"></span>{WebUtility.HtmlEncode(doc.Name)}</a></li>");
        }

        sb.AppendLine("  </ul>");
        sb.AppendLine("</nav>");

        // Main content
        sb.AppendLine("<main class=\"content\" id=\"content\">");

        // Page header
        sb.AppendLine("  <header class=\"page-header\">");
        sb.AppendLine($"    <h1>{WebUtility.HtmlEncode(_options.Title)}</h1>");
        if (_options.Description != null)
            sb.AppendLine($"    <p class=\"description\">{WebUtility.HtmlEncode(_options.Description)}</p>");
        sb.AppendLine("  </header>");

        // Render root declarations
        foreach (var doc in rootDocs.OrderBy(d => d.Name))
            RenderDocBlock(sb, doc);

        // Render by namespace
        foreach (var ns in byNamespace)
        {
            sb.AppendLine($"  <section class=\"ns-section\">");
            sb.AppendLine($"    <h2 class=\"namespace\">{WebUtility.HtmlEncode(ns.Key)}</h2>");
            foreach (var doc in ns.OrderBy(d => d.Name))
                RenderDocBlock(sb, doc);
            sb.AppendLine("  </section>");
        }

        sb.AppendLine("</main>");

        // Search + nav script
        sb.AppendLine("<script>");
        sb.AppendLine(EmbeddedScript());
        sb.AppendLine("</script>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(Path.Combine(_options.OutputDir, "index.html"), sb.ToString());
    }

    private void RenderDocBlock(StringBuilder sb, DocCommentExtractor.DocBlock doc)
    {
        string id = $"-{doc.Name}";
        string kindClass = doc.Kind.Replace(" ", "-");

        sb.AppendLine($"  <article class=\"decl\" id=\"{WebUtility.HtmlEncode(id)}\" data-kind=\"{doc.Kind}\">");

        // Header row
        sb.AppendLine("    <div class=\"decl-header\">");
        sb.AppendLine($"      <span class=\"kind-badge {kindClass}\">{WebUtility.HtmlEncode(doc.Kind)}</span>");
        sb.AppendLine($"      <h3 class=\"decl-name\">{WebUtility.HtmlEncode(doc.Name)}</h3>");

        // Modifiers
        if (doc.Modifiers.Count > 0)
        {
            sb.AppendLine("      <span class=\"modifiers\">");
            foreach (var mod in doc.Modifiers)
                sb.Append($"<span class=\"mod\">{WebUtility.HtmlEncode(mod)}</span>");
            sb.AppendLine("      </span>");
        }

        // Type parameters
        if (doc.TypeParameters.Count > 0)
        {
            sb.AppendLine($"      <span class=\"type-params\">&lt;{WebUtility.HtmlEncode(string.Join(", ", doc.TypeParameters))}&gt;</span>");
        }

        // Base / interface types
        if (doc.BaseType != null || doc.InterfaceTypes.Count > 0)
        {
            sb.Append("      <span class=\"extends\">: ");
            var parts = new List<string>();
            if (doc.BaseType != null) parts.Add(WebUtility.HtmlEncode(doc.BaseType));
            foreach (var iface in doc.InterfaceTypes) parts.Add(WebUtility.HtmlEncode(iface));
            sb.Append(string.Join(", ", parts));
            sb.AppendLine("</span>");
        }

        sb.AppendLine("    </div>");

        // Signature
        sb.AppendLine($"    <pre class=\"signature\"><code>{RenderSignature(doc)}</code></pre>");

        // Attributes
        if (doc.Attributes.Count > 0)
        {
            sb.AppendLine("    <div class=\"attributes\">");
            foreach (var attr in doc.Attributes)
                sb.AppendLine($"      <span class=\"attr\">{WebUtility.HtmlEncode(attr)}</span>");
            sb.AppendLine("    </div>");
        }

        // Summary
        if (doc.Summary != null)
            sb.AppendLine($"    <p class=\"summary\">{WebUtility.HtmlEncode(doc.Summary)}</p>");

        // Parameters table
        var paramRows = OrderedParams(doc);
        if (paramRows.Count > 0)
        {
            sb.AppendLine("    <div class=\"params\">");
            sb.AppendLine("      <h4>Parameters</h4>");
            sb.AppendLine("      <table class=\"param-table\">");
            sb.AppendLine("        <thead><tr><th>Name</th><th>Type</th><th>Description</th></tr></thead>");
            sb.AppendLine("        <tbody>");
            foreach (var (paramName, paramType) in paramRows)
            {
                doc.Params.TryGetValue(paramName, out var paramDoc);
                sb.AppendLine("          <tr>");
                sb.AppendLine($"            <td class=\"param-name\"><code>{WebUtility.HtmlEncode(paramName)}</code></td>");
                sb.AppendLine($"            <td class=\"param-type\"><code>{WebUtility.HtmlEncode(paramType ?? "")}</code></td>");
                sb.AppendLine($"            <td class=\"param-desc\">{WebUtility.HtmlEncode(paramDoc ?? "")}</td>");
                sb.AppendLine("          </tr>");
            }
            sb.AppendLine("        </tbody>");
            sb.AppendLine("      </table>");
            sb.AppendLine("    </div>");
        }

        // Returns
        if (doc.ReturnType != null || doc.Returns != null)
        {
            sb.AppendLine("    <div class=\"returns\">");
            sb.AppendLine($"      <h4>{(doc.Kind == "field" ? "Type" : "Returns")}</h4>");
            sb.Append("      <p>");
            if (doc.ReturnType != null)
                sb.Append($"<code class=\"ret-type\">{WebUtility.HtmlEncode(doc.ReturnType)}</code> ");
            if (doc.Returns != null)
                sb.Append(WebUtility.HtmlEncode(doc.Returns));
            sb.AppendLine("</p>");
            sb.AppendLine("    </div>");
        }

        // Remarks
        if (doc.Remarks != null)
            sb.AppendLine($"    <div class=\"remarks\"><h4>Remarks</h4><p>{WebUtility.HtmlEncode(doc.Remarks)}</p></div>");

        // Example
        if (doc.Example != null)
            sb.AppendLine($"    <div class=\"example\"><h4>Example</h4><pre><code>{WebUtility.HtmlEncode(doc.Example)}</code></pre></div>");

        // Children (nested members)
        if (doc.Children.Count > 0)
        {
            sb.AppendLine("    <div class=\"members\">");
            sb.AppendLine($"      <h4>Members <span class=\"member-count\">({doc.Children.Count})</span></h4>");
            foreach (var child in doc.Children.OrderBy(c => c.Name))
                RenderDocBlock(sb, child);
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("  </article>");
    }

    /// <summary>
    /// Renders the signature as structured spans (modifiers, keyword, name,
    /// parameters with types, return type). Falls back to the raw line when
    /// the signature was not parsed into parts.
    /// </summary>
    private static string RenderSignature(DocCommentExtractor.DocBlock doc)
    {
        if (doc.Keyword == null && !doc.HasParenList && doc.ReturnType == null)
            return WebUtility.HtmlEncode(doc.Signature ?? doc.Name);

        var sb = new StringBuilder();
        foreach (var mod in doc.Modifiers)
            sb.Append($"<span class=\"sig-mod\">{WebUtility.HtmlEncode(mod)}</span> ");

        bool isCtor = doc.Keyword?.Equals("constructor", StringComparison.OrdinalIgnoreCase) == true;
        if (doc.Keyword != null)
            sb.Append($"<span class=\"sig-kw\">{WebUtility.HtmlEncode(doc.Keyword)}</span>");
        if (!isCtor && doc.Keyword != null)
            sb.Append(' ');
        if (!isCtor)
            sb.Append($"<span class=\"sig-name\">{WebUtility.HtmlEncode(doc.Name)}</span>");

        if (doc.TypeParameters.Count > 0 && !isCtor)
        {
            sb.Append("<span class=\"sig-type-params\">&lt;");
            for (int i = 0; i < doc.TypeParameters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"<span class=\"sig-type\">{WebUtility.HtmlEncode(doc.TypeParameters[i])}</span>");
            }
            sb.Append("&gt;</span>");
        }

        if (doc.HasParenList)
        {
            sb.Append('(');
            for (int i = 0; i < doc.Parameters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = doc.Parameters[i];
                sb.Append($"<span class=\"sig-pname\">{WebUtility.HtmlEncode(p.Name)}</span>");
                if (p.Type != null)
                    sb.Append($": <span class=\"sig-type\">{WebUtility.HtmlEncode(p.Type)}</span>");
            }
            sb.Append(')');
            if (doc.ReturnType != null)
                sb.Append($" <span class=\"sig-arrow\">-&gt;</span> <span class=\"sig-type\">{WebUtility.HtmlEncode(doc.ReturnType)}</span>");
        }
        else if (doc.ReturnType != null)
        {
            sb.Append($": <span class=\"sig-type\">{WebUtility.HtmlEncode(doc.ReturnType)}</span>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Merges signature parameters (declaration order) with documented ones;
    /// documented-but-undeclared names are appended alphabetically.
    /// </summary>
    private static List<(string Name, string? Type)> OrderedParams(DocCommentExtractor.DocBlock doc)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<(string, string?)>();
        foreach (var p in doc.Parameters)
            if (seen.Add(p.Name))
                list.Add((p.Name, p.Type));
        foreach (var name in doc.Params.Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (seen.Add(name))
                list.Add((name, null));
        return list;
    }

    private void GenerateMarkdown(
        List<DocCommentExtractor.DocBlock> allDocs,
        List<IGrouping<string, DocCommentExtractor.DocBlock>> byNamespace,
        List<DocCommentExtractor.DocBlock> rootDocs)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {_options.Title}");
        if (_options.Version != null)
            sb.AppendLine($"\n*Version: {_options.Version}*");
        if (_options.Description != null)
            sb.AppendLine($"\n{_options.Description}");

        sb.AppendLine("\n---\n");

        // Table of contents
        sb.AppendLine("## Table of Contents\n");
        foreach (var doc in rootDocs.OrderBy(d => d.Name))
            sb.AppendLine($"- [{doc.Name}](#{doc.Name.ToLower().Replace(" ", "-")})");
        foreach (var ns in byNamespace)
        {
            sb.AppendLine($"- **{ns.Key}**");
            foreach (var doc in ns.OrderBy(d => d.Name))
                sb.AppendLine($"  - [{doc.Name}](#{doc.Name.ToLower().Replace(" ", "-")})");
        }
        sb.AppendLine("\n---\n");

        // Root declarations
        foreach (var doc in rootDocs.OrderBy(d => d.Name))
            RenderMarkdownBlock(sb, doc, 2);

        // Namespaces
        foreach (var ns in byNamespace)
        {
            sb.AppendLine($"## {ns.Key}\n");
            foreach (var doc in ns.OrderBy(d => d.Name))
                RenderMarkdownBlock(sb, doc, 3);
        }

        File.WriteAllText(Path.Combine(_options.OutputDir, "index.md"), sb.ToString());
    }

    private void RenderMarkdownBlock(StringBuilder sb, DocCommentExtractor.DocBlock doc, int headingLevel)
    {
        string heading = new string('#', headingLevel);
        sb.AppendLine($"{heading} {doc.Name}\n");
        sb.AppendLine($"**{doc.Kind}**\n");

        if (doc.Signature != null)
        {
            sb.AppendLine("```contract");
            sb.AppendLine(doc.Signature);
            sb.AppendLine("```\n");
        }

        if (doc.Summary != null)
            sb.AppendLine($"{doc.Summary}\n");

        var paramRows = OrderedParams(doc);
        if (paramRows.Count > 0)
        {
            sb.AppendLine("**Parameters:**\n");
            sb.AppendLine("| Name | Type | Description |");
            sb.AppendLine("|------|------|-------------|");
            foreach (var (paramName, paramType) in paramRows)
            {
                doc.Params.TryGetValue(paramName, out var paramDoc);
                sb.AppendLine($"| `{paramName}` | `{paramType ?? ""}` | {paramDoc ?? ""} |");
            }
            sb.AppendLine();
        }

        if (doc.ReturnType != null || doc.Returns != null)
        {
            string label = doc.Kind == "field" ? "Type" : "Returns";
            var line = $"**{label}:**";
            if (doc.ReturnType != null)
                line += $" `{doc.ReturnType}`";
            if (doc.Returns != null)
                line += (doc.ReturnType != null ? " -- " : " ") + doc.Returns;
            sb.AppendLine(line + "\n");
        }

        if (doc.Remarks != null)
            sb.AppendLine($"**Remarks:** {doc.Remarks}\n");

        if (doc.Example != null)
        {
            sb.AppendLine("**Example:**\n");
            sb.AppendLine("```contract");
            sb.AppendLine(doc.Example);
            sb.AppendLine("```\n");
        }

        // Children
        foreach (var child in doc.Children.OrderBy(c => c.Name))
            RenderMarkdownBlock(sb, child, headingLevel + 1);

        sb.AppendLine("---\n");
    }

    private static string EmbeddedCss() => @"
/* ── Reset & base ─────────────────────────────────────────── */
*, *::before, *::after { margin: 0; padding: 0; box-sizing: border-box; }
:root {
  --bg: #111216;
  --bg-raised: #18191e;
  --bg-surface: #1e2028;
  --bg-hover: #252830;
  --border: #2a2d37;
  --border-subtle: #22242c;
  --text: #d1d5de;
  --text-dim: #888d9a;
  --text-muted: #5c6070;
  --accent: #6e8efb;
  --accent-dim: #4a629c;
  --kind-contract: #e8945a;
  --kind-struct: #b686e8;
  --kind-enum: #5cc98e;
  --kind-function: #6e8efb;
  --kind-constructor: #d4a0e8;
  --kind-field: #888d9a;
  --kind-extension: #e8c85a;
  --sig-kw: #e06070;
  --sig-name: #7cb5f5;
  --sig-pname: #e8a860;
  --sig-type: #80c8f0;
  --sig-mod: #e8945a;
  --mono: 'JetBrains Mono', 'SF Mono', 'Cascadia Code', Consolas, monospace;
  --sans: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  --sidebar-w: 272px;
  --content-pad: clamp(16px, 3vw, 48px);
}

html { scroll-behavior: smooth; scroll-padding-top: 24px; }
body {
  font-family: var(--sans);
  font-size: 15px;
  line-height: 1.65;
  color: var(--text);
  background: var(--bg);
  display: flex;
  min-height: 100vh;
  -webkit-font-smoothing: antialiased;
}

/* ── Mobile nav toggle ─────────────────────────────────────── */
.nav-toggle {
  display: none;
  position: fixed;
  top: 12px;
  left: 12px;
  z-index: 1001;
  width: 40px;
  height: 40px;
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: 8px;
  cursor: pointer;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
}
.nav-toggle span {
  display: block;
  width: 18px;
  height: 2px;
  background: var(--text);
  border-radius: 1px;
  transition: transform 0.2s, opacity 0.2s;
}
.nav-toggle.open span:nth-child(1) { transform: translateY(6px) rotate(45deg); }
.nav-toggle.open span:nth-child(2) { opacity: 0; }
.nav-toggle.open span:nth-child(3) { transform: translateY(-6px) rotate(-45deg); }

.nav-overlay {
  display: none;
  position: fixed;
  inset: 0;
  background: rgba(0,0,0,0.6);
  z-index: 999;
  backdrop-filter: blur(2px);
}

/* ── Sidebar ───────────────────────────────────────────────── */
.sidebar {
  width: var(--sidebar-w);
  background: var(--bg-raised);
  border-right: 1px solid var(--border);
  position: fixed;
  top: 0;
  left: 0;
  height: 100vh;
  overflow-y: auto;
  overscroll-behavior: contain;
  display: flex;
  flex-direction: column;
  scrollbar-width: thin;
  scrollbar-color: var(--border) transparent;
}
.sidebar-header {
  padding: 24px 20px 8px;
}
.sidebar-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text);
  letter-spacing: -0.01em;
}
.version {
  display: inline-block;
  font-size: 11px;
  color: var(--text-dim);
  background: var(--bg-surface);
  padding: 2px 8px;
  border-radius: 4px;
  margin-top: 6px;
  font-family: var(--mono);
}
.sidebar input {
  display: block;
  width: calc(100% - 40px);
  margin: 12px 20px;
  padding: 8px 12px;
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: 6px;
  color: var(--text);
  font-size: 13px;
  font-family: var(--sans);
  outline: none;
  transition: border-color 0.15s;
}
.sidebar input:focus { border-color: var(--accent-dim); }
.sidebar ul {
  list-style: none;
  padding: 0 8px 24px;
  flex: 1;
}
.sidebar li a {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  color: var(--text-dim);
  text-decoration: none;
  border-radius: 5px;
  font-size: 13px;
  transition: color 0.12s, background 0.12s;
}
.sidebar li a:hover { color: var(--text); background: var(--bg-hover); }
.sidebar li a.active { color: var(--text); background: var(--bg-surface); }

/* Kind indicators in nav */
.sidebar li a .kind-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
}
.sidebar li a[data-kind=""contract""] .kind-dot { background: var(--kind-contract); }
.sidebar li a[data-kind=""struct""] .kind-dot { background: var(--kind-struct); }
.sidebar li a[data-kind=""enum""] .kind-dot { background: var(--kind-enum); }
.sidebar li a[data-kind=""function""] .kind-dot { background: var(--kind-function); }
.sidebar li a[data-kind=""constructor""] .kind-dot { background: var(--kind-constructor); }
.sidebar li a[data-kind=""field""] .kind-dot { background: var(--kind-field); }
.sidebar li a[data-kind=""extension""] .kind-dot { background: var(--kind-extension); }

/* Fallback for links without .kind-dot (legacy markup) */
.sidebar li a[data-kind=""contract""]::before { content: ''; }
.sidebar li a[data-kind=""struct""]::before { content: ''; }
.sidebar li a[data-kind=""enum""]::before { content: ''; }
.sidebar li a[data-kind=""function""]::before { content: ''; }
.sidebar li a[data-kind=""constructor""]::before { content: ''; }
.sidebar li a[data-kind=""field""]::before { content: ''; }
.sidebar li a[data-kind=""extension""]::before { content: ''; }

.nav-section {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-muted);
  padding: 16px 12px 4px;
}

/* ── Main content ──────────────────────────────────────────── */
.content {
  margin-left: var(--sidebar-w);
  padding: var(--content-pad);
  padding-top: clamp(24px, 3vw, 48px);
  max-width: 780px;
  width: 100%;
}
.page-header {
  margin-bottom: 40px;
  padding-bottom: 24px;
  border-bottom: 1px solid var(--border-subtle);
}
.page-header h1 {
  font-size: clamp(22px, 3vw, 30px);
  font-weight: 700;
  color: var(--text);
  letter-spacing: -0.02em;
  line-height: 1.2;
}
.description {
  color: var(--text-dim);
  margin-top: 8px;
  font-size: 15px;
  line-height: 1.6;
}

/* ── Declaration blocks ────────────────────────────────────── */
.decl {
  margin-bottom: 36px;
  padding-bottom: 28px;
  border-bottom: 1px solid var(--border-subtle);
}
.decl:last-child { border-bottom: none; }

.decl-header {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}
.decl-name {
  font-size: clamp(17px, 2.2vw, 21px);
  font-weight: 600;
  color: #f0f2f8;
  letter-spacing: -0.01em;
}

.kind-badge {
  display: inline-flex;
  align-items: center;
  font-size: 10px;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  flex-shrink: 0;
}
.kind-badge.contract { background: rgba(232,148,90,0.15); color: var(--kind-contract); }
.kind-badge.struct { background: rgba(182,134,232,0.15); color: var(--kind-struct); }
.kind-badge.enum { background: rgba(92,201,142,0.15); color: var(--kind-enum); }
.kind-badge.function { background: rgba(110,142,251,0.15); color: var(--kind-function); }
.kind-badge.constructor { background: rgba(212,160,232,0.15); color: var(--kind-constructor); }
.kind-badge.field { background: rgba(136,141,154,0.15); color: var(--kind-field); }
.kind-badge.extension { background: rgba(232,200,90,0.15); color: var(--kind-extension); }

.modifiers {
  display: inline-flex;
  gap: 4px;
}
.mod {
  font-size: 11px;
  font-family: var(--mono);
  color: var(--sig-mod);
  background: rgba(232,148,90,0.1);
  padding: 1px 6px;
  border-radius: 3px;
}
.type-params, .extends {
  font-size: 13px;
  font-family: var(--mono);
  color: var(--sig-type);
}

/* ── Signature ─────────────────────────────────────────────── */
.signature {
  background: var(--bg-raised);
  padding: 12px 16px;
  border-radius: 6px;
  border: 1px solid var(--border-subtle);
  font-family: var(--mono);
  font-size: 13px;
  color: #e2e5ed;
  overflow-x: auto;
  margin: 8px 0 12px;
  line-height: 1.5;
  -webkit-overflow-scrolling: touch;
}
.signature code { font-family: inherit; }
.sig-mod { color: var(--sig-mod); }
.sig-kw { color: var(--sig-kw); }
.sig-name { color: var(--sig-name); font-weight: 600; }
.sig-pname { color: var(--sig-pname); }
.sig-type { color: var(--sig-type); }
.sig-arrow { color: var(--text-muted); }
.sig-type-params { color: var(--sig-type); }

/* ── Attributes ────────────────────────────────────────────── */
.attributes {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 8px 0;
}
.attr {
  font-size: 12px;
  font-family: var(--mono);
  color: var(--accent);
  background: rgba(110,142,251,0.1);
  padding: 2px 8px;
  border-radius: 4px;
}

/* ── Summary ───────────────────────────────────────────────── */
.summary {
  margin: 8px 0 12px;
  line-height: 1.65;
  color: var(--text);
}

/* ── Parameters table ──────────────────────────────────────── */
.params { margin: 12px 0; }
.params h4, .returns h4, .remarks h4, .example h4 {
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  margin: 16px 0 8px;
}
.param-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}
.param-table th {
  text-align: left;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  padding: 6px 12px 6px 0;
  border-bottom: 1px solid var(--border);
}
.param-table td {
  padding: 8px 12px 8px 0;
  border-bottom: 1px solid var(--border-subtle);
  vertical-align: top;
}
.param-table td:first-child { padding-left: 0; }
.param-name code {
  font-family: var(--mono);
  font-size: 13px;
  color: var(--sig-pname);
  background: none;
  padding: 0;
  border: none;
}
.param-type code {
  font-family: var(--mono);
  font-size: 13px;
  color: var(--sig-type);
  background: none;
  padding: 0;
  border: none;
}
.param-desc { color: var(--text-dim); }

/* ── Returns ───────────────────────────────────────────────── */
.returns p { font-size: 14px; line-height: 1.6; }
.ret-type {
  font-family: var(--mono);
  font-size: 13px;
  color: var(--kind-enum);
  background: rgba(92,201,142,0.1);
  padding: 2px 8px;
  border-radius: 4px;
}

/* ── Remarks & Example ─────────────────────────────────────── */
.remarks p { font-size: 14px; line-height: 1.65; }
.example pre {
  background: var(--bg-raised);
  padding: 14px 18px;
  border-radius: 6px;
  border: 1px solid var(--border-subtle);
  font-family: var(--mono);
  font-size: 13px;
  overflow-x: auto;
  line-height: 1.55;
  -webkit-overflow-scrolling: touch;
}
.example code { font-family: inherit; }

/* ── Namespace sections ─────────────────────────────────────── */
.namespace {
  color: var(--accent);
  font-size: clamp(16px, 2vw, 20px);
  font-weight: 600;
  margin: 40px 0 20px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--border);
  letter-spacing: -0.01em;
}

/* ── Nested members ────────────────────────────────────────── */
.members {
  margin-top: 16px;
  padding-left: 16px;
  border-left: 2px solid var(--border);
}
.members h4 {
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  margin: 16px 0 8px;
}
.member-count { font-weight: 400; color: var(--text-muted); font-size: 11px; }

/* ── Utilities ─────────────────────────────────────────────── */
.hidden { display: none !important; }

/* ── Responsive: tablet ─────────────────────────────────────── */
@media (max-width: 860px) {
  .content {
    margin-left: 0;
    max-width: 100%;
  }
  .sidebar {
    transform: translateX(-100%);
    transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1);
    z-index: 1000;
    box-shadow: 4px 0 24px rgba(0,0,0,0.4);
  }
  .sidebar.open {
    transform: translateX(0);
  }
  .nav-toggle { display: flex; }
  .nav-overlay.visible { display: block; }

  .content {
    padding-top: 60px;
  }

  .param-table thead { display: none; }
  .param-table, .param-table tbody, .param-table tr, .param-table td {
    display: block;
  }
  .param-table tr {
    padding: 8px 0;
    border-bottom: 1px solid var(--border-subtle);
  }
  .param-table td {
    padding: 2px 0;
    border: none;
  }
  .param-table td:first-child { font-weight: 600; }
}

/* ── Responsive: phone ──────────────────────────────────────── */
@media (max-width: 480px) {
  .page-header h1 { font-size: 20px; }
  .decl-name { font-size: 17px; }
  .signature { font-size: 12px; padding: 10px 12px; }
  .members { padding-left: 10px; }
  .decl { margin-bottom: 28px; padding-bottom: 20px; }
}

/* ── Print ──────────────────────────────────────────────────── */
@media print {
  .sidebar, .nav-toggle, .nav-overlay { display: none !important; }
  .content { margin-left: 0; max-width: 100%; }
  body { background: white; color: #1a1a1a; }
  .signature { background: #f5f5f5; border-color: #ddd; }
  .decl { page-break-inside: avoid; }
}
";

    private static string EmbeddedScript() => @"
(function() {
  const sidebar = document.getElementById('sidebar');
  const toggle = document.getElementById('nav-toggle');
  const overlay = document.getElementById('nav-overlay');
  const search = document.getElementById('search');
  const navList = document.getElementById('nav-list');
  const decls = document.querySelectorAll('.decl');
  const navLinks = navList.querySelectorAll('li a');

  // Mobile sidebar toggle
  toggle.addEventListener('click', () => {
    sidebar.classList.toggle('open');
    toggle.classList.toggle('open');
    overlay.classList.toggle('visible');
  });
  overlay.addEventListener('click', () => {
    sidebar.classList.remove('open');
    toggle.classList.remove('open');
    overlay.classList.remove('visible');
  });

  // Close sidebar on nav click (mobile)
  navLinks.forEach(a => {
    a.addEventListener('click', () => {
      if (window.innerWidth <= 860) {
        sidebar.classList.remove('open');
        toggle.classList.remove('open');
        overlay.classList.remove('visible');
      }
    });
  });

  // Search
  search.addEventListener('input', () => {
    const q = search.value.toLowerCase();
    navLinks.forEach(a => {
      const match = a.textContent.toLowerCase().includes(q);
      a.parentElement.style.display = match || !q ? '' : 'none';
    });
    // Also show/hide section headers when all their children are hidden
    navList.querySelectorAll('.nav-section').forEach(sec => {
      let next = sec.nextElementSibling;
      let anyVisible = false;
      while (next && !next.classList.contains('nav-section')) {
        if (next.style.display !== 'none') anyVisible = true;
        next = next.nextElementSibling;
      }
      sec.style.display = anyVisible || !q ? '' : 'none';
    });
    decls.forEach(d => {
      const name = d.querySelector('.decl-name')?.textContent.toLowerCase() || '';
      const sig = d.querySelector('.signature')?.textContent.toLowerCase() || '';
      const summary = d.querySelector('.summary')?.textContent.toLowerCase() || '';
      d.classList.toggle('hidden', q && !name.includes(q) && !sig.includes(q) && !summary.includes(q));
    });
  });

  // Scrollspy: highlight current declaration in nav
  const observer = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        const id = entry.target.id;
        navLinks.forEach(a => {
          a.classList.toggle('active', a.getAttribute('href') === '#' + id);
        });
      }
    });
  }, { rootMargin: '-20% 0px -70% 0px' });

  decls.forEach(d => observer.observe(d));
})();
";
}
