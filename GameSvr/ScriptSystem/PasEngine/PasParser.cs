using System.Text;

namespace GameSvr.PasEngine
{
    public class PasParser
    {
        private readonly PasLexer _lexer;
        private readonly string _basePath;
        private readonly HashSet<string> _resolvedIncludes;
        private readonly Dictionary<string, ArrayTypeInfo> _arrayTypes;

        private sealed class ArrayTypeInfo
        {
            public bool Dynamic;
            public int Low;
            public int High;
            public string ElementType;
        }

        public PasParser(PasLexer lexer, string basePath = "")
        {
            _lexer = lexer;
            _basePath = basePath;
            _resolvedIncludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _arrayTypes = new Dictionary<string, ArrayTypeInfo>(StringComparer.OrdinalIgnoreCase);
        }

        public PasProgram Parse()
        {
            _lexer.Tokenize();
            var prog = new PasProgram();

            // program Name; (optional)
            if (_lexer.Expect(PasTokenType.Program))
            {
                var nameTok = _lexer.ExpectAny(PasTokenType.Identifier);
                prog.Name = nameTok.Text;
                _lexer.Expect(PasTokenType.Semicolon);
            }

            ParseTopLevelDeclarations(prog);
            SkipTopLevelNoiseBeforeMain();

            // Main block: begin ... end. OR bare statements before end.
            if (_lexer.PeekToken().Type == PasTokenType.Begin)
            {
                _lexer.ReadToken();
                prog.MainBlock = ParseBlock();
                if (_lexer.PeekToken().Type == PasTokenType.Dot)
                    _lexer.ReadToken();
            }
            else if (_lexer.PeekToken().Type != PasTokenType.Eof && _lexer.PeekToken().Type != PasTokenType.Dot)
            {
                // Bare main body (no "begin"): parse statements until end. or EOF
                var block = new PasBlock();
                while (_lexer.PeekToken().Type != PasTokenType.Dot && _lexer.PeekToken().Type != PasTokenType.Eof)
                {
                    var before = _lexer.TokenPosition;
                    var stmt = ParseStatement();
                    if (stmt != null) block.Statements.Add(stmt);
                    _lexer.Expect(PasTokenType.Semicolon);
                    EnsureProgress(_lexer, before, "main block");
                }
                prog.MainBlock = block;
                if (_lexer.PeekToken().Type == PasTokenType.Dot)
                    _lexer.ReadToken();
            }

            return prog;
        }

        private void ParseTopLevelDeclarations(PasProgram prog)
        {
            while (true)
            {
                var t = _lexer.PeekToken();
                switch (t.Type)
                {
                    case PasTokenType.Include:
                        _lexer.ReadToken();
                        var includeFile = t.Text;
                        if (!_resolvedIncludes.Contains(includeFile))
                        {
                            _resolvedIncludes.Add(includeFile);
                            LoadInclude(includeFile, prog);
                        }
                        break;

                    case PasTokenType.Const:
                        _lexer.ReadToken();
                        ParseConstSection(prog.Consts);
                        break;

                    case PasTokenType.Var:
                        _lexer.ReadToken();
                        ParseVarSection(prog.GlobalVars);
                        break;

                    case PasTokenType.Identifier when t.Text.Equals("type", StringComparison.OrdinalIgnoreCase):
                        _lexer.ReadToken();
                        ParseTypeSection();
                        break;

                    case PasTokenType.Procedure:
                    case PasTokenType.Function:
                        prog.Procedures.Add(ParseProcDecl());
                        break;

                    default:
                        return;
                }
            }
        }

        private void LoadInclude(string fileName, PasProgram prog)
        {
            // Search paths: CommonScripts, current directory
            string[] searchPaths = {
                _basePath,
                Path.Combine(_basePath, "CommonScripts"),
                _basePath,
            };

            string content = null;
            string resolvedPath = null;

            foreach (var dir in searchPaths)
            {
                var path = Path.Combine(dir, fileName.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    content = PasScriptTextReader.ReadAllText(path);
                    resolvedPath = Path.GetFullPath(path);
                    break;
                }
            }

            if (content == null)
                throw new FileNotFoundException($"Include not found: {fileName} (from {_basePath})");

            var includeBasePath = Path.GetDirectoryName(resolvedPath) ?? _basePath;
            var subLexer = new PasLexer(content, resolvedPath);
            var subParser = new PasParser(subLexer, includeBasePath);
            subParser._resolvedIncludes.AddRange(_resolvedIncludes);

            subLexer.Tokenize();

            // Parse const/var/procedure from include
            while (true)
            {
                var t = subLexer.PeekToken();
                if (t.Type == PasTokenType.Eof) break;

                switch (t.Type)
                {
                    case PasTokenType.Include:
                        subLexer.ReadToken();
                        var subInclude = t.Text;
                        if (!_resolvedIncludes.Contains(subInclude))
                        {
                            _resolvedIncludes.Add(subInclude);
                            var subSubParser = new PasParser(new PasLexer(""), includeBasePath);
                            subSubParser.LoadInclude(subInclude, prog);
                        }
                        break;

                    case PasTokenType.Const:
                        subLexer.ReadToken();
                        ParseConstSection(prog.Consts, subLexer);
                        break;

                    case PasTokenType.Var:
                        subLexer.ReadToken();
                        ParseVarSection(prog.GlobalVars, subLexer);
                        break;

                    case PasTokenType.Identifier when t.Text.Equals("type", StringComparison.OrdinalIgnoreCase):
                        subLexer.ReadToken();
                        ParseTypeSection(subLexer);
                        break;

                    case PasTokenType.Procedure:
                    case PasTokenType.Function:
                        var subParser2 = new PasParser(subLexer, _basePath);
                        // We need to parse the proc with the same lexer...
                        // For simplicity, read the proc directly
                        var proc = ParseProcDecl(subLexer);
                        if (proc != null) prog.Procedures.Add(proc);
                        break;

                    default:
                        subLexer.ReadToken(); // skip
                        break;
                }
            }

            _resolvedIncludes.AddRange(subParser._resolvedIncludes);
        }

        private void ParseConstSection(List<PasConstDecl> consts, PasLexer lexer = null)
        {
            lexer ??= _lexer;
            while (true)
            {
                var t = lexer.PeekToken();
                if (t.Type != PasTokenType.Identifier) break;

                var name = lexer.ReadToken().Text;
                // Support both = and := in const (战神兼容)
                if (!lexer.Expect(PasTokenType.Eq) && !lexer.Expect(PasTokenType.Assign))
                    throw new Exception($"Expected = or := for const {name}");
                var val = ParseConstValue(lexer, consts);
                lexer.Expect(PasTokenType.Semicolon);

                consts.Add(new PasConstDecl { Name = name, Value = val });
            }
        }

        private PasValue ParseConstValue(PasLexer lexer, List<PasConstDecl> consts = null)
        {
            var value = ParseConstAtom(lexer, consts);
            while (lexer.PeekToken().Type == PasTokenType.Plus)
            {
                lexer.ReadToken();
                value += ParseConstAtom(lexer, consts);
            }
            return value;
        }

        private PasValue ParseConstAtom(PasLexer lexer, List<PasConstDecl> consts)
        {
            var t = lexer.PeekToken();
            switch (t.Type)
            {
                case PasTokenType.Number:
                    lexer.ReadToken();
                    if (t.Text.Contains('.'))
                        return PasValue.FromDouble(double.Parse(t.Text));
                    return PasValue.FromInt(int.Parse(t.Text));
                case PasTokenType.StringLiteral:
                    lexer.ReadToken();
                    return PasValue.FromString(t.Text);
                case PasTokenType.True:
                    lexer.ReadToken();
                    return PasValue.FromBool(true);
                case PasTokenType.False:
                    lexer.ReadToken();
                    return PasValue.FromBool(false);
                case PasTokenType.Identifier:
                    lexer.ReadToken();
                    var found = consts?.LastOrDefault(c => string.Equals(c.Name, t.Text, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found.Value;
                    return PasValue.FromString(t.Text); // treat identifier constants as strings
                default:
                    lexer.ReadToken();
                    return PasValue.Nil;
            }
        }

        private void ParseVarSection(List<PasVarDecl> vars, PasLexer lexer = null)
        {
            lexer ??= _lexer;
            while (true)
            {
                var t = lexer.PeekToken();
                if (t.Type != PasTokenType.Identifier) break;

                var names = new List<string>();
                names.Add(lexer.ReadToken().Text);
                while (lexer.Expect(PasTokenType.Comma))
                    names.Add(lexer.ReadToken().Text);

                lexer.Expect(PasTokenType.Colon);

                var typeName = "";
                bool isArray = false;
                int arrLow = 0, arrHigh = 0;
                string elemType = "";

                if (lexer.PeekToken().Type == PasTokenType.Array)
                {
                    isArray = true;
                    lexer.ReadToken(); // array
                    if (lexer.Expect(PasTokenType.LBracket))
                    {
                        arrLow = int.Parse(lexer.ReadToken().Text);
                        lexer.Expect(PasTokenType.DoubleDot);
                        arrHigh = int.Parse(lexer.ReadToken().Text);
                        lexer.Expect(PasTokenType.RBracket);
                    }
                    else
                    {
                        arrLow = 0;
                        arrHigh = -1;
                    }
                    lexer.Expect(PasTokenType.Of);
                    var typeTok = lexer.ExpectAny(PasTokenType.Identifier, PasTokenType.Integer, PasTokenType.String, PasTokenType.Boolean);
                    elemType = typeTok.Text;
                }
                else
                {
                    var typeTok = lexer.ExpectAny(PasTokenType.Identifier, PasTokenType.Integer, PasTokenType.String, PasTokenType.Boolean, PasTokenType.Double);
                    typeName = typeTok.Text;
                    if (_arrayTypes.TryGetValue(typeName, out var arrayType))
                    {
                        isArray = true;
                        arrLow = arrayType.Low;
                        arrHigh = arrayType.High;
                        elemType = arrayType.ElementType;
                    }
                }

                lexer.Expect(PasTokenType.Semicolon);

                foreach (var name in names)
                {
                    vars.Add(new PasVarDecl
                    {
                        Name = name,
                        TypeName = typeName,
                        IsArray = isArray,
                        IsDynamicArray = isArray && arrHigh < arrLow,
                        ArrayLow = arrLow,
                        ArrayHigh = arrHigh,
                        ArrayElementType = elemType
                    });
                }
            }
        }

        private void ParseTypeSection(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            while (lexer.PeekToken().Type == PasTokenType.Identifier)
            {
                var aliasName = lexer.ReadToken().Text;
                if (!lexer.Expect(PasTokenType.Eq))
                    break;

                if (lexer.Expect(PasTokenType.Array))
                {
                    var arrayType = new ArrayTypeInfo { Dynamic = true, Low = 0, High = -1 };
                    if (lexer.Expect(PasTokenType.LBracket))
                    {
                        arrayType.Dynamic = false;
                        arrayType.Low = int.Parse(lexer.ReadToken().Text);
                        lexer.Expect(PasTokenType.DoubleDot);
                        arrayType.High = int.Parse(lexer.ReadToken().Text);
                        lexer.Expect(PasTokenType.RBracket);
                    }
                    lexer.Expect(PasTokenType.Of);
                    arrayType.ElementType = lexer.ExpectAny(PasTokenType.Identifier, PasTokenType.Integer,
                        PasTokenType.String, PasTokenType.Boolean, PasTokenType.Double).Text;
                    lexer.Expect(PasTokenType.Semicolon);
                    _arrayTypes[aliasName] = arrayType;
                    continue;
                }

                var depth = 0;
                while (lexer.PeekToken().Type != PasTokenType.Eof)
                {
                    var t = lexer.PeekToken();
                    if (depth == 0 && t.Type == PasTokenType.Semicolon)
                    {
                        lexer.ReadToken();
                        break;
                    }

                    if (t.Type == PasTokenType.LParen || t.Type == PasTokenType.LBracket)
                        depth++;
                    else if ((t.Type == PasTokenType.RParen || t.Type == PasTokenType.RBracket) && depth > 0)
                        depth--;

                    lexer.ReadToken();
                }
            }
        }

        private PasProcDecl ParseProcDecl(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            var isFunc = lexer.Expect(PasTokenType.Function);
            if (!isFunc && !lexer.Expect(PasTokenType.Procedure))
                return null;

            var nameTok = lexer.ExpectAny(PasTokenType.Identifier);
            var proc = Located(new PasProcDecl
            {
                Name = nameTok.Text,
                IsFunction = isFunc
            }, nameTok);

            // Parameters
            if (lexer.Expect(PasTokenType.LParen))
            {
                if (lexer.PeekToken().Type != PasTokenType.RParen)
                {
                    ParseParamList(proc.Parameters, lexer);
                }
                lexer.Expect(PasTokenType.RParen);
            }

            // Return type for functions; some legacy scripts also annotate procedures.
            if (isFunc || lexer.PeekToken().Type == PasTokenType.Colon)
            {
                lexer.Expect(PasTokenType.Colon);
                var retTok = lexer.ExpectAny(PasTokenType.Identifier, PasTokenType.Integer, PasTokenType.String, PasTokenType.Boolean, PasTokenType.Double);
                proc.ReturnType = retTok.Text;
                if (_arrayTypes.TryGetValue(proc.ReturnType, out var returnArrayType))
                {
                    proc.ReturnIsArray = true;
                    proc.ReturnArrayElementType = returnArrayType.ElementType;
                }
            }

            lexer.Expect(PasTokenType.Semicolon);

            // Local var declarations
            while (lexer.PeekToken().Type == PasTokenType.Var)
            {
                lexer.ReadToken();
                ParseVarSection(proc.LocalVars, lexer);
            }

            // Procedure/function body
            // Could be forward declaration (no body)
            if (lexer.PeekToken().Type == PasTokenType.Begin)
            {
                lexer.ReadToken();
                proc.Body = ParseBlock(lexer);
                lexer.Expect(PasTokenType.Semicolon);
            }

            return proc;
        }

        private void ParseParamList(List<PasVarDecl> parameters, PasLexer lexer)
        {
            while (true)
            {
                var parameterMode = PasParameterMode.Value;
                var t = lexer.PeekToken();
                if (t.Type == PasTokenType.Var || t.Type == PasTokenType.Const || (t.Type == PasTokenType.Identifier &&
                    (t.Text.Equals("const", StringComparison.OrdinalIgnoreCase) ||
                     t.Text.Equals("out", StringComparison.OrdinalIgnoreCase))))
                {
                    if (t.Type == PasTokenType.Var)
                        parameterMode = PasParameterMode.Var;
                    else if (t.Text.Equals("out", StringComparison.OrdinalIgnoreCase))
                        parameterMode = PasParameterMode.Out;
                    else
                        parameterMode = PasParameterMode.Const;
                    lexer.ReadToken();
                }
                var names = new List<string>();
                names.Add(lexer.ReadToken().Text);
                while (Expect(PasTokenType.Comma, lexer))
                    names.Add(lexer.ReadToken().Text);

                ExpectThrow(PasTokenType.Colon, lexer);

                var typeTok = lexer.ExpectAny(PasTokenType.Identifier, PasTokenType.Integer, PasTokenType.String, PasTokenType.Boolean, PasTokenType.Double);
                _arrayTypes.TryGetValue(typeTok.Text, out var arrayType);

                foreach (var name in names)
                {
                    parameters.Add(new PasVarDecl
                    {
                        Name = name,
                        TypeName = typeTok.Text,
                        ParameterMode = parameterMode,
                        IsArray = arrayType != null,
                        IsDynamicArray = arrayType?.Dynamic ?? false,
                        ArrayLow = arrayType?.Low ?? 0,
                        ArrayHigh = arrayType?.High ?? 0,
                        ArrayElementType = arrayType?.ElementType
                    });
                }

                if (lexer.PeekToken().Type != PasTokenType.Semicolon) break;
                lexer.ReadToken();
                if (lexer.PeekToken().Type == PasTokenType.RParen) break;
            }
        }

        private void ExpectThrow(PasTokenType type, PasLexer lexer)
        {
            if (!lexer.Expect(type))
                throw new PasRuntimeException($"Expected {type} but got {lexer.PeekToken().Type} at line {lexer.PeekToken().Line}");
        }

        private PasBlock ParseBlock(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            var block = new PasBlock();

            while (true)
            {
                var t = lexer.PeekToken();

                if (t.Type == PasTokenType.End)
                {
                    lexer.ReadToken();
                    return block;
                }

                if (t.Type == PasTokenType.Dot) break; // end.
                if (t.Type == PasTokenType.Eof) break;

                var before = lexer.TokenPosition;
                var stmt = ParseStatement(lexer);
                if (stmt != null)
                    block.Statements.Add(stmt);

                // Semicolons are separators, optional before end
                lexer.Expect(PasTokenType.Semicolon);
                EnsureProgress(lexer, before, "block");
            }

            return block;
        }

        private PasAstNode ParseStatement(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            var t = lexer.PeekToken();

            switch (t.Type)
            {
                case PasTokenType.Begin:
                    lexer.ReadToken();
                    return ParseBlock(lexer);

                case PasTokenType.If:
                    return ParseIfStmt(lexer);

                case PasTokenType.Case:
                    return ParseCaseStmt(lexer);

                case PasTokenType.While:
                    return ParseWhileStmt(lexer);

                case PasTokenType.For:
                    return ParseForStmt(lexer);

                case PasTokenType.Repeat:
                    return ParseRepeatStmt(lexer);

                case PasTokenType.Try:
                    return ParseTryStmt(lexer);

                case PasTokenType.Break:
                    lexer.ReadToken();
                    return new PasBreakStmt();

                case PasTokenType.Continue:
                    lexer.ReadToken();
                    return new PasContinueStmt();

                case PasTokenType.With:
                    return ParseWithStmt(lexer);

                case PasTokenType.Raise:
                    return ParseRaiseStmt(lexer);

                case PasTokenType.Inc:
                    return ParseIncStmt(lexer);

                case PasTokenType.Dec:
                    return ParseDecStmt(lexer, false);

                case PasTokenType.Assert:
                    return ParseAssertStmt(lexer);

                case PasTokenType.Exit:
                    return ParseExitStmt(lexer);

                case PasTokenType.Semicolon:
                    lexer.ReadToken();
                    return null;

                default:
                    return ParseAssignmentOrCall(lexer);
            }
        }

        private PasAstNode ParseIfStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // if
            var cond = ParseExpression(lexer);
            Expect(PasTokenType.Then, lexer);

            PasAstNode thenBlock = ParseStatement(lexer);

            PasAstNode elseBlock = null;
            if (lexer.PeekToken().Type == PasTokenType.Semicolon &&
                lexer.PeekToken(1).Type == PasTokenType.Else)
            {
                lexer.ReadToken();
            }
            if (lexer.PeekToken().Type == PasTokenType.Else)
            {
                lexer.ReadToken();
                elseBlock = ParseStatement(lexer);
            }

            return new PasIfStmt { Condition = cond, ThenBlock = thenBlock, ElseBlock = elseBlock };
        }

        private PasAstNode ParseCaseStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // case
            var expr = ParseExpression(lexer);
            Expect(PasTokenType.Of, lexer);

            var caseStmt = new PasCaseStmt { Expression = expr };

            while (true)
            {
                var t = lexer.PeekToken();
                if (t.Type == PasTokenType.End) { lexer.ReadToken(); break; }

                var branch = new PasCaseBranch();
                if (t.Type == PasTokenType.Else)
                {
                    lexer.ReadToken();
                    branch.Body = ParseStatement(lexer);
                    caseStmt.Branches.Add(branch);
                    lexer.Expect(PasTokenType.Semicolon);
                    continue;
                }

                // Parse value list: 1, 2, 3 : statement
                branch.Values.Add(ParseExpression(lexer));
                while (Expect(PasTokenType.Comma, lexer))
                    branch.Values.Add(ParseExpression(lexer));

                Expect(PasTokenType.Colon, lexer);

                // Colon followed by either a statement or begin...end
                branch.Body = ParseStatement(lexer);
                caseStmt.Branches.Add(branch);

                // Optional semicolon
                lexer.Expect(PasTokenType.Semicolon);
            }

            return caseStmt;
        }

        private PasAstNode ParseWhileStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // while
            var cond = ParseExpression(lexer);
            Expect(PasTokenType.Do, lexer);

            return new PasWhileStmt { Condition = cond, Body = ParseStatement(lexer) };
        }

        private PasAstNode ParseForStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // for
            var varTok = lexer.ExpectAny(PasTokenType.Identifier);
            Expect(PasTokenType.Assign, lexer);
            var from = ParseExpression(lexer);
            var isDownTo = lexer.Expect(PasTokenType.DownTo);
            if (!isDownTo) Expect(PasTokenType.To, lexer);
            var to = ParseExpression(lexer);
            Expect(PasTokenType.Do, lexer);

            return new PasForStmt
            {
                VarName = varTok.Text,
                From = from,
                To = to,
                DownTo = isDownTo,
                Body = ParseStatement(lexer)
            };
        }

        private PasAstNode ParseRepeatStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // repeat
            var body = new PasBlock();
            while (lexer.PeekToken().Type != PasTokenType.Until)
            {
                if (lexer.PeekToken().Type == PasTokenType.Eof)
                    throw new PasRuntimeException("Unexpected EOF in repeat block");
                var before = lexer.TokenPosition;
                var stmt = ParseStatement(lexer);
                if (stmt != null) body.Statements.Add(stmt);
                lexer.Expect(PasTokenType.Semicolon);
                EnsureProgress(lexer, before, "repeat block");
            }
            lexer.ReadToken(); // until
            var cond = ParseExpression(lexer);
            return new PasRepeatStmt { Body = body, Condition = cond };
        }

        private PasAstNode ParseExitStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // exit
            PasAstNode val = null;
            if (!IsStatementBoundary(lexer.PeekToken().Type))
                val = ParseExpression(lexer);
            return new PasExitStmt { Value = val };
        }

        // ===== NEW: try...except...end / try...finally...end =====

        private PasAstNode ParseTryStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // try
            var tryStmt = new PasTryStmt();

            // try body: list of statements
            tryStmt.Body = new PasBlock();
            while (lexer.PeekToken().Type != PasTokenType.Except &&
                   lexer.PeekToken().Type != PasTokenType.Finally &&
                   lexer.PeekToken().Type != PasTokenType.End)
            {
                if (lexer.PeekToken().Type == PasTokenType.Eof)
                    throw new PasRuntimeException("Unexpected EOF in try block");
                var before = lexer.TokenPosition;
                var stmt = ParseStatement(lexer);
                if (stmt != null) tryStmt.Body.Statements.Add(stmt);
                lexer.Expect(PasTokenType.Semicolon);
                EnsureProgress(lexer, before, "try block");
            }

            // except handlers: on E: ExceptionType do ...
            if (lexer.Expect(PasTokenType.Except))
            {
                while (lexer.PeekToken().Type != PasTokenType.End &&
                       lexer.PeekToken().Type != PasTokenType.Finally &&
                       lexer.PeekToken().Type != PasTokenType.Else)
                {
                    if (lexer.PeekToken().Type == PasTokenType.Eof)
                        throw new PasRuntimeException("Unexpected EOF in except block");
                    var before = lexer.TokenPosition;
                    var handler = new PasExceptHandler();

                    if (lexer.Expect(PasTokenType.On))
                    {
                        handler.VariableName = lexer.ExpectAny(PasTokenType.Identifier).Text;
                        Expect(PasTokenType.Colon, lexer);
                        handler.ExceptionType = lexer.ExpectAny(PasTokenType.Identifier).Text;
                        Expect(PasTokenType.Do, lexer);
                        handler.Body = ParseStatement(lexer);
                    }
                    else
                    {
                        // Catch-all handler: just execute a statement
                        handler.Body = ParseStatement(lexer);
                    }
                    tryStmt.ExceptHandlers.Add(handler);
                    lexer.Expect(PasTokenType.Semicolon);
                    EnsureProgress(lexer, before, "except block");
                }

                // else block (optional)
                if (lexer.Expect(PasTokenType.Else))
                {
                    var elseHandler = new PasExceptHandler { Body = ParseStatement(lexer) };
                    tryStmt.ExceptHandlers.Add(elseHandler);
                    lexer.Expect(PasTokenType.Semicolon);
                }
            }
            else if (lexer.Expect(PasTokenType.Finally))
            {
                tryStmt.FinallyBlock = new PasBlock();
                while (lexer.PeekToken().Type != PasTokenType.End)
                {
                    if (lexer.PeekToken().Type == PasTokenType.Eof)
                        throw new PasRuntimeException("Unexpected EOF in finally block");
                    var before = lexer.TokenPosition;
                    var stmt = ParseStatement(lexer);
                    if (stmt != null) tryStmt.FinallyBlock.Statements.Add(stmt);
                    lexer.Expect(PasTokenType.Semicolon);
                    EnsureProgress(lexer, before, "finally block");
                }
            }

            Expect(PasTokenType.End, lexer);
            return tryStmt;
        }

        // ===== NEW: raise; / raise Exception.Create('msg'); =====

        private PasAstNode ParseRaiseStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // raise
            PasAstNode expr = null;
            if (lexer.PeekToken().Type != PasTokenType.Semicolon && lexer.PeekToken().Type != PasTokenType.End)
                expr = ParseExpression(lexer);
            return new PasRaiseStmt { Exception = expr };
        }

        // ===== NEW: with Obj1, Obj2 do begin ... end; =====

        private PasAstNode ParseWithStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // with
            var withStmt = new PasWithStmt();

            // Parse object list
            withStmt.Objects.Add(ParseExpression(lexer));
            while (Expect(PasTokenType.Comma, lexer))
                withStmt.Objects.Add(ParseExpression(lexer));

            Expect(PasTokenType.Do, lexer);
            withStmt.Body = ParseStatement(lexer);
            return withStmt;
        }

        // ===== NEW: Inc(varname); / Inc(varname, amount); =====

        private PasAstNode ParseIncStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // inc
            Expect(PasTokenType.LParen, lexer);
            var varName = lexer.ExpectAny(PasTokenType.Identifier).Text;
            PasAstNode amount = null;
            if (Expect(PasTokenType.Comma, lexer))
                amount = ParseExpression(lexer);
            Expect(PasTokenType.RParen, lexer);
            return new PasIncStmt { VariableName = varName, Amount = amount };
        }

        // ===== NEW: Dec(varname); / Dec(varname, amount); =====

        private PasAstNode ParseDecStmt(PasLexer lexer = null, bool alreadyRead = false)
        {
            lexer ??= _lexer;
            if (!alreadyRead)
            {
                lexer.ReadToken(); // dec
                Expect(PasTokenType.LParen, lexer);
            }
            var varName = lexer.ExpectAny(PasTokenType.Identifier).Text;
            PasAstNode amount = null;
            if (Expect(PasTokenType.Comma, lexer))
                amount = ParseExpression(lexer);
            Expect(PasTokenType.RParen, lexer);
            return new PasDecStmt { VariableName = varName, Amount = amount };
        }

        // ===== NEW: Assert(condition); / Assert(condition, message); =====

        private PasAstNode ParseAssertStmt(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            lexer.ReadToken(); // assert
            Expect(PasTokenType.LParen, lexer);
            var cond = ParseExpression(lexer);
            PasAstNode msg = null;
            if (Expect(PasTokenType.Comma, lexer))
                msg = ParseExpression(lexer);
            Expect(PasTokenType.RParen, lexer);
            return new PasAssertStmt { Condition = cond, Message = msg };
        }

        private PasAstNode ParseAssignmentOrCall(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            var t = lexer.PeekToken();
            if (!IsIdentifierLike(t.Type))
                return null;

            // Read identifier
            var idTok = lexer.ReadToken();
            var idName = idTok.Text;

            // Check for := assignment
            if (lexer.PeekToken().Type == PasTokenType.Assign)
            {
                lexer.ReadToken(); // :=
                var value = ParseExpression(lexer);
                return new PasAssignStmt
                {
                    Target = new PasIdentifierExpr { Name = idName },
                    Value = value
                };
            }

            // Check for array assignment: arr[i] := ...
            if (lexer.PeekToken().Type == PasTokenType.LBracket)
            {
                // Could be array access assignment or multi-dimensional
                var indices = new List<PasAstNode>();
                lexer.ReadToken(); // [
                indices.Add(ParseExpression(lexer));
                Expect(PasTokenType.RBracket, lexer);

                // Handle multi-dimensional arrays like name[i][j]
                while (lexer.PeekToken().Type == PasTokenType.LBracket)
                {
                    lexer.ReadToken();
                    indices.Add(ParseExpression(lexer));
                    Expect(PasTokenType.RBracket, lexer);
                }

                if (lexer.PeekToken().Type == PasTokenType.Assign)
                {
                    lexer.ReadToken(); // :=
                    var value = ParseExpression(lexer);
                    return new PasAssignStmt
                    {
                        Target = new PasMultiArrayAccessExpr { ArrayName = idName, Indices = indices },
                        Value = value
                    };
                }

                // This is a function call with array indexing as argument? Unlikely but...
                // Fall through to call expression parsing
                return Located(new PasCallStmt { Name = idName }, idTok);
            }

            // Check for method call or member access: This_Player.xxx / This_Npc.xxx
            if (lexer.PeekToken().Type == PasTokenType.Dot)
            {
                lexer.ReadToken(); // .
                var memberTok = ReadIdentifierLike(lexer);

                // Method call: This_Player.FlyTo(...)
                if (lexer.PeekToken().Type == PasTokenType.LParen)
                {
                    lexer.ReadToken(); // (
                    var args = ParseArgList(lexer);
                    Expect(PasTokenType.RParen, lexer);
                    var methodCall = Located(new PasCallStmt
                    {
                        Name = memberTok.Text,
                        IsMethod = true,
                        ObjectName = idName,
                        Arguments = args
                    }, memberTok);
                    return FinishStatementExpression(lexer,
                        ParsePostfixContinuation(lexer, methodCall));
                }

                var memberAccess = Located(new PasMemberAccessExpr
                {
                    ObjectName = idName,
                    MemberName = memberTok.Text
                }, memberTok);

                var memberExpression = ParsePostfixContinuation(lexer, memberAccess);

                if (lexer.PeekToken().Type == PasTokenType.Assign)
                {
                    lexer.ReadToken(); // :=
                    return new PasAssignStmt
                    {
                        Target = memberExpression,
                        Value = ParseExpression(lexer)
                    };
                }

                // Property/field access: This_Player.Level
                return memberExpression;
            }

            // Function/procedure call: ServerSay(...), GetG(...), etc.
            if (lexer.PeekToken().Type == PasTokenType.LParen)
            {
                lexer.ReadToken(); // (
                var args = ParseArgList(lexer);
                Expect(PasTokenType.RParen, lexer);

                return FinishStatementExpression(lexer, ParsePostfixContinuation(lexer,
                    Located(new PasCallStmt { Name = idName, Arguments = args }, idTok)));
            }

            // Standalone Result or identifier reference
            if (idName.Equals("RESULT", StringComparison.OrdinalIgnoreCase))
            {
                return Located(new PasIdentifierExpr { Name = "Result" }, idTok);
            }

            return Located(new PasIdentifierExpr { Name = idName }, idTok);
        }

        private List<PasAstNode> ParseArgList(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            var args = new List<PasAstNode>();
            if (lexer.PeekToken().Type == PasTokenType.RParen) return args;

            args.Add(ParseExpression(lexer));
            while (Expect(PasTokenType.Comma, lexer))
                args.Add(ParseExpression(lexer));
            return args;
        }

        // Expression parsing (simple precedence climbing)
        private PasAstNode ParseExpression(PasLexer lexer = null)
        {
            lexer ??= _lexer;
            return ParseOrExpr(lexer);
        }

        private PasAstNode ParseOrExpr(PasLexer lexer)
        {
            var left = ParseAndExpr(lexer);
            while (lexer.PeekToken().Type == PasTokenType.Or)
            {
                lexer.ReadToken();
                left = new PasBinaryOpExpr { Left = left, Op = "or", Right = ParseAndExpr(lexer) };
            }
            return left;
        }

        private PasAstNode ParseAndExpr(PasLexer lexer)
        {
            var left = ParseComparisonExpr(lexer);
            while (lexer.PeekToken().Type == PasTokenType.And)
            {
                lexer.ReadToken();
                left = new PasBinaryOpExpr { Left = left, Op = "and", Right = ParseComparisonExpr(lexer) };
            }
            return left;
        }

        private PasAstNode ParseComparisonExpr(PasLexer lexer)
        {
            var left = ParseAddExpr(lexer);
            var t = lexer.PeekToken();
            if (t.Type == PasTokenType.Eq || t.Type == PasTokenType.Neq ||
                t.Type == PasTokenType.Lt || t.Type == PasTokenType.Gt ||
                t.Type == PasTokenType.Le || t.Type == PasTokenType.Ge)
            {
                lexer.ReadToken();
                var op = t.Type switch
                {
                    PasTokenType.Eq => "=",
                    PasTokenType.Neq => "<>",
                    PasTokenType.Lt => "<",
                    PasTokenType.Gt => ">",
                    PasTokenType.Le => "<=",
                    PasTokenType.Ge => ">=",
                    _ => "="
                };
                left = new PasBinaryOpExpr { Left = left, Op = op, Right = ParseAddExpr(lexer) };
            }
            return left;
        }

        private PasAstNode ParseAddExpr(PasLexer lexer)
        {
            var left = ParseMulExpr(lexer);
            while (true)
            {
                var t = lexer.PeekToken();
                if (t.Type == PasTokenType.Plus)
                {
                    lexer.ReadToken();
                    left = new PasBinaryOpExpr { Left = left, Op = "+", Right = ParseMulExpr(lexer) };
                }
                else if (t.Type == PasTokenType.Minus)
                {
                    lexer.ReadToken();
                    left = new PasBinaryOpExpr { Left = left, Op = "-", Right = ParseMulExpr(lexer) };
                }
                else break;
            }
            return left;
        }

        private PasAstNode ParseMulExpr(PasLexer lexer)
        {
            var left = ParseUnaryExpr(lexer);
            while (true)
            {
                var t = lexer.PeekToken();
                if (t.Type == PasTokenType.Star)
                {
                    lexer.ReadToken();
                    left = new PasBinaryOpExpr { Left = left, Op = "*", Right = ParseUnaryExpr(lexer) };
                }
                else if (t.Type == PasTokenType.Slash)
                {
                    lexer.ReadToken();
                    left = new PasBinaryOpExpr { Left = left, Op = "/", Right = ParseUnaryExpr(lexer) };
                }
                else if (t.Type == PasTokenType.Div)
                {
                    lexer.ReadToken();
                    left = new PasBinaryOpExpr { Left = left, Op = "div", Right = ParseUnaryExpr(lexer) };
                }
                else if (t.Type == PasTokenType.Mod)
                {
                    lexer.ReadToken();
                    left = new PasBinaryOpExpr { Left = left, Op = "mod", Right = ParseUnaryExpr(lexer) };
                }
                else break;
            }
            return left;
        }

        private PasAstNode ParseUnaryExpr(PasLexer lexer)
        {
            var t = lexer.PeekToken();
            if (t.Type == PasTokenType.Minus)
            {
                lexer.ReadToken();
                return new PasUnaryOpExpr { Op = "-", Operand = ParseUnaryExpr(lexer) };
            }
            if (t.Type == PasTokenType.Plus)
            {
                lexer.ReadToken();
                return ParseUnaryExpr(lexer); // unary plus is a no-op
            }
            if (t.Type == PasTokenType.Not)
            {
                lexer.ReadToken();
                return new PasUnaryOpExpr { Op = "not", Operand = ParseUnaryExpr(lexer) };
            }
            return ParsePrimaryExpr(lexer);
        }

        private PasAstNode ParsePrimaryExpr(PasLexer lexer)
        {
            var t = lexer.PeekToken();

            switch (t.Type)
            {
                case PasTokenType.Number:
                    lexer.ReadToken();
                    if (t.Text.Contains('.'))
                        return new PasLiteralExpr { Value = PasValue.FromDouble(double.Parse(t.Text)) };
                    return new PasLiteralExpr { Value = PasValue.FromInt(int.Parse(t.Text)) };

                case PasTokenType.StringLiteral:
                    lexer.ReadToken();
                    return new PasLiteralExpr { Value = PasValue.FromString(t.Text) };

                case PasTokenType.True:
                    lexer.ReadToken();
                    return new PasLiteralExpr { Value = PasValue.FromBool(true) };

                case PasTokenType.False:
                    lexer.ReadToken();
                    return new PasLiteralExpr { Value = PasValue.FromBool(false) };

                case PasTokenType.Nil:
                    lexer.ReadToken();
                    return new PasLiteralExpr { Value = PasValue.Nil };

                case PasTokenType.Identifier:
                case PasTokenType.Result:
                case PasTokenType.Integer:
                case PasTokenType.String:
                case PasTokenType.Boolean:
                case PasTokenType.Double:
                    {
                        var idTok = lexer.ReadToken();
                        var idName = idTok.Text;

                        // Array access: arr[i]
                        if (lexer.PeekToken().Type == PasTokenType.LBracket)
                        {
                            var indices = new List<PasAstNode>();
                            lexer.ReadToken(); // [
                            indices.Add(ParseExpression(lexer));
                            Expect(PasTokenType.RBracket, lexer);

                            // Multi-dimensional: arr[i][j]
                            while (lexer.PeekToken().Type == PasTokenType.LBracket)
                            {
                                lexer.ReadToken();
                                indices.Add(ParseExpression(lexer));
                                Expect(PasTokenType.RBracket, lexer);
                            }

                            return ParsePostfixContinuation(lexer,
                                new PasMultiArrayAccessExpr { ArrayName = idName, Indices = indices });
                        }

                        // Function call: IntToStr(x)
                        if (lexer.PeekToken().Type == PasTokenType.LParen)
                        {
                            lexer.ReadToken(); // (
                            var args = ParseArgList(lexer);
                            Expect(PasTokenType.RParen, lexer);

                            return ParsePostfixContinuation(lexer, Located(new PasCallStmt
                            {
                                Name = idName,
                                Arguments = args
                            }, idTok));
                        }

                        return ParsePostfixContinuation(lexer,
                            Located(new PasIdentifierExpr { Name = idName }, idTok));
                    }

                case PasTokenType.LParen:
                    {
                        lexer.ReadToken(); // (
                        var expr = ParseExpression(lexer);
                        Expect(PasTokenType.RParen, lexer);
                        return ParsePostfixContinuation(lexer, expr);
                    }

                default:
                    throw new PasRuntimeException($"Unsupported expression token {t.Type}('{t.Text}') at line {t.Line}");
            }
        }

        private PasAstNode ParsePostfixContinuation(PasLexer lexer, PasAstNode expression)
        {
            while (lexer.PeekToken().Type == PasTokenType.Dot)
            {
                lexer.ReadToken();
                var memberTok = ReadIdentifierLike(lexer);
                var member = memberTok.Text;
                var directObject = expression as PasIdentifierExpr;
                if (lexer.PeekToken().Type == PasTokenType.LParen)
                {
                    lexer.ReadToken();
                    var args = ParseArgList(lexer);
                    Expect(PasTokenType.RParen, lexer);
                    expression = Located(new PasMethodCallExpr
                    {
                        Target = directObject == null ? expression : null,
                        ObjectName = directObject?.Name,
                        MethodName = member,
                        Arguments = args
                    }, memberTok);
                }
                else
                {
                    expression = Located(new PasMemberAccessExpr
                    {
                        Target = directObject == null ? expression : null,
                        ObjectName = directObject?.Name,
                        MemberName = member
                    }, memberTok);
                }
            }
            return expression;
        }

        private PasAstNode FinishStatementExpression(PasLexer lexer, PasAstNode expression)
        {
            if (lexer.PeekToken().Type != PasTokenType.Assign)
                return expression;
            lexer.ReadToken();
            return new PasAssignStmt
            {
                Target = expression,
                Value = ParseExpression(lexer)
            };
        }

        private bool Expect(PasTokenType type, PasLexer lexer)
        {
            if (lexer.PeekToken().Type == type) { lexer.ReadToken(); return true; }
            return false;
        }

        private static T Located<T>(T node, PasToken token) where T : PasAstNode
        {
            node.SourceFile = token.SourceFile;
            node.SourceLine = token.SourceLine > 0 ? token.SourceLine : token.Line;
            node.SourceColumn = token.Column;
            return node;
        }

        private void SkipTopLevelNoiseBeforeMain()
        {
            while (true)
            {
                var t = _lexer.PeekToken().Type;
                if (t == PasTokenType.Semicolon)
                {
                    _lexer.ReadToken();
                    continue;
                }

                if (t == PasTokenType.End)
                {
                    _lexer.ReadToken();
                    _lexer.Expect(PasTokenType.Semicolon);
                    continue;
                }

                break;
            }
        }

        private static bool IsIdentifierLike(PasTokenType type)
        {
            return type == PasTokenType.Identifier ||
                   type == PasTokenType.Result ||
                   type == PasTokenType.Integer ||
                   type == PasTokenType.String ||
                   type == PasTokenType.Boolean ||
                   type == PasTokenType.Double;
        }

        private PasToken ReadIdentifierLike(PasLexer lexer)
        {
            if (IsIdentifierLike(lexer.PeekToken().Type))
                return lexer.ReadToken();

            var t = lexer.PeekToken();
            throw new PasRuntimeException($"Expected identifier but got {t.Type}('{t.Text}') at line {t.Line}");
        }

        private static bool IsStatementBoundary(PasTokenType type)
        {
            return type == PasTokenType.Semicolon ||
                   type == PasTokenType.End ||
                   type == PasTokenType.Else ||
                   type == PasTokenType.Until ||
                   type == PasTokenType.Except ||
                   type == PasTokenType.Finally ||
                   type == PasTokenType.Eof;
        }

        private void EnsureProgress(PasLexer lexer, int before, string context)
        {
            if (lexer.TokenPosition != before) return;
            var t = lexer.PeekToken();
            throw new PasRuntimeException($"Parser made no progress in {context} at line {t.Line}: {t.Type}('{t.Text}')");
        }
    }

    public static class HashSetExtensions
    {
        public static void AddRange<T>(this HashSet<T> set, IEnumerable<T> items)
        {
            foreach (var item in items) set.Add(item);
        }
    }
}
