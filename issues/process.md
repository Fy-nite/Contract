# Process Module - Missing Features

## [Process] Add `RunCapture` - capture stdout and stderr separately

`Process.RunCapture(filename, args) -> (string, string, int)` - return (stdout, stderr, exitCode) as a tuple.

Checking exit codes AND output. `Run` only returns stdout. `RunExitCode` only returns the code. `RunError` only returns stderr. No way to get all three.

```
Process.RunCapture(string fileName, string arguments) -> (string, string, int)
```

---

## [Process] Add `IsRunning` / `Kill`

Process management beyond fire-and-forget.

Managing long-running processes, timeouts, cleanup.

```
Process.IsRunning(string processName) -> bool
Process.Kill(string processName) -> void
Process.RunTimeout(string fileName, string arguments, int timeoutMs) -> string
```

---

## [Process] Add `RunBackground` - non-blocking process execution

Launch a process without waiting for it to finish.

Running build tools, compilers, long tasks in the background.

```
Process.RunBackground(string fileName, string arguments) -> int  // returns PID
Process.GetOutput(int pid) -> string
Process.WaitForExit(int pid) -> int
```

---

## [Process] Add `SetWorkingDirectory` / `SetEnvironment`

Configure the process environment before running.

Running tools that depend on CWD or env vars.

```
Process.SetWorkingDirectory(string path) -> void
Process.SetEnvironmentVariable(string name, string value) -> void
Process.ClearEnvironmentVariable(string name) -> void
```
