using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Random number helpers. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Random")]
public static class Random
{
    /// <summary>Random integer in [0, <paramref name="max"/>).</summary>
    public static int NextInt(int max) => global::System.Random.Shared.Next(max);

    /// <summary>Random integer in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public static int NextIntRange(int min, int max) => global::System.Random.Shared.Next(min, max);

    /// <summary>Random float in [0.0, 1.0).</summary>
    public static float NextFloat() => global::System.Random.Shared.NextSingle();

    /// <summary>Random float in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public static float NextFloatRange(float min, float max) => min + (max - min) * global::System.Random.Shared.NextSingle();

    /// <summary>Random double in [0.0, 1.0).</summary>
    public static double NextDouble() => global::System.Random.Shared.NextDouble();

    /// <summary>Random true or false.</summary>
    public static bool NextBool() => global::System.Random.Shared.Next(2) == 0;

    /// <summary>Returns a random element of the array, or null when empty.</summary>
    public static object Choice(object[] arr)
        => arr.Length == 0 ? null! : arr[global::System.Random.Shared.Next(arr.Length)];

    /// <summary>Returns a random element of a string array, or "" when empty.</summary>
    public static string ChoiceString(string[] arr)
        => arr.Length == 0 ? "" : arr[global::System.Random.Shared.Next(arr.Length)];

    /// <summary>Returns a random integer in [0, <paramref name="max"/>] inclusive.</summary>
    public static int NextIntInclusive(int max) => global::System.Random.Shared.Next(max + 1);
}
