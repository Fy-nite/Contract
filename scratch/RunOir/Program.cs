using ObjectRT.Runtime;
using Contract.Compiler.StandardLibrary.Builtins;

// Minimal end-to-end harness: register the Contract compiler's real stdlib
// modules as host bindings, load a compiled .oir module, and run its entry.
if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: RunOir <file.oir>");
    return 1;
}

var rt = new Runtime();
rt.RegisterClrType("IO", typeof(IO));
rt.RegisterClrType("String", typeof(StringModule));
rt.RegisterClrType("Math", typeof(MathModule));
rt.RegisterClrType("Convert", typeof(ConvertModule));
rt.RegisterClrType("Random", typeof(RandomModule));
rt.RegisterClrType("List", typeof(ListModule));
rt.RegisterClrType("Dict", typeof(DictModule));
rt.RegisterClrType("Array", typeof(ArrayModule));
rt.RegisterClrType("File", typeof(FileModule));
rt.RegisterClrType("Environment", typeof(EnvironmentModule));
rt.RegisterClrType("GC", typeof(GCModule));
rt.RegisterClrType("Debug", typeof(DebugModule));
rt.RegisterClrType("Time", typeof(TimeModule));
rt.RegisterClrType("Thread", typeof(ThreadModule));

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
