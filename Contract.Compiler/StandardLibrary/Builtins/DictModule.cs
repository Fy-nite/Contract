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
        [MethodBinding]
        public static object Create() => new Dictionary<object, object>();

        [MethodBinding]
        public static void Set(object dict, object key, object value) => ((Dictionary<object, object>)dict)[key] = value;

        [MethodBinding]
        public static object Get(object dict, object key)
            => ((Dictionary<object, object>)dict).TryGetValue(key, out var value) ? value : null;

        [MethodBinding]
        public static bool ContainsKey(object dict, object key) => ((Dictionary<object, object>)dict).ContainsKey(key);

        [MethodBinding]
        public static bool Remove(object dict, object key) => ((Dictionary<object, object>)dict).Remove(key);

        [MethodBinding]
        public static object Keys(object dict) => ((Dictionary<object, object>)dict).Keys.ToArray();

        [MethodBinding]
        public static int Count(object dict) => ((Dictionary<object, object>)dict).Count;
    }
}
