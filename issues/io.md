# IO Module - Missing Features

## [IO] Add `Println()` with no args - empty newline

`IO.Println()` with no arguments (empty newline). Currently requires an argument.

Empty newlines are common in formatting and output separation.

```
IO.Println() -> void
```

---

## [IO] Add `ReadBytes` / `WriteBytes` - binary I/O

Binary I/O at the IO module level.

Quick binary read/write without going through File module.

```
IO.ReadBytes(string prompt) -> int[]
IO.WriteBytes(int[] data) -> void
```

---

## [IO] Add `PrintF` / `PrintlnF` - formatted output

Formatted print functions.

Building formatted console output without string interpolation.

```
IO.PrintF(string format, ...args) -> void
IO.PrintlnF(string format, ...args) -> void
```

---

## [IO] Add `ReadKey` - read a single keypress

`IO.ReadKey() -> string` - read a single keypress without requiring enter.

Console UI, menu navigation, input prompts. `Console.ReadKey` exists but `IO` doesn't have it.

```
IO.ReadKey() -> string
```

---

## [IO] Add `Input` - read with a prompt

`IO.Input(prompt) -> string` - display a prompt and read a line.

User interaction. Currently requires `IO.Print(prompt)` + `IO.Readln("")` (two calls, no flush guarantee).

```
IO.Input(string prompt) -> string
```
