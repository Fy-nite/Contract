# Time Module - Missing Features

## [Time] Add structured date component extraction

Decompose a timestamp into human-readable components. `Time.Now()` returns a unix ms long and `Time.Format()` takes a format string, but there's no way to extract individual components.

Displaying dates, conditional logic based on time of day, age calculation.

```
Time.Year(long timestamp) -> int
Time.Month(long timestamp) -> int
Time.Day(long timestamp) -> int
Time.Hour(long timestamp) -> int
Time.Minute(long timestamp) -> int
Time.Second(long timestamp) -> int
Time.DayOfWeek(long timestamp) -> int
Time.DayOfYear(long timestamp) -> int
Time.IsLeapYear(int year) -> bool
Time.DaysInMonth(int year, int month) -> int
```

---

## [Time] Add `Elapsed` / timer utilities

Convenience functions for measuring elapsed time.

Benchmarking, performance measurement, cooldowns.

```
Time.ElapsedMs(long startTimestamp) -> long
Time.ElapsedSec(long startTimestamp) -> double
Time.AddSeconds(long timestamp, int seconds) -> long
Time.AddMinutes(long timestamp, int minutes) -> long
Time.AddHours(long timestamp, int hours) -> long
Time.AddDays(long timestamp, int days) -> long
Time.Diff(long a, long b) -> long             // absolute difference in ms
```

---

## [Time] Add `Parse` - parse datetime strings

`Time.Parse(string str, string format) -> long` - parse a datetime string into a timestamp.

Reading dates from files, APIs, user input.

```
Time.Parse(string dateTimeString, string format) -> long
```

---

## [Time] Add `NowSec` / `NowMs` - explicit unit variants

Clarify the current ambiguous `Time.Now()` which returns unix ms. Add explicitly named variants.

`Now()` returns milliseconds but the name doesn't communicate that. Adding explicit variants prevents confusion.

```
Time.NowMs() -> long     // alias for Now()
Time.NowSec() -> long    // unix seconds
Time.UtcNowMs() -> long  // UTC milliseconds
```

---

## [Time] Add `ToTimestamp` / `FromTimestamp` conversion helpers

Convert between different time representations.

```
Time.ToSeconds(long ms) -> long              // ms to seconds
Time.ToMilliseconds(long seconds) -> long    // seconds to ms
Time.ToDateTimeString(long timestamp) -> string  // ISO 8601 format
Time.ToDateString(long timestamp) -> string  // date only
```
