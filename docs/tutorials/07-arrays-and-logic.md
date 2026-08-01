# Tutorial 7: Arrays and Logic

Arrays store a fixed number of values of one type. Combine them with the
logical operators for real programs.

## Declaring arrays

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

## Reading and writing elements

Indexes start at 0. Read with `arr[i]`, write with `arr[i] = value`:

```ct
var first: int = scores[0];  // 90
scores[1] = 95;              // was 85, now 95
```

## Array length

`.Length` gives the number of elements:

```ct
var count: int = scores.Length;  // 3
```

## Looping over an array

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

## Logical operators

`&&` (and), `||` (or), and `!` (not) work on `bool` values:

```ct
var isAdult: bool = age >= 18;
var hasId: bool = true;
var allowed: bool = isAdult && hasId;
var notAdult: bool = !isAdult;
```

> Note: in v1, `&&` and `||` always evaluate both sides — they don't short-circuit.

## Type inference

Array types are inferred from literals and `new Type[size]`:

```ct
let nums = [1, 2, 3];      // nums: int[]
let sizes = new int[8];    // sizes: int[]
```

## Exercise

Write a program that finds the largest value in `[3, 9, 4, 12, 7]` and prints
it (should be 12).

<details>
<summary>Solution</summary>

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

</details>
