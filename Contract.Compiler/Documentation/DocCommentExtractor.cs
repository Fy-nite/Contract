using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Contract.Compiler.Documentation;

/// <summary>
/// Extracts structured documentation from `///` XML doc comments in Contract source files.
/// Parses `&lt;summary&gt;`, `&lt;param&gt;`, `&lt;returns&gt;`, `&lt;remarks&gt;`, and `&lt;example&gt;` tags.
/// </summary>
public static class DocCommentExtractor
{
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

        // Extract <remarks>
        var remarksMatch = Regex.Match(rawDoc, @"<remarks>\s*(.*?)\s*</remarks>", RegexOptions.Singleline);
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
        // contract Name
        var contractMatch = Regex.Match(line, @"^\s*(?:public\s+|private\s+|internal\s+|export\s+)*contract\s+(\w+)");
        if (contractMatch.Success)
            return (contractMatch.Groups[1].Value, "contract");

        // struct Name
        var structMatch = Regex.Match(line, @"^\s*(?:public\s+|private\s+|internal\s+|export\s+)*struct\s+(\w+)");
        if (structMatch.Success)
            return (structMatch.Groups[1].Value, "struct");

        // enum Name
        var enumMatch = Regex.Match(line, @"^\s*(?:public\s+|private\s+|internal\s+|export\s+)*enum\s+(\w+)");
        if (enumMatch.Success)
            return (enumMatch.Groups[1].Value, "enum");

        // fn Name(...) or fun Name(...)
        var fnMatch = Regex.Match(line, @"^\s*(?:public\s+|private\s+|internal\s+|static\s+|export\s+)*(?:fn|fun)\s+(\w+)");
        if (fnMatch.Success)
            return (fnMatch.Groups[1].Value, "function");

        // Type Name (field declaration — starts with a type keyword)
        var fieldMatch = Regex.Match(line, @"^\s*(?:public\s+|private\s+|internal\s+|static\s+|let\s+|var\s+)+(\w+)\s+(\w+)");
        if (fieldMatch.Success)
            return (fieldMatch.Groups[2].Value, "field");

        return (null, "");
    }
}
