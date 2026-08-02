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
        /// <summary>Parses <paramref name="value"/> as a base-10 integer.</summary>
        [MethodBinding]
        public static int ToInt32(string value) => int.Parse(value);

        /// <summary>Formats an integer as a string.</summary>
        [MethodBinding]
        public static string ToString(int value) => value.ToString();

        /// <summary>Formats a float as a string using the invariant culture (always '.' as the decimal separator).</summary>
        [MethodBinding]
        public static string ToStringF(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Formats a bool as "True" or "False".</summary>
        [MethodBinding]
        public static string ToStringB(bool value) => value.ToString();

        /// <summary>Parses <paramref name="value"/> as a float.</summary>
        [MethodBinding]
        public static float ToFloat32(string value) => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Truncates a float to an integer.</summary>
        [MethodBinding]
        public static int ToInt32F(float value) => (int)value;

        /// <summary>Converts an integer to a float.</summary>
        [MethodBinding]
        public static float ToFloat32I(int value) => value;

        /// <summary>Parses <paramref name="value"/> as "true" or "false" (case-insensitive).</summary>
        [MethodBinding]
        public static bool ToBool(string value) => bool.Parse(value);
    }
}
