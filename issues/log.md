# New Module: Log - Structured Logging

## [New Module] `Log` - structured logging

Logging at different severity levels.

Application logging with levels, currently only `IO.Println` for output.

### Proposed Signatures

```
Log.Info(string message) -> void
Log.Warn(string message) -> void
Log.Error(string message) -> void
Log.Debug(string message) -> void
Log.SetLevel(string level) -> void
Log.SetFile(string path) -> void
Log.SetFormat(string format) -> void
Log.GetLevel() -> string
```

### Examples

```
Log.SetLevel("debug");
Log.SetFile("app.log");
Log.Info("Server started on port 8080");
Log.Warn("Disk usage above 80%");
Log.Error("Failed to connect to database");
Log.Debug("Request headers: " + headers);
```

### Notes

- Log levels (ordered): `debug` < `info` < `warn` < `error`
- `SetLevel` filters: setting to `"warn"` suppresses `debug` and `info`
- Format tokens: `{level}`, `{time}`, `{message}`, `{file}`, `{line}`
- Default format: `[{level}] {time}: {message}`
- File output appends by default
- Consider adding `Log.SetFormatter((string level, string message) -> string)` for custom formatters
