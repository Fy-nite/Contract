# Feature Proposals — post-1.0

Status: **proposed** (2026-08-22). Worked examples for every proposal:
[FEATURE_EXAMPLES.md](FEATURE_EXAMPLES.md)

Candidate features for Contract after 1.0, collected from language review.

Shipped since this document was written: §1 match expressions, §2 sum types
with compile-time exhaustive checking, §3 if-as-expression, §5 for-in
iteration (parenthesized headers: `for (x in xs)`, `for (k, v in d)`),
§6 interfaces via contract multiple inheritance (no new keyword), and
§7 List.Map/Filter/Reduce. Builtin modules are no longer implicitly global —
they live under a reserved `__builtin.std.` root reachable by import or full
qualification, and user contracts shadow same-named builtins. See
tests/success for working programs.

---

## 1. Match expressions (priority) — SHIPPED

A value-producing, exhaustive multi-way branch. Supersedes statement-only
`switch`: same role, but usable anywhere an expression is expected.

```text
let label = match (status) {
    0 => "ok",
    1 | 2 => "retryable",
    n if n >= 100 => "server error",
    _ => "unknown"
};
```

### Motivation

- `switch` is currently a statement; its result cannot feed an initializer,
  argument, or pipe stage.
- Case heads are limited to single integer/string literals — no multi-values,
  no guards, no catch-all beyond `else:`.
- The delegate/closure machinery already produces expression-oriented code;
  a branch construct that yields values matches the rest of the language.

### Syntax

Extends the existing `switch` shape rather than replacing it: braces, comma-
or-newline-separated arms, `=>` separating pattern from result, `_` as the
wildcard (the `else:` spelling stays valid for back-compat inside statements).

```ebnf
match_expr    ::= 'match' '(' expression ')' '{' match_arm+ '}'
match_arm     ::= match_pattern ('if' expression)? '=>' expression ','
match_pattern ::= literal
                | literal ('|' literal)+          // or-pattern
                | identifier                       // binding (stage 2)
                | '_'                              // wildcard
```

### Semantics

1. The scrutinee is evaluated exactly once.
2. Arms are tried top to bottom; the first pattern that matches wins.
3. A guard (`if cond`) re-tests the *matched* arm; failure falls through to
   later arms.
4. Or-patterns are sugar for repeated comparison against the same scrutinee.
5. Exhaustiveness: without a wildcard/binding final arm, non-matching input
   is a runtime fault in stage 1 (compile-time exhaustiveness checking needs
   sum types — see §2).
6. The whole `match` is an expression: it lowers to a value on the stack like
   every other Contract expression (statement position still emits the
   trailing `pop` per the lowering model).

### Lowering sketch

Decision chain against the ObjektIR stack machine — scrutinee evaluated once,
then compared per arm:

```text
// match (x) { 1 => a, 2 => b, _ => c }
<scritinee>              // once
dup; ldc.i4 1; ceq; brfalse L1
pop                      // consume scrutinee copy
<a>
br Lend
L1: dup; ldc.i4 2; ceq; brfalse L2
pop
<b>
br Lend
L2: pop                  // wildcard consumes the last copy
<c>
Lend:
```

Guards compile to a nested test between the `ceq` and the arm body; guard
failure branches to the next arm's head (re-duplicating the saved scrutinee).
String cases use `call String.Equals` instead of `ceq`. This mirrors the
existing `while (stack)` convention: every path through the chain must leave
exactly one value.

### Staging

- **Stage 1** — literal/or-pattern/wildcard arms with guards, expression form.
  Pure compiler work; no runtime changes.
- **Stage 2** — binding patterns (`n if n > 10`) and struct destructuring
  (`Point(x, y)`); needs ldfld sequences into locals.
- **Stage 3** — exhaustiveness checking, once sum types exist to be
  exhaustive over.

---

## 2. Sum types (+ exhaustive match) — SHIPPED

Tagged unions to give match something to be exhaustive over:

```text
type Shape {
    Circle(radius: double)
  | Rect(w: double, h: double)
}
```

Each variant becomes a contract with a shared tag; constructors lower to
`newobj` of the variant class. Pairs naturally with match stage 3.

Shipped as parse-time synthesis into plain contracts: the base carries the
hidden `__tag` field plus one static factory per variant (`Shape.Circle(2.0)`,
usable bare as `Shape.Unit` for fieldless variants), and each variant becomes
a contract deriving the base whose constructor stores its fields and stamps
the tag. The analyzer checks exhaustiveness: a match over a sum type with no
wildcard/binding catch-all must cover every variant or it is a compile error.
Variant arms destructure fields positionally (`Rect(w, h) => w * h`) and
compose with guards.

## 3. `if` as an expression — SHIPPED

Same treatment as match: `let max = if (a > b) { a } else { b };`. Cheap once
match lands — both share the "branch yields a value" lowering.

## 4. Null-safe operators

`?.` safe navigation and `??` coalesce. `null` already exists in the language;
these are the ergonomic complement and cheaper than retrofitting option types.

## 5. `for ... in` iteration — SHIPPED

`for x in 0..10` and `for item in list`. Removes manual `Count`/`Get` index
juggling over collections; range lowering is desugared to the existing C-style
loop, collection iteration goes through an index protocol on List/Dict/arrays.
Shipped with `..=` (inclusive) and `by step`, plus the `(k, v)` pair form over
Dict.

## 6. Interfaces — SHIPPED (as contract multiple inheritance)

Named method sets a contract can satisfy; the instance-method machinery
(fields ⇒ implicit receiver) is most of the prerequisite. Enables generics
constraints later (`where T : Drawable`).

Shipped without an `interface` keyword: an ordinary contract whose instance
methods lack bodies IS the interface, and a deriving contract lists it after
the primary base — `Contract Circle : Drawable, Named { ... }`. Interface
parents must be stateless (no fields, no constructors); every abstract method
they declare must be implemented or the contract cannot be instantiated.
Instance calls lower to `callvirt`, and the runtime refines the target through
the RECEIVER's concrete type chain (base chain + interfaces), so base- and
interface-typed variables dispatch to the most-derived override.

## 7. Higher-order stdlib functions — SHIPPED

`List.Map/Filter/Reduce` built directly on the shipped delegate mechanism
(`Delegates.ct` exercises everything required today). Host-side natives invoke
the Contract delegate per element via `InvokeDelegate`.

## 8. Interpolation upgrades

Full expressions in `{...}` plus format specifiers (`{price:F2}`), replacing
the identifier-only restriction.

## 9. Numeric literal forms

Hex (`0xFF`), binary (`0b1010`), digit separators (`1_000_000`), exponent
floats (`1e10`) — all explicitly absent in 1.0.

## 10. Labeled break/continue

`break outer;` for nested loops; trivial in the existing break/continue
lowering (labels already resolve through the loop stack).

## 11. Default and named arguments

Parameter defaults recorded in metadata; named args reorder at the call site.

## 12. Attribute extensions

Already shipped: VB-style `<Name(args)>` applications before declarations;
attribute types declared as contracts inheriting the built-in `Attribute`
base (`Contract Author : Attribute { ... }`); arity validation; round-trip
through ORBT; reflection surfacing via `GetAttributes()`; built-ins include
`DllImport`, `ClrImport`, `NativeBinding`. Remaining extensions:

- field-level applications (currently unsupported),
- named attribute arguments,
- attribute-driven tooling — e.g. `<Test>` / `<Property>` marking functions
  the CLI collects into `ccl test`,
- codegen hooks: attributes the compiler expands into boilerplate rather than
  merely annotating (pairs with §15).

## 13. Channels

Typed queues pairing with `Thread.Spawn`, giving cross-thread communication
without locking the shared heap (heap stays unlocked by design in 1.x).

## 14. `is` type tests

`obj is Counter` — `object` handles are opaque today; there is no way to ask
what a handle actually is. Lowers to a tag/class-name check on the heap object.

## 15. Comptime constants

Compile-time evaluation of `const` declarations; supports self-hosting goals
without a macro system.

---

# Whacky ideas

Higher-risk / higher-delight candidates. Each leans on something the runtime
or compiler already does, so none require new infrastructure — just nerve.

## 16. Design-by-contract clauses

The language is named Contract and has no contracts. Add `requires` /
`ensures` / `invariant`:

```text
fn div(a: int, b: int) -> int
    requires b != 0
    ensures result >= 0
{
    return a / b;
}
```

Lowering is injected branch-fault code at function entry (requires), exit
(ensures), and field writes on `invariant`-carrying contracts. Debug-builds
only via flag, or always-on with an opt-out attribute. Enormous branding
payoff for modest compiler work.

## 17. Inline IR blocks

An escape hatch dropping raw ObjektRT opcodes mid-function:

```text
ir {
    dup
    ldc.i4 4
    newarr int32
}
```

Feasible only because the lowering model is documented and stable; the block
must still satisfy the stack-discipline conventions (@lowering). The
compiler's own peephole/verifier becomes the safety net.

## 18. Quote / eval (homoiconicity)

Expose the compiler's own AST as a runtime type:

```text
let ast = quote(x + 1);
let two = eval(ast, env);   // 2
```

Self-hosting is the plan, which makes this natural: the pipeline is already
in-process, and `lisp.orbt` in the repo root proves the IR can carry this.
Stage it: `quote` first (pure data), `eval` second (invokes the embedded
compiler), macros third (compile-time functions over quoted ASTs).

## 19. Runtime delegate retargeting

Delegates are `{ target: string, closure }` objects in the shared heap, so
the target string can be rewritten at runtime: every existing closure value
instantly points elsewhere. This is a hot-reload / live-patching primitive;
a `<Hotswappable>` attribute could mark the methods allowed to be retargeted.

## 20. Time-travel debugging

The interpreter owns the heap, value stack, and frames — snapshotting every N
instructions gives rewind nearly free in a VM we control end-to-end:

```text
GC.Checkpoint()      // returns a handle to a full state snapshot
GC.Rewind(handle)    // restore it
```

Also the substrate for a debugger scrub bar (DAP work in ROADMAP).

## 21. Units of measure

F#'s trick, cheap because types are interned strings:

```text
let d: double<km> = 42.0<km>;
let t: double<h> = 0.5<h>;
let v = d / t;              // double<km/h>
let bad = d + t;            // compile error
```

Unit algebra happens entirely in the semantic analyser; the IR never sees
anything but `float64`.

## 22. Refinement types lite

`let age: int where age >= 0` — checked at assignment and argument passing,
discharged by guards feeding match arms (`if age >= 0 { ... }`). A subset of
§16's clauses lifted into the type system.

## 23. Typestates

Methods callable only in certain states, encoded in the type of the receiver:
`File.Open(p)` yields `File<Open>` whose `.Read()` yields `File<Read>`;
calling `.Read()` twice or after `.Close()` is a compile error. Lowering is
ordinary contracts plus phantom type parameters (materialization machinery
from GENERICS_MATERIALIZATION.md applies).

## 24. Generators / `yield`

`fn fib() yields int` compiles to a resumable frame: locals hoisted into a
closure-like object, `yield` records pc + spills, resume restores them. The
delegate mechanism already models the object half; the executor needs a
"park frame" operation.

## 25. `amb` — nondeterministic evaluation

```text
let x = amb(1, 2, 3);
assert(x * x == 9);         // x backtracks to 3
```

Classic SICP amb evaluator: fork the interpreter state at each `amb`, fail
backtracks to the last choice point. Peak whacky, genuinely useful for
puzzle solvers and property-test shrinking (§28).

## 26. Guaranteed tail calls

Recursion-as-loop in the executor: when a call is in tail position and the
frame's locals die, reuse the frame instead of pushing. Pairs with the
functional influences; makes §24 generators and persistent recursion cheap.

## 27. Custom infix operators

`infix >< = cross;` declaring operator spellings bound to functions, with a
declared precedence tier. Emoji operators ride along free once identifiers
are unicode-aware. Deeply unserious; endlessly memorable demos.

## 28. Built-in property testing

```text
<Property>
fn add_commutative(a: int, b: int) {
    assert(add(a, b) == add(b, a));
}
```

Random generation from parameter types via the existing `Random` module,
shrinking via §25's choice points, collection into `ccl test` via §12's
attribute-driven tooling. Three existing systems compose into one feature.

## 29. Heap introspection

`GC.Objects()` returning queryable live handles; combined with metaclasses
(already future work in the spec) the language could inspect its own running
state: who holds a reference to this object, what type is at handle N,
heap-graph queries as match patterns.
