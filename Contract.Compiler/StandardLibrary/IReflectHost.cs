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

    /// <summary>The loaded module's name, or "" when nothing is loaded.</summary>
    string ModuleName();

    /// <summary>The type's kind: "Class" / "Interface" / "Struct" / "Enum", or "" when unknown.</summary>
    string Kind(string typeName);

    /// <summary>True when the type is a class (TypeKind.Class).</summary>
    bool IsClass(string typeName);

    /// <summary>True when the type is an interface (TypeKind.Interface).</summary>
    bool IsInterface(string typeName);

    /// <summary>True when the type is a struct (TypeKind.Struct).</summary>
    bool IsStruct(string typeName);

    /// <summary>True when the type is an enum (TypeKind.Enum).</summary>
    bool IsEnum(string typeName);

    /// <summary>True when the type is abstract (IR <c>TypeFlags.Abstract</c>).</summary>
    bool IsAbstract(string typeName);

    /// <summary>True when the type is sealed (IR <c>TypeFlags.Sealed</c>).</summary>
    bool IsSealed(string typeName);

    /// <summary>The type's declared access: "Public" / "Private" / "Protected" / "Internal".</summary>
    string Access(string typeName);

    /// <summary>Direct interfaces implemented by the type, by name (external ones as "").</summary>
    string[] Interfaces(string typeName);

    /// <summary>All interfaces implemented by the type, including inherited ones.</summary>
    string[] AllInterfaces(string typeName);

    /// <summary>This type and all its bases, most-derived first.</summary>
    string[] Hierarchy(string typeName);

    /// <summary>True when <paramref name="typeName"/> transitively inherits from <paramref name="baseTypeName"/>.</summary>
    bool IsSubclassOf(string typeName, string baseTypeName);

    /// <summary>True when <paramref name="otherTypeName"/> is <paramref name="typeName"/>, a subclass of it, or (for interfaces) an implementor of it.</summary>
    bool IsAssignableFrom(string typeName, string otherTypeName);

    /// <summary>Resolves a qualified method name ("Type.Method") through inheritance — the most-derived declaration wins — returning its canonical "DeclaringType.Method" form, or "" when unresolvable.</summary>
    string Resolve(string qualifiedMethodName);

    /// <summary>Methods declared on the type itself (not inherited), as "Type.Method".</summary>
    string[] DeclaredMethods(string typeName);

    /// <summary>Fields declared on the type itself (not inherited), as "Type.field".</summary>
    string[] DeclaredFields(string typeName);

    /// <summary>Attributes applied to the type, as "Name(arg, ...)" strings.</summary>
    string[] Attributes(string typeName);

    /// <summary>Attributes applied to a method, as "Name(arg, ...)" strings.</summary>
    string[] MethodAttributes(string typeName, string methodName);

    /// <summary>The method's declared return type name ("int32", "string", "void", ...), or "" when unknown.</summary>
    string MethodReturn(string typeName, string methodName);

    /// <summary>The method's parameters as "type name" strings ("int32 x"), or empty when unknown. Instance methods include "this" as parameter 0.</summary>
    string[] MethodParams(string typeName, string methodName);

    /// <summary>True when the method is static.</summary>
    bool MethodStatic(string typeName, string methodName);

    /// <summary>True when the method is virtual (IR <c>MethodFlags.Virtual</c>).</summary>
    bool MethodVirtual(string typeName, string methodName);

    /// <summary>True when the method is an override (IR <c>MethodFlags.Override</c>).</summary>
    bool MethodOverride(string typeName, string methodName);

    /// <summary>True when the method is abstract (IR <c>MethodFlags.Abstract</c>).</summary>
    bool MethodAbstract(string typeName, string methodName);

    /// <summary>The type that declares the method (the base type for inherited lookups), or "" when unknown.</summary>
    string MethodDeclaringType(string typeName, string methodName);

    /// <summary>The base definition of the method — the "Type.Method" root of an override chain — or "" when unknown.</summary>
    string MethodBase(string typeName, string methodName);

    /// <summary>The field's declared type name ("int32", ...), or "" when unknown.</summary>
    string FieldType(string typeName, string fieldName);

    /// <summary>True when the field is static.</summary>
    bool FieldStatic(string typeName, string fieldName);

    /// <summary>The type that declares the field, or "" when unknown.</summary>
    string FieldDeclaringType(string typeName, string fieldName);

    /// <summary>
    /// Invokes a method by name through a runtime. For static methods the
    /// receiver is ignored; for instance methods it must be the object handle
    /// returned by a previous call (VM-internal objects round-trip as handles).
    /// </summary>
    object? Invoke(string typeName, string methodName, object? receiver, object?[] args);

    /// <summary>Reads a static field by qualified name ("Type.field").</summary>
    object? GetStatic(string typeName, string fieldName);

    /// <summary>Writes a static field by qualified name ("Type.field").</summary>
    void SetStatic(string typeName, string fieldName, object? value);

    /// <summary>Invokes a static method by qualified name ("Type.Method").</summary>
    object? Call(string typeName, string methodName, object?[] args);
}
