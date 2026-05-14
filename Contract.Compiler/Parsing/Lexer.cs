using System;
using System.Collections.Generic;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler.Parsing
{
    public enum TokenType
    {
        // Keywords
        Contract, Fn, If, Else, While, Switch, Case, Return, Var, Let, Static,
        Public, Private, Protected, Internal, Null, Import, Constructor, Struct, Export, Fun,
        
        // Literals
        Identifier, IntLiteral, StringLiteral,
        
        // Symbols
        LParen, RParen, LBrace, RBrace, LBracket, RBracket,
        Semicolon, Colon, DoubleColon, Comma, Dot, Plus, Minus, Star, Slash,
        Less, LessEqual, Greater, GreaterEqual, EqualEqual, Bang, BangEqual, Assign, Arrow, Pipe,
        
        // Special
        EOF
    }

    public class Token
    {
        public TokenType Type { get; }
        public string Text { get; }
        public int Line { get; }
        public int Column { get; }

        public Token(TokenType type, string text, int line, int column)
        {
            Type = type;
            Text = text;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"{Type}: '{Text}' at {Line}:{Column}";
    }

    public class Lexer
    {
        private readonly string _source;
        private int _position;
        private int _line = 1;
        private int _column = 1;
        private readonly DiagnosticBag _diagnostics;

        private static readonly Dictionary<string, TokenType> Keywords = new()
        {
            ["Contract"] = TokenType.Contract,
            ["fn"] = TokenType.Fn,
            ["if"] = TokenType.If,
            ["else"] = TokenType.Else,
            ["while"] = TokenType.While,
            ["switch"] = TokenType.Switch,
            ["case"] = TokenType.Case,
            ["return"] = TokenType.Return,
            ["var"] = TokenType.Var,
            ["let"] = TokenType.Let,
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
            ["export"] = TokenType.Export
        };

        public Lexer(string source, DiagnosticBag diagnostics)
        {
            _source = source;
            _diagnostics = diagnostics;
        }

        public IEnumerable<Token> Tokenize()
        {
            while (_position < _source.Length)
            {
                SkipWhitespace();

                if (_position >= _source.Length)
                    break;

                char c = _source[_position];

                if (char.IsLetter(c) || c == '_')
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

            return new Token(type, text, _line, startColumn);
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

            string text = _source.Substring(start, _position - start);
            return new Token(TokenType.IntLiteral, text, _line, startColumn);
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
            return new Token(TokenType.StringLiteral, text, _line, startColumn);
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
                case '.': type = TokenType.Dot; break;
                case '+': type = TokenType.Plus; break;
                case '-':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '>')
                    {
                        type = TokenType.Arrow;
                        length = 2;
                    }
                    else
                    {
                        type = TokenType.Minus;
                    }
                    break;
                case '*': type = TokenType.Star; break;
                case '/': type = TokenType.Slash; break;
                case '|':
                    if (_position + 1 < _source.Length && _source[_position + 1] == '>')
                    {
                        type = TokenType.Pipe;
                        length = 2;
                    }
                    else
                    {
                        _diagnostics.AddError($"Unexpected character: {c}", _line, _column);
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
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
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
                    if (_position + 1 < _source.Length && _source[_position + 1] == '=')
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
                    else
                    {
                        type = TokenType.Assign;
                    }
                    break;
                default:
                    _diagnostics.AddError($"Unexpected character: {c}", _line, _column);
                    _position++;
                    _column++;
                    // Skip unexpected characters - don't return a token
                    return null;
            }

            _position += length;
            _column += length;

            string text = _source.Substring(_position - length, length);
            return new Token(type, text, _line, startColumn);
        }
    }
}