using System;
using System.IO;
using System.Linq;
using Contract.LanguageServer;
using Contract.Runtime;

namespace Contract.Cli
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Help();
                return 1;
            }

            if (args.Length == 1 && args[0] == "--test")
            {
                TestRunner.RunTests();
                return 0;
            }

            if (args.Length > 0 && args[0] == "lsp")
            {
                // Host the language server over stdio: ccl lsp [--trace]
                return await ServerMain.RunLsp(args.Contains("--trace", StringComparer.OrdinalIgnoreCase));
            }

            var compileOnly = false;
            var debug = false;
            var verbose = false;
            string? outPath = null;
            string? format = null;
            string? methodCall = null;
            string? bindAssembly = null;
            var files = new System.Collections.Generic.List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h" or "--help": Help(); return 0;
                    case "--version":
                        var version = typeof(Program).Assembly.GetName().Version;
                        Console.WriteLine($"Contract Compiler {(version != null ? version.ToString(3) : "unknown")} (ccl)");
                        return 0;
                    case "-c" or "--compile": compileOnly = true; break;
                    case "-d" or "--debug": debug = true; break;
                    case "-v" or "--verbose": verbose = true; break;
                    case "-o" or "--output":
                        if (++i >= args.Length) { Error("--output requires a path"); return 1; }
                        outPath = args[i]; break;
                    case "-f" or "--format":
                        if (++i >= args.Length) { Error("--format requires oil|orbt"); return 1; }
                        format = args[i].ToLowerInvariant();
                        if (format is not ("oil" or "orbt")) { Error($"Unknown format '{format}' (expected oil|orbt)"); return 1; }
                        break;
                    case "-m" or "--method":
                        if (++i >= args.Length) { Error("--method requires a name"); return 1; }
                        methodCall = args[i]; break;
                    case "--bind":
                        if (++i >= args.Length) { Error("--bind requires an assembly path"); return 1; }
                        bindAssembly = args[i]; break;
                    case "run":
                        break; // subcommand marker; the remaining positional is the file
                    default:
                        if (args[i].StartsWith('-')) { Error($"Unknown option: {args[i]}"); return 1; }
                        files.Add(args[i]); break;
                }
            }

            if (files.Count == 0) { Error("No input file."); Help(); return 1; }
            var filePath = files[0];

            try
            {
                var rt = new ContractRuntime();
                System.Reflection.Assembly? bindingAsm = null;
                if (bindAssembly != null)
                {
                    if (!File.Exists(bindAssembly)) { Error($"Binding assembly not found: {bindAssembly}"); return 1; }
                    bindingAsm = System.Reflection.Assembly.LoadFrom(bindAssembly);
                    rt.RegisterBindingAssembly(bindingAsm);
                    if (verbose) Console.Error.WriteLine($"; Loaded bindings from {bindAssembly}");
                }

                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                // ── Run precompiled modules (.orbt / .oil / .oir) ─────────
                if (ext is ".orbt" or ".oil" or ".oir")
                {
                    if (verbose) Console.Error.WriteLine($"; Running {filePath}");
                    var module = rt.LoadModuleFileAuto(filePath);
                    if (methodCall != null)
                    {
                        var result = rt.CallMethod<object?>(methodCall);
                        PrintResult(result);
                    }
                    else
                    {
                        rt.RunModule(module);
                    }
                    return 0;
                }

                // ── Compile .ct source ────────────────────────────────────
                if (verbose) Console.Error.WriteLine($"; Compiling {filePath}");
                var ir = ContractCompiler.CompileFile(filePath, out var diagnostics,
                    bindingAsm != null ? new[] { bindingAsm } : null);
                if (ir == null)
                {
                    diagnostics.ReportToConsole();
                    return 1;
                }
                if (debug)
                {
                    Console.WriteLine(ir);
                }

                // Compile only → write .oil / .orbt (format from -f or -o extension; default orbt).
                if (compileOnly || outPath != null)
                {
                    var (outFile, outFormat) = ResolveOutput(filePath, outPath, format);
                    if (outFormat == "oil")
                    {
                        File.WriteAllText(outFile, ir);
                    }
                    else
                    {
                        var module = rt.LoadTextModule(ir);
                        var bytes = new ObjectRT.Reader.ORBTWriter().WriteModule(module);
                        File.WriteAllBytes(outFile, bytes);
                    }
                    Console.WriteLine(outFile);
                    return 0;
                }

                // ── One-shot: compile then run ───────────────────────────
                if (verbose) Console.Error.WriteLine($"; Running compiled module");
                var runModule = rt.LoadTextModule(ir);
                if (methodCall != null)
                {
                    var result = rt.CallMethod<object?>(methodCall);
                    PrintResult(result);
                }
                else
                {
                    rt.RunModule(runModule);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                return 1;
            }
        }

        /// <summary>Picks the output path and format. Format: -f flag, else the -o extension (.oil = text), else default .orbt.</summary>
        static (string Path, string Format) ResolveOutput(string inputPath, string? outPath, string? format)
        {
            var outFile = outPath ?? Path.ChangeExtension(inputPath, format == "oil" ? ".oil" : ".orbt");
            if (!outFile.EndsWith(".oil", StringComparison.OrdinalIgnoreCase)
                && !outFile.EndsWith(".orbt", StringComparison.OrdinalIgnoreCase))
            {
                outFile += format == "oil" ? ".oil" : ".orbt";
            }
            var outFormat = format ?? (outFile.EndsWith(".oil", StringComparison.OrdinalIgnoreCase) ? "oil" : "orbt");
            return (outFile, outFormat);
        }

        static void PrintResult(object? result)
        {
            Console.WriteLine(result switch
            {
                null => "null",
                int i => i.ToString(),
                bool b => b ? "True" : "False",
                _ => result.ToString()
            });
        }

        static void Error(string msg) => Console.Error.WriteLine($"Error: {msg}");

        static void Help()
        {
            Console.WriteLine("""
Contract CLI — compile and run Contract programs.

Usage:
  contract <file.ct> [options]          Compile and run in one go
  contract -c <file.ct> [-o out]        Compile only (default output .orbt)
  contract run <file.orbt|oil|oir>      Run a precompiled module
  contract lsp [--trace]                Run the language server (LSP over stdio)
  contract --test                       Run the compiler test suite

Options:
  -o, --output <path>    Output path; extension drives format (.oil = text, .orbt = binary)
  -f, --format <fmt>     Force output format: oil | orbt (default orbt)
  -c, --compile          Compile only, do not run
  -m, --method <name>    Call a specific method (e.g. "Program.Main")
  -v, --verbose          Show pipeline stages
  -d, --debug            Print the generated IR
      --bind <assembly>  Load custom host bindings from a .dll (see Contract.Runtime)
      --version          Print the compiler version
  -h, --help             Show this message

Examples:
  contract hello.ct                      Compile + run
  contract -c hello.ct -o hello.orbt     Compile to binary (default)
  contract -c hello.ct -f oil            Compile to .oil text
  contract run hello.orbt                Run precompiled binary
  contract run hello.oir                 Run precompiled text
  contract -m Program.Main app.ct        Compile + call a specific method
""");
        }
    }
}
