using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Contract.LanguageServer.Lsp;

/// <summary>
/// Minimal JSON-RPC 2.0 server over a byte stream with LSP's Content-Length
/// framing. Handles request/response correlation, notifications, method
/// dispatch and JSON-RPC error objects. Messages are processed sequentially,
/// so a didChange notification is fully handled before the next message.
/// </summary>
public sealed class JsonRpcServer
{
    private static readonly byte[] HeaderTerminator = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly List<byte> _buffer = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object?>>> _requests = new();
    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task>> _notifications = new();

    // Pending outgoing requests (client role): correlation id -> completion.
    private readonly Dictionary<string, TaskCompletionSource<JsonElement?>> _pending = new();
    private int _nextRequestId = 1;

    /// <summary>Optional tracer for protocol traffic; writes to stderr, never stdout.</summary>
    public Action<string>? Trace { get; set; }

    public JsonRpcServer(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    public void OnRequest(string method, Func<JsonElement, CancellationToken, Task<object?>> handler)
        => _requests[method] = handler;

    public void OnNotification(string method, Func<JsonElement, CancellationToken, Task> handler)
        => _notifications[method] = handler;

    /// <summary>Reads and dispatches messages until EOF or cancellation.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var chunk = new byte[16384];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await _input.ReadAsync(chunk.AsMemory(0, chunk.Length), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break; // client closed the pipe
            }

            if (read <= 0) break; // EOF

            _buffer.AddRange(chunk.AsSpan(0, read).ToArray());

            while (TryExtractMessage(out string? json))
            {
                Trace?.Invoke($">>> {json}");
                await DispatchAsync(json!, ct);
            }
        }
    }

    /// <summary>Sends a notification (no id) to the client, e.g. publishDiagnostics.</summary>
    public async Task NotifyAsync(string method, object? @params)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteString("method", method);
            if (@params != null)
            {
                w.WritePropertyName("params");
                JsonSerializer.Serialize(w, @params, @params.GetType(), LspJson.Options);
            }
            w.WriteEndObject();
        }
        var body = ms.ToArray();
        Trace?.Invoke($"<<< {Encoding.UTF8.GetString(body)}");
        await WriteFramedAsync(body);
    }

    /// <summary>
    /// Sends a request (with an id) and awaits the peer's response — the
    /// client half of JSON-RPC. <typeparamref name="T"/> is the response's
    /// result type; a JSON-RPC error response throws
    /// <see cref="JsonRpcException"/>.
    /// </summary>
    public async Task<T?> RequestAsync<T>(string method, object? @params, CancellationToken ct = default)
    {
        string id = (_nextRequestId++).ToString();
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("jsonrpc", "2.0");
                w.WritePropertyName("id");
                w.WriteRawValue(id, skipInputValidation: true);
                w.WriteString("method", method);
                if (@params != null)
                {
                    w.WritePropertyName("params");
                    JsonSerializer.Serialize(w, @params, @params.GetType(), LspJson.Options);
                }
                w.WriteEndObject();
            }
            var body = ms.ToArray();
            Trace?.Invoke($"<<< {Encoding.UTF8.GetString(body)}");
            await WriteFramedAsync(body);

            var response = await tcs.Task.WaitAsync(ct);
            if (response == null) return default;
            if (response.Value.TryGetProperty("error", out var errorEl))
            {
                var error = errorEl.Deserialize<JsonRpcError>(LspJson.Options);
                throw new JsonRpcException(error?.Code ?? -32603, error?.Message ?? method, error?.Data);
            }
            if (response.Value.TryGetProperty("result", out var resultEl))
            {
                if (resultEl.ValueKind == JsonValueKind.Null) return default;
                return resultEl.Deserialize<T>(LspJson.Options);
            }
            return default;
        }
        finally
        {
            _pending.Remove(id);
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task DispatchAsync(string json, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return; } // malformed frame; drop

        using (doc)
        {
            var root = doc.RootElement;
            string? method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            bool hasId = root.TryGetProperty("id", out var idEl);
            JsonElement? id = hasId ? idEl : null;
            JsonElement? prms = root.TryGetProperty("params", out var p) ? p : null;

            try
            {
                if (method != null && _requests.TryGetValue(method, out var request))
                {
                    var result = await request(prms ?? default, ct);
                    await SendResponseAsync(id!.Value, result, null);
                }
                else if (method != null && _notifications.TryGetValue(method, out var notification))
                {
                    await notification(prms ?? default, ct);
                }
                else if (method != null && hasId)
                {
                    await SendResponseAsync(id!.Value, null, new JsonRpcError(-32601, $"Method not found: {method}"));
                }
                else if (method == null && hasId)
                {
                    // A response to one of our requests (client role): match by id.
                    // Clone the element so it outlives this JsonDocument.
                    var idVal = id!.Value;
                    var key = idVal.ValueKind == JsonValueKind.String
                        ? idVal.GetString() ?? ""
                        : idVal.GetRawText();
                    if (_pending.TryGetValue(key, out var tcs))
                    {
                        tcs.TrySetResult(root.Clone());
                    }
                }
                // Unknown notifications are ignored, per JSON-RPC 2.0.
            }
            catch (Exception ex)
            {
                if (hasId)
                    await SendResponseAsync(id!.Value, null, new JsonRpcError(-32603, ex.Message));
                else
                    Trace?.Invoke($"Error handling '{method}': {ex}");
            }
        }
    }

    private async Task SendResponseAsync(JsonElement id, object? result, JsonRpcError? error)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("id");
            id.WriteTo(w); // preserves the client's id (number or string)
            if (error != null)
            {
                w.WritePropertyName("error");
                JsonSerializer.Serialize(w, error, LspJson.Options);
            }
            else
            {
                w.WritePropertyName("result");
                if (result == null) w.WriteNullValue();
                else JsonSerializer.Serialize(w, result, result.GetType(), LspJson.Options);
            }
            w.WriteEndObject();
        }
        var body = ms.ToArray();
        Trace?.Invoke($"<<< {Encoding.UTF8.GetString(body)}");
        await WriteFramedAsync(body);
    }

    private async Task WriteFramedAsync(byte[] body)
    {
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writeLock.WaitAsync();
        try
        {
            await _output.WriteAsync(header);
            await _output.WriteAsync(body);
            await _output.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Extracts one complete message from the buffer, if any.</summary>
    private bool TryExtractMessage(out string? json)
    {
        json = null;
        int headerEnd = IndexOfSequence(_buffer, HeaderTerminator, 0);
        if (headerEnd < 0) return false;

        string header = Encoding.ASCII.GetString(_buffer.Take(headerEnd).ToArray());
        int contentLength = -1;
        foreach (var rawLine in header.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.Substring("Content-Length:".Length).Trim(), out int len))
            {
                contentLength = len;
            }
        }

        if (contentLength < 0)
        {
            // Malformed header; drop the frame so we don't loop forever.
            _buffer.RemoveRange(0, headerEnd + HeaderTerminator.Length);
            return false;
        }

        int total = headerEnd + HeaderTerminator.Length + contentLength;
        if (_buffer.Count < total) return false; // wait for more bytes

        json = Encoding.UTF8.GetString(_buffer.GetRange(headerEnd + HeaderTerminator.Length, contentLength).ToArray());
        _buffer.RemoveRange(0, total);
        return true;
    }

    private static int IndexOfSequence(List<byte> haystack, byte[] needle, int start)
    {
        int max = haystack.Count - needle.Length;
        for (int i = start; i <= max; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}

public class JsonRpcError
{
    public int Code { get; set; }
    public string Message { get; set; } = "";
    public object? Data { get; set; }

    public JsonRpcError() { }
    public JsonRpcError(int code, string message) { Code = code; Message = message; }
}

/// <summary>Thrown when a JSON-RPC request is answered with an error response.</summary>
public class JsonRpcException : Exception
{
    public int Code { get; }
    public object? ErrorData { get; }

    public JsonRpcException(int code, string message, object? data = null)
        : base(message)
    {
        Code = code;
        ErrorData = data;
    }
}
