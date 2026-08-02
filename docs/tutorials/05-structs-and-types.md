# Tutorial 5: Structs, Classes, and Custom Types

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
