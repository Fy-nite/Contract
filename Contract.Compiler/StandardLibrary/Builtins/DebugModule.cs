using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Debug module — assertions (mirrors ObjectIR's native Debug class).
    /// </summary>
    [ClassBinding("Debug")]
    public static class DebugModule
    {
        [MethodBinding]
        public static void Assert(bool condition, string message) => System.Diagnostics.Debug.Assert(condition, message);
    }
}
