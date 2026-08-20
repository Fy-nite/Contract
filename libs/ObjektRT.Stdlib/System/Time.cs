using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>Timestamps and formatting. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Time")]
public static class Time
{
    /// <summary>Current UTC time as Unix milliseconds (since 1970-01-01).</summary>
    public static long Now() => global::System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Formats a Unix-millisecond <paramref name="timestamp"/> using a .NET date/time <paramref name="format"/> string.</summary>
    public static string Format(long timestamp, string format)
        => global::System.DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToString(format);
}
