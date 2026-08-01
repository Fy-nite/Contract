# Tutorial 5: Structs and Custom Types

Group related values together with structs and the `Types` block.

## The `Types` block

`Types` blocks define reusable shapes at the top level of a file:

```ct
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

Each `type` declares fields as `name: type;`. Create an instance with
`new TypeName()` and read/write fields with `.`:

```ct
var p: Point = new Point();
p.x = 10;
p.y = 20;
var sum: int = p.x + p.y;  // 30
```

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
    constructor(start: int) {
        IO.Println("Counter started at " + start);
    }

    static fn Main() {
        var c: Counter = new Counter();
    }
}
```

## Exercise

Define a `type Rectangle` with `width` and `height` fields. In `Main`, create
one, set its dimensions to 5 and 3, and print `width * height`.

<details>
<summary>Solution</summary>

```ct
Types {
    type Rectangle {
        width: int;
        height: int;
    }
}

Contract Program {
    static fn Main() {
        var r: Rectangle = new Rectangle();
        r.width = 5;
        r.height = 3;
        IO.Println(r.width * r.height);  // 15
    }
}
```

</details>
