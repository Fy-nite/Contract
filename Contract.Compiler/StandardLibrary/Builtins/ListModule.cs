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
        /// <summary>Creates a new empty list.</summary>
        [MethodBinding]
        public static object Create() => new List<object>();

        /// <summary>Appends <paramref name="item"/> to the end of <paramref name="list"/>.</summary>
        [MethodBinding]
        public static void Add(object list, object item) => ((List<object>)list).Add(item);

        /// <summary>Returns the element at <paramref name="index"/>.</summary>
        [MethodBinding]
        public static object Get(object list, int index) => ((List<object>)list)[index];

        /// <summary>Replaces the element at <paramref name="index"/> with <paramref name="item"/>.</summary>
        [MethodBinding]
        public static void Set(object list, int index, object item) => ((List<object>)list)[index] = item;

        /// <summary>Number of elements in <paramref name="list"/>.</summary>
        [MethodBinding]
        public static int Count(object list) => ((List<object>)list).Count;

        /// <summary>Removes the element at <paramref name="index"/>.</summary>
        [MethodBinding]
        public static void RemoveAt(object list, int index) => ((List<object>)list).RemoveAt(index);
    }
}
