using GameSvr;

// Contract check for the dormant SKILL/HERO/EQUIP GM command family model
// (GameSvr/Services/NativeGmSkillEquipCommands.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyAddSkillExp();
    VerifyChgHeroSkill();
    VerifySmeltEquip();
    VerifyDecEquipDura();
    VerifyUnimplementedAreNoOps();

    Console.WriteLine(
        "PASS NativeGmSkillEquipCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 " +
        "implemented=AddSkillExp/ChgHeroSkill/SmeltEquip/DecEquipDura " +
        "noop=ChgSuperSkillLv/ChgFourthSkillState/DelSSKSkill/EquipDropProtectOne/SetEquipComposeLv/ClearEquipCompose");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmSkillEquipCommandsCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static void VerifyDispatcherConstants()
{
    Assert(NativeGmSkillEquipCommands.DispatcherEa == 0x00622820, "dispatcher ea");
    Assert(NativeGmSkillEquipCommands.IndexLookupEa == 0x00621F28, "index lookup ea");
    Assert(NativeGmSkillEquipCommands.JumpTableEa == 0x00622B1C, "jump table ea");
    Assert(NativeGmSkillEquipCommands.SwitchMaxIndex == 750, "switch max index");
    Assert(NativeGmSkillEquipCommands.DefaultCaseEa == 0x0062B648, "default case ea");
    Assert(NativeGmSkillEquipCommands.HeroFieldOffset == 0x0BB0, "hero offset");
    Assert(NativeGmSkillEquipCommands.ItemDuraOffset == 0x26, "item dura offset");
    Assert(NativeGmSkillEquipCommands.SkillListOffset == 0x500, "skill list offset");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, implemented)
    (GmSkillEquipCommand cmd, string name, int idx, int perm, bool impl)[] expected =
    {
        (GmSkillEquipCommand.AddSkillExp,         "AddSkillExp",         312, 5, true),
        (GmSkillEquipCommand.ChgHeroSkill,        "ChgHeroSkill",        228, 5, true),
        (GmSkillEquipCommand.SmeltEquip,          "SmeltEquip",          272, 5, true),
        (GmSkillEquipCommand.DecEquipDura,        "DecEquipDura",        549, 4, true),
        (GmSkillEquipCommand.ChgSuperSkillLv,     "ChgSuperSKilllv",     494, 4, false),
        (GmSkillEquipCommand.ChgFourthSkillState, "chgFourthSkillState", 328, 4, false),
        (GmSkillEquipCommand.DelSskSkill,         "DelSSKSkill",         240, 5, false),
        (GmSkillEquipCommand.EquipDropProtectOne, "EquipDropProtectOne", 469, 4, false),
        (GmSkillEquipCommand.SetEquipComposeLv,   "SetEquipComposelv",   498, 5, false),
        (GmSkillEquipCommand.ClearEquipCompose,   "ClearEquipCompose",   500, 5, false),
    };

    Assert(NativeGmSkillEquipCommands.All.Count == expected.Length, "registry count");
    foreach (var e in expected)
    {
        var info = NativeGmSkillEquipCommands.Info(e.cmd);
        Assert(info.Name == e.name, $"{e.cmd} name (got {info.Name})");
        Assert(info.DispatchIndex == e.idx, $"{e.cmd} index (got {info.DispatchIndex})");
        Assert(info.RequiredPermission == e.perm, $"{e.cmd} perm (got {info.RequiredPermission})");
        Assert(info.Implemented == e.impl, $"{e.cmd} implemented flag");
        Assert(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmSkillEquipCommands.SwitchMaxIndex,
            $"{e.cmd} index in switch range");
        // implemented => real case address; unimplemented => default label
        if (e.impl)
            Assert(info.CaseAddress != NativeGmSkillEquipCommands.DefaultCaseEa, $"{e.cmd} has distinct case");
        else
            Assert(info.CaseAddress == NativeGmSkillEquipCommands.DefaultCaseEa, $"{e.cmd} on default case");
    }
}

static void VerifyAddSkillExp()
{
    // validation failures are silent no-ops (no core call, no SysMsg)
    var e1 = NativeGmAddSkillExp.Evaluate("", true, "Fire", false, false, 100);
    Assert(e1.Branch == AddSkillExpBranch.CharNameEmpty && !e1.CallsCore && !e1.SendsSysMsg, "add: empty charname");

    var e2 = NativeGmAddSkillExp.Evaluate("Bob", false, "Fire", false, false, 100);
    Assert(e2.Branch == AddSkillExpBranch.PlayerNotFound && !e2.CallsCore, "add: player not found");

    var e3 = NativeGmAddSkillExp.Evaluate("Bob", true, "", false, false, 100);
    Assert(e3.Branch == AddSkillExpBranch.SkillNameEmpty && !e3.CallsCore, "add: empty skill");

    // hero flag set but no hero -> no-op
    var e4 = NativeGmAddSkillExp.Evaluate("Bob", true, "Fire", true, false, 100);
    Assert(e4.Branch == AddSkillExpBranch.HeroMissing && !e4.CallsCore, "add: hero missing");

    // main-character path
    var e5 = NativeGmAddSkillExp.Evaluate("Bob", true, "Fire", false, false, 100);
    Assert(e5.Branch == AddSkillExpBranch.AppliedToPlayer && e5.CallsCore && !e5.TargetsHero && e5.CoreAppliesExp,
        "add: player path");

    // hero path
    var e6 = NativeGmAddSkillExp.Evaluate("Bob", true, "Fire", true, true, 100);
    Assert(e6.Branch == AddSkillExpBranch.AppliedToHero && e6.CallsCore && e6.TargetsHero, "add: hero path");

    // exp<=0 still calls core but the core exp gate is off (guaranteed no change)
    var e7 = NativeGmAddSkillExp.Evaluate("Bob", true, "Fire", false, false, 0);
    Assert(e7.CallsCore && !e7.CoreAppliesExp, "add: exp<=0 core no-op");

    // no path ever sends a SysMsg to the GM
    foreach (var o in new[] { e1, e2, e3, e4, e5, e6, e7 })
        Assert(!o.SendsSysMsg, "add: never SysMsg");
}

static void VerifyChgHeroSkill()
{
    // empty args -> silent no-op
    var a = NativeGmChgHeroSkill.Evaluate("", "Fire", true, false, true, false, true);
    Assert(a.Branch == ChgHeroSkillBranch.ArgsEmpty && !a.SendsSysMsg && !a.CallsChgSkillLv, "chg: empty args");
    var a2 = NativeGmChgHeroSkill.Evaluate("Bob", "", true, false, true, false, true);
    Assert(a2.Branch == ChgHeroSkillBranch.ArgsEmpty && !a2.SendsSysMsg, "chg: empty skill");

    // player offline / ghost -> error SysMsg, no ChgSkillLv
    var b = NativeGmChgHeroSkill.Evaluate("Bob", "Fire", false, false, true, false, true);
    Assert(b.Branch == ChgHeroSkillBranch.PlayerOffline && b.SendsSysMsg
        && b.MessageColor == NativeGmSkillEquipCommands.ColorError && !b.CallsChgSkillLv, "chg: player offline");
    var b2 = NativeGmChgHeroSkill.Evaluate("Bob", "Fire", true, true, true, false, true);
    Assert(b2.Branch == ChgHeroSkillBranch.PlayerOffline, "chg: player ghost");

    // hero offline / ghost -> error SysMsg, no ChgSkillLv
    var c = NativeGmChgHeroSkill.Evaluate("Bob", "Fire", true, false, false, false, true);
    Assert(c.Branch == ChgHeroSkillBranch.HeroOffline && c.SendsSysMsg && !c.CallsChgSkillLv, "chg: hero offline");
    var c2 = NativeGmChgHeroSkill.Evaluate("Bob", "Fire", true, false, true, true, true);
    Assert(c2.Branch == ChgHeroSkillBranch.HeroOffline, "chg: hero ghost");

    // success -> success colour, ChgSkillLv called
    var d = NativeGmChgHeroSkill.Evaluate("Bob", "Fire", true, false, true, false, true);
    Assert(d.Branch == ChgHeroSkillBranch.Success && d.SendsSysMsg
        && d.MessageColor == NativeGmSkillEquipCommands.ColorSuccess && d.CallsChgSkillLv, "chg: success");

    // failure -> error colour, ChgSkillLv called (returned false)
    var e = NativeGmChgHeroSkill.Evaluate("Bob", "Fire", true, false, true, false, false);
    Assert(e.Branch == ChgHeroSkillBranch.Failure && e.SendsSysMsg
        && e.MessageColor == NativeGmSkillEquipCommands.ColorError && e.CallsChgSkillLv, "chg: failure");
}

static void VerifySmeltEquip()
{
    // invalid item id parse -> silent no-op
    var a = NativeGmSmeltEquip.Evaluate(itemIdValid: false, itemFound: false, count: 1, maxCount: 9);
    Assert(a.Branch == SmeltEquipBranch.InvalidItemId && !a.MutatesItem && !a.SendsSysMsg, "smelt: invalid id");

    // not found -> SysMsg, no mutation
    var b = NativeGmSmeltEquip.Evaluate(true, false, 1, 9);
    Assert(b.Branch == SmeltEquipBranch.ItemNotFound && !b.MutatesItem && b.SendsSysMsg, "smelt: not found");

    // over max -> SysMsg, no mutation
    var c = NativeGmSmeltEquip.Evaluate(true, true, 10, 9);
    Assert(c.Branch == SmeltEquipBranch.OverMaxCount && !c.MutatesItem && c.SendsSysMsg, "smelt: over max");

    // applied -> mutation + notice
    var d = NativeGmSmeltEquip.Evaluate(true, true, 5, 9);
    Assert(d.Branch == SmeltEquipBranch.Applied && d.MutatesItem && d.SendsSysMsg, "smelt: applied");

    // boundary: count == maxCount is allowed (only strictly greater is rejected)
    var e = NativeGmSmeltEquip.Evaluate(true, true, 9, 9);
    Assert(e.Branch == SmeltEquipBranch.Applied, "smelt: count==max allowed");
}

static void VerifyDecEquipDura()
{
    var a = NativeGmDecEquipDura.Evaluate(value: 1, occupiedEquipSlots: 3);
    Assert(a.TargetsSelf && a.AffectsAllEquipSlots && a.EquipSlotCount == 16, "dec: self all slots");
    Assert(a.DuraValue == 1 && a.SlotsWritten == 3, "dec: writes occupied slots");
    Assert(a.DuraWrittenOffset == NativeGmSkillEquipCommands.ItemDuraOffset, "dec: dura offset");
    Assert(!a.SendsSysMsg, "dec: silent");

    // slot count is clamped to 0..16
    Assert(NativeGmDecEquipDura.Evaluate(1, -5).SlotsWritten == 0, "dec: clamp low");
    Assert(NativeGmDecEquipDura.Evaluate(1, 99).SlotsWritten == 16, "dec: clamp high");
}

static void VerifyUnimplementedAreNoOps()
{
    GmSkillEquipCommand[] noop =
    {
        GmSkillEquipCommand.ChgSuperSkillLv,
        GmSkillEquipCommand.ChgFourthSkillState,
        GmSkillEquipCommand.DelSskSkill,
        GmSkillEquipCommand.EquipDropProtectOne,
        GmSkillEquipCommand.SetEquipComposeLv,
        GmSkillEquipCommand.ClearEquipCompose,
    };
    foreach (var c in noop)
    {
        var o = NativeGmSkillEquipCommands.EvaluateUnimplemented(c);
        Assert(o.Recognized && o.DispatchesToDefaultCase, $"{c}: recognized+default");
        Assert(!o.MutatesState && !o.SendsResponse, $"{c}: no effect / no response");
    }

    // implemented commands must NOT be routed through the unimplemented path
    var threw = false;
    try { NativeGmSkillEquipCommands.EvaluateUnimplemented(GmSkillEquipCommand.AddSkillExp); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw, "implemented command rejected by unimplemented path");
}
