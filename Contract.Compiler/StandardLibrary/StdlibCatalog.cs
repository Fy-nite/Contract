using System.Reflection;
using ObjektRT.Core.Attributes;

namespace Contract.Compiler.StandardLibrary;

/// <summary>
/// Auto-discovers <c>ObjektRT.Stdlib</c> types annotated with
/// <see cref="ClassBindingAttribute"/> and registers them into a
/// <see cref="SymbolTable"/>. Each type is registered under both its
/// short name (e.g. <c>"IO"</c>) and the <c>__builtin.std.</c> prefix
/// (e.g. <c>"__builtin.std.IO"</c>). Adding a new stdlib module now
/// requires only the <c>[ClassBinding]</c> attribute on the type — no
/// catalog update needed.
/// </summary>
public static class StdlibCatalog
{
    public static void RegisterInto(SymbolTable table)
    {
        var assembly = typeof(ObjektRT.Stdlib.System.IO).Assembly;
        foreach (var type in assembly.GetExportedTypes())
        {
            var attr = type.GetCustomAttribute<ClassBindingAttribute>();
            if (attr == null) continue;

            // Short name: IO, String, Math, ... (requires import __builtin.std;)
            table.RegisterExternalType(attr.Name, type);

            // Qualified name: __builtin.std.IO, __builtin.std.Math, ...
            table.RegisterExternalType($"__builtin.std.{attr.Name}", type);
        }
    }
}
