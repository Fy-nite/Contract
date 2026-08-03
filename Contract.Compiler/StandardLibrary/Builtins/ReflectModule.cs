using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary.Builtins;

/// <summary>
/// In-language reflection: <c>Reflect.Types()</c>, <c>Reflect.Methods("Foo")</c>,
/// <c>Reflect.GetStatic(...)</c>, <c>Reflect.Call(...)</c>, ... — runtime
/// introspection over the loaded module. The host (a ContractRuntime) sets
/// <see cref="Host"/>; without one every call returns empty/false/null.
/// </summary>
[ClassBinding("Reflect")]
public static class ReflectModule
{
    /// <summary>The host providing module metadata + static access, set by the runtime.</summary>
    public static IReflectHost? Host { get; set; }

    /// <summary>Every type in the loaded module (qualified wire names).</summary>
    [MethodBinding]
    public static string[] Types() => Host?.Types() ?? Array.Empty<string>();

    /// <summary>True when a type with this (short or qualified) name exists.</summary>
    [MethodBinding]
    public static bool HasType(string typeName) => Host?.HasType(typeName) ?? false;

    /// <summary>Qualified names ("Type.Method") of a type's methods, including inherited.</summary>
    [MethodBinding]
    public static string[] Methods(string typeName) => Host?.Methods(typeName) ?? Array.Empty<string>();

    /// <summary>Qualified names ("Type.field") of a type's fields, including inherited.</summary>
    [MethodBinding]
    public static string[] Fields(string typeName) => Host?.Fields(typeName) ?? Array.Empty<string>();

    /// <summary>The direct base type's wire name, or "" when none.</summary>
    [MethodBinding]
    public static string Base(string typeName) => Host?.BaseType(typeName) ?? "";

    /// <summary>Reads a static field by type name + field name.</summary>
    [MethodBinding]
    public static object? GetStatic(string typeName, string fieldName) => Host?.GetStatic(typeName, fieldName);

    /// <summary>Writes a static field by type name + field name.</summary>
    [MethodBinding]
    public static void SetStatic(string typeName, string fieldName, object? value) => Host?.SetStatic(typeName, fieldName, value);

    /// <summary>Invokes a static method by type name + method name with args.</summary>
    [MethodBinding]
    public static object? Call(string typeName, string methodName, object?[] args) => Host?.Call(typeName, methodName, args);
}
