# Feature Examples

Companion to [FEATURE_PROPOSALS.md](FEATURE_PROPOSALS.md)  one worked example per
proposal, numbered to match its sections. **These use proposed syntax and do not
compile against the current CLI**; they are design targets showing what each
feature looks like from the programmer's seat. Syntax follows the v1.0 spec
wherever a proposal doesn't change it (`Contract X { ... }`, `var/let`,
`fun x -> e`, `<Attr(...)>`, `List<T>`).

## §1 Match expressions

```text
// PROPOSAL §1  value-producing match with or-patterns and guards.

import __builtin.std;

Contract Traffic {
    static fn Main() {
        var status: int = 503;

        // Expression position: feeds an initializer directly.
        let label: string = match (status) {
            0 => "ok",
            1 | 2 => "retryable",            // or-pattern
            n if n >= 500 => "server fault", // guard binds the scrutinee
            _ => "unknown"                   // wildcard
        };
        IO.Println(label);                   // server fault

        // Guard failure falls through to later arms.
        var attempt: int = 2;
        let advice: string = match (attempt) {
            0 => "try now",
            n if n < 3 => "back off",
            _ => "give up"
        };
        IO.Println(advice);                  // back off

        // Statement position still works (arm results popped per @lowering).
        match (label) {
            "ok" => IO.Println("all good"),
            _ => IO.Println("not ok")
        };

        // Composes with pipes.
        status |> describe |> IO.Println;    // meh
    }

    static fn describe(code: int) -> string {
        return match (code) {
            200 => "fine",
            404 => "missing",
            _ => "meh"
        };
    }
}
```

## §2 Sum types (+ exhaustive match)

```text
// PROPOSAL §2  tagged unions give match something to be exhaustive over.

type Shape {
    Circle(radius: double)
  | Rect(w: double, h: double)
  | Unit
}

import __builtin.std;

Contract Areas {
    static fn Main() {
        let shapes: List<Shape> = List.Create();
        List.Add(shapes, Shape.Circle(2.0));
        List.Add(shapes, Shape.Rect(3.0, 4.0));
        List.Add(shapes, Shape.Unit);

        var total: double = 0.0;
        for (s in shapes) {                    // §5 for-in
            total += match (s) {             // destructuring arms (match stage 2)
                Circle(r) => 3.14159 * r * r,
                Rect(w, h) => w * h,
                Unit      => 0.0             // no wildcard needed: compiler-checked
            };
        }
        IO.Println(total);                   // 24.56636
    }
}
```

## §3 `if` as an expression

```text
import __builtin.std;

Contract AbsMax {
    static fn Main() {
        let a: int = -7;
        let b: int = 4;

        let max: int = if (a > b) { a } else { b };
        let abs: int = if (a < 0) { -a } else { a };

        IO.Println(max);   // 4
        IO.Println(abs);   // 7

        // Chains work like statement else-if, but yield.
        let sign: string = if (a > 0) { "+" } else if (a < 0) { "-" } else { "0" };
        IO.Println(sign);  // -
    }
}
```

## §4 Null-safe operators

```text
// PROPOSAL §4  ?. safe navigation and ?? coalesce over existing null.

Contract Profile { email: string; }
Contract User    { profile: Profile; }

Contract Directory {
    static fn Main() {
        var ada: User = new User();
        ada.profile = new Profile();
        ada.profile.email = "ada@example.com";

        IO.Println(ada?.profile?.email ?? "no email");   // ada@example.com

        var anon: User = null;
        IO.Println(anon?.profile?.email ?? "no email");  // no email

        let nums: int[] = null;
        IO.Println(nums?.Length ?? 0);                   // 0
    }
}
```

## §5 `for ... in` iteration

```text
import __builtin.std;

Contract Loops {
    static fn Main() {
        for (i in 0..10) { IO.Print(i); }       // 0123456789   (end-exclusive)
        for (i in 0..=10 by 2) { IO.Print(i); } // 0246810
        IO.Println();

        let names: List<string> = List.Create();
        List.Add(names, "ada");
        List.Add(names, "grace");
        for (name in names) { IO.Println(name); } // no Count/Get juggling

        let scores: Dict<string, int> = Dict.Create();
        Dict.Set(scores, "ada", 99);
        Dict.Set(scores, "grace", 100);
        for (k, v in scores) { IO.Println(k + "=" + Convert.ToString(v)); }
    }
}
```

Lowering note: ranges desugar to the existing C-style `for`; collection loops
lower to index protocol calls on List/Dict/arrays  no runtime changes.

## §6 Interfaces

```text
// PROPOSAL §6  same inheritance slot Attribute uses today.

interface Drawable {
    fn Draw();
    fn Area() -> double;
}

Contract Circle : Drawable {
    radius: double;
    constructor(r: double) { this.radius = r; }   // assumes ctor args land too
    fn Draw() { IO.Println("(o)"); }
    fn Area() -> double { return 3.14159 * this.radius * this.radius; }
}

Contract Square : Drawable {
    side: double;
    constructor(s: double) { this.side = s; }
    fn Draw() { IO.Println("[_]"); }
    fn Area() -> double { return this.side * this.side; }
}


Contract Gallery {
    static fn Main() {
        let wall: List<Drawable> = List.Create();  // interface as type argument
        List.Add(wall, new Circle(1.0));
        List.Add(wall, new Square(2.0));

        for (d in wall) { d.Draw(); }
        // (o)
        // [_]
    }
}
```

## §7 Higher-order stdlib functions

```text
// PROPOSAL §7  built on the delegate machinery that already ships.

import __builtin.std;

Contract Pipeline {
    static fn Main() {
        let nums: List<int> = List.Create();
        List.Add(nums, 1);
        List.Add(nums, 2);
        List.Add(nums, 3);
        List.Add(nums, 4);

        let squares: List<int> = nums |> fun xs -> List.Map(xs, fun x -> x * x);
        let evens: List<int>   = List.Filter(nums, fun x -> x % 2 == 0);
        let sum: int           = List.Reduce(nums, fun (acc, x) -> acc + x, 0);

        IO.Println(List.Count(squares));  // 4
        IO.Println(List.Count(evens));    // 2
        IO.Println(sum);                  // 10
    }
}
```

## §8 Interpolation upgrades

```text
Contract Receipt {
    static fn Main() {
        let item: string = "coffee";
        let price: double = 3.5;
        let qty: int = 2;

        IO.Println("total: {price:F2} ({qty} x {item})");
        // total: 3.50 (2 x coffee)

        IO.Println("{item.ToUpper()} costs {price * qty}");
        // coffee.ToUpper()... no  COFFEE costs 7
        // (full expressions inside {}, not just identifiers)
    }
}
```

Format specifiers: `F2` fixed-point, `X` hex for ints, `,10` right-pad width.
Anything unparseable inside `{}` stays literal text for back-compat.

## §9 Numeric literal forms

```text
Contract Literals {
    static fn Main() {
        let mask: int     = 0xFF00FF00;     // hex
        let bits: int     = 0b10100011;     // binary
        let crowd: int    = 1_000_000;      // digit separators
        let tiny: double  = 1.0e-6;         // exponent notation arrives
        let big: long     = 9_000_000_000L; // L suffix for int64

        IO.Println(mask);    // -16711936
        IO.Println(bits);    // 163
        IO.Println(crowd);   // 1000000
        IO.Println(tiny);    // 0.000001
        IO.Println(big);     // 9000000000
    }
}
```

## §10 Labeled break / continue

```text
Contract Grid {
    static fn Main() {
        var hit: int = 0;

        outer: for i in 0..10 {
            for j in 0..10 {
                if (i * j > 20) { break outer; }   // exits BOTH loops
                if (j == 3)     { continue outer; }
                hit += 1;
            }
        }

        IO.Println(hit);
    }
}
```

Trivial in the current lowering: labels resolve through the loop stack that
break/continue already walks.

## §11 Default and named arguments

```text
Contract Window {
    static fn Open(path: string, readOnly: bool = false, timeoutMs: int = 5000) -> bool {
        return !readOnly && timeoutMs > 0;
    }

    static fn Main() {
        Open("data.txt");                     // all defaults
        Open("data.txt", timeoutMs: 1000);    // skip the middle parameter
        Open("log.txt", readOnly: true);      // named, clearer at call site
    }
}
```

Defaults are recorded once in metadata; named args are reordered by the
compiler at the call site, so the IR call sequence is unchanged.

## §12 Attributes

Shipped  see `examples/AttributesDemo.ct`. Extensions (field applications,
named args, tooling attributes) are sketched in FEATURE_PROPOSALS.md §12.

## §13 Channels

```text
// PROPOSAL §13  typed queues pairing with Thread.Spawn; no locked heap needed.

Contract Workers {
    static fn Main() {
        let ch: Channel<int> = Channel.Create();

        Thread.Spawn(fun () -> {
            var n: int = 0;
            while (n < 5) {
                ch.Send(n);
                n += 1;
            }
            ch.Close();
        });

        for msg in ch { IO.Println(msg); }   // receives until Close
        // 0 1 2 3 4
    }
}
```

Channels live as external objects on the shared heap like List/Dict; the
queue itself is the lock boundary, so the heap stays unlocked.

## §14 `is` type tests

```text
Contract Cat { meow() -> string }
Contract Dog { bark() -> string }

Contract Kennel {
    static fn Sound(pet: object) -> string {
        return match (pet) {
            c is Cat => c.meow(),
            d is Dog => d.bark(),
            _ => "?"
        };
    }
}
```

Lowers to a class-name check on the heap object's handle  the metadata the
runtime already keeps in its type tables.

## §15 Comptime constants

```text
const TICKS_PER_DAY: long = 24 * 60 * 60 * 1000;   // folded at compile time

Contract Config {
    const APP_NAME: string = "contract";

    static fn Main() {
        IO.Println(TICKS_PER_DAY);   // ldc.i8 86400000  no runtime math
    }
}
```

## §16 Design-by-contract clauses

```text
// PROPOSAL §16  requires / ensures / invariant.

Contract Math2 {
    invariant total >= 0;

    total: int;

    fn div(a: int, b: int) -> int
        requires b != 0
        ensures result * b <= a
    {
        return a / b;
    }
}
```

Runtime fault output mirrors VM error reporting:

```text
runtime error: PostconditionFailed: ensures result * b <= a
  ”””€ at Math2.div  [exit]
  ”””€ source line 9:9
```

Requires check on entry, ensures on every exit path (including implicit
zero-value returns), invariants after field writes on the carrying contract.
Off by default under `-O`, on under debug builds unless suppressed with
<Unchecked>.

## §17 Inline IR blocks

```text
// PROPOSAL §17  escape hatch to raw ObjektRT opcodes.
// Now realized: the implemented block statement is `IL { ... }`, not `ir { }`.
// See CONTRACT_LANGUAGE.md > Pointers & Managed Memory > `IL { ... }` inline blocks.

Contract FastMath {
    static fn FreshArray() -> object {
        ir {
            ldc.i4 4
            newarr int32
        }   // block must leave exactly one value, like any expression
    }
}
```

The verifier (existing stack-discipline checks) runs over the block; misuse
is a compile error, not undefined behavior.

## §18 Quote / eval

```text
// PROPOSAL §18  the compiler's AST as a runtime value.

Contract Meta {
    static fn Main() {
        let expr = quote(x + 1);
        IO.Println(expr);          // BinOp(+, Ident(x), Lit(1))

        let env: Dict<string, object> = Dict.Create();
        Dict.Set(env, "x", 41);
        IO.Println(eval(expr, env));   // 42
    }
}
```

Staged: quote (pure data) †’ eval (embedded compiler invocation) †’ macros
(compile-time fns over quoted ASTs). Self-hosting makes step two small: the
pipeline is already a library.

## §19 Runtime delegate retargeting

```text
// PROPOSAL §19  delegates are { target: string, closure }; rewrite target.

Contract Hotfix {
    static fn Main() {
        let handler = fun s -> IO.Println("v1: " + s);

        handler("boot");                       // v1: boot
        Delegate.Retarget(handler, "Hotfix.v2");
        handler("boot");                       // v2: boot  same closure, new body
    }

    static fn v2(s: string) { IO.Println("v2: " + s); }
}
```

One heap write; every thread holding the delegate sees the new target on its
next Invoke. A <Pinned> attribute could forbid retargeting sensitive methods.

## §20 Time-travel debugging

```text
// PROPOSAL §20  snapshot/rewind over state the interpreter already owns.

Contract Undo {
    static fn Main() {
        var score: int = 10;
        let mark = GC.Checkpoint();

        score = 99;
        IO.Println(score);     // 99
        GC.Rewind(mark);
        IO.Println(score);     // 10
    }
}
```

Checkpoint copies statics + heap (byte-buffered objects copy cheaply); Rewind
swaps them back. Also the substrate for debugger scrub bars (ROADMAP: DAP).

## §21 Units of measure

```text
// PROPOSAL §21  F#-style units; algebra lives entirely in semantic analysis.

Contract Trip {
    static fn Main() {
        let dist: double<km> = 120.0<km>;
        let time: double<h>  = 1.5<h>;

        let speed = dist / time;          // inferred double<km/h>
        IO.Println(speed);                // 80

        // let oops = dist + time;       // compile error: km vs h
        let rest: double<min> = 30.0<min>;
        let total_time = time + rest / 60.0<h/min>;   // unit-correct conversion
    }
}
```

The IR never sees anything but float64; unit tags are interned strings on the
type registry entries, erased at lowering.

## §22 Refinement types lite

```text
Contract Signup {
    static fn Register(age: int where age >= 0, name: string where name != "") {
        // compiler tracks age >= 0 through here
    }

    static fn Main() {
        Register(25, "ada");    // ok
        Register(-1, "root");   // compile error: refinement violated
    }
}
```

Guards discharge refinements: after `if (age >= 0) { ... }` the positive fact
holds inside the branch, feeding match guards too.

## §23 Typestates

```text
// PROPOSAL §23  methods callable only in the declared state.

Contract File<state> {
    static fn Open(path: string) -> File<Closed> { ... }
    fn Read(this: File<Closed>) -> string { ... }     // only when Closed
    fn Close(this: File<Closed>) -> File<Open> { ... }
}

Contract Demo {
    static fn Main() {
        let f = File.Open("notes.txt");
        let text = f.Read();       // ok: f is File<Closed>
        let done = f.Close();      // f becomes File<Open>
        // f.Read();               // compile error: Read needs File<Closed>
    }
}
```

Ordinary materialized generics underneath (GENERICS_MATERIALIZATION.md);
the states are phantom parameters erased before lowering.

## §24 Generators / `yield`

```text
// PROPOSAL §24  resumable frames.

Contract Fib {
    static fn fib() yields int {
        var a: int = 0;
        var b: int = 1;
        while (true) {
            yield a;
            let t: int = a + b;
            a = b;
            b = t;
        }
    }

    static fn Main() {
        var taken: int = 0;
        for n in fib() {
            IO.Print(n);
            taken += 1;
            if (taken == 8) { break; }
        }
        IO.Println();          // 011235813
    }
}
```

Locals hoist into a closure-like object; `yield` parks pc + spills, resume
restores them  one new executor op ("park frame"), everything else exists.

## §25 `amb`  nondeterministic evaluation

```text
// PROPOSAL §25  backtrack until constraints hold.

Contract Puzzle {
    static fn Main() {
        let x: int = amb(1, 2, 3);
        let y: int = amb(10, 20, 30);

        assert(x * y == 60);      // search space pruned automatically
        IO.Println(x + ", " + y); // 2, 30
    }
}
```

Fork interpreter state at each `amb`; failed asserts rewind to the most
recent choice point (reuses §20 snapshots). Doubles as the shrinker for §28.

## §26 Guaranteed tail calls

```text
// PROPOSAL §26  recursion that never grows the stack.

Contract Countdown {
    static fn loop(n: int) {
        if (n == 0) { IO.Println("liftoff"); return; }
        loop(n - 1);              // tail call: frame reused
    }

    static fn Main() {
        loop(10_000_000);         // constant stack, runs to completion
    }
}
```

Executor rule: a call whose result is immediately returned replaces the
current frame. Makes recursive folds and parsers free of overflow.

## §27 Custom infix operators

```text
// PROPOSAL §27  declare operator spellings bound to functions.

infix >< = cross at 6;    // precedence tier, multiplicative band

Contract Vec2 {
    x: double;
    y: double;

    static fn cross(a: Vec2, b: Vec2) -> double {
        return a.x * b.y - a.y * b.x;
    }
}

Contract Physics {
    static fn Main() {
        let p = new Vec2();  p.x = 1.0; p.y = 0.0;
        let q = new Vec2();  q.x = 0.0; q.y = 1.0;
        IO.Println(p >< q);   // 1
    }
}
```

Pure desugaring to `Vec2.cross(p, q)`; emoji spellings ride along once the
lexer accepts unicode identifiers/operators.

## §28 Built-in property testing

```text
// PROPOSAL §28  attributes (shipped) + Random (shipped) + shrinking (§25).

<Property(iterations = 1000)>
fn add_commutative(a: int, b: int) {
    assert(add(a, b) == add(b, a));
}

<Property>
fn abs_nonnegative(x: int) {
    let r: int = abs(x);
    assert(r >= 0);           // shrinks toward 0 on failure via amb choice points
}
```

`ccl test` collects <Property>-marked functions, generates arguments from
parameter types via Random, reports the failing seed and minimal shrink.

## §29 Heap introspection

```text
// PROPOSAL §29  the language inspects its own running state.

Contract Probe {
    static fn Main() {
        var keep: List<int> = List.Create();

        for obj in GC.Objects() {
            match (obj) {
                o is List<int> => IO.Println("list, size " + List.Count(o)),
                _ => ()
            };
        }

        IO.Println(GC.Reachable(keep));   // who holds references to this handle
    }
}
```

Combined with metaclasses (spec future work): heap-graph queries as match
patterns, leak detection, live object census  all in-language.
