using System;
using Contract.Compiler.StandardLibrary;

namespace Contract.CustomBindings
{
    /// <summary>
    /// Example custom host binding: callable from Contract as Greeter.SayHi().
    /// Loaded via `contract --bind CustomBindings.dll`.
    /// </summary>
    [ClassBinding("Greeter")]
    public static class Greeter
    {
        public static void SayHi() => Console.WriteLine("hi from a custom binding!");
    }
}
