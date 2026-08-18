using System;
using System.IO;
using System.Linq;
using Contract.LanguageServer;
using Contract.Runtime;
using ObjectRT.Runtime;

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

            if (args.Length > 0 && args[0] == "new")
            {
                // Scaffold a project: ccl new [name] [--type exe|lib] [--namespace ns]
                return NewProject(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "build")
            {
                // Build the project in the current directory: ccl build [--run] [--output path]
                return BuildProject(args.Skip(1).ToArray());
            }

            var compileOnly = false;
            var debug = false;
            var verbose = false;
            var bundle = false;
            var singleFile = false;
            var jit = false;
            string? outPath = null;
            string? format = null;
            string? methodCall = null;
            string? bindAssembly = null;
            string? hostTypeName = null;
            string? emitDir = null;
            string? cacheDir = null;
            var emitCallgraph = false;
            var rids = new System.Collections.Generic.List<string>();
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
                    case "-b" or "--bundle": bundle = true; break;
                    case "-d" or "--debug": debug = true; break;
                    case "-v" or "--verbose": verbose = true; break;
                    case "--single-file": singleFile = true; break;
                    case "--host":
                        if (++i >= args.Length) { Error("--host requires a type full name (e.g. Contract.Runtime.ContractRuntime)"); return 1; }
                        hostTypeName = args[i]; break;
                    case "--rid":
                        if (++i >= args.Length) { Error("--rid requires a value (comma-separated)"); return 1; }
                        rids.AddRange(args[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
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
                    case "--jit": jit = true; break;
                    case "--emit":
                        if (++i >= args.Length) { Error("--emit requires a directory path"); return 1; }
                        emitDir = args[i]; break;
                    case "--cache":
                        if (++i >= args.Length) { Error("--cache requires a directory path"); return 1; }
                        cacheDir = args[i]; break;
                    case "--emit-callgraph": emitCallgraph = true; break;
                    case "run":
                        break; // subcommand marker; the remaining positional is the file
                    case "bundle":
                        bundle = true;
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

                // JIT / emit / cache options
                if (jit) rt.Inner.Mode = JitMode.Reflection;
                if (emitDir != null) { ObjectRT.Runtime.Runtime.EmitDir = emitDir; Directory.CreateDirectory(emitDir); }
                if (cacheDir != null) { ObjectRT.Runtime.Runtime.CacheDir = cacheDir; Directory.CreateDirectory(cacheDir); }
                var callCounts = emitCallgraph ? new System.Collections.Concurrent.ConcurrentDictionary<string, long>() : null;
                if (callCounts != null) rt.Inner.CallCounts = callCounts;
                System.Reflection.Assembly? bindingAsm = null;
                if (bindAssembly != null)
                {
                    if (!File.Exists(bindAssembly)) { Error($"Binding assembly not found: {bindAssembly}"); return 1; }
                    bindingAsm = System.Reflection.Assembly.LoadFrom(bindAssembly);
                    rt.RegisterBindingAssembly(bindingAsm);
                    if (verbose) Console.Error.WriteLine($"; Loaded bindings from {bindAssembly}");
                }

                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                // ── Bundle: wrap in a standalone executable ──────────────
                if (bundle)
                {
                    Type hostType = ResolveHostType(hostTypeName) ?? typeof(ContractRuntime);

                    // Compile to .orbt bytes: use the module as-is for
                    // precompiled input, or compile the .ct source first.
                    byte[] orbtBytes;
                    if (ext is ".orbt" or ".oil" or ".oir")
                    {
                        orbtBytes = new ObjektRT.Core.Serialization.ORBTWriter()
                            .WriteModule(rt.LoadModuleFileAuto(filePath));
                    }
                    else
                    {
                        var compiled = ContractCompiler.CompileFileToBinary(filePath, out var bundleDiags,
                            bindingAsm != null ? new[] { bindingAsm } : null);
                        if (compiled == null)
                        {
                            bundleDiags.ReportToConsole();
                            return 1;
                        }
                        bundleDiags.ReportWarningsToConsole();
                        orbtBytes = compiled;
                    }

                    // Metadata-driven validation: every module the program
                    // imports must be provided by the stdlib or a --bind
                    // assembly, or the bundle would fail at runtime.
                    var module = ObjektRT.Core.Serialization.OrbtFileReader.ReadBytes(orbtBytes);
                    var required = BundleDriver.RequiredBindingModules(module);
                    if (verbose)
                        Console.Error.WriteLine($"; module imports: {string.Join(", ", required)}");
                    var missing = required
                        .Where(name => !StdlibModuleNames.Contains(name))
                        .ToList();
                    if (missing.Count > 0 && bindAssembly == null)
                    {
                        Error($"module imports binding module(s) [{string.Join(", ", missing)}] — pass --bind <assembly> providing them");
                        return 1;
                    }

                    var spec = new BundleSpec
                    {
                        HostType = hostType,
                        BindingAssemblyPaths = bindAssembly != null ? new[] { bindAssembly } : Array.Empty<string>(),
                        Rids = rids,
                        SingleFile = singleFile,
                    };
                    return BundleDriver.Bundle("ccl", filePath, orbtBytes, outPath, spec, verbose);
                }

                // ── Run precompiled modules (.orbt / .oil / .oir) ─────────
                if (ext is ".orbt" or ".oil" or ".oir")
                {
                    if (verbose) Console.Error.WriteLine($"; Running {filePath}");
                    var module = rt.LoadModuleFileAuto(filePath);
                    if (methodCall != null)
                    {
                        rt.PrepareModule(module);
                        rt.LoadModule(module);
                        var result = rt.CallMethod<object?>(methodCall);
                        PrintResult(result);
                    }
                    else
                    {
                        rt.RunModule(module);
                    }
                    if (callCounts != null) EmitCallgraph(callCounts);
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
                diagnostics.ReportWarningsToConsole();
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
                        var bytes = new ObjektRT.Core.Serialization.ORBTWriter().WriteModule(module);
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
                    rt.PrepareModule(runModule);
                    rt.LoadModule(runModule);
                    var result = rt.CallMethod<object?>(methodCall);
                    PrintResult(result);
                }
                else
                {
                    rt.RunModule(runModule);
                }
                if (callCounts != null) EmitCallgraph(callCounts);
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

        /// <summary>Module names provided by the Contract standard library —
        /// the bundle-time counterpart of <c>ContractRuntime.RegisterDefaultBindings()</c>.</summary>
        static readonly System.Collections.Generic.HashSet<string> StdlibModuleNames = new(StringComparer.Ordinal)
        {
            "IO", "String", "Math", "Convert", "Random", "List", "Dict", "Array",
            "File", "Environment", "GC", "Debug", "Time", "Thread", "Reflect",
            "ObjektRT.Stdlib.System.IO", "ObjektRT.Stdlib.Math.Numbers",
            "ObjektRT.Stdlib.Threading.Thread", "ObjektRT.Stdlib.Generics.Array",
        };

        /// <summary>Resolves a host runtime type name across loaded assemblies
        /// (including ones loaded via <c>--bind</c>), or null when not found.</summary>
        static Type? ResolveHostType(string? name)
        {
            if (name == null) return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Dumps a sorted call-frequency table to stderr.</summary>
        static void EmitCallgraph(System.Collections.Concurrent.ConcurrentDictionary<string, long> counts)
        {
            var total = counts.Values.Sum();
            var sorted = counts.OrderByDescending(kv => kv.Value);
            Console.Error.WriteLine();
            Console.Error.WriteLine("; ── Call Graph (" + total + " total entries) ──────────");
            foreach (var kv in sorted)
            {
                var pct = total > 0 ? (kv.Value * 100.0 / total) : 0;
                Console.Error.WriteLine($"; {kv.Value,8}  ({pct,5:F1}%)  {kv.Key}");
            }
            Console.Error.WriteLine("; ───────────────────────────────────────────────");
        }

        static void Help()
        {
            Console.WriteLine("""
Contract CLI — compile and run Contract programs.

Usage:
  contract <file.ct> [options]          Compile and run in one go
  contract -c <file.ct> [-o out]        Compile only (default output .orbt)
  contract run <file.orbt|oil|oir>      Run a precompiled module
  contract bundle <file> [options]      Compile and wrap in a standalone executable
  contract new [name] [options]         Scaffold a project (creates contract.ctproj + src/)
  contract build [options]              Build the project in the current directory
  contract lsp [--trace]                Run the language server (LSP over stdio)
  contract --test                       Run the compiler test suite

Options:
  -o, --output <path>    Output path; extension drives format (.oil = text, .orbt = binary)
  -f, --format <fmt>     Force output format: oil | orbt (default orbt)
  -c, --compile          Compile only, do not run
  -b, --bundle           Bundle into a standalone executable
  -m, --method <name>    Call a specific method (e.g. "Program.Main")
  -v, --verbose          Show pipeline stages
  -d, --debug            Print the generated IR
      --bind <assembly>  Load custom host bindings from a .dll (see Contract.Runtime)
      --host <type>      Runtime host type full name (default Contract.Runtime.ContractRuntime);
                         must be public, new-able, and implement IHostedRuntime
      --jit              Use the reflection JIT backend (Roslyn C# emit, not the interpreter)
      --emit <dir>       Write JIT-generated C# source to <dir> (requires --jit)
      --cache <dir>      Cache JIT-compiled DLLs to <dir> (requires --jit)
      --emit-callgraph   Print per-method call frequency after execution
      --rid <list>       RIDs for self-contained publish, comma-separated (e.g. win-x64,linux-x64)
      --single-file      With --rid, publish as a single-file executable
      --version          Print the compiler version
  -h, --help             Show this message

Project commands:
  contract new myapp [--type exe|lib] [--namespace com.example]
                         Create myapp/ with contract.ctproj + src/main.ct
  contract build [--run] [--output path]
                         Read ./contract.ctproj, compile the main file
                         (exe → .orbt binary; lib → .oil text module)

Examples:
  contract hello.ct                      Compile + run
  contract -c hello.ct -o hello.orbt     Compile to binary (default)
  contract -c hello.ct -f oil            Compile to .oil text
  contract run hello.orbt                Run precompiled binary
  contract run hello.oir                 Run precompiled text
  contract -m Program.Main app.ct        Compile + call a specific method
  contract bundle app.ct                 Bundle (framework-dependent .exe, needs .NET installed)
  contract bundle app.ct --bind myhost.dll
  contract bundle app.ct --rid win-x64 --single-file
""");
        }

        /// <summary>Scaffolds a new project: `ccl new [name] [--type exe|lib] [--namespace ns]`.</summary>
        static int NewProject(string[] args)
        {
            string? name = null;
            string type = "exe";
            string? ns = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--type":
                        if (++i >= args.Length) { Error("--type requires exe|lib"); return 1; }
                        type = args[i].ToLowerInvariant();
                        if (type is not ("exe" or "lib")) { Error("--type must be exe or lib"); return 1; }
                        break;
                    case "--namespace":
                        if (++i >= args.Length) { Error("--namespace requires a dotted name"); return 1; }
                        ns = args[i];
                        break;
                    case "-h" or "--help": Help(); return 0;
                    default:
                        if (args[i].StartsWith('-')) { Error($"Unknown option: {args[i]}"); return 1; }
                        name = args[i];
                        break;
                }
            }

            string root = name ?? Path.GetFileName(Directory.GetCurrentDirectory());
            string fullRoot = Path.GetFullPath(root);
            if (Directory.Exists(fullRoot) && Directory.EnumerateFileSystemEntries(fullRoot).Any())
            {
                Error($"Directory '{root}' already exists and is not empty.");
                return 1;
            }
            Directory.CreateDirectory(Path.Combine(fullRoot, "src"));
            Directory.CreateDirectory(Path.Combine(fullRoot, "bin"));

            var project = new Contract.Compiler.ContractProject
            {
                Name = name ?? Path.GetFileName(fullRoot),
                Type = type,
                Namespace = ns,
            };
            project.Save(fullRoot);

            // Default namespace: the explicit one, else the project name lowercased.
            string defaultNs = ns ?? project.Name.ToLowerInvariant();
            string mainBody = type == "lib"
                ? $"// Library project — no entry point required.\n" +
                  $"namespace {defaultNs};\n\n" +
                  $"Contract Greeter {{\n" +
                  $"    static fn Greet(who: string) -> string {{\n" +
                  $"        return \"Hello, \" + who + \"!\";\n" +
                  $"    }}\n" +
                  $"}}\n"
                : $"namespace {defaultNs};\n\n" +
                  $"Contract Program {{\n" +
                  $"    static fn Main() {{\n" +
                  $"        IO.Println(\"Hello from {project.Name}!\");\n" +
                  $"    }}\n" +
                  $"}}\n";
            File.WriteAllText(Path.Combine(fullRoot, "src", "main.ct"), mainBody);

            File.WriteAllText(Path.Combine(fullRoot, "README.md"),
                $"# {project.Name}\n\nA Contract project (`{project.Type}`).\n\n- `ccl build` to compile\n- `ccl build --run` to compile and run\n");
            Console.WriteLine($"Created project '{project.Name}' ({project.Type}) at {fullRoot}");
            Console.WriteLine($"  contract.ctproj  — project settings (edit type/namespace/main)");
            Console.WriteLine($"  src/main.ct      — source");
            Console.WriteLine("Next: cd into it and run `ccl build --run`");
            return 0;
        }

        /// <summary>Builds the project in the current directory: `ccl build [--run] [--output path]`.</summary>
        static int BuildProject(string[] args)
        {
            bool run = false;
            string? output = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--run": run = true; break;
                    case "-o" or "--output":
                        if (++i >= args.Length) { Error("--output requires a path"); return 1; }
                        output = args[i];
                        break;
                    default:
                        if (args[i].StartsWith("--output=")) { output = args[i].Substring("--output=".Length); }
                        else if (args[i].StartsWith('-')) { Error($"Unknown option: {args[i]}"); return 1; }
                        break;
                }
            }

            Contract.Compiler.ContractProject? project;
            try
            {
                project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory());
            }
            catch (FormatException ex)
            {
                Error(ex.Message);
                return 1;
            }
            if (project == null)
            {
                Error($"No project found — '{Contract.Compiler.ContractProject.FileName}' not found in {Directory.GetCurrentDirectory()} (run `ccl new` first).");
                return 1;
            }
            if (project.MainPath == null || !File.Exists(project.MainPath))
            {
                Error($"Project main file not found: {project.Main}");
                return 1;
            }

            // Library builds (type "lib") don't require a Main entry point; the
            // analyzer gates the "no Main" info and unused-declaration warnings
            // on the isExecutable flag.
            var ir = Contract.Runtime.ContractCompiler.CompileFile(
                project.MainPath, out var diagnostics,
                isExecutable: project.IsExecutable);
            if (ir == null)
            {
                diagnostics.ReportToConsole();
                return 1;
            }
            diagnostics.ReportWarningsToConsole();

            string outDir = output != null
                ? (Path.IsPathRooted(output) ? output : Path.Combine(project.RootPath!, output))
                : (project.OutputPath ?? Path.Combine(project.RootPath!, "bin"));
            Directory.CreateDirectory(outDir);
            string outFile = Path.Combine(outDir,
                Path.GetFileNameWithoutExtension(project.Main) + (project.IsExecutable ? ".orbt" : ".oil"));
            if (project.IsExecutable)
            {
                var module = ObjektRT.Core.Parsing.OilFileReader.ParseString(ir);
                var bytes = new ObjektRT.Core.Serialization.ORBTWriter().WriteModule(module);
                File.WriteAllBytes(outFile, bytes);
            }
            else
            {
                File.WriteAllText(outFile, ir);
            }

            Console.WriteLine($"[{project.Type}] {project.Name} → {outFile}");
            if (run && project.IsExecutable)
            {
                try
                {
                    var rt = new Contract.Runtime.ContractRuntime();
                    rt.RunModule(rt.LoadModuleFileAuto(outFile));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: {ex.Message}");
                    return 1;
                }
            }
            else if (run)
            {
                Console.WriteLine("(library project — nothing to run)");
            }
            return 0;
        }
    }
}
