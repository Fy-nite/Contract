// Contract Language Demo — A Quick Tour
// A beginner-friendly walkthrough of the Contract programming language,
// rendered from the docs/tutorials/*.md series into a single PDF.

#import "@preview/xyznote:0.5.0": *
#set text(font: "Fira Code", size: 10.5pt)
#set par(justify: true, leading: 0.7em)
#show: xyznote.with(
  title: "Contract — A Quick Tour",
  author: "charlie santana — Finite",
  abstract: "A hands-on walkthrough of the Contract programming language: eight lessons with complete, runnable programs — from \"hello world\" to the standard library.",
  createtime: "2026-08-02",
  lang: "en",
  bibliography-style: "ieee",
  preface: [
    *What this document is*

    This is a beginner-friendly tour of Contract, version 1.0: a
    statically-typed language that compiles to ObjektRT modules. Each lesson
    builds on the previous one, and every example is a complete program you
    can compile and run.

    *What you need*

    - The .NET 9 SDK (`dotnet --version`).
    - The repo cloned, with the compiler built:
      `dotnet build .\Contract.Compiler\Contract.Compiler.csproj`

    *Compiling and running*

    The CLI compiles a `.ct` source file into an IR text file (`.oir`):

    ```powershell
    dotnet run --project .\Contract.Cli\ -- path\to\file.ct
    ```

    For example, from the repo root:

    ```powershell
    dotnet run --project .\Contract.Cli\ -- .\tutorials\01-hello-world\hello.ct
    ```

    This writes `hello.oir` next to the source — the compiled intermediate
    representation. Pass `--debug` to also print the parsed AST, or run the
    compiler's own test suite with `dotnet run --project .\Contract.Cli\ --
    --test`.
  ],
)

#divider()
#linebreak()
#set heading(numbering: "1.1")
#set ref(supplement: "Section")
#set raw(lang: "text")

#linebreak()

The tour is eight short lessons, meant to be read in order — each one builds
on the last, and cross-references point both ways so you can also jump
straight to a topic.

#linebreak()

= Hello, World! <sec-hello>

Welcome to the start of the line. By the time you finish @sec-stdlib you'll
have written programs that read input, loop over data, and call the standard
library. Every lesson follows the same shape: one idea, one complete program,
one exercise — with a shaded solution if you get stuck.

The classic first program. Create a file called `hello.ct`:

```ct
Contract Program {
    static fn Main() {
        IO.Println("hello world");
    }
}
```

Compile it:

```powershell
dotnet run --project .\Contract.Cli\ -- .\hello.ct
```

You'll see `Bytecode written to: .\hello.oir`.

== What's going on?

- *`Contract Program { ... }`* — a *contract* is the top-level organizational
  unit, like a class or namespace. Every program has one (or more).
- *`static fn Main()`* — the entry point. When the program runs, execution
  starts here. It must be `static`.
- *`IO.Println(...)`* — a call into the `IO` standard library module that
  prints a line of text.

== Making it a conversation

The `IO` module also has `IO.Print` (no newline) and `IO.Readln` (read a line):

```ct
Contract Program {
    static fn Main() {
        IO.Print("What's your name? ");
        var name: string = IO.Readln();
        IO.Println("Hi, " + name + "!");
    }
}
```

== Exercise

Modify `hello.ct` so it prints two lines: `hello world` and then your name.

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Program {
      static fn Main() {
          IO.Println("hello world");
          IO.Println("your name here");
      }
  }
  ```
]

That's the whole tour in miniature. Next, @sec-vars gives the program
something to remember: variables and the types they can hold.

#linebreak()

= Variables and Types <sec-vars>

The hello-world program from @sec-hello printed text but remembered nothing.
Real programs need to store values — that's what variables are for — and
Contract, being statically typed, wants to know the *kind* of every value up
front.

Contract is statically typed. Every value has a type, and the compiler checks
that you use types consistently.

== Declaring variables

Use `let` for values that never change and `var` for values that do:

```ct
Contract Program {
    static fn Main() {
        var count: int = 0;        // mutable
        let pi: double = 3.14159;  // immutable
        count = count + 1;         // ok
        // pi = 3.0;               // ERROR: pi is immutable
    }
}
```

The syntax is `var name: type = value;`. The type annotation is required when
the compiler can't figure it out on its own (see Type inference below).

== Built-in types

#table(
  columns: (auto, auto, auto),
  table.header([*Type*], [*What it is*], [*Example literal*]),
  [`int`], [32-bit whole number], [`5`, `-42`, `0`],
  [`double`], [64-bit floating point], [`3.14`, `0.5`, `2.0`],
  [`float`], [32-bit floating point], [`1.5`],
  [`bool`], [`true` or `false`], [`true`, `false`],
  [`string`], [Text], [`"hi"`, `""`],
)

Type names are case-insensitive — `int`, `Int`, and `INT` all mean the same
thing.

== Type inference

You can leave the type off when the initializer makes it obvious:

```ct
let answer = 42;        // int
let name = "Ada";       // string
let ready = true;       // bool
let ratio = 0.5;        // double
let p = new Person();   // Person
```

If the compiler can't infer (e.g. `let x = null;`), add the annotation:

```ct
let x: string = null;
```

== Exercise

Write a program that declares `let temperature = 23.5;` and prints it. Then
declare a `var score: int = 10;`, add 5 to it, and print that too.

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Program {
      static fn Main() {
          let temperature = 23.5;
          var score: int = 10;
          score = score + 5;
          IO.Println(temperature);
          IO.Println(score);
      }
  }
  ```
]

Variables give us data; @sec-flow gives it shape. Programs that only run
top-to-bottom are limited — the next lesson adds decisions and repetition.

#linebreak()

= Control Flow <sec-flow>

Lesson @sec-vars gave us data to store; now we give it behavior. Nearly every
program asks questions, repeats work, and bails out early — Contract's
control flow is how.

Programs make decisions and repeat work. Contract has the usual tools.

== if / else

```ct
Contract Program {
    static fn Main() {
        var x: int = 7;
        if (x > 10) {
            IO.Println("big");
        } else {
            IO.Println("small");
        }
    }
}
```

== while

```ct
var i: int = 0;
while (i < 5) {
    IO.Println(i);
    i = i + 1;
}
// prints 0 1 2 3 4
```

== for

A C-style `for` loop has an initializer, a condition, and an update:

```ct
for (var i: int = 0; i < 5; i = i + 1) {
    IO.Println(i);
}
```

All three parts are optional, so `for (;;)` is an infinite loop. You can also
leave parts out:

```ct
var i: int = 0;
for (; i < 5;) {
    IO.Println(i);
    i = i + 1;
}
```

== break and continue

- `break` stops the loop immediately.
- `continue` jumps to the next iteration.

```ct
for (var i: int = 0; i < 10; i = i + 1) {
    if (i == 2) {
        continue;          // skip 2
    }
    if (i > 5) {
        break;             // stop after 5
    }
    IO.Println(i);         // prints 0 1 3 4 5
}
```

== switch

```ct
var day: int = 3;
switch (day) {
    case 1: IO.Println("Monday");
    case 2: IO.Println("Tuesday");
    case 3: IO.Println("Wednesday");
    else: IO.Println("Weekend");
}
```

`case` values must be integer literals. The `else` case catches everything
else.

== Exercise

Print the numbers 1 through 20, but:

- print `"fizz"` for multiples of 3,
- print `"buzz"` for multiples of 5,
- print `"fizzbuzz"` for multiples of both.

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Program {
      static fn Main() {
          for (var i: int = 1; i <= 20; i = i + 1) {
              if (i % 15 == 0) {
                  IO.Println("fizzbuzz");
              } else if (i % 3 == 0) {
                  IO.Println("fizz");
              } else if (i % 5 == 0) {
                  IO.Println("buzz");
              } else {
                  IO.Println(i);
              }
          }
      }
  }
  ```
]

Loops and branches are the plumbing of a program; functions are how we
package that plumbing under a name. On to @sec-fn.

#linebreak()

= Functions <sec-fn>

So far, every program has been a single list of statements inside `Main`.
The control flow from @sec-flow makes those statements powerful, but they
can't be reused. Functions fix that: named, reusable bundles of logic.

Functions bundle a piece of logic under a name so you can call it repeatedly.

== Defining a function

```ct
fn greet(name: string) {
    IO.Println("Hello, " + name + "!");
}
```

Functions live inside a `Contract` block:

```ct
Contract Program {
    fn greet(name: string) {
        IO.Println("Hello, " + name + "!");
    }

    static fn Main() {
        greet("Ada");
        greet("Grace");
    }
}
```

== Return types

Use `-> type` after the parameter list to say what the function produces:

```ct
fn add(a: int, b: int) -> int {
    return a + b;
}

fn isEven(n: int) -> bool {
    return n % 2 == 0;
}
```

```ct
static fn Main() {
    var sum: int = add(2, 3);     // 5
    var even: bool = isEven(4);   // true
    IO.Println(sum);
    IO.Println(even);
}
```

If you leave the return type off, the compiler defaults to `void` for `Main`
and `int` for other functions. If a function with a return type falls off the
end without a `return`, it implicitly returns the zero value for that type
(`0`, `0.0`, `false`, or `null`).

== Parameters

Parameters are `name: type` pairs separated by commas:

```ct
fn describe(name: string, age: int, active: bool) {
    IO.Println(name);
    IO.Println(age);
    IO.Println(active);
}
```

== Access modifiers and static

Functions default to `public`. You can mark them `private`, `protected`, or
`internal`, and `static` when they don't need an instance:

```ct
Contract Math {
    public fn add(a: int, b: int) -> int {
        return a + b;
    }

    private fn helper() {
        // only callable from inside Math
    }

    static fn twice(n: int) -> int {
        return n * 2;
    }
}
```

== Exercise

Write a function `max(a: int, b: int) -> int` that returns the larger of two
numbers, then print `max(4, 9)` from `Main`.

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Program {
      fn max(a: int, b: int) -> int {
          if (a > b) {
              return a;
          }
          return b;
      }

      static fn Main() {
          var result: int = max(4, 9);
          IO.Println(result);  // 9
      }
  }
  ```
]

Functions so far shuttle single values around. Real programs deal in shapes —
a point, a counter, a rectangle. @sec-structs groups values into structs and
classes.

#linebreak()

= Structs, Classes, and Custom Types <sec-structs>

In @sec-fn, functions passed individual values — an `int` here, a `string`
there. But real values come in shapes: a point *is* its x and y together, a
counter *is* a running total plus the operations on it. Structs and classes
bundle related values — and, for classes, the behavior that goes with them.

Group related values together with structs and classes.

== Classes (contracts with fields)

A `Contract` that declares instance fields acts as a class. Create an instance
with `new ContractName()` and read/write its fields with `.`:

```ct
Contract Point {
    x: int;
    y: int;
}

Contract Program {
    static fn Main() {
        var p: Point = new Point();
        p.x = 3;
        p.y = 4;
        IO.Println(p.x);
        IO.Println(p.y);
    }
}
```

Fields are declared as `name: type;` directly in the contract body. Each
instance of the contract gets its own copy of the fields.

== Structs inside a contract

You can also declare a `struct` directly inside a `Contract`:

```ct
Contract Geometry {
    struct Point {
        x: int;
        y: int;
    }

    static fn Main() {
        var p: Point = new Point();
        p.x = 1;
        p.y = 2;
    }
}
```

== Type inference with new

`new` expressions infer their own type:

```ct
var p = new Point();   // p: Point
```

== Constructors

A contract can declare a `constructor` that runs when an instance is created:

```ct
Contract Counter {
    count: int;

    constructor() {
        this.count = 0;
        IO.Println("Counter created");
    }

    static fn Main() {
        var c: Counter = new Counter();
    }
}
```

Inside the constructor (and any instance method), `this` refers to the current
instance, so `this.count` initializes the field.

== Instance methods

When a contract has fields, its non-static member functions become instance
methods: they receive the receiver implicitly and can be called with `.`:

```ct
Contract Counter {
    count: int;

    constructor() {
        this.count = 0;
    }

    fn increment() {
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

== Exercise

Define a `Rectangle` class with `width` and `height` fields and an `area()`
instance method. In `Main`, create one, set its dimensions to 5 and 3, and
print `area()`.

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Rectangle {
      width: int;
      height: int;

      fn area() -> int {
          return this.width * this.height;
      }
  }

  Contract Program {
      static fn Main() {
          var r: Rectangle = new Rectangle();
          r.width = 5;
          r.height = 3;
          IO.Println(r.area());  // 15
      }
  }
  ```
]

Classes attach behavior to data. @sec-fnprog turns the tables: it treats
behavior itself — functions — as a value you can pass around.

#linebreak()

= Functional Programming <sec-fnprog>

The classes of @sec-structs package data and behavior into one unit.
Functional programming pulls in the opposite direction: it makes behavior a
first-class value that can be stored, passed, and composed — with lambdas and
the pipe operator.

Contract has lambdas and a pipe operator for a functional style.

== Lambdas with `fun`

A lambda is an anonymous function. The syntax is
`fun parameter -> expression`:

```ct
let inc = fun x -> x + 1;
```

The body is a single expression; the result is the lambda's return value.

```ct
Contract Program {
    static fn Main() {
        let inc = fun x -> x + 1;
        IO.Println(inc(5));   // 6
    }
}
```

== The pipe operator `|>`

`left |> right` passes the left value as the argument to the right function:

```ct
let doubled = 5 |> double;   // double(5)
```

This chains nicely:

```ct
fn addOne(x: int) -> int {
    return x + 1;
}

fn square(x: int) -> int {
    return x * x;
}

static fn Main() {
    let result = 3 |> addOne |> square;  // (3 + 1)^2 = 16
    IO.Println(result);
}
```

Pipes work with lambdas too:

```ct
let result = 10 |> fun x -> x + 5;  // 15
```

== Why bother?

Pipes read left-to-right, matching how you'd describe the data flow:
"take 3, add one, then square it." Nested calls read inside-out:

```ct
// Compare:
let a = square(addOne(3));      // 16, inside-out
let b = 3 |> addOne |> square;  // 16, left-to-right
```

== Exercise

Write a function `halve(x: int) -> int` returning `x / 2`. In `Main`, pipe
`100` through `halve` twice and print the result (should be 25).

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Program {
      fn halve(x: int) -> int {
          return x / 2;
      }

      static fn Main() {
          let result = 100 |> halve |> halve;
          IO.Println(result);  // 25
      }
  }
  ```
]

Lambdas and pipes work on single values. Real programs handle *many* values
at once — @sec-arrays is about collections, plus the logic operators that
filter them.

#linebreak()

= Arrays and Logic <sec-arrays>

Everything so far handled one value at a time. Most programs need groups of
values — a list of scores, a grid of cells. Arrays are the simplest such
collection, and they pair naturally with the `for` loops from @sec-flow.

Arrays store a fixed number of values of one type. Combine them with the
logical operators for real programs.

== Declaring arrays

The type is `elementType[]`:

```ct
var scores: int[] = [90, 85, 100];
var names: string[] = ["Ada", "Grace"];
```

You can also create an empty array of a known size with `new Type[size]`:

```ct
var grid: int[] = new int[9];
```

The elements start out as the zero value of the element type (`0` for ints,
`null` for strings, `false` for bools).

== Reading and writing elements

Indexes start at 0. Read with `arr[i]`, write with `arr[i] = value`:

```ct
var first: int = scores[0];  // 90
scores[1] = 95;              // was 85, now 95
```

== Array length

`.Length` gives the number of elements:

```ct
var count: int = scores.Length;  // 3
```

== Looping over an array

`for` plus `.Length` is the classic combo:

```ct
Contract Program {
    static fn Main() {
        var scores: int[] = [90, 85, 100];
        var total: int = 0;

        for (var i: int = 0; i < scores.Length; i = i + 1) {
            total += scores[i];
        }

        IO.Println(total);  // 275
    }
}
```

== Logical operators

`&&` (and), `||` (or), and `!` (not) work on `bool` values:

```ct
var isAdult: bool = age >= 18;
var hasId: bool = true;
var allowed: bool = isAdult && hasId;
var notAdult: bool = !isAdult;
```

> Note: in v1, `&&` and `||` always evaluate both sides — they don't
> short-circuit.

== Type inference

Array types are inferred from literals and `new Type[size]`:

```ct
let nums = [1, 2, 3];      // nums: int[]
let sizes = new int[8];    // sizes: int[]
```

== Exercise

Write a program that finds the largest value in `[3, 9, 4, 12, 7]` and prints
it (should be 12).

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Program {
      static fn Main() {
          var values: int[] = [3, 9, 4, 12, 7];
          var largest: int = values[0];

          for (var i: int = 1; i < values.Length; i = i + 1) {
              if (values[i] > largest) {
                  largest = values[i];
              }
          }

          IO.Println(largest);  // 12
      }
  }
  ```
]

Arrays cover the data side. @sec-stdlib rounds out the language with the
utilities every real program needs: strings, math, conversion, randomness.

#linebreak()

= The Standard Library <sec-stdlib>

You now have the whole core language — programs, data, control flow,
functions, classes, lambdas, arrays. What's left is the toolbox that ships
with it: string handling, math, conversion, and randomness, all callable from
anywhere without an import.

Contract ships with several built-in modules you can call without any imports.

== String

```ct
Contract Program {
    static fn Main() {
        var name: string = "ada";
        IO.Println(String.ToUpper(name));      // ADA
        IO.Println(String.Length(name));       // 3
        IO.Println(String.Substring(name, 0, 2)); // ad
        IO.Println(String.StartsWith(name, "a")); // true
        IO.Println(String.Replace(name, "a", "o")); // odo
    }
}
```

String concatenation: `+` (and `+=`) on strings calls `String.Concat` for you,
so `"Hello, " + name` and `String.Concat("Hello, ", name)` are equivalent.

== Math

```ct
Contract Program {
    static fn Main() {
        IO.Println(Math.Abs(-7));   // 7
        IO.Println(Math.Min(3, 9)); // 3
        IO.Println(Math.Max(3, 9)); // 9
    }
}
```

Float math is available too: `Math.Sqrt`, `Math.Pow`, `Math.Floor`,
`Math.Ceiling`, `Math.Round`, `Math.Sin`, `Math.Cos`, `Math.Tan`, `Math.Log`.

== Convert

```ct
Contract Program {
    static fn Main() {
        var n: int = Convert.ToInt32("42");   // 42
        var s: string = Convert.ToString(7);  // "7"
        var b: bool = Convert.ToBool("true"); // true
    }
}
```

== Random

```ct
Contract Program {
    static fn Main() {
        var die: int = Random.NextInt(6) + 1;  // 1..6
        IO.Println(die);
    }
}
```

== String switch

`switch` accepts string case values:

```ct
Contract Program {
    static fn Main() {
        var cmd: string = IO.Readln();
        switch (cmd) {
            case "start": IO.Println("starting");
            case "stop": IO.Println("stopping");
            else: IO.Println("unknown command");
        }
    }
}
```

== Exercise

Write a program that reads a line, trims it, converts it to lowercase, and
prints `"yes"` if it equals `"go"`.

#block(
  width: 100%,
  fill: rgb("#f2f2f2"),
  inset: 8pt,
  radius: 4pt,
)[
  *Solution*

  ```ct
  Contract Program {
      static fn Main() {
          var input: string = IO.Readln();
          var cleaned: string = String.ToLower(String.Trim(input));
          switch (cleaned) {
              case "go": IO.Println("yes");
              else: IO.Println("no");
          }
      }
  }
  ```
]

That's the full tour — every concept in the language, from `hello world` to
the standard library, in eight programs. @sec-next points you at the deeper
reference material.

#linebreak()

= Where to Go Next <sec-next>

- The full language definition lives in `docs/CONTRACT_SPEC.typ` — the
  complete Contract v1.0 specification.
- Every example here is a complete program: save it as `name.ct` and run
  `dotnet run --project .\Contract.Cli\ -- name.ct` to compile and execute.
- Sample solutions for every exercise live next to each lesson in
  `docs/tutorials/`.

That's the whole tour. Copy any example, change it, break it, and see what
the compiler says — that's how the language sticks.
