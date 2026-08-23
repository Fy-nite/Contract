using System.Reflection;
using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.Attributes;
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
/// qualified and short names); custom host bindings (game engines,
/// scripting APIs, whatever) register through <see cref="RegisterBinding"/>
/// and stay out of the stdlib too.
/// </summary>
public class ContractRuntime : IHostedRuntime
{
    private readonly ObjectRT.Runtime.Runtime _runtime;

    /// <summary>The underlying ObjectRT runtime (for advanced use: JIT mode, resolvers, etc.).</summary>
    public ObjectRT.Runtime.Runtime Inner => _runtime;

    /// <summary>Creates a runtime with the standard library pre-registered.</summary>
    public ContractRuntime()
    {
        _runtime = new ObjectRT.Runtime.Runtime();
        RegisterDefaultBindings();
    }

    /// <summary>Creates a runtime wrapping an existing ObjectRT runtime.</summary>
    public ContractRuntime(ObjectRT.Runtime.Runtime inner)
    {
        _runtime = inner;
        RegisterDefaultBindings();
    }

    // â”€â”€ Standard library bindings â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Registers every binding in the standard library. The implementations
    /// live in the generic ObjektRT.Stdlib project and are auto-discovered
    /// via <c>[ClassBinding]</c> attributes on each type. Adding a new
    /// stdlib module requires only the attribute â€” no catalog update needed.
    /// </summary>
    public void RegisterDefaultBindings()
    {
        // Auto-discover stdlib types via [ClassBinding] attribute scanning.
        var stdlibAssembly = typeof(ObjektRT.Stdlib.System.IO).Assembly;
        foreach (var type in TypeLoader.GetLoadableTypes(stdlibAssembly))
        {
            var attr = type.GetCustomAttribute<ClassBindingAttribute>();
            if (attr != null)
                RegisterBinding(attr.Name, type);
        }
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
    /// <see cref="ObjektRT.Core.Attributes.ClassBindingAttribute"/>,
    /// keyed by the attribute's binding name. Useful for custom binding
    /// assemblies. Types whose dependencies are missing in this process are
    /// skipped (see <see cref="TypeLoader.GetLoadableTypes"/>).
    /// </summary>
    public void RegisterBindingAssembly(Assembly assembly)
    {
        foreach (var type in TypeLoader.GetLoadableTypes(assembly))
        {
            var attr = type.GetCustomAttribute<ClassBindingAttribute>();
            if (attr != null)
                RegisterBinding(attr.Name, type);
        }
    }

    // â”€â”€ Module loading â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Loads a compiled module file, detecting the format from the extension:
    /// <c>.orbt</c> â†’ binary, anything else (<c>.oil</c>/<c>.oir</c>) â†’ text.
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

    // â”€â”€ Running â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        => RunModule(module, null);

    /// <summary>
    /// Loads and runs a module, then returns the entry method's result. If the
    /// entry declares a C#-style <c>Main(string[] args)</c> parameter, the
    /// command-line arguments are passed through; otherwise they're ignored.
    /// </summary>
    public object? RunModule(ORBTModule module, string[]? args)
    {
        PrepareModule(module);
        _runtime.LoadModule(module);
        var entry = FindEntry(module);
        if (entry == null)
            throw new InvalidOperationException("No entry point (class with static method Main) found.");
        return _runtime.CallMethod<object?>(entry, EntryTakesArgs(module)
            ? new object?[] { args ?? Array.Empty<string>() }
            : Array.Empty<object?>());
    }

    /// <summary>True when the static Main entry declares a parameter (C#-style <c>Main(string[] args)</c>).</summary>
    private static bool EntryTakesArgs(ORBTModule module)
    {
        foreach (var t in module.Types)
        {
            foreach (var m in t.Methods)
            {
                if (module.Resolve(m.NameIndex) == "Main")
                    return m.ParamCount > 0;
            }
        }
        return false;
    }

    /// <summary>
    /// Prepares a loaded module for execution. Registers CLR types referenced
    /// by <c>&lt;ClrImport("System.Math")&gt;</c> facades with the runtime's
    /// reflection resolver (so they link without a host-side
    /// <c>[ClassBinding]</c> wrapper) and scans <c>&lt;DllImport("x.dll")&gt;</c>
    /// classes so their P/Invoke bridges are generated on first call. Idempotent â€”
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
                catch (System.Exception) { /* malformed name â€” reported below */ }

                if (clrType == null)
                {
                    Console.Error.WriteLine($"[ClrImport] type '{typeName}' could not be resolved at runtime â€” no CLR type with that name is loaded (try an assembly-qualified name)");
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
    /// given args. Safe to call from host callbacks â€” the delegate runs on a
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

    // â”€â”€ Reflection â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// C#-style reflection over the currently loaded module (types, methods,
    /// fields, attributes, inheritance) â€” or null when nothing is loaded yet.
    /// </summary>
    public ModuleReflector? Reflector => Inner.GetReflector();

    /// <summary>Builds a reflector over a module object without loading it.</summary>
    public static ModuleReflector ReflectModule(ORBTModule module) => new(module);

}
