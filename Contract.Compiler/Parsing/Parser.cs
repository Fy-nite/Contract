using System;
using System.Collections.Generic;
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

        // Add to TokenType in Lexer.cs if necessary - skipping for now as 'constructor' is a new keyword.
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
                Consume(TokenType.Identifier, "Expected field name");
                string fieldName = Previous.Text;

                Consume(TokenType.Colon, "Expected ':' after field name");
                string fieldType = ParseType();

                structDecl.Fields.Add(new StructField(fieldName, fieldType, Previous.Line, Previous.Column));

                if (Match(TokenType.Comma))
                {
                    continue;
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
                else if (Match(TokenType.Fn))
                {
                    var function = ParseFunction();
                    function.ContractName = name;
                    function.IsStatic = isStatic;
                    function.Access = access;
                    contract.Members.Add(function);
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

                    ctor.Parameters.Add(new Parameter(paramName, paramType, Previous.Line, Previous.Column));
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

                    function.Parameters.Add(new Parameter(paramName, paramType, Previous.Line, Previous.Column));
                } while (Match(TokenType.Comma));
            }

            Consume(TokenType.RParen, "Expected ')' after parameters");

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
            Consume(TokenType.Identifier, "Expected type name");
            string type = Previous.Text;
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
            else if (Match(TokenType.Switch))
            {
                return ParseSwitchStatement();
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

            return new VariableDeclaration(name, type, initializer, line, column);
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
                    Consume(TokenType.IntLiteral, "Expected integer literal after 'case'");
                    int caseValue = int.Parse(Previous.Text);
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

                    var switchCase = new SwitchCase(caseValue, Previous.Line, Previous.Column);
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
            var expr = ParseEquality();

            if (Match(TokenType.Assign))
            {
                var equals = Previous;
                var value = ParseAssignment();

                if (expr is IdentifierExpression || expr is MemberExpression || expr is IndexExpression)
                {
                    return new BinaryExpression(expr, "=", value, equals.Line, equals.Column);
                }

                _diagnostics.AddError("Invalid assignment target", equals.Line, equals.Column);
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
            var expr = ParsePostfix();

            while (Match(TokenType.Star, TokenType.Slash))
            {
                var op = Previous.Text;
                var right = ParsePostfix();
                expr = new BinaryExpression(expr, op, right, expr.Line, expr.Column);
            }

            return expr;
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
            else if (Match(TokenType.StringLiteral))
            {
                return new LiteralExpression(Previous.Text, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.Null))
            {
                return new LiteralExpression(null, Previous.Line, Previous.Column);
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