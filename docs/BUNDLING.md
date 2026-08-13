# Bundling Contract programs into executables

A *bundle* turns a compiled Contract program (`.orbt`) into a standalone
executable: a small C# host that embeds the module as a manifest resource,
constructs a runtime, and runs the module's entry point. The host is compiled
in-memory with Roslyn, then wrapped in a native apphost (or published
self-contained per-RID).

```
.ct ──compile──▶ .orbt ──bundle──▶ myapp.exe  (framework-dependent, needs .NET)
                                  myapp-win-x64.exe  (self-contained, no .NET needed)
```

This doc covers both entry points to the same machinery:

- **`ccl bundle`** — the Contract CLI (`contract bundle …`), host =
  `Contract.Runtime.ContractRuntime`, stdlib pre-registered.
- **`objectrt -b`** — the generic ObjectRT CLI, host = `ObjectRT.Runtime.Runtime`.
  Use this when you *don't* have Contract and are running plain ObjectIL modules.

---

## Quick start

### Contract: `ccl bundle`

```bash
# Framework-dependent exe (runs anywhere a compatible .NET runtime is installed)
ccl bundle app.ct

# Program that uses a host binding (e.g. the Crituque Ui facade):
# the program drives startup itself via Host.Run — no --host needed.
ccl bundle app.ct --bind Crituque.dll

# Self-contained native exe for a platform, no .NET required on the target
ccl bundle app.ct --rid win-x64
ccl bundle app.ct --rid win-x64,linux-x64,osx-x64

# Self-contained, single-file
ccl bundle app.ct --rid win-x64 --single-file

# Custom runtime host instead of the default ContractRuntime
ccl bundle app.ct --host MyApp.MyRuntime,MyApp.dll --bind MyApp.dll

# Precompiled module input (.orbt / .oil / .oir) instead of .ct source
ccl bundle app.orbt --bind Crituque.dll
```

Output layout (framework-dependent):

```
./app/
  app.exe          native apphost launcher
  app.dll          the generated host assembly
  app.runtimeconfig.json
  *.dll            runtime + stdlib + binding assemblies + their dependencies
```

With `--rid`, each RID produces a directory `./app-{rid}/` (or `-o` overrides
the parent).

### ObjectRT (no Contract): `objectrt -b`

```bash
# Bundle a plain ObjectIL module with the generic runtime as the host
objectrt -b module.orbt
objectrt -b module.oil --rid win-x64
```

`objectrt` and `ccl` share the same `ObjectRT.Runtime.BundleDriver`; the only
difference is which `IHostedRuntime` the generated host instantiates.

---

## How it works

### 1. Compile to bytes

`ccl` compiles `.ct` → `.orbt` bytes via
`ContractCompiler.CompileFileToBinary` (lex → parse → analyze → ObjektIR →
ORBT binary). For precompiled input (`.orbt`/`.oil`/`.oir`) the module is used
as-is. `objectrt -b` accepts `.orbt` directly or compiles `.oil` in memory.

### 2. The generated host

`BundleDriver` emits a C# host that:

```csharp
var bind0 = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Crituque.dll"));
IHostedRuntime rt = new Contract.Runtime.ContractRuntime();   // from --host / default
rt.RegisterBindingAssembly(bind0);                             // each --bind assembly

// optional: first IHostedRuntimeSetup in the bind assemblies
foreach (var t in bind0.GetTypes())
    if (t is IHostedRuntimeSetup) ((IHostedRuntimeSetup)Activator.CreateInstance(t)).Setup(rt);

using var s = typeof(Program).Assembly.GetManifestResourceStream("module.orbt")!;
var data = new byte[s.Length]; s.Read(data, 0, data.Length);
rt.RunModule(OrbtFileReader.ReadBytes(data));                  // load + call Program.Main
```

The `.orbt` is embedded as a manifest resource named `module.orbt`. The host is
compiled in-memory with Roslyn against the CLI's own assemblies, so it always
matches the runtime it ships with.

### 3. Runtime DLLs

The CLI copies its own runtime assemblies (`ObjectRT.*`, `Contract.*`,
Roslyn for JIT mode) next to the host, then — for each `--bind` assembly — every
DLL in that assembly's directory (the binding's own dependencies, e.g. the
Avalonia stack for a UI binding). The CLI's copies are written *last* so the
freshest runtime always wins over any stale copies a binding folder carries.

### 4. Apphost / publish

- **Framework-dependent**: `BundleDriver` finds the installed .NET SDK and uses
  its `apphost` template + `Microsoft.NET.HostModel.HostWriter` to produce the
  native launcher, plus a `runtimeconfig.json`.
- **Self-contained (`--rid`)**: the host source is written to a temp csproj and
  `dotnet publish -r <rid> --self-contained true` runs. `--single-file` adds
  `<PublishSingleFile>true</PublishSingleFile>`.

---

## Runtime version compatibility (rollForward)

The generated `runtimeconfig.json` requests the **minimal** version for the
target framework and uses `LatestMajor`:

```json
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "rollForward": "LatestMajor",
    "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
  }
}
```

This means the bundle runs on:

- the same major with any newer patch/minor (10.0.0 → 10.0.x),
- a future major if that's all that's installed (10.0.0 → 11.x),
- **not** on an older major (a `net10.0` assembly cannot run on .NET 9 no matter
  what — the TFM decides that).

`LatestMinor` is deliberately not used: it only rolls forward within a patch
band and rejects older patches of the same minor. `LatestMajor` is the most
forgiving choice for "I don't know what .NET the end user has." For a
guaranteed-identical environment, use `--rid` self-contained instead — it ships
the runtime inside the exe and needs no installed .NET at all.

---

## The generic seam: `IHostedRuntime` + `BundleSpec`

Bundling is not Contract-specific. The driver only needs three things from a
host:

| Member | Purpose |
|---|---|
| `RegisterBinding(name, Type)` | expose a CLR type's static methods as a module |
| `RegisterBindingAssembly(Assembly)` | register every `[ClassBinding]` type in an assembly |
| `RunModule(ORBTModule)` | load the module and run its entry point |

`ObjectRT.Runtime.Runtime` implements this directly. `Contract.Runtime.ContractRuntime`
implements it too (and additionally pre-registers the Contract stdlib).
Anything else that implements the interface — a game engine runtime, a
headless server host — can be bundled without touching `BundleDriver`.

```csharp
var spec = new BundleSpec
{
    HostType = typeof(MyRuntime),                 // : IHostedRuntime
    BindingAssemblyPaths = new[] { "MyRuntime.dll" },
    Rids = new[] { "win-x64" },
    SingleFile = true,
    HostInit = "rt.SomeConfig = true;",           // optional C# lines in Main
};
BundleDriver.Bundle("ccl", "app.ct", orbtBytes, outDir, spec, verbose: true);
```

`HostInit` injects arbitrary statements right after the runtime is constructed —
for one-off host configuration the runtime doesn't expose through its
constructor.

### Platform initialization: `IHostedRuntimeSetup`

A binding that needs framework setup before any module code runs (a UI
framework, a device, config loading) implements `IHostedRuntimeSetup`:

```csharp
public sealed class HostRuntimeSetup : IHostedRuntimeSetup
{
    public void Setup(IHostedRuntime runtime)
    {
        // Remember the runtime so the bindings can invoke Contract lambdas.
        if (runtime is ContractRuntime rt) UiBinding.HostRuntime = rt;
    }
}
```

`BundleDriver` finds the first such type in the binding assemblies and calls
`Setup(rt)` before `RunModule`. Keep it **thin** — a capture, not a bootstrap.
The convention is that the *Contract program* drives startup through a `Host`
binding (e.g. Crituque's `Host.Run(fun -> { ... })` boots Avalonia, runs the
callback on the UI thread, and pumps until the main window closes). That way:

- the bundle host stays a dumb shim (`ccl bundle app.ct --bind Crituque.dll`,
  no `--host` flag needed), and
- the program controls its own lifecycle — when the app starts, which window
  is the main window, when to `Host.Shutdown` — from the language, not C#.

---

## Metadata-driven binding validation

At bundle time the module's own metadata tells us what it needs. `RequiredBindingModules`
scans:

1. the format's import table (`module.Imports`), and
2. every `Call`/`Callvirt`/`NativeCall` operand's `Module.Method` string,

minus the types the module itself defines. The leftover prefixes are external
bindings — e.g. `Ui` and `IO` for the UiDemo sample.

`ccl` diffs those against its built-in stdlib module names; anything left over
must be provided by a `--bind` assembly:

```
Error: module imports binding module(s) [Ui] — pass --bind <assembly> providing them
```

You get the missing-bind failure at bundle time instead of a `call 'Ui.Create'
could not be resolved` stack trace at runtime. (`ccl` also validates at
*compile* time when compiling from `.ct`, because the semantic analyzer knows
the binding assemblies; the bundle-time check is what catches it for
precompiled `.orbt` input.)

---

## What gets copied next to the bundle

- the CLI's own runtime: `ObjectRT.Abstractions/Reader/Runtime/VM`,
  `Contract.Compiler/Runtime`, `ObjektRT.Core/Stdlib`, `Microsoft.CodeAnalysis*`
  (ReflectionJIT mode compiles at runtime),
- each `--bind` assembly **and every DLL in its directory** (its transitive
  dependencies, deduplicated by file name),
- **native assets** from `runtimes/<rid>/native` of the binding directories,
  copied flat next to the host — a bundle has no `deps.json`, so P/Invoke
  can't probe `runtimes/...` paths (this is what ships `libSkiaSharp` etc. for
  a UI binding; framework-dependent gets the current platform's natives,
  self-contained gets each `--rid`'s before its publish),
- the embedded `module.orbt` (inside `app.dll`, not a loose file).

Self-contained output is larger (the .NET runtime is inside the exe); for a
hello-world that is ~85 MB single-file / ~92 MB directory on win-x64.

---

## How a native binding resolves at runtime

A `<NativeBinding("Ui")>` contract compiles its calls to `Call` opcodes whose
operand is the string `Ui.SetTitle` (with the window handle as argument 0 for
instance methods). At runtime `ClrNativeResolver.TryResolve` splits the string
on the last dot, looks up the CLR type registered as `Ui` (registered from the
binding assembly via `RegisterBinding`/`RegisterBindingAssembly`), finds the
matching public static method by name + parameter count, and invokes it. That
resolution path is identical in the IDE and in a bundle — the only difference is
the bundle must have copied the binding assembly (and its deps) next to the
host, which `BundleDriver` does.
