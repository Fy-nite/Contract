using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// A C# calendar/clock binding exposed as <c>[ClassBinding("DateTime")]</c>.
/// Every method takes or returns an opaque <c>object</c> handle that wraps a
/// boxed <see cref="global::System.DateTime"/> — the Contract side never sees
/// the CLR struct layout, it just holds the handle (see
/// <c>ObjektRT.Core.DateTime</c> in the ContractStdlib repo, which shadows this
/// binding and delegates through <c>host</c>). Used by the stdlib wrapper, not
/// meant to be called directly.
/// </summary>
[ClassBinding("DateTime")]
public static class DateTime
{
    private static global::System.DateTime Unwrap(object handle)
        => handle is global::System.DateTime dt ? dt : default;

    /// <summary>Handle for the current UTC date/time.</summary>
    public static object Now() => global::System.DateTime.UtcNow;

    /// <summary>Handle for <c>year-month-day</c> (local calendar values).</summary>
    public static object Create(int year, int month, int day) => new global::System.DateTime(year, month, day);

    /// <summary>Handle for a full date/time (local calendar values).</summary>
    public static object CreateFull(int year, int month, int day, int hour, int minute, int second)
        => new global::System.DateTime(year, month, day, hour, minute, second);

    /// <summary>Year component of the wrapped value.</summary>
    public static int GetYear(object handle) => Unwrap(handle).Year;

    /// <summary>Month component of the wrapped value.</summary>
    public static int GetMonth(object handle) => Unwrap(handle).Month;

    /// <summary>Day-of-month component of the wrapped value.</summary>
    public static int GetDay(object handle) => Unwrap(handle).Day;

    /// <summary>Hour component of the wrapped value.</summary>
    public static int GetHour(object handle) => Unwrap(handle).Hour;

    /// <summary>Minute component of the wrapped value.</summary>
    public static int GetMinute(object handle) => Unwrap(handle).Minute;

    /// <summary>Second component of the wrapped value.</summary>
    public static int GetSecond(object handle) => Unwrap(handle).Second;

    /// <summary>Day-of-week component (0 = Sunday, matching <see cref="DayOfWeek"/>).</summary>
    public static int GetDayOfWeek(object handle) => (int)Unwrap(handle).DayOfWeek;

    /// <summary>Ticks of the wrapped value.</summary>
    public static long GetTicks(object handle) => Unwrap(handle).Ticks;

    /// <summary>Short date string (e.g. <c>15/01/2024</c>).</summary>
    public static string ToShortDate(object handle) => Unwrap(handle).ToShortDateString();

    /// <summary>ISO-8601 round-trip string.</summary>
    public static string ToIso(object handle) => Unwrap(handle).ToString("o");

    /// <summary>Formats the wrapped value with a .NET date/time <paramref name="format"/> string.</summary>
    public static string ToStringCustom(object handle, string format) => Unwrap(handle).ToString(format);

    /// <summary>New handle for the wrapped value plus <paramref name="days"/>.</summary>
    public static object AddDays(object handle, double days) => Unwrap(handle).AddDays(days);

    /// <summary>New handle for the wrapped value plus <paramref name="months"/>.</summary>
    public static object AddMonths(object handle, int months) => Unwrap(handle).AddMonths(months);

    /// <summary>Diagnostic string for the wrapped value.</summary>
    public static string DebugString(object handle) => Unwrap(handle).ToString("yyyy-MM-dd HH:mm:ss");
}