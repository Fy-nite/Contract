using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Contract.Compiler.AST;

namespace Contract.Compiler.Documentation;

/// <summary>
/// Extracts structured documentation from Contract source files.
/// The primary path walks the AST for typed declaration info and scans
/// raw source text for <c>///</c> doc comments above each declaration.
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

        /// <summary>The kind of declaration (contract, struct, enum, function, field, constructor).</summary>
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

        /// <summary>Parameter documentation (name -&gt; description).</summary>
        public Dictionary<string, string> Params { get; set; } = new();

        /// <summary>Return value description.</summary>
        public string? Returns { get; set; }

        /// <summary>Raw doc comment text (before XML parsing).</summary>
        public string RawDoc { get; set; } = "";

        /// <summary>Full declaration signature (reconstructed from AST).</summary>
        public string? Signature { get; set; }

        /// <summary>The declaration keyword (fn, fun, Contract, struct, enum, constructor, var, let).</summary>
        public string? Keyword { get; set; }

        /// <summary>Modifier keywords (public, private, static, export, ...).</summary>
        public List<string> Modifiers { get; set; } = new();

        /// <summary>Parameters parsed from the declaration, in declaration order.</summary>
        public List<ParamInfo> Parameters { get; set; } = new();

        /// <summary>Whether the declaration carries a parenthesised parameter list.</summary>
        public bool HasParenList { get; set; }

        /// <summary>
        /// The declared return type (functions), or the declared type (fields).
        /// Null when absent.
        /// </summary>
        public string? ReturnType { get; set; }

        /// <summary>Generic type parameters (e.g. "T", "T, E").</summary>
        public List<string> TypeParameters { get; set; } = new();

        /// <summary>Base type name (for contracts: <c>Contract Dog : Animal</c>).</summary>
        public string? BaseType { get; set; }

        /// <summary>Interface type names (for contracts).</summary>
        public List<string> InterfaceTypes { get; set; } = new();

        /// <summary>Attributes on this declaration.</summary>
        public List<string> Attributes { get; set; } = new();

        /// <summary>Child members (for contracts/structs with nested functions/fields).</summary>
        public List<DocBlock> Children { get; set; } = new();

        /// <summary>Parent declaration name (for nested members).</summary>
        public string? Parent { get; set; }
    }

    // ── AST-based extraction (primary path) ───────────────────────

    /// <summary>
    /// Extracts documentation from a parsed AST and raw source text.
    /// Walks the AST for typed declaration info and scans raw source
    /// backwards from each declaration line for <c>///</c> doc comments.
    /// </summary>
    public static List<DocBlock> ExtractFromAst(Program program, string source, string? sourceFile = null)
    {
        var lines = source.Split('\n');
        var results = new List<DocBlock>();

        foreach (var contract in program.Contracts)
            results.Add(FromContract(contract, lines, sourceFile));

        foreach (var structDecl in program.Structs)
            results.Add(FromStruct(structDecl, lines, sourceFile));

        foreach (var enumDecl in program.Enums)
            results.Add(FromEnum(enumDecl, lines, sourceFile));

        foreach (var func in program.Functions)
            results.Add(FromFunction(func, null, lines, sourceFile));

        return results;
    }

    private static DocBlock FromContract(ContractDeclaration node, string[] lines, string? sourceFile)
    {
        var doc = new DocBlock
        {
            Name = node.Name,
            Kind = "contract",
            Line = node.Line,
            SourceFile = sourceFile,
            Namespace = node.Namespace,
            BaseType = node.BaseTypeName,
            InterfaceTypes = new List<string>(node.InterfaceNames),
            TypeParameters = new List<string>(node.TypeParameters),
            HasParenList = false,
            Signature = ReconstructContractSignature(node),
        };
        doc.Keyword = "Contract";
        doc.Modifiers.Add("public"); // contracts are always public
        if (node.IsExported) doc.Modifiers.Insert(0, "export");

        ExtractDocComment(lines, node.Line, doc);
        ExtractAttributes(node.Attributes, doc);

        // Constructors
        foreach (var ctor in node.Constructors)
        {
            var child = new DocBlock
            {
                Name = "constructor",
                Kind = "constructor",
                Line = ctor.Line,
                SourceFile = sourceFile,
                Namespace = node.Namespace,
                Parent = node.Name,
                HasParenList = true,
                Parameters = ctor.Parameters.Select(p => new ParamInfo { Name = p.Name, Type = FormatType(p.Type) }).ToList(),
                Signature = ReconstructCtorSignature(ctor),
            };
            child.Keyword = "constructor";
            ExtractDocComment(lines, ctor.Line, child);
            ExtractAttributes(ctor.Attributes, child);
            doc.Children.Add(child);
        }

        // Fields
        foreach (var field in node.Fields)
        {
            var child = new DocBlock
            {
                Name = field.Name,
                Kind = "field",
                Line = field.Line,
                SourceFile = sourceFile,
                Namespace = node.Namespace,
                Parent = node.Name,
                ReturnType = FormatType(field.Type),
                Signature = ReconstructFieldSignature(field),
            };
            child.Keyword = field.IsStatic ? "static" : "var";
            if (field.Access != AccessModifier.Private) child.Modifiers.Add("public");
            if (field.IsStatic) child.Modifiers.Add("static");
            if (field.IsConst) child.Modifiers.Add("const");
            ExtractDocComment(lines, field.Line, child);
            doc.Children.Add(child);
        }

        // Members (functions, nested contracts, nested structs, nested enums)
        foreach (var member in node.Members)
        {
            switch (member)
            {
                case FunctionDeclaration fn:
                    doc.Children.Add(FromFunction(fn, node.Name, lines, sourceFile));
                    break;
                case ContractDeclaration nested:
                    doc.Children.Add(FromContract(nested, lines, sourceFile));
                    break;
                case StructDeclaration nestedStruct:
                    doc.Children.Add(FromStruct(nestedStruct, lines, sourceFile));
                    break;
                case EnumDeclaration nestedEnum:
                    doc.Children.Add(FromEnum(nestedEnum, lines, sourceFile));
                    break;
            }
        }

        return doc;
    }

    private static DocBlock FromStruct(StructDeclaration node, string[] lines, string? sourceFile)
    {
        var doc = new DocBlock
        {
            Name = node.Name,
            Kind = "struct",
            Line = node.Line,
            SourceFile = sourceFile,
            Namespace = node.Namespace,
            HasParenList = false,
            Signature = ReconstructStructSignature(node),
        };
        doc.Keyword = "struct";
        if (node.IsExported) doc.Modifiers.Add("export");

        ExtractDocComment(lines, node.Line, doc);
        ExtractAttributes(node.Attributes, doc);

        foreach (var field in node.Fields)
        {
            var child = new DocBlock
            {
                Name = field.Name,
                Kind = "field",
                Line = field.Line,
                SourceFile = sourceFile,
                Namespace = node.Namespace,
                Parent = node.Name,
                ReturnType = FormatType(field.Type),
                Signature = ReconstructFieldSignature(field),
            };
            child.Keyword = field.IsStatic ? "static" : "var";
            if (field.Access != AccessModifier.Private) child.Modifiers.Add("public");
            if (field.IsStatic) child.Modifiers.Add("static");
            if (field.IsConst) child.Modifiers.Add("const");
            ExtractDocComment(lines, field.Line, child);
            doc.Children.Add(child);
        }

        foreach (var method in node.Methods)
            doc.Children.Add(FromFunction(method, node.Name, lines, sourceFile));

        return doc;
    }

    private static DocBlock FromEnum(EnumDeclaration node, string[] lines, string? sourceFile)
    {
        var doc = new DocBlock
        {
            Name = node.Name,
            Kind = "enum",
            Line = node.Line,
            SourceFile = sourceFile,
            Namespace = node.Namespace,
            HasParenList = false,
            Signature = ReconstructEnumSignature(node),
        };
        doc.Keyword = "enum";
        if (node.IsExported) doc.Modifiers.Add("export");

        ExtractDocComment(lines, node.Line, doc);
        ExtractAttributes(node.Attributes, doc);

        return doc;
    }

    private static DocBlock FromFunction(FunctionDeclaration node, string? parentContract, string[] lines, string? sourceFile)
    {
        var kind = node.IsExtension ? "extension" : "function";
        var doc = new DocBlock
        {
            Name = node.Name,
            Kind = kind,
            Line = node.Line,
            SourceFile = sourceFile,
            Namespace = node.ContractName != null ? null : null, // set by caller
            Parent = parentContract,
            HasParenList = true,
            Parameters = node.Parameters.Select(p => new ParamInfo { Name = p.Name, Type = FormatType(p.Type) }).ToList(),
            ReturnType = node.ReturnType != null ? FormatType(node.ReturnType) : null,
            TypeParameters = new List<string>(node.TypeParameters),
            Signature = ReconstructFnSignature(node),
        };

        doc.Keyword = "fn";
        switch (node.Access)
        {
            case AccessModifier.Public: doc.Modifiers.Add("public"); break;
            case AccessModifier.Private: doc.Modifiers.Add("private"); break;
        }
        if (node.IsStatic) doc.Modifiers.Add("static");

        ExtractDocComment(lines, node.Line, doc);
        ExtractAttributes(node.Attributes, doc);

        return doc;
    }

    // ── Doc comment extraction from raw source ────────────────────

    /// <summary>
    /// Scans backwards from a declaration line to find the contiguous
    /// <c>///</c> block above it, then parses XML tags.
    /// </summary>
    private static void ExtractDocComment(string[] lines, int declLine, DocBlock doc)
    {
        // declLine is 1-based; lines array is 0-based
        int idx = declLine - 1;
        if (idx < 0 || idx >= lines.Length) return;

        // Scan backwards to find the first /// line above the declaration
        int end = idx - 1;
        while (end >= 0 && string.IsNullOrWhiteSpace(lines[end]))
            end--;
        if (end < 0) return;
        if (!lines[end].TrimStart().StartsWith("///")) return;

        int start = end;
        while (start > 0 && lines[start - 1].TrimStart().StartsWith("///"))
            start--;

        // Collect doc lines
        var docLines = new List<string>();
        for (int i = start; i <= end; i++)
        {
            string text = lines[i].TrimStart();
            if (text.StartsWith("///"))
                text = text.Substring(3).Trim();
            docLines.Add(text);
        }

        doc.RawDoc = string.Join("\n", docLines);
        ParseDocTags(doc);
    }

    /// <summary>
    /// Parses XML doc tags from the raw doc comment text.
    /// </summary>
    private static void ParseDocTags(DocBlock doc)
    {
        string raw = doc.RawDoc;
        if (string.IsNullOrWhiteSpace(raw)) return;

        // Extract <summary>
        var summaryMatch = Regex.Match(raw, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
        if (summaryMatch.Success)
            doc.Summary = summaryMatch.Groups[1].Value.Trim();

        // Extract <remarks> (accept <remark> as alias)
        var remarksMatch = Regex.Match(raw, @"<(?:remarks|remark)>\s*(.*?)\s*</(?:remarks|remark)>", RegexOptions.Singleline);
        if (remarksMatch.Success)
            doc.Remarks = remarksMatch.Groups[1].Value.Trim();

        // Extract <example>
        var exampleMatch = Regex.Match(raw, @"<example>\s*(.*?)\s*</example>", RegexOptions.Singleline);
        if (exampleMatch.Success)
            doc.Example = exampleMatch.Groups[1].Value.Trim();

        // Extract <returns>
        var returnsMatch = Regex.Match(raw, @"<returns>\s*(.*?)\s*</returns>", RegexOptions.Singleline);
        if (returnsMatch.Success)
            doc.Returns = returnsMatch.Groups[1].Value.Trim();

        // Extract <param name="x"> tags
        var paramMatches = Regex.Matches(raw, @"<param\s+name=""(\w+)"">\s*(.*?)\s*</param>", RegexOptions.Singleline);
        foreach (Match m in paramMatches)
            doc.Params[m.Groups[1].Value] = m.Groups[2].Value.Trim();

        // If no <summary> tag, use the first non-tag line as summary
        if (doc.Summary == null)
        {
            foreach (var docLine in raw.Split('\n'))
            {
                string trimmed = docLine.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("<"))
                {
                    doc.Summary = trimmed;
                    break;
                }
            }
        }
    }

    // ── Attribute extraction ──────────────────────────────────────

    private static void ExtractAttributes(List<AttributeUsage> attrs, DocBlock doc)
    {
        foreach (var attr in attrs)
        {
            string s = $"@{attr.Name}";
            if (attr.Arguments.Count > 0)
                s += $"({string.Join(", ", attr.Arguments)})";
            doc.Attributes.Add(s);
        }
    }

    // ── Signature reconstruction from AST ─────────────────────────

    private static string ReconstructContractSignature(ContractDeclaration node)
    {
        var sb = new StringBuilder();
        sb.Append("Contract ");
        sb.Append(node.Name);
        if (node.TypeParameters.Count > 0)
            sb.Append($"<{string.Join(", ", node.TypeParameters)}>");
        if (node.BaseTypeName != null || node.InterfaceNames.Count > 0)
        {
            sb.Append(" : ");
            var parents = new List<string>();
            if (node.BaseTypeName != null) parents.Add(node.BaseTypeName);
            parents.AddRange(node.InterfaceNames);
            sb.Append(string.Join(", ", parents));
        }
        return sb.ToString();
    }

    private static string ReconstructFnSignature(FunctionDeclaration node)
    {
        var sb = new StringBuilder();
        if (node.IsStatic) sb.Append("static ");
        sb.Append("fn ");
        sb.Append(node.Name);
        if (node.TypeParameters.Count > 0)
            sb.Append($"<{string.Join(", ", node.TypeParameters)}>");
        sb.Append('(');
        for (int i = 0; i < node.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{node.Parameters[i].Name}: {FormatType(node.Parameters[i].Type)}");
        }
        sb.Append(')');
        if (node.ReturnType != null)
            sb.Append($" -> {FormatType(node.ReturnType)}");
        return sb.ToString();
    }

    private static string ReconstructCtorSignature(ConstructorDeclaration node)
    {
        var sb = new StringBuilder();
        sb.Append("constructor(");
        for (int i = 0; i < node.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{node.Parameters[i].Name}: {FormatType(node.Parameters[i].Type)}");
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string ReconstructStructSignature(StructDeclaration node)
    {
        var sb = new StringBuilder();
        sb.Append("struct ");
        sb.Append(node.Name);
        return sb.ToString();
    }

    private static string ReconstructEnumSignature(EnumDeclaration node)
    {
        var sb = new StringBuilder();
        sb.Append("enum ");
        sb.Append(node.Name);
        if (node.Members.Count > 0)
        {
            sb.Append(" { ");
            sb.Append(string.Join(", ", node.Members));
            sb.Append(" }");
        }
        return sb.ToString();
    }

    private static string ReconstructFieldSignature(StructField node)
    {
        var sb = new StringBuilder();
        if (node.IsStatic) sb.Append("static ");
        if (node.IsConst) sb.Append("const ");
        sb.Append($"{node.Name}: {FormatType(node.Type)}");
        return sb.ToString();
    }

    // ── Type formatting ───────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="TypeDescriptor"/> to a human-readable string.
    /// </summary>
    public static string FormatType(TypeDescriptor? type)
    {
        if (type == null || type == TypeDescriptor.Empty) return "";
        return type switch
        {
            TypeDescriptor.Named named => named.Name,
            TypeDescriptor.ArrayOf arr => $"{FormatType(arr.Element)}[]",
            TypeDescriptor.GenericInstance gen => $"{gen.Name}<{string.Join(", ", gen.Arguments.Select(FormatType))}>",
            TypeDescriptor.Function fn => $"({string.Join(", ", fn.Parameters.Select(FormatType))}) -> {FormatType(fn.Return)}",
            TypeDescriptor.Tuple tup => $"({string.Join(", ", tup.Elements.Select(FormatType))})",
            _ => type.ToString() ?? ""
        };
    }

    // ── Legacy regex-based extraction (fallback) ──────────────────

    /// <summary>
    /// Extracts all documentation blocks from raw source text (no AST).
    /// Kept as fallback when parsing fails.
    /// </summary>
    public static List<DocBlock> ExtractFromSource(string source, string? sourceFile = null)
    {
        var results = new List<DocBlock>();
        var lines = source.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("///")) continue;

            var docLines = new List<string>();
            int docStart = i;
            while (i < lines.Length && lines[i].Trim().StartsWith("///"))
            {
                string text = lines[i].Trim().Substring(3).Trim();
                docLines.Add(text);
                i++;
            }

            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                i++;

            if (i >= lines.Length) break;

            string declLine = lines[i].Trim();
            if (string.IsNullOrEmpty(declLine)) continue;

            var (name, kind) = ParseDeclaration(declLine);
            if (string.IsNullOrEmpty(name)) continue;

            var doc = new DocBlock
            {
                Name = name,
                Kind = kind,
                Line = i + 1,
                SourceFile = sourceFile,
                RawDoc = string.Join("\n", docLines),
                Signature = declLine,
            };
            ParseDocTags(doc);
            ParseSignature(doc, declLine);
            results.Add(doc);
        }

        return results;
    }

    private static (string? name, string kind) ParseDeclaration(string line)
    {
        const string Mods = @"(?:public\s+|private\s+|protected\s+|internal\s+|static\s+|export\s+)*";

        var contractMatch = Regex.Match(line, @"^\s*" + Mods + @"[Cc]ontract\s+(\w+)");
        if (contractMatch.Success) return (contractMatch.Groups[1].Value, "contract");

        var structMatch = Regex.Match(line, @"^\s*" + Mods + @"struct\s+(\w+)");
        if (structMatch.Success) return (structMatch.Groups[1].Value, "struct");

        var enumMatch = Regex.Match(line, @"^\s*" + Mods + @"enum\s+(\w+)");
        if (enumMatch.Success) return (enumMatch.Groups[1].Value, "enum");

        var fnMatch = Regex.Match(line, @"^\s*" + Mods + @"(?:fn|fun)\s+(\w+)");
        if (fnMatch.Success) return (fnMatch.Groups[1].Value, "function");

        var ctorMatch = Regex.Match(line, @"^\s*" + Mods + @"constructor\s*\(");
        if (ctorMatch.Success) return ("constructor", "constructor");

        var fnValueMatch = Regex.Match(line, @"^\s*(?:let|var)\s+(\w+)(?:\s*:\s*[\w<>,\.\[\]\?]+)?\s*=\s*(?:fn|fun)\s*\(");
        if (fnValueMatch.Success) return (fnValueMatch.Groups[1].Value, "function");

        var fieldMatch = Regex.Match(line, @"^\s*" + Mods + @"(\w+)\s*:\s*[A-Za-z_][\w<>,\.\[\]\?]*");
        if (fieldMatch.Success) return (fieldMatch.Groups[1].Value, "field");

        var letMatch = Regex.Match(line, @"^\s*(?:let|var)\s+(\w+)\s*=");
        if (letMatch.Success) return (letMatch.Groups[1].Value, "field");

        return (null, "");
    }

    private static readonly string[] KnownModifiers =
        { "public", "private", "protected", "internal", "static", "export" };

    public static void ParseSignature(DocBlock doc, string line)
    {
        int cut = line.IndexOfAny(new[] { '{', '}', ';' });
        string s = (cut >= 0 ? line[..cut] : line).Trim();
        if (s.Length == 0) return;

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

        var kw = Regex.Match(s, @"^(fn|fun|contract|struct|enum|constructor|var|let)\b", RegexOptions.IgnoreCase);
        if (!kw.Success)
        {
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
