using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// The C# half of the <c>TypeBinding</c> binder. The registry is keyed by a
/// type's full name plus a method name, so the Contract side passes
/// <c>target.FullName</c> (the stdlib <c>Type</c> contract keeps the name in
/// that string field) and the Contract-side registry in
/// <c>ObjektRT.Core.Bindings.TypeBinding</c> uses the same
/// <c>typeName#method</c> key — both halves agree.
/// Handlers are stored as opaque <c>object</c> handles; the Contract side owns
/// delegate invocation, so the C# side just stores and echoes them.
/// </summary>
[ClassBinding("CSharpTypeBinding")]
public static class CSharpTypeBinding
{
    private static readonly Dictionary<string, object> Store = new();

    private static string Key(string typeName, string method) => typeName + "#" + method;

    /// <summary>Registers (or replaces) a C# handler for <paramref name="typeName"/>#<paramref name="method"/>.</summary>
    public static bool Bind(string typeName, string method, object handler)
    {
        Store[Key(typeName, method)] = handler;
        return true;
    }

    /// <summary>Removes the handler for <paramref name="typeName"/>#<paramref name="method"/>.</summary>
    public static bool Unbind(string typeName, string method) => Store.Remove(Key(typeName, method));

    /// <summary>True when a handler is registered for <paramref name="typeName"/>#<paramref name="method"/>.</summary>
    public static bool HasBinding(string typeName, string method) => Store.ContainsKey(Key(typeName, method));

    /// <summary>Invokes the stored handler for <paramref name="typeName"/>#<paramref name="method"/>, or null when not bound.</summary>
    public static object? TryInvoke(string typeName, object instance, string method, object[] args)
    {
        if (!Store.TryGetValue(Key(typeName, method), out var handler))
            return null;
        return Dispatch(handler, instance, args);
    }

    /// <summary>Same as <see cref="TryInvoke"/>.</summary>
    public static object? Invoke(string typeName, object instance, string method, object[] args)
        => TryInvoke(typeName, instance, method, args);

    private static object? Dispatch(object handler, object instance, object[] args)
    {
        // Handlers are stored as opaque handles; the Contract side drives the
        // delegate invocation. Echoing the stored value mirrors
        // TypeBinding.InvokeHandler.
        return handler;
    }
}