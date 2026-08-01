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
        private readonly TypeRegistry _typeRegistry = new();
        private readonly Stack<Dictionary<string, VariableDeclaration>> _scopes = new();
        private readonly HashSet<string> _definedFunctions = new();
        private readonly Dictionary<string, TypeDescriptor> _functionReturnTypes = new();

        public SemanticAnalyzer(SymbolTable symbolTable, DiagnosticBag diagnostics)
        {
            _symbolTable = symbolTable;
            _diagnostics = diagnostics;
        }

        public void Analyze(Program program)
        {
            // Register custom types
            foreach (var typesDecl in program.Types)
            {
                foreach (var customType in typesDecl.Definitions)
                {
                    _typeRegistry.RegisterCustomType(customType.Name);
                }
            }
            
            // Register contracts as valid types
            foreach (var contract in program.Contracts)
            {
                _typeRegistry.RegisterCustomType(contract.Name);
            }

            // Register structs as valid types
            foreach (var structDecl in program.Structs)
            {
                _typeRegistry.RegisterCustomType(structDecl.Name);
            }
            
            // Register structs defined inside contracts
            foreach (var contract in program.Contracts)
            {
                foreach (var member in contract.Members)
                {
                    if (member is StructDeclaration structDecl)
                    {
                        _typeRegistry.RegisterCustomType(structDecl.Name);
                    }
                }
            }
            
            // First pass: collect all function definitions and register contracts/structs
            foreach (var contract in program.Contracts)
            {
                _symbolTable.RegisterUserContract(contract);
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func)
                    {
                        _definedFunctions.Add(func.Name);
                        if (func.ReturnType != null)
                            _functionReturnTypes[func.Name] = func.ReturnType;
                    }
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
                if (func.ReturnType != null)
                    _functionReturnTypes[func.Name] = func.ReturnType;
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
            foreach (var ctor in contract.Constructors)
            {
                AnalyzeConstructor(ctor);
            }

            foreach (var member in contract.Members)
            {
                if (member is FunctionDeclaration func)
                {
                    AnalyzeFunction(func);
                }
            }
        }

        private void AnalyzeConstructor(ConstructorDeclaration ctor)
        {
            _scopes.Clear();
            _scopes.Push(new Dictionary<string, VariableDeclaration>());

            foreach (var param in ctor.Parameters)
            {
                if (!param.Type.IsEmpty && !_typeRegistry.IsValid(param.Type))
                {
                    _diagnostics.AddError($"Unknown type '{param.Type}' for parameter '{param.Name}'", param.Line, param.Column);
                }
                DeclareVariable(param.Name, param.Type, param.Line, param.Column);
            }

            if (ctor.Body != null)
            {
                AnalyzeStatement(ctor.Body);
            }

            _scopes.Pop();
        }

        private void AnalyzeFunction(FunctionDeclaration func)
        {
            _scopes.Clear();
            _scopes.Push(new Dictionary<string, VariableDeclaration>());

            foreach (var param in func.Parameters)
            {
                if (!_typeRegistry.IsValid(param.Type))
                {
                    _diagnostics.AddError($"Unknown type '{param.Type}' for parameter '{param.Name}'", param.Line, param.Column);
                }
                DeclareVariable(param.Name, param.Type, param.Line, param.Column);
            }

            if (func.ReturnType != null && !_typeRegistry.IsValid(func.ReturnType))
            {
                _diagnostics.AddError($"Unknown return type '{func.ReturnType}' for function '{func.Name}'", func.Line, func.Column);
            }

            if (func.Body != null)
            {
                AnalyzeStatement(func.Body);
            }

            _scopes.Pop();
        }

        private void DeclareVariable(string name, TypeDescriptor type, int line, int column)
        {
            if (!_typeRegistry.IsValid(type))
            {
                _diagnostics.AddError($"Unknown type '{type}'", line, column);
            }

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
                    // Analyze the initializer first so calls get symbol-linked and
                    // their return types are available for inference.
                    if (varDecl.Initializer != null)
                        AnalyzeExpression(varDecl.Initializer);

                    if (varDecl.Type.IsEmpty)
                    {
                        if (varDecl.Initializer != null)
                        {
                            varDecl.Type = InferType(varDecl.Initializer) ?? TypeDescriptor.Empty;
                        }
                    }

                    if (varDecl.Type.IsEmpty)
                    {
                        _diagnostics.AddError($"Variable '{varDecl.Name}' must have an explicit type (using 'var name: type').", varDecl.Line, varDecl.Column);
                    }

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
                case ForStatement forStmt:
                    // The loop variable is scoped to the loop itself (like C),
                    // so two sequential 'for (var i = ...)' loops don't collide.
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    if (forStmt.Initializer != null)
                        AnalyzeStatement(forStmt.Initializer);
                    if (forStmt.Condition != null)
                        AnalyzeExpression(forStmt.Condition);
                    AnalyzeStatement(forStmt.Body);
                    if (forStmt.Update != null)
                        AnalyzeExpression(forStmt.Update);
                    _scopes.Pop();
                    break;
                case BreakStatement:
                case ContinueStatement:
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
                    bin.ResolvedType = InferType(bin);
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
                case UnaryExpression unary:
                    AnalyzeExpression(unary.Operand);
                    break;
                case ArrayLiteralExpression arrLit:
                    foreach (var element in arrLit.Elements)
                        AnalyzeExpression(element);
                    break;
                case NewExpression newExpr:
                    if (newExpr.Size != null)
                    {
                        AnalyzeExpression(newExpr.Size);
                        if (!_typeRegistry.IsValidType(newExpr.TypeName))
                        {
                            _diagnostics.AddError($"Unknown type '{newExpr.TypeName}'", newExpr.Line, newExpr.Column);
                        }
                    }
                    else if (!_typeRegistry.IsValidType(newExpr.TypeName))
                    {
                        _diagnostics.AddError($"Unknown type '{newExpr.TypeName}'", newExpr.Line, newExpr.Column);
                    }
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

        private TypeDescriptor? InferType(Expression expression)
        {
            switch (expression)
            {
                case LiteralExpression lit:
                    return lit.Value switch
                    {
                        int => new TypeDescriptor.Named("int"),
                        string => new TypeDescriptor.Named("string"),
                        bool => new TypeDescriptor.Named("bool"),
                        double => new TypeDescriptor.Named("double"),
                        _ => null
                    };
                case IdentifierExpression id:
                    return FindVariableType(id.Name);
                case UnaryExpression unary:
                    return InferType(unary.Operand);
                case NewExpression newExpr:
                    return newExpr.Size != null
                        ? new TypeDescriptor.ArrayOf(new TypeDescriptor.Named(newExpr.TypeName))
                        : new TypeDescriptor.Named(newExpr.TypeName);
                case ArrayLiteralExpression arrLit:
                    if (arrLit.Elements.Count == 0) return null;
                    var element = InferType(arrLit.Elements[0]);
                    if (element == null) return null;
                    foreach (var e in arrLit.Elements)
                    {
                        if (!Equals(InferType(e), element)) return null;
                    }
                    arrLit.ElementType = element;
                    return new TypeDescriptor.ArrayOf(element);
                case LambdaExpression lambda:
                    var lambdaParams = lambda.Parameters
                        .Select(_ => new TypeDescriptor.Named("int"))
                        .ToList();
                    var lambdaReturn = InferType(lambda.Body) ?? new TypeDescriptor.Named("int");
                    return new TypeDescriptor.Function(lambdaParams, lambdaReturn);
                case CallExpression call:
                    if (call.Symbol is ExternalMethod em)
                    {
                        return MapSystemTypeToLanguageType(em.Info.ReturnType);
                    }
                    if (call.Callee is IdentifierExpression calleeIdent &&
                        _functionReturnTypes.TryGetValue(calleeIdent.Name, out var funcReturnType))
                    {
                        return funcReturnType;
                    }
                    return new TypeDescriptor.Named("int");
                case BinaryExpression bin:
                    if (bin.Operator is "+" or "+=")
                    {
                        var leftType = InferType(bin.Left);
                        var rightType = InferType(bin.Right);
                        if (leftType?.IsString == true || rightType?.IsString == true)
                            return new TypeDescriptor.Named("string");
                    }
                    return new TypeDescriptor.Named("int");
                default:
                    // Pipes and other expressions default to int in v1.
                    return new TypeDescriptor.Named("int");
            }
        }

        private TypeDescriptor? FindVariableType(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.TryGetValue(name, out var decl))
                {
                    return decl.Type.IsEmpty ? null : decl.Type;
                }
            }
            return null;
        }

        private static TypeDescriptor MapSystemTypeToLanguageType(System.Type t)
        {
            if (t.IsArray)
            {
                var elementType = t.GetElementType();
                return elementType != null
                    ? new TypeDescriptor.ArrayOf(MapSystemTypeToLanguageType(elementType))
                    : new TypeDescriptor.ArrayOf(new TypeDescriptor.Named("int"));
            }
            if (t == typeof(string)) return new TypeDescriptor.Named("string");
            if (t == typeof(object)) return new TypeDescriptor.Named("object");
            if (t == typeof(bool)) return new TypeDescriptor.Named("bool");
            if (t == typeof(double)) return new TypeDescriptor.Named("double");
            if (t == typeof(float)) return new TypeDescriptor.Named("float");
            if (t == typeof(long)) return new TypeDescriptor.Named("int64");
            if (t == typeof(void)) return new TypeDescriptor.Named("void");
            return new TypeDescriptor.Named("int");
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
                // Allow calling identifiers that hold lambdas (or are defined functions).
                if (!_definedFunctions.Contains(ident.Name) && !ident.Name.StartsWith("__lambda_") && !IsVariableDefined(ident.Name))
                {
                    _diagnostics.AddError($"Undefined function: '{ident.Name}'", call.Line, call.Column);
                }
            }
        }
    }
}
