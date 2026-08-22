using System;
using System.IO;

namespace Contract.LanguageServer.Dap;

/// <summary>
/// File logger for the debug adapter. Enabled always; writes to
/// %TEMP%\ccl-dap.log unless CONTRACT_DEBUG_LOG points elsewhere.
/// </summary>
public static class DapLog
{
    private const long MaxBytes = 5_000_000;
    private static readonly object Gate = new();
    private static string? _path;

    public static string FilePath
    {
        get
        {
            if (_path != null) return _path;
            var env = Environment.GetEnvironmentVariable("CONTRACT_DEBUG_LOG");
            _path = string.IsNullOrWhiteSpace(env)
                ? System.IO.Path.Combine(Path.GetTempPath(), "ccl-dap.log")
                : env;
            return _path;
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    info.Delete();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId:D2}] {message}\n");
            }
        }
        catch { }
    }
}
