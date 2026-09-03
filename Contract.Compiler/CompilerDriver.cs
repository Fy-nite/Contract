using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.Parsing;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler
{
    public class CompilerDriver
    {
        private readonly DiagnosticBag _diagnostics;
        private readonly HashSet<string> _loadedFiles = new();
        private readonly Func<string, string?>? _sourceProvider;
        private string? _mainFileDir;

        /// <summary>
        /// When <paramref name="sourceProvider"/> is set, it is consulted (with the absolute file path)
        /// before reading from disk. Return null to fall back to disk. This lets embedders (e.g. a
        /// language server) compile documents that only exist in memory.
        /// </summary>
        public CompilerDriver(DiagnosticBag diagnostics, Func<string, string?>? sourceProvider = null)
        {
            _diagnostics = diagnostics;
            _sourceProvider = sourceProvider;
        }

        public Program Compile(string mainFilePath)
        {
            _mainFileDir = Path.GetDirectoryName(ImportResolver.NormalizeAbsolutePath(mainFilePath));
            var fullProgram = new Program(1, 1);
            LoadFile(mainFilePath, fullProgram);
            return fullProgram;
        }

        /// <summary>
        /// Compiles a main file together with additional source files (glob-compile mode).
        /// The additional files are loaded first so they're recognized when reached via
        /// import statements, and all top-level declarations are merged.
        /// </summary>
        public Program Compile(string mainFilePath, IEnumerable<string> additionalFiles)
        {
            _mainFileDir = Path.GetDirectoryName(ImportResolver.NormalizeAbsolutePath(mainFilePath));
            var fullProgram = new Program(1, 1);

            // Pre-load additional files so they're known to the dedup set
            foreach (var file in additionalFiles)
            {
                string abs = ImportResolver.NormalizeAbsolutePath(file);
                if (!_loadedFiles.Contains(abs) && File.Exists(abs))
                    LoadFile(abs, fullProgram);
            }

            LoadFile(mainFilePath, fullProgram);
            return fullProgram;
        }

        /// <summary>
        /// Compiles only the given source files with no main entry point (library glob mode).
        /// <paramref name="projectRoot"/> is used as the base directory for import resolution.
        /// </summary>
        public Program Compile(IEnumerable<string> sourceFiles, string projectRoot)
        {
            _mainFileDir = projectRoot;
            var fullProgram = new Program(1, 1);

            foreach (var file in sourceFiles)
            {
                string abs = ImportResolver.NormalizeAbsolutePath(file);
                if (!_loadedFiles.Contains(abs) && File.Exists(abs))
                    LoadFile(abs, fullProgram);
            }

            return fullProgram;
        }

        /// <summary>
        /// Search roots for Python-style namespace imports, after the importing
        /// file's own directory: the main file's directory, any declared
        /// <c>ImportRoots</c> from the project's <c>contract.ctproj</c> (a
        /// C#/Java "classpath" so <c>import Some.Namespace;</c> finds a library
        /// source by its DECLARED namespace wherever it lives), then the CWD,
        /// then any Purr package directories.
        /// </summary>
        private IEnumerable<string> ExtraSearchRoots()
        {
            if (!string.IsNullOrEmpty(_mainFileDir))
            {
                yield return _mainFileDir;

                // Find the nearest contract.ctproj (the project root).
                string? projectRoot = _mainFileDir;
                while (projectRoot != null && !File.Exists(Path.Combine(projectRoot, ContractProject.FileName)))
                    projectRoot = Path.GetDirectoryName(projectRoot);

                if (projectRoot != null)
                {
                    // Yield the project's declared extra import roots first.
                    ContractProject? project = null;
                    try { project = ContractProject.Load(projectRoot); }
                    catch { project = null; }
                    if (project?.ImportRoots is { Count: > 0 })
                    {
                        string projectBase = project.RootPath ?? projectRoot;
                        foreach (var impRoot in project.ImportRoots)
                        {
                            string full = ImportResolver.NormalizeAbsolutePath(
                                Path.IsPathRooted(impRoot) ? impRoot : Path.Combine(projectBase, impRoot));
                            if (!string.IsNullOrEmpty(full)) yield return full;
                        }
                    }

                    string purrPackages = Path.Combine(projectRoot, ".purr", "packages");
                    if (Directory.Exists(purrPackages))
                    {
                        foreach (var pkgDir in Directory.GetDirectories(purrPackages))
                            yield return pkgDir;
                    }
                }
            }
            yield return Environment.CurrentDirectory;
        }

        private void LoadFile(string filePath, Program fullProgram)
        {
            string absolutePath = ImportResolver.NormalizeAbsolutePath(filePath);
            if (_loadedFiles.Contains(absolutePath)) return;
            _loadedFiles.Add(absolutePath);

            string? source = _sourceProvider?.Invoke(absolutePath);
            if (source == null)
            {
                if (!File.Exists(absolutePath))
                {
                    _diagnostics.AddError($"Imported file not found: {filePath}", 0, 0);
                    return;
                }

                source = File.ReadAllText(absolutePath);
            }

            try
            {
                var lexer = new Lexer(source, _diagnostics, absolutePath);
                var tokens = lexer.Tokenize().ToList();

                var parser = new Parser(tokens, _diagnostics, absolutePath);
                var program = parser.Parse();

                // Merge into full program
                foreach (var contract in program.Contracts)
                    fullProgram.Contracts.Add(contract);
                foreach (var func in program.Functions)
                    fullProgram.Functions.Add(func);
                foreach (var structDecl in program.Structs)
                    fullProgram.Structs.Add(structDecl);
                foreach (var enumDecl in program.Enums)
                    fullProgram.Enums.Add(enumDecl);
                foreach (var ns in program.NamespaceImports)
                    fullProgram.NamespaceImports.Add(ns);
                foreach (var ext in program.Extensions)
                    fullProgram.Extensions.Add(ext);

                // Recursively load imports.
                //
                // Quoted file imports (`import "path/to/file.ct";`) resolve
                // relative to this file's directory, like Python's script-dir
                // lookup. A compiled reference (.orbt/.oil/.oir) is loaded as a
                // DLL-style include; anything else is a .ct source file import.
                foreach (var import in program.Imports)
                {
                    string? importedFilePath = ImportResolver.ResolveImport(import, absolutePath, ExtraSearchRoots());
                    if (importedFilePath == null)
                    {
                        _diagnostics.AddError($"Imported file not found: {import}", 0, 0);
                        continue;
                    }
                    if (CompiledReferenceLoader.IsCompiledReference(importedFilePath))
                        LoadCompiledReference(importedFilePath, fullProgram);
                    else
                        LoadFile(importedFilePath, fullProgram);
                }

                // Namespace imports (`import ovh.finite.hello.Terminal;`) also
                // map to files by location (dots → directory separators), like
                // Python modules. Stdlib-only namespace imports simply have no
                // file, which is fine — they still register for name resolution.
                foreach (var ns in program.NamespaceImports)
                {
                    string? nsFile = ImportResolver.ResolveNamespace(ns, absolutePath, ExtraSearchRoots());
                    if (nsFile == null) continue;
                    if (CompiledReferenceLoader.IsCompiledReference(nsFile))
                        LoadCompiledReference(nsFile, fullProgram);
                    else
                        LoadFile(nsFile, fullProgram);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.AddError($"Error loading file {filePath}: {ex.Message}", 0, 0);
            }
        }

        /// <summary>
        /// Loads a compiled module (.orbt / .oil / .oir) as a static reference:
        /// its types are synthesized into the program for analysis, and the raw
        /// module body is retained for the codegen to link into the output.
        /// </summary>
        private void LoadCompiledReference(string filePath, Program fullProgram)
        {
            string absolutePath = ImportResolver.NormalizeAbsolutePath(filePath);
            if (_loadedFiles.Contains(absolutePath)) return;
            _loadedFiles.Add(absolutePath);

            if (!File.Exists(absolutePath))
            {
                _diagnostics.AddError($"Imported module not found: {filePath}", 0, 0);
                return;
            }

            try
            {
                var module = CompiledReferenceLoader.ParseModule(absolutePath);
                CompiledReferenceLoader.Synthesize(module, fullProgram);
            }
            catch (Exception ex)
            {
                _diagnostics.AddError($"Error loading module {filePath}: {ex.Message}", 0, 0);
            }
        }
    }
}
