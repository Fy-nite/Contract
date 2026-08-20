using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// String helpers. A generic ObjektRT stdlib module — the language's string
/// '+' operator lowers to <see cref="Concat"/>.
/// </summary>
[ClassBinding("String")]
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

    /// <summary>True when <paramref name="str"/> contains <paramref name="sub"/>.</summary>
    public static bool Contains(string str, string sub) => str.Contains(sub, global::System.StringComparison.Ordinal);

    /// <summary>True when <paramref name="str"/> is null or empty.</summary>
    public static bool IsNullOrEmpty(string str) => string.IsNullOrEmpty(str);

    /// <summary>True when <paramref name="str"/> is null, empty, or only whitespace.</summary>
    public static bool IsNullOrWhitespace(string str) => string.IsNullOrWhiteSpace(str);

    /// <summary>Joins the elements of a string array into one string with <paramref name="separator"/> between them.</summary>
    public static string Join(string separator, string[] values) => string.Join(separator, values);

    /// <summary>Pads <paramref name="str"/> on the left with spaces to the given <paramref name="totalWidth"/>.</summary>
    public static string PadLeft(string str, int totalWidth) => str.PadLeft(totalWidth);

    /// <summary>Pads <paramref name="str"/> on the right with spaces to the given <paramref name="totalWidth"/>.</summary>
    public static string PadRight(string str, int totalWidth) => str.PadRight(totalWidth);

    /// <summary>Removes leading whitespace from <paramref name="str"/>.</summary>
    public static string TrimStart(string str) => str.TrimStart();

    /// <summary>Removes trailing whitespace from <paramref name="str"/>.</summary>
    public static string TrimEnd(string str) => str.TrimEnd();

    /// <summary>Returns the zero-based index of the last occurrence of <paramref name="sub"/> in <paramref name="str"/>, or -1 when not found.</summary>
    public static int LastIndexOf(string str, string sub) => str.LastIndexOf(sub, global::System.StringComparison.Ordinal);

    /// <summary>Returns <paramref name="str"/> repeated <paramref name="count"/> times.</summary>
    public static string Repeat(string str, int count) => string.Concat(global::System.Linq.Enumerable.Repeat(str, count));

    /// <summary>Compares two strings lexicographically: negative when a &lt; b, 0 when equal, positive when a &gt; b.</summary>
    public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

    /// <summary>Returns the character at <paramref name="index"/> as a single-character string.</summary>
    public static string CharAt(string str, int index) => str[index].ToString();

    /// <summary>Returns the zero-based index of the first occurrence of <paramref name="sub"/> at or after <paramref name="start"/>, or -1 when not found.</summary>
    public static int IndexOfFrom(string str, string sub, int start)
        => str.IndexOf(sub, start, global::System.StringComparison.Ordinal);
}
