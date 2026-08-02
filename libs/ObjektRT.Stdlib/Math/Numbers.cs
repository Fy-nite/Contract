namespace ObjektRT.Stdlib.Math;

/// <summary>Numeric helpers. A generic ObjektRT stdlib module.</summary>
public static class Numbers
{
    /// <summary>Absolute value of an integer.</summary>
    public static int Abs(int value) => global::System.Math.Abs(value);

    /// <summary>Absolute value of a float.</summary>
    public static float AbsF(float value) => global::System.MathF.Abs(value);

    /// <summary>Square root of a float.</summary>
    public static float Sqrt(float value) => global::System.MathF.Sqrt(value);

    /// <summary>The smaller of two integers.</summary>
    public static int Min(int a, int b) => global::System.Math.Min(a, b);

    /// <summary>The larger of two integers.</summary>
    public static int Max(int a, int b) => global::System.Math.Max(a, b);

    /// <summary>Raises x to the power y.</summary>
    public static float Pow(float x, float y) => global::System.MathF.Pow(x, y);

    /// <summary>Largest integer less than or equal to value.</summary>
    public static int Floor(float value) => (int)global::System.MathF.Floor(value);

    /// <summary>Smallest integer greater than or equal to value.</summary>
    public static int Ceiling(float value) => (int)global::System.MathF.Ceiling(value);

    /// <summary>Rounds to the nearest integer.</summary>
    public static int Round(float value) => (int)global::System.MathF.Round(value);

    /// <summary>Sine of an angle in radians.</summary>
    public static float Sin(float value) => global::System.MathF.Sin(value);

    /// <summary>Cosine of an angle in radians.</summary>
    public static float Cos(float value) => global::System.MathF.Cos(value);
}
