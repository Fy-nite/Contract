using System;
using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.Attributes;

namespace Contract.CustomBindings
{
    /// <summary>
    /// Example custom host binding: callable from Contract as Greeter.SayHi().
    /// Loaded via `contract --bind CustomBindings.dll`.
    /// </summary>
    [ClassBinding("Greeter")]
    public static class Greeter
    {
        [MethodBinding]
        public static void SayHi() => Console.WriteLine("hi from a custom binding!");

        [MethodBinding]
        public static int Add(int a, int b) => a + b;
    }
}
