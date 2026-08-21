namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Model of the ANTICHEAT / IP / SECURITY GM command family (inventory family 09), reversed 1:1
    // from the original Delphi M2Server. Recovered cores are wired into the live command table one at
    // a time; unrecovered cores remain explicit deferred contracts instead of guessed behavior.
    //
    // Evidence (IDA/Hex-Rays over the unpacked image m2full.i64, image base 0x00400000; dump-only,
    // no idat/dotnet executed):
    //   Single GM switch sub_622820 @0x00622820; name->index sub_621F28 @0x00621F28 (returns index
    //   only when callerPerm >= requiredPerm); jump table jpt_622B15 @0x00622B1C (752 slots).
    //   Handler address = dword @ (0x00622B1C + idx*4).  Two no-op sinks:
    //     def_622B15 @0x0062B648 (result=0 default) and the empty-body exit @0x0062B64C.
    //   ALL 15 commands in this family have a distinct real case body (0 no-ops) — verified in
    //   disp_decomp.txt (cases 74/151/152/153/154/158/160/163/180/488/501/505/510/511/516) and against
    //   the raw disassembly in big622820.txt.
    //
    //   Parameter model inside sub_622820: v648 = 1st "@name" argument, v649 = 2nd argument (string
    //   pointers); sub_40CA18() = "fetch next parameter as integer". SysMsg to the invoking GM is the
    //   virtual call at vtable +0xD4 (offset 212) on the self object v656:
    //       (*(...)(*(_DWORD*)v656 + 212))(colorWord, messagePtr)
    //   Observed colour words (LOWORD immediates): 0x38FF (14591) and 0xFFDB (-37). These are the same
    //   two colours the skill/equip model calls ColorError / ColorSuccess; here they are pure echo /
    //   notice colours, so they are named neutrally.
    //
    //   Per-command case bodies (exact):
    //     74  MapUserInfo   perm3  @0x00624D3B  ->  sub_6D6698()                 (0 args; dumps hack-map players)
    //     151 ClearHackFlag perm4  @0x006255EE  ->  sub_6D321C()                 (charName read by core)
    //     152 Hackerpunish  perm4  @0x006255FE  ->  LADDER: n=Int; off_7D6010 = {0->1,1->2,2->3,else->1,n=0};
    //                                              sub_713890(0,n); SysMsg(off_7D6FEC[byte], 0x38FF)  [always]
    //     153 HackFlag      perm4  @0x00625690  ->  sub_6D440C(charName, days)
    //     154 IPHackFlag    perm4  @0x006256A3  ->  sub_6D45C8(ip, days)
    //     158 IPOutSay      perm4  @0x006258AC  ->  sub_6D4CA4(ip, seconds)
    //     160 IPHumNum      perm4  @0x006256B6  ->  sub_40CA18() (parse count); sub_6E3498()  (core reads ip)
    //     163 IpBlackRoom   perm4  @0x00625C98  ->  sub_6D49E4(ipOrUser, days)
    //     180 ClientVersion perm4  @0x00625969  ->  LADDER: set off_7D60D8=ver; SysMsg(confirm,0xFFDB) [always];
    //                                              if sub_655954()>0 -> SysMsg(mismatchCount, 0x38FF)
    //     488 kickOutPtid   perm4  @0x00629228  ->  sub_651CBC()                 (ptid read by core; iterates off_7D6D50)
    //     501 SetIpHumanMaxCount perm4 @0x006293B5 -> GUARD: if arg empty -> silent no-op; else
    //                                              n=Int; sub_7130E8((byte)self[262], key, n)  (config core, silent)
    //     505 ReloadWhiteList perm4 @0x00629465  ->  sub_7130E8((byte)self[262], key, 0)         (config core, silent)
    //     510 SetMonitor    perm3  @0x006294EB  ->  sub_79F908(charName, monType)  (off_7D62A4 monitor list)
    //     511 ViewMonitor   perm3  @0x00629502  ->  sub_79F5C4(buf, arg); SysMsg(view, 0xFFDB)   [always]
    //     516 ReloadSmsUserList perm4 @0x006294A9 -> ok=sub_6556F4(); SysMsg(ok?done:fail, 0xFFDB) (off_7D6D50)
    //
    //   sub_6D321C (ClearHackFlag) and sub_6D440C (HackFlag) are fully recovered and wired. The
    //   remaining core subs are deferred: their result/effect is taken as an INPUT here, never
    //   fabricated. Dispatcher-level ladders remain modelled exactly.
    //
    //   Shared cores worth noting: SetIpHumanMaxCount(501) and ReloadWhiteList(505) both tail into the
    //   generic server-config core sub_7130E8(selfByte, keyString, intValue) (also used by
    //   LogQueueSwitch(578) via LABEL_768). kickOutPtid(488) and ReloadSmsUserList(516) both walk the
    //   online player list off_7D6D50.
    //
    // Live drift flagged (NOT represented in this native-truth registry; see report / staging doc):
    //   MapUserInfoCommand.cs        perm 10 (native 3) AND behaviour drift (map-count vs hack-map dump)
    //   ReloadWhiteListCommand.cs    perm 10 (native 4), fail-closed stub
    //   ReloadSmsUserListCommand.cs  perm 10 (native 4), fail-closed stub
    // ------------------------------------------------------------------------------------------------

    public enum GmAntiCheatCommand
    {
        MapUserInfo,
        ClearHackFlag,
        Hackerpunish,
        HackFlag,
        IPHackFlag,
        IPOutSay,
        IPHumNum,
        IpBlackRoom,
        ClientVersion,
        KickOutPtid,
        SetIpHumanMaxCount,
        ReloadWhiteList,
        SetMonitor,
        ViewMonitor,
        ReloadSmsUserList,
    }

    /// <summary>Shape of the case body inside sub_622820 for a family-09 command.</summary>
    public enum GmAntiCheatShape
    {
        /// <summary>Case body is a single tail-call to a core; the dispatcher emits no message.</summary>
        ForwardOnly,
        /// <summary>Dispatcher parses one leading integer parameter, then tail-calls a core.</summary>
        ParseIntThenCore,
        /// <summary>Case body itself contains the branch ladder / global writes / SysMsg(s).</summary>
        DispatcherLadder,
    }

    /// <summary>Static command-table facts for one family-09 GM command (record name / +0x18 / +0x1C).</summary>
    public sealed class GmAntiCheatCommandInfo
    {
        public GmAntiCheatCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (static record +0x18, == runtime record +0x18 used by the switch).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (static record +0x1C). Original value; the C# stubs use 10.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>True when sub_622820 has a real case body (all family-09 commands are true).</summary>
        public bool Implemented { get; init; }
        /// <summary>Address of the case block (jump-table target dword @ 0x00622B1C + idx*4).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>Address of the core the case tail-calls / consults (0 if none).</summary>
        public uint CoreAddress { get; init; }
        /// <summary>True when the core body is not in the dumps and is abstracted as an input.</summary>
        public bool CoreBodyDeferred { get; init; }
        /// <summary>Shape of the case body.</summary>
        public GmAntiCheatShape Shape { get; init; }
        /// <summary>Number of "@name" string arguments passed straight through to the core.</summary>
        public int CoreStringArgs { get; init; }
        /// <summary>True when the case body itself sends at least one SysMsg to the invoking GM.</summary>
        public bool DispatcherSendsSysMsg { get; init; }
    }

    /// <summary>Forward contract for a ForwardOnly / ParseIntThenCore command (no dispatcher ladder).</summary>
    public sealed class GmAntiCheatForward
    {
        public uint CoreAddress { get; init; }
        public int CoreStringArgs { get; init; }
        /// <summary>True when the dispatcher parses one leading int before the core call (IPHumNum).</summary>
        public bool ParsesLeadingInt { get; init; }
        /// <summary>True while the forwarded core body has not yet been recovered.</summary>
        public bool CoreBodyDeferred { get; init; }
        /// <summary>Forward cases never emit a SysMsg from the dispatcher; the core may.</summary>
        public bool DispatcherSendsSysMsg => false;
    }

    public static class NativeGmAntiCheatCommands
    {
        // dispatcher constants (shared with the rest of the NativeGm* family)
        public const uint DispatcherEa = 0x00622820;   // sub_622820
        public const uint IndexLookupEa = 0x00621F28;  // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;    // jpt_622B15
        public const int SwitchMaxIndex = 750;         // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;  // def_622B15 (no-op sink #1)
        public const uint EmptyBodyNoOpEa = 0x0062B64C; // empty-body exit (no-op sink #2)

        // SysMsg colour words observed in the family-09 case bodies (LOWORD immediates)
        public const int ColorEcho = 0x38FF;   // 14591 — Hackerpunish echo, ClientVersion mismatch notice
        public const int ColorNotice = 0xFFDB; // -37    — ClientVersion confirm, ViewMonitor, ReloadSmsUserList

        // data globals touched by the case bodies
        public const uint HackPunishModeGlobalEa = 0x007D6010; // off_7D6010: punish-mode byte (1..3)
        public const uint HackPunishModeNamesEa = 0x007D6FEC;  // off_7D6FEC: mode-name string array
        public const uint ClientVersionGlobalEa = 0x007D60D8;  // off_7D60D8: server client-version string
        public const uint PlayerListEa = 0x007D6D50;           // off_7D6D50: online player list
        public const uint MonitorListEa = 0x007D62A4;          // off_7D62A4: monitor list

        // core subs (ClearHackFlag/HackFlag recovered; the others remain deferred)
        public const uint CoreMapUserInfo = 0x006D6698;   // sub_6D6698
        public const uint CoreClearHackFlag = 0x006D321C; // sub_6D321C
        public const uint CoreHackPunishApply = 0x00713890; // sub_713890
        public const uint CoreHackFlag = 0x006D440C;      // sub_6D440C
        public const uint CoreIpHackFlag = 0x006D45C8;    // sub_6D45C8
        public const uint CoreIpOutSay = 0x006D4CA4;      // sub_6D4CA4
        public const uint CoreIpHumNum = 0x006E3498;      // sub_6E3498
        public const uint CoreIpBlackRoom = 0x006D49E4;   // sub_6D49E4
        public const uint CoreVersionCheckAll = 0x00655954; // sub_655954 (returns mismatch count)
        public const uint CoreKickOutPtid = 0x00651CBC;   // sub_651CBC
        public const uint CoreServerConfig = 0x007130E8;  // sub_7130E8 (shared: WhiteList + IpHumanMax + LogQueue)
        public const uint CoreSmsReload = 0x006556F4;     // sub_6556F4 (returns bool)
        public const uint CoreSetMonitor = 0x0079F908;    // sub_79F908
        public const uint CoreViewMonitor = 0x0079F5C4;   // sub_79F5C4

        private static readonly GmAntiCheatCommandInfo[] Registry =
        {
            new() { Command = GmAntiCheatCommand.MapUserInfo,        Name = "MapUserInfo",        DispatchIndex = 74,  RequiredPermission = 3, Implemented = true, CaseAddress = 0x00624D3B, CoreAddress = CoreMapUserInfo,     CoreBodyDeferred = true, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 0, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.ClearHackFlag,      Name = "ClearHackFlag",      DispatchIndex = 151, RequiredPermission = 4, Implemented = true, CaseAddress = 0x006255EE, CoreAddress = CoreClearHackFlag,   CoreBodyDeferred = false, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 1, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.Hackerpunish,       Name = "Hackerpunish",       DispatchIndex = 152, RequiredPermission = 4, Implemented = true, CaseAddress = 0x006255FE, CoreAddress = CoreHackPunishApply, CoreBodyDeferred = true, Shape = GmAntiCheatShape.DispatcherLadder, CoreStringArgs = 0, DispatcherSendsSysMsg = true  },
            new() { Command = GmAntiCheatCommand.HackFlag,           Name = "HackFlag",           DispatchIndex = 153, RequiredPermission = 4, Implemented = true, CaseAddress = 0x00625690, CoreAddress = CoreHackFlag,        CoreBodyDeferred = false, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 2, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.IPHackFlag,         Name = "IPHackFlag",         DispatchIndex = 154, RequiredPermission = 4, Implemented = true, CaseAddress = 0x006256A3, CoreAddress = CoreIpHackFlag,      CoreBodyDeferred = true, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 2, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.IPOutSay,           Name = "IPOutSay",           DispatchIndex = 158, RequiredPermission = 4, Implemented = true, CaseAddress = 0x006258AC, CoreAddress = CoreIpOutSay,        CoreBodyDeferred = true, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 2, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.IPHumNum,           Name = "IPHumNum",           DispatchIndex = 160, RequiredPermission = 4, Implemented = true, CaseAddress = 0x006256B6, CoreAddress = CoreIpHumNum,        CoreBodyDeferred = true, Shape = GmAntiCheatShape.ParseIntThenCore, CoreStringArgs = 0, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.IpBlackRoom,        Name = "IpBlackRoom",        DispatchIndex = 163, RequiredPermission = 4, Implemented = true, CaseAddress = 0x00625C98, CoreAddress = CoreIpBlackRoom,     CoreBodyDeferred = true, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 2, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.ClientVersion,      Name = "ClientVersion",      DispatchIndex = 180, RequiredPermission = 4, Implemented = true, CaseAddress = 0x00625969, CoreAddress = CoreVersionCheckAll, CoreBodyDeferred = true, Shape = GmAntiCheatShape.DispatcherLadder, CoreStringArgs = 0, DispatcherSendsSysMsg = true  },
            new() { Command = GmAntiCheatCommand.KickOutPtid,        Name = "kickOutPtid",        DispatchIndex = 488, RequiredPermission = 4, Implemented = true, CaseAddress = 0x00629228, CoreAddress = CoreKickOutPtid,     CoreBodyDeferred = true, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 0, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.SetIpHumanMaxCount, Name = "SetIpHumanMaxCount", DispatchIndex = 501, RequiredPermission = 4, Implemented = true, CaseAddress = 0x006293B5, CoreAddress = CoreServerConfig,    CoreBodyDeferred = true, Shape = GmAntiCheatShape.DispatcherLadder, CoreStringArgs = 0, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.ReloadWhiteList,    Name = "ReloadWhiteList",    DispatchIndex = 505, RequiredPermission = 4, Implemented = true, CaseAddress = 0x00629465, CoreAddress = CoreServerConfig,    CoreBodyDeferred = true, Shape = GmAntiCheatShape.DispatcherLadder, CoreStringArgs = 0, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.SetMonitor,         Name = "SetMonitor",         DispatchIndex = 510, RequiredPermission = 3, Implemented = true, CaseAddress = 0x006294EB, CoreAddress = CoreSetMonitor,      CoreBodyDeferred = true, Shape = GmAntiCheatShape.ForwardOnly,      CoreStringArgs = 2, DispatcherSendsSysMsg = false },
            new() { Command = GmAntiCheatCommand.ViewMonitor,        Name = "ViewMonitor",        DispatchIndex = 511, RequiredPermission = 3, Implemented = true, CaseAddress = 0x00629502, CoreAddress = CoreViewMonitor,     CoreBodyDeferred = true, Shape = GmAntiCheatShape.DispatcherLadder, CoreStringArgs = 1, DispatcherSendsSysMsg = true  },
            new() { Command = GmAntiCheatCommand.ReloadSmsUserList,  Name = "ReloadSmsUserList",  DispatchIndex = 516, RequiredPermission = 4, Implemented = true, CaseAddress = 0x006294A9, CoreAddress = CoreSmsReload,       CoreBodyDeferred = true, Shape = GmAntiCheatShape.DispatcherLadder, CoreStringArgs = 0, DispatcherSendsSysMsg = true  },
        };

        public static GmAntiCheatCommandInfo Info(GmAntiCheatCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmAntiCheatCommandInfo> All => Registry;

        /// <summary>Family 09 has ZERO no-op commands — every index lands on a real case body.</summary>
        public static int NoOpCount
        {
            get
            {
                var n = 0;
                foreach (var e in Registry)
                    if (e.CaseAddress == DefaultCaseEa || e.CaseAddress == EmptyBodyNoOpEa)
                        n++;
                return n;
            }
        }

        /// <summary>
        /// Forward contract for a ForwardOnly / ParseIntThenCore command: recognized by the table,
        /// permission-gated, then the case body tail-calls its core with the string args
        /// passed straight through. The dispatcher itself sends no message.
        /// </summary>
        public static GmAntiCheatForward ForwardContract(GmAntiCheatCommand command)
        {
            var info = Info(command);
            if (info.Shape == GmAntiCheatShape.DispatcherLadder)
                throw new System.InvalidOperationException($"{info.Name} is a dispatcher ladder; use its own Evaluate");
            return new GmAntiCheatForward
            {
                CoreAddress = info.CoreAddress,
                CoreStringArgs = info.CoreStringArgs,
                ParsesLeadingInt = info.Shape == GmAntiCheatShape.ParseIntThenCore,
                CoreBodyDeferred = info.CoreBodyDeferred,
            };
        }
    }

    // ===================== ClearHackFlag (idx 151) =====================
    // sub_6D321C @0x006D321C:
    //   empty target name -> silent;
    //   target = sub_652784(name); missing/ghost/not-ReadyRun -> cx=0x38FF;
    //   target tier != 0 -> clear +0x1829/+0x180C/+0x7B0/+0x7B4,
    //                       RemoveState(25), cx=0xFFDB;
    //   target tier == 0 && target permission > 3 -> tier=3,
    //                       expiry=sub_6D43C4(invoking GM), cx=0x38FF;
    //   otherwise -> no mutation, cx=0xFFDB.
    public enum ClearHackFlagBranch
    {
        TargetNameEmpty,
        TargetMissing,
        Cleared,
        AppliedToPrivilegedTarget,
        NoRestriction,
    }

    public sealed class ClearHackFlagOutcome
    {
        public ClearHackFlagBranch Branch { get; init; }
        public bool MutatesTarget { get; init; }
        public byte StoredTier { get; init; }
        public int StoredExpiryDay { get; init; }
        public bool ClearsQuizState { get; init; }
        public bool RemovesTimedState25 { get; init; }
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
        public string Message { get; init; }
        public bool CoreBodyDeferred => false;
    }

    public static class NativeGmClearHackFlag
    {
        public static ClearHackFlagOutcome Evaluate(string targetName,
            bool targetFound, byte targetTier, byte targetPermission,
            int invokerCurrentDay)
        {
            if (string.IsNullOrEmpty(targetName))
            {
                return new ClearHackFlagOutcome
                {
                    Branch = ClearHackFlagBranch.TargetNameEmpty,
                };
            }

            if (!targetFound)
            {
                return new ClearHackFlagOutcome
                {
                    Branch = ClearHackFlagBranch.TargetMissing,
                    SendsSysMsg = true,
                    MessageColor = NativeGmAntiCheatCommands.ColorEcho,
                    Message = $"{targetName} 不在线或不在本GS服务器",
                };
            }

            if (targetTier != 0)
            {
                return new ClearHackFlagOutcome
                {
                    Branch = ClearHackFlagBranch.Cleared,
                    MutatesTarget = true,
                    StoredTier = 0,
                    StoredExpiryDay = 0,
                    ClearsQuizState = true,
                    RemovesTimedState25 = true,
                    SendsSysMsg = true,
                    MessageColor = NativeGmAntiCheatCommands.ColorNotice,
                    Message = $"清除 {targetName} 使用非法外挂的限制成功",
                };
            }

            if (targetPermission > 3)
            {
                return new ClearHackFlagOutcome
                {
                    Branch = ClearHackFlagBranch.AppliedToPrivilegedTarget,
                    MutatesTarget = true,
                    StoredTier = 3,
                    StoredExpiryDay = invokerCurrentDay,
                    SendsSysMsg = true,
                    MessageColor = NativeGmAntiCheatCommands.ColorEcho,
                    Message = $"设置 {targetName} 使用非法外挂成功",
                };
            }

            return new ClearHackFlagOutcome
            {
                Branch = ClearHackFlagBranch.NoRestriction,
                SendsSysMsg = true,
                MessageColor = NativeGmAntiCheatCommands.ColorNotice,
                Message = $"{targetName} 没有受到外挂惩罚机制的限制",
            };
        }
    }

    // ===================== HackFlag (idx 153) =====================
    // sub_6D440C @0x006D440C:
    //   empty target name -> usage, cx=0xFFDB;
    //   days = StrToIntDef(arg2, 0); target = sub_652784(arg1);
    //   missing/ghost/not-ReadyRun target -> silent;
    //   days == 0 -> [target+0x1829]=0, [target+0x180C]=0, cx=0xFFDB;
    //   days != 0 -> [target+0x1829]=3,
    //                [target+0x180C]=unchecked(sub_6D43C4(target)+7-days), cx=0x38FF.
    public enum HackFlagBranch
    {
        Usage,
        TargetMissing,
        Cleared,
        Applied,
    }

    public sealed class HackFlagOutcome
    {
        public HackFlagBranch Branch { get; init; }
        public int ParsedDays { get; init; }
        public bool MutatesTarget { get; init; }
        public byte StoredTier { get; init; }
        public int StoredExpiryDay { get; init; }
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
        public string Message { get; init; }
        public bool CoreBodyDeferred => false;
    }

    public static class NativeGmHackFlag
    {
        public const string UsageMessage =
            "设置标志：@HackFlag <玩家名> <天数> （天数=0清除）";

        public static HackFlagOutcome Evaluate(string targetName,
            string daysText, bool targetFound, int currentDay)
        {
            if (string.IsNullOrEmpty(targetName))
            {
                return new HackFlagOutcome
                {
                    Branch = HackFlagBranch.Usage,
                    SendsSysMsg = true,
                    MessageColor = NativeGmAntiCheatCommands.ColorNotice,
                    Message = UsageMessage,
                };
            }

            var days = SystemModule.HUtil32.Str_ToInt(daysText, 0);
            if (!targetFound)
            {
                return new HackFlagOutcome
                {
                    Branch = HackFlagBranch.TargetMissing,
                    ParsedDays = days,
                };
            }

            if (days == 0)
            {
                return new HackFlagOutcome
                {
                    Branch = HackFlagBranch.Cleared,
                    ParsedDays = days,
                    MutatesTarget = true,
                    StoredTier = 0,
                    StoredExpiryDay = 0,
                    SendsSysMsg = true,
                    MessageColor = NativeGmAntiCheatCommands.ColorNotice,
                    Message = $"清除 {targetName} 使用非法外挂的限制成功",
                };
            }

            return new HackFlagOutcome
            {
                Branch = HackFlagBranch.Applied,
                ParsedDays = days,
                MutatesTarget = true,
                StoredTier = 3,
                StoredExpiryDay = ComputeExpiryDay(currentDay, days),
                SendsSysMsg = true,
                MessageColor = NativeGmAntiCheatCommands.ColorEcho,
                Message = $"设置 {targetName} 外挂惩罚 {daysText} 天成功",
            };
        }

        public static int ComputeExpiryDay(int currentDay, int penaltyDays) =>
            unchecked(currentDay + 7 - penaltyDays);
    }

    // ===================== Hackerpunish (idx 152) =====================
    // "@Hackerpunish [无/0..2]"  case @0x006255FE
    //   n = Int(param);
    //   n==0        -> off_7D6010 = 1  (record-only)   , applied n=0
    //   n==1        -> off_7D6010 = 2  (forbid-record)  , applied n=1
    //   n==2        -> off_7D6010 = 3  (peace-mode)     , applied n=2
    //   n other !=0 -> off_7D6010 = 1  , n reset to 0   (invalid -> record-only)
    //   sub_713890(0, n)   [apply core, deferred]
    //   SysMsg(off_7D6FEC[byte], 0x38FF)   ALWAYS — echoes the chosen mode name.
    public enum HackerpunishBranch
    {
        RecordOnly,               // input 0
        ForbidRecord,             // input 1
        PeaceMode,                // input 2
        InvalidResetToRecordOnly, // any other nonzero
    }

    public sealed class HackerpunishOutcome
    {
        public HackerpunishBranch Branch { get; init; }
        /// <summary>Normalized mode passed to sub_713890 (0..2).</summary>
        public int NormalizedMode { get; init; }
        /// <summary>Value written to off_7D6010 (NormalizedMode + 1, i.e. 1..3).</summary>
        public int StoredModeByte { get; init; }
        public bool CallsApplyCore => true;
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmAntiCheatCommands.ColorEcho;
    }

    public static class NativeGmHackerpunish
    {
        public static HackerpunishOutcome Evaluate(int mode)
        {
            switch (mode)
            {
                case 0: return Make(HackerpunishBranch.RecordOnly, 0);
                case 1: return Make(HackerpunishBranch.ForbidRecord, 1);
                case 2: return Make(HackerpunishBranch.PeaceMode, 2);
                default: return Make(HackerpunishBranch.InvalidResetToRecordOnly, 0);
            }
        }

        private static HackerpunishOutcome Make(HackerpunishBranch b, int normalized) =>
            new() { Branch = b, NormalizedMode = normalized, StoredModeByte = normalized + 1 };
    }

    // ===================== ClientVersion (idx 180) =====================
    // "@ClientVersion 版本号"  case @0x00625969
    //   off_7D60D8 = param (server client-version string);
    //   SysMsg(confirm, 0xFFDB)   ALWAYS;
    //   if sub_655954() > 0   (count of online players whose version mismatches, deferred core)
    //     SysMsg(mismatchCount, 0x38FF).
    public enum ClientVersionBranch
    {
        NoMismatch,   // sub_655954() == 0 -> one message
        HasMismatch,  // sub_655954() > 0  -> two messages
    }

    public sealed class ClientVersionOutcome
    {
        public ClientVersionBranch Branch { get; init; }
        public bool SetsServerVersion => true;
        public bool SendsConfirm => true;
        public int ConfirmColor => NativeGmAntiCheatCommands.ColorNotice;
        public bool SendsMismatchNotice { get; init; }
        public int MismatchNoticeColor => NativeGmAntiCheatCommands.ColorEcho;
        public int MismatchCount { get; init; }
    }

    public static class NativeGmClientVersion
    {
        public static ClientVersionOutcome Evaluate(int mismatchCount)
        {
            var has = mismatchCount > 0;
            return new ClientVersionOutcome
            {
                Branch = has ? ClientVersionBranch.HasMismatch : ClientVersionBranch.NoMismatch,
                SendsMismatchNotice = has,
                MismatchCount = mismatchCount,
            };
        }
    }

    // ===================== SetIpHumanMaxCount (idx 501) =====================
    // "@SetIpHumanMaxCount 人数"  case @0x006293B5 -> LABEL_768
    //   if arg (v649) empty -> silent no-op (no core call, no message);
    //   else n = Int(param); sub_7130E8((byte)self[262], key, n)   (shared config core, deferred).
    //   No SysMsg from the dispatcher.
    public enum SetIpHumanMaxCountBranch
    {
        ParamEmpty, // argument missing -> nothing happens
        Applied,    // argument present -> config core invoked with the count
    }

    public sealed class SetIpHumanMaxCountOutcome
    {
        public SetIpHumanMaxCountBranch Branch { get; init; }
        public bool CallsConfigCore { get; init; }
        /// <summary>Count forwarded to sub_7130E8 (0 when the argument is missing).</summary>
        public int ConfigValue { get; init; }
        public bool SendsSysMsg => false;
    }

    public static class NativeGmSetIpHumanMaxCount
    {
        public static SetIpHumanMaxCountOutcome Evaluate(bool paramPresent, int count)
        {
            if (!paramPresent)
                return new SetIpHumanMaxCountOutcome { Branch = SetIpHumanMaxCountBranch.ParamEmpty, CallsConfigCore = false, ConfigValue = 0 };
            return new SetIpHumanMaxCountOutcome { Branch = SetIpHumanMaxCountBranch.Applied, CallsConfigCore = true, ConfigValue = count };
        }
    }

    // ===================== ReloadWhiteList (idx 505) =====================
    // "@ReloadWhiteList"  case @0x00629465
    //   sub_7130E8((byte)self[262], key, 0)   (shared config core, deferred) — unconditional.
    //   No SysMsg from the dispatcher (a confirmation, if any, is emitted by the core).
    public sealed class ReloadWhiteListOutcome
    {
        public bool CallsConfigCore => true;
        public int ConfigValue => 0;
        public bool SendsSysMsg => false;
    }

    public static class NativeGmReloadWhiteList
    {
        public static ReloadWhiteListOutcome Evaluate() => new();
    }

    // ===================== ViewMonitor (idx 511) =====================
    // "@ViewMonitor 0"  case @0x00629502
    //   sub_79F5C4(buf, arg)   (build the monitor-log view for the arg, deferred core);
    //   SysMsg(view, 0xFFDB)   ALWAYS — single path, always replies to the GM.
    public sealed class ViewMonitorOutcome
    {
        public bool CallsViewCore => true;
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmAntiCheatCommands.ColorNotice;
    }

    public static class NativeGmViewMonitor
    {
        public static ViewMonitorOutcome Evaluate() => new();
    }

    // ===================== ReloadSmsUserList (idx 516) =====================
    // "@ReloadSmsUserList"  case @0x006294A9
    //   ok = sub_6556F4()   (reload SmsUserList.txt, returns bool, deferred core);
    //   ok  -> SysMsg(done, 0xFFDB);
    //   !ok -> SysMsg(fail, 0xFFDB).   Both branches send a message, same colour.
    public enum ReloadSmsUserListBranch
    {
        Success,
        Failure,
    }

    public sealed class ReloadSmsUserListOutcome
    {
        public ReloadSmsUserListBranch Branch { get; init; }
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmAntiCheatCommands.ColorNotice;
    }

    public static class NativeGmReloadSmsUserList
    {
        public static ReloadSmsUserListOutcome Evaluate(bool reloadOk) =>
            new() { Branch = reloadOk ? ReloadSmsUserListBranch.Success : ReloadSmsUserListBranch.Failure };
    }
}
