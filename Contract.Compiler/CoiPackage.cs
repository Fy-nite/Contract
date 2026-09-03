using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Contract.Compiler
{
    /// <summary>
    /// A Contract Overlay/Intermediate package (<c>.coi</c>): a ZIP archive that
    /// bundles precompiled Contract modules (<c>.orbt</c>/<c>.oil</c>) together with
    /// the binding assemblies (and their transitive managed + native dependencies)
    /// needed to consume them, plus a <c>manifest.json</c> that describes the
    /// package. This is Contract's analogue of Java's <c>.jar</c> or .NET's
    /// <c>.nupkg</c>.
    ///
    /// <para>Unlike a Purr git-clone package, a <c>.coi</c> is a single self-contained
    /// binary artifact: a consumer installs one file and can import its namespaces
    /// and call its (shadow/native) bindings without passing <c>--bind</c>.</para>
    /// </summary>
    public sealed class CoiManifest
    {
        /// <summary>Package name. Matches the archive stem by convention.</summary>
        public string Name { get; set; } = "";

        /// <summary>Semver version of the package.</summary>
        public string Version { get; set; } = "";

        /// <summary>Package kind: "lib" (default), reserved for future "exe".</summary>
        public string Type { get; set; } = "lib";

        /// <summary>Archive-relative compiled module paths to link in.</summary>
        public List<string> Modules { get; set; } = new();

        /// <summary>Maps an imported namespace to an archive-relative module path.</summary>
        public Dictionary<string, string>? Namespaces { get; set; }

        /// <summary>Archive-relative managed assemblies to auto-register as bindings.</summary>
        public List<string> Bindings { get; set; } = new();

        /// <summary>Transitive .coi dependencies (name → version range).</summary>
        public Dictionary<string, string>? Dependencies { get; set; }
    }

    /// <summary>
    /// A <c>.coi</c> package loaded from disk or from an archive stream. Provides
    /// access to the manifest and targeted entry extraction of the compiled modules
    /// and binding assemblies a consumer needs.
    /// </summary>
    public sealed class CoiPackage : IDisposable
    {
        /// <summary>The manifest read from the top of the archive.</summary>
        public CoiManifest Manifest { get; }

        private readonly ZipArchive _archive;
        private bool _disposed;

        private CoiPackage(CoiManifest manifest, ZipArchive archive)
        {
            Manifest = manifest;
            _archive = archive;
        }

        public static bool IsCoiFile(string path)
            => string.Equals(Path.GetExtension(path), ".coi", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Opens a <c>.coi</c> archive from a file path and reads its manifest.
        /// Throws <see cref="FormatException"/> when the archive or manifest is
        /// malformed.
        /// </summary>
        public static CoiPackage Open(string path)
        {
            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(path);
            }
            catch (Exception ex)
            {
                throw new FormatException($"'{path}' is not a valid .coi archive: {ex.Message}", ex);
            }

            var manifestEntry = archive.Entries.FirstOrDefault(e => e.FullName == "manifest.json")
                ?? throw new FormatException($"'{path}' has no manifest.json at its root");

            CoiManifest manifest;
            try
            {
                using var sr = new StreamReader(manifestEntry.Open());
                manifest = JsonSerializer.Deserialize<CoiManifest>(sr.ReadToEnd(), JsonOptions)
                    ?? new CoiManifest();
            }
            catch (JsonException ex)
            {
                throw new FormatException($"Malformed manifest.json in '{path}': {ex.Message}", ex);
            }

            return new CoiPackage(manifest, archive);
        }

        /// <summary>True when the archive contains an entry at <paramref name="path"/>.</summary>
        public bool Contains(string path)
            => _archive.GetEntry(NormalizeEntry(path)) != null;

        /// <summary>Returns the relative <c>lib/</c> paths of every compiled module.</summary>
        public IEnumerable<string> ModulePaths()
        {
            foreach (var m in Manifest.Modules)
                if (Contains(m))
                    yield return m;
        }

        /// <summary>
        /// Extracts <paramref name="archivePath"/> (relative to the archive root,
        /// using forward slashes) to <paramref name="destinationFile"/>, creating
        /// the destination directory. Returns false when the entry is absent.
        /// </summary>
        public bool ExtractTo(string archivePath, string destinationFile)
        {
            var entry = _archive.GetEntry(NormalizeEntry(archivePath));
            if (entry == null) return false;
            string dir = Path.GetDirectoryName(Path.GetFullPath(destinationFile))!;
            Directory.CreateDirectory(dir);
            entry.ExtractToFile(destinationFile, overwrite: true);
            return true;
        }

        /// <summary>
        /// Copies <paramref name="archivePath"/>'s contents into a byte buffer.
        /// Returns null when the entry is absent.
        /// </summary>
        public byte[]? ReadBytes(string archivePath)
        {
            var entry = _archive.GetEntry(NormalizeEntry(archivePath));
            if (entry == null) return null;
            using var ms = new MemoryStream();
            using var s = entry.Open();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _archive.Dispose();
        }

        /// <summary>All archive-relative paths (forward slashes) of managed DLLs under <c>bindings/</c>.</summary>
        public IEnumerable<string> BindingDllPaths()
        {
            foreach (var b in Manifest.Bindings)
                if (Contains(b))
                    yield return b;
        }

        private static string NormalizeEntry(string path)
            => path.Replace('\\', '/').TrimStart('/');

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
    }
}
