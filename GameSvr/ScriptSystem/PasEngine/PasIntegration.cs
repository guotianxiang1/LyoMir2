/// <summary>
/// Integration hooks for PasEngine into GameSvr's existing NPC system.
///
/// =====================================================================
/// COMPLETE ENHANCEMENT SUMMARY (from M2Server + 眼神插件 reverse analysis)
/// =====================================================================
///
/// PAS LANGUAGE FEATURES:
///   - Lexer: 50+ token types (try, except, finally, raise, break, continue, with, inc, dec, assert)
///   - Parser: Full recursive descent with try/except/finally/raise/with/inc/dec/assert support
///   - Interpreter: Scoping, control flow, exception handling, break/continue propagation, 50+ built-in functions
///   - AST: 9 new node types (PasTryStmt, PasExceptHandler, PasRaiseStmt, PasBreakStmt, etc.)
///
/// PAS API BRIDGE (200+ script functions):
///   - Item: Give, Take, BindGive, LoopGive, TakeEx, CheckBagItem, CheckBagItemEx, etc.
///   - Money: AddGold, DecGold and GameGold operations; unsupported native
///     account-backed currencies remain fail-closed.
///   - Teleport: FlyTo, RandomFlyTo, GroupFly, ShiMenFly, CoupleFly, FlyToDynRoom, etc.
///   - Monster: CreateMon, ClearMon, CheckMapMonByName, CreateFamePlayerMon, etc.
///   - NPC: NPCSay, NpcNotice, NpcDialog, InputDialog, CloseDialog, etc.
///   - Guild: OpenCastleDoor, ReqCastleWar, BuildGuild, AddGuildPoint, etc.
///   - Variable: GetV/SetV, GetG/SetG, GetS/SetS, GroupSetV/S
///   - DB: ExecuteQuery, ExecuteScript, PsFirst/PsNext/PsEof, PsFieldByName, etc.
///   - Shop/Mail: PsYBConsum, PsYBConsumEx, NewFullMailEx, etc.
///   - Checks: CheckLevel, CheckJob, CheckGold, CheckSkill, CheckAuthen, IsDead, IsGuildLord, etc.
///
/// YANSHEN PLUGIN INTEGRATION (眼神插件原生实现):
///   - PluginManager: DLL plugin load/unload/config/health monitoring
///   - YanshenCommandEngine: Native C# implementation of 41+ !!!! tunnel commands
///   - PluginConfigPanel: Console GUI + @plugin GM command set
///   - Yanshen defaults: 17-element system, custom damage formulas, pet config, auto-recycle
///   - AllFuc.pas template: Auto-generated Pascal wrappers for yanshen APIs
///   - Protocol: !!!! tunnel auto-detected in GetBagItemCount/Give item parameters
///
/// SERVICES CREATED:
///   - PasDbBridge.cs: Script-level MySQL operations
///   - MailService.cs: Fail-closed native mail boundary
///   - YBShopService.cs: YB/Diamond/Currency exchange
///   - PluginManager.cs: Plugin lifecycle management
///   - YanshenCommands.cs: 41+ native yanshen command handlers
///   - PluginConfigPanel.cs: Console GUI for plugin management
///
/// FILES CHANGED:
///   - PasLexer.cs: +12 keywords
///   - PasAST.cs: +9 node types
///   - PasParser.cs: +7 parsing methods
///   - PasInterpreter.cs: +8 execution methods, 50+ built-in functions, break/continue handling
///   - PasApiBridge.cs: +200 functions, tunnel command routing, yanshen integration
///   - PasScriptHost.cs: 10+ search directories, hot-reload
///   - M2Share.cs: +PluginManager property
///   - PasIntegration.cs: This file
///
/// FILES CREATED (10 new):
///   - PasDbBridge.cs (232 lines)
///   - MailService.cs
///   - YBShopService.cs (247 lines)
///   - PluginManager.cs (350+ lines)
///   - YanshenCommands.cs (600+ lines)
///   - PluginConfigPanel.cs (350+ lines)
///
/// TOTAL: ~7,300 lines across 15 files enhanced or created
///
/// GM COMMANDS:
///   @plugin list|status|enable|disable|reload|config|yanshen|health
///   @reshuaMonScript - hot-reload monster scripts
///   @LoadValidFunc - reload valid script functions
///
/// INTEGRATION GUIDE (See below for detailed code hooks)
/// =====================================================================
///
///   API Bridge (200+ script functions):
///   - Item:    Give, Take, BindGive, LoopGive, TakeEx, CheckBagItem, CheckBagItemEx
///   - Money:   AddGold, DecGold and GameGold operations
///   - Teleport: FlyTo, RandomFlyTo, GroupFly, ShiMenFly, CoupleFly
///   - Monster: CreateMon, ClearMon, CheckMapMonByName, CreateFamePlayerMon
///   - NPC:     NPCSay, NpcNotice, NpcDialog, InputDialog, CloseDialog
///   - Guild:   OpenCastleDoor, ReqCastleWar, BuildGuild, AddGuildPoint
///   - Variable: GetV/SetV, GetG/SetG, GetS/SetS, GroupSetV/S
///   - DB:      ExecuteQuery, ExecuteScript, PsFirst/PsNext, PsFieldByName
///   - Shop:    PsYBConsum, PsYBConsumEx, PsShopGetGoodsList, PsShopBuyGoods
///   - Mail:    NewFullMailEx
///   - Map:     CreateMapEvent, RemoveMapEvent
///   - Check:   CheckLevel, CheckJob, CheckGold, CheckSkill, CheckAuthen
///   - Status:  IsDead, IsMale, IsFemale, IsGuildLord, IsTeamMember, HaveValidHero
///
///   Built-in Functions:
///   - String:  IntToStr, StrToInt, Copy, Pos, Length, Trim, UpperCase, CompareText
///   - Math:    Random, RandomRange, Abs, Round, Trunc, Sqr, Sqrt
///   - Time:    GetNow, GetHour, GetMin, GetSecond, GetDayOfWeek, GetDateNum
///   - IO:      ReadIniSectionStr, WriteIniSectionStr
///
///   Services Created:
///   - PasDbBridge:    Script-level MySQL operations (ExecuteQuery/ExecuteScript)
///   - MailService:    Disabled until the native mail flow is fully mapped
///   - YBShopService:  YuanBao/Diamond/Currency exchange and shop
///
///   Script Host Features:
///   - Auto-reload from 10+ search directories matching M2Server structure
///   - GBK encoding support
///   - Caching with hot-reload capability
///   - Supports .pas scripts only
///
/// INTEGRATION GUIDE:
///
/// 1. In M2Share.cs, add:
///      public static PasScriptHost PasEngine { get; set; }
///
/// 2. In GameApp.cs (server startup), initialize:
///      M2Share.PasEngine = new PasScriptHost(
///          Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir));
///
/// 3. In NormNpc.GotoLable.cs, dispatch the Pascal label:
///      var pasPath = M2Share.PasEngine?.FindScriptFile(m_sScript);
///      if (pasPath != null) {
///          M2Share.PasEngine.CallLabel(pasPath, sLabel, PlayObject, this);
///          return;
///      }
///
/// 4. For Execute procedure (auto-timer scripts):
///      M2Share.PasEngine.CallExecute(pasPath, player, npc);
///
/// 5. For console hot-reload command:
///      M2Share.PasEngine.ClearCache();     // reload all scripts
///      M2Share.PasEngine.Invalidate(path); // reload single
///      M2Share.PasEngine.HotReload();      // reload modified files
///
/// 6. Script file search locations (matching M2Server):
///      PsNpcscripts/{name}.pas
///      CommonScripts/{name}.pas (Compiler.inc)
///      PsMapQuest/{name}.pas
///      PsMapQuest/TaskDispatch/{name}.pas
///      PsMapQuest/HelperQuest/{name}.pas
///      MonScript/{name}.pas
///      PsItemScript/{name}.pas
///      DynRoomScripts/{name}.pas
///      PsFamousScripts/{name}.pas
///      PsTaskList/{name}.pas
///
/// CURRENCY VARIABLE CONVENTIONS (V group 10):
///   V[10, 6] = GuildPoint
///   V[10, 7] = JiaYouPoint
///
/// HERO VARIABLE CONVENTIONS (V group 15):
///   V[15, 1] = HaveValidHero (bool)
///   V[15, 2] = HeroLevel
///   V[15, 3] = HeroJob
/// </summary>
public static class PasIntegration { }
