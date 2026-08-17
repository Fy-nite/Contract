# Chapter 6: Functional Programming

> **You'll learn:**
>
> - anonymous functions with `fun`,
> - the pipe operator `|>` and why it reads better than nesting,
> - how lambdas and pipes compose.

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

## Summary

- `fun x -> expr` is an anonymous single-expression function.
- `a |> f` calls `f(a)`; chains read left-to-right.

## Exercise

Write a function `halve(x: int) -> int` returning `x / 2`. In `Main`, pipe
`100` through `halve` twice and print the result (should be 25).

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
    }
}
```

</details>
