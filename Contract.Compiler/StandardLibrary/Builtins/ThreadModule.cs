using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Thread module — threading / sleeping (mirrors ObjectIR's native Thread class).
    /// Spawn's implementation lives in the runtime host (ObjectRT.Runtime), which
    /// registers a real "Thread.Spawn" native that runs the delegate on a new
    /// thread sharing the module state. The stub here exists so the language's
    /// semantic analysis accepts the call; the host binding takes precedence at
    /// dispatch time.
    /// </summary>
    [ClassBinding("Thread")]
    public static class ThreadModule
    {
        [MethodBinding]
        public static void Sleep(int ms) => System.Threading.Thread.Sleep(ms);

        [MethodBinding]
        public static void Spawn(object d)
        {
            // Runtime host overrides this via an explicit native binding.
        }
    }
}
