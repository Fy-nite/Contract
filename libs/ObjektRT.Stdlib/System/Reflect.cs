using ObjektRT.Core.Attributes;
using ObjektRT.Core.Hosting;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// In-language reflection: <c>Reflect.Types()</c>, <c>Reflect.Methods("Foo")</c>,
/// <c>Reflect.GetStatic(...)</c>, <c>Reflect.Call(...)</c>, <c>Reflect.Invoke(...)</c>,
/// <c>Reflect.Hierarchy("Foo")</c>, ... — runtime introspection over the module
/// loaded into the runtime. The runtime attaches a host on load; without one
/// every call returns empty/false/null.
/// </summary>
[ClassBinding("Reflect")]
public static class Reflect
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

    /// <summary>The loaded module's name, or "" when nothing is loaded.</summary>
    [MethodBinding]
    public static string ModuleName() => Host?.ModuleName() ?? "";

    /// <summary>The type's kind: "Class" / "Interface" / "Struct" / "Enum", or "" when unknown.</summary>
    [MethodBinding]
    public static string Kind(string typeName) => Host?.Kind(typeName) ?? "";

    /// <summary>True when the type is a class (TypeKind.Class).</summary>
    [MethodBinding]
    public static bool IsClass(string typeName) => Host?.IsClass(typeName) ?? false;

    /// <summary>True when the type is an interface (TypeKind.Interface).</summary>
    [MethodBinding]
    public static bool IsInterface(string typeName) => Host?.IsInterface(typeName) ?? false;

    /// <summary>True when the type is a struct (TypeKind.Struct).</summary>
    [MethodBinding]
    public static bool IsStruct(string typeName) => Host?.IsStruct(typeName) ?? false;

    /// <summary>True when the type is an enum (TypeKind.Enum).</summary>
    [MethodBinding]
    public static bool IsEnum(string typeName) => Host?.IsEnum(typeName) ?? false;

    /// <summary>True when the type is abstract (IR TypeFlags.Abstract).</summary>
    [MethodBinding]
    public static bool IsAbstract(string typeName) => Host?.IsAbstract(typeName) ?? false;

    /// <summary>True when the type is sealed (IR TypeFlags.Sealed).</summary>
    [MethodBinding]
    public static bool IsSealed(string typeName) => Host?.IsSealed(typeName) ?? false;

    /// <summary>The type's declared access: "Public" / "Private" / "Protected" / "Internal".</summary>
    [MethodBinding]
    public static string Access(string typeName) => Host?.Access(typeName) ?? "";

    /// <summary>Direct interfaces implemented by the type, by name (external ones as "").</summary>
    [MethodBinding]
    public static string[] Interfaces(string typeName) => Host?.Interfaces(typeName) ?? Array.Empty<string>();

    /// <summary>All interfaces implemented by the type, including inherited ones.</summary>
    [MethodBinding]
    public static string[] AllInterfaces(string typeName) => Host?.AllInterfaces(typeName) ?? Array.Empty<string>();

    /// <summary>This type and all its bases, most-derived first.</summary>
    [MethodBinding]
    public static string[] Hierarchy(string typeName) => Host?.Hierarchy(typeName) ?? Array.Empty<string>();

    /// <summary>True when typeName transitively inherits from baseTypeName.</summary>
    [MethodBinding]
    public static bool IsSubclassOf(string typeName, string baseTypeName) => Host?.IsSubclassOf(typeName, baseTypeName) ?? false;

    /// <summary>True when otherTypeName is typeName, a subclass of it, or (for interfaces) an implementor of it.</summary>
    [MethodBinding]
    public static bool IsAssignableFrom(string typeName, string otherTypeName) => Host?.IsAssignableFrom(typeName, otherTypeName) ?? false;

    /// <summary>Resolves "Type.Method" through inheritance — most-derived wins — to its canonical "DeclaringType.Method" form, or "" when unresolvable.</summary>
    [MethodBinding]
    public static string Resolve(string qualifiedMethodName) => Host?.Resolve(qualifiedMethodName) ?? "";

    /// <summary>Methods declared on the type itself (not inherited), as "Type.Method".</summary>
    [MethodBinding]
    public static string[] DeclaredMethods(string typeName) => Host?.DeclaredMethods(typeName) ?? Array.Empty<string>();

    /// <summary>Fields declared on the type itself (not inherited), as "Type.field".</summary>
    [MethodBinding]
    public static string[] DeclaredFields(string typeName) => Host?.DeclaredFields(typeName) ?? Array.Empty<string>();

    /// <summary>Attributes applied to the type, as "Name(arg, ...)" strings.</summary>
    [MethodBinding]
    public static string[] Attributes(string typeName) => Host?.Attributes(typeName) ?? Array.Empty<string>();

    /// <summary>Attributes applied to a method, as "Name(arg, ...)" strings.</summary>
    [MethodBinding]
    public static string[] MethodAttributes(string typeName, string methodName) => Host?.MethodAttributes(typeName, methodName) ?? Array.Empty<string>();

    /// <summary>The method's declared return type name ("int32", "string", "void", ...), or "" when unknown.</summary>
    [MethodBinding]
    public static string MethodReturn(string typeName, string methodName) => Host?.MethodReturn(typeName, methodName) ?? "";

    /// <summary>The method's parameters as "type name" strings ("int32 x"), or empty when unknown. Instance methods include "this" as parameter 0.</summary>
    [MethodBinding]
    public static string[] MethodParams(string typeName, string methodName) => Host?.MethodParams(typeName, methodName) ?? Array.Empty<string>();

    /// <summary>True when the method is static.</summary>
    [MethodBinding]
    public static bool MethodStatic(string typeName, string methodName) => Host?.MethodStatic(typeName, methodName) ?? false;

    /// <summary>True when the method is virtual (IR MethodFlags.Virtual).</summary>
    [MethodBinding]
    public static bool MethodVirtual(string typeName, string methodName) => Host?.MethodVirtual(typeName, methodName) ?? false;

    /// <summary>True when the method is an override (IR MethodFlags.Override).</summary>
    [MethodBinding]
    public static bool MethodOverride(string typeName, string methodName) => Host?.MethodOverride(typeName, methodName) ?? false;

    /// <summary>True when the method is abstract (IR MethodFlags.Abstract).</summary>
    [MethodBinding]
    public static bool MethodAbstract(string typeName, string methodName) => Host?.MethodAbstract(typeName, methodName) ?? false;

    /// <summary>The type that declares the method (the base type for inherited lookups), or "" when unknown.</summary>
    [MethodBinding]
    public static string MethodDeclaringType(string typeName, string methodName) => Host?.MethodDeclaringType(typeName, methodName) ?? "";

    /// <summary>The base definition of the method — the "Type.Method" root of an override chain — or "" when unknown.</summary>
    [MethodBinding]
    public static string MethodBase(string typeName, string methodName) => Host?.MethodBase(typeName, methodName) ?? "";

    /// <summary>The field's declared type name ("int32", ...), or "" when unknown.</summary>
    [MethodBinding]
    public static string FieldType(string typeName, string fieldName) => Host?.FieldType(typeName, fieldName) ?? "";

    /// <summary>True when the field is static.</summary>
    [MethodBinding]
    public static bool FieldStatic(string typeName, string fieldName) => Host?.FieldStatic(typeName, fieldName) ?? false;

    /// <summary>The type that declares the field, or "" when unknown.</summary>
    [MethodBinding]
    public static string FieldDeclaringType(string typeName, string fieldName) => Host?.FieldDeclaringType(typeName, fieldName) ?? "";

    /// <summary>Reads a static field by type name + field name.</summary>
    [MethodBinding]
    public static object? GetStatic(string typeName, string fieldName) => Host?.GetStatic(typeName, fieldName);

    /// <summary>Writes a static field by type name + field name.</summary>
    [MethodBinding]
    public static void SetStatic(string typeName, string fieldName, object? value) => Host?.SetStatic(typeName, fieldName, value);

    /// <summary>Invokes a static method by type name + method name with args.</summary>
    [MethodBinding]
    public static object? Call(string typeName, string methodName, object?[] args) => Host?.Call(typeName, methodName, args);

    /// <summary>
    /// Invokes a method by type name + method name with a receiver and args.
    /// For static methods the receiver is ignored; for instance methods it
    /// must be the handle returned by a previous call (e.g. from
    /// <c>Reflect.Call</c>).
    /// </summary>
    [MethodBinding]
    public static object? Invoke(string typeName, string methodName, object? receiver, object?[] args) => Host?.Invoke(typeName, methodName, receiver, args);
}
