# New Module: Regex - Regular Expressions

## [New Module] `Regex` - regular expressions

Regular expression support module.

Pattern matching, validation, extraction. Currently no regex support - `String.Contains` / `IndexOf` are substring-only.

### Proposed Signatures

```
Regex.IsMatch(string pattern, string input) -> bool
Regex.Match(string pattern, string input) -> string         // first match
Regex.Matches(string pattern, string input) -> string[]     // all matches
Regex.Replace(string pattern, string input, string replacement) -> string
Regex.Split(string pattern, string input) -> string[]
Regex.Extract(string pattern, string input, int group) -> string  // capture group
Regex.Count(string pattern, string input) -> int            // number of matches
Regex.ReplaceCount(string pattern, string input, string replacement, int count) -> string
```

### Examples

```
Regex.IsMatch("[a-z]+@[a-z]+\\.[a-z]+", email)        // validate email
Regex.Matches("\\d+", "abc123def456")                  // ["123", "456"]
Regex.Replace("\\s+", input, " ")                       // collapse whitespace
Regex.Split("[,;]+", "a,b;c,,d")                       // ["a", "b", "c", "d"]
Regex.Extract("(\\d{4})-(\\d{2})-(\\d{2})", date, 1)  // year capture group
```

### Notes

- Backslash escaping: `\\d`, `\\w`, etc. (double backslash in Contract strings)
- Consider whether to wrap `System.Text.RegularExpressions` or implement a simpler pattern syntax
- Group indices: group 0 = entire match, group 1+ = capture groups
