namespace ObjektRT.Stdlib.System;

/// <summary>Random number helpers. A generic ObjektRT stdlib module.</summary>
public static class Random
{
    /// <summary>Random integer in [0, <paramref name="max"/>).</summary>
    public static int NextInt(int max) => global::System.Random.Shared.Next(max);

    /// <summary>Random float in [0.0, 1.0).</summary>
    public static float NextFloat() => global::System.Random.Shared.NextSingle();
}
