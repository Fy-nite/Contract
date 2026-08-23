using System.Diagnostics;
using System.Text;

// Drives `ccl debug` over stdio with a scripted DAP session and asserts the
// expected lifecycle: initialize → setBreakpoints → launch → initialized →
// configurationDone → stopped → stackTrace (non-empty) → terminate/disconnect.

var cli = args[0];
var target = Path.GetFullPath(args[1]);
var breakLine = args.Length > 2 ? int.Parse(args[2]) : 9;

var psi = new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = $"\"{cli}\" debug",
    WorkingDirectory = Path.GetDirectoryName(target),
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
};
var proc = new Process { StartInfo = psi };

var frames = new List<string>();
int seq = 0;

async Task<bool> PumpUntilAsync(Stream stdout, string? marker, TimeSpan budget)
{
    var buf = new byte[65536];
    Task<int>? pending = null;
    var deadline = DateTime.UtcNow + budget;
    while (DateTime.UtcNow < deadline && !proc.HasExited)
    {
        if (marker != null && frames.Any(f => f.Contains($"\"{marker}\"")))
            return true;
        pending ??= stdout.ReadAsync(buf).AsTask();
        var done = await Task.WhenAny(pending, Task.Delay(120));
        if (done != pending) continue;
        int n;
        try { n = await pending; } catch { break; }
        pending = null;
        if (n <= 0) break;
        var acc = new List<byte>(buf[..n]);
        while (true)
        {
            var text = Encoding.ASCII.GetString(acc.ToArray());
            var idx = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (idx < 0) break;
            if (!int.TryParse(RegexLen(text[..idx]), out int len)) break;
            if (acc.Count < idx + 4 + len)
            {
                // need more bytes for this body; read once more into acc
                var extra = new byte[65536];
                var t2 = stdout.ReadAsync(extra).AsTask();
                var d2 = await Task.WhenAny(t2, Task.Delay(2000));
                if (d2 != t2) { break; }
                int n2 = await t2;
                if (n2 <= 0) break;
                acc.AddRange(extra[..n2]);
                continue;
            }
            frames.Add(Encoding.UTF8.GetString(acc.ToArray()[(idx + 4)..(idx + 4 + len)]));
            acc.RemoveRange(0, idx + 4 + len);
        }
    }
    return marker != null && frames.Any(f => f.Contains($"\"{marker}\""));
}

static string RegexLen(string header)
{
    var m = System.Text.RegularExpressions.Regex.Match(header, @"Content-Length:\s*(\d+)");
    return m.Success ? m.Groups[1].Value : "x";
}

void Send(string command, object arguments)
{
    if (proc.HasExited)
    {
        Console.WriteLine($"child already exited ({proc.ExitCode}) before '{command}'");
        return;
    }
    var json = System.Text.Json.JsonSerializer.Serialize(
        new { seq = ++seq, type = "request", command, arguments });
    var bytes = Encoding.UTF8.GetBytes(json);
    try
    {
        proc.StandardInput.Write($"Content-Length: {bytes.Length}\r\n\r\n");
        proc.StandardInput.Write(json);
        proc.StandardInput.Flush();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"send '{command}' failed: {ex.Message}");
    }
}

List<byte> carry = new();
try
{
    proc.Start();
    var stdout = proc.StandardOutput.BaseStream;
    var stderrTask = proc.StandardError.ReadToEndAsync();

    Send("initialize", new { });
    bool initResp = await PumpUntilAsync(stdout, "\"initialize\"", TimeSpan.FromSeconds(20));

    Send("setBreakpoints", new { source = new { path = target }, breakpoints = new[] { new { line = breakLine } } });
    await PumpUntilAsync(stdout, null, TimeSpan.FromSeconds(5));

    Send("launch", new { program = target });
    bool initialized = await PumpUntilAsync(stdout, "initialized", TimeSpan.FromSeconds(30));

    Send("configurationDone", new { });
    bool stopped = await PumpUntilAsync(stdout, "stopped", TimeSpan.FromSeconds(40));

    Send("stackTrace", new { threadId = 1 });
    await PumpUntilAsync(stdout, "stackFrames", TimeSpan.FromSeconds(10));
    var stFrame = frames.LastOrDefault(f => f.Contains("\"stackFrames\""));
    bool hasFrames = stFrame != null && !stFrame.Contains("\"stackFrames\":[]");

    Send("terminate", new { });
    await PumpUntilAsync(stdout, null, TimeSpan.FromSeconds(10));
    Send("disconnect", new { });
    try { await proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(8)).Token); } catch { }

    Console.WriteLine("=== DAP SMOKE RESULTS ===");
    Console.WriteLine($"initialize resp : {initResp}");
    Console.WriteLine($"initialized evt : {initialized}");
    Console.WriteLine($"stopped evt     : {stopped}");
    Console.WriteLine($"stack frames>0  : {hasFrames}");
    Console.WriteLine($"exit code       : {(proc.HasExited ? proc.ExitCode.ToString() : "still running")}");
    Console.WriteLine($"frames          : {frames.Count}");
    Console.WriteLine("--- notable frames ---");
    foreach (var f in frames.Where(f => f.Contains("\"event\"") || f.Contains("stackFrames")))
        Console.WriteLine(f.Length > 300 ? f[..300] + "..." : f);

    try { var err = await stderrTask; if (!string.IsNullOrWhiteSpace(err)) Console.WriteLine("--- stderr ---\n" + err); } catch { }

    int score = (initResp ? 1 : 0) + (initialized ? 1 : 0) + (stopped ? 1 : 0) + (hasFrames ? 1 : 0);
    Environment.Exit(score == 4 ? 0 : 1);
}
finally
{
    if (!proc.HasExited) { try { proc.Kill(); } catch { } }
}
