using ObjektRT.Core.Model;
using ObjektRT.Core.Parsing;
using ObjektRT.Core.Serialization;
using ObjectRT.Runtime;
using ObjectRT.VM;

// Diagnostic harness:
//   RunOir <file.orbt|oil|oir> [--dump] [--emit <dir>]
// --dump: print the compiled bytecode of *Program.Main instead of running.
// --emit <dir>: write the generated DllImport bridge source to <dir>.
// A step limit is always armed so an infinite loop surfaces as an error.
if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: RunOir <file.oir> [--dump] [--emit <dir>]");
    return 1;
}

bool dump = args.Contains("--dump");
bool dumpAll = args.Contains("--dump-all");
string? emitDir = null;
for (int i = 1; i < args.Length; i++)
    if (args[i] == "--emit" && i + 1 < args.Length) emitDir = args[++i];

if (emitDir != null)
    DllImportResolver.EmitDir = emitDir;

ORBTModule ReadModule(string path) => path.EndsWith(".orbt", StringComparison.OrdinalIgnoreCase)
    ? OrbtFileReader.ReadFile(path)
    : OilFileReader.ParseFile(path);

if (dumpAll)
{
    var m = ReadModule(args[0]);
    foreach (var t in m.Types)
    {
        var attrs = string.Join(", ", t.Attributes.Select(a => $"[{m.Resolve(a.NameIndex)} args={a.ArgIndices.Count}]"));
        Console.WriteLine($"type '{m.Resolve(t.NameIndex)}' attrs: {attrs}");
    }
    var rt2 = new Runtime();
    var scanned = rt2.DllResolver.ScanModule(m, s => Console.WriteLine("SCAN: " + s));
    Console.WriteLine($"ScanModule found {scanned} import classes");
    rt2.LoadModule(m);
    var cm = typeof(Runtime).GetField("_compiled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(rt2)
        as ObjectRT.VM.CompiledModule;
    foreach (var fn in cm.Functions)
        Console.WriteLine($"== {fn.DebugName} (len {fn.Code.Length}) == {string.Join(" ", fn.Code.Select(b => b.ToString("X2")))}");
    return 0;
}

if (dump)
{
    var mod = ReadModule(args[0]);
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

var module = ReadModule(args[0]);
DllImportResolver.AddSearchDirectory(Path.GetDirectoryName(args[0]));
rt.DllResolver.ScanModule(module, s => Console.WriteLine("SCAN: " + s));
rt.LoadModule(module);
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
