# Generic Materialization (`@Generic`)

Status: **implemented** (2026-08).

User-defined generic contracts are **materialized at runtime**: the compiler
emits a generic *definition* (a template), and the VM lazily clones +
specializes it for each concrete instantiation at first use — the same idea as
C#'s runtime generics, done with string substitution because every type in
this IR is an interned string.

This document describes the wire protocol (`@Generic`), what the compiler
emits, what the VM does, and the exact substitution rules.

---

## 1. The two runtime shapes

### Definitions (the template)

A generic contract is emitted as an ordinary class carrying an `@Generic`
attribute listing its type parameters. Its body references those parameters
**literally**:

```text
@Generic(T)
class Box {
    public field value: T

    constructor(this: object, v: T) { ... }
    method get(this: object) -> T { ... }
}
```

The definition is never instantiated directly — it is the template the VM
clones from. It is still compiled (its methods land in the function table as
`Box.get`, etc.), which keeps inheritance resolution simple, but no code
references it by the bare name.

### Instantiations (the specialization)

Call sites reference concrete instantiations by their **materialized name**:

```text
newobj Box<int32>
call Box<int32>..ctor(object, int32) -> void
call Box<int32>.get(object) -> int32
ldfld Box<int32>::value
```

The VM clones `Box`, substitutes `T -> int32` (and the class name
`Box -> Box<int32>`) in every type position, and compiles the specialized
class. `Box<int32>` and `Box<string>` are distinct runtime classes with
distinct fields, methods, and (for statics) storage.

## 2. Syntax

```text
contract Box<T> { ... }              // definition
contract Pair<T, U> { ... }          // multi-parameter

var b = new Box<int>(5);             // explicit args on new
var b: Box<int> = ...;               // annotation
b.get()                              // instance call (args from the variable)
Box::wrap(7)                         // static call, T inferred from args
Box<int>::reset()                    // static call, explicit args (no inferable args)
Reflect.HasType("Box<int32>")        // the materialized name is real

fn identity<T>(x: T) -> T { ... }    // generic FUNCTION — see §4
```

## 3. What the compiler emits

### Definition side

- `@Generic(T, U, ...)` attribute on the class (survives both the text and the
  binary ORBT formats — attributes ride the type-level attribute list).
- **Contract** type parameters are written literally: field types, constructor
  params, method params/returns, and locals all say `T`.
- The class's own name in field/call references inside the body stays the
  definition name (`ldfld Box::value`, `call Box.get`), so the substitution
  can rewrite them during materialization.

### Use side

- `new Box<int>(5)` → `newobj Box<int32>` + `call Box<int32>..ctor(object, int32)`.
- `b.get()` where `b: Box<int>` → `call Box<int32>.get(object) -> int32`
  (signature substituted to the concrete args).
- `Box::wrap(7)` → T inferred → `call Box<int32>.wrap(int32) -> int32`.
- `Box<int>::reset()` → explicit args → `call Box<int32>.reset()`.
- Field access `b.value` → `ldfld Box<int32>::value`.
- Inside the definition's own body, calls/fields keep the definition name —
  the clone's substituted instructions become the specialized refs.

### Type-argument plumbing (analyzer)

At each call site the analyzer links a *substituted copy* of the target
declaration (concrete param/return types for inference) and attaches
`FunctionDeclaration.TypeArguments` — the concrete contract arguments in
declaration order. The codegen uses those for the materialized name and the
wire signature, and the *original* declaration for the erased parts.

## 4. What stays type-erased

- **Function-level type parameters** (`fn identity<T>(x: T) -> T`) erase to
  `object`: a function is a single runtime entity, so `identity<int>` and
  `identity<string>` are the same `Program.identity(object) -> object`.
  Explicit type args at the call site are compile-time only.
- **Stdlib generics** (`List<T>`, `Dict<K,V>`, `Delegate<F>`) remain erased
  `object` handles — their runtime classes are native, not Contract classes.

So the rule: *contract type parameters materialize; function type parameters
erase.*

## 5. What the VM does (lazy, at first use)

`ModuleCompiler.Compile` runs a materialization pre-pass before building its
resolution tables:

1. **Collect definitions.** Any type with an `@Generic(...)` attribute is a
   template; the attribute args are its type-parameter names.
2. **Scan for references.** Every method's bytecode is decoded and inspected:
   `newobj`/`newarr` type operands, `castclass`/`isinst`/`conv` type operands,
   `ldfld`/`stfld` declaring types, and `call`/`callvirt` targets. Any name of
   the form `Base<arg, ...>` whose base is a known definition and whose arity
   matches is queued for materialization.
3. **Materialize.** For each queued instantiation (deduplicated), clone the
   definition's `TypeRecord`:
   - name → `Base<arg, ...>` (re-interned);
   - field/param/local/return types → substituted;
   - instruction operands → substituted (field refs, call refs, type refs);
   - the `@Generic` attribute is dropped from the clone (it is a concrete
     class, not a template).
   The clone is appended to the module, then **scanned again** — nested
   instantiations (a materialized method referencing another specialization)
   queue recursively until stable.
4. **Compile normally.** Definitions and clones flow through the existing
   pipeline: resolution tables, field offsets, function compilation, string
   table. Each clone gets its own `VMType`, field slots, and function-table
   entries.

Only instantiations actually referenced by the code are materialized — a
module that never uses `Box<string>` never builds it.

## 6. The substitution rules

Materialization is string-level (types are interned strings). For a clone of
definition `Box` with params `[T]` and args `[int32]`, materialized name
`Box<int32>`:

1. **Type parameters** — replace `T` with `int32` where not adjacent to a name
   character: `T` → `int32`, `T[]` → `int32[]`, `List<T>` → `List<int32>`,
   `(int32) -> T` → `(int32) -> int32`. Names like `Type` (where `T` is
   followed by a letter) are untouched.
2. **Definition name** — replace `Box` with `Box<int32>` when it appears as a
   qualified declaring type: `Box::value` → `Box<int32>::value`,
   `Box.get` → `Box<int32>.get`, `Box..ctor` → `Box<int32>..ctor`, or exactly
   `Box`. `Box` inside an already-materialized `Box<int32>` (followed by `<`)
   is left alone.

Order matters: params first, then the definition name.

## 7. Wire-format notes

- The text tokenizer treats `<`/`>` as identifier-continuation characters, so
  `Box<int32>` is one identifier — but `,` is not, so multi-argument names
  (`Pair<int32, string>`) split across tokens. `ObjectILParser` joins tokens
  while angle brackets are unbalanced (preserving the `, ` separator) wherever
  a qualified name is read: call targets, `newobj` types, field refs, and
  `ReadTypeName` (locals/params/returns/fields).
- The binary ORBT format needs no changes: type-level attributes already carry
  `@Generic`, and type/field/method names are plain strings.

## 8. Limitations (v1)

- `new T()` is still impossible: the template's `T` is a name, not a
  constructible type. Only monomorphization with a real type would allow it.
- Generic structs: the IR's `StructNode` has no generic parameters, and
  structs lack a verified runtime execution path — planned later.
- Generic inheritance (`Box<T> : Base<T>`) materializes with the base resolved
  to the *definition*'s base; deep generic base chains are untested.
- No constraints (type-parameter bounds) yet.
