# AGENTS.md

Contract is a statically-typed language (`.ct`) that compiles to ObjektIL and
runs on the ObjektRT VM. The repo is the compiler, runtime host, language
server, CLI, and VS Code extension, written in C# (.NET 10).

## Repo layout (ownership)

- `Contract.Cli/` - `ccl` CLI entrypoint (`Program.cs`), compiles/runs/hosts
  the LSP, and the in-process test runner (`TestRunner.cs`)
- `Contract.Compiler/` - lexer, parser, semantic analyzer, codegen (the
  language compiler itself)
- `Contract.Compiler.Abstractions/` - shared compiler interfaces/types
- `Contract.Runtime/` - runtime host + Contract-specific bindings
- `Contract.LanguageServer/` - LSP implementation (also hosted via `ccl lsp`)
- `contract.Compiler.Abstractions`, `contract.Security.*` - see docs
- `libs/Objekt-RT/`, `libs/ObjektRT.Core/`, `libs/ObjektRT.Stdlib/` - **git
  submodules**: the VM/runtime and generic stdlib. Hosted on `git.finite.ovh`,
  NOT generally on GitHub.
- `editors/vscode/` - VS Code extension (npm)
- `tests/` - language test programs, see below
- `docs/` - language reference, spec, design notes
- `scratch/` - ad-hoc probes and experiments (some built into the .sln)

## Critical: the test suites are two different things

Do not reach for `dotnet test` when you mean "run the compiler tests".

- **`ccl --test`** (or `dotnet run --project Contract.Cli -- --test`) is the
  actual language test suite. It is an in-process pipeline
  (lex -> parse -> analyze -> codegen) over `tests/success/*.ct` (must
  compile) and `tests/failure/*.ct` (must produce errors). Run this after any
  compiler change.
- **`dotnet test`** (what CI runs) executes only `ObjectRT.Runtime.Tests`, the
  submodule's .NET unit tests. It does NOT exercise the langauge compiler.

If you change code the compiler emits or analyzes, verify with `ccl --test`;
`dotnet test` will not catch it.

`tests/multi-project/` and `tests/static-link/` are additional integration
scenarios (multi-project `.ctproj` builds and static linking) not wired into
the `--test` runner - check them manually if your change touches project
loading or linking. Note `tests/multi-project/bin/` may exist locally.

## Setup / build

- Requires .NET SDK 10.x.
- Must init submodules first or the build fails:
  `git submodule update --init --recursive`
  (`libs/Objekt-RT`, `libs/ObjektRT.Core`, `libs/ObjektRT.Stdlib`).
- Build: `dotnet build` (solution `ContractCompiler.sln`). CI order is
  `dotnet restore` -> `dotnet build --no-restore` -> `dotnet test --no-build`.
- `install.ps1` copies `ccl` onto PATH (PowerShell, cross-platform).

## CLI shorthands

```text
ccl <file.ct>              compile and run
ccl -c <file.ct>           compile only (writes .orbt or .oil with -f)
ccl run <file.orbt|oil>    run a precompiled module
ccl lsp [--trace]          language server over stdio
ccl --test                 compiler test suite
ccl new <name> [--type exe|lib]   scaffold a project (`ccl build` to build it)
```

## Conventions that differ from defaults

- **Changelog is mandatory, in-PR.** Any change to user-facing behavior
  (language feature, CLI command/flag, runtime/interop behavior, build tooling,
  relied-upon docs) must add one terse entry under `## [Unreleased]` in
  `CHANGELOG.md`, in the matching `### Added/Changed/...` subsection, newest at
  top. Do not create versioned headings (those are cut at release). Do not log
  dependency bumps or pure internal refactors. Keep a changelog scout for this
  whenever you touch the compiler or CLI.
- Commit messages: conventional type prefixes (`feat:`, `fix:`, `chore:`,
  `docs:`) are used and encouraged.
- Builtin modules (`__builtin.std.*`) are never implicitly global - they must
  be imported or fully qualified. User contracts shadow same-named builtins.
- See `CONTRIBUTING.md` for the full contributing and changelog/release
  workflow.

## Keys to keep in mind

- Builtins register under the reserved `__builtin.std` root at analysis time
  (`StdlibCatalog.RegisterInto`).
