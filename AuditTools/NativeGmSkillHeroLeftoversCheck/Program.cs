// NativeGmSkillHeroLeftoversCheck
//
// Pins the two dormant models of the family-02 SKILL/FORCE/MERIDIAN and family-03
// HERO/FIELDHERO leftovers (the records NOT already covered by
// NativeGmSkillEquipCommands.cs / NativeGmHeroPetCommands.cs):
//   GameSvr/Services/NativeGmSkillMeridianCommands.cs  (10 records)
//   GameSvr/Services/NativeGmHeroFieldCommands.cs       (18 records)
// against the reversed binary facts (registry + per-case branch ladders + SysMsg
// idents), for the M2Server dispatcher sub_622820 @0x00622820.
//
// Evidence: staging/update_clothes_4637_ida_work/{disp_decomp.txt, big622820.txt,
// world_scan_out.txt} over m2full.i64 (SHA256 5540f43b…c049670b14e, base 0x00400000).

using GameSvr;

int checks = 0;
void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"FAIL {label}: expected [{expected}], actual [{actual}]");
}

// ===========================================================================
// A) family 02 SKILL / FORCE / MERIDIAN
// ===========================================================================
Equal(0x0062B648u, NativeSkillMeridianCommand.DefaultHandler, "skill DefaultHandler");
Equal(0x007D5E58u, NativeGmSkillMeridianCommands.UnionMaxLvGlobalEa, "SetUnionMaxLv global off_7D5E58");
Equal(5, NativeGmSkillMeridianCommands.UnionMaxLvMin, "union lv min 5");
Equal(10, NativeGmSkillMeridianCommands.UnionMaxLvMax, "union lv max 10");
Equal(0x0073F500u, NativeGmSkillMeridianCommands.ChgSelfSkillLvCoreEa, "ChgSelfSkillLv sub_73F500");
Equal(0x006CDCA8u, NativeGmSkillMeridianCommands.SetNoSkillZoneCoreEa, "SetNoSkillZone sub_6CDCA8");
Equal(0x007907B4u, NativeGmSkillMeridianCommands.FastnessReloadEa, "FASTNESS sub_7907B4");
Equal(0x00748338u, NativeGmSkillMeridianCommands.ClearColdTimeCoreEa, "ClearColdTime sub_748338");

var skill = new (string Name, int Idx, int Perm, uint Handler, bool Impl)[]
{
    ("ChgSelfSkillLv", 94,  4, 0x00624FBBu, true),
    ("DelSelfSkill",   95,  4, 0x00624FE8u, true),
    ("UpUserSkill",    218, 5, 0x00625F6Au, true),
    ("DelUserSkill",   219, 5, 0x00625F9Au, true),
    ("SetNoSkillZone", 393, 5, 0x006286D5u, true),
    ("SetUnionMaxLv",  406, 5, 0x006289AAu, true),
    ("FASTNESS",       480, 4, 0x00628FC2u, true),
    ("ClearColdTime",  547, 4, 0x006297DEu, true),
    ("SetSmjd",        323, 5, 0x0062B648u, false),
    ("LearnSuperForce",445, 4, 0x0062B648u, false),
};
Equal(10, skill.Length, "skill family size");
Equal(skill.Length, NativeGmSkillMeridianCommands.All.Count, "skill modeled count");
int skImpl = 0;
foreach (var e in skill)
{
    var c = NativeGmSkillMeridianCommands.Find(e.Name);
    Equal(true, c != null, $"skill has {e.Name}");
    Equal(e.Idx, c.DispatchIndex, $"{e.Name}.Idx");
    Equal(e.Perm, c.RequiredPerm, $"{e.Name}.Perm");
    Equal(e.Handler, c.HandlerAddress, $"{e.Name}.Handler");
    Equal(e.Impl, c.Implemented, $"{e.Name}.Implemented");
    Equal(NativeSkillMeridianCommand.JumpTableBase + (uint)e.Idx * 4, c.JumpSlotAddress, $"{e.Name}.JumpSlot");
    if (e.Impl) skImpl++;
}
Equal(8, skImpl, "skill impl count");

Equal(NativeSkillMeridianOutcome.PermissionRejected,
    NativeGmSkillMeridianCommands.Evaluate("SetUnionMaxLv", 4, null).Outcome, "SetUnionMaxLv perm4<5");
foreach (var n in new[] { "SetSmjd", "LearnSuperForce" })
{
    Equal(NativeSkillMeridianOutcome.SilentNoOp,
        NativeGmSkillMeridianCommands.Evaluate(n, 10, null).Outcome, $"{n} -> SilentNoOp");
    var d = NativeGmSkillMeridianCommands.EvaluateUnimplemented(n);
    Equal(true, d.Recognized, $"{n} NoOp.Recognized");
    Equal(false, d.SendsResponse, $"{n} NoOp.SendsResponse");
}

// delegations
Equal("sub_73F500", NativeGmSkillMeridianCommands.Evaluate("ChgSelfSkillLv", 4, new[] { "fire", "5", "0" }).NativeCore, "ChgSelfSkillLv core");
Equal(NativeSkillMeridianOutcome.Executed, NativeGmSkillMeridianCommands.Evaluate("ClearColdTime", 4, null).Outcome, "ClearColdTime Executed");

// SetNoSkillZone ladder
var nsz = NativeGmSkillMeridianCommands.Evaluate("SetNoSkillZone", 5, new[] { "10", "20", "5", "8", "on" });
Equal(NativeSkillMeridianOutcome.Executed, nsz.Outcome, "SetNoSkillZone valid -> Executed");
Equal("sub_6CDCA8", nsz.NativeCore, "SetNoSkillZone core");
Equal(NativeSkillMeridianOutcome.RejectedWithGmMessage,
    NativeGmSkillMeridianCommands.Evaluate("SetNoSkillZone", 5, new[] { "10", "20", "5", "8", "bad" }).Outcome, "SetNoSkillZone bad token");
Equal(NativeSkillMeridianOutcome.RejectedWithGmMessage,
    NativeGmSkillMeridianCommands.Evaluate("SetNoSkillZone", 5, new[] { "-1", "20", "5", "8", "on" }).Outcome, "SetNoSkillZone neg coord");
Equal(0xFFDB,
    NativeGmSkillMeridianCommands.Evaluate("SetNoSkillZone", 5, new[] { "-1", "20", "5", "8", "on" }).NativeSysMsgIdent, "SetNoSkillZone err ident 0xFFDB");

// SetUnionMaxLv ladder
NativeGmSkillMeridianCommands.UnionConfigExists = false;
Equal("no-config", NativeGmSkillMeridianCommands.Evaluate("SetUnionMaxLv", 5, new[] { "7" }).Branch, "SetUnionMaxLv no-config");
NativeGmSkillMeridianCommands.UnionConfigExists = true;
var um = NativeGmSkillMeridianCommands.Evaluate("SetUnionMaxLv", 5, new[] { "7" });
Equal(NativeSkillMeridianOutcome.ExecutedWithGmMessage, um.Outcome, "SetUnionMaxLv 7 -> msg");
Equal(0xFFDB, um.NativeSysMsgIdent, "SetUnionMaxLv set ident 0xFFDB");
Equal(NativeSkillMeridianOutcome.RejectedWithGmMessage,
    NativeGmSkillMeridianCommands.Evaluate("SetUnionMaxLv", 5, new[] { "11" }).Outcome, "SetUnionMaxLv 11 out-of-range");
Equal(0x38FF,
    NativeGmSkillMeridianCommands.Evaluate("SetUnionMaxLv", 5, new[] { "4" }).NativeSysMsgIdent, "SetUnionMaxLv 4 usage ident");

// FASTNESS
var fn = NativeGmSkillMeridianCommands.Evaluate("FASTNESS", 4, new[] { "UNION" });
Equal(NativeSkillMeridianOutcome.ExecutedWithGmMessage, fn.Outcome, "FASTNESS -> msg");
Equal(0xFFDB, fn.NativeSysMsgIdent, "FASTNESS ident 0xFFDB");
Equal("sub_7907B4", fn.NativeCore, "FASTNESS reload core");

// ===========================================================================
// B) family 03 HERO / FIELDHERO
// ===========================================================================
Equal(0x0062B648u, NativeHeroFieldCommand.DefaultHandler, "hero DefaultHandler");
Equal(0x0062B64Cu, NativeHeroFieldCommand.EmptyBodyHandler, "hero EmptyBodyHandler");
Equal(0xBB0, NativeGmHeroFieldCommands.HeroPtrSelfOffset, "hero ptr self+0xBB0");
Equal(0x00688650u, NativeGmHeroFieldCommands.RestHeroCoreEa, "RestHero sub_688650");
Equal(0x006E6EF0u, NativeGmHeroFieldCommands.ChgBreakLevelCoreEa, "ChgBreakLevel sub_6E6EF0");
Equal(0x0074665Cu, NativeGmHeroFieldCommands.LearnSkillCoreEa, "LearnSkill sub_74665C");
Equal(0x006F3284u, NativeGmHeroFieldCommands.HeroAbilCoreEa, "HeroAbil sub_6F3284");

var hero = new (string Name, int Idx, int Perm, uint Handler, bool Impl, bool EmptyBody)[]
{
    ("RestHero",         28,  0, 0x00623AD1u, true,  false),
    ("UpGradeHero",      69,  3, 0x00624AD1u, true,  false),
    ("UpUserHeroExp",    223, 5, 0x00626168u, true,  false),
    ("ChgHeroFealty",    227, 5, 0x006261C5u, true,  false),
    ("ChgBreakLevel",    308, 5, 0x006272B7u, true,  false),
    ("ReloadPromptFile", 367, 4, 0x0062821Du, true,  false),
    ("LearnSkill",       379, 5, 0x006283F2u, true,  false),
    ("HeroAbil",         548, 4, 0x00623BD1u, true,  false),
    ("KingActorVal",     178, 4, 0x0062B648u, false, false),
    ("SetSSKLv",         241, 5, 0x0062B648u, false, false),
    ("SetSSKColdTime",   243, 5, 0x0062B648u, false, false),
    ("UpgradeJM",        244, 5, 0x0062B648u, false, false),
    ("OpenPoint",        245, 5, 0x0062B648u, false, false),
    ("ClearSSKInfo",     247, 5, 0x0062B648u, false, false),
    ("SetForceDB",       441, 5, 0x0062B648u, false, false),
    ("EnableHeroSF",     446, 4, 0x0062B648u, false, false),
    ("HeroHypericumUsed",489, 4, 0x0062B64Cu, false, true),   // empty-body sink
    ("GetTigeScore",     541, 4, 0x0062B648u, false, false),
};
Equal(18, hero.Length, "hero family size");
Equal(hero.Length, NativeGmHeroFieldCommands.All.Count, "hero modeled count");
int hImpl = 0, hEmpty = 0;
foreach (var e in hero)
{
    var c = NativeGmHeroFieldCommands.Find(e.Name);
    Equal(true, c != null, $"hero has {e.Name}");
    Equal(e.Idx, c.DispatchIndex, $"{e.Name}.Idx");
    Equal(e.Perm, c.RequiredPerm, $"{e.Name}.Perm");
    Equal(e.Handler, c.HandlerAddress, $"{e.Name}.Handler");
    Equal(e.Impl, c.Implemented, $"{e.Name}.Implemented");
    Equal(NativeHeroFieldCommand.JumpTableBase + (uint)e.Idx * 4, c.JumpSlotAddress, $"{e.Name}.JumpSlot");
    if (e.Impl) hImpl++;
    if (e.EmptyBody) { hEmpty++; Equal(NativeHeroFieldCommand.EmptyBodyHandler, c.HandlerAddress, $"{e.Name} empty-body sink"); }
}
Equal(8, hImpl, "hero impl count");
Equal(1, hEmpty, "hero empty-body no-op count (HeroHypericumUsed)");

foreach (var n in new[] { "KingActorVal", "SetSSKLv", "SetSSKColdTime", "UpgradeJM", "OpenPoint",
                          "ClearSSKInfo", "SetForceDB", "EnableHeroSF", "HeroHypericumUsed", "GetTigeScore" })
{
    Equal(NativeHeroFieldOutcome.SilentNoOp,
        NativeGmHeroFieldCommands.Evaluate(n, 10, new[] { "a" }).Outcome, $"{n} -> SilentNoOp");
    var d = NativeGmHeroFieldCommands.EvaluateUnimplemented(n);
    Equal(true, d.Recognized, $"{n} NoOp.Recognized");
    Equal(false, d.MutatesState, $"{n} NoOp.MutatesState");
}

// delegations
Equal("sub_6D1E98", NativeGmHeroFieldCommands.Evaluate("UpUserHeroExp", 5, new[] { "bob", "100" }).NativeCore, "UpUserHeroExp core");
Equal("sub_6F3284", NativeGmHeroFieldCommands.Evaluate("HeroAbil", 4, null).NativeCore, "HeroAbil core");

// RestHero
NativeGmHeroFieldCommands.HeroPresent = true; NativeGmHeroFieldCommands.RestHeroBlocked = false;
var rh = NativeGmHeroFieldCommands.Evaluate("RestHero", 0, null);
Equal(NativeHeroFieldOutcome.ExecutedWithGmMessage, rh.Outcome, "RestHero present -> msg");
Equal(0xFCFF, rh.NativeSysMsgIdent, "RestHero ident 0xFCFF");
NativeGmHeroFieldCommands.HeroPresent = false;
Equal(NativeHeroFieldOutcome.RejectedSilently, NativeGmHeroFieldCommands.Evaluate("RestHero", 0, null).Outcome, "RestHero no-hero silent");
NativeGmHeroFieldCommands.HeroPresent = true; NativeGmHeroFieldCommands.RestHeroBlocked = true;
Equal(NativeHeroFieldOutcome.RejectedSilently, NativeGmHeroFieldCommands.Evaluate("RestHero", 0, null).Outcome, "RestHero blocked silent");
NativeGmHeroFieldCommands.RestHeroBlocked = false;

// UpGradeHero
var ug = NativeGmHeroFieldCommands.Evaluate("UpGradeHero", 3, new[] { "3" });
Equal(NativeHeroFieldOutcome.Executed, ug.Outcome, "UpGradeHero present+level -> Executed");
Equal(NativeHeroFieldOutcome.RejectedSilently,
    NativeGmHeroFieldCommands.Evaluate("UpGradeHero", 3, new[] { "0" }).Outcome, "UpGradeHero level0 silent");
NativeGmHeroFieldCommands.HeroPresent = false;
Equal(NativeHeroFieldOutcome.RejectedSilently,
    NativeGmHeroFieldCommands.Evaluate("UpGradeHero", 3, new[] { "3" }).Outcome, "UpGradeHero no-hero silent");
NativeGmHeroFieldCommands.HeroPresent = true;

// ChgBreakLevel
Equal("hero", NativeGmHeroFieldCommands.Evaluate("ChgBreakLevel", 5, new[] { "bob", "1", "5", "hero" }).Branch, "ChgBreakLevel hero");
Equal("main", NativeGmHeroFieldCommands.Evaluate("ChgBreakLevel", 5, new[] { "bob", "1", "5" }).Branch, "ChgBreakLevel main");

// LearnSkill
NativeGmHeroFieldCommands.LearnSkillTargetFound = true;
Equal("hero-learn", NativeGmHeroFieldCommands.Evaluate("LearnSkill", 5, new[] { "fire", "1" }).Branch, "LearnSkill hero-learn");
Equal(0xFCFF, NativeGmHeroFieldCommands.Evaluate("LearnSkill", 5, new[] { "fire", "1" }).NativeSysMsgIdent, "LearnSkill success ident 0xFCFF");
Equal("main-learn", NativeGmHeroFieldCommands.Evaluate("LearnSkill", 5, new[] { "fire", "2" }).Branch, "LearnSkill main-learn");
NativeGmHeroFieldCommands.LearnSkillTargetFound = false;
var lsNf = NativeGmHeroFieldCommands.Evaluate("LearnSkill", 5, new[] { "fire", "1" });
Equal(NativeHeroFieldOutcome.RejectedWithGmMessage, lsNf.Outcome, "LearnSkill not-found -> reject msg");
Equal(0x38FF, lsNf.NativeSysMsgIdent, "LearnSkill not-found ident 0x38FF");
NativeGmHeroFieldCommands.LearnSkillTargetFound = true;

// ReloadPromptFile
var rp = NativeGmHeroFieldCommands.Evaluate("ReloadPromptFile", 4, null);
Equal(NativeHeroFieldOutcome.ExecutedWithGmMessage, rp.Outcome, "ReloadPromptFile -> msg");
Equal(0xFFDB, rp.NativeSysMsgIdent, "ReloadPromptFile ident 0xFFDB");

Console.WriteLine($"PASS NativeGmSkillHeroLeftoversCheck ({checks} checks): "
    + $"{NativeGmSkillMeridianCommands.All.Count} skill/force/meridian (02) + "
    + $"{NativeGmHeroFieldCommands.All.Count} hero/fieldhero (03) leftover GM commands modeled "
    + "(registry, permission ladder, branch ladders, SysMsg idents, dual no-op sinks).");
return 0;
