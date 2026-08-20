using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.Generics;

/// <summary>
/// HashSet helpers over object-backed sets (unique elements). A generic
/// ObjektRT stdlib module. The runtime stores sets as opaque object handles.
/// </summary>
[ClassBinding("HashSet")]
public static class HashSet
{
    /// <summary>Creates a new empty set.</summary>
    public static object Create() => new global::System.Collections.Generic.HashSet<object>();

    /// <summary>Adds <paramref name="item"/> to the set; returns true when it was newly added.</summary>
    public static bool Add(object set, object item) => ((global::System.Collections.Generic.HashSet<object>)set).Add(item);

    /// <summary>True when the set contains <paramref name="item"/>.</summary>
    public static bool Contains(object set, object item) => ((global::System.Collections.Generic.HashSet<object>)set).Contains(item);

    /// <summary>Removes <paramref name="item"/> from the set; returns true when it was present.</summary>
    public static bool Remove(object set, object item) => ((global::System.Collections.Generic.HashSet<object>)set).Remove(item);

    /// <summary>Number of elements in the set.</summary>
    public static int Count(object set) => ((global::System.Collections.Generic.HashSet<object>)set).Count;

    /// <summary>Removes all elements from the set.</summary>
    public static void Clear(object set) => ((global::System.Collections.Generic.HashSet<object>)set).Clear();

    /// <summary>Returns an array of all elements in the set.</summary>
    public static object[] ToArray(object set) => ((global::System.Collections.Generic.HashSet<object>)set).ToArray();
}
