// -----------------------------------------------------------------------------
// NativeGmMonsterMapCommands.cs
//
// DORMANT / FAIL-CLOSED reference model of the M2Server "战神" (God-of-War)
// MONSTER / MAP / NPC GM ("@") command family — the subset of the single GM
// dispatcher sub_622820 @0x00622820 that spawns/queries monsters, reloads
// monster/NPC config, moves the GM around maps, and mutates per-map / global map
// runtime parameters (through-range, fountain switch, crit levels, spider-web
// timings, no-kill level cap, map free cells, etc.).
//
// This EXTENDS the world-admin family (NativeGmWorldAdminCommands.cs) but is a
// DISTINCT bucket: the 12 world commands that summon/clear monsters, reload NPCs,
// mutate map state/drops, kick/drag OTHER players and set/lock the world clock
// (KickOut/CallMan/Shuag/CallMob/MonClear/ReShuaNpc/SetSysTime/MapDropItem/
// LockTimeChg/CreateCampMon/SetMapState/kickOutBlackRoom) are modeled THERE and
// are NOT re-modeled here. This file covers the OTHER 27 monster/map/npc records.
//
// Most entries are not wired into the live command pipeline. This type
// *describes* the exact original contract so AuditTools/
// NativeGmMonsterMapCommandsCheck can pin it against the binary, and so a future
// port can reproduce each branch/effect/SysMsg precisely.
//
// Source of truth
//   Binary : M2Server_unpacked_fixed.exe (战神), image base 0x00400000,
//            SHA256 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e
//   IDA db : staging/update_clothes_4637_ida_work/m2full.i64
//   Dumps  : disp_decomp.txt (Hex-Rays of sub_622820; case N == dispatchIndex),
//            big622820.txt (raw disassembly, addresses inline),
//            world_scan_out.txt / world_scan_lo_out.txt (decoded command records),
//            all_strings.txt.
//
// Dispatch contract (reused verbatim from the world-admin / item peers)
//   * The GM string "@name p0:p1,p2 ..." is split; the first token selects a
//     static command record (name ShortString; +0x18 = dispatchIndex,
//     +0x1C = requiredPerm; GBK help follows).
//   * esi = sub_621F28(player, name, callerPerm, &reqPerm) @0x00621F28 returns
//     the record's dispatchIndex ONLY when callerPerm >= reqPerm, else 0.
//   * cmp esi,0x2EE(750); ja default; jmp jpt_622B15[esi*4]  (table @0x00622B1C,
//     752 slots). jpt_622B15[0] = def_622B15 @0x0062B648 = the shared SILENT
//     no-op (sets the handled flag var_D=0, returns, no effect, no message).
//   * Handler = *(uint*)(0x00622B1C + dispatchIndex*4). A handler that equals
//     def_622B15 (0x0062B648) is a REGISTERED-BUT-UNIMPLEMENTED silent no-op.
//     (This family's 4 no-ops — ReloadLinkEx 239, sendProc 338, CellInfo 474,
//     reloadbossmon 521 — all land on 0x0062B648, i.e. the "default" sink, NOT
//     the separate 0x0062B64C empty-body sink used by other indices.)
//
// Inline GM feedback is `mov cx,IDENT; mov edx,offset gbk; call [self+0xD4]`
// (SysMsg). THREE idents appear in this family (Hex-Rays shows them as the signed
// LOWORD assigned just before the +212==0xD4 vtable call):
//   0xFFDB (Hex-Rays -37)   ordinary GM reply / report
//   0x38FF (Hex-Rays 14591) usage / refusal / error notice
//   0xFCFF (Hex-Rays -769)  parameter-set notice (SpiderWebTest, TempSetMapParam)
//
// Str_ToInt-with-default helper sub_40CA18(str, default): returns parsed int, or
// the default when the string is empty/non-numeric (default is -1 in the AutoMove
// coordinate parse, 0 elsewhere unless noted).
//
// KEY FINDING (same as the ITEM/EQUIP peer): the delegating case blocks are THIN
// SHIMS — they marshal parsed params + self and tail-call a core subroutine
// (sub_6CE400/sub_6BE4D0/…). Those core bodies are NOT present in the dumps, so
// per the fail-closed rule they are abstracted as inputs (CoreBodyDeferred=true).
// This model captures only what the SHIM proves: which branch fires, any inline
// write the shim itself performs, and which SysMsg (if any) the shim emits. It
// does NOT invent core-internal ladders. Where the shim performs the write inline
// (ThroughRange, SetFountSwitch, SpiderWebTest) the effect IS fully modeled
// (CoreBodyDeferred=false). SetNoKillMapLv's core has since been recovered too.
//
// Inline global/data writes proven by the shims (absolute addresses, reliable
// even though the decompiler flagged "bad sp value" for stack locals):
//   ThroughRange   *off_7D6970[0] = value        (安全区穿人范围, 0..50)
//   SetFountSwitch *(BYTE*)off_7D6EC8 = 1|0       (GM 可控泉水开关)
//   SpiderWebTest  *(WORD*)off_7D6364 = value     (lasttime 持续时间)
//                  *(WORD*)off_7D6D14 = value     (codetime 冷却时间)
//                  *(WORD*)off_7D6F54 = value     (effect 效果标志)
//   BreakLvCtrl    *(BYTE*)(map+0xB8) / *(WORD*)(map+0xBA)  per-map crit;
//                  off_7D6830[2] / off_7D6830[3]            global crit
//
// C# STUB DRIFT (live GameSvr/Command/Commands, NOT this model — flagged for the
// port; see the audit's drift section and gm_monster_map_commands_20260731.md):
//   * Every live *Command.cs in this family declares RequiredPermission 10; the
//     native records use 0/3/4/5 (see the registry perms below and the per-command
//     drift table in staging/gm_monster_map_commands_20260731.md).
//   * CellInfoCommand.cs (idx 474) is LIVE and does real work (GetMapCellInfo + SysMsg
//     report) even though its native record is a SILENT NO-OP — the live port sends
//     MORE than the original. ReloadBossMonCommand.cs (idx 521), by contrast, is a
//     fail-closed stub (NativeCommandFailure.Report, no reload) — it MATCHES the native
//     no-op and is NOT over-impl.
//   * TempSetMapParamCommand.cs correctly uses perm 5 and mirrors the native
//     status codes Success=1 / UnsupportedAttribute=100 (== sub_774D24 returns).
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>Observable outcome class of one monster/map/npc GM invocation.</summary>
    public enum NativeMonsterMapOutcome
    {
        /// <summary>Token is not a member of the monster/map/npc family modeled here.</summary>
        UnknownCommand,

        /// <summary>
        /// callerPerm &lt; requiredPerm: sub_621F28 returns index 0, the switch
        /// lands on def_622B15, handled=0. Indistinguishable from an unknown
        /// command — no effect, no message.
        /// </summary>
        PermissionRejected,

        /// <summary>
        /// Handler == def_622B15 (0x0062B648): the command IS registered (name,
        /// index, perm, help all present) but this build ships no case body.
        /// var_D=0, no effect, no message.
        /// </summary>
        SilentNoOp,

        /// <summary>Native helper invoked / state mutated; no inline GM message from the shim.</summary>
        Executed,

        /// <summary>As <see cref="Executed"/>, plus an inline confirmation/report SysMsg.</summary>
        ExecutedWithGmMessage,

        /// <summary>
        /// A native guard refused the action with NO message (required arg absent,
        /// value out of range, coords == -1). The shim falls straight to the exit.
        /// </summary>
        RejectedSilently,

        /// <summary>A native guard refused the action AND sent the GM an inline SysMsg.</summary>
        RejectedWithGmMessage
    }

    /// <summary>
    /// One static GM command record as it exists in the binary's command table,
    /// restricted to the monster / map / npc family.
    /// </summary>
    public sealed class NativeMonsterMapCommand
    {
        public const uint JumpTableBase = 0x00622B1C; // jpt_622B15
        public const uint DefaultHandler = 0x0062B648; // def_622B15 (silent no-op)

        public NativeMonsterMapCommand(string name, int dispatchIndex,
            int requiredPerm, uint handlerAddress, string nativeCore,
            bool coreBodyDeferred, string helpGbk, string effectSummary)
        {
            Name = name;
            DispatchIndex = dispatchIndex;
            RequiredPerm = requiredPerm;
            HandlerAddress = handlerAddress;
            NativeCore = nativeCore ?? "";
            CoreBodyDeferred = coreBodyDeferred;
            HelpGbk = helpGbk ?? "";
            EffectSummary = effectSummary ?? "";
        }

        public string Name { get; }
        public int DispatchIndex { get; }
        public int RequiredPerm { get; }

        /// <summary>Handler address = *(JumpTableBase + DispatchIndex*4).</summary>
        public uint HandlerAddress { get; }

        /// <summary>Jump-table slot that stores <see cref="HandlerAddress"/>.</summary>
        public uint JumpSlotAddress => JumpTableBase + (uint)DispatchIndex * 4;

        /// <summary>False iff the handler is the shared silent-no-op default.</summary>
        public bool Implemented => HandlerAddress != DefaultHandler;

        /// <summary>Primary core subroutine the shim delegates to ("" / "(inline)" when none).</summary>
        public string NativeCore { get; }

        /// <summary>True when the core body is not present in the dumps and is abstracted as an input.</summary>
        public bool CoreBodyDeferred { get; }

        /// <summary>GBK help string carried by the record.</summary>
        public string HelpGbk { get; }

        /// <summary>Prose description of the effect / branch ladder (for porters).</summary>
        public string EffectSummary { get; }
    }

    /// <summary>Result of dormant-evaluating one invocation against the model.</summary>
    public sealed class NativeMonsterMapEvaluation
    {
        public NativeMonsterMapEvaluation(NativeMonsterMapOutcome outcome,
            string branch, string nativeCore, int nativeSysMsgIdent,
            bool coreBodyDeferred, string detail)
        {
            Outcome = outcome;
            Branch = branch ?? "";
            NativeCore = nativeCore ?? "";
            NativeSysMsgIdent = nativeSysMsgIdent;
            CoreBodyDeferred = coreBodyDeferred;
            Detail = detail ?? "";
        }

        public NativeMonsterMapOutcome Outcome { get; }

        /// <summary>Which internal branch fired (e.g. "open", "found", "value-le-50").</summary>
        public string Branch { get; }

        /// <summary>Core sub_XXXX invoked on this branch ("" / "(inline)" if none).</summary>
        public string NativeCore { get; }

        /// <summary>cx word passed to SysMsg (self+0xD4); -1 when no message.</summary>
        public int NativeSysMsgIdent { get; }

        /// <summary>True when the branch's real work lives in a core body absent from the dumps.</summary>
        public bool CoreBodyDeferred { get; }

        public string Detail { get; }
    }

    /// <summary>
    /// Fail-closed model of the monster / map / npc GM command family. Pure data +
    /// <see cref="Evaluate"/>; performs no I/O and mutates nothing. The registered
    /// no-ops reuse <see cref="NativeGmDefaultNoOp"/> (defined in
    /// NativeGmSkillEquipCommands.cs) via <see cref="EvaluateUnimplemented"/>.
    /// </summary>
    public static class NativeGmMonsterMapCommands
    {
        // dispatcher constants (identical single switch as the world-admin / item families)
        public const uint DispatcherEa = 0x00622820;   // sub_622820
        public const uint IndexLookupEa = 0x00621F28;  // sub_621F28 (permission-gated index)
        public const uint JumpTableEa = 0x00622B1C;    // jpt_622B15
        public const int SwitchMaxIndex = 750;         // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;  // def_622B15 (silent no-op)

        // SysMsg call + idents (cx word handed to self+0xD4)
        public const int SysMsgVtableOffset = 0xD4;    // call dword ptr [self]+0xD4 (ident, text)
        public const int SysMsgGmReply = 0xFFDB;       // ordinary GM feedback / report   (Hex-Rays -37)
        public const int SysMsgUsage = 0x38FF;         // usage / refusal / error notice  (Hex-Rays 14591)
        public const int SysMsgNotice = 0xFCFF;        // parameter-set notice            (Hex-Rays -769)
        public const int NoSysMsg = -1;

        // parse helper + inline global/data addresses proven by the shims
        public const uint StrToIntWithDefaultEa = 0x0040CA18; // sub_40CA18(str, default)
        public const uint ThroughRangeGlobalEa = 0x007D6970;  // off_7D6970 -> *[0] = through range
        public const int ThroughRangeMax = 0x32;              // 0 <= n <= 50 accepted
        public const uint FountSwitchGlobalEa = 0x007D6EC8;   // *(BYTE*)off_7D6EC8 = 1|0
        public const uint SpiderLastTimeGlobalEa = 0x007D6364; // *(WORD*)off_7D6364
        public const uint SpiderCodeTimeGlobalEa = 0x007D6D14; // *(WORD*)off_7D6D14
        public const uint SpiderEffectGlobalEa = 0x007D6F54;   // *(WORD*)off_7D6F54
        public const uint CritGlobalEa = 0x007D6830;           // off_7D6830[2]/[3] global crit
        public const int MapCritByteOffset = 0xB8;             // *(BYTE*)(map+184)
        public const int MapCritWordOffset = 0xBA;             // *(WORD*)(map+186)

        // AutoMove coordinate sentinel + TempSetMapParam status codes (sub_774D24 return)
        public const int AutoMoveInvalidCoord = -1;            // sub_40CA18(...,-1); skip when either == -1
        public const int TempSetMapParamSuccess = 1;           // n100 == 1
        public const int TempSetMapParamUnsupported = 100;     // n100 == 100

        // core subroutines invoked by the shims — bodies NOT in the dumps (CoreBodyDeferred)
        public const uint GowgoCoreEa = 0x006CE400;        // sub_6CE400 (gowgo move)
        public const uint DingdianCoreEa = 0x006BE4D0;     // sub_6BE4D0 (dingdianyidong fixed move)
        public const uint MonXinxiCoreEa = 0x006BEC4C;     // sub_6BEC4C (nearby monster info -> GM)
        public const uint HumNumCoreEa = 0x006BED0C;       // sub_6BED0C (player count for map)
        public const uint MapIdxCoreEa = 0x006DEF10;       // sub_6DEF10 (current-map index for @MAP)
        public const uint MonNumberCoreEa = 0x00779DDC;    // sub_779DDC (monster count for map)
        public const uint ReloadMonAttCoreEa = 0x0067D484; // sub_67D484 (reload TMonSupport)
        public const uint ReloadNpcPrizeCoreEa = 0x0074EBB4; // sub_74EBB4 (reload NormalPrize.ini -> bool)
        public const uint RangeShuagCoreEa = 0x0062E58C;   // sub_62E58C (range spawn monsters)
        public const uint NpcHitCoreEa = 0x0062EA7C;       // sub_62EA7C (nearby NPC animation)
        public const uint AutoMoveCoreEa = 0x006D3024;     // sub_6D3024 (auto-move to map/x/y)
        public const uint LockInPlayersCoreEa = 0x006CDD48; // sub_6CDD48 (mass teleport to Gow002)
        public const uint GetMapEa = 0x00696228;           // sub_696228 (GetMap; BreakLvCtrl set-map)
        public const uint GetMap2Ea = 0x006962D0;          // sub_6962D0 (GetMap; MonNumber/TempSetMapParam)
        public const uint SetRecoverFactorCoreEa = 0x0062ECE0; // sub_62ECE0 (safe-zone hp/mp recover)
        public const uint SetNoKillMapLvCoreEa = 0x006CDBBC; // sub_6CDBBC (no-kill map level cap)
        public const uint MapCellFreeCoreEa = 0x0077BEB4;  // sub_77BEB4 (free every cell in ownmap)
        public const uint ReshuaMonScriptCoreEa = 0x0067DC40; // sub_67DC40 (reload monster script)
        public const uint LoadMonGenCoreEa = 0x0067B35C;   // sub_67B35C (reload MonGen config)
        public const uint LoadMonFindEa = 0x00679954;      // sub_679954 (find monster by name -> index)
        public const uint ReloadMonitemsTreeCoreEa = 0x0067AEC0; // sub_67AEC0 (reload MonItemsTree.txt)
        public const uint TempSetMapParamCoreEa = 0x00774D24; // sub_774D24 (apply map param -> status)

        private static readonly NativeMonsterMapCommand[] _all =
        {
            // ---- movement / teleport of SELF ----------------------------------
            new NativeMonsterMapCommand("gowgo", 29, 0, 0x00623B37u, "sub_6CE400", true,
                "移动(GMLevel >= 2)，同一地图可以不指定地图名	@gowgo [地图名/无] X坐标 Y坐标",
                "Pure delegation: sub_6CE400(self, mapNameOrCoords). No inline SysMsg; the core performs the move."),
            new NativeMonsterMapCommand("dingdianyidong", 51, 3, 0x00623BA3u, "sub_6BE4D0", true,
                "定点移动	@dingdianyidong 地图名 X坐标 Y坐标",
                "Pure delegation: sub_6BE4D0(argTail). No inline SysMsg; core teleports the GM to map/x/y."),
            new NativeMonsterMapCommand("AutoMove", 233, 5, 0x006262CFu, "sub_6D3024", true,
                "GM自身自动移动到某个地图的某个点	@AutoMove 地图名 X坐标 Y坐标",
                "x=StrToInt(argX,-1); y=StrToInt(argY,-1). Only when BOTH != -1 -> sub_6D3024(...) auto-move; "
                + "if either coord is -1 (missing/non-numeric) the shim exits silently. No inline SysMsg."),

            // ---- monster / map queries (report to GM) -------------------------
            new NativeMonsterMapCommand("MonXinxi", 53, 3, 0x00624098u, "sub_6BEC4C", true,
                "查看附近怪物的信息(包括自身信息)	@MonXinxi",
                "Pure delegation: sub_6BEC4C(). The GM-facing report is emitted inside the deferred core; the "
                + "shim itself sends no SysMsg."),
            new NativeMonsterMapCommand("HumNum", 54, 3, 0x006240A5u, "sub_6BED0C", true,
                "指定地图(不指定的时候为当前地图)的玩家数量	@HumNum [地图名/无]",
                "Always: n=sub_6BED0C(map) counts players, then SysMsg(0xFFDB, template+count). The count core "
                + "is deferred; the report + ident are shim-proven."),
            new NativeMonsterMapCommand("MAP", 58, 3, 0x006241CAu, "(inline)+sub_6DEF10", false,
                "当前玩家所在地图编号	@MAP",
                "Reads the GM's current map name inline, optionally appends '  idx: '+sub_6DEF10() index, then "
                + "SysMsg(0x38FF, msg). Read-only report; no mutation."),
            new NativeMonsterMapCommand("MonNumber", 73, 3, 0x00624CA4u, "sub_779DDC", true,
                "查询指定地图的怪物数量(不指定的时候为当前地图)	@MonNumber [地图名/无]",
                "Empty arg -> current map (always resolvable). GetMap(sub_6962D0): found -> n=sub_779DDC(map) + "
                + "SysMsg(0xFFDB, template+count); not found -> SysMsg(0x38FF, error)."),

            // ---- reload monster / npc / map config ----------------------------
            new NativeMonsterMapCommand("ReloadMonAtt", 111, 4, 0x0062517Cu, "sub_67D484", false,
                "重载怪物信息	@ReloadMonAtt",
                "Always: sub_67D484() reloads Thousand_mon.ini, then SysMsg(0x38FF, success/failure text)."),
            new NativeMonsterMapCommand("ReloadNpcPrize", 159, 4, 0x006258BFu, "sub_74EBB4", true,
                "重载NPC脚本奖励配置文件NormalPrize.ini	@ReloadNpcPrize",
                "ok=sub_74EBB4() reloads NormalPrize.ini. ok -> SysMsg(0xFFDB, success); else SysMsg(0x38FF, fail). "
                + "The reload runs on both paths; only the report ident differs. Result bool is deferred."),
            new NativeMonsterMapCommand("reshuaMonScript", 476, 5, 0x006291EFu, "sub_67DC40", false,
                "重新加载怪物脚本	@reshuaMonScript",
                "SysMsg(0xFFDB, '开始刷新怪物脚本') -> sub_67DC40 walks the manager+0xD4 active-script "
                + "monster list; sub_71F240 replaces each script from its existing path and reinitializes it "
                + "without rereading monScript.txt. An all-success run logs '成功刷新怪物脚本N个'. The shim then "
                + "always sends SysMsg(0xFFDB, '刷新怪物脚本结束')."),
            new NativeMonsterMapCommand("ReloadMonitemsTreeCfg", 576, 4, 0x00624002u, "sub_67AEC0", true,
                "重载MonItemsTree.txt文件	@ReloadMonitemsTreeCfg",
                "Always: sub_67AEC0() reloads MonItemsTree.txt, then SysMsg(0xFFDB, fixed). Reload core deferred."),
            new NativeMonsterMapCommand("LoadMonGen", 529, 3, 0x006295D4u, "sub_67B35C / sub_679954", true,
                "加载怪物配置文件	@LoadMonGen mongen/{mon 怪物名}",
                "arg0=='mongen' -> sub_67B35C(...) reload all + SysMsg(0xFFDB). arg0=='mon' -> idx=sub_679954(name): "
                + "idx==1 -> found report(0xFFDB); idx<0 -> not-found report(0xFFDB); idx==0 -> silent. Other/absent "
                + "sub-op -> silent. Both cores deferred."),

            // ---- monster spawning / npc action --------------------------------
            new NativeMonsterMapCommand("RangeShuag", 165, 4, 0x00625CC7u, "sub_62E58C", true,
                "范围刷怪	@RangeShuag 怪物名称 刷怪数量 刷怪范围",
                "Pure delegation: sub_62E58C(count, self, monName, range) spawns monsters across a radius. No SysMsg."),
            new NativeMonsterMapCommand("NpcHit", 182, 4, 0x00625AF8u, "sub_62EA7C", true,
                "让自身附近的可见的NPC做一个活动的动作	@NpcHit",
                "Pure delegation: sub_62EA7C() triggers a visible-NPC animation near the GM. No SysMsg."),

            // ---- inline map / global runtime parameters -----------------------
            new NativeMonsterMapCommand("ThroughRange", 136, 4, 0x006252D3u, "(inline)", false,
                "设置本服务器的安全区穿人范围(0;0..50)	@ThroughRange [无/0..50]",
                "n=StrToInt(arg,0). 0<=n<=50 -> *off_7D6970[0]=n + SysMsg(0x38FF, confirm). Out of range -> silent."),
            new NativeMonsterMapCommand("SetFountSwitch", 307, 4, 0x0062723Eu, "(inline)", false,
                "打开/关闭GM可控泉水	@SetFountSwitch [open/close]",
                "arg=='open' -> *(BYTE*)off_7D6EC8=1 + SysMsg(0x38FF, on). arg=='close' -> =0 + SysMsg(0x38FF, off). "
                + "else -> SysMsg(0x38FF, usage), no write."),
            new NativeMonsterMapCommand("SpiderWebTest", 340, 5, 0x00627D8Du, "(inline)", false,
                "设置蛛网的持续时间、使用的冷却时间、效果	@SpiderWebTest [lasttime/codetime/effect] [值]",
                "arg0=='lasttime' -> *(WORD*)off_7D6364=v. 'codetime' -> *(WORD*)off_7D6D14=v. 'effect' -> "
                + "*(WORD*)off_7D6F54=v (message differs when v==1). Every branch confirms with SysMsg(0xFCFF)."),
            new NativeMonsterMapCommand("setRecoverFactor", 375, 4, 0x0062826Fu, "sub_62ECE0", true,
                "设置安全区血量,魔法恢复比值	@setRecoverFactor 血量回复值 魔法回复值",
                "Both args present -> hp=StrToInt(arg0); mp=StrToInt(arg1); sub_62ECE0(hp) applies. Missing either "
                + "arg -> silent. No inline SysMsg."),
            new NativeMonsterMapCommand("SetNoKillMapLv", 392, 5, 0x006286BCu, "sub_6CDBBC", false,
                "如果GM所在地图是NOKillMap,则设置该地图的等级上限	@SetNoKillMapLv 等级值",
                "Strict StrToInt parses the level. sub_6CDBBC reads current map+0x71: false -> SysMsg(0xFFDB) "
                + "refusal, no write; true -> WORD map+0x74=(ushort)level and SysMsg(0xFFDB) reports the stored value."),
            new NativeMonsterMapCommand("MapCellFree", 454, 5, 0x00628B3Eu, "sub_77BEB4", false,
                "GM设置其ownmap中的每个点为free状态	@MapCellFree",
                "sub_77BEB4 walks every 12-byte cell record in the GM's current map and writes only "
                + "attribute byte +0 to Walk (0). Object chains and skill flags are unchanged. No SysMsg."),
            new NativeMonsterMapCommand("BreakLvCtrl", 309, 4, 0x00627322u, "sub_696228 / sub_718914 / sub_65645C", true,
                "查询全服暴击信息;设置某地图或全服的暴击等级	@BreakLvCtrl …",
                "Multi-mode. No arg -> report(0xFFDB). Set-per-map (GetMap-gated): writes *(BYTE*)(map+0xB8) or "
                + "*(WORD*)(map+0xBA) + report(0xFFDB); missing map -> silent. Set-global: writes off_7D6830[2]/[3] + "
                + "report(0xFFDB). Query: reports global crit info(0xFFDB), optional per-player line. EVERY path "
                + "that speaks uses 0xFFDB. The exact sub-op tokens are opaque in disp_decomp (sub_40BD78 string "
                + "compares against unresolved literals) and are NOT fabricated here."),
            new NativeMonsterMapCommand("LockInPlayers", 258, 3, 0x00626540u, "sub_6CDD48", true,
                "将指定范围内的玩家传送到地图Gow002~01中(位置随机)	@LockInPlayers 范围",
                "Pure delegation: sub_6CDD48() mass-teleports players in range to Gow002. No SysMsg. "
                + "(Also listed as a registry-only fact in the world-admin doc; modeled here.)"),
            new NativeMonsterMapCommand("TempSetMapParam", 577, 5, 0x006298E6u, "sub_774D24", true,
                "设置地图属性	@TempSetMapParam 地图名 属性 [1/0]",
                "Requires 3 args. GetMap(sub_6962D0): missing -> SysMsg(0x38FF). flag=StrToInt(arg2); "
                + "status=sub_774D24(flag, attr): status==1 -> SysMsg(0xFCFF add/remove per flag); status==100 -> "
                + "SysMsg(0x38FF unsupported); else -> SysMsg(0x38FF fail). Missing args -> SysMsg(0xFCFF usage)."),

            // ---- registered but SILENT NO-OP in this build (handler == def_622B15) ----
            new NativeMonsterMapCommand("ReloadLinkEx", 239, 4, NativeMonsterMapCommand.DefaultHandler, "", false,
                "重载地图的阻挡连接点文件LinkExInfo.txt	@ReloadLinkEx",
                "Registered (idx 239, perm 4, help present) but the jump slot is def_622B15: no case body ships. "
                + "Invoking it is a silent no-op (handled=0)."),
            new NativeMonsterMapCommand("sendProc", 338, 5, NativeMonsterMapCommand.DefaultHandler, "", false,
                "GM执行选中NPC的脚本函数	@sendProc 函数名",
                "Registered (idx 338, perm 5) but mapped to def_622B15: running a selected NPC's script proc is "
                + "unimplemented here — silent no-op."),
            new NativeMonsterMapCommand("CellInfo", 474, 4, NativeMonsterMapCommand.DefaultHandler, "", false,
                "打印当前地图某个点的信息	@CellInfo X坐标 Y坐标 半径范围",
                "Registered (idx 474, perm 4) but mapped to def_622B15: silent no-op. NOTE: the live "
                + "CellInfoCommand.cs DOES print cell info — that is drift (live does more than native)."),
            new NativeMonsterMapCommand("reloadbossmon", 521, 4, NativeMonsterMapCommand.DefaultHandler, "", false,
                "加载BOSS配置文件	@reloadbossmon",
                "Registered (idx 521, perm 4) but mapped to def_622B15: silent no-op. The live "
                + "ReloadBossMonCommand.cs is a fail-closed stub (NativeCommandFailure.Report, no reload) — it "
                + "matches the native no-op (NOT over-impl)."),
        };

        private static readonly Dictionary<string, NativeMonsterMapCommand> _byName =
            BuildIndex(_all);

        private static Dictionary<string, NativeMonsterMapCommand> BuildIndex(
            NativeMonsterMapCommand[] cmds)
        {
            var map = new Dictionary<string, NativeMonsterMapCommand>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in cmds)
                map[c.Name] = c;
            return map;
        }

        /// <summary>All modeled monster/map/npc command records.</summary>
        public static IReadOnlyList<NativeMonsterMapCommand> All => _all;

        /// <summary>Look up a modeled record by GM token (case-insensitive).</summary>
        public static NativeMonsterMapCommand Find(string name)
        {
            if (name != null && _byName.TryGetValue(name, out var c))
                return c;
            return null;
        }

        /// <summary>
        /// Contract for the registered-but-unimplemented commands (ReloadLinkEx,
        /// sendProc, CellInfo, reloadbossmon): recognized by the table (valid index
        /// + permission) but the switch lands on def_622B15 — nothing is mutated,
        /// nothing is sent. Reuses the shared <see cref="NativeGmDefaultNoOp"/>.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(string name)
        {
            var rec = Find(name);
            if (rec == null)
                throw new System.ArgumentException($"'{name}' is not a monster/map/npc command", nameof(name));
            if (rec.Implemented)
                throw new System.InvalidOperationException($"{rec.Name} is implemented; use Evaluate");
            return new NativeGmDefaultNoOp
            {
                Recognized = true,
                DispatchesToDefaultCase = true,
                MutatesState = false,
                SendsResponse = false,
            };
        }

        /// <summary>
        /// Dormant-evaluate "@name args" exactly as sub_622820 would, returning the
        /// observable outcome (branch, core, inline SysMsg ident, deferred flag).
        /// </summary>
        public static NativeMonsterMapEvaluation Evaluate(string name, int callerPerm,
            IReadOnlyList<string> args)
        {
            var rec = Find(name);
            if (rec == null)
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.UnknownCommand,
                    "", "", NoSysMsg, false, "token is not in the monster/map/npc family");

            // sub_621F28 returns 0 when the caller is under-privileged -> def_622B15.
            if (callerPerm < rec.RequiredPerm)
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.PermissionRejected,
                    "", "", NoSysMsg, false,
                    "callerPerm " + callerPerm + " < requiredPerm " + rec.RequiredPerm
                    + "; sub_621F28 returns index 0 -> def_622B15 (silent, handled=0)");

            // Handler is the shared no-op default.
            if (!rec.Implemented)
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.SilentNoOp,
                    "", "", NoSysMsg, false,
                    "handler == def_622B15 (0x0062B648): registered but no case body; "
                    + "no effect, no message, handled=0");

            var a = args ?? System.Array.Empty<string>();
            switch (rec.Name)
            {
                case "MonNumber": return EvalMonNumber(a);
                case "ThroughRange": return EvalThroughRange(a);
                case "ReloadNpcPrize": return EvalReloadNpcPrize();
                case "SetFountSwitch": return EvalSetFountSwitch(a);
                case "SetNoKillMapLv": return EvalSetNoKillMapLv(a);
                case "MapCellFree":
                    return new NativeMonsterMapEvaluation(
                        NativeMonsterMapOutcome.Executed, "attributes-walk",
                        rec.NativeCore, NoSysMsg, false, rec.EffectSummary);
                case "SpiderWebTest": return EvalSpiderWebTest(a);
                case "AutoMove": return EvalAutoMove(a);
                case "setRecoverFactor": return EvalSetRecoverFactor(a);
                case "LoadMonGen": return EvalLoadMonGen(a);
                case "TempSetMapParam": return EvalTempSetMapParam(a);
                case "BreakLvCtrl": return EvalBreakLvCtrl(a);

                // Unconditional-report commands (no guard; always one inline SysMsg).
                case "HumNum":
                    return Msg(NativeMonsterMapOutcome.ExecutedWithGmMessage, "report",
                        rec.NativeCore, SysMsgGmReply, true,
                        "count players on map via sub_6BED0C, then report");
                case "MAP":
                    return Msg(NativeMonsterMapOutcome.ExecutedWithGmMessage, "report",
                        rec.NativeCore, SysMsgUsage, false,
                        "report current map name (+ optional idx via sub_6DEF10); read-only");
                case "ReloadMonAtt":
                    return Msg(NativeMonsterMapOutcome.ExecutedWithGmMessage, "reload",
                        rec.NativeCore, SysMsgUsage, false,
                        "sub_67D484 reloads TMonSupport then reports status");
                case "reshuaMonScript":
                    return Msg(NativeMonsterMapOutcome.ExecutedWithGmMessage, "reload",
                        rec.NativeCore, SysMsgGmReply, false,
                        "exact start/end 0xFFDB messages bracket active-monster script replacement; "
                        + "each existing path is reloaded and initialized, while monScript.txt is not reread");
                case "ReloadMonitemsTreeCfg":
                    return Msg(NativeMonsterMapOutcome.ExecutedWithGmMessage, "reload",
                        rec.NativeCore, SysMsgGmReply, true, "sub_67AEC0 reload then confirm");

                default:
                    // gowgo / dingdianyidong / MonXinxi / RangeShuag / NpcHit /
                    // LockInPlayers: single-branch
                    // delegations, no shim-level guard, no shim SysMsg.
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.Executed,
                        "delegate", rec.NativeCore, NoSysMsg, rec.CoreBodyDeferred,
                        rec.EffectSummary);
            }
        }

        // --- MonNumber (case 73) --------------------------------------------
        private static NativeMonsterMapEvaluation EvalMonNumber(IReadOnlyList<string> a)
        {
            var map = Arg(a, 0);
            // Empty arg -> the GM's current map, which always resolves.
            if (!string.IsNullOrEmpty(map) && !ModeledMapExists(map))
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedWithGmMessage,
                    "map-missing", "sub_6962D0", SysMsgUsage, false,
                    "GetMap(sub_6962D0) failed -> SysMsg(0x38FF) error, no count");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                "count", "sub_779DDC", SysMsgGmReply, true,
                "sub_779DDC(map) counts monsters; SysMsg(0xFFDB) reports the count");
        }

        // --- ThroughRange (case 136) ----------------------------------------
        private static NativeMonsterMapEvaluation EvalThroughRange(IReadOnlyList<string> a)
        {
            int n = StrToInt(Arg(a, 0), 0);
            if (n >= 0 && n <= ThroughRangeMax)
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                    "value-0-to-50", "(inline)", SysMsgUsage, false,
                    "*off_7D6970[0] = " + n + " (安全区穿人范围), then SysMsg(0x38FF) confirm");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedSilently,
                "value-out-of-range", "(inline)", NoSysMsg, false,
                "n < 0 or n > 50 -> no write, no message");
        }

        // --- ReloadNpcPrize (case 159) --------------------------------------
        private static NativeMonsterMapEvaluation EvalReloadNpcPrize()
        {
            // sub_74EBB4 reloads NormalPrize.ini on both paths; only the report ident differs.
            if (ReloadNpcPrizeSucceeds)
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                    "success", "sub_74EBB4", SysMsgGmReply, true,
                    "reload ok -> SysMsg(0xFFDB) success");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                "fail", "sub_74EBB4", SysMsgUsage, true,
                "reload returned false -> SysMsg(0x38FF) failure");
        }

        // --- SetFountSwitch (case 307) --------------------------------------
        private static NativeMonsterMapEvaluation EvalSetFountSwitch(IReadOnlyList<string> a)
        {
            // sub_40591C is Delphi's case-sensitive long-string equality helper.
            var op = Arg(a, 0);
            if (op == "open")
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                    "open", "(inline)", SysMsgUsage, false,
                    "*(BYTE*)off_7D6EC8 = 1, then SysMsg(0x38FF) on");
            if (op == "close")
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                    "close", "(inline)", SysMsgUsage, false,
                    "*(BYTE*)off_7D6EC8 = 0, then SysMsg(0x38FF) off");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedWithGmMessage,
                "usage", "(inline)", SysMsgUsage, false,
                "arg matched neither open/close -> SysMsg(0x38FF) usage, no write");
        }

        // --- SetNoKillMapLv (case 392) --------------------------------------
        private static NativeMonsterMapEvaluation EvalSetNoKillMapLv(
            IReadOnlyList<string> a)
        {
            if (!TryParseStrictInt(Arg(a, 0), out var level))
                return new NativeMonsterMapEvaluation(
                    NativeMonsterMapOutcome.RejectedSilently,
                    "parse-failed", "sub_40C9D8", NoSysMsg, false,
                    "strict StrToInt failed before sub_6CDBBC; live port keeps state unchanged");

            if (!SetNoKillMapLvMapEnabled)
                return new NativeMonsterMapEvaluation(
                    NativeMonsterMapOutcome.RejectedWithGmMessage,
                    "map-not-user-no-kill", "sub_6CDBBC", SysMsgGmReply, false,
                    "current map byte+0x71 is zero -> no write; SysMsg(0xFFDB) refusal");

            var stored = unchecked((ushort)level);
            return new NativeMonsterMapEvaluation(
                NativeMonsterMapOutcome.ExecutedWithGmMessage,
                "stored-word-" + stored, "sub_6CDBBC", SysMsgGmReply, false,
                "current map WORD+0x74 = " + stored
                + "; SysMsg(0xFFDB) reports the stored WORD value");
        }

        // --- SpiderWebTest (case 340) ---------------------------------------
        private static NativeMonsterMapEvaluation EvalSpiderWebTest(IReadOnlyList<string> a)
        {
            var op = Lower(Arg(a, 0));
            switch (op)
            {
                case "lasttime":
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                        "lasttime", "(inline)", SysMsgNotice, false,
                        "*(WORD*)off_7D6364 = value (持续时间), SysMsg(0xFCFF)");
                case "codetime":
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                        "codetime", "(inline)", SysMsgNotice, false,
                        "*(WORD*)off_7D6D14 = value (冷却时间), SysMsg(0xFCFF)");
                case "effect":
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                        "effect", "(inline)", SysMsgNotice, false,
                        "*(WORD*)off_7D6F54 = value (效果标志); message text differs when value==1; SysMsg(0xFCFF)");
                default:
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedSilently,
                        "unknown-sub", "(inline)", NoSysMsg, false,
                        "arg0 matched none of lasttime/codetime/effect -> no write, no message");
            }
        }

        // --- AutoMove (case 233) --------------------------------------------
        private static NativeMonsterMapEvaluation EvalAutoMove(IReadOnlyList<string> a)
        {
            // sub_40CA18(argX,-1) / sub_40CA18(argY,-1): both must be != -1.
            int x = StrToInt(Arg(a, 1), AutoMoveInvalidCoord);
            int y = StrToInt(Arg(a, 2), AutoMoveInvalidCoord);
            if (x != AutoMoveInvalidCoord && y != AutoMoveInvalidCoord)
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.Executed,
                    "coords-ok", "sub_6D3024", NoSysMsg, true,
                    "both coords valid -> sub_6D3024(...) auto-move; no SysMsg");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedSilently,
                "coords-invalid", "sub_6D3024", NoSysMsg, false,
                "a coord parsed as -1 (missing/non-numeric) -> silent, no move");
        }

        // --- setRecoverFactor (case 375) ------------------------------------
        private static NativeMonsterMapEvaluation EvalSetRecoverFactor(IReadOnlyList<string> a)
        {
            if (!string.IsNullOrEmpty(Arg(a, 0)) && !string.IsNullOrEmpty(Arg(a, 1)))
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.Executed,
                    "both-args", "sub_62ECE0", NoSysMsg, true,
                    "hp=StrToInt(arg0); mp=StrToInt(arg1); sub_62ECE0(hp) applies; no SysMsg");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedSilently,
                "missing-arg", "sub_62ECE0", NoSysMsg, false,
                "one of the two recover values is absent -> silent, no apply");
        }

        // --- LoadMonGen (case 529) ------------------------------------------
        private static NativeMonsterMapEvaluation EvalLoadMonGen(IReadOnlyList<string> a)
        {
            var op = Lower(Arg(a, 0));
            if (op == "mongen")
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                    "mongen", "sub_67B35C", SysMsgGmReply, true,
                    "reload all MonGen config, then SysMsg(0xFFDB)");
            if (op == "mon")
            {
                int idx = LoadMonGenMonIndex(Arg(a, 1)); // sub_679954 return (deferred)
                if (idx == 1)
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                        "mon-found", "sub_679954", SysMsgGmReply, true,
                        "monster located (idx 1) -> found report SysMsg(0xFFDB)");
                if (idx < 0)
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                        "mon-not-found", "sub_679954", SysMsgGmReply, true,
                        "monster not found (idx -1) -> not-found report SysMsg(0xFFDB)");
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedSilently,
                    "mon-idx0", "sub_679954", NoSysMsg, true,
                    "idx 0 edge -> the shim reports nothing (only idx==1 hits the found branch)");
            }
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedSilently,
                "unknown-sub", "", NoSysMsg, false,
                "arg0 matched neither mongen nor mon -> silent");
        }

        // --- TempSetMapParam (case 577) -------------------------------------
        private static NativeMonsterMapEvaluation EvalTempSetMapParam(IReadOnlyList<string> a)
        {
            // Requires all three args (map, attribute, [1|0] flag).
            if (string.IsNullOrEmpty(Arg(a, 0)) || string.IsNullOrEmpty(Arg(a, 1))
                || string.IsNullOrEmpty(Arg(a, 2)))
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedWithGmMessage,
                    "usage", "", SysMsgNotice, false,
                    "fewer than 3 args -> SysMsg(0xFCFF) usage");

            if (!ModeledMapExists(Arg(a, 0)))
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedWithGmMessage,
                    "map-missing", "sub_6962D0", SysMsgUsage, false,
                    "GetMap(sub_6962D0) failed -> SysMsg(0x38FF)");

            int flag = StrToInt(Arg(a, 2), 0);
            int status = TempSetMapParamStatus; // sub_774D24(flag, attr) return (deferred)
            if (status == TempSetMapParamSuccess)
            {
                if (flag == 1)
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                        "added", "sub_774D24", SysMsgNotice, true, "attribute added -> SysMsg(0xFCFF)");
                if (flag == 0)
                    return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                        "removed", "sub_774D24", SysMsgNotice, true, "attribute removed -> SysMsg(0xFCFF)");
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedSilently,
                    "flag-other", "sub_774D24", NoSysMsg, true,
                    "flag not in {0,1} on success -> no report branch (silent)");
            }
            if (status == TempSetMapParamUnsupported)
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedWithGmMessage,
                    "unsupported", "sub_774D24", SysMsgUsage, true,
                    "status 100 (unsupported attribute) -> SysMsg(0x38FF)");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.RejectedWithGmMessage,
                "fail", "sub_774D24", SysMsgUsage, true,
                "status other -> SysMsg(0x38FF) failure");
        }

        // --- BreakLvCtrl (case 309) -----------------------------------------
        private static NativeMonsterMapEvaluation EvalBreakLvCtrl(IReadOnlyList<string> a)
        {
            // Coarse but faithful: the exact sub-op tokens are opaque in the dumps.
            // What is provable: every GM-visible path uses ident 0xFFDB, and the
            // set-per-map path is GetMap-gated (missing map -> silent). Distinguish
            // only "no arg -> report" from "arg -> set/query" without inventing
            // token names.
            if (string.IsNullOrEmpty(Arg(a, 0)))
                return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                    "report", "sub_65645C", SysMsgGmReply, true,
                    "no arg -> global crit info report (0xFFDB)");
            return new NativeMonsterMapEvaluation(NativeMonsterMapOutcome.ExecutedWithGmMessage,
                "set-or-query", "sub_696228", SysMsgGmReply, true,
                "arg present -> query / set-per-map (writes map+0xB8|+0xBA) / set-global "
                + "(off_7D6830[2]/[3]); all reporting paths use 0xFFDB; sub-op tokens not decoded");
        }

        // -----------------------------------------------------------------------------
        // Dormant hooks. The live port replaces these with real lookups; the model keeps
        // them injectable so the audit can exercise each branch deterministically without
        // a running server. Defaults describe the "empty world / reload succeeds" case.
        // -----------------------------------------------------------------------------

        /// <summary>GetMap oracle (sub_696228 / sub_6962D0): name -> exists.</summary>
        public static System.Func<string, bool> MapExistsHook { get; set; }

        /// <summary>sub_74EBB4 result for ReloadNpcPrize (NormalPrize.ini reload ok?).</summary>
        public static bool ReloadNpcPrizeSucceeds { get; set; } = true;

        /// <summary>sub_679954 monster-index oracle for LoadMonGen "mon" (default -1 = not found).</summary>
        public static System.Func<string, int> LoadMonGenMonIndexHook { get; set; }

        /// <summary>sub_774D24 status for TempSetMapParam (default 1 = success).</summary>
        public static int TempSetMapParamStatus { get; set; } = TempSetMapParamSuccess;

        /// <summary>Current map byte +0x71 oracle for SetNoKillMapLv.</summary>
        public static bool SetNoKillMapLvMapEnabled { get; set; }

        private static bool ModeledMapExists(string map)
            => MapExistsHook != null && MapExistsHook(map);

        private static int LoadMonGenMonIndex(string monName)
            => LoadMonGenMonIndexHook != null ? LoadMonGenMonIndexHook(monName) : -1;

        // --- tiny helpers ----------------------------------------------------
        private static NativeMonsterMapEvaluation Msg(NativeMonsterMapOutcome outcome,
            string branch, string core, int ident, bool deferred, string detail)
            => new NativeMonsterMapEvaluation(outcome, branch, core, ident, deferred, detail);

        private static string Arg(IReadOnlyList<string> a, int i)
            => (a != null && i < a.Count && a[i] != null) ? a[i] : "";

        private static string Lower(string s)
            => s == null ? "" : s.ToLowerInvariant();

        // Mirrors sub_40CA18(str, default): parse int, else return the default.
        private static int StrToInt(string s, int dflt)
            => int.TryParse(s, out int v) ? v : dflt;

        private static bool TryParseStrictInt(string s, out int value)
            => PasEngine.PasApiBridge.TryParseNativeDelphiInteger(s, out value);
    }
}
