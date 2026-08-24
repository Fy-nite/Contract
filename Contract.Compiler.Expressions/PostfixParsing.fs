namespace Contract.Compiler.Expressions.Internal

open Contract.Compiler.AST
open Contract.Compiler.Parsing

/// <summary>
/// Helpers used by <c>ParsePostfix</c>: generic type-argument lookahead and
/// dotted-path extraction.
/// </summary>
[<AutoOpen>]
module PostfixHelpers =

    /// <summary>
    /// Collects a dotted identifier path from an expression: IdentifierExpression("A")
    /// yields "A"; A.B.C (a left-leaning chain of MemberExpressions over identifiers)
    /// yields "A.B.C". Returns false for anything else.
    /// </summary>
    let tryGetDottedPath (expr: Expression) : string option =
        let segments = System.Collections.Generic.Stack<string>()
        let mutable current = expr
        while current :? MemberExpression do
            let mem = current :?> MemberExpression
            segments.Push(mem.Property)
            current <- mem.Object
        match current with
        | :? IdentifierExpression as root ->
            segments.Push(root.Name)
            Some(System.String.Join(".", segments))
        | _ -> None

    /// <summary>
    /// When the current token is <c>&lt;</c>, scans ahead for a balanced
    /// <c>&lt;...&gt;</c> of type-ish tokens immediately followed by <c>(</c> or
    /// <c>::</c> — the explicit type arguments of a generic call. Returns the
    /// parsed type arguments with the cursor left after the <c>&gt;</c>; returns
    /// None (restoring the cursor) when the lookahead doesn't match.
    /// </summary>
    let tryLookaheadGenericCallArgs (ctx: ParserContext) : System.Collections.Generic.List<TypeDescriptor> option =
        if not (ctx.Check(TokenType.Less)) then None
        else
            let save = ctx.Cursor
            ctx.Advance() |> ignore // consume '<'
            let mutable depth = 1
            let argTexts = System.Collections.Generic.List<string>()
            let current = System.Text.StringBuilder()

            let mutable result = None

            while (not (ctx.IsAtEnd())) && depth > 0 && Option.isNone result do
                let tok = ctx.Current
                match tok.Type with
                | TokenType.Less ->
                    depth <- depth + 1
                    current.Append('<') |> ignore
                    ctx.Advance() |> ignore
                | TokenType.Greater ->
                    depth <- depth - 1
                    if depth = 0 then
                        if current.Length > 0 then argTexts.Add(current.ToString())
                        ctx.Advance() |> ignore
                        if ctx.Check(TokenType.LParen) || ctx.Check(TokenType.DoubleColon) then
                            result <- Some(System.Collections.Generic.List<TypeDescriptor>(argTexts |> Seq.map TypeDescriptor.Parse))
                        else
                            ctx.Cursor <- save
                            result <- Some(null) // sentinel for "no match"
                    else
                        current.Append('>') |> ignore
                        ctx.Advance() |> ignore
                | TokenType.GreaterGreater ->
                    if depth < 2 then
                        ctx.Cursor <- save
                        result <- Some(null)
                    else
                        depth <- depth - 2
                        current.Append('>') |> ignore
                        ctx.Advance() |> ignore
                        if depth = 0 then
                            if current.Length > 0 then argTexts.Add(current.ToString())
                            if ctx.Check(TokenType.LParen) || ctx.Check(TokenType.DoubleColon) then
                                result <- Some(System.Collections.Generic.List<TypeDescriptor>(argTexts |> Seq.map TypeDescriptor.Parse))
                            else
                                ctx.Cursor <- save
                                result <- Some(null)
                | TokenType.Arrow ->
                    current.Append("->") |> ignore
                    ctx.Advance() |> ignore
                | TokenType.Comma ->
                    if depth = 1 then
                        argTexts.Add(current.ToString())
                        current.Clear() |> ignore
                    else
                        current.Append(", ") |> ignore
                    ctx.Advance() |> ignore
                | _ ->
                    current.Append(tok.Text) |> ignore
                    ctx.Advance() |> ignore

            match result with
            | Some null -> None
            | other -> other
