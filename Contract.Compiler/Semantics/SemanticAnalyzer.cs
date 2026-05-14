using System;
using System.Collections.Generic;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler.Semantics
{
    public class SemanticAnalyzer
    {
        private readonly SymbolTable _symbolTable;
        private readonly DiagnosticBag _diagnostics;
        private readonly Stack<Dictionary<string, VariableDeclaration>> _scopes = new();
        private readonly HashSet<string> _definedFunctions = new();

        public SemanticAnalyzer(SymbolTable symbolTable, DiagnosticBag diagnostics)
        {
            _symbolTable = symbolTable;
            _diagnostics = diagnostics;
        }

        public void Analyze(Program program)
        {
            // First pass: collect all function definitions and register contracts/structs
            foreach (var contract in program.Contracts)
            {
                _symbolTable.RegisterUserContract(contract);
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func)
                        _definedFunctions.Add(func.Name);
                }
            }

            // Register structs so we can look up their fields
            foreach (var structDecl in program.Structs)
            {
                _symbolTable.RegisterUserStruct(structDecl);
            }

            foreach (var func in program.Functions)
            {
                _definedFunctions.Add(func.Name);
            }

            // Second pass: detailed analysis
            foreach (var contract in program.Contracts)
            {
                AnalyzeContract(contract);
            }

            foreach (var func in program.Functions)
            {
                AnalyzeFunction(func);
            }
        }

        private void AnalyzeContract(ContractDeclaration contract)
        {
            foreach (var member in contract.Members)
            {
                if (member is FunctionDeclaration func)
                {
                    AnalyzeFunction(func);
                }
            }
        }

        private void AnalyzeFunction(FunctionDeclaration func)
        {
            _scopes.Clear();
            _scopes.Push(new Dictionary<string, VariableDeclaration>());

            foreach (var param in func.Parameters)
            {
                DeclareVariable(param.Name, param.Type, param.Line, param.Column);
            }

            if (func.Body != null)
            {
                AnalyzeStatement(func.Body);
            }

            _scopes.Pop();
        }

        private void DeclareVariable(string name, string type, int line, int column)
        {
            var currentScope = _scopes.Peek();
            if (currentScope.ContainsKey(name))
            {
                _diagnostics.AddError($"Variable '{name}' is already defined in this scope.", line, column);
            }
            else
            {
                // We use a dummy VariableDeclaration for tracking
                currentScope[name] = new VariableDeclaration(name, type, null, line, column);
            }
        }

        private void AnalyzeStatement(Statement statement)
        {
            switch (statement)
            {
                case BlockStatement block:
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    foreach (var stmt in block.Statements)
                        AnalyzeStatement(stmt);
                    _scopes.Pop();
                    break;
                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;
                case VariableDeclaration varDecl:
                    if (string.IsNullOrEmpty(varDecl.Type))
                    {
                        _diagnostics.AddError($"Variable '{varDecl.Name}' must have an explicit type (using 'var name: type').", varDecl.Line, varDecl.Column);
                    }

                    if (varDecl.Initializer != null)
                        AnalyzeExpression(varDecl.Initializer);

                    DeclareVariable(varDecl.Name, varDecl.Type, varDecl.Line, varDecl.Column);
                    break;
                case IfStatement ifStmt:
                    AnalyzeExpression(ifStmt.Condition);
                    AnalyzeStatement(ifStmt.ThenBranch);
                    if (ifStmt.ElseBranch != null)
                        AnalyzeStatement(ifStmt.ElseBranch);
                    break;
                case WhileStatement whileStmt:
                    AnalyzeExpression(whileStmt.Condition);
                    AnalyzeStatement(whileStmt.Body);
                    break;
                case ReturnStatement retStmt:
                    if (retStmt.Value != null)
                        AnalyzeExpression(retStmt.Value);
                    break;
                case SwitchStatement sw:
                    AnalyzeExpression(sw.Expression);
                    foreach (var @case in sw.Cases)
                    {
                        foreach (var stmt in @case.Statements)
                            AnalyzeStatement(stmt);
                    }
                    break;
            }
        }

        private void AnalyzeExpression(Expression expression)
        {
            switch (expression)
            {
                case BinaryExpression bin:
                    AnalyzeExpression(bin.Left);
                    AnalyzeExpression(bin.Right);
                    break;
                case CallExpression call:
                    AnalyzeExpression(call.Callee);
                    foreach (var arg in call.Arguments)
                        AnalyzeExpression(arg);
                    
                    ResolveCall(call);
                    break;
                case ScopedAccessExpression scoped:
                    if (!_symbolTable.GetBoundClasses().Contains(scoped.Module))
                    {
                        _diagnostics.AddError($"Undefined module: '{scoped.Module}'", scoped.Line, scoped.Column);
                    }
                    else if (!_symbolTable.TryGetMethod(scoped.Module, scoped.Member, out _))
                    {
                        _diagnostics.AddError($"Member '{scoped.Member}' not found in module '{scoped.Module}'", scoped.Line, scoped.Column);
                    }
                    break;
                case MemberExpression mem:
                    if (mem.Object is IdentifierExpression objIdent && _symbolTable.GetBoundClasses().Contains(objIdent.Name))
                    {
                        // It's a standard library call, don't analyze the 'object' as a variable
                    }
                    else
                    {
                        AnalyzeExpression(mem.Object);
                    }
                    break;
                case IndexExpression indexExpr:
                    AnalyzeExpression(indexExpr.Target);
                    AnalyzeExpression(indexExpr.Index);
                    break;
                case IdentifierExpression id:
                    if (!IsVariableDefined(id.Name) && !_definedFunctions.Contains(id.Name) && !_symbolTable.GetBoundClasses().Contains(id.Name))
                    {
                        _diagnostics.AddError($"Undefined variable: '{id.Name}'", id.Line, id.Column);
                    }
                    break;
                case LiteralExpression _:
                    break;
            }
        }

        private bool IsVariableDefined(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.ContainsKey(name)) return true;
            }
            return false;
        }

        private void ResolveCall(CallExpression call)
        {
            if (call.Callee is MemberExpression mem && mem.Object is IdentifierExpression objIdent)
            {
                string className = objIdent.Name;
                string methodName = mem.Property;

                if (_symbolTable.TryGetMethod(className, methodName, out var method))
                {
                    call.Symbol = method;
                }
                else
                {
                    _diagnostics.AddError($"External method '{className}.{methodName}' not found.", call.Line, call.Column);
                }
            }
            else if (call.Callee is IdentifierExpression ident)
            {
                if (!_definedFunctions.Contains(ident.Name) && !ident.Name.StartsWith("__lambda_"))
                {
                    _diagnostics.AddError($"Undefined function: '{ident.Name}'", call.Line, call.Column);
                }
            }
        }
    }
}
