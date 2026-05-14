using System;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    [ClassBinding("IO")]
    public static class IO
    {
        [MethodBinding]
        public static void Println(object contents)
        {
            Console.WriteLine(contents);
        }

        [MethodBinding]
        public static void Print(object contents)
        {
            Console.Write(contents);
        }
        [MethodBinding]
        public static string Readln(string args)
        {
            return Console.ReadLine();
        }
    }
}
