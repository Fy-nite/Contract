using System.Collections.Generic;
using Contract.Compiler.Parsing;

namespace Contract.LanguageServer.Lsp;

/// <summary>
/// Builds LSP semantic tokens (absolute encoding, delta from previous token)
/// and brace-based folding ranges straight from the token stream.
/// </summary>
public static class SemanticTokensBuilder
{
    // Indices must match the legend order below (the protocol sends indices).
    public static readonly List<string> TokenTypes = new()
    {
        "keyword",    // 0
        "type",       // 1
        "class",      // 2
        "struct",     // 3
        "function",   // 4
        "method",     // 5
        "property",   // 6
        "variable",   // 7
        "parameter",  // 8
        "module",     // 9
        "string",     // 10
        "number",     // 11
        "operator",   // 12
    };

    public static readonly List<string> TokenModifiers = new()
    {
        "declaration",     // 0
        "defaultLibrary",  // 1
    };

    private static readonly HashSet<TokenType> Keywords = new()
    {
        TokenType.Contract, TokenType.Fn, TokenType.If, TokenType.Else, TokenType.While,
        TokenType.For, TokenType.Switch, TokenType.Case, TokenType.Return, TokenType.Var,
        TokenType.Let, TokenType.Static, TokenType.Public, TokenType.Private, TokenType.Protected,
        TokenType.Internal, TokenType.Null, TokenType.Import, TokenType.Constructor, TokenType.Struct,
        TokenType.Export, TokenType.Fun, TokenType.Types, TokenType.Type, TokenType.New,
        TokenType.Break, TokenType.Continue, TokenType.True, TokenType.False,
    };

    private static readonly HashSet<TokenType> Operators = new()
    {
        TokenType.Plus, TokenType.Minus, TokenType.Star, TokenType.Slash, TokenType.Percent,
        TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual,
        TokenType.EqualEqual, TokenType.Bang, TokenType.BangEqual, TokenType.Assign,
        TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual, TokenType.SlashEqual,
        TokenType.PercentEqual, TokenType.AndAnd, TokenType.OrOr, TokenType.Pipe, TokenType.Arrow,
    };

    public static SemanticTokens Build(CompilationResult result, string uri, SymbolIndex index)
    {
        var data = new List<int>();
        var tokens = result.MainTokens;
        var decls = index.DeclarationCategories(uri);

        int prevLine = 0, prevChar = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.EOF) continue;

            int type = Classify(t, i, tokens, result, index, decls);
            if (type < 0) continue; // punctuation — leave to the textmate grammar

            int mods = 0;
            if (decls.ContainsKey((t.Line, t.Column))) mods |= 1 << 0; // declaration
            if (t.Type == TokenType.Identifier && result.SymbolTable.IsBoundModule(t.Text)) mods |= 1 << 1; // defaultLibrary

            int line = t.Line - 1, startChar = t.Column - 1;
            data.Add(line - prevLine);
            data.Add(line == prevLine ? startChar - prevChar : startChar);
            data.Add(t.Length);
            data.Add(type);
            data.Add(mods);

            prevLine = line;
            prevChar = startChar;
        }
        return new SemanticTokens { Data = data };
    }

    private static int Classify(Token t, int i, IReadOnlyList<Token> tokens, CompilationResult result,
        SymbolIndex index, Dictionary<(int, int), SymbolCategory> decls)
    {
        if (Keywords.Contains(t.Type)) return 0;                       // keyword
        if (t.Type is TokenType.IntLiteral or TokenType.FloatLiteral) return 11; // number
        if (t.Type is TokenType.StringLiteral or TokenType.InterpolatedString) return 10; // string
        if (Operators.Contains(t.Type)) return 12;                     // operator
        if (t.Type != TokenType.Identifier) return -1;                 // braces, parens, etc.

        // Declaration name token → category-driven type.
        if (decls.TryGetValue((t.Line, t.Column), out var cat))
        {
            return cat switch
            {
                SymbolCategory.Contract => 2,   // class
                SymbolCategory.Struct => 3,     // struct
                SymbolCategory.Function => 4,   // function
                SymbolCategory.Constructor => 4,
                SymbolCategory.Field => 6,      // property
                SymbolCategory.Parameter => 8,  // parameter
                _ => 7,                         // variable
            };
        }

        // User-defined type (use site) → class / struct.
        var typeCat = index.TypeCategory(t.Text);
        if (typeCat == SymbolCategory.Contract) return 2;
        if (typeCat == SymbolCategory.Struct) return 3;

        // Builtin types (int, string, ...) used as annotations.
        if (BuiltinTypeNames.Contains(t.Text)) return 1;

        // Stdlib module use (IO, Math, ...).
        if (result.SymbolTable.IsBoundModule(t.Text)) return 9;

        // Call: identifier followed by '('
        Token? next = i + 1 < tokens.Count ? tokens[i + 1] : null;
        Token? prev = i > 0 ? tokens[i - 1] : null;

        if (prev != null && prev.Type == TokenType.Dot)
            return next != null && next.Type == TokenType.LParen ? 5 : 6; // method / property

        if (next != null && next.Type == TokenType.LParen) return 4; // function call

        return 7; // variable
    }

    private static readonly string[] BuiltinTypeNames =
        { "int", "int64", "long", "string", "bool", "double", "float", "object", "void" };

    /// <summary>Folding ranges from brace matching. Lines are 0-based per LSP.</summary>
    public static List<FoldingRange> FoldingRanges(IReadOnlyList<Token> tokens)
    {
        var folds = new List<FoldingRange>();
        var stack = new Stack<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.LBrace)
            {
                stack.Push(i);
            }
            else if (t.Type == TokenType.RBrace && stack.Count > 0)
            {
                int open = stack.Pop();
                int startLine = tokens[open].Line - 1;
                int endLine = t.Line - 1;
                if (endLine - startLine >= 1)
                    folds.Add(new FoldingRange { StartLine = startLine, EndLine = endLine });
            }
        }
        return folds;
    }
}
