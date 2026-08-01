using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Time module — timestamps and formatting (mirrors ObjectIR's native Time class).
    /// </summary>
    [ClassBinding("Time")]
    public static class TimeModule
    {
        [MethodBinding]
        public static long Now() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        [MethodBinding]
        public static string Format(long timestamp, string format)
            => System.DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToString(format);
    }
}
