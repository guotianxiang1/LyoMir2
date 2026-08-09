// -----------------------------------------------------------------------------
// NativeGmSkillMeridianCommands.cs
//
// DORMANT / FAIL-CLOSED reference model of the M2Server "战神" GM ("@") command
// family 02 SKILL / FORCE / MERIDIAN — the subset of the dispatcher sub_622820
// @0x00622820 that changes/deletes player & self skills, sets no-skill map zones,
// the合击 (union) skill cap, damage-resistance (FASTNESS) config, and skill
// cooldowns.
//
// DISJOINT from the SKILL/HERO/EQUIP set already modeled in
// NativeGmSkillEquipCommands.cs (AddSkillExp/DelSSKSkill/ChgSuperSKilllv/
// SmeltEquip/SetEquipComposelv/…). Cross-checked: none of this family's 10 records
// appear there. This file covers only the family-02 leftovers.
//
// NOT wired into the live pipeline; evidence-anchored spec for the audit
// (AuditTools/NativeGmSkillHeroLeftoversCheck) + porters.
//
// Source of truth: M2Server_unpacked_fixed.exe (战神), base 0x00400000,
// SHA256 5540f43b…c049670b14e; IDA db m2full.i64; dumps disp_decomp.txt /
// big622820.txt / world_scan_out.txt. Dispatch + SysMsg idents identical to the
// move/leitai + monster-map peers (0xFFDB reply / 0x38FF usage / 0xFCFF notice;
// no-op sinks def_622B15 @0x0062B648 and loc_62B64C @0x0062B64C).
//
// Delegating shims tail-call cores not in the dumps (CoreBodyDeferred=true ==
// needs-idat). SetUnionMaxLv performs an inline global write (off_7D5E58) that IS
// modeled; its per-instance persist (vtable call) is deferred.
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace GameSvr
{
    public enum NativeSkillMeridianOutcome
    {
        UnknownCommand, PermissionRejected, SilentNoOp,
        Executed, ExecutedWithGmMessage, RejectedSilently, RejectedWithGmMessage
    }

    public sealed class NativeSkillMeridianCommand
    {
        public const uint JumpTableBase = 0x00622B1C;
        public const uint DefaultHandler = 0x0062B648;
        public const uint EmptyBodyHandler = 0x0062B64C;

        public NativeSkillMeridianCommand(string name, int dispatchIndex, int requiredPerm,
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
        public string NativeCore { get; }
        public bool CoreBodyDeferred { get; }
        public string HelpGbk { get; }
        public string EffectSummary { get; }
    }

    public sealed class NativeSkillMeridianEvaluation
    {
        public NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome outcome, string branch,
            string nativeCore, int nativeSysMsgIdent, bool coreBodyDeferred, string detail)
        {
            Outcome = outcome; Branch = branch ?? ""; NativeCore = nativeCore ?? "";
            NativeSysMsgIdent = nativeSysMsgIdent; CoreBodyDeferred = coreBodyDeferred; Detail = detail ?? "";
        }
        public NativeSkillMeridianOutcome Outcome { get; }
        public string Branch { get; }
        public string NativeCore { get; }
        public int NativeSysMsgIdent { get; }
        public bool CoreBodyDeferred { get; }
        public string Detail { get; }
    }

    public static class NativeGmSkillMeridianCommands
    {
        public const uint DispatcherEa = 0x00622820;
        public const uint JumpTableEa = 0x00622B1C;
        public const uint DefaultCaseEa = 0x0062B648;
        public const int SysMsgGmReply = 0xFFDB;
        public const int SysMsgUsage = 0x38FF;
        public const int NoSysMsg = -1;

        // inline global + range proven by the shims
        public const uint UnionMaxLvGlobalEa = 0x007D5E58; // *off_7D5E58 = unionMaxLv (SetUnionMaxLv)
        public const int UnionMaxLvMin = 5;                // accepted range 5..10 ((lv-5) < 6)
        public const int UnionMaxLvMax = 10;

        // delegated cores (deferred / needs-idat)
        public const uint ChgSelfSkillLvCoreEa = 0x0073F500; // sub_73F500
        public const uint DelSelfSkillCoreEa = 0x0073F690;   // sub_73F690
        public const uint UpUserSkillCoreEa = 0x006C7644;    // sub_6C7644
        public const uint DelUserSkillCoreEa = 0x006C772C;   // sub_6C772C
        public const uint SetNoSkillZoneCoreEa = 0x006CDCA8; // sub_6CDCA8
        public const uint UnionConfigGetEa = 0x00790210;     // sub_790210 (union config obj)
        public const uint FastnessReloadEa = 0x007907B4;     // sub_7907B4 (reload resist config)
        public const uint ClearColdTimeCoreEa = 0x00748338;  // sub_748338

        private static readonly NativeSkillMeridianCommand[] _all =
        {
            new NativeSkillMeridianCommand("ChgSelfSkillLv", 94, 4, 0x00624FBBu, "sub_73F500", true,
                "改变自身技能等级	@ChgSelfSkillLv 技能名称 新的等级 经验值",
                "level=Str_ToInt(arg1); exp=Str_ToInt(arg2); sub_73F500(level,...) applies to self skill by name. No SysMsg."),
            new NativeSkillMeridianCommand("DelSelfSkill", 95, 4, 0x00624FE8u, "sub_73F690", true,
                "删除自身技能	@DelSelfSkill 技能名称",
                "Pure delegation: sub_73F690() removes the named self skill. No SysMsg."),
            new NativeSkillMeridianCommand("UpUserSkill", 218, 5, 0x00625F6Au, "sub_6C7644", true,
                "升级玩家技能	@UpUserSkill 角色名 技能名 技能等级 技能经验",
                "level=Str_ToInt; exp=Str_ToInt; sub_6C7644(exp,level,...) upgrades a target player's skill. No SysMsg."),
            new NativeSkillMeridianCommand("DelUserSkill", 219, 5, 0x00625F9Au, "sub_6C772C", true,
                "删除玩家技能	@DelUserSkill 角色名 技能名",
                "Pure delegation: sub_6C772C(charName, skillName) removes a target player's skill. No SysMsg."),
            new NativeSkillMeridianCommand("SetSmjd", 323, 5, 0x0062B648u, "", false,
                "设置玩家的神秘解读技能的1:熟练度、2:使用的精力值、3:幸运值	@SetSmjd 角色名 [1/2/3] 值",
                "Registered (idx 323, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativeSkillMeridianCommand("SetNoSkillZone", 393, 5, 0x006286D5u, "sub_6CDCA8", true,
                "设置地图点能否使用技能	@SetNoSkillZone left right top bot [on/off]",
                "left/right/top/bot = 4 Str_ToInt; when ALL >= 0 AND arg4 matches on/off -> sub_6CDCA8(onoff,bot,top) "
                + "marks the rect; otherwise SysMsg(0xFFDB) error/usage."),
            new NativeSkillMeridianCommand("SetUnionMaxLv", 406, 5, 0x006289AAu, "sub_790210", true,
                "设置服务器合击技能等级上限	@SetUnionMaxLv 等级值(5..10)",
                "config=sub_790210(); if null -> silent. lv=Str_ToInt; lv not in 5..10 -> SysMsg(0x38FF) usage; else "
                + "*off_7D5E58 = lv (inline) + persist(\"unionMaxLv\",\"setup\",lv) + SysMsg(0xFFDB) confirm."),
            new NativeSkillMeridianCommand("LearnSuperForce", 445, 4, 0x0062B648u, "", false,
                "学习内功心法(心法类型:1..5)	@LearnSuperForce 心法名字 心法类型",
                "Registered (idx 445, perm 4) but = def_622B15 (default sink): silent no-op."),
            new NativeSkillMeridianCommand("FASTNESS", 480, 4, 0x00628FC2u, "sub_7907B4", true,
                "加载合击伤害抗性、火墙伤害抗性、近战伤害抗性配置文件	@FASTNESS [空/UNION/HQ/NEARHit]",
                "Always sub_7907B4() reloads the resistance config, then reports via SysMsg(0xFFDB); the report text is "
                + "selected by the arg token (UNION/HQ/NEARHIT/LSJN/… incl. empty). Report-selector, always 0xFFDB."),
            new NativeSkillMeridianCommand("ClearColdTime", 547, 4, 0x006297DEu, "sub_748338", true,
                "清空技能冷却时间	@ClearColdTime",
                "Pure delegation: sub_748338() clears skill cooldowns. No SysMsg."),
        };

        private static readonly Dictionary<string, NativeSkillMeridianCommand> _byName = BuildIndex(_all);
        private static Dictionary<string, NativeSkillMeridianCommand> BuildIndex(NativeSkillMeridianCommand[] cmds)
        {
            var map = new Dictionary<string, NativeSkillMeridianCommand>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in cmds) map[c.Name] = c;
            return map;
        }

        public static IReadOnlyList<NativeSkillMeridianCommand> All => _all;
        public static NativeSkillMeridianCommand Find(string name)
            => (name != null && _byName.TryGetValue(name, out var c)) ? c : null;

        public static NativeGmDefaultNoOp EvaluateUnimplemented(string name)
        {
            var rec = Find(name);
            if (rec == null) throw new System.ArgumentException($"'{name}' is not a skill/meridian command", nameof(name));
            if (rec.Implemented) throw new System.InvalidOperationException($"{rec.Name} is implemented; use Evaluate");
            return new NativeGmDefaultNoOp
            { Recognized = true, DispatchesToDefaultCase = true, MutatesState = false, SendsResponse = false };
        }

        public static NativeSkillMeridianEvaluation Evaluate(string name, int callerPerm, IReadOnlyList<string> args)
        {
            var rec = Find(name);
            if (rec == null)
                return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.UnknownCommand, "", "", NoSysMsg, false,
                    "token is not in the skill/meridian family");
            if (callerPerm < rec.RequiredPerm)
                return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.PermissionRejected, "", "", NoSysMsg, false,
                    "callerPerm " + callerPerm + " < requiredPerm " + rec.RequiredPerm + "; sub_621F28 -> 0 -> def_622B15");
            if (!rec.Implemented)
                return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.SilentNoOp, "", "", NoSysMsg, false,
                    "handler == def_622B15 (silent no-op); no effect, no message");

            var a = args ?? System.Array.Empty<string>();
            switch (rec.Name)
            {
                case "SetNoSkillZone": return EvalSetNoSkillZone(a);
                case "SetUnionMaxLv":  return EvalSetUnionMaxLv(a);
                case "FASTNESS":       return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.ExecutedWithGmMessage,
                                           "reload-report", "sub_7907B4", SysMsgGmReply, true,
                                           "sub_7907B4 reload; SysMsg(0xFFDB) report selected by arg token");
                default: // ChgSelfSkillLv / DelSelfSkill / UpUserSkill / DelUserSkill / ClearColdTime
                    return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.Executed, "delegate",
                        rec.NativeCore, NoSysMsg, rec.CoreBodyDeferred, rec.EffectSummary);
            }
        }

        // SetNoSkillZone (393): 4 coords >=0 AND on/off token -> apply; else error.
        private static NativeSkillMeridianEvaluation EvalSetNoSkillZone(IReadOnlyList<string> a)
        {
            bool coordsOk = TryCoord(Arg(a, 0)) && TryCoord(Arg(a, 1)) && TryCoord(Arg(a, 2)) && TryCoord(Arg(a, 3));
            string t = Arg(a, 4).ToLowerInvariant();
            bool tokenOk = t == "on" || t == "off";
            if (coordsOk && tokenOk)
                return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.Executed, "apply",
                    "sub_6CDCA8", NoSysMsg, true, "all coords>=0 and on/off token -> sub_6CDCA8(onoff,bot,top); no SysMsg");
            return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.RejectedWithGmMessage, "invalid",
                "(inline)", SysMsgGmReply, false, "a coord<0 or bad on/off token -> SysMsg(0xFFDB) error");
        }

        // SetUnionMaxLv (406): config? lv in 5..10 -> set+persist+confirm; else usage.
        private static NativeSkillMeridianEvaluation EvalSetUnionMaxLv(IReadOnlyList<string> a)
        {
            if (!UnionConfigExists)
                return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.RejectedSilently, "no-config",
                    "sub_790210", NoSysMsg, false, "sub_790210() returned null -> silent");
            int lv = StrToInt(Arg(a, 0), 0);
            if (lv < UnionMaxLvMin || lv > UnionMaxLvMax)
                return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.RejectedWithGmMessage, "out-of-range",
                    "(inline)", SysMsgUsage, false, "lv not in 5..10 -> SysMsg(0x38FF) usage");
            return new NativeSkillMeridianEvaluation(NativeSkillMeridianOutcome.ExecutedWithGmMessage, "set",
                "(inline)+persist", SysMsgGmReply, true,
                "*off_7D5E58 = " + lv + " + persist(\"unionMaxLv\") + SysMsg(0xFFDB) confirm");
        }

        /// <summary>SetUnionMaxLv: did sub_790210() return a non-null config object?</summary>
        public static bool UnionConfigExists { get; set; } = true;

        private static string Arg(IReadOnlyList<string> a, int i)
            => (a != null && i < a.Count && a[i] != null) ? a[i] : "";
        private static int StrToInt(string s, int dflt) => int.TryParse(s, out int v) ? v : dflt;
        private static bool TryCoord(string s) => int.TryParse(s, out int v) && v >= 0;
    }
}
