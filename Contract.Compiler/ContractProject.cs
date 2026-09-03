using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contract.Compiler
{
    /// <summary>
    /// A reference to a package dependency from the Purr registry.
    /// </summary>
    public class PackageDependency
    {
        /// <summary>Package name on the Purr registry (e.g. "ObjektRT").</summary>
        public string Name { get; set; } = "";

        /// <summary>Semver version range. Empty or "*" means latest.</summary>
        public string Version { get; set; } = "*";

        public override string ToString() => string.IsNullOrEmpty(Version) || Version == "*" ? Name : $"{Name}@{Version}";
    }

    /// <summary>
    /// A reference to a NuGet package dependency. NuGet packages provide
    /// .NET assemblies that can be bound to via <c>&lt;ClrImport&gt;</c>.
    /// Packages are restored to <c>~/.purr/nuget/{id}/{version}/</c>
    /// (global cache) and linked into <c>.purr/nuget/{id}/{version}/</c>
    /// (project-local).
    /// </summary>
    public class NuGetDependency
    {
        /// <summary>NuGet package ID (e.g. "Newtonsoft.Json").</summary>
        public string Name { get; set; } = "";

        /// <summary>Semver version or range. Empty or "*" means latest.
        /// Supports NuGet version ranges: <c>[1.0.0]</c>, <c>[1.0.0, 2.0.0)</c>.</summary>
        public string Version { get; set; } = "*";

        public override string ToString() => string.IsNullOrEmpty(Version) || Version == "*" ? Name : $"{Name} {Version}";
    }

    /// <summary>
    /// A Contract project: a folder containing a <c>contract.ctproj</c> settings
    /// file that describes how to build the sources in the folder — as an
    /// executable (needs a <c>Main</c>) or as a library (no entry point; the
    /// compiled module is included from other projects).
    /// </summary>
    public class ContractProject
    {
        /// <summary>Project name (folder name by default).</summary>
        public string Name { get; set; } = "app";

        /// <summary>"exe" (has/needs Main) or "lib" (no entry point required).</summary>
        public string Type { get; set; } = "exe";

        /// <summary>Main source file, relative to the project root. Defaults to src/main.ct.</summary>
        public string Main { get; set; } = "src/main.ct";

        /// <summary>Declared namespace applied to new files created by `ccl new` (optional).</summary>
        public string? Namespace { get; set; }

        /// <summary>Output directory for compiled modules, relative to the project root. Defaults to bin.</summary>
        public string Output { get; set; } = "bin";

        // --- Metadata fields ---

        /// <summary>Semver version string (e.g. "1.0.0").</summary>
        public string? Version { get; set; }

        /// <summary>Package author name.</summary>
        public string? Author { get; set; }

        /// <summary>Short description of the project.</summary>
        public string? Description { get; set; }

        /// <summary>License identifier (e.g. "MIT", "GPL-3.0").</summary>
        public string? License { get; set; }

        /// <summary>Tags for Purr registry search (e.g. ["library", "gui"]).</summary>
        public List<string>? Tags { get; set; }

        /// <summary>Package dependencies from the Purr registry.</summary>
        public List<PackageDependency>? Dependencies { get; set; }

        /// <summary>
        /// NuGet package dependencies. Each entry triggers a download from
        /// nuget.org during <c>ccl restore</c>. Restored assemblies are
        /// available for <c>&lt;ClrImport&gt;</c> binding without explicit
        /// <c>Path:</c> arguments.
        /// </summary>
        public List<NuGetDependency>? NuGetDependencies { get; set; }

        /// <summary>
        /// Sub-project paths (relative to this project's root). When present,
        /// this project acts as a solution: each sub-project is built in
        /// dependency order. Mutually exclusive with <see cref="Main"/> and
        /// <see cref="Sources"/>.
        /// </summary>
        public List<string>? Projects { get; set; }

        /// <summary>
        /// Source file globs for single-project multi-file builds (e.g.
        /// <c>["src/**/*.ct"]</c>). When present (and <see cref="Projects"/>
        /// is null), all matching .ct files are compiled together into one
        /// .orbt. Mutually exclusive with <see cref="Projects"/>.
        /// </summary>
        public List<string>? Sources { get; set; }

        /// <summary>
        /// Extra search roots for <c>import</c> resolution (C#/Java
        /// "classpath"-style). Each entry is a directory (absolute, or
        /// relative to this project's root) that is added to the compiler's
        /// import search roots, so a <c>import Some.Namespace;</c> can find a
        /// library source by its DECLARED namespace no matter which file
        /// declares it. Typical use: point at a sibling library repo's source
        /// directory, e.g. <c>["../../OwnAudioSharp.Contract/src"]</c>.
        /// </summary>
        public List<string>? ImportRoots { get; set; }

        /// <summary>True when the project builds as an executable (requires a Main entry point).</summary>
        [JsonIgnore]
        public bool IsExecutable => !string.Equals(Type, "lib", StringComparison.OrdinalIgnoreCase);

        /// <summary>The file name of the project settings file inside the project root.</summary>
        public const string FileName = "contract.ctproj";

        /// <summary>Absolute path of the project root directory.</summary>
        [JsonIgnore]
        public string? RootPath { get; private set; }

        /// <summary>Absolute path of the project settings file.</summary>
        [JsonIgnore]
        public string? SettingsPath { get; private set; }

        /// <summary>Absolute path of the main source file.</summary>
        [JsonIgnore]
        public string? MainPath => RootPath == null ? null : Path.Combine(RootPath, Main.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Absolute path of the output directory.</summary>
        [JsonIgnore]
        public string? OutputPath => RootPath == null ? null : Path.Combine(RootPath, Output.Replace('/', Path.DirectorySeparatorChar));

        public static string SettingsPathFor(string projectRoot)
            => Path.Combine(projectRoot, FileName);

        /// <summary>
        /// Resolves the project settings file inside <paramref name="projectRoot"/>:
        /// <paramref name="fileName"/> when given, else <c>contract.ctproj</c> when
        /// present, else the single remaining <c>*.ctproj</c> (or the first, ordered
        /// deterministically) when present. Null when no settings file is found.
        /// </summary>
        public static string? ResolveSettingsFile(string projectRoot, string? fileName = null)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                var direct = Path.Combine(projectRoot, fileName);
                return File.Exists(direct) ? direct : null;
            }
            var standard = Path.Combine(projectRoot, FileName);
            if (File.Exists(standard)) return standard;
            var others = Directory.GetFiles(projectRoot, "*.ctproj", SearchOption.TopDirectoryOnly);
            if (others.Length == 0) return null;
            Array.Sort(others, StringComparer.OrdinalIgnoreCase);
            return others[0];
        }

        /// <summary>
        /// Expands a glob pattern under <paramref name="root"/> with full
        /// <c>**</c> (zero or more directories), <c>*</c> and <c>?</c> support
        /// in ANY path component. Returns the matching file paths (directories
        /// are not matched at the final component). Patterns that are already
        /// rooted are used as-is.
        /// </summary>
        public static List<string> ExpandGlob(string root, string pattern)
        {
            var result = new List<string>();
            string baseDir = Path.IsPathRooted(pattern) ? "" : root;
            string normalized = pattern.Replace('/', Path.DirectorySeparatorChar)
                                       .Replace('\\', Path.DirectorySeparatorChar);
            var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return result;
            ExpandGlobInto(baseDir, parts, 0, result);
            return result.Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void ExpandGlobInto(string currentDir, string[] parts, int idx, List<string> result)
        {
            if (idx >= parts.Length)
            {
                result.Add(currentDir);
                return;
            }

            string part = parts[idx];

            if (part == "**")
            {
                // `**` matches zero OR more directory levels.
                ExpandGlobInto(currentDir, parts, idx + 1, result);          // zero dirs
                if (!Directory.Exists(currentDir)) return;
                foreach (var sub in Directory.EnumerateDirectories(currentDir))
                {
                    // Recurse on the SAME `**` component to consume one+ dirs.
                    ExpandGlobInto(sub, parts, idx, result);
                }
            }
            else if (idx == parts.Length - 1)
            {
                // Final component: match files.
                if (!Directory.Exists(currentDir)) return;
                if (part.IndexOfAny(new[] { '*', '?' }) >= 0)
                {
                    foreach (var file in Directory.EnumerateFiles(currentDir, part))
                        result.Add(file);
                }
                else
                {
                    var full = Path.Combine(currentDir, part);
                    if (File.Exists(full)) result.Add(full);
                }
            }
            else
            {
                // Intermediate component: match directories.
                if (!Directory.Exists(currentDir)) return;
                if (part.IndexOfAny(new[] { '*', '?' }) >= 0)
                {
                    foreach (var sub in Directory.EnumerateDirectories(currentDir, part))
                        ExpandGlobInto(sub, parts, idx + 1, result);
                }
                else
                {
                    ExpandGlobInto(Path.Combine(currentDir, part), parts, idx + 1, result);
                }
            }
        }

        /// <summary>Loads the project settings from <paramref name="settingsPath"/>.
        /// When <paramref name="settingsPath"/> is a directory,
        /// <paramref name="fileName"/> (default <c>contract.ctproj</c>) is used.
        /// When no explicit name is given and <c>contract.ctproj</c> is absent,
        /// any <c>*.ctproj</c> in the directory is used so custom-named project
        /// files are still discovered.</summary>
        /// <remarks>Throws <see cref="FormatException"/> when the settings file exists but is malformed,
        /// so callers can distinguish "no project here" from "project file is broken".</remarks>
        public static ContractProject? Load(string settingsPath, string? fileName = null)
        {
            string full = ImportResolver.NormalizeAbsolutePath(settingsPath);
            if (Directory.Exists(full))
                full = ResolveSettingsFile(full, fileName);
            if (!File.Exists(full)) return null;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            ContractProject? project;
            try
            {
                project = JsonSerializer.Deserialize<ContractProject>(File.ReadAllText(full), options);
            }
            catch (JsonException ex)
            {
                throw new FormatException($"Malformed project file '{full}': {ex.Message}", ex);
            }
            if (project == null) return null;
            project.SettingsPath = full;
            project.RootPath = Path.GetDirectoryName(full);
            return project;
        }

        /// <summary>Writes the settings file into <paramref name="rootPath"/>.</summary>
        public void Save(string rootPath)
        {
            RootPath = rootPath;
            SettingsPath = SettingsPathFor(rootPath);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
        }
    }
}
