using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.Generics;

/// <summary>
/// Queue helpers over object-backed queues (FIFO). A generic ObjektRT stdlib
/// module. The runtime stores queues as opaque object handles.
/// </summary>
[ClassBinding("Queue")]
public static class Queue
{
    /// <summary>Creates a new empty queue.</summary>
    public static object Create() => new global::System.Collections.Generic.Queue<object>();

    /// <summary>Adds <paramref name="item"/> to the back of the queue.</summary>
    public static void Enqueue(object queue, object item) => ((global::System.Collections.Generic.Queue<object>)queue).Enqueue(item);

    /// <summary>Removes and returns the front item of the queue.</summary>
    public static object Dequeue(object queue) => ((global::System.Collections.Generic.Queue<object>)queue).Dequeue();

    /// <summary>Returns the front item of the queue without removing it.</summary>
    public static object Peek(object queue) => ((global::System.Collections.Generic.Queue<object>)queue).Peek();

    /// <summary>Number of items in the queue.</summary>
    public static int Count(object queue) => ((global::System.Collections.Generic.Queue<object>)queue).Count;

    /// <summary>True when the queue contains <paramref name="item"/>.</summary>
    public static bool Contains(object queue, object item) => ((global::System.Collections.Generic.Queue<object>)queue).Contains(item);

    /// <summary>Removes all items from the queue.</summary>
    public static void Clear(object queue) => ((global::System.Collections.Generic.Queue<object>)queue).Clear();
}
