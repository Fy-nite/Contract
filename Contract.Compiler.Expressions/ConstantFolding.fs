namespace Contract.Compiler.Expressions.Internal

open Contract.Compiler.AST
open Contract.Compiler.Parsing

/// <summary>
/// Compile-time constant folding (FEATURE_PROPOSALS §15).
/// Folds literals, arithmetic, comparison, logical operators, and references
/// to constants declared earlier in the same contract.
/// </summary>
[<AutoOpen>]
module ConstantFolding =

    let private constValueTypeName (value: obj option) =
        match value with
        | None -> null
        | Some (:? int) -> "int"
        | Some (:? double) -> "double"
        | Some (:? string) -> "string"
        | Some (:? bool) -> "bool"
        | _ -> null

    let private constantTypeMatches (annotation: string) (value: obj option) =
        let actual = constValueTypeName value
        if actual = null then true // null folds / unknown kinds: skip
        else annotation = actual

    /// <summary>Folds a binary operator applied to two constant values.</summary>
    let rec foldConstantBinary (op: string) (left: obj option) (right: obj option) : obj option * bool =
        match op with
        | "==" | "!=" ->
            let equal =
                match left, right with
                | Some (:? int as a), Some (:? double as b) -> (double a) = b
                | Some (:? double as a), Some (:? int as b) -> a = (double b)
                | Some (:? int as a), Some (:? int as b) -> a = b
                | Some (:? double as a), Some (:? double as b) -> a = b
                | Some (:? string as a), Some (:? string as b) -> a = b
                | Some (:? bool as a), Some (:? bool as b) -> a = b
                | None, None -> true
                | _ -> false
            let result = if op = "==" then equal else not equal
            (Some (box result), true)

        | "<" | "<=" | ">" | ">=" ->
            let cmp =
                match left, right with
                | Some (:? int as a), Some (:? int as b) -> a.CompareTo(b)
                | Some (:? double as a), Some (:? double as b) -> a.CompareTo(b)
                | Some (:? int as a), Some (:? double as b) -> (double a).CompareTo(b)
                | Some (:? double as a), Some (:? int as b) -> a.CompareTo(double b)
                | Some (:? string as a), Some (:? string as b) -> a.CompareTo(b)
                | _ -> 0
            let result =
                match op with
                | "<" -> cmp < 0
                | "<=" -> cmp <= 0
                | ">" -> cmp > 0
                | ">=" -> cmp >= 0
                | _ -> false
            (Some (box result), true)

        | "+" ->
            match left, right with
            | Some (:? int as a), Some (:? int as b) -> (Some (box (a + b)), true)
            | Some (:? double as a), Some (:? double as b) -> (Some (box (a + b)), true)
            | Some (:? int as a), Some (:? double as b) -> (Some (box (double a + b)), true)
            | Some (:? double as a), Some (:? int as b) -> (Some (box (a + double b)), true)
            | Some (:? string as a), Some (:? string as b) -> (Some (box (a + b)), true)
            | _ -> (None, false)

        | "-" ->
            match left, right with
            | Some (:? int as a), Some (:? int as b) -> (Some (box (a - b)), true)
            | Some (:? double as a), Some (:? double as b) -> (Some (box (a - b)), true)
            | Some (:? int as a), Some (:? double as b) -> (Some (box (double a - b)), true)
            | Some (:? double as a), Some (:? int as b) -> (Some (box (a - double b)), true)
            | _ -> (None, false)

        | "*" ->
            match left, right with
            | Some (:? int as a), Some (:? int as b) -> (Some (box (a * b)), true)
            | Some (:? double as a), Some (:? double as b) -> (Some (box (a * b)), true)
            | Some (:? int as a), Some (:? double as b) -> (Some (box (double a * b)), true)
            | Some (:? double as a), Some (:? int as b) -> (Some (box (a * double b)), true)
            | _ -> (None, false)

        | "/" ->
            match left, right with
            | Some (:? int as a), Some (:? int as b) when b <> 0 -> (Some (box (a / b)), true)
            | Some (:? double as a), Some (:? double as b) when b <> 0.0 -> (Some (box (a / b)), true)
            | Some (:? int as a), Some (:? double as b) when b <> 0.0 -> (Some (box (double a / b)), true)
            | Some (:? double as a), Some (:? int as b) when b <> 0 -> (Some (box (a / double b)), true)
            | _ -> (None, false)

        | "%" ->
            match left, right with
            | Some (:? int as a), Some (:? int as b) when b <> 0 -> (Some (box (a % b)), true)
            | Some (:? double as a), Some (:? double as b) when b <> 0.0 -> (Some (box (a % b)), true)
            | Some (:? int as a), Some (:? double as b) when b <> 0.0 -> (Some (box (double a % b)), true)
            | Some (:? double as a), Some (:? int as b) when b <> 0 -> (Some (box (a % double b)), true)
            | _ -> (None, false)

        | _ -> (None, false)

    /// <summary>
    /// Folds a compile-time constant expression. Returns true when the
    /// expression is a foldable constant and sets <c>value</c>.
    /// </summary>
    let rec tryFoldConstant
        (ctx: ParserContext)
        (expr: Expression)
        (contract: ContractDeclaration)
        (host: IParserHost)
        : bool * obj option =
        match expr with
        | :? LiteralExpression as lit -> (true, Some lit.Value)

        | :? IdentifierExpression as id ->
            let field =
                contract.Fields
                |> Seq.tryFind (fun f -> f.IsConst && f.IsStatic && f.Name = id.Name)
            match field with
            | None -> (false, None)
            | Some f -> (true, f.ConstantValue |> Option.ofObj)

        | :? UnaryExpression as unary ->
            let (ok, operand) = tryFoldConstant ctx unary.Operand contract host
            if not ok then (false, None)
            else
                match unary.Operator, operand with
                | "-", Some (:? int as i) -> (true, Some (box (-i)))
                | "-", Some (:? double as d) -> (true, Some (box (-d)))
                | "!", Some (:? bool as b) -> (true, Some (box (not b)))
                | _ -> (false, None)

        | :? BinaryExpression as bin ->
            // Short-circuit forms
            if bin.Operator = "&&" || bin.Operator = "||" then
                let (okL, lhs) = tryFoldConstant ctx bin.Left contract host
                if not okL then (false, None)
                else
                    match lhs with
                    | Some (:? bool as leftBool) ->
                        if (bin.Operator = "&&" && not leftBool) || (bin.Operator = "||" && leftBool) then
                            let result = bin.Operator = "&&" |> not
                            (true, Some (box result))
                        else
                            let (okR, rhs) = tryFoldConstant ctx bin.Right contract host
                            if not okR then (false, None)
                            else
                                match rhs with
                                | Some (:? bool as rightBool) ->
                                    let result =
                                        if bin.Operator = "&&" then leftBool && rightBool
                                        else leftBool || rightBool
                                    (true, Some (box result))
                                | _ -> (false, None)
                    | _ -> (false, None)
            else
                let (okL, left) = tryFoldConstant ctx bin.Left contract host
                let (okR, right) = tryFoldConstant ctx bin.Right contract host
                if not okL || not okR then (false, None)
                else
                    let (value, ok) = foldConstantBinary bin.Operator left right
                    (ok, value)

        | _ -> (false, None)

    /// <summary>
    /// Best-effort type inference from an expression for contract-scope
    /// lambda desugaring. Returns null when the type cannot be inferred.
    /// </summary>
    let rec inferTypeFromExpression (expr: Expression) : string =
        match expr with
        | :? LiteralExpression as lit ->
            match lit.Value with
            | :? int -> "int"
            | :? double -> "double"
            | :? string -> "string"
            | :? bool -> "bool"
            | _ -> null
        | :? BinaryExpression as b -> inferTypeFromExpression b.Left
        | :? UnaryExpression as u -> inferTypeFromExpression u.Operand
        | :? IdentifierExpression as id -> id.Name
        | _ -> null
