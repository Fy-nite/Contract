# Reflection API

C#-style reflection over compiled Contract / ObjectRT modules. You can inspect
types, methods, fields and attributes, walk inheritance hierarchies, resolve
**method references** — including inherited ones — and invoke them through a
live runtime. Think `System.Reflection`, but for `.oil` / `.orbt` modules.

The API lives in `ObjectRT.Runtime.Reflection` and is exposed two ways:

| Entry point | Description |
|---|---|
| `Runtime.GetReflector()` | Reflection over the module currently loaded in a runtime |
| `ModuleReflector.From(module)` | Reflection over any parsed `ORBTModule`, without loading it |

`ContractRuntime` (the Contract-specific host) adds conveniences:
`rt.Reflector` (loaded module) and `ContractRuntime.ReflectModule(module)`.

```csharp
using Contract.Runtime;
using ObjectRT.Runtime.Reflection;

var rt = new ContractRuntime();
rt.RunModuleFile("app.orbt");

var refl = rt.Reflector;                       // or ContractRuntime.ReflectModule(module)
var player = refl.GetType("Player");           // TypeInfo?
```

---

## 1. Getting a module

A module is an `ORBTModule` object. Any of these give you one:

```csharp
// From the Contract compiler, without writing a file:
var mod = ContractCompiler.CompileFileToModule("app.ct", out var diag);

// From IR text (.oil / .oir):
var mod = ObjectRT.Reader.OilFileReader.ParseFile("app.oil");
var mod = ObjectRT.Reader.OilFileReader.ParseString(oilSource);

// From compiled binary (.orbt):
var mod = ObjectRT.Reader.OrbtFileReader.ReadFile("app.orbt");

// From a runtime that has loaded a module:
var refl = runtime.GetReflector();             // null until something is loaded
```

`ModuleReflector.From(mod)` wraps one of these and gives you the query API.

## 2. Types

```csharp
var refl = ModuleReflector.From(mod);

refl.ModuleName;                    // "MyGame"
refl.GetTypes();                    // IReadOnlyList<TypeInfo> — all types, declaration order
refl.GetType("Player");             // TypeInfo? — by simple name
```

`TypeInfo` mirrors `System.Type`:

```csharp
var t = refl.GetType("Player")!;

t.Kind;            // TypeKind.Class / Interface / Struct / Enum
t.Access;          // MemberAccess.Public / Private / Protected / Internal
t.IsClass; t.IsInterface; t.IsStruct; t.IsEnum;
t.IsAbstract; t.IsSealed;

t.Name;            // "Player"
t.BaseType;        // TypeInfo? — direct base, or null (no base / external base)
t.Interfaces;      // IReadOnlyList<TypeInfo?> — direct interfaces (null if external)
t.GetAttributes(); // IReadOnlyList<AttributeInfo> — @Name(args) annotations
```

Inheritance helpers:

```csharp
t.GetHierarchy();                     // this + all bases, most-derived first
t.IsSubclassOf(other);                // transitively inherits from `other`
t.IsAssignableFrom(other);            // other == this, subclass, or interface implementor
t.GetInterfaces();                    // all interfaces, including inherited
```

## 3. Methods — and method references

`MethodInfo` is the method reference: it knows *which* method (declaring type +
name), its signature, and how to invoke it. Lookup is **inheritance-aware**:

```csharp
// On a type — walks the base chain, most-derived declaration wins (like
// System.Type.GetMethod). An override on a derived type shadows the base one.
var describe = player.FindMethod("Describe");   // → Circle's override, not Shape's

player.GetMethod("Describe");                   // alias of FindMethod
player.GetDeclaredMethod("Describe");           // own declarations only, no inheritance
player.GetMethods();                            // declared + inherited, most-derived first
player.GetDeclaredMethods();                    // own only

// By qualified name, from the reflector — also inheritance-aware:
var triple = refl.FindMethod("Calc.Triple");    // resolves even when declared on MathBase
```

Method metadata:

```csharp
describe.Name;            // "Describe"
describe.QualifiedName;   // "Circle.Describe" — the key used by Runtime.CallMethod<T>
describe.DeclaringType;   // TypeInfo — the type that DECLARES it (base for inherited)
describe.ReturnTypeName;  // "string" / "int32" / "void" / ...
describe.ParameterCount;  // instance methods include 'this' as parameter 0
describe.GetParameters(); // IReadOnlyList<ParameterInfo> (Name + TypeName)

describe.IsStatic; describe.IsVirtual; describe.IsOverride; describe.IsAbstract;
describe.GetAttributes();                       // method-level @Name(args)

// The root of an override chain:
describe.GetBaseDefinition();                   // → Shape.Describe (virtual root)
```

**Invoking a method reference** — through a runtime that has the module loaded:

```csharp
// Static method — receiver is null:
var n = triple.Invoke(rt, null, 7);             // → object? holding the result

// Instance method — receiver is the object handle from a prior call:
object? playerHandle = rt.CallMethod<object>("Game.CreatePlayer");
var name = describe.Invoke(rt, playerHandle);   // 'this' is passed as arg 0

// Same thing via raw qualified-name calls:
int n = rt.CallMethod<int>("Calc.Triple", 7);            // static
object? s = rt.CallMethod<object>("Game.CreatePlayer");  // instance factory
```

> **Receiver handles.** VM-internal objects round-trip through
> `CallMethod<object>` as raw `uint` heap handles. Pass that straight back as
> the receiver to `MethodInfo.Invoke` (or as arg 0 of an instance
> `CallMethod<T>`). Host/external objects (arrays, stdlib objects) come back as
> their CLR values instead.

### Inheritance-aware calls

Because `Runtime.CallMethod<T>` and the interpreter both resolve method names
through the base chain, you can call an inherited method *through the derived
type's name* — exactly like C#:

```csharp
// MathBase declares Triple; Calc : MathBase declares nothing named Triple.
rt.CallMethod<int>("Calc.Triple", 7);   // works — resolves to MathBase.Triple
triple.Invoke(rt, null, 6);             // MethodInfo.Invoke uses the declaring type
```

Overrides resolve to the most-derived declaration, so
`refl.FindMethod("Square.Describe")` returns the override (declaring type
`Circle`), and `GetBaseDefinition()` walks back up to the virtual root.

## 4. Fields

```csharp
player.GetField("health");     // FieldInfo? — walks the base chain
player.GetDeclaredField("health");
player.GetFields();            // declared + inherited
player.GetDeclaredFields();

f.Name;            // "health"
f.TypeName;        // "int32"
f.DeclaringType;   // TypeInfo
f.QualifiedName;   // "Player.health"
f.IsStatic;        // carried in the module metadata (see note below)
```

> **Static fields.** The ORBT wire format (v0x02) now carries a per-field static
> flag, so `FieldInfo.IsStatic` reflects what the module declares —
> `static field total: int32` in IR text. v0x01 modules still load (the flag is
> absent and reads as `false`).

## 5. Attributes

Module attributes (`@Name(args)`) are surfaced on types and methods:

```csharp
foreach (var attr in player.GetAttributes())
{
    attr.Name;         // "Author"
    attr.Arguments;    // IReadOnlyList<string> — string args are unquoted
}
```

For CLR-side *binding* attributes — `[ClassBinding("Name")]` on host classes,
`[IRClassBinding]` / `[IRMethodBinding]` on proxy interfaces — see the runtime
bindings docs; those are discovered via `RegisterBindingAssembly` /
`RegisterClrType`, not via module reflection.

## 6. Example — a full embed

```csharp
using Contract.Runtime;
using ObjectRT.Runtime.Reflection;

// 1. Compile from source (no temp files)
var mod = ContractCompiler.CompileFileToModule("game.ct", out var diag);
if (mod == null) { foreach (var e in diag.Errors) Console.WriteLine(e); return; }

// 2. Run it
var rt = new ContractRuntime();
rt.RunModule(mod);

// 3. Reflect over what just ran
var refl = ModuleReflector.From(mod);
var factory = refl.GetType("GameFactory");
var make = factory!.FindMethod("CreatePlayer");          // static

object? handle = make!.Invoke(rt, null);                  // → uint handle
var player = refl.GetType("Player")!;

var describe = player.FindMethod("Describe");             // most-derived override
Console.WriteLine(describe!.Invoke(rt, handle));          // "circle"

// Inspect attributes / hierarchy
Console.WriteLine(string.Join(", ", player.GetAttributes()));
Console.WriteLine(string.Join(" <- ", player.GetHierarchy().Select(t => t.Name)));
```

## 7. Limitations

- **External bases** (declared in another module) are not indexed — the reader
  drops their names, so `BaseType` is `null` and cross-module method lookup
  stops at the module boundary.
- **Field access** is metadata-only in this API: there's no
  `GetValue`/`SetValue`. Static-field *state* lives in the VM's static slots and
  is not yet exposed.
- `FieldInfo.IsStatic` for v0x01 (pre-flag) modules is `false` — the old format
  had no way to express it.
- Method signature matching (`GetMethod(name, paramTypes)`) is not implemented —
  name + most-derived-first ordering is the current granularity. Two overloads
  with the same name are disambiguated by declaration order.

## Format note

ORBT binary format v0x02 adds one flag byte per field (after name + type).
Writers emit it when the module's `FormatVersion >= 2`; readers accept both
v0x01 and v0x02. Text IR uses `static field name: type` (and
`public static field ...`). The text parsers (`ObjectILParser` on both the
compiler and runtime sides, `TextIrParser`) and serializers (`ModuleSerializer`)
all round-trip the flag.
