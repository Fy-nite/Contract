# ContractIR

A programming language and compiler infrastructure for the Contract language.

## Overview

Contract is a statically-typed language that compiles to CIL1 (Contract Intermediate Language v1) bytecode. It features a clean, modern syntax with contracts, functions, structs, and functional programming constructs.

## Features

- **Contracts**: Organizational units similar to namespaces/classes
- **Functions**: First-class functions with optional type annotations
- **Structs**: Custom data structures
- **Type System**: Built-in types and custom type definitions
- **Functional Programming**: Lambda expressions and pipe operator
- **Access Modifiers**: Public, private, protected, and internal visibility
- **Standard Library**: IO module for input/output operations

## Project Structure

```
ContractIR/
├── Contract.Cli/           # Command-line interface
│   ├── Program.cs          # Main entry point
│   ├── TestRunner.cs       # Test execution
│   └── examples/           # Example Contract files
├── Contract.Compiler/      # Compiler implementation
│   ├── AST/                # Abstract Syntax Tree definitions
│   ├── Parsing/            # Lexer and Parser
│   ├── CodeGen/            # Code generation
│   ├── Semantics/          # Semantic analysis
│   ├── Diagnostics/        # Error handling
│   └── StandardLibrary/    # Built-in functions
├── Contract.LanguageServer/  # Language Server Protocol implementation
│   └── Lsp/                  # JSON-RPC transport, document store, symbol index
├── editors/vscode/           # VS Code extension (client, grammar, config)
├── tests/                  # Test files
│   ├── success/            # Valid test cases
│   └── failure/            # Expected failure cases
├── docs/                   # Documentation
└── libs/                   # External libraries
```

## Getting Started

### Prerequisites

- .NET SDK (for building the compiler)
- Git

### Building

```bash
# Clone the repository
git clone https://github.com/fy-nite/contract
cd contract

# Build the solution
dotnet build

# optionally installing this, just run
./install.ps1
```

### Running

```bash
# Run the CLI
dotnet run --project Contract.Cli

# Compile a Contract file
dotnet run --project Contract.Cli -- compile input.ct
```

### Testing

```bash
# Run all tests
dotnet test

# Run specific test
dotnet run --project Contract.Cli -- test tests/success/HelloWorld.ct
```

## Example Code

```ct
Contract Program {
    static fn Main() {
        IO.Println("Hello, World!");
        
        var x: int = 5;
        var y: int = 10;
        
        if (x < y) {
            IO.Println("x is less than y");
        }
        
        // Lambda functions and pipe operator
        let inc: Int = fun x -> x + 1;
        let result: int = 5 |> inc;
        IO.Println(result);  // 6
    }
}
```

## Documentation

- [Language Reference](docs/CONTRACT_LANGUAGE.md) - Complete language specification
- [Language Specification](docs/LANGUAGE_SPEC_v1.md) - Formal grammar and semantics
- [Implementation Plan](docs/LANGUAGE_PLAN.md) - Roadmap and development plans

## Compiler Architecture

### Lexer (`Contract.Compiler/Parsing/Lexer.cs`)
- Tokenizes source code into tokens
- Handles comments, strings, numbers, and identifiers
- Supports line directives (`#line N`)

### Parser (`Contract.Compiler/Parsing/Parser.cs`)
- Recursive descent parser
- Builds Abstract Syntax Tree (AST)
- Error recovery and diagnostics

### AST (`Contract.Compiler/AST/Ast.cs`)
- Defines all AST node types
- Supports contracts, functions, structs, statements, and expressions

### Code Generation
- `IRCodeGenerator.cs` - Intermediate representation generation
- `BytecodeEmitter.cs` - CIL1 bytecode emission

## Language Server (LSP)

`Contract.LanguageServer` is a Language Server Protocol implementation for
Contract, with a companion VS Code extension in `editors/vscode`. It reuses the
real compiler pipeline (lex → parse → analyze) so editor diagnostics always
match the CLI.

### Features

- **Diagnostics** — errors and warnings as you type, with precise ranges
  (parser error recovery means *all* errors report, not just the first)
- **Hover** — signatures for user functions, inferred types for locals,
  stdlib method signatures (`IO.Println`, `Math.Sqrt`, …), modules
- **Doc comments** — `///` doc comments above declarations in `.ct` files
  appear on hover and in completion; stdlib methods and modules surface their
  C# XML docs (`Contract.Compiler.xml` — the same "docs live next to the
  assembly" pattern .NET IDEs use)
- **Go to definition** — cross-file navigation for functions, locals,
  struct/contract types and members
- **Completion** — context-aware: keywords, types (after `new` / `:`),
  module members (`IO.` / `Module::`), struct/contract members, functions,
  locals and stdlib modules
- **Signature help** — parameter hints inside calls, with the active
  parameter tracked across commas
- **Document highlight** — highlights the declaration (write) and all
  same-name usages (read) in the file
- **Go to references** — all references to a symbol across every file of
  the compilation
- **Document symbols** — outline of contracts, structs, functions, fields,
  parameters and locals
- **Folding ranges** — brace-based folding for contracts, functions, structs
  and blocks
- **Semantic tokens** — editor coloring for keywords, types, classes,
  structs, functions, methods, properties, variables, parameters, modules,
  strings, numbers and operators (with `declaration`/`defaultLibrary`
  modifiers)
- **Code actions** — quick fixes such as *Add missing `;`*
- Full document sync (`didOpen` / `didChange` / `didClose`), including
  in-memory compilation of imported files

### Architecture

```
editors/vscode (client)                 Contract.LanguageServer (server)
┌─────────────────────────┐   LSP over   ┌──────────────────────────────────┐
│ extension.js → ccl lsp  │   stdio      │ JsonRpcServer                    │
└─────────────────────────┘ ◄──────────► │   ├─ DocumentStore (open docs)   │
                        Content-Length   │   ├─ CompilationService          │
                                          │   │   ├─ ProgramLoader (imports) │
                                          │   │   └─ SemanticAnalyzer        │
                                          │   └─ SymbolIndex (hover/def)    │
                                          └──────────────────────────────────┘
```

The server is hand-rolled JSON-RPC over stdio byte streams with zero external
NuGet dependencies. Code generation is skipped — the editor only needs
semantic diagnostics. The server lives in `Contract.LanguageServer` and is
hosted by the CLI (`ccl lsp`), so the VS Code extension only needs `ccl` on
PATH.

### Running the server

The language server is hosted by the CLI, so no separate install is needed:

```bash
ccl lsp                      # LSP over stdio; --trace logs traffic to stderr
ccl lsp --trace
```

Smoke test with a scripted session (runs the freshly built CLI):

```bash
dotnet build Contract.Cli
powershell -ExecutionPolicy Bypass -File scratch/lsptest/run-session.ps1
```

### Using the VS Code extension

1. Make sure `ccl` is installed and on PATH: `dotnet tool install --global cclc`
   (after pulling changes, reinstall so `lsp` is included:
   `dotnet tool uninstall --global cclc; dotnet tool install --global cclc`)
2. `cd editors/vscode && npm install`
3. Press **F5** — the root `.vscode/launch.json` launches the Extension
   Development Host with the extension loaded. (Opening the repo root directly
   works; no need to open `editors/vscode` as the workspace.)
4. Open a `.ct` file

Settings: `contract.languageServer.path` (explicit CLI path; defaults to
`ccl` on PATH), `contract.languageServer.trace` (protocol trace in the
Output panel).

### Doc comments

Write `///` doc comments directly above any declaration (contract, struct,
function, field, parameter, local) and they show up in hover, completion and
signature help:

```contract
Contract Greeter {
    /// Says hello to the given name.
    static fn hello(name: string) -> string {
        return "Hello, " + name;
    }
}
```

The stdlib functions are documented with C# `/// <summary>` comments; the
build emits `Contract.Compiler.xml` next to the assembly and the server reads
it, so hovering `IO.Println(...)` shows its real documentation. Both degrade
gracefully — if the XML is missing, you just get the signature.

## Namespaces & the generic stdlib

Modules don't have to live at the root. Import a namespace and use dotted
module names — the runtime dispatches by splitting on the last dot, so any
registered CLR type works, including namespaced ones:

```contract
import ObjektRT.Stdlib.System;
import ObjektRT.Stdlib.Math;

Contract Program {
    static fn Main() {
        // Fully-qualified dotted access — no import needed.
        ObjektRT.Stdlib.System.IO.Println("hello from ns");

        // Short name after the namespace import.
        IO.Println("short IO after import");

        // Dotted scoped access.
        ObjektRT.Stdlib.System.IO::Println("scoped dotted");

        var x: int = ObjektRT.Stdlib.Math.Numbers.Abs(-42);
        IO.Println(Numbers.Max(3, 9));   // short module after import
    }
}
```

Rules:

- `import A.B.C;` (a dotted identifier, no quotes) imports a namespace;
  `import "file.ct";` still imports a file
- After a namespace import, the last segment of a module name is addressable
  by its short name (`IO`, `Numbers`, …); imported namespaces take priority
  over same-named root modules
- Dotted chains work in both `Module.Method(...)` and `Module::Method(...)`
  forms
- LSP: hover and completion understand dotted chains (including namespace
  segment completion: `ObjektRT.Stdlib.` → `System`, `Math`, …)

The generic stdlib lives in `libs/ObjektRT.Stdlib` — plain static C# classes
with zero Contract-specific attributes (`[ClassBinding]`/`[MethodBinding]` are
only used by the original root modules). `StdlibCatalog` in the compiler maps
language module names to those types; hosts register the same types with the
runtime's generic `RegisterClrType`. (`libs/Stdlib` is the original
submodule, still referencing the old runtime types; the migrated modules live
in `libs/ObjektRT.Stdlib`.)

### Known limitations

- Diagnostics report the same errors as the compiler — gaps in the compiler's
  semantic checks (e.g. `var x: int = "oops"` is not currently flagged) are
  compiler gaps, not LSP gaps
- Hover type inference mirrors `SemanticAnalyzer.InferType`; scoped calls like
  `Greeter::hello(...)` infer `int` today (the analyzer does not yet resolve
  scoped-access return types)
- Imported files are compiled from disk unless the import is also open in the
  editor (open documents take precedence)
- Untitled buffers are compiled standalone (no import resolution)

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

See LICENSE file for details.

## Acknowledgments

- Inspired by modern programming language design
- Built with .NET and C#
