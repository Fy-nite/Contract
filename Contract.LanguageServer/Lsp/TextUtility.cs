using System;
using System.Collections.Generic;
using System.IO;
using Contract.Compiler.Parsing;

namespace Contract.LanguageServer.Lsp;

/// <summary>Helpers for converting between the compiler's coordinate system and LSP's.</summary>
public static class TextUtility
{
    /// <summary>Converts a local path (or raw uri) to a file:// URI string.</summary>
    public static string PathToUri(string path)
    {
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return path;
        if (path.Contains("://")) return path; // untitled:, etc. — pass through
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>Converts a file:// URI to a local path, or null for non-file URIs.</summary>
    public static string? UriToPath(string uri)
    {
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return null;
        try { return new Uri(uri).LocalPath; }
        catch (UriFormatException) { return null; }
    }

    /// <summary>Normalizes a filesystem path for use as a dictionary key (case-insensitive on Windows).</summary>
    public static string NormalizePath(string path)
    {
        string full = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    /// <summary>Finds the token whose span contains the given 1-based compiler position.</summary>
    public static Token? FindTokenAt(IReadOnlyList<Token> tokens, int line, int column)
    {
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.EOF) continue;
            if (t.Line != line) continue;
            if (t.Column <= column && column < t.EndColumn) return t;
        }
        return null;
    }

    /// <summary>Finds the token whose span contains the given 0-based LSP position.</summary>
    public static Token? FindTokenAt(IReadOnlyList<Token> tokens, Position pos)
        => FindTokenAt(tokens, pos.Line + 1, pos.Character + 1);

    /// <summary>The last token that starts at or before the given 0-based LSP position.</summary>
    public static Token? TokenBefore(IReadOnlyList<Token> tokens, Position pos)
    {
        Token? best = null;
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.EOF) continue;
            if (!IsAtOrBeforeStart(t, pos)) break; // tokens are ordered; past the cursor
            best = t;
        }
        return best;
    }

    /// <summary>True when the token's start (1-based line/column) is at or before the 0-based LSP position.</summary>
    public static bool IsAtOrBeforeStart(Token t, Position pos)
        => t.Line < pos.Line + 1 || (t.Line == pos.Line + 1 && t.Column <= pos.Character + 1);

    /// <summary>Builds the LSP range for a token (start..end, exclusive end).</summary>
    public static Range TokenRange(Token t)
        => new(new Position(t.Line - 1, t.Column - 1), new Position(t.Line - 1, t.Column - 1 + t.Length));

    /// <summary>
    /// Builds a diagnostic range. Uses the token at the diagnostic's position when one
    /// exists (precise underline); otherwise falls back to a single character clamped
    /// to the line length.
    /// </summary>
    public static Range DiagnosticRange(Contract.Compiler.Diagnostics.Diagnostic d, IReadOnlyList<Token>? tokens, string source)
    {
        int line = Math.Clamp(d.Line - 1, 0, int.MaxValue);
        int column = Math.Max(0, d.Column - 1);

        var token = tokens != null ? FindTokenAt(tokens, d.Line, d.Column) : null;
        if (token != null)
            return TokenRange(token);

        string lineText = GetLine(source, d.Line);
        int end = Math.Min(lineText.Length, column + 1);
        return new Range(new Position(line, column), new Position(line, end));
    }

    public static string GetLine(string source, int oneBasedLine)
    {
        if (oneBasedLine <= 0) return "";
        int start = 0;
        int line = 1;
        while (line < oneBasedLine && start < source.Length)
        {
            int nl = source.IndexOf('\n', start);
            if (nl < 0) return "";
            start = nl + 1;
            line++;
        }
        int end = source.IndexOf('\n', start);
        if (end < 0) end = source.Length;
        string s = source[start..end];
        return s.TrimEnd('\r');
    }

    /// <summary>
    /// Collects contiguous `///` doc-comment lines immediately above the given
    /// 1-based line and joins them. Returns null when there is no doc comment.
    /// </summary>
    public static string? ExtractDocComment(string source, int oneBasedLine)
    {
        if (oneBasedLine <= 1) return null;
        var lines = source.Split('\n');
        var docs = new List<string>();
        for (int i = oneBasedLine - 2; i >= 0; i--)
        {
            string trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("///")) break;
            docs.Insert(0, trimmed.Substring(3).Trim());
        }
        return docs.Count > 0 ? string.Join("\n", docs) : null;
    }
}
