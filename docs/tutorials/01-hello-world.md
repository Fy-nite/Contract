# Tutorial 1: Hello, World!

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
dotnet run --project .\Contract.Cli\ -- .\hello.ct
```

You'll see `Bytecode written to: .\hello.oir`.

## What's going on?

- **`Contract Program { ... }`** — a *contract* is the top-level organizational
  unit, like a class or namespace. Every program has one (or more).
- **`static fn Main()`** — the entry point. When the program runs, execution
  starts here. It must be `static`.
- **`IO.Println(...)`** — a call into the `IO` standard library module that
  prints a line of text.

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
