using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary;

/// <summary>
/// Registers the <c>ObjektRT.Stdlib</c> modules into a
/// <see cref="SymbolTable"/>. The stdlib project itself is free of
/// Contract-specific attributes — this catalog is the Contract-side mapping
/// from language module names to CLR types. Each module is registered under
/// its fully-qualified dotted name (so <c>ObjektRT.Stdlib.System.IO.Println</c>
/// and <c>import ObjektRT.Stdlib.System;</c> short forms resolve) and under
/// its short name (so <c>IO.Println</c> works without an import). Hosts
/// register the same types with the runtime's generic <c>RegisterClrType</c>.
/// </summary>
public static class StdlibCatalog
{
    private static readonly (string Short, System.Type Type)[] Modules =
    {
        ("IO", typeof(ObjektRT.Stdlib.System.IO)),
        ("String", typeof(ObjektRT.Stdlib.System.String)),
        ("Convert", typeof(ObjektRT.Stdlib.System.Convert)),
        ("Random", typeof(ObjektRT.Stdlib.System.Random)),
        ("File", typeof(ObjektRT.Stdlib.System.File)),
        ("Environment", typeof(ObjektRT.Stdlib.System.Environment)),
        ("GC", typeof(ObjektRT.Stdlib.System.GC)),
        ("Debug", typeof(ObjektRT.Stdlib.System.Debug)),
        ("Time", typeof(ObjektRT.Stdlib.System.Time)),
        ("Math", typeof(ObjektRT.Stdlib.Math.Numbers)),
        ("Thread", typeof(ObjektRT.Stdlib.Threading.Thread)),
        ("Array", typeof(ObjektRT.Stdlib.Generics.Array)),
        ("List", typeof(ObjektRT.Stdlib.Generics.List)),
        ("Dict", typeof(ObjektRT.Stdlib.Generics.Dict)),
    };

    public static void RegisterInto(SymbolTable table)
    {
        // Fully-qualified names: ObjektRT.Stdlib.System.IO, ...
        foreach (var (_, type) in Modules)
            table.RegisterExternalType(type.FullName!, type);

        // Short names: IO, String, Math, ... (no import needed).
        foreach (var (shortName, type) in Modules)
            table.RegisterExternalType(shortName, type);
    }
}
