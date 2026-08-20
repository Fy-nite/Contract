using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.Generics;

/// <summary>
/// Stack helpers over object-backed stacks (LIFO). A generic ObjektRT stdlib
/// module. The runtime stores stacks as opaque object handles.
/// </summary>
[ClassBinding("Stack")]
public static class Stack
{
    /// <summary>Creates a new empty stack.</summary>
    public static object Create() => new global::System.Collections.Generic.Stack<object>();

    /// <summary>Pushes <paramref name="item"/> onto the top of the stack.</summary>
    public static void Push(object stack, object item) => ((global::System.Collections.Generic.Stack<object>)stack).Push(item);

    /// <summary>Removes and returns the top item of the stack.</summary>
    public static object Pop(object stack) => ((global::System.Collections.Generic.Stack<object>)stack).Pop();

    /// <summary>Returns the top item of the stack without removing it.</summary>
    public static object Peek(object stack) => ((global::System.Collections.Generic.Stack<object>)stack).Peek();

    /// <summary>Number of items in the stack.</summary>
    public static int Count(object stack) => ((global::System.Collections.Generic.Stack<object>)stack).Count;

    /// <summary>True when the stack contains <paramref name="item"/>.</summary>
    public static bool Contains(object stack, object item) => ((global::System.Collections.Generic.Stack<object>)stack).Contains(item);

    /// <summary>Removes all items from the stack.</summary>
    public static void Clear(object stack) => ((global::System.Collections.Generic.Stack<object>)stack).Clear();
}
