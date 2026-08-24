using System;
using Contract.Compiler.AST;

namespace Contract.Compiler.Parsing
{
    /// <summary>
    /// Minimal host interface that the F# expression parser uses to call back
    /// into the C# Parser for block and type parsing. The C# Parser implements
    /// this; the F# code programs against it.
    /// </summary>
    public interface IParserHost
    {
        BlockStatement ParseBlock();
        string ParseType();
    }

    /// <summary>
    /// Contract for the expression parser implementation. The F# project
    /// implements this interface; the C# Parser loads it at runtime via
    /// assembly discovery and delegates expression parsing to it.
    /// </summary>
    public interface IExpressionParser
    {
        Expression ParseExpression(ParserContext ctx, IParserHost host);
        IfExpression ParseIfExpression(ParserContext ctx, IParserHost host);
        MatchExpression ParseMatchExpression(ParserContext ctx, IParserHost host);
        MatchPattern ParseMatchPattern(ParserContext ctx);
        Expression ParseInterpolatedString(ParserContext ctx, string raw, int line, int column);

        /// <summary>
        /// Wraps an expression containing a free <c>_</c>/<c>@</c> marker into an
        /// implicit lambda. No-op for lambdas and expressions without a marker.
        /// </summary>
        Expression ApplyImplicitLambda(ParserContext ctx, Expression expr, int line, int column);

        /// <summary>
        /// Folds a compile-time constant expression. Returns true when the
        /// expression is a foldable constant and sets <paramref name="value"/>.
        /// </summary>
        bool TryFoldConstant(Expression expr, ContractDeclaration contract, IParserHost host, out object? value);
    }
}
