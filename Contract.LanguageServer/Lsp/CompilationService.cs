using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Parsing;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;
using Contract.Compiler.StandardLibrary.Builtins;

namespace Contract.LanguageServer.Lsp;

/// <summary>One parsed source file (main document or an import).</summary>
public class ParsedFile
{
    public required string Path { get; init; }   // absolute path, or uri key for untitled docs
    public required string Source { get; init; }
    public required List<Token> Tokens { get; init; }
    public required Program Program { get; init; }
}

/// <summary>The outcome of compiling one document: merged program, per-file artifacts, diagnostics.</summary>
public class CompilationResult
{
    public required string MainUri { get; init; }
    public required List<Token> MainTokens { get; init; }
    public required Program Program { get; init; }
    public required Dictionary<string, ParsedFile> Files { get; init; }   // normalized path -> parsed file
    public required DiagnosticBag Diagnostics { get; init; }
    public required SymbolTable SymbolTable { get; init; }
    public int Version { get; init; }

    public ParsedFile? MainFile { get; init; }

    public List<Token>? TokensFor(string? filePathOrUri)
    {
        if (filePathOrUri == null) return MainTokens;
        string key = TextUtility.NormalizePath(filePathOrUri);
        return Files.TryGetValue(key, out var f) ? f.Tokens : null;
    }
}

/// <summary>
/// Runs the lex → parse → analyze pipeline for a document and its imports, all in
/// memory. Keeps per-file tokens and programs so the language server can attribute
/// diagnostics and build a symbol index. Code generation is intentionally skipped —
/// the editor only needs semantic diagnostics, not IR.
/// </summary>
public class CompilationService
{
    private readonly DocumentStore _store;

    public CompilationService(DocumentStore store)
    {
        _store = store;
    }

    public CompilationResult Compile(Document doc)
    {
        var diagnostics = new DiagnosticBag { SourceCode = doc.Text };
        var symbolTable = new SymbolTable();
        symbolTable.RegisterAssembly(typeof(IO).Assembly);
        StdlibCatalog.RegisterInto(symbolTable);

        var loader = new ProgramLoader(_store, diagnostics);
        var files = loader.Load(doc);

        var merged = new Program(1, 1);
        foreach (var file in files.Values)
        {
            merged.Contracts.AddRange(file.Program.Contracts);
            merged.Structs.AddRange(file.Program.Structs);
            merged.Enums.AddRange(file.Program.Enums);
            merged.Functions.AddRange(file.Program.Functions);
            merged.Imports.AddRange(file.Program.Imports);
            merged.NamespaceImports.AddRange(file.Program.NamespaceImports);
        }

        // Compiled references (.orbt/.oil/.oir) synthesize declarations + retain
        // their module bodies for linking; dedup happens in Synthesize by name.
        foreach (var extModule in files.Values.SelectMany(f => f.Program.ExternalModules))
            Contract.Compiler.CompiledReferenceLoader.Synthesize(extModule, merged);

        var analyzer = new SemanticAnalyzer(symbolTable, diagnostics);
        analyzer.Analyze(merged);

        var mainKey = doc.Path != null ? TextUtility.NormalizePath(doc.Path) : doc.Uri;
        var mainFile = files.GetValueOrDefault(mainKey);

        return new CompilationResult
        {
            MainUri = doc.Uri,
            MainTokens = mainFile?.Tokens ?? new List<Token>(),
            Program = merged,
            Files = files,
            Diagnostics = diagnostics,
            SymbolTable = symbolTable,
            Version = doc.Version,
            MainFile = mainFile,
        };
    }
}

/// <summary>
/// Recursively loads a document and its imports (from the document store first,
/// falling back to disk), mirroring CompilerDriver's merge behavior but retaining
/// per-file artifacts.
/// </summary>
public class ProgramLoader
{
    private readonly DocumentStore _store;
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, ParsedFile> _files = new();

    public ProgramLoader(DocumentStore store, DiagnosticBag diagnostics)
    {
        _store = store;
        _diagnostics = diagnostics;
    }

    public Dictionary<string, ParsedFile> Load(Document main)
    {
        if (main.Path != null)
        {
            LoadPath(main.Path, main.Text); // main doc always comes from memory
        }
        else
        {
            // Untitled document: parse it standalone (no import resolution).
            var file = ParseFile(main.Uri, main.Text, main.Uri);
            _files[main.Uri] = file;
        }
        return _files;
    }

    private void LoadPath(string absolutePath, string? inMemorySource)
    {
        string key = TextUtility.NormalizePath(absolutePath);
        if (_files.ContainsKey(key)) return;

        // Compiled module reference (.orbt/.oil/.oir) — parse the module and
        // record it for static linking + synthetic declarations. Not source text.
        if (inMemorySource == null && Contract.Compiler.CompiledReferenceLoader.IsCompiledReference(absolutePath))
        {
            if (!File.Exists(absolutePath))
            {
                _diagnostics.AddError($"Imported module not found: {absolutePath}", 0, 0);
                return;
            }
            try
            {
                var module = Contract.Compiler.CompiledReferenceLoader.ParseModule(absolutePath);
                var prog = new Program(1, 1);
                prog.ExternalModules.Add(module);
                _files[key] = new ParsedFile { Path = absolutePath, Source = "", Tokens = new List<Token>(), Program = prog };
            }
            catch (Exception ex)
            {
                _diagnostics.AddError($"Error loading module {absolutePath}: {ex.Message}", 0, 0);
            }
            return;
        }

        string? source = inMemorySource
            ?? _store.GetSourceByPath(absolutePath)
            ?? (File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : null);

        if (source == null)
        {
            _diagnostics.AddError($"Imported file not found: {absolutePath}", 0, 0);
            return;
        }

        var file = ParseFile(absolutePath, source, absolutePath);
        _files[key] = file;

        string directory = Path.GetDirectoryName(absolutePath) ?? "";
        foreach (var import in file.Program.Imports)
        {
            LoadPath(Path.Combine(directory, import), null);
        }
    }

    private ParsedFile ParseFile(string path, string source, string diagnosticFile)
    {
        var lexer = new Lexer(source, _diagnostics, diagnosticFile);
        var tokens = lexer.Tokenize().ToList();
        var parser = new Parser(tokens, _diagnostics, diagnosticFile);
        var program = parser.Parse();
        return new ParsedFile { Path = path, Source = source, Tokens = tokens, Program = program };
    }
}
