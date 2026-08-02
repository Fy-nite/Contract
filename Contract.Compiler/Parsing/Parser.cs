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

        public Parser(IEnumerable<Token> tokens, DiagnosticBag diagnostics)
        {
            _tokens = new List<Token>(tokens);
            _diagnostics = diagnostics;
        }

        public Program Parse()
        {
            var program = new Program(Current.Line, Current.Column);

            while (!IsAtEnd())
            {
                try
                {
                    int startPos = _current;

                    bool isExported = Match(TokenType.Export);
                    AccessModifier access = AccessModifier.Default;
                    if (Match(TokenType.Public)) access = AccessModifier.Public;
                    else if (Match(TokenType.Private)) access = AccessModifier.Private;
                    else if (Match(TokenType.Protected)) access = AccessModifier.Protected;
                    else if (Match(TokenType.Internal)) access = AccessModifier.Internal;

                    bool isStatic = Match(TokenType.Static);

                    if (Match(TokenType.Import))
                    {
                        Consume(TokenType.StringLiteral, "Expected string literal after 'import'");
                        string importPath = Previous.Text.Trim('"');
                        program.Imports.Add(importPath);
                        Consume(TokenType.Semicolon, "Expected ';' after import statement");
                    }
                    else if (Match(TokenType.Contract))
                    {
                        var contract = ParseContract();
                        contract.IsExported = isExported;
                        program.Contracts.Add(contract);
                    }
                    else if (Match(TokenType.Struct))
                    {
                        var structDecl = ParseStruct();
                        structDecl.IsExported = isExported;
                        program.Structs.Add(structDecl);
                    }
                    else if (Match(TokenType.Fn))
                    {
                        var func = ParseFunction();
                        func.IsExported = isExported;
                        func.IsStatic = isStatic;
                        func.Access = access;
                        program.Functions.Add(func);
                    }
                    else
                    {
                        _diagnostics.AddError($"Unexpected token at top level: {Current.Type} ('{Current.Text}')", Current.Line, Current.Column);
                        Synchronize();
                    }
                    
                    if (_current == startPos && !IsAtEnd())
                    {
                        Advance();
                    }
                }
                catch (Exception ex)
                {
                    _diagnostics.AddError($"Parser error: {ex.Message}", Current.Line, Current.Column);
                    Synchronize();
                }
            }

            return program;
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
                    _diagnostics.AddError("Parser failed to advance in ParseStruct", Current.Line, Current.Column);
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

            Consume(TokenType.LBrace, "Expected '{' after contract name");

            var contract = new ContractDeclaration(name, line, column);

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;
                
                AccessModifier access = AccessModifier.Default;
                if (Match(TokenType.Public)) access = AccessModifier.Public;
                else if (Match(TokenType.Private)) access = AccessModifier.Private;
                else if (Match(TokenType.Protected)) access = AccessModifier.Protected;
                else if (Match(TokenType.Internal)) access = AccessModifier.Internal;

                bool isStatic = Match(TokenType.Static);

                if (Match(TokenType.Constructor))
                {
                    contract.Constructors.Add(ParseConstructor());
                }
                else if (Match(TokenType.Struct))
                {
                    var structDecl = ParseStruct();
                    contract.Members.Add(structDecl);
                }
                else if (Match(TokenType.Fn))
                {
                    var function = ParseFunction();
                    function.ContractName = name;
                    function.IsStatic = isStatic;
                    function.Access = access;
                    contract.Members.Add(function);
                }
                else if (Match(TokenType.Identifier))
                {
                    // A field declaration: name: type;
                    string fieldName = Previous.Text;
                    Consume(TokenType.Colon, "Expected ':' after field name");
                    string fieldType = ParseType();
                    Consume(TokenType.Semicolon, "Expected ';' after field declaration");
                    contract.Fields.Add(new StructField(fieldName, TypeDescriptor.Parse(fieldType), Previous.Line, Previous.Column));
                }
                else
                {
                    _diagnostics.AddError($"Unexpected token in contract: {Current.Type}", Current.Line, Current.Column);
                    Advance();
                }

                if (_current == startPos && !IsAtEnd())
                {
                    Advance();
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after contract body");

            // A non-static member fn is an instance method only when the
            // contract declares instance fields — that's the "class" case.
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
            // Function type: (T1, T2) -> R
            if (Match(TokenType.LParen))
            {
                var paramTypes = new List<string>();
                if (!Check(TokenType.RParen))
                {
                    do
                    {
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
                    _diagnostics.AddError($"Unexpected token in block: {Current.Type}", Current.Line, Current.Column);
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
                        _diagnostics.AddError("Expected integer or string literal after 'case'", Current.Line, Current.Column);
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
                            _diagnostics.AddError("Unexpected token in switch case", Current.Line, Current.Column);
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
                            _diagnostics.AddError("Unexpected token in switch else", Current.Line, Current.Column);
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
                    _diagnostics.AddError($"Unexpected token in switch: {Current.Type}", Current.Line, Current.Column);
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

                _diagnostics.AddError("Invalid assignment target", opToken.Line, opToken.Column);
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
                    if (expr is IdentifierExpression moduleExpr)
                    {
                        Consume(TokenType.Identifier, "Expected member name after '::'");
                        string member = Previous.Text;
                        expr = new ScopedAccessExpression(moduleExpr.Name, member, expr.Line, expr.Column);
                    }
                    else
                    {
                        _diagnostics.AddError("Left side of '::' must be a module identifier", expr.Line, expr.Column);
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
                _diagnostics.AddError($"Invalid float literal: '{Previous.Text}'", Previous.Line, Previous.Column);
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
                    _diagnostics.AddError("Expected '->' after lambda parameters", Previous.Line, Previous.Column);
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

            _diagnostics.AddError($"Unexpected token in expression: {Current.Type} ('{Current.Text}')", Current.Line, Current.Column);
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
                    _diagnostics.AddError("Unterminated interpolation in string literal", line, column);
                    parts.Add(new LiteralExpression(content.Substring(i), line, column));
                    break;
                }

                string name = content.Substring(open + 1, close - open - 1);
                if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') ||
                    name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                {
                    _diagnostics.AddError($"Invalid interpolation expression: '{{{name}}}'", line, column);
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

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous;
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            
            _diagnostics.AddError(message, Current.Line, Current.Column);
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