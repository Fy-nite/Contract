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
  lang: "en",
  bibliography-style: "ieee",
  preface: [
    *What this document is*

    This document defines the Contract programming language, version 1.0: a
    statically-typed language that compiles to ObjektRT (`.oil/.orbt`) text / binary modules.

    This document covers:

    - the complete lexical structure and grammar.
    - the type system, including arrays, tuples, function types, generics, and inference.
    - contracts, functions, statements, and expressions.
    - sum types, enums, pattern matching, and error handling.
    - inheritance, interfaces, and attributes.
    - delegates and closures.
    - the standard library surface.
    - documentation comments and the API documentation generator.
    - the lowering model from Contract source to ObjektRT.
    - the runtime execution model (stack machine, heap, threading).

    *What this document is not*

    This document does not specify:

    - the ObjektRT text format itself — see the ObjectIL V1 specification.
    - the binary encoding of ObjektRT modules — see the ORBT V1 encoding reference.
    - the bytecode execution semantics — see the ObjectRT V1 specification.
  ],
)

#divider()
#linebreak()
#set heading(numbering: "1.1")
#set ref(supplement: "Section")
#set raw(lang: "text")

#linebreak()

= Introduction

Contract is a statically-typed, imperative programming language with
functional influences. It compiles to ObjektRT (`.oil/.orbt`), the text / binary
modules executed by the ObjektRT runtime.

Contract is designed around a small, familiar core: C-style control flow and
operators, Rust-flavoured `let`/`var` bindings, and lightweight functional
programming via lambdas, higher-order functions, and a pipe operator. It
supports generics (on contracts and functions), sum types with exhaustive
pattern matching, inheritance with interfaces, and structured error handling
via `try`/`catch`/`throw`. It is intended to be self-hosting in the long term,
so every feature is designed to be expressible in the language itself.

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

Identifiers are case-sensitive. The reserved words listed in @tokens may not
be used as identifiers.

== Keywords

The following words are reserved and may not be used as identifiers:

`Contract` `fn` `fun` `var` `let` `const` `if` `else` `while` `for` `in`
`break` `continue` `switch` `case` `return` `struct` `new` `constructor`
`static` `public` `private` `protected` `internal` `import` `Types` `type`
`export` `enum` `namespace` `match` `try` `catch` `finally` `throw`
`null` `true` `false`

Their meanings, together with every operator and punctuation symbol, are
listed in the token reference table in @tokens.

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

<tokens>

The complete set of reserved words, operators, and punctuation symbols, with
their meanings:

#table(
  columns: (auto, auto, auto),
  table.header([*Token*], [*Category*], [*Meaning*]),
  [`Contract`], [keyword], [Declares a contract (a class-like module unit)],
  [`fn`], [keyword], [Declares a function],
  [`fun`], [keyword], [Declares a lambda],
  [`var`], [keyword], [Declares a mutable variable],
  [`let`], [keyword], [Declares an immutable variable],
  [`const`], [keyword], [Declares a compile-time constant (synonym for `let` at contract scope)],
  [`if` `else`], [keyword], [Conditional statement / expression],
  [`while`], [keyword], [Loop],
  [`for`], [keyword], [C-style loop, `for`-in iteration],
  [`in`], [keyword], [`for`-in loop iterator binding],
  [`break` `continue`], [keyword], [Loop control],
  [`switch` `case`], [keyword], [Multi-way branch statement],
  [`match`], [keyword], [Pattern-matching expression],
  [`return`], [keyword], [Returns from a function],
  [`struct`], [keyword], [Declares a struct],
  [`enum`], [keyword], [Declares an enum],
  [`new`], [keyword], [Allocates an object or array],
  [`constructor`], [keyword], [Declares a constructor],
  [`static`], [keyword], [Declares a static member],
  [`public` `private` `protected` `internal`], [keyword], [Access modifiers],
  [`import`], [keyword], [Imports another source file or namespace],
  [`namespace`], [keyword], [Declares a namespace for subsequent declarations],
  [`Types` `type`], [keyword], [Custom type / sum type declarations],
  [`export`], [keyword], [Marks a declaration as exported],
  [`try` `catch` `finally` `throw`], [keyword], [Error handling],
  [`null` `true` `false`], [keyword], [Literals],
  [`+` `-` `*` `/` `%`], [operator], [Arithmetic],
  [`==` `!=` `<` `<=` `>` `>=`], [operator], [Comparison],
  [`&&` `||` `!`], [operator], [Logical and / or / not],
  [`=` `+=` `-=` `*=` `/=` `%=`], [operator], [Assignment],
  [`->`], [operator], [Lambda arrow, return-type arrow],
  [`|>`], [operator], [Pipe],
  [`>>`], [operator], [Function composition],
  [`::`], [operator], [Scoped member access],
  [`..`], [operator], [Range (end-exclusive)],
  [`..=`], [operator], [Range (end-inclusive)],
  [`=>`], [operator], [Match arm arrow],
  [`?` `:`], [operator], [Ternary conditional],
  [`.`], [operator], [Member access],
  [`[` `]`], [punctuation], [Array index, array type, array literal],
  [`(` `)`], [punctuation], [Grouping, call, parameter list, tuple],
  [`{` `}`], [punctuation], [Block, struct/type body, match body],
  [`<` `>`], [punctuation], [Attribute brackets, generic type arguments],
  [`,` `;` `:`], [punctuation], [Separators],
)

Notes:

- The `!` token is a prefix operator (logical not) only; there is no
  `!=`-style postfix.
- There is no `++` or `--` operator — use `x += 1`.
- `::` is scoped member access (`Type::member`); `..` and `..=` are range
  operators; `=>` is the match arm arrow.
- `<` and `>` double as generic type argument delimiters and attribute brackets,
  distinguished by context.

#linebreak()

= Types

Contract is statically typed. Every expression has a type, determined at
compile time. The type system in version 1.0 is intentionally small but
includes three composite shapes: named, array, and function types.

== Built-in Types

#table(
  columns: (auto, auto, auto),
  table.header([*Type*], [*IR name*], [*Description*]),
  [`int`], [`int32`], [32-bit signed integer],
  [`int64` (alias `long`)], [`int64`], [64-bit signed integer],
  [`uint`], [`uint32`], [32-bit unsigned integer],
  [`byte`], [`uint8`], [Unsigned 8-bit integer],
  [`sbyte`], [`int8`], [Signed 8-bit integer],
  [`short`], [`int16`], [Signed 16-bit integer],
  [`ushort`], [`uint16`], [Unsigned 16-bit integer],
  [`string`], [`string`], [Text string (reference)],
  [`bool`], [`bool`], [Boolean (`true`/`false`)],
  [`double`], [`float64`], [64-bit IEEE 754 float],
  [`float`], [`float32`], [32-bit IEEE 754 float],
  [`object`], [`object`], [Opaque reference handle],
  [`void`], [`void`], [No value (return type only)],
  [`null`], [—], [The null literal's type],
  [`Attribute`], [`object`], [Base type for custom attribute declarations],
)

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

== Generic Types

<generics>

A generic type is written as a type name followed by a `<`-delimited argument
list:

```ebnf
generic_type ::= identifier '<' type (',' type)* '>'
```

```text
List<int>             List<string>
Dict<string, int>     Dict<string, string[]>
```

The generic *collections* `List<T>` and `Dict<K, V>` are built into the
standard library. They are #strong[type-erased]: at runtime the value is an
`object` handle into the shared heap (the same object-backed `List`/`Dict`
classes from the stdlib), and the element types exist only for compile-time
checking and signatures.

```text
var nums: List<int> = List.Create();
List.Add(nums, 10);
var count: int = List.Count(nums);   // int

fn sum(xs: List<int>) -> int { ... }
```

Generic types are valid when the unbound name is a known generic and every
argument is a valid type. Custom generic types (user-defined type parameters)
are supported in version 1.0 (see @generics-user-defined).

== Tuple Types

A tuple type groups multiple types into a single composite:

```ebnf
tuple_type ::= '(' type (',' type)+ ')'
```

```text
(int, string)
(bool, object)
(int, int, bool)
```

Tuples are used for multi-value returns and are lowered to `object[]` at the
wire level. A single-element parenthesised type is just grouping, not a tuple.

== User-Defined Generic Types

<generics-user-defined>

Contracts and functions may declare type parameters. A generic contract is
written with `<`-delimited type parameter names after the contract name:

```text
Contract Box<T> {
    public value: T;

    constructor(val: T) { this.value = val; }

    fn get() -> T { return this.value; }
}

var intBox: Box<int> = new Box<int>(42);
var strBox: Box<string> = new Box<string>("hi");
```

Generic functions declare type parameters between the function name and the
parameter list:

```text
fn identity<T>(x: T) -> T { return x; }
fn first<T>(xs: T[]) -> T { return xs[0]; }

identity(42);          // T inferred as int
first<int>(xs);        // T explicit
```

Type parameters are inferred from call-site arguments when possible. Each
generic instantiation is materialised as a separate class at runtime with its
own static state. Multiple type parameters use commas:

```text
Contract Pair<T, U> {
    public first: T;
    public second: U;
    constructor(a: T, b: U) { this.first = a; this.second = b; }
}
```

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
always present; contracts and structs are registered during semantic
analysis. Array, function, and generic types are valid when their constituent
types are valid.

#linebreak()

= Program Structure

A Contract *program* is a single source file (plus any imported files merged
by the driver). A program consists of top-level declarations: contracts,
functions, structs, enums, sum types, and namespaces, in any order.

```ebnf
program ::= toplevel*
toplevel ::= import
           | namespace
           | contract
           | struct_decl
           | enum_decl
           | sum_type
           | fn
           | access_modifier? 'static'? (contract | fn | struct_decl | enum_decl)
           | 'export' toplevel
```

== Contracts

A *contract* is the primary organizational unit: a named, class-like container
for instance fields, functions, constructors, and nested structs. Contracts
are the closest analog to a C\# class.

```ebnf
contract ::= 'Contract' identifier '{' member* '}'
member   ::= field
           | access_modifier? 'static'? fn
           | 'constructor' '(' params ')' block-or-semicolon
           | 'struct' struct_body
field    ::= identifier ':' type ';'
```

```text
Contract Counter {
    count: int;

    constructor() { this.count = 0; }

    fn increment() { this.count += 1; }
    fn value() -> int { return this.count; }

    static fn Main() {
        var c: Counter = new Counter();
        c.increment();
        IO.Println(c.value());
    }
}
```

A contract compiles to an ObjektIR class. Its instance fields become class
fields; its functions become methods of that class.

=== Instance Fields and `this`

A contract may declare instance fields (`name: type;`). When a contract has
fields, its *non-static* member functions become instance methods: they
receive the receiver as an implicit first argument, and can read/write fields
both directly (`count`) and through `this` (`this.count`).

A non-static member function is an instance method #strong[only when its
contract declares fields]. Contracts without fields keep the legacy
module-function behavior (no implicit receiver), which preserves the
module-style use of contracts as namespaces.

Constructors run when an instance is created with `new ContractName()`. They
receive `this` too, so they can initialize fields:

```text
constructor() { this.count = 0; }
```

Instance methods are called with `.` on a value:

```text
var c: Counter = new Counter();
c.increment();
```

which lowers to pushing the receiver then `call Counter.increment`.

== Top-Level Functions

Functions declared outside any contract are collected into a synthetic
`Global` class in the emitted IR. They are callable from anywhere in the
program and are static.

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
    count: int;

    constructor() {
        this.count = 0;
    }
}
```

Constructors are analysed and code-generated like functions; they are emitted
as the class's `.ctor`. They receive `this`, so they can initialise fields
before the instance is returned.

> `new` currently supports the no-argument form `new Type()`. Constructor
> parameters can be declared but are not yet passed through the call site.

== Enums

An `enum` declares a set of named integer constants. Members are
comma-separated identifiers that fold to their zero-based index at compile time:

```ebnf
enum_decl ::= 'enum' identifier '{' identifier (',' identifier)* '}'
```

```text
enum Color { Red, Green, Blue }

var c: int = Color.Red;    // 0
var g: int = Color.Green;  // 1
```

Enums may be top-level or nested inside a contract. Scoped access via `::` is
supported: `Color::Blue` evaluates to `2`.

== Sum Types

<sum-types>

A `type` declaration defines a tagged union (sum type). Variants are separated
by `|` (or `,`) and may carry named parameters:

```ebnf
sum_type  ::= 'type' identifier '{' variant (('|' | ',') variant)* '}'
variant   ::= identifier ('(' param (',' param)* ')')?
param     ::= identifier ':' type
```

```text
type Shape {
    Circle(radius: double)
  | Rect(w: double, h: double)
  | Unit
}

type Color {
    Red | Green | Blue
}
```

Each variant becomes a separate contract inheriting from a compiler-generated
base class. Variants are constructed via static factory methods on the base:

```text
var c: Shape = Shape.Circle(2.0);
var r: Shape = Shape.Rect(3.0, 4.0);
var u: Shape = Shape.Unit;
```

Sum types work with `match` expressions for exhaustive pattern matching
(see @match-expressions).

== Inheritance and Interfaces

<inheritance>

Contracts support single inheritance for the primary base and multiple
interface-style parents. An interface is an ordinary contract whose instance
methods have no bodies (abstract methods):

```ebnf
contract ::= 'Contract' identifier (':' type (',' type)*)? '{' member* '}'
```

```text
Contract Drawable {
    fn Draw();
    fn Area() -> double;
}

Contract Circle : Drawable {
    radius: double;

    constructor(r: double) { this.radius = r; }

    fn Draw() { IO.Println("(o)"); }
    fn Area() -> double { return 3.14159 * this.radius * this.radius; }
}
```

The first type after `:` is the primary base; additional types are interfaces.
Interface parents must be stateless (no instance fields, no constructors). The
compiler verifies that all abstract methods are implemented by derived
contracts and detects inheritance cycles.

== Attributes

<attributes>

Declarations may carry attributes in angle brackets. Attributes are contracts
that inherit (transitively) from the built-in `Attribute` base type:

```ebnf
attribute   ::= '<' attribute_name ('(' arg_list? ')')? '>'
attribute_name ::= identifier
arg_list    ::= argument (',' argument)*
argument    ::= integer | float | string | 'true' | 'false' | identifier
             | '-' (integer | float)
```

```text
<NativeBinding("ModuleIO")>
Contract IO { ... }

<Deprecated("use hello2")>
fn oldHelper() -> int { ... }

<Serializable>
struct Point { ... }
```

Multiple attributes may be stacked: `<A> <B> Contract X { }`. Attributes are
supported on contracts, structs, enums, functions, and constructors. The
compiler validates that argument counts match the attribute type's constructors.

== Namespaces

A `namespace` declaration sets the fully-qualified name prefix for all
subsequent declarations in the file:

```ebnf
namespace_decl ::= 'namespace' dotted_name ';'
dotted_name    ::= identifier ('.' identifier)*
```

```text
namespace com.example;

Contract Foo { }   // fully qualified as com.example.Foo
```

Namespaces interact with `import` statements. A namespace import (without a
string path) brings all types in that namespace into scope:

```text
import ObjektRT.Stdlib.System;   // IO, String, etc. available by short name
import com.lib;                  // types in com.lib available by short name
```

Scoped access via `::` resolves against the current namespace, imported
namespaces, and the unique short-name match: `Counter::Reset()`,
`IO::Println("hi")`.

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

=== Expression-Bodied Functions

A function may use `=` instead of a block body, with the expression as an
implicit return:

```ebnf
fn ::= access_modifier? 'static'? 'fn' identifier '(' params? ')' ('->' | ':')? type '=' expression ';'
     | access_modifier? 'static'? 'fn' identifier '(' params? ')' '=' expression ';'
```

```text
static fn double(x: int) -> int = x * 2;
static fn add(a: int, b: int) = a + b;        // inferred return type
static fn negate(x: int) : int = 0 - x;       // colon variant
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

=== Implicit Lambda with `_`

The markers `_` and `@` act as holes for an implicit lambda. When an expression
contains `_`, it is automatically wrapped in `fun _ -> <expr>`:

```text
let incr = _ + 1;              // equivalent to: fun x -> x + 1
let sq = 7 |> _ * _;          // implicit lambda in pipe RHS
let padded = "hi" |> String.Concat(_, "!");
```

Multiple `_` markers in one expression are bound to the same parameter. The
implicit form works in variable initializers, pipe right-hand sides, match arm
results, and if-expression branches.

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

== Tuple Destructuring

A `var` or `let` declaration may destructure a tuple into individual bindings:

```text
var (q, r) = divmod(17, 5);
let (ok, value) = Dict.TryGetValue(dict, "name");
```

Each name binds to the corresponding element of the tuple (zero-indexed). The
expression on the right must return a tuple.

== Compile-Time Constants

At contract scope, `let` and `const` both declare compile-time constants.
Their initializers are folded at compile time:

```text
Contract Config {
    let APP_NAME: string = "contract";
    const TICKS_PER_HOUR: int = 60 * 60 * 1000;
    let DOUBLE_TICKS: int = TICKS_PER_HOUR * 2;
    let RATIO: double = 3.0 / 2;
    let FLAG: bool = true && !false;
    let GREETING: string = "hi, " + APP_NAME;
}
```

Supported folding: arithmetic, boolean logic, string concatenation,
comparisons, unary negation, and references to previously declared constants.
At contract scope, `var` must be initialised with a lambda (which becomes a
static function).

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
            | for_in_statement
            | foreach_statement
            | switch_statement
            | return_statement
            | try_statement
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

== `for`-in

```ebnf
for_in_statement ::= 'for' '(' identifier (',' identifier)? 'in' expression ')' statement
                   | 'for' '(' identifier 'in' range_expression ')' statement
```

Iterates over the elements of an array, a `List<T>`, the keys of a `Dict<K, V>`,
or a range. The `Dict` pair form binds both key and value:

```text
for (x in nums) { IO.Println(x); }
for (k, v in scores) { IO.Println(k); }
for (i in 0..5) { IO.Println(i); }
for (i in 0..=10 by 2) { IO.Println(i); }
```

=== Ranges

Range expressions generate a sequence of integers:

```text
0..5          // 0, 1, 2, 3, 4  (end-exclusive)
0..=10        // 0, 1, 2, ..., 10  (end-inclusive)
0..10 by 2    // 0, 2, 4, 6, 8  (with step)
10..=0 by -2  // 10, 8, 6, 4, 2, 0  (descending)
```

Outside `for`-in, `start..end` with integer literal bounds is folded to an
array literal at parse time.

== `foreach` (sugar)

```ebnf
foreach_statement ::= 'for' '(' identifier ':' expression ')' statement
```

A shorthand for indexed array iteration, desugared at parse time:

```text
for (item : collection) { IO.Println(item); }
// desugars to:
for (var __i: int = 0; __i < Array.Length(collection); __i += 1) {
    var item = collection[__i];
    IO.Println(item);
}
```

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

== Error Handling (`try` / `catch` / `throw`)

<error-handling>

```ebnf
try_statement  ::= 'try' block (catch_clause)+ (finally_block)?
                 | 'try' block finally_block
catch_clause   ::= 'catch' ('(' identifier? identifier ')')? block
finally_block  ::= 'finally' block
throw_statement ::= 'throw' expression ';'
```

Any value may be thrown. Multiple `catch` clauses are evaluated in order; the
first whose type matches (or the bare `catch`) handles the exception:

```text
try {
    var result = riskyOperation();
    IO.Println(result);
} catch (IOError e) {
    IO.Println("IO failed: " + e);
} catch {
    IO.Println("unknown error");
} finally {
    cleanup();
}
```

A bare `catch { ... }` catches all exceptions. A typed catch
`catch (SomeException e) { ... }` matches when the thrown value's type matches
the declared exception type. `throw expression;` raises an exception, unwinding
the call stack until a matching `catch` is found. If no `catch` handles it, the
runtime reports an unhandled exception with a stack trace.

#linebreak()

= Expressions

<expressions>

== Precedence

Expressions are parsed with the following precedence, from lowest to highest:

#table(
  columns: (auto, auto, auto),
  table.header([*Level*], [*Operators*], [*Associativity*]),
  [Assignment], [`=` `+=` `-=` `*=` `/=` `%=`], [right],
  [Ternary], [`?` `:`], [right],
  [Logical or], [`||`], [left],
  [Logical and], [`&&`], [left],
  [Equality], [`==` `!=`], [left],
  [Comparison], [`<` `<=` `>` `>=`], [left],
  [Additive], [`+` `-`], [left],
  [Multiplicative], [`*` `/` `%`], [left],
  [Pipe], [`|>` `>>`], [left],
  [Unary], [`-` `!`], [prefix],
  [Postfix], [`()` `.` `[]` `::`], [left],
)

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
`cne`, `clt`, `cle`, `cgt`, `cge`) with short-circuit control flow for
logical operators.

Notably, in version 1.0:

- `&&` is *short-circuiting*: when the left operand is `false`, the right
  operand is not evaluated and the result is `false`.
- `||` is *short-circuiting*: when the left operand is `true`, the right
  operand is not evaluated and the result is `true`.
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

== Tuple Literals

`(e1, e2, ...)` creates a tuple value (two or more comma-separated expressions
in parentheses):

```text
return (a / b, a % b);      // multi-value return
var pair = (42, "hello");
```

A single expression in parentheses is just grouping, not a tuple. Tuples are
lowered to `object[]` on the wire.

== If-Expressions

An `if`/`else` may be used as an expression, returning a value:

```text
let max = if (a > b) { a } else { b };
let sign = if (a > 0) { "+" } else if (a < 0) { "-" } else { "0" };
```

Both branches must be present and must produce the same type.

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

== Function Composition

`left >> right` composes two named functions into a new function:

```text
let dblThenInc = double >> addOne;         // fun x -> addOne(double(x))
let lenOfUpper = String.ToUpper >> String.Length;
```

Both operands must be named functions (not lambdas) in version 1.0. The result
is a lambda that applies the left function first, then the right.

== Ternary Conditional

```ebnf
ternary ::= expression '?' expression ':' expression
```

```text
let max = if (a > b) { a } else { b };   // if-expression
var label = (x > 0) ? "positive" : "non-positive";
```

The condition must be truthy-compatible. The ternary evaluates the condition,
then evaluates and returns exactly one of the two branches.

== Match Expressions

<match-expressions>

A `match` expression performs pattern matching on a value, returning the result
of the matching arm:

```ebnf
match_expression ::= 'match' '(' expression ')' '{' match_arm (',' match_arm)* '}'
match_arm        ::= pattern ('if' expression)? '=>' expression
pattern          ::= literal_pattern
                   | wildcard_pattern
                   | binding_pattern
                   | variant_pattern
                   | or_pattern
literal_pattern  ::= integer | float | string | 'true' | 'false' | 'null'
wildcard_pattern ::= '_'
binding_pattern  ::= identifier
variant_pattern  ::= identifier ('(' pattern (',' pattern)* ')')?
or_pattern       ::= pattern ('|' pattern)+
```

```text
let label = match (x) {
    0 => "ok",
    1 | 2 => "retryable",
    n if n >= 500 => "server error",
    _ => "unknown"
};
```

Pattern types:

- *Literal*: matches a specific integer, float, string, or boolean value.
- *Wildcard* (`_`): matches anything, no binding.
- *Binding*: a bare identifier that binds the scrutinee to that name.
- *Variant*: destructures a sum type variant, e.g. `Circle(r)` binds `r`.
- *Or*: multiple patterns separated by `|` for the same arm.

Guard conditions (`if guard`) may follow a pattern. The compiler checks
exhaustiveness on sum types: every variant must be covered by an arm, or a
`_` / binding catch-all must be present. A non-exhaustive match without `_`
is a compile error on sum types and a warning on other types.

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
free variables are *captured*: the compiler hoists them into a per-function
*display* object shared by the function body and all lambdas it creates:

```text
var base: int = 10;
let addBase = fun x -> x + base;   // captures base
```

lowers to:

```text
class __display_0 { field base: int32 }
// at function entry:
newobj __display_0
stloc __display
ldloc base
stfld __display_0::base

// lambda site (reuses the shared display):
newobj Delegate
dup
ldloc __display               // shared display, no copy
stfld Delegate::closure
dup
ldstr Global.__lambda_N
stfld Delegate::target
```

The lambda's compiled method takes the display object as its first parameter
(`__closure`), and reads/writes captured variables through its fields.

=== Capture Semantics

Capture is *by reference* (C\#-style closure cells). The compiler hoists
captured variables into a per-function *display* object. The display is
allocated once when the enclosing function is called; all lambdas created in
that function share the same display. Reads and writes to captured variables
go through the display's fields, so mutations are visible across invocations
and across lambdas — including to the enclosing function itself after the
lambda was created:

```text
var count: int = 0;
let bump = fun -> { count += 1; return count; };
IO.Println(bump());   // 1
IO.Println(bump());   // 2
IO.Println(count);    // 2 — visible from outer scope
```

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

These operate on `object` handles created and consumed by the module. In
Contract source they are usually used through their generic forms
(`List<int>`, `Dict<string, int>` — see @generics), which are type-erased to
these same object handles:

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
API historically represented them as `void*`). In the managed runtime they are
external-object handles that round-trip real CLR `List`/`Dictionary` objects
through the native boundary.

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
           | namespace_decl
           | contract
           | struct_decl
           | enum_decl
           | sum_type
           | types_block
           | access_modifier? 'static'? 'fn' function_header
           | 'export' toplevel

import ::= 'import' string ';'
         | 'import' dotted_name ';'

namespace_decl ::= 'namespace' dotted_name ';'

dotted_name ::= identifier ('.' identifier)*

types_block ::= 'Types' '{' type_def* '}'
type_def ::= 'type' identifier '{' struct_field+ '}'

contract ::= 'Contract' identifier (':' type (',' type)*)? '{' member* '}'
member ::= field
         | access_modifier? 'static'? 'fn' function_header
         | 'constructor' '(' params? ')' (block | ';')
         | struct_decl
         | enum_decl
field ::= identifier ':' type ';'

struct_decl ::= access_modifier? 'struct' identifier '{' struct_field+ '}'
struct_field ::= identifier ':' type ';'

enum_decl ::= 'enum' identifier '{' identifier (',' identifier)* '}'

sum_type ::= 'type' identifier '{' variant (('|' | ',') variant)* '}'
variant ::= identifier ('(' param (',' param)* ')')?

function_header ::= identifier '(' params? ')' ('->' | ':')? type ('=' expression ';')?
                  | identifier '(' params? ')' ('=' expression ';')?
                  | identifier '(' params? ')' ('->' type)? (block | ';')
params ::= param (',' param)*
param ::= identifier (':' type)?

attribute ::= '<' identifier ('(' (argument (',' argument)*)? ')')? '>'
argument ::= integer | float | string | 'true' | 'false' | identifier
           | '-' (integer | float)

block ::= '{' statement* '}'
statement ::= block
            | var_decl
            | expression_statement
            | if_statement
            | while_statement
            | for_statement
            | for_in_statement
            | foreach_statement
            | switch_statement
            | try_statement
            | return_statement
            | 'break' ';'
            | 'continue' ';'

var_decl ::= ('var' | 'let' | 'const') destructuring_or_decl
destructuring_or_decl ::= identifier (':' type)? ('=' expression)? ';'
                        | '(' identifier (',' identifier)+ ')' ('=' expression)? ';'

expression_statement ::= expression ';'

if_statement ::= 'if' '(' expression ')' statement ('else' statement)?
while_statement ::= 'while' '(' expression ')' statement
for_statement ::= 'for' '(' (var_decl | expression_statement | ';')
                        expression? ';' expression? ')' statement
for_in_statement ::= 'for' '(' identifier (',' identifier)? 'in' expression ')' statement
                   | 'for' '(' identifier 'in' range_expression ')' statement
foreach_statement ::= 'for' '(' identifier ':' expression ')' statement

range_expression ::= integer '..' integer
                   | integer '..=' integer
                   | integer '..' integer 'by' expression
                   | integer '..=' integer 'by' expression

switch_statement ::= 'switch' '(' expression ')' '{'
                       ('case' (integer | string) ':' statement*)*
                       ('else' ':' statement*)?
                     '}'

try_statement ::= 'try' block ('catch' ('(' identifier? identifier ')')? block)+ ('finally' block)?
                | 'try' block 'finally' block

return_statement ::= 'return' expression? ';'

expression ::= assignment
assignment ::= ternary (('=' | '+=' | '-=' | '*=' | '/=' | '%=') assignment)?
ternary ::= logical_or ('?' expression ':' logical_or)?
logical_or ::= logical_and ('||' logical_and)*
logical_and ::= equality ('&&' equality)*
equality ::= comparison (('==' | '!=') comparison)*
comparison ::= additive (('<' | '<=' | '>' | '>=') additive)*
additive ::= multiplicative (('+' | '-') multiplicative)*
multiplicative ::= pipe (('*' | '/' | '%') pipe)*
pipe ::= unary (('|>' | '>>') unary)*
unary ::= ('-' | '!') unary | postfix
postfix ::= primary ( '(' arg_list? ')'
                    | '.' identifier
                    | '::' identifier
                    | '[' expression ']')*
primary ::= integer | float | string | interpolated_string
          | 'true' | 'false' | 'null'
          | identifier
          | array_literal
          | tuple_literal
          | lambda
          | match_expression
          | 'new' identifier ('(' ')' | '[' expression ']')
          | '(' expression ')'

match_expression ::= 'match' '(' expression ')' '{' match_arm (',' match_arm)* '}'
match_arm ::= pattern ('if' expression)? '=>' expression
pattern ::= integer | float | string | 'true' | 'false' | 'null'
          | '_'
          | identifier
          | identifier ('(' pattern (',' pattern)* ')')?
          | pattern ('|' pattern)+

arg_list ::= expression (',' expression)*
array_literal ::= '[' (expression (',' expression)*)? ']'
tuple_literal ::= '(' expression ',' expression (',' expression)* ')'

lambda ::= 'fun' (identifier+ | '(' params? ')') '->' (expression | block)
         | 'fn' (identifier+ | '(' params? ')') '->' (expression | block)
         | 'fn' '(' params? ')' '->' expression

type ::= identifier ('[' ']')*
       | identifier '<' type (',' type)* '>'
       | '(' (type (',' type)*)? ')' '->' type
       | '(' type (',' type)+ ')'
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
- `Classes.ct` — instance fields, `this`, constructors, instance methods.
- `Generics.ct` — `List<int>`/`Dict<string, int>` end-to-end.
- `UserGenerics.ct` — user-defined generic contracts and functions.
- `MaterializedGenerics.ct` — generic instantiations with static state.
- `Inheritance.ct` — contract inheritance and interface implementation.
- `Interfaces.ct` — abstract methods and multiple inheritance.
- `Enums.ct` — enum declarations and scoped access.
- `SumTypes.ct` — tagged unions and variant construction.
- `MatchExpressions.ct` — pattern matching with guards and or-patterns.
- `Attributes.ct` — custom and built-in attribute declarations.
- `Namespaces.ct` — namespace declarations and scoped access.
- `ImportNamespaces.ct` — namespace imports and short-name resolution.
- `FunctionalSyntax.ct` — composition, implicit lambdas, expression bodies.
- `ForIn.ct` — for-in loops, ranges, and foreach sugar.
- `ComptimeConstants.ct` — compile-time constant folding.

#linebreak()

= Tooling and Hosting

<tooling>

== The `contract` CLI

The unified `Contract.Cli` tool compiles and runs Contract programs:

```text
contract hello.ct                     # compile + run in one go
contract -c hello.ct -o hello.orbt    # compile to .orbt binary (default)
contract -c hello.ct -f oil           # compile to .oil text
contract run hello.orbt / hello.oir   # run a precompiled module
contract --bind MyBindings.dll app.ct # load custom host bindings
contract --test                       # run the compiler test suite
```

- Output format is chosen by `-f oil|orbt`, else the `-o` extension (`.oil` =
  text, otherwise binary), else defaults to `.orbt`.
- `-m Name.Method` calls a specific method instead of the entry point.
- `--bind <assembly>` loads an assembly of `[ClassBinding]`-annotated classes
  and makes them callable from Contract as `Module.Method(...)` — the
  mechanism for custom host bindings that stay out of the standard library.

== `Contract.Runtime`

`Contract.Runtime` is the Contract-specific runtime host library. It wraps
`ObjectRT.Runtime` and pre-registers the standard library bindings, so the
stdlib registration lives in one place (ready to be split into a standalone
spec). Custom bindings register through `RegisterBinding(name, type)`,
`RegisterBindingAssembly(assembly)`, or the CLI's `--bind`.

== VM Error Reporting

The runtime captures `// #line N:C "source"` metadata emitted by the compiler
and reports runtime failures with full context:

```text
runtime error: DivisionByZero: division by zero
  └─ at Program.divide  [pc=0x6 · div]
  └─ source line 3:20
     3 | return a / b;
                    ^
  └─ stack: Program.divide@0x6 → Program.Main
```

The report shows the error kind, the failing IR instruction (opcode + pc),
the original source line with a caret, and the call stack. It is colourised
when stderr is a terminal (disabled by `NO_COLOR` or when redirected).

#linebreak()

= Documentation Comments

<doc-comments>

Declarations may carry *documentation comments*: consecutive lines beginning
with `///` placed immediately above a declaration. To the compiler proper they
are ordinary comments; their purpose is tooling. The `contract doc` command
(@tooling) collects them and generates browsable API documentation in HTML or
Markdown.

```text
/// <summary>Adds two integers.</summary>
/// <param name="a">The first addend.</param>
/// <param name="b">The second addend.</param>
/// <returns>The sum of a and b.</returns>
fn add(a: int, b: int) -> int {
    return a + b;
}
```

A documentation comment attaches to the next recognized declaration: a
contract, struct, function, constructor, or field. Blank lines between the
comment and its declaration are permitted.

== Supported Tags

<doc-tags>

Each tag may appear at most once per comment, except `param`, which appears
once per documented parameter:

#table(
  columns: (auto, auto),
  table.header([*Tag*], [*Meaning*]),
  [`<summary>...`], [One-line description shown beneath the declaration name],
  [`<remarks>...`], [Extended prose; the singular `<remark>` is accepted as an alias],
  [`<param name="x">...`], [Documents the parameter named `x`],
  [`<returns>...`], [Describes the return value],
  [`<example>...`], [A usage example, rendered as a code block],
)

(All tags close with the matching end tag; the table omits the closers for
brevity.) When `<summary>` is absent, the first non-tag line of the comment
becomes the summary. Tag content is plain text: version 1.0 defines no inline
markup (such as `<c>` or `<see>`), and nested tags are not interpreted.

== Generated Output

The generator parses each declaration line into its structural parts and
renders them separately in the output:

- the declaration name,
- modifier keywords (`public`, `private`, `static`, `export`, ...),
- every parameter together with its declared type,
- the declared return type (or, for fields, the declared type).

Parameter types are preserved exactly as written, including array (`int[]`),
generic (`Dict<string, int>`), and function types (`(int, int) -> int`).
Parameters are listed in declaration order; a parameter documented with
`<param>` but absent from the signature is listed last, after the declared
ones.

```text
contract doc                    # generate docs for the current project
contract doc --format md        # Markdown instead of HTML
contract doc -o site/api --title MyLib
```

The generator scans every `.ct` file under the project root (excluding build
directories). HTML output is a single self-contained `index.html` with a
navigation sidebar and client-side search; Markdown output is an `index.md`
with a table of contents.

#linebreak()

= Future Work

The following are planned but not part of version 1.0:

- nested-lambda capture of outer lambda scopes.
- `for`-loop `break`/`continue` with values, and `switch` fallthrough.
- implicit numeric-to-string coercion across the native boundary.
- method overloading (currently all method names must be unique per contract).
- operator overloading.
- async/await or coroutine support.
- `Option<T>` / `Result<T, E>` algebraic data types.
- nested generic type arguments in pattern matching.
