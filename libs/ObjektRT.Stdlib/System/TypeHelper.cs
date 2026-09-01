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
}