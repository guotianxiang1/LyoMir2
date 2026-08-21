using System.Reflection;
using GameSvr;
using GameSvr.CommandSystem;

// Divergence #17 + #17b permanent guard.
//
// Covers the original exact-name census and confirmed name-drift aliases,
// plus commands wired from later binary-verified passes.
//
// Asserts that every name-matched GM command's nPermissionMin equals the
// authoritative native census value (record +0x1C) from
// gm_full_inventory_20260731.md. The execution gate in BaseCommond.Handle
// only enforces nPermissionMin (callerPerm >= requiredPerm), so nPermissionMax
// is intentionally NOT checked here.
//
// Command Types are reflection-loaded from the GameSvr assembly and matched by
// GameCommandAttribute.Name (case-insensitive), which is immune to class-name
// drift. Several commands live on classes whose name differs from the command
// name -- e.g. "Make" is on MakeItemCommond (note the "Commond" typo),
// "AttackMode" on ChangeAttackModeCommand, "Rest" on ChangeSalveStatusCommand --
// so a hardcoded typeof(<Command>Command) list would not even compile.

var assembly = typeof(BaseCommond).Assembly;
Type[] allTypes;
try
{
    allTypes = assembly.GetTypes();
}
catch (ReflectionTypeLoadException ex)
{
    allTypes = ex.Types.Where(type => type != null).ToArray();
}

var commands = new Dictionary<string, (Type Type, int Perm)>(
    StringComparer.OrdinalIgnoreCase);
foreach (var type in allTypes)
{
    var attribute = type.GetCustomAttribute<GameCommandAttribute>();
    if (!string.IsNullOrEmpty(attribute?.Name))
    {
        commands[attribute.Name] = (type, attribute.nPermissionMin);
    }
}

var checks = 0;

// ============================================================
// 80 DIVERGE rows -- live perm corrected to the native census.
// ============================================================

// live 10 -> native 2
Census("SetAP", 2);

// live 10 -> native 3
Census("EquipExchange", 3);
Census("MapUserInfo", 3);
Census("ServerInfo", 3);

// live 10 -> native 4 (GetSysTime was live 0 -> native 4)
Census("BeginAreaCastleMatch", 4);
Census("CallTaskMon", 4);
Census("CellInfo", 4);
Census("ChgBodyLuck", 4);
Census("ChgFourthSkillState", 4);
Census("ChgHideState", 4);
Census("ChgSuperSkillLv", 4);
Census("CreateCampMon", 4);
Census("DecEquipDura", 4);
Census("DreamCastleScore", 4);
Census("EndAreaCastleMatch", 4);
Census("EquipDropProtectOne", 4);
Census("GetBackItem", 4);
Census("GiveUserItem", 4);
Census("GMActCtrl", 4);
Census("GuildForbid", 4);
Census("GuildWarOff", 4);
Census("LeaveHero", 4);
Census("LoadValidFunc", 4);
Census("LogSwitch", 4);
Census("LookUserItemId", 4);
Census("MakeMyHero", 4);
Census("NpcHit", 4);
Census("PayScore", 4);
Census("ReloadBossMon", 4);
Census("ReloadComposeConfig", 4);
Census("ReloadDailyActiveCfg", 4);
Census("ReloadEquipSplit", 4);
Census("ReloadGoddessConfig", 4);
Census("ReloadLeitaiBlock", 4);
Census("ReloadMonAtt", 4);
Census("ReloadMonitemsTreeCfg", 4);
Census("ReloadPromptFile", 4);
Census("ReloadQuest", 4);
Census("ReloadRabbit", 4);
Census("ReloadRndItem", 4);
Census("ReloadSmsUserList", 4);
Census("ReloadSnakeConf", 4);
Census("ReloadTaskDispatch", 4);
Census("ReloadTBBConfig", 4);
Census("ReloadTransDuobao", 4);
Census("ReloadunBindItem", 4);
Census("ReloadWhiteList", 4);
Census("SendYuanBaoText", 4);
Census("SetFountSwitch", 4);
Census("SetPetSwitch", 4);
Census("ShowPayScore", 4);
Census("StorageItem", 4);
Census("SuperGm", 4);
Census("GetSysTime", 4);

// live 10 -> native 5
Census("AddCoin", 5);
Census("AddSkillExp", 5);
Census("AddVote", 5);
Census("ChgCastleWar", 5);
Census("ChgDoubleCastleWar", 5);
Census("ChgDreamCastleWar", 5);
Census("ChgEquipLevel", 5);
Census("ChgHeroSkill", 5);
Census("ClearEquipCompose", 5);
Census("DelGuild", 5);
Census("DelSSKSkill", 5);
Census("FileOperate", 5);
Census("LearnSkill", 5);
Census("Make", 5);
Census("MapCellFree", 5);
Census("ReloadC2CItems", 5);
Census("ReshuaMonScript", 5);
Census("ScriptTest", 5);
Census("SetAchieve", 5);
Census("SetDominateLv", 5);
Census("SetEquipComposeAbil", 5);
Census("SetEquipComposelv", 5);
Census("SetGoldActLv", 5);
Census("SetNoKillMapLv", 5);
Census("SmeltEquip", 5);
Census("SuperMerchant", 5);

// ============================================================
// 13 MATCH rows -- already at the native census, must stay put.
// ============================================================
Census("AddLinFu", 4);
Census("AttackMode", 0);
Census("ClearNickLinfu", 4);
Census("ChgMonAtt", 4);
Census("CreditCard", 4);
Census("ClearHackFlag", 4);
Census("DelHero", 4);
Census("GetUserItem", 4);
Census("HackFlag", 4);
Census("MapDropItem", 4);
Census("Rest", 0);
Census("SetAllGM", 5);
Census("SetNickLF", 4);
Census("SetNoSkillZone", 5);
Census("SignInAct", 4);
Census("TempSetMapParam", 5);
Census("LesCoin", 5);
Census("IncSelfLv", 4);
Census("拒绝私聊", 0);
Census("允许私聊", 0);
Census("拒绝喊话", 0);
Census("接受喊话", 0);
Census("拒绝交易", 0);
Census("允许交易", 0);
Census("允许行会聊天", 0);
Census("拒绝行会聊天", 0);

// ============================================================
// Name-drift aliases (#17b). OpenMir2 names (KickHuman/Shutup/...) were
// invented and deleted; the live [GameCommand] names are the native ones.
// Census those. Native counterparts that still have no GameCommand
// (KickOut/LookFor/HumNum/MonClear/reloadStditem/ChgCastleOwner/Die/IncSelfLv)
// are product MISSING, not this census's job -- it never registered them
// under the native name either.
// Evidence: staging/gm_alias_resolution_20260801.md + 430-row registry.
// ============================================================
Census("OutSay", 2);           // native OutSay(62); was C# Shutup
Census("ShifangSay", 2);       // native ShifangSay(63); was C# ShutupRelease
Census("LookOutSay", 3);       // native LookOutSay(64); was C# ShutupList
Census("CallMan", 3);          // native CallMan(72); was C# RecallHuman
Census("ReLoadGmFile", 5);     // native ReLoadGmFile(206); was C# ReLoadAdmin
// LoadAdmin was a second invented alias of the same native command; gone.

Console.WriteLine($"PASS NativeGmPermissionCensusCheck: {checks} checks");
return 0;

void Equal<T>(T actual, T expected, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.Error.WriteLine(
            $"FAIL NativeGmPermissionCensusCheck: {label}: " +
            $"expected={expected}, actual={actual}");
        Environment.Exit(1);
    }
}

void Census(string command, int nativePermission)
{
    if (!commands.TryGetValue(command, out var entry))
    {
        Console.Error.WriteLine(
            $"FAIL NativeGmPermissionCensusCheck: command '{command}' " +
            "has no [GameCommand] type in the GameSvr assembly");
        Environment.Exit(1);
        return;
    }

    Equal(entry.Perm, nativePermission,
        $"{command} ({entry.Type.Name}) nPermissionMin");
}
