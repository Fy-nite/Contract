# Contract Language Reference

Contract is a statically-typed programming language. It features a clean syntax with contracts, functions, structs, and functional programming constructs.

## File Structure

Source files use the `.ct` extension and are UTF-8 encoded.

## Basic Syntax

### Hello World

```ct
Contract Program {
    static fn Main() {
        IO.Println("hello world");
    }
}
```

### Comments

```ct
// Single-line comments
```

## Keywords

| Keyword | Description |
|---------|-------------|
| `Contract` | Declares a contract (namespace/class) |
| `fn` | Declares a function |
| `var` | Mutable variable declaration |
| `let` | Immutable variable declaration |
| `if` / `else` | Conditional statements |
| `while` | Loop statement |
| `for` | C-style for loop |
| `break` | Exit the innermost loop |
| `continue` | Skip to the next loop iteration |
| `switch` / `case` | Pattern matching |
| `return` | Return from function |
| `struct` | Declares a structure |
| `new` | Creates a new instance |
| `fun` | Lambda function |
| `static` | Static member |
| `public` / `private` / `protected` / `internal` | Access modifiers |
| `import` | Import external modules |
| `null` | Null literal |
| `true` / `false` | Boolean literals |
| `constructor` | Class constructor |
| `enum` | Declares an enum (named int constants) |
| `namespace` | Declares a Java-style package for the file |

`var` is an alias of `let` — both declare variables, and mutability is decided
by usage (assignment through the binding).

## Contracts

Contracts are the primary organizational unit, similar to namespaces or classes:

```ct
Contract Math {
    // Functions and structs go here
    fn add(a: int, b: int) {
        return a + b;
    }
    
    struct Point {
        x: int;
        y: int;
    }
}
```

### Inheritance

A contract can declare a single base type (C#-style). The base type must be a
contract declared in the same program, or the built-in `Attribute` type.

```ct
Contract Animal {
    name: string;
}

Contract Dog : Animal {
    breed: string;
}
```

The base type is recorded in the IR as declarative metadata
(`class Dog : Animal`), and the runtime stores it as the type's base index when
the base is declared in the same module. Virtual dispatch through base types is
not implemented yet.

## Attributes

Contract supports C#-style custom attributes. Attribute *types* are contracts
that inherit from the built-in `Attribute` base; attribute *applications* use
angle-bracket syntax before a declaration.

### Declaring an attribute type

```ct
Contract Author : Attribute {
    constructor(name: string) {
    }
}

Contract Deprecated : Attribute {
    constructor(msg: string) {
    }
}
```

- An attribute type is any contract whose base chain reaches `Attribute`
  (directly or transitively, so `Contract X : Author` is also an attribute type).
- Constructor parameters define the positional arguments the attribute accepts.
  Applications are validated against the declared arity: an attribute with a
  constructor taking one argument must be applied with exactly one argument.
  An attribute type with no constructor accepts any number of arguments.

### Applying attributes

Attributes are written as `<Name(arg1, arg2)>` on the line(s) directly before a
contract, struct, function, or constructor:

```ct
<Author("bob")>
Contract Greeter {
    greeting: string;

    <Author("alice")>
    constructor() {
        this.greeting = "hello";
    }

    <Deprecated("use the new API")>
    fn hello() -> string {
        return "hello";
    }
}

<Serializable>
struct Point {
    x: int;
    y: int;
}
```

Arguments are string, integer, float, or boolean literals (strings keep their
quotes). Attribute applications on fields are not supported yet.

### How attributes compile

Attributes are emitted as annotations on the IR type/method declarations
(`@Author("bob")` before `class Greeter`, `@Deprecated("use the new API")`
before the method) and round-trip through the ORBT binary format. Attribute
types are marked with the built-in `@Attribute` annotation. This mirrors the
runtime's existing `@DllImport` / `@NativeBinding` metadata mechanism, so
host-side reflection and bindings can consume custom attributes the same way.

### Built-in binding attributes

Three built-in attributes — no user declaration needed — turn a contract into
a **pure facade**: no fields, no constructors, and every member is an
empty-bodied `static fn` whose call sites dispatch somewhere outside the
module. They differ in where the methods land:

| Attribute | Example | Methods dispatch to |
|---|---|---|
| `<NativeBinding("Module")>` | `<NativeBinding("File")>` | a registered host binding module (`[ClassBinding]` class or stdlib module) |
| `<ClrImport("Type")>` | `<ClrImport("System.Math")>` | public static methods on a CLR type, resolved by reflection — **no host wrapper needed** |
| `<DllImport("lib.dll")>` | `<DllImport("kernel32.dll")>` | native exports of a DLL, marshalled via generated P/Invoke bridges |

#### `<ClrImport("System.Math")>` — link to CLR methods without class bindings

Any public static method on a .NET type becomes callable from Contract.
Unlike `NativeBinding`, the target type does **not** need a
`[ClassBinding]`-annotated wrapper class: the compiler emits `Type.Method`
call targets and the runtime resolves them via reflection. This works for
BCL types (`System.Math`, `System.Convert`, `System.Environment`, ...) and
for any type the host process has loaded.

```ct
<ClrImport("System.Math")>
Contract ClrMath {
    static fn Abs(x: double) -> double { }
    static fn Sqrt(x: double) -> double { }
    static fn Max(a: double, b: double) -> double { }
    static fn Pow(x: double, y: double) -> double { }
}

Contract Program {
    static fn Main() {
        IO.Println(ClrMath.Abs(-3.5));      // 3.5
        IO.Println(ClrMath.Sqrt(16.0));     // 4
        IO.Println(ClrMath.Max(2.0, 7.0));  // 7
        IO.Println(ClrMath.Pow(2.0, 10.0)); // 1024
    }
}
```

- The argument is the CLR type's full name. Use an assembly-qualified name
  (`"System.Diagnostics.Process, System.Diagnostics.Process"`) when the type
  lives outside the core library.
- When the type resolves in the compiler process (BCL or `--bind`
  assemblies), each `static fn` is checked against the real method: it must
  exist and the declared parameter count must match an overload. An
  unresolvable type compiles with a warning and is checked at runtime.
- Method signatures come from the Contract declarations (`int` → `int32`,
  `double` → `float64`, `string` → `string`, `bool` → `bool`). Overloads are
  picked by argument types, so `Math.Abs` works with both ints and doubles.
- Types whose methods are instance-only, or whose assembly isn't loaded by
  the runtime, fail at call time with a clear error.

#### `<DllImport("user32.dll")>` — P/Invoke a native library

Facade methods map to native exports of the named DLL by name. The
signatures are emitted into the module metadata and the runtime generates
the marshalling bridge on first use (strings marshal as `LPWStr`, `int32` →
`int`, `float64` → `double`, `bool` → `bool`).

```ct
<DllImport("kernel32.dll")>
Contract Kernel32 {
    static fn GetTickCount() -> int { }
    static fn GetCurrentProcessId() -> int { }
}

Contract Program {
    static fn Main() {
        IO.Println(Kernel32.GetTickCount());         // ms since boot
        IO.Println(Kernel32.GetCurrentProcessId());  // this process's id
    }
}
```

**Struct marshalling.** User-defined structs can be passed to and returned from
native functions **by value**. The compiler records each struct's field types;
at runtime the VM packs the struct into its C layout (natural alignment, nested
structs inlined) before the call and unpacks the native result back into a
struct value. Narrow integer widths are converted at the boundary, so a native
`uint16` field or return lands in a `ushort`-typed slot and a native `float`
(32-bit) field converts to/from the language's `float`/`double` cleanly.

```ct
struct Color {
    r: byte;
    g: byte;
    b: byte;
    a: byte;
}

struct Vec3 {
    x: float;
    y: float;
    z: float;
}

<DllImport("raylib.dll")>
Contract Raylib {
    static fn ColorAlpha(c: Color, alpha: float) -> Color { }
    static fn Vector3DotProduct(a: Vec3, b: Vec3) -> float { }
}

Contract Program {
    static fn Main() {
        var c = new Color(255, 0, 128, 255);
        var faded = Raylib.ColorAlpha(c, 0.5);   // struct in, struct out
        IO.Println(faded.a);
        IO.Println(Raylib.Vector3DotProduct(v1, v2));
    }
}
```

Struct-typed methods must be declared `static` and the struct types must be
visible to the facade (declared in the same file or a referenced module). The
C-layout rules are the ones the platform C compiler would use for a struct
with the same fields in the same order; for byte-exact layout guarantees use
fixed-width fields (`byte`/`short`/`ushort`/`int`/`uint`/`float`/`double`).

> **⚠️ Match the native layout exactly.** The bridge passes the struct by value
> with exactly the fields you declare — it cannot know the C side's layout. If
> the native struct has fields you omit (e.g. raylib's `Color` is
> `r, g, b, a` — four bytes — but you declare only `r, g, b`), the native
> function reads the missing bytes from uninitialized stack/register space,
> producing garbage alpha or scrambled colors. Declare **every** field the
> native struct has, in the same order, with the same widths. When in doubt,
> check the header: `typedef struct Color { unsigned char r, g, b, a; } Color;`
> means four `byte` fields, not three.

- P/Invoke bridge generation happens at runtime; Windows DLLs are the
  primary target. If an export is missing, the error surfaces at the call
  site (and the bridge generator's source is retained for debugging).
- Because the compiler can't portably inspect native exports, only the
  facade shape is checked at compile time — arity/type mismatches appear as
  runtime errors.

#### `new` on facades

None of the three facade kinds declare constructors, and `NativeBinding`
maps `new Type()` to the host module's `Create` method. For `ClrImport` and
`DllImport` there is nothing to construct — keep the facades static-only.

## Functions

### Basic Function

```ct
fn greet(name: string) {
    IO.Println("Hello, " + name);
}
```

### Function with Return Type

Functions declare their return type with `-> type` after the parameter list:

```ct
fn add(a: int, b: int) -> int {
    return a + b;
}

fn isEven(n: int) -> bool {
    return n % 2 == 0;
}

fn greeting(name: string) -> string {
    return "Hello, " + name;
}
```

If the return type is omitted, the compiler defaults to `void` for `Main` and `int` for other functions. Functions that fall off the end without a `return` implicitly return a zero value for the declared type (`0`, `0.0`, `false`, `null`).

### Static Functions

```ct
Contract Program {
    static fn Main() {
        // Entry point
    }
}
```

Static members of a contract can be called with either `.` or `::` — both
resolve to the same static function:

```ct
Contract Utils {
    static fn triple(x: int) -> int {
        return x * 3;
    }
}

static fn Main() {
    IO.Println(Utils::triple(5));   // 15 — :: on a user contract
    IO.Println(Utils.triple(5));    // 15 — dot form
}
```

### Entry Point & Command-Line Arguments

An executable program's entry point is a static `Main`. Like C#, it may take
either no parameters or a single `string[]` that receives the command-line
arguments passed to the program:

```ct
Contract Program {
    static fn Main(args: string[]) {
        IO.Println("arg count: " + Convert.ToString(args.Length));
        for (var i = 0; i < args.Length; i = i + 1) {
            IO.Println(Convert.ToString(i) + ": " + args[i]);
        }
    }
}
```

Arguments after the source file are forwarded to `Main`:

```
ccl app.ct hello world 42
# arg count: 3
# 0: hello
# 1: world
# 2: 42
```

A `Main()` with no parameters is still valid — any arguments passed are simply
ignored. The compiler rejects a `Main` that declares more than one parameter,
or a single parameter that isn't a `string[]`.

`::` is the same scoped-access operator used for stdlib modules (`IO::Println`).

### Functions with No Parameters

```ct
fn sayHello() {
    IO.Println("Hello!");
}
```

## Variables

### Mutable Variables (`var`)

```ct
var x: int = 5;
var name: string = "Alice";
var uninitialized: int; // Optional initialization
```

### Immutable Variables (`let`)

```ct
let pi: double = 3.14159;
let greeting: string = "Hello";
```

### Type Inference

If you omit the type annotation, the compiler infers it from the initializer:

```ct
let a = 5;          // a: int
let b = "hello";    // b: string
let c = true;       // c: bool
let d = 3.5;        // d: double
let p = new Person(); // p: Person
```

Inference works from literals, `new` expressions, and other variables. `null` and expressions whose type can't be determined still require an explicit annotation.

### Compile-Time Constants (contract scope)

A contract-scope `let` whose initializer is **not** a lambda declares a *comptime constant* (FEATURE_PROPOSALS §15). The initializer must fold at compile time — literals, arithmetic/comparison/logical operators over constants, string concatenation, or constants declared earlier in the same contract. `const` is the explicit spelling of the same declaration:

```ct
Contract Config {
    let APP_NAME: string = "contract";
    const TICKS_PER_HOUR: int = 60 * 60 * 1000;
    let GREETING: string = "hi, " + APP_NAME;   // constants reference constants

    static fn Main() {
        IO.Println(TICKS_PER_HOUR);   // folds to 3600000 — no runtime math
    }
}
```

Constants are immutable: assigning to one (`X = 1`, `X += 1`, `Config.X = 1`) is an error. Reads fold to their value everywhere, including bare reads inside the contract and `Config.APP_NAME` / `Config::APP_NAME` from outside. A constant whose initializer cannot be folded (calls, non-constant references) is rejected at compile time.

## Types

### Built-in Types

| Type | Description | IR Type |
|------|-------------|---------|
| `int` | 32-bit integer | `int32` |
| `int64` (or `long`) | 64-bit integer | `int64` |
| `byte` | Unsigned 8-bit integer (0-255) | `uint8` |
| `sbyte` | Signed 8-bit integer (-128-127) | `int8` |
| `short` | Signed 16-bit integer | `int16` |
| `ushort` | Unsigned 16-bit integer | `uint16` |
| `uint` | Unsigned 32-bit integer | `uint32` |
| `string` | Text string | `string` |
| `bool` | Boolean (`true`/`false`) | `bool` |
| `double` | 64-bit floating point | `float64` |
| `float` | 32-bit floating point | `float32` |
| `object` | Opaque object handle (lists, dicts, ...) | `object` |
| `void` | No value (return type only) | `void` |
| `intptr` | Pointer-sized signed integer (`int` when 32-bit, `int64` when 64-bit) | `int64` |
| `uintptr` | Pointer-sized unsigned integer | `int64` |
| `nuint` | Pointer-sized unsigned integer (alias of `uintptr`) | `int64` |

The narrow integer widths (`byte`, `sbyte`, `short`, `ushort`, `uint`) exist
primarily for native interop. At the VM level they all ride on the 32-bit
integer slot; conversion happens at the P/Invoke boundary, so they are exact
where it matters (struct fields, `@DllImport` params and returns). There are no
unsigned literals: an integer literal that overflows the signed 32-bit range is
clamped with a warning, so expect results of `uint`-typed calls to appear as
their signed 32-bit view (e.g. a native `uint` returning `4294967295` reads as
`-1`).

The pointer-sized types (`intptr`, `uintptr`, `nuint`) are used to hold
addresses returned by native interop and the managed-memory system. They are
the Contract names for pointer-sized integers: there is no dedicated
`ValueTag.Ptr` in the VM yet, so at runtime they ride on the 64-bit integer
slot (IR type `int64`). They are primarily meant for storing and passing
`Address()` values around, not for raw address arithmetic (see Pointers &
Managed Memory below).

Type names are **case-insensitive**: `Int`, `String`, and `int` all refer to the same type.

### Classes (Contracts with Instance Methods)

A contract with member functions and (optionally) instance fields acts as a
class. Instances are created with `new ContractName()` — this works whether or
not the contract declares fields — and non-static member functions are
instance methods that receive the receiver implicitly:

```ct
Contract Counter {
    count: int;                    // instance field (optional)

    constructor() {
        this.count = 0;
    }

    fn increment() {               // instance method
        this.count += 1;
    }

    fn value() -> int {
        return this.count;
    }

    static fn Main() {
        var c: Counter = new Counter();
        c.increment();
        c.increment();
        IO.Println(c.value());  // 2
    }
}
```

Inside an instance method the receiver is available as `this`, and fields can
be read/written either through `this` or directly by name (`count` vs
`this.count`). Constructors receive `this` too, so they can initialize fields
before the instance is returned. A bare call to a sibling instance method
(`increment()` inside another instance method of the same contract) implicitly
passes `this`.

> Every non-static member function is an instance method, whether or not its
> contract declares fields. Module-style use of contracts as namespaces (e.g.
> `IO`, `Math`, `List`) is expressed with `static fn` — static members are
> called on the type itself (`Util.Bump()`, `Counter::Reset()`), never on an
> instance, and an instance method cannot be invoked without an object created
> with `new`.

### Indexers

A contract may declare one `this(...)` indexer giving instances array-style
access. It is an instance member (never `static`) with at least one parameter,
an element type, and `get` / `set` accessors (`value` is the incoming value in
the setter). Either accessor may be omitted for a read-only or write-only
view:

```ct
Contract Matrix {
    cells: int[];
    rows: int;

    constructor() {
        this.cells = new int[16];
        this.rows = 4;
    }

    // multi-argument indexer, read + write
    this(row: int, col: int) -> int {
        get { return this.cells[row * this.rows + col]; }
        set { this.cells[row * this.rows + col] = value; }
    }
}

Contract Box<T> {
    item: T;
    constructor(v: T) { this.item = v; }

    // generics work: T resolves per materialized instance
    this(slot: int) -> T {
        get { return this.item; }
        set { this.item = value; }
    }
}
```

Use is just `obj[i]` (read), `obj[i] = v` (write), and compound assignment
`obj[i] += v` (a read-then-write through both accessors):

```ct
let m = new Matrix();
m[1, 2] = 7;
IO.Println(m[1, 2]);    // 7
m[1, 2] += 3;           // 10

let b: Box<int> = new Box<int>(5);
b[0] = 9;
IO.Println(b[0]);       // 9
```

The indexer compiles to synthesized instance methods (`__idx_get_N` /
`__idx_set_N`) on the contract, so it composes with C#-style access and can be
read/written through a field receiver too (`holder.points[i]`). Inside the
contract's own methods, `this[i]` dispatches to the indexer the same way.

### Static Fields

A field declared with `static` is **shared state** on the contract itself,
not per-instance. It is stored in the VM's static slots, so every instance
(and every call) sees the same value — the classic class-level counter.

```ct
Contract Widget {
    static made: int;              // static field — one slot for the whole type

    constructor() {
        made = made + 1;           // bare access works in ctors too
    }

    fn serial() -> int {
        return made;               // instance methods can read static state
    }
}

Contract Program {
    static fn Main() {
        var a = new Widget();
        var b = new Widget();
        IO.Println(Widget.made);   // 2 — qualified access from anywhere
        IO.Println(a.serial());    // 2 — shared across instances
        Widget.made = 0;           // qualified write
        Widget.made += 1;          // qualified compound
        IO.Println(Widget::made);  // 1 — the :: form works too
    }
}
```

Access rules:

- **Bare** (`made`) — resolves to the static field inside the declaring
  contract's functions, constructors, and instance methods. Instance fields
  shadow statics of the same name.
- **Qualified** (`Widget.made`) — from anywhere, via either `.` or `::`.
- Reads, plain writes (`=`), and compound assignment (`+= -= *= /= %=`) all
  work in every form.

Static fields are carried in the ORBT metadata (`static field name: type` in
IR text, a flag byte in the v0x02 binary format), so hosts can reflect over
them with `FieldInfo.IsStatic`. Structs do not support static fields yet.

### Generic Collections

`List<T>` and `Dict<K, V>` can be written with generic syntax. They are
**type-erased**: at runtime the value is an `object` handle, and the element
types exist only for compile-time checking and signatures.

```ct
var nums: List<int> = List.Create();
List.Add(nums, 10);
List.Add(nums, 20);
IO.Println(List.Count(nums));   // 2
IO.Println(List.Get(nums, 1));  // 20

var scores: Dict<string, int> = Dict.Create();
Dict.Set(scores, "ada", 95);
Dict.Set(scores, "grace", 100);
IO.Println(Dict.Get(scores, "ada"));            // 95
IO.Println(Dict.ContainsKey(scores, "grace"));  // true
```

`List<T>` and `Dict<K, V>` are the erased native bindings. `[i]` array-style
indexing compiles to a *contract indexer* (see "Indexers" below) only when the
receiver is a user contract that declares one; the raw natives expose
`List.Get` / `List.Set` directly.

Generic types are valid in signatures, so helpers can take and return typed
collections:

```ct
fn sum(xs: List<int>) -> int {
    var total: int = 0;
    for (var i: int = 0; i < List.Count(xs); i = i + 1) {
        total += List.Get(xs, i);
    }
    return total;
}
```

### Structs

Structs are value-like records. They can be declared at the top level or inside
a contract:

```ct
Contract Data {
    struct Point {
        x: int;
        y: int;
    }
    
    struct Rectangle {
        topLeft: Point;
        bottomRight: Point;
    }
}
```

Structs support **positional construction** — the arguments are assigned to
fields in declaration order, so `new Point(3, 4)` sets `x = 3`, `y = 4` without
declaring a constructor. A warning is raised if the argument count does not
match the field count. Nested structs construct recursively:

```ct
struct Color {
    r: byte;
    g: byte;
    b: byte;
    a: byte;
}

var c = new Color(255, 0, 128, 255);
var r = new Rect(new Vec3(0.5, 0.25, 0.0), 100.0, 200.0);  // nested struct arg
IO.Println(r.origin.x);                                     // nested field read
```

Structs marshal **by value** across `@DllImport` boundaries (see below), with C
natural alignment and the field order you declared.

### Creating Instances

```ct
var p: Point = new Point();
var c: Counter = new Counter();  // runs the constructor
```

Constructors take arguments with `new Type(args)`. Arguments are evaluated
left-to-right and passed positionally to the matching constructor:

```ct
Contract Point {
    x: int;
    y: int;

    constructor(x: int, y: int) {
        this.x = x;
        this.y = y;
    }
}

var p = new Point(3, 4);   // x = 3, y = 4
```

The analyzer validates that a constructor with the matching arity exists.

## Enums

An `enum` declares named integer constants that fold to their zero-based
index at compile time. Declare them at the top level or inside a contract:

```ct
enum Color {
    Red, Green, Blue
}

Contract Program {
    static fn Main() {
        IO.Println(Color.Red);      // 0
        IO.Println(Color.Green);    // 1
        IO.Println(Color::Blue);    // 2 — scoped form works too
    }
}
```

- Members are accessed as `Color.Red` or `Color::Red`.
- Every member read compiles to `ldc.i4 <index>` — the enum itself is emitted
  to the IR metadata (as a class of static int fields) so hosts can reflect
  over it.
- Enums are valid types, so they can appear in field/signature positions, and
  `switch` over them works like `switch` over `int`.
- Top-level and nested (inside a contract) enums are both supported.

## Namespaces

Java-style packages, declared explicitly with `namespace` at the top of a file
— **not** derived from the file name or path. Everything declared below a
`namespace` statement lives in that package:

```ct
// file: src/geo.ct
namespace com.lib;

enum Direction { North, East, South, West }

Contract Geo {
    static fn Triple(x: int) -> int {
        return x * 3;
    }
}
```

Types in a namespace are addressable three ways:

1. **Fully qualified**: `com.lib.Geo.Triple(4)`, `com.lib.Direction.North`.
2. **Same-namespace short names**: code in another file that also declares
   `namespace com.lib;` uses `Geo.Triple(...)` directly.
3. **Namespace import**: `import com.lib;` (no semicolon string) lets any file
   use short names `Geo.Triple(...)` / `Direction::North`.

```ct
// file: src/main.ct
import "geo.ct";      // pull in the file (source include)
import com.lib;       // enable short names for the com.lib package

Contract Program {
    static fn Main() {
        IO.Println(Geo.Triple(4));              // 12 — short via import
        IO.Println(com.lib.Geo.Triple(2));      // 6 — fully qualified
        IO.Println(Direction::North);           // 0
    }
}
```

- The wire format carries the fully-qualified name (`class com.lib.Geo`), so
  cross-file and cross-module references resolve by name at runtime.
- `import com.lib;` is the "wildcard import" for a package; a single contract
  inside it can be referenced by its full name without any import.
- The namespace applies to contracts, structs, and enums. The declared
  namespace is independent of file layout, but a namespace import can also
  *find* its file by location — see "Python-style module resolution" below.

### Python-style module resolution

A namespace import resolves to a file by *location*, the way Python maps
modules to paths: `import ovh.finite.hello.Terminal;` looks for
`ovh/finite/hello/Terminal.ct` (then `.oir` / `.oil` / `.orbt`) relative to the
importing file's directory, then the main file's directory, then the working
directory. This lets a directory tree BE the package:

```ct
// src/main.ct
namespace ovh.finite.hello;
import ovh.finite.hello.Terminal;

Contract Program {
    static fn Main() {
        var t = new Terminal::Terminal();
        t.Start();
    }
}
```

```text
src/
  main.ct
  ovh/finite/hello/Terminal.ct      # import ovh.finite.hello.Terminal finds this
```

When a file is loaded this way it is merged exactly like a quoted file import
(`import "path/to/file.ct";` resolves relative to the importing file's
directory); the namespace declared inside it still determines type names.
Namespace imports that have no backing file — like stdlib imports — are still
registered for short-name resolution. Types can also be constructed with a
module qualifier: `new Terminal::Terminal()` is `Terminal.Terminal`, resolved
through the namespace import to its fully-qualified wire name.

## Compiled module references (DLL-style)

`import "lib.orbt"` pulls in a **compiled** module instead of a `.ct` source
file — the Contract equivalent of referencing a DLL. The referenced module's
types become available with full type checking, and its method bodies are
statically linked into the output module, so the result runs standalone.

```text
ccl -c -f orbt -o lib.orbt src/geo.ct     # build the "library"
```

```ct
// app.ct
import "lib.orbt";     // compiled reference (static link)
import com.lib;        // short names for the linked package

Contract Program {
    static fn Main() {
        IO.Println(Geo.Triple(5));   // 15 — calls into lib.orbt's code
        IO.Println(Direction::West); // 3
    }
}
```

```text
ccl -c -f orbt -o app.orbt app.ct      # links lib.orbt's types in
ccl run app.orbt
```

- `.orbt`, `.oil`, and `.oir` are all accepted as compiled references.
- The linked types are deduplicated by fully-qualified name, so re-referencing
  the same library is safe.
- This is the building block for a package/build system: compile each module
  to `.orbt`, then link them into apps.

## In-language reflection

The `Reflect` module exposes runtime introspection over the **loaded** module
from inside Contract code — type metadata, inheritance, attributes, method and
field signatures, plus reading/writing statics and invoking methods (static and
instance) by name. The host (`ContractRuntime`) provides the metadata and
access; without a host every call returns empty/false/null.

```ct
Contract Shape {
    fn Describe() -> string { return "shape"; }
}

Contract Circle : Shape {
    radius: int;
    constructor() { this.radius = 5; }
    fn Area() -> int { return this.radius * this.radius * 3; }
    static fn Make() -> object { return new Circle(); }
}

Contract Counter {
    static count: int;
    static fn Get() -> int { return count; }
    static fn Twice(x: int) -> int { return x * 2; }
}

Contract Program {
    static fn Main() {
        // Type enumeration / existence / metadata.
        IO.Println(Reflect.HasType("Counter"));      // true
        IO.Println(Reflect.Kind("Counter"));         // Class
        IO.Println(Reflect.IsClass("Counter"));      // true
        IO.Println(Reflect.ModuleName());            // module's declared name

        // Inheritance.
        IO.Println(Reflect.Base("Circle"));          // Shape
        IO.Println(Reflect.Hierarchy("Circle")[0]);  // Circle (most-derived first)
        IO.Println(Reflect.Hierarchy("Circle")[1]);  // Shape
        IO.Println(Reflect.IsSubclassOf("Circle", "Shape"));     // true
        IO.Println(Reflect.IsAssignableFrom("Shape", "Circle")); // true

        // Method + field listing (qualified names, incl. inherited).
        var methods = Reflect.Methods("Circle");     // Circle..ctor, ..., Shape.Describe
        var fields  = Reflect.Fields("Circle");      // Circle.radius, Shape.kind

        // Signatures — Describe is declared on Shape, found through Circle.
        IO.Println(Reflect.MethodDeclaringType("Circle", "Describe")); // Shape
        IO.Println(Reflect.MethodReturn("Circle", "Describe"));        // string
        IO.Println(Reflect.MethodStatic("Counter", "Twice"));          // true
        IO.Println(Reflect.FieldType("Circle", "radius"));             // int32

        // Static field read/write by name.
        Reflect.SetStatic("Counter", "count", 7);
        IO.Println(Reflect.GetStatic("Counter", "count"));  // 7

        // Invoke a static method by name (type, method, args array).
        IO.Println(Reflect.Call("Counter", "Get", []));     // 7
        IO.Println(Reflect.Call("Counter", "Twice", [21])); // 42

        // Instance invocation: Make() returns an object handle, then Describe
        // runs on it — resolved through the base chain via reflection.
        var c = Reflect.Call("Circle", "Make", []);
        IO.Println(Reflect.Invoke("Circle", "Describe", c, [])); // "shape"
        IO.Println(Reflect.Invoke("Circle", "Area", c, []));     // 75
    }
}
```

### API summary

| Function | Returns |
|---|---|
| `Reflect.Types()` | `string[]` — every type's qualified wire name |
| `Reflect.HasType(name)` | `bool` |
| `Reflect.ModuleName()` | `string` — the loaded module's declared name |
| `Reflect.Kind(type)` | `string` — `"Class"` / `"Interface"` / `"Struct"` / `"Enum"`, or `""` |
| `Reflect.IsClass(type)` / `IsInterface` / `IsStruct` / `IsEnum` | `bool` |
| `Reflect.IsAbstract(type)` / `IsSealed(type)` | `bool` — IR type flags |
| `Reflect.Access(type)` | `string` — `"Public"` / `"Private"` / `"Protected"` / `"Internal"` |
| `Reflect.Base(type)` | `string` — direct base's wire name, or `""` |
| `Reflect.Hierarchy(type)` | `string[]` — type + all bases, most-derived first |
| `Reflect.Interfaces(type)` | `string[]` — direct interfaces by name |
| `Reflect.AllInterfaces(type)` | `string[]` — all interfaces, incl. inherited |
| `Reflect.IsSubclassOf(type, base)` | `bool` — transitive inheritance |
| `Reflect.IsAssignableFrom(type, other)` | `bool` — `other` is `type`, a subclass, or (for interfaces) an implementor |
| `Reflect.Methods(type)` | `string[]` — qualified `Type.Method` names (incl. inherited) |
| `Reflect.DeclaredMethods(type)` | `string[]` — own methods only |
| `Reflect.Fields(type)` | `string[]` — qualified `Type.field` names (incl. inherited) |
| `Reflect.DeclaredFields(type)` | `string[]` — own fields only |
| `Reflect.Resolve("Type.Method")` | `string` — canonical `DeclaringType.Method`, most-derived wins; `""` if unresolvable |
| `Reflect.MethodDeclaringType(type, method)` | `string` — the type that declares it (base for inherited) |
| `Reflect.MethodReturn(type, method)` | `string` — `"int32"`, `"string"`, `"void"`, ... |
| `Reflect.MethodParams(type, method)` | `string[]` — `"int32 x"` per param; instance methods include `"object this"` first |
| `Reflect.MethodStatic(type, method)` | `bool` |
| `Reflect.MethodVirtual(type, method)` / `MethodOverride` / `MethodAbstract` | `bool` — IR method flags |
| `Reflect.MethodBase(type, method)` | `string` — root of an override chain (`Type.Method`), or `""` |
| `Reflect.MethodAttributes(type, method)` | `string[]` — `"Name(arg, ...)"` |
| `Reflect.FieldType(type, field)` | `string` — `"int32"`, ... |
| `Reflect.FieldStatic(type, field)` | `bool` |
| `Reflect.FieldDeclaringType(type, field)` | `string` — the type that declares it |
| `Reflect.Attributes(type)` | `string[]` — `"Name(arg, ...)"` |
| `Reflect.GetStatic(type, field)` | `object` — static field value |
| `Reflect.SetStatic(type, field, value)` | `void` |
| `Reflect.Call(type, method, args)` | `object` — static method result |
| `Reflect.Invoke(type, method, receiver, args)` | `object` — instance method result; `receiver` is a handle from a previous call |

Type names accept either the short name (`Counter`) or the fully-qualified
wire name (`com.lib.Geo`). `Reflect.Call` and `Reflect.Invoke` always take the
args array as their last argument — pass `[]` for no arguments. VM-internal
objects returned by `Reflect.Call` / `Reflect.Invoke` round-trip as object
handles: pass one straight back as the `receiver` argument to call an instance
method on it. This pairs with the host-side `ObjectRT.Runtime.Reflection` API
(see `docs/REFLECTION.md`) for tooling.

## Control Flow

### If/Else

```ct
if (x > 10) {
    IO.Println("x is greater than 10");
} else {
    IO.Println("x is 10 or less");
}
```

### While Loops

```ct
var i: int = 0;
while (i < 10) {
    IO.Println(i);
    i = i + 1;
}
```

### For Loops

```ct
for (var i: int = 0; i < 10; i = i + 1) {
    IO.Println(i);
}
```

All three parts are optional, so `for (;;)` is an infinite loop. The initializer can also be a plain expression or omitted entirely:

```ct
var i: int = 0;
for (; i < 10;) {
    IO.Println(i);
    i = i + 1;
}
```

### Break and Continue

`break` exits the innermost loop; `continue` skips to the next iteration:

```ct
for (var i: int = 0; i < 100; i = i + 1) {
    if (i == 3) {
        continue; // skip 3
    }
    if (i > 10) {
        break; // stop at 11
    }
}
```

### Switch Statements

```ct
var day: int = 3;
switch (day) {
    case 1: IO.Println("Monday");
    case 2: IO.Println("Tuesday");
    case 3: IO.Println("Wednesday");
    case 4: IO.Println("Thursday");
    case 5: IO.Println("Friday");
    else: IO.Println("Weekend");
}
```

Case values can be integer **or string** literals, and the `else` case catches
anything unmatched:

```ct
switch (command) {
    case "start": IO.Println("starting");
    case "stop": IO.Println("stopping");
    else: IO.Println("unknown command");
}
```

## Expressions

### Arithmetic Operators

```ct
var sum: int = a + b;
var diff: int = a - b;
var product: int = a * b;
var quotient: int = a / b;
var remainder: int = a % b;
var negated: int = -a;       // unary minus
var shifted: int = a << n;   // left shift (integer)
var or: int = a | b;         // bitwise or (integer)
```

> `<<` is a left shift on integers: `1 << 4` is `16`. It binds tighter than the
> comparison operators but looser than `+`/`-`, so `a << b + c` is `a << (b + c)`.
> The right operand masks to the lower 5 bits of `n` (32-bit shift), as in C#.
>
> A lone `|` is bitwise OR on integers (useful for packing bytes:
> `v = lo | (hi << 8)`). It is distinct from the `|>` pipe operator and the
> `||` logical OR. `|` binds tighter than `&&`/`||` but looser than `==`, matching
> C#: `a | b == c` is `a | (b == c)`. In `match` or-patterns and `type` variant
> lists, `|` still separates alternatives.

### Logical Operators

```ct
var both: bool = a && b;    // logical and
var either: bool = a || b;  // logical or
var not: bool = !a;         // logical not
```

> Note: `&&` and `||` are not short-circuiting in v1 — both sides are always
> evaluated.

### Comparison Operators

```ct
if (a < b) { }
if (a <= b) { }
if (a == b) { }
if (a != b) { }
if (a > b) { }
if (a >= b) { }
```

### Compound Assignment

```ct
var x: int = 10;
x += 5;  // x = 15
x -= 3;  // x = 12
x *= 2;  // x = 24
x /= 4;  // x = 6
x %= 3;  // x = 0
```

Compound assignment works on variables, struct members, and array elements.

### Boolean and Float Literals

```ct
let isReady: bool = true;
let isDone: bool = false;
let pi: double = 3.14159;
let half: float = 0.5;
```

Floats are written with a decimal point (`3.14`, `0.5`, `2.0`). A number with no decimal point is an `int`.

### String Concatenation

```ct
var greeting: string = "Hello, " + name + "!";
```

String concatenation with `+` (and `+=`) lowers to a call to `String.Concat`
in the IR — the `add` opcode is only used for numeric addition.

### String Interpolation

You can embed variables directly inside a string with `{name}`:

```ct
var name: string = "Ada";
var msg = "Hello, {name}!";   // same as "Hello, " + name + "!"
```

The braces take a single identifier. Interpolation is sugar for string
concatenation.

## Arrays

Arrays hold a fixed number of elements of one type. The type syntax is
`elementType[]`.

### Array literals

```ct
var nums: int[] = [10, 20, 30];
var names: string[] = ["Ada", "Grace"];
```

### Array allocation

Create an array of a known size with `new Type[size]`:

```ct
var sizes: int[] = new int[4];
```

### Accessing elements

Indexes start at 0. Read with `arr[i]`, write with `arr[i] = value`:

```ct
var first: int = nums[0];
nums[1] = 25;
```

### Length

The `.Length` property gives the number of elements:

```ct
var count: int = nums.Length;
```

### Array types in functions

Arrays can be function parameters and return values:

```ct
fn sum(arr: int[]) -> int {
    var total: int = 0;
    for (var i: int = 0; i < arr.Length; i = i + 1) {
        total += arr[i];
    }
    return total;
}

fn makeRange(n: int) -> int[] {
    var result: int[] = new int[n];
    for (var i: int = 0; i < n; i = i + 1) {
        result[i] = i;
    }
    return result;
}
```

`String.Split` returns a `string[]`:

```ct
var parts: string[] = String.Split("a,b,c", ",");
IO.Println(parts[0]);  // "a"
```

Array types can be inferred from literals and `new Type[size]`:

```ct
let nums = [1, 2, 3];      // nums: int[]
let sizes = new int[8];    // sizes: int[]
let empty: string[] = [];  // explicit type needed for empty literals
```

### Member Access

```ct
var p: Point = new Point();
p.x = 10;
p.y = 20;
```

### Function Calls

```ct
IO.Println("Hello");
var value: int = add(5, 3);
```

## Functional Programming

### Lambda Functions

```ct
let inc: Int = fun x -> x + 1;
let double: Int = fun x -> x * 2;
let add: Int = fun x y -> x + y;   // multiple parameters
```

### Function Types

A function type is written `(T1, T2) -> R`. Parameter names may be included
(`(a: int, b: int) -> R`) — they are documentation only and describe the same
type as the unnamed form. Function types are structural: a lambda whose
parameter count/types and return type match is assignable.

Functions can return lambdas/closures, and lambdas can be passed as arguments:

```ct
fn makeAdder() -> (x:int) -> int {
    return fun a -> a + 1;
}

fn apply(f: (v:int) -> int, n: int) -> int {
    return f(n);
}

static fn Main() {
    var add: (x:int) -> int = makeAdder();
    IO.Println(add(5));          // 6

    IO.Println(apply(fun q -> q * 10, 7));  // 70
}
```

Capturing closures work the same way — the returned delegate carries its
closure, so calling the value invokes the captured state:

```ct
fn makeClosure(base: int) -> (n:int) -> int {
    return fun v -> v + base;
}

static fn Main() {
    var cl: (n:int) -> int = makeClosure(10);
    IO.Println(cl(5));   // 15
}
```

**Captured variables are shared (C#-style by-reference capture).** When a
lambda references a local of the enclosing function, that variable is hoisted
into a shared "display" object that the function body *and* every lambda
created in it reference — there is no per-lambda copy. Writes from the lambda
are visible to the enclosing scope and to any other lambda that captured the
same variable, which is what makes thread communication through captured
variables work:

```ct
static fn Main() {
    var n: int = 0;
    let inc  = fun -> { n += 1; };   // shares n with Main
    let peek = fun -> { IO.Println(n); };
    inc();
    peek();            // 1 — the same n
    IO.Println(n);     // 1
}
```

This also applies to `this` captured from an instance method (field writes
through a captured receiver mutate the same instance), and to captured
parameters.

### `Delegate<T>`

A function type can also be wrapped in `Delegate<T>`, which is the runtime's
delegate class. `Delegate<F>` and the bare function type `F` are interchangeable
for calling purposes — a `Delegate<T>` value is invoked the same way:

```ct
fn makeAdd() -> (int, int) -> int {
    return fun (x: int, y: int) -> {
        return x + y;
    };
}

fn apply(f: Delegate<(int) -> int>, x: int) -> int {
    return f(x);
}

static fn Main() {
    var add: Delegate<(int, int) -> int> = makeAdd();
    IO.Println(add(3, 4));          // 7

    IO.Println(apply(fun q -> q * 10, 7));  // 70
}
```

`Delegate<T>` can be used as a field type, and a delegate field is invoked the
same way as any other delegate — through the field name:

```ct
Contract Box {
    public add: Delegate<(int, int) -> int>;

    constructor() {
        this.add = Program.makeAdd();
    }

    fn Sum(a: int, b: int) -> int {
        return this.add(a, b);   // delegate field invocation
    }
}
```

Delegate fields can be assigned and invoked on any receiver — `this.add(...)`
inside the contract, or `box.add(...)` on an instance — and work the same for a
struct field typed with a function type.

### Calling a function's returned delegate

A function that returns a lambda/closure can be called, and its returned
delegate invoked, in one expression — `f()(args)`:

```ct
fn makeAdd() -> (int, int) -> int {
    return fun (x: int, y: int) -> {
        return x + y;
    };
}

static fn Main() {
    IO.Println(makeAdd()(5, 5));   // 10
}
```

This also works with capturing closures and with inferred result types
(`var r = makeAdd()(2, 3);` infers `int`), and when the function's declared
return type is the `Delegate<F>` form rather than the bare function type:

```ct
static fn makeScale(k: int) -> Delegate<(int) -> int> {
    return fun (v: int) -> {
        return v * k;
    };
}

static fn Main() {
    IO.Println(makeScale(100)(5));   // 500
}
```

The delegate type is always written with the angle-bracket form
`Delegate<(params) -> return>` (or, in any position that expects a delegate, the
bare function type `(params) -> return` is interchangeable). `Delegate<F>` and
`F` are the same value for calling purposes.

### Pipe Operator (`|>`)

The pipe operator passes the left value to the right function:

```ct
let result: int = 10 |> inc;        // 11
let doubled: int = 5 |> double;     // 10
let summed: int = 3 |> add(7);      // 10
```

Note the pipe is spelled `|>`; a bare `|` is the bitwise-OR operator (see
Arithmetic Operators).

### Practical Examples

```ct
Contract Program {
    fn inc(x: int) -> int {
        return x + 1;
    }

    fn square(x: int) -> int {
        return x * x;
    }

    static fn Main() {
        let inc: Int = fun x -> x + 1;
        let val: Int = 10 |> inc;
        IO.Println(val);      // 11

        let result: int = 5 |> square;
        IO.Println(result);   // 25
    }
}
```

## Pointers & Managed Memory

Contract has an explicit managed-memory system built on two layers: a
**`ManagedPtr<T>`** generic contract in the standard `Memory` library (the
high-level, type-safe face), and **`IL { ... }` inline blocks** (the escape
hatch to raw ObjektRT pointer opcodes). Both share one runtime primitive: a
pinned, bound-checked pointer block. Allocation is manual — there is no
mark-and-sweep garbage collector yet — so every block you allocate must have a
matching free.

The surface is: create a block with a fixed element count, then read and
write typed values at element positions. Element access has three C-style
forms — the deref `*m`, indexed `m[i]`, and address-of `&m` (see C-style
element access below). The raw deref instructions operate at one element at a
time (see the opcode table in the `IL {}` section below). Pointer arithmetic
(`m + i`/`m - i`) and adjustments beyond a single step are the next planned
follow-up.

### `ManagedPtr<T>` (Memory library)

`ManagedPtr<T>` is instance-based: constructing it allocates a bound-checked
buffer of `count` elements, and you read/write/free through the instance.

```ct
import ObjektRT.std.Memory;

// Allocate a managed buffer of 4 ints (manual lifetime).
var m = new ManagedPtr<int>(4);

// Store at element index.
m.Write(0, 10);
m.Write(1, 20);
m.Write(2, 42);

// Read back.
var a: int = m.Read(0);   // 10
var b: int = m.Read(2);   // 42

// Introspection.
var cnt: int    = m.Count();      // 4
var addr: long  = m.Address();    // non-zero, host-managed
var freed: bool = m.IsFreed();    // false while allocated

// Reclaim the buffer (idempotent).
m.Free();
```

The generic contract is auto-shadowed by the runtime `ManagedPtr` host binding
(`ObjektRT.Stdlib.Memory.PtrHost`, marked `[ClassBinding("ManagedPtr")]`), so
`Read`/`Write`/`Count`/... resolve to `host.`-style native calls, mirroring how
`DateTime` is bound. The element type `T` is passed to the host as its wire
type name: `ManagedPtr<T>`'s constructor passes the literal `"T"`, which
generic materialization rewrites to the concrete wire name (`int32`, `int64`,
`float32`, `float64`, ...). The host computes the element size and kind from
that name, so `<int>`, `<long>`, `<float>` and `<double>` all work with the
correct per-element size and integer-vs-float semantics.

Typed access is element-aware: `Read`/`Write` dispatch on the buffer's element
kind (4-byte int and 4-byte float share a size but not semantics), so
`ManagedPtr<float>` reads/writes real 32-bit floats and `ManagedPtr<long>`
64-bit longs. On the language side the read/write layer uses `as T` and `is T`
to unbox; because the VM surfaces every boxed numeric as a `System.Double` on
the object boundary, the primitive cast helper (`TypeHelper.CastOrNull`)
*coerces* the value to the target type rather than requiring an exact CLR
type match. This is why typed element access needs `TypeHelper.CastOrNull`
(see [Casts](#casts)).

Reading or writing past the end of the block throws (the block is
bound-checked), so out-of-range access is a runtime error, not undefined
behavior:

```ct
m.Write(99, 1);   // throws: index out of range
```

### C-style element access

`ManagedPtr<T>` (and any contract with an indexer) supports the classic
C-array operators, so buffer access reads like pointer arithmetic without the
manual stepping:

| Operator | Meaning | Lowered to |
| --- | --- | --- |
| `m[i]` | element at index `i` (read) | the indexer getter (`__idx_get`), bounds-checked |
| `m[i] = v` | write element at `i` | the indexer setter (`__idx_set`), bounds-checked |
| `m[i] += v` | read-modify-write through both accessors | — |
| `*m` | deref — current element (`*m == m[0]`) | `m[0]` |
| `*m = v` | deref-write element 0 | `m[0] = v` |
| `&m` | address-of — the block's raw address | `m.Address()` (a `long`) |

```ct
var m = new ManagedPtr<int>(4);
*m = 7;                    // *m == m[0] → writes element 0
m[1] = 20;
m[2] = 42;
var a: int = *m;           // 7   (deref read)
var b: int = m[2];         // 42  (indexed read)
m[2] += 8;                 // 50  (compound through get+set)
var raw: long = &m;        // 123456 (the block's native address)
```

Because `*m` lowers to `m[0]` and `m[i]` lowers to the indexer, all of these
are bounds-checked and throw on an out-of-range index. `&m` only makes sense
on a value that has an `Address()` method (i.e. a `ManagedPtr`); on anything
else, member resolution reports a compile error. `&` and `*` are lexed as
their own tokens (single `&` is address-of; `&&` is logical-and; `*` is
deref in prefix position and multiply in infix position).

> Printing: an expression of type `int`/`long`/`bool` cannot be concatenated
> into a string directly (`"x " + *m` fails at runtime). Convert first:
> `IO.Println("addr: " + Convert.ToString(&m))`, `Convert.ToStringB(cond)`
> for booleans. See [String Concatenation](#string-concatenation).

**Zero-copy native interop.** `&m` returns a real native pointer (the buffer is
an HGlobal allocation), so a custom host binding (`[ClassBinding(…)]`, loaded
with `ccl --bind myhost.dll`) can take that `long`, re-create the `IntPtr`, and
span it as `Span<float>`/`Span<byte>` to read and write the *same* memory as
the `.ct` side — no copying. Declare the host methods with a
`<NativeBinding("MyHost")>` facade. Full worked example:
`ContractIR/examples/NativeAudio/` (a `ManagedPtr<int>` read as a `float`
stream for gain/RMS/sine processing).

### `IL { ... }` inline blocks

`IL { ... }` is a statement that emits a sequence of raw ObjektRT opcodes
directly into the enclosing function. Each line is one instruction (mnemonic
with optional operand). No method wrapper is produced — the instructions run
in place, in the function's body, and can use the function's locals by name.
It is the escape hatch when the high-level language cannot express an
operation, and it is verified like ordinary code.

```ct
fn RawAdd() -> int {
    var r = 0;
    IL {
        ldc.i4 20
        ldc.i4 22
        add
        stloc r
    }
    return r;   // 42
}
```

The block is reconstructed verbatim from the source between the braces, so
`ldc.i4 20` keeps its mnemonic and literal as one instruction. Statements are
newline-separated (a semicolon also works).

### Pointer opcodes

The `IL {}` block can target any pointer opcode directly. The runtime opcode
table lists them as `ldptr.*`/`stptr.*` (typed deref), `ptr.addr`, `ptr.len`,
`ptr.alloc`, and `ptr.free`. In an `IL {}` block the `ptr.*` spellings map to
their `ldind.*`/`stind.*`/alloc/free opcodes:

| Example | Operation |
| --- | --- |
| `ptr.alloc` | Pop an element count then an element size (both pushed, size on top), allocate a bound-checked block, push its handle |
| `ptr.len` | Pop a block handle, push its element count |
| `ptr.addr` | Pop a block handle, push its native address |
| `ldptr.i4` / `ldptr.i8` / `ldptr.r4` / `ldptr.r8` | Pop a block handle, push the value at element 0 (typed) |
| `stptr.i4` / `stptr.i8` / `stptr.r4` / `stptr.r8` | Pop a value and a block handle, store at element 0 (typed) |
| `ptr.free` | Pop a block handle, reclaim it |

```ct
fn RawPtrLen() -> int {
    var n = 0;
    var h: object = null;
    IL {
        ldc.i4 3        // count
        ldc.i4 4        // element size
        ptr.alloc
        stloc h
        ldloc h
        ptr.len
        stloc n
        ldloc h
        ptr.free
    }
    return n;           // 3
}
```

Notes:
- The deref opcodes (`ldptr.*` / `stptr.*`) read and write at element position 0
  of a block handle — they do not take a raw address and there is no pointer
  arithmetic or indexing yet. Element access through `ManagedPtr<T>` handles
  indexing in the binding, so prefer it for indexed access.
- Pointer handles are opaque `object` values on the VM stack — there is no
  dedicated `ValueTag.Ptr` yet.
- Instructions flow through the same operand-emitter and local
  name-to-slot resolution as ordinary `stloc r`/`ldloc r`, so a local used by
  an `IL {}` block must be declared first.

## Standard Library

The standard library modules below are implemented in the generic
**`ObjektRT.Stdlib`** project (the official ObjektRT stdlib) — they contain no
Contract-specific code. The Contract compiler and runtime bind them under
their short names (`IO.Println`, `String.Length`, ...) and their
fully-qualified names (`ObjektRT.Stdlib.System.IO.Println`, ...), so both
forms work. The one Contract-specific binding, `Reflect`, is hosted by
`Contract.Runtime`.

### Memory Module

The `Memory` library lives in the Contract stdlib under the
`ObjektRT.std.Memory` namespace (a separate `Memory` project) and binds to the
runtime `ObjektRT.Stdlib.Memory.PtrHost` host. It provides the managed-pointer
generic contract:

```ct
import ObjektRT.std.Memory;

var m = new ManagedPtr<int>(4);
m.Write(0, 10);
var x: int = m.Read(0);   // 10
m.Free();
```

See Pointers & Managed Memory above for the full API and semantics.

### IO Module

The `IO` module provides basic input/output functions:

```ct
// Print to stdout without newline
IO.Print("Hello");

// Print to stdout with newline
IO.Println("Hello, World!");

// Read a line from stdin
var input: string = IO.Readln();
```

### String Module

```ct
var len: int = String.Length("hello");        // 5
var upper: string = String.ToUpper("ada");    // "ADA"
var lower: string = String.ToLower("ADA");    // "ada"
var sub: string = String.Substring("hello", 1, 3);   // "ell"
var idx: int = String.IndexOf("hello", "ll"); // 2
var has: bool = String.StartsWith("hello", "he");   // true
var tail: bool = String.EndsWith("hello", "lo");    // true
var trimmed: string = String.Trim("  hi  ");  // "hi"
var replaced: string = String.Replace("a-b", "-", "+"); // "a+b"
var concat: string = String.Concat("a", "b"); // "ab"
var parts: string[] = String.Split("a,b,c", ","); // ["a", "b", "c"]
```

Additional String helpers:

```ct
var contains: bool = String.Contains("hello", "ell");   // true
var empty: bool = String.IsNullOrEmpty("");             // true
var ws: bool = String.IsNullOrWhitespace("   ");        // true
var joined: string = String.Join("-", ["a", "b", "c"]); // "a-b-c"
var padded: string = String.PadLeft("7", 3);            // "  7"
var paddedR: string = String.PadRight("7", 3);          // "7  "
var trimmedS: string = String.TrimStart("  hi");        // "hi"
var trimmedE: string = String.TrimEnd("hi  ");          // "hi"
var last: int = String.LastIndexOf("a-b-a", "a");       // 4
var repeated: string = String.Repeat("ab", 3);          // "ababab"
var cmp: int = String.Compare("a", "b");                // negative
var ch: string = String.CharAt("hello", 1);             // "e"
var from: int = String.IndexOfFrom("hello", "l", 3);    // 3
```

### Math Module

```ct
var abs: int = Math.Abs(-7);       // 7
var min: int = Math.Min(3, 9);     // 3
var max: int = Math.Max(3, 9);     // 9
var root: float = Math.Sqrt(16.0); // 4.0
```

Also available: `Math.AbsF`, `Math.MinF`, `Math.MaxF`, `Math.Pow`, `Math.Floor`,
`Math.Ceiling`, `Math.Round`, `Math.Sin`, `Math.Cos`, `Math.Tan`, `Math.Log`,
`Math.Log10`, `Math.Exp`.

Additional Math helpers (double-based, so they work with the language's
default `double` literals; `Clamp` and `Sign` are overloaded for both `int`
and `double`):

```ct
var clamped: int = Math.Clamp(15, 0, 10);        // 10 (int overload)
var clampedD: double = Math.Clamp(1.5, 0.0, 1.0); // 1.0 (double overload)
var clamped01: double = Math.Clamp01(1.5);       // 1.0
var sign: int = Math.Sign(-5);                   // -1 (int overload)
var signD: int = Math.Sign(-2.5);                // -1 (double overload)
var trunc: int = Math.Truncate(3.7);             // 3
var lerp: double = Math.Lerp(0.0, 10.0, 0.5);    // 5.0
var rad: double = Math.DegToRad(180.0);          // ~3.14
var deg: double = Math.RadToDeg(3.14159);        // ~180
var atan2: double = Math.Atan2(1.0, 1.0);        // ~0.785
var asin: double = Math.Asin(1.0);               // ~1.57
var acos: double = Math.Acos(1.0);               // 0
var atan: double = Math.Atan(1.0);               // ~0.785
var log2: double = Math.Log2(8.0);               // 3
var logb: double = Math.LogBase(8.0, 2.0);       // 3
var nan: bool = Math.IsNaN(0.0 / 0.0);           // true
var inf: bool = Math.IsInfinity(1.0 / 0.0);      // true
var pi: double = Math.PI();                      // 3.14159...
var e: double = Math.E();                        // 2.71828...
```

### Convert Module

```ct
var n: int = Convert.ToInt32("42");
var s: string = Convert.ToString(7);
var f: float = Convert.ToFloat32("3.5");
var b: bool = Convert.ToBool("true");
```

Also available: `Convert.ToStringF`, `Convert.ToStringB`, `Convert.ToInt32F`,
`Convert.ToFloat32I`.

Additional Convert helpers:

```ct
var big: long = Convert.ToInt64("1234567890123");  // 1234567890123
var bigS: string = Convert.ToStringL(1234567890123);
var d: double = Convert.ToDouble("3.5");           // 3.5
var hex: string = Convert.ToHexString(255);        // "FF"
var fromHex: int = Convert.FromHexString("FF");    // 255
var ok: bool = Convert.TryToInt32("42");           // true
var okF: bool = Convert.TryToFloat32("3.5");       // true
var okD: bool = Convert.TryToDouble("3.5");        // true
var okB: bool = Convert.TryToBool("true");         // true
```

### Random Module

```ct
var n: int = Random.NextInt(100);   // 0..99
var f: float = Random.NextFloat();  // 0.0..1.0
```

Additional Random helpers:

```ct
var r: int = Random.NextIntRange(5, 10);   // 5..9
var rf: float = Random.NextFloatRange(0.0, 1.0);
var rd: double = Random.NextDouble();      // 0.0..1.0
var b: bool = Random.NextBool();           // true or false
var pick: object = Random.Choice(["a", "b", "c"]);
var pickS: string = Random.ChoiceString(["a", "b", "c"]);
var inc: int = Random.NextIntInclusive(10); // 0..10
```

### List, Dict, Array, and object types

`List`, `Dict`, and `Array` work with `object` handles — values the runtime
manages but the language has no named type for (the C++ native API represents
these as `void*`). Create a handle with `...Create()`, store it in an inferred
`object` variable, and pass it back to the module functions:

```ct
var list = List.Create();          // list: object
List.Add(list, 10);
List.Add(list, "hello");
IO.Println(List.Count(list));      // 2
IO.Println(List.Get(list, 0));     // 10
List.RemoveAt(list, 0);

var dict = Dict.Create();
Dict.Set(dict, "name", "Ada");
Dict.ContainsKey(dict, "name");    // true
IO.Println(Dict.Get(dict, "name"));
Dict.Remove(dict, "name");

// The Array module works on language arrays:
var nums: int[] = [5, 6, 7];
IO.Println(Array.Length(nums));    // 3
Array.Set(nums, 2, 9);
```

`List` methods: `Create`, `Add(list, item)`, `Get(list, index)`,
`Set(list, index, item)`, `Count(list)`, `RemoveAt(list, index)`. In typed
code these are usually written with their generic forms — see
[Generic Collections](#generic-collections).

`Dict` methods: `Create`, `Set(dict, key, value)`, `Get(dict, key)`,
`ContainsKey(dict, key)`, `Remove(dict, key)`, `Keys(dict)`, `Count(dict)`.

`Array` methods: `Length(arr)`, `Get(arr, index)`, `Set(arr, index, value)`.

Additional collection helpers:

```ct
// List
List.Contains(list, item);   // true when present
List.Remove(list, item);     // removes first occurrence; returns true when present
List.IndexOf(list, item);    // index or -1
List.Insert(list, index, item);
List.Clear(list);
List.Sort(list);             // in place
List.Reverse(list);          // in place
var arr = List.ToArray(list);

// Dict
var vals = Dict.Values(dict);  // array of values
Dict.Clear(dict);

// Array
Array.Contains(arr, value);    // true when present
Array.IndexOf(arr, value);     // index or -1
Array.Reverse(arr);            // in place
Array.Sort(arr);               // in place
Array.Copy(src, srcIdx, dst, dstIdx, count);
Array.Fill(arr, value);
Array.Sum(arr);  Array.Min(arr);  Array.Max(arr);  Array.Average(arr);
```

### Stack, Queue, HashSet

```ct
// Stack (LIFO)
var st = Stack.Create();
Stack.Push(st, 1);
Stack.Push(st, 2);
IO.Println(Stack.Pop(st));     // 2
IO.Println(Stack.Peek(st));    // 1
IO.Println(Stack.Count(st));   // 1
Stack.Contains(st, 1);         // true
Stack.Clear(st);

// Queue (FIFO)
var q = Queue.Create();
Queue.Enqueue(q, 1);
Queue.Enqueue(q, 2);
IO.Println(Queue.Dequeue(q));  // 1
IO.Println(Queue.Peek(q));     // 2
IO.Println(Queue.Count(q));    // 1
Queue.Contains(q, 2);          // true
Queue.Clear(q);

// HashSet (unique elements)
var hs = HashSet.Create();
HashSet.Add(hs, "x");          // true (newly added)
HashSet.Contains(hs, "x");     // true
HashSet.Remove(hs, "x");       // true
IO.Println(HashSet.Count(hs)); // 0
var elems = HashSet.ToArray(hs);
```

### JSON

`Json.Parse` turns a JSON string into a `Dict`/`List` handle (or a scalar), and
`Json.Serialize` turns a value back into a JSON string. JSON objects become
`Dict` handles, arrays become `List` handles, and scalars become
string / int / double / bool / null.

```ct
var obj = Json.Parse("{\"name\":\"Ada\",\"age\":36}");
IO.Println(Dict.Get(obj, "name"));   // Ada
IO.Println(Dict.Get(obj, "age"));    // 36
IO.Println(Json.Serialize(obj));     // {"name":"Ada","age":36}

var list = Json.Parse("[1, 2, 3]");
IO.Println(List.Count(list));        // 3
```

### Path and Directory

```ct
// Path
Path.Combine("a", "b");                 // "a/b" (platform separator)
Path.GetFileName("/a/b/file.txt");      // "file.txt"
Path.GetFileNameWithoutExtension("a.txt"); // "a"
Path.GetExtension("file.txt");          // ".txt"
Path.GetDirectoryName("/a/b/file.txt"); // "/a/b"
Path.GetFullPath("rel");                // absolute path
Path.IsPathRooted("/a");                // true
Path.ChangeExtension("a.txt", "md");    // "a.md"

// Directory
Directory.Exists("dir");                // true when present
Directory.Create("dir");
Directory.Delete("dir", true);          // recursive
Directory.GetFiles("dir");              // string[] of file paths
Directory.GetDirectories("dir");        // string[] of subdirectory paths
Directory.GetCurrentDirectory();
Directory.SetCurrentDirectory("dir");
Directory.Move("src", "dst");
```

### Process

```ct
var out = Process.Run("echo", "hello");      // "hello" (stdout, trimmed)
var code = Process.RunExitCode("cmd", "/c exit 3");  // 3
var err = Process.RunError("cmd", "/c echo oops 1>&2"); // "oops"
```

### Guid, Base64, Console

```ct
// Guid
var id = Guid.New();                 // "xxxxxxxx-xxxx-..."
var idN = Guid.NewN();               // no dashes
Guid.IsValid(id);                    // true

// Base64
var enc = Base64.Encode("hi");       // "aGk="
Base64.Decode(enc);                  // "hi"
Base64.IsValid(enc);                 // true

// Console (colors, cursor, keys)
Console.SetForeground("red");
Console.SetBackground("black");
Console.ResetColor();
Console.Clear();
Console.SetCursorPosition(0, 0);
var key = Console.ReadKey();         // single character as a string
Console.KeyAvailable();              // true when a key is waiting
Console.Write("x");  Console.WriteLine("y");
```

### File, Environment, GC, Debug, Time, Thread

```ct
// File
var text: string = File.ReadAllText("path.txt");
File.WriteAllText("path.txt", "contents");
var exists: bool = File.Exists("path.txt");
var lines: string[] = File.ReadAllLines("path.txt");
File.Copy("a.txt", "b.txt");
File.Move("b.txt", "c.txt");
File.Delete("c.txt");

// Environment
var path: string = Environment.GetEnv("PATH");
Environment.Exit(0);

// Time (returns int64 milliseconds since epoch)
var now = Time.Now();
var formatted = Time.Format(now, "yyyy-MM-dd");

// Misc
Thread.Sleep(1000);      // pause for 1 second
GC.Collect();
Debug.Assert(true, "ok");
```

### Threading

Threads follow the C# model: **a thread is a value**. `Thread.Create(delegate)`
returns a handle you can store in a variable; it does nothing until
`Thread.Start` is called. `Thread.Join(handle)` blocks until the thread
finishes, and `Thread.IsAlive(handle)` polls it:

```ct
// Create a thread and hold it in a variable — nothing runs yet.
var t = Thread.Create(fun -> { IO.Println("hello from thread"); });
Thread.Start(t);          // start it explicitly
Thread.Join(t);           // wait for it to finish
IO.Println(Thread.IsAlive(t));   // false — finished

// Threads are values: closures capture by reference (C#-style), so the
// captured variable is shared between the thread and the caller.
var n: int = 0;
let bump = fun -> { n += 1; };
var t2 = Thread.Create(bump);
Thread.Start(t2);
Thread.Join(t2);
IO.Println(n);            // 1 — the thread mutated the shared variable
```

`Thread.Sleep(ms)` pauses the current thread. `Thread.Spawn(delegate)` is the
fire-and-forget variant — it starts a background thread immediately without
returning a handle:

```ct
Thread.Spawn(fun -> { IO.Println("hello from thread"); });

Thread.Sleep(200);    // give background threads time to run
```

## Imports

```ct
import "ModuleName";
```

## Access Modifiers

Functions and structs can have access modifiers:

```ct
Contract Secrets {
    public fn publicFunction() { }
    private fn privateFunction() { }
    protected fn protectedFunction() { }
    internal fn internalFunction() { }
}
```

## Constructors

A contract with fields can declare a `constructor` that runs when an instance
is created with `new`:

```ct
Contract Person {
    name: string;
    age: int;

    constructor() {
        this.name = "anonymous";
        this.age = 0;
    }
}
```

> `new` currently supports the no-argument form `new Type()`. Constructor
> parameters can be declared but are not yet passed through the call site.

## Complete Example

```ct
Contract Vector2 {
    x: double;
    y: double;

    constructor() {
        this.x = 0.0;
        this.y = 0.0;
    }

    fn length() -> double {
        return Math.Sqrt(this.x * this.x + this.y * this.y);
    }
}

Contract Program {
    static fn Main() {
        IO.Println("Vector Demo");

        var v: Vector2 = new Vector2();
        v.x = 3.0;
        v.y = 4.0;
        var dist: double = v.length();
        IO.Println("Length: ");
        IO.Println(dist);  // 5.0
    }
}
```

## Compiler Information

- **Compiler**: Contract.Compiler (C#)
- **CLI**: Contract.Cli (installed as `ccl`)
- **Bytecode Format**: CIL1 (Contract Intermediate Language v1); text form `.oil`, binary form `.orbt`
- **Runtime**: Stack-based virtual machine (ObjektRT), hosted by `Contract.Runtime`

### Using the CLI

```text
ccl hello.ct                        # compile + run in one go
ccl -c hello.ct -o hello.orbt       # compile to .orbt binary (default)
ccl -c hello.ct -f oil              # compile to .oil text IR
ccl run hello.orbt                  # run a precompiled module
ccl --bind MyBindings.dll app.ct    # load custom host bindings
ccl --test                          # run the compiler test suite
```

- Output format follows `-f oil|orbt`, else the `-o` extension (`.oil` =
  text, otherwise binary), else defaults to `.orbt`.
- `-m Name.Method` calls a specific method instead of the entry point.
- `-d` prints the generated IR before running.
- `--bind <assembly>` makes `[ClassBinding]`-annotated classes callable as
  `Module.Method(...)` from Contract — custom host bindings that stay out of
  the standard library.

### Runtime Error Reporting

The compiler embeds `// #line N:C "source"` directives into the IR, and the
runtime reports failures with the error kind, the failing instruction (opcode
+ program counter), the original source line with a caret, and a call stack.
Output is colourised when stderr is a terminal (`NO_COLOR` disables it).

```text
runtime error: DivisionByZero: division by zero
  └─ at Program.divide  [pc=0x6 · div]
  └─ source line 3:20
     3 | return a / b;
                    ^
  └─ stack: Program.divide@0x6 → Program.Main
```

## Grammar (EBNF)

```ebnf
Start ::= TopLevel*

TopLevel ::= NamespaceDecl? ImportDecl* (ContractDecl | StructDecl | EnumDecl | FunctionDecl)*

NamespaceDecl ::= 'namespace' IDENTIFIER ('.' IDENTIFIER)* ';'
ImportDecl ::= 'import' (STRING | IDENTIFIER ('.' IDENTIFIER)*) ';'

ContractDecl ::= 'Contract' IDENTIFIER (':' IDENTIFIER)? '{' Member* '}'
Member ::= FieldDecl
         | ConstDecl
         | ContractLambdaDecl
         | AccessModifier? 'static'? 'fn' IDENTIFIER '(' ParamList? ')' (ReturnType)? Block
         | 'constructor' '(' ParamList? ')' Block
         | StructDecl
         | EnumDecl

FieldDecl ::= AccessModifier? 'static'? IDENTIFIER ':' Type ';'
ConstDecl ::= ('let' | 'const') IDENTIFIER (':' Type)? '=' ConstExpr ';'
ContractLambdaDecl ::= ('let' | 'var') IDENTIFIER (':' Type)? '=' Lambda ';'
StructDecl ::= 'struct' IDENTIFIER '{' FieldDecl* '}'
EnumDecl ::= 'enum' IDENTIFIER '{' IDENTIFIER (',' IDENTIFIER)* '}'

FunctionDecl ::= AccessModifier? 'static'? 'fn' IDENTIFIER '(' ParamList? ')' (ReturnType)? Block
ReturnType ::= '->' Type

ParamList ::= Param (',' Param)*
Param ::= IDENTIFIER (':' Type)?

Type ::= IDENTIFIER ('.' IDENTIFIER)* ('[' ']')*
       | IDENTIFIER '<' Type (',' Type)* '>'
       | '(' (Type (',' Type)*)? ')' '->' Type

Block ::= '{' Statement* '}'

Statement ::= ExprStatement | VarDecl | IfStmt | WhileStmt | ForStmt | SwitchStmt | ReturnStmt | BreakStmt | ContinueStmt

ExprStatement ::= Expression ';'
VarDecl ::= ('var' | 'let') IDENTIFIER (':' Type)? ('=' Expression)? ';'
IfStmt ::= 'if' '(' Expression ')' Block ('else' Block)?
WhileStmt ::= 'while' '(' Expression ')' Block
ForStmt ::= 'for' '(' (VarDecl | ExprStatement | ';') Expression? ';' Expression? ')' Block
BreakStmt ::= 'break' ';'
ContinueStmt ::= 'continue' ';'
SwitchStmt ::= 'switch' '(' Expression ')' '{' ( 'case' (INT | STRING) ':' Statement* )* ('else' ':' Statement*)? '}'
ReturnStmt ::= 'return' Expression? ';'

Expression ::= Assignment
Assignment ::= Or ( ('=' | '+=' | '-=' | '*=' | '/=' | '%=') Assignment )?
Or ::= And ( '||' And )*
And ::= Equality ( '&&' Equality )*
Equality ::= Relational ( ('==' | '!=') Relational )*
Relational ::= Additive ( ('<' | '<=' | '>' | '>=') Additive )*
Additive ::= Multiplicative ( ('+' | '-') Multiplicative )*
Multiplicative ::= Unary ( ('*' | '/' | '%') Unary )*
Unary ::= ('-' | '!') Unary | Postfix
Postfix ::= Primary ( '(' ArgList? ')' | '.' IDENTIFIER | '::' IDENTIFIER | '[' Expression ']' | '|>' Primary )*

Primary ::= INT | FLOAT | STRING | INTERPOLATED_STRING | 'true' | 'false' | 'null'
          | IDENTIFIER | ARRAY_LITERAL | Lambda | 'new' IDENTIFIER ('(' ')' | '[' Expression ']')
          | '(' Expression ')'
Lambda ::= 'fun' (IDENTIFIER+ | '(' Params? ')') '->' (Expression | Block)
ARRAY_LITERAL ::= '[' (Expression (',' Expression)*)? ']'

ArgList ::= Expression (',' Expression)*
```

## Future Extensions

- By-reference capture (C\#-style closure cells)
- Nested-lambda capture of outer lambda scopes
- Custom user-defined generic types (type parameters)
- Exception handling (`try`/`throw`)
- Async/await patterns
- Contracts as objects (metaclasses)

## Compiler Warnings

The compiler reports development-time warnings (not errors — compilation
continues) for suspicious code. The CLI prints them after a successful
compile; the language server shows them as yellow squiggles in the editor.
The full set:

| Warning | When it fires |
| --- | --- |
| `Variable 'x' is declared but never used` | A local is never read |
| `Variable 'x' is assigned but its value is never used` | A local is only written, never read |
| `Variable 'x' shadows a declaration in an outer scope` | A local/parameter hides a name from an enclosing block (lambda parameters are exempt) |
| `Function 'f' is never called` | A top-level or `static fn` is never referenced |
| `Contract/Struct/Enum 'X' is never used` | A declared type is never referenced by name |
| `Field 'C.f' is never used` / `assigned but never read` | A contract field is never read |
| `Function 'f' declares return type 'T' but not all code paths return a value` | A non-void function can fall off the end |
| `'return' with no value in a function returning 'T'` | A `return;` in a function that declares a return type |
| `Unreachable code — ... follows a 'return', 'break', or 'continue'` | A statement can never execute |
| `'if'/'while'/'for' condition is always true/false` | A literal condition |
| `'if'/'while'/'for' condition is an assignment — did you mean '=='?` | `=` used where `==` was probably meant |
| `Empty block — the branch does nothing` / `Empty loop body` | An empty `{}` branch or loop body |
| `Division by zero` | `/` or `%` by a constant zero |
| `Integer literal '...' exceeds the int range; value clamped to 0` | A numeric literal overflows `int` |
| `No constructor of 'X' takes N argument(s)` | `new X(...)` arity matches no declared constructor — the ctor won't run |
| `Namespace import 'A.B' is never used` | An `import A.B;` resolved nothing |
| `Imported file 'x.ct' is never used` | Every declaration in an imported file is unreferenced |
| `No static 'Main' entry point found` (info) | The module has no runnable entry point |

Dead-code analysis is name-based and intentionally conservative: types and
functions referenced only through string-based reflection (`Reflect.Invoke`)
are invisible to it and may still be reported.