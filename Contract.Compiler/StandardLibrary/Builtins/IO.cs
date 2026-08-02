using System;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// IO module — console input/output (mirrors ObjectIR's native IO class).
    /// </summary>
    [ClassBinding("IO")]
    public static class IO
    {
        /// <summary>Prints a value to standard output followed by a newline.</summary>
        [MethodBinding]
        public static void Println(object contents)
        {
            Console.WriteLine(contents);
        }

        /// <summary>Prints a value to standard output without a trailing newline.</summary>
        [MethodBinding]
        public static void Print(object contents)
        {
            Console.Write(contents);
        }
        /// <summary>Reads a single line from standard input. The parameter is currently unused.</summary>
        [MethodBinding]
        public static string Readln(string args)
        {
            return Console.ReadLine();
        }
    }
}
