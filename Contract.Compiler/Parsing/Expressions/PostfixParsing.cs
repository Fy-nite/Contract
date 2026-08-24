using System;
using System.Collections.Generic;
using System.Text;
using Contract.Compiler.AST;
using Contract.Compiler.Parsing;

namespace Contract.Compiler.Expressions.Internal
{
    /// <summary>
    /// Helpers used by <c>ParsePostfix</c>: generic type-argument lookahead and
    /// dotted-path extraction.
    /// </summary>
    internal static class PostfixHelpers
    {
        /// <summary>
        /// Collects a dotted identifier path from an expression: IdentifierExpression("A")
        /// yields "A"; A.B.C (a left-leaning chain of MemberExpressions over identifiers)
        /// yields "A.B.C". Returns null for anything else.
        /// </summary>
        public static string? TryGetDottedPath(Expression expr)
        {
            var segments = new Stack<string>();
            var current = expr;
            while (current is MemberExpression mem)
            {
                segments.Push(mem.Property);
                current = mem.Object;
            }
            if (current is IdentifierExpression root)
            {
                segments.Push(root.Name);
                return string.Join(".", segments);
            }
            return null;
        }

        /// <summary>
        /// When the current token is <c>&lt;</c>, scans ahead for a balanced
        /// <c>&lt;...&gt;</c> of type-ish tokens immediately followed by <c>(</c> or
        /// <c>::</c> — the explicit type arguments of a generic call. Returns the
        /// parsed type arguments with the cursor left after the <c>&gt;</c>; returns
        /// null (restoring the cursor) when the lookahead doesn't match.
        /// </summary>
        public static List<TypeDescriptor>? TryLookaheadGenericCallArgs(ParserContext ctx)
        {
            if (!ctx.Check(TokenType.Less)) return null;

            int save = ctx.Cursor;
            ctx.Advance(); // consume '<'
            int depth = 1;
            var argTexts = new List<string>();
            var current = new StringBuilder();
            List<TypeDescriptor>? result = null;

            while (!ctx.IsAtEnd() && depth > 0 && result == null)
            {
                var tok = ctx.Current;
                switch (tok.Type)
                {
                    case TokenType.Less:
                        depth++;
                        current.Append('<');
                        ctx.Advance();
                        break;
                    case TokenType.Greater:
                        depth--;
                        if (depth == 0)
                        {
                            if (current.Length > 0) argTexts.Add(current.ToString());
                            ctx.Advance();
                            if (ctx.Check(TokenType.LParen) || ctx.Check(TokenType.DoubleColon))
                                result = new List<TypeDescriptor>(argTexts.ConvertAll(TypeDescriptor.Parse));
                            else
                            {
                                ctx.Cursor = save;
                                result = new List<TypeDescriptor>(); // sentinel for "no match"
                            }
                        }
                        else
                        {
                            current.Append('>');
                            ctx.Advance();
                        }
                        break;
                    case TokenType.GreaterGreater:
                        if (depth < 2)
                        {
                            ctx.Cursor = save;
                            result = new List<TypeDescriptor>();
                        }
                        else
                        {
                            depth -= 2;
                            current.Append('>');
                            ctx.Advance();
                            if (depth == 0)
                            {
                                if (current.Length > 0) argTexts.Add(current.ToString());
                                if (ctx.Check(TokenType.LParen) || ctx.Check(TokenType.DoubleColon))
                                    result = new List<TypeDescriptor>(argTexts.ConvertAll(TypeDescriptor.Parse));
                                else
                                {
                                    ctx.Cursor = save;
                                    result = new List<TypeDescriptor>();
                                }
                            }
                        }
                        break;
                    case TokenType.Arrow:
                        current.Append("->");
                        ctx.Advance();
                        break;
                    case TokenType.Comma:
                        if (depth == 1)
                        {
                            argTexts.Add(current.ToString());
                            current.Clear();
                        }
                        else
                        {
                            current.Append(", ");
                        }
                        ctx.Advance();
                        break;
                    default:
                        current.Append(tok.Text);
                        ctx.Advance();
                        break;
                }
            }

            // null means "no match, cursor restored"; empty list is the sentinel for "no match after advancing"
            if (result != null && result.Count == 0) return null;
            return result;
        }
    }
}
