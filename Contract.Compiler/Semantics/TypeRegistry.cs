using System;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;

namespace Contract.Compiler.Semantics
{
    public class TypeRegistry
    {
        private readonly HashSet<string> _types = new(StringComparer.OrdinalIgnoreCase)
        {
            "int", "string", "bool", "double", "float", "object", "int64", "long", "null", "void",
            // Additional integer widths. All behave as int32 in the VM (I4); the
            // distinct names exist so DllImport signatures and interop struct
            // fields keep their native C widths (byte -> uint8, etc.).
            "byte", "sbyte", "short", "ushort", "uint",
            // Built-in base type for attribute declarations (C#-style custom attributes).
            "Attribute"
        };

        // Generic type names (type-erased: the runtime sees the unbound name).
        private readonly HashSet<string> _genericTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "List", "Dict",
            // Delegate<T> — a typed delegate wrapper; T is the function type.
            "Delegate"
        };

        private readonly Dictionary<string, string> _aliases = new();

        public void RegisterCustomType(string name)
        {
            _types.Add(name);
        }

        public void RegisterGenericType(string name)
        {
            _genericTypes.Add(name);
        }

        /// <summary>
        /// Validates a type descriptor. Arrays are valid when their element is valid;
        /// function types are valid when every parameter and the return type are valid;
        /// generic instances are valid when the unbound name is a known generic and
        /// every argument is valid.
        /// </summary>
        public bool IsValid(TypeDescriptor type)
        {
            switch (type)
            {
                case TypeDescriptor.Named n:
                    return _types.Contains(n.Name);
                case TypeDescriptor.ArrayOf a:
                    return IsValid(a.Element);
                case TypeDescriptor.Function f:
                    return f.Parameters.All(IsValid) && IsValid(f.Return);
                case TypeDescriptor.GenericInstance g:
                    return _genericTypes.Contains(g.Name) && g.Arguments.All(IsValid);
                default:
                    return false;
            }
        }

        public bool IsValidType(string type) => IsValid(TypeDescriptor.Parse(type));

        public string ResolveType(string type)
        {
            return _aliases.TryGetValue(type, out var baseType) ? baseType : type;
        }
    }
}
