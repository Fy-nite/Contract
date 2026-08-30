namespace Contract.Compiler.Parsing
{
    public enum TokenType
    {
        // Keywords
        Contract, Fn, If, Else, While, For, Switch, Case, Return, Var, Let, Const, Static,
        Public, Private, Protected, Internal, Null, Import, Constructor, Struct, Export, Fun, Types, Type, New,
        Break, Continue, True, False, Enum, Namespace,
        Try, Catch, Finally, Throw, Match, In,
        Requires, Ensures, Invariant, Extend,
        Is,

        // Literals
        Identifier, IntLiteral, FloatLiteral, StringLiteral, InterpolatedString,

        // Symbols
        LParen, RParen, LBrace, RBrace, LBracket, RBracket,
        Semicolon, Colon, DoubleColon, Comma, Dot, Plus, Minus, Star, Slash, Percent,
        Less, LessEqual, Greater, GreaterEqual, EqualEqual, Bang, BangEqual, Assign, Arrow, Pipe, BitwiseOr,
        PlusEqual, MinusEqual, StarEqual, SlashEqual, PercentEqual, AndAnd, OrOr,
        DotDot, GreaterGreater, LessLess, Question, FatArrow,
        QuestionDot, NullCoalesce,

        // Special
        EOF
    }

    public class Token
    {
        public TokenType Type { get; }
        public string Text { get; }
        public int Line { get; }
        public int Column { get; }

        /// <summary>Length of the token text in characters. End position (exclusive) is Column + Length.</summary>
        public int Length { get; }

        public Token(TokenType type, string text, int line, int column, int length = 0)
        {
            Type = type;
            Text = text;
            Line = line;
            Column = column;
            Length = length;
        }

        /// <summary>Column (1-based) of the first character AFTER this token, i.e. start + length.</summary>
        public int EndColumn => Column + Length;

        public override string ToString() => $"{Type}: '{Text}' at {Line}:{Column}";
    }
}
