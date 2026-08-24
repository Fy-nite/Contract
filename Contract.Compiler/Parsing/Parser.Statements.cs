using System;
using System.Collections.Generic;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler.Parsing
{
    partial class Parser
    {
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
                bool bare = Check(TokenType.Identifier) && CheckNext(TokenType.In);
                if (bare)
                    AddError("for-in loops require parentheses — write 'for (x in xs)'", Current.Line, Current.Column);
                else if (!CheckForIn())
                    return ParseForStatement();
                return ParseForInStatement(bare);
            }
            else if (Match(TokenType.Switch))
            {
                return ParseSwitchStatement();
            }
            else if (Match(TokenType.Match))
            {
                var matchExpr = ParseMatchExpression();
                Consume(TokenType.Semicolon, "Expected ';' after match expression");
                return new ExpressionStatement(matchExpr, matchExpr.Line, matchExpr.Column);
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
            else if (Match(TokenType.Try))
            {
                return ParseTryStatement();
            }
            else if (Match(TokenType.Throw))
            {
                return ParseThrowStatement();
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
            bool isMutable = Previous.Text == "var";

            if (Match(TokenType.LParen))
            {
                var decl = new VariableDeclaration("", TypeDescriptor.Empty, null, line, column) { IsMutable = isMutable };
                if (!Check(TokenType.RParen))
                {
                    do
                    {
                        Consume(TokenType.Identifier, "Expected variable name in destructuring");
                        decl.Names.Add(Previous.Text);
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RParen, "Expected ')' after destructuring names");

                Expression? init = null;
                if (Match(TokenType.Assign))
                    init = ApplyImplicitLambda(ParseExpression(), Previous.Line, Previous.Column);
                Consume(TokenType.Semicolon, "Expected ';' after variable declaration");
                decl.Initializer = init;
                return decl;
            }

            Consume(TokenType.Identifier, "Expected variable name");
            string name = Previous.Text;

            bool hasExplicitType = false;
            string type = "";
            if (Match(TokenType.Colon))
            {
                type = ParseType();
                hasExplicitType = true;
            }

            Expression? initializer = null;
            if (Match(TokenType.Assign))
            {
                initializer = ApplyImplicitLambda(ParseExpression(), Previous.Line, Previous.Column);
            }

            Consume(TokenType.Semicolon, "Expected ';' after variable declaration");

            return new VariableDeclaration(name, TypeDescriptor.Parse(type), initializer, line, column) { IsMutable = isMutable, HasExplicitType = hasExplicitType };
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

            if (Check(TokenType.Identifier) && CheckNext(TokenType.Colon))
            {
                return ParseForEachStatement(line, column);
            }
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

            Expression? condition = null;
            if (!Check(TokenType.Semicolon))
            {
                condition = ParseExpression();
            }
            Consume(TokenType.Semicolon, "Expected ';' after for condition");

            Expression? update = null;
            if (!Check(TokenType.RParen))
            {
                update = ParseExpression();
            }
            Consume(TokenType.RParen, "Expected ')' after for update");

            var body = ParseStatement();

            return new ForStatement(initializer, condition, update, body, line, column);
        }

        private ForStatement ParseForEachStatement(int line, int column)
        {
            int temp = ++_forTempCounter;
            string arrName = $"__forArr_{temp}";
            string idxName = $"__forIdx_{temp}";

            Consume(TokenType.Identifier, "Expected loop variable name");
            string itemName = Previous.Text;
            Consume(TokenType.Colon, "Expected ':' after loop variable");
            var collection = ParseExpression();
            Consume(TokenType.RParen, "Expected ')' after foreach collection");
            var body = ParseStatement();

            var arrDecl = new VariableDeclaration(
                arrName, TypeDescriptor.Empty, collection, line, column);
            var idxDecl = new VariableDeclaration(
                idxName, TypeDescriptor.Empty, new LiteralExpression(0, line, column), line, column);

            var initBlock = new BlockStatement(line, column);
            initBlock.Statements.Add(arrDecl);
            initBlock.Statements.Add(idxDecl);

            var lengthCall = new CallExpression(
                new MemberExpression(new IdentifierExpression("Array", line, column), "Length", line, column),
                line, column);
            lengthCall.Arguments.Add(new IdentifierExpression(arrName, line, column));
            var condition = new BinaryExpression(
                new IdentifierExpression(idxName, line, column), "<", lengthCall, line, column);

            var update = new BinaryExpression(
                new IdentifierExpression(idxName, line, column), "+=",
                new LiteralExpression(1, line, column), line, column);

            var itemDecl = new VariableDeclaration(
                itemName, TypeDescriptor.Empty,
                new IndexExpression(
                    new IdentifierExpression(arrName, line, column),
                    new IdentifierExpression(idxName, line, column),
                    line, column),
                line, column);
            var bodyBlock = new BlockStatement(line, column);
            bodyBlock.Statements.Add(itemDecl);
            bodyBlock.Statements.Add(body);

            return new ForStatement(initBlock, condition, update, bodyBlock, line, column);
        }

        private bool CheckForIn()
        {
            if (!Check(TokenType.LParen)) return false;
            var t1 = PeekAhead(1);
            var t2 = PeekAhead(2);
            if (t1.Type == TokenType.Identifier && t2.Type == TokenType.In) return true;
            if (t1.Type == TokenType.Identifier && t2.Type == TokenType.Comma)
                return PeekAhead(3).Type == TokenType.Identifier
                    && PeekAhead(4).Type == TokenType.In;
            return false;
        }

        private Token PeekAhead(int offset)
        {
            int idx = _current + offset;
            return idx < Tokens.Count ? Tokens[idx] : Tokens[^1];
        }

        private ForInStatement ParseForInStatement(bool bareHeader = false)
        {
            int line = Previous.Line;
            int column = Previous.Column;

            string? keyVar;
            string? valueVar = null;

            if (!bareHeader)
                Consume(TokenType.LParen, "Expected '(' after 'for'");

            if (Check(TokenType.Identifier) && PeekAhead(1).Type == TokenType.Comma)
            {
                Consume(TokenType.Identifier, "Expected key variable in for-in pair");
                keyVar = Previous.Text;
                Consume(TokenType.Comma, "Expected ',' between key and value variables");
                Consume(TokenType.Identifier, "Expected value variable in for-in pair");
                valueVar = Previous.Text;
            }
            else
            {
                Consume(TokenType.Identifier, "Expected loop variable after 'for'");
                keyVar = Previous.Text;
            }

            Consume(TokenType.In, "Expected 'in' after loop variable");

            Expression iterable = LooksLikeRangeAhead() ? ParseRangeExpression() : ParseExpression();

            if (!bareHeader)
                Consume(TokenType.RParen, "Expected ')' after for-in header");

            var body = ParseStatement();

            return new ForInStatement(keyVar!, valueVar, iterable, body, line, column);
        }

        private bool LooksLikeRangeAhead()
        {
            int i = _current;
            int depth = 0;
            while (i < Tokens.Count)
            {
                var t = Tokens[i];
                switch (t.Type)
                {
                    case TokenType.EOF:
                        return false;
                    case TokenType.LParen or TokenType.LBracket:
                        depth++;
                        break;
                    case TokenType.RParen or TokenType.RBracket:
                        if (depth == 0) return false;
                        depth--;
                        break;
                    case TokenType.LBrace:
                        if (depth == 0) return false;
                        depth++;
                        break;
                    case TokenType.RBrace:
                        if (depth == 0) return false;
                        depth--;
                        break;
                    case TokenType.Semicolon when depth == 0:
                        return false;
                    case TokenType.DotDot when depth == 0:
                        return true;
                }
                i++;
            }
            return false;
        }

        private RangeExpression ParseRangeExpression()
        {
            int line = Current.Line;
            int column = Current.Column;

            _suppressRangeDepth++;
            Expression start;
            try { start = ParseExpression(); }
            finally { _suppressRangeDepth--; }

            Consume(TokenType.DotDot, "Expected '..' in range");
            bool inclusive = Match(TokenType.Assign);

            _suppressRangeDepth++;
            Expression end;
            try { end = ParseExpression(); }
            finally { _suppressRangeDepth--; }

            Expression? step = null;
            if (Check(TokenType.Identifier) && Current.Text == "by")
            {
                Advance();
                step = ParseExpression();
            }

            return new RangeExpression(start, end, inclusive, step, line, column);
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

        private TryStatement ParseTryStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.LBrace, "Expected '{' after 'try'");
            var tryBlock = ParseBlock();
            var stmt = new TryStatement(tryBlock, line, column);

            while (Match(TokenType.Catch))
            {
                string? excType = null;
                string excVar = "e";

                if (Check(TokenType.LParen))
                {
                    Advance();
                    if (Check(TokenType.Identifier))
                    {
                        var next = PeekAhead(1);
                        if (next.Type == TokenType.Identifier || next.Type == TokenType.RParen)
                        {
                            excType = Previous.Text;
                            excVar = Advance().Text;
                        }
                        else
                        {
                            excVar = Advance().Text;
                        }
                    }
                    Consume(TokenType.RParen, "Expected ')' after catch clause");
                }

                Consume(TokenType.LBrace, "Expected '{' after catch");
                var catchBody = ParseBlock();
                var catchClause = new CatchClause(excType, excVar, catchBody, Previous.Line, Previous.Column);
                stmt.CatchClauses.Add(catchClause);
            }

            if (Match(TokenType.Finally))
            {
                Consume(TokenType.LBrace, "Expected '{' after 'finally'");
                stmt.FinallyBlock = ParseBlock();
            }

            return stmt;
        }

        private ThrowStatement ParseThrowStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            var value = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after throw");

            return new ThrowStatement(value, line, column);
        }

        private ExpressionStatement ParseExpressionStatement()
        {
            var expr = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after expression");
            return new ExpressionStatement(expr, expr.Line, expr.Column);
        }
    }
}
