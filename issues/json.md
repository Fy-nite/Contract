# Json Module - Missing Features

## [Json] Add `PrettyPrint` - formatted JSON output

`Json.PrettyPrint(json) -> string` - format a JSON string with indentation for human readability.

Debugging, log output, displaying JSON to users. `Json.Serialize` produces compact JSON with no formatting.

```
Json.PrettyPrint(string json) -> string
Json.PrettyPrint(string json, int indent) -> string
```

---

## [Json] Add `Minify` - remove whitespace from JSON

`Json.Minify(json) -> string` - remove all unnecessary whitespace from a JSON string.

Network optimization, compact storage. The inverse of `PrettyPrint`.

```
Json.Minify(string json) -> string
```

---

## [Json] Add `IsValid` - validate JSON

`Json.IsValid(string json) -> bool` - check if a string is valid JSON without parsing it.

Input validation, error handling. Currently must catch a parse error.

```
Json.IsValid(string json) -> bool
```

---

## [Json] Add `GetValue` / `GetArray` - typed access helpers

Navigate nested JSON structures without casting.

Working with `Json.Parse` results which return `object`. Currently requires manual casting and dict/array access.

```
Json.GetValue(string json, string path) -> string
Json.GetNumber(string json, string path) -> double
Json.GetBool(string json, string path) -> bool
Json.GetArray(string json, string path) -> object
Json.ArrayGet(object arr, int index) -> object
Json.ArrayLength(object arr) -> int
```

---

## [Json] Add `SerializeTyped` - serialize with type hints

`Json.SerializeTyped(object value) -> string` - serialize with type information so `ParseTyped` can reconstruct the original types.

Currently `Json.Parse` returns `object` (int or double for numbers, string for strings, Dict for objects, object[] for arrays). No way to distinguish float from double, or preserve custom types.

```
Json.SerializeTyped(object value) -> string
Json.ParseTyped(string json) -> object
```

---

## [Json] Add `Merge` - merge two JSON objects

`Json.Merge(jsonA, jsonB) -> string` - merge two JSON objects, with B winning on key conflicts.

Configuration merging, API response combining.

```
Json.Merge(string a, string b) -> string
```

---

## [Json] Add `Pick` / `Omit` - include/exclude keys

`Json.Pick(json, keys) -> string` - return a new JSON object with only the specified keys.
`Json.Omit(json, keys) -> string` - return a new JSON object without the specified keys.

API data shaping, filtering sensitive fields.

```
Json.Pick(string json, string[] keys) -> string
Json.Omit(string json, string[] keys) -> string
```
