using System.Reflection;
using Contract.Compiler;
using Contract.Compiler.CodeGen;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Parsing;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;
using Contract.Compiler.StandardLibrary.Builtins;
using ObjectRT.Reader;

namespace Contract.Runtime;

/// <summary>
/// The Contract source compiler pipeline, exposed as a library: lex → parse →
/// analyze → emit ObjektIR text, or compile straight to ORBT binary bytes.
/// </summary>
public static class ContractCompiler
{
    /// <summary>Compiles a .ct file to ObjektIR text (.oil).</summary>
    /// <returns>The IR text, or null when compilation failed (errors on <paramref name="diagnostics"/>).</returns>
    public static string? CompileFile(string path, out DiagnosticBag diagnostics, IEnumerable<Assembly>? bindingAssemblies = null)
    {
        var source = File.ReadAllText(path);
        return CompileSource(source, path, out diagnostics, bindingAssemblies);
    }

    /// <summary>Compiles a .ct source string to ObjektIR text (.oil).</summary>
    public static string? CompileSource(string source, string? fileName, out DiagnosticBag diagnostics, IEnumerable<Assembly>? bindingAssemblies = null)
    {
        diagnostics = new DiagnosticBag { SourceCode = source };
        var symbolTable = new SymbolTable();
        symbolTable.RegisterAssembly(typeof(IO).Assembly);
        StdlibCatalog.RegisterInto(symbolTable);
        if (bindingAssemblies != null)
        {
            foreach (var asm in bindingAssemblies)
                symbolTable.RegisterAssembly(asm);
        }

        var driver = new CompilerDriver(diagnostics);
        var program = fileName != null ? driver.Compile(fileName) : ParseProgram(source, diagnostics);

        if (diagnostics.HasErrors) return null;

        var analyzer = new SemanticAnalyzer(symbolTable, diagnostics);
        analyzer.Analyze(program);
        if (diagnostics.HasErrors) return null;

        var codeGenerator = new IRCodeGenerator(diagnostics);
        codeGenerator.Generate(program);
        if (diagnostics.HasErrors) return null;

        return codeGenerator.GetIRText();
    }

    private static Contract.Compiler.AST.Program ParseProgram(string source, DiagnosticBag diagnostics)
    {
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.Tokenize().ToList();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    /// <summary>
    /// Compiles a .ct file to ORBT binary bytes (.orbt).
    /// </summary>
    public static byte[]? CompileFileToBinary(string path, out DiagnosticBag diagnostics, IEnumerable<Assembly>? bindingAssemblies = null)
    {
        var text = CompileFile(path, out diagnostics, bindingAssemblies);
        if (text == null) return null;
        var module = OilFileReader.ParseString(text);
        return new ORBTWriter().WriteModule(module);
    }

    /// <summary>Compiles a .ct file to an ORBT module object.</summary>
    public static ObjectRT.Abstractions.ORBTModule? CompileFileToModule(string path, out DiagnosticBag diagnostics, IEnumerable<Assembly>? bindingAssemblies = null)
    {
        var text = CompileFile(path, out diagnostics, bindingAssemblies);
        if (text == null) return null;
        return OilFileReader.ParseString(text);
    }
}
