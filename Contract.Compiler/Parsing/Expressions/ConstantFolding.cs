using System;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Parsing;

namespace Contract.Compiler.Expressions.Internal
{
    /// <summary>
    /// Compile-time constant folding (FEATURE_PROPOSALS section 15).
    /// Folds literals, arithmetic, comparison, logical operators, and references
    /// to constants declared earlier in the same contract.
    /// </summary>
    internal static class ConstantFolding
    {
        private static string? ConstValueTypeName(object? value)
        {
            return value switch
            {
                int => "int",
                double => "double",
                string => "string",
                bool => "bool",
                _ => null
            };
        }

        private static bool ConstantTypeMatches(string annotation, object? value)
        {
            var actual = ConstValueTypeName(value);
            if (actual == null) return true; // null folds / unknown kinds: skip
            return annotation == actual;
        }

        /// <summary>Folds a binary operator applied to two constant values.</summary>
        public static (bool ok, object? value) FoldConstantBinary(string op, object? left, object? right)
        {
            switch (op)
            {
                case "==":
                case "!=":
                {
                    bool equal;
                    switch (left, right)
                    {
                        case (int a, double b): equal = (double)a == b; break;
                        case (double a, int b): equal = a == (double)b; break;
                        case (int a, int b): equal = a == b; break;
                        case (double a, double b): equal = a == b; break;
                        case (string a, string b): equal = a == b; break;
                        case (bool a, bool b): equal = a == b; break;
                        case (null, null): equal = true; break;
                        default: equal = false; break;
                    }
                    return (op == "==" ? equal : !equal, true);
                }

                case "<":
                case "<=":
                case ">":
                case ">=":
                {
                    int cmp;
                    switch (left, right)
                    {
                        case (int a, int b): cmp = a.CompareTo(b); break;
                        case (double a, double b): cmp = a.CompareTo(b); break;
                        case (int a, double b): cmp = ((double)a).CompareTo(b); break;
                        case (double a, int b): cmp = a.CompareTo((double)b); break;
                        case (string a, string b): cmp = a.CompareTo(b); break;
                        default: cmp = 0; break;
                    }
                    bool result = op switch
                    {
                        "<" => cmp < 0,
                        "<=" => cmp <= 0,
                        ">" => cmp > 0,
                        ">=" => cmp >= 0,
                        _ => false
                    };
                    return (true, (object?)result);
                }

                case "+":
                    switch (left, right)
                    {
                        case (int a, int b): return (true, (object?)(a + b));
                        case (double a, double b): return (true, (object?)(a + b));
                        case (int a, double b): return (true, (object?)((double)a + b));
                        case (double a, int b): return (true, (object?)(a + (double)b));
                        case (string a, string b): return (true, (object?)(a + b));
                        default: return (false, null);
                    }

                case "-":
                    switch (left, right)
                    {
                        case (int a, int b): return (true, (object?)(a - b));
                        case (double a, double b): return (true, (object?)(a - b));
                        case (int a, double b): return (true, (object?)((double)a - b));
                        case (double a, int b): return (true, (object?)(a - (double)b));
                        default: return (false, null);
                    }

                case "*":
                    switch (left, right)
                    {
                        case (int a, int b): return (true, (object?)(a * b));
                        case (double a, double b): return (true, (object?)(a * b));
                        case (int a, double b): return (true, (object?)((double)a * b));
                        case (double a, int b): return (true, (object?)(a * (double)b));
                        default: return (false, null);
                    }

                case "/":
                    switch (left, right)
                    {
                        case (int a, int b) when b != 0: return (true, (object?)(a / b));
                        case (double a, double b) when b != 0.0: return (true, (object?)(a / b));
                        case (int a, double b) when b != 0.0: return (true, (object?)((double)a / b));
                        case (double a, int b) when b != 0: return (true, (object?)(a / (double)b));
                        default: return (false, null);
                    }

                case "%":
                    switch (left, right)
                    {
                        case (int a, int b) when b != 0: return (true, (object?)(a % b));
                        case (double a, double b) when b != 0.0: return (true, (object?)(a % b));
                        case (int a, double b) when b != 0.0: return (true, (object?)((double)a % b));
                        case (double a, int b) when b != 0: return (true, (object?)(a % (double)b));
                        default: return (false, null);
                    }

                default:
                    return (false, null);
            }
        }

        /// <summary>
        /// Folds a compile-time constant expression. Returns true when the
        /// expression is a foldable constant and sets <c>value</c>.
        /// </summary>
        public static (bool ok, object? value) TryFoldConstant(
            ParserContext ctx, Expression expr,
            ContractDeclaration contract, IParserHost host)
        {
            switch (expr)
            {
                case LiteralExpression lit:
                    return (true, lit.Value);

                case IdentifierExpression id:
                {
                    var field = contract.Fields.FirstOrDefault(f => f.IsConst && f.IsStatic && f.Name == id.Name);
                    return field != null ? (true, (object?)field.ConstantValue) : (false, null);
                }

                case UnaryExpression unary:
                {
                    var (ok, operand) = TryFoldConstant(ctx, unary.Operand, contract, host);
                    if (!ok) return (false, null);
                    return (unary.Operator, operand) switch
                    {
                        ("-", int i) => (true, (object?)(-i)),
                        ("-", double d) => (true, (object?)(-d)),
                        ("!", bool b) => (true, (object?)(!b)),
                        _ => (false, null)
                    };
                }

                case BinaryExpression bin:
                {
                    // Short-circuit forms
                    if (bin.Operator == "&&" || bin.Operator == "||")
                    {
                        var (okL, lhs) = TryFoldConstant(ctx, bin.Left, contract, host);
                        if (!okL) return (false, null);
                        if (lhs is bool leftBool)
                        {
                            if ((bin.Operator == "&&" && !leftBool) || (bin.Operator == "||" && leftBool))
                            {
                                bool result = bin.Operator != "&&";
                                return (true, (object?)result);
                            }
                            var (okR, rhs) = TryFoldConstant(ctx, bin.Right, contract, host);
                            if (!okR) return (false, null);
                            if (rhs is bool rightBool)
                            {
                                bool result = bin.Operator == "&&" ? leftBool && rightBool : leftBool || rightBool;
                                return (true, (object?)result);
                            }
                            return (false, null);
                        }
                        return (false, null);
                    }

                    var (okLeft, left) = TryFoldConstant(ctx, bin.Left, contract, host);
                    var (okRight, right) = TryFoldConstant(ctx, bin.Right, contract, host);
                    if (!okLeft || !okRight) return (false, null);
                    return FoldConstantBinary(bin.Operator, left, right);
                }

                default:
                    return (false, null);
            }
        }

        /// <summary>
        /// Best-effort type inference from an expression for contract-scope
        /// lambda desugaring. Returns null when the type cannot be inferred.
        /// </summary>
        public static string? InferTypeFromExpression(Expression expr)
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
    }
}
