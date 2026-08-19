# GC Module - Missing Features

## [GC] Add `MemoryUsed` / `MaxMemory`

Memory inspection utilities.

Monitoring memory usage, debugging leaks, performance tuning.

```
GC.MemoryUsed() -> long          // bytes currently allocated
GC.MaxMemory() -> long           // max bytes allocated
GC.CollectGen(int generation) -> void  // collect specific generation
```

---

## [GC] Add `TotalAllocated` / `PendingFinalizers`

More detailed GC statistics.

Performance monitoring and diagnostics.

```
GC.TotalAllocated() -> long
GC.PendingFinalizers() -> int
GC.GetGeneration(object obj) -> int
```
