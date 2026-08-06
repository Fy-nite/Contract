using System;
using System.Collections.Generic;
using System.IO;

namespace Contract.Compiler
{
    /// <summary>
    /// Python-style import resolution. An <c>import</c> statement names either a
    /// quoted file path (<c>import "terminal.ct";</c>) or a dotted namespace
    /// (<c>import ovh.finite.hello.Terminal;</c>). Both resolve to a file on
    /// disk:
    ///
    /// <list type="bullet">
    /// <item>The quoted path resolves relative to the importing file's directory.</item>
    /// <item>
    /// The dotted name maps dots to directory separators, like Python modules:
    /// <c>ovh.finite.hello.Terminal</c> → <c>ovh/finite/hello/Terminal.ct</c>.
    /// </item>
    /// </list>
    ///
    /// Search roots, in order: the importing file's directory, then the main
    /// file's directory, then the current working directory.
    /// </summary>
    public static class ImportResolver
    {
        /// <summary>Resolves an import spec to an existing file path, or null when nothing matches.</summary>
        public static string? ResolveImport(string importSpec, string importingFile, IEnumerable<string> extraSearchRoots)
        {
            string spec = importSpec.Trim();
            if (spec.Length >= 2 && spec[0] == '"' && spec[^1] == '"')
                return ResolveRelativePath(spec.Substring(1, spec.Length - 2), importingFile, extraSearchRoots);
            return ResolveNamespace(spec, importingFile, extraSearchRoots);
        }

        /// <summary>
        /// Maps a dotted namespace to a file path: dots become directory
        /// separators and the last segment becomes the file name. Tries source
        /// (<c>.ct</c>) then compiled-reference (<c>.oir</c>/<c>.oil</c>/<c>.orbt</c>)
        /// extensions. Returns null when no candidate file exists.
        /// </summary>
        public static string? ResolveNamespace(string ns, string importingFile, IEnumerable<string> extraSearchRoots)
        {
            string relative = ns.Replace('.', Path.DirectorySeparatorChar);
            foreach (var ext in new[] { ".ct", ".oir", ".oil", ".orbt" })
            {
                string? hit = ResolveRelativePath(relative + ext, importingFile, extraSearchRoots);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Resolves a (possibly relative) path against the importing file's
        /// directory, then the extra search roots. Absolute paths are used
        /// directly. Returns null when no candidate exists.
        /// </summary>
        public static string? ResolveRelativePath(string relativePath, string importingFile, IEnumerable<string> extraSearchRoots)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            if (Path.IsPathRooted(relativePath))
            {
                string full = NormalizeAbsolutePath(relativePath);
                return File.Exists(full) ? full : null;
            }

            string? importingDir = SafeGetDirectoryName(importingFile);
            if (importingDir != null)
            {
                string candidate = NormalizeAbsolutePath(Path.Combine(importingDir, relativePath));
                if (File.Exists(candidate)) return candidate;
            }

            foreach (var root in extraSearchRoots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string candidate = NormalizeAbsolutePath(Path.Combine(root, relativePath));
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Normalizes a path to an absolute form without throwing. Repairs
        /// drive-relative paths like <c>\d:\git\foo.ct</c> (a leading separator
        /// before a drive letter) that some editors/clients produce.
        /// </summary>
        public static string NormalizeAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.Length >= 3 && (path[0] == '\\' || path[0] == '/') && char.IsLetter(path[1]) && path[2] == ':')
                path = path.Substring(1);
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        private static string? SafeGetDirectoryName(string file)
        {
            try { return Path.GetDirectoryName(NormalizeAbsolutePath(file)); }
            catch { return Path.GetDirectoryName(file); }
        }
    }
}
