using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.Threading;

/// <summary>
/// Threading helpers. A generic ObjektRT stdlib module.
///
/// Two styles are provided:
///  - <see cref="Spawn"/> — fire-and-forget: run a delegate on a background
///    thread and don't keep a reference to it.
///  - <see cref="Create"/> + <see cref="Start"/> + <see cref="Join"/> +
///    <see cref="IsAlive"/> — C#-style lifecycle: a thread is a value you
///    can store in a variable, start explicitly, wait on, and poll.
///
/// The methods that touch the VM (Create/Start/Join/IsAlive/Spawn) are
/// host-provided: the runtime registers native bindings under these names
/// that take precedence at dispatch time. These stubs exist so language
/// compilers can bind the calls at compile time.
/// </summary>
[ClassBinding("Thread")]
public static class Thread
{
    /// <summary>Suspends the current thread for the given number of milliseconds.</summary>
    public static void Sleep(int ms) => global::System.Threading.Thread.Sleep(ms);

    /// <summary>
    /// Runs the delegate on a new background thread. The runtime host
    /// registers a real native binding for "Thread.Spawn" that runs the
    /// delegate on a fresh interpreter sharing the module state; that native
    /// binding takes precedence at dispatch time. This stub exists so language
    /// compilers can bind the call at compile time.
    /// </summary>
    public static void Spawn(object d)
    {
        // Overridden at dispatch time by the runtime host's native binding.
    }

    /// <summary>
    /// Creates a thread for <paramref name="work"/> (a delegate) and returns
    /// a handle you can store in a variable. Nothing runs until
    /// <see cref="Start"/> is called. Host-provided.
    /// </summary>
    public static object Create(object work) => throw new global::System.NotSupportedException(
        "Thread.Create is provided by the ObjectRT runtime host.");

    /// <summary>Starts a thread created with <see cref="Create"/>. Host-provided.</summary>
    public static void Start(object thread) => throw new global::System.NotSupportedException(
        "Thread.Start is provided by the ObjectRT runtime host.");

    /// <summary>Blocks the calling thread until the given thread finishes. Host-provided.</summary>
    public static void Join(object thread) => throw new global::System.NotSupportedException(
        "Thread.Join is provided by the ObjectRT runtime host.");

    /// <summary>True while the given thread is running. Host-provided.</summary>
    public static bool IsAlive(object thread) => throw new global::System.NotSupportedException(
        "Thread.IsAlive is provided by the ObjectRT runtime host.");
}
