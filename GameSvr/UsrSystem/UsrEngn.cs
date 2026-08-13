using System.Collections;
using GameSvr.Services;
using SystemModule;
using ThreadState = System.Threading.ThreadState;

namespace GameSvr
{
    public partial class UserEngine
    {
        private sealed class MagicDefinitionPublication
        {
            public MagicDefinitionPublication(IList<TMagic> human,
                IList<TMagic> hero)
            {
                Human = human;
                Hero = hero;
            }

            public IList<TMagic> Human { get; }
            public IList<TMagic> Hero { get; }
        }

        private sealed class MonsterDefinitionPublication
        {
            public MonsterDefinitionPublication(IList<TMonInfo> definitions,
                NativeType2MonsterRuntimeCatalog catalog,
                NativeType2MonsterManagerTables managerTables)
            {
                Definitions = definitions;
                Catalog = catalog;
                ManagerTables = managerTables;
            }

            public IList<TMonInfo> Definitions { get; }
            public NativeType2MonsterRuntimeCatalog Catalog { get; }
            public NativeType2MonsterManagerTables ManagerTables { get; }
        }

        private sealed class StdItemDefinitionPublication
        {
            public StdItemDefinitionPublication(IList<GoodItem> definitions,
                NativeType2StdItemStaticCatalog catalog)
            {
                Definitions = definitions;
                Catalog = catalog;
            }

            public IList<GoodItem> Definitions { get; }
            public NativeType2StdItemStaticCatalog Catalog { get; }
        }

        private sealed class HeroFreeInfo
        {
            public HeroObject Hero;
            public int FreeTick;
        }

        private sealed class NativeMagicTowerDeferredSpawn
        {
            public NativeMagicTowerDeferredSpawn(Envirnoment environment,
                string monsterName, int race, short x, short y)
            {
                Environment = environment;
                MonsterName = monsterName;
                Race = race;
                X = x;
                Y = y;
                IsDynamicRoom = environment.IsDynamicRoom;
                DynamicRoomPhysicalInstanceId =
                    environment.DynamicRoomPhysicalInstanceId;
                DynamicRoomIndex = environment.DynamicRoomIndex;
            }

            public Envirnoment Environment { get; }
            public string MonsterName { get; }
            public int Race { get; }
            public short X { get; }
            public short Y { get; }
            public bool IsDynamicRoom { get; }
            public int DynamicRoomPhysicalInstanceId { get; }
            public int DynamicRoomIndex { get; }
            public int Remaining { get; set; } = 1;
            public int RetryCounter { get; set; } = 5;
        }

        private const int HeroRunInterval = 50;
        private const int HeroProcessBudget = 25;
        private const int HeroFreeDelay = 5 * 60 * 1000;
        private const int NativeMagicTowerDeferredSpawnBudget = 5;
        private const int NativeMagicTowerRuntimeBudgetCheckInterval = 20;
        private const int NativeMagicTowerRuntimeTimeBudget = 25;
        /// <summary>0x67C252 <c>mov edi,0x50</c> — generators examined per regen pass.</summary>
        private const int NativeMonGenScanPerTick = 0x50;
        /// <summary>0x67CAAC <c>cmp dword [ebp-0x10],0x19</c> — monsters one worker call may add.</summary>
        private const int NativeMonGenSpawnBudget = 0x19;
        /// <summary>0x67CA2B <c>cmp dword [ebx+0x38],5</c> — failures before placement relaxes.</summary>
        private const int NativeMonGenFailRelaxThreshold = 5;
        private int dwProcessMapDoorTick;
        public int dwProcessMerchantTimeMax;
        public int dwProcessMerchantTimeMin;
        private int dwProcessMissionsTime;
        public int dwProcessNpcTimeMax;
        public int dwProcessNpcTimeMin;
        private int dwRegenMonstersTick;
        private int dwSendOnlineHumTime;
        private int dwShowOnlineTick;
        public IList<TAdminInfo> m_AdminList;
        private readonly IList<TGoldChangeInfo> m_ChangeHumanDBGoldList;
        private readonly IList<TSwitchDataInfo> m_ChangeServerList;
        private int m_dwProcessLoadPlayTick;
        private readonly ArrayList m_ListOfGateIdx;
        private readonly ArrayList m_ListOfSocket;
        private readonly ArrayList m_ListOfUserGeneration;
        
        
        
        private readonly IList<TUserOpenInfo> m_LoadPlayList;
        private readonly object m_LoadPlaySection;
        public IList<MagicEvent> m_MagicEventList;
        private readonly object _magicDefinitionSync = new();
        private MagicDefinitionPublication _magicDefinitions;
        private int _nativeMagicDefinitionsPublished;
        public IList<TMagic> m_MagicList =>
            Volatile.Read(ref _magicDefinitions).Human;
        public IList<TMagic> m_HeroMagicList =>
            Volatile.Read(ref _magicDefinitions).Hero;
        public IList<Merchant> m_MerchantList;
        private readonly ArrayList m_MonFreeList;
        public IList<MonGenInfo> m_MonGenList;
        private readonly object _monsterDefinitionSync = new();
        private MonsterDefinitionPublication _monsterDefinitions;
        private int _nativeMonsterDefinitionsPublished;
        private int m_nCurrMonGen;
        private readonly IList<TPlayObject> m_NewHumanList;
        
        
        
        private int m_nMonGenCertListPosition;
        private int m_nMonGenListPosition;
        private int m_nProcHumIDx;
        private int m_nProcHeroIdx;
        private readonly IList<TPlayObject> m_PlayObjectFreeList;
        private readonly IList<HeroObject> m_HeroObjectList;
        private readonly Queue<HeroFreeInfo> m_HeroObjectFreeList;
        private readonly HashSet<int> m_HeroFreeObjectIds;
        private readonly object m_HeroSync;
        private readonly Dictionary<string, ServerGruopInfo> m_OtherUserNameList;
        private readonly IList<TPlayObject> m_PlayObjectList;
        private readonly IList<TPlayObject> m_AiPlayObjectList;
        public IList<TMonInfo> MonsterList =>
            Volatile.Read(ref _monsterDefinitions).Definitions;
        private int nMerchantPosition;
        
        
        
        private int nMonsterCount;
        
        
        
        private int nMonsterProcessCount = 0;
        
        
        
        private int nMonsterProcessPostion;
        
        
        
        private int nNpcPosition;
        
        
        
        private int nProcessHumanLoopTime;
        private readonly ArrayList OldMagicList;
        public IList<NormNpc> QuestNPCList;
        private readonly object _stdItemDefinitionSync = new();
        private StdItemDefinitionPublication _stdItemDefinitions;
        private int _nativeStdItemDefinitionsPublished;
        public IList<GoodItem> StdItemList =>
            Volatile.Read(ref _stdItemDefinitions).Definitions;
        public long m_dwAILogonTick;//处理假人间隔
        public IList<TAILogon> m_UserLogonList;//假人列表
        private readonly Thread _userEngineThread;
        private readonly Thread _processAiThread;
        private readonly Queue<NativeMagicTowerDeferredSpawn>
            _nativeMagicTowerDeferredSpawns;
        private readonly IList<TBaseObject>
            _nativeMagicTowerRuntimeMonsters;
        private int _nativeMagicTowerRuntimeCursor;
        private int _nativeMagicTowerRuntimeMonsterCount;
        private volatile bool _stopRequested;

        public UserEngine()
        {
            m_LoadPlaySection = new object();
            m_LoadPlayList = new List<TUserOpenInfo>();
            m_PlayObjectList = new List<TPlayObject>();
            m_PlayObjectFreeList = new List<TPlayObject>();
            m_HeroObjectList = new List<HeroObject>();
            m_HeroObjectFreeList = new Queue<HeroFreeInfo>();
            m_HeroFreeObjectIds = new HashSet<int>();
            m_HeroSync = new object();
            m_ChangeHumanDBGoldList = new List<TGoldChangeInfo>();
            dwShowOnlineTick = HUtil32.GetTickCount();
            dwSendOnlineHumTime = HUtil32.GetTickCount();
            dwProcessMapDoorTick = HUtil32.GetTickCount();
            dwProcessMissionsTime = HUtil32.GetTickCount();
            dwRegenMonstersTick = HUtil32.GetTickCount();
            m_dwProcessLoadPlayTick = HUtil32.GetTickCount();
            m_nCurrMonGen = 0;
            m_nMonGenListPosition = 0;
            m_nMonGenCertListPosition = 0;
            m_nProcHumIDx = 0;
            m_nProcHeroIdx = 0;
            nProcessHumanLoopTime = 0;
            nMerchantPosition = 0;
            nNpcPosition = 0;
            _stdItemDefinitions = new StdItemDefinitionPublication(
                new List<GoodItem>(), null);
            _monsterDefinitions = new MonsterDefinitionPublication(
                new List<TMonInfo>(), null, null);
            m_MonGenList = new List<MonGenInfo>();
            m_MonFreeList = new ArrayList();
            _magicDefinitions = new MagicDefinitionPublication(
                new List<TMagic>(), new List<TMagic>());
            m_AdminList = new List<TAdminInfo>();
            m_MerchantList = new List<Merchant>();
            QuestNPCList = new List<NormNpc>();
            m_ChangeServerList = new List<TSwitchDataInfo>();
            m_MagicEventList = new List<MagicEvent>();
            dwProcessMerchantTimeMin = 0;
            dwProcessMerchantTimeMax = 0;
            dwProcessNpcTimeMin = 0;
            dwProcessNpcTimeMax = 0;
            m_NewHumanList = new List<TPlayObject>();
            m_ListOfGateIdx = new ArrayList();
            m_ListOfSocket = new ArrayList();
            m_ListOfUserGeneration = new ArrayList();
            OldMagicList = new ArrayList();
            m_OtherUserNameList = new Dictionary<string, ServerGruopInfo>(StringComparer.OrdinalIgnoreCase);
            m_UserLogonList = new List<TAILogon>();
            _nativeMagicTowerDeferredSpawns =
                new Queue<NativeMagicTowerDeferredSpawn>();
            _nativeMagicTowerRuntimeMonsters = new List<TBaseObject>();
            _nativeMagicTowerRuntimeCursor = 0;
            _nativeMagicTowerRuntimeMonsterCount = 0;
            _userEngineThread = new Thread(PrcocessData) { IsBackground = true };
            _processAiThread = new Thread(ProcessAiPlayObjectData) { IsBackground = true };
            m_AiPlayObjectList = new List<TPlayObject>();
        }

        public int MonsterCount => nMonsterCount;
        public int OnlinePlayObject => GetOnlineHumCount();
        public int PlayObjectCount => GetUserCount();
        public int LoadPlayCount => GetLoadPlayCount();

        public int HeroObjectCount
        {
            get
            {
                lock (m_HeroSync)
                {
                    return m_HeroObjectList.Count;
                }
            }
        }

        public int HeroFreeObjectCount
        {
            get
            {
                lock (m_HeroSync)
                {
                    return m_HeroObjectFreeList.Count;
                }
            }
        }

        public IEnumerable<TPlayObject> PlayObjects => m_PlayObjectList;

        public void AllGetCastle()
        {
            if (M2Share.g_FunctionNPC == null) return;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                var playObject = m_PlayObjectList[i];
                if (playObject == null || playObject.m_boGhost) continue;
                playObject.m_nScriptGotoCount = 0;
                M2Share.g_FunctionNPC.GotoLable(playObject, "@GetCastFunc", false);
            }
        }

        public void Start()
        {
            _stopRequested = false;
            if (_userEngineThread.ThreadState == ThreadState.Unstarted)
            {
                _userEngineThread.Start();
            }
        }

        public void Stop()
        {
            _stopRequested = true;
            JoinThread(_userEngineThread);
            JoinThread(_processAiThread);
            FlushNativeMerchantGoods();
            _nativeMagicTowerDeferredSpawns.Clear();
            ClearNativeMagicTowerRuntimeMonsters();
        }

        private void FlushNativeMerchantGoods()
        {
            var currentTick = HUtil32.GetTickCount();
            var merchants = SnapshotMerchants();
            for (var i = 0; i < merchants.Length; i++)
            {
                var merchant = merchants[i];
                if (merchant == null || !merchant.HasNativePasProperty(9))
                    continue;
                try
                {
                    merchant.FlushNativeGoods(currentTick,
                        Merchant.GetNativeGoodsRootPath());
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage(
                        $"[Exception] TUserEngine::FlushNativeMerchantGoods " +
                        $"{merchant.m_sCharName}: {ex.Message}");
                }
            }
        }

        private static void JoinThread(Thread thread)
        {
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            {
                thread.Join();
            }
        }

        public void Initialize()
        {
            M2Share.MainOutMessage("正在初始化NPC脚本...");
            MerchantInitialize();
            NpCinitialize();
            M2Share.MainOutMessage("初始化NPC脚本完成...");
            var matchedMonGenCount = 0;
            var unmatchedMonGenCount = 0;
            var unmatchedSamples = new List<string>();
            for (var i = 0; i < m_MonGenList.Count; i++)
            {
                if (m_MonGenList[i] == null || string.IsNullOrEmpty(m_MonGenList[i].sMonName)) continue;

                m_MonGenList[i].nRace = GetMonRace(m_MonGenList[i].sMonName);
                if (m_MonGenList[i].nRace > 0)
                {
                    matchedMonGenCount++;
                }
                else
                {
                    unmatchedMonGenCount++;
                    if (unmatchedSamples.Count < 8)
                    {
                        unmatchedSamples.Add($"{m_MonGenList[i].sMonName}@{m_MonGenList[i].sMapName}({m_MonGenList[i].nX}:{m_MonGenList[i].nY})");
                    }
                }
            }
            M2Share.MainOutMessage($"怪物刷新配置匹配完成: 可刷({matchedMonGenCount}) 未匹配({unmatchedMonGenCount})...");
            if (unmatchedSamples.Count > 0)
            {
                M2Share.MainOutMessage("未匹配怪物示例: " + string.Join(", ", unmatchedSamples));
            }
        }

        private int GetMonRace(string sMonName)
        {
            return TryGetMonsterInfo(sMonName, out var monster)
                ? monster.btRace : -1;
        }

        private bool TryGetMonsterInfo(string name, out TMonInfo monster)
        {
            monster = default;
            var publication = Volatile.Read(ref _monsterDefinitions);
            var definitions = publication.Definitions;
            if (publication.Catalog != null)
            {
                var index = publication.Catalog.FindIndexByName(name);
                if (index < 0 || index >= definitions.Count) return false;
                monster = definitions[index];
                return true;
            }

            // Isolated tests and tooling can still populate the legacy list.
            for (var index = 0; index < definitions.Count; index++)
            {
                if (!string.Equals(definitions[index].sName, name,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                monster = definitions[index];
                return true;
            }
            return false;
        }

        /// <summary>
        /// 战神 <c>dword[self+0x474]</c>, the monster's own drop table.  It is a straight
        /// copy of the template's list pointer — <c>0x71EB5E mov eax,[esi+0x48] /
        /// 0x71EB61 mov [ebx+0x474],eax</c> in the template-to-instance blit, whose
        /// neighbours (<c>[esi+0x3A]→[ebx+0x24E]</c>, <c>[esi+0x3C]→[ebx+0x244]</c>)
        /// identify esi as the TMonInfo record — so the C# equivalent is the shared
        /// <c>TMonInfo.ItemList</c> reference, and a nil pointer is a null list, not an
        /// empty one.  The constructor leaves it nil (<c>0x71D870 mov [edi+0x474],eax</c>
        /// with eax just zeroed at 0x71D86E).
        /// </summary>
        public bool NativeHasMonsterDropTable(string monsterName)
        {
            return TryGetMonsterInfo(monsterName, out var monster)
                   && monster.ItemList != null;
        }

        private static void ReleaseUnpublishedObject(TBaseObject baseObject)
        {
            if (baseObject != null)
            {
                M2Share.ObjectManager.Remove(baseObject.ObjectId);
            }
        }

        private void MerchantInitialize()
        {
            Merchant Merchant;
            var merchants = SnapshotMerchants();
            for (var i = merchants.Length - 1; i >= 0; i--)
            {
                Merchant = merchants[i];
                Merchant.m_PEnvir = M2Share.MapManager.FindMap(Merchant.m_sMapName);
                if (Merchant.m_PEnvir != null)
                {
                    Merchant.OnEnvirnomentChanged();
                    Merchant.Initialize();
                    if (Merchant.m_boAddtoMapSuccess && !Merchant.m_boIsHide)
                    {
                        M2Share.MainOutMessage("Merchant Initalize fail..." + Merchant.m_sCharName + ' ' +
                                               Merchant.m_sMapName + '(' +
                                               Merchant.m_nCurrX + ':' + Merchant.m_nCurrY + ')');
                        TryRemoveMerchantExact(Merchant);
                        ReleaseUnpublishedObject(Merchant);
                    }
                    else
                    {
                        Merchant.LoadMerchantScript();
                        Merchant.LoadNPCData();
                    }
                }
                else
                {
                    M2Share.MainOutMessage(Merchant.m_sCharName + " - Merchant Initalize fail... (m.PEnvir=nil)");
                    TryRemoveMerchantExact(Merchant);
                    ReleaseUnpublishedObject(Merchant);
                }
            }
        }

        private void NpCinitialize()
        {
            NormNpc NormNpc;
            var questNpcs = SnapshotReloadableQuestNpcs();
            for (var i = questNpcs.Length - 1; i >= 0; i--)
            {
                NormNpc = questNpcs[i];
                NormNpc.m_PEnvir = M2Share.MapManager.FindMap(NormNpc.m_sMapName);
                if (NormNpc.m_PEnvir != null)
                {
                    NormNpc.OnEnvirnomentChanged();
                    NormNpc.Initialize();
                    if (NormNpc.m_boAddtoMapSuccess && !NormNpc.m_boIsHide)
                    {
                        M2Share.MainOutMessage(NormNpc.m_sCharName + " Npc Initalize fail... ");
                        TryRemoveQuestNpcExact(NormNpc);
                        ReleaseUnpublishedObject(NormNpc);
                    }
                    else
                    {
                        NormNpc.LoadNPCScript();
                    }
                }
                else
                {
                    M2Share.MainOutMessage(NormNpc.m_sCharName + " Npc Initalize fail... (npc.PEnvir=nil) ");
                    TryRemoveQuestNpcExact(NormNpc);
                    ReleaseUnpublishedObject(NormNpc);
                }
            }
        }

        private int GetLoadPlayCount()
        {
            lock (m_LoadPlaySection) return m_LoadPlayList.Count;
        }

        private int GetOnlineHumCount()
        {
            return m_PlayObjectList.Count + m_AiPlayObjectList.Count;
        }

        private int GetUserCount()
        {
            return m_PlayObjectList.Count + m_AiPlayObjectList.Count;
        }

        private bool ProcessHumans_IsLogined(string sChrName)
        {
            var result = false;
            if (M2Share.FrontEngine.InSaveRcdList(sChrName))
            {
                result = true;
            }
            else
            {
                for (var i = 0; i < m_PlayObjectList.Count; i++)
                {
                    if (string.Compare(m_PlayObjectList[i].m_sCharName, sChrName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        result = true;
                        break;
                    }
                }
            }
            return result;
        }

        private TPlayObject ProcessHumans_MakeNewHuman(TUserOpenInfo UserOpenInfo)
        {
            TPlayObject result = null;
            TPlayObject PlayObject = null;
            TSwitchDataInfo SwitchDataInfo = null;
            const string sExceptionMsg = "[Exception] TUserEngine::MakeNewHuman";
            const string sChangeServerFail1 = "chg-server-fail-1 [{0}] -> [{1}] [{2}]";
            const string sChangeServerFail2 = "chg-server-fail-2 [{0}] -> [{1}] [{2}]";
            const string sChangeServerFail3 = "chg-server-fail-3 [{0}] -> [{1}] [{2}]";
            const string sChangeServerFail4 = "chg-server-fail-4 [{0}] -> [{1}] [{2}]";
            const string sErrorEnvirIsNil = "[Error] PlayObject.PEnvir = nil";
        ReGetMap:
            try
            {
                PlayObject = new TPlayObject();
                var nativeSessionSuffix = UserOpenInfo.NativeSessionSuffix;
                PlayObject.m_NativeDbSessionSuffix = nativeSessionSuffix is { Length: > 0 }
                    ? (byte[])nativeSessionSuffix.Clone()
                    : Array.Empty<byte>();
                if (!M2Share.g_Config.boVentureServer)
                {
                    SwitchDataInfo = null;
                }
                else
                {
                    SwitchDataInfo = GetSwitchData(UserOpenInfo.sChrName, UserOpenInfo.LoadUser.nSessionID);
                }
                Envirnoment Envir = null;
                if (SwitchDataInfo == null)
                {
                    GetHumData(PlayObject, ref UserOpenInfo.HumanRcd);
                    PlayObject.m_btRaceServer = Grobal2.RC_PLAYOBJECT;
                    if (string.IsNullOrEmpty(PlayObject.m_sHomeMap))
                    {
                        PlayObject.m_sHomeMap = GetHomeInfo(PlayObject.m_btJob, ref PlayObject.m_nHomeX, ref PlayObject.m_nHomeY);
                        PlayObject.m_sMapName = PlayObject.m_sHomeMap;
                        PlayObject.m_nCurrX = GetRandHomeX(PlayObject);
                        PlayObject.m_nCurrY = GetRandHomeY(PlayObject);
                        if (PlayObject.m_Abil.Level == 0)
                        {
                            var Abil = PlayObject.m_Abil;
                            Abil.Level = 1;
                            Abil.AC = 0;
                            Abil.MAC = 0;
                            Abil.DC = HUtil32.MakeLong(1, 2);
                            Abil.MC = HUtil32.MakeLong(1, 2);
                            Abil.SC = HUtil32.MakeLong(1, 2);
                            Abil.MP = 15;
                            Abil.HP = 15;
                            Abil.MaxHP = 15;
                            Abil.MaxMP = 15;
                            Abil.Exp = 0;
                            Abil.MaxExp = 100;
                            Abil.Weight = 0;
                            Abil.MaxWeight = 30;
                            PlayObject.m_boNewHuman = true;
                        }
                    }
                    Envir = M2Share.MapManager.FindMap(PlayObject.m_sMapName);
                    if (Envir != null)
                    {
                        PlayObject.m_sMapFileName = Envir.m_sMapFileName;
                        if (Envir.Flag.boFight3Zone) 
                        {
                            if (PlayObject.m_Abil.HP <= 0 && PlayObject.m_nFightZoneDieCount < 3)
                            {
                                PlayObject.m_Abil.HP = PlayObject.m_Abil.MaxHP;
                                PlayObject.m_Abil.MP = PlayObject.m_Abil.MaxMP;
                                PlayObject.m_boDieInFight3Zone = true;
                            }
                            else
                            {
                                PlayObject.m_nFightZoneDieCount = 0;
                            }
                        }
                    }
                    PlayObject.m_MyGuild = M2Share.GuildManager.MemberOfGuild(PlayObject.m_sCharName);
                    var userCastle = M2Share.CastleManager.InCastleWarArea(Envir, PlayObject.m_nCurrX, PlayObject.m_nCurrY);
                    if (Envir != null && userCastle != null && (userCastle.m_MapPalace == Envir || userCastle.m_boUnderWar))
                    {
                        userCastle = M2Share.CastleManager.IsCastleMember(PlayObject);
                        if (userCastle == null)
                        {
                            PlayObject.m_sMapName = PlayObject.m_sHomeMap;
                            PlayObject.m_nCurrX = (short)(PlayObject.m_nHomeX - 2 + M2Share.RandomNumber.Random(5));
                            PlayObject.m_nCurrY = (short)(PlayObject.m_nHomeY - 2 + M2Share.RandomNumber.Random(5));
                        }
                        else
                        {
                            if (userCastle.m_MapPalace == Envir)
                            {
                                PlayObject.m_sMapName = userCastle.GetMapName();
                                PlayObject.m_nCurrX = userCastle.GetHomeX();
                                PlayObject.m_nCurrY = userCastle.GetHomeY();
                            }
                        }
                    }
                    if (PlayObject.nC4 <= 1 && PlayObject.m_Abil.Level >= 1) PlayObject.nC4 = 2;
                    if (M2Share.MapManager.FindMap(PlayObject.m_sMapName) == null) PlayObject.m_Abil.HP = 0;
                    if (PlayObject.m_Abil.HP <= 0)
                    {
                        PlayObject.ClearStatusTime();
                        if (PlayObject.PKLevel() < 2)
                        {
                            userCastle = M2Share.CastleManager.IsCastleMember(PlayObject);
                            if (userCastle != null && userCastle.m_boUnderWar)
                            {
                                PlayObject.m_sMapName = userCastle.m_sHomeMap;
                                PlayObject.m_nCurrX = userCastle.GetHomeX();
                                PlayObject.m_nCurrY = userCastle.GetHomeY();
                            }
                            else
                            {
                                PlayObject.m_sMapName = PlayObject.m_sHomeMap;
                                PlayObject.m_nCurrX = (short)(PlayObject.m_nHomeX - 2 + M2Share.RandomNumber.Random(5));
                                PlayObject.m_nCurrY = (short)(PlayObject.m_nHomeY - 2 + M2Share.RandomNumber.Random(5));
                            }
                        }
                        else
                        {
                            PlayObject.m_sMapName = M2Share.g_Config.sRedDieHomeMap;// '3'
                            PlayObject.m_nCurrX = (short)(M2Share.RandomNumber.Random(13) + M2Share.g_Config.nRedDieHomeX);// 839
                            PlayObject.m_nCurrY = (short)(M2Share.RandomNumber.Random(13) + M2Share.g_Config.nRedDieHomeY);// 668
                        }
                        PlayObject.m_Abil.HP = 14;
                    }
                    PlayObject.AbilCopyToWAbil();
                    Envir = M2Share.MapManager.FindMap(PlayObject.m_sMapName);
                    if (Envir == null)
                    {
                        PlayObject.m_nSessionID = UserOpenInfo.LoadUser.nSessionID;
                        PlayObject.m_nSocket = UserOpenInfo.LoadUser.nSocket;
                        PlayObject.m_nGateIdx = UserOpenInfo.LoadUser.nGateIdx;
                        PlayObject.m_nGSocketIdx = UserOpenInfo.LoadUser.nGSocketIdx;
                        PlayObject.m_UserGeneration =
                            UserOpenInfo.LoadUser.UserGeneration;
                        PlayObject.m_WAbil = PlayObject.m_Abil;
                        PlayObject.m_nServerIndex = M2Share.MapManager.GetMapOfServerIndex(PlayObject.m_sMapName);
                        if (PlayObject.m_Abil.HP != 14)
                        {
                            M2Share.MainOutMessage(string.Format(sChangeServerFail1, new object[] { M2Share.nServerIndex, PlayObject.m_nServerIndex, PlayObject.m_sMapName }));
                        }
                        SendSwitchData(PlayObject, PlayObject.m_nServerIndex);
                        SendChangeServer(PlayObject, (byte)PlayObject.m_nServerIndex);
                        ReleaseUnpublishedObject(PlayObject);
                        PlayObject = null;
                        return result;
                    }
                    PlayObject.m_sMapFileName = Envir.m_sMapFileName;
                    var nC = 0;
                    while (true)
                    {
                        if (Envir.CanWalk(PlayObject.m_nCurrX, PlayObject.m_nCurrY, true)) break;
                        PlayObject.m_nCurrX = (short)(PlayObject.m_nCurrX - 3 + M2Share.RandomNumber.Random(6));
                        PlayObject.m_nCurrY = (short)(PlayObject.m_nCurrY - 3 + M2Share.RandomNumber.Random(6));
                        nC++;
                        if (nC >= 5) break;
                    }
                    if (!Envir.CanWalk(PlayObject.m_nCurrX, PlayObject.m_nCurrY, true))
                    {
                        M2Share.MainOutMessage(string.Format(sChangeServerFail2,
                            new object[] { M2Share.nServerIndex, PlayObject.m_nServerIndex, PlayObject.m_sMapName }));
                        PlayObject.m_sMapName = M2Share.g_Config.sHomeMap;
                        Envir = M2Share.MapManager.FindMap(M2Share.g_Config.sHomeMap);
                        PlayObject.m_nCurrX = M2Share.g_Config.nHomeX;
                        PlayObject.m_nCurrY = M2Share.g_Config.nHomeY;
                    }
                    if (Envir == null)
                    {
                        M2Share.MainOutMessage(sErrorEnvirIsNil);
                        ReleaseUnpublishedObject(PlayObject);
                        return result;
                    }
                    PlayObject.m_PEnvir = Envir;
                    PlayObject.OnEnvirnomentChanged();
                    if (PlayObject.m_PEnvir == null)
                    {
                        M2Share.MainOutMessage(sErrorEnvirIsNil);
                        ReleaseUnpublishedObject(PlayObject);
                        goto ReGetMap;
                    }
                    else
                        PlayObject.m_boReadyRun = false;
                    PlayObject.m_sMapFileName = Envir.m_sMapFileName;
                }
                else
                {
                    GetHumData(PlayObject, ref UserOpenInfo.HumanRcd);
                    PlayObject.m_sMapName = SwitchDataInfo.sMap;
                    PlayObject.m_nCurrX = SwitchDataInfo.wX;
                    PlayObject.m_nCurrY = SwitchDataInfo.wY;
                    PlayObject.m_Abil = SwitchDataInfo.Abil;
                    PlayObject.m_WAbil = SwitchDataInfo.Abil;
                    LoadSwitchData(SwitchDataInfo, ref PlayObject);
                    DelSwitchData(SwitchDataInfo);
                    Envir = M2Share.MapManager.FindMap(PlayObject.m_sMapName);
                    if (Envir == null)
                    {
                        M2Share.MainOutMessage(string.Format(sChangeServerFail3,
                            new object[] { M2Share.nServerIndex, PlayObject.m_nServerIndex, PlayObject.m_sMapName }));
                        PlayObject.m_sMapName = M2Share.g_Config.sHomeMap;
                        Envir = M2Share.MapManager.FindMap(M2Share.g_Config.sHomeMap);
                        PlayObject.m_nCurrX = M2Share.g_Config.nHomeX;
                        PlayObject.m_nCurrY = M2Share.g_Config.nHomeY;
                    }
                    if (Envir == null)
                    {
                        M2Share.MainOutMessage(sErrorEnvirIsNil);
                        ReleaseUnpublishedObject(PlayObject);
                        return result;
                    }
                    if (!Envir.CanWalk(PlayObject.m_nCurrX, PlayObject.m_nCurrY, true))
                    {
                        M2Share.MainOutMessage(string.Format(sChangeServerFail4,
                            new object[] { M2Share.nServerIndex, PlayObject.m_nServerIndex, PlayObject.m_sMapName }));
                        PlayObject.m_sMapName = M2Share.g_Config.sHomeMap;
                        Envir = M2Share.MapManager.FindMap(M2Share.g_Config.sHomeMap);
                        PlayObject.m_nCurrX = M2Share.g_Config.nHomeX;
                        PlayObject.m_nCurrY = M2Share.g_Config.nHomeY;
                    }
                    if (Envir == null)
                    {
                        M2Share.MainOutMessage(sErrorEnvirIsNil);
                        ReleaseUnpublishedObject(PlayObject);
                        return result;
                    }
                    PlayObject.AbilCopyToWAbil();
                    PlayObject.m_PEnvir = Envir;
                    PlayObject.OnEnvirnomentChanged();
                    if (PlayObject.m_PEnvir == null)
                    {
                        M2Share.MainOutMessage(sErrorEnvirIsNil);
                        ReleaseUnpublishedObject(PlayObject);
                        goto ReGetMap;
                    }
                    PlayObject.m_boReadyRun = false;
                    // 不提前设 true — 让游戏循环调用 RunNotice → SendNotice 发健康公告
                    PlayObject.bo6AB = true;
                }
                PlayObject.m_sLoginAccount = UserOpenInfo.LoadUser.sAccount;
                PlayObject.m_sIPaddr = UserOpenInfo.LoadUser.sIPaddr;
                PlayObject.m_sIPLocal = M2Share.GetIPLocal(PlayObject.m_sIPaddr);
                PlayObject.m_nSocket = UserOpenInfo.LoadUser.nSocket;
                PlayObject.m_nGSocketIdx = UserOpenInfo.LoadUser.nGSocketIdx;
                PlayObject.m_nGateIdx = UserOpenInfo.LoadUser.nGateIdx;
                PlayObject.m_nSessionID = UserOpenInfo.LoadUser.nSessionID;
                PlayObject.m_UserGeneration =
                    UserOpenInfo.LoadUser.UserGeneration;
                PlayObject.m_nPayMent = UserOpenInfo.LoadUser.nPayMent;
                PlayObject.m_nPayMode = UserOpenInfo.LoadUser.nPayMode;
                PlayObject.m_dwLoadTick = UserOpenInfo.LoadUser.dwNewUserTick;
                PlayObject.m_nSoftVersionDateEx = M2Share.GetExVersionNO(UserOpenInfo.LoadUser.nSoftVersionDate, ref PlayObject.m_nSoftVersionDate);
                result = PlayObject;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(ex.StackTrace);
                ReleaseUnpublishedObject(PlayObject);
            }
            return result;
        }

        private void ProcessHumans()
        {
            const string sExceptionMsg1 = "[Exception] TUserEngine::ProcessHumans -> Ready, Save, Load...";
            const string sExceptionMsg3 = "[Exception] TUserEngine::ProcessHumans ClosePlayer.Delete";
            var dwCheckTime = HUtil32.GetTickCount();
            TPlayObject PlayObject;
            if ((HUtil32.GetTickCount() - m_dwProcessLoadPlayTick) > 200)
            {
                m_dwProcessLoadPlayTick = HUtil32.GetTickCount();
                try
                {
                    HUtil32.EnterCriticalSection(m_LoadPlaySection);
                    try
                    {
                        for (var i = 0; i < m_LoadPlayList.Count; i++)
                        {
                            TUserOpenInfo UserOpenInfo;
                            var pendingOpen = m_LoadPlayList[i];
                            if (pendingOpen?.LoadUser == null ||
                                (pendingOpen.LoadUser.UserGeneration != 0 &&
                                 !M2Share.GateManager.IsCurrentUser(
                                     pendingOpen.LoadUser.nGateIdx,
                                     pendingOpen.LoadUser.nSocket,
                                     pendingOpen.LoadUser.UserGeneration)))
                            {
                                m_LoadPlayList[i] = null;
                                continue;
                            }
                            if (!M2Share.FrontEngine.IsFull() && !ProcessHumans_IsLogined(m_LoadPlayList[i].sChrName))
                            {
                                UserOpenInfo = m_LoadPlayList[i];
                                PlayObject = ProcessHumans_MakeNewHuman(UserOpenInfo);
                                if (PlayObject != null)
                                {
                                    if (PlayObject.m_boAI)
                                    {
                                        m_AiPlayObjectList.Add(PlayObject);
                                    }
                                    else
                                    {
                                        m_PlayObjectList.Add(PlayObject);
                                    }
                                    m_NewHumanList.Add(PlayObject);
                                    SendServerGroupMsg(Grobal2.ISM_USERLOGON, M2Share.nServerIndex, PlayObject.m_sCharName);
                                }
                                else
                                {
                                    M2Share.MainOutMessage($"[OpenMakeNil] account={UserOpenInfo.LoadUser.sAccount} chr={UserOpenInfo.sChrName} gate={UserOpenInfo.LoadUser.nGateIdx} socket={UserOpenInfo.LoadUser.nSocket}");
                                }
                            }
                            else
                            {
                                KickOnlineUser(m_LoadPlayList[i].sChrName);
                                UserOpenInfo = m_LoadPlayList[i];
                                M2Share.MainOutMessage($"[OpenSkipOnline] account={UserOpenInfo.LoadUser.sAccount} chr={UserOpenInfo.sChrName} gate={UserOpenInfo.LoadUser.nGateIdx} socket={UserOpenInfo.LoadUser.nSocket}");
                                m_ListOfGateIdx.Add(UserOpenInfo.LoadUser.nGateIdx);
                                m_ListOfSocket.Add(UserOpenInfo.LoadUser.nSocket);
                                m_ListOfUserGeneration.Add(
                                    UserOpenInfo.LoadUser.UserGeneration);
                            }
                            m_LoadPlayList[i] = null;
                        }
                        m_LoadPlayList.Clear();
                        for (var i = 0; i < m_ChangeHumanDBGoldList.Count; i++)
                        {
                            var GoldChangeInfo = m_ChangeHumanDBGoldList[i];
                            PlayObject = GetPlayObject(GoldChangeInfo.sGameMasterName);
                            if (PlayObject != null)
                            {
                                if (GoldChangeInfo.Succeeded)
                                    PlayObject.GoldChange(GoldChangeInfo.sGetGoldUser, GoldChangeInfo.nGold);
                                else
                                    PlayObject.SysMsg(
                                        $"{GoldChangeInfo.sGetGoldUser} 的离线金币调整失败: {GoldChangeInfo.FailureReason}",
                                        MsgColor.Red, MsgType.Hint);
                            }
                            GoldChangeInfo = null;
                        }
                        m_ChangeHumanDBGoldList.Clear();
                    }
                    finally
                    {
                        HUtil32.LeaveCriticalSection(m_LoadPlaySection);
                    }
                    var newHumans = TakeNewHumansForBinding();
                    for (var i = 0; i < newHumans.Count; i++)
                    {
                        PlayObject = newHumans[i];
                        if (!M2Share.GateManager.SetGateUserList(
                                PlayObject.m_nGateIdx, PlayObject.m_nSocket,
                                PlayObject))
                        {
                            PlayObject.m_boEmergencyClose = true;
                            PlayObject.m_boSoftClose = true;
                        }
                    }
                    for (var i = 0; i < m_ListOfGateIdx.Count; i++)
                    {
                        M2Share.GateManager.CloseUser(
                            (int)m_ListOfGateIdx[i],
                            (int)m_ListOfSocket[i],
                            (long)m_ListOfUserGeneration[i]);
                    }
                    m_ListOfGateIdx.Clear();
                    m_ListOfSocket.Clear();
                    m_ListOfUserGeneration.Clear();
                }
                catch (Exception e)
                {
                    M2Share.ErrorMessage(sExceptionMsg1);
                    M2Share.ErrorMessage(e.Message);
                }
            }

            
            if (m_UserLogonList.Count > 0)
            {
                if (HUtil32.GetTickCount() - m_dwAILogonTick > 1000)
                {
                    m_dwAILogonTick = HUtil32.GetTickCount();
                    if (m_UserLogonList.Count > 0)
                    {
                        var AI = m_UserLogonList[0];
                        RegenAIObject(AI);
                        m_UserLogonList.RemoveAt(0);
                    }
                }
            }

            try
            {
                for (var i = 0; i < m_PlayObjectFreeList.Count; i++)
                {
                    PlayObject = m_PlayObjectFreeList[i];
                    if ((HUtil32.GetTickCount() - PlayObject.m_dwGhostTick) > M2Share.g_Config.dwHumanFreeDelayTime)// 5 * 60 * 1000
                    {
                        M2Share.ObjectManager.Remove(PlayObject.ObjectId);
                        m_PlayObjectFreeList[i] = null;
                        m_PlayObjectFreeList.RemoveAt(i);
                        break;
                    }
                    if (PlayObject.m_boSwitchData && PlayObject.m_boRcdSaved)
                    {
                        if (SendSwitchData(PlayObject, PlayObject.m_nServerIndex) || PlayObject.m_nWriteChgDataErrCount > 20)
                        {
                            PlayObject.m_boSwitchData = false;
                            PlayObject.m_boSwitchDataOK = true;
                            PlayObject.m_boSwitchDataSended = true;
                            PlayObject.m_dwChgDataWritedTick = HUtil32.GetTickCount();
                        }
                        else
                        {
                            PlayObject.m_nWriteChgDataErrCount++;
                        }
                    }
                    if (PlayObject.m_boSwitchDataSended && HUtil32.GetTickCount() - PlayObject.m_dwChgDataWritedTick > 100)
                    {
                        PlayObject.m_boSwitchDataSended = false;
                        SendChangeServer(PlayObject, (byte)PlayObject.m_nServerIndex);
                    }
                }
            }
            catch
            {
                M2Share.MainOutMessage(sExceptionMsg3);
            }

            ProcessPlayObjectData();

            nProcessHumanLoopTime++;
            M2Share.g_nProcessHumanLoopTime = nProcessHumanLoopTime;
            if (m_nProcHumIDx == 0)
            {
                nProcessHumanLoopTime = 0;
                M2Share.g_nProcessHumanLoopTime = nProcessHumanLoopTime;
                var dwUsrRotTime = HUtil32.GetTickCount() - M2Share.g_dwUsrRotCountTick;
                M2Share.dwUsrRotCountMin = dwUsrRotTime;
                M2Share.g_dwUsrRotCountTick = HUtil32.GetTickCount();
                if (M2Share.dwUsrRotCountMax < dwUsrRotTime) M2Share.dwUsrRotCountMax = dwUsrRotTime;
            }
            M2Share.g_nHumCountMin = HUtil32.GetTickCount() - dwCheckTime;
            if (M2Share.g_nHumCountMax < M2Share.g_nHumCountMin) M2Share.g_nHumCountMax = M2Share.g_nHumCountMin;
        }

        private static bool TryGetHeroPosition(TPlayObject owner, HeroObject hero, out short x, out short y)
        {
            x = hero.m_nCurrX;
            y = hero.m_nCurrY;
            var envir = owner.m_PEnvir;
            if (envir == null)
            {
                return false;
            }

            if ((x != owner.m_nCurrX || y != owner.m_nCurrY)
                && Math.Abs(x - owner.m_nCurrX) <= 3
                && Math.Abs(y - owner.m_nCurrY) <= 3
                && envir.CanWalk(x, y, false))
            {
                return true;
            }

            for (var range = 1; range <= 3; range++)
            {
                for (var offsetY = -range; offsetY <= range; offsetY++)
                {
                    for (var offsetX = -range; offsetX <= range; offsetX++)
                    {
                        if (Math.Abs(offsetX) != range && Math.Abs(offsetY) != range)
                        {
                            continue;
                        }

                        var candidateX = owner.m_nCurrX + offsetX;
                        var candidateY = owner.m_nCurrY + offsetY;
                        if (envir.CanWalk(candidateX, candidateY, false))
                        {
                            x = (short)candidateX;
                            y = (short)candidateY;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void RemoveActiveHeroLocked(HeroObject hero)
        {
            var index = m_HeroObjectList.IndexOf(hero);
            if (index < 0)
            {
                return;
            }

            m_HeroObjectList.RemoveAt(index);
            if (index < m_nProcHeroIdx)
            {
                m_nProcHeroIdx--;
            }
            if (m_nProcHeroIdx > m_HeroObjectList.Count)
            {
                m_nProcHeroIdx = m_HeroObjectList.Count;
            }
        }

        private bool QueueHeroForFreeLocked(HeroObject hero, int freeTick)
        {
            if (hero == null)
            {
                RemoveActiveHeroLocked(null);
                return true;
            }

            if (hero.NativeHeroState != null && !HeroDataService.QueueSave(hero, 1))
                return false;

            RemoveActiveHeroLocked(hero);

            var owner = hero.m_Master as TPlayObject;
            if (owner != null && ReferenceEquals(owner.m_HeroObject, hero))
            {
                owner.m_HeroObject = null;
            }

            if (!hero.m_boGhost)
            {
                hero.MakeGhost();
            }
            hero.m_dwGhostTick = freeTick;
            hero.ReleaseRuntimeReferences();

            if (m_HeroFreeObjectIds.Add(hero.ObjectId))
            {
                m_HeroObjectFreeList.Enqueue(new HeroFreeInfo
                {
                    Hero = hero,
                    FreeTick = freeTick
                });
            }
            return true;
        }

        private static void ReleaseUnpublishedHero(HeroObject hero)
        {
            if (hero == null)
            {
                return;
            }

            hero.m_Master = null;
            if (hero.m_PEnvir != null && hero.m_boAddToMaped)
            {
                hero.MakeGhost();
            }
            else
            {
                hero.m_boGhost = true;
                hero.m_dwGhostTick = HUtil32.GetTickCount();
            }
            hero.ReleaseRuntimeReferences();
            M2Share.ObjectManager.Remove(hero.ObjectId);
        }

        public bool RegisterHero(TPlayObject owner, HeroObject hero)
        {
            if (hero == null)
            {
                return false;
            }
            if (owner == null)
            {
                if (!IsHeroManaged(hero))
                {
                    ReleaseUnpublishedHero(hero);
                }
                return false;
            }

            lock (m_HeroSync)
            {
                if (owner.m_boGhost || owner.m_PEnvir == null || string.IsNullOrEmpty(hero.m_sCharName))
                {
                    if (!hero.m_boGhost)
                    {
                        ReleaseUnpublishedHero(hero);
                    }
                    return false;
                }
                if (hero.m_boGhost)
                {
                    if (!m_HeroObjectList.Contains(hero) && !m_HeroFreeObjectIds.Contains(hero.ObjectId))
                    {
                        ReleaseUnpublishedHero(hero);
                    }
                    return false;
                }

                if (m_HeroObjectList.Contains(hero))
                {
                    if (hero.m_Master != null && !ReferenceEquals(hero.m_Master, owner))
                    {
                        return false;
                    }
                    hero.MasterName = owner.m_sCharName;
                    hero.m_Master = owner;
                    owner.m_HeroObject = hero;
                    return true;
                }

                if (!TryGetHeroPosition(owner, hero, out var heroX, out var heroY))
                {
                    ReleaseUnpublishedHero(hero);
                    return false;
                }

                if (owner.m_HeroObject != null && !ReferenceEquals(owner.m_HeroObject, hero))
                {
                    if (!QueueHeroForFreeLocked(owner.m_HeroObject, HUtil32.GetTickCount()))
                    {
                        ReleaseUnpublishedHero(hero);
                        return false;
                    }
                }

                for (var i = m_HeroObjectList.Count - 1; i >= 0; i--)
                {
                    var activeHero = m_HeroObjectList[i];
                    if (activeHero == null
                        || ReferenceEquals(activeHero.m_Master, owner)
                        || (!string.IsNullOrEmpty(hero.m_sCharName)
                            && string.Equals(activeHero.m_sCharName, hero.m_sCharName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!QueueHeroForFreeLocked(activeHero, HUtil32.GetTickCount()))
                        {
                            ReleaseUnpublishedHero(hero);
                            return false;
                        }
                    }
                }

                hero.MasterName = owner.m_sCharName;
                hero.m_Master = owner;
                hero.m_PEnvir = owner.m_PEnvir;
                hero.m_sMapName = owner.m_sMapName;
                hero.m_sMapFileName = owner.m_sMapFileName;
                hero.m_nCurrX = heroX;
                hero.m_nCurrY = heroY;
                hero.m_boAddToMaped = false;
                hero.m_boDelFormMaped = true;

                try
                {
                    hero.OnEnvirnomentChanged();
                    hero.Initialize();
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage($"[Exception] TUserEngine::RegisterHero {ex}");
                    ReleaseUnpublishedHero(hero);
                    return false;
                }

                if (hero.m_boAddtoMapSuccess || !hero.m_boAddToMaped)
                {
                    ReleaseUnpublishedHero(hero);
                    return false;
                }

                hero.m_dwRunTick = HUtil32.GetTickCount();
                owner.m_HeroObject = hero;
                m_HeroObjectList.Add(hero);
                return true;
            }
        }

        public bool RemoveHero(TPlayObject owner)
        {
            if (owner == null)
            {
                return false;
            }

            lock (m_HeroSync)
            {
                var hero = owner.m_HeroObject;
                if (hero == null)
                {
                    for (var i = 0; i < m_HeroObjectList.Count; i++)
                    {
                        if (ReferenceEquals(m_HeroObjectList[i]?.m_Master, owner))
                        {
                            hero = m_HeroObjectList[i];
                            break;
                        }
                    }
                }
                if (hero == null)
                {
                    return false;
                }

                return QueueHeroForFreeLocked(hero, HUtil32.GetTickCount());
            }
        }

        public bool RemoveHero(HeroObject hero)
        {
            if (hero == null)
            {
                return false;
            }

            lock (m_HeroSync)
            {
                if (!m_HeroObjectList.Contains(hero) && m_HeroFreeObjectIds.Contains(hero.ObjectId))
                {
                    return false;
                }
                return QueueHeroForFreeLocked(hero, HUtil32.GetTickCount());
            }
        }

        public HeroObject GetHero(string heroName)
        {
            if (string.IsNullOrEmpty(heroName))
            {
                return null;
            }

            lock (m_HeroSync)
            {
                for (var i = 0; i < m_HeroObjectList.Count; i++)
                {
                    var hero = m_HeroObjectList[i];
                    if (hero != null && string.Equals(hero.m_sCharName, heroName, StringComparison.OrdinalIgnoreCase))
                    {
                        return hero;
                    }
                }
            }
            return null;
        }

        internal bool IsHeroManaged(HeroObject hero)
        {
            if (hero == null)
            {
                return false;
            }

            lock (m_HeroSync)
            {
                return m_HeroObjectList.Contains(hero) || m_HeroFreeObjectIds.Contains(hero.ObjectId);
            }
        }

        private void ProcessHeroes()
        {
            var processStart = HUtil32.GetTickCount();
            HeroDataService.Process(this);
            lock (m_HeroSync)
            {
                while (m_HeroObjectFreeList.Count > 0)
                {
                    var freeInfo = m_HeroObjectFreeList.Peek();
                    if ((processStart - freeInfo.FreeTick) < HeroFreeDelay)
                    {
                        break;
                    }
                    m_HeroObjectFreeList.Dequeue();
                    m_HeroFreeObjectIds.Remove(freeInfo.Hero.ObjectId);
                    M2Share.ObjectManager.Remove(freeInfo.Hero.ObjectId);
                }
            }

            while (true)
            {
                HeroObject hero;
                lock (m_HeroSync)
                {
                    if (m_nProcHeroIdx >= m_HeroObjectList.Count)
                    {
                        m_nProcHeroIdx = 0;
                        break;
                    }

                    hero = m_HeroObjectList[m_nProcHeroIdx];
                    if (hero == null || hero.m_boGhost)
                    {
                        if (!QueueHeroForFreeLocked(hero, HUtil32.GetTickCount()))
                            m_nProcHeroIdx++;
                        continue;
                    }
                    m_nProcHeroIdx++;
                }

                var currentTick = HUtil32.GetTickCount();
                if (!hero.m_boGhost && (currentTick - hero.m_dwRunTick) >= HeroRunInterval)
                {
                    hero.m_dwRunTick = currentTick;
                    try
                    {
                        if ((currentTick - hero.m_dwSearchTick) > hero.m_dwSearchTime)
                        {
                            hero.m_dwSearchTick = currentTick;
                            hero.SearchViewRange();
                        }
                        hero.Run();
                        HeroDataService.QueuePeriodicSave(hero, currentTick);
                    }
                    catch (Exception ex)
                    {
                        M2Share.ErrorMessage($"[Exception] TUserEngine::ProcessHero {hero.m_sCharName}: {ex}");
                    }
                }

                if ((HUtil32.GetTickCount() - processStart) > HeroProcessBudget)
                {
                    break;
                }
            }
        }

        private void ProcessAiPlayObjectData()
        {
            const string sExceptionMsg8 = "[Exception] TUserEngine::ProcessHumans";
            try
            {
                while (M2Share.boStartReady && !_stopRequested)
                {
                    HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
                    try
                    {
                        var dwCurTick = HUtil32.GetTickCount();
                        var nIdx = m_nProcHumIDx;
                        var boCheckTimeLimit = false;
                        var dwCheckTime = HUtil32.GetTickCount();
                        while (true)
                        {
                            if (m_AiPlayObjectList.Count <= nIdx) break;
                            var PlayObject = m_AiPlayObjectList[nIdx];
                            if (dwCurTick - PlayObject.m_dwRunTick > PlayObject.m_nRunTime)
                            {
                                PlayObject.m_dwRunTick = dwCurTick;
                                if (!PlayObject.m_boGhost)
                                {
                                    if (!PlayObject.m_boLoginNoticeOK)
                                    {
                                        PlayObject.SendNativeClientConfig();
                                        PlayObject.RunNotice();
                                    }
                                    else
                                    {
                                        if (!PlayObject.m_boReadyRun)
                                        {
                                            PlayObject.m_boReadyRun = true;
                                            PlayObject.UserLogon();
                                        }
                                        else
                                        {
                                            if ((HUtil32.GetTickCount() - PlayObject.m_dwSearchTick) > PlayObject.m_dwSearchTime)
                                            {
                                                PlayObject.m_dwSearchTick = HUtil32.GetTickCount();
                                                PlayObject.SearchViewRange();
                                                PlayObject.GameTimeChanged();
                                            }
                                            PlayObject.Run();
                                        }
                                    }
                                }
                                else
                                {
                                    m_AiPlayObjectList.Remove(PlayObject);
                                    PlayObject.Disappear();
                                    AddToHumanFreeList(PlayObject);
                                    PlayObject.DealCancelA();
                                    SaveHumanRcd(PlayObject, 3);
                                    M2Share.GateManager.CloseUser(
                                        PlayObject.m_nGateIdx,
                                        PlayObject.m_nSocket,
                                        PlayObject.m_UserGeneration);
                                    SendServerGroupMsg(Grobal2.SS_202, M2Share.nServerIndex, PlayObject.m_sCharName);
                                    continue;
                                }
                            }
                            nIdx++;
                            if ((HUtil32.GetTickCount() - dwCheckTime) > M2Share.g_dwHumLimit)
                            {
                                boCheckTimeLimit = true;
                                m_nProcHumIDx = nIdx;
                                break;
                            }
                        }
                        if (!boCheckTimeLimit) m_nProcHumIDx = 0;
                    }
                    finally
                    {
                        HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
                    }

                    Thread.Sleep(30);
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage(sExceptionMsg8);
                M2Share.MainOutMessage(ex.StackTrace);
            }
        }

        private void ProcessPlayObjectData()
        {
            try
            {
                var dwCurTick = HUtil32.GetTickCount();
                var nIdx = m_nProcHumIDx;
                var boCheckTimeLimit = false;
                var dwCheckTime = HUtil32.GetTickCount();
                while (true)
                {
                    if (m_PlayObjectList.Count <= nIdx) break;
                    var PlayObject = m_PlayObjectList[nIdx];
                    if (PlayObject == null)
                    {
                        nIdx++;
                        if ((HUtil32.GetTickCount() - dwCheckTime) > M2Share.g_dwHumLimit)
                        {
                            boCheckTimeLimit = true;
                            m_nProcHumIDx = nIdx;
                            break;
                        }
                        continue;
                    }
                    if ((dwCurTick - PlayObject.m_dwRunTick) > PlayObject.m_nRunTime)
                    {
                        PlayObject.m_dwRunTick = dwCurTick;
                        if (!PlayObject.m_boGhost)
                        {
                            if (!PlayObject.m_boLoginNoticeOK)
                            {
                                PlayObject.SendNativeClientConfig();
                                PlayObject.RunNotice();
                            }
                            else
                            {
                                if (!PlayObject.m_boReadyRun)
                                {
                                    PlayObject.m_boReadyRun = true;
                                    PlayObject.UserLogon();
                                }
                                else
                                {
                                    if ((HUtil32.GetTickCount() - PlayObject.m_dwSearchTick) > PlayObject.m_dwSearchTime)
                                    {
                                        PlayObject.m_dwSearchTick = HUtil32.GetTickCount();
                                        PlayObject.SearchViewRange();//搜索对像
                                        PlayObject.GameTimeChanged();//游戏时间改变
                                    }
                                    if ((HUtil32.GetTickCount() - PlayObject.m_dwShowLineNoticeTick) > M2Share.g_Config.dwShowLineNoticeTime)
                                    {
                                        PlayObject.m_dwShowLineNoticeTick = HUtil32.GetTickCount();
                                        if (M2Share.LineNoticeList.Count > PlayObject.m_nShowLineNoticeIdx)
                                        {
                                            var lineNoticeMsg = NormNpc.FormatLineVariableText(PlayObject, M2Share.LineNoticeList[PlayObject.m_nShowLineNoticeIdx]);
                                            if (lineNoticeMsg.Length == 0)
                                            {
                                                lineNoticeMsg = " ";
                                            }
                                            switch (lineNoticeMsg[0])
                                            {
                                                case 'R':
                                                    PlayObject.SysMsg(lineNoticeMsg.Substring(1, lineNoticeMsg.Length - 1), MsgColor.Red, MsgType.Notice);
                                                    break;
                                                case 'G':
                                                    PlayObject.SysMsg(lineNoticeMsg.Substring(1, lineNoticeMsg.Length - 1), MsgColor.Green, MsgType.Notice);
                                                    break;
                                                case 'B':
                                                    PlayObject.SysMsg(lineNoticeMsg.Substring(1, lineNoticeMsg.Length - 1), MsgColor.Blue, MsgType.Notice);
                                                    break;
                                                default:
                                                    PlayObject.SysMsg(lineNoticeMsg, (MsgColor)M2Share.g_Config.nLineNoticeColor, MsgType.Notice);
                                                    break;
                                            }
                                        }
                                        PlayObject.m_nShowLineNoticeIdx++;
                                        if (M2Share.LineNoticeList.Count <= PlayObject.m_nShowLineNoticeIdx)
                                        {
                                            PlayObject.m_nShowLineNoticeIdx = 0;
                                        }
                                    }
                                    PlayObject.Run();
                                    if (!M2Share.FrontEngine.IsFull() && (HUtil32.GetTickCount() - PlayObject.m_dwSaveRcdTick) > M2Share.g_Config.dwSaveHumanRcdTime)
                                    {
                                        PlayObject.m_dwSaveRcdTick = HUtil32.GetTickCount();
                                        PlayObject.DealCancelA();
                                        SaveHumanRcd(PlayObject);
                                    }
                                }
                            }
                        }
                        else
                        {
                            m_PlayObjectList.Remove(PlayObject);
                            PlayObject.Disappear();
                            AddToHumanFreeList(PlayObject);
                            PlayObject.DealCancelA();
                            SaveHumanRcd(PlayObject, 3);
                            M2Share.GateManager.CloseUser(
                                PlayObject.m_nGateIdx, PlayObject.m_nSocket,
                                PlayObject.m_UserGeneration);
                            SendServerGroupMsg(Grobal2.ISM_USERLOGOUT, M2Share.nServerIndex, PlayObject.m_sCharName);
                            continue;
                        }
                    }
                    nIdx++;
                    if ((HUtil32.GetTickCount() - dwCheckTime) > M2Share.g_dwHumLimit)
                    {
                        boCheckTimeLimit = true;
                        m_nProcHumIDx = nIdx;
                        break;
                    }
                }
                if (!boCheckTimeLimit) m_nProcHumIDx = 0;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[Exception] TUserEngine::ProcessHumans");
                M2Share.MainOutMessage(ex.StackTrace);
            }
        }

        private void ProcessMerchants()
        {
            var boProcessLimit = false;
            const string sExceptionMsg = "[Exception] TUserEngine::ProcessMerchants";
            var dwRunTick = HUtil32.GetTickCount();
            try
            {
                var dwCurrTick = HUtil32.GetTickCount();
                for (var i = nMerchantPosition; i < m_MerchantList.Count; i++)
                {
                    var merchantNpc = m_MerchantList[i];
                    if (!merchantNpc.m_boGhost)
                    {
                        if ((dwCurrTick - merchantNpc.m_dwRunTick) > merchantNpc.m_nRunTime)
                        {
                            if ((HUtil32.GetTickCount() - merchantNpc.m_dwSearchTick) > merchantNpc.m_dwSearchTime)
                            {
                                merchantNpc.m_dwSearchTick = HUtil32.GetTickCount();
                                merchantNpc.SearchViewRange();
                            }
                            if ((dwCurrTick - merchantNpc.m_dwRunTick) > merchantNpc.m_nRunTime)
                            {
                                merchantNpc.m_dwRunTick = dwCurrTick;
                                merchantNpc.Run();
                            }
                        }
                    }
                    else
                    {
                        if ((HUtil32.GetTickCount() - merchantNpc.m_dwGhostTick) > 60 * 1000)
                        {
                            // 同 ProcessNpcs：战神 ProcessMon 循环3（0x67C614..0x67C63B）在
                            // 摘表之外还有 sub_67D8F0 入队 + vtable+0x7C 钩子。补上全局注册表回收，
                            // 走身份校验重载以保住 PasEngine 脚本状态清理。
                            // 60s 门是既有值（战神此处实为 5 分钟 FIFO，既存次要偏差，本次不改）。
                            TryRemoveMerchantExact(merchantNpc);
                            M2Share.ObjectManager.Remove(merchantNpc.ObjectId, merchantNpc);
                            break;
                        }
                    }
                    if ((HUtil32.GetTickCount() - dwRunTick) > M2Share.g_dwNpcLimit)
                    {
                        nMerchantPosition = i;
                        boProcessLimit = true;
                        break;
                    }
                }
                if (!boProcessLimit)
                {
                    nMerchantPosition = 0;
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
            dwProcessMerchantTimeMin = HUtil32.GetTickCount() - dwRunTick;
            if (dwProcessMerchantTimeMin > dwProcessMerchantTimeMax)
            {
                dwProcessMerchantTimeMax = dwProcessMerchantTimeMin;
            }
            if (dwProcessNpcTimeMin > dwProcessNpcTimeMax)
            {
                dwProcessNpcTimeMax = dwProcessNpcTimeMin;
            }
        }

        private void ProcessMissions()
        {

        }

        public int ProcessMonsters_GetZenTime(int dwTime)
        {
            int result;
            if (dwTime < 30 * 60 * 1000)
            {
                var d10 = (double)(PlayObjectCount - M2Share.g_Config.nUserFull) / HUtil32._MAX(1, M2Share.g_Config.nZenFastStep);
                if (d10 > 0)
                {
                    if (d10 > 6) d10 = 6;
                    result = (int)(dwTime - Math.Round(dwTime / 10.0 * d10));
                }
                else
                {
                    result = dwTime;
                }
            }
            else
            {
                result = dwTime;
            }
            return result;
        }

        private void ProcessMonsters()
        {
            bool boCanCreate;
            var dwRunTick = HUtil32.GetTickCount();
            AnimalObject Monster = null;
            try
            {
                var boProcessLimit = false;
                var dwCurrentTick = HUtil32.GetTickCount();
                MonGenInfo MonGen = null;
                
                if ((HUtil32.GetTickCount() - dwRegenMonstersTick) > M2Share.g_Config.dwRegenMonstersTime)
                {
                    dwRegenMonstersTick = HUtil32.GetTickCount();
                    // 战神 ProcessMon sub_67C150 Phase-B, 0x67C245-0x67C2C1.  The whole
                    // block is a cursor scan over up to 80 generators per pass, not a
                    // single generator per pass:
                    //   67C248  8B 40 2C        mov eax,[self+0x2C]   ; MonGenList
                    //   67C24B  E8 40 A8 D8 FF  call 0x406A90         ; @DynArrayHigh
                    //   67C250  8B D8           mov ebx,eax           ; ebx = Count-1
                    //   67C252  BF 50 00 00 00  mov edi,0x50          ; 80 iterations
                    //   67C25A  3B 58 48        cmp ebx,[self+0x48]   ; High vs cursor
                    //   67C25D  7D 0A           jge 0x67C269
                    //   67C264  89 50 48        mov [self+0x48],edx   ; wrap: cursor = 0
                    //   67C267  EB 5A           jmp 0x67C2C3          ;   ...and stop
                    //   67C275  8B 04 82        mov eax,[edx+eax*4]   ; gen = list[cursor]
                    //   67C27A  8B 46 24 / 3B 46 2C / 7E 38  nCount > nActiveCount
                    //   67C282  80 7E 0C 00 / 74 32          sMonName length byte
                    //   67C288  83 7E 1C 00 / 74 2C          template record resolved
                    //   67C28E  8B 45 EC / 2B 46 34 / 3B 46 30 / 76 21   elapsed > ZenTime
                    //   67C2AB  E8 30 07 00 00  call 0x67C9E0         ; worker
                    //   67C2B0  84 C0 / 74 0F   test al,al / je 0x67C2C3
                    //   67C2B7  89 46 34        mov [gen+0x34],eax    ; dwStartTick = now
                    //   67C2BD  FF 40 48        inc dword [self+0x48] ; cursor++
                    //   67C2C0  4F / 75 94      dec edi / jne 0x67C257
                    //
                    // Three things the old one-generator-per-pass shape got wrong and
                    // this restores: the cursor advances once per generator examined
                    // (it used to advance twice on a successful regen and once
                    // otherwise); a worker returning false leaves the scan WITHOUT
                    // advancing the cursor and WITHOUT refreshing dwStartTick, so the
                    // same generator is retried immediately next pass; and running off
                    // the end resets the cursor and ends the pass rather than wrapping
                    // into a second lap.
                    //
                    // [ebp-0x14] is one GetTickCount from 0x67C198 reused at 0x67C28E
                    // and 0x67C2B4, so the scan reads a single "now".
                    for (var nGenScan = 0; nGenScan < NativeMonGenScanPerTick; nGenScan++)
                    {
                        if (m_MonGenList.Count - 1 < m_nCurrMonGen)
                        {
                            m_nCurrMonGen = 0;
                            break;
                        }
                        MonGen = m_MonGenList[m_nCurrMonGen];
                        // 0x67C288 `cmp dword [esi+0x1C],0` is the resolved template
                        // record, not the range: sub_679F8C dispatches on
                        // `byte[edi+0x14]` through the race jump table at 0x67A115
                        // (0x67A008-0x67A01F) with edi = ecx = [gen+0x1C], while the
                        // coordinate jitter uses [gen+0x20] instead.  nRace is what
                        // Initialize() resolves that pointer into.
                        if (MonGen != null
                            && MonGen.nCount > MonGen.nActiveCount
                            && !string.IsNullOrEmpty(MonGen.sMonName)
                            && MonGen.nRace > 0
                            && !M2Share.g_Config.boVentureServer
                            && (MonGen.dwStartTick == 0
                                || dwCurrentTick - MonGen.dwStartTick > MonGen.dwZenTime))
                        {
                            // 战神 resolves the map once at load time into [gen+0x00];
                            // C# looks it up by name, so the null test is a C#
                            // necessity rather than a native gate and sits after the
                            // four native gates.
                            boCanCreate =
                                M2Share.MapManager.FindMap(MonGen.sMapName) != null;
                            if (boCanCreate)
                            {
                                if (!RegenMonsters(MonGen,
                                        MonGen.nCount - MonGen.nActiveCount))
                                {
                                    break;
                                }
                                MonGen.dwStartTick = dwCurrentTick;
                            }
                        }
                        m_nCurrMonGen++;
                    }
                }
                
                var dwMonProcTick = HUtil32.GetTickCount();

                nMonsterProcessCount = 0;
                var i = 0;
                for (i = m_nMonGenListPosition; i < m_MonGenList.Count; i++)
                {
                    MonGen = m_MonGenList[i];
                    int nProcessPosition;
                    if (m_nMonGenCertListPosition < MonGen.CertList.Count)
                        nProcessPosition = m_nMonGenCertListPosition;
                    else
                        nProcessPosition = 0;
                    m_nMonGenCertListPosition = 0;
                    while (true)
                    {
                        if (nProcessPosition >= MonGen.CertList.Count)
                        {
                            break;
                        }
                        Monster = (AnimalObject)MonGen.CertList[nProcessPosition];
                        if (Monster != null)
                        {
                            if (!Monster.m_boGhost)
                            {
                                if ((dwCurrentTick - Monster.m_dwRunTick) > Monster.m_nRunTime)
                                {
                                    Monster.m_dwRunTick = dwRunTick;
                                    // 已按战神二进制删除"原地复活"分支（2026-08-03，Tier-1 闭环证据）：
                                    // 战神逐怪 tick 循环 = TUserEngine.ProcessMon `sub_67C150`
                                    // (0x0067C150..0x0067C805；身份由自带异常串 "[Exception]: ProcessMon -1/-2/-5"
                                    // @0x67C218/0x67C2DB/0x67C769 坐实；虚派发 vtable 槽 0x6794FC，故不在
                                    // TAppEngine.Execute 的直接被调者里——前几轮因此找不到，且它叫 ProcessMon 而非
                                    // ProcessMonsters)。其 CertList 遍历(0x67C354，Phase4 第二表逐字节相同)
                                    // 每只怪【只有三个出口】：null 跳过 / m_boGhost[+0x73] 则移出表+Free(vtable+0x7C) /
                                    // 存活则 (now-m_dwRunTick[+0x340])>m_nRunTime[+0x33C] 时置 runtick 并 Run(vtable+0x88)。
                                    // 【无】m_boDeath 测试、【无】m_boCanReAlive、【无】m_pMonGen[+0x128] 读取、
                                    // 【无】ReAliveEx。死怪变 ghost→移除释放，补怪由 make-fresh 工厂在刷怪点生成。
                                    // 佐证：3 个兄弟 tick 循环(sub_605790/sub_64A134/sub_651E1C)分支集相同且均无复活；
                                    // C# ReAliveEx 的函数体(肉质 Random(3500)+3000 + 种族 switch)在战神仅存在于
                                    // make-fresh 工厂 sub_679F8C，其 4 个调用者全是创建怪物路径。
                                    // 证据：staging/adjudicate_3_disputed_20260802.md TARGET-C-FINAL +
                                    //       staging/ida_montickloop{,2,3,4}_20260803.txt。
                                    if (!Monster.m_boIsVisibleActive && (Monster.m_nProcessRunCount < M2Share.g_Config.nProcessMonsterInterval))
                                    {
                                        Monster.m_nProcessRunCount++;
                                    }
                                    else
                                    {
                                        if ((dwCurrentTick - Monster.m_dwSearchTick) > Monster.m_dwSearchTime)
                                        {
                                            Monster.m_dwSearchTick = HUtil32.GetTickCount();
                                            // 战神 ProcessMon 无死亡分支（搜索折叠在 Run 内），故无条件走 SearchViewRange。
                                            Monster.SearchViewRange();
                                        }
                                        Monster.m_nProcessRunCount = 0;
                                        Monster.Run();
                                    }
                                }
                                nMonsterProcessPostion++;
                            }
                            else
                            {
                                if ((HUtil32.GetTickCount() - Monster.m_dwGhostTick) > 5 * 60 * 1000)
                                {
                                    // 战神 ProcessMon sub_67C150 循环2 的 ghost 分支（0x67C46F..0x67C49F）
                                    // 一共做三件事：
                                    //   (1) NULL 掉 CertList 槽 + 桶计数减一（0x67C491 / 0x67C497；
                                    //       战神不压实，故 C# 的 RemoveAt 语义上等价）；
                                    //   (2) sub_67D8F0 把对象【入队到全局延迟释放 FIFO】
                                    //       （0x67C47E；FIFO 在 MonsterMgr+0x20/+0x24/+0x28，节点 12 字节
                                    //       {obj, enqueueTick, next}），该 FIFO 由同函数循环1 在
                                    //       0x67C1BD `cmp eax,0x493E0`（= 300000ms = 5 分钟）后排空，
                                    //       调 sub_404690 = TObject.Free 真正销毁对象；
                                    //   (3) vtable+0x7C 的"离开世界"钩子（0x67C49F）。
                                    // C# 之前只搬了 (1)，(2)(3) 丢了，全靠非原生的
                                    // ObjectManager.ClearObject 全局扫 ghost（3 分钟 dwMakeGhostTime）兜底。
                                    // 战神【没有】任何 id→对象的全局 ghost 扫描；补齐这里之后
                                    // ClearObject 才可以删。
                                    // 必须走 Remove(id, expected) 这个身份校验重载：它内部执行
                                    // PasEngine.CancelDeferredCallsForObject + ClearMonsterScriptState，
                                    // 即 vtable+0x7C 的对应物；没有别的逐类型循环做这件事。
                                    // 本分支的 5*60*1000 就是战神的 0x493E0，逐字节一致。
                                    // 覆盖范围：MonGen 怪物 + 守卫 + 宝宝/召唤（m_SlaveList 先在
                                    // TBaseObject.Base.cs:460 摘链，随后落到本循环）。
                                    var ghostMonster = Monster;
                                    MonGen.CertList.RemoveAt(nProcessPosition);
                                    MonGen.CertCount--;
                                    M2Share.ObjectManager.Remove(ghostMonster.ObjectId, ghostMonster);
                                    Monster = null;
                                    continue;
                                }
                            }
                        }
                        nProcessPosition++;
                        if ((HUtil32.GetTickCount() - dwMonProcTick) > M2Share.g_dwMonLimit)
                        {
                            boProcessLimit = true;
                            m_nMonGenCertListPosition = nProcessPosition;
                            break;
                        }
                    }
                    if (boProcessLimit) break;
                }
                if (m_MonGenList.Count <= i)
                {
                    m_nMonGenListPosition = 0;
                    nMonsterCount = nMonsterProcessPostion;
                    nMonsterProcessPostion = 0;
                }
                if (!boProcessLimit)
                    m_nMonGenListPosition = 0;
                else
                    m_nMonGenListPosition = i;

                ProcessNativeMagicTowerRuntimeMonsters();
                ProcessNativeMagicTowerDeferredSpawns();
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(e.StackTrace);
            }
        }

        
        
        
        
        
        private int GetGenMonCount(MonGenInfo MonGen)
        {
            var nCount = 0;
            TBaseObject BaseObject;
            for (var i = 0; i < MonGen.CertList.Count; i++)
            {
                BaseObject = MonGen.CertList[i];
                if (!BaseObject.m_boDeath && !BaseObject.m_boGhost)
                {
                    nCount++;
                }
            }
            return nCount;
        }

        private void ProcessNpcs()
        {
            NormNpc NPC;
            var dwRunTick = HUtil32.GetTickCount();
            var boProcessLimit = false;
            try
            {
                var dwCurrTick = HUtil32.GetTickCount();
                for (var i = nNpcPosition; i < QuestNPCList.Count; i++)
                {
                    NPC = QuestNPCList[i];
                    if (!NPC.m_boGhost)
                    {
                        if ((dwCurrTick - NPC.m_dwRunTick) > NPC.m_nRunTime)
                        {
                            if ((HUtil32.GetTickCount() - NPC.m_dwSearchTick) > NPC.m_dwSearchTime)
                            {
                                NPC.m_dwSearchTick = HUtil32.GetTickCount();
                                NPC.SearchViewRange();
                            }
                            if ((dwCurrTick - NPC.m_dwRunTick) > NPC.m_nRunTime)
                            {
                                NPC.m_dwRunTick = dwCurrTick;
                                NPC.Run();
                            }
                        }
                    }
                    else
                    {
                        if ((HUtil32.GetTickCount() - NPC.m_dwGhostTick) > 60 * 1000)
                        {
                            // 同 ProcessMonsters 的说明：战神 ProcessMon 循环3
                            // （0x67C614..0x67C63B，即"不属于任何 MonGen 的扁平表"，对应 C# 的
                            // QuestNPCList / m_MerchantList）也是 NULL 槽 + sub_67D8F0 入队 5 分钟
                            // 延迟释放 FIFO + vtable+0x7C 钩子。C# 原本只摘 QuestNPCList，
                            // 全局注册表 _actors 里的条目仅由非原生的 ClearObject 回收。
                            // 走身份校验重载以保住 PasEngine 脚本状态清理（vtable+0x7C 的对应物）。
                            // 60s 门是既有值（战神此处实为 5 分钟 FIFO，属既存的次要偏差，
                            // 本次不改动，仅记录）。
                            TryRemoveQuestNpcExact(NPC);
                            M2Share.ObjectManager.Remove(NPC.ObjectId, NPC);
                            break;
                        }
                    }
                    if ((HUtil32.GetTickCount() - dwRunTick) > M2Share.g_dwNpcLimit)
                    {
                        nNpcPosition = i;
                        boProcessLimit = true;
                        break;
                    }
                }
                if (!boProcessLimit) nNpcPosition = 0;
            }
            catch
            {
                M2Share.MainOutMessage("[Exceptioin] TUserEngine.ProcessNpcs");
            }
            dwProcessNpcTimeMin = HUtil32.GetTickCount() - dwRunTick;
            if (dwProcessNpcTimeMin > dwProcessNpcTimeMax) dwProcessNpcTimeMax = dwProcessNpcTimeMin;
        }

        public TBaseObject RegenMonsterByName(string sMap, short nX, short nY, string sMonName)
        {
            return RegenMonsterByName(M2Share.MapManager.FindMap(sMap), nX, nY,
                sMonName);
        }

        public TBaseObject RegenMonsterByName(Envirnoment environment, short nX,
            short nY, string sMonName)
        {
            var nRace = GetMonRace(sMonName);
            var baseObject = AddBaseObject(environment, nX, nY, nRace, sMonName,
                false);
            if (baseObject == null) return null;
            var mapPublication = CaptureMapPublication(baseObject);

            MonGenInfo certificateOwner = null;
            var certificateCountBefore = 0;
            var certificatePublishAttempted = false;
            var committed = false;
            try
            {
                var n18 = m_MonGenList.Count - 1;
                if (n18 < 0) n18 = 0;
                if (m_MonGenList.Count > n18)
                {
                    certificateOwner = m_MonGenList[n18];
                    certificateCountBefore = certificateOwner.CertCount;
                    certificatePublishAttempted = true;
                    certificateOwner.CertList.Add(baseObject);
                }

                if (certificateOwner != null)
                {
                    certificateOwner.CertCount = certificateCountBefore + 1;
                }
                committed = true;
            }
            catch (Exception e)
            {
                if (certificateOwner != null)
                {
                    if (certificatePublishAttempted)
                    {
                        try
                        {
                            certificateOwner.CertList?.Remove(baseObject);
                        }
                        catch
                        {
                            // Continue releasing the actor even if a custom list failed.
                        }
                    }
                    certificateOwner.CertCount = certificateCountBefore;
                }
                RollbackUnpublishedMonster(baseObject, mapPublication);
                LogMonsterSpawnFailure("certificate publication", e);
                return null;
            }

            // Script initialization is post-commit. PAS failures do not invalidate
            // an otherwise live monster under the existing host contract.
            if (committed)
            {
                try
                {
                    M2Share.PasEngine?.TryInitializeMonsterScript(baseObject);
                }
                catch (Exception e)
                {
                    LogMonsterSpawnFailure("OnInitialize", e);
                }
            }
            return baseObject;
        }

        internal TBaseObject RegenNativeMagicTowerArcher(
            Envirnoment environment, short x, short y)
        {
            if (GetMonRace(TPlayObject.NativeMagicTowerArcherName) !=
                TPlayObject.NativeMagicTowerArcherRace)
                return null;

            var archer = AddBaseObject(environment, x, y,
                TPlayObject.NativeMagicTowerArcherRace,
                TPlayObject.NativeMagicTowerArcherName, false, true);
            if (archer == null) return null;
            if (!RegisterNativeMagicTowerRuntimeMonster(archer))
            {
                RollbackUnpublishedMonster(archer);
                return null;
            }

            try
            {
                M2Share.PasEngine?.TryInitializeMonsterScript(archer);
            }
            catch (Exception e)
            {
                LogMonsterSpawnFailure("OnInitialize", e);
            }
            return archer;
        }

        internal TBaseObject RegenNativeMagicTowerChallengeMonster(
            Envirnoment environment, string monsterName, short x, short y)
        {
            var race = GetMonRace(monsterName);
            if (race < 0) return null;

            var monster = AddBaseObject(environment, x, y, race,
                monsterName, false, true);
            if (monster == null) return null;
            if (!RegisterNativeMagicTowerRuntimeMonster(monster))
            {
                RollbackUnpublishedMonster(monster);
                return null;
            }

            try
            {
                M2Share.PasEngine?.TryInitializeMonsterScript(monster);
            }
            catch (Exception e)
            {
                LogMonsterSpawnFailure("OnInitialize", e);
            }
            return monster;
        }

        internal TBaseObject RegenNativeMagicTowerWarMonster(
            Envirnoment environment, string monsterName, short x, short y)
        {
            if (environment == null) return null;

            var race = GetMonRace(monsterName);
            if (race < 0) return null;

            var monster = CreateNativeMagicTowerWarMonster(environment,
                monsterName, race, x, y);
            if (monster != null) return monster;

            _nativeMagicTowerDeferredSpawns.Enqueue(
                new NativeMagicTowerDeferredSpawn(environment, monsterName,
                    race, x, y));
            return null;
        }

        internal int NativeMagicTowerDeferredSpawnCount =>
            _nativeMagicTowerDeferredSpawns.Count;

        internal int NativeMagicTowerRuntimeMonsterCount =>
            _nativeMagicTowerRuntimeMonsterCount;

        internal int NativeMagicTowerRuntimeSlotCount =>
            _nativeMagicTowerRuntimeMonsters.Count;

        private bool RegisterNativeMagicTowerRuntimeMonster(
            TBaseObject monster)
        {
            if (monster == null || monster.m_boGhost ||
                !ReferenceEquals(M2Share.ObjectManager?.Get(monster.ObjectId),
                    monster))
                return false;
            try
            {
                var emptySlot = _nativeMagicTowerRuntimeMonsters.IndexOf(null);
                if (emptySlot >= 0)
                    _nativeMagicTowerRuntimeMonsters[emptySlot] = monster;
                else
                    _nativeMagicTowerRuntimeMonsters.Add(monster);
                _nativeMagicTowerRuntimeMonsterCount++;
                return true;
            }
            catch (Exception e)
            {
                LogMonsterSpawnFailure("runtime publication", e);
                return false;
            }
        }

        internal void ProcessNativeMagicTowerRuntimeMonsters()
        {
            var startTick = HUtil32.GetTickCount();
            var processedSlots = 0;
            while (_nativeMagicTowerRuntimeCursor <
                   _nativeMagicTowerRuntimeMonsters.Count)
            {
                var slot = _nativeMagicTowerRuntimeCursor++;
                var monster = _nativeMagicTowerRuntimeMonsters[slot];
                if (monster != null)
                {
                    var published = ReferenceEquals(
                        M2Share.ObjectManager?.Get(monster.ObjectId), monster);
                    if (monster.m_boGhost || !published)
                    {
                        if (monster.m_boGhost && published)
                            M2Share.ObjectManager.Remove(monster.ObjectId,
                                monster);
                        _nativeMagicTowerRuntimeMonsters[slot] = null;
                        if (_nativeMagicTowerRuntimeMonsterCount > 0)
                            _nativeMagicTowerRuntimeMonsterCount--;
                    }
                    else
                    {
                        var currentTick = HUtil32.GetTickCount();
                        if ((currentTick - monster.m_dwRunTick) >
                            monster.m_nRunTime)
                        {
                            monster.m_dwRunTick = currentTick;
                            monster.Run();
                        }
                    }
                }

                processedSlots++;
                if (processedSlots %
                        NativeMagicTowerRuntimeBudgetCheckInterval == 0 &&
                    HUtil32.GetTickCount() - startTick >
                    NativeMagicTowerRuntimeTimeBudget)
                    break;
            }
            if (_nativeMagicTowerRuntimeCursor >=
                _nativeMagicTowerRuntimeMonsters.Count)
                _nativeMagicTowerRuntimeCursor = 0;
        }

        private void ClearNativeMagicTowerRuntimeMonsters()
        {
            for (var i = _nativeMagicTowerRuntimeMonsters.Count - 1;
                 i >= 0; i--)
            {
                var monster = _nativeMagicTowerRuntimeMonsters[i];
                if (monster != null && ReferenceEquals(
                        M2Share.ObjectManager?.Get(monster.ObjectId), monster))
                    RollbackUnpublishedMonster(monster);
            }
            _nativeMagicTowerRuntimeMonsters.Clear();
            _nativeMagicTowerRuntimeCursor = 0;
            _nativeMagicTowerRuntimeMonsterCount = 0;
        }

        internal void ProcessNativeMagicTowerDeferredSpawns()
        {
            for (var attempt = 0;
                 attempt < NativeMagicTowerDeferredSpawnBudget &&
                 _nativeMagicTowerDeferredSpawns.Count > 0;
                 attempt++)
            {
                var pending = _nativeMagicTowerDeferredSpawns.Peek();
                if (!IsCurrentDynamicRoomGeneration(pending))
                {
                    _nativeMagicTowerDeferredSpawns.Dequeue();
                    continue;
                }

                var monster = CreateNativeMagicTowerWarMonster(
                    pending.Environment, pending.MonsterName, pending.Race,
                    pending.X, pending.Y);
                if (monster != null)
                {
                    pending.Remaining--;
                }
                else if (pending.RetryCounter > 0)
                {
                    pending.RetryCounter--;
                }
                else
                {
                    pending.RetryCounter = 5;
                    pending.Remaining--;
                }

                if (pending.Remaining <= 0)
                    _nativeMagicTowerDeferredSpawns.Dequeue();
            }
        }

        private static bool IsCurrentDynamicRoomGeneration(
            NativeMagicTowerDeferredSpawn pending)
        {
            if (!pending.IsDynamicRoom) return true;

            var environment = pending.Environment;
            return environment.IsDynamicRoom &&
                   environment.DynamicRoomState == 2 &&
                   environment.DynamicRoomPhysicalInstanceId ==
                   pending.DynamicRoomPhysicalInstanceId &&
                   environment.DynamicRoomIndex == pending.DynamicRoomIndex;
        }

        private TBaseObject CreateNativeMagicTowerWarMonster(
            Envirnoment environment, string monsterName, int race, short x,
            short y)
        {
            var monster = AddBaseObject(environment, x, y, race, monsterName,
                false, true);
            if (monster == null) return null;
            if (!RegisterNativeMagicTowerRuntimeMonster(monster))
            {
                RollbackUnpublishedMonster(monster);
                return null;
            }

            try
            {
                M2Share.PasEngine?.TryInitializeMonsterScript(monster);
            }
            catch (Exception e)
            {
                LogMonsterSpawnFailure("OnInitialize", e);
            }
            return monster;
        }

        public void Run()
        {
            const string sExceptionMsg = "[Exception] TUserEngine::Run";
            try
            {
                if ((HUtil32.GetTickCount() - dwShowOnlineTick) > M2Share.g_Config.dwConsoleShowUserCountTime)
                {
                    dwShowOnlineTick = HUtil32.GetTickCount();
                    M2Share.NoticeManager.LoadingNotice();
                    M2Share.MainOutMessage("在线�?: " + PlayObjectCount);
                    if (!M2Share.CastleManager.Save())
                    {
                        M2Share.ErrorMessage("在线统计周期的城堡配置保存未完成。");
                    }
                }
                if ((HUtil32.GetTickCount() - dwSendOnlineHumTime) > 10000)
                {
                    dwSendOnlineHumTime = HUtil32.GetTickCount();
                    IdSrvClient.Instance.SendOnlineHumCountMsg(OnlinePlayObject);
                }
                // Stall (摆摊) 3-minute maintenance tick (sub_61BFB8). Owns its own 180000 ms interval gate
                // and no-ops when the stall subsystem is dormant, so calling it every pass is cheap.
                NativeStallExpiryTick.Run();
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
        }

        public GoodItem GetStdItem(int nItemIdx)
        {
            GoodItem result = null;
            var items = StdItemList;
            var listIndex = HasNativeStdItemSentinel(items)
                ? nItemIdx : nItemIdx - 1;
            if (listIndex >= 0 && items.Count > listIndex)
            {
                result = items[listIndex];
                if (result == null || result.Name == "") result = null;
            }
            return result;
        }

        public GoodItem GetStdItem(string sItemName)
        {
            GoodItem result = null;
            GoodItem StdItem = null;
            if (string.IsNullOrEmpty(sItemName)) return result;
            var items = StdItemList;
            for (var i = 0; i < items.Count; i++)
            {
                StdItem = items[i];
                if (StdItem == null) continue;
                if (string.Compare(StdItem.Name, sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    result = StdItem;
                    break;
                }
            }
            return result;
        }

        public int GetStdItemWeight(int nItemIdx)
        {
            int result = 0;
            var items = StdItemList;
            var listIndex = HasNativeStdItemSentinel(items)
                ? nItemIdx : nItemIdx - 1;
            if (listIndex >= 0 && items.Count > listIndex
                && items[listIndex] != null)
            {
                result = items[listIndex].Weight;
            }
            return result;
        }

        public string GetStdItemName(int nItemIdx)
        {
            var result = "";
            var items = StdItemList;
            var listIndex = HasNativeStdItemSentinel(items)
                ? nItemIdx : nItemIdx - 1;
            if (listIndex >= 0 && items.Count > listIndex
                && items[listIndex] != null)
            {
                result = items[listIndex].Name;
            }
            return result;
        }

        // ====================================================================
        // Item-use mode-1 refill table (powerupItem.ini) — native UserEngine+0x2A0
        // ====================================================================
        // The C# analogue of the TUserEngine TList at UserEngine+0x2A0: a {StdItem wIndex -> refill} map.
        // Native populator sub_74DEDC loads <ServerBase>\config\powerupItem.ini (each non-';' line
        // "<ItemName> = <RefillValue>"; ItemName -> StdItem index via sub_74C1E0; value = StrToIntDef(RHS,1);
        // TList.Add {wIndex, value}); it is wired at the engine bulk config-load (sub_74C12C from init
        // sub_792838) and re-invoked by a @Reload GM case (sub_622820). Item-use mode-1 (hero DragonHeart
        // amulet refill, sub_6866BC -> sub_763840) reads refill = sub_74E0A0(g_UserEngine, consumed.wIndex).
        // A missing file leaves the map empty (native sub_40CF2C FileExists gate) => mode-1 fails closed.
        private Dictionary<int, int> m_NativePowerupItems = new Dictionary<int, int>();

        /// <summary>
        /// Native sub_74E0A0: the powerupItem refill amount keyed by a consumed item's wIndex; 0 when the
        /// key is absent (native "no entry -> 0 -> no refill, no consume").
        /// </summary>
        public int GetNativePowerupItemRefill(int wIndex)
        {
            var table = Volatile.Read(ref m_NativePowerupItems);
            return table != null && table.TryGetValue(wIndex, out var refill) ? refill : 0;
        }

        /// <summary>
        /// Native sub_74DEDC: (re)load Share/config/powerupItem.ini into the mode-1 refill table using the
        /// same base-dir mechanism as the sibling config loaders (sRootPath\sBaseDir\config\...). Called at
        /// startup once StdItemList is published, and from the GM item-DB reload. A missing/locked file
        /// yields an empty table (byte-identical to today's fail-closed mode-1) and never throws.
        /// </summary>
        public void LoadNativePowerupItems()
        {
            var path = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir, "config", "powerupItem.ini"));
            LoadNativePowerupItems(path);
        }

        internal void LoadNativePowerupItems(string path)
        {
            var table = new Dictionary<int, int>();
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))   // native sub_40CF2C FileExists gate
                {
                    foreach (var rawLine in File.ReadAllLines(path, HUtil32.GbkEncoding))
                    {
                        if (rawLine == null) continue;
                        var line = rawLine.Trim();
                        if (line.Length == 0 || line[0] == ';') continue;   // skip blank + ';' comment lines
                        var separator = line.IndexOf('=');                  // native split {' ','='}: name before '='
                        if (separator < 0) continue;
                        var itemName = line.Substring(0, separator).Trim();
                        if (itemName.Length == 0) continue;
                        var wIndex = GetStdItemIdx(itemName);               // native sub_74C1E0 name -> StdItem index
                        if (wIndex < 0 || table.ContainsKey(wIndex)) continue;  // -1 => skip; first occurrence wins (TList order)
                        var rhs = line.Substring(separator + 1).Trim();
                        table[wIndex] = int.TryParse(rhs, out var value) ? value : 1;   // native StrToIntDef(RHS, 1)
                    }
                }
            }
            catch (IOException)
            {
                // absent/locked file -> keep the freshly-built empty table -> faithful fail-closed (never throw)
            }
            catch (UnauthorizedAccessException)
            {
            }
            Volatile.Write(ref m_NativePowerupItems, table);
        }

        public bool FindOtherServerUser(string sName, ref int nServerIndex)
        {
            if (m_OtherUserNameList.TryGetValue(sName, out var groupServer))
            {
                nServerIndex = groupServer.nServerIdx;
                Console.WriteLine($"玩家在[{nServerIndex}]服务器上.");
                return true;
            }
            return false;
        }

        public void CryCry(short wIdent, Envirnoment pMap, int nX, int nY, int nWide, byte btFColor, byte btBColor, string sMsg)
        {
            TPlayObject PlayObject;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                // 0x652CB2 3B 45 10 cmp eax,[ebp+0x10] / 0x652CB5 7F 32 jg skip: include abs==nWide
                if (!PlayObject.m_boGhost && PlayObject.m_PEnvir == pMap && PlayObject.m_boBanShout &&
                    Math.Abs(PlayObject.m_nCurrX - nX) <= nWide && Math.Abs(PlayObject.m_nCurrY - nY) <= nWide)
                    PlayObject.SendMsg(null, wIdent, 0, btFColor, btBColor, 0, sMsg);
            }
        }

        
        
        
        
        
        public void MonGetRandomItems(TBaseObject mon, TBaseObject killer = null)
        {
            IList<TMonItem> ItemList = TryGetMonsterInfo(mon.m_sCharName,
                out var monster) ? monster.ItemList : null;
            if (ItemList != null)
            {
                // native obj+0x1828: anti-addiction fatigue tier 2 = half-drop probability
                int penalty = (killer as TPlayObject)?.m_btNativeFatigueTier == 2 ? 2 : 1;
                for (var i = 0; i < ItemList.Count; i++)
                {
                    var MonItem = ItemList[i];
                    if (M2Share.RandomNumber.Random(MonItem.MaxPoint * penalty) <= MonItem.SelPoint)
                    {
                        if (string.Compare(MonItem.ItemName, Grobal2.sSTRING_GOLDNAME, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            mon.m_nGold = mon.m_nGold + MonItem.Count / 2 + M2Share.RandomNumber.Random(MonItem.Count);
                        }
                        else
                        {
                            TUserItem UserItem = null;
                            if (CopyToUserItemFromName(MonItem.ItemName, ref UserItem))
                            {
                                // 战神's only per-item initialisation hook on the drop
                                // path is the freshly-built object's virtual +0x28,
                                // invoked once with edx=0 at 0x71FDA2 (this loop) and
                                // 0x71FBD5 (the exclusive chain):
                                //   71FD9B  33 D2              xor edx,edx
                                //   71FD9D  8B 45 D8           mov eax,[ebp-0x28]
                                //   71FDA0  8B 08              mov ecx,[eax]
                                //   71FDA2  FF 51 28           call dword [ecx+0x28]
                                // For the base item class that slot is sub_783EFC, and
                                // sub_783EFC is exactly this line:
                                //   783F05  B8 50 00 00 00     mov eax,0x50      ; 80
                                //   783F0A  E8 3D FC C7 FF     call 0x403B4C     ; Random
                                //   783F0F  83 C0 14           add eax,0x14      ; +20
                                //   783F18  0F B7 43 28        movzx eax,word [ebx+0x28] ; DuraMax
                                //   783F22  D8 35 38 3F 78 00  fdiv dword [0x783F38]     ; 100.0f
                                //   783F28  DE C9              fmulp st(1)
                                //   783F2A  E8 45 F6 C7 FF     call 0x403574     ; @ROUND
                                //   783F2F  66 89 43 26        mov word [ebx+0x26],ax    ; Dura
                                // sub_783EFC sits in VMT slot +0x28 of 116 item classes;
                                // every one of the 116 dword references passes the Delphi
                                // self-pointer test dword[VMT-0x4C]==VMT and the
                                // slot-offset histogram over all of them is {0x28: 116}.
                                // 0x71FD9B xor edx,edx / 0x71FDA2 call [ecx+0x28].
                                // Base +0x28 is 783EFC (Random(80) dura). Equipment
                                // overrides add Random(10)/Random(9) and, on 0, the
                                // extra-attr body. Pile +0x28 is 7882B4 C3 (no draw).
                                NativeItemPlus28.ApplyOnDrop(UserItem, GetStdItem(UserItem.wIndex));
                                mon.m_ItemList.Add(UserItem);
                            }
                        }
                    }
                }
            }
        }

        public bool CopyToUserItemFromName(string sItemName, ref TUserItem Item)
        {
            return CopyToUserItemFromName(sItemName, ref Item, 0);
        }

        public bool CopyToUserItemFromName(string sItemName, ref TUserItem Item,
            int makeIndex)
        {
            if (string.IsNullOrEmpty(sItemName)) return false;
            var items = StdItemList;
            var nativeIndices = HasNativeStdItemSentinel(items);
            for (var i = nativeIndices ? 1 : 0; i < items.Count; i++)
            {
                var StdItem = items[i];
                if (StdItem == null) continue;
                if (!StdItem.Name.Equals(sItemName, StringComparison.OrdinalIgnoreCase)) continue;
                if (Item == null) Item = new TUserItem();
                Item.wIndex = unchecked((ushort)(nativeIndices ? i : i + 1));
                Item.MakeIndex = makeIndex == 0
                    ? M2Share.GetItemNumber()
                    : makeIndex;
                Item.DuraMax = StdItem.DuraMax;
                // Native constructs a pile item through a subclass whose body runs
                // the root constructor and then overwrites Dura unconditionally:
                //   0x78810D  E8 76 B6 FF FF     call 0x783788          ; root ctor,
                //                                                       ; sets Dura = DuraMax
                //   0x788112  66 C7 46 26 01 00  mov word [esi+0x26], 1 ; Dura = 1
                //   0x788118  C6 46 14 07        mov byte [esi+0x14], 7 ; mark as pile
                // For a pile item Dura IS the stack count, so seeding it from DuraMax
                // hands out DuraMax units where native hands out one - a duplication
                // path shared by crafting, NPC grants, drops and script gives, since
                // they all construct through here.
                Item.Dura = NativeItemFactory.IsPileItem(StdItem)
                    ? (ushort)1
                    : StdItem.DuraMax;
                return true;
            }
            return false;
        }

        public void ProcessUserMessage(TPlayObject PlayObject, ClientPacket DefMsg, string Buff,
            byte[] payload = null)
        {
            var sMsg = string.Empty;
            if (PlayObject.m_boOffLineFlag) return;
            if (!string.IsNullOrEmpty(Buff)) sMsg = Buff;
            switch (DefMsg.Ident)
            {
                case Grobal2.CM_SPELL:
                    // Native handler 0x6DA04A hands four separate header fields to the
                    // spell worker 0x6BC510:
                    //   0x6DA0C1  0F B7 40 06  movzx eax, word [eax+6]   ; Param  -> pushed 1st
                    //   0x6DA0C9  0F B7 40 08  movzx eax, word [eax+8]   ; Tag    -> pushed 2nd
                    //   0x6DA0D1  8B 08        mov   ecx, [eax]          ; Recog  -> ECX
                    //   0x6DA0D6  66 8B 50 0A  mov   dx,  word [eax+0xA] ; Series -> EDX
                    // and each role is pinned inside 0x6BC510: EDX reaches the
                    // "skill known / area allowed" gate at 0x6BC541-0x6BC546, so Series is
                    // the skill index; Param and Tag reach GetNextDirection at 0x6BC637
                    // (ECX) / 0x6BC633 (stack) alongside CurrX 0x12C and CurrY 0x130 at
                    // 0x6BC640 / 0x6BC63A, so they are target X and Y; ECX is compared
                    // against the map cells' object pointers at 0x76CA42 and then
                    // dereferenced for target.CurrX at 0x6BC66B, so Recog is the target.
                    if (M2Share.g_Config.boSpellSendUpdateMsg)
                    {
                        PlayObject.SendUpdateMsg(PlayObject, DefMsg.Ident, DefMsg.Series,
                            DefMsg.Param, DefMsg.Tag, DefMsg.Recog, "");
                    }
                    else
                    {
                        PlayObject.SendMsg(PlayObject, DefMsg.Ident, DefMsg.Series,
                            DefMsg.Param, DefMsg.Tag, DefMsg.Recog, "");
                    }
                    break;
                case Grobal2.CM_QUERYUSERNAME:
                    PlayObject.SendMsg(PlayObject, DefMsg.Ident, 0, DefMsg.Recog, DefMsg.Param, DefMsg.Tag, "");
                    break;
                case Grobal2.CM_DROPITEM:
                case Grobal2.CM_TAKEONITEM:
                case Grobal2.CM_TAKEOFFITEM:
                case Grobal2.CM_MERCHANTDLGSELECT:
                case Grobal2.CM_MERCHANT_QUERY:
                case Grobal2.CM_PILEUPITEM:
                case Grobal2.CM_SPLITITEM:
                case Grobal2.CM_MERCHANTQUERYSELLPRICE:
                case Grobal2.CM_USERSELLITEM:
                case Grobal2.CM_USERBUYITEM:
                case Grobal2.CM_USERGETDETAILITEM:
                case Grobal2.CM_CREATEGROUP:
                case Grobal2.CM_ADDGROUPMEMBER:
                case Grobal2.CM_DELGROUPMEMBER:
                case Grobal2.CM_USERREPAIRITEM:
                case Grobal2.CM_MERCHANTQUERYREPAIRCOST:
                case Grobal2.CM_DEALTRY:
                case Grobal2.CM_DEALADDITEM:
                case Grobal2.CM_DEALDELITEM:
                case Grobal2.CM_USERSTORAGEITEM:
                case Grobal2.CM_USERTAKEBACKSTORAGEITEM:
                case Grobal2.CM_USERMAKEDRUGITEM:
                        PlayObject.SendMsg(PlayObject, DefMsg.Ident, DefMsg.Series, DefMsg.Recog, DefMsg.Param, DefMsg.Tag,
                            sMsg, payload);
                    break;
                case Grobal2.CM_PASSWORD:
                case Grobal2.CM_CHGPASSWORD:
                case Grobal2.CM_SETPASSWORD:
                    PlayObject.SendMsg(PlayObject, DefMsg.Ident, DefMsg.Param, DefMsg.Recog, DefMsg.Series, DefMsg.Tag,
                        sMsg, payload);
                    break;
                case Grobal2.CM_ADJUST_BONUS:
                    PlayObject.SendMsg(PlayObject, DefMsg.Ident, DefMsg.Series, DefMsg.Recog, DefMsg.Param, DefMsg.Tag,
                        sMsg, payload);
                    break;
                case Grobal2.CM_HORSERUN:
                case Grobal2.CM_TURN:
                case Grobal2.CM_WALK:
                case Grobal2.CM_SITDOWN:
                case Grobal2.CM_RUN:
                case Grobal2.CM_HIT:
                case Grobal2.CM_HEAVYHIT:
                case Grobal2.CM_BIGHIT:
                case Grobal2.CM_POWERHIT:
                case Grobal2.CM_LONGHIT:
                case Grobal2.CM_CRSHIT:
                case Grobal2.CM_TWINHIT:
                case Grobal2.CM_WIDEHIT:
                case Grobal2.CM_FIREHIT:
                case Grobal2.CM_3037:
                    // The shared native action handler at 0x6D9EAF takes X, Y and the
                    // direction from three separate header fields:
                    //   0x6D9EE9  0F B7 40 06  movzx eax, word [eax+6]   ; Param  -> Y
                    //   0x6D9EF1  8A 40 0A     mov   al,  byte [eax+0xA] ; Series
                    //   0x6D9EF4  24 07        and   al,  7              ; direction
                    //   0x6D9EFA  8B 08        mov   ecx, [eax]          ; Recog  -> X
                    // and its callee 0x6EC078 proves which is which by comparing them
                    // against the actor's own coordinates:
                    //   0x6EC0C3  cmp esi, [ebx+0x12C]   ; Recog vs CurrX
                    //   0x6EC0D2  cmp eax, [ebx+0x130]   ; Param vs CurrY
                    // This used to read Y out of the high word of Recog and the
                    // direction out of Tag, which only worked because GameGate-CS
                    // repacked the header to suit; that repack is gone.
                    //
                    // CM_3037 (3027) has its own native arm at 0x6D9F4B which is
                    // byte-identical to 0x6D9EAF apart from one instruction: where
                    // 0x6D9EFF `66 8B 50 04 mov dx,[msg+4]` hands 0x6EC078 the Ident,
                    // 0x6D9F9B `66 8B 50 08 mov dx,[msg+8]` hands it the Tag. Tag is
                    // therefore the action selector for 3027 and has to survive the
                    // hop into the player message queue.
                    if (M2Share.g_Config.boActionSendActionMsg)
                    {
                        PlayObject.SendActionMsg(PlayObject, DefMsg.Ident, DefMsg.Series & 7,
                            DefMsg.Recog, DefMsg.Param,
                            DefMsg.Ident == Grobal2.CM_3037 ? (int)DefMsg.Tag : 0, "");
                    }
                    else
                    {
                        PlayObject.SendMsg(PlayObject, DefMsg.Ident, DefMsg.Series & 7,
                            DefMsg.Recog, DefMsg.Param,
                            DefMsg.Ident == Grobal2.CM_3037 ? (int)DefMsg.Tag : 0, "");
                    }
                    break;
                case Grobal2.CM_SAY:
                    PlayObject.SendMsg(PlayObject, Grobal2.CM_SAY, 0, 0, 0, 0, sMsg, payload);
                    break;
                default:
                    PlayObject.SendMsg(PlayObject, DefMsg.Ident, DefMsg.Series, DefMsg.Recog, DefMsg.Param, DefMsg.Tag,
                        sMsg, payload);
                    break;
            }
            if (!PlayObject.m_boReadyRun) return;
            switch (DefMsg.Ident)
            {
                case Grobal2.CM_TURN:
                case Grobal2.CM_WALK:
                case Grobal2.CM_SITDOWN:
                case Grobal2.CM_RUN:
                case Grobal2.CM_HIT:
                case Grobal2.CM_HEAVYHIT:
                case Grobal2.CM_BIGHIT:
                case Grobal2.CM_POWERHIT:
                case Grobal2.CM_LONGHIT:
                case Grobal2.CM_WIDEHIT:
                case Grobal2.CM_FIREHIT:
                case Grobal2.CM_CRSHIT:
                case Grobal2.CM_TWINHIT:
                    PlayObject.m_dwRunTick -= 100;
                    break;
            }
        }

        public void SendServerGroupMsg(int nCode, int nServerIdx, string sMsg)
        {
            if (M2Share.nServerIndex == 0)
            {
                SnapsmService.Instance.SendServerSocket(nCode + "/" + nServerIdx + "/" + sMsg);
            }
            else
            {
                SnapsmClient.Instance.SendSocket(nCode + "/" + nServerIdx + "/" + sMsg);
            }
        }

        public void GetISMChangeServerReceive(string flName)
        {
            TPlayObject hum;
            for (var i = 0; i < m_PlayObjectFreeList.Count; i++)
            {
                hum = m_PlayObjectFreeList[i];
                if (hum.m_sSwitchDataTempFile == flName)
                {
                    hum.m_boSwitchDataOK = true;
                    break;
                }
            }
        }

        public void OtherServerUserLogon(int sNum, string uname)
        {
            var Name = string.Empty;
            var apmode = HUtil32.GetValidStr3(uname, ref Name, ":");
            m_OtherUserNameList.Remove(Name);
            m_OtherUserNameList.Add(Name, new ServerGruopInfo()
            {
                nServerIdx = sNum,
                sCharName = uname
            });
        }

        public void OtherServerUserLogout(int sNum, string uname)
        {
            var Name = string.Empty;
            var apmode = HUtil32.GetValidStr3(uname, ref Name, ":");
            m_OtherUserNameList.Remove(Name);
            
            
            
            
            
            
            
            
        }

        
        
        
        
        private TBaseObject AddBaseObject(string sMapName, short nX, short nY,
            int nMonRace, string sMonName, bool initializeMonsterScript = true,
            bool ignoreCellBlockers = false)
        {
            return AddBaseObject(M2Share.MapManager.FindMap(sMapName), nX, nY,
                nMonRace, sMonName, initializeMonsterScript,
                ignoreCellBlockers: ignoreCellBlockers);
        }

        private TBaseObject AddBaseObject(Envirnoment map, short nX, short nY,
            int nMonRace, string sMonName, bool initializeMonsterScript = true,
            bool exactPosition = false, bool ignoreCellBlockers = false)
        {
            TBaseObject result = null;
            TBaseObject Cert = null;
            int n1C;
            int n20;
            int n24;
            object p28;
            if (map == null) return result;
            try
            {
            switch (nMonRace)
            {
                case M2Share.SUPREGUARD:
                    Cert = new SuperGuard();
                    break;
                case M2Share.PETSUPREGUARD:
                    Cert = new PetSuperGuard();
                    break;
                case M2Share.ARCHER_POLICE:
                    Cert = new ArcherPolice();
                    break;
                case M2Share.ANIMAL_CHICKEN:
                    Cert = new Monster
                    {
                        m_boAnimal = true,
                        m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(3500) + 3000),
                        m_nBodyLeathery = 50
                    };
                    break;
                case M2Share.ANIMAL_DEER:
                    if (M2Share.RandomNumber.Random(30) == 0)
                        Cert = new ChickenDeer
                        {
                            m_boAnimal = true,
                            m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(20000) + 10000),
                            m_nBodyLeathery = 150
                        };
                    else
                        Cert = new Monster
                        {
                            m_boAnimal = true,
                            m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(8000) + 8000),
                            m_nBodyLeathery = 150
                        };
                    break;
                case M2Share.ANIMAL_WOLF:
                    Cert = new AtMonster
                    {
                        m_boAnimal = true,
                        m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(8000) + 8000),
                        m_nBodyLeathery = 150
                    };
                    break;
                case M2Share.TRAINER:
                    Cert = new Trainer();
                    break;
                case M2Share.MONSTER_OMA:
                    Cert = new Monster();
                    break;
                case M2Share.MONSTER_OMAKNIGHT:
                    Cert = new AtMonster();
                    break;
                case M2Share.MONSTER_SPITSPIDER:
                    Cert = new SpitSpider();
                    break;
                case 83:
                    Cert = new SlowAtMonster();
                    break;
                case 84:
                    Cert = new Scorpion();
                    break;
                case M2Share.MONSTER_STICK:
                    Cert = new StickMonster();
                    break;
                case 86:
                    Cert = new AtMonster();
                    break;
                case M2Share.MONSTER_DUALAXE:
                    Cert = new DualAxeMonster();
                    break;
                case 88:
                    Cert = new AtMonster();
                    break;
                case 89:
                    Cert = new AtMonster();
                    break;
                case 90:
                    Cert = new GasAttackMonster();
                    break;
                case 91:
                    Cert = new MagCowMonster();
                    break;
                case 92:
                    Cert = new CowKingMonster();
                    break;
                case M2Share.MONSTER_THONEDARK:
                    Cert = new ThornDarkMonster();
                    break;
                case M2Share.MONSTER_LIGHTZOMBI:
                    Cert = new LightingZombi();
                    break;
                case M2Share.MONSTER_DIGOUTZOMBI:
                    Cert = new DigOutZombi();
                    if (M2Share.RandomNumber.Random(2) == 0) Cert.bo2BA = true;
                    break;
                case M2Share.MONSTER_ZILKINZOMBI:
                    Cert = new ZilKinZombi();
                    if (M2Share.RandomNumber.Random(4) == 0) Cert.bo2BA = true;
                    break;
                case 97:
                    Cert = new TCowMonster();
                    if (M2Share.RandomNumber.Random(2) == 0) Cert.bo2BA = true;
                    break;
                case M2Share.MONSTER_WHITESKELETON:
                    Cert = new WhiteSkeleton();
                    break;
                case TPlayObject.NativeMagicTowerArcherRace:
                    Cert = new MagicTowerArcherMonster();
                    break;
                case M2Share.MONSTER_SCULTURE:
                    Cert = new ScultureMonster
                    {
                        bo2BA = true
                    };
                    break;
                case M2Share.MONSTER_SCULTUREKING:
                    Cert = new ScultureKingMonster();
                    break;
                case M2Share.MONSTER_BEEQUEEN:
                    Cert = new BeeQueen();
                    break;
                case 104:
                    Cert = new ArcherMonster();
                    break;
                case 105:
                    Cert = new GasMothMonster();
                    break;
                case 106: 
                    Cert = new GasDungMonster();
                    break;
                case 107:
                    Cert = new CentipedeKingMonster();
                    break;
                // ✅ 战神字节证据 (Tier-1)：race 108 的 case body 逐字节可复刻,
                // 且它构造的类就是 C# 已有的 AtMonster,故本轮接线。
                // EA: 工厂 sub_679F8C 派发 (movzx eax,byte[monRec+0x14] / add eax,-0xB /
                //     cmp eax,0xEE / ja 0x67AE5E / mov al,byte[eax+0x67A026] /
                //     jmp dword[eax*4+0x67A115])；索引表[108-0xB=0x61]=0x25=37；jt[37]=0x67A6FD。
                // case body 0x67A6FD..0x67A72C 全文：
                //   67A6FD  B2 01                 mov  dl,1
                //   67A6FF  A1 98 E7 65 00        mov  eax,[0x65E798]  ; classref -> TATMonster
                //                                 ;   vmt 0x65E7E4 size 1256, 唯一覆写 Run=sub_666AE4
                //                                 ;   = C# AtMonster(parent Monster, 只覆写 Run) 一一对应
                //   67A704  E8 8F C3 FE FF        call sub_666A98      ; TATMonster.Create
                //                                 ;   ctor 内: 0x666AB6 mov eax,0x5DC ; call Random ;
                //                                 ;           add eax,0x5DC ; mov [esi+0x7C],eax
                //                                 ;   = m_dwSearchTime = Random(1500)+1500
                //                                 ;   —— C# AtMonster 构造函数已逐字相同
                //   67A709  89 45 F8              mov  [ebp-8],eax
                //   67A70C  B8 02 00 00 00        mov  eax,2
                //   67A711  E8 36 94 D8 FF        call sub_403B4C      ; Random(2)
                //   67A716  85 C0                 test eax,eax
                //   67A718  0F 85 21 06 00 00     jne  0x67AD3F        ; !=0 跳过,不置位
                //   67A71E  8B 45 F8              mov  eax,[ebp-8]
                //   67A721  C6 80 81 04 00 00 01  mov  byte [eax+0x481],1
                //   67A728  E9 12 06 00 00        jmp  0x67AD3F        ; 汇入公共尾部
                // [+0x481] == C# bo2BA：由 race 95/96/97/101 四个 case 交叉印证
                //   (95:Random(2)==0、96:Random(4)==0、97:Random(2)==0、101:无条件置 1，
                //    与 C# 现有 DigOutZombi/ZilKinZombi/TCowMonster/ScultureMonster 逐条同形)。
                // RNG 序：先 ctor 内 Random(1500)，再 case 内 Random(2)——与下方写法一致。
                case 108:
                    Cert = new AtMonster();
                    if (M2Share.RandomNumber.Random(2) == 0) Cert.bo2BA = true;
                    break;
                case 110:
                    Cert = new CastleDoor();
                    break;
                case 111:
                    Cert = new WallStructure();
                    break;
                case M2Share.MONSTER_ARCHERGUARD:
                    Cert = new ArcherGuard();
                    break;
                case M2Share.MONSTER_ELFMONSTER:
                    Cert = new ElfMonster();
                    break;
                case M2Share.MONSTER_ELFWARRIOR:
                    Cert = new ElfWarriorMonster();
                    break;
                case 115:
                    Cert = new BigHeartMonster();
                    break;
                case 116:
                    Cert = new SpiderHouseMonster();
                    break;
                case 117:
                    Cert = new ExplosionSpider();
                    break;
                case 118:
                    Cert = new HighRiskSpider();
                    break;
                case 119:
                    Cert = new BigPoisionSpider();
                    break;
                case 120:
                    Cert = new SoccerBall();
                    break;
                case 130:
                    Cert = new DoubleCriticalMonster();
                    break;
                case 131:
                    Cert = new RonObject();
                    break;
                case 132:
                    Cert = new SandMobObject();
                    break;
                case 133:
                    Cert = new MagicMonObject();
                    break;
                case 134:
                    Cert = new BoneKingMonster();
                    break;
                case 200:
                    Cert = new ElectronicScolpionMon();
                    break;
                case 201:
                    Cert = new CloneMonster();
                    break;
                case 203:
                    Cert = new TeleMonster();
                    break;
                case 206:
                    Cert = new Khazard();
                    break;
                case 208:
                    Cert = new GreenMonster();
                    break;
                case 209:
                    Cert = new RedMonster();
                    break;
                case 210:
                    Cert = new FrostTiger();
                    break;
                case 214:
                    Cert = new FireMonster();
                    break;
                case 215:
                    Cert = new FireballMonster();
                    break;
                // ✅ 战神字节证据 (Tier-1)：TFireKingMonster 的 race 是 **150**，不是 216。
                // 索引表[150-0xB=0x8B] = 0x44 = 68 ; jt[68] = 0x67A985 ; case body 全文：
                //   67A985  B2 01              mov  dl,1
                //   67A987  A1 34 FF 67 00     mov  eax,[0x67FF34]  ; classref -> TFireKingMonster
                //                              ;   VMT 0x67FF80 size 1256, parent = TAnimal
                //   67A98C  E8 67 78 00 00     call sub_6821F8      ; TFireKingMonster.Create
                //   67A991  89 45 F8           mov  [ebp-8],eax
                //   67A994  E9 A6 03 00 00     jmp  0x67AD3F
                // race 216 在战神【没有 case】：索引表[216-0xB=0xCD] = 0x00 → jt[0] = 0x67AE5E
                // = default sink（`xor eax,eax` → nil）。同理 208/209/210/214/215 也全无原生 case。
                // 归属唯一性（穷尽判据，非 xref 推测）：
                //   · classref 全局 [0x67FF34] 在整个 CODE 段只有 **1** 个加载点 = 0x67A987
                //   · ctor sub_6821F8 的 E8 rel32 调用者全扫 = **1** 个 = 0x67A98C
                //   · `mov/cmp byte [reg+0x178], 0xD8`（把 216 当 race 用）= **0** 站
                //   · 工厂 sub_679F8C 的 4 个调用者(0x67BD77/0x67BE2F/0x67BFE2/0x67CA3B)
                //     调用前 0x40 字节内出现立即数 0xD8 = **0** 处
                // => 216 这个号在战神侧零依据；C# 的 FireKingMonster 类本身是 sub_682304 的忠实移植
                //    (500ms 门 @0x68233E cmp edx,0x1F4；Random(5)+5 @0x682350；Random(20)+5
                //     @0x6823AC；Random(50)==0 @0x682405；Random(8) 转向；RM_BIGHIT=0x2716=10006
                //     @0x682399，RM_HIT=0x2714=10004 @0x68242F)，只有挂的 race 号错了。
                case 150:
                    Cert = new FireKingMonster();
                    break;
            }

            if (Cert != null)
            {
                // AddToMap owns both the cell and count publication.
                Cert.m_boAddToMaped = false;
                Cert.m_boDelFormMaped = true;
                Cert.m_sCharName = sMonName;
                MonInitialize(Cert, sMonName);
                Cert.m_PEnvir = map;
                Cert.m_sMapName = map.sMapName;
                Cert.m_nCurrX = nX;
                Cert.m_nCurrY = nY;
                Cert.m_btDirection = (byte)M2Share.RandomNumber.Random(8);
                // ✅ 战神字节证据 (Tier-1)：m_Abil → m_WAbil 是【逐字节拷贝】，不是别名。
                // EA: MonInitialize `sub_71EA04` 尾部 @0x71EB67-0x71EB7B：
                //   71EB67  56                    push esi
                //   71EB68  8D B3 E8 01 00 00     lea  esi,[ebx+0x1E8]   ; m_Abil
                //   71EB6E  8D BB 64 02 00 00     lea  edi,[ebx+0x264]   ; m_WAbil
                //   71EB74  B9 1F 00 00 00        mov  ecx,0x1F          ; 31 dwords = 0x7C 字节
                //   71EB79  F3 A5                 rep movsd
                //   71EB7B  5E                    pop  esi
                // 两块 0x7C 区域在原生【物理独立】：0x1E8..0x264 与 0x264..0x2E0 不重叠。
                //
                // C# 的 TAbility 是 class(引用类型, SystemModule/Packet/TAbility.cs:8)，
                // 所以旧代码 `Cert.m_WAbil = Cert.m_Abil;` 让两个字段指向【同一个对象】。
                // 这不是无害的：`MonsterRecalcAbilitys()` (GameSvr/Actors/TBaseObject.cs:3097,
                // 由 RecalcAbilitys 在 TBaseObject.Base.cs:2825 对每个 race>=RC_ANIMAL 的怪调用)
                // 把 m_Abil.MaxHP 当【出生模板】读、同时写 m_WAbil.MaxHP：
                //   n8 = m_Abil.MaxHP + Round(m_Abil.MaxHP*0.15)*m_btSlaveExpLevel
                //   m_WAbil.MaxHP = Min(m_Abil.MaxHP + lvl*60, n8)
                // 别名下 “模板” 每次都变成上一轮的结果 → 宠物/召唤兽每次 Recalc 都在自身
                // 基础上复利放大(或被战斗掉血污染)，原生的 m_Abil 永远保持出生值。
                // 修法与 Monster.MakeClone (GameSvr/Monsters/Monster.cs:39-41) 及
                // AddRobotObject (本文件 :4504) 已在用的同一惯用法一致：深拷贝。
                Cert.m_WAbil = new TAbility();
                Cert.m_WAbil.CopyFrom(Cert.m_Abil);
                Cert.OnEnvirnomentChanged();
                if (M2Share.RandomNumber.Random(100) < Cert.m_btCoolEye) Cert.m_boCoolEye = true;
                Cert.Initialize();
                if (Cert.m_boAddtoMapSuccess)
                {
                    p28 = null;
                    if (Cert.m_PEnvir.wWidth < 50)
                        n20 = 2;
                    else
                        n20 = 3;
                    if (Cert.m_PEnvir.wHeight < 250)
                    {
                        if (Cert.m_PEnvir.wHeight < 30)
                            n24 = 2;
                        else
                            n24 = 20;
                    }
                    else
                    {
                        n24 = 50;
                    }

                    n1C = 0;
                    while (true)
                    {
                        // 0x77834B `mov al,[ebp+0xC] / push eax` feeds CanWalk
                        // sub_777EF8, whose 0x777F70 `cmp byte [ebp+8],0 / jne`
                        // short-circuits the cell's object scan.  The generator raises
                        // it after five consecutive failures; every other caller
                        // passes 0.
                        if (!Cert.m_PEnvir.CanWalk(Cert.m_nCurrX,
                                Cert.m_nCurrY, ignoreCellBlockers))
                        {
                            if (exactPosition) break;
                            if (Cert.m_PEnvir.wWidth - n24 - 1 > Cert.m_nCurrX)
                            {
                                Cert.m_nCurrX += (short)n20;
                            }
                            else
                            {
                                Cert.m_nCurrX = (short)(M2Share.RandomNumber.Random(Cert.m_PEnvir.wWidth / 2) + n24);
                                if (Cert.m_PEnvir.wHeight - n24 - 1 > Cert.m_nCurrY)
                                    Cert.m_nCurrY += (short)n20;
                                else
                                    Cert.m_nCurrY =
                                        (short)(M2Share.RandomNumber.Random(Cert.m_PEnvir.wHeight / 2) + n24);
                            }
                        }
                        else
                        {
                            p28 = Cert.m_PEnvir.AddToMap(Cert.m_nCurrX, Cert.m_nCurrY, CellType.OS_MOVINGOBJECT, Cert);
                            break;
                        }

                        n1C++;
                        if (n1C >= 31) break;
                    }

                    if (p28 == null)
                    {
                        RollbackUnpublishedMonster(Cert);
                        Cert = null;
                    }
                }

                if (Cert != null && initializeMonsterScript)
                {
                    M2Share.PasEngine?.TryInitializeMonsterScript(Cert);
                }
            }
            }
            catch (Exception e)
            {
                RollbackUnpublishedMonster(Cert);
                LogMonsterSpawnFailure("construction/Initialize", e);
                Cert = null;
            }

            result = Cert;
            return result;
        }

        private readonly struct MonsterMapPublication
        {
            public MonsterMapPublication(Envirnoment environment, short x,
                short y, bool registrationPublished, byte raceServer)
            {
                Environment = environment;
                X = x;
                Y = y;
                RegistrationPublished = registrationPublished;
                RaceServer = raceServer;
            }

            public Envirnoment Environment { get; }
            public short X { get; }
            public short Y { get; }
            public bool RegistrationPublished { get; }
            public byte RaceServer { get; }
        }

        private static MonsterMapPublication CaptureMapPublication(
            TBaseObject baseObject)
        {
            return new MonsterMapPublication(baseObject?.m_PEnvir,
                baseObject?.m_nCurrX ?? 0, baseObject?.m_nCurrY ?? 0,
                baseObject != null && baseObject.m_boAddToMaped &&
                !baseObject.m_boDelFormMaped,
                baseObject?.m_btRaceServer ?? 0);
        }

        private static void RollbackUnpublishedMonster(TBaseObject baseObject)
        {
            RollbackUnpublishedMonster(baseObject,
                CaptureMapPublication(baseObject));
        }

        private static void RollbackUnpublishedMonster(TBaseObject baseObject,
            MonsterMapPublication mapPublication)
        {
            if (baseObject == null) return;

            var environment = mapPublication.Environment;
            if (environment != null)
            {
                if (mapPublication.RegistrationPublished)
                {
                    baseObject.m_btRaceServer = mapPublication.RaceServer;
                    baseObject.m_boAddToMaped = true;
                    baseObject.m_boDelFormMaped = false;
                }
                var removed = environment.DeleteFromMap(mapPublication.X,
                    mapPublication.Y, CellType.OS_MOVINGOBJECT, baseObject,
                    false);
                if (mapPublication.RegistrationPublished && removed != 1 &&
                    !baseObject.m_boDelFormMaped)
                {
                    environment.RemoveMovingObjectRegistration(baseObject, false);
                }
            }
            M2Share.ObjectManager?.Remove(baseObject.ObjectId, baseObject);
        }

        private static void LogMonsterSpawnFailure(string stage, Exception exception)
        {
            try
            {
                M2Share.ErrorMessage($"[Exception] TUserEngine::RegenMonsterByName {stage}: {exception.Message}");
            }
            catch
            {
                // Spawn rollback must not depend on logging availability.
            }
        }

        
        



        private TBaseObject CreateGeneratedMonster(MonGenInfo monGen, short x,
            short y, bool ignoreCellBlockers = false)
        {
            // SPAWN-15: Check capacity before spawning (native slot check at 0x67CA15).
            // Native: fixed-size array with NULL check: cmp dword [eax+edi*4],0
            // C#: CertList is dynamic but must respect nCount capacity limit.
            // Bug window: nActiveCount decrements at death but CertList entry removal
            // is deferred 5min (ghost-reap) -> without this gate, unbounded growth.
            if (monGen.CertList == null || monGen.CertList.Count >= monGen.nCount)
            {
                return null;
            }

            var cert = AddBaseObject(monGen.sMapName, x, y, monGen.nRace,
                monGen.sMonName, ignoreCellBlockers: ignoreCellBlockers,
                initializeMonsterScript: false);
            if (cert == null) return null;
            var mapPublication = CaptureMapPublication(cert);

            var oldCanReAlive = cert.m_boCanReAlive;
            var oldReAliveTick = cert.m_dwReAliveTick;
            var oldMonGen = cert.m_pMonGen;
            var activeCountBefore = monGen.nActiveCount;
            var certificateCountBefore = monGen.CertCount;
            var certificatePublishAttempted = false;
            try
            {
                cert.m_boCanReAlive = true;
                cert.m_dwReAliveTick = HUtil32.GetTickCount();
                cert.m_pMonGen = monGen;
                monGen.nActiveCount = activeCountBefore + 1;
                nMonsterCount++;  // Native 0x67CA9E..0x67CAA1
                certificatePublishAttempted = true;
                monGen.CertList.Add(cert);
                monGen.CertCount = certificateCountBefore + 1;
            }
            catch
            {
                if (certificatePublishAttempted)
                {
                    try
                    {
                        monGen.CertList?.Remove(cert);
                    }
                    catch
                    {
                        // Continue releasing the actor if a custom list failed.
                    }
                }
                monGen.nActiveCount = activeCountBefore;
                monGen.CertCount = certificateCountBefore;
                nMonsterCount--;  // Rollback
                cert.m_boCanReAlive = oldCanReAlive;
                cert.m_dwReAliveTick = oldReAliveTick;
                cert.m_pMonGen = oldMonGen;
                RollbackUnpublishedMonster(cert, mapPublication);
                throw;
            }

            try
            {
                M2Share.PasEngine?.TryInitializeMonsterScript(cert);
            }
            catch (Exception e)
            {
                LogMonsterSpawnFailure("generator OnInitialize", e);
            }
            return cert;
        }

        private bool RegenMonsters(MonGenInfo MonGen, int nCount)
        {
            const string sExceptionMsg = "[Exception] TUserEngine::RegenMonsters";
            var result = true;
            try
            {
                if (MonGen.nRace > 0)
                {
                    // 战神 spawns one monster per iteration with one coordinate jitter
                    // each, and nothing else.  The jitter lives in the factory
                    // sub_679F8C, whose first two Random calls are the only ones on the
                    // spawn path before the per-race body rolls:
                    //   00679FBD  85 F6              test esi,esi          ; range
                    //   00679FC1  8B C6 / 03 C0 / 40 mov eax,esi / add eax,eax / inc eax
                    //   00679FC9  8B 45 F4           mov eax,[ebp-0xC]     ; 2*range+1
                    //   00679FCC  E8 7B 9B D8 FF     call 0x403B4C
                    //   00679FD1  03 45 14 / 2B C6   add eax,[ebp+0x14] / sub eax,esi
                    //   00679FD9  8B 45 F4           mov eax,[ebp-0xC]
                    //   00679FDC  E8 6B 9B D8 FF     call 0x403B4C
                    //   00679FE1  03 45 10 / 2B C6   add eax,[ebp+0x10] / sub eax,esi
                    // i.e. base + Random(2r+1) - r, x first then y, which is what the two
                    // lines below compute.
                    //
                    // Removed with this commit: a `Random(100) < nMissionGenRate` gate
                    // selecting a "cluster" branch, and that branch's two Random(20) - 10
                    // per-monster offsets.  Neither exists natively.  The regen worker
                    // sub_67C9E0 (0x67C9E0-0x67CC74) and ProcessMonsters sub_67C150
                    // (0x67C150-0x67C2EA) each contain ZERO Random call sites by
                    // full-image E8 census, the modulus 20 appears nowhere on the spawn
                    // path (native jitter is always 2*range+1), and the config key
                    // "MissionGenRate" is 0-hit across the image in case-insensitive
                    // ASCII and UTF-16LE.
                    //
                    // The per-call budget is a COUNT, not a stopwatch.  sub_67C9E0
                    // keeps the tally in [ebp-0x10], bumps it only after a successful
                    // factory return, and turns the whole call into "false" the moment
                    // it reaches 25:
                    //   67CAA4  FF 45 F0        inc dword [ebp-0x10]
                    //   67CAAC  83 7D F0 19     cmp dword [ebp-0x10],0x19
                    //   67CAB0  7C 09           jl 0x67CABB          ; keep filling
                    //   67CAB2  C6 45 F7 00     mov byte [ebp-9],0   ; result = FALSE
                    //   67CAB6  EB 0B           jmp 0x67CAC3
                    // C# used a g_dwZenLimit millisecond budget in this slot, which has
                    // no native counterpart; the caller's reaction to "false" (retry
                    // the same generator next tick without touching its cursor or its
                    // dwStartTick) only makes sense against the count.
                    var nSpawned = 0;
                    for (var i = 0; i < nCount; i++)
                    {
                        // An occupied slot is not a failure: 0x67CA15
                        // `cmp dword [eax+edi*4],0 / jne 0x67CABB` skips it without
                        // touching [gen+0x38] and without drawing coordinates, and
                        // running out of slots just ends the worker's loop.  Only a nil
                        // factory return reaches 0x67CAB8, so the capacity test has to
                        // happen here rather than being folded into the factory's null
                        // return.
                        if (MonGen.CertList == null
                            || MonGen.CertList.Count >= MonGen.nCount)
                        {
                            break;
                        }
                        // 0x67CA2B..0x67CA32 computes the relax flag from the CURRENT
                        // failure tally, before the factory call that may reset it.
                        var boIgnoreCellBlockers =
                            MonGen.nFailCount >= NativeMonGenFailRelaxThreshold;
                        // 0x679FBD `test esi,esi` / 0x679FBF `jle 0x679FE9` skips BOTH
                        // draws when the range is not positive, so a point generator
                        // (nRange == 0) consumes no randomness at all.  C# was calling
                        // Random(1) twice, which returns 0 both times but still advances
                        // the sequence twice per monster.
                        var nX = (short)MonGen.nX;
                        var nY = (short)MonGen.nY;
                        if (MonGen.nRange > 0)
                        {
                            nX = (short)(MonGen.nX - MonGen.nRange + M2Share.RandomNumber.Random(MonGen.nRange * 2 + 1));
                            nY = (short)(MonGen.nY - MonGen.nRange + M2Share.RandomNumber.Random(MonGen.nRange * 2 + 1));
                        }
                        if (CreateGeneratedMonster(MonGen, nX, nY,
                                boIgnoreCellBlockers) == null)
                        {
                            MonGen.nFailCount++;              // 0x67CAB8
                            continue;                         // 0x67CABB, keeps scanning
                        }
                        MonGen.nFailCount = 0;                // 0x67CAA7 / 0x67CAA9
                        nSpawned++;
                        if (nSpawned >= NativeMonGenSpawnBudget)
                        {
                            result = false;
                            break;
                        }
                    }
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
            return result;
        }

        public TPlayObject GetPlayObject(string sName)
        {
            TPlayObject result = null;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                if (string.Compare(m_PlayObjectList[i].m_sCharName, sName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    TPlayObject PlayObject = m_PlayObjectList[i];
                    if (!PlayObject.m_boGhost)
                    {
                        if (!(PlayObject.m_boPasswordLocked && PlayObject.m_boObMode && PlayObject.m_boAdminMode))
                        {
                            result = PlayObject;
                        }
                    }
                    break;
                }
            }
            return result;
        }

        public List<TPlayObject> GetPlayerList() => PlayObjects.ToList();
        public void KickPlayObjectEx(string sName)
        {
            TPlayObject PlayObject;
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                for (var i = 0; i < m_PlayObjectList.Count; i++)
                {
                    if (string.Compare(m_PlayObjectList[i].m_sCharName, sName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        PlayObject = m_PlayObjectList[i];
                        PlayObject.m_boEmergencyClose = true;
                        // Duplicate-login kick should follow normal logout flow; otherwise account/session may stay occupied.
                        PlayObject.m_boSoftClose = true;
                        PlayObject.m_boReconnection = false;
                        break;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }
        }

        public TPlayObject GetPlayObjectEx(string sName)
        {
            TPlayObject result = null;
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                for (var i = 0; i < m_PlayObjectList.Count; i++)
                {
                    if (string.Compare(m_PlayObjectList[i].m_sCharName, sName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        result = m_PlayObjectList[i];
                        break;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }
            return result;
        }

        public object FindMerchant(int merchantId)
        {
            var normNpc = M2Share.ObjectManager.Get(merchantId);
            NormNpc npcObject = null;
            var npcType = normNpc.GetType();
            if (npcType == typeof(Merchant))
            {
                npcObject = (Merchant)Convert.ChangeType(normNpc, typeof(Merchant));
            }
            if (npcType == typeof(TGuildOfficial))
            {
                npcObject = (TGuildOfficial)Convert.ChangeType(normNpc, typeof(TGuildOfficial));
            }
            if (npcType == typeof(NormNpc))
            {
                npcObject = (NormNpc)Convert.ChangeType(normNpc, typeof(NormNpc));
            }
            if (npcType == typeof(CastleOfficial))
            {
                npcObject = (CastleOfficial)Convert.ChangeType(normNpc, typeof(CastleOfficial));
            }
            return npcObject;
        }

        public object FindNPC(int npcId)
        {
            return M2Share.ObjectManager.Get(npcId); ;
        }

        
        
        
        
        public int GetMapOfRangeHumanCount(Envirnoment Envir, int nX, int nY, int nRange)
        {
            var result = 0;
            TPlayObject PlayObject;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (!PlayObject.m_boGhost && PlayObject.m_PEnvir == Envir)
                {
                    if (Math.Abs(PlayObject.m_nCurrX - nX) < nRange && Math.Abs(PlayObject.m_nCurrY - nY) < nRange)
                    {
                        result++;
                    }
                }
            }
            return result;
        }

        public bool GetHumPermission(string sUserName, ref string sIPaddr, ref byte btPermission)
        {
            var result = false;
            TAdminInfo AdminInfo;
            btPermission = (byte)M2Share.g_Config.nStartPermission;
            for (var i = 0; i < m_AdminList.Count; i++)
            {
                AdminInfo = m_AdminList[i];
                if (string.Compare(AdminInfo.sChrName, sUserName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    btPermission = (byte)AdminInfo.nLv;
                    sIPaddr = AdminInfo.sIPaddr;
                    result = true;
                    break;
                }
            }
            return result;
        }

        public void AddUserOpenInfo(TUserOpenInfo UserOpenInfo)
        {
            HUtil32.EnterCriticalSection(m_LoadPlaySection);
            try
            {
                m_LoadPlayList.Add(UserOpenInfo);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_LoadPlaySection);
            }
        }

        public void CancelUserOpen(int gateIdx, int socket,
            long userGeneration)
        {
            lock (m_LoadPlaySection)
            {
                for (var i = m_LoadPlayList.Count - 1; i >= 0; i--)
                {
                    var load = m_LoadPlayList[i]?.LoadUser;
                    if (load == null || load.nGateIdx != gateIdx ||
                        load.nSocket != socket ||
                        (userGeneration != 0 &&
                         load.UserGeneration != userGeneration))
                        continue;
                    m_LoadPlayList.RemoveAt(i);
                }
                MarkCancelledUsers(m_NewHumanList, gateIdx, socket,
                    userGeneration);
                MarkCancelledUsers(m_PlayObjectList, gateIdx, socket,
                    userGeneration);
                MarkCancelledUsers(m_AiPlayObjectList, gateIdx, socket,
                    userGeneration);
                MarkCancelledUsers(m_PlayObjectFreeList, gateIdx, socket,
                    userGeneration);
            }
        }

        private IList<TPlayObject> TakeNewHumansForBinding()
        {
            lock (m_LoadPlaySection)
            {
                var result = m_NewHumanList.Where(player => player != null)
                    .ToList();
                m_NewHumanList.Clear();
                return result;
            }
        }

        private static void MarkCancelledUsers(
            IList<TPlayObject> users, int gateIdx, int socket,
            long userGeneration)
        {
            for (var i = users.Count - 1; i >= 0; i--)
            {
                if (i >= users.Count) continue;
                TPlayObject playObject;
                try
                {
                    playObject = users[i];
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }
                if (playObject == null || playObject.m_nGateIdx != gateIdx ||
                    playObject.m_nSocket != socket ||
                    (userGeneration != 0 &&
                     playObject.m_UserGeneration != userGeneration))
                    continue;
                playObject.m_boEmergencyClose = true;
                playObject.m_boSoftClose = true;
            }
        }

        private void KickOnlineUser(string sChrName)
        {
            TPlayObject PlayObject;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (string.Compare(PlayObject.m_sCharName, sChrName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    PlayObject.m_boKickFlag = true;
                    break;
                }
            }
        }

        private void SendChangeServer(TPlayObject PlayObject, byte nServerIndex)
        {
            var sIPaddr = string.Empty;
            var nPort = 0;
            if (M2Share.GetMultiServerAddrPort(nServerIndex, ref sIPaddr, ref nPort))
            {
                PlayObject.m_boReconnection = true;
            }
        }

        public void SaveHumanRcd(TPlayObject PlayObject)
        {
            SaveHumanRcdCore(PlayObject, 0);
        }

        public void SaveHumanRcd(TPlayObject PlayObject, ushort saveMode)
        {
            SaveHumanRcdCore(PlayObject, saveMode);
        }

        private void SaveHumanRcdCore(TPlayObject PlayObject, ushort saveMode)
        {
            if (PlayObject == null) return;
            if (PlayObject.m_boAI) 
            {
                return;
            }
            PlayObject.SaveNativeAccountStorageIfDirty();
            if (PlayObject.m_HeroObject != null)
                HeroDataService.QueueSave(PlayObject.m_HeroObject);
            M2Share.CreditCardService?.TrySaveDue(
                PlayObject, HUtil32.GetTickCount(), true);
            if (!PlayObject.PersistNativeHeroState())
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 人物原生ScriptData过短，英雄状态无法回写: {PlayObject.m_sCharName}");
            }
            if (!PlayObject.PersistNativeChatShieldMask())
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 人物原生记录过短，聊天屏蔽状态无法回写: {PlayObject.m_sCharName}");
            }
            if (!PlayObject.PersistNativeCommonInformation())
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 人物原生记录过短，1099状态无法回写: {PlayObject.m_sCharName}");
            }
            if (!PlayObject.PersistNativeUnmappedScalars())
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 人物原生记录过短，PK点/幸运/攻击模式/平台等级/加油点/天地合一无法回写: {PlayObject.m_sCharName}");
            }
            if (!PlayObject.PersistNativeFixedCoord())
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 人物原生记录过短，定位石召回坐标无法回写: {PlayObject.m_sCharName}");
            }
            // 原生 SAVE 把当前本地时间换算到数据库时钟后再写绝对到期时间
            // (0x6B12F9 Now / 0x6B12FE fsub [ebx+0x780])，三个 buff 共用同一基准。
            if (!PlayObject.PersistNativeTimedExpBuff(
                    PlayObject.NativeDbClockNow(DateTime.Now.ToOADate())))
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 人物原生记录过短，倍经验/真视时间无法回写: {PlayObject.m_sCharName}");
            }
            // Rebuild ScriptData sections 2/6/7/8. Must run AFTER
            // PersistNativeHeroState, which patches a byte inside the type 0
            // payload in place and would be invalidated by a reframe.
            if (!PlayObject.PersistNativeScriptSections())
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 人物原生ScriptData分节格式损坏，灵游/身体状态/冷却/一次性标志无法回写: {PlayObject.m_sCharName}");
            }
            // 背包这一路比对的是**能写回多少**而不是**能装多少**：存档记录固定 48 槽，
            // 48 格以后的物品要靠大背包持久层，两者之和就是 PersistableOf。超出即拒绝
            // 整次存盘 —— 原生在 0x6B171B 处是静默截断，那等于删物品。
            if (PlayObject.m_ItemList.Count > BagCapacity.PersistableOf(PlayObject)
                || PlayObject.m_StorageItemList.Count > TPlayObject.MAX_STORAGE_ITEM_COUNT
                || PlayObject.m_MagicList.Count > Grobal2.MAXMAGIC)
            {
                M2Share.ErrorMessage(
                    $"[SaveHumanRcd] 拒绝截断人物存档 {PlayObject.m_sCharName}: " +
                    $"bag={PlayObject.m_ItemList.Count}/{BagCapacity.PersistableOf(PlayObject)}, " +
                    $"storage={PlayObject.m_StorageItemList.Count}/{TPlayObject.MAX_STORAGE_ITEM_COUNT}, " +
                    $"magic={PlayObject.m_MagicList.Count}/{Grobal2.MAXMAGIC}");
                return;
            }
            YbDbClient.Instance.RequestLingFuAccounting(PlayObject);
            PlayObject.FlushSecHeroPracticeLingFuLog();
            var SaveRcd = new TSaveRcd
            {
                sAccount = PlayObject.m_sUserID,
                sChrName = PlayObject.m_sCharName,
                nSessionID = PlayObject.m_nSessionID,
                NativeSaveMode = saveMode,
                NativeSaveParam1 = 0,
                NativeSaveParam2 = 0,
                PlayObject = PlayObject,
                LastErrorLogTick = HUtil32.GetTickCount() - 10_000,
                HumanRcd = new THumDataInfo
                {
                    NativeData = PlayObject.m_NativeHumanData,
                    NativeDataCrc = PlayObject.m_NativeHumanDataCrc,
                    NativeScriptData = PlayObject.m_NativeScriptData,
                    NativeScriptDataCrc = PlayObject.m_NativeScriptDataCrc
                }
            };
            SaveRcd.HumanRcd.Data.Initialization();
            PlayObject.MakeSaveRcd(ref SaveRcd.HumanRcd);
            M2Share.FrontEngine.AddToSaveRcdList(SaveRcd);
        }

        private void AddToHumanFreeList(TPlayObject PlayObject)
        {
            PlayObject.m_dwGhostTick = HUtil32.GetTickCount();
            m_PlayObjectFreeList.Add(PlayObject);
        }

        private void GetHumData(TPlayObject PlayObject, ref THumDataInfo HumanRcd)
        {
            THumInfoData HumData;
            TUserItem[] HumItems;
            TUserItem[] BagItems;
            TMagicRcd[] HumMagic;
            TMagic MagicInfo;
            TUserMagic UserMagic;
            TUserItem[] StorageItems;
            TUserItem UserItem;
            if (HumanRcd == null || HumanRcd.Data == null)
            {
                throw new ArgumentException($"HumanRcd.Data is null for character {PlayObject.m_sCharName}");
            }
            if (HumanRcd.Header == null || !double.IsFinite(HumanRcd.Header.dCreateDate))
            {
                throw new InvalidDataException(
                    $"Invalid native create date for character {PlayObject.m_sCharName}");
            }
            try
            {
                _ = DateTime.FromOADate(HumanRcd.Header.dCreateDate);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(
                    $"Invalid native create date for character {PlayObject.m_sCharName}", ex);
            }
            HumData = HumanRcd.Data;
            PlayObject.m_dCreateDate = HumanRcd.Header.dCreateDate;
            PlayObject.m_NativeHumanData = HumanRcd.NativeData;
            PlayObject.m_NativeHumanDataCrc = HumanRcd.NativeDataCrc;
            PlayObject.m_NativeScriptData = HumanRcd.NativeScriptData;
            PlayObject.m_NativeScriptDataCrc = HumanRcd.NativeScriptDataCrc;
            PlayObject.LoadNativeMailRecipientId(HumanRcd.NativeUserId);
            PlayObject.RestoreNativeHeroState();
            PlayObject.RestoreNativeChatShieldMask();
            // ScriptData sections 2/6/7/8 (shenYou / bodyState / coldTime /
            // FirstDoSome). Native parses them on the GAME side at character load
            // (sub_6E448C, sole caller 0x6547D1), which is exactly here.
            PlayObject.RestoreNativeScriptSections();
            PlayObject.RestoreNativeCommonInformation();
            PlayObject.m_sCharName = HumData.sCharName;
            PlayObject.m_sMapName = HumData.sCurMap;
            PlayObject.m_nCurrX = HumData.wCurX;
            PlayObject.m_nCurrY = HumData.wCurY;
            PlayObject.m_btDirection = HumData.btDir;
            PlayObject.m_btHair = HumData.btHair;
            PlayObject.m_btGender = Enum.TryParse<PlayGender>(HumData.btSex.ToString(), out var gender) ? gender : PlayGender.Man;
            PlayObject.m_btJob = HumData.btJob;
            PlayObject.m_nGold = HumData.nGold;
            PlayObject.m_Abil.Level = HumData.Abil.Level;
            PlayObject.m_Abil.HP = HumData.Abil.HP;
            PlayObject.m_Abil.MP = HumData.Abil.MP;
            PlayObject.m_Abil.MaxHP = HumData.Abil.MaxHP;
            PlayObject.m_Abil.MaxMP = HumData.Abil.MaxMP;
            PlayObject.m_Abil.Exp = HumData.Abil.Exp;
            PlayObject.m_Abil.MaxExp = HumData.Abil.MaxExp;
            PlayObject.m_Abil.Weight = HumData.Abil.Weight;
            PlayObject.m_Abil.MaxWeight = HumData.Abil.MaxWeight;
            PlayObject.m_Abil.WearWeight = HumData.Abil.WearWeight;
            PlayObject.m_Abil.MaxWearWeight = HumData.Abil.MaxWearWeight;
            PlayObject.m_Abil.HandWeight = HumData.Abil.HandWeight;
            PlayObject.m_Abil.MaxHandWeight = HumData.Abil.MaxHandWeight;
            // Load path of the legacy-slot trio (REPLICATION_RULES 4.19). The
            // slots no longer have storage of their own; CopyFrom replays the
            // saved seconds onto the native Self+0xDC node list, which is the
            // single authority. Slot 11 (STATE_BUBBLEDEFENCEUP, native state 20)
            // is still cleared here, matching the save path in
            // TPlayObject.GetHumData - the two ends have to agree or a login
            // would resurrect what the logout dropped.
            // Zeroed before the replay, not after, so the restore stays silent:
            // dropping the slot afterwards would go through the indexer and
            // broadcast a removal for a player who is not on a map yet.
            var restoredStatus = HumData.wStatusTimeArr == null
                ? new ushort[TBaseObject.LegacyStatusSlotCount]
                : (ushort[])HumData.wStatusTimeArr.Clone();
            if (restoredStatus.Length > Grobal2.STATE_BUBBLEDEFENCEUP)
            {
                restoredStatus[Grobal2.STATE_BUBBLEDEFENCEUP] = 0;
            }
            PlayObject.m_wStatusTimeArr.CopyFrom(restoredStatus);
            PlayObject.m_sHomeMap = HumData.sHomeMap;
            PlayObject.m_nHomeX = HumData.wHomeX;
            PlayObject.m_nHomeY = HumData.wHomeY;
            PlayObject.m_BonusAbil = HumData.BonusAbil;
            PlayObject.m_nBonusPoint = HumData.nBonusPoint;
            PlayObject.m_btCreditPoint = HumData.btCreditPoint;
            PlayObject.m_nShengWan = HumData.nShengWan;
            PlayObject.m_nLingFu = HumData.nLingFu;
            PlayObject.m_nUsedLingFu = HumData.nUsedLingFu;
            PlayObject.m_nNickLinFu = HumData.nNickLinFu;
            PlayObject.m_nForceLv = HumData.ForceLv;
            PlayObject.m_nForceExp = HumData.ForceExp;
            PlayObject.m_nFightPoints = HumData.FightPoints;
            PlayObject.m_nSfLevel = HumData.sfLevel;
            PlayObject.m_dNativeHeroIntimacy = HumData.NativeHeroIntimacy;
            PlayObject.m_NativeHeroExperienceAccumulator =
                HumData.NativeHeroExperienceAccumulator?.Length == 24
                    ? (byte[])HumData.NativeHeroExperienceAccumulator.Clone()
                    : new byte[24];
            PlayObject.m_btSecHeroPracticeRewardMode = HumData.btSecHeroPracticeRewardMode;
            PlayObject.m_btSecHeroPracticeCostTier = HumData.btSecHeroPracticeCostTier;
            PlayObject.m_wSecHeroPracticeLevel = HumData.wSecHeroPracticeLevel;
            PlayObject.m_btGoldActNextLevel = HumData.btGoldActNextLevel;
            PlayObject.m_btFirstUsedGiftStage = HumData.btFirstUsedGiftStage;
            PlayObject.m_nActivePoint = HumData.nActivePoint;
            PlayObject.RestoreNativeExchangeBookPersonalRareCounters(
                HumData.ExchangeBookPersonalRareCounters);
            PlayObject.RefreshNativeHeroIntimacy();
            PlayObject.m_btReLevel = HumData.btReLevel;
            PlayObject.m_sMasterName = HumData.sMasterName;
            PlayObject.m_boAllowMarry = HumData.boAllowMarry;
            PlayObject.m_boMarried = HumData.boMarried;
            PlayObject.m_boAllowMaster = HumData.boAllowMaster;
            PlayObject.m_boMaster = HumData.boMaster;
            PlayObject.m_boStudent = HumData.boStudent;
            PlayObject.m_btStudentOrder = HumData.btStudentOrder;
            PlayObject.m_nStudentCount = HumData.btStudentCount;
            PlayObject.m_sStudentNames = new string[5];
            for (var i = 0; i < PlayObject.m_sStudentNames.Length; i++)
                PlayObject.m_sStudentNames[i] = HumData.sStudentNames != null
                    && i < HumData.sStudentNames.Length
                    ? HumData.sStudentNames[i] ?? string.Empty
                    : string.Empty;
            PlayObject.m_sDearName = HumData.sDearName;
            PlayObject.m_sStoragePwd = HumData.sStoragePwd;
            PlayObject.m_nStorageSpaceCount = Math.Clamp(
                HumData.StorageSpaceCount, TPlayObject.MIN_STORAGE_ITEM_COUNT,
                TPlayObject.MAX_STORAGE_ITEM_COUNT);
            if (PlayObject.m_sStoragePwd != "")
            {
                PlayObject.m_boPasswordLocked = true;
            }
            PlayObject.m_nGameGold = HumData.nGameGold;
            PlayObject.m_nGamePoint = HumData.nGamePoint;
            PlayObject.m_nPayMentPoint = HumData.nPayMentPoint;
            PlayObject.m_nPkPoint = HumData.nPKPoint;
            if (HumData.btAllowGroup > 0)
            {
                PlayObject.m_boAllowGroup = true;
            }
            else
            {
                PlayObject.m_boAllowGroup = false;
            }
            PlayObject.btB2 = HumData.btF9;
            PlayObject.m_btAttatckMode =
                HumData.btAttatckMode <= TPlayObject.NativeAttackModeCorps
                    ? HumData.btAttatckMode
                    : TPlayObject.NativeAttackModeAll;
            PlayObject.m_nIncHealth = HumData.btIncHealth;
            PlayObject.m_nIncSpell = HumData.btIncSpell;
            PlayObject.m_nIncHealing = HumData.btIncHealing;
            PlayObject.m_nFightZoneDieCount = HumData.btFightZoneDieCount;
            PlayObject.m_sUserID = HumData.sAccount;
            PlayObject.nC4 = HumData.btEE;
            PlayObject.m_boLockLogon = HumData.boLockLogon;
            PlayObject.m_wContribution = HumData.wContribution;
            PlayObject.btC8 = HumData.btEF;
            PlayObject.m_nHungerStatus = HumData.nHungerStatus;
            PlayObject.m_boAllowGuildReCall = HumData.boAllowGuildReCall;
            PlayObject.m_wGroupRcallTime = HumData.wGroupRcallTime;
            PlayObject.m_nBodyLuckLevel = (int)HumData.dBodyLuck; // 载入权威小值(原生 [+0x164]↔HumData[+160]); 越界由 add-to-map 的 AddBodyLuck(0) re-clamp
            // The shared DTO codec models none of these slots, so the DTO
            // members above arrive as 0 and would wipe the live value on every
            // login. Re-read them straight from the native record; must stay
            // AFTER the DTO assignments it supersedes.
            PlayObject.RestoreNativeUnmappedScalars();
            // Same reasoning for rec[0x5AC]/[0x5BC]/[0x5BE] (定位石 recall anchor):
            // pure clone-carry with no DTO member, so TryDecode never surfaces them.
            PlayObject.RestoreNativeFixedCoord();
            // 倍经验/真视/第三计时 buff: rec[0x110]/[0x4C6]/[0x118]/[0x120]. These
            // hold ABSOLUTE deadlines on the DB clock, so the per-session offset
            // (原生 obj+0x778 / obj+0x780, 0x6B026E..0x6B0292) must be established
            // FIRST -- LOAD does exactly that before reading any deadline.
            PlayObject.RestoreNativeTimedExpBuff(
                PlayObject.EstablishNativeDbClock(DateTime.Now.ToOADate()));
            // m_boAllowGroupReCall (天地合一 toggle, 原生 obj+0xBA4 <-> rec+0x0D7,
            // enc 0x6B11DA / dec 0x6B0104) is now restored by
            // RestoreNativeUnmappedScalars above. The shared DTO codec does not
            // model rec+0x0D7, so HumData.boAllowGroupReCall always arrives false
            // and assigning it HERE -- after the restore -- silently wiped the
            // toggle on every login. Do not reinstate this line.
            PlayObject.m_QuestUnitOpen = HumData.QuestUnitOpen;
            PlayObject.m_QuestUnit = HumData.QuestUnit;
            PlayObject.m_QuestFlag = HumData.QuestFlag;
            PlayObject.m_ScriptVVars.Clear();
            PlayObject.m_ScriptSVars.Clear();
            // 原生 SetV/SetS 无零值特例（sub_6E4140 四个存储点原样写入），
            // 所以持久化的 0 必须原样载回。先前这里跳过 0，会把写入端
            // 已修好的零值在下次登录时重新抹掉——两处必须同时成立。
            if (HumData.ScriptV != null)
            {
                foreach (var variable in HumData.ScriptV)
                {
                    // group-0 V lives at +0x80C..+0x99B and is never in the
                    // type1 dynarray. A flat key below 1001 can only be group 0
                    // (sub_6E42CC imul 0x3E8), so it does not belong here.
                    if (variable.Key < 1001) continue;
                    PlayObject.m_ScriptVVars[variable.Key] = variable.Value;
                }
            }
            if (HumData.ScriptS != null)
            {
                foreach (var variable in HumData.ScriptS)
                {
                    if (variable.Key < 1001) continue;
                    PlayObject.m_ScriptSVars[variable.Key] = variable.Value;
                }
            }
            HumItems = HumanRcd.Data.HumItems;
            if (HumItems != null)
            {
                var itemCount = Math.Min(PlayObject.m_UseItems.Length, HumItems.Length);
                for (var i = 0; i < itemCount; i++)
                    PlayObject.m_UseItems[i] = HumItems[i];
            }
            BagItems = HumanRcd.Data.BagItems;
            if (BagItems != null)
            {
                for (var i = BagItems.GetLowerBound(0); i <= BagItems.GetUpperBound(0); i++)
                {
                    if (BagItems[i] == null)
                    {
                        continue;
                    }
                    if (BagItems[i].wIndex > 0)
                    {
                        UserItem = BagItems[i];
                        PlayObject.m_ItemList.Add(UserItem);
                    }
                }
            }
            HumMagic = HumanRcd.Data.Magic;
            if (HumMagic != null)
            {
                for (var i = HumMagic.GetLowerBound(0); i <= HumMagic.GetUpperBound(0); i++)
                {
                    if (HumMagic[i] == null)
                    {
                        continue;
                    }
                    MagicInfo = M2Share.UserEngine.FindMagic(HumMagic[i].wMagIdx);
                    if (MagicInfo != null)
                    {
                        UserMagic = new TUserMagic();
                        UserMagic.MagicInfo = MagicInfo;
                        UserMagic.wMagIdx = HumMagic[i].wMagIdx;
                        UserMagic.btLevel = HumMagic[i].btLevel;
                        UserMagic.btKey = HumMagic[i].btKey;
                        UserMagic.nTranPoint = HumMagic[i].nTranPoint;
                        UserMagic.NativeRecord = HumMagic[i].NativeRecord;
                        PlayObject.m_MagicList.Add(UserMagic);
                    }
                }
            }
            StorageItems = HumanRcd.Data.StorageItems;
            if (StorageItems != null)
            {
                for (var i = StorageItems.GetLowerBound(0); i <= StorageItems.GetUpperBound(0); i++)
                {
                    if (StorageItems[i] == null)
                    {
                        continue;
                    }
                    if (StorageItems[i].wIndex > 0)
                    {
                        UserItem = StorageItems[i];
                        PlayObject.m_StorageItemList.Add(UserItem);
                    }
                }
            }
        }

        private string GetHomeInfo(int nJob, ref short nX, ref short nY)
        {
            string result;
            int I;
            if (M2Share.StartPointList.Count > 0)
            {
                if (M2Share.StartPointList.Count > M2Share.g_Config.nStartPointSize)
                    I = M2Share.RandomNumber.Random(M2Share.g_Config.nStartPointSize);
                else
                    I = 0;
                result = M2Share.GetStartPointInfo(I, ref nX, ref nY);
            }
            else
            {
                result = M2Share.g_Config.sHomeMap;
                nX = M2Share.g_Config.nHomeX;
                nY = M2Share.g_Config.nHomeY;
            }
            return result;
        }

        private short GetRandHomeX(TPlayObject PlayObject)
        {
            return (short)(M2Share.RandomNumber.Random(3) + (PlayObject.m_nHomeX - 2));
        }

        private short GetRandHomeY(TPlayObject PlayObject)
        {
            return (short)(M2Share.RandomNumber.Random(3) + (PlayObject.m_nHomeY - 2));
        }

        public TMagic FindMagic(int nMagIdx)
        {
            TMagic result = null;
            TMagic Magic = null;
            var definitions = Volatile.Read(ref _magicDefinitions).Human;
            for (var i = 0; i < definitions.Count; i++)
            {
                Magic = definitions[i];
                if (Magic.wMagicID == nMagIdx)
                {
                    result = Magic;
                    break;
                }
            }
            return result;
        }

        public TMagic FindHeroMagic(int nMagIdx)
        {
            var definitions = Volatile.Read(ref _magicDefinitions).Hero;
            for (var i = 0; i < definitions.Count; i++)
            {
                var magic = definitions[i];
                if (magic.wMagicID == nMagIdx)
                    return magic;
            }
            return null;
        }

        private void MonInitialize(TBaseObject BaseObject, string sMonName)
        {
            if (TryGetMonsterInfo(sMonName, out var Monster))
            {
                BaseObject.m_btRaceServer = Monster.btRace;
                BaseObject.m_btRaceImg = Monster.btRaceImg;
                BaseObject.m_wAppr = Monster.wAppr;
                BaseObject.m_Abil.Level = Monster.wLevel;
                BaseObject.m_btLifeAttrib = Monster.btLifeAttrib;
                BaseObject.m_btCoolEye = (byte)Monster.wCoolEye;
                BaseObject.m_dwFightExp = Monster.dwExp;
                BaseObject.m_Abil.HP = Monster.wHP;
                BaseObject.m_Abil.MaxHP = Monster.wHP;
                BaseObject.m_btMonsterWeapon = 0;
                BaseObject.m_Abil.MP = Monster.wMP;
                BaseObject.m_Abil.MaxMP = Monster.wMP;
                BaseObject.m_Abil.AC = HUtil32.MakeLong(Monster.wAC, Monster.wAC);
                BaseObject.m_Abil.MAC = HUtil32.MakeLong(Monster.wMAC, Monster.wMAC);
                BaseObject.m_Abil.DC = HUtil32.MakeLong(Monster.wDC, Monster.wMaxDC);
                BaseObject.m_Abil.MC = HUtil32.MakeLong(Monster.wMC, Monster.wMC);
                BaseObject.m_Abil.SC = HUtil32.MakeLong(Monster.wSC, Monster.wSC);
                BaseObject.m_btSpeedPoint = (byte)Monster.wSpeed;
                BaseObject.m_wSpeedPoint = Monster.wSpeed;
                BaseObject.m_btHitPoint = Monster.wHitPoint;
                BaseObject.m_nWalkSpeed = Monster.wWalkSpeed;
                BaseObject.m_nWalkStep = Monster.wWalkStep;
                BaseObject.m_dwWalkWait = Monster.wWalkWait;
                BaseObject.m_nNextHitTime = Monster.wAttackSpeed;
                BaseObject.m_boNastyMode = Monster.boAggro;
                BaseObject.m_boNoTame = Monster.boTame;

                var publication = Volatile.Read(ref _monsterDefinitions);
                var definition = publication.Catalog?.FindByName(sMonName);
                if (definition != null)
                {
                    BaseObject.ApplyNativeType2MonsterProjection(
                        NativeType2MonsterActorProjection.Create(definition,
                            publication.ManagerTables));
                }
            }
        }

        public bool OpenDoor(Envirnoment Envir, int nX, int nY)
        {
            var result = false;
            var door = Envir.GetDoor(nX, nY);
            if (door != null && !door.Status.boOpened && !door.Status.bo01)
            {
                door.Status.boOpened = true;
                door.Status.dwOpenTick = HUtil32.GetTickCount();
                SendDoorStatus(Envir, nX, nY, Grobal2.RM_DOOROPEN, 0, nX, nY, 0, "");
                result = true;
            }
            return result;
        }

        private bool CloseDoor(Envirnoment Envir, TDoorInfo Door)
        {
            var result = false;
            if (Door != null && Door.Status.boOpened)
            {
                Door.Status.boOpened = false;
                SendDoorStatus(Envir, Door.nX, Door.nY, Grobal2.RM_DOORCLOSE, 0, Door.nX, Door.nY, 0, "");
                result = true;
            }
            return result;
        }

        private void SendDoorStatus(Envirnoment Envir, int nX, int nY, short wIdent, short wX, int nDoorX, int nDoorY,
            int nA, string sStr)
        {
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            TBaseObject BaseObject;
            int n1C = nX - 12;
            int n24 = nX + 12;
            int n20 = nY - 12;
            int n28 = nY + 12;
            for (var n10 = n1C; n10 <= n24; n10++)
            {
                for (var n14 = n20; n14 <= n28; n14++)
                {
                    var mapCell = false;
                    MapCellInfo = Envir.GetMapCellInfo(n10, n14, ref mapCell);
                    if (mapCell && MapCellInfo.ObjList != null)
                    {
                        for (var i = 0; i < MapCellInfo.Count; i++)
                        {
                            OSObject = MapCellInfo.ObjList[i];
                            if (OSObject != null && OSObject.CellType == CellType.OS_MOVINGOBJECT)
                            {
                                BaseObject = (TBaseObject)OSObject.CellObj;
                                if (BaseObject != null && !BaseObject.m_boGhost && BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                {
                                    BaseObject.SendMsg(BaseObject, wIdent, wX, nDoorX, nDoorY, nA, sStr);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ProcessMapDoor()
        {
            TDoorInfo Door;
            var dorrList = M2Share.MapManager.GetDoorMapList();
            for (var i = 0; i < dorrList.Count; i++)
            {
                var Envir = dorrList[i];
                for (var j = 0; j < Envir.m_DoorList.Count; j++)
                {
                    Door = Envir.m_DoorList[j];
                    if (Door.Status.boOpened)
                    {
                        if ((HUtil32.GetTickCount() - Door.Status.dwOpenTick) > 5 * 1000)
                        {
                            CloseDoor(Envir, Door);
                        }
                    }
                }
            }
        }

        private void ProcessEvents()
        {
            int count;
            MagicEvent MagicEvent;
            TBaseObject BaseObject;
            for (var i = m_MagicEventList.Count - 1; i >= 0; i--)
            {
                MagicEvent = m_MagicEventList[i];
                if (MagicEvent != null)
                {
                    for (var j = MagicEvent.BaseObjectList.Count - 1; j >= 0; j--)
                    {
                        BaseObject = MagicEvent.BaseObjectList[j];
                        if (BaseObject.m_boDeath || BaseObject.m_boGhost || !BaseObject.m_boHolySeize)
                            MagicEvent.BaseObjectList.RemoveAt(j);
                    }
                    if (MagicEvent.BaseObjectList.Count <= 0 || (HUtil32.GetTickCount() - MagicEvent.dwStartTick) > MagicEvent.dwTime ||
                        (HUtil32.GetTickCount() - MagicEvent.dwStartTick) > 180000)
                    {
                        count = 0;
                        while (true)
                        {
                            if (MagicEvent.Events[count] != null) MagicEvent.Events[count].Close();
                            count++;
                            if (count >= 8) break;
                        }
                        MagicEvent = null;
                        m_MagicEventList.RemoveAt(i);
                    }
                }
            }
        }

        public TMagic FindMagic(string sMagicName)
        {
            TMagic result = null;
            TMagic Magic = null;
            var definitions = Volatile.Read(ref _magicDefinitions).Human;
            for (var i = 0; i < definitions.Count; i++)
            {
                Magic = definitions[i];
                if (Magic.sMagicName.Equals(sMagicName, StringComparison.OrdinalIgnoreCase))
                {
                    result = Magic;
                    break;
                }
            }
            return result;
        }

        public TMagic FindHeroMagic(string sMagicName)
        {
            var definitions = Volatile.Read(ref _magicDefinitions).Hero;
            for (var i = 0; i < definitions.Count; i++)
            {
                var magic = definitions[i];
                if (magic.sMagicName.Equals(sMagicName,
                        StringComparison.OrdinalIgnoreCase))
                    return magic;
            }
            return null;
        }

        public bool NativeMagicDefinitionsPublished =>
            Volatile.Read(ref _nativeMagicDefinitionsPublished) != 0;

        public bool NativeMonsterDefinitionsPublished =>
            Volatile.Read(ref _nativeMonsterDefinitionsPublished) != 0;

        public bool NativeStdItemDefinitionsPublished =>
            Volatile.Read(ref _nativeStdItemDefinitionsPublished) != 0;

        public bool TryPublishNativeStdItemDefinitions(
            NativeType2StdItemStaticCatalog catalog, out string error)
        {
            error = string.Empty;
            if (catalog == null || !catalog.Ready || catalog.Count == 0)
            {
                error = "原生标准物品定义尚未完成";
                return false;
            }

            var definitions = catalog.CreateGoodItemList();
            if (definitions.Count == 0
                || definitions[0] == null
                || definitions[0].NativeWireIndex != 0
                || !string.Equals(definitions[0].Name, "金币",
                    StringComparison.Ordinal))
            {
                error = "原生标准物品 index-0 金币哨兵无效";
                return false;
            }

            lock (_stdItemDefinitionSync)
            {
                if (_nativeStdItemDefinitionsPublished != 0)
                {
                    error = "原生标准物品定义已发布，拒绝运行期整表替换";
                    return false;
                }

                Interlocked.Exchange(ref _stdItemDefinitions,
                    new StdItemDefinitionPublication(definitions, catalog));
                M2Share.g_boGameLogGold =
                    M2Share.GetGameLogItemNameList(
                        Grobal2.sSTRING_GOLDNAME) == 1;
                M2Share.g_boGameLogHumanDie =
                    M2Share.GetGameLogItemNameList(
                        M2Share.g_sHumanDieEvent) == 1;
                M2Share.g_boGameLogGameGold =
                    M2Share.GetGameLogItemNameList(
                        M2Share.g_Config.sGameGoldName) == 1;
                M2Share.g_boGameLogGamePoint =
                    M2Share.GetGameLogItemNameList(
                        M2Share.g_Config.sGamePointName) == 1;
                Volatile.Write(ref _nativeStdItemDefinitionsPublished, 1);
            }
            return true;
        }

        public bool TryPublishNativeMonsterDefinitions(
            NativeType2MonsterRuntimeCatalog catalog, out string error)
        {
            error = string.Empty;
            if (catalog == null || !catalog.Ready
                || catalog.Definitions.Count == 0)
            {
                error = "原生怪物定义尚未完成";
                return false;
            }
            if (M2Share.LocalDB == null)
            {
                error = "本地怪物爆率加载器尚未初始化";
                return false;
            }

            var source = catalog.CreateMonsterList();
            var definitions = new List<TMonInfo>(source.Length);
            var managerTables = NativeType2MonsterManagerTables
                .LoadFromDirectory(Path.Combine(M2Share.sRootPath,
                    M2Share.g_Config.sBaseDir, "Config"));
            try
            {
                for (var index = 0; index < source.Length; index++)
                {
                    var monster = source[index];
                    IList<TMonItem> items = null;
                    M2Share.LocalDB.LoadMonitems(monster.sName, ref items);
                    monster.ItemList = items;
                    definitions.Add(monster);
                }
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is UnauthorizedAccessException
                                       || ex is ArgumentException)
            {
                error = "加载原生怪物爆率失败: " + ex.Message;
                return false;
            }

            lock (_monsterDefinitionSync)
            {
                if (_nativeMonsterDefinitionsPublished != 0)
                {
                    error = "原生怪物定义已发布，拒绝运行期替换";
                    return false;
                }

                Interlocked.Exchange(ref _monsterDefinitions,
                    new MonsterDefinitionPublication(definitions, catalog,
                        managerTables));
                Volatile.Write(ref _nativeMonsterDefinitionsPublished, 1);
            }
            return true;
        }

        public bool TryPublishNativeMagicDefinitions(
            NativeType2MagicRuntimeCatalog catalog, out string error)
        {
            error = string.Empty;
            if (catalog == null || !catalog.Ready)
            {
                error = "原生人物/英雄技能双表尚未完成";
                return false;
            }

            var human = new List<TMagic>(catalog.CreateHumanMagicList());
            var hero = new List<TMagic>(catalog.CreateHeroMagicList());
            if (human.Count == 0 || hero.Count == 0)
            {
                error = "原生人物/英雄技能双表不能为空";
                return false;
            }

            lock (_magicDefinitionSync)
            {
                if (_nativeMagicDefinitionsPublished != 0)
                {
                    error = "原生人物/英雄技能双表已发布，拒绝运行期替换";
                    return false;
                }

                var previous = Volatile.Read(ref _magicDefinitions);
                if (previous.Human.Count > 0)
                    OldMagicList.Add(previous.Human);
                Interlocked.Exchange(ref _magicDefinitions,
                    new MagicDefinitionPublication(human, hero));
                Volatile.Write(ref _nativeMagicDefinitionsPublished, 1);
            }
            return true;
        }

        public int GetMapRangeMonster(Envirnoment Envir, int nX, int nY, int nRange, IList<TBaseObject> List)
        {
            var result = 0;
            if (Envir == null) return result;
            for (var i = 0; i < m_MonGenList.Count; i++)
            {
                var MonGen = m_MonGenList[i];
                if (MonGen == null) continue;
                if (MonGen.Envir != null && MonGen.Envir != Envir) continue;
                for (var j = 0; j < MonGen.CertList.Count; j++)
                {
                    var BaseObject = MonGen.CertList[j];
                    if (!BaseObject.m_boDeath && !BaseObject.m_boGhost && BaseObject.m_PEnvir == Envir &&
                        Math.Abs(BaseObject.m_nCurrX - nX) <= nRange && Math.Abs(BaseObject.m_nCurrY - nY) <= nRange)
                    {
                        if (List != null) List.Add(BaseObject);
                        result++;
                    }
                }
            }
            return result;
        }

        public void AddMerchant(Merchant Merchant)
        {
            TryAddMerchantExact(Merchant);
        }

        public int GetMerchantList(Envirnoment Envir, int nX, int nY, int nRange, IList<TBaseObject> TmpList)
        {
            Merchant Merchant;
            var merchants = SnapshotMerchants();
            for (var i = 0; i < merchants.Length; i++)
            {
                Merchant = merchants[i];
                if (Merchant.m_PEnvir == Envir && Math.Abs(Merchant.m_nCurrX - nX) <= nRange &&
                    Math.Abs(Merchant.m_nCurrY - nY) <= nRange) TmpList.Add(Merchant);
            }
            return TmpList.Count;
        }

        public int GetNpcList(Envirnoment Envir, int nX, int nY, int nRange, IList<TBaseObject> TmpList)
        {
            NormNpc Npc;
            var questNpcs = SnapshotQuestNpcs();
            for (var i = 0; i < questNpcs.Length; i++)
            {
                Npc = questNpcs[i];
                if (Npc.m_PEnvir == Envir && Math.Abs(Npc.m_nCurrX - nX) <= nRange &&
                    Math.Abs(Npc.m_nCurrY - nY) <= nRange) TmpList.Add(Npc);
            }
            return TmpList.Count;
        }

        public void ReloadMerchantList()
        {
            Merchant Merchant;
            var merchants = SnapshotMerchants();
            for (var i = 0; i < merchants.Length; i++)
            {
                Merchant = merchants[i];
                if (!Merchant.m_boGhost)
                {
                    Merchant.ClearScript();
                    Merchant.LoadNPCScript();
                }
            }
        }

        public void ReloadNpcList()
        {
            NormNpc Npc;
            var questNpcs = SnapshotReloadableQuestNpcs();
            for (var i = 0; i < questNpcs.Length; i++)
            {
                Npc = questNpcs[i];
                Npc.ClearScript();
                Npc.LoadNPCScript();
            }
        }

        public int GetMapMonster(Envirnoment Envir, IList<TBaseObject> List)
        {
            MonGenInfo MonGen;
            TBaseObject BaseObject;
            var result = 0;
            if (Envir == null) return result;
            for (var i = 0; i < m_MonGenList.Count; i++)
            {
                MonGen = m_MonGenList[i];
                if (MonGen == null) continue;
                for (var j = 0; j < MonGen.CertList.Count; j++)
                {
                    BaseObject = MonGen.CertList[j];
                    if (!BaseObject.m_boDeath && !BaseObject.m_boGhost && BaseObject.m_PEnvir == Envir)
                    {
                        if (List != null)
                            List.Add(BaseObject);
                        result++;
                    }
                }
            }
            return result;
        }

        public void HumanExpire(string sAccount)
        {
            TPlayObject PlayObject;
            if (!M2Share.g_Config.boKickExpireHuman) return;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (string.Compare(PlayObject.m_sUserID, sAccount, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    PlayObject.m_boExpire = true;
                    break;
                }
            }
        }

        public int GetMapHuman(string sMapName)
        {
            TPlayObject PlayObject;
            var result = 0;
            var Envir = M2Share.MapManager.FindMap(sMapName);
            if (Envir == null) return result;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (!PlayObject.m_boDeath && !PlayObject.m_boGhost && PlayObject.m_PEnvir == Envir) result++;
            }
            return result;
        }

        public int GetMapRageHuman(Envirnoment Envir, int nRageX, int nRageY, int nRage, IList<TBaseObject> List)
        {
            var result = 0;
            TPlayObject PlayObject;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (!PlayObject.m_boDeath && !PlayObject.m_boGhost && PlayObject.m_PEnvir == Envir &&
                    Math.Abs(PlayObject.m_nCurrX - nRageX) <= nRage && Math.Abs(PlayObject.m_nCurrY - nRageY) <= nRage)
                {
                    List.Add(PlayObject);
                    result++;
                }
            }
            return result;
        }

        public int GetStdItemIdx(string sItemName)
        {
            GoodItem StdItem;
            var result = -1;
            if (string.IsNullOrEmpty(sItemName)) return result;
            var items = StdItemList;
            var nativeIndices = HasNativeStdItemSentinel(items);
            for (var i = nativeIndices ? 1 : 0; i < items.Count; i++)
            {
                StdItem = items[i];
                if (StdItem == null) continue;
                if (StdItem.Name.Equals(sItemName, StringComparison.OrdinalIgnoreCase))
                {
                    result = nativeIndices ? i : i + 1;
                    break;
                }
            }
            return result;
        }

        private static bool HasNativeStdItemSentinel(
            IList<GoodItem> items)
        {
            return items != null && items.Count != 0
                   && items[0] != null
                   && items[0].NativeWireIndex == 0
                   && string.Equals(items[0].Name, "金币",
                       StringComparison.Ordinal);
        }

        
        
        
        public void SendBroadCastMsgExt(string sMsg, MsgType MsgType)
        {
            TPlayObject PlayObject;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (!PlayObject.m_boGhost)
                    PlayObject.SysMsg(sMsg, MsgColor.Red, MsgType);
            }
        }

        public void SendBroadCastMsg(string sMsg, MsgType MsgType)
        {
            TPlayObject PlayObject;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (!PlayObject.m_boGhost)
                {
                    PlayObject.SysMsg(sMsg, MsgColor.Red, MsgType);
                }
            }
        }

        public void sub_4AE514(TGoldChangeInfo GoldChangeInfo)
        {
            if (GoldChangeInfo == null) return;
            var GoldChange = GoldChangeInfo;
            HUtil32.EnterCriticalSection(m_LoadPlaySection);
            try
            {
                m_ChangeHumanDBGoldList.Add(GoldChange);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_LoadPlaySection);
            }
        }

        public void ClearMonSayMsg()
        {
            MonGenInfo MonGen;
            TBaseObject MonBaseObject;
            for (var i = 0; i < m_MonGenList.Count; i++)
            {
                MonGen = m_MonGenList[i];
                for (var j = 0; j < MonGen.CertList.Count; j++)
                {
                    MonBaseObject = MonGen.CertList[j];
                    MonBaseObject.m_SayMsgList = null;
                }
            }
        }

        private void PrcocessData()
        {
            try
            {
                while (M2Share.boStartReady && !_stopRequested)
                {
                    HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
                    try
                    {
                        ProcessUiActions();
                        NativePasYbPurchaseService.ProcessCompletions();
                        NativeYuanbaoManager.ProcessCompletions();
                        NativeAwardCodeService.Process(HUtil32.GetTickCount());
                        YbDbClient.Instance.ProcessCompletions();
                        ProcessHumans();
                        ProcessHeroes();
                        ProcessMonsters();
                        ProcessMerchants();
                        ProcessNpcs();
                        if ((HUtil32.GetTickCount() - dwProcessMissionsTime) > 1000)
                        {
                            dwProcessMissionsTime = HUtil32.GetTickCount();
                            ProcessMissions();
                            ProcessEvents();
                        }
                        if ((HUtil32.GetTickCount() - dwProcessMapDoorTick) > 500)
                        {
                            dwProcessMapDoorTick = HUtil32.GetTickCount();
                            ProcessMapDoor();
                        }
                    }
                    finally
                    {
                        HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
                    }
                    Thread.Sleep(20);
                }
            }
            catch (Exception e)
            {
                if (!_stopRequested)
                {
                    M2Share.ErrorMessage($"[Exception] TUserEngine::ProcessData {e}");
                }
            }
        }

        public string GetHomeInfo(ref short nX, ref short nY)
        {
            string result;
            if (M2Share.StartPointList.Count > 0)
            {
                int I;
                if (M2Share.StartPointList.Count > M2Share.g_Config.nStartPointSize)
                    I = M2Share.RandomNumber.Random(M2Share.g_Config.nStartPointSize);
                else
                    I = 0;
                result = M2Share.GetStartPointInfo(I, ref nX, ref nY);
            }
            else
            {
                result = M2Share.g_Config.sHomeMap;
                nX = M2Share.g_Config.nHomeX;
                nY = M2Share.g_Config.nHomeY;
            }
            return result;
        }

        public void StartAI()
        {
            if (_processAiThread.ThreadState == ThreadState.Unstarted)
            {
                _processAiThread.Start();
            }
        }

        public int RobotPopulation => m_AiPlayObjectList.Count + m_UserLogonList.Count;

        public void AddAILogon(TAILogon AI)
        {
            m_UserLogonList.Add(AI);
        }

        private bool RegenAIObject(TAILogon AI)
        {
            var PlayObject = AddAIPlayObject(AI);
            if (PlayObject != null)
            {
                PlayObject.m_sHomeMap = GetHomeInfo(ref PlayObject.m_nHomeX, ref PlayObject.m_nHomeY);
                PlayObject.m_sUserID = "假人";
                PlayObject.Start(TPathType.t_Dynamic);
                m_AiPlayObjectList.Add(PlayObject);
                return true;
            }
            return false;
        }

        private RobotPlayObject AddAIPlayObject(TAILogon AI)
        {
            int n1C;
            int n20;
            int n24;
            object p28;
            RobotPlayObject result = null;
            var Map = M2Share.MapManager.FindMap(AI.sMapName);
            if (Map == null)
            {
                return result;
            }
            RobotPlayObject Cert = new RobotPlayObject();
            if (Cert != null)
            {
                Cert.m_PEnvir = Map;
                Cert.m_sMapName = AI.sMapName;
                Cert.m_nCurrX = AI.nX;
                Cert.m_nCurrY = AI.nY;
                Cert.m_btDirection = (byte)M2Share.RandomNumber.Random(8);
                Cert.m_sCharName = AI.sCharName;
                // Bug1 fix 2026-04-22: deep copy instead of aliasing.
                Cert.m_WAbil.CopyFrom(Cert.m_Abil);
                if (M2Share.RandomNumber.Random(100) < Cert.m_btCoolEye)
                {
                    Cert.m_boCoolEye = true;
                }
                
                
                Cert.m_sConfigFileName = AI.sConfigFileName;
                Cert.m_sHeroConfigFileName = AI.sHeroConfigFileName;
                Cert.m_sFilePath = AI.sFilePath;
                Cert.m_sConfigListFileName = AI.sConfigListFileName;
                Cert.m_sHeroConfigListFileName = AI.sHeroConfigListFileName;
                
                Cert.Initialize();
                Cert.RecalcLevelAbilitys();
                Cert.RecalcAbilitys();
                Cert.m_WAbil.HP = Cert.m_WAbil.MaxHP;
                Cert.m_WAbil.MP = Cert.m_WAbil.MaxMP;
                if (Cert.m_boAddtoMapSuccess)
                {
                    p28 = null;
                    if (Cert.m_PEnvir.wWidth < 50)
                    {
                        n20 = 2;
                    }
                    else
                    {
                        n20 = 3;
                    }
                    if ((Cert.m_PEnvir.wHeight < 250))
                    {
                        if ((Cert.m_PEnvir.wHeight < 30))
                        {
                            n24 = 2;
                        }
                        else
                        {
                            n24 = 20;
                        }
                    }
                    else
                    {
                        n24 = 50;
                    }
                    n1C = 0;
                    while (true)
                    {
                        if (!Cert.m_PEnvir.CanWalk(Cert.m_nCurrX, Cert.m_nCurrY, false))
                        {
                            if ((Cert.m_PEnvir.wWidth - n24 - 1) > Cert.m_nCurrX)
                            {
                                Cert.m_nCurrX += (short)n20;
                            }
                            else
                            {
                                Cert.m_nCurrX = (byte)(M2Share.RandomNumber.Random(Cert.m_PEnvir.wWidth / 2) + n24);
                                if (Cert.m_PEnvir.wHeight - n24 - 1 > Cert.m_nCurrY)
                                {
                                    Cert.m_nCurrY += (short)n20;
                                }
                                else
                                {
                                    Cert.m_nCurrY = (byte)(M2Share.RandomNumber.Random(Cert.m_PEnvir.wHeight / 2) + n24);
                                }
                            }
                        }
                        else
                        {
                            p28 = Cert.m_PEnvir.AddToMap(Cert.m_nCurrX, Cert.m_nCurrY, CellType.OS_MOVINGOBJECT, Cert);
                            break;
                        }
                        n1C++;
                        if (n1C >= 31)
                        {
                            break;
                        }
                    }
                    if (p28 == null)
                    {
                        M2Share.ObjectManager.Remove(Cert.ObjectId);
                        Cert = null;
                    }
                }
            }
            result = Cert;
            return result;
        }

        public void SendQuestMsg(string sQuestName)
        {
            TPlayObject PlayObject;
            for (var i = 0; i < m_PlayObjectList.Count; i++)
            {
                PlayObject = m_PlayObjectList[i];
                if (!PlayObject.m_boDeath && !PlayObject.m_boGhost)
                    M2Share.PasEngine?.TryCallScriptLabel(sQuestName, "@main", PlayObject);
            }
        }

        public void ClearItemList()
        {
            StdItemList.Reverse();
            ClearMerchantData();
        }

        public void SwitchMagicList()
        {
            lock (_magicDefinitionSync)
            {
                var current = Volatile.Read(ref _magicDefinitions);
                if (current.Human.Count > 0)
                {
                    OldMagicList.Add(current.Human);
                    Interlocked.Exchange(ref _magicDefinitions,
                        new MagicDefinitionPublication(
                            new List<TMagic>(), current.Hero));
                }
            }
        }

        private void ClearMerchantData()
        {
            Merchant Merchant;
            var merchants = SnapshotMerchants();
            for (var i = 0; i < merchants.Length; i++)
            {
                Merchant = merchants[i];
                Merchant.ClearData();
            }
        }

        public void GuildMemberReGetRankName(Association guild)
        {
            var nRankNo = 0;
            for (int i = 0; i < m_PlayObjectList.Count; i++)
            {
                if (m_PlayObjectList[i].m_MyGuild == guild)
                {
                    guild.GetRankName(m_PlayObjectList[i], ref nRankNo);
                }
            }
        }
    }
}
