using System.Collections.Generic;
using Contract.Compiler.AST;

namespace Contract.Compiler.Semantics
{
    public class TypeRegistry
    {
        private readonly HashSet<string> _types = new()
        {
            "int", "string", "bool", "null", "void"
        };

        private readonly Dictionary<string, string> _aliases = new();

        public void RegisterCustomType(string name)
        {
            _types.Add(name);
        }

        public bool IsValidType(string type)
        {
            return _types.Contains(type);
        }

        public string ResolveType(string type)
        {
            return _aliases.TryGetValue(type, out var baseType) ? baseType : type;
        }
    }
}
