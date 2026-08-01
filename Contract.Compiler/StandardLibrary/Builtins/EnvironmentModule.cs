using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Environment module — OS environment access (mirrors ObjectIR's native Environment class).
    /// </summary>
    [ClassBinding("Environment")]
    public static class EnvironmentModule
    {
        [MethodBinding]
        public static string GetEnv(string name) => System.Environment.GetEnvironmentVariable(name) ?? "";

        [MethodBinding]
        public static void Exit(int code) => System.Environment.Exit(code);
    }
}
