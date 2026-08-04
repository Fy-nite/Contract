namespace ObjektRT.Stdlib.Generics;

/// <summary>
/// List helpers over object-backed lists. A generic ObjektRT stdlib module.
/// The runtime stores lists as opaque object handles.
/// </summary>
public static class List
{
    /// <summary>Creates a new empty list.</summary>
    public static object Create() => new global::System.Collections.Generic.List<object>();

    /// <summary>Appends <paramref name="item"/> to the end of <paramref name="list"/>.</summary>
    public static void Add(object list, object item) => ((global::System.Collections.Generic.List<object>)list).Add(item);

    /// <summary>Returns the element at <paramref name="index"/>.</summary>
    public static object Get(object list, int index) => ((global::System.Collections.Generic.List<object>)list)[index];

    /// <summary>Replaces the element at <paramref name="index"/> with <paramref name="item"/>.</summary>
    public static void Set(object list, int index, object item) => ((global::System.Collections.Generic.List<object>)list)[index] = item;

    /// <summary>Number of elements in <paramref name="list"/>.</summary>
    public static int Count(object list) => ((global::System.Collections.Generic.List<object>)list).Count;

    /// <summary>Removes the element at <paramref name="index"/>.</summary>
    public static void RemoveAt(object list, int index) => ((global::System.Collections.Generic.List<object>)list).RemoveAt(index);
}
