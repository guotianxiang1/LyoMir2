using System.Collections.ObjectModel;

namespace GameSvr.PasEngine
{
    public abstract class PasAstNode
    {
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
        public int SourceColumn { get; set; }
    }

    // Program: program Mir2; uses...; const...; var...; procedures...; begin...end.
    public class PasProgram : PasAstNode
    {
        public string Name { get; set; }
        public List<PasConstDecl> Consts { get; set; } = new();
        public List<PasVarDecl> GlobalVars { get; set; } = new();
        public List<PasProcDecl> Procedures { get; set; } = new();
        public PasBlock MainBlock { get; set; }
    }

    // Const declaration
    public class PasConstDecl : PasAstNode
    {
        public string Name { get; set; }
        public PasValue Value { get; set; }
    }

    public enum PasParameterMode
    {
        Value,
        Const,
        Var,
        Out
    }

    // Var declaration: Name: Type; or Name: array[lo..hi] of Type;
    public class PasVarDecl : PasAstNode
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public PasParameterMode ParameterMode { get; set; }
        public bool IsByRef => ParameterMode == PasParameterMode.Var || ParameterMode == PasParameterMode.Out;
        public bool IsArray { get; set; }
        public bool IsDynamicArray { get; set; }
        public int ArrayLow { get; set; }
        public int ArrayHigh { get; set; }
        public string ArrayElementType { get; set; }
    }

    // Procedure/Function declaration
    public class PasProcDecl : PasAstNode
    {
        public string Name { get; set; }
        public bool IsFunction { get; set; }
        public string ReturnType { get; set; }
        public bool ReturnIsArray { get; set; }
        public string ReturnArrayElementType { get; set; }
        public List<PasVarDecl> Parameters { get; set; } = new();
        public List<PasVarDecl> LocalVars { get; set; } = new();
        public PasBlock Body { get; set; }
    }

    // begin ... end block
    public class PasBlock : PasAstNode
    {
        public List<PasAstNode> Statements { get; set; } = new();
    }

    // Statements
    public class PasAssignStmt : PasAstNode
    {
        public PasAstNode Target { get; set; }  // Identifier or ArrayAccess
        public PasAstNode Value { get; set; }
    }

    public class PasIfStmt : PasAstNode
    {
        public PasAstNode Condition { get; set; }
        public PasAstNode ThenBlock { get; set; }
        public PasAstNode ElseBlock { get; set; }
    }

    public class PasCaseStmt : PasAstNode
    {
        public PasAstNode Expression { get; set; }
        public List<PasCaseBranch> Branches { get; set; } = new();
    }

    public class PasCaseBranch : PasAstNode
    {
        public List<PasAstNode> Values { get; set; } = new();  // list of literals
        public PasAstNode Body { get; set; }
    }

    public class PasWhileStmt : PasAstNode
    {
        public PasAstNode Condition { get; set; }
        public PasAstNode Body { get; set; }
    }

    public class PasForStmt : PasAstNode
    {
        public string VarName { get; set; }
        public PasAstNode From { get; set; }
        public PasAstNode To { get; set; }
        public bool DownTo { get; set; }
        public PasAstNode Body { get; set; }
    }

    public class PasRepeatStmt : PasAstNode
    {
        public PasBlock Body { get; set; }
        public PasAstNode Condition { get; set; }
    }

    public class PasCallStmt : PasAstNode
    {
        public string Name { get; set; }
        public List<PasAstNode> Arguments { get; set; } = new();
        public bool IsMethod { get; set; }
        public string ObjectName { get; set; }  // e.g. "This_Player"
        public bool IsIniWrite { get; set; }    // special: WriteIniSectionStr
    }

    // Method call like This_Player.FlyTo(...) or This_Npc.NpcDialog(...)
    public class PasMethodCallExpr : PasAstNode
    {
        public PasAstNode Target { get; set; }
        public string ObjectName { get; set; }
        public string MethodName { get; set; }
        public List<PasAstNode> Arguments { get; set; } = new();
    }

    // Member access like This_Player.Level
    public class PasMemberAccessExpr : PasAstNode
    {
        public PasAstNode Target { get; set; }
        public string ObjectName { get; set; }
        public string MemberName { get; set; }
    }

    // Array access like arr[i]
    public class PasArrayAccessExpr : PasAstNode
    {
        public string ArrayName { get; set; }
        public PasAstNode Index { get; set; }
    }

    // Multi-dimensional array access like name[i][j]
    public class PasMultiArrayAccessExpr : PasAstNode
    {
        public string ArrayName { get; set; }
        public List<PasAstNode> Indices { get; set; } = new();
    }

    // Expressions
    public class PasBinaryOpExpr : PasAstNode
    {
        public PasAstNode Left { get; set; }
        public string Op { get; set; }  // +, -, *, /, =, <>, <, >, <=, >=, and, or, div, mod
        public PasAstNode Right { get; set; }
    }

    public class PasUnaryOpExpr : PasAstNode
    {
        public string Op { get; set; }  // -, not
        public PasAstNode Operand { get; set; }
    }

    public class PasLiteralExpr : PasAstNode
    {
        public PasValue Value { get; set; }
    }

    public class PasIdentifierExpr : PasAstNode
    {
        public string Name { get; set; }
    }

    // Exit; Exit(value);
    public class PasExitStmt : PasAstNode
    {
        public PasAstNode Value { get; set; }
    }

    // Include directive
    public class PasIncludeDir : PasAstNode
    {
        public string FileName { get; set; }
    }

    // ===== New language constructs =====

    // try ... except ... end; or try ... finally ... end;
    public class PasTryStmt : PasAstNode
    {
        public PasBlock Body { get; set; }
        public List<PasExceptHandler> ExceptHandlers { get; set; } = new();
        public PasBlock FinallyBlock { get; set; }
    }

    public class PasExceptHandler : PasAstNode
    {
        public string ExceptionType { get; set; }  // e.g., "Exception" or empty for catch-all
        public string VariableName { get; set; }   // e.g., "E" in "on E: Exception do"
        public PasAstNode Body { get; set; }
    }

    // raise Exception; or raise;
    public class PasRaiseStmt : PasAstNode
    {
        public PasAstNode Exception { get; set; }  // optional
    }

    // break;  (exit loop)
    public class PasBreakStmt : PasAstNode { }

    // continue;  (skip to next loop iteration)
    public class PasContinueStmt : PasAstNode { }

    // with Obj do begin ... end;
    public class PasWithStmt : PasAstNode
    {
        public List<PasAstNode> Objects { get; set; } = new();
        public PasAstNode Body { get; set; }
    }

    // Inc(x); or Inc(x, n);
    public class PasIncStmt : PasAstNode
    {
        public string VariableName { get; set; }
        public PasAstNode Amount { get; set; }  // optional, default 1
    }

    // Dec(x); or Dec(x, n);
    public class PasDecStmt : PasAstNode
    {
        public string VariableName { get; set; }
        public PasAstNode Amount { get; set; }
    }

    // Assert(condition); or Assert(condition, message);
    public class PasAssertStmt : PasAstNode
    {
        public PasAstNode Condition { get; set; }
        public PasAstNode Message { get; set; }
    }

    // Mark for labels like "@main:", "@exit:", etc.
    public class PasLabelDecl : PasAstNode
    {
        public string Name { get; set; }
    }
}
