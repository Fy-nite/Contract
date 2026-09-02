# Zero-copy native interop (ManagedPtr <-> C# host binding)

This example proves that a Contract `ManagedPtr<T>` buffer can be handed to a
custom C# host binding over the native address — no copying, no compiler
changes.

## The mechanism

- `ManagedPtr<T>` is backed by a native (`Marshal.AllocHGlobal`) buffer that
  lives in the runtime's `PtrHost` binding.
- `m.Address()` — and the language operator `&m`, which lowers to it — returns
  the buffer's **real native address** as a `long`.
- A Contract facade (`<NativeBinding("NativeAudio")>`) declares host methods
  that take that address. The C# side re-creates the pointer as an `IntPtr`
  and spans it as `ReadOnlySpan<float>`/`Span<float>`, reading and writing the
  **same** memory the `.ct` side owns.

Because the address is real native memory, the span created on the C# side and
the buffer on the `.ct` side are the same bytes. `ManagedPtr<T>` now supports
typed element types (`<int>`, `<long>`, `<float>`, `<double>`), so audio/numeric
streaming can use `ManagedPtr<float>` directly instead of reinterpreting a
4-byte `int` as a float (see `OwnAudioBindings.ct`). The example still uses
`ManagedPtr<int>` to show the byte-level equivalence: a 4-byte `int` element is
bit-identical to a 4-byte `float`.

## Layout

| Path | Purpose |
|---|---|
| `NativeAudioHost/NativeAudioHostBinding.cs` | C# `[ClassBinding("NativeAudio")]` host: gain, RMS, sine fill, byte peek, address echo |
| `NativeInterop.ct` | Contract program that allocates a `ManagedPtr<int>`, passes `&m` to the host, and reads results back |
| `build.ps1` | Build the host DLL and run the example |

## Run

```powershell
.\build.ps1
```

Or by hand:

```powershell
dotnet build NativeAudioHost\NativeAudioHost.csproj -c Debug
..\..\Contract.Cli\bin\Debug\net10.0\Contract.Cli.exe `
  --bind NativeAudioHost\bin\Debug\net10.0\NativeAudioHost.dll `
  NativeInterop.ct
```

Expected output ends with:

```
host peak after gain   => 6
m[0] now holds float 3 => 1077936128 (was 1.5f)
sine rms               => 0.66
```

`m[0] => 1077936128` (0x40400000 = `3.0f`, was `1.5f`) is the proof: the host
wrote through the native pointer and the `.ct` side read it back.

## Lifetime

Lifetime stays on the Contract side — the host must not touch the address after
the `.ct` calls `Free()` (the runtime zeroes it). `ApplyGain`/`Rms`/`FillSine`
are bounds-safe only if `length <= Count() * elementSize`.