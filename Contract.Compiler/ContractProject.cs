using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contract.Compiler
{
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
        public static ContractProject? Load(string settingsPath)
        {
            string full = ImportResolver.NormalizeAbsolutePath(settingsPath);
            if (Directory.Exists(full))
                full = Path.Combine(full, FileName);
            if (!File.Exists(full)) return null;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                var project = JsonSerializer.Deserialize<ContractProject>(File.ReadAllText(full), options);
                if (project == null) return null;
                project.SettingsPath = full;
                project.RootPath = Path.GetDirectoryName(full);
                return project;
            }
            catch
            {
                return null;
            }
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
