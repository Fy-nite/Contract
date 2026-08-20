using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Base64 encoding helpers. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Base64")]
public static class Base64
{
    /// <summary>Encodes a string to a Base64 string.</summary>
    public static string Encode(string value)
        => global::System.Convert.ToBase64String(global::System.Text.Encoding.UTF8.GetBytes(value));

    /// <summary>Decodes a Base64 string back to a string.</summary>
    public static string Decode(string value)
        => global::System.Text.Encoding.UTF8.GetString(global::System.Convert.FromBase64String(value));

    /// <summary>True when <paramref name="value"/> is valid Base64.</summary>
    public static bool IsValid(string value)
    {
        try { global::System.Convert.FromBase64String(value); return true; }
        catch { return false; }
    }
}
