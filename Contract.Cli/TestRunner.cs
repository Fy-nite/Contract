using System;
using System.IO;
using System.Linq;
using Contract.Compiler.Parsing;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;

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
            var diagnostics = new DiagnosticBag();
            string? source = null;

            try
            {
                // Full pipeline: lex → parse → analyze → codegen, through the
                // CompilerDriver so file imports (`import "other.ct";`) resolve
                // relative to the test file — mirroring the real CLI.
                var driver = new Contract.Compiler.CompilerDriver(diagnostics);
                var program = driver.Compile(path);
                if (!diagnostics.HasErrors)
                {
                    var symbolTable = new SymbolTable();
                    // The stdlib is the generic ObjektRT.Stdlib; Reflect is
                    // the one Contract-specific [ClassBinding] module.
                    symbolTable.RegisterAssembly(typeof(ReflectModule).Assembly);
                    StdlibCatalog.RegisterInto(symbolTable);
                    var analyzer = new SemanticAnalyzer(symbolTable, diagnostics);
                    analyzer.Analyze(program);
                }
                if (!diagnostics.HasErrors)
                {
                    var codeGenerator = new Contract.Compiler.CodeGen.IRCodeGenerator(diagnostics);
                    codeGenerator.Generate(program);
                }
                source = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                diagnostics.AddError($"Critical parser crash: {ex.Message}", 0, 0);
            }

            if (source != null)
                diagnostics.SourceCode = source;

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
