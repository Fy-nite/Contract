using Contract.LanguageServer.Lsp;

var source = File.ReadAllText(args[0]);
string uri = "file:///" + args[0].Replace('\\', '/').TrimStart('/');
var store = new DocumentStore();
store.Open(uri, source, 1);
var compiler = new CompilationService(store);
var doc = store.Get(uri)!;
var result = compiler.Compile(doc);
var index = new SymbolIndex(new XmlDocProvider());
index.Build(result);

// Report any diagnostics up front — the LSP publishes these as red squiggles.
Console.WriteLine("--- DIAGNOSTICS ---");
if (result.Diagnostics.HasErrors)
    result.Diagnostics.ReportToConsole();
else
    Console.WriteLine("(none)");

var toks = result.MainTokens;

// Print every line so line numbers are visible for reference.
Console.WriteLine("--- LINES (1-based) ---");
foreach (var line in source.Split('\n').Select((s, i) => $"{i + 1}: {s.TrimEnd('\r')}"))
    Console.WriteLine(line);

void Hover(string label, Contract.Compiler.Parsing.Token tok)
{
    var pos = new Position(tok.Line - 1, tok.Column - 1 + tok.Length / 2);
    var target = index.Resolve(result, pos);
    Console.WriteLine($"{label} L{tok.Line} C{tok.Column}: {(target?.Symbol != null ? $"SYMBOL {target.Symbol.Name} ({target.Symbol.Category})" : target?.HoverText != null ? $"HOVER {target.HoverText.Split('\n')[0]}" : "null")}");
}

void Completion(string label, Contract.Compiler.Parsing.Token dotTok)
{
    var pos = new Position(dotTok.Line - 1, dotTok.Column - 1 + dotTok.Length);
    var items = index.Completions(result, pos);
    Console.WriteLine($"{label} L{dotTok.Line} C{dotTok.Column}: [{string.Join(", ", items.Items.Select(i => i.Label))}]");
}

// Hover every Println (dotted, short, scoped).
foreach (var t in toks.Where(t => t.Type == Contract.Compiler.Parsing.TokenType.Identifier && t.Text == "Println"))
    Hover("hover Println", t);

// Hover the member after :: and after . (the method name tokens).
foreach (var t in toks.Where(t => t.Type == Contract.Compiler.Parsing.TokenType.Identifier && (t.Text == "triple" || t.Text == "quadruple")))
    Hover($"hover {t.Text}", t);

// Hover the contract name in Utils:: and Utils.
foreach (var t in toks.Where(t => t.Type == Contract.Compiler.Parsing.TokenType.Identifier && t.Text == "Utils" && t.Line > 2))
    Hover("hover Utils", t);

// Completion after :: and after .
for (int i = 0; i < toks.Count; i++)
{
    if (toks[i].Type is Contract.Compiler.Parsing.TokenType.Dot or Contract.Compiler.Parsing.TokenType.DoubleColon
        && i + 1 < toks.Count && toks[i + 1].Type == Contract.Compiler.Parsing.TokenType.Identifier)
    {
        string kind = toks[i].Type == Contract.Compiler.Parsing.TokenType.DoubleColon ? "::" : ".";
        Completion($"complete after {kind}", toks[i]);
    }
}

// Hover the Numbers module use.
var numbersTok = toks.FirstOrDefault(t => t.Type == Contract.Compiler.Parsing.TokenType.Identifier && t.Text == "Numbers" && t.Line > 3);
if (numbersTok != null) Hover("hover Numbers", numbersTok);

// Completion after every '.' that is followed by an identifier (module member completion).
for (int i = 0; i < toks.Count; i++)
{
    if (toks[i].Type == Contract.Compiler.Parsing.TokenType.Dot && i + 1 < toks.Count && toks[i + 1].Type == Contract.Compiler.Parsing.TokenType.Identifier)
        Completion("complete after .", toks[i]);
}


