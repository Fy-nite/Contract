# Contract — Shadowing Reference

IL modules and contracts can *shadow* a C# host binding. The IL wins, the host is the fallback, and the union is visible everywhere — compiler, LSP, and runtime.

```
C# [ClassBinding("IO")]  ──host──┐
                                 ├──►  union (IL wins)  ──►  caller sees both
IL  Contract IO (shadow) ──IL────┘
```

---

## 1. What it does

* An IL contract whose name matches a host wire name becomes a **hybrid module**. Calls to members that exist in IL dispatch to IL; calls to members that exist only on the host dispatch to the host.
* Works for static methods, instance methods, fields, and properties. Everything on the host is exposed automatically.
* LSP completion / hover / go-to-definition / signature help sees the merged set. You can call a host-only method without ever declaring it in IL and still get diagnostics, completion, and hover.

---

## 2. Declaring a shadow

### 2.1 Explicit — recommended

```ct
import __builtin.std;

<ShadowBinding("IO")>
Contract IO {
    // new IL-only surface — never exists on the C# side
    static fn Extra(msg: string) {
        IO.Println("extra:" + msg);   // falls back to host
    }
}

Contract Program {
    static fn Main() {
        IO.Extra("hello");            // IL
        IO.Println("host still");     // host fallback
    }
}
```

* `<ShadowBinding("Target")>` is a **compile-time attribute** — it is consumed by the analyzer and never emitted to IR (like `<NativeBinding>`/`<ClrImport>`).
* `Target` is the host wire name (`"IO"`, `"Greeter"`, `"ObjektRT.Stdlib.System.IO"` all work; the short name is taken as the wire).
* The contract may have fields, constructors, and bodies — it is a normal contract, not a facade.

### 2.2 Automatic

If a contract (including one synthesized from an imported `.orbt`/`.oil`) has the same short name as a host binding, it is auto-shadowed. Explicit is preferred when the IL name must differ from the host name.

```ct
import __builtin.std;
// no attribute — name "IO" matches host "IO" → auto-shadow
Contract IO {
    static fn Extra2(msg: string) { IO.Println("extra2:" + msg); }
}
```

Auto-shadow is recorded in `SymbolTable.BuildShadowMap()` (`Contract.Compiler/StandardLibrary/SymbolTable.cs:540`) after all contracts are registered. An explicit `<ShadowBinding>` overrides it.

---

## 3. Host surface that is exposed

### 3.1 Methods

Host discovery is `SymbolTable.RegisterExternalType` (`SymbolTable.cs:93`) scanning `BindingFlags.Public | Static | Instance | DeclaredOnly` and ignoring `IsSpecialName`. Both static and instance methods are registered. Instance host methods require a receiver:

```ct
var b = new HostBox();   // HostBox is <ShadowBinding("HostBox")>
b.SetValue(42);           // instance host method — receiver is passed as object
IO.Println(b.GetValue());
```

Static host methods need no receiver:

```ct
HostBox.Print("direct");   // static host
HostBox.Extra("hi");       // IL shadow
```

### 3.2 Fields and properties — everything

Every public field and every property with a getter/setter is exposed. Opt-in rename via `[FieldBinding]` is optional.

```csharp
[ClassBinding("HostBox")]
public class HostBox {
    public int value;
    public string label = "hostLabel";
    public string Name { get; set; } = "propName";

    [FieldBinding("renamed")]
    public int internalValue;
}
```

```ct
var b = new HostBox();
IO.Println(b.value);     // field — host fallback: call HostBox.value(object) -> object
IO.Println(b.label);
IO.Println(b.Name);      // property — same dispatch: call HostBox.Name(object)
b.value = 99;            // write — call HostBox.value(object, object)
```

Registration is in `SymbolTable.cs:112` (`GetFields` + `GetProperties`). LSP completion offers fields alongside methods (`SymbolIndex.cs:1101` `GetExternalFields`). IR field reads on a shadowed IL type that lacks the field emit `call Wire.field(object)` (`IRCodeGenerator.cs:2790`); writes emit `call Wire.field(object, object)` (`:2185`). The runtime field branch (`ObjectRT.Runtime/ClrNativeResolver.cs:177`) handles both.

### 3.3 Generic hosts

No extra wiring for non-generic. For generic CLR hosts via `<ClrImport>`, the tick is synthesized automatically.

### 3.4 Auto-forwarding bodyless methods — and the `host` keyword

Two ways to route calls to the host binding directly from a shadow contract.

**Auto-forward (declaration only).** A shadow method with an empty body has no IL implementation — it is emitted as a *native stub* (a single `ret`) and every call to it falls through to host dispatch with the same name and the already-pushed arguments:

```ct
<ShadowBinding("HostBox")>
Contract HostBox {
    static fn Print(msg: string) { }     // forwards to host HostBox.Print
    fn GetValue() -> int { }             // forwards to host HostBox.GetValue
}

Contract Program {
    static fn Main() {
        HostBox.Print("auto");           // host, with no IL body involved
    }
}
```

Because the stub body is ≤2 bytes, the VM treats it like a `@DllImport` placeholder: `call IO.Method` resolves to the stub, sees the tiny body, and reroutes to the native resolver chain. This is what makes the auto-forward (and any ordinary `HostBox.Print(...)` call inside IL) reach the C# implementation without recursion — a stub, not a `call` back into itself.

**Explicit — `host.Method(args)`.** Inside a shadow contract method, `host.` forces host resolution even when the IL contract declares its own implementation of the same method:

```ct
<ShadowBinding("HostBox")>
Contract HostBox {
    fn GetValue() -> int { return 7; }
    fn Bump() -> int {
        host.GetValue();                 // host HostBox.GetValue (bypasses IL impl)
        return 42;
    }
}
```

* `host` is a keyword (`TokenType.Host`, `Lexer.cs`); `host.Method` parses as a `HostCallExpression` head whose `(args)` become a normal `CallExpression` (`ExpressionParser.cs` `ParsePrimary`).
* The analyzer resolves it through the **host-only** `SymbolTable.TryResolveHostMethod` (host `ExternalMethod`), not the IL-wins union (`SemanticAnalyzer.cs:3682` `ResolveCall`).
* `host` outside a `<ShadowBinding(...)>` contract — `error: 'host' can only be used inside a contract marked <ShadowBinding(...)>`.
* Unknown method — `error: Host method 'NoSuchMethod' not found on shadow target 'HostBox'`.
* A bodyless method's host presence is **not** validated at compile time; if the host lacks it the stub simply fails to resolve at runtime.

The two mechanisms compose: the explicit `host.X()` call emits `call Wire.X(...)` with the host's arg count/types, which resolves to the stub when the IL contract declares `X` bodyless, and to the host directly when it does not.

---

## 4. Generic `ClrImport` — tick synthesis

`System.Collections.Generic.List` without `` `1 `` is enough when the facade is generic. The analyzer synthesizes the tick from the contract's type parameters.

```ct
import __builtin.std;

<ClrImport("System.Collections.Generic.List")>
Contract MyList<T> {
    items: int;                  // presence of an instance field marks instance facade
    fn Add(item: T) { }          // empty body — facade
    fn get_Count() -> int { }
    fn get_Item(index: int) -> T { }
}

Contract Program {
    static fn Main() {
        var l = new MyList<int>();
        l.Add(42);
        IO.Println(l.get_Count());
        IO.Println(l.get_Item(0));
    }
}
```

Resolution (`SemanticAnalyzer.cs:932` `ValidateClrImportContract`):

1. If contract is generic and `ClrImport` string lacks `` ` `` and lacks `<`, append `` `N`` where `N = TypeParameters.Count`.
2. If string contains `<` (e.g. `GenericAttr<int>`), parse type args and synthesize `` `arity``.
3. Probe `Type.GetType(name)` then `name + ", System.Runtime"` / `", System.Private.CoreLib"` / `", mscorlib"` / `", System.Collections"` / `", netstandard"` and scan `AppDomain.CurrentDomain.GetAssemblies()` (`ResolveClrTypeWithFallback:1122`, `TryResolveTickFallback`).
4. Same fallback in `ContractRuntime.RegisterClrImports` (`ContractRuntime.cs:252` `ResolveClrWithFallback`) so `run` and `bundle` agree.

You may still write the tick explicitly (`System.Collections.Generic.List`1``) or an assembly-qualified name.

---

## 5. Attributes — any arity, generic or not

C# attributes of any arity are usable as Contract attributes when their assembly is loaded via `--bind`.

```ct
// C#:
// [AttributeUsage(AttributeTargets.Class)]
// public class MyAttrAttribute : Attribute {
//     public MyAttrAttribute() {}
//     public MyAttrAttribute(string name) {}
//     public MyAttrAttribute(string name, int value) {}
// }
<MyAttr>
<MyAttr("hello")>
<MyAttr("hi", 42)>
Contract Foo { }
```

The host scanner (`SymbolTable.cs:132` `_externalAttributeCtors`) records every `Attribute`-subclass constructor under its short name (without `Attribute` suffix), its full CLR name, and its wire name. `SemanticAnalyzer.ValidateAttributes:756` checks:

* Strip `<` type args: `GenericAttr<int>` → `GenericAttr`.
* Probe `GetExternalAttributeCtors(name)`.
* Accept any constructor where `Parameters.Length == Arguments.Count + NamedArguments.Count`.

Generic C# attributes (`GenericAttr<T>`) are recorded the same way; the parser does not yet support `<GenericAttr<int>(123)>` syntax — use non-generic C# attributes or Contract-defined generic attributes (`Contract MyAttr<T> : Attribute`).

---

## 6. Compile-time attribute model

`<ShadowBinding>`, `<NativeBinding>`, `<ClrImport>`, `<DllImport>` are **compile-time**: validated in `SemanticAnalyzer.ValidateNativeImports:831`, stored on `ContractDeclaration` (`Ast.cs:152` `IsShadowed`/`ShadowTarget`, `NativeBindingName`, `ClrImportType`), and erased before IR emission (`IRCodeGenerator.cs:241` skips `ShadowBinding`). All other `<Attr>` are runtime-emitted (`classBuilder.Attribute`).

---

## 7. Compiler, LSP, and runtime flow

```
RegisterAssembly  ──►  _externalBindings / _externalFields / _externalAttributeCtors
ProgramLoader     ──►  ExternalModules + synthesized Contracts (IsExternal)
SemanticAnalyzer  ──►  ValidateNativeImports sets IsShadowed + BuildShadowMap
                      TryGetMethod / TryResolveMethod union (IL first)
IRCodeGenerator   ──►  _shadowBindings wall; call sites:
                      Symbol is FunctionDeclaration → call/callvirt to IL wire (ResolveTypeName)
                      Symbol is ExternalMethod      → call to host wire (em.ClassName)
                      bodyless shadow method        → native stub (single ret) → host dispatch
                      HostCallExpression            → host-only resolution (call.Symbol = host ExternalMethod)
                      field missing in IL → call Wire.field(object) fallback
LanguageServer    ──►  SymbolIndex.AddMembers / AddModuleMembers / ResolveMember merge
                      (IsShadowedModule + GetShadowWireName + GetExternalFields)
Runtime           ──►  PrepareModule RegisterClrImports with tick fallback
                      call dispatch: VM CompiledModule first, then ClrNativeResolver/Host
```

Key files:

| Area | File | Symbol |
|------|------|--------|
| Host scan | `StandardLibrary/SymbolTable.cs:93` | `RegisterExternalType` |
| Shadow map | `StandardLibrary/SymbolTable.cs:540` | `BuildShadowMap` |
| Union lookup | `StandardLibrary/SymbolTable.cs:222` | `TryGetMethod` / `TryResolveMethod` |
| Shadow flag | `Compiler.Abstractions/AST/Ast.cs:152` | `IsShadowed` / `ShadowTarget` |
| IL synthesis | `Compiler/CompiledReferenceLoader.cs:83` | `@ShadowBinding` |
| Compile-time validation | `Semantics/SemanticAnalyzer.cs:831` | `ValidateNativeImports` |
| Generic tick | `Semantics/SemanticAnalyzer.cs:932` | `ValidateClrImportContract` + `ResolveClrTypeWithFallback` |
| Attribute any-arity | `Semantics/SemanticAnalyzer.cs:756` | `ValidateAttributes` |
| Codegen dispatch | `CodeGen/IRCodeGenerator.cs:67` | `_shadowBindings` + field `call` fallback + native stub forward |
| `host` resolution | `StandardLibrary/SymbolTable.cs:312` | `TryResolveHostMethod` (host-only) |
| LSP | `LanguageServer/Lsp/SymbolIndex.cs:1065` | `AddMembers` / `ResolveMember` |
| Runtime | `Runtime/ContractRuntime.cs:252` | `RegisterClrImports` |

---

## 8. Known limits

* **Instance state on `ClassBinding` hosts** — a shadowed `new HostBox()` allocates an IL object. Host instance methods/fields that expect a CLR host instance will not work on that IL object. Use `<ClrImport>` instance facades (which allocate CLR objects via `..ctor`) or keep shadowed instance state in IL fields.
* **Generic `ClrImport` construction of closed types** — `new MyList<int>()` validates but at runtime the open generic `List`1` must be closed with `MakeGenericType`. Current `ContractRuntime` closes via tick fallback but does not carry type arguments; prefer concrete non-generic facades or host `ClassBinding` wrappers for generics that need instance construction.
* **`FieldBinding` rename is opt-in only** — there is no `[ClassBinding]`-level exclusion; every public field/property is exposed.
* **Parser generic attribute syntax** — `<GenericAttr<int>(...)>` is not yet parsed; use non-generic C# attributes with varied constructors instead.
* **Instance auto-forward on `ClassBinding` hosts** — a bodyless instance method still forwards through host dispatch, which expects a CLR host instance as receiver; on a shadowed-IL-allocated object it fails just like any instance host call (see first limit).

---

## 9. Examples

### Static stdlib augmentation

```ct
import __builtin.std;

<ShadowBinding("IO")>
Contract IO {
    static fn LogExtra(msg: string) { IO.Println("[extra] " + msg); }
}
```

### Host assembly augmentation

```bash
dotnet run --project Contract.Cli -- -c app.ct -o app.oil -f oil --bind HostLib.dll
dotnet run --project Contract.Cli -- run app.oil --bind HostLib.dll
```

### C# attribute any-arity

```ct
import __builtin.std;

<MyAttr>
<MyAttr("hello")>
<MyAttr("hi", 42)>
Contract Service { }
```

### Auto-forward + host keyword

```ct
import __builtin.std;

<ShadowBinding("IO")>
Contract IO {
    static fn Println(msg: string) { }         // bodyless → native stub → host IO.Println
    static fn PrintTwice(msg: string) -> int {
        host.Println("once:" + msg);            // host IO.Println directly
        IO.Println("twice:" + msg);             // resolves to the stub → host IO.Println
        return 0;
    }
}
