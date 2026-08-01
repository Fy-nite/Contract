using System;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Random module — mirrors ObjectIR's native Random class.
    /// </summary>
    [ClassBinding("Random")]
    public static class RandomModule
    {
        [MethodBinding]
        public static int NextInt(int max) => Random.Shared.Next(max);

        [MethodBinding]
        public static float NextFloat() => Random.Shared.NextSingle();
    }
}
