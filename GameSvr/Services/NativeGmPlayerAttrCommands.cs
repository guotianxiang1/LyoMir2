// -----------------------------------------------------------------------------
// NativeGmPlayerAttrCommands.cs
//
// DORMANT / FAIL-CLOSED reference model of the M2Server "战神" GM ("@") command
// family 10 PLAYER-ATTR / TREND — the subset of the dispatcher sub_622820
// @0x00622820 that reads/sets a player's attributes, relationships (marry/divorce/
// master-apprentice/royal-master), reputation, level/exp, multi-exp timer, damage-
// share, status effects, and the (entirely no-op) 个性化数据 "Trend" sub-family.
//
// Written from the reversed census staging/gm_player_attr_commands_20260801.md
// (agent gm-fields, task #38) + the case bodies in disp_decomp.txt/big622820.txt.
// The census author shipped the doc but not the code; this file is that code.
//
// Scope = the 33 family-10 records. It EXCLUDES ChgBodyLuck(92) and ChgHideState(102),
// which sit in this family behaviourally but are already modeled (byte-exact) in
// NativeGmPlayerAdminCommands.cs. Cross-checked 2026-08-01: none of the 33 names
// appear in any other NativeGm*.cs — no type/entry collision.
//
// NOT wired into the live pipeline; evidence-anchored spec for the audit
// (AuditTools/NativeGmPlayerAttrCommandsCheck) + porters.
//
// Source of truth: M2Server_unpacked_fixed.exe (战神), base 0x00400000,
// SHA256 5540f43b…c049670b14e; IDA db m2full.i64; dumps disp_decomp.txt /
// big622820.txt / padmin_out.txt / world_scan_out.txt. Handler = *(0x00622B1C+idx*4)
// (each == the case-branch entry cross-checked in big622820.txt, e.g. LookFor@0x623BBA
// "case 52", Die@0x627FD5 "case 358", ClearAllState@0x6297EB "case 575").
//
// Dual silent no-op sinks (reusing NativeNoOpSink from NativeGmMoveLeitaiCommands.cs):
//   def_622B15 @0x0062B648 (default) and loc_62B64C @0x0062B64C (empty-body). The
//   whole Trend cluster is inert: GetTrendV/SetTrendV/ClearTrendData @0x62B64C,
//   ClearAllTrendData @0x62B648; plus ZongpaiTest/AddImpress/ChgDragonState/Show
//   @0x62B648 and SetDominateLv @0x62B64C. 9 no-ops / 24 real handlers.
//
// SysMsg idents: 0xFFDB (-37) reply · 0x38FF (14591) usage/error. sub_652784 =
// FindPlayerByName. Delegating shims tail-call cores absent from the dumps
// (CoreBodyDeferred=true == needs-idat); QuizLevel's cap write is inline & modeled.
//
// UN-WIRED MUTES: OutSay(62)/ShifangSay(63)/LookOutSay(64) are real handlers whose
// backing store (BlockUsers.Dat mute codec) is already modeled by
// NativeGmDenyListCommands.cs — only the @-command wiring is missing. Flagged so a
// porter wires these three to that existing model rather than re-implementing.
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace GameSvr
{
    public enum NativePlayerAttrOutcome
    {
        UnknownCommand, PermissionRejected, SilentNoOp,
        Executed, ExecutedWithGmMessage, RejectedSilently, RejectedWithGmMessage
    }

    public sealed class NativePlayerAttrCommand
    {
        public const uint JumpTableBase = 0x00622B1C;
        public const uint DefaultHandler = 0x0062B648;
        public const uint EmptyBodyHandler = 0x0062B64C;

        public NativePlayerAttrCommand(string name, int dispatchIndex, int requiredPerm,
            uint handlerAddress, string nativeCore, bool coreBodyDeferred, string helpGbk, string effectSummary)
        {
            Name = name; DispatchIndex = dispatchIndex; RequiredPerm = requiredPerm;
            HandlerAddress = handlerAddress; NativeCore = nativeCore ?? ""; CoreBodyDeferred = coreBodyDeferred;
            HelpGbk = helpGbk ?? ""; EffectSummary = effectSummary ?? "";
        }

        public string Name { get; }
        public int DispatchIndex { get; }
        public int RequiredPerm { get; }
        public uint HandlerAddress { get; }
        public uint JumpSlotAddress => JumpTableBase + (uint)DispatchIndex * 4;
        public bool Implemented => HandlerAddress != DefaultHandler && HandlerAddress != EmptyBodyHandler;
        public NativeNoOpSink Sink =>
            HandlerAddress == DefaultHandler ? NativeNoOpSink.DefaultCase
            : HandlerAddress == EmptyBodyHandler ? NativeNoOpSink.EmptyBody
            : NativeNoOpSink.None;
        public string NativeCore { get; }
        public bool CoreBodyDeferred { get; }
        public string HelpGbk { get; }
        public string EffectSummary { get; }
    }

    public sealed class NativePlayerAttrEvaluation
    {
        public NativePlayerAttrEvaluation(NativePlayerAttrOutcome outcome, string branch,
            string nativeCore, int nativeSysMsgIdent, bool coreBodyDeferred, string detail)
        {
            Outcome = outcome; Branch = branch ?? ""; NativeCore = nativeCore ?? "";
            NativeSysMsgIdent = nativeSysMsgIdent; CoreBodyDeferred = coreBodyDeferred; Detail = detail ?? "";
        }
        public NativePlayerAttrOutcome Outcome { get; }
        public string Branch { get; }
        public string NativeCore { get; }
        public int NativeSysMsgIdent { get; }
        public bool CoreBodyDeferred { get; }
        public string Detail { get; }
    }

    public static class NativeGmPlayerAttrCommands
    {
        public const uint DispatcherEa = 0x00622820;
        public const uint IndexLookupEa = 0x00621F28;
        public const uint JumpTableEa = 0x00622B1C;
        public const int SwitchMaxIndex = 750;
        public const uint DefaultCaseEa = 0x0062B648;
        public const uint EmptyBodyCaseEa = 0x0062B64C;

        public const int SysMsgGmReply = 0xFFDB;  // -37
        public const int SysMsgUsage = 0x38FF;    // 14591
        public const int NoSysMsg = -1;

        public const uint FindPlayerEa = 0x00652784;      // sub_652784 FindPlayerByName
        public const uint QuizLevelGlobalEa = 0x007D611C;  // *(WORD*)off_7D611C = public-chat lvl cap
        public const int QuizLevelMax = 30;                // n capped at 30
        public const int DmgShareFieldOffset = 1400;       // target[+1400] = dmg-share bonus (ChgDmgShare)
        public const int DieVtblOffset = 0x84;             // vtbl+132 Die() (Die 358)
        public const int RecalcVtblOffset = 0x8C;          // vtbl+140 recalc (ChgDmgShare 359)

        private static readonly NativePlayerAttrCommand[] _all =
        {
            // ---- queries / reports ----
            new NativePlayerAttrCommand("LookFor", 52, 2, 0x00623BBAu, "sub_6BE5DC", true,
                "查看在线角色信息	@LookFor 角色名",
                "Pure delegation: sub_6BE5DC(name) — the online-char info report is emitted inside the core; shim silent."),
            new NativePlayerAttrCommand("UpLvZx", 57, 3, 0x0062415Du, "sub_652BEC", true,
                "当前GS大于等于XX级数的玩家总数	@UpLvZx [级数/无(默认8)]",
                "n=Str_ToInt(arg, default 8); sub_652BEC counts players >= n; SysMsg(0x38FF) reports the total."),
            new NativePlayerAttrCommand("LookOutSay", 64, 3, 0x006242B5u, "sub_621E74", true,
                "查看禁言名单列表	@LookOutSay",
                "sub_621E74() builds the mute roster; arg present -> roster line, else empty-roster line; both SysMsg(0xFFDB). "
                + "Backing store is NativeGmDenyListCommands (BlockUsers.Dat)."),
            new NativePlayerAttrCommand("RangeHumCount", 313, 4, 0x00627680u, "sub_6BED44", true,
                "查看周围玩家个数(包括GM自己)	@RangeHumCount 范围值",
                "range=Str_ToInt(arg); n=sub_6BED44() counts players in range incl. self; SysMsg(0xFFDB) reports."),
            new NativePlayerAttrCommand("showMulExp", 280, 4, 0x00626C83u, "(inline report)", true,
                "查询玩家多倍经验时间情况	@showMulExp 角色名",
                "FindPlayer(sub_652784): found -> report the target's multi-exp timer (from target+0x3000/0x3004); "
                + "not found -> error line. Both SysMsg(0xFFDB)."),

            // ---- relationship / social ----
            new NativePlayerAttrCommand("GowLihun", 122, 4, 0x0062527Au, "sub_6C51BC", true,
                "指定玩家离婚	@GowLihun 角色名",
                "Pure delegation: sub_6C51BC() force-divorces the named char. No shim SysMsg."),
            new NativePlayerAttrCommand("GowJiehun", 123, 4, 0x0062528Au, "sub_6C5568", true,
                "指定玩家结婚	@GowJiehun 角色名1 角色名2",
                "Pure delegation: sub_6C5568(name1, name2) force-marries two chars. No shim SysMsg."),
            new NativePlayerAttrCommand("GowStuTec", 124, 4, 0x0062529Du, "sub_6C57B4", true,
                "指定玩家的师徒关系	@GowStuTec 徒弟名 师傅名",
                "Pure delegation: sub_6C57B4(apprentice, master) forces the master link. No shim SysMsg."),
            new NativePlayerAttrCommand("LeaveTech", 125, 4, 0x006252B0u, "sub_6C5E08", false,
                "解除玩家与其师傅的师徒关系	@LeaveTech 角色名",
                "sub_6C5E08 body reversed (WIRED, LeaveTechCommand.cs): nil name -> silent return "
                + "(0x6C5E13); FindPlayer(sub_652784) miss OR target not a student ([+0xB95]==0, "
                + "0x6C5E29) -> ONE failure line \"[失败] 角色不在有效范围或角色无师承\" (0x6C5EA4) to the "
                + "GM only; else sub_6C5EC8(edx=0) == NativeLeaveMaster(0) then "
                + "\"离师操作已被系统接受\" (0x6C5E84) TWICE -- to the target (0x6C5E48) and to the GM "
                + "(0x6C5E5B). All three use cx=0xFFDB (the Green pair), failure included."),
            new NativePlayerAttrCommand("ClearRelation", 126, 4, 0x006252C0u, "sub_6C61D8", true,
                "清除玩家的师徒、配偶关系	@ClearRelation 角色名 [all/徒弟名]",
                "Pure delegation: sub_6C61D8(name, scope) clears master/spouse relations. No shim SysMsg."),
            new NativePlayerAttrCommand("ChgWangshi", 224, 5, 0x0062617Bu, "sub_6D20D0", true,
                "更改玩家的王师关系	@ChgWangshi 角色名 王师名",
                "Pure delegation: sub_6D20D0(name, wangshi) resets the royal-master link. No shim SysMsg."),

            // ---- attributes / level / exp ----
            new NativePlayerAttrCommand("ChgSelfHair", 93, 4, 0x00624FA2u, "sub_6D77DC", true,
                "改变自身发型	@ChgSelfHair 发型",
                "hair=Str_ToInt(arg); sub_6D77DC() sets the GM's own hair. No shim SysMsg."),
            new NativePlayerAttrCommand("ChgSwTo", 107, 4, 0x0062513Fu, "sub_6C2148", true,
                "调整玩家声望	@ChgSwTo 角色名 声望数值",
                "Pure delegation: sub_6C2148(name, value) sets the char's 声望 (reputation). No shim SysMsg."),
            new NativePlayerAttrCommand("Upgradedata", 210, 5, 0x00625E79u, "sub_6C6F40", true,
                "更改玩家级别	@Upgradedata 角色名 级别值",
                "level=Str_ToInt(arg1); sub_6C6F40(level, name) sets the char's level. No shim SysMsg."),
            new NativePlayerAttrCommand("UpExpdata", 211, 5, 0x00625E9Au, "sub_6C70CC", true,
                "更改玩家经验	@UpExpdata 角色名 经验值",
                "Pure delegation: sub_6C70CC(name, exp) sets the char's experience. No shim SysMsg."),
            new NativePlayerAttrCommand("QuizLevel", 179, 4, 0x00625901u, "(inline)", false,
                "设置本服务器公聊等级限制(默认7,最大30)	@QuizLevel [无/等级值]",
                "n=Str_ToInt(arg); n>30 -> n=30; `*(WORD*)off_7D611C = n` (public-chat level cap); SysMsg(0xFFDB) confirm."),
            new NativePlayerAttrCommand("ChgDmgShare", 359, 5, 0x00628008u, "(inline)+vtbl+0x8C", true,
                "设置玩家的伤害分担加成值	@ChgDmgShare 角色名 伤害分担加成值",
                "FindPlayer(sub_652784); found AND val>=0 -> target[+1400]=val + vtbl+0x8C recalc + SysMsg(0xFFDB); else silent."),
            new NativePlayerAttrCommand("ClearAllState", 575, 5, 0x006297EBu, "(deferred)", true,
                "清除玩家状态	@ClearAllState 角色名 [flags]",
                "FindPlayer(sub_652784); found -> parse flags and clear the target's status effects (apply at LABEL_811); "
                + "not found -> silent. Clear core deferred."),

            // ---- misc single-target ----
            new NativePlayerAttrCommand("Cattle", 155, 4, 0x006256D4u, "sub_7159A0 / sub_716740", true,
                "增加玩家牛气值 / 打开天赐	@Cattle [[Add 无/角色名 牛气值]/[OpenBox 0..4]]",
                "Add sub-op: FindPlayer or self; val>0 -> sub_7159A0() add 牛气 + SysMsg(0xFFDB); target missing -> SysMsg(0x38FF). "
                + "OpenBox sub-op: sub_716740() opens 天赐 box. Multi-branch; op tokens from help."),
            new NativePlayerAttrCommand("clearMulExp", 279, 4, 0x00626C0Du, "sub_6E3FB0", true,
                "清除玩家多倍经验时间	@clearMulExp 角色名",
                "FindPlayer(sub_652784): found -> sub_6E3FB0() clears the multi-exp timer + SysMsg(0xFFDB); "
                + "not found -> error line SysMsg(0xFFDB)."),
            new NativePlayerAttrCommand("Die", 358, 5, 0x00627FD5u, "vtbl+0x84", false,
                "GM自杀或设置其他玩家死亡	@Die [角色名/空(自身)]",
                "arg present -> FindPlayer(sub_652784) else self; target != null -> vtbl+0x84 Die(); else silent. No shim SysMsg."),
            new NativePlayerAttrCommand("OpenZhuZaiShenYou", 425, 4, 0x00628A94u, "sub_6BF658", true,
                "打开/关闭主宰神佑([1/非1])	@OpenZhuZaiShenYou [1/非1]",
                "flag=Str_ToInt(arg); sub_6BF658() toggles 主宰神佑. No shim SysMsg."),

            // ---- un-wired mute trio (backing store = NativeGmDenyListCommands) ----
            new NativePlayerAttrCommand("OutSay", 62, 2, 0x00624290u, "sub_6BF260", true,
                "禁言角色多少时间	@OutSay 角色名 [时间数/无]",
                "Pure delegation: sub_6BF260(name, seconds) mutes the char -> BlockUsers.Dat. No shim SysMsg. "
                + "WIRE to NativeGmDenyListCommands (mute codec already modeled)."),
            new NativePlayerAttrCommand("ShifangSay", 63, 2, 0x006242A3u, "sub_6BF340", true,
                "解除角色的禁言	@ShifangSay 角色名",
                "Pure delegation: sub_6BF340(1, name) un-mutes the char. No shim SysMsg. WIRE to NativeGmDenyListCommands."),

            // ============ registered SILENT NO-OPs (9): 5 @def_622B15, 4 @loc_62B64C ============
            new NativePlayerAttrCommand("ZongpaiTest", 285, 5, 0x0062B648u, "", false,
                "新宗派中模拟客户端的修改成员的命令	@ZongpaiTest 操作ID 宗派名 职位名 角色名",
                "Registered (idx 285, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativePlayerAttrCommand("AddImpress", 330, 5, 0x0062B648u, "", false,
                "增加/减少玩家的挑战点	@AddImpress 角色名 挑战点value",
                "Registered (idx 330, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativePlayerAttrCommand("SetDominateLv", 365, 5, 0x0062B64Cu, "", false,
                "设置玩家的主宰者星级	@SetDominateLv 角色名 星级",
                "Registered (idx 365, perm 5) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativePlayerAttrCommand("ChgDragonState", 366, 4, 0x0062B648u, "", false,
                "开启或关闭自身的半神话状态([1/非1])	@ChgDragonState [1/非1]",
                "Registered (idx 366, perm 4) but = def_622B15 (default sink): silent no-op."),
            new NativePlayerAttrCommand("GetTrendV", 390, 3, 0x0062B64Cu, "", false,
                "查询角色个性化数据	@GetTrendV [无/角色名] 字段",
                "Registered (idx 390, perm 3) but = loc_62B64C (empty-body sink): silent no-op. Entire Trend cluster is inert."),
            new NativePlayerAttrCommand("SetTrendV", 391, 4, 0x0062B64Cu, "", false,
                "设置玩家个性化数据	@SetTrendV 角色名 字段名 [值/无]",
                "Registered (idx 391, perm 4) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativePlayerAttrCommand("Show", 397, 3, 0x0062B648u, "", false,
                "显示在线角色某些信息	@Show 信息名 角色名",
                "Registered (idx 397, perm 3) but = def_622B15 (default sink): silent no-op."),
            new NativePlayerAttrCommand("ClearTrendData", 398, 4, 0x0062B64Cu, "", false,
                "清空玩家个性化数据	@ClearTrendData 角色名",
                "Registered (idx 398, perm 4) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativePlayerAttrCommand("ClearAllTrendData", 400, 4, 0x0062B648u, "", false,
                "清空服务器cache中所有玩家的个性化数据	@ClearAllTrendData",
                "Registered (idx 400, perm 4) but = def_622B15 (default sink): silent no-op."),
        };

        private static readonly Dictionary<string, NativePlayerAttrCommand> _byName = BuildIndex(_all);
        private static Dictionary<string, NativePlayerAttrCommand> BuildIndex(NativePlayerAttrCommand[] cmds)
        {
            var map = new Dictionary<string, NativePlayerAttrCommand>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in cmds) map[c.Name] = c;
            return map;
        }

        public static IReadOnlyList<NativePlayerAttrCommand> All => _all;
        public static NativePlayerAttrCommand Find(string name)
            => (name != null && _byName.TryGetValue(name, out var c)) ? c : null;

        public static NativeGmDefaultNoOp EvaluateUnimplemented(string name)
        {
            var rec = Find(name);
            if (rec == null) throw new System.ArgumentException($"'{name}' is not a player-attr command", nameof(name));
            if (rec.Implemented) throw new System.InvalidOperationException($"{rec.Name} is implemented; use Evaluate");
            return new NativeGmDefaultNoOp
            { Recognized = true, DispatchesToDefaultCase = true, MutatesState = false, SendsResponse = false };
        }

        public static NativePlayerAttrEvaluation Evaluate(string name, int callerPerm, IReadOnlyList<string> args)
        {
            var rec = Find(name);
            if (rec == null)
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.UnknownCommand, "", "", NoSysMsg, false,
                    "token is not in the player-attr family");
            if (callerPerm < rec.RequiredPerm)
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.PermissionRejected, "", "", NoSysMsg, false,
                    "callerPerm " + callerPerm + " < requiredPerm " + rec.RequiredPerm + "; sub_621F28 -> 0 -> def_622B15");
            if (!rec.Implemented)
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.SilentNoOp, "", "", NoSysMsg, false,
                    "handler == " + (rec.Sink == NativeNoOpSink.EmptyBody ? "loc_62B64C" : "def_622B15")
                    + " (silent no-op); no effect, no message");

            var a = args ?? System.Array.Empty<string>();
            switch (rec.Name)
            {
                // unconditional reports
                case "UpLvZx":        return Msg(rec, "report", SysMsgUsage, "count players >= level; report");
                case "LookOutSay":    return Msg(rec, "roster", SysMsgGmReply, "mute roster report (both arg/empty paths)");
                case "RangeHumCount": return Msg(rec, "report", SysMsgGmReply, "count players in range; report");
                case "QuizLevel":     return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.ExecutedWithGmMessage,
                                          "set-cap", "(inline)", SysMsgGmReply, false,
                                          "*off_7D611C = min(n,30) public-chat cap; confirm");

                // find-player guarded
                case "clearMulExp":   return EvalFindReplyBoth(rec, "sub_6E3FB0");   // both paths 0xFFDB
                case "showMulExp":    return EvalFindReplyBoth(rec, "(inline report)");
                case "ChgDmgShare":   return EvalChgDmgShare(a);
                case "Die":           return EvalDie(a);
                case "ClearAllState": return EvalFindSilent(rec, "(deferred)");
                case "Cattle":        return EvalCattle(a);
                case "LeaveTech":     return EvalLeaveTech(a);

                default:
                    // pure delegations (core does its own lookup/report):
                    // LookFor/OutSay/ShifangSay/ChgSelfHair/ChgSwTo/Gow*/
                    // ClearRelation/Upgradedata/UpExpdata/ChgWangshi/OpenZhuZaiShenYou
                    // (LeaveTech left this list on 2026-08-08: its sub_6C5E08 body is
                    // reversed and wired in LeaveTechCommand.cs -- see EvalLeaveTech.)
                    return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.Executed, "delegate",
                        rec.NativeCore, NoSysMsg, rec.CoreBodyDeferred, rec.EffectSummary);
            }
        }

        // clearMulExp / showMulExp: found -> apply/report; not-found -> error line. BOTH send 0xFFDB.
        private static NativePlayerAttrEvaluation EvalFindReplyBoth(NativePlayerAttrCommand rec, string core)
            => TargetPlayerFound
                ? new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.ExecutedWithGmMessage, "found", core,
                    SysMsgGmReply, true, "target found -> apply/report + SysMsg(0xFFDB)")
                : new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.RejectedWithGmMessage, "not-found", "sub_652784",
                    SysMsgGmReply, false, "FindPlayer failed -> error line SysMsg(0xFFDB)");

        // ChgDmgShare (359): found AND val>=0 -> set target[+1400] + recalc + 0xFFDB; else silent.
        private static NativePlayerAttrEvaluation EvalChgDmgShare(IReadOnlyList<string> a)
        {
            int val = StrToInt(Arg(a, 1), -1);
            if (TargetPlayerFound && val >= 0)
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.ExecutedWithGmMessage, "set",
                    "(inline)+vtbl+0x8C", SysMsgGmReply, true, "target[+1400]=val + vtbl+0x8C recalc + SysMsg(0xFFDB)");
            return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.RejectedSilently, "reject",
                "sub_652784", NoSysMsg, false, "target missing or val<0 -> silent");
        }

        // Die (358): arg -> find target else self; target != null -> Die(); else silent. No SysMsg.
        private static NativePlayerAttrEvaluation EvalDie(IReadOnlyList<string> a)
        {
            bool self = string.IsNullOrEmpty(Arg(a, 0));
            if (self || TargetPlayerFound)
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.Executed, self ? "self" : "target",
                    "vtbl+0x84", NoSysMsg, false, "target (self or found) -> vtbl+0x84 Die(); no SysMsg");
            return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.RejectedSilently, "not-found",
                "sub_652784", NoSysMsg, false, "named target not found -> silent");
        }

        // ClearAllState (575): found -> clear states (deferred); not-found -> silent. No proven shim SysMsg.
        private static NativePlayerAttrEvaluation EvalFindSilent(NativePlayerAttrCommand rec, string core)
            => TargetPlayerFound
                ? new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.Executed, "found", core,
                    NoSysMsg, true, "target found -> clear status effects (apply at LABEL_811, deferred)")
                : new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.RejectedSilently, "not-found", "sub_652784",
                    NoSysMsg, false, "FindPlayer failed -> silent");

        // Cattle (155): Add sub-op (found->0xFFDB / missing->0x38FF) vs OpenBox sub-op.
        private static NativePlayerAttrEvaluation EvalCattle(IReadOnlyList<string> a)
        {
            var op = Arg(a, 0).ToLowerInvariant();
            if (op == "openbox")
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.Executed, "openbox", "sub_716740",
                    NoSysMsg, true, "OpenBox 0..4 -> sub_716740() opens 天赐 box");
            // Add path (default): find target (or self) then add 牛气 when val>0.
            if (TargetPlayerFound)
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.ExecutedWithGmMessage, "add",
                    "sub_7159A0", SysMsgGmReply, true, "target found + val>0 -> sub_7159A0() add 牛气 + SysMsg(0xFFDB)");
            return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.RejectedWithGmMessage, "add-not-found",
                "sub_652784", SysMsgUsage, false, "Add target not found -> SysMsg(0x38FF)");
        }

        // LeaveTech (125): sub_6C5E08 reversed. Empty name -> silent return
        // (0x6C5E13). FindPlayer miss (0x6C5E27) OR target [+0xB95]==0 (0x6C5E29)
        // -> the SAME single failure line to the GM, cx=0xFFDB. Success ->
        // sub_6C5EC8(edx=0) then the accepted line TWICE (target 0x6C5E48,
        // GM 0x6C5E5B), also cx=0xFFDB.
        private static NativePlayerAttrEvaluation EvalLeaveTech(IReadOnlyList<string> a)
        {
            if (string.IsNullOrEmpty(Arg(a, 0)))
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.RejectedSilently, "nil-name",
                    "(entry)", NoSysMsg, false, "edx == nil -> 0x6C5E76 return; no effect, no message");
            if (!TargetPlayerFound || !TargetIsStudent)
                return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.RejectedWithGmMessage,
                    TargetPlayerFound ? "not-student" : "not-found", "sub_652784", SysMsgGmReply, false,
                    "0x6C5E63 one failure line to the GM only (0x6C5EA4), cx=0xFFDB");
            return new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.ExecutedWithGmMessage, "dissolve",
                "sub_6C5EC8", SysMsgGmReply, false,
                "sub_6C5EC8(edx=0) 自行离开师门, then 0x6C5E84 to target AND GM, cx=0xFFDB");
        }

        // -------- dormant hooks --------
        /// <summary>FindPlayerByName(sub_652784) oracle for the target-guarded commands.</summary>
        public static bool TargetPlayerFound { get; set; } = true;

        /// <summary>`cmp byte [ebx+0xB95],0` @0x6C5E29 oracle -- is the target a student?</summary>
        public static bool TargetIsStudent { get; set; } = true;

        // -------- helpers --------
        private static NativePlayerAttrEvaluation Msg(NativePlayerAttrCommand rec, string branch, int ident, string detail)
            => new NativePlayerAttrEvaluation(NativePlayerAttrOutcome.ExecutedWithGmMessage, branch, rec.NativeCore,
                ident, rec.CoreBodyDeferred, detail);

        private static string Arg(IReadOnlyList<string> a, int i)
            => (a != null && i < a.Count && a[i] != null) ? a[i] : "";
        private static int StrToInt(string s, int dflt) => int.TryParse(s, out int v) ? v : dflt;
    }
}
