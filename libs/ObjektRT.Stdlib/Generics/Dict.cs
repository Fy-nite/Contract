namespace ObjektRT.Stdlib.Generics;

/// <summary>
/// Dict helpers over object-backed dictionaries. A generic ObjektRT stdlib
/// module. The runtime stores dicts as opaque object handles.
/// </summary>
public static class Dict
{
    /// <summary>Creates a new empty dictionary.</summary>
    public static object Create() => new global::System.Collections.Generic.Dictionary<object, object>();

    /// <summary>Associates <paramref name="key"/> with <paramref name="value"/>.</summary>
    public static void Set(object dict, object key, object value)
        => ((global::System.Collections.Generic.Dictionary<object, object>)dict)[key] = value;

    /// <summary>Returns the value for <paramref name="key"/>, or null when absent.</summary>
    public static object Get(object dict, object key)
        => ((global::System.Collections.Generic.Dictionary<object, object>)dict).TryGetValue(key, out var value) ? value : null;

    /// <summary>True when <paramref name="dict"/> contains <paramref name="key"/>.</summary>
    public static bool ContainsKey(object dict, object key)
        => ((global::System.Collections.Generic.Dictionary<object, object>)dict).ContainsKey(key);

    /// <summary>Removes <paramref name="key"/>; returns true when it was present.</summary>
    public static bool Remove(object dict, object key)
        => ((global::System.Collections.Generic.Dictionary<object, object>)dict).Remove(key);

    /// <summary>Returns an array of all keys in <paramref name="dict"/>.</summary>
    public static object Keys(object dict)
        => ((global::System.Collections.Generic.Dictionary<object, object>)dict).Keys.ToArray();

    /// <summary>Number of entries in <paramref name="dict"/>.</summary>
    public static int Count(object dict) => ((global::System.Collections.Generic.Dictionary<object, object>)dict).Count;
}
