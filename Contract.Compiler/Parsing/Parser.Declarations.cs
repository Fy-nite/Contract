using System;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler.Parsing
{
    partial class Parser
    {
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

        private void ParseSumType(Program program)
        {
            int line = Previous.Line;
            int column = Previous.Column;

            Consume(TokenType.Identifier, "Expected sum type name after 'type'");
            string name = Previous.Text;

            // Parse optional generic type parameters: type Result<T, E> { ... }
            var typeParams = new List<string>();
            if (Match(TokenType.Less))
            {
                if (!Check(TokenType.Greater))
                {
                    do
                    {
                        Consume(TokenType.Identifier, "Expected type parameter name");
                        typeParams.Add(Previous.Text);
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.Greater, "Expected '>' after type parameters");
            }

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
                Match(TokenType.Pipe);

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
            // Add generic type parameters to the base contract
            baseDecl.TypeParameters.AddRange(typeParams);
            baseDecl.Fields.Add(new StructField("__tag", TypeDescriptor.Parse("int"), line, column));

            program.Contracts.Add(baseDecl);

            foreach (var (vname, vparams) in variants)
            {
                baseDecl.SumVariants.Add(vname);

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
                // Variant contracts inherit the base's type parameters for
                // field validation, but the codegen emits @Generic on the base.
                variant.TypeParameters.AddRange(typeParams);

                foreach (var p in vparams)
                    variant.Fields.Add(new StructField(p.Name, p.Type, p.Line, p.Column));

                var ctor = new ConstructorDeclaration(line, column);
                var ctorBody = new BlockStatement(line, column);
                foreach (var p in vparams)
                {
                    ctor.Parameters.Add(new Parameter(p.Name, p.Type, p.Line, p.Column));
                    ctorBody.Statements.Add(new ExpressionStatement(
                        new BinaryExpression(
                            new MemberExpression(
                                new IdentifierExpression("this", line, column), p.Name, line, column),
                            "=",
                            new IdentifierExpression(p.Name, line, column),
                            line, column),
                        line, column));
                }
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
        {
            int line = Previous.Line;
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
                Match(TokenType.Comma);
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

            string? contractBaseType = null;
            if (Match(TokenType.Colon))
            {
                Consume(TokenType.Identifier, "Expected base type name after ':'");
                contractBaseType = Previous.Text;

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
                else if (Match(TokenType.Invariant))
                {
                    var invLine = Previous.Line;
                    var invCol = Previous.Column;
                    Consume(TokenType.LBrace, "Expected '{' after 'invariant'");
                    var invariant = new InvariantClause(invLine, invCol);
                    while (!Check(TokenType.RBrace) && !IsAtEnd())
                    {
                        var expr = ParseExpression();
                        invariant.Conditions.Add(expr);
                        Match(TokenType.Semicolon);
                    }
                    Consume(TokenType.RBrace, "Expected '}' after invariant body");
                    contract.Invariants.Add(invariant);
                }
                else if (Match(TokenType.Var) || Match(TokenType.Let) || Match(TokenType.Const))
                {
                    string keyword = Previous.Text;
                    var memberLine = Previous.Line;
                    var memberCol = Previous.Column;
                    Consume(TokenType.Identifier, $"Expected variable name after '{keyword}'");
                    var varName = Previous.Text;

                    string? explicitReturnType = null;
                    if (Match(TokenType.Colon))
                    {
                        explicitReturnType = ParseType();
                    }

                    Consume(TokenType.Assign, "Expected '=' after variable name in contract scope");

                    Expression initExpr = ParseExpression();
                    Consume(TokenType.Semicolon, "Expected ';' after contract-scope variable declaration");

                    if (initExpr is LambdaExpression lambda && keyword != "const")
                    {
                        var function = new FunctionDeclaration(varName, memberLine, memberCol);
                        function.ContractName = name;
                        function.IsStatic = true;
                        function.Access = access;
                        function.Attributes.AddRange(memberAttributes);

                        for (int i = 0; i < lambda.Parameters.Count; i++)
                        {
                            string paramName = lambda.Parameters[i];
                            string paramType = (i < lambda.ParameterTypes.Count && lambda.ParameterTypes[i] != "")
                                ? lambda.ParameterTypes[i]
                                : "int";
                            function.Parameters.Add(new Parameter(paramName, TypeDescriptor.Parse(paramType), lambda.Line, lambda.Column));
                        }

                        if (explicitReturnType != null)
                        {
                            function.ReturnType = TypeDescriptor.Parse(explicitReturnType);
                        }

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
                    else if (keyword == "var")
                    {
                        AddError("Contract-scope 'var' must be initialized with a lambda — use 'let' or 'const' for compile-time constants", initExpr.Line, initExpr.Column);
                    }
                    else
                    {
                        if (memberAttributes.Count > 0)
                        {
                            AddError("Attributes on fields are not supported yet", memberAttributes[0].Line, memberAttributes[0].Column);
                        }

                        bool folded = TryFoldConstant(initExpr, contract, out object? constValue);
                        if (!folded)
                        {
                            AddError($"Initializer of contract-scope '{keyword}' declaration must be a compile-time constant (literals, operators, or constants declared earlier in this contract)", initExpr.Line, initExpr.Column);
                        }
                        else if (explicitReturnType != null && !ConstantTypeMatches(explicitReturnType, constValue))
                        {
                            AddError($"Constant '{varName}' is declared '{explicitReturnType}' but its initializer evaluates to '{ConstValueTypeName(constValue)}'", initExpr.Line, initExpr.Column);
                        }

                        string typeName = explicitReturnType ?? ConstValueTypeName(constValue) ?? "";
                        contract.Fields.Add(new StructField(varName, TypeDescriptor.Parse(typeName), memberLine, memberCol)
                        {
                            IsStatic = true,
                            Access = access,
                            IsConst = true,
                            ConstantValue = constValue,
                        });
                    }
                }
                else if (Match(TokenType.Identifier))
                {
                    if (memberAttributes.Count > 0)
                    {
                        AddError("Attributes on fields are not supported yet", memberAttributes[0].Line, memberAttributes[0].Column);
                    }
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

            // Parse requires clauses before the body
            while (Match(TokenType.Requires))
            {
                var reqLine = Previous.Line;
                var reqCol = Previous.Column;
                var condition = ParseExpression();
                ctor.Requires.Add(new RequiresClause(condition, reqLine, reqCol));
            }

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

            if (Match(TokenType.Assign))
            {
                var exprBody = ParseExpression();
                Consume(TokenType.Semicolon, "Expected ';' after expression-bodied function");
                var block = new BlockStatement(line, column);
                block.Statements.Add(new ReturnStatement(exprBody, exprBody.Line, exprBody.Column));
                function.Body = block;
                return function;
            }

            if (Match(TokenType.Arrow) || Match(TokenType.Colon))
            {
                function.ReturnType = TypeDescriptor.Parse(ParseType());

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

            // Parse requires/ensures clauses before the body
            while (Match(TokenType.Requires))
            {
                var reqLine = Previous.Line;
                var reqCol = Previous.Column;
                var condition = ParseExpression();
                function.Requires.Add(new RequiresClause(condition, reqLine, reqCol));
            }
            while (Match(TokenType.Ensures))
            {
                var ensLine = Previous.Line;
                var ensCol = Previous.Column;
                var condition = ParseExpression();
                function.Ensures.Add(new EnsuresClause(condition, ensLine, ensCol));
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

        private ExtendDeclaration ParseExtend()
        {
            int line = Previous.Line;
            int column = Previous.Column;

            string targetType = ParseType();
            var extend = new ExtendDeclaration(targetType, line, column)
            {
                SourceFile = _sourceFile,
                Namespace = _currentNamespace,
            };

            Consume(TokenType.LBrace, "Expected '{' after extend target type");

            while (!Check(TokenType.RBrace) && !IsAtEnd())
            {
                int startPos = _current;

                AccessModifier access = AccessModifier.Default;
                if (Match(TokenType.Public)) access = AccessModifier.Public;
                else if (Match(TokenType.Private)) access = AccessModifier.Private;
                else if (Match(TokenType.Protected)) access = AccessModifier.Protected;
                else if (Match(TokenType.Internal)) access = AccessModifier.Internal;

                bool isStatic = Match(TokenType.Static);

                if (MatchFn())
                {
                    var function = ParseFunction();
                    function.IsExtension = true;
                    function.ExtensionTargetType = targetType;
                    function.IsStatic = true; // extension methods are always static
                    function.Access = access;
                    extend.Methods.Add(function);
                }
                else
                {
                    AddError($"Expected function declaration in extend block, got {Current.Type}", Current.Line, Current.Column);
                    Advance();
                }

                if (_current == startPos && !IsAtEnd())
                {
                    Advance();
                }
            }

            Consume(TokenType.RBrace, "Expected '}' after extend body");

            return extend;
        }
    }
}
