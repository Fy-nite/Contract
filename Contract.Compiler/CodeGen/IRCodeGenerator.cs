using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Semantics;
using Contract.Compiler.StandardLibrary;
using ObjectIR.Core.AST;
using ObjectIR.Core.Builder;
using ObjectIR.Core.Serialization;
using AccessModifier = Contract.Compiler.AST.AccessModifier;

namespace Contract.Compiler.CodeGen;

public class IRCodeGenerator
{
    private readonly DiagnosticBag _diagnostics;
    public static IRBuilder b = null!;
    private string[] _sourceLines = null!;
    private bool _lastIsReturn = false;
    private int _lambdaCounter = 0;
    private Program? _program;

    public IRCodeGenerator(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void WriteToFile(string outputPath)
    {
        if (b == null) return;
        File.WriteAllText(outputPath, b.Build().Serialize().DumpToIRCode());
    }

    public void Generate(Program program)
    {
        _program = program;
        if (program.Contracts.Count == 0) return;
        
        _sourceLines = _diagnostics.SourceCode?.Split('\n') ?? Array.Empty<string>();
        b = new IRBuilder(program.Contracts[0].Name);
        
        foreach (var cls in program.Contracts)
        {
            var classBuilder = b.Class(cls.Name);
            foreach (var member in cls.Members)
            {
                if (member is FunctionDeclaration func)
                    GenerateFunction(classBuilder, func);
            }
            classBuilder.EndClass();
        }

        if (program.Functions.Count > 0)
        {
            var globalClass = b.Class("Global");
            foreach (var func in program.Functions)
            {
                GenerateFunction(globalClass, func);
            }
            globalClass.EndClass();
        }
    }

    private void GenerateFunction(ClassBuilder cb, FunctionDeclaration func)
    {
        _lastIsReturn = false;
        // Guess return type: Main is void, others are int32 by default in v1
        var returnType = func.Name == "Main" ? TypeRef.Void : TypeRef.Int32;
        var mb = cb.Method(func.Name, returnType);
        
        if (func.IsStatic) mb.Static();
        
        mb.Access(func.Access switch {
            AccessModifier.Public => ObjectIR.Core.AST.AccessModifier.Public,
            AccessModifier.Private => ObjectIR.Core.AST.AccessModifier.Private,
            _ => ObjectIR.Core.AST.AccessModifier.Public
        });

        var paramMap = new Dictionary<string, int>();
        for (int i = 0; i < func.Parameters.Count; i++)
        {
            var p = func.Parameters[i];
            mb.Parameter(p.Name, MapType(p.Type));
            paramMap[p.Name] = i;
        }

        var ib = mb.Body();
        if (func.Body != null)
        {
            GenerateStatement(ib, func.Body, paramMap);
        }

        // Implicit return safety
        if (!_lastIsReturn)
        {
            if (returnType == TypeRef.Void) ib.Ret();
            else ib.LdcI4(0).Ret();
        }

        ib.EndBody().EndMethod();
    }

    private void GenerateStatement(InstructionBuilder ib, Contract.Compiler.AST.Statement stmt, Dictionary<string, int> paramMap)
    {
        string? source = GetSourceLine(stmt.Line);
        ib.SetLocation(stmt.Line, stmt.Column, source);
        _lastIsReturn = false;

        switch (stmt)
        {
            case Contract.Compiler.AST.BlockStatement block:
                foreach (var s in block.Statements)
                    GenerateStatement(ib, s, paramMap);
                break;

            case Contract.Compiler.AST.VariableDeclaration v:
                ib.Local(v.Name, MapType(v.Type));
                if (v.Initializer != null)
                {
                    GenerateExpression(ib, v.Initializer, paramMap);
                    ib.Stloc(v.Name);
                }
                break;

            case Contract.Compiler.AST.ExpressionStatement exprStmt:
                GenerateExpression(ib, exprStmt.Expression, paramMap);
                break;

            case Contract.Compiler.AST.IfStatement ifStmt:
                GenerateExpression(ib, ifStmt.Condition, paramMap);
                ib.If("stack", 
                    then => GenerateStatement(then, ifStmt.ThenBranch, paramMap),
                    ifStmt.ElseBranch != null ? els => GenerateStatement(els, ifStmt.ElseBranch, paramMap) : null
                );
                break;

            case Contract.Compiler.AST.WhileStatement whileStmt:
                GenerateExpression(ib, whileStmt.Condition, paramMap);
                ib.While("stack", body => GenerateStatement(body, whileStmt.Body, paramMap));
                break;

            case Contract.Compiler.AST.ReturnStatement ret:
                if (ret.Value != null)
                    GenerateExpression(ib, ret.Value, paramMap);
                ib.Ret();
                _lastIsReturn = true;
                break;

            case Contract.Compiler.AST.SwitchStatement switchStmt:
                GenerateExpression(ib, switchStmt.Expression, paramMap);
                ib.Switch("stack", cases => {
                    foreach (var c in switchStmt.Cases)
                    {
                        cases.Case(c.Value, body => {
                            foreach (var s in c.Statements)
                                GenerateStatement(body, s, paramMap);
                        });
                    }
                });
                break;
        }
    }

    private bool IsComparison(string op) => op is "==" or "!=" or ">" or ">=" or "<" or "<=";

    private void GenerateExpression(InstructionBuilder ib, Expression expr, Dictionary<string, int> paramMap)
    {
        string? source = GetSourceLine(expr.Line);
        ib.SetLocation(expr.Line, expr.Column, source);

        switch (expr)
        {
            case LiteralExpression lit:
                if (lit.Value is int i) ib.LdcI4(i);
                else if (lit.Value is string s) ib.Ldstr(s);
                else if (lit.Value == null) ib.Ldnull();
                break;

            case IdentifierExpression id:
                if (paramMap.TryGetValue(id.Name, out int argIdx)) ib.Ldarg(argIdx);
                else ib.Ldloc(id.Name);
                break;

            case BinaryExpression bin:
                if (bin.Operator == "=")
                {
                    if (bin.Left is IndexExpression indexTarget)
                    {
                        GenerateExpression(ib, indexTarget.Target, paramMap);
                        GenerateExpression(ib, indexTarget.Index, paramMap);
                        GenerateExpression(ib, bin.Right, paramMap);
                        ib.Stelem();
                        return;
                    }

                    GenerateExpression(ib, bin.Right, paramMap);
                    if (bin.Left is IdentifierExpression target)
                    {
                        if (paramMap.TryGetValue(target.Name, out int idx)) ib.Starg(idx);
                        else ib.Stloc(target.Name);
                    }
                    return;
                }

                GenerateExpression(ib, bin.Left, paramMap);
                GenerateExpression(ib, bin.Right, paramMap);
                switch (bin.Operator)
                {
                    case "+": ib.Add(); break;
                    case "-": ib.Sub(); break;
                    case "*": ib.Mul(); break;
                    case "/": ib.Div(); break;
                    case "%": ib.Rem(); break;
                    case "==": ib.Ceq(); break;
                    case "!=": ib.Cne(); break;
                    case ">": ib.Cgt(); break;
                    case ">=": ib.Cge(); break;
                    case "<": ib.Clt(); break;
                    case "<=": ib.Cle(); break;
                    case "&&": ib.And(); break;
                    case "||": ib.Or(); break;
                }
                break;

            case CallExpression call:
                // Push arguments to stack
                foreach (var arg in call.Arguments)
                    GenerateExpression(ib, arg, paramMap);

                if (call.Symbol is ExternalMethod em)
                {
                    var target = new MethodReference(
                        em.ClassName, 
                        em.MethodName, 
                        MapTypeFromSystemType(em.Info.ReturnType),
                        em.Info.GetParameters().Select(p => MapTypeFromSystemType(p.ParameterType)).ToList()
                    );
                    ib.Call(target);
                }
                else if (call.Callee is IdentifierExpression ident)
                {
                    // Internal function call
                    var target = new MethodReference("this", ident.Name, TypeRef.Int32, call.Arguments.Select(_ => TypeRef.Int32).ToList());
                    ib.Call(target);
                }
                break;
                
            case MemberExpression mem:
                GenerateExpression(ib, mem.Object, paramMap);
                break;

            case IndexExpression indexExpr:
                GenerateExpression(ib, indexExpr.Target, paramMap);
                GenerateExpression(ib, indexExpr.Index, paramMap);
                ib.Ldelem();
                break;

            case LambdaExpression lambda:
                string name = $"__lambda_{_lambdaCounter++}";
                var func = new FunctionDeclaration(name, lambda.Line, lambda.Column)
                {
                    IsStatic = true
                };
                foreach (var param in lambda.Parameters)
                {
                    func.Parameters.Add(new Parameter(param, "Int", lambda.Line, lambda.Column));
                }
                var block = new Contract.Compiler.AST.BlockStatement(lambda.Line, lambda.Column);
                block.Statements.Add(new ReturnStatement(lambda.Body, lambda.Line, lambda.Column));
                func.Body = block;
                
                _program!.Functions.Add(func); // Add to program directly
                
                // Return reference to the new function by its name
                ib.Ldstr(name); 
                break;

            case PipeExpression pipe:
                // Lower: expr |> fn => fn(expr)
                GenerateExpression(ib, pipe.Left, paramMap);
                var callExpr = new CallExpression(pipe.Right, pipe.Line, pipe.Column);
                // We need the result of the left side to be the first argument.
                // Assuming it's a direct function identifier for now, as in tests/success/FunctionalSyntax.ct
                if (pipe.Right is IdentifierExpression)
                {
                    callExpr.Arguments.Add(pipe.Left);
                    GenerateExpression(ib, callExpr, paramMap);
                }
                else
                {
                    throw new NotSupportedException("Piping to complex expressions is not yet supported.");
                }
                break;
        }
    }

    private TypeRef MapType(string type) => string.IsNullOrEmpty(type) ? TypeRef.Int32 : type.ToLower() switch {
        "string" => TypeRef.String,
        "void" => TypeRef.Void,
        "bool" => TypeRef.Bool,
        "int" => TypeRef.Int32,
        _ => new TypeRef(type)
    };

    private TypeRef MapTypeFromSystemType(System.Type t) 
        => t == typeof(string) || t == typeof(object) ? TypeRef.String : (t == typeof(void) ? TypeRef.Void : TypeRef.Int32);

    private string? GetSourceLine(int line) 
        => (line > 0 && line <= _sourceLines.Length) ? _sourceLines[line - 1] : null;
}
