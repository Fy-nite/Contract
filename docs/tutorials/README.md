# Contract Language Tutorials

A beginner-friendly walkthrough of the Contract programming language. Each tutorial
builds on the previous one, and every example is a complete program you can compile.

## Prerequisites

- The .NET 9 SDK (`dotnet --version`)
- The repo cloned, with the compiler built: `dotnet build .\Contract.Compiler\Contract.Compiler.csproj`

## Compiling and running

The CLI compiles a `.ct` source file into an IR text file (`.oir`):

```powershell
dotnet run --project .\Contract.Cli\ -- path\to\file.ct
```

For example, from the repo root:

```powershell
dotnet run --project .\Contract.Cli\ -- .\tutorials\01-hello-world\hello.ct
```

This writes `hello.oir` next to the source. The `.oir` file is the compiled
intermediate representation. Use `--debug` to also print the parsed AST.

Run the compiler's own test suite with:

```powershell
dotnet run --project .\Contract.Cli\ -- --test
```

## Lessons

| # | Tutorial | What you'll learn |
|---|----------|-------------------|
| 1 | [Hello, World!](01-hello-world.md) | Your first program, the `Contract` block, `Main` |
| 2 | [Variables and Types](02-variables-and-types.md) | `var`/`let`, all built-in types, type inference |
| 3 | [Control Flow](03-control-flow.md) | `if`/`else`, `while`, `for`, `break`, `continue`, `switch` |
| 4 | [Functions](04-functions.md) | Parameters, return types, `return` |
| 5 | [Structs, Classes, and Custom Types](05-structs-and-types.md) | `struct`, classes (contracts with fields), `this`, `new`, constructors, instance methods |
| 6 | [Functional Programming](06-functional-programming.md) | Lambdas with `fun`, the `|>` pipe operator |
| 7 | [Arrays and Logic](07-arrays-and-logic.md) | Array literals, `new Type[size]`, indexing, `.Length`, `&&`/`\|\|`/`!` |
| 8 | [The Standard Library](08-standard-library.md) | `String`, `Math`, `Convert`, `Random`, string switch |

## Example solutions

Each tutorial ends with a small exercise. Sample solutions live next to each
lesson in the `solutions/` folder if you get stuck.

> Tip: all source files end in `.ct` and are UTF-8 encoded.
