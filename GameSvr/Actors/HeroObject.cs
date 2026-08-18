using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Hero system (THeroAct in Delphi). A player-owned companion that follows the master,
    /// attacks the master's target, and has its own magic list, bag, and equipment.
    /// Handles all 43 SM_HERO_* and 22 CM_HERO_* protocol messages.
    /// </summary>
    public partial class HeroObject : AnimalObject
    {
        /// <summary>Character name of the master player.</summary>
        public string MasterName;

        /// <summary>Heroes descend from THumanKind too, so they own a cooldown
        /// table: THeroAct VMT 0x685630+0x1F0 and all six hero leaves hold the
        /// same sub_748130. Their notifications reach the client indirectly --
        /// VMT+0x254 is sub_689A38, which forwards to the master at obj+0x68C.
        /// (Monsters do NOT have a table: TCreature 0x764608+0x1F0 is a
        /// different function, sub_773CA0.)</summary>
        internal override bool SupportsNativeColdTime => true;

        /// <summary>
        /// 0x73FCEF 的 `is THumanKind`（类指针 [0x73BBE8]）。英雄同样是 THumanKind 的一支，
        /// 所以英雄当凶手时，它的 [+0x579] 一样会从受害者的爆装分母里减掉。
        /// </summary>
        internal override bool IsNativeHumanKind() => true;

        /// <summary>Hero's current level (mirrors or derives from master).</summary>
        public ushort HeroLevel;

        public byte HeroType { get; internal set; }

        public byte HeroRank { get; internal set; }

        public int m_nForceExp;
        public int m_nMaxForceExp;
        public int m_nForceLv;
        internal int m_nNativeCombinedFealty;
        internal int m_nNativeFealtyBonus;
        internal byte m_btNativeUnionState;
        internal ushort m_wNativeUnionEnergy;
        internal ushort m_wNativeUnionChargeTier;
        public bool m_boNativeCommonInformationOption1;
        public int m_nNativeCommonInformationOption2;
        public bool m_boNativeCommonInformationOption3;

        private int m_dwNativeUnionChargeTick;
        private int m_dwNativeUnionProcessTick;
        private TUserMagic m_NativeUnionMagic;
        private static readonly byte[] NativeUnionTierBonus = { 0, 1, 1, 2, 2, 2 };
        // 出厂系数表现在与插件覆盖同源，避免两处各写一份 11 个 f64。
        private static readonly double[] NativeUnionDamageMultiplier =
            Plugins.YanshenComboTables.WizTaoStock;
        private static readonly int[] NativeUnionDamageBonus =
        {
            0, 0, 0, 0, 0, 6000, 8000, 10000, 12000, 14000, 21000
        };
        private static readonly ushort[,] NativeUnionMagicByHeroAndMasterJob =
        {
            { 50, 52, 51, 300 },
            { 52, 55, 54, 301 },
            { 51, 54, 53, 302 }
        };
        private const string NativeUnionSkillBookReward = "\u706b\u9f99\u4e4b\u5fc3";

        internal byte NativeRace { get; set; }

        public NativeHeroRuntimeState NativeHeroState { get; internal set; }

        protected override ReadOnlySpan<byte> GetNativeFixedAbilityRecord()
        {
            byte[] fixedRecord = NativeHeroState?.FixedRecord;
            return fixedRecord != null &&
                fixedRecord.Length == NativeHeroDbFrameCodec.HeroRecordSize
                ? fixedRecord
                : ReadOnlySpan<byte>.Empty;
        }

        /// <summary>Skills the hero has learned.</summary>
        public IList<TUserMagic> m_HeroMagicList;

        /// <summary>Tick for periodic master search/view refresh.</summary>
        private int m_dwSearchMasterTick;

        /// <summary>Tick for hero skill casting cooldown.</summary>
        private int m_dwHeroMagicTick;

        private int m_dwNativeHealthSpellDirtyTick;

        /// <summary>
        /// 原版跟随锚 <c>[hero+0x63C]/[+0x640]</c>。sub_68BAD4 @0x68BB6F 与主人当前格比较，
        /// 变了才写回并 <c>call 0x68B838</c>。
        /// </summary>
        private int m_nNativeGuardAnchorX;
        private int m_nNativeGuardAnchorY;

        /// <summary>
        /// 原版英雄模式字节 <c>[hero+0x6A1]</c>。三值循环 0/1/2，由 sub_688650 维护：
        /// 6886AE-6886BE  <c>mov al,[ebx+0x6A1]; inc eax; mov ecx,3; div ecx; mov [ebx+0x6A1],dl</c>
        /// 名称取自表 0x7D32FC（GBK，NUL 结尾）：[0]=0x6862CC 攻击 [1]=0x6862DC 跟随 [2]=0x6862EC 休息。
        /// </summary>
        internal enum NativeHeroMode : byte
        {
            /// <summary>攻击 — 唯一会跑战斗分支的模式（Run 0x68A1F5 <c>cmp byte [eax+0x6A1],0 / jne</c>）。</summary>
            Attack = 0,

            /// <summary>跟随 — 构造函数默认值（ctor 0x6865A6 <c>mov byte [edi+0x6A1],1</c>）。</summary>
            Follow = 1,

            /// <summary>休息 — Run 0x68A4DA <c>cmp byte [eax+0x6A1],2</c>，清目标后只跟地图比较。</summary>
            Rest = 2
        }

        /// <summary>
        /// 原版 <c>[hero+0x6A1]</c>。默认 1=跟随，由 THeroAct 构造函数 0x6865A6 播种。
        /// </summary>
        internal NativeHeroMode m_btNativeHeroMode;

        public HeroObject() : base()
        {
            m_UseItems = new TUserItem[NativeHeroDbFrameCodec.EquippedItemCount];
            m_HeroMagicList = m_MagicList;
            m_dwSearchMasterTick = HUtil32.GetTickCount();
            m_dwHeroMagicTick = HUtil32.GetTickCount();
            m_dwNativeHealthSpellDirtyTick = HUtil32.GetTickCount();
            m_dwNativeWarCrossMoonReadyTick = HUtil32.GetTickCount();
            // 原版 ctor sub_6864C4 @0x6865A6: mov byte [edi+0x6A1], 1 → 英雄出生即"跟随"。
            m_btNativeHeroMode = NativeHeroMode.Follow;
            m_btRaceServer = Grobal2.RC_HEROOBJECT;
            m_boWantRefMsg = true;
            m_nRunTime = 50;
            // 原版 ctor sub_6864C4 @0x68650E: mov dword [edi+0x7C], 0x3E8 → 搜索间隔 1000ms。
            m_dwSearchTime = 1000;
            // 原版 ctor sub_6864C4 @0x68659F: mov dword [edi+0x78], 0xB → 视野 11（不是 10）。
            // [+0x78] 即视野半径：sub_765DEC @0x765E3E `mov eax,[ebx+0x78]; push eax` 作范围入参。
            m_nViewRange = 11;
            // 原版 ctor sub_6864C4 @0x686515: mov dword [edi+0x320], 0x3E8 → 攻击间隔 1000ms。
            // [+0x320] 是"命中间隔"而非步行间隔:三个职业子类都拿它和命中戳 [+0x35C] 相比——
            // TWarHero  sub_693090 @0x6930B0/0x6930B6、TTaosHero sub_69422C @0x6942B8/0x6942BE、
            // TMagHero  sub_694FE8 @0x695037/0x69503D。步行间隔是 [+0x324](@0x6864FD=600)。
            m_nNextHitTime = 1000;
            m_boCanReAlive = false;
            InitializeNativeForceState();
        }

        internal void InitializeNativeForceState()
        {
            m_nMaxForceExp = NativeHeroForceTable.GetThreshold(m_nForceLv);
        }

        internal void RefreshNativeForceState()
        {
            m_nMaxForceExp = NativeHeroForceTable.GetThreshold(m_nForceLv);
            var master = m_Master as TPlayObject;
            if (master != null)
                m_nNativeFealtyBonus = NativeHeroFealtyTable.GetBonus(
                    master.m_nNativeHeroIntimacyCurrent);
            var combinedFealty = unchecked(m_nForceLv + m_nNativeFealtyBonus);
            if (combinedFealty != m_nNativeCombinedFealty)
            {
                m_nNativeCombinedFealty = combinedFealty;
                if (master != null)
                    master.QueueNativeGloryFealty(combinedFealty);
            }

            RefreshNativePrimaryMagicLevel(master, combinedFealty);

            // Native virtual +0x44 updates an unmapped linked object after this notification.
        }

        private void RefreshNativePrimaryMagicLevel(TPlayObject master,
            int combinedFealty)
        {
            if (m_Abil.Level < 44 || m_HeroMagicList == null)
                return;

            var primaryMagicId = m_btJob switch
            {
                0 => SpellsDef.SKILL_FIRESWORD,
                1 => SpellsDef.SKILL_WINDTEBO,
                2 => SpellsDef.SKILL_FIRECHARM,
                _ => -1
            };
            if (primaryMagicId < 0)
                return;

            TUserMagic primaryMagic = null;
            foreach (var magic in m_HeroMagicList)
            {
                if (magic?.MagicInfo?.wMagicID == primaryMagicId)
                {
                    primaryMagic = magic;
                    break;
                }
            }
            if (primaryMagic == null)
                return;

            byte targetLevel;
            string hint;
            if (combinedFealty >= 3000 && primaryMagic.btLevel == 3)
            {
                targetLevel = 4;
                hint = $"由于你们亲密的关系，您的英雄已经领悟了4级{primaryMagic.MagicInfo.sMagicName}";
            }
            else if (combinedFealty < 1000 && primaryMagic.btLevel == 4)
            {
                targetLevel = 3;
                hint = $"由于英雄的忠诚度下降超出限制，您的4级{primaryMagic.MagicInfo.sMagicName}下降到了3级";
            }
            else
            {
                return;
            }

            primaryMagic.btLevel = targetLevel;
            SendMsg(this, Grobal2.RM_MAGIC_LVEXP, 0,
                primaryMagic.MagicInfo.wMagicID, targetLevel,
                primaryMagic.nTranPoint, string.Empty);
            master?.SysMsg(hint, MsgColor.Red, MsgType.Hint);
        }

        // THeroAct 的 mover 与 THumanKind / TPlayer 同槽 0x741224（人形），不是 0x71F0F4（怪物）——
        // MOVE-40 的 VMT 普查逐列列出。英雄在 C# 里挂在 AnimalObject 之下，
        // 不 override 就会继承怪物的松边界，故把人形边界取回（0x741276 jle、0x741284 jge）。
        // 注意 TFieldHero 是另一回事：它在普查里走 0x71F0F4，沿用父类的怪物边界即忠实。
        protected override bool WalkToInBounds(short nNX, short nNY)
        {
            return nNX > 0 && nNX < m_PEnvir.wWidth
                && nNY > 0 && nNY < m_PEnvir.wHeight;
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            if (ProcessMsg.wIdent == Grobal2.RM_SYSMESSAGE ||
                ProcessMsg.wIdent == Grobal2.RM_NATIVE_REVIVE_MESSAGE)
            {
                ForwardNativeTextMessage(ProcessMsg);
                return true;
            }
            if (ProcessMsg.wIdent == Grobal2.RM_NATIVE_LOGON_STATE_SYNC)
            {
                SendNativeHeroLogonStateSync();
                return true;
            }
            if (ProcessMsg.wIdent == Grobal2.RM_10401)
            {
                if (ProcessMsg.Payload is TSlaveInfo slaveInfo)
                    RestoreNativeHeroSummon(slaveInfo);
                return true;
            }
            if (ProcessMsg.wIdent == Grobal2.RM_NATIVE_EXP_CONTINUE)
            {
                if (m_Master is TPlayObject master)
                {
                    master.GrantNativeHeroExperience(this, ProcessMsg.nParam1,
                        ProcessMsg.nParam3 != 0, ProcessMsg.nParam2 != 0);
                }
                return true;
            }
            if (ProcessMsg.wIdent == Grobal2.RM_HEALTHSPELLCHANGED)
            {
                SendNativeHealthSpellChanged(ProcessMsg.BaseObject);
                return true;
            }
            if (ProcessMsg.wIdent == Grobal2.RM_ABILITY)
            {
                SendHeroAbility();
                return true;
            }
            if (ProcessMsg.wIdent == Grobal2.RM_SENDDELITEMLIST)
            {
                // THeroAct dispatcher 0x6896ED..0x689708 forwards the RM buffer
                // unchanged as SM_HERO_DELITEMS. The producer stores count consecutive
                // item+0x18 ClientItemID dwords, with no trailing terminator.
                if (ProcessMsg.Payload is byte[] body)
                {
                    var frame = BuildSm917(ProcessMsg.nParam1, body);
                    // sub_689A38 checks the bound pointer and owner+0x73 ghost;
                    // owner+0x74 death does not suppress this queued deletion batch.
                    if (m_Master is TPlayObject master && !master.m_boGhost)
                        master.SendSocket(frame.Header, frame.Body);
                }
                return true;
            }

            var packet = BuildHeroRuntimePacket(ProcessMsg, m_Abil.Exp, m_Abil.Level);
            if (packet == null)
                return base.Operate(ProcessMsg);

            if (ProcessMsg.Payload is byte[] rawBody)
            {
                var master = FindMaster();
                master?.SendSocket(packet, rawBody);
            }
            else
            {
                SendToMaster((short)packet.Ident, packet.Recog, packet.Param, packet.Tag, packet.Series, "");
            }
            if (ProcessMsg.wIdent == Grobal2.RM_LEVELUP)
                SendHeroAbility();
            return true;
        }

        private void ForwardNativeTextMessage(TProcessMessage processMsg)
        {
            // THeroAct queues on the hero first. Its dispatcher then checks only
            // the bound master's ghost flag, prefixes the text and forwards it.
            if (!(m_Master is TPlayObject master) || master.m_boGhost)
                return;

            var text = "(英雄) " + (processMsg.sMsg ?? string.Empty);
            ClientPacket packet;
            if (processMsg.wIdent == Grobal2.RM_SYSMESSAGE)
            {
                packet = Grobal2.MakeDefaultMsg(Grobal2.SM_SYSMESSAGE,
                    processMsg.BaseObject,
                    processMsg.Payload is byte[]
                        ? processMsg.wParam
                        : HUtil32.MakeWord(processMsg.nParam1, processMsg.nParam2),
                    0, 1);
            }
            else
            {
                packet = Grobal2.MakeDefaultMsg(Grobal2.SM_REVIVE_MESSAGE,
                    processMsg.BaseObject, processMsg.wParam, 0, 0);
            }
            master.SendSocket(packet, BuildNativeTerminatedTextBody(text));
        }

        protected override void QueueTimedAbilitySnapshotAfterRecalc()
        {
            SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, string.Empty);
        }

        public override void Initialize()
        {
            var currentHp = m_WAbil.HP;
            var currentMp = m_WAbil.MP;
            base.Initialize();
            m_WAbil.HP = currentHp;
            m_WAbil.MP = currentMp;
            RecalcAbilitys();
        }

        internal static ClientPacket BuildHeroRuntimePacket(TProcessMessage processMsg, int currentExp, int level)
        {
            switch (processMsg.wIdent)
            {
                case Grobal2.RM_LEVELUP:
                    return Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_LEVELUP, currentExp, level,
                        GetHeroBagCapacity(level), 0);
                case Grobal2.RM_WINEXP:
                    return Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_WINEXP, currentExp,
                        HUtil32.LoWord(processMsg.nParam1), HUtil32.HiWord(processMsg.nParam1), 0);
                case Grobal2.RM_MAGIC_LVEXP:
                    return Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_MAGIC_LVEXP, processMsg.nParam1,
                        processMsg.nParam2, HUtil32.LoWord(processMsg.nParam3),
                        HUtil32.HiWord(processMsg.nParam3));
                case Grobal2.RM_DURACHANGE:
                    return Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_DURACHANGE, processMsg.nParam1,
                        processMsg.wParam, HUtil32.LoWord(processMsg.nParam2),
                        HUtil32.HiWord(processMsg.nParam2));
                case Grobal2.RM_HERO_UNIONSTATUS:
                    return BuildNativeUnionStatusPacket((ushort)processMsg.wParam,
                        (byte)processMsg.nParam1,
                        M2Share.g_Config?.nHeroUnionMaxEnergy ?? 200);
                default:
                    return null;
            }
        }

        internal static ClientPacket BuildNativeUnionStatusPacket(ushort energy,
            byte state, int maximumEnergy)
        {
            return Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_UNIONSTATUS,
                energy, state, maximumEnergy, 0);
        }

        internal static bool IsNativeUnionMagicId(ushort magicId)
        {
            return magicId >= 50 && magicId <= 55 ||
                   magicId >= 300 && magicId <= 302;
        }

        /// <summary>
        /// Basic hero AI: follow master, attack master's target, auto-use skills.
        /// Mirrors the Delphi THeroAct.Run loop (<c>sub_689FDC</c>).
        /// </summary>
        public override void Run()
        {
            base.Run();

            PollNativeBurstStateExpiry();

            var dwCurTick = HUtil32.GetTickCount();
            RunNativeHealthSpellDirty(dwCurTick);

            // 原版 Run 的门序（sub_689FDC 0x689FFE-0x68A046）必须照抄，尤其是
            // 【回收门在"取主人"之前】这一点：
            //   0x689FFE  cmp byte [eax+0x73],0  -> self.m_boGhost，跳 0x68A5B5(继承 Run)
            //   0x68A00B  cmp dword [eax+0x68C],0 -> 英雄主人【指针】为空，同样跳 0x68A5B5
            //   0x68A018  回收门（自己尸体满 60s ‖ 主人 ghost）
            //   0x68A076  正常 AI
            // 注意 0x68A00B 判的是【原始绑定指针】,不是"能不能作为 AI 主人用"。
            // C# 的 FindMaster() 会把 ghost/死亡的主人过滤成 null —— 若先调它再回收,
            // 主人 ghost 这条最重要的回收路径永远走不到（2026-08-04 自查发现: 反射直调
            // RunNativeMasterGoneReap 的审计全绿,但经 Run() 实际永不触发）。
            if (m_boGhost)
                return;

            if (m_Master == null)
            {
                // 0x68A012: 主人指针为空只是跳过英雄 AI 段，不是死亡路径。
                return;
            }

            // 0x68A018-0x68A071: 回收门，用原始绑定指针判定。
            RunNativeMasterGoneReap(dwCurTick);
            if (m_boGhost)
                return;

            if (m_boDeath)
            {
                // 尸体尚未到 60s：原版此时已在回收门内 return（0x68A063 jne 0x68A5BD），
                // 不进战斗分支。
                return;
            }

            ProcessNativeUnionState(dwCurTick);
            if (m_btJob == 0)
                ProcessNativeWarCrossMoonSelectionExpiry(
                    HUtil32.GetTickCount());

            // Find master（AI 用：需要一个活着、在场的主人）
            var master = FindMaster();
            if (master == null)
                return;

            // Native THeroAct.Run processes the 500ms union state pulse before
            // entering the profession-specific action routine.
            TryReleaseNativeUnionMagic(master);

            // 0x68A1F5 `cmp byte [eax+0x6A1],0 / jne 0x68A4CF`：非攻击先清目标。
            // 休息 0x68A4DA `cmp byte [eax+0x6A1],2` 再比主人/自己 [+0x128] 地图指针，
            // `setne bl` 后只有地图不同才 `call 0x68BAD4`。跟随模式 bl 保持 1，必跟。
            // 攻击模式走职业 AttackTarget，无目标时仍走跟随（尾部 0x68A524）。
            if (m_btNativeHeroMode != NativeHeroMode.Attack)
            {
                ClearNativeHeroTargets();
                var restNeedsFollow = m_btNativeHeroMode == NativeHeroMode.Rest
                    && !ReferenceEquals(master.m_PEnvir, m_PEnvir);
                if (m_btNativeHeroMode == NativeHeroMode.Follow || restNeedsFollow)
                    FollowMasterNative(master);
                return;
            }

            RefreshNativeHeroGuildWarAttackFlag(master);

            if (master.m_TargetCret != null && !master.m_TargetCret.m_boDeath)
                SetTargetCreat(master.m_TargetCret);

            if (m_TargetCret != null && !m_TargetCret.m_boDeath)
                AttackTarget(master, dwCurTick);
            else
                FollowMasterNative(master);
        }

        /// <summary>
        /// 原版 <c>sub_6885DC</c>（英雄清目标）。逐字节 0x6885DC-0x68860C：
        /// <code>
        /// 6885DC  mov byte  [eax+0x6CA], 0     ; 守护标志
        /// 6885E3  mov byte  [eax+0x6CB], 0
        /// 6885EC  mov dword [eax+0x344], 0     ; 自己的目标
        /// 6885F4  mov dword [eax+0x67C], 0
        /// 6885FA  mov edx,[eax+0x6C4] / test edx,edx / je ret
        /// 688606  mov dword [edx+0x344], 0     ; 联动英雄的目标
        /// </code>
        /// 它**不**清主人的目标（那只在 sub_688650 模式切换尾巴里发生）。
        /// [+0x6CA]/[+0x67C]/[+0x6C4] 在 C# 端尚无字段映射；[+0x6CB] 见 NativeSafeZoneGate.cs。
        /// </summary>
        private void ClearNativeHeroTargets()
        {
            m_boNativeHeroGuildWarAttack = false;
            m_TargetCret = null;
        }

        /// <summary>
        /// 原版 THeroAct.Run 的英雄回收门（<c>sub_689FDC</c> 0x68A018-0x68A071）。逐字节：
        /// <code>
        /// 68A01B  call sub_772DA8            ; al = self.m_boDeath   ([obj+0x74])
        /// 68A020  test al,al / je 0x68A039   ; 活着 -> 只查主人
        /// 68A024  call GetTickCount
        /// 68A02C  sub  eax,[edx+0x330]      ; tick - self.m_dwDeathTick
        /// 68A032  cmp  eax,0xEA60           ; 60000
        /// 68A037  jae  0x68A048             ; 尸体超 60 秒 -> 回收
        /// 68A039  mov  eax,[self+0x68C]     ; 主人
        /// 68A042  cmp  byte [eax+0x73],0    ; 主人 m_boGhost
        /// 68A046  je   0x68A076             ; 主人还在 -> 正常 AI
        /// ; --- 回收 ---
        /// 68A051  cmp byte [master+0x73],0
        /// 68A055  jne 0x68A05C              ; 主人已 ghost -> 跳过通知
        /// 68A057  call sub_6CCA1C           ; 通知主人
        /// 68A05F  cmp byte [self+0x73],0 / jne 返回   ; 已 ghost 就别重复
        /// 68A06C  call sub_768060           ; TCreature.MarkDelete = 变 ghost + 下地图
        /// 68A071  jmp 收尾                   ; 注意:此路径不再跑继承 Run
        /// </code>
        /// 两点与发现文档相反：60 秒是英雄**自己的尸体**超时（`[+0x74]`=m_boDeath、
        /// `[+0x330]`=m_dwDeathTick），不是"主人死后宽限"；主人侧判的是 **ghost**
        /// (`[+0x73]`) 而非死亡，且**无任何延时**。动作是 MarkDelete(变 ghost)，不是 Die。
        /// </summary>
        private void RunNativeMasterGoneReap(int dwCurTick)
        {
            if (m_boGhost)
                return;

            // 68A01B-68A037: 自己是尸体且已超 60 秒 -> 回收。
            var boSelfCorpseExpired = m_boDeath &&
                unchecked((uint)(dwCurTick - m_dwDeathTick)) >= 60000u;

            // 68A039-68A046: 主人不在了（ghost / 掉线）-> 立即回收，无延时。
            var owner = m_Master;
            var boOwnerGone = owner == null || owner.m_boGhost;

            if (!boSelfCorpseExpired && !boOwnerGone)
                return;

            // 68A051-68A057: 主人还在（未 ghost）时才通知它，`call sub_6CCA1C` 的 eax
            // 就是 [hero+0x68C]（0x68A04B `mov eax,[eax+0x68C]`），所以 sub_6CCA1C 是
            // **主人**的方法。逐字节：
            //   6CCA27  8B 98 B0 0B 00 00     mov  ebx,[master+0xBB0]   ; 英雄
            //   6CCA2D  85 DB / 0F 84 ..      test ebx,ebx / je  ret    ; 无英雄直接返回
            //   6CCA35  80 7B 73 00 / 0F 85   cmp  byte [hero+0x73],0 / jne ret ; 已 ghost 返回
            //   6CCA41  E8 62 63 0A 00        call 0x772DA8             ; al = [hero+0x74] m_boDeath
            //   6CCA48  75 7B                 jne  0x6CCAC5             ; 尸体 -> 跳过消失广播
            //   6CCA6C  66 BA 70 29           mov  dx,0x2970 -> [vmt+0xD8]，参数是英雄格 [+0x12C]/[+0x130]
            //   6CCAC5  E8 8D B5 09 00        call 0x768060             ; MarkDelete(英雄)
            //   6CCAD6  C6 80 C8 04 00 00 00  mov  byte [master+0x4C8],0
            //   6CCAE7  66 BA 96 03           mov  dx,0x396  = SM_HERO_LOGOUT(918)
            //   6CCAF0  FF 93 50 02 00 00     call [master_vmt+0x250]   ; 单播发送槽 sub_6D7CB0
            //   6CCAF7  E8 AC FE FF FF        call 0x6CC9A8             ; DB 存档请求 0x194
            //   6CCB02  89 90 B0 0B 00 00     mov  [master+0xBB0],edx(=0)
            // 也就是说：尸体满 60 秒这条路径**必须**清掉主人的英雄指针、下发
            // SM_HERO_LOGOUT、并落一次存档。此前 C# 只 MakeGhost()，主人的
            // m_HeroObject 永远非空，CM_HERO_LOGON 的 `m_HeroObject == null` 门就再也
            // 打不开——英雄到下线为止无法重新召唤，客户端还留着英雄面板。
            // RemoveHero(owner) -> QueueHeroForFreeLocked 覆盖 0x6CCAC5/0x6CCB02 三件事
            // （QueueSave = 0x194、owner.m_HeroObject = null、MakeGhost = 0x768060）。
            // 仍未映射：0x2970 消失广播（尸体路径上原版本来就跳过）、[master+0x4C8]、
            // 英雄自己的联动对象 [hero+0x6C4]。
            var master = owner as TPlayObject;
            if (master != null && !master.m_boGhost)
            {
                if (M2Share.UserEngine?.RemoveHero(master) == true)
                    master.SendDefMessage(Grobal2.SM_HERO_LOGOUT, 0, 0, 0, 0, "");
            }

            // 68A05F-68A06C: MarkDelete —— 变 ghost 并从地图摘除，而非走 Die()。
            if (!m_boGhost)
                MakeGhost();
        }

        private void RunNativeHealthSpellDirty(int currentTick)
        {
            if (m_boDeath || m_WAbil.HP <= 0)
                return;

            if (unchecked((uint)(currentTick - m_dwNativeHealthSpellDirtyTick)) <= 500u)
                return;

            m_dwNativeHealthSpellDirtyTick = currentTick;
            if (!m_boNativeHealthSpellDirty)
                return;

            m_boNativeHealthSpellDirty = false;
            SendMsg(this, Grobal2.RM_HEALTHSPELLCHANGED,
                0, 0, 0, 0, string.Empty);
        }

        private void SendNativeHealthSpellChanged(int sourceObjectId)
        {
            if (!(m_Master is TPlayObject master))
                return;

            var body = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, 4), m_WAbil.HP);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), m_WAbil.MaxHP);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, 4), m_WAbil.MP);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12, 4), m_WAbil.MaxMP);
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_HEALTHSPELLCHANGED,
                sourceObjectId, HUtil32.LoWord(m_WAbil.HP),
                HUtil32.LoWord(m_WAbil.MP), HUtil32.LoWord(m_WAbil.MaxHP));
            master.SendSocket(header, body);
        }

        private TPlayObject FindMaster()
        {
            if (m_Master is TPlayObject boundMaster)
            {
                return boundMaster.m_boDeath || boundMaster.m_boGhost ? null : boundMaster;
            }

            if (string.IsNullOrEmpty(MasterName))
                return null;

            var master = M2Share.UserEngine.GetPlayObject(MasterName);
            if (master == null || master.m_boDeath || master.m_boGhost)
                return null;

            return master;
        }

        public override void MakeGhost()
        {
            if (m_boGhost)
                return;

            var owner = m_Master;
            m_Master = null;
            try
            {
                base.MakeGhost();
            }
            finally
            {
                m_Master = owner;
            }
        }

        internal void ReleaseRuntimeReferences()
        {
            ClearTimedAbilitiesOnExit();
            m_Master = null;
            DelTargetCreat();
            m_LastHiter = null;
            m_ExpHitter = null;
            m_MsgList.Clear();
            m_VisibleHumanList.Clear();
            m_VisibleActors.Clear();
            m_VisibleItems.Clear();
            m_VisibleEvents.Clear();
        }

        /// <summary>
        /// 原版格子距离 <c>sub_76B4A4</c> = 切比雪夫 <c>max(|dx|,|dy|)</c>，不是曼哈顿和。
        /// 逐字节：
        /// <code>
        /// 76B4AA  mov eax,[ebx+0x12C]; sub eax,esi; cdq; xor eax,edx; sub eax,edx   ; abs dx
        /// 76B4B9  mov eax,[ebx+0x130]; sub eax,ecx; cdq; xor eax,edx; sub eax,edx   ; abs dy
        /// 76B4C6  cmp eax,esi; jge +2; mov eax,esi                                  ; 取较大者
        /// </code>
        /// 英雄 Run 里每一处距离判定都走它：0x68A16E / 0x68A31E / 0x68A4A0 /
        /// 0x68A660 / 0x68A6BC / 0x68BB35（跟随），以及职业子类 0x6930DD / 0x692466。
        /// </summary>
        private static int NativeGridDistance(int x1, int y1, int x2, int y2)
        {
            return Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
        }

        /// <summary>
        /// 原版 <c>sub_68BAD4</c> 跟随。已落地的门：
        /// 地图指针不同 → SpaceMove；切比雪夫 <c>0x68BB49 83 FF 0C / 7C</c> 即 ≥12 过远；
        /// <c>0x68BB54 83 FF 03 / 7D</c> 即 &lt;3 贴身（已在主人格则停）；
        /// 锚 <c>+0x63C/+0x640</c> 随主人格更新。
        /// 附近 5×4 搜格 <c>0x777EF8</c> 与 VMT+0xBC 门未跟完，不臆造，同图只走 <c>GotoTargetXY</c>。
        /// </summary>
        private void FollowMasterNative(TPlayObject master)
        {
            if (master == null || master.m_PEnvir == null)
                return;

            if (!ReferenceEquals(master.m_PEnvir, m_PEnvir))
            {
                SpaceMove(master.m_PEnvir, master.m_nCurrX, master.m_nCurrY, 0);
                m_nNativeGuardAnchorX = master.m_nCurrX;
                m_nNativeGuardAnchorY = master.m_nCurrY;
                return;
            }

            var dist = NativeGridDistance(m_nCurrX, m_nCurrY, master.m_nCurrX, master.m_nCurrY);
            if (dist < 3
                && m_nCurrX == master.m_nCurrX
                && m_nCurrY == master.m_nCurrY)
                return;

            if (m_nNativeGuardAnchorX != master.m_nCurrX
                || m_nNativeGuardAnchorY != master.m_nCurrY)
            {
                m_nNativeGuardAnchorX = master.m_nCurrX;
                m_nNativeGuardAnchorY = master.m_nCurrY;
            }

            // ≥12 过远、3..11 跟随、&lt;3 但不在主人格：都走向主人。
            // 同格贴身已在上面 return。不发明 dist>20 传送（H-B6，sub_68BAD4 无此 cmp）。
            SetTargetXY(master.m_nCurrX, master.m_nCurrY);
            GotoTargetXY();
            m_btDirection = M2Share.GetNextDirection(
                m_nCurrX, m_nCurrY, master.m_nCurrX, master.m_nCurrY);
        }

        private void AttackTarget(TPlayObject master, int dwCurTick)
        {
            if (m_TargetCret == null || m_TargetCret.m_boDeath)
            {
                DelTargetCreat();
                return;
            }

            // 原版目标有效性清扫 sub_68A610 @0x68A660 用 sub_76B4A4（切比雪夫）。
            // 0x68A665 83 F8 0F / 0x68A668 72 0A jb — 仅 dist<15 保留。
            int dist = NativeGridDistance(m_nCurrX, m_nCurrY, m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY);

            if (dist >= 15)
            {
                DelTargetCreat();
                return;
            }

            // TWarHero VMT+0x2A0 selects the 2/4-cell cross-moon action
            // before its ordinary melee-distance branch. Its epilogue also
            // arms the stance, so a newly armed stance is not consumed until
            // a later decision pass.
            if (m_btJob == 0 &&
                TryRunNativeWarCrossMoon(m_TargetCret,
                    HUtil32.GetTickCount()))
            {
                return;
            }

            // Job engagement radius. These are the AttackTarget predicates, not
            // the invented universal melee-1 walk:
            //   TWarHero 0x6931B0 `cmp eax,1 / ja` → walk when dist > 1
            //   TMagHero 0x69502A `cmp edi,8 / setle` → act when dist <= 8
            //   TTaosHero 0x694268 `cmp edi,9 / jg` and `cmp [ebp-8],9 / jg`
            //     (per-axis box = Chebyshev 9)
            var approach = m_btJob switch
            {
                1 => 8,
                2 => 9,
                _ => 1
            };
            if (dist > approach)
            {
                SetTargetXY(m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY);
                GotoTargetXY();
                return;
            }

            if (m_btJob != 0 && dist > 1)
            {
                TryCastSkill(master, dwCurTick);
                return;
            }

            // In melee range — attack using m_dwHitTick from AnimalObject base.
            // 攻击间隔 = 原版 [hero+0x320]（m_nNextHitTime）。ctor sub_6864C4 @0x686515
            // `mov dword [edi+0x320], 0x3E8` 播种 1000ms；装备/记录应用 sub_68FAB8
            // @0x68FB8E-0x68FBAE 只会调低：`edx = 0x7D0(2000) - rec[0x1D]*0xC8(200)`,
            // `cmp edx,[eax+0x320]; jge skip; mov [eax+0x320],edx`。
            // 三个职业子类都把它和命中戳 [+0x35C] 相比（TWarHero sub_693090 @0x6930B0/0x6930B6）。
            // 旧代码 `_MAX(300, 800 - m_Abil.Level*5)` 是发明——原版此处从不读等级。
            int attackInterval = m_nNextHitTime;
            if ((dwCurTick - m_dwHitTick) > attackInterval)
            {
                byte nDir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY);
                m_btDirection = nDir;
                Attack(m_TargetCret, nDir);
                m_dwHitTick = dwCurTick;

                // Try casting a hero skill
                TryCastSkill(master, dwCurTick);
            }
        }

        /// <summary>
        /// Attempt to cast a hero skill if available and cooldown allows.
        /// Uses direct spell execution rather than MagicManager.DoSpell
        /// which requires TPlayObject.
        /// </summary>
        private void TryCastSkill(TPlayObject master, int dwCurTick)
        {
            bool targetlessNativeSkill153 = m_TargetCret == null &&
                master != null &&
                YanshenHeroCastState.TryPeek(master.m_sCharName,
                    out var pendingMagicId, out _) &&
                pendingMagicId == SpellsDef.SKILL_153;
            if (!targetlessNativeSkill153 &&
                (m_TargetCret == null || m_TargetCret.m_boDeath))
                return;

            int skillCooldown = 3000; // 3 seconds between hero skills
            if ((dwCurTick - m_dwHeroMagicTick) < skillCooldown)
                return;

            if (TrySelectCommandedMagic(master, out var commandedMagic))
            {
                TryReleaseHeroMagic(commandedMagic, dwCurTick);
                return;
            }

            if (targetlessNativeSkill153)
                return;

            if (m_HeroMagicList.Count == 0)
                return;

            foreach (var userMagic in m_HeroMagicList)
            {
                if (userMagic == null || userMagic.MagicInfo == null)
                    continue;
                if (IsNativeUnionMagicId(userMagic.MagicInfo.wMagicID))
                    continue;

                if (TryReleaseHeroMagic(userMagic, dwCurTick))
                    break;
            }
        }

        private bool TrySelectCommandedMagic(TPlayObject master,
            out TUserMagic userMagic)
        {
            userMagic = null;
            if (master == null ||
                (m_btJob != M2Share.jWizard && m_btJob != M2Share.jTaos) ||
                !YanshenHeroCastState.TryConsume(master.m_sCharName,
                    out var magicId))
            {
                return false;
            }

            userMagic = FindHeroMagicById(magicId);
            return true;
        }

        private bool TryReleaseHeroMagic(TUserMagic userMagic, int dwCurTick)
        {
            if (userMagic?.MagicInfo == null ||
                IsNativeUnionMagicId(userMagic.MagicInfo.wMagicID))
            {
                return false;
            }

            bool nativeUnsupportedSkill153 =
                userMagic.MagicInfo.wMagicID == SpellsDef.SKILL_153;
            TBaseObject spellTarget = m_TargetCret;
            if (nativeUnsupportedSkill153 && spellTarget != null &&
                spellTarget != this &&
                (spellTarget.m_PEnvir != m_PEnvir ||
                 Math.Max(Math.Abs(m_nCurrX - spellTarget.m_nCurrX),
                     Math.Abs(m_nCurrY - spellTarget.m_nCurrY)) > 9))
            {
                return false;
            }

            var spellPoint = GetHeroSpellPoint(userMagic);
            if (m_WAbil.MP < spellPoint)
                return false;

            bool nativeSkill152 =
                userMagic.MagicInfo.wMagicID == SpellsDef.SKILL_152;

            m_WAbil.MP -= spellPoint;
            HealthSpellChanged();

            if (nativeUnsupportedSkill153)
            {
                short targetX = unchecked((short)(spellTarget?.m_nCurrX ??
                    m_nCurrX));
                short targetY = unchecked((short)(spellTarget?.m_nCurrY ??
                    m_nCurrY));
                MagicManager.SendNativeSpell(this, userMagic, targetX,
                    targetY);
                return false;
            }

            m_dwHeroMagicTick = dwCurTick;
            if (nativeSkill152)
                return TryActivateNativeSkill152(userMagic, dwCurTick);

            // ---- HERO-MAGIC-3: 接入 sub_68DD88 已移植的分支 (见 HeroObject.NativeDoSpell.cs) ----
            // 0x68DDC3-0x68DDF1: tx/ty 取目标坐标, 目标为空时取英雄自己的坐标。
            // (0x68DE41 的死目标清空发生在 SM_SPELL 之后, 故这里仍用目标坐标。)
            var wMagicID = userMagic.MagicInfo.wMagicID;
            short nSpellX = spellTarget != null
                ? spellTarget.m_nCurrX : m_nCurrX;
            short nSpellY = spellTarget != null
                ? spellTarget.m_nCurrY : m_nCurrY;

            // sub_68DD88 broadcasts the ordinary spell frame before dispatch
            // for every native hero magic except id 42.
            if (wMagicID != SpellsDef.SKILL_GROUPLIGHTENING)
                MagicManager.SendNativeSpell(this, userMagic, nSpellX,
                    nSpellY);

            if (IsNativeHeroSummonMagic(wMagicID))
            {
                // 17/30/41/62/112 在原生分派器里是主人造宠内联段, 完全不经伤害路径。
                if (!TryReleaseNativeHeroSummonMagic(userMagic, wMagicID))
                {
                    return false;             // 0x68E6BE boSpellFail -> 返回 0, 不发 0x27E
                }
                // 0x68E6C0-0x68E6F8: boSpellFire 恒为 1, 发 SM_MAGICFIRE(sub_76920C) 后 result=1。
                MagicManager.SendNativeMagicFire(this, userMagic,
                    nSpellX, nSpellY, spellTarget);
                return true;
            }

            // 13/14/15/18/19/48: 原生在效果之前有一道 sub_73EA20 护身符门,
            // 不过门则 boSpellFail=1 并在 0x68E6BE 直接返回 false (不发 0x27E)。
            // 门后的效果体 (sub_76EB54 / 76ECEC / 76ED74 / 76F1B8 / 76FD40 / 76FBBC)
            // 都是 TCreature 级 helper, C# 侧目前只有 TPlayObject 形态的转写,
            // 尚未按英雄接收者移植 —— 故这些技能【仅】补上护身符门,
            // 效果仍走下面既有的通用伤害路径, 属已登记缺口。
            if (TryGetNativeHeroAmuletCost(wMagicID, out var nCharmCount) &&
                !NativeConsumeBujukCharm(nCharmCount, true))
            {
                return false;
            }

            // Native hero cast path sub_71BB8C reaches the SAME power helper the
            // player path uses (sub_4C8648 -> sub_4C8658, e.g. @0x71BD51, @0x71C13F),
            // which fuses the power roll and divides by the hardcoded float32 4.0 at
            // [0x4C86B8]. There is no separate hero formula and no train-cap divisor.
            int finalDmg = Magic.GetPower(userMagic);

            int nDamage = m_TargetCret.GetMagStruckDamage(this, finalDmg);
            if (nDamage > 0)
            {
                m_TargetCret.StruckDamage(nDamage, this);
                m_TargetCret.SendMsg(m_TargetCret, Grobal2.RM_STRUCK,
                    (short)nDamage, m_TargetCret.m_WAbil.HP,
                    m_TargetCret.m_WAbil.MaxHP, ObjectId, "");
                SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX,
                    m_nCurrY, 0, "");
            }
            return true;
        }

        /// <summary>Calculate MP cost for a hero skill.</summary>
        // Native sub_4C8888 (@0x4C8888..0x4C88C5, whole function, single basic block):
        //   4C8896  mov al,[esi+0x14]        ; MagicInfo+0x14 = wSpell (BYTE read)
        //   4C889C  fild dword [ebp-4]
        //   4C889F  fdiv dword ptr [0x4C88C8]; float32 4.0 (raw 00 00 80 40). The `D8 /6`
        //                                    ; encoding fixes the operand at 4 bytes.
        //   4C88A7  mov al,[ebx+0x0C] / inc  ; UserMagic+0x0C = btLevel, then +1
        //   4C88B1  fmulp st(1)              ; (wSpell/4.0) * (btLevel+1)
        //   4C88B3  call 0x403574            ; fistp qword = round-half-to-even
        //   4C88BA  mov dl,[esi+0x17] / add ax,dx ; + btDefSpell, INSIDE the function
        // btTrainLv (+0x1A) is NEVER read here — in native it is only the level CAP
        // (sub_4C88EC / sub_4C896C). The old `(btTrainLv + 1)` divisor agreed with 4.0
        // only when btTrainLv == 3. All 18 native call sites consume the returned AX
        // directly (`movzx eax,ax` / `mov word[..],ax`, never a further add), so the
        // +0x17 term must stay folded in. Unified onto the byte-audited helper.
        // See staging/heromagic_mpcost_fix_20260804.md §B.
        private ushort GetHeroSpellPoint(TUserMagic userMagic)
        {
            return TPlayObject.GetNativeMagicProducerMpCost(userMagic);
        }

        // --- Hero Magic Management ---

        /// <summary>Learn a new magic skill for the hero.</summary>
        public bool LearnHeroMagic(string sMagicName)
        {
            var magic = M2Share.UserEngine.FindHeroMagic(sMagicName);
            if (magic == null)
                return false;

            foreach (var m in m_HeroMagicList)
            {
                if (m.MagicInfo.wMagicID == magic.wMagicID)
                    return false;
            }

            var userMagic = new TUserMagic
            {
                MagicInfo = magic,
                wMagIdx = magic.wMagicID,
                btLevel = Math.Min((byte)1, magic.btTrainLv),
                btKey = 0,
                nTranPoint = 0
            };
            m_HeroMagicList.Add(userMagic);
            if (m_MagicArr != null && userMagic.wMagIdx < m_MagicArr.Length)
                m_MagicArr[userMagic.wMagIdx] = userMagic;
            RecalcAbilitys();
            SendHeroAddMagic(userMagic);
            return true;
        }

        /// <summary>Delete a hero skill.</summary>
        // Native sub_73F690 (@0x73F690..0x73F7D2), anchored via SM_HERO_DELMAGIC = 2972
        // = 0xB9C (`mov dx,0xB9C` @0x73F74C — a full CODE-segment sweep for that
        // immediate in every 16-bit load form finds exactly 2 sites, both here).
        // It consults NO global definition pool: it name-matches the hero's OWN
        // magic TList in place.
        //   73F6B4  test edi,edi / je            ; nil/empty name -> False
        //   73F6BC  mov eax,[ebx+0x500]          ; the hero's own magic TList
        //   73F6C2  mov esi,[eax+8] / dec esi    ; iterate Count-1 DOWNTO 0
        //   73F6EF  mov edx,[edx]                ; UserMagic+0x00 = MagicInfo*
        //   73F6F1  call 0x405774                ; ShortString@MagicInfo+0 -> AnsiString
        //   73F6FB  call 0x40BD78                ; CASE-INSENSITIVE compare (it repe
        //                                        ; cmpsb then upper-cases the mismatching
        //                                        ; byte pair @0x40BD9E-0x40BDA8)
        //   73F708  mov al,[ebx+0x178]           ; m_btRaceServer: 0 -> SM_DELMAGIC 0xD4,
        //   73F735  cmp al,0x36                  ; 54=RC_HEROOBJECT -> SM_HERO_DELMAGIC,
        //                                        ; anything else -> send nothing
        //   73F797  call 0x424B30                ; TList.Delete(i)
        //   73F79C  mov byte [ebp-1],1 / jmp     ; True, and STOP after the first match
        // Earlier C# resolved the name through UserEngine.FindHeroMagic and then matched
        // the resulting wMagicID — a shape native does not have, which additionally made
        // deletion impossible whenever the definition was missing from the published Hero
        // pool. See staging/heromagic_mpcost_fix_20260804.md §A.
        public bool DeleteHeroMagic(string sMagicName)
        {
            if (string.IsNullOrEmpty(sMagicName))
                return false;

            for (int i = m_HeroMagicList.Count - 1; i >= 0; i--)
            {
                var userMagic = m_HeroMagicList[i];
                if (userMagic?.MagicInfo == null)
                    continue;
                if (!string.Equals(userMagic.MagicInfo.sMagicName, sMagicName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var magicId = userMagic.MagicInfo.wMagicID;
                SendToMaster(Grobal2.SM_HERO_DELMAGIC, magicId, 0, 0, 0, "");
                if (m_MagicArr != null && magicId < m_MagicArr.Length &&
                    ReferenceEquals(m_MagicArr[magicId], userMagic))
                    m_MagicArr[magicId] = null;
                m_HeroMagicList.RemoveAt(i);
                RecalcAbilitys();
                return true;
            }
            return false;
        }

        /// <summary>Upgrade a hero skill level.</summary>
        public bool UpgradeHeroMagic(string sMagicName)
        {
            var magic = M2Share.UserEngine.FindHeroMagic(sMagicName);
            if (magic == null)
                return false;

            foreach (var m in m_HeroMagicList)
            {
                if (m.MagicInfo.wMagicID == magic.wMagicID)
                {
                    var currentLevel = Math.Min(m.btLevel, magic.btTrainLv);
                    if (currentLevel >= magic.btTrainLv)
                        return false;

                    m.btLevel = (byte)(currentLevel + 1);
                    RecalcAbilitys();
                    SendMsg(this, Grobal2.RM_MAGIC_LVEXP, 0,
                        magic.wMagicID, m.btLevel, m.nTranPoint, string.Empty);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Check if hero has learned a specific skill.</summary>
        public int CheckHeroSkill(int skillId)
        {
            foreach (var m in m_HeroMagicList)
            {
                if (m.MagicInfo.wMagicID == skillId)
                    return m.btLevel;
            }
            return -1;
        }

        internal int CheckIfCanAddUSExp()
        {
            if (FindNativeUnionMagic() == null)
                return -2;

            return FindNativeUnionItem() == null ? -3 : 0;
        }

        internal int AddUSExp(int lingFuAmount, int powerAmount, int skillExp)
        {
            var owner = m_Master as TPlayObject;
            if (owner == null || lingFuAmount <= 0 ||
                !owner.TryGetNativeLingFuBalance(out var balance) ||
                lingFuAmount > balance)
                return -4;

            var unionMagic = FindNativeUnionMagic();
            if (unionMagic == null)
                return -2;

            var unionItem = FindNativeUnionItem();
            if (unionItem == null)
                return -3;

            if (unionItem.Dura < powerAmount)
                return -5;

            if (!TrainNativeUnionMagic(unionMagic, skillExp,
                    out var requiredTrain))
                return -6;

            unionItem.Dura = unchecked((ushort)(unionItem.Dura - powerAmount));
            QueueNativeUnionMagicProgress(unionMagic, requiredTrain);
            SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK,
                unionItem.Dura, unionItem.DuraMax, 0, string.Empty);
            owner.DecNativeLingFu(30_004, lingFuAmount);
            return 1;
        }

        private TUserMagic FindNativeUnionMagic()
        {
            return m_NativeUnionMagic;
        }

        internal bool IsCachedNativeUnionMagic(TUserMagic magic)
        {
            return ReferenceEquals(m_NativeUnionMagic, magic);
        }

        internal void RestoreNativeUnionMagicCacheForLogon()
        {
            if (m_HeroMagicList == null)
                return;

            foreach (var magic in m_HeroMagicList)
            {
                if (magic?.MagicInfo != null &&
                    IsNativeUnionMagicId(magic.MagicInfo.wMagicID))
                {
                    m_NativeUnionMagic = magic;
                    return;
                }
            }
        }

        private TUserItem FindNativeUnionItem()
        {
            var unionItem = m_UseItems != null &&
                            m_UseItems.Length > Grobal2.U_BUJUK
                ? m_UseItems[Grobal2.U_BUJUK]
                : null;
            var stdItem = unionItem == null || unionItem.wIndex == 0
                ? null
                : M2Share.UserEngine?.GetStdItem(unionItem.wIndex);
            return string.Equals(NativeItemFactory.GetClassName(stdItem),
                "TUnionItem", StringComparison.Ordinal) ? unionItem : null;
        }

        internal void ProcessNativeUnionState(int currentTick)
        {
            if (m_boDeath || m_boGhost || m_WAbil.HP <= 0 ||
                unchecked((uint)(currentTick - m_dwNativeUnionProcessTick)) <= 500)
                return;

            m_dwNativeUnionProcessTick = currentTick;
            if (m_btNativeUnionState == 2)
            {
                QueueNativeUnionStatus();
                m_wNativeUnionEnergy = unchecked((ushort)(m_wNativeUnionEnergy - 5));
                if (m_wNativeUnionEnergy == 0)
                    m_btNativeUnionState = 0;
                return;
            }

            if (m_btNativeUnionState != 0 ||
                unchecked((uint)(currentTick - m_dwNativeUnionChargeTick)) < 900)
                return;

            m_dwNativeUnionChargeTick = currentTick;
            var amount = CalculateNativeUnionChargeAmount(m_Abil.Level,
                m_wNativeUnionChargeTier,
                M2Share.g_Config?.HeroUnionChargeOverrides,
                M2Share.RandomNumber.Random);
            ChargeNativeUnionEnergy(amount);
        }

        internal bool TryReleaseNativeUnionMagic()
        {
            return TryReleaseNativeUnionMagic(FindMaster());
        }

        private bool TryReleaseNativeUnionMagic(TPlayObject master)
        {
            var magic = FindNativeUnionMagic();
            var target = m_TargetCret;
            if (magic?.MagicInfo == null || m_btNativeUnionState != 2 ||
                master == null || master.m_boDeath || master.m_boGhost ||
                target == null || target.m_boDeath || target.m_boGhost)
                return false;

            var spellPoint = GetHeroSpellPoint(magic);
            if (m_WAbil.MP < spellPoint || master.m_WAbil.MP < spellPoint)
                return false;

            if (!ReleaseNativeUnionMagic(master, target, magic))
                return false;

            // The native job routines only verify MP.  They do not spend it,
            // then clear the pending state and restore the energy field to 0.
            m_btNativeUnionState = 0;
            m_wNativeUnionEnergy = 0;
            return true;
        }

        private bool ReleaseNativeUnionMagic(TPlayObject master,
            TBaseObject target, TUserMagic magic)
        {
            switch (m_btJob)
            {
                case 0:
                    switch (master.m_btJob)
                    {
                        case 0:
                            return ReleaseNativeUnionLine(master, target, magic);
                        case 1:
                            return ReleaseNativeUnionArea(master, target, magic,
                                2, -1, 2, 2, 8, 4, 100,
                                Grobal2.RM_WSJATTACK);
                        case 2:
                            return ReleaseNativeUnionDiagonals(master, target,
                                magic, 2, 8, Grobal2.RM_WTJATTACK);
                        case 3:
                            return ReleaseNativeUnionDoublePhysical(master,
                                target, magic, 1, 1);
                    }
                    break;
                case 1:
                    switch (master.m_btJob)
                    {
                        case 0:
                            return ReleaseNativeUnionArea(master, target, magic,
                                2, -1, 2, 8, 2, 4, 100,
                                Grobal2.RM_WSJATTACK);
                        case 1:
                            return ReleaseNativeUnionArea(master, target, magic,
                                2, -1, 2, 9, 9, 4, 10,
                                rollTargetDamage: true);
                        case 2:
                            return ReleaseNativeUnionArea(master, target, magic,
                                2, -1, 2, 9, 9, 4, 10);
                        case 3:
                            return ReleaseNativeUnionMagicPhysical(master,
                                target, magic, 1, 8);
                    }
                    break;
                case 2:
                    switch (master.m_btJob)
                    {
                        case 0:
                            return ReleaseNativeUnionDiagonals(master, target,
                                magic, 8, 2, Grobal2.RM_WTJATTACK);
                        case 1:
                            return ReleaseNativeUnionArea(master, target, magic,
                                2, -1, 2, 9, 9, 4, 10);
                        case 2:
                            return ReleaseNativeUnionArea(master, target, magic,
                                2, -2, 2, 9, 9, 4, 10);
                        case 3:
                            return ReleaseNativeUnionMagicPhysical(master,
                                target, magic, 1, 8);
                    }
                    break;
            }
            return false;
        }

        private bool ReleaseNativeUnionLine(TPlayObject master,
            TBaseObject target, TUserMagic magic)
        {
            var range = GetNativeUnionEffectiveLevel(magic) >= 4 ? 3 : 2;
            if (!IsNativeUnionLineInRange(this, target, range) ||
                !IsNativeUnionLineInRange(master, target, range))
                return false;

            SendNativeUnionSpellEffect(target, magic);
            MagicManager.SendNativeSpell(master, magic, target.m_nCurrX,
                target.m_nCurrY);
            SendNativeUnionAction(this, target, Grobal2.RM_WWJATTACK);
            SendNativeUnionAction(master, target, Grobal2.RM_WWJATTACK);
            return true;
        }

        private bool ReleaseNativeUnionDiagonals(TPlayObject master,
            TBaseObject target, TUserMagic magic, int heroRange,
            int masterRange, int nativeAction)
        {
            if (!AreNativeUnionActorsInRange(master, target, heroRange,
                masterRange))
                return false;

            SendNativeUnionSpellEffect(target, magic);
            SendNativeUnionAction(this, target, nativeAction);
            var power = GetNativeUnionMagicDamage(master, magic);
            for (var offset = -3; offset <= 3; offset++)
            {
                DealNativeUnionMagicAreaHit(target.m_nCurrX + offset,
                    target.m_nCurrY + offset, target, magic, power, 4, 10,
                    out _, true);
            }
            for (var offset = 3; offset >= -3; offset--)
            {
                DealNativeUnionMagicAreaHit(target.m_nCurrX - offset,
                    target.m_nCurrY + offset, target, magic, power, 4, 10,
                    out _, true);
            }
            DealNativeUnionMagicHit(target, magic, power);
            return true;
        }

        private bool ReleaseNativeUnionArea(TPlayObject master,
            TBaseObject target, TUserMagic magic, int xRadius, int yStart,
            int yEnd, int heroRange, int masterRange,
            int collateralDamageNumerator, int collateralDamageDenominator,
            int nativeAction = 0, bool rollTargetDamage = false)
        {
            if (!AreNativeUnionActorsInRange(master, target, heroRange,
                masterRange))
                return false;

            SendNativeUnionSpellEffect(target, magic);
            if (nativeAction != 0)
                SendNativeUnionAction(this, target, nativeAction);
            var power = GetNativeUnionMagicDamage(master, magic);
            for (var x = target.m_nCurrX - xRadius;
                 x <= target.m_nCurrX + xRadius; x++)
            {
                for (var y = target.m_nCurrY + yStart;
                     y <= target.m_nCurrY + yEnd; y++)
                {
                    if (rollTargetDamage && DealNativeUnionMagicAreaHit(x, y,
                            target, magic, power, collateralDamageNumerator,
                            collateralDamageDenominator, out var targetDamage))
                    {
                        power = targetDamage;
                    }
                    else if (!rollTargetDamage)
                    {
                        DealNativeUnionMagicAreaHit(x, y, target, magic, power,
                            collateralDamageNumerator,
                            collateralDamageDenominator);
                    }
                }
            }
            return true;
        }

        private bool ReleaseNativeUnionDoublePhysical(TPlayObject master,
            TBaseObject target, TUserMagic magic, int heroRange,
            int masterRange)
        {
            if (!AreNativeUnionActorsInRange(master, target, heroRange,
                masterRange))
                return false;

            DealNativeUnionPhysicalHit(target, this, magic);
            SendNativeUnionEffect(this, target, magic, 1032);
            DealNativeUnionPhysicalHit(target, master, magic);
            SendNativeUnionEffect(master, target, magic, 1029);
            return true;
        }

        private bool ReleaseNativeUnionMagicPhysical(TPlayObject master,
            TBaseObject target, TUserMagic magic, int masterRange,
            int heroRange)
        {
            if (!AreNativeUnionActorsInRange(master, target, heroRange,
                masterRange))
                return false;

            DealNativeUnionMagicHit(target, magic,
                GetNativeUnionMagicDamage(master, magic));
            SendNativeUnionSpellEffect(target, magic);
            DealNativeUnionPhysicalHit(target, master, magic);
            SendNativeUnionEffect(master, target, magic,
                m_btJob == M2Share.jTaos ? 1031 : 1030);
            return true;
        }

        private bool AreNativeUnionActorsInRange(TPlayObject master,
            TBaseObject target, int heroRange, int masterRange)
        {
            return GetNativeUnionGridDistance(this, target) <= heroRange &&
                   GetNativeUnionGridDistance(master, target) <= masterRange;
        }

        private static int GetNativeUnionGridDistance(TBaseObject left,
            TBaseObject right)
        {
            return Math.Max(Math.Abs(left.m_nCurrX - right.m_nCurrX),
                Math.Abs(left.m_nCurrY - right.m_nCurrY));
        }

        private static bool IsNativeUnionLineInRange(TBaseObject actor,
            TBaseObject target, int range)
        {
            var x = Math.Abs(actor.m_nCurrX - target.m_nCurrX);
            var y = Math.Abs(actor.m_nCurrY - target.m_nCurrY);
            return x == 0 ? y <= range : y == 0 ? x <= range :
                x == y && x <= range;
        }

        private int GetNativeUnionMagicDamage(TPlayObject master,
            TUserMagic magic)
        {
            return CalculateNativeUnionMagicDamage(master, magic,
                M2Share.RandomNumber.Random);
        }

        internal int CalculateNativeUnionMagicDamage(TPlayObject master,
            TUserMagic magic, Func<int, int> random)
        {
            var power = 0;
            if (m_btJob == 1)
                power += GetNativeUnionMagicParticipantDamage(this, magic,
                    m_WAbil.MC, random);
            else if (m_btJob == 2)
                power += GetNativeUnionMagicParticipantDamage(this, magic,
                    m_WAbil.SC, random);

            if (master.m_btJob == 1)
                power += GetNativeUnionMagicParticipantDamage(master, magic,
                    master.m_WAbil.MC, random);
            else if (master.m_btJob == 2)
                power += GetNativeUnionMagicParticipantDamage(master, magic,
                    master.m_WAbil.SC, random);
            return power;
        }

        private int GetNativeUnionMagicParticipantDamage(TBaseObject participant,
            TUserMagic magic, int ability, Func<int, int> random)
        {
            // Native splits the two levels: the power formula takes the RAW btLevel
            // (sub_4C8648 @0x4C864B), the damage-table index takes the EFFECTIVE level
            // (sub_4C896C @0x68EF05). GetNativeUnionEffectiveLevel is still called first
            // because it also PUBLISHES magic.NativeLevelBonus, which the raw-level power
            // path leaves alone but later readers rely on.
            var level = GetNativeUnionEffectiveLevel(magic);
            var power = GetNativeUnionMagicBasePower(magic, random);
            return ApplyNativeUnionParticipantFinalDamage(participant,
                CalculateNativeUnionDamage(power, HUtil32.LoWord(ability),
                    HUtil32.HiWord(ability), level, random,
                    NativeUnionDamageTable.WizTao));
        }

        private int GetNativeUnionPhysicalDamage(TBaseObject attacker,
            TUserMagic magic)
        {
            return CalculateNativeUnionPhysicalDamage(attacker, magic,
                M2Share.RandomNumber.Random);
        }

        internal int CalculateNativeUnionPhysicalDamage(TBaseObject attacker,
            TUserMagic magic, Func<int, int> random)
        {
            // Same raw-vs-effective level split as
            // GetNativeUnionMagicParticipantDamage (see its comment).
            var level = GetNativeUnionEffectiveLevel(magic);
            var power = GetNativeUnionMagicBasePower(magic, random);
            // DC 走的是另一条结算例程 sub_68FF2C（系数表 0x7D33FC），不是
            // sub_68EEDC。见 NativeUnionDamageTable 的注释与调用点划分表。
            return ApplyNativeUnionParticipantFinalDamage(attacker,
                CalculateNativeUnionDamage(power,
                    HUtil32.LoWord(attacker.m_WAbil.DC),
                    HUtil32.HiWord(attacker.m_WAbil.DC), level, random,
                    NativeUnionDamageTable.Warrior));
        }

        internal static int ApplyNativeUnionParticipantFinalDamage(
            TBaseObject participant, int incomingDamage)
        {
            // Native player and hero VMT +0x274 handlers return incoming damage.
            return incomingDamage;
        }

        // Native base power = sub_4C8648 -> sub_4C8658, i.e. the SAME function that
        // Spells/Magic.cs GetPower delegates to. Located by anchoring on the union data
        // tables (multiplier f64 @0x7D3278, bonus int32 @0x7D32D0), whose only code
        // readers are @0x68EF20/@0x68EF32 inside sub_68EEDC; each of sub_68EEDC's 10
        // callers computes the base power immediately beforehand, e.g. fn 0x690BAC:
        //   690C1E  mov  eax,[ebx+0x6D4]      ; the union UserMagic
        //   690C3C  push eax                  ; (abilMax - abilMin + 1) spread
        //   690C40  call 0x4C8648             ; <<== BASE POWER
        //   690C45  mov  ecx,eax / add ecx,edi; + abilMin
        //   690C4E  call 0x68EEDC             ; final union damage
        // sub_4C8658 divides by the float32 4.0 at [0x4C86B8] (`D8 /6` fixes the width at
        // 4 bytes; reading it as f64 yields 1.9e+96 because the trailing 55 8B EC is the
        // next function's prologue) and rounds with sub_403574 = fistp qword =
        // round-half-to-even. btTrainLv (+0x1A) is NEVER read in the body; the only
        // MagicInfo offsets it touches are {+0x15,+0x16,+0x18,+0x19}. Draw order is MPow
        // first (@0x4C866E) then btDefPower (@0x4C8683), both BEFORE the divide, and the
        // level multiply is an integer `imul` (@0x4C868E) before the `fild` (@0x4C8693),
        // so nothing truncates early.
        // ⚠️ The level here is the RAW btLevel (`sub_4C8648` @0x4C864B
        // `mov dl,[eax+0x0C]`). Only the damage-TABLE index inside sub_68EEDC uses the
        // EFFECTIVE level (`sub_4C896C` @0x68EF05). Passing the effective level into the
        // power formula as well inflated damage for any hero with a NativeLevelBonus.
        // See staging/heromagic_mpcost_fix_20260804.md §C-RESOLVED.
        private static int GetNativeUnionMagicBasePower(TUserMagic magic,
            Func<int, int> random)
        {
            var basePower = magic.MagicInfo.wPower + random(
                Math.Max(1, magic.MagicInfo.wMaxPower - magic.MagicInfo.wPower));
            var power = HUtil32.Round(basePower * (magic.btLevel + 1) / 4.0d);
            return power + magic.MagicInfo.btDefPower + random(
                Math.Max(1, magic.MagicInfo.btDefMaxPower -
                    magic.MagicInfo.btDefPower));
        }

        /// <summary>
        /// 宿主有两条同形的合击结算例程，唯一差别是 <c>fmul qword [eax*8 + …]</c>
        /// 取哪张系数表：<c>sub_68FF2C @0x68FF6D</c> 取 <c>0x7D33FC</c>（战士），
        /// <c>sub_68EEDC @0x68EF1D</c> 取 <c>0x7D3278</c>（法道）。加法表
        /// <c>0x7D32D0</c> 两条共用（@0x68FF7F / @0x68EF2F）。调用点按能力字段
        /// 干净二分：DC/CC → 战士，MC/SC → 法道，逐点清单见
        /// <see cref="GameSvr.Plugins.YanshenComboTables"/>。
        /// </summary>
        internal enum NativeUnionDamageTable
        {
            /// <summary>sub_68EEDC / 0x7D3278：MC、SC 路。</summary>
            WizTao,
            /// <summary>sub_68FF2C / 0x7D33FC：DC、CC 路。</summary>
            Warrior
        }

        internal static int CalculateNativeUnionDamage(int basePower,
            int minimum, int maximum, int effectiveLevel,
            Func<int, int> random,
            NativeUnionDamageTable table = NativeUnionDamageTable.WizTao)
        {
            var level = Math.Min(Math.Max(0, effectiveLevel),
                NativeUnionDamageMultiplier.Length - 1);
            var power = basePower + minimum + random(Math.Max(1,
                maximum - minimum + 1));
            var multiplier = table == NativeUnionDamageTable.Warrior
                ? Plugins.YanshenComboTables.Warrior(level)
                : Plugins.YanshenComboTables.WizTao(level);
            return HUtil32.Round(power * multiplier) +
                NativeUnionDamageBonus[level];
        }

        private void DealNativeUnionMagicAreaHit(int x, int y,
            TBaseObject primaryTarget, TUserMagic magic, int power,
            int collateralDamageNumerator, int collateralDamageDenominator)
        {
            DealNativeUnionMagicAreaHit(x, y, primaryTarget, magic, power,
                collateralDamageNumerator, collateralDamageDenominator,
                out _);
        }

        private bool DealNativeUnionMagicAreaHit(int x, int y,
            TBaseObject primaryTarget, TUserMagic magic, int power,
            int collateralDamageNumerator, int collateralDamageDenominator,
            out int targetDamage, bool excludePrimaryTarget = false)
        {
            targetDamage = power;
            if (m_PEnvir == null)
                return false;

            var objects = new List<TBaseObject>();
            GetMapBaseObjects(m_PEnvir, x, y, 0, objects);
            foreach (var candidate in objects)
            {
                if (candidate == null || candidate.m_boDeath ||
                    candidate.m_boGhost || !IsProperTarget(candidate))
                    continue;

                if (excludePrimaryTarget &&
                    ReferenceEquals(candidate, primaryTarget))
                    return false;

                targetDamage = DealNativeUnionMagicHit(candidate, magic,
                    ReferenceEquals(candidate, primaryTarget) ? power :
                    CalculateNativeUnionCollateralDamage(power,
                        collateralDamageNumerator, collateralDamageDenominator));
                return true;
            }
            return false;
        }

        internal static int CalculateNativeUnionCollateralDamage(int power,
            int numerator, int denominator)
        {
            return unchecked(power * numerator) / denominator;
        }

        private int DealNativeUnionMagicHit(TBaseObject target,
            TUserMagic magic, int power)
        {
            if (target == null || target.m_boDeath || target.m_boGhost ||
                !IsProperTarget(target))
                return power;

            var damage = ApplyNativeUnionTargetDamage(target, this, magic,
                power);
            SendNativeUnionStruck(target, damage, ObjectId, this);
            return damage;
        }

        private void DealNativeUnionPhysicalHit(TBaseObject target,
            TBaseObject attacker, TUserMagic magic)
        {
            if (target == null || attacker == null || target.m_boDeath ||
                target.m_boGhost || !IsProperTarget(target))
                return;

            var basePower = GetNativeUnionPhysicalDamage(attacker, magic);
            var damage = ApplyNativeUnionTargetDamage(target, attacker, magic,
                basePower);
            SendNativeUnionStruck(target, damage, attacker.ObjectId, attacker);
        }

        private int ApplyNativeUnionTargetDamage(TBaseObject target,
            TBaseObject attacker, TUserMagic magic, int incomingDamage)
        {
            ApplyNativeUnionTargetManaCost(target, attacker,
                GetNativeUnionEffectiveLevel(magic), incomingDamage);
            return ApplyNativeUnionTargetDamage(target, attacker,
                incomingDamage);
        }

        internal static int ApplyNativeUnionTargetDamage(TBaseObject target,
            TBaseObject attacker, int incomingDamage)
        {
            // Native target VMT +0x198 is a separate union-damage handler.
            // The modeled player/hero baseline does not use ordinary AC/MAC.
            if (target is AnimalObject && !(target is TPlayObject) &&
                !(target is HeroObject) && target.m_ExpHitterTick == 0)
                return unchecked(incomingDamage * 3);

            if (target is TPlayObject || target is HeroObject)
                return target.ApplyNativeUnionDamageReductions(incomingDamage);

            return incomingDamage;
        }

        internal static bool ApplyNativeUnionTargetManaCost(TBaseObject target,
            TBaseObject attacker, int effectiveLevel, int incomingDamage)
        {
            if (target == null || attacker == null || effectiveLevel < 4 ||
                attacker.m_btJob != M2Share.jWarr ||
                (!(target is TPlayObject) && !(target is HeroObject)))
                return false;

            var coefficient = target.m_btJob switch
            {
                0 => 0.10,
                1 => 0.40,
                2 or 3 => 0.20,
                _ => 0.0
            };
            if (coefficient == 0.0)
                return false;

            var manaCost = HUtil32.Round(incomingDamage * coefficient);
            target.m_WAbil.MP = Math.Max(0, target.m_WAbil.MP - manaCost);
            if (target is HeroObject targetHero)
                targetHero.HealthSpellChanged();
            else
                target.HealthSpellChanged();
            return true;
        }

        private void SendNativeUnionStruck(TBaseObject target, int damage,
            int attackerId, TBaseObject attacker)
        {
            if (damage <= 0)
                return;

            target.StruckDamage(damage, attacker);
            target.SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101,
                (short)damage, target.m_WAbil.HP, target.m_WAbil.MaxHP,
                attackerId, string.Empty, 600);
        }

        private void SendNativeUnionSpellEffect(TBaseObject target,
            TUserMagic magic)
        {
            m_btDirection = M2Share.GetNextDirection(m_nCurrX, m_nCurrY,
                target.m_nCurrX, target.m_nCurrY);
            // The union path publishes the combined hero/master bonus before
            // sub_769258 computes the effective level carried in the body.
            GetNativeUnionEffectiveLevel(magic);
            MagicManager.SendNativeSpell(this, magic, target.m_nCurrX,
                target.m_nCurrY);
        }

        private void SendNativeUnionEffect(TBaseObject actor,
            TBaseObject target, TUserMagic magic, int action)
        {
            actor.m_btDirection = M2Share.GetNextDirection(actor.m_nCurrX,
                actor.m_nCurrY, target.m_nCurrX, target.m_nCurrY);
            actor.SendRefMsg(Grobal2.RM_NATIVE_UNION_EFFECT, action,
                actor.m_nCurrX, actor.m_nCurrY, 0, string.Empty,
                BuildNativeUnionEffectBody(action,
                    GetNativeUnionEffectiveLevel(magic), actor.m_btDirection,
                    actor.m_nCurrX, actor.m_nCurrY));
        }

        internal static byte[] BuildNativeUnionEffectBody(int action,
            int effectiveLevel, byte direction, int actorX, int actorY)
        {
            var body = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0, 2),
                unchecked((ushort)action));
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2, 2),
                unchecked((ushort)effectiveLevel));
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6, 2), direction);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8, 2),
                unchecked((ushort)actorX));
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(10, 2),
                unchecked((ushort)actorY));
            return body;
        }

        private static void SendNativeUnionAction(TBaseObject actor,
            TBaseObject target, int action)
        {
            actor.SendRefMsg(action, GetNativeUnionActionDirection(
                actor.m_nCurrX, actor.m_nCurrY, target.m_nCurrX,
                target.m_nCurrY), actor.m_nCurrX, actor.m_nCurrY, 0,
                string.Empty);
        }

        internal static byte GetNativeUnionActionDirection(int actorX,
            int actorY, int targetX, int targetY)
        {
            var horizontal = Math.Sign(targetX - actorX);
            var vertical = Math.Sign(targetY - actorY);
            if (horizontal > 0)
            {
                if (vertical < 0)
                    return Grobal2.DR_UPRIGHT;
                if (vertical > 0)
                    return Grobal2.DR_DOWNRIGHT;
                return Grobal2.DR_RIGHT;
            }
            if (horizontal < 0)
            {
                if (vertical < 0)
                    return Grobal2.DR_UPLEFT;
                if (vertical > 0)
                    return Grobal2.DR_DOWNLEFT;
                return Grobal2.DR_LEFT;
            }
            if (vertical < 0)
                return Grobal2.DR_UP;
            return Grobal2.DR_DOWN;
        }

        internal static int CalculateNativeUnionChargeAmount(int level,
            ushort chargeTier, IReadOnlyDictionary<int, int> overrides,
            Func<int, int> random)
        {
            if (level < 43)
                return 0;

            var amount = level switch
            {
                43 => random(2) + 2,
                44 => random(3) + 2,
                45 or 46 => random(3) + 3,
                _ => random(4) + 4
            };
            var tier = unchecked((byte)chargeTier);
            if (tier > 0 && tier < NativeUnionTierBonus.Length)
                amount += NativeUnionTierBonus[tier];
            if (overrides != null && overrides.TryGetValue(level - 43, out var configured))
                amount = configured;
            return amount;
        }

        private void ChargeNativeUnionEnergy(int amount)
        {
            var maximumEnergy = M2Share.g_Config?.nHeroUnionMaxEnergy ?? 200;
            if (amount <= 0 || maximumEnergy <= 0 || FindNativeUnionMagic() == null ||
                m_btNativeUnionState != 0 || m_wNativeUnionEnergy >= maximumEnergy)
                return;

            var unionItem = FindNativeUnionItem();
            if (unionItem == null || unionItem.Dura == 0)
                return;

            amount = Math.Min(amount, unionItem.Dura);
            unionItem.Dura = unchecked((ushort)(unionItem.Dura - amount));
            SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK,
                unionItem.Dura, unionItem.DuraMax, 0, string.Empty);

            var energy = m_wNativeUnionEnergy + amount;
            if (energy >= maximumEnergy)
            {
                m_wNativeUnionEnergy = unchecked((ushort)maximumEnergy);
                m_btNativeUnionState = 1;
            }
            else
            {
                m_wNativeUnionEnergy = unchecked((ushort)energy);
            }
            QueueNativeUnionStatus();
        }

        private void QueueNativeUnionStatus()
        {
            SendMsg(this, Grobal2.RM_HERO_UNIONSTATUS, m_wNativeUnionEnergy,
                m_btNativeUnionState, 0, 0, string.Empty);
        }

        private bool TrainNativeUnionMagic(TUserMagic magic, int skillExp,
            out int requiredTrain)
        {
            requiredTrain = 0;
            if (magic?.MagicInfo == null ||
                magic.MagicInfo.TrainLevel == null ||
                magic.MagicInfo.MaxTrain == null ||
                magic.MagicInfo.TrainLevel.Length < 3 ||
                magic.MagicInfo.MaxTrain.Length < 3 ||
                m_Abil.Level < unchecked((ushort)GetNativeUnionRequiredActorLevel(magic)))
                return false;

            var awardedExp = m_boFastTrain
                ? unchecked(skillExp * 3)
                : skillExp;
            magic.nTranPoint = unchecked(magic.nTranPoint + awardedExp);
            requiredTrain = GetNativeUnionRequiredTrain(magic);
            var crossedThreshold = false;
            while (requiredTrain != -1 &&
                   (uint)requiredTrain <= (uint)magic.nTranPoint)
            {
                magic.nTranPoint = unchecked(magic.nTranPoint - requiredTrain);
                crossedThreshold = true;
                if (magic.btLevel >= magic.MagicInfo.btTrainLv)
                    break;

                magic.btLevel++;
                requiredTrain = GetNativeUnionRequiredTrain(magic);
            }

            if (crossedThreshold)
            {
                RecalcAbilitys();
                QueueNativeUnionMagicProgress(magic, requiredTrain);
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                    string.Empty);
            }
            return true;
        }

        private void QueueNativeUnionMagicProgress(TUserMagic magic,
            int requiredTrain)
        {
            var effectiveLevel = GetNativeUnionEffectiveLevel(magic);
            SendMsg(this, Grobal2.RM_MAGIC_LVEXP, 0,
                magic.MagicInfo.wMagicID, effectiveLevel,
                magic.nTranPoint, string.Empty,
                BitConverter.GetBytes(requiredTrain));
        }

        private void SendNativeUnionMagicProgressNow(TUserMagic magic,
            int requiredTrain)
        {
            var master = FindMaster();
            if (master == null)
                return;

            var effectiveLevel = GetNativeUnionEffectiveLevel(magic);
            var packet = BuildHeroRuntimePacket(new TProcessMessage
            {
                wIdent = Grobal2.RM_MAGIC_LVEXP,
                nParam1 = magic.MagicInfo.wMagicID,
                nParam2 = effectiveLevel,
                nParam3 = magic.nTranPoint
            }, m_Abil.Exp, m_Abil.Level);
            master.SendSocket(packet, BitConverter.GetBytes(requiredTrain));
        }

        private int GetNativeUnionEffectiveLevel(TUserMagic magic)
        {
            var master = FindMaster();
            var masterBonus = master?.NativeMagicLevelBonus ?? 0;
            magic.NativeLevelBonus = unchecked((byte)(NativeMagicLevelBonus + masterBonus));
            return Math.Min(unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        private void RecalcAndSendNativeLogonAbility(bool queued)
        {
            RecalcAbilitys();
            var unionMagic = FindNativeUnionMagic();
            if (unionMagic != null)
            {
                var requiredTrain = GetNativeUnionRequiredTrain(unionMagic);
                if (queued)
                    QueueNativeUnionMagicProgress(unionMagic, requiredTrain);
                else
                    SendNativeUnionMagicProgressNow(unionMagic, requiredTrain);
            }

            if (queued)
            {
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                    string.Empty);
            }
            else
            {
                SendHeroAbility();
            }
        }

        private static int GetNativeUnionRequiredActorLevel(TUserMagic magic)
        {
            var level = magic.btLevel;
            var trainLevels = magic.MagicInfo.TrainLevel;
            return level < 3 && trainLevels != null && level < trainLevels.Length
                ? trainLevels[level]
                : -1;
        }

        private static int GetNativeUnionRequiredTrain(TUserMagic magic)
        {
            var level = magic.btLevel;
            var maxTrain = magic.MagicInfo.MaxTrain;
            return level < 3 && maxTrain != null && level < maxTrain.Length
                ? maxTrain[level]
                : -1;
        }

        /// <summary>Add skill experience for hero (for skill leveling).</summary>
        public bool AddHeroSkillExp(string sMagicName, int exp)
        {
            var magic = M2Share.UserEngine.FindHeroMagic(sMagicName);
            if (magic == null)
                return false;

            foreach (var m in m_HeroMagicList)
            {
                if (m.MagicInfo.wMagicID == magic.wMagicID)
                {
                    TrainSkill(m, exp);
                    CheckMagicLevelup(m);
                    return true;
                }
            }
            return false;
        }

        // ====================================================================
        // Helpers — communicate through the master's socket
        // ====================================================================

        /// <summary>Send a client message through the master player's socket.</summary>
        private void SendToMaster(short wIdent, int nRecog, int nParam, int nTag, int nSeries, string sMsg)
        {
            var master = FindMaster();
            if (master != null)
                master.SendDefMessage(wIdent, nRecog, nParam, nTag, nSeries, sMsg);
        }

        /// <summary>Build the native 184-byte body for SM_HERO_ABILITY.</summary>
        private byte[] BuildHeroAbility()
        {
            return BuildNativeAbilityPacket();
        }

        internal static ClientPacket BuildHeroAbilityHeader(int currentExp, int job)
            => Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_ABILITY, currentExp, job, 0, 0);

        internal static ClientPacket BuildHeroNameHeader(int heroType, int heroRank)
            => Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_NAME, 0, heroType, heroRank, 0);

        /// <summary>Build a binary logon body for SM_HERO_LOGON.
        /// Same format as player BuildLogonBody() but with hero data.</summary>
        private byte[] BuildHeroLogonBody()
        {
            var body = new byte[40];
            using (var ms = new MemoryStream(body))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(GetFeatureToLong());               // [0-3] outlook
                WriteBodyState(bw);                         // [4-19] TAllBodyState
                bw.Write(0);                                // [20-23] state
                bw.Write(0);                                // [24-27] native reserved field
                bw.Write(GetMobileFeature());               // [28-37] TFeature
                bw.Write((ushort)0);                        // [38-39] padding
            }
            return body;
        }

        // ====================================================================
        // SM_HERO_* Send Methods — Category A: Master-only (UI/data messages)
        // ====================================================================

        /// <summary>Send SM_HERO_LOGON — spawn the hero on the master's client.</summary>
        public void SendHeroLogon()
        {
            var master = FindMaster();
            if (master != null)
            {
                // Native THeroAct::Logon emits RM 10599 before RM 10600. The player
                // dispatcher converts those messages to SM 897, then SM 899/898.
                SendHeroBornEffect(m_nCurrX, m_nCurrY);

                // Send SM_HERO_LOGON with hero feature/position body (same 40-byte format as player)
                var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_LOGON, ObjectId, m_nCurrX, m_nCurrY,
                    m_btDirection);
                var body = BuildHeroLogonBody();
                master.SendSocket(defMsg, body);

                // Native THeroAct sends the identity packet immediately after the spawn packet.
                SendHeroName();
            }
            // THeroAct.Logon sub_687D70 @0x688180 queues RM 0x3010 to the
            // hero itself. Its message loop later dispatches the hero-specific
            // 3324 -> optional 4367 cold-time-list cluster (sub_69057C).
            SendMsg(this, Grobal2.RM_NATIVE_LOGON_STATE_SYNC,
                0, 0, 0, 0, string.Empty);
            if (master == null)
                return;
            SendHeroBagItems();
            RecalcAndSendNativeLogonAbility(false);
            SendHeroUseItems();
            SendHeroMyMagic();
            RefreshNativeForceState();
            if (m_btNativeUnionState == 2)
            {
                m_btNativeUnionState = 0;
                m_wNativeUnionEnergy = 0;
            }

            RestoreNativeUnionMagicCacheForLogon();
            if (FindNativeUnionMagic() != null)
                QueueNativeUnionStatus();
            RecalcAndSendNativeLogonAbility(true);
        }

        /// <summary>Send SM_HERO_LOGOUT — despawn the hero from master's client.</summary>
        public void SendHeroLogout()
        {
            SendToMaster(Grobal2.SM_HERO_LOGOUT, 0, 0, 0, 0, "");
        }

        /// <summary>Send SM_HERO_ABILITY — hero main stats (HP/MP/Exp/Level/Gold).</summary>
        public void SendHeroAbility()
        {
            var master = FindMaster();
            if (master == null) return;
            var body = BuildHeroAbility();
            master.SendSocket(BuildHeroAbilityHeader(m_Abil.Exp, m_btJob), body);
        }

        /// <summary>Native has no SM 901 (16-bit dx/cx ident load 0 in CODE).</summary>
        public void SendHeroSubAbility()
        {
        }

        /// <summary>Send SM_HERO_BAGITEMS — hero full bag contents.</summary>
        public void SendHeroBagItems()
        {
            var master = FindMaster();
            if (master == null) return;
            foreach (var item in m_ItemList)
                master.EnsureClientItemId(item);
            var body = EncodeHeroBagItems(m_ItemList, out var itemCount);
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_BAGITEMS, ObjectId, 0,
                itemCount, GetHeroBagCapacity(m_Abil.Level));
            master.SendSocket(defMsg, body);
        }

        internal static byte[] EncodeHeroBagItems(IEnumerable<TUserItem> items, out int itemCount)
        {
            itemCount = 0;
            using var stream = new MemoryStream();
            foreach (var item in items)
            {
                if (item == null || item.wIndex <= 0)
                    continue;

                var record = TPlayObject.EncodeClientItemRecord(item);
                stream.Write(record, 0, record.Length);
                itemCount++;
            }
            return stream.ToArray();
        }

        internal static int GetHeroBagCapacity(int level)
        {
            if (level < 11) return 10;
            if (level < 21) return 20;
            if (level < 31) return 30;
            if (level < 36) return 35;
            return 40;
        }

        public int GetNativeBagItemCount(string itemName)
        {
            if (NativeHeroState == null)
                return -1;
            if (string.IsNullOrEmpty(itemName) || M2Share.UserEngine == null)
                return 0;

            var stdItem = M2Share.UserEngine.GetStdItem(itemName);
            if (stdItem == null)
                return 0;
            var stdItemIndex = M2Share.UserEngine.StdItemList.IndexOf(stdItem) + 1;
            if (stdItemIndex <= 0 || stdItemIndex > ushort.MaxValue)
                return 0;

            var result = 0;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                var item = m_ItemList[i];
                if (item == null || item.wIndex != stdItemIndex)
                    continue;
                result += stdItem.StdMode == 7 ? item.Dura : 1;
            }
            return result;
        }

        public bool TryTakeNativeBagItems(string itemName, int count, out string error)
        {
            error = string.Empty;
            if (NativeHeroState == null)
            {
                error = "hero has no native runtime state";
                return false;
            }
            if (count < byte.MinValue || count > byte.MaxValue)
            {
                error = "native hero bag count must fit in one byte";
                return false;
            }
            if (string.IsNullOrEmpty(itemName) || M2Share.UserEngine == null)
            {
                error = "invalid hero bag item name";
                return false;
            }

            var master = FindMaster();
            if (master?.m_boDealing == true)
            {
                error = "master is trading";
                return false;
            }
            var stdItem = M2Share.UserEngine.GetStdItem(itemName);
            if (stdItem == null)
            {
                error = "unknown standard item";
                return false;
            }
            var stdItemIndex = M2Share.UserEngine.StdItemList.IndexOf(stdItem) + 1;
            if (stdItemIndex <= 0 || stdItemIndex > ushort.MaxValue)
            {
                error = "invalid standard item index";
                return false;
            }
            if (count == 0)
                return true;

            var removeIndices = new List<int>(count);
            for (var i = m_ItemList.Count - 1; i >= 0 && removeIndices.Count < count; i--)
            {
                var item = m_ItemList[i];
                if (item != null && item.wIndex == stdItemIndex)
                    removeIndices.Add(i);
            }
            if (removeIndices.Count != count)
            {
                error = "not enough matching hero bag items";
                return false;
            }
            if (!NativeHeroRuntimeCodec.TryCreateSnapshot(this, out var rollbackRecord,
                    out var rollbackDynamicData, out error))
                return false;

            foreach (var index in removeIndices)
                m_ItemList.RemoveAt(index);
            if (!HeroDataService.QueueSave(this))
            {
                if (!NativeHeroRuntimeCodec.TryApply(this, rollbackRecord,
                        rollbackDynamicData, out var rollbackError))
                    M2Share.ErrorMessage($"[HeroDB] 英雄背包扣除回滚失败 {m_sCharName}: {rollbackError}");
                error = "hero save could not be queued";
                return false;
            }

            WeightChanged();
            SendHeroBagItems();
            return true;
        }

        public bool TrySetNativeLevel(int level, out string error)
        {
            error = string.Empty;
            if (NativeHeroState == null)
            {
                error = "hero has no native runtime state";
                return false;
            }
            if (m_boDeath)
            {
                error = "dead hero level cannot be changed";
                return false;
            }
            if (level < ushort.MinValue || level > ushort.MaxValue)
            {
                error = "native hero level must fit in one word";
                return false;
            }
            if (!NativeHeroRuntimeCodec.TryCreateSnapshot(this, out var rollbackRecord,
                    out var rollbackDynamicData, out error))
                return false;

            var currentHp = m_WAbil.HP;
            var currentMp = m_WAbil.MP;
            m_Abil.Level = (ushort)level;
            HeroLevel = (ushort)level;
            // EXP-06: the 100 at 0x652479 / 0x6B1A3E is a fresh-object default, not a pin --
            // 0x6B1988 only runs it when B.Level is still 0, and the level-up loop refreshes the
            // threshold from the level table every iteration (0x687930 call [vtbl+0x240] ->
            // 0x6BDBD3 B.MaxExp = table[B.Level]). A.MaxExp likewise tracks the level at
            // 0x68720E (call 0x6884C0 GetLevelExp(A.Level) -> [obj+0x244]).
            m_Abil.MaxExp = GetLevelExp(m_Abil.Level);
            RecalcLevelAbilitys();
            m_Abil.HP = Math.Min(currentHp, m_Abil.MaxHP);
            m_Abil.MP = Math.Min(currentMp, m_Abil.MaxMP);
            m_WAbil.HP = m_Abil.HP;
            m_WAbil.MP = m_Abil.MP;
            RecalcAbilitys();

            if (!HeroDataService.QueueSave(this))
            {
                if (!NativeHeroRuntimeCodec.TryApply(this, rollbackRecord,
                        rollbackDynamicData, out var rollbackError))
                    M2Share.ErrorMessage($"[HeroDB] 英雄等级设置回滚失败 {m_sCharName}: {rollbackError}");
                error = "hero save could not be queued";
                return false;
            }

            SendMsg(this, Grobal2.RM_LEVELUP, 0, m_Abil.Exp, 0, 0, string.Empty);
            return true;
        }

        /// <summary>Send SM_HERO_SENDUSEITEMS — hero equipped items.</summary>
        public void SendHeroUseItems()
        {
            var master = FindMaster();
            if (master == null) return;
            foreach (var item in m_UseItems)
                master.EnsureClientItemId(item);
            var body = EncodeHeroUseItems(m_UseItems, out var itemCount);
            if (itemCount <= 0)
                return;
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_SENDUSEITEMS, ObjectId, 0,
                itemCount, 0);
            master.SendSocket(defMsg, body);
        }

        internal static byte[] EncodeHeroUseItems(TUserItem[] useItems, out int itemCount)
        {
            itemCount = 0;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            for (var i = useItems.GetLowerBound(0); i <= useItems.GetUpperBound(0); i++)
            {
                var item = useItems[i];
                if (item == null || item.wIndex <= 0)
                    continue;

                writer.Write(i);
                writer.Write(TPlayObject.EncodeClientItemRecord(item));
                itemCount++;
            }
            return stream.ToArray();
        }

        /// <summary>Send SM_HERO_SENDMYMAGIC — hero skill list.</summary>
        public void SendHeroMyMagic()
        {
            var master = FindMaster();
            if (master == null) return;
            using var stream = new MemoryStream();
            var magicCount = 0;
            for (var i = 0; i < m_HeroMagicList.Count; i++)
            {
                var magic = m_HeroMagicList[i];
                if (magic?.MagicInfo == null) continue;
                var record = EncodeHeroMagic(magic);
                stream.Write(record, 0, record.Length);
                magicCount++;
            }
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_SENDMYMAGIC, 0, 0, 0,
                magicCount);
            master.SendSocket(defMsg, stream.ToArray());
        }

        internal static byte[] EncodeHeroMagic(TUserMagic userMagic)
        {
            return TPlayObject.EncodeClientMagic(userMagic);
        }

        internal void SendHeroAddMagic(TUserMagic userMagic)
        {
            var master = FindMaster();
            if (master == null || userMagic?.MagicInfo == null) return;
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_ADDMAGIC, 0, 0, 0, 1);
            master.SendSocket(defMsg, EncodeHeroMagic(userMagic));
        }

        /// <summary>Send SM_HERO_ADDITEM — add a single item to hero bag display.</summary>
        public void SendHeroAddItem(TUserItem userItem)
        {
            var master = FindMaster();
            if (master == null) return;
            var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
            if (stdItem == null) return;
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_ADDITEM, ObjectId, 0, 0, 1);
            master.SendSocket(defMsg, master.EncodeOwnedClientItemRecord(userItem));
        }

        /// <summary>Send SM_HERO_DELITEM — remove a single item from hero bag display.</summary>
        public void SendHeroDelItem(TUserItem userItem)
        {
            var master = FindMaster();
            if (master == null || userItem == null) return;
            SendToMaster(Grobal2.SM_HERO_DELITEM, master.EnsureClientItemId(userItem), 0, 0, 1, "");
        }

        public void SendHeroBagItemDuraChange(TUserItem userItem)
        {
            var master = FindMaster();
            if (master == null || userItem == null) return;
            SendToMaster(Grobal2.SM_HERO_BAGITEMDURACHG, master.EnsureClientItemId(userItem),
                userItem.Dura, HUtil32.LoWord(userItem.DuraMax), HUtil32.HiWord(userItem.DuraMax), "");
        }

        /// <summary>Refresh a hero bag item without using a non-native update message.</summary>
        public void SendHeroUpdateItem(TUserItem userItem)
        {
            SendHeroBagItemDuraChange(userItem);
        }

        /// <summary>Refresh hero HP/MP through the native ability snapshot.</summary>
        public new void HealthSpellChanged()
        {
            SendHeroAbility();
            base.HealthSpellChanged();
        }

        /// <summary>Send SM_HERO_NAME — hero name display.</summary>
        public void SendHeroName()
        {
            var master = FindMaster();
            if (master == null) return;
            master.SendSocket(BuildHeroNameHeader(HeroType, HeroRank), m_sCharName);
        }

        /// <summary>Play the native hero-hide effect at the specified map position.</summary>
        public void SendHeroHideEffect(int x, int y)
        {
            SendToMaster(Grobal2.SM_HERO_QUITMAGIC, 0, x, y, 0, "");
        }

        /// <summary>Play the native hero-born effect at the specified map position.</summary>
        public void SendHeroBornEffect(int x, int y)
        {
            SendToMaster(Grobal2.SM_HERO_LOGMAGIC, 0, x, y, 0, "");
        }

        // Visible hero actions use the same RM-to-SM conversion as every other map actor.
        public void SendHeroStruck(int nDmg, int nHp, int nMaxHp)
        {
            SendRefMsg(Grobal2.RM_STRUCK, (short)nDmg, nHp, nMaxHp, ObjectId, "");
            HealthSpellChanged();
        }

        public void SendHeroDeath()
        {
            SendRefMsg(Grobal2.RM_DEATH, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroAlive()
        {
            SendRefMsg(Grobal2.RM_ALIVE, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroTurn()
        {
            SendRefMsg(Grobal2.RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroWalk()
        {
            SendRefMsg(Grobal2.RM_WALK, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroRun()
        {
            SendRefMsg(Grobal2.RM_RUN, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroHit()
        {
            SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroHeavyHit()
        {
            SendRefMsg(Grobal2.RM_HEAVYHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroBigHit()
        {
            SendRefMsg(Grobal2.RM_BIGHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroSpell(short nSpellId, short nTargetX, short nTargetY, int nTargetId)
        {
            SendRefMsg(Grobal2.RM_SPELL, nSpellId, nTargetX, nTargetY, nTargetId, "");
        }

        public void SendHeroPowerHit()
        {
            SendRefMsg(Grobal2.RM_POWERHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroLongHit()
        {
            SendRefMsg(Grobal2.RM_LONGHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroLongHit2()
        {
            SendRefMsg(Grobal2.RM_LONGHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroLastHit()
        {
            SendRefMsg(Grobal2.RM_TWINHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroWideHit()
        {
            SendRefMsg(Grobal2.RM_WIDEHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroFireHit()
        {
            SendRefMsg(Grobal2.RM_FIREHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroCrshit()
        {
            SendRefMsg(Grobal2.RM_CRSHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroTwinHit()
        {
            SendRefMsg(Grobal2.RM_TWINHIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroRush()
        {
            SendRefMsg(Grobal2.RM_RUSH, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        public void SendHeroRushKung()
        {
            SendRefMsg(Grobal2.RM_RUSHKUNG, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        // ====================================================================
        // CM_HERO_* Client Message Handlers
        // Called from TPlayObject.Operate() when master sends a hero command.
        // ====================================================================

        /// <summary>Handle CM_HERO_TAKEON — hero equip an item from bag.</summary>
        public void ClientHeroTakeOn(TProcessMessage ProcessMsg)
        {
            var master = FindMaster();
            if (master == null) return;

            var requestClientItemId = ProcessMsg.nParam1;
            var equipSlot = ProcessMsg.nParam2;

            var item = master.FindClientItemIn(m_ItemList, requestClientItemId, false)
                       ?? master.FindClientItemIn(m_ItemList, requestClientItemId, true);
            var itemIdx = item == null ? -1 : m_ItemList.IndexOf(item);
            var stdItem = item == null ? null : M2Share.UserEngine.GetStdItem(item.wIndex);
            if (m_boDeath || item == null || itemIdx < 0 || stdItem == null ||
                equipSlot < 0 || equipSlot >= m_UseItems.Length ||
                !ItemNameMatches(item, ProcessMsg.sMsg))
            {
                SendToMaster(Grobal2.SM_HERO_TAKEON_FAIL, 0, 0, 0, 0, "");
                return;
            }

            var takeOffItem = m_UseItems[equipSlot];
            if (!M2Share.CheckUserItems(equipSlot, stdItem) ||
                (equipSlot != Grobal2.U_BUJUK && equipSlot != Grobal2.U_CHARM &&
                 master.m_boDealing && takeOffItem != null && takeOffItem.wIndex > 0))
            {
                SendToMaster(Grobal2.SM_HERO_TAKEON_FAIL, -2, 0, 0, 0, "");
                return;
            }

            if (takeOffItem != null && takeOffItem.wIndex > 0 && !CanTakeOffHeroItem(takeOffItem))
            {
                SendToMaster(Grobal2.SM_HERO_TAKEON_FAIL, 0, 0, 0, 0, "");
                return;
            }

            m_ItemList.RemoveAt(itemIdx);
            m_UseItems[equipSlot] = item;
            if (takeOffItem != null && takeOffItem.wIndex > 0)
                m_ItemList.Add(takeOffItem);

            RecalcAbilitys();
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_TAKEON_OK, ObjectId, 0, 0, 0);
            master.SendSocket(defMsg, GetMobileFeature());
            SendHeroAbility();
            FeatureChanged();
        }

        /// <summary>Handle CM_HERO_TAKEOFF — hero unequip an item to bag.</summary>
        public void ClientHeroTakeOff(TProcessMessage ProcessMsg)
        {
            var master = FindMaster();
            if (master == null) return;

            var requestClientItemId = ProcessMsg.nParam1;
            var equipSlot = ProcessMsg.nParam2;

            if (m_boDeath || equipSlot < 0 || equipSlot >= m_UseItems.Length ||
                (equipSlot != Grobal2.U_BUJUK && equipSlot != Grobal2.U_CHARM && master.m_boDealing))
            {
                SendToMaster(Grobal2.SM_HERO_TAKEOFF_FAIL, 0, 0, 0, 0, "");
                return;
            }

            var item = m_UseItems[equipSlot];
            var itemMatchesRequest = item != null && (master.ClientItemIdMatches(item, requestClientItemId)
                                                      || item.MakeIndex == requestClientItemId);
            if (item == null || item.wIndex <= 0 || !itemMatchesRequest ||
                !CanTakeOffHeroItem(item) || !ItemNameMatches(item, ProcessMsg.sMsg))
            {
                SendToMaster(Grobal2.SM_HERO_TAKEOFF_FAIL, 0, 0, 0, 0, "");
                return;
            }

            if (m_ItemList.Count >= GetHeroBagCapacity(m_Abil.Level))
            {
                SendToMaster(Grobal2.SM_HERO_TAKEOFF_FAIL, -3, 0, 0, 0, "");
                return;
            }

            m_UseItems[equipSlot] = null;
            m_ItemList.Add(item);

            RecalcAbilitys();
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_TAKEOFF_OK, ObjectId, 0, 0, 0);
            master.SendSocket(defMsg, GetMobileFeature());
            SendHeroAbility();
            FeatureChanged();
        }

        private bool TryLearnHeroSkillBook(GoodItem stdItem, TUserItem book,
            int bookIndex)
        {
            if (stdItem.Weight != 0 || bookIndex < 0 ||
                !ReferenceEquals(m_ItemList[bookIndex], book) ||
                M2Share.PasEngine?.FindItemScriptFile(stdItem.Name) != null)
                return false;

            var magic = M2Share.UserEngine.FindHeroMagic(stdItem.Name);
            if (magic == null || magic.TrainLevel == null ||
                magic.TrainLevel.Length == 0)
                return false;

            foreach (var learnedMagic in m_HeroMagicList)
            {
                if (learnedMagic?.MagicInfo?.wMagicID == magic.wMagicID)
                    return false;
            }

            if (m_Abil.Level < magic.TrainLevel[0])
                return false;

            var isNativeUnion = TryGetNativeUnionSkillBookMasterJob(
                magic.wMagicID, out var requiredMasterJob);
            if (isNativeUnion)
            {
                var master = FindMaster();
                if (master == null || master.m_btJob != requiredMasterJob)
                    return false;
            }
            else if (magic.btJob != 99 && magic.btJob != m_btJob)
            {
                return false;
            }

            if (isNativeUnion)
            {
                m_btNativeUnionState = 0;
                m_wNativeUnionEnergy = 0;
            }

            var userMagic = new TUserMagic
            {
                MagicInfo = magic,
                wMagIdx = magic.wMagicID,
                btKey = 0,
                btLevel = GetNativeHeroSkillBookInitialLevel(magic.wMagicID),
                nTranPoint = 0
            };
            m_HeroMagicList.Add(userMagic);
            if (m_MagicArr != null && userMagic.wMagIdx < m_MagicArr.Length)
                m_MagicArr[userMagic.wMagIdx] = userMagic;
            RecalcAbilitys();
            SendHeroAddMagic(userMagic);

            if (isNativeUnion)
            {
                m_NativeUnionMagic = userMagic;
                QueueNativeUnionStatus();
            }

            m_ItemList.RemoveAt(bookIndex);
            if (isNativeUnion)
                TryAddNativeUnionSkillBookReward();
            m_WAbil.Weight = RecalcBagWeight();
            return true;
        }

        private bool TryGetNativeUnionSkillBookMasterJob(ushort magicId,
            out byte requiredMasterJob)
        {
            requiredMasterJob = 0;
            if (m_btJob >= NativeUnionMagicByHeroAndMasterJob.GetLength(0))
                return false;

            for (var masterJob = 0;
                 masterJob < NativeUnionMagicByHeroAndMasterJob.GetLength(1);
                 masterJob++)
            {
                if (NativeUnionMagicByHeroAndMasterJob[m_btJob, masterJob] != magicId)
                    continue;

                requiredMasterJob = (byte)masterJob;
                return true;
            }
            return false;
        }

        private static byte GetNativeHeroSkillBookInitialLevel(ushort magicId)
        {
            return magicId is 151 or 152 or 153 or 154 or 211 ? (byte)1 : (byte)0;
        }

        private void TryAddNativeUnionSkillBookReward()
        {
            if (m_ItemList.Count >= GetHeroBagCapacity(m_Abil.Level))
                return;

            TUserItem reward = null;
            if (!M2Share.UserEngine.CopyToUserItemFromName(
                    NativeUnionSkillBookReward, ref reward) || reward == null)
                return;

            m_ItemList.Add(reward);
            SendHeroAddItem(reward);
        }

        // Native item-use mode 1 = sub_6866BC ("apply-to-hero" TDragonHeart amulet refill) + body sub_763840.
        // sub_6866BC is called with a1 = a HERO in BOTH dispatch paths:
        //   * master CM_EAT mode-1 (sub_6B8380 case 1) loads [Self+0xBB0]=m_HeroObject and calls sub_6866BC(hero,...);
        //   * hero  CM_HERO_EAT mode-1 (sub_68C4B0) calls sub_6866BC(this-hero,...).
        // So the amulet refill, the RM_DURACHANGE(9) and the bind-period reject all act on/through the hero (a1).
        // The consumed token is removed from the caller's bag by the shared consume tail ONLY on a true return.
        // Conservation: mutates ONLY amulet.Dura (in place, u16-add-then-clamp exactly like sub_763840 +
        // sub_784584); returns false with no mutation on every failure (bound token / no amulet / wrong class /
        // amulet full / no config entry / empty table) so nothing is consumed.
        internal bool TryNativeHeroAmuletRefill(TUserItem consumed)
        {
            if (consumed == null)
                return false;
            // sub_784710 guard: the consumed item's bind word (item+0x34 = btValue[10..11]) must be 0. A bound
            // token is rejected with SysMsg "该物品还处于绑定期" (color 0x38FF) via a1(=hero).vtable+0xD4 and is
            // NOT consumed (native returns false -> shared tail SM_EAT_FAIL, item untouched).
            if (consumed.btValue != null && consumed.btValue.Length >= 12 &&
                BinaryPrimitives.ReadUInt16LittleEndian(consumed.btValue.AsSpan(10, 2)) != 0)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0, "该物品还处于绑定期");
                return false;
            }
            if (m_UseItems == null || Grobal2.U_BUJUK >= m_UseItems.Length)
                return false;
            var amulet = m_UseItems[Grobal2.U_BUJUK];                       // sub_75EC20(hero[+0x4C0], 9)
            if (amulet == null)
                return false;
            var amuletStd = M2Share.UserEngine?.GetStdItem(amulet.wIndex);
            // sub_404828(amulet, off_75E7C4) is the Delphi `is` operator, which walks
            // the parent chain (sub_4048C8: 0x4048D0 mov eax,[eax-0x24] / 0x4048CA
            // mov eax,[eax]) and therefore ACCEPTS descendants. TDragonHeart
            // (VMT 0x75E810) has one -- TSuperDragonHeart (VMT 0x75E90C), produced by
            // factory branch 0x74D118 -- so an exact name comparison here refused a
            // 超级龙之心 that native would refill.
            if (!NativeItemFactory.IsClassOrDescendantOf(amuletStd, "TDragonHeart"))
                return false;
            if (amulet.Dura >= amulet.DuraMax)                              // sub_763840: requires Dura < DuraMax
                return false;
            // refill = sub_74E0A0(g_UserEngine, consumed.wIndex); native stores the result in a 16-bit reg (si).
            var refill = unchecked((ushort)(
                M2Share.UserEngine?.GetNativePowerupItemRefill(consumed.wIndex) ?? 0));
            if (refill == 0)                                                // native: test si,si; jbe => no refill/consume
                return false;
            var refilled = unchecked((ushort)(amulet.Dura + refill));       // native sub_763840: add dx, si (u16)
            if (refilled > amulet.DuraMax)                                  // native sub_784584: clamp to DuraMax
                refilled = amulet.DuraMax;
            amulet.Dura = refilled;
            // native sub_765E68(hero,...,9): RM_DURACHANGE slot 9 -> SM_HERO_DURACHANGE on the master's client.
            SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK, amulet.Dura, amulet.DuraMax, 0, "");
            return true;
        }

        /// <summary>
        /// CM_HERO_EAT mode-4 hero charm charge. Native sub_68C4B0 mode4 -> sub_763B64(a1=hero.m_UseItems[12]
        /// charm, a2=consumed token, a3=hero). Byte-mirror of the master TPlayObject.TryClientUseCharmCharge
        /// (formula/gate/message idat-verified 2026-08-03, sub_763B64 on the reunpacked image): the a3 race gate
        /// reads [hero+0x178]=RC_HEROOBJECT=54 which PASSES the native `race==0||race==54`. Charge is deterministic
        /// (no RNG). Weight is left to the caller's RecalcBagWeight (native's WeightChanged is cosmetic on a Dura refill).
        /// </summary>
        internal bool TryNativeHeroCharmCharge(TUserItem consumed)
        {
            if (consumed == null)
                return false;
            if (m_UseItems == null || Grobal2.U_CHARM >= m_UseItems.Length)
                return false;
            var gem = m_UseItems[Grobal2.U_CHARM];                          // a1 = hero.m_UseItems[12]
            if (gem == null)
                return false;
            if (m_btRaceServer != 0 && m_btRaceServer != 54)                // [a3+0x178] 种族门；英雄 RC_HEROOBJECT=54 通过
                return false;
            var gemStd = M2Share.UserEngine.GetStdItem(gem.wIndex);
            if (gemStd == null)
                return false;
            if (NativeItemFactory.GetClassName(gemStd) != "TMarkStoneCharm")   // native sub_404828(gem,off_75E200)
                return false;
            if (gem.Dura >= gem.DuraMax)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0, "您的" + gemStd.Name + "持久已满,无需填充!");
                return false;
            }
            var consumedStd = M2Share.UserEngine.GetStdItem(consumed.wIndex);
            if (consumedStd == null || consumedStd.StdMode != 7 || consumedStd.Shape != 3 || consumedStd.Sc != 0)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0, "不能使用此物品来填充");
                return false;
            }
            int k = gemStd.Mac != 0 ? gemStd.Mac : (gemStd.Ac != 0 ? gemStd.Ac : 10);   // sub_764560
            long delta = (long)consumedStd.Ac * consumed.Dura;
            long gemDuraTimesK = (long)gem.Dura * k;
            long numerator = delta + gemDuraTimesK;
            if ((long)gem.DuraMax * k < numerator)                          // fits iff DuraMax*K >= numerator
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0, consumedStd.Name + "持久超过" + gemStd.Name + "需填充的持久,无法填充");
                return false;
            }
            var newDura = (int)Math.Round((double)numerator / k, MidpointRounding.ToEven);   // fistp = round-half-to-even
            if (newDura > gem.DuraMax)
                newDura = gem.DuraMax;
            gem.Dura = (ushort)newDura;
            SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_CHARM, gem.Dura, gem.DuraMax, 0, "");   // sub_765E68(...,0x278D,U_CHARM)
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0, "您为" + gemStd.Name + "中填充了1个" + consumedStd.Name + "，持久增加了" + delta + "！");
            return true;
        }

        /// <summary>Handle CM_HERO_EAT — hero use/consume an item.</summary>
        public void ClientHeroUseItem(TProcessMessage ProcessMsg)
        {
            var master = FindMaster();
            if (master == null) return;

            var requestClientItemId = ProcessMsg.nParam1;
            var useMode = ProcessMsg.nParam2;   // 原版 CM_HERO_EAT 派发把 nParam2 use-mode 转给 sub_68C4B0
            if (m_boDeath || !master.m_boCanUseItem)
            {
                SendToMaster(Grobal2.SM_HERO_EAT_FAIL, requestClientItemId, 0, 0, 0, "");
                return;
            }

            var item = master.FindClientItemIn(m_ItemList, requestClientItemId, false)
                       ?? master.FindClientItemIn(m_ItemList, requestClientItemId, true);
            if (item != null)
            {
                var clientItemId = master.EnsureClientItemId(item);
                var itemIndex = m_ItemList.IndexOf(item);
                if (itemIndex >= 0)
                {
                    var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                    if (stdItem == null)
                    {
                        SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                        return;
                    }

                    // 原版派发 sub_68C4B0(hero,itemId,useMode) 按 use-mode 分支：mode1→sub_6866BC(英雄护符
                    // 补充)、mode4→sub_763B64(英雄充能)、否则→sub_784C78(普通)。
                    if (useMode == 1)
                    {
                        // sub_68C4B0 mode1 -> sub_6866BC(this-hero, consumed): 英雄自身 U_BUJUK(9) TDragonHeart
                        // 护符从 powerupItem.ini refill 表填充。成功才消耗令牌(与 mode0 英雄消耗尾一致)，
                        // all-or-nothing；失败(无护符/满/无配置/绑定期)返回 false 且不消耗。
                        if (!TryNativeHeroAmuletRefill(item))
                        {
                            SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                            return;
                        }
                        if (NativeItemFactory.IsPileItem(stdItem) && item.Dura > 1)
                        {
                            item.Dura--;
                            SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                            SendHeroBagItemDuraChange(item);
                        }
                        else
                        {
                            m_ItemList.RemoveAt(itemIndex);
                            SendToMaster(Grobal2.SM_HERO_EAT_OK, clientItemId, 0, 0, 0, "");
                        }
                        m_WAbil.Weight = RecalcBagWeight();
                        return;
                    }
                    if (useMode == 4)
                    {
                        // sub_68C4B0 mode4 -> sub_763B64(hero.m_UseItems[12] charm, consumed, hero): 英雄 U_CHARM(12)
                        // TMarkStoneCharm 充能。种族门 [hero+0x178]=RC_HEROOBJECT=54→通过(idatP 2026-08-03 已证)。
                        // 公式/门/消息与 master TryClientUseCharmCharge 字节一致。成功才消耗令牌(all-or-nothing)。
                        if (!TryNativeHeroCharmCharge(item))
                        {
                            SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                            return;
                        }
                        if (NativeItemFactory.IsPileItem(stdItem) && item.Dura > 1)
                        {
                            item.Dura--;
                            SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                            SendHeroBagItemDuraChange(item);
                        }
                        else
                        {
                            m_ItemList.RemoveAt(itemIndex);
                            SendToMaster(Grobal2.SM_HERO_EAT_OK, clientItemId, 0, 0, 0, "");
                        }
                        m_WAbil.Weight = RecalcBagWeight();
                        return;
                    }

                    var nativeClass = NativeItemFactory.GetClassName(stdItem);
                    if (string.Equals(nativeClass, "TSkillBook", StringComparison.Ordinal))
                    {
                        if (!TryLearnHeroSkillBook(stdItem, item, itemIndex))
                        {
                            SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                            return;
                        }

                        SendToMaster(Grobal2.SM_HERO_EAT_OK, clientItemId, 0, 0, 0, "");
                        return;
                    }

                    var refreshRequired = true;
                    if (string.Equals(nativeClass, "TQuickDrug", StringComparison.Ordinal))
                    {
                        if (!TryApplyNativeQuickDrug(stdItem, out refreshRequired))
                        {
                            SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                            return;
                        }
                    }
                    else
                    {
                        // Preserve the existing hero behavior for non-quick items.
                        if (stdItem.Ac > 0)
                            m_WAbil.HP = (int)Math.Min((long)m_WAbil.HP + stdItem.Ac, m_WAbil.MaxHP);
                        if (stdItem.Mac > 0)
                            m_WAbil.MP = (int)Math.Min((long)m_WAbil.MP + stdItem.Mac, m_WAbil.MaxMP);
                    }

                    if (NativeItemFactory.IsPileItem(stdItem) && item.Dura > 1)
                    {
                        item.Dura--;
                        // Cancel the client's optimistic whole-item removal before updating the stack.
                        SendToMaster(Grobal2.SM_HERO_EAT_FAIL, clientItemId, 0, 0, 0, "");
                        SendHeroBagItemDuraChange(item);
                    }
                    else
                    {
                        m_ItemList.RemoveAt(itemIndex);
                        SendToMaster(Grobal2.SM_HERO_EAT_OK, clientItemId, 0, 0, 0, "");
                    }

                    m_WAbil.Weight = RecalcBagWeight();
                    if (refreshRequired)
                        HealthSpellChanged();
                    return;
                }
            }

            SendToMaster(Grobal2.SM_HERO_EAT_FAIL, requestClientItemId, 0, 0, 0, "");
        }

        /// <summary>Handle CM_HERO_APPTARG — hero approach target.</summary>
        public void ClientHeroAppTarg(TProcessMessage ProcessMsg)
        {
            var targetId = ProcessMsg.nParam1;
            var targetObj = M2Share.ObjectManager.Get(targetId);
            if (targetObj != null)
                SetTargetCreat(targetObj);
        }

        /// <summary>Handle CM_HERO_DROPITEM — hero drop an item from bag.</summary>
        public void ClientHeroDropItem(TProcessMessage ProcessMsg)
        {
            var requestClientItemId = ProcessMsg.nParam1;
            var master = FindMaster();
            if (master == null)
                return;

            var yanshenNoDrop = new YanshenApi(master, null, M2Share.PluginManager).IsSafeNoDrop();
            if (m_boDeath || !master.m_boCanDrop || m_PEnvir == null || m_PEnvir.Flag.boNOTHROWITEM ||
                (M2Share.g_Config.boInSafeDisableDrop || yanshenNoDrop) && InSafeZone())
            {
                SendToMaster(Grobal2.SM_HERO_DROPITEM_FAIL, requestClientItemId, 0, 0, 0, "");
                return;
            }

            var item = master.FindClientItemIn(m_ItemList, requestClientItemId, false)
                       ?? master.FindClientItemIn(m_ItemList, requestClientItemId, true);
            if (item != null)
            {
                var clientItemId = master.EnsureClientItemId(item);
                var itemIndex = m_ItemList.IndexOf(item);
                if (itemIndex >= 0)
                {
                    if (!ItemNameMatches(item, ProcessMsg.sMsg) ||
                        !DropItemDown(item, 1, false, null, this))
                    {
                        SendToMaster(Grobal2.SM_HERO_DROPITEM_FAIL, clientItemId, 0, 0, 0, "");
                        return;
                    }

                    m_ItemList.RemoveAt(itemIndex);
                    WeightChanged();
                    SendToMaster(Grobal2.SM_HERO_DROPITEM_SUCCESS, clientItemId, 0, 0, 0, "");
                    return;
                }
            }

            SendToMaster(Grobal2.SM_HERO_DROPITEM_FAIL, requestClientItemId, 0, 0, 0, "");
        }

        private static bool ItemNameMatches(TUserItem item, string requestedName)
        {
            return string.IsNullOrEmpty(requestedName) || string.Equals(ItmUnit.GetItemName(item), requestedName,
                StringComparison.OrdinalIgnoreCase);
        }

        private bool CanTakeOffHeroItem(TUserItem item)
        {
            var stdItem = item == null ? null : M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem == null || (stdItem.Reserved & 4) != 0 || M2Share.InDisableTakeOffList(item.wIndex))
                return false;

            if (!m_boUserUnLockDurg && (stdItem.Reserved & 2) != 0)
                return false;

            if (!m_boUserUnLockDurg && IsDurabilityLockedMode(stdItem.StdMode) && item.btValue[7] != 0)
                return false;

            return true;
        }

        private static bool IsDurabilityLockedMode(byte stdMode)
        {
            return stdMode == 15 || stdMode == 19 || stdMode == 20 || stdMode == 21 ||
                   stdMode == 22 || stdMode == 23 || stdMode == 24 || stdMode == 26;
        }

        /// <summary>
        /// CM_HERO_CHGSTATE (1107) — 原版为静默 no-op，本实现对齐。
        /// 客户端消息派发 sub_6D7D68：
        /// <code>
        /// 6D81A5  add eax, 0xFFFFFBED        ; ident - 0x413
        /// 6D81AA  cmp eax, 0x48 / ja default
        /// 6D81B3  jmp dword [eax*4 + 0x6D81BA]
        /// </code>
        /// 表项 ident 1107 (0x453, idx 0x40) → 0x6DBC2C，而 0x6DBC2C 是共享的
        /// default 落地标签（`xor eax,eax; pop; pop; pop; mov fs:[eax],edx; jmp 0x6DBD0E`，
        /// 全表 73 项里有 36 项指向它，1111/1112 同样落这里）。相邻 opcode 都是真 handler：
        /// 1100→0x6D9743、1105→0x6D97B0、1106→0x6D97D9、1108→0x6D98B1、1109→0x6D993C、1110→0x6D9963。
        /// 原版模式切换只有两个入口调 sub_688650：0x623B02（GM 路径，dl=0 循环）与
        /// 0x6D104B（主人指派目标时 dl=1 强制攻击模式）——没有任何 CM opcode 能改模式。
        /// </summary>
        public void ClientHeroChgState(TProcessMessage ProcessMsg)
        {
            // 原版无 handler：不改 m_btNativeHeroMode，不回包。
        }

        /// <summary>
        /// 原版 <c>sub_688650(self, dl=forceAttack, ecx=out modeName)</c> — 英雄模式切换。
        /// 逐字节梯级（0x688650-0x68870B）：
        /// <code>
        /// 68865D  cmp byte [ebx+0x6CA],0 / je      ; 守护标志置位 -> sub_6885DC 清目标
        /// 68866D  cmp byte [ebp-1],0     / je 6886AC ; dl==0 -> 循环分支
        /// 688673  cmp byte [ebx+0x6A1],0 / je 68869C ; dl!=0 且已是 0 -> 只清出参
        /// 68867C  xor eax,eax / mov [ebx+0x6A1],al   ; 模式 := 0(攻击)
        /// 6886AE  mov al,[ebx+0x6A1]; inc eax; mov ecx,3; xor edx,edx; div ecx
        /// 6886BE  mov [ebx+0x6A1],dl                ; 模式 := (模式+1) mod 3
        /// 6886DA  cmp byte [ebx+0x6A1],0 / je 68870D
        /// 6886E3  [ebx+0x6C4]!=0 -> [该英雄+0x344]=0 ; 清联动英雄目标
        /// 6886F5  [ebx+0x68C]!=0 -> [主人+0x344]=0   ; 清主人目标
        /// </code>
        /// </summary>
        /// <param name="boForceAttack">原版 dl 参数：非 0 = 强制切到"攻击"，0 = 循环。</param>
        /// <returns>切换后模式的原版显示名（表 0x7D32FC）。</returns>
        internal string ChangeNativeHeroMode(bool boForceAttack)
        {
            // 68865D: 守护标志 [+0x6CA] 置位时先清目标。C# 尚无该字段（见 herobehaviour 文档
            // BLOCKED 项），这里不臆造分支。
            string modeName;
            if (boForceAttack)
            {
                // 688673: 已是攻击模式则不改、不报名（原版只清出参字符串）。
                if (m_btNativeHeroMode == NativeHeroMode.Attack)
                    return string.Empty;
                m_btNativeHeroMode = NativeHeroMode.Attack;
                modeName = GetNativeHeroModeName(m_btNativeHeroMode);
            }
            else
            {
                // 6886AE-6886BE: (模式+1) mod 3。
                m_btNativeHeroMode = (NativeHeroMode)(((byte)m_btNativeHeroMode + 1) % 3);
                modeName = GetNativeHeroModeName(m_btNativeHeroMode);
            }

            // 6886DA-68870B: 只要模式不是 0(攻击) 就清掉联动英雄与主人的目标。
            // 原版是直接字段赋零（`mov dword [eax+0x344], edx` with edx=0），不是虚调用，
            // 所以这里也用字段清零而不是 DelTargetCreat()/SetTargetCreat(null)——
            // 后者在 TAnimalObject 有 override（TAnimalObject.cs:201）会多做事。
            if (m_btNativeHeroMode != NativeHeroMode.Attack)
            {
                // 6886E3: 联动英雄 [+0x6C4]。C# 尚无该字段映射，见文档 BLOCKED 项。
                // 6886F5: 主人 [+0x68C] 的目标。
                if (m_Master != null)
                    m_Master.m_TargetCret = null;
                m_TargetCret = null;
            }

            return modeName;
        }

        /// <summary>
        /// 原版模式名表 <c>0x7D32FC</c>（GBK，NUL 结尾 PChar）：
        /// [0]=0x6862CC 攻击 [1]=0x6862DC 跟随 [2]=0x6862EC 休息。
        /// （表内 [3]=0x6862FC 守护 / [4]=0x68630C 决斗 不在 `div 3` 循环可达范围内。）
        /// </summary>
        internal static string GetNativeHeroModeName(NativeHeroMode mode)
        {
            switch (mode)
            {
                case NativeHeroMode.Attack: return "攻击";
                case NativeHeroMode.Follow: return "跟随";
                case NativeHeroMode.Rest: return "休息";
                default: return string.Empty;
            }
        }

        /// <summary>Handle the native CM_HERO_POWERUP command.</summary>
        public void ClientHeroPowerUp(TProcessMessage ProcessMsg)
        {
            if (m_btNativeUnionState == 1)
                m_btNativeUnionState = 2;
        }

        /// <summary>Handle CM_HERO_SKILL_HOTKEY (skill id in recog, key in param).</summary>
        public void ClientHeroSkillHotkey(int skillId, int key)
        {
            var normalizedKey = key == 255 ? (byte)255 : (byte)0;
            foreach (var userMagic in m_HeroMagicList)
            {
                if (userMagic?.MagicInfo?.wMagicID == skillId)
                {
                    userMagic.btKey = normalizedKey;
                    break;
                }
            }
        }

        private TUserMagic FindHeroMagicById(int spellId)
        {
            foreach (var m in m_HeroMagicList)
            {
                if (m.MagicInfo.wMagicID == spellId)
                    return m;
            }
            return null;
        }

        public new void WeightChanged()
        {
            m_WAbil.Weight = RecalcBagWeight();
            SendHeroAbility();
        }
    }
}
