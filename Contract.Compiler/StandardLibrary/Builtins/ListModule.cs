using System.Collections.Generic;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// List module — mirrors ObjectIR's native List class.
    /// The runtime stores lists as object references (void* in the C++ native API).
    /// </summary>
    [ClassBinding("List")]
    public static class ListModule
    {
        [MethodBinding]
        public static object Create() => new List<object>();

        [MethodBinding]
        public static void Add(object list, object item) => ((List<object>)list).Add(item);

        [MethodBinding]
        public static object Get(object list, int index) => ((List<object>)list)[index];

        [MethodBinding]
        public static void Set(object list, int index, object item) => ((List<object>)list)[index] = item;

        [MethodBinding]
        public static int Count(object list) => ((List<object>)list).Count;

        [MethodBinding]
        public static void RemoveAt(object list, int index) => ((List<object>)list).RemoveAt(index);
    }
}
