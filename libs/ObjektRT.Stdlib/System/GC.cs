using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Garbage collection control. A generic ObjektRT stdlib module.</summary>
[ClassBinding("GC")]
public static class GC
{
    /// <summary>Requests a garbage collection.</summary>
    public static void Collect() => global::System.GC.Collect();
}
