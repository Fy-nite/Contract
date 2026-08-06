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
        /// Search roots for Python-style namespace imports, after the importing
        /// file's own directory: the main file's directory, then the CWD.
        /// </summary>
        private IEnumerable<string> ExtraSearchRoots()
        {
            if (!string.IsNullOrEmpty(_mainFileDir)) yield return _mainFileDir;
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
