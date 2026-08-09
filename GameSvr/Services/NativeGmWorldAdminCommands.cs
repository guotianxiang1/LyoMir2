// -----------------------------------------------------------------------------
// NativeGmWorldAdminCommands.cs
//
// DORMANT / FAIL-CLOSED reference model of the M2Server "战神" (God-of-War)
// WORLD / OTHER-ENTITY GM ("@") command family — the subset of the single GM
// dispatcher sub_622820 @0x00622820 that summons/clears monsters, reloads NPCs,
// mutates map state / drops, kicks or drags OTHER players, and sets/locks the
// world clock. This is NOT wired into the live command pipeline; it is an
// evidence-anchored specification that the audit (AuditTools/
// NativeGmWorldAdminCommandsCheck) pins against the binary and that porters can
// consult when replacing the C# stubs.
//
// Source of truth
//   Binary : M2Server_unpacked_fixed.exe (战神), image base 0x00400000,
//            SHA256 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e
//   IDA db : staging/update_clothes_4637_ida_work/m2full.i64
//   Dumps  : big622820.txt (full sub_622820 disassembly, addresses inline),
//            world_scan_out.txt / world_scan_lo_out.txt (decoded command records).
//
// Dispatch contract (already reversed by peers, reused verbatim here)
//   * The GM string "@name p0:p1,p2 ..." is split; the first token selects a
//     static command record (name ShortString; +0x18 = dispatchIndex,
//     +0x1C = requiredPerm; GBK help follows).
//   * esi = sub_621F28(player, name, callerPerm, &reqPerm) @0x00621F28 returns
//     the record's dispatchIndex ONLY when callerPerm >= reqPerm, else 0.
//   * cmp esi,0x2EE(750); ja default; jmp jpt_622B15[esi*4]  (table @0x00622B1C,
//     752 slots). jpt_622B15[0] = def_622B15 @0x0062B648.
//   * Handler = *(uint*)(0x00622B1C + dispatchIndex*4). A handler that equals
//     def_622B15 (0x0062B648) is a REGISTERED-BUT-UNIMPLEMENTED silent no-op:
//     the case sets var_D (the handled flag) to 0 and returns with no effect and
//     no message. Every real world-admin case body lives inside sub_622820 in
//     the range [0x00622820, 0x0062B760) and ends with `jmp loc_62B64C`.
//
// Case bodies parse the split tokens out of frame locals var_34 (arg0),
// var_38 (arg1), var_40+4 (arg2), var_40 (arg3); var_8 is the invoking GM
// TPlayObject. Inline GM feedback is `mov cx,IDENT; mov edx,offset gbk; call
// [player_vtable+0xD4]` (SysMsg, cx = native message ident: 0xFFDB = GM reply,
// 0x38FF = usage/refusal). Everything else is delegated to a Player/UserEngine
// helper (sub_xxxxxx) that performs the world mutation.
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>Observable outcome class of one world-admin GM invocation.</summary>
    public enum NativeWorldAdminOutcome
    {
        /// <summary>Token is not a member of the world-admin family modeled here.</summary>
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

        /// <summary>Native helper invoked / world state mutated; no inline GM message.</summary>
        Executed,

        /// <summary>As <see cref="Executed"/>, plus an inline confirmation/report SysMsg.</summary>
        ExecutedWithGmMessage,

        /// <summary>A native guard refused the action AND sent the GM an inline SysMsg.</summary>
        RejectedWithGmMessage,

        /// <summary>
        /// A native guard refused the action with NO message (e.g. map not found,
        /// target map is not a black-room, required argument absent). The case
        /// jumps straight to loc_62B64C.
        /// </summary>
        RejectedSilently
    }

    /// <summary>
    /// One static GM command record as it exists in the binary's command table,
    /// restricted to the world / other-entity family.
    /// </summary>
    public sealed class NativeWorldAdminCommand
    {
        public const uint JumpTableBase = 0x00622B1C; // jpt_622B15
        public const uint DefaultHandler = 0x0062B648; // def_622B15 (silent no-op)

        public NativeWorldAdminCommand(string name, int dispatchIndex,
            int requiredPerm, uint handlerAddress, string nativeHelper,
            string helpGbk, string effectSummary)
        {
            Name = name;
            DispatchIndex = dispatchIndex;
            RequiredPerm = requiredPerm;
            HandlerAddress = handlerAddress;
            NativeHelper = nativeHelper ?? "";
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

        /// <summary>Player/UserEngine helper the case body delegates to ("" if inline/no-op).</summary>
        public string NativeHelper { get; }

        /// <summary>GBK help string carried by the record.</summary>
        public string HelpGbk { get; }

        /// <summary>Prose description of the world effect (for porters).</summary>
        public string EffectSummary { get; }
    }

    /// <summary>Result of dormant-evaluating one invocation against the model.</summary>
    public sealed class NativeWorldAdminEvaluation
    {
        public NativeWorldAdminEvaluation(NativeWorldAdminOutcome outcome,
            string branch, string nativeHelper, int nativeSysMsgIdent, string detail)
        {
            Outcome = outcome;
            Branch = branch ?? "";
            NativeHelper = nativeHelper ?? "";
            NativeSysMsgIdent = nativeSysMsgIdent;
            Detail = detail ?? "";
        }

        public NativeWorldAdminOutcome Outcome { get; }

        /// <summary>Which internal branch fired (e.g. "fight", "open", "time-locked").</summary>
        public string Branch { get; }

        /// <summary>Native helper invoked on this branch ("" if none / inline / no-op).</summary>
        public string NativeHelper { get; }

        /// <summary>cx word passed to SysMsg (player_vtable+0xD4); -1 when no message.</summary>
        public int NativeSysMsgIdent { get; }

        public string Detail { get; }
    }

    /// <summary>
    /// Fail-closed model of the world / other-entity GM command family. Pure data
    /// + <see cref="Evaluate"/>; performs no I/O and mutates nothing.
    /// </summary>
    public static class NativeGmWorldAdminCommands
    {
        // Native inline SysMsg idents (cx word handed to player_vtable+0xD4).
        public const int SysMsgGmReply = 0xFFDB; // ordinary GM feedback / report
        public const int SysMsgUsage = 0x38FF;   // usage / refusal notice
        public const int NoSysMsg = -1;

        private static readonly NativeWorldAdminCommand[] _all =
        {
            // ---- kick / drag / teleport OTHER players -----------------------
            new NativeWorldAdminCommand("KickOut", 59, 2, 0x0062424E, "sub_6BEDDC",
                "踢出某个玩家让其断线(不指定地图)或者传送到另外一张地图(指定)",
                "Parses [charName] [mapName]; delegates to sub_6BEDDC. Empty map => force "
                + "the target player offline; non-empty => teleport the target to that map."),
            new NativeWorldAdminCommand("CallMan", 72, 3, 0x00624C94, "sub_6BF458",
                "将玩家拖到身边",
                "Parses [charName]; sub_6BF458 drags the named online player to the GM's cell."),
            new NativeWorldAdminCommand("kickOutBlackRoom", 490, 4, 0x0062923C, "sub_77BF14",
                "踢小黑屋中的玩家下线",
                "GetMap(mapName) via sub_696228; if the map is missing OR its black-room flag "
                + "[map+0x7C]==0, return silently; otherwise sub_77BF14 kicks every player on it offline."),

            // ---- summon / spawn / clear monsters ----------------------------
            new NativeWorldAdminCommand("Shuag", 82, 4, 0x00624D93, "sub_6BE470",
                "刷怪",
                "Parses [monName] [count]; sub_6BE470 spawns 'count' copies of monName at the GM's cell."),
            new NativeWorldAdminCommand("CallMob", 83, 4, 0x00624DA6, "sub_6BFC20",
                "招宠物",
                "Parses [monName] [count] [level]; sub_6BFC20 spawns the monsters as the GM's pets/slaves."),
            new NativeWorldAdminCommand("MonClear", 181, 4, 0x00625A57, "sub_779DF4",
                "杀死本地图(无:表示本地图)或指定地图的所有怪物;后面的参数0和非0的情况只是做了不同的处理",
                "Empty map arg => GM's current map (obj [player+0x128]); else GetMap(name). If the map "
                + "is missing, return silently. sub_779DF4(map, mode) kills all monsters and returns the "
                + "count; a formatted GM SysMsg (0xFFDB) reports it. arg1 (0 / non-0) selects the kill mode."),
            new NativeWorldAdminCommand("CreateCampMon", 339, 4, 0x00627EDD, "sub_6EB6B8",
                "在本地图刷怪(命令后面的参数可以用空格隔开,也可以用逗号隔开)	@CreateCampMon 怪物名 所属阵营 刷怪中心点X Y 怪物数量 刷怪范围 怪物守护点X Y",
                "Passes the whole argument tail (frame var_50) to sub_6EB6B8, which parses "
                + "monName/camp/centerX/centerY/count/range/guardX/guardY and spawns faction monsters on "
                + "the GM's map. No inline GM message."),

            // ---- NPC / map world state --------------------------------------
            new NativeWorldAdminCommand("ReShuaNpc", 207, 3, 0x00625DDF, "sub_6C6DE8",
                "重新加载自身周围的NPC/重载所有NPC/重载称号/重载地图扩展属性/关系重载/...",
                "Parses [scope] (all|Title|MapInfoExt|rela|...); sub_6C6DE8 reloads/respawns the "
                + "corresponding NPC / map-extension / relation runtime tables."),
            new NativeWorldAdminCommand("MapDropItem", 306, 4, 0x00627077, "sub_62E8F0",
                "开启/关闭/地图爆物;重载地图/动态地图爆物配置(MapDropItem_地图名.txt/PsDynNpc.txt)",
                "arg0 (lower-cased) selects a branch: open/close => sub_62E8F0(GM, 1|0) flips global "
                + "map-drop; loaddyn <room> => reload dynamic-room drops + GM SysMsg; worddrop => reload "
                + "world drop + GM SysMsg; load <map> => reload one map's drops. Unknown op returns silently."),
            new NativeWorldAdminCommand("SetMapState", 343, 4, 0x00627EED, "sub_696228",
                "修改地图的状态	@SetMapState 地图名 状态([fight|Safe|Normal])",
                "GetMap(name) via sub_696228; missing => silent. fight => [map+0x5D]=1,[map+0x5C]=0; "
                + "Safe => [map+0x5C]=1,[map+0x5D]=0; Normal => both 0; unknown state => silent. On a valid "
                + "state, a GM SysMsg (0xFFDB) reports the new state."),

            // ---- world clock ------------------------------------------------
            new NativeWorldAdminCommand("SetSysTime", 268, 5, 0x006267DF, "(inline)",
                "设置系统时间	@SetSysTime 年/月/日 时:分:秒",
                "If the time-lock flag byte_7DC270 is set, refuse with a usage SysMsg (0x38FF) and do "
                + "nothing. Otherwise apply the parsed date/time to the server clock and confirm with a GM "
                + "SysMsg (0xFFDB)."),
            new NativeWorldAdminCommand("LockTimeChg", 329, 5, 0x0062790D, "(inline)",
                "修改系统时间锁定标志(为true:表示不能修改系统时间;否则可以修改)",
                "Toggles the world time-lock flag: byte_7DC270 ^= 1. No message. This is exactly the flag "
                + "SetSysTime consults, so toggling it on makes @SetSysTime refuse."),

            // ---- registered but SILENT NO-OP in this build ------------------
            new NativeWorldAdminCommand("ChgOutlooksByMap", 342, 4, NativeWorldAdminCommand.DefaultHandler, "",
                "GM改变地图上所有玩家和英雄的外显	@ChgOutlooksByMap 地图名 第几套外显 持续时间(秒)",
                "Registered (index 342, perm 4, help present) but the jump slot points at def_622B15: no "
                + "case body ships in this build. Invoking it is a silent no-op (handled=0)."),
            new NativeWorldAdminCommand("ChgDreamCastleWar", 533, 5, NativeWorldAdminCommand.DefaultHandler, "",
                "开启/结束幻境沙巴克攻城战	@ChgDreamCastleWar",
                "Registered (index 533, perm 5) but mapped to def_622B15: opening/closing the 幻境 "
                + "(dream-castle) siege is unimplemented here — invoking it is a silent no-op."),
        };

        private static readonly Dictionary<string, NativeWorldAdminCommand> _byName =
            BuildIndex(_all);

        private static Dictionary<string, NativeWorldAdminCommand> BuildIndex(
            NativeWorldAdminCommand[] cmds)
        {
            var map = new Dictionary<string, NativeWorldAdminCommand>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in cmds)
                map[c.Name] = c;
            return map;
        }

        /// <summary>All modeled world-admin command records.</summary>
        public static IReadOnlyList<NativeWorldAdminCommand> All => _all;

        /// <summary>Look up a modeled record by GM token (case-insensitive).</summary>
        public static NativeWorldAdminCommand Find(string name)
        {
            if (name != null && _byName.TryGetValue(name, out var c))
                return c;
            return null;
        }

        /// <summary>
        /// Dormant-evaluate "@name args" exactly as sub_622820 would, returning the
        /// observable outcome (branch, native helper, inline SysMsg ident).
        /// </summary>
        public static NativeWorldAdminEvaluation Evaluate(string name, int callerPerm,
            IReadOnlyList<string> args)
        {
            var rec = Find(name);
            if (rec == null)
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.UnknownCommand,
                    "", "", NoSysMsg, "token is not in the world-admin family");

            // sub_621F28 returns 0 when the caller is under-privileged -> def_622B15.
            if (callerPerm < rec.RequiredPerm)
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.PermissionRejected,
                    "", "", NoSysMsg,
                    "callerPerm " + callerPerm + " < requiredPerm " + rec.RequiredPerm
                    + "; sub_621F28 returns index 0 -> def_622B15 (silent, handled=0)");

            // Handler is the shared no-op default.
            if (!rec.Implemented)
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.SilentNoOp,
                    "", "", NoSysMsg,
                    "handler == def_622B15 (0x0062B648): registered but no case body; "
                    + "no effect, no message, handled=0");

            var a = args ?? System.Array.Empty<string>();
            switch (rec.Name)
            {
                case "MapDropItem": return EvalMapDropItem(rec, a);
                case "SetMapState": return EvalSetMapState(rec, a);
                case "SetSysTime": return EvalSetSysTime(rec, a);
                case "MonClear": return EvalMonClear(rec, a);
                case "kickOutBlackRoom": return EvalKickOutBlackRoom(rec, a);
                default:
                    // KickOut / CallMan / Shuag / CallMob / CreateCampMon / ReShuaNpc /
                    // LockTimeChg: single-branch delegations with no GM-visible guard.
                    return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.Executed,
                        "default", rec.NativeHelper, NoSysMsg, rec.EffectSummary);
            }
        }

        // --- MapDropItem (case 306) -----------------------------------------
        private static NativeWorldAdminEvaluation EvalMapDropItem(
            NativeWorldAdminCommand rec, IReadOnlyList<string> a)
        {
            var op = Lower(Arg(a, 0));
            switch (op)
            {
                case "open":
                    return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.Executed,
                        "open", "sub_62E8F0", NoSysMsg,
                        "sub_62E8F0(GM, 1): enable global map-drop. Native sends no inline SysMsg here "
                        + "(the helper broadcasts a server switch).");
                case "close":
                    return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.Executed,
                        "close", "sub_62E8F0", NoSysMsg,
                        "sub_62E8F0(GM, 0): disable global map-drop.");
                case "loaddyn":
                    if (string.IsNullOrEmpty(Arg(a, 1)))
                        return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                            "loaddyn", "", NoSysMsg, "room name absent -> jz exit, no effect/message");
                    return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                        "loaddyn", "sub_5FD26C", SysMsgGmReply,
                        "reload the named dynamic room's drop config, then GM SysMsg (0xFFDB)");
                case "worddrop":
                    return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                        "worddrop", "sub_756310", SysMsgGmReply,
                        "reload world drop; GM SysMsg (0xFFDB) reports success/failure");
                case "load":
                    if (string.IsNullOrEmpty(Arg(a, 1)))
                        return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                            "load", "", NoSysMsg, "map name absent -> no reload");
                    return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                        "load", "(map drop loader)", SysMsgGmReply,
                        "reload the named map's drop config; GM SysMsg (0xFFDB)");
                default:
                    return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                        "unknown-op", "", NoSysMsg,
                        "arg0 matched none of open/close/loaddyn/worddrop/load -> jmp exit, no message");
            }
        }

        // --- SetMapState (case 343) -----------------------------------------
        private static NativeWorldAdminEvaluation EvalSetMapState(
            NativeWorldAdminCommand rec, IReadOnlyList<string> a)
        {
            var map = Arg(a, 0);
            if (string.IsNullOrEmpty(map))
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                    "no-map", "sub_696228", NoSysMsg, "empty map name -> GetMap returns 0 -> silent exit");

            var state = Arg(a, 1);
            if (EqI(state, "fight"))
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                    "fight", "sub_696228", SysMsgGmReply, "[map+0x5D]=1,[map+0x5C]=0; GM SysMsg reports state");
            if (EqI(state, "Safe"))
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                    "safe", "sub_696228", SysMsgGmReply, "[map+0x5C]=1,[map+0x5D]=0; GM SysMsg reports state");
            if (EqI(state, "Normal"))
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                    "normal", "sub_696228", SysMsgGmReply, "[map+0x5C]=0,[map+0x5D]=0; GM SysMsg reports state");

            // Map exists but the state token is none of fight/Safe/Normal.
            return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                "unknown-state", "sub_696228", NoSysMsg,
                "state token matched none of fight/Safe/Normal -> jz exit before the report SysMsg");
        }

        // --- SetSysTime (case 268) ------------------------------------------
        private static NativeWorldAdminEvaluation EvalSetSysTime(
            NativeWorldAdminCommand rec, IReadOnlyList<string> a)
        {
            if (WorldTimeLocked)
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedWithGmMessage,
                    "time-locked", "", SysMsgUsage,
                    "byte_7DC270 set: refuse with usage SysMsg (0x38FF), clock unchanged");
            return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                "set", "(inline)", SysMsgGmReply,
                "apply parsed date/time to the server clock; confirm with GM SysMsg (0xFFDB)");
        }

        // --- MonClear (case 181) --------------------------------------------
        private static NativeWorldAdminEvaluation EvalMonClear(
            NativeWorldAdminCommand rec, IReadOnlyList<string> a)
        {
            // Empty map arg => the GM's current map (never missing) => always clears.
            // Non-empty => GetMap(name); a missing map exits silently.
            var map = Arg(a, 0);
            if (!string.IsNullOrEmpty(map) && !ModeledMapExists(map))
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                    "map-missing", "sub_6962D0", NoSysMsg, "named map not found -> silent exit");
            return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.ExecutedWithGmMessage,
                "clear", "sub_779DF4", SysMsgGmReply,
                "sub_779DF4(map, mode) kills all monsters; GM SysMsg (0xFFDB) reports the count");
        }

        // --- kickOutBlackRoom (case 490) ------------------------------------
        private static NativeWorldAdminEvaluation EvalKickOutBlackRoom(
            NativeWorldAdminCommand rec, IReadOnlyList<string> a)
        {
            var map = Arg(a, 0);
            if (string.IsNullOrEmpty(map) || !ModeledMapExists(map))
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                    "map-missing", "sub_696228", NoSysMsg, "map not found -> silent exit");
            if (!ModeledMapIsBlackRoom(map))
                return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.RejectedSilently,
                    "not-blackroom", "sub_696228", NoSysMsg,
                    "[map+0x7C]==0 (not a black-room map) -> silent exit, no kick");
            return new NativeWorldAdminEvaluation(NativeWorldAdminOutcome.Executed,
                "kick", "sub_77BF14", NoSysMsg, "sub_77BF14(map): kick every player on the map offline");
        }

        // -----------------------------------------------------------------------------
        // Dormant world-state hooks. The live port replaces these with real lookups; the
        // model keeps them injectable so the audit can exercise each branch deterministically
        // without a running server. They default to the "no such map / not locked" world.
        // -----------------------------------------------------------------------------

        /// <summary>Mirror of byte_7DC270 (the world time-lock flag @0x007DC270).</summary>
        public static bool WorldTimeLocked { get; set; }

        /// <summary>Injected map-existence oracle (name -> exists).</summary>
        public static System.Func<string, bool> MapExistsHook { get; set; }

        /// <summary>Injected black-room oracle ([map+0x7C]!=0).</summary>
        public static System.Func<string, bool> MapIsBlackRoomHook { get; set; }

        private static bool ModeledMapExists(string map)
            => MapExistsHook != null && MapExistsHook(map);

        private static bool ModeledMapIsBlackRoom(string map)
            => MapIsBlackRoomHook != null && MapIsBlackRoomHook(map);

        // --- tiny helpers ----------------------------------------------------
        private static string Arg(IReadOnlyList<string> a, int i)
            => (a != null && i < a.Count && a[i] != null) ? a[i] : "";

        private static string Lower(string s)
            => s == null ? "" : s.ToLowerInvariant();

        private static bool EqI(string a, string b)
            => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }
}
