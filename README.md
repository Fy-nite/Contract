# Contract

A statically-typed programming language that compiles to ObjektIL, runs on the ObjektRT virtual machine, and
ships with editor tooling (LSP + VS Code extension).

```ct
import __builtin.std;

Contract Program {
    static fn Main() {
        IO.Println("Hello, World!");

        var x: int = 5;
        var y: int = 10;

        if (x < y) {
            IO.Println("x is less than y");
        }

        // Lambdas, closures, and the pipe operator
        let inc = fun x -> x + 1;
        let result: int = 5 |> inc;
        IO.Println(result);  // 6
    }
}
```

## Highlights

- **Contracts** — organizational units that work as both namespaces and
  classes: fields, constructors, static *and* instance methods, inheritance,
  and attributes
- **First-class functions** — lambdas, closures with **by-reference capture**
  (C#-style shared variables), delegates, and the `|>` pipe operator
- **Data modeling** — structs, enums, namespaces, and type-erased generics
  (`List<T>`, `Dict<K, V>`)
- **Threads as values** — `Thread.Create` / `Start` / `Join` / `IsAlive`, plus
  fire-and-forget `Thread.Spawn`
- **Reflection** — introspect types, methods, fields, and call code at runtime
  with the `Reflect` module
- **Standard library** — `IO`, `String`, `Math`, `Convert`, `Random`, `File`,
  `Environment`, `Time`, `GC`, `Debug`, `Thread`, `Array`, `List`, `Dict`
- **Tooling** — a built-in language server (`ccl lsp`) with diagnostics,
  hover, completion, go-to-definition, signature help and more, plus a VS Code
  extension

## Getting started

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download)

### Install

```bash
git clone https://github.com/fy-nite/contract --recursive
cd contract
dotnet build
```

To make `ccl` available on your PATH:

```bash
./install.ps1              # Windows, mac, linux (powershell required)
```

Alternatively, ccl can be installed via a nix shell with
```nix
let
  pkgs = import <nixpkgs> {};
  contract = import (fetchGit {
    url = "https://github.com/Fy-nite/Contract.git";
    submodules = true;
  });
in
pkgs.mkShell {
  packages = [
    contract
  ];
}
```

### Usage

```bash
ccl hello.ct               # compile and run
ccl -c hello.ct            # compile only (writes hello.orbt)
ccl -c hello.ct -f oil     # compile to readable IR text
ccl run hello.orbt         # run a precompiled module
ccl lsp                    # language server over stdio
ccl --test                 # run the compiler's test suite
```

```text
Usage:
  ccl <file.ct> [options]          Compile and run in one go
  ccl -c <file.ct> [-o out]        Compile only
  ccl run <file.orbt|oil|oir>      Run a precompiled module
  ccl lsp [--trace]                Run the language server
  ccl --test                       Run the compiler test suite

Options:
  -o, --output <path>   Output path (.oil = text, else binary)
  -f, --format oil|orbt Output format
  -m Name.Method        Call a specific method instead of the entry point
  -d                    Print the generated IR before running
  --bind <assembly>     Load custom host bindings
  -v, --verbose         Verbose output
```

## The language at a glance

```ct
// Contracts as classes
import __builtin.std;

Contract Counter {
    count: int;

    constructor() { this.count = 0; }

    fn increment() { this.count += 1; }

    static fn Main() {
        var c: Counter = new Counter();
        c.increment();
        IO.Println(c.count);   // 1
    }
}
```

```ct
// Closures capture by reference — the variable is shared, not copied
var n: int = 0;
let bump = fun -> { n += 1; };
bump();
IO.Println(n);                  // 1
```

```ct
// Threads are values
var t = Thread.Create(fun -> { IO.Println("work"); });
Thread.Start(t);
Thread.Join(t);
```

Builtin modules are never implicitly global: import them
(`import __builtin.std;`) or spell them fully qualified
(`__builtin.std.IO.Println(...)`). User-declared contracts shadow same-named
builtin modules — that is what keeps a Contract-written stdlib free to
replace them.

See [docs/CONTRACT_LANGUAGE.md](docs/CONTRACT_LANGUAGE.md) for the full
language reference, including the standard library and threading model.

## Editor support

The language server is hosted by the CLI (`ccl lsp`), so no separate install
is needed. A VS Code extension lives in `editors/vscode` (install the CLI,
then `cd editors/vscode && npm install` and press **F5**).

Features: diagnostics as you type, hover with doc-comment support, completion,
go-to-definition and references, signature help, document symbols, folding,
semantic highlighting, and quick fixes.

## Repository layout

```
Contract.Cli/              CLI (compile, run, test, host the LSP)
Contract.Compiler/         Compiler: lexer, parser, semantic analysis, codegen
Contract.Runtime/          Runtime host + Contract-specific bindings
Contract.LanguageServer/   Language Server Protocol implementation
editors/vscode/            VS Code extension
libs/Objekt-RT/            The ObjektRT runtime / VM (submodule)
libs/ObjektRT.Stdlib/      The generic standard library
tests/                     Success + expected-failure test programs
docs/                      Language reference, spec, design docs
```

## Documentation

- [Language Reference](docs/CONTRACT_LANGUAGE.md)
- [Language Specification](docs/LANGUAGE_SPEC_v1.md)
- [Formal Spec (Typst)](docs/CONTRACT_SPEC.typ)
- [Design Notes](docs/DESIGN_DELEGATES.md)

## Testing

The compiler ships an in-process test suite:

```bash
ccl --test
```

`tests/success/` holds programs that must compile, `tests/failure/` programs
that must produce errors.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, testing, commit conventions,
and how to keep `CHANGELOG.md` up to date.

## License

See [LICENCE](LICENCE).

