namespace Contract.Compiler.Expressions

open System
open System.Collections.Generic
open Contract.Compiler.AST
open Contract.Compiler.Parsing
open Contract.Compiler.Expressions.Internal

/// <summary>
/// The recursive expression parsing chain. All functions are mutually
/// recursive and live in this module so they can reference each other.
/// </summary>
[<AutoOpen>]
module internal ExpressionParsing =

    // ── Entry point ─────────────────────────────────────────────────

    let rec parseExpression (ctx: ParserContext) (host: IParserHost) : Expression =
        parsePipeline ctx host

    // ── Pipeline: lowest precedence ─────────────────────────────────

    and parsePipeline (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parsePipeOperand ctx host

        while ctx.Match(TokenType.Pipe) do
            let line = ctx.Previous.Line
            let column = ctx.Previous.Column
            let right = parsePipeOperand ctx host
            expr <- PipeComposition.buildPipe ctx host expr right line column

        expr

    and parsePipeOperand (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parseAssignment ctx host

        while ctx.Match(TokenType.GreaterGreater) do
            let line = ctx.Previous.Line
            let column = ctx.Previous.Column
            let right = parseAssignment ctx host
            expr <- PipeComposition.compose ctx host expr right line column

        expr

    // ── Assignment (includes ternary) ───────────────────────────────

    and parseAssignment (ctx: ParserContext) (host: IParserHost) : Expression =
        let expr = parseOr ctx host

        // Ternary: cond ? then : else
        if ctx.Match(TokenType.Question) then
            let thenBranch = parseAssignment ctx host
            ctx.Consume(TokenType.Colon, "Expected ':' in ternary expression") |> ignore
            let elseBranch = parseAssignment ctx host
            TernaryExpression(expr, thenBranch, elseBranch, expr.Line, expr.Column) :> Expression
        else
            if ctx.Match(TokenType.Assign, TokenType.PlusEqual, TokenType.MinusEqual,
                         TokenType.StarEqual, TokenType.SlashEqual, TokenType.PercentEqual) then
                let opToken = ctx.Previous
                let op =
                    match opToken.Type with
                    | TokenType.Assign -> "="
                    | TokenType.PlusEqual -> "+="
                    | TokenType.MinusEqual -> "-="
                    | TokenType.StarEqual -> "*="
                    | TokenType.SlashEqual -> "/="
                    | TokenType.PercentEqual -> "%="
                    | _ -> "="

                let value = parsePipeline ctx host

                match expr with
                | :? IdentifierExpression | :? MemberExpression | :? IndexExpression ->
                    BinaryExpression(expr, op, value, opToken.Line, opToken.Column) :> Expression
                | _ ->
                    ctx.AddError("Invalid assignment target", opToken.Line, opToken.Column)
                    expr
            else
                expr

    // ── Boolean operators ───────────────────────────────────────────

    and parseOr (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parseAnd ctx host

        while ctx.Match(TokenType.OrOr) do
            let op = ctx.Previous.Text
            let right = parseAnd ctx host
            expr <- BinaryExpression(expr, op, right, expr.Line, expr.Column) :> Expression

        expr

    and parseAnd (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parseEquality ctx host

        while ctx.Match(TokenType.AndAnd) do
            let op = ctx.Previous.Text
            let right = parseEquality ctx host
            expr <- BinaryExpression(expr, op, right, expr.Line, expr.Column) :> Expression

        expr

    // ── Comparison operators ────────────────────────────────────────

    and parseEquality (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parseComparison ctx host

        while ctx.Match(TokenType.EqualEqual, TokenType.BangEqual) do
            let op = ctx.Previous.Text
            let right = parseComparison ctx host
            expr <- BinaryExpression(expr, op, right, expr.Line, expr.Column) :> Expression

        expr

    and parseComparison (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parseTerm ctx host

        while ctx.Match(TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual) do
            let op = ctx.Previous.Text
            let right = parseTerm ctx host
            expr <- BinaryExpression(expr, op, right, expr.Line, expr.Column) :> Expression

        expr

    // ── Arithmetic operators ────────────────────────────────────────

    and parseTerm (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parseMultiplication ctx host

        while ctx.Match(TokenType.Plus, TokenType.Minus) do
            let op = ctx.Previous.Text
            let right = parseMultiplication ctx host
            expr <- BinaryExpression(expr, op, right, expr.Line, expr.Column) :> Expression

        expr

    and parseMultiplication (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parseUnary ctx host

        while ctx.Match(TokenType.Star, TokenType.Slash, TokenType.Percent) do
            let op = ctx.Previous.Text
            let right = parseUnary ctx host
            expr <- BinaryExpression(expr, op, right, expr.Line, expr.Column) :> Expression

        expr

    // ── Unary operators ─────────────────────────────────────────────

    and parseUnary (ctx: ParserContext) (host: IParserHost) : Expression =
        if ctx.Match(TokenType.Minus, TokenType.Bang) then
            let op = ctx.Previous
            let operand = parseUnary ctx host
            UnaryExpression(operand, op.Text, op.Line, op.Column) :> Expression
        else
            parsePostfix ctx host

    // ── Postfix: call, member, index, scoped access, range ──────────

    and parsePostfix (ctx: ParserContext) (host: IParserHost) : Expression =
        let mutable expr = parsePrimary ctx host

        let mutable looping = true
        while looping do
            // Generic call-site type args: first<int>(xs) or Box<int>::reset()
            if ctx.Check(TokenType.Less) then
                match PostfixHelpers.tryLookaheadGenericCallArgs ctx with
                | Some genArgs when not (isNull (box genArgs)) ->
                    if ctx.Match(TokenType.LParen) then
                        let call = CallExpression(expr, expr.Line, expr.Column)
                        call.TypeArguments.AddRange(genArgs)
                        if not (ctx.Check(TokenType.RParen)) then
                            let mutable argLooping = true
                            while argLooping do
                                call.Arguments.Add(parseExpression ctx host)
                                if not (ctx.Match(TokenType.Comma)) then argLooping <- false
                        ctx.Consume(TokenType.RParen, "Expected ')' after arguments") |> ignore
                        expr <- call :> Expression
                    elif ctx.Match(TokenType.DoubleColon) then
                        match PostfixHelpers.tryGetDottedPath expr with
                        | Some modulePath ->
                            ctx.Consume(TokenType.Identifier, "Expected member name after '::'") |> ignore
                            let member' = ctx.Previous.Text
                            let scoped = ScopedAccessExpression(modulePath, member', expr.Line, expr.Column)
                            scoped.TypeArguments.AddRange(genArgs)
                            expr <- scoped :> Expression
                        | None ->
                            ctx.AddError("Left side of '::' must be a module identifier", expr.Line, expr.Column)
                    else
                        looping <- false
                | _ -> looping <- false
            elif ctx.Match(TokenType.LParen) then
                let call = CallExpression(expr, expr.Line, expr.Column)
                if not (ctx.Check(TokenType.RParen)) then
                    let mutable argLooping = true
                    while argLooping do
                        call.Arguments.Add(parseExpression ctx host)
                        if not (ctx.Match(TokenType.Comma)) then argLooping <- false
                ctx.Consume(TokenType.RParen, "Expected ')' after arguments") |> ignore
                expr <- call :> Expression
            elif ctx.Match(TokenType.Dot) then
                ctx.Consume(TokenType.Identifier, "Expected property name after '.'") |> ignore
                let property = ctx.Previous.Text
                expr <- MemberExpression(expr, property, expr.Line, expr.Column) :> Expression
            elif ctx.Match(TokenType.DoubleColon) then
                match PostfixHelpers.tryGetDottedPath expr with
                | Some modulePath ->
                    ctx.Consume(TokenType.Identifier, "Expected member name after '::'") |> ignore
                    let member' = ctx.Previous.Text
                    expr <- ScopedAccessExpression(modulePath, member', expr.Line, expr.Column) :> Expression
                | None ->
                    ctx.AddError("Left side of '::' must be a module identifier", expr.Line, expr.Column)
            elif ctx.Match(TokenType.LBracket) then
                let index = parseExpression ctx host
                ctx.Consume(TokenType.RBracket, "Expected ']' after array index") |> ignore
                expr <- IndexExpression(expr, index, expr.Line, expr.Column) :> Expression
            elif ctx.Check(TokenType.DotDot) && ctx.SuppressRangeDepth = 0 then
                ctx.Advance() |> ignore
                let line = ctx.Previous.Line
                let column = ctx.Previous.Column
                let endExpr = parseOr ctx host
                let sv : obj =
                    match expr with
                    | :? LiteralExpression as lit -> lit.Value
                    | _ -> null
                let ev : obj =
                    match endExpr with
                    | :? LiteralExpression as lit -> lit.Value
                    | _ -> null
                if not (isNull sv) && not (isNull ev) then
                    match sv with
                    | :? int as startVal ->
                        match ev with
                        | :? int as endVal ->
                            let arr = ArrayLiteralExpression(line, column)
                            let step = if startVal <= endVal then 1 else -1
                            let mutable v = startVal
                            while v <> endVal + step do
                                arr.Elements.Add(LiteralExpression(v, line, column) :> Expression)
                                v <- v + step
                            expr <- arr :> Expression
                        | _ ->
                            ctx.AddError("Range bounds must be integer literals (v1)", line, column)
                            expr <- ArrayLiteralExpression(line, column) :> Expression
                    | _ ->
                        ctx.AddError("Range bounds must be integer literals (v1)", line, column)
                        expr <- ArrayLiteralExpression(line, column) :> Expression
                else
                    ctx.AddError("Range bounds must be integer literals (v1)", line, column)
                    expr <- ArrayLiteralExpression(line, column) :> Expression
            else
                looping <- false

        expr

    // ── Primary: literals, identifiers, control flow, lambdas ───────

    and parsePrimary (ctx: ParserContext) (host: IParserHost) : Expression =
        // match (x) { ... }
        if ctx.Check(TokenType.Match) then
            ctx.Advance() |> ignore
            parseMatchExpression ctx host :> Expression

        // if (c) { a } else { b } as a VALUE
        elif ctx.Check(TokenType.If) then
            ctx.Advance() |> ignore
            parseIfExpression ctx host :> Expression

        elif ctx.Match(TokenType.IntLiteral) then
            let intText = ctx.Previous.Text
            match Int32.TryParse(intText) with
            | (true, intValue) ->
                LiteralExpression(box intValue, ctx.Previous.Line, ctx.Previous.Column) :> Expression
            | _ ->
                ctx.AddWarning(
                    sprintf "Integer literal '%s' exceeds the int range (max %d); value clamped to 0" intText Int32.MaxValue,
                    ctx.Previous.Line, ctx.Previous.Column)
                LiteralExpression(box 0, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        elif ctx.Match(TokenType.FloatLiteral) then
            match Double.TryParse(ctx.Previous.Text, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | (true, floatValue) ->
                LiteralExpression(box floatValue, ctx.Previous.Line, ctx.Previous.Column) :> Expression
            | _ ->
                ctx.AddError(sprintf "Invalid float literal: '%s'" ctx.Previous.Text, ctx.Previous.Line, ctx.Previous.Column)
                LiteralExpression(box 0.0, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        elif ctx.Match(TokenType.StringLiteral) then
            LiteralExpression(box ctx.Previous.Text, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        elif ctx.Match(TokenType.InterpolatedString) then
            parseInterpolatedString ctx ctx.Previous.Text ctx.Previous.Line ctx.Previous.Column

        elif ctx.Match(TokenType.True) then
            LiteralExpression(box true, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        elif ctx.Match(TokenType.False) then
            LiteralExpression(box false, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        elif ctx.Match(TokenType.Null) then
            LiteralExpression(null, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        elif ctx.Match(TokenType.LBracket) then
            let arrayLit = ArrayLiteralExpression(ctx.Previous.Line, ctx.Previous.Column)
            let mutable arrLooping = true
            while arrLooping && not (ctx.Check(TokenType.RBracket)) && not (ctx.Check(TokenType.Comma)) && not (ctx.IsAtEnd()) do
                arrayLit.Elements.Add(parseExpression ctx host)
                if not (ctx.Match(TokenType.Comma)) then arrLooping <- false
            ctx.Consume(TokenType.RBracket, "Expected ']' after array literal") |> ignore
            arrayLit :> Expression

        elif ctx.Match(TokenType.Fun) then
            parseLambda ctx host "fun" false

        elif ctx.CheckFnLambda() then
            ctx.Advance() |> ignore // consume 'fn'
            parseLambda ctx host "fn" false

        elif ctx.Match(TokenType.New) then
            parseNewExpression ctx host

        elif ctx.Match(TokenType.Identifier) then
            IdentifierExpression(ctx.Previous.Text, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        elif ctx.Match(TokenType.LParen) then
            // Tuple literal or parenthesized expression
            if not (ctx.Check(TokenType.RParen)) then
                let first = parseExpression ctx host
                if ctx.Match(TokenType.Comma) then
                    let tuple = TupleLiteralExpression(ctx.Previous.Line, ctx.Previous.Column)
                    tuple.Elements.Add(first)
                    let mutable tupleLooping = true
                    while tupleLooping do
                        tuple.Elements.Add(parseExpression ctx host)
                        if not (ctx.Match(TokenType.Comma)) then tupleLooping <- false
                    ctx.Consume(TokenType.RParen, "Expected ')' after tuple literal") |> ignore
                    tuple :> Expression
                else
                    ctx.Consume(TokenType.RParen, "Expected ')' after expression") |> ignore
                    first
            else
                ctx.Consume(TokenType.RParen, "Expected ')' after expression") |> ignore
                LiteralExpression(box 0, ctx.Previous.Line, ctx.Previous.Column) :> Expression

        else
            ctx.AddError(sprintf "Unexpected token in expression: %s ('%s')" (ctx.Current.Type.ToString()) ctx.Current.Text,
                         ctx.Current.Line, ctx.Current.Column)
            ctx.Advance() |> ignore
            LiteralExpression(box 0, ctx.Current.Line, ctx.Current.Column) :> Expression

    // ── Lambda (fun / fn) ───────────────────────────────────────────

    and parseLambda (ctx: ParserContext) (host: IParserHost) (keyword: string) (isFn: bool) : Expression =
        let line = ctx.Previous.Line
        let column = ctx.Previous.Column

        let parameters = List<string>()
        let paramTypes = List<string>()

        if ctx.Match(TokenType.LParen) then
            if not (ctx.Check(TokenType.RParen)) then
                let mutable paramLooping = true
                while paramLooping do
                    ctx.Consume(TokenType.Identifier, "Expected parameter name") |> ignore
                    parameters.Add(ctx.Previous.Text)
                    let mutable pt = ""
                    if ctx.Match(TokenType.Colon) then pt <- host.ParseType()
                    paramTypes.Add(pt)
                    if not (ctx.Match(TokenType.Comma)) then paramLooping <- false
            ctx.Consume(TokenType.RParen, "Expected ')' after lambda parameters") |> ignore
        else
            while ctx.Check(TokenType.Identifier) do
                parameters.Add(ctx.Advance().Text)
                paramTypes.Add("")

        if not (ctx.Match(TokenType.Arrow)) then
            ctx.AddError("Expected '->' after lambda parameters", ctx.Previous.Line, ctx.Previous.Column)

        if ctx.Match(TokenType.LBrace) then
            let blockBody = host.ParseBlock()
            LambdaExpression(parameters, paramTypes, null, blockBody, line, column) :> Expression
        else
            let body = parseExpression ctx host
            LambdaExpression(parameters, paramTypes, body, null, line, column) :> Expression

    // ── new expression ──────────────────────────────────────────────

    and parseNewExpression (ctx: ParserContext) (host: IParserHost) : Expression =
        let line = ctx.Previous.Line
        let column = ctx.Previous.Column

        ctx.Consume(TokenType.Identifier, "Expected type name after 'new'") |> ignore
        let mutable typeName = ctx.Previous.Text
        while ctx.Match(TokenType.Dot) do
            ctx.Consume(TokenType.Identifier, "Expected identifier after '.' in type name") |> ignore
            typeName <- typeName + "." + ctx.Previous.Text

        if ctx.Match(TokenType.DoubleColon) then
            ctx.Consume(TokenType.Identifier, "Expected type name after '::'") |> ignore
            typeName <- typeName + "." + ctx.Previous.Text

        let newExpr = NewExpression(typeName, line, column)

        if ctx.Match(TokenType.Less) then
            if not (ctx.Check(TokenType.Greater)) then
                let mutable typeArgLooping = true
                while typeArgLooping do
                    newExpr.TypeArguments.Add(TypeDescriptor.Parse(host.ParseType()))
                    if not (ctx.Match(TokenType.Comma)) then typeArgLooping <- false
            ctx.Consume(TokenType.Greater, "Expected '>' after type arguments") |> ignore

        if ctx.Match(TokenType.LBracket) then
            newExpr.Size <- parseExpression ctx host
            ctx.Consume(TokenType.RBracket, "Expected ']' after array size") |> ignore
        else
            ctx.Consume(TokenType.LParen, "Expected '(' after type name") |> ignore
            if not (ctx.Check(TokenType.RParen)) then
                let mutable ctorArgLooping = true
                while ctorArgLooping do
                    newExpr.Arguments.Add(parseExpression ctx host)
                    if not (ctx.Match(TokenType.Comma)) then ctorArgLooping <- false
            ctx.Consume(TokenType.RParen, "Expected ')' after '('") |> ignore

        newExpr :> Expression

    // ── if expression ───────────────────────────────────────────────

    and parseIfExpression (ctx: ParserContext) (host: IParserHost) : IfExpression =
        let line = ctx.Previous.Line
        let column = ctx.Previous.Column

        ctx.Consume(TokenType.LParen, "Expected '(' after 'if'") |> ignore
        let condition = parseExpression ctx host
        ctx.Consume(TokenType.RParen, "Expected ')' after condition") |> ignore

        let thenBranch = parseValueBlock ctx host "if branch"
        let mutable elseBranch : Expression = Unchecked.defaultof<_>

        if ctx.Match(TokenType.Else) then
            if ctx.Check(TokenType.If) then
                ctx.Advance() |> ignore
                elseBranch <- parseIfExpression ctx host :> Expression
            else
                elseBranch <- parseValueBlock ctx host "else branch"
        else
            ctx.AddError("An 'if' expression requires an 'else' branch — both arms must produce a value", line, column)
            elseBranch <- LiteralExpression(box 0, line, column) :> Expression

        IfExpression(condition, thenBranch, elseBranch, line, column)

    and parseValueBlock (ctx: ParserContext) (host: IParserHost) (what: string) : Expression =
        ctx.Consume(TokenType.LBrace, sprintf "Expected '{' after %s" what) |> ignore
        let value = PipeComposition.applyImplicitLambda ctx (parseExpression ctx host) ctx.Current.Line ctx.Current.Column
        ctx.Match(TokenType.Semicolon) |> ignore // tolerate trailing ';'
        ctx.Consume(TokenType.RBrace, sprintf "Expected '}' to close %s" what) |> ignore
        value

    // ── match expression ────────────────────────────────────────────

    and parseMatchExpression (ctx: ParserContext) (host: IParserHost) : MatchExpression =
        let line = ctx.Previous.Line
        let column = ctx.Previous.Column

        ctx.Consume(TokenType.LParen, "Expected '(' after 'match'") |> ignore
        let scrutinee = parseExpression ctx host
        ctx.Consume(TokenType.RParen, "Expected ')' after match scrutinee") |> ignore
        let match' = MatchExpression(scrutinee, line, column)

        ctx.Consume(TokenType.LBrace, "Expected '{' after match scrutinee") |> ignore

        let mutable armLooping = true
        while armLooping && not (ctx.Check(TokenType.RBrace)) && not (ctx.IsAtEnd()) do
            let startPos = ctx.Cursor
            let arm = MatchArm(ctx.Current.Line, ctx.Current.Column)

            // Patterns: p1 | p2 | p3 (or-pattern)
            let mutable patternLooping = true
            while patternLooping do
                arm.Patterns.Add(parseMatchPattern ctx)
                if not (ctx.Match(TokenType.Pipe)) then patternLooping <- false

            if ctx.Match(TokenType.If) then
                arm.Guard <- parseExpression ctx host

            ctx.Consume(TokenType.FatArrow, "Expected '=>' after match pattern") |> ignore
            arm.Result <- PipeComposition.applyImplicitLambda ctx (parseExpression ctx host) line column
            match'.Arms.Add(arm)

            if not (ctx.Match(TokenType.Comma)) then armLooping <- false // last arm may omit trailing comma

            if ctx.Cursor = startPos && not (ctx.IsAtEnd()) then
                ctx.AddError("Parser failed to advance in match arm", ctx.Current.Line, ctx.Current.Column)
                ctx.Advance() |> ignore

        ctx.Consume(TokenType.RBrace, "Expected '}' after match arms") |> ignore

        if match'.Arms.Count = 0 then
            ctx.AddError("Match must declare at least one arm", line, column)

        match'

    // ── match patterns ──────────────────────────────────────────────

    and parseMatchPattern (ctx: ParserContext) : MatchPattern =
        let line = ctx.Current.Line
        let column = ctx.Current.Column

        if ctx.Match(TokenType.IntLiteral) then
            let mutable i = 0
            Int32.TryParse(ctx.Previous.Text, &i) |> ignore
            LiteralPattern(box i, line, column) :> MatchPattern
        elif ctx.Match(TokenType.FloatLiteral) then
            let mutable dv = 0.0
            let d = Double.TryParse(ctx.Previous.Text, Globalization.NumberStyles.Float,
                                     Globalization.CultureInfo.InvariantCulture, &dv)
            LiteralPattern(box (if d then dv else 0.0), line, column) :> MatchPattern
        elif ctx.Match(TokenType.StringLiteral) then
            LiteralPattern(box ctx.Previous.Text, line, column) :> MatchPattern
        elif ctx.Match(TokenType.True) then
            LiteralPattern(box true, line, column) :> MatchPattern
        elif ctx.Match(TokenType.False) then
            LiteralPattern(box false, line, column) :> MatchPattern
        elif ctx.Match(TokenType.Null) then
            LiteralPattern(null, line, column) :> MatchPattern
        elif ctx.Match(TokenType.Identifier) then
            let name = ctx.Previous.Text
            if ctx.Check(TokenType.LParen) then
                // Variant pattern: Circle(r), Rect(w, h)
                let variant = VariantPattern(name, line, column)
                ctx.Advance() |> ignore // consume '('
                if not (ctx.Check(TokenType.RParen)) then
                    let mutable varArgLooping = true
                    while varArgLooping do
                        variant.Arguments.Add(parseMatchPattern ctx)
                        if not (ctx.Match(TokenType.Comma)) then varArgLooping <- false
                ctx.Consume(TokenType.RParen, sprintf "Expected ')' after variant pattern '%s('" name) |> ignore
                variant :> MatchPattern
            elif name = "_" then
                WildcardPattern(line, column) :> MatchPattern
            else
                BindingPattern(name, line, column) :> MatchPattern
        else
            ctx.AddError(sprintf "Expected a match pattern, found '%s'" ctx.Current.Text, line, column)
            ctx.Advance() |> ignore
            WildcardPattern(line, column) :> MatchPattern

    // ── interpolated strings ────────────────────────────────────────

    and parseInterpolatedString (ctx: ParserContext) (raw: string) (line: int) (column: int) : Expression =
        let content = if raw.Length >= 2 then raw.Substring(1, raw.Length - 2) else raw
        let parts = List<Expression>()
        let mutable i = 0

        let mutable strLooping = true
        while strLooping && i < content.Length do
            let mutable openIdx = content.IndexOf('{', i)
            if openIdx < 0 then
                parts.Add(LiteralExpression(box ("\"" + content.Substring(i) + "\""), line, column) :> Expression)
                strLooping <- false
            else
                if openIdx > i then
                    parts.Add(LiteralExpression(box ("\"" + content.Substring(i, openIdx - i) + "\""), line, column) :> Expression)

                let closeIdx = content.IndexOf('}', openIdx)
                if closeIdx < 0 then
                    ctx.AddError("Unterminated interpolation in string literal", line, column)
                    parts.Add(LiteralExpression(box (content.Substring(i)), line, column) :> Expression)
                    strLooping <- false
                else
                    let name = content.Substring(openIdx + 1, closeIdx - openIdx - 1)
                    if name.Length = 0 || not (Char.IsLetter(name.[0]) || name.[0] = '_') ||
                       name |> Seq.exists (fun c -> not (Char.IsLetterOrDigit(c) || c = '_')) then
                        ctx.AddError(sprintf "Invalid interpolation expression: '{%s}'" name, line, column)
                    else
                        parts.Add(IdentifierExpression(name, line, column) :> Expression)
                    i <- closeIdx + 1

        if parts.Count = 0 then
            LiteralExpression(box "", line, column) :> Expression
        else
            let mutable result = parts.[0]
            for k in 1 .. parts.Count - 1 do
                result <- BinaryExpression(result, "+", parts.[k], line, column) :> Expression
            result

/// <summary>
/// F# expression parser — a direct port of the recursive-descent
/// expression parsing from the C# Parser class. Implements
/// <see cref="IExpressionParser"/> so the C# Parser can delegate to it.
/// </summary>
type FSharpExpressionParser() =

    interface IExpressionParser with

        member _.ParseExpression(ctx, host) =
            ExpressionParsing.parseExpression ctx host

        member _.ParseIfExpression(ctx, host) =
            ExpressionParsing.parseIfExpression ctx host

        member _.ParseMatchExpression(ctx, host) =
            ExpressionParsing.parseMatchExpression ctx host

        member _.ParseMatchPattern(ctx) =
            ExpressionParsing.parseMatchPattern ctx

        member _.ParseInterpolatedString(ctx, raw, line, column) =
            ExpressionParsing.parseInterpolatedString ctx raw line column

        member _.ApplyImplicitLambda(ctx, expr, line, column) =
            PipeComposition.applyImplicitLambda ctx expr line column

        member _.TryFoldConstant(expr, contract, host, value) =
            let (ok, foldedValue) = ConstantFolding.tryFoldConstant (Unchecked.defaultof<ParserContext>) expr contract host
            value <- foldedValue
            ok
