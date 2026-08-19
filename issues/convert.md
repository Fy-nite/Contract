# Convert Module - Missing Features

## [Convert] Add `ToOctal` / `FromOctal`

Convert integers to/from octal (base-8) string representation.

File permissions (`chmod`-style), low-level data work. `ToHexString` / `FromHexString` exist but octal is missing.

```
Convert.ToOctal(int value) -> string
Convert.FromOctal(string value) -> int
```

---

## [Convert] Add `ToBinary` / `FromBinary`

Convert integers to/from binary string representation.

Bitmask visualization, debugging bitwise operations, educational tools.

```
Convert.ToBinary(int value) -> string
Convert.FromBinary(string value) -> int
```

---

## [Convert] Add `ToHexStringPadded` - hex with zero-padding

`Convert.ToHexStringPadded(value, digits) -> string` - hex string padded with leading zeros to `digits` characters.

Color codes (`#FF00AA`), memory addresses, fixed-width hex display. `ToHexString` exists but has no padding option.

```
Convert.ToHexStringPadded(int value, int digits) -> string
```

---

## [Convert] Add `ToChar` / `FromChar` - char<->int conversion

Convert between integer and single-character string.

ASCII manipulation, character processing. `String.CharAt` returns a string but there's no way to convert an int to its character equivalent.

```
Convert.ToChar(int ascii) -> string
Convert.FromChar(string ch) -> int
```

---

## [Convert] Add `ToByteArray` / `FromByteArray` - byte array conversions

Convert between strings and byte arrays for binary data processing.

Network protocols, file I/O with binary data, cryptographic operations.

```
Convert.ToByteArray(string str) -> int[]
Convert.FromByteArray(int[] bytes) -> string
Convert.ToByteArrayHex(int[] bytes) -> string     // hex dump
Convert.FromByteArrayHex(string hex) -> int[]      // parse hex dump
```

---

## [Convert] Add `TryToInt64` / `TryToDouble`

Missing Try* variants for `long` and `double` parsing.

Safe parsing without exceptions. `TryToInt32`, `TryToFloat32`, `TryToBool` exist but `long` and `double` are missing.

```
Convert.TryToInt64(string value) -> bool
Convert.TryToDouble(string value) -> bool
```

### Note

This may require tuple return or out-parameter support in the language first. Alternatively, could return `(-1, false)` style tuples where the first element is the parsed value and the second indicates success.

---

## [Convert] Add `ToScientific` - scientific notation

`Convert.ToScientific(value, decimals) -> string` - format a double in scientific notation.

Displaying very large or very small numbers. No way to do this currently.

```
Convert.ToScientific(double value, int decimals) -> string
```

---

## [Convert] Fix `ToStringD2` naming

`ToStringD2(double)` is unclear - what does the `2` mean? If it formats to 2 decimal places, it should be renamed:

```
Convert.ToStringFixed(double value, int decimals) -> string
```

Or if it's a different formatting mode, document what it does.

---

## [Convert] Standardize naming conventions

The current naming uses inconsistent suffixes:

| Current | Issue |
|---------|-------|
| `ToString(int)` | No suffix |
| `ToStringF(float)` | `F` suffix |
| `ToStringD(double)` | `D` suffix |
| `ToStringD2(double)` | Unclear `D2` |
| `ToStringL(long)` | `L` suffix |
| `ToStringB(bool)` | `B` suffix |

Consider standardizing to either:
- Full type names: `ToStringFloat`, `ToStringDouble`, `ToStringLong`, `ToStringBool`
- Or consistent suffixes with documentation
