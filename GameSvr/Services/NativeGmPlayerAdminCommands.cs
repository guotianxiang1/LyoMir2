namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of the PLAYER / CHARACTER-SELF admin GM command family, reversed 1:1 from the
    // original Delphi M2Server. NOT wired into the live command table — the live commands remain the
    // C# stubs in GameSvr/Command/Commands/*Command.cs (several of which currently DIVERGE from the
    // original; the drifts are recorded at the bottom of this header). This type only *describes* the
    // exact original contract so an AuditTools check can lock it, and so a future port can reproduce
    // it precisely instead of guessing.
    //
    // Evidence (IDA/Hex-Rays over the unpacked image M2Server_unpacked_fixed.exe = m2full.i64,
    // image base 0x00400000, SHA256 5540f43b…c049670b14e). Same dispatcher as the SKILL/EQUIP family
    // (see NativeGmSkillEquipCommands.cs):
    //   sub_6D7D68 -> sub_6BB2F8 -> sub_622820 (the single GM switch).
    //   sub_622820 @0x00622820: splits "@name a:b,c d" into name + up to 6 params (':',',',' ');
    //     esi = sub_621F28(player, name, callerPerm, &reqPerm) — returns the record's dispatch index
    //     only when callerPerm >= record[+0x1C]; cmp esi,0x2EE(750); ja default; jmp jpt_622B15[esi*4]
    //     (table @0x00622B1C). Default def_622B15 @0x0062B648: handled=0, no effect / no message.
    //   Every command in THIS family has an INLINE case block inside sub_622820, so its "handler
    //   address" is the address of the case block itself (not a separate function). Static command
    //   records (name ShortString, +0x18 dispatchIndex, +0x1C requiredPerm, GBK help) were resolved by
    //   content scan (padmin_scan.py -> padmin_out.txt); delegated sub-handlers decompiled via
    //   core_dec.py -> padmin_subs.txt.
    //
    // TPlayObject field offsets touched by this family (verified from the case/sub decompiles):
    //   player + 0x071  gender/sex byte      (ChgSex toggles bit 0)                 [sub_6D77F0]
    //   player + 0x072  job/class byte       (ChgmanKind sets 0..3 by name)         [sub_6BE358]
    //   player + 0x074  death flag byte (Relive acts only when set)                 [sub_772DA8]
    //   player + 0x155  name-colour byte     (ChgNameClr writes low byte of arg)
    //   player + 0x160  PK point  (DWORD)    (ChgPkZero=0; InComePk += 100; ShowPk reads)
    //   player + 0x164  body luck (DWORD)    (ChgBodyLuck += delta, CLAMP [-10, +5])[sub_7698BC]
    //   player + 0x278  level  (WORD)        (IncSelfLv clamp[1,500]; UpSelfGrade raw)
    //   player + 0x1FC  level mirror (WORD)  (written alongside +0x278)
    //   player + 0x2E4  hide/invis flag byte (ChgHideState XOR bit 0)
    //   player + 0xAED  attack-mode byte     (AttackMode cycles 0..5)
    //   player + 0xBB0  hero object pointer   (HeroRename requires it)
    //   vtbl   + 0x0D4  SysMsg(cx = colour, text)      vtbl + 0x240 OnLevelChanged/recalc
    //   vtbl   + 0x250  feature broadcast (AttackMode feat 0x221)  vtbl + 0x1A8 state notify (ChgHideState feat 0x17)
    //   off_7D6D50 player list (FindPlayerByName sub_652784);  off_7D6B8C global PkRuleLevel (SetPkLv)
    //   sub_40CA18 Str_ToInt(str, default@edx);  sub_767548 refresh appearance/PK colour;
    //   sub_768C7C town-recall;  sub_766060 perform revive.
    //   SysMsg colours: 0xFFDB (= (short)-37, confirm/green), 0x38FF (= 14591, red/error),
    //                   0x27B1 (relive notice), 0xFCFF (SetPkLv empty-arg).
    //
    // C# STUB DRIFT (live commands send MORE / differently than the original):
    //   * ChgHideStateCommand.cs — parses "humName state(0/1)", looks up a NAMED player, sends
    //     SysMsg + MainOutMessage log, registers perm 10. ORIGINAL (case 102): NO args, toggles the
    //     GM's OWN +0x2E4 flag, broadcasts (vtbl+0x1A8 feat 0x17), sends NO SysMsg, perm 4.
    //   * ChgBodyLuckCommand.cs — SETS m_dBodyLuck = luck * 5000 and sends a 2nd SysMsg to the target,
    //     perm 10. ORIGINAL (case 92): ADDS delta to +0x164 with CLAMP [-10,+5], sends only the GM a
    //     confirm, no message to the target, perm 4.
    // ------------------------------------------------------------------------------------------------

    public enum GmPlayerAdminCommand
    {
        AttackMode,
        MakeGo,
        SuperCome,
        ChgPkZero,
        ShowPk,
        InComePk,
        ChgBodyLuck,
        ChgManKind,
        ChgSex,
        ChgNameClr,
        ChgHideState,
        IncSelfLv,
        PlayerRename,
        HeroRename,
        Relive,
        UpSelfGrade,
        SetPkLv,
        ChgNewBie, // registered but no-op (def_622B15)
    }

    /// <summary>Static command-table facts for one player-admin GM command (record name / +0x18 / +0x1C).</summary>
    public sealed class GmPlayerAdminCommandInfo
    {
        public GmPlayerAdminCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (static record +0x18 == runtime record +0x18 used by the switch).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (static record +0x1C). The C# stubs use their own values.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>True when sub_622820 has a real INLINE case; false when the index falls on def_622B15.</summary>
        public bool Implemented { get; init; }
        /// <summary>Address of the inline case block (implemented) or the shared default label (0x0062B648).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>True when this file carries a full per-command Evaluate() branch model.</summary>
        public bool ModeledInDepth { get; init; }
    }

    public static class NativeGmPlayerAdminCommands
    {
        // dispatcher constants (shared with every GM family)
        public const uint DispatcherEa = 0x00622820;   // sub_622820
        public const uint IndexLookupEa = 0x00621F28;  // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;    // jpt_622B15
        public const int SwitchMaxIndex = 750;         // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;  // def_622B15 (no-op)

        // TPlayObject field offsets (see header)
        public const int GenderOffset = 0x071;
        public const int JobOffset = 0x072;
        public const int DeadFlagOffset = 0x074;
        public const int NameColorOffset = 0x155;
        public const int PkPointOffset = 0x160;
        public const int BodyLuckOffset = 0x164;
        public const int LevelOffset = 0x278;
        public const int LevelMirrorOffset = 0x1FC;
        public const int HideStateOffset = 0x2E4;
        public const int AttackModeOffset = 0x0AED;
        public const int HeroPtrOffset = 0x0BB0;

        // vtbl byte offsets
        public const int SysMsgVtbl = 0x0D4;
        public const int LevelChangeVtbl = 0x240;
        public const int FeatureBroadcastVtbl = 0x250; // AttackMode
        public const int StateNotifyVtbl = 0x1A8;      // ChgHideState

        // helper / global addresses
        public const uint FindPlayerByNameEa = 0x00652784;
        public const uint StrToIntEa = 0x0040CA18;
        public const uint RefreshAppearanceEa = 0x00767548;
        public const uint TownRecallEa = 0x00768C7C;
        public const uint PerformReviveEa = 0x00766060;
        public const uint PkRuleLevelGlobalEa = 0x007D6B8C;

        // SysMsg colours (cx immediates)
        public const int ColorConfirm = 0xFFDB;        // (short)-37 — confirm / green
        public const int ColorRed = 0x38FF;            // 14591 — error / red
        public const int ColorSetPkEmpty = 0xFCFF;     // SetPkLv empty-arg message

        // Scheduled/immediate message IDENTS (the `cx` argument to sub_766060 /
        // sub_765E68).  These are NOT colours — sub_766060 @0x766069/0x76608E does
        // `mov word [ebp-6],cx` then `mov word [ebx],ax`, i.e. cx becomes the queued
        // record's ident field.  A previous constant here mislabelled 0x27B1 as a
        // relive broadcast COLOUR; the value was right, the classification was not.
        /// <summary>
        /// 0x27B1 = 10161, the DELAYED-REVIVE ident. Used by the GM <c>@Relive</c> case
        /// @0x625A43 (<c>mov cx,0x27B1</c>) with a <c>push 0x1F4</c> = 500 ms delay into
        /// <c>sub_766060</c> @0x625A4D, and by the PAS <c>dorelive</c> worker
        /// <c>sub_6E13C8</c> @0x6E13E9 with <c>imul eax,esi,0x3E8</c> (delay x 1000 ms)
        /// @0x6E13DE.
        /// </summary>
        public const int DelayedReviveIdent = 0x27B1;  // 10161

        /// <summary>
        /// 0x27B0 = 10160, the immediate NOTICE ident, issued through
        /// <c>sub_765E68</c>. In <c>sub_6E13C8</c> @0x6E1403 it follows the delayed
        /// revive with <c>push 4</c> / <c>push delayTime</c> / <c>push 0x3E9</c> (1001) /
        /// <c>push 0</c> / <c>push str@0x6E141C</c> / <c>push 0</c>.
        /// </summary>
        public const int ImmediateNoticeIdent = 0x27B0; // 10160

        // domain constants proven by the case/sub decompiles
        public const int AttackModeMax = 5;            // cycles 0..5 (6 modes)
        public const int LevelHardCap = 500;           // IncSelfLv clamp upper (0x1F4)
        public const int LevelFloor = 1;               // IncSelfLv clamp lower
        public const int BodyLuckMin = -10;            // ChgBodyLuck clamp
        public const int BodyLuckMax = 5;              // ChgBodyLuck clamp
        public const int InComePkStep = 100;           // InComePk immediate (0x64)
        public const int NameColorDefault = 0xFF;      // ChgNameClr Str_ToInt default (edx=0xFF)
        public const int JobCount = 4;                 // ChgmanKind matches 4 job names -> 0..3

        private static readonly GmPlayerAdminCommandInfo[] Registry =
        {
            // name              idx  perm impl  caseAddr     modeledInDepth
            new() { Command = GmPlayerAdminCommand.AttackMode,   Name = "AttackMode",   DispatchIndex = 26,  RequiredPermission = 0, Implemented = true,  CaseAddress = 0x006239FA, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.MakeGo,       Name = "MakeGo",       DispatchIndex = 60,  RequiredPermission = 3, Implemented = true,  CaseAddress = 0x00624269, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.SuperCome,    Name = "supercome",    DispatchIndex = 61,  RequiredPermission = 3, Implemented = true,  CaseAddress = 0x00624279, ModeledInDepth = false },
            new() { Command = GmPlayerAdminCommand.ChgPkZero,    Name = "ChgPkZero",    DispatchIndex = 89,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00624EA3, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.ShowPk,       Name = "ShowPk",       DispatchIndex = 90,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00624EB3, ModeledInDepth = false },
            new() { Command = GmPlayerAdminCommand.InComePk,     Name = "InComePk",     DispatchIndex = 91,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00624EC3, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.ChgBodyLuck,  Name = "ChgBodyLuck",  DispatchIndex = 92,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00624ED5, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.ChgManKind,   Name = "ChgmanKind",   DispatchIndex = 97,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625008, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.ChgSex,       Name = "ChgSex",       DispatchIndex = 98,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625018, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.ChgNameClr,   Name = "ChgNameClr",   DispatchIndex = 99,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625025, ModeledInDepth = false },
            new() { Command = GmPlayerAdminCommand.ChgHideState, Name = "ChgHideState", DispatchIndex = 102, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625076, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.IncSelfLv,    Name = "IncSelfLv",    DispatchIndex = 104, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006250AB, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.PlayerRename, Name = "PlayerRename", DispatchIndex = 105, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625114, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.HeroRename,   Name = "HeroRename",   DispatchIndex = 106, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625124, ModeledInDepth = false },
            new() { Command = GmPlayerAdminCommand.Relive,       Name = "Relive",       DispatchIndex = 193, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006259FB, ModeledInDepth = true  },
            new() { Command = GmPlayerAdminCommand.UpSelfGrade,  Name = "UpSelfGrade",  DispatchIndex = 217, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00625F17, ModeledInDepth = false },
            new() { Command = GmPlayerAdminCommand.SetPkLv,      Name = "SetPkLv",      DispatchIndex = 259, RequiredPermission = 3, Implemented = true,  CaseAddress = 0x00626550, ModeledInDepth = false },
            // registered but no-op — index maps to def_622B15
            new() { Command = GmPlayerAdminCommand.ChgNewBie,    Name = "ChgNewBie",    DispatchIndex = 225, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa, ModeledInDepth = false },
        };

        public static GmPlayerAdminCommandInfo Info(GmPlayerAdminCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmPlayerAdminCommandInfo> All => Registry;

        /// <summary>
        /// Contract for a registered-but-unimplemented command (ChgNewBie): recognized by the table
        /// (valid index + permission), permission-gated, but the switch lands on def_622B15 — so
        /// nothing is mutated and nothing is sent back. Faithful behaviour is a silent no-op.
        /// Reuses the shared <see cref="NativeGmDefaultNoOp"/> defined in NativeGmSkillEquipCommands.cs.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(GmPlayerAdminCommand command)
        {
            var info = Info(command);
            if (info.Implemented)
                throw new System.InvalidOperationException($"{info.Name} is implemented; use its own Evaluate");
            return new NativeGmDefaultNoOp
            {
                Recognized = true,
                DispatchesToDefaultCase = true,
                MutatesState = false,
                SendsResponse = false,
            };
        }

        /// <summary>Clamp helper mirroring the sub_4C7004/sub_4C700C (min/max) pair used by IncSelfLv.</summary>
        public static int Clamp(int value, int lo, int hi)
            => value < lo ? lo : (value > hi ? hi : value);
    }

    // ===================== AttackMode (idx 26, perm 0) =====================
    // case @0x006239FA: no args. cmp [self+0xAED],5; if >=5 -> 0 else ++.  Then broadcast the new
    // mode via vtbl[0x250] with feature 0x221 (dx=0x221, cl=newMode). No SysMsg. Perm 0 = anyone.
    // NOTE: the C# ChangeNativeAttackMode(requestTag) is a client-driven *setter*; this GM command is
    // a *cycle* over the same 0..5 range (All/Peace/Group/Gild/Hostile/Corps) then a 0x221 broadcast.
    public sealed class AttackModeOutcome
    {
        public int OldMode { get; init; }
        public int NewMode { get; init; }
        public bool BroadcastsFeature => true;   // vtbl+0x250, feature 0x221
        public bool SendsSysMsg => false;
    }

    public static class NativeGmAttackMode
    {
        public static AttackModeOutcome Evaluate(int currentMode)
        {
            int next = currentMode >= NativeGmPlayerAdminCommands.AttackModeMax ? 0 : currentMode + 1;
            return new AttackModeOutcome { OldMode = currentMode, NewMode = next };
        }
    }

    // ===================== MakeGo (idx 60, perm 3) =====================
    // case @0x00624269 -> sub_6BF02C(self, p0=charName).  charName empty -> target = self; else
    // target = FindPlayerByName(charName).  target found -> town-recall to a random return point
    // (sub_768C7C(1)); target not found -> SysMsg(ColorRed, not-found text). "@MakeGo [角色名|无]".
    public enum MakeGoBranch { RecalledSelf, RecalledPlayer, PlayerNotFound }

    public sealed class MakeGoOutcome
    {
        public MakeGoBranch Branch { get; init; }
        public bool Recalls { get; init; }          // town-recall performed (sub_768C7C)
        public bool SendsSysMsg { get; init; }      // only on not-found
        public int MessageColor { get; init; }
    }

    public static class NativeGmMakeGo
    {
        public static MakeGoOutcome Evaluate(string charName, bool playerFound)
        {
            if (string.IsNullOrEmpty(charName))
                return new MakeGoOutcome { Branch = MakeGoBranch.RecalledSelf, Recalls = true, SendsSysMsg = false, MessageColor = 0 };
            if (!playerFound)
                return new MakeGoOutcome { Branch = MakeGoBranch.PlayerNotFound, Recalls = false, SendsSysMsg = true, MessageColor = NativeGmPlayerAdminCommands.ColorRed };
            return new MakeGoOutcome { Branch = MakeGoBranch.RecalledPlayer, Recalls = true, SendsSysMsg = false, MessageColor = 0 };
        }
    }

    // ===================== ChgPkZero (idx 89, perm 4) =====================
    // case @0x00624EA3 -> sub_6BFD58(self, p0=charName).  player = FindPlayerByName(charName):
    //   found     -> player[+0x160] (PK point) = 0; refresh appearance (sub_767548); confirm text
    //                is formatted and the SysMsg(ColorConfirm) carries it.
    //   not found -> the confirm buffer is never formatted; SysMsg(ColorConfirm) still fires but empty.
    // "将某角色的PK值清零  @ChgPkZero 角色名"
    public enum ChgPkZeroBranch { Cleared, PlayerNotFound }

    public sealed class ChgPkZeroOutcome
    {
        public ChgPkZeroBranch Branch { get; init; }
        public bool PkSetToZero { get; init; }
        public bool RefreshesAppearance { get; init; }
        public bool SendsSysMsg => true;            // vtbl+0xD4 called on both paths
        public bool SendsConfirmText { get; init; } // buffer only formatted when the player is found
        public int MessageColor => NativeGmPlayerAdminCommands.ColorConfirm;
    }

    public static class NativeGmChgPkZero
    {
        public static ChgPkZeroOutcome Evaluate(bool playerFound)
            => playerFound
                ? new ChgPkZeroOutcome { Branch = ChgPkZeroBranch.Cleared, PkSetToZero = true, RefreshesAppearance = true, SendsConfirmText = true }
                : new ChgPkZeroOutcome { Branch = ChgPkZeroBranch.PlayerNotFound, PkSetToZero = false, RefreshesAppearance = false, SendsConfirmText = false };
    }

    // ===================== InComePk (idx 91, perm 4) =====================
    // case @0x00624EC3 -> sub_73F4BC(self, 0x64).  self[+0x160] += 100.  If the /100 bucket changed
    // AND the new bucket <= 2, refresh appearance (sub_767548 — PK name-colour tier). No SysMsg.
    // "增加自身PK值100点  @InComePk"  (operates on the GM only; the +100 is a hard-coded immediate)
    public sealed class InComePkOutcome
    {
        public int OldPkPoint { get; init; }
        public int NewPkPoint { get; init; }
        public bool RefreshesAppearance { get; init; }
        public bool SendsSysMsg => false;
    }

    public static class NativeGmInComePk
    {
        public static InComePkOutcome Evaluate(int currentPkPoint)
        {
            int oldBucket = currentPkPoint / 100;
            int updated = currentPkPoint + NativeGmPlayerAdminCommands.InComePkStep;
            int newBucket = updated / 100;
            bool refresh = newBucket != oldBucket && newBucket <= 2;
            return new InComePkOutcome { OldPkPoint = currentPkPoint, NewPkPoint = updated, RefreshesAppearance = refresh };
        }
    }

    // ===================== ChgBodyLuck (idx 92, perm 4) =====================
    // case @0x00624ED5: player = FindPlayerByName(p0).
    //   not found -> SysMsg(ColorRed, format(charName, dword_62BE90)).
    //   found     -> delta = Str_ToInt(p1); the case passes delta*0x1F4 to sub_7698BC which divides
    //                by 500.0, rounds (sub_403580), and ADDS to player[+0x164], then CLAMPS to
    //                [-10, +5].  Success SysMsg(ColorConfirm) echoes the RAW delta (not the clamped
    //                result).  "增加角色防御幸运值  @ChgBodyLuck 角色名 幸运值"
    // (Contrast with the live ChgBodyLuckCommand.cs, which SETS luck = value*5000 and messages the
    //  target — both wrong; see header.)
    public enum ChgBodyLuckBranch { Applied, PlayerNotFound }

    public sealed class ChgBodyLuckOutcome
    {
        public ChgBodyLuckBranch Branch { get; init; }
        public bool LuckApplied { get; init; }
        public int NewLuck { get; init; }           // clamped result actually stored at +0x164
        public bool SendsSysMsg => true;
        public int MessageColor { get; init; }
    }

    public static class NativeGmChgBodyLuck
    {
        public static ChgBodyLuckOutcome Evaluate(bool playerFound, int currentLuck, int delta)
        {
            if (!playerFound)
                return new ChgBodyLuckOutcome { Branch = ChgBodyLuckBranch.PlayerNotFound, LuckApplied = false, NewLuck = currentLuck, MessageColor = NativeGmPlayerAdminCommands.ColorRed };

            int applied = NativeGmPlayerAdminCommands.Clamp(
                currentLuck + delta, NativeGmPlayerAdminCommands.BodyLuckMin, NativeGmPlayerAdminCommands.BodyLuckMax);
            return new ChgBodyLuckOutcome { Branch = ChgBodyLuckBranch.Applied, LuckApplied = true, NewLuck = applied, MessageColor = NativeGmPlayerAdminCommands.ColorConfirm };
        }
    }

    // ===================== ChgmanKind (idx 97, perm 4) =====================
    // case @0x00625008 -> sub_6BE358(self, p0=jobName).  Compares jobName against 4 job-name
    // constants; on the first match sets player[+0x72] = matchedIndex (0..3), formats a confirm
    // (dword_6BE460), SysMsg(ColorConfirm), then re-runs the stat recalc (vtbl[0x240] with the
    // current level).  If jobName matches NONE of the 4 -> silent no-op (no set, no message).
    // "更改自身职业  @ChgmanKind 职业名"
    public enum ChgManKindBranch { JobChanged, UnknownJob }

    public sealed class ChgManKindOutcome
    {
        public ChgManKindBranch Branch { get; init; }
        public bool JobSet { get; init; }
        public int NewJob { get; init; }            // 0..3 when set, -1 otherwise
        public bool RecalculatesStats { get; init; } // vtbl+0x240
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmChgManKind
    {
        // matchedJobIndex: 0..3 for a matched job-name, or negative when nothing matched.
        public static ChgManKindOutcome Evaluate(int matchedJobIndex)
        {
            if (matchedJobIndex < 0 || matchedJobIndex >= NativeGmPlayerAdminCommands.JobCount)
                return new ChgManKindOutcome { Branch = ChgManKindBranch.UnknownJob, JobSet = false, NewJob = -1, RecalculatesStats = false, SendsSysMsg = false, MessageColor = 0 };
            return new ChgManKindOutcome { Branch = ChgManKindBranch.JobChanged, JobSet = true, NewJob = matchedJobIndex, RecalculatesStats = true, SendsSysMsg = true, MessageColor = NativeGmPlayerAdminCommands.ColorConfirm };
        }
    }

    // ===================== ChgSex (idx 98, perm 4) =====================
    // case @0x00625018 -> sub_6D77F0(self).  self[+0x71] = !(self[+0x71] & 1)  (toggle gender bit 0),
    // then SysMsg(ColorConfirm, dword_6D781C).  No args; operates on the GM's own character.
    // "更改自身性别  @ChgSex"
    public sealed class ChgSexOutcome
    {
        public bool TogglesGender => true;          // player+0x71 bit 0
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmPlayerAdminCommands.ColorConfirm;
    }

    public static class NativeGmChgSex
    {
        public static ChgSexOutcome Evaluate() => new();
    }

    // ===================== ChgHideState (idx 102, perm 4) =====================
    // case @0x00625076: self[+0x2E4] ^= 1  (toggle invisibility bit 0); then vtbl[0x1A8] with
    // (dl=0x17, cx=1, push 0) to broadcast the changed state.  No args, NO SysMsg.
    // "关闭/开启自身隐身状态  @ChgHideState"
    public sealed class ChgHideStateOutcome
    {
        public bool TogglesHideFlag => true;        // player+0x2E4 bit 0
        public bool BroadcastsState => true;        // vtbl+0x1A8, feature 0x17
        public bool SendsSysMsg => false;
    }

    public static class NativeGmChgHideState
    {
        public static ChgHideStateOutcome Evaluate() => new();
    }

    // ===================== IncSelfLv (idx 104, perm 4) =====================
    // case @0x006250AB: lvl = Str_ToInt(p0, default 1); lvl = min(max(lvl,1),500) via the
    // sub_4C7004/sub_4C700C clamp pair (help: "最大等级为500").  Writes player[+0x278]=lvl and mirror
    // player[+0x1FC]=lvl, then OnLevelChanged(vtbl[0x240], old, new) to recompute stats. No SysMsg.
    // "提升自身等级(最大等级为500)  @IncSelfLv 等级数"
    public sealed class IncSelfLvOutcome
    {
        public int RequestedLevel { get; init; }
        public int NewLevel { get; init; }          // clamped to [1, 500]
        public bool RecalculatesStats => true;      // vtbl+0x240
        public bool SendsSysMsg => false;
    }

    public static class NativeGmIncSelfLv
    {
        public static IncSelfLvOutcome Evaluate(int requestedLevel)
        {
            int clamped = NativeGmPlayerAdminCommands.Clamp(
                requestedLevel, NativeGmPlayerAdminCommands.LevelFloor, NativeGmPlayerAdminCommands.LevelHardCap);
            return new IncSelfLvOutcome { RequestedLevel = requestedLevel, NewLevel = clamped };
        }
    }

    // ===================== PlayerRename (idx 105, perm 4) =====================
    // case @0x00625114 -> sub_6C1FDC(self, p0=charName).
    //   charName empty -> usage-hint SysMsg(ColorConfirm, "@PlayerRename <PlayerName>").
    //   found          -> grant the player a rename chance (sub_6C53B8), confirm SysMsg(ColorConfirm),
    //                     notify the target (sub_79DF74).
    //   not found      -> SysMsg(ColorRed, format(charName, dword_6C2114)).
    // "设置某位玩家有更改自己名字的机会  @PlayerRename 角色名"
    public enum PlayerRenameBranch { UsageHint, Granted, PlayerNotFound }

    public sealed class PlayerRenameOutcome
    {
        public PlayerRenameBranch Branch { get; init; }
        public bool GrantsRenameChance { get; init; }
        public bool SendsSysMsg => true;
        public int MessageColor { get; init; }
    }

    public static class NativeGmPlayerRename
    {
        public static PlayerRenameOutcome Evaluate(string charName, bool playerFound)
        {
            if (string.IsNullOrEmpty(charName))
                return new PlayerRenameOutcome { Branch = PlayerRenameBranch.UsageHint, GrantsRenameChance = false, MessageColor = NativeGmPlayerAdminCommands.ColorConfirm };
            if (!playerFound)
                return new PlayerRenameOutcome { Branch = PlayerRenameBranch.PlayerNotFound, GrantsRenameChance = false, MessageColor = NativeGmPlayerAdminCommands.ColorRed };
            return new PlayerRenameOutcome { Branch = PlayerRenameBranch.Granted, GrantsRenameChance = true, MessageColor = NativeGmPlayerAdminCommands.ColorConfirm };
        }
    }

    // ===================== Relive (idx 193, perm 4) =====================
    // case @0x006259FB: sub_772DA8(self) just returns self[+0x74] (the death flag).
    //   flag == 0 (alive) -> silent no-op.
    //   flag != 0 (dead)  -> queue the DELAYED-REVIVE message, ident 0x27B1 (=10161), with a
    //                        500 ms delay (@0x625A3E `push 0x1F4`) via sub_766060 @0x625A4D,
    //                        carrying the player's map (self+0x134) / Y (self+0x148,
    //                        pushed first) / X (self+0x144) plus `push 0xE` and `push 1`.
    //                        sub_766060 performs the actual revive (respawn / restore).
    //                        "重生  @Relive"
    // NOTE: 0x27B1 is the message IDENT, not a colour — sub_766060 @0x766069 does
    //       `mov word [ebp-6],cx` and @0x76608E `mov word [ebx],ax`, storing cx as the
    //       queued record's ident.  See DelayedReviveIdent.
    public enum ReliveBranch { NotDead, Revived }

    public sealed class ReliveOutcome
    {
        public ReliveBranch Branch { get; init; }
        public bool PerformsRevive { get; init; }   // sub_766060
        public bool SendsNotice { get; init; }       // ident 0x27B1, 500 ms delay
        public bool SendsSysMsg => false;
    }

    public static class NativeGmRelive
    {
        public static ReliveOutcome Evaluate(bool selfIsDead)
            => selfIsDead
                ? new ReliveOutcome { Branch = ReliveBranch.Revived, PerformsRevive = true, SendsNotice = true }
                : new ReliveOutcome { Branch = ReliveBranch.NotDead, PerformsRevive = false, SendsNotice = false };
    }
}
