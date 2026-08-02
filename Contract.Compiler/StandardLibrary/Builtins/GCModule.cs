using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// GC module — garbage collection control (mirrors ObjectIR's native GC class).
    /// </summary>
    [ClassBinding("GC")]
    public static class GCModule
    {
        /// <summary>Requests a garbage collection.</summary>
        [MethodBinding]
        public static void Collect() => System.GC.Collect();
    }
}
