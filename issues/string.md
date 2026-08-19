# String Module - Missing Features

## [String] Add `Reverse` - reverse a string

`String.Reverse(str) -> string` - returns the string with characters in reverse order.

Common operation for palindrome checking, display tricks, and string manipulation. Currently requires manual char-by-char reversal with a loop.

```
String.Reverse(string str) -> string
```

---

## [String] Add `Count` - count occurrences of a substring

`String.Count(str, sub) -> int` - returns how many non-overlapping occurrences of `sub` appear in `str`.

Frequency analysis, validation, parsing. Currently no way to count substring occurrences without a manual loop.

```
String.Count(string str, string sub) -> int
```

---

## [String] Add `Insert` - insert a string at a position

`String.Insert(str, index, value) -> string` - returns `str` with `value` inserted at `index`.

Building strings programmatically. Currently `Substring` + `Concat` is required for this.

```
String.Insert(string str, int index, string value) -> string
```

---

## [String] Add `Remove` - remove characters at a position

`String.Remove(str, index, count) -> string` - returns `str` with `count` characters removed starting at `index`. If `count` is omitted, remove to end.

Stripping portions of strings during parsing. Currently requires `Substring` concatenation hacks.

```
String.Remove(string str, int index) -> string
String.Remove(string str, int index, int count) -> string
```

---

## [String] Add `EqualsIgnoreCase` - case-insensitive comparison

`String.EqualsIgnoreCase(a, b) -> bool` - case-insensitive string equality check.

User input validation, case-insensitive lookups. `String.Compare` exists but returns an int and requires manual logic.

```
String.EqualsIgnoreCase(string a, string b) -> bool
```

---

## [String] Add `IsDigit` / `IsLetter` / `IsAlphaNumeric` - character class checks

Character classification functions that check if a character (or string at an index) is a digit, letter, or alphanumeric.

Parsing, validation, tokenizer construction. Currently no way to check character classes without comparing against hardcoded strings.

```
String.IsDigit(string str) -> bool
String.IsLetter(string str) -> bool
String.IsAlphaNumeric(string str) -> bool
String.IsDigitAt(string str, int index) -> bool
String.IsLetterAt(string str, int index) -> bool
```

---

## [String] Add `Capitalize` / `TitleCase`

`String.Capitalize(str) -> string` - uppercase first char, lowercase rest.
`String.TitleCase(str) -> string` - uppercase first char of each word.

Display formatting, generating titles. Currently no way to do this without manual char manipulation.

```
String.Capitalize(string str) -> string
String.TitleCase(string str) -> string
```

---

## [String] Add `PadCenter` - center-align a string

`String.PadCenter(str, totalWidth) -> string` - pads both sides equally to reach `totalWidth`.

Table formatting, console output alignment. `PadLeft` and `PadRight` exist but centering requires manual math.

```
String.PadCenter(string str, int totalWidth) -> string
```

---

## [String] Add `Indent` - indent lines of a string

`String.Indent(str, spaces) -> string` - prepend `spaces` spaces to each line in the string.

Pretty-printing, code generation, log formatting. Currently no way to indent multi-line strings.

```
String.Indent(string str, int spaces) -> string
```

---

## [String] Add `Fill` - create string from character repetition

`String.Fill(count, char) -> string` - create a string of `count` copies of a single character.

Building separator lines, padding, visual formatting. `Repeat(string, int)` exists but only repeats a whole string, not a single char efficiently.

```
String.Fill(int count, string char) -> string
```

---

## [String] Add `SplitLines` / `Lines`

`String.SplitLines(str) -> string[]` - split on `\n`, `\r\n`, and `\r`.

Processing file contents line by line. `String.Split(str, "\n")` exists but doesn't handle `\r\n` cross-platform.

```
String.SplitLines(string str) -> string[]
```

---

## [String] Add `CharCodeAt` - numeric char code

`String.CharCodeAt(str, index) -> int` - return the integer char code at `index`.

`CharAt` returns a string, but there's no way to get the numeric value for ASCII/Unicode manipulation. Needed for parsing, encoding, and low-level string work.

```
String.CharCodeAt(string str, int index) -> int
```

---

## [String] Add `StartsWithAny` / `EndsWithAny`

Check if a string starts or ends with any of several prefixes/suffixes.

Validation, routing, multi-pattern matching. Currently requires manual looping with `StartsWith`.

```
String.StartsWithAny(string str, string[] prefixes) -> bool
String.EndsWithAny(string str, string[] suffixes) -> bool
```

---

## [String] Add `ReplaceFirst` - replace only first occurrence

`String.ReplaceFirst(str, old, new_) -> string` - replace only the first occurrence.

Templates, partial substitutions. `Replace` replaces all occurrences; there's no way to replace just the first.

```
String.ReplaceFirst(string str, string old, string new_) -> string
```

---

## [String] Add `ToCharArray` / `FromCharArray`

Convert between a string and an array of single-character strings.

Manual character-level processing when `CharAt` in a loop is too verbose.

```
String.ToCharArray(string str) -> string[]
String.FromCharArray(string[] chars) -> string
```
