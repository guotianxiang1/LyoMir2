// -----------------------------------------------------------------------------
// NativeGmMoveLeitaiCommands.cs
//
// DORMANT / FAIL-CLOSED reference model of two small M2Server "战神" GM ("@")
// command families inside the single dispatcher sub_622820 @0x00622820:
//   * family 08 MOVE / TELEPORT / DYNROOM  (Searching, AllowTeam, flyToDynRoom,
//     testCreateDynRoom, AddGroupMember, ReloadDynRoomConf)
//   * family 11 LEITAI / YABIAO / CROSS-SERVER (SetLTState, SetLTLimit,
//     queryTAScore, unlockAllTranser, unlockTranser, SetGsTaskVersion,
//     CloseYaBiao, ReloadLeitaiBlock, ReloadTransDuobao)
//
// NOT wired into the live command pipeline; the live commands remain the
// fail-closed stubs in GameSvr/Command/Commands/*Command.cs. This type only
// *describes* the exact original contract so AuditTools/
// NativeGmMoveLeitaiCommandsCheck can pin it against the binary.
//
// Source of truth
//   Binary : M2Server_unpacked_fixed.exe (战神), image base 0x00400000,
//            SHA256 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e
//   IDA db : staging/update_clothes_4637_ida_work/m2full.i64
//   Dumps  : disp_decomp.txt (Hex-Rays of sub_622820; case N == dispatchIndex),
//            big622820.txt, world_scan_out.txt / world_scan_lo_out.txt.
//
// Dispatch contract (reused verbatim from the world-admin / monster-map peers)
//   esi = sub_621F28(player, name, callerPerm, &reqPerm) @0x00621F28 returns the
//   record's dispatchIndex ONLY when callerPerm >= reqPerm, else 0;
//   cmp esi,0x2EE(750); ja default; jmp jpt_622B15[esi*4] (table @0x00622B1C).
//   Handler = *(uint*)(0x00622B1C + dispatchIndex*4).
//
// TWO silent no-op sinks appear in these families (unlike monster-map, which only
// used the first):
//   * def_622B15 @0x0062B648 — the shared "default" sink (var_D=0, silent).
//   * loc_62B64C @0x0062B64C — the empty-body case sink (bodyless `goto LABEL_1055`).
//   Both are behaviourally identical silent no-ops; a handler equal to EITHER ⇒
//   registered-but-unimplemented. The distinction is recorded per command.
//
// SysMsg = `mov cx,IDENT; call [self+0xD4]`. THREE idents (Hex-Rays signed LOWORD):
//   0xFFDB (-37)   GM reply/report · 0x38FF (14591) usage/error · 0xFCFF (-769) notice.
//
// KEY FINDING (same as the peers): delegating case blocks are THIN SHIMS that
// tail-call a core sub_XXXX whose body is NOT in the dumps (CoreBodyDeferred=true;
// == "needs-idat" to reverse further). This model captures only shim-provable
// facts (branch, inline write, SysMsg). Inline-write commands are fully modeled
// (CoreBodyDeferred=false): AllowTeam.
//
// NOTABLE QUIRK: the real "关闭押镖战场" (close-yabiao) logic lives under
// SetGsTaskVersion (idx 477 → sub_6FAD74); the command literally named CloseYaBiao
// (idx 509) is a registered SILENT NO-OP (0x0062B648). Both carry the same help
// text — an off-by-one in the record→help association (see gm_full_inventory).
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>Observable outcome class of one move/leitai GM invocation.</summary>
    public enum NativeMoveLeitaiOutcome
    {
        UnknownCommand,
        PermissionRejected,
        SilentNoOp,
        Executed,
        ExecutedWithGmMessage,
        RejectedSilently,
        RejectedWithGmMessage
    }

    /// <summary>Which silent-sink a registered no-op dispatches to.</summary>
    public enum NativeNoOpSink
    {
        /// <summary>Not a no-op (real case body).</summary>
        None,
        /// <summary>def_622B15 @0x0062B648 — shared default sink.</summary>
        DefaultCase,
        /// <summary>loc_62B64C @0x0062B64C — empty-body case sink.</summary>
        EmptyBody
    }

    /// <summary>One static GM command record, restricted to the move/leitai families.</summary>
    public sealed class NativeMoveLeitaiCommand
    {
        public const uint JumpTableBase = 0x00622B1C;    // jpt_622B15
        public const uint DefaultHandler = 0x0062B648;   // def_622B15 (silent no-op)
        public const uint EmptyBodyHandler = 0x0062B64C; // loc_62B64C (empty-body no-op)

        public NativeMoveLeitaiCommand(string name, int dispatchIndex, int requiredPerm,
            uint handlerAddress, string nativeCore, bool coreBodyDeferred,
            string helpGbk, string effectSummary)
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

        /// <summary>Handler address = *(JumpTableBase + DispatchIndex*4) — the CASE-BRANCH (shim) address.</summary>
        public uint HandlerAddress { get; }

        public uint JumpSlotAddress => JumpTableBase + (uint)DispatchIndex * 4;

        /// <summary>False iff the handler is one of the two silent-no-op sinks.</summary>
        public bool Implemented => HandlerAddress != DefaultHandler && HandlerAddress != EmptyBodyHandler;

        public NativeNoOpSink Sink =>
            HandlerAddress == DefaultHandler ? NativeNoOpSink.DefaultCase
            : HandlerAddress == EmptyBodyHandler ? NativeNoOpSink.EmptyBody
            : NativeNoOpSink.None;

        /// <summary>Delegated core sub_XXXX ("" / "(inline)" when none).</summary>
        public string NativeCore { get; }
        public bool CoreBodyDeferred { get; }
        public string HelpGbk { get; }
        public string EffectSummary { get; }
    }

    public sealed class NativeMoveLeitaiEvaluation
    {
        public NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome outcome, string branch,
            string nativeCore, int nativeSysMsgIdent, bool coreBodyDeferred, string detail)
        {
            Outcome = outcome;
            Branch = branch ?? "";
            NativeCore = nativeCore ?? "";
            NativeSysMsgIdent = nativeSysMsgIdent;
            CoreBodyDeferred = coreBodyDeferred;
            Detail = detail ?? "";
        }

        public NativeMoveLeitaiOutcome Outcome { get; }
        public string Branch { get; }
        public string NativeCore { get; }
        public int NativeSysMsgIdent { get; }
        public bool CoreBodyDeferred { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// Fail-closed model of the move/teleport/dynroom (08) + leitai/yabiao/cross-server
    /// (11) GM families. Registered no-ops reuse <see cref="NativeGmDefaultNoOp"/>.
    /// </summary>
    public static class NativeGmMoveLeitaiCommands
    {
        public const uint DispatcherEa = 0x00622820;
        public const uint IndexLookupEa = 0x00621F28;
        public const uint JumpTableEa = 0x00622B1C;
        public const int SwitchMaxIndex = 750;
        public const uint DefaultCaseEa = 0x0062B648;   // def_622B15
        public const uint EmptyBodyCaseEa = 0x0062B64C; // loc_62B64C

        public const int SysMsgVtableOffset = 0xD4;
        public const int SysMsgGmReply = 0xFFDB;  // -37
        public const int SysMsgUsage = 0x38FF;    // 14591
        public const int SysMsgNotice = 0xFCFF;   // -769
        public const int NoSysMsg = -1;

        // parse helper + inline data addresses proven by the shims
        public const uint StrToIntWithDefaultEa = 0x0040CA18; // sub_40CA18(str, default)
        public const int AllowTeamSelfFlagIndex = 2977;       // self[2977] = 1 (allow-team)
        public const uint DynRoomEnabledGlobalEa = 0x007D6728; // *off_7D6728[0] gates ReloadDynRoomConf

        // delegated cores (bodies NOT in the dumps — CoreBodyDeferred / needs-idat)
        public const uint SearchingCoreEa = 0x006CE56C;    // sub_6CE56C (探测项链 locate player)
        public const uint FlyToDynRoomLeCoreEa = 0x006DF088; // sub_6DF088 (roomNo <= 0 path)
        public const uint FlyToDynRoomGtCoreEa = 0x006DF020; // sub_6DF020 (roomNo > 0 path)
        public const uint AddGroupMemberCoreEa = 0x006C32D0; // sub_6C32D0 (add group member)
        public const uint FindPlayerEa = 0x00652784;         // sub_652784 (find player by name)
        public const uint ReloadDynRoomCoreEa = 0x005FBA58;  // sub_5FBA58 (reload dyn-room config)
        public const uint UnlockAllTranserCoreEa = 0x00713094; // sub_713094 (unlock all cross-server)
        public const uint UnlockTranserCoreEa = 0x007130E8;    // sub_7130E8 (unlock one)
        public const uint CloseYaBiaoCoreEa = 0x006FAD74;      // sub_6FAD74 (close 押镖 battlefield)

        private static readonly NativeMoveLeitaiCommand[] _all =
        {
            // ================= family 08 MOVE / TELEPORT / DYNROOM =================
            new NativeMoveLeitaiCommand("Searching", 30, 0, 0x00623B4Eu, "sub_6CE56C", true,
                "使用探测项链探测指定玩家角色的位置坐标(GMLevel >= 3)	@Searching 角色名",
                "Inline gate: only when self flag [+451] set OR GMLevel(self[+1653]) >= 3 -> sub_6CE56C() "
                + "locates the target; otherwise silent. Record perm is 0 but the body enforces GMLevel>=3."),
            new NativeMoveLeitaiCommand("AllowTeam", 256, 3, 0x006264F4u, "(inline)", false,
                "允许组队	@AllowTeam",
                "Inline: self[+2977] = 1 (enable the allow-team flag on the GM). No core, no SysMsg."),
            new NativeMoveLeitaiCommand("flyToDynRoom", 257, 3, 0x00626503u, "sub_6DF088 / sub_6DF020", true,
                "传送到动态房间	@flyToDynRoom 房间名",
                "n=Str_ToInt(arg). n<=0 -> sub_6DF088(0); n>0 -> sub_6DF020(0,0). Two delegated cores; no SysMsg."),
            new NativeMoveLeitaiCommand("testCreateDynRoom", 369, 5, 0x0062B64Cu, "", false,
                "GM大量建立动态房间(可以以此测试效率和内存使用)	@testCreateDynRoom 房间名 数量",
                "Registered (idx 369, perm 5) but the jump slot = loc_62B64C (empty-body sink): silent no-op."),
            new NativeMoveLeitaiCommand("AddGroupMember", 497, 5, 0x00629307u, "sub_6C32D0", true,
                "添加组队成员	@AddGroupMember 角色名",
                "GM has a group (self[+8-word])? no -> SysMsg(0x38FF) no-group. yes -> FindPlayer(sub_652784): "
                + "found -> sub_6C32D0() add; not found -> SysMsg(0x38FF) player-not-found."),
            new NativeMoveLeitaiCommand("ReloadDynRoomConf", 579, 4, 0x00629ACDu, "sub_5FBA58", true,
                "加载动态房间的配置信息	@ReloadDynRoomConf 房间名",
                "Guarded by *off_7D6728[0] (dyn-room system on) AND a non-empty room arg -> sub_5FBA58() reload + "
                + "SysMsg(0xFCFF); otherwise silent."),

            // ============ family 11 LEITAI / YABIAO / CROSS-SERVER ============
            new NativeMoveLeitaiCommand("SetLTState", 261, 5, 0x0062B64Cu, "", false,
                "设置擂台的当前状态(0..4)	@SetLTState 擂台状态",
                "Registered (idx 261, perm 5) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativeMoveLeitaiCommand("SetLTLimit", 266, 5, 0x0062B64Cu, "", false,
                "设置当前擂台人数限制	@SetLTLimit 守擂方人数 攻擂方人数",
                "Registered (idx 266, perm 5) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativeMoveLeitaiCommand("queryTAScore", 448, 3, 0x0062B64Cu, "", false,
                "查询玩家的跨服积分情况	@queryTAScore 玩家名 积分类型(1~3)",
                "Registered (idx 448, perm 3) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativeMoveLeitaiCommand("unlockAllTranser", 470, 4, 0x00628C5Eu, "sub_713094", true,
                "请求解锁所有跨服玩家	@unlockAllTranser",
                "Always: sub_713094(0) requests unlock of all cross-server players, then SysMsg(0xFFDB) confirm."),
            new NativeMoveLeitaiCommand("unlockTranser", 471, 4, 0x00628C8Bu, "sub_7130E8", true,
                "请求解锁某个跨服玩家	@unlockTranser 角色名",
                "arg present -> sub_7130E8(name,...) unlock one + SysMsg(0xFFDB); no arg -> SysMsg(0x38FF) usage."),
            new NativeMoveLeitaiCommand("SetGsTaskVersion", 477, 3, 0x0062B59Cu, "sub_6FAD74", true,
                "关闭押镖战场	@CloseYaBiao",
                "arg present -> sub_6FAD74() closes the 押镖 (escort) battlefield; else silent. No shim SysMsg. "
                + "This is the command that actually performs close-yabiao (the CloseYaBiao record @509 is a no-op)."),
            new NativeMoveLeitaiCommand("CloseYaBiao", 509, 3, 0x0062B648u, "", false,
                "关闭押镖战场	@CloseYaBiao",
                "Registered (idx 509, perm 3) but = def_622B15 (default sink): silent no-op. The real close-yabiao "
                + "action lives under SetGsTaskVersion (idx 477)."),
            new NativeMoveLeitaiCommand("ReloadLeitaiBlock", 563, 4, 0x0062B64Cu, "", false,
                "重载擂台阻挡点	@ReloadLeitaiBlock",
                "Registered (idx 563, perm 4) but = loc_62B64C (empty-body sink): silent no-op."),
            new NativeMoveLeitaiCommand("ReloadTransDuobao", 580, 4, 0x0062B648u, "", false,
                "加载跨服夺宝跳转配置文件	@ReloadTransDuobao",
                "Registered (idx 580, perm 4) but = def_622B15 (default sink): silent no-op."),
        };

        private static readonly Dictionary<string, NativeMoveLeitaiCommand> _byName = BuildIndex(_all);

        private static Dictionary<string, NativeMoveLeitaiCommand> BuildIndex(NativeMoveLeitaiCommand[] cmds)
        {
            var map = new Dictionary<string, NativeMoveLeitaiCommand>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in cmds) map[c.Name] = c;
            return map;
        }

        public static IReadOnlyList<NativeMoveLeitaiCommand> All => _all;

        public static NativeMoveLeitaiCommand Find(string name)
            => (name != null && _byName.TryGetValue(name, out var c)) ? c : null;

        /// <summary>Registered-but-unimplemented contract (both 0x62B648 and 0x62B64C sinks).</summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(string name)
        {
            var rec = Find(name);
            if (rec == null)
                throw new System.ArgumentException($"'{name}' is not a move/leitai command", nameof(name));
            if (rec.Implemented)
                throw new System.InvalidOperationException($"{rec.Name} is implemented; use Evaluate");
            return new NativeGmDefaultNoOp
            {
                Recognized = true,
                DispatchesToDefaultCase = true, // both sinks are the silent no-op behaviour
                MutatesState = false,
                SendsResponse = false,
            };
        }

        public static NativeMoveLeitaiEvaluation Evaluate(string name, int callerPerm, IReadOnlyList<string> args)
        {
            var rec = Find(name);
            if (rec == null)
                return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.UnknownCommand,
                    "", "", NoSysMsg, false, "token is not in the move/leitai families");

            if (callerPerm < rec.RequiredPerm)
                return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.PermissionRejected,
                    "", "", NoSysMsg, false,
                    "callerPerm " + callerPerm + " < requiredPerm " + rec.RequiredPerm
                    + "; sub_621F28 returns 0 -> def_622B15 (silent)");

            if (!rec.Implemented)
                return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.SilentNoOp,
                    "", "", NoSysMsg, false,
                    "handler == " + (rec.Sink == NativeNoOpSink.EmptyBody ? "loc_62B64C" : "def_622B15")
                    + " (silent no-op); no effect, no message");

            var a = args ?? System.Array.Empty<string>();
            switch (rec.Name)
            {
                case "Searching":         return EvalSearching();
                case "AllowTeam":         return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.Executed,
                                              "set-flag", "(inline)", NoSysMsg, false,
                                              "self[+2977] = 1 (allow-team); no SysMsg");
                case "flyToDynRoom":      return EvalFlyToDynRoom(a);
                case "AddGroupMember":    return EvalAddGroupMember();
                case "ReloadDynRoomConf": return EvalReloadDynRoomConf(a);
                case "unlockAllTranser":  return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.ExecutedWithGmMessage,
                                              "unlock-all", "sub_713094", SysMsgGmReply, true,
                                              "sub_713094(0) unlock all cross-server; SysMsg(0xFFDB) confirm");
                case "unlockTranser":     return EvalUnlockTranser(a);
                case "SetGsTaskVersion":  return EvalSetGsTaskVersion(a);
                default:
                    return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.Executed,
                        "delegate", rec.NativeCore, NoSysMsg, rec.CoreBodyDeferred, rec.EffectSummary);
            }
        }

        // Searching (30): gmLevel>=3 OR self-flag gate.
        private static NativeMoveLeitaiEvaluation EvalSearching()
            => SearchingGatePasses
                ? new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.Executed, "gate-ok", "sub_6CE56C",
                    NoSysMsg, true, "flag[+451] set or GMLevel>=3 -> sub_6CE56C() locate; no SysMsg")
                : new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.RejectedSilently, "gate-blocked",
                    "sub_6CE56C", NoSysMsg, false, "neither flag[+451] nor GMLevel>=3 -> silent, no locate");

        // flyToDynRoom (257): roomNo<=0 vs >0 -> different cores.
        private static NativeMoveLeitaiEvaluation EvalFlyToDynRoom(IReadOnlyList<string> a)
        {
            int n = StrToInt(Arg(a, 0), 0);
            return n <= 0
                ? new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.Executed, "room-le-0", "sub_6DF088",
                    NoSysMsg, true, "Str_ToInt(arg)<=0 -> sub_6DF088(0); no SysMsg")
                : new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.Executed, "room-gt-0", "sub_6DF020",
                    NoSysMsg, true, "Str_ToInt(arg)>0 -> sub_6DF020(0,0); no SysMsg");
        }

        // AddGroupMember (497): group? -> find -> add / errors.
        private static NativeMoveLeitaiEvaluation EvalAddGroupMember()
        {
            if (!GmHasGroup)
                return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.RejectedWithGmMessage, "no-group",
                    "", SysMsgUsage, false, "GM has no group -> SysMsg(0x38FF)");
            if (!AddGroupTargetFound)
                return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.RejectedWithGmMessage, "player-not-found",
                    "sub_652784", SysMsgUsage, false, "FindPlayer failed -> SysMsg(0x38FF)");
            return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.Executed, "add", "sub_6C32D0",
                NoSysMsg, true, "sub_6C32D0() adds the found player to the group; no shim SysMsg");
        }

        // ReloadDynRoomConf (579): system-on AND arg -> reload + notice.
        private static NativeMoveLeitaiEvaluation EvalReloadDynRoomConf(IReadOnlyList<string> a)
        {
            if (!DynRoomSystemEnabled)
                return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.RejectedSilently, "system-off",
                    "", NoSysMsg, false, "*off_7D6728[0]==0 -> silent");
            if (string.IsNullOrEmpty(Arg(a, 0)))
                return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.RejectedSilently, "no-arg",
                    "", NoSysMsg, false, "room name absent -> silent");
            return new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.ExecutedWithGmMessage, "reload",
                "sub_5FBA58", SysMsgNotice, true, "sub_5FBA58() reload + SysMsg(0xFCFF)");
        }

        // unlockTranser (471): arg -> unlock + reply; else usage.
        private static NativeMoveLeitaiEvaluation EvalUnlockTranser(IReadOnlyList<string> a)
            => string.IsNullOrEmpty(Arg(a, 0))
                ? new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.RejectedWithGmMessage, "usage", "",
                    SysMsgUsage, false, "no char name -> SysMsg(0x38FF) usage")
                : new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.ExecutedWithGmMessage, "unlock-one",
                    "sub_7130E8", SysMsgGmReply, true, "sub_7130E8(name,...) unlock + SysMsg(0xFFDB)");

        // SetGsTaskVersion (477): arg -> close-yabiao; else silent.
        private static NativeMoveLeitaiEvaluation EvalSetGsTaskVersion(IReadOnlyList<string> a)
            => string.IsNullOrEmpty(Arg(a, 0))
                ? new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.RejectedSilently, "no-arg", "sub_6FAD74",
                    NoSysMsg, false, "no arg -> silent, no close")
                : new NativeMoveLeitaiEvaluation(NativeMoveLeitaiOutcome.Executed, "close-yabiao", "sub_6FAD74",
                    NoSysMsg, true, "sub_6FAD74() closes the 押镖 battlefield; no shim SysMsg");

        // -------- dormant hooks (injectable for branch coverage) --------
        /// <summary>Searching gate: self flag [+451] set OR GMLevel(self[+1653]) >= 3.</summary>
        public static bool SearchingGatePasses { get; set; } = true;
        /// <summary>AddGroupMember: does the GM currently have a group?</summary>
        public static bool GmHasGroup { get; set; } = true;
        /// <summary>AddGroupMember: did FindPlayer(sub_652784) locate the target?</summary>
        public static bool AddGroupTargetFound { get; set; } = true;
        /// <summary>ReloadDynRoomConf: *off_7D6728[0] dyn-room system enabled?</summary>
        public static bool DynRoomSystemEnabled { get; set; } = true;

        // -------- helpers --------
        private static string Arg(IReadOnlyList<string> a, int i)
            => (a != null && i < a.Count && a[i] != null) ? a[i] : "";

        private static int StrToInt(string s, int dflt)
            => int.TryParse(s, out int v) ? v : dflt;
    }
}
