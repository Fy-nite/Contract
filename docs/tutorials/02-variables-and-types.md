# Tutorial 2: Variables and Types

Contract is statically typed. Every value has a type, and the compiler checks
that you use types consistently.

## Declaring variables

Use `let` for values that never change and `var` for values that do:

```ct
Contract Program {
    static fn Main() {
        var count: int = 0;        // mutable
        let pi: double = 3.14159;  // immutable
        count = count + 1;         // ok
        // pi = 3.0;               // ERROR: pi is immutable
    }
}
```

The syntax is `var name: type = value;`. The type annotation is required when
the compiler can't figure it out on its own (see Type Inference below).

## Built-in types

| Type     | What it is              | Example literal      |
|----------|-------------------------|---------------------|
| `int`    | 32-bit whole number     | `5`, `-42`, `0`     |
| `double` | 64-bit floating point   | `3.14`, `0.5`, `2.0`|
| `float`  | 32-bit floating point   | `1.5`               |
| `bool`   | `true` or `false`       | `true`, `false`     |
| `string` | Text                    | `"hi"`, `""`        |

Type names are case-insensitive — `int`, `Int`, and `INT` all mean the same
thing.

## Type inference

You can leave the type off when the initializer makes it obvious:

```ct
let answer = 42;        // int
let name = "Ada";       // string
let ready = true;       // bool
let ratio = 0.5;        // double
let p = new Person();   // Person
```

If the compiler can't infer (e.g. `let x = null;`), add the annotation:

```ct
let x: string = null;
```

## Exercise

Write a program that declares `let temperature = 23.5;` and prints it. Then
declare a `var score: int = 10;`, add 5 to it, and print that too.

<details>
<summary>Solution</summary>

```ct
Contract Program {
    static fn Main() {
        let temperature = 23.5;
        var score: int = 10;
        score = score + 5;
        IO.Println(temperature);
        IO.Println(score);
    }
}
```

</details>
