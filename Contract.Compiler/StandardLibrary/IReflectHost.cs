namespace Contract.Compiler.StandardLibrary;

/// <summary>
/// Host implementation for the in-language <c>Reflect</c> module. The runtime
/// host (a <c>ContractRuntime</c>) sets <see cref="ReflectModule.Host"/> so
/// Contract code can introspect the loaded module at runtime.
/// </summary>
public interface IReflectHost
{
    /// <summary>Every type in the loaded module, as qualified wire names.</summary>
    string[] Types();

    /// <summary>True when a type with this (short or qualified) name exists.</summary>
    bool HasType(string typeName);

    /// <summary>Qualified names ("Type.Method") of a type's methods, including inherited.</summary>
    string[] Methods(string typeName);

    /// <summary>Qualified names ("Type.field") of a type's fields, including inherited.</summary>
    string[] Fields(string typeName);

    /// <summary>The direct base type's wire name, or "" when none.</summary>
    string BaseType(string typeName);

    /// <summary>Reads a static field by qualified name ("Type.field").</summary>
    object? GetStatic(string typeName, string fieldName);

    /// <summary>Writes a static field by qualified name ("Type.field").</summary>
    void SetStatic(string typeName, string fieldName, object? value);

    /// <summary>Invokes a static method by qualified name ("Type.Method").</summary>
    object? Call(string typeName, string methodName, object?[] args);
}
