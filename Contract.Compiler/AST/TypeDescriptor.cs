using System;
using System.Collections.Generic;
using System.Linq;

namespace Contract.Compiler.AST;

/// <summary>
/// Describes a type in the Contract type system. This is the Phase 0 replacement
/// for raw type strings: it can represent simple named types ("int", "Person"),
/// array types ("int[]"), and function types ("(int, bool) -> string").
///
/// The three shapes are structurally comparable (records / custom equality), so
/// inference can compare types by value.
/// </summary>
public abstract class TypeDescriptor
{
    /// <summary>The untyped marker (empty name), used before inference runs.</summary>
    public static readonly TypeDescriptor Empty = new Named("");

    /// <summary>True when this descriptor is the untyped/empty placeholder.</summary>
    public bool IsEmpty => this is Named { Name.Length: 0 };

    /// <summary>True when this descriptor is the 'string' type (case-insensitive).</summary>
    public bool IsString => this is Named n && string.Equals(n.Name, "string", StringComparison.OrdinalIgnoreCase);

    /// <summary>A simple named type, e.g. "int", "Person". Arrays are NOT included here.</summary>
    public sealed class Named : TypeDescriptor, IEquatable<Named>
    {
        public string Name { get; }

        public Named(string name)
        {
            Name = name;
        }

        public override string ToString() => Name;

        public bool Equals(Named? other)
            => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as Named);

        public override int GetHashCode() => Name.GetHashCode();
    }

    /// <summary>An array of some element type, e.g. "int[]".</summary>
    public sealed class ArrayOf : TypeDescriptor, IEquatable<ArrayOf>
    {
        public TypeDescriptor Element { get; }

        public ArrayOf(TypeDescriptor element)
        {
            Element = element;
        }

        public override string ToString() => $"{Element}[]";

        public bool Equals(ArrayOf? other) => other is not null && Element.Equals(other.Element);

        public override bool Equals(object? obj) => Equals(obj as ArrayOf);

        public override int GetHashCode() => Element.GetHashCode();
    }

    /// <summary>A function type, e.g. "(int, bool) -> string".</summary>
    public sealed class Function : TypeDescriptor, IEquatable<Function>
    {
        public IReadOnlyList<TypeDescriptor> Parameters { get; }
        public TypeDescriptor Return { get; }

        public Function(IReadOnlyList<TypeDescriptor> parameters, TypeDescriptor returnType)
        {
            Parameters = parameters;
            Return = returnType;
        }

        public override string ToString() => $"({string.Join(", ", Parameters)}) -> {Return}";

        public bool Equals(Function? other)
        {
            if (other is null) return false;
            if (!Return.Equals(other.Return)) return false;
            if (Parameters.Count != other.Parameters.Count) return false;
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (!Parameters[i].Equals(other.Parameters[i])) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as Function);

        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(Return);
            foreach (var p in Parameters) h.Add(p);
            return h.ToHashCode();
        }
    }

    /// <summary>
    /// Parses a type string into a descriptor. Supports simple names ("int", "Person"),
    /// array suffixes ("int[]", "int[][]"), and function types ("(int, bool) -> string").
    /// </summary>
    public static TypeDescriptor Parse(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return Empty;

        // Function type: "(T, U) -> R"
        if (s[0] == '(')
        {
            int close = s.IndexOf(')');
            int arrow = s.IndexOf("->", StringComparison.Ordinal);
            if (close > 0 && arrow > close)
            {
                var paramPart = s.Substring(1, close - 1);
                var returnPart = s.Substring(arrow + 2).Trim();
                var parameters = paramPart.Split(',')
                    .Select(p => Parse(p))
                    .ToList();
                return new Function(parameters, Parse(returnPart));
            }
        }

        // Peel array suffixes: "int[]" -> ArrayOf(Named("int")), "int[][]" -> ArrayOf(ArrayOf(...))
        if (s.EndsWith("[]", StringComparison.Ordinal))
        {
            return new ArrayOf(Parse(s[..^2]));
        }

        return new Named(s);
    }
}
