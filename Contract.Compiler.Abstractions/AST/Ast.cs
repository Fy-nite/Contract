using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// An attribute application: <code>&lt;Name(arg1, Key: value)&gt;</code>.
    /// Arguments are kept as raw source text (strings retain their quotes),
    /// matching how the IR stores attribute arguments in its string pool.
    /// Named arguments are stored separately in <see cref="NamedArguments"/>
    /// and encoded as <c>@Key=Value</c> in the string pool.
    /// </summary>
    public class AttributeUsage : Node
    {
        public string Name { get; }
        public List<string> Arguments { get; } = new();
        public Dictionary<string, string> NamedArguments { get; } = new(StringComparer.OrdinalIgnoreCase);

        public AttributeUsage(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class Program : Node
    {
        public List<string> Imports { get; } = new();             // file imports: "path/to/file.ct"
        public List<string> NamespaceImports { get; } = new();    // namespace imports: ObjektRT.Stdlib.System
        public List<ContractDeclaration> Contracts { get; } = new();
        public List<StructDeclaration> Structs { get; } = new();
        public List<EnumDeclaration> Enums { get; } = new();
        public List<FunctionDeclaration> Functions { get; } = new();
        public List<ExtendDeclaration> Extensions { get; } = new();

        /// <summary>
        /// Compiled modules pulled in via <c>import "lib.orbt";</c> (DLL-style
        /// references). Their types are synthesized into the declaration lists
        /// for analysis; the codegen statically links these module bodies into
        /// the output.
        /// </summary>
        public List<ObjektRT.Core.AST.ModuleNode> ExternalModules { get; } = new();

        public Program(int line, int column) : base(line, column) { }
    }

    public class ContractDeclaration : Node
    {
        public string Name { get; }
        public bool IsExported { get; set; }
        /// <summary>Absolute path of the .ct file that declared this contract (set by the parser).</summary>
        public string? SourceFile { get; set; }
        /// <summary>Java-style package, from `namespace com.example;`. Null when undeclared.</summary>
        public string? Namespace { get; set; }
        /// <summary>True when this type came from an imported compiled module (statically linked, not re-emitted).</summary>
        public bool IsExternal { get; set; }
        /// <summary>Namespace-qualified wire name — <c>com.example.Foo</c> or just <c>Foo</c> when unnamed.</summary>
        public string FullName => Namespace == null ? Name : $"{Namespace}.{Name}";
        /// <summary>Type parameters for <c>contract Box&lt;T&gt;</c> — empty for non-generic contracts.</summary>
        public List<string> TypeParameters { get; } = new();
        /// <summary>True when this contract declares type parameters (<c>contract Box&lt;T&gt;</c>).</summary>
        public bool IsGeneric => TypeParameters.Count > 0;
        public List<ConstructorDeclaration> Constructors { get; } = new();
        public List<StructField> Fields { get; } = new();
        public List<Node> Members { get; } = new();
        public List<AttributeUsage> Attributes { get; } = new();

        /// <summary>Single-inheritance base type name (C#-style), or null.</summary>
        public string? BaseTypeName { get; set; }

        /// <summary>
        /// Additional parent contracts after the first (<c>Contract A : B, C</c>).
        /// These behave as interfaces: they must declare no fields or
        /// constructors, and the deriving contract implements their abstract
        /// methods (FEATURE_PROPOSALS §6 — interfaces via contract multiple
        /// inheritance). Recorded in the IR via Implements metadata.
        /// </summary>
        public List<string> InterfaceNames { get; } = new();

        /// <summary>True when any instance method lacks a body (abstract declaration).</summary>
        public bool HasAbstractMethods => Members.OfType<FunctionDeclaration>().Any(f => f.IsInstance && f.Body == null);

        /// <summary>
        /// Abstract methods this contract still must implement: declarations
        /// without bodies found on itself, its primary base chain, and its
        /// secondary parents. Filled by the analyzer.
        /// </summary>
        public List<FunctionDeclaration> PendingAbstractMethods { get; } = new();

        /// <summary>True when this contract is a sum-type base synthesized from `type X { ... }`.</summary>
        public bool IsSumTypeBase { get; set; }

        /// <summary>Variant names (in tag order) when <see cref="IsSumTypeBase"/>.</summary>
        public List<string> SumVariants { get; } = new();

        /// <summary>The sum base's name when this contract is a synthesized variant of a sum type.</summary>
        public string? SumTypeOf { get; set; }

        /// <summary>Tag index within the sum type when <see cref="SumTypeOf"/> is set.</summary>
        public int SumVariantIndex { get; set; }

        /// <summary>True when this contract (transitively) inherits from the built-in Attribute type. Set by the analyzer.</summary>
        public bool IsAttributeType { get; set; }

        /// <summary>Invariant clauses (design-by-contract): expressions that must hold after every field write.</summary>
        public List<InvariantClause> Invariants { get; } = new();

        /// <summary>
        /// When the contract is marked <c>&lt;NativeBinding("Module")&gt;</c>, the
        /// name of the host module its members dispatch to. Calls to this
        /// contract's methods become native calls to <c>Module.Method</c>
        /// (instance calls pass the receiver as argument 0), and
        /// <c>new Contract()</c> becomes <c>Module.Create</c>.
        /// </summary>
        public string? NativeBindingName { get; set; }

        /// <summary>
        /// When the contract is marked <c>&lt;ClrImport("System.Math")&gt;</c>, the
        /// CLR type whose public static methods its <c>static fn</c>s map to.
        /// Unlike <c>NativeBinding</c> no host-side <c>[ClassBinding]</c> wrapper
        /// is needed: calls emit <c>Type.Method</c> targets and the runtime
        /// resolves them via CLR reflection.
        /// </summary>
        public string? ClrImportType { get; set; }

        /// <summary>
        /// When the contract is marked <c>&lt;DllImport("user32.dll")&gt;</c>, the
        /// native library its <c>static fn</c>s P/Invoke. Method signatures are
        /// emitted into the module metadata so the runtime's DllImportResolver
        /// can generate the marshalling bridge.
        /// </summary>
        public string? DllImportLibrary { get; set; }

        /// <summary>
        /// When the contract is marked <c>&lt;ClrImport(..., Path: "Foo.dll")&gt;</c>,
        /// the path to a project-local .NET assembly containing the target type.
        /// Resolved relative to the source file's directory.
        /// </summary>
        public string? AssemblyImportPath { get; set; }

        public ContractDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class StructDeclaration : Node
    {
        public string Name { get; }
        public bool IsExported { get; set; }
        /// <summary>Absolute path of the .ct file that declared this struct (set by the parser).</summary>
        public string? SourceFile { get; set; }
        /// <summary>Java-style package, from `namespace com.example;`. Null when undeclared.</summary>
        public string? Namespace { get; set; }
        /// <summary>True when this type came from an imported compiled module.</summary>
        public bool IsExternal { get; set; }
        /// <summary>Namespace-qualified wire name.</summary>
        public string FullName => Namespace == null ? Name : $"{Namespace}.{Name}";
        public List<StructField> Fields { get; } = new();
        public List<FunctionDeclaration> Methods { get; } = new();
        public List<AttributeUsage> Attributes { get; } = new();

        public StructDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    /// <summary>
    /// An enum declaration: <c>enum Color { Red, Green, Blue }</c>. Members are
    /// named constants that fold to their zero-based index at compile time
    /// (<c>Color.Red</c> emits <c>ldc.i4 0</c>); the type itself is emitted to
    /// the IR for type checking and reflection.
    /// </summary>
    public class EnumDeclaration : Node
    {
        public string Name { get; }
        public List<string> Members { get; } = new();
        public bool IsExported { get; set; }
        /// <summary>Absolute path of the .ct file that declared this enum (set by the parser).</summary>
        public string? SourceFile { get; set; }
        /// <summary>Java-style package, from `namespace com.example;`. Null when undeclared.</summary>
        public string? Namespace { get; set; }
        /// <summary>True when this type came from an imported compiled module.</summary>
        public bool IsExternal { get; set; }
        /// <summary>Namespace-qualified wire name.</summary>
        public string FullName => Namespace == null ? Name : $"{Namespace}.{Name}";
        public List<AttributeUsage> Attributes { get; } = new();

        public EnumDeclaration(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    public class StructField : Node
    {
        public string Name { get; }
        public TypeDescriptor Type { get; }

        /// <summary>True for <c>static name: type;</c> declarations — shared state on the contract.</summary>
        public bool IsStatic { get; set; }

        /// <summary>Access level from <c>public</c>/<c>private</c>/<c>protected</c>/<c>internal</c> modifiers.</summary>
        public AccessModifier Access { get; set; } = AccessModifier.Default;

        /// <summary>
        /// True for comptime constants (FEATURE_PROPOSALS §15): a contract-scope
        /// <c>let X: T = &lt;const-expr&gt;;</c> / <c>const X: T = ...;</c>. The
        /// initializer folds at parse time and every read emits the folded
        /// literal instead of loading the field.
        /// </summary>
        public bool IsConst { get; set; }

        /// <summary>The folded compile-time value of a constant field (int, double, string, bool, or null).</summary>
        public object? ConstantValue { get; set; }

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
        /// <summary>Absolute path of the .ct file that declared this constructor (set by the parser).</summary>
        public string? SourceFile { get; set; }
        public List<Parameter> Parameters { get; } = new();
        public BlockStatement? Body { get; set; }
        public List<AttributeUsage> Attributes { get; } = new();

        /// <summary>Pre-condition clauses (design-by-contract).</summary>
        public List<RequiresClause> Requires { get; } = new();

        public ConstructorDeclaration(int line, int column) : base(line, column) { }
    }

    public class FunctionDeclaration : Node
    {
        public string Name { get; }
        public bool IsExported { get; set; }
        /// <summary>Absolute path of the .ct file that declared this function (set by the parser).</summary>
        public string? SourceFile { get; set; }
        public List<Parameter> Parameters { get; } = new();
        public BlockStatement? Body { get; set; }
        public string? ContractName { get; set; }
        public bool IsStatic { get; set; }
        public bool IsInstance { get; set; }
        public AccessModifier Access { get; set; } = AccessModifier.Default;
        /// <summary>Type parameters for <c>fn identity&lt;T&gt;(x: T) -&gt; T</c> — empty for non-generic functions.</summary>
        public List<string> TypeParameters { get; } = new();
        /// <summary>True when this function declares type parameters.</summary>
        public bool IsGeneric => TypeParameters.Count > 0;
        /// <summary>
        /// Concrete contract type arguments for a substituted generic call
        /// (e.g. <c>b.get()</c> on <c>Box&lt;int&gt;</c> → <c>[int]</c>). Set by
        /// the analyzer on the substituted symbol; the codegen uses it for the
        /// materialized declaring name and wire signature.
        /// </summary>
        public List<TypeDescriptor> TypeArguments { get; } = new();
        public TypeDescriptor? ReturnType { get; set; }
        public List<AttributeUsage> Attributes { get; } = new();

        /// <summary>Pre-condition clauses (design-by-contract).</summary>
        public List<RequiresClause> Requires { get; } = new();
        /// <summary>Post-condition clauses (design-by-contract).</summary>
        public List<EnsuresClause> Ensures { get; } = new();

        /// <summary>True when this is an extension method (declared inside an extend block).</summary>
        public bool IsExtension { get; set; }
        /// <summary>The target type of the extension method, when <see cref="IsExtension"/> is true.</summary>
        public string? ExtensionTargetType { get; set; }

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
        public Expression? Initializer { get; set; }
        /// <summary>True when the source wrote an explicit <c>: T</c> annotation.
        /// The semantic analyzer overwrites <see cref="Type"/> with inferred
        /// types, so this is the only record of what the user actually typed.</summary>
        public bool HasExplicitType { get; set; }
        /// <summary>
        /// When non-empty, this is a destructuring declaration
        /// (<c>var (a, b) = f();</c>) and <see cref="Name"/> is unused. Each
        /// element is a variable bound to the corresponding tuple element.
        /// </summary>
        public List<string> Names { get; } = new();
        /// <summary>
        /// True for <c>var</c> declarations (mutable), false for <c>let</c>
        /// (immutable).  Enforced by the semantic analyzer: assigning to a
        /// let-bound variable is a compile error.
        /// </summary>
        public bool IsMutable { get; set; } = true;

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

    /// <summary>
    /// <c>for x in iterable { body }</c> (FEATURE_PROPOSALS §5). The analyzer
    /// records <see cref="ResolvedElementType"/> (and <see cref="IsDictionary"/>
    /// for the <c>(k, v)</c> pair form); codegen picks the index protocol
    /// (array / List / Dict) from those.
    /// </summary>
    public class ForInStatement : Statement
    {
        public string Variable { get; }
        /// <summary>Second binding for the Dict pair form <c>for (k, v) in d</c>; null otherwise.</summary>
        public string? ValueVariable { get; }
        public Expression Iterable { get; }
        public Statement Body { get; }
        /// <summary>Element type of the iterable, set by the analyzer.</summary>
        public TypeDescriptor? ResolvedElementType { get; set; }
        /// <summary>True when iterating a Dict with a (key, value) pair binding.</summary>
        public bool IsDictionary { get; set; }
        /// <summary>True when the iterable is statically known to be an array (ldlen/ldelem protocol).</summary>
        public bool IsArray { get; set; }

        public ForInStatement(string variable, string? valueVariable, Expression iterable, Statement body, int line, int column)
            : base(line, column)
        {
            Variable = variable;
            ValueVariable = valueVariable;
            Iterable = iterable;
            Body = body;
        }
    }

    /// <summary>
    /// An <c>if</c> used as a value: <c>let m = if (a &gt; b) { a } else { b };</c>
    /// (FEATURE_PROPOSALS §3). Both branches are expressions; chains nest via
    /// <see cref="ElseBranch"/> holding another IfExpression.
    /// </summary>
    public class IfExpression : Expression
    {
        public Expression Condition { get; }
        public Expression ThenBranch { get; }
        /// <summary>The else arm — an expression, or another IfExpression for else-if chains.</summary>
        public Expression ElseBranch { get; set; }

        public IfExpression(Expression condition, Expression thenBranch, Expression elseBranch, int line, int column)
            : base(line, column)
        {
            Condition = condition;
            ThenBranch = thenBranch;
            ElseBranch = elseBranch;
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

    // ── Match expressions (FEATURE_PROPOSALS §1) ───────────────────

    /// <summary>
    /// A value-producing match: <c>match (x) { 1 | 2 => "a", n if n > 0 => "b", _ => "c" }</c>.
    /// Arms are tried top to bottom; the first pattern that matches (and whose
    /// guard holds) wins. Lowers to a scrutinee temp plus a nested if-chain.
    /// </summary>
    public class MatchExpression : Expression
    {
        public Expression Scrutinee { get; }
        public List<MatchArm> Arms { get; } = new();
        /// <summary>Resolved sum-type base when the scrutinee is a variant (set by the analyzer).</summary>
        public string? SumTypeName { get; set; }

        public MatchExpression(Expression scrutinee, int line, int column) : base(line, column)
        {
            Scrutinee = scrutinee;
        }
    }

    /// <summary>One arm of a match: patterns (or-patterns list several), an optional
    /// guard, and the result expression.</summary>
    public class MatchArm : Node
    {
        public List<MatchPattern> Patterns { get; } = new();
        public Expression? Guard { get; set; }
        public Expression Result { get; set; }
        /// <summary>
        /// Names bound by this arm's binding/variant patterns → their types
        /// (filled by the analyzer). Visible in the guard and result.
        /// </summary>
        public Dictionary<string, TypeDescriptor> BoundNames { get; } = new();

        public MatchArm(int line, int column) : base(line, column)
        {
            Result = null!;
        }
    }

    public abstract class MatchPattern : Node
    {
        protected MatchPattern(int line, int column) : base(line, column) { }
    }

    /// <summary>A literal pattern: 1, "ok", true, null.</summary>
    public class LiteralPattern : MatchPattern
    {
        public object Value { get; }
        public LiteralPattern(object value, int line, int column) : base(line, column)
        {
            Value = value;
        }
    }

    /// <summary>The <c>_</c> catch-all.</summary>
    public class WildcardPattern : MatchPattern
    {
        public WildcardPattern(int line, int column) : base(line, column) { }
    }

    /// <summary>
    /// A binding pattern: <c>n if n &gt;= 100</c> binds the scrutinee to the
    /// named variable for the arm's guard and result.
    /// </summary>
    public class BindingPattern : MatchPattern
    {
        public string Name { get; }
        public BindingPattern(string name, int line, int column) : base(line, column)
        {
            Name = name;
        }
    }

    /// <summary>
    /// A variant pattern over a sum type: <c>Circle(r)</c>, <c>Rect(w, h)</c>,
    /// <c>Unit</c>. Tests the tag and binds the variant's fields positionally.
    /// <see cref="VariantName"/> resolves to the declaring sum type by the analyzer,
    /// which also fills <see cref="VariantIndex"/> and <see cref="BoundFields"/>.
    /// </summary>
    public class VariantPattern : MatchPattern
    {
        public string VariantName { get; }
        /// <summary>Sub-patterns aligned with the variant's parameters ('_' allowed).</summary>
        public List<MatchPattern> Arguments { get; } = new();
        /// <summary>Fully-qualified variant contract name ("Shape.Circle"), set by the analyzer.</summary>
        public string? ResolvedVariantName { get; set; }
        /// <summary>The variant's tag index within its sum type, set by the analyzer.</summary>
        public int VariantIndex { get; set; }
        /// <summary>Field names bound positionally from the variant's parameters.</summary>
        public List<string> BoundFields { get; } = new();

        public VariantPattern(string variantName, int line, int column) : base(line, column)
        {
            VariantName = variantName;
        }
    }

    // ── Error handling ─────────────────────────────────────────────

    public class CatchClause : Node
    {
        public string? ExceptionType { get; }
        public string ExceptionVar { get; }
        public BlockStatement Body { get; }

        public CatchClause(string? exceptionType, string exceptionVar, BlockStatement body, int line, int column)
            : base(line, column)
        {
            ExceptionType = exceptionType;
            ExceptionVar = exceptionVar;
            Body = body;
        }
    }

    public class TryStatement : Statement
    {
        public BlockStatement TryBlock { get; }
        public List<CatchClause> CatchClauses { get; } = new();
        public BlockStatement? FinallyBlock { get; set; }

        public TryStatement(BlockStatement tryBlock, int line, int column) : base(line, column)
        {
            TryBlock = tryBlock;
        }
    }

    public class ThrowStatement : Statement
    {
        public Expression Value { get; }

        public ThrowStatement(Expression value, int line, int column) : base(line, column)
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

    /// <summary>
    /// A tuple literal: <c>(a, b, c)</c>. Used as a multi-value return
    /// (<c>return (true, value);</c>). On the wire it lowers to an
    /// <c>object[]</c>.
    /// </summary>
    public class TupleLiteralExpression : Expression
    {
        public List<Expression> Elements { get; } = new();

        public TupleLiteralExpression(int line, int column) : base(line, column) { }
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

    /// <summary>A ternary expression: <c>cond ? then : else</c>.</summary>
    public class TernaryExpression : Expression
    {
        public Expression Condition { get; }
        public Expression ThenBranch { get; }
        public Expression ElseBranch { get; }

        public TernaryExpression(Expression condition, Expression thenBranch, Expression elseBranch, int line, int column) : base(line, column)
        {
            Condition = condition;
            ThenBranch = thenBranch;
            ElseBranch = elseBranch;
        }
    }

    public class CallExpression : Expression
    {
        public Expression Callee { get; }
        public List<Expression> Arguments { get; } = new();
        /// <summary>Explicit type arguments for <c>first&lt;int&gt;(xs)</c> — empty when inferred.</summary>
        public List<TypeDescriptor> TypeArguments { get; } = new();

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
        /// <summary>Explicit type arguments for <c>Box&lt;int&gt;::reset()</c> — empty when inferred.</summary>
        public List<TypeDescriptor> TypeArguments { get; } = new();

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
        /// <summary>
        /// The type to allocate. Written form initially (short or
        /// module-qualified, e.g. <c>Terminal.Terminal</c>); the semantic
        /// analyzer rewrites it to the fully-qualified wire name once
        /// namespace/import resolution runs.
        /// </summary>
        public string TypeName { get; set; }
        /// <summary>Explicit type arguments for <c>new Box&lt;int&gt;(5)</c> — empty when the type isn't generic.</summary>
        public List<TypeDescriptor> TypeArguments { get; } = new();
        public Expression? Size { get; set; }
        /// <summary>Constructor arguments — non-null for `new Type(args)`.</summary>
        public List<Expression> Arguments { get; } = new();

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

    /// <summary>
    /// An inline range in a for-in header: <c>start .. end</c> (end-exclusive),
    /// <c>start ..= end</c> (inclusive), with an optional <c>by step</c>.
    /// Bounds may be arbitrary expressions. Codegen desugars to the C-style
    /// loop over hidden temps.
    /// </summary>
    public class RangeExpression : Expression
    {
        public Expression Start { get; }
        public Expression End { get; }
        /// <summary>True for <c>..=</c> (the end value is produced).</summary>
        public bool Inclusive { get; }
        public Expression? Step { get; }

        public RangeExpression(Expression start, Expression end, bool inclusive, Expression? step, int line, int column)
            : base(line, column)
        {
            Start = start;
            End = end;
            Inclusive = inclusive;
            Step = step;
        }
    }

    /// <summary>
    /// A type check expression: <c>expr is TypeName</c>.
    /// Evaluates to <c>true</c> when the value is of the specified type.
    /// </summary>
    public class IsExpression : Expression
    {
        public Expression Value { get; }
        public string TypeName { get; }

        public IsExpression(Expression value, string typeName, int line, int column) : base(line, column)
        {
            Value = value;
            TypeName = typeName;
        }
    }

    /// <summary>
    /// A null coalescing expression: <c>expr ?? defaultValue</c>.
    /// Returns the left operand if non-null, otherwise the right operand.
    /// </summary>
    public class NullCoalesceExpression : Expression
    {
        public Expression Left { get; }
        public Expression Right { get; }

        public NullCoalesceExpression(Expression left, Expression right, int line, int column) : base(line, column)
        {
            Left = left;
            Right = right;
        }
    }

    /// <summary>
    /// A member expression with null-conditional access: <c>expr?.member</c>.
    /// Returns null when the receiver is null, otherwise accesses the member.
    /// </summary>
    public class SafeAccessExpression : Expression
    {
        public Expression Object { get; }
        public string Property { get; }

        public SafeAccessExpression(Expression obj, string property, int line, int column) : base(line, column)
        {
            Object = obj;
            Property = property;
        }
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

    // ── Design-by-contract clauses ─────────────────────────────────

    /// <summary>
    /// A pre-condition clause: <c>requires expr</c>.
    /// Attached to functions and constructors.
    /// </summary>
    public class RequiresClause : Node
    {
        public Expression Condition { get; }

        public RequiresClause(Expression condition, int line, int column) : base(line, column)
        {
            Condition = condition;
        }
    }

    /// <summary>
    /// A post-condition clause: <c>ensures expr</c>.
    /// The identifier <c>result</c> in the expression refers to the return value.
    /// Attached to non-void functions only.
    /// </summary>
    public class EnsuresClause : Node
    {
        public Expression Condition { get; }

        public EnsuresClause(Expression condition, int line, int column) : base(line, column)
        {
            Condition = condition;
        }
    }

    /// <summary>
    /// A class invariant: <c>invariant { expr; ... }</c>.
    /// All expressions must hold after every field write.
    /// Attached to contracts.
    /// </summary>
    public class InvariantClause : Node
    {
        public List<Expression> Conditions { get; } = new();

        public InvariantClause(int line, int column) : base(line, column) { }
    }

    // ── Extension methods ──────────────────────────────────────────

    /// <summary>
    /// An extension method block: <c>extend Type { fn ... }</c>.
    /// Declares methods that can be called as instance methods on the target type.
    /// </summary>
    public class ExtendDeclaration : Node
    {
        public string TargetType { get; }
        public List<FunctionDeclaration> Methods { get; } = new();
        public string? SourceFile { get; set; }
        public string? Namespace { get; set; }

        public ExtendDeclaration(string targetType, int line, int column) : base(line, column)
        {
            TargetType = targetType;
        }
    }
}
