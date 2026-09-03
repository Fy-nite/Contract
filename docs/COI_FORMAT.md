# Contract Overlay/Intermediate packages (`.coi`)

A `.coi` file is a ZIP archive that packages a compiled Contract library
(precompiled `.orbt` modules) together with everything needed to consume it: the
binding assemblies it requires, their transitive managed and native dependencies,
and metadata. It is Contract's equivalent of Java's `.jar` or .NET's `.nupkg`.

A `.coi` is a *binary* deliverable. It carries compiled module bytes, not a source
tree — a producer builds it from a `lib` Contract project, and a consumer installs
it with `ccl install` and imports it by namespace, with no `--bind` needed.

Two distribution modes are supported:

- **Install into a project.** `ccl install <name>` (or a local `.coi` path) links
  the package into `.purr/packages/<name>/` for one project.
- **Ship beside the compiler.** Installing into the global cache
  (`~/.purr/cache/<name>/<version>/`) makes a package available to every project
  on the machine, the way the stdlib ships with the runtime.

---

## Layout

```
OwnAudioSharp.coi
├── manifest.json                    # package metadata (required, at archive root)
├── lib/
│   ├── OwnAudioSharp.orbt           # compiled module (one or more)
│   └── OwnAudioSharp.Extra.orbt
└── bindings/                        # optional: .NET assemblies
    ├── OwnAudioSharp.Contract.dll   # the [ClassBinding] bridge
    ├── OwnAudioSharp.dll            # transitive managed dep
    ├── SomeDep.dll
    └── runtimes/
        ├── win-x64/native/ownaudio_ffi.dll
        └── linux-x64/native/libownaudio_ffi.so
```

- `manifest.json` must sit at the archive root. Everything else is placed relative
  to it.
- `lib/*` holds the precompiled modules that back the package's namespaces.
- `bindings/*` holds the managed assemblies (and nested `runtimes/<rid>/native`
  trees for native assets) that host-side bindings need. These are what make
  `--bind` unnecessary: consuming a `.coi` auto-registers every binding assembly
  it ships.

---

## Manifest (`manifest.json`)

```jsonc
{
  "name": "OwnAudioSharp",
  "version": "1.0.0",
  "type": "lib",

  // Modules to register as compiled references (import roots). Each is a
  // Compile to the namespace(s) they expose. Each path is a compiled module
  // (.orbt/.oil) inside the archive.
  "modules": [
    "lib/OwnAudioSharp.orbt",
    "lib/OwnAudioSharp.Extra.orbt"
  ],

  // Name-to-path mapping of the namespaces this package exposes, so `import
  // OwnAudioSharp;` resolves to the right compiled module without guessing.
  "namespaces": {
    "OwnAudioSharp": "lib/OwnAudioSharp.orbt"
  },

  // Managed assemblies to auto-register as binding assemblies. Every
  // [ClassBinding] type is registered on the runtime and made visible to the
  // compiler's analyzer. Paths are archive-relative.
  "bindings": [
    "bindings/OwnAudioSharp.Contract.dll"
  ],

  // Transitive .coi dependencies (name -> version range).
  "dependencies": {
    "ContractStdlib": "^1.0.0"
  }
}
```

### Field reference

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Package name. Matches the archive stem by convention. |
| `version` | string | yes | Semver version of the package. |
| `type` | string | no | `"lib"` (default). Reserved for future `"exe"`. |
| `modules` | string[] | yes | Archive-relative compiled modules to link in. |
| `namespaces` | object | no | Maps an imported namespace to a module path inside the archive. |
| `bindings` | string[] | no | Archive-relative managed assemblies to auto-register. |
| `dependencies` | object | no | Transitive `.coi` dependencies (name → version range). |

---

## Producing a `.coi`

### `ccl pack <project> [-o out.coi]`

Given a `lib` `contract.ctproj` (or a source `.ct` file), `ccl pack`:

1. Compiles the project's sources to `.orbt` bytes (existing
   `CompileFileToBinary` path).
2. Writes `manifest.json` with `modules`, `namespaces` (derived from the project's
   `Namespace` field), and `bindings`.
3. Collects the binding assemblies to ship:
   - the project's explicitly declared binding DLLs (a `BindAssemblies`-style field
     or CLI `--bind` inputs),
   - every DLL in those assemblies' directories (their transitive managed deps),
   - native assets from `runtimes/<rid>/native` trees — the same flattening the
     bundler already does in `BundleDriver`.
4. Zips `manifest.json` + `lib/*` + `bindings/*` into `out.coi`.

The result is a self-contained artifact: a consumer needs only the one file.

---

## Consuming a `.coi`

### `ccl install <name>` / `ccl install path/to/pkg.coi`

Installs (extracts) the archive into `.purr/packages/<name>/`, preserving the
`lib/` and `bindings/` trees, and records the dependency. A package installed from
the registry is also cached in the global `~/.purr/cache/<name>/<version>/`.

### In a project: `contract.ctproj`



### At compile / run / bundle time

When a package is installed, the compiler and runtime auto-wire it:

1. **Import resolution.** The archive's `lib/` directory is added to the compiler's
   import search roots (alongside the existing `.purr/packages/*` roots), and the
   `namespaces` map is indexed so `import OwnAudioSharp;` resolves to the correct
   module.
2. **Binding registration.** Every assembly listed in the package's `bindings` is
   loaded via `Assembly.LoadFrom` and registered through
   `Runtime.RegisterBindingAssembly` — the same path `--bind` uses, but automatic.
   This happens at compile time (so `[ClassBinding]` validation succeeds) and at
   run/bundle time (so calls resolve).
3. **Native assets.** `bindings/runtimes/<rid>/native/*` are flattened next to the
   output so P/Invoke finds them without a `deps.json`, exactly as the bundler
   does.
4. **Transitive deps.** The manifest's `dependencies` are recursively resolved and
   installed, mirroring how `NuGetResolver` walks `.nuspec`.

Because registration is fully automatic, a consumer never passes `--bind`
explicitly — the package carries everything it needs.

---

## Language server

The LSP opens the workspace's `contract.ctproj` (from `InitializeParams.RootUri`/
`RootPath`), reads its package `Dependencies`, and:

- registers each installed package's binding assemblies (`RegisterBindingAssembly`),
- adds each package's `lib/` to its `ProgramLoader.ExtraSearchRoots` so namespace
  imports resolve,
- preloads NuGet assemblies the project declares.

This mirrors the CLI's `CompilerDriver` behavior, which the LSP's `ProgramLoader`
does not currently share.

---

## Notes

- A `.coi` is a normal ZIP archive and can be inspected with any zip tool.
- Modules inside a `.coi` are *precompiled*: they resolve as DLL-style includes via
  `CompiledReferenceLoader` (`.orbt`/`.oil`), so no source is shipped. This keeps
  the artifact a library, not a source tree, and lets shadow/native binding
  modules carry their host-side contracts in compiled form.
- The `bindings/` tree may embed a NuGet-restored layout; the consumer does not
  need NuGet to use the package because the assemblies ship inside the archive.
