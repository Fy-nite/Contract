# Contract Language Reference

Contract is a statically-typed programming language that compiles to an intermediate representation (CIL1 bytecode). It features a clean syntax with contracts, functions, structs, and functional programming constructs.

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

## Types

### Built-in Types

| Type | Description | IR Type |
|------|-------------|---------|
| `int` | 32-bit integer | `int32` |
| `int64` (or `long`) | 64-bit integer | `int64` |
| `string` | Text string | `string` |
| `bool` | Boolean (`true`/`false`) | `bool` |
| `double` | 64-bit floating point | `float64` |
| `float` | 32-bit floating point | `float32` |
| `object` | Opaque object handle (lists, dicts, ...) | `object` |
| `void` | No value (return type only) | `void` |

Type names are **case-insensitive**: `Int`, `String`, and `int` all refer to the same type.

### Classes (Contracts with Fields)

A contract that declares instance fields acts as a class. Instances are
created with `new ContractName()`, and non-static member functions become
instance methods that receive the receiver implicitly:

```ct
Contract Counter {
    count: int;                    // instance field

    constructor() {
        this.count = 0;
    }

    fn increment() {               // instance method (contract has fields)
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
before the instance is returned.

> A non-static member function is an instance method **only when its contract
> declares fields**. Contracts without fields keep the legacy module-function
> behavior (no implicit receiver), preserving the module-style use of
> contracts as namespaces (e.g. `IO`, `Math`, `List`).

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

### Creating Instances

```ct
var p: Point = new Point();
var c: Counter = new Counter();  // runs the constructor
```

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
```

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

`Delegate<T>` can be used as a field type:

```ct
Contract Box {
    public add: Delegate<(int, int) -> int>;

    constructor() {
        this.add = Program.makeAdd();
    }
}
```

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
(`var r = makeAdd()(2, 3);` infers `int`).

### Pipe Operator (`|>`)

The pipe operator passes the left value to the right function:

```ct
let result: int = 10 |> inc;        // 11
let doubled: int = 5 |> double;     // 10
let summed: int = 3 |> add(7);      // 10
```

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

## Standard Library

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

### Convert Module

```ct
var n: int = Convert.ToInt32("42");
var s: string = Convert.ToString(7);
var f: float = Convert.ToFloat32("3.5");
var b: bool = Convert.ToBool("true");
```

Also available: `Convert.ToStringF`, `Convert.ToStringB`, `Convert.ToInt32F`,
`Convert.ToFloat32I`.

### Random Module

```ct
var n: int = Random.NextInt(100);   // 0..99
var f: float = Random.NextFloat();  // 0.0..1.0
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

`Thread.Sleep(ms)` pauses the current thread. `Thread.Spawn` starts a
background thread running a delegate — any lambda, capturing or not:

```ct
Thread.Spawn(fun -> { IO.Println("hello from thread"); });

var n: int = 0;
let bump = fun -> { n += 1; IO.Println(n); };
Thread.Spawn(bump);   // capturing lambda: closure lives in the shared heap

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

TopLevel ::= ContractDecl | FunctionDecl

ContractDecl ::= 'Contract' IDENTIFIER '{' Member* '}'
Member ::= FieldDecl
         | AccessModifier? 'static'? 'fn' IDENTIFIER '(' ParamList? ')' (ReturnType)? Block
         | 'constructor' '(' ParamList? ')' Block
         | StructDecl

FieldDecl ::= IDENTIFIER ':' Type ';'
StructDecl ::= 'struct' IDENTIFIER '{' FieldDecl* '}'

FunctionDecl ::= AccessModifier? 'static'? 'fn' IDENTIFIER '(' ParamList? ')' (ReturnType)? Block
ReturnType ::= '->' Type

ParamList ::= Param (',' Param)*
Param ::= IDENTIFIER (':' Type)?

Type ::= IDENTIFIER ('[' ']')*
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