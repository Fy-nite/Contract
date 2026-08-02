using System;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Math module — mirrors ObjectRT's native Math class.
    /// </summary>
    [ClassBinding("Math")]
    public static class MathModule
    {
        /// <summary>Square root of <paramref name="value"/>.</summary>
        [MethodBinding]
        public static float Sqrt(float value) => MathF.Sqrt(value);

        /// <summary>Absolute value of an integer.</summary>
        [MethodBinding]
        public static int Abs(int value) => Math.Abs(value);

        /// <summary>Absolute value of a float.</summary>
        [MethodBinding]
        public static float AbsF(float value) => MathF.Abs(value);

        /// <summary>The smaller of two integers.</summary>
        [MethodBinding]
        public static int Min(int a, int b) => Math.Min(a, b);

        /// <summary>The smaller of two floats.</summary>
        [MethodBinding]
        public static float MinF(float a, float b) => MathF.Min(a, b);

        /// <summary>The larger of two integers.</summary>
        [MethodBinding]
        public static int Max(int a, int b) => Math.Max(a, b);

        /// <summary>The larger of two floats.</summary>
        [MethodBinding]
        public static float MaxF(float a, float b) => MathF.Max(a, b);

        /// <summary>Raises <paramref name="x"/> to the power <paramref name="y"/>.</summary>
        [MethodBinding]
        public static float Pow(float x, float y) => MathF.Pow(x, y);

        /// <summary>Largest integer less than or equal to <paramref name="value"/>.</summary>
        [MethodBinding]
        public static int Floor(float value) => (int)MathF.Floor(value);

        /// <summary>Smallest integer greater than or equal to <paramref name="value"/>.</summary>
        [MethodBinding]
        public static int Ceiling(float value) => (int)MathF.Ceiling(value);

        /// <summary>Rounds <paramref name="value"/> to the nearest integer (away from zero on .5).</summary>
        [MethodBinding]
        public static int Round(float value) => (int)MathF.Round(value);

        /// <summary>Sine of an angle in radians.</summary>
        [MethodBinding]
        public static float Sin(float value) => MathF.Sin(value);

        /// <summary>Cosine of an angle in radians.</summary>
        [MethodBinding]
        public static float Cos(float value) => MathF.Cos(value);

        /// <summary>Tangent of an angle in radians.</summary>
        [MethodBinding]
        public static float Tan(float value) => MathF.Tan(value);

        /// <summary>Natural logarithm of <paramref name="value"/>.</summary>
        [MethodBinding]
        public static float Log(float value) => MathF.Log(value);

        /// <summary>Base-10 logarithm of <paramref name="value"/>.</summary>
        [MethodBinding]
        public static float Log10(float value) => MathF.Log10(value);

        /// <summary>e raised to the power <paramref name="value"/>.</summary>
        [MethodBinding]
        public static float Exp(float value) => MathF.Exp(value);
    }
}
