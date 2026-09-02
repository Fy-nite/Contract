using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Contract.Cli;

/// <summary>
/// Resolves and downloads NuGet packages from nuget.org without requiring
/// the .NET SDK. Uses the NuGet v3 flat-container API for downloads and
/// System.IO.Compression for extraction. No external package dependencies
/// beyond the .NET runtime.
///
/// Packages are cached globally at <c>~/.purr/nuget/{id}/{version}/</c>
/// and linked into projects at <c>.purr/nuget/{id}/{version}/</c>.
/// </summary>
public sealed class NuGetResolver
{
    private static readonly HttpClient Http = new();
    private const string FlatContainer = "https://api.nuget.org/v3-flatcontainer";

    /// <summary>Global cache directory (<c>~/.purr/nuget</c>).</summary>
    public static string GlobalCacheDirStatic { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".purr", "nuget");

    /// <summary>Global cache directory (<c>~/.purr/nuget</c>).</summary>
    public string GlobalCacheDir { get; }

    /// <summary>Local project packages directory (<c>.purr/nuget</c>).
    /// Null when no project root is provided.</summary>
    public string? ProjectNuGetDir { get; }

    static NuGetResolver()
    {
        // NuGet.org requires a User-Agent header.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("ContractCompiler/1.0");
    }

    public NuGetResolver(string? projectRoot = null)
    {
        GlobalCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".purr", "nuget");
        if (projectRoot != null)
            ProjectNuGetDir = Path.Combine(projectRoot, ".purr", "nuget");
    }

    // ── Public API ─────────────────────────────────────────────

    /// <summary>
    /// Restores all NuGet dependencies listed in a project.
    /// Downloads each package and its transitive dependencies to the
    /// global cache, then copies them into the project-local directory.
    /// </summary>
    public async Task<int> RestoreAllAsync(List<Contract.Compiler.NuGetDependency> deps)
    {
        int count = 0;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in deps)
        {
            string? version = dep.Version;
            if (string.IsNullOrWhiteSpace(version) || version == "*")
            {
                Console.Write($"  Resolving {dep.Name} (latest)...");
                version = await GetLatestVersionAsync(dep.Name);
                if (version == null)
                {
                    Console.WriteLine(" not found");
                    continue;
                }
                Console.WriteLine($" {version}");
            }

            try
            {
                Console.Write($"  {dep.Name} {version}...");
                await RestorePackageAsync(dep.Name, version, visited);
                Console.WriteLine(" ok");
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" failed: {ex.Message}");
            }
        }
        return count;
    }

    /// <summary>
    /// Pre-loads all DLLs from a directory into the current AppDomain.
    /// This makes types available for <c>&lt;ClrImport&gt;</c> resolution
    /// without needing explicit <c>Path:</c> references.
    /// </summary>
    public static void PreloadAssemblies(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var dll in Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories))
        {
            try { System.Reflection.Assembly.LoadFrom(dll); }
            catch { /* skip unresolvable assemblies */ }
        }
    }

    /// <summary>
    /// Returns all DLL paths recursively under <paramref name="dir"/>.
    /// </summary>
    public static List<string> GetAllAssemblyPaths(string dir)
    {
        if (!Directory.Exists(dir)) return new();
        return Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories).ToList();
    }

    // ── Package restore ────────────────────────────────────────

    private async Task RestorePackageAsync(string id, string version, HashSet<string> visited)
    {
        string key = $"{id.ToLowerInvariant()}/{version}";
        if (!visited.Add(key)) return; // already restored in this pass

        string cachePath = Path.Combine(GlobalCacheDir, id.ToLowerInvariant(), version);
        string nupkgFileName = $"{id.ToLowerInvariant()}.{version}.nupkg";
        string nupkgPath = Path.Combine(cachePath, nupkgFileName);

        // ── 1. Download ────────────────────────────────────────
        if (!File.Exists(nupkgPath))
        {
            Directory.CreateDirectory(cachePath);
            var url = $"{FlatContainer}/{id.ToLowerInvariant()}/{version}/{nupkgFileName}";
            var bytes = await Http.GetByteArrayAsync(url);
            File.WriteAllBytes(nupkgPath, bytes);
        }

        // ── 2. Extract best-TFM DLLs into cache ────────────────
        string libDir = Path.Combine(cachePath, "lib");
        if (!Directory.Exists(libDir) || Directory.GetFiles(libDir, "*.dll").Length == 0)
        {
            ExtractBestTfmDlls(nupkgPath, id, libDir);
        }

        // ── 3. Link into project ───────────────────────────────
        if (ProjectNuGetDir != null)
        {
            string linkDir = Path.Combine(ProjectNuGetDir, id.ToLowerInvariant(), version);
            if (!Directory.Exists(linkDir))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkDir)!);
                CopyDirectory(cachePath, linkDir);
            }
        }

        // ── 4. Resolve transitive dependencies ─────────────────
        var deps = ReadDependencies(nupkgPath, id);
        foreach (var (depId, depVersion) in deps)
        {
            // Resolve version ranges: take the lower bound of the range.
            string? resolved = ResolveVersionLowerBound(depVersion);
            if (resolved != null)
                await RestorePackageAsync(depId, resolved, visited);
        }
    }

    private async Task<string?> GetLatestVersionAsync(string id)
    {
        try
        {
            var url = $"{FlatContainer}/{id.ToLowerInvariant()}/index.json";
            var json = await Http.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var versions = doc.RootElement.GetProperty("versions");
            if (versions.GetArrayLength() == 0) return null;
            return versions[versions.GetArrayLength() - 1].GetString();
        }
        catch { return null; }
    }

    // ── Nupkg reading ──────────────────────────────────────────

    /// <summary>
    /// Reads transitive dependency ID + version-range from the .nuspec
    /// inside a .nupkg file. Returns all unique dependencies across
    /// all target-framework groups.
    /// </summary>
    private static List<(string id, string version)> ReadDependencies(string nupkgPath, string packageId)
    {
        var result = new List<(string, string)>();
        try
        {
            using var archive = ZipFile.OpenRead(nupkgPath);

            // Nupkg contains {lowercase-id}.nuspec at the root.
            var nuspecEntry = archive.GetEntry($"{packageId.ToLowerInvariant()}.nuspec")
                ?? archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry == null) return result;

            using var stream = nuspecEntry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // NuGet v2-style flat dependency list (no targetFramework groups)
            var groups = doc.Descendants(ns + "dependencies")
                .Elements(ns + "group").ToList();

            if (groups.Count > 0)
            {
                // Pick the group with the highest-scoring TFM
                var bestGroup = groups
                    .OrderByDescending(g => TfmScore(g.Attribute("targetFramework")?.Value ?? "any"))
                    .First();

                foreach (var dep in bestGroup.Elements(ns + "dependency"))
                {
                    var depId = dep.Attribute("id")?.Value;
                    var depVersion = dep.Attribute("version")?.Value;
                    if (depId != null && depVersion != null && seen.Add(depId))
                        result.Add((depId, depVersion));
                }
            }
            else
            {
                // Flat list (no <group> wrappers)
                foreach (var dep in doc.Descendants(ns + "dependency"))
                {
                    var depId = dep.Attribute("id")?.Value;
                    var depVersion = dep.Attribute("version")?.Value;
                    if (depId != null && depVersion != null && seen.Add(depId))
                        result.Add((depId, depVersion));
                }
            }
        }
        catch { /* best-effort: return what we have */ }
        return result;
    }

    // ── TFM selection ──────────────────────────────────────────

    /// <summary>
    /// Extracts DLLs from the best-matching target-framework folder
    /// inside a .nupkg into <paramref name="targetDir"/>.
    /// Returns paths to the extracted DLLs.
    /// </summary>
    private static List<string> ExtractBestTfmDlls(string nupkgPath, string packageId, string targetDir)
    {
        var dlls = new List<string>();
        try
        {
            using var archive = ZipFile.OpenRead(nupkgPath);

            // Find all entries under lib/ that end with .dll
            var libDlls = archive.Entries
                .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                         && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (libDlls.Count == 0) return dlls;

            // Collect unique TFM folder names (lib/{tfm}/file.dll → tfm)
            var tfmFolders = libDlls
                .Select(e => e.FullName.Split('/', 3)[1])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Pick the highest-scoring TFM
            string bestTfm = tfmFolders
                .OrderByDescending(tf => TfmScore(tf))
                .First();

            // Extract DLLs from the best TFM folder
            Directory.CreateDirectory(targetDir);
            foreach (var entry in libDlls.Where(e =>
                e.FullName.Split('/', 3)[1].Equals(bestTfm, StringComparison.OrdinalIgnoreCase)))
            {
                var fileName = Path.GetFileName(entry.FullName);
                var destPath = Path.Combine(targetDir, fileName);
                entry.ExtractToFile(destPath, overwrite: true);
                dlls.Add(destPath);
            }
        }
        catch { /* best-effort */ }
        return dlls;
    }

    /// <summary>
    /// Maps a TFM folder name to a sortable score.
    /// Higher = more preferred for modern .NET projects.
    /// </summary>
    private static int TfmScore(string tfm)
    {
        if (string.IsNullOrEmpty(tfm)) return 0;
        return tfm switch
        {
            "net10.0" => 1000,
            "net9.0"  => 900,
            "net8.0"  => 800,
            "net7.0"  => 700,
            "net6.0"  => 600,
            "net5.0"  => 500,
            "netstandard2.1" => 410,
            "netstandard2.0" => 400,
            "netcoreapp3.1"  => 310,
            "netcoreapp3.0"  => 300,
            "netcoreapp2.2"  => 220,
            "netcoreapp2.1"  => 210,
            "netcoreapp2.0"  => 200,
            "netcoreapp1.1"  => 110,
            "netcoreapp1.0"  => 100,
            "net48"  => 380,
            "net472" => 372,
            "net471" => 371,
            "net47"  => 370,
            "net462" => 362,
            "net461" => 361,
            "net46"  => 360,
            "net452" => 352,
            "net451" => 351,
            "net45"  => 350,
            "net40"  => 300,
            "net35"  => 250,
            "net20"  => 200,
            "net11"  => 110,
            "net10"  => 100,
            "any"    => 1,
            _ => 0
        };
    }

    // ── Version range handling ─────────────────────────────────

    /// <summary>
    /// Extracts the lower bound from a NuGet version range string.
    /// <list type="bullet">
    ///   <item><c>1.0.0</c> → <c>1.0.0</c></item>
    ///   <item><c>[1.0.0]</c> → <c>1.0.0</c></item>
    ///   <item><c>[1.0.0, 2.0.0)</c> → <c>1.0.0</c></item>
    ///   <item><c>(, 2.0.0)</c> → <c>null</c> (no lower bound)</item>
    ///   <item><c>*</c> → <c>null</c> (caller resolves to latest)</item>
    /// </list>
    /// </summary>
    private static string? ResolveVersionLowerBound(string versionRange)
    {
        if (string.IsNullOrWhiteSpace(versionRange) || versionRange == "*")
            return null;

        versionRange = versionRange.Trim();

        // Exact version: "1.0.0"
        if (!versionRange.StartsWith('[') && !versionRange.StartsWith('('))
            return versionRange;

        // Range: "[1.0.0, 2.0.0)" or "(1.0.0, 2.0.0]"
        var inner = versionRange[1..^1]; // strip outer brackets
        var comma = inner.IndexOf(',');
        if (comma < 0)
        {
            // Single version in brackets: "[1.0.0]"
            var v = inner.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
        var lower = inner[..comma].Trim();
        return string.IsNullOrEmpty(lower) ? null : lower;
    }

    // ── Helpers ────────────────────────────────────────────────

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
