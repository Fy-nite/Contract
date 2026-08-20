using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>File system I/O. A generic ObjektRT stdlib module.</summary>
[ClassBinding("File")]
public static class File
{
    /// <summary>Reads the entire file at <paramref name="path"/> as a string.</summary>
    public static string ReadAllText(string path) => global::System.IO.File.ReadAllText(path);

    /// <summary>Writes <paramref name="contents"/> to the file at <paramref name="path"/> (overwrites).</summary>
    public static void WriteAllText(string path, string contents) => global::System.IO.File.WriteAllText(path, contents);

    /// <summary>True when a file exists at <paramref name="path"/>.</summary>
    public static bool Exists(string path) => global::System.IO.File.Exists(path);

    /// <summary>Reads all lines of the file at <paramref name="path"/> as an array of strings.</summary>
    public static string[] ReadAllLines(string path) => global::System.IO.File.ReadAllLines(path);

    /// <summary>Copies the file at <paramref name="src"/> to <paramref name="dst"/>.</summary>
    public static void Copy(string src, string dst) => global::System.IO.File.Copy(src, dst);

    /// <summary>Moves the file at <paramref name="src"/> to <paramref name="dst"/>.</summary>
    public static void Move(string src, string dst) => global::System.IO.File.Move(src, dst);

    /// <summary>Deletes the file at <paramref name="path"/>.</summary>
    public static void Delete(string path) => global::System.IO.File.Delete(path);
}
