# ContractIR — 5 big features (2026-08)

## 1. new Type(args)
- Parser: NewExpression.Arguments (list); `Size` made settable. Analyzer: validates ctor arity against `contract.Constructors` (only when ctors declared). Codegen: push receiver FIRST (this=param 0), then args, `call Type..ctor(object, argTypes...)`. Gotcha: the receiver-first convention matters (runtime pops args in reverse into locals).
- Fixed a PRE-EXISTING field-shadowing bug exposed by ctor args: `this.x = x;` compiled RHS `x` as the FIELD via IsInstanceField (contract has field x) instead of the PARAM. Fix: `IsInstanceField`/`IsStaticField` now check `!_variableTypes.ContainsKey(name)` (params/locals shadow fields).

## 2. Enums
- `enum Color { Red, Green, Blue }` — top-level or nested in contract. Members fold to zero-based index (`ldc.i4 N`) at compile time. Emitted to IR as a class of `public static field N: int32` (metadata/reflection only — values never read from slots).
- Lexer keywords `enum`/`namespace` added. Parser ParseEnum. Analyzer: IsEnumType/IsEnumMember/FindEnum (matches short OR FullName); undefined-variable check accepts enum type names; MemberExpression spine treated as type access. Codegen: FindEnumMemberIndex + LdcI4 (member + scoped forms).
- CompilerDriver must merge `program.Enums` into the full program (was only Contracts/Functions/Structs — enum references failed with "Undefined variable" until fixed).

## 3. Java-style namespaces (explicit, not path-based)
- `namespace com.example;` at top of file → applies to subsequent contracts/structs/enums (per-file, parser `_currentNamespace`). `import com.lib;` = wildcard package import (short names). Types also addressable fully-qualified `com.lib.Geo.Triple(4)`.
- AST: declarations got `Namespace` + `FullName` (ns==null ? Name : ns.Name). SymbolTable registers user contracts/structs under BOTH short + FQN keys. Analyzer: registers both as valid types; FindContract/FindEnum match short-or-FQN; IsTypeAccessChain treats dotted spines ending in user types as non-variables.
- Codegen: wire names are FQN (`class com.lib.Geo`). `ResolveTypeName(name)`: dotted passes through; current contract's namespace → namespace imports → unique short match. Applied at: declaration emission (FullName), Extends, MapNamedType fallback (covers ALL signature type refs), newobj/ctor, method refs (instance+static), field refs (member/scoped/compound), ResolveMemberObjectType, FindEnumMemberIndex, IsStaticField(contractName,...).
- VM handles dotted names transparently (type map keys = FQN; call/field refs dotted; ResolveFunction splits at LAST dot).

## 4. DLL-style compiled references: `import "lib.orbt"`
- CompilerDriver: import with extension .orbt/.oil/.oir → `LoadCompiledReference` → parse via ObjektRT.Core `OrbtFileReader.ReadFile` or `ObjectILParser.ParseModule` → `ModelToAstConverter` → ModuleNode → stored in `Program.ExternalModules`; synthetic Contract/Struct/Enum declarations (marked `IsExternal`) added for analysis.
- Wire→language type mapping REQUIRED: "int32"→"int", "float64"→"double", etc. (TypeRegistry doesn't know IR names). Array suffixes preserved.
- Enum heuristic for externals: class with no base/ctors/methods + all-static-int32 fields → EnumDeclaration (member order = field order). Cross-module enum members fold correctly.
- Codegen: `if (decl.IsExternal) continue` (don't re-emit); `BuildModule()` merges ExternalModules into the output ModuleNode (dedup by FQN) before serialization. STATIC LINKING — output is one module, runs standalone. No VM changes needed.
- Test fixture: tests/success/lib_ns.orbt is a committed build artifact (only *.oir is gitignored). TestRunner now uses CompilerDriver (was single-file parse+analyze) so file imports resolve — 46/46.

## 5. In-language reflection (Reflect module)
- `Contract.Compiler/StandardLibrary/IReflectHost.cs` (interface) + `Builtins/ReflectModule.cs` `[ClassBinding("Reflect")]` — MUST live in Contract.Compiler so `CompileSource`'s `RegisterAssembly(typeof(IO).Assembly)` registers it for analysis (Contract.Runtime can't be referenced by Compiler — circular).
- `ContractRuntime : IReflectHost` sets `ReflectModule.Host = this` in ctor + registers the binding. Host uses `Runtime.GetReflector()` + new `Runtime.GetStaticField/SetStaticField` (needs `CompiledModule.FieldMap` — added, populated in ModuleCompiler.Compile from `_fieldMap`).
- Name collision gotcha: ContractRuntime has a static METHOD `ReflectModule(ORBTModule)` — fully-qualify `Contract.Compiler.StandardLibrary.Builtins.ReflectModule.Host` inside the class. Also `TypeInfo` ambiguous with System.Reflection — use `ObjectRT.Runtime.Reflection.TypeInfo`.
- Reflect.Call takes (type, method, args[]) — ALWAYS 3 args. Calling with 2 compiles the 3-param signature (external-method codegen emits DECLARED params, not actual args) → StackUnderflow at runtime. No external-method arity check exists yet.
- RE-ENTRANCY BUG (fixed): calling `CallMethod` from inside a running VM function (host binding like Reflect.Call) hit `CallMethodViaVm` which did `_executor.Reset()` — wiping the OUTER frames (heap/statics preserved but stack/frames cleared) → corruption/crash. Fix: if `_executor is Interpreter active && active.IsExecuting` → create `new Interpreter(_compiled, active.State)` (shares heap/statics), run, discard. Added `Interpreter.IsExecuting`.
- VM `Not` opcode BUG (fixed, pre-existing): was bitwise `~` (so `!(a<b)` → `~0` = -1 — printed as -1, and truthiness always true). Now logical: `v == 0 ? 1 : 0`. Compiler lowers `>=`/`<=`/`>`/`<` via clt + not etc.

## Tests
- New success tests: CtorArgs, Enums, JavaNamespaces, ImportNamespaces, OrbtInclude, Reflection, ArrayArg, lib_ns.ct(+lib_ns.orbt). Suite 46/46; runtime reflection 54/54; core 19/19. LSP builds clean (0 errors).
- docs/CONTRACT_LANGUAGE.md updated (ctor args, enums, namespaces, DLL refs, Reflect module, keywords, grammar).
