using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Contract.Compiler;

namespace Contract.Cli;

/// <summary>
/// Produces and consumes <c>.coi</c> (Contract Overlay/Intermediate) packages.
/// A <c>.coi</c> is a ZIP archive bundling precompiled Contract modules
/// (<c>.orbt</c>/<c>.oil</c>) with the binding assemblies (and their transitive
/// managed + native dependencies) needed to consume them, plus a
/// <c>manifest.json</c>. See docs/COI_FORMAT.md.
/// </summary>
public static class CoiSupport
{
    /// <summary>Default archive-relative directory for compiled modules.</summary>
    public const string LibDir = "lib";

    /// <summary>Default archive-relative directory for binding assemblies.</summary>
    public const string BindingsDir = "bindings";

    /// <summary>
    /// Packs compiled modules and binding DLLs into a <c>.coi</c> archive.
    /// <paramref name="moduleFiles"/> are .orbt/.oil module files whose content is
    /// copied into <c>lib/</c>. <paramref name="bindingDllPaths"/> are managed
    /// assemblies copied into <c>bindings/</c> (their surrounding <c>runtimes/&lt;rid&gt;/native</c>
    /// trees are included so P/Invoke natives ship too).
    /// </summary>
    /// <returns>The path the archive was written to.</returns>
    public static string Pack(
        string packageName,
        string version,
        IEnumerable<string> moduleFiles,
        IEnumerable<string>? bindingDllPaths,
        string? namespaceMap /* source namespace -> module base name */,
        string outputPath)
    {
        var modules = moduleFiles.ToList();
        foreach (var m in modules)
        {
            if (!File.Exists(m))
                throw new FileNotFoundException($"Module file not found: {m}");
            if (!CoiPackage.IsCoiFile(m) && !CompiledReferenceLoader.IsCompiledReference(m))
                throw new InvalidDataException($"'{m}' is not a compiled module (.orbt/.oil/.oir)");
        }

        var bindings = (bindingDllPaths ?? Enumerable.Empty<string>()).ToList();
        foreach (var b in bindings)
            if (!File.Exists(b))
                throw new FileNotFoundException($"Binding assembly not found: {b}");

        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        var manifest = new CoiManifest
        {
            Name = packageName,
            Version = version,
            Type = "lib",
            Modules = modules.Select(m => $"{LibDir}/{Path.GetFileName(m)}").ToList(),
            Namespaces = namespaceMap != null
                ? new Dictionary<string, string> { { namespaceMap, $"{LibDir}/{Path.GetFileName(modules[0])}" } }
                : null,
            Bindings = bindings.Select(b => $"{BindingsDir}/{Path.GetFileName(b)}").ToList(),
        };

        WriteEntry(archive, "manifest.json", ToJson(manifest));

        for (int i = 0; i < modules.Count; i++)
            archive.CreateEntryFromFile(modules[i], manifest.Modules[i]);

        foreach (var b in bindings)
        {
            archive.CreateEntryFromFile(b, $"{BindingsDir}/{Path.GetFileName(b)}");

            // Include the assembly's native asset trees (runtimes/<rid>/native/*)
            // so a consumer's bundle can flatten them next to its host.
            var dir = Path.GetDirectoryName(Path.GetFullPath(b));
            if (!string.IsNullOrEmpty(dir))
            {
                var runtimes = Path.Combine(dir, "runtimes");
                if (Directory.Exists(runtimes))
                    AddDirectoryToArchive(archive, runtimes, $"{BindingsDir}/runtimes");
            }
        }

        return outputPath;
    }

    /// <summary>
    /// Installs a <c>.coi</c> archive into <paramref name="projectRoot"/>'s
    /// <c>.purr/packages/&lt;name&gt;/</c>, extracting all entries so its
    /// <c>lib/</c> modules are import-resolvable and its <c>bindings/</c>
    /// assemblies are auto-bindable. Returns the installed package name.
    /// </summary>
    public static string Install(string coiPath, string projectRoot)
    {
        if (!File.Exists(coiPath)) throw new FileNotFoundException(coiPath);
        if (!CoiPackage.IsCoiFile(coiPath))
            throw new InvalidDataException($"'{coiPath}' is not a .coi package");

        using var pkg = CoiPackage.Open(coiPath);
        string name = pkg.Manifest.Name;
        if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(coiPath);

        string destDir = Path.Combine(projectRoot, ".purr", "packages", name);
        if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        Directory.CreateDirectory(destDir);

        // Re-extract the archive from its path since our CoiPackage is read-only.
        using var zip = ZipFile.OpenRead(coiPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            string fullPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!fullPath.StartsWith(Path.GetFullPath(destDir), StringComparison.Ordinal)) continue; // zip-slip guard
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
        }

        // Version marker (consistent with Purr git-clone packages).
        File.WriteAllText(Path.Combine(destDir, ".purr-version"), pkg.Manifest.Version);

        return name;
    }

    /// <summary>
    /// Given a project root, returns the binding assembly paths auto-provided by
    /// every installed <c>.coi</c> package (each archive's <c>bindings/*.dll</c>).
    /// These can be loaded via <c>Assembly.LoadFrom</c> and registered so
    /// <c>--bind</c> is unnecessary.
    /// </summary>
    public static List<string> InstalledBindingAssemblies(string projectRoot)
        => CoiResolver.InstalledBindingAssemblies(projectRoot);

    /// <summary>
    /// Registers the namespace→module map of every installed <c>.coi</c> package
    /// so a dotted <c>import OwnAudioSharp;</c> resolves to the compiled module
    /// inside the package. Returns the search roots (the package root or its
    /// <c>lib/</c> dir) that let compiled references be found by path as well.
    /// </summary>
    public static List<string> RegisterPackageImportRoots(string projectRoot)
        => CoiResolver.RegisterPackages(projectRoot);

    /// <summary>Reads a manifest.json from an installed package directory, or null.</summary>
    public static CoiManifest? LoadManifest(string manifestPath)
        => CoiResolver.LoadManifest(manifestPath);

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDir, string targetPrefix)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, file).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, $"{targetPrefix}/{rel}");
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string ToJson(CoiManifest m)
        => System.Text.Json.JsonSerializer.Serialize(m,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
