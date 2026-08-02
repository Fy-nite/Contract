using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// File module — file system I/O (mirrors ObjectIR's native File class).
    /// </summary>
    [ClassBinding("File")]
    public static class FileModule
    {
        /// <summary>Reads the entire file at <paramref name="path"/> as a string.</summary>
        [MethodBinding]
        public static string ReadAllText(string path) => System.IO.File.ReadAllText(path);

        /// <summary>Writes <paramref name="contents"/> to the file at <paramref name="path"/> (overwrites).</summary>
        [MethodBinding]
        public static void WriteAllText(string path, string contents) => System.IO.File.WriteAllText(path, contents);

        /// <summary>True when a file exists at <paramref name="path"/>.</summary>
        [MethodBinding]
        public static bool Exists(string path) => System.IO.File.Exists(path);

        /// <summary>Reads all lines of the file at <paramref name="path"/> as an array of strings.</summary>
        [MethodBinding]
        public static string[] ReadAllLines(string path) => System.IO.File.ReadAllLines(path);

        /// <summary>Copies the file at <paramref name="src"/> to <paramref name="dst"/>.</summary>
        [MethodBinding]
        public static void Copy(string src, string dst) => System.IO.File.Copy(src, dst);

        /// <summary>Moves the file at <paramref name="src"/> to <paramref name="dst"/>.</summary>
        [MethodBinding]
        public static void Move(string src, string dst) => System.IO.File.Move(src, dst);

        /// <summary>Deletes the file at <paramref name="path"/>.</summary>
        [MethodBinding]
        public static void Delete(string path) => System.IO.File.Delete(path);
    }
}
