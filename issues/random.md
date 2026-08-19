# Random Module - Missing Features

## [Random] Add `NextBytes` - random byte array

Generate an array of random bytes.

Cryptography, token generation, seeding.

```
Random.NextBytes(int count) -> int[]
```

---

## [Random] Add `NextString` - random alphanumeric string

`Random.NextString(length) -> string` - generate a random alphanumeric string of the given length.

Token generation, session IDs, temporary names.

```
Random.NextString(int length) -> string
```

---

## [Random] Add `ShuffleArray` - shuffle an array

`Random.ShuffleArray(arr) -> object[]` - return a new array with elements in random order.

Delegates to `Array.Shuffle` internally but provides a convenience entry point on the Random module.

```
Random.ShuffleArray(object arr) -> object[]
```

---

## [Random] Add `WeightedChoice` - weighted random selection

`Random.WeightedChoice(items, weights) -> object` - select a random item from `items` using `weights` for probability.

Game mechanics (loot tables, spawn rates), simulation.

```
Random.WeightedChoice(object items, object weights) -> object
```

---

## [Random] Add `NextChar` - random character

`Random.NextChar() -> string` - generate a single random printable ASCII character.

Building random strings, password generation.

```
Random.NextChar() -> string
Random.NextCharInRange(string range) -> string   // e.g. "az" for lowercase only
```

---

## [Random] Add `Seed` - set random seed

`Random.Seed(long value) -> void` - set the random seed for reproducible results.

Testing, deterministic simulations, procedural generation.

```
Random.Seed(long value) -> void
```
