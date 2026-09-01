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
            "Attribute",
            // Root reflection type (C#-style System.Type): a Type value can
            // represent/introspect ANY type in the loaded module.
            "Type"
        };

        // Generic type names → arity (the runtime sees the unbound name for
        // stdlib generics; user generic contracts register their own arity).
        private readonly Dictionary<string, int> _genericTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["List"] = 1,
            ["Dict"] = 2,
            // Delegate<T> — a typed delegate wrapper; T is the function type.
            ["Delegate"] = 1
        };

        private readonly Dictionary<string, string> _aliases = new();

        public void RegisterCustomType(string name)
        {
            _types.Add(name);
        }

        public void RegisterGenericType(string name, int arity)
        {
            _genericTypes[name] = arity;
        }

        /// <summary>True when <paramref name="name"/> is a known generic type (List, Dict, Delegate, or a user generic contract).</summary>
        public bool IsGenericType(string name) => _genericTypes.ContainsKey(name);

        /// <summary>The declared arity of a generic type, or 0 when it isn't one.</summary>
        public int GetArity(string name)
            => _genericTypes.TryGetValue(name, out var arity) ? arity : 0;

        /// <summary>
        /// True when <paramref name="name"/> is a valid type name: a built-in,
        /// a registered custom type, or a known generic (the unbound name is
        /// valid on its own — <c>Box</c> is a legal type reference even though
        /// instantiations carry the args).
        /// </summary>
        public bool IsValidTypeName(string name)
            => _types.Contains(name) || _genericTypes.ContainsKey(name);

        /// <summary>
        /// Returns the registered (canonical) spelling of a type name, matching
        /// case-insensitively. The registry accepts any case, but the emitted
        /// wire name must match the declared type exactly, so callers
        /// canonicalize (<c>Raylib.color</c> → <c>Raylib.Color</c>) before codegen.
        /// </summary>
        public string? CanonicalName(string name)
        {
            foreach (var t in _types)
                if (string.Equals(t, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        /// <summary>
        /// Validates a type descriptor. Arrays are valid when their element is valid;
        /// function types are valid when every parameter and the return type are valid;
        /// generic instances are valid when the unbound name is a known generic, the
        /// argument count matches the declared arity, and every argument is valid.
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
                    return _genericTypes.TryGetValue(g.Name, out var arity)
                        && g.Arguments.Count == arity
                        && g.Arguments.All(IsValid);
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
