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
| `Types` | Type definitions block |
| `type` | Individual type definition |
| `null` | Null literal |
| `true` / `false` | Boolean literals |
| `constructor` | Class constructor |

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

### Custom Types with `Types` Block

```ct
Types {
    type Person {
        name: string;
        age: int;
    }
    
    type Address {
        street: string;
        city: string;
        zipCode: string;
    }
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
var addr: Address = new Address();
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
    fn main() {
        let inc: Int = fun x -> x + 1;
        let val: Int = 10 |> inc;
        IO.Println(val);  // 11
    }
    
    fn square(x: int) {
        return x * x;
    }
    
    fn main() {
        let result: int = 5 |> square;
        IO.Println(result);  // 25
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
`Set(list, index, item)`, `Count(list)`, `RemoveAt(list, index)`.

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

> `Thread.Spawn` takes a delegate, which the language can't express yet.

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

```ct
Contract Person {
    struct Person {
        name: string;
        age: int;
    }
    
    constructor(name: string, age: int) {
        // Initialize person
    }
}
```

## Complete Example

```ct
import "IO";

Types {
    type Vector2 {
        x: double;
        y: double;
    }
}

Contract Geometry {
    fn distance(p1: Vector2, p2: Vector2) {
        var dx: double = p2.x - p1.x;
        var dy: double = p2.y - p1.y;
        // Return distance calculation
        return 0; // Placeholder
    }
}

Contract Program {
    static fn Main() {
        IO.Println("Geometry Calculator");
        
        var origin: Vector2 = new Vector2();
        origin.x = 0;
        origin.y = 0;
        
        var point: Vector2 = new Vector2();
        point.x = 3;
        point.y = 4;
        
        // Calculate distance
        var dist: double = Geometry.distance(origin, point);
        IO.Println("Distance: ");
        IO.Println(dist);
    }
}
```

## Compiler Information

- **Compiler**: Contract.Compiler (C#)
- **CLI**: Contract.Cli
- **Bytecode Format**: CIL1 (Contract Intermediate Language v1)
- **Runtime**: Stack-based virtual machine

## Grammar (EBNF)

```ebnf
Start ::= TopLevel*

TopLevel ::= ContractDecl | FunctionDecl | TypesDecl

ContractDecl ::= 'Contract' IDENTIFIER '{' TopLevel* '}'

FunctionDecl ::= AccessModifier? 'static'? 'fn' IDENTIFIER '(' ParamList? ')' Block

ParamList ::= Param (',' Param)*
Param ::= IDENTIFIER (':' Type)?

Type ::= IDENTIFIER ('[' ']')*

Block ::= '{' Statement* '}'

Statement ::= ExprStatement | VarDecl | IfStmt | WhileStmt | ForStmt | SwitchStmt | ReturnStmt | BreakStmt | ContinueStmt

ExprStatement ::= Expression ';'
VarDecl ::= ('var' | 'let') IDENTIFIER (':' Type)? ('=' Expression)? ';'
IfStmt ::= 'if' '(' Expression ')' Block ('else' ':'? (Block | Statement))?
WhileStmt ::= 'while' '(' Expression ')' Block
ForStmt ::= 'for' '(' (VarDecl | ExprStatement | ';') Expression? ';' Expression? ')' Block
BreakStmt ::= 'break' ';'
ContinueStmt ::= 'continue' ';'
SwitchStmt ::= 'switch' Expression '{' ( 'case' INT ':' Statement* )* ('else' ':' Statement* )? '}'
ReturnStmt ::= 'return' Expression? ';'

Expression ::= Assignment
Assignment ::= Equality ( ('=' | '+=' | '-=' | '*=' | '/=' | '%=') Assignment )?
Or ::= And ( '||' And )*
And ::= Equality ( '&&' Equality )*
Equality ::= Relational ( ('==' | '!=') Relational )*
Relational ::= Additive ( ('<' | '<=' | '>' | '>=') Additive )*
Additive ::= Multiplicative ( ('+' | '-') Multiplicative )*
Multiplicative ::= Unary ( ('*' | '/' | '%') Unary )*
Unary ::= ('-' | '!') Unary | Postfix
Postfix ::= Primary ( '(' ArgList? ')' | '.' IDENTIFIER | '::' IDENTIFIER | '[' Expression ']' | '|>' Primary )*

Primary ::= INT | FLOAT | STRING | INTERPOLATED_STRING | 'true' | 'false' | 'null' | IDENTIFIER | ARRAY_LITERAL | 'fun' IDENTIFIER+ '->' Expression | 'new' IDENTIFIER ('(' ')' | '[' Expression ']') | '(' Expression ')'
ARRAY_LITERAL ::= '[' (Expression (',' Expression)*)? ']'

ArgList ::= Expression (',' Expression)*
```

## Future Extensions

- Enhanced type system with optional annotations
- First-class string operations in IL
- Optimized opcodes for small integer constants
- Array and collection types
- Exception handling
- Async/await patterns