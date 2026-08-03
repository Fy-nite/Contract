using System;
using System.IO;
using System.Linq;
using Contract.Compiler.Parsing;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;
using Contract.Compiler.StandardLibrary.Builtins;

namespace Contract.Cli
{
    public class TestRunner
    {
        public static void RunTests()
        {
            Console.WriteLine("\u001b[1;34mRunning Compiler Test Suite...\u001b[0m\n");

            int passed = 0;
            int total = 0;

            var successTests = Directory.Exists("tests/success") ? Directory.GetFiles("tests/success", "*.ct") : Array.Empty<string>();
            var failureTests = Directory.Exists("tests/failure") ? Directory.GetFiles("tests/failure", "*.ct") : Array.Empty<string>();

            foreach (var test in successTests)
            {
                total++;
                if (RunSingleTest(test, shouldPass: true)) passed++;
            }

            foreach (var test in failureTests)
            {
                total++;
                if (RunSingleTest(test, shouldPass: false)) passed++;
            }

            Console.WriteLine($"\n\u001b[1mTest Results: {passed}/{total} passed\u001b[0m");
            
            if (passed < total)
            {
                Environment.Exit(1);
            }
        }

        private static bool RunSingleTest(string path, bool shouldPass)
        {
            string source = File.ReadAllText(path);
            var diagnostics = new DiagnosticBag { SourceCode = source };
            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Parser(tokens, diagnostics);

            try
            {
                var program = parser.Parse();

                // Semantic analysis, mirroring the full compile pipeline, so
                // failure tests can cover analyzer errors (unknown attribute,
                // inheritance cycles, ...).
                if (!diagnostics.HasErrors)
                {
                    var symbolTable = new SymbolTable();
                    symbolTable.RegisterAssembly(typeof(IO).Assembly);
                    StdlibCatalog.RegisterInto(symbolTable);
                    var analyzer = new SemanticAnalyzer(symbolTable, diagnostics);
                    analyzer.Analyze(program);
                }
            }
            catch (Exception ex)
            {
                diagnostics.AddError($"Critical parser crash: {ex.Message}", 0, 0);
            }

            bool failed = diagnostics.HasErrors;
            bool success = (shouldPass && !failed) || (!shouldPass && failed);

            string status = success ? "\u001b[32m[PASS]\u001b[0m" : "\u001b[31m[FAIL]\u001b[0m";
            string type = shouldPass ? "(Expect Success)" : "(Expect Failure)";
            
            Console.WriteLine($"{status} {path} {type}");
            
            if (!success)
            {
                if (shouldPass && failed)
                {
                    Console.WriteLine("\u001b[33mUnexpected errors found:\u001b[0m");
                    diagnostics.ReportToConsole();
                }
                else if (!shouldPass && !failed)
                {
                    Console.WriteLine("\u001b[33mExpected errors but none were found.\u001b[0m");
                }
            }

            return success;
        }
    }
}
