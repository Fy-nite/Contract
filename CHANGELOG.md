# Changelog

All notable changes to this project since the last release are documented in this file.

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