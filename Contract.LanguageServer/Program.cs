using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Contract.LanguageServer.Lsp;

namespace Contract.LanguageServer;

/// <summary>
/// Contract Language Server entry point. Speaks LSP over stdio (Content-Length
/// framing). All protocol output goes to stdout; log/trace output goes to stderr.
///
/// Also hosted by the CLI: `ccl lsp [--trace]` runs the same loop.
/// </summary>
public static class ServerMain
{
    public static Task<int> Main(string[] args)
        => RunLsp(args.Contains("--trace", StringComparer.OrdinalIgnoreCase));

    /// <summary>Runs the LSP server loop over the process's stdio streams.</summary>
    public static async Task<int> RunLsp(bool trace)
    {
        // Raw byte streams — never write protocol data through Console text APIs.
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();

        var rpc = new JsonRpcServer(input, output);
        if (trace)
            rpc.Trace = msg => Console.Error.WriteLine(msg);

        var server = new LspServer();
        server.Register(rpc);

        try
        {
            await rpc.RunAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"contract-language-server: fatal: {ex}");
            return 1;
        }
        return 0;
    }
}
