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

```ct
fn add(a: int, b: int) {
    return a + b;
}
```

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

## Types

### Built-in Types

- `int` - Integer numbers
- `string` - Text strings
- `double` - Floating point numbers (reserved for future use)

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

## Expressions

### Arithmetic Operators

```ct
var sum: int = a + b;
var diff: int = a - b;
var product: int = a * b;
var quotient: int = a / b;
```

### Comparison Operators

```ct
if (a < b) { }
if (a <= b) { }
if (a == b) { }
if (a != b) { }  // Uses ! operator
if (a > b) { }
if (a >= b) { }
```

### String Concatenation

```ct
var greeting: string = "Hello, " + name + "!";
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
let add: Int = fun x y -> x + y;
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

Statement ::= ExprStatement | VarDecl | IfStmt | WhileStmt | SwitchStmt | ReturnStmt

ExprStatement ::= Expression ';'
VarDecl ::= ('var' | 'let') IDENTIFIER (':' Type)? ('=' Expression)? ';'
IfStmt ::= 'if' '(' Expression ')' Block ('else' ':'? (Block | Statement))?
WhileStmt ::= 'while' '(' Expression ')' Block
SwitchStmt ::= 'switch' Expression '{' ( 'case' INT ':' Statement* )* ('else' ':' Statement* )? '}'
ReturnStmt ::= 'return' Expression? ';'

Expression ::= Assignment
Assignment ::= Equality ( '=' Assignment )?
Equality ::= Relational ( ('==' | '!=') Relational )*
Relational ::= Additive ( ('<' | '<=' | '>' | '>=') Additive )*
Additive ::= Multiplicative ( ('+' | '-') Multiplicative )*
Multiplicative ::= Unary ( ('*' | '/') Unary )*
Unary ::= ('-' | '!') Unary | Postfix
Postfix ::= Primary ( '(' ArgList? ')' | '.' IDENTIFIER | '::' IDENTIFIER | '[' Expression ']' | '|>' Primary )*

Primary ::= INT | STRING | 'null' | IDENTIFIER | 'fun' IDENTIFIER '->' Expression | 'new' IDENTIFIER '(' ')' | '(' Expression ')'

ArgList ::= Expression (',' Expression)*
```

## Future Extensions

- Enhanced type system with optional annotations
- First-class string operations in IL
- Optimized opcodes for small integer constants
- Array and collection types
- Exception handling
- Async/await patterns
