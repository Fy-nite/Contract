using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Assertions. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Debug")]
public static class Debug
{
    /// <summary>Asserts that <paramref name="condition"/> is true, failing with <paramref name="message"/> otherwise.</summary>
    public static void Assert(bool condition, string message) => global::System.Diagnostics.Debug.Assert(condition, message);
}
