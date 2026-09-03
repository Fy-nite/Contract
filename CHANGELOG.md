# Changelog

All notable changes to this project since the last release are documented in this file.

## [Unreleased]

### Added

- Add the `.coi` binary package format: `ccl pack` bundles a compiled `.orbt` module, `bindings/*.dll`, and transitive native `runtimes/<rid>/native` assets into a single installable archive; `ccl install <pkg.coi>` extracts it into a project's `.purr/packages/`, and `ccl` auto-registers its compiled namespaces and `[ClassBinding]` assemblies so consumers `import <pkg>;` with no `--bind`. Bundles and the LSP also pick up a project's installed `.coi` bindings automatically. See `docs/COI_FORMAT.md`.
- Add the `libs/ContractStdlib` submodule (Contract-written stdlib) pinned to `main`.
- Add `ObjektRT.std.Security`: SHA-256/SHA-512 hashing, HMAC-SHA256/SHA-512, CSPRNG bytes/salt/nonce and URL-safe base64 tokens, and constant-time compare, all as pure `CLRImport` facades over `System.Security.Cryptography`.
- Add `Convert.ToUTF8Bytes` and `Convert.ToUTF8String` builtins for string/`byte[]` UTF-8 conversion.

### Fixed

- Fix delegate invocation bugs: calling a function that returns a `Delegate<F>` could raise a false arity error; invoking a `Delegate<F>` returned by a function (`makeAdd()(5, 5)`) called the delegate twice; and invoking a function/delegate stored in a contract or struct field (`this.add(...)`, `box.add(...)`) emitted no call, leaving a wrong value on the stack. All forms now compile and run correctly.
- Fix CLRImport argument/array marshaling for narrow integral types: `byte`/`sbyte`/`short`/`ushort`/`uint` values (boxed as VM `int`) are now coerced to the declared parameter/element type, so CLRImport facades can accept and return `byte[]` (and other narrow arrays).

## [V1.0 beta 2]

### Added

**Language features**
- Add support for inline IL blocks and pointer types in the Contract language.
- Add NuGet package support.
- Add `as` type cast expression.
- Implement indexers in the contract language.
- Add shift (`<<`, `>>`) and `or` (`|`) operators.
- Add `is`/null safety operations and try/catch support.
- Add design-by-contract features and extension methods.
- Add project features: create, add, remove and much more for sub project creation.

**Native interop**
- Enhance zero-copy native interop with `ManagedPtr` and C# host binding.
- Add `PtrHost` interop helpers and C# type binding support.

**Build system**
- Add glob expansion and project settings resolution.
- Add project settings and solution builder enhancements.
- Add `StaticLinker` for static linking of multi-project builds.
- Split up the codebase into `Contract.Compiler.Abstractions`, `Contract.Compiler.Expressions`, and `Contract.Runtime` modules.

**Tooling & docs**
- Add C# host binding examples, shadowing and host call examples.
- Add various test contracts demonstrating conditional logic, absolute value calculations, indexers, and multi-project setups.
- Add `ATTRIBUTES.md`, `CONTRACT_SPEC.typ`, `CONTRACT_LANGUAGE.md`, `ctproj.md`, `SHADOWING.md`, and `TYPE.md` documentation.
- Add `ContractDesktop` MAUI editor.
- Update the doc generator and doc comment extractor.

### Changed

- Enhance import resolution by adding support for extra search roots and memoizing namespace declarations.
- Update VM performance.
- Update documentation across the repo.

### Removed

- Remove `ObjectRT.Dap` project references from the solution and project files.
- Remove legacy `Contract.Compiler.Expressions` F# project in favor of C# implementations.

### Fixed

- Fix docs formatting issues.
- Update `dockerfile`/`.gitignore` hygiene.

## [V1.0 beta 1]

### Added
initial language features, built stdlib in csharp, made compiler, language things.

i don't know much besides that.