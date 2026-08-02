using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins
{
    /// <summary>
    /// Dict module — mirrors ObjectIR's native Dict class (string-keyed object map).
    /// </summary>
    [ClassBinding("Dict")]
    public static class DictModule
    {
        /// <summary>Creates a new empty dictionary.</summary>
        [MethodBinding]
        public static object Create() => new Dictionary<object, object>();

        /// <summary>Associates <paramref name="key"/> with <paramref name="value"/>.</summary>
        [MethodBinding]
        public static void Set(object dict, object key, object value) => ((Dictionary<object, object>)dict)[key] = value;

        /// <summary>Returns the value for <paramref name="key"/>, or null when absent.</summary>
        [MethodBinding]
        public static object Get(object dict, object key)
            => ((Dictionary<object, object>)dict).TryGetValue(key, out var value) ? value : null;

        /// <summary>True when <paramref name="dict"/> contains <paramref name="key"/>.</summary>
        [MethodBinding]
        public static bool ContainsKey(object dict, object key) => ((Dictionary<object, object>)dict).ContainsKey(key);

        /// <summary>Removes <paramref name="key"/>; returns true when it was present.</summary>
        [MethodBinding]
        public static bool Remove(object dict, object key) => ((Dictionary<object, object>)dict).Remove(key);

        /// <summary>Returns an array of all keys in <paramref name="dict"/>.</summary>
        [MethodBinding]
        public static object Keys(object dict) => ((Dictionary<object, object>)dict).Keys.ToArray();

        /// <summary>Number of entries in <paramref name="dict"/>.</summary>
        [MethodBinding]
        public static int Count(object dict) => ((Dictionary<object, object>)dict).Count;
    }
}
