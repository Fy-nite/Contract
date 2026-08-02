namespace ObjektRT.Stdlib.Threading;

/// <summary>Threading helpers. A generic ObjektRT stdlib module.</summary>
public static class Thread
{
    /// <summary>Suspends the current thread for the given number of milliseconds.</summary>
    public static void Sleep(int ms) => global::System.Threading.Thread.Sleep(ms);
}
