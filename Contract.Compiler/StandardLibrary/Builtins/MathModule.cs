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
        [MethodBinding]
        public static float Sqrt(float value) => MathF.Sqrt(value);

        [MethodBinding]
        public static int Abs(int value) => Math.Abs(value);

        [MethodBinding]
        public static float AbsF(float value) => MathF.Abs(value);

        [MethodBinding]
        public static int Min(int a, int b) => Math.Min(a, b);

        [MethodBinding]
        public static float MinF(float a, float b) => MathF.Min(a, b);

        [MethodBinding]
        public static int Max(int a, int b) => Math.Max(a, b);

        [MethodBinding]
        public static float MaxF(float a, float b) => MathF.Max(a, b);

        [MethodBinding]
        public static float Pow(float x, float y) => MathF.Pow(x, y);

        [MethodBinding]
        public static int Floor(float value) => (int)MathF.Floor(value);

        [MethodBinding]
        public static int Ceiling(float value) => (int)MathF.Ceiling(value);

        [MethodBinding]
        public static int Round(float value) => (int)MathF.Round(value);

        [MethodBinding]
        public static float Sin(float value) => MathF.Sin(value);

        [MethodBinding]
        public static float Cos(float value) => MathF.Cos(value);

        [MethodBinding]
        public static float Tan(float value) => MathF.Tan(value);

        [MethodBinding]
        public static float Log(float value) => MathF.Log(value);

        [MethodBinding]
        public static float Log10(float value) => MathF.Log10(value);

        [MethodBinding]
        public static float Exp(float value) => MathF.Exp(value);
    }
}
