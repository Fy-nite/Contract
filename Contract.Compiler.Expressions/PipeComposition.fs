namespace Contract.Compiler.Expressions.Internal

open Contract.Compiler.AST
open Contract.Compiler.Parsing

/// <summary>
/// Pipe (<c>|&gt;</c>) and composition (<c>&gt;&gt;</c>) operators, plus
/// implicit lambda wrapping (<c>_</c>/<c>@</c> marker discovery).
/// </summary>
[<AutoOpen>]
module PipeComposition =

    let isImplicitMarker (name: string) = name = "_" || name = "@"

    /// <summary>Finds the first free <c>_</c>/<c>@</c> identifier in an expression tree.</summary>
    let rec findImplicitMarker (expr: Expression) : string option =
        match expr with
        | :? IdentifierExpression as id when isImplicitMarker id.Name -> Some id.Name
        | :? CallExpression as c ->
            c.Arguments
            |> Seq.tryPick (fun a -> findImplicitMarker a)
        | :? MemberExpression as m -> findImplicitMarker m.Object
        | :? BinaryExpression as b ->
            match findImplicitMarker b.Left with
            | Some _ as s -> s
            | None -> findImplicitMarker b.Right
        | :? UnaryExpression as u -> findImplicitMarker u.Operand
        | :? IndexExpression as ix ->
            match findImplicitMarker ix.Target with
            | Some _ as s -> s
            | None -> findImplicitMarker ix.Index
        | :? PipeExpression as p ->
            match findImplicitMarker p.Left with
            | Some _ as s -> s
            | None -> findImplicitMarker p.Right
        | :? ArrayLiteralExpression as al ->
            al.Elements |> Seq.tryPick (fun e -> findImplicitMarker e)
        | _ -> None

    /// <summary>
    /// Wraps an expression containing a free <c>_</c>/<c>@</c> into an implicit
    /// lambda: <c>_ * 2</c> becomes <c>fun _ -&gt; _ * 2</c>. No-op for lambdas
    /// and for expressions without a marker.
    /// </summary>
    let applyImplicitLambda (ctx: ParserContext) (expr: Expression) (line: int) (column: int) : Expression =
        if expr :? LambdaExpression then expr
        else
            match findImplicitMarker expr with
            | None -> expr
            | Some marker ->
                LambdaExpression(
                    System.Collections.Generic.List<string>([ marker ]),
                    null,
                    expr,
                    null,
                    line,
                    column
                ) :> Expression

    /// <summary>f &gt;&gt; g — composition. Lowers to <c>fun x -&gt; g(f(x))</c>.</summary>
    let compose
        (ctx: ParserContext)
        (host: IParserHost)
        (left: Expression)
        (right: Expression)
        (line: int)
        (column: int)
        : Expression =
        if left :? LambdaExpression || right :? LambdaExpression then
            ctx.AddError("Composition operands must be named functions (no lambdas in v1)", line, column)
            PipeExpression(left, right, line, column) :> Expression
        else
            let x = IdentifierExpression("__compose_arg", line, column)
            let inner = CallExpression(left, line, column)
            inner.Arguments.Add(x)

            let body =
                match right with
                | :? CallExpression as rightCall ->
                    rightCall.Arguments.Insert(0, inner)
                    rightCall :> Expression
                | _ ->
                    let outer = CallExpression(right, line, column)
                    outer.Arguments.Add(inner)
                    outer :> Expression

            LambdaExpression(
                System.Collections.Generic.List<string>([ "__compose_arg" ]),
                null,
                body,
                null,
                line,
                column
            ) :> Expression

    /// <summary>
    /// <c>left |&gt; right</c> — rewrites the RHS so the piped value lands where
    /// the user expects.
    /// </summary>
    let buildPipe
        (ctx: ParserContext)
        (host: IParserHost)
        (left: Expression)
        (right: Expression)
        (line: int)
        (column: int)
        : Expression =
        if right :? LambdaExpression then
            PipeExpression(left, right, line, column) :> Expression
        else
            match right with
            | :? CallExpression as call ->
                let mutable hole = -1
                let mutable i = 0
                while i < call.Arguments.Count do
                    let arg = call.Arguments.[i]
                    match arg with
                    | :? IdentifierExpression as holeId when isImplicitMarker holeId.Name ->
                        if hole >= 0 then
                            ctx.AddError("A pipe target can only use '_' as the value's spot once", arg.Line, arg.Column)
                        else
                            hole <- i
                        call.Arguments.RemoveAt(i)
                        // don't increment i
                    | _ ->
                        call.Arguments.[i] <- applyImplicitLambda ctx arg arg.Line arg.Column
                        i <- i + 1
                call.Arguments.Insert((if hole >= 0 then hole else 0), left)
                call :> Expression

            | :? IdentifierExpression | :? MemberExpression | :? ScopedAccessExpression ->
                let call2 = CallExpression(right, line, column)
                call2.Arguments.Add(left)
                call2 :> Expression

            | _ ->
                PipeExpression(left, applyImplicitLambda ctx right line column, line, column) :> Expression
