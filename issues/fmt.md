# New Module: Fmt - String Formatting

## [New Module] `Fmt` - printf-style and template string formatting

String interpolation (`"Hello {name}"`) exists in the language, but there is no runtime format function for generating formatted strings from templates. This is needed for logging, i18n, and dynamic string construction from data.

### Proposed Signatures

```
Fmt.Format(string template, ...args) -> string
Fmt.ToRadix(int value, int radix) -> string       // base 2-36
Fmt.PadLeft(string value, int width, string fill) -> string
Fmt.PadRight(string value, int width, string fill) -> string
Fmt.IntToBytes(int value) -> string                // binary byte representation
```

### Examples

```
Fmt.Format("Hello {0}, you are {1} years old", name, age)
Fmt.ToRadix(255, 16)  // "ff"
Fmt.ToRadix(255, 2)   // "11111111"
Fmt.ToRadix(255, 8)   // "377"
```

### Notes

- `{0}`, `{1}`, etc. for positional args (C#-style)
- Consider also supporting `{name}` named args if the language adds named parameters later
- `ToRadix` covers base 2 (binary), 8 (octal), 10 (decimal), 16 (hex) and everything in between up to 36 (using 0-9a-z)
