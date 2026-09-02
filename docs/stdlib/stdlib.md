# ObjektRT.Stdlib

This document defines what ObjektRT's Stdlib is alongside the main document
(ObjektRT-Stdlib.typ).

## What is the stdlib?

ObjektRT is a spec for a runtime, and being a runtime, it needs a library that
is, as marked, *standard*.

Of course, languages can package their own library modules inside their
`<name>.objbin` files (name change pending). However, by default ObjektRT
comes with a prepackaged standard library, turned on by default. These can be
turned off via a config option in the `manifest.json` file in the zip binary
files (the `.objbin`).

The stdlib is generic: it contains no language-specific code. Languages (like
Contract) bind it under their own names — for example `IO.Println` may map to
`ObjektRT.Stdlib.System.IO.Println` — and may register it under short names,
qualified names, or namespace imports as they prefer.

## Module layout

The stdlib is organized into namespaces:

| Namespace | Modules |
|---|---|
| `ObjektRT.Stdlib.System` | `IO`, `String`, `Convert`, `Random`, `File`, `Environment`, `GC`, `Debug`, `Time` |
| `ObjektRT.Stdlib.Math` | `Numbers` |
| `ObjektRT.Stdlib.Threading` | `Thread` |
| `ObjektRT.Stdlib.Generics` | `Array`, `List`, `Dict` |
| `ObjektRT.Stdlib.Memory` | `PtrHost` (managed-pointer host binding) |

## Threads

Threading is the one area of the stdlib with a lifecycle, and it follows C#'s
model: **a thread is a value**. You create one from a delegate, hold it in a
variable, start it explicitly, wait on it, and poll it:

```text
var t = Thread.Create(work);   // create — nothing runs yet
Thread.Start(t);               // start explicitly
Thread.Join(t);                // block until it finishes
Thread.IsAlive(t);             // true while it's running
```

`Thread.Create(delegate)` returns an opaque handle. The delegate runs on a
background thread that shares the runtime's state, so closures captured by
the delegate are valid on the new thread. Capture is by reference (C#-style):
a variable captured from the creating scope is shared storage, so writes on
the thread are visible back in the creating scope. A thread handle may be
started exactly once; `Join` on an unstarted thread is an error.

For fire-and-forget work there is also `Thread.Spawn(delegate)`, which creates
a background thread and runs it immediately without returning a handle.
`Thread.Sleep(ms)` pauses the calling thread.

## Module reference

### System.IO

Console input/output.

| Method | Description |
|---|---|
| `Print(contents)` | Writes `contents` to stdout without a trailing newline |
| `Println(contents)` | Writes `contents` to stdout followed by a newline |
| `Readln()` | Reads a line from stdin (empty string on EOF) |

### System.String

String helpers. A language's string `+` operator typically lowers to
`Concat`.

| Method | Description |
|---|---|
| `Length(str)` | Number of characters |
| `Concat(a, b)` | Concatenates two strings |
| `Substring(str, start, length)` | Substring by offset and length |
| `IndexOf(str, sub)` | First index of `sub`, or -1 |
| `StartsWith(str, prefix)` / `EndsWith(str, suffix)` | Prefix/suffix test |
| `Trim(str)` | Trims leading/trailing whitespace |
| `ToUpper(str)` / `ToLower(str)` | Case conversion |
| `Replace(str, old, new)` | Replace all occurrences |
| `Split(str, separator)` | Split into an array of strings |

### System.Convert

Type conversion.

| Method | Description |
|---|---|
| `ToInt32(string)` / `ToFloat32(string)` / `ToBool(string)` | Parse |
| `ToString(int)` / `ToStringF(float)` / `ToStringB(bool)` | Format |
| `ToInt32F(float)` / `ToFloat32I(int)` | Truncate / widen |

### System.Random

| Method | Description |
|---|---|
| `NextInt(max)` | Random int in `[0, max)` |
| `NextFloat()` | Random float in `[0.0, 1.0)` |

### System.File

| Method | Description |
|---|---|
| `ReadAllText(path)` / `WriteAllText(path, contents)` | Text read/write |
| `ReadAllLines(path)` | Lines as an array of strings |
| `Exists(path)` | File exists check |
| `Copy(src, dst)` / `Move(src, dst)` / `Delete(path)` | Filesystem ops |

### System.Environment

| Method | Description |
|---|---|
| `GetEnv(name)` | Environment variable value, or "" when unset |
| `Exit(code)` | Terminates the process |

### System.GC

| Method | Description |
|---|---|
| `Collect()` | Requests a garbage collection |

### System.Debug

| Method | Description |
|---|---|
| `Assert(condition, message)` | Fails with `message` when false |

### System.Time

| Method | Description |
|---|---|
| `Now()` | Current UTC time as Unix milliseconds |
| `Format(timestamp, format)` | Formats Unix milliseconds with a .NET date/time format string |

### Math.Numbers

| Method | Description |
|---|---|
| `Abs(int)` / `AbsF(float)` | Absolute value |
| `Sqrt(float)` | Square root |
| `Min(a, b)` / `Max(a, b)` | Integer min/max |
| `MinF(a, b)` / `MaxF(a, b)` | Float min/max |
| `Pow(x, y)` | Power |
| `Floor` / `Ceiling` / `Round(float)` | Rounding |
| `Sin` / `Cos` / `Tan(float)` | Trigonometry |
| `Log` / `Log10` / `Exp(float)` | Exponentials/logarithms |

### Threading.Thread

See [Threads](#threads).

| Method | Description |
|---|---|
| `Create(delegate)` | Returns a thread handle (nothing runs yet) |
| `Start(handle)` | Starts the thread's delegate on a background thread |
| `Join(handle)` | Blocks until the thread finishes |
| `IsAlive(handle)` | True while the thread is running |
| `Spawn(delegate)` | Fire-and-forget: run immediately, no handle |
| `Sleep(ms)` | Pauses the calling thread |

### Generics.Array

Array helpers over object-backed arrays.

| Method | Description |
|---|---|
| `Length(arr)` | Element count |
| `Get(arr, index)` | Element at index |
| `Set(arr, index, value)` | Write element |
| `Join(arr, separator)` | String-join an array of strings |

### Generics.List

Object-backed list.

| Method | Description |
|---|---|
| `Create()` | New empty list |
| `Add(list, item)` / `RemoveAt(list, index)` | Mutate |
| `Get(list, index)` / `Set(list, index, item)` | Access |
| `Count(list)` | Element count |

### Generics.Dict

Object-backed dictionary (any key/value types).

| Method | Description |
|---|---|
| `Create()` | New empty dict |
| `Set(dict, key, value)` / `Get(dict, key)` | Read/write |
| `ContainsKey(dict, key)` / `Remove(dict, key)` | Membership |
| `Keys(dict)` | Array of keys |
| `Count(dict)` | Entry count |

### System.Memory

Host binding for the managed-pointer system. The high-level, type-safe
`ManagedPtr<T>` generic contract (lives in the Contract stdlib's
`ObjektRT.std.Memory` namespace) delegates to this host; use that contract
rather than calling these directly.

| Method | Description |
|---|---|
| `Alloc(count, elemTypeName)` | Allocate a zeroed bound-checked buffer of `count` elements whose size and kind come from the element type wire name (`"int32"`, `"int64"`, `"float32"`, `"float64"`, ...); return an opaque handle |
| `Free(ptr)` | Reclaim the buffer (idempotent) |
| `Length(ptr)` | Element count of the buffer |
| `Address(ptr)` | Native address of the buffer (pointer-sized) |
| `IsFreed(ptr)` | True if the buffer has been freed |
| `Read(ptr, index)` | Read the element at `index` as the buffer's element kind, boxed to its CLR type |
| `Write(ptr, index, value)` | Write `value` at `index` in the buffer's element kind |
| `ReadI4 / ReadI8 / ReadR4 / ReadR8` | Read a typed value at element index (fixed-kind accessors) |
| `WriteI4 / WriteI8 / WriteR4 / WriteR8` | Write a typed value at element index (fixed-kind accessors) |

Each buffer carries an element kind (4-byte int vs 4-byte float share a size
but not semantics), so `Read`/`Write` dispatch on the allocation's element
type. Access is bound-checked: reading or writing out of range throws. The
generic `ManagedPtr<T>` contract passes the literal element type name (e.g.
`"int32"`), which generic materialization rewrites to the concrete wire name;
`<int>`, `<long>`, `<float>` and `<double>` are all supported. See
`CONTRACT_LANGUAGE.md` > Pointers & Managed Memory and the `_MemoryRuntimeCheck`
example for the language-level semantics and typed usage.

Access is bound-checked: reading or writing out of range throws. See
`CONTRACT_LANGUAGE.md` > Pointers & Managed Memory for the language-level
semantics.