# Chapter 3: Control Flow

> **You'll learn:**
>
> - branching with `if` / `else`,
> - looping with `while` and `for`,
> - jumping with `break` and `continue`,
> - dispatching with `switch`.

Programs make decisions and repeat work. Contract has the usual tools.

## if / else

```ct
Contract Program {
    static fn Main() {
        var x: int = 7;
        if (x > 10) {
            IO.Println("big");
        } else {
            IO.Println("small");
        }
    }
}
```

## while

```ct
var i: int = 0;
while (i < 5) {
    IO.Println(i);
    i = i + 1;
}
// prints 0 1 2 3 4
```

## for

A C-style `for` loop has an initializer, a condition, and an update:

```ct
for (var i: int = 0; i < 5; i = i + 1) {
    IO.Println(i);
}
```

All three parts are optional, so `for (;;)` is an infinite loop. You can also
leave parts out:

```ct
var i: int = 0;
for (; i < 5;) {
    IO.Println(i);
    i = i + 1;
}
```

## break and continue

- `break` stops the loop immediately.
- `continue` jumps to the next iteration.

```ct
for (var i: int = 0; i < 10; i = i + 1) {
    if (i == 2) {
        continue;          // skip 2
    }
    if (i > 5) {
        break;             // stop after 5
    }
    IO.Println(i);         // prints 0 1 3 4 5
}
```

## switch

```ct
var day: int = 3;
switch (day) {
    case 1: IO.Println("Monday");
    case 2: IO.Println("Tuesday");
    case 3: IO.Println("Wednesday");
    else: IO.Println("Weekend");
}
```

> **Note** — `switch` also works on strings, not just integers (see
> [Chapter 8](08-standard-library.md)).

## Summary

- `if`/`else` branch; `while` and `for` loop; `break`/`continue` steer.
- `switch` dispatches on integers or strings, with an `else` fallback.

## Exercise

Print the numbers 1 through 10, but print `"fizz"` for multiples of 3 and
`"buzz"` for multiples of 5.

<details>
<summary>Solution</summary>

```ct
Contract Program {
    static fn Main() {
        for (var i: int = 1; i <= 10; i = i + 1) {
            if (i % 3 == 0 && i % 5 == 0) {
                IO.Println("fizzbuzz");
            } else if (i % 3 == 0) {
                IO.Println("fizz");
            } else if (i % 5 == 0) {
                IO.Println("buzz");
            } else {
                IO.Println(i);
            }
        }
    }
}
```

</details>

`case` values must be integer literals. The `else` case catches everything
else.

## Exercise

Print the numbers 1 through 20, but:
- print `"fizz"` for multiples of 3,
- print `"buzz"` for multiples of 5,
- print `"fizzbuzz"` for multiples of both.

<details>
<summary>Solution</summary>

```ct
Contract Program {
    static fn Main() {
        for (var i: int = 1; i <= 20; i = i + 1) {
            if (i % 15 == 0) {
                IO.Println("fizzbuzz");
            } else if (i % 3 == 0) {
                IO.Println("fizz");
            } else if (i % 5 == 0) {
                IO.Println("buzz");
            } else {
                IO.Println(i);
            }
        }
    }
}
```

</details>
