using System.Linq;
using System.Reflection;
using Contract.Compiler.StandardLibrary;
using Contract.Compiler.StandardLibrary.Builtins;
using ObjectRT.Abstractions;
using ObjectRT.Reader;
using ObjectRT.Runtime;
using ObjectRT.Runtime.Reflection;

namespace Contract.Runtime;

/// <summary>
/// The Contract language runtime host.
///
/// This is the *Contract-specific* runtime: it wraps the generic
/// <see cref="ObjectRT.Runtime.Runtime"/> and pre-registers the Contract
/// standard library bindings, so the stdlib lives here — not mixed into the
/// generic ObjectRT runtime library. Custom host bindings (game engines,
/// scripting APIs, whatever) register through <see cref="RegisterBinding"/>
/// and stay out of the main runtime too.
///
/// The standard library is planned to become a standalone spec alongside this
/// C# project; this class is where the binding registration lives so that
/// split is mechanical.
/// </summary>
public class ContractRuntime : IReflectHost
{
    private readonly ObjectRT.Runtime.Runtime _runtime;

    /// <summary>The underlying ObjectRT runtime (for advanced use: JIT mode, resolvers, etc.).</summary>
    public ObjectRT.Runtime.Runtime Inner => _runtime;

    /// <summary>Creates a runtime with the Contract standard library pre-registered.</summary>
    public ContractRuntime()
    {
        _runtime = new ObjectRT.Runtime.Runtime();
        RegisterDefaultBindings();
        Contract.Compiler.StandardLibrary.Builtins.ReflectModule.Host = this;
    }

    /// <summary>Creates a runtime wrapping an existing ObjectRT runtime.</summary>
    public ContractRuntime(ObjectRT.Runtime.Runtime inner)
    {
        _runtime = inner;
        RegisterDefaultBindings();
        Contract.Compiler.StandardLibrary.Builtins.ReflectModule.Host = this;
    }

    // ── Standard library bindings ──────────────────────────────────

    /// <summary>
    /// Registers every binding in the Contract standard library. The binding
    /// classes live in Contract.Compiler.StandardLibrary.Builtins and are
    /// callable from Contract as <c>Module.Method(...)</c>.
    /// </summary>
    public void RegisterDefaultBindings()
    {
        RegisterBinding("IO", typeof(IO));
        RegisterBinding("String", typeof(StringModule));
        RegisterBinding("Math", typeof(MathModule));
        RegisterBinding("Convert", typeof(ConvertModule));
        RegisterBinding("Random", typeof(RandomModule));
        RegisterBinding("List", typeof(ListModule));
        RegisterBinding("Dict", typeof(DictModule));
        RegisterBinding("Array", typeof(ArrayModule));
        RegisterBinding("File", typeof(FileModule));
        RegisterBinding("Environment", typeof(EnvironmentModule));
        RegisterBinding("GC", typeof(GCModule));
        RegisterBinding("Debug", typeof(DebugModule));
        RegisterBinding("Time", typeof(TimeModule));
        RegisterBinding("Thread", typeof(ThreadModule));

        // Generic ObjektRT stdlib, registered under its fully-qualified names.
        RegisterBinding("ObjektRT.Stdlib.System.IO", typeof(ObjektRT.Stdlib.System.IO));
        RegisterBinding("ObjektRT.Stdlib.Math.Numbers", typeof(ObjektRT.Stdlib.Math.Numbers));
        RegisterBinding("ObjektRT.Stdlib.Threading.Thread", typeof(ObjektRT.Stdlib.Threading.Thread));
        RegisterBinding("ObjektRT.Stdlib.Generics.Array", typeof(ObjektRT.Stdlib.Generics.Array));

        // In-language reflection bridge.
        RegisterBinding("Reflect", typeof(Contract.Compiler.StandardLibrary.Builtins.ReflectModule));
    }

    /// <summary>
    /// Registers a custom host binding: a static class whose public static
    /// methods become callable from Contract as <c>name.Method(args)</c>.
    /// Custom bindings are registered here so they never touch the generic
    /// runtime library.
    /// </summary>
    public void RegisterBinding(string name, Type type) => _runtime.RegisterClrType(name, type);

    /// <summary>
    /// Registers every class in an assembly annotated with
    /// <see cref="Contract.Compiler.StandardLibrary.ClassBindingAttribute"/>,
    /// keyed by the attribute's binding name. Useful for custom binding
    /// assemblies.
    /// </summary>
    public void RegisterBindingAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<Contract.Compiler.StandardLibrary.ClassBindingAttribute>();
            if (attr != null)
                RegisterBinding(attr.Name, type);
        }
    }

    // ── Module loading ─────────────────────────────────────────────

    /// <summary>
    /// Loads a compiled module file, detecting the format from the extension:
    /// <c>.orbt</c> → binary, anything else (<c>.oil</c>/<c>.oir</c>) → text.
    /// </summary>
    public ORBTModule LoadModuleFileAuto(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".orbt"
            ? OrbtFileReader.ReadFile(path)
            : OilFileReader.ParseFile(path);
    }

    /// <summary>Loads a text module (<c>.oil</c>/<c>.oir</c>).</summary>
    public ORBTModule LoadTextModule(string oilSource) => OilFileReader.ParseString(oilSource);

    // ── Running ────────────────────────────────────────────────────

    /// <summary>Loads and runs a compiled module file, using its static Main as the entry.</summary>
    public object? RunModuleFile(string path)
    {
        var module = LoadModuleFileAuto(path);
        return RunModule(module);
    }

    /// <summary>Loads and runs a module, then returns the entry method's result.</summary>
    public object? RunModule(ORBTModule module)
    {
        _runtime.LoadModule(module);
        var entry = FindEntry(module);
        if (entry == null)
            throw new InvalidOperationException("No entry point (class with static method Main) found.");
        return _runtime.CallMethod<object?>(entry);
    }

    /// <summary>Calls an arbitrary module method by qualified name.</summary>
    public T CallMethod<T>(string qualifiedName, params object?[] args)
        => _runtime.CallMethod<T>(qualifiedName, args);

    /// <summary>Finds the qualified name of the static Main entry (e.g. "Program.Main").</summary>
    public static string? FindEntry(ORBTModule module)
    {
        foreach (var t in module.Types)
        {
            var name = $"{module.Resolve(t.NameIndex)}.Main";
            foreach (var m in t.Methods)
            {
                if (module.Resolve(m.NameIndex) == "Main")
                    return name;
            }
        }
        return null;
    }

    // ── Reflection ─────────────────────────────────────────────────

    /// <summary>
    /// C#-style reflection over the currently loaded module (types, methods,
    /// fields, attributes, inheritance) — or null when nothing is loaded yet.
    /// </summary>
    public ModuleReflector? Reflector => Inner.GetReflector();

    /// <summary>Builds a reflector over a module object without loading it.</summary>
    public static ModuleReflector ReflectModule(ORBTModule module) => new(module);

    // ── IReflectHost — backs the in-language Reflect module ─────────

    private ModuleReflector? HostReflector => Inner.GetReflector();

    string[] IReflectHost.Types()
        => HostReflector?.GetTypes().Select(t => t.Name).ToArray() ?? Array.Empty<string>();

    bool IReflectHost.HasType(string typeName)
    {
        var refl = HostReflector;
        if (refl == null) return false;
        if (refl.GetType(typeName) != null) return true;
        var shortName = ResolveShort(typeName);
        return shortName != null && refl.GetType(shortName) != null;
    }

    string[] IReflectHost.Methods(string typeName)
    {
        var t = FindHostType(typeName);
        return t?.GetMethods().Select(m => m.QualifiedName).ToArray() ?? Array.Empty<string>();
    }

    string[] IReflectHost.Fields(string typeName)
    {
        var t = FindHostType(typeName);
        return t?.GetFields().Select(f => f.QualifiedName).ToArray() ?? Array.Empty<string>();
    }

    string IReflectHost.BaseType(string typeName)
        => FindHostType(typeName)?.BaseType?.Name ?? "";

    object? IReflectHost.GetStatic(string typeName, string fieldName)
    {
        var t = FindHostType(typeName);
        var field = t?.GetField(fieldName);
        if (field == null || !field.IsStatic) return null;
        return Inner.GetStaticField(field.QualifiedName);
    }

    void IReflectHost.SetStatic(string typeName, string fieldName, object? value)
    {
        var t = FindHostType(typeName);
        var field = t?.GetField(fieldName);
        if (field == null || !field.IsStatic) return;
        Inner.SetStaticField(field.QualifiedName, value);
    }

    object? IReflectHost.Call(string typeName, string methodName, object?[] args)
    {
        var t = FindHostType(typeName);
        var method = t?.GetMethod(methodName);
        if (method == null || !method.IsStatic) return null;
        return Inner.CallMethod<object?>(method.QualifiedName, args);
    }

    private ObjectRT.Runtime.Reflection.TypeInfo? FindHostType(string typeName)
    {
        var refl = HostReflector;
        if (refl == null) return null;
        var direct = refl.GetType(typeName);
        if (direct != null) return direct;
        var shortName = ResolveShort(typeName);
        return shortName != null ? refl.GetType(shortName) : null;
    }

    /// <summary>Finds a short name's qualified form ("Geo" → "com.lib.Geo") in the loaded module.</summary>
    private string? ResolveShort(string shortName)
    {
        if (shortName.Contains('.')) return null;
        return Reflector?.GetTypes().FirstOrDefault(t =>
            t.Name == shortName || t.Name.EndsWith("." + shortName, StringComparison.Ordinal))?.Name;
    }
}
