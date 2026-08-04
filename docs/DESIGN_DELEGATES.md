# Design: Delegates, Closures, and Threading

Status: **Draft — sketch for discussion, not implemented.**

This document designs first-class function values (delegates) for the Contract
language, how they lower through the IR, what the ObjektRT runtime must provide,
and how they plug into threading. It is grounded in the current compiler and the
`pure_lattice_native.h` stdlib spec (the discontinued Lattice runtime).

---

## 1. Why this is hard today

The current lambda path (`IRCodeGenerator.GenerateLambda`) has three structural
limitations:

1. **No value.** `fun x -> x + 1` becomes a static function `Global.__lambda_N`,
   and the call site works only because `_lambdaVariableMap` records the name
   *inside the compiler process*. Nothing survives into the IR as a value.
   You cannot pass `inc` to another function, store it, or return it.
2. **No capture.** Lambdas can only see their own parameters and globals. They
   cannot close over enclosing locals, so `fun y -> x + y` is impossible today.
3. **No runtime object model.** `ObjektRT.Core.ObjectNode` is a stub
   (`// TODO: To be implemented as Object inside runtimes`). There is no
   canonical object representation for the VM to dispatch on.

The `.h` delegate model (`Action`/`Func` = `{ instance: void*, methodName:
string }` + `Invoke()`) is **stringly-typed and no-arg only** — this is the
"code debt" you mentioned. We should not reproduce it.

====

charlie:
yeah, this one i'm not so proud of. however it did test out the idea of functional programming in what looks like a kotlin'ified c# language.

====



---

## 2. The core decision: what IS a delegate value?

A lambda value must be: **a method reference + an optional captured
environment (a "closure object") + a call convention**. This is the C# closure
model, and it has a key consequence:

> **Capturing lambdas become instance methods on a compiler-generated closure
> type.** Non-capturing lambdas are the zero-field special case.

The current compiler always makes lambdas *static* functions. The missing piece
is not the method — it's the *closure allocation* and *instance dispatch*.

```
fun x -> x + 1                    // non-capturing: static, zero-field closure
let base: int = 10;
fun x -> x + base                 // capturing: hoist `base` into a closure object
```

### 2.1 Charlie's questions: why not inline, and why not compile-time overloading?

> *charlie: why not provide all of the required arguments and generate function
> overloading at compile time. And why doesn't lambdas get inlined into the
> output IL if they are just x + 1?*

Both are real and should happen — they're just **optimizations / call-convention
choices on top of the same lowering**, not alternatives to it:

- **Inlining**: the compiler absolutely should inline a lambda body when the
  target is known at compile time (a direct call to a lambda *value* it can
  see). That's a peephole in codegen: instead of emitting `DelegateNode` +
  `InvokeDelegate`, substitute the body. This is the common case for `let inc =
  fun x -> x + 1; IO.Println(inc(5))`. The delegate *value* machinery is still
  needed for the cases where inlining is impossible (passed across a boundary,
  stored, or `Thread.Spawn`'d) — those need the real value. **Inlining = fast
  path, value = general path.**
- **Compile-time overloading / providing all arguments**: that's really
  describing *partial application* or *binding* — passing some args now and
  the rest later (`fun x y -> x + y` where `x` is fixed). In v1, do **not** do
  this. A delegate carries its *full* argument list in its signature; partial
  application implies creating a *new* delegate that captures the fixed args
  (which is exactly a closure). It can be sugar later: `let add5 = add(5)`
  where `add` is `(int, int) -> int` desugars to
  `let add5 = fun y -> add(5, y)`. Keep it out of the core design.

---

## 3. Language layer

### 3.1 Function types

The analyzer currently models types as **strings** (`"int"`, `"string"`,
`"int[]"`). A function type cannot be a string cleanly — it needs arity, param
types, and a return type.

**Required refactor (DONE — Phase 0 landed):** types are now `TypeDescriptor`
in `Contract.Compiler/AST/TypeDescriptor.cs` — `Named`, `ArrayOf`, `Function`
with structural equality. `Function`'s equality is hand-rolled because C#
record auto-equality does reference equality on `List<T>` properties, so a pure
`record Function(params, return)` would treat equal-but-distinct parameter
lists as unequal. (Future option: a `[ValueEquality]` source generator could
emit deep structural equality for composite types once the type system grows
more of them — Charlie's source-gen idea. Not worth it for one type today.)

```csharp
sealed class FunctionType
{
    List<TypeDescriptor> Parameters;
    TypeDescriptor Return;
    // stringifies to "(int, bool) -> string"
}
```

**Syntax (two tiers):**

```ct
let inc = fun x -> x + 1;              // inferred: (int) -> int
let add: (int, int) -> int = fun a b -> a + b;   // explicit annotation (later)
```

Inference is the v1 path: a lambda's function type is fully known from its
parameter list and body. Explicit `(T...) -> R` annotations are a parser+type
addition that can follow.

> *charlie: everything should be its native type ideally. But don't rely on
> language features that aren't reproducible outside the host environment — the
> compiler should be self-hostable in the future.*

Agreed — that's a hard constraint on this design: the descriptor model,
closures, and delegate lowering must all be expressible in Contract itself
(types, structs, classes, arrays, `object`) once the language can express them.
`FunctionType` is just a struct with a param-type list + a return type; the
closure class is an ordinary generated class. Nothing here needs a
host-only feature. Self-hosting stays possible.



### 3.2 Invocation desugaring

A call `f(args)` where `f`'s type is a function type compiles to an
**indirect invoke** of the delegate value, not a static `call`. Same syntax —
the analyzer decides which lowering applies (delegate-typed callee vs. known
function).

---

## 4. IR layer

### 4.1 New node: `DelegateNode` (or extend `NewObjInstruction`)

A delegate literal lowers to an allocation of a runtime delegate object
carrying:

```csharp
sealed record DelegateNode(
    MethodReference Target,          // the __lambda_N method (real signature)
    TypeRef? ClosureType,            // null for non-capturing lambdas
    IReadOnlyList<Expression>? CapturedArguments  // closure ctor args, or null
) : Instruction;
```

- **Non-capturing:** `DelegateNode(Target = Global::__lambda_N, ClosureType = null)`.
- **Capturing:** the compiler generates `class __closure_N { fields...;
  constructor(captured...); method __lambda_N(params...) { body } }`, and the
  node is `DelegateNode(Target = __closure_N::__lambda_N, ClosureType =
  __closure_N, CapturedArguments = [values of captured vars])`. Allocation
  order: `newobj __closure_N(captured...)`, then bind the method ref.

This mirrors the `.h`'s `{ instance, methodName }` shape, but with a **real
method token instead of a string** — no reflection-based dispatch.

### 4.2 Invocation: reuse `call` where possible, `callvirt Delegate.Invoke` for value calls

**DECIDED (implemented Phase 1, 2026-08): do it the C# way — no new instruction.**

- **Direct calls use `call`.** `let inc = fun x -> x + 1; inc(5)` compiles to
  `call Global.__lambda_N(int32) -> int32` — the compiler knows the target.
  Zero delegate value involved. This stays the fast path.
- **Function-typed *value* calls use `callvirt Delegate.Invoke`.** A lambda value
  is an instance of a compiler-generated `class Delegate { field target: string }`.
  The lambda site emits `newobj Delegate; dup; ldstr "Global.__lambda_N";
  stfld Delegate.target`. A call `f(x)` where `f` is function-typed emits
  `ldarg f; <args>; callvirt Delegate.Invoke(...)`. The interpreter special-cases
  `Delegate.Invoke`: pops the receiver, reads its `target` field, resolves that
  module function in `FunctionMap`, and calls it with the args.

This is exactly the `.h` `{ instance, methodName }` model, with the target as a
real module-function name (not reflection). **No wire-format change, no new
opcode** — `newobj`, `stfld`, `ldfld`, and `callvirt` all already exist.

> **Why not `Calli`:** it's declared in the `OpCode` enum but has no wire
> encoding (the library's tests list it under `expectedUnmapped`). Reusing
> `callvirt` avoids touching the wire table entirely.

The runtime dispatch (Interpreter.cs, `callvirt Delegate.Invoke`):
```
pop receiver (must be a Delegate object)
pop argc args
read receiver.target (a string) from the heap at the Delegate class's field offset
resolve target in Mod.FunctionMap
call it with the args
```

**Validated end-to-end** (`tests/success/Delegates.ct`): direct calls, passing
lambdas as values, higher-order functions (`apply`, `twice`), and inline lambdas
passed directly — output `6 10 11 20 12 40 101`.

### 4.3 Lowering: lambdas as instance methods

> *charlie: C# delegates are just non-static functions placed inside their
> enclosing class (Program.__lambda_N is its function name), so why not lower
> lambdas as instance methods — `this.<lambda>(<args>)` when you can get the
> object instance?*

Yes — that's the natural model, and it composes with closures:

- **Capturing lambda** `fun x -> x + base` → instance method `__lambda_N` on
  the **closure class** `__closure_N`, invoked as `__closure_N_instance.__lambda_N(x)`. The
  closure object *is* the `this`.
- **Non-capturing lambda** → still an instance method, but on a **zero-field
  singleton** — or the enclosing contract if the contract itself is an object.

So `this.__lambda_N(args)` is exactly right once the language has `this` (which
today it doesn't — contracts are modules, not objects). This is why the
`this`-capture row in §7 is "defer": the *dispatch model* (instance method on a
closure) is decided now; the *surface syntax* (`this`) waits for
contracts-as-objects.

### 4.4 `Thread.Spawn` and Func/Action

> *charlie: threads should know whether it's a Func&lt;T&gt; or an Action&lt;T&gt;.*

Agreed — the delegate type carries whether it returns a value, and that matters
to the runtime:

- **Action** (void delegate): the thread entry invokes and discards.
- **Func&lt;T&gt;** (value delegate): the thread entry invokes and stores the result
  somewhere retrievable — a future `Thread.Join`/`Thread.Result` query, or a
  channel. The delegate's *return-type tag* (`void` vs `T`) is part of its
  signature (the `TypeRef.Return` on the method token), so the runtime can
  distinguish without extra machinery.

`Thread.Spawn(delegate)` needs no special IR — the delegate is an ordinary
object value. The VM's thread entry is *itself* an `InvokeDelegate` on a new OS
thread. **The delegate object must carry no thread affinity** (see §6).



---

## 5. Compiler phases (buildable in this repo today)

The compiler side produces the IR; the VM implements the contract. Phases 0–2
are fully implementable and testable here (the `.oir` text output is the
contract).

### Phase 0 — Type model refactor
Grow types from strings to descriptors; add `FunctionType`; make lambdas infer
a function type. No behavior change for existing programs (string-based types
remain the common case). *Prereq for everything.*

### Phase 1 — Non-capturing delegates (DONE, 2026-08)
- Lambda values emit a compiler-generated `Delegate` class (`{ target: string,
  closure: object }`) via `newobj` + `stfld`; direct calls stay `call`.
- Function-typed values call via `callvirt Delegate.Invoke` (the C# way, §4.2).
- Validated end-to-end: `tests/success/Delegates.ct` → `6 10 11 20 12 40 101`.

### Phase 2 — Capture / closures (DONE, 2026-08)
- Free-variable analysis collects identifiers in the lambda body not bound by
  lambda params / nested params / block locals, whose type is known in the
  enclosing function scope.
- Captures hoist into a generated `__closure_N` class (a field per capture).
  The lambda becomes `Global.__lambda_N(__closure: object, ...params)`, and the
  body reads/writes captures as `ldfld __closure_N::v` on the closure arg.
- Lambda value emission: allocate the Delegate, then the closure, fill capture
  fields from the enclosing scope, store the closure into `Delegate.closure`,
  store the target name. `Delegate.Invoke` dispatch prepends the closure object
  as the target's first argument when present.
- **Capture semantics: BY VALUE** (the closure field copies the value at
  creation). This deviates from the §7 "C#-like by-reference" recommendation —
  writes *through the closure field* work (counters, factories), but mutations
  of the original variable after capture are not seen. A C#-style closure cell
  is future work.
- **New syntax:** `fun (x: int, y) -> { stmts }` — parenthesized params
  (optional types) + block bodies; the old `fun x -> expr` and `fun a b -> expr`
  forms still work. Block bodies are real statement blocks (`return` works).
- **v1 boundary:** nested lambdas can't capture the outer lambda's params/locals
  (only the enclosing *function's* scope). Closure *factories* work because the
  factory's own locals are its method scope (see `makeCounter` in
  `tests/success/Closures.ct`).
- Validated end-to-end: `tests/success/Closures.ct` →
  `10 11 15 1 2 11 12 101 13` (block lambda, by-value capture, captured write
  counter, independent closure-factory counters).

### Phase 3 — Threading (DONE, 2026-08)

**How it works:** the interpreter's call stack (`_stack`/`_frames`) is
per-executor, but the heap/statics/string table live on a shared
`ExecutorState`. A spawned thread gets a **fresh `Interpreter` sharing the
module state** — so the delegate handle and its closure (created on the main
thread) are valid on the new thread, and the thread has its own call stack.

- `ObjectRT.VM.ExecutorState` — the shared heap/statics/strings (string table
  locked; heap allocation unlocked, documented as programmer's responsibility).
  `ExecutorBase` now owns a `State` reference with `Heap`/`StaticFields`/
  `InternString`/`GetStringValue` aliases so existing code is unchanged.
- `Interpreter.ReadDelegate(mod, state, handle)` — the delegate-dispatch
  resolution (target name + closure) extracted from the `Delegate.Invoke` case
  and shared with threads. `Interpreter.RunDelegate(handle, args)` runs a
  delegate's target as a standalone call (thread entry point).
- `Runtime.SpawnThread(handle)` — registers `Thread.Spawn` as an explicit
  native; spawns a background `Thread` running `RunDelegate` on a fresh
  interpreter with `NativeCallHandler` wired to the runtime's resolver chain.
- Compiler: `ObjektRT.Stdlib.Threading.Thread.Spawn(object d)` stub so the
  analyzer accepts the call; the runtime host binding takes precedence at
  dispatch.
- **v1 surface:** `Thread.Spawn(delegate)` (fire-and-forget, background
  thread), `Thread.Sleep`. No join/result/locks yet.
- Validated end-to-end: `tests/success/Threading.ct` → non-capturing lambda
  prints from a thread; a **capturing** lambda mutates its closure field
  (`n += 1`) on the spawned thread through the shared heap.

---

## 6. The ObjektRT runtime contract (what the VM must provide)

1. **A canonical object representation.** `ObjectNode` must stop being a stub:
   objects need a type tag, field slots (for closures and struct instances),
   and either a method-dispatch table or token-based method resolution.
2. **Indirect invocation.** Execute a delegate: given a heap handle, read the
   target method name + closure, call the module function (static or instance).
   Implemented as `Interpreter.ReadDelegate`/`RunDelegate`.
3. **Thread entry.** Start an OS thread whose entry point is "invoke this
   delegate" — implemented via `Runtime.SpawnThread` sharing `ExecutorState`.
4. **Boxing.** Values stored in `object` slots (delegate args/results of type
   `object`) need boxing. The IR already has `Box`/`Unbox` opcodes.
5. **Allocation + lifecycle.** Closure objects must outlive the frame that
   created them. Requires the allocator/GC the `.h` hints at
   (`GC.Collect`, `Malloc.GetUsedMemory`).


====

charlie:
one thing i've been talking about with my developers is representing classes as classes. like `class class` where class has underlying fields. even in IL land. now i do not know how that would work and i am open to the idea to talk about it.

====




---

## 7. Design decisions to settle (flagged for discussion)

| Decision | Option A | Option B | Recommendation |
|---|---|---|---|
| **Capture semantics** | By value (copy on create) | By reference (C#-like closure cell) | **B** — matches expectations; document races |
| **Delegate equality** | Reference equality | Structural | Reference equality in v1 — `==` must mean "this exact delegate" |
| **Invoke mechanism** | Dedicated `InvokeDelegate` | Reuse `Calli` | Dedicated opcode — `Calli` has no wire encoding today |
| **Thread results** | Fire-and-forget void | Func returns boxed result | **Threads know Action vs Func** — void vs value is part of the delegate signature; store Func results for `Join`/`Result` |
| **`this` capture** | Allow closure over `this` | Defer | Defer — contracts-as-objects is a separate feature; the dispatch model still lands now |
| **Method token** | String method name (`.h` model) | Real `MethodReference` token | **Real token** — this is the fix for the stringly-typed code debt |
| **Lowering** | Instance method on closure (C#-like) | Static method + captured params | **Instance method on closure class** — `this.__lambda_N(args)` once `this` exists |
| **Inlining** | Always create a delegate value | Inline when target known at compile time | **Inline fast path, value general path** — inline direct calls, keep values for boundaries |
| **Partial application** | Support `add(5)` sugar | Defer | **Defer** — it's sugar over closures; can desugar later to `fun y -> add(5, y)` |
| **Self-hostability** | Design for it now | Defer | **Design for it now** — descriptors/closures/delegates must be expressible in Contract itself |


> **Charlie's rulings** (capture, equality, calli, threads, method token):
> - Capture semantics: **like C#** (by reference, closure cell).
> - Delegate equality: **reference equality** — `==` must mean "this exact delegate".
> - Invoke: **calli effectively doesn't exist** (matches the codebase finding in §4.2) — use a dedicated opcode.
> - Threads: **know whether it's a Func&lt;T&gt; or Action&lt;T&gt;** (void vs value is part of the signature).
> - Method token: **method references, not strings**.

---

## 8. The piece that previously didn't work "alongside threading"

The failure mode is usually one of:

1. **Delegate captured stack state.** If the closure lives on the creating
   thread's stack (or the lambda is lowered to a bare name in a compiler map),
   the spawned thread has nothing valid to invoke. Fix: the closure is a
   **heap object** created at the lambda site; the delegate value is
   self-contained.
2. **Thread-local dispatch.** If invocation routes through the creating
   thread's symbol tables, it breaks cross-thread. Fix: the delegate carries its
   own method token; dispatch is a pure function of the delegate object.
3. **Race on captured mutable state.** A captured `var` written from two
   threads races. Fix for v1: by-reference capture + documented rule that
   sharing mutable captures across threads is the programmer's responsibility,
   exactly like C#.

The invariant to design around:

> **A delegate value is a self-contained heap object: method token + closure
> object. It can be created on any thread and invoked on any thread.**

====

charlie: really nothing is working alongside anything XD
most of it is string together and i'm hopiNg at some point to get it to be properly in place.

====



---

## 9. What "done" looks like

```ct
Contract Program {
    fn apply(f: (int) -> int, x: int) -> int {
        return f(x);
    }

    static fn Main() {
        let inc = fun x -> x + 1;
        IO.Println(apply(inc, 5));      // 6 — passed as a value

        var base: int = 10;              // captured
        let addBase = fun x -> x + base; // closure over `base`
        IO.Println(addBase(1));          // 11

        Thread.Spawn(fun -> IO.Println("from thread"));  // fire-and-forget
        Thread.Sleep(100);
    }
}
```
====

charlie:
already loving this!!!!

====


---

## 10. Suggested execution order

1. **Phase 0** — type descriptors (small, risky refactor, do first, alone).
2. **Phase 1** — non-capturing delegates + `InvokeDelegate` (compiler-side
   complete; unblocks `Thread.Spawn`).
3. **Phase 2** — closures (free-variable analysis + closure hoisting).
4. **Phase 3** — runtime: ObjectNode, indirect invoke, thread entry.
5. **Conformance** — `tests/success/Delegates.ct`, `Closures.ct`,
   `Threading.ct`; extend the `.h` spec only where the runtime contract lands.

Each phase is independently testable: phases 0–2 produce `.oir` text you can
assert on today, before the VM exists.

---

## 11. Open question: "representing classes as classes"

> *charlie: one thing i've been talking about with my developers is representing
> classes as classes. like `class class` where class has underlying fields.
> even in IL land. now i do not know how that would work and i am open to the
> idea to talk about it.*

This is the **metaclass** idea, and it's separate from — but the natural
foundation of — `this`-capture and contracts-as-objects. It deserves its own
design pass, so here's the shape of the discussion:

### What "class class" means

Today a `Contract` is a *module*: a bag of static functions. There's no value
that *is* a `Contract`. "Class as a class" means a class (and its static state,
constructors, and methods) is itself **an object with a type** — so you can
refer to `Program` as a value, pass it around, and (eventually) reflect on it.

### How it could work in IL land

Two practical shapes:

- **A. Classes as ordinary objects with a type tag.** Every class compiles to a
  runtime object whose type is its own (generated) metaclass. Static methods
  become instance methods on the metaclass; static fields become its fields.
  This is the Smalltalk/self-hosting model. The IR already has `ClassNode`,
  `MethodNode`, `FieldNode`, and `ObjectNode` — the work is *linking* a class
  to its metaclass object and giving the VM a way to instantiate it.
- **B. Classes as descriptors.** A class compiles to a data structure
  (name + method table + field layout), and "class objects" are just instances
  of a `Type`-like descriptor. Lower overhead, less "everything is an object".

### Why it matters here

1. **`this`-capture** (deferred in §7) needs an object to be `this` — a
   contract-as-object gives you that.
2. **Self-hosting** (your constraint in §3.1) basically requires the compiler
   to describe types as *data*, which is either A or B.
3. **Closures** in §4 are a special case: a closure class is just a class whose
   instance is the captured environment. "Classes as classes" generalizes that.

### Recommendation

Discuss this as its own design doc (it's bigger than delegates). If it's ever
built, it likely **precedes or parallels** Phase 3's runtime object model, and
it makes `this`-capture free. The delegate work in Phases 0–2 doesn't depend on
it, so we can proceed either way.

### 11.1 Research note: the three paths (classe.md)

A research pass on this idea produced a breakdown that matches the codebase
(`classe.md` at the repo root; all wire-format claims verified against this
tree):

**What's already there**
- `TypeRecord` (`libs/ObjektRT.Core/Model/ORBTModule.cs`) already carries name,
  base, interfaces, fields, methods — that *is* a class object, just not
  materialized as a runtime value.
- Static methods are expressible (`MethodFlags.Static`, preserved end-to-end).
- The spec already declares a `Reflection.Basic` capability in module metadata.
- `ObjectNode` (`Core/ObjectNode.cs`) carries the TODO: *"Canonical Type for All
  Objects Inside ObjectIR. TODO: To be implemented as Object inside runtimes"* —
  the intent is already there.

**The real gaps (verified in this tree)**
1. **No way to load a type as a value.** The `Opcode` table (`Model/Opcode.cs`)
   ends at `NativeCall` — no `ldtype`/`ldtoken`/`typeof`. `castclass`/`isinst`
   take type operands but only for casting, never to produce a type value.
2. **The wire format can't express static fields.** `FieldRecord` is just
   `(NameIndex, TypeIndex)`; `AstToModelConverter.AddClass` emits
   `new FieldRecord(Intern(f.Name), Intern(f.FieldType.Name))` and silently
   drops `FieldNode.IsStatic`. Static-ness survives *only implicitly*, via
   `ldsfld`/`stsfld` opcodes. There's also no static constructor (`.cctor`)
   concept anywhere. (The language can't even declare a field on a `Contract`
   today — only structs have fields — so static state is doubly unexpressible.)
3. **`Value` has no "type" tag.** `ValueTag` is `Nil/I4/I8/R4/R8/Obj/Str` — a
   class object needs a new tag (or a handle into a type-object table).

**Three paths, rising cost** (the research pass's recommendation)
- **Path 1 — Host-side TypeObject prototype (days, zero format changes).**
  Register a native `Type.Of(string) -> object`. `Program` as a value compiles
  to `ldstr "Program"` + `call Type.Of(string)`. The class object is a
  host-side descriptor built from the already-compiled type metadata — name,
  base, members, attributes, `Invoke`. No wire/VM format changes. Validates the
  UX before committing to format work.
- **Path 2 — First-class type values (1–2 focused weeks).** Add `ldtype
  <type-index>` (the format supports extension-table opcodes), a `ValueTag.Type`,
  lazily-initialized singleton class object per type index, and a reflection
  API. Optionally fix the static-field flag while bumping the format.
  .NET/Java-style `typeof(Foo)` semantics.
- **Path 3 — Full Smalltalk metaclasses (multi-week).** Class objects *own*
  their static state; `ldsfld` becomes `ldfld` on the class object; static
  calls become message sends; class-init on first touch. Reworks the static
  storage model in both VMs. **Not recommended** unless the language needs
  polymorphic behavior on classes themselves.

**Recommendation (agreed):** TypeObject, not metaclasses. Class objects are
instances of a single fixed `Class` type — no user-defined metaclasses, no
meta-meta (the same place .NET's `RuntimeType` and Java's `Class` stopped).
The honest blocker is **gap #2 (static state)**, which is prerequisite work no
matter which path, and worth doing on its own. Suggested order: prototype
Path 1 to validate the API against a real use case (script binding / host
dispatch), then Path 2 if it earns its keep; skip Path 3.

Note: the VM-side claims in the research pass (`ExecutorBase.StaticFields`,
`AllocObject`, the D port, `ReflectionJit`) are about the runtime repo, which
is not in this workspace — this tree is the compiler + core library. The
compiler-side prerequisite stands regardless: give contracts the ability to
declare fields, and make the wire model preserve static-ness, before any
class-as-value work.
