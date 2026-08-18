using System.Linq;
using System.Reflection;
using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.Model;
using ObjektRT.Core.Parsing;
using ObjektRT.Core.Serialization;
using ObjectRT.Abstractions;
using ObjectRT.Runtime;
using ObjectRT.Runtime.Reflection;

namespace Contract.Runtime;

/// <summary>
/// The Contract language runtime host.
///
/// This is the *Contract-specific* runtime: it wraps the generic
/// <see cref="ObjectRT.Runtime.Runtime"/> and pre-registers the standard
/// library bindings. The stdlib implementations themselves live in the
/// generic <c>ObjektRT.Stdlib</c> project (registered under both their
/// qualified and short names); Contract-specific bindings (like the
/// in-language <c>Reflect</c> bridge) live here. Custom host bindings (game
/// engines, scripting APIs, whatever) register through
/// <see cref="RegisterBinding"/> and stay out of the stdlib too.
/// </summary>
public class ContractRuntime : IReflectHost, IHostedRuntime
{
    private readonly ObjectRT.Runtime.Runtime _runtime;

    /// <summary>The underlying ObjectRT runtime (for advanced use: JIT mode, resolvers, etc.).</summary>
    public ObjectRT.Runtime.Runtime Inner => _runtime;

    /// <summary>Creates a runtime with the standard library pre-registered.</summary>
    public ContractRuntime()
    {
        _runtime = new ObjectRT.Runtime.Runtime();
        RegisterDefaultBindings();
        Contract.Compiler.StandardLibrary.ReflectModule.Host = this;
    }

    /// <summary>Creates a runtime wrapping an existing ObjectRT runtime.</summary>
    public ContractRuntime(ObjectRT.Runtime.Runtime inner)
    {
        _runtime = inner;
        RegisterDefaultBindings();
        Contract.Compiler.StandardLibrary.ReflectModule.Host = this;
    }

    // ── Standard library bindings ──────────────────────────────────

    /// <summary>
    /// Registers every binding in the standard library. The implementations
    /// live in the generic ObjektRT.Stdlib project and are callable from
    /// Contract as <c>Module.Method(...)</c> under their short names
    /// (<c>IO.Println</c>) or fully-qualified names
    /// (<c>ObjektRT.Stdlib.System.IO.Println</c>).
    /// </summary>
    public void RegisterDefaultBindings()
    {
        // Short names — the everyday surface: IO.Println, String.Length, ...
        RegisterBinding("IO", typeof(ObjektRT.Stdlib.System.IO));
        RegisterBinding("String", typeof(ObjektRT.Stdlib.System.String));
        RegisterBinding("Convert", typeof(ObjektRT.Stdlib.System.Convert));
        RegisterBinding("Random", typeof(ObjektRT.Stdlib.System.Random));
        RegisterBinding("File", typeof(ObjektRT.Stdlib.System.File));
        RegisterBinding("Environment", typeof(ObjektRT.Stdlib.System.Environment));
        RegisterBinding("GC", typeof(ObjektRT.Stdlib.System.GC));
        RegisterBinding("Debug", typeof(ObjektRT.Stdlib.System.Debug));
        RegisterBinding("Time", typeof(ObjektRT.Stdlib.System.Time));
        RegisterBinding("Math", typeof(ObjektRT.Stdlib.Math.Numbers));
        RegisterBinding("Thread", typeof(ObjektRT.Stdlib.Threading.Thread));
        RegisterBinding("Array", typeof(ObjektRT.Stdlib.Generics.Array));
        RegisterBinding("List", typeof(ObjektRT.Stdlib.Generics.List));
        RegisterBinding("Dict", typeof(ObjektRT.Stdlib.Generics.Dict));
        // Phase 2 additions
        RegisterBinding("Json", typeof(ObjektRT.Stdlib.System.Json));
        RegisterBinding("Path", typeof(ObjektRT.Stdlib.System.Path));
        RegisterBinding("Directory", typeof(ObjektRT.Stdlib.System.Directory));
        RegisterBinding("Process", typeof(ObjektRT.Stdlib.System.Process));
        RegisterBinding("Guid", typeof(ObjektRT.Stdlib.System.Guid));
        RegisterBinding("Base64", typeof(ObjektRT.Stdlib.System.Base64));
        RegisterBinding("Console", typeof(ObjektRT.Stdlib.System.Console));
        RegisterBinding("Stack", typeof(ObjektRT.Stdlib.Generics.Stack));
        RegisterBinding("Queue", typeof(ObjektRT.Stdlib.Generics.Queue));
        RegisterBinding("HashSet", typeof(ObjektRT.Stdlib.Generics.HashSet));

        // Fully-qualified names — the official stdlib surface:
        // ObjektRT.Stdlib.System.IO.Println, ...
        RegisterBinding("ObjektRT.Stdlib.System.IO", typeof(ObjektRT.Stdlib.System.IO));
        RegisterBinding("ObjektRT.Stdlib.System.String", typeof(ObjektRT.Stdlib.System.String));
        RegisterBinding("ObjektRT.Stdlib.System.Convert", typeof(ObjektRT.Stdlib.System.Convert));
        RegisterBinding("ObjektRT.Stdlib.System.Random", typeof(ObjektRT.Stdlib.System.Random));
        RegisterBinding("ObjektRT.Stdlib.System.File", typeof(ObjektRT.Stdlib.System.File));
        RegisterBinding("ObjektRT.Stdlib.System.Environment", typeof(ObjektRT.Stdlib.System.Environment));
        RegisterBinding("ObjektRT.Stdlib.System.GC", typeof(ObjektRT.Stdlib.System.GC));
        RegisterBinding("ObjektRT.Stdlib.System.Debug", typeof(ObjektRT.Stdlib.System.Debug));
        RegisterBinding("ObjektRT.Stdlib.System.Time", typeof(ObjektRT.Stdlib.System.Time));
        RegisterBinding("ObjektRT.Stdlib.Math.Numbers", typeof(ObjektRT.Stdlib.Math.Numbers));
        RegisterBinding("ObjektRT.Stdlib.Threading.Thread", typeof(ObjektRT.Stdlib.Threading.Thread));
        RegisterBinding("ObjektRT.Stdlib.Generics.Array", typeof(ObjektRT.Stdlib.Generics.Array));
        RegisterBinding("ObjektRT.Stdlib.Generics.List", typeof(ObjektRT.Stdlib.Generics.List));
        RegisterBinding("ObjektRT.Stdlib.Generics.Dict", typeof(ObjektRT.Stdlib.Generics.Dict));
        // Phase 2 additions (fully-qualified)
        RegisterBinding("ObjektRT.Stdlib.System.Json", typeof(ObjektRT.Stdlib.System.Json));
        RegisterBinding("ObjektRT.Stdlib.System.Path", typeof(ObjektRT.Stdlib.System.Path));
        RegisterBinding("ObjektRT.Stdlib.System.Directory", typeof(ObjektRT.Stdlib.System.Directory));
        RegisterBinding("ObjektRT.Stdlib.System.Process", typeof(ObjektRT.Stdlib.System.Process));
        RegisterBinding("ObjektRT.Stdlib.System.Guid", typeof(ObjektRT.Stdlib.System.Guid));
        RegisterBinding("ObjektRT.Stdlib.System.Base64", typeof(ObjektRT.Stdlib.System.Base64));
        RegisterBinding("ObjektRT.Stdlib.System.Console", typeof(ObjektRT.Stdlib.System.Console));
        RegisterBinding("ObjektRT.Stdlib.Generics.Stack", typeof(ObjektRT.Stdlib.Generics.Stack));
        RegisterBinding("ObjektRT.Stdlib.Generics.Queue", typeof(ObjektRT.Stdlib.Generics.Queue));
        RegisterBinding("ObjektRT.Stdlib.Generics.HashSet", typeof(ObjektRT.Stdlib.Generics.HashSet));

        // In-language reflection bridge (Contract-specific host).
        RegisterBinding("Reflect", typeof(Contract.Compiler.StandardLibrary.ReflectModule));
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
    /// assemblies. Types whose dependencies are missing in this process are
    /// skipped (see <see cref="TypeLoader.GetLoadableTypes"/>).
    /// </summary>
    public void RegisterBindingAssembly(Assembly assembly)
    {
        foreach (var type in TypeLoader.GetLoadableTypes(assembly))
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
        DllImportResolver.AddSearchDirectory(Path.GetDirectoryName(path));
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

    /// <summary>
    /// Loads an already-parsed module into the VM (compiles it for execution).
    /// <see cref="RunModule"/> does load+run in one step; use this with
    /// <see cref="CallMethod{T}"/> when running an explicit entry point.
    /// </summary>
    public void LoadModule(ORBTModule module) => _runtime.LoadModule(module);

    /// <summary>Loads and runs a module, then returns the entry method's result.</summary>
    public object? RunModule(ORBTModule module)
    {
        PrepareModule(module);
        _runtime.LoadModule(module);
        var entry = FindEntry(module);
        if (entry == null)
            throw new InvalidOperationException("No entry point (class with static method Main) found.");
        return _runtime.CallMethod<object?>(entry);
    }

    /// <summary>
    /// Prepares a loaded module for execution. Registers CLR types referenced
    /// by <c>&lt;ClrImport("System.Math")&gt;</c> facades with the runtime's
    /// reflection resolver (so they link without a host-side
    /// <c>[ClassBinding]</c> wrapper) and scans <c>&lt;DllImport("x.dll")&gt;</c>
    /// classes so their P/Invoke bridges are generated on first call. Idempotent —
    /// call it before <c>CallMethod</c>/<c>RunModule</c> when running a module
    /// with an explicit entry point.
    /// </summary>
    public void PrepareModule(ORBTModule module)
    {
        RegisterClrImports(module);
        _runtime.DllResolver.ScanModule(module, null);
    }

    /// <summary>
    /// Scans module metadata for <c>@ClrImport("TypeName")</c> class
    /// annotations and registers each resolvable CLR type with the
    /// <see cref="ObjectRT.Runtime.ClrNativeResolver"/>. Call sites emitted
    /// for <c>&lt;ClrImport&gt;</c> facades target <c>TypeName.Method</c>,
    /// which the reflection resolver then dispatches.
    /// </summary>
    private void RegisterClrImports(ORBTModule module)
    {
        foreach (var type in module.Types)
        {
            foreach (var attr in type.Attributes)
            {
                var attrName = module.Resolve(attr.NameIndex);
                if (!attrName.Equals("ClrImport", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (attr.ArgIndices.Count != 1) continue;

                var typeName = module.Resolve(attr.ArgIndices[0]);
                if (typeName.Length >= 2 && typeName[0] == '"' && typeName[^1] == '"')
                    typeName = typeName[1..^1];

                System.Type? clrType = null;
                try { clrType = System.Type.GetType(typeName); }
                catch (System.Exception) { /* malformed name — reported below */ }

                if (clrType == null)
                {
                    Console.Error.WriteLine($"[ClrImport] type '{typeName}' could not be resolved at runtime — no CLR type with that name is loaded (try an assembly-qualified name)");
                    continue;
                }

                // Register under the CLR type name (what emitted call sites
                // target: System.Math.Abs) and under the facade contract's
                // wire name (so explicit entry points like `-m ClrMath.Abs`
                // resolve too).
                _runtime.ClrResolver.RegisterType(typeName, clrType);
                var className = module.Resolve(type.NameIndex);
                if (!string.Equals(className, typeName, StringComparison.OrdinalIgnoreCase))
                    _runtime.ClrResolver.RegisterType(className, clrType);
            }
        }
    }

    /// <summary>Calls an arbitrary module method by qualified name.</summary>
    public T CallMethod<T>(string qualifiedName, params object?[] args)
        => _runtime.CallMethod<T>(qualifiedName, args);

    /// <summary>
    /// Invokes a delegate value (a lambda passed to a host binding) with the
    /// given args. Safe to call from host callbacks — the delegate runs on a
    /// fresh interpreter sharing the module state, so it never disturbs the
    /// VM's current execution.
    /// </summary>
    public object? InvokeDelegate(object? handle, params object?[] args)
        => _runtime.InvokeDelegate(handle, args);

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

    string IReflectHost.ModuleName()
        => HostReflector?.ModuleName ?? "";

    string IReflectHost.Kind(string typeName)
        => FindHostType(typeName)?.Kind.ToString() ?? "";

    bool IReflectHost.IsClass(string typeName)
        => FindHostType(typeName)?.IsClass ?? false;

    bool IReflectHost.IsInterface(string typeName)
        => FindHostType(typeName)?.IsInterface ?? false;

    bool IReflectHost.IsStruct(string typeName)
        => FindHostType(typeName)?.IsStruct ?? false;

    bool IReflectHost.IsEnum(string typeName)
        => FindHostType(typeName)?.IsEnum ?? false;

    bool IReflectHost.IsAbstract(string typeName)
        => FindHostType(typeName)?.IsAbstract ?? false;

    bool IReflectHost.IsSealed(string typeName)
        => FindHostType(typeName)?.IsSealed ?? false;

    string IReflectHost.Access(string typeName)
        => FindHostType(typeName)?.Access.ToString() ?? "";

    string[] IReflectHost.Interfaces(string typeName)
        => FindHostType(typeName)?.Interfaces.Select(i => i?.Name ?? "").ToArray() ?? Array.Empty<string>();

    string[] IReflectHost.AllInterfaces(string typeName)
        => FindHostType(typeName)?.GetInterfaces().Select(i => i.Name).ToArray() ?? Array.Empty<string>();

    string[] IReflectHost.Hierarchy(string typeName)
        => FindHostType(typeName)?.GetHierarchy().Select(t => t.Name).ToArray() ?? Array.Empty<string>();

    bool IReflectHost.IsSubclassOf(string typeName, string baseTypeName)
    {
        var t = FindHostType(typeName);
        var baseType = FindHostType(baseTypeName);
        return t != null && baseType != null && t.IsSubclassOf(baseType);
    }

    bool IReflectHost.IsAssignableFrom(string typeName, string otherTypeName)
    {
        var t = FindHostType(typeName);
        var other = FindHostType(otherTypeName);
        return t != null && other != null && t.IsAssignableFrom(other);
    }

    string IReflectHost.Resolve(string qualifiedMethodName)
    {
        int dot = qualifiedMethodName.LastIndexOf('.');
        if (dot <= 0 || dot >= qualifiedMethodName.Length - 1) return "";
        var typeName = qualifiedMethodName[..dot];
        var methodName = qualifiedMethodName[(dot + 1)..];
        return FindHostType(typeName)?.FindMethod(methodName)?.QualifiedName ?? "";
    }

    string[] IReflectHost.DeclaredMethods(string typeName)
        => FindHostType(typeName)?.GetDeclaredMethods().Select(m => m.QualifiedName).ToArray() ?? Array.Empty<string>();

    string[] IReflectHost.DeclaredFields(string typeName)
        => FindHostType(typeName)?.GetDeclaredFields().Select(f => f.QualifiedName).ToArray() ?? Array.Empty<string>();

    string[] IReflectHost.Attributes(string typeName)
        => FindHostType(typeName)?.GetAttributes().Select(a => a.ToString()).ToArray() ?? Array.Empty<string>();

    string[] IReflectHost.MethodAttributes(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.GetAttributes().Select(a => a.ToString()).ToArray() ?? Array.Empty<string>();

    string IReflectHost.MethodReturn(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.ReturnTypeName ?? "";

    string[] IReflectHost.MethodParams(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.GetParameters().Select(p => p.ToString()).ToArray() ?? Array.Empty<string>();

    bool IReflectHost.MethodStatic(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.IsStatic ?? false;

    bool IReflectHost.MethodVirtual(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.IsVirtual ?? false;

    bool IReflectHost.MethodOverride(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.IsOverride ?? false;

    bool IReflectHost.MethodAbstract(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.IsAbstract ?? false;

    string IReflectHost.MethodDeclaringType(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.DeclaringType.Name ?? "";

    string IReflectHost.MethodBase(string typeName, string methodName)
        => FindHostMethod(typeName, methodName)?.GetBaseDefinition()?.QualifiedName ?? "";

    string IReflectHost.FieldType(string typeName, string fieldName)
        => FindHostField(typeName, fieldName)?.TypeName ?? "";

    bool IReflectHost.FieldStatic(string typeName, string fieldName)
        => FindHostField(typeName, fieldName)?.IsStatic ?? false;

    string IReflectHost.FieldDeclaringType(string typeName, string fieldName)
        => FindHostField(typeName, fieldName)?.DeclaringType.Name ?? "";

    object? IReflectHost.Invoke(string typeName, string methodName, object? receiver, object?[] args)
    {
        var method = FindHostMethod(typeName, methodName);
        if (method == null) return null;
        return method.Invoke(Inner, method.IsStatic ? null : receiver, args);
    }

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

    /// <summary>Finds a method by name on a type, walking the base chain (most-derived wins).</summary>
    private ObjectRT.Runtime.Reflection.MethodInfo? FindHostMethod(string typeName, string methodName)
        => FindHostType(typeName)?.FindMethod(methodName);

    /// <summary>Finds a field by name on a type, walking the base chain.</summary>
    private ObjectRT.Runtime.Reflection.FieldInfo? FindHostField(string typeName, string fieldName)
        => FindHostType(typeName)?.GetField(fieldName);

    /// <summary>Finds a short name's qualified form ("Geo" → "com.lib.Geo") in the loaded module.</summary>
    private string? ResolveShort(string shortName)
    {
        if (shortName.Contains('.')) return null;
        return Reflector?.GetTypes().FirstOrDefault(t =>
            t.Name == shortName || t.Name.EndsWith("." + shortName, StringComparison.Ordinal))?.Name;
    }
}
