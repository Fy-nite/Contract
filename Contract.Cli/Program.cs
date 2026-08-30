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

            if (args.Length > 0 && args[0] == "doc")
            {
                // Generate API documentation: ccl doc [--format html|md] [--output dir] [--title title]
                return GenerateDocs(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "install")
            {
                return InstallPackage(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "remove")
            {
                return RemovePackage(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "search")
            {
                return SearchPackages(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "list")
            {
                return ListPackages(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "project")
            {
                return ProjectCommand(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "debug")
            {
                return DebugProject(args.Skip(1).ToArray());
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
            var listImports = false;
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
                    case "--list-imports": listImports = true; break;
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

                // Propagate source directory for assembly imports
                var sourceDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
                rt.SourceDir = sourceDir;

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

                // ── --list-imports: enumerate available CLR types ─────
                if (listImports)
                {
                    Console.WriteLine($"CLR types available for <ClrImport> (source: {sourceDir}):");
                    Console.WriteLine();
                    // 1. BCL types already loaded in the process
                    var bclTypes = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic)
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .Where(t => t.IsPublic && t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length > 0)
                        .OrderBy(t => t.FullName)
                        .ToList();
                    foreach (var t in bclTypes)
                    {
                        var methods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                            .Select(m => m.Name).Distinct().OrderBy(n => n);
                        Console.WriteLine($"  {t.FullName,-50} [{string.Join(", ", methods)}]");
                    }
                    // 2. Project-local DLLs in source directory
                    if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
                    {
                        foreach (var dll in Directory.GetFiles(sourceDir, "*.dll"))
                        {
                            try
                            {
                                var asm = System.Reflection.Assembly.LoadFrom(dll);
                                var types = asm.GetTypes()
                                    .Where(t => t.IsPublic && t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length > 0)
                                    .OrderBy(t => t.FullName)
                                    .ToList();
                                if (types.Count > 0)
                                {
                                    Console.WriteLine();
                                    Console.WriteLine($"  --- {Path.GetFileName(dll)} ---");
                                    foreach (var t in types)
                                    {
                                        var methods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                            .Select(m => m.Name).Distinct().OrderBy(n => n);
                                        Console.WriteLine($"  {t.FullName,-50} [{string.Join(", ", methods)}]");
                                    }
                                }
                            }
                            catch { /* skip unresolvable assemblies */ }
                        }
                    }
                    return 0;
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
                        rt.RunModule(module, files.Skip(1).ToArray());
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
                    rt.RunModule(runModule, files.Skip(1).ToArray());
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

        static int ErrorReturn(string msg) { Error(msg); return 1; }

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
  contract debug [file.ct]              Run the debug adapter (DAP over stdio)
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
  contract build [--run] [--static] [--output path]
                          Read ./contract.ctproj, compile the main file
                          (always emits a .orbt binary module)
                         --static resolves imports at build time
  contract doc [--format html|md]  Generate API docs from /// comments
  contract install <pkg[@ver]>    Install a package from the Purr registry
  contract remove <pkg>           Remove an installed package
  contract search <query>         Search the Purr registry
  contract list                   List installed packages
  contract debug [file.ct]        Launch DAP debug server for VSCode

Examples:
  contract hello.ct                      Compile + run
  contract hello.ct a b c                Compile + run, passing a b c to Main(string[] args)
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

        /// <summary>Scaffolds a new project: `ccl new [name] [--type exe|lib] [--namespace ns] [--version ver]`.</summary>
        static int NewProject(string[] args)
        {
            string? name = null;
            string type = "exe";
            string? ns = null;
            string? version = null;
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
                    case "--version":
                        if (++i >= args.Length) { Error("--version requires a semver string"); return 1; }
                        version = args[i];
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
                Version = version ?? "0.1.0",
                Description = $"A Contract {(type == "lib" ? "library" : "application")}",
                Tags = new() { type == "lib" ? "library" : "application" }
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

        /// <summary>Builds the project in the current directory: `ccl build [--run] [--static] [--output path]`.</summary>
        static int BuildProject(string[] args)
        {
            bool run = false;
            bool staticLink = false;
            string? output = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--run": run = true; break;
                    case "--static": staticLink = true; break;
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

            // ── Route to the correct build mode ───────────────────────
            if (project.Projects != null && project.Projects.Count > 0)
                return BuildSolution(project, staticLink, output);

            if (project.Sources != null && project.Sources.Count > 0)
                return BuildGlobCompile(project, staticLink, output);

            // Single-file mode (existing behavior)
            return BuildSingleFile(project, staticLink, run, output);
        }

        /// <summary>Multi-project solution build: `ccl build` with a Projects array.</summary>
        static int BuildSolution(Contract.Compiler.ContractProject project, bool staticLink, string? output)
        {
            Console.WriteLine($"Solution: {project.Name} ({project.Projects!.Count} project(s))");
            try
            {
                var result = Contract.Compiler.SolutionBuilder.Build(project, staticLink, output);
                if (!result.Success)
                {
                    Error("Solution build failed.");
                    return 1;
                }
                Console.WriteLine($"Build succeeded — {result.Projects.Count} project(s)");
                return 0;
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                return 1;
            }
        }

        /// <summary>Glob-compile mode: `ccl build` with a Sources array.</summary>
        static int BuildGlobCompile(Contract.Compiler.ContractProject project, bool staticLink, string? output)
        {
            bool hasMain = project.MainPath != null && File.Exists(project.MainPath);
            if (!hasMain && project.IsExecutable)
            {
                Error($"Project main file not found: {project.Main}");
                return 1;
            }

            // Expand globs
            var sourceFiles = new List<string>();
            foreach (var pattern in project.Sources!)
            {
                string dir = project.RootPath!;
                string searchPattern = Path.GetFileName(pattern);
                string? searchDir = Path.GetDirectoryName(pattern);

                if (!string.IsNullOrEmpty(searchDir))
                    dir = Path.Combine(dir, searchDir.Replace('/', Path.DirectorySeparatorChar));

                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, searchPattern, SearchOption.AllDirectories);
                    sourceFiles.AddRange(files.Where(f => f.EndsWith(".ct", StringComparison.OrdinalIgnoreCase)));
                }
            }

            if (sourceFiles.Count == 0)
            {
                Error("No source files matched the Sources globs.");
                return 1;
            }

            Console.WriteLine($"Glob compile: {sourceFiles.Count} file(s)");

            var diagnostics = new Contract.Compiler.Diagnostics.DiagnosticBag();
            var symbolTable = new Contract.Compiler.StandardLibrary.SymbolTable();
            Contract.Compiler.StandardLibrary.StdlibCatalog.RegisterInto(symbolTable);

            var driver = new Contract.Compiler.CompilerDriver(diagnostics);
            var program = hasMain
                ? driver.Compile(project.MainPath!, sourceFiles)
                : driver.Compile(sourceFiles, project.RootPath!);

            if (diagnostics.HasErrors)
            {
                diagnostics.ReportToConsole();
                return 1;
            }
            diagnostics.ReportWarningsToConsole();

            string? mainRef = hasMain ? project.MainPath : null;
            var analyzer = new Contract.Compiler.Semantics.SemanticAnalyzer(symbolTable, diagnostics, mainRef, project.IsExecutable);
            analyzer.Analyze(program);
            if (diagnostics.HasErrors)
            {
                diagnostics.ReportToConsole();
                return 1;
            }

            var codeGen = new Contract.Compiler.CodeGen.IRCodeGenerator(diagnostics);
            codeGen.Generate(program);
            if (diagnostics.HasErrors)
            {
                diagnostics.ReportToConsole();
                return 1;
            }

            string ir = codeGen.GetIRText();
            string outDir = output != null
                ? (Path.IsPathRooted(output) ? output : Path.Combine(project.RootPath!, output))
                : (project.OutputPath ?? Path.Combine(project.RootPath!, "bin"));
            Directory.CreateDirectory(outDir);
            string outputBase = hasMain
                ? Path.GetFileNameWithoutExtension(project.Main)
                : project.Name ?? "lib";
            string outFile = Path.Combine(outDir, outputBase + ".orbt");

            // All compiled code is emitted as a binary .orbt module.
            var module = ObjektRT.Core.Parsing.OilFileReader.ParseString(ir);
            if (project.IsExecutable && staticLink && module.Imports.Count > 0)
            {
                Console.WriteLine($"Static linking {module.Imports.Count} import(s)...");
                module = Contract.Compiler.StaticLinker.Link(module, project.RootPath!);
            }
            var bytes = new ObjektRT.Core.Serialization.ORBTWriter().WriteModule(module);
            File.WriteAllBytes(outFile, bytes);

            Console.WriteLine($"[{project.Type}] {project.Name} → {outFile}");
            return 0;
        }

        /// <summary>Single-file mode: `ccl build` with a Main entry point.</summary>
        static int BuildSingleFile(Contract.Compiler.ContractProject project, bool staticLink, bool run, string? output)
        {
            if (project.MainPath == null || !File.Exists(project.MainPath))
            {
                Error($"Project main file not found: {project.Main}");
                return 1;
            }

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
                Path.GetFileNameWithoutExtension(project.Main) + ".orbt");

            // All compiled code is emitted as a binary .orbt module.
            var module = ObjektRT.Core.Parsing.OilFileReader.ParseString(ir);
            if (project.IsExecutable && staticLink && module.Imports.Count > 0)
            {
                string? moduleDir = project.RootPath;
                Console.WriteLine($"Static linking {module.Imports.Count} import(s)...");
                module = Contract.Compiler.StaticLinker.Link(module, moduleDir!);
                Console.WriteLine($"  → {module.Types.Count} type(s), {module.Exports.Count} export(s), 0 import(s)");
            }
            var bytes = new ObjektRT.Core.Serialization.ORBTWriter().WriteModule(module);
            File.WriteAllBytes(outFile, bytes);

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

        /// <summary>Generates API documentation: `ccl doc [--format html|md] [--output dir] [--title title]`.</summary>
        static int GenerateDocs(string[] args)
        {
            string format = "html";
            string? output = null;
            string? title = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--format":
                        if (++i >= args.Length) { Error("--format requires html|md"); return 1; }
                        format = args[i].ToLowerInvariant();
                        if (format is not ("html" or "md")) { Error("--format must be html or md"); return 1; }
                        break;
                    case "-o" or "--output":
                        if (++i >= args.Length) { Error("--output requires a path"); return 1; }
                        output = args[i];
                        break;
                    case "--title":
                        if (++i >= args.Length) { Error("--title requires a string"); return 1; }
                        title = args[i];
                        break;
                    case "-h" or "--help":
                        Console.WriteLine("Usage: ccl doc [options]");
                        Console.WriteLine();
                        Console.WriteLine("Options:");
                        Console.WriteLine("  --format html|md   Output format (default: html)");
                        Console.WriteLine("  -o, --output DIR   Output directory (default: docs/)");
                        Console.WriteLine("  --title TEXT       Project title");
                        Console.WriteLine("  -h, --help         Show this help");
                        return 0;
                    default:
                        if (args[i].StartsWith('-')) { Error($"Unknown option: {args[i]}"); return 1; }
                        break;
                }
            }

            // Load project for metadata
            Contract.Compiler.ContractProject? project = null;
            try { project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory()); }
            catch (FormatException ex) { Error(ex.Message); return 1; }

            string projectRoot = project?.RootPath ?? Directory.GetCurrentDirectory();
            string mainFile = project?.MainPath ?? Path.Combine(projectRoot, "src", "main.ct");

            // Collect all .ct files in the project
            var sourceFiles = Directory.GetFiles(projectRoot, "*.ct", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.Combine("bin", "")) && !f.Contains(Path.Combine(".purr", "")))
                .ToList();

            if (sourceFiles.Count == 0)
            {
                Error("No .ct source files found in this project.");
                return 1;
            }

            Console.WriteLine($"Scanning {sourceFiles.Count} source file(s)...");

            // Extract doc comments from each file using AST parsing
            var fileDocs = new Dictionary<string, List<Contract.Compiler.Documentation.DocCommentExtractor.DocBlock>>();
            foreach (var file in sourceFiles)
            {
                string source = File.ReadAllText(file);
                string relPath = Path.GetRelativePath(projectRoot, file);
                List<Contract.Compiler.Documentation.DocCommentExtractor.DocBlock> docs;

                // Try to parse the AST for richer type info; fall back to regex on parse errors
                try
                {
                    var diags = new Contract.Compiler.Diagnostics.DiagnosticBag();
                    var lexer = new Contract.Compiler.Parsing.Lexer(source, diags, file);
                    var tokens = lexer.Tokenize();
                    var parser = new Contract.Compiler.Parsing.Parser(tokens, diags, file);
                    var program = parser.Parse();
                    docs = Contract.Compiler.Documentation.DocCommentExtractor.ExtractFromAst(program, source, relPath);
                }
                catch
                {
                    docs = Contract.Compiler.Documentation.DocCommentExtractor.ExtractFromSource(source, relPath);
                }

                // Assign namespace from the project or file path
                string ns = project?.Namespace ?? "";
                if (docs.Count > 0 && string.IsNullOrEmpty(docs[0].Namespace))
                {
                    // Try to infer namespace from file path
                    string dir = Path.GetDirectoryName(relPath) ?? "";
                    if (dir.StartsWith("src")) dir = dir.Substring(3).TrimStart(Path.DirectorySeparatorChar, '/');
                    if (!string.IsNullOrEmpty(dir))
                        ns = dir.Replace(Path.DirectorySeparatorChar, '.').Replace('/', '.');
                    foreach (var doc in docs)
                        doc.Namespace = ns;
                }

                if (docs.Count > 0)
                    fileDocs[relPath] = docs;
            }

            int totalDocs = fileDocs.Values.Sum(d => d.Count);
            if (totalDocs == 0)
            {
                Console.WriteLine("No doc comments found. Add /// comments above declarations to generate docs.");
                return 0;
            }

            Console.WriteLine($"Found {totalDocs} documented declaration(s).");

            var genOptions = new Contract.Compiler.Documentation.DocGenerator.Options
            {
                Title = title ?? project?.Name ?? "Contract API",
                OutputDir = output ?? Path.Combine(projectRoot, "docs"),
                Format = format,
                Version = project?.Version,
                Description = project?.Description
            };

            var generator = new Contract.Compiler.Documentation.DocGenerator(genOptions);
            generator.Generate(fileDocs);

            Console.WriteLine($"Documentation generated: {genOptions.OutputDir}/ ({format})");
            return 0;
        }

        /// <summary>Installs a package: `ccl install [name[@version]]`.</summary>
        static int InstallPackage(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("Usage: ccl install <package[@version]>");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  ccl install ObjektRT            Install latest version");
                Console.WriteLine("  ccl install ObjektRT@1.2.0      Install specific version");
                return 0;
            }

            string packageName = args[0];
            Contract.Compiler.ContractProject? project = null;
            try { project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory()); }
            catch (FormatException ex) { Error(ex.Message); return 1; }

            string projectRoot = project?.RootPath ?? Directory.GetCurrentDirectory();
            using var client = new PurrClient();
            var resolver = new PackageResolver(client, projectRoot);

            var info = resolver.InstallAsync(packageName).GetAwaiter().GetResult();
            if (info == null) return 1;

            // Add to .ctproj dependencies
            if (project != null)
            {
                project.Dependencies ??= new();
                if (!project.Dependencies.Any(d => d.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    project.Dependencies.Add(new Contract.Compiler.PackageDependency
                    {
                        Name = info.Name,
                        Version = info.Version
                    });
                    project.Save(projectRoot);
                    Console.WriteLine($"Added {info.Name}@{info.Version} to dependencies");
                }
            }

            Console.WriteLine("Done.");
            return 0;
        }

        /// <summary>Removes a package: `ccl remove <name>`.</summary>
        static int RemovePackage(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("Usage: ccl remove <package>");
                return 0;
            }

            string packageName = args[0];
            Contract.Compiler.ContractProject? project = null;
            try { project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory()); }
            catch (FormatException ex) { Error(ex.Message); return 1; }

            string projectRoot = project?.RootPath ?? Directory.GetCurrentDirectory();
            using var client = new PurrClient();
            var resolver = new PackageResolver(client, projectRoot);

            if (resolver.Remove(packageName))
            {
                Console.WriteLine($"Removed {packageName}");

                // Remove from .ctproj dependencies
                if (project?.Dependencies != null)
                {
                    project.Dependencies.RemoveAll(d => d.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
                    project.Save(projectRoot);
                }
                return 0;
            }
            else
            {
                Error($"Package '{packageName}' is not installed.");
                return 1;
            }
        }

        /// <summary>Searches the Purr registry: `ccl search <query>`.</summary>
        static int SearchPackages(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("Usage: ccl search <query>");
                return 0;
            }

            string query = string.Join(" ", args);
            using var client = new PurrClient();

            Console.Write($"Searching for '{query}'...");
            var results = client.SearchAsync(query).GetAwaiter().GetResult();

            if (results.Count == 0)
            {
                Console.WriteLine(" no results.");
                return 0;
            }

            Console.WriteLine($" {results.Count} result(s):");
            Console.WriteLine();
            foreach (var pkg in results)
            {
                Console.Write($"  {pkg.Name}@{pkg.Version}");
                if (pkg.Author != null) Console.Write($" by {pkg.Author}");
                Console.WriteLine();
                if (pkg.Description != null)
                    Console.WriteLine($"    {pkg.Description}");
                if (pkg.Tags != null && pkg.Tags.Count > 0)
                    Console.WriteLine($"    tags: {string.Join(", ", pkg.Tags)}");
                Console.WriteLine($"    downloads: {pkg.Downloads}");
            }
            return 0;
        }

        /// <summary>Lists installed packages: `ccl list`.</summary>
        static int ListPackages(string[] args)
        {
            Contract.Compiler.ContractProject? project = null;
            try { project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory()); }
            catch (FormatException ex) { Error(ex.Message); return 1; }

            string projectRoot = project?.RootPath ?? Directory.GetCurrentDirectory();
            using var client = new PurrClient();
            var resolver = new PackageResolver(client, projectRoot);

            var installed = resolver.ListInstalled();
            if (installed.Count == 0)
            {
                Console.WriteLine("No packages installed. Use `ccl install <package>` to add one.");
                return 0;
            }

            Console.WriteLine("Installed packages:");
            foreach (var pkg in installed)
                Console.WriteLine($"  {pkg.Name} v{pkg.Version}");

            return 0;
        }

        // ── Project management ──────────────────────────────────────────

        /// <summary>Routes `ccl project add|remove|list` subcommands.</summary>
        static int ProjectCommand(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("Usage: ccl project <command>");
                Console.WriteLine();
                Console.WriteLine("Commands:");
                Console.WriteLine("  ccl project add <path>      Add a sub-project to the solution");
                Console.WriteLine("  ccl project remove <name>   Remove a sub-project from the solution");
                Console.WriteLine("  ccl project list            List sub-projects in the solution");
                return 0;
            }

            return args[0] switch
            {
                "add"    => ProjectAdd(args.Skip(1).ToArray()),
                "remove" => ProjectRemove(args.Skip(1).ToArray()),
                "list"   => ProjectList(args.Skip(1).ToArray()),
                _        => ErrorReturn($"Unknown project command: {args[0]}")
            };
        }

        /// <summary>Adds a sub-project: `ccl project add <path-to-ctproj-dir>`.</summary>
        static int ProjectAdd(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("Usage: ccl project add <path>");
                Console.WriteLine();
                Console.WriteLine("Adds a sub-project to the Projects array in the current solution ctproj.");
                Console.WriteLine("The path should point to a directory containing a contract.ctproj, or a .ctproj file.");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  ccl project add ../LibB              Add a sibling project");
                Console.WriteLine("  ccl project add libs/collections    Add a nested project");
                return 0;
            }

            string inputPath = args[0];
            Contract.Compiler.ContractProject? project = null;
            try { project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory()); }
            catch (FormatException ex) { Error(ex.Message); return 1; }

            if (project == null)
            {
                Error($"No project found — '{Contract.Compiler.ContractProject.FileName}' not found in {Directory.GetCurrentDirectory()}.");
                return 1;
            }

            // Resolve the target: directory → look for contract.ctproj inside
            string targetPath = Contract.Compiler.ImportResolver.NormalizeAbsolutePath(inputPath);
            if (Directory.Exists(targetPath))
                targetPath = Path.Combine(targetPath, Contract.Compiler.ContractProject.FileName);

            if (!File.Exists(targetPath))
            {
                Error($"No contract.ctproj found at: {targetPath}");
                return 1;
            }

            // Verify it loads as a valid project
            var subProject = Contract.Compiler.ContractProject.Load(targetPath);
            if (subProject == null)
            {
                Error($"Invalid project file: {targetPath}");
                return 1;
            }

            // Compute relative path from the root project to the sub-project
            string rootDir = project.RootPath!;
            string subDir = Path.GetDirectoryName(targetPath)!;
            string relativePath = GetRelativePath(rootDir, subDir)
                .Replace(Path.DirectorySeparatorChar, '/');

            // Initialize Projects array if needed
            project.Projects ??= new();

            // Check for duplicates
            if (project.Projects.Any(p => p.Replace('\\', '/').Equals(relativePath, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"Project already in solution: {relativePath}");
                return 0;
            }

            project.Projects.Add(relativePath);
            project.Save(rootDir);

            string subName = subProject.Name ?? Path.GetFileName(subDir);
            Console.WriteLine($"Added {subName} ({relativePath}) to {project.Name}");
            return 0;
        }

        /// <summary>Removes a sub-project: `ccl project remove <name>`.</summary>
        static int ProjectRemove(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("Usage: ccl project remove <name>");
                Console.WriteLine();
                Console.WriteLine("Removes a sub-project from the Projects array by name or path.");
                return 0;
            }

            string target = args[0];
            Contract.Compiler.ContractProject? project = null;
            try { project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory()); }
            catch (FormatException ex) { Error(ex.Message); return 1; }

            if (project == null)
            {
                Error($"No project found — '{Contract.Compiler.ContractProject.FileName}' not found in {Directory.GetCurrentDirectory()}.");
                return 1;
            }

            if (project.Projects == null || project.Projects.Count == 0)
            {
                Error("This solution has no sub-projects (no Projects array in ctproj).");
                return 1;
            }

            // Try to find by name (load each sub-project and match Name)
            // or by path substring
            int idx = -1;
            for (int i = 0; i < project.Projects.Count; i++)
            {
                string rel = project.Projects[i];
                string normalizedRel = rel.Replace('/', Path.DirectorySeparatorChar);
                string absPath = Path.Combine(project.RootPath!, normalizedRel);

                string absCtproj;
                if (Directory.Exists(absPath))
                    absCtproj = Path.Combine(absPath, Contract.Compiler.ContractProject.FileName);
                else
                    absCtproj = absPath;

                if (File.Exists(absCtproj))
                {
                    var sub = Contract.Compiler.ContractProject.Load(absCtproj);
                    if (sub != null && sub.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }

                // Also match by path
                if (rel.Replace('\\', '/').Equals(target.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
                    || rel.Contains(target, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                }
            }

            if (idx < 0)
            {
                Error($"Sub-project '{target}' not found in solution.");
                return 1;
            }

            string removed = project.Projects[idx];
            project.Projects.RemoveAt(idx);
            project.Save(project.RootPath!);
            Console.WriteLine($"Removed {removed} from {project.Name}");
            return 0;
        }

        /// <summary>Lists sub-projects: `ccl project list`.</summary>
        static int ProjectList(string[] args)
        {
            Contract.Compiler.ContractProject? project = null;
            try { project = Contract.Compiler.ContractProject.Load(Directory.GetCurrentDirectory()); }
            catch (FormatException ex) { Error(ex.Message); return 1; }

            if (project == null)
            {
                Error($"No project found — '{Contract.Compiler.ContractProject.FileName}' not found in {Directory.GetCurrentDirectory()}.");
                return 1;
            }

            if (project.Projects == null || project.Projects.Count == 0)
            {
                Console.WriteLine("No sub-projects. Use `ccl project add <path>` to add one.");
                return 0;
            }

            Console.WriteLine($"Sub-projects in {project.Name}:");
            foreach (var rel in project.Projects)
            {
                // rel can be "LibB/contract.ctproj" or "LibB"
                string normalizedRel = rel.Replace('/', Path.DirectorySeparatorChar);
                string absPath = Path.Combine(project.RootPath!, normalizedRel);

                // If it's a directory, look for ctproj inside
                string absCtproj;
                if (Directory.Exists(absPath))
                    absCtproj = Path.Combine(absPath, Contract.Compiler.ContractProject.FileName);
                else
                    absCtproj = absPath;

                string absDir = Path.GetDirectoryName(absCtproj)!;
                string name = Path.GetFileName(absDir);
                string type = "?";

                if (File.Exists(absCtproj))
                {
                    var sub = Contract.Compiler.ContractProject.Load(absCtproj);
                    if (sub != null)
                    {
                        name = sub.Name ?? name;
                        type = sub.Type ?? "exe";
                    }
                }

                Console.WriteLine($"  {name} [{type}] — {rel}");
            }

            return 0;
        }

        /// <summary>Computes a relative path from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static string GetRelativePath(string from, string to)
        {
            from = Path.GetFullPath(from);
            to = Path.GetFullPath(to);

            var fromUri = new Uri(from.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var toUri = new Uri(to);

            string relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);

            return relative;
        }

        /// <summary>Launches the DAP debug server: `ccl debug [file.ct]`.</summary>
        static int DebugProject(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is "-h" or "--help")
                {
                    Console.WriteLine("Usage: ccl debug [file.ct]");
                    Console.WriteLine();
                    Console.WriteLine("Launches the Debug Adapter Protocol (DAP) server over stdin/stdout.");
                    Console.WriteLine("Intended to be launched by VSCode's launch.json configuration.");
                    return 0;
                }
            }

            var stdout = Console.Out;
            Console.SetOut(Console.Error);

            ObjectRT.Dap.DapLog.Write(
                $"ccl debug started: args=[{string.Join(" ", args)}] cwd={Directory.GetCurrentDirectory()} version={typeof(Program).Assembly.GetName().Version} dll={typeof(Program).Assembly.Location}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ObjectRT.Dap.DapLog.Write($"FATAL unhandled: {e.ExceptionObject}");

            var server = new ObjectRT.Dap.DapServer(Console.In, stdout, new Contract.LanguageServer.Dap.ContractDapLoader());
            server.RunAsync().GetAwaiter().GetResult();
            return 0;
        }
    }
}
