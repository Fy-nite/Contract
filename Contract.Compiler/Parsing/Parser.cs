using System;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Parsing;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler.Parsing
{
    public class Parser
    {
        private readonly List<Token> _tokens;
        private int _current = 0;
        private readonly DiagnosticBag _diagnostics;
        private readonly string? _sourceFile;

        public Parser(IEnumerable<Token> tokens, DiagnosticBag diagnostics, string? sourceFile = null)
        {
            _tokens = new List<Token>(tokens);
            _diagnostics = diagnostics;
            _sourceFile = sourceFile;
        }

        /// <summary>Reports a parse error attributed to this parser's source file (if any).</summary>
        private void AddError(string message, int line, int column)
            => _diagnostics.AddError(message, line, column, _sourceFile);

        public Program Parse()
        {
            var program = new Program(Current.Line, Current.Column);

            while (!IsAtEnd())
            {
                try
                {
                    int startPos = _current;

                    var attributes = ParseAttributes();

                    bool isExported = Match(TokenType.Export);
                    AccessModifier access = AccessModifier.Default;
                    if (Match(TokenType.Public)) access = AccessModifier.Public;
                    else if (Match(TokenType.Private)) access = AccessModifier.Private;
                    else if (Match(TokenType.Protected)) access = AccessModifier.Protected;
                    else if (Match(TokenType.Internal)) access = AccessModifier.Internal;

                    bool isStatic = Match(TokenType.Static);

                    if (Match(TokenType.Import))
                    {
                        if (Match(TokenType.StringLiteral))
                        {
                            // File import: import "path/to/file.ct";
                            string importPath = Previous.Text.Trim('"');
                            program.Imports.Add(importPath);
                        }
                        else
                        {
                            // Namespace import: import ObjektRT.Stdlib.System;
                            Consume(TokenType.Identifier, "Expected namespace name or string literal after 'import'");
                            var ns = new System.Text.StringBuilder(Previous.Text);
                            while (Match(TokenType.Dot))
                            {
                                Consume(TokenType.Identifier, "Expected identifier after '.' in namespace import");
                                ns.Append('.').Append(Previous.Text);
                            }
                            program.NamespaceImports.Add(ns.ToString());
                        }
                        Consume(TokenType.Semicolon, "Expected ';' after import statement");
                    }
                    else if (Match(TokenType.Contract))
                    {
                        var contract = ParseContract();
                        contract.IsExported = isExported;
                        contract.Attributes.AddRange(attributes);
                        program.Contracts.Add(contract);
                    }
                    else if (Match(TokenType.Struct))
                    {
                        var structDecl = ParseStruct();
                        structDecl.IsExported = isExported;
                        structDecl.Attributes.AddRange(attributes);
                        program.Structs.Add(structDecl);
                    }
                    else if (Match(TokenType.Fn))
                    {
                        var func = ParseFunction();
                        func.IsExported = isExported;
                        func.IsStatic = isStatic;
                        func.Access = access;
                        func.Attributes.AddRange(attributes);
                        program.Functions.Add(func);
                    }
                    else if (attributes.Count > 0)
                    {
                        AddError("Attributes must be applied to a contract, struct, or function", attributes[0].Line, attributes[0].Column);
                        Synchronize();
                    }
                    else
                    {
                        AddError($"Unexpected token at top level: {Current.Type} ('{Current.Text}')", Current.Line, Current.Column);
                        Synchronize();
                    }
                    
                    if (_current == startPos && !IsAtEnd())
                    {
                        Advance();
                    }
                }
                catch (Exception ex)
                {
                    AddError($"Parser error: {ex.Message}", Current.Line, Current.Column);
                    Synchronize();
                }
            }

            return program;
        }

        /// <summary>
        /// Parses zero or more attribute applications: <c>&lt;Name(arg1, arg2)&gt;</c>.
        /// Arguments are string / numeric / bool literals or bare identifiers; each is
        /// kept as raw source text (strings retain their quotes, matching the IR's
        /// string-pool convention for annotation arguments).
        /// </summary>
        private List<AttributeUsage> ParseAttributes()
        {
            var attributes = new List<AttributeUsage>();
            while (Match(TokenType.Less))
            {
                int line = Previous.Line;
                int column = Previous.Column;

                Consume(TokenType.Identifier, "Expected attribute name after '<'");
                var usage = new AttributeUsage(Previous.Text, line, column);

                if (Match(TokenType.LParen))
                {
                    if (!Check(TokenType.RParen))
                    {
                        do
                        {
                            if (Match(TokenType.StringLiteral))
                            {
                                // Keep quotes: the IR stores annotation args with quotes.
                                usage.Arguments.Add(Previous.Text);
                            }
                            else if (Match(TokenType.IntLiteral) || Match(TokenType.FloatLiteral) ||
                                     Match(TokenType.True) || Match(TokenType.False) ||
                                     Match(TokenType.Identifier))
                            {
                                usage.Arguments.Add(Previous.Text);
                            }
                            else if (Match(TokenType.Minus))
                            {
                                if (Match(TokenType.IntLiteral) || Match(TokenType.FloatLiteral))
                                    usage.Arguments.Add("-" + Previous.Text);
                                else
                                    AddError("Expected number after '-' in attribute argument", Current.Line, Current.Column);
                            }
                            else
                            {
                                AddError($"Unexpected token in attribute arguments: {Current.Type}", Current.Line, Current.Column);
                                break;
                            }
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RParen, "Expected ')' after attribute arguments");
                }

                Consume(TokenType.Greater, "Expected '>' to close attribute");
                attributes.Add(usage);
            }
            return attributes;
        }

        private StructDeclaration ParseStruct()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected struct name");
            string name = Previous.Text;

            Consume(TokenType.LBrace, "Expected '{' after struct name");

            var structDecl = new StructDeclaration(name, line, column);

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;
                Consume(TokenType.Identifier, "Expected field name");
                string fieldName = Previous.Text;

                Consume(TokenType.Colon, "Expected ':' after field name");
                string fieldType = ParseType();
                Consume(TokenType.Semicolon, "Expected ';' after field definition");

                structDecl.Fields.Add(new StructField(fieldName, TypeDescriptor.Parse(fieldType), Previous.Line, Previous.Column));

                if (Match(TokenType.Comma))
                {
                    continue;
                }
                
                if (_current == startPos)
                {
                    AddError("Parser failed to advance in ParseStruct", Current.Line, Current.Column);
                    break;
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after struct body");

            return structDecl;
        }

        private ContractDeclaration ParseContract()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected contract name");
            string name = Previous.Text;

            // Optional single-inheritance base: contract Foo : Base { ... }
            string? contractBaseType = null;
            if (Match(TokenType.Colon))
            {
                Consume(TokenType.Identifier, "Expected base type name after ':'");
                contractBaseType = Previous.Text;
            }

            Consume(TokenType.LBrace, "Expected '{' after contract name");

            var contract = new ContractDeclaration(name, line, column);
            contract.BaseTypeName = contractBaseType;

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;

                var memberAttributes = ParseAttributes();

                AccessModifier access = AccessModifier.Default;
                if (Match(TokenType.Public)) access = AccessModifier.Public;
                else if (Match(TokenType.Private)) access = AccessModifier.Private;
                else if (Match(TokenType.Protected)) access = AccessModifier.Protected;
                else if (Match(TokenType.Internal)) access = AccessModifier.Internal;

                bool isStatic = Match(TokenType.Static);

                if (Match(TokenType.Constructor))
                {
                    var ctor = ParseConstructor();
                    ctor.Attributes.AddRange(memberAttributes);
                    contract.Constructors.Add(ctor);
                }
                else if (Match(TokenType.Struct))
                {
                    var structDecl = ParseStruct();
                    structDecl.Attributes.AddRange(memberAttributes);
                    contract.Members.Add(structDecl);
                }
                else if (Match(TokenType.Fn))
                {
                    var function = ParseFunction();
                    function.Attributes.AddRange(memberAttributes);
                    function.ContractName = name;
                    function.IsStatic = isStatic;
                    function.Access = access;
                    contract.Members.Add(function);
                }
                else if (Match(TokenType.Identifier))
                {
                    if (memberAttributes.Count > 0)
                    {
                        AddError("Attributes on fields are not supported yet", memberAttributes[0].Line, memberAttributes[0].Column);
                    }
                    // A field declaration: name: type;
                    string fieldName = Previous.Text;
                    Consume(TokenType.Colon, "Expected ':' after field name");
                    string fieldType = ParseType();
                    Consume(TokenType.Semicolon, "Expected ';' after field declaration");
                    contract.Fields.Add(new StructField(fieldName, TypeDescriptor.Parse(fieldType), Previous.Line, Previous.Column));
                }
                else
                {
                    if (memberAttributes.Count > 0)
                    {
                        AddError("Attributes must be applied to a constructor, struct, or function", memberAttributes[0].Line, memberAttributes[0].Column);
                    }
                    else
                    {
                        AddError($"Unexpected token in contract: {Current.Type}", Current.Line, Current.Column);
                    }
                    Advance();
                }

                if (_current == startPos && !IsAtEnd())
                {
                    Advance();
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after contract body");

            // A non-static member fn is an instance method only when the
            // contract declares instance fields â€” that's the "class" case.
            // Contracts without fields keep the legacy module-function behavior.
            bool hasFields = contract.Fields.Count > 0;
            foreach (var member in contract.Members)
            {
                if (member is FunctionDeclaration f)
                    f.IsInstance = hasFields && !f.IsStatic;
            }

            return contract;
        }

        private ConstructorDeclaration ParseConstructor()
        {
            int line = Previous.Line;
            int column = Previous.Column;
            var ctor = new ConstructorDeclaration(line, column);

            Consume(TokenType.LParen, "Expected '(' after 'constructor'");

            if (!Check(TokenType.RParen))
            {
                do
                {
                    Consume(TokenType.Identifier, "Expected parameter name");
                    string paramName = Previous.Text;

                    string paramType = "";
                    if (Match(TokenType.Colon))
                    {
                        paramType = ParseType();
                    }

                    ctor.Parameters.Add(new Parameter(paramName, TypeDescriptor.Parse(paramType), Previous.Line, Previous.Column));
                } while (Match(TokenType.Comma));
            }

            Consume(TokenType.RParen, "Expected ')' after parameters");

            if (Match(TokenType.LBrace))
            {
                ctor.Body = ParseBlock();
            }
            else
            {
                Consume(TokenType.Semicolon, "Expected '{' or ';' after constructor declaration");
            }

            return ctor;
        }

        private FunctionDeclaration ParseFunction()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected function name");
            string name = Previous.Text;

            var function = new FunctionDeclaration(name, line, column);

            Consume(TokenType.LParen, "Expected '(' after function name");

            if (!Check(TokenType.RParen))
            {
                do
                {
                    Consume(TokenType.Identifier, "Expected parameter name");
                    string paramName = Previous.Text;

                    string paramType = "";
                    if (Match(TokenType.Colon))
                    {
                        paramType = ParseType();
                    }

                    function.Parameters.Add(new Parameter(paramName, TypeDescriptor.Parse(paramType), Previous.Line, Previous.Column));
                } while (Match(TokenType.Comma));
            }

            Consume(TokenType.RParen, "Expected ')' after parameters");

            if (Match(TokenType.Arrow))
            {
                function.ReturnType = TypeDescriptor.Parse(ParseType());
            }

            if (Match(TokenType.LBrace))
            {
                function.Body = ParseBlock();
            }
            else
            {
                Consume(TokenType.Semicolon, "Expected '{' or ';' after function declaration");
            }

            return function;
        }

        private string ParseType()
        {
            // Function type: (T1, T2) -> R, or with named params: (a: T1, b: T2) -> R
            if (Match(TokenType.LParen))
            {
                var paramTypes = new List<string>();
                if (!Check(TokenType.RParen))
                {
                    do
                    {
                        // Named parameter: "name: Type" — keep only the type.
                        // (Bare "T" and "name: T" both describe the same wire type.)
                        if (Check(TokenType.Identifier) && CheckNext(TokenType.Colon))
                        {
                            Advance(); // parameter name
                            Advance(); // ':'
                        }
                        paramTypes.Add(ParseType());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RParen, "Expected ')' after function parameter types");
                Consume(TokenType.Arrow, "Expected '->' in function type");
                var returnType = ParseType();
                return $"({string.Join(", ", paramTypes)}) -> {returnType}";
            }

            Consume(TokenType.Identifier, "Expected type name");
            string type = Previous.Text;

            // Generic instance: Name<T1, T2>
            if (Match(TokenType.Less))
            {
                type += "<";
                if (!Check(TokenType.Greater))
                {
                    do
                    {
                        type += ParseType();
                        if (Check(TokenType.Comma))
                        {
                            Advance();
                            type += ", ";
                        }
                    } while (!Check(TokenType.Greater) && !IsAtEnd());
                }
                Consume(TokenType.Greater, "Expected '>' after generic type arguments");
                type += ">";
            }

            while (Match(TokenType.LBracket))
            {
                Consume(TokenType.RBracket, "Expected ']' after '[' in type");
                type += "[]";
            }
            return type;
        }

        private BlockStatement ParseBlock()
        {
            var block = new BlockStatement(Current.Line, Current.Column);

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;
                block.Statements.Add(ParseStatement());
                if (_current == startPos && !IsAtEnd())
                {
                    AddError($"Unexpected token in block: {Current.Type}", Current.Line, Current.Column);
                    Advance();
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after block");

            return block;
        }

        private Statement ParseStatement()
        {
            if (Match(TokenType.Var) || Match(TokenType.Let))
            {
                return ParseVariableDeclaration();
            }
            else if (Match(TokenType.If))
            {
                return ParseIfStatement();
            }
            else if (Match(TokenType.While))
            {
                return ParseWhileStatement();
            }
            else if (Match(TokenType.For))
            {
                return ParseForStatement();
            }
            else if (Match(TokenType.Switch))
            {
                return ParseSwitchStatement();
            }
            else if (Match(TokenType.Break))
            {
                int line = Previous.Line;
                int column = Previous.Column;
                Consume(TokenType.Semicolon, "Expected ';' after 'break'");
                return new BreakStatement(line, column);
            }
            else if (Match(TokenType.Continue))
            {
                int line = Previous.Line;
                int column = Previous.Column;
                Consume(TokenType.Semicolon, "Expected ';' after 'continue'");
                return new ContinueStatement(line, column);
            }
            else if (Match(TokenType.Return))
            {
                return ParseReturnStatement();
            }
            else if (Match(TokenType.LBrace))
            {
                return ParseBlock();
            }
            else
            {
                return ParseExpressionStatement();
            }
        }

        private VariableDeclaration ParseVariableDeclaration()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected variable name");
            string name = Previous.Text;

            string type = "";
            if (Match(TokenType.Colon))
            {
                type = ParseType();
            }

            Expression? initializer = null;
            if (Match(TokenType.Assign))
            {
                initializer = ParseExpression();
            }

            Consume(TokenType.Semicolon, "Expected ';' after variable declaration");

            return new VariableDeclaration(name, TypeDescriptor.Parse(type), initializer, line, column);
        }

        private IfStatement ParseIfStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.LParen, "Expected '(' after 'if'");
            var condition = ParseExpression();
            Consume(TokenType.RParen, "Expected ')' after condition");

            var thenBranch = ParseStatement();
            Statement? elseBranch = null;

            if (Match(TokenType.Else))
            {
                elseBranch = ParseStatement();
            }

            return new IfStatement(condition, thenBranch, elseBranch, line, column);
        }

        private WhileStatement ParseWhileStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.LParen, "Expected '(' after 'while'");
            var condition = ParseExpression();
            Consume(TokenType.RParen, "Expected ')' after condition");

            var body = ParseStatement();

            return new WhileStatement(condition, body, line, column);
        }

        private ForStatement ParseForStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.LParen, "Expected '(' after 'for'");

            // Initializer: variable declaration, expression, or empty
            Statement? initializer = null;
            if (Match(TokenType.Var) || Match(TokenType.Let))
            {
                initializer = ParseVariableDeclaration();
            }
            else if (!Check(TokenType.Semicolon))
            {
                initializer = ParseExpressionStatement();
            }
            else
            {
                Consume(TokenType.Semicolon, "Expected ';' after for initializer");
            }

            // Condition (optional)
            Expression? condition = null;
            if (!Check(TokenType.Semicolon))
            {
                condition = ParseExpression();
            }
            Consume(TokenType.Semicolon, "Expected ';' after for condition");

            // Update (optional)
            Expression? update = null;
            if (!Check(TokenType.RParen))
            {
                update = ParseExpression();
            }
            Consume(TokenType.RParen, "Expected ')' after for update");

            var body = ParseStatement();

            return new ForStatement(initializer, condition, update, body, line, column);
        }

        private SwitchStatement ParseSwitchStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.LParen, "Expected '(' after 'switch'");
            var expression = ParseExpression();
            Consume(TokenType.RParen, "Expected ')' after expression");

            var switchStmt = new SwitchStatement(expression, line, column);

            Consume(TokenType.LBrace, "Expected '{' after switch expression");

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;
                if (Match(TokenType.Case))
                {
                    int? caseValue = null;
                    string? caseString = null;
                    if (Match(TokenType.IntLiteral))
                    {
                        caseValue = int.Parse(Previous.Text);
                    }
                    else if (Match(TokenType.StringLiteral))
                    {
                        caseString = Previous.Text;
                    }
                    else
                    {
                        AddError("Expected integer or string literal after 'case'", Current.Line, Current.Column);
                        Advance();
                    }
                    Consume(TokenType.Colon, "Expected ':' after case value");

                    var caseStatements = new List<Statement>();
                    while (!Check(TokenType.Case) && !Check(TokenType.Else) && !Check(TokenType.RBrace) && !IsAtEnd())
                    {
                        int caseStartPos = _current;
                        caseStatements.Add(ParseStatement());
                        if (_current == caseStartPos && !IsAtEnd())
                        {
                            // ParseStatement didn't consume any tokens, skip this token to prevent infinite loop
                            AddError("Unexpected token in switch case", Current.Line, Current.Column);
                            Advance();
                        }
                    }

                    var switchCase = new SwitchCase(caseValue, Previous.Line, Previous.Column) { StringValue = caseString };
                    switchCase.Statements.AddRange(caseStatements);
                    switchStmt.Cases.Add(switchCase);
                }
                else if (Match(TokenType.Else))
                {
                    Consume(TokenType.Colon, "Expected ':' after 'else'");

                    var elseStatements = new List<Statement>();
                    while (!Check(TokenType.RBrace) && !IsAtEnd())
                    {
                        int elseStartPos = _current;
                        elseStatements.Add(ParseStatement());
                        if (_current == elseStartPos && !IsAtEnd())
                        {
                            // ParseStatement didn't consume any tokens, skip this token to prevent infinite loop
                            AddError("Unexpected token in switch else", Current.Line, Current.Column);
                            Advance();
                        }
                    }

                    var elseCase = new SwitchCase(null, Previous.Line, Previous.Column);
                    elseCase.Statements.AddRange(elseStatements);
                    switchStmt.Cases.Add(elseCase);
                }
                else
                {
                    // Unexpected token in switch
                    AddError($"Unexpected token in switch: {Current.Type}", Current.Line, Current.Column);
                    Advance();
                }

                if (_current == startPos && !IsAtEnd())
                {
                    Advance();
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after switch body");

            return switchStmt;
        }

        private ReturnStatement ParseReturnStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Expression? value = null;
            if (!Check(TokenType.Semicolon))
            {
                value = ParseExpression();
            }

            Consume(TokenType.Semicolon, "Expected ';' after return statement");

            return new ReturnStatement(value, line, column);
        }

        private ExpressionStatement ParseExpressionStatement()
        {
            var expr = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after expression");
            return new ExpressionStatement(expr, expr.Line, expr.Column);
        }

        private Expression ParseExpression()
        {
            return ParseAssignment();
        }

        private Expression ParseAssignment()
        {
            var expr = ParseOr();

            if (Match(TokenType.Assign, TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual, TokenType.SlashEqual, TokenType.PercentEqual))
            {
                var opToken = Previous;
                string op = opToken.Type switch
                {
                    TokenType.Assign => "=",
                    TokenType.PlusEqual => "+=",
                    TokenType.MinusEqual => "-=",
                    TokenType.StarEqual => "*=",
                    TokenType.SlashEqual => "/=",
                    TokenType.PercentEqual => "%=",
                    _ => "="
                };

                var value = ParseAssignment();

                if (expr is IdentifierExpression || expr is MemberExpression || expr is IndexExpression)
                {
                    return new BinaryExpression(expr, op, value, opToken.Line, opToken.Column);
                }

                AddError("Invalid assignment target", opToken.Line, opToken.Column);
            }

            return expr;
        }

        private Expression ParseOr()
        {
            var expr = ParseAnd();

            while (Match(TokenType.OrOr))
            {
                var op = Previous.Text;
                var right = ParseAnd();
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseAnd()
        {
            var expr = ParseEquality();

            while (Match(TokenType.AndAnd))
            {
                var op = Previous.Text;
                var right = ParseEquality();
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseEquality()
        {
            var expr = ParseComparison();

            while (Match(TokenType.EqualEqual, TokenType.BangEqual))
            {
                var op = Previous.Text;
                var right = ParseComparison();
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseComparison()
        {
            var expr = ParseTerm();

            while (Match(TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual))
            {
                var op = Previous.Text;
                var right = ParseTerm();
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseTerm()
        {
            var expr = ParseMultiplication();

            while (Match(TokenType.Plus, TokenType.Minus))
            {
                var op = Previous.Text;
                var right = ParseMultiplication();
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseMultiplication()
        {
            var expr = ParseUnary();

            while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
            {
                var op = Previous.Text;
                var right = ParseUnary();
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
        }

        private Expression ParseUnary()
        {
            if (Match(TokenType.Minus, TokenType.Bang))
            {
                var op = Previous;
                var operand = ParseUnary();
                return new UnaryExpression(operand, op.Text, op.Line, op.Column);
            }

            return ParsePostfix();
        }

        private Expression ParsePostfix()
        {
            var expr = ParsePrimary();

            while (true)
            {
                if (Match(TokenType.LParen))
                {
                    var call = new CallExpression(expr, expr.Line, expr.Column);

                    if (!Check(TokenType.RParen))
                    {
                        do
                        {
                            call.Arguments.Add(ParseExpression());
                        } while (Match(TokenType.Comma));
                    }

                    Consume(TokenType.RParen, "Expected ')' after arguments");
                    expr = call;
                }
                else if (Match(TokenType.Dot))
                {
                    Consume(TokenType.Identifier, "Expected property name after '.'");
                    string property = Previous.Text;
                    expr = new MemberExpression(expr, property, expr.Line, expr.Column);
                }
                else if (Match(TokenType.DoubleColon))
                {
                    if (TryGetDottedPath(expr, out string modulePath))
                    {
                        Consume(TokenType.Identifier, "Expected member name after '::'");
                        string member = Previous.Text;
                        expr = new ScopedAccessExpression(modulePath, member, expr.Line, expr.Column);
                    }
                    else
                    {
                        AddError("Left side of '::' must be a module identifier", expr.Line, expr.Column);
                    }
                }
                else if (Match(TokenType.LBracket))
                {
                    var index = ParseExpression();
                    Consume(TokenType.RBracket, "Expected ']' after array index");
                    expr = new IndexExpression(expr, index, expr.Line, expr.Column);
                }
                else if (Match(TokenType.Pipe))
                {
                    var right = ParsePrimary(); // Pipe to a primary (e.g., function call or identifier)
                    expr = new PipeExpression(expr, right, expr.Line, expr.Column);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        /// <summary>
        /// Collects a dotted identifier path from an expression: IdentifierExpression("A")
        /// → "A"; A.B.C (a left-leaning chain of MemberExpressions over identifiers) → "A.B.C".
        /// Returns false for anything else (calls, indexers, literals, ...).
        /// </summary>
        private static bool TryGetDottedPath(Expression expr, out string path)
        {
            path = "";
            var segments = new Stack<string>();
            var current = expr;
            while (current is MemberExpression mem)
            {
                segments.Push(mem.Property);
                current = mem.Object;
            }
            if (current is not IdentifierExpression root) return false;
            segments.Push(root.Name);
            path = string.Join(".", segments);
            return true;
        }

        private Expression ParsePrimary()
        {
            if (Match(TokenType.IntLiteral))
            {
                return new LiteralExpression(int.Parse(Previous.Text), Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.FloatLiteral))
            {
                if (double.TryParse(Previous.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double floatValue))
                {
                    return new LiteralExpression(floatValue, Previous.Line, Previous.Column);
                }
                AddError($"Invalid float literal: '{Previous.Text}'", Previous.Line, Previous.Column);
                return new LiteralExpression(0.0, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.StringLiteral))
            {
                return new LiteralExpression(Previous.Text, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.InterpolatedString))
            {
                return ParseInterpolatedString(Previous.Text, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.True))
            {
                return new LiteralExpression(true, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.False))
            {
                return new LiteralExpression(false, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.Null))
            {
                return new LiteralExpression(null, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.LBracket))
            {
                var arrayLit = new ArrayLiteralExpression(Previous.Line, Previous.Column);
                if (!Check(TokenType.RBracket))
                {
                    do
                    {
                        arrayLit.Elements.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RBracket, "Expected ']' after array literal");
                return arrayLit;
            }
            else if (Match(TokenType.Fun))
            {
                int line = Previous.Line;
                int column = Previous.Column;

                // Parameters, two forms:
                //   fun x -> ... | fun a b -> ...          (space-separated)
                //   fun (x) -> ... | fun (x: int, y) -> ... (parenthesized)
                var parameters = new List<string>();
                var paramTypes = new List<string>();

                if (Match(TokenType.LParen))
                {
                    if (!Check(TokenType.RParen))
                    {
                        do
                        {
                            Consume(TokenType.Identifier, "Expected parameter name");
                            parameters.Add(Previous.Text);
                            string pt = "";
                            if (Match(TokenType.Colon)) pt = ParseType();
                            paramTypes.Add(pt);
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RParen, "Expected ')' after lambda parameters");
                }
                else
                {
                    while (Check(TokenType.Identifier))
                    {
                        parameters.Add(Advance().Text);
                        paramTypes.Add("");
                    }
                }

                if (!Match(TokenType.Arrow))
                {
                    AddError("Expected '->' after lambda parameters", Previous.Line, Previous.Column);
                }

                // Body: an expression, or a block when '{' follows.
                if (Match(TokenType.LBrace))
                {
                    var blockBody = ParseBlock(); // consumes the closing '}'
                    return new LambdaExpression(parameters, paramTypes, null, blockBody, line, column);
                }

                var body = ParseExpression();
                return new LambdaExpression(parameters, paramTypes, body, null, line, column);
            }
            else if (Match(TokenType.New))
            {
                int line = Previous.Line;
                int column = Previous.Column;

                Consume(TokenType.Identifier, "Expected type name after 'new'");
                string typeName = Previous.Text;

                Expression? size = null;
                if (Match(TokenType.LBracket))
                {
                    // Array allocation: new Type[expr]
                    size = ParseExpression();
                    Consume(TokenType.RBracket, "Expected ']' after array size");
                }
                else
                {
                    Consume(TokenType.LParen, "Expected '(' after type name");
                    Consume(TokenType.RParen, "Expected ')' after '('");
                }

                return new NewExpression(typeName, line, column, size);
            }
            else if (Match(TokenType.Identifier))
            {
                return new IdentifierExpression(Previous.Text, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.LParen))
            {
                var expr = ParseExpression();
                Consume(TokenType.RParen, "Expected ')' after expression");
                return expr;
            }

            AddError($"Unexpected token in expression: {Current.Type} ('{Current.Text}')", Current.Line, Current.Column);
            var dummy = new LiteralExpression(0, Current.Line, Current.Column);
            Advance(); // Advance to prevent infinite loop
            return dummy;
        }

        private Expression ParseInterpolatedString(string raw, int line, int column)
        {
            // raw includes the surrounding quotes: "Hello, {name}!"
            string content = raw.Length >= 2 ? raw.Substring(1, raw.Length - 2) : raw;
            var parts = new List<Expression>();
            int i = 0;

            while (i < content.Length)
            {
                int open = content.IndexOf('{', i);
                if (open < 0)
                {
                    parts.Add(new LiteralExpression("\"" + content.Substring(i) + "\"", line, column));
                    break;
                }

                if (open > i)
                {
                    parts.Add(new LiteralExpression("\"" + content.Substring(i, open - i) + "\"", line, column));
                }

                int close = content.IndexOf('}', open);
                if (close < 0)
                {
                    AddError("Unterminated interpolation in string literal", line, column);
                    parts.Add(new LiteralExpression(content.Substring(i), line, column));
                    break;
                }

                string name = content.Substring(open + 1, close - open - 1);
                if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') ||
                    name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                {
                    AddError($"Invalid interpolation expression: '{{{name}}}'", line, column);
                }
                else
                {
                    parts.Add(new IdentifierExpression(name, line, column));
                }

                i = close + 1;
            }

            if (parts.Count == 0) return new LiteralExpression("", line, column);

            Expression result = parts[0];
            for (int k = 1; k < parts.Count; k++)
            {
                result = new BinaryExpression(result, "+", parts[k], line, column);
            }
            return result;
        }

        private bool Match(params TokenType[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Current.Type == type;
        }

        /// <summary>True when the token AFTER the current one has the given type.</summary>
        private bool CheckNext(TokenType type)
        {
            if (IsAtEnd() || _current + 1 >= _tokens.Count) return false;
            return _tokens[_current + 1].Type == type;
        }

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous;
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            
            AddError(message, Current.Line, Current.Column);
            return new Token(TokenType.Identifier, "", Current.Line, Current.Column); // Return dummy token
        }

        private void Synchronize()
        {
            // More aggressive synchronization - skip to next statement boundary
            int startLine = Current.Line;
            Advance();

            // If we've moved to a new line, we might be at a statement boundary
            while (!IsAtEnd())
            {
                // Stop at semicolons
                if (Previous.Type == TokenType.Semicolon) return;

                // Stop at statement-starting keywords
                switch (Current.Type)
                {
                    case TokenType.Contract:
                    case TokenType.Fn:
                    case TokenType.Import:
                    case TokenType.If:
                    case TokenType.While:
                    case TokenType.Return:
                    case TokenType.Var:
                    case TokenType.Let:
                    case TokenType.Switch:
                        return;
                }

                // If we've moved to a new line and the current token is an identifier,
                // it might be the start of a new statement
                if (Current.Line > startLine && Current.Type == TokenType.Identifier)
                {
                    // Check if it's followed by something that looks like a statement
                    // For now, just stop to be safe
                    return;
                }

                Advance();
            }
        }

        private bool IsAtEnd() => Current.Type == TokenType.EOF;

        private Token Current => _tokens[_current];

        private Token Previous => _tokens[_current - 1];
    }
}