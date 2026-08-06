using System;
using System.Collections.Generic;

namespace Contract.Compiler
{
    /// <summary>
    /// Resolves written type names to their fully-qualified wire names, the way
    /// Java/C#/Python namespaces work: namespace imports map a short name (or
    /// the first segment of a dotted name) onto a full namespace.
    ///
    /// Short name <c>Foo</c>:
    /// <list type="number">
    /// <item>The current contract's own namespace (<c>com.example.Foo</c>).</item>
    /// <item>Each namespace import as a prefix (<c>import com.lib;</c> → <c>com.lib.Foo</c>).</item>
    /// <item>A unique short-name match anywhere in the program.</item>
    /// </list>
    ///
    /// Dotted name <c>Terminal.Terminal</c>:
    /// <list type="number">
    /// <item>Already-qualified names pass through when registered.</item>
    /// <item>
    /// The first segment is mapped through a namespace import
    /// (<c>import ovh.finite.hello.Terminal;</c> → <c>ovh.finite.hello.Terminal.Terminal</c>).
    /// </item>
    /// <item>The whole dotted name is prefixed with each namespace import
    /// (<c>import com.lib;</c> + <c>Direction.North</c> → <c>com.lib.Direction.North</c>).</item>
    /// </list>
    /// </summary>
    public static class TypeNameResolver
    {
        /// <summary>
        /// Resolves a possibly-short type name to its fully-qualified wire name.
        /// </summary>
        /// <param name="name">The written type name (short or dotted).</param>
        /// <param name="namespaceImports">The program's namespace imports, in order.</param>
        /// <param name="hasType">True when a type with the given qualified name is registered.</param>
        /// <param name="currentNamespace">The current contract's namespace, or null.</param>
        /// <param name="uniqueShortMatch">The single qualified name for a short name, or null when ambiguous/absent.</param>
        /// <param name="onImportUsed">
        /// Invoked with the index (into <paramref name="namespaceImports"/>) of each
        /// import that actually resolved a short/first-segment name — lets the
        /// analyzer flag unused imports.
        /// </param>
        public static string Resolve(
            string name,
            IReadOnlyList<string> namespaceImports,
            Func<string, bool> hasType,
            string? currentNamespace = null,
            Func<string, string?>? uniqueShortMatch = null,
            Action<int>? onImportUsed = null)
        {
            if (string.IsNullOrEmpty(name)) return name;

            if (name.Contains('.'))
            {
                if (hasType(name)) return name;

                int dot = name.IndexOf('.');
                string first = name.Substring(0, dot);
                string rest = name.Substring(dot + 1);

                for (int i = 0; i < namespaceImports.Count; i++)
                {
                    string ns = namespaceImports[i];
                    if (string.IsNullOrEmpty(ns)) continue;

                    // First-segment mapping: import ovh.finite.hello.Terminal;
                    // + Terminal.Terminal → ovh.finite.hello.Terminal.Terminal.
                    if (ns == first || (ns.Length > first.Length && ns.EndsWith("." + first, StringComparison.Ordinal)))
                    {
                        string candidate = ns + "." + rest;
                        if (hasType(candidate))
                        {
                            onImportUsed?.Invoke(i);
                            return candidate;
                        }
                    }

                    // Whole-name prefixing: import com.lib; + Direction.North
                    // → com.lib.Direction.North.
                    string whole = ns + "." + name;
                    if (hasType(whole))
                    {
                        onImportUsed?.Invoke(i);
                        return whole;
                    }
                }

                return name;
            }

            // Short name.
            if (!string.IsNullOrEmpty(currentNamespace))
            {
                string candidate = currentNamespace + "." + name;
                if (hasType(candidate)) return candidate;
            }

            for (int i = 0; i < namespaceImports.Count; i++)
            {
                string ns = namespaceImports[i];
                if (string.IsNullOrEmpty(ns)) continue;
                string candidate = ns + "." + name;
                if (hasType(candidate))
                {
                    onImportUsed?.Invoke(i);
                    return candidate;
                }
            }

            if (uniqueShortMatch != null)
            {
                string? full = uniqueShortMatch(name);
                if (full != null) return full;
            }

            return name;
        }
    }
}
