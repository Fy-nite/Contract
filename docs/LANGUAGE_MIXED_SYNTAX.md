# Contract — Mixed OOP + Functional Syntax Design

Goal
----
Allow Contract source files to mix C#-like OOP declarations (Contracts, `fn` methods, fields) and F#-style functional expressions and declarations in the same file. Provide clear rules for parsing and semantics so both styles interoperate naturally.

Overview & Motivation
---------------------
- Contract is primarily OOP/C#-like: `Contract` types, fields, methods (`fn`), dot-qualified calls (e.g., `IO.println`), and `var` declarations.
- You want to also allow functional-style code (lightweight expression syntax, piping, pattern matching, let-bindings) in the same file, enabling succinct functional idioms where appropriate.
- Key constraint: a single parser (or coordinated parsers) must accept both syntaxes in context-sensitive ways without ambiguity. Diagnostics and tool support must remain accurate.

Design principles
-----------------
1. Contextual parsing: the syntax recognized depends on the surrounding construct. Inside a `fn` body or top-level module body, both expression syntaxes are allowed. Declarations (contracts, fields, fn headers) follow OOP syntax.
2. Minimal new syntax: reuse existing token set where possible. Add only a small set of functional sugar: `let` bindings, `|>` pipe operator, `match`/`with` pattern matching, lambda syntax `fun x -> expr`, and optional infix operators for composition.
3. Deterministic parsing: ensure precedence & associativity are well-defined and parsing decisions are local (no global backtracking across large ranges).
4. Interop semantics: functional constructs compile to the same IL/VM model — lambdas become synthetic functions/closures capturing locals, `let` maps to local vars, `match` compiles to switch-like bytecode.

Proposed syntax additions
-------------------------
- let binding (expression or statement position):
  - `let x = expr` (creates a local named `x`), optionally `let rec` for recursion.
  - `let x: Int = expr` allowed
- lambda: `fun x -> expr` (single-arg lambdas), `fun (x,y) -> expr` for tuples.
- pipe operator: `expr |> fn` (rewrites to `fn(expr)` during parsing/IR lowering)
- match expression (expression position):
  - `match expr with | pattern -> expr | pattern -> expr ...` where patterns are integer literals, identifiers, or `_` wildcard.
- sequence expressions: `expr; expr` is already present; `let` and `match` are expression-first where useful.

Parsing strategy
----------------
Option A (recommended for speed):
- Implement a single grammar (nearley/PEG) that includes both OOP and functional constructs with contextual precedence rules (e.g., `let` recognized in expression/statement positions).
- Use the parser in the LSP for editor features and in the C compiler later (optionally by generating a C parser from the grammar or porting the grammar logic to the C parser module).

Option B (native C parser):
- Extend recursive-descent parser: expand `parse_expr` to recognize `let`, `fun`, `match`, `|>` and `|` alternatives; treat these constructs only inside function bodies or top-level expression areas.
- Ensure `parse_primary` handles `fun` and `match` and `let` as either expressions or statements depending on position.

Ambiguities and resolution
--------------------------
- `let` vs `var`: both declare locals; adopt rule: `var` remains statement-level mutable declaration; `let` is expression-level and immutable by default (`let mut` can be added later).
- `fn (...)` vs `fun`: `fn` is function declaration; `fun` is lambda. They are distinct tokens.
- Pipe operator precedence: `|>` is low-precedence, right-associative.
- Pattern matching vs switch: `match` is expression-first and can destructure; `switch` remains statement-level for legacy code.

Runtime/IL changes
------------------
- Lambdas: represent as closures capturing environment. Implementation choices:
  - Simple approach: compile `fun` to a method with synthetic name and capture variables as additional hidden parameters (no heap-allocated closure object). The caller/owner must pass captured values when calling the closure.
  - Future: add first-class closure objects on the heap. For v1, the synthetic-method approach keeps VM simple.
- `let` -> local variable allocation (similar to `var`). Map `let` to local_map entries during emission.
- `match` -> lowered to equality checks and jump chains (like switch) or to a sequence of comparisons.

Examples
--------
Functional style inside fn body:

```
fn main() {
  let inc = fun x -> x + 1;
  let nums = [1,2,3];
  let doubled = nums |> map (fun x -> x * 2);
  IO.println("done");
}
```

Pattern matching example:
```
let sign x = match x with
  | 0 -> 0
  | n when n > 0 -> 1
  | _ -> -1
```

Interop with OOP members:
```
Contract Program {
  fn main() {
    let msg = "Hello";
    IO.println(msg |> String.toUpper);
  }
}
```