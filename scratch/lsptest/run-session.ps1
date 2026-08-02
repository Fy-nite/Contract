# Scripted LSP session for smoke-testing the Contract language server.
# Builds JSON-RPC frames with Content-Length headers and pipes them to the
# server over stdin (via cmd.exe for exact byte fidelity), capturing stdout.
# The server is hosted by the CLI: Contract.Cli.exe lsp

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $root 'Contract.Cli\bin\Debug\net10.0\Contract.Cli.exe'
$uri = 'file:///d:/git/ContractIR/scratch/lsptest/test.ct'

$text = @'
Contract Greeter {
    /// Says hello to the given name.
    static fn hello(name: string) -> string {
        return "Hello, " + name;
    }
}

fn main() {
    let msg = Greeter::hello("World");
    IO.Println(msg);
    var x: int = "oops";
}
'@

function Frame([string]$json) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return "Content-Length: $($bytes.Length)`r`n`r`n$json"
}

$init = @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{} } | ConvertTo-Json -Compress -Depth 20
$initialized = @{ jsonrpc = '2.0'; method = 'initialized'; params = @{} } | ConvertTo-Json -Compress -Depth 20
$didOpen = @{
    jsonrpc = '2.0'; method = 'textDocument/didOpen';
    params = @{ textDocument = @{ uri = $uri; languageId = 'contract'; version = 1; text = $text } }
} | ConvertTo-Json -Compress -Depth 20
$symbols = @{
    jsonrpc = '2.0'; id = 2; method = 'textDocument/documentSymbol';
    params = @{ textDocument = @{ uri = $uri } }
} | ConvertTo-Json -Compress -Depth 20
$hoverMsg = @{
    jsonrpc = '2.0'; id = 3; method = 'textDocument/hover';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 8; character = 9 } }
} | ConvertTo-Json -Compress -Depth 20
$hoverHello = @{
    jsonrpc = '2.0'; id = 4; method = 'textDocument/hover';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 8; character = 25 } }
} | ConvertTo-Json -Compress -Depth 20
$hoverPrintln = @{
    jsonrpc = '2.0'; id = 5; method = 'textDocument/hover';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 9; character = 10 } }
} | ConvertTo-Json -Compress -Depth 20
$defHello = @{
    jsonrpc = '2.0'; id = 6; method = 'textDocument/definition';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 8; character = 25 } }
} | ConvertTo-Json -Compress -Depth 20
$didChange = @{
    jsonrpc = '2.0'; method = 'textDocument/didChange';
    params = @{ textDocument = @{ uri = $uri; version = 2 }; contentChanges = @(@{ text = ($text -replace 'var x: int = "oops";', 'var x: int = ;') }) }
} | ConvertTo-Json -Compress -Depth 20
$didChange = @{
    jsonrpc = '2.0'; method = 'textDocument/didChange';
    params = @{ textDocument = @{ uri = $uri; version = 2 }; contentChanges = @(@{ text = ($text -replace 'var x: int = "oops";', 'var x: int = 5') }) }
} | ConvertTo-Json -Compress -Depth 20

# ── New feature requests ────────────────────────────────────────────────────
# Completion after "IO." (line 9, char 7 = right after the dot, at start of Println)
$completion = @{
    jsonrpc = '2.0'; id = 8; method = 'textDocument/completion';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 9; character = 7 }; context = @{ triggerKind = 2; triggerCharacter = '.' } }
} | ConvertTo-Json -Compress -Depth 20
# Folding ranges
$folding = @{
    jsonrpc = '2.0'; id = 9; method = 'textDocument/foldingRange';
    params = @{ textDocument = @{ uri = $uri } }
} | ConvertTo-Json -Compress -Depth 20
# Semantic tokens
$semantic = @{
    jsonrpc = '2.0'; id = 10; method = 'textDocument/semanticTokens/full';
    params = @{ textDocument = @{ uri = $uri } }
} | ConvertTo-Json -Compress -Depth 20
# Code action on the parse-error diagnostic (from didChange; missing ';' at end of line 10)
$codeAction = @{
    jsonrpc = '2.0'; id = 11; method = 'textDocument/codeAction';
    params = @{
        textDocument = @{ uri = $uri }
        range = @{ start = @{ line = 11; character = 0 }; end = @{ line = 11; character = 1 } }
        context = @{ diagnostics = @(@{ range = @{ start = @{ line = 11; character = 0 }; end = @{ line = 11; character = 1 } }; message = "Expected ';' after variable declaration"; severity = 1 }) }
    }
} | ConvertTo-Json -Compress -Depth 20
# Signature help right after "Greeter::hello(" (line 8, char 28 = right after the '(')
$signature = @{
    jsonrpc = '2.0'; id = 12; method = 'textDocument/signatureHelp';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 8; character = 28 } }
} | ConvertTo-Json -Compress -Depth 20
# Document highlight on "msg" (line 9, char 16 = inside msg)
$highlight = @{
    jsonrpc = '2.0'; id = 13; method = 'textDocument/documentHighlight';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 9; character = 16 } }
} | ConvertTo-Json -Compress -Depth 20
# References to "hello" (line 8, char 25 = inside hello)
$references = @{
    jsonrpc = '2.0'; id = 14; method = 'textDocument/references';
    params = @{ textDocument = @{ uri = $uri }; position = @{ line = 8; character = 25 }; context = @{ includeDeclaration = $true } }
} | ConvertTo-Json -Compress -Depth 20

$shutdown = @{ jsonrpc = '2.0'; id = 15; method = 'shutdown'; params = $null } | ConvertTo-Json -Compress -Depth 20
$exit = @{ jsonrpc = '2.0'; method = 'exit'; params = $null } | ConvertTo-Json -Compress -Depth 20

$session = @(
    (Frame $init), (Frame $initialized), (Frame $didOpen),
    (Frame $symbols), (Frame $hoverMsg), (Frame $hoverHello),
    (Frame $hoverPrintln), (Frame $defHello), (Frame $didChange),
    (Frame $completion), (Frame $folding), (Frame $semantic),
    (Frame $codeAction), (Frame $signature), (Frame $highlight), (Frame $references),
    (Frame $shutdown), (Frame $exit)
) -join ''

$dir = Join-Path $root 'scratch\lsptest'
$inFile = Join-Path $dir 'session.bin'
$outFile = Join-Path $dir 'stdout.txt'
$errFile = Join-Path $dir 'stderr.txt'

[System.IO.File]::WriteAllText($inFile, $session, [System.Text.Encoding]::ASCII)

cmd /c "`"$exe`" lsp --trace < `"$inFile`" > `"$outFile`" 2> `"$errFile`""
Write-Host "=== exit: $LASTEXITCODE ==="
Write-Host "--- stderr ---"
if (Test-Path $errFile) { Get-Content $errFile }
Write-Host "--- stdout ---"
if (Test-Path $outFile) { Get-Content $outFile }
