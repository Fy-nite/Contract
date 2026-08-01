using System;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Convert module — mirrors ObjectIR's native Convert class.
    /// </summary>
    [ClassBinding("Convert")]
    public static class ConvertModule
    {
        [MethodBinding]
        public static int ToInt32(string value) => int.Parse(value);

        [MethodBinding]
        public static string ToString(int value) => value.ToString();

        [MethodBinding]
        public static string ToStringF(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        [MethodBinding]
        public static string ToStringB(bool value) => value.ToString();

        [MethodBinding]
        public static float ToFloat32(string value) => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        [MethodBinding]
        public static int ToInt32F(float value) => (int)value;

        [MethodBinding]
        public static float ToFloat32I(int value) => value;

        [MethodBinding]
        public static bool ToBool(string value) => bool.Parse(value);
    }
}
