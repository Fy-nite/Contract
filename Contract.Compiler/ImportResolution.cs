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

        // Memoizes the declared-namespace of an already-scanned .ct file so a
        // multi-library build scanning the same roots repeatedly does not re-read.
        private static readonly Dictionary<string, string?> s_namespaceCache = new(StringComparer.OrdinalIgnoreCase);

        // Maps a dotted namespace directly to a compiled module (.orbt/.oil)
        // provided by an installed .coi package. Consulted ahead of path-based
        // resolution so `import OwnAudioSharp;` finds the compiled module inside
        // the package even though there is no OwnAudioSharp.ct file.
        private static readonly Dictionary<string, string> s_compiledNamespaces = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers a dotted namespace → compiled module file mapping
        /// (used by installed <c>.coi</c> packages). Compiles to the fully
        /// qualified path so a <c>import</c> of that namespace returns the module.</summary>
        public static void RegisterCompiledNamespace(string ns, string moduleFile)
        {
            if (string.IsNullOrWhiteSpace(ns)) return;
            string abs;
            try { abs = NormalizeAbsolutePath(Path.GetFullPath(moduleFile)); }
            catch { abs = moduleFile; }
            lock (s_compiledNamespaces)
            {
                s_compiledNamespaces[ns] = abs;
            }
        }

        /// <summary>Resolves a registered compiled-module namespace, or null.</summary>
        public static string? TryResolveCompiledNamespace(string ns)
        {
            lock (s_compiledNamespaces)
            {
                return s_compiledNamespaces.TryGetValue(ns, out var p) && File.Exists(p) ? p : null;
            }
        }

        /// <summary>
        /// Maps a dotted namespace to a file path: dots become directory
        /// separators and the last segment becomes the file name. Tries source
        /// (<c>.ct</c>) then compiled-reference (<c>.oir</c>/<c>.oil</c>/<c>.orbt</c>)
        /// extensions. When no file matches by location, falls back to a
        /// content search: any <c>.ct</c> under the search roots whose first
        /// declared <c>namespace</c> equals <paramref name="ns"/> is returned,
        /// regardless of its file name. Returns null when no candidate exists.
        /// </summary>
        public static string? ResolveNamespace(string ns, string importingFile, IEnumerable<string> extraSearchRoots)
        {
            // An installed .coi package may map this namespace directly to a
            // compiled module; that takes precedence so precompiled libraries
            // resolve without shipping .ct source.
            string? compiled = TryResolveCompiledNamespace(ns);
            if (compiled != null) return compiled;

            string relative = ns.Replace('.', Path.DirectorySeparatorChar);

            foreach (var ext in new[] { ".ct", ".oir", ".oil", ".orbt" })
            {
                string? hit = ResolveRelativePath(relative + ext, importingFile, extraSearchRoots);
                if (hit != null) return hit;
            }

            // No file at the expected dotted-path location. C#/Java-style
            // namespace imports don't require a type-named file: find the
            // source that DECLARES this namespace, by content. Only the
            // implicit bootstrap namespace (__builtin.*) and the pure runtime
            // .NET namespaces (System.*) are never source-located; ObjektRT.*,
            // std.* and any user/library namespaces are genuine source
            // namespaces and are scanned by content so files like ManagedPtr.ct
            // (declaring `namespace ObjektRT.std.Memory;`) resolve on name.
            return IsRuntimeBindingNamespace(ns)
                ? null
                : ResolveNamespaceByContent(ns, importingFile, extraSearchRoots);
        }

        /// <summary>True for namespace conventions that resolve at runtime /
        /// by bootstrap, never by a source file's declared namespace: the
        /// implicit <c>__builtin.*</c> bootstrap and pure .NET runtime
        /// <c>System.*</c> namespaces.</summary>
        private static bool IsRuntimeBindingNamespace(string ns)
        {
            return ns.StartsWith("__builtin", StringComparison.OrdinalIgnoreCase)
                || ns.StartsWith("System", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Scans every search root recursively for a <c>.ct</c> source whose
        /// first declared <c>namespace ...;</c> equals <paramref name="ns"/>.
        /// Returns the first (deterministic, breadth-first by directory) match,
        /// or null. This lets <c>import OwnAudioSharp;</c> resolve an
        /// arbitrarily-named file that declares <c>namespace OwnAudioSharp;</c>.
        /// Scans are memoized per namespace so a build touching the roots more
        /// than once stays cheap.
        /// </summary>
        public static string? ResolveNamespaceByContent(string ns, string importingFile, IEnumerable<string> extraSearchRoots)
        {
            if (string.IsNullOrWhiteSpace(ns)) return null;

            // Deterministic root order: importing file's dir first, then main, then extras.
            string? importingDir = SafeGetDirectoryName(importingFile);
            var roots = new List<string>();
            if (importingDir != null) roots.Add(importingDir);
            foreach (var root in extraSearchRoots)
                if (!string.IsNullOrEmpty(root) && !roots.Contains(root))
                    roots.Add(root);

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string? hit = ScanRootForNamespace(root, ns);
                if (hit != null) return hit;
            }
            return null;
        }

        private static string? ScanRootForNamespace(string root, string ns)
        {
            // Breadth-first by depth, then by path, so results are deterministic.
            foreach (var file in EnumerateCtFilesBfs(root))
            {
                if (DeclaredNamespace(file) == ns)
                    return file;
            }
            return null;
        }

        /// <summary>
        /// All <c>.ct</c> files under a root whose declared namespace equals
        /// <paramref name="ns"/> (deterministic: breadth-first by depth, then by
        /// path, so <c>Chip.ct</c> precedes <c>OwnAudio.ct</c> in the same dir).
        /// </summary>
        private static IEnumerable<string> ScanRootForFilesWithNamespace(string root, string ns)
        {
            foreach (var file in EnumerateCtFilesBfs(root))
            {
                if (DeclaredNamespace(file) == ns)
                    yield return file;
            }
        }

        /// <summary>
        /// Every source module that POPULATES a namespace import — the
        /// directory-located file (dots → path separators) plus every
        /// content-matched <c>.ct</c> under the search roots that declares the
        /// namespace. <see cref="ResolveNamespace"/> keeps resolving the single
        /// first hit for single-file consumers; this variant lets a namespace
        /// span multiple files (e.g. <c>OwnAudio.ct</c> + <c>Chip.ct</c> both
        /// declaring <c>namespace OwnAudioSharp;</c>), each of which is loaded
        /// into the consuming program so all declared members resolve.
        /// </summary>
        public static IEnumerable<string> ResolveNamespaceFiles(string ns, string importingFile, IEnumerable<string> extraSearchRoots)
        {
            // An installed .coi package maps the whole namespace to one module.
            string? compiled = TryResolveCompiledNamespace(ns);
            if (compiled != null)
            {
                yield return compiled;
                yield break;
            }

            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // The directory-located file (the ResolveNamespace convention).
            string relative = ns.Replace('.', Path.DirectorySeparatorChar);
            foreach (var ext in new[] { ".ct", ".oir", ".oil", ".orbt" })
            {
                string? hit = ResolveRelativePath(relative + ext, importingFile, extraSearchRoots);
                if (hit != null && yielded.Add(hit)) yield return hit;
            }

            if (IsRuntimeBindingNamespace(ns)) yield break;

            // Content search: every source declaring this namespace.
            string? importingDir = SafeGetDirectoryName(importingFile);
            var roots = new List<string>();
            if (importingDir != null) roots.Add(importingDir);
            foreach (var root in extraSearchRoots)
                if (!string.IsNullOrEmpty(root) && !roots.Contains(root))
                    roots.Add(root);

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in ScanRootForFilesWithNamespace(root, ns))
                {
                    if (yielded.Add(file)) yield return file;
                }
            }
        }

        private static IEnumerable<string> EnumerateCtFilesBfs(string root)
        {
            var dirs = new Queue<string>();
            dirs.Enqueue(root);
            while (dirs.Count > 0)
            {
                string dir = dirs.Dequeue();
                IEnumerable<string> children;
                try { children = Directory.GetFileSystemEntries(dir); }
                catch { continue; }

                // Emit this directory's .ct files first (deterministic order),
                // then queue its subdirectories for the next breadth level.
                var seenDirs = new List<string>();
                string[] ctFiles;
                try { ctFiles = Directory.GetFiles(dir, "*.ct", SearchOption.TopDirectoryOnly); }
                catch { ctFiles = Array.Empty<string>(); }
                Array.Sort(ctFiles, StringComparer.OrdinalIgnoreCase);
                foreach (var f in ctFiles) yield return f;

                foreach (var entry in children)
                {
                    if (Directory.Exists(entry)) seenDirs.Add(entry);
                }
                seenDirs.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var d in seenDirs) dirs.Enqueue(d);
            }
        }

        /// <summary>
        /// Reads the first <c>namespace</c> declaration of a <c>.ct</c> file,
        /// skipping blank lines and <c>//</c> line comments. Cheap: only reads
        /// up to the first non-comment 'namespace' token, or the first ';'.
        /// Returns the dotted namespace string, or null when the file has none
        /// (e.g. it only imports and declares contracts without a namespace).
        /// </summary>
        public static string? DeclaredNamespace(string file)
        {
            if (!File.Exists(file))
            {
                lock (s_namespaceCache) { s_namespaceCache[file] = null; }
                return null;
            }
            if (s_namespaceCache.TryGetValue(file, out var cached)) return cached;

            string? result = null;
            try
            {
                using var reader = new StreamReader(file);
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (line == null) break;
                    string trimmed = line.TrimStart();
                    if (trimmed.Length == 0) continue;
                    if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    {
                        // Keep scanning; a block comment spanning lines is rare
                        // before a namespace and not worth tracking here.
                        continue;
                    }
                    if (trimmed.StartsWith("namespace", StringComparison.OrdinalIgnoreCase))
                    {
                        string rest = trimmed.Substring("namespace".Length).TrimStart();
                        int semi = rest.IndexOf(';');
                        if (semi > 0)
                        {
                            result = rest.Substring(0, semi).Trim();
                            break;
                        }
                    }
                    // A real statement that isn't a namespace declaration is
                    // enough to stop: imports/contracts follow the namespace.
                    break;
                }
            }
            catch
            {
                result = null;
            }

            lock (s_namespaceCache) { s_namespaceCache[file] = result; }
            return result;
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
