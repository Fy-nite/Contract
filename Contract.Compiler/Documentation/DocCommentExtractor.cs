using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Contract.Compiler.Documentation;

/// <summary>
/// Extracts structured documentation from `///` XML doc comments in Contract source files.
/// Parses `&lt;summary&gt;`, `&lt;param&gt;`, `&lt;returns&gt;`, `&lt;remarks&gt;` (or `&lt;remark&gt;`),
/// and `&lt;example&gt;` tags, and decomposes declaration signatures into
/// modifiers, parameters, and return types.
/// </summary>
public static class DocCommentExtractor
{
    /// <summary>
    /// A single parameter parsed from a declaration signature.
    /// </summary>
    public class ParamInfo
    {
        /// <summary>The parameter name.</summary>
        public string Name { get; set; } = "";

        /// <summary>The declared parameter type, when annotated.</summary>
        public string? Type { get; set; }
    }

    /// <summary>
    /// A parsed documentation block for a single declaration.
    /// </summary>
    public class DocBlock
    {
        /// <summary>The declaration name (contract, struct, function, etc.).</summary>
        public string Name { get; set; } = "";

        /// <summary>The kind of declaration (contract, struct, enum, function, field, parameter).</summary>
        public string Kind { get; set; } = "";

        /// <summary>The one-based line number of the declaration.</summary>
        public int Line { get; set; }

        /// <summary>The source file this declaration came from.</summary>
        public string? SourceFile { get; set; }

        /// <summary>The namespace this declaration belongs to.</summary>
        public string? Namespace { get; set; }

        /// <summary>First line summary text.</summary>
        public string? Summary { get; set; }

        /// <summary>Detailed remarks section.</summary>
        public string? Remarks { get; set; }

        /// <summary>Example usage block.</summary>
        public string? Example { get; set; }

        /// <summary>Parameter documentation (name -> description).</summary>
        public Dictionary<string, string> Params { get; set; } = new();

        /// <summary>Return value description.</summary>
        public string? Returns { get; set; }

        /// <summary>Raw doc comment text (before XML parsing).</summary>
        public string RawDoc { get; set; } = "";

        /// <summary>Full declaration signature line (e.g. "fn Add(int a, int b) -> int").</summary>
        public string? Signature { get; set; }

        /// <summary>The declaration keyword from the signature (fn, fun, Contract, struct, enum, constructor, var, let).</summary>
        public string? Keyword { get; set; }

        /// <summary>Modifier keywords preceding the declaration (public, private, static, export, ...).</summary>
        public List<string> Modifiers { get; set; } = new();

        /// <summary>Parameters parsed from the declaration, in declaration order.</summary>
        public List<ParamInfo> Parameters { get; set; } = new();

        /// <summary>Whether the declaration carries a parenthesised parameter list.</summary>
        public bool HasParenList { get; set; }

        /// <summary>
        /// The declared return type (functions), or the declared type (fields).
        /// Null when absent or not inferable from the signature line.
        /// </summary>
        public string? ReturnType { get; set; }

        /// <summary>Child members (for contracts/structs with nested functions/fields).</summary>
        public List<DocBlock> Children { get; set; } = new();

        /// <summary>Parent declaration name (for nested members).</summary>
        public string? Parent { get; set; }
    }

    /// <summary>
    /// Extracts all documentation blocks from a source file.
    /// Returns one DocBlock per declaration that has a `///` comment above it.
    /// </summary>
    public static List<DocBlock> ExtractFromSource(string source, string? sourceFile = null)
    {
        var results = new List<DocBlock>();
        var lines = source.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            // Look for `///` doc comment blocks
            if (!trimmed.StartsWith("///")) continue;

            // Collect the full doc comment block
            var docLines = new List<string>();
            int docStart = i;
            while (i < lines.Length && lines[i].Trim().StartsWith("///"))
            {
                string text = lines[i].Trim().Substring(3).Trim();
                docLines.Add(text);
                i++;
            }

            // Skip blank lines and find the declaration
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                i++;

            if (i >= lines.Length) break;

            string declLine = lines[i].Trim();
            if (string.IsNullOrEmpty(declLine)) continue;

            // Parse the declaration to extract name and kind
            var (name, kind) = ParseDeclaration(declLine);
            if (string.IsNullOrEmpty(name)) continue;

            var doc = ParseDocBlock(string.Join("\n", docLines), name, kind, i + 1, sourceFile);
            doc.Signature = declLine;
            ParseSignature(doc, declLine);
            results.Add(doc);
        }

        return results;
    }

    /// <summary>
    /// Parses a `///` comment block into a structured DocBlock.
    /// </summary>
    public static DocBlock ParseDocBlock(string rawDoc, string name, string kind, int line, string? sourceFile = null)
    {
        var doc = new DocBlock
        {
            Name = name,
            Kind = kind,
            Line = line,
            SourceFile = sourceFile,
            RawDoc = rawDoc
        };

        // Extract <summary>
        var summaryMatch = Regex.Match(rawDoc, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
        if (summaryMatch.Success)
            doc.Summary = summaryMatch.Groups[1].Value.Trim();

        // Extract <remarks> (the singular <remark> is accepted as an alias)
        var remarksMatch = Regex.Match(rawDoc, @"<(?:remarks|remark)>\s*(.*?)\s*</(?:remarks|remark)>", RegexOptions.Singleline);
        if (remarksMatch.Success)
            doc.Remarks = remarksMatch.Groups[1].Value.Trim();

        // Extract <example>
        var exampleMatch = Regex.Match(rawDoc, @"<example>\s*(.*?)\s*</example>", RegexOptions.Singleline);
        if (exampleMatch.Success)
            doc.Example = exampleMatch.Groups[1].Value.Trim();

        // Extract <returns>
        var returnsMatch = Regex.Match(rawDoc, @"<returns>\s*(.*?)\s*</returns>", RegexOptions.Singleline);
        if (returnsMatch.Success)
            doc.Returns = returnsMatch.Groups[1].Value.Trim();

        // Extract <param name="x"> tags
        var paramMatches = Regex.Matches(rawDoc, @"<param\s+name=""(\w+)"">\s*(.*?)\s*</param>", RegexOptions.Singleline);
        foreach (Match m in paramMatches)
            doc.Params[m.Groups[1].Value] = m.Groups[2].Value.Trim();

        // If no <summary> tag, use the first non-tag line as summary
        if (doc.Summary == null)
        {
            foreach (var docLine in rawDoc.Split('\n'))
            {
                string trimmed = docLine.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("<"))
                {
                    doc.Summary = trimmed;
                    break;
                }
            }
        }

        return doc;
    }

    /// <summary>
    /// Parses a declaration line to extract the declaration name and kind.
    /// Returns (name, kind) or (null, null) if not a recognized declaration.
    /// </summary>
    private static (string? name, string kind) ParseDeclaration(string line)
    {
        // Modifiers that may precede any declaration.
        const string Mods = @"(?:public\s+|private\s+|protected\s+|internal\s+|static\s+|export\s+)*";

        // Contract Name (the grammar's keyword is capital-C; accept either case)
        var contractMatch = Regex.Match(line, @"^\s*" + Mods + @"[Cc]ontract\s+(\w+)");
        if (contractMatch.Success)
            return (contractMatch.Groups[1].Value, "contract");

        // struct Name
        var structMatch = Regex.Match(line, @"^\s*" + Mods + @"struct\s+(\w+)");
        if (structMatch.Success)
            return (structMatch.Groups[1].Value, "struct");

        // enum Name
        var enumMatch = Regex.Match(line, @"^\s*" + Mods + @"enum\s+(\w+)");
        if (enumMatch.Success)
            return (enumMatch.Groups[1].Value, "enum");

        // fn Name(...) or fun Name(...)
        var fnMatch = Regex.Match(line, @"^\s*" + Mods + @"(?:fn|fun)\s+(\w+)");
        if (fnMatch.Success)
            return (fnMatch.Groups[1].Value, "function");

        // constructor(...)
        var ctorMatch = Regex.Match(line, @"^\s*" + Mods + @"constructor\s*\(");
        if (ctorMatch.Success)
            return ("constructor", "constructor");

        // let/var Name (: Type)? = fn(...) / fun(...)   (function-valued binding)
        var fnValueMatch = Regex.Match(line, @"^\s*(?:let|var)\s+(\w+)(?:\s*:\s*[\w<>,\.\[\]\?]+)?\s*=\s*(?:fn|fun)\s*\(");
        if (fnValueMatch.Success)
            return (fnValueMatch.Groups[1].Value, "function");

        // [mods] name: Type   (field declaration — the grammar is name-colon-type)
        var fieldMatch = Regex.Match(line, @"^\s*" + Mods + @"(\w+)\s*:\s*[A-Za-z_][\w<>,\.\[\]\?]*");
        if (fieldMatch.Success)
            return (fieldMatch.Groups[1].Value, "field");

        // let/var Name = expression   (initialized field)
        var letMatch = Regex.Match(line, @"^\s*(?:let|var)\s+(\w+)\s*=");
        if (letMatch.Success)
            return (letMatch.Groups[1].Value, "field");

        return (null, "");
    }

    private static readonly string[] KnownModifiers =
        { "public", "private", "protected", "internal", "static", "export" };

    /// <summary>
    /// Parses a declaration line into its structural parts and fills the given
    /// block's Keyword, Modifiers, Parameters, HasParenList, and ReturnType.
    /// Anything unrecognized is left unset; the raw signature line remains the fallback.
    /// </summary>
    public static void ParseSignature(DocBlock doc, string line)
    {
        // Header only: everything before the first brace or terminator.
        int cut = line.IndexOfAny(new[] { '{', '}', ';' });
        string s = (cut >= 0 ? line[..cut] : line).Trim();
        if (s.Length == 0) return;

        // Leading modifiers.
        bool progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (var mod in KnownModifiers)
            {
                if (s.StartsWith(mod, StringComparison.Ordinal) &&
                    s.Length > mod.Length && char.IsWhiteSpace(s[mod.Length]))
                {
                    doc.Modifiers.Add(mod);
                    s = s[(mod.Length)..].TrimStart();
                    progressed = true;
                    break;
                }
            }
        }

        // Declaration keyword.
        var kw = Regex.Match(s, @"^(fn|fun|contract|struct|enum|constructor|var|let)\b", RegexOptions.IgnoreCase);
        if (!kw.Success)
        {
            // Bare field declaration: "name: Type".
            var field = Regex.Match(s, @"^(\w+)\s*:\s*(.+)$", RegexOptions.Singleline);
            if (!field.Success) return;
            doc.Name = field.Groups[1].Value;
            doc.ReturnType = field.Groups[2].Value.Trim();
            return;
        }
        doc.Keyword = kw.Groups[1].Value;
        s = s[kw.Length..].TrimStart();

        if (!doc.Keyword.Equals("constructor", StringComparison.OrdinalIgnoreCase))
        {
            var nameMatch = Regex.Match(s, @"^(\w+)");
            if (!nameMatch.Success) return;
            doc.Name = nameMatch.Groups[1].Value;
            s = s[nameMatch.Length..].TrimStart();
        }

        // Parameter list: fn Name(params...) -> Ret
        if (s.StartsWith("("))
        {
            int close = FindBalanced(s, '(', ')');
            if (close < 0) return;
            doc.HasParenList = true;
            doc.Parameters = SplitParams(s.Substring(1, close - 1));
            s = s[(close + 1)..].TrimStart();
            ParseReturnType(doc, s);
            return;
        }

        // Function-valued binding: let f = fun x -> ... / var g = fun (a: int) -> ...
        if (s.StartsWith("="))
        {
            s = s[1..].TrimStart();
            if (!Regex.IsMatch(s, @"^(fn|fun)\b", RegexOptions.IgnoreCase)) return;
            s = Regex.Replace(s, @"^(fn|fun)\b", "", RegexOptions.IgnoreCase).TrimStart();

            if (s.StartsWith("("))
            {
                int close = FindBalanced(s, '(', ')');
                if (close < 0) return;
                doc.HasParenList = true;
                doc.Parameters = SplitParams(s.Substring(1, close - 1));
                s = s[(close + 1)..].TrimStart();
            }
            else
            {
                // Bare-parameter lambda: identifiers up to "->".
                int arrow = s.IndexOf("->", StringComparison.Ordinal);
                var head = (arrow >= 0 ? s[..arrow] : s).Trim();
                doc.HasParenList = true;
                foreach (var id in head.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    doc.Parameters.Add(new ParamInfo { Name = id.Trim() });
                s = arrow >= 0 ? s[arrow..] : "";
            }

            ParseReturnType(doc, s);
            return;
        }

        // Field: remaining ": Type".
        if (s.StartsWith(":"))
        {
            var type = s[1..].Trim();
            doc.ReturnType = type.Length > 0 ? type : null;
        }
    }

    private static void ParseReturnType(DocBlock doc, string remainder)
    {
        if (!remainder.StartsWith("->")) return;
        var ret = remainder[2..].Trim();
        doc.ReturnType = ret.Length > 0 ? ret : null;
    }

    /// <summary>Finds the index of the paren/bracket matching the one at index 0.</summary>
    private static int FindBalanced(string s, char open, char close)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == open) depth++;
            else if (s[i] == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Splits a parameter list on top-level commas (commas nested in (), [], or &lt;&gt;
    /// are preserved) and parses each part as "name" or "name: type".
    /// </summary>
    private static List<ParamInfo> SplitParams(string text)
    {
        var list = new List<ParamInfo>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var parts = new List<string>();
        var cur = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // Arrows ("->") belong to function types; their ">" must not
            // be counted as a generic closer.
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '>')
            {
                cur.Append("->");
                i++;
                continue;
            }

            switch (c)
            {
                case '(' or '[' or '<':
                    depth++;
                    cur.Append(c);
                    break;
                case ')' or ']' or '>' when depth > 0:
                    depth--;
                    cur.Append(c);
                    break;
                case ',' when depth == 0:
                    parts.Add(cur.ToString());
                    cur.Clear();
                    break;
                default:
                    cur.Append(c);
                    break;
            }
        }
        if (cur.Length > 0) parts.Add(cur.ToString());

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            var m = Regex.Match(p, @"^(\w+)\s*:\s*(.+)$", RegexOptions.Singleline);
            if (m.Success)
                list.Add(new ParamInfo { Name = m.Groups[1].Value, Type = m.Groups[2].Value.Trim() });
            else
                list.Add(new ParamInfo { Name = p });
        }
        return list;
    }
}
