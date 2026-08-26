using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Expressions.Internal;
using Contract.Compiler.Parsing;

namespace Contract.Compiler.Expressions
{
    /// <summary>
    /// C# expression parser — a direct port of the recursive-descent
    /// expression parsing from the original F# implementation. Implements
    /// <see cref="IExpressionParser"/> so the C# Parser can delegate to it.
    /// </summary>
    public class CSharpExpressionParser : IExpressionParser
    {
        // ── Entry point ─────────────────────────────────────────────────

        private Expression ParseExpression(ParserContext ctx, IParserHost host)
            => ParsePipeline(ctx, host);

        // ── Pipeline: lowest precedence ─────────────────────────────────

        private Expression ParsePipeline(ParserContext ctx, IParserHost host)
        {
            var expr = ParsePipeOperand(ctx, host);

            while (ctx.Match(TokenType.Pipe))
            {
                int line = ctx.Previous.Line;
                int column = ctx.Previous.Column;
                var right = ParsePipeOperand(ctx, host);
                expr = PipeComposition.BuildPipe(ctx, host, expr, right, line, column);
            }

            return expr;
        }

        private Expression ParsePipeOperand(ParserContext ctx, IParserHost host)
        {
            var expr = ParseAssignment(ctx, host);

            while (ctx.Match(TokenType.GreaterGreater))
            {
                int line = ctx.Previous.Line;
                int column = ctx.Previous.Column;
                var right = ParseAssignment(ctx, host);
                expr = PipeComposition.Compose(ctx, host, expr, right, line, column);
            }

            return expr;
        }

        // ── Assignment (includes ternary) ───────────────────────────────

        private Expression ParseAssignment(ParserContext ctx, IParserHost host)
        {
            var expr = ParseOr(ctx, host);

            // Ternary: cond ? then : else
            if (ctx.Match(TokenType.Question))
            {
                var thenBranch = ParseAssignment(ctx, host);
                ctx.Consume(TokenType.Colon, "Expected ':' in ternary expression");
                var elseBranch = ParseAssignment(ctx, host);
                expr = new TernaryExpression(expr, thenBranch, elseBranch, expr.Line, expr.Column);
            }

            // Null coalescing: expr ?? default
            if (ctx.Match(TokenType.NullCoalesce))
            {
                var right = ParseAssignment(ctx, host);
                expr = new NullCoalesceExpression(expr, right, expr.Line, expr.Column);
            }

            if (ctx.Match(TokenType.Assign, TokenType.PlusEqual, TokenType.MinusEqual,
                         TokenType.StarEqual, TokenType.SlashEqual, TokenType.PercentEqual))
            {
                var opToken = ctx.Previous;
                string op = opToken.Type switch
                {
                    TokenType.Assign => "=",
                    TokenType.PlusEqual => "+=",
                    TokenType.MinusEqual => "-=",
                    TokenType.StarEqual => "*=",
                    TokenType.SlashEqual => "/=",
                    TokenType.PercentEqual => "%=",
                    _ => "="
                };

                var value = ParsePipeline(ctx, host);

                if (expr is IdentifierExpression || expr is MemberExpression || expr is IndexExpression)
                    return new BinaryExpression(expr, op, value, opToken.Line, opToken.Column);

                ctx.AddError("Invalid assignment target", opToken.Line, opToken.Column);
            }

            return expr;
        }

        // ── Boolean operators ───────────────────────────────────────────

        private Expression ParseOr(ParserContext ctx, IParserHost host)
        {
            var expr = ParseAnd(ctx, host);

            while (ctx.Match(TokenType.OrOr))
            {
                string op = ctx.Previous.Text;
                var right = ParseAnd(ctx, host);
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseAnd(ParserContext ctx, IParserHost host)
        {
            var expr = ParseEquality(ctx, host);

            while (ctx.Match(TokenType.AndAnd))
            {
                string op = ctx.Previous.Text;
                var right = ParseEquality(ctx, host);
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        // ── Comparison operators ────────────────────────────────────────

        private Expression ParseEquality(ParserContext ctx, IParserHost host)
        {
            var expr = ParseComparison(ctx, host);

            while (ctx.Match(TokenType.EqualEqual, TokenType.BangEqual))
            {
                string op = ctx.Previous.Text;
                var right = ParseComparison(ctx, host);
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseComparison(ParserContext ctx, IParserHost host)
        {
            var expr = ParseTerm(ctx, host);

            while (ctx.Match(TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual))
            {
                string op = ctx.Previous.Text;
                var right = ParseTerm(ctx, host);
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            // is type check: expr is TypeName
            if (ctx.Match(TokenType.Is))
            {
                ctx.Consume(TokenType.Identifier, "Expected type name after 'is'");
                string typeName = ctx.Previous.Text;
                // Handle dotted type names
                while (ctx.Match(TokenType.Dot))
                {
                    ctx.Consume(TokenType.Identifier, "Expected identifier after '.' in type name");
                    typeName += "." + ctx.Previous.Text;
                }
                expr = new IsExpression(expr, typeName, expr.Line, expr.Column);
            }

            return expr;
        }

        // ── Arithmetic operators ────────────────────────────────────────

        private Expression ParseTerm(ParserContext ctx, IParserHost host)
        {
            var expr = ParseMultiplication(ctx, host);

            while (ctx.Match(TokenType.Plus, TokenType.Minus))
            {
                string op = ctx.Previous.Text;
                var right = ParseMultiplication(ctx, host);
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseMultiplication(ParserContext ctx, IParserHost host)
        {
            var expr = ParseUnary(ctx, host);

            while (ctx.Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
            {
                string op = ctx.Previous.Text;
                var right = ParseUnary(ctx, host);
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        // ── Unary operators ─────────────────────────────────────────────

        private Expression ParseUnary(ParserContext ctx, IParserHost host)
        {
            if (ctx.Match(TokenType.Minus, TokenType.Bang))
            {
                var op = ctx.Previous;
                var operand = ParseUnary(ctx, host);
                return new UnaryExpression(operand, op.Text, op.Line, op.Column);
            }
            return ParsePostfix(ctx, host);
        }

        // ── Postfix: call, member, index, scoped access, range ──────────

        private Expression ParsePostfix(ParserContext ctx, IParserHost host)
        {
            var expr = ParsePrimary(ctx, host);

            bool looping = true;
            while (looping)
            {
                // Generic call-site type args: first<int>(xs) or Box<int>::reset()
                if (ctx.Check(TokenType.Less))
                {
                    var genArgs = PostfixHelpers.TryLookaheadGenericCallArgs(ctx);
                    if (genArgs != null)
                    {
                        if (ctx.Match(TokenType.LParen))
                        {
                            var call = new CallExpression(expr, expr.Line, expr.Column);
                            call.TypeArguments.AddRange(genArgs);
                            if (!ctx.Check(TokenType.RParen))
                            {
                                bool argLooping = true;
                                while (argLooping)
                                {
                                    call.Arguments.Add(ParseExpression(ctx, host));
                                    if (!ctx.Match(TokenType.Comma)) argLooping = false;
                                }
                            }
                            ctx.Consume(TokenType.RParen, "Expected ')' after arguments");
                            expr = call;
                        }
                        else if (ctx.Match(TokenType.DoubleColon))
                        {
                            var modulePath = PostfixHelpers.TryGetDottedPath(expr);
                            if (modulePath != null)
                            {
                                ctx.Consume(TokenType.Identifier, "Expected member name after '::'");
                                string member = ctx.Previous.Text;
                                var scoped = new ScopedAccessExpression(modulePath, member, expr.Line, expr.Column);
                                scoped.TypeArguments.AddRange(genArgs);
                                expr = scoped;
                            }
                            else
                            {
                                ctx.AddError("Left side of '::' must be a module identifier", expr.Line, expr.Column);
                            }
                        }
                        else
                        {
                            looping = false;
                        }
                    }
                    else
                    {
                        looping = false;
                    }
                }
                else if (ctx.Match(TokenType.LParen))
                {
                    var call = new CallExpression(expr, expr.Line, expr.Column);
                    if (!ctx.Check(TokenType.RParen))
                    {
                        bool argLooping = true;
                        while (argLooping)
                        {
                            call.Arguments.Add(ParseExpression(ctx, host));
                            if (!ctx.Match(TokenType.Comma)) argLooping = false;
                        }
                    }
                    ctx.Consume(TokenType.RParen, "Expected ')' after arguments");
                    expr = call;
                }
                else if (ctx.Match(TokenType.Dot))
                {
                    ctx.Consume(TokenType.Identifier, "Expected property name after '.'");
                    string property = ctx.Previous.Text;
                    expr = new MemberExpression(expr, property, expr.Line, expr.Column);
                }
                else if (ctx.Match(TokenType.QuestionDot))
                {
                    ctx.Consume(TokenType.Identifier, "Expected property name after '?.'");
                    string property = ctx.Previous.Text;
                    expr = new SafeAccessExpression(expr, property, expr.Line, expr.Column);
                }
                else if (ctx.Match(TokenType.DoubleColon))
                {
                    var modulePath = PostfixHelpers.TryGetDottedPath(expr);
                    if (modulePath != null)
                    {
                        ctx.Consume(TokenType.Identifier, "Expected member name after '::'");
                        string member = ctx.Previous.Text;
                        expr = new ScopedAccessExpression(modulePath, member, expr.Line, expr.Column);
                    }
                    else
                    {
                        ctx.AddError("Left side of '::' must be a module identifier", expr.Line, expr.Column);
                    }
                }
                else if (ctx.Match(TokenType.LBracket))
                {
                    var index = ParseExpression(ctx, host);
                    ctx.Consume(TokenType.RBracket, "Expected ']' after array index");
                    expr = new IndexExpression(expr, index, expr.Line, expr.Column);
                }
                else if (ctx.Check(TokenType.DotDot) && ctx.SuppressRangeDepth == 0)
                {
                    ctx.Advance();
                    int line = ctx.Previous.Line;
                    int column = ctx.Previous.Column;
                    var endExpr = ParseOr(ctx, host);

                    object? sv = expr is LiteralExpression litS ? litS.Value : null;
                    object? ev = endExpr is LiteralExpression litE ? litE.Value : null;

                    if (sv != null && ev != null && sv is int startVal && ev is int endVal)
                    {
                        var arr = new ArrayLiteralExpression(line, column);
                        int step = startVal <= endVal ? 1 : -1;
                        for (int v = startVal; v != endVal + step; v += step)
                            arr.Elements.Add(new LiteralExpression(v, line, column));
                        expr = arr;
                    }
                    else
                    {
                        ctx.AddError("Range bounds must be integer literals (v1)", line, column);
                        expr = new ArrayLiteralExpression(line, column);
                    }
                }
                else
                {
                    looping = false;
                }
            }

            return expr;
        }

        // ── Primary: literals, identifiers, control flow, lambdas ───────

        private Expression ParsePrimary(ParserContext ctx, IParserHost host)
        {
            // match (x) { ... }
            if (ctx.Check(TokenType.Match))
            {
                ctx.Advance();
                return ParseMatchExpression(ctx, host);
            }

            // if (c) { a } else { b } as a VALUE
            if (ctx.Check(TokenType.If))
            {
                ctx.Advance();
                return ParseIfExpression(ctx, host);
            }

            if (ctx.Match(TokenType.IntLiteral))
            {
                string intText = ctx.Previous.Text;
                if (int.TryParse(intText, out int intValue))
                    return new LiteralExpression(intValue, ctx.Previous.Line, ctx.Previous.Column);

                ctx.AddWarning(
                    $"Integer literal '{intText}' exceeds the int range (max {int.MaxValue}); value clamped to 0",
                    ctx.Previous.Line, ctx.Previous.Column);
                return new LiteralExpression(0, ctx.Previous.Line, ctx.Previous.Column);
            }

            if (ctx.Match(TokenType.FloatLiteral))
            {
                if (double.TryParse(ctx.Previous.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double floatValue))
                    return new LiteralExpression(floatValue, ctx.Previous.Line, ctx.Previous.Column);

                ctx.AddError($"Invalid float literal: '{ctx.Previous.Text}'", ctx.Previous.Line, ctx.Previous.Column);
                return new LiteralExpression(0.0, ctx.Previous.Line, ctx.Previous.Column);
            }

            if (ctx.Match(TokenType.StringLiteral))
                return new LiteralExpression(ctx.Previous.Text, ctx.Previous.Line, ctx.Previous.Column);

            if (ctx.Match(TokenType.InterpolatedString))
                return ParseInterpolatedString(ctx, ctx.Previous.Text, ctx.Previous.Line, ctx.Previous.Column);

            if (ctx.Match(TokenType.True))
                return new LiteralExpression(true, ctx.Previous.Line, ctx.Previous.Column);

            if (ctx.Match(TokenType.False))
                return new LiteralExpression(false, ctx.Previous.Line, ctx.Previous.Column);

            if (ctx.Match(TokenType.Null))
                return new LiteralExpression(null!, ctx.Previous.Line, ctx.Previous.Column);

            if (ctx.Match(TokenType.LBracket))
            {
                var arrayLit = new ArrayLiteralExpression(ctx.Previous.Line, ctx.Previous.Column);
                bool arrLooping = true;
                while (arrLooping && !ctx.Check(TokenType.RBracket) && !ctx.Check(TokenType.Comma) && !ctx.IsAtEnd())
                {
                    arrayLit.Elements.Add(ParseExpression(ctx, host));
                    if (!ctx.Match(TokenType.Comma)) arrLooping = false;
                }
                ctx.Consume(TokenType.RBracket, "Expected ']' after array literal");
                return arrayLit;
            }

            if (ctx.Match(TokenType.Fun))
                return ParseLambda(ctx, host, "fun", false);

            if (ctx.CheckFnLambda())
            {
                ctx.Advance(); // consume 'fn'
                return ParseLambda(ctx, host, "fn", false);
            }

            if (ctx.Match(TokenType.New))
                return ParseNewExpression(ctx, host);

            if (ctx.Match(TokenType.Identifier))
                return new IdentifierExpression(ctx.Previous.Text, ctx.Previous.Line, ctx.Previous.Column);

            if (ctx.Match(TokenType.LParen))
            {
                // Tuple literal or parenthesized expression
                if (!ctx.Check(TokenType.RParen))
                {
                    var first = ParseExpression(ctx, host);
                    if (ctx.Match(TokenType.Comma))
                    {
                        var tuple = new TupleLiteralExpression(ctx.Previous.Line, ctx.Previous.Column);
                        tuple.Elements.Add(first);
                        bool tupleLooping = true;
                        while (tupleLooping)
                        {
                            tuple.Elements.Add(ParseExpression(ctx, host));
                            if (!ctx.Match(TokenType.Comma)) tupleLooping = false;
                        }
                        ctx.Consume(TokenType.RParen, "Expected ')' after tuple literal");
                        return tuple;
                    }
                    ctx.Consume(TokenType.RParen, "Expected ')' after expression");
                    return first;
                }
                ctx.Consume(TokenType.RParen, "Expected ')' after expression");
                return new LiteralExpression(0, ctx.Previous.Line, ctx.Previous.Column);
            }

            ctx.AddError(
                $"Unexpected token in expression: {ctx.Current.Type} ('{ctx.Current.Text}')",
                ctx.Current.Line, ctx.Current.Column);
            ctx.Advance();
            return new LiteralExpression(0, ctx.Current.Line, ctx.Current.Column);
        }

        // ── Lambda (fun / fn) ───────────────────────────────────────────

        private Expression ParseLambda(ParserContext ctx, IParserHost host, string keyword, bool isFn)
        {
            int line = ctx.Previous.Line;
            int column = ctx.Previous.Column;

            var parameters = new List<string>();
            var paramTypes = new List<string>();

            if (ctx.Match(TokenType.LParen))
            {
                if (!ctx.Check(TokenType.RParen))
                {
                    bool paramLooping = true;
                    while (paramLooping)
                    {
                        ctx.Consume(TokenType.Identifier, "Expected parameter name");
                        parameters.Add(ctx.Previous.Text);
                        string pt = "";
                        if (ctx.Match(TokenType.Colon)) pt = host.ParseType();
                        paramTypes.Add(pt);
                        if (!ctx.Match(TokenType.Comma)) paramLooping = false;
                    }
                }
                ctx.Consume(TokenType.RParen, "Expected ')' after lambda parameters");
            }
            else
            {
                while (ctx.Check(TokenType.Identifier))
                {
                    parameters.Add(ctx.Advance().Text);
                    paramTypes.Add("");
                }
            }

            if (!ctx.Match(TokenType.Arrow))
                ctx.AddError("Expected '->' after lambda parameters", ctx.Previous.Line, ctx.Previous.Column);

            if (ctx.Match(TokenType.LBrace))
            {
                var blockBody = host.ParseBlock();
                return new LambdaExpression(parameters, paramTypes, null, blockBody, line, column);
            }

            var body = ParseExpression(ctx, host);
            return new LambdaExpression(parameters, paramTypes, body, null, line, column);
        }

        // ── new expression ──────────────────────────────────────────────

        private Expression ParseNewExpression(ParserContext ctx, IParserHost host)
        {
            int line = ctx.Previous.Line;
            int column = ctx.Previous.Column;

            ctx.Consume(TokenType.Identifier, "Expected type name after 'new'");
            string typeName = ctx.Previous.Text;
            while (ctx.Match(TokenType.Dot))
            {
                ctx.Consume(TokenType.Identifier, "Expected identifier after '.' in type name");
                typeName += "." + ctx.Previous.Text;
            }

            if (ctx.Match(TokenType.DoubleColon))
            {
                ctx.Consume(TokenType.Identifier, "Expected type name after '::'");
                typeName += "." + ctx.Previous.Text;
            }

            var newExpr = new NewExpression(typeName, line, column);

            if (ctx.Match(TokenType.Less))
            {
                if (!ctx.Check(TokenType.Greater))
                {
                    bool typeArgLooping = true;
                    while (typeArgLooping)
                    {
                        newExpr.TypeArguments.Add(TypeDescriptor.Parse(host.ParseType()));
                        if (!ctx.Match(TokenType.Comma)) typeArgLooping = false;
                    }
                }
                ctx.Consume(TokenType.Greater, "Expected '>' after type arguments");
            }

            if (ctx.Match(TokenType.LBracket))
            {
                newExpr.Size = ParseExpression(ctx, host);
                ctx.Consume(TokenType.RBracket, "Expected ']' after array size");
            }
            else
            {
                ctx.Consume(TokenType.LParen, "Expected '(' after type name");
                if (!ctx.Check(TokenType.RParen))
                {
                    bool ctorArgLooping = true;
                    while (ctorArgLooping)
                    {
                        newExpr.Arguments.Add(ParseExpression(ctx, host));
                        if (!ctx.Match(TokenType.Comma)) ctorArgLooping = false;
                    }
                }
                ctx.Consume(TokenType.RParen, "Expected ')' after '('");
            }

            return newExpr;
        }

        // ── if expression ───────────────────────────────────────────────

        private IfExpression ParseIfExpression(ParserContext ctx, IParserHost host)
        {
            int line = ctx.Previous.Line;
            int column = ctx.Previous.Column;

            ctx.Consume(TokenType.LParen, "Expected '(' after 'if'");
            var condition = ParseExpression(ctx, host);
            ctx.Consume(TokenType.RParen, "Expected ')' after condition");

            var thenBranch = ParseValueBlock(ctx, host, "if branch");
            Expression? elseBranch = null;

            if (ctx.Match(TokenType.Else))
            {
                if (ctx.Check(TokenType.If))
                {
                    ctx.Advance();
                    elseBranch = ParseIfExpression(ctx, host);
                }
                else
                {
                    elseBranch = ParseValueBlock(ctx, host, "else branch");
                }
            }
            else
            {
                ctx.AddError("An 'if' expression requires an 'else' branch — both arms must produce a value", line, column);
                elseBranch = new LiteralExpression(0, line, column);
            }

            return new IfExpression(condition, thenBranch, elseBranch!, line, column);
        }

        private Expression ParseValueBlock(ParserContext ctx, IParserHost host, string what)
        {
            ctx.Consume(TokenType.LBrace, $"Expected '{{' after {what}");
            var value = PipeComposition.ApplyImplicitLambda(ctx, ParseExpression(ctx, host), ctx.Current.Line, ctx.Current.Column);
            ctx.Match(TokenType.Semicolon); // tolerate trailing ';'
            ctx.Consume(TokenType.RBrace, $"Expected '}}' to close {what}");
            return value;
        }

        // ── match expression ────────────────────────────────────────────

        private MatchExpression ParseMatchExpression(ParserContext ctx, IParserHost host)
        {
            int line = ctx.Previous.Line;
            int column = ctx.Previous.Column;

            ctx.Consume(TokenType.LParen, "Expected '(' after 'match'");
            var scrutinee = ParseExpression(ctx, host);
            ctx.Consume(TokenType.RParen, "Expected ')' after match scrutinee");
            var match = new MatchExpression(scrutinee, line, column);

            ctx.Consume(TokenType.LBrace, "Expected '{' after match scrutinee");

            bool armLooping = true;
            while (armLooping && !ctx.Check(TokenType.RBrace) && !ctx.IsAtEnd())
            {
                int startPos = ctx.Cursor;
                var arm = new MatchArm(ctx.Current.Line, ctx.Current.Column);

                // Patterns: p1 | p2 | p3 (or-pattern)
                bool patternLooping = true;
                while (patternLooping)
                {
                    arm.Patterns.Add(ParseMatchPattern(ctx));
                    if (!ctx.Match(TokenType.Pipe)) patternLooping = false;
                }

                if (ctx.Match(TokenType.If))
                    arm.Guard = ParseExpression(ctx, host);

                ctx.Consume(TokenType.FatArrow, "Expected '=>' after match pattern");
                arm.Result = PipeComposition.ApplyImplicitLambda(ctx, ParseExpression(ctx, host), line, column);
                match.Arms.Add(arm);

                if (!ctx.Match(TokenType.Comma)) armLooping = false; // last arm may omit trailing comma

                if (ctx.Cursor == startPos && !ctx.IsAtEnd())
                {
                    ctx.AddError("Parser failed to advance in match arm", ctx.Current.Line, ctx.Current.Column);
                    ctx.Advance();
                }
            }

            ctx.Consume(TokenType.RBrace, "Expected '}' after match arms");

            if (match.Arms.Count == 0)
                ctx.AddError("Match must declare at least one arm", line, column);

            return match;
        }

        // ── match patterns ──────────────────────────────────────────────

        private MatchPattern ParseMatchPattern(ParserContext ctx)
        {
            int line = ctx.Current.Line;
            int column = ctx.Current.Column;

            if (ctx.Match(TokenType.IntLiteral))
            {
                int.TryParse(ctx.Previous.Text, out int i);
                return new LiteralPattern(i, line, column);
            }
            if (ctx.Match(TokenType.FloatLiteral))
            {
                double.TryParse(ctx.Previous.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double dv);
                return new LiteralPattern(dv, line, column);
            }
            if (ctx.Match(TokenType.StringLiteral))
                return new LiteralPattern(ctx.Previous.Text, line, column);
            if (ctx.Match(TokenType.True))
                return new LiteralPattern(true, line, column);
            if (ctx.Match(TokenType.False))
                return new LiteralPattern(false, line, column);
            if (ctx.Match(TokenType.Null))
                return new LiteralPattern(null!, line, column);

            if (ctx.Match(TokenType.Identifier))
            {
                string name = ctx.Previous.Text;
                if (ctx.Check(TokenType.LParen))
                {
                    // Variant pattern: Circle(r), Rect(w, h)
                    var variant = new VariantPattern(name, line, column);
                    ctx.Advance(); // consume '('
                    if (!ctx.Check(TokenType.RParen))
                    {
                        bool varArgLooping = true;
                        while (varArgLooping)
                        {
                            variant.Arguments.Add(ParseMatchPattern(ctx));
                            if (!ctx.Match(TokenType.Comma)) varArgLooping = false;
                        }
                    }
                    ctx.Consume(TokenType.RParen, $"Expected ')' after variant pattern '{name}('");
                    return variant;
                }
                if (name == "_")
                    return new WildcardPattern(line, column);
                return new BindingPattern(name, line, column);
            }

            ctx.AddError($"Expected a match pattern, found '{ctx.Current.Text}'", line, column);
            ctx.Advance();
            return new WildcardPattern(line, column);
        }

        // ── interpolated strings ────────────────────────────────────────

        private Expression ParseInterpolatedString(ParserContext ctx, string raw, int line, int column)
        {
            string content = raw.Length >= 2 ? raw.Substring(1, raw.Length - 2) : raw;
            var parts = new List<Expression>();
            int i = 0;

            bool strLooping = true;
            while (strLooping && i < content.Length)
            {
                int openIdx = content.IndexOf('{', i);
                if (openIdx < 0)
                {
                    parts.Add(new LiteralExpression("\"" + content.Substring(i) + "\"", line, column));
                    strLooping = false;
                }
                else
                {
                    if (openIdx > i)
                        parts.Add(new LiteralExpression("\"" + content.Substring(i, openIdx - i) + "\"", line, column));

                    int closeIdx = content.IndexOf('}', openIdx);
                    if (closeIdx < 0)
                    {
                        ctx.AddError("Unterminated interpolation in string literal", line, column);
                        parts.Add(new LiteralExpression(content.Substring(i), line, column));
                        strLooping = false;
                    }
                    else
                    {
                        string name = content.Substring(openIdx + 1, closeIdx - openIdx - 1);
                        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') ||
                            name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                        {
                            ctx.AddError($"Invalid interpolation expression: '{{{name}}}'", line, column);
                        }
                        else
                        {
                            parts.Add(new IdentifierExpression(name, line, column));
                        }
                        i = closeIdx + 1;
                    }
                }
            }

            if (parts.Count == 0)
                return new LiteralExpression("", line, column);

            var result = parts[0];
            for (int k = 1; k < parts.Count; k++)
                result = new BinaryExpression(result, "+", parts[k], line, column);
            return result;
        }

        // ── IExpressionParser implementation ────────────────────────────

        Expression IExpressionParser.ParseExpression(ParserContext ctx, IParserHost host)
            => ParseExpression(ctx, host);

        IfExpression IExpressionParser.ParseIfExpression(ParserContext ctx, IParserHost host)
            => ParseIfExpression(ctx, host);

        MatchExpression IExpressionParser.ParseMatchExpression(ParserContext ctx, IParserHost host)
            => ParseMatchExpression(ctx, host);

        MatchPattern IExpressionParser.ParseMatchPattern(ParserContext ctx)
            => ParseMatchPattern(ctx);

        Expression IExpressionParser.ParseInterpolatedString(ParserContext ctx, string raw, int line, int column)
            => ParseInterpolatedString(ctx, raw, line, column);

        Expression IExpressionParser.ApplyImplicitLambda(ParserContext ctx, Expression expr, int line, int column)
            => PipeComposition.ApplyImplicitLambda(ctx, expr, line, column);

        bool IExpressionParser.TryFoldConstant(Expression expr, ContractDeclaration contract, IParserHost host, out object? value)
        {
            var (ok, foldedValue) = ConstantFolding.TryFoldConstant(null!, expr, contract, host);
            value = foldedValue;
            return ok;
        }
    }
}
