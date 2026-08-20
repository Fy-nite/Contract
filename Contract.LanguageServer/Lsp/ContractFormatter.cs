using System.Collections.Generic;
using System.Text;
using Contract.Compiler.Parsing;

namespace Contract.LanguageServer.Lsp;

/// <summary>
/// Token-based code formatter for the Contract language. Normalizes indentation
/// (2 spaces per brace level), spacing around operators/commas/semicolons,
/// blank line counts, and trailing whitespace. Operates directly on the token
/// stream — no AST needed.
/// </summary>
public static class ContractFormatter
{
    public static string Format(string source, IReadOnlyList<Token> tokens, FormattingOptions options)
    {
        string indent = options.InsertSpaces
            ? new string(' ', options.TabSize)
            : "\t";

        var sb = new StringBuilder(source.Length);
        int depth = 0;
        bool lineStart = true;
        int prevLine = 0;
        int blankRun = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok.Type == TokenType.EOF) break;

            // ── Newlines ──────────────────────────────────────────────────
            if (tok.Line > prevLine)
            {
                int gaps = tok.Line - prevLine;
                bool wasPrevBraceClose = i > 0
                    && tokens[i - 1].Type == TokenType.RBrace;

                if (gaps > 1 || (gaps == 1 && wasPrevBraceClose && !IsBraceOpen(tok.Type)))
                {
                    blankRun++;
                    if (blankRun <= 1) sb.AppendLine();
                }
                else
                {
                    blankRun = 0;
                    sb.AppendLine();
                }

                lineStart = true;
                prevLine = tok.Line;
            }

            // ── Indentation ───────────────────────────────────────────────
            if (lineStart)
            {
                for (int s = 0; s < depth; s++) sb.Append(indent);
                lineStart = false;
            }
            else
            {
                int gap = ComputeGap(tokens, i, depth);
                for (int s = 0; s < gap; s++) sb.Append(' ');
            }

            sb.Append(tok.Text);
        }

        return sb.ToString();
    }

    private static int ComputeGap(IReadOnlyList<Token> tokens, int index, int depth)
    {
        var prev = tokens[index - 1];
        var cur = tokens[index];

        if (cur.Type == TokenType.Semicolon) return 0;
        if (prev.Type == TokenType.Semicolon && cur.Type == TokenType.RBrace) return 0;
        if (prev.Type == TokenType.DoubleColon) return 0;
        if (prev.Type is TokenType.Dot or TokenType.DoubleColon) return 0;
        if (cur.Type is TokenType.Dot or TokenType.DoubleColon) return 0;

        if (prev.Type == TokenType.Comma) return 1;

        if (cur.Type == TokenType.LBrace) return 1;
        if (prev.Type == TokenType.RBrace && cur.Type == TokenType.Else) return 1;

        if (IsOperator(prev.Type) || IsOperator(cur.Type))
        {
            if (prev.Type is TokenType.Less or TokenType.LessEqual
                && cur.Type is TokenType.Less or TokenType.LessEqual
                && index >= 2 && tokens[index - 2].Type == TokenType.Identifier)
                return 1; // generic: List<int>
            return 1;
        }

        return 1;
    }

    private static bool IsBraceOpen(TokenType t)
        => t is TokenType.LBrace or TokenType.LBracket or TokenType.LParen;

    private static bool IsOperator(TokenType t)
        => t is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash
            or TokenType.Percent or TokenType.EqualEqual or TokenType.BangEqual
            or TokenType.Less or TokenType.LessEqual or TokenType.Greater
            or TokenType.GreaterEqual or TokenType.Assign or TokenType.Arrow
            or TokenType.AndAnd or TokenType.OrOr or TokenType.PlusEqual
            or TokenType.MinusEqual or TokenType.StarEqual or TokenType.SlashEqual
            or TokenType.PercentEqual or TokenType.DotDot or TokenType.Pipe;
}
