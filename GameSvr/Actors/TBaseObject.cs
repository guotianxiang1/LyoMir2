using System.Collections;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        public readonly int ObjectId;
        public string m_sMapName;
        public string m_sMapFileName;
        
        
        
        public string m_sCharName;
        
        
        
        public short m_nCurrX = 0;
        
        
        
        public short m_nCurrY = 0;
        
        
        
        public byte m_btDirection = 0;
        
        
        
        public PlayGender m_btGender = 0;
        
        
        
        public byte m_btHair = 0;
        
        
        
        public byte m_btJob = 0;
        
        
        
        public int m_nGold = 0;
        public TAbility m_Abil = null;
        
        
        
        public int m_nCharStatus = 0;
        public int m_nCharStatus2 = 0;
        public int m_nCharStatus3 = 0;
        public int m_nCharStatus4 = 0;
        
        
        
        public string m_sHomeMap;
        
        
        
        public short m_nHomeX = 0;
        
        
        
        public short m_nHomeY = 0;
        public bool m_boOnHorse = false;
        public byte m_btHorseType = 0;
        public byte m_btDressEffType = 0;
        
        
        
        public int m_nPkPoint = 0;
        
        
        
        public bool m_boAllowGroup = false;
        
        
        
        public bool m_boAllowGuild = false;
        public byte btB2 = 0;
        public int m_nIncHealth = 0;
        public int m_nIncSpell = 0;
        public int m_nIncHealing = 0;
        public int m_nIncHPStoneTime = 0;
        public int m_nIncMPStoneTime = 0;
        
        
        
        public int m_nFightZoneDieCount = 0;
        public int nC4 = 0;
        public byte btC8 = 0;
        public TNakedAbility m_BonusAbil = null;
        public TNakedAbility m_CurBonusAbil = null;
        public int m_nBonusPoint = 0;
        public int m_nHungerStatus = 0;
        public bool m_boAllowGuildReCall = false;
        public double m_dBodyLuck = 0;
        public int m_nBodyLuckLevel = 0;
        public short m_wGroupRcallTime = 0;
        public bool m_boAllowGroupReCall = false;
        public byte[] m_QuestUnitOpen;
        public byte[] m_QuestUnit;
        public byte[] m_QuestFlag;
        public long m_nCharStatusEx = 0;
        
        
        
        public int m_dwFightExp = 0;
        public TAbility m_WAbil = null;
        public TAddAbility m_AddAbil = null;
        
        
        
        public int m_nViewRange = 0;

        public ushort[] m_wStatusTimeArr = new ushort[12];
        public int[] m_dwStatusArrTick = new int[12];
        public ushort[] m_wStatusArrValue = null;
        public int[] m_dwStatusArrTimeOutTick = null;
        public ushort m_wAppr = 0;
        
        
        
        public byte m_btRaceServer = 0;
        
        
        
        public byte m_btRaceImg = 0;
        
        
        
        // Native actor +0x1E8 is a word. Keep the legacy member name because
        // it is referenced throughout the combat code.
        public ushort m_btHitPoint = 0;
        public ushort m_nHitPlus = 0;
        public ushort m_nHitDouble = 0;
        
        
        
        public int m_dwGroupRcallTick = 0;
        
        
        
        public bool m_boRecallSuite = false;
        public bool m_boRaceImg = false;
        public ushort m_nHealthRecover = 0;
        public ushort m_nSpellRecover = 0;
        public byte m_btAntiPoison = 0;
        public ushort m_wEffectResistance = 0;
        public ushort m_wEffectStrength = 0;
        public ushort m_nPoisonRecover = 0;
        public ushort m_nAntiMagic = 0;
        public ushort m_wNativeDrugHealthBonus = 0;
        public ushort m_wNativeDrugSpellBonus = 0;
        public ushort m_wNativeDrugJobBonus = 0;
        
        
        
        public int m_nLuck = 0;
        public sbyte m_sbHealthSpellRecoveryStep = 0;
        public int m_nPerHealing = 0;
        public int m_dwIncHealthSpellTick = 0;
        
        
        
        private byte m_btGreenPoisoningPoint = 0;
        
        
        
        public int m_nGoldMax = 0;
        
        
        
        public byte m_btSpeedPoint = 0;
        public ushort m_wSpeedPoint = 0;
        
        
        
        public byte m_btPermission = 0;
        
        
        
        protected ushort m_nHitSpeed = 0;
        public byte m_btLifeAttrib = 0;
        public byte m_btCoolEye = 0;
        public TBaseObject m_GroupOwner = null;
        
        
        
        public IList<TPlayObject> m_GroupMembers = null;
        
        
        
        public bool m_boHearWhisper = false;
        
        
        
        public bool m_boBanShout = false;
        
        
        
        public bool m_boBanGuildChat = false;
        
        
        
        public bool m_boAllowDeal = false;
        
        
        
        public IList<string> m_BlockWhisperList = null;
        public int m_dwShoutMsgTick = 0;
        
        
        
        public TBaseObject m_Master = null;
        
        
        
        public int m_dwMasterRoyaltyTick = 0;
        public int m_dwMasterTick = 0;
        
        
        
        public int m_nKillMonCount = 0;
        
        
        
        public byte m_btSlaveExpLevel = 0;
        
        
        
        public byte m_btSlaveMakeLevel = 0;
        
        
        
        public IList<TBaseObject> m_SlaveList = null;
        
        
        
        public bool m_boSlaveRelax = false;
        
        
        
        public byte m_btAttatckMode = 0;
        
        
        
        public byte m_btNameColor = 0;
        
        
        
        public int m_nLight = 0;
        
        
        
        private bool m_boGuildWarArea = false;
        
        
        
        public TUserCastle m_Castle = null;
        public bool bo2B0 = false;
        public int m_dw2B4Tick = 0;
        
        
        
        public bool m_boSuperMan = false;
        public bool bo2B9 = false;
        public bool bo2BA = false;
        public bool m_boAnimal = false;
        public bool m_boNoItem = false;
        public bool m_boFixedHideMode = false;
        public bool m_boStickMode = false;
        public bool bo2BF = false;
        public bool m_boNoAttackMode = false;
        public bool m_boNoTame = false;
        public bool m_boSkeleton = false;
        public ushort m_nMeatQuality = 0;
        public int m_nBodyLeathery = 0;
        public bool m_boHolySeize = false;
        public int m_dwHolySeizeTick = 0;
        public int m_dwHolySeizeInterval = 0;
        public bool m_boCrazyMode = false;
        public int m_dwCrazyModeTick = 0;
        public int m_dwCrazyModeInterval = 0;
        public bool m_boShowHP = false;
        
        
        
        public int m_dwShowHPTick = 0;
        
        
        
        public int m_dwShowHPInterval = 0;
        public bool bo2F0 = false;
        public int m_dwDupObjTick = 0;
        public Envirnoment m_PEnvir = null;
        public bool m_boGhost = false;
        public int m_dwGhostTick = 0;
        public bool m_boDeath = false;
        public int m_dwDeathTick = 0;
        public bool m_boInvisible = false;
        public bool m_boCanReAlive = false;
        
        
        
        public int m_dwReAliveTick = 0;
        public MonGenInfo m_pMonGen = null;

        
        
        
        public byte m_btMonsterWeapon = 0;
        public int m_dwStruckTick = 0;
        public bool m_boWantRefMsg = false;
        public bool m_boAddtoMapSuccess = false;
        public bool m_bo316 = false;
        public bool m_boDealing = false;
        
        
        
        public int m_DealLastTick = 0;
        public TBaseObject m_DealCreat = null;
        public Association m_MyGuild = null;
        public int m_nGuildRankNo = 0;
        public string m_sGuildRankName = string.Empty;
        public string m_sScriptLable = string.Empty;
        public byte m_btAttackSkillCount = 0;
        public byte m_btAttackSkillPointCount = 0;
        public bool m_boMission = false;
        public short m_nMissionX = 0;
        public short m_nMissionY = 0;
        
        
        
        public bool m_boHideMode = false;
        public bool m_boStoneMode = false;
        
        
        
        public bool m_boCoolEye = false;
        
        
        
        public bool m_boUserUnLockDurg = false;
        
        
        
        public bool m_boTransparent = false;
        
        
        
        public bool m_boAdminMode = false;
        
        
        
        public bool m_boObMode = false;
        
        
        
        public bool m_boTeleport = false;
        
        
        
        public bool m_boParalysis = false;
        public bool m_boUnParalysis = false;
        
        
        
        public bool m_boRevival = false;
        
        
        
        public bool m_boUnRevival = false;
        
        
        
        public int m_dwRevivalTick = 0;
        
        
        
        public bool m_boFlameRing = false;
        
        
        
        public bool m_boRecoveryRing = false;
        
        
        
        public bool m_boAngryRing = false;
        
        
        
        public bool m_boMagicShield = false;
        
        
        
        public bool m_boUnMagicShield = false;
        
        
        
        public bool m_boMuscleRing = false;
        
        
        
        public bool m_boFastTrain = false;
        
        
        
        public bool m_boProbeNecklace = false;
        
        
        
        public bool m_boGuildMove = false;
        public bool m_boSupermanItem = false;
        
        
        
        public bool m_bopirit = false;
        public bool m_boNoDropItem = false;
        public bool m_boNoDropUseItem = false;
        public bool m_boExpItem = false;
        public bool m_boPowerItem = false;
        public int m_rExpItem = 0;
        public int m_rPowerItem = 0;
        
        
        
        public int m_dwPKDieLostExp = 0;
        
        
        
        public int m_nPKDieLostLevel = 0;
        
        
        
        public bool m_boAbilSeeHealGauge = false;
        
        
        
        public bool m_boAbilMagBubbleDefence = false;
        public byte m_btMagBubbleDefenceLevel = 0;
        public int m_dwSearchTime = 0;
        public int m_dwSearchTick = 0;
        
        
        
        public int m_dwRunTick = 0;
        public int m_nRunTime = 0;
        public int m_nHealthTick = 0;
        public int m_nSpellTick = 0;
        public TBaseObject m_TargetCret = null;
        public int m_dwTargetFocusTick = 0;
        
        
        
        public TBaseObject m_LastHiter = null;
        public int m_LastHiterTick = 0;
        public TBaseObject m_ExpHitter = null;
        public int m_ExpHitterTick = 0;
        
        
        
        public int m_dwTeleportTick = 0;
        
        
        
        public int m_dwProbeTick = 0;
        public int m_dwMapMoveTick = 0;
        
        
        
        public bool m_boPKFlag = false;
        
        
        
        public int m_dwPKTick = 0;
        
        
        
        public int m_nMoXieSuite = 0;
        
        
        
        public int m_nHongMoSuite = 0;
        public double m_db3B0 = 0;
        
        
        
        public int m_dwPoisoningTick = 0;
        
        
        
        public int m_dwDecPkPointTick = 0;
        public int m_DecLightItemDrugTick = 0;
        public int m_dwVerifyTick = 0;
        public int m_dwCheckRoyaltyTick = 0;
        public int m_dwDecHungerPointTick = 0;
        public int m_dwHPMPTick = 0;
        public IList<SendMessage> m_MsgList = null;
        public IList<TBaseObject> m_VisibleHumanList = null;
        public IList<VisibleMapItem> m_VisibleItems = null;
        public IList<Event> m_VisibleEvents = null;
        public int m_SendRefMsgTick = 0;
        
        
        
        public bool m_boInFreePKArea = false;
        public int m_dwHitTick = 0;
        public int m_dwWalkTick = 0;
        public int m_dwSearchEnemyTick = 0;
        public bool m_boNameColorChanged = false;
        
        
        
        public bool m_boIsVisibleActive = false;
        
        
        
        public short m_nProcessRunCount = 0;
        
        
        
        public IList<TVisibleBaseObject> m_VisibleActors = null;
        
        
        
        public IList<TUserItem> m_ItemList = null;
        
        
        
        public IList<TUserItem> m_DealItemList = null;
        
        
        
        public int m_nDealGolds = 0;
        
        
        
        public bool m_boDealOK = false;
        
        
        
        public IList<TUserMagic> m_MagicList = null;
        public TUserItem[] m_UseItems;
        public IList<TMonSayMsg> m_SayMsgList = null;
        public IList<TUserItem> m_StorageItemList = null;
        
        
        
        public int m_nWalkSpeed = 0;
        public int m_nWalkStep = 0;
        public int m_nWalkCount = 0;
        internal int m_nNativeForcedMoveRemaining = 0;
        public int m_dwWalkWait = 0;
        public int m_dwWalkWaitTick = 0;
        public bool m_boWalkWaitLocked = false;
        public int m_nNextHitTime = 0;
        public TUserMagic[] m_MagicArr = null;
        internal byte NativeMagicLevelBonus;
        public bool m_boPowerHit = false;
        public bool m_boUseThrusting = false;
        public bool m_boUseHalfMoon = false;
        public bool m_boRedUseHalfMoon = false;
        public bool m_boFireHitSkill = false;
        public bool m_boCrsHitkill = false;
        public bool m_bo41kill = false;
        public bool m_boTwinHitSkill = false;
        public bool m_bo43kill = false;
        public bool m_boSunSwordReady = false;
        public int m_dwLatestFireHitTick = 0;
        public int m_dwDoMotaeboTick = 0;
        public int m_dwLatestTwinHitTick = 0;
        public int m_dwLatestSunSwordTick = 0;
        public bool m_boDenyRefStatus = false;
        
        public bool m_boAddToMaped = false;
        
        public bool m_boDelFormMaped = false;

        internal virtual bool CountsAsPlayerPresence =>
            m_btRaceServer == Grobal2.RC_PLAYOBJECT;

        /// <summary>
        /// Native sub_76B354 base can-act predicate: 6-gate ladder.
        /// MOVE-14: Gate 5 takes caller argument for bodyState 0x18 selective block.
        /// </summary>
        internal virtual bool IsNativeCanActBlocked(int callerArg)
        {
            if (m_boDeath) return true;
            if (HasNativeActiveState(0x1D)) return true;
            if (HasNativeActiveState(0x01)) return true;
            if (HasNativeActiveState(0x1A)) return true;
            if (HasNativeActiveState(0x18) && callerArg != 0) return true; // MOVE-14
            if (HasNativeActiveState(0x3E)) return true;
            return false;
        }

        public bool m_boAutoChangeColor = false;
        public int m_dwAutoChangeColorTick = 0;
        public int m_nAutoChangeIdx = 0;
        
        
        
        public bool m_boFixColor = false;
        public int m_nFixColorIdx = 0;
        public int m_nFixStatus = 0;
        
        
        
        public bool m_boFastParalysis = false;
        public bool m_boSmashSet = false;
        public bool m_boHwanDevilSet = false;
        public bool m_boPuritySet = false;
        public bool m_boMundaneSet = false;
        public bool m_boNokChiSet = false;
        public bool m_boTaoBuSet = false;
        public bool m_boFiveStringSet = false;
        public bool m_boOffLineFlag = false;
        
        public string m_sOffLineLeaveword = string.Empty;
        
        public int m_dwKickOffLineTick = 0;
        public bool m_boNastyMode = false;
        
        
        
        public int m_nAutoAddHPMPMode = 0;
        public int m_dwCheckHPMPTick = 0;
        public long dwTick3F4 = 0;
        
        
        
        public bool m_boAI;

        public TBaseObject()
        {
            ObjectId = HUtil32.Sequence();
            m_boGhost = false;
            m_dwGhostTick = 0;
            m_boDeath = false;
            m_dwDeathTick = 0;
            m_SendRefMsgTick = HUtil32.GetTickCount();
            m_btDirection = 4;
            m_btRaceServer = Grobal2.RC_ANIMAL;
            m_btRaceImg = 0;
            m_btHair = 0;
            m_btJob = M2Share.jWarr;
            m_nGold = 0;
            m_wAppr = 0;
            bo2B9 = true;
            m_nViewRange = 5;
            m_sHomeMap = "0";
            m_btPermission = 0;
            m_nLight = 0;
            m_btNameColor = 255;
            m_nHitPlus = 0;
            m_nHitDouble = 0;
            m_dBodyLuck = 0;
            m_nBodyLuckLevel = 0;
            m_wGroupRcallTime = 0;
            m_dwGroupRcallTick = HUtil32.GetTickCount();
            m_boRecallSuite = false;
            m_boRaceImg = false;
            bo2BA = false;
            m_boAbilSeeHealGauge = false;
            m_boPowerHit = false;
            m_boUseThrusting = false;
            m_boUseHalfMoon = false;
            m_boRedUseHalfMoon = false;
            m_boFireHitSkill = false;
            m_boTwinHitSkill = false;
            m_boSunSwordReady = false;
            m_btHitPoint = 5;
            m_btSpeedPoint = 15;
            m_wSpeedPoint = 15;
            m_nHitSpeed = 0;
            m_btLifeAttrib = 0;
            m_btAntiPoison = 0;
            m_nPoisonRecover = 0;
            m_nHealthRecover = 0;
            m_nSpellRecover = 0;
            m_nAntiMagic = 0;
            m_nLuck = 0;
            m_nIncSpell = 0;
            m_nIncHealth = 0;
            m_nIncHealing = 0;
            m_nIncHPStoneTime = HUtil32.GetTickCount();
            m_nIncMPStoneTime = HUtil32.GetTickCount();
            m_sbHealthSpellRecoveryStep = 5;
            m_nPerHealing = 5;
            m_dwIncHealthSpellTick = HUtil32.GetTickCount();
            m_btGreenPoisoningPoint = 0;
            m_btRedPoisoningLevel = 0;
            m_nFightZoneDieCount = 0;
            m_nGoldMax = M2Share.g_Config.nHumanMaxGold;
            m_nCharStatus = 0;
            m_nCharStatusEx = 0;
            m_wStatusTimeArr = new ushort[12];// FillChar(m_wStatusTimeArr, sizeof(grobal2.short), '\0');
            m_BonusAbil = new TNakedAbility();// FillChar(m_BonusAbil, sizeof(TNakedAbility), '\0');
            m_CurBonusAbil = new TNakedAbility();// FillChar(m_CurBonusAbil, sizeof(TNakedAbility), '\0');
            m_wStatusArrValue = new ushort[6];// FillChar(m_wStatusArrValue, sizeof(m_wStatusArrValue), 0);
            m_dwStatusArrTimeOutTick = new int[6];// FillChar(m_dwStatusArrTimeOutTick, sizeof(m_dwStatusArrTimeOutTick), '\0');
            m_boAllowGroup = false;
            m_boAllowGuild = false;
            btB2 = 0;
            m_btAttatckMode = 0;
            m_boInFreePKArea = false;
            m_boGuildWarArea = false;
            bo2B0 = false;
            m_boSuperMan = false;
            m_boSkeleton = false;
            bo2BF = false;
            m_boHolySeize = false;
            m_boCrazyMode = false;
            m_boShowHP = false;
            bo2F0 = false;
            m_boAnimal = false;
            m_boNoItem = false;
            m_nBodyLeathery = 50;
            m_boFixedHideMode = false;
            m_boStickMode = false;
            m_boNoAttackMode = false;
            m_boNoTame = false;
            m_boPKFlag = false;
            m_nMoXieSuite = 0;
            m_nHongMoSuite = 0;
            m_db3B0 = 0;
            m_AddAbil = new TAddAbility();//FillChar(m_AddAbil, sizeof(TAddAbility), '\0');
            m_MsgList = new List<SendMessage>();
            m_VisibleHumanList = new List<TBaseObject>();
            m_VisibleActors = new List<TVisibleBaseObject>();
            m_VisibleItems = new List<VisibleMapItem>();
            m_VisibleEvents = new List<Event>();
            m_ItemList = new List<TUserItem>();
            m_DealItemList = new List<TUserItem>();
            m_boIsVisibleActive = false;
            m_nProcessRunCount = 0;
            m_nDealGolds = 0;
            m_MagicList = new List<TUserMagic>();
            m_StorageItemList = new List<TUserItem>();
            
            m_UseItems = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
            m_GroupOwner = null;
            m_Castle = null;
            m_Master = null;
            m_nKillMonCount = 0;
            m_btSlaveExpLevel = 0;
            m_GroupMembers = new List<TPlayObject>();
            m_boHearWhisper = true;
            m_boBanShout = true;
            m_boBanGuildChat = true;
            m_boAllowDeal = true;
            m_boAllowGroupReCall = false;
            m_BlockWhisperList = new List<string>();
            m_SlaveList = new List<TBaseObject>();
            m_WAbil = new TAbility();// FillChar(m_WAbil, sizeof(TAbility), '\0');
            m_QuestUnitOpen = new byte[128];//FillChar(m_QuestUnitOpen, sizeof(grobal2.byte), '\0');
            m_QuestUnit = new byte[128];// FillChar(m_QuestUnit, sizeof(grobal2.byte), '\0');
            m_QuestFlag = new byte[128];
            m_Abil = new TAbility
            {
                Level = 1,
                AC = 0,
                MAC = 0,
                DC = HUtil32.MakeLong(1, 4),
                MC = HUtil32.MakeLong(1, 2),
                SC = HUtil32.MakeLong(1, 2),
                HP = 15,
                MP = 15,
                MaxHP = 15,
                MaxMP = 15,
                Exp = 0,
                MaxExp = 50,
                Weight = 0,
                MaxWeight = 100
            };
            m_boWantRefMsg = false;
            m_boDealing = false;
            m_DealCreat = null;
            m_MyGuild = null;
            m_nGuildRankNo = 0;
            m_sGuildRankName = "";
            m_sScriptLable = "";
            m_boMission = false;
            m_boHideMode = false;
            m_boStoneMode = false;
            m_boCoolEye = false;
            m_boUserUnLockDurg = false;
            m_boTransparent = false;
            m_boAdminMode = false;
            m_boObMode = false;
            m_dwRunTick = HUtil32.GetTickCount() + M2Share.RandomNumber.Random(1500);
            m_nRunTime = 250;
            m_dwSearchTime = M2Share.RandomNumber.Random(2000) + 2000;
            m_dwSearchTick = HUtil32.GetTickCount();
            m_dwDecPkPointTick = HUtil32.GetTickCount();
            m_DecLightItemDrugTick = HUtil32.GetTickCount();
            m_dwPoisoningTick = HUtil32.GetTickCount();
            m_dwVerifyTick = HUtil32.GetTickCount();
            m_dwCheckRoyaltyTick = HUtil32.GetTickCount();
            m_dwDecHungerPointTick = HUtil32.GetTickCount();
            m_dwHPMPTick = HUtil32.GetTickCount();
            m_dwShoutMsgTick = 0;
            m_dwTeleportTick = 0;
            m_dwProbeTick = 0;
            m_dwMapMoveTick = HUtil32.GetTickCount();
            m_dwMasterTick = 0;
            m_nWalkSpeed = 1400;
            m_nNextHitTime = 2000;
            m_nWalkCount = 0;
            m_dwWalkWaitTick = HUtil32.GetTickCount();
            m_boWalkWaitLocked = false;
            m_nHealthTick = 0;
            m_nSpellTick = 0;
            m_TargetCret = null;
            m_LastHiter = null;
            m_ExpHitter = null;
            m_SayMsgList = null;
            m_boDenyRefStatus = false;
            m_btHorseType = 0;
            m_btDressEffType = 0;
            m_dwPKDieLostExp = 0;
            m_nPKDieLostLevel = 0;
            m_boAddToMaped = true;
            m_boAutoChangeColor = false;
            m_dwAutoChangeColorTick = HUtil32.GetTickCount();
            m_nAutoChangeIdx = 0;
            m_boFixColor = false;
            m_nFixColorIdx = 0;
            m_nFixStatus = -1;
            m_boFastParalysis = false;
            m_boNastyMode = false;
            m_MagicArr = new TUserMagic[116];
            M2Share.ObjectManager.RegisterConstructed(this);
        }

        public void ChangePKStatus(bool boWarFlag)
        {
            if (m_boInFreePKArea != boWarFlag)
            {
                m_boInFreePKArea = boWarFlag;
                m_boNameColorChanged = true;
            }
        }

        public bool GetDropPosition(int nOrgX, int nOrgY, int nRange, ref int nDX, ref int nDY)
        {
            var result = false;
            var nItemCount = 0;
            var n24 = 999;
            var n28 = 0;
            var n2C = 0;
            for (var i = 0; i <= nRange; i++)
            {
                for (var ii = -i; ii <= i; ii++)
                {
                    for (var iii = -i; iii <= i; iii++)
                    {
                        nDX = nOrgX + iii;
                        nDY = nOrgY + ii;
                        if (m_PEnvir.GetItemEx(nDX, nDY, ref nItemCount) == null)
                        {
                            if (m_PEnvir.bo2C)
                            {
                                result = true;
                                break;
                            }
                        }
                        else
                        {
                            if (m_PEnvir.bo2C && n24 > nItemCount)
                            {
                                n24 = nItemCount;
                                n28 = nDX;
                                n2C = nDY;
                            }
                        }
                    }
                    if (result)
                    {
                        break;
                    }
                }
                if (result)
                {
                    break;
                }
            }
            if (!result)
            {
                if (n24 < 8)
                {
                    nDX = n28;
                    nDY = n2C;
                }
                else
                {
                    nDX = nOrgX;
                    nDY = nOrgY;
                }
            }
            return result;
        }

        public bool DropItemDown(TUserItem UserItem, int nScatterRange, bool boDieDrop, TBaseObject ItemOfCreat, TBaseObject DropCreat)
        {
            bool result = false;
            int dx = 0;
            int dy = 0;
            int idura;
            MapItem MapItem;
            MapItem pr;
            string logcap;
            if (UserItem == null)
            {
                return false;
            }
            GoodItem StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
            if (StdItem != null)
            {
                if (StdItem.StdMode == 40)
                {
                    idura = UserItem.Dura;
                    idura = idura - 2000;
                    if (idura < 0)
                    {
                        idura = 0;
                    }
                    UserItem.Dura = (ushort)idura;
                }
                MapItem = new MapItem
                {
                    UserItem = UserItem,
                    Name = ItmUnit.GetItemName(UserItem),// 取自定义物品名称
                    Looks = StdItem.Looks
                };
                if (StdItem.StdMode == 45)
                {
                    MapItem.Looks = (ushort)M2Share.GetRandomLook(MapItem.Looks, StdItem.Shape);
                }
                MapItem.AniCount = unchecked((byte)StdItem.AniCount);
                MapItem.Reserved = 0;
                MapItem.Count = 1;
                MapItem.OfBaseObject = ItemOfCreat;
                MapItem.CanPickUpTick = HUtil32.GetTickCount();
                MapItem.DropBaseObject = DropCreat;
                GetDropPosition(m_nCurrX, m_nCurrY, nScatterRange, ref dx, ref dy);
                pr = (MapItem)m_PEnvir.AddToMap(dx, dy, CellType.OS_ITEMOBJECT, MapItem);
                if (pr == MapItem)
                {
                    SendRefMsg(Grobal2.RM_ITEMSHOW, MapItem.Looks, MapItem.Id, dx, dy, MapItem.Name);
                    if (boDieDrop)
                    {
                        logcap = "15";
                    }
                    else
                    {
                        logcap = "7";
                    }
                    if (!M2Share.IsCheapStuff(StdItem.StdMode))
                    {
                        if (StdItem.NeedIdentify == 1)
                        {
                            M2Share.AddGameDataLog(logcap + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + UserItem.MakeIndex + "\t" + HUtil32.BoolToIntStr(m_btRaceServer == Grobal2.RC_PLAYOBJECT) + "\t" + '0');
                        }
                    }
                    result = true;
                }
                else
                {
                    MapItem = null;
                }
            }
            return result;
        }

        public void GoldChanged()
        {
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                SendUpdateMsg(this, Grobal2.RM_GOLDCHANGED, 0, 0, 0, 0, "");
            }
        }

        public void GameGoldChanged()
        {
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                SendUpdateMsg(this, Grobal2.RM_GAMEGOLDCHANGED, 0, 0, 0, 0, "");
            }
        }

        public void RecalcLevelAbilitys()
        {
            int n;
            double nLevel = m_Abil.Level;
            switch (m_btJob)
            {
                case 3:
                    m_Abil.MaxHP = Math.Max(0, 14 + HUtil32.Round((nLevel / 5 + 3.75) * nLevel));
                    m_Abil.MaxMP = m_Abil.MaxHP;
                    m_Abil.MaxWeight = (ushort)Math.Min(0xFFDC,
                        50 + HUtil32.Round(nLevel / 3 * nLevel));
                    m_Abil.MaxWearWeight = (ushort)Math.Min(0xFFDC,
                        15 + HUtil32.Round(nLevel / 20 * nLevel));
                    m_Abil.MaxHandWeight = (ushort)Math.Min(0xFFDC,
                        12 + HUtil32.Round(nLevel / 13 * nLevel));
                    m_Abil.DC = 0;
                    m_Abil.MC = 0;
                    m_Abil.SC = 0;
                    m_Abil.AC = HUtil32.MakeLong(0, nLevel / 7);
                    m_Abil.MAC = HUtil32.MakeLong(0, nLevel / 7);
                    break;
                case M2Share.jTaos:
                    m_Abil.MaxHP = Math.Max(0, 14 + HUtil32.Round((nLevel / M2Share.g_Config.nLevelValueOfTaosHP + M2Share.g_Config.nLevelValueOfTaosHPRate) * nLevel));
                    m_Abil.MaxMP = Math.Max(0, 13 + HUtil32.Round(nLevel / M2Share.g_Config.nLevelValueOfTaosMP * 2.2 * nLevel));
                    m_Abil.MaxWeight = (ushort)(50 + HUtil32.Round(nLevel / 4 * nLevel));
                    // 0x6BA4E3 fild / 0x6BA4E6 fdiv dword [0x6BA7B0]=50.0 / fild /
                    // fmulp / 0x6BA4F7 call @ROUND / 0x6BA4FC add eax,0x0F. The
                    // quotient stays in an 80-bit register, so a double chain
                    // double-rounds and diverges at levels 55/415/805/855/905.
                    m_Abil.MaxWearWeight = (ushort)(15
                        + HUtil32.RoundDivMulExtended(m_Abil.Level, 50));
                    m_Abil.MaxHandWeight = (ushort)(12 + HUtil32.Round(nLevel / 42 * nLevel));
                    n = (int)(nLevel / 7);
                    m_Abil.DC = HUtil32.MakeLong(HUtil32._MAX(n - 1, 0), HUtil32._MAX(1, n));
                    m_Abil.MC = 0;
                    m_Abil.SC = HUtil32.MakeLong(HUtil32._MAX(n - 1, 0), HUtil32._MAX(1, n));
                    m_Abil.AC = 0;
                    n = HUtil32.Round(nLevel / 6);
                    m_Abil.MAC = HUtil32.MakeLong(n / 2, n + 1);
                    break;
                case M2Share.jWizard:
                    m_Abil.MaxHP = Math.Max(0, 14 + HUtil32.Round((nLevel / M2Share.g_Config.nLevelValueOfWizardHP + M2Share.g_Config.nLevelValueOfWizardHPRate) * nLevel));
                    m_Abil.MaxMP = Math.Max(0, 13 + HUtil32.Round((nLevel / 5 + 2) * 2.2 * nLevel));
                    m_Abil.MaxWeight = (ushort)(50 + HUtil32.Round(nLevel / 5 * nLevel));
                    m_Abil.MaxWearWeight = (ushort)HUtil32._MIN(short.MaxValue, 15 + HUtil32.Round(nLevel / 100 * nLevel));
                    // 0x6BA3B5 fild / 0x6BA3B8 fdiv dword [0x6BA7A0]=90.0 / fild /
                    // fmulp / 0x6BA3C9 call @ROUND / 0x6BA3CE add eax,0x0C. Same
                    // extended-precision chain; diverges at levels 105 and 795.
                    m_Abil.MaxHandWeight = (ushort)(12
                        + HUtil32.RoundDivMulExtended(m_Abil.Level, 90));
                    n = (int)(nLevel / 7);
                    m_Abil.DC = HUtil32.MakeLong(HUtil32._MAX(n - 1, 0), HUtil32._MAX(1, n));
                    m_Abil.MC = HUtil32.MakeLong(HUtil32._MAX(n - 1, 0), HUtil32._MAX(1, n));
                    m_Abil.SC = 0;
                    m_Abil.AC = 0;
                    m_Abil.MAC = 0;
                    break;
                case M2Share.jWarr:
                    m_Abil.MaxHP = Math.Max(0, 14 + HUtil32.Round((nLevel / M2Share.g_Config.nLevelValueOfWarrHP + M2Share.g_Config.nLevelValueOfWarrHPRate + nLevel / 20) * nLevel));
                    m_Abil.MaxMP = Math.Max(0, 11 + HUtil32.Round(nLevel * 3.5));
                    m_Abil.MaxWeight = (ushort)(50 + HUtil32.Round(nLevel / 3 * nLevel));
                    m_Abil.MaxWearWeight = (ushort)(15 + HUtil32.Round(nLevel / 20 * nLevel));
                    m_Abil.MaxHandWeight = (ushort)(12 + HUtil32.Round(nLevel / 13 * nLevel));
                    m_Abil.DC = HUtil32.MakeLong(HUtil32._MAX((int)(nLevel / 5) - 1, 1), HUtil32._MAX(1, (int)(nLevel / 5)));
                    m_Abil.SC = 0;
                    m_Abil.MC = 0;
                    m_Abil.AC = HUtil32.MakeLong(0, nLevel / 7);
                    m_Abil.MAC = 0;
                    break;
            }
            if (m_Abil.HP > m_Abil.MaxHP)
            {
                m_Abil.HP = m_Abil.MaxHP;
            }
            if (m_Abil.MP > m_Abil.MaxMP)
            {
                m_Abil.MP = m_Abil.MaxMP;
            }
        }

        public void HasLevelUp(int nLevel)
        {
            m_Abil.MaxExp = GetLevelExp(m_Abil.Level);
            RecalcLevelAbilitys();
            RecalcAbilitys();
            SendMsg(this, Grobal2.RM_LEVELUP, 0, m_Abil.Exp, 0, 0, "");
#if FOR_ABIL_POINT
            
            if (prevlevel + 1 == Abil.Level)
            {
                BonusPoint = BonusPoint + GetBonusPoint(Job, Abil.Level);
                SendMsg(this, grobal2.RM_ADJUST_BONUS, 0, 0, 0, 0, "");
            }
            else
            {
                if (prevlevel != Abil.Level)
                {
                    
                    BonusPoint = GetLevelBonusSum(Job, Abil.Level);
                    FillChar(BonusAbil, sizeof(TNakedAbility), '\0');
                    FillChar(CurBonusAbil, sizeof(TNakedAbility), '\0');
                    RecalcLevelAbilitys();
                    SendMsg(this, grobal2.RM_ADJUST_BONUS, 0, 0, 0, 0, "");
                }
            }
#endif
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(this as TPlayObject, "@LevelUp", false);
            }
            if (M2Share.g_PsFunctionNPC != null
                && M2Share.g_PsFunctionNPC != M2Share.g_FunctionNPC)
            {
                M2Share.g_PsFunctionNPC.GotoLable(this as TPlayObject, "@LevelUp", false);
            }
        }

        // ── 原版的 mover 是"按类型分"的三个 VMT+0x30 槽（MOVE-40，Tier-1 VMT 普查）：
        //     TCreature / TPsNpc                             -> 0x767568（xor eax,eax; ret，永不移动）
        //     TAnimal / TMonster / TAIMon / 卫士 / TFieldHero -> 0x71F0F4
        //     THumanKind / TPlayer / THeroAct                 -> 0x741224
        //   三者在"入口闸 / 方向校验 / 边界"三处各不相同，严禁合成一套边界
        //   （MOVE-38 / MOVE-41 / MOVE-42：四行是同一个表达式，必须一起修）。
        //   C# 的继承链是 TBaseObject -> AnimalObject -> {各怪物, TPlayObject, HeroObject}，
        //   人形类挂在 AnimalObject 之下，所以人形边界必须在 TPlayObject / HeroObject
        //   各自 override 回来，光靠本类的基实现会被 AnimalObject 的 override 截断。
        //
        // 本类对应 TCreature/TPsNpc 那一槽 0x767568，其整个函数体只有两条指令：
        //   767568  33c0    xor eax, eax
        //   76756A  c3      ret
        // 即"永不移动"，故基实现一律返回 false，而不是复制人形边界。
        // 与之相符：C# 里 NormNpc / SuperGuard / GuardUnit 及其子类没有任何
        // WalkTo 或 GotoTargetXY 调用点（两种写法各扫一遍均零命中），
        // 所以这一槽在 C# 中本就走不到；此处把它写成 false 是让"走不到"变成
        // "即使走到也不动"，与 0x767568 逐字节一致。
        protected virtual bool WalkToInBounds(short nNX, short nNY)
        {
            return false;
        }

        // 0x74123E  sub eax,8 / jb fail —— 人形 mover 校验方向 0..7。
        // 怪物 mover 用无符号的 0x71F115 sub eax,8 / jae fail，对 byte 入参等价，
        // 故两侧共用本实现，不另 override。
        // C# 原先完全没有这道校验：越界方向会穿过 switch 留下 nNX=nNY=0，
        // 再配上怪物侧放宽后的 >= 0 就会把对象瞬移到 (0,0)（MOVE-37 备注）。
        protected virtual bool WalkToDirectionIsValid(byte btDir)
        {
            return btDir <= Grobal2.DR_UPLEFT;
        }

        // 怪物 mover 的入口闸 0x71F106 cmp byte [ebx+0x480],0 / jne fail，
        // +0x480 即 m_boHolySeize（BreakHolySeizeMode 0x71E9EB 读、0x71E9F4 写同一字节），
        // 所以怪物侧沿用本实现即可，不另 override。
        // MOVE-41 说人形 mover 缺这道闸，但 0x741224 的序言（0x741224..0x74123E）未被 dump 覆盖，
        // 无法逐字节证明它没有，故此处保留现状：不凭"未见到的字节"去删玩家的定身闸。
        protected virtual bool WalkToEntryGateBlocks()
        {
            return m_boHolySeize || HasTimedAbility(13);
        }

        public bool WalkTo(byte btDir, bool boFlag)
        {
            short nOX = 0;
            short nOY = 0;
            short nNX = 0;
            short nNY = 0;
            short n20 = 0;
            short n24 = 0;
            bool bo29;
            const string sExceptionMsg = "[Exception] TBaseObject::WalkTo";
            bool result = false;
            if (WalkToEntryGateBlocks())
            {
                return result;
            }
            // 方向校验在 Dir 落盘之前：原版 0x74123E 的失败路径不会走到 0x74124A。
            if (!WalkToDirectionIsValid(btDir))
            {
                return result;
            }
            try
            {
                nOX = m_nCurrX;
                nOY = m_nCurrY;
                m_btDirection = btDir;
                nNX = 0;
                nNY = 0;
                switch (btDir)
                {
                    case Grobal2.DR_UP:
                        nNX = m_nCurrX;
                        nNY = (short)(m_nCurrY - 1);
                        break;
                    case Grobal2.DR_UPRIGHT:
                        nNX = (short)(m_nCurrX + 1);
                        nNY = (short)(m_nCurrY - 1);
                        break;
                    case Grobal2.DR_RIGHT:
                        nNX = (short)(m_nCurrX + 1);
                        nNY = m_nCurrY;
                        break;
                    case Grobal2.DR_DOWNRIGHT:
                        nNX = (short)(m_nCurrX + 1);
                        nNY = (short)(m_nCurrY + 1);
                        break;
                    case Grobal2.DR_DOWN:
                        nNX = m_nCurrX;
                        nNY = (short)(m_nCurrY + 1);
                        break;
                    case Grobal2.DR_DOWNLEFT:
                        nNX = (short)(m_nCurrX - 1);
                        nNY = (short)(m_nCurrY + 1);
                        break;
                    case Grobal2.DR_LEFT:
                        nNX = (short)(m_nCurrX - 1);
                        nNY = m_nCurrY;
                        break;
                    case Grobal2.DR_UPLEFT:
                        nNX = (short)(m_nCurrX - 1);
                        nNY = (short)(m_nCurrY - 1);
                        break;
                }
                // 边界按类型分派：人形用本类的 > 0 / < Width，怪物在 AnimalObject 里 override 成
                // >= 0 / <= Width（MOVE-38 / MOVE-42）。原先这里是一条共用的 >= 0 && <= Width-1，
                // 两侧都不匹配：它放玩家进第 0 列（原版拒绝），又挡住怪物碰 Width-1。
                if (WalkToInBounds(nNX, nNY))
                {
                    bo29 = true;
                    if (bo2BA && !m_PEnvir.CanSafeWalk(nNX, nNY))
                    {
                        bo29 = false;
                    }
                    if (m_Master != null)
                    {
                        m_Master.m_PEnvir.GetNextPosition(m_Master.m_nCurrX, m_Master.m_nCurrY, m_Master.m_btDirection, 1, ref n20, ref n24);
                        if (nNX == n20 && nNY == n24)
                        {
                            bo29 = false;
                        }
                    }
                    if (bo29)
                    {
                        if (m_PEnvir.MoveToMovingObject(m_nCurrX, m_nCurrY, this, nNX, nNY, boFlag) > 0)
                        {
                            m_nCurrX = nNX;
                            m_nCurrY = nNY;
                        }
                    }
                }
                if (m_nCurrX != nOX || m_nCurrY != nOY)
                {
                    if (Walk(Grobal2.RM_WALK))
                    {
                        if (m_boTransparent && m_boHideMode)
                        {
                            m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 1;
                        }
                        if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            m_dwSearchTick = 0;
                        }
                        result = true;
                    }
                    else
                    {
                        m_PEnvir.DeleteFromMap(m_nCurrX, m_nCurrY,
                            CellType.OS_MOVINGOBJECT, this, false,
                            suppressMapDropConsumer: true);
                        m_nCurrX = nOX;
                        m_nCurrY = nOY;
                        m_PEnvir.AddToMap(m_nCurrX, m_nCurrY, CellType.OS_MOVINGOBJECT, this);
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
            return result;
        }

        public bool IsGroupMember(TBaseObject target)
        {
            bool result = false;
            if (m_GroupOwner == null)
            {
                return result;
            }
            for (int i = 0; i < m_GroupOwner.m_GroupMembers.Count; i++)
            {
                if (m_GroupOwner.m_GroupMembers[i] == target)
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        public int PKLevel()
        {
            return m_nPkPoint / 100;
        }

        public void HealthSpellChanged()
        {
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                SendUpdateMsg(this, Grobal2.RM_HEALTHSPELLCHANGED, 0, 0, 0, 0, "");
            }
            if (m_boShowHP)
            {
                SendRefMsg(Grobal2.RM_HEALTHSPELLCHANGED, 0, 0, 0, 0, "");
            }
        }

        /// <summary>
        /// 战神 sub_6C02A4 @0x6C02A4 —— 击杀怪物时的等级差经验惩罚。
        /// 调用点 0x6C0184..0x6C0193：EAX = 击杀者(Self)，EDX = 受害者等级
        /// (movzx word [esi+0x278])，ECX = 受害者经验 ([esi+0x2BC])。
        /// 归属：sub_6C0148 是 **TPlayer** 的虚方法(槽 +0xB0；593 个 VMT 全扫，
        /// 只有 TPlayer 与 TGdMsgGMAgent 两个类的 +0xB0 指向它)。
        ///
        /// ⚠️ 阈值是 **18**(0x12)，不是 10：
        ///   0x6C02C7  lea edx,[edi+0x12]      ; nLevel + 18
        ///   0x6C02E5  add edi,0x12            ; 惩罚项里同样是 +18
        /// 10 属于**另一个孪生函数** sub_728124(0x728138 `lea eax,[esi+0xA]`)，那是
        /// 组队分经验的等级差惩罚，见 TPlayObject.NativeGroupExpLevelGapAdjust。
        /// 两者除阈值外形状相同、都 fdiv 15.0（0x6C0314 与 0x728180 都是 float 15.0），
        /// 极易串味——本函数此前正是错用了 10，等级差落在 10..17 的每一次击杀都算错。
        ///
        /// 0x6C0314 = float 15.0；sub_403574 = Delphi Round(fistp，banker's) = HUtil32.Round。
        /// </summary>
        public int CalcGetExp(int nLevel, int nExp)
        {
            // 0x6C02B3 `test esi,esi` / `jle 0x6C0308` -> `xor eax,eax`：
            // 非正输入直接返回 0（不是落到下面的下限 1）。
            if (nExp <= 0)
            {
                return 0;
            }
            int result;
            // 0x6C02B7 `cmp dword [ebx+0xBD0],0` / `jg 0x6C02CE`：余额 > 0 时整条惩罚
            // 被跳过。obj+0xBD0 是**按秒计的余额**而非配置开关——0x7865EE
            // `imul esi,eax,0xE10` / 0x7865F4 `add [edi+0xBD0],esi` 按 3600 秒/小时充值，
            // 0x7865D2 在超过 0x7A1200 时拒绝充值。原先这里用的
            // g_Config.boHighLevelKillMonFixExp 在战神中不存在（'HighLevelKillMonFixExp'
            // 全镜像 0 命中，同法搜 'TDoubleExpProp' 命中 2 处作为阳性对照），
            // 且全局布尔无法表达“该角色还剩多少秒”。
            if (NativeFixedExpBalanceSeconds > 0 || (m_Abil.Level < (nLevel + 18)))
            {
                result = nExp;                                  // 0x6C02CE `mov eax,esi`
            }
            else
            {
                // 0x6C02D2..0x6C02FB
                result = nExp - HUtil32.Round(nExp / 15.0 * (m_Abil.Level - (nLevel + 18)));
            }
            if (result <= 0)
            {
                result = 1;                                     // 0x6C0301 下限 1
            }
            return result;
        }

        /// <summary>
        /// obj+0xBD0 —— 等级差经验惩罚的豁免余额（秒）。战神里这是玩家对象上的字段，
        /// 而 sub_6C02A4 的 Self 必然是 TPlayer（见上），所以非玩家一律读作 0。
        /// TPlayObject 覆写为真实字段。
        /// </summary>
        internal virtual int NativeFixedExpBalanceSeconds => 0;

        public void RefNameColor()
        {
            SendRefMsg(Grobal2.RM_CHANGENAMECOLOR, 0, 0, 0, 0, "");
        }

        private int GainSlaveUpKillCount()
        {
            int tCount;
            if (m_btSlaveExpLevel < Grobal2.SLAVEMAXLEVEL - 2)
            {
                tCount = M2Share.g_Config.MonUpLvNeedKillCount[m_btSlaveExpLevel];
            }
            else
            {
                tCount = 0;
            }
            return (m_Abil.Level * M2Share.g_Config.nMonUpLvRate) - m_Abil.Level + M2Share.g_Config.nMonUpLvNeedKillBase + tCount;
        }

        private void GainSlaveExp(int nLevel)
        {
            m_nKillMonCount += nLevel;
            if (GainSlaveUpKillCount() < m_nKillMonCount)
            {
                m_nKillMonCount -= GainSlaveUpKillCount();
                if (m_btSlaveExpLevel < (m_btSlaveMakeLevel * 2 + 1))
                {
                    m_btSlaveExpLevel++;
                    RecalcAbilitys();
                    RefNameColor();
                }
            }
        }

        protected bool DropGoldDown(int nGold, bool boFalg, TBaseObject GoldOfCreat, TBaseObject DropGoldCreat)
        {
            bool result = false;
            int nX = 0;
            int nY = 0;
            string s20;
            int DropWide = HUtil32._MIN(M2Share.g_Config.nDropItemRage, 7);
            MapItem MapItem = new MapItem
            {
                Name = Grobal2.sSTRING_GOLDNAME,
                Count = nGold,
                Looks = M2Share.GetGoldShape(nGold),
                OfBaseObject = GoldOfCreat,
                CanPickUpTick = HUtil32.GetTickCount(),
                DropBaseObject = DropGoldCreat
            };
            GetDropPosition(m_nCurrX, m_nCurrY, 3, ref nX, ref nY);
            MapItem MapItemA = (MapItem)m_PEnvir.AddToMap(nX, nY, CellType.OS_ITEMOBJECT, MapItem);
            if (MapItemA != null)
            {
                if (MapItemA != MapItem)
                {
                    MapItem = null;
                    MapItem = MapItemA;
                }

                SendRefMsg(Grobal2.RM_ITEMSHOW, MapItem.Looks, MapItem.Id, nX, nY, MapItem.Name);
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    if (boFalg)
                    {
                        s20 = "15";
                    }
                    else
                    {
                        s20 = "7";
                    }
                    if (M2Share.g_boGameLogGold)
                    {
                        M2Share.AddGameDataLog(s20 + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME + "\t" + nGold + "\t" +
                                               HUtil32.BoolToIntStr(m_btRaceServer == Grobal2.RC_PLAYOBJECT) + "\t" + '0');
                    }
                }
                result = true;
            }
            else
            {
                MapItem = null;
            }
            return result;
        }

        public int GetGuildRelation(TBaseObject cert1, TBaseObject cert2)
        {
            int result = 0;
            m_boGuildWarArea = false;
            if ((cert1.m_MyGuild == null) || (cert2.m_MyGuild == null))
            {
                return result;
            }
            if (cert1.InSafeArea() || cert2.InSafeArea())
            {
                return result;
            }
            // Native: Guild war is declaratory only with no combat effect.
            // The m_boGuildWarArea=true assignment and GuildWarList.Count gate are invented C# behavior.
            // Native supports same-guild (result=1) and ally (result=3) relations only.
            // War relation (result=2) is tracked but never consumed by combat (no native ==2 test exists).
            if (cert1.m_MyGuild == cert2.m_MyGuild)
            {
                result = 1;
            }
            if (cert1.m_MyGuild.IsAllyGuild(cert2.m_MyGuild) && cert2.m_MyGuild.IsAllyGuild(cert1.m_MyGuild))
            {
                result = 3;
            }
            return result;
        }

        protected void IncPkPoint(int nPoint)
        {
            var nOldPKLevel = PKLevel();
            m_nPkPoint += nPoint;
            if (PKLevel() != nOldPKLevel)
            {
                if (PKLevel() <= 2)
                {
                    RefNameColor();
                }
            }
        }

        private void DecPKPoint(int nPoint)
        {
            int nC = PKLevel();
            m_nPkPoint -= nPoint;
            if (m_nPkPoint < 0)
            {
                m_nPkPoint = 0;
            }
            if ((PKLevel() != nC) && (nC > 0) && (nC <= 2))
            {
                RefNameColor();
            }
        }

        // 原版 sub_7698BC(luck_hide_out.txt): [+0x164] += Round(a2/500); 然后 clamp[-10,+5]。
        // 所有原生写入者都传 500 的整数倍(GM ChgBodyLuck=luck*500 / PAS AddPlayerBodyLuck=luck*500 /
        // 死亡 sub_6C07A0=+500 / PK杀 sub_6C0FE4=-500 / 状态刷新 sub_7650D8=0)，净效果即对权威小值做整级
        // ±n 加法。故此处直接对 m_nBodyLuckLevel(== 原生 [+0x164])做整级加法并 clamp[-10,+5]。
        // 原 ×5000 累加器 m_dBodyLuck 已退役(原生无此字段；消费端 Merchant.cs:1045 / NativeMagicDamage.cs:246 /
        // PasApiBridge crit 全部读 m_nBodyLuckLevel)；低界由 -5 修正为原生的 -10。
        // (逆向证据: staging/update_clothes_4637_ida_work/luck_hide_out.txt sub_7698BC +
        //  staging/gm_player_attr_commands_20260801.md)
        protected void AddBodyLuck(int nLuck)
        {
            m_nBodyLuckLevel += nLuck;
            if (m_nBodyLuckLevel > 5)
            {
                m_nBodyLuckLevel = 5;
            }
            if (m_nBodyLuckLevel < -10)
            {
                m_nBodyLuckLevel = -10;
            }
        }

        protected void MakeWeaponUnlock()
        {
            if (m_UseItems[Grobal2.U_WEAPON] == null)
            {
                return;
            }
            if (m_UseItems[Grobal2.U_WEAPON].wIndex <= 0)
            {
                return;
            }
            if (m_UseItems[Grobal2.U_WEAPON].btValue[3] > 0)
            {
                m_UseItems[Grobal2.U_WEAPON].btValue[3] -= 1;
                SysMsg(M2Share.g_sTheWeaponIsCursed, MsgColor.Red, MsgType.Hint);
            }
            else
            {
                if (m_UseItems[Grobal2.U_WEAPON].btValue[4] < 10)
                {
                    m_UseItems[Grobal2.U_WEAPON].btValue[4]++;
                    SysMsg(M2Share.g_sTheWeaponIsCursed, MsgColor.Red, MsgType.Hint);
                }
            }
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                RecalcAbilitys();
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
            }
        }

        public ushort GetAttackPower(int nBasePower, int nPower)
        {
            int result;
            TPlayObject PlayObject;
            if (nPower < 0)
            {
                nPower = 0;
            }
            if (m_nLuck > 0)
            {
                if (M2Share.RandomNumber.Random(10 - HUtil32._MIN(9, m_nLuck)) == 0)
                {
                    result = nBasePower + nPower;
                }
                else
                {
                    result = nBasePower + M2Share.RandomNumber.Random(nPower + 1);
                }
            }
            else
            {
                result = nBasePower + M2Share.RandomNumber.Random(nPower + 1);
                if (m_nLuck < 0)
                {
                    if (M2Share.RandomNumber.Random(10 - HUtil32._MAX(0, -m_nLuck)) == 0)
                    {
                        result = nBasePower;
                    }
                }
            }
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                PlayObject = this as TPlayObject;
                result = HUtil32.Round(result * (PlayObject.m_nPowerRate / 100.0));
                if (PlayObject.m_boPowerItem)
                {
                    result = HUtil32.Round(m_rPowerItem * result);
                }
            }
            if (m_boAutoChangeColor)
            {
                result = result * m_nAutoChangeIdx + 1;
            }
            if (m_boFixColor)
            {
                result = result * m_nFixColorIdx + 1;
            }
            return (ushort)result;
        }

        
        
        
        
        /// <summary>
        /// Native <c>DamageHealth</c> = <c>sub_767D14</c> (TPlayer VMT
        /// <c>+0x1B0</c>, ref @<c>0x6ACA78</c>). Native routes BOTH the physical
        /// (<c>sub_767958</c>) and the magic (<c>sub_7679B8</c>) armour results
        /// into this ONE function, so the legacy physical path and the
        /// earth-fire/state-26/FireKing path must be the same code.
        /// <para>
        /// The four native mitigation stages, its <c>max(applied,1)</c> return
        /// (@0x767E49 <c>mov edx,1; mov eax,esi; call sub_4C7004</c>) and the
        /// <c>+0x99</c> dirty bit (@0x767E44 <c>call sub_7693E8</c>) all live in
        /// <c>ApplyStandardEarthFireLanding</c>
        /// (TBaseObject.NativeMagicDamage.cs), the byte-verified port. This is a
        /// pure alias so there is exactly one implementation.
        /// </para>
        /// <para>
        /// Native <c>sub_767D14</c> takes no attacker argument at all
        /// (<c>eax</c>=self, <c>edx</c>=damage) and its whole body
        /// <c>0x767D14-0x767E5A</c> reads only <c>+0x3F</c> state, <c>+0x3DF</c>,
        /// <c>+0x1D3</c>, <c>+0x1BA</c>, <c>+0x1BB</c>, <c>+0x2AC</c>,
        /// <c>+0x2B0</c> and <c>+0x2B4</c> — so it CANNOT consult the hitter.
        /// The former <c>m_LastHiter.m_boUnMagicShield</c> precondition and the
        /// immediate <c>HealthSpellChanged()</c> both had no native counterpart
        /// (native only sets <c>+0x99</c>, flushed by the 500 ms player loop).
        /// </para>
        /// </summary>
        internal int DamageHealth(int nDamage)
        {
            return ApplyStandardEarthFireLanding(nDamage);
        }

        public byte GetBackDir(int nDir)
        {
            byte result = 0;
            switch (nDir)
            {
                case Grobal2.DR_UP:
                    result = Grobal2.DR_DOWN;
                    break;
                case Grobal2.DR_DOWN:
                    result = Grobal2.DR_UP;
                    break;
                case Grobal2.DR_LEFT:
                    result = Grobal2.DR_RIGHT;
                    break;
                case Grobal2.DR_RIGHT:
                    result = Grobal2.DR_LEFT;
                    break;
                case Grobal2.DR_UPLEFT:
                    result = Grobal2.DR_DOWNRIGHT;
                    break;
                case Grobal2.DR_UPRIGHT:
                    result = Grobal2.DR_DOWNLEFT;
                    break;
                case Grobal2.DR_DOWNLEFT:
                    result = Grobal2.DR_UPRIGHT;
                    break;
                case Grobal2.DR_DOWNRIGHT:
                    result = Grobal2.DR_UPLEFT;
                    break;
            }
            return result;
        }

        public int CharPushed(byte nDir, int nPushCount)
        {
            short nx = 0;
            short ny = 0;
            int result = 0;
            byte olddir = m_btDirection;
            int oldx = m_nCurrX;
            int oldy = m_nCurrY;
            m_btDirection = nDir;
            byte nBackDir = GetBackDir(nDir);
            for (var i = 0; i < nPushCount; i++)
            {
                GetFrontPosition(ref nx, ref ny);
                if (m_PEnvir.CanWalk(nx, ny, false))
                {
                    if (m_PEnvir.MoveToMovingObject(m_nCurrX, m_nCurrY, this, nx, ny, false) > 0)
                    {
                        m_nCurrX = nx;
                        m_nCurrY = ny;
                        SendRefMsg(Grobal2.RM_PUSH, nBackDir, m_nCurrX, m_nCurrY, 0, "");
                        result++;
                        if (m_btRaceServer >= Grobal2.RC_ANIMAL)
                        {
                            m_dwWalkTick = m_dwWalkTick + 800;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            m_btDirection = nBackDir;
            if (result == 0)
            {
                m_btDirection = olddir;
            }
            return result;
        }

        public int MagPassThroughMagic(short sx, short sy, short tx, short ty, int ndir, int magpwr, bool undeadattack)
        {
            TBaseObject BaseObject;
            int tcount = 0;
            for (int i = 0; i <= 12; i++)
            {
                BaseObject = m_PEnvir.GetMovingObject(sx, sy, true) as TBaseObject;
                if (BaseObject != null)
                {
                    if (IsProperTarget(BaseObject))
                    {
                        if (M2Share.RandomNumber.Random(10) >= BaseObject.m_nAntiMagic)
                        {
                            if (undeadattack)
                            {
                                magpwr = HUtil32.Round(magpwr * 1.5);
                            }
                            BaseObject.SendDelayMsg(this, Grobal2.RM_MAGSTRUCK, 0, magpwr, 0, 0, "", 600);
                            tcount++;
                        }
                    }
                }
                if (!((Math.Abs(sx - tx) <= 0) && (Math.Abs(sy - ty) <= 0)))
                {
                    ndir = M2Share.GetNextDirection(sx, sy, tx, ty);
                    if (!m_PEnvir.GetNextPosition(sx, sy, ndir, 1, ref sx, ref sy))
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            return tcount;
        }

        private void BreakOpenHealth()
        {
            if (m_boShowHP)
            {
                m_boShowHP = false;
                m_nCharStatusEx = m_nCharStatusEx ^ Grobal2.STATE_OPENHEATH;
                m_nCharStatus = GetCharStatus();
                SendRefMsg(Grobal2.RM_CLOSEHEALTH, 0, 0, 0, 0, "");
            }
        }

        private void MakeOpenHealth()
        {
            m_boShowHP = true;
            m_nCharStatusEx = m_nCharStatusEx | Grobal2.STATE_OPENHEATH;
            m_nCharStatus = GetCharStatus();
            SendRefMsg(Grobal2.RM_OPENHEALTH, 0, m_WAbil.HP, m_WAbil.MaxHP, 0, "");
        }

        public void IncHealthSpell(int nHP, int nMP)
        {
            if ((nHP < 0) || (nMP < 0))
            {
                return;
            }
            m_WAbil.HP = (int)Math.Min((long)m_WAbil.HP + nHP,
                Math.Max(0, m_WAbil.MaxHP));
            m_WAbil.MP = (int)Math.Min((long)m_WAbil.MP + nMP,
                Math.Max(0, m_WAbil.MaxMP));
            HealthSpellChanged();
        }

        private void ItemDamageRevivalRing()
        {
            GoodItem pSItem;
            ushort nDura;
            ushort tDura;
            TPlayObject PlayObject;
            for (int i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                if (m_UseItems[i] != null && m_UseItems[i].wIndex > 0)
                {
                    pSItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                    if (pSItem != null)
                    {
                        if (new ArrayList(new byte[] { 114, 160, 161, 162 }).Contains(pSItem.Shape) || (((i == Grobal2.U_WEAPON) || (i == Grobal2.U_RIGHTHAND)) && new ArrayList(new byte[] { 114, 160, 161, 162 }).Contains(pSItem.AniCount)))
                        {
                            nDura = m_UseItems[i].Dura;
                            tDura = (ushort)HUtil32.Round(nDura / 1000.0);// 1.03
                            nDura -= 1000;
                            if (nDura <= 0)
                            {
                                nDura = 0;
                                m_UseItems[i].Dura = nDura;
                                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                {
                                    PlayObject = this as TPlayObject;
                                    PlayObject.SendDelItems(m_UseItems[i]);
                                }
                                m_UseItems[i].wIndex = 0;
                                RecalcAbilitys();
                            }
                            else
                            {
                                m_UseItems[i].Dura = nDura;
                            }
                            // NOTE: this is ItemDamageRevivalRing, NOT DoDamageWeapon.
                            // The `old > new` shape proven for sub_73E804 belongs to
                            // DoDamageWeapon (see that method); it must not be copied
                            // here without evidence for this function's own native
                            // counterpart, which has not been identified yet.
                            if (tDura != HUtil32.Round(nDura / 1000.0))
                            {
                                SendMsg(this, Grobal2.RM_DURACHANGE, i, nDura, m_UseItems[i].DuraMax, 0, "");
                            }
                        }
                    }
                }
            }
        }

        public bool GetFrontPosition(ref short nX, ref short nY)
        {
            bool result;
            Envirnoment Envir = m_PEnvir;
            nX = m_nCurrX;
            nY = m_nCurrY;
            switch (m_btDirection)
            {
                case Grobal2.DR_UP:
                    if (nY > 0)
                    {
                        nY -= 1;
                    }
                    break;
                case Grobal2.DR_UPRIGHT:
                    if ((nX < (Envir.wWidth - 1)) && (nY > 0))
                    {
                        nX++;
                        nY -= 1;
                    }
                    break;
                case Grobal2.DR_RIGHT:
                    if (nX < (Envir.wWidth - 1))
                    {
                        nX++;
                    }
                    break;
                case Grobal2.DR_DOWNRIGHT:
                    if ((nX < (Envir.wWidth - 1)) && (nY < (Envir.wHeight - 1)))
                    {
                        nX++;
                        nY++;
                    }
                    break;
                case Grobal2.DR_DOWN:
                    if (nY < (Envir.wHeight - 1))
                    {
                        nY++;
                    }
                    break;
                case Grobal2.DR_DOWNLEFT:
                    if ((nX > 0) && (nY < (Envir.wHeight - 1)))
                    {
                        nX -= 1;
                        nY++;
                    }
                    break;
                case Grobal2.DR_LEFT:
                    if (nX > 0)
                    {
                        nX -= 1;
                    }
                    break;
                case Grobal2.DR_UPLEFT:
                    if ((nX > 0) && (nY > 0))
                    {
                        nX -= 1;
                        nY -= 1;
                    }
                    break;
            }
            result = true;
            return result;
        }

        /// 战神 <c>GetRandomXY sub_7782D0</c> (ret 8) — the ONE random-spot search
        /// primitive behind every teleport. All 11 native callers reach this same
        /// function (0x64F4AB, 0x667498, 0x679FF9, 0x6B9C81, 0x6B9DC7, 0x6BD346,
        /// 0x6BD4EA, 0x728A35, 0x768DB5, 0x768EA7, 0x788070), so there is no second
        /// native search with different constants. Byte-pinned parameters:
        /// <code>
        /// 77830A  stepX := 3
        /// 778311  cmp Width,0x32   ; below 50 -> stepX := 2
        /// 77831E  cmp Height,0xFA  ; 250 or more -> margin := 0x32 (50)
        /// 778328  cmp Height,0x1E  ; 30 or more  -> margin := 0x14 (20)
        /// 77832D  margin := 2
        /// 778346  mov esi,0x1F     ; retry count = 31, NOT 32
        /// 778358  probe sub_777EF8 (boIgnoreOccupancy from caller = 1 here)
        /// 778361/778373  X advance by stepX, else reseed Random(Width div 2)+margin
        /// 778387/77839F  Y advance by stepX, else reseed Random(Height div 2)+margin
        /// 7783BE  dec esi / jne    ; loop
        /// </code>
        /// On total failure it falls back to a random LinkPoint (0x7783C4-0x7783FD):
        /// list at Envir[+0x10], count at [eax+8], X = word[rec+0], Y = word[rec+2];
        /// FALSE only when that list is empty. The fallback is gated on the caller's
        /// flag byte [ebp+8], and every teleport caller pushes 1 (0x6BD336 `push 1
        /// push 1` same-map, 0x6BD4EA cross-map both flags 1), so it is unconditional
        /// here. This body previously used step 3/10 at Width&lt;80, margin 50/15/2 at
        /// Height 150/50, a retry count of 201 and a reseed of Random(Width) with no
        /// margin, and had no LinkPoint fallback at all — none of which native has.
        private bool SpaceMove_GetRandXY(Envirnoment Envir, ref short nX, ref short nY)
        {
            var nStep = (short)(Envir.wWidth < 50 ? 2 : 3);
            var nMargin = Envir.wHeight < 30
                ? 2
                : Envir.wHeight < 250 ? 20 : 50;
            for (var nRetry = 0; nRetry < 31; nRetry++)
            {
                if (Envir.CanWalk(nX, nY, true))
                {
                    return true;
                }
                if (nX < (Envir.wWidth - nMargin - 1))
                {
                    nX += nStep;
                }
                else
                {
                    nX = (short)(M2Share.RandomNumber.Random(Envir.wWidth / 2)
                        + nMargin);
                    if (nY < (Envir.wHeight - nMargin - 1))
                    {
                        nY += nStep;
                    }
                    else
                    {
                        nY = (short)(M2Share.RandomNumber.Random(Envir.wHeight / 2)
                            + nMargin);
                    }
                }
            }
            if (Envir.m_PointList == null || Envir.m_PointList.Count == 0)
            {
                return false;
            }
            var Point = Envir.m_PointList[
                M2Share.RandomNumber.Random(Envir.m_PointList.Count)];
            nX = Point.nX;
            nY = Point.nY;
            return true;
        }

        internal bool TrySpaceMoveToEnvironment(Envirnoment targetEnvironment,
            short nX, short nY, int showMode,
            bool coordinatesAlreadyResolved = false,
            bool useNativeInternalMessages = false)
        {
            if (targetEnvironment == null
                || M2Share.nServerIndex != targetEnvironment.nServerIndex)
                return false;

            var oldEnvironment = m_PEnvir;
            if (oldEnvironment == null) return false;

            var sameEnvironment = ReferenceEquals(oldEnvironment,
                targetEnvironment);

            if (this is TPlayObject playObject)
            {
                playObject.CleanupNativeHorseBeforeSpaceMove();
            }

            var oldMapName = m_sMapName;
            var oldMapFileName = m_sMapFileName;
            var oldX = m_nCurrX;
            var oldY = m_nCurrY;
            var oldVisibleHumans = m_VisibleHumanList?.ToList();
            var oldVisibleItems = m_VisibleItems?.ToList();
            var oldVisibleActors = m_VisibleActors?.ToList();
            var oldVisibleEvents = m_VisibleEvents?.ToList();
            var sourceRemoved = false;
            var sourceRestored = true;
            var targetAddAttempted = false;
            var committed = false;
            try
            {
                sourceRemoved = oldEnvironment.DeleteFromMap(m_nCurrX, m_nCurrY,
                    CellType.OS_MOVINGOBJECT, this, false,
                    suppressMapDropConsumer: true) == 1;
                if (!sourceRemoved) return false;

                m_PEnvir = targetEnvironment;
                m_sMapName = targetEnvironment.sMapName;
                m_sMapFileName = targetEnvironment.m_sMapFileName;
                m_nCurrX = nX;
                m_nCurrY = nY;
                if (!coordinatesAlreadyResolved
                    && !SpaceMove_GetRandXY(targetEnvironment, ref m_nCurrX,
                        ref m_nCurrY))
                    return false;

                targetAddAttempted = true;
                if (!ReferenceEquals(targetEnvironment.AddToMap(m_nCurrX, m_nCurrY,
                    CellType.OS_MOVINGOBJECT, this), this))
                    return false;

                m_VisibleHumanList.Clear();
                for (var i = 0; i < m_VisibleItems.Count; i++)
                {
                    m_VisibleItems[i] = null;
                }
                m_VisibleItems.Clear();
                for (var i = 0; i < m_VisibleActors.Count; i++)
                {
                    m_VisibleActors[i] = null;
                }
                m_VisibleActors.Clear();
                m_VisibleEvents.Clear();
                m_dwMapMoveTick = HUtil32.GetTickCount();
                m_bo316 = true;
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT) m_dwSearchTick = 0;
                OnEnvirnomentChanged();
                SendMsg(this, useNativeInternalMessages
                    ? Grobal2.RM_NATIVE_CLEAROBJECTS
                    : Grobal2.RM_CLEAROBJECTS, 0, 0, 0, 0, "");
                SendMsg(this, useNativeInternalMessages
                    ? Grobal2.RM_NATIVE_CHANGEMAP
                    : Grobal2.RM_CHANGEMAP, 0, 0, 0, 0, m_sMapFileName);
                if (showMode == 1)
                {
                    SendRefMsg(Grobal2.RM_SPACEMOVE_SHOW2, m_btDirection,
                        m_nCurrX, m_nCurrY, 0, "");
                }
                else
                {
                    SendRefMsg(Grobal2.RM_SPACEMOVE_SHOW, m_btDirection,
                        m_nCurrX, m_nCurrY, 0, "");
                }
                committed = true;
                if (this is TPlayObject committedPlayer)
                {
                    var committedEnvironment = m_PEnvir;
                    var committedMapName = m_sMapName;
                    var committedMapFileName = m_sMapFileName;
                    var committedX = m_nCurrX;
                    var committedY = m_nCurrY;
                    try
                    {
                        m_PEnvir = oldEnvironment;
                        m_sMapName = oldMapName;
                        m_sMapFileName = oldMapFileName;
                        m_nCurrX = oldX;
                        m_nCurrY = oldY;
                        committedPlayer.ReleaseNativeMapDropItems(
                            oldEnvironment, removeTracker: !sameEnvironment);
                    }
                    finally
                    {
                        m_PEnvir = committedEnvironment;
                        m_sMapName = committedMapName;
                        m_sMapFileName = committedMapFileName;
                        m_nCurrX = committedX;
                        m_nCurrY = committedY;
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage("[Exception] TBaseObject::TrySpaceMoveToEnvironment " + e.Message);
            }
            finally
            {
                if (sourceRemoved && !committed)
                {
                    if (targetAddAttempted)
                    {
                        targetEnvironment.DeleteFromMap(m_nCurrX, m_nCurrY,
                            CellType.OS_MOVINGOBJECT, this, false,
                            suppressMapDropConsumer: true);
                    }
                    m_PEnvir = oldEnvironment;
                    m_sMapName = oldMapName;
                    m_sMapFileName = oldMapFileName;
                    m_nCurrX = oldX;
                    m_nCurrY = oldY;
                    sourceRestored = ReferenceEquals(oldEnvironment.AddToMap(m_nCurrX,
                        m_nCurrY, CellType.OS_MOVINGOBJECT, this), this);
                    if (!sourceRestored)
                    {
                        M2Share.ErrorMessage("[Exception] TBaseObject::TrySpaceMoveToEnvironment failed to restore source map");
                    }
                    else
                    {
                        m_VisibleHumanList?.Clear();
                        if (oldVisibleHumans != null)
                        {
                            foreach (var actor in oldVisibleHumans)
                                m_VisibleHumanList?.Add(actor);
                        }
                        m_VisibleItems?.Clear();
                        if (oldVisibleItems != null)
                        {
                            foreach (var item in oldVisibleItems)
                                m_VisibleItems?.Add(item);
                        }
                        m_VisibleActors?.Clear();
                        if (oldVisibleActors != null)
                        {
                            foreach (var actor in oldVisibleActors)
                                m_VisibleActors?.Add(actor);
                        }
                        m_VisibleEvents?.Clear();
                        if (oldVisibleEvents != null)
                        {
                            foreach (var mapEvent in oldVisibleEvents)
                                m_VisibleEvents?.Add(mapEvent);
                        }
                    }
                }

                if (CountsAsPlayerPresence
                    && ((committed && !ReferenceEquals(oldEnvironment, targetEnvironment))
                        || !sourceRestored))
                {
                    try
                    {
                        oldEnvironment.NotifyDynamicRoomPlayerRemoved();
                    }
                    catch (Exception e)
                    {
                        M2Share.ErrorMessage("[Exception] TBaseObject::TrySpaceMoveToEnvironment dynamic room notification " + e.Message);
                    }
                }
            }
            return committed;
        }

        internal void SpaceMove(Envirnoment Envir, short nX, short nY,
            int nInt)
        {
            if (Envir == null)
                return;
            if (M2Share.nServerIndex == Envir.nServerIndex)
            {
                TrySpaceMoveToEnvironment(Envir, nX, nY, nInt);
            }
            else if (SpaceMove_GetRandXY(Envir, ref nX, ref nY))
            {
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    TryBeginCrossServerTransfer(Envir, nX, nY);
                }
                else
                {
                    KickException();
                }
            }
        }

        public void SpaceMove(string sMap, short nX, short nY, int nInt)
        {
            SpaceMove(M2Share.MapManager.FindMap(sMap), nX, nY, nInt);
        }

        public void RefShowName()
        {
            SendRefMsg(Grobal2.RM_USERNAME, 0, 0, 0, 0, GetShowName());
        }

        public TBaseObject MakeSlave(string sMonName, int nMakeLevel, int nExpLevel, int nMaxMob, int dwRoyaltySec)
        {
            short nX = 0;
            short nY = 0;
            TBaseObject result = null;
            if (m_SlaveList.Count < nMaxMob)
            {
                GetFrontPosition(ref nX, ref nY);
                var MonObj = M2Share.UserEngine.RegenMonsterByName(m_PEnvir, nX, nY, sMonName);
                if (MonObj != null)
                {
                    MonObj.m_Master = this;
                    MonObj.m_dwMasterRoyaltyTick = HUtil32.GetTickCount() + (dwRoyaltySec * 1000);
                    MonObj.m_btSlaveMakeLevel = (byte)nMakeLevel;
                    MonObj.m_btSlaveExpLevel = (byte)nExpLevel;
                    MonObj.RecalcAbilitys();
                    if (MonObj.m_WAbil.HP < MonObj.m_WAbil.MaxHP)
                    {
                        MonObj.m_WAbil.HP += (MonObj.m_WAbil.MaxHP - MonObj.m_WAbil.HP) / 2;
                    }
                    MonObj.RefNameColor();
                    m_SlaveList.Add(MonObj);
                    result = MonObj;
                }
            }
            return result;
        }

        
        
        
        
        
        public void MapRandomMove(string sMapName, int nInt)
        {
            int nEgdey;
            Envirnoment Envir = M2Share.MapManager.FindMap(sMapName);
            if (Envir != null)
            {
                if (Envir.wHeight < 150)
                {
                    if (Envir.wHeight < 30)
                    {
                        nEgdey = 2;
                    }
                    else
                    {
                        nEgdey = 20;
                    }
                }
                else
                {
                    nEgdey = 50;
                }
                short nX = (short)(M2Share.RandomNumber.Random(Envir.wWidth - nEgdey - 1) + nEgdey);
                short nY = (short)(M2Share.RandomNumber.Random(Envir.wHeight - nEgdey - 1) + nEgdey);
                SpaceMove(sMapName, nX, nY, nInt);
            }
        }

        /// <summary>
        /// 战神 <c>sub_6B7378</c> — the OUTER AddItemToBag (TPlayer VMT slot <c>+0x248</c>,
        /// 68 native call sites).  It runs the acquisition stamper and then delegates to
        /// the plain add.  Byte-exact body (capstone over the reunpacked image):
        /// <code>
        /// 6B739A  test esi,esi / setne al / test al,bl / je 0x6B73C7 ; item != nil AND bl (stampEnable)
        /// 6B73A3  cmp byte [edi+0x675],3 / ja 0x6B73C7               ; GMLevel &lt;= 3 (RTTI idx=0014)
        /// 6B73AC  mov al,[ebp+8] / push eax                          ; acquisitionReason
        /// 6B73B2  call sub_6D43C4                                    ; ecx = Trunc(Now() - [player+0x780]) = days online
        /// 6B73B9  mov dx,word [edi+0x278]                            ; dx = Level word
        /// 6B73C2  call sub_7842F8                                    ; THE STAMPER
        /// 6B73C7  mov al,[ebp+8] / push eax / call sub_73D078        ; the plain add (unconditional)
        /// 6B73D8  test byte [[0x7D7038]+2],0x80 / …                  ; optional async item-log branch
        /// </code>
        /// Note the stamper is gated but the plain add at <c>0x6B73C7</c> is reached on
        /// every path — a rejected stamp never blocks the add.  <paramref name="stampEnable"/>
        /// is native <c>bl</c> (<c>mov cl,1</c> at 36 sites, <c>xor ecx,ecx</c> at 35);
        /// <paramref name="reason"/> is the pushed stack argument (0 at 63 sites,
        /// 1 = deal <c>sub_6C4580</c>, 2 = <c>sub_6D3C7C</c>/<c>sub_6F1358</c>,
        /// 4 = pickup <c>sub_6B74D8</c> @0x6B7708).
        /// </summary>
        public bool AddItemToBag(TUserItem UserItem, byte reason, bool stampEnable)
        {
            // 0x6B739A: item != nil AND the caller's stamp-enable flag.
            // 0x6B73A3: cmp byte[player+0x675],3; ja -> skip the stamper entirely
            //           (GM-acquired items are deliberately NOT stamped).
            if (UserItem != null && stampEnable && m_btPermission
                    <= NativeItemAcquisitionStamp.MaxStampedGmLevel)
            {
                var stdItem = M2Share.UserEngine?.GetStdItem(UserItem.wIndex);
                if (stdItem != null)
                {
                    // 0x6B73B2 sub_6D43C4 = Trunc(Now() - [player+0x780]) — the whole
                    // days the character has been online in total.  0x6B73B9 dx = Level.
                    NativeItemAcquisitionStamp.Apply(UserItem, stdItem, reason,
                        GetNativeAcquisitionDaysOnline(), m_Abil.Level);
                }
            }
            // 0x6B73C7: sub_73D078 — the plain add, reached unconditionally.
            return AddItemToBag(UserItem);
        }

        /// <summary>
        /// 战神 <c>sub_73D078</c> — the plain (inner) AddItemToBag: bag-space gate via
        /// VMT slot <c>+0x244</c> (<c>sub_6D0AE8</c>: <c>Count + 1 &lt;= 48</c>, i.e.
        /// <c>Count &lt; 48</c>) then <c>sub_73CEA8</c> = TList.Add and the weight refresh
        /// <c>sub_73CEE4</c>.
        /// </summary>
        public bool AddItemToBag(TUserItem UserItem)
        {
            bool result = false;
            if (m_ItemList.Count < Grobal2.MAXBAGITEM)
            {
                m_ItemList.Add(UserItem);
                WeightChanged();
                result = true;
            }
            return result;
        }

        /// <summary>
        /// 战神 <c>sub_6D43C4</c> @0x6D43C4: <c>call sub_40F0A4</c> (Delphi <c>Now</c>)
        /// then <c>fsub qword [player+0x780]</c> then <c>sub_403580</c> (= <c>fistp</c>
        /// with the round-toward-zero control word set, i.e. Trunc) — the number of whole
        /// days between now and the player's accumulated-online-time base.
        ///
        /// Returning 0 is EQUIVALENT to native, not a gap.  Corrected 2026-08-07;
        /// the previous comment here claimed <c>+0xEF40</c> was an unmodelled
        /// PERSISTED record double, which is wrong on two independent counts:
        ///
        /// 1. <c>+0xEF40</c> is addressed off <c>[ebp-8]</c> (the THumDataInfo STRUCT
        ///    base), not off <c>[ebp-0x28]</c> (the ShortString data area, which is
        ///    <c>struct+8</c> — see 0x6AFDBC `mov eax,[ebp-8]` / 0x6AFDBF `add eax,8`
        ///    / 0x6AFDC2 `mov [ebp-0x28],eax`).  As a data-area offset it would be
        ///    0xEF38, past the end of the persisted payload (0xEEF8 = DataRecordSize),
        ///    so it is NOT part of the saved record — it lives in the session tail of
        ///    the in-memory struct (total 0xF0FC, zero-filled at 0x6B65FE).
        /// 2. NOTHING EVER WRITES IT.  `_cs_field.py ef40 all` reports 5 refs, all
        ///    reads, all inside the loader sub_6AFD7C (0x6B0289, 0x6B02DD, 0x6B03EB,
        ///    0x6B04CD, 0x6B075D), and a byte scan of the repaired DBServer image
        ///    finds zero occurrences of the displacement.  The slot is therefore
        ///    always the 0 left by the SAVE-side FillChar.
        ///
        /// So natively <c>+0x780 = [player+0x778] - 0.0</c>, and <c>+0x778</c> is just
        /// Delphi <c>Now</c> captured during the load (0x6B026E `call sub_40F0A4` ->
        /// 0x6B0276 `fstp qword [eax+0x778]`).  sub_6D43C4 then computes
        /// <c>Trunc(Now - Now) == 0</c>.  Do NOT "fix" this by inventing an
        /// accumulated-online field; 0 is the faithful result.
        /// </summary>
        /// <summary>
        /// 战神 <c>sub_617A38</c> called with <c>cl = 4</c> against the singleton at
        /// <c>[[0x7D6534]]</c> — the authentication test used by the DESTROY branch of all
        /// three drop paths (@0x73CD3B, @0x740158, @0x73FDC7).  Non-player actors never
        /// reach the ladder (the <c>byte [self+0x178]</c> race gate short-circuits first),
        /// so the base returns true and only <c>TPlayObject</c> consults the real status
        /// bits.
        /// </summary>
        protected virtual bool NativeItemDropDestroyAuthenticated()
        {
            return true;
        }

        protected virtual int GetNativeAcquisitionDaysOnline()
        {
            return 0;
        }

        
        
        
        
        protected void CheckSeeHealGauge(TUserMagic Magic)
        {
            if (Magic.MagicInfo.wMagicID == 28)
            {
                if (Magic.btLevel >= 2)
                {
                    m_boAbilSeeHealGauge = true;
                }
            }
        }

        public int GetQuestFalgStatus(int nFlag)
        {
            int result = 0;
            nFlag -= 1;
            if (nFlag < 0)
            {
                return result;
            }
            int n10 = nFlag / 8;
            int n14 = nFlag % 8;
            if ((n10 - m_QuestFlag.Length) < 0)
            {
                if (((128 >> n14) & m_QuestFlag[n10]) != 0)
                {
                    result = 1;
                }
                else
                {
                    result = 0;
                }
            }
            return result;
        }

        public void SetQuestFlagStatus(int nFlag, int nValue)
        {
            nFlag -= 1;
            if (nFlag < 0)
            {
                return;
            }
            int n10 = nFlag / 8;
            int n14 = nFlag % 8;
            if ((n10 - m_QuestFlag.Length) < 0)
            {
                byte bt15 = m_QuestFlag[n10];
                if (nValue == 0)
                {
                    m_QuestFlag[n10] = (byte)((~(128 >> n14)) & bt15);
                }
                else
                {
                    m_QuestFlag[n10] = (byte)((128 >> n14) | bt15);
                }
            }
        }

        public int GetQuestUnitOpenStatus(int nFlag)
        {
            var result = 0;
            nFlag -= 1;
            if (nFlag < 0)
            {
                return result;
            }
            var n10 = nFlag / 8;
            var n14 = nFlag % 8;
            if ((n10 - m_QuestUnitOpen.Length) < 0)
            {
                if (((128 >> n14) & m_QuestUnitOpen[n10]) != 0)
                {
                    result = 1;
                }
                else
                {
                    result = 0;
                }
            }
            return result;
        }

        public void SetQuestUnitOpenStatus(int nFlag, int nValue)
        {
            nFlag -= 1;
            if (nFlag < 0)
            {
                return;
            }
            var n10 = nFlag / 8;
            var n14 = nFlag % 8;
            if ((n10 - m_QuestUnitOpen.Length) < 0)
            {
                var bt15 = m_QuestUnitOpen[n10];
                if (nValue == 0)
                {
                    m_QuestUnitOpen[n10] = (byte)((~(128 >> n14)) & bt15);
                }
                else
                {
                    m_QuestUnitOpen[n10] = (byte)((128 >> n14) | bt15);
                }
            }
        }

        public int GetQuestUnitStatus(int nFlag)
        {
            int result = 0;
            nFlag -= 1;
            if (nFlag < 0)
            {
                return result;
            }
            int n10 = nFlag / 8;
            int n14 = nFlag % 8;
            if ((n10 - m_QuestUnit.Length) < 0)
            {
                if (((128 >> n14) & m_QuestUnit[n10]) != 0)
                {
                    result = 1;
                }
                else
                {
                    result = 0;
                }
            }
            return result;
        }

        public void SetQuestUnitStatus(int nFlag, int nValue)
        {
            nFlag -= 1;
            if (nFlag < 0)
            {
                return;
            }
            var n10 = nFlag / 8;
            var n14 = nFlag % 8;
            if ((n10 - m_QuestUnit.Length) < 0)
            {
                var bt15 = m_QuestUnit[n10];
                if (nValue == 0)
                {
                    m_QuestUnit[n10] = (byte)((~(128 >> n14)) & bt15);
                }
                else
                {
                    m_QuestUnit[n10] = (byte)((128 >> n14) | bt15);
                }
            }
        }

        private bool KillFunc()
        {
            const string sExceptionMsg = "[Exception] TBaseObject::KillFunc";
            bool result = false;
            try
            {
                if ((M2Share.g_FunctionNPC != null) && (m_PEnvir != null) && m_PEnvir.Flag.boKILLFUNC)
                {
                    if (m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                    {
                        if (m_ExpHitter != null)
                        {
                            if (m_ExpHitter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                            {
                                M2Share.g_FunctionNPC.GotoLable(m_ExpHitter as TPlayObject, "@KillPlayMon" + m_PEnvir.Flag.nKILLFUNCNO, false);
                            }
                            if (m_ExpHitter.m_Master != null)
                            {
                                M2Share.g_FunctionNPC.GotoLable(m_ExpHitter.m_Master as TPlayObject, "@KillPlayMon" + m_PEnvir.Flag.nKILLFUNCNO, false);
                            }
                        }
                        else
                        {
                            if (m_LastHiter != null)
                            {
                                if (m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                {
                                    M2Share.g_FunctionNPC.GotoLable(m_LastHiter as TPlayObject, "@KillPlayMon" + m_PEnvir.Flag.nKILLFUNCNO, false);
                                }
                                if (m_LastHiter.m_Master != null)
                                {
                                    M2Share.g_FunctionNPC.GotoLable(m_LastHiter.m_Master as TPlayObject, "@KillPlayMon" + m_PEnvir.Flag.nKILLFUNCNO, false);
                                }
                            }
                        }
                    }
                    else
                    {
                        if ((m_LastHiter != null) && (m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT))
                        {
                            M2Share.g_FunctionNPC.GotoLable(m_LastHiter as TPlayObject, "@KillPlay" + m_PEnvir.Flag.nKILLFUNCNO, false);
                        }
                    }
                    result = true;
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        
        
        
        private void UseLamp()
        {
            const string sExceptionMsg = "[Exception] TBaseObject::UseLamp";
            try
            {
                if (m_UseItems[Grobal2.U_RIGHTHAND] != null && m_UseItems[Grobal2.U_RIGHTHAND].wIndex > 0)
                {
                    var stdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_RIGHTHAND].wIndex);
                    if ((stdItem == null) || (stdItem.Source != 0))
                    {
                        return;
                    }
                    var nOldDura = HUtil32.Round(m_UseItems[Grobal2.U_RIGHTHAND].Dura / 1000);
                    int nDura = 0;
                    if (M2Share.g_Config.boDecLampDura)
                    {
                        nDura = m_UseItems[Grobal2.U_RIGHTHAND].Dura - 1;
                    }
                    else
                    {
                        nDura = m_UseItems[Grobal2.U_RIGHTHAND].Dura;
                    }
                    if (nDura <= 0)
                    {
                        m_UseItems[Grobal2.U_RIGHTHAND].Dura = 0;
                        if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            var PlayObject = this as TPlayObject;
                            PlayObject.SendDelItems(m_UseItems[Grobal2.U_RIGHTHAND]);
                        }
                        m_UseItems[Grobal2.U_RIGHTHAND].wIndex = 0;
                        m_nLight = 0;
                        SendRefMsg(Grobal2.RM_CHANGELIGHT, 0, 0, 0, 0, "");
                        SendMsg(this, Grobal2.RM_LAMPCHANGEDURA, 0, m_UseItems[Grobal2.U_RIGHTHAND].Dura, 0, 0, "");
                        RecalcAbilitys();
                    }
                    else
                    {
                        m_UseItems[Grobal2.U_RIGHTHAND].Dura = (ushort)nDura;
                    }
                    if (nOldDura != HUtil32.Round(nDura / 1000.0))
                    {
                        SendMsg(this, Grobal2.RM_LAMPCHANGEDURA, 0, m_UseItems[Grobal2.U_RIGHTHAND].Dura, 0, 0, "");
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
        }

        public TBaseObject GetPoseCreate()
        {
            short nX = 0;
            short nY = 0;
            TBaseObject result = null;
            if (GetFrontPosition(ref nX, ref nY))
            {
                result = (TBaseObject)m_PEnvir.GetMovingObject(nX, nY, true);
            }
            return result;
        }

        public bool GetAttackDir(TBaseObject BaseObject, ref byte btDir)
        {
            bool result = false;
            if ((m_nCurrX - 1 <= BaseObject.m_nCurrX) && (m_nCurrX + 1 >= BaseObject.m_nCurrX) && (m_nCurrY - 1 <= BaseObject.m_nCurrY) && (m_nCurrY + 1 >= BaseObject.m_nCurrY) && ((m_nCurrX != BaseObject.m_nCurrX) || (m_nCurrY != BaseObject.m_nCurrY)))
            {
                result = true;
                if (((m_nCurrX - 1) == BaseObject.m_nCurrX) && (m_nCurrY == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_LEFT;
                    return result;
                }
                if (((m_nCurrX + 1) == BaseObject.m_nCurrX) && (m_nCurrY == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_RIGHT;
                    return result;
                }
                if ((m_nCurrX == BaseObject.m_nCurrX) && ((m_nCurrY - 1) == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_UP;
                    return result;
                }
                if ((m_nCurrX == BaseObject.m_nCurrX) && ((m_nCurrY + 1) == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_DOWN;
                    return result;
                }
                if (((m_nCurrX - 1) == BaseObject.m_nCurrX) && ((m_nCurrY - 1) == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_UPLEFT;
                    return result;
                }
                if (((m_nCurrX + 1) == BaseObject.m_nCurrX) && ((m_nCurrY - 1) == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_UPRIGHT;
                    return result;
                }
                if (((m_nCurrX - 1) == BaseObject.m_nCurrX) && ((m_nCurrY + 1) == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_DOWNLEFT;
                    return result;
                }
                if (((m_nCurrX + 1) == BaseObject.m_nCurrX) && ((m_nCurrY + 1) == BaseObject.m_nCurrY))
                {
                    btDir = Grobal2.DR_DOWNRIGHT;
                    return result;
                }
                btDir = 0;
            }
            return result;
        }

        public bool GetAttackDir(TBaseObject BaseObject, int nRange, ref byte btDir)
        {
            short nX = 0;
            short nY = 0;
            btDir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, BaseObject.m_nCurrX, BaseObject.m_nCurrY);
            if (m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, btDir, nRange, ref nX, ref nY))
            {
                return BaseObject == (TBaseObject)m_PEnvir.GetMovingObject(nX, nY, true);
            }
            return false;
        }

        public bool TargetInSpitRange(TBaseObject BaseObject, ref byte btDir)
        {
            bool result = false;
            if ((Math.Abs(BaseObject.m_nCurrX - m_nCurrX) <= 2) && (Math.Abs(BaseObject.m_nCurrY - m_nCurrY) <= 2))
            {
                int nX = BaseObject.m_nCurrX - m_nCurrX;
                int nY = BaseObject.m_nCurrY - m_nCurrY;
                if ((Math.Abs(nX) <= 1) && (Math.Abs(nY) <= 1))
                {
                    GetAttackDir(BaseObject, ref btDir);
                    result = true;
                    return result;
                }
                nX += 2;
                nY += 2;
                if ((nX >= 0) && (nX <= 4) && (nY >= 0) && (nY <= 4))
                {
                    btDir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, BaseObject.m_nCurrX, BaseObject.m_nCurrY);
                    if (M2Share.g_Config.SpitMap[btDir, nY, nX] == 1)
                    {
                        result = true;
                    }
                }
            }
            return result;
        }

        
        
        
        
        protected ushort RecalcBagWeight()
        {
            ushort result = 0;
            TUserItem UserItem;
            GoodItem StdItem;
            for (int i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                if (StdItem != null)
                {
                    // Native sub_73E8D4: for pile items (byte[item+0x14]==7), multiply Weight by Dura
                    // Equivalent: StdMode >= 150 && runtime type TBasePileItem
                    if (NativeItemFactory.IsPileItem(StdItem))
                    {
                        result = unchecked((ushort)(result + StdItem.Weight * UserItem.Dura));
                    }
                    else
                    {
                        result += StdItem.Weight;
                    }
                }
            }
            return result;
        }

        
        
        
        private void RecalcHitSpeed()
        {
            TUserMagic UserMagic;
            if (m_btJob == 3)
            {
                m_btHitPoint = 0;
                m_btSpeedPoint = 0;
                m_wSpeedPoint = 0;
                m_nHitPlus = 0;
                m_nHitDouble = 0;
                return;
            }
            TNakedAbility BonusTick = null;
            switch (m_btJob)
            {
                case M2Share.jWarr:
                    BonusTick = M2Share.g_Config.BonusAbilofWarr;
                    break;
                case M2Share.jWizard:
                    BonusTick = M2Share.g_Config.BonusAbilofWizard;
                    break;
                case M2Share.jTaos:
                    BonusTick = M2Share.g_Config.BonusAbilofTaos;
                    break;
            }
            m_btHitPoint = (byte)(M2Share.DEFHIT + m_BonusAbil.Hit / BonusTick.Hit);
            int speedPoint;
            switch (m_btJob)
            {
                case M2Share.jTaos:
                    speedPoint = M2Share.DEFSPEED +
                        m_BonusAbil.Speed / BonusTick.Speed + 3;
                    break;
                default:
                    speedPoint = M2Share.DEFSPEED +
                        m_BonusAbil.Speed / BonusTick.Speed;
                    break;
            }
            m_btSpeedPoint = unchecked((byte)speedPoint);
            m_wSpeedPoint = unchecked((ushort)speedPoint);
            m_nHitPlus = 0;
            m_nHitDouble = 0;
            for (int i = 0; i < m_MagicList.Count; i++)
            {
                UserMagic = m_MagicList[i];
                if (UserMagic.wMagIdx < m_MagicArr.Length)
                    m_MagicArr[UserMagic.wMagIdx] = UserMagic;
                switch (UserMagic.wMagIdx)
                {
                    case SpellsDef.SKILL_ONESWORD:// 基本剑法
                        if (UserMagic.btLevel > 0)
                        {
                            m_btHitPoint = (byte)(m_btHitPoint + HUtil32.Round(9 / 3 * UserMagic.btLevel));
                        }
                        break;
                    case SpellsDef.SKILL_ILKWANG:// 精神力战法
                        if (UserMagic.btLevel > 0)
                        {
                            m_btHitPoint = (byte)(m_btHitPoint + HUtil32.Round(8.0 / 3.0 * UserMagic.btLevel));
                        }
                        break;
                    case SpellsDef.SKILL_YEDO:// 攻杀剑法
                        if (UserMagic.btLevel > 0)
                        {
                            m_btHitPoint = (byte)(m_btHitPoint + HUtil32.Round(3 / 3 * UserMagic.btLevel));
                        }
                        m_nHitPlus = (byte)(M2Share.DEFHIT + UserMagic.btLevel);
                        m_btAttackSkillCount = (byte)(7 - UserMagic.btLevel);
                        m_btAttackSkillPointCount = (byte)M2Share.RandomNumber.Random(m_btAttackSkillCount);
                        break;
                    case SpellsDef.SKILL_FIRESWORD:// 烈火剑法
                        m_nHitDouble = (byte)(4 + UserMagic.btLevel * 4);
                        break;
                }
            }
        }

        public void AddItemSkill(int nIndex)
        {
            TMagic Magic = null;
            switch (nIndex)
            {
                case 1:
                    Magic = this is HeroObject
                        ? M2Share.UserEngine.FindHeroMagic(
                            M2Share.g_Config.sFireBallSkill)
                        : M2Share.UserEngine.FindMagic(
                            M2Share.g_Config.sFireBallSkill);
                    break;
                case 2:
                    Magic = this is HeroObject
                        ? M2Share.UserEngine.FindHeroMagic(
                            M2Share.g_Config.sHealSkill)
                        : M2Share.UserEngine.FindMagic(
                            M2Share.g_Config.sHealSkill);
                    break;
            }
            if (Magic != null)
            {
                if (!IsTrainingSkill(Magic.wMagicID))
                {
                    var UserMagic = new TUserMagic
                    {
                        MagicInfo = Magic,
                        wMagIdx = Magic.wMagicID,
                        btKey = 0,
                        btLevel = 1,
                        nTranPoint = 0
                    };
                    m_MagicList.Add(UserMagic);
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        (this as TPlayObject).SendAddMagic(UserMagic);
                    }
                }
            }
        }

        private bool AddToMap()
        {
            bool result;
            object Point = m_PEnvir.AddToMap(m_nCurrX, m_nCurrY, CellType.OS_MOVINGOBJECT, this);
            if (Point != null)
            {
                result = true;
            }
            else
            {
                result = false;
            }
            if (!m_boFixedHideMode)
            {
                SendRefMsg(Grobal2.RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, 0, GetShowName());
            }
            return result;
        }

        
        
        
        
        
        // Native sub_4C8888 — the ONLY native MP-cost producer. It returns the COMPLETE
        // cost including btDefSpell (@0x4C88BA `mov dl,[esi+0x17]` / @0x4C88BD `add ax,dx`),
        // and every one of its 18 call sites consumes AX directly with no further add
        // (e.g. @0x6BC837 / @0x6BC8B3 / @0x6BC974 `movzx eax,ax` then straight into the
        // `cmp eax,[esi+0x2B4]` MP check for the three 半月/烈火半月/圆月 branches of the
        // native attack-direction handler; @0x4C8511 `mov word[ebx+0x18],ax` for the
        // packet field). The divisor is the float32 4.0 at [0x4C88C8], NOT (btTrainLv + 1)
        // — btTrainLv (+0x1A) is never read in the body; it is only the level cap.
        // Rounding is sub_403574 = fistp qword = round-half-to-even = HUtil32.Round.
        // Callers must therefore NOT add btDefSpell themselves.
        // See staging/heromagic_mpcost_fix_20260804.md §B.
        public short GetMagicSpell(TUserMagic UserMagic)
        {
            return unchecked((short)TPlayObject.GetNativeMagicProducerMpCost(UserMagic));
        }

        private void CheckPKStatus()
        {
            if (m_boPKFlag && ((HUtil32.GetTickCount() - m_dwPKTick) > M2Share.g_Config.dwPKFlagTime))// 60 * 1000
            {
                m_boPKFlag = false;
                RefNameColor();
            }
        }

        
        
        
        
        public void DamageSpell(ushort nSpellPoint)
        {
            if (nSpellPoint > 0)
            {
                if ((m_WAbil.MP - nSpellPoint) > 0)
                {
                    m_WAbil.MP -= nSpellPoint;
                }
                else
                {
                    m_WAbil.MP = 0;
                }
            }
            else
            {
                if ((m_WAbil.MP - nSpellPoint) < m_WAbil.MaxMP)
                {
                    m_WAbil.MP -= nSpellPoint;
                }
                else
                {
                    m_WAbil.MP = m_WAbil.MaxMP;
                }
            }
        }

        public void DelItemSkill_DeleteSkill(string sSkillName)
        {
            TUserMagic UserMagic;
            TPlayObject PlayObject;
            for (int i = 0; i < m_MagicList.Count; i++)
            {
                UserMagic = m_MagicList[i];
                if (UserMagic.MagicInfo.sMagicName == sSkillName)
                {
                    PlayObject = this as TPlayObject;
                    PlayObject.SendDelMagic(UserMagic);
                    UserMagic = null;
                    m_MagicList.RemoveAt(i);
                    break;
                }
            }
        }

        public void DelItemSkill(int nIndex)
        {
            if (m_btRaceServer != Grobal2.RC_PLAYOBJECT)
            {
                return;
            }
            switch (nIndex)
            {
                case 1:
                    if (m_btJob != M2Share.jWizard)
                    {
                        DelItemSkill_DeleteSkill(M2Share.g_Config.sFireBallSkill);
                    }
                    break;
                case 2:
                    if (m_btJob != M2Share.jTaos)
                    {
                        DelItemSkill_DeleteSkill(M2Share.g_Config.sHealSkill);
                    }
                    break;
            }
        }

        public void DelMember(TBaseObject BaseObject)
        {
            TPlayObject PlayObject;
            if (m_GroupOwner != BaseObject)
            {
                for (int i = 0; i < m_GroupMembers.Count; i++)
                {
                    if (m_GroupMembers[i] == BaseObject)
                    {
                        BaseObject.LeaveGroup();
                        m_GroupMembers.RemoveAt(i);
                        break;
                    }
                }
            }
            else
            {
                for (int i = m_GroupMembers.Count - 1; i >= 0; i--)
                {
                    m_GroupMembers[i].LeaveGroup();
                    m_GroupMembers.RemoveAt(i);
                }
            }
            PlayObject = this as TPlayObject;
            if (!PlayObject.CancelGroup())
            {
                PlayObject.SendDefMessage(Grobal2.SM_GROUPCANCEL, 0, 0, 0, 0, "");
            }
            else
            {
                PlayObject.SendGroupMembers();
            }
        }

        public void DoDamageWeapon(int nWeaponDamage)
        {
            // MINE-43: 原版 sub_73E804 在取到武器后立刻判耐久：
            //   0x73E829  0F B7 73 26              movzx esi, word [ebx+0x26]   ; Dura
            //   0x73E82D  85 F6                    test esi, esi
            //   0x73E82F  0F 8E 93 00 00 00        jle 0x73E8C8                 ; <=0 直接返回
            // (0x7845A0: 66 8B 40 26 C3 证明 item+0x26 = Dura)
            if (m_UseItems[Grobal2.U_WEAPON] == null || m_UseItems[Grobal2.U_WEAPON].wIndex <= 0
                || m_UseItems[Grobal2.U_WEAPON].Dura <= 0)
            {
                return;
            }
            int nDura = m_UseItems[Grobal2.U_WEAPON].Dura;
            // DURA-04: Native uses 1000.0 divisor (EA 0x73E8D0: float32 1000.0), not 1.03
            // Display threshold: Round(Dura / 1000.0) with half-even rounding (@ROUND at EA 0x403574)
            var nDuraPoint = HUtil32.Round(nDura / 1000.0);
            nDura -= nWeaponDamage;
            if (nDura <= 0)
            {
                nDura = 0;
                m_UseItems[Grobal2.U_WEAPON].Dura = (ushort)nDura;
                // 原版 slot 1 (sub_73E804) 到此为止：0x73E850 mov word [ebx+0x26],0
                // 全函数无 0x75F27C(清槽)/0x404690(释放) ⇒ 武器留在身上，0 耐久。
                // 下面的销毁分支是【非原版行为】，仅在运营方显式开启时执行。
                if (M2Share.g_Config.boDeleteWeaponOnZeroDura && m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    var PlayObject = this as TPlayObject;
                    PlayObject.SendDelItems(m_UseItems[Grobal2.U_WEAPON]);
                    var StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_WEAPON].wIndex);
                    if (StdItem.NeedIdentify == 1)
                    {
                        M2Share.AddGameDataLog('3' + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + m_UseItems[Grobal2.U_WEAPON].MakeIndex + "\t" + HUtil32.BoolToIntStr(m_btRaceServer == Grobal2.RC_PLAYOBJECT) + "\t" + '0');
                    }
                    // 发包必须先于清空 wIndex：原先的顺序在 wIndex=0 之后才读
                    // m_UseItems[U_WEAPON].DuraMax，属 use-after-clear。
                    SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_WEAPON, nDura, m_UseItems[Grobal2.U_WEAPON].DuraMax, 0, "");
                    m_UseItems[Grobal2.U_WEAPON].wIndex = 0;
                }
                else
                {
                    SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_WEAPON, nDura, m_UseItems[Grobal2.U_WEAPON].DuraMax, 0, "");
                }
            }
            else
            {
                m_UseItems[Grobal2.U_WEAPON].Dura = (ushort)nDura;
            }
            // MINE-44: 原版两侧都取 ROUND(dura/1000.0)，再判「显示值是否变小」：
            //   0x73E838  DB 45 F4 / D8 35 D0 E8 73 00 / E8 2E 4D CC FF  旧值 fild,fdiv,ROUND
            //   0x73E893  DB 45 F4 / D8 35 D0 E8 73 00 / E8 D3 4C CC FF  新值 fild,fdiv,ROUND
            //   0x73E8A1  8B 55 F8   mov edx,[ebp-8]   ; 旧显示值
            //   0x73E8A4  2B D0      sub edx, eax      ; 旧-新
            //   0x73E8A6  4A         dec edx
            //   0x73E8A7  7C 1F      jl 0x73E8C8       ; (旧-新-1)<0 则不发包
            // 即发包条件是「旧 > 新」而非「旧 != 新」。0x73E8D0 = 00 00 7A 44 = float32(1000.0)。
            if (nDuraPoint > HUtil32.Round(nDura / 1000.0))
            {
                SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_WEAPON, m_UseItems[Grobal2.U_WEAPON].Dura, m_UseItems[Grobal2.U_WEAPON].DuraMax, 0, "");
            }
        }

        protected byte GetCharColor(TBaseObject BaseObject)
        {
            TUserCastle Castle;
            byte result = BaseObject.GetNamecolor();
            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                if (BaseObject.PKLevel() < 2)
                {
                    if (BaseObject.m_boPKFlag)
                    {
                        result = M2Share.g_Config.btPKFlagNameColor;
                    }
                    var n10 = GetGuildRelation(this, BaseObject);
                    switch (n10)
                    {
                        case 1:
                        case 3:
                            result = M2Share.g_Config.btAllyAndGuildNameColor;
                            break;
                        case 2:
                            result = M2Share.g_Config.btWarGuildNameColor;
                            break;
                    }
                    if (BaseObject.m_PEnvir.Flag.boFight3Zone)
                    {
                        if (m_MyGuild == BaseObject.m_MyGuild)
                        {
                            result = M2Share.g_Config.btAllyAndGuildNameColor;
                        }
                        else
                        {
                            result = M2Share.g_Config.btWarGuildNameColor;
                        }
                    }
                }
                Castle = M2Share.CastleManager.InCastleWarArea(BaseObject);
                if ((Castle != null) && Castle.m_boUnderWar && m_boInFreePKArea && BaseObject.m_boInFreePKArea)
                {
                    result = M2Share.g_Config.btInFreePKAreaNameColor;
                    m_boGuildWarArea = true;
                    if (m_MyGuild == null)
                    {
                        return result;
                    }
                    if (Castle.IsMasterGuild(m_MyGuild))
                    {
                        if ((m_MyGuild == BaseObject.m_MyGuild) || m_MyGuild.IsAllyGuild(BaseObject.m_MyGuild))
                        {
                            result = M2Share.g_Config.btAllyAndGuildNameColor;
                        }
                        else
                        {
                            if (Castle.IsAttackGuild(BaseObject.m_MyGuild))
                            {
                                result = M2Share.g_Config.btWarGuildNameColor;
                            }
                        }
                    }
                    else
                    {
                        if (Castle.IsAttackGuild(m_MyGuild))
                        {
                            if ((m_MyGuild == BaseObject.m_MyGuild) || m_MyGuild.IsAllyGuild(BaseObject.m_MyGuild))
                            {
                                result = M2Share.g_Config.btAllyAndGuildNameColor;
                            }
                            else
                            {
                                if (Castle.IsMember(BaseObject))
                                {
                                    result = M2Share.g_Config.btWarGuildNameColor;
                                }
                            }
                        }
                    }
                }
            }
            else if (BaseObject.m_btRaceServer == Grobal2.RC_NPC) 
            {
                result = M2Share.g_Config.NpcNameColor;
                if (BaseObject.m_boCrazyMode) 
                {
                    result = 0xF9;
                }
                if (BaseObject.m_boHolySeize) 
                {
                    result = 0x7D;
                }
            }
            else
            {
                if (BaseObject.m_btSlaveExpLevel <= Grobal2.SLAVEMAXLEVEL)
                {
                    result = M2Share.g_Config.SlaveColor[BaseObject.m_btSlaveExpLevel];
                }
                else
                {
                    result = 255;
                }
                if (BaseObject.m_boCrazyMode)
                {
                    result = 0xF9;
                }
                if (BaseObject.m_boHolySeize)
                {
                    result = 0x7D;
                }
            }
            return result;
        }

        public int GetLevelExp(int nLevel)
        {
            int result;
            if (nLevel <= Grobal2.MAXLEVEL)
            {
                result = M2Share.g_Config.dwNeedExps[nLevel];
            }
            else
            {
                result = M2Share.g_Config.dwNeedExps[M2Share.g_Config.dwNeedExps.GetUpperBound(0)];
            }
            return result;
        }

        private byte GetNamecolor()
        {
            byte result = m_btNameColor;
            if (PKLevel() == 1)
            {
                result = M2Share.g_Config.btPKLevel1NameColor;
            }
            if (PKLevel() >= 2)
            {
                result = M2Share.g_Config.btPKLevel2NameColor;
            }
            return result;
        }

        public void HearMsg(string sMsg)
        {
            if (!string.IsNullOrEmpty(sMsg))
            {
                SendMsg(null, Grobal2.RM_HEAR, 0, M2Share.g_Config.btHearMsgFColor, M2Share.g_Config.btHearMsgBColor, 0, sMsg);
            }
        }

        protected bool InSafeArea()
        {
            bool result = false;
            int n14;
            int n18;
            if (m_PEnvir == null)
            {
                return result;
            }
            result = m_PEnvir.Flag.boSAFE;
            if (result)
            {
                return result;
            }
            for (int i = 0; i < M2Share.StartPointList.Count; i++)
            {
                if (M2Share.StartPointList[i].m_sMapName == m_PEnvir.sMapName)
                {
                    if (M2Share.StartPointList[i] != null)
                    {
                        n14 = M2Share.StartPointList[i].m_nCurrX;
                        n18 = M2Share.StartPointList[i].m_nCurrY;
                        if ((Math.Abs(m_nCurrX - n14) <= 60) && (Math.Abs(m_nCurrY - n18) <= 60))
                        {
                            result = true;
                        }
                    }
                }
            }
            return result;
        }

        public void MonsterRecalcAbilitys()
        {
            m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC), HUtil32.HiWord(m_Abil.DC));
            long n8 = 0;
            if ((m_btRaceServer == M2Share.MONSTER_WHITESKELETON) || (m_btRaceServer == M2Share.MONSTER_ELFMONSTER) || (m_btRaceServer == M2Share.MONSTER_ELFWARRIOR))
            {
                m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC), HUtil32.Round((m_btSlaveExpLevel * 0.1 + 0.3) * 3.0 * m_btSlaveExpLevel + HUtil32.HiWord(m_WAbil.DC)));
                n8 = n8 + HUtil32.Round((m_btSlaveExpLevel * 0.1 + 0.3) * m_Abil.MaxHP) * m_btSlaveExpLevel;
                n8 = n8 + m_Abil.MaxHP;
                if (m_btSlaveExpLevel > 0)
                {
                    m_WAbil.MaxHP = ClampAbility(n8);
                }
                else
                {
                    m_WAbil.MaxHP = m_Abil.MaxHP;
                }
            }
            else
            {
                n8 = m_Abil.MaxHP;
                m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC), HUtil32.Round(m_btSlaveExpLevel * 2 + HUtil32.HiWord(m_WAbil.DC)));
                n8 = n8 + HUtil32.Round(m_Abil.MaxHP * 0.15) * m_btSlaveExpLevel;
                m_WAbil.MaxHP = ClampAbility(Math.Min(
                    (long)m_Abil.MaxHP + m_btSlaveExpLevel * 60L, n8));
            }
        }

        internal static int ClampAbility(long value)
        {
            return (int)Math.Clamp(value, 0, int.MaxValue);
        }

        public void SendFirstMsg(TBaseObject BaseObject, short wIdent, short wParam, int lParam1, int lParam2, int lParam3, string sMsg)
        {
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                if (!m_boGhost)
                {
                    SendMessage = new SendMessage
                    {
                        wIdent = wIdent,
                        wParam = wParam,
                        nParam1 = lParam1,
                        nParam2 = lParam2,
                        nParam3 = lParam3,
                        dwDeliveryTime = 0,
                        BaseObject = BaseObject
                    };
                    if (!string.IsNullOrEmpty(sMsg))
                    {
                        SendMessage.Buff = sMsg;
                    }
                    m_MsgList.Insert(0, SendMessage);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        public void SendMsg(TBaseObject BaseObject, int wIdent, int wParam, int nParam1, int nParam2, int nParam3,
            string sMsg, object payload = null)
        {
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                if (!m_boGhost)
                {
                    SendMessage = new SendMessage
                    {
                        wIdent = wIdent,
                        wParam = wParam,
                        nParam1 = nParam1,
                        nParam2 = nParam2,
                        nParam3 = nParam3,
                        dwDeliveryTime = 0,
                        BaseObject = BaseObject,
                        boLateDelivery = false,
                        Payload = payload
                    };
                    if (!string.IsNullOrEmpty(sMsg))
                    {
                        SendMessage.Buff = sMsg;
                    }
                    m_MsgList.Add(SendMessage);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        
        
        
        public void SendDelayMsg(TBaseObject BaseObject, int wIdent, int wParam, int lParam1, int lParam2, int lParam3,
            string sMsg, int dwDelay, object payload = null)
        {
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                if (!m_boGhost)
                {
                    SendMessage = new SendMessage
                    {
                        wIdent = wIdent,
                        wParam = wParam,
                        nParam1 = lParam1,
                        nParam2 = lParam2,
                        nParam3 = lParam3,
                        dwDeliveryTime = HUtil32.GetTickCount() + dwDelay,
                        BaseObject = BaseObject,
                        boLateDelivery = true,
                        Payload = payload
                    };
                    if (!string.IsNullOrEmpty(sMsg))
                    {
                        SendMessage.Buff = sMsg;
                    }
                    m_MsgList.Add(SendMessage);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        
        
        
        public void SendDelayMsg(int BaseObject, short wIdent, int wParam, int lParam1, int lParam2, int lParam3, string sMsg, int dwDelay)
        {
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                if (!m_boGhost)
                {
                    SendMessage = new SendMessage
                    {
                        wIdent = wIdent,
                        wParam = wParam,
                        nParam1 = lParam1,
                        nParam2 = lParam2,
                        nParam3 = lParam3,
                        dwDeliveryTime = HUtil32.GetTickCount() + dwDelay,
                        boLateDelivery = true
                    };
                    if (BaseObject == Grobal2.RM_STRUCK)
                    {
                        SendMessage.ObjectId = Grobal2.RM_STRUCK;
                    }
                    else
                    {
                        SendMessage.BaseObject = M2Share.ObjectManager.Get(BaseObject);
                    }
                    if (!string.IsNullOrEmpty(sMsg))
                    {
                        SendMessage.Buff = sMsg;
                    }
                    m_MsgList.Add(SendMessage);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        private void SendUpdateDelayMsg(TBaseObject BaseObject, short wIdent, short wParam, int lParam1, int lParam2, int lParam3, string sMsg, int dwDelay)
        {
            SendMessage SendMessage;
            int i;
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                i = 0;
                while (true)
                {
                    if (m_MsgList.Count <= i)
                    {
                        break;
                    }
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent != wIdent)
                    {
                        i++;
                        continue;
                    }
                    if (SendMessage.nParam1 == lParam1)
                    {
                        if (wIdent == Grobal2.RM_MAGIC_LVEXP && !SendMessage.boLateDelivery)
                        {
                            i++;
                            continue;
                        }
                        m_MsgList.RemoveAt(i);
                        Dispose(SendMessage);
                        continue;
                    }
                    if (wIdent == Grobal2.RM_MAGIC_LVEXP && SendMessage.boLateDelivery)
                    {
                        SendMessage.dwDeliveryTime = 0;
                        SendMessage.boLateDelivery = false;
                        m_MsgList[i] = SendMessage;
                    }
                    i++;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            SendDelayMsg(BaseObject, wIdent, wParam, lParam1, lParam2, lParam3, sMsg, dwDelay);
        }

        public void SendUpdateMsg(TBaseObject BaseObject, int wIdent, int wParam, int lParam1, int lParam2, int lParam3, string sMsg, object payload = null)
        {
            SendMessage SendMessage;
            int i;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                i = 0;
                while (true)
                {
                    if (m_MsgList.Count <= i)
                    {
                        break;
                    }
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == wIdent)
                    {
                        m_MsgList.RemoveAt(i);
                        Dispose(SendMessage);
                        continue;
                    }
                    i++;
                }
            }
            finally
            {

                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            SendMsg(BaseObject, wIdent, wParam, lParam1, lParam2, lParam3, sMsg, payload);
        }

        public void SendActionMsg(TBaseObject BaseObject, int wIdent, int wParam, int lParam1, int lParam2, int lParam3, string sMsg)
        {
            SendMessage SendMessage;
            int i;
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                i = 0;
                while (true)
                {
                    if (m_MsgList.Count <= i)
                    {
                        break;
                    }
                    SendMessage = m_MsgList[i];
                    if ((SendMessage.wIdent == Grobal2.CM_TURN) || (SendMessage.wIdent == Grobal2.CM_WALK) || (SendMessage.wIdent == Grobal2.CM_SITDOWN) || (SendMessage.wIdent == Grobal2.CM_HORSERUN) || (SendMessage.wIdent == Grobal2.CM_RUN) || (SendMessage.wIdent == Grobal2.CM_HIT) || (SendMessage.wIdent == Grobal2.CM_HEAVYHIT) || (SendMessage.wIdent == Grobal2.CM_BIGHIT) || (SendMessage.wIdent == Grobal2.CM_POWERHIT) || (SendMessage.wIdent == Grobal2.CM_LONGHIT) || (SendMessage.wIdent == Grobal2.CM_WIDEHIT) || (SendMessage.wIdent == Grobal2.CM_FIREHIT))
                    {
                        m_MsgList.RemoveAt(i);
                        Dispose(SendMessage);
                        continue;
                    }
                    i++;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            SendMsg(BaseObject, wIdent, wParam, lParam1, lParam2, lParam3, sMsg);
        }

        protected virtual bool GetMessage(ref TProcessMessage Msg)
        {
            bool result = false;
            int I;
            SendMessage SendMessage;
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                I = 0;
                while (m_MsgList.Count > I)
                {
                    if (m_MsgList.Count <= I)
                    {
                        break;
                    }
                    SendMessage = m_MsgList[I];
                    if ((SendMessage.dwDeliveryTime != 0) && (HUtil32.GetTickCount() < SendMessage.dwDeliveryTime))//延时消息
                    {
                        I++;
                        continue;
                    }
                    m_MsgList.RemoveAt(I);
                    Msg = new TProcessMessage();
                    Msg.wIdent = SendMessage.wIdent;
                    Msg.wParam = SendMessage.wParam;
                    Msg.nParam1 = SendMessage.nParam1;
                    Msg.nParam2 = SendMessage.nParam2;
                    Msg.nParam3 = SendMessage.nParam3;
                    if (SendMessage.BaseObject != null)
                    {
                        Msg.BaseObject = SendMessage.BaseObject.ObjectId;
                    }
                    else if (SendMessage.ObjectId > 0)
                    {
                        Msg.BaseObject = SendMessage.ObjectId;
                    }
                    Msg.dwDeliveryTime = SendMessage.dwDeliveryTime;
                    Msg.boLateDelivery = SendMessage.boLateDelivery;
                    Msg.Payload = SendMessage.Payload;
                    if (!string.IsNullOrEmpty(SendMessage.Buff))
                    {
                        Msg.sMsg = SendMessage.Buff;
                    }
                    else
                    {
                        Msg.sMsg = string.Empty;
                    }
                    result = true;
                    break;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        public bool GetMapBaseObjects(Envirnoment tEnvir, int nX, int nY, int nRage, IList<TBaseObject> rList)
        {
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            TBaseObject BaseObject;
            const string sExceptionMsg = "[Exception] TBaseObject::GetMapBaseObjects";
            if (rList == null)
            {
                return false;
            }
            try
            {
                int nStartX = nX - nRage;
                int nEndX = nX + nRage;
                int nStartY = nY - nRage;
                int nEndY = nY + nRage;
                for (var x = nStartX; x <= nEndX; x++)
                {
                    for (var y = nStartY; y <= nEndY; y++)
                    {
                        var mapCell = false;
                        MapCellInfo = tEnvir.GetMapCellInfo(x, y, ref mapCell);
                        if (mapCell && (MapCellInfo.ObjList != null))
                        {
                            for (var j = 0; j < MapCellInfo.Count; j++)
                            {
                                OSObject = MapCellInfo.ObjList[j];
                                if ((OSObject != null) && (OSObject.CellType == CellType.OS_MOVINGOBJECT))
                                {
                                    BaseObject = OSObject.CellObj as TBaseObject;
                                    if ((BaseObject != null) && (!BaseObject.m_boDeath) && (!BaseObject.m_boGhost))
                                    {
                                        rList.Add(BaseObject);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
            return true;
        }

        public void SendRefMsg(int wIdent, int wParam, int nParam1, int nParam2, int nParam3, string sMsg, object payload = null)
        {
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            TBaseObject BaseObject;
            const string sExceptionMsg = "[Exception] TBaseObject::SendRefMsg Name = {0}";
            if (m_PEnvir == null)
            {
                M2Share.ErrorMessage(m_sCharName + " SendRefMsg nil PEnvir ");
                return;
            }
            if (m_boObMode || m_boFixedHideMode)
            {
                SendMsg(this, wIdent, wParam, nParam1, nParam2, nParam3, sMsg, payload); 
                return;
            }
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                if (((HUtil32.GetTickCount() - m_SendRefMsgTick) >= 500) || (m_VisibleHumanList.Count == 0))
                {
                    m_SendRefMsgTick = HUtil32.GetTickCount();
                    m_VisibleHumanList.Clear();
                    var nLX = m_nCurrX - M2Share.g_Config.nSendRefMsgRange; 
                    var nHX = m_nCurrX + M2Share.g_Config.nSendRefMsgRange; 
                    var nLY = m_nCurrY - M2Share.g_Config.nSendRefMsgRange; 
                    var nHY = m_nCurrY + M2Share.g_Config.nSendRefMsgRange; 
                    for (var nCX = nLX; nCX <= nHX; nCX++)
                    {
                        for (var nCY = nLY; nCY <= nHY; nCY++)
                        {
                            var mapCell = false;
                            MapCellInfo = m_PEnvir.GetMapCellInfo(nCX, nCY, ref mapCell);
                            if (mapCell)
                            {
                                if (MapCellInfo.ObjList != null)
                                {
                                    for (var i = MapCellInfo.Count - 1; i >= 0; i--)
                                    {
                                        OSObject = MapCellInfo.ObjList[i];
                                        if (OSObject != null)
                                        {
                                            if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                                            {
                                                if ((HUtil32.GetTickCount() - OSObject.dwAddTime) >= 60 * 1000)
                                                {
                                                    OSObject = null;
                                                    MapCellInfo.Remove(i);
                                                    if (MapCellInfo.Count <= 0)
                                                    {
                                                        m_PEnvir.ReleaseCellObjectList(nCX, nCY);
                                                        break;
                                                    }
                                                }
                                                else
                                                {
                                                    try
                                                    {
                                                        BaseObject = OSObject.CellObj as TBaseObject;
                                                        if ((BaseObject != null) && !BaseObject.m_boGhost)
                                                        {
                                                            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                                            {
                                                                BaseObject.SendMsg(this, wIdent, wParam, nParam1, nParam2, nParam3, sMsg, payload);
                                                                m_VisibleHumanList.Add(BaseObject);
                                                            }
                                                            else if (BaseObject.m_boWantRefMsg)
                                                            {
                                                                if ((wIdent == Grobal2.RM_STRUCK) || (wIdent == Grobal2.RM_HEAR) || (wIdent == Grobal2.RM_DEATH))
                                                                {
                                                                    BaseObject.SendMsg(this, wIdent, wParam, nParam1, nParam2, nParam3, sMsg, payload);
                                                                    m_VisibleHumanList.Add(BaseObject);
                                                                }
                                                            }
                                                        }
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        MapCellInfo.Remove(i);
                                                        if (MapCellInfo.Count <= 0)
                                                        {
                                                            m_PEnvir.ReleaseCellObjectList(nCX, nCY);
                                                        }
                                                        M2Share.ErrorMessage(format(sExceptionMsg, m_sCharName));
                                                        M2Share.ErrorMessage(e.Message);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return;
                }
                for (var nC = 0; nC < m_VisibleHumanList.Count; nC++)
                {
                    BaseObject = m_VisibleHumanList[nC];
                    if (BaseObject.m_boGhost)
                    {
                        continue;
                    }
                    if ((BaseObject.m_PEnvir == m_PEnvir) && (Math.Abs(BaseObject.m_nCurrX - m_nCurrX) < 11) && (Math.Abs(BaseObject.m_nCurrY - m_nCurrY) < 11))
                    {
                        if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            BaseObject.SendMsg(this, wIdent, wParam, nParam1, nParam2, nParam3, sMsg, payload);
                        }
                        else if (BaseObject.m_boWantRefMsg)
                        {
                            if ((wIdent == Grobal2.RM_STRUCK) || (wIdent == Grobal2.RM_HEAR) || (wIdent == Grobal2.RM_DEATH))
                            {
                                BaseObject.SendMsg(this, wIdent, wParam, nParam1, nParam2, nParam3, sMsg, payload);
                            }
                        }
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        public int GetFeatureToLong()
        {
            return GetFeature(null);
        }

        public byte[] GetMobileFeature()
        {
            ushort weapon = 0;
            ushort dress = 0;

            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT || m_btRaceServer == Grobal2.RC_HEROOBJECT)
            {
                if (m_UseItems[Grobal2.U_WEAPON] != null && m_UseItems[Grobal2.U_WEAPON].wIndex > 0)
                {
                    var stdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_WEAPON].wIndex);
                    if (stdItem != null)
                    {
                        weapon = stdItem.Shape;
                    }
                }

                if (m_UseItems[Grobal2.U_DRESS] != null && m_UseItems[Grobal2.U_DRESS].wIndex > 0)
                {
                    var stdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_DRESS].wIndex);
                    if (stdItem != null)
                    {
                        dress = stdItem.Shape;
                    }
                }
            }
            else
            {
                weapon = m_btMonsterWeapon;
                dress = m_wAppr;
            }

            return BuildMobileFeatureRecord(
                m_btRaceImg,
                (byte)(m_btGender == PlayGender.Man ? 0 : 1),
                m_btHair,
                weapon,
                dress,
                (ushort)(m_boOnHorse ? m_btHorseType : 0));
        }

        private static byte[] BuildMobileFeatureRecord(ushort race, byte sex, byte hair,
            ushort weapon, ushort dress, ushort horse)
        {
            using var memoryStream = new MemoryStream(10);
            using var writer = new BinaryWriter(memoryStream);
            writer.Write(race);
            writer.Write(sex);
            writer.Write(hair);
            writer.Write(weapon);
            writer.Write(dress);
            writer.Write(horse);
            return memoryStream.ToArray();
        }

        public ushort GetFeatureEx()
        {
            ushort result;
            if (m_boOnHorse)
            {
                result = HUtil32.MakeWord(m_btHorseType, m_btDressEffType);
            }
            else
            {
                result = HUtil32.MakeWord(0, m_btDressEffType);
            }
            return result;
        }

        public int GetFeature(TBaseObject BaseObject)
        {
            int result;
            GoodItem StdItem;
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT || m_btRaceServer == Grobal2.RC_HEROOBJECT)
            {
                byte nDress = 0;
                if (m_UseItems[Grobal2.U_DRESS] != null && m_UseItems[Grobal2.U_DRESS].wIndex > 0)// 衣服
                {
                    StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_DRESS].wIndex);
                    if (StdItem != null)
                    {
                        nDress = (byte)(StdItem.Shape * 2);
                    }
                }
                nDress += (byte)m_btGender;
                byte nWeapon = 0;
                if (m_UseItems[Grobal2.U_WEAPON] != null && m_UseItems[Grobal2.U_WEAPON].wIndex > 0)// 武器
                {
                    StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_WEAPON].wIndex);
                    if (StdItem != null)
                    {
                        nWeapon = (byte)(StdItem.Shape * 2);
                    }
                }
                nWeapon += (byte)m_btGender;
                byte nHair = (byte)(m_btHair * 2 + (byte)m_btGender);
                result = Grobal2.MakeHumanFeature(0, nDress, nWeapon, nHair);
                return result;
            }
            bool bo25 = false;
            if ((BaseObject != null) && BaseObject.m_boRaceImg)
            {
                bo25 = true;
            }
            if (bo25)
            {
                byte nRaceImg = m_btRaceImg;
                byte nAppr = (byte)m_wAppr;
                switch (nAppr)
                {
                    case 0:
                        nRaceImg = 12;
                        nAppr = 5;
                        break;
                    case 1:
                        nRaceImg = 11;
                        nAppr = 9;
                        break;
                    case 160:
                        nRaceImg = 10;
                        nAppr = 0;
                        break;
                    case 161:
                        nRaceImg = 10;
                        nAppr = 1;
                        break;
                    case 162:
                        nRaceImg = 11;
                        nAppr = 6;
                        break;
                    case 163:
                        nRaceImg = 11;
                        nAppr = 3;
                        break;
                }
                result = Grobal2.MakeMonsterFeature(nRaceImg, m_btMonsterWeapon, nAppr);
                return result;
            }
            result = Grobal2.MakeMonsterFeature(m_btRaceImg, m_btMonsterWeapon, m_wAppr);
            return result;
        }

        public int GetCharStatus()
        {
            long nStatus = 0;
            for (int i = m_wStatusTimeArr.GetLowerBound(0); i <= m_wStatusTimeArr.GetUpperBound(0); i++)
            {
                if (m_wStatusTimeArr[i] > 0)
                {
                    nStatus = (0x80000000 >> i) | nStatus;
                }
            }
            var status = (m_nCharStatusEx & NativePersistentLowStateMask) |
                         nStatus;
            // ✅ 结论已由战神侧独立支撑:战神从不"重建"状态字 —— sub_7729C4 直接把 [Self+0x168] 起的
            // 16 字节位集当 blob 发出(RefMsg 0x291=657),没有任何"超界归零"逻辑。故旧写法
            // `status >= int.MaxValue ? 0 : ...` 无论如何都不是战神语义,unchecked 截断是正确方向。
            // 证据: staging/statuslayer_migration_plan_20260803.md 行 80(GetCharStatus 一栏:
            // "native never rebuilds; sub_7729C4 just ships [+0x168]") 与 spec_statustable_
            // ghostsweep_20260803.md A.15.3(ident 657 匹配)。
            // 并列 ref 引用(保留,勿删;来源=GameOfMir 参考分支,非战神,仅算术形态线索):
            //   ObjBase.pas:20074 返回的是 32 位【截断】后的 Integer，不是"超界就归零"。
            // 旧 bug 的实际后果: 状态槽 0(POISON_DECHEALTH 绿毒)会置位 0x80000000 = 2147483648
            // > int.MaxValue，旧写法会把【整个状态字清零】——玩家一中绿毒，中毒/护体/隐身/防御等
            // 所有状态图标全部消失（且 Run tick 里高频 StatusChanged 广播）。
            // ⚠️ 但本方法【整体】仍是 C# 独有的合成层,战神没有对应函数:legacy 12 槽 overlay 占用
            // 线上 bit 20..31,与战神 state 20..31 撞位;`0x80000000 >> i` 的 slot→bit 映射【不是 bug】
            // (它就是客户端期望的线位,由 LoginProtocolExactCheck / NativeState26CompatCheck 断言),
            // 详见 statuslayer_migration_plan_20260803.md §2.3 —— 勿按 spec A.15.2 的"位反转"措辞去改。
            return unchecked((int)status);
        }

        public int GetBodyStateWord(int index)
        {
            return index switch
            {
                0 => m_nCharStatus,
                1 => m_nCharStatus2,
                2 => m_nCharStatus3,
                3 => m_nCharStatus4,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        public const int NativeActiveStateMax = 111;

        public bool HasNativeActiveState(int internalType)
        {
            if (internalType < 0 || internalType > NativeActiveStateMax)
                return false;

            if (internalType == NativeState26Type)
                return (m_nCharStatusEx & NativeState26Mask) != 0;

            var word = unchecked((uint)GetBodyStateWord(internalType / 32));
            var mask = 1u << (internalType % 32);
            return (word & mask) != 0;
        }

        public bool SetNativeActiveState(int internalType)
        {
            return internalType >= 0 && internalType <= NativeActiveStateMax &&
                   SetBodyState(internalType, true);
        }

        public bool ClearNativeActiveState(int internalType)
        {
            return internalType >= 0 && internalType <= NativeActiveStateMax &&
                   SetBodyState(internalType, false);
        }

        public void WriteBodyState(BinaryWriter writer)
        {
            writer.Write(m_nCharStatus);
            writer.Write(m_nCharStatus2);
            writer.Write(m_nCharStatus3);
            writer.Write(m_nCharStatus4);
        }

        public byte[] GetBodyStateBuffer()
        {
            var buffer = new byte[16];
            using var stream = new MemoryStream(buffer);
            using var writer = new BinaryWriter(stream);
            WriteBodyState(writer);
            return buffer;
        }

        public bool SetBodyState(int stateIndex, bool enabled)
        {
            if (stateIndex < 0 || stateIndex > 127)
                return false;

            var wordIndex = stateIndex / 32;
            var mask = 1 << (stateIndex % 32);
            if (stateIndex == NativeState26Type)
            {
                var wasEnabled = (m_nCharStatusEx & NativeState26Mask) != 0;
                if (wasEnabled == enabled)
                    return false;

                m_nCharStatusEx = enabled
                    ? m_nCharStatusEx | (uint)mask
                    : m_nCharStatusEx & ~(uint)mask;
                m_nCharStatus = GetCharStatus();
                return true;
            }

            var oldValue = GetBodyStateWord(wordIndex);
            var newValue = enabled ? oldValue | mask : oldValue & ~mask;
            if (oldValue == newValue)
                return false;

            switch (wordIndex)
            {
                case 0:
                    if (stateIndex <= 20)
                    {
                        m_nCharStatusEx = enabled
                            ? m_nCharStatusEx | (uint)mask
                            : m_nCharStatusEx & ~(uint)mask;
                        m_nCharStatus = GetCharStatus();
                    }
                    else
                    {
                        // States 21..31 remain owned by m_wStatusTimeArr. Raw writes
                        // affect the current body word only and the next status tick
                        // deliberately rebuilds them from that legacy authority.
                        m_nCharStatus = newValue;
                    }
                    break;
                case 1:
                    m_nCharStatus2 = newValue;
                    break;
                case 2:
                    m_nCharStatus3 = newValue;
                    break;
                case 3:
                    m_nCharStatus4 = newValue;
                    break;
            }
            return true;
        }

        public void AbilCopyToWAbil()
        {
            // Bug1 fix 2026-04-22: deep copy to prevent m_WAbil/m_Abil aliasing.
            m_WAbil.CopyFrom(m_Abil);
        }

        public virtual void Initialize()
        {
            TUserMagic UserMagic;
            AbilCopyToWAbil();
            for (int i = 0; i < m_MagicList.Count; i++)
            {
                UserMagic = m_MagicList[i];
                if (UserMagic.btLevel >= 4)
                {
                    UserMagic.btLevel = 0;
                }
            }
            m_boAddtoMapSuccess = true;
            if (m_PEnvir.CanWalk(m_nCurrX, m_nCurrY, true) && AddToMap())
            {
                m_boAddtoMapSuccess = false;
            }
            m_nCharStatus = GetCharStatus();
            AddBodyLuck(0);
            LoadSayMsg();
            if (M2Share.g_Config.boMonSayMsg)
            {
                MonsterSayMsg(null, MonStatus.MonGen);
            }
        }

        
        
        
        private void LoadSayMsg()
        {
            for (var i = 0; i < M2Share.g_MonSayMsgList.Count; i++)
            {
                if (M2Share.g_MonSayMsgList.TryGetValue(m_sCharName, out m_SayMsgList))
                {
                    break;
                }
            }
        }

        public virtual void Disappear()
        {
            ClearTimedAbilitiesOnExit();
        }

        public void FeatureChanged()
        {
            SendRefMsg(Grobal2.RM_FEATURECHANGED, 0, GetFeatureToLong(), 0, 0, "",
                GetMobileFeature());
        }

        public void StatusChanged()
        {
            SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED, m_nHitSpeed, m_nCharStatus,
                0, 0, "", GetBodyStateBuffer());
        }

        protected void DisappearA(bool notifyDynamicRoomLifecycle = true)
        {
            m_PEnvir.DeleteFromMap(m_nCurrX, m_nCurrY, CellType.OS_MOVINGOBJECT,
                this, notifyDynamicRoomLifecycle);
            SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
        }

        private bool TryBeginCrossServerTransfer(Envirnoment targetEnvironment,
            short targetX, short targetY)
        {
            if (targetEnvironment == null || m_PEnvir == null
                || m_btRaceServer != Grobal2.RC_PLAYOBJECT
                || this is not TPlayObject playObject)
                return false;

            var sourceEnvironment = m_PEnvir;
            var sourceX = m_nCurrX;
            var sourceY = m_nCurrY;
            playObject.CleanupNativeHorseBeforeSpaceMove();
            if (sourceEnvironment.DeleteFromMap(sourceX, sourceY,
                CellType.OS_MOVINGOBJECT, this, false) != 1)
                return false;

            m_bo316 = true;
            playObject.m_sSwitchMapName = targetEnvironment.sMapName;
            playObject.m_nSwitchMapX = targetX;
            playObject.m_nSwitchMapY = targetY;
            playObject.m_nServerIndex = targetEnvironment.nServerIndex;
            playObject.m_boSwitchData = true;
            playObject.m_boEmergencyClose = true;
            playObject.m_boReconnection = true;

            try
            {
                SendCommittedDisappear(sourceEnvironment, sourceX, sourceY);
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage("[Exception] TBaseObject::TryBeginCrossServerTransfer disappear " + e.Message);
            }

            if (CountsAsPlayerPresence)
            {
                try
                {
                    sourceEnvironment.NotifyDynamicRoomPlayerRemoved();
                }
                catch (Exception e)
                {
                    M2Share.ErrorMessage("[Exception] TBaseObject::TryBeginCrossServerTransfer dynamic room notification " + e.Message);
                }
            }
            return true;
        }

        protected void KickException()
        {
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                m_sMapName = M2Share.g_Config.sHomeMap;
                m_nCurrX = M2Share.g_Config.nHomeX;
                m_nCurrY = M2Share.g_Config.nHomeY;
                TPlayObject PlayObject = this as TPlayObject;
                PlayObject.m_boEmergencyClose = true;
            }
            else
            {
                m_boDeath = true;
                m_dwDeathTick = HUtil32.GetTickCount();
                MakeGhost();
            }
        }

        protected bool Walk(int nIdent)
        {
            CellObject OSObject;
            TGateObj GateObj = null;
            const string sExceptionMsg = "[Exception] TBaseObject::Walk {0} {1} {2}:{3}";
            bool result = true;
            var suppressMovementBroadcast = false;
            if (m_PEnvir == null)
            {
                M2Share.ErrorMessage("Walk nil PEnvir");
                return result;
            }
            try
            {
                bool mapCell = false;
                var MapCellInfo = m_PEnvir.GetMapCellInfo(m_nCurrX, m_nCurrY, ref mapCell);
                if (mapCell && (MapCellInfo.ObjList != null))
                {
                    for (int i = 0; i < MapCellInfo.Count; i++)
                    {
                        OSObject = MapCellInfo.ObjList[i];
                        switch (OSObject.CellType)
                        {
                            case CellType.OS_GATEOBJECT:
                                GateObj = (TGateObj)OSObject.CellObj;
                                if ((GateObj != null))
                                {
                                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        if (m_PEnvir.ArroundDoorOpened(m_nCurrX, m_nCurrY))
                                        {
                                            if ((!GateObj.DEnvir.Flag.boNEEDHOLE) || (M2Share.EventManager.GetEvent(m_PEnvir, m_nCurrX, m_nCurrY, Grobal2.ET_DIGOUTZOMBI) != null))
                                            {
                                                if (M2Share.nServerIndex == GateObj.DEnvir.nServerIndex)
                                                {
                                                    if (!EnterAnotherMap(GateObj.DEnvir, GateObj.nDMapX, GateObj.nDMapY))
                                                    {
                                                        result = false;
                                                    }
                                                }
                                                else
                                                {
                                                    suppressMovementBroadcast = TryBeginCrossServerTransfer(
                                                        GateObj.DEnvir, GateObj.nDMapX, GateObj.nDMapY);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        result = false;
                                    }
                                }
                                break;
                            case CellType.OS_EVENTOBJECT:
                                {
                                    ((Event)OSObject.CellObj).ApplyTo(this);
                                    break;
                                }
                            case CellType.OS_MAPEVENT:
                                break;
                            case CellType.OS_DOOR:
                                break;
                            case CellType.OS_ROON:
                                break;
                        }
                        if (suppressMovementBroadcast)
                            break;
                    }
                }
                if (result && !suppressMovementBroadcast)
                {
                    SendRefMsg(nIdent, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(format(sExceptionMsg, new object[] { m_sCharName, m_sMapName, m_nCurrX, m_nCurrY }));
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        
        
        
        private void SendCommittedDisappear(Envirnoment sourceEnvironment,
            short sourceX, short sourceY)
        {
            if (m_boObMode || m_boFixedHideMode)
            {
                SendMsg(this, Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
                return;
            }

            var observers = new List<TBaseObject>();
            sourceEnvironment.GetMapBaseObjects(sourceX, sourceY,
                M2Share.g_Config.nSendRefMsgRange, observers);
            foreach (var observer in observers)
            {
                if (observer != null && !ReferenceEquals(observer, this)
                    && !observer.m_boGhost
                    && observer.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    observer.SendMsg(this, Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
                }
            }
        }

        private bool EnterAnotherMap(Envirnoment Envir, int nDMapX, int nDMapY)
        {
            bool result = false;
            Envirnoment OldEnvir = null;
            string sOldMapName = null;
            string sOldMapFileName = null;
            int nOldX = 0;
            int nOldY = 0;
            TUserCastle Castle;
            const string sExceptionMsg7 = "[Exception] TBaseObject::EnterAnotherMap";
            var sourceRemoved = false;
            var sourceRestored = true;
            var targetAddAttempted = false;
            List<TBaseObject> oldVisibleHumans = null;
            List<VisibleMapItem> oldVisibleItems = null;
            List<TVisibleBaseObject> oldVisibleActors = null;
            List<Event> oldVisibleEvents = null;
            try
            {
                if (Envir == null)
                {
                    return false;
                }
                if (m_Abil.Level < Envir.nRequestLevel)
                {
                    SysMsg($"需要 {Envir.Flag.nL - 1} 级以上才能进入 {Envir.sMapDesc}", MsgColor.Red, MsgType.Hint);
                    return false;
                }
                if (Envir.QuestNPC != null)
                {
                    ((Merchant)Envir.QuestNPC).Click(this as TPlayObject);
                }
                if (Envir.Flag.nNEEDSETONFlag >= 0)
                {
                    if (GetQuestFalgStatus(Envir.Flag.nNEEDSETONFlag) != Envir.Flag.nNeedONOFF)
                    {
                        return false;
                    }
                }
                var mapCell = false;
                Envir.GetMapCellInfo(nDMapX, nDMapY, ref mapCell);
                if (!mapCell)
                {
                    return false;
                }
                Castle = M2Share.CastleManager.IsCastlePalaceEnvir(Envir);
                if ((Castle != null) && (m_btRaceServer == Grobal2.RC_PLAYOBJECT))
                {
                    if (!Castle.CheckInPalace(m_nCurrX, m_nCurrY, this))
                    {
                        return false;
                    }
                }
                OldEnvir = m_PEnvir;
                if (OldEnvir == null)
                {
                    return false;
                }

                sOldMapName = m_sMapName;
                sOldMapFileName = m_sMapFileName;
                nOldX = m_nCurrX;
                nOldY = m_nCurrY;
                oldVisibleHumans = m_VisibleHumanList?.ToList();
                oldVisibleItems = m_VisibleItems?.ToList();
                oldVisibleActors = m_VisibleActors?.ToList();
                oldVisibleEvents = m_VisibleEvents?.ToList();
                sourceRemoved = OldEnvir.DeleteFromMap(m_nCurrX, m_nCurrY,
                    CellType.OS_MOVINGOBJECT, this, false,
                    suppressMapDropConsumer: true) == 1;
                if (!sourceRemoved)
                {
                    return false;
                }

                m_VisibleHumanList.Clear();
                for (var i = 0; i < m_VisibleItems.Count; i++)
                {
                    m_VisibleItems[i] = null;
                }
                m_VisibleItems.Clear();
                m_VisibleEvents.Clear();
                for (var i = 0; i < m_VisibleActors.Count; i++)
                {
                    m_VisibleActors[i] = null;
                }
                m_VisibleActors.Clear();
                m_PEnvir = Envir;
                m_sMapName = Envir.sMapName;
                m_sMapFileName = Envir.m_sMapFileName;
                m_nCurrX = (short)nDMapX;
                m_nCurrY = (short)nDMapY;
                targetAddAttempted = true;
                if (!ReferenceEquals(m_PEnvir.AddToMap(m_nCurrX, m_nCurrY,
                    CellType.OS_MOVINGOBJECT, this), this))
                {
                    return false;
                }
                if (Envir.Flag.boNOHORSE)
                {
                    m_boOnHorse = false;
                }
                SendCommittedDisappear(OldEnvir, (short)nOldX, (short)nOldY);
                SendMsg(this, Grobal2.RM_CLEAROBJECTS, 0, 0, 0, 0, "");
                SendMsg(this, Grobal2.RM_CHANGEMAP, 0, 0, 0, 0, Envir.m_sMapFileName);
                m_dwMapMoveTick = HUtil32.GetTickCount();
                m_bo316 = true;
                if (!m_boFixedHideMode)
                {
                    SendRefMsg(Grobal2.RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, 0, GetShowName());
                }
                OnEnvirnomentChanged();
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)  
                {
                    (this as TPlayObject).m_dwIncGamePointTick = HUtil32.GetTickCount();
                    (this as TPlayObject).m_dwIncGameGoldTick = HUtil32.GetTickCount();
                    (this as TPlayObject).m_dwAutoGetExpTick = HUtil32.GetTickCount();
                }
                if (m_PEnvir.Flag.boFight3Zone && (m_PEnvir.Flag.boFight3Zone != OldEnvir.Flag.boFight3Zone))
                {
                    RefShowName();
                }
                result = true;
                if (this is TPlayObject committedPlayer)
                {
                    var committedEnvironment = m_PEnvir;
                    var committedMapName = m_sMapName;
                    var committedMapFileName = m_sMapFileName;
                    var committedX = m_nCurrX;
                    var committedY = m_nCurrY;
                    try
                    {
                        m_PEnvir = OldEnvir;
                        m_sMapName = sOldMapName;
                        m_sMapFileName = sOldMapFileName;
                        m_nCurrX = (short)nOldX;
                        m_nCurrY = (short)nOldY;
                        committedPlayer.ReleaseNativeMapDropItems(OldEnvir,
                            removeTracker: !ReferenceEquals(OldEnvir, Envir));
                    }
                    finally
                    {
                        m_PEnvir = committedEnvironment;
                        m_sMapName = committedMapName;
                        m_sMapFileName = committedMapFileName;
                        m_nCurrX = committedX;
                        m_nCurrY = committedY;
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg7 + " " + e.Message);
            }
            finally
            {
                if (sourceRemoved && !result)
                {
                    if (targetAddAttempted)
                    {
                        Envir.DeleteFromMap(m_nCurrX, m_nCurrY,
                            CellType.OS_MOVINGOBJECT, this, false,
                            suppressMapDropConsumer: true);
                    }
                    m_PEnvir = OldEnvir;
                    m_sMapName = sOldMapName;
                    m_sMapFileName = sOldMapFileName;
                    m_nCurrX = (short)nOldX;
                    m_nCurrY = (short)nOldY;
                    sourceRestored = ReferenceEquals(OldEnvir.AddToMap(m_nCurrX, m_nCurrY,
                        CellType.OS_MOVINGOBJECT, this), this);
                    if (!sourceRestored)
                    {
                        M2Share.ErrorMessage(sExceptionMsg7 + " failed to restore source map");
                    }
                    else
                    {
                        m_VisibleHumanList?.Clear();
                        if (oldVisibleHumans != null)
                        {
                            foreach (var actor in oldVisibleHumans)
                                m_VisibleHumanList?.Add(actor);
                        }
                        m_VisibleItems?.Clear();
                        if (oldVisibleItems != null)
                        {
                            foreach (var item in oldVisibleItems)
                                m_VisibleItems?.Add(item);
                        }
                        m_VisibleActors?.Clear();
                        if (oldVisibleActors != null)
                        {
                            foreach (var actor in oldVisibleActors)
                                m_VisibleActors?.Add(actor);
                        }
                        m_VisibleEvents?.Clear();
                        if (oldVisibleEvents != null)
                        {
                            foreach (var mapEvent in oldVisibleEvents)
                                m_VisibleEvents?.Add(mapEvent);
                        }
                    }
                }

                if (CountsAsPlayerPresence
                    && ((result && !ReferenceEquals(OldEnvir, Envir))
                        || !sourceRestored))
                {
                    try
                    {
                        OldEnvir.NotifyDynamicRoomPlayerRemoved();
                    }
                    catch (Exception e)
                    {
                        M2Share.ErrorMessage(sExceptionMsg7 + " dynamic room notification " + e.Message);
                    }
                }
            }
            return result;
        }

        protected void TurnTo(byte nDir)
        {
            m_btDirection = nDir;
            SendRefMsg(Grobal2.RM_TURN, nDir, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SysMsg(string sMsg, MsgColor MsgColor, MsgType MsgType)
        {
            if (M2Share.g_Config.boShowPreFixMsg)
            {
                switch (MsgType)
                {
                    case MsgType.Mon:
                        sMsg = M2Share.g_Config.sMonSayMsgpreFix + sMsg;
                        break;
                    case MsgType.Hint:
                        sMsg = M2Share.g_Config.sHintMsgPreFix + sMsg;
                        break;
                    case MsgType.GM:
                        sMsg = M2Share.g_Config.sGMRedMsgpreFix + sMsg;
                        break;
                    case MsgType.System:
                        sMsg = M2Share.g_Config.sSysMsgPreFix + sMsg;
                        break;
                    case MsgType.Cust:
                        sMsg = M2Share.g_Config.sCustMsgpreFix + sMsg;
                        break;
                    case MsgType.Castle:
                        sMsg = M2Share.g_Config.sCastleMsgpreFix + sMsg;
                        break;
                }
            }

            if (MsgType == MsgType.Notice)// 如果发的是公告
            {
                string str = string.Empty;
                string FColor = string.Empty;
                string BColor = string.Empty;
                string nTime = string.Empty;
                if (sMsg[0] == '[')// 顶部滚动公告
                {
                    sMsg = HUtil32.ArrestStringEx(sMsg, '[', ']', ref str);
                    BColor = HUtil32.GetValidStrCap(str, ref FColor, new string[] { "," });
                    if (M2Share.g_Config.boShowPreFixMsg)
                    {
                        sMsg = M2Share.g_Config.sLineNoticePreFix + sMsg;
                    }
                    SendMsg(this, Grobal2.RM_MOVEMESSAGE, 0, HUtil32.Str_ToInt(FColor, 255), HUtil32.Str_ToInt(BColor, 255), 0, sMsg);
                }
                else if (sMsg[0] == '<')// 聊天框彩色公告
                {
                    sMsg = HUtil32.ArrestStringEx(sMsg, '<', '>', ref str);
                    BColor = HUtil32.GetValidStrCap(str, ref FColor, new string[] { "," });
                    if (M2Share.g_Config.boShowPreFixMsg)
                    {
                        sMsg = M2Share.g_Config.sLineNoticePreFix + sMsg;
                    }
                    SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, HUtil32.Str_ToInt(FColor, 255), HUtil32.Str_ToInt(BColor, 255), 0, sMsg);
                }
                else if (sMsg[0] == '{')// 屏幕居中公告
                {
                    sMsg = HUtil32.ArrestStringEx(sMsg, '{', '}', ref str);
                    str = HUtil32.GetValidStrCap(str, ref FColor, new string[] { "," });
                    str = HUtil32.GetValidStrCap(str, ref BColor, new string[] { "," });
                    str = HUtil32.GetValidStrCap(str, ref nTime, new string[] { "," });
                    if (M2Share.g_Config.boShowPreFixMsg)
                    {
                        sMsg = M2Share.g_Config.sLineNoticePreFix + sMsg;
                    }
                    SendMsg(this, Grobal2.RM_MOVEMESSAGE, 1, HUtil32.Str_ToInt(FColor, 255), HUtil32.Str_ToInt(BColor, 255), HUtil32.Str_ToInt(nTime, 0), sMsg);
                }
                else
                {
                    switch (MsgColor)
                    {
                        case MsgColor.Red:// 控制公告的颜色
                            if (M2Share.g_Config.boShowPreFixMsg)
                            {
                                sMsg = M2Share.g_Config.sLineNoticePreFix + sMsg;
                            }
                            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, M2Share.g_Config.btRedMsgFColor, M2Share.g_Config.btRedMsgBColor, 0, sMsg);
                            break;
                        case MsgColor.Green:
                            if (M2Share.g_Config.boShowPreFixMsg)
                            {
                                sMsg = M2Share.g_Config.sLineNoticePreFix + sMsg;
                            }
                            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, M2Share.g_Config.btGreenMsgFColor, M2Share.g_Config.btGreenMsgBColor, 0, sMsg);
                            break;
                        case MsgColor.Blue:
                            if (M2Share.g_Config.boShowPreFixMsg)
                            {
                                sMsg = M2Share.g_Config.sLineNoticePreFix + sMsg;
                            }
                            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, M2Share.g_Config.btBlueMsgFColor, M2Share.g_Config.btBlueMsgBColor, 0, sMsg);
                            break;
                    }
                }
            }
            else
            {
                switch (MsgColor)
                {
                    case MsgColor.Green:
                        SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, M2Share.g_Config.btGreenMsgFColor, M2Share.g_Config.btGreenMsgBColor, 0, sMsg);
                        break;
                    case MsgColor.Blue:
                        SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, M2Share.g_Config.btBlueMsgFColor, M2Share.g_Config.btBlueMsgBColor, 0, sMsg);
                        break;
                    default:
                        if (MsgType == MsgType.Cust)
                        {
                            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, M2Share.g_Config.btCustMsgFColor, M2Share.g_Config.btCustMsgBColor, 0, sMsg);
                        }
                        else
                        {
                            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, M2Share.g_Config.btRedMsgFColor, M2Share.g_Config.btRedMsgBColor, 0, sMsg);
                        }
                        break;
                }
            }
        }

        
        
        
        
        
        protected void MonsterSayMsg(TBaseObject AttackBaseObject, MonStatus MonStatus)
        {
            if (m_SayMsgList == null)
            {
                return;
            }
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                return;
            }
            string sAttackName = string.Empty;
            if (AttackBaseObject != null)
            {
                if ((AttackBaseObject.m_btRaceServer != Grobal2.RC_PLAYOBJECT) && (AttackBaseObject.m_Master == null))
                {
                    return;
                }
                if (AttackBaseObject.m_Master != null)
                {
                    sAttackName = AttackBaseObject.m_Master.m_sCharName;
                }
                else
                {
                    sAttackName = AttackBaseObject.m_sCharName;
                }
            }
            TMonSayMsg MonSayMsg = null;
            string sMsg = string.Empty;
            for (var i = 0; i < m_SayMsgList.Count; i++)
            {
                MonSayMsg = m_SayMsgList[i];
                sMsg = MonSayMsg.sSayMsg.Replace("%s", M2Share.FilterShowName(m_sCharName));
                sMsg = sMsg.Replace("%d", sAttackName);
                if ((MonSayMsg.State == MonStatus) && (M2Share.RandomNumber.Random(MonSayMsg.nRate) == 0))
                {
                    if (MonStatus == MonStatus.MonGen)
                    {
                        M2Share.UserEngine.SendBroadCastMsg(sMsg, MsgType.Mon);
                        SendRefMsg(Grobal2.SM_MONSTERSAY, 0, 0, 0, 0, sMsg);
                        break;
                    }
                    if (MonSayMsg.Color == MsgColor.White)
                    {
                        ProcessSayMsg(sMsg);
                        SendRefMsg(Grobal2.SM_MONSTERSAY, 0, 0, 0, 0, sMsg);
                    }
                    else
                    {
                        AttackBaseObject.SysMsg(sMsg, MonSayMsg.Color, MsgType.Mon);
                    }
                    break;
                }
            }
        }

        public void SendGroupText(string sMsg)
        {
            TPlayObject PlayObject;
            sMsg = M2Share.g_Config.sGroupMsgPreFix + sMsg;
            if (m_GroupOwner != null)
            {
                for (int i = 0; i < m_GroupOwner.m_GroupMembers.Count; i++)
                {
                    PlayObject = m_GroupOwner.m_GroupMembers[i];
                    PlayObject.SendMsg(this, Grobal2.RM_GROUPMESSAGE, 0, M2Share.g_Config.btGroupMsgFColor, M2Share.g_Config.btGroupMsgBColor, 0, sMsg);
                }
            }
        }

        
        
        
        protected void ApplyMeatQuality()
        {
            for (int i = 0; i < m_ItemList.Count; i++)
            {
                var UserItem = m_ItemList[i];
                var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                if (StdItem != null)
                {
                    if (StdItem.StdMode == 40)
                    {
                        UserItem.Dura = m_nMeatQuality;
                    }
                }
            }
        }

        protected bool TakeBagItems(TBaseObject BaseObject)
        {
            bool result = false;
            TUserItem UserItem;
            TPlayObject PlayObject;
            while (true)
            {
                if (BaseObject.m_ItemList.Count <= 0)
                {
                    break;
                }
                UserItem = BaseObject.m_ItemList[0];
                if (!AddItemToBag(UserItem))
                {
                    break;
                }
                if (this is TPlayObject)
                {
                    PlayObject = this as TPlayObject;
                    PlayObject.SendAddItem(UserItem);
                    result = true;
                }
                BaseObject.m_ItemList.RemoveAt(0);
            }
            return result;
        }

        
        
        
        
        private void ScatterGolds(TBaseObject GoldOfCreat,
            IList<KeyValuePair<string, string>> scatteredItems = null)
        {
            int I;
            int nGold;
            if (m_nGold > 0)
            {
                I = 0;
                while (true)
                {
                    if (m_nGold > M2Share.g_Config.nMonOneDropGoldCount)
                    {
                        nGold = M2Share.g_Config.nMonOneDropGoldCount;
                        m_nGold = m_nGold - M2Share.g_Config.nMonOneDropGoldCount;
                    }
                    else
                    {
                        nGold = m_nGold;
                        m_nGold = 0;
                    }
                    if (nGold > 0)
                    {
                        if (!DropGoldDown(nGold, true, GoldOfCreat, this))
                        {
                            m_nGold = m_nGold + nGold;
                            break;
                        }
                        scatteredItems?.Add(new KeyValuePair<string, string>(
                            Grobal2.sSTRING_GOLDNAME,
                            nGold.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        break;
                    }
                    I++;
                    if (I >= 16)
                    {
                        break;
                    }
                }
                GoldChanged();
            }
        }

        public void SetLastHiter(TBaseObject BaseObject)
        {
            // 战神 sub_767504 @0x76750D-0x76750F: `test esi,esi / je 0x767544` — a NULL
            // hitter makes the WHOLE function a no-op.  Native never clears m_LastHiter or
            // m_ExpHitter through this setter and never stamps a tick for a null attacker.
            // C# assigned unconditionally, so a null attacker would have wiped kill
            // attribution: the victim then dies with m_LastHiter == null => no PK point, no
            // drop credit, and the death log falls back to the unknown-killer token.
            // Not reachable today (all 8 C# call sites null-check at the caller), but the
            // guard belongs here rather than in the callers, which is where native puts it —
            // and the LastHiter call-site sweep still has native sites left to port.
            if (BaseObject == null)
            {
                return;
            }
            m_LastHiter = BaseObject;
            m_LastHiterTick = HUtil32.GetTickCount();
            if (m_ExpHitter == null)
            {
                m_ExpHitter = BaseObject;
                m_ExpHitterTick = HUtil32.GetTickCount();
            }
            else
            {
                if (m_ExpHitter == BaseObject)
                {
                    m_ExpHitterTick = HUtil32.GetTickCount();
                }
            }
        }

        public void SetPKFlag(TBaseObject BaseObject)
        {
            if ((PKLevel() < 2) && (BaseObject.PKLevel() < 2) && (!m_PEnvir.Flag.boFightZone) && (!m_PEnvir.Flag.boFight3Zone) && !m_boPKFlag)
            {
                BaseObject.m_dwPKTick = HUtil32.GetTickCount();
                if (!BaseObject.m_boPKFlag)
                {
                    BaseObject.m_boPKFlag = true;
                    BaseObject.RefNameColor();
                }
            }
        }

        public bool IsGoodKilling(TBaseObject cert)
        {
            return cert.m_boPKFlag;
        }

        public bool IsAttackTarget_sub_4C88E4()
        {
            return true;
        }

        
        
        
        
        
        public virtual bool IsAttackTarget(TBaseObject BaseObject)
        {
            bool result = false;
            if ((BaseObject == null) || (BaseObject == this))
            {
                return result;
            }
            if (m_btRaceServer >= Grobal2.RC_ANIMAL)
            {
                if (m_Master != null)
                {
                    if ((m_Master.m_LastHiter == BaseObject) || (m_Master.m_ExpHitter == BaseObject) || (m_Master.m_TargetCret == BaseObject))
                    {
                        result = true;
                    }
                    if (BaseObject.m_TargetCret != null)
                    {
                        if ((BaseObject.m_TargetCret == m_Master) || (BaseObject.m_TargetCret.m_Master == m_Master) && (BaseObject.m_btRaceServer != Grobal2.RC_PLAYOBJECT))
                        {
                            result = true;
                        }
                    }
                    if ((BaseObject.m_TargetCret == this) && (BaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL))
                    {
                        result = true;
                    }
                    if (BaseObject.m_Master != null)
                    {
                        if ((BaseObject.m_Master == m_Master.m_LastHiter) || (BaseObject.m_Master == m_Master.m_TargetCret))
                        {
                            result = true;
                        }
                    }
                    if (BaseObject.m_Master == m_Master)
                    {
                        result = false;
                    }
                    if (BaseObject.m_boHolySeize)
                    {
                        result = false;
                    }
                    if (m_Master.m_boSlaveRelax)
                    {
                        result = false;
                    }
                    if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        if (BaseObject.InSafeZone())
                        {
                            result = false;
                        }
                    }
                    BreakCrazyMode();
                }
                else
                {
                    if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        result = true;
                    }
                    if ((m_btRaceServer > Grobal2.RC_PEACENPC) && (m_btRaceServer < Grobal2.RC_ANIMAL))
                    {
                        result = true;
                    }
                    if (BaseObject.m_Master != null)
                    {
                        result = true;
                    }
                }
                if (m_boCrazyMode && ((BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT) || (BaseObject.m_btRaceServer > Grobal2.RC_PEACENPC)))
                {
                    result = true;
                }
                if (m_boNastyMode && ((BaseObject.m_btRaceServer < Grobal2.RC_NPC) || (BaseObject.m_btRaceServer > Grobal2.RC_PEACENPC)))
                {
                    result = true;
                }
            }
            else
            {
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    switch (m_btAttatckMode)
                    {
                        case M2Share.HAM_ALL:
                            if ((BaseObject.m_btRaceServer < Grobal2.RC_NPC) || (BaseObject.m_btRaceServer > Grobal2.RC_PEACENPC))
                            {
                                result = true;
                            }
                            if (M2Share.g_Config.boNonPKServer)
                            {
                                result = IsAttackTarget_sub_4C88E4();
                            }
                            break;
                        case M2Share.HAM_PEACE:
                            if (BaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                            {
                                result = true;
                            }
                            break;
                        case M2Share.HAM_DEAR:
                            if (BaseObject != (this as TPlayObject).m_DearHuman)
                            {
                                result = true;
                            }
                            break;
                        case M2Share.HAM_MASTER:
                            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                            {
                                result = true;
                                if ((this as TPlayObject).m_boMaster)
                                {
                                    for (var i = 0; i < (this as TPlayObject).m_MasterList.Count; i++)
                                    {
                                        if ((this as TPlayObject).m_MasterList[i] == BaseObject)
                                        {
                                            result = false;
                                            break;
                                        }
                                    }
                                }
                                if ((BaseObject as TPlayObject).m_boMaster)
                                {
                                    for (var i = 0; i < (BaseObject as TPlayObject).m_MasterList.Count; i++)
                                    {
                                        if ((BaseObject as TPlayObject).m_MasterList[i] == this)
                                        {
                                            result = false;
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                result = true;
                            }
                            break;
                        case M2Share.HAM_GROUP:
                            if ((BaseObject.m_btRaceServer < Grobal2.RC_NPC) || (BaseObject.m_btRaceServer > Grobal2.RC_PEACENPC))
                            {
                                result = true;
                            }
                            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                            {
                                if (IsGroupMember(BaseObject))
                                {
                                    result = false;
                                }
                            }
                            if (M2Share.g_Config.boNonPKServer)
                            {
                                result = IsAttackTarget_sub_4C88E4();
                            }
                            break;
                        case M2Share.HAM_GUILD:
                            if ((BaseObject.m_btRaceServer < Grobal2.RC_NPC) || (BaseObject.m_btRaceServer > Grobal2.RC_PEACENPC))
                            {
                                result = true;
                            }
                            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                            {
                                if (m_MyGuild != null)
                                {
                                    if (m_MyGuild.IsMember(BaseObject.m_sCharName))
                                    {
                                        result = false;
                                    }
                                    if (m_boGuildWarArea && (BaseObject.m_MyGuild != null))
                                    {
                                        if (m_MyGuild.IsAllyGuild(BaseObject.m_MyGuild))
                                        {
                                            result = false;
                                        }
                                    }
                                }
                            }
                            if (M2Share.g_Config.boNonPKServer)
                            {
                                result = IsAttackTarget_sub_4C88E4();
                            }
                            break;
                        case M2Share.HAM_PKATTACK:
                            if ((BaseObject.m_btRaceServer < Grobal2.RC_NPC) || (BaseObject.m_btRaceServer > Grobal2.RC_PEACENPC))
                            {
                                result = true;
                            }
                            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                            {
                                if (PKLevel() >= 2)
                                {
                                    if (BaseObject.PKLevel() < 2)
                                    {
                                        result = true;
                                    }
                                    else
                                    {
                                        result = false;
                                    }
                                }
                                else
                                {
                                    if (BaseObject.PKLevel() >= 2)
                                    {
                                        result = true;
                                    }
                                    else
                                    {
                                        result = false;
                                    }
                                }
                            }
                            if (M2Share.g_Config.boNonPKServer)
                            {
                                result = IsAttackTarget_sub_4C88E4();
                            }
                            break;
                    }
                }
                else
                {
                    result = true;
                }
            }
            if (BaseObject.m_boAdminMode || BaseObject.m_boStoneMode)
            {
                result = false;
            }
            return result;
        }

        public virtual bool IsProperTarget(TBaseObject BaseObject)
        {
            bool result = IsAttackTarget(BaseObject);
            if (result)
            {
                if ((m_btRaceServer == Grobal2.RC_PLAYOBJECT) && (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT))
                {
                    result = IsProtectTarget(BaseObject);
                }
            }
            if ((BaseObject != null) && (m_btRaceServer == Grobal2.RC_PLAYOBJECT) && (BaseObject.m_Master != null) && (BaseObject.m_btRaceServer != Grobal2.RC_PLAYOBJECT))
            {
                if (BaseObject.m_Master == this)
                {
                    if (m_btAttatckMode != M2Share.HAM_ALL)
                    {
                        result = false;
                    }
                }
                else
                {
                    result = IsAttackTarget(BaseObject.m_Master);
                    if (InSafeZone() || BaseObject.InSafeZone())
                    {
                        result = false;
                    }
                }
            }
            return result;
        }

        public void WeightChanged()
        {
            m_WAbil.Weight = RecalcBagWeight();
            SendUpdateMsg(this, Grobal2.RM_WEIGHTCHANGED, 0, 0, 0, 0, "");
        }

        public bool InSafeZone()
        {
            // MFLG-17: Fixed safe zone rule order and logic to match native sub_76858C
            // Native ladder:
            //   1. m_PEnvir == null -> FALSE (not true!)
            //   2. m_PEnvir.Flag.boSAFE -> TRUE
            //   3. StartPointList check -> TRUE if found
            //   4. RedHomeMap check (only if nSafeZoneSize > 0) -> TRUE if in range
            //   5. SafeZoneList polygon check -> TRUE if inside

            // Step 1: null check returns FALSE (7684B5-7684B7: xor eax,eax / test edi,edi / je)
            if (m_PEnvir == null)
            {
                return false;
            }

            // Step 2: boSAFE flag (76859B-76859D: test al,al / jne)
            if (m_PEnvir.Flag.boSAFE)
            {
                return true;
            }

            // Step 3: StartPointList iteration (sub_7684A0 -> sub_696D7C)
            for (int i = 0; i < M2Share.StartPointList.Count; i++)
            {
                var startPoint = M2Share.StartPointList[i];
                if (startPoint != null && startPoint.m_sMapName == m_PEnvir.sMapName)
                {
                    int nSafeX = startPoint.m_nCurrX;
                    int nSafeY = startPoint.m_nCurrY;
                    if ((Math.Abs(m_nCurrX - nSafeX) <= M2Share.g_Config.nSafeZoneSize) &&
                        (Math.Abs(m_nCurrY - nSafeY) <= M2Share.g_Config.nSafeZoneSize))
                    {
                        return true;
                    }
                }
            }

            // Step 4: RedHomeMap check (76850E-768549)
            // Native: 76850E test edi,edi / 768510 jle -> skip if nSafeZoneSize <= 0
            if (M2Share.g_Config.nSafeZoneSize > 0 &&
                !string.IsNullOrEmpty(M2Share.g_Config.sRedHomeMap) &&
                string.Equals(m_PEnvir.sMapName, M2Share.g_Config.sRedHomeMap, StringComparison.OrdinalIgnoreCase))
            {
                // Native: 768527-768549 uses <= for range check (not >)
                if ((Math.Abs(m_nCurrX - M2Share.g_Config.nRedHomeX) <= M2Share.g_Config.nSafeZoneSize) &&
                    (Math.Abs(m_nCurrY - M2Share.g_Config.nRedHomeY) <= M2Share.g_Config.nSafeZoneSize))
                {
                    return true;
                }
            }

            // Step 5: SafeZoneList polygon check (76854F-76856E: call sub_696E48)
            if (M2Share.SafeZoneList != null)
            {
                for (var i = 0; i < M2Share.SafeZoneList.Count; i++)
                {
                    if (M2Share.SafeZoneList[i].Contains(m_PEnvir.sMapName, m_nCurrX, m_nCurrY))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool InSafeZone(Envirnoment Envir, int nX, int nY)
        {
            // MFLG-17: Same logic as InSafeZone() but with explicit Envir/coordinates

            // Step 1: null check returns FALSE
            if (Envir == null)
            {
                return false;
            }

            // Step 2: boSAFE flag
            if (Envir.Flag.boSAFE)
            {
                return true;
            }

            // Step 3: StartPointList iteration
            for (int i = 0; i < M2Share.StartPointList.Count; i++)
            {
                var startPoint = M2Share.StartPointList[i];
                if (startPoint != null && startPoint.m_sMapName == Envir.sMapName)
                {
                    int nSafeX = startPoint.m_nCurrX;
                    int nSafeY = startPoint.m_nCurrY;
                    if ((Math.Abs(nX - nSafeX) <= M2Share.g_Config.nSafeZoneSize) &&
                        (Math.Abs(nY - nSafeY) <= M2Share.g_Config.nSafeZoneSize))
                    {
                        return true;
                    }
                }
            }

            // Step 4: RedHomeMap check
            if (M2Share.g_Config.nSafeZoneSize > 0 &&
                !string.IsNullOrEmpty(M2Share.g_Config.sRedHomeMap) &&
                string.Equals(Envir.sMapName, M2Share.g_Config.sRedHomeMap, StringComparison.OrdinalIgnoreCase))
            {
                if ((Math.Abs(nX - M2Share.g_Config.nRedHomeX) <= M2Share.g_Config.nSafeZoneSize) &&
                    (Math.Abs(nY - M2Share.g_Config.nRedHomeY) <= M2Share.g_Config.nSafeZoneSize))
                {
                    return true;
                }
            }

            // Step 5: SafeZoneList polygon check
            if (M2Share.SafeZoneList != null)
            {
                for (var i = 0; i < M2Share.SafeZoneList.Count; i++)
                {
                    if (M2Share.SafeZoneList[i].Contains(Envir.sMapName, nX, nY))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void OpenHolySeizeMode(int dwInterval)
        {
            m_boHolySeize = true;
            m_dwHolySeizeTick = HUtil32.GetTickCount();
            m_dwHolySeizeInterval = dwInterval;
            RefNameColor();
        }

        public void BreakHolySeizeMode()
        {
            m_boHolySeize = false;
            RefNameColor();
        }

        public void OpenCrazyMode(int nTime)
        {
            m_boCrazyMode = true;
            m_dwCrazyModeTick = HUtil32.GetTickCount();
            m_dwCrazyModeInterval = nTime * 1000;
            RefNameColor();
        }

        public void BreakCrazyMode()
        {
            if (m_boCrazyMode)
            {
                m_boCrazyMode = false;
                RefNameColor();
            }
        }

        private void LeaveGroup()
        {
            const string sExitGropMsg = "{0} 已经退出了本组.";
            SendGroupText(format(sExitGropMsg, m_sCharName));
            m_GroupOwner = null;
            SendMsg(this, Grobal2.RM_GROUPCANCEL, 0, 0, 0, 0, "");
        }

        public TUserMagic GetMagicInfo(int nMagicID)
        {
            TUserMagic result = null;
            TUserMagic UserMagic;
            for (int i = 0; i < m_MagicList.Count; i++)
            {
                UserMagic = m_MagicList[i];
                if (UserMagic.MagicInfo.wMagicID == nMagicID)
                {
                    result = UserMagic;
                    break;
                }
            }
            return result;
        }

        public void TrainSkill(TUserMagic UserMagic, int nTranPoint)
        {
            if (m_boFastTrain)
            {
                nTranPoint = nTranPoint * 3;
            }
            UserMagic.nTranPoint += nTranPoint;
        }

        public bool CheckMagicLevelup(TUserMagic UserMagic)
        {
            bool result = false;
            int nLevel;
            if ((UserMagic.btLevel < 4) && (UserMagic.MagicInfo.btTrainLv >= UserMagic.btLevel))
            {
                nLevel = UserMagic.btLevel;
            }
            else
            {
                nLevel = 0;
            }
            if ((UserMagic.MagicInfo.btTrainLv > UserMagic.btLevel) && (UserMagic.MagicInfo.MaxTrain[nLevel] <= UserMagic.nTranPoint))
            {
                if (UserMagic.MagicInfo.btTrainLv > UserMagic.btLevel)
                {
                    UserMagic.nTranPoint -= UserMagic.MagicInfo.MaxTrain[nLevel];
                    UserMagic.btLevel++;
                    SendUpdateDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, UserMagic.MagicInfo.wMagicID, UserMagic.btLevel, UserMagic.nTranPoint, "", 800);
                    CheckSeeHealGauge(UserMagic);
                }
                else
                {
                    UserMagic.nTranPoint = UserMagic.MagicInfo.MaxTrain[nLevel];
                }
                result = true;
            }
            return result;
        }

        
        
        
        
        public void RecallSlave(string sSlaveName)
        {
            short nX = 0;
            short nY = 0;
            int nFlag = -1;
            GetFrontPosition(ref nX, ref nY);
            if (sSlaveName == M2Share.g_Config.sDragon)
            {
                nFlag = 1;
            }
            for (int i = m_SlaveList.Count - 1; i >= 0; i--)
            {
                if (nFlag == 1)
                {
                    if ((m_SlaveList[i].m_sCharName == M2Share.g_Config.sDragon) || (m_SlaveList[i].m_sCharName == M2Share.g_Config.sDragon1))
                    {
                        m_SlaveList[i].SpaceMove(m_PEnvir.sMapName, nX, nY, 1);
                        break;
                    }
                }
                else if (m_SlaveList[i].m_sCharName == sSlaveName)
                {
                    m_SlaveList[i].SpaceMove(m_PEnvir.sMapName, nX, nY, 1);
                    break;
                }
            }
        }

        /// <summary>
        /// Native <c>GetHitStruckDamage</c> = <c>sub_767958</c> — the physical
        /// (AC) half of the ONE armour path; <c>sub_7679B8</c> below is its
        /// byte-identical MAC twin. Both are reached only from
        /// <c>sub_76C35C</c> (VMT <c>+0x1B4</c>), which picks AC vs MAC from the
        /// skill-id bitset at <c>0x76C49C</c>.
        /// <para>
        /// @0x767965 <c>mov dl,0x11; call sub_772960; jne</c> → <c>xor eax,eax</c>
        /// — internal state <c>0x11</c> (17) SKIPS the roll (armour 0), it is not
        /// immunity. @0x767976 loads MaxAC <c>+0x280</c> / AC <c>+0x27C</c> and
        /// @0x767984 <c>call sub_7678F4</c> = the shared luck-weighted roll.
        /// @0x767989 <c>sub edx,eax; call sub_4C7004(edx=0)</c> = <c>max(0,…)</c>.
        /// @0x767997 <c>cmp byte [ebx+0x2EE],1; jne</c> → <c>add eax,[ebp+8]</c>.
        /// @0x7679A9 tail-calls <c>sub_76FFE8</c> (bubble) and returns.
        /// </para>
        /// </summary>
        public ushort GetHitStruckDamage(TBaseObject Target, int nDamage)
        {
            // sub_7678F4 is shared with the magic entry; state 0x11 skips it.
            if (!HasNativeActiveState(17))
            {
                nDamage = HUtil32._MAX(0, unchecked(nDamage -
                    RollNativeDefenceValue(m_WAbil.AC)));
            }
            nDamage = ApplyNativeBubbleDefence(0, nDamage);
            // sub_767958 ENDS at the bubble: @0x7679A9 `call sub_76FFE8` is a
            // tail-call followed immediately by `pop esi; pop ebx; pop ecx;
            // pop ebp; ret 4` (@0x7679AE-0x7679B2). Its MAC twin sub_7679B8
            // ends the same way @0x767A09. Neither getter calls sub_767CBC
            // (super-force) and neither touches word [+0x3FC] (the charge
            // shield) — `_cs_field.py 3FC` lists no ref inside either body.
            // Both of those steps live in StruckDamage: super-force at
            // sub_767A18 @0x767AE4, charge shield at sub_73F9FC @0x73FB5F.
            // Moved there; see TBaseObject.StruckDamage below.
            //
            // Arg roles re-read from bytes (Hex-Rays renders them wrongly):
            // @0x76795E `mov [ebp-4],ecx` = DAMAGE (consumed @0x767989
            // `mov edx,[ebp-4]; sub edx,eax`), @0x767961 `mov esi,edx` = SKILL
            // ID (forwarded to the bubble @0x7679A5), eax = self. There is NO
            // attacker parameter in either armour getter — confirmed by the
            // dispatcher @0x76C449-0x76C455 (`mov ecx,[ebp+0xC]` = damage,
            // `movzx edx,bx` = skill id).
            return (ushort)nDamage;
        }

        /// <summary>
        /// Native <c>GetMagStruckDamage</c> = <c>sub_7679B8</c>: byte-identical
        /// to <c>sub_767958</c> except that @0x7679D6 loads MaxMAC
        /// <c>+0x288</c> / MAC <c>+0x284</c>. Same state-<c>0x11</c> skip
        /// @0x7679C5, same shared <c>sub_7678F4</c> roll @0x7679E4, same
        /// <c>sub_76FFE8</c> bubble tail @0x767A09.
        /// </summary>
        public int GetMagStruckDamage(TBaseObject BaseObject, int nDamage)
        {
            if (!HasNativeActiveState(17))
            {
                nDamage = HUtil32._MAX(0, unchecked(nDamage -
                    RollNativeDefenceValue(m_WAbil.MAC)));
            }
            nDamage = ApplyNativeBubbleDefence(0, nDamage);
            // Same tail as sub_767958: @0x767A09 `call sub_76FFE8` then
            // `pop esi; pop ebx; pop ecx; pop ebp; ret 4`. No super-force,
            // no charge shield — both moved into StruckDamage.
            return nDamage;
        }

        /// <summary>
        /// Native <c>StruckDamage</c> = VMT slot <c>+0x0A8</c>, which is
        /// CLASS-SPLIT: <c>sub_73F9FC</c> = <c>THumanKind.StruckDamage</c>
        /// (VMT <c>0x73BC34+0xA8</c>) and <c>sub_767A18</c> =
        /// <c>TCreature.StruckDamage</c> (VMT <c>0x764608+0xA8</c>).
        /// <para>
        /// Native takes the ATTACKER in <c>ecx</c> (both bodies are
        /// <c>ret 8</c>): <c>0x73FA05 mov [ebp-4],ecx</c> and
        /// <c>0x767A1F mov edi,ecx</c>. <c>m_LastHiter</c> (<c>+0x354</c>,
        /// written only by <c>SetLastHiter</c> = <c>sub_767504</c>
        /// <c>@0x767511</c>) is NOT an equivalent source: 19 of the 23 native
        /// callers of <c>+0x0A8</c> never call <c>sub_767504</c> at all;
        /// <c>sub_766A70</c> calls <c>StruckDamage</c> <c>@0x766BA6</c> and only
        /// then <c>SetLastHiter</c> <c>@0x766BC1</c>, and only when
        /// <c>byte [+0x178] &gt;= 0x32</c>; and <c>0x73F3AD</c> / <c>0x73F43E</c>
        /// pass <c>xor ecx,ecx</c> = a deliberately NIL attacker. Hence the
        /// explicit parameter. A null attacker is a native-faithful state — the
        /// <c>test edi,edi; je</c> guards at <c>0x767A50</c> and
        /// <c>0x767AD9</c> simply skip the two attacker-dependent stages.
        /// </para>
        /// </summary>
        public void StruckDamage(int nDamage) => StruckDamage(nDamage, null);

        public void StruckDamage(int nDamage, TBaseObject attacker)
        {
            int nDam;
            int nDura;
            int nOldDura;
            TPlayObject PlayObject;
            GoodItem StdItem;
            bool bo19;
            // sub_73F9FC @0x73FA0C-0x73FA28: `mov dl,0x34; call sub_772960; jne`
            // / `mov dl,0x37; call sub_772960; je` -> `xor esi,esi` and jump
            // straight to the epilogue: internal states 0x34 (52) and 0x37 (55)
            // zero the damage outright. These are the same immunity pair
            // ResolveFullMagicDamage already gates on (NativeMagicDamage.cs:16),
            // and both live in m_nCharStatus2 so they latch correctly today.
            // NOTE: the trailing HasTimedAbility(17) is *scriptType* 17, i.e.
            // internal state 49 (无敌) — a different gate from the two above and
            // one with no counterpart in sub_73F9FC. It is left untouched here:
            // state 49's carrier [+0x2E1] is status-layer owned.
            if (nDamage <= 0 || HasNativeActiveState(52) ||
                HasNativeActiveState(55) || HasTimedAbility(17))
            {
                return;
            }
            // The `nWarrMon`/`nWizardMon`/`nTaosMon`/`nMonHum` config scaling
            // that stood here was an INVENTION with no native counterpart.
            // Every function on the native damage chain was disassembled
            // end-to-end — sub_73F9FC, sub_767A18, sub_73F8E0, sub_767B5C,
            // sub_76C35C, sub_767958, sub_7679B8, sub_7678F4, sub_767BA8,
            // sub_767D14 — and not one reads a config global; no `[+0x72]` job
            // byte is read by any of them except sub_767A18 @0x767ADD, which
            // feeds sub_767CBC. 战神's ONLY job-differentiated damage scaling is
            // that per-monster pair (mask `dword [self+0x43C]` + percent
            // `dword [self+0x440]`, both written from the monster definition at
            // sub_71E9E8 @0x71EA7A/@0x71EA83), and it is applied below at its
            // native stage.
            // sub_73F9FC @0x73FA30: `mov eax,0Ah; call Random; add eax,5`.
            nDam = M2Share.RandomNumber.Random(10) + 5;
            // sub_73F9FC @0x73FA40-0x73FADE: the damage-amplify states. The
            // former `POISON_DAMAGEARMOR * (nPosionDamagarmor/10)` multiplier
            // that stood here has NO counterpart anywhere in the native body
            // (0x73F9FC-0x73FBBD reads no config global and never touches the
            // poison timers), so it is removed in favour of the native chain.
            ApplyNativeStruckAmplifyStates(ref nDam, ref nDamage);
            // sub_767A18 @0x767AD9-0x767AE9 — the SUPER-FORCE reduction, the
            // TCreature-body stage: `test edi,edi; je 0x767B13` (nil attacker
            // skips it) / `mov cl,[edi+0x72]` (ATTACKER job byte) /
            // `mov edx,esi` (damage) / `mov eax,ebx` (self) /
            // `call sub_767CBC`. It sits AFTER the 1.3 / 1.25 / 1.2 amplify
            // tier and BEFORE the land call `[+0x1AC]` @0x767B1D. It is NOT in
            // the armour getters — both end at the bubble tail-call.
            nDamage = ApplyNativeMonsterSuperForceReduction(attacker, nDamage);
            // sub_73F9FC @0x73FAE0-0x73FB53: the block/parry proc sits between the
            // damage-amplify states and the durability worker. Native only re-tests
            // `test esi,esi; jle` (@0x73FB51) inside the taken branch, so the
            // early-out is conditional on the proc having fired.
            if (TryApplyNativePhysicalBlockProc(ref nDamage) && nDamage <= 0)
            {
                return;
            }
            bo19 = false;
            // DURA-16: U_DRESS also requires 1/8 probability gate, matching other equipment slots
            if (m_UseItems[Grobal2.U_DRESS] != null && m_UseItems[Grobal2.U_DRESS].wIndex > 0 && M2Share.RandomNumber.Random(8) == 0)
            {
                nDura = m_UseItems[Grobal2.U_DRESS].Dura;
                nOldDura = HUtil32.Round(nDura / 1000.0);
                nDura -= nDam;
                if (nDura <= 0)
                {
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        PlayObject = this as TPlayObject;
                        PlayObject.SendDelItems(m_UseItems[Grobal2.U_DRESS]);
                        StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_DRESS].wIndex);
                        if (StdItem.NeedIdentify == 1)
                        {
                            M2Share.AddGameDataLog('3' + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + m_UseItems[Grobal2.U_DRESS].MakeIndex + "\t"
                                + HUtil32.BoolToIntStr(m_btRaceServer == Grobal2.RC_PLAYOBJECT) + "\t" + '0');
                        }
                        m_UseItems[Grobal2.U_DRESS].wIndex = 0;
                        FeatureChanged();
                    }
                    m_UseItems[Grobal2.U_DRESS].wIndex = 0;
                    m_UseItems[Grobal2.U_DRESS].Dura = 0;
                    bo19 = true;
                }
                else
                {
                    m_UseItems[Grobal2.U_DRESS].Dura = (ushort)nDura;
                }
                if (nOldDura != HUtil32.Round(nDura / 1000.0))
                {
                    SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_DRESS, nDura, m_UseItems[Grobal2.U_DRESS].DuraMax, 0, "");
                }
            }
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                if ((m_UseItems[i] != null) && (m_UseItems[i].wIndex > 0) && (M2Share.RandomNumber.Random(8) == 0))
                {
                    nDura = m_UseItems[i].Dura;
                    nOldDura = HUtil32.Round(nDura / 1000.0);
                    nDura -= nDam;
                    if (nDura <= 0)
                    {
                        if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            PlayObject = this as TPlayObject;
                            PlayObject.SendDelItems(m_UseItems[i]);
                            StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                            if (StdItem.NeedIdentify == 1)
                            {
                                M2Share.AddGameDataLog('3' + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + m_UseItems[i].MakeIndex + "\t"
                                    + HUtil32.BoolToIntStr(m_btRaceServer == Grobal2.RC_PLAYOBJECT) + "\t" + '0');
                            }
                            m_UseItems[i].wIndex = 0;
                            FeatureChanged();
                        }
                        m_UseItems[i].wIndex = 0;
                        m_UseItems[i].Dura = 0;
                        bo19 = true;
                    }
                    else
                    {
                        m_UseItems[i].Dura = (ushort)nDura;
                    }
                    if (nOldDura != HUtil32.Round(nDura / 1000.0))
                    {
                        SendMsg(this, Grobal2.RM_DURACHANGE, i, nDura, m_UseItems[i].DuraMax, 0, "");
                    }
                }
            }
            if (bo19)
            {
                RecalcAbilitys();
            }
            // sub_73F9FC @0x73FB5F-0x73FBA4 — the CHARGE SHIELD, native stage
            // 11/19: it runs AFTER the durability worker (@0x73FB5A
            // `call sub_73FBE8`) and BEFORE the `test esi,esi; jle` land gate
            // (@0x73FBA6), so the reduction applies to the ALREADY-AMPLIFIED,
            // post-block damage — not to the armour-getter result.
            //   73FB5F  cmp  word [ebx+0x3FC],0 ; jbe 0x73FBA6   (no charges)
            //   73FB69  mov  dl,1 ; call sub_76CD8C              (job HiAbil)
            //   73FB75  fild [ebp-0x10] ; fmul dword [0x73FBE4]  (= 2.5, bytes
            //           `00002040`; the next 4 bytes are `55 8B EC` = the
            //           sub_73FBE8 prologue, proving the constant is 4 bytes)
            //   73FB7E  call sub_403580                          (TRUNCATING
            //           fistp — NOT the round-half-even sub_403574)
            //   73FB83  sub  esi,eax                             (no clamp)
            //   73FB85  dec  word [ebx+0x3FC]
            //   73FB8C  cmp  word [ebx+0x3FC],0 ; jne 0x73FBA6
            //   73FB96  mov  dl,0x3B ; call sub_7729A8           (clear state
            //           0x3B = 59 = NativeSkill153ShieldState)
            //   73FBA1  call sub_7729C4                          (StatusChanged)
            // There is NO Math.Max(0,...) here: @0x73FBA6 `jle` is a RETURN
            // gate, not a clamp — native returns the negative value to its
            // caller and skips the landing.
            nDamage = ConsumeNativeSkill153ShieldCharge(nDamage);
            // sub_73F9FC @0x73FBA6 `test esi,esi; jle 0x73FBBD`: return WITHOUT
            // landing when the shield drove the damage to zero or below.
            if (nDamage <= 0)
            {
                return;
            }
            DamageHealth(nDamage);
        }

        public virtual string GeTBaseObjectInfo()
        {
            string result = m_sCharName + ' ' + "地图:" + m_sMapName + '(' + m_PEnvir.sMapDesc + ") " + "座标:" + m_nCurrX + '/' + m_nCurrY + ' ' + "等级:" + m_Abil.Level + ' ' + "经验:" + m_Abil.Exp + ' '
                + "生命值: " + m_WAbil.HP + '-' + m_WAbil.MaxHP + ' ' + "魔法值: " + m_WAbil.MP + '-' + m_WAbil.MaxMP + ' ' + "攻击力: " + HUtil32.LoWord(m_WAbil.DC) + '-' + HUtil32.HiWord(m_WAbil.DC) + ' '
                + "魔法力: " + HUtil32.LoWord(m_WAbil.MC) + '-' + HUtil32.HiWord(m_WAbil.MC) + ' ' + "道术: " + HUtil32.LoWord(m_WAbil.SC) + '-' + HUtil32.HiWord(m_WAbil.SC) + ' '
                + "防御力: " + HUtil32.LoWord(m_WAbil.AC) + '-' + HUtil32.HiWord(m_WAbil.AC) + ' ' + "魔防力: " + HUtil32.LoWord(m_WAbil.MAC) + '-' + HUtil32.HiWord(m_WAbil.MAC) + ' ' + "准确:" + m_btHitPoint + ' '
                + "敏捷:" + m_btSpeedPoint;
            return result;
        }

        public bool GetBackPosition(ref short nX, ref short nY)
        {
            bool result;
            Envirnoment Envir;
            Envir = m_PEnvir;
            nX = m_nCurrX;
            nY = m_nCurrY;
            switch (m_btDirection)
            {
                case Grobal2.DR_UP:
                    if (nY < (Envir.wHeight - 1))
                    {
                        nY++;
                    }
                    break;
                case Grobal2.DR_DOWN:
                    if (nY > 0)
                    {
                        nY -= 1;
                    }
                    break;
                case Grobal2.DR_LEFT:
                    if (nX < (Envir.wWidth - 1))
                    {
                        nX++;
                    }
                    break;
                case Grobal2.DR_RIGHT:
                    if (nX > 0)
                    {
                        nX -= 1;
                    }
                    break;
                case Grobal2.DR_UPLEFT:
                    if ((nX < (Envir.wWidth - 1)) && (nY < (Envir.wHeight - 1)))
                    {
                        nX++;
                        nY++;
                    }
                    break;
                case Grobal2.DR_UPRIGHT:
                    if ((nX < (Envir.wWidth - 1)) && (nY > 0))
                    {
                        nX -= 1;
                        nY++;
                    }
                    break;
                case Grobal2.DR_DOWNLEFT:
                    if ((nX > 0) && (nY < (Envir.wHeight - 1)))
                    {
                        nX++;
                        nY -= 1;
                    }
                    break;
                case Grobal2.DR_DOWNRIGHT:
                    if ((nX > 0) && (nY > 0))
                    {
                        nX -= 1;
                        nY -= 1;
                    }
                    break;
            }
            result = true;
            return result;
        }

        public bool MakePosion(int nType, int nTime, int nPoint)
        {
            bool result = false;
            if (nType < Grobal2.MAX_STATUS_ATTRIBUTE)
            {
                var nOldCharStatus = m_nCharStatus;
                if (m_wStatusTimeArr[nType] > 0)
                {
                    if (m_wStatusTimeArr[nType] < nTime)
                    {
                        m_wStatusTimeArr[nType] = (ushort)nTime;
                    }
                }
                else
                {
                    m_wStatusTimeArr[nType] = (ushort)nTime;
                }
                m_dwStatusArrTick[nType] = HUtil32.GetTickCount();
                m_nCharStatus = GetCharStatus();
                m_btGreenPoisoningPoint = (byte)nPoint;
                // POIS-12. 红毒(state 0x1E=30)的伤害放大档位由 **level** 选择，
                // 而这个 level 在原版是【链表记录】里的值，不是位:
                //   767A94  B2 1E / E8 ->0x772960   HasState(0x1E)；没中毒就整档跳过
                //   767A9F  74 38                   je 0x767AD9
                //   767AA1  B2 1E / E8 ->0x773BEC   取该状态的 level
                //   767AAA  83 F8 04                cmp eax,4
                //   767AAD  75 15                   jne -> 走 1.2 档
                //   767AB5  D8 0D 3C7B7600          fmul dword[0x767B3C]  = 1.25  (float32 00 00 A0 3F)
                //   767ACA  DB 2D 407B7600          fld  xword[0x767B40]  = 1.2   (ext80 9A99..FF3F)
                // C# 侧 ApplyNativeStruckAmplifyStates / ApplyNativeTargetMidMagicStates
                // 用 TryGetNativeTimedAbilityValue(30) 读 level，可是 MakePosion 以前
                // 只写 legacy 槽 m_wStatusTimeArr[1]，从不写 level：实测(4 条腿)
                //   MakePosion(POISON_DAMAGEARMOR,60,pt=4) -> HasNativeActiveState(30)=True
                //   但 MidMagic(1000)=1200，即 **level 恒为 0，×1.25 档永远打不到**。
                // 所以真正的缺口是 level 不落地(不是"整档不触发"——×1.2 一直在生效)。
                // 这里补记 level：nPoint 就是原版 push 进 AddState 的那个 level 参数
                // (0x680ACC push 3 / 0x680AE0 push 0 / 0x666D42 push 1 同一位置)。
                if (nType == Grobal2.POISON_DAMAGEARMOR)
                {
                    RecordNativeRedPoisonLevel(nPoint);
                }
                if (nOldCharStatus != m_nCharStatus)
                {
                    StatusChanged();
                }
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    SysMsg(format(M2Share.sYouPoisoned, nTime, nPoint), MsgColor.Red, MsgType.Hint);
                }
                result = true;
            }
            return result;
        }

        
        
        
        
        public bool CheckServerMakeSlave()
        {
            bool result = false;
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    if (m_MsgList[i].wIdent == Grobal2.RM_10401)
                    {
                        result = true;
                        break;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        protected bool GetRecallXY(short nX, short nY, int nRange, ref short nDX, ref short nDY)
        {
            bool result = false;
            if (m_PEnvir.GetMovingObject(nX, nY, true) == null)
            {
                result = true;
                nDX = nX;
                nDY = nY;
            }
            if (!result)
            {
                for (int i = 0; i <= nRange; i++)
                {
                    for (int j = -i; j <= i; j++)
                    {
                        for (int k = -i; k <= i; k++)
                        {
                            nDX = (short)(nX + k);
                            nDY = (short)(nY + j);
                            if (m_PEnvir.GetMovingObject(nDX, nDY, true) == null)
                            {
                                result = true;
                                break;
                            }
                        }
                        if (result)
                        {
                            break;
                        }
                    }
                    if (result)
                    {
                        break;
                    }
                }
            }
            if (!result)
            {
                nDX = nX;
                nDY = nY;
            }
            return result;
        }

        public bool IsTrainingSkill(int nIndex)
        {
            bool result = false;
            TUserMagic UserMagic;
            for (int i = 0; i < m_MagicList.Count; i++)
            {
                UserMagic = m_MagicList[i];
                if ((UserMagic != null) && (UserMagic.wMagIdx == nIndex))
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        // The native 3000 ms bubble decrement (sub_76FFE8 @0x770064
        // `sub dword [eax+2],0xBB8`) now lives inside the single
        // ApplyNativeBubbleDefence port, so this wrapper has no callers left.
        public bool IsGuildMaster()
        {
            return (m_MyGuild != null) && (m_nGuildRankNo == 1);
        }

        public bool MagCanHitTarget(short nX, short nY, TBaseObject TargeTBaseObject)
        {
            bool result = false;
            int n18;
            if (TargeTBaseObject == null)
            {
                return result;
            }
            int n20 = Math.Abs(nX - TargeTBaseObject.m_nCurrX) + Math.Abs(nY - TargeTBaseObject.m_nCurrY);
            int n14 = 0;
            while (n14 < 13)
            {
                n18 = M2Share.GetNextDirection(nX, nY, TargeTBaseObject.m_nCurrX, TargeTBaseObject.m_nCurrY);
                if (m_PEnvir.GetNextPosition(nX, nY, n18, 1, ref nX, ref nY) && m_PEnvir.IsValidCell(nX, nY))
                {
                    if ((nX == TargeTBaseObject.m_nCurrX) && (nY == TargeTBaseObject.m_nCurrY))
                    {
                        result = true;
                        break;
                    }
                    else
                    {
                        int n1C = Math.Abs(nX - TargeTBaseObject.m_nCurrX) + Math.Abs(nY - TargeTBaseObject.m_nCurrY);
                        if (n1C > n20)
                        {
                            result = true;
                            break;
                        }
                        n1C = n20;
                    }
                }
                else
                {
                    break;
                }
                n14++;
            }
            return result;
        }

        private bool IsProperFriend_IsFriend(TBaseObject cret)
        {
            bool result = false;
            if (cret.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                switch (m_btAttatckMode)
                {
                    case M2Share.HAM_ALL:
                        result = true;
                        break;
                    case M2Share.HAM_PEACE:
                        result = true;
                        break;
                    case M2Share.HAM_DEAR:
                        if ((this == cret) || (cret == (this as TPlayObject).m_DearHuman))
                        {
                            result = true;
                        }
                        break;
                    case M2Share.HAM_MASTER:
                        if (this == cret)
                        {
                            result = true;
                        }
                        else if ((this as TPlayObject).m_boMaster)
                        {
                            for (int i = 0; i < (this as TPlayObject).m_MasterList.Count; i++)
                            {
                                if ((this as TPlayObject).m_MasterList[i] == cret)
                                {
                                    result = true;
                                    break;
                                }
                            }
                        }
                        else if ((cret as TPlayObject).m_boMaster)
                        {
                            for (int i = 0; i < (cret as TPlayObject).m_MasterList.Count; i++)
                            {
                                if ((cret as TPlayObject).m_MasterList[i] == this)
                                {
                                    result = true;
                                    break;
                                }
                            }
                        }
                        break;
                    case M2Share.HAM_GROUP:
                        if (cret == this)
                        {
                            result = true;
                        }
                        if (IsGroupMember(cret))
                        {
                            result = true;
                        }
                        break;
                    case M2Share.HAM_GUILD:
                        if (cret == this)
                        {
                            result = true;
                        }
                        if (m_MyGuild != null)
                        {
                            if (m_MyGuild.IsMember(cret.m_sCharName))
                            {
                                result = true;
                            }
                            if (m_boGuildWarArea && (cret.m_MyGuild != null))
                            {
                                if (m_MyGuild.IsAllyGuild(cret.m_MyGuild))
                                {
                                    result = true;
                                }
                            }
                        }
                        break;
                    case M2Share.HAM_PKATTACK:
                        if (cret == this)
                        {
                            result = true;
                        }
                        if (PKLevel() >= 2)
                        {
                            if (cret.PKLevel() < 2)
                            {
                                result = true;
                            }
                        }
                        else
                        {
                            if (cret.PKLevel() >= 2)
                            {
                                result = true;
                            }
                        }
                        break;
                }
            }
            return result;
        }

        public int MagMakeDefenceArea(int nX, int nY, int nRange, ushort nSec, byte btState)
        {
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            TBaseObject BaseObject;
            int result = 0;
            int nStartX = nX - nRange;
            int nEndX = nX + nRange;
            int nStartY = nY - nRange;
            int nEndY = nY + nRange;
            for (int i = nStartX; i <= nEndX; i++)
            {
                for (int j = nStartY; j <= nEndY; j++)
                {
                    var mapCell = false;
                    MapCellInfo = m_PEnvir.GetMapCellInfo(i, j, ref mapCell);
                    if (mapCell && (MapCellInfo.ObjList != null))
                    {
                        for (int k = 0; k < MapCellInfo.Count; k++)
                        {
                            OSObject = MapCellInfo.ObjList[k];
                            if ((OSObject != null) && (OSObject.CellType == CellType.OS_MOVINGOBJECT))
                            {
                                BaseObject = OSObject.CellObj as TBaseObject;
                                if ((BaseObject != null) && (!BaseObject.m_boGhost))
                                {
                                    if (IsProperFriend(BaseObject))
                                    {
                                        if (btState == 0)
                                        {
                                            BaseObject.DefenceUp(nSec);
                                        }
                                        else
                                        {
                                            BaseObject.MagDefenceUp(nSec);
                                        }
                                        result++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        private bool DefenceUp(ushort nSec)
        {
            bool result = false;
            if (m_wStatusTimeArr[Grobal2.STATE_DEFENCEUP] > 0)
            {
                if (m_wStatusTimeArr[Grobal2.STATE_DEFENCEUP] < nSec)
                {
                    m_wStatusTimeArr[Grobal2.STATE_DEFENCEUP] = nSec;
                    result = true;
                }
            }
            else
            {
                m_wStatusTimeArr[Grobal2.STATE_DEFENCEUP] = nSec;
                result = true;
            }
            m_dwStatusArrTick[Grobal2.STATE_DEFENCEUP] = HUtil32.GetTickCount();
            SysMsg(format(M2Share.g_sDefenceUpTime, nSec), MsgColor.Green, MsgType.Hint);
            RecalcAbilitys();
            SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
            return result;
        }

        public bool AttPowerUp(int nPower, int nTime)
        {
            m_wStatusArrValue[0] = (ushort)nPower;
            m_dwStatusArrTimeOutTick[0] = HUtil32.GetTickCount() + nTime * 1000;
            int nMin = nTime / 60;
            int nSec = nTime % 60;
            SysMsg(format(M2Share.g_sAttPowerUpTime, nMin, nSec), MsgColor.Green, MsgType.Hint);
            RecalcAbilitys();
            SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
            return true;
        }

        private bool MagDefenceUp(ushort nSec)
        {
            bool result = false;
            if (m_wStatusTimeArr[Grobal2.STATE_MAGDEFENCEUP] > 0)
            {
                if (m_wStatusTimeArr[Grobal2.STATE_MAGDEFENCEUP] < nSec)
                {
                    m_wStatusTimeArr[Grobal2.STATE_MAGDEFENCEUP] = nSec;
                    result = true;
                }
            }
            else
            {
                m_wStatusTimeArr[Grobal2.STATE_MAGDEFENCEUP] = nSec;
                result = true;
            }
            m_dwStatusArrTick[Grobal2.STATE_MAGDEFENCEUP] = HUtil32.GetTickCount();
            SysMsg(format(M2Share.g_sMagDefenceUpTime, nSec), MsgColor.Green, MsgType.Hint);
            RecalcAbilitys();
            SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
            return result;
        }

        
        
        
        
        public bool MagBubbleDefenceUp(byte nLevel, ushort nSec)
        {
            return AddNativeBubbleTimedAbility(nLevel, nSec);
        }

        public TUserItem CheckItemCount(string sItemName, ref int nCount)
        {
            TUserItem result = null;
            nCount = 0;
            for (int i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                if (m_UseItems[i] == null)
                {
                    continue;
                }
                var sName = M2Share.UserEngine.GetStdItemName(m_UseItems[i].wIndex);
                if (string.Compare(sName, sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    result = m_UseItems[i];
                    nCount++;
                }
            }
            return result;
        }

        public TUserItem CheckItems(string sItemName)
        {
            TUserItem result = null;
            TUserItem UserItem;
            for (int i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                if (UserItem == null)
                {
                    continue;
                }
                if (string.Compare(M2Share.UserEngine.GetStdItemName(UserItem.wIndex), sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    result = UserItem;
                    break;
                }
            }
            return result;
        }

        protected void DelBagItem(int nIndex)
        {
            if ((nIndex < 0) || (nIndex >= m_ItemList.Count))
            {
                return;
            }
            Dispose(m_ItemList[nIndex]);
            m_ItemList.RemoveAt(nIndex);
        }

        public bool DelBagItem(int nItemIndex, string sItemName)
        {
            TUserItem UserItem;
            bool result = false;
            for (int i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                if ((UserItem.MakeIndex == nItemIndex) && string.Compare(M2Share.UserEngine.GetStdItemName(UserItem.wIndex), sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    Dispose(UserItem);
                    m_ItemList.RemoveAt(i);
                    result = true;
                    break;
                }
            }
            if (result)
            {
                WeightChanged();
            }
            return result;
        }

        public bool CanMove(short nX, short nY, bool boFlag)
        {
            if (Math.Abs(m_nCurrX - nX) <= 1 && Math.Abs(m_nCurrY - nY) <= 1)
            {
                return m_PEnvir.CanWalkEx(nX, nY, boFlag);
            }
            return CanRun(nX, nY, boFlag);
        }

        public bool CanMove(short nCurrX, short nCurrY, short nX, short nY, bool boFlag)
        {
            if ((Math.Abs(nCurrX - nX) <= 1) && (Math.Abs(nCurrY - nY) <= 1))
            {
                return m_PEnvir.CanWalkEx(nX, nY, boFlag);
            }
            else
            {
                return CanRun(nCurrX, nCurrY, nX, nY, boFlag);
            }
        }

        public bool CanRun(short nCurrX, short nCurrY, short nX, short nY, bool boFlag)
        {
            var result = false;
            var btDir = M2Share.GetNextDirection(nCurrX, nCurrY, nX, nY);
            switch (btDir)
            {
                case Grobal2.DR_UP:
                    if (nCurrY > 1)
                    {
                        if ((m_PEnvir.CanWalkEx(nCurrX, nCurrY - 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()))
                                && (m_PEnvir.CanWalkEx(nCurrX, nCurrY - 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                        {
                            result = true;
                            return result;
                        }
                    }
                    break;
                case Grobal2.DR_UPRIGHT:
                    if (nCurrX < m_PEnvir.wWidth - 2 && nCurrY > 1)
                    {
                        if ((m_PEnvir.CanWalkEx(nCurrX + 1, nCurrY - 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) &&
                            (m_PEnvir.CanWalkEx(nCurrX + 2, nCurrY - 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                        {
                            result = true;
                            return result;
                        }
                    }
                    break;
                case Grobal2.DR_RIGHT:
                    if (nCurrX < m_PEnvir.wWidth - 2)
                    {
                        if (m_PEnvir.CanWalkEx(nCurrX + 1, nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()) &&
                         (m_PEnvir.CanWalkEx(nCurrX + 2, nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                        {
                            result = true;
                            return result;
                        }
                    }
                    break;
                case Grobal2.DR_DOWNRIGHT:
                    if ((nCurrX < m_PEnvir.wWidth - 2) && (nCurrY < m_PEnvir.wHeight - 2) && (m_PEnvir.CanWalkEx(nCurrX + 1, nCurrY + 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) ||
                        M2Share.g_Config.boSafeAreaLimited && InSafeZone()) && (m_PEnvir.CanWalkEx(nCurrX + 2, nCurrY + 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_DOWN:
                    if ((nCurrY < m_PEnvir.wHeight - 2) &&
                        (m_PEnvir.CanWalkEx(nCurrX, nCurrY + 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()) &&
                        (m_PEnvir.CanWalkEx(nCurrX, nCurrY + 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()))))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_DOWNLEFT:
                    if ((nCurrX > 1) && (nCurrY < m_PEnvir.wHeight - 2) && (m_PEnvir.CanWalkEx(nCurrX - 1, nCurrY + 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) &&
                    (m_PEnvir.CanWalkEx(nCurrX - 2, nCurrY + 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_LEFT:
                    if ((nCurrX > 1) && (m_PEnvir.CanWalkEx(nCurrX - 1, nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) &&
                    (m_PEnvir.CanWalkEx(nCurrX - 2, nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_UPLEFT:
                    if ((nCurrX > 1) && (nCurrY > 1) && (m_PEnvir.CanWalkEx(nCurrX - 1, nCurrY - 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll))
                    || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) && (m_PEnvir.CanWalkEx(nCurrX - 2, nCurrY - 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) ||
                    (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
            }
            return false;
        }

        private bool CanRun(short nX, short nY, bool boFlag)
        {
            var result = false;
            var btDir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nX, nY);
            switch (btDir)
            {
                case Grobal2.DR_UP:
                    if (m_nCurrY > 1)
                    {
                        if ((m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY - 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()))
                                && (m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY - 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                        {
                            result = true;
                            return result;
                        }
                    }
                    break;
                case Grobal2.DR_UPRIGHT:
                    if (m_nCurrX < m_PEnvir.wWidth - 2 && m_nCurrY > 1)
                    {
                        if ((m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY - 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) &&
                            (m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY - 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                        {
                            result = true;
                            return result;
                        }
                    }
                    break;
                case Grobal2.DR_RIGHT:
                    if (m_nCurrX < m_PEnvir.wWidth - 2)
                    {
                        if (m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()) &&
                         (m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                        {
                            result = true;
                            return result;
                        }
                    }
                    break;
                case Grobal2.DR_DOWNRIGHT:
                    if ((m_nCurrX < m_PEnvir.wWidth - 2) && (m_nCurrY < m_PEnvir.wHeight - 2) && (m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY + 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) ||
                        M2Share.g_Config.boSafeAreaLimited && InSafeZone()) && (m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY + 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_DOWN:
                    if ((m_nCurrY < m_PEnvir.wHeight - 2) &&
                        (m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY + 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()) &&
                        (m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY + 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone()))))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_DOWNLEFT:
                    if ((m_nCurrX > 1) && (m_nCurrY < m_PEnvir.wHeight - 2) && (m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY + 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) &&
                    (m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY + 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_LEFT:
                    if ((m_nCurrX > 1) && (m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) &&
                    (m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
                case Grobal2.DR_UPLEFT:
                    if ((m_nCurrX > 1) && (m_nCurrY > 1) && (m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY - 1, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll))
                    || (M2Share.g_Config.boSafeAreaLimited && InSafeZone())) && (m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY - 2, M2Share.g_Config.boDiableHumanRun || ((m_btPermission > 9) && M2Share.g_Config.boGMRunAll)) ||
                    (M2Share.g_Config.boSafeAreaLimited && InSafeZone())))
                    {
                        result = true;
                        return result;
                    }
                    break;
            }
            return false;
        }

        public TBaseObject GetMaster()
        {
            if (m_btRaceServer != Grobal2.RC_PLAYOBJECT)
            {
                TBaseObject MasterObject = m_Master;
                if (MasterObject != null)
                {
                    while (true)
                    {
                        if (MasterObject.m_Master != null)
                        {
                            MasterObject = MasterObject.m_Master;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                return MasterObject;
            }
            return null;
        }

        public bool ReAliveEx(MonGenInfo MonGen)
        {
            m_WAbil = m_Abil;
            m_nGold = 0;
            
            m_boNoItem = false;
            m_boStoneMode = false;
            m_boSkeleton = false;
            m_boHolySeize = false;
            m_boCrazyMode = false;
            m_boShowHP = false;
            
            m_boFixedHideMode = false;

            if (this is CastleDoor)
            {
                ((CastleDoor)(this)).m_boOpened = false;
                ((CastleDoor)(this)).m_boStickMode = true;
            }
            if (this is MagicMonster)
            {
                ((MagicMonster)(this)).m_boDupMode = false;
            }
            if (this is MagicMonObject)
            {
                ((MagicMonObject)(this)).m_boUseMagic = false;
            }
            if (this is RockManObject)
            {
                ((RockManObject)(this)).m_boHideMode = false;
            }
            if (this is WallStructure)
            {
                ((WallStructure)(this)).boSetMapFlaged = false;
            }
            if (this is SoccerBall)
            {
                ((SoccerBall)(this)).n550 = 0;
                ((SoccerBall)(this)).m_nTargetX = -1;
            }
            if (this is FrostTiger)
            {
                
            }
            if (this is CowKingMonster)
            {
                
            }
            if (this is DigOutZombi)
            {
                ((DigOutZombi)(this)).m_boFixedHideMode = true;
            }
            if (this is WhiteSkeleton)
            {
                ((WhiteSkeleton)(this)).m_boIsFirst = true;
                ((WhiteSkeleton)(this)).m_boFixedHideMode = true;
            }
            if (this is ScultureMonster)
            {
                ((DigOutZombi)(this)).m_boFixedHideMode = true;
            }
            if (this is ScultureKingMonster)
            {
                ((ScultureKingMonster)(this)).m_boStoneMode = true;
                ((ScultureKingMonster)(this)).m_nCharStatusEx = Grobal2.STATE_STONE_MODE;
            }
            if (this is ElfMonster)
            {
                ((ElfMonster)(this)).m_boFixedHideMode = true;
                ((ElfMonster)(this)).m_boNoAttackMode = true;
                ((ElfMonster)(this)).boIsFirst = true;
            }
            if (this is ElfWarriorMonster)
            {
                ((ElfWarriorMonster)(this)).m_boFixedHideMode = true;
                ((ElfWarriorMonster)(this)).boIsFirst = true;
                ((ElfWarriorMonster)(this)).m_boUsePoison = false;
            }
            if (this is ElectronicScolpionMon)
            {
                ((ElectronicScolpionMon)(this)).m_boUseMagic = false;
                
            }
            if (this is DoubleCriticalMonster)
            {
                
            }
            if (this is StickMonster)
            {
                ((StickMonster)(this)).m_dwSearchTick = HUtil32.GetTickCount();
                ((StickMonster)(this)).m_boFixedHideMode = true;
                ((StickMonster)(this)).m_boStickMode = true;
            }

            m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(3500) + 3000);
            
            m_nProcessRunCount = 0;
            
            

            switch (this.m_btRaceServer)
            {
                case 51:
                    m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(3500) + 3000);
                    m_nBodyLeathery = 50;
                    break;
                case 52:
                    if (M2Share.RandomNumber.Random(30) == 0)
                    {
                        m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(20000) + 10000);
                        m_nBodyLeathery = 150;
                    }
                    else
                    {
                        m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(8000) + 8000);
                        m_nBodyLeathery = 150;
                    }
                    break;
                case 53:
                    m_nMeatQuality = (ushort)(M2Share.RandomNumber.Random(8000) + 8000);
                    m_nBodyLeathery = 150;
                    break;
                case 54:
                    m_boAnimal = true;
                    break;
                case 95:
                    if (M2Share.RandomNumber.Random(2) == 0)
                    {
                        
                    }
                    break;
                case 96:
                    if (M2Share.RandomNumber.Random(4) == 0)
                    {
                        
                    }
                    break;
                case 97:
                    if (M2Share.RandomNumber.Random(2) == 0)
                    {
                        
                    }
                    break;
                case 169:
                    m_boStickMode = false;
                    break;
                case 170:
                    m_boStickMode = true;
                    break;
            }
            m_UseItems = new TUserItem[8];
            for (int i = 0; i < m_ItemList.Count; i++)
            {
                m_ItemList[i] = null;
            }
            m_ItemList.Clear();

            OnEnvirnomentChanged();
            m_nCharStatus = GetCharStatus();
            StatusChanged();
            if (m_PEnvir == null)
            {
                return false;
            }
            var nX = (MonGen.nX - MonGen.nRange) + M2Share.RandomNumber.Random(MonGen.nRange * 2 + 1);
            var nY = (MonGen.nY - MonGen.nRange) + M2Share.RandomNumber.Random(MonGen.nRange * 2 + 1);
            var m_boErrorOnInit = true;
            if (m_PEnvir.CanWalk(nX, nY, true))
            {
                m_nCurrX = (short)nX;
                m_nCurrY = (short)nY;
                if (AddToMap())
                {
                    m_boErrorOnInit = false;
                }
            }
            var nRange = 0;
            var nRange2 = 0;
            if (m_boErrorOnInit)
            {
                if (m_PEnvir.wWidth < 50)
                {
                    nRange = 2;
                }
                else
                {
                    nRange = 3;
                }
                if ((m_PEnvir.wHeight < 250))
                {
                    if ((m_PEnvir.wHeight < 30))
                    {
                        nRange2 = 2;
                    }
                    else
                    {
                        nRange2 = 20;
                    }
                }
                else
                {
                    nRange2 = 50;
                }
            }

            var nC = 0;
            object addObj = null;
            var nX2 = m_nCurrX;
            var nY2 = m_nCurrY;
            while (true)
            {
                if (!m_PEnvir.CanWalk(nX, nY, false))
                {
                    if ((m_PEnvir.wWidth - nRange2 - 1) > nX)
                    {
                        nX = nX + nRange;
                    }
                    else
                    {
                        nX = M2Share.RandomNumber.Random(m_PEnvir.wWidth / 2) + nRange2;
                    }
                    if (m_PEnvir.wHeight - nRange2 - 1 > nY)
                    {
                        nY = nY + nRange;
                    }
                    else
                    {
                        nY = M2Share.RandomNumber.Random(m_PEnvir.wHeight / 2) + nRange2;
                    }
                }
                else
                {
                    m_nCurrX = (short)nX;
                    m_nCurrY = (short)nY;
                    addObj = m_PEnvir.AddToMap(nX, nY, CellType.OS_MOVINGOBJECT, this);
                    break;
                }
                nC++;
                if (nC > 46)
                {
                    break;
                }
            }
            if (addObj == null)
            {
                m_nCurrX = nX2;
                m_nCurrY = nY2;
                m_PEnvir.AddToMap(m_nCurrX, m_nCurrY, CellType.OS_MOVINGOBJECT, this);
            }

            m_Abil.HP = m_Abil.MaxHP;
            m_Abil.MP = m_Abil.MaxMP;
            m_WAbil.HP = m_WAbil.MaxHP;
            m_WAbil.MP = m_WAbil.MaxMP;

            RecalcAbilitys();

            m_boDeath = false;
            m_boInvisible = false;

            SendRefMsg(Grobal2.RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, GetFeatureToLong(), GetShowName());

            if (M2Share.g_Config.boMonSayMsg)
            {
                MonsterSayMsg(null, MonStatus.MonGen);
            }
            return true;
        }

        internal void OnEnvirnomentChanged()
        {
            if (m_boCanReAlive)
            {
                if ((m_pMonGen != null) && (m_pMonGen.Envir != m_PEnvir))
                {
                    m_boCanReAlive = false;
                    if (m_pMonGen.nActiveCount > 0)
                    {
                        m_pMonGen.nActiveCount--;
                    }
                    m_pMonGen = null;
                }
            }
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
        }

        internal void Dispose(object obj)
        {
            obj = null;
        }

        internal string format(string str, params object[] par)
        {
            return string.Format(str, par);
        }
    }
}
