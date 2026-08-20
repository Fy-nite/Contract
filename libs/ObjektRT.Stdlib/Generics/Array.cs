using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.Generics;

/// <summary>Array helpers over object-backed arrays. A generic ObjektRT stdlib module.</summary>
[ClassBinding("Array")]
public static class Array
{
    /// <summary>Number of elements in the array.</summary>
    public static int Length(object arr) => ((global::System.Array)arr).Length;

    /// <summary>Returns the element at the given index.</summary>
    public static object Get(object arr, int index) => ((global::System.Array)arr).GetValue(index)!;

    /// <summary>Sets the element at the given index.</summary>
    public static void Set(object arr, int index, object value) => ((global::System.Array)arr).SetValue(value, index);

    /// <summary>Joins the elements of a string array into one string with <paramref name="separator"/> between them.</summary>
    public static string Join(object arr, string separator)
    {
        var values = ((global::System.Array)arr).Cast<object?>().Select(v => v?.ToString() ?? "").ToArray();
        return string.Join(separator, values);
    }

    /// <summary>True when the array contains <paramref name="value"/>.</summary>
    public static bool Contains(object arr, object value)
        => ((global::System.Array)arr).Cast<object?>().Contains(value);

    /// <summary>Returns the zero-based index of the first occurrence of <paramref name="value"/>, or -1 when absent.</summary>
    public static int IndexOf(object arr, object value)
        => global::System.Array.IndexOf((global::System.Array)arr, value);

    /// <summary>Reverses the order of the elements of the array in place.</summary>
    public static void Reverse(object arr) => global::System.Array.Reverse((global::System.Array)arr);

    /// <summary>Sorts the elements of the array in place.</summary>
    public static void Sort(object arr) => global::System.Array.Sort((global::System.Array)arr);

    /// <summary>Copies <paramref name="count"/> elements from <paramref name="src"/> starting at <paramref name="srcIndex"/> into <paramref name="dst"/> at <paramref name="dstIndex"/>.</summary>
    public static void Copy(object src, int srcIndex, object dst, int dstIndex, int count)
        => global::System.Array.Copy((global::System.Array)src, srcIndex, (global::System.Array)dst, dstIndex, count);

    /// <summary>Fills the array with <paramref name="value"/>.</summary>
    public static void Fill(object arr, object value)
    {
        var a = (global::System.Array)arr;
        for (int i = 0; i < a.Length; i++) a.SetValue(value, i);
    }

    /// <summary>Sum of the integer elements of the array.</summary>
    public static int Sum(object arr) => ((global::System.Array)arr).Cast<int>().Sum();

    /// <summary>Smallest integer element of the array.</summary>
    public static int Min(object arr) => ((global::System.Array)arr).Cast<int>().Min();

    /// <summary>Largest integer element of the array.</summary>
    public static int Max(object arr) => ((global::System.Array)arr).Cast<int>().Max();

    /// <summary>Average of the integer elements of the array (truncated).</summary>
    public static int Average(object arr) => (int)((global::System.Array)arr).Cast<int>().Average();

    /// <summary>Appends <paramref name="value"/> to the end of the array by resizing it. The caller must reassign to the returned array (arrays are fixed-size).</summary>
    public static object[] Push(object arr, object value)
    {
        var src = (global::System.Array)arr;
        var dst = global::System.Array.CreateInstance(src.GetType().GetElementType()!, src.Length + 1);
        global::System.Array.Copy(src, dst, src.Length);
        dst.SetValue(value, src.Length);
        return (object[])dst;
    }
}