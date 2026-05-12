using System;
using System.IO;
using System.Threading.Tasks;
using Contract.Compiler.Parsing;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.CodeGen;

namespace Contract.Cli
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: Contract.Cli <file.ct>");
                return;
            }

            string filePath = args[0];

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File not found: {filePath}");
                return;
            }

            try
            {
                string source = File.ReadAllText(filePath);

                // Lexical analysis
                var diagnostics = new DiagnosticBag();
                var lexer = new Lexer(source, diagnostics);
                var allTokens = lexer.Tokenize().ToList();
                var tokens = allTokens.Where(t => t.Type != TokenType.EOF).ToList();

                Console.WriteLine("Tokens:");
                foreach (var token in tokens)
                {
                    Console.WriteLine($"  {token}");
                }

                Console.WriteLine("Starting parsing...");
                
                // Parsing with diagnostics and timeout
                var parser = new Parser(allTokens, diagnostics);
                Contract.Compiler.AST.Program program;
                
                var parsingTask = Task.Run(() => parser.Parse());
                if (parsingTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    program = parsingTask.Result;
                    Console.WriteLine("Parsing completed successfully");
                }
                else
                {
                    Console.WriteLine("Parsing timed out after 5 seconds");
                    return;
                }

                Console.WriteLine($"\nParsed program with {program.Contracts.Count} contracts and {program.Functions.Count} functions");

                // Report diagnostics
                if (diagnostics.Diagnostics.Count > 0)
                {
                    Console.WriteLine("\nDiagnostics:");
                    diagnostics.ReportToConsole();
                }

                Console.WriteLine("\nParsed AST:");
                PrintAstSimple(program);

                // Code generation
                Console.WriteLine("\nGenerating bytecode...");
                var codeGenerator = new IRCodeGenerator(diagnostics);
                codeGenerator.Generate(program);
                
                string outputPath = Path.ChangeExtension(filePath, ".cil");
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
            Console.WriteLine($"{prefix}{node.GetType().Name} at ({node.Line}:{node.Column})");

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
                Console.WriteLine($"{prefix}  Name: {f.Name}");
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
        }
        static void PrintInlineExpr(Expression expr)
        {
            switch (expr)
            {
                case IdentifierExpression id:
                    Console.Write($"\u001b[1;36m{id.Name}\u001b[0m");
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
                default:
                    Console.Write($"\u001b[37m{expr.GetType().Name.Replace("Expression", "")}\u001b[0m");
                    break;
            }
        }
    }
}
