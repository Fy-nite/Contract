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
        private readonly List<ContractDeclaration> _contractsWithFields = new();
        private readonly Dictionary<string, ContractDeclaration> _contractsByName =
            new(StringComparer.OrdinalIgnoreCase);

        public SemanticAnalyzer(SymbolTable symbolTable, DiagnosticBag diagnostics)
        {
            _symbolTable = symbolTable;
            _diagnostics = diagnostics;
        }

        public void Analyze(Program program)
        {
            // Register namespace imports so short module names resolve.
            foreach (var ns in program.NamespaceImports)
                _symbolTable.ImportNamespace(ns);

            // Register contracts as valid types
            foreach (var contract in program.Contracts)
            {
                _typeRegistry.RegisterCustomType(contract.Name);
                if (contract.Fields.Count > 0)
                    _contractsWithFields.Add(contract);
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

            // Validate every field declaration's type (contracts, structs, and
            // structs nested inside contracts).
            ValidateFieldTypes(program);
            
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

            // Validate inheritance (base types, cycles) and mark attribute types,
            // then validate every attribute application against those types.
            ValidateInheritanceAndAttributes(program);

            // Validate <NativeBinding("Module")> contracts: the module must be
            // bound, methods must be empty-bodied declarations, and every method
            // name must exist on the host module.
            ValidateNativeBindings(program);

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

        // ── Field types ──────────────────────────────────────────────

        /// <summary>
        /// Validates every field declaration in the program: the declared type
        /// must be known/valid, must not be <c>void</c>/<c>null</c>, and field
        /// names must be unique within the declaring type.
        /// </summary>
        private void ValidateFieldTypes(Program program)
        {
            foreach (var contract in program.Contracts)
            {
                ValidateFields(contract.Fields, "contract");
                foreach (var member in contract.Members)
                {
                    if (member is StructDeclaration nestedStruct)
                        ValidateFields(nestedStruct.Fields, "struct");
                }
            }

            foreach (var structDecl in program.Structs)
            {
                ValidateFields(structDecl.Fields, "struct");
            }
        }

        private void ValidateFields(IReadOnlyList<StructField> fields, string ownerKind)
        {
            var seen = new HashSet<string>();
            foreach (var field in fields)
            {
                if (!seen.Add(field.Name))
                {
                    _diagnostics.AddError($"Field '{field.Name}' is already declared in this {ownerKind}", field.Line, field.Column);
                }

                if (field.Type.IsEmpty)
                {
                    _diagnostics.AddError($"Field '{field.Name}' must have a type", field.Line, field.Column);
                    continue;
                }

                if (field.Type is TypeDescriptor.Named n
                    && (string.Equals(n.Name, "void", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Name, "null", StringComparison.OrdinalIgnoreCase)))
                {
                    _diagnostics.AddError($"Field '{field.Name}' cannot have type '{n.Name}'", field.Line, field.Column);
                    continue;
                }

                if (!_typeRegistry.IsValid(field.Type))
                {
                    _diagnostics.AddError($"Unknown type '{field.Type}' for field '{field.Name}'", field.Line, field.Column);
                }
            }
        }

        // ── Attributes & inheritance ───────────────────────────────────

        /// <summary>
        /// Validates base types and inheritance cycles across all contracts,
        /// marks contracts that (transitively) inherit from the built-in
        /// <c>Attribute</c> type, then validates every attribute application.
        /// </summary>
        private void ValidateInheritanceAndAttributes(Program program)
        {
            _contractsByName.Clear();
            foreach (var c in program.Contracts)
                _contractsByName[c.Name] = c;

            foreach (var contract in program.Contracts)
            {
                var chain = new List<ContractDeclaration>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var current = contract;
                bool reachesAttribute = false;

                while (current.BaseTypeName != null)
                {
                    if (!seen.Add(current.Name))
                    {
                        _diagnostics.AddError($"Inheritance cycle involving contract '{current.Name}'", current.Line, current.Column);
                        break;
                    }

                    chain.Add(current);
                    var baseName = current.BaseTypeName;

                    if (baseName.Equals("Attribute", StringComparison.OrdinalIgnoreCase))
                    {
                        reachesAttribute = true;
                        break;
                    }

                    if (!_contractsByName.TryGetValue(baseName, out var baseContract))
                    {
                        if (!_typeRegistry.IsValidType(baseName))
                        {
                            _diagnostics.AddError($"Unknown base type '{baseName}' for contract '{current.Name}'", current.Line, current.Column);
                        }
                        break;
                    }
                    current = baseContract;
                }

                if (reachesAttribute)
                {
                    contract.IsAttributeType = true;
                    foreach (var c in chain)
                        c.IsAttributeType = true;
                }
            }

            foreach (var contract in program.Contracts)
            {
                ValidateAttributes(contract.Attributes, "contract", _contractsByName);
                foreach (var ctor in contract.Constructors)
                    ValidateAttributes(ctor.Attributes, "constructor", _contractsByName);
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func)
                        ValidateAttributes(func.Attributes, "function", _contractsByName);
                    else if (member is StructDeclaration structDecl)
                        ValidateAttributes(structDecl.Attributes, "struct", _contractsByName);
                }
            }

            foreach (var structDecl in program.Structs)
                ValidateAttributes(structDecl.Attributes, "struct", _contractsByName);

            foreach (var func in program.Functions)
                ValidateAttributes(func.Attributes, "function", _contractsByName);
        }

        private void ValidateAttributes(List<AttributeUsage> attributes, string targetKind, Dictionary<string, ContractDeclaration> contractsByName)
        {
            foreach (var attr in attributes)
            {
                // Built-in attribute: <NativeBinding("ModuleName")> — no user
                // declaration needed. Only valid on contracts; argument must be
                // the name of a host binding module.
                if (attr.Name.Equals("NativeBinding", StringComparison.OrdinalIgnoreCase))
                {
                    if (targetKind != "contract")
                    {
                        _diagnostics.AddError($"NativeBinding attribute is only valid on contracts", attr.Line, attr.Column);
                    }
                    else if (attr.Arguments.Count != 1)
                    {
                        _diagnostics.AddError($"NativeBinding expects exactly 1 argument (the binding module name), got {attr.Arguments.Count}", attr.Line, attr.Column);
                    }
                    continue;
                }

                if (!contractsByName.TryGetValue(attr.Name, out var attrContract))
                {
                    _diagnostics.AddError($"Unknown attribute '{attr.Name}'", attr.Line, attr.Column);
                    continue;
                }

                if (!attrContract.IsAttributeType)
                {
                    _diagnostics.AddError($"'{attr.Name}' is not an attribute type — it must inherit from Attribute", attr.Line, attr.Column);
                    continue;
                }

                // Argument count must match one of the attribute type's constructors.
                if (attrContract.Constructors.Count > 0)
                {
                    bool matches = attrContract.Constructors.Any(c => c.Parameters.Count == attr.Arguments.Count);
                    if (!matches)
                    {
                        var expected = string.Join(" or ", attrContract.Constructors.Select(c => c.Parameters.Count.ToString()));
                        _diagnostics.AddError($"Attribute '{attr.Name}' expects {expected} argument(s), got {attr.Arguments.Count}", attr.Line, attr.Column);
                    }
                }
            }
        }

        /// <summary>
        /// Validates <c>&lt;NativeBinding("Module")&gt;</c> contracts: a native
        /// binding is a pure facade — no fields, no constructors, and every
        /// member function is an empty-bodied declaration that dispatches to
        /// <c>Module.Method</c> at runtime. The module must be a registered
        /// host binding and each declared method must exist on it.
        /// </summary>
        private void ValidateNativeBindings(Program program)
        {
            foreach (var contract in program.Contracts)
            {
                var attr = contract.Attributes.FirstOrDefault(a =>
                    a.Name.Equals("NativeBinding", StringComparison.OrdinalIgnoreCase));
                if (attr == null) continue;

                if (attr.Arguments.Count != 1) continue; // arity already reported

                string bindingName = attr.Arguments[0].Trim();
                if (bindingName.Length >= 2 && bindingName[0] == '"' && bindingName[^1] == '"')
                    bindingName = bindingName[1..^1];

                if (!_symbolTable.IsBoundModule(bindingName))
                {
                    _diagnostics.AddError(
                        $"NativeBinding module '{bindingName}' is not a registered binding (check bindingAssemblies)",
                        attr.Line, attr.Column);
                    continue;
                }

                contract.NativeBindingName = bindingName;

                // The parser marks non-static members as instance methods only
                // for contracts with fields; native-bound contracts are pure
                // facades (no fields), so mark their methods as instance here.
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration f && !f.IsStatic)
                        f.IsInstance = true;
                }

                if (contract.Fields.Count > 0)
                    _diagnostics.AddError($"Native-bound contract '{contract.Name}' cannot have fields", contract.Line, contract.Column);
                if (contract.Constructors.Count > 0)
                    _diagnostics.AddError($"Native-bound contract '{contract.Name}' cannot declare constructors — 'new' maps to '{bindingName}.Create'", contract.Line, contract.Column);

                foreach (var member in contract.Members)
                {
                    if (member is not FunctionDeclaration func) continue;
                    if (func.Body != null && func.Body.Statements.Count > 0)
                        _diagnostics.AddError($"Native-bound method '{contract.Name}.{func.Name}' must have an empty body", func.Line, func.Column);
                    if (!_symbolTable.TryGetMethod(bindingName, func.Name, out _))
                        _diagnostics.AddError($"Native binding '{bindingName}' has no method named '{func.Name}'", func.Line, func.Column);
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
                    // Module::member — an stdlib module, or a static member on a
                    // user contract (Contract::staticMethod works like the dot form).
                    if (_symbolTable.IsBoundModule(scoped.Module))
                    {
                        if (!_symbolTable.TryGetMethod(scoped.Module, scoped.Member, out _))
                        {
                            _diagnostics.AddError($"Member '{scoped.Member}' not found in module '{scoped.Module}'", scoped.Line, scoped.Column);
                        }
                    }
                    else if (_symbolTable.IsUserContract(scoped.Module))
                    {
                        if (!_symbolTable.TryGetMethod(scoped.Module, scoped.Member, out var cm)
                            || (cm is FunctionDeclaration cf && !cf.IsStatic))
                        {
                            _diagnostics.AddError($"Member '{scoped.Member}' not found in contract '{scoped.Module}'", scoped.Line, scoped.Column);
                        }
                    }
                    else
                    {
                        _diagnostics.AddError($"Undefined module: '{scoped.Module}'", scoped.Line, scoped.Column);
                    }
                    break;
                case MemberExpression mem:
                    if (IsModuleAccessChain(mem))
                    {
                        // It's a standard library call, don't analyze the spine as variables
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
                    if (id.Name == "this") break; // instance context
                    if (!IsVariableDefined(id.Name)
                        && !_definedFunctions.Contains(id.Name)
                        && !_symbolTable.GetBoundClasses().Contains(id.Name)
                        && !_symbolTable.IsBoundModule(id.Name)
                        && !IsContractField(id.Name))
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

                    // Native-bound contracts construct through the host module's
                    // Create method: new Window() → binding.Create().
                    if (_contractsByName.TryGetValue(newExpr.TypeName, out var nbContract)
                        && nbContract.NativeBindingName != null
                        && !_symbolTable.TryGetMethod(nbContract.NativeBindingName, "Create", out _))
                    {
                        _diagnostics.AddError($"Native binding '{nbContract.NativeBindingName}' has no method named 'Create' (required for 'new {newExpr.TypeName}')", newExpr.Line, newExpr.Column);
                    }
                    break;
                case LambdaExpression lambda:
                    // Validate annotated param types and analyze the body so
                    // calls inside lambdas get symbol-linked and errors surface.
                    for (int i = 0; i < lambda.Parameters.Count; i++)
                    {
                        string pt = i < lambda.ParameterTypes.Count ? lambda.ParameterTypes[i] : "";
                        if (!string.IsNullOrEmpty(pt) && !_typeRegistry.IsValidType(pt))
                        {
                            _diagnostics.AddError($"Unknown type '{pt}' for lambda parameter '{lambda.Parameters[i]}'", lambda.Line, lambda.Column);
                        }
                    }
                    _scopes.Push(new Dictionary<string, VariableDeclaration>());
                    foreach (var p in lambda.Parameters)
                        DeclareVariable(p, new TypeDescriptor.Named("int"), lambda.Line, lambda.Column);
                    if (lambda.BlockBody != null)
                    {
                        foreach (var stmt in lambda.BlockBody.Statements)
                            AnalyzeStatement(stmt);
                    }
                    else if (lambda.Body != null)
                    {
                        AnalyzeExpression(lambda.Body);
                    }
                    _scopes.Pop();
                    break;
            }
        }

        private static TypeDescriptor? FindBlockReturnType(Contract.Compiler.AST.BlockStatement block)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt is ReturnStatement ret && ret.Value != null)
                {
                    var t = ret.Value switch
                    {
                        LiteralExpression lit => lit.Value switch
                        {
                            int => "int",
                            string => "string",
                            bool => "bool",
                            double => "double",
                            _ => null
                        },
                        _ => "int" // default for other expression forms
                    };
                    if (t != null) return TypeDescriptor.Parse(t);
                }
            }
            return null;
        }

        private bool IsVariableDefined(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.ContainsKey(name)) return true;
            }
            return false;
        }

        private bool IsContractField(string name)
        {
            // True when a contract has a field with this name (bare field access
            // in an instance method).
            foreach (var contract in _contractsWithFields)
            {
                if (contract.Fields.Any(f => f.Name == name)) return true;
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
                        .Select((p, i) => (TypeDescriptor)(i < lambda.ParameterTypes.Count && !string.IsNullOrEmpty(lambda.ParameterTypes[i])
                            ? TypeDescriptor.Parse(lambda.ParameterTypes[i])
                            : new TypeDescriptor.Named("int")))
                        .ToList();
                    TypeDescriptor? lambdaReturn;
                    if (lambda.BlockBody != null)
                    {
                        // Block bodies: default to int unless we can find a return.
                        lambdaReturn = FindBlockReturnType(lambda.BlockBody);
                    }
                    else
                    {
                        lambdaReturn = InferType(lambda.Body);
                    }
                    return new TypeDescriptor.Function(lambdaParams, lambdaReturn ?? new TypeDescriptor.Named("int"));
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
                    // f()(args): the result of invoking a delegate is the
                    // function type's return.
                    if (call.Callee is CallExpression innerCall)
                    {
                        var innerFn = GetCallResultFunctionType(innerCall);
                        if (innerFn != null)
                        {
                            return innerFn.Return;
                        }
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
            if (call.Callee is MemberExpression mem)
            {
                // Collect the dotted base path: IO.Println → "IO",
                // ObjektRT.Stdlib.System.IO.Println → "ObjektRT.Stdlib.System.IO".
                if (TryGetModuleAccessPath(mem, out var moduleName))
                {
                    string methodName = mem.Property;

                    if (_symbolTable.TryGetMethod(moduleName, methodName, out var moduleMethod))
                    {
                        call.Symbol = moduleMethod;
                        return;
                    }

                    if (moduleName.Contains('.'))
                    {
                        // A dotted chain that isn't a bound module — nothing else to try.
                        _diagnostics.AddError($"External method '{moduleName}.{methodName}' not found.", call.Line, call.Column);
                        return;
                    }

                    // Single-segment base: try the existing instance-method resolution
                    // (c.method() where c is a variable of a contract type).
                    string className = moduleName;

                    // User-contract instance method call: c.method() where c is a
                    // variable whose type is a contract with that method.
                    if (IsVariableDefined(className))
                    {
                        var varType = FindVariableType(className);
                        if (varType is TypeDescriptor.Named n
                            && _contractsByName.TryGetValue(n.Name, out var contract))
                        {
                            var member = contract.Members
                                .OfType<FunctionDeclaration>()
                                .FirstOrDefault(f => f.Name == methodName && f.IsInstance);
                            if (member != null)
                            {
                                call.Symbol = member;
                                return;
                            }
                        }
                        // Fall through to error below.
                    }

                    _diagnostics.AddError($"External method '{className}.{methodName}' not found.", call.Line, call.Column);
                }
            }
            else if (call.Callee is ScopedAccessExpression scoped)
            {
                // Module::Method(...) — external stdlib or a static function on a contract.
                if (_symbolTable.TryGetMethod(scoped.Module, scoped.Member, out var scopedMethod))
                {
                    call.Symbol = scopedMethod;
                    return;
                }
                // (The undefined-module/member case is already reported by
                // AnalyzeExpression's ScopedAccessExpression branch.)
            }
            else if (call.Callee is IdentifierExpression ident)
            {
                // Allow calling identifiers that hold lambdas (or are defined functions).
                if (!_definedFunctions.Contains(ident.Name) && !ident.Name.StartsWith("__lambda_") && !IsVariableDefined(ident.Name))
                {
                    _diagnostics.AddError($"Undefined function: '{ident.Name}'", call.Line, call.Column);
                }
            }
            else if (call.Callee is CallExpression innerCall)
            {
                // f()(args) — calling the result of a call (a delegate/closure).
                var fnType = GetCallResultFunctionType(innerCall);
                if (fnType == null)
                {
                    _diagnostics.AddError($"Expression is not callable: it does not return a function", call.Line, call.Column);
                    return;
                }
                if (call.Arguments.Count != fnType.Parameters.Count)
                {
                    _diagnostics.AddError($"Function expects {fnType.Parameters.Count} argument(s), got {call.Arguments.Count}", call.Line, call.Column);
                    return;
                }
                call.Symbol = innerCall.Symbol;
            }
        }

        /// <summary>
        /// The function type of the delegate that a call produces — the call's
        /// result type when that result is a function type. Used to resolve
        /// <c>f()(args)</c>: the inner call must produce a delegate whose
        /// signature drives the outer invocation.
        /// </summary>
        private TypeDescriptor.Function? GetCallResultFunctionType(CallExpression call)
        {
            // f() where f's declared return type is a function type.
            if (call.Symbol is FunctionDeclaration fd && fd.ReturnType is TypeDescriptor.Function fn)
                return fn;

            // Bare function call by name: f() with f declared elsewhere.
            if (call.Callee is IdentifierExpression id
                && _functionReturnTypes.TryGetValue(id.Name, out var ret)
                && ret is TypeDescriptor.Function fnById)
            {
                return fnById;
            }

            // Calling a function-typed value: the value's type is the function
            // type of the call result (a delegate), so the delegate produced by
            // calling it has the value's function type's return.
            if (call.Callee is IdentifierExpression idVal)
            {
                var vType = FindVariableType(idVal.Name);
                if (vType is TypeDescriptor.Function f1 && f1.Return is TypeDescriptor.Function nested)
                    return nested;
                if (vType is TypeDescriptor.GenericInstance g
                    && g.Name.Equals("Delegate", StringComparison.OrdinalIgnoreCase)
                    && g.Arguments.Count == 1
                    && g.Arguments[0] is TypeDescriptor.Function gf
                    && gf.Return is TypeDescriptor.Function nestedDelegate)
                {
                    return nestedDelegate;
                }
            }

            return null;
        }

        /// <summary>
        /// Collects the dotted identifier spine on the left of a member access:
        /// IO.Println → "IO"; ObjektRT.Stdlib.System.IO.Println → "ObjektRT.Stdlib.System.IO".
        /// The outermost <see cref="MemberExpression.Property"/> is the member, not part of the path.
        /// Returns false when the base is not a pure identifier chain.
        /// </summary>
        private static bool TryGetModuleAccessPath(MemberExpression mem, out string modulePath)
        {
            modulePath = "";
            var segments = new Stack<string>();
            var current = mem.Object;
            while (current is MemberExpression inner)
            {
                segments.Push(inner.Property);
                current = inner.Object;
            }
            if (current is not IdentifierExpression root) return false;
            segments.Push(root.Name);
            modulePath = string.Join(".", segments);
            return true;
        }

        /// <summary>True when the member chain's base is a bound stdlib module (possibly dotted).</summary>
        private bool IsModuleAccessChain(MemberExpression mem)
            => TryGetModuleAccessPath(mem, out var modulePath) && _symbolTable.IsBoundModule(modulePath);
    }
}
