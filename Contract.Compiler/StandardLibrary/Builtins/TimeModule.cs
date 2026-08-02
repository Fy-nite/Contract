using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Time module — timestamps and formatting (mirrors ObjectIR's native Time class).
    /// </summary>
    [ClassBinding("Time")]
    public static class TimeModule
    {
        /// <summary>Current UTC time as Unix milliseconds (since 1970-01-01).</summary>
        [MethodBinding]
        public static long Now() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Formats a Unix-millisecond <paramref name="timestamp"/> using a .NET date/time <paramref name="format"/> string.</summary>
        [MethodBinding]
        public static string Format(long timestamp, string format)
            => System.DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToString(format);
    }
}
