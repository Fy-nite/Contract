using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Parsing;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;

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
    private readonly List<System.Reflection.Assembly> _extraBindings = new();

    /// <summary>Extra search roots (installed <c>.coi</c> package dirs) that
    /// compiled references can be found in by path.</summary>
    public static readonly List<string> PackageSearchRoots = new();

    public CompilationService(DocumentStore store)
    {
        _store = store;
    }

    public void AddPackageSearchRoot(string root)
    {
        if (!PackageSearchRoots.Contains(root)) PackageSearchRoots.Add(root);
    }

    /// <summary>Registers a custom host binding assembly so its
    /// <c>[ClassBinding]</c> modules resolve in editor diagnostics.</summary>
    public void RegisterBindingAssembly(System.Reflection.Assembly assembly)
        => _extraBindings.Add(assembly);

    public CompilationResult Compile(Document doc)
    {
        var diagnostics = new DiagnosticBag { SourceCode = doc.Text };
        var symbolTable = new SymbolTable();
        // Builtins live under the reserved __builtin.std root (import or
        // fully qualify); Reflect is registered there too.
        StdlibCatalog.RegisterInto(symbolTable);
        // Custom host bindings the client registered (Crituque's Ui/Host/Window).
        // These stay globally addressable — they are user-provided hosts.
        foreach (var asm in _extraBindings)
            symbolTable.RegisterAssembly(asm);

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

        var analyzer = new SemanticAnalyzer(symbolTable, diagnostics, doc.Path);
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
    private string? _mainPath;

    public ProgramLoader(DocumentStore store, DiagnosticBag diagnostics)
    {
        _store = store;
        _diagnostics = diagnostics;
    }

    public Dictionary<string, ParsedFile> Load(Document main)
    {
        _mainPath = main.Path;
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

    /// <summary>
    /// Search roots for Python-style namespace imports, after the importing
    /// file's own directory: the main document's directory, then the CWD.
    /// </summary>
    private IEnumerable<string> ExtraSearchRoots()
    {
        string? mainDir = _mainPath != null
            ? Path.GetDirectoryName(Contract.Compiler.ImportResolver.NormalizeAbsolutePath(_mainPath))
            : null;
        if (!string.IsNullOrEmpty(mainDir)) yield return mainDir;
        foreach (var root in CompilationService.PackageSearchRoots)
            yield return root;
        yield return Environment.CurrentDirectory;
    }

    private void LoadPath(string absolutePath, string? inMemorySource)
    {
        string normalized = Contract.Compiler.ImportResolver.NormalizeAbsolutePath(absolutePath);
        string key = TextUtility.NormalizePath(normalized);
        if (_files.ContainsKey(key)) return;

        // Compiled module reference (.orbt/.oil/.oir) — parse the module and
        // record it for static linking + synthetic declarations. Not source text.
        if (inMemorySource == null && Contract.Compiler.CompiledReferenceLoader.IsCompiledReference(normalized))
        {
            if (!File.Exists(normalized))
            {
                _diagnostics.AddError($"Imported module not found: {absolutePath}", 0, 0);
                return;
            }
            try
            {
                var module = Contract.Compiler.CompiledReferenceLoader.ParseModule(normalized);
                var prog = new Program(1, 1);
                prog.ExternalModules.Add(module);
                _files[key] = new ParsedFile { Path = normalized, Source = "", Tokens = new List<Token>(), Program = prog };
            }
            catch (Exception ex)
            {
                _diagnostics.AddError($"Error loading module {absolutePath}: {ex.Message}", 0, 0);
            }
            return;
        }

        string? source = inMemorySource
            ?? _store.GetSourceByPath(normalized)
            ?? (File.Exists(normalized) ? File.ReadAllText(normalized) : null);

        if (source == null)
        {
            _diagnostics.AddError($"Imported file not found: {absolutePath}", 0, 0);
            return;
        }

        var file = ParseFile(normalized, source, normalized);
        _files[key] = file;

        // Quoted file imports resolve relative to this file's directory.
        foreach (var import in file.Program.Imports)
        {
            string? importedFilePath = Contract.Compiler.ImportResolver.ResolveImport(import, normalized, ExtraSearchRoots());
            if (importedFilePath == null)
            {
                _diagnostics.AddError($"Imported file not found: {import}", 0, 0);
                continue;
            }
            LoadPath(importedFilePath, null);
        }

        // Namespace imports (`import ovh.finite.hello.Terminal;`) also map to
        // files by location (dots → directory separators), Python-style.
        // Stdlib-only namespace imports have no file — that's fine, they still
        // register for name resolution.
        foreach (var ns in file.Program.NamespaceImports)
        {
            string? nsFile = Contract.Compiler.ImportResolver.ResolveNamespace(ns, normalized, ExtraSearchRoots());
            if (nsFile == null) continue;
            LoadPath(nsFile, null);
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
