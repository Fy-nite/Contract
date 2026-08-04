namespace ObjektRT.Stdlib.System;

/// <summary>
/// String helpers. A generic ObjektRT stdlib module — the language's string
/// '+' operator lowers to <see cref="Concat"/>.
/// </summary>
public static class String
{
    /// <summary>Returns the number of characters in <paramref name="str"/>.</summary>
    public static int Length(string str) => str.Length;

    /// <summary>Concatenates two strings. This is also what the language's '+' operator on strings lowers to.</summary>
    public static string Concat(string a, string b) => string.Concat(a, b);

    /// <summary>Returns the substring of <paramref name="str"/> starting at <paramref name="start"/> with the given <paramref name="length"/>.</summary>
    public static string Substring(string str, int start, int length) => str.Substring(start, length);

    /// <summary>Returns the zero-based index of the first occurrence of <paramref name="sub"/> in <paramref name="str"/>, or -1 when not found.</summary>
    public static int IndexOf(string str, string sub) => str.IndexOf(sub, global::System.StringComparison.Ordinal);

    /// <summary>True when <paramref name="str"/> starts with <paramref name="prefix"/>.</summary>
    public static bool StartsWith(string str, string prefix) => str.StartsWith(prefix, global::System.StringComparison.Ordinal);

    /// <summary>True when <paramref name="str"/> ends with <paramref name="suffix"/>.</summary>
    public static bool EndsWith(string str, string suffix) => str.EndsWith(suffix, global::System.StringComparison.Ordinal);

    /// <summary>Removes leading and trailing whitespace from <paramref name="str"/>.</summary>
    public static string Trim(string str) => str.Trim();

    /// <summary>Returns <paramref name="str"/> with all characters converted to upper case.</summary>
    public static string ToUpper(string str) => str.ToUpperInvariant();

    /// <summary>Returns <paramref name="str"/> with all characters converted to lower case.</summary>
    public static string ToLower(string str) => str.ToLowerInvariant();

    /// <summary>Replaces every occurrence of <paramref name="old"/> in <paramref name="str"/> with <paramref name="new_"/>.</summary>
    public static string Replace(string str, string old, string new_) => str.Replace(old, new_);

    /// <summary>Splits <paramref name="str"/> into an array of substrings separated by <paramref name="separator"/>.</summary>
    public static string[] Split(string str, string separator)
        => str.Split(new[] { separator }, global::System.StringSplitOptions.None);
}
