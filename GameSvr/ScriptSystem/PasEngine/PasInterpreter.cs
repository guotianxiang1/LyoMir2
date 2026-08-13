using System.Collections;
using SystemModule;
using GameSvr.Plugins;

namespace GameSvr.PasEngine
{
    public class PasInterpreter
    {
        private readonly PasProgram _program;
        private readonly PasApiBridge _api;
        private readonly Dictionary<string, PasValue> _globals;
        private Dictionary<string, PasValue> _locals;
        private readonly Stack<Dictionary<string, PasValue>> _scopeStack;
        private readonly Stack<PasProcDecl> _callStack;
        private PasValue _functionResult;
        private bool _exiting;
        private bool _breaking;     // break statement
        private bool _continuing;   // continue statement
        private string _lastExceptionType = string.Empty;
        private string _lastExceptionParam = string.Empty;
        private readonly HashSet<string> _builtinFuncs;
        private readonly HashSet<string> _builtinProcs;

        public PasInterpreter(PasProgram program, PasApiBridge api)
        {
            _program = program;
            _api = api;
            _globals = new Dictionary<string, PasValue>(StringComparer.OrdinalIgnoreCase);
            _locals = _globals;
            _scopeStack = new Stack<Dictionary<string, PasValue>>();
            _callStack = new Stack<PasProcDecl>();
            _functionResult = PasValue.Nil;
            _exiting = false;
            _breaking = false;
            _continuing = false;

            // Register built-in functions (condition checkers, value returners)
            _builtinFuncs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // String functions
                "IntToStr", "StrToInt", "FloatToStr", "FloatToString", "StrToFloat",
                "CompareText", "CompareStr", "SameText",
                "Length", "Trim", "UpperCase", "LowerCase", "Copy", "Pos",
                "BoolToStr", "StringReplace", "AddSpace", "AddSpace1", "GetValidStr",
                "Integer", "String", "Boolean", "Double", "GetArrayLength", "ExceptionToString",
                // Math/Random
                "Random", "RandomRange", "Abs", "Round", "Trunc", "Sqr", "Sqrt",
                // Date/Time
                "GetNow", "GetHour", "GetMin", "GetSecond", "GetDayOfWeek", "GetDateNum",
                "GetYear", "GetMonth", "GetDay", "FormatDateTime",
                "MinusDataTime", "SecondsBetween", "AddDateTimeWithSec",
                "ConvertDateTimeToDB", "ConvertDBToDateTime",
                // Variable system (G is public; V/S are player variables)
                "GetG", "GetV", "GetS",
                "SetG", "SetV", "SetS", "GroupSetV", "GroupSetS",
                // Check functions
                "CheckBagItem", "CheckBagItemEx", "CheckSkill", "CheckHeroSkill",
                "CheckLevel", "CheckGold", "CheckJob", "CheckGameGold",
                "CheckCurrMapMon", "CheckCurrMapHum", "CheckMapMonByName",
                "CheckOtherMapHum", "CheckDiamond",
                "IsCheckBodyItem", "IsMale", "IsFemale", "IsDead",
                "IsGuildLord", "IsFirstGuildLord", "IsTeamMember",
                "IsGroupOwner", "IsStudent", "IsCastle",
                "HaveValidHero", "CheckAuthen",
                // INI/Script
                "ReadIniSectionStr",
                // DB functions
                "PsFirst", "PsNext", "PsBof", "PsEof",
                "PsFieldName", "PsFieldByName",
                // String parsing (战神: 217+ calls)
                "StrToIntDef", "ObtainParamByIndex",
                // Lowercase aliases
                "inttostr", "strtoint", "strtointdef", "obtainparambyindex",
                // Global property access
                "PsRecordCount", "PsFieldCount",
            };

            _builtinProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Messaging
                "ServerSay", "NpcSay", "NpcNotice", "NpcSideNotice", "NpcMapNotice",
                // INI
                "WriteIniSectionStr",
                // Date/Time output procedures
                "PsDecodeDate", "PsDecodeTime",
                // Script DB
                "ExecuteScript", "ExecuteQuery",
                // Mail
                "NewFullMailEx",
                // Map events
                "CreateMapEvent", "RemoveMapEvent",
                "SetArrayLength",
                // Exit
                "Exit",
            };

            // Initialize global constants
            foreach (var c in _program.Consts)
                _globals[c.Name] = c.Value;

            // Initialize global variables
            foreach (var v in _program.GlobalVars)
            {
                _globals[v.Name] = CreateDefaultValue(v);
            }
        }

        private static PasValue CreateDefaultValue(PasVarDecl declaration)
        {
            if (declaration.IsArray)
            {
                var array = new PasArray(declaration.ArrayLow, declaration.ArrayHigh,
                    declaration.ArrayElementType);
                return PasValue.FromArray(array);
            }

            return IsStringType(declaration.TypeName)
                ? PasValue.FromString(string.Empty)
                : IsObjectType(declaration.TypeName) ? PasValue.Nil : PasValue.FromInt(0);
        }

        private static bool IsStringType(string typeName) =>
            string.Equals(typeName, "string", StringComparison.OrdinalIgnoreCase);

        private static bool IsObjectType(string typeName) => typeName?.ToLowerInvariant() is
            "tobject" or "tbaseobj" or "tcreature" or "thumankind" or "tplayer" or
            "tpsnpc" or "tmysqldb" or "tbasegroup" or "tbaseitem" or "tanimal" or "thero";

        /// <summary>Set a global variable (used for Compiler.inc merge).</summary>
        public void SetGlobal(string name, PasValue value)
        {
            _globals[name] = value;
        }

        // Recursion depth guard (Delphi Pascal Script typically has no recursion, guard at 50)
        private int _recursionDepth;
        private const int MaxRecursionDepth = 50;
        private int _executionSteps;
        private long _executionDeadlineTick;
        private const int MaxExecutionSteps = 200000;
        private const long MaxExecutionMillis = 1500;

        private void ResetExecutionBudget()
        {
            _executionSteps = 0;
            _executionDeadlineTick = Environment.TickCount64 + MaxExecutionMillis;
        }

        private void CheckExecutionBudget()
        {
            _executionSteps++;
            if (_executionSteps > MaxExecutionSteps)
                throw new PasRuntimeException($"Script execution step limit exceeded ({MaxExecutionSteps})");
            if ((_executionSteps & 0x3FF) == 0 && Environment.TickCount64 > _executionDeadlineTick)
                throw new PasRuntimeException($"Script execution time limit exceeded ({MaxExecutionMillis}ms)");
        }

        public PasValue ExecuteProcedure(string name, List<PasValue> args = null)
        {
            var proc = _program.Procedures.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (proc == null)
                throw new PasRuntimeException($"Procedure '{name}' not found");

            if (_recursionDepth == 0)
                ResetExecutionBudget();

            if (++_recursionDepth > MaxRecursionDepth)
                throw new PasRuntimeException($"Recursion depth exceeded ({MaxRecursionDepth}) in '{name}'");

            try
            {
                return ExecuteProcDecl(proc, args ?? new List<PasValue>());
            }
            finally
            {
                _recursionDepth--;
            }
        }

        public PasValue ExecuteMain()
        {
            ResetExecutionBudget();
            var savedExit = _exiting;
            _exiting = false;
            try
            {
                if (_program.MainBlock == null || _program.MainBlock.Statements.Count == 0)
                {
                    // Try _main procedure as fallback (common pattern)
                    foreach (var proc in _program.Procedures)
                    {
                        if (proc.Name.Equals("_main", StringComparison.OrdinalIgnoreCase) ||
                            proc.Name.Equals("main", StringComparison.OrdinalIgnoreCase))
                        {
                            return ExecuteProcedure(proc.Name);
                        }
                    }
                    return PasValue.Nil;
                }

                PushScope();
                try
                {
                    foreach (var stmt in _program.MainBlock.Statements)
                    {
                        if (_exiting) break;
                        ExecuteStatement(stmt);
                    }
                }
                finally
                {
                    PopScope();
                }
                return PasValue.Nil;
            }
            finally
            {
                _exiting = savedExit;
            }
        }

        // ========== Statement execution ==========

        private void ExecuteStatement(PasAstNode node)
        {
            if (node == null || _exiting) return;
            CheckExecutionBudget();

            switch (node)
            {
                case PasBlock block:
                    ExecuteBlock(block);
                    break;

                case PasAssignStmt assign:
                    ExecuteAssign(assign);
                    break;

                case PasIfStmt ifStmt:
                    ExecuteIf(ifStmt);
                    break;

                case PasCaseStmt caseStmt:
                    ExecuteCase(caseStmt);
                    break;

                case PasWhileStmt whileStmt:
                    ExecuteWhile(whileStmt);
                    break;

                case PasForStmt forStmt:
                    ExecuteFor(forStmt);
                    break;

                case PasRepeatStmt repeatStmt:
                    ExecuteRepeat(repeatStmt);
                    break;

                case PasCallStmt call:
                    if (call.IsMethod)
                        ExecuteMethodCall(call.ObjectName, call.Name, call.Arguments, call);
                    else
                        ExecuteCall(call, call.Name, call.Arguments);
                    break;

                case PasExitStmt exitStmt:
                    _exiting = true;
                    if (exitStmt.Value != null)
                        _functionResult = Evaluate(exitStmt.Value);
                    break;

                case PasTryStmt tryStmt:
                    ExecuteTry(tryStmt);
                    break;

                case PasRaiseStmt raiseStmt:
                    ExecuteRaise(raiseStmt);
                    break;

                case PasBreakStmt breakStmt:
                    _breaking = true;
                    break;

                case PasContinueStmt continueStmt:
                    _continuing = true;
                    break;

                case PasWithStmt withStmt:
                    ExecuteWith(withStmt);
                    break;

                case PasIncStmt incStmt:
                    ExecuteInc(incStmt);
                    break;

                case PasDecStmt decStmt:
                    ExecuteDec(decStmt);
                    break;

                case PasAssertStmt assertStmt:
                    ExecuteAssert(assertStmt);
                    break;

                case PasMemberAccessExpr member:
                    if (member.Target == null)
                        ExecuteMethodCall(member.ObjectName, member.MemberName,
                            new List<PasAstNode>(), member);
                    else
                        EvaluateMemberAccess(member);
                    break;

                case PasIdentifierExpr ident:
                    if (!_locals.ContainsKey(ident.Name) && !_globals.ContainsKey(ident.Name))
                        ExecuteCall(ident, ident.Name, new List<PasAstNode>());
                    break;

                default:
                    // Expression as statement - evaluate and discard
                    Evaluate(node);
                    break;
            }
        }

        private void ExecuteBlock(PasBlock block)
        {
            foreach (var stmt in block.Statements)
            {
                if (_exiting || _breaking || _continuing) break;
                ExecuteStatement(stmt);
            }
        }

        private void ExecuteAssign(PasAssignStmt assign)
        {
            var value = Evaluate(assign.Value);

            switch (assign.Target)
            {
                case PasIdentifierExpr ident:
                    if (ident.Name.Equals("Result", StringComparison.OrdinalIgnoreCase) ||
                        (_callStack.Count > 0 &&
                         _callStack.Peek().IsFunction &&
                         ident.Name.Equals(_callStack.Peek().Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        _functionResult = value;
                    }
                    else
                    {
                        SetVariable(ident.Name, value);
                    }
                    break;

                case PasMultiArrayAccessExpr arrayAccess:
                    {
                        var arr = GetVariable(arrayAccess.ArrayName).ArrVal;
                        if (arrayAccess.Indices.Count == 1)
                        {
                            var idx = Evaluate(arrayAccess.Indices[0]).AsInt();
                            arr[idx] = value;
                        }
                        else if (arrayAccess.Indices.Count == 2)
                        {
                            var i = Evaluate(arrayAccess.Indices[0]).AsInt();
                            var j = Evaluate(arrayAccess.Indices[1]).AsInt();
                            var inner = arr[i];
                            if (inner.Type == PasValueType.Array)
                            {
                                inner.ArrVal[j] = value;
                            }
                        }
                        break;
                    }

                case PasMemberAccessExpr member:
                    if (member.Target != null)
                    {
                        var target = Evaluate(member.Target);
                        if (target.Type == PasValueType.Object &&
                            target.ObjVal is TUserItem chainedItem &&
                            TrySetItemProperty(chainedItem, member.MemberName, value))
                            break;
                        if (target.Type == PasValueType.Object &&
                            target.ObjVal is TPlayObject chainedPlayer &&
                            TrySetPlayerProperty(chainedPlayer, member.MemberName, value))
                            break;
                        if (target.Type == PasValueType.Object &&
                            target.ObjVal is NormNpc chainedNpc &&
                            TrySetNpcProperty(chainedNpc, member.MemberName, value))
                            break;
                    }
                    else
                    {
                        if (TryResolveItemTarget(member, out var targetItem) &&
                            TrySetItemProperty(targetItem, member.MemberName, value))
                            break;
                        if (TryResolvePlayerTarget(member, out var targetPlayer) &&
                            TrySetPlayerProperty(targetPlayer, member.MemberName, value))
                            break;
                        if (TryResolveNpcTarget(member, out var targetNpc) &&
                            TrySetNpcProperty(targetNpc, member.MemberName, value))
                            break;
                    }
                    throw new PasRuntimeException($"Invalid assignment target: {DescribeMember(member)}");

                default:
                    throw new PasRuntimeException($"Invalid assignment target: {assign.Target?.GetType().Name}");
            }
        }

        private void ExecuteIf(PasIfStmt ifStmt)
        {
            var cond = Evaluate(ifStmt.Condition);
            if (cond.AsBool())
            {
                if (ifStmt.ThenBlock is PasBlock thenBlock)
                    ExecuteBlock(thenBlock);
                else
                    ExecuteStatement(ifStmt.ThenBlock);
            }
            else if (ifStmt.ElseBlock != null)
            {
                if (ifStmt.ElseBlock is PasBlock elseBlock)
                    ExecuteBlock(elseBlock);
                else
                    ExecuteStatement(ifStmt.ElseBlock);
            }
        }

        private void ExecuteCase(PasCaseStmt caseStmt)
        {
            var val = Evaluate(caseStmt.Expression);
            PasCaseBranch defaultBranch = null;

            foreach (var branch in caseStmt.Branches)
            {
                if (branch.Values.Count == 0)
                {
                    defaultBranch = branch;
                    continue;
                }

                foreach (var caseVal in branch.Values)
                {
                    var cv = Evaluate(caseVal);
                    if (val.Equals(cv))
                    {
                        if (branch.Body is PasBlock body)
                            ExecuteBlock(body);
                        else
                            ExecuteStatement(branch.Body);
                        return;
                    }
                }
            }

            if (defaultBranch?.Body is PasBlock defaultBody)
                ExecuteBlock(defaultBody);
            else if (defaultBranch?.Body != null)
                ExecuteStatement(defaultBranch.Body);
        }

        private void ExecuteWhile(PasWhileStmt whileStmt)
        {
            while (Evaluate(whileStmt.Condition).AsBool() && !_exiting)
            {
                if (whileStmt.Body is PasBlock body)
                    ExecuteBlock(body);
                else
                    ExecuteStatement(whileStmt.Body);

                if (_breaking) { _breaking = false; break; }
                _continuing = false;
            }
        }

        private void ExecuteFor(PasForStmt forStmt)
        {
            var from = Evaluate(forStmt.From).AsInt();
            var to = Evaluate(forStmt.To).AsInt();

            if (forStmt.DownTo)
            {
                for (var i = from; i >= to && !_exiting; i--)
                {
                    CheckExecutionBudget();
                    SetVariable(forStmt.VarName, PasValue.FromInt(i));
                    if (forStmt.Body is PasBlock body)
                        ExecuteBlock(body);
                    else
                        ExecuteStatement(forStmt.Body);

                    if (_breaking) { _breaking = false; break; }
                    _continuing = false;
                }
            }
            else
            {
                for (var i = from; i <= to && !_exiting; i++)
                {
                    CheckExecutionBudget();
                    SetVariable(forStmt.VarName, PasValue.FromInt(i));
                    if (forStmt.Body is PasBlock body)
                        ExecuteBlock(body);
                    else
                        ExecuteStatement(forStmt.Body);

                    if (_breaking) { _breaking = false; break; }
                    _continuing = false;
                }
            }
        }

        private void ExecuteRepeat(PasRepeatStmt repeatStmt)
        {
            do
            {
                if (_exiting) break;
                ExecuteBlock(repeatStmt.Body);

                if (_breaking) { _breaking = false; break; }
                _continuing = false;
            } while (!Evaluate(repeatStmt.Condition).AsBool() && !_exiting);
        }

        // ===== NEW: try...except...end =====

        private void ExecuteTry(PasTryStmt tryStmt)
        {
            try
            {
                ExecuteBlock(tryStmt.Body);
            }
            catch (PasRuntimeException ex)
            {
                _lastExceptionType = ex.GetType().Name;
                _lastExceptionParam = ex.Message;
                bool handled = false;
                foreach (var handler in tryStmt.ExceptHandlers)
                {
                    if (string.IsNullOrEmpty(handler.ExceptionType) ||
                        string.Equals(handler.ExceptionType, "Exception", StringComparison.OrdinalIgnoreCase))
                    {
                        // Catch-all or explicit Exception handler
                        if (!string.IsNullOrEmpty(handler.VariableName))
                            _locals[handler.VariableName] = PasValue.FromString(ex.Message);
                        if (handler.Body is PasBlock block)
                            ExecuteBlock(block);
                        else
                            ExecuteStatement(handler.Body);
                        handled = true;
                        break;
                    }
                    else if (string.Equals(handler.ExceptionType, "PasRuntimeException", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(handler.VariableName))
                            _locals[handler.VariableName] = PasValue.FromString(ex.Message);
                        if (handler.Body is PasBlock block)
                            ExecuteBlock(block);
                        else
                            ExecuteStatement(handler.Body);
                        handled = true;
                        break;
                    }
                }
                if (!handled)
                    throw; // re-raise if unhandled
            }
            catch (Exception ex)
            {
                _lastExceptionType = ex.GetType().Name;
                _lastExceptionParam = ex.Message;
                bool handled = false;
                foreach (var handler in tryStmt.ExceptHandlers)
                {
                    if (string.IsNullOrEmpty(handler.ExceptionType))
                    {
                        if (!string.IsNullOrEmpty(handler.VariableName))
                            _locals[handler.VariableName] = PasValue.FromString(ex.Message);
                        if (handler.Body is PasBlock block)
                            ExecuteBlock(block);
                        else
                            ExecuteStatement(handler.Body);
                        handled = true;
                        break;
                    }
                }
                if (!handled)
                    throw;
            }
            finally
            {
                if (tryStmt.FinallyBlock != null)
                    ExecuteBlock(tryStmt.FinallyBlock);
            }
        }

        // ===== NEW: raise; / raise Exception.Create('msg'); =====

        private void ExecuteRaise(PasRaiseStmt raiseStmt)
        {
            string msg = "Exception raised";
            if (raiseStmt.Exception != null)
            {
                var val = Evaluate(raiseStmt.Exception);
                msg = val.AsString();
            }
            throw new PasRuntimeException(msg);
        }

        // ===== NEW: with Obj do begin ... end; =====

        private void ExecuteWith(PasWithStmt withStmt)
        {
            // with This_Player, This_Npc do ...
            // Makes object members accessible without prefix
            // For each object, push properties into a temporary scope
            PushScope();
            try
            {
                foreach (var obj in withStmt.Objects)
                {
                    if (obj is PasIdentifierExpr ident)
                    {
                        if (ident.Name.Equals("This_Player", StringComparison.OrdinalIgnoreCase))
                        {
                            // Mirror player properties into local scope
                            if (_api.CurrentPlayer != null)
                            {
                                foreach (var propName in new[] {
                                    "Level", "Name", "MapName", "X", "Y", "HP", "MaxHP", "MP", "MaxMP",
                                    "GoldNum", "Job", "Gender", "MyPKPoint", "FreeBagNum", "IsDead",
                                    "My_X", "My_Y", "GuildName", "IsGuildLord", "YBNum", "MyShengWan",
                                    "MyDiamondNum", "MyExp", "MyLFNum", "GameGold", "IsTeamMember",
                                    "IsGroupOwner", "DynRoomName", "DynRoomIdx" })
                                {
                                    if (_api.GetPlayerProperty(propName, out var val))
                                        _locals[propName] = val;
                                }
                            }
                        }
                        else if (ident.Name.Equals("This_Npc", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_api.CurrentNpc != null && _api.GetNpcProperty("Name", out var nameVal))
                                _locals["Name"] = nameVal;
                        }
                        else if (ident.Name.Equals("This_Animal", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_api.CurrentAnimal == null) continue;
                            foreach (var propName in new[] { "Name", "MapName", "MapDesc", "My_X", "My_Y" })
                            {
                                if (_api.GetAnimalProperty(propName, out var val))
                                    _locals[propName] = val;
                            }
                        }
                    }
                }

                if (withStmt.Body is PasBlock block)
                    ExecuteBlock(block);
                else
                    ExecuteStatement(withStmt.Body);
            }
            finally
            {
                PopScope();
            }
        }

        // ===== NEW: Inc(varname); / Inc(varname, amount); =====

        private void ExecuteInc(PasIncStmt incStmt)
        {
            var amount = incStmt.Amount != null ? Evaluate(incStmt.Amount).AsInt() : 1;
            var val = GetVariable(incStmt.VariableName);
            SetVariable(incStmt.VariableName, PasValue.FromInt(val.AsInt() + amount));
        }

        // ===== NEW: Dec(varname); / Dec(varname, amount); =====

        private void ExecuteDec(PasDecStmt decStmt)
        {
            var amount = decStmt.Amount != null ? Evaluate(decStmt.Amount).AsInt() : 1;
            var val = GetVariable(decStmt.VariableName);
            SetVariable(decStmt.VariableName, PasValue.FromInt(val.AsInt() - amount));
        }

        // ===== NEW: Assert(condition); / Assert(condition, message); =====

        private void ExecuteAssert(PasAssertStmt assertStmt)
        {
            var cond = Evaluate(assertStmt.Condition);
            if (!cond.AsBool())
            {
                var msg = assertStmt.Message != null
                    ? Evaluate(assertStmt.Message).AsString()
                    : "Assertion failed";
                throw new PasRuntimeException(msg);
            }
        }

        private PasValue ExecuteCall(PasAstNode callSite, string name, List<PasAstNode> args)
        {
            var evaluatedArgs = EvaluateArgs(args, out var references);
            if (TryInvokeGlobalAt(callSite, name, evaluatedArgs, references, out var result))
                return result;

            // Terminal miss: no builtin, no script proc, and no bridge case on any surface.
            // Log once per name so the silent fall-through class is observable; the throw
            // below is unchanged.
            PasApiBridge.TraceUnknownPasName("Global", name);
            throw new PasRuntimeException($"函数找不到: '{name}'");
        }

        private bool TryInvokeGlobalAt(PasAstNode callSite, string name,
            List<PasValue> args, List<Action<PasValue>> references, out PasValue result)
        {
            IDisposable yanshenCall = null;
            try
            {
                yanshenCall = _api.BeginYanshenScriptApiCall(name);
                return TryInvokeGlobal(name, args, references, out result);
            }
            catch (YanshenApiUnavailableException ex)
            {
                throw CreateYanshenApiNotFound(callSite, ex);
            }
            finally
            {
                yanshenCall?.Dispose();
            }
        }

        private PasRuntimeException CreateYanshenApiNotFound(PasAstNode callSite,
            YanshenApiUnavailableException exception)
        {
            var procedure = _callStack.Count > 0 ? _callStack.Peek().Name : "主程序";
            var file = callSite?.SourceFile;
            if (string.IsNullOrWhiteSpace(file) && _callStack.Count > 0)
                file = _callStack.Peek().SourceFile;
            if (string.IsNullOrWhiteSpace(file)) file = "<未知>";

            var line = callSite?.SourceLine ?? 0;
            var column = callSite?.SourceColumn ?? 0;
            return new PasRuntimeException(
                $"API函数找不到 | API={exception.FunctionName} | 文件={file} | " +
                $"过程/函数={procedure} | 行={line} | 列={column} | 原因={exception.FailureReason}",
                exception);
        }

        private bool TryInvokeGlobal(string name, List<PasValue> args,
            List<Action<PasValue>> references, out PasValue result)
        {
            result = PasValue.Nil;
            if (_builtinFuncs.Contains(name))
            {
                result = ExecuteBuiltinFunction(name, args);
                CopyBackBridgeArguments(name, references, args);
                return true;
            }

            if (_builtinProcs.Contains(name))
            {
                ExecuteBuiltinProcedure(name, args);
                CopyBackBridgeArguments(name, references, args);
                return true;
            }

            var proc = _program.Procedures.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (proc != null)
            {
                result = ExecuteProcDecl(proc, args);
                CopyBackProcedureArguments(proc, references, args);
                return true;
            }

            if (_api.CallStandaloneFunction(name, args, out result))
            {
                CopyBackBridgeArguments(name, references, args);
                return true;
            }

            if (_api.TryCallThisPlayerFunc(name, args, out result))
            {
                CopyBackBridgeArguments(name, references, args);
                return true;
            }

            return false;
        }

        private void ExecuteMethodCall(string objectName, string method,
            List<PasAstNode> args, PasAstNode callSite)
        {
            var evaluatedArgs = EvaluateArgs(args, out var references);
            if (TryInvokeMethodAt(callSite, objectName, method, evaluatedArgs, references, out _))
                return;

            if (objectName.Equals("This_Player", StringComparison.OrdinalIgnoreCase) && evaluatedArgs.Count == 1 &&
                _api.SetPlayerProperty(method, evaluatedArgs[0]))
            {
                return;
            }

            // Terminal miss on the object-method surfaces (This_Player / This_Npc / This_DB
            // funcs, then methods, then the property-set fallback above). This is the form
            // that ABORTS the script mid-way with earlier side-effects already persisted, so
            // it is the highest-value trace point. The throw below is unchanged.
            PasApiBridge.TraceUnknownPasName(objectName, method);
            throw new PasRuntimeException($"函数找不到: '{objectName}.{method}'");
        }

        private bool TryInvokeMethodAt(PasAstNode callSite, string objectName, string method,
            List<PasValue> args, List<Action<PasValue>> references, out PasValue result)
        {
            try
            {
                return TryInvokeMethod(objectName, method, args, references, out result);
            }
            catch (YanshenApiUnavailableException ex)
            {
                throw CreateYanshenApiNotFound(callSite, ex);
            }
        }

        private bool TryInvokeObjectMethodAt(PasAstNode callSite, object target, string method,
            List<PasValue> args, List<Action<PasValue>> references, out PasValue result)
        {
            try
            {
                return TryInvokeObjectMethod(target, method, args, references, out result);
            }
            catch (YanshenApiUnavailableException ex)
            {
                throw CreateYanshenApiNotFound(callSite, ex);
            }
        }

        private bool TryInvokeMethod(string objectName, string method, List<PasValue> args,
            List<Action<PasValue>> references, out PasValue result)
        {
            result = PasValue.Nil;
            if (TryGetObjectValue(objectName, out var objectValue))
            {
                if (TryInvokeObjectMethod(objectValue, method, args, references, out result))
                    return true;
            }

            if (objectName.Equals("This_Player", StringComparison.OrdinalIgnoreCase))
            {
                return TryInvokePlayerMethod(method, args, references, out result);
            }
            else if (objectName.Equals("This_Npc", StringComparison.OrdinalIgnoreCase))
            {
                return TryInvokeNpcMethod(method, args, references, out result);
            }
            else if (objectName.Equals("This_DB", StringComparison.OrdinalIgnoreCase))
            {
                if (_api.CallDbMethod(method, args, out result))
                {
                    CopyBackBridgeArguments(method, references, args);
                    return true;
                }
            }

            return false;
        }

        private bool TryInvokeObjectMethod(object target, string method, List<PasValue> args,
            List<Action<PasValue>> references, out PasValue result)
        {
            if (target is TPlayObject targetPlayer)
            {
                if (ReferenceEquals(targetPlayer, _api.CurrentPlayer))
                    return TryInvokePlayerMethod(method, args, references, out result);
                var npc = _api.CurrentNpc;
                var inputOk = _api.CurrentInputOk;
                var inputStr = _api.CurrentInputStr;
                using var context = _api.PushItemContext(targetPlayer, npc, inputOk, inputStr, _api.CurrentItem);
                return TryInvokePlayerMethod(method, args, references, out result);
            }
            if (target is NormNpc targetNpc)
            {
                if (ReferenceEquals(targetNpc, _api.CurrentNpc))
                    return TryInvokeNpcMethod(method, args, references, out result);
                var player = _api.CurrentPlayer;
                var inputOk = _api.CurrentInputOk;
                var inputStr = _api.CurrentInputStr;
                using var context = _api.PushItemContext(player, targetNpc, inputOk, inputStr, _api.CurrentItem);
                return TryInvokeNpcMethod(method, args, references, out result);
            }
            result = PasValue.Nil;
            return false;
        }

        private bool TryInvokePlayerMethod(string method, List<PasValue> args,
            List<Action<PasValue>> references, out PasValue result)
        {
            var isYanshenSignInTunnel =
                method.Equals("getsigninactprizer", StringComparison.OrdinalIgnoreCase)
                && PasApiBridge.IsYanshenSignInTunnelCall(args);
            if (_api.CallPlayerFunc(method, args, out result) || _api.CallPlayerMethod(method, args))
            {
                CopyBackBridgeArguments(method, references, args,
                    isYanshenSignInTunnel);
                return true;
            }
            result = PasValue.Nil;
            return false;
        }

        private bool TryInvokeNpcMethod(string method, List<PasValue> args,
            List<Action<PasValue>> references, out PasValue result)
        {
            if (_api.CallNpcFunc(method, args, out result) || _api.CallNpcMethod(method, args, out result))
            {
                CopyBackBridgeArguments(method, references, args);
                return true;
            }
            result = PasValue.Nil;
            return false;
        }

        private bool TryGetObjectValue(string name, out object value)
        {
            value = null;
            if (_locals.TryGetValue(name, out var local) && local.Type == PasValueType.Object)
                value = local.ObjVal;
            else if (_globals.TryGetValue(name, out var global) && global.Type == PasValueType.Object)
                value = global.ObjVal;
            return value != null;
        }

        // ========== Expression evaluation ==========

        public PasValue Evaluate(PasAstNode node)
        {
            CheckExecutionBudget();
            switch (node)
            {
                case PasLiteralExpr lit:
                    return lit.Value;

                case PasIdentifierExpr ident:
                    if (ident.Name.Equals("Result", StringComparison.OrdinalIgnoreCase))
                        return _functionResult;
                    if (_locals.TryGetValue(ident.Name, out var localValue))
                        return localValue;
                    if (_globals.TryGetValue(ident.Name, out var globalValue))
                        return globalValue;
                    if (ident.Name.Equals("This_Player", StringComparison.OrdinalIgnoreCase) ||
                        ident.Name.Equals("This_Npc", StringComparison.OrdinalIgnoreCase) ||
                        ident.Name.Equals("This_Animal", StringComparison.OrdinalIgnoreCase) ||
                        ident.Name.Equals("This_DB", StringComparison.OrdinalIgnoreCase) ||
                        ident.Name.Equals("This_Item", StringComparison.OrdinalIgnoreCase) ||
                        ident.Name.Equals("ExceptionType", StringComparison.OrdinalIgnoreCase) ||
                        ident.Name.Equals("ExceptionParam", StringComparison.OrdinalIgnoreCase))
                        return GetVariable(ident.Name);
                    if (TryInvokeGlobalAt(ident, ident.Name, new List<PasValue>(),
                        new List<Action<PasValue>>(), out var bareResult))
                        return bareResult;
                    return GetVariable(ident.Name);

                case PasBinaryOpExpr binOp:
                    return EvaluateBinaryOp(binOp);

                case PasUnaryOpExpr unaryOp:
                    return EvaluateUnaryOp(unaryOp);

                case PasMemberAccessExpr member:
                    return EvaluateMemberAccess(member);

                case PasMethodCallExpr methodCall:
                    return EvaluateMethodCall(methodCall);

                case PasCallStmt call:
                    if (call.IsMethod)
                        return EvaluateMethodCall(new PasMethodCallExpr
                        {
                            ObjectName = call.ObjectName,
                            MethodName = call.Name,
                            Arguments = call.Arguments,
                            SourceFile = call.SourceFile,
                            SourceLine = call.SourceLine,
                            SourceColumn = call.SourceColumn
                        });
                    return EvaluateCallExpression(call);

                case PasMultiArrayAccessExpr arrayAccess:
                    {
                        var arr = GetVariable(arrayAccess.ArrayName).ArrVal;
                        if (arrayAccess.Indices.Count == 1)
                            return arr[Evaluate(arrayAccess.Indices[0]).AsInt()];
                        if (arrayAccess.Indices.Count == 2)
                        {
                            var innerArr = arr[Evaluate(arrayAccess.Indices[0]).AsInt()];
                            return innerArr.ArrVal[Evaluate(arrayAccess.Indices[1]).AsInt()];
                        }
                        return PasValue.Nil;
                    }

                case PasBlock block:
                    ExecuteBlock(block);
                    return _functionResult;

                default:
                    ExecuteStatement(node);
                    return _functionResult;
            }
        }

        private PasValue EvaluateCallExpression(PasCallStmt call)
        {
            var savedExit = _exiting;
            var savedResult = _functionResult;
            _exiting = false;
            try
            {
                return ExecuteCall(call, call.Name, call.Arguments);
            }
            finally
            {
                _functionResult = savedResult;
                _exiting = savedExit;
            }
        }

        private PasValue EvaluateBinaryOp(PasBinaryOpExpr binOp)
        {
            var left = Evaluate(binOp.Left);
            if (binOp.Op.Equals("and", StringComparison.OrdinalIgnoreCase) && !left.AsBool())
                return PasValue.FromBool(false);
            if (binOp.Op.Equals("or", StringComparison.OrdinalIgnoreCase) && left.AsBool())
                return PasValue.FromBool(true);

            var right = Evaluate(binOp.Right);

            switch (binOp.Op)
            {
                case "+": return left + right;
                case "-": return left - right;
                case "*": return left * right;
                case "/": return left / right;
                case "div":
                    if (right.AsInt() == 0) throw new PasRuntimeException("Division by zero");
                    return PasValue.FromInt(left.AsInt() / right.AsInt());
                case "mod":
                    if (right.AsInt() == 0) throw new PasRuntimeException("Modulo by zero");
                    return PasValue.FromInt(left.AsInt() % right.AsInt());
                case "=":  return left == right;
                case "<>": return left != right;
                case "<":  return left < right;
                case ">":  return left > right;
                case "<=": return left <= right;
                case ">=": return left >= right;
                case "and": return PasValue.FromBool(left.AsBool() && right.AsBool());
                case "or":  return PasValue.FromBool(left.AsBool() || right.AsBool());
                default: throw new PasRuntimeException($"Unknown operator: {binOp.Op}");
            }
        }

        private PasValue EvaluateUnaryOp(PasUnaryOpExpr unaryOp)
        {
            var operand = Evaluate(unaryOp.Operand);
            switch (unaryOp.Op)
            {
                case "-": return PasValue.FromInt(-operand.AsInt());
                case "not": return PasValue.FromBool(!operand.AsBool());
                default: throw new PasRuntimeException($"Unknown unary operator: {unaryOp.Op}");
            }
        }

        private PasValue EvaluateMemberAccess(PasMemberAccessExpr member)
        {
            if (member.Target != null)
            {
                var target = Evaluate(member.Target);
                if (target.Type != PasValueType.Object || target.ObjVal == null)
                    throw new PasRuntimeException($"Object target is nil: {DescribeMember(member)}");
                if (TryInvokeObjectMethodAt(member, target.ObjVal, member.MemberName, new List<PasValue>(),
                    new List<Action<PasValue>>(), out var targetMethodResult))
                    return targetMethodResult;
                if (target.ObjVal is TPlayObject chainedPlayer &&
                    TryGetPlayerProperty(chainedPlayer, member.MemberName, out var targetValue))
                    return targetValue;
                if (target.ObjVal is NormNpc targetNpc &&
                    TryGetNpcProperty(targetNpc, member.MemberName, out targetValue))
                    return targetValue;
                if (target.ObjVal is TUserItem targetItem &&
                    TryGetItemProperty(targetItem, member.MemberName, out targetValue))
                    return targetValue;
                if (ReferenceEquals(target.ObjVal, _api.CurrentAnimal) &&
                    target.ObjVal is TBaseObject &&
                    _api.GetAnimalProperty(member.MemberName, out targetValue))
                    return targetValue;
                throw new PasRuntimeException($"Unknown member: {DescribeMember(member)}");
            }

            if (member.ObjectName.Equals("This_DB", StringComparison.OrdinalIgnoreCase))
            {
                if (_api.GetDbProperty(member.MemberName, out var dbValue))
                    return dbValue;
                throw new PasRuntimeException($"Unknown member: {member.ObjectName}.{member.MemberName}");
            }

            if (TryInvokeMethodAt(member, member.ObjectName, member.MemberName, new List<PasValue>(),
                new List<Action<PasValue>>(), out var methodResult))
                return methodResult;

            if (TryGetObjectValue(member.ObjectName, out var objectValue) && objectValue is TPlayObject targetPlayer)
            {
                if (ReferenceEquals(targetPlayer, _api.CurrentPlayer))
                {
                    if (_api.GetPlayerProperty(member.MemberName, out var targetValue)) return targetValue;
                }
                else
                {
                    var npc = _api.CurrentNpc;
                    var inputOk = _api.CurrentInputOk;
                    var inputStr = _api.CurrentInputStr;
                    using var context = _api.PushItemContext(targetPlayer, npc, inputOk, inputStr, _api.CurrentItem);
                    if (_api.GetPlayerProperty(member.MemberName, out var targetValue)) return targetValue;
                }
            }

            if (member.ObjectName.Equals("This_Player", StringComparison.OrdinalIgnoreCase))
            {
                if (_api.GetPlayerProperty(member.MemberName, out var val))
                    return val;
            }
            else if (member.ObjectName.Equals("This_Npc", StringComparison.OrdinalIgnoreCase))
            {
                if (_api.GetNpcProperty(member.MemberName, out var val))
                    return val;
            }
            else if (member.ObjectName.Equals("This_Animal", StringComparison.OrdinalIgnoreCase))
            {
                if (_api.GetAnimalProperty(member.MemberName, out var val))
                    return val;
            }
            else if (member.ObjectName.Equals("This_Item", StringComparison.OrdinalIgnoreCase))
            {
                if (_api.GetItemProperty(member.MemberName, out var val))
                    return val;
            }

            throw new PasRuntimeException($"Unknown member: {member.ObjectName}.{member.MemberName}");
        }

        private PasValue EvaluateMethodCall(PasMethodCallExpr methodCall)
        {
            PasValue target = PasValue.Nil;
            if (methodCall.Target != null)
                target = Evaluate(methodCall.Target);
            var args = EvaluateArgs(methodCall.Arguments, out var references);
            if (methodCall.Target != null)
            {
                if (target.Type == PasValueType.Object && target.ObjVal != null &&
                    TryInvokeObjectMethodAt(methodCall, target.ObjVal, methodCall.MethodName,
                        args, references, out var targetResult))
                    return targetResult;
                throw new PasRuntimeException($"Unknown method: {DescribeMethod(methodCall)}(...)");
            }
            if (TryInvokeMethodAt(methodCall, methodCall.ObjectName, methodCall.MethodName,
                    args, references, out var result))
                return result;

            throw new PasRuntimeException($"Unknown method: {methodCall.ObjectName}.{methodCall.MethodName}(...)");
        }

        private bool TryResolvePlayerTarget(PasMemberAccessExpr member, out TPlayObject player)
        {
            player = null;
            if (TryGetObjectValue(member.ObjectName, out var target) && target is TPlayObject objectPlayer)
            {
                player = objectPlayer;
                return true;
            }
            if (member.ObjectName.Equals("This_Player", StringComparison.OrdinalIgnoreCase))
            {
                player = _api.CurrentPlayer;
                return player != null;
            }
            return false;
        }

        private bool TryResolveItemTarget(PasMemberAccessExpr member, out TUserItem item)
        {
            item = null;
            if (TryGetObjectValue(member.ObjectName, out var target) && target is TUserItem objectItem)
            {
                item = objectItem;
                return true;
            }
            if (member.ObjectName.Equals("This_Item", StringComparison.OrdinalIgnoreCase))
            {
                item = _api.CurrentItem;
                return item != null;
            }
            return false;
        }

        private bool TryResolveNpcTarget(PasMemberAccessExpr member, out NormNpc npc)
        {
            npc = null;
            if (TryGetObjectValue(member.ObjectName, out var target) && target is NormNpc objectNpc)
            {
                npc = objectNpc;
                return true;
            }
            if (member.ObjectName.Equals("This_Npc", StringComparison.OrdinalIgnoreCase))
            {
                npc = _api.CurrentNpc;
                return npc != null;
            }
            return false;
        }

        private bool TryGetPlayerProperty(TPlayObject player, string name, out PasValue value)
        {
            if (ReferenceEquals(player, _api.CurrentPlayer))
                return _api.GetPlayerProperty(name, out value);
            var npc = _api.CurrentNpc;
            var inputOk = _api.CurrentInputOk;
            var inputStr = _api.CurrentInputStr;
            using var context = _api.PushItemContext(player, npc, inputOk, inputStr, _api.CurrentItem);
            return _api.GetPlayerProperty(name, out value);
        }

        private bool TryGetNpcProperty(NormNpc npc, string name, out PasValue value)
        {
            if (ReferenceEquals(npc, _api.CurrentNpc))
                return _api.GetNpcProperty(name, out value);
            var player = _api.CurrentPlayer;
            var inputOk = _api.CurrentInputOk;
            var inputStr = _api.CurrentInputStr;
            using var context = _api.PushItemContext(player, npc, inputOk, inputStr, _api.CurrentItem);
            return _api.GetNpcProperty(name, out value);
        }

        private bool TrySetPlayerProperty(TPlayObject player, string name, PasValue value)
        {
            if (ReferenceEquals(player, _api.CurrentPlayer))
                return _api.SetPlayerProperty(name, value);
            var npc = _api.CurrentNpc;
            var inputOk = _api.CurrentInputOk;
            var inputStr = _api.CurrentInputStr;
            using var context = _api.PushItemContext(player, npc, inputOk, inputStr, _api.CurrentItem);
            return _api.SetPlayerProperty(name, value);
        }

        private bool TrySetNpcProperty(NormNpc npc, string name, PasValue value)
        {
            if (ReferenceEquals(npc, _api.CurrentNpc))
                return _api.SetNpcProperty(name, value);
            var player = _api.CurrentPlayer;
            var inputOk = _api.CurrentInputOk;
            var inputStr = _api.CurrentInputStr;
            using var context = _api.PushItemContext(player, npc, inputOk, inputStr, _api.CurrentItem);
            return _api.SetNpcProperty(name, value);
        }

        private bool TryGetItemProperty(TUserItem item, string name, out PasValue value)
        {
            if (ReferenceEquals(item, _api.CurrentItem))
                return _api.GetItemProperty(name, out value);
            using var context = _api.PushItemContext(_api.CurrentPlayer, _api.CurrentNpc,
                _api.CurrentInputOk, _api.CurrentInputStr, item);
            return _api.GetItemProperty(name, out value);
        }

        private bool TrySetItemProperty(TUserItem item, string name, PasValue value)
        {
            if (ReferenceEquals(item, _api.CurrentItem))
                return _api.SetItemProperty(name, value);
            using var context = _api.PushItemContext(_api.CurrentPlayer, _api.CurrentNpc,
                _api.CurrentInputOk, _api.CurrentInputStr, item);
            return _api.SetItemProperty(name, value);
        }

        private static string DescribeMember(PasMemberAccessExpr member) =>
            member.Target == null ? $"{member.ObjectName}.{member.MemberName}" : $"<expression>.{member.MemberName}";

        private static string DescribeMethod(PasMethodCallExpr method) =>
            method.Target == null ? $"{method.ObjectName}.{method.MethodName}" : $"<expression>.{method.MethodName}";

        private PasValue GetVariable(string name)
        {
            if (name.Equals("Result", StringComparison.OrdinalIgnoreCase))
                return _functionResult;
            // Check local scope first, then global
            if (_locals.TryGetValue(name, out var val))
                return val;
            if (_globals.TryGetValue(name, out val))
                return val;
            // Check API globals
            if (name.Equals("This_Player", StringComparison.OrdinalIgnoreCase))
                return PasValue.FromObject(_api.CurrentPlayer);
            if (name.Equals("This_Npc", StringComparison.OrdinalIgnoreCase))
                return PasValue.FromObject(_api.CurrentNpc);
            if (name.Equals("This_Animal", StringComparison.OrdinalIgnoreCase))
                return PasValue.FromObject(_api.CurrentAnimal);
            if (name.Equals("This_Item", StringComparison.OrdinalIgnoreCase))
                return PasValue.FromObject(_api.CurrentItem);
            if (name.Equals("ExceptionType", StringComparison.OrdinalIgnoreCase))
                return PasValue.FromString(_lastExceptionType);
            if (name.Equals("ExceptionParam", StringComparison.OrdinalIgnoreCase))
                return PasValue.FromString(_lastExceptionParam);
            // Return 0 for undefined (Pascal allows reading uninitialized variables)
            return PasValue.FromInt(0);
        }

        private void SetVariable(string name, PasValue value)
        {
            if (name.Equals("Result", StringComparison.OrdinalIgnoreCase))
            {
                _functionResult = value;
                return;
            }
            if (_locals.ContainsKey(name))
                _locals[name] = value;
            else
                _globals[name] = value;
        }

        // ========== Procedure execution ==========

        private PasValue ExecuteProcDecl(PasProcDecl proc, List<PasValue> args)
        {
            PushScope();
            _callStack.Push(proc);
            var initializationPlugin = _api.BeginYanshenInitialization(proc.Name,
                proc.SourceFile,
                out var wasYanshenInitialized);
            var initializationSucceeded = false;
            var savedExit = _exiting;
            _exiting = false;
            var savedResult = _functionResult;
            _functionResult = proc.IsFunction
                ? CreateDefaultValue(new PasVarDecl
                {
                    TypeName = proc.ReturnType,
                    IsArray = proc.ReturnIsArray,
                    IsDynamicArray = proc.ReturnIsArray,
                    ArrayLow = 0,
                    ArrayHigh = proc.ReturnIsArray ? -1 : 0,
                    ArrayElementType = proc.ReturnArrayElementType
                })
                : PasValue.Nil;

            try
            {
                for (int i = 0; i < proc.Parameters.Count; i++)
                {
                    var parameter = proc.Parameters[i];
                    _locals[parameter.Name] = parameter.ParameterMode == PasParameterMode.Out || i >= args.Count
                        ? CreateDefaultValue(parameter)
                        : args[i];
                }

                // Initialize local variables
                foreach (var v in proc.LocalVars)
                {
                    _locals[v.Name] = CreateDefaultValue(v);
                }

                // Execute body
                if (proc.Body != null)
                {
                    ExecuteBlock(proc.Body);
                }

                initializationSucceeded = true;
                return _functionResult;
            }
            finally
            {
                PasApiBridge.EndYanshenInitialization(initializationPlugin,
                    wasYanshenInitialized, initializationSucceeded);
                for (int i = 0; i < proc.Parameters.Count && i < args.Count; i++)
                {
                    var parameter = proc.Parameters[i];
                    if (parameter.IsByRef && _locals.TryGetValue(parameter.Name, out var value))
                        args[i] = value;
                }
                _exiting = savedExit;
                _functionResult = savedResult;
                _callStack.Pop();
                PopScope();
            }
        }

        // ========== Scope management ==========

        private void PushScope()
        {
            _scopeStack.Push(_locals);
            _locals = new Dictionary<string, PasValue>(StringComparer.OrdinalIgnoreCase);
        }

        private void PopScope()
        {
            _locals = _scopeStack.Pop();
        }

        // ========== Argument evaluation ==========

        private List<PasValue> EvaluateArgs(List<PasAstNode> args, out List<Action<PasValue>> references)
        {
            var result = new List<PasValue>();
            references = new List<Action<PasValue>>();
            foreach (var arg in args)
            {
                result.Add(EvaluateArgument(arg, out var reference));
                references.Add(reference);
            }
            return result;
        }

        private PasValue EvaluateArgument(PasAstNode argument, out Action<PasValue> reference)
        {
            reference = null;
            if (argument is PasIdentifierExpr identifier)
            {
                var name = identifier.Name;
                if (name.Equals("Result", StringComparison.OrdinalIgnoreCase))
                {
                    reference = newValue => _functionResult = newValue;
                    return _functionResult;
                }
                var isVariable = !_program.Consts.Any(constant =>
                        constant.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                    (_locals.ContainsKey(name) || _globals.ContainsKey(name));
                var value = Evaluate(identifier);
                if (isVariable)
                    reference = newValue => SetVariable(name, newValue);
                return value;
            }

            if (argument is PasArrayAccessExpr arrayAccess)
            {
                CheckExecutionBudget();
                var array = GetVariable(arrayAccess.ArrayName).ArrVal;
                var index = Evaluate(arrayAccess.Index).AsInt();
                reference = newValue => array[index] = newValue;
                return array[index];
            }

            if (argument is PasMultiArrayAccessExpr multiArrayAccess)
            {
                CheckExecutionBudget();
                var array = GetVariable(multiArrayAccess.ArrayName).ArrVal;
                if (multiArrayAccess.Indices.Count == 1)
                {
                    var index = Evaluate(multiArrayAccess.Indices[0]).AsInt();
                    reference = newValue => array[index] = newValue;
                    return array[index];
                }
                if (multiArrayAccess.Indices.Count == 2)
                {
                    var outerIndex = Evaluate(multiArrayAccess.Indices[0]).AsInt();
                    var innerArray = array[outerIndex].ArrVal;
                    var innerIndex = Evaluate(multiArrayAccess.Indices[1]).AsInt();
                    reference = newValue => innerArray[innerIndex] = newValue;
                    return innerArray[innerIndex];
                }
            }

            return Evaluate(argument);
        }

        private static void CopyBackProcedureArguments(PasProcDecl proc,
            List<Action<PasValue>> references, List<PasValue> args)
        {
            for (int i = 0; i < proc.Parameters.Count && i < args.Count; i++)
            {
                if (!proc.Parameters[i].IsByRef)
                    continue;
                WriteBackArgument(proc.Name, i, references, args);
            }
        }

        private static void CopyBackBridgeArguments(string name,
            List<Action<PasValue>> references, List<PasValue> args,
            bool isYanshenSignInTunnel = false)
        {
            switch (name.ToLowerInvariant())
            {
                case "getvalidstr":
                    WriteBackArgument(name, 1, references, args);
                    break;
                case "checkbagitemex":
                case "psdecodedate":
                    WriteBackArgument(name, 1, references, args);
                    WriteBackArgument(name, 2, references, args);
                    WriteBackArgument(name, 3, references, args);
                    break;
                case "psdecodetime":
                    WriteBackArgument(name, 1, references, args);
                    WriteBackArgument(name, 2, references, args);
                    WriteBackArgument(name, 3, references, args);
                    WriteBackArgument(name, 4, references, args);
                    break;
                case "getmapcanwalkxy":
                    WriteBackArgument(name, 1, references, args);
                    WriteBackArgument(name, 2, references, args);
                    break;
                case "setarraylength":
                    WriteBackArgument(name, 0, references, args);
                    break;
                case "getsigninactprizer":
                    if (isYanshenSignInTunnel) break;
                    WriteBackArgument(name, 0, references, args);
                    WriteBackArgument(name, 1, references, args);
                    break;
            }
        }

        private static void WriteBackArgument(string callName, int index,
            List<Action<PasValue>> references, List<PasValue> args)
        {
            if (index >= args.Count)
                return;
            if (index >= references.Count || references[index] == null)
                throw new PasRuntimeException($"'{callName}' argument {index + 1} must be a variable");
            references[index](args[index]);
        }

        private static int PascalRound(double value) =>
            checked((int)Math.Round(value, MidpointRounding.ToEven));

        // ========== Built-in functions ==========

        private PasValue ExecuteBuiltinFunction(string name, List<PasValue> args)
        {
            var lower = name.ToLowerInvariant();
            switch (lower)
            {
                case "integer":
                    return PasValue.FromInt(args.Count > 0 ? args[0].AsInt() : 0);
                case "string":
                    return PasValue.FromString(args.Count > 0 ? args[0].AsString() : string.Empty);
                case "boolean":
                    return PasValue.FromBool(args.Count > 0 && args[0].AsBool());
                case "double":
                    return PasValue.FromDouble(args.Count > 0 ? args[0].AsDouble() : 0);
                case "getarraylength":
                    return PasValue.FromInt(args.Count > 0 && args[0].Type == PasValueType.Array
                        ? args[0].ArrVal.Elements.Length
                        : 0);
                case "exceptiontostring":
                    if (args.Count >= 2)
                        return PasValue.FromString(args[0].AsString() + ": " + args[1].AsString());
                    return args.Count > 0 ? PasValue.FromString(args[0].AsString()) : PasValue.FromString(string.Empty);

                // String conversions
                case "addspace":
                case "addspace1":
                    if (args.Count >= 2)
                    {
                        var s = args[0].AsString() ?? "";
                        var needLen = args[1].AsInt();
                        while (s.Length < needLen) s += " ";
                        return PasValue.FromString(s);
                    }
                    return PasValue.FromString("");

                case "inttostr":
                    return PasValue.FromString(args.Count > 0 ? args[0].AsInt().ToString() : "");

                case "strtoint":
                    return PasValue.FromInt(args.Count > 0 ? int.TryParse(args[0].AsString(), out var si) ? si : 0 : 0);

                case "strtointdef":
                    if (args.Count >= 2)
                    {
                        if (int.TryParse(args[0].AsString(), out var sdVal))
                            return PasValue.FromInt(sdVal);
                        else
                            return PasValue.FromInt(args[1].AsInt());
                    }
                    return PasValue.FromInt(0);

                case "obtainparambyindex":
                    if (args.Count >= 3)
                    {
                        var s = args[0].AsString() ?? "";
                        var delim = args[1].AsString();
                        if (!string.IsNullOrEmpty(delim))
                        {
                            var parts = s.Split(delim[0]);
                            int idx = args[2].AsInt();
                            return idx >= 0 && idx < parts.Length
                                ? PasValue.FromString(parts[idx])
                                : PasValue.FromString("");
                        }
                        return PasValue.FromString(s);
                    }
                    return PasValue.FromString("");

                case "floattostr":
                case "floattostring":
                    return PasValue.FromString(args.Count > 0 ? args[0].AsDouble().ToString() : "");

                case "strtofloat":
                    return PasValue.FromDouble(args.Count > 0 ? double.TryParse(args[0].AsString(), out var sd) ? sd : 0.0 : 0.0);

                case "booltostr":
                    return args.Count > 0
                        ? PasValue.FromString(args[0].AsBool() ? "TRUE" : "FALSE")
                        : PasValue.FromString("FALSE");

                case "stringreplace":
                    if (args.Count >= 4)
                        return PasValue.FromString(args[0].AsString().Replace(args[1].AsString(), args[2].AsString()));
                    return args.Count > 0 ? args[0] : PasValue.FromString("");

                case "getvalidstr":
                    if (args.Count >= 3)
                    {
                        var source = args[0].AsString();
                        var divider = args[2].AsString();
                        var index = string.IsNullOrEmpty(divider)
                            ? -1
                            : source.IndexOf(divider[0]);
                        if (index >= 0)
                        {
                            args[1] = PasValue.FromString(source.Substring(0, index));
                            return PasValue.FromString(source.Substring(index + 1));
                        }
                        args[1] = PasValue.FromString(source);
                    }
                    return PasValue.FromString("");

                // Math
                case "random":
                    return args.Count > 0
                        ? PasValue.FromInt(NativePasRandomContract.Random(args[0].AsInt()))
                        : PasValue.FromDouble(NativePasRandomContract.RandomFloat());

                case "randomrange":
                    if (args.Count >= 2)
                        return PasValue.FromInt(NativePasRandomContract.RandomRange(args[0].AsInt(), args[1].AsInt()));
                    return args.Count > 0 ? PasValue.FromInt(NativePasRandomContract.Random(args[0].AsInt())) : PasValue.FromInt(0);

                case "abs":
                    return args.Count > 0 ? PasValue.FromInt(Math.Abs(args[0].AsInt())) : PasValue.FromInt(0);

                case "round":
                    return args.Count > 0 ? PasValue.FromInt((int)Math.Round(args[0].AsDouble())) : PasValue.FromInt(0);

                case "trunc":
                    return args.Count > 0 ? PasValue.FromInt((int)args[0].AsDouble()) : PasValue.FromInt(0);

                case "sqr":
                    if (args.Count > 0) { var v = args[0].AsInt(); return PasValue.FromInt(v * v); }
                    return PasValue.FromInt(0);

                case "sqrt":
                    return args.Count > 0 ? PasValue.FromDouble(Math.Sqrt(args[0].AsDouble())) : PasValue.FromDouble(0);

                // Date/Time
                case "getnow":
                    return PasValue.FromDouble(DateTime.Now.ToOADate());

                case "gethour":
                    return PasValue.FromInt(DateTime.Now.Hour);

                case "getmin":
                    return PasValue.FromInt(DateTime.Now.Minute);

                case "getsecond":
                    return PasValue.FromInt(DateTime.Now.Second);

                case "getdayofweek":
                    return PasValue.FromInt((int)DateTime.Now.DayOfWeek + 1); // Pascal: Sun=1

                case "getyear":
                    return PasValue.FromInt(DateTime.Now.Year);

                case "getmonth":
                    return PasValue.FromInt(DateTime.Now.Month);

                case "getday":
                    return PasValue.FromInt(DateTime.Now.Day);

                case "getdatenum":
                    return PasValue.FromInt(PascalRound(args.Count > 0
                        ? args[0].AsDouble()
                        : DateTime.Now.ToOADate()));

                case "minusdatatime":
                case "minusdatetime":
                    return args.Count >= 2
                        ? PasValue.FromInt(PascalRound((args[0].AsDouble() - args[1].AsDouble()) * 86400.0))
                        : PasValue.FromInt(0);

                case "secondsbetween":
                    return args.Count >= 2
                        ? PasValue.FromInt(PascalRound(Math.Abs(args[0].AsDouble() - args[1].AsDouble()) * 86400.0))
                        : PasValue.FromInt(0);

                case "adddatetimewithsec":
                    return args.Count >= 2
                        ? PasValue.FromDouble(args[0].AsDouble() + args[1].AsInt() / 86400.0)
                        : PasValue.FromDouble(0);

                case "convertdatetimetodb":
                    return args.Count > 0
                        ? PasValue.FromInt(PascalRound((args[0].AsDouble() - 30000.0) * 100000.0))
                        : PasValue.FromInt(0);

                case "convertdbtodatetime":
                    return args.Count > 0
                        ? PasValue.FromDouble(args[0].AsInt() / 100000.0 + 30000.0)
                        : PasValue.FromDouble(0);

                case "formatdatetime":
                    if (args.Count >= 2)
                        return PasValue.FromString(DateTime.Now.ToString(args[1].AsString()));
                    return PasValue.FromString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                // String operations
                case "comparetext":
                    if (args.Count >= 2)
                        return PasValue.FromInt(string.Compare(
                            args[0].AsString(), args[1].AsString(), StringComparison.OrdinalIgnoreCase));
                    return PasValue.FromInt(0);

                case "sametext":
                case "comparestr":
                    if (args.Count >= 2)
                        return PasValue.FromInt(string.Compare(args[0].AsString(), args[1].AsString(), StringComparison.OrdinalIgnoreCase) == 0 ? 1 : 0);
                    return PasValue.FromInt(0);

                case "length":
                    return PasValue.FromInt(args.Count > 0 ? args[0].AsString().Length : 0);

                case "trim":
                    return PasValue.FromString(args.Count > 0 ? args[0].AsString().Trim() : "");

                case "uppercase":
                    return PasValue.FromString(args.Count > 0 ? args[0].AsString().ToUpperInvariant() : "");

                case "lowercase":
                    return PasValue.FromString(args.Count > 0 ? args[0].AsString().ToLowerInvariant() : "");

                case "copy":
                    if (args.Count >= 3)
                    {
                        var s = args[0].AsString();
                        var start = args[1].AsInt() - 1; // Pascal is 1-based
                        var len = args[2].AsInt();
                        if (start < 0) start = 0;
                        if (len < 0) return PasValue.FromString("");
                        if (start >= s.Length) return PasValue.FromString("");
                        if (start + len > s.Length) len = s.Length - start;
                        return PasValue.FromString(s.Substring(start, len));
                    }
                    return PasValue.FromString("");

                case "pos":
                    if (args.Count >= 2)
                    {
                        var idx = args[1].AsString().IndexOf(args[0].AsString(), StringComparison.OrdinalIgnoreCase);
                        return PasValue.FromInt(idx + 1); // Pascal: 1-based
                    }
                    return PasValue.FromInt(0);

                // ===== Variable system (G/V/S) =====
                // GetG = sub_699198: 未命中值是 -2（0x6991BF mov esi,-2，收尾
                // 0x6992B2 mov eax,esi），且 index 有 1..50 的门。和 GetV/GetS 的 -1
                // 不是同一个值，脚本里 `if GetG(a,b) = 0` 靠这个区分。
                case "getg":
                    return args.Count >= 2
                        ? _api.GetGlobalVar(args[0].AsInt(), args[1].AsInt())
                        : PasValue.FromInt(-2);

                // GetV/GetS = sub_6DF1E4 / sub_6DF1B4: the miss/reject result is -1
                // (seeded at 0x6DF1F1 / 0x6DF1BB and by the keyed core at 0x6E427A),
                // never 0 — a script guard `if GetV(a,b) = 0` must not fire on "unset".
                case "getv":
                    return args.Count >= 2
                        ? _api.GetPlayerVar('V', args[0].AsInt(), args[1].AsInt())
                        : PasValue.FromInt(-1);

                case "gets":
                    return args.Count >= 2
                        ? _api.GetPlayerVar('S', args[0].AsInt(), args[1].AsInt())
                        : PasValue.FromInt(-1);

                // ===== Variable setters (now return bool) =====
                case "setg":
                    return args.Count >= 3
                        ? PasValue.FromBool(_api.SetGlobalVar(args[0].AsInt(), args[1].AsInt(), args[2]))
                        : PasValue.FromBool(false);

                case "setv":
                    return args.Count >= 3
                        ? PasValue.FromBool(_api.SetPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]))
                        : PasValue.FromBool(false);

                case "sets":
                    return args.Count >= 3
                        ? PasValue.FromBool(_api.SetPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]))
                        : PasValue.FromBool(false);

                case "groupsetv":
                    return args.Count >= 3
                        ? PasValue.FromBool(_api.SetGroupPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]))
                        : PasValue.FromBool(false);

                case "groupsets":
                    return args.Count >= 3
                        ? PasValue.FromBool(_api.SetGroupPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]))
                        : PasValue.FromBool(false);

                // ===== INI file =====
                case "readinisectionstr":
                    return args.Count >= 3
                        ? PasValue.FromString(_api.ReadIniSectionStr(args[0].AsString(), args[1].AsString(), args[2].AsString()))
                        : PasValue.FromString("");

                // ===== Check functions (delegated to API bridge) =====
                case "checkbagitem":
                    if (args.Count >= 2 && _api.CallPlayerFunc("CheckBagItem", args, out var cbi))
                        return cbi;
                    return PasValue.FromBool(false);

                case "checkbagitemex":
                    if (_api.CallPlayerFunc("CheckBagItemEx", args, out var cbie))
                        return cbie;
                    return PasValue.FromInt(0);

                case "checkskill":
                    if (_api.CallPlayerFunc("CheckSkill", args, out var cs))
                        return cs;
                    return PasValue.FromInt(0);

                case "checkheroskill":
                    if (_api.CallPlayerFunc("CheckHeroSkill", args, out var chs))
                        return chs;
                    return PasValue.FromInt(0);

                case "checklevel":
                    if (args.Count >= 1 && _api.CallPlayerFunc("CheckLevel", args, out var cl))
                        return cl;
                    return PasValue.FromBool(false);

                case "checkgold":
                    if (args.Count >= 1 && _api.CallPlayerFunc("CheckGold", args, out var cg))
                        return cg;
                    return PasValue.FromBool(false);

                case "checkjob":
                    if (args.Count >= 1 && _api.CallPlayerFunc("CheckJob", args, out var cj))
                        return cj;
                    return PasValue.FromBool(false);

                case "checkgamegold":
                    if (args.Count >= 1 && _api.CallPlayerFunc("CheckGameGold", args, out var cgg))
                        return cgg;
                    return PasValue.FromBool(false);

                case "checkdiamond":
                    if (args.Count >= 1 && _api.CallPlayerFunc("CheckDiamond", args, out var cd))
                        return cd;
                    return PasValue.FromBool(false);

                case "checkcurrmapmon":
                    if (_api.CallPlayerFunc("CheckCurrMapMon", args, out var ccm))
                        return ccm;
                    return PasValue.FromInt(0);

                case "checkcurrmaphum":
                    if (_api.CallPlayerFunc("CheckCurrMapHum", args, out var cch))
                        return cch;
                    return PasValue.FromInt(0);

                case "checkmapmonbyname":
                    if (_api.CallNpcFunc("CheckMapMonByName", args, out var cmmn))
                        return cmmn;
                    return PasValue.FromInt(0);

                case "checkothermaphum":
                    if (args.Count >= 1 && _api.CallStandaloneFunction("CheckOtherMapHum", args, out var comh))
                        return comh;
                    return PasValue.FromInt(0);

                case "ischeckbodyitem":
                    if (_api.CallPlayerFunc("IsCheckBodyItem", args, out var icbi))
                        return icbi;
                    return PasValue.FromBool(false);

                case "ismale":
                    if (_api.CallPlayerFunc("IsMale", args, out var im))
                        return im;
                    return PasValue.FromBool(false);

                case "isfemale":
                    if (_api.CallPlayerFunc("IsFemale", args, out var iff))
                        return iff;
                    return PasValue.FromBool(false);

                case "isdead":
                    if (_api.CallPlayerFunc("IsDead", args, out var id))
                        return id;
                    return PasValue.FromBool(false);

                case "isguildlord":
                    if (_api.CallPlayerFunc("IsGuildLord", args, out var igl))
                        return igl;
                    return PasValue.FromBool(false);

                case "isfirstguildlord":
                    if (_api.CallPlayerFunc("IsFirstGuildLord", args, out var ifgl))
                        return ifgl;
                    return PasValue.FromBool(false);

                case "isteammember":
                    if (_api.CallPlayerFunc("IsTeamMember", args, out var itm))
                        return itm;
                    return PasValue.FromBool(false);

                case "isgroupowner":
                    if (_api.CallPlayerFunc("IsGroupOwner", args, out var igo))
                        return igo;
                    return PasValue.FromBool(false);

                case "isstudent":
                    if (_api.CallPlayerFunc("IsStudent", args, out var isStu))
                        return isStu;
                    return PasValue.FromBool(false);

                case "iscastle":
                    if (_api.CallPlayerFunc("IsCastle", args, out var ic))
                        return ic;
                    return PasValue.FromBool(false);

                case "havevalidhero":
                    if (_api.CallPlayerFunc("HaveValidHero", args, out var hvh))
                        return hvh;
                    return PasValue.FromBool(false);

                case "checkauthen":
                    if (_api.CallPlayerFunc("CheckAuthen", args, out var ca))
                        return ca;
                    return PasValue.FromBool(false);

                // ===== DB result set navigation =====
                case "psfirst":
                    _api.CallStandaloneFunction("PsFirst", args, out _);
                    return PasValue.Nil;

                case "psnext":
                    _api.CallStandaloneFunction("PsNext", args, out _);
                    return PasValue.Nil;

                case "psbof":
                    if (_api.CallStandaloneFunction("PsBof", args, out var bof))
                        return bof;
                    return PasValue.FromBool(false);

                case "pseof":
                    if (_api.CallStandaloneFunction("PsEof", args, out var eof))
                        return eof;
                    return PasValue.FromBool(true);

                case "psfieldname":
                    if (_api.CallStandaloneFunction("PsFieldName", args, out var fn))
                        return fn;
                    return PasValue.FromString("");

                case "psfieldbyname":
                    if (_api.CallStandaloneFunction("PsFieldByName", args, out var fbn))
                        return fbn;
                    return PasValue.FromString("");

                // Global property access (handled like functions)
                case "psrecordcount":
                    if (_api.CallStandaloneFunction("PsRecordCount", args, out var prc))
                        return prc;
                    return PasValue.FromInt(0);

                case "psfieldcount":
                    if (_api.CallStandaloneFunction("PsFieldCount", args, out var pfc))
                        return pfc;
                    return PasValue.FromInt(0);

                default:
                    // Try API standalone function
                    if (_api.CallStandaloneFunction(name, args, out var apiResult))
                        return apiResult;
                    // Terminal miss in EXPRESSION form: the script gets a silent 0. For the
                    // guards and getters in this class that means fail-open or fail-wrong
                    // (e.g. a cost/quantity query answering "0" = free). Log once per name;
                    // the returned value is unchanged.
                    PasApiBridge.TraceUnknownPasName("BuiltinFunction", name);
                    return PasValue.FromInt(0);
            }
        }

        private void ExecuteBuiltinProcedure(string name, List<PasValue> args)
        {
            switch (name.ToLowerInvariant())
            {
                case "setarraylength":
                    if (args.Count >= 2)
                    {
                        var array = args[0].Type == PasValueType.Array
                            ? args[0].ArrVal
                            : new PasArray(0, -1);
                        array.Resize(args[1].AsInt());
                        args[0] = PasValue.FromArray(array);
                    }
                    break;

                case "psdecodedate":
                    if (args.Count >= 4)
                    {
                        var date = DateTime.FromOADate(args[0].AsDouble());
                        args[1] = PasValue.FromInt(date.Year);
                        args[2] = PasValue.FromInt(date.Month);
                        args[3] = PasValue.FromInt(date.Day);
                    }
                    break;

                case "psdecodetime":
                    if (args.Count >= 5)
                    {
                        var time = DateTime.FromOADate(args[0].AsDouble());
                        args[1] = PasValue.FromInt(time.Hour);
                        args[2] = PasValue.FromInt(time.Minute);
                        args[3] = PasValue.FromInt(time.Second);
                        args[4] = PasValue.FromInt(time.Millisecond);
                    }
                    break;

                // ===== Messaging =====
                case "serversay":
                    if (args.Count >= 1)
                    {
                        var msg = args[0].AsString();
                        var color = args.Count >= 2 ? args[1].AsInt() : 0;
                        _api.ServerSay(msg, color);
                    }
                    break;

                case "npcsay":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    break;

                case "npcnotice":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    break;

                case "npcsidenotice":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    break;

                case "npcmapnotice":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    break;

                // ===== INI =====
                case "writeinisectionstr":
                    if (args.Count >= 4)
                        _api.WriteIniSectionStr(args[0].AsString(), args[1].AsString(), args[2].AsString(), args[3].AsString());
                    break;

                // ===== Script DB operations =====
                case "executescript":
                    if (args.Count >= 1)
                        _api.CallStandaloneFunction("ExecuteScript", args, out _);
                    break;

                case "executequery":
                    if (args.Count >= 1)
                        _api.CallStandaloneFunction("ExecuteQuery", args, out _);
                    break;

                // ===== Mail =====
                case "newfullmailex":
                    if (args.Count != 8 ||
                        !_api.CallStandaloneFunction("NewFullMailEx", args, out _))
                        throw new PasRuntimeException("NewFullMailEx failed");
                    break;

                // ===== Map events =====
                case "createmapevent":
                    if (args.Count >= 7)
                    {
                        // CreateMapEvent(eventType, lineNo, columnNo, lastSecond, startDamage, intervalSecond, incDamage)
                        var evtType = args[0].AsInt();
                        var colNo = args[1].AsInt();
                        var lineNo = args[2].AsInt();
                        var lastSec = args[3].AsInt();
                        var startDmg = args[4].AsInt();
                        // intervalSecond = args[5]
                        var incDmg = args[6].AsInt();

                        var npc = _api.CurrentNpc;
                        if (npc?.m_PEnvir != null)
                        {
                            var envir = npc.m_PEnvir;
                            var evt = new Event(envir, colNo, lineNo, evtType, lastSec * 1000, true);
                            evt.m_nDamage = startDmg;
                            evt.m_nEventParam = incDmg;
                            M2Share.EventManager.AddEvent(evt);
                        }
                    }
                    break;

                case "removemapevent":
                    if (args.Count >= 3)
                    {
                        // RemoveMapEvent(eventType, lineNo, columnNo)
                        var evtType = args[0].AsInt();
                        var colNo = args[1].AsInt();
                        var lineNo = args[2].AsInt();

                        var npc = _api.CurrentNpc;
                        if (npc?.m_PEnvir != null)
                        {
                            var envir = npc.m_PEnvir;
                            var evt = M2Share.EventManager.GetEvent(envir, colNo, lineNo, evtType);
                            if (evt != null)
                            {
                                evt.Close();
                            }
                        }
                    }
                    break;

                // ===== Exit =====
                case "exit":
                    _exiting = true;
                    if (args.Count >= 1) _functionResult = args[0];
                    break;

                default:
                    // Try API for unknown procedures
                    _api.CallStandaloneFunction(name, args, out _);
                    break;
            }
        }

        public void Reset()
        {
            _globals.Clear();
            foreach (var c in _program.Consts)
                _globals[c.Name] = c.Value;
            foreach (var v in _program.GlobalVars)
            {
                _globals[v.Name] = CreateDefaultValue(v);
            }
            _locals = _globals;
            _scopeStack.Clear();
            _callStack.Clear();
            _functionResult = PasValue.Nil;
            _exiting = false;
            _breaking = false;
            _continuing = false;
            _lastExceptionType = string.Empty;
            _lastExceptionParam = string.Empty;
        }
    }
}
