# Chapter 8: The Standard Library

> **You'll learn:**
>
> - the `String`, `Math`, `Convert`, and `Random` modules,
> - string concatenation with `+`,
> - `switch` on strings.

Contract ships with several built-in modules you can call without any imports.

## String

```ct
Contract Program {
    static fn Main() {
        var name: string = "ada";
        IO.Println(String.ToUpper(name));      // ADA
        IO.Println(String.Length(name));       // 3
        IO.Println(String.Substring(name, 0, 2)); // ad
        IO.Println(String.StartsWith(name, "a")); // true
        IO.Println(String.Replace(name, "a", "o")); // odo
    }
}
```

String concatenation: `+` (and `+=`) on strings calls `String.Concat` for you,
so `"Hello, " + name` and `String.Concat("Hello, ", name)` are equivalent.

## Math

```ct
Contract Program {
    static fn Main() {
        IO.Println(Math.Abs(-7));   // 7
        IO.Println(Math.Min(3, 9)); // 3
        IO.Println(Math.Max(3, 9)); // 9
    }
}
```

Float math is available too: `Math.Sqrt`, `Math.Pow`, `Math.Floor`,
`Math.Ceiling`, `Math.Round`, `Math.Sin`, `Math.Cos`, `Math.Tan`, `Math.Log`.

## Convert

```ct
Contract Program {
    static fn Main() {
        var n: int = Convert.ToInt32("42");   // 42
        var s: string = Convert.ToString(7);  // "7"
        var b: bool = Convert.ToBool("true"); // true
    }
}
```

## Random

```ct
Contract Program {
    static fn Main() {
        var die: int = Random.NextInt(6) + 1;  // 1..6
        IO.Println(die);
    }
}
```

## String switch

`switch` accepts string case values:

```ct
Contract Program {
    static fn Main() {
        var cmd: string = IO.Readln();
        switch (cmd) {
            case "start": IO.Println("starting");
            case "stop": IO.Println("stopping");
            else: IO.Println("unknown command");
        }
    }
}
```

> **Tip** — combine `String.ToLower` / `String.Trim` with a string switch to
> build robust command parsers.

## Summary

- `String`: `ToUpper`/`ToLower`, `Length`, `Substring`, `StartsWith`, `Replace`, `Trim`.
- `Math`, `Convert`, `Random` cover everyday numeric work.
- `+` concatenates strings; `switch` matches string values.

## Exercise

Write a program that reads a line, trims it, converts it to lowercase, and
prints `"yes"` if it equals `"go"`.

<details>
<summary>Solution</summary>

```ct
Contract Program {
    static fn Main() {
        var input: string = IO.Readln();
        var cleaned: string = String.ToLower(String.Trim(input));
        switch (cleaned) {
            case "go": IO.Println("yes");
            else: IO.Println("no");
        }
    }
}
```

</details>
