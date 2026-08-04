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
            var fullProgram = new Program(1, 1);
            LoadFile(mainFilePath, fullProgram);
            return fullProgram;
        }

        private void LoadFile(string filePath, Program fullProgram)
        {
            string absolutePath = Path.GetFullPath(filePath);
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

                // Recursively load imports. A compiled reference (.orbt/.oil/.oir)
                // is loaded as a DLL-style include; anything else is a .ct source
                // file import.
                string directory = Path.GetDirectoryName(absolutePath) ?? "";
                foreach (var import in program.Imports)
                {
                    string importedFilePath = Path.Combine(directory, import);
                    if (CompiledReferenceLoader.IsCompiledReference(importedFilePath))
                        LoadCompiledReference(importedFilePath, fullProgram);
                    else
                        LoadFile(importedFilePath, fullProgram);
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
            string absolutePath = Path.GetFullPath(filePath);
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
