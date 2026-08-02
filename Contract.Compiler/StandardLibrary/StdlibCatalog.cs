using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.StandardLibrary;

/// <summary>
/// Registers the generic <c>ObjektRT.Stdlib</c> modules into a
/// <see cref="SymbolTable"/> under their fully-qualified dotted names. The
/// stdlib project itself is free of Contract-specific attributes — this catalog
/// is the Contract-side mapping from language module names to CLR types. Hosts
/// register the same types with the runtime's generic <c>RegisterClrType</c>.
/// </summary>
public static class StdlibCatalog
{
    public static void RegisterInto(SymbolTable table)
    {
        table.RegisterExternalType("ObjektRT.Stdlib.System.IO", typeof(ObjektRT.Stdlib.System.IO));
        table.RegisterExternalType("ObjektRT.Stdlib.Math.Numbers", typeof(ObjektRT.Stdlib.Math.Numbers));
        table.RegisterExternalType("ObjektRT.Stdlib.Threading.Thread", typeof(ObjektRT.Stdlib.Threading.Thread));
        table.RegisterExternalType("ObjektRT.Stdlib.Generics.Array", typeof(ObjektRT.Stdlib.Generics.Array));
    }
}
