// -----------------------------------------------------------------------------
// NativeGmHeroFieldCommands.cs
//
// DORMANT / FAIL-CLOSED reference model of the M2Server "战神" GM ("@") command
// family 03 HERO / FIELDHERO — the subset of the dispatcher sub_622820
// @0x00622820 that rests/upgrades the GM's hero, changes a player's hero
// exp/fealty/break-level, reloads the level-up prompt files, teaches skills, and
// views hero attributes; plus the family's 10 registered no-ops (SSK/meridian/
// point/force sub-commands unimplemented in this build).
//
// DISJOINT from the HERO/PET/SUMMON set already modeled in
// NativeGmHeroPetCommands.cs (CreateHero/CallHero/AddHeroExp/UpUserHeroLv/
// ChgHeroSkill/MakeMyHero/DelHero/SetCallHero/…) and the SKILL/EQUIP set in
// NativeGmSkillEquipCommands.cs. Cross-checked: none of this family's 18 records
// appear in either. This file covers only the family-03 leftovers.
//
// NOT wired into the live pipeline; evidence-anchored spec for the audit
// (AuditTools/NativeGmSkillHeroLeftoversCheck) + porters.
//
// Source of truth: M2Server_unpacked_fixed.exe (战神), base 0x00400000,
// SHA256 5540f43b…c049670b14e; IDA db m2full.i64; dumps disp_decomp.txt /
// big622820.txt / world_scan_out.txt. Dispatch + SysMsg idents identical to the
// peers (0xFFDB reply / 0x38FF usage / 0xFCFF notice; no-op sinks def_622B15
// @0x0062B648 and loc_62B64C @0x0062B64C).
//
// The GM's hero pointer is *(uint*)(self + 0xBB0) (self[+748] dword). RestHero and
// UpGradeHero gate on it being non-null. Delegating shims tail-call cores not in
// the dumps (CoreBodyDeferred=true == needs-idat); UpGradeHero performs inline
// hero-field writes whose level-up broadcast core is deferred.
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace GameSvr
{
    public enum NativeHeroFieldOutcome
    {
        UnknownCommand, PermissionRejected, SilentNoOp,
        Executed, ExecutedWithGmMessage, RejectedSilently, RejectedWithGmMessage
    }

    public sealed class NativeHeroFieldCommand
    {
        public const uint JumpTableBase = 0x00622B1C;
        public const uint DefaultHandler = 0x0062B648;
        public const uint EmptyBodyHandler = 0x0062B64C;

        public NativeHeroFieldCommand(string name, int dispatchIndex, int requiredPerm,
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

    public sealed class NativeHeroFieldEvaluation
    {
        public NativeHeroFieldEvaluation(NativeHeroFieldOutcome outcome, string branch,
            string nativeCore, int nativeSysMsgIdent, bool coreBodyDeferred, string detail)
        {
            Outcome = outcome; Branch = branch ?? ""; NativeCore = nativeCore ?? "";
            NativeSysMsgIdent = nativeSysMsgIdent; CoreBodyDeferred = coreBodyDeferred; Detail = detail ?? "";
        }
        public NativeHeroFieldOutcome Outcome { get; }
        public string Branch { get; }
        public string NativeCore { get; }
        public int NativeSysMsgIdent { get; }
        public bool CoreBodyDeferred { get; }
        public string Detail { get; }
    }

    public static class NativeGmHeroFieldCommands
    {
        public const uint DispatcherEa = 0x00622820;
        public const uint JumpTableEa = 0x00622B1C;
        public const uint DefaultCaseEa = 0x0062B648;
        public const uint EmptyBodyCaseEa = 0x0062B64C;
        public const int SysMsgGmReply = 0xFFDB;
        public const int SysMsgUsage = 0x38FF;
        public const int SysMsgNotice = 0xFCFF;
        public const int NoSysMsg = -1;

        public const int HeroPtrSelfOffset = 0xBB0; // *(uint*)(self + 0xBB0) == GM's hero

        // delegated cores (deferred / needs-idat)
        public const uint RestHeroCoreEa = 0x00688650;    // sub_688650 (toggle hero rest/action)
        public const uint RestHeroStateEa = 0x00772DA8;   // sub_772DA8 (negated guard)
        public const uint UpGradeHeroEffectEa = 0x0069B14C; // sub_69B14C (hero level-up effect/broadcast)
        public const uint UpUserHeroExpCoreEa = 0x006D1E98; // sub_6D1E98
        public const uint ChgHeroFealtyCoreEa = 0x006D2C7C; // sub_6D2C7C
        public const uint ChgBreakLevelCoreEa = 0x006E6EF0; // sub_6E6EF0(heroFlag, pos)
        public const uint ReloadPromptFileCoreEa = 0x0069B15C; // sub_69B15C
        public const uint LearnSkillCoreEa = 0x0074665C;    // sub_74665C (learn skill)
        public const uint FindPlayerEa = 0x00652784;        // sub_652784 (find player by name)
        public const uint HeroAbilCoreEa = 0x006F3284;      // sub_6F3284 (view hero attrs -> report in core)

        private static readonly NativeHeroFieldCommand[] _all =
        {
            // ---- implemented (8) ----
            new NativeHeroFieldCommand("RestHero", 28, 0, 0x00623AD1u, "sub_688650", true,
                "设置英雄的行动	@RestHero",
                "hero=*(self+0xBB0); when hero!=null AND !sub_772DA8() -> sub_688650(...) toggles the hero's "
                + "rest/action state + SysMsg(0xFCFF); otherwise silent."),
            new NativeHeroFieldCommand("UpGradeHero", 69, 3, 0x00624AD1u, "sub_69B14C", true,
                "提升自身英雄等级	@UpGradeHero 等级数",
                "n=Str_ToInt(arg); when hero=*(self+0xBB0)!=null AND n>0 -> inline hero level fields set "
                + "(hero+0x278/hero+0x1FC = level from hero+0x278 base) + level-up effect sub_69B14C + client push; "
                + "otherwise silent. No shim SysMsg."),
            new NativeHeroFieldCommand("UpUserHeroExp", 223, 5, 0x00626168u, "sub_6D1E98", true,
                "更改玩家的英雄的当前经验值	@UpUserHeroExp 角色名 英雄的新经验值",
                "Pure delegation: sub_6D1E98(charName, exp). No SysMsg."),
            new NativeHeroFieldCommand("ChgHeroFealty", 227, 5, 0x006261C5u, "sub_6D2C7C", true,
                "更改玩家的英雄的忠诚值(0..4999)	@ChgHeroFealty 角色名 英雄忠诚值",
                "Pure delegation: sub_6D2C7C(charName, fealty). No SysMsg."),
            new NativeHeroFieldCommand("ChgBreakLevel", 308, 5, 0x006272B7u, "sub_6E6EF0", true,
                "更改装备暴击等级(最后一个参数为hero表示英雄否则是主号)	@ChgBreakLevel 角色名 装备pos 暴击等级值 [hero/无]",
                "last arg == \"hero\" -> sub_6E6EF0(0, pos); otherwise sub_6E6EF0(1, pos). Two delegated branches, no SysMsg."),
            new NativeHeroFieldCommand("ReloadPromptFile", 367, 4, 0x0062821Du, "sub_69B15C", true,
                "重载升级提示文件 升级提示.txt;心法升级提示.txt;英雄心法升级提示.txt	@ReloadPromptFile",
                "Always: sub_69B15C() reloads the level-up prompt files, then SysMsg(0xFFDB) confirm."),
            new NativeHeroFieldCommand("LearnSkill", 379, 5, 0x006283F2u, "sub_74665C", true,
                "GM自身或GM的英雄学习新的技能(1表示英雄,否则主号)	@LearnSkill 技能名 [1/非1]",
                "flag==1 -> hero learns (target=hero, gate sub_74665C); else main char learns. found+can-learn -> "
                + "SysMsg(0xFCFF) success; target not found -> SysMsg(0x38FF) error. Core sub_74665C deferred."),
            new NativeHeroFieldCommand("HeroAbil", 548, 4, 0x00623BD1u, "sub_6F3284", true,
                "查看英雄属性	@HeroAbil",
                "Pure delegation: sub_6F3284() emits the hero-attribute report inside the core; the shim sends no SysMsg."),

            // ---- registered no-ops (10): 9 at def_622B15, 1 at loc_62B64C ----
            new NativeHeroFieldCommand("KingActorVal", 178, 4, 0x0062B648u, "", false,
                "增加自身或玩家的英雄点	@KingActorVal [无/角色名] 英雄点",
                "Registered (idx 178, perm 4) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("SetSSKLv", 241, 5, 0x0062B648u, "", false,
                "设置GM自身或英雄的连击技能等级	@SetSSKLv 技能索引值 技能等级 [1/非1]",
                "Registered (idx 241, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("SetSSKColdTime", 243, 5, 0x0062B648u, "", false,
                "设置GM自身或英雄的连击技能的冷却时间	@SetSSKColdTime 冷却时间 [1/非1]",
                "Registered (idx 243, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("UpgradeJM", 244, 5, 0x0062B648u, "", false,
                "设置GM自身或英雄的指定经脉的等级	@UpgradeJM 经脉索引 经脉等级 [1/非1]",
                "Registered (idx 244, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("OpenPoint", 245, 5, 0x0062B648u, "", false,
                "打开GM自身或英雄的指定穴位及之前的所有穴位	@OpenPoint 穴位编号 [1/非1]",
                "Registered (idx 245, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("ClearSSKInfo", 247, 5, 0x0062B648u, "", false,
                "清除GM自身或英雄的所有连击技能相关信息	@ClearSSKInfo [1/非1]",
                "Registered (idx 247, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("SetForceDB", 441, 5, 0x0062B648u, "", false,
                "修改玩家主号或英雄的内功等级,若不在线则修改DB里的值	@SetForceDB 角色名/英雄名 内功等级值",
                "Registered (idx 441, perm 5) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("EnableHeroSF", 446, 4, 0x0062B648u, "", false,
                "GM激活自身的英雄的心法	@EnableHeroSF",
                "Registered (idx 446, perm 4) but = def_622B15 (default sink): silent no-op."),
            new NativeHeroFieldCommand("HeroHypericumUsed", 489, 4, 0x0062B64Cu, "", false,
                "查询玩家今天使用的英雄火龙珠的量	@HeroHypericumUsed 角色名",
                "Registered (idx 489, perm 4) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativeHeroFieldCommand("GetTigeScore", 541, 4, 0x0062B648u, "", false,
                "查看体格洗炼次数。2表示英雄	@GetTigeScore 角色名 [2]",
                "Registered (idx 541, perm 4) but = def_622B15 (default sink): silent no-op."),
        };

        private static readonly Dictionary<string, NativeHeroFieldCommand> _byName = BuildIndex(_all);
        private static Dictionary<string, NativeHeroFieldCommand> BuildIndex(NativeHeroFieldCommand[] cmds)
        {
            var map = new Dictionary<string, NativeHeroFieldCommand>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in cmds) map[c.Name] = c;
            return map;
        }

        public static IReadOnlyList<NativeHeroFieldCommand> All => _all;
        public static NativeHeroFieldCommand Find(string name)
            => (name != null && _byName.TryGetValue(name, out var c)) ? c : null;

        public static NativeGmDefaultNoOp EvaluateUnimplemented(string name)
        {
            var rec = Find(name);
            if (rec == null) throw new System.ArgumentException($"'{name}' is not a hero/field command", nameof(name));
            if (rec.Implemented) throw new System.InvalidOperationException($"{rec.Name} is implemented; use Evaluate");
            return new NativeGmDefaultNoOp
            { Recognized = true, DispatchesToDefaultCase = true, MutatesState = false, SendsResponse = false };
        }

        public static NativeHeroFieldEvaluation Evaluate(string name, int callerPerm, IReadOnlyList<string> args)
        {
            var rec = Find(name);
            if (rec == null)
                return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.UnknownCommand, "", "", NoSysMsg, false,
                    "token is not in the hero/field family");
            if (callerPerm < rec.RequiredPerm)
                return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.PermissionRejected, "", "", NoSysMsg, false,
                    "callerPerm " + callerPerm + " < requiredPerm " + rec.RequiredPerm + "; sub_621F28 -> 0 -> def_622B15");
            if (!rec.Implemented)
                return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.SilentNoOp, "", "", NoSysMsg, false,
                    "handler == " + (rec.HandlerAddress == NativeHeroFieldCommand.EmptyBodyHandler ? "loc_62B64C" : "def_622B15")
                    + " (silent no-op); no effect, no message");

            var a = args ?? System.Array.Empty<string>();
            switch (rec.Name)
            {
                case "RestHero":         return EvalRestHero();
                case "UpGradeHero":      return EvalUpGradeHero(a);
                case "ChgBreakLevel":    return EvalChgBreakLevel(a);
                case "LearnSkill":       return EvalLearnSkill(a);
                case "ReloadPromptFile": return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.ExecutedWithGmMessage,
                                             "reload", "sub_69B15C", SysMsgGmReply, true,
                                             "sub_69B15C reload prompt files + SysMsg(0xFFDB)");
                default: // UpUserHeroExp / ChgHeroFealty / HeroAbil
                    return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.Executed, "delegate",
                        rec.NativeCore, NoSysMsg, rec.CoreBodyDeferred, rec.EffectSummary);
            }
        }

        // RestHero (28): hero present && !sub_772DA8() -> toggle + notice; else silent.
        private static NativeHeroFieldEvaluation EvalRestHero()
            => (HeroPresent && !RestHeroBlocked)
                ? new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.ExecutedWithGmMessage, "toggle",
                    "sub_688650", SysMsgNotice, true, "hero present + allowed -> sub_688650() toggle + SysMsg(0xFCFF)")
                : new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.RejectedSilently, "no-hero-or-blocked",
                    "sub_688650", NoSysMsg, false, "hero absent or sub_772DA8() true -> silent");

        // UpGradeHero (69): hero present && level>0 -> apply; else silent.
        private static NativeHeroFieldEvaluation EvalUpGradeHero(IReadOnlyList<string> a)
        {
            int n = StrToInt(Arg(a, 0), 0);
            return (HeroPresent && n > 0)
                ? new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.Executed, "apply", "sub_69B14C",
                    NoSysMsg, true, "hero present and level>0 -> inline hero level fields + sub_69B14C effect; no SysMsg")
                : new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.RejectedSilently, "no-hero-or-zero",
                    "sub_69B14C", NoSysMsg, false, "hero absent or level<=0 -> silent");
        }

        // ChgBreakLevel (308): "hero" token -> sub_6E6EF0(0,...) else (1,...).
        private static NativeHeroFieldEvaluation EvalChgBreakLevel(IReadOnlyList<string> a)
        {
            bool hero = string.Equals(LastArg(a), "hero", System.StringComparison.OrdinalIgnoreCase);
            return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.Executed, hero ? "hero" : "main",
                "sub_6E6EF0", NoSysMsg, true,
                "sub_6E6EF0(" + (hero ? "0" : "1") + ", pos) sets equip crit level; no SysMsg");
        }

        // LearnSkill (379): hero (flag==1) vs main; found -> 0xFCFF, not-found -> 0x38FF.
        private static NativeHeroFieldEvaluation EvalLearnSkill(IReadOnlyList<string> a)
        {
            bool hero = LastArg(a) == "1";
            string branch = hero ? "hero" : "main";
            if (!LearnSkillTargetFound)
                return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.RejectedWithGmMessage, branch + "-not-found",
                    "sub_652784", SysMsgUsage, true, "target (" + branch + ") not found / cannot learn -> SysMsg(0x38FF)");
            return new NativeHeroFieldEvaluation(NativeHeroFieldOutcome.ExecutedWithGmMessage, branch + "-learn",
                "sub_74665C", SysMsgNotice, true, "target (" + branch + ") learns via sub_74665C -> SysMsg(0xFCFF)");
        }

        // -------- dormant hooks --------
        /// <summary>Is the GM's hero pointer (*(self+0xBB0)) non-null? (RestHero, UpGradeHero)</summary>
        public static bool HeroPresent { get; set; } = true;
        /// <summary>RestHero: sub_772DA8() guard (true blocks the toggle).</summary>
        public static bool RestHeroBlocked { get; set; }
        /// <summary>LearnSkill: was the learn target (hero or main) found and able to learn?</summary>
        public static bool LearnSkillTargetFound { get; set; } = true;

        // -------- helpers --------
        private static string Arg(IReadOnlyList<string> a, int i)
            => (a != null && i < a.Count && a[i] != null) ? a[i] : "";
        private static string LastArg(IReadOnlyList<string> a)
            => (a != null && a.Count > 0 && a[a.Count - 1] != null) ? a[a.Count - 1] : "";
        private static int StrToInt(string s, int dflt) => int.TryParse(s, out int v) ? v : dflt;
    }
}
