# Contract Language — Attributes Reference

A complete reference for every attribute in the Contract language: built-in
compiler-recognized attributes, user-defined custom attributes, host-side C#
binding attributes, and synthetic compiler-emitted annotations.

---

## Built-in Compiler Attributes

These are recognized by the compiler without needing a user-defined type. They
are applied with the standard `<Name(args)>` syntax.

### `<NativeBinding("ModuleName")>`

Links a pure-facade contract to a registered host binding module. When a
method on the contract is called, the runtime dispatches to
`Module.Method(...)` on the host side.

**Scope:** Contracts only.
**Arguments:** Exactly 1 string — the binding module name.
**Constraints:** The contract must have no fields, no constructors, and all
methods must have empty bodies (pure declarations).

```ct
<NativeBinding("FakeUi")>
Contract Window {
    fn SetTitle(title: string) { }
    fn Show() { }
}
```

The host-side C# module registers with `[ClassBinding("FakeUi")]` and
`[MethodBinding]` attributes (see Host Binding Attributes below).

### `<ClrImport("System.TypeName")>`

Links a facade contract to a CLR type's public static methods via
reflection. No host-side wrapper is needed — the runtime resolves methods
through .NET reflection at load time.

**Scope:** Contracts only.
**Arguments:** Exactly 1 string — the fully-qualified CLR type name.
**Constraints:** Same as NativeBinding (no fields, no constructors, empty
method bodies).

```ct
<ClrImport("System.Math")>
Contract ClrMath {
    static fn Abs(x: double) -> double { }
    static fn Sqrt(x: double) -> double { }
    static fn Max(a: double, b: double) -> double { }
}
```

### `<DllImport("native_library.dll")>`

Links a facade contract to a native library's P/Invoke exports. The runtime
generates a C# marshalling bridge at load time using
`System.Runtime.InteropServices.DllImport`.

**Scope:** Contracts only.
**Arguments:** Exactly 1 string — the native library filename.
**Constraints:** Same as NativeBinding. Methods ARE emitted as empty stubs
in the IR (unlike NativeBinding/ClrImport which skip emission), because the
runtime needs the signatures to build the bridge.

```ct
<DllImport("kernel32.dll")>
Contract Kernel32 {
    static fn GetTickCount() -> int { }
    static fn GetCurrentProcessId() -> int { }
}
```

### `Attribute` (built-in base type)

The base type that all custom attribute types must inherit from (directly or
transitively). A contract that inherits from `Attribute` is recognized as an
attribute type by the compiler.

**Usage:** Declare an attribute by inheriting from `Attribute`:

```ct
Contract Author : Attribute {
    constructor(name: string) { }
}

Contract Serializable : Attribute { }

// Inherited — Version is also an attribute type
Contract Version : Author {
    constructor(major: int) { }
}
```

Apply with angle brackets:

```ct
<Author("bob")>
<Serializable>
Contract Circle { ... }

<Version(2)>
fn compute() -> int { ... }
```

**Validation rules:**
- The attribute name must resolve to a known contract.
- That contract (or an ancestor) must inherit from `Attribute`.
- The argument count must match one of the attribute type's constructors.
  A parameterless attribute accepts any arity.

**Custom attributes are reflected at runtime:**

```ct
IO.Println(Reflect.Attributes("Circle")[0]);              // Author(bob)
IO.Println(Reflect.MethodAttributes("Circle", "Area")[0]); // Deprecated(...)
```

---

## Synthetic Compiler-Generated Annotations

These appear in the IR (`.oil`/`.orbt`) but are not written by the user.

### `@Generic(T, U, ...)`

Emitted on every generic contract to tell the VM which type parameters the
definition carries. The VM reads this during materialization to clone and
substitute per concrete instantiation.

```text
// Source:
Contract Box<T> { ... }

// IR output:
@Generic(T)
class Box { ... }
```

### `@Attribute`

Emitted on every type that is an attribute type (inherits from `Attribute`).
This allows the runtime to identify attribute types through reflection without
walking the inheritance chain.

```text
// Source:
Contract Author : Attribute { ... }

// IR output:
@Attribute
class Author { ... }
```

---

## Host-Side C# Binding Attributes

These are C# attributes used on the host side (in .NET assemblies) to register
native bindings that Contract code can call. They live in
`ObjektRT.Core.Attributes`.

### `[ClassBinding("ModuleName")]`

**Defined in:** `libs/ObjektRT.Core/Attributes/ClassBindingAttribute.cs`
**Scope:** C# classes
**Purpose:** Registers a C# class as a binding module. The Contract compiler's
symbol table discovers it via assembly scanning and makes its methods available
as `ModuleName.Method(...)` in Contract source.

```csharp
[ClassBinding("IO")]
public static class IO
{
    [MethodBinding]
    public static void Println(object value) { ... }

    [MethodBinding]
    public static void Print(object value) { ... }
}
```

### `[MethodBinding("wireName")]`

**Defined in:** `libs/ObjektRT.Core/Attributes/ClassBindingAttribute.cs`
**Scope:** C# methods (inside a `[ClassBinding]` class)
**Purpose:** Marks a method as callable from Contract. The optional `Name`
parameter overrides the wire name; when omitted, the C# method name is used.

```csharp
[ClassBinding("Greeter")]
public static class Greeter
{
    [MethodBinding]
    public static void SayHi() => Console.WriteLine("hi!");

    [MethodBinding("Add")]
    public static int AddValues(int a, int b) => a + b;
}
```

---

## Module Metadata Attributes (C# Infrastructure)

Abstract base classes in `ObjektRT.Core.Attributes` used for ORBT module
metadata. These are not part of the Contract language itself.

| Attribute | File | Purpose |
|-----------|------|---------|
| `ModuleNameAttribute` | `ModuleAttribute.cs` | Module name metadata |
| `ModuleVersionAttribute` | `ModuleVersionAttribute.cs` | Module version metadata |
| `ModuleDescriptionAttribute` | `ModuleDescriptionAttribute.cs` | Module description metadata |
| `ModuleCompilerAttribute` | `ModuleCompilerAttribute.cs` | Compiler info + arguments |

All are `abstract`, `internal`, and decorated with `[AttributeUsage(AllowMultiple = true)]`.

---

## Where Attributes Can Be Applied

In Contract source, `<Name(args)>` syntax is supported on:

| Declaration | Supported | Example |
|-------------|-----------|---------|
| Contracts | Yes | `<Author("bob")> Contract Circle { ... }` |
| Functions | Yes | `<Deprecated("use v2")> fn old() { ... }` |
| Constructors | Yes | `<Internal> constructor() { ... }` |
| Structs (inside contracts) | Yes | `<Serializable> struct Point { ... }` |
| Enums | Yes | `<Flags> enum Color { ... }` |
| Fields | **No** | Parser error: "Attributes on fields are not supported yet" |

---

## Attribute Validation (Compiler)

The semantic analyzer validates attributes in `ValidateInheritanceAndAttributes`:

1. **Inheritance walk:** For each contract, walks the base chain. If it reaches
   `Attribute`, marks `IsAttributeType = true` on the contract and all ancestors.

2. **Built-in check:** `NativeBinding`, `ClrImport`, `DllImport` are recognized
   as built-in. They are valid only on contracts, require exactly 1 argument,
   and skip further validation.

3. **Unknown attribute:** If the attribute name doesn't match any known contract,
   error: `"Unknown attribute 'X'"`.

4. **Not an attribute type:** If the referenced contract exists but doesn't
   inherit from `Attribute`, error: `"'X' is not an attribute type"`.

5. **Arity check:** Argument count must match one of the attribute type's
   constructors.

---

## Attribute Reflection at Runtime

The `Reflect` stdlib module exposes attributes through two functions:

```ct
Reflect.Attributes(typeName) -> string[]
// Returns all attributes on the type as "Name(arg, ...)" strings

Reflect.MethodAttributes(typeName, methodName) -> string[]
// Returns all attributes on the method
```

Example:

```ct
<Author("alice")>
Contract Greeter { ... }

static fn Main() {
    var attrs = Reflect.Attributes("Greeter");
    IO.Println(attrs[0]);  // Author(alice)
}
```

---

## Key Files

| Area | File |
|------|------|
| AST node | `Contract.Compiler.Abstractions/AST/Ast.cs` (lines 19-33) |
| Parsing | `Contract.Compiler/Parsing/Parser.Declarations.cs` (lines 11-59) |
| Type registry | `Contract.Compiler/Semantics/TypeRegistry.cs` (line 17-18) |
| Validation | `Contract.Compiler/Semantics/SemanticAnalyzer.cs` (lines 508-898) |
| IR codegen | `Contract.Compiler/CodeGen/IRCodeGenerator.cs` (lines 186-254, 424-427) |
| Wire model | `libs/ObjektRT.Core/Model/ORBTModule.cs` (line 26) |
| Host bindings | `libs/ObjektRT.Core/Attributes/ClassBindingAttribute.cs` |
| Runtime scanning | `Contract.Runtime/ContractRuntime.cs` (lines 53-215) |
| Stdlib catalog | `Contract.Compiler/StandardLibrary/StdlibCatalog.cs` (lines 22-45) |
| Reflection | `libs/ObjektRT.Stdlib/System/Reflect.cs` (lines 107-113) |
| DllImport bridge | `libs/Objekt-RT/src/ObjectRT.Runtime/DllImportResolver.cs` |
