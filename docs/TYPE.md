# Type System

The `Type` contract (`ObjektRT.Core.Type`) is the root reflection type for Contract — the analogue of `System.Type` in C# or `java.lang.Class` in Java. A `Type` value can represent **any** type in the loaded module: primitives (`int`, `string`, `bool`, …), contracts, structs, interfaces, enums, and generic instantiations. It is the value you pass to `Reflect` and `TypeBinding` when you want to talk about a type as data.

## 1. The `Type` contract

`Type` lives in `ObjektRT.Core` and is available after `import ObjektRT.Core;` (or via the compiled `ObjektRT.Core.orbt` reference). It is also registered as a built-in type name, so `var t: Type` is always a valid type annotation.

```ct
import ObjektRT.Core;

var t: Type = Type.Of("Player");
var t2 = new Type("Player");          // same, via constructor
var t3 = new Type(); t3.FullName = "Player"; // empty ctor + field assignment
```

### 1.1 Construction

| Form | Description |
|------|-------------|
| `new Type()` | Empty `Type` (`FullName = ""`). Set `FullName` manually for synthetic types. |
| `new Type("Player")` | `Type` for the type whose wire name is `"Player"` (short or qualified). |
| `Type.Of("Player")` | `Type` for `"Player"` if it exists, otherwise `null` (mirrors `Type.GetType`). |
| `Type.Of("com.game.Player")` | Qualified wire name — always works. |
| `Type.Exists("Player")` | `bool` — does the module contain this type? |
| `Type.ModuleName()` | `string` — the loaded module's declared name. |

`Type.Of` is the primary factory — it returns `null` when the name is not a type in the loaded module, so you can gate `Reflect`/`TypeBinding` calls with `if (t != null)`.

### 1.2 Core metadata

`Type` is a thin wrapper around a `FullName: string` (the qualified wire name). All query methods are `Type`-based and delegate to `Reflect` internally, so you never pass raw `string` type names yourself:

```ct
var t = Type.Of("Player");

t.FullName;          // "Player" or "com.game.Player"
t.Name();            // "Player" — short name after last '.'
t.Namespace();       // "com.game" or "" when none
t.Kind();            // "Class" / "Interface" / "Struct" / "Enum"
t.Access();          // "Public" / "Private" / "Protected" / "Internal"
t.IsClass();         // true for Class
t.IsInterface();     // true for Interface
t.IsStruct();        // true for Struct
t.IsEnum();          // true for Enum
t.IsAbstract();      // TypeFlags.Abstract
t.IsSealed();        // TypeFlags.Sealed

t.BaseType();        // Type? — direct base or null (no base / external base)
t.BaseName();        // string — wire name of direct base or "" (compat, prefer BaseType)

t.Fields();          // string[] — "Type.field" qualified, incl. inherited
t.DeclaredFields();  // string[] — own fields only
t.Methods();         // string[] — "Type.Method" qualified, incl. inherited
t.DeclaredMethods(); // string[] — own methods only
t.Attributes();      // string[] — "@Name(arg, ...)" on the type

t.Hierarchy();       // Type[] — this + all bases, most-derived first
t.Interfaces();      // Type[] — direct interfaces
t.AllInterfaces();   // Type[] — all interfaces, incl. inherited
```

All `Type[]` returns are `Type` handles (each element is a `Type` with `FullName` set), not strings, so you can chain them: `t.Hierarchy()[0].Name()`.

### 1.3 Type relations (Type-based)

```ct
var player = Type.Of("Player");
var animal = Type.Of("Animal");
var dog = Type.Of("Dog");

dog.IsSubclassOf(animal);              // true when Dog : Animal transitively
dog.IsSubclassOfName("Animal");        // string overload (for dynamic names)
animal.IsAssignableFrom(dog);          // true when dog == animal, subclass, or interface impl
animal.IsAssignableFromName("Dog");

player.FieldType("health");            // "int32" or "" when unknown
player.FieldStatic("health");          // bool
player.MethodReturn("Describe");       // "string"
player.MethodParams("Describe");       // string[] like "int32 x"
player.Equals(other);                  // FullName equality
player.ToString();                     // FullName
```

These are the `Type`-based overloads of `Reflect` — `Type` forwards `this` as a `Type` handle, not as a string, so the compiler and host never see a stringly-typed type name.

### 1.4 `Type` can represent anything

`Type` is not limited to contracts you authored. It can represent:

* **Primitives** — `Type.Of("int")`, `Type.Of("string")`, `Type.Of("object")` (the `Type` for `string` has `Kind() == "Class"`? No, primitives have no `TypeInfo` in the module, so queries return empty/`false`, but the `Type` value itself is still a valid handle you can pass to `Reflect` and `TypeBinding`).
* **Structs** — `Type.Of("Point")` where `Point` is `struct Point { x: int; y: int; }`
* **Interfaces** — `Type.Of("IUpdateable")`
* **Enums** — `Type.Of("Color")`
* **Generic instantiations** — `Type.Of("List")` for the unbound generic, or `Type.Of("Box")` where `Box<T>` is `Contract Box<T>`

`Type` is the value you pass to `Reflect` and `TypeBinding` when you want to talk about a type as data, exactly like `typeof(Player)` in C#.

## 2. `Reflect` with `Type`

`Reflect` (`ObjektRT.Stdlib.System.Reflect` — `[ClassBinding("Reflect")]`) is the in-language reflection module. Historically it was stringly-typed: `Reflect.IsClass("Player")`, `Reflect.Methods("Player")`, etc. It now has `Type`-based overloads that take `Type` handles (as `object`) and unwrap them to wire names via `IReflectHost.GetTypeName`.

All string overloads are retained for backward compat; the `Type` overloads are preferred.

### 2.1 New `Type`-based surface (via `object` handles)

C# `Reflect` now has, for every `string typeName` method, a `object typeObj` overload that accepts a `Type` handle:

* `HasType(Type)` / `HasType(object)` – `bool` (was `HasType(string)`)
* `Methods(Type)`, `Fields(Type)`, `BaseType(Type) -> Type?`, `Kind(Type)`, `IsClass(Type)`, `IsStruct(Type)`, `IsEnum(Type)`, `IsAbstract(Type)`, `IsSealed(Type)`, `Access(Type)`, `Interfaces(Type) -> Type[]`, `AllInterfaces(Type) -> Type[]`, `Hierarchy(Type) -> Type[]`, `IsSubclassOf(Type, Type)`, `IsAssignableFrom(Type, Type)`, `DeclaredMethods(Type)`, `DeclaredFields(Type)`, `Attributes(Type)`, `MethodAttributes(Type,string)`, `MethodReturn(Type,string)`, `MethodParams(Type,method)`, `MethodStatic(Type,method)`, `MethodVirtual`, `MethodOverride`, `MethodAbstract`, `MethodDeclaringType(Type,method) -> Type?`, `MethodBase(Type,method) -> Type?`, `FieldType(Type,field)`, `FieldStatic(Type,field)`, `FieldDeclaringType(Type,field) -> Type?`, `Invoke(Type,method,recv,args)`, `GetStatic(Type,field)`, `SetStatic(Type,field,val)`, `Call(Type,method,args)`, `TypesAsObjects() -> Type[]` (new: all types as `Type` handles).

In Contract you call them with `Type`:

```ct
var playerType = Type.Of("Player");
Reflect.IsClass(playerType);          // bool, Type overload
Reflect.Methods(playerType);          // string[] via Type
Reflect.Hierarchy(playerType);        // Type[] via Type (new)
Reflect.IsSubclassOf(dogType, animalType); // Type, Type
Reflect.Invoke(playerType, "Describe", playerHandle, []);
```

The old `string` forms still work: `Reflect.IsClass("Player")`, `Reflect.HasType("Player")`, etc.

### 2.2 Host interface `IReflectHost`

`IReflectHost` (`ObjektRT.Core.Hosting.IReflectHost`) – the interface `RuntimeReflectHost` implements – gained the `Type` surface:

* `TypesAsObjects(): object[]` – all types as `Type` handles (each handle is an allocated `ObjektRT.Core.Type` with `FullName` set)
* `HasType(object)`, `GetTypeName(object?)`, `Methods(object)`, `Fields(object)`, `BaseType(object)->object?`, `Kind(object)`, `IsClass(object)`, etc., plus `InterfacesAsObjects`, `AllInterfacesAsObjects`, `HierarchyAsObjects`, `IsSubclassOf(object,object)`, etc.

`GetTypeName` unwraps a `Type` handle by reading `ObjektRT.Core.Type.FullName` via `Runtime.GetField`; if the argument is already a `string` it is returned directly, so both `string` and `Type` call sites work.

## 3. `is` with pattern variable (`if (x is Type y)`)

`is` is a keyword (`TokenType.Is`). `x is Type` was already a boolean type check (``x is Dog`` → `bool` via `isinst`/`CastOrNull`). It now supports an optional pattern variable, like C# `is string x`:

```ct
var obj: object = GetSomething();

if (obj is Dog d) {
    // d: Dog is in scope here, typed as Dog, holding the cast value (or null if the check failed)
    IO.Println(d.speak()); // d is Dog, not object
}

if (b is string s) {
    IO.Println(s); // s: string
}

var t: object = dogType;
if (t is Type tt) {
    IO.Println(tt.FullName); // tt: Type
}
```

* **Syntax:** `expr is TypeName [Identifier]` where `TypeName` is a (possibly dotted) type name (currently `Identifier` + `'.' Identifier` chains; array/function types like `object[]` via `is object[] arr` are not yet supported as `TypeName` for the pattern – use `is string`/`is Dog`/`is Type`). The `Identifier` after `TypeName` is the new variable name (`s`, `d`, `tt`).

* **Semantics:** `expr is TypeName varName` is `bool` *and* binds `varName` as `TypeName`. Codegen: `isinst TypeName` (or `TypeHelper.CastOrNull` for primitives `string`/`int`/`bool`/`object` which have no `TypeInfo` record) → `Dup`/`Stloc varName` → `Ldnull`/`Cne` → `bool`. `varName` holds the cast value (`object` handle when the check succeeds, `null` otherwise) and is typed as `TypeName`.

* **Scoping:** The pattern variable is declared in the current scope (like `var`/`let`) and is visible after the `is` expression. For `if (x is Dog d) { use d }` the `d` is visible in the `then` branch and, conservatively, also after the `if` (as `null` when the check failed). A future refinement will limit it to the `then` branch only.

* **Primitives:** `is` for primitives (`string`, `int`/`int32`, `long`/`int64`, `bool`, `double`/`float64`, `float`/`float32`, `object`, `byte`/`sbyte`/`short`/`ushort`/`uint`) uses `TypeHelper.CastOrNull(object,string)` (C# `System.Type` checks) instead of `isinst`, because primitives have no `TypeInfo` record.

* **`&&` chaining:** `if (x is string s && s.Length > 0)` works – the `is` pattern is parsed at `ParseComparison` level, `&&` at `ParseAnd`, so `s` is in scope for the right-hand side and the `then` branch.

## 4. TypeBinding – bind new behavior to already-compiled IL types

`extend` is compile-time: it adds methods to a type when you control the source (`extend Player { fn Heal() { ... } }` folds into the type's class at build time). `TypeBinding` is the **runtime** counterpart: it lets you attach new behavior to **any** type that exists in the loaded IL module, including types you did not author and that were already compiled to `.orbt`, without recompiling their IL.

It is a `Type`-based registry, not stringly-typed.

### 4.1 Contract side (`ObjektRT.Core.Bindings.TypeBinding`)

`src/ObjektRT.Core/Bindings/TypeBinding.ct` – `namespace ObjektRT.Core.Bindings; import __builtin.std; import ObjektRT.Core;`

```ct
import ObjektRT.Core.Bindings;

var playerType = Type.Of("Player");

// Bind a new method "Describe" to the IL type Player
TypeBinding.Bind(playerType, "Describe", "Player is a good boy");

// Or via the ILBinder helpers that use Type/Reflect to synthesize a handler:
ILBinder.GiveDescribe(playerType);   // Describe -> Name() + "{}" via Type.Fields()
ILBinder.GiveToJson(playerType);     // ToJson -> "{}"
ILBinder.GiveClone(playerType);      // Clone -> new Player via Reflect
```

Full surface (all `Type`-based):

* `Bind(target: Type, method: string, handler: object)` – `handler` is a `Delegate` `(object, object[]) -> object` stored as `object` handle. Replaces any existing binding for the same `FullName#method` key (`Dict<string,object>` where key is `target.FullName + "#" + method`).
* `Unbind(target: Type, method: string) -> bool`
* `HasBinding(target: Type, method: string) -> bool` – checks both the Contract `Dict` and the C# `CSharpTypeBinding` registry.
* `HasMethod(target: Type, method: string) -> bool` – `HasBinding` or real method via `target.Methods()`/`DeclaredMethods()` (Type-based `Reflect`).
* `TryInvoke(target: Type, instance: object, method: string, args: object[]) -> object` – `null` when no binding, otherwise `InvokeHandler(handler, instance, args)` (casts `handler` to `(object,object[])->object`, `()->object`, or `(object)->object`).
* `Invoke(target: Type, instance: object, method: string, args: object[]) -> object` – `TryInvoke` → C# `CSharpTypeBinding.TryInvoke` (which returns `object?[true,result]`/`[false,null]`) → `Reflect.Invoke(target, method, instance, args)` fallback to the type's real IL method.
* `BoundMethods(target: Type) -> string[]` (currently stubbed to `[]` – full `is object[]` enumeration needs `is` array-type support).
* `Contract ILBinder` – `GiveDescribe(Type)`, `GiveToJson(Type)`, `GiveClone(Type)` examples.

`BoundInstance` – ergonomic wrapper:

```ct
let bound = new BoundInstance(playerInstance, playerType);
bound.Has("Describe");                    // HasMethod
bound.Invoke("Describe", new object[0]);  // TypeBinding.Invoke
bound.Bind("Heal", myHandler);
```

### 4.2 C# side (`ObjektRT.Stdlib.Bindings.CSharpTypeBinding`)

`libs/ObjektRT.Stdlib/Bindings/CSharpTypeBinding.cs` – `[ClassBinding("CSharpTypeBinding")]` counterpart that stores C# `Func<object?,object?[],object?>` handlers in a `ConcurrentDictionary<string, Func<...>>` keyed the same way (`FullName#Method`). Contract `TypeBinding`'s `TryInvoke`/`HasBinding` delegate to it, so either side can provide a handler and the other sees it.

```csharp
// C# – bind arbitrary C# logic to an IL type at runtime
var playerType = Type.Of("Player"); // Type handle
CSharpTypeBinding.Bind(playerType, "Describe", (self, args) => {
    var name = Reflect.GetField(playerType, "name") as string; // or via Runtime
    return $"Player({name})";
});
CSharpTypeBinding.HasBinding(playerType, "Describe"); // true
CSharpTypeBinding.Invoke(playerInstance, playerType, "Describe", Array.Empty<object>());
CSharpTypeBinding.BoundMethods(playerType); // string[]
CSharpTypeBinding.BoundTypes();             // string[] of FullNames with bindings
```

C# handlers can capture closures, DI services, `DateTime.Now`, etc., that have no Contract representation. Contract `TypeBinding` handlers are `Delegate`s created from `fn(self: object, args: object[]) -> object` literals.

The two registries are checked in order `Contract TypeBinding.Dict` → `CSharpTypeBinding` → `Reflect.Invoke`, so a C#-authored binding can satisfy a `TypeBinding.Invoke` call from Contract and vice versa.

## 5. C# bound struct `DateTime` – `new DateTime` in Contract with C# contents

`libs/ObjektRT.Stdlib/System/DateTime.cs` – `[ClassBinding("DateTimeNative")] public static class DateTimeNative` wraps `System.DateTime` as opaque handles:

* `Create(year,month,day) -> object`, `CreateFull(y,m,d,h,mi,s) -> object`, `Now() -> object`, `UtcNow() -> object`, `Parse(text) -> object`
* `GetYear(object) -> int`, `GetMonth`, `GetDay`, `GetHour`, `GetMinute`, `GetSecond`, `GetDayOfWeek`, `GetDayOfYear`, `GetTicks`, `GetKind`, `ToShortDate`, `ToLongDate`, `ToIso`, `ToStringCustom`, `AddDays`, `AddMonths`, `AddYears`, `TotalDays`, `Compare`, `IsLeapYear`, `DaysInMonth`, `DebugString`, etc. – all `static` and `object`-handle based, like `List`.

`src/ObjektRT.Core/Bindings/DateTime.ct` – `namespace ObjektRT.Core; import __builtin.std; Contract DateTime { handle: object; ... }` is the Contract *wrapper* that makes `new DateTime` feel like a struct:

```ct
import ObjektRT.Core; // DateTime lives in ObjektRT.Core

var dt = new DateTime(2024, 1, 15);   // -> DateTimeNative.Create
var dt2 = new DateTime();              // -> DateTimeNative.Now
var dt3 = DateTime.Now();              // static Now() -> DateTime
IO.Println(dt.Year());                 // 2024 via DateTimeNative.GetYear(handle)
IO.Println(dt.ToShortDate());          // via DateTimeNative.ToShortDate
IO.Println(dt.AddDays(10).ToShortDate());
var h = dt.Handle();                   // raw System.DateTime handle for TypeBinding
var dt4 = DateTime.FromHandle(h);

var dtType = Type.Of("DateTime");
Reflect.IsClass(dtType);               // Type-based Reflect works on the bound struct too
```

`new DateTime(...)` lowers to `DateTimeNative.Create(...)` which returns a `System.DateTime` boxed as `object` handle; the `DateTime` contract instance holds that handle in its `handle` field and all instance methods (`Year()`, `ToShortDate()`, `AddDays()`, …) delegate to `DateTimeNative.GetYear(handle)` etc. The C# side never sees the `DateTime` contract layout, the Contract side never sees `System.DateTime` layout – the handle is the bridge, exactly like `List`/`Dict` are `List<object>`/`Dictionary<object,object>` handles.

Add a new C# struct binding the same way: create `libs/ObjektRT.Stdlib/System/MyStruct.cs` with `[ClassBinding("MyStruct")] public static class MyStruct { public static object Create(... ) => new MyStruct(...); public static int GetFoo(object h) => ((MyStruct)h).Foo; … }` and a `src/ObjektRT.Core/Bindings/MyStruct.ct` wrapper that mirrors `DateTime.ct`.
