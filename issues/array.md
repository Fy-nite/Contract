# Array Module - Missing Features

## [Array] Add `Filter` - filter elements by predicate

`Array.Filter(arr, predicate) -> object[]` - return a new array containing only elements that satisfy the predicate.

One of the most fundamental array operations. Currently requires a manual loop with push.

```
Array.Filter(object arr, (object) -> bool predicate) -> object[]
```

---

## [Array] Add `Map` - transform elements

`Array.Map(arr, transform) -> object[]` - return a new array with `transform` applied to each element.

Transforming collections. Currently requires manual loop with push.

```
Array.Map(object arr, (object) -> object transform) -> object[]
```

---

## [Array] Add `Reduce` - accumulate a single value

`Array.Reduce(arr, accumulator, initial) -> object` - fold array into a single value.

Summing, building strings, combining values. Currently requires a manual loop.

```
Array.Reduce(object arr, (object, object) -> object accumulator, object initial) -> object
```

---

## [Array] Add `Find` / `FindIndex` / `FindLast` / `FindLastIndex`

Search operations that return an element or its index.

Looking up elements by condition. `IndexOf` does equality search but there's no predicate-based search.

```
Array.Find(object arr, (object) -> bool predicate) -> object
Array.FindIndex(object arr, (object) -> bool predicate) -> int
Array.FindLast(object arr, (object) -> bool predicate) -> object
Array.FindLastIndex(object arr, (object) -> bool predicate) -> int
```

---

## [Array] Add `Every` / `Some` - boolean aggregation

`Every` returns true if all elements satisfy a predicate. `Some` returns true if at least one does.

Validation, conditional checks. Currently requires manual loops with flags.

```
Array.Every(object arr, (object) -> bool predicate) -> bool
Array.Some(object arr, (object) -> bool predicate) -> bool
```

---

## [Array] Add `Distinct` / `Unique` - deduplicate

`Array.Distinct(arr) -> object[]` - return array with duplicates removed (preserving first occurrence order).

Removing duplicates from collections. Currently no way to do this without manual Dict/Set usage.

```
Array.Distinct(object arr) -> object[]
```

---

## [Array] Add `Slice` / `SubArray` - extract a portion

`Array.Slice(arr, start, count) -> object[]` - extract a sub-array.

Pagination, windowing, partial reads. Currently requires manual loop.

```
Array.Slice(object arr, int start, int count) -> object[]
Array.Slice(object arr, int start) -> object[]       // to end
```

---

## [Array] Add `Concat` - merge arrays

`Array.Concat(a, b) -> object[]` - return a new array containing all elements of `a` followed by all elements of `b`.

Combining collections. Currently requires manual push loop.

```
Array.Concat(object a, object b) -> object[]
```

---

## [Array] Add `Flat` / `FlatMap` - flatten nested arrays

`Array.Flat(arr) -> object[]` - flatten one level of nesting.
`Array.FlatMap(arr, transform) -> object[]` - map then flatten.

Working with nested data structures.

```
Array.Flat(object arr) -> object[]
Array.FlatMap(object arr, (object) -> object transform) -> object[]
```

---

## [Array] Add `Shuffle` - randomize order

`Array.Shuffle(arr) -> object[]` - return a new array with elements in random order.

Randomizing card decks, creating random samples, game mechanics.

```
Array.Shuffle(object arr) -> object[]
```

---

## [Array] Add `SumF` / `AverageF` / `MinF` / `MaxF` - float/double aggregations

Float and double versions of the aggregate functions. `Sum`, `Average`, `Min`, `Max` only work with `int` currently.

Working with float/double arrays. Currently there's no way to sum or average a float array without casting.

```
Array.SumF(object arr) -> float
Array.SumD(object arr) -> double
Array.AverageF(object arr) -> float
Array.AverageD(object arr) -> double
Array.MinF(object arr) -> float
Array.MaxF(object arr) -> float
Array.MinD(object arr) -> double
Array.MaxD(object arr) -> double
```

---

## [Array] Add `Sort` with comparator

`Array.Sort(arr, comparator) -> object[]` - sort with a custom comparison function.

Sorting by custom criteria (by a field, reverse order, case-insensitive). Current `Sort` uses default comparison only.

```
Array.Sort(object arr, (object, object) -> int comparator) -> object[]
```

---

## [Array] Add `BinarySearch`

`Array.BinarySearch(arr, value) -> int` - binary search on a sorted array. Returns index or -1.

Efficient lookup in sorted data. O(log n) vs O(n) linear search.

```
Array.BinarySearch(object arr, object value) -> int
```

---

## [Array] Add `Range` - generate a numeric sequence

`Array.Range(start, count) -> int[]` or `Array.Range(count) -> int[]` - generate an array of sequential integers.

Looping with indices, generating test data. Currently requires `for` with manual push.

```
Array.Range(int count) -> int[]
Array.Range(int start, int count) -> int[]
Array.RangeStep(int start, int count, int step) -> int[]
```

---

## [Array] Add `RemoveAt` / `RemoveAll`

Mutation operations that are on `List` but missing from `Array`.

Modifying fixed-size arrays in place.

```
Array.RemoveAt(object arr, int index) -> object[]   // returns new array
Array.RemoveAll(object arr, (object) -> bool predicate) -> object[]
```

---

## [Array] Add `ContainsAll` / `ContainsAny`

Set-like operations on arrays.

Checking if one collection is a subset/superset of another.

```
Array.ContainsAll(object arr, object values) -> bool   // arr contains all of values
Array.ContainsAny(object arr, object values) -> bool   // arr contains any of values
```

---

## [Array] Bug: `Sum` / `Min` / `Max` / `Average` only work with int arrays

`Array.Sum(object arr)`, `Array.Min(object arr)`, `Array.Max(object arr)`, and `Array.Average(object arr)` all return `int` and cast elements via `Convert.ToInt32`. There are no float/double variants.

Any array of `float` or `double` cannot use aggregate functions without losing precision.

**Fix**: Add `SumF`, `SumD`, `MinF`, `MaxF`, `AverageF`, `AverageD` variants.
