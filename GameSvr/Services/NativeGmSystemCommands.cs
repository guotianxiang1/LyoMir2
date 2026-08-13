// -----------------------------------------------------------------------------
// NativeGmSystemCommands.cs
//
// DORMANT / FAIL-CLOSED reference model of the M2Server "战神" (God-of-War)
// SYSTEM / SERVER / GM-ADMIN GM ("@") command family — the subset of the single
// GM dispatcher sub_622820 @0x00622820 that queries/administers the SERVER itself:
// online-count reports, config/script reloads, the server clock read-back,
// mirparam (script global) get/set, quest-field get/set, log-switch control,
// GM self-promotion / GM attack-power, and a handful of test/diagnostic verbs.
// This is NOT wired into the live command pipeline; it is an evidence-anchored
// specification that the audit (AuditTools/NativeGmSystemCommandsCheck) pins
// against the binary and that porters can consult when replacing the C# stubs.
//
// Source of truth
//   Binary : M2Server_unpacked_fixed.exe (战神), image base 0x00400000,
//            SHA256 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e
//   IDA db : staging/update_clothes_4637_ida_work/m2full.i64
//   Dumps  : disp_decomp.txt (full sub_622820 Hex-Rays switch, case == dispatchIndex),
//            big622820.txt   (full sub_622820 disassembly, case-branch addresses inline),
//            gm_full_inventory_20260731.md (name / index / perm / handler census).
//
// Dispatch contract (already reversed by peers, reused verbatim here)
//   * The GM string "@name p0:p1,p2 ..." is split; the first token selects a
//     static command record (name ShortString; +0x18 = dispatchIndex,
//     +0x1C = requiredPerm; GBK help follows).
//   * esi = sub_621F28(player, name, callerPerm, &reqPerm) @0x00621F28 returns
//     the record's dispatchIndex ONLY when callerPerm >= reqPerm, else 0.
//   * cmp esi,0x2EE(750); ja default; jmp jpt_622B15[esi*4]  (table @0x00622B1C,
//     752 slots). Two shared no-op sinks:
//       - def_622B15 @0x0062B648: sets the handled byte var_D=0, no effect/msg.
//       - loc_62B64C @0x0062B64C: the shared empty-case exit (handled byte stays 1),
//         no effect/msg. Named records whose slot points here are registered-but-empty.
//   * HandlerAddress in THIS model is the CASE-BRANCH address (the jump-table slot
//     value), NOT the delegated core sub. NativeCore names the delegated helper.
//     (Verified against big622820.txt: e.g. slot@idx27 -> 0x00623A42 "case 27".)
//
// Feedback idents. Case bodies talk back through two player vtable slots:
//   * SysMsg  = vtable+0xD4 (+212): cx word = message ident. Two idents occur in
//     this family — 0xFFDB (GM reply, cx=-37) and 0x38FF (usage/notice, cx=14591).
//   * SendEffect = vtable+0xD8 (+216): a visual packet, NOT a text SysMsg. SkyRocket
//     (cx=0x277D) and TestBodyEffect (cx=0x2905) fire this; the dispatcher sends no
//     text, so those evaluate to Executed / NoSysMsg.
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>Observable outcome class of one system/server GM invocation.</summary>
    public enum NativeSystemAdminOutcome
    {
        /// <summary>Token is not a member of the system/server family modeled here.</summary>
        UnknownCommand,

        /// <summary>
        /// callerPerm &lt; requiredPerm: sub_621F28 returns index 0, the switch lands
        /// on def_622B15, handled=0. Indistinguishable from an unknown command.
        /// </summary>
        PermissionRejected,

        /// <summary>
        /// Handler is one of the two shared no-op sinks (def_622B15 0x0062B648 or the
        /// empty-case exit 0x0062B64C): the command IS registered (name/index/perm/help
        /// present) but this build ships no case body. No effect, no message.
        /// </summary>
        SilentNoOp,

        /// <summary>Native helper invoked / server state mutated; no inline GM message.</summary>
        Executed,

        /// <summary>As <see cref="Executed"/>, plus an inline confirmation/report SysMsg.</summary>
        ExecutedWithGmMessage,

        /// <summary>A native guard refused the action AND sent the GM an inline SysMsg.</summary>
        RejectedWithGmMessage,

        /// <summary>
        /// A native guard refused the action with NO message (a required argument was
        /// absent/invalid, a feature was inactive, or a lookup missed — the case jumps
        /// straight to the shared exit before any SysMsg).
        /// </summary>
        RejectedSilently
    }

    /// <summary>
    /// One static GM command record as it exists in the binary's command table,
    /// restricted to the system / server / GM-admin family.
    /// </summary>
    public sealed class NativeSystemAdminCommand
    {
        public const uint JumpTableBase = 0x00622B1C;   // jpt_622B15
        public const uint DefaultHandler = 0x0062B648;  // def_622B15  (handled=0 no-op)
        public const uint EmptyBodyHandler = 0x0062B64C; // loc_62B64C  (handled=1 empty case)

        public NativeSystemAdminCommand(string name, int dispatchIndex,
            int requiredPerm, uint handlerAddress, string nativeCore,
            NativeSystemAdminOutcome baseOutcome, int baseSysMsgIdent,
            string helpGbk, string effectSummary)
        {
            Name = name;
            DispatchIndex = dispatchIndex;
            RequiredPerm = requiredPerm;
            HandlerAddress = handlerAddress;
            NativeCore = nativeCore ?? "";
            BaseOutcome = baseOutcome;
            BaseSysMsgIdent = baseSysMsgIdent;
            HelpGbk = helpGbk ?? "";
            EffectSummary = effectSummary ?? "";
        }

        public string Name { get; }
        public int DispatchIndex { get; }
        public int RequiredPerm { get; }

        /// <summary>Case-branch address = *(JumpTableBase + DispatchIndex*4).</summary>
        public uint HandlerAddress { get; }

        /// <summary>Jump-table slot that stores <see cref="HandlerAddress"/>.</summary>
        public uint JumpSlotAddress => JumpTableBase + (uint)DispatchIndex * 4;

        /// <summary>False iff the handler is one of the two shared no-op sinks.</summary>
        public bool Implemented =>
            HandlerAddress != DefaultHandler && HandlerAddress != EmptyBodyHandler;

        /// <summary>Delegated core helper the case body calls ("" / "(inline)" when none).</summary>
        public string NativeCore { get; }

        /// <summary>
        /// Outcome for commands whose case body has a single, unconditional path
        /// (no GM-visible guard). Branch-bearing commands override this in Evaluate.
        /// </summary>
        public NativeSystemAdminOutcome BaseOutcome { get; }

        /// <summary>SysMsg ident on that single path (<see cref="NativeGmSystemCommands.NoSysMsg"/> if none).</summary>
        public int BaseSysMsgIdent { get; }

        /// <summary>GBK help string carried by the record.</summary>
        public string HelpGbk { get; }

        /// <summary>Prose description of the server effect (for porters).</summary>
        public string EffectSummary { get; }
    }

    /// <summary>Result of dormant-evaluating one invocation against the model.</summary>
    public sealed class NativeSystemAdminEvaluation
    {
        public NativeSystemAdminEvaluation(NativeSystemAdminOutcome outcome,
            string branch, string nativeCore, int nativeSysMsgIdent, string detail)
        {
            Outcome = outcome;
            Branch = branch ?? "";
            NativeCore = nativeCore ?? "";
            NativeSysMsgIdent = nativeSysMsgIdent;
            Detail = detail ?? "";
        }

        public NativeSystemAdminOutcome Outcome { get; }

        /// <summary>Which internal branch fired (e.g. "event", "all", "open", "locked").</summary>
        public string Branch { get; }

        /// <summary>Native core invoked on this branch ("" / "(inline)" if none).</summary>
        public string NativeCore { get; }

        /// <summary>cx word passed to SysMsg (player_vtable+0xD4); -1 when no message.</summary>
        public int NativeSysMsgIdent { get; }

        public string Detail { get; }
    }

    /// <summary>
    /// Fail-closed model of the system / server / GM-admin GM command family. Pure
    /// data + <see cref="Evaluate"/>; performs no I/O and mutates nothing.
    /// </summary>
    public static class NativeGmSystemCommands
    {
        // Native inline SysMsg idents (cx word handed to player_vtable+0xD4).
        public const int SysMsgGmReply = 0xFFDB; // ordinary GM feedback / report   (cx = -37)
        public const int SysMsgUsage = 0x38FF;   // usage / notice / multi-line list (cx = 14591)
        public const int NoSysMsg = -1;

        // Visual SendEffect idents (player_vtable+0xD8) — recorded for evidence only;
        // these are NOT text SysMsg, so the commands that fire them evaluate NoSysMsg.
        public const int EffectSkyRocket = 0x277D;  // SkyRocket arg path
        public const int EffectBodyEffect = 0x2905; // TestBodyEffect

        private static readonly NativeSystemAdminCommand[] _all =
        {
            // ---- online-count / server-info read-only queries -----------------
            new NativeSystemAdminCommand("GsZx", 55, 3, 0x006240E7, "sub_652BDC",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "当前GS的在线人数",
                "sub_652BDC counts this GameServer's online players; a GM SysMsg (0xFFDB) reports it. Read-only."),
            new NativeSystemAdminCommand("AllZx", 56, 3, 0x00624122, "sub_65411C",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "当前组服务器的在线人数",
                "sub_65411C sums the whole server-group's online count; a GM SysMsg (0xFFDB) reports it. Read-only."),
            new NativeSystemAdminCommand("SkyIncome", 138, 4, 0x006253DD, "(inline report)",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgUsage,
                "查询天关相关信息",
                "Formats several 天关 (sky-gate) income/statistics fields into one line and reports it via a "
                + "usage-ident SysMsg (0x38FF). Read-only."),
            new NativeSystemAdminCommand("GetSysTime", 273, 4, 0x00626AF3, "sub_410B3C",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "获取系统服务器当前时间",
                "sub_410B3C formats the current server clock (off_7D6A88); a GM SysMsg (0xFFDB) reports it. Read-only."),

            // ---- self level / stat administration -----------------------------
            new NativeSystemAdminCommand("UpGrade", 68, 2, 0x00624A50, "(inline)",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "提升自身等级",
                "Parses [level]; writes it to the GM's own level word (obj+0x278) and mirror (+0x1FC), recalcs "
                + "abilities (vtable+0x240), then sends a level-up effect (vtable+0x250, cx=0x0B3E) if a prompt exists. "
                + "No inline SysMsg."),
            new NativeSystemAdminCommand("DoNewRes", 205, 5, 0x00625D89, "sub_6C6CAC",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "GM设置自身武器的属性值(攻击、魔法、道术、准确)",
                "Passes the argument tail to sub_6C6CAC, which sets the GM's own weapon attack/magic/taoism/accuracy. "
                + "No inline SysMsg."),

            // ---- self GM privilege / attack-power -----------------------------
            new NativeSystemAdminCommand("supergm", 119, 4, 0x00625253, "sub_6D782C",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "升级超级GM",
                "sub_6D782C promotes the caller to super-GM. The helper handles any messaging; the case sends none inline."),
            new NativeSystemAdminCommand("GMPower", 475, 5, 0x0062919D, "(inline)",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgUsage,
                "进入/退出GM攻击模式",
                "Toggles the GM attack-power flag (obj+0xFFC ^= 1); a usage-ident SysMsg (0x38FF) reports entered/exited. "
                + "Always sends a message."),

            // ---- visual / test verbs (SendEffect, no text SysMsg) -------------
            new NativeSystemAdminCommand("SkyRocket", 70, 3, 0x00624BAB, "sub_718004/sub_7181F0",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "放烟花",
                "With a [type] arg: sends a firework visual via SendEffect (vtable+0xD8, cx=0x277D). Without: fires the "
                + "default firework (sub_718004(0,60000,x,y) + sub_7181F0). No text SysMsg."),
            new NativeSystemAdminCommand("TestBodyEffect", 296, 5, 0x00626ED3, "(inline)",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "程序自用测试命令，测试bodyeffect效果",
                "Developer self-test: parses an effect id and sends a body-effect visual via SendEffect "
                + "(vtable+0xD8, cx=0x2905). No text SysMsg."),

            // ---- config / script / file reloads -------------------------------
            new NativeSystemAdminCommand("ReloadSkyPrize", 121, 4, 0x0062526D, "sub_6C291C",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "重载天关奖励配置文件",
                "sub_6C291C reloads the 天关 (sky-gate) prize config. Helper handles feedback; case sends none inline."),
            new NativeSystemAdminCommand("ReLoadGmFile", 206, 5, 0x00625DA4, "sub_6554FC/sub_713890",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "重载GM列表 AdminList.txt",
                "sub_6554FC reloads the GM/admin list (AdminList.txt), sub_713890 rebroadcasts it, then a GM SysMsg "
                + "(0xFFDB) confirms."),
            new NativeSystemAdminCommand("ReLoadTask", 234, 4, 0x00625DEF, "sub_6DF540",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "重载任务脚本 (@LogonQuest)",
                "sub_6DF540 reloads the quest/task scripts. Helper handles feedback; case sends none inline."),
            new NativeSystemAdminCommand("reloadTaskDispatch", 459, 4, 0x00628C39, "sub_6997BC",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "重载任务发布脚本",
                "sub_6997BC reloads the task-dispatch script; a GM SysMsg (0xFFDB) confirms."),

            // ---- quest-field get/set (single-branch delegations) --------------
            new NativeSystemAdminCommand("GetV", 235, 3, 0x00625DFC, "sub_6CD574",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "获取指定角色、指定任务号、字段号对应的值",
                "sub_6CD574 looks up a target's quest-field value and reports it. The value report is emitted by the "
                + "helper, not by an inline SysMsg in the case."),
            new NativeSystemAdminCommand("SetV", 237, 5, 0x00625E2A, "sub_6CD85C",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "设置玩家某个任务中指定字段的值",
                "sub_6CD85C writes a target's quest-field value. Helper handles feedback; case sends none inline."),

            // ---- branch-bearing verbs (own evaluators below) ------------------
            new NativeSystemAdminCommand("Rest", 27, 0, 0x00623A42, "(inline)",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "设置宠物休息或攻击？",
                "Only acts when the GM commands slaves (obj+0x4FC slave-count>0) or a hero (obj+0x18A8, a field with no "
                + "non-zero writer in the image so that disjunct is dead): if the current map forbids it ([map+5]!=0, "
                + "the DARE flag) refuse with a GM SysMsg (0xFFDB); otherwise toggle the rest flag "
                + "(0x623A73 80 B0 C7 04 00 00 01 xor byte [obj+0x4C7],1) and report on/off (0xFFDB). "
                + "No pets/hero => silent fall-through."),
            new NativeSystemAdminCommand("Reshuawolong", 156, 4, 0x00625846, "sub_606960",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "重载卧龙任务配置文件 卧龙山庄.ini",
                "Only when the 卧龙 (WoLong) feature is active (off_7D6724 chain non-null): sub_606960 attempts the reload; "
                + "on its true result a GM SysMsg (0xFFDB) fires, on false a usage SysMsg (0x38FF). Feature inactive => "
                + "silent fall-through."),
            new NativeSystemAdminCommand("ServerInfo", 194, 3, 0x00625B08, "(subcommand dispatch)",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "查询服务器信息 [Event/Npc/Monster/DynMap/VisibleCache]",
                "Six-way read-only dispatch on the sub-token: Event->sub_718894, Npc->sub_64BB40, Monster->sub_67D9E4, "
                + "DynMap->sub_5FCBDC, VisibleCache->inline; each reports via GM SysMsg (0xFFDB). An unrecognised token "
                + "gets a usage SysMsg (0x38FF)."),
            new NativeSystemAdminCommand("getg", 282, 4, 0x00626D21, "sub_699198",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "获取指定的ID和index的脚本值(从mirparams中查询)",
                "Parses [paramId] [index] (StrToIntDef default -1). Both must be != -1; then sub_699198(index,paramId) "
                + "reads the mirparam value and a GM SysMsg (0xFFDB) reports it. A missing/invalid arg => silent fall-through."),
            new NativeSystemAdminCommand("setg", 283, 4, 0x00626DC0, "sub_699310",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "设置指定的ID和index的脚本值(存到mirparams中)",
                "Parses [paramId] [index] (default -1) and [value] (default -2). Requires paramId!=-1, index!=-1, "
                + "value!=-2 and sub_699310(value) validating; then stores it and a GM SysMsg (0xFFDB) confirms. Any "
                + "guard miss => silent fall-through."),
            new NativeSystemAdminCommand("LoadValidFunc", 350, 4, 0x006278A2, "sub_7900FC",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "重载脚本安全函数列表validScriptFunc.txt",
                "sub_7900FC reloads validScriptFunc.txt and returns a count; on a negative result a usage SysMsg (0x38FF) "
                + "reports failure, otherwise a GM SysMsg (0xFFDB) reports the loaded count."),
            new NativeSystemAdminCommand("LogSwitch", 482, 4, 0x00628CFD, "sub_403B2C/sub_79501C",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgUsage,
                "查询日志开关、打开/关闭日志开关 (参数为空的时候就是查询)",
                "Multiplexer over a 13-entry log-switch table (off_7D6BAC) backed by a bitmask (off_7D5FBC): '_all_' lists "
                + "every switch's state (usage SysMsg 0x38FF); a known switch name + open/close sets/clears its bit and "
                + "reports (0xFFDB), persisting via sub_79501C; an unknown switch name falls through silently."),
            new NativeSystemAdminCommand("LogQueueSwitch", 578, 4, 0x00624027, "sub_7130E8",
                NativeSystemAdminOutcome.Executed, NoSysMsg,
                "排队系统开关 @LogQueueSwitch 0/1",
                "When the arg is one of the two accepted literals (0/1): parses it and broadcasts the queue-system switch "
                + "via sub_7130E8((byte)obj+0x418, ...). No inline SysMsg. Any other arg => silent fall-through."),
            new NativeSystemAdminCommand("SetAP", 723, 2, 0x0062A985, "sub_6F9220",
                NativeSystemAdminOutcome.ExecutedWithGmMessage, SysMsgGmReply,
                "",
                "Gated by two server-config enables (v548[23] feature, v548[22] mode) and target lookup: only when both "
                + "enables are set AND the named target exists does sub_6F9220 apply the AP change and a GM SysMsg (0xFFDB) "
                + "report it. Feature off / mode off => empty usage SysMsg (0x38FF); target missing => not-found SysMsg (0x38FF)."),

            // ---- registered but SILENT NO-OP in this build --------------------
            // def_622B15 (0x0062B648) sink:
            new NativeSystemAdminCommand("ReloadQuest", 150, 4, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "重载锦囊问题配置文件Question.txt以及奖励配置文件AskPrize.ini",
                "Registered (index 150, perm 4, help present) but the jump slot is def_622B15: no case body ships. Silent no-op (handled=0)."),
            new NativeSystemAdminCommand("SetAllGM", 335, 5, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "设置所有登录为GM权限",
                "Registered (index 335, perm 5) but mapped to def_622B15: promoting all logins to GM is unimplemented here. Silent no-op."),
            new NativeSystemAdminCommand("ScriptTest", 337, 5, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "设置/取消脚本测试模式",
                "Registered (index 337, perm 5) but mapped to def_622B15: script-test mode toggle is unimplemented here. Silent no-op."),
            new NativeSystemAdminCommand("chgMenPaiName", 370, 5, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "GM为指定名字的门派更改名字",
                "Registered (index 370, perm 5) but mapped to def_622B15: 门派 (sect) rename is unimplemented here. Silent no-op."),
            new NativeSystemAdminCommand("setMenPaiPopularity", 371, 5, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "GM设置指定的门派的人气值",
                "Registered (index 371, perm 5) but mapped to def_622B15: setting a 门派 popularity value is unimplemented here. Silent no-op."),
            new NativeSystemAdminCommand("ReloadTBBConfig", 380, 4, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "重载淘宝宝藏奖励配置 淘宝宝藏奖励配置.ini",
                "Registered (index 380, perm 4) but mapped to def_622B15: reloading the 淘宝宝藏 reward config is unimplemented here. Silent no-op."),
            new NativeSystemAdminCommand("FileOperate", 483, 5, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "文件上传下载操作 [up/down/exists/del] [BaseDir/GuildDir/VentureDir/ConLogDir/CastleDir/EnvirDir/MapDir]",
                "Registered (index 483, perm 5) but mapped to def_622B15: the file up/down/exists/del transfer op is unimplemented here. Silent no-op."),
            new NativeSystemAdminCommand("lookzhenqi", 550, 4, NativeSystemAdminCommand.DefaultHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "查询真气值 @lookzhenqi 0/1",
                "Registered (index 550, perm 4) but mapped to def_622B15: the 真气 (zhenqi) query is unimplemented here. Silent no-op."),
            // loc_62B64C (0x0062B64C) empty-case sink:
            new NativeSystemAdminCommand("reloadrabbit", 532, 4, NativeSystemAdminCommand.EmptyBodyHandler, "",
                NativeSystemAdminOutcome.SilentNoOp, NoSysMsg,
                "加载大展宏图包配置文件 @reloadrabbit",
                "Registered (index 532, perm 4) but the jump slot points at the shared empty-case exit loc_62B64C: an "
                + "empty body (handled stays 1), no effect, no message. Silent no-op."),
        };

        private static readonly Dictionary<string, NativeSystemAdminCommand> _byName =
            BuildIndex(_all);

        private static Dictionary<string, NativeSystemAdminCommand> BuildIndex(
            NativeSystemAdminCommand[] cmds)
        {
            var map = new Dictionary<string, NativeSystemAdminCommand>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in cmds)
                map[c.Name] = c;
            return map;
        }

        /// <summary>All modeled system/server GM command records.</summary>
        public static IReadOnlyList<NativeSystemAdminCommand> All => _all;

        /// <summary>Look up a modeled record by GM token (case-insensitive).</summary>
        public static NativeSystemAdminCommand Find(string name)
        {
            if (name != null && _byName.TryGetValue(name, out var c))
                return c;
            return null;
        }

        /// <summary>
        /// Dormant-evaluate "@name args" exactly as sub_622820 would, returning the
        /// observable outcome (branch, native core, inline SysMsg ident).
        /// </summary>
        public static NativeSystemAdminEvaluation Evaluate(string name, int callerPerm,
            IReadOnlyList<string> args)
        {
            var rec = Find(name);
            if (rec == null)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.UnknownCommand,
                    "", "", NoSysMsg, "token is not in the system/server family");

            // sub_621F28 returns 0 when the caller is under-privileged -> def_622B15.
            if (callerPerm < rec.RequiredPerm)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.PermissionRejected,
                    "", "", NoSysMsg,
                    "callerPerm " + callerPerm + " < requiredPerm " + rec.RequiredPerm
                    + "; sub_621F28 returns index 0 -> def_622B15 (silent, handled=0)");

            // Handler is one of the two shared no-op sinks.
            if (!rec.Implemented)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.SilentNoOp,
                    "", "", NoSysMsg,
                    "handler == " + (rec.HandlerAddress == NativeSystemAdminCommand.DefaultHandler
                        ? "def_622B15 (0x0062B648, handled=0)"
                        : "loc_62B64C (0x0062B64C, empty case, handled=1)")
                    + ": registered but no case body; no effect, no message");

            var a = args ?? System.Array.Empty<string>();
            switch (rec.Name)
            {
                case "Rest": return EvalRest(rec);
                case "Reshuawolong": return EvalReshuawolong(rec);
                case "ServerInfo": return EvalServerInfo(rec, a);
                case "getg": return EvalGetg(rec, a);
                case "setg": return EvalSetg(rec, a);
                case "LoadValidFunc": return EvalLoadValidFunc(rec);
                case "LogSwitch": return EvalLogSwitch(rec, a);
                case "LogQueueSwitch": return EvalLogQueueSwitch(rec, a);
                case "SetAP": return EvalSetAP(rec, a);
                default:
                    // Single-path commands: outcome + SysMsg ident come from the record.
                    return new NativeSystemAdminEvaluation(rec.BaseOutcome, "default",
                        rec.NativeCore, rec.BaseSysMsgIdent, rec.EffectSummary);
            }
        }

        // --- Rest (case 27) -------------------------------------------------
        private static NativeSystemAdminEvaluation EvalRest(NativeSystemAdminCommand rec)
        {
            if (!RestHasTargets)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedSilently,
                    "no-targets", "(inline)", NoSysMsg,
                    "GM commands no slaves (obj+0x4FC==0) and no hero (obj+0x18A8==0): outer guard false, silent fall-through");
            if (RestBlockedHere)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedWithGmMessage,
                    "map-blocked", "(inline)", SysMsgGmReply,
                    "current map forbids rest ([map+5]!=0): refuse with GM SysMsg (0xFFDB), flag unchanged");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                "toggle", "(inline)", SysMsgGmReply,
                "toggle rest flag (obj+0x1324 ^= 1) and report on/off via GM SysMsg (0xFFDB)");
        }

        // --- Reshuawolong (case 156) ----------------------------------------
        private static NativeSystemAdminEvaluation EvalReshuawolong(NativeSystemAdminCommand rec)
        {
            if (!WolongActive)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedSilently,
                    "inactive", "", NoSysMsg,
                    "卧龙 feature not active (off_7D6724 chain null): outer guard false, silent fall-through");
            if (WolongReloadOk)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                    "reload-ok", "sub_606960", SysMsgGmReply,
                    "sub_606960 returned true: GM SysMsg (0xFFDB)");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedWithGmMessage,
                "reload-alt", "sub_606960", SysMsgUsage,
                "sub_606960 returned false: usage SysMsg (0x38FF)");
        }

        // --- ServerInfo (case 194) ------------------------------------------
        private static NativeSystemAdminEvaluation EvalServerInfo(
            NativeSystemAdminCommand rec, IReadOnlyList<string> a)
        {
            var sub = Arg(a, 0);
            if (EqI(sub, "Event"))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                    "event", "sub_718894", SysMsgGmReply, "event-queue report (0xFFDB)");
            if (EqI(sub, "Npc"))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                    "npc", "sub_64BB40", SysMsgGmReply, "NPC report (0xFFDB)");
            if (EqI(sub, "Monster"))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                    "monster", "sub_67D9E4", SysMsgGmReply, "monster report (0xFFDB)");
            if (EqI(sub, "DynMap"))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                    "dynmap", "sub_5FCBDC", SysMsgGmReply, "dynamic-map report (0xFFDB)");
            if (EqI(sub, "VisibleCache"))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                    "visiblecache", "(inline)", SysMsgGmReply, "visible-cache report (0xFFDB)");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedWithGmMessage,
                "unknown-sub", "", SysMsgUsage,
                "sub-token matched none of Event/Npc/Monster/DynMap/VisibleCache: usage SysMsg (0x38FF)");
        }

        // --- getg (case 282) ------------------------------------------------
        private static NativeSystemAdminEvaluation EvalGetg(
            NativeSystemAdminCommand rec, IReadOnlyList<string> a)
        {
            int paramId = ParseIntDef(Arg(a, 0), -1);
            int index = ParseIntDef(Arg(a, 1), -1);
            if (paramId == -1 || index == -1)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedSilently,
                    "bad-args", "", NoSysMsg,
                    "paramId or index parsed to -1 (absent/non-numeric): silent fall-through");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                "read", "sub_699198", SysMsgGmReply,
                "sub_699198(index,paramId) reads the mirparam value; GM SysMsg (0xFFDB) reports it");
        }

        // --- setg (case 283) ------------------------------------------------
        private static NativeSystemAdminEvaluation EvalSetg(
            NativeSystemAdminCommand rec, IReadOnlyList<string> a)
        {
            int paramId = ParseIntDef(Arg(a, 0), -1);
            int index = ParseIntDef(Arg(a, 1), -1);
            int value = ParseIntDef(Arg(a, 2), -2);
            if (paramId == -1 || index == -1 || value == -2 || !ScriptParamValueValid(value))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedSilently,
                    "bad-args", "sub_699310", NoSysMsg,
                    "a guard failed (paramId==-1 | index==-1 | value==-2 | sub_699310 rejected value): silent fall-through");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                "write", "sub_699310", SysMsgGmReply,
                "store mirparam[paramId][index]=value; GM SysMsg (0xFFDB) confirms");
        }

        // --- LoadValidFunc (case 350) ---------------------------------------
        private static NativeSystemAdminEvaluation EvalLoadValidFunc(NativeSystemAdminCommand rec)
        {
            if (!ValidFuncReloadOk)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedWithGmMessage,
                    "reload-fail", "sub_7900FC", SysMsgUsage,
                    "sub_7900FC returned < 0 (reload failed): usage SysMsg (0x38FF)");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                "reload-ok", "sub_7900FC", SysMsgGmReply,
                "sub_7900FC returned >= 0: GM SysMsg (0xFFDB) reports the loaded count");
        }

        // --- LogSwitch (case 482) -------------------------------------------
        private static NativeSystemAdminEvaluation EvalLogSwitch(
            NativeSystemAdminCommand rec, IReadOnlyList<string> a)
        {
            var target = Arg(a, 0);
            if (EqI(target, "_all_"))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                    "all", "(inline)", SysMsgUsage,
                    "iterate all 13 switches (off_7D6BAC) and list their on/off state via usage SysMsg (0x38FF)");
            if (LogSwitchExists(target))
            {
                var op = Lower(Arg(a, 1));
                if (op == "open" || op == "close")
                    return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                        op, "sub_79501C", SysMsgGmReply,
                        "set/clear the switch bit (off_7D5FBC), report via GM SysMsg (0xFFDB), persist via sub_79501C");
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedSilently,
                    "no-op-verb", "", NoSysMsg,
                    "switch found but op is neither open nor close: silent fall-through");
            }
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedSilently,
                "unknown-switch", "", NoSysMsg,
                "arg0 is neither '_all_' nor a known switch name: silent fall-through");
        }

        // --- LogQueueSwitch (case 578) --------------------------------------
        private static NativeSystemAdminEvaluation EvalLogQueueSwitch(
            NativeSystemAdminCommand rec, IReadOnlyList<string> a)
        {
            var arg = Arg(a, 0);
            if (arg == "0" || arg == "1")
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.Executed,
                    "set", "sub_7130E8", NoSysMsg,
                    "arg is an accepted literal (0/1): broadcast the queue switch via sub_7130E8; no inline SysMsg");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedSilently,
                "unmatched", "", NoSysMsg,
                "arg matched neither accepted literal: silent fall-through");
        }

        // --- SetAP (case 723) -----------------------------------------------
        private static NativeSystemAdminEvaluation EvalSetAP(
            NativeSystemAdminCommand rec, IReadOnlyList<string> a)
        {
            if (!SetApFeatureEnabled)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedWithGmMessage,
                    "feature-off", "", SysMsgUsage,
                    "feature enable (v548[23]) clear: jump to LABEL_976 -> empty usage SysMsg (0x38FF)");
            var target = Arg(a, 0);
            if (!SetApTargetExists(target))
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedWithGmMessage,
                    "target-missing", "", SysMsgUsage,
                    "named target not found: not-found usage SysMsg (0x38FF)");
            if (!SetApModeEnabled)
                return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.RejectedWithGmMessage,
                    "mode-off", "", SysMsgUsage,
                    "mode enable (v548[22]) clear: LABEL_976 -> empty usage SysMsg (0x38FF)");
            return new NativeSystemAdminEvaluation(NativeSystemAdminOutcome.ExecutedWithGmMessage,
                "apply", "sub_6F9220", SysMsgGmReply,
                "both enables set and target present: sub_6F9220 applies the AP change; GM SysMsg (0xFFDB) reports it");
        }

        // -----------------------------------------------------------------------------
        // Dormant world-state hooks. The live port replaces these with real lookups; the
        // model keeps them injectable so the audit can exercise each branch deterministically
        // without a running server. They default to the fail-closed world (no targets /
        // feature off / reload succeeds), so an unconfigured call lands on its safe branch.
        // -----------------------------------------------------------------------------

        /// <summary>Rest: GM commands slaves (obj+0x4FC&gt;0) or a hero (obj+0x18A8) — the outer guard.</summary>
        public static bool RestHasTargets { get; set; }

        /// <summary>Rest: current map forbids resting ([map+5]!=0).</summary>
        public static bool RestBlockedHere { get; set; }

        /// <summary>Reshuawolong: the 卧龙 feature is active (off_7D6724 chain non-null).</summary>
        public static bool WolongActive { get; set; }

        /// <summary>Reshuawolong: sub_606960() result (true => reply branch). Defaults true.</summary>
        public static bool WolongReloadOk { get; set; } = true;

        /// <summary>LoadValidFunc: sub_7900FC() >= 0 (reload succeeded). Defaults true.</summary>
        public static bool ValidFuncReloadOk { get; set; } = true;

        /// <summary>SetAP: feature enable (v548[23]).</summary>
        public static bool SetApFeatureEnabled { get; set; }

        /// <summary>SetAP: mode enable (v548[22]).</summary>
        public static bool SetApModeEnabled { get; set; }

        /// <summary>SetAP: injected target-existence oracle (name -> exists). Defaults present.</summary>
        public static System.Func<string, bool> SetApTargetExistsHook { get; set; }

        /// <summary>LogSwitch: injected switch-name oracle (name -> known). Defaults unknown.</summary>
        public static System.Func<string, bool> LogSwitchExistsHook { get; set; }

        /// <summary>setg: injected sub_699310 value validator. Defaults accept-all.</summary>
        public static System.Func<int, bool> ScriptParamValueValidHook { get; set; }

        private static bool SetApTargetExists(string name)
            => SetApTargetExistsHook == null || SetApTargetExistsHook(name);

        private static bool LogSwitchExists(string name)
            => LogSwitchExistsHook != null && LogSwitchExistsHook(name);

        private static bool ScriptParamValueValid(int value)
            => ScriptParamValueValidHook == null || ScriptParamValueValidHook(value);

        // --- tiny helpers ----------------------------------------------------
        private static string Arg(IReadOnlyList<string> a, int i)
            => (a != null && i < a.Count && a[i] != null) ? a[i] : "";

        private static string Lower(string s)
            => s == null ? "" : s.ToLowerInvariant();

        private static bool EqI(string a, string b)
            => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        private static int ParseIntDef(string s, int def)
            => int.TryParse(s, out var v) ? v : def;
    }
}
