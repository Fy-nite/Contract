using System;
using System.Collections.Generic;
using System.Linq;

namespace Contract.LanguageServer.Lsp;

/// <summary>A document open in the editor, plus cached compilation artifacts.</summary>
public class Document
{
    public required string Uri { get; init; }
    public string? Path { get; init; }        // local path for file:// URIs; null for untitled
    public string Text { get; set; } = "";
    public int Version { get; set; }
    public CompilationResult? LastCompilation { get; set; }

    /// <summary>The main document's token list (from its most recent compilation).</summary>
    public IReadOnlyList<Contract.Compiler.Parsing.Token>? Tokens => LastCompilation?.MainTokens;

    public bool IsUntitled => Path == null;
}

/// <summary>In-memory store of open documents, keyed by URI (and by normalized path).</summary>
public class DocumentStore
{
    private readonly Dictionary<string, Document> _byUri = new();
    private readonly Dictionary<string, string> _pathToUri = new(); // normalized path -> uri

    private readonly object _lock = new();

    public Document? Get(string uri)
    {
        lock (_lock) return _byUri.TryGetValue(uri, out var d) ? d : null;
    }

    public Document? GetByPath(string absolutePath)
    {
        string key = TextUtility.NormalizePath(absolutePath);
        lock (_lock)
        {
            return _pathToUri.TryGetValue(key, out var uri) ? _byUri.GetValueOrDefault(uri) : null;
        }
    }

    public void Open(string uri, string text, int version)
    {
        lock (_lock)
        {
            var doc = new Document { Uri = uri, Path = TextUtility.UriToPath(uri), Text = text, Version = version };
            _byUri[uri] = doc;
            if (doc.Path != null) _pathToUri[TextUtility.NormalizePath(doc.Path)] = uri;
        }
    }

    public void Change(string uri, string text, int version)
    {
        lock (_lock)
        {
            if (!_byUri.TryGetValue(uri, out var doc)) return;
            doc.Text = text;
            doc.Version = version;
            doc.LastCompilation = null; // stale
        }
    }

    public void Close(string uri)
    {
        lock (_lock)
        {
            if (!_byUri.TryGetValue(uri, out var doc)) return;
            if (doc.Path != null) _pathToUri.Remove(TextUtility.NormalizePath(doc.Path));
            _byUri.Remove(uri);
        }
    }

    public IReadOnlyList<Document> All()
    {
        lock (_lock) return _byUri.Values.ToList();
    }

    /// <summary>Source provider for CompilerDriver: in-memory text for open documents, else null (disk fallback).</summary>
    public string? GetSourceByPath(string absolutePath)
        => GetByPath(absolutePath)?.Text;
}
