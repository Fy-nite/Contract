# New Module: BitOps - Bitwise Operations

## [New Module] `BitOps` - bitwise operations

The language supports `>>` (right shift) but lacks `<<` (left shift) as an operator. There's also no stdlib for bitwise logic. A `BitOps` module fills this gap.

**Use case**: Bitmask manipulation, flag checking, low-level data processing, networking, protocol parsing.

### Proposed Signatures

```
BitOps.And(int a, int b) -> int
BitOps.Or(int a, int b) -> int
BitOps.Xor(int a, int b) -> int
BitOps.Not(int value) -> int
BitOps.ShiftLeft(int value, int bits) -> int
BitOps.ShiftRight(int value, int bits) -> int
BitOps.GetBit(int value, int bit) -> bool
BitOps.SetBit(int value, int bit, bool on) -> int
BitOps.PopCount(int value) -> int          // hamming weight / number of set bits
BitOps.TrailingZeros(int value) -> int
BitOps.LeadingZeros(int value) -> int
BitOps.RotateLeft(int value, int bits) -> int
BitOps.RotateRight(int value, int bits) -> int
```

### Examples

```
var flags: int = 0;
flags = BitOps.SetBit(flags, 3, true);   // set bit 3
flags = BitOps.SetBit(flags, 7, true);   // set bit 7
BitOps.GetBit(flags, 3)                  // true
BitOps.PopCount(flags)                   // 2

var mask: int = BitOps.And(0xFF00, 0x0FF0);  // 0x0F00
var combined: int = BitOps.Or(0xA, 0x5);     // 0xF
```

### Note

Consider also adding `<<` as a language operator to match `>>`. The bitwise operator set is currently asymmetric.
