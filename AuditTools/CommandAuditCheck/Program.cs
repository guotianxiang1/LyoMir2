using System.Text.RegularExpressions;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepositoryRoot();
if (root == null)
{
    Console.Error.WriteLine("INCOMPLETE: repository root was not supplied and could "
        + "not be located from the working directory. "
        + "Usage: CommandAuditCheck [repository root]");
    Environment.Exit(2);
}
var commandDirectory = Path.Combine(root, "GameSvr", "Command", "Commands");
if (!Directory.Exists(commandDirectory))
{
    throw new DirectoryNotFoundException(commandDirectory);
}

// NOTE (2026-08-03): 25 former protected-file entries whose NATIVE @name is ABSENT from the 430-row
// 战神 registry were moved OUT of this list into absentCommandsMustNotBeRegistered below (class-1 unknown-command
// silent-sink correction, idat_pass_C §Item D). Their names were confirmed absent by exact-match scan of
// staging/ida_award_case584_command_registry_20260720.txt (the direct TABLE=0x7B4654 count=430 extraction).
// The entries retained here are commands that ARE present in the native registry (real known handlers) and
// so remain faithfully fail-closed with an explicit red NativeCommandFailure.Report until implemented.
// R-pass 2026-08-03 (idat_R_ap_skillexp_reload_20260803.md): 9 entries left this list. The AP/信用分
// family (SetAP 723 / AddAP 726 / ClearAP 724 / Dec30AP 725 / GetAP 729) and AddSkillExp 312 were wired
// to their reversed native contracts and moved to implementedFiles below. The 3 Reload verbs
// (ReloadSnakeConf 512 / ReloadDailyActiveCfg 561 / ReloadTransDuobao 580) route to the silent
// def_622B15 sink and moved to silentNativeSinkFiles below (their red Report was an over-send).
var protectedFiles = new[]
{
    "AddVoteCommand.cs",
    "BeginAreaCastleMatchCommand.cs",
    "CallTaskMonCommand.cs",
    "ChgEquipLevelCommand.cs",
    "ChgGameOpenTimeCommand.cs",
    "EndAreaCastleMatchCommand.cs",
    "GetBackItemCommand.cs",
    "GMActCtrlCommand.cs",
    "GuildForbidCommand.cs",
    "HeroSkillSwitchCommand.cs",
    "LoadValidFuncCommand.cs",
    "LogSwitchCommand.cs",
    "MakeMyHeroCommand.cs",
    "MapCellFreeCommand.cs",
    "NpcHitCommand.cs",
    "ReloadC2CItemsCommand.cs",
    "ReloadPromptFileCommand.cs",
    "ReloadRndItemCommand.cs",
    "ReloadSmsUserListCommand.cs",
    "ReloadTaskDispatchCommand.cs",
    "ReloadunBindItemCommand.cs",
    "ReloadWhiteListCommand.cs",
    "ReshuaMonScriptCommand.cs",
    "SendYuanBaoTextCommand.cs",
    "SetFountSwitchCommand.cs",
    "SetNoKillMapLvCommand.cs",
    "SmeltEquipCommand.cs",
    "SuperMerchantCommand.cs",
};

foreach (var fileName in protectedFiles)
{
    var source = Read(fileName);
    Assert(source.Contains("NativeCommandFailure.Report", StringComparison.Ordinal),
        $"{fileName} no longer fails explicitly");
    Assert(!source.Contains("MsgColor.Green", StringComparison.Ordinal),
        $"{fileName} reintroduced a success response");
}

// Class-5 OVER-SEND correction (2026-08-03): each of these GM commands' native dispatch id routes
// to a SILENT sink -- either def_622B15 (0x0062B648: `mov [ebp+var_D],0` -> cleanup epilogue) or the
// empty-exit loc_62B64C (0x0062B64C: `xor eax,eax` -> the same epilogue). BOTH sinks fall through to
// the shared Delphi local-finalizer epilogue and send NO message. Verified against the jump table at
// staging/update_clothes_4637_ida_work/big622820.txt (jpt_622B15 @0x00622B1C; the case lists on the
// `ja def_622B15` @0x00622B0F and `jmp loc_62B64C` annotations enumerate every routed case), and
// corroborated by staging/gm_address_qa_20260801.md (per-command HandlerAddress == binary jump-slot).
// Because native does nothing AND says nothing, the previous C# NativeCommandFailure.Report RED
// message was an over-send. These files are now faithful SILENT no-ops: they must emit NO
// player-facing output (no NativeCommandFailure.Report / SysMsg / SendDefMessage / MsgColor.Green)
// while still carrying the [GameCommand registration so the @command stays recognized.
// id -> sink (from big622820.txt case lists + registry ida_award_case584_command_registry_20260720.txt):
//   def_622B15 (0x0062B648): DreamCastleScore 351, ChgDoubleCastleWar 531, ChgDreamCastleWar 533,
//     DelSSKSkill 240, SetEquipComposelv 498, SetEquipComposeAbil 499, ClearEquipCompose 500,
//     ChgSuperSkillLv 494, ChgFourthSkillState 328, EquipDropProtectOne 469, EquipExchange 557,
//     ReloadComposeConfig 542, ReloadEquipSplit 447, ReloadBossMon 521, ReloadGoddessConfig 438,
//     ReloadTBBConfig 380, FileOperate 483, ScriptTest 337, SetAllGM 335, and SetAchieve 543 (proved
//     by elimination: switch bound cmp esi,2EEh=750 so 543 has an in-range jump slot; 543 is absent
//     from the fully-listed loc_62B64C empty set; no "case 543" handler label exists anywhere in the
//     disasm -- neighbours 540/544/545 are separate real handlers and immediate neighbour 542 is a
//     proven def case that also sits past the truncated def-list tail -> 543's only remaining slot is
//     def_622B15).
//   empty-exit loc_62B64C (0x0062B64C): SetDominateLv 365, ReloadRabbit 532, ReloadLeitaiBlock 563.
//   R-pass 2026-08-03 (idat_R_ap_skillexp_reload_20260803.md §ITEM D): ReloadSnakeConf 512
//     (table[0x0062331C]), ReloadDailyActiveCfg 561 (table[0x006233E0]) and ReloadTransDuobao 580
//     (table[0x0062342C]) each read def_622B15 (0x0062B648) -> silent def-sink; their prior red
//     NativeCommandFailure.Report was an over-send, now nulled to faithful silent no-ops.
var silentNativeSinkFiles = new[]
{
    "ChgDoubleCastleWarCommand.cs",
    "ChgDreamCastleWarCommand.cs",
    "ChgFourthSkillStateCommand.cs",
    "ChgSuperSkillLvCommand.cs",
    "ClearEquipComposeCommand.cs",
    "DelSSKSkillCommand.cs",
    "DreamCastleScoreCommand.cs",
    "EquipDropProtectOneCommand.cs",
    "EquipExchangeCommand.cs",
    "FileOperateCommand.cs",
    "ReloadBossMonCommand.cs",
    "ReloadComposeConfigCommand.cs",
    "ReloadDailyActiveCfgCommand.cs",
    "ReloadEquipSplitCommand.cs",
    "ReloadGoddessConfigCommand.cs",
    "ReloadLeitaiBlockCommand.cs",
    "ReloadRabbitCommand.cs",
    "ReloadSnakeConfCommand.cs",
    "ReloadTBBConfigCommand.cs",
    "ReloadTransDuobaoCommand.cs",
    "ScriptTestCommand.cs",
    "SetAchieveCommand.cs",
    "SetAllGMCommand.cs",
    "SetDominateLvCommand.cs",
    "SetEquipComposeAbilCommand.cs",
    "SetEquipComposelvCommand.cs",
};

foreach (var fileName in silentNativeSinkFiles)
{
    var source = Read(fileName);
    Assert(!source.Contains("NativeCommandFailure.Report", StringComparison.Ordinal),
        $"{fileName} must stay a silent native-sink no-op (native def/empty sink sends nothing)");
    Assert(!source.Contains("SysMsg", StringComparison.Ordinal),
        $"{fileName} reintroduced player-facing output (native sink is silent)");
    Assert(!source.Contains("SendDefMessage", StringComparison.Ordinal),
        $"{fileName} reintroduced player-facing output (native sink is silent)");
    Assert(!source.Contains("MsgColor.Green", StringComparison.Ordinal),
        $"{fileName} reintroduced a success response");
    Assert(source.Contains("[GameCommand", StringComparison.Ordinal),
        $"{fileName} lost its GM command registration");
}

// Class-1 ABSENT-command OVER-SEND correction (2026-08-03): each of these GM commands' NATIVE @name is
// ABSENT from the 430-row 战神 GM command table (TABLE=0x7B4654, count=430, stride=0x78). Per
// staging/idat_pass_C_refill_unkcmd_plumbing_20260803.md §Item D (Tier-1 disassembly, D.1-D.5): for an
// unknown @name, sub_621F28 (name->index lookup @0x00621F28) misses -> writes outReqLevel(var_61)=0 and
// returns index 0 -> back in sub_622820 @0x00622AB5 `test esi,esi` is zero AND `cmp [var_61],0; jbe`
// guards out the only failure reply ("该命令需要N级GM才能使用", which requires a KNOWN command's
// required-level > 0) -> control falls to the silent def_622B15 sink (0x0062B648) = NATIVE SENDS NOTHING,
// for ALL callers including GMs. So the previous C# NativeCommandFailure.Report RED message was an
// over-send. Absence of every @name below was reconfirmed here by an exact-match (command='<name>')
// scan of staging/ida_award_case584_command_registry_20260720.txt returning ZERO hits; the only
// substring near-misses in that dump are GuildWarOn/GuildWarOff/ReportGuildWar (the bare 'GuildWar' is
// still absent) and chguserGlory/SetGloryPoint (the bare 'GameGlory' is still absent) -- both handled as
// the class-1 name. These files are now faithful SILENT no-ops: they must emit NO player-facing output
// (no NativeCommandFailure.Report / SysMsg / SendDefMessage / MsgColor.Green) while keeping the
// [GameCommand registration so the @command stays recognized by the C# dispatcher.
// 2026-08-13 分裂：原来这张表要求「保留 [GameCommand 注册 + 命令体静默」。那个契约是错的。
// 注册本身就是发散：BaseCommond.Handle 在调用命令体【之前】就用 nPermissionMin 短路，
// 权限不足者收到 M2Share.g_sGameCommandPermissionTooLow("权限不够!!!") 红字。这批命令的
// cs_perm 全是 10，而原生权限上限是 5，所以任何真实调用者都走短路分支——它们从来不是
// 「静默 no-op」，而是稳定回一句红字。原生对表外 @命令 一个字都不回（0x00621F4F 把所需权限
// 出参清 0 -> 0x00622AC2 `jbe 0x622B09` 跳过唯一失败回复 -> jt[0]=0x0062B648 静默收尾）。
// 因此忠实做法是【不注册】。下表的命令必须在 GameSvr 全树查无 [GameCommand("名字" 注册。
// 2026-08-13 第二批：原先这 10 个走「保留注册 + 命令体静默」，与上一段同样的理由改成
// 「不注册」，并已由 d5198c6b / 13ef4953 删除文件。名字在 430 行注册表里 exact 0 命中，
// 全镜像也 0 命中（ASCII 大小写不敏感 + UTF-16LE 双编码，纯 ASCII 名无需 GBK）。
var absentCommandsMustNotBeRegistered = new[]
{
    "AdjustExp", "Announcement", "DelDenyAccountLogon", "DelDenyCharNameLogon",
    "DelDenyIPaddrLogon", "DeleteChar", "DenyAccountLogon", "DenyCharNameLogon",
    "DenyIPaddrLogon", "DisableSendMsg", "DisableSendMsgList", "EnableSendMsg",
    "EndContest", "GameDiaMond", "GameGlory", "GuildWar",
    "ReloadAbuse", "RestartServer", "SbkDoorControl", "ShowDenyAccountLogon",
    "ShowDenyCharNameLogon", "ShowDenyIPaddrLogon", "SignMapMove",
    "TestGetBagItems", "Training", "UserCmd",
};

var gameSvrSources = Directory.GetFiles(Path.Combine(root, "GameSvr"), "*.cs",
    SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
foreach (var command in absentCommandsMustNotBeRegistered)
{
    var needle = $"[GameCommand(\"{command}\"";
    Assert(!gameSvrSources.Any(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase)),
        $"@{command} is absent from the 430-row native registry and must not be registered");
}

foreach (var implementedFile in new[]
         {
             "AddLinFuCommand.cs", "CreditCardCommand.cs", "ClearNickLinfuCommand.cs",
             "SetNoSkillZoneCommand.cs",
             // Native GM commands reconciled to their idat-backed implementations (the protected list
             // above was stale after these were wired in prior sessions / corroborated here):
             //   SetGoldActLv  [char+0x181D]==m_btGoldActNextLevel byte-confirmed
             //   DecEquipDura  sub_6F3324, own 0..15 equip Dura=value, no SysMsg
             //   CreateCampMon sub_6EB6B8, real RegenMonsterByName spawn infra
             //   ChgHeroSkill  idx228@0x006261D8 MATCH -> sub_6D2E08 -> sub_73F500 hero skill-level set
             "SetGoldActLvCommand.cs", "DecEquipDuraCommand.cs",
             "CreateCampMonCommand.cs", "ChgHeroSkillCommand.cs",
             // R-pass 2026-08-03 (idat_R_ap_skillexp_reload_20260803.md): the AP/信用分 family and
             // AddSkillExp wired to reversed sub_6F92xx / sub_744D4C contracts, moved out of
             // protectedFiles. AP runtime field = m_nActivePoint ([player+0x0AE4], NOT save off 0x0608);
             // ClearAP/Dec30AP also MapRandomMove to town "3"(盟重). AddSkillExp adds RAW skill exp
             // (no x3 fast-train) via a CheckMagicLevelup cascade.
             "SetAPCommand.cs", "AddAPCommand.cs", "ClearAPCommand.cs",
             "Dec30APCommand.cs", "GetAPCommand.cs", "AddSkillExpCommand.cs",
             // c5338f8e wired @ReloadMonitemsTreeCfg (present in the 430-row registry) to
             // 0x624002: `mov eax,[0x7D5D9C] / call 0x67AEC0` then an unconditional
             // `mov cx,0xFFDB / mov edx,0x62BA3C` reply -- 0x62BA3C is a length-25 Delphi
             // string "MonItemsTree.txt重载成功!" (D6 D8 D4 D8 B3 C9 B9 A6 in GBK). Native
             // never tests the loader result, so the green success is faithful, not invented.
             "ReloadMonitemsTreeCfgCommand.cs",
             // 2026-08-08: @LeaveTech (125, perm 4, case@0x006252B0) wired once
             // sub_6C5E08's body was reversed. It delegates to the one shared
             // dissolve core sub_6C5EC8 with edx = 0 (自行离开师门) -- the same
             // mode-0 entry PAS NpcLeaveTec uses at 0x6CB017, minus the 50,000
             // gold charge, which lives in the PAS wrapper and not the GM path.
             // Ladder and colour channel are asserted in NativeLeaveMasterCheck.
             "LeaveTechCommand.cs"
         })
{
    var source = Read(implementedFile);
    Assert(!source.Contains("NativeCommandFailure.Report", StringComparison.Ordinal),
        $"{implementedFile} reverted to fail-closed");
}
Assert(!File.Exists(Path.Combine(commandDirectory, "GameGirdCommand.cs")),
    "non-native GameGird command is registered");

var allSources = Directory.GetFiles(commandDirectory, "*.cs")
    .Select(path => (Path: path, Source: File.ReadAllText(path)))
    .ToArray();

foreach (var (path, source) in allSources)
{
    var name = Path.GetFileName(path);
    // @RestartServer joined the absent list above, so its former "durable shutdown" contract
    // is gone with the command. What must not come back is a host-killing exit reachable from
    // any GM command, since native has no such verb at all.
    Assert(!source.Contains("Environment.Exit", StringComparison.Ordinal),
        $"{name} kills the host process from a GM command");
    Assert(!source.Contains("[TODO]", StringComparison.Ordinal), $"{name} contains TODO output");
    Assert(!source.Contains("已触发", StringComparison.Ordinal), $"{name} contains fake triggered output");
    Assert(!source.Contains("暂未实现", StringComparison.Ordinal), $"{name} contains placeholder output");
    Assert(!Regex.IsMatch(source, @"for\s*\([^)]*\)\s*\{\s*\}", RegexOptions.Singleline),
        $"{name} contains an empty loop");
    Assert(!Regex.IsMatch(source,
            @"public\s+void\s+\w+\([^)]*\)\s*\{\s*(?:if\s*\([^)]*\)\s*\{\s*return;\s*\}\s*)?\}",
            RegexOptions.Singleline),
        $"{name} contains an empty command body");
}

var failureHelper = Read("NativeCommandFailure.cs");
Assert(failureHelper.Contains("MsgColor.Red", StringComparison.Ordinal),
    "failure helper is not a red client response");
Assert(!failureHelper.Contains("MsgColor.Green", StringComparison.Ordinal),
    "failure helper reports success");

var creditCard = Read("CreditCardCommand.cs");
Assert(creditCard.Contains("open|close|ClearMonLingfu|ClearAll", StringComparison.Ordinal),
    "CreditCard original parameter contract is missing");
Assert(Regex.IsMatch(creditCard,
        "GameCommand\\(\"CreditCard\",[\\s\\S]{0,180}?4\\)"),
    "CreditCard original permission 4 is missing");
Assert(creditCard.Contains("TryArchiveAll", StringComparison.Ordinal) &&
       creditCard.Contains("TryClearMonthly", StringComparison.Ordinal),
    "CreditCard native clear operations are missing");
Assert(!creditCard.Contains("人物名称 数量", StringComparison.Ordinal),
    "CreditCard reverted to the invented recharge contract");

var clearNickLinfu = Read("ClearNickLinfuCommand.cs");
Assert(Regex.IsMatch(clearNickLinfu,
        "GameCommand\\(\"ClearNickLinfu\",[\\s\\S]{0,160}?\"\",\\s*4\\)"),
    "ClearNickLinfu original permission/no-parameter contract is missing");
Assert(clearNickLinfu.Contains("RequestClearNickLinfu(PlayObject)",
        StringComparison.Ordinal),
    "ClearNickLinfu does not dispatch the native YBDB request");
Assert(!clearNickLinfu.Contains("GetPlayObject", StringComparison.Ordinal),
    "ClearNickLinfu reverted to targeting another player");

var shutup = Read("ShutupCommand.cs");
Assert(shutup.Contains("g_DenySayMsgList[sHumanName]", StringComparison.Ordinal) &&
       shutup.Contains("60_000L", StringComparison.Ordinal),
    "temporary mute does not write the expiration dictionary");
Assert(Read("ShutupReleaseCommand.cs").Contains("TryRemove(sHumanName", StringComparison.Ordinal),
    "temporary mute release does not remove the entry");
Assert(Read("ShutupListCommand.cs").Contains("foreach (var item in M2Share.g_DenySayMsgList)",
        StringComparison.Ordinal),
    "temporary mute list does not enumerate entries");

Console.WriteLine($"PASS protected={protectedFiles.Length} silentNativeSink={silentNativeSinkFiles.Length} absentUnregistered={absentCommandsMustNotBeRegistered.Length} commandFiles={allSources.Length}");
return;

string Read(string fileName) => File.ReadAllText(Path.Combine(commandDirectory, fileName));

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

// run_audits.py invokes every audit with no arguments, so a tool that hard-requires
// its repository root reported FAIL without evaluating a single assertion. Falling
// back to the enclosing checkout keeps the assertions exactly as they were and only
// removes the "never ran" outcome.
static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }
    return null;
}
