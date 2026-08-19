# Directory Module - Missing Features

## [Directory] Add `GetParent` / `GetTempPath` / `IsEmpty`

Common directory operations that are missing.

Navigation, temp file management, cleanup checks.

```
Directory.GetParent(string path) -> string
Directory.GetTempPath() -> string
Directory.IsEmpty(string path) -> bool
Directory.GetSize(string path) -> long
```

---

## [Directory] Add recursive file enumeration

Recursive versions of file enumeration. `GetFiles` and `GetDirectories` are not recursive.

Walking entire directory trees, finding files by extension.

```
Directory.GetFilesRecursive(string path) -> string[]
Directory.GetDirectoriesRecursive(string path) -> string[]
Directory.GetFilesRecursiveExt(string path, string extension) -> string[]
```

---

## [Directory] Add `Move` validation / `Copy`

`Directory.Move` exists but there's no `Directory.Copy` for recursive directory copying.

```
Directory.Copy(string src, string dst) -> void
Directory.Copy(string src, string dst, bool recursive) -> void
```

---

## [Directory] Add `GetFiles` with filter

Filter by extension or pattern without recursive enumeration.

```
Directory.GetFilesFiltered(string path, string pattern) -> string[]
Directory.GetFilesExt(string path, string extension) -> string[]
```
