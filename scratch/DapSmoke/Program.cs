using System.Diagnostics;
using System.Text;
using System.Text.Json;

// Drives `ccl debug` over stdio and asserts the full debugging experience:
// lifecycle, named variables per scope, identifier evaluation on hover, and
// guest console output forwarded as DAP output events.

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

var jsonFrames = new List<JsonElement>();
int reqSeq = 0;

async Task<bool> PumpUntilAsync(Stream stdout, Func<JsonElement, bool>? until, TimeSpan budget)
{
    var buf = new byte[65536];
    Task<int>? pending = null;
    var acc = new List<byte>();
    var deadline = DateTime.UtcNow + budget;
    while (DateTime.UtcNow < deadline && !proc.HasExited)
    {
        if (until != null && jsonFrames.Any(until)) return true;
        pending ??= stdout.ReadAsync(buf).AsTask();
        var done = await Task.WhenAny(pending, Task.Delay(120));
        if (done != pending) continue;
        int n;
        try { n = await pending; } catch { break; }
        pending = null;
        if (n <= 0) break;
        acc.AddRange(buf[..n]);
        while (true)
        {
            var bytes = acc.ToArray();
            var text = Encoding.ASCII.GetString(bytes);
            var idx = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (idx < 0) break;
            var m = System.Text.RegularExpressions.Regex.Match(text[..idx], @"Content-Length:\s*(\d+)");
            if (!m.Success) break;
            int len = int.Parse(m.Groups[1].Value);
            if (acc.Count < idx + 4 + len) break;
            try
            {
                jsonFrames.Add(JsonDocument.Parse(Encoding.UTF8.GetString(bytes[(idx + 4)..(idx + 4 + len)])).RootElement.Clone());
            }
            catch { }
            acc.RemoveRange(0, idx + 4 + len);
        }
    }
    return until != null && jsonFrames.Any(until);
}

int Send(string command, object arguments)
{
    int seq = ++reqSeq;
    if (!proc.HasExited)
    {
        var json = JsonSerializer.Serialize(new { seq, type = "request", command, arguments });
        var bytes = Encoding.UTF8.GetBytes(json);
        proc.StandardInput.Write($"Content-Length: {bytes.Length}\r\n\r\n");
        proc.StandardInput.Write(json);
        proc.StandardInput.Flush();
    }
    return seq;
}

async Task<JsonElement?> RequestAsync(Stream stdout, string command, object arguments, TimeSpan budget)
{
    int seq = Send(command, arguments);
    bool arrived = await PumpUntilAsync(stdout,
        f => f.TryGetProperty("type", out var t) && t.GetString() == "response"
          && f.TryGetProperty("request_seq", out var r) && r.GetInt32() == seq
          && f.TryGetProperty("success", out var s) && s.GetBoolean(),
        budget);
    return arrived
        ? jsonFrames.Last(f => f.TryGetProperty("request_seq", out var r) && r.GetInt32() == seq)
        : null;
}

List<JsonElement> Events(string name) =>
    jsonFrames.Where(f => f.TryGetProperty("event", out var e) && e.GetString() == name).ToList();

try
{
    proc.Start();
    var stdout = proc.StandardOutput.BaseStream;
    var stderrTask = proc.StandardError.ReadToEndAsync();

    RequestAsync(stdout, "initialize", new { }, TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();

    Send("setBreakpoints", new { source = new { path = target }, breakpoints = new[] { new { line = breakLine } } });
    await PumpUntilAsync(stdout, null, TimeSpan.FromSeconds(5));

    Send("launch", new { program = target });
    bool initialized = await PumpUntilAsync(stdout, f => Events("initialized").Count > 0, TimeSpan.FromSeconds(30));

    Send("configurationDone", new { });
    bool stopped = await PumpUntilAsync(stdout, f => Events("stopped").Count > 0, TimeSpan.FromSeconds(40));

    var stResp = await RequestAsync(stdout, "stackTrace", new { threadId = 1 }, TimeSpan.FromSeconds(10));
    var stFrames = stResp?.GetProperty("body").GetProperty("stackFrames");
    int frameId = stFrames is { ValueKind: JsonValueKind.Array } sfr && sfr.GetArrayLength() > 0
        ? sfr[0].GetProperty("id").GetInt32()
        : -1;

    var scopeRefs = new List<(string Name, int Ref)>();
    var scopesResp = await RequestAsync(stdout, "scopes", new { frameId }, TimeSpan.FromSeconds(10));
    if (scopesResp.HasValue)
        foreach (var s in scopesResp.Value.GetProperty("body").GetProperty("scopes").EnumerateArray())
            scopeRefs.Add((s.GetProperty("name").GetString() ?? "?", s.GetProperty("variablesReference").GetInt32()));

    var variableNamesByScope = new Dictionary<string, List<string>>();
    foreach (var (name, reference) in scopeRefs)
    {
        var vresp = await RequestAsync(stdout, "variables", new { variablesReference = reference }, TimeSpan.FromSeconds(10));
        var list = new List<string>();
        if (vresp.HasValue)
            foreach (var v in vresp.Value.GetProperty("body").GetProperty("variables").EnumerateArray())
                list.Add(v.GetProperty("name").GetString() ?? "?");
        variableNamesByScope[name] = list;
    }

    var evalResp = await RequestAsync(stdout, "evaluate", new { expression = "x", frameId }, TimeSpan.FromSeconds(10));
    string evalResult = evalResp.HasValue ? evalResp.Value.GetProperty("body").GetProperty("result").GetString() ?? "" : "";

    // Clear breakpoints so the final continue runs to completion (the
    // breakpoint sits inside the loop body and would re-fire each iteration).
    Send("setBreakpoints", new { source = new { path = target }, breakpoints = Array.Empty<object>() });
    await PumpUntilAsync(stdout, null, TimeSpan.FromSeconds(3));

    Send("continue", new { });
    await PumpUntilAsync(stdout, f => Events("terminated").Count > 0, TimeSpan.FromSeconds(20));
    // Keep draining until the trailing stdout event lands (it races terminated).
    bool gotStdout = await PumpUntilAsync(stdout,
        f => Events("output").Any(e =>
            e.GetProperty("body").TryGetProperty("category", out var cat) && cat.GetString() == "stdout" &&
            e.GetProperty("body").TryGetProperty("text", out var tx) && tx.GetString()!.Contains("13")),
        TimeSpan.FromSeconds(8));

    Send("disconnect", new { });
    try { await proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(8)).Token); } catch { }

    Console.WriteLine("=== DAP SMOKE RESULTS ===");
    Console.WriteLine($"initialized evt      : {initialized}");
    Console.WriteLine($"stopped evt          : {stopped}");
    Console.WriteLine($"stack frames         : {(stFrames?.GetArrayLength() ?? 0)}");
    foreach (var (name, _) in scopeRefs)
        Console.WriteLine($"scope '{name}' vars  : [{string.Join(", ", variableNamesByScope.GetValueOrDefault(name))}]");
    Console.WriteLine($"evaluate x           : \"{evalResult}\"");
    Console.WriteLine($"output events        : {Events("output").Count}");
    foreach (var e in Events("output").Take(5))
        Console.WriteLine("  out: " + JsonSerializer.Serialize(e.GetProperty("body")).Substring(0, Math.Min(140, JsonSerializer.Serialize(e.GetProperty("body")).Length)));
    Console.WriteLine($"stdout '13' event    : {gotStdout}");

    try { var err = await stderrTask; if (!string.IsNullOrWhiteSpace(err)) Console.WriteLine("--- stderr ---\n" + err); } catch { }

    var locals = variableNamesByScope.GetValueOrDefault("Locals");
    int stackFrameCount = stFrames is { ValueKind: JsonValueKind.Array } sfa ? sfa.GetArrayLength() : 0;
    int score =
        (initialized ? 1 : 0) +
        (stopped ? 1 : 0) +
        (stackFrameCount > 0 ? 1 : 0) +
        (locals.Any(n => n == "x" || n == "i") ? 1 : 0) +
        (evalResult != "" && evalResult != "Unknown expression" ? 1 : 0) +
        (gotStdout ? 1 : 0);
    Console.WriteLine($"score: {score}/6");
    Environment.Exit(score == 6 ? 0 : 1);
}
finally
{
    if (!proc.HasExited) { try { proc.Kill(); } catch { } }
}
