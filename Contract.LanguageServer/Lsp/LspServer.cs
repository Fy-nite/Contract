using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Parsing;
using Contract.Compiler.StandardLibrary;

namespace Contract.LanguageServer.Lsp;

/// <summary>
/// LSP method handlers. State is per-document: text is kept in the DocumentStore,
/// and every didChange re-runs the lex → parse → analyze pipeline and publishes
/// fresh diagnostics (parser error recovery means all errors report while typing).
/// </summary>
public class LspServer
{
    private const string LanguageId = "contract";

    private readonly DocumentStore _store = new();
    private readonly CompilationService _compiler;
    private readonly SymbolIndex _index;
    private JsonRpcServer? _rpc;
    private bool _shutdownRequested;

    public LspServer()
    {
        _compiler = new CompilationService(_store);
        _index = new SymbolIndex(new XmlDocProvider());
    }

    public void Register(JsonRpcServer rpc)
    {
        _rpc = rpc;
        rpc.OnRequest("initialize", Initialize);
        rpc.OnRequest("shutdown", Shutdown);
        rpc.OnRequest("textDocument/documentSymbol", DocumentSymbols);
        rpc.OnRequest("textDocument/hover", Hover);
        rpc.OnRequest("textDocument/definition", Definition);
        rpc.OnRequest("textDocument/completion", Completion);
        rpc.OnRequest("textDocument/signatureHelp", SignatureHelp);
        rpc.OnRequest("textDocument/documentHighlight", DocumentHighlights);
        rpc.OnRequest("textDocument/references", References);
        rpc.OnRequest("textDocument/foldingRange", FoldingRanges);
        rpc.OnRequest("textDocument/semanticTokens/full", SemanticTokens);
        rpc.OnRequest("textDocument/codeAction", CodeActions);
        rpc.OnNotification("initialized", (_, _) => Task.CompletedTask);
        rpc.OnNotification("exit", Exit);
        rpc.OnNotification("textDocument/didOpen", DidOpen);
        rpc.OnNotification("textDocument/didChange", DidChange);
        rpc.OnNotification("textDocument/didClose", DidClose);
    }

    /// <summary>Registers a custom host binding assembly so its
    /// <c>[ClassBinding]</c> modules (Ui, Host, …) resolve in editor
    /// diagnostics, completion, hover. Call before the first document opens.</summary>
    public void RegisterBindingAssembly(System.Reflection.Assembly assembly)
        => _compiler.RegisterBindingAssembly(assembly);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private Task<object?> Initialize(JsonElement _, CancellationToken _2)
    {
        var result = new InitializeResult
        {
            Capabilities = new ServerCapabilities
            {
                TextDocumentSync = new TextDocumentSyncOptions { OpenClose = true, Change = 1 }, // full sync
                HoverProvider = true,
                DefinitionProvider = true,
                DocumentSymbolProvider = true,
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = new List<string> { ".", ":", "::" },
                    ResolveProvider = false,
                },
                SignatureHelpProvider = new SignatureHelpOptions
                {
                    TriggerCharacters = new List<string> { "(", "," },
                },
                DocumentHighlightProvider = true,
                ReferencesProvider = true,
                FoldingRangeProvider = true,
                SemanticTokensProvider = new SemanticTokensOptions
                {
                    Legend = new SemanticTokensLegend
                    {
                        TokenTypes = SemanticTokensBuilder.TokenTypes,
                        TokenModifiers = SemanticTokensBuilder.TokenModifiers,
                    },
                    Full = true,
                },
                CodeActionProvider = new CodeActionOptions
                {
                    CodeActionKinds = new List<string> { CodeActionKind.QuickFix },
                },
            },
            ServerInfo = new ServerInfo { Name = "contract-language-server", Version = "0.2.0" },
        };
        return Task.FromResult<object?>(result);
    }

    private Task<object?> Shutdown(JsonElement _, CancellationToken _2)
    {
        _shutdownRequested = true;
        return Task.FromResult<object?>(null);
    }

    private Task Exit(JsonElement _, CancellationToken _2)
    {
        Environment.Exit(_shutdownRequested ? 0 : 1);
        return Task.CompletedTask;
    }

    // ── Document sync ────────────────────────────────────────────────────────

    private async Task DidOpen(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<DidOpenTextDocumentParams>(LspJson.Options);
        if (prms == null) return;
        var item = prms.TextDocument;
        _store.Open(item.Uri, item.Text, item.Version);
        await CompileAndPublishAsync(_store.Get(item.Uri));
    }

    private async Task DidChange(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<DidChangeTextDocumentParams>(LspJson.Options);
        if (prms == null || prms.ContentChanges.Count == 0) return;
        var doc = _store.Get(prms.TextDocument.Uri);
        if (doc == null) return;
        _store.Change(prms.TextDocument.Uri, prms.ContentChanges[^1].Text, prms.TextDocument.Version ?? 0);
        await CompileAndPublishAsync(doc);
    }

    private async Task DidClose(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<DidCloseTextDocumentParams>(LspJson.Options);
        if (prms == null) return;
        _store.Close(prms.TextDocument.Uri);
        // Clear any diagnostics for the closed document.
        await _rpc!.NotifyAsync("textDocument/publishDiagnostics",
            new PublishDiagnosticsParams { Uri = prms.TextDocument.Uri, Diagnostics = new List<Diagnostic>() });
    }

    // ── Compilation + diagnostics ────────────────────────────────────────────

    private async Task CompileAndPublishAsync(Document? doc)
    {
        if (doc == null || _rpc == null) return;

        CompilationResult result;
        try
        {
            result = _compiler.Compile(doc);
        }
        catch (Exception ex)
        {
            // Never crash the server on a document that breaks the compiler.
            result = new CompilationResult
            {
                MainUri = doc.Uri,
                MainTokens = new List<Token>(),
                Program = new Program(1, 1),
                Files = new Dictionary<string, ParsedFile>(),
                Diagnostics = new DiagnosticBag { SourceCode = doc.Text },
                SymbolTable = new SymbolTable(),
                Version = doc.Version,
                MainFile = null,
            };
            result.Diagnostics.AddError($"Internal compiler error: {ex.Message}", 1, 1, doc.Path);
        }

        doc.LastCompilation = result;
        _index.Build(result);

        string? mainPathNorm = result.MainFile != null ? TextUtility.NormalizePath(result.MainFile.Path) : null;
        string mainSource = doc.Text;

        // Always publish for the main document (empty list clears stale squiggles).
        var mainDiags = result.Diagnostics.Diagnostics
            .Where(d => d.SourceFile == null
                        || (mainPathNorm != null && TextUtility.NormalizePath(d.SourceFile) == mainPathNorm))
            .Select(d => ToLspDiagnostic(d, result.MainTokens, mainSource))
            .ToList();
        await _rpc.NotifyAsync("textDocument/publishDiagnostics",
            new PublishDiagnosticsParams { Uri = doc.Uri, Diagnostics = mainDiags });

        // Publish per imported file (attributed via SourceFile, set by Lexer/Parser).
        foreach (var file in result.Files.Values.Where(f =>
                     mainPathNorm == null || TextUtility.NormalizePath(f.Path) != mainPathNorm))
        {
            var fileDiags = result.Diagnostics.Diagnostics
                .Where(d => d.SourceFile != null && TextUtility.NormalizePath(d.SourceFile) == TextUtility.NormalizePath(file.Path))
                .Select(d => ToLspDiagnostic(d, file.Tokens, file.Source))
                .ToList();
            await _rpc.NotifyAsync("textDocument/publishDiagnostics",
                new PublishDiagnosticsParams { Uri = TextUtility.PathToUri(file.Path), Diagnostics = fileDiags });
        }
    }

    private static Diagnostic ToLspDiagnostic(Contract.Compiler.Diagnostics.Diagnostic d, IReadOnlyList<Token>? tokens, string source)
        => new()
        {
            Range = TextUtility.DiagnosticRange(d, tokens, source),
            Severity = d.Severity switch
            {
                DiagnosticSeverity.Error => LspSeverity.Error,
                DiagnosticSeverity.Warning => LspSeverity.Warning,
                _ => LspSeverity.Information,
            },
            Source = "contract",
            Message = d.Message,
        };

    // ── Requests ─────────────────────────────────────────────────────────────

    private async Task<object?> EnsureCompiledAsync(string uri)
    {
        var doc = _store.Get(uri);
        if (doc == null) return null;
        if (doc.LastCompilation == null || doc.LastCompilation.Version != doc.Version)
            await CompileAndPublishAsync(doc);
        return doc;
    }

    private async Task<object?> DocumentSymbols(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<DocumentSymbolParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;
        return _index.DocumentSymbols(doc.Uri);
    }

    private async Task<object?> Hover(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<TextDocumentPositionParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;

        var target = _index.Resolve(doc.LastCompilation, prms.Position);
        if (target == null) return null;

        var hover = new Hover();
        if (target.Symbol != null)
        {
            hover.Contents.Value = SymbolIndex.SymbolHoverText(target.Symbol);
            hover.Range = target.Symbol.SelectionRange;
        }
        else if (target.HoverText != null)
        {
            hover.Contents.Value = target.HoverText;
            var tok = TextUtility.FindTokenAt(doc.Tokens!, prms.Position);
            if (tok != null) hover.Range = TextUtility.TokenRange(tok);
        }
        return hover;
    }

    private async Task<object?> Definition(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<TextDocumentPositionParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;

        var target = _index.Resolve(doc.LastCompilation, prms.Position);
        if (target?.Symbol == null) return null;
        return new Location { Uri = target.Symbol.Uri, Range = target.Symbol.SelectionRange };
    }

    // ── Completion ───────────────────────────────────────────────────────────

    private async Task<object?> Completion(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<CompletionParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;
        return _index.Completions(doc.LastCompilation, prms.Position);
    }

    // ── Signature help ───────────────────────────────────────────────────────

    private async Task<object?> SignatureHelp(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<SignatureHelpParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;
        return _index.SignatureHelp(doc.LastCompilation, prms.Position);
    }

    // ── Document highlight ───────────────────────────────────────────────────

    private async Task<object?> DocumentHighlights(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<TextDocumentPositionParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;
        return _index.DocumentHighlights(doc.LastCompilation, prms.Position);
    }

    // ── References ───────────────────────────────────────────────────────────

    private async Task<object?> References(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<ReferenceParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;
        return _index.References(doc.LastCompilation, prms.Position, prms.Context.IncludeDeclaration);
    }

    // ── Folding ──────────────────────────────────────────────────────────────

    private async Task<object?> FoldingRanges(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<FoldingRangeParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;
        return SemanticTokensBuilder.FoldingRanges(doc.LastCompilation.MainTokens);
    }

    // ── Semantic tokens ──────────────────────────────────────────────────────

    private async Task<object?> SemanticTokens(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<SemanticTokensParams>(LspJson.Options);
        if (prms == null) return null;
        var doc = await EnsureCompiledAsync(prms.TextDocument.Uri) as Document;
        if (doc?.LastCompilation == null) return null;
        return SemanticTokensBuilder.Build(doc.LastCompilation, doc.Uri, _index);
    }

    // ── Code actions ─────────────────────────────────────────────────────────

    private Task<object?> CodeActions(JsonElement p, CancellationToken _)
    {
        var prms = p.Deserialize<CodeActionParams>(LspJson.Options);
        if (prms == null) return Task.FromResult<object?>(null);
        var doc = _store.Get(prms.TextDocument.Uri);
        if (doc == null) return Task.FromResult<object?>(null);

        var actions = new List<CodeAction>();
        foreach (var d in prms.Context.Diagnostics)
        {
            if (d.Message.Contains("Expected ';'") && d.Range.Start.Line > 0)
            {
                int prevLine = d.Range.Start.Line - 1;
                string prevLineText = TextUtility.GetLine(doc.Text, prevLine + 1);
                if (prevLineText.TrimEnd().EndsWith(';') || prevLineText.TrimEnd().EndsWith('}'))
                    continue; // already terminated; inserting would be noise
                actions.Add(new CodeAction
                {
                    Title = "Add missing ';'",
                    Kind = CodeActionKind.QuickFix,
                    Diagnostics = new List<Diagnostic> { d },
                    Edit = new WorkspaceEdit
                    {
                        Changes = new Dictionary<string, List<TextEdit>>
                        {
                            [prms.TextDocument.Uri] = new()
                            {
                                new TextEdit
                                {
                                    Range = new Range(new Position(prevLine, prevLineText.Length),
                                                       new Position(prevLine, prevLineText.Length)),
                                    NewText = ";",
                                },
                            },
                        },
                    },
                });
            }
        }
        return Task.FromResult<object?>(actions.Count > 0 ? actions : null);
    }
}
