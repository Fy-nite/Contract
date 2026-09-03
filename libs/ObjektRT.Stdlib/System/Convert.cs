using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Type conversion helpers. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Convert")]
public static class Convert
{
    /// <summary>Parses <paramref name="value"/> as a base-10 integer.</summary>
    public static int ToInt32(string value) => int.Parse(value);

    /// <summary>Formats an integer as a string.</summary>
    public static string ToString(int value) => value.ToString();

    /// <summary>Formats a float as a string using the invariant culture (always '.' as the decimal separator).</summary>
    public static string ToStringF(float value) => value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Formats a double as a string using the invariant culture (always '.' as the decimal separator).</summary>
    public static string ToStringD(double value) => value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Truncates a double to an integer.</summary>
    public static int ToInt32D(double value) => (int)value;

    /// <summary>Formats a bool as "True" or "False".</summary>
    public static string ToStringB(bool value) => value.ToString();

    /// <summary>Parses <paramref name="value"/> as a float.</summary>
    public static float ToFloat32(string value) => float.Parse(value, global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Truncates a float to an integer.</summary>
    public static int ToInt32F(float value) => (int)value;

    /// <summary>Converts an integer to a float.</summary>
    public static float ToFloat32I(int value) => value;

    /// <summary>Parses <paramref name="value"/> as "true" or "false" (case-insensitive).</summary>
    public static bool ToBool(string value) => bool.Parse(value);

    /// <summary>Parses <paramref name="value"/> as a 64-bit integer.</summary>
    public static long ToInt64(string value) => long.Parse(value);

    /// <summary>Formats a 64-bit integer as a string.</summary>
    public static string ToStringL(long value) => value.ToString();

    /// <summary>Parses <paramref name="value"/> as a double.</summary>
    public static double ToDouble(string value) => double.Parse(value, global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Formats a double as a string using the invariant culture.</summary>
    public static string ToStringD2(double value) => value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Formats an integer as a hexadecimal string (e.g. 255 → "FF").</summary>
    public static string ToHexString(int value) => value.ToString("X");

    /// <summary>Parses a hexadecimal string as an integer (e.g. "FF" → 255).</summary>
    public static int FromHexString(string value) => global::System.Convert.ToInt32(value, 16);

    /// <summary>True when <paramref name="value"/> can be parsed as an integer.</summary>
    public static bool TryToInt32(string value) => int.TryParse(value, out _);

    /// <summary>True when <paramref name="value"/> can be parsed as a float.</summary>
    public static bool TryToFloat32(string value) => float.TryParse(value, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out _);

    /// <summary>True when <paramref name="value"/> can be parsed as a double.</summary>
    public static bool TryToDouble(string value) => double.TryParse(value, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out _);

    /// <summary>True when <paramref name="value"/> is "true" or "false" (case-insensitive).</summary>
    public static bool TryToBool(string value) => bool.TryParse(value, out _);

    /// <summary>Encodes <paramref name="value"/> to its UTF-8 byte representation.</summary>
    public static byte[] ToUTF8Bytes(string value) => global::System.Text.Encoding.UTF8.GetBytes(value);

    /// <summary>Decodes a UTF-8 byte buffer to a string.</summary>
    public static string ToUTF8String(byte[] value) => global::System.Text.Encoding.UTF8.GetString(value);
}
