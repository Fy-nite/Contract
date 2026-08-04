namespace ObjektRT.Stdlib.System;

/// <summary>Type conversion helpers. A generic ObjektRT stdlib module.</summary>
public static class Convert
{
    /// <summary>Parses <paramref name="value"/> as a base-10 integer.</summary>
    public static int ToInt32(string value) => int.Parse(value);

    /// <summary>Formats an integer as a string.</summary>
    public static string ToString(int value) => value.ToString();

    /// <summary>Formats a float as a string using the invariant culture (always '.' as the decimal separator).</summary>
    public static string ToStringF(float value) => value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

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
}
