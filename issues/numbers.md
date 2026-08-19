# Numbers / Math Module - Missing Features

## [Numbers] Add `AbsD` - absolute value for double

`Numbers.AbsD(double) -> double` - absolute value for doubles. `Abs` (int) and `AbsF` (float) exist but double is missing.

```
Numbers.AbsD(double value) -> double
```

---

## [Numbers] Add `MinD` / `MaxD` - min/max for double

Double versions of Min/Max. Only `int` and `float` versions exist.

```
Numbers.MinD(double a, double b) -> double
Numbers.MaxD(double a, double b) -> double
```

---

## [Numbers] Add `GCD` / `LCM` - greatest common divisor / least common multiple

Classic number theory functions.

Fraction simplification, scheduling, modular arithmetic.

```
Numbers.GCD(int a, int b) -> int
Numbers.LCM(int a, int b) -> int
```

---

## [Numbers] Add `Factorial`

`Numbers.Factorial(int n) -> int` - compute n!

Combinatorics, probability calculations.

```
Numbers.Factorial(int n) -> int
```

---

## [Numbers] Add `DivMod` - integer division with remainder

`Numbers.DivMod(int dividend, int divisor) -> (int, int)` - return both quotient and remainder in one call.

Avoids redundant division + modulo. Currently requires two separate operations.

```
Numbers.DivMod(int dividend, int divisor) -> (int, int)
```

---

## [Numbers] Add `InverseLerp` / `Remap` / `Smoothstep` / float `Clamp`

Common interpolation/math utilities. `Lerp` and `Clamp(double)` exist but these are missing.

Game dev, animations, data normalization.

```
Numbers.InverseLerp(double a, double b, double value) -> double
Numbers.Remap(double value, double fromMin, double fromMax, double toMin, double toMax) -> double
Numbers.Smoothstep(double edge0, double edge1, double x) -> double
Numbers.LerpF(float a, float b, float t) -> float
Numbers.ClampF(float value, float min, float max) -> float
```

---

## [Numbers] Add `PowD` / `SqrtD` - double precision power and sqrt

Double-precision versions of power and square root. `Pow(float, float)` and `Sqrt(float)` exist but have float precision.

```
Numbers.PowD(double x, double y) -> double
Numbers.SqrtD(double value) -> double
```

---

## [Numbers] Add `IsPowerOfTwo` / `NextPowerOfTwo` / `PrevPowerOfTwo`

Bit-manipulation utilities for power-of-two checks.

Memory alignment, hash table sizing, bitfield work.

```
Numbers.IsPowerOfTwo(int value) -> bool
Numbers.NextPowerOfTwo(int value) -> int
Numbers.PrevPowerOfTwo(int value) -> int
```

---

## [Numbers] Add `CountDigits` / `DigitAt`

Digit-level operations on integers.

Number formatting, digit extraction, digit counting.

```
Numbers.CountDigits(int value) -> int
Numbers.DigitAt(int value, int position) -> int   // 0 = ones, 1 = tens, etc.
```

---

## [Numbers] Add `PingPong` / `Wrap` - value cycling

`PingPong(value, max)` bounces a value back and forth within [0, max]. `Wrap(value, max)` wraps a value cyclically.

Animations, looping indices, game logic.

```
Numbers.PingPong(double value, double max) -> double
Numbers.Wrap(double value, double min, double max) -> double
Numbers.WrapInt(int value, int min, int max) -> int
```

---

## [Numbers] Add `Avg` - average of multiple values

`Numbers.Avg(values...) -> double` - average of a variadic set of numbers.

Quick averages without array construction.

```
Numbers.Avg(double a, double b) -> double
Numbers.Avg3(double a, double b, double c) -> double
```

---

## [Numbers] Clarify `Log` naming - add `LogN`

`Numbers.Log(float)` wraps `Math.Log` which is natural log (base e), but the name is ambiguous alongside `Log10` and `Log2`.

Either rename `Log` to `LogN` or add `LogN` as an alias:

```
Numbers.LogN(double value) -> double   // natural log (base e)
```

---

## [Numbers] Naming inconsistency across the module

The module mixes `int`-specific, `float`-specific, and `double`-specific functions inconsistently:

| Current | Issue |
|---------|-------|
| `Abs(int)` | int only |
| `AbsF(float)` | float only, no double version |
| `Sqrt(float)` | float only, no double version |
| `Pow(float, float)` | float only |
| `Clamp(int, int, int)` | int only |
| `Clamp(double, double, double)` | double, but no float version |
| `Lerp(double, double, double)` | double only |

Consider adding a consistent set of `F`/`D` variants for all functions, or document the precision expectations.
