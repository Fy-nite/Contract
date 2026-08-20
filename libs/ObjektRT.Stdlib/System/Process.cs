using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Process execution. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Process")]
public static class Process
{
    /// <summary>Runs a command with arguments and returns its standard output (trimmed of the trailing newline).</summary>
    public static string Run(string fileName, string arguments)
    {
        var psi = new global::System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = global::System.Diagnostics.Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.TrimEnd('\r', '\n');
    }

    /// <summary>Runs a command with arguments and returns its exit code.</summary>
    public static int RunExitCode(string fileName, string arguments)
    {
        var psi = new global::System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = global::System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>Runs a command with arguments and returns its standard error output (trimmed).</summary>
    public static string RunError(string fileName, string arguments)
    {
        var psi = new global::System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = global::System.Diagnostics.Process.Start(psi)!;
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return error.TrimEnd('\r', '\n');
    }
}
