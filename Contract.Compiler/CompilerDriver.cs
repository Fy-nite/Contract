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

        public CompilerDriver(DiagnosticBag diagnostics)
        {
            _diagnostics = diagnostics;
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

            if (!File.Exists(absolutePath))
            {
                _diagnostics.AddError($"Imported file not found: {filePath}", 0, 0);
                return;
            }

            try
            {
                string source = File.ReadAllText(absolutePath);
                var lexer = new Lexer(source, _diagnostics);
                var tokens = lexer.Tokenize().ToList();

                var parser = new Parser(tokens, _diagnostics);
                var program = parser.Parse();

                // Merge into full program
                foreach (var contract in program.Contracts)
                    fullProgram.Contracts.Add(contract);
                foreach (var func in program.Functions)
                    fullProgram.Functions.Add(func);

                // Recursively load imports
                string directory = Path.GetDirectoryName(absolutePath) ?? "";
                foreach (var import in program.Imports)
                {
                    string importedFilePath = Path.Combine(directory, import);
                    LoadFile(importedFilePath, fullProgram);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.AddError($"Error loading file {filePath}: {ex.Message}", 0, 0);
            }
        }
    }
}
