using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Contract.Compiler.AST;
using Contract.Compiler.Parsing;
using Contract.Compiler.StandardLibrary;

namespace Contract.LanguageServer.Lsp;

public enum SymbolCategory
{
    Contract,
    Struct,
    Enum,
    Function,
    Constructor,
    Field,
    Parameter,
    Local,
}

/// <summary>A declaration discovered in the source: contract, struct, function, field, param or local.</summary>
public class SymbolInfo
{
    public required string Name { get; init; }
    /// <summary>Namespace-qualified wire name (com.example.Foo), or the short name when unnamed.</summary>
    public string? FullName { get; init; }
    /// <summary>Base type name for contracts (single inheritance), or null.</summary>
    public string? BaseTypeName { get; init; }
    public required SymbolCategory Category { get; init; }
    public required string Uri { get; init; }
    public int Line { get; init; }      // 1-based name-token position
    public int Column { get; init; }
    public int Length { get; init; }
    public SymbolInfo? Parent { get; init; }
    public List<SymbolInfo> Children { get; } = new();
    public List<SymbolInfo> Locals { get; } = new();
    public string? Detail { get; init; }
    public string? Doc { get; init; }
    public TypeDescriptor? VarType { get; init; }
    /// <summary>True for declarations synthesized from an imported compiled
    /// module (.orbt/.oil) — they have no source position to navigate to.</summary>
    public bool IsExternal { get; init; }
    public Range SelectionRange { get; init; } = new();
    public Range ContainerRange { get; init; } = new();
}

/// <summary>The result of resolving an identifier at a position.</summary>
public class ResolvedTarget
{
    public SymbolInfo? Symbol { get; init; }
    public string? HoverText { get; init; }   // set when there is no source declaration (stdlib, modules)
}

/// <summary>
/// Indexes declarations across all files of a compilation and resolves identifier
/// references (hover / go-to-definition) using a pragmatic lexical model:
/// nearest declaration before the cursor inside the containing function, then
/// functions, then types, then stdlib modules.
/// </summary>
public class SymbolIndex
{
    private readonly List<SymbolInfo> _all = new();
    private readonly List<SymbolInfo> _functions = new();
    private readonly List<SymbolInfo> _contracts = new();
    private readonly List<SymbolInfo> _structs = new();
    private readonly List<SymbolInfo> _enums = new();
    private readonly Dictionary<string, List<SymbolInfo>> _byUri = new();

    /// <summary>XML docs for the stdlib (Contract.Compiler.xml), or null when unavailable.</summary>
    public XmlDocProvider? XmlDocs { get; }

    public SymbolIndex(XmlDocProvider? xmlDocs = null)
    {
        XmlDocs = xmlDocs;
    }

    public void Build(CompilationResult result)
    {
        _all.Clear();
        _functions.Clear();
        _contracts.Clear();
        _structs.Clear();
        _enums.Clear();
        _byUri.Clear();

        string? mainPathNorm = result.MainFile != null ? TextUtility.NormalizePath(result.MainFile.Path) : null;

        // Compiled references (.orbt/.oil): map each module type's wire name to
        // the file it was imported from, so the synthesized declarations below
        // can be attributed to a URI.
        var externalUris = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in result.Files.Values)
        {
            foreach (var extModule in file.Program.ExternalModules)
            {
                foreach (var cls in extModule.Classes) externalUris[cls.Name] = TextUtility.PathToUri(file.Path);
                foreach (var str in extModule.Structs) externalUris[str.Name] = TextUtility.PathToUri(file.Path);
            }
        }

        foreach (var file in result.Files.Values)
        {
            string uri = mainPathNorm != null && TextUtility.NormalizePath(file.Path) == mainPathNorm
                ? result.MainUri
                : TextUtility.PathToUri(file.Path);

            var tops = new List<SymbolInfo>();
            foreach (var contract in file.Program.Contracts)
                tops.Add(MakeContract(contract, uri, file.Tokens, file.Source));
            foreach (var st in file.Program.Structs)
                tops.Add(MakeStruct(st, uri, file.Tokens, null, file.Source));
            foreach (var en in file.Program.Enums)
                tops.Add(MakeEnum(en, uri, file.Tokens, null, file.Source));
            foreach (var fn in file.Program.Functions)
                tops.Add(MakeFunction(fn, uri, file.Tokens, null, file.Source));
            _byUri[uri] = tops;
        }

        // Imported compiled modules expose their types through IsExternal
        // declarations synthesized onto the merged program. They have no source
        // tokens, so they get synthetic positions — but indexing them makes
        // completion, hover, and signature help see the library's members.
        // They are deliberately kept out of _byUri (no document symbols).
        var noTokens = new List<Token>();
        foreach (var c in result.Program.Contracts)
            if (c.IsExternal)
                MakeContract(c, ExternalUri(externalUris, c.FullName), noTokens, "");
        foreach (var s in result.Program.Structs)
            if (s.IsExternal)
                MakeStruct(s, ExternalUri(externalUris, s.FullName), noTokens, null, "");
        foreach (var e in result.Program.Enums)
            if (e.IsExternal)
                MakeEnum(e, ExternalUri(externalUris, e.FullName), noTokens, null, "");
    }

    /// <summary>The URI of the compiled module file that declared <paramref name="fullName"/>, or "" when unknown.</summary>
    private static string ExternalUri(Dictionary<string, string> map, string? fullName)
        => fullName != null && map.TryGetValue(fullName, out var uri) ? uri : "";

    // ── Symbol construction ──────────────────────────────────────────────────

    private SymbolInfo MakeContract(ContractDeclaration c, string uri, List<Token> tokens, string source)
    {
        var nameTok = FindNameToken(tokens, c.Line, c.Column, c.Name);
        var sym = new SymbolInfo
        {
            Name = c.Name,
            FullName = c.FullName,
            BaseTypeName = c.BaseTypeName,
            Category = SymbolCategory.Contract,
            Uri = uri,
            IsExternal = c.IsExternal,
            Line = nameTok.Line,
            Column = nameTok.Column,
            Length = nameTok.Length,
            Detail = $"Contract {c.Name}",
            Doc = TextUtility.ExtractDocComment(source, c.Line),
            SelectionRange = TextUtility.TokenRange(nameTok),
            ContainerRange = ComputeContainerRange(tokens, nameTok),
        };
        _contracts.Add(sym);
        _all.Add(sym);

        foreach (var field in c.Fields)
            sym.Children.Add(MakeField(field, uri, tokens, sym, source));
        foreach (var ctor in c.Constructors)
            sym.Children.Add(MakeConstructor(ctor, uri, tokens, sym, source));
        foreach (var member in c.Members)
        {
            switch (member)
            {
                case FunctionDeclaration f:
                    sym.Children.Add(MakeFunction(f, uri, tokens, sym, source));
                    break;
                case StructDeclaration s:
                    sym.Children.Add(MakeStruct(s, uri, tokens, sym, source));
                    break;
                case EnumDeclaration e:
                    sym.Children.Add(MakeEnum(e, uri, tokens, sym, source));
                    break;
            }
        }
        return sym;
    }

    private SymbolInfo MakeEnum(EnumDeclaration e, string uri, List<Token> tokens, SymbolInfo? parent, string source)
    {
        var nameTok = FindNameToken(tokens, e.Line, e.Column, e.Name);
        var sym = new SymbolInfo
        {
            Name = e.Name,
            FullName = e.FullName,
            Category = SymbolCategory.Enum,
            Uri = uri,
            IsExternal = e.IsExternal,
            Line = nameTok.Line,
            Column = nameTok.Column,
            Length = nameTok.Length,
            Parent = parent,
            Detail = $"enum {e.Name} {{ {string.Join(", ", e.Members)} }}",
            Doc = TextUtility.ExtractDocComment(source, e.Line),
            SelectionRange = TextUtility.TokenRange(nameTok),
            ContainerRange = ComputeContainerRange(tokens, nameTok),
        };
        _enums.Add(sym);
        _all.Add(sym);

        // Enum members as constant children (each folds to its index).
        foreach (var member in e.Members)
        {
            var mTok = FindNameToken(tokens, e.Line, e.Column, member);
            var idx = e.Members.IndexOf(member);
            sym.Children.Add(new SymbolInfo
            {
                Name = member,
                Category = SymbolCategory.Field,
                Uri = uri,
                Line = mTok.Line,
                Column = mTok.Column,
                Length = mTok.Length,
                Parent = sym,
                Detail = $"{e.Name}.{member} = {idx}",
                SelectionRange = TextUtility.TokenRange(mTok),
                ContainerRange = ComputeContainerRange(tokens, mTok),
            });
        }
        return sym;
    }

    private SymbolInfo MakeStruct(StructDeclaration s, string uri, List<Token> tokens, SymbolInfo? parent, string source)
    {
        var nameTok = FindNameToken(tokens, s.Line, s.Column, s.Name);
        var detail = new StringBuilder($"struct {s.Name}");
        if (s.Fields.Count > 0)
        {
            detail.Append(" { ");
            detail.Append(string.Join(", ", s.Fields.Select(f => $"{f.Name}: {FormatType(f.Type)}")));
            detail.Append(" }");
        }
        var sym = new SymbolInfo
        {
            Name = s.Name,
            FullName = s.FullName,
            Category = SymbolCategory.Struct,
            Uri = uri,
            IsExternal = s.IsExternal,
            Line = nameTok.Line,
            Column = nameTok.Column,
            Length = nameTok.Length,
            Parent = parent,
            Detail = detail.ToString(),
            Doc = TextUtility.ExtractDocComment(source, s.Line),
            SelectionRange = TextUtility.TokenRange(nameTok),
            ContainerRange = ComputeContainerRange(tokens, nameTok),
        };
        _structs.Add(sym);
        _all.Add(sym);

        foreach (var field in s.Fields)
            sym.Children.Add(MakeField(field, uri, tokens, sym, source));
        foreach (var method in s.Methods)
            sym.Children.Add(MakeFunction(method, uri, tokens, sym, source));
        return sym;
    }

    private SymbolInfo MakeFunction(FunctionDeclaration f, string uri, List<Token> tokens, SymbolInfo? parent, string source)
    {
        var nameTok = FindNameToken(tokens, f.Line, f.Column, f.Name);
        // Members of an external (compiled-module) contract are library code:
        // keep them out of _functions so they don't surface as top-level
        // functions in completion and bare-call resolution.
        bool external = parent?.IsExternal ?? false;
        var sym = new SymbolInfo
        {
            Name = f.Name,
            Category = SymbolCategory.Function,
            Uri = uri,
            IsExternal = external,
            Line = nameTok.Line,
            Column = nameTok.Column,
            Length = nameTok.Length,
            Parent = parent,
            Detail = FunctionSignature(f),
            Doc = TextUtility.ExtractDocComment(source, f.Line),
            SelectionRange = TextUtility.TokenRange(nameTok),
            ContainerRange = ComputeContainerRange(tokens, nameTok),
        };
        if (!external) _functions.Add(sym);
        _all.Add(sym);

        foreach (var p in f.Parameters)
        {
            var pTok = FindNameToken(tokens, p.Line, p.Column, p.Name);
            sym.Children.Add(new SymbolInfo
            {
                Name = p.Name,
                Category = SymbolCategory.Parameter,
                Uri = uri,
                Line = pTok.Line,
                Column = pTok.Column,
                Length = pTok.Length,
                Parent = sym,
                Detail = $"{p.Name}: {FormatType(p.Type)}",
                VarType = p.Type,
                SelectionRange = TextUtility.TokenRange(pTok),
                ContainerRange = TextUtility.TokenRange(pTok),
            });
        }

        if (f.Body != null)
            CollectLocals(f.Body, sym.Locals, uri, tokens, sym, source);

        return sym;
    }

    private SymbolInfo MakeConstructor(ConstructorDeclaration c, string uri, List<Token> tokens, SymbolInfo? parent, string source)
    {
        var nameTok = FindNameToken(tokens, c.Line, c.Column, parent?.Name ?? "constructor");
        var detail = $"constructor({string.Join(", ", c.Parameters.Select(p => $"{p.Name}: {FormatType(p.Type)}"))})";
        return new SymbolInfo
        {
            Name = parent?.Name ?? "constructor",
            Category = SymbolCategory.Constructor,
            Uri = uri,
            Line = nameTok.Line,
            Column = nameTok.Column,
            Length = nameTok.Length,
            Parent = parent,
            Detail = detail,
            Doc = TextUtility.ExtractDocComment(source, c.Line),
            SelectionRange = TextUtility.TokenRange(nameTok),
            ContainerRange = ComputeContainerRange(tokens, nameTok),
        };
    }

    private SymbolInfo MakeField(StructField f, string uri, List<Token> tokens, SymbolInfo? parent, string source)
    {
        var nameTok = FindNameToken(tokens, f.Line, f.Column, f.Name);
        return new SymbolInfo
        {
            Name = f.Name,
            Category = SymbolCategory.Field,
            Uri = uri,
            Line = nameTok.Line,
            Column = nameTok.Column,
            Length = nameTok.Length,
            Parent = parent,
            Detail = $"{f.Name}: {FormatType(f.Type)}",
            Doc = TextUtility.ExtractDocComment(source, f.Line),
            VarType = f.Type,
            SelectionRange = TextUtility.TokenRange(nameTok),
            ContainerRange = TextUtility.TokenRange(nameTok),
        };
    }

    private void CollectLocals(Statement stmt, List<SymbolInfo> locals, string uri, List<Token> tokens, SymbolInfo parent, string source)
    {
        switch (stmt)
        {
            case VariableDeclaration v:
                locals.Add(MakeLocal(v, uri, tokens, parent, source));
                break;
            case BlockStatement b:
                foreach (var s in b.Statements) CollectLocals(s, locals, uri, tokens, parent, source);
                break;
            case IfStatement i:
                CollectLocals(i.ThenBranch, locals, uri, tokens, parent, source);
                if (i.ElseBranch != null) CollectLocals(i.ElseBranch, locals, uri, tokens, parent, source);
                break;
            case WhileStatement w:
                CollectLocals(w.Body, locals, uri, tokens, parent, source);
                break;
            case ForStatement f:
                if (f.Initializer != null) CollectLocals(f.Initializer, locals, uri, tokens, parent, source);
                CollectLocals(f.Body, locals, uri, tokens, parent, source);
                break;
            case SwitchStatement s:
                foreach (var c in s.Cases)
                    foreach (var st in c.Statements) CollectLocals(st, locals, uri, tokens, parent, source);
                break;
        }
    }

    private SymbolInfo MakeLocal(VariableDeclaration v, string uri, List<Token> tokens, SymbolInfo parent, string source)
    {
        var nameTok = FindNameToken(tokens, v.Line, v.Column, v.Name);
        string keyword = "var";
        var kwTok = TextUtility.FindTokenAt(tokens, v.Line, v.Column);
        if (kwTok != null && kwTok.Type == TokenType.Let) keyword = "let";
        return new SymbolInfo
        {
            Name = v.Name,
            Category = SymbolCategory.Local,
            Uri = uri,
            Line = nameTok.Line,
            Column = nameTok.Column,
            Length = nameTok.Length,
            Parent = parent,
            Detail = $"{keyword} {v.Name}{(v.Type.IsEmpty ? "" : ": " + FormatType(v.Type))}",
            Doc = TextUtility.ExtractDocComment(source, v.Line),
            VarType = v.Type.IsEmpty ? null : v.Type,
            SelectionRange = TextUtility.TokenRange(nameTok),
            ContainerRange = TextUtility.TokenRange(nameTok),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatType(TypeDescriptor t) => t.IsEmpty ? "" : (t.ToString() ?? "");

    public static string FunctionSignature(FunctionDeclaration f)
    {
        var sb = new StringBuilder();
        if (f.IsStatic) sb.Append("static ");
        sb.Append("fn ").Append(f.Name).Append('(');
        sb.Append(string.Join(", ", f.Parameters.Select(p => $"{p.Name}: {FormatType(p.Type)}")));
        sb.Append(')');
        if (f.ReturnType != null && !f.ReturnType.IsEmpty)
            sb.Append(" -> ").Append(f.ReturnType);
        return sb.ToString();
    }

    /// <summary>Finds the identifier token for a declaration, scanning for the closest match to the anchor.</summary>
    private static Token FindNameToken(List<Token> tokens, int anchorLine, int anchorCol, string name)
    {
        Token? best = null;
        long bestDist = long.MaxValue;
        foreach (var t in tokens)
        {
            if (t.Type != TokenType.Identifier || t.Text != name) continue;
            long dist = Math.Abs(t.Line - anchorLine) * 100_000L + Math.Abs(t.Column - anchorCol);
            if (dist < bestDist) { bestDist = dist; best = t; }
        }
        if (best != null) return best;
        return new Token(TokenType.Identifier, name, anchorLine, anchorCol, name.Length);
    }

    /// <summary>Ranges a container symbol from its name token to the matching closing brace.</summary>
    private static Range ComputeContainerRange(List<Token> tokens, Token nameTok)
    {
        int i = tokens.IndexOf(nameTok);
        if (i < 0) return TextUtility.TokenRange(nameTok);
        while (i < tokens.Count && tokens[i].Type != TokenType.LBrace) i++;
        if (i >= tokens.Count) return TextUtility.TokenRange(nameTok);

        int depth = 1;
        i++;
        while (i < tokens.Count && depth > 0)
        {
            if (tokens[i].Type == TokenType.LBrace) depth++;
            else if (tokens[i].Type == TokenType.RBrace) depth--;
            i++;
        }
        if (depth > 0) return TextUtility.TokenRange(nameTok); // unterminated (parse error)

        var endTok = tokens[i - 1];
        return new Range(
            new Position(nameTok.Line - 1, nameTok.Column - 1),
            new Position(endTok.Line - 1, endTok.Column - 1 + endTok.Length));
    }

    // ── Lookups ──────────────────────────────────────────────────────────────

    private SymbolInfo? FindFunction(string name)
        => _functions.FirstOrDefault(f => f.Name == name);

    private SymbolInfo? FindType(string name)
        => _contracts.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase))
           ?? _structs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(s.FullName, name, StringComparison.OrdinalIgnoreCase))
           ?? _enums.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>All members of a type, most-derived first, including inherited
    /// base members (deduped by name so an override hides the base declaration).</summary>
    private IEnumerable<SymbolInfo> TypeMembersIncludingBase(SymbolInfo typeSym)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = typeSym;
        while (current != null)
        {
            foreach (var child in current.Children)
                if (seen.Add(child.Name))
                    yield return child;
            current = current.BaseTypeName != null ? FindType(current.BaseTypeName) : null;
        }
    }

    /// <summary>The first member (own or inherited) of a type with the given name.</summary>
    private SymbolInfo? FindMemberIncludingBase(SymbolInfo typeSym, string memberName)
        => TypeMembersIncludingBase(typeSym).FirstOrDefault(c => c.Name == memberName);

    private SymbolInfo? FindContainingFunction(Position pos)
        => _functions.FirstOrDefault(f => f.ContainerRange.Contains(pos));

    private static long PosKey(int line, int column) => line * 1_000_000L + column;
    private static long PosKey(SymbolInfo s) => PosKey(s.Line, s.Column);
    private static long PosKey(Position p) => PosKey(p.Line + 1, p.Character + 1);

    private SymbolInfo? FindLocal(Position pos, string name)
    {
        var fn = FindContainingFunction(pos);
        if (fn == null) return null;
        SymbolInfo? best = null;
        long bestPos = -1;
        foreach (var candidate in fn.Children.Concat(fn.Locals))
        {
            if (candidate.Name != name) continue;
            if (candidate.Category is not (SymbolCategory.Parameter or SymbolCategory.Local)) continue;
            long pk = PosKey(candidate);
            if (pk <= PosKey(pos) && pk > bestPos) { best = candidate; bestPos = pk; }
        }
        return best;
    }

    // ── Resolution ───────────────────────────────────────────────────────────

    public ResolvedTarget? Resolve(CompilationResult result, Position pos)
    {
        var tokens = result.MainTokens;
        var token = TextUtility.FindTokenAt(tokens, pos);
        if (token == null || token.Type != TokenType.Identifier) return null;

        int i = tokens.IndexOf(token);
        var prev = i > 0 ? tokens[i - 1] : null;
        var next = i + 1 < tokens.Count ? tokens[i + 1] : null;

        // Member access: base.field / base.method
        if (prev != null && prev.Type == TokenType.Dot)
        {
            TryGetDottedBase(tokens, i - 1, out var baseName);
            return ResolveMember(token.Text, baseName, new Position(prev.Line - 1, prev.Column - 1), result);
        }

        // Scoped access: Module::member
        if (prev != null && prev.Type == TokenType.DoubleColon)
        {
            TryGetDottedBase(tokens, i - 1, out var module);
            if (module.Length == 0) return null;
            if (result.SymbolTable.TryGetMethod(module, token.Text, out var method))
            {
                if (method is ExternalMethod ext)
                    return new ResolvedTarget { HoverText = ExternalMethodMarkdown(ext) };
                if (method is FunctionDeclaration f)
                {
                    var fn = FindFunction(f.Name);
                    if (fn != null) return new ResolvedTarget { Symbol = fn };
                }
            }
            // Inherited member on a user type: Dog::Speak resolves to Animal.Speak.
            var typeSym = FindType(module);
            var hit = typeSym != null ? FindMemberIncludingBase(typeSym, token.Text) : null;
            if (hit != null) return new ResolvedTarget { Symbol = hit };
            return null;
        }

        // Type position after 'new' or ':'
        if (prev != null && (prev.Type == TokenType.New || prev.Type == TokenType.Colon))
        {
            var typeSym = FindType(token.Text);
            if (typeSym != null) return new ResolvedTarget { Symbol = typeSym };
            return null;
        }

        // Function call
        if (next != null && next.Type == TokenType.LParen)
        {
            var fn = FindFunction(token.Text);
            if (fn != null) return new ResolvedTarget { Symbol = fn };
            return null;
        }

        // Bare identifier: local → function → type → stdlib module
        var local = FindLocal(pos, token.Text);
        if (local != null) return new ResolvedTarget { Symbol = local };

        var func = FindFunction(token.Text);
        if (func != null) return new ResolvedTarget { Symbol = func };

        var typeSym2 = FindType(token.Text);
        if (typeSym2 != null) return new ResolvedTarget { Symbol = typeSym2 };

        if (result.SymbolTable.IsBoundModule(token.Text))
            return new ResolvedTarget { HoverText = ModuleHoverText(result, token.Text, token.Text) };

        return null;
    }

    /// <summary>Markdown for a stdlib module hover, including its XML class doc.</summary>
    private string ModuleHoverText(CompilationResult result, string displayName, string modulePath)
    {
        var moduleType = result.SymbolTable.GetExternalMethods(modulePath).FirstOrDefault()?.Info.DeclaringType;
        var typeDoc = moduleType != null ? XmlDocs?.GetTypeDoc(moduleType) : null;
        string hover = $"**module** `{displayName}`";
        if (typeDoc?.Summary != null) hover += $"\n\n{typeDoc.Summary}";
        hover += "\n\n*(standard library)*";
        return hover;
    }

    private ResolvedTarget? ResolveMember(string memberName, string? baseName, Position basePos, CompilationResult result)
    {
        if (baseName != null)
        {
            // this.field / this.method → the containing contract or struct
            if (baseName == "this")
            {
                var fn = FindContainingFunction(basePos);
                var owner = fn?.Parent;
                while (owner != null && owner.Category is not (SymbolCategory.Contract or SymbolCategory.Struct))
                    owner = owner.Parent;
                var hit = owner != null ? FindMemberIncludingBase(owner, memberName) : null;
                return hit != null ? new ResolvedTarget { Symbol = hit } : null;
            }

            // Stdlib module: IO.Println, Math.Sqrt, String.Length, ...
            if (result.SymbolTable.TryGetMethod(baseName, memberName, out var bound))
            {
                if (bound is ExternalMethod ext)
                    return new ResolvedTarget { HoverText = ExternalMethodMarkdown(ext) };
                if (bound is FunctionDeclaration f)
                {
                    var fn = FindFunction(f.Name);
                    if (fn != null) return new ResolvedTarget { Symbol = fn };
                }
            }

            // Mid-chain module name: A.B.Numbers (hover the module itself).
            string fullPath = baseName + "." + memberName;
            if (result.SymbolTable.IsBoundModule(fullPath))
                return new ResolvedTarget { HoverText = ModuleHoverText(result, memberName, fullPath) };

            // Known base type → search that struct/contract first (including
            // inherited base members)
            var local = FindLocal(basePos, baseName);
            if (local?.VarType is TypeDescriptor.Named named && !named.IsEmpty)
            {
                var typeSym = FindType(named.Name);
                var hit = typeSym != null ? FindMemberIncludingBase(typeSym, memberName) : null;
                if (hit != null) return new ResolvedTarget { Symbol = hit };
            }
        }

        // Fallback: search all struct/contract members by name
        var field = _structs.SelectMany(s => s.Children).FirstOrDefault(c => c.Category == SymbolCategory.Field && c.Name == memberName);
        if (field != null) return new ResolvedTarget { Symbol = field };
        var member = _contracts.SelectMany(c => c.Children)
            .FirstOrDefault(c => c.Category is SymbolCategory.Field or SymbolCategory.Function && c.Name == memberName);
        return member != null ? new ResolvedTarget { Symbol = member } : null;
    }

    // ── Outputs ──────────────────────────────────────────────────────────────

    public List<DocumentSymbol> DocumentSymbols(string uri)
    {
        if (!_byUri.TryGetValue(uri, out var tops)) return new List<DocumentSymbol>();
        return tops.Select(ToDocumentSymbol).ToList();
    }

    private static DocumentSymbol ToDocumentSymbol(SymbolInfo s)
    {
        var ds = new DocumentSymbol
        {
            Name = s.Name,
            Detail = s.Detail,
            Kind = s.Category switch
            {
                SymbolCategory.Contract => SymbolKind.Class,
                SymbolCategory.Struct => SymbolKind.Struct,
                SymbolCategory.Enum => SymbolKind.Enum,
                SymbolCategory.Function => s.Parent != null ? SymbolKind.Method : SymbolKind.Function,
                SymbolCategory.Constructor => SymbolKind.Constructor,
                SymbolCategory.Field => SymbolKind.Field,
                _ => SymbolKind.Variable,
            },
            Range = s.ContainerRange,
            SelectionRange = s.SelectionRange,
        };
        if (s.Children.Count > 0)
            ds.Children = s.Children.Select(ToDocumentSymbol).ToList();
        return ds;
    }

    public static string SymbolHoverText(SymbolInfo s)
    {
        string kind = s.Category switch
        {
            SymbolCategory.Contract => "Contract",
            SymbolCategory.Struct => "struct",
            SymbolCategory.Enum => "enum",
            SymbolCategory.Function => "function",
            SymbolCategory.Constructor => "constructor",
            SymbolCategory.Field => "field",
            SymbolCategory.Parameter => "parameter",
            _ => "variable",
        };
        var detail = s.Detail ?? s.Name;
        string head = $"**{kind}** `{s.Name}`";
        if (s.Doc != null) head += $"\n\n{s.Doc}";
        string text = $"{head}\n\n```contract\n{detail}\n```";
        if (s.IsExternal) text += "\n\n*(external module)*";
        return text;
    }

    public static string ExternalMethodSignature(ExternalMethod m)
        => $"**method** `{ExternalSignatureText(m)}`\n\n*(standard library)*";

    /// <summary>Plain signature text: IO.Println(contents: object) -> void.</summary>
    public static string ExternalSignatureText(ExternalMethod m)
    {
        string ps = string.Join(", ", m.Info.GetParameters().Select(p => $"{p.Name}: {MapSystemType(p.ParameterType)}"));
        string ret = MapSystemType(m.Info.ReturnType);
        return $"{m.ClassName}.{m.MethodName}({ps}) -> {ret}";
    }

    /// <summary>Markdown for a stdlib method, including its XML doc comment when available.</summary>
    public string ExternalMethodMarkdown(ExternalMethod m)
    {
        var doc = XmlDocs?.GetMethodDoc(m.Info);
        if (doc == null) return ExternalMethodSignature(m);

        var sb = new StringBuilder();
        sb.Append("**method** `").Append(ExternalSignatureText(m)).Append('`');
        if (doc.Summary != null) sb.Append("\n\n").Append(doc.Summary);
        if (doc.Params.Count > 0)
        {
            sb.Append("\n\n| Param | Description |\n|---|---|\n");
            foreach (var p in m.Info.GetParameters())
            {
                if (doc.Params.TryGetValue(p.Name ?? "", out var pd))
                    sb.Append("| `").Append(p.Name).Append("` | ").Append(pd).Append(" |\n");
            }
        }
        if (doc.Returns != null) sb.Append("\n**Returns:** ").Append(doc.Returns);
        sb.Append("\n\n*(standard library)*");
        return sb.ToString();
    }

    public static string MapSystemType(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(double)) return "double";
        if (t == typeof(float)) return "float";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(long)) return "long";
        if (t == typeof(object)) return "object";
        if (t == typeof(void)) return "void";
        if (t.IsArray) return MapSystemType(t.GetElementType()!) + "[]";
        return t.Name;
    }

    // ── Completion ───────────────────────────────────────────────────────────

    private static readonly (string Name, int Kind, string Detail)[] CompletionKeywords =
    {
        ("if", CompletionItemKind.Keyword, "if (condition) { }"),
        ("else", CompletionItemKind.Keyword, "else { }"),
        ("while", CompletionItemKind.Keyword, "while (condition) { }"),
        ("for", CompletionItemKind.Keyword, "for (init; condition; update) { }"),
        ("switch", CompletionItemKind.Keyword, "switch (value) { }"),
        ("case", CompletionItemKind.Keyword, "case value:"),
        ("return", CompletionItemKind.Keyword, "return expression;"),
        ("var", CompletionItemKind.Keyword, "var name: Type = value;"),
        ("let", CompletionItemKind.Keyword, "let name = value;"),
        ("fun", CompletionItemKind.Keyword, "fun x -> body"),
        ("new", CompletionItemKind.Keyword, "new Type"),
        ("true", CompletionItemKind.Keyword, "true"),
        ("false", CompletionItemKind.Keyword, "false"),
        ("null", CompletionItemKind.Keyword, "null"),
        ("break", CompletionItemKind.Keyword, "break;"),
        ("continue", CompletionItemKind.Keyword, "continue;"),
    };

    private static readonly string[] BuiltinTypeNames =
        { "int", "int64", "long", "string", "bool", "double", "float", "object", "void" };

    /// <summary>Completion items for a position, context-aware (member, module, type, or expression).</summary>
    public CompletionList Completions(CompilationResult result, Position pos)
    {
        var items = new List<CompletionItem>();
        var tokens = result.MainTokens;

        // Context token: if the cursor is inside an identifier (the word being
        // typed), use the token *before* that word; otherwise the token before
        // the cursor. This makes "IO.|" and "Module::|" resolve as member/module
        // completion even when the doc already has the full word on screen.
        var at = TextUtility.FindTokenAt(tokens, pos);
        Token? before;
        if (at != null && at.Type == TokenType.Identifier)
        {
            int ai = tokens.IndexOf(at);
            before = ai > 0 ? tokens[ai - 1] : null;
        }
        else
        {
            before = TextUtility.TokenBefore(tokens, pos);
        }

        if (before != null)
        {
            int bi = tokens.IndexOf(before);

            // Module members: Module::member
            if (before.Type == TokenType.DoubleColon && bi >= 1)
            {
                if (TryGetDottedBase(tokens, bi, out var module) && module.Length > 0)
                    AddModuleMembers(result, module, items);
                return new CompletionList { Items = items };
            }

            // Member access: base.member
            if (before.Type == TokenType.Dot && bi >= 1)
            {
                if (TryGetDottedBase(tokens, bi, out var dottedBase) && dottedBase.Length > 0)
                    AddMembers(result, dottedBase, new Position(before.Line - 1, before.Column - 1), items);
                return new CompletionList { Items = items };
            }

            // Types after 'new' or ':'
            if (before.Type == TokenType.New || before.Type == TokenType.Colon)
            {
                AddTypes(result, items);
                return new CompletionList { Items = items };
            }
        }

        // The word being typed may itself be a known type or module — the user
        // has typed `Raylib` and wants to call one of its members. Offer that
        // type's members (including inherited) instead of the keyword soup.
        Token? typedWord = at != null && at.Type == TokenType.Identifier ? at
            : (before != null && before.Type == TokenType.Identifier ? before : null);
        if (typedWord != null)
        {
            int wi = tokens.IndexOf(typedWord);
            if (TryGetDottedPathEndingAt(tokens, wi, out var typedPath) && typedPath.Length > 0)
            {
                var typedType = FindType(typedPath);
                if (typedType != null)
                {
                    foreach (var child in TypeMembersIncludingBase(typedType))
                        items.Add(SymbolCompletion(child));
                    return new CompletionList { Items = items };
                }
                if (result.SymbolTable.IsBoundModule(typedPath))
                {
                    foreach (var m in result.SymbolTable.GetExternalMethods(typedPath))
                        items.Add(ExternalCompletion(m));
                    return new CompletionList { Items = items };
                }
            }
        }

        // Expression / statement context: keywords + functions + locals + types + modules
        foreach (var kw in CompletionKeywords)
        {
            items.Add(new CompletionItem
            {
                Label = kw.Name,
                Kind = kw.Kind,
                Detail = kw.Detail,
                SortText = "0" + kw.Name,
            });
        }

        foreach (var fn in _functions)
        {
            items.Add(new CompletionItem
            {
                Label = fn.Name,
                Kind = CompletionItemKind.Function,
                Detail = fn.Detail,
                SortText = "3" + fn.Name,
            });
        }

        var fnSym = FindContainingFunction(pos);
        if (fnSym != null)
        {
            foreach (var c in fnSym.Children.Concat(fnSym.Locals))
            {
                if (c.Category is not (SymbolCategory.Parameter or SymbolCategory.Local)) continue;
                items.Add(new CompletionItem
                {
                    Label = c.Name,
                    Kind = CompletionItemKind.Variable,
                    Detail = c.Detail,
                    SortText = "2" + c.Name,
                });
            }
        }

        AddTypes(result, items, sortPrefix: "1");
        foreach (var module in result.SymbolTable.GetBoundClasses())
        {
            if (_contracts.Any(c => c.Name == module)
                || _structs.Any(s => s.Name == module)
                || _enums.Any(e => e.Name == module)) continue;
            items.Add(new CompletionItem
            {
                Label = module,
                Kind = CompletionItemKind.Module,
                Detail = "module",
                SortText = "1" + module,
            });
        }

        return new CompletionList { Items = items };
    }

    private void AddTypes(CompilationResult result, List<CompletionItem> items, string sortPrefix = "1")
    {
        foreach (var t in BuiltinTypeNames)
        {
            items.Add(new CompletionItem
            {
                Label = t,
                Kind = CompletionItemKind.Class,
                Detail = "type",
                SortText = sortPrefix + t,
            });
        }
        foreach (var c in _contracts)
        {
            items.Add(new CompletionItem
            {
                Label = c.Name,
                Kind = CompletionItemKind.Class,
                Detail = c.Detail,
                SortText = sortPrefix + c.Name,
            });
        }
        foreach (var s in _structs)
        {
            items.Add(new CompletionItem
            {
                Label = s.Name,
                Kind = CompletionItemKind.Struct,
                Detail = s.Detail,
                SortText = sortPrefix + s.Name,
            });
        }
        foreach (var e in _enums)
        {
            items.Add(new CompletionItem
            {
                Label = e.Name,
                Kind = CompletionItemKind.Enum,
                Detail = e.Detail,
                SortText = sortPrefix + e.Name,
            });
            foreach (var child in e.Children)
            {
                items.Add(new CompletionItem
                {
                    Label = child.Name,
                    Kind = CompletionItemKind.EnumMember,
                    Detail = child.Detail,
                    SortText = sortPrefix + e.Name + "." + child.Name,
                });
            }
        }
    }

    private void AddModuleMembers(CompilationResult result, string module, List<CompletionItem> items)
    {
        if (result.SymbolTable.IsBoundModule(module))
        {
            foreach (var m in result.SymbolTable.GetExternalMethods(module))
                items.Add(ExternalCompletion(m));
            return;
        }
        var typeSym = FindType(module);
        if (typeSym == null) return;
        foreach (var child in TypeMembersIncludingBase(typeSym))
            items.Add(SymbolCompletion(child));
    }

    private void AddMembers(CompilationResult result, string baseName, Position basePos, List<CompletionItem> items)
    {
        if (baseName == "this")
        {
            var fn = FindContainingFunction(basePos);
            var owner = fn?.Parent;
            while (owner != null && owner.Category is not (SymbolCategory.Contract or SymbolCategory.Struct))
                owner = owner.Parent;
            if (owner != null)
                foreach (var child in TypeMembersIncludingBase(owner)) items.Add(SymbolCompletion(child));
            return;
        }

        if (result.SymbolTable.IsBoundModule(baseName))
        {
            foreach (var m in result.SymbolTable.GetExternalMethods(baseName))
                items.Add(ExternalCompletion(m));
            return;
        }

        // User-defined type (contract/struct/enum): offer its members — e.g.
        // `Util.` → [total, Bump, Total], `Status.` → [Idle, Busy, Done].
        // Inherited base members are included (Dog. → Bark + Animal's members).
        var typeMemberSym = FindType(baseName);
        if (typeMemberSym != null)
        {
            foreach (var child in TypeMembersIncludingBase(typeMemberSym)) items.Add(SymbolCompletion(child));
            return;
        }

        // Namespace-prefix completion: ObjektRT.Stdlib. → System, Math, ...
        var nextSegments = result.SymbolTable.GetBoundClasses()
            .Where(full => full.StartsWith(baseName + ".", StringComparison.Ordinal))
            .Select(full => full.Substring(baseName.Length + 1).Split('.')[0])
            .Distinct()
            .ToList();
        if (nextSegments.Count > 0)
        {
            foreach (var seg in nextSegments)
                items.Add(new CompletionItem
                {
                    Label = seg,
                    Kind = CompletionItemKind.Module,
                    Detail = "namespace",
                    SortText = "4" + seg,
                });
            return;
        }

        // Local variable of a known type: d. → Dog's members + inherited.
        var local = FindLocal(basePos, baseName);
        if (local?.VarType is TypeDescriptor.Named named && !named.IsEmpty)
        {
            var typeSym = FindType(named.Name);
            if (typeSym != null)
            {
                foreach (var child in TypeMembersIncludingBase(typeSym)) items.Add(SymbolCompletion(child));
                return;
            }
        }

        // Nothing resolved — an unknown base like `foo.` shouldn't dump every
        // contract's members, so offer nothing.
    }

    /// <summary>
    /// Walks back from a Dot/DoubleColon token index collecting the dotted
    /// identifier path: A.B.C. → "A.B.C". Returns false when the spine is not
    /// pure identifiers and dots.
    /// </summary>
    private static bool TryGetDottedBase(IReadOnlyList<Token> tokens, int dotIndex, out string path)
    {
        path = "";
        if (dotIndex < 1 || tokens[dotIndex - 1].Type != TokenType.Identifier) return false;
        var segments = new Stack<string>();
        segments.Push(tokens[dotIndex - 1].Text);
        int i = dotIndex - 2;
        while (i >= 1 && tokens[i].Type == TokenType.Dot && tokens[i - 1].Type == TokenType.Identifier)
        {
            segments.Push(tokens[i - 1].Text);
            i -= 2;
        }
        path = string.Join(".", segments);
        return true;
    }

    /// <summary>Walks back from an identifier token collecting the dotted path
    /// ending at it: A.B.C → "A.B.C". Returns false when the spine is not pure
    /// identifiers and dots.</summary>
    private static bool TryGetDottedPathEndingAt(IReadOnlyList<Token> tokens, int idIndex, out string path)
    {
        path = "";
        if (idIndex < 0 || idIndex >= tokens.Count || tokens[idIndex].Type != TokenType.Identifier) return false;
        var segments = new Stack<string>();
        segments.Push(tokens[idIndex].Text);
        int i = idIndex - 1;
        while (i >= 1 && tokens[i].Type == TokenType.Dot && tokens[i - 1].Type == TokenType.Identifier)
        {
            segments.Push(tokens[i - 1].Text);
            i -= 2;
        }
        path = string.Join(".", segments);
        return true;
    }

    private static CompletionItem SymbolCompletion(SymbolInfo s)
    {
        int kind = s.Category switch
        {
            SymbolCategory.Function => CompletionItemKind.Function,
            SymbolCategory.Constructor => CompletionItemKind.Constructor,
            SymbolCategory.Field => CompletionItemKind.Field,
            SymbolCategory.Parameter or SymbolCategory.Local => CompletionItemKind.Variable,
            SymbolCategory.Struct => CompletionItemKind.Struct,
            SymbolCategory.Enum => CompletionItemKind.Enum,
            _ => CompletionItemKind.Class,
        };
        return new CompletionItem
        {
            Label = s.Name,
            Kind = kind,
            Detail = s.Detail,
            Documentation = s.Doc != null ? new MarkupContent { Value = s.Doc } : null,
            SortText = "4" + s.Name,
        };
    }

    private CompletionItem ExternalCompletion(ExternalMethod m)
    {
        var doc = XmlDocs?.GetMethodDoc(m.Info);
        return new CompletionItem
        {
            Label = m.MethodName,
            Kind = CompletionItemKind.Method,
            Detail = ExternalSignatureText(m),
            Documentation = new MarkupContent
            {
                Value = doc?.Summary ?? "*(standard library)*",
            },
            SortText = "4" + m.MethodName,
        };
    }

    // ── Signature help ───────────────────────────────────────────────────────

    /// <summary>Finds the enclosing call at the cursor and builds its signature + active parameter.</summary>
    public SignatureHelp? SignatureHelp(CompilationResult result, Position pos)
    {
        var tokens = result.MainTokens;

        // Innermost open paren at/before the cursor.
        var stack = new Stack<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (!TextUtility.IsAtOrBeforeStart(t, pos)) break;
            if (t.Type == TokenType.LParen) stack.Push(i);
            else if (t.Type == TokenType.RParen && stack.Count > 0) stack.Pop();
        }
        if (stack.Count == 0) return null;
        int openIdx = stack.Peek();

        // Call name: name( or Module::name( or base.name(
        int nameIdx = openIdx - 1;
        if (nameIdx < 0 || tokens[nameIdx].Type != TokenType.Identifier) return null;
        string fnName = tokens[nameIdx].Text;
        string? module = null;
        if (nameIdx >= 2 && tokens[nameIdx - 1].Type is TokenType.Dot or TokenType.DoubleColon
            && tokens[nameIdx - 2].Type == TokenType.Identifier)
        {
            module = tokens[nameIdx - 2].Text;
        }

        // Active parameter = commas at depth 0 between the open paren and the cursor.
        int active = 0;
        int depth = 0;
        for (int i = openIdx + 1; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (!TextUtility.IsAtOrBeforeStart(t, pos)) break;
            if (t.Type == TokenType.LParen) depth++;
            else if (t.Type == TokenType.RParen) { if (depth > 0) depth--; else break; }
            else if (t.Type == TokenType.Comma && depth == 0) active++;
        }

        SignatureInformation? sig = null;
        if (module != null)
        {
            if (result.SymbolTable.TryGetMethod(module, fnName, out var m))
            {
                sig = m switch
                {
                    ExternalMethod ext => SignatureFromExternal(ext),
                    FunctionDeclaration f => SignatureFromFunction(f),
                    _ => null,
                };
            }
            else
            {
                var typeSym = FindType(module);
                var child = typeSym?.Children.FirstOrDefault(c => c.Name == fnName && c.Category == SymbolCategory.Function);
                if (child != null) sig = SignatureFromSymbol(child);
            }
        }
        else
        {
            var fn = FindFunction(fnName);
            if (fn != null) sig = SignatureFromSymbol(fn);
        }

        if (sig == null) return null;
        var help = new SignatureHelp { Signatures = { sig }, ActiveSignature = 0 };
        help.ActiveParameter = sig.Parameters.Count > 0 ? Math.Clamp(active, 0, sig.Parameters.Count - 1) : 0;
        return help;
    }

    private static SignatureInformation SignatureFromSymbol(SymbolInfo s)
    {
        var sig = new SignatureInformation { Label = s.Detail ?? s.Name, Documentation = s.Doc };
        foreach (var p in s.Children.Where(c => c.Category == SymbolCategory.Parameter))
            sig.Parameters.Add(new ParameterInformation { Label = p.Detail ?? p.Name });
        return sig;
    }

    private static SignatureInformation SignatureFromFunction(FunctionDeclaration f)
    {
        var sig = new SignatureInformation { Label = FunctionSignature(f) };
        foreach (var p in f.Parameters)
            sig.Parameters.Add(new ParameterInformation { Label = $"{p.Name}: {FormatType(p.Type)}" });
        return sig;
    }

    private SignatureInformation SignatureFromExternal(ExternalMethod m)
    {
        var doc = XmlDocs?.GetMethodDoc(m.Info);
        var sig = new SignatureInformation
        {
            Label = ExternalSignatureText(m),
            Documentation = doc?.Summary,
        };
        foreach (var p in m.Info.GetParameters())
        {
            sig.Parameters.Add(new ParameterInformation
            {
                Label = $"{p.Name}: {MapSystemType(p.ParameterType)}",
                Documentation = doc?.Params.GetValueOrDefault(p.Name ?? ""),
            });
        }
        return sig;
    }

    // ── Highlight / references ───────────────────────────────────────────────

    /// <summary>Highlights the declaration (write) and same-name usages (read) in the main file.</summary>
    public List<DocumentHighlight> DocumentHighlights(CompilationResult result, Position pos)
    {
        var tokens = result.MainTokens;
        var token = TextUtility.FindTokenAt(tokens, pos);
        if (token == null || token.Type != TokenType.Identifier) return new List<DocumentHighlight>();

        var highlights = new List<DocumentHighlight>();
        var target = Resolve(result, pos);
        var sym = target?.Symbol;
        string name = sym?.Name ?? token.Text;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type != TokenType.Identifier || t.Text != name) continue;
            bool isDecl = sym != null && t.Line == sym.Line && t.Column == sym.Column;
            highlights.Add(new DocumentHighlight
            {
                Range = TextUtility.TokenRange(t),
                Kind = isDecl ? DocumentHighlightKind.Write : DocumentHighlightKind.Read,
            });
        }
        return highlights;
    }

    /// <summary>Finds all references to a symbol across every file of the compilation.</summary>
    public List<Location> References(CompilationResult result, Position pos, bool includeDeclaration)
    {
        var target = Resolve(result, pos);
        if (target?.Symbol == null) return new List<Location>();
        var sym = target.Symbol;

        var locs = new List<Location>();
        string? mainNorm = result.MainFile != null ? TextUtility.NormalizePath(result.MainFile.Path) : null;

        foreach (var file in result.Files.Values)
        {
            string uri = mainNorm != null && TextUtility.NormalizePath(file.Path) == mainNorm
                ? result.MainUri
                : TextUtility.PathToUri(file.Path);
            foreach (var t in file.Tokens)
            {
                if (t.Type != TokenType.Identifier || t.Text != sym.Name) continue;
                bool isDecl = t.Line == sym.Line && t.Column == sym.Column
                              && uri == sym.Uri;
                if (isDecl && !includeDeclaration) continue;
                locs.Add(new Location { Uri = uri, Range = TextUtility.TokenRange(t) });
            }
        }
        return locs;
    }

    // ── Semantic token support ───────────────────────────────────────────────

    /// <summary>Category of the declaration whose name token starts at the given 1-based position, if any.</summary>
    public Dictionary<(int Line, int Col), SymbolCategory> DeclarationCategories(string uri)
    {
        var map = new Dictionary<(int, int), SymbolCategory>();
        if (_byUri.TryGetValue(uri, out var tops))
            foreach (var t in tops) CollectDecls(t, map);
        return map;
    }

    private static void CollectDecls(SymbolInfo s, Dictionary<(int, int), SymbolCategory> map)
    {
        map[(s.Line, s.Column)] = s.Category;
        foreach (var c in s.Children) CollectDecls(c, map);
        foreach (var l in s.Locals) CollectDecls(l, map);
    }

    /// <summary>User-defined type category by name (Contract/Struct/Enum), or null when not a user type.</summary>
    public SymbolCategory? TypeCategory(string name)
    {
        if (_contracts.Any(c => c.Name == name)) return SymbolCategory.Contract;
        if (_structs.Any(s => s.Name == name)) return SymbolCategory.Struct;
        if (_enums.Any(e => e.Name == name)) return SymbolCategory.Enum;
        return null;
    }

    // ── Workspace symbols ─────────────────────────────────────────────────

    /// <summary>Searches all indexed symbols by name substring (case-insensitive).</summary>
    public List<SymbolInformation> WorkspaceSymbols(string query, string? stdlibModuleFilter = null)
    {
        var results = new List<SymbolInformation>();
        foreach (var s in _all)
        {
            // External (compiled-module) declarations have no navigable source
            // position — offering them in workspace symbol search would just
            // open a binary file.
            if (s.IsExternal) continue;
            if (query.Length > 0
                && !s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !(s.FullName != null && s.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new SymbolInformation
            {
                Name = s.Name,
                Kind = s.Category switch
                {
                    SymbolCategory.Contract => SymbolKind.Class,
                    SymbolCategory.Struct => SymbolKind.Struct,
                    SymbolCategory.Enum => SymbolKind.Enum,
                    SymbolCategory.Function => s.Parent != null ? SymbolKind.Method : SymbolKind.Function,
                    SymbolCategory.Constructor => SymbolKind.Constructor,
                    SymbolCategory.Field => SymbolKind.Field,
                    SymbolCategory.Parameter => SymbolKind.Variable,
                    _ => SymbolKind.Variable,
                },
                Location = new Location { Uri = s.Uri, Range = s.SelectionRange },
                ContainerName = s.Parent?.Name,
            });
        }
        return results;
    }

    // ── Inlay hints ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns inlay hints for variable/field declarations that have an explicit
    /// type in the AST but no type annotation in source (inferred bindings).
    /// </summary>
    public List<InlayHint> InlayHints(string uri, Range range)
    {
        var hints = new List<InlayHint>();
        if (!_byUri.TryGetValue(uri, out var tops)) return hints;

        foreach (var top in tops)
            CollectInlayHints(top, range, hints);
        return hints;
    }

    private void CollectInlayHints(SymbolInfo sym, Range range, List<InlayHint> hints)
    {
        if (sym.VarType != null && !sym.VarType.IsEmpty
            && sym.Category is SymbolCategory.Field or SymbolCategory.Local)
        {
            if (range.Contains(sym.SelectionRange.Start))
            {
                hints.Add(new InlayHint
                {
                    Position = new Position(sym.Line - 1, sym.Column - 1 + sym.Length),
                    Label = $": {sym.VarType}",
                    Kind = InlayHintKind.Type,
                });
            }
        }
        foreach (var child in sym.Children)
            CollectInlayHints(child, range, hints);
        foreach (var local in sym.Locals)
            CollectInlayHints(local, range, hints);
    }
}
