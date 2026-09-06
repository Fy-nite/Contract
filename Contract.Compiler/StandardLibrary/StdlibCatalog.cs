using System.Reflection;
using ObjektRT.Core.Attributes;

namespace Contract.Compiler.StandardLibrary;

/// <summary>
/// Auto-discovers <c>ObjektRT.Stdlib</c> types annotated with
/// <see cref="ClassBindingAttribute"/> and registers them into a
/// <see cref="SymbolTable"/> under the single reserved <c>__builtin</c>
/// root ONLY — nothing is implicitly global and nothing is registered
/// under a bare <c>ObjektRT.*</c> namespace as a flat key. The full tree
/// under <c>__builtin</c> mirrors the real CLR namespace hierarchy, plus a
/// <c>std</c> branch that groups every module by its binding name:
///
/// <code>
/// __builtin.std.IO                       ~ group by [ClassBinding] name
/// __builtin.ObjektRT.Stdlib.System.IO    ~ real CLR namespace path
/// __builtin.ObjektRT.Stdlib.Math.Numbers ~ type-name alias when the CLR
///                                           type name differs from the binding
///                                           name (class Numbers bound as "Math")
/// </code>
///
/// So <c>import __builtin.std;</c> grants the binding-name short modules
/// (<c>IO</c>, <c>Math</c>, ...), <c>import __builtin.ObjektRT.Stdlib.System;</c>
/// grants the short names of one real namespace, and <c>import __builtin;</c>
/// makes the two subtrees (<c>std</c> and <c>ObjektRT</c>) addressable. The
/// wire name emitted in calls always comes from the binding name (e.g.
/// <c>IO</c>), matching the host bindings. User-declared contracts shadow
/// same-named builtin modules, keeping a Contract-written stdlib free to
/// replace them. Adding a new stdlib module requires only the
/// <c>[ClassBinding]</c> attribute on the type.
/// </summary>
public static class StdlibCatalog
{
    public const string BuiltinPrefix = "__builtin";

    /// <summary>The reserved sub-namespace of <c>__builtin</c> that groups
    /// every stdlib module by its <c>[ClassBinding]</c> name.</summary>
    public const string StdPrefix = "std";

    public static void RegisterInto(SymbolTable table)
    {
        var assembly = typeof(ObjektRT.Stdlib.System.IO).Assembly;
        foreach (var type in assembly.GetExportedTypes())
        {
            var attr = type.GetCustomAttribute<ClassBindingAttribute>();
            if (attr == null) continue;

            // __builtin.std.IO — the binding-name grouping.
            table.RegisterExternalType($"{BuiltinPrefix}.{StdPrefix}.{attr.Name}", type);

            // __builtin.ObjektRT.Stdlib.System.IO — the real CLR namespace
            // path, so `import __builtin.ObjektRT.Stdlib.System;` grants the
            // short name and the fully-qualified dotted spine resolves
            // without any import. When the CLR type name differs from the
            // binding name (class Numbers bound as "Math"), alias that too.
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                table.RegisterExternalType($"{BuiltinPrefix}.{type.Namespace}.{attr.Name}", type);
                if (!string.Equals(type.Name, attr.Name, StringComparison.Ordinal))
                    table.RegisterExternalType($"{BuiltinPrefix}.{type.Namespace}.{type.Name}", type);
            }
        }
    }
}
