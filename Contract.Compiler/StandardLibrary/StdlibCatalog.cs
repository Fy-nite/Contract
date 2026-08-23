using System.Reflection;
using ObjektRT.Core.Attributes;

namespace Contract.Compiler.StandardLibrary;

/// <summary>
/// Auto-discovers <c>ObjektRT.Stdlib</c> types annotated with
/// <see cref="ClassBindingAttribute"/> and registers them into a
/// <see cref="SymbolTable"/> under the reserved <c>__builtin.std.</c>
/// prefix ONLY — nothing is implicitly global. Call sites reach the
/// builtins either fully qualified (<c>__builtin.std.IO.Println(...)</c>)
/// or, after <c>import __builtin.std;</c>, by short name
/// (<c>IO.Println(...)</c>). User-declared contracts shadow same-named
/// builtin modules, which is what keeps a Contract-written stdlib free to
/// replace them. Adding a new stdlib module requires only the
/// <c>[ClassBinding]</c> attribute on the type.
/// </summary>
public static class StdlibCatalog
{
    public const string BuiltinPrefix = "__builtin.std";

    public static void RegisterInto(SymbolTable table)
    {
        var assembly = typeof(ObjektRT.Stdlib.System.IO).Assembly;
        foreach (var type in assembly.GetExportedTypes())
        {
            var attr = type.GetCustomAttribute<ClassBindingAttribute>();
            if (attr == null) continue;

            // Reserved root: __builtin.std.IO, __builtin.std.Math, ...
            table.RegisterExternalType($"{BuiltinPrefix}.{attr.Name}", type);

            // Real namespace too: ObjektRT.Stdlib.System.IO — so
            // `import ObjektRT.Stdlib.System;` also grants the short name,
            // and fully-qualified dotted spines resolve without any import.
            // When the CLR type name differs from the binding name
            // (class Numbers bound as "Math"), alias that spelling too.
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                table.RegisterExternalType($"{type.Namespace}.{attr.Name}", type);
                if (!string.Equals(type.Name, attr.Name, StringComparison.Ordinal))
                    table.RegisterExternalType($"{type.Namespace}.{type.Name}", type);
            }
        }
    }
}
