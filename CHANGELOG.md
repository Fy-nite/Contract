# Changelog

All notable changes to this project since the last release are documented in this file.

## [Unreleased]

### Added

- Restructure the stdlib's reserved root into a real namespace tree under `__builtin`. Nothing is registered outside this root anymore: `__builtin.std.*` groups every module by its binding name (`__builtin.std.IO`, `__builtin.std.Math`, ...), and `__builtin.ObjektRT.Stdlib.*` mirrors the real CLR namespaces (`__builtin.ObjektRT.Stdlib.System.IO`, `__builtin.ObjektRT.Stdlib.Math.Numbers`, ...). `import __builtin.std;` grants the short binding-name modules (`IO`, `Math`, ...), `import __builtin.ObjektRT.Stdlib.System;` grants the short names of one real namespace, and `import __builtin;` makes both `std` and `ObjektRT` addressable so dotted spines (`std.IO.Println(...)`, `ObjektRT.Stdlib.System.IO.Println(...)`) resolve. The standalone `ObjektRT.Stdlib.*` registration is gone, so `import ObjektRT.Stdlib.System;` must be spelled with the `__builtin` prefix. Fully-qualified spellings now require the full path (`__builtin.std.IO.Println(...)`), and the LSP offers `__builtin` as a single top-level module with the tree below it.
- `ccl pack` now accepts a project path (`ccl pack <name.ctproj|dir>`): it builds the project — or the full solution of sub-projects when the root ctproj has a `Projects` array — and packs every resulting `.orbt` module into one `.coi` archive, deriving the manifest `namespaces` map from each sub-project's `Namespace` field (falling back to its name). The existing raw-module form (`ccl pack <name> <module.orbt>`) is unchanged.
- Add the `.coi` binary package format: `ccl pack` bundles a compiled `.orbt` module, `bindings/*.dll`, and transitive native `runtimes/<rid>/native` assets into a single installable archive; `ccl install <pkg.coi>` extracts it into a project's `.purr/packages/`, and `ccl` auto-registers its compiled namespaces and `[ClassBinding]` assemblies so consumers `import <pkg>;` with no `--bind`. Bundles and the LSP also pick up a project's installed `.coi` bindings automatically. See `docs/COI_FORMAT.md`.
- Add the `libs/ContractStdlib` submodule (Contract-written stdlib) pinned to `main`.
- Add `ObjektRT.std.Security`: SHA-256/SHA-512 hashing, HMAC-SHA256/SHA-512, CSPRNG bytes/salt/nonce and URL-safe base64 tokens, and constant-time compare, all as pure `CLRImport` facades over `System.Security.Cryptography`.
- Add `Convert.ToUTF8Bytes` and `Convert.ToUTF8String` builtins for string/`byte[]` UTF-8 conversion.

### Changed

- Restructure the stdlib compiled modules into named sub-namespaces served as accessors: `ObjektRT.std.Debugging` (`Logger`), `ObjektRT.std.Generics` (`List`, `HashMap`, `option`, `Result`), `ObjektRT.std.Math` (`Math`), `ObjektRT.std.VectorMath` (`Vector2`, `Vector3`), `ObjektRT.std.Memory` (`ManagedPtr`), `ObjektRT.std.Security` (`Hash`, `Hmac`, `Random`, `Rng`, ...). `import ObjektRT.std;` still brings in the whole package tree — the packer now maps every module under all ancestor prefixes of its namespace and the import resolver aggregates registered compiled sub-namespaces — so short names keep working while dotted completion shows the module accessors, then the nested types: `ObjektRT.std.Security.Hash`.
- Language server: contracts/structs/enums imported from compiled modules now surface their real namespace — completion/hover detail reads `Contract ObjektRT.std.Security.Hash` instead of the bare `Contract Hash` — and dotted completion walks the compiled-package tree just like the builtins: `ObjektRT.` → `std`, `Core`; `ObjektRT.std.` → `Debugging`, `Generics`, `Math`, `Memory`, `Security`, `VectorMath` all as `namespace` items, and the leaf types one level deeper. Namespace segments come back as `namespace` items; leaf types as `class`/`struct`/`enum` with their full name.

### Fixed

- Fix packaged libraries' `if`/`if-else` bodies losing the instruction right after the block when consumed from a compiled module: the converter consumed one instruction beyond the post-branch label, so e.g. the array-grow path in a packaged `List<T>.Add` dropped the `ldarg 0` before the index-target `stfld`/stelem and crashed on run. The label instruction is now kept for the flat continuation.
- Fix `array.Length`/`array.Count` on a field whose declaring contract is shadowed by a same-named builtin (e.g. a custom `List<T>` next to the builtin `List`): the field's declaring type matched the shadow binding, so member-access codegen emitted a bogus `call List.Count(object)` on the pushed array instead of `ldlen`, producing a runtime cast error. Array `.Length`/`.Count` members now always lower to `ldlen` regardless of shadowing.
- Fix `.coi` packages losing all but one module when several modules share a namespace: the manifest `namespaces` map was 1:1 (namespace → one module path), so packing a project where multiple sub-projects declare the same `Namespace` (e.g. six modules under `ObjektRT.std`) silently dropped all but the last. The packer now groups every sub-project module under its namespace, the map value is a list (a single string is still read for older packages), and a consumer's `import Ns;` loads **all** of the namespace's modules — one import brings in the whole tree (Java `import pkg.*`-style).
- Fix constructors on contracts loaded from compiled module references: the wire format stores `this: object` as the first constructor parameter (the same as instance methods), but only instance methods skipped it. A synthesized constructor therefore looked like it took one extra parameter, so `new Foo(3, 4)` on any packaged library reported "No constructor ... takes 2 argument(s)" and silently ran nothing — fields stayed at their defaults.
- Fix building the language server not finding a project's packages when the workspace root sits *above* the project (or the client only sends `workspaceFolders`): the server only walked up from `rootUri`/`rootPath`, so a project nested under the opened folder — or a client that omits `rootUri` entirely — showed no installed `.coi` package contents and unresolved `import PkgNs;`. Project/package discovery now also walks up from the opened document (falling back to the initialized project/workspace roots) and honors the `workspaceFolders` array.
- Fix OIL text round-trip of string literals: `ldstr` operands from a loaded binary module were emitted without surrounding quotes, so any string containing a comma (or quotes/backslash) broke re-parsing the `.oil`/`.ir.txt` text (`ldstr Hello, ` was read as `Hello` then a stray `,`). The text writer now quotes and escapes string operands, so the IR text loads back losslessly.
- Fix parsing generated OIL text that contains the `?` placeholder: native/CLR-import call sites that lose their signature in the binary round-trip are emitted as `call Foo.Bar(?) -> void`, and the tokenizer threw `Unexpected character '?'` while re-parsing that text — crashing `ccl build`/`ccl build --static` for any project whose package imports CLR-import methods. The tokenizer now accepts `?` as a placeholder identifier, so the generated text loads back losslessly.
- Fix `ccl build` never registering a project's installed `.coi` packages: the namespace → module maps were only published on the direct `ccl run`/`-c` compile path, so any project consuming a package (`import PkgNs;`) failed to compile — with or without `--static`. The build (single-file, glob, and solution) and `ccl pack` paths now register installed packages before compiling, so their modules are embedded into the built application.
- Fix the language server only discovering installed `.coi` packages at startup: a package installed (or removed) with `ccl install`/`ccl remove` while the server was already running stayed invisible until restart. The server now re-registers the project's packages whenever the `.purr/packages` tree changes, so new `import PkgNs;` lines resolve without an editor restart.
- Fix delegate invocation bugs: calling a function that returns a `Delegate<F>` could raise a false arity error; invoking a `Delegate<F>` returned by a function (`makeAdd()(5, 5)`) called the delegate twice; and invoking a function/delegate stored in a contract or struct field (`this.add(...)`, `box.add(...)`) emitted no call, leaving a wrong value on the stack. All forms now compile and run correctly.
- Fix CLRImport argument/array marshaling for narrow integral types: `byte`/`sbyte`/`short`/`ushort`/`uint` values (boxed as VM `int`) are now coerced to the declared parameter/element type, so CLRImport facades can accept and return `byte[]` (and other narrow arrays).
- Fix namespace imports spanning multiple source files: `import Some.Namespace;` content resolution previously returned only the *first* `.ct` declaring that namespace, so members declared in sibling files (e.g. `OwnAudio.ct` + `Chip.ct` both declaring `namespace OwnAudioSharp;`) silently fell back to host bindings or failed to resolve. Namespace imports now load **every** matching source file (compiled `.coi` mapping and directory-located file still win/come first).
- Fix a false "Namespace import 'X' is never used" warning: short module names recorded for usage tracking (e.g. `Chip`, `OwnAudio`) are now resolved through the unique-short-name index before comparing against import prefixes, so a namespace import used through its member types no longer warns spuriously.

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