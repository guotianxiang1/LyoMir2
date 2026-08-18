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

        // m_wStatusTimeArr is now a forwarding view onto the native Self+0xDC
        // node list; see TBaseObject.LegacyStatusTimeView.cs. It has no storage,
        // so there is nothing to declare here and nothing to allocate.
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

        // 战神群对象 [group+0x40]：CM 1089 组长广播时 sub_727628 @0x72763C
        //   8B 45 FC / 8B 55 F8 / 89 50 40  mov [group+0x40],edx  把包里的 Recog
        // 缓存到群对象。C# 无独立群对象，用组长身份对象（m_GroupOwner==this）承载。
        public int m_NativeGroupBroadcastRecog = 0;

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
        /// <summary>
        /// 战神 byte[self+0x47F].  Two consumers share it and each one both tests and
        /// arms it, so the monster's drop table can be consumed exactly once per
        /// instance no matter which of them runs first:
        /// <code>
        /// ; sub_71FA20 @AfterScatterItems
        /// 71FA50  80 B8 7F 04 00 00 00  cmp byte [eax+0x47F],0
        /// 71FA57  0F 85 35 06 00 00     jne 0x720092          ; whole function exits
        /// 71FA6C  C6 80 7F 04 00 00 01  mov byte [eax+0x47F],1
        /// ; sub_71EC88 (deliver-to-killer path)
        /// 71ECB1  80 BB 7F 04 00 00 00  cmp byte [ebx+0x47F],0
        /// 71ECB8  0F 85 B8 00 00 00     jne 0x71ED76
        /// 71ECBE  C6 83 7F 04 00 00 01  mov byte [ebx+0x47F],1
        /// </code>
        /// Full-image scan of disp32 0x47F finds exactly these two writes, both
        /// storing 1, and no clear site: the monster constructor's zero-fill at
        /// 0x71D840.. is the only thing that ever puts it back to 0, so the flag is
        /// per-instance and one-way.
        ///
        /// The two <c>call [esi+0x1FC]</c> sites in monster Die (0x71E3D2 / 0x71E3EF)
        /// are the if/else arms of <c>0x71E3C2 je 0x71E3DA</c> and cannot both run,
        /// so they are NOT what this guards; sub_71EC88 and any later re-entry are.
        /// </summary>
        public bool m_boNativeScatterConsumed = false;
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

        /// <summary>
        /// <c>[self+0x1D1]</c> = agg2[0x21] after <c>sub_73D500</c> @0x73D63D copy.
        /// Set by ext-abil ident 0x50 @0x76235F. Feeds <c>sub_746084</c>.
        /// </summary>
        internal byte m_btNativeSecondPathFlag;

        /// <summary>
        /// <c>[self+0x1DD]</c> = agg2[0x2D]. Tier for <c>sub_74609C</c> cooldown table.
        /// </summary>
        internal byte m_btNativeSecondPathTier;
        
        
        
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

        /// <summary>Native [+0x714] — display bit toggled in sub_6B6B78 @0x6B6BD7..0x6B6BDA.</summary>
        private byte m_btNativeSafeZonePkDisplay;
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
        public bool m_boFireHitSkill = false;
        public bool m_bo41kill = false;
        public bool m_boTwinHitSkill = false;
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

        /// <summary>
        /// Native sub_765D94 (TPlayer VMT+0x00), the single predicate every cell
        /// scan reaches when it asks "does this actor block the cell?". Native
        /// defaults to passable and only zeroes the answer at the bottom, so the
        /// blocking case is the whole conjunction:
        /// <code>
        /// 00765D9B  B3 01                 mov  bl,1               ; default passable
        /// 00765D9D  80 7E 73 00           cmp  byte [esi+0x73],0  ; m_boGhost
        /// 00765DA1  75 2A                 jne  0x765DCD
        /// 00765DA3  80 BE E6 02 00 00 00  cmp  byte [esi+0x2E6],0 ; bo2B9
        /// 00765DAA  74 21                 je   0x765DCD
        /// 00765DAE  E8 F5 CF 00 00        call 0x772DA8           ; byte [eax+0x74] death
        /// 00765DB5  75 16                 jne  0x765DCD
        /// 00765DB7  80 BE E3 02 00 00 00  cmp  byte [esi+0x2E3],0 ; m_boFixedHideMode
        /// 00765DBE  75 0D                 jne  0x765DCD
        /// 00765DC2  E8 F1 D0 00 00        call 0x772EB8           ; pass-through grant
        /// 00765DC9  75 02                 jne  0x765DCD
        /// 00765DCB  33 DB                 xor  ebx,ebx            ; blocks
        /// </code>
        /// MOVE-33: the sixth term was missing in C#, which had inlined only the
        /// m_boObMode half of 0x772EB8 and dropped the bodyState 0x3C half. The
        /// expression was also copied five times inside Envirnoment.cs, so the
        /// term is added here once instead.
        /// </summary>
        internal bool IsNativeCellBlocking()
        {
            return !m_boGhost && bo2B9 && !m_boDeath && !m_boFixedHideMode
                   && !HasNativeCellPassThroughGrant();
        }

        /// <summary>
        /// Native sub_772EB8, the unconditional pass-through grant consumed by
        /// sub_765D94 at 0x765DC2. It is a disjunction of two terms:
        /// <code>
        /// 00772EBE  80 BB E2 02 00 00 00  cmp  byte [ebx+0x2E2],0 ; m_boObMode
        /// 00772EC5  75 12                 jne  0x772ED9           ; -> TRUE
        /// 00772EC7  B2 3C                 mov  dl,0x3C
        /// 00772ECB  E8 90 FA FF FF        call 0x772960           ; InBodyState(0x3C)
        /// 00772ED2  75 05                 jne  0x772ED9           ; -> TRUE
        /// 00772ED4  33 C0                 xor  eax,eax            ; FALSE
        /// </code>
        /// </summary>
        internal bool HasNativeCellPassThroughGrant()
        {
            return m_boObMode || HasNativeActiveState(0x3C);
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
            ClearLegacyStatusSlots();
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
            // Native sub_6B6B78 @0x006B6B78 — FreePK setter; 0x6B6BC4 calls sub_76858C.
            if (!boWarFlag && m_MyGuild != null && m_MyGuild.GuildWarList.Count > 0)
                boWarFlag = true;

            if (m_boInFreePKArea != boWarFlag)
            {
                m_boInFreePKArea = boWarFlag;
                m_boNameColorChanged = true;
                if (this is TPlayObject playObject)
                    playObject.SendNativeMapEntryStateMessages();
            }

            var displayFlag = boWarFlag;
            if (displayFlag && InNativeSafeZone12())
                displayFlag = false;

            var b714 = (byte)(displayFlag ? 1 : 0);
            if (b714 == m_btNativeSafeZonePkDisplay)
            {
                m_btNativeSafeZonePkDisplay = (byte)(m_btNativeSafeZonePkDisplay ^ 1);
                RefNameColor();
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
            const string exceptionMessage =
                "[Exception]: TCreature.DropItemDown";
            if (UserItem == null)
                return false;

            try
            {
                var stdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                if (stdItem == null)
                    return false;

                // sub_7688A0 @0x7688EA..0x768901: only callers whose final
                // boolean argument is zero run sub_78389C mode 5. Death/scatter
                // callers pass one and bypass this classifier.
                if (!boDieDrop
                    && NativeItemDropDestroy.CheckTransferPermission(UserItem,
                        stdItem, NativeItemDropDestroy.TransferModeDrop) != 0)
                {
                    return false;
                }

                var itemName = M2Share.UserEngine.GetStdItemName(
                    UserItem.wIndex);
                var mapItem = new MapItem
                {
                    UserItem = UserItem,
                    Name = itemName,
                    Looks = stdItem.Looks,
                    AniCount = unchecked((byte)stdItem.AniCount),
                    Reserved = 0,
                    Count = 1,
                    OfBaseObject = ItemOfCreat,
                    CanPickUpTick = HUtil32.GetTickCount(),
                    DropBaseObject = DropCreat
                };

                var dx = 0;
                var dy = 0;
                GetDropPosition(m_nCurrX, m_nCurrY, nScatterRange, ref dx, ref dy);
                var placed = (MapItem)m_PEnvir.AddToMap(dx, dy,
                    CellType.OS_ITEMOBJECT, mapItem);
                if (!ReferenceEquals(placed, mapItem))
                    return false;

                // sub_768934..0x768939 confirms placement before sub_7839E8;
                // these class-specific mutations belong to that committed arm.
                if (stdItem.StdMode == 40)
                {
                    UserItem.Dura = unchecked((ushort)Math.Max(0,
                        UserItem.Dura - 2000));
                }
                if (stdItem.StdMode == 45)
                {
                    mapItem.Looks = unchecked((ushort)M2Share.GetRandomLook(
                        mapItem.Looks, stdItem.Shape));
                }

                SendRefMsg(Grobal2.RM_ITEMSHOW, mapItem.Looks, mapItem.Id,
                    dx, dy, mapItem.Name);
                // sub_783984 @0x783984 is an unconditional zero-return stub,
                // so every successful sub_7688A0 placement reaches action 7/15.
                var logType = boDieDrop ? (byte)0x0F : (byte)0x07;
                var logReason = m_btRaceServer == Grobal2.RC_HEROOBJECT
                                && m_Master != null
                    ? m_Master.m_sCharName
                    : "1";
                var logQuantity = stdItem.StdMode == 7 ? UserItem.Dura : 1;
                M2Share.AddNativeGameDataLog(this, logType, itemName,
                    UserItem.MakeIndex, logQuantity, logReason);
                return true;
            }
            catch (Exception exception)
            {
                M2Share.ErrorMessage(exceptionMessage + " "
                                     + exception.Message);
                return false;
            }
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
                // 屏蔽元宝增减信息 @0x6F8288 等 8 站点：启用时不发 RM_GAMEGOLDCHANGED。
                if (Plugins.YanshenPangu1Patches.ShouldSuppressGameGoldClientMsg())
                    return;
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
            // VMT+0x240 的真身是 sub_6BDBA0，它**忽略** edx 里的前一等级，直接
            // 从对象重读已自增的等级：
            //   0x006BDBC5  0f b7 93 78 02 00 00  movzx edx,word [ebx+0x278]
            //   0x006BDBCE  e8 f5 20 ff ff        call 0x6AFCC8   (GetLevelExp)
            //   0x006BDBD3  89 83 c0 02 00 00     mov [ebx+0x2C0],eax  (MaxExp)
            // 归属证据：0x6BDBA0 在全镜像只有两处 dword 引用，0x0062F1CC 与
            // 0x006ACB08，二者各自减 0x240 得到的 0x0062EF8C / 0x006AC8C8 都是
            // 145/145 全代码指针的 VMT。被误引的 sub_6BA140（及其孪生
            // sub_6BA7BC）**零 dword 引用**，根本不在任何 VMT 里，而且写的是
            // [edi+0x1E8+0x5C]=+0x244、按 [edi+0x72] 分职业，是另一套对象布局。
            // 0x6C0543 的 `dec edx` 只影响转发给 sub_73EE14 的第二个参数。
            // nLevel（前一等级）因此不参与 MaxExp 计算。
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
                    // MOVE-39 —— 提交新格后清定时状态 0x17。两个 mover 都有这一步：
                    //   人形 sub_741224  0x7412E8  B2 17          mov  dl,0x17
                    //                    0x7412EC  E8 DF A1 02 00 call 0x76B4D0
                    //   怪物 sub_71F0F4  0x71F21C  B2 17          mov  dl,0x17
                    //                    0x71F220  E8 AB C2 04 00 call 0x76B4D0
                    // 都在成功提交 X/Y 之后（人形 0x7412D5、怪物 0x71F203），失败臂
                    // （0x7412CF / 0x71F201 的 je）直接跳过，所以本端也只在位置真的
                    // 变了的这条臂上清。sub_76B4D0 只是 sub_7731C0 的薄壳：按
                    // [node+1] 匹配定时状态链表节点、摘链、经 vmt+0x5C 通知丢失，
                    // 即 C# 的 RemoveTimedAbilityInternal。
                    // 相对清 0x17 的次序：人形在广播(0x741315)之前，怪物在广播
                    // (0x71F217)之后；四 mover 均在 sub_778EC0 之前广播。RM_WALK 载荷
                    // 只有 Dir/X/Y，不含状态位，故清 0x17 与广播的相对次序无可观测差。
                    RemoveNativeMovementTimedState(23);
                    // MOVE-39 — Walk() now matches sub_741224: 0x741315 then 0x741323;
                    // native discards 778EC0's return, so never roll back here.
                    Walk(Grobal2.RM_WALK);
                    if (m_boTransparent && m_boHideMode)
                    {
                        m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 1;
                    }
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        m_dwSearchTick = 0;
                    }
                    // MOVE-39 — 人形 mover 尾部同伴跟随 0x741328..0x741350，在广播与
                    // sub_778EC0 之后；sub_741224 不因 778EC0 失败跳过此步。
                    OnNativeHumanWalkMoverCommitted();
                    result = true;
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
            // 眼神「BB杀怪触发」把 sub_71F3D0 的收尾 0x71F467 `5E 5B 59 5D C3` 换成
            // jmp 桩体，桩体重放 pop esi/pop ebx，派发 @BBupr，再重放 pop ecx/pop ebp/ret。
            // 所以它跑在【整段升级判定之后】，而且是纯通知：宿主行为一字未改。
            GameSvr.Plugins.YanshenTriggerDispatch.FireSlaveGainExp(this);
        }

        protected bool DropGoldDown(int nGold, bool boFalg, TBaseObject GoldOfCreat, TBaseObject DropGoldCreat)
        {
            bool result = false;
            int nX = 0;
            int nY = 0;
            string s20;
            MapItem MapItem = new MapItem
            {
                Name = Grobal2.sSTRING_GOLDNAME,
                Count = nGold,
                Looks = M2Share.GetGoldShape(nGold),
                OfBaseObject = GoldOfCreat,
                CanPickUpTick = HUtil32.GetTickCount(),
                DropBaseObject = DropGoldCreat
            };
            // 金币半径是立即数：sub_768AAC @0x768ADC `6A 03 push 3` 直接压给
            // 0x768AF4 call sub_768688。这里原先还算了个 _MIN(nDropItemRage,7)
            // 的局部，从未被用过，且那个旋钮全镜像零命中，已删。
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
            // 0x6C1392 / 0x6C139D call sub_76858C on cert1/cert2 (not sub_7684DC).
            if (cert1.InNativeSafeZone12() || cert2.InNativeSafeZone12())
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
            // 眼神「英雄倍攻和暴击」的桩体挂 0x76C816（`cmp [ebx+0x84],0`，7 字节），
            // 也就是上面那道 nPower 钳零（0x76C810..0x76C814）之后、幸运掷点之前。
            // 它改写的是 edi = nBasePower，与挂 0x76C88B 改返回值的 @baoji 是两条不同的臂：
            // 类门只放行 TTaosHero/TWarHero/TMagHero，倍率取主人的 S(1,44/45/46)。
            nBasePower = GameSvr.Plugins.YanshenTriggerDispatch.FireHerobaoji(this, nBasePower);
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
            // 掷点到此为止就是 sub_76C804 的全部；VMT+0xCC 槽 0x767F10 只是
            // `call 0x76C804` 的 thunk。眼神「新倍攻和暴击」的桩体覆盖 0x76C88B 的
            // `8B C6 5F 5E 5B`（mov eax,esi + pop edi/esi/ebx），在改写 esi 后重放
            // 这五字节并 jmp 0x76C890，所以它作用的正是这里的 result。
            if (this is TPlayObject baojiPlayer)
            {
                result = GameSvr.Plugins.YanshenTriggerDispatch.FireBaoji(baojiPlayer, result);
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
                        // sub_76834C @0x7683D0 writes the back direction before
                        // both RM_PUSH and the horse-partner callback. It restores
                        // the requested direction at 0x76841E for the next step.
                        m_btDirection = nBackDir;
                        SendRefMsg(Grobal2.RM_PUSH, nBackDir, m_nCurrX, m_nCurrY, 0, "");
                        // sub_76834C @0x7683E6..0x768419: after each
                        // successful push step, a mounted player's passenger
                        // follows through the lightweight sub_6BBF4C wrapper.
                        (this as TPlayObject)?.SyncNativeHorsePartnerAfterPush(
                            nBackDir);
                        m_btDirection = nDir;
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
            // TPlayer overrides CharPushed and cancels an open trade, but only when
            // the player actually moved. VMT [0x6AC8C8+0xA4] holds 0x6BFD1C, and the
            // override reads:
            //   0x6BFD28  B2 34              mov dl, 0x34
            //   0x6BFD2C  E8 2F 2C 0B 00     call 0x772960       ; state 0x34 set?
            //   0x6BFD33  74 04              je  0x6BFD39
            //   0x6BFD35  33 F6 / EB 18      xor esi,esi / jmp   ; blocked, no cancel
            //   0x6BFD3F  E8 08 86 0A 00     call 0x76834C       ; inherited, eax=steps
            //   0x6BFD46  85 F6              test esi, esi
            //   0x6BFD48  7E 07              jle 0x6BFD51        ; 0 steps -> no cancel
            //   0x6BFD4C  E8 73 46 00 00     call 0x6C43C4       ; DealCancel
            // So the gate is "moved at least one cell", not "took damage": a push that
            // was fully blocked leaves the trade standing.
            if (result > 0 && m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                (this as TPlayObject)?.DealCancel();
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
            // POIS-18 — native IncHealthSpell @0x769DB4 halves both amounts while
            // bodyState 0x66 is held, between the negative guard and the clamped adds:
            //   769DC9  85 F6 / 7C 74      test esi,esi / jl  return    ; nHP < 0
            //   769DCF  85 FF / 7C 70      test edi,edi / jl  return    ; nMP < 0
            //   769DD1  B2 66              mov  dl, 0x66
            //   769DD3  8B C3 / E8 86 8B 00 00  call 0x772960           ; HasState(0x66)
            //   769DDA  84 C0 / 74 14      test al,al / je 0x769DF2
            //   769DDE  D1 FE / 79 03 / 83 D6 00   sar esi,1 (toward zero)
            //   769DE8  D1 FF / 79 03 / 83 D7 00   sar edi,1
            // Both operands are already non-negative here, so the sar/adc pair is
            // plain integer division by two.
            if (HasNativeActiveState(0x66))
            {
                nHP /= 2;
                nMP /= 2;
            }
            m_WAbil.HP = (int)Math.Min((long)m_WAbil.HP + nHP,
                Math.Max(0, m_WAbil.MaxHP));
            m_WAbil.MP = (int)Math.Min((long)m_WAbil.MP + nMP,
                Math.Max(0, m_WAbil.MaxMP));
            HealthSpellChanged();
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
        /// MOVE-61/62: the loop is preceded by two independent per-axis seeders.
        /// MOVE-63: native keeps one body for all 11 callers, so this is the single
        /// C# authority; SpaceMove_GetRandXY and TPlayObject's
        /// TryResolveNativeUserMoveCoordinates are both adapters over it.
        /// <code>
        /// 7782E4  83 3F 00     cmp dword [edi],0    ; *pX, signed
        /// 7782E7  7F 0B        jg  0x7782F4         ; only >0 keeps the caller value
        /// 7782E9  8B 43 3C     mov eax,[ebx+0x3C]   ; Width
        /// 7782EC  E8 5B B8 C8 FF call 0x403B4C      ; Random(Width)
        /// 7782F1  40           inc eax              ; 1..Width (X==Width is possible)
        /// 7782F2  89 07        mov [edi],eax
        /// 7782F4..778308        the same against Height (+0x40) for *pY
        /// </code>
        /// The X seeder runs before the Y seeder, which fixes the RNG call order.
        internal static bool NativeGetRandomXY(Envirnoment Envir,
            ref int nX, ref int nY)
        {
            if (nX <= 0)
            {
                nX = M2Share.RandomNumber.Random(Envir.wWidth) + 1;
            }
            if (nY <= 0)
            {
                nY = M2Share.RandomNumber.Random(Envir.wHeight) + 1;
            }
            var nStep = Envir.wWidth < 50 ? 2 : 3;
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
                    nX = M2Share.RandomNumber.Random(Envir.wWidth / 2)
                        + nMargin;
                    if (nY < (Envir.wHeight - nMargin - 1))
                    {
                        nY += nStep;
                    }
                    else
                    {
                        nY = M2Share.RandomNumber.Random(Envir.wHeight / 2)
                            + nMargin;
                    }
                }
            }
            if (Envir.m_PointList == null || Envir.m_PointList.Count == 0)
            {
                return false;
            }
            var Point = Envir.m_PointList[
                M2Share.RandomNumber.Random(Envir.m_PointList.Count)];
            // 0x7783EC / 0x7783F4 read the record with movzx, so the word is
            // zero-extended into the caller's Int32 slot.
            nX = unchecked((ushort)Point.nX);
            nY = unchecked((ushort)Point.nY);
            return true;
        }

        private bool SpaceMove_GetRandXY(Envirnoment Envir, ref short nX, ref short nY)
        {
            int nWideX = nX;
            int nWideY = nY;
            var result = NativeGetRandomXY(Envir, ref nWideX, ref nWideY);
            nX = unchecked((short)nWideX);
            nY = unchecked((short)nWideY);
            return result;
        }

        private static string TruncateNativeMapShortString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            const int capacity = 15;
            if (HUtil32.GbkEncoding.GetByteCount(value) <= capacity)
                return value;

            var buffer = new byte[capacity];
            HUtil32.GbkEncoding.GetEncoder().Convert(
                value.AsSpan(), buffer.AsSpan(), true,
                out var charsUsed, out _, out _);
            return value[..charsUsed];
        }

        // Native base-object SpaceMove sub_768D78.
        // TPlayer has a separate VMT override (sub_6BD294), so this path must
        // never be shared with managed TPlayObject instances.
        private bool TryNativeNonPlayerSpaceMove(
            Envirnoment environment, short requestedX, short requestedY,
            int showMode)
        {
            var candidateX = requestedX;
            var candidateY = requestedY;
            if (ReferenceEquals(environment, m_PEnvir))
            {
                // sub_768D78 has no "already resolved" input. Its same-map
                // arm always searches before attempting the quiet relocation.
                var candidateResolved = SpaceMove_GetRandXY(environment,
                    ref candidateX, ref candidateY);

                // 0x768DA5..0x768E0C: the quiet arm reuses the exact map node.
                // It checks occupancy, sends nothing, and leaves tick/latch.
                if (candidateResolved && environment.MoveToMovingObject(
                        m_nCurrX, m_nCurrY, this, candidateX, candidateY,
                        false) > 0)
                {
                    for (var i = 0; i < m_VisibleActors.Count; i++)
                    {
                        m_VisibleActors[i] = null;
                    }
                    m_VisibleActors.Clear();
                    m_VisibleHumanList.Clear();
                    m_nCurrX = candidateX;
                    m_nCurrY = candidateY;
                    return true;
                }
            }

            // 0x768E11..0x768F41: cross-map moves enter here directly; a
            // same-map search/move failure enters with the first candidate.
            // Delete/Add results are ignored and item/event visibility survives.
            var oldEnvironment = m_PEnvir;
            var oldX = m_nCurrX;
            var oldY = m_nCurrY;
            m_VisibleHumanList.Clear();
            oldEnvironment.DeleteFromMap(oldX, oldY,
                CellType.OS_MOVINGOBJECT, this);
            for (var i = 0; i < m_VisibleActors.Count; i++)
            {
                m_VisibleActors[i] = null;
            }
            m_VisibleActors.Clear();

            m_PEnvir = environment;
            m_sMapName = TruncateNativeMapShortString(environment.sMapName);
            m_nCurrX = candidateX;
            m_nCurrY = candidateY;
            if (!SpaceMove_GetRandXY(environment, ref m_nCurrX,
                    ref m_nCurrY))
            {
                m_PEnvir = oldEnvironment;
                m_nCurrX = oldX;
                m_nCurrY = oldY;
                // Native AddToMap calls the map's AddObject slot for every new
                // moving node. Reset the managed registration guard so a prior
                // failed Delete does not suppress that observable count update.
                m_boAddToMaped = false;
                oldEnvironment.AddToMap(oldX, oldY,
                    CellType.OS_MOVINGOBJECT, this);
                return false;
            }

            m_boAddToMaped = false;
            environment.AddToMap(m_nCurrX, m_nCurrY,
                CellType.OS_MOVINGOBJECT, this);
            SendRefMsg(showMode == 1
                    ? Grobal2.RM_SPACEMOVE_SHOW2
                    : Grobal2.RM_SPACEMOVE_SHOW,
                m_btDirection, m_nCurrX, m_nCurrY, 0, string.Empty);
            m_dwMapMoveTick = HUtil32.GetTickCount();
            m_bo316 = true;
            return true;
        }

        // MOVE-52 — both space-move arms load the internal idents as immediates:
        //   006BD3AA  66 B9 85 27  mov cx,0x2785 -> 006BD3B2 call 0x765E68
        //   006BD3D3  66 B9 86 27  mov cx,0x2786 -> 006BD3DB call 0x765F6C
        // and the cross-map arm repeats them at 0x6BD51B / 0x6BD544. Only
        // ExecuteNativeUserMove used to ask for those; every other teleport took
        // the default and queued the legacy 8097/8098 instead. The default is
        // now the native pair, so all teleports agree.
        internal bool TrySpaceMoveToEnvironment(Envirnoment targetEnvironment,
            short nX, short nY, int showMode,
            bool coordinatesAlreadyResolved = false,
            bool useNativeInternalMessages = true,
            bool requireLocalServerIndex = true)
        {
            if (targetEnvironment == null
                || (requireLocalServerIndex
                    && M2Share.nServerIndex != targetEnvironment.nServerIndex))
                return false;

            var oldEnvironment = m_PEnvir;
            if (oldEnvironment == null) return false;

            var sameEnvironment = ReferenceEquals(oldEnvironment,
                targetEnvironment);

            if (this is not TPlayObject)
            {
                return TryNativeNonPlayerSpaceMove(
                    targetEnvironment, nX, nY, showMode);
            }

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

                // Non-player dispatch returned through sub_768D78 above. This
                // transaction is the managed TPlayer/sub_6BD294 branch.
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
                // The move is done here: the actor is on the target cell and its visibility
                // state has been rebuilt; everything below is notification. 战神 has no
                // rollback at all on this path — sub_6BD294's prologue (0x6BD294 push ebp /
                // 0x6BD295 mov ebp,esp / 0x6BD297 add esp,-0x10C / push ebx/esi/edi) installs
                // no SEH frame, and no "SpaceMove" exception string exists in the image — so
                // a fault while queueing RM_NATIVE_CLEAROBJECTS / RM_NATIVE_CHANGEMAP /
                // RM_SPACEMOVE_SHOW must never un-move an actor that native would have left
                // on the target map. Committing after the sends did exactly that.
                committed = true;
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

            // Native base-object sub_768D78 passes target[+0x54] through
            // sub_78FE84, which is `xor eax,eax; ret` in this image. Only the
            // TPlayer override owns cross-server handoff semantics.
            if (this is not TPlayObject)
            {
                TrySpaceMoveToEnvironment(Envir, nX, nY, nInt,
                    requireLocalServerIndex: false);
                return;
            }

            if (M2Share.nServerIndex == Envir.nServerIndex)
            {
                TrySpaceMoveToEnvironment(Envir, nX, nY, nInt);
            }
            else if (SpaceMove_GetRandXY(Envir, ref nX, ref nY))
            {
                TryBeginCrossServerTransfer(Envir, nX, nY);
            }
        }

        /// <summary>
        /// Native sub_768CEC, the by-name SpaceMove wrapper. Its first act,
        /// ahead of the map lookup, is the stealth reveal:
        /// <code>
        /// 00768CF2  89 4D FC              mov  [ebp-4],ecx       ; nX
        /// 00768CF5  8B F2                 mov  esi,edx           ; sMap
        /// 00768CF7  8B D8                 mov  ebx,eax           ; Self
        /// 00768CF9  8B C3                 mov  eax,ebx
        /// 00768CFB  E8 C0 B5 00 00        call 0x7742C0          ; ★ break stealth
        /// 00768D00  A1 0C 66 7D 00        mov  eax,[0x7D660C]    ; envir list
        /// 00768D09  E8 C2 D5 F2 FF        call 0x6962D0          ; find map by name
        /// 00768D42  FF 93 C0 01 00 00     call [vmt+0x1C0]       ; SpaceMove(envir,...)
        /// </code>
        /// The by-envir overload behind [vmt+0x1C0] (TPlayer 0x6BD294) does
        /// not carry the call — the rel32 census of sub_7742C0 has exactly
        /// four sites and 0x768CFB is the only one in this family — so the
        /// reveal belongs to the name-taking wrapper, which is what this
        /// overload is. The correspondence is pinned by a literal: at
        /// 0x64D203 native passes `edx = 0x64D22C` (AnsiString len 7,
        /// "D5071~0"), `ecx = 0xB`, `push 0xD`, i.e. SpaceMove("D5071~0",
        /// 11, 13) — the same call this port makes in
        /// TPlayObject.NativeMagicTower.cs. The script arm is in as well:
        /// 0x743BB9 feeds it a record's map/X/Y with the second-lookup flag
        /// set (`6A 01` at 0x743BA4).
        /// </summary>
        public void SpaceMove(string sMap, short nX, short nY, int nInt)
        {
            // 0x768CFB
            BreakNativeStealthOnAction();
            SpaceMove(M2Share.MapManager.FindMap(sMap), nX, nY, nInt);
        }

        public void RefShowName()
        {
            SendRefMsg(Grobal2.RM_USERNAME, 0, 0, 0, 0, GetShowName());
        }

        /// <summary>
        /// Native SM 4469 (join) / 4470 (leave): name-only slave-list notify.
        /// Sender sub_6F784C / sub_6F78B4: Recog=0, Param=Tag=Series=0, sMsg=[slave+0x106].
        /// Only TPlayObject has the [+0x250] unicast slot this goes through
        /// (TPlayer.MakeSlave = sub_6CB070). HeroObject is AnimalObject — skip.
        /// </summary>
        public void NotifyNativeSlaveListChanged(bool joining, TBaseObject slave)
        {
            if (slave == null || this is not TPlayObject player)
                return;
            player.SendDefMessage(
                (short)(joining ? Grobal2.SM_SLAVE_JOIN : Grobal2.SM_SLAVE_LEAVE),
                0, 0, 0, 0, slave.m_sCharName ?? "");
        }

        public TBaseObject MakeSlave(string sMonName, int nMakeLevel,
            int nExpLevel, int nMaxMob, int dwRoyaltySec)
        {
            // Compatibility surface for existing managed extensions. Native
            // callers use MakeNativeSlave, whose one MagicLv feeds both level
            // bytes and whose hpAfterSlave argument is explicit.
            return MakeSlaveCore(sMonName, nMakeLevel, nExpLevel, nMaxMob,
                dwRoyaltySec, fromHero: false, hpAfterSlave: 10);
        }

        /// <summary>
        /// Exact TPlayer.MakeSlave <c>sub_6CB070</c> argument surface. Native
        /// writes <paramref name="magicLevel"/> to both slave level bytes and
        /// stores the raw signed <paramref name="hpAfterSlave"/> percentage at
        /// TAnimal <c>+0x48C</c>.
        /// </summary>
        internal TBaseObject MakeNativeSlave(string sMonName, int magicLevel,
            int nMaxMob, int dwRoyaltySec, bool fromHero, int hpAfterSlave)
        {
            return MakeSlaveCore(sMonName, magicLevel, magicLevel, nMaxMob,
                dwRoyaltySec, fromHero, hpAfterSlave);
        }

        private TBaseObject MakeSlaveCore(string sMonName, int nMakeLevel,
            int nExpLevel, int nMaxMob, int dwRoyaltySec, bool fromHero,
            int hpAfterSlave)
        {
            short nX = -1;
            short nY = -1;
            var spawnEnvironment = m_PEnvir;

            // 0x6CB09D..0x6CB108 and 0x6CB1E7..0x6CB1F0 execute before
            // and independently of the BoFromHero branch.
            var hero = (this as TPlayObject)?.m_HeroObject;
            if (hero != null && !hero.m_boDeath && !hero.m_boGhost)
            {
                if (hero.PrepareNativeHeroSummonSlotForMakeSlave())
                    nMaxMob++;

                if (fromHero && hero.m_PEnvir != null)
                {
                    spawnEnvironment = hero.m_PEnvir;
                    ResolveNativeSlaveSpawnPosition(hero, spawnEnvironment,
                        ref nX, ref nY);
                }
            }

            // A missing/dead/ghost/mapless hero, or an ordinary five-argument
            // call, falls back to the player's physical environment.
            if (nX == -1)
            {
                spawnEnvironment = m_PEnvir;
                if (spawnEnvironment == null)
                    return null;
                ResolveNativeSlaveSpawnPosition(this, spawnEnvironment,
                    ref nX, ref nY);
            }

            if (m_SlaveList.Count >= nMaxMob || nX == -1)
                return null;

            var MonObj = M2Share.UserEngine.RegenMonsterByName(
                spawnEnvironment, nX, nY, sMonName);
            if (MonObj == null)
                return null;

            MonObj.m_Master = this;
            // TPlayer.MakeSlave sub_6CB070 @0x6CB2DA writes byte
            // [slave+0x47D]=1. sub_71E3B7 reads that byte as the native
            // no-item-drop gate, so every slave created by this shared core
            // inherits the same suppression.
            MonObj.m_boNoItem = true;
            MonObj.m_dwMasterRoyaltyTick = HUtil32.GetTickCount() +
                                           (dwRoyaltySec * 1000);
            MonObj.m_btSlaveMakeLevel = (byte)nMakeLevel;
            MonObj.m_btSlaveExpLevel = (byte)nExpLevel;
            if (MonObj is AnimalObject animal)
                animal.m_nNativeHpAfterSlavePercent = hpAfterSlave;
            MonObj.RecalcAbilitys();
            if (MonObj.m_WAbil.HP < MonObj.m_WAbil.MaxHP)
            {
                MonObj.m_WAbil.HP +=
                    (MonObj.m_WAbil.MaxHP - MonObj.m_WAbil.HP) / 2;
            }
            MonObj.RefNameColor();
            m_SlaveList.Add(MonObj);
            // 0x6CB348 adds to master+0x4FC; 0x6CB357 sends SM 4469 to
            // the master even when the spawn anchor was the hero.
            NotifyNativeSlaveListChanged(joining: true, MonObj);
            return MonObj;
        }

        /// <summary>
        /// TAnimal.Run <c>0x71E657..0x71E720</c>: detach an expired slave,
        /// apply the signed Int32 HP percentage, then refresh its displayed
        /// name. The multiply deliberately wraps before division.
        /// </summary>
        internal void ExpireNativeSlaveRoyalty()
        {
            var master = m_Master;
            if (master == null)
                return;

            for (var i = master.m_SlaveList.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(master.m_SlaveList[i], this))
                    continue;
                master.NotifyNativeSlaveListChanged(joining: false, this);
                master.m_SlaveList.RemoveAt(i);
                break;
            }

            m_Master = null;
            var hpAfterSlave = 10;
            if (this is AnimalObject animal)
            {
                animal.m_boNativeSlaveRoyaltyExpired = true;
                hpAfterSlave = animal.m_nNativeHpAfterSlavePercent;
            }
            m_WAbil.HP = unchecked(m_WAbil.HP * hpAfterSlave) / 100;
            RefShowName();
        }

        private static void ResolveNativeSlaveSpawnPosition(TBaseObject anchor,
            Envirnoment environment, ref short nX, ref short nY)
        {
            anchor.GetFrontPosition(ref nX, ref nY);
            if (environment.CanWalk(nX, nY, false))
                return;

            var centerX = anchor.m_nCurrX;
            var centerY = anchor.m_nCurrY;
            nX = centerX;
            nY = centerY;

            // 0x6CB184..0x6CB1DF / 0x6CB237..0x6CB28C: X is the
            // outer loop, Y the inner loop. If all nine cells are blocked,
            // native retains the anchor's center coordinates.
            for (var scanX = centerX - 1; scanX <= centerX + 1; scanX++)
            {
                for (var scanY = centerY - 1; scanY <= centerY + 1; scanY++)
                {
                    if (!environment.CanWalk(scanX, scanY, false))
                        continue;
                    nX = unchecked((short)scanX);
                    nY = unchecked((short)scanY);
                    return;
                }
            }
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
        ///
        /// 这是全树的主入包门。<see cref="BagCapacity.Of"/> 对非 <c>TPlayObject</c>
        /// 返回 48，所以英雄/怪物走的仍是原生那条 <c>sub_6D0AE8</c>。
        /// </summary>
        public bool AddItemToBag(TUserItem UserItem)
        {
            bool result = false;
            if (m_ItemList.Count < BagCapacity.Of(this))
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

        
        
        
        // The 基本剑术 override is a code patch on the recalc arm below, so it is
        // not scoped to any one actor: whoever reaches 0x0076AF96 runs the
        // rewritten lea. Only the plugin manager is consulted here.
        private static bool NativeOneSwordOverrideActive()
        {
            var api = new Plugins.YanshenApi(null, null, M2Share.PluginManager);
            return api.IsOneSword();
        }

        private static int NativeOneSwordAccuracyFactor()
        {
            var api = new Plugins.YanshenApi(null, null, M2Share.PluginManager);
            return api.IsOneSword()
                ? Plugins.YanshenApi.OneSwordLevelFactor(api.OneSwordN())
                : 3;
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
                // Native recalc sub_76ADA0 feeds every one of these arms from
                // sub_4C896C (`mov dl,[eax+0x0C]; add dl,[eax+0x18];
                // mov cl,[[eax]+0x1A]; cmp dl,cl; jbe`), never from the raw
                // btLevel: 0x0076AF81, 0x0076AFC6, 0x0076B009, 0x0076B027,
                // 0x0076B036, 0x0076B0E7.
                int effectiveLevel = UserMagic.MagicInfo == null
                    ? UserMagic.btLevel
                    : Math.Min(
                        unchecked((byte)(UserMagic.btLevel + UserMagic.NativeLevelBonus)),
                        UserMagic.MagicInfo.btTrainLv);
                switch (UserMagic.wMagIdx)
                {
                    case SpellsDef.SKILL_ONESWORD:// 基本剑法
                        // 0x0076AF96 8D 04 40 lea eax,[eax+eax*2] then
                        // 0x0076AF99 66 01 83 64 02 00 00 add word[ebx+0x264],ax
                        // yanshen rewrites that lea's SIB byte (plugin
                        // 0x100B49D9, 3 bytes @0x0076AF96) so n replaces the 3.
                        // Only LEA-encodable scales exist; see OneSwordLevelFactor.
                        if (effectiveLevel > 0)
                        {
                            m_btHitPoint = unchecked((ushort)(m_btHitPoint
                                + NativeOneSwordAccuracyFactor() * effectiveLevel));
                        }
                        // 0x0076AFA7 3C 04 cmp al,4 /
                        // 0x0076AFA9 0F 85 4F 03 00 00 jne 0x0076B2FE /
                        // 0x0076AFAF 83 83 90 02 00 00 02 add dword[ebx+0x290],2
                        // The same override deletes this tier: plugin 0x100B4A10
                        // splices `E9 50 03 00 00 90` over 0x0076AFA9, turning the
                        // conditional skip into an unconditional one.
                        if (effectiveLevel == 4 && !NativeOneSwordOverrideActive())
                        {
                            m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC),
                                HUtil32.HiWord(m_WAbil.DC) + 2);
                        }
                        break;
                    case SpellsDef.SKILL_ILKWANG:// 精神力战法
                        // 0x0076AFE5 DB 2D 14 B3 76 00 fld tbyte[0x76B314] = 8/3
                        if (effectiveLevel > 0)
                        {
                            m_btHitPoint = unchecked((ushort)(m_btHitPoint + HUtil32.Round(8.0 / 3.0 * effectiveLevel)));
                        }
                        break;
                    case SpellsDef.SKILL_YEDO:// 攻杀剑法
                        // 0x0076B01E adds the level itself, then
                        // 0x0076B02C 04 05 add al,5 / 0x0076B02E mov [ebx+0x90],al
                        if (effectiveLevel > 0)
                        {
                            m_btHitPoint = unchecked((ushort)(m_btHitPoint + effectiveLevel));
                        }
                        m_nHitPlus = unchecked((byte)(M2Share.DEFHIT + effectiveLevel));
                        m_btAttackSkillCount = unchecked((byte)(7 - effectiveLevel));
                        m_btAttackSkillPointCount = (byte)M2Share.RandomNumber.Random(m_btAttackSkillCount);
                        break;
                    case SpellsDef.SKILL_FIRESWORD:// 烈火剑法
                        // 0x0076B0EC C1 E0 02 shl eax,2 / 0x0076B0EF 04 04 add al,4
                        m_nHitDouble = unchecked((byte)(4 + effectiveLevel * 4));
                        break;
                    // Native sub_76ADA0 reaches 0x76B16D through
                    // `add eax,-7` / `sub eax,4` / `jb` at 0x76AF2B, i.e.
                    // unsigned (id - 65) < 4, and stores the record with
                    // 0x76B170 `mov [ebx+0xC4],eax`. Last one in list order
                    // wins, and nothing ever clears the field.
                    case 65:
                    case SpellsDef.SKILL_66:
                    case SpellsDef.SKILL_67:
                    case 68:
                        m_NativeChargedCounterMagic = UserMagic;
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
                // 战神 sub_726E68 删的是队长时 @0x726FBF call 0x727FB0：
                // 从槽 0..10 找第一个「72843C 记录有效且 player != 离队者」的成员，
                // 写入 group+0x3C，再广播 ShortString 0x7280AC「 提升为小队队长!」。
                // 旧 C# 把全队 LeaveGroup，等于队长一点删除就解散。
                TPlayObject successor = null;
                for (int i = 0; i < m_GroupMembers.Count; i++)
                {
                    var cand = m_GroupMembers[i];
                    if (cand != null && cand != BaseObject && !cand.m_boGhost)
                    {
                        successor = cand;
                        break;
                    }
                }
                BaseObject.LeaveGroup();
                m_GroupMembers.Remove(BaseObject as TPlayObject);
                if (successor != null)
                {
                    var remaining = new List<TPlayObject>(m_GroupMembers);
                    m_GroupMembers.Clear();
                    successor.m_GroupMembers.Clear();
                    successor.m_GroupMembers.Add(successor);
                    successor.m_GroupOwner = successor;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        var member = remaining[i];
                        if (member == null || member == successor)
                            continue;
                        successor.m_GroupMembers.Add(member);
                        member.m_GroupOwner = successor;
                    }
                    successor.SendGroupText(successor.m_sCharName + " 提升为小队队长!");
                    successor.RefreshNativeGroupWire();
                    return;
                }
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
                // 战神 726FE6 call 0x7270F8 仍存活则 726FF7 call 0x7271D0，
                // 下发 SM 667 的 54 字节成员记录，不是斜杠拼名。
                PlayObject.RefreshNativeGroupWire();
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
                // 原版零耐久分支 0x73E850..0x73E88A 逐条：
                //   0x73E850  66 C7 43 26 00 00   mov word [ebx+0x26],0   ; Dura = 0
                //   0x73E856  8B 87 C0 04 00 00   mov eax,[edi+0x4C0]     ; 装备容器
                //   0x73E85C  E8 17 06 02 00      call 0x75EE78           ; 容器 RecalcAbilitys
                //   0x73E863  8B 10               mov edx,[eax]
                //   0x73E865  FF 92 8C 00 00 00   call [edx+0x8C]         ; 角色 RecalcAbilitys
                //   0x73E86B  6A 01 …             cx=0x278D 发包（无条件）
                //   0x73E88A  EB 3C               jmp 0x73E8C8            ; 直接返回
                // 本仓库既定映射：0x75EE78 + VMT+0x8C 这一对写成一次 RecalcAbilitys()
                // （见 Magic.cs UseAmulet）。少了它，武器打到 0 耐久后攻击力仍按有效
                // 武器计算，直到别处偶然触发重算为止。
                RecalcAbilitys();
                SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_WEAPON, nDura, m_UseItems[Grobal2.U_WEAPON].DuraMax, 0, "");
                // 原版这里是 jmp 0x73E8C8，**跳过**下面那段显示值比较。落到下面会在
                // nDuraPoint >= 1 时把同一个 0x278D 包再发一遍。
                return;
            }
            m_UseItems[Grobal2.U_WEAPON].Dura = (ushort)nDura;
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

        /// <summary>
        /// Native OOB return of player GetLevelExp (sub_6AFCC8 @ 0x6AFCF5
        /// <c>B8 80 DA 51 FD</c>) and hero GetLevelExp (sub_6884C0 @ 0x688520
        /// <c>BE 80 DA 51 FD</c>). Unsigned 4250000000; signed -44967296.
        /// </summary>
        public const int NativeNeedExpSentinel = unchecked((int)0xFD51DA80);

        public int GetLevelExp(int nLevel)
        {
            var table = M2Share.g_Config.dwNeedExps;
            // Player path 0x6AFCC8: `cmp ebx, Count / ja` then sentinel.
            // Negative nLevel becomes a huge uint and takes the same arm.
            if ((uint)nLevel >= (uint)table.Length)
            {
                // sub_651118 @0x65114E OOB: MainOutMessage(0x6511B8 + level) via 0x79DF74
                M2Share.MainOutMessage("获取角色升级经验越界 - 等级 : " + nLevel);
                return NativeNeedExpSentinel;
            }
            var value = table[nLevel];
            if (value == 0 && nLevel > M2Share.g_Config.nNeedExpMaxLevel)
            {
                M2Share.MainOutMessage("获取角色升级经验越界 - 等级 : " + nLevel);
                return NativeNeedExpSentinel;
            }
            return value;
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

        /// <summary>
        /// Native <c>sub_76858C</c> @0x76858C — boSAFE OR SafeZoneList polygon OR
        /// start-point within hardcoded range 12 (<c>0x7685BE push 0xC</c>). No RedHome arm.
        /// Eleven image call sites use this predicate (death drop 0x741405, consign 0x6F103D,
        /// corps/gild exit 0x6F57AE/0x6F6C02, GetGuildRelation 0x6C1392/0x6C139D, …).
        /// Do not confuse with <see cref="InSafeZone()"/> which ports sibling
        /// <c>sub_7684DC</c> (caller-supplied range, RedHome map '3' @845/674).
        /// </summary>
        public bool InNativeSafeZone12()
        {
            return InNativeSafeZone12(m_PEnvir, m_nCurrX, m_nCurrY);
        }

        public bool InNativeSafeZone12(Envirnoment Envir, int nX, int nY)
        {
            if (Envir == null)
            {
                return false;
            }
            // 0x768598 mov al,[map+0x5C] / 0x76859D jne return-true
            if (Envir.Flag.boSAFE)
            {
                return true;
            }
            // 0x7685AD call 0x7684A0 (SafeZoneList polygon)
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
            // 0x7685BE push 0xC / 0x7685D7 call 0x696E48 (start points, per-entry range)
            const int nativeRange = 12;
            for (int i = 0; i < M2Share.StartPointList.Count; i++)
            {
                var startPoint = M2Share.StartPointList[i];
                if (startPoint != null && startPoint.m_sMapName == Envir.sMapName)
                {
                    int range = startPoint.m_nRange > 0
                        ? startPoint.m_nRange
                        : nativeRange;
                    if ((Math.Abs(nX - startPoint.m_nCurrX) <= range) &&
                        (Math.Abs(nY - startPoint.m_nCurrY) <= range))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Legacy name kept for stock-Mir2 call sites; now delegates to
        /// <see cref="InNativeSafeZone12()"/> (native sub_76858C, range 12).
        /// The old radius-60 start-point scan had no native counterpart.
        /// </summary>
        protected bool InSafeArea()
        {
            return InNativeSafeZone12();
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
            // CRAFT-34: same ghost gate as SendMsg / sub_765E68. Hit = return, queue untouched.
            if (m_boGhost)
            {
                return;
            }
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
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
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        public void SendMsg(TBaseObject BaseObject, int wIdent, int wParam, int nParam1, int nParam2, int nParam3,
            string sMsg, object payload = null, int nBodyLen = 0)
        {
            // CRAFT-34 — native enqueue family gates on ghost byte [self+0x73], NOT death [self+0x74].
            //   0x765E7D  80 7E 73 00           cmp byte [esi+0x73], 0
            //   0x765E81  0F 85 DB 00 00 00     jne 0x765F62   ; epilogue pop/leave/ret 0x18
            // Same shape, same landing (allocate nothing, queue nothing, send nothing):
            //   0x765F81 sub_765F6C / 0x766075 sub_766060 / 0x76614D sub_76613C
            // The only [+0x73] write in the image is MarkDelete:
            //   0x7680EF  C6 43 73 01           mov byte [ebx+0x73], 1
            //   (string @0x768138 = "TCreature.MarkDelete"; also writes [+0x14C]=GetTickCount)
            // Die writes a different byte:
            //   0x766323  C6 43 74 01           mov byte [ebx+0x74], 1   ; corpse still enqueues
            // 1034 success/fail both call this (0x63FFCE / 0x63FFED call 0x765E68), so a ghosted
            // player gets no RM_MAKEDRUG_* either.
            if (m_boGhost)
            {
                return;
            }
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
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
                    Payload = payload,
                    nBodyLen = nBodyLen
                };
                if (!string.IsNullOrEmpty(sMsg))
                {
                    SendMessage.Buff = sMsg;
                }
                m_MsgList.Add(SendMessage);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        
        
        
        public void SendDelayMsg(TBaseObject BaseObject, int wIdent, int wParam, int lParam1, int lParam2, int lParam3,
            string sMsg, int dwDelay, object payload = null)
        {
            // CRAFT-34: sub_766060 @0x766075  80 7E 73 00 / 0x766079  0F 85 B1 00 00 00 jne 0x766130
            if (m_boGhost)
            {
                return;
            }
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
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
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        
        
        
        public void SendDelayMsg(int BaseObject, short wIdent, int wParam, int lParam1, int lParam2, int lParam3, string sMsg, int dwDelay)
        {
            // CRAFT-34: sub_76613C @0x76614D  80 7E 73 00 / 0x766151  0F 85 86 00 00 00 jne 0x7661DD
            if (m_boGhost)
            {
                return;
            }
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
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

        public void SendUpdateMsg(TBaseObject BaseObject, int wIdent, int wParam, int lParam1, int lParam2, int lParam3, string sMsg, object payload = null, int nBodyLen = 0)
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
            SendMsg(BaseObject, wIdent, wParam, lParam1, lParam2, lParam3, sMsg, payload, nBodyLen);
        }

        public void SendActionMsg(TBaseObject BaseObject, int wIdent, int wParam, int lParam1, int lParam2, int lParam3, string sMsg, int nBodyLen = 0)
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
            SendMsg(BaseObject, wIdent, wParam, lParam1, lParam2, lParam3, sMsg, null, nBodyLen);
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
                    Msg.nBodyLen = SendMessage.nBodyLen;
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
                                                // 这条重建循环就是 sub_77A990
                                                // （TEnvironment.DoSearchTargetList，VMT+0x1C =
                                                // [0x774798] -> thunk 0x77B330）的内联体，三条硬证据：
                                                //  ① 扫描窗半径取自全局 [[0x7D6754]]
                                                //     （0x77A9DD / 0x77A9E7 / 0x77A9F2 / 0x77A9FF），
                                                //     该全局 = INI [Setup] GlobalSeeZone，缺省 12
                                                //     （0x794495 6A 0C push 0xC 作 ReadInteger 默认值、
                                                //      0x7944B7 C7 00 0C 00 00 00 缺省写回），
                                                //     正是 g_Config.nSendRefMsgRange；而
                                                //     TBaseObject.SearchViewRange 用的是每对象 m_nViewRange。
                                                //  ② sub_77A990 的 TList 出参 [ebp+0xC] 就是本表：
                                                //     三个调用点 0x76528A / 0x765451 / 0x76589D 一律
                                                //     先 TList.Clear（call [edx+8]）再 push [self+0x380]。
                                                //  ③ 节点循环只有 CellType 1 一条臂
                                                //     （0x77AAEE 80 38 01 / 0x77AAF1 0F 85 1C 01 00 00）。
                                                // 摘链臂在 0x77AB07：
                                                //   77AAFB  8B 45 E8 / 8B 70 04   esi := node^.POject
                                                //   77AB01  85 F6 / 0F 84 0C 01.. POject = nil -> 只跳过
                                                //   77AB07  E8 58 B2 FE FF        call 0x765D64
                                                //   77AB0C  84 C0 / 0F 85 A3 00.. jne 0x77ABB5（有效臂）
                                                //   77AB14-77AB23  摘链 / 77AB2E B3 01 bl := 1 /
                                                //   77AB30 push 0x77AD00 记异常 -> continue
                                                // 0x77AB07 call 0x765D64 / test al / jne 有效臂；与
                                                // SearchViewRange 三副本同一谓词，原生无 60s 并联。
                                                // OS_MOVINGOBJECT 孤儿若无其它 GC：族 A 地图腿
                                                // (Envirnoment CanWalk/MoveTo/GetMovObjCount) 已纯谓词；
                                                // 仍通过谓词的悬挂节点原生亦保留，无第二摘链臂。
                                                if (IsNativeStaleCellActor(OSObject.CellObj))
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
                                                            // Cache membership is decided by the SCAN alone. Native
                                                            // rebuilds [self+0x380] in sub_7651EC @0x765263-0x76528A
                                                            // (Clear + Envirnoment VMT+0x1C), a step that knows
                                                            // nothing about the ident, and the broadcast slot
                                                            // sub_6DC590 only ever READS the list. Deciding
                                                            // membership from the send made the cache depend on
                                                            // which ident happened to trigger the refresh.
                                                            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT ||
                                                                BaseObject.m_boWantRefMsg)
                                                            {
                                                                m_VisibleHumanList.Add(BaseObject);
                                                                if (CanNativeRefMsgReach(BaseObject, wIdent))
                                                                {
                                                                    BaseObject.SendMsg(this, wIdent, wParam, nParam1, nParam2, nParam3, sMsg, payload);
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
                // 倒序：原生 0x765468 `8B D8 / 4B`（ebx := Count 后 dec）自尾向头走，
                // 因为幽灵臂要就地 TList.Delete；正序删会漏掉被前移的那一项。
                for (var nC = m_VisibleHumanList.Count - 1; nC >= 0; nC--)
                {
                    BaseObject = m_VisibleHumanList[nC];
                    // 族 B —— 取用前的「这指针还能碰吗」门。原生 sub_76533C
                    // (TCreature.SendRefMsg) 倒序遍历 [self+0x380]：
                    //   00765474  8B 45 FC / 8B 80 80 03 00 00  eax := self^.VisibleHumanList
                    //   0076547F  E8 C8 F8 CB FF                call 0x424D4C  ; TList.Get(edx=idx)
                    //   00765487  83 7D F4 00 / 0F 84 96 00..   item = nil -> jmp 0x765527（跳过）
                    //   00765494  E8 CB 08 00 00                call 0x765D64  ; 有效性谓词
                    //   00765499  84 C0 / 75 49                 jne 0x7654E6   ; 有效 -> 幽灵判定
                    //   0076549D-007654DF  拼 "[Exception]: TCreature.SendRefMsg Obj.CName = 空 Obj = "
                    //   007654DF  E8 90 8A 03 00                call 0x79DF74  ; 记异常
                    //   007654E4  EB 41                         jmp 0x765527   ; 跳过，表项**留着**
                    // 与族 A 的关键区别就在这一步：这里**不删表项**。相邻的幽灵臂才删
                    // （0x7654E9 cmp byte [obj+0x73],0 -> 0x7654FA call 0x424B30 = TList.Delete），
                    // 两条处置不同，正是「有效性 != 死亡/幽灵」的原生佐证。
                    // sub_765790 (TCreature.SendRefBuff) 在 0x7658E0 用同一形状。
                    // 原生的诊断日志未复刻：它无 gameplay 可观察面，且这里是每 tick 热路径。
                    if (!IsNativeCellObjectValid(BaseObject))
                    {
                        continue;
                    }
                    // 幽灵臂与上面的失效臂处置**不同**：失效只跳过、表项留着
                    // （0x7654E4 jmp 0x765527），幽灵则就地摘除 ——
                    //   0x7654E9  80 7B 73 00    cmp byte [obj+0x73],0   ; m_boGhost
                    //   0x7654FA  E8 31 F6 CB FF call 0x424B30           ; TList.Delete
                    // 0x424B30 已实测为 TList.Delete（0x424B72 `dec dword [eax+8]`
                    // 后走 System.Move 前移）。原先这里也写成 continue，幽灵会一直
                    // 滞留在 m_VisibleHumanList 里，与原生「见到即摘」不符。
                    if (BaseObject.m_boGhost)
                    {
                        m_VisibleHumanList.RemoveAt(nC);
                        continue;
                    }
                    if ((BaseObject.m_PEnvir == m_PEnvir) && (Math.Abs(BaseObject.m_nCurrX - m_nCurrX) < 11) && (Math.Abs(BaseObject.m_nCurrY - m_nCurrY) < 11))
                    {
                        if (CanNativeRefMsgReach(BaseObject, wIdent))
                        {
                            BaseObject.SendMsg(this, wIdent, wParam, nParam1, nParam2, nParam3, sMsg, payload);
                        }
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
        }

        /// <summary>
        /// The per-observer send test of a ref-broadcast, kept separate from
        /// the visible-list refresh above so the two cannot influence each
        /// other. Native keeps them apart the same way: the list is rebuilt
        /// only in <c>sub_7651EC</c> @<c>0x765263</c>-@<c>0x76528A</c>, behind
        /// its own 800 ms throttle on <c>[self+0x37C]</c>, while the broadcast
        /// slot <c>sub_6DC590</c> walks <c>[self+0x380]</c> read-only and
        /// applies its filters per item at <c>0x6DC6D1</c>-<c>0x6DC720</c>.
        /// <para>
        /// The stealth term is <c>sub_774288</c>, called at <c>0x6DC6F1</c>
        /// with <c>eax</c> = the broadcaster and <c>edx</c> = the observer,
        /// and a true result skips that observer outright
        /// (<c>0x6DC6F8 jne</c>). The same filter sits at <c>0x6DC247</c> in
        /// the VMT+0xE0 slot. Because it is applied here and not in the
        /// refresh, a stealthed caster's observers stay in the cache and
        /// resume receiving the moment the state lapses or they close inside
        /// two cells.
        /// </para>
        /// <para>
        /// Divergence left standing: native's cached loop rejects every
        /// observer with <c>byte [item+0x178] != 0</c> at <c>0x6DC6D1</c>,
        /// i.e. only race 0 is served from the cache, with no
        /// <c>m_boWantRefMsg</c> branch at all. Dropping that branch here
        /// would stop monsters reacting to STRUCK/HEAR/DEATH between refresh
        /// ticks, which is a far wider change than this one; it is recorded
        /// rather than made.
        /// </para>
        /// </summary>
        private bool CanNativeRefMsgReach(TBaseObject observer, int wIdent)
        {
            if (observer.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
            {
                if (!observer.m_boWantRefMsg)
                {
                    return false;
                }
                if ((wIdent != Grobal2.RM_STRUCK) && (wIdent != Grobal2.RM_HEAR) &&
                    (wIdent != Grobal2.RM_DEATH))
                {
                    return false;
                }
            }
            return !IsNativeStealthedFrom(observer);
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

        /// <summary>
        /// 状态字低 32 位。战神从不"重建"这个字：<c>sub_7729C4</c> 直接把
        /// <c>[Self+0x168]</c> 起的 16 字节位集当 blob 发出（<c>lea edx,[eax+0x168]</c>,
        /// RefMsg 0x291 = 657），位 s 就是 state s，没有任何合成或超界归零逻辑。
        ///
        /// 这里只剩把持久位集回读出来。原先叠在上面的 legacy overlay
        /// （<c>m_wStatusTimeArr[i] &gt; 0 =&gt; 0x80000000 &gt;&gt; i</c>）已经删除：
        /// slot i 就是 state 31 - i，而 state 31 - i 的位由
        /// <c>SetNativeActiveState</c> / <c>ClearNativeActiveState</c> 在同一个
        /// <c>m_nCharStatusEx</c> 里维护，overlay 只是把同一位再算一遍，
        /// 且用的是另一套（秒级、自己倒计时的）过期判据——正是 4.18 说的双权威。
        /// </summary>
        public int GetCharStatus()
        {
            var status = m_nCharStatusEx & NativePersistentLowStateMask;
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
                    // Native `bts dword [esi+0x168], ebx` @0x77299B writes one flat
                    // bitset with no per-index ownership split, so the whole low
                    // word lives in the durable store. A `stateIndex <= 20` fork
                    // used to sit here and hand 21..31 to the legacy seconds array
                    // instead; that array is gone (see
                    // TBaseObject.LegacyStatusTimeView.cs) and GetCharStatus now
                    // just reads this word back.
                    m_nCharStatusEx = enabled
                        ? m_nCharStatusEx | (uint)mask
                        : m_nCharStatusEx & ~(uint)mask;
                    m_nCharStatus = GetCharStatus();
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
            playObject.m_boRcdSaved = false;
            playObject.m_boSwitchData = true;
            if (playObject.m_HeroObject != null)
            {
                playObject.m_HeroObject.m_boNativeSwitchData = true;
                playObject.m_boNativeSwitchHeroHandoffPending = true;
            }
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
            const string sExceptionMsg = "[Exception] TBaseObject::Walk {0} {1} {2}:{3}";
            if (m_PEnvir == null)
            {
                M2Share.ErrorMessage("Walk nil PEnvir");
                return true;
            }
            try
            {
                // MOVE-39 — four native movers broadcast before sub_778EC0 and ignore
                // its return: walk sub_741224 0x741315→0x741323; run2 sub_76756C
                // 0x767645→0x767656; run3 sub_767694 0x76776F→0x767780; monster walk
                // sub_71F0F4 0x71F217→0x71F231. CompleteNativeRun3Move already uses
                // this shape; gate/event handling lives in ProcessNativeMoveActionWithoutBroadcast.
                SendRefMsg(nIdent, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
                // The four native movers discard sub_778EC0's return value.
                // It controls CM_TURN broadcasting, not mover success.
                _ = ProcessNativeMoveActionWithoutBroadcast();
                return true;
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(format(sExceptionMsg, new object[] { m_sCharName, m_sMapName, m_nCurrX, m_nCurrY }));
                M2Share.ErrorMessage(e.Message);
            }
            return true;
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
                        break;
                    }
                    if (MonSayMsg.Color == MsgColor.White)
                    {
                        ProcessSayMsg(sMsg);
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
            // 0x7270F4 LStr len=1 "-" then 0x40581C dest = "-" + body.
            // 〖组队〗 is 0-hit INVENTED. Recog is the member (0x7270B8 edx=esi).
            sMsg = "-" + sMsg;
            if (m_GroupOwner != null)
            {
                for (int i = 0; i < m_GroupOwner.m_GroupMembers.Count; i++)
                {
                    PlayObject = m_GroupOwner.m_GroupMembers[i];
                    PlayObject.SendMsg(PlayObject, Grobal2.RM_GROUPMESSAGE, 0, 0, 0, 0, sMsg);
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

        
        
        
        
        /// <summary>
        /// <c>cmp dword [ebp-0x14],0xBB8</c> @0x71FFBD — the gold cap is a literal 3000,
        /// not a config key ("MonOneDropGoldCount" and friends are 0-hit in the image
        /// under GBK, bare ASCII and UTF-16LE).
        /// </summary>
        private const int NativeScatterGoldCap = 0xBB8;

        /// <summary>
        /// 战神字节证据 (Tier-1)：0x71FFB7 <c>cmp byte [ebp+8],0 / je 0x71FFCD</c> —
        /// the 3000 cap at 0x71FFBD-0x71FFC6 is the ONLY thing that byte gates; the
        /// <c>idiv</c> at 0x71FFD1 sits on the 0x71FFCD merge point and always runs.
        /// That byte reaches <c>sub_71FA20</c> through the thin VMT+0x1FC forwarder
        /// <c>sub_71F46C</c>, and monster Die pushes it literally 1 at both sites
        /// (0x71E3C4 and 0x71E3DA <c>push 0 / push 1 / call [esi+0x1FC]</c>).
        /// A class that overrides VMT+0x1FC can therefore only ever turn the cap OFF,
        /// which is exactly what <c>TGoldbarPig</c> (race 249, override 0x66D270)
        /// does — see GoldbarPig.cs. Modelled as a virtual because the single native
        /// caller always supplies 1.
        /// </summary>
        protected virtual bool NativeScatterGoldCapped => true;

        /// <summary>
        /// <c>cmp dword [ebp-0x14],0x7D0</c> @0x71FFDC with the matching
        /// <c>mov ebx,0x7D0</c> @0x71FFE5 and <c>sub dword [ebp-0x14],0x7D0</c> @0x71FFEA
        /// — the per-pile amount is a literal 2000, not the operator-tunable
        /// <c>nMonOneDropGoldCount</c> (whose key name is 0-hit in the image).
        /// </summary>
        private const int NativeScatterGoldPile = 0x7D0;

        private void ScatterGolds(TBaseObject GoldOfCreat,
            IList<KeyValuePair<string, string>> scatteredItems = null,
            bool nativeMonsterScatter = false)
        {
            int I;
            int nGold;
            if (m_nGold > 0)
            {
                // 战神 sub_71FA20 @0x71FFAD-0x71FFD4 — the settlement that runs between
                // "is there any gold" and the pile loop.  C# went straight from
                // `m_nGold > 0` into the piles, so both steps were missing:
                //
                //   71FFAD  cmp dword [ebp-0x14],0    / jle 0x720049  ; == `m_nGold > 0`
                //   71FFB7  cmp byte [ebp+8],0        / je  0x71FFCD  ; cap switch off
                //   71FFBD  cmp dword [ebp-0x14],0xBB8/ jle 0x71FFCD
                //   71FFC6  mov dword [ebp-0x14],0xBB8               ; <== cap FIRST
                //   71FFCD  mov eax,[ebp-0x14] / cdq
                //   71FFD1  idiv dword [ebp-0x2C]                    ; <== divide SECOND
                //   71FFD4  mov [ebp-0x14],eax
                //
                // 0x71FFC6 < 0x71FFD1 and the idiv sits on the 0x71FFCD merge point, so
                // the divide runs whether or not the cap fired.  Order matters: capping
                // after dividing would let a 20000-gold monster pay out 3000 at tier 2
                // instead of 1500.
                //
                // The cap switch [ebp+8] is 1 on the monster-death path.  It threads
                // through two thin forwarders and both call sites in monster Die push it
                // literally:
                //   71E3C4  6A 00 push 0 / 71E3C6  6A 01 push 1 / 71E3D2 call [esi+0x1FC]
                //   71E3DA  6A 00 push 0 / 71E3DC  6A 01 push 1 / 71E3EF call [esi+0x1FC]
                // slot +0x1FC holds sub_71F46C in 123 monster VMTs (every one verified by
                // the Delphi self-pointer test dword[VMT-0x4C]==VMT); sub_71F46C @0x71F483
                // re-pushes [ebp+0xC] then [ebp+8] and tail-calls sub_71FA20 @0x71F491, so
                // the last-pushed 1 lands in sub_71FA20's [ebp+8].  (The 0 that goes with
                // it is [ebp+0xC], the second-arm switch tested at 0x71FBD8 / 0x71FDA5.)
                //
                // [ebp-0x2C] is the same anti-fatigue multiplier MonGetRandomItems folds
                // into its gate modulus: 1 at 0x71FA62, 2 at 0x71FB27 when
                // byte[killer+0x1828]==2, and only reachable for a non-nil killer of race
                // RC_PLAYOBJECT (0x71FAB4 / 0x71FACE).  HeroObject derives from
                // AnimalObject, not TPlayObject, so the cast reproduces the race gate.
                //
                // Uncapped and undivided this was a straight money printer: the overshoot
                // is (raw gold / 3000) with no ceiling, i.e. 6.7x on a 20 000-gold monster
                // and 33x on a 100 000-gold boss, doubled again for a tier-2 account.
                if (nativeMonsterScatter)
                {
                    if (NativeScatterGoldCapped && m_nGold > NativeScatterGoldCap)
                    {
                        m_nGold = NativeScatterGoldCap;
                    }
                    var goldDivisor =
                        (GoldOfCreat as TPlayObject)?.m_btNativeFatigueTier == 2 ? 2 : 1;
                    m_nGold = m_nGold / goldDivisor;
                }
                // The monster path takes the literal from 0x71FFDC; the player-death
                // path is a different native worker whose per-pile constant has not been
                // read, so it keeps the config value it has always used.
                var pileAmount = nativeMonsterScatter
                    ? NativeScatterGoldPile
                    : M2Share.g_Config.nMonOneDropGoldCount;
                I = 0;
                while (true)
                {
                    if (m_nGold > pileAmount)
                    {
                        nGold = pileAmount;
                        m_nGold = m_nGold - pileAmount;
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
                    // 0x767334  8B 45 FC / 80 B8 C7 04 00 00 00 / 74 02 / 33 DB
                    //   主人休息位 [+0x4C7]，与下面两道门同属一条直线，不要重排。
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
                    // PKD-09 —— 宠物不打「主人的英雄」和「主人本人」。战神 sub_7671F0
                    // （TCreature 的 [vmt+0x20]，即 IsAttackTarget）在主人分支的收尾处
                    // 连着两道门，C# 一道都没有：
                    //   76736F  8B 45 FC              mov eax,[ebp-4]      ; 责任玩家
                    //   767372  3B B0 B0 0B 00 00     cmp esi,[eax+0xBB0]  ; 主人的英雄
                    //   767378  75 02                 jne 0x76737C
                    //   76737A  33 DB                 xor ebx,ebx          ; -> 不可攻击
                    //   76737C  3B B7 8C 03 00 00     cmp esi,[edi+0x38C]  ; self.m_Master
                    //   767382  75 02                 jne 0x767386
                    //   767384  33 DB                 xor ebx,ebx          ; -> 不可攻击
                    // [ebp-4] 是 self.[vmt+0xB4]() 的返回值，即沿 m_Master 链一路向上解出的
                    // 责任玩家（TCreature 版 sub_769910 递归取 [+0x38C]，TPlayer 版
                    // sub_6C185C 是裸 `C3`，eax 未改动 = 返回自身）。C# 没有这条递归解析器，
                    // 本函数上下文里一律用 m_Master 代替（第 5182/5199 行同样如此），所以这里
                    // 保持同一约定：单层宠物时 m_Master 就是责任玩家。
                    // [+0xBB0] = 英雄指针，身份由 sub_6D09D0 @0x6D09FA 独立锚定
                    // （见 TPlayObject.Message.cs 的 ClientHeroMoveToHeroBag 注释）。
                    // 玩家可见后果：没有这两道门，宠物/召唤物会把主人的英雄乃至主人本人
                    // 当成合法目标 —— 混乱状态、诱惑之光反目、群攻溅射都会打到自己人。
                    if (m_Master is TPlayObject masterOfSlave
                        && masterOfSlave.m_HeroObject != null
                        && ReferenceEquals(BaseObject, masterOfSlave.m_HeroObject))
                    {
                        result = false;                                 // 0x76737A
                    }
                    if (ReferenceEquals(BaseObject, m_Master))
                    {
                        result = false;                                 // 0x767384
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
            // PKD-08 —— 战神 sub_767498 先过九道硬门再调虚槽 [vmt+0x20]（= IsAttackTarget）。
            // 完整字节与身份证据见 TBaseObject.NativeProperTargetGate.cs。C# 此前把这九道门
            // 摊到约 50 个调用点上各写一部分，跨地图 / 石化 / 管理员模式 / 状态 52 的目标在
            // 未写全的路径上仍可被攻击。
            if (!NativeProperTargetPreGate(BaseObject))
            {
                return false;
            }
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
            return InSafeZone(m_PEnvir, m_nCurrX, m_nCurrY);
        }

        // MFLG-17. The native counterpart is sub_7684DC (eax=Self, edx=nX, ecx=nY,
        // [ebp+8]=nRange, `ret 4`); every caller passes the range as a literal 10
        // (0x6C1776 `6A0A push 0xa`, 0x68A362 `6A0A`), which is g_Config.nSafeZoneSize.
        // sub_76858C is a sibling with no RedHome arm and a hardcoded range of 12
        // (0x7685BE `6A0C`), so it is NOT the function this ports.
        //
        // Native gate order is boSAFE (0x7684F6 `8A585C` / `84DB` / `750F`), then the
        // SafeZoneList polygons (0x768505 -> sub_7684A0 -> sub_696D7C), then RedHome,
        // then the start points (0x76856B -> sub_696E48). C# runs the start points
        // before the polygons; all four gates are side-effect-free, so the OR is
        // order-independent and the result is identical.
        //
        // RedHome is compiled in as map '3' (Delphi literal at 0x768588) at
        // 845 / 674 (0x76852C `2D4D030000`, 0x76853D `2DA2020000`) -- the same values
        // GameSvrConfig defaults to. Both bounds are inclusive: 0x768536 `3BF8 cmp edi,eax`
        // / 0x768538 `7C13 jl` takes the arm only while nRange >= abs(dx), likewise
        // 0x768547 `3BF8` / 0x768549 `7C02` for dy. The start-point scan compares the
        // same way but unsigned on 16 bits (0x696EDE `663B45F6` / 0x696EE2 `720B jb`).
        // The arm is reached only when nRange > 0 (0x768510 `85FF` / 0x768512 `7E5E jle`).
        //
        // Two deliberate deviations, both out of MFLG-17's scope:
        //   - that `jle` skips the start-point scan as well, not just RedHome, so a
        //     nSafeZoneSize <= 0 config lets C# match start points where native cannot;
        //   - sub_696E48 prefers a per-entry range when it is non-zero (0x696ED1
        //     `668B4014` / 0x696ED5 `6685C0` / 0x696ED8 `7704 ja`), which is what
        //     SendSafeZoneInfo already models; here every entry uses nSafeZoneSize.
        // Native never null-checks the map (0x7684F0 loads it, 0x7684F6 dereferences it
        // straight away), so returning false below is a port-safety choice.
        public bool InSafeZone(Envirnoment Envir, int nX, int nY)
        {
            if (Envir == null)
            {
                return false;
            }
            if (Envir.Flag.boSAFE)
            {
                return true;
            }
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
            if (M2Share.g_Config.nSafeZoneSize > 0 &&
                !string.IsNullOrEmpty(M2Share.g_Config.sRedHomeMap) &&
                string.Equals(Envir.sMapName, M2Share.g_Config.sRedHomeMap,
                    StringComparison.OrdinalIgnoreCase))
            {
                if ((Math.Abs(nX - M2Share.g_Config.nRedHomeX) <= M2Share.g_Config.nSafeZoneSize) &&
                    (Math.Abs(nY - M2Share.g_Config.nRedHomeY) <= M2Share.g_Config.nSafeZoneSize))
                {
                    return true;
                }
            }
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
            // 战神 sub_6C3200 @0x6C3252 edx=0x6C32C4 ShortString len=9「 退出小组」
            // （字节 09 20 CD CB B3 F6 D0 A1 D7 E9），拼在 [self+0x106] 名字后面，
            // 经 sub_727068 用 RM 0x2776 广播给全队。全镜像「已经退出了本组」0 命中。
            SendGroupText(m_sCharName + " 退出小组");
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
        public int GetHitStruckDamage(TBaseObject Target, int nDamage)
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
            // 0x7679A9..0x7679B2 returns the full EAX value; there is no
            // word-sized load or truncation in the epilogue.
            return nDamage;
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
            // === DURA-16 (struck-durability): NATIVE-CONFIRMED — DO NOT REMOVE ===
            // The 16-slot durability loop below is NOT a GameOfMir-legacy invention.
            // The native struck path DOES wear the DEFENDER's equipment on all 16
            // slots by writing durability word [item+0x26]. Re-disassembled end to
            // end from flat_image.bin (VA-0x400000):
            //   sub_73F9FC (THumanKind.StruckDamage) @0x73FB55: `mov edx,[ebp-8]`
            //     (nDam = Random(10)+5, computed @0x73FA30-0x73FA3D); `mov eax,ebx`
            //     (self = the object being struck); @0x73FB5A `call sub_73FBE8`.
            //   sub_73FBE8 @0x73FBEB: `mov eax,[self+0x4c0]` (UseItems container);
            //     @0x73FBF1 `call sub_75EBC0` (edx=nDam preserved).
            //   sub_75EBC0 @0x75EBD2..0x75EC07: `xor esi,esi` … `inc esi; cmp esi,0x10;
            //     jne` = the 16-SLOT LOOP. Per non-null slot calls sub_75EA40; on the
            //     destroy flag calls sub_75F49C (remove); after the loop, if anything
            //     changed, @0x75EC12 `call sub_75EE78` (RecalcAbilitys).
            //   sub_75EA40 (per item): @0x75EA69 `call [item_vmt+0x74]` (has-durability
            //     guard); @0x75EA7D `mov eax,8; call Random`; @0x75EA89 `jne` = the 1/8
            //     GATE (skipped when byte[item+0xfc]!=0); @0x75EA8F `movzx eax,word
            //     [item+0x26]` (Dura); @0x75EAA4 `sub word [item+0x26],ax` = Dura-=nDam;
            //     destroy branch @0x75EB51 `mov word [item+0x26],0`. nDam is read from
            //     the parent frame ([ebp+8]-4 via the `push ebp` @0x75EBDF).
            //   The identical worker is also reached from a 2nd struck override
            //     sub_68F548 @0x68F688 — durability wear is a struck-side behavior.
            // Field map cross-checked vs DoDamageWeapon sub_73E804 (+0x26=Dura,
            // +0x28=DuraMax): that function is the ATTACKER's slot-1-only weapon wear
            // (`mov dl,1; call GetUseItems 0x75EC20`), a DIFFERENT path — not this one.
            // === U_DRESS slot-0 pre-pass REMOVED — native-equivalence fix (U_DRESS task) ===
            // A slot-0-only durability pre-pass used to sit here. It had NO native
            // counterpart and made slot 0 (U_DRESS/armour) roll durability TWICE per
            // struck: once in that pre-pass, then again as iteration i==0 of the
            // 16-slot loop below. Re-disassembled from flat_image.bin (VA-0x400000):
            //   sub_75EBC0 @0x75EBD2 `xor esi,esi`; @0x75EBD7 `mov eax,[eax+esi*4+8]`
            //     (slot = container[i]); @0x75EBE3 `call sub_75EA40` once per non-nil
            //     slot; @0x75EC03 `inc esi; cmp esi,0x10; jne 0x75EBD4` = ONE 0..15 loop.
            //     Slot 0 is merely its first iteration — there is NO special/extra
            //     handling and NO separate slot-0 pre-pass anywhere on the native struck
            //     path (sub_73F9FC→sub_73FBE8→sub_75EBC0→sub_75EA40). Native therefore
            //     wears slot 0 EXACTLY ONCE. With U_DRESS==0 and
            //     HUMAN_EQUIPPED_ITEM_COUNT==16 the loop below (i=0..15) already covers
            //     slot 0 identically (1/8 gate, Dura-=nDam, destroy, RM_DURACHANGE,
            //     RecalcAbilitys via bo19), so deleting this pre-pass is the 1:1 fix.
            // The removed pre-pass was NOT the byte[item+0xfc] "forced wear" flag: that
            //   flag is read in sub_75EA40 @0x75EA74 `cmp byte[item+0xfc],0` / @0x75EA7B
            //   `jne 0x75EA8F`, which BYPASSES the @0x75EA7D `Random(8)` 1/8 gate so the
            //   item wears on every hit. It is set at item-build time in sub_74DAE4
            //   @0x74DC58..0x74DDF0: an OVER-CAP/illegal-attribute detector dispatched on
            //   StdMode=byte[StdItem+0x14] & Shape=byte[StdItem+0x15], comparing the
            //   copied UserItem attribute bytes [+0x0A..+0x11] against per-type caps and
            //   setting +0xfc=1 when any exceeds. It is a general, ALL-slot anti-over-limit
            //   mechanism, unrelated to slot 0, and the deleted pre-pass used the ordinary
            //   Random(8) gate — it never modelled 0xfc. NativeClassFc now carries the
            //   item-build over-cap result, and the per-slot condition below preserves
            //   the native short-circuit: a nonzero flag consumes no Random(8) draw.
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                // The `Dura > 0` term is the native per-item gate sub_75EA40
                // @0x75EA69 `call [item_vmt+0x74]`, whose equipment-class
                // implementation 0x75F6C8 is exactly `cmp word [item+0x26],0; jbe
                // -> false`. Without it an already-broken item re-enters the
                // wear path and is destroyed on the next hit, which native never
                // does. See TBaseObject.NativeStruckDurability.cs.
                if ((m_UseItems[i] != null) && (m_UseItems[i].wIndex > 0) && NativeStruckWearsOnHit(m_UseItems[i])
                    && (m_UseItems[i].NativeClassFc != 0
                        || M2Share.RandomNumber.Random(8) == 0))
                {
                    nDura = m_UseItems[i].Dura;
                    nOldDura = NativeStruckDuraDisplayPoint(nDura);
                    nDura -= nDam;
                    // sub_75EA40 sets the notify flag @0x75EB57 unconditionally on
                    // the destroy arm, and only on a visible point change @0x75EAC9
                    // on the wear arm.
                    bool boSendDuraChange;
                    if (nDura <= 0)
                    {
                        // DURA-39: sub_75EA40 destroy arm @0x75EB45-0x75EB57 (byte-verified)
                        // sends the "item expired" notice (cx=0xFFDB via VMT+0xD4) then
                        // `mov word [ebx+0x26],0` (Dura=0) and `mov byte [esi],1` (changed).
                        // It does NOT null the slot, NOT SendDelItems, NOT Free/FeatureChanged
                        // — the broken item stays equipped (greyed) and RecalcAbilitys
                        // (@0x75EE9B `jbe` skips Dura<=0) drops its stat contribution. The
                        // prior C# deleted the item, which native never does on struck-destroy.
                        if (this is TPlayObject || this is HeroObject)
                        {
                            StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                            if (StdItem != null)
                            {
                                M2Share.AddNativeGameDataLog(this, 0x43,
                                    StdItem.Name, m_UseItems[i].MakeIndex, 1,
                                    "持久耗尽");
                                SendNativeStateSysMsg(0xFFDB,
                                    "您的" + StdItem.Name + "失效了");
                            }
                        }
                        m_UseItems[i].Dura = 0;
                        bo19 = true;
                        boSendDuraChange = true;
                    }
                    else
                    {
                        m_UseItems[i].Dura = (ushort)nDura;
                        boSendDuraChange = nOldDura != NativeStruckDuraDisplayPoint(nDura);
                    }
                    if (boSendDuraChange)
                    {
                        // sub_75F49C @0x75F4D2/@0x75F4D7 reads the item's CURRENT
                        // `word [item+0x26]` / `word [item+0x28]`, so the destroy arm
                        // reports 0 rather than the negative running total.
                        SendMsg(this, Grobal2.RM_DURACHANGE, i, m_UseItems[i].Dura, m_UseItems[i].DuraMax, 0, "");
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
                // POIS-08 / STATE-52 — native MakePosion is VMT+0xC8 @0x76B3C8 and owns
                // no storage of its own; it is a seconds->milliseconds wrapper around the
                // one and only state authority, AddState (VMT+0x1EC @0x7730D0):
                //   76B3D8  E8 67 88 00 00        call 0x773C44          ; ImmuneCheck -> abort
                //   76B3E1  B2 34 / E8 76 75 00 00 HasState(0x34)        ; global veto -> abort
                //   76B3EE  80 FB 12 / 75 16      if id==0x12 && HasState(0x1A) -> RemoveState(0x1A)
                //   76B409  0F B7 45 08 / 50      push word [ebp+8]      ; value/level
                //   76B40E  6A 00                 push 0                 ; flag
                //   76B413  69 C8 E8 03 00 00     imul ecx, eax, 0x3E8   ; seconds -> ms
                //   76B41F  FF 93 EC 01 00 00     call [ebx+0x1EC]       ; AddState
                // C# had grown a second authority (m_wStatusTimeArr, seconds) that
                // AddTimedAbilityInternal never saw, so no code ever built a
                // TimedAbilityNode for bodyStates 0x06/0x01/0x1C/0x1F and the four
                // poison-tick tiers @0x76BD4F-0x76BDF5 were unreachable, while
                // HasNativeActiveState(26) stayed false for a target the client was
                // already drawing as petrified. Route through the native authority.
                // GetCharStatus maps slot i to bit 31-i, so bodyState = 31 - nType.
                // CanAddNativeTimedAbility inside AddTimedAbilityInternal already covers
                // the ImmuneCheck and the 0x34 veto, and the 0x12 -> remove 0x1A companion
                // lives in the same method.
                // The legacy array is now a view onto that same node
                // (m_wStatusTimeArr[nType] IS state 31 - nType), so the
                // max()-and-stamp block that used to follow this call has been
                // deleted: it was the second authority writing the same state a
                // second time, in seconds, on its own clock. AddState already
                // owns the refresh rule - 0x773117 `cmp edi,eax / jle` takes the
                // higher value, 0x773140 `cmp eax,[ebp-4] / jge` extends only to
                // a longer duration - and native MakePosion adds nothing on top.
                //
                // STATE-19 — this used to call AddTimedAbilityInternal directly,
                // which skipped the VMT+0xC8 slot itself. Native never reaches
                // AddState from a poison source without passing through
                // MakePosion, and for a player or hero target that slot is
                // THumanKind's 0x746604, not TCreature's 0x76B3C8. Going through
                // NativeMakePosion restores the override (state-29 resist roll)
                // and the unconditional `0x12 -> RemoveState(0x1A)` companion at
                // 0x76B3EE. The word casts are native's own truncation:
                // 0x76B410 `movzx eax,di` on the seconds and 0x76B409
                // `movzx eax,word [ebp+8]` on the value.
                if (!NativeMakePosion((byte)(31 - nType),
                        unchecked((ushort)nTime), unchecked((ushort)nPoint)))
                {
                    return false;
                }
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
                // STATE-16 — native MakePosion (0x76B3C8 / TPlayObject override
                // 0x746604) does not broadcast 657 itself. AddState @0x77318C
                // notifies through VMT+0x14, which for the default class is
                // 0x76B42C -> 0x7729C4 (`66 8B 90 74 02 00 00` word [Self+0x274]
                // then ident 0x291). C# SendTimedAbilityState already sends that
                // packet (nParam1 = m_nHitSpeed). The extra StatusChanged() here
                // was a second 657 with wParam=m_nHitSpeed / nParam1=m_nCharStatus,
                // a shape 0x7729C4 never uses.
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
