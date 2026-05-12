using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using ObjectIR.Core.Builder;
using ObjectIR.Core.Serialization;

namespace Contract.Compiler.CodeGen;
public class IRCodeGenerator(DiagnosticBag diagnostics)
{
    public static IRBuilder b;
    public void WriteToFile(string outputPath)
    {
        File.WriteAllText(outputPath,b.Build().Serialize().DumpToIRCode());
    }

    public void Generate(Program program)
    {
        foreach (var cls in program.Contracts)
        {
            
        }
    }
}