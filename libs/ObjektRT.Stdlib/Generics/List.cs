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

    /// <summary>True when <paramref name="list"/> contains <paramref name="item"/>.</summary>
    public static bool Contains(object list, object item) => ((global::System.Collections.Generic.List<object>)list).Contains(item);

    /// <summary>Removes the first occurrence of <paramref name="item"/>; returns true when it was present.</summary>
    public static bool Remove(object list, object item) => ((global::System.Collections.Generic.List<object>)list).Remove(item);

    /// <summary>Returns the zero-based index of the first occurrence of <paramref name="item"/>, or -1 when absent.</summary>
    public static int IndexOf(object list, object item) => ((global::System.Collections.Generic.List<object>)list).IndexOf(item);

    /// <summary>Inserts <paramref name="item"/> at <paramref name="index"/>.</summary>
    public static void Insert(object list, int index, object item) => ((global::System.Collections.Generic.List<object>)list).Insert(index, item);

    /// <summary>Removes all elements from <paramref name="list"/>.</summary>
    public static void Clear(object list) => ((global::System.Collections.Generic.List<object>)list).Clear();

    /// <summary>Sorts the elements of <paramref name="list"/> in place.</summary>
    public static void Sort(object list) => ((global::System.Collections.Generic.List<object>)list).Sort();

    /// <summary>Reverses the order of the elements of <paramref name="list"/> in place.</summary>
    public static void Reverse(object list) => ((global::System.Collections.Generic.List<object>)list).Reverse();

    /// <summary>Returns a new array containing the elements of <paramref name="list"/>.</summary>
    public static object[] ToArray(object list) => ((global::System.Collections.Generic.List<object>)list).ToArray();
}
