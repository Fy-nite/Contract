using System.Collections.Generic;
using System.IO;

namespace Contract.Compiler
{
    /// <summary>
    /// Utility for expanding project templates: copies files and replaces {{VariableName}} placeholders.
    /// Templates are contract projects (directories with a contract.ctproj) that define what gets created.
    /// </summary>
    public static class TemplateEngine
    {
        // Standard placeholder variables available in all templates
        private static readonly Dictionary<string, string> DefaultPlaceholders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["{{ProjectName}}"] = "",  // filled in at call site
            ["{{ProjectType}}"] = "",
            ["{{Namespace}}"]   = "",
            ["{{Version}}"]     = "",
        };

        /// <summary>Expands a template directory into the target location, replacing placeholders.</summary>
        public static int ExpandTemplate(string? name, string templatePath)
        {
            var root = name ?? Path.GetFileName(Directory.GetCurrentDirectory());
            var fullRoot = Path.GetFullPath(root);

            if (Directory.Exists(fullRoot) && Directory.EnumerateFileSystemEntries(fullRoot).Any())
            {
                return Error($"Directory '{root}' already exists and is not empty.");
            }

            // Validate template source
            if (!Directory.Exists(templatePath))
                return Error($"Template not found: '{templatePath}'");

            // Load template's contract.ctproj for metadata
            var templateCtProj = ContractProject.Load(Path.Combine(templatePath, ContractProject.FileName));

            // Create target directory
            Directory.CreateDirectory(fullRoot);

            // Copy all files from template -> target (skip template's own contract.ctproj)
            foreach (var srcFile in Directory.GetFiles(templatePath, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(srcFile);
                if (fileName == ContractProject.FileName) continue;

                var relPath = Path.GetRelativePath(templatePath, srcFile);
                var destPath = Path.Join(fullRoot, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(srcFile, destPath, false);
            }

            // Build replacement map
            var projName = name ?? "app";
            var replacements = new Dictionary<string, string>(DefaultPlaceholders)
            {
                ["{{ProjectName}}"] = projName,
                ["{{ProjectType}}"] = templateCtProj?.Type ?? "exe",
                ["{{Namespace}}"]   = templateCtProj?.Namespace ?? projName.ToLowerInvariant(),
                ["{{Version}}"]     = templateCtProj?.Version ?? "0.1.0",
            };

            // Replace placeholders in all text files
            ReplacePlaceholdersInDir(fullRoot, replacements);

            // Write the final contract.ctproj with resolved values
            if (templateCtProj != null)
            {
                templateCtProj.Name = ResolvePlaceholder(projName, templateCtProj.Name);
                templateCtProj.Type = ResolvePlaceholder(templateCtProj.Type, "{{ProjectType}}")
                    .Replace("{{ProjectType}}", "exe");
                if (string.IsNullOrEmpty(templateCtProj.Namespace))
                    templateCtProj.Namespace = replacements["{{Namespace}}"];
                if (string.IsNullOrEmpty(templateCtProj.Version))
                    templateCtProj.Version = replacements["{{Version}}"];
                templateCtProj.Save(fullRoot);
            }
            else
            {
                // Fallback: create minimal contract.ctproj
                new ContractProject { Name = projName, Type = "exe", Version = "0.1.0" }.Save(fullRoot);
            }

            Console.WriteLine($"Created project '{root}' from template at '{templatePath}'");
            Console.WriteLine($"  contract.ctproj  — project settings");
            Console.WriteLine("Next: cd into it and run `ccl build --run`");
            return 0;
        }

        /// <summary>Finds a named template in known discovery directories.</summary>
        public static string? FindNamedTemplate(string name)
        {
            // 1. Project-local templates
            var local = Path.Join(".purr", "templates", name);
            if (Directory.Exists(local)) return local;

            // 2. Global templates cache
            var global = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".purr", "templates", name);
            if (Directory.Exists(global)) return global;

            return null;
        }

        /// <summary>Recursively replaces {{Var}} placeholders in all text files under a directory.</summary>
        private static void ReplacePlaceholdersInDir(string dir, IDictionary<string, string> replacements)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    foreach (var kv in replacements)
                        content = content.Replace(kv.Key, kv.Value);
                    File.WriteAllText(file, content);
                }
                catch
                {
                    // Skip binary files or unreadable files
                }
            }
        }

        private static string ResolvePlaceholder(string value, string placeholderKey)
        {
            if (value.Contains(placeholderKey))
                return value.Replace(placeholderKey, "");
            return value;
        }

        private static int Error(string message)
        {
            Console.Error.WriteLine($"Error: {message}");
            return 1;
        }
    }
}
