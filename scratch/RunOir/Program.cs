using ObjectRT.Runtime;
using ObjectRT.Reader;
using ObjectRT.VM;

// Minimal end-to-end harness: register the generic ObjektRT.Stdlib modules
// as host bindings, load a compiled .oir module, and run its entry.
// With --dump, print the compiled bytecode of *Program.Main instead of running.
if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: RunOir <file.oir> [--dump]");
    return 1;
}

if (args.Contains("--dump"))
{
    var mod = OilFileReader.ParseFile(args[0]);
    var compiled = VmCompiler.Compile(mod);
    if (compiled.IsError) { Console.Error.WriteLine(compiled.Error); return 1; }
    foreach (var fn in compiled.Value.Functions)
    {
        if (!fn.DebugName.EndsWith("Program.Main")) continue;
        Console.WriteLine($"== {fn.DebugName} (len {fn.Code.Length}) ==");
        for (int i = 0; i < fn.Code.Length; i++)
            Console.Write($"{fn.Code[i]:X2} ");
        Console.WriteLine();
    }
    return 0;
}

var rt = new Runtime();
rt.RegisterClrType("IO", typeof(ObjektRT.Stdlib.System.IO));
rt.RegisterClrType("String", typeof(ObjektRT.Stdlib.System.String));
rt.RegisterClrType("Math", typeof(ObjektRT.Stdlib.Math.Numbers));
rt.RegisterClrType("Convert", typeof(ObjektRT.Stdlib.System.Convert));
rt.RegisterClrType("Random", typeof(ObjektRT.Stdlib.System.Random));
rt.RegisterClrType("List", typeof(ObjektRT.Stdlib.Generics.List));
rt.RegisterClrType("Dict", typeof(ObjektRT.Stdlib.Generics.Dict));
rt.RegisterClrType("Array", typeof(ObjektRT.Stdlib.Generics.Array));
rt.RegisterClrType("File", typeof(ObjektRT.Stdlib.System.File));
rt.RegisterClrType("Environment", typeof(ObjektRT.Stdlib.System.Environment));
rt.RegisterClrType("GC", typeof(ObjektRT.Stdlib.System.GC));
rt.RegisterClrType("Debug", typeof(ObjektRT.Stdlib.System.Debug));
rt.RegisterClrType("Time", typeof(ObjektRT.Stdlib.System.Time));
rt.RegisterClrType("Thread", typeof(ObjektRT.Stdlib.Threading.Thread));

rt.LoadModuleFile(args[0]);
// Find the first type with a static Main, else fall back to Program.Main.
var entry = FindEntry(rt);
rt.CallMethod<object?>(entry);
return 0;

static string FindEntry(ObjectRT.Runtime.Runtime rt)
{
    var mod = typeof(ObjectRT.Runtime.Runtime).GetField("_compiled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(rt)
        as ObjectRT.VM.CompiledModule;
    if (mod != null)
    {
        foreach (var t in mod.Types)
        {
            if (mod.FunctionMap.ContainsKey($"{t.DebugName}.Main"))
                return $"{t.DebugName}.Main";
        }
    }
    return "Program.Main";
}
