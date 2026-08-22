using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ObjectRT.VM;
using ObjektRT.Core.Model;

namespace Contract.LanguageServer.Dap;

/// <summary>
/// Debug Adapter Protocol (DAP) server for Contract programs.
/// Communicates over stdin/stdout using JSON-RPC.
/// </summary>
public class DapServer
{
    private readonly TextReader _stdin;
    private readonly TextWriter _stdout;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _seq;

    // ── Debug state ────────────────────────────────────────────────
    private readonly List<Frame> _emptyFrames = new();
    private Interpreter? _interpreter;
    private CompiledModule? _module;
    private InterpreterDebugState? _debugState;
    private Task? _runTask;
    private CancellationTokenSource? _cts;
    private volatile bool _exitRequested;
    private readonly Dictionary<int, (string file, int line, int col)> _variableRefs = new();
    private int _nextVarRef;
    private readonly Dictionary<string, List<Breakpoint>> _breakpointsByFile = new();
    private string? _launchProgram;

    public DapServer(TextReader stdin, TextWriter stdout)
    {
        _stdin = stdin;
        _stdout = stdout;
    }

    // ── Message loop ───────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct = default)
    {
        DapLog.Write($"=== adapter start pid={Environment.ProcessId} cwd={Directory.GetCurrentDirectory()}");
        while (!ct.IsCancellationRequested && !_exitRequested)
        {
            var msg = await ReadMessageAsync(ct);
            if (msg == null) { DapLog.Write("stdin EOF, exiting run loop"); break; }

            try
            {
                await HandleMessageAsync(msg, ct);
            }
            catch (Exception ex)
            {
                DapLog.Write($"ERROR handling '{msg.Command}': {ex}");
                await SendErrorResponseAsync(msg.Seq, msg.Command, ex.Message);
            }
        }
        DapLog.Write("=== adapter exit");
    }

    private async Task<DapMessage?> ReadMessageAsync(CancellationToken ct)
    {
        // DAP uses Content-Length header (like LSP)
        while (true)
        {
            string? header = await _stdin.ReadLineAsync(ct);
            if (header == null) return null;

            if (header.StartsWith("Content-Length:"))
            {
                int len = int.Parse(header.Substring("Content-Length:".Length).Trim());
                await _stdin.ReadLineAsync(ct); // empty line
                var buf = new char[len];
                int read = 0;
                while (read < len)
                {
                    int n = await _stdin.ReadAsync(buf, read, len - read);
                    if (n == 0) return null;
                    read += n;
                }
                var json = new string(buf);
                DapLog.Write($">>> {json}");
                try
                {
                    return JsonSerializer.Deserialize<DapMessage>(json);
                }
                catch (JsonException ex)
                {
                    DapLog.Write($"malformed frame dropped: {ex.Message}");
                    continue;
                }
            }
        }
    }

    private async Task SendMessageAsync(Func<object> makeMsg)
    {
        await _writeLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(makeMsg());
            DapLog.Write($"<<< {json}");
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _stdout.WriteAsync($"Content-Length: {bytes.Length}\r\n\r\n");
            await _stdout.WriteAsync(json);
            await _stdout.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    private async Task SendResponseAsync(DapMessage request, object? body = null)
    {
        await SendMessageAsync(() => new
        {
            seq = Interlocked.Increment(ref _seq),
            request_seq = request.Seq,
            type = "response",
            command = request.Command,
            success = true,
            body
        });
    }

    private async Task SendErrorResponseAsync(int seq, string command, string message)
    {
        await SendMessageAsync(() => new
        {
            seq = Interlocked.Increment(ref _seq),
            request_seq = seq,
            type = "response",
            command,
            success = false,
            message
        });
    }

    private async Task SendEventAsync(string ev, object? body = null)
    {
        await SendMessageAsync(() => new
        {
            seq = Interlocked.Increment(ref _seq),
            type = "event",
            @event = ev,
            body
        });
    }

    private async Task SendOutputAsync(string category, string text)
    {
        await SendEventAsync("output", new { category, text = text.EndsWith('\n') ? text : text + "\n" });
    }

    // ── Message dispatch ───────────────────────────────────────────

    private async Task HandleMessageAsync(DapMessage msg, CancellationToken ct)
    {
        switch (msg.Command)
        {
            case "initialize": await HandleInitializeAsync(msg); break;
            case "launch": await HandleLaunchAsync(msg, ct); break;
            case "setBreakpoints": await HandleSetBreakpointsAsync(msg); break;
            case "setExceptionBreakpoints": await SendResponseAsync(msg, new { breakpoints = Array.Empty<object>() }); break;
            case "configurationDone": await SendResponseAsync(msg); break;
            case "cancel": await SendResponseAsync(msg); break;
            case "threads": await HandleThreadsAsync(msg); break;
            case "stackTrace": await HandleStackTraceAsync(msg); break;
            case "scopes": await HandleScopesAsync(msg); break;
            case "variables": await HandleVariablesAsync(msg); break;
            case "evaluate": await HandleEvaluateAsync(msg); break;
            case "source": await SendResponseAsync(msg, new { content = "", mimeType = "text/plain" }); break;
            case "continue": await HandleContinueAsync(msg); break;
            case "next": await HandleNextAsync(msg); break;
            case "stepIn": await HandleStepInAsync(msg); break;
            case "stepOut": await HandleStepOutAsync(msg); break;
            case "pause": await HandlePauseAsync(msg); break;
            case "terminate": await HandleTerminateAsync(msg); break;
            case "disconnect": await HandleDisconnectAsync(msg); break;
            default:
                await SendErrorResponseAsync(msg.Seq, msg.Command, $"Unknown command: {msg.Command}");
                break;
        }
    }

    // ── Handlers ───────────────────────────────────────────────────

    private async Task HandleInitializeAsync(DapMessage msg)
    {
        await SendResponseAsync(msg,new
        {
            supportsConfigurationDoneRequest = true,
            supportsStepInRequest = true,
            supportsStepOutRequest = true,
            supportsSteppingGranularity = false,
            supportsEvaluateForHovers = true,
            supportsTerminateRequest = true,
            supportsCancelRequest = true,
            supportsModulesRequest = false,
            supportsDataBreakpoints = false,
            supportsInstructionBreakpoints = false,
            exceptionBreakpointFilters = Array.Empty<object>()
        });
    }

    private async Task HandleLaunchAsync(DapMessage msg, CancellationToken ct)
    {
        var args = msg.Arguments;
        string? program = GetArg<string>(args, "program");
        string? cwd = GetArg<string>(args, "cwd");

        if (program == null)
        {
            await SendErrorResponseAsync(msg.Seq, msg.Command, "Missing 'program' argument");
            return;
        }

        if (!string.IsNullOrEmpty(cwd))
        {
            if (!Directory.Exists(cwd))
            {
                await SendErrorResponseAsync(msg.Seq, msg.Command, $"Working directory not found: {cwd}");
                return;
            }
            Directory.SetCurrentDirectory(cwd);
        }

        program = Path.GetFullPath(program);
        _launchProgram = program;
        DapLog.Write($"launch: program={program} cwd={cwd ?? Directory.GetCurrentDirectory()}");
        if (!File.Exists(program))
        {
            await SendEventAsync("output", new { category = "stderr", text = $"Program not found: {program}\n" });
            await SendErrorResponseAsync(msg.Seq, msg.Command, $"Program not found: {program}");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _debugState = new InterpreterDebugState();
        _debugState.OnPause += OnDebugPause;
        foreach (var kv in _breakpointsByFile)
            _debugState.SetBreakpoints(kv.Key, kv.Value);

        _runTask = RunProgramOnDedicatedStackAsync(program);

        await SendResponseAsync(msg);
        await SendEventAsync("initialized");
    }

    private Task RunProgramOnDedicatedStackAsync(string program)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                RunProgramAsync(program).GetAwaiter().GetResult();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, maxStackSize: 256 * 1024 * 1024);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    private async Task RunProgramAsync(string program)
    {
        try
        {
            DapLog.Write($"program starting: {program}");
            string? ir = null;
            Contract.Compiler.Diagnostics.DiagnosticBag? diags = null;
            try
            {
                ir = Contract.Runtime.ContractCompiler.CompileFile(program, out var d, isExecutable: true);
                diags = d;
            }
            catch (Exception ex)
            {
                await SendOutputAsync("stderr", ex.Message);
            }
            if (ir == null)
            {
                if (diags != null)
                    foreach (var diag in diags.Diagnostics)
                        await SendOutputAsync("stderr", diag.ToString());
                DapLog.Write("compile failed");
                await SendEventAsync("terminated", new { reason = "error" });
                return;
            }

            // Parse oil text → ORBTModule → CompiledModule
            var orbtModule = ObjektRT.Core.Parsing.OilFileReader.ParseString(ir);
            var compileResult = ObjectRT.VM.VmCompiler.Compile(orbtModule);
            if (compileResult.IsError)
            {
                string text = compileResult.Error?.Message ?? "Compilation failed";
                await SendOutputAsync("stderr", text);
                DapLog.Write($"vm compile failed: {text}");
                await SendEventAsync("terminated", new { reason = "error", text });
                return;
            }

            var host = new Contract.Runtime.ContractRuntime();
            host.PrepareModule(orbtModule);

            var loadedMod = compileResult.Value;
            _module = loadedMod;

            var interp = new Interpreter(loadedMod);
            interp.DebugState = _debugState;
            host.Inner.AttachHostHandlers(interp);
            _interpreter = interp;

            // Resolve breakpoints against source maps
            _debugState.ResolveBreakpoints(loadedMod);
            foreach (var kv in _breakpointsByFile)
                foreach (var bp in kv.Value)
                    if (bp.Verified)
                        await SendEventAsync("breakpoint", new
                        {
                            reason = "changed",
                            breakpoint = new { verified = true, line = bp.Line, column = bp.Column }
                        });

            // Run
            var result = interp.Run();
            DapLog.Write($"program finished: {(result.IsError ? result.Error.Message : "ok")}");

            if (result.IsError)
            {
                string text = result.Error.Message;
                await SendOutputAsync("stderr", $"runtime error: {text}");
                await SendEventAsync("terminated", new { reason = "error", text });
                return;
            }

            await SendEventAsync("terminated", new { reason = "normal" });
        }
        catch (Exception ex)
        {
            DapLog.Write($"program crashed: {ex}");
            await SendOutputAsync("stderr", ex.Message);
            await SendEventAsync("terminated", new { reason = "error", text = ex.Message });
        }
    }

    private async Task HandleSetBreakpointsAsync(DapMessage msg)
    {
        var args = msg.Arguments;
        string file = "";
        var sourceEl = GetArg<JsonElement>(args, "source");
        if (sourceEl.ValueKind == JsonValueKind.Object
            && sourceEl.TryGetProperty("path", out var pathEl)
            && pathEl.ValueKind == JsonValueKind.String)
        {
            file = pathEl.GetString() ?? "";
        }
        var breakpoints = GetArg<List<JsonElement>>(args, "breakpoints");

        var bps = new List<Breakpoint>();

        if (breakpoints != null)
        {
            foreach (var bp in breakpoints)
            {
                int line = bp.GetProperty("line").GetInt32();
                int col = bp.TryGetProperty("column", out var c) ? c.GetInt32() : 0;
                bps.Add(new Breakpoint { File = file, Line = line, Column = col });
            }
        }

        _breakpointsByFile[file] = bps;

        _debugState?.SetBreakpoints(file, bps);

        // Verify breakpoints against source maps
        if (_module != null)
            _debugState?.ResolveBreakpoints(_module);

        var verified = bps.Select(b => new
        {
            verified = b.Verified,
            line = b.Line,
            column = b.Column,
            source = new { path = file }
        }).ToList();

        await SendResponseAsync(msg,new { breakpoints = verified });
    }

    private async Task HandleThreadsAsync(DapMessage msg)
    {
        await SendResponseAsync(msg,new
        {
            threads = new[] { new { id = 1, name = "main" } }
        });
    }

    private async Task HandleStackTraceAsync(DapMessage msg)
    {
        if (_interpreter == null || _module == null)
        {
            await SendResponseAsync(msg,new { stackFrames = Array.Empty<object>(), totalFrames = 0 });
            return;
        }

        var frames = InterpreterDebugState.BuildFrames(
            new List<Frame>(_interpreter.Frames), _interpreter.CurrentPc, _module);

        var stackFrames = new List<object>();
        foreach (var f in frames)
        {
            string frameFile = !string.IsNullOrEmpty(f.File) && File.Exists(f.File)
                ? f.File
                : (_launchProgram != null && File.Exists(_launchProgram) ? _launchProgram : "");
            if (frameFile.Length > 0)
            {
                stackFrames.Add(new
                {
                    id = (int)f.FrameIndex,
                    name = f.Name,
                    line = f.Line,
                    column = f.Column,
                    source = new { path = frameFile }
                });
            }
            else
            {
                stackFrames.Add(new
                {
                    id = (int)f.FrameIndex,
                    name = f.Name,
                    line = f.Line,
                    column = f.Column
                });
            }
        }

        await SendResponseAsync(msg,new
        {
            stackFrames,
            totalFrames = stackFrames.Count
        });
    }

    private async Task HandleScopesAsync(DapMessage msg)
    {
        var args = msg.Arguments;
        int frameId = GetArg<int>(args, "frameId");

        var scopes = new List<object>();

        // Arguments scope
        int argVarRef = _nextVarRef++;
        _variableRefs[argVarRef] = ("arguments", frameId, 0);
        scopes.Add(new
        {
            name = "Arguments",
            variablesReference = argVarRef,
            expensive = false
        });

        // Locals scope
        int localVarRef = _nextVarRef++;
        _variableRefs[localVarRef] = ("locals", frameId, 0);
        scopes.Add(new
        {
            name = "Locals",
            variablesReference = localVarRef,
            expensive = false
        });

        // Module statics scope
        int staticVarRef = _nextVarRef++;
        _variableRefs[staticVarRef] = ("statics", frameId, 0);
        scopes.Add(new
        {
            name = "Module Statics",
            variablesReference = staticVarRef,
            expensive = false
        });

        await SendResponseAsync(msg,new { scopes });
    }

    private async Task HandleVariablesAsync(DapMessage msg)
    {
        var args = msg.Arguments;
        int varRef = GetArg<int>(args, "variablesReference");

        if (!_variableRefs.TryGetValue(varRef, out var info))
        {
            await SendResponseAsync(msg,new { variables = Array.Empty<object>() });
            return;
        }

        var (kind, frameId, _) = info;
        var variables = new List<object>();

        if (_interpreter == null || _module == null)
        {
            await SendResponseAsync(msg,new { variables });
            return;
        }

        var frames = _interpreter.Frames;
        if (frameId < 0 || frameId >= frames.Count)
        {
            await SendResponseAsync(msg,new { variables });
            return;
        }

        var frame = frames[frameId];

        switch (kind)
        {
            case "arguments":
                for (int i = 0; i < frame.Func.NumParams && i < frame.Locals.Length; i++)
                {
                    var name = frame.Func.SourceMap?.Count > 0 ? $"arg{i}" : $"arg{i}";
                    variables.Add(new
                    {
                        name = $"arg{i}",
                        value = FormatValue(frame.Locals[i]),
                        type = GetTypeName(frame.Locals[i]),
                        variablesReference = 0
                    });
                }
                break;

            case "locals":
                for (int i = 0; i < frame.Func.NumLocals; i++)
                {
                    int idx = (int)(frame.Func.NumParams + i);
                    if (idx < frame.Locals.Length)
                    {
                        variables.Add(new
                        {
                            name = $"local{i}",
                            value = FormatValue(frame.Locals[idx]),
                            type = GetTypeName(frame.Locals[idx]),
                            variablesReference = 0
                        });
                    }
                }
                break;

            case "statics":
                for (int i = 0; i < _interpreter.StaticFields.Length && i < 100; i++)
                {
                    var val = _interpreter.StaticFields[i];
                    if (val.Tag != ValueTag.Nil)
                    {
                        variables.Add(new
                        {
                            name = $"static{i}",
                            value = FormatValue(val),
                            type = GetTypeName(val),
                            variablesReference = 0
                        });
                    }
                }
                break;
        }

        await SendResponseAsync(msg,new { variables });
    }

    private async Task HandleEvaluateAsync(DapMessage msg)
    {
        var args = msg.Arguments;
        string? expression = GetArg<string>(args, "expression");

        // Simple evaluation: look up locals/args by name
        string result = "Unknown expression";
        int varRef = 0;

        if (_interpreter != null && _interpreter.Frames.Count > 0 && expression != null)
        {
            var frame = _interpreter.Frames[^1];

            // Try arg0..argN
            for (int i = 0; i < frame.Func.NumParams; i++)
            {
                if ($"arg{i}" == expression || $"arg_{i}" == expression)
                {
                    result = FormatValue(frame.Locals[i]);
                    break;
                }
            }

            // Try local0..localN
            for (int i = 0; i < frame.Func.NumLocals; i++)
            {
                int idx = (int)(frame.Func.NumParams + i);
                if (idx < frame.Locals.Length && ($"local{i}" == expression || $"local_{i}" == expression))
                {
                    result = FormatValue(frame.Locals[idx]);
                    break;
                }
            }
        }

        await SendResponseAsync(msg,new
        {
            result,
            variablesReference = varRef
        });
    }

    private async Task HandleContinueAsync(DapMessage msg)
    {
        await SendResponseAsync(msg);
        await SendEventAsync("continued", new { threadId = 1 });
        _debugState?.Resume();
    }

    private async Task HandleNextAsync(DapMessage msg)
    {
        if (_interpreter != null)
        {
            _debugState?.ConfigureStepOver(_interpreter.Frames.Count);
        }
        await SendResponseAsync(msg);
        await SendEventAsync("continued", new { threadId = 1 });
        _debugState?.Resume();
    }

    private async Task HandleStepInAsync(DapMessage msg)
    {
        _debugState?.ConfigureStepIn();
        await SendResponseAsync(msg);
        await SendEventAsync("continued", new { threadId = 1 });
        _debugState?.Resume();
    }

    private async Task HandleStepOutAsync(DapMessage msg)
    {
        if (_interpreter != null)
        {
            _debugState?.ConfigureStepOut(_interpreter.Frames.Count);
        }
        await SendResponseAsync(msg);
        await SendEventAsync("continued", new { threadId = 1 });
        _debugState?.Resume();
    }

    private async Task HandlePauseAsync(DapMessage msg)
    {
        _debugState?.RequestPause();
        await SendResponseAsync(msg);
    }

    private async Task HandleTerminateAsync(DapMessage msg)
    {
        _cts?.Cancel();
        await SendResponseAsync(msg);
        await SendEventAsync("terminated", new { reason = "remote" });
    }

    private async Task HandleDisconnectAsync(DapMessage msg)
    {
        DapLog.Write("disconnect requested, shutting down");
        _cts?.Cancel();
        _exitRequested = true;
        await SendResponseAsync(msg);
        await SendEventAsync("terminated");
    }

    // ── Debug pause event handler ──────────────────────────────────

    private async void OnDebugPause(object? sender, DebugPauseEventArgs e)
    {
        try
        {
            string reason = e.Reason switch
            {
                "breakpoint" => "breakpoint",
                "step" => "step",
                "pause" => "pause",
                _ => "pause"
            };

            var frames = _interpreter != null && _module != null
                ? InterpreterDebugState.BuildFrames(new List<Frame>(_interpreter.Frames), _interpreter.CurrentPc, _module)
                : new List<DebugFrame>();

            var file = !string.IsNullOrEmpty(e.File) && File.Exists(e.File) ? e.File : _launchProgram;
            int threadId = 1;
            await SendEventAsync("stopped", new
            {
                reason,
                threadId,
                text = $"Paused: {reason}",
                source = !string.IsNullOrEmpty(file) ? new { path = file } : null,
                line = e.Line > 0 ? (int?)e.Line : null
            });
        }
        catch (Exception ex)
        {
            await SendOutputAsync("stderr", $"debug adapter: {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static string FormatValue(Value v)
    {
        return v.Tag switch
        {
            ValueTag.Nil => "nil",
            ValueTag.I4 => v.I4.ToString(),
            ValueTag.I8 => v.I8.ToString(),
            ValueTag.R4 => v.R4.ToString("G"),
            ValueTag.R8 => v.R8.ToString("G"),
            ValueTag.Str => $"\"{v.AsStr()}\"",
            ValueTag.Obj => $"Object({v.AsObj()})",
            _ => "?"
        };
    }

    private static string GetTypeName(Value v)
    {
        return v.Tag switch
        {
            ValueTag.Nil => "nil",
            ValueTag.I4 => "int",
            ValueTag.I8 => "long",
            ValueTag.R4 => "float",
            ValueTag.R8 => "double",
            ValueTag.Str => "string",
            ValueTag.Obj => "object",
            _ => "unknown"
        };
    }

    private static T? GetArg<T>(JsonElement? args, string name)
    {
        if (args == null || !args.Value.TryGetProperty(name, out var prop))
            return default;
        return JsonSerializer.Deserialize<T>(prop.GetRawText());
    }

    // ── Message model ──────────────────────────────────────────────

    private class DapMessage
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("command")]
        public string Command { get; set; } = "";

        [JsonPropertyName("arguments")]
        public JsonElement? Arguments { get; set; }
    }
}
