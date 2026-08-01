# Tutorial 4: Functions

Functions bundle a piece of logic under a name so you can call it repeatedly.

## Defining a function

```ct
fn greet(name: string) {
    IO.Println("Hello, " + name + "!");
}
```

Functions live inside a `Contract` block:

```ct
Contract Program {
    fn greet(name: string) {
        IO.Println("Hello, " + name + "!");
    }

    static fn Main() {
        greet("Ada");
        greet("Grace");
    }
}
```

## Return types

Use `-> type` after the parameter list to say what the function produces:

```ct
fn add(a: int, b: int) -> int {
    return a + b;
}

fn isEven(n: int) -> bool {
    return n % 2 == 0;
}
```

```ct
static fn Main() {
    var sum: int = add(2, 3);     // 5
    var even: bool = isEven(4);   // true
    IO.Println(sum);
    IO.Println(even);
}
```

If you leave the return type off, the compiler defaults to `void` for `Main`
and `int` for other functions. If a function with a return type falls off the
end without a `return`, it implicitly returns the zero value for that type
(`0`, `0.0`, `false`, or `null`).

## Parameters

Parameters are `name: type` pairs separated by commas:

```ct
fn describe(name: string, age: int, active: bool) {
    IO.Println(name);
    IO.Println(age);
    IO.Println(active);
}
```

## Access modifiers and static

Functions default to `public`. You can mark them `private`, `protected`, or
`internal`, and `static` when they don't need an instance:

```ct
Contract Math {
    public fn add(a: int, b: int) -> int {
        return a + b;
    }

    private fn helper() {
        // only callable from inside Math
    }

    static fn twice(n: int) -> int {
        return n * 2;
    }
}
```

## Exercise

Write a function `max(a: int, b: int) -> int` that returns the larger of two
numbers, then print `max(4, 9)` from `Main`.

<details>
<summary>Solution</summary>

```ct
Contract Program {
    fn max(a: int, b: int) -> int {
        if (a > b) {
            return a;
        }
        return b;
    }

    static fn Main() {
        var result: int = max(4, 9);
        IO.Println(result);  // 9
    }
}
```

</details>
