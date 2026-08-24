using System;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Parsing;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.Expressions;

namespace Contract.Compiler.Parsing
{
    /// <summary>
    /// Recursive-descent parser for the Contract language. The expression
    /// parsing is delegated to the F# implementation.
    /// </summary>
    public partial class Parser : IParserHost
    {
        private readonly ParserContext _ctx;
        private readonly DiagnosticBag _diagnostics;
        private readonly string? _sourceFile;

        /// <summary>Current `namespace com.example;` for the file.</summary>
        private string? _currentNamespace;
        /// <summary>Unique suffix for foreach desugar temps.</summary>
        private int _forTempCounter;
        /// <summary>When positive, postfix <c>..</c> is left unconsumed.</summary>
        private int _suppressRangeDepth;

        /// <summary>The F# expression parser.</summary>
        private static readonly IExpressionParser _expressionParser = new FSharpExpressionParser();

        public Parser(IEnumerable<Token> tokens, DiagnosticBag diagnostics, string? sourceFile = null)
        {
            _ctx = new ParserContext(tokens, diagnostics, sourceFile);
            _diagnostics = diagnostics;
            _sourceFile = sourceFile;
        }

        /// <summary>The parser context — used by the F# expression parser.</summary>
        internal ParserContext Ctx => _ctx;

        // ── Forwarding properties (keep existing code working) ────────

        private int _current
        {
            get => _ctx.Cursor;
            set => _ctx.Cursor = value;
        }

        private Token Current => _ctx.Current;
        private Token Previous => _ctx.Previous;
        private IReadOnlyList<Token> Tokens => _ctx.Tokens;

        // ── IParserHost implementation ───────────────────────────────

        BlockStatement IParserHost.ParseBlock() => ParseBlock();
        string IParserHost.ParseType() => ParseType();

        // ── Diagnostics ─────────────────────────────────────────────

        private void AddError(string message, int line, int column)
            => _ctx.AddError(message, line, column);

        private void AddWarning(string message, int line, int column)
            => _ctx.AddWarning(message, line, column);

        // ── IExpressionParser delegation ─────────────────────────────

        private Expression ParseExpression()
            => _expressionParser.ParseExpression(_ctx, this);

        private IfExpression ParseIfExpression()
            => _expressionParser.ParseIfExpression(_ctx, this);

        private MatchExpression ParseMatchExpression()
            => _expressionParser.ParseMatchExpression(_ctx, this);

        private MatchPattern ParseMatchPattern()
            => _expressionParser.ParseMatchPattern(_ctx);

        private Expression ParseInterpolatedString(string raw, int line, int column)
            => _expressionParser.ParseInterpolatedString(_ctx, raw, line, column);

        private Expression ApplyImplicitLambda(Expression expr, int line, int column)
            => _expressionParser.ApplyImplicitLambda(_ctx, expr, line, column);

        // ── Parse() entry point ─────────────────────────────────────

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

                    if (Match(TokenType.Namespace))
                    {
                        Consume(TokenType.Identifier, "Expected namespace name after 'namespace'");
                        var ns = new System.Text.StringBuilder(Previous.Text);
                        while (Match(TokenType.Dot))
                        {
                            Consume(TokenType.Identifier, "Expected identifier after '.' in namespace");
                            ns.Append('.').Append(Previous.Text);
                        }
                        Consume(TokenType.Semicolon, "Expected ';' after namespace declaration");
                        _currentNamespace = ns.ToString();
                    }
                    else if (Match(TokenType.Import))
                    {
                        if (Match(TokenType.StringLiteral))
                        {
                            program.Imports.Add(Previous.Text);
                        }
                        else
                        {
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
                        contract.Namespace = _currentNamespace;
                        contract.SourceFile = _sourceFile;
                        contract.Attributes.AddRange(attributes);
                        program.Contracts.Add(contract);
                    }
                    else if (Match(TokenType.Struct))
                    {
                        var structDecl = ParseStruct();
                        structDecl.IsExported = isExported;
                        structDecl.Namespace = _currentNamespace;
                        structDecl.SourceFile = _sourceFile;
                        structDecl.Attributes.AddRange(attributes);
                        program.Structs.Add(structDecl);
                    }
                    else if (Match(TokenType.Enum))
                    {
                        var enumDecl = ParseEnum();
                        enumDecl.IsExported = isExported;
                        enumDecl.Namespace = _currentNamespace;
                        enumDecl.SourceFile = _sourceFile;
                        enumDecl.Attributes.AddRange(attributes);
                        program.Enums.Add(enumDecl);
                    }
                    else if (Match(TokenType.Type))
                    {
                        ParseSumType(program);
                    }
                    else if (MatchFn())
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
    }
}
