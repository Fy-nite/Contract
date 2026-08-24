using System;
using System.Collections.Generic;
using Contract.Compiler.AST;
using Contract.Compiler.Parsing;

namespace Contract.Compiler.Expressions.Internal
{
    /// <summary>
    /// Pipe (<c>|></c>) and composition (<c>>></c>) operators, plus
    /// implicit lambda wrapping (<c>_</c>/<c>@</c> marker discovery).
    /// </summary>
    internal static class PipeComposition
    {
        public static bool IsImplicitMarker(string name)
            => name == "_" || name == "@";

        /// <summary>Finds the first free <c>_</c>/<c>@</c> identifier in an expression tree.</summary>
        public static string? FindImplicitMarker(Expression expr)
        {
            switch (expr)
            {
                case IdentifierExpression id when IsImplicitMarker(id.Name):
                    return id.Name;
                case CallExpression c:
                    foreach (var a in c.Arguments)
                    {
                        var result = FindImplicitMarker(a);
                        if (result != null) return result;
                    }
                    return null;
                case MemberExpression m:
                    return FindImplicitMarker(m.Object);
                case BinaryExpression b:
                    return FindImplicitMarker(b.Left) ?? FindImplicitMarker(b.Right);
                case UnaryExpression u:
                    return FindImplicitMarker(u.Operand);
                case IndexExpression ix:
                    return FindImplicitMarker(ix.Target) ?? FindImplicitMarker(ix.Index);
                case PipeExpression p:
                    return FindImplicitMarker(p.Left) ?? FindImplicitMarker(p.Right);
                case ArrayLiteralExpression al:
                    foreach (var e in al.Elements)
                    {
                        var result = FindImplicitMarker(e);
                        if (result != null) return result;
                    }
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Wraps an expression containing a free <c>_</c>/<c>@</c> into an implicit
        /// lambda: <c>_ * 2</c> becomes <c>fun _ -> _ * 2</c>. No-op for lambdas
        /// and for expressions without a marker.
        /// </summary>
        public static Expression ApplyImplicitLambda(ParserContext ctx, Expression expr, int line, int column)
        {
            if (expr is LambdaExpression) return expr;

            var marker = FindImplicitMarker(expr);
            if (marker == null) return expr;

            return new LambdaExpression(
                new List<string> { marker },
                null,
                expr,
                null,
                line,
                column);
        }

        /// <summary>f >> g — composition. Lowers to <c>fun x -> g(f(x))</c>.</summary>
        public static Expression Compose(
            ParserContext ctx, IParserHost host,
            Expression left, Expression right,
            int line, int column)
        {
            if (left is LambdaExpression || right is LambdaExpression)
            {
                ctx.AddError("Composition operands must be named functions (no lambdas in v1)", line, column);
                return new PipeExpression(left, right, line, column);
            }

            var x = new IdentifierExpression("__compose_arg", line, column);
            var inner = new CallExpression(left, line, column);
            inner.Arguments.Add(x);

            Expression body;
            if (right is CallExpression rightCall)
            {
                rightCall.Arguments.Insert(0, inner);
                body = rightCall;
            }
            else
            {
                var outer = new CallExpression(right, line, column);
                outer.Arguments.Add(inner);
                body = outer;
            }

            return new LambdaExpression(
                new List<string> { "__compose_arg" },
                null,
                body,
                null,
                line,
                column);
        }

        /// <summary>
        /// <c>left |> right</c> — rewrites the RHS so the piped value lands where
        /// the user expects.
        /// </summary>
        public static Expression BuildPipe(
            ParserContext ctx, IParserHost host,
            Expression left, Expression right,
            int line, int column)
        {
            if (right is LambdaExpression)
                return new PipeExpression(left, right, line, column);

            switch (right)
            {
                case CallExpression call:
                {
                    int hole = -1;
                    int i = 0;
                    while (i < call.Arguments.Count)
                    {
                        var arg = call.Arguments[i];
                        if (arg is IdentifierExpression holeId && IsImplicitMarker(holeId.Name))
                        {
                            if (hole >= 0)
                                ctx.AddError("A pipe target can only use '_' as the value's spot once", arg.Line, arg.Column);
                            else
                                hole = i;
                            call.Arguments.RemoveAt(i);
                        }
                        else
                        {
                            call.Arguments[i] = ApplyImplicitLambda(ctx, arg, arg.Line, arg.Column);
                            i++;
                        }
                    }
                    call.Arguments.Insert(hole >= 0 ? hole : 0, left);
                    return call;
                }
                case IdentifierExpression:
                case MemberExpression:
                case ScopedAccessExpression:
                {
                    var call2 = new CallExpression(right, line, column);
                    call2.Arguments.Add(left);
                    return call2;
                }
                default:
                    return new PipeExpression(left, ApplyImplicitLambda(ctx, right, line, column), line, column);
            }
        }
    }
}
