using System;
using System.Collections.Generic;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler.Parsing
{
    public class Lexer
    {
        private readonly string _source;
        private int _position;
        private int _line = 1;
        private int _column = 1;
        private readonly DiagnosticBag _diagnostics;
        private readonly string? _sourceFile;

        private static readonly Dictionary<string, TokenType> Keywords = new()
        {
            ["Contract"] = TokenType.Contract,
            ["if"] = TokenType.If,
            ["else"] = TokenType.Else,
            ["while"] = TokenType.While,
            ["switch"] = TokenType.Switch,
            ["case"] = TokenType.Case,
            ["return"] = TokenType.Return,
            ["var"] = TokenType.Var,
            ["let"] = TokenType.Let,
            ["const"] = TokenType.Const,
            ["fun"] = TokenType.Fun,
            ["static"] = TokenType.Static,
            ["public"] = TokenType.Public,
            ["private"] = TokenType.Private,
            ["protected"] = TokenType.Protected,
            ["internal"] = TokenType.Internal,
            ["null"] = TokenType.Null,
            ["import"] = TokenType.Import,
            ["constructor"] = TokenType.Constructor,
            ["struct"] = TokenType.Struct,
            ["export"] = TokenType.Export,
            ["Types"] = TokenType.Types,
            ["type"] = TokenType.Type,
            ["new"] = TokenType.New,
            ["for"] = TokenType.For,
            ["break"] = TokenType.Break,
            ["continue"] = TokenType.Continue,
            ["true"] = TokenType.True,
            ["false"] = TokenType.False,
            ["enum"] = TokenType.Enum,
            ["namespace"] = TokenType.Namespace,
            ["try"] = TokenType.Try,
            ["catch"] = TokenType.Catch,
            ["finally"] = TokenType.Finally,
            ["throw"] = TokenType.Throw,
            ["match"] = TokenType.Match,
            ["in"] = TokenType.In,
            ["requires"] = TokenType.Requires,
            ["ensures"] = TokenType.Ensures,
            ["invariant"] = TokenType.Invariant,
            ["extend"] = TokenType.Extend,
            ["is"] = TokenType.Is
        };

        public Lexer(string source, DiagnosticBag diagnostics, string? sourceFile = null)
        {
            _source = source;
            _diagnostics = diagnostics;
            _sourceFile = sourceFile;
        }

        public IEnumerable<Token> Tokenize()
        {
            while (_position < _source.Length)
            {
                SkipWhitespace();

                if (_position >= _source.Length)
                    break;

                char c = _source[_position];

                if (char.IsLetter(c) || c == '_' || c == '@')
                {
                    yield return ReadIdentifier();
                }
                else if (char.IsDigit(c))
                {
                    yield return ReadNumber();
                }
                else if (c == '"')
                {
                    yield return ReadString();
                }
                else
                {
                    var token = ReadSymbol();
                    if (token != null)
                        yield return token;
                }
            }

            yield return new Token(TokenType.EOF, "", _line, _column);
        }

        private void SkipWhitespace()
        {
            while (_position < _source.Length)
            {
                char c = _source[_position];
                if (c == ' ' || c == '\t' || c == '\r')
                {
                    _position++;
                    _column++;
                }
                else if (c == '\n')
                {
                    _position++;
                    _line++;
                    _column = 1;
                }
                else if (c == '/' && _position + 1 < _source.Length && _source[_position + 1] == '/')
                {
                    // Skip comments
                    while (_position < _source.Length && _source[_position] != '\n')
                    {
                        _position++;
                    }
                }
                else if (c == '#' && _column == 1)
                {
                    // Look for '#line'
                    if (_source.Substring(_position).StartsWith("#line"))
                    {
                        _position += 5;
                        _column += 5;
                        SkipWhitespaceInline();
                        
                        // Parse line number
                        int start = _position;
                        while (_position < _source.Length && char.IsDigit(_source[_position]))
                        {
                            _position++;
                            _column++;
                        }
                        if (int.TryParse(_source.Substring(start, _position - start), out int newLine))
                        {
                            _line = newLine;
                        }
                        // Skip rest of line
                        while (_position < _source.Length && _source[_position] != '\n')
                        {
                            _position++;
                        }
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private void SkipWhitespaceInline()
        {
            while (_position < _source.Length && (_source[_position] == ' ' || _source[_position] == '\t'))
            {
                _position++;
                _column++;
            }
        }

        private Token ReadIdentifier()
        {
            int start = _position;
            int startColumn = _column;

            while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_'))
            {
                _position++;
                _column++;
            }

            string text = _source.Substring(start, _position - start);

            TokenType type = Keywords.TryGetValue(text, out var keywordType) ? keywordType : TokenType.Identifier;

            return new Token(type, text, _line, startColumn, _position - start);
        }

        private Token ReadNumber()
        {
            int start = _position;
            int startColumn = _column;

            while (_position < _source.Length && char.IsDigit(_source[_position]))
            {
                _position++;
                _column++;
            }

            // Float literals: digits '.' digits (e.g. 3.14, 2.0)
            if (_position + 1 < _source.Length && _source[_position] == '.' && char.IsDigit(_source[_position + 1]))
            {
                _position++; // '.'
                _column++;
                while (_position < _source.Length && char.IsDigit(_source[_position]))
                {
                    _position++;
                    _column++;
                }
            }

            string text = _source.Substring(start, _position - start);
            bool isFloat = text.Contains('.');
            return new Token(isFloat ? TokenType.FloatLiteral : TokenType.IntLiteral, text, _line, startColumn, _position - start);
        }

        private Token ReadString()
        {
            int start = _position;
            int startColumn = _column;
            _position++; // Skip opening quote
            _column++;

            while (_position < _source.Length && _source[_position] != '"')
            {
                if (_source[_position] == '\\' && _position + 1 < _source.Length)
                {
                    _position += 2; // Skip escape sequence
                    _column += 2;
                }
                else
                {
                    _position++;
                    _column++;
                }
            }

            if (_position < _source.Length)
            {
                _position++; // Skip closing quote
                _column++;
            }

            string text = _source.Substring(start, _position - start);
            bool interpolated = ContainsInterpolation(text);
            return new Token(interpolated ? TokenType.InterpolatedString : TokenType.StringLiteral, text, _line, startColumn, _position - start);
        }

        private static bool ContainsInterpolation(string text)
        {
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] != '{') continue;
                int j = i + 1;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                if (j < text.Length && text[j] == '}') return true;
            }
            return false;
        }

        private Token? ReadSymbol()
        {
            char c = _source[_position];
            int startColumn = _column;

            TokenType type = TokenType.EOF; // Default, will be overridden
            int length = 1;

            switch (c)
            {
                case '(': type = TokenType.LParen; break;
                case ')': type = TokenType.RParen; break;
                case '{': type = TokenType.LBrace; break;
                case '}': type = TokenType.RBrace; break;
                case '[': type = TokenType.LBracket; break;
                case ']': type = TokenType.RBracket; break;
                case ';': type = TokenType.Semicolon; break;
                case ':':
                    if (_position + 1 < _source.Length && _source[_position + 1] == ':')
                    {
                        type = TokenType.DoubleColon;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Colon;
                    }
                    break;
                case ',': type = TokenType.Comma; break;
                case '.':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '.')
                    {
                        // Range operator: 1..5
                        type = TokenType.DotDot;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Dot;
                    }
                    break;
                case '+':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.PlusEqual;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Plus;
                    }
                    break;
                case '-':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.MinusEqual;
                        length = 2;
                    }
                    else if (_position + 1 < _source.Length && _source[_position + 1] == '>')
                    {
                        type = TokenType.Arrow;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Minus;
                    }
                    break;
                case '*':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.StarEqual;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Star;
                    }
                    break;
                case '/':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.SlashEqual;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Slash;
                    }
                    break;
                case '%':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.PercentEqual;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Percent;
                    }
                    break;
                case '|':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '>')
                    {
                        type = TokenType.Pipe;
                        length = 2;
                    }
                    else if (_position + 1 < _source.Length && _source[_position + 1] == '|')
                    {
                        type = TokenType.OrOr;
                        length = 2;
                    }
                    else
                    {
                        // Lone '|' — bitwise OR (was the match/pipe separator).
                        type = TokenType.BitwiseOr;
                        length = 1;
                    }
                    break;
                case '&':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '&')
                    {
                        type = TokenType.AndAnd;
                        length = 2;
                    }
                    else
                    {
                        _diagnostics.AddError($"Unexpected character: {c}", _line, _column, _sourceFile);
                        _position++;
                        _column++;
                        return null;
                    }
                    break;
                case '!':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.BangEqual;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Bang;
                    }
                    break;
                case '>':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '>')
                    {
                        // Composition operator: f >> g
                        type = TokenType.GreaterGreater;
                        length = 2;
                    }
                    else if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.GreaterEqual;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Greater;
                    }
                    break;
                case '<':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '<')
                    {
                        // Left shift: a << b
                        type = TokenType.LessLess;
                        length = 2;
                    }
                    else if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.LessEqual;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Less;
                    }
                    break;
                case '=':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
                    {
                        type = TokenType.EqualEqual;
                        length = 2;
                    }
                    else if (_position + 1 < _source.Length && _source[_position + 1] == '>')
                    {
                        // Fat arrow: pattern => result (match arms)
                        type = TokenType.FatArrow;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Assign;
                    }
                    break;
                case '?':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '.')
                    {
                        type = TokenType.QuestionDot;
                        length = 2;
                    }
                    else if (_position + 1 < _source.Length && _source[_position + 1] == '?')
                    {
                        type = TokenType.NullCoalesce;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Question;
                    }
                    break;
                default:
                    _diagnostics.AddError($"Unexpected character: {c}", _line, _column, _sourceFile);
                    _position++;
                    _column++;
                    // Skip unexpected characters - don't return a token
                    return null;
            }

            _position += length;
            _column += length;

            string text = _source.Substring(_position - length, length);
            return new Token(type, text, _line, startColumn, length);
        }
    }
}