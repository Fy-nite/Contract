using System;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler.Parsing
{
    partial class Parser
    {
        private string ParseType()
        {
            // Delegate type: <Delegate(params) -> return>
            if (Match(TokenType.Less))
            {
                int line = Previous.Line;
                int column = Previous.Column;
                Consume(TokenType.Identifier, "Expected 'Delegate' after '<' in delegate type");
                if (Previous.Text != "Delegate")
                {
                    AddError($"Expected 'Delegate' after '<' in delegate type, got '{Previous.Text}'", line, column);
                }
                var fnType = ParseType();
                Consume(TokenType.Greater, "Expected '>' after delegate type");
                return $"Delegate<{fnType}>";
            }

            if (Match(TokenType.LParen))
            {
                var paramTypes = new List<string>();
                if (!Check(TokenType.RParen))
                {
                    do
                    {
                        if (Check(TokenType.Identifier) && CheckNext(TokenType.Colon))
                        {
                            Advance();
                            Advance();
                        }
                        paramTypes.Add(ParseType());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RParen, "Expected ')' after function parameter types");
                if (Match(TokenType.Arrow))
                {
                    var returnType = ParseType();
                    return $"({string.Join(", ", paramTypes)}) -> {returnType}";
                }
                return $"({string.Join(", ", paramTypes)})";
            }

            Consume(TokenType.Identifier, "Expected type name");
            string type = Previous.Text;

            while (Match(TokenType.Dot))
            {
                Consume(TokenType.Identifier, "Expected identifier after '.' in type name");
                type += "." + Previous.Text;
            }

            if (Match(TokenType.Less))
            {
                type += "<";
                if (!Check(TokenType.Greater) && !Check(TokenType.GreaterGreater))
                {
                    do
                    {
                        type += ParseType();
                        if (Check(TokenType.Comma))
                        {
                            Advance();
                            type += ", ";
                        }
                    } while (!Check(TokenType.Greater) && !Check(TokenType.GreaterGreater) && !IsAtEnd());
                }
                if (Check(TokenType.GreaterGreater))
                    SplitGreaterGreater();
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

        private bool CheckNext(TokenType type)
        {
            if (IsAtEnd() || _current + 1 >= Tokens.Count) return false;
            return Tokens[_current + 1].Type == type;
        }

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous;
        }

        private void SplitGreaterGreater()
            => _ctx.SplitGreaterGreater();

        private Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            
            AddError(message, Current.Line, Current.Column);
            return new Token(TokenType.Identifier, "", Current.Line, Current.Column);
        }

        private void Synchronize()
        {
            int startLine = Current.Line;
            Advance();

            while (!IsAtEnd())
            {
                if (Previous.Type == TokenType.Semicolon) return;

                if (Current.Type is TokenType.Contract or TokenType.Import or TokenType.If or TokenType.While or TokenType.Return or TokenType.Var or TokenType.Let or TokenType.Const or TokenType.Switch or TokenType.For or TokenType.Static or TokenType.Public or TokenType.Private or TokenType.Protected or TokenType.Internal or TokenType.Export)
                    return;
                if (CheckFn())
                    return;

                if (Current.Line > startLine && Current.Type == TokenType.Identifier)
                {
                    return;
                }

                Advance();
            }
        }

        private bool IsAtEnd() => Current.Type == TokenType.EOF;

        private bool CheckFn()
            => Current.Type == TokenType.Identifier && Current.Text == "fn"
               && _current + 1 < Tokens.Count && Tokens[_current + 1].Type == TokenType.Identifier;

        private bool MatchFn()
        {
            if (CheckFn()) { Advance(); return true; }
            return false;
        }

        private bool CheckFnLambda()
            => Current.Type == TokenType.Identifier && Current.Text == "fn"
               && _current + 1 < Tokens.Count && Tokens[_current + 1].Type == TokenType.LParen;

        // ── Constant folding helpers (used by declarations) ──────────

        /// <summary>
        /// Best-effort type inference from an expression for contract-scope lambda desugaring.
        /// Returns null when the type cannot be inferred.
        /// </summary>
        private string? InferTypeFromExpression(Expression expr)
        {
            return expr switch
            {
                LiteralExpression lit => lit.Value switch
                {
                    int => "int",
                    double => "double",
                    string => "string",
                    bool => "bool",
                    _ => null
                },
                BinaryExpression b => InferTypeFromExpression(b.Left),
                UnaryExpression u => InferTypeFromExpression(u.Operand),
                IdentifierExpression id => id.Name,
                _ => null
            };
        }

        private bool TryFoldConstant(Expression expr, ContractDeclaration contract, out object? value)
        {
            switch (expr)
            {
                case LiteralExpression lit:
                    value = lit.Value;
                    return true;

                case IdentifierExpression id:
                {
                    var field = contract.Fields.FirstOrDefault(f => f.IsConst && f.IsStatic && f.Name == id.Name);
                    if (field == null)
                    {
                        value = null;
                        return false;
                    }
                    value = field.ConstantValue;
                    return true;
                }

                case UnaryExpression unary:
                {
                    if (!TryFoldConstant(unary.Operand, contract, out object? operand))
                    {
                        value = null;
                        return false;
                    }
                    switch (unary.Operator)
                    {
                        case "-" when operand is int i: value = -i; return true;
                        case "-" when operand is double d: value = -d; return true;
                        case "!" when operand is bool b: value = !b; return true;
                        default: value = null; return false;
                    }
                }

                case BinaryExpression bin:
                {
                    if (bin.Operator is "&&" or "||")
                    {
                        if (!TryFoldConstant(bin.Left, contract, out object? lhs) || lhs is not bool leftBool)
                        {
                            value = null;
                            return false;
                        }
                        if ((bin.Operator == "&&" && !leftBool) || (bin.Operator == "||" && leftBool))
                        {
                            value = bin.Operator == "&&" ? false : true;
                            return true;
                        }
                        if (!TryFoldConstant(bin.Right, contract, out object? rhs) || rhs is not bool rightBool)
                        {
                            value = null;
                            return false;
                        }
                        value = bin.Operator == "&&" ? leftBool && rightBool : leftBool || rightBool;
                        return true;
                    }

                    if (!TryFoldConstant(bin.Left, contract, out object? left) ||
                        !TryFoldConstant(bin.Right, contract, out object? right))
                    {
                        value = null;
                        return false;
                    }
                    return FoldConstantBinary(bin.Operator, left, right, out value);
                }

                default:
                    value = null;
                    return false;
            }
        }

        private bool FoldConstantBinary(string op, object? left, object? right, out object? value)
        {
            value = null;

            if (op is "==" or "!=")
            {
                bool equal = left switch
                {
                    int a when right is double b => a == b,
                    double a when right is int b => a == b,
                    int a when right is int b => a == b,
                    double a when right is double b => a == b,
                    string a when right is string b => a == b,
                    bool a when right is bool b => a == b,
                    null when right == null => true,
                    _ => false
                };
                value = op == "==" ? equal : !equal;
                return true;
            }

            if (op is "<" or "<=" or ">" or ">=")
            {
                int cmp = (left, right) switch
                {
                    (int a, int b) => a.CompareTo(b),
                    (double a, double b) => a.CompareTo(b),
                    (int a, double b) => a.CompareTo(b),
                    (double a, int b) => a.CompareTo(b),
                    (string a, string b) => string.CompareOrdinal(a, b),
                    _ => int.MinValue
                };
                if (cmp == int.MinValue)
                    return false;
                value = op switch
                {
                    "<" => cmp < 0,
                    "<=" => cmp <= 0,
                    ">" => cmp > 0,
                    _ => cmp >= 0
                };
                return true;
            }

            if (op == "+" && (left is string || right is string))
            {
                string ls = left is string lc ? UnquoteConstant(lc) : ConstantToText(left);
                string rs = right is string rc ? UnquoteConstant(rc) : ConstantToText(right);
                value = "\"" + ls + rs + "\"";
                return true;
            }

            if (left is int li && right is int ri)
            {
                switch (op)
                {
                    case "+": value = li + ri; return true;
                    case "-": value = li - ri; return true;
                    case "*": value = li * ri; return true;
                    case "/":
                        if (ri == 0) return false;
                        value = li / ri;
                        return true;
                    case "%":
                        if (ri == 0) return false;
                        value = li % ri;
                        return true;
                    case "<<":
                        value = li << ri;
                        return true;
                    case "|":
                        value = li | ri;
                        return true;
                }
                return false;
            }

            if ((left is int || left is double) && (right is int || right is double))
            {
                double ld = Convert.ToDouble(left, System.Globalization.CultureInfo.InvariantCulture);
                double rd = Convert.ToDouble(right, System.Globalization.CultureInfo.InvariantCulture);
                switch (op)
                {
                    case "+": value = ld + rd; return true;
                    case "-": value = ld - rd; return true;
                    case "*": value = ld * rd; return true;
                    case "/":
                        if (rd == 0.0) return false;
                        value = ld / rd;
                        return true;
                    case "%":
                        if (rd == 0.0) return false;
                        value = ld % rd;
                        return true;
                }
            }

            return false;
        }

        private static string ConstantToText(object? value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => s,
            _ => ""
        };

        private static string UnquoteConstant(string s)
            => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

        private static string? ConstValueTypeName(object? value) => value switch
        {
            int => "int",
            double => "double",
            string => "string",
            bool => "bool",
            _ => null
        };

        private static bool ConstantTypeMatches(string annotation, object? value)
        {
            string? actual = ConstValueTypeName(value);
            if (actual == null) return true;
            return annotation == actual;
        }
    }
}
