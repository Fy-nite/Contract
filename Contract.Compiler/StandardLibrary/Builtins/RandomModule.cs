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
        /// <summary>Random integer in [0, <paramref name="max"/>).</summary>
        [MethodBinding]
        public static int NextInt(int max) => Random.Shared.Next(max);

        /// <summary>Random float in [0.0, 1.0).</summary>
        [MethodBinding]
        public static float NextFloat() => Random.Shared.NextSingle();
    }
}
