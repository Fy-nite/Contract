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
        /// <summary>Current `namespace com.example;` for the file — applied to subsequent declarations.</summary>
        private string? _currentNamespace;
        /// <summary>Unique suffix for foreach desugar temps (__forArr_N / __forIdx_N) and match temps (__match_N).</summary>
        private int _forTempCounter;

        /// <summary>
        /// When positive, the postfix <c>..</c> range operator is left
        /// unconsumed — used while parsing for-in headers so the header can
        /// claim <c>0..10</c>/<c>0..=10</c> as its own range form instead of
        /// letting the operand unroll into an array literal.
        /// </summary>
        private int _suppressRangeDepth;

        public Parser(IEnumerable<Token> tokens, DiagnosticBag diagnostics, string? sourceFile = null)
        {
            _tokens = new List<Token>(tokens);
            _diagnostics = diagnostics;
            _sourceFile = sourceFile;
        }

        /// <summary>Reports a parse error attributed to this parser's source file (if any).</summary>
        private void AddError(string message, int line, int column)
            => _diagnostics.AddError(message, line, column, _sourceFile);

        /// <summary>Reports a parse warning attributed to this parser's source file (if any).</summary>
        private void AddWarning(string message, int line, int column)
            => _diagnostics.AddWarning(message, line, column, _sourceFile);

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
                        // namespace com.example;
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
                            // File import: import "path/to/file.ct". The quotes
                            // are kept so import resolution can distinguish a
                            // file path from a dotted namespace import.
                            program.Imports.Add(Previous.Text);
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
                        // Sum type (FEATURE_PROPOSALS §2):
                        //   type Shape { Circle(r: double) | Rect(w, h) | Unit }
                        // Synthesizes one base contract + one variant contract
                        // per alternative; see ParseSumType.
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

        /// <summary>
        /// Parses a sum type (FEATURE_PROPOSALS §2):
        /// <code>
        /// type Shape {
        ///     Circle(radius: double)
        ///   | Rect(w: double, h: double)
        ///   | Unit
        /// }
        /// </code>
        /// Lowers into plain contracts so the rest of the compiler sees nothing
        /// new:
        ///  - the base (<c>Shape</c>) carries the hidden tag field <c>__tag</c>,
        ///    one static factory per variant (<c>Shape.Circle(2.0)</c>), and is
        ///    marked IsSumTypeBase with its variant list (exhaustiveness data);
        ///  - each variant becomes a contract named <c>Shape.Circle</c> etc.
        ///    deriving the base, holding the variant's fields, whose constructor
        ///    stores them and stamps <c>__tag</c>.
        /// </summary>
        private void ParseSumType(Program program)
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected sum type name after 'type'");
            string name = Previous.Text;

            Consume(TokenType.LBrace, "Expected '{' after sum type name");

            var variants = new List<(string Name, List<Parameter> Params)>();
            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;
                Consume(TokenType.Identifier, "Expected variant name in sum type");
                string vname = Previous.Text;

                var vparams = new List<Parameter>();
                if (Match(TokenType.LParen))
                {
                    if (!Check(TokenType.RParen))
                    {
                        do
                        {
                            int pline = Current.Line;
                            int pcol = Current.Column;
                            Consume(TokenType.Identifier, "Expected parameter name in variant");
                            string pname = Previous.Text;
                            Consume(TokenType.Colon, "Expected ':' after variant parameter name");
                            string ptype = ParseType();
                            vparams.Add(new Parameter(pname, TypeDescriptor.Parse(ptype), pline, pcol));
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RParen, $"Expected ')' after variant '{vname}' parameters");
                }

                variants.Add((vname, vparams));
                Match(TokenType.Comma);
                Match(TokenType.Pipe);   // '|' separator between alternatives

                if (_current == startPos && !IsAtEnd())
                {
                    AddError("Parser failed to advance in sum type body", Current.Line, Current.Column);
                    Advance();
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after sum type body");

            if (variants.Count == 0)
            {
                AddError($"Sum type '{name}' must declare at least one variant", line, column);
                return;
            }

            string? ns = _currentNamespace;

            var baseDecl = new ContractDeclaration(name, line, column)
            {
                Namespace = ns,
                SourceFile = _sourceFile,
                IsSumTypeBase = true,
            };
            baseDecl.Fields.Add(new StructField("__tag", TypeDescriptor.Parse("int"), line, column));

            // The BASE goes first: the IR emitter writes contracts in order,
            // and the ObjektIL text parser resolves 'extends' against types
            // declared so far — variants ahead of their base would lose the
            // inheritance link (and their instance size) at load time.
            program.Contracts.Add(baseDecl);

            foreach (var (vname, vparams) in variants)
            {
                baseDecl.SumVariants.Add(vname);

                // ── Variant contract: Shape.Circle : Shape ──
                string variantShort = $"{name}.{vname}";
                string variantFull = ns == null ? variantShort : $"{ns}.{variantShort}";
                var variant = new ContractDeclaration(variantShort, line, column)
                {
                    Namespace = ns,
                    SourceFile = _sourceFile,
                    BaseTypeName = name,
                    SumTypeOf = name,
                    SumVariantIndex = baseDecl.SumVariants.Count - 1,
                };

                foreach (var p in vparams)
                    variant.Fields.Add(new StructField(p.Name, p.Type, p.Line, p.Column));

                var ctor = new ConstructorDeclaration(line, column);
                var ctorBody = new BlockStatement(line, column);
                foreach (var p in vparams)
                {
                    ctor.Parameters.Add(new Parameter(p.Name, p.Type, p.Line, p.Column));
                    // this.p = p;
                    ctorBody.Statements.Add(new ExpressionStatement(
                        new BinaryExpression(
                            new MemberExpression(
                                new IdentifierExpression("this", line, column), p.Name, line, column),
                            "=",
                            new IdentifierExpression(p.Name, line, column),
                            line, column),
                        line, column));
                }
                // this.__tag = <variant index>;
                ctorBody.Statements.Add(new ExpressionStatement(
                    new BinaryExpression(
                        new MemberExpression(
                            new IdentifierExpression("this", line, column), "__tag", line, column),
                        "=",
                        new LiteralExpression(variant.SumVariantIndex, line, column),
                        line, column),
                    line, column));
                ctor.Body = ctorBody;
                variant.Constructors.Add(ctor);

                program.Contracts.Add(variant);
                // ── Static factory on the base: static fn Circle(...) -> Shape ──
                var factory = new FunctionDeclaration(vname, line, column)
                {
                    ContractName = name,
                    IsStatic = true,
                    ReturnType = TypeDescriptor.Parse(name),
                };
                var factoryBody = new BlockStatement(line, column);
                var newExpr = new NewExpression(variantFull, line, column);
                foreach (var p in vparams)
                {
                    factory.Parameters.Add(new Parameter(p.Name, p.Type, p.Line, p.Column));
                    newExpr.Arguments.Add(new IdentifierExpression(p.Name, line, column));
                }
                factoryBody.Statements.Add(new ReturnStatement(newExpr, line, column));
                factory.Body = factoryBody;
                baseDecl.Members.Add(factory);
            }
        }

        private EnumDeclaration ParseEnum()
        {            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected enum name");
            string name = Previous.Text;

            Consume(TokenType.LBrace, "Expected '{' after enum name");

            var enumDecl = new EnumDeclaration(name, line, column)
            {
                SourceFile = _sourceFile,
            };

            if (Check(TokenType.RBrace))
            {
                AddError("Enum must declare at least one member", line, column);
            }

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;
                Consume(TokenType.Identifier, "Expected enum member name");
                enumDecl.Members.Add(Previous.Text);
                Match(TokenType.Comma);   // optional trailing comma between members
                if (_current == startPos)
                {
                    AddError("Parser failed to advance in ParseEnum", Current.Line, Current.Column);
                    break;
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after enum body");

            return enumDecl;
        }

        private StructDeclaration ParseStruct()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected struct name");
            string name = Previous.Text;

            Consume(TokenType.LBrace, "Expected '{' after struct name");

            var structDecl = new StructDeclaration(name, line, column)
            {
                SourceFile = _sourceFile,
            };

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

            var contract = new ContractDeclaration(name, line, column);

            // Generic contract: contract Box<T, U> { ... }
            if (Match(TokenType.Less))
            {
                if (!Check(TokenType.Greater))
                {
                    do
                    {
                        Consume(TokenType.Identifier, "Expected type parameter name");
                        contract.TypeParameters.Add(Previous.Text);
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.Greater, "Expected '>' after type parameters");
            }

            // Optional inheritance: contract Foo : Base { ... } or, for
            // interface-style multiple parents (§6), contract Foo : Base, I1, I2
            string? contractBaseType = null;
            if (Match(TokenType.Colon))
            {
                Consume(TokenType.Identifier, "Expected base type name after ':'");
                contractBaseType = Previous.Text;

                // Additional parents behave as interfaces (no fields/ctors).
                while (Match(TokenType.Comma))
                {
                    Consume(TokenType.Identifier, "Expected parent name after ','");
                    contract.InterfaceNames.Add(Previous.Text);
                }
            }

            Consume(TokenType.LBrace, "Expected '{' after contract name");

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
                    structDecl.Namespace = _currentNamespace;
                    structDecl.SourceFile = _sourceFile;
                    contract.Members.Add(structDecl);
                }
                else if (Match(TokenType.Enum))
                {
                    var enumDecl = ParseEnum();
                    enumDecl.Attributes.AddRange(memberAttributes);
                    enumDecl.Namespace = _currentNamespace;
                    enumDecl.SourceFile = _sourceFile;
                    contract.Members.Add(enumDecl);
                }
                else if (MatchFn())
                {
                    var function = ParseFunction();
                    function.Attributes.AddRange(memberAttributes);
                    function.ContractName = name;
                    function.IsStatic = isStatic;
                    function.Access = access;
                    contract.Members.Add(function);
                }
                else if (Match(TokenType.Var) || Match(TokenType.Let))
                {
                    // Contract-scope let/var: desugar lambda bindings to functions.
                    //   let name = fn (p) -> body;        → static fn name(p) -> body
                    //   let name = fun (p) -> body;       → static fn name(p) -> body
                    //   var name = fn (p) -> body;        → static fn name(p) -> body
                    var memberLine = Previous.Line;
                    var memberCol = Previous.Column;
                    Consume(TokenType.Identifier, "Expected variable name after 'let' or 'var'");
                    var varName = Previous.Text;

                    // Optional type annotation: let name: Type = expr;
                    string? explicitReturnType = null;
                    if (Match(TokenType.Colon))
                    {
                        explicitReturnType = ParseType();
                    }

                    Consume(TokenType.Assign, "Expected '=' after variable name in contract scope");

                    Expression initExpr = ParseExpression();
                    Consume(TokenType.Semicolon, "Expected ';' after contract-scope variable declaration");

                    if (initExpr is LambdaExpression lambda)
                    {
                        // Desugar to a static function declaration.
                        var function = new FunctionDeclaration(varName, memberLine, memberCol);
                        function.ContractName = name;
                        function.IsStatic = true;
                        function.Access = access;
                        function.Attributes.AddRange(memberAttributes);

                        // Parameters from the lambda.
                        for (int i = 0; i < lambda.Parameters.Count; i++)
                        {
                            string paramName = lambda.Parameters[i];
                            string paramType = (i < lambda.ParameterTypes.Count && lambda.ParameterTypes[i] != "")
                                ? lambda.ParameterTypes[i]
                                : "int";
                            function.Parameters.Add(new Parameter(paramName, TypeDescriptor.Parse(paramType), lambda.Line, lambda.Column));
                        }

                        // Return type: explicit annotation wins.
                        if (explicitReturnType != null)
                        {
                            function.ReturnType = TypeDescriptor.Parse(explicitReturnType);
                        }

                        // Body: wrap expression body in a return, or use block body directly.
                        if (lambda.Body != null)
                        {
                            var block = new BlockStatement(lambda.Body.Line, lambda.Body.Column);
                            block.Statements.Add(new ReturnStatement(lambda.Body, lambda.Body.Line, lambda.Body.Column));
                            function.Body = block;
                        }
                        else if (lambda.BlockBody != null)
                        {
                            function.Body = lambda.BlockBody;
                        }

                        contract.Members.Add(function);
                    }
                    else
                    {
                        AddError($"Contract-scope variable initializer must be a lambda expression (use 'static fn' for method declarations)", initExpr.Line, initExpr.Column);
                    }
                }
                else if (Match(TokenType.Identifier))
                {
                    if (memberAttributes.Count > 0)
                    {
                        AddError("Attributes on fields are not supported yet", memberAttributes[0].Line, memberAttributes[0].Column);
                    }
                    // A field declaration: [static] name: type;
                    string fieldName = Previous.Text;
                    Consume(TokenType.Colon, "Expected ':' after field name");
                    string fieldType = ParseType();
                    Consume(TokenType.Semicolon, "Expected ';' after field declaration");
                    contract.Fields.Add(new StructField(fieldName, TypeDescriptor.Parse(fieldType), Previous.Line, Previous.Column)
                    {
                        IsStatic = isStatic,
                        Access = access,
                    });
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

            // A non-static member fn is an instance method: it takes the
            // receiver (`this`) as param 0, whether or not the contract
            // declares fields. Module-style behavior is opt-in via
            // `static fn` — that keeps contracts-as-namespaces working while
            // letting field-less contracts be used with `new Type()` too.
            foreach (var member in contract.Members)
            {
                if (member is FunctionDeclaration f)
                    f.IsInstance = !f.IsStatic;
            }

            return contract;
        }

        private ConstructorDeclaration ParseConstructor()
        {
            int line = Previous.Line;
            int column = Previous.Column;
            var ctor = new ConstructorDeclaration(line, column)
            {
                SourceFile = _sourceFile,
            };

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

            var function = new FunctionDeclaration(name, line, column)
            {
                SourceFile = _sourceFile,
            };

            // Generic function: fn identity<T>(x: T) -> T
            if (Match(TokenType.Less))
            {
                if (!Check(TokenType.Greater))
                {
                    do
                    {
                        Consume(TokenType.Identifier, "Expected type parameter name");
                        function.TypeParameters.Add(Previous.Text);
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.Greater, "Expected '>' after type parameters");
            }

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

            // Expression body: fn name(params) = expr  (Construct-style)
            // Checked before return type so `fn f(x) = x + 1` is unambiguous.
            if (Match(TokenType.Assign))
            {
                var exprBody = ParseExpression();
                Consume(TokenType.Semicolon, "Expected ';' after expression-bodied function");
                var block = new BlockStatement(line, column);
                block.Statements.Add(new ReturnStatement(exprBody, exprBody.Line, exprBody.Column));
                function.Body = block;
                return function;
            }

            // Return type annotation: fn name(params) -> Type  or  fn name(params): Type
            if (Match(TokenType.Arrow) || Match(TokenType.Colon))
            {
                function.ReturnType = TypeDescriptor.Parse(ParseType());

                // Expression body after return type: fn name(p) -> Type = expr
                if (Match(TokenType.Assign))
                {
                    var exprBody = ParseExpression();
                    Consume(TokenType.Semicolon, "Expected ';' after expression-bodied function");
                    var block = new BlockStatement(line, column);
                    block.Statements.Add(new ReturnStatement(exprBody, exprBody.Line, exprBody.Column));
                    function.Body = block;
                    return function;
                }
            }

            if (Match(TokenType.LBrace))
            {
                function.Body = ParseBlock();
            }
            else
            {
                Consume(TokenType.Semicolon, "Expected '{', '=', '->', ':' or ';' after function declaration");
            }

            return function;
        }

        private string ParseType()
        {
            // Delegate type: <Delegate(params) -> return> — sugar for the
            // generic form Delegate<(params) -> return>.
            if (Match(TokenType.Less))
            {
                int line = Previous.Line;
                int column = Previous.Column;
                Consume(TokenType.Identifier, "Expected 'Delegate' after '<' in delegate type");
                if (Previous.Text != "Delegate")
                {
                    AddError($"Expected 'Delegate' after '<' in delegate type, got '{Previous.Text}'", line, column);
                }
                var fnType = ParseType();   // (params) -> return
                Consume(TokenType.Greater, "Expected '>' after delegate type");
                return $"Delegate<{fnType}>";
            }

            // Function type: (T1, T2) -> R, or with named params: (a: T1, b: T2) -> R.
            // A parenthesized list with NO arrow is a tuple type: (T1, T2).
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
                if (Match(TokenType.Arrow))
                {
                    var returnType = ParseType();
                    return $"({string.Join(", ", paramTypes)}) -> {returnType}";
                }
                // Tuple type: (T1, T2) — a multi-value return.
                return $"({string.Join(", ", paramTypes)})";
            }

            Consume(TokenType.Identifier, "Expected type name");
            string type = Previous.Text;

            // Dotted type path: com.example.Foo
            while (Match(TokenType.Dot))
            {
                Consume(TokenType.Identifier, "Expected identifier after '.' in type name");
                type += "." + Previous.Text;
            }

            // Generic instance: Name<T1, T2>
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
                // `List<List<int>>` lexes the close as one `>>` — split it so
                // the enclosing generic sees its own `>`.
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
                // for-in iteration (§5): `for x in xs { }`,
                // `for i in 0..10 by 2 { }`, or the Dict pair form
                // `for (k, v) in d { }` — recognized before the C-style form.
                if (CheckForIn())
                    return ParseForInStatement();
                return ParseForStatement();
            }
            else if (Match(TokenType.Switch))
            {
                return ParseSwitchStatement();
            }
            else if (Match(TokenType.Match))
            {
                // Match in statement position: parse the expression, then let
                // the statement's trailing pop discard the arm value (§1).
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

            // Destructuring: var (a, b) = f(); — binds each tuple element.
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
                // `let double = _ * 2;` — a free `_`/`@` in the initializer is
                // sugar for `fun _ -> _ * 2`.
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

        /// <summary>
        /// Parses an if used as an expression (FEATURE_PROPOSALS §3):
        /// <c>if (cond) { value } else { value }</c>. Branch bodies are brace
        /// blocks holding a single expression; <c>else if</c> chains nest as
        /// another IfExpression in the else slot.
        /// </summary>
        private IfExpression ParseIfExpression()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.LParen, "Expected '(' after 'if'");
            var condition = ParseExpression();
            Consume(TokenType.RParen, "Expected ')' after condition");

            var thenBranch = ParseValueBlock("if branch");
            Expression elseBranch;
            if (Match(TokenType.Else))
            {
                if (Check(TokenType.If))
                {
                    Advance();
                    elseBranch = ParseIfExpression();   // else-if chain
                }
                else
                {
                    elseBranch = ParseValueBlock("else branch");
                }
            }
            else
            {
                AddError("An 'if' expression requires an 'else' branch — both arms must produce a value", line, column);
                elseBranch = new LiteralExpression(0, line, column);
            }

            return new IfExpression(condition, thenBranch, elseBranch, line, column);
        }

        /// <summary>A brace block whose value is a single expression: <c>{ expr }</c>.</summary>
        private Expression ParseValueBlock(string what)
        {
            Consume(TokenType.LBrace, $"Expected '{{' after {what}");
            var value = ApplyImplicitLambda(ParseExpression(), Current.Line, Current.Column);
            Match(TokenType.Semicolon);   // tolerate a trailing ';'
            Consume(TokenType.RBrace, $"Expected '}}' to close {what}");
            return value;
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

            // foreach: for (item : collection) — desugars to an index loop.
            if (Check(TokenType.Identifier) && CheckNext(TokenType.Colon))
            {
                return ParseForEachStatement(line, column);
            }
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

        /// <summary>
        /// Parses <c>for (item : collection) body</c> and desugars it into a
        /// standard index loop:
        /// <code>
        /// var __forArr_N = collection;
        /// var __forIdx_N = 0;
        /// for (; __forIdx_N &lt; Array.Length(__forArr_N); __forIdx_N += 1) {
        ///     var item = __forArr_N[__forIdx_N];
        ///     body
        /// }
        /// </code>
        /// The collection expression is evaluated once into a temp.
        /// </summary>
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

            // var __forArr_N = collection;
            var arrDecl = new VariableDeclaration(
                arrName, TypeDescriptor.Empty, collection, line, column);
            // var __forIdx_N = 0;
            var idxDecl = new VariableDeclaration(
                idxName, TypeDescriptor.Empty, new LiteralExpression(0, line, column), line, column);

            var initBlock = new BlockStatement(line, column);
            initBlock.Statements.Add(arrDecl);
            initBlock.Statements.Add(idxDecl);

            // __forIdx_N < Array.Length(__forArr_N)
            var lengthCall = new CallExpression(
                new MemberExpression(new IdentifierExpression("Array", line, column), "Length", line, column),
                line, column);
            lengthCall.Arguments.Add(new IdentifierExpression(arrName, line, column));
            var condition = new BinaryExpression(
                new IdentifierExpression(idxName, line, column), "<", lengthCall, line, column);

            // __forIdx_N += 1
            var update = new BinaryExpression(
                new IdentifierExpression(idxName, line, column), "+=",
                new LiteralExpression(1, line, column), line, column);

            // { var item = __forArr_N[__forIdx_N]; body }
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

        /// <summary>
        /// True when the tokens ahead form a for-in header: <c>x in</c> or the
        /// Dict pair form <c>(k, v) in</c>.
        /// </summary>
        private bool CheckForIn()
        {
            if (Check(TokenType.Identifier) && CheckNext(TokenType.In)) return true;
            if (Check(TokenType.LParen) && _current + 5 < _tokens.Count)
                return _tokens[_current + 1].Type == TokenType.Identifier
                    && _tokens[_current + 2].Type == TokenType.Comma
                    && _tokens[_current + 3].Type == TokenType.Identifier
                    && _tokens[_current + 4].Type == TokenType.RParen
                    && _tokens[_current + 5].Type == TokenType.In;
            return false;
        }

        /// <summary>
        /// Parses a for-in loop (FEATURE_PROPOSALS §5): the variable binding,
        /// the iterable (a plain expression or an inline range with optional
        /// <c>by step</c>), and the body. Codegen picks the index protocol by
        /// iterable type; ranges desugar to the C-style loop.
        /// </summary>
        private ForInStatement ParseForInStatement()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            string? keyVar;
            string? valueVar = null;
            if (Match(TokenType.LParen))
            {
                Consume(TokenType.Identifier, "Expected key variable in for-in pair");
                keyVar = Previous.Text;
                Consume(TokenType.Comma, "Expected ',' between key and value variables");
                Consume(TokenType.Identifier, "Expected value variable in for-in pair");
                valueVar = Previous.Text;
                Consume(TokenType.RParen, "Expected ')' after for-in pair");
            }
            else
            {
                Consume(TokenType.Identifier, "Expected loop variable after 'for'");
                keyVar = Previous.Text;
            }

            Consume(TokenType.In, "Expected 'in' after loop variable");

            Expression iterable = LooksLikeRangeAhead() ? ParseRangeExpression() : ParseExpression();
            var body = ParseStatement();

            return new ForInStatement(keyVar!, valueVar, iterable, body, line, column);
        }

        /// <summary>
        /// Scans ahead over the header (stopping at the body's opening brace)
        /// for a top-level <c>..</c>/<c>..=</c> — the inline range form.
        /// </summary>
        private bool LooksLikeRangeAhead()
        {
            int i = _current;
            int depth = 0;
            while (i < _tokens.Count)
            {
                var t = _tokens[i];
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
                        // The body's opening brace ends the header.
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

        /// <summary>Parses <c>start .. end [by step]</c> / <c>start ..= end [by step]</c>.</summary>
        private RangeExpression ParseRangeExpression()
        {
            int line = Current.Line;
            int column = Current.Column;

            _suppressRangeDepth++;
            Expression start;
            try { start = ParseExpression(); }
            finally { _suppressRangeDepth--; }

            Consume(TokenType.DotDot, "Expected '..' in range");
            bool inclusive = Match(TokenType.Assign);   // `..=`

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

                // Optional type annotation: catch (TypeError e) or catch (e)
                if (Check(TokenType.LParen))
                {
                    Advance();
                    if (!Check(TokenType.RParen))
                    {
                        // First identifier is the variable unless a second follows (type var)
                        if (Match(TokenType.Identifier))
                        {
                            string first = Previous.Text;
                            if (Match(TokenType.Identifier))
                            {
                                excType = first;
                                excVar = Previous.Text;
                            }
                            else
                            {
                                excVar = first;
                            }
                        }
                    }
                    Consume(TokenType.RParen, "Expected ')' after catch parameter");
                }

                Consume(TokenType.LBrace, "Expected '{' after catch");
                var catchBody = ParseBlock();
                stmt.CatchClauses.Add(new CatchClause(excType, excVar, catchBody, line, column));
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
            Consume(TokenType.Semicolon, "Expected ';' after throw expression");

            return new ThrowStatement(value, line, column);
        }

        private ExpressionStatement ParseExpressionStatement()
        {
            var expr = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after expression");
            return new ExpressionStatement(expr, expr.Line, expr.Column);
        }

        private Expression ParseExpression()
        {
            return ParsePipeline();
        }

        /// <summary>
        /// Pipeline: the lowest-precedence expression form. `a |> f` passes a
        /// into f. Pipes lower at parse time into calls (or stay pipes only
        /// when the RHS is a lambda), so the rest of the compiler never reasons
        /// about `|>` beyond the lambda case.
        /// </summary>
        private Expression ParsePipeline()
        {
            var expr = ParsePipeOperand();

            while (Match(TokenType.Pipe))
            {
                int line = Previous.Line;
                int column = Previous.Column;
                var right = ParsePipeOperand();
                expr = BuildPipe(expr, right, line, column);
            }

            return expr;
        }

        /// <summary>
        /// A pipe operand: an assignment-level expression, optionally composed
        /// with `>>`. `f >> g` lowers to `fun x -> g(f(x))`.
        /// </summary>
        private Expression ParsePipeOperand()
        {
            var expr = ParseAssignment();

            while (Match(TokenType.GreaterGreater))
            {
                int line = Previous.Line;
                int column = Previous.Column;
                var right = ParseAssignment();
                expr = Compose(expr, right, line, column);
            }

            return expr;
        }

        /// <summary>
        /// `left |> right` — rewrites the RHS so the piped value lands where
        /// the user expects:
        ///   x |> f(a)          → f(x, a)               (value prepended)
        ///   x |> f(_, a)       → f(x, a)               (bare _ = the value's spot)
        ///   x |> f(_ * 2)      → f(x, fun _ -> _ * 2)  (compound _ = lambda param)
        ///   x |> f | x |> A.B  → f(x) / A.B(x)         (synthesized call)
        ///   x |> expr-with-_   → x |> fun _ -> expr    (implicit lambda)
        ///   x |> fun a -> ...  → unchanged
        /// </summary>
        private Expression BuildPipe(Expression left, Expression right, int line, int column)
        {
            if (right is LambdaExpression)
                return new PipeExpression(left, right, line, column);

            if (right is CallExpression call)
            {
                // Bare `_`/`@` args mark where the piped value goes; compound
                // `_`/`@` args become implicit lambdas.
                int hole = -1;
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    var arg = call.Arguments[i];
                    if (arg is IdentifierExpression holeId && IsImplicitMarker(holeId.Name))
                    {
                        if (hole >= 0)
                        {
                            AddError("A pipe target can only use '_' as the value's spot once", arg.Line, arg.Column);
                            break;
                        }
                        hole = i;
                        call.Arguments.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        call.Arguments[i] = ApplyImplicitLambda(arg, arg.Line, arg.Column);
                    }
                }
                call.Arguments.Insert(hole >= 0 ? hole : 0, left);
                return call;
            }

            if (right is IdentifierExpression or MemberExpression or ScopedAccessExpression)
            {
                // x |> f  /  x |> IO.Println  /  x |> Math::Sqrt  →  f(x) etc.
                var call2 = new CallExpression(right, line, column);
                call2.Arguments.Add(left);
                return call2;
            }

            return new PipeExpression(left, ApplyImplicitLambda(right, line, column), line, column);
        }

        /// <summary>f >> g — composition. Lowers to `fun x -> g(f(x))`.</summary>
        private Expression Compose(Expression left, Expression right, int line, int column)
        {
            if (left is LambdaExpression || right is LambdaExpression)
            {
                AddError("Composition operands must be named functions (no lambdas in v1)", line, column);
                return new PipeExpression(left, right, line, column);
            }

            var x = new IdentifierExpression("__compose_arg", line, column);
            var inner = new CallExpression(left, line, column);
            inner.Arguments.Add(x);

            Expression body;
            if (right is CallExpression rightCall)
            {
                rightCall.Arguments.Insert(0, inner);
                body = rightCall;
            }
            else
            {
                var outer = new CallExpression(right, line, column);
                outer.Arguments.Add(inner);
                body = outer;
            }

            return new LambdaExpression(new List<string> { "__compose_arg" }, null, body, null, line, column);
        }

        /// <summary>
        /// Wraps an expression containing a free `_`/`@` into an implicit
        /// lambda: `_ * 2` → `fun _ -> _ * 2`. No-op for lambdas and for
        /// expressions without a marker.
        /// </summary>
        private Expression ApplyImplicitLambda(Expression expr, int line, int column)
        {
            if (expr is LambdaExpression) return expr;
            string? marker = FindImplicitMarker(expr);
            if (marker == null) return expr;
            return new LambdaExpression(new List<string> { marker }, null, expr, null, line, column);
        }

        private static bool IsImplicitMarker(string name) => name == "_" || name == "@";

        /// <summary>Finds the first free `_`/`@` identifier in an expression tree.</summary>
        private static string? FindImplicitMarker(Expression expr)
        {
            switch (expr)
            {
                case IdentifierExpression id when IsImplicitMarker(id.Name):
                    return id.Name;
                case CallExpression c:
                    foreach (var a in c.Arguments)
                    {
                        var m = FindImplicitMarker(a);
                        if (m != null) return m;
                    }
                    return null;
                case MemberExpression m:
                    return FindImplicitMarker(m.Object);
                case BinaryExpression b:
                    return FindImplicitMarker(b.Left) ?? FindImplicitMarker(b.Right);
                case UnaryExpression u:
                    return FindImplicitMarker(u.Operand);
                case IndexExpression ix:
                    return FindImplicitMarker(ix.Target) ?? FindImplicitMarker(ix.Index);
                case PipeExpression p:
                    return FindImplicitMarker(p.Left) ?? FindImplicitMarker(p.Right);
                case ArrayLiteralExpression al:
                    foreach (var e in al.Elements)
                    {
                        var m = FindImplicitMarker(e);
                        if (m != null) return m;
                    }
                    return null;
                default:
                    return null;
            }
        }

        private Expression ParseAssignment()
        {
            var expr = ParseOr();

            // Ternary: cond ? then : else — lowest precedence (above assignment).
            if (Match(TokenType.Question))
            {
                var thenBranch = ParseAssignment();
                Consume(TokenType.Colon, "Expected ':' in ternary expression");
                var elseBranch = ParseAssignment();
                return new TernaryExpression(expr, thenBranch, elseBranch, expr.Line, expr.Column);
            }

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

                var value = ParsePipeline();

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
                // Generic call-site type args: first<int>(xs) or Box<int>::reset().
                // Checked BEFORE the '(' branch so the '<' isn't parsed as a
                // comparison. Backtracks when the '<...>' isn't followed by
                // '(' or '::' (so `a < b` comparisons are untouched).
                if (Check(TokenType.Less) && TryLookaheadGenericCallArgs() is { } genArgs)
                {
                    if (Match(TokenType.LParen))
                    {
                        var call = new CallExpression(expr, expr.Line, expr.Column);
                        call.TypeArguments.AddRange(genArgs);
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
                    else if (Match(TokenType.DoubleColon))
                    {
                        // Box<int>::reset() — the type args belong to the
                        // module part of the scoped access.
                        if (TryGetDottedPath(expr, out string modulePath))
                        {
                            Consume(TokenType.Identifier, "Expected member name after '::'");
                            string member = Previous.Text;
                            var scoped = new ScopedAccessExpression(modulePath, member, expr.Line, expr.Column);
                            scoped.TypeArguments.AddRange(genArgs);
                            expr = scoped;
                        }
                        else
                        {
                            AddError("Left side of '::' must be a module identifier", expr.Line, expr.Column);
                        }
                    }
                    else
                    {
                        break; // defensive — the lookahead only succeeds on '(' or '::'
                    }
                }
                else if (Match(TokenType.LParen))
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
                else if (Check(TokenType.DotDot) && _suppressRangeDepth == 0)
                {
                    Advance();
                    // Range: 1..5 — unrolled to an array literal at parse time.
                    // (v1 requires integer-literal bounds.)
                    int line = Previous.Line;
                    int column = Previous.Column;
                    var end = ParseOr();
                    if (expr is LiteralExpression { Value: int startVal }
                        && end is LiteralExpression { Value: int endVal })
                    {
                        var arr = new ArrayLiteralExpression(line, column);
                        int step = startVal <= endVal ? 1 : -1;
                        for (int v = startVal; v != endVal + step; v += step)
                            arr.Elements.Add(new LiteralExpression(v, line, column));
                        expr = arr;
                    }
                    else
                    {
                        AddError("Range bounds must be integer literals (v1)", line, column);
                        expr = new ArrayLiteralExpression(line, column);
                    }
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

        /// <summary>
        /// When the current token is '&lt;', scans ahead for a balanced &lt;...&gt;
        /// of type-ish tokens immediately followed by '(' or '::' — the explicit
        /// type arguments of a generic call (<c>first&lt;int&gt;(xs)</c>,
        /// <c>Box&lt;int&gt;::reset()</c>). Returns the parsed type arguments with
        /// the cursor left after the '&gt;'; returns null (restoring the cursor)
        /// when the lookahead doesn't match, so <c>a &lt; b</c> comparisons are
        /// untouched.
        /// </summary>
        private List<TypeDescriptor>? TryLookaheadGenericCallArgs()
        {
            if (!Check(TokenType.Less)) return null;
            int save = _current;

            Advance(); // consume '<'
            int depth = 1;
            var argTexts = new List<string>();
            var current = new System.Text.StringBuilder();

            while (!IsAtEnd() && depth > 0)
            {
                var tok = Current;
                switch (tok.Type)
                {
                    case TokenType.Less:
                        depth++;
                        current.Append('<');
                        Advance();
                        break;
                    case TokenType.Greater:
                        depth--;
                        if (depth == 0)
                        {
                            if (current.Length > 0) argTexts.Add(current.ToString());
                            Advance();
                            if (Check(TokenType.LParen) || Check(TokenType.DoubleColon))
                                return argTexts.Select(TypeDescriptor.Parse).ToList();
                            _current = save;
                            return null;
                        }
                        current.Append('>');
                        Advance();
                        break;
                    case TokenType.GreaterGreater:
                        // '>>' closes two levels (nested generic close). At
                        // depth 1 it would be an unbalanced extra '>' — not a
                        // generic call.
                        if (depth < 2) { _current = save; return null; }
                        depth -= 2;
                        current.Append('>');   // one close belongs to the inner generic
                        Advance();
                        if (depth == 0)
                        {
                            if (current.Length > 0) argTexts.Add(current.ToString());
                            if (Check(TokenType.LParen) || Check(TokenType.DoubleColon))
                                return argTexts.Select(TypeDescriptor.Parse).ToList();
                            _current = save;
                            return null;
                        }
                        break;
                    case TokenType.Arrow:
                        current.Append("->");
                        Advance();
                        break;
                    case TokenType.Comma:
                        if (depth == 1)
                        {
                            argTexts.Add(current.ToString());
                            current.Clear();
                        }
                        else
                        {
                            current.Append(", ");
                        }
                        Advance();
                        break;
                    default:
                        current.Append(tok.Text);
                        Advance();
                        break;
                }
            }

            _current = save;
            return null;
        }

        private Expression ParsePrimary()
        {
            // match (x) { ... } — value-producing multi-way branch (§1).
            if (Check(TokenType.Match))
            {
                Advance();
                return ParseMatchExpression();
            }

            // if (c) { a } else { b } as a VALUE (§3). Statement position is
            // handled earlier by ParseStatement; reaching ParsePrimary means
            // the if appears where an expression is expected.
            if (Check(TokenType.If))
            {
                Advance();
                return ParseIfExpression();
            }

            if (Match(TokenType.IntLiteral))
            {
                string intText = Previous.Text;
                if (int.TryParse(intText, out int intValue))
                {
                    return new LiteralExpression(intValue, Previous.Line, Previous.Column);
                }
                AddWarning(
                    $"Integer literal '{intText}' exceeds the int range (max {int.MaxValue}); value clamped to 0",
                    Previous.Line, Previous.Column);
                return new LiteralExpression(0, Previous.Line, Previous.Column);
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
                while (!Check(TokenType.RBracket) && !Check(TokenType.Comma) && !IsAtEnd())
                {
                    arrayLit.Elements.Add(ParseExpression());
                    if (!Match(TokenType.Comma)) break;
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
            else if (CheckFnLambda())
            {
                // Construct-style anonymous lambda: fn (x: int) -> x + 1
                // Syntactically identical to `fun`, just uses the `fn` keyword.
                Advance(); // consume 'fn'
                int line = Previous.Line;
                int column = Previous.Column;

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

                if (Match(TokenType.LBrace))
                {
                    var blockBody = ParseBlock();
                    return new LambdaExpression(parameters, paramTypes, null, blockBody, line, column);
                }

                var fnBody = ParseExpression();
                return new LambdaExpression(parameters, paramTypes, fnBody, null, line, column);
            }
            else if (Match(TokenType.New))
            {
                int line = Previous.Line;
                int column = Previous.Column;

                Consume(TokenType.Identifier, "Expected type name after 'new'");
                string typeName = Previous.Text;
                // Dotted type path: new com.example.Foo(...)
                while (Match(TokenType.Dot))
                {
                    Consume(TokenType.Identifier, "Expected identifier after '.' in type name");
                    typeName += "." + Previous.Text;
                }
                // Module-qualified type: new Terminal::Terminal() — the module
                // short name is folded into the dotted path so namespace
                // imports can resolve it (`Terminal.Terminal` → full name).
                if (Match(TokenType.DoubleColon))
                {
                    Consume(TokenType.Identifier, "Expected type name after '::'");
                    typeName += "." + Previous.Text;
                }

                var newExpr = new NewExpression(typeName, line, column);

                // Generic instantiation: new Box<int>(5) / new Pair<int, string>(...)
                if (Match(TokenType.Less))
                {
                    if (!Check(TokenType.Greater))
                    {
                        do
                        {
                            newExpr.TypeArguments.Add(TypeDescriptor.Parse(ParseType()));
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.Greater, "Expected '>' after type arguments");
                }

                if (Match(TokenType.LBracket))
                {
                    // Array allocation: new Type[expr]
                    newExpr.Size = ParseExpression();
                    Consume(TokenType.RBracket, "Expected ']' after array size");
                }
                else
                {
                    // new Type() or new Type(args)
                    Consume(TokenType.LParen, "Expected '(' after type name");
                    if (!Check(TokenType.RParen))
                    {
                        do
                        {
                            newExpr.Arguments.Add(ParseExpression());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RParen, "Expected ')' after '('");
                }

                return newExpr;
            }
            else if (Match(TokenType.Identifier))
            {
                return new IdentifierExpression(Previous.Text, Previous.Line, Previous.Column);
            }
            else if (Match(TokenType.LParen))
            {
                // Tuple literal: (a, b, c) — a multi-value return. A single
                // parenthesized expression (a) is just grouping.
                if (!Check(TokenType.RParen))
                {
                    var first = ParseExpression();
                    if (Match(TokenType.Comma))
                    {
                        var tuple = new TupleLiteralExpression(Previous.Line, Previous.Column);
                        tuple.Elements.Add(first);
                        do
                        {
                            tuple.Elements.Add(ParseExpression());
                        } while (Match(TokenType.Comma));
                        Consume(TokenType.RParen, "Expected ')' after tuple literal");
                        return tuple;
                    }
                    Consume(TokenType.RParen, "Expected ')' after expression");
                    return first;
                }
                Consume(TokenType.RParen, "Expected ')' after expression");
                return new LiteralExpression(0, Previous.Line, Previous.Column);
            }

            AddError($"Unexpected token in expression: {Current.Type} ('{Current.Text}')", Current.Line, Current.Column);
            var dummy = new LiteralExpression(0, Current.Line, Current.Column);
            Advance(); // Advance to prevent infinite loop
            return dummy;
        }

        /// <summary>
        /// Parses <c>match (scrutinee) { pattern [if guard] =&gt; result, ... }</c>
        /// (FEATURE_PROPOSALS §1). Arms are comma-separated with an optional
        /// trailing comma. Patterns: literals, or-patterns (<c>1 | 2</c>),
        /// bindings (<c>n</c>), wildcards (<c>_</c>), and — once sum types
        /// exist — variants (<c>Circle(r)</c>).
        /// </summary>
        private MatchExpression ParseMatchExpression()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.LParen, "Expected '(' after 'match'");
            var scrutinee = ParseExpression();
            Consume(TokenType.RParen, "Expected ')' after match scrutinee");
            var match = new MatchExpression(scrutinee, line, column);

            Consume(TokenType.LBrace, "Expected '{' after match scrutinee");

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;
                var arm = new MatchArm(Current.Line, Current.Column);

                // Patterns: p1 | p2 | p3 (or-pattern)
                do
                {
                    arm.Patterns.Add(ParseMatchPattern());
                } while (MatchOrPatternPipe());

                if (Match(TokenType.If))
                    arm.Guard = ParseExpression();

                Consume(TokenType.FatArrow, "Expected '=>' after match pattern");
                arm.Result = ApplyImplicitLambda(ParseExpression(), line, column);
                match.Arms.Add(arm);

                if (!Match(TokenType.Comma))
                    break;   // last arm may omit the trailing comma

                if (_current == startPos && !IsAtEnd())
                {
                    AddError("Parser failed to advance in match arm", Current.Line, Current.Column);
                    Advance();
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after match arms");

            if (match.Arms.Count == 0)
                AddError("Match must declare at least one arm", line, column);

            return match;
        }

        /// <summary>
        /// The or-pattern separator is a bare <c>|</c> — which the lexer only
        /// produces as Pipe (<c>|&gt;</c>) or OrOr (<c>||</c>). Inside a
        /// pattern head a single pipe is unambiguous, so accept the Pipe token
        /// here (the lexer emits Pipe for any lone '|').
        /// </summary>
        private bool MatchOrPatternPipe() => Match(TokenType.Pipe);

        private MatchPattern ParseMatchPattern()
        {
            int line = Current.Line;
            int column = Current.Column;

            if (Match(TokenType.IntLiteral))
            {
                _ = int.TryParse(Previous.Text, out int i);
                return new LiteralPattern(i, line, column);
            }
            if (Match(TokenType.FloatLiteral))
            {
                double d = double.TryParse(Previous.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double dv) ? dv : 0.0;
                return new LiteralPattern(d, line, column);
            }
            if (Match(TokenType.StringLiteral))
                return new LiteralPattern(Previous.Text, line, column);
            if (Match(TokenType.True))
                return new LiteralPattern(true, line, column);
            if (Match(TokenType.False))
                return new LiteralPattern(false, line, column);
            if (Match(TokenType.Null))
                return new LiteralPattern(null, line, column);

            if (Match(TokenType.Identifier))
            {
                string name = Previous.Text;
                // Variant pattern: Circle(r), Rect(w, h) — an identifier
                // followed by '(' opens a sum-type variant destructure.
                if (Check(TokenType.LParen))
                {
                    var variant = new VariantPattern(name, line, column);
                    Advance(); // consume '('
                    if (!Check(TokenType.RParen))
                    {
                        do
                        {
                            variant.Arguments.Add(ParseMatchPattern());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RParen, $"Expected ')' after variant pattern '{name}('");
                    return variant;
                }
                if (name == "_")
                    return new WildcardPattern(line, column);
                return new BindingPattern(name, line, column);
            }

            AddError($"Expected a match pattern, found '{Current.Text}'", line, column);
            Advance();
            return new WildcardPattern(line, column);
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

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous;
        }

        /// <summary>
        /// Splits the current `>>` token in place into two `>` tokens, so a
        /// nested generic close (`List&lt;List&lt;int&gt;&gt;`) parses even though
        /// the lexer merges the two closes. The second `>` is inserted so the
        /// enclosing generic level consumes it as its own close.
        /// </summary>
        private void SplitGreaterGreater()
        {
            if (Current.Type != TokenType.GreaterGreater) return;
            var tok = Current;
            _tokens[_current] = new Token(TokenType.Greater, ">", tok.Line, tok.Column, 1);
            _tokens.Insert(_current + 1, new Token(TokenType.Greater, ">", tok.Line, tok.Column + 1, 1));
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

                // Stop at statement-starting keywords (including contextual 'fn')
                if (Current.Type is TokenType.Contract or TokenType.Import or TokenType.If or TokenType.While or TokenType.Return or TokenType.Var or TokenType.Let or TokenType.Switch or TokenType.For or TokenType.Static or TokenType.Public or TokenType.Private or TokenType.Protected or TokenType.Internal or TokenType.Export)
                    return;
                // fn is contextual: only a statement-starter when at the start of a line
                if (CheckFn())
                    return;

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

        /// <summary>
        /// True when the current token is an identifier with text "fn" followed
        /// by another identifier — the start of a function declaration. This
        /// makes <c>fn</c> a contextual keyword: it only acts as a keyword in
        /// declaration positions (before a function name), and remains a plain
        /// identifier elsewhere (e.g. <c>var fn = ...</c>).
        /// </summary>
        private bool CheckFn()
            => Current.Type == TokenType.Identifier && Current.Text == "fn"
               && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Identifier;

        /// <summary>Consumes the current token when it is a contextual "fn" keyword.</summary>
        private bool MatchFn()
        {
            if (CheckFn()) { Advance(); return true; }
            return false;
        }

        /// <summary>
        /// True when the current token is "fn" followed by <c>(</c> — the start
        /// of an anonymous lambda using the Construct-style <c>fn</c> keyword
        /// (e.g. <c>fn (x) -> x + 1</c>).  Distinguished from a function
        /// declaration by the <c>(</c> instead of an identifier.
        /// </summary>
        private bool CheckFnLambda()
            => Current.Type == TokenType.Identifier && Current.Text == "fn"
               && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.LParen;

        private Token Current => _tokens[_current];

        private Token Previous => _tokens[_current - 1];
    }
}