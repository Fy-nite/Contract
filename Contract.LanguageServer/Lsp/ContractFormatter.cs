using System;
using System.Collections.Generic;
using System.Text;
using Contract.Compiler.Parsing;

namespace Contract.LanguageServer.Lsp;

/// <summary>
/// Token-based code formatter for the Contract language. Normalizes indentation
/// (one indent unit per bracket level), spacing around operators/commas/colons,
/// blank line counts, and trailing whitespace. Operates on the token stream with
/// comments re-attached from the raw source — no AST needed.
/// </summary>
public static class ContractFormatter
{
    /// <summary>One renderable piece of a line: a token or a preserved comment.</summary>
    private readonly record struct Item(int Line, string Text, TokenType Type, bool IsComment)
    {
        public bool IsOpener => !IsComment && Type is TokenType.LBrace or TokenType.LParen or TokenType.LBracket;
        public bool IsCloser => !IsComment && Type is TokenType.RBrace or TokenType.RParen or TokenType.RBracket;
    }

    public static string Format(string source, IReadOnlyList<Token> tokens, FormattingOptions options)
    {
        string indentUnit = options.InsertSpaces
            ? new string(' ', Math.Max(1, options.TabSize))
            : "\t";

        var items = MergeItems(source, tokens);
        if (items.Count == 0) return string.Empty;

        var generic = MarkGenericSpans(items);

        var sb = new StringBuilder(source.Length);
        int depth = 0;
        bool lineStart = true;
        bool firstItem = true;
        int prevLine = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // ── Newlines ──────────────────────────────────────────────────
            if (item.Line > prevLine)
            {
                if (!firstItem)
                {
                    sb.AppendLine();
                    if (item.Line - prevLine > 1) sb.AppendLine(); // keep at most one blank line
                }
                lineStart = true;
                prevLine = item.Line;
            }

            // ── Indentation ───────────────────────────────────────────────
            if (lineStart)
            {
                // A closing bracket sits at its enclosing level.
                int effectiveDepth = item.IsCloser ? depth - 1 : depth;
                if (effectiveDepth < 0) effectiveDepth = 0;
                for (int s = 0; s < effectiveDepth; s++) sb.Append(indentUnit);
                lineStart = false;
            }
            else if (item.IsComment)
            {
                sb.Append("  ");
            }
            else
            {
                int gap = ComputeGap(items, i, generic);
                for (int s = 0; s < gap; s++) sb.Append(' ');
            }

            sb.Append(item.Text);

            if (item.IsOpener) depth++;
            else if (item.IsCloser && depth > 0) depth--;

            firstItem = false;
        }

        if (sb.Length == 0 || sb[sb.Length - 1] != '\n') sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Merges the token stream with comments scanned from the raw source
    /// (the lexer drops them). Each comment is placed after the last token
    /// of its original line, so own-line and trailing comments both survive.
    /// </summary>
    private static List<Item> MergeItems(string source, IReadOnlyList<Token> tokens)
    {
        var comments = ExtractComments(source);
        var items = new List<Item>(tokens.Count + comments.Count);
        int ci = 0;

        for (int ti = 0; ti < tokens.Count; ti++)
        {
            var tok = tokens[ti];
            if (tok.Type == TokenType.EOF) break;

            while (ci < comments.Count && comments[ci].Line < tok.Line)
            {
                items.Add(new Item(comments[ci].Line, comments[ci].Text, TokenType.EOF, IsComment: true));
                ci++;
            }

            items.Add(new Item(tok.Line, tok.Text, tok.Type, IsComment: false));
        }

        while (ci < comments.Count)
        {
            items.Add(new Item(comments[ci].Line, comments[ci].Text, TokenType.EOF, IsComment: true));
            ci++;
        }

        return items;
    }

    /// <summary>
    /// Scans `//` comments out of the source, skipping string literals so
    /// URLs and other "//" occurrences inside strings are not mistaken for
    /// comments.
    /// </summary>
    private static List<(int Line, string Text)> ExtractComments(string source)
    {
        var comments = new List<(int Line, string Text)>();
        int line = 1;
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '"')
            {
                i++;
                while (i < source.Length && source[i] != '"')
                {
                    if (source[i] == '\\' && i + 1 < source.Length) i++;
                    else if (source[i] == '\n') line++;
                    i++;
                }
                i++; // closing quote
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int start = i;
                while (i < source.Length && source[i] != '\n') i++;
                comments.Add((line, source.Substring(start, i - start).TrimEnd()));
            }
            else
            {
                if (c == '\n') line++;
                i++;
            }
        }

        return comments;
    }

    /// <summary>
    /// Marks angle brackets that delimit generic type arguments (List&lt;int&gt;,
    /// Dict&lt;string, int&gt;) and attribute applications (&lt;Author("bob")&gt;)
    /// so they render without spaces. A `&lt;` opens a generic span when it follows
    /// an identifier and everything up to the matching `&gt;` is type-shaped;
    /// anything else is a comparison. A `&lt;` in value position that is followed
    /// by Name( is an attribute.
    /// </summary>
    private static bool[] MarkGenericSpans(IReadOnlyList<Item> items)
    {
        var generic = new bool[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Type != TokenType.Less) continue;

            // Generic type arguments: Identifier<...>
            if (i > 0 && items[i - 1].Type == TokenType.Identifier)
            {
                int close = FindMatchingGreater(items, i, requireTypeContents: true);
                if (close > i + 1)
                {
                    for (int j = i; j <= close; j++) generic[j] = true;
                    continue;
                }
            }

            // Attributes: <Name(...)> appearing where a value cannot (own line).
            bool prevIsValue = i > 0 && items[i - 1].Type is TokenType.Identifier or TokenType.IntLiteral
                or TokenType.FloatLiteral or TokenType.StringLiteral or TokenType.InterpolatedString
                or TokenType.RParen or TokenType.RBracket
                or TokenType.True or TokenType.False or TokenType.Null;
            if (!prevIsValue && i + 2 < items.Count
                && items[i + 1].Type == TokenType.Identifier && items[i + 2].Type == TokenType.LParen)
            {
                int close = FindMatchingGreater(items, i, requireTypeContents: false);
                if (close != -1)
                    for (int j = i; j <= close; j++) generic[j] = true;
            }
        }

        return generic;
    }

    /// <summary>Finds the Greater closing the span opened at `open`, or -1.</summary>
    private static int FindMatchingGreater(IReadOnlyList<Item> items, int open, bool requireTypeContents)
    {
        int angleDepth = 0;
        int parenDepth = 0;

        for (int j = open + 1; j < items.Count; j++)
        {
            var t = items[j].Type;

            if (requireTypeContents)
            {
                bool typeish = t is TokenType.Identifier or TokenType.Dot or TokenType.Comma
                    or TokenType.LBracket or TokenType.RBracket or TokenType.Question
                    or TokenType.Less or TokenType.Greater or TokenType.GreaterGreater;
                if (!typeish) return -1;
            }
            else if (t == TokenType.Semicolon || t == TokenType.LBrace || t == TokenType.RBrace)
                return -1;

            if (t == TokenType.LParen) parenDepth++;
            else if (t == TokenType.RParen) { if (parenDepth > 0) parenDepth--; }
            else if (t == TokenType.Less) angleDepth++;
            else if (t == TokenType.GreaterGreater)
            {
                if (angleDepth <= 1 && parenDepth == 0) return j;
                angleDepth -= 2;
            }
            else if (t == TokenType.Greater && angleDepth == 0 && parenDepth == 0) return j;
            else if (t == TokenType.Greater && angleDepth > 0) angleDepth--;
        }

        return -1;
    }

    private static int ComputeGap(IReadOnlyList<Item> items, int index, bool[] generic)
    {
        var prev = items[index - 1];
        var cur = items[index];

        if (cur.Type == TokenType.Semicolon) return 0;
        if (cur.Type == TokenType.Colon) return 0;              // name: Type, case X:
        if (cur.Type == TokenType.Comma) return 0;              // a, b
        if (prev.Type == TokenType.Comma) return 1;
        if (cur.Type is TokenType.RParen or TokenType.RBracket) return 0;   // f(x), arr[i]
        if (prev.Type == TokenType.LBrace && cur.Type == TokenType.RBrace) return 0;  // {}
        if (prev.Type is TokenType.Dot or TokenType.DoubleColon) return 0;
        if (cur.Type is TokenType.Dot or TokenType.DoubleColon) return 0;

        // Calls and declarations attach to their name: foo(...), constructor(...).
        if (cur.Type == TokenType.LParen &&
            prev.Type is TokenType.Identifier or TokenType.Constructor or TokenType.Fun)
            return 0;

        // Indexing attaches to its target: arr[i], f(x)[0], m[0][1]. Array
        // literals after keywords/operators keep their space: return [...].
        if (cur.Type == TokenType.LBracket &&
            prev.Type is TokenType.Identifier or TokenType.RParen or TokenType.RBracket)
            return 0;

        // Tokens attaching backwards onto open brackets and prefix operators:
        // (x, [x, !x, -x in prefix position.
        if (prev.Type == TokenType.LParen || prev.Type == TokenType.LBracket) return 0;
        if (prev.Type == TokenType.Bang) return 0;
        if (prev.Type is TokenType.Minus or TokenType.Plus)
        {
            // A value before the sign means it was binary (a - b); otherwise prefix (-x).
            bool binarySign = index >= 2 && items[index - 2].Type is TokenType.Identifier
                or TokenType.IntLiteral or TokenType.FloatLiteral or TokenType.StringLiteral
                or TokenType.InterpolatedString or TokenType.RParen or TokenType.RBracket
                or TokenType.True or TokenType.False or TokenType.Null;
            return binarySign ? 1 : 0;
        }

        // Generic type arguments stay tight: Dict<string, int>.
        if (!cur.IsComment && cur.Type is TokenType.Less or TokenType.Greater or TokenType.GreaterGreater
            && index < generic.Length && generic[index])
            return 0;
        if (prev.Type is TokenType.Less or TokenType.Greater or TokenType.GreaterGreater
            && index > 0 && generic[index - 1])
            return 0;

        if (cur.Type == TokenType.LBrace) return 1;
        if (prev.Type == TokenType.RBrace && cur.Type == TokenType.Else) return 1;

        if (IsOperator(prev.Type) || IsOperator(cur.Type)) return 1;

        return 1;
    }

    private static bool IsOperator(TokenType t)
        => t is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash
            or TokenType.Percent or TokenType.EqualEqual or TokenType.BangEqual
            or TokenType.Less or TokenType.LessEqual or TokenType.Greater
            or TokenType.GreaterEqual or TokenType.Assign or TokenType.Arrow
            or TokenType.AndAnd or TokenType.OrOr or TokenType.PlusEqual
            or TokenType.MinusEqual or TokenType.StarEqual or TokenType.SlashEqual
            or TokenType.PercentEqual or TokenType.DotDot or TokenType.Pipe;
}
