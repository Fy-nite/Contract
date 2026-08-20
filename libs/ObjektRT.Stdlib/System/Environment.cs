using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>OS environment access. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Environment")]
public static class Environment
{
    /// <summary>Returns the value of the environment variable <paramref name="name"/>, or "" when unset.</summary>
    public static string GetEnv(string name) => global::System.Environment.GetEnvironmentVariable(name) ?? "";

    /// <summary>Terminates the process with the given exit <paramref name="code"/>.</summary>
    public static void Exit(int code) => global::System.Environment.Exit(code);
}
