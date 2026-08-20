using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;

namespace Contract.Compiler.Documentation;

/// <summary>
/// Generates HTML documentation from extracted doc blocks.
/// Produces a single-file static site with navigation and search.
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

        // Sidebar
        sb.AppendLine("<nav class=\"sidebar\">");
        sb.AppendLine($"  <h2>{WebUtility.HtmlEncode(_options.Title)}</h2>");
        if (_options.Version != null)
            sb.AppendLine($"  <span class=\"version\">v{WebUtility.HtmlEncode(_options.Version)}</span>");
        sb.AppendLine("  <input type=\"text\" id=\"search\" placeholder=\"Search...\" autocomplete=\"off\">");
        sb.AppendLine("  <ul id=\"nav-list\">");

        // Root declarations
        if (rootDocs.Count > 0)
        {
            sb.AppendLine("    <li class=\"nav-section\">Root</li>");
            foreach (var doc in rootDocs.OrderBy(d => d.Name))
                sb.AppendLine($"      <li><a href=\"#-{WebUtility.HtmlEncode(doc.Name)}\" data-kind=\"{doc.Kind}\">{WebUtility.HtmlEncode(doc.Name)}</a></li>");
        }

        // Namespaces
        foreach (var ns in byNamespace)
        {
            sb.AppendLine($"    <li class=\"nav-section\">{WebUtility.HtmlEncode(ns.Key)}</li>");
            foreach (var doc in ns.OrderBy(d => d.Name))
                sb.AppendLine($"      <li><a href=\"#-{WebUtility.HtmlEncode(doc.Name)}\" data-kind=\"{doc.Kind}\">{WebUtility.HtmlEncode(doc.Name)}</a></li>");
        }

        sb.AppendLine("  </ul>");
        sb.AppendLine("</nav>");

        // Main content
        sb.AppendLine("<main class=\"content\">");

        if (_options.Description != null)
            sb.AppendLine($"  <p class=\"description\">{WebUtility.HtmlEncode(_options.Description)}</p>");

        // Render root declarations
        foreach (var doc in rootDocs.OrderBy(d => d.Name))
            RenderDocBlock(sb, doc);

        // Render by namespace
        foreach (var ns in byNamespace)
        {
            sb.AppendLine($"  <h2 class=\"namespace\">{WebUtility.HtmlEncode(ns.Key)}</h2>");
            foreach (var doc in ns.OrderBy(d => d.Name))
                RenderDocBlock(sb, doc);
        }

        sb.AppendLine("</main>");

        // Search script
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
        sb.AppendLine($"  <div class=\"decl\" id=\"{WebUtility.HtmlEncode(id)}\" data-kind=\"{doc.Kind}\">");
        sb.AppendLine($"    <div class=\"decl-header\">");
        sb.AppendLine($"      <span class=\"kind-badge {doc.Kind}\">{doc.Kind}</span>");
        sb.AppendLine($"      <h3 class=\"decl-name\">{WebUtility.HtmlEncode(doc.Name)}</h3>");
        sb.AppendLine($"    </div>");

        if (doc.Signature != null)
            sb.AppendLine($"    <pre class=\"signature\">{WebUtility.HtmlEncode(doc.Signature)}</pre>");

        if (doc.Summary != null)
            sb.AppendLine($"    <p class=\"summary\">{WebUtility.HtmlEncode(doc.Summary)}</p>");

        if (doc.Params.Count > 0)
        {
            sb.AppendLine("    <div class=\"params\">");
            sb.AppendLine("      <h4>Parameters</h4>");
            sb.AppendLine("      <dl>");
            foreach (var (paramName, paramDoc) in doc.Params.OrderBy(p => p.Key))
            {
                sb.AppendLine($"        <dt>{WebUtility.HtmlEncode(paramName)}</dt>");
                sb.AppendLine($"        <dd>{WebUtility.HtmlEncode(paramDoc)}</dd>");
            }
            sb.AppendLine("      </dl>");
            sb.AppendLine("    </div>");
        }

        if (doc.Returns != null)
            sb.AppendLine($"    <div class=\"returns\"><h4>Returns</h4><p>{WebUtility.HtmlEncode(doc.Returns)}</p></div>");

        if (doc.Remarks != null)
            sb.AppendLine($"    <div class=\"remarks\"><h4>Remarks</h4><p>{WebUtility.HtmlEncode(doc.Remarks)}</p></div>");

        if (doc.Example != null)
            sb.AppendLine($"    <div class=\"example\"><h4>Example</h4><pre>{WebUtility.HtmlEncode(doc.Example)}</pre></div>");

        // Render children (nested members)
        if (doc.Children.Count > 0)
        {
            sb.AppendLine("    <div class=\"members\">");
            sb.AppendLine("      <h4>Members</h4>");
            foreach (var child in doc.Children.OrderBy(c => c.Name))
                RenderDocBlock(sb, child);
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("  </div>");
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

        if (doc.Params.Count > 0)
        {
            sb.AppendLine("**Parameters:**\n");
            sb.AppendLine("| Name | Description |");
            sb.AppendLine("|------|-------------|");
            foreach (var (paramName, paramDoc) in doc.Params.OrderBy(p => p.Key))
                sb.AppendLine($"| `{paramName}` | {paramDoc} |");
            sb.AppendLine();
        }

        if (doc.Returns != null)
            sb.AppendLine($"**Returns:** {doc.Returns}\n");

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
      * { margin: 0; padding: 0; box-sizing: border-box; }
      body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; min-height: 100vh; background: #0d1117; color: #c9d1d9; }
      .sidebar { width: 280px; background: #161b22; border-right: 1px solid #30363d; padding: 20px; position: fixed; height: 100vh; overflow-y: auto; }
      .sidebar h2 { font-size: 16px; color: #58a6ff; margin-bottom: 4px; }
      .version { font-size: 12px; color: #8b949e; }
      .sidebar input { width: 100%; padding: 8px; margin: 12px 0; background: #0d1117; border: 1px solid #30363d; border-radius: 6px; color: #c9d1d9; font-size: 13px; }
      .sidebar ul { list-style: none; }
      .sidebar li a { display: block; padding: 4px 8px; color: #c9d1d9; text-decoration: none; border-radius: 4px; font-size: 13px; }
      .sidebar li a:hover { background: #21262d; }
      .sidebar li a[data-kind=""contract""]::before { content: 'C '; color: #f0883e; font-weight: bold; }
      .sidebar li a[data-kind=""struct""]::before { content: 'S '; color: #a371f7; font-weight: bold; }
      .sidebar li a[data-kind=""enum""]::before { content: 'E '; color: #3fb950; font-weight: bold; }
      .sidebar li a[data-kind=""function""]::before { content: 'f '; color: #58a6ff; font-weight: bold; }
      .sidebar li a[data-kind=""field""]::before { content: 'F '; color: #8b949e; font-weight: bold; }
      .nav-section { font-size: 11px; text-transform: uppercase; color: #8b949e; padding: 8px 8px 2px; letter-spacing: 0.5px; }
      .content { margin-left: 280px; padding: 40px; max-width: 900px; }
      .description { color: #8b949e; margin-bottom: 24px; font-size: 15px; }
      .decl { margin-bottom: 32px; padding-bottom: 24px; border-bottom: 1px solid #21262d; }
      .decl-header { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
      .decl-name { font-size: 20px; color: #f0f6fc; }
      .kind-badge { font-size: 11px; padding: 2px 8px; border-radius: 12px; font-weight: 600; text-transform: uppercase; }
      .kind-badge.contract { background: #f0883e22; color: #f0883e; }
      .kind-badge.struct { background: #a371f722; color: #a371f7; }
      .kind-badge.enum { background: #3fb95022; color: #3fb950; }
      .kind-badge.function { background: #58a6ff22; color: #58a6ff; }
      .kind-badge.field { background: #8b949e22; color: #8b949e; }
      .signature { background: #161b22; padding: 12px 16px; border-radius: 6px; border: 1px solid #30363d; font-family: 'SF Mono', Consolas, monospace; font-size: 13px; color: #e6edf3; overflow-x: auto; margin: 8px 0; }
      .summary { margin: 8px 0; line-height: 1.6; }
      .params h4, .returns h4, .remarks h4, .example h4 { font-size: 13px; color: #8b949e; margin: 12px 0 6px; }
      .params dl { display: grid; grid-template-columns: 120px 1fr; gap: 4px 16px; }
      .params dt { font-family: 'SF Mono', Consolas, monospace; font-size: 13px; color: #58a6ff; }
      .params dd { font-size: 14px; }
      .returns p { font-size: 14px; }
      .remarks p { font-size: 14px; line-height: 1.6; }
      .example pre { background: #161b22; padding: 12px 16px; border-radius: 6px; border: 1px solid #30363d; font-family: 'SF Mono', Consolas, monospace; font-size: 13px; overflow-x: auto; }
      .namespace { color: #58a6ff; margin: 32px 0 16px; font-size: 18px; border-bottom: 1px solid #21262d; padding-bottom: 8px; }
      .members { margin-left: 20px; padding-left: 16px; border-left: 2px solid #21262d; }
      .hidden { display: none; }
    ";

    private static string EmbeddedScript() => @"
      const search = document.getElementById('search');
      const navList = document.getElementById('nav-list');
      const decls = document.querySelectorAll('.decl');
      search.addEventListener('input', () => {
        const q = search.value.toLowerCase();
        navList.querySelectorAll('li a').forEach(a => {
          const match = a.textContent.toLowerCase().includes(q);
          a.parentElement.style.display = match || !q ? '' : 'none';
        });
        decls.forEach(d => {
          const name = d.querySelector('.decl-name')?.textContent.toLowerCase() || '';
          const sig = d.querySelector('.signature')?.textContent.toLowerCase() || '';
          d.classList.toggle('hidden', q && !name.includes(q) && !sig.includes(q));
        });
      });
    ";
}
