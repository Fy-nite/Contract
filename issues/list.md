# List Module - Missing Features

## [List] Add `AddRange` - append multiple items

`List.AddRange(list, items) -> void` - add all elements from an array to the list.

Bulk insertion. Currently requires looping `Add`.

```
List.AddRange(object list, object items) -> void
```

---

## [List] Add `GetRange` / `Slice` - extract a portion

`List.GetRange(list, index, count) -> object[]` - extract a sub-array from a list.

Pagination, windowing.

```
List.GetRange(object list, int index, int count) -> object[]
```

---

## [List] Add `LastIndexOf` - find last occurrence

`List.LastIndexOf(list, item) -> int` - find the last index of an item, or -1.

Reverse searching. `String.LastIndexOf` exists but `List` doesn't have it.

```
List.LastIndexOf(object list, object item) -> int
```

---

## [List] Add `First` / `Last` - get first or last element

Convenience accessors for the first and last elements.

Extremely common operation. Currently requires `Get(list, 0)` or `Get(list, Count(list) - 1)`.

```
List.First(object list) -> object
List.Last(object list) -> object
```

---

## [List] Add `Min` / `Max` / `Sum` / `Average`

Aggregate operations for numeric lists.

Statistical analysis. These exist on `Array` but not `List`.

```
List.Min(object list) -> int
List.Max(object list) -> int
List.Sum(object list) -> int
List.Average(object list) -> int
```

---

## [List] Add `ForEach` / `Find` / `FindLast` / `FindIndex`

Functional-style operations.

Common list operations that currently require manual loops.

```
List.ForEach(object list, (object) -> void action) -> void
List.Find(object list, (object) -> bool predicate) -> object
List.FindLast(object list, (object) -> bool predicate) -> object
List.FindIndex(object list, (object) -> bool predicate) -> int
List.Exists(object list, (object) -> bool predicate) -> bool
List.TrueForAll(object list, (object) -> bool predicate) -> bool
```

---

## [List] Add `Sort` with comparator

Custom sort with a comparison function.

Sorting by specific criteria. `Sort()` only uses default ordering.

```
List.Sort(object list, (object, object) -> int comparator) -> void
```

---

## [List] Add `Distinct` / `Unique`

Remove duplicates from a list in place.

Deduplication.

```
List.Distinct(object list) -> void
```
