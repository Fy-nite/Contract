# Chapter 5: Structs and Classes

> **You'll learn:**
>
> - how to group related values with `struct`,
> - how contracts with fields act as classes, with `new`, constructors, and `this`,
> - how to pass structs to native C libraries — and the one pitfall that
>   produces mysteriously wrong results.

Group related values together with structs and classes.

## Classes (contracts with fields)

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

## Structs inside a contract

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

> **Note** — a nested struct's full name is `Namespace.Name`, **not**
> `Contract.Name`. If the namespace and contract happen to share a name
> (`namespace Raylib; contract Raylib { struct Color }`), the wire name is
> `Raylib.Color` — call it with `new Raylib.Color(...)`, never
> `new Raylib.Raylib.Color(...)`.

## Type inference with new

`new` expressions infer their own type:

```ct
var p = new Point();   // p: Point
```

## Constructors

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

## Instance methods

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

## Structs at a glance: construction with arguments

Structs can be constructed positionally — `new Type(a, b, c)` assigns the
arguments to the fields **in declaration order**:

```ct
struct Color {
    r: byte;
    g: byte;
    b: byte;
    a: byte;
}

var white = new Color(255, 255, 255, 255);  // r=255, g=255, b=255, a=255
```

> **Warning** — the compiler does **not** check that the number of arguments
> matches the number of fields; extras are silently ignored. It will not save
> you from `new Color(255, 255, 255)` leaving `a` at its default.

## Native interop: struct layout must match the C ABI

Contract can call native C libraries through the `<DllImport("lib.dll")>`
facade. When a struct crosses that boundary **by value**, the compiler packs
it into C layout using exactly the fields you declared — no more, no less.

> **Warning** — the compiler cannot see the C side. If your struct declares
> fewer fields than the native one, the native function reads the missing
> bytes from **uninitialized memory**, and you get garbage — scrambled
> colors, random alpha, crash-adjacent values. This is the #1 source of
> "why is my color blue when I said yellow?" bugs.

raylib's `Color` is a famous example. The C header says:

```c
typedef struct Color {
    unsigned char r, g, b, a;   // four bytes!
} Color;
```

The correct Contract declaration mirrors it **field for field**:

```ct
namespace Raylib;

<DllImport("raylib")>
Contract Raylib {
    public struct Color {
        r: byte;
        g: byte;
        b: byte;
        a: byte;
    }

    static fn DrawText(text: string, posX: int, posY: int, fontSize: int, color: Color) -> void {}
}

Contract Program {
    static fn Main() {
        let Yellow = new Raylib.Color(255, 255, 0, 255);  // yellow, fully opaque
        Raylib.DrawText("hello", 10, 10, 20, Yellow);
    }
}
```

A struct with only `r, g, b` compiles fine but renders as garbage — the
native function reads a fourth byte that was never written. Declare **every**
field the native struct has, in the same order, with the same widths.

> **Tip** — rules of thumb for interop structs:
>
> - Copy the C declaration **exactly**: same fields, same order, same widths.
> - `byte` ↔ C `unsigned char`/`uint8_t`, `short`/`ushort` ↔ C
>   `short`/`unsigned short`, `int`/`uint` ↔ C `int`/`unsigned int`,
>   `float` ↔ C `float`, `double` ↔ C `double`.
> - Check the header when in doubt — the `.h` file is the contract.

## Summary

- Contracts with fields act as classes: `new Type()`, `this`, constructors.
- Non-static member functions are instance methods; `static fn` are not.
- Nested structs resolve as `Namespace.Name`, not `Contract.Name`.
- `new Type(a, b, c)` assigns struct fields positionally; the argument count
  is **not** checked.
- Structs passed to native code must match the C layout **exactly** — a
  missing field means uninitialized memory on the C side.

## Exercise

Define a `Rectangle` class with `width` and `height` fields and an `area()`
instance method. In `Main`, create one, set its dimensions to 5 and 3, and
print `area()`.

<details>
<summary>Solution</summary>

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

</details>
