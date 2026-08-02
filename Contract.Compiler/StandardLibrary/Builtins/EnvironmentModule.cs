using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Environment module — OS environment access (mirrors ObjectIR's native Environment class).
    /// </summary>
    [ClassBinding("Environment")]
    public static class EnvironmentModule
    {
        /// <summary>Returns the value of the environment variable <paramref name="name"/>, or "" when unset.</summary>
        [MethodBinding]
        public static string GetEnv(string name) => System.Environment.GetEnvironmentVariable(name) ?? "";

        /// <summary>Terminates the process with the given exit <paramref name="code"/>.</summary>
        [MethodBinding]
        public static void Exit(int code) => System.Environment.Exit(code);
    }
}
