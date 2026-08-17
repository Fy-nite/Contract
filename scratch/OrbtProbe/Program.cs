using ObjektRT.Core.Model;
using ObjektRT.Core.Parsing;
using ObjektRT.Core.Serialization;

// Probe: parse IR text, write to bytes, re-read, and report per-method
// InstrCount vs RawInstructionData length to find the mismatch.
var ir = File.ReadAllText(args[0]);

var mod = OilFileReader.ParseString(ir);
Console.WriteLine($"parsed: {mod.Types.Count} types");

foreach (var t in mod.Types)
{
    Console.WriteLine($"type '{mod.Resolve(t.NameIndex)}' methods={t.MethodCount} attrs={t.Attributes.Count}");
    foreach (var m in t.Methods)
    {
        Console.WriteLine($"  method '{mod.Resolve(m.NameIndex)}' instrCount={m.InstrCount} rawLen={m.RawInstructionData.Length} params={m.ParamCount} locals={m.LocalCount} labels={m.LabelCount} attrs={m.Attributes.Count}");
    }
}

var bytes = new ORBTWriter().WriteModule(mod);
Console.WriteLine($"wrote {bytes.Length} bytes");

// Decode each method's raw bytecode with the reader's decoder and compare
// instruction count against the parser's InstrCount.
foreach (var t in mod.Types)
{
    foreach (var m in t.Methods)
    {
        try
        {
            var decoded = ORBTReader.DecodeRawBytecode(m.RawInstructionData, mod.StringPool);
            Console.WriteLine($"  decode '{mod.Resolve(m.NameIndex)}': parser={m.InstrCount} decoded={decoded.Count} rawLen={m.RawInstructionData.Length}");
            if (decoded.Count != m.InstrCount)
            {
                for (int i = 0; i < Math.Min(decoded.Count, m.InstrCount); i++)
                {
                    var d = decoded[i];
                    Console.WriteLine($"    [{i}] {d.Opcode} {d.Operand}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  decode '{mod.Resolve(m.NameIndex)}' FAILED: {ex.Message}");
        }
    }
}

try
{
    var back = OrbtFileReader.ReadBytes(bytes);
    Console.WriteLine($"re-read OK: {back.Types.Count} types");
}
catch (Exception ex)
{
    Console.WriteLine($"re-read FAILED: {ex.Message}");
    Console.WriteLine(ex.StackTrace?.Split('\n').FirstOrDefault());
}