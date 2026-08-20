using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Globally-unique identifier helpers. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Guid")]
public static class Guid
{
    /// <summary>Returns a new random GUID as a string (e.g. "xxxxxxxx-xxxx-...").</summary>
    public static string New() => global::System.Guid.NewGuid().ToString();

    /// <summary>Returns a new random GUID as a string with no dashes.</summary>
    public static string NewN() => global::System.Guid.NewGuid().ToString("N");

    /// <summary>True when <paramref name="value"/> is a valid GUID string.</summary>
    public static bool IsValid(string value) => global::System.Guid.TryParse(value, out _);
}
