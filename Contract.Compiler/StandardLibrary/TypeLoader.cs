using System.Reflection;

namespace Contract.Compiler.StandardLibrary;

/// <summary>
/// Tolerant assembly type enumeration. <see cref="Assembly.GetTypes()"/> throws
/// <see cref="ReflectionTypeLoadException"/> when a referenced assembly (or one
/// of its dependencies) can't be loaded — common for host binding assemblies
/// that depend on UI frameworks which aren't present in a compiler/runtime
/// process. This returns the types that *did* load, dropping the rest, so
/// registration of the remaining bindings still succeeds.
/// </summary>
public static class TypeLoader
{
    /// <summary>Returns the loadable types of an assembly, skipping any that
    /// failed to load due to missing dependencies.</summary>
    public static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaded = ex.Types?.Where(t => t != null).Cast<Type>().ToArray()
                ?? Array.Empty<Type>();
            if (loaded.Length == 0 && ex.LoaderExceptions is { Length: > 0 })
            {
                // Nothing loaded: surface the first root cause so callers can
                // tell the user what's actually missing.
                throw new InvalidOperationException(
                    "Could not load any types from " + assembly.FullName + ": " +
                    ex.LoaderExceptions[0]?.Message, ex.LoaderExceptions[0]);
            }
            return loaded;
        }
    }
}
