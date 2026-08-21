// Headless end-to-end probe for the in-process LSP stack:
//   client (RequestAsync) <-> pipes <-> real Contract LspServer
// Validates initialize, didOpen, publishDiagnostics, hover, definition,
// completion — exactly the path the IDE's LspClient uses.

using System.IO.Pipelines;
using System.Text.Json;
using Contract.LanguageServer.Lsp;

var failures = new List<string>();
int passed = 0;

void Check(bool condition, string name, string? detail = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failures.Add(name);
        Console.WriteLine($"  FAIL  {name}{(detail != null ? $"\n        {detail}" : "")}");
    }
}

// ── Transport: two pipes form a duplex connection ──────────────────────────

var serverPipe = new Pipe();
var clientPipe = new Pipe();
var serverInput = serverPipe.Reader.AsStream();
var serverOutput = clientPipe.Writer.AsStream();
var clientInput = clientPipe.Reader.AsStream();
var clientOutput = serverPipe.Writer.AsStream();

// Server half — the real language server.
var serverRpc = new JsonRpcServer(serverInput, serverOutput);
var server = new LspServer();
server.Register(serverRpc);

// Client half — like the IDE's LspClient.
var clientRpc = new JsonRpcServer(clientInput, clientOutput);
var diagnosticsReceived = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
clientRpc.OnNotification("textDocument/publishDiagnostics", (p, _) =>
{
    var prms = p.Deserialize<PublishDiagnosticsParams>(LspJson.Options);
    diagnosticsReceived.TrySetResult(prms!);
    return Task.CompletedTask;
});

var cts = new CancellationTokenSource();
var serverTask = Task.Run(() => serverRpc.RunAsync(cts.Token));
var clientTask = Task.Run(() => clientRpc.RunAsync(cts.Token));

// ── Handshake ───────────────────────────────────────────────────────────────

var init = await clientRpc.RequestAsync<InitializeResult>("initialize", new InitializeParams { ProcessId = Environment.ProcessId });
Check(init?.Capabilities?.HoverProvider == true, "initialize returns capabilities", $"hover={init?.Capabilities?.HoverProvider}");
Check(init?.Capabilities?.DefinitionProvider == true, "definition provider advertised");
await clientRpc.NotifyAsync("initialized", new { });

// ── Open a document ─────────────────────────────────────────────────────────

const string Source = """
Contract Program {
    static fn add(a: int, b: int) -> int {
        return a + b;
    }

    static fn Main() {
        var x = add(1, 2);
        IO.Println(x);
    }
}
""";

var uri = new Uri(Path.GetFullPath("probe.ct")).AbsoluteUri;
await clientRpc.NotifyAsync("textDocument/didOpen", new DidOpenTextDocumentParams
{
    TextDocument = new TextDocumentItem { Uri = uri, LanguageId = "contract", Version = 1, Text = Source },
});

var diags = await diagnosticsReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(diags.Diagnostics.Count == 0, "clean program publishes no diagnostics",
    $"count={diags.Diagnostics.Count} first={(diags.Diagnostics.Count > 0 ? diags.Diagnostics[0].Message : "")}");

// ── Hover over `add` at Main's call site ────────────────────────────────────

// The raw-string indentation shifts `add` to 0-based column 16.
Hover? hover;
try
{
    hover = await clientRpc.RequestAsync<Hover>("textDocument/hover", new TextDocumentPositionParams
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
        Position = new Position(6, 16),
    });
}
catch (Exception ex)
{
    Console.WriteLine($"[debug] hover threw: {ex}");
    hover = null;
}
Check(hover?.Contents.Value.Contains("add") == true, "hover resolves the function", $"text='{hover?.Contents.Value}'");

// ── Go to definition ────────────────────────────────────────────────────────

Location? def;
try
{
    def = await clientRpc.RequestAsync<Location>("textDocument/definition", new TextDocumentPositionParams
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
        Position = new Position(6, 16),
    });
}
catch (Exception ex)
{
    Console.WriteLine($"[debug] definition threw: {ex}");
    def = null;
}
Check(def != null && def.Uri == uri && def.Range.Start.Line == 1, "definition lands on the declaration",
    $"line={def?.Range.Start.Line}");

// ── Completion at the call site ─────────────────────────────────────────────

var comp = await clientRpc.RequestAsync<CompletionList>("textDocument/completion", new CompletionParams
{
    TextDocument = new TextDocumentIdentifier { Uri = uri },
    Position = new Position(7, 12),
});
Check(comp?.Items.Count > 0, "completion returns items", $"count={comp?.Items.Count}");

// ── Typing a broken line publishes an error ─────────────────────────────────

var broken = Source.Replace("var x = add(1, 2);", "var x = add(1, 2;");
var errDiags = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
clientRpc.OnNotification("textDocument/publishDiagnostics", (p, _) =>
{
    var prms = p.Deserialize<PublishDiagnosticsParams>(LspJson.Options);
    errDiags.TrySetResult(prms!);
    return Task.CompletedTask;
});
await clientRpc.NotifyAsync("textDocument/didChange", new DidChangeTextDocumentParams
{
    TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
    ContentChanges = new List<TextDocumentContentChangeEvent> { new() { Text = broken } },
});
var err = await errDiags.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(err.Diagnostics.Count > 0, "broken line publishes diagnostics", $"count={err.Diagnostics.Count} first={(err.Diagnostics.Count > 0 ? err.Diagnostics[0].Message : "")}");

// ── External compiled-module members (.oil import) ──────────────────────────

// A tiny IR library on disk, imported DLL-style. Its members are synthesized
// declarations with no source tokens — completion/hover/signature help must
// still see them, while definition/rename must stay hands-off.
var libPath = Path.GetFullPath("extlib.oil");
File.WriteAllText(libPath, """
module Lib version 1.0.0

class Lib.Geo {
    static method Triple(x: int32) -> int32 {
        ldarg 0
        ldc.i4 3
        mul
        ret
    }
}
""");

const string ExtSource = """
import "extlib.oil";

Contract Program {
    static fn Main() {
        var n = Geo.Triple(4);
        IO.Println(n);
    }
}
""";

var extUri = new Uri(Path.GetFullPath("probe_ext.ct")).AbsoluteUri;
var extDiags = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
clientRpc.OnNotification("textDocument/publishDiagnostics", (p, _) =>
{
    var prms = p.Deserialize<PublishDiagnosticsParams>(LspJson.Options);
    extDiags.TrySetResult(prms!);
    return Task.CompletedTask;
});
await clientRpc.NotifyAsync("textDocument/didOpen", new DidOpenTextDocumentParams
{
    TextDocument = new TextDocumentItem { Uri = extUri, LanguageId = "contract", Version = 1, Text = ExtSource },
});
var extDiag = await extDiags.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(extDiag.Diagnostics.Count == 0, "external-import program publishes no diagnostics",
    $"count={extDiag.Diagnostics.Count} first={(extDiag.Diagnostics.Count > 0 ? extDiag.Diagnostics[0].Message : "")}");

// Line 4 (0-based): `        var n = Geo.Triple(4);`
// Geo=16-18, dot=19, Triple=20-25, lparen=26.
var extComp = await clientRpc.RequestAsync<CompletionList>("textDocument/completion", new CompletionParams
{
    TextDocument = new TextDocumentIdentifier { Uri = extUri },
    Position = new Position(4, 20),
});
Check(extComp?.Items.Any(i => i.Label == "Triple") == true, "completion offers external members",
    $"labels=[{string.Join(", ", extComp?.Items.Select(i => i.Label) ?? Array.Empty<string>())}]");

var extHover = await clientRpc.RequestAsync<Hover>("textDocument/hover", new TextDocumentPositionParams
{
    TextDocument = new TextDocumentIdentifier { Uri = extUri },
    Position = new Position(4, 22),
});
Check(extHover?.Contents.Value.Contains("static fn Triple(x: int) -> int") == true
    && extHover.Contents.Value.Contains("(external module)"),
    "hover shows external method signature",
    $"text='{extHover?.Contents.Value}'");

var extSig = await clientRpc.RequestAsync<SignatureHelp>("textDocument/signatureHelp", new SignatureHelpParams
{
    TextDocument = new TextDocumentIdentifier { Uri = extUri },
    Position = new Position(4, 27),
});
Check(extSig?.Signatures.Count > 0 && extSig.Signatures[0].Label.Contains("Triple(x: int)"),
    "signature help resolves external method",
    $"label='{extSig?.Signatures.FirstOrDefault()?.Label}'");

var extDef = await clientRpc.RequestAsync<Location>("textDocument/definition", new TextDocumentPositionParams
{
    TextDocument = new TextDocumentIdentifier { Uri = extUri },
    Position = new Position(4, 22),
});
Check(extDef == null, "definition stays silent for external symbols",
    $"uri={extDef?.Uri}");

var extRename = await clientRpc.RequestAsync<object?>("textDocument/prepareRename", new TextDocumentPositionParams
{
    TextDocument = new TextDocumentIdentifier { Uri = extUri },
    Position = new Position(4, 22),
});
Check(extRename == null, "rename stays disabled for external symbols");

var wsSym = await clientRpc.RequestAsync<List<SymbolInformation>>("workspace/symbol",
    new WorkspaceSymbolParams { Query = "Triple" });
Check(wsSym == null || wsSym.Count == 0, "workspace symbols exclude external declarations",
    $"count={wsSym?.Count}");

cts.Cancel();
try { await serverTask; await clientTask; } catch { /* cancelled */ }

Console.WriteLine($"\n== Probe results: {passed} passed, {failures.Count} failed ==");
return failures.Count == 0 ? 0 : 1;

