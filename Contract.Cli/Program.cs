using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Contract.Compiler;
using Contract.Compiler.Parsing;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.CodeGen;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;
using Contract.Compiler.StandardLibrary.Builtins;

namespace Contract.Cli
{
    class Program
    {
        static void Main(string[] args)
        {
            var debug = false;
            if (args.Length == 1 && args[0] == "--test")
            {
                TestRunner.RunTests();
                return;
            }

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: Contract.Cli <file.ct>");
                Console.WriteLine("       Contract.Cli --test");
                return;
            }

            string filePath = args[0];
            if (args.Contains("--debug"))
            {
                debug = true;
            }
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File not found: {filePath}");
                return;
            }

            try
            {
                // Diagnostics and Standard Library Setup
                var diagnostics = new DiagnosticBag();
                diagnostics.SourceCode = File.ReadAllText(filePath);
                var symbolTable = new SymbolTable();
                symbolTable.RegisterAssembly(typeof(IO).Assembly);

                // Use CompilerDriver to handle imports and recursive loading
                Console.WriteLine("Loading and parsing files...");
                var driver = new CompilerDriver(diagnostics);
                var program = driver.Compile(filePath);

                if (diagnostics.HasErrors)
                {
                    Console.WriteLine("\nErrors during loading/parsing:");
                    diagnostics.ReportToConsole();
                    return;
                }

                // Semantic Analysis (Symbol Linking)
                Console.WriteLine("Starting semantic analysis...");
                var analyzer = new SemanticAnalyzer(symbolTable, diagnostics);
                analyzer.Analyze(program);

                // Report diagnostics
                if (diagnostics.Diagnostics.Count > 0)
                {
                    Console.WriteLine("\nDiagnostics:");
                    diagnostics.ReportToConsole();
                }

                if (diagnostics.HasErrors)
                {
                    Console.WriteLine("Compilation failed due to errors.");
                    return;
                }

                if (debug)
                {
                    Console.WriteLine("\nParsed AST:");
                    PrintAstSimple(program);
                }

                // Code generation
                Console.WriteLine("\nGenerating IR...");
                var codeGenerator = new IRCodeGenerator(diagnostics);
                codeGenerator.Generate(program);
                
                string outputPath = Path.ChangeExtension(filePath, ".oir");
                codeGenerator.WriteToFile(outputPath);
                Console.WriteLine($"Bytecode written to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
        }

        static void PrintAstSimple(Node node, int indent = 0)
        {
            string prefix = new string(' ', indent * 2);
            string symbolInfo = "";
            if (node.Symbol is ExternalMethod em)
            {
                symbolInfo = $" \u001b[32m[Linked to {em.ClassName}.{em.MethodName}]\u001b[0m";
            }
            Console.WriteLine($"{prefix}{node.GetType().Name} at ({node.Line}:{node.Column}){symbolInfo}");

            // Recursively print children
            if (node is Contract.Compiler.AST.Program p)
            {
                foreach (var contract in p.Contracts)
                    PrintAstSimple(contract, indent + 1);
                foreach (var func in p.Functions)
                    PrintAstSimple(func, indent + 1);
            }
            else if (node is ContractDeclaration c)
            {
                Console.WriteLine($"{prefix}  Name: {c.Name}");
                foreach (var member in c.Members)
                    PrintAstSimple(member, indent + 1);
            }
            else if (node is FunctionDeclaration f)
            {
                string staticPart = f.IsStatic ? "static " : "";
                string accessPart = f.Access != AccessModifier.Default ? f.Access.ToString().ToLower() + " " : "";
                Console.WriteLine($"{prefix}  Name: {accessPart}{staticPart}{f.Name}");
                if (f.Body != null)
                    PrintAstSimple(f.Body, indent + 1);
            }
            else if (node is BlockStatement b)
            {
                foreach (var stmt in b.Statements)
                    PrintAstSimple(stmt, indent + 1);
            }
            else if (node is VariableDeclaration v)
            {
                Console.WriteLine($"{prefix}  Name: {v.Name}, Type: {v.Type}");
                if (v.Initializer != null)
                    PrintAstSimple(v.Initializer, indent + 1);
            }
            else if (node is IfStatement ifs)
            {
                PrintAstSimple(ifs.Condition, indent + 1);
                PrintAstSimple(ifs.ThenBranch, indent + 1);
                if (ifs.ElseBranch != null)
                    PrintAstSimple(ifs.ElseBranch, indent + 1);
            }
            else if (node is WhileStatement w)
            {
                PrintAstSimple(w.Condition, indent + 1);
                PrintAstSimple(w.Body, indent + 1);
            }
            else if (node is SwitchStatement sw)
            {
                PrintAstSimple(sw.Expression, indent + 1);
                foreach (var caseStmt in sw.Cases)
                    PrintAstSimple(caseStmt, indent + 1);
            }
            else if (node is SwitchCase sc)
            {
                Console.WriteLine($"{prefix}  Value: {sc.Value?.ToString() ?? "else"}");
                foreach (var stmt in sc.Statements)
                    PrintAstSimple(stmt, indent + 1);
            }
            else if (node is ReturnStatement r)
            {
                if (r.Value != null)
                    PrintAstSimple(r.Value, indent + 1);
            }
            else if (node is ExpressionStatement e)
            {
                PrintAstSimple(e.Expression, indent + 1);
            }
            else if (node is BinaryExpression bin)
            {
                Console.WriteLine($"{prefix}  Operator: {bin.Operator}");
                PrintAstSimple(bin.Left, indent + 1);
                PrintAstSimple(bin.Right, indent + 1);
            }
            else if (node is CallExpression call)
            {
                PrintAstSimple(call.Callee, indent + 1);
                foreach (var arg in call.Arguments)
                    PrintAstSimple(arg, indent + 1);
            }
            else if (node is IdentifierExpression id)
            {
                Console.WriteLine($"{prefix}  Name: {id.Name}");
            }
            else if (node is LiteralExpression lit)
            {
                Console.WriteLine($"{prefix}  Value: {lit.Value}");
            }
            else if (node is MemberExpression mem)
            {
                Console.WriteLine($"{prefix}  Property: {mem.Property}");
                PrintAstSimple(mem.Object, indent + 1);
            }
            else if (node is IndexExpression idx)
            {
                PrintAstSimple(idx.Target, indent + 1);
                PrintAstSimple(idx.Index, indent + 1);
            }
        }
        static void PrintInlineExpr(Expression expr)
        {
            switch (expr)
            {
                case IdentifierExpression id:
                    Console.Write($"\u001b[1;36m{id.Name}\u001b[0m");
                    break;
                case LiteralExpression lit when lit.Value == null:
                    Console.Write("\u001b[1;31mnull\u001b[0m");
                    break;
                case LiteralExpression lit:
                    string valueStr = lit.Value switch
                    {
                        string s => $"\"{s}\"",
                        int i => i.ToString(),
                        _ => lit.Value?.ToString() ?? "null"
                    };
                    Console.Write($"\u001b[1;33m{valueStr}\u001b[0m");
                    break;
                case BinaryExpression bin:
                    PrintInlineExpr(bin.Left);
                    Console.Write($" \u001b[33m{bin.Operator}\u001b[0m ");
                    PrintInlineExpr(bin.Right);
                    break;
                case CallExpression call:
                    PrintInlineExpr(call.Callee);
                    Console.Write("(");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) Console.Write(", ");
                        PrintInlineExpr(call.Arguments[i]);
                    }
                    Console.Write(")");
                    break;
                case MemberExpression mem:
                    PrintInlineExpr(mem.Object);
                    Console.Write($".\u001b[1;35m{mem.Property}\u001b[0m");
                    break;
                case IndexExpression idx:
                    PrintInlineExpr(idx.Target);
                    Console.Write("[");
                    PrintInlineExpr(idx.Index);
                    Console.Write("]");
                    break;
                default:
                    Console.Write($"\u001b[37m{expr.GetType().Name.Replace("Expression", "")}\u001b[0m");
                    break;
            }
        }
    }
}