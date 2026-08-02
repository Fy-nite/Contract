using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Debug module — assertions (mirrors ObjectIR's native Debug class).
    /// </summary>
    [ClassBinding("Debug")]
    public static class DebugModule
    {
        /// <summary>Asserts that <paramref name="condition"/> is true, failing with <paramref name="message"/> otherwise.</summary>
        [MethodBinding]
        public static void Assert(bool condition, string message) => System.Diagnostics.Debug.Assert(condition, message);
    }
}
