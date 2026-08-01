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
        [MethodBinding]
        public static int Length(object arr) => ((System.Array)arr).Length;

        [MethodBinding]
        public static object Get(object arr, int index) => ((System.Array)arr).GetValue(index);

        [MethodBinding]
        public static void Set(object arr, int index, object value) => ((System.Array)arr).SetValue(value, index);
    }
}
