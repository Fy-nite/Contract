using System.Text;
using Contract.Compiler.Diagnostics;
using Contract.Runtime;
using ObjectRT.Dap;
using ObjectRT.VM;
using ObjektRT.Core.Parsing;

namespace Contract.LanguageServer.Dap;

/// <summary>
/// Contract-specific program loader for the DAP adapter: compiles a
/// <c>.ct</c> source file, VM-compiles the resulting module and wires the
/// Contract runtime host (stdlib bindings, ClrImports, DllImport scanning).
/// All protocol handling lives in <see cref="ObjectRT.Dap.DapServer"/>.
/// </summary>
public sealed class ContractDapLoader : IDapProgramLoader
{
    public Task<DapProgram> LoadAsync(string program, CancellationToken ct)
    {
        string? ir;
        DiagnosticBag diags;
        try
        {
            ir = ContractCompiler.CompileFile(program, out diags!, isExecutable: true);
        }
        catch (Exception ex)
        {
            throw new DapLoadException(ex.Message);
        }

        if (ir == null)
        {
            var sb = new StringBuilder();
            foreach (var diag in diags.Diagnostics)
                sb.AppendLine(diag.ToString());
            throw new DapLoadException(sb.Length > 0 ? sb.ToString() : "Compilation failed");
        }

        var orbtModule = OilFileReader.ParseString(ir);
        var compiled = VmCompiler.Compile(orbtModule);
        if (compiled.IsError)
            throw new DapLoadException(compiled.Error?.Message ?? "Compilation failed");

        var host = new ContractRuntime();
        host.PrepareModule(orbtModule);

        var interp = new Interpreter(compiled.Value);
        host.Inner.AttachHostHandlers(interp);

        return Task.FromResult(new DapProgram { Interpreter = interp, Module = compiled.Value });
    }
}
