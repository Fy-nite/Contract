using System.Collections.Generic;

namespace Contract.Compiler.AST
{
    public abstract class Node
    {
        public int Line { get; }
        public int Column { get; }
        public object? Symbol { get; set; } // For name resolution/linking

        protected Node(int line, int column)
        {
            Line = line;
            Column = column;
        }
    }

    public class Program : Node
    {
        public List<string> Imports { get; } = new();
        public List<ContractDeclaration> Contracts { get; } = new();
        public List<FunctionDeclaration> Functions { get; } = new();

        public Program(int line, int column) : base(line, column) { }
    }

    public class ContractDeclaration : Node
    {
        public string Name { get; }
        public List<ConstructorDeclaration> Constructors { get; } = new();
        public List<Node> Members { get; } = new();

        public ContractDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public enum AccessModifier
    {
        Default,
        Public,
        Private,
        Protected,
        Internal
    }

    public class ConstructorDeclaration : Node
    {
        public List<Parameter> Parameters { get; } = new();
        public BlockStatement? Body { get; set; }

        public ConstructorDeclaration(int line, int column) : base(line, column) { }
    }

    public class FunctionDeclaration : Node
    {
        public string Name { get; }
        public List<Parameter> Parameters { get; } = new();
        public BlockStatement? Body { get; set; }
        public string? ContractName { get; set; }
        public bool IsStatic { get; set; }
        public AccessModifier Access { get; set; } = AccessModifier.Default;

        public FunctionDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class Parameter : Node
    {
        public string Name { get; }
        public string Type { get; }

        public Parameter(string name, string type, int line, int column) : base(line, column)
        {
            Name = name;
            Type = type;
        }
    }

    public abstract class Statement : Node
    {
        protected Statement(int line, int column) : base(line, column) { }
    }

    public class BlockStatement : Statement
    {
        public List<Statement> Statements { get; } = new();

        public BlockStatement(int line, int column) : base(line, column) { }
    }

    public class ExpressionStatement : Statement
    {
        public Expression Expression { get; }

        public ExpressionStatement(Expression expression, int line, int column) : base(line, column)
        {
            Expression = expression;
        }
    }

    public class VariableDeclaration : Statement
    {
        public string Name { get; }
        public string Type { get; }
        public Expression? Initializer { get; }

        public VariableDeclaration(string name, string type, Expression? initializer, int line, int column) : base(line, column)
        {
            Name = name;
            Type = type;
            Initializer = initializer;
        }
    }

    public class IfStatement : Statement
    {
        public Expression Condition { get; }
        public Statement ThenBranch { get; }
        public Statement? ElseBranch { get; }

        public IfStatement(Expression condition, Statement thenBranch, Statement? elseBranch, int line, int column) : base(line, column)
        {
            Condition = condition;
            ThenBranch = thenBranch;
            ElseBranch = elseBranch;
        }
    }

    public class WhileStatement : Statement
    {
        public Expression Condition { get; }
        public Statement Body { get; }

        public WhileStatement(Expression condition, Statement body, int line, int column) : base(line, column)
        {
            Condition = condition;
            Body = body;
        }
    }

    public class ReturnStatement : Statement
    {
        public Expression? Value { get; }

        public ReturnStatement(Expression? value, int line, int column) : base(line, column)
        {
            Value = value;
        }
    }

    public class SwitchStatement : Statement
    {
        public Expression Expression { get; }
        public List<SwitchCase> Cases { get; } = new();

        public SwitchStatement(Expression expression, int line, int column) : base(line, column)
        {
            Expression = expression;
        }
    }

    public class SwitchCase : Node
    {
        public int? Value { get; } // null for 'else' case
        public List<Statement> Statements { get; } = new();

        public SwitchCase(int? value, int line, int column) : base(line, column)
        {
            Value = value;
        }
    }

    public abstract class Expression : Node
    {
        protected Expression(int line, int column) : base(line, column) { }
    }

    public class LiteralExpression : Expression
    {
        public object Value { get; }

        public LiteralExpression(object value, int line, int column) : base(line, column)
        {
            Value = value;
        }
    }

    public class IdentifierExpression : Expression
    {
        public string Name { get; }

        public IdentifierExpression(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class BinaryExpression : Expression
    {
        public Expression Left { get; }
        public string Operator { get; }
        public Expression Right { get; }

        public BinaryExpression(Expression left, string op, Expression right, int line, int column) : base(line, column)
        {
            Left = left;
            Operator = op;
            Right = right;
        }
    }

    public class CallExpression : Expression
    {
        public Expression Callee { get; }
        public List<Expression> Arguments { get; } = new();

        public CallExpression(Expression callee, int line, int column) : base(line, column)
        {
            Callee = callee;
        }
    }

    public class MemberExpression : Expression
    {
        public Expression Object { get; }
        public string Property { get; }

        public MemberExpression(Expression obj, string property, int line, int column) : base(line, column)
        {
            Object = obj;
            Property = property;
        }
    }

    public class IndexExpression : Expression
    {
        public Expression Target { get; }
        public Expression Index { get; }

        public IndexExpression(Expression target, Expression index, int line, int column) : base(line, column)
        {
            Target = target;
            Index = index;
        }
    }
}