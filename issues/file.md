# File Module - Missing Features

## [File] Add `ReadAllBytes` / `WriteAllBytes`

Binary file I/O. `ReadAllText` / `WriteAllText` exist but there's no byte-level access.

Reading images, binaries, serialized data, network payloads saved to disk.

```
File.ReadAllBytes(string path) -> int[]
File.WriteAllBytes(string path, int[] bytes) -> void
```

---

## [File] Add `AppendAllText` / `AppendAllLines`

Append to files without overwriting. Currently only `WriteAllText` (overwrite) exists.

Logging, incremental data writes.

```
File.AppendAllText(string path, string contents) -> void
File.AppendAllLines(string path, string[] lines) -> void
```

---

## [File] Add `GetSize` / `GetLastModified` / `GetCreated`

File metadata queries.

Cache invalidation, freshness checks, display.

```
File.GetSize(string path) -> long
File.GetLastModified(string path) -> long     // returns timestamp
File.GetCreated(string path) -> long
File.IsHidden(string path) -> bool
File.IsReadOnly(string path) -> bool
```

---

## [File] Add `ReadLinesChunk` - streaming partial read

Process large files without loading entirely into memory.

```
File.ReadLinesChunk(string path, int count) -> string[]
```
