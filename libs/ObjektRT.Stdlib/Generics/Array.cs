namespace ObjektRT.Stdlib.Generics;

/// <summary>Array helpers over object-backed arrays. A generic ObjektRT stdlib module.</summary>
public static class Array
{
    /// <summary>Number of elements in the array.</summary>
    public static int Length(object arr) => ((global::System.Array)arr).Length;

    /// <summary>Returns the element at the given index.</summary>
    public static object Get(object arr, int index) => ((global::System.Array)arr).GetValue(index)!;

    /// <summary>Sets the element at the given index.</summary>
    public static void Set(object arr, int index, object value) => ((global::System.Array)arr).SetValue(value, index);
}
