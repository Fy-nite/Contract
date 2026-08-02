using Contract.LanguageServer.Lsp;

var source = File.ReadAllText(args[0]);
var store = new DocumentStore();
store.Open("file:///probe.ct", source, 1);
var compiler = new CompilationService(store);
var doc = store.Get("file:///probe.ct")!;
var result = compiler.Compile(doc);
var index = new SymbolIndex(new XmlDocProvider());
index.Build(result);

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

// Hover the Numbers module use.
var numbersTok = toks.FirstOrDefault(t => t.Type == Contract.Compiler.Parsing.TokenType.Identifier && t.Text == "Numbers" && t.Line > 3);
if (numbersTok != null) Hover("hover Numbers", numbersTok);

// Completion after every '.' that is followed by an identifier (module member completion).
for (int i = 0; i < toks.Count; i++)
{
    if (toks[i].Type == Contract.Compiler.Parsing.TokenType.Dot && i + 1 < toks.Count && toks[i + 1].Type == Contract.Compiler.Parsing.TokenType.Identifier)
        Completion("complete after .", toks[i]);
}

