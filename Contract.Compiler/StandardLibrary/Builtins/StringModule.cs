using System;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// String module — mirrors ObjectIR's native String class.
    /// In the current runtime, string concatenation (the language's '+' operator on
    /// strings) lowers to String.Concat rather than the add opcode.
    /// </summary>
    [ClassBinding("String")]
    public static class StringModule
    {
        /// <summary>Returns the number of characters in <paramref name="str"/>.</summary>
        [MethodBinding]
        public static int Length(string str) => str.Length;

        /// <summary>Concatenates two strings. This is also what the language's '+' operator on strings lowers to.</summary>
        [MethodBinding]
        public static string Concat(string a, string b) => string.Concat(a, b);

        /// <summary>Returns the substring of <paramref name="str"/> starting at <paramref name="start"/> with the given <paramref name="length"/>.</summary>
        [MethodBinding]
        public static string Substring(string str, int start, int length) => str.Substring(start, length);

        /// <summary>Returns the zero-based index of the first occurrence of <paramref name="sub"/> in <paramref name="str"/>, or -1 when not found.</summary>
        [MethodBinding]
        public static int IndexOf(string str, string sub) => str.IndexOf(sub, StringComparison.Ordinal);

        /// <summary>True when <paramref name="str"/> starts with <paramref name="prefix"/>.</summary>
        [MethodBinding]
        public static bool StartsWith(string str, string prefix) => str.StartsWith(prefix, StringComparison.Ordinal);

        /// <summary>True when <paramref name="str"/> ends with <paramref name="suffix"/>.</summary>
        [MethodBinding]
        public static bool EndsWith(string str, string suffix) => str.EndsWith(suffix, StringComparison.Ordinal);

        /// <summary>Removes leading and trailing whitespace from <paramref name="str"/>.</summary>
        [MethodBinding]
        public static string Trim(string str) => str.Trim();

        /// <summary>Returns <paramref name="str"/> with all characters converted to upper case.</summary>
        [MethodBinding]
        public static string ToUpper(string str) => str.ToUpperInvariant();

        /// <summary>Returns <paramref name="str"/> with all characters converted to lower case.</summary>
        [MethodBinding]
        public static string ToLower(string str) => str.ToLowerInvariant();

        /// <summary>Replaces every occurrence of <paramref name="old"/> in <paramref name="str"/> with <paramref name="new_"/>.</summary>
        [MethodBinding]
        public static string Replace(string str, string old, string new_) => str.Replace(old, new_);

        /// <summary>Splits <paramref name="str"/> into an array of substrings separated by <paramref name="separator"/>.</summary>
        [MethodBinding]
        public static string[] Split(string str, string separator) => str.Split(new[] { separator }, StringSplitOptions.None);
    }
}
