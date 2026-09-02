using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// Small shared string/type-name helpers for the stdlib reflection wrappers.
/// <c>GetName</c> / <c>GetNamespace</c> split a dotted wire name the same way
/// <c>ObjektRT.Core.Type</c> expects (everything after / before the last '.').
/// </summary>
[ClassBinding("TypeHelper")]
public static class TypeHelper
{
    private static int LastDot(string fullName) => fullName.LastIndexOf('.');

    /// <summary>The simple name: everything after the last '.', or the whole string when none.</summary>
    public static string GetName(string fullName)
    {
        int i = LastDot(fullName);
        return i < 0 ? fullName : fullName[(i + 1)..];
    }

    /// <summary>The namespace: everything before the last '.', or "" when none.</summary>
    public static string GetNamespace(string fullName)
    {
        int i = LastDot(fullName);
        return i <= 0 ? "" : fullName[..i];
    }

    /// <summary>
    /// The runtime engine behind the language's `expr as T` / `expr is T`
    /// casts for primitive (and string/object) target types, which have no
    /// VM type record for <c>isinst</c>. Returns <paramref name="value"/>
    /// unchanged when its CLR type matches the primitive named by
    /// <paramref name="typeName"/> (the compiler emits a literal type name),
    /// or <c>null</c> when it does not. Materialized generic contracts pass a
    /// concrete type name (e.g. <c>"int32"</c>) in place of their type
    /// parameter, so this also implements `as T` / `is T` on primitives.
    /// </summary>
    public static object? CastOrNull(object? value, string typeName)
    {
        if (value == null) return null;
        switch (typeName.Trim().ToLowerInvariant())
        {
            // The VM materializes every boxed numeric as System.Double on the
            // object boundary, so `as int` / `as long` / etc. must COERCE the
            // value to the target type (the boxed CLR type never matches the
            // primitive target type). Only a genuine non-numeric value fails.
            case "int": case "int32": case "uint": case "uint32":
            case "byte": case "sbyte": case "short": case "ushort":
                return IsNumeric(value) ? global::System.Convert.ToInt32(value) : null;
            case "long": case "int64": case "ulong": case "uint64":
                return IsNumeric(value) ? global::System.Convert.ToInt64(value) : null;
            case "float": case "float32":
                return IsNumeric(value) ? global::System.Convert.ToSingle(value) : null;
            case "double": case "float64":
                return IsNumeric(value) ? global::System.Convert.ToDouble(value) : null;
            case "bool":
                return value is bool ? value : null;
            case "string":
                return value is string ? value : null;
            default:
                return value;   // reference/named types are handled via isinst, not here
        }
    }

    private static bool IsNumeric(object v) =>
        v is int or long or float or double or short or byte or sbyte
        or uint or ulong or ushort or decimal;
}