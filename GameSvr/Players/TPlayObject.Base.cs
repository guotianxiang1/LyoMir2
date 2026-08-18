using System.Buffers.Binary;
using System.Collections;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private static readonly object s_initLock = new object();
        public ClientPacket m_DefMsg;
        public string m_sOldSayMsg = string.Empty;
        public int m_nSayMsgCount = 0;
        public byte m_btSayRapidCount = 0;
        public int m_dwSayMsgTick = 0;
        public bool m_boDisableSayMsg = false;
        public int m_dwDisableSayMsgTick = 0;
        public int m_dwCheckDupObjTick = 0;
        public int dwTick578 = 0;
        public int m_dwLastMobileHbTick = 0;
        public string m_sCallOutParam = string.Empty;

        public void SendLogonPublic() { SendLogon(); }
        public void SendMobileHeartbeatPublic() { SendMobileHeartbeat(); }
        private void SendMobileHeartbeat()
        {
            if (HUtil32.GetTickCount() - m_dwLastMobileHbTick < 3000) return;
            m_dwLastMobileHbTick = HUtil32.GetTickCount();
            // SM_LOGON is an initialization packet. Re-sending it makes the mobile client
            // recreate the main scene and re-run @onLogin/CM_QUERYBAGITEMS continuously.
            // Surrounding actors are synchronized by SearchViewRange with SM_TURN/SM_ALIVE.
        }

        private void SendMobileMovement(ClientPacket defMsg, TBaseObject BaseObject)
        {
            if (BaseObject == null)
            {
                SendSocket(defMsg);
                return;
            }

            SendSocket(defMsg,
                BuildMobileActorStateBody(BaseObject.GetFeature(this), BaseObject));
        }
        public int dwTick57C = 0;
        // MOVE-74: m_boInSafeArea 已删除。它是 stock-Mir2 遗留，不对应任何原生槽 ——
        // 原生 Obj+0x3FE 的 28 个访问点(1 写 27 读)全部服务穿透判定
        // (WalkTo 第三参 / CanWalkEx 第四参 / MoveToMovingObject 第六参 / 三道挤人闸 /
        // tick 比较)，零个与名字颜色或 PK 显示相关，权威载体是
        // m_boThroughOccupancyCache。详见 docs/move74_through_predicate_20260814.md。
        
        
        
        public string m_sLoginAccount = string.Empty;
        public string m_sUserID;
        
        
        
        public string m_sIPaddr = string.Empty;
        public string m_sIPLocal = string.Empty;
        public int m_nSocket = 0;
        
        
        
        public ushort m_nGSocketIdx = 0;
        
        
        
        public int m_nGateIdx = 0;
        public long m_UserGeneration = 0;
        public int m_nSoftVersionDate = 0;
        
        
        
        public DateTime m_dLogonTime;
        
        
        
        public int m_dwLogonTick = 0;
        
        
        
        public bool m_boReadyRun = false;
        
        
        
        public int m_nSessionID = 0;

        // Opaque Type1 0x0050 data; it must not override gate routing fields.
        public byte[] m_NativeDbSessionSuffix = Array.Empty<byte>();

        // Runtime-only fields carried by the original saveMode=2 block at
        // HumanInfo suffix +0xA0. Unknown business meanings remain offset-named.
        internal int m_nNativeSwitchSerial;
        internal bool m_boNativeSwitchOffsetB75;
        internal bool m_boNativeClientVersionHandshakeDone;
        internal string m_sNativeClientVersion = string.Empty;
        internal int m_dwNativeClientVersionCheckTick;
        internal byte m_btNativeRepairMode;
        internal bool m_boNativeSwitchHeroHandoffPending;
        internal ushort m_wNativeSwitchOffsetD38;
        internal int m_nNativeSwitchOffsetD3C;
        internal int m_nNativeSwitchOffsetD40;
        internal int m_dwNativeSwitchOffsetD44;
        internal int m_dwNativeSwitchHeroKind0Tick;
        internal int m_dwNativeSwitchHeroKind1Tick;
        internal byte m_btNativeHeroRequestKind;
        internal byte m_btNativeHeroRequestSlot;
        
        
        
        public int m_nPayMent = 0;
        public int m_nPayMode = 0;
        
        
        
        public TSessInfo m_SessInfo = null;
        public int m_dwLoadTick = 0;
        
        
        
        public int m_nServerIndex = 0;
        public bool m_boEmergencyClose = false;
        
        
        
        public bool m_boSoftClose = false;
        
        
        
        public bool m_boKickFlag = false;
        
        
        
        public bool m_boReconnection = false;
        public bool m_boRcdSaved = false;
        public bool m_boSwitchData = false;
        public bool m_boSwitchDataOK = false;
        public string m_sSwitchDataTempFile = string.Empty;
        public int m_nWriteChgDataErrCount = 0;
        public string m_sSwitchMapName = string.Empty;
        public short m_nSwitchMapX = 0;
        public short m_nSwitchMapY = 0;
        public bool m_boSwitchDataSended = false;
        public int m_dwChgDataWritedTick = 0;
        
        
        
        public int m_dwHitIntervalTime = 0;
        
        
        
        public int m_dwMagicHitIntervalTime = 0;
        
        
        
        public int m_dwRunIntervalTime = 0;
        
        
        
        public int m_dwWalkIntervalTime = 0;
        
        
        
        public int m_dwTurnIntervalTime = 0;
        
        
        
        public int m_dwActionIntervalTime = 0;
        
        
        
        public int m_dwRunLongHitIntervalTime = 0;
        
        
        
        public int m_dwRunHitIntervalTime = 0;
        
        
        
        public int m_dwWalkHitIntervalTime = 0;
        
        
        
        public int m_dwRunMagicIntervalTime = 0;
        
        
        
        public int m_dwMagicAttackTick = 0;
        
        
        
        public int m_dwMagicAttackInterval = 0;
        
        
        
        public int m_dwAttackTick = 0;
        
        
        
        public int m_dwMoveTick = 0;
        
        
        
        public int m_dwAttackCount = 0;
        
        
        
        public int m_dwAttackCountA = 0;
        
        
        
        public int m_dwMagicAttackCount = 0;
        
        
        
        public int m_dwMoveCount = 0;
        
        
        
        public int m_dwMoveCountA = 0;
        
        
        
        public int m_nOverSpeedCount = 0;
        public bool m_boDieInFight3Zone = false;
        public string m_sGotoNpcLabel = string.Empty;
        public int m_nDelayCall = 0;
        public int m_dwDelayCallTick = 0;
        public bool m_boDelayCall = false;
        public int m_DelayCallNPC = 0;
        public string m_sDelayCallLabel = string.Empty;
        public TBaseObject m_NPC = null;
        
        
        
        /// <summary>
        /// The legacy `$STR(P&lt;n&gt;)` text-substitution bank, n in 0..99 - see
        /// M2Share.GetValNameNo and its only reader, NormNpc.ReplaceVariableText.
        /// It is NOT the quest V bank, despite the sizing and the comment it used to
        /// carry; nothing keys it by group*1000+index and nothing outside the `$STR`
        /// path touches it.
        /// <para>
        /// A disp-aware census of every [base+disp] access with disp in [0x800,0xA00)
        /// over 0x401000..0x7EFFFF finds the player object using +0x804 (6 sites, all
        /// GetS/SetS/decoder/encoder), +0x808 (7 sites, all GetV/SetV/decoder/encoder),
        /// then NOTHING until +0x99C - and 0x99C is exactly 0x80C + 100*4. The only
        /// index-scaled accesses anywhere in that window are the two inline slot
        /// instructions 0x6DF20F and 0x6DF2A8. So the 100 dwords behind the V pointer
        /// are dedicated storage reached solely through GetV/SetV group 0, and there
        /// is no second native array in the object for this field to be mirroring.
        /// </para>
        /// </summary>
        public int[] m_nVal;
        public Dictionary<int, int> m_ScriptVVars;
        public Dictionary<int, int> m_ScriptSVars;

        /// <summary>
        /// The group-0 V slots, which native keeps inside the object rather than in
        /// the +0x808 V dictionary. GetV/SetV address them as
        /// <c>[self + index*4 + 0x808]</c> for index 1..100, i.e. the 100 dwords at
        /// +0x80C..+0x99B packed immediately behind the dictionary pointer:
        /// <code>
        ///   SetV 0x6DF299  85 FF                 test edi, edi      ; group
        ///        0x6DF29B  75 16                 jne  0x6DF2B3      ; != 0 -> keyed
        ///        0x6DF29F  4A                    dec  edx           ; index - 1
        ///        0x6DF2A0  83 EA 64              sub  edx, 0x64
        ///        0x6DF2A3  73 0E                 jae  0x6DF2B3      ; outside 1..100
        ///        0x6DF2A8  89 84 B3 08 08 00 00  mov [ebx+esi*4+0x808], eax
        ///   GetV 0x6DF20F  8B 84 83 08 08 00 00  mov eax, [ebx+eax*4+0x808]
        /// </code>
        /// Two consequences follow from it being plain object memory. A slot that was
        /// never written reads back as 0, because Delphi's InitInstance zero-fills the
        /// instance - not the -1 that a dictionary miss produces. And the region is
        /// session-scoped: the save decoder sub_6E448C references +0x804 and +0x808
        /// (the two dictionaries) and nothing in +0x80C..+0x99B, so these slots are
        /// never written to the character record. Index 0 is unused, kept so the array
        /// can be indexed by the native index directly.
        /// <para>
        /// S has no counterpart. GetS/SetS reject group 0 outright and own no inline
        /// region - see NativeScriptVarArgsAccepted.
        /// </para>
        /// </summary>
        public int[] m_ScriptVGroup0;

        /// <summary>
        /// The single resolver for a (bank, group, index) triple. Everything that reads
        /// script variables has to go through here, because the storage a triple lands
        /// in is not derivable from a flat <c>group*1000+index</c> key alone: group 0 of
        /// the V bank lives in <see cref="m_ScriptVGroup0"/>, not in the dictionary.
        /// Code that computed the flat key itself and indexed the dictionary silently
        /// read nothing for group 0 - a flat key below 1000 can only mean group 0, so it
        /// can never match a dictionary entry either.
        /// <para>
        /// Returns false when native would report a miss. For the keyed path that is a
        /// key that was never written; for V group 0 it is an index outside 1..100,
        /// which native rejects at 0x6DF20A <c>sub edx,0x64</c> / <c>jae</c>. An in-range
        /// group-0 slot always reports true, since the region is zero-filled rather than
        /// sparse. Callers keep their own miss default: native GetV answers -1, but the
        /// mall and the drop-script gate both treat a miss as 0.
        /// </para>
        /// </summary>
        public bool TryGetScriptVar(char bank, int group, int index, out int value)
        {
            var upper = char.ToUpperInvariant(bank);
            if (upper == 'V' && group == 0)
            {
                if (index >= 1 && index <= 100)
                {
                    value = m_ScriptVGroup0[index];
                    return true;
                }
                value = 0;
                return false;
            }
            var store = upper == 'V' ? m_ScriptVVars : m_ScriptSVars;
            if (store != null && store.TryGetValue(group * 1000 + index, out value))
            {
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Write counterpart of <see cref="TryGetScriptVar"/>. A group-0 S write is
        /// dropped rather than filed under a flat key, matching native SetS rejecting a
        /// non-positive group outright at 0x6DF251 / 0x6DF255.
        /// </summary>
        public void SetScriptVar(char bank, int group, int index, int value)
        {
            var upper = char.ToUpperInvariant(bank);
            if (group == 0)
            {
                if (upper == 'V' && index >= 1 && index <= 100)
                {
                    m_ScriptVGroup0[index] = value;
                }
                return;
            }
            var store = upper == 'V' ? m_ScriptVVars : m_ScriptSVars;
            if (store != null)
            {
                store[group * 1000 + index] = value;
            }
            if (upper == 'V' && index > 0)
            {
                // 眼神「脚本控制人物爆率」在 keyed SetV 的 0x6DF2CC 装 trampoline，
                // index 2/3 旁路写 [self+0x579] / [self+0x18C]。桩体只比 index、
                // 不比 group，且 group 0 的快支够不到它。见 YanshenScriptDropRate。
                Plugins.YanshenScriptDropRate.OnKeyedSetV(this, index, value);
            }
        }



        public int[] m_nMval;
        
        
        
        public int[] m_DyVal;
        
        
        
        public string[] m_nSval;
        
        
        
        public int[] m_nInteger;
        
        
        
        public string[] m_sString;
        
        
        
        public string[] m_ServerStrVal;
        
        
        
        public int[] m_ServerIntVal;
        public string m_sPlayDiceLabel = string.Empty;
        public bool m_boTimeRecall = false;
        public int m_dwTimeRecallTick = 0;
        public string m_sMoveMap = string.Empty;
        public short m_nMoveX = 0;
        public short m_nMoveY = 0;
        
        
        
        public int m_dwSaveRcdTick = 0;
        public double m_dCreateDate;
        public byte m_btBright = 0;
        public bool m_boNewHuman = false;
        public bool m_boSendNotice = false;
        public int m_dwWaitLoginNoticeOKTick = 0;
        public bool m_boLoginNoticeOK = false;
        public bool bo6AB = false;
        
        
        
        public bool m_boExpire = false;
        public int m_dwShowLineNoticeTick = 0;
        public int m_nShowLineNoticeIdx = 0;
        public int m_nSoftVersionDateEx = 0;
        
        
        
        private readonly Hashtable m_CanJmpScriptLableList = null;
        public int m_nScriptGotoCount = 0;
        public string m_sScriptCurrLable = string.Empty;
        
        public string m_sScriptGoBackLable = string.Empty;
        
        public int m_dwTurnTick = 0;
        public int m_wOldIdent = 0;
        public byte m_btOldDir = 0;
        
        
        
        public bool m_boFirstAction = false;
        
        
        
        public int m_dwActionTick = 0;
        
        
        
        public string m_sDearName;
        public TPlayObject m_DearHuman = null;
        public bool m_boAllowMarry = true;
        
        
        
        public bool m_boCanDearRecall = false;
        public bool m_boCanMasterRecall = false;
        
        
        
        public int m_dwDearRecallTick = 0;
        public int m_dwMasterRecallTick = 0;
        
        
        
        public string m_sMasterName;
        public TPlayObject m_MasterHuman = null;
        public TPlayObject m_TargetPlayer;
        public IList<TPlayObject> m_MasterList = null;
        public bool m_boAllowMaster = false;
        public bool m_boMaster = false;
        public bool m_boRequestMaster = false;
        public TPlayObject m_MasterRequestTarget = null;
        public int m_dwMasterRequestTime = 0;
        public bool m_boStudent = false;
        public byte m_btStudentOrder = 0;
        public int m_nStudentCount = 0;
        public string[] m_sStudentNames = new string[5];
        
        
        
        public byte m_btCreditPoint = 0;
        public byte m_btAntiAddictionTier = 0;
        public int m_nShengWan = 0;
        public int m_nForceLv = 0;
        public int m_nForceExp = 0;
        public int m_nFightPoints = 0;
        public int m_nSfLevel = 0;
        internal double m_dNativeHeroIntimacy = 0;
        internal int m_nNativeHeroIntimacyBase = 0;
        internal int m_nNativeHeroIntimacyCurrent = 0;
        internal byte[] m_NativeHeroExperienceAccumulator = new byte[24];
        public byte m_btSecHeroPracticeRewardMode = 0;
        public byte m_btSecHeroPracticeCostTier = 0;
        public ushort m_wSecHeroPracticeLevel = 0;
        public byte m_btGoldActNextLevel = 0;
        public byte m_btFirstUsedGiftStage = 0;
        public int m_nActivePoint = 0;
        // #16 PAS shadow-var dedicated native fields (gm-playerattr offsets,
        // idat-verified in staging/pas_shadow_field_offsets_20260801.md).
        public byte m_btPlatLv = 0;       // ObjPlayer.PlatLv,      native Self+0xB85 (Byte, RW)
        public uint m_dwJiaYouPoint = 0;  // ObjPlayer.JiaYouPoint, native Self+0xAF0 (Cardinal, RO property)

        /// <summary>
        /// native <c>Self+0x1828</c> (Byte) — Anti-fatigue tier (防沉迷疲劳档位).
        /// Values: 0=normal, 1=normal, 2=half-speed (mining 1/96, drop multiplier 2),
        /// 3=hard-block (mining disabled, drop disabled).
        /// Binary: 0x6BC202 (mining gate), 0x6BC2A3 (mining tier==2 check),
        /// 0x71FADA/0x71FB1E (drop gates). Default 0 = not in effect.
        /// </summary>
        public byte m_btNativeFatigueTier = 0;

        /// <summary>
        /// native <c>Self+0x1829</c> (Byte) — Cheat penalty tier (外挂惩罚档位).
        /// Values: 0=normal, 1=normal, 2=penalty, 3=hard-block.
        /// Binary: 0x6BC21E (mining gate), 0x71FAE3 (drop gate).
        /// Default 0 = not in effect.
        /// </summary>
        public byte m_btNativeCheatPenaltyTier = 0;

        /// <summary>
        /// native <c>Self+0x1898</c> (Byte) — "show the 副将 level-cap hint" preference.
        /// Constructor default is ON (<c>0x6AD8C0 C6 87 98 18 00 00 01
        /// mov byte [edi+0x1898],1</c>), the client flips it with CM 1239
        /// (<c>0x6DA3AF</c> = 1 when Param == 0, <c>0x6DA3BE</c> = 0 otherwise), and the
        /// single reader is the hero experience adder <c>sub_687714</c> at
        /// <c>0x68781D 80 B8 98 18 00 00 00 cmp byte [owner+0x1898],0 / 0x687824 je</c>,
        /// which suppresses the 1-in-100 「您的副将英雄等级受限于主将等级」 hint.
        /// A whole-CODE scan finds exactly those four references and no persistence
        /// site, so the flag is per-session and never reaches the character record.
        /// </summary>
        public bool m_boNativeHeroCapHintEnabled = true;

        /// <summary>
        /// native <c>Self+0x18AC</c> (Byte) — "let other players read my hero's
        /// 28-byte record" permission, flipped by CM 1281 (<c>0x6DA9D7</c> = 0 when
        /// Param == 0, <c>0x6DA9F0</c> = 1 when Param == 1, any other Param leaves it
        /// alone). A whole-CODE scan finds three real references — those two writes
        /// and one read — and NO constructor write, so Delphi zero-init leaves the
        /// default at 0 (sharing OFF).
        /// <para>
        /// The single reader is <c>sub_68DAD0</c>, a hero virtual method
        /// (<c>33 D2 / 8B 88 8C 06 00 00 mov ecx,[hero+0x68C] / 85 C9 / 74 06 /
        /// 8A 91 AC 18 00 00 mov dl,[owner+0x18AC] / 8B C2 / C3</c>) that occupies VMT
        /// slot <c>+0x27C</c> in all seven hero-family VMTs (anchored off
        /// <c>sub_690B08</c> at slot +0x154: <c>0x685784 - 0x154 = 0x685630</c> base,
        /// and <c>sub_68DAD0</c> sits at <c>0x6858AC = base + 0x27C</c>). Slot +0x27C
        /// has exactly ONE call site in the whole image — <c>0x6DA96F</c>, inside the
        /// CM 1280 hero-inspect handler — where a true result selects the
        /// SM 3291 reply that carries <c>target+0x554</c> (28 bytes) and a false result
        /// selects the empty-body SM 3291.
        /// </para>
        /// <para>
        /// CM 1280 itself is still MISSING (the <c>+0x554</c> record layout has not
        /// been reversed), so this flag has no live consumer yet; it is stored so the
        /// preference the client sends today is not silently discarded.
        /// </para>
        /// </summary>
        public bool m_boNativeHeroRecordShared;

        /// <summary>
        /// native <c>Self+0x18A0</c> (Word) — 元宝 trade-protection amount, persisted
        /// at rec+0x050C. Setter <c>0x6D154E</c>; named by the in-function literals
        /// 「已成功设置交易保护金额为：」 / 「修改元宝交易金额」.
        /// </summary>
        public ushort m_nNativeTradeProtectAmount = 0;

        /// <summary>
        /// native <c>Self+0x18A4</c> (Word) — the accumulator paired with
        /// <see cref="m_nNativeTradeProtectAmount"/>, persisted at rec+0x0534.
        /// Cap 0x1F4 = 500, and exceeding it RESETS TO ZERO rather than clamping
        /// (<c>0x633D7C jbe / 0x633D87 mov word 0</c>, twinned at
        /// <c>0x6F1652..0x6F165D</c>). Use
        /// <see cref="AccumulateNativeYuanBaoTrade"/> so that rule stays in one place.
        /// </summary>
        public ushort m_nNativeYuanBaoTradeAccum = 0;

        /// <summary>
        /// native <c>Self+0x578</c> (Byte, unsigned 0..255) — the 伤害分担
        /// ("damage sharing") bonus, persisted at rec+0x0537 and set by GM
        /// dispatch index 359 (<c>@ChgDmgShare</c>, <c>0x628036</c>). Consumed at
        /// <c>0x73DEB1</c> with an explicit zero-extend before being added into the
        /// word total at <c>obj+0x2DC</c>.
        /// </summary>
        public byte m_btNativeDamageShare = 0;

        /// <summary>
        /// write#2 源覆盖：0x73DEB1 <c>mov al,[esi+0x578]</c> 零扩展后 0x73DEB7
        /// <c>add word[esi+0x2DC],ax</c>。self+0x578 = <see cref="m_btNativeDamageShare"/>
        /// （unsigned byte），供 <see cref="NativeRecalcPhysicalReductionPercent"/> 累加进
        /// self+0x2DC。见 TBaseObject.NativePhysicalPercentReduction.cs 文件头 write#2。
        /// </summary>
        protected override int NativePhysicalReductionDamageShare() => m_btNativeDamageShare;

        /// <summary>
        /// 战神's capped accumulator for <see cref="m_nNativeYuanBaoTradeAccum"/>,
        /// implemented identically at two independent sites:
        /// <code>
        /// 633D75  add  word [esi+0x18A4], ax
        /// 633D7C  cmp  word [esi+0x18A4], 0x1F4   ; 500
        /// 633D85  jbe  0x633D90                   ; at-or-under cap -> keep
        /// 633D87  mov  word [esi+0x18A4], 0       ; OVER cap -> reset to ZERO
        /// </code>
        /// The overflow behaviour is a RESET, not a clamp. Replicating it as a
        /// clamp would let an over-cap account keep accumulating, which is the
        /// opposite of native. The add itself wraps at 16 bits before the compare,
        /// exactly as the `add word` does.
        /// </summary>
        public void AccumulateNativeYuanBaoTrade(int amount)
        {
            m_nNativeYuanBaoTradeAccum = unchecked(
                (ushort)(m_nNativeYuanBaoTradeAccum + amount));
            if (m_nNativeYuanBaoTradeAccum > 0x01F4)
            {
                m_nNativeYuanBaoTradeAccum = 0;
            }
        }
        
        
        
        public byte m_btMarryCount = 0;
        public bool m_boMarried = false;
        
        
        
        public byte m_btReLevel = 0;
        public byte m_btReColorIdx = 0;
        public int m_dwReColorTick = 0;
        
        
        
        public int m_nKillMonExpMultiple = 0;
        
        
        
        public int m_dwGetMsgTick = 0;
        public bool m_boSetStoragePwd = false;
        public bool m_boReConfigPwd = false;
        public bool m_boCheckOldPwd = false;
        public bool m_boUnLockPwd = false;
        public bool m_boUnLockStoragePwd = false;
        public bool m_boPasswordLocked = false;
        /// <summary>
        /// Native obj+0x674. SuperGm <c>0x006D785F C6 83 74 06 00 00 01</c> 是
        /// 全镜像对该字节的唯一写入。尚未找到对应的 byte 读点（密码核对路径 BLOCKED）。
        /// </summary>
        public bool m_boWaitSuperGmPassword = false;
        
        public byte m_btPwdFailCount = 0;
        
        
        
        public bool m_boLockLogon = false;
        
        
        
        public bool m_boLockLogoned = false;
        public string m_sTempPwd;
        public string m_sStoragePwd;
        public TBaseObject m_PoseBaseObject = null;
        public bool m_boStartMarry = false;
        public int m_dwMarryRequestTime = 0;
        public bool m_boStartMaster = false;
        public bool m_boStartUnMarry = false;
        public bool m_boStartUnMaster = false;
        
        
        
        public bool m_boFilterSendMsg = false;
        
        
        
        public int m_nKillMonExpRate = 0;
        
        
        
        public int m_nPowerRate = 0;
        public int m_dwKillMonExpRateTime = 0;
        public int m_dwPowerRateTime = 0;
        public int m_dwRateTick = 0;
        
        
        
        public bool m_boCanUseItem = false;
        public bool m_boCanDeal = false;
        public bool m_boCanDrop = false;
        public bool m_boCanGetBackItem = false;

        /// <summary>战神 obj+0x454：TPercentResumeDrug 冷却 tick（sub_747E80）。</summary>
        internal int m_dwNativePercentResumeDrugTick;
        public bool m_boCanWalk = false;
        public bool m_boCanRun = false;
        public bool m_boCanHit = false;
        public bool m_boCanSpell = false;
        public bool m_boCanSendMsg = false;
        public int m_nMemberType = 0;
        
        public int m_nMemberLevel = 0;
        
        public bool m_boSendMsgFlag = false;
        
        public bool m_boChangeItemNameFlag = false;
        
        
        
        public int m_nGameGold = 0;
        
        
        
        public bool m_boDecGameGold = false;
        public int m_dwDecGameGoldTime = 0;
        public int m_dwDecGameGoldTick = 0;
        public int m_nDecGameGold = 0;
        
        public bool m_boIncGameGold = false;
        
        public int m_dwIncGameGoldTime = 0;
        public int m_dwIncGameGoldTick = 0;
        public int m_nIncGameGold = 0;
        
        public int m_nGamePoint = 0;
        
        public int m_dwIncGamePointTick = 0;
        public int m_nPayMentPoint = 0;
        public int m_dwPayMentPointTick = 0;
        public int m_dwDecHPTick = 0;
        public int m_dwIncHPTick = 0;
        public TPlayObject m_GetWhisperHuman = null;
        public int m_dwClearObjTick = 0;
        public short m_wContribution = 0;
        
        public string m_sRankLevelName = string.Empty;
        
        public bool m_boFilterAction = false;
        public bool m_boClientFlag = false;
        public byte m_nStep = 0;
        public int m_nClientFlagMode = 0;
        public int m_dwAutoGetExpTick = 0;
        public int m_nAutoGetExpTime = 0;
        public int m_nAutoGetExpPoint = 0;
        public Envirnoment m_AutoGetExpEnvir = null;
        public bool m_boAutoGetExpInSafeZone = false;
        public Dictionary<string, TDynamicVar> m_DynamicVarList = null;
        public short m_dwClientTick = 0;
        
        
        
        public bool m_boShutup;
        public bool m_boTestSpeedMode = false;
        public int m_dwDelayTime = 0;
        public string m_sRandomNo = string.Empty;
        public int m_dwQueryBagItemsTick = 0;
        private int _nextClientItemId;
        public const int STORAGE_PAGE_SIZE = 48;
        // 战神【确有多页仓库】(48*4=192)，容量是存档里的真实字段：原生存档解码器
        // DBSvr/Core/NativeHumanDataCodec.cs 的 StorageSpaceCountOffset=0x050E 从战神二进制逆出，
        // THumDataInfo.StorageItems 宽度 192，SM_SAVEITEMLIST 带 (页内数量, 每页48, 当前页) 分页协议。
        // ⚠️ 勿按 staging/ref-MIR2/GameOfMir 那份【别的 Mir2 分支】的 `< 39` 去夹容量——那不是战神。
        public const int MIN_STORAGE_ITEM_COUNT = 24;
        public const int MAX_STORAGE_ITEM_COUNT = 192;
        public int m_nStorageSpaceCount = STORAGE_PAGE_SIZE;
        public int m_nStoragePage = 0;
        public byte[] m_NativeHumanData;
        public uint m_NativeHumanDataCrc;
        public byte[] m_NativeScriptData;
        public uint m_NativeScriptDataCrc;
        public uint m_dwChatShieldMask;
        public HeroObject m_HeroObject;
        // rec+0x4F8: 0x6B12A0 mov eax,[ebx+0xB9C] / 0x6B12A6 89 86 F8 04 00 00 mov [esi+0x4F8],eax
        // load: 0x6B029C mov eax,[eax+0x4F8] / 0x6B02A5 mov [edx+0xB9C],eax
        internal const int NativeChatShieldMaskOffset = 0x4F8;
        internal const int NativeHeroStateOffset = 0x52;
        public byte m_btNativeHeroState;
        public int m_dwHeroLogoutTick;

        internal void RestoreNativeHeroState()
        {
            m_btNativeHeroState = m_NativeScriptData != null
                                  && m_NativeScriptData.Length > NativeHeroStateOffset
                ? m_NativeScriptData[NativeHeroStateOffset]
                : (byte)0;
        }

        internal bool PersistNativeHeroState()
        {
            if (m_NativeScriptData == null
                || m_NativeScriptData.Length <= NativeHeroStateOffset)
                return m_btNativeHeroState == 0;
            m_NativeScriptData[NativeHeroStateOffset] = m_btNativeHeroState;
            return true;
        }

        internal void RestoreNativeChatShieldMask()
        {
            m_dwChatShieldMask = m_NativeHumanData != null
                                 && m_NativeHumanData.Length >=
                                 NativeChatShieldMaskOffset + sizeof(uint)
                ? BinaryPrimitives.ReadUInt32LittleEndian(
                    m_NativeHumanData.AsSpan(NativeChatShieldMaskOffset, sizeof(uint)))
                : 0;
            ApplyChatShieldMaskToAllowFlags();
        }

        // Native has only obj+0xB9C. C# bools are mirrors so @ALLOWMSG/@LETSHOUT/@BANGUILDCHAT
        // and CM 3032 last-writer-win the same bits (0x6236FE/0x6237AF/0x623984).
        internal void ApplyChatShieldMaskToAllowFlags()
        {
            m_boHearWhisper = (m_dwChatShieldMask & 0x01u) == 0;
            m_boBanShout = (m_dwChatShieldMask & 0x04u) == 0;
            m_boBanGuildChat = (m_dwChatShieldMask & 0x08u) == 0;
        }

        internal bool PersistNativeChatShieldMask()
        {
            if (m_NativeHumanData == null
                || m_NativeHumanData.Length < NativeChatShieldMaskOffset + sizeof(uint))
                return m_dwChatShieldMask == 0;
            BinaryPrimitives.WriteUInt32LittleEndian(
                m_NativeHumanData.AsSpan(NativeChatShieldMaskOffset, sizeof(uint)),
                m_dwChatShieldMask);
            return true;
        }

        internal void SendNativeClientConfig()
        {
            SendDefMessage(Grobal2.SM_CLIENT_CONF,
                unchecked((int)m_dwChatShieldMask), 0, 0, 0, "");
        }
        public bool m_boTimeGoto;
        public int m_dwTimeGotoTick;
        public string m_sTimeGotoLable;
        public TBaseObject m_TimeGotoNPC;
        
        
        
        public int[] AutoTimerTick;
        
        
        
        public int[] AutoTimerStatus;
        
        
        
        public int m_dwClickNpcTime = 0;
        public TPlayObject() : base()
        {
            m_NativeCattle = new TBodyCattleU(this);
            m_btRaceServer = Grobal2.RC_PLAYOBJECT;
            m_boAddToMaped = false;
            m_boDelFormMaped = true;
            m_boEmergencyClose = false;
            m_boSwitchData = false;
            m_boReconnection = false;
            m_boKickFlag = false;
            m_boSoftClose = false;
            m_boReadyRun = false;
            m_dwSaveRcdTick = HUtil32.GetTickCount();
            m_dwSecHeroPracticeTick = HUtil32.GetTickCount();
            m_nSecHeroPracticeLingFuUsed = 0;
            m_boWantRefMsg = true;
            m_boRcdSaved = false;
            m_boDieInFight3Zone = false;
            m_sGotoNpcLabel = "";
            m_nDelayCall = 0;
            m_sDelayCallLabel = "";
            m_boDelayCall = false;
            m_DelayCallNPC = 0;
            m_boTimeRecall = false;
            m_sMoveMap = "";
            m_nMoveX = 0;
            m_nMoveY = 0;
            m_dwRunTick = HUtil32.GetTickCount();
            m_nRunTime = 250;
            m_dwSearchTime = 1000;
            m_dwSearchTick = HUtil32.GetTickCount();
            m_nViewRange = 12;
            m_boNewHuman = false;
            m_boLoginNoticeOK = false;
            bo6AB = false;
            m_boExpire = false;
            m_boSendNotice = false;
            m_dwCheckDupObjTick = HUtil32.GetTickCount();
            dwTick578 = HUtil32.GetTickCount();
            dwTick57C = HUtil32.GetTickCount();
            m_dwMagicAttackTick = HUtil32.GetTickCount();
            m_dwMagicAttackInterval = 0;
            m_dwAttackTick = HUtil32.GetTickCount();
            m_dwMoveTick = HUtil32.GetTickCount();
            m_dwTurnTick = HUtil32.GetTickCount();
            m_dwActionTick = HUtil32.GetTickCount();
            m_dwAttackCount = 0;
            m_dwAttackCountA = 0;
            m_dwMagicAttackCount = 0;
            m_dwMoveCount = 0;
            m_dwMoveCountA = 0;
            m_nOverSpeedCount = 0;
            m_sOldSayMsg = "";
            m_nSayMsgCount = 0;
            m_btSayRapidCount = 0;
            m_dwSayMsgTick = HUtil32.GetTickCount();
            m_boDisableSayMsg = false;
            m_dwDisableSayMsgTick = HUtil32.GetTickCount();
            m_dLogonTime = DateTime.Now;
            m_dwLogonTick = HUtil32.GetTickCount();
            _nextClientItemId = unchecked(m_dwLogonTick + 1000);
            m_boSwitchData = false;
            m_boSwitchDataSended = false;
            m_nWriteChgDataErrCount = 0;
            m_dwShowLineNoticeTick = HUtil32.GetTickCount();
            m_nShowLineNoticeIdx = 0;
            m_nSoftVersionDateEx = 0;
            m_CanJmpScriptLableList = new Hashtable(StringComparer.OrdinalIgnoreCase);
            m_nKillMonExpMultiple = 1;
            m_nKillMonExpRate = 100;
            m_dwRateTick = HUtil32.GetTickCount();
            m_nPowerRate = 100;
            m_boSetStoragePwd = false;
            m_boReConfigPwd = false;
            m_boCheckOldPwd = false;
            m_boUnLockPwd = false;
            m_boUnLockStoragePwd = false;
            m_boPasswordLocked = false;
            m_boWaitSuperGmPassword = false;
            
            m_btPwdFailCount = 0;
            m_sTempPwd = "";
            m_sStoragePwd = "";
            m_boFilterSendMsg = false;
            m_boCanDeal = true;
            m_boCanDrop = true;
            m_boCanGetBackItem = true;
            m_boCanWalk = true;
            m_boCanRun = true;
            m_boCanHit = true;
            m_boCanSpell = true;
            m_boCanUseItem = true;
            m_nMemberType = 0;
            m_nMemberLevel = 0;
            m_nGameGold = 0;
            m_boDecGameGold = false;
            m_nDecGameGold = 1;
            m_dwDecGameGoldTick = HUtil32.GetTickCount();
            m_dwDecGameGoldTime = 60 * 1000;
            m_boIncGameGold = false;
            m_nIncGameGold = 1;
            m_dwIncGameGoldTick = HUtil32.GetTickCount();
            m_dwIncGameGoldTime = 60 * 1000;
            m_nGamePoint = 0;
            m_dwIncGamePointTick = HUtil32.GetTickCount();
            m_nPayMentPoint = 0;
            m_DearHuman = null;
            m_MasterHuman = null;
            m_MasterList = new List<TPlayObject>();
            m_boSendMsgFlag = false;
            m_boChangeItemNameFlag = false;
            m_boCanMasterRecall = false;
            m_boCanDearRecall = false;
            m_dwDearRecallTick = HUtil32.GetTickCount();
            m_dwMasterRecallTick = HUtil32.GetTickCount();
            m_btReColorIdx = 0;
            m_GetWhisperHuman = null;
            m_boOnHorse = false;
            m_wContribution = 0;
            m_sRankLevelName = M2Share.g_sRankLevelName;
            m_boFixedHideMode = true;
            m_nStep = 0;
            // Only 0..99 are addressable ($STR(P<n>), M2Share.GetValNameNo); the width
            // is a leftover from a comment that mistook this for the quest V bank.
            m_nVal = new int[20000];
            m_ScriptVVars = new Dictionary<int, int>();
            m_ScriptSVars = new Dictionary<int, int>();
            m_ScriptVGroup0 = new int[101];
            m_nMval = new int[100];
            m_DyVal = new int[100];
            m_nSval = new string[100];
            m_nInteger = new int[100];
            m_sString = new string[100];
            m_ServerStrVal = new string[100];
            m_ServerIntVal = new int[100];
            m_nClientFlagMode = -1;
            m_dwAutoGetExpTick = HUtil32.GetTickCount();
            m_nAutoGetExpPoint = 0;
            m_AutoGetExpEnvir = null;
            m_dwHitIntervalTime = M2Share.g_Config.dwHitIntervalTime;// 攻击间隔
            m_dwMagicHitIntervalTime = M2Share.g_Config.dwMagicHitIntervalTime;// 魔法间隔
            m_dwRunIntervalTime = M2Share.g_Config.dwRunIntervalTime;// 走路间隔
            m_dwWalkIntervalTime = M2Share.g_Config.dwWalkIntervalTime;// 走路间隔
            m_dwTurnIntervalTime = M2Share.g_Config.dwTurnIntervalTime;// 换方向间隔
            m_dwActionIntervalTime = M2Share.g_Config.dwActionIntervalTime;// 组合操作间隔
            m_dwRunLongHitIntervalTime = M2Share.g_Config.dwRunLongHitIntervalTime;// 组合操作间隔
            m_dwRunHitIntervalTime = M2Share.g_Config.dwRunHitIntervalTime;// 组合操作间隔
            m_dwWalkHitIntervalTime = M2Share.g_Config.dwWalkHitIntervalTime;// 组合操作间隔
            m_dwRunMagicIntervalTime = M2Share.g_Config.dwRunMagicIntervalTime;// 跑位魔法间隔
            m_DynamicVarList = new Dictionary<string, TDynamicVar>(StringComparer.OrdinalIgnoreCase);
            m_SessInfo = null;
            m_boTestSpeedMode = false;
            m_boLockLogon = true;
            m_boLockLogoned = false;
            m_boTimeGoto = false;
            m_dwTimeGotoTick = HUtil32.GetTickCount();
            m_sTimeGotoLable = "";
            m_TimeGotoNPC = null;
            AutoTimerTick = new int[20];
            AutoTimerStatus = new int[20];
            m_sRandomNo = M2Share.RandomNumber.Random(999999).ToString();
        }

        private void SendNotice()
        {
            var LoadList = new List<string>();
            M2Share.NoticeManager.GetNoticeMsg("Notice", LoadList);
            var sNoticeMsg = string.Empty;
            if (LoadList.Count > 0)
            {
                for (var i = 0; i < LoadList.Count; i++)
                {
                    sNoticeMsg = sNoticeMsg + LoadList[i] + "\x20\x1B";
                }
            }
            LoadList = null;
            // 0x6B2CFE `33 C9 xor ecx,ecx` - nRecog is zero, not a duration.
            SendDefMessage(Grobal2.SM_SENDNOTICE, 0, 0, 0, 0, sNoticeMsg.Replace("/r/n/r/n ", ""));
        }

        public void RunNotice()
        {
            TProcessMessage Msg = null;
            const string sExceptionMsg = "[Exception] TPlayObject::RunNotice";
            if (m_boEmergencyClose || m_boKickFlag || m_boSoftClose)
            {
                if (m_boKickFlag)
                {
                    SendDefMessage(Grobal2.SM_OUTOFCONNECTION, 0, 0, 0, 0, "");
                }
                MakeGhost();
            }
            else
            {
                try
                {
                    if (!m_boSendNotice)
                    {
                        SendNotice();
                        m_boSendNotice = true;
                        m_boLoginNoticeOK = true;
                    }
                }
                catch (Exception)
                {
                    M2Share.ErrorMessage(sExceptionMsg);
                }
            }
        }

        private byte[] GetMobileAbility()
        {
            return BuildNativeAbilityPacket();
        }

        private byte[] BuildLogonBody()
        {
            // Native SM_LOGON body (40 bytes):
            // [0-3]   outlook (uint)
            // [4-19]  TAllBodyState (four 32-bit state words)
            // [20-23] allow-group flag (uint)
            // [24-27] reserved (uint) = 0
            // [28-37] TFeature
            // [38-39] padding (ushort) = 0
            var body = new byte[40];
            using (var ms = new MemoryStream(body))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(GetFeatureToLong());
                WriteBodyState(bw);
                bw.Write(m_boAllowGroup ? 1 : 0);
                bw.Write(0);
                bw.Write(GetMobileFeature());
                bw.Write((ushort)0);
            }
            return body;
        }

        /// <summary>
        /// Native CODE has zero 16-bit dx/cx loads of 772 (0x0304) reaching a send slot.
        /// The only 32-bit hit is <c>0x61116B sub eax,0x304</c> (a case bound, not an SM ident).
        /// srv_AppearTimes.ini 772=0. Constant kept.
        /// </summary>
        public void SendDlgMsg(string sMsg, int nType = 0)
        {
        }

        private void SendLogon()
        {
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_LOGON, ObjectId,
                m_nCurrX, m_nCurrY, m_btDirection);
            var body = BuildLogonBody();
            SendSocket(m_DefMsg, body);
        }

        private void SendNativeLoginNow()
        {
            var body = new byte[0x18];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), 0x14);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 0x2E);
            BitConverter.TryWriteBytes(body.AsSpan(8), DateTime.Now.ToOADate());
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_LOGIN_NOW, 0x3F1, 0x3E7, 0x1009, 0);
            SendSocket(m_DefMsg, body);
        }

        internal void SendNativeMapInfoExLogin()
        {
            var body = M2Share.MapManager?.GetNativeMapInfoExText()
                ?? string.Empty;
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_MAPINFO_EX,
                0, 0, 0, 0);
            SendSocket(header, body);
        }

        
        
        
        public void UserLogon()
        {
            TUserItem UserItem;
            var sIPaddr = "127.0.0.1";
            var logonStage = "start";
            const string sExceptionMsg = "[Exception] TPlayObject::UserLogon";
            const string sCheckIPaddrFail = "登录IP地址不匹配!!!";
            try
            {
                M2Share.CreditCardService?.TryLoad(this);
                if (M2Share.g_Config.boTestServer)
                {
                    if (m_Abil.Level < M2Share.g_Config.nTestLevel)
                    {
                        m_Abil.Level = (ushort)M2Share.g_Config.nTestLevel;
                    }
                    if (m_nGold < M2Share.g_Config.nTestGold)
                    {
                        m_nGold = M2Share.g_Config.nTestGold;
                    }
                }
                if (M2Share.g_Config.boTestServer || M2Share.g_Config.boServiceMode)
                {
                    m_nPayMent = 3;
                }
                if (!M2Share.g_Config.boAuthOpen && m_nPayMent == 1)
                {
                    m_nPayMent = 3;
                }
                m_dwMapMoveTick = HUtil32.GetTickCount();
                m_dLogonTime = DateTime.Now;
                m_dwLogonTick = HUtil32.GetTickCount();
                Initialize();
                logonStage = "initialized";
                SendNativeMapInfoExLogin();
                SendMsg(this, Grobal2.RM_LOGON, 0, 0, 0, 0, "");
                logonStage = "logon-sent";
                if (m_Abil.Level <= 7)
                {
                    if (GetRangeHumanCount() >= 80)
                    {
                        MapRandomMove(m_PEnvir.sMapName, 0);
                    }
                }
                if (m_boDieInFight3Zone)
                {
                    MapRandomMove(m_PEnvir.sMapName, 0);
                }
                if (M2Share.UserEngine.GetHumPermission(m_sCharName, ref sIPaddr, ref m_btPermission))
                {
                    if (M2Share.g_Config.PermissionSystem)
                    {
                        if (!M2Share.CompareIPaddr(m_sIPaddr, sIPaddr))
                        {
                            SysMsg(sCheckIPaddrFail, MsgColor.Red, MsgType.Hint);
                            // 不踢出玩家，仅警告 IP 不匹配（测试环境客户端 IP 常为 127.0.0.1 或空）
                        }
                    }
                }
                InitializeNativeClientVersionRunGate(HUtil32.GetTickCount());
                GetStartPoint();
                for (var i = m_MagicList.Count - 1; i >= 0; i--)
                {
                    CheckSeeHealGauge(m_MagicList[i]);
                }
                if (m_boNewHuman)
                {
                    UserItem = new TUserItem();
                    if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sCandle, ref UserItem))
                    {
                        m_ItemList.Add(UserItem);
                        SendAddItem(UserItem);
                    }
                    else
                    {
                        Dispose(UserItem);
                    }
                    UserItem = new TUserItem();
                    if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sBasicDrug, ref UserItem))
                    {
                        m_ItemList.Add(UserItem);
                        SendAddItem(UserItem);
                    }
                    else
                    {
                        Dispose(UserItem);
                    }
                    UserItem = new TUserItem();
                    if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sWoodenSword, ref UserItem))
                    {
                        m_ItemList.Add(UserItem);
                        SendAddItem(UserItem);
                    }
                    else
                    {
                        Dispose(UserItem);
                    }
                    UserItem = new TUserItem();
                    var sItem = m_btGender == PlayGender.Man
                        ? M2Share.g_Config.sClothsMan
                        : M2Share.g_Config.sClothsWoman;
                    if (M2Share.UserEngine.CopyToUserItemFromName(sItem, ref UserItem))
                    {
                        m_ItemList.Add(UserItem);
                        SendAddItem(UserItem);
                    }
                    else
                    {
                        Dispose(UserItem);
                    }
                }
                
                for (var i = m_ItemList.Count - 1; i >= 0; i--)
                {
                    UserItem = m_ItemList[i];
                    if (!string.IsNullOrEmpty(M2Share.UserEngine.GetStdItemName(UserItem.wIndex))) continue;
                    Dispose(m_ItemList[i]);
                    m_ItemList.RemoveAt(i);
                }
                
                if (M2Share.g_Config.boCheckUserItemPlace)
                {
                    for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
                    {
                        if (m_UseItems[i] == null || m_UseItems[i].wIndex <= 0) continue;
                        var StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                        if (StdItem != null)
                        {
                            if (!M2Share.CheckUserItems(i, StdItem))
                            {
                                UserItem = m_UseItems[i];
                                if (!AddItemToBag(UserItem))
                                {
                                    m_ItemList.Insert(0, UserItem);
                                }
                                m_UseItems[i].wIndex = 0;
                            }
                        }
                        else
                        {
                            m_UseItems[i].wIndex = 0;
                        }
                    }
                }
                
                for (var i = m_ItemList.Count - 1; i >= 0; i--)
                {
                    UserItem = m_ItemList[i];
                    var sItemName = M2Share.UserEngine.GetStdItemName(UserItem.wIndex);
                    for (var j = i - 1; j >= 0; j--)
                    {
                        var UserItem1 = m_ItemList[j];
                        if (M2Share.UserEngine.GetStdItemName(UserItem1.wIndex) == sItemName && UserItem.MakeIndex == UserItem1.MakeIndex)
                        {
                            m_ItemList.RemoveAt(j);
                            break;
                        }
                    }
                }
                // The per-slot tick stamps this used to re-base belonged to the
                // deleted seconds countdown. The node list carries its own
                // LastTick, set by AddState @0x7731AB (`call 0x408340` then
                // `mov [edx+6],eax`), so the CopyFrom during load already
                // stamped every restored node with the current tick.
                m_nCharStatus = GetCharStatus();
                logonStage = "before-recalc";
                RecalcLevelAbilitys();
                RecalcAbilitys();
                logonStage = "ability-recalculated";
                lock (s_initLock)
                {
                    if (btB2 == 0)
                    {
                        m_nPkPoint = 0;
                        btB2++;
                    }
                }
                if (m_nGold > M2Share.g_Config.nHumanMaxGold * 2 && M2Share.g_Config.nHumanMaxGold > 0)
                {
                    m_nGold = M2Share.g_Config.nHumanMaxGold * 2;
                }
                if (!bo6AB)
                {
                    if (m_nSoftVersionDate < M2Share.g_Config.nSoftVersionDate)
                    {
                        SysMsg(M2Share.sClientSoftVersionError, MsgColor.Red, MsgType.Hint);
                        SysMsg(M2Share.sDownLoadNewClientSoft, MsgColor.Red, MsgType.Hint);
                        SysMsg(M2Share.sForceDisConnect, MsgColor.Red, MsgType.Hint);
                        // PERF: diagnostic write removed
                    m_boEmergencyClose = true;
                        return;
                    }
                    if (m_nSoftVersionDateEx == 0 && M2Share.g_Config.boOldClientShowHiLevel)
                    {
                        SysMsg(M2Share.sClientSoftVersionTooOld, MsgColor.Blue, MsgType.Hint);
                        SysMsg(M2Share.sDownLoadAndUseNewClient, MsgColor.Red, MsgType.Hint);
                        if (!M2Share.g_Config.boCanOldClientLogon)
                        {
                            SysMsg(M2Share.sClientSoftVersionError, MsgColor.Red, MsgType.Hint);
                            SysMsg(M2Share.sDownLoadNewClientSoft, MsgColor.Red, MsgType.Hint);
                            SysMsg(M2Share.sForceDisConnect, MsgColor.Red, MsgType.Hint);
                            // PERF: diagnostic write removed
                    m_boEmergencyClose = true;
                            return;
                        }
                    }
                    if (M2Share.g_Config.boShowLoginAttackModeHint)
                    {
                        switch (m_btAttatckMode)
                        {
                            case NativeAttackModeAll:
                                SysMsg(M2Share.sAttackModeOfAll, MsgColor.Green, MsgType.Hint);
                                break;
                            case NativeAttackModePeace:
                                SysMsg(M2Share.sAttackModeOfPeaceful, MsgColor.Green, MsgType.Hint);
                                break;
                            case NativeAttackModeGroup:
                                SysMsg(M2Share.sAttackModeOfGroup, MsgColor.Green, MsgType.Hint);
                                break;
                            case NativeAttackModeGild:
                                SysMsg(M2Share.sAttackModeOfGuild, MsgColor.Green, MsgType.Hint);
                                break;
                            case NativeAttackModeHostile:
                                SysMsg(M2Share.sAttackModeOfHostile, MsgColor.Green, MsgType.Hint);
                                break;
                            case NativeAttackModeCorps:
                                SysMsg(M2Share.sAttackModeOfCorps, MsgColor.Green, MsgType.Hint);
                                break;
                        }
                        SysMsg(M2Share.sStartChangeAttackModeHelp, MsgColor.Green, MsgType.Hint);// 使用组合快捷键 CTRL-H 更改攻击...
                    }
                    // 战神 UserLogon @0x6B210C: `xor ecx,ecx; mov cl,[esi+0xAED]`
                    // (MyAttackMode -> nRecog); `mov dx,0x221` (=545) via VMT+0x250.
                    SendDefMessage(Grobal2.SM_ATTACKMODE, m_btAttatckMode, 0, 0, 0, "");
                    if (M2Share.g_Config.boTestServer)
                    {
                        SysMsg(M2Share.sStartNoticeMsg, MsgColor.Green, MsgType.Hint);// 欢迎进入本服务器进行游戏...
                    }
                    if (M2Share.UserEngine.PlayObjectCount > M2Share.g_Config.nTestUserLimit)
                    {
                        if (m_btPermission < 2)
                        {
                            SysMsg(M2Share.sOnlineUserFull, MsgColor.Red, MsgType.Hint);
                            SysMsg(M2Share.sForceDisConnect, MsgColor.Red, MsgType.Hint);
                            // PERF: diagnostic write removed
                    m_boEmergencyClose = true;
                        }
                    }
                }
                m_btBright = (byte)M2Share.g_nGameTime;
                m_Abil.MaxExp = GetLevelExp(m_Abil.Level);// 登录重新取得升级所需经验值
                logonStage = "before-init-messages";
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                SendMsg(this, Grobal2.RM_ADJUST_BONUS, 0, 0, 0, 0, "");
                SendMsg(this, Grobal2.RM_DAYCHANGING, 0, 0, 0, 0, "");
                SendMsg(this, Grobal2.RM_SENDUSEITEMS, 0, 0, 0, 0, "");
                SendMsg(this, Grobal2.RM_SENDMYMAGIC, 0, 0, 0, 0, "");
                SendMsg(this, Grobal2.RM_USERAZSETUP, 0, 0, 0, 0, "");
                SendRefMsg(Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                logonStage = "init-messages-sent";

                m_MyGuild = M2Share.GuildManager.MemberOfGuild(m_sCharName);
                if (m_MyGuild != null)
                {
                    m_sGuildRankName = m_MyGuild.GetRankName(this, ref m_nGuildRankNo);
                    for (var i = m_MyGuild.GuildWarList.Count - 1; i >= 0; i--)
                    {
                        SysMsg(m_MyGuild.GuildWarList[i] + " 正在与本行会进行行会战.", MsgColor.Green, MsgType.Hint);
                    }
                }
                RefShowName();
                SendNativeItemMovementSmsLoginNotice();
                M2Share.AuthenticationManager?.TryLoad(this);
                if (m_nPayMent == 1)
                {
                    if (!bo6AB)
                    {
                        SysMsg(M2Share.sYouNowIsTryPlayMode, MsgColor.Red, MsgType.Hint);
                    }
                    m_nGoldMax = M2Share.g_Config.nHumanTryModeMaxGold;
                    if (m_Abil.Level > M2Share.g_Config.nTryModeLevel)
                    {
                        SysMsg("测试状态可以使用到第 " + M2Share.g_Config.nTryModeLevel, MsgColor.Red, MsgType.Hint);
                        SysMsg("链接中断，请到以下地址获得收费相关信息。(http://www.mir2.com)", MsgColor.Red, MsgType.Hint);
                        m_boEmergencyClose = true;
                    }
                }
                if (m_nPayMent == 3 && !bo6AB && (M2Share.g_Config.boTestServer || M2Share.g_Config.boServiceMode))
                {
                    SysMsg(M2Share.g_sNowIsFreePlayMode, MsgColor.Green, MsgType.Hint);
                }
                if (M2Share.g_Config.boVentureServer)
                {
                    SysMsg("当前服务器运行于冒险模式.", MsgColor.Green, MsgType.Hint);
                }
                if (m_MagicArr[SpellsDef.SKILL_ERGUM] != null && !m_boUseThrusting)
                {
                    m_boUseThrusting = true;
                    // 战神 UserLogon @0x6B225B: after latching [obj+0x94]=1, sends
                    // ident 0x270 (624) with nParam=1 through VMT+0x250
                    // (`push 1; push 0; push 0; push 0; xor ecx,ecx; mov dx,0x270`).
                    // Native never sends a "+LNG" text body here: the "+LNG" string at
                    // 0x11A86D0 is outside CODE (>0x7A10D0) and has zero xrefs.
                    // SendDefMessage builds a fresh ClientPacket rather than reusing
                    // whatever m_DefMsg last held.
                    SendDefMessage(Grobal2.SM_THRUSTING, 0, 1, 0, 0, "");
                }
                if (m_PEnvir.Flag.boNORECONNECT)
                {
                    MapRandomMove(m_PEnvir.Flag.sNoReConnectMap, 0);
                }
                BeginNativeYbCreditLoad(unchecked((uint)HUtil32.GetTickCount()));
                SendNativeAuthenticationStatus();
                M2Share.HonorValueManager?.TryLoad(this);
                if (CheckDenyLogon())// 如果人物在禁止登录列表里则直接掉线而不执行下面内容
                {
                    return;
                }
                M2Share.PasEngine?.TryInitializeYanshen(this);
                M2Share.PasEngine?.TryCallScriptLabel("onLogin", "@onLogin", this);
                NotifyNativeMailboxOnLogin();
                TriggerNativeMailQuest();
                m_boFixedHideMode = false;
                // 战神 sub_6CCE40 (login, reached from TPlayer VMT+0x78 UserLogon
                // sub_6B1D64 @0x6B21CF) repairs a relationship flag whose social-block
                // name slot is empty BEFORE running the relink/notify legs: the clear
                // at 0x6CCE8A precedes the spouse announce, and the clear at 0x6CCED3
                // precedes both the graduation compare (0x6CCEB0) and the master
                // announce.  Must run ahead of CheckMarry/CheckMaster for the same
                // reason -- and it cannot live INSIDE CheckMarry, because the
                // `!IsNullOrEmpty(m_sDearName)` guard below excludes exactly the case
                // that needs healing.
                HealNativeRelationFlags();
                // 战神 UserLogon @0x6B2358 queues RM 0x3010 (`66 B9 10 30 mov cx,0x3010`
                // -> sub_765E68 with edx=eax=Self, six zero params), just ahead of the
                // 0x6B23C6 SM 888 send below. Being an enqueue, the login-state cluster
                // (SM 3324/1264/3554/3556) is delivered on the next Run tick, after every
                // direct SM this UserLogon writes. All four player legs are emitted
                // in native order; an empty 3556 cold-time list stays silent.
                SendMsg(this, Grobal2.RM_NATIVE_LOGON_STATE_SYNC, 0, 0, 0, 0, "");
                // Native UserLogon @0x6B23C6 call 0x6F05D8, immediately before the
                // 定位石 replay at 0x6B23E3. sub_6F05D8 first sends SM 888:
                //   0x6F05E2 68 E7 03 00 00  push 0x3E7   ; Param=999
                //   0x6F05E7 6A 00           push 0       ; Tag
                //   0x6F05E9 6A 00           push 0       ; Series
                //   0x6F05EB 6A 00           push 0       ; sMsg=nil
                //   0x6F05ED B9 EA 03 00 00  mov ecx,0x3EA ; Recog=1002
                //   0x6F05F2 66 BA 78 03     mov dx,0x378  ; ident 888
                //   0x6F05FA FF 96 50 02 00 00 call [esi+0x250]
                SendDefMessage(Grobal2.SM_LOGIN_VER, 0x3EA, 0x3E7, 0, 0, "");
                SendNativeLoginNow();
                // 战神 replays the 定位石 marker in the same logon body, AFTER the social
                // relink call at 0x6B21CF: 0x6B23E3 cmp byte [esi+0x18f8],0 / je skip,
                // else re-push SM 0x3026 (0x6B23EC-0x6B2414) with X=[esi+0x1908] and
                // Y=[esi+0x190a]. Without it the client draws no marker after a relog.
                ReplayNativeFixedCoordOnLogon();
                // 战神 UserLogon @0x6B24D2: call 0x6F071C always emits SM 4501
                // (0x1195) via [obj+0x254]. Empty [obj+0xAE8] -> Param=5 Len=0;
                // otherwise Param=0 + 0x40-byte corps desc. C# SendNativePlayerCorps
                // already matches that ladder; native fires it on login, not only
                // on CM_PLAYER_CORPS.
                SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
                // 战神 UserLogon @0x6B24D9: call 0x6F07CC always emits SM 4500
                // (0x1194) via [obj+0x254]. 0x6F07E3 cmp [ebx+0xAE8],0 / je
                // Param=5; else 0x6ADAE4 fail -> Param=12; else Param=0 +
                // 0x38-byte gild desc (0x6F0826 66 BA 94 11).
                SendNativePlayerGuild();
                // 战神 UserLogon @0x6B24E0: call 0x6F7638 always emits SM 4613
                // (0x6F7687 66 BA 05 12) with an 8-byte body.
                SendNativePendingRequestOnLogon();
                // 战神 UserLogon @0x6B24E7: call 0x6F769C always emits SM 4615
                // (0x6F76F1 66 BA 07 12) with an 8-byte body.
                SendNativeClearPendingRequestOnLogon();
                // 战神 UserLogon @0x6B24EE: call 0x6F772C always emits SM 4612
                // (0x6F7813 66 BA 04 12) via [obj+0x254], even when the notice
                // list is empty (Len=0).
                SendNativePendingNoticesOnLogon();
                // 战神 UserLogon @0x6B24F5: call 0x6AEE04 always emits SM 4628
                // (0x6AEE90 66 BA 14 12) Recog=0 Param=0 Tag=role Series=0.
                // [obj+0xAE8]==0 -> role 0 (0x6AEE0B xor esi,esi / 0x6AEE15 je).
                SendNativeSocialRoleRefresh();
                // 战神 login-burst virtual sub_6E9A98 (VMT slots 0x62F190/0x6ACACC)
                // fires SM 3554 (my whole timed-ability list) exactly once per login
                // via sub_6E99B8 -> [obj+0x254]. srv_AppearTimes 3554 = 50,911 = the
                // SM_LOGON count, so every player login emits it (empty list still
                // sends, Len=0). The burst's exact intra-login position is a VMT call
                // and not byte-pinned; the once-per-login contract is what matters, so
                // it is grouped with the other login list packets here.
                SendNativeTimedAbilityListOnLogon();
                if (!string.IsNullOrEmpty(m_sDearName))
                {
                    CheckMarry();
                }
                CheckMaster();
                m_boFilterSendMsg = M2Share.GetDisableSendMsgList(m_sCharName);
                
                if (M2Share.g_Config.boPasswordLockSystem)
                {
                    if (m_boPasswordLocked)
                    {
                        m_boCanGetBackItem = !M2Share.g_Config.boLockGetBackItemAction;
                    }
                    if (M2Share.g_Config.boLockHumanLogin && m_boLockLogon && m_boPasswordLocked)
                    {
                        m_boCanDeal = !M2Share.g_Config.boLockDealAction;
                        m_boCanDrop = !M2Share.g_Config.boLockDropAction;
                        m_boCanUseItem = !M2Share.g_Config.boLockUserItemAction;
                        m_boCanWalk = !M2Share.g_Config.boLockWalkAction;
                        m_boCanRun = !M2Share.g_Config.boLockRunAction;
                        m_boCanHit = !M2Share.g_Config.boLockHitAction;
                        m_boCanSpell = !M2Share.g_Config.boLockSpellAction;
                        m_boCanSendMsg = !M2Share.g_Config.boLockSendMsgAction;
                        m_boObMode = M2Share.g_Config.boLockInObModeAction;
                        m_boAdminMode = M2Share.g_Config.boLockInObModeAction;
                        SysMsg(M2Share.g_sActionIsLockedMsg + " 开锁命令: @" + M2Share.g_GameCommand.LOCKLOGON.sCmd, MsgColor.Red, MsgType.Hint);
                        SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sActionIsLockedMsg + "\\ \\" + "密码命令: @" + M2Share.g_GameCommand.PASSWORDLOCK.sCmd);
                    }
                    if (!m_boPasswordLocked)
                    {
                        SysMsg(format(M2Share.g_sPasswordNotSetMsg, M2Share.g_GameCommand.PASSWORDLOCK.sCmd), MsgColor.Red, MsgType.Hint);
                    }
                    if (!m_boLockLogon && m_boPasswordLocked)
                    {
                        SysMsg(format(M2Share.g_sNotPasswordProtectMode, M2Share.g_GameCommand.LOCKLOGON.sCmd), MsgColor.Red, MsgType.Hint);
                    }
                    SysMsg(M2Share.g_sActionIsLockedMsg + " 开锁命令: @" + M2Share.g_GameCommand.UNLOCK.sCmd, MsgColor.Red, MsgType.Hint);
                    SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sActionIsLockedMsg + "\\ \\" + "开锁命令: @" + M2Share.g_GameCommand.UNLOCK.sCmd + '\\' + "加锁命令: @" + M2Share.g_GameCommand.__LOCK.sCmd + '\\' + "设置密码命令: @" + M2Share.g_GameCommand.SETPASSWORD.sCmd + '\\' + "修改密码命令: @" + M2Share.g_GameCommand.CHGPASSWORD.sCmd);
                }
                
                m_dwIncGamePointTick = HUtil32.GetTickCount();
                m_dwIncGameGoldTick = HUtil32.GetTickCount();
                m_dwAutoGetExpTick = HUtil32.GetTickCount();
                ResumeSecHeroPracticeAfterLogon();
                InitializeNativeDiamondCacheAfterLogon();
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.ToString());
                M2Share.MainOutMessage($"[UserLogonError] stage={logonStage} chr={m_sCharName} {e}");
            }
            
        }

        
        
        
        
        private bool WeaptonMakeLuck()
        {
            if (m_UseItems[Grobal2.U_WEAPON] == null || m_UseItems[Grobal2.U_WEAPON].wIndex <= 0)
            {
                return false;
            }
            var nRand = 0;
            var StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_WEAPON].wIndex);
            if (StdItem != null)
            {
                nRand = Math.Abs(StdItem.Dc2 - StdItem.Dc) / 5;
            }
            if (M2Share.RandomNumber.Random(M2Share.g_Config.nWeaponMakeUnLuckRate) == 1)
            {
                MakeWeaponUnlock();
            }
            else
            {
                var boMakeLuck = false;
                if (m_UseItems[Grobal2.U_WEAPON].btValue[4] > 0)
                {
                    m_UseItems[Grobal2.U_WEAPON].btValue[4] -= 1;
                    SysMsg(M2Share.g_sWeaptonMakeLuck, MsgColor.Green, MsgType.Hint);
                    boMakeLuck = true;
                }
                else if (m_UseItems[Grobal2.U_WEAPON].btValue[3] < M2Share.g_Config.nWeaponMakeLuckPoint1)
                {
                    m_UseItems[Grobal2.U_WEAPON].btValue[3]++;
                    SysMsg(M2Share.g_sWeaptonMakeLuck, MsgColor.Green, MsgType.Hint);
                    boMakeLuck = true;
                }
                else if (m_UseItems[Grobal2.U_WEAPON].btValue[3] < M2Share.g_Config.nWeaponMakeLuckPoint2 && M2Share.RandomNumber.Random(nRand + M2Share.g_Config.nWeaponMakeLuckPoint2Rate) == 1)
                {
                    m_UseItems[Grobal2.U_WEAPON].btValue[3]++;
                    SysMsg(M2Share.g_sWeaptonMakeLuck, MsgColor.Green, MsgType.Hint);
                    boMakeLuck = true;
                }
                else if (m_UseItems[Grobal2.U_WEAPON].btValue[3] < M2Share.g_Config.nWeaponMakeLuckPoint3 && M2Share.RandomNumber.Random(nRand * M2Share.g_Config.nWeaponMakeLuckPoint3Rate) == 1)
                {
                    m_UseItems[Grobal2.U_WEAPON].btValue[3]++;
                    SysMsg(M2Share.g_sWeaptonMakeLuck, MsgColor.Green, MsgType.Hint);
                    boMakeLuck = true;
                }
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    RecalcAbilitys();
                    SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                }
                if (!boMakeLuck)
                {
                    SysMsg(M2Share.g_sWeaptonNotMakeLuck, MsgColor.Green, MsgType.Hint);
                }
            }
            return true;
        }

        
        
        
        
        private bool RepairWeapon()
        {
            if (m_UseItems[Grobal2.U_WEAPON] == null)
            {
                return false;
            }
            var UserItem = m_UseItems[Grobal2.U_WEAPON];
            if (UserItem.wIndex <= 0 || UserItem.DuraMax <= UserItem.Dura)
            {
                return false;
            }
            UserItem.DuraMax -= (ushort)((UserItem.DuraMax - UserItem.Dura) / M2Share.g_Config.nRepairItemDecDura);
            var nDura = HUtil32._MIN(5000, UserItem.DuraMax - UserItem.Dura);
            if (nDura <= 0) return false;
            UserItem.Dura += (ushort)nDura;
            SendMsg(this, Grobal2.RM_DURACHANGE, 1, UserItem.Dura, UserItem.DuraMax, 0, "");
            SysMsg(M2Share.g_sWeaponRepairSuccess, MsgColor.Green, MsgType.Hint);
            return true;
        }

        
        
        
        
        private bool SuperRepairWeapon()
        {
            if (m_UseItems[Grobal2.U_WEAPON] == null || m_UseItems[Grobal2.U_WEAPON].wIndex <= 0)
            {
                return false;
            }
            m_UseItems[Grobal2.U_WEAPON].Dura = m_UseItems[Grobal2.U_WEAPON].DuraMax;
            SendMsg(this, Grobal2.RM_DURACHANGE, 1, m_UseItems[Grobal2.U_WEAPON].Dura, m_UseItems[Grobal2.U_WEAPON].DuraMax, 0, "");
            SysMsg(M2Share.g_sWeaponRepairSuccess, MsgColor.Green, MsgType.Hint);
            return true;
        }

        private void WinLottery()
        {
            var nGold = 0;
            var nWinLevel = 0;
            var nRate = M2Share.RandomNumber.Random(M2Share.g_Config.nWinLotteryRate);
            if (nRate >= M2Share.g_Config.nWinLottery6Min && nRate <= M2Share.g_Config.nWinLottery6Max)
            {
                if (M2Share.g_Config.nWinLotteryCount < M2Share.g_Config.nNoWinLotteryCount)
                {
                    nGold = M2Share.g_Config.nWinLottery6Gold;
                    nWinLevel = 6;
                    M2Share.g_Config.nWinLotteryLevel6++;
                }
            }
            else if (nRate >= M2Share.g_Config.nWinLottery5Min && nRate <= M2Share.g_Config.nWinLottery5Max)
            {
                if (M2Share.g_Config.nWinLotteryCount < M2Share.g_Config.nNoWinLotteryCount)
                {
                    nGold = M2Share.g_Config.nWinLottery5Gold;
                    nWinLevel = 5;
                    M2Share.g_Config.nWinLotteryLevel5++;
                }
            }
            else if (nRate >= M2Share.g_Config.nWinLottery4Min && nRate <= M2Share.g_Config.nWinLottery4Max)
            {
                if (M2Share.g_Config.nWinLotteryCount < M2Share.g_Config.nNoWinLotteryCount)
                {
                    nGold = M2Share.g_Config.nWinLottery4Gold;
                    nWinLevel = 4;
                    M2Share.g_Config.nWinLotteryLevel4++;
                }
            }
            else if (nRate >= M2Share.g_Config.nWinLottery3Min && nRate <= M2Share.g_Config.nWinLottery3Max)
            {
                if (M2Share.g_Config.nWinLotteryCount < M2Share.g_Config.nNoWinLotteryCount)
                {
                    nGold = M2Share.g_Config.nWinLottery3Gold;
                    nWinLevel = 3;
                    M2Share.g_Config.nWinLotteryLevel3++;
                }
            }
            else if (nRate >= M2Share.g_Config.nWinLottery2Min && nRate <= M2Share.g_Config.nWinLottery2Max)
            {
                if (M2Share.g_Config.nWinLotteryCount < M2Share.g_Config.nNoWinLotteryCount)
                {
                    nGold = M2Share.g_Config.nWinLottery2Gold;
                    nWinLevel = 2;
                    M2Share.g_Config.nWinLotteryLevel2++;
                }
            }
            else if (new ArrayList(new int[] { M2Share.g_Config.nWinLottery1Min + M2Share.g_Config.nWinLottery1Max }).Contains(nRate))
            {
                if (M2Share.g_Config.nWinLotteryCount < M2Share.g_Config.nNoWinLotteryCount)
                {
                    nGold = M2Share.g_Config.nWinLottery1Gold;
                    nWinLevel = 1;
                    M2Share.g_Config.nWinLotteryLevel1++;
                }
            }
            if (nGold > 0)
            {
                switch (nWinLevel)
                {
                    case 1:
                        SysMsg(M2Share.g_sWinLottery1Msg, MsgColor.Green, MsgType.Hint);
                        break;
                    case 2:
                        SysMsg(M2Share.g_sWinLottery2Msg, MsgColor.Green, MsgType.Hint);
                        break;
                    case 3:
                        SysMsg(M2Share.g_sWinLottery3Msg, MsgColor.Green, MsgType.Hint);
                        break;
                    case 4:
                        SysMsg(M2Share.g_sWinLottery4Msg, MsgColor.Green, MsgType.Hint);
                        break;
                    case 5:
                        SysMsg(M2Share.g_sWinLottery5Msg, MsgColor.Green, MsgType.Hint);
                        break;
                    case 6:
                        SysMsg(M2Share.g_sWinLottery6Msg, MsgColor.Green, MsgType.Hint);
                        break;
                }
                if (IncGold(nGold))
                {
                    GoldChanged();
                }
                else
                {
                    DropGoldDown(nGold, true, null, null);
                }
            }
            else
            {
                M2Share.g_Config.nNoWinLotteryCount += 500;
                SysMsg(M2Share.g_sNotWinLotteryMsg, MsgColor.Red, MsgType.Hint);
            }
        }

        public override void RecalcAbilitys()
        {
            base.RecalcAbilitys();
            if (m_btJob != 3)
                RecalcAdjusBonus();
        }

        protected override void QueueTimedAbilitySnapshotAfterRecalc()
        {
            SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, string.Empty);
        }

        protected override void UpdateVisibleGay(TBaseObject BaseObject)
        {
            var boIsVisible = false;
            TVisibleBaseObject VisibleBaseObject;
            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT || BaseObject.m_Master != null)
            {
                m_boIsVisibleActive = true;// 如果是人物或宝宝则置TRUE
            }
            for (var i = 0; i < m_VisibleActors.Count; i++)
            {
                VisibleBaseObject = m_VisibleActors[i];
                if (VisibleBaseObject.BaseObject == BaseObject)
                {
                    VisibleBaseObject.nVisibleFlag = 1;
                    boIsVisible = true;
                    break;
                }
            }
            if (boIsVisible)
            {
                return;
            }
            VisibleBaseObject = new TVisibleBaseObject
            {
                nVisibleFlag = 2,
                BaseObject = BaseObject
            };
            m_VisibleActors.Add(VisibleBaseObject);
            if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                SendWhisperMsg(BaseObject as TPlayObject);
            }
        }

        public override void SearchViewRange()
        {
            MapCellinfo MapCellInfo;
            TBaseObject BaseObject = null;
            Event MapEvent = null;
            var floorItemClearTimeout = ResolveFloorItemClearTimeout();
            for (var i = m_VisibleItems.Count - 1; i >= 0; i--)
            {
                m_VisibleItems[i].nVisibleFlag = 0;
            }
            for (var i = m_VisibleEvents.Count - 1; i >= 0; i--)
            {
                m_VisibleEvents[i].nVisibleFlag = 0;
            }
            for (var i = m_VisibleActors.Count - 1; i >= 0; i--)
            {
                m_VisibleActors[i].nVisibleFlag = 0;
            }
            var nStartX = m_nCurrX - m_nViewRange;
            var nEndX = m_nCurrX + m_nViewRange;
            var nStartY = m_nCurrY - m_nViewRange;
            var nEndY = m_nCurrY + m_nViewRange;
            try
            {
                for (var n20 = nStartX; n20 <= nEndX; n20++)
                {
                    for (var n1C = nStartY; n1C <= nEndY; n1C++)
                    {
                        var mapCell = false;
                        MapCellInfo = m_PEnvir.GetMapCellInfo(n20, n1C, ref mapCell);
                        if (mapCell && MapCellInfo.ObjList != null)
                        {
                            var nIdx = 0;
                            while (true)
                            {
                                if (MapCellInfo.Count <= nIdx)
                                {
                                    break;
                                }
                                var OSObject = MapCellInfo.ObjList[nIdx];
                                if (OSObject != null)
                                {
                                    if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                                    {
                                        // 本 override 正对原生 TEnvironment.DoPlayerSearchViewRange
                                        // (sub_77A178)，其唯一摘链谓词是 0x77A2EB call 0x765D64
                                        // (CName/PEnvir/PEnvir.MapName 三项合取)；原生无 60s 并联。
                                        if (IsNativeStaleCellActor(OSObject.CellObj))
                                        {
                                            Dispose(OSObject);
                                            MapCellInfo.Remove(nIdx);
                                            if (MapCellInfo.Count > 0)
                                            {
                                                continue;
                                            }
                                            m_PEnvir.ReleaseCellObjectList(n20, n1C);
                                            break;
                                        }
                                        BaseObject = (TBaseObject)OSObject.CellObj;
                                        if (BaseObject != null && !BaseObject.m_boInvisible)
                                        {
                                            if (!BaseObject.m_boGhost && !BaseObject.m_boFixedHideMode && !BaseObject.m_boObMode)
                                            {
                                                if (m_btRaceServer < Grobal2.RC_ANIMAL || m_Master != null || m_boCrazyMode || m_boNastyMode || m_boWantRefMsg || BaseObject.m_Master != null && Math.Abs(BaseObject.m_nCurrX - m_nCurrX) <= 3 && Math.Abs(BaseObject.m_nCurrY - m_nCurrY) <= 3 || BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                                {
                                                    UpdateVisibleGay(BaseObject);
                                                }
                                            }
                                        }
                                    }
                                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        if (OSObject.CellType == CellType.OS_ITEMOBJECT)
                                        {
                                            // 战神 sub_77A178 cell tag 2 @0x77A3D9:
                                            // `cmp edx,0x927C0 / jb keep` @0x77A3FD — one
                                            // flat 600_000 ms, no StdMode ladder.
                                            if (HasFloorItemExpired(OSObject.CellObj,
                                                    HUtil32.GetTickCount() - OSObject.dwAddTime,
                                                    floorItemClearTimeout))
                                            {
                                                Dispose(OSObject.CellObj);
                                                Dispose(OSObject);
                                                MapCellInfo.Remove(nIdx);
                                                if (MapCellInfo.Count > 0)
                                                {
                                                    continue;
                                                }
                                                m_PEnvir.ReleaseCellObjectList(n20, n1C);
                                                break;
                                            }
                                            var MapItem = (MapItem)OSObject.CellObj;
                                            UpdateVisibleItem(n20, n1C, MapItem);
                                            if (MapItem.OfBaseObject != null || MapItem.DropBaseObject != null)
                                            {
                                                if ((HUtil32.GetTickCount() - MapItem.CanPickUpTick) > M2Share.g_Config.dwFloorItemCanPickUpTime) 
                                                {
                                                    MapItem.OfBaseObject = null;
                                                    MapItem.DropBaseObject = null;
                                                }
                                                else
                                                {
                                                    // PKD-11 —— 归属人作废的判据是**幽灵**，不是死亡。
                                                    // 战神 sub_783988（地面物归属过期，唯一调用点
                                                    // 0x77A476，在地图格 tick 里）：
                                                    //   783988  cmp dword [item+0xF4],0 / 75 09 jne
                                                    //   783991  cmp dword [item+0xF8],0 / 74 4D je   ; 两槽都空 -> 无事
                                                    //   78399A  2B 50 08              sub edx,[item+8]  ; now - 落地tick
                                                    //   78399D  81 FA C0 D4 01 00     cmp edx,0x1D4C0   ; 120000 ms
                                                    //   7839A3  76 12                 jbe 0x7839B7      ; 严格 > 才清
                                                    //   7839A5  两槽同时清零                            ; -> 变成公共物
                                                    //   7839B7  mov edx,[item+0xF4] / test / je
                                                    //   7839C1  80 7A 73 00           cmp byte [edx+0x73],0
                                                    //   7839C5  74 08                 je 0x7839CF
                                                    //   7839C7  清 [item+0xF4]
                                                    //   7839CF  对 [item+0xF8] 重复同一段
                                                    // +0x73 是 m_boGhost（全镜像唯一写入点 0x7680EF，在
                                                    // MakeGhost sub_768060 里，且从不写 0）；m_boDeath 是
                                                    // +0x74（0x766323，TCreature.Die 的第一条语句）。
                                                    // C# 写成 m_boDeath 会把归属提前作废整整一个尸体周期：
                                                    // 击杀者一死，他脚下的战利品立刻变公共，旁边的人可以直接
                                                    // 捡走；原生要等他变成幽灵（或满 120 秒）才放开。
                                                    if (MapItem.OfBaseObject as TBaseObject != null)
                                                    {
                                                        if ((MapItem.OfBaseObject as TBaseObject).m_boGhost)
                                                        {
                                                            MapItem.OfBaseObject = null;    // 0x7839C7
                                                        }
                                                    }
                                                    if (MapItem.DropBaseObject as TBaseObject != null)
                                                    {
                                                        if ((MapItem.DropBaseObject as TBaseObject).m_boGhost)
                                                        {
                                                            MapItem.DropBaseObject = null;  // 0x7839DF
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        if (OSObject.CellType == CellType.OS_EVENTOBJECT)
                                        {
                                            MapEvent = (Event)OSObject.CellObj;
                                            if (MapEvent.m_boVisible)
                                            {
                                                UpdateVisibleEvent(n20, n1C, MapEvent);
                                            }
                                        }
                                    }
                                }
                                nIdx++;
                            }
                        }
                    }
                }
                var n18 = 0;
                while (true)
                {
                    if (m_VisibleActors.Count <= n18)
                    {
                        break;
                    }
                    var VisibleBaseObject = m_VisibleActors[n18];
                    if (VisibleBaseObject.nVisibleFlag == 0)
                    {
                        if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            BaseObject = VisibleBaseObject.BaseObject;
                            if (!BaseObject.m_boFixedHideMode && !BaseObject.m_boGhost)//防止人物退出时发送重复的消息占用带宽，人物进入隐身模式时人物不消失问题
                            {
                                SendMsg(BaseObject, Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
                            }
                        }
                        m_VisibleActors.RemoveAt(n18);
                        Dispose(VisibleBaseObject);
                        continue;
                    }
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT && VisibleBaseObject.nVisibleFlag == 2)
                    {
                        BaseObject = VisibleBaseObject.BaseObject;
                        if (BaseObject != this)
                        {
                            if (BaseObject.m_boDeath)
                            {
                                if (BaseObject.m_boSkeleton)
                                {
                                    SendMsg(BaseObject, Grobal2.RM_SKELETON, BaseObject.m_btDirection, BaseObject.m_nCurrX, BaseObject.m_nCurrY, 0, "");
                                }
                                else
                                {
                                    SendMsg(BaseObject, Grobal2.RM_DEATH, BaseObject.m_btDirection, BaseObject.m_nCurrX, BaseObject.m_nCurrY, 0, "");
                                }
                            }
                            else
                            {
                                SendMsg(BaseObject, Grobal2.RM_TURN, BaseObject.m_btDirection, BaseObject.m_nCurrX, BaseObject.m_nCurrY, 0, BaseObject.GetShowName());
                            }
                        }
                    }
                    n18++;
                }

                var I = 0;
                while (true)
                {
                    if (m_VisibleItems.Count <= I)
                    {
                        break;
                    }
                    var VisibleMapItem = m_VisibleItems[I];
                    if (VisibleMapItem.nVisibleFlag == 0)
                    {
                        SendMsg(this, Grobal2.RM_ITEMHIDE, 0, VisibleMapItem.MapItem.Id, VisibleMapItem.nX, VisibleMapItem.nY, "");
                        m_VisibleItems.RemoveAt(I);
                        Dispose(VisibleMapItem);
                        continue;
                    }
                    if (VisibleMapItem.nVisibleFlag == 2)
                    {
                        SendMsg(this, Grobal2.RM_ITEMSHOW, VisibleMapItem.wLooks, VisibleMapItem.MapItem.Id, VisibleMapItem.nX, VisibleMapItem.nY, VisibleMapItem.sName);
                    }
                    I++;
                }
                I = 0;
                while (true)
                {
                    if (m_VisibleEvents.Count <= I)
                    {
                        break;
                    }
                    MapEvent = m_VisibleEvents[I];
                    if (MapEvent.nVisibleFlag == 0)
                    {
                        SendMsg(this, Grobal2.RM_HIDEEVENT, 0, MapEvent.Id, MapEvent.m_nX, MapEvent.m_nY, "");
                        m_VisibleEvents.RemoveAt(I);
                        continue;
                    }
                    if (MapEvent.nVisibleFlag == 2)
                    {
                        SendMsg(this, Grobal2.RM_SHOWEVENT, (short)MapEvent.m_nEventType,
                            MapEvent.Id, MapEvent.m_nX, MapEvent.m_nY, "", MapEvent);
                    }
                    I++;
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(e.StackTrace);
                KickException();
            }
        }

        
        
        
        
        public override string GetShowName()
        {
            var result = string.Empty;
            string sShowName;
            var sCharName = string.Empty;
            var sGuildName = string.Empty;
            var sDearName = string.Empty;
            var sMasterName = string.Empty;
            const string sExceptionMsg = "[Exception] TPlayObject::GetShowName";
            try
            {
                if (m_MyGuild != null)
                {
                    var Castle = M2Share.CastleManager.IsCastleMember(this);
                    if (Castle != null)
                    {
                        sGuildName = M2Share.g_sCastleGuildName.Replace("%castlename", Castle.m_sName);
                        sGuildName = sGuildName.Replace("%guildname", m_MyGuild.sGuildName);
                        sGuildName = sGuildName.Replace("%rankname", m_sGuildRankName);
                    }
                    else
                    {
                        Castle = M2Share.CastleManager.InCastleWarArea(this);// 01/25 多城堡
                        // 眼神 行会显示 memcpys 90 90 over both skip-jumps of this branch
                        // (0x6C5BCB 74 49 and 0x6C5BF7 74 1D), leaving no path to 0x6C5C16.
                        if (new YanshenApi(this, null, M2Share.PluginManager).IsGuildShow()
                            || M2Share.g_Config.boShowGuildName || Castle != null && Castle.m_boUnderWar || m_boInFreePKArea)
                        {
                            sGuildName = M2Share.g_sNoCastleGuildName.Replace("%guildname", m_MyGuild.sGuildName);
                            sGuildName = sGuildName.Replace("%rankname", m_sGuildRankName);
                        }
                    }
                }
                if (!M2Share.g_Config.boShowRankLevelName)
                {
                    if (m_btReLevel > 0)
                    {
                        switch (m_btJob)
                        {
                            case M2Share.jWarr:
                                sCharName = M2Share.g_sWarrReNewName.Replace("%chrname", m_sCharName);
                                break;
                            case M2Share.jWizard:
                                sCharName = M2Share.g_sWizardReNewName.Replace("%chrname", m_sCharName);
                                break;
                            case M2Share.jTaos:
                                sCharName = M2Share.g_sTaosReNewName.Replace("%chrname", m_sCharName);
                                break;
                        }
                    }
                    else
                    {
                        sCharName = m_sCharName;
                    }
                }
                else
                {
                    sCharName = format(m_sRankLevelName, m_sCharName);
                }
                if (!string.IsNullOrEmpty(m_sMasterName))
                {
                    if (m_boMaster)
                    {
                        sMasterName = format(M2Share.g_sMasterName, m_sMasterName);
                    }
                    else
                    {
                        sMasterName = format(M2Share.g_sNoMasterName, m_sMasterName);
                    }
                }
                if (!string.IsNullOrEmpty(m_sDearName))
                {
                    if (m_btGender == PlayGender.Man)
                    {
                        sDearName = format(M2Share.g_sManDearName, m_sDearName);
                    }
                    else
                    {
                        sDearName = format(M2Share.g_sWoManDearName, m_sDearName);
                    }
                }
                sShowName = M2Share.g_sHumanShowName.Replace("%chrname", sCharName);
                sShowName = sShowName.Replace("%guildname", sGuildName);
                sShowName = sShowName.Replace("%dearname", sDearName);
                sShowName = sShowName.Replace("%mastername", sMasterName);
                result = sShowName;
                if (_yanshenTitleInfoSet && !string.IsNullOrEmpty(_yanshenTitleInfo))
                    result += "\\" + _yanshenTitleInfo;
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        public override void MakeGhost()
        {
            // PERF: diagnostic write removed
            string sSayMsg;
            TPlayObject Human;
            const string sExceptionMsg = "[Exception] TPlayObject::MakeGhost";
            try
            {
                // 「下线宝宝死亡」在这里没有原生对应物，不要再往 MakeGhost 加杀宠循环。
                // 整个功能只有一处补丁：0x006B5BA1 的 RM_10401 守卫
                //   0F 84 A5 06 00 00  je 0x006B624C   →   E9 A6 06 00 00 90  jmp + nop
                // （安装点 0x100AB10B，还原支 0x100AB19B 写回 0F 84 A5 06 00 00，
                //  开关字段 [edi+0xBF0] 在补丁函数 0x100A96C0 里只被读两次 ——
                //  0x100AB0AA 使能守卫与 0x100AB134 还原守卫）。
                // 也没有任何 trampoline 站点挂这个标签。所以原生的做法是**登录时
                // 不重建从宠**，而不是下线时把从宠打死；后者会多跑一遍死亡路径
                // （死亡动画、BB死亡触发、掉落），是 C# 自己加的。
                // 消费点在 TPlayObject.Message.cs 的 RM_10401 分支。
                M2Share.UserEngine?.RemoveHero(this);
                lock (M2Share.HighStatLock)
                {
                    if (M2Share.g_HighLevelHuman == this)
                    {
                        M2Share.g_HighLevelHuman = null;
                    }
                    if (M2Share.g_HighPKPointHuman == this)
                    {
                        M2Share.g_HighPKPointHuman = null;
                    }
                    if (M2Share.g_HighDCHuman == this)
                    {
                        M2Share.g_HighDCHuman = null;
                    }
                    if (M2Share.g_HighMCHuman == this)
                    {
                        M2Share.g_HighMCHuman = null;
                    }
                    if (M2Share.g_HighSCHuman == this)
                    {
                        M2Share.g_HighSCHuman = null;
                    }
                    if (M2Share.g_HighOnlineHuman == this)
                    {
                        M2Share.g_HighOnlineHuman = null;
                    }
                }
                
                if (m_DearHuman != null)
                {
                    if (m_btGender == PlayGender.Man)
                    {
                        sSayMsg = M2Share.g_sManLongOutDearOnlineMsg.Replace("%d", m_sDearName);
                        sSayMsg = sSayMsg.Replace("%s", m_sCharName);
                        sSayMsg = sSayMsg.Replace("%m", m_PEnvir.sMapDesc);
                        sSayMsg = sSayMsg.Replace("%x", m_nCurrX.ToString());
                        sSayMsg = sSayMsg.Replace("%y", m_nCurrY.ToString());
                        m_DearHuman.SysMsg(sSayMsg, MsgColor.Red, MsgType.Hint);
                    }
                    else
                    {
                        sSayMsg = M2Share.g_sWoManLongOutDearOnlineMsg.Replace("%d", m_sDearName);
                        sSayMsg = sSayMsg.Replace("%s", m_sCharName);
                        sSayMsg = sSayMsg.Replace("%m", m_PEnvir.sMapDesc);
                        sSayMsg = sSayMsg.Replace("%x", m_nCurrX.ToString());
                        sSayMsg = sSayMsg.Replace("%y", m_nCurrY.ToString());
                        m_DearHuman.SysMsg(sSayMsg, MsgColor.Red, MsgType.Hint);
                    }
                    m_DearHuman.m_DearHuman = null;
                    m_DearHuman = null;
                }
                if (m_MasterHuman != null || m_MasterList.Count > 0)
                {
                    if (m_boMaster)
                    {
                        for (var i = m_MasterList.Count - 1; i >= 0; i--)
                        {
                            Human = m_MasterList[i];
                            sSayMsg = M2Share.g_sMasterLongOutMasterListOnlineMsg.Replace("%s", m_sCharName);
                            sSayMsg = sSayMsg.Replace("%m", m_PEnvir.sMapDesc);
                            sSayMsg = sSayMsg.Replace("%x", m_nCurrX.ToString());
                            sSayMsg = sSayMsg.Replace("%y", m_nCurrY.ToString());
                            Human.SysMsg(sSayMsg, MsgColor.Red, MsgType.Hint);
                            Human.m_MasterHuman = null;
                        }
                    }
                    else if (m_MasterHuman != null)
                    {
                        sSayMsg = M2Share.g_sMasterListLongOutMasterOnlineMsg.Replace("%d", m_sMasterName);
                        sSayMsg = sSayMsg.Replace("%s", m_sCharName);
                        sSayMsg = sSayMsg.Replace("%m", m_PEnvir.sMapDesc);
                        sSayMsg = sSayMsg.Replace("%x", m_nCurrX.ToString());
                        sSayMsg = sSayMsg.Replace("%y", m_nCurrY.ToString());
                        m_MasterHuman.SysMsg(sSayMsg, MsgColor.Red, MsgType.Hint);
                        
                        if (m_MasterHuman.m_sMasterName == m_sCharName)
                        {
                            m_MasterHuman.m_MasterHuman = null;
                        }
                        for (var i = 0; i < m_MasterHuman.m_MasterList.Count; i++)
                        {
                            if (m_MasterHuman.m_MasterList[i] == this)
                            {
                                m_MasterHuman.m_MasterList.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
            base.MakeGhost();
        }

        protected override void ScatterBagItems(TBaseObject ItemOfCreat)
        {
            const int DropWide = 2;
            TUserItem pu;
            const string sExceptionMsg = "[Exception] TPlayObject::ScatterBagItems";
            IList<TDeleteItem> DelList = null;
            // 战神 sub_740078 的序言里没有任何早退。0x740078..0x7400D4 依次是
            //   740078  55 / 8B EC / 81 C4 24 FF FF FF    栈帧
            //   740081  53 56 57                          push ebx,esi,edi
            //   740084  33 D2 / 89 95 24 FF FF FF / 89 55 F4   两个局部清零
            //   74008F  8B F0                             esi := self
            //   740093  55 / 68 B4 02 74 00 / 64 FF 30 / 64 89 20   SEH 帧
            //   74009F  8D 85 28 FF FF FF / 33 C9 / BA C8 00 00 00 / E8 …  FillChar(200)
            //   7400B1  A1 AC 5F 7D 00 / 8B 00 / 3B 86 60 01 00 00 / 0F 9E 45 FF  红名判据
            //   7400C7  8B 86 08 05 00 00 / 8B 78 08 / 4F / 83 FF 00 / 0F 8C …
            // 整个函数的**第一条**条件跳转就是 0x7400D4 那句 `jl 0x740266`（背包为空），
            // 之前一个字节的早退都没有。上游 sub_741368 的策略梯（0x7413F6..0x741492）
            // 也只读六个地图旗标字节 [+0x5D] [+0x5E] [+0x76] [+0x77] [+0x8C] 与安全区，
            // 从不读任何玩家侧的「不掉落」布尔。
            // 原先这里的 `m_boAngryRing || m_boNoDropItem || Flag.boNODROPITEM` 早退
            // 在原生无对应，且全镜像多编码零命中（GBK / 裸 ASCII 大小写不敏感 /
            // UTF-16LE 三路皆 0）。按 §3.1 删除——原版就是不给这道保护。
            // 0x7400B1..0x7400BE: setle => PKPoint >= configured threshold.
            // The worker never reads boDieRedScatterBagAll or PKLevel().
            var boDropall = m_nPkPoint >= M2Share.g_Config.nPKPunishPoint;
            // 战神 sub_740078 @0x740140-0x740223 — the auth + gift DESTROY branch, absent
            // from C# until now.  Same three-part test as the manual drop:
            //   0x740140  cmp byte [esi+0x178],0 / jne 0x740225   ; non-player -> normal scatter
            //   0x740154  mov cl,4 / call sub_617A38              ; authenticated?
            //   0x74015D  test al,al / je 0x74016E                ; NOT authed -> destroy
            //   0x740161  cmp byte [ebx+0xD8],0 / je 0x740225     ; authed + not gift -> scatter
            //   0x74019D  call sub_424B30                         ; remove from bag
            //   0x740217  call sub_768BE0(dx=0x5E)                ; "未验证,物品消失(死亡)" / "赠品,…"
            //   0x74021E  call sub_404690                         ; Free — NEVER DropItemDown
            // This was the primary laundering route in the whole game: die on purpose,
            // alt picks the gift/unverified items up off the floor.
            var isPlayerRace = m_btRaceServer == Grobal2.RC_PLAYOBJECT;
            try
            {
                for (var i = m_ItemList.Count - 1; i >= 0; i--)
                {
                    // PKD-04 —— 抽签与门的顺序。战神 sub_740078 每件物品的真实顺序是
                    //   7400E9  80 BB FC 00 00 00 00  cmp byte [ebx+0xFC],0
                    //   7400F0  75 4E                 jne 0x740140   ; 必爆类 -> 跳过抽签与三道门
                    //   7400F2  80 7D FF 00           cmp byte [ebp-1],0
                    //   7400F6  75 12                 jne 0x74010A   ; 红名 -> 跳过抽签
                    //   7400F8  B8 03 00 00 00        mov eax,3
                    //   7400FD  E8 4A 3A CC FF        call sub_403B4C   ; Random(3)
                    //   740102  85 C0 / 0F 85 …       test eax,eax / jne 0x74025C  ; 非 0 -> 不掉
                    //   74010A  8B 43 1C / F6 40 02 10 / jne 0x74025C  ; Reserved02 & 0x0010
                    //   740117  8B 43 1C / F6 40 03 02 / jne 0x74025C  ; Reserved02 & 0x0200
                    //   740124  call sub_784720 (Reserved02 & 0x4000) / je 0x740140
                    //   74012F  call sub_784710 (绑定字 [item+0x34]) / cmp ax,1 / je 0x74025C
                    //   740140  ← 销毁/落地分流从这里才开始
                    // 也就是说销毁支**在抽签与三道门之后**。C# 之前把 ShouldDestroy 提到
                    // 循环第一句，于是 (a) 未验证/赠品被 100% 销毁而不是原生的 1/3，
                    // (b) 绑定物、Reserved02&0x0010/0x0200 的物品本来一件都不该动，
                    // 却照样被销毁 —— 两条都是净额外的玩家资产损失。
                    var bagItem = m_ItemList[i];
                    var bagStdItem = M2Share.UserEngine.GetStdItem(bagItem.wIndex);
                    if (bagItem.NativeClassFc == 0)
                    {
                        // 分母是**硬编码 3**：0x7400F8 `B8 03 00 00 00 mov eax,3` 紧接
                        // 0x7400FD `E8 4A 3A CC FF call sub_403B4C`，没有任何全局读。
                        // 这里原先读 g_Config.nDieScatterBagRate（默认也是 3，所以默认配置下
                        // 行为不变），但 DieScatterBagRate 这个名字在全镜像 GBK、裸 ASCII
                        // （大小写不敏感）、UTF-16LE 三路皆 0 命中，原生没有这个旋钮。
                        if (!boDropall
                            && M2Share.RandomNumber.Random(3) != 0)
                        {
                            continue;                               // 0x740104 jne 0x74025C
                        }
                        if (bagStdItem != null)
                        {
                            if ((bagStdItem.NativeReserved02 & 0x0010) != 0)
                            {
                                continue;                           // 0x740111 jne 0x74025C
                            }
                            if ((bagStdItem.NativeReserved02 & 0x0200) != 0)
                            {
                                continue;                           // 0x74011E jne 0x74025C
                            }
                            if ((bagStdItem.NativeReserved02 & 0x4000) != 0
                                && NativeItemAcquisitionStamp.ReadBindWord(bagItem) == 1)
                            {
                                continue;                           // 0x74013A je 0x74025C
                            }
                        }
                    }
                    // 0x740140: 分流点。
                    var scatterAuthenticated = NativeItemDropDestroyAuthenticated();
                    if (NativeItemDropDestroy.ShouldDestroy(isPlayerRace,
                            scatterAuthenticated, bagItem))
                    {
                        var destroyed = bagItem;
                        // 0x74016E..0x740182: the auth/gift destruction arm
                        // still runs sub_78389C mode 5. A non-zero result keeps
                        // the item in the bag and advances to the next slot.
                        if (NativeItemDropDestroy.CheckTransferPermission(destroyed,
                                bagStdItem,
                                NativeItemDropDestroy.TransferModeDrop) != 0)
                        {
                            continue;
                        }
                        var notice = NativeItemDropDestroy.BuildDestroyNotice(
                            NativeItemDropDestroyAuthenticated(), destroyed,
                            NativeItemDropDestroy.DeathBagUnverifiedNotice,
                            NativeItemDropDestroy.DeathBagGiftNotice);
                        if (isPlayerRace)
                        {
                            DelList ??= new List<TDeleteItem>();
                            DelList.Add(new TDeleteItem()
                            {
                                sItemName = M2Share.UserEngine.GetStdItemName(destroyed.wIndex),
                                MakeIndex = destroyed.MakeIndex,
                                ClientItemID = EnsureClientItemId(destroyed)
                            });
                        }
                        m_ItemList.RemoveAt(i);                 // 0x74019D
                        if (!string.IsNullOrEmpty(notice))
                        {
                            SysMsg(notice + " "                 // 0x740211 dx=0x5E
                                + M2Share.UserEngine.GetStdItemName(destroyed.wIndex),
                                MsgColor.Red, MsgType.Hint);
                        }
                        Dispose(destroyed);                     // 0x74021E sub_404690
                        continue;                               // 0x740223 jmp 0x74025C
                    }
                    // 0x740225: 落地支。抽签与三道门已在上面走过，这里不再重抽。
                    if (DropItemDown(m_ItemList[i], DropWide, true, ItemOfCreat, this))
                    {
                        pu = m_ItemList[i];
                        if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            if (DelList == null)
                            {
                                DelList = new List<TDeleteItem>();
                            }
                            DelList.Add(new TDeleteItem()
                            {
                                sItemName = M2Share.UserEngine.GetStdItemName(pu.wIndex),
                                MakeIndex = pu.MakeIndex,
                                ClientItemID = EnsureClientItemId(pu)
                            });
                        }
                        Dispose(m_ItemList[i]);
                        m_ItemList.RemoveAt(i);
                    }
                }
                if (DelList != null)
                {
                    SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                        DelList.Count, 0, 0, "", DelList);
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
        }

    }
}
