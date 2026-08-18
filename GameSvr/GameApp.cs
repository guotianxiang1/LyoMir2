using GameSvr.CommandSystem;
using GameSvr.Configs;
using GameSvr.Services;
using System.Collections;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public class GameApp : ServerBase
    {
        private const int NativeDefinitionInitializationTimeoutMilliseconds =
            30_000;

        public bool Initialize()
        {
            int nCode;
            M2Share.LocalDB = new LocalDB();
            M2Share.CommonDB = new CommonDB();
            if (!NativeYbShopPurchaseStore.EnsureNativeSchema(
                    out var ybConsumeSchemaError))
                M2Share.ErrorMessage("创建原生YBConsume表失败: " +
                                     ybConsumeSchemaError);
            if (!NativeGloryLogManager.EnsureNativeSchema(
                    out var gloryLogSchemaError))
                M2Share.ErrorMessage("创建原生GloryLog表失败: " +
                                     gloryLogSchemaError);
            NativeAccountLogManager.Start();
            M2Share.LoadGameLogItemNameList();
            M2Share.LoadDenyIPAddrList();
            M2Share.LoadDenyAccountList();
            M2Share.LoadDenyChrNameList();
            M2Share.LoadNoClearMonList();
            M2Share.MainOutMessage("正在加载原生静态定义及标准物品数据库...");
            M2Share.DataServer.Start();
            if (!M2Share.DataServer.TryWaitForNativeDefinitionInitialization(
                    NativeDefinitionInitializationTimeoutMilliseconds,
                    out var nativeDefinitionError))
            {
                M2Share.MainOutMessage(
                    "加载原生静态定义失败!!! " + nativeDefinitionError);
                M2Share.DataServer.Stop();
                return false;
            }
            if (!M2Share.UserEngine.TryPublishNativeStdItemDefinitions(
                    M2Share.DataServer.StdItemRuntimeCatalog,
                    out nativeDefinitionError))
            {
                M2Share.MainOutMessage(
                    "发布原生标准物品数据库失败!!! " + nativeDefinitionError);
                M2Share.DataServer.Stop();
                return false;
            }
            if (!TPlayObject.InitializeNativeNeedKeyBoxConfig(
                    M2Share.sRootPath, M2Share.g_Config.sBaseDir))
            {
                M2Share.ErrorMessage(
                    "加载原生宝藏天赐配置失败，OpenNeedKeyBox 保持关闭。");
            }
            M2Share.MainOutMessage(
                $"加载原生标准物品数据库成功({M2Share.UserEngine.StdItemList.Count})...");
            // 原生 sub_74DEDC: 标准物品表发布后加载 Share/config/powerupItem.ini 的物品使用 mode-1
            // (英雄 TDragonHeart 护符填充) refill 表 (UserEngine+0x2A0)。文件缺失 => 空表 => mode-1 保持 fail-closed。
            M2Share.UserEngine.LoadNativePowerupItems();
            M2Share.UserEngine.LoadNativeItemAdvanceConfigs();
            M2Share.MainOutMessage("正在发布原生怪物及人物/英雄技能数据库...");
            if (!M2Share.UserEngine.TryPublishNativeMonsterDefinitions(
                    M2Share.DataServer.MonsterRuntimeCatalog,
                    out nativeDefinitionError))
            {
                M2Share.MainOutMessage(
                    "发布原生怪物数据库失败!!! " + nativeDefinitionError);
                M2Share.DataServer.Stop();
                return false;
            }
            if (!M2Share.UserEngine.TryPublishNativeMagicDefinitions(
                    M2Share.DataServer.MagicRuntimeCatalog,
                    out nativeDefinitionError))
            {
                M2Share.MainOutMessage(
                    "发布原生人物/英雄技能数据库失败!!! " + nativeDefinitionError);
                M2Share.DataServer.Stop();
                return false;
            }
            M2Share.MainOutMessage(
                $"加载原生怪物数据库成功({M2Share.UserEngine.MonsterList.Count})...");
            M2Share.MainOutMessage(
                $"加载原生技能数据库成功(人物 {M2Share.UserEngine.m_MagicList.Count}/" +
                $"英雄 {M2Share.UserEngine.m_HeroMagicList.Count})...");
            var signActStore = new NativeSignActStore(
                M2Share.g_Config?.sConnctionString);
            var signActSchemasReady = signActStore.EnsureSchemas(
                out var signActSchemaError);
            M2Share.SignActManager = new NativeSignActManager(signActStore);
            if (signActSchemasReady)
            {
                M2Share.MainOutMessage("原生SignAct/SignActEveryday管理器初始化成功。");
            }
            else
            {
                M2Share.ErrorMessage("原生SignAct表初始化失败，管理器按原版继续可用: " +
                                     signActSchemaError);
            }
            var nativeShareDirectory = Path.GetFullPath(Path.Combine(M2Share.sRootPath,
                M2Share.g_Config.sBaseDir));
            M2Share.SuperMerchantManager = new NativeSuperMerchantManager();
            M2Share.SuperMerchantManager.EnsureLoaded(nativeShareDirectory);
            if (NativeServerSwitchStore.TryLoad(nativeShareDirectory,
                    out var serverSwitches, out var serverSwitchError))
            {
                M2Share.ServerSwitches = serverSwitches;
            }
            else
            {
                M2Share.ServerSwitches = serverSwitches;
                M2Share.ErrorMessage("原生ServerSwitch.Bin加载失败: " +
                                     serverSwitchError);
            }
            if (NativeNickLinFuState.TryLoad(nativeShareDirectory,
                M2Share.ServerSwitches,
                out var nickLinFuState, out var nickLinFuError))
            {
                M2Share.NickLinFuState = nickLinFuState;
                M2Share.MainOutMessage($"圣殿灵符倍率加载成功({nickLinFuState.Multiplier})，" +
                    (nickLinFuState.Enabled ? "功能开启。" : "功能关闭。"));
            }
            else
            {
                M2Share.NickLinFuState = nickLinFuState;
                M2Share.ErrorMessage("圣殿灵符倍率/开关加载失败: " + nickLinFuError);
            }
            if (NativeCreditCardService.TryCreate(M2Share.ServerSwitches,
                    out var creditCardService, out var creditCardError))
            {
                M2Share.CreditCardService = creditCardService;
                M2Share.MainOutMessage("原生CreditCard灵符账户加载成功，" +
                    (creditCardService.Enabled ? "功能开启。" : "功能关闭。"));
            }
            else
            {
                M2Share.CreditCardService = creditCardService;
                M2Share.ErrorMessage("原生CreditCard灵符账户加载失败: " + creditCardError);
            }
            var nativeCorpsStore = new NativeCorpsMySqlStore(
                () => M2Share.g_Config?.sConnctionString);
            var nativeGildStore = new NativeGildMySqlStore(
                () => M2Share.g_Config?.sConnctionString);
            if (NativeCorpsService.TryCreate(nativeCorpsStore,
                    out var corpsService, out var corpsError, nativeGildStore))
            {
                M2Share.CorpsService = corpsService;
                M2Share.MainOutMessage(
                    $"原生Corps/Gild对象图加载成功(战队 {corpsService.CorpsCount}/行会 {corpsService.GildCount})...");
            }
            else
            {
                M2Share.CorpsService = corpsService;
                M2Share.ErrorMessage("原生Corps/Gild对象图加载失败: " +
                                     corpsError);
            }
            // Stall (摆摊): inject the write store + the in-memory manager and HYDRATE from gamedata.stall*
            // so live booths + their listed items survive a restart, plus run the closed-booth item-return
            // recovery. The write-gate itself is flipped ON 26 lines below (NativeStallWriteGate
            // .SupportsStallWrites = true) — see the LIVE rationale there. This block only builds the
            // store/manager and loads state; it no longer leaves the subsystem dormant.
            // (Historical note: this comment used to claim the gate stays OFF. That was stale as of
            //  2026-08-02 when the BUY conservation + CM wire round-trip proofs were reviewed and the gate
            //  was flipped. Corrected 2026-08-03 so the comment no longer contradicts the code below.)
            if (!NativeStartupConfigValidation.TryEnsureGamedataSchema(
                    out var gamedataSchemaError))
            {
                NativeStartupConfigValidation.ReportStallGamedataMissing();
            }
            var nativeStallStore = new NativeStallMySqlStore(
                () => M2Share.g_Config?.sConnctionString);
            NativeStallWriteGate.Store = nativeStallStore;
            var nativeStallManager = new NativeStallManager();
            NativeStallManagerHost.Manager = nativeStallManager;
            if (nativeStallStore.TryLoadActiveStalls(out var activeStalls, out var stallLoadError))
            {
                foreach (var stallRecord in activeStalls)
                    nativeStallManager.Register(stallRecord);
                var returnedStallItems = NativeStallRecovery.ReturnPendingPayouts(
                    () => M2Share.g_Config?.sConnctionString, nativeStallManager);
                M2Share.MainOutMessage(
                    $"原生摆摊对象图加载成功(活动摊位 {nativeStallManager.Count}，返还关摊物品 {returnedStallItems})，" +
                    "写入门禁已开启(LIVE)。");
            }
            else
            {
                M2Share.ErrorMessage("原生摆摊对象图加载失败: " + stallLoadError);
            }
            // LIVE (2026-08-02): the BUY conservation proof (AuditTools/NativeStallBuyConservationCheck) AND
            // the CM wire round-trip proof (NativeStallWireIntegrationCheck) both PASS and were reviewed — the
            // economy is conservation-safe BY CONSTRUCTION (equal/opposite gold deltas, all-or-nothing; a wrong
            // decode only fail-closes, never a gold/item bug). Flip the write-gate on: the stall subsystem
            // (setup/add/del/pause/buy/query) is now LIVE, matching native. Pre-flip display flags (query
            // item-blob per-item render + header name-source) are non-economy, non-buy-load-bearing follow-ups.
            NativeStallWriteGate.SupportsStallWrites = true;
            var nickPrizeFile = Path.Combine(M2Share.sRootPath,
                M2Share.g_Config.sBaseDir, "Config", "SearchNormalPrizeNew.Txt");
            if (NativeNickPrizeManager.TryLoad(nickPrizeFile,
                out var nickPrizeManager, out var nickPrizeError))
            {
                M2Share.NickPrizeManager = nickPrizeManager;
                M2Share.MainOutMessage("圣殿灵符奖池加载成功(4x25)...");
            }
            else
            {
                M2Share.NickPrizeManager = null;
                M2Share.ErrorMessage("圣殿灵符奖池加载失败: " + nickPrizeError);
            }
            M2Share.GoldIDRewards = LoadNativeRewardConfig(
                Path.Combine(nativeShareDirectory, "GoldID.ini"),
                path => new GoldIDLoader(path), "GoldID.ini");
            M2Share.GoldActRewards = LoadNativeRewardConfig(
                Path.Combine(nativeShareDirectory, "Config", "NewGoldID.ini"),
                path => new GoldActRewardLoader(path), "Config\\NewGoldID.ini");
            if (ReloadNormalPrize(out var configPrizeError))
            {
                M2Share.MainOutMessage("原生NormalPrize奖励池加载成功(99x100)...");
            }
            else
            {
                M2Share.ErrorMessage("原生NormalPrize奖励池加载失败: " + configPrizeError);
            }
            var nativeConfigDirectory = Path.Combine(nativeShareDirectory,
                "Config");
            if (NativeRewardConfigLoaders.TryLoadAll(nativeShareDirectory,
                    nativeConfigDirectory, out var rewardConfigError))
            {
                M2Share.MainOutMessage("原生奖励/排行配置加载成功(风云榜/火龙珠/白金/新手礼包等)...");
            }
            else
            {
                M2Share.ErrorMessage("原生奖励/排行配置加载失败: " +
                                     rewardConfigError);
            }
            if (!Mall.MallManager.Instance.LoadMallItems())
            {
                M2Share.MainOutMessage("元宝商城未启用：YBShopScript.pas 不存在、商品清单为空或脚本格式无效。");
            }
            if (NativeMailCacheService.InitializeFromStore(out var mailError))
            {
                M2Share.MainOutMessage(
                    $"加载邮件信息成功,加载{NativeMailCacheService.MailboxCount}个玩家的邮件。");
            }
            else
            {
                M2Share.ErrorMessage("加载邮件信息失败: " + mailError);
            }
            if (!NativeSuperMerchantIniLoader.TryValidateAtStartup(
                    out var superMerchantError)
                && !string.IsNullOrEmpty(superMerchantError))
            {
                M2Share.ErrorMessage(
                    "SuperMerchant.ini校验失败: " + superMerchantError);
            }
            try
            {
                var expiredWeaponCount = M2Share.WeaponUpgrades.CleanupExpired();
                if (expiredWeaponCount > 0)
                {
                    M2Share.MainOutMessage($"已清理过期武器升级记录({expiredWeaponCount})...");
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"清理武器升级记录失败: {ex.Message}");
            }
            M2Share.MainOutMessage("正在加载数据图文件...");
            nCode = Maps.LoadMinMap();
            if (nCode < 0)
            {
                M2Share.MainOutMessage("小地图数据加载失败!!!" + "Code: " + nCode);
                return false;
            }
            M2Share.MainOutMessage("小地图数据加载成功...");
            M2Share.MainOutMessage("正在加载地图数据...");
            nCode = Maps.LoadMapInfo();
            if (nCode < 0)
            {
                M2Share.MainOutMessage("地图数据加载失败!!!" + "Code: " + nCode);
                return false;
            }
            M2Share.MainOutMessage($"地图数据加载成功({M2Share.MapManager.Maps.Count})...");
            if (!NativeLimitBagItemDropLoader.TryResolveAutoLoadFile(
                    M2Share.sRootPath, out var limitBagItemDropAutoLoad,
                    out var limitBagItemDropFile,
                    out var limitBagItemDropError))
            {
                M2Share.ErrorMessage("MapDropLimitBagItems.xml config failed: "
                                     + limitBagItemDropError);
            }
            else if (!limitBagItemDropAutoLoad)
            {
                M2Share.MainOutMessage(
                    "MapDropLimitBagItems.xml AutoStart/AutoLoad disabled...");
            }
            else if (NativeLimitBagItemDropLoader.TryApply(limitBagItemDropFile,
                    M2Share.MapManager.FindMapByNativeName,
                    out limitBagItemDropError,
                    message => M2Share.ErrorMessage(message)))
            {
                var limitBagRuleCount = M2Share.MapManager.Maps.Sum(map =>
                    map.NativeLimitBagItemDrops.Count);
                M2Share.MainOutMessage(
                    $"MapDropLimitBagItems.xml loaded({limitBagRuleCount})...");
            }
            else
            {
                M2Share.ErrorMessage("MapDropLimitBagItems.xml load failed: " +
                                     limitBagItemDropError);
            }
            M2Share.UserEngine.LoadNativeMonSupport();
            _ = NativeDropControlLoader.TryLoadWorld(M2Share.sRootPath,
                M2Share.NativeWorldDropControl, out _);
            var dynamicRoomEnvirDirectory = Path.GetFullPath(Path.Combine(
                M2Share.sConfigPath, M2Share.g_Config.sEnvirDir));
            var dynamicRoomMapDirectory = Path.GetFullPath(Path.Combine(
                M2Share.sConfigPath, M2Share.g_Config.sMapDir));
            if (!M2Share.DynamicRoomService.TryInitializeFromFiles(
                    dynamicRoomEnvirDirectory, dynamicRoomMapDirectory,
                    M2Share.nServerIndex, out var dynamicRoomErrors))
            {
                M2Share.ErrorMessage("动态房初始化失败: "
                                     + string.Join(" | ", dynamicRoomErrors));
                return false;
            }
            M2Share.MainOutMessage(
                $"动态房初始化成功({M2Share.DynamicRoomService.DefinitionCount}类/" +
                $"{M2Share.DynamicRoomService.PhysicalRoomCount}实例)...");
            var activityPointFile = Path.Combine(M2Share.sRootPath, "Share", "EngineConfig",
                "\u4fe1\u7528\u5206\u7ba1\u7406", "PlayerActivePoint.xml");
            if (NativeActivityPointManager.TryLoad(activityPointFile,
                out var activityPointManager, out var activityPointError))
            {
                M2Share.ActivityPointManager = activityPointManager;
                M2Share.MainOutMessage("PlayerActivePoint.xml loaded.");
            }
            else
            {
                M2Share.ActivityPointManager = null;
                M2Share.ErrorMessage($"PlayerActivePoint.xml load failed: {activityPointError}");
            }
            var mapActivePointFile = NativeMapActivePointLoader.DefaultFilePath;
            if (NativeMapActivePointLoader.TryApply(mapActivePointFile, out var mapApError))
            {
                M2Share.MainOutMessage("MapActivePoint.xml loaded.");
            }
            else
            {
                M2Share.ErrorMessage($"MapActivePoint.xml load failed: {mapApError}");
            }
            if (M2Share.MapManager.FindMap(M2Share.g_Config.sHomeMap) == null)
            {
                M2Share.MainOutMessage($"出生地图加载失败!!! HomeMap={M2Share.g_Config.sHomeMap}");
                return false;
            }
            M2Share.MainOutMessage("正在加载怪物刷新配置信息...");
            nCode = M2Share.LocalDB.LoadMonGen();
            if (nCode < 0)
            {
                M2Share.MainOutMessage("加载怪物刷新配置信息失败!!!" + "Code: " + nCode);
                return false;
            }
            M2Share.MainOutMessage($"加载怪物刷新配置信息成功({M2Share.UserEngine.m_MonGenList.Count})...");
            M2Share.MainOutMessage("正加载怪物说话配置信息...");
            M2Share.LoadMonSayMsg();
            M2Share.MainOutMessage($"加载怪物说话配置信息成功({M2Share.g_MonSayMsgList.Count})...");
            // 战神 boot init sub_792838 loads MonItemsTree.txt alongside the other engine
            // tables: 0x792906 `call 0x67AEC0` with eax = [[0x7D5D9C]] (g_UserEngine),
            // the same loader the @ReloadMonitemsTreeCfg command re-runs at 0x624009.
            // Without this the chain would only ever populate after a manual GM reload.
            M2Share.UserEngine.ReloadMonItemsTree();
            M2Share.LoadDisableTakeOffList();
            M2Share.LoadMonDropLimitList();
            M2Share.LoadDisableMakeItem();
            M2Share.LoadEnableMakeItem();
            M2Share.LoadDisableMoveMap();
            M2Share.LoadFixedCoordDisableMap();
            M2Share.ItemUnit.LoadCustomItemName();
            M2Share.LoadDisableSendMsgList();
            M2Share.LoadItemBindIPaddr();
            M2Share.LoadItemBindAccount();
            M2Share.LoadItemBindCharName();
            M2Share.LoadUnMasterList();
            M2Share.LoadUnForceMasterList();
            M2Share.MainOutMessage("正在加载捆装物品信息...");
            nCode = M2Share.LocalDB.LoadUnbindList();
            if (nCode < 0)
            {
                M2Share.MainOutMessage("加载捆装物品信息失败!!!" + "Code: " + nCode);
                return false;
            }
            M2Share.MainOutMessage("加载捆装物品信息成功...");
            M2Share.MainOutMessage("正在加载任务地图信息...");
            nCode = M2Share.LocalDB.LoadMapQuest();
            if (nCode < 0)
            {
                M2Share.MainOutMessage("加载任务地图信息失败!!!");
                return false;
            }
            M2Share.MainOutMessage("加载任务地图信息成功...");
            if (LoadAbuseInformation(".\\abuse.txt"))
            {
                M2Share.MainOutMessage("加载文字过滤信息成功...");
            }
            M2Share.MainOutMessage("正在加载公告提示信息...");
            if (!M2Share.LoadLineNotice(Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sNoticeDir, "LineNotice.txt")))
            {
                M2Share.MainOutMessage("加载公告提示信息失败!!!");
            }
            M2Share.MainOutMessage("加载公告提示信息成功...");
            M2Share.LocalDB.LoadAdminList();
            M2Share.MainOutMessage("管理员列表加载成功...");
            M2Share.GuildManager.LoadGuildInfo();
            M2Share.MainOutMessage("行会列表加载成功...");
            M2Share.CastleManager.LoadCastleList();
            M2Share.MainOutMessage("城堡列表加载成功...");
            M2Share.CastleManager.Initialize();
            M2Share.MainOutMessage("城堡城初始完成...");
            if (M2Share.nServerIndex == 0)
            {
                try
                {
                    SnapsmService.Instance.StartSnapsServer();
                    M2Share.MainOutMessage("当前服务器运行主节点模式...");
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage($"消息节点服务启动失败，已跳过主节点监听: {ex.Message}");
                    M2Share.MainOutMessage("当前服务器降级为无消息节点模式运行...");
                }
            }
            else
            {
                SnapsmClient.Instance.ConnectMsgServer();
                M2Share.MainOutMessage($"当前运行从节点模式...[{M2Share.g_Config.sMsgSrvAddr}:{M2Share.g_Config.nMsgSrvPort}]");
            }
            NativeStartupConfigValidation.LogSystemParametersInitialized();
            return true;
        }

        public static bool ReloadNormalPrize(out string error)
        {
            var nativeShareDirectory = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir));
            var configPrizeFile = Path.Combine(nativeShareDirectory,
                "Config", "NormalPrize.ini");
            M2Share.ConfigPrizeManager ??=
                NativeConfigPrizeManager.CreateNative();
            return M2Share.ConfigPrizeManager.ReloadInPlace(configPrizeFile,
                out error);
        }

        public static bool ReloadDiamondFoundry(out string error)
        {
            var giftsFile = Path.GetFullPath(Path.Combine(
                M2Share.sConfigPath, M2Share.g_Config.sEnvirDir,
                "Gifts.txt"));
            if (File.Exists(giftsFile))
            {
                Volatile.Write(ref M2Share.DiamondFoundry,
                    NativeDiamondFoundry.Unavailable);
            }
            if (!NativeDiamondFoundry.TryLoad(giftsFile,
                    out var foundry, out error))
                return false;

            Volatile.Write(ref M2Share.DiamondFoundry, foundry);
            return true;
        }

        private static T LoadNativeRewardConfig<T>(string fileName,
            Func<string, T> load, string displayName) where T : class
        {
            if (!File.Exists(fileName) || new FileInfo(fileName).Length == 0)
            {
                M2Share.ErrorMessage($"原生奖励配置加载失败: {displayName} 不存在或为空。");
                return null;
            }
            try
            {
                var result = load(fileName);
                M2Share.MainOutMessage($"原生奖励配置加载成功: {displayName}");
                return result;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"原生奖励配置加载失败: {displayName}: {ex.Message}");
                return null;
            }
        }

        public void StartEngine()
        {
            try
            {
                IdSrvClient.Instance.Initialize();
                M2Share.MainOutMessage("登录服务器连接初始化完成...");
                M2Share.MapManager.LoadMapDoor();
                M2Share.MainOutMessage("地图环境加载成功...");
                M2Share.LocalDB.LoadMerchant();
                var psNpcCount = M2Share.LocalDB.LoadPsNpcScriptNpcs();
                M2Share.MainOutMessage($"交易NPC列表加载成功... PsNpcScript NPC({psNpcCount})");
                if (!M2Share.g_Config.boVentureServer)
                {
                    M2Share.LocalDB.LoadGuardList();
                    M2Share.MainOutMessage("守卫列表加载成功...");
                }
                M2Share.LocalDB.LoadNpcs();
                M2Share.MainOutMessage("管理NPC列表加载成功...");
                M2Share.MainOutMessage("PsNpcScript NPC列表加载完毕...");
                try { M2Share.LocalDB.LoadMakeItem(); } catch (Exception ex) { M2Share.MainOutMessage($"LoadMakeItem跳过: {ex.Message}"); }
                M2Share.MainOutMessage("炼制物品信息加载成功...");
                M2Share.LocalDB.LoadStartPoint();
                M2Share.MainOutMessage("回城点配置加载成功...");
                var safeZoneCount = M2Share.LocalDB.LoadSafeZone();
                M2Share.MainOutMessage($"安全区配置加载成功({safeZoneCount})...");
                M2Share.MainOutMessage("正在初始安全区光圈...");
                M2Share.MapManager.MakeSafePkZone();
                M2Share.MainOutMessage("安全区光圈初始化成功...");
                M2Share.FrontEngine.Start();
                M2Share.MainOutMessage("人物数据引擎启动成功...");
                M2Share.UserEngine.Initialize();
                M2Share.MainOutMessage("游戏处理引擎初始化成功...");
                M2Share.MainOutMessage(M2Share.g_sVersion);
                M2Share.MainOutMessage(M2Share.g_sUpDateTime);
                M2Share.boStartReady = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw new Exception(ex.ToString());
            }
        }

        public void InitializeServer()
        {
            M2Share.g_nSockCountMax = 0;
            M2Share.g_nHumCountMax = 0;
            M2Share.dwUsrRotCountMin = 0;
            M2Share.dwUsrRotCountMax = 0;
            M2Share.g_nProcessHumanLoopTime = 0;
            M2Share.g_dwHumLimit = 30;
            M2Share.g_dwMonLimit = 30;
            M2Share.g_dwZenLimit = 5;
            M2Share.g_dwNpcLimit = 5;
            M2Share.g_dwSocLimit = 10;
            M2Share.nDecLimit = 20;
            M2Share.g_Config.nLoadDBErrorCount = 0;
            M2Share.g_Config.nLoadDBCount = 0;
            M2Share.g_Config.nSaveDBCount = 0;
            M2Share.g_Config.nDBQueryID = 0;
            M2Share.g_Config.nItemNumberSeed = 1000;
            M2Share.g_Config.nItemNumber = 1000;
            M2Share.boStartReady = false;
            M2Share.boFilterWord = true;
            M2Share.g_Config.nWinLotteryCount = 0;
            M2Share.g_Config.nNoWinLotteryCount = 0;
            M2Share.g_Config.nWinLotteryLevel1 = 0;
            M2Share.g_Config.nWinLotteryLevel2 = 0;
            M2Share.g_Config.nWinLotteryLevel3 = 0;
            M2Share.g_Config.nWinLotteryLevel4 = 0;
            M2Share.g_Config.nWinLotteryLevel5 = 0;
            M2Share.g_Config.nWinLotteryLevel6 = 0;
            M2Share.LoadConfig();
            var fastnessUnionPath = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir, "config",
                "FASTNESS_UNION.txt"));
            var fastnessUnionTable = new NativeFastnessTable();
            if (fastnessUnionTable.Load(fastnessUnionPath))
            {
                Volatile.Write(ref M2Share.NativeFastnessUnionTable,
                    fastnessUnionTable);
            }
            var fastnessHqPath = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir, "config",
                "FASTNESS_HQ.txt"));
            var fastnessHqTable = new NativeFastnessHqTable();
            if (fastnessHqTable.Load(fastnessHqPath))
            {
                Volatile.Write(ref M2Share.NativeFastnessHqTable,
                    fastnessHqTable);
            }
            var fastnessNearHitPath = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir, "config",
                "FASTNESS_NEARHit.txt"));
            var fastnessNearHitTable = new NativeFastnessTable();
            if (fastnessNearHitTable.Load(fastnessNearHitPath))
            {
                Volatile.Write(ref M2Share.NativeFastnessNearHitTable,
                    fastnessNearHitTable);
            }
            var fastnessMagicPath = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir, "config",
                "FASTNESS_MAGIC.txt"));
            var fastnessMagicTable = new NativeFastnessTable();
            if (fastnessMagicTable.Load(fastnessMagicPath))
            {
                Volatile.Write(ref M2Share.NativeFastnessMagicTable,
                    fastnessMagicTable);
            }
            var fastnessSoulPath = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir, "config",
                "FASTNESS_SOUL.txt"));
            var fastnessSoulTable = new NativeFastnessTable();
            if (fastnessSoulTable.Load(fastnessSoulPath))
            {
                Volatile.Write(ref M2Share.NativeFastnessSoulTable,
                    fastnessSoulTable);
            }
            if (ReloadDiamondFoundry(out var foundryError))
            {
                var foundry = Volatile.Read(ref M2Share.DiamondFoundry);
                M2Share.MainOutMessage(
                    $"原生Gifts锻造配方加载成功({foundry.Recipes.Count})，" +
                    $"跳过无效行({foundry.SkippedRowCount})。");
            }
            else
            {
                M2Share.ErrorMessage("原生Gifts锻造配方加载失败: " +
                                     foundryError);
            }
            M2Share.MainOutMessage("[ScriptSystem] Pascal script system enabled (PAS-only).");
            var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
            M2Share.DataServer = new DBService();
            M2Share.DataServer.ConfigureFieldHeroMonItemsSource(
                new NativeFieldHeroFileMonItemsSource(envirDir));
            M2Share.ObjectManager = new ObjectManager();
            M2Share.GateManager = GateManager.Instance;
            M2Share.g_FindPath = new TFindPath();
            M2Share.CommandSystem = new CommandManager();
            M2Share.LogStringList = new ArrayList();
            M2Share.LogonCostLogList = new ArrayList();
            M2Share.MapManager = new MapManager();
            M2Share.DynamicRoomManager = new NativeDynamicRoomManager();
            M2Share.HonorValueManager = new NativeHonorValueManager();
            M2Share.HonorValueManager.Initialize();
            M2Share.AuthenticationManager = new NativeAuthenticationManager();
            M2Share.AuthenticationManager.Initialize();
            M2Share.ItemUnit = new ItemUnit();
            M2Share.MagicManager = new MagicManager();
            M2Share.NoticeManager = new NoticeManager();
            M2Share.GuildManager = new AssociationManager();
            M2Share.EventManager = new EventManager();
            M2Share.CastleManager = new CastleManager();
            M2Share.FrontEngine = new TFrontEngine();
            M2Share.ProcessHumanCriticalSection = new object();
            M2Share.UserEngine = new UserEngine();
            var dynamicRoomScriptDirectory = Path.Combine(envirDir,
                "DynRoomScripts");
            M2Share.DynamicRoomPasRoutes =
                new NativeDynamicRoomPasScriptRouteTable(
                    dynamicRoomScriptDirectory);
            M2Share.DynamicRoomNpcOwner = new NativeDynamicRoomNpcOwner(
                M2Share.DynamicRoomPasRoutes);
            M2Share.DynamicRoomRuntime = new NativeDynamicRoomRuntime(
                M2Share.DynamicRoomManager,
                M2Share.DynamicRoomPasRoutes, envirDir);
            M2Share.DynamicRoomNpcMaterializer =
                new NativeDynamicRoomNpcMaterializer(
                    M2Share.ObjectManager, M2Share.UserEngine);
            M2Share.DynamicRoomService = new NativeDynamicRoomService(
                M2Share.DynamicRoomManager, M2Share.DynamicRoomRuntime,
                M2Share.DynamicRoomNpcOwner,
                M2Share.DynamicRoomNpcMaterializer,
                M2Share.EventManager, M2Share.ObjectManager,
                M2Share.UserEngine);
            M2Share.PasEngine = new PasEngine.PasScriptHost(envirDir,
                M2Share.DynamicRoomPasRoutes,
                M2Share.DynamicRoomRuntime);
            PasEngine.PasApiBridge.ScriptHost = M2Share.PasEngine;
            _ = PasEngine.NativeValidScriptFunctionRegistry.Reload(
                M2Share.sConfigPath);
            M2Share.PasEngine.LoadNpcScriptMap();
            M2Share.PasEngine.LoadTaskScripts();
            M2Share.PasEngine.LoadMonsterScripts();
            M2Share.MainOutMessage("[PasEngine] Pascal script engine initialized.");
            M2Share.g_MakeItemList = new Dictionary<string, IList<TMakeItem>>();
            M2Share.StartPointList = new List<TStartPoint>();
            M2Share.SafeZoneList = new List<TSafeZoneArea>();
            M2Share.ServerTableList = new TRouteInfo[20];
            M2Share.g_DenySayMsgList = NativeMirrorChatBan.CreateStore();
            if (NativeMirrorChatBan.TryInitializePersistentStore(envirDir,
                    out var blockUserCount, out var blockUserError))
            {
                M2Share.MainOutMessage(
                    $"加载BlockUsers.Dat禁言记录 {blockUserCount} 条...");
            }
            else
            {
                M2Share.ErrorMessage(
                    "加载BlockUsers.Dat失败，禁言持久化未启用: " + blockUserError);
            }
            M2Share.MiniMapList = new Dictionary<string, int>();
            M2Share.g_UnbindList = new Dictionary<int, string>();
            M2Share.LineNoticeList = new List<string>();
            M2Share.AbuseTextList = new StringList();
            M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
            M2Share.g_ChatLoggingList = new List<string>();
            M2Share.g_DisableMakeItemList = new List<string>();
            M2Share.g_EnableMakeItemList = new List<string>();
            M2Share.g_DisableMoveMapList = new List<string>();
            M2Share.g_DisableSendMsgList = new List<string>();
            M2Share.g_MonDropLimitLIst = new Dictionary<string, TMonDrop>();
            M2Share.g_DisableTakeOffList = new List<string>();
            M2Share.g_UnMasterList = new List<string>();
            M2Share.g_UnForceMasterList = new List<string>();
            M2Share.g_GameLogItemNameList = new List<string>();
            M2Share.g_DenyIPAddrList = new List<string>();
            M2Share.g_DenyChrNameList = new List<string>();
            M2Share.g_DenyAccountList = new List<string>();
            M2Share.g_NoClearMonLIst = new List<string>();
            M2Share.g_NoHptoexpMonLIst = new List<string>();
            M2Share.g_ItemBindIPaddr = new List<TItemBind>();
            M2Share.g_ItemBindAccount = new List<TItemBind>();
            M2Share.g_ItemBindCharName = new List<TItemBind>();
            M2Share.LogMsgCriticalSection = new object();
            M2Share.ProcessMsgCriticalSection = new object();
            M2Share.g_Config.UserIDSection = new object();
            M2Share.UserDBSection = new object();
            M2Share.g_DynamicVarList = new Dictionary<string, TDynamicVar>(StringComparer.OrdinalIgnoreCase);
            LoadServerTable();
            M2Share.dwRunDBTimeMax = HUtil32.GetTickCount();
            M2Share.CommandSystem.RegisterCommand();
        }

        private void LoadServerTable()
        {
            StringList LoadList;
            var nRouteIdx = 0;
            var sLineText = string.Empty;
            var sIdx = string.Empty;
            var sSelGateIPaddr = string.Empty;
            var sGameGateIPaddr = string.Empty;
            var sGameGate = string.Empty;
            var sGameGatePort = string.Empty;
            var sMapName = string.Empty;
            var sMapInfo = string.Empty;
            var sServerIndex = string.Empty;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sBaseDir, "servertable.txt");
            if (File.Exists(sFileName))
            {
                LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    sLineText = LoadList[i];
                    if (sLineText != "" && sLineText[0] != ';')
                    {
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sIdx, new[] { " ", "\09" });
                        sGameGate = HUtil32.GetValidStr3(sLineText, ref sSelGateIPaddr, new[] { " ", "\09" });
                        if (sIdx == "" || sGameGate == "" || sSelGateIPaddr == "")
                        {
                            continue;
                        }
                        if (M2Share.ServerTableList[nRouteIdx] == null)
                        {
                            M2Share.ServerTableList[nRouteIdx] = new TRouteInfo();
                        }
                        M2Share.ServerTableList[nRouteIdx].nGateCount = 0;
                        M2Share.ServerTableList[nRouteIdx].nServerIdx = HUtil32.Str_ToInt(sIdx, 0);
                        M2Share.ServerTableList[nRouteIdx].sSelGateIP = sSelGateIPaddr.Trim();
                        var nGateIdx = 0;
                        while (sGameGate != "")
                        {
                            sGameGate = HUtil32.GetValidStr3(sGameGate, ref sGameGateIPaddr, new[] { " ", "\09" });
                            sGameGate = HUtil32.GetValidStr3(sGameGate, ref sGameGatePort, new[] { " ", "\09" });
                            M2Share.ServerTableList[nRouteIdx].sGameGateIP[nGateIdx] = sGameGateIPaddr.Trim();
                            M2Share.ServerTableList[nRouteIdx].nGameGatePort[nGateIdx] = HUtil32.Str_ToInt(sGameGatePort, 0);
                            nGateIdx++;
                        }
                        M2Share.ServerTableList[nRouteIdx].nGateCount = nGateIdx;
                        nRouteIdx++;
                        if (nRouteIdx > M2Share.ServerTableList.GetUpperBound(0))
                        {
                            break;
                        }
                    }
                }
            }
        }

        
        
        
        
        
        private bool LoadAbuseInformation(string FileName)
        {
            int lineCount = 0;
            var result = false;
            var sText = string.Empty;
            if (File.Exists(FileName))
            {
                M2Share.AbuseTextList.Clear();
                M2Share.AbuseTextList.LoadFromFile(FileName);
                while (true)
                {
                    if (M2Share.AbuseTextList.Count <= lineCount)
                    {
                        break;
                    }
                    sText = M2Share.AbuseTextList[lineCount].Trim();
                    if (sText == "")
                    {
                        M2Share.AbuseTextList.RemoveAt(lineCount);
                        continue;
                    }
                    lineCount++;
                }
                result = true;
            }
            return result;
        }

        private void StatisticTime()
        {
            var sc = new System.Text.StringBuilder();
            
            
            sc.AppendLine(
                $"Hum:{M2Share.g_nHumCountMin}/{M2Share.g_nHumCountMax} UsrRot:{M2Share.dwUsrRotCountMin}/{M2Share.dwUsrRotCountMax} Merch:{M2Share.UserEngine.dwProcessMerchantTimeMin}/{M2Share.UserEngine.dwProcessMerchantTimeMax} Npc:{M2Share.UserEngine.dwProcessNpcTimeMin}/{M2Share.UserEngine.dwProcessNpcTimeMax} ({M2Share.g_nProcessHumanLoopTime})");
            
        }
    }
}
