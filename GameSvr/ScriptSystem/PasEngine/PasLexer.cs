using System.Globalization;
using System.Text;

namespace GameSvr.PasEngine
{
    public enum PasTokenType
    {
        // Keywords
        Program, Procedure, Function, Begin, End,
        If, Then, Else, Case, Of, While, Do,
        For, To, DownTo, Repeat, Until,
        Var, Const, Array,
        Not, And, Or, Div, Mod, In,
        Exit, Result, True, False, Nil,
        Integer, String, Boolean, Double,

        // Identifiers & literals
        Identifier, Number, StringLiteral,

        // Operators
        Assign,      // :=
        Plus, Minus, Star, Slash,  // + - * /
        Eq, Neq, Lt, Gt, Le, Ge,  // = <> < > <= >=
        Dot, Comma, Semicolon, Colon,
        LParen, RParen,
        LBracket, RBracket,
        DoubleDot,   // ..

        // New language constructs
        Try, Except, Finally, Raise, On,
        Break, Continue, With,
        Inc, Dec, Assert,
        Constructor, Destructor,

        // Preprocessor
        IfDef, ElseIf, EndIf, Include,

        // Special
        Eof, Unknown
    }

    public class PasToken
    {
        public PasTokenType Type { get; set; }
        public string Text { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }

        public override string ToString() => $"{Type}('{Text}' @ {Line}:{Column})";
    }

    public readonly record struct PasSourceLine(string FileName, int LineNumber);

    public class PasLexer
    {
        private readonly string _source;
        private int _pos;
        private int _line;
        private int _col;
        private readonly List<PasToken> _tokens;
        private readonly string _sourceFile;
        private readonly IReadOnlyList<PasSourceLine> _sourceLines;
        private int _tokenPos;

        // Preprocessor state
        private bool _skipMode;
        private int _skipDepth;

        private static readonly Dictionary<string, PasTokenType> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["program"] = PasTokenType.Program,
            ["procedure"] = PasTokenType.Procedure,
            ["function"] = PasTokenType.Function,
            ["begin"] = PasTokenType.Begin,
            ["end"] = PasTokenType.End,
            ["if"] = PasTokenType.If,
            ["then"] = PasTokenType.Then,
            ["else"] = PasTokenType.Else,
            ["case"] = PasTokenType.Case,
            ["of"] = PasTokenType.Of,
            ["while"] = PasTokenType.While,
            ["do"] = PasTokenType.Do,
            ["for"] = PasTokenType.For,
            ["to"] = PasTokenType.To,
            ["downto"] = PasTokenType.DownTo,
            ["repeat"] = PasTokenType.Repeat,
            ["until"] = PasTokenType.Until,
            ["var"] = PasTokenType.Var,
            ["const"] = PasTokenType.Const,
            ["array"] = PasTokenType.Array,
            ["not"] = PasTokenType.Not,
            ["and"] = PasTokenType.And,
            ["or"] = PasTokenType.Or,
            ["div"] = PasTokenType.Div,
            ["mod"] = PasTokenType.Mod,
            ["in"] = PasTokenType.In,
            ["exit"] = PasTokenType.Exit,
            ["result"] = PasTokenType.Result,
            ["true"] = PasTokenType.True,
            ["false"] = PasTokenType.False,
            ["nil"] = PasTokenType.Nil,
            ["integer"] = PasTokenType.Integer,
            ["string"] = PasTokenType.String,
            ["boolean"] = PasTokenType.Boolean,
            ["double"] = PasTokenType.Double,
            // New keywords
            ["try"] = PasTokenType.Try,
            ["except"] = PasTokenType.Except,
            ["finally"] = PasTokenType.Finally,
            ["raise"] = PasTokenType.Raise,
            ["on"] = PasTokenType.On,
            ["break"] = PasTokenType.Break,
            ["continue"] = PasTokenType.Continue,
            ["with"] = PasTokenType.With,
            ["inc"] = PasTokenType.Inc,
            ["dec"] = PasTokenType.Dec,
            ["assert"] = PasTokenType.Assert,
            ["constructor"] = PasTokenType.Constructor,
            ["destructor"] = PasTokenType.Destructor,
        };

        public PasLexer(string source)
            : this(source, null, null)
        {
        }

        public PasLexer(string source, string sourceFile)
            : this(source, sourceFile, null)
        {
        }

        public PasLexer(string source, string sourceFile,
            IReadOnlyList<PasSourceLine> sourceLines)
        {
            _source = source;
            _sourceFile = sourceFile;
            _sourceLines = sourceLines;
            _pos = 0;
            _line = 1;
            _col = 1;
            _tokens = new List<PasToken>();
            _tokenPos = 0;
            _skipMode = false;
            _skipDepth = 0;
        }

        public void Tokenize()
        {
            while (_pos < _source.Length)
            {
                var ch = Peek();

                // Skip whitespace
                if (ch == ' ' || ch == '\t' || ch == '\r')
                {
                    Advance();
                    continue;
                }

                if (ch == '\n')
                {
                    _line++;
                    _col = 1;
                    _pos++;
                    continue;
                }

                // Preprocessor: {$IFDEF ...}, {$ELSE}, {$ENDIF}, {$I ...}
                if (ch == '{' && _pos + 1 < _source.Length && _source[_pos + 1] == '$')
                {
                    HandlePreprocessor();
                    continue;
                }

                // Comments: { ... }
                if (ch == '{')
                {
                    SkipBlockComment();
                    continue;
                }

                // Comments: // line
                if (ch == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '/')
                {
                    SkipLineComment();
                    continue;
                }

                // Comments: (* ... *)
                if (ch == '(' && _pos + 1 < _source.Length && _source[_pos + 1] == '*')
                {
                    _pos += 2; _col += 2;
                    SkipBlockCommentStar();
                    continue;
                }

                // String literal: '...'
                if (ch == '\'')
                {
                    AddToken(PasTokenType.StringLiteral, ReadString());
                    continue;
                }

                // Numbers
                if (char.IsDigit(ch))
                {
                    ReadNumber();
                    continue;
                }

                // Identifiers and keywords
                if (char.IsLetter(ch) || ch == '_')
                {
                    ReadIdentifier();
                    continue;
                }

                // Operators
                var op = ReadOperator();
                if (op != null)
                {
                    if (!_skipMode) _tokens.Add(op);
                    continue;
                }

                // Skip unknown characters (likely encoding artifacts)
                Advance();
            }

            if (!_skipMode)
                _tokens.Add(CreateToken(PasTokenType.Eof, "", _line, _col));
        }

        public PasToken PeekToken(int offset = 0)
        {
            var pos = _tokenPos + offset;
            if (pos >= _tokens.Count)
                return CreateToken(PasTokenType.Eof, "", _line, _col);
            return _tokens[pos];
        }

        public PasToken ReadToken()
        {
            if (_tokenPos >= _tokens.Count)
                return CreateToken(PasTokenType.Eof, "", _line, _col);
            return _tokens[_tokenPos++];
        }

        public int TokenPosition => _tokenPos;

        public bool Expect(PasTokenType type)
        {
            var t = PeekToken();
            if (t.Type == type) { ReadToken(); return true; }
            return false;
        }

        public PasToken ExpectAny(params PasTokenType[] types)
        {
            var t = PeekToken();
            if (types.Contains(t.Type)) return ReadToken();
            throw new PasRuntimeException($"Expected one of [{string.Join(", ", types)}] but got {t.Type}('{t.Text}') at line {t.Line}");
        }

        public void SkipNewlines()
        {
            while (PeekToken().Type == PasTokenType.Unknown && PeekToken().Text == "\n")
                ReadToken();
        }

        private char Peek(int offset = 0) => _pos + offset < _source.Length ? _source[_pos + offset] : '\0';

        private char Advance()
        {
            if (_pos >= _source.Length) return '\0';
            var ch = _source[_pos];
            _pos++;
            _col++;
            return ch;
        }

        private void AddToken(PasTokenType type, string text)
        {
            if (!_skipMode)
                _tokens.Add(CreateToken(type, text, _line, _col - text.Length));
        }

        private PasToken CreateToken(PasTokenType type, string text, int line, int column)
        {
            var sourceLine = line;
            var sourceFile = _sourceFile;
            if (_sourceLines != null && line > 0 && line <= _sourceLines.Count)
            {
                var mapped = _sourceLines[line - 1];
                sourceFile = mapped.FileName ?? sourceFile;
                if (mapped.LineNumber > 0) sourceLine = mapped.LineNumber;
            }

            return new PasToken
            {
                Type = type,
                Text = text,
                Line = line,
                Column = column,
                SourceFile = sourceFile,
                SourceLine = sourceLine
            };
        }

        private void SkipBlockComment()
        {
            _pos++; _col++;
            while (_pos < _source.Length)
            {
                if (_source[_pos] == '}') { _pos++; _col++; return; }
                if (_source[_pos] == '\n') { _line++; _col = 1; }
                else _col++;
                _pos++;
            }
        }

        private void SkipBlockCommentStar()
        {
            while (_pos + 1 < _source.Length)
            {
                if (_source[_pos] == '*' && _source[_pos + 1] == ')') { _pos += 2; _col += 2; return; }
                if (_source[_pos] == '\n') { _line++; _col = 1; }
                else _col++;
                _pos++;
            }
        }

        private void SkipLineComment()
        {
            while (_pos < _source.Length && _source[_pos] != '\n')
            {
                _pos++; _col++;
            }
        }

        private void HandlePreprocessor()
        {
            var start = _pos;
            _pos += 2; _col += 2; // skip {$
            while (_pos < _source.Length && _source[_pos] != '}') _pos++;
            if (_pos < _source.Length) { _pos++; _col++; }
            var dir = _source.Substring(start + 2, _pos - start - 3).Trim();

            if (dir.StartsWith("IFDEF", StringComparison.OrdinalIgnoreCase))
            {
                if (_skipMode) { _skipDepth++; }
                else
                {
                    var defineName = dir.Substring(5).Trim();
                    _skipMode = !_definedDefines.Contains(defineName);
                    if (_skipMode) _skipDepth = 1;
                }
            }
            else if (dir.StartsWith("IFNDEF", StringComparison.OrdinalIgnoreCase))
            {
                if (_skipMode) { _skipDepth++; }
                else
                {
                    var defineName = dir.Substring(6).Trim();
                    _skipMode = _definedDefines.Contains(defineName);
                    if (_skipMode) _skipDepth = 1;
                }
            }
            else if (dir.Equals("ELSE", StringComparison.OrdinalIgnoreCase) || dir.StartsWith("ELSE ", StringComparison.OrdinalIgnoreCase))
            {
                if (_skipDepth > 0) { } // inner IFDEF, ignore
                else _skipMode = !_skipMode;
            }
            else if (dir.StartsWith("ENDIF", StringComparison.OrdinalIgnoreCase))
            {
                if (_skipDepth > 0) _skipDepth--;
                else _skipMode = false;
            }
            else if (dir.StartsWith("I ", StringComparison.OrdinalIgnoreCase) || dir.StartsWith("INCLUDE ", StringComparison.OrdinalIgnoreCase))
            {
                var includeFile = dir.Substring(dir.IndexOf(' ') + 1).Trim().Trim();
                if (!_skipMode)
                {
                    AddToken(PasTokenType.Include, includeFile);
                }
            }
        }

        private string ReadString()
        {
            var sb = new StringBuilder();
            Advance(); // skip opening '
            while (_pos < _source.Length)
            {
                var ch = Advance();
                if (ch == '\'')
                {
                    if (_pos < _source.Length && _source[_pos] == '\'')
                    {
                        sb.Append('\'');
                        Advance(); // skip doubled '
                    }
                    else break;
                }
                else if (ch == '\r')
                {
                    break;
                }
                else if (ch == '\n')
                {
                    _line++;
                    _col = 1;
                    break;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private void ReadNumber()
        {
            var sb = new StringBuilder();
            var isDouble = false;
            while (_pos < _source.Length && char.IsDigit(Peek()))
                sb.Append(Advance());
            if (Peek() == '.' && Peek(1) != '.')
            {
                sb.Append(Advance());
                isDouble = true;
                while (_pos < _source.Length && char.IsDigit(Peek()))
                    sb.Append(Advance());
            }
            AddToken(PasTokenType.Number, sb.ToString());
        }

        private void ReadIdentifier()
        {
            var sb = new StringBuilder();
            while (_pos < _source.Length && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
                sb.Append(Advance());
            var word = sb.ToString();
            if (Keywords.TryGetValue(word, out var kwType))
                AddToken(kwType, word);
            else
                AddToken(PasTokenType.Identifier, word);
        }

        private PasToken ReadOperator()
        {
            var ch = Advance();
            switch (ch)
            {
                case '+': return MakeToken(PasTokenType.Plus, "+");
                case '-': return MakeToken(PasTokenType.Minus, "-");
                case '*': return MakeToken(PasTokenType.Star, "*");
                case '/': return MakeToken(PasTokenType.Slash, "/");
                case '=': return MakeToken(PasTokenType.Eq, "=");
                case '(': return MakeToken(PasTokenType.LParen, "(");
                case ')': return MakeToken(PasTokenType.RParen, ")");
                case '[': return MakeToken(PasTokenType.LBracket, "[");
                case ']': return MakeToken(PasTokenType.RBracket, "]");
                case ',': return MakeToken(PasTokenType.Comma, ",");
                case ';': return MakeToken(PasTokenType.Semicolon, ";");
                case '.':
                    if (Peek() == '.') { Advance(); return MakeToken(PasTokenType.DoubleDot, ".."); }
                    return MakeToken(PasTokenType.Dot, ".");
                case ':':
                    if (Peek() == '=') { Advance(); return MakeToken(PasTokenType.Assign, ":="); }
                    return MakeToken(PasTokenType.Colon, ":");
                case '<':
                    if (Peek() == '>') { Advance(); return MakeToken(PasTokenType.Neq, "<>"); }
                    if (Peek() == '=') { Advance(); return MakeToken(PasTokenType.Le, "<="); }
                    return MakeToken(PasTokenType.Lt, "<");
                case '>':
                    if (Peek() == '=') { Advance(); return MakeToken(PasTokenType.Ge, ">="); }
                    return MakeToken(PasTokenType.Gt, ">");
                default:
                    return null;
            }
        }

        private PasToken MakeToken(PasTokenType type, string text)
        {
            return CreateToken(type, text, _line, _col - text.Length);
        }

        // Defines set by the host
        private HashSet<string> _definedDefines = new(StringComparer.OrdinalIgnoreCase);
        public void SetDefines(IEnumerable<string> defines)
        {
            _definedDefines = new HashSet<string>(defines, StringComparer.OrdinalIgnoreCase);
            _definedDefines.Add("VER150"); // base compatibility define
        }
    }
}
