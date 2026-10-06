# Assembly-link

Reference a real .NET assembly and call its public types **directly by their CLR
name**, exactly as in C# — no hand-written `<ClrImport>` facade and no
`[ClassBinding]` wrapper.

```ct
import __builtin.std;

<AssemblyRef("System.Private.CoreLib")>
Contract CoreLib { }

Contract Program {
    static fn Main() {
        IO.Println(System.Math.Abs(-3.5));          // static
        var sb = new System.Text.StringBuilder();   // constructor
        sb.Append("hello");                         // instance method
        sb.Append(" world");
        IO.Println(sb.ToString());                  // chain + instance
        IO.Println(sb.Length);                      // property read
    }
}
```

Everything a linked type exposes is registered as an external `ClrImport`
contract: public static/instance methods (including inherited), public fields
and properties, and public constructors. Return types are mapped back to the
linked type, so instance chains type-check and resolve.

## Enabling it

Any one of:

| Surface | Example |
|---------|---------|
| CLI flag (repeatable) | `ccl app.ct --link V12.Core` or `--link ./bin/V12.Core.dll` |
| Project file | `{ "LinkAssemblies": ["V12.Core"] }` in `contract.ctproj` |
| Compiler API | `ContractCompiler.CompileFile(path, out diag, linkedAssemblies: new[] { asm })` |
| Source attribute | `<AssemblyRef("V12.Core")>` or `<AssemblyRef(Path: "V12.Core.dll")>` |

Linked assemblies may be given as a simple assembly name (already loaded or
resolvable by the loader) or a path relative to the source file. The
`<AssemblyRef>` attribute is a **compile-time** directive; it is honored on any
contract and does not change that contract's other semantics.

Want the same surface as *source* instead of a runtime link — facade `.ct`
files for the editor, or a `.coi` that ships them? `ccl bindgen` reuses this
assembly-link reflection walk to emit `<ClrImport>` facades for every public
type; see [BINDGEN.md](BINDGEN.md).

## Runtime dispatch

Call sites are emitted as `Type.Method` targets and resolved by the runtime's
CLR reflection resolver. Register the same assembly before running:

```csharp
var rt = new ContractRuntime();
rt.RegisterLinkedAssembly(typeof(V12.Core.World).Assembly); // full name + short name
```

When the source uses `<AssemblyRef("...")>`, `ccl run`/`PrepareModule` reads the
emitted `@AssemblyRef` metadata and registers the assembly automatically, so a
self-describing script runs with no host wiring.

From a game host, pass the assemblies when creating the host:

```csharp
var host = ContractV12Host.Create(scriptPath, new[] { typeof(World).Assembly });
// or ContractScriptRuntime(linkedAssemblies: new[] { ... })
```

## How it differs from the neighbours

- **`--bind` / `[ClassBinding]`** expose *only* types explicitly annotated and
  only under the wrapper's flattened API (ids, primitives). Assembly-link exposes
  the real type surface of the whole assembly.
- **`<ClrImport("Fully.Qualified.Type")>`** links one type but requires you to
  author a bodyless facade contract in `.ct`. Assembly-link synthesizes that
  facade from reflection for every public type.
- **`import "lib.orbt"`** statically links a compiled ObjektIL module.
  Assembly-link targets a live .NET assembly and dispatches by reflection.

## Bindings own their names

A registered binding module wins over a linked CLR type. If a real type's full
name equals a `[ClassBinding]`/host module name — the engine's `V12.Registry`
class vs the `V12.Registry` binding, for example — the link skips synthesizing
the type and `RegisterLinkedAssembly` leaves the bound facade in place, so
`V12.Registry.RegisterString(...)` keeps resolving to the binding. Scripts that
need the real type under a colliding name are out of luck in v1: the binding
facade is the intended surface for that name.

## Limits (v1)

- Public **top-level, non-generic** types only (nested types and open generic
  definitions are skipped). References to an open generic (`List<T>`) and to
  delegates map to `object`.
- Other assemblies' reference types (not themselves linked) map to `object`.
- Overloads are matched by **arity** at the call site; the runtime then picks the
  concrete overload from the argument values (reflection scoring).
- Indexers and generic methods are not exposed.
- A short type name shared by two linked types resolves to the first
  registered; use the full dotted name to disambiguate.
