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
        public List<string> Imports { get; } = new();             // file imports: "path/to/file.ct"
        public List<string> NamespaceImports { get; } = new();    // namespace imports: ObjektRT.Stdlib.System
        public List<ContractDeclaration> Contracts { get; } = new();
        public List<StructDeclaration> Structs { get; } = new();
        public List<FunctionDeclaration> Functions { get; } = new();

        public Program(int line, int column) : base(line, column) { }
    }

    public class ContractDeclaration : Node
    {
        public string Name { get; }
        public bool IsExported { get; set; }
        public List<ConstructorDeclaration> Constructors { get; } = new();
        public List<StructField> Fields { get; } = new();
        public List<Node> Members { get; } = new();

        public ContractDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class StructDeclaration : Node
    {
        public string Name { get; }
        public bool IsExported { get; set; }
        public List<StructField> Fields { get; } = new();
        public List<FunctionDeclaration> Methods { get; } = new();

        public StructDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class StructField : Node
    {
        public string Name { get; }
        public TypeDescriptor Type { get; }

        public StructField(string name, TypeDescriptor type, int line, int column) : base(line, column)
        {
            Name = name;
            Type = type;
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
        public bool IsExported { get; set; }
        public List<Parameter> Parameters { get; } = new();
        public BlockStatement? Body { get; set; }
        public string? ContractName { get; set; }
        public bool IsStatic { get; set; }
        public bool IsInstance { get; set; }
        public AccessModifier Access { get; set; } = AccessModifier.Default;
        public TypeDescriptor? ReturnType { get; set; }

        public FunctionDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class Parameter : Node
    {
        public string Name { get; }
        public TypeDescriptor Type { get; }

        public Parameter(string name, TypeDescriptor type, int line, int column) : base(line, column)
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
        public TypeDescriptor Type { get; set; }
        public Expression? Initializer { get; }

        public VariableDeclaration(string name, TypeDescriptor type, Expression? initializer, int line, int column) : base(line, column)
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

    public class ForStatement : Statement
    {
        public Statement? Initializer { get; }
        public Expression? Condition { get; }
        public Expression? Update { get; }
        public Statement Body { get; }

        public ForStatement(Statement? initializer, Expression? condition, Expression? update, Statement body, int line, int column) : base(line, column)
        {
            Initializer = initializer;
            Condition = condition;
            Update = update;
            Body = body;
        }
    }

    public class BreakStatement : Statement
    {
        public BreakStatement(int line, int column) : base(line, column) { }
    }

    public class ContinueStatement : Statement
    {
        public ContinueStatement(int line, int column) : base(line, column) { }
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
        public string? StringValue { get; set; } // set for string cases like case "start":
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
        /// <summary>Resolved result type (e.g. string for concat), set during semantic analysis.</summary>
        public TypeDescriptor? ResolvedType { get; set; }

        public BinaryExpression(Expression left, string op, Expression right, int line, int column) : base(line, column)
        {
            Left = left;
            Operator = op;
            Right = right;
        }
    }

    public class UnaryExpression : Expression
    {
        public Expression Operand { get; }
        public string Operator { get; }

        public UnaryExpression(Expression operand, string op, int line, int column) : base(line, column)
        {
            Operand = operand;
            Operator = op;
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

    public class ScopedAccessExpression : Expression
    {
        public string Module { get; }
        public string Member { get; }

        public ScopedAccessExpression(string module, string member, int line, int column) : base(line, column)
        {
            Module = module;
            Member = member;
        }
    }

    public class LambdaExpression : Expression
    {
        public List<string> Parameters { get; } = new();
        /// <summary>Optional param type annotations (aligned with Parameters; "" when absent).</summary>
        public List<string> ParameterTypes { get; } = new();
        /// <summary>Expression body (fun x -> expr).</summary>
        public Expression? Body { get; }
        /// <summary>Block body (fun (x) -> { stmts }). Mutually exclusive with Body.</summary>
        public BlockStatement? BlockBody { get; }

        public LambdaExpression(List<string> parameters, Expression body, int line, int column) : base(line, column)
        {
            Parameters = parameters;
            Body = body;
        }

        public LambdaExpression(List<string> parameters, List<string>? parameterTypes, Expression? body, BlockStatement? blockBody, int line, int column) : base(line, column)
        {
            Parameters = parameters;
            if (parameterTypes != null) ParameterTypes = parameterTypes;
            Body = body;
            BlockBody = blockBody;
        }
    }

    public class NewExpression : Expression
    {
        public string TypeName { get; }
        public Expression? Size { get; }

        public NewExpression(string typeName, int line, int column, Expression? size = null) : base(line, column)
        {
            TypeName = typeName;
            Size = size;
        }
    }

    public class ArrayLiteralExpression : Expression
    {
        public List<Expression> Elements { get; } = new();
        /// <summary>Element type, resolved during semantic analysis.</summary>
        public TypeDescriptor? ElementType { get; set; }

        public ArrayLiteralExpression(int line, int column) : base(line, column) { }
    }

    public class PipeExpression : Expression
    {
        public Expression Left { get; }
        public Expression Right { get; }

        public PipeExpression(Expression left, Expression right, int line, int column) : base(line, column)
        {
            Left = left;
            Right = right;
        }
    }
}
