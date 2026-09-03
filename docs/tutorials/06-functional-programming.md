# Chapter 6: Functional Programming

> **You'll learn:**
>
> - anonymous functions with `fun`,
> - the pipe operator `|>` and why it reads better than nesting,
> - implicit lambdas with `_` and pipe holes,
> - range literals `1..5`,
> - function composition with `>>`.

Contract has lambdas and a pipe operator for a functional style.

## Lambdas with `fun`

A lambda is an anonymous function. The syntax is
`fun parameter -> expression`:

```ct
let inc = fun x -> x + 1;
```

The body is a single expression; the result is the lambda's return value.

```ct
Contract Program {
    static fn Main() {
        let inc = fun x -> x + 1;
        IO.Println(inc(5));   // 6
    }
}
```

## The pipe operator `|>`

`left |> right` passes the left value as the argument to the right function:

```ct
let doubled = 5 |> double;   // double(5)
```

This chains nicely:

```ct
fn addOne(x: int) -> int {
    return x + 1;
}

fn square(x: int) -> int {
    return x * x;
}

static fn Main() {
    let result = 3 |> addOne |> square;  // (3 + 1)^2 = 16
    IO.Println(result);
}
```

Pipes work with lambdas too:

```ct
let result = 10 |> fun x -> x + 5;  // 15
```

### Piping into modules and statics

The pipe target can be any callable — a standard library module member, a
contract's static method, or a `::` member — and the piped value becomes its
first argument:

```ct
"hello" |> String.ToUpper |> IO.Println;   // HELLO
"hi"    |> String.Concat(_, "!");          // hi!  (see holes below)
```

### Implicit lambdas with `_`

A bare `_` (or `@`) anywhere an expression is expected turns the surrounding
expression into a lambda — the marker is the parameter. In a pipe RHS:

```ct
let sq = 7 |> _ * _;    // 49 — same as 7 |> fun _ -> _ * _
```

In a `let` initializer:

```ct
let inc = _ + 1;        // same as fun _ -> _ + 1
IO.Println(10 |> inc);  // 11
```

> **Note** — `_` and `@` are reserved as the implicit parameter. The marker is
> always the first `_`/`@` the compiler finds in the expression, so
> `_ * _` uses the same parameter twice, and nested calls work:
> `"hi" |> String.ToUpper(_)` is `String.ToUpper("hi")`.

### Holes: where the piped value lands

Inside a **call** on the pipe RHS, a bare `_` argument marks *where* the piped
value goes — so the value can be the 2nd, 3rd, ... argument:

```ct
let padded = "hi" |> String.Concat(_, "!");  // hi!  — value in slot 0
let stamp  = "5" |> String.Concat("x", _);   // x5   — value in slot 1
```

Without a hole, the piped value is simply prepended:

```ct
let result = 3 |> Add(4);   // Add(3, 4)
```

Compound `_` expressions inside call arguments become lambdas instead:

```ct
let evens = [1,2,3,4] |> Array.Map(_ * 2);   // needs Array.Map — see below
```

## Range literals

`a..b` is an inclusive integer range. It lowers to an array literal, so you
can index, iterate, or pipe it:

```ct
let nums = 1..5;       // [1, 2, 3, 4, 5]
IO.Println(nums.Length);   // 5
IO.Println(nums[0]);       // 1

let rev = 3..1;        // [3, 2, 1] — descending works too
```

> **Note** — v1 ranges need integer-literal bounds; `1..n` with a variable
> will be supported once higher-order array functions land.

## Composition with `>>`

`f >> g` composes two functions into one: `fun x -> g(f(x))`. The result is
just a lambda, so you can assign it, pipe into it, or compose further:

```ct
fn double(x: int) -> int { return x * 2; }
fn addOne(x: int) -> int { return x + 1; }

static fn Main() {
    let dblThenInc = double >> addOne;
    IO.Println(4 |> dblThenInc);        // 9

    let lenOfUpper = String.ToUpper >> String.Length;
    IO.Println(lenOfUpper("hey"));      // 3
}
```

## Delegate types

A lambda is a first-class value, and its type can be named explicitly. The
`<>`-bracketed inline generic `Delegate<(params) -> return>` is the named form;
the bare function type `(params) -> return` is interchangeable anywhere a
delegate is expected.

```ct
// A function-typed field, assigned in the constructor and called later.
Contract Box {
    public add: Delegate<(int, int) -> int>;

    constructor() {
        this.add = fun (x: int, y: int) -> {
            return x + y;
        };
    }

    fn Sum(a: int, b: int) -> int {
        return this.add(a, b);   // invoke the stored delegate
    }
}

static fn apply(f: Delegate<(int, int) -> int>, x: int, y: int) -> int {
    return f(x, y);
}

static fn Main() {
    let add = fun (x: int, y: int) -> {
        return x + y;
    };
    IO.Println(apply(add, 3, 4));   // 7 (passed as a value)
    IO.Println((new Box()).Sum(10, 32)); // 42 (via a field)
}
```

Delegates can be stored in fields, passed as arguments, returned from
functions, and invoked immediately from a call — `makeAdd()(5, 5)` — including
when the function's declared return type is `Delegate<(...) -> ...>`.

## Why bother?

Pipes read left-to-right, matching how you'd describe the data flow:
"take 3, add one, then square it." Nested calls read inside-out:

```ct
// Compare:
let a = square(addOne(3));      // 16, inside-out
let b = 3 |> addOne |> square;  // 16, left-to-right
```

> **Tip** — reach for `|>` whenever you chain more than one transform. It
> reads in the order the work happens.

> **Coming soon** — higher-order array functions (`Array.Map`, `Filter`,
> `Fold`) that turn ranges and pipes into full data pipelines. They need a
> bridge so VM lambdas can cross into the standard library; once that lands,
> `1..10 |> Array.Map(_ * 2)` works as written.

## Summary

- `fun x -> expr` is an anonymous single-expression function.
- `a |> f` calls `f(a)`; chains read left-to-right.
- `_` / `@` anywhere an expression goes = implicit lambda parameter.
- A bare `_` in a piped call marks where the value lands.
- `a..b` is an inclusive integer array range.
- `f >> g` composes: `fun x -> g(f(x))`.

## Exercise

Write a function `halve(x: int) -> int` returning `x / 2`. In `Main`, pipe
`100` through `halve` twice and print the result (should be 25). Then rewrite
the pipe with composition, and redo the whole thing using an implicit `_`
lambda (`100 |> _ / 2 |> _ / 2`).

<details>
<summary>Solution</summary>

```ct
Contract Program {
    fn halve(x: int) -> int {
        return x / 2;
    }

    static fn Main() {
        let result = 100 |> halve |> halve;
        IO.Println(result);  // 25

        let halveTwice = halve >> halve;
        IO.Println(4 |> halveTwice);  // 1

        let direct = 100 |> _ / 2 |> _ / 2;
        IO.Println(direct);  // 25
    }
}
```

</details>
