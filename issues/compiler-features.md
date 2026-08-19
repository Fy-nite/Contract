# Language & Compiler Feature Requests

These are features that the language itself (not just the stdlib) is missing.

---

## [Compiler] Add `<<` (left shift) operator

The language supports `>>` (right shift) but lacks `<<` (left shift). This is an asymmetry in the operator set.

```
var x: int = 1;
var y: int = x << 3;   // y = 8
```

---

## [Compiler] Add `&` (bitwise AND), `|` (bitwise OR), `^` (bitwise XOR) operators

Bitwise operations are fundamental for low-level work. Currently requires calling `BitOps` module functions (once that module exists).

```
var flags: int = 0b1010 | 0b0101;   // flags = 0b1111
var mask: int = flags & 0b0011;     // mask = 0b0011
```

---

## [Compiler] Add `~` (bitwise NOT) operator

Unary bitwise complement.

```
var x: int = 0;
var y: int = ~x;   // y = -1 (all bits set)
```

---

## [Compiler] Add `for (key : dict)` iteration over dictionaries

Foreach currently only works with arrays. Adding dictionary iteration would be natural.

```
for (key : myDict) {
    IO.Println(key + " = " + Dict.Get(myDict, key));
}
```

---

## [Compiler] Add `for (item : list)` iteration over List objects

Foreach works with arrays but not with `List<T>` objects.

```
for (item : myList) {
    IO.Println(item);
}
```

---

## [Compiler] Add `switch` on strings

Check if `switch` already supports string patterns. If not, add it:

```
switch (command) {
    case "start": StartServer();
    case "stop": StopServer();
    else: IO.Println("Unknown command");
}
```

---

## [Compiler] Add `as` type cast operator

Safe type casting with null result instead of runtime error.

```
var obj: object = GetSomething();
var num: int = obj as int;   // null if obj is not int
```

---

## [Compiler] Add `is` type check operator

Check if a value is of a certain type.

```
var obj: object = GetSomething();
if (obj is int) {
    IO.Println("It is an int");
}
```

---

## [Compiler] Add `null` coalescing operator `??`

Provide a default value when something is null.

```
var name: string = GetName() ?? "Anonymous";
```

---

## [Compiler] Add `try` / `catch` exception handling

Currently no way to catch errors. Any thrown exception crashes the program.

```
try {
    var data = File.ReadAllText("config.json");
} catch (e) {
    IO.Println("Failed to read config: " + e);
}
```
