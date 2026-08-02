// Contract Language Specification — Version 1.0
// Written in Typst using the xyznote package (same style as the ObjectRT docs).

#import "@preview/xyznote:0.5.0": *
#set text(font: "Fira Code", size: 10.5pt)
#set par(justify: true, leading: 0.7em)
#show: xyznote.with(
  title: "Contract Version 1.0",
  author: "charlie santana — Finite",
  abstract: "The Contract programming language specification.",
  createtime: "2026-08-02",
  lang: "au",
  bibliography-style: "ieee",
  preface: [
    = What this document is

    This document defines the Contract programming language, version 1.0: a
    statically-typed language that compiles to ObjektRT (`.oil/.orbt`) text / binary modules.

    This document covers:

    - the complete lexical structure and grammar.
    - the type system, including arrays, function types, and inference.
    - contracts, functions, statements, and expressions.
    - delegates and closures.
    - the standard library surface.
    - the lowering model from Contract source to ObjektRT.
    - the runtime execution model (stack machine, heap, threading).

    = What this document is not

    This document does not specify:

    - the ObjektRT text format itself — see the ObjectIL V1 specification.
    - the binary encoding of ObjektRT modules — see the ORBT V1 encoding reference.
    - the bytecode execution semantics — see the ObjectRT V1 specification.
  ],
)

#divider()
#linebreak()
#set heading(numbering: "1.")
#set raw(lang: "text")

#linebreak()

= Introduction

Contract is a statically-typed, imperative programming language with
functional influences. It compiles to ObjektRT (`.oil/.orbt`), the text / binary
modules executed by the ObjektRT runtime.

Contract is designed around a small, familiar core: C-style control flow and
operators, Rust-flavoured `let`/`var` bindings, and lightweight functional
programming via lambdas, higher-order functions, and a pipe operator. It is
intended to be self-hosting in the long term, so every feature is designed to
be expressible in the language itself.

```text
Contract Program {
    static fn Main() {
        IO.Println("hello world");
    }
}
```

== Compilation Pipeline

Contract source (`.ct`) passes through four stages:

1. *Lexing* — source text becomes a token stream.
2. *Parsing* — tokens become an abstract syntax tree (AST).
3. *Semantic analysis* — symbol linking, type validation, and inference.
4. *Code generation* — the AST is lowered to an ObjektRT module, serialised as
   `.oil` (text) or `.orbt` (binary).

The output is a single ObjektRT module: contracts become classes, functions
become methods, and statement/expression lowering follows the stack-machine
conventions described in @lowering.

== Conventions

- Keywords, literals, and code appear in `monospace`.
- The grammar in @grammar uses EBNF. `*` means zero or more, `+` means
  one or more, `?` means optional, `|` means alternation.
- "Must", "must not", "should", and "may" are used in the RFC 2119 sense.

#linebreak()

= Lexical Structure

== Source Files

Contract source files use the `.ct` extension and are UTF-8 encoded. There is
no prescriptive line length or indentation rule; whitespace is insignificant
between tokens.

== Whitespace and Comments

Whitespace consists of spaces (`U+0020`), tabs (`U+0009`), and CR/LF newlines.
Whitespace separates tokens and is otherwise ignored.

A comment begins with `//` and extends to the end of the line. Comments are
treated as whitespace. There are no block comments in version 1.0.

```text
// This is a comment
var x: int = 5;  // inline comment
```

== Line Directives

The lexer recognises `\#line N` directives at the start of a line. A directive
resets the source-line counter to `N`, which is used for error reporting and
the `\#line` metadata emitted into the IR. Directives are primarily produced by
tooling that concatenates sources.

== Identifiers

Identifiers name variables, functions, contracts, structs, fields, and
parameters. An identifier begins with a letter or underscore and continues with
letters, digits, or underscores:

```ebnf
identifier ::= (letter | '_') (letter | digit | '_')*
```

Identifiers are case-sensitive. Keywords (below) may not be used as
identifiers.

== Keywords

The following words are reserved and may not be used as identifiers:

| Keyword | Purpose |
|---|---|
| `Contract` | Declares a contract (a class-like module unit) |
| `fn` | Declares a function |
| `fun` | Declares a lambda |
| `var` | Declares a mutable variable |
| `let` | Declares an immutable variable |
| `if` `else` | Conditional statement |
| `while` | Loop |
| `for` | C-style loop |
| `break` `continue` | Loop control |
| `switch` `case` | Multi-way branch |
| `return` | Returns from a function |
| `struct` | Declares a struct |
| `new` | Allocates an object or array |
| `constructor` | Declares a constructor |
| `static` | Declares a static member |
| `public` `private` `protected` `internal` | Access modifiers |
| `import` | Imports another source file |
| `Types` `type` | Custom type declarations |
| `export` | Marks a declaration as exported |
| `null` `true` `false` | Literals |

== Literals

<literals>

=== Integer Literals

An integer literal is a non-empty run of decimal digits:

```ebnf
integer ::= digit+
```

The value must fit in a signed 32-bit integer. Integers have type `int`.

```text
0  42  -7  1000000
```

=== Float Literals

A float literal is a run of decimal digits containing a single `.` followed by
at least one digit:

```ebnf
float ::= digit+ '.' digit+
```

Floats have type `double` (a 64-bit IEEE 754 value). There is no exponent
notation in version 1.0.

```text
3.14159  0.5  2.0
```

=== String Literals

A string literal is a sequence of characters between double quotes. Escape
sequences use a backslash: `\\`, `\"`, `\n`, `\t`, `\r`, `\0`. Unescaped
newlines are not permitted inside a string literal.

```text
"hello"  "line 1\nline 2"  ""
```

=== Interpolated Strings

<interpolated-strings>

A string literal that contains `{` immediately followed by an identifier and
then `}` is an *interpolated string*. Interpolation is lowered to string
concatenation with the named variable:

```text
var name: string = "Ada";
IO.Println("Hello, {name}!");   // == "Hello, " + name + "!"
```

The interpolated expression is restricted to a single identifier in version
1.0.

=== Boolean and Null Literals

The keywords `true` and `false` are boolean literals of type `bool`. The
keyword `null` is the null reference of type `null`.

== Operators and Punctuation

| Token | Meaning |
|---|---|
| `+` `-` `*` `/` `%` | Arithmetic |
| `==` `!=` `<` `<=` `>` `>=` | Comparison |
| `&&` `\|\|` `!` | Logical and / or / not |
| `=` `+=` `-=` `*=` `/=` `%=` | Assignment |
| `++`? | None — use `x += 1` |
| `->` | Lambda arrow, return-type arrow |
| `\|>` | Pipe |
| `::` | Scoped member access |
| `.` | Member access |
| `[` `]` | Array index, array type, array literal |
| `(` `)` | Grouping, call, parameter list |
| `{` `}` | Block, struct/type body |
| `,` `;` `:` | Separators |

The `!` token is a prefix operator (logical not) only; there is no `!=`-style
postfix.

#linebreak()

= Types

Contract is statically typed. Every expression has a type, determined at
compile time. The type system in version 1.0 is intentionally small but
includes three composite shapes: named, array, and function types.

== Built-in Types

| Type | IR name | Description |
|---|---|---|
| `int` | `int32` | 32-bit signed integer |
| `int64` (alias `long`) | `int64` | 64-bit signed integer |
| `string` | `string` | Text string (reference) |
| `bool` | `bool` | Boolean (`true`/`false`) |
| `double` | `float64` | 64-bit IEEE 754 float |
| `float` | `float32` | 32-bit IEEE 754 float |
| `object` | `object` | Opaque reference handle |
| `void` | `void` | No value (return type only) |
| `null` | — | The null literal's type |

Type names are case-insensitive: `int`, `Int`, and `INT` all denote the same
type.

== Array Types

An array type is written as the element type followed by `[]`. Arrays are
one-dimensional and indexed from zero.

```ebnf
array_type ::= type '[' ']'
```

```text
int[]    string[]    double[]    Person[]
```

Arrays are heap-allocated, fixed-length objects. Their length is available via
the `.Length` member (see @expressions).

== Function Types

A function type is written as a parenthesised parameter list, an arrow, and a
return type:

```ebnf
function_type ::= '(' (type (',' type)*)? ')' '->' type
```

```text
(int) -> int
() -> int
(int, string) -> bool
(int) -> int
```

Zero parameters is written `() -> R`. Function-typed values are represented at
runtime as delegate objects (see @delegates).

== The `object` Type

`object` is an opaque reference handle. It is the type of the runtime's
collections (see @collections) and of function-typed values when the
exact signature is not tracked. Values of any reference type may be passed
where `object` is expected.

== Type Inference

<inference>

A variable declaration may omit its type annotation when the initializer makes
the type obvious. The compiler infers the type from the initializer:

```text
let a = 5;              // a: int
let b = "hello";        // b: string
let c = true;           // c: bool
let d = 3.5;            // d: double
let p = new Person();   // p: Person
let nums = [1, 2, 3];   // nums: int[]
let f = fun x -> x + 1; // f: (int) -> int
```

Inference is performed by the semantic analyser: it walks the initializer and
derives the type from literals, `new` expressions, calls (via declared or
system return types), and other variables. When the type cannot be determined
(such as `let x = null;`), an explicit annotation is required.

== Type Registry

The compiler maintains a registry of known type names. The built-in names are
always present; contracts, structs, and `Types`-block definitions are
registered during semantic analysis. Array and function types are valid when
their constituent types are valid.

#linebreak()

= Program Structure

A Contract *program* is a single source file (plus any imported files merged
by the driver). A program consists of top-level declarations: contracts,
functions, structs, and `Types` blocks, in any order.

```ebnf
program ::= toplevel*
toplevel ::= import
           | Types block
           | contract
           | struct
           | fn
           | access_modifier? 'static'? (Contract | fn | struct)
```

== Contracts

A *contract* is the primary organizational unit: a named, class-like container
for functions, constructors, and nested structs. Contracts are the closest
analog to a C\# class or a namespace.

```ebnf
contract ::= 'Contract' identifier '{' member* '}'
member   ::= access_modifier? 'static'? fn
           | 'constructor' '(' params ')' block-or-semicolon
           | 'struct' struct_body
```

```text
Contract Math {
    fn add(a: int, b: int) -> int {
        return a + b;
    }

    struct Point {
        x: int;
        y: int;
    }
}
```

A contract compiles to an ObjektIR class. Its functions become methods of that
class.

== Top-Level Functions

Functions declared outside any contract are collected into a synthetic
`Global` class in the emitted IR. They are callable from anywhere in the
program and are static.

== The `Types` Block

A `Types` block declares custom value types (structs) at the top level:

```ebnf
types_block ::= 'Types' '{' type_def* '}'
type_def    ::= 'type' identifier '{' field (',' field)* '}'
field       ::= identifier ':' type ';'
```

```text
Types {
    type Point {
        x: int;
        y: int;
    }

    type Person {
        name: string;
        age: int;
    }
}
```

Each `type` compiles to an ObjektIR struct. Instances are created with
`new TypeName()`.

== Structs

A `struct` is a lightweight collection of fields. Structs may be declared
inside a contract or at the top level. Fields are `name: type;` pairs.

```text
Contract Geometry {
    struct Point {
        x: int;
        y: int;
    }
}
```

== Imports

An `import` statement loads another `.ct` file and merges its declarations
into the current program. The path is a string literal, resolved relative to
the importing file.

```ebnf
import ::= 'import' string ';'
```

```text
import "Utils.ct";
```

The compiler driver resolves imports recursively and prevents a file from being
loaded twice.

== Access Modifiers and `static`

Functions and structs may be annotated with an access modifier (`public`,
`private`, `protected`, `internal`) and/or `static`. `static` marks a function
that does not require an instance and is the required modifier on the entry
point `Main`. Access modifiers and `static` are preserved into the IR method
flags.

```text
Contract Secrets {
    public fn open() { }
    private fn hidden() { }
    static fn shared() { }
}
```

== Constructors

A contract may declare a `constructor` that runs when an instance is created:

```ebnf
constructor ::= 'constructor' '(' params ')' block
```

```text
Contract Counter {
    constructor(start: int) {
        IO.Println("started at " + start);
    }
}
```

Constructors are analysed and code-generated like functions; they are emitted
as the class's `.ctor`.

#linebreak()

= Functions

<functions>

== Function Declarations

A function is declared with `fn`, a name, a parameter list, an optional return
type, and a body:

```ebnf
fn ::= access_modifier? 'static'? 'fn' identifier '(' params? ')' ('->' type)? block
```

```text
fn add(a: int, b: int) -> int {
    return a + b;
}
```

== Parameters

Parameters are `name: type` pairs separated by commas. A parameter type is
required in version 1.0 (an untyped parameter is an error). Parameters are
positional: the `n`-th parameter maps to argument `n` at the call site and to
`ldarg n` in the IR.

```ebnf
params ::= param (',' param)*
param  ::= identifier ':' type
```

== Return Types

The return type is declared with `-> type` after the parameter list. When
omitted:

- `Main` defaults to `void`.
- every other function defaults to `int` (version 1.0's legacy default).

A function whose body falls off the end without a `return` implicitly returns
the zero value of its declared type: `0` for numeric types, `false` for
`bool`, `null` for reference types, and nothing for `void`.

== The Entry Point

Execution begins at `static fn Main()` of a contract (the first contract with
a `Main` method in IR terms). `Main` is conventionally `void` and takes no
arguments in version 1.0.

== Lambdas and Function Values

Lambdas are expressions that produce function values (see
@lambdas). They may be assigned to variables, passed as arguments,
returned from functions, and invoked through the delegate mechanism (see
@delegates).

#linebreak()

= Variables

== Declaration

Variables are declared with `var` (mutable) or `let` (immutable):

```ebnf
var_decl ::= ('var' | 'let') identifier (':' type)? ('=' expression)? ';'
```

```text
var count: int = 0;
let name: string = "Ada";
var uninitialized: int;
```

A `let` binding may not be reassigned; assigning to it is a compile error. A
`var` binding may be reassigned.

== Initialization and Inference

The initializer is optional. When present and the type annotation is omitted,
the type is inferred (see @inference). When both are absent (and the
type is not inferable), an explicit annotation is required.

== Scope

Variables are scoped to the enclosing block. A variable declared in a `for`
initializer is scoped to the loop body (like C). Shadowing an outer variable
with an inner declaration is permitted.

#linebreak()

= Statements

A block is a sequence of statements enclosed in braces. Statements execute in
order.

```ebnf
block     ::= '{' statement* '}'
statement ::= block
            | var_decl
            | expression_statement
            | if_statement
            | while_statement
            | for_statement
            | switch_statement
            | return_statement
            | break_statement
            | continue_statement
```

== Expression Statements

An expression followed by a semicolon is an expression statement. Its value is
discarded. In the IR, every expression leaves its result on the stack, so an
expression statement is followed by a `pop`.

```text
IO.Println("hi");   // call ...; pop
x = 5;              // dup; stloc x (value discarded by the statement pop)
```

== `if` / `else`

```ebnf
if_statement ::= 'if' '(' expression ')' statement ('else' statement)?
```

The condition must be truthy-compatible (numeric or boolean). The `else`
branch binds to the nearest unmatched `if`. `else if` chains are expressed by
nesting an `if` inside the `else`.

== `while`

```ebnf
while_statement ::= 'while' '(' expression ')' statement
```

The condition is evaluated before each iteration. See @lowering for the
stack-machine lowering of loops.

== `for`

```ebnf
for_statement ::= 'for' '(' (var_decl | expression_statement | ';')
                          expression? ';' expression? ')' statement
```

All three clauses are optional, so `for (;;)` is an infinite loop. The
initializer runs once; the condition is checked before each iteration; the
update runs at the end of each iteration.

== `break` and `continue`

`break` exits the innermost enclosing loop. `continue` skips to the next
iteration (running the loop update first, for `for` loops). Both require an
enclosing loop.

== `switch`

```ebnf
switch_statement ::= 'switch' '(' expression ')' '{'
                        ( 'case' (integer | string) ':' statement* )*
                        ( 'else' ':' statement* )?
                     '}'
```

Case values are integer or string literals. The `else` case is the default.
Execution does not fall through between cases in version 1.0 — each case is an
independent block. The switch expression is evaluated once and compared
against the case values.

```text
switch (command) {
    case "start": IO.Println("starting");
    case "stop": IO.Println("stopping");
    else: IO.Println("unknown");
}
```

== `return`

```ebnf
return_statement ::= 'return' expression? ';'
```

Returns the given value (for non-void functions) or exits the function (for
void functions). A function with a return type must return a value on every
path; falling off the end returns the zero value (see @functions).

#linebreak()

= Expressions

<expressions>

== Precedence

Expressions are parsed with the following precedence, from lowest to highest:

| Level | Operators | Associativity |
|---|---|---|
| Assignment | `=` `+=` `-=` `*=` `/=` `%=` | right |
| Logical or | `\|\|` | left |
| Logical and | `&&` | left |
| Equality | `==` `!=` | left |
| Comparison | `<` `<=` `>` `>=` | left |
| Additive | `+` `-` | left |
| Multiplicative | `*` `/` `%` | left |
| Unary | `-` `!` | prefix |
| Postfix | `()` `.` `[]` `::` `\|>` | left |

== Literals

Integer, float, string, interpolated-string, boolean, and `null` literals are
expressions (see @literals).

== Identifiers

An identifier references a variable, parameter, function, or lambda bound in
scope. Reading it pushes its value; in IR it lowers to `ldloc`/`ldarg`, or
`ldfld` through the closure when captured.

== Arithmetic and Comparison

Binary arithmetic (`+ - * / %`), comparison (`== != < <= > >=`), and logical
(`&& ||`) operators follow their usual numeric semantics. The IR lowers them
to the corresponding stack opcodes (`add`, `sub`, `mul`, `div`, `rem`, `ceq`,
`cne`, `clt`, `cle`, `cgt`, `cge`, `and`, `or`).

Notably, in version 1.0:

- `&&` and `||` are *not* short-circuiting — both operands are always
  evaluated.
- string `+` (and `+=`) is *not* the `add` opcode — it lowers to a call to
  `String.Concat` (see @lowering).

== Unary Operators

`-x` negates a numeric operand (`neg`). `!x` is logical not (`not`).

== Assignment as an Expression

<assignment>

Assignment returns the assigned value, C\#-style. `x = e` evaluates `e`,
stores it into `x`, and leaves the value on the stack:

```text
if ((m = m * m) == 25) { ... }
```

The same holds for compound assignment: `x += e` computes `x + e`, stores it,
and leaves the result on the stack. This is a deliberate, documented part of
the lowering (see @lowering).

== Calls

`callee(args...)` invokes a function, external method, or delegate. Arguments
are pushed left-to-right. The lowering depends on the callee:

- known function → `call Type.method(...)`
- known lambda bound to a variable → `call Global.__lambda_N(...)` (direct)
- function-typed value → `callvirt Delegate.Invoke(...)` (see
  @delegates)

== Member Access

`.` accesses a field or a built-in property. `obj.length`? No — the built-in
array property is `.Length`:

```text
var p: Point = new Point();
p.x = 10;
var count: int = nums.Length;
```

`Type::member` is scoped access to a module or type member.

== Indexing

`a[i]` reads (`ldelem`) and `a[i] = v` writes (`stelem`) an array element.
Indexes are zero-based.

== `new`

`new TypeName()` allocates an object (`newobj`). `new TypeName[size]` allocates
an array of the given length (`newarr`):

```text
var p: Point = new Point();
var nums: int[] = new int[4];
```

== Array Literals

`[e1, e2, ...]` allocates and initializes an array:

```text
var nums: int[] = [10, 20, 30];
```

The element type is inferred from the elements (all elements must share a
type). The literal lowers to `ldc.i4 count; newarr T` followed by
`dup; ldc.i4 i; <elem>; stelem` per element.

== Lambdas

<lambdas>

Lambdas have four syntactic forms:

```ebnf
lambda ::= 'fun' identifier+ '->' expression            // fun x -> x + 1
         | 'fun' identifier+ '->' block                 // fun x -> { ... }
         | 'fun' '(' params? ')' '->' expression        // fun (x, y) -> ...
         | 'fun' '(' params? ')' '->' block             // fun (x: int) -> { ... }
```

```text
let inc = fun x -> x + 1;
let add = fun (a: int, b: int) -> { return a + b; };
let greet = fun () -> { IO.Println("hi"); };
```

Lambda parameter types default to `int` when not annotated. The body may be an
expression (whose value is returned) or a block (whose `return` statements
provide the value). Lambdas are function values — see @delegates.

== Pipe Operator

`left |> right` passes `left` as the argument to `right`:

```text
let result = 3 |> addOne |> square;   // square(addOne(3))
```

The right side must be a function name or a lambda.

== String Interpolation

String interpolation in expressions is defined in @interpolated-strings: a
string literal containing `{name}` is lowered to `"..." + name + "..."`
(string concatenation via `String.Concat`).

#linebreak()

= Delegates and Closures

<delegates>

== Lambda Values and the `Delegate` Class

A lambda in a value position (assigned to a variable, passed as an argument,
or returned) is lowered to an allocation of the compiler-generated `Delegate`
class:

```text
class Delegate {
    field target: string
    field closure: object
}
```

`target` holds the qualified name of the lambda's compiled method
(`Global.__lambda_N`); `closure` holds the captured environment, when any.

== Invocation

Invoking a function-typed value compiles to `callvirt Delegate.Invoke`:

```text
fn apply(f: (int) -> int, x: int) -> int {
    return f(x);
}
```

lowers to:

```text
ldarg x
ldarg f
callvirt Delegate.Invoke(int32) -> int32
```

The runtime resolves `Delegate.Invoke` specially: it pops the receiver, reads
`target` and `closure` from the delegate's heap object, and calls the target
function with the closure prepended to the argument list when present. Direct
calls to a lambda bound to a known variable bypass this and emit a plain
`call Global.__lambda_N` — the fast path.

== Capture

A lambda may reference variables from its enclosing *function* scope. Such
free variables are *captured*: the compiler generates a per-lambda closure
class and copies the variables into it at the point the lambda is created:

```text
var base: int = 10;
let addBase = fun x -> x + base;   // captures base
```

lowers to:

```text
class __closure_1 { field base: int32 }
// lambda site:
newobj Delegate
dup
newobj __closure_1
dup
ldloc base
stfld __closure_1::base
stfld Delegate::closure
dup
ldstr Global.__lambda_N
stfld Delegate::target
```

The lambda's compiled method takes the closure object as its first parameter
(`__closure`), and reads/writes captured variables through its fields.

=== Capture Semantics

Capture is *by value* in version 1.0: the closure field copies the variable's
value at creation. Writes *through the closure field* (e.g. a counter lambda
doing `n += 1`) work and are visible across invocations, including across
threads. But mutations of the original variable *after* the lambda was created
are not observed. A C\#-style closure cell (by-reference capture) is planned
but not implemented.

=== Capture Scope

Only the enclosing *function* scope is visible to capture analysis. A lambda
nested inside another lambda cannot capture the outer lambda's own parameters
or locals — the outer lambda's parameters are only visible as closure fields
of the outer lambda, and that indirection is not yet resolved. Closure
*factories* work around this naturally, because the factory's own locals are
in its (function) scope:

```text
fn makeCounter(start: int) -> () -> int {
    var n: int = start;
    return fun () -> { n += 1; return n; };   // captures factory's n
}
```

== Function-Typed Parameters and Locals

A parameter or local declared with a function type (e.g. `f: (int) -> int`)
is tracked by the code generator; calling it compiles to
`callvirt Delegate.Invoke` with the signature derived from its type.

#linebreak()

= Standard Library

The standard library is a set of *bindings*: C\# static classes registered with
the compiler's symbol table under a binding name. Scripts call them as
`Module.Method(args)`. At runtime, calls resolve through the host's native
dispatch chain (explicit natives first, then reflection-based resolvers).

== `IO`

Console input and output.

```text
IO.Println(value)  // print + newline
IO.Print(value)     // print, no newline
IO.Readln() -> string
```

== `String`

```text
String.Concat(a, b) -> string
String.Length(s) -> int
String.Substring(s, start, length) -> string
String.IndexOf(s, sub) -> int
String.StartsWith(s, prefix) -> bool
String.EndsWith(s, suffix) -> bool
String.Trim(s) -> string
String.ToUpper(s) -> string
String.ToLower(s) -> string
String.Replace(s, old, new) -> string
String.Split(s, separator) -> string[]
```

The language's string `+` and `+=` operators lower to `String.Concat`.

== `Math`

```text
Math.Abs(x: int) -> int          Math.AbsF(x: float) -> float
Math.Min(a: int, b: int) -> int  Math.MinF(a: float, b: float) -> float
Math.Max(a: int, b: int) -> int  Math.MaxF(a: float, b: float) -> float
Math.Sqrt(x: float) -> float     Math.Pow(x: float, y: float) -> float
Math.Floor(x: float) -> int      Math.Ceiling(x: float) -> int
Math.Round(x: float) -> int
Math.Sin/Cos/Tan/Log/Log10/Exp(x: float) -> float
```

== `Convert`

```text
Convert.ToInt32(s: string) -> int
Convert.ToString(v: int) -> string
Convert.ToStringF(v: float) -> string
Convert.ToStringB(v: bool) -> string
Convert.ToFloat32(s: string) -> float
Convert.ToInt32F(v: float) -> int
Convert.ToFloat32I(v: int) -> float
Convert.ToBool(s: string) -> bool
```

== `Random`

```text
Random.NextInt(max: int) -> int    // [0, max)
Random.NextFloat() -> float        // [0, 1)
```

== `List`, `Dict`, `Array`

<collections>

These operate on `object` handles created and consumed by the module:

```text
List.Create() -> object
List.Add(list, item)  List.Get(list, index) -> object
List.Set(list, index, item)  List.Count(list) -> int
List.RemoveAt(list, index)

Dict.Create() -> object
Dict.Set(dict, key, value)  Dict.Get(dict, key) -> object
Dict.ContainsKey(dict, key) -> bool  Dict.Remove(dict, key) -> bool
Dict.Keys(dict) -> object   Dict.Count(dict) -> int

Array.Length(arr) -> int    Array.Get(arr, index) -> object
Array.Set(arr, index, value)
```

The handles are heap indices into the runtime's object heap (the C++ native
API historically represented them as `void*`).

== `File`, `Environment`, `GC`, `Debug`, `Time`, `Thread`

```text
File.ReadAllText(path) -> string    File.WriteAllText(path, contents)
File.Exists(path) -> bool           File.ReadAllLines(path) -> string[]
File.Copy(src, dst)  File.Move(src, dst)  File.Delete(path)

Environment.GetEnv(name) -> string  Environment.Exit(code)

GC.Collect()
Debug.Assert(condition, message)

Time.Now() -> int64                 // epoch milliseconds
Time.Format(timestamp, format) -> string

Thread.Sleep(ms)
Thread.Spawn(delegate)              // runs the delegate on a new thread
```

#linebreak()

= Lowering Model

<lowering>

Contract lowers to ObjektIR, a stack-machine text IR. The following
conventions are load-bearing for any consumer of the `.oir` output.

== Stack Discipline

Every expression leaves its result on the stack. Statement-level expression
statements therefore end with `pop`. Assignments leave the assigned value on
the stack (see @assignment), so they participate in expressions and are
also popped in statement position.

== Loops

The ObjektIR `while (stack)` convention requires the body to refresh the
condition each iteration:

- the runtime's `while` duplicates the condition value at the loop head
  (`dup; brfalse`), so the body begins with *two* copies.
- the compiler emits a leading `pop` (removing the extra), the body, then the
  re-computed condition at the end.
- after the loop exits, one final copy remains, which the compiler pops.
- `break` pushes a dummy value so the trailing pop is balanced on every exit
  path.
- `continue` pushes the next condition (running the loop update first for
  `for`).

```text
// while (cond) { body }
<cond>          // push initial condition
while (stack) {
    pop         // consume the dup'd extra copy
    <body>
    <cond>      // recompute for the next iteration
}
pop             // consume the final copy
```

== Calls

Calls push arguments left-to-right, then the call:

```text
<argN> ... <arg0> call Type.method(...)
```

The `this`-style receiver for `callvirt Delegate.Invoke` is pushed last (after
the args), which the runtime's special case pops first.

== String Concatenation

`a + b` where either operand is a string lowers to:

```text
<a> <b> call String.Concat(string, string) -> string
```

not to the `add` opcode. The compiler records the resolved type of every `+`
expression during semantic analysis to make this decision.

== Lambdas and Closures

See @delegates.

#linebreak()

= Runtime Model

== Module Loading

The `.oir` text is parsed by the runtime's ObjectIL parser into an
`ORBTModule`, then compiled by `ModuleCompiler` into a `CompiledModule`: flat
tables of types, fields, functions, strings, and a `FunctionMap` mapping
qualified names (`Type.method`) to function indices.

== Execution

An `Executor` (interpreter or JIT) executes the module. The executor owns a
call stack (frames + value stack) and a reference to the shared module state:
the object heap (`List<byte[]>`), static field storage (`Value[]`), and the
interned string table.

== Objects and the Heap

Objects are fixed-size byte buffers in the heap, sized by the declaring
type's instance size (field count × 16-byte slot). Object handles are heap
indices; `ValueTag.Obj` values carry a handle. Fields are laid out
contiguously per type, so a type's fields occupy
`[FieldOffset, FieldOffset + FieldCount)` in the global field table.

== Threads

<threads>

A spawned thread runs a *fresh interpreter over the same shared state*: same
heap, same statics, same string table, but its own call stack. Because the
delegate's `target` and `closure` live in the shared heap, a delegate created
on one thread is valid on another — the spawned thread reads them and runs the
target function. The string table is interning-locked; the heap is not
locked in version 1.0 (threads typically touch their own closures; sharing
mutable objects across threads is the programmer's responsibility).

#linebreak()

= Grammar

<grammar>

The complete EBNF grammar:

```ebnf
program ::= toplevel*

toplevel ::= import
           | types_block
           | contract
           | struct_decl
           | access_modifier? 'static'? 'fn' function_header
           | 'export' toplevel

import ::= 'import' string ';'

types_block ::= 'Types' '{' type_def* '}'
type_def ::= 'type' identifier '{' struct_field+ '}'

contract ::= 'Contract' identifier '{' member* '}'
member ::= access_modifier? 'static'? 'fn' function_header
         | 'constructor' '(' params? ')' (block | ';')
         | struct_decl

struct_decl ::= access_modifier? 'struct' identifier '{' struct_field+ '}'
struct_field ::= identifier ':' type ';'

function_header ::= identifier '(' params? ')' ('->' type)? (block | ';')
params ::= param (',' param)*
param ::= identifier (':' type)?

block ::= '{' statement* '}'
statement ::= block
            | var_decl
            | expression_statement
            | if_statement
            | while_statement
            | for_statement
            | switch_statement
            | return_statement
            | 'break' ';'
            | 'continue' ';'

var_decl ::= ('var' | 'let') identifier (':' type)? ('=' expression)? ';'
expression_statement ::= expression ';'
if_statement ::= 'if' '(' expression ')' statement ('else' statement)?
while_statement ::= 'while' '(' expression ')' statement
for_statement ::= 'for' '(' (var_decl | expression_statement | ';')
                        expression? ';' expression? ')' statement
switch_statement ::= 'switch' '(' expression ')' '{'
                       ('case' (integer | string) ':' statement*)*
                       ('else' ':' statement*)?
                     '}'
return_statement ::= 'return' expression? ';'

expression ::= assignment
assignment ::= logical_or (('=' | '+=' | '-=' | '*=' | '/=' | '%=') assignment)?
logical_or ::= logical_and ('||' logical_and)*
logical_and ::= equality ('&&' equality)*
equality ::= comparison (('==' | '!=') comparison)*
comparison ::= additive (('<' | '<=' | '>' | '>=') additive)*
additive ::= multiplicative (('+' | '-') multiplicative)*
multiplicative ::= unary (('*' | '/' | '%') unary)*
unary ::= ('-' | '!') unary | postfix
postfix ::= primary ( '(' arg_list? ')'
                    | '.' identifier
                    | '::' identifier
                    | '[' expression ']'
                    | '|>' primary )*
primary ::= integer | float | string | interpolated_string
          | 'true' | 'false' | 'null'
          | identifier
          | array_literal
          | lambda
          | 'new' identifier ('(' ')' | '[' expression ']')
          | '(' expression ')'
arg_list ::= expression (',' expression)*
array_literal ::= '[' (expression (',' expression)*)? ']'

lambda ::= 'fun' (identifier+ | '(' params? ')') '->' (expression | block)

type ::= identifier ('[' ']')*
       | '(' (type (',' type)*)? ')' '->' type
```

#linebreak()

= Conformance and Testing

The compiler ships a test suite (`dotnet run --project Contract.Cli -- --test`)
that lexes and parses every `.ct` under `tests/success` (expecting no errors)
and `tests/failure` (expecting errors). A scratch harness
(`scratch/RunOir`) additionally compiles and *executes* modules through the
ObjektRT runtime to validate the lowering end-to-end.

Key validation programs:

- `HelloWorld.ct` — entry point, `IO`.
- `BinarySearch.ct` — loops, assignment-as-expression.
- `NewFeatures.ct` — inference, floats, compound assignment, `for`/`break`/
  `continue`, string returns.
- `Delegates.ct` — lambda values, higher-order functions.
- `Closures.ct` — capture, captured writes, closure factories.
- `Threading.ct` — `Thread.Spawn` with capturing lambdas.

#linebreak()

= Future Work

The following are planned but not part of version 1.0:

- by-reference capture (C\#-style closure cells).
- nested-lambda capture of outer lambda scopes.
- `for`-loop `break`/`continue` with values, and `switch` fallthrough.
- short-circuit `&&`/`||`.
- implicit numeric-to-string coercion across the native boundary.
- contracts as objects (metaclasses), generics, exceptions, `try`/`throw`,
  and first-class array operations in the IR.
