using ObjektRT.Core.Model;
using ObjektRT.Core.Parsing;
using ObjektRT.Core.Serialization;
using ObjectRT.Runtime;
using ObjectRT.VM;

// Captures the exact bytes the VM passes to the DllImport bridge for a struct
// argument, to diagnose the raylib Color scramble.
if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: StructProbe <file.orbt>");
    return 1;
}

var module = OrbtFileReader.ReadFile(args[0]);
DllImportResolver.AddSearchDirectory(Path.GetDirectoryName(args[0]));

var rt = new Runtime { MaxSteps = 200_000_000 };
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

rt.DllResolver.ScanModule(module, s => Console.WriteLine("SCAN: " + s));
rt.LoadModule(module);

// Intercept native calls to log the raw args (bytes for struct params).
var executor = typeof(Runtime).GetField("_executor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(rt);
if (executor is ObjectRT.VM.ExecutorBase exec)
{
    var original = exec.NativeCallHandler;
    exec.NativeCallHandler = (name, args) =>
    {
        if (name.Contains("DrawText"))
        {
            Console.WriteLine($"DrawText args: {args.Length}");
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a is byte[] bytes)
                    Console.WriteLine($"  arg[{i}] byte[]: [{string.Join(", ", bytes)}] (len {bytes.Length})");
                else
                    Console.WriteLine($"  arg[{i}]: {a ?? "<null>"} ({a?.GetType().Name})");
            }
        }
        return original(name, args);
    };
}

var entry = FindEntry(rt);
try
{
    var result = rt.CallMethod<object?>(entry);
    Console.WriteLine($"== ok: {result ?? "<null>"} ==");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"== ERROR: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null)
        Console.Error.WriteLine($"   inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
    return 2;
}
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