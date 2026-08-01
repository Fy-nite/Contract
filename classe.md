
Short version: **the IR already models everything a class object needs, so "types as first-class values + reflection" is a 1–2 week job. Full Smalltalk-style metaclasses are a multi-week VM refactor, and probably not worth it for this runtime.** Here's the breakdown based on what's actually in the tree.

## What you already have (the good news)

The "class as a class" idea is much closer than it looks, because the type metadata already exists as first-class data in the wire model:

- `TypeRecord` (in `ObjektRT.Core/Model/ORBTModule.cs`) already carries name, namespace, base, interfaces, fields, methods, and `AttributeRecord`s — that *is* a class object, just not yet materialized as a runtime value.
- Static methods are expressible (`MethodFlags.Static`, preserved end-to-end).
- The VM already has per-module static storage (`ExecutorBase.StaticFields`, flat `Value[]`), and a real heap (objects are `byte[]` buffers sized by `InstanceSize`, `AllocObject` works).
- The spec already declares a `Reflection.Basic` capability in module metadata (ObjectIL.typ §Module Metadata, and the reader.typ example).
- `ObjektRT.Core/Core/ObjectNode.cs` has a TODO: *"Canonical Type for All Objects Inside ObjectIR. TODO: To be implemented as Object inside runtimes"* — so the intent is already there.

## The actual gaps

These are the things that would block a clean "class as a value" story:

1. **No way to load a type as a value.** The opcode table (`ObjektRT.Core/Model/Opcode.cs`) ends at `NativeCall` — there's no `ldtype`/`ldtoken`/`typeof`. `castclass`/`isinst` take type operands but only for casting, never to *produce* a type value.
2. **The wire format can't express static fields.** `FieldRecord` is just `(name_index, type_index)` — confirmed in `ObjektRT.Core`, Module.hpp (C++), and `ObjectRT.Reader`. The AST has `FieldNode.IsStatic`, but `AstToModelConverter.AddClass` silently drops it. Static-ness currently survives only *implicitly*, by whether the emitted opcode is `ldsfld` vs `ldfld`. There's also no static constructor (`.cctor`) concept anywhere.
3. **`Value` has no "type" tag.** `ValueTag` is `Nil/I4/I8/R4/R8/Obj/Str` — a class object would need a new tag (or a handle into a preallocated type-object table).
4. **Two VM backends to keep in sync.** Anything you add to `ObjectRT.VM` (C#) also lands in the D port (vm), plus `ReflectionJit` and `Runtime` consume `CompiledModule` — the cost center of any feature is "touch all 6 layers + version bump `FormatVersion 0x01`."

## Three paths, in rising cost

**Path 1 — Host-side TypeObject prototype (days, zero format changes).** Register a native `Type.Of(string) -> object`. `Program` as a value compiles to `ldstr "Program"` + `call Type.Of(string)`. The class object is a host-side descriptor built from the already-compiled `VMType`/`TypeRecord` — name, base, members, attributes, `Invoke`. Nothing in the wire format or either VM changes; it's a `Runtime` feature. You can validate the entire UX (passing `Program` around, reflecting on it) before committing to any format work.

**Path 2 — First-class type values (1–2 focused weeks).** Add `ldtype <type-index>` (the format supports extension-table opcodes), a `ValueTag.Type`, lazily-initialized singleton class object per type index, and a reflection API on it. Optionally also fix the static-field flag while you're bumping the format, since you'll be touching `ORBTWriter`/`ORBTReader`/`ObjectILParser`/`AstToModelConverter`/`ModuleSerializer` anyway. This gives you .NET/Java-style `typeof(Foo)` semantics — the model both ecosystems settled on.

**Path 3 — Full Smalltalk metaclasses (multi-week).** Class objects *own* their static state: static fields become instance fields of the class object, `ldsfld` becomes `ldfld` on it, static calls become message sends, plus class-init on first touch. This means reworking the static storage model (flat array → per-class objects) in both VMs, new dispatch rules in `ModuleCompiler`, and a `.cctor` concept that doesn't exist yet. That's the expensive one, and it only pays off if the language genuinely needs polymorphic behavior on classes themselves (subclassing a class object).

## Is it a good idea?

**Yes — as a TypeObject, not as metaclasses.** A few reasons specific to this codebase:

- It's the natural convergence point for three things already pointing at it: `TypeRecord` (the data), `Reflection.Basic` (the declared capability), and `ObjectNode` (the TODO).
- If the Vala front-end is in the picture, this is basically the GType analog — Vala/GObject already think in "class structs + type registries," so a first-class type value matches the mental model rather than fighting it.
- Full metaclass hierarchy (metaclass-of-metaclass) is the part to avoid. .NET stopped at `RuntimeType`, Java stopped at `Class`, and both are fine. Recommend: class objects are instances of a single fixed `Class` type, no user-defined metaclasses, no meta-meta.

**The honest caveat:** the actual blocker isn't metaclass machinery — it's that static state is barely expressible today (gap #2 above). Fixing the `FieldRecord` flag and adding a static constructor is prerequisite work no matter which path you take, and it's worth doing on its own.

My recommendation: prototype **Path 1** to validate the API surface against a real use case (probably the strongest argument for "pass classes around" in this project is script binding / host dispatch — e.g., the `ScriptingDemo`/`MonoGame` host registrations), then do **Path 2** if it earns its keep. Skip Path 3 unless you can name a concrete feature that requires polymorphic class behavior.