# Chapter 1: Hello, World!

> **You'll learn:**
>
> - what a `Contract` block is and where execution starts,
> - how to print text with `IO`,
> - how to read input and join strings.

The classic first program. Create a file called `hello.ct`:

```ct
Contract Program {
    static fn Main() {
        IO.Println("hello world");
    }
}
```

Compile it:

```powershell
ccl .\hello.ct
```

You'll see `Bytecode written to: .\hello.oir`.

## What's going on?

- **`Contract Program { ... }`** — a *contract* is the top-level organizational
  unit, like a class or namespace. Every program has one (or more).
- **`static fn Main()`** — the entry point. When the program runs, execution
  starts here. It must be `static`.
- **`IO.Println(...)`** — a call into the `IO` standard library module that
  prints a line of text.

> **Note** — the entry point is exactly `static fn Main()`. A lowercase
> `fn main()` is *not* an entry point; your program will compile but run
> nothing.

## Making it a conversation

The `IO` module also has `IO.Print` (no newline) and `IO.Readln` (read a line):

```ct
Contract Program {
    static fn Main() {
        IO.Print("What's your name? ");
        var name: string = IO.Readln();
        IO.Println("Hi, " + name + "!");
    }
}
```

`+` on strings concatenates them (see [Chapter 8](08-standard-library.md) for
more string tools).

## Summary

- Every program is a `Contract` containing `static fn Main()`.
- `IO.Println` / `IO.Print` write text; `IO.Readln` reads a line.
- `+` joins strings.

## Exercise

Modify `hello.ct` so it prints two lines: `hello world` and then your name.

<details>
<summary>Solution</summary>

```ct
Contract Program {
    static fn Main() {
        IO.Println("hello world");
        IO.Println("your name here");
    }
}
```

</details>
