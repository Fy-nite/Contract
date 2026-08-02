using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Array module — static array helpers (mirrors ObjectIR's native Array class).
    /// Language arrays (int[], string[], ...) are passed as objects to these helpers.
    /// </summary>
    [ClassBinding("Array")]
    public static class ArrayModule
    {
        /// <summary>Number of elements in the array.</summary>
        [MethodBinding]
        public static int Length(object arr) => ((System.Array)arr).Length;

        /// <summary>Returns the element at <paramref name="index"/>.</summary>
        [MethodBinding]
        public static object Get(object arr, int index) => ((System.Array)arr).GetValue(index);

        /// <summary>Sets the element at <paramref name="index"/> to <paramref name="value"/>.</summary>
        [MethodBinding]
        public static void Set(object arr, int index, object value) => ((System.Array)arr).SetValue(value, index);
    }
}
