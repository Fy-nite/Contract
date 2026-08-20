using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Path manipulation. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Path")]
public static class Path
{
    /// <summary>Combines two path segments into one path.</summary>
    public static string Combine(string a, string b) => global::System.IO.Path.Combine(a, b);

    /// <summary>Returns the file name (with extension) of a path.</summary>
    public static string GetFileName(string path) => global::System.IO.Path.GetFileName(path);

    /// <summary>Returns the file name without its extension.</summary>
    public static string GetFileNameWithoutExtension(string path) => global::System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>Returns the extension (including the dot) of a path, or "" when none.</summary>
    public static string GetExtension(string path) => global::System.IO.Path.GetExtension(path);

    /// <summary>Returns the directory portion of a path.</summary>
    public static string GetDirectoryName(string path) => global::System.IO.Path.GetDirectoryName(path) ?? "";

    /// <summary>Returns the full (absolute) path for a relative path.</summary>
    public static string GetFullPath(string path) => global::System.IO.Path.GetFullPath(path);

    /// <summary>True when the path is rooted (absolute).</summary>
    public static bool IsPathRooted(string path) => global::System.IO.Path.IsPathRooted(path);

    /// <summary>Returns the path separator character for the platform as a string.</summary>
    public static string DirectorySeparator() => global::System.IO.Path.DirectorySeparatorChar.ToString();

    /// <summary>Changes the extension of a path to <paramref name="extension"/> (with or without the leading dot).</summary>
    public static string ChangeExtension(string path, string extension) => global::System.IO.Path.ChangeExtension(path, extension);
}
