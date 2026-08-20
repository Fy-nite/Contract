using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>Loads the project settings from <paramref name="settingsPath"/> (or a directory containing it).</summary>
        /// <remarks>Throws <see cref="FormatException"/> when the settings file exists but is malformed,
        /// so callers can distinguish "no project here" from "project file is broken".</remarks>
        public static ContractProject? Load(string settingsPath)
        {
            string full = ImportResolver.NormalizeAbsolutePath(settingsPath);
            if (Directory.Exists(full))
                full = Path.Combine(full, FileName);
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
