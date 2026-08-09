namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of the SKILL / HERO / EQUIP GM command family, reversed 1:1 from the original
    // Delphi M2Server. NOT wired into the live command table — the ten live commands remain the
    // fail-closed stubs in GameSvr/Command/Commands/*Command.cs. This type only *describes* the
    // exact original contract so an AuditTools check can lock it, and so a future port can reproduce
    // it precisely instead of guessing.
    //
    // Evidence (IDA/Hex-Rays over the unpacked image M2Server_unpacked_fixed.exe = m2full.i64,
    // image base 0x00400000):
    //
    //   GM command dispatch is a SINGLE switch. Entry chain:
    //     sub_6D7D68 (main GM processor) -> sub_6BB2F8 -> sub_622820 (the switch).
    //   sub_622820 @0x00622820:
    //     * splits "@name a:b,c d" into name + up to 6 params (delimiters ':',',',' ').
    //     * esi = sub_621F28(player, name, callerPermission, &requiredPerm)   ; command-index lookup
    //         sub_621F28 @0x00621F28: sub_49F5F4() returns the runtime command record;
    //           record[+0x1C]=requiredPermission (out), record[+0x18]=dispatchIndex;
    //           returns index only when callerPermission >= requiredPermission, else 0.
    //         (record[+0x18]/[+0x1C] are the STATIC typed-constant fields at name+0x18 / name+0x1C.)
    //     * cmp esi, 0x2EE (750) ; ja default ; jmp jpt_622B15[esi*4]   (table @0x00622B1C, 752 slots)
    //   Default case def_622B15 @0x0062B648: sets "handled"=0, runs the string-cleanup epilogue,
    //     returns not-handled. No effect, no message, no plugin forward.
    //
    //   Only sub_621F28 -> only sub_622820: there is no second dispatcher. A command whose index
    //   lands on def_622B15 is registered (name+index+permission+help) but is a NO-OP in this build.
    //
    // Per-command facts (Name is the exact table spelling; Index=static+0x18; Perm=static+0x1C):
    //   IMPLEMENTED (distinct case in sub_622820):
    //     AddSkillExp        idx 312 perm 5  case@0x006271C4 -> AddSkillExp core sub_744D4C
    //     ChgHeroSkill       idx 228 perm 5  case@0x006261D8 -> sub_6D2E08 (-> hero ChgSkillLv sub_73F500)
    //     SmeltEquip         idx 272 perm 5  case@0x00626B27 -> sub_6E3670 (item refine)
    //     DecEquipDura       idx 549 perm 4  case@0x00623BDE -> sub_6F3324 (all 16 equip slots)
    //   REGISTERED BUT UNIMPLEMENTED (index maps to def_622B15 / no-op):
    //     ChgSuperSKilllv        idx 494 perm 4   help "GM改变自身强化技能重数"
    //     chgFourthSkillState    idx 328 perm 4   help "打开/关闭玩家或玩家的英雄第四招连击"
    //     DelSSKSkill            idx 240 perm 5   help "删除GM自身或GM自己的英雄的连击技能"
    //     EquipDropProtectOne    idx 469 perm 4   help "设置玩家最大死亡保护次数"
    //     SetEquipComposelv      idx 498 perm 5   help "设置合成装备等级"
    //     ClearEquipCompose      idx 500 perm 5   help "清除装备属性"
    //
    // Struct offsets touched by the implemented handlers (player = TPlayObject, item = TUserItem):
    //     player + 0x0BB0  hero object pointer            (C#: TPlayObject.m_HeroObject)
    //     object + 0x73    ghost/deleted byte (skip flag)
    //     player + 0x500   skill/magic list              (AddSkillExp core iterates this)
    //     item   + 0x26    durability WORD               (C#: TUserItem.Dura)   [DecEquipDura writes this]
    //     item   + 0x1C    std-item definition pointer;  refine reads def+0x2E/0x32/0x36 (dc/mc/sc ranges)
    //     item   + 0x2C/0x2D/0x2E  per-attribute refine level; item + 0x37 refine count [SmeltEquip writes]
    // ------------------------------------------------------------------------------------------------

    public enum GmSkillEquipCommand
    {
        AddSkillExp,
        ChgHeroSkill,
        SmeltEquip,
        DecEquipDura,
        ChgSuperSkillLv,
        ChgFourthSkillState,
        DelSskSkill,
        EquipDropProtectOne,
        SetEquipComposeLv,
        ClearEquipCompose,
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C).</summary>
    public sealed class GmSkillEquipCommandInfo
    {
        public GmSkillEquipCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (static record +0x18, == runtime record +0x18 used by the switch).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (static record +0x1C). Original value; the C# stubs use 10.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>True when sub_622820 has a real case; false when the index falls on def_622B15.</summary>
        public bool Implemented { get; init; }
        /// <summary>Address of the case block (implemented) or the shared default label (0x0062B648).</summary>
        public uint CaseAddress { get; init; }
    }

    public static class NativeGmSkillEquipCommands
    {
        // dispatcher constants
        public const uint DispatcherEa = 0x00622820;          // sub_622820
        public const uint IndexLookupEa = 0x00621F28;         // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;           // jpt_622B15
        public const int SwitchMaxIndex = 750;                // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;         // def_622B15 (no-op)

        // struct offsets (see header)
        public const int HeroFieldOffset = 0x0BB0;
        public const int GhostFlagOffset = 0x73;
        public const int SkillListOffset = 0x500;
        public const int ItemDuraOffset = 0x26;
        public const int ItemStdDefOffset = 0x1C;
        public const int ItemRefineCountOffset = 0x37;

        // observed SysMsg colours (cx immediates) used by the implemented handlers
        public const int ColorError = 0x38FF;                 // player/hero offline, over-max
        public const int ColorSuccess = 0xFFDB;              // ChgHeroSkill success, smelt notices

        // exact GBK response strings that were resolvable in the image
        public const string MsgPlayerOffline = " 不在线 或者不在本服务器"; // dword_6D3008 (prefixed with name)
        public const string MsgHeroOffline = " 的英雄不在线";              // dword_6D2FF0 (prefixed with name)
        public const string MsgSmeltOverMax = "超过最大升级次数";          // dword_6E3794
        public const string MsgSmeltNotFound = "找不到要精炼的物品";        // dword_6E37B0
        public const string MsgSmeltDone = "极品装备升级";                // dword_6E377C (success notice)

        private static readonly GmSkillEquipCommandInfo[] Registry =
        {
            new() { Command = GmSkillEquipCommand.AddSkillExp,         Name = "AddSkillExp",         DispatchIndex = 312, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006271C4 },
            new() { Command = GmSkillEquipCommand.ChgHeroSkill,        Name = "ChgHeroSkill",        DispatchIndex = 228, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006261D8 },
            new() { Command = GmSkillEquipCommand.SmeltEquip,          Name = "SmeltEquip",          DispatchIndex = 272, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00626B27 },
            new() { Command = GmSkillEquipCommand.DecEquipDura,        Name = "DecEquipDura",        DispatchIndex = 549, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00623BDE },
            new() { Command = GmSkillEquipCommand.ChgSuperSkillLv,     Name = "ChgSuperSKilllv",     DispatchIndex = 494, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa },
            new() { Command = GmSkillEquipCommand.ChgFourthSkillState, Name = "chgFourthSkillState", DispatchIndex = 328, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa },
            new() { Command = GmSkillEquipCommand.DelSskSkill,         Name = "DelSSKSkill",         DispatchIndex = 240, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa },
            new() { Command = GmSkillEquipCommand.EquipDropProtectOne, Name = "EquipDropProtectOne", DispatchIndex = 469, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa },
            new() { Command = GmSkillEquipCommand.SetEquipComposeLv,   Name = "SetEquipComposelv",   DispatchIndex = 498, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa },
            new() { Command = GmSkillEquipCommand.ClearEquipCompose,   Name = "ClearEquipCompose",   DispatchIndex = 500, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa },
        };

        public static GmSkillEquipCommandInfo Info(GmSkillEquipCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmSkillEquipCommandInfo> All => Registry;

        /// <summary>
        /// Contract for the six registered-but-unimplemented commands: recognized by the table
        /// (valid index + permission), permission-gated, but the switch lands on def_622B15 — so
        /// nothing is mutated and nothing is sent back. Faithful behaviour is a silent no-op.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(GmSkillEquipCommand command)
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
    }

    public sealed class NativeGmDefaultNoOp
    {
        public bool Recognized { get; init; }
        public bool DispatchesToDefaultCase { get; init; }
        public bool MutatesState { get; init; }
        public bool SendsResponse { get; init; }
    }

    // ===================== AddSkillExp (idx 312) =====================
    // "@AddSkillExp 角色名 技能名字 技能经验 [空:主号 / 非空:召唤出的英雄]"
    // case @0x006271C4: p0 charName must be non-empty; player = FindPlayerByName(list@off_7D6D50, p0)
    //   must exist; p1 skillName must be non-empty; p3 heroFlag empty -> target=player, else
    //   target = player[+0xBB0] (must exist). Then sub_744D4C(target, skillName, exp) applies exp
    //   (the core method only acts when exp>0, matches the skill by name in target[+0x500], adds exp,
    //   may auto-advance the level, and pushes a client skill update). No SysMsg to the GM on any path.
    public enum AddSkillExpBranch
    {
        CharNameEmpty,
        PlayerNotFound,
        SkillNameEmpty,
        HeroMissing,
        AppliedToPlayer,
        AppliedToHero,
    }

    public sealed class AddSkillExpOutcome
    {
        public AddSkillExpBranch Branch { get; init; }
        public bool CallsCore { get; init; }
        /// <summary>Whether the resolved target is the hero (+0xBB0) vs. the player itself.</summary>
        public bool TargetsHero { get; init; }
        /// <summary>Core sub_744D4C only changes exp when exp&gt;0; false here means a guaranteed no-op call.</summary>
        public bool CoreAppliesExp { get; init; }
        public bool SendsSysMsg => false;
    }

    public static class NativeGmAddSkillExp
    {
        public static AddSkillExpOutcome Evaluate(
            string charName, bool playerFound, string skillName, bool heroFlag, bool heroPresent, int exp)
        {
            if (string.IsNullOrEmpty(charName))
                return Fail(AddSkillExpBranch.CharNameEmpty);
            if (!playerFound)
                return Fail(AddSkillExpBranch.PlayerNotFound);
            if (string.IsNullOrEmpty(skillName))
                return Fail(AddSkillExpBranch.SkillNameEmpty);
            if (heroFlag && !heroPresent)
                return Fail(AddSkillExpBranch.HeroMissing);

            return new AddSkillExpOutcome
            {
                Branch = heroFlag ? AddSkillExpBranch.AppliedToHero : AddSkillExpBranch.AppliedToPlayer,
                CallsCore = true,
                TargetsHero = heroFlag,
                CoreAppliesExp = exp > 0,
            };
        }

        private static AddSkillExpOutcome Fail(AddSkillExpBranch b) =>
            new() { Branch = b, CallsCore = false, TargetsHero = false, CoreAppliesExp = false };
    }

    // ===================== ChgHeroSkill (idx 228) =====================
    // "@ChgHeroSkill 角色名 技能名字 技能等级"  -> sub_6D2E08(self, p0, p1, p2)
    //   guard: p0 (charName) AND p1 (skillName) must be non-empty, else silent no-op.
    //   player = FindPlayerByName(p0):
    //     null or player[+0x73] ghost      -> SysMsg(name + MsgPlayerOffline, ColorError)
    //     hero player[+0xBB0] null/ghost   -> SysMsg(name + MsgHeroOffline,   ColorError)
    //     hero.ChgSkillLv(skillName,lv,..) true  -> SysMsg(success text, ColorSuccess)
    //     else                                    -> SysMsg(failure text, ColorError)
    public enum ChgHeroSkillBranch
    {
        ArgsEmpty,
        PlayerOffline,
        HeroOffline,
        Success,
        Failure,
    }

    public sealed class ChgHeroSkillOutcome
    {
        public ChgHeroSkillBranch Branch { get; init; }
        public bool SendsSysMsg { get; init; }
        /// <summary>SysMsg colour (0 when no message is sent).</summary>
        public int MessageColor { get; init; }
        public bool CallsChgSkillLv { get; init; }
    }

    public static class NativeGmChgHeroSkill
    {
        public static ChgHeroSkillOutcome Evaluate(
            string charName, string skillName, bool playerFound, bool playerGhost,
            bool heroPresent, bool heroGhost, bool chgSkillLvResult)
        {
            if (string.IsNullOrEmpty(charName) || string.IsNullOrEmpty(skillName))
                return new ChgHeroSkillOutcome { Branch = ChgHeroSkillBranch.ArgsEmpty, SendsSysMsg = false, MessageColor = 0, CallsChgSkillLv = false };

            if (!playerFound || playerGhost)
                return Msg(ChgHeroSkillBranch.PlayerOffline, NativeGmSkillEquipCommands.ColorError, false);
            if (!heroPresent || heroGhost)
                return Msg(ChgHeroSkillBranch.HeroOffline, NativeGmSkillEquipCommands.ColorError, false);

            return chgSkillLvResult
                ? Msg(ChgHeroSkillBranch.Success, NativeGmSkillEquipCommands.ColorSuccess, true)
                : Msg(ChgHeroSkillBranch.Failure, NativeGmSkillEquipCommands.ColorError, true);
        }

        private static ChgHeroSkillOutcome Msg(ChgHeroSkillBranch b, int color, bool called) =>
            new() { Branch = b, SendsSysMsg = true, MessageColor = color, CallsChgSkillLv = called };
    }

    // ===================== SmeltEquip (idx 272) =====================
    // "@SmeltEquip 物品ID 精炼的次数"  -> sub_6E3670(self, itemId, count)
    //   itemId/count = Str_ToInt(...); itemId == -1 -> silent no-op.
    //   item = FindItemById(self, itemId) [sub_73D028]:
    //     not found            -> SysMsg(MsgSmeltNotFound, ColorSuccess-channel 0xFFDB)
    //     count > maxUpgrade   -> SysMsg(MsgSmeltOverMax,  0xFFDB)     [max from sub_406A88]
    //     else                 -> writes refine level=count into the item's dominant-attribute slot
    //                             (+0x2C/0x2D/0x2E), item[+0x37]=count, recalc, notice MsgSmeltDone.
    public enum SmeltEquipBranch
    {
        InvalidItemId,
        ItemNotFound,
        OverMaxCount,
        Applied,
    }

    public sealed class SmeltEquipOutcome
    {
        public SmeltEquipBranch Branch { get; init; }
        public bool MutatesItem { get; init; }
        public bool SendsSysMsg { get; init; }
    }

    public static class NativeGmSmeltEquip
    {
        public static SmeltEquipOutcome Evaluate(bool itemIdValid, bool itemFound, int count, int maxCount)
        {
            if (!itemIdValid)
                return new SmeltEquipOutcome { Branch = SmeltEquipBranch.InvalidItemId, MutatesItem = false, SendsSysMsg = false };
            if (!itemFound)
                return new SmeltEquipOutcome { Branch = SmeltEquipBranch.ItemNotFound, MutatesItem = false, SendsSysMsg = true };
            if (count > maxCount)
                return new SmeltEquipOutcome { Branch = SmeltEquipBranch.OverMaxCount, MutatesItem = false, SendsSysMsg = true };
            return new SmeltEquipOutcome { Branch = SmeltEquipBranch.Applied, MutatesItem = true, SendsSysMsg = true };
        }
    }

    // ===================== DecEquipDura (idx 549) =====================
    // "@DecEquipDura <value>"  -> sub_6F3324(self, value)
    //   value = Str_ToInt(p0). For slot 0..15: item = GetUseItems(self, slot) [sub_75EC20];
    //   if item != null: item.Dura(+0x26) = value.  Operates on the invoking GM only; no SysMsg.
    public sealed class DecEquipDuraOutcome
    {
        public bool TargetsSelf => true;
        public bool AffectsAllEquipSlots => true;
        public int EquipSlotCount => 16;
        public int DuraWrittenOffset => NativeGmSkillEquipCommands.ItemDuraOffset;
        public int DuraValue { get; init; }
        /// <summary>Number of occupied slots actually written (given how many are equipped).</summary>
        public int SlotsWritten { get; init; }
        public bool SendsSysMsg => false;
    }

    public static class NativeGmDecEquipDura
    {
        public static DecEquipDuraOutcome Evaluate(int value, int occupiedEquipSlots)
        {
            if (occupiedEquipSlots < 0) occupiedEquipSlots = 0;
            if (occupiedEquipSlots > 16) occupiedEquipSlots = 16;
            return new DecEquipDuraOutcome { DuraValue = value, SlotsWritten = occupiedEquipSlots };
        }
    }
}
