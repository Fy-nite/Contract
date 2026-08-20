using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Directory operations. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Directory")]
public static class Directory
{
    /// <summary>True when a directory exists at <paramref name="path"/>.</summary>
    public static bool Exists(string path) => global::System.IO.Directory.Exists(path);

    /// <summary>Creates a directory (and any missing parents) at <paramref name="path"/>.</summary>
    public static void Create(string path) => global::System.IO.Directory.CreateDirectory(path);

    /// <summary>Deletes the directory at <paramref name="path"/> (recursively when <paramref name="recursive"/>).</summary>
    public static void Delete(string path, bool recursive) => global::System.IO.Directory.Delete(path, recursive);

    /// <summary>Returns the names of files in the directory at <paramref name="path"/>.</summary>
    public static string[] GetFiles(string path) => global::System.IO.Directory.GetFiles(path);

    /// <summary>Returns the names of subdirectories in the directory at <paramref name="path"/>.</summary>
    public static string[] GetDirectories(string path) => global::System.IO.Directory.GetDirectories(path);

    /// <summary>Returns the current working directory.</summary>
    public static string GetCurrentDirectory() => global::System.IO.Directory.GetCurrentDirectory();

    /// <summary>Sets the current working directory.</summary>
    public static void SetCurrentDirectory(string path) => global::System.IO.Directory.SetCurrentDirectory(path);

    /// <summary>Moves a directory from <paramref name="src"/> to <paramref name="dst"/>.</summary>
    public static void Move(string src, string dst) => global::System.IO.Directory.Move(src, dst);
}
