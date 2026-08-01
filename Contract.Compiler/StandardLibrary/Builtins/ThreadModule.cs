using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Thread module — threading / sleeping (mirrors ObjectIR's native Thread class).
    /// Spawn takes a delegate, which the language can't express yet, so only Sleep is bound.
    /// </summary>
    [ClassBinding("Thread")]
    public static class ThreadModule
    {
        [MethodBinding]
        public static void Sleep(int ms) => System.Threading.Thread.Sleep(ms);
    }
}
