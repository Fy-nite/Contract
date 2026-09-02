using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler;

/// <summary>
/// Result of building a single sub-project within a solution.
/// </summary>
public class ProjectBuildResult
{
    public required ContractProject Project { get; init; }
    public required string ProjectPath { get; init; }
    public required string OutputPath { get; init; }
    public required DiagnosticBag Diagnostics { get; init; }
    public bool Success => !Diagnostics.HasErrors;
}

/// <summary>
/// Result of building an entire solution.
/// </summary>
public class SolutionBuildResult
{
    public List<ProjectBuildResult> Projects { get; set; } = new();
    public bool Success => Projects.All(p => p.Success);
}

/// <summary>
/// Builds a multi-project solution: loads sub-projects, topologically sorts
/// them by dependency, and builds each in order.
/// </summary>
public static class SolutionBuilder
{
    /// <summary>
    /// Builds a multi-project solution defined by a root ctproj with a
    /// <c>Projects</c> array.
    /// </summary>
    public static SolutionBuildResult Build(ContractProject root, bool staticLink = false, string? outputOverride = null)
    {
        if (root.Projects == null || root.Projects.Count == 0)
            throw new ArgumentException("SolutionBuilder requires a project with a 'Projects' array.", nameof(root));

        // ── 1. Load all sub-projects ──────────────────────────────
        var subProjects = new List<(ContractProject project, string path, string name)>();
        foreach (var relPath in root.Projects)
        {
            string absPath = Path.Combine(
                root.RootPath!,
                relPath.Replace('/', Path.DirectorySeparatorChar));

            var sub = ContractProject.Load(absPath);
            if (sub == null)
                throw new FormatException($"Sub-project not found: {relPath} (looked at {absPath})");
            if (sub.MainPath == null || !File.Exists(sub.MainPath))
            {
                if (sub.IsExecutable)
                    throw new FormatException($"Sub-project main file not found: {relPath}/{sub.Main}");
                if (sub.Sources == null || sub.Sources.Count == 0)
                    throw new FormatException($"Sub-project has no main file and no Sources: {relPath}");
            }

            string name = sub.Name ?? Path.GetFileNameWithoutExtension(relPath);
            subProjects.Add((sub, Path.GetDirectoryName(absPath)!, name));
        }

        // ── 2. Topo-sort by dependency ────────────────────────────
        var sorted = TopologicalSort(subProjects);

        // ── 3. Build each in order ────────────────────────────────
        var results = new List<ProjectBuildResult>();

        // All sub-projects emit into the root project's resolved output directory.
        string rootOutputDir = outputOverride
            ?? root.OutputPath
            ?? Path.Combine(root.RootPath!, root.Output ?? "bin");

        foreach (var (project, projectDir, name) in sorted)
        {
            string outDir = rootOutputDir;
            Directory.CreateDirectory(outDir);

            var result = BuildSingleProject(project, projectDir, outDir, staticLink);
            results.Add(result);

            if (!result.Success)
            {
                Console.WriteLine($"  ✗ {name} — build failed");
                break;
            }
            Console.WriteLine($"  ✓ {name} → {result.OutputPath}");
        }

        return new SolutionBuildResult { Projects = results };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Build a single sub-project
    // ═══════════════════════════════════════════════════════════════════

    private static ProjectBuildResult BuildSingleProject(
        ContractProject project, string projectDir, string outDir, bool staticLink)
    {
        var diagnostics = new DiagnosticBag();

        // Use the compiler pipeline directly (no runtime reference needed)
        var symbolTable = new StandardLibrary.SymbolTable();
        StandardLibrary.StdlibCatalog.RegisterInto(symbolTable);

        bool hasMain = project.MainPath != null && File.Exists(project.MainPath);

        var driver = new CompilerDriver(diagnostics);
        Program program;
        if (hasMain)
        {
            program = driver.Compile(project.MainPath!);
        }
        else
        {
            // Library with Sources globs, no main file
            var sourceFiles = new List<string>();
            foreach (var pattern in project.Sources!)
            {
                sourceFiles.AddRange(ContractProject.ExpandGlob(project.RootPath!, pattern)
                    .Where(f => f.EndsWith(".ct", StringComparison.OrdinalIgnoreCase)));
            }
            program = driver.Compile(sourceFiles, project.RootPath!);
        }

        if (diagnostics.HasErrors)
        {
            diagnostics.ReportToConsole();
            return new ProjectBuildResult
            {
                Project = project,
                ProjectPath = projectDir,
                OutputPath = "",
                Diagnostics = diagnostics,
            };
        }

        string? mainRef = hasMain ? project.MainPath : null;
        var analyzer = new Semantics.SemanticAnalyzer(symbolTable, diagnostics, mainRef, project.IsExecutable);
        analyzer.Analyze(program);

        if (diagnostics.HasErrors)
        {
            diagnostics.ReportToConsole();
            return new ProjectBuildResult
            {
                Project = project,
                ProjectPath = projectDir,
                OutputPath = "",
                Diagnostics = diagnostics,
            };
        }

        var codeGen = new CodeGen.IRCodeGenerator(diagnostics);
        codeGen.Generate(program);

        if (diagnostics.HasErrors)
        {
            diagnostics.ReportToConsole();
            return new ProjectBuildResult
            {
                Project = project,
                ProjectPath = projectDir,
                OutputPath = "",
                Diagnostics = diagnostics,
            };
        }

        diagnostics.ReportWarningsToConsole();
        string ir = codeGen.GetIRText();

        // Name the output by the project name so that multiple sub-projects
        // sharing the solution's output directory don't overwrite each other.
        string outputBase = project.Name
            ?? (hasMain ? Path.GetFileNameWithoutExtension(project.Main) : "lib");
        string outFile = Path.Combine(outDir, outputBase + ".orbt");

        if (ir == null)
        {
            diagnostics.ReportToConsole();
            return new ProjectBuildResult
            {
                Project = project,
                ProjectPath = projectDir,
                OutputPath = outFile,
                Diagnostics = diagnostics,
            };
        }

        diagnostics.ReportWarningsToConsole();

        // All compiled code is emitted as a binary .orbt module.
        var module = ObjektRT.Core.Parsing.OilFileReader.ParseString(ir);
        if (project.IsExecutable && staticLink && module.Imports.Count > 0)
        {
            Console.WriteLine($"  Static linking {module.Imports.Count} import(s) in {project.Name}...");
            module = Contract.Compiler.StaticLinker.Link(module, projectDir);
        }
        var bytes = new ObjektRT.Core.Serialization.ORBTWriter().WriteModule(module);
        File.WriteAllBytes(outFile, bytes);

        return new ProjectBuildResult
        {
            Project = project,
            ProjectPath = projectDir,
            OutputPath = outFile,
            Diagnostics = diagnostics,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Topological sort
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sorts sub-projects by dependency order using DFS-based topological sort.
    /// Dependencies are inferred by analyzing import statements in source files:
    /// if project A imports from a path that resolves within project B's
    /// directory, A depends on B.
    /// </summary>
    private static List<(ContractProject project, string path, string name)> TopologicalSort(
        List<(ContractProject project, string path, string name)> projects)
    {
        // Build dependency graph: project index → list of dependency indices
        var graph = new Dictionary<int, List<int>>();
        var dirToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < projects.Count; i++)
        {
            var (_, path, _) = projects[i];
            dirToIndex[NormalizeDir(path)] = i;
            graph[i] = new List<int>();
        }

        // Analyze each project's source files for imports
        for (int i = 0; i < projects.Count; i++)
        {
            var (project, projectDir, _) = projects[i];
            var imports = CollectImports(project, projectDir);

            foreach (var importPath in imports)
            {
                // Check if this import resolves within another project's directory
                foreach (var (otherProject, otherDir, _) in projects)
                {
                    if (otherDir == projectDir) continue;
                    if (IsWithinDirectory(importPath, otherDir))
                    {
                        int otherIdx = dirToIndex[NormalizeDir(otherDir)];
                        if (!graph[i].Contains(otherIdx))
                            graph[i].Add(otherIdx);
                    }
                }
            }
        }

        // DFS topological sort with cycle detection
        var sorted = new List<int>();
        var visited = new HashSet<int>();
        var visiting = new HashSet<int>();

        for (int i = 0; i < projects.Count; i++)
        {
            if (!visited.Contains(i))
                DfsVisit(i, graph, sorted, visited, visiting);
        }

        return sorted.Select(idx => projects[idx]).ToList();
    }

    private static void DfsVisit(int node, Dictionary<int, List<int>> graph,
        List<int> sorted, HashSet<int> visited, HashSet<int> visiting)
    {
        if (visited.Contains(node)) return;
        if (visiting.Contains(node))
            throw new InvalidOperationException("Circular dependency detected among projects");

        visiting.Add(node);
        foreach (var dep in graph[node])
        {
            if (!visited.Contains(dep))
                DfsVisit(dep, graph, sorted, visited, visiting);
        }
        visiting.Remove(node);
        visited.Add(node);
        sorted.Add(node);
    }

    /// <summary>
    /// Collects all import paths referenced by a project's source files.
    /// Returns the resolved absolute paths of imported files.
    /// </summary>
    private static List<string> CollectImports(ContractProject project, string projectDir)
    {
        var result = new List<string>();
        string mainDir = Path.GetDirectoryName(project.MainPath!) ?? projectDir;

        try
        {
            // Parse the main file to find imports
            var source = File.ReadAllText(project.MainPath!);
            var diagnostics = new DiagnosticBag();
            var lexer = new Contract.Compiler.Parsing.Lexer(source, diagnostics, project.MainPath);
            var tokens = lexer.Tokenize().ToList();
            var parser = new Contract.Compiler.Parsing.Parser(tokens, diagnostics, project.MainPath);
            var program = parser.Parse();

            // Resolve quoted imports
            foreach (var import in program.Imports)
            {
                string spec = import.Trim();
                if (spec.Length >= 2 && spec[0] == '"' && spec[^1] == '"')
                {
                    string relPath = spec.Substring(1, spec.Length - 2);
                    string candidate = Path.GetFullPath(Path.Combine(mainDir, relPath));
                    if (File.Exists(candidate))
                        result.Add(candidate);
                }
            }

            // Resolve namespace imports
            foreach (var ns in program.NamespaceImports)
            {
                string relative = ns.Replace('.', Path.DirectorySeparatorChar);
                foreach (var ext in new[] { ".ct", ".orbt", ".oil" })
                {
                    string candidate = Path.GetFullPath(Path.Combine(mainDir, relative + ext));
                    if (File.Exists(candidate))
                    {
                        result.Add(candidate);
                        break;
                    }
                }
            }
        }
        catch
        {
            // If parsing fails, return empty — we'll get a proper error during build
        }

        return result;
    }

    private static string NormalizeDir(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsWithinDirectory(string filePath, string directory)
    {
        string normalizedFile = Path.GetFullPath(filePath);
        string normalizedDir = NormalizeDir(directory);
        return normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase);
    }
}
