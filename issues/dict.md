# Dict Module - Missing Features

## [Dict] Bug: `Keys` return type is `object` instead of `object[]`

`Dict.Keys(dict)` is declared as returning `object` but the implementation calls `.Keys.ToArray()` which returns `object[]`. This makes it impossible to iterate the result in Contract code.

**Severity**: Bug - breaks anyone trying to iterate dictionary keys.

**Fix**: Change return type from `object` to `object[]` in `Dict.cs` and the stdlib catalog.

---

## [Dict] Add `TryAdd` - add only if key doesn't exist

`Dict.TryAdd(dict, key, value) -> bool` - add the pair only if the key doesn't already exist. Returns true if added.

Building maps from data without overwriting. Currently requires `ContainsKey` + `Set` (two lookups).

```
Dict.TryAdd(object dict, object key, object value) -> bool
```

---

## [Dict] Add `GetOrDefault` - safe get with fallback

`Dict.GetOrDefault(dict, key, defaultValue) -> object` - return the value for `key`, or `defaultValue` if missing.

Avoiding null/missing key errors. Currently requires `ContainsKey` + `Get`.

```
Dict.GetOrDefault(object dict, object key, object defaultValue) -> object
```

---

## [Dict] Add `SetRange` - bulk insert

`Dict.SetRange(dict, keys, values) -> void` or `Dict.SetRange(dict, otherDict) -> void` - insert multiple pairs at once.

Merging dictionaries, bulk initialization.

```
Dict.SetRange(object dict, object keys, object values) -> void
Dict.SetRange(object dict, object otherDict) -> void
```

---

## [Dict] Add `Entries` - get key-value pairs

`Dict.Entries(dict) -> object[]` - return an array of `[key, value]` pairs.

Iteration over both keys and values. Currently `Keys()` and `Values()` exist separately but there's no way to get paired entries.

```
Dict.Entries(object dict) -> object[]
```

---

## [Dict] Add `ForEach` - iterate with callback

`Dict.ForEach(dict, callback) -> void` - call a function for each key-value pair.

Processing dictionary contents without manual key iteration.

```
Dict.ForEach(object dict, (object, object) -> void callback) -> void
```

---

## [Dict] Add `Merge` - combine two dicts

`Dict.Merge(a, b) -> object` - return a new dict with all entries from both, with `b` winning on conflicts.

Combining configuration, building result sets.

```
Dict.Merge(object a, object b) -> object
```

---

## [Dict] Add `Any` / `All` - predicate-based checks

`Any` returns true if any entry satisfies a predicate. `All` returns true if every entry does.

Conditional validation over dictionary contents.

```
Dict.Any(object dict, (object, object) -> bool predicate) -> bool
Dict.All(object dict, (object, object) -> bool predicate) -> bool
```

---

## [Dict] Add `FromPairs` - create dict from arrays

`Dict.FromPairs(keys, values) -> object` - create a dictionary from parallel key and value arrays.

Building dicts from external data (CSV columns, API responses).

```
Dict.FromPairs(object keys, object values) -> object
```
