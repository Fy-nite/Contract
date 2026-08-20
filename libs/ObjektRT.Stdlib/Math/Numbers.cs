using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.Math;

/// <summary>Numeric helpers. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Math")]
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

    /// <summary>The smaller of two floats.</summary>
    public static float MinF(float a, float b) => global::System.MathF.Min(a, b);

    /// <summary>The larger of two floats.</summary>
    public static float MaxF(float a, float b) => global::System.MathF.Max(a, b);

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

    /// <summary>Tangent of an angle in radians.</summary>
    public static float Tan(float value) => global::System.MathF.Tan(value);

    /// <summary>Natural logarithm of value.</summary>
    public static float Log(float value) => global::System.MathF.Log(value);

    /// <summary>Base-10 logarithm of value.</summary>
    public static float Log10(float value) => global::System.MathF.Log10(value);

    /// <summary>e raised to the power value.</summary>
    public static float Exp(float value) => global::System.MathF.Exp(value);

    /// <summary>Clamps <paramref name="value"/> to the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static int Clamp(int value, int min, int max) => global::System.Math.Clamp(value, min, max);

    /// <summary>Clamps a double to the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static double Clamp(double value, double min, double max) => global::System.Math.Clamp(value, min, max);

    /// <summary>Clamps a double to the inclusive range [0, 1].</summary>
    public static double Clamp01(double value) => global::System.Math.Clamp(value, 0.0, 1.0);

    /// <summary>Returns -1, 0, or 1 indicating the sign of <paramref name="value"/>.</summary>
    public static int Sign(int value) => global::System.Math.Sign(value);

    /// <summary>Returns -1, 0, or 1 indicating the sign of a double.</summary>
    public static int Sign(double value) => global::System.Math.Sign(value);

    /// <summary>Truncates a double toward zero to an integer.</summary>
    public static int Truncate(double value) => (int)global::System.Math.Truncate(value);

    /// <summary>Linearly interpolates between <paramref name="a"/> and <paramref name="b"/> by <paramref name="t"/> (t in [0,1]).</summary>
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Converts degrees to radians.</summary>
    public static double DegToRad(double degrees) => degrees * (global::System.Math.PI / 180.0);

    /// <summary>Converts radians to degrees.</summary>
    public static double RadToDeg(double radians) => radians * (180.0 / global::System.Math.PI);

    /// <summary>Arc tangent of y/x, in radians.</summary>
    public static double Atan2(double y, double x) => global::System.Math.Atan2(y, x);

    /// <summary>Arc sine of value, in radians.</summary>
    public static double Asin(double value) => global::System.Math.Asin(value);

    /// <summary>Arc cosine of value, in radians.</summary>
    public static double Acos(double value) => global::System.Math.Acos(value);

    /// <summary>Arc tangent of value, in radians.</summary>
    public static double Atan(double value) => global::System.Math.Atan(value);

    /// <summary>Base-2 logarithm of value.</summary>
    public static double Log2(double value) => global::System.Math.Log2(value);

    /// <summary>Logarithm of value in the given base.</summary>
    public static double LogBase(double value, double newBase) => global::System.Math.Log(value, newBase);

    /// <summary>True when value is NaN (not a number).</summary>
    public static bool IsNaN(double value) => double.IsNaN(value);

    /// <summary>True when value is positive or negative infinity.</summary>
    public static bool IsInfinity(double value) => double.IsInfinity(value);

    /// <summary>The mathematical constant pi (3.14159...).</summary>
    public static double PI() => global::System.Math.PI;

    /// <summary>The base of natural logarithms (2.71828...).</summary>
    public static double E() => global::System.Math.E;
}
