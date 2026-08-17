using ObjektRT.Core.Parsing;
using ObjektRT.Core.Serialization;
using ObjectRT.VM;

// Dump VM type/field layout after compilation to verify the inheritance fix.
if (args.Length < 1) { Console.Error.WriteLine("Usage: LayoutProbe <file.orbt>"); return 1; }
var module = OrbtFileReader.ReadFile(args[0]);
var compiled = VmCompiler.Compile(module);
if (compiled.IsError) { Console.Error.WriteLine(compiled.Error); return 1; }
foreach (var t in compiled.Value.Types)
{
    Console.WriteLine($"type '{t.DebugName}' kind={t.Kind} base={t.BaseType} fields={t.FieldCount} size={t.InstanceSize} fieldOffset={t.FieldOffset}");
}
foreach (var f in compiled.Value.Fields)
    Console.WriteLine($"  field '{f.DebugName}' offset={f.Offset}");
return 0;