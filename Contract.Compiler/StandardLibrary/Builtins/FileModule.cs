using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// File module — file system I/O (mirrors ObjectIR's native File class).
    /// </summary>
    [ClassBinding("File")]
    public static class FileModule
    {
        [MethodBinding]
        public static string ReadAllText(string path) => System.IO.File.ReadAllText(path);

        [MethodBinding]
        public static void WriteAllText(string path, string contents) => System.IO.File.WriteAllText(path, contents);

        [MethodBinding]
        public static bool Exists(string path) => System.IO.File.Exists(path);

        [MethodBinding]
        public static string[] ReadAllLines(string path) => System.IO.File.ReadAllLines(path);

        [MethodBinding]
        public static void Copy(string src, string dst) => System.IO.File.Copy(src, dst);

        [MethodBinding]
        public static void Move(string src, string dst) => System.IO.File.Move(src, dst);

        [MethodBinding]
        public static void Delete(string path) => System.IO.File.Delete(path);
    }
}
