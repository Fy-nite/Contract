using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contract.LanguageServer.Lsp;

/// <summary>
/// Shared JSON options for the LSP protocol. Web defaults give us camelCase
/// property names (Line -> "line", Uri -> "uri") and case-insensitive reads.
/// </summary>
public static class LspJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

// ── Positions and ranges ────────────────────────────────────────────────────

public class Position
{
    public int Line { get; set; }          // 0-based
    public int Character { get; set; }     // 0-based, UTF-16 code units

    public Position() { }
    public Position(int line, int character) { Line = line; Character = character; }

    public override string ToString() => $"{Line}:{Character}";
}

public class Range
{
    public Position Start { get; set; } = new();
    public Position End { get; set; } = new();

    public Range() { }
    public Range(Position start, Position end) { Start = start; End = end; }

    public bool Contains(Position p)
    {
        if (p.Line < Start.Line || p.Line > End.Line) return false;
        if (p.Line == Start.Line && p.Character < Start.Character) return false;
        if (p.Line == End.Line && p.Character >= End.Character) return false;
        return true;
    }
}

public class Location
{
    public string Uri { get; set; } = "";
    public Range Range { get; set; } = new();
}

// ── Text documents ──────────────────────────────────────────────────────────

public class TextDocumentIdentifier
{
    public string Uri { get; set; } = "";
}

public class VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    public int? Version { get; set; }
}

public class TextDocumentItem
{
    public string Uri { get; set; } = "";
    public string LanguageId { get; set; } = "";
    public int Version { get; set; }
    public string Text { get; set; } = "";
}

public class DidOpenTextDocumentParams
{
    public TextDocumentItem TextDocument { get; set; } = new();
}

public class TextDocumentContentChangeEvent
{
    // Full sync: the whole document text.
    public string Text { get; set; } = "";
}

public class DidChangeTextDocumentParams
{
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();
    public List<TextDocumentContentChangeEvent> ContentChanges { get; set; } = new();
}

public class DidCloseTextDocumentParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

public class TextDocumentPositionParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Position Position { get; set; } = new();
}

public class DocumentSymbolParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

// ── Diagnostics ─────────────────────────────────────────────────────────────

public class Diagnostic
{
    public Range Range { get; set; } = new();
    public int? Severity { get; set; }   // 1 Error, 2 Warning, 3 Information, 4 Hint
    public string? Code { get; set; }
    public string? Source { get; set; }
    public string Message { get; set; } = "";
}

public static class LspSeverity
{
    public const int Error = 1;
    public const int Warning = 2;
    public const int Information = 3;
    public const int Hint = 4;
}

public class PublishDiagnosticsParams
{
    public string Uri { get; set; } = "";
    public List<Diagnostic> Diagnostics { get; set; } = new();
}

// ── Document symbols ────────────────────────────────────────────────────────

public static class SymbolKind
{
    public const int File = 1, Module = 2, Namespace = 3, Package = 4, Class = 5,
        Method = 6, Property = 7, Field = 8, Constructor = 9, Enum = 10,
        Interface = 11, Function = 12, Variable = 13, Constant = 14, String = 15,
        Number = 16, Boolean = 17, Array = 18, Object = 19, Key = 20, Null = 21,
        EnumMember = 22, Struct = 23, Event = 24, Operator = 25, TypeParameter = 26;
}

public class DocumentSymbol
{
    public string Name { get; set; } = "";
    public string? Detail { get; set; }
    public int Kind { get; set; }
    public Range Range { get; set; } = new();
    public Range SelectionRange { get; set; } = new();
    public List<DocumentSymbol>? Children { get; set; }
}

// ── Hover ───────────────────────────────────────────────────────────────────

public class MarkupContent
{
    public string Kind { get; set; } = "markdown";
    public string Value { get; set; } = "";
}

public class Hover
{
    public MarkupContent Contents { get; set; } = new();
    public Range? Range { get; set; }
}

// ── Completion ──────────────────────────────────────────────────────────────

public static class CompletionItemKind
{
    public const int Text = 1, Method = 2, Function = 3, Constructor = 4, Field = 5,
        Variable = 6, Class = 7, Interface = 8, Module = 9, Property = 10, Unit = 11,
        Value = 12, Enum = 13, Keyword = 14, Snippet = 15, Color = 16, File = 17,
        Reference = 18, Folder = 19, EnumMember = 20, Constant = 21, Struct = 22,
        Event = 23, Operator = 24, TypeParameter = 25;
}

public class CompletionItem
{
    public string Label { get; set; } = "";
    public int? Kind { get; set; }
    public string? Detail { get; set; }
    public MarkupContent? Documentation { get; set; }
    public string? SortText { get; set; }
    public string? FilterText { get; set; }
}

public class CompletionList
{
    public bool IsIncomplete { get; set; }
    public List<CompletionItem> Items { get; set; } = new();
}

public class CompletionParams : TextDocumentPositionParams
{
    public CompletionContext? Context { get; set; }
}

public class CompletionContext
{
    public int TriggerKind { get; set; }   // 1 invoked, 2 triggerCharacter, 3 triggerForIncompleteCompletions
    public string? TriggerCharacter { get; set; }
}

// ── Signature help ──────────────────────────────────────────────────────────

public class ParameterInformation
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
}

public class SignatureInformation
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
    public List<ParameterInformation> Parameters { get; set; } = new();
}

public class SignatureHelp
{
    public List<SignatureInformation> Signatures { get; set; } = new();
    public int? ActiveSignature { get; set; }
    public int? ActiveParameter { get; set; }
}

public class SignatureHelpParams : TextDocumentPositionParams
{
    public CompletionContext? Context { get; set; }
}

// ── Highlight / references ──────────────────────────────────────────────────

public static class DocumentHighlightKind
{
    public const int Text = 1, Read = 2, Write = 3;
}

public class DocumentHighlight
{
    public Range Range { get; set; } = new();
    public int? Kind { get; set; }
}

public class ReferenceContext
{
    public bool IncludeDeclaration { get; set; }
}

public class ReferenceParams : TextDocumentPositionParams
{
    public ReferenceContext Context { get; set; } = new();
}

// ── Folding ─────────────────────────────────────────────────────────────────

public class FoldingRange
{
    public int StartLine { get; set; }
    public int? StartCharacter { get; set; }
    public int EndLine { get; set; }
    public int? EndCharacter { get; set; }
    public string? Kind { get; set; }   // "comment", "imports", "region"
}

public class FoldingRangeParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

// ── Semantic tokens ─────────────────────────────────────────────────────────

public class SemanticTokensLegend
{
    public List<string> TokenTypes { get; set; } = new();
    public List<string> TokenModifiers { get; set; } = new();
}

public class SemanticTokens
{
    public string? ResultId { get; set; }
    public List<int> Data { get; set; } = new();
}

public class SemanticTokensParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

// ── Code actions / edits ────────────────────────────────────────────────────

public class TextEdit
{
    public Range Range { get; set; } = new();
    public string NewText { get; set; } = "";
}

public class WorkspaceEdit
{
    public Dictionary<string, List<TextEdit>>? Changes { get; set; }
}

public class CodeActionContext
{
    public List<Diagnostic> Diagnostics { get; set; } = new();
}

public class CodeActionParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Range Range { get; set; } = new();
    public CodeActionContext Context { get; set; } = new();
}

public class CodeAction
{
    public string Title { get; set; } = "";
    public string? Kind { get; set; }
    public List<Diagnostic>? Diagnostics { get; set; }
    public WorkspaceEdit? Edit { get; set; }
}

public static class CodeActionKind
{
    public const string QuickFix = "quickfix";
    public const string Source = "source";
}

// ── Initialize / capabilities ───────────────────────────────────────────────

public class InitializeParams
{
    public int? ProcessId { get; set; }
    public string? RootUri { get; set; }
    public string? RootPath { get; set; }
}

public class TextDocumentSyncOptions
{
    public bool OpenClose { get; set; }
    public int Change { get; set; }   // 1 = full, 2 = incremental
}

public class CompletionOptions
{
    public List<string>? TriggerCharacters { get; set; }
    public bool ResolveProvider { get; set; }
}

public class SignatureHelpOptions
{
    public List<string>? TriggerCharacters { get; set; }
    public List<string>? RetriggerCharacters { get; set; }
}

public class SemanticTokensOptions
{
    public SemanticTokensLegend Legend { get; set; } = new();
    public bool Full { get; set; }   // true = full document tokens (no delta support)
}

public class CodeActionOptions
{
    public List<string>? CodeActionKinds { get; set; }
}

public class ServerCapabilities
{
    public TextDocumentSyncOptions? TextDocumentSync { get; set; }
    public bool HoverProvider { get; set; }
    public bool DefinitionProvider { get; set; }
    public bool DocumentSymbolProvider { get; set; }
    public CompletionOptions? CompletionProvider { get; set; }
    public SignatureHelpOptions? SignatureHelpProvider { get; set; }
    public bool DocumentHighlightProvider { get; set; }
    public bool ReferencesProvider { get; set; }
    public bool FoldingRangeProvider { get; set; }
    public SemanticTokensOptions? SemanticTokensProvider { get; set; }
    public CodeActionOptions? CodeActionProvider { get; set; }
}

public class ServerInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

public class InitializeResult
{
    public ServerCapabilities Capabilities { get; set; } = new();
    public ServerInfo? ServerInfo { get; set; }
}
