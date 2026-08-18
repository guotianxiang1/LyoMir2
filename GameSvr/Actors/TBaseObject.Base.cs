using System.Collections;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private static readonly object s_baseObjTimeLock = new object();
        private static readonly object s_btB2InitLock = new object();
        private bool m_boAbilityRecalcPending;
        public virtual void Run()
        {
            TProcessMessage ProcessMsg = null;
            const string sExceptionMsg0 = "[Exception] TBaseObject::Run 0";
            const string sExceptionMsg1 = "[Exception] TBaseObject::Run 1";
            const string sExceptionMsg2 = "[Exception] TBaseObject::Run 2";
            const string sExceptionMsg3 = "[Exception] TBaseObject::Run 3";
            const string sExceptionMsg4 = "[Exception] TBaseObject::Run 4 Code:{0}";
            const string sExceptionMsg5 = "[Exception] TBaseObject::Run 5";
            const string sExceptionMsg6 = "[Exception] TBaseObject::Run 6";
            const string sExceptionMsg7 = "[Exception] TBaseObject::Run 7";
            const string sExceptionMsg8 = "[Exception] TBaseObject::Run 8";
            var dwRunTick = HUtil32.GetTickCount();
            // coldTime tick. In 战神 this is the FIRST thing THumanKind.Run does
            // after entering its try-frame: 0x73C23C reads GetTickCount, 0x73C245
            // calls sub_748200, and only then does 0x73C24C run the death check.
            // Cooldowns therefore tick for dead and living actors alike, so this
            // must stay ahead of any m_boDeath gate. The internal 250 ms gate and
            // the ownership check both live in ProcessNativeColdTimes.
            ProcessNativeColdTimes(dwRunTick);
            try
            {
                while (GetMessage(ref ProcessMsg))
                {
                    Operate(ProcessMsg);
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg0);
                M2Share.ErrorMessage(e.StackTrace);
            }
            try
            {
                if (m_boSuperMan)
                {
                    m_WAbil.HP = m_WAbil.MaxHP;
                    m_WAbil.MP = m_WAbil.MaxMP;
                }
                int dwC = (HUtil32.GetTickCount() - m_dwHPMPTick) / 20;
                m_dwHPMPTick = HUtil32.GetTickCount();
                m_nHealthTick += dwC;
                m_nSpellTick += dwC;
                if (!m_boDeath)
                {
                    int n18;
                    // POIS-18 — native natural HP regen @0x76B769-0x76B7AE:
                    //   76B769  81 7E 10 2C 01 00 00  cmp  [esi+0x10], 0x12C   ; budget >= 300
                    //   76B770  7C 40                 jl   0x76B7B2
                    //   76B772  33 C0 / 89 46 10      mov  [esi+0x10], 0
                    //   76B77D  8B 43 48              mov  eax, [ebx+0x48]     ; HP
                    //   76B780  85 C0 / 7E 2E         test eax,eax / jle skip  ; HP > 0 required
                    //   76B784  3B 43 4C / 7D 29      cmp  eax,[ebx+0x4C] / jge skip
                    //   76B789  8B 86 B0 02 00 00     mov  eax, [esi+0x2B0]    ; MaxHP
                    //   76B78F  B9 4B 00 00 00        mov  ecx, 0x4B           ; 75
                    //   76B795  F7 F9 / 8B D0 / 42    idiv ecx; edx = eax + 1
                    //   76B79E  E8 11 E6 FF FF        call 0x769DB4            ; IncHealthSpell(n,0)
                    // The `test eax,eax / jle` gate was missing: an actor sitting at 0 HP
                    // could be healed back up on the tick the budget matured and so never
                    // reach the HP == 0 death poll below. The MP branch has no such gate
                    // natively and keeps only the MP < MaxMP compare.
                    // Going through IncHealthSpell also picks up the bodyState 0x66 halving
                    // at 0x769DD1, which the inline add skipped; IncHealthSpell clamps to
                    // MaxHP and raises HealthSpellChanged itself (0x769E3C call 0x7693E8).
                    if ((m_WAbil.HP > 0) && (m_WAbil.HP < m_WAbil.MaxHP) && (m_nHealthTick >= M2Share.g_Config.nHealthFillTime))
                    {
                        n18 = m_WAbil.MaxHP / 75 + 1;
                        IncHealthSpell(n18, 0);
                    }
                    if ((m_WAbil.MP < m_WAbil.MaxMP) && (m_nSpellTick >= M2Share.g_Config.nSpellFillTime))
                    {
                        n18 = m_WAbil.MaxMP / 18 + 1;
                        if ((long)m_WAbil.MP + n18 < m_WAbil.MaxMP)
                        {
                            m_WAbil.MP += n18;
                        }
                        else
                        {
                            m_WAbil.MP = m_WAbil.MaxMP;
                        }
                        HealthSpellChanged();
                    }
                    if (m_WAbil.HP == 0)
                    {
                        TryNativeRevive();
                        if (m_WAbil.HP == 0)
                        {
                            // 眼神「被击杀触发」的桩体改写 0x766624 的 `8B 45 FC 8B 10`
                            // ——即 `call [vmt+0x84]`(Die) 的两条实参装载——重放后 jmp
                            // 0x766629，所以 @MyKill 发在 Die 之前。三道原生门（死者是
                            // TPlayer、m_ExpHitter 非空且是 TPlayer）在 FireMyKill 内。
                            GameSvr.Plugins.YanshenTriggerDispatch.FireMyKill(this);
                            Die();
                        }
                    }
                    if (m_nHealthTick >= M2Share.g_Config.nHealthFillTime)
                    {
                        m_nHealthTick = 0;
                    }
                    if (m_nSpellTick >= M2Share.g_Config.nSpellFillTime)
                    {
                        m_nSpellTick = 0;
                    }
                }
                else
                {
                    // 战神 TCreature.Run 的死亡分支 @0x766674..0x76669A —— 尸体存留
                    // 时间只由 word[obj+0x38] 决定，既不读 MonGen.dwZenTime 也不读
                    // 任何配置项：
                    //   007665FD  80 78 74 00           cmp   byte [eax+0x74],0   ; m_boDeath
                    //   00766601  75 71                 jne   0x766674
                    //   00766674  8B 45 FC              mov   eax,[ebp-4]
                    //   00766677  8B D6                 mov   edx,esi             ; esi=GetTickCount @0x76658A
                    //   00766679  2B 90 30 03 00 00     sub   edx,[eax+0x330]     ; - m_dwDeathTick
                    //   00766682  0F BF 40 38           movsx eax,word [eax+0x38] ; 尸体秒数
                    //   00766686  69 C0 E8 03 00 00     imul  eax,eax,1000
                    //   0076668C  3B D0 / 72 0F         cmp   edx,eax / jb 0x76669F
                    //   0076669A  E8 C1 19 00 00        call  0x768060            ; TCreature.MarkDelete
                    // 三条独立旁证钉死这里没有第二套计时：
                    //   (1) 全镜像 `movsx r32,word [reg+0x38]` 只有 0x766682 一条；
                    //   (2) 镜像里根本没有 "MakeGhostTime" / "ZenTime" 串（同区段的
                    //       "Setup" 等 ini 键都是明文可搜的），战神没有这个配置键；
                    //   (3) 其余 now-m_dwDeathTick→MakeGhost 组合全是硬编码常量且属
                    //       派生类自己的 Run（0x66A66E 2000ms、0x66B7ED 5000ms、
                    //       0x66C8D1 2000ms、0x68A06C 60000ms），不经过本基类分支。
                    // 原先此处的 _MAX(10s, dwZenTime-20s) 截断到 g_Config.dwMakeGhostTime
                    // 来自 LOMCN/GameOfMir 的 Delphi 血统（ObjBase.pas 只有 dwMakeGhostTime
                    // 那一支，MonGen 那一支是上游 C# 端加的），不是战神二进制的行为。
                    if (NativeCorpseGhostDue(HUtil32.GetTickCount()))
                    {
                        MakeGhost();
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg1);
                M2Share.ErrorMessage(e.Message);
            }
            try
            {
                if (!m_boDeath && ((m_nIncSpell > 0) || (m_nIncHealth > 0) || (m_nIncHealing > 0)))
                {
                    int dwInChsTime = 600 - HUtil32._MIN(400, m_Abil.Level * 10);
                    if (((HUtil32.GetTickCount() - m_dwIncHealthSpellTick) >= dwInChsTime) && !m_boDeath)
                    {
                        int dwC = HUtil32._MIN(200, HUtil32.GetTickCount() - m_dwIncHealthSpellTick - dwInChsTime);
                        m_dwIncHealthSpellTick = HUtil32.GetTickCount() + dwC;
                        if ((m_nIncSpell > 0) || (m_nIncHealth > 0) || (m_nPerHealing > 0))
                        {
                            if (m_sbHealthSpellRecoveryStep < 1)
                            {
                                m_sbHealthSpellRecoveryStep = 1;
                            }
                            if (m_nPerHealing <= 0)
                            {
                                m_nPerHealing = 1;
                            }
                            int nHP;
                            int recoveryStep = m_sbHealthSpellRecoveryStep;
                            if (m_nIncHealth < recoveryStep)
                            {
                                nHP = m_nIncHealth;
                                m_nIncHealth = 0;
                            }
                            else
                            {
                                nHP = recoveryStep;
                                m_nIncHealth -= recoveryStep;
                            }
                            int nMP;
                            if (m_nIncSpell < recoveryStep)
                            {
                                nMP = m_nIncSpell;
                                m_nIncSpell = 0;
                            }
                            else
                            {
                                nMP = recoveryStep;
                                m_nIncSpell -= recoveryStep;
                            }
                            if (m_nIncHealing < m_nPerHealing)
                            {
                                nHP += m_nIncHealing;
                                m_nIncHealing = 0;
                            }
                            else
                            {
                                nHP += m_nPerHealing;
                                m_nIncHealing -= m_nPerHealing;
                            }
                            m_sbHealthSpellRecoveryStep = unchecked(
                                (sbyte)(m_Abil.Level / 10 + 10));
                            m_nPerHealing = 5;
                            IncHealthSpell(nHP, nMP);
                            if (m_WAbil.HP == m_WAbil.MaxHP)
                            {
                                m_nIncHealth = 0;
                                m_nIncHealing = 0;
                            }
                            if (m_WAbil.MP == m_WAbil.MaxMP)
                            {
                                m_nIncSpell = 0;
                            }
                        }
                    }
                }
                else
                {
                    m_dwIncHealthSpellTick = HUtil32.GetTickCount();
                }
                if ((m_nHealthTick < -M2Share.g_Config.nHealthFillTime) && (m_WAbil.HP > 1))
                {
                    m_WAbil.HP -= 1;
                    m_nHealthTick += M2Share.g_Config.nHealthFillTime;
                    HealthSpellChanged();
                }
                
                bool boNeedRecalc = false;
                if (m_WAbil.HP > m_WAbil.MaxHP)
                {
                    boNeedRecalc = true;
                    m_WAbil.HP = Math.Max(0, m_WAbil.MaxHP - 1);
                }
                if (m_WAbil.MP > m_WAbil.MaxMP)
                {
                    boNeedRecalc = true;
                    m_WAbil.MP = Math.Max(0, m_WAbil.MaxMP - 1);
                }
                if (boNeedRecalc)
                {
                    HealthSpellChanged();
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg2 + " " + ex.Message);
            }

            try
            {
                if (m_UseItems.Length >= Grobal2.U_CHARM && m_UseItems[Grobal2.U_CHARM] != null)
                {
                    if (!m_boDeath && new ArrayList(new byte[] { Grobal2.RC_PLAYOBJECT, Grobal2.RC_PLAYCLONE }).Contains(m_btRaceServer))
                    {
                        int nCount;
                        int dCount;
                        int bCount;
                        GoodItem StdItem;
                        
                        if ((m_nIncHealth == 0) && (m_UseItems[Grobal2.U_CHARM].wIndex > 0) && ((HUtil32.GetTickCount() - m_nIncHPStoneTime) > M2Share.g_Config.HPStoneIntervalTime) && (((long)m_WAbil.HP * 100 / m_WAbil.MaxHP) < M2Share.g_Config.HPStoneStartRate))
                        {
                            m_nIncHPStoneTime = HUtil32.GetTickCount();
                            StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_CHARM].wIndex);
                            if ((StdItem.StdMode == 7) && new ArrayList(new byte[] { 1, 3 }).Contains(StdItem.Shape))
                            {
                                nCount = m_UseItems[Grobal2.U_CHARM].Dura * 10;
                                bCount = Convert.ToInt32(nCount / M2Share.g_Config.HPStoneAddRate);
                                dCount = m_WAbil.MaxHP - m_WAbil.HP;
                                if (dCount > bCount)
                                {
                                    dCount = bCount;
                                }
                                if (nCount > dCount)
                                {
                                    m_nIncHealth += dCount;
                                    m_UseItems[Grobal2.U_CHARM].Dura -= (ushort)HUtil32.Round(dCount / 10);
                                }
                                else
                                {
                                    nCount = 0;
                                    m_nIncHealth += nCount;
                                    m_UseItems[Grobal2.U_CHARM].Dura = 0;
                                }
                                if (m_UseItems[Grobal2.U_CHARM].Dura >= 1000)
                                {
                                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_CHARM, m_UseItems[Grobal2.U_CHARM].Dura, m_UseItems[Grobal2.U_CHARM].DuraMax, 0, "");
                                    }
                                }
                                else
                                {
                                    m_UseItems[Grobal2.U_CHARM].Dura = 0;
                                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        (this as TPlayObject).SendDelItems(m_UseItems[Grobal2.U_CHARM]);
                                    }
                                    m_UseItems[Grobal2.U_CHARM].wIndex = 0;
                                }
                            }
                        }
                        
                        if ((m_nIncSpell == 0) && (m_UseItems[Grobal2.U_CHARM].wIndex > 0) && ((HUtil32.GetTickCount() - m_nIncMPStoneTime) > M2Share.g_Config.MPStoneIntervalTime) && (((long)m_WAbil.MP * 100 / m_WAbil.MaxMP) < M2Share.g_Config.MPStoneStartRate))
                        {
                            m_nIncMPStoneTime = HUtil32.GetTickCount();
                            StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_CHARM].wIndex);
                            if ((StdItem.StdMode == 7) && new ArrayList(new byte[] { 2, 3 }).Contains(StdItem.Shape))
                            {
                                nCount = m_UseItems[Grobal2.U_CHARM].Dura * 10;
                                bCount = Convert.ToInt32(nCount / M2Share.g_Config.MPStoneAddRate);
                                dCount = m_WAbil.MaxMP - m_WAbil.MP;
                                if (dCount > bCount)
                                {
                                    dCount = bCount;
                                }
                                if (nCount > dCount)
                                {
                                    
                                    m_nIncSpell += dCount;
                                    m_UseItems[Grobal2.U_CHARM].Dura -= (ushort)HUtil32.Round(dCount / 10);
                                }
                                else
                                {
                                    nCount = 0;
                                    m_nIncSpell += nCount;
                                    m_UseItems[Grobal2.U_CHARM].Dura = 0;
                                }
                                if (m_UseItems[Grobal2.U_CHARM].Dura >= 1000)
                                {
                                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_CHARM, m_UseItems[Grobal2.U_CHARM].Dura, m_UseItems[Grobal2.U_CHARM].DuraMax, 0, "");
                                    }
                                }
                                else
                                {
                                    m_UseItems[Grobal2.U_CHARM].Dura = 0;
                                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        (this as TPlayObject).SendDelItems(m_UseItems[Grobal2.U_CHARM]);
                                    }
                                    m_UseItems[Grobal2.U_CHARM].wIndex = 0;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                M2Share.ErrorMessage(sExceptionMsg7);
            }
            
            
            try
            {
                if (m_TargetCret != null)
                {
                    
                    if (((HUtil32.GetTickCount() - m_dwTargetFocusTick) > 30000) || m_TargetCret.m_boDeath || m_TargetCret.m_boGhost || (m_TargetCret.m_PEnvir != m_PEnvir) || (Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) > 15) || (Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) > 15))
                    {
                        m_TargetCret = null;
                    }
                }
                if (m_LastHiter != null)
                {
                    if (((HUtil32.GetTickCount() - m_LastHiterTick) > 30000) || m_LastHiter.m_boDeath || m_LastHiter.m_boGhost)
                    {
                        m_LastHiter = null;
                    }
                }
                if (m_ExpHitter != null)
                {
                    if (((HUtil32.GetTickCount() - m_ExpHitterTick) > 6000) || m_ExpHitter.m_boDeath || m_ExpHitter.m_boGhost)
                    {
                        m_ExpHitter = null;
                    }
                }
                if (m_Master != null)
                {
                    m_boNoItem = true;
                    
                    int nInteger;
                    if (m_boAutoChangeColor && (HUtil32.GetTickCount() - m_dwAutoChangeColorTick > M2Share.g_Config.dwBBMonAutoChangeColorTime))
                    {
                        m_dwAutoChangeColorTick = HUtil32.GetTickCount();
                        switch (m_nAutoChangeIdx)
                        {
                            case 0:
                                nInteger = Grobal2.STATE_TRANSPARENT;
                                break;
                            case 1:
                                nInteger = Grobal2.POISON_STONE;
                                break;
                            case 2:
                                nInteger = Grobal2.POISON_DONTMOVE;
                                break;
                            case 3:
                                nInteger = Grobal2.POISON_68;
                                break;
                            case 4:
                                nInteger = Grobal2.POISON_DECHEALTH;
                                break;
                            case 5:
                                nInteger = Grobal2.POISON_LOCKSPELL;
                                break;
                            case 6:
                                nInteger = Grobal2.POISON_DAMAGEARMOR;
                                break;
                            default:
                                m_nAutoChangeIdx = 0;
                                nInteger = Grobal2.STATE_TRANSPARENT;
                                break;
                        }
                        m_nAutoChangeIdx++;
                        m_nCharStatus = (int)((m_nCharStatusEx & 0x1FFFFF) | ((0x80000000 >> nInteger) | 0));
                        StatusChanged();
                    }
                    if (m_boFixColor && (m_nFixStatus != m_nCharStatus))
                    {
                        switch (m_nFixColorIdx)
                        {
                            case 0:
                                nInteger = Grobal2.STATE_TRANSPARENT;
                                break;
                            case 1:
                                nInteger = Grobal2.POISON_STONE;
                                break;
                            case 2:
                                nInteger = Grobal2.POISON_DONTMOVE;
                                break;
                            case 3:
                                nInteger = Grobal2.POISON_68;
                                break;
                            case 4:
                                nInteger = Grobal2.POISON_DECHEALTH;
                                break;
                            case 5:
                                nInteger = Grobal2.POISON_LOCKSPELL;
                                break;
                            case 6:
                                nInteger = Grobal2.POISON_DAMAGEARMOR;
                                break;
                            default:
                                m_nFixColorIdx = 0;
                                nInteger = Grobal2.STATE_TRANSPARENT;
                                break;
                        }
                        m_nCharStatus = (int)((m_nCharStatusEx & 0x1FFFFF) | ((0x80000000 >> nInteger) | 0));
                        m_nFixStatus = m_nCharStatus;
                        StatusChanged();
                    }

                    // 英雄不走这段"通用怪物奴隶"主人死亡/消失处理。原版把英雄的【通用 master 槽】
                    // 钉死为 NULL：THeroAct / TWarHero / TTaosHero / TMagHero 四张 VMT 的
                    // +0x154(sub_690B08) 与 +0x158(sub_690B1C) 字面都是
                    //   690B0C  xor ebx,ebx
                    //   690B0E  mov [eax+0x38C],ebx      ; 通用 m_Master := NULL
                    // 所以任何以 [+0x38C]!=0 为门的块（本块的原版对应物在 TAnimal.Run
                    // sub_71E50C @0x71E594）对英雄永不成立。英雄的主人是另一个字段
                    // [hero+0x68C]，其"主人不在了"处理在 THeroAct.Run sub_689FDC
                    // @0x68A018-0x68A071：判主人 **ghost**(不是死亡)、**无 1000ms 延时**、
                    // 且走 sub_768060(MarkDelete=变 ghost) 而不是把 HP 归零。
                    // 见 HeroObject.RunNativeMasterGoneReap 与
                    // staging/herobehaviour_fix_20260804.md。
                    // 不加这道门的后果（活线 bug）：主人一死，英雄 1 秒后 HP=0；
                    // boMasterDieMutiny 开启时英雄还会被"叛变"成敌对宠物并放大 DC。
                    if (!(this is HeroObject))
                    {
                        if (m_Master.m_boDeath && ((HUtil32.GetTickCount() - m_Master.m_dwDeathTick) > 1000))
                        {
                            if (M2Share.g_Config.boMasterDieMutiny && (m_Master.m_LastHiter != null) && (M2Share.RandomNumber.Random(M2Share.g_Config.nMasterDieMutinyRate) == 0))
                            {
                                m_Master = null;
                                m_btSlaveExpLevel = (byte)M2Share.g_Config.SlaveColor.GetUpperBound(0);
                                RecalcAbilitys();
                                m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC) * M2Share.g_Config.nMasterDieMutinyPower, HUtil32.HiWord(m_WAbil.DC) * M2Share.g_Config.nMasterDieMutinyPower);
                                m_nWalkSpeed = m_nWalkSpeed / M2Share.g_Config.nMasterDieMutinySpeed;
                                RefNameColor();
                                RefShowName();
                            }
                            else
                            {
                                m_WAbil.HP = 0;
                            }
                        }
                        if (m_Master != null && m_Master.m_boGhost && ((HUtil32.GetTickCount() - m_Master.m_dwGhostTick) > 1000))
                        {
                            MakeGhost();
                        }
                    }
                }

                if (this is TPlayObject playObject)
                    playObject.m_nNativeSwitchOffsetD3C = 0;

                for (var i = m_SlaveList.Count - 1; i >= 0; i--)
                {
                    if (m_SlaveList[i].m_boDeath || m_SlaveList[i].m_boGhost || (m_SlaveList[i].m_Master != this))
                    {
                        // sub_6B3993: TList.Get [player+0x4FC] then SM 4470 (0x6F78B4)
                        // before the slot is dropped.
                        NotifyNativeSlaveListChanged(joining: false, m_SlaveList[i]);
                        m_SlaveList.RemoveAt(i);
                    }
                }
                if (m_boHolySeize && ((HUtil32.GetTickCount() - m_dwHolySeizeTick) > m_dwHolySeizeInterval))
                {
                    BreakHolySeizeMode();
                }
                if (m_boCrazyMode && ((HUtil32.GetTickCount() - m_dwCrazyModeTick) > m_dwCrazyModeInterval))
                {
                    BreakCrazyMode();
                }
                if (m_boShowHP && ((HUtil32.GetTickCount() - m_dwShowHPTick) > m_dwShowHPInterval))
                {
                    BreakOpenHealth();
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg3 + " " + ex.Message);
            }
            try
            {

                if ((HUtil32.GetTickCount() - m_dwDecPkPointTick) > M2Share.g_Config.dwDecPkPointTime)// 120000
                {
                    m_dwDecPkPointTick = HUtil32.GetTickCount();
                    if (m_nPkPoint > 0)
                    {
                        DecPKPoint(M2Share.g_Config.nDecPkPointCount);
                    }
                }
                if ((HUtil32.GetTickCount() - m_DecLightItemDrugTick) > M2Share.g_Config.dwDecLightItemDrugTime)
                {
                    m_DecLightItemDrugTick += M2Share.g_Config.dwDecLightItemDrugTime;
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        UseLamp();
                        CheckPKStatus();
                    }
                }
                if ((HUtil32.GetTickCount() - m_dwCheckRoyaltyTick) > 10000)
                {
                    m_dwCheckRoyaltyTick = HUtil32.GetTickCount();
                    // 英雄不参与"通用怪物奴隶忠诚度"。原版把英雄的【通用 master 槽】钉死为 NULL——
                    // THeroAct 的 VMT +0x154/+0x158(sub_690B08/sub_690B1C)字面就是
                    // `xor ebx,ebx; mov [eax+0x38C],ebx`,所以下面这段在原版对英雄永不成立。
                    // C# 复用同一个 m_Master 字段兼作英雄↔主人绑定(HeroObject 有 9 处读、FindMaster
                    // 优先用它),故不能照抄"置 NULL",改为在入口排除英雄=等价且不破坏绑定。
                    // 不修的后果(活线 bug):m_dwMasterRoyaltyTick 默认 0 → `GetTickCount() > 0` 永真
                    // → 英雄生成后 10 秒内被摘主人且 m_WAbil.HP /= 10,全英雄全会话必中。
                    // 证据:staging/discovery_heropet_20260803.md #1。
                    if (this is HeroObject)
                    {
                        // 原版英雄走 THeroAct 专用路径(主人死亡等待 60s 等),不走此通用块。
                    }
                    else if (m_Master != null)
                    {
                        if ((M2Share.g_dwSpiritMutinyTick > HUtil32.GetTickCount()) && (m_btSlaveExpLevel < 5))
                        {
                            m_dwMasterRoyaltyTick = 0;
                        }
                        if (HUtil32.GetTickCount() > m_dwMasterRoyaltyTick)
                        {
                            ExpireNativeSlaveRoyalty();
                        }
                        if (m_dwMasterTick != 0)
                        {
                            if ((HUtil32.GetTickCount() - m_dwMasterTick) > 12 * 60 * 60 * 1000)
                            {
                                m_WAbil.HP = 0;
                            }
                        }
                    }
                }

                if ((HUtil32.GetTickCount() - m_dwVerifyTick) > 30 * 1000)
                {
                    m_dwVerifyTick = HUtil32.GetTickCount();

                    // 这里曾经有第三处自造的组队拆解：每 30 秒把「队长已死或已 ghost」
                    // 的 m_GroupOwner 置空，再把队长名单里所有 dead/ghost 成员删掉。
                    // 原生没有这道扫描，两条独立证据：
                    //   ① 清组指针只有 sub_6C3200（6C3278 mov [ebx+0xA80],0 /
                    //      6C3280 mov [ebx+0xA7C],0），全镜像 3 个 E8 调用者 ——
                    //      0x6B3C26（BLACKROOM 图 [map+0x7C] 门）与 0x726F64 / 0x72716E
                    //      （都在 TGroup.DelMember sub_726E68 内部），没有一个是 tick 扫描。
                    //   ② 同一个 30 秒块的原生对应体 0x6B3B54 `cmp edx,0x7530` 只清
                    //      [self+0xBAC]（0x6B3B87），0x6B3BB2 只清 [self+0x18A8]，
                    //      都是单个对端指针；两处都没碰 [self+0xA80] 或槽数组 [group+0x48]。
                    // 原生的做法是「保留成员、在每个消费点按 IsDead/ghost 过滤」：
                    //   经验收集 726C84 call 0x772DA8（只测 IsDead）、
                    //   GroupSetV 727790 cmp [eax+0x73],0、
                    //   GroupFly 6CECFD/6CED06、同图计数 727A9D/727AA6。
                    // 这道扫描一直在抵消「死亡不退组」那条修复：死者 30 秒内照样掉队。

                    // 战神 sub_6B3EAC @0x6B3B71-0x6B3B87：`eax=ebx(m_DealCreat)` ; `call sub_772DA8`
                    //   （= m_boDeath getter `mov al,[eax+0x74]`）; `test al,al` / `jne 清零` ;
                    //   **`cmp byte ptr [ebx+0x73], 0` / `je 跳过`**（= m_boGhost 析取项）; 清零 [self+0xBAC]。
                    // 即原生在 **ghost 或 death** 任一为真时都清 m_DealCreat；旧 C# 只查 ghost，
                    // 于是一个「已死但尚未 ghost」的对端会把 m_DealCreat 一直挂着 ——
                    // 配合 ClientDealEnd 曾缺失的存活门，这就是「和尸体成交」的具体路径。
                    // （节流：原生用专属 tick 字段 [self+0x73C] 比 0x7530=30000ms；此处所在的
                    //  `m_dwVerifyTick` 块周期同为 30*1000ms，只是字段合并，周期等价，
                    //  不影响可观察行为。）
                    if ((m_DealCreat != null) && (m_DealCreat.m_boGhost || m_DealCreat.m_boDeath))
                    {
                        m_DealCreat = null;
                    }
                    if (!m_boDenyRefStatus)
                    {
                        m_PEnvir.VerifyMapTime(m_nCurrX, m_nCurrY, this);// 刷新在地图上位置的时间
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg4);
                M2Share.ErrorMessage(e.Message);
            }
            try
            {
                // The once-per-second `m_wStatusTimeArr[i] -= 1` countdown that
                // used to sit here was the second half of the 4.18 dual
                // authority: it expired the legacy slots on its own clock while
                // the node list expired the same states on the native 500 ms
                // clock (0x772FE4 `sub eax,[ebx+0xE0] / cmp eax,0x1F4 / jb`).
                // The slots are now a view onto those nodes, so a countdown here
                // would double-decrement them. Expiry lives in
                // ProcessTimedAbilities, and the four per-slot side effects moved
                // to OnNativeTimedStateLost.
                bool boNeedRecalc = false;
                for (var i = m_wStatusArrValue.GetLowerBound(0); i <= m_wStatusArrValue.GetUpperBound(0); i++)
                {
                    if (m_wStatusArrValue[i] > 0)
                    {
                        if (HUtil32.GetTickCount() > m_dwStatusArrTimeOutTick[i])
                        {
                            m_wStatusArrValue[i] = 0;
                            boNeedRecalc = true;
                            switch (i)
                            {
                                case 0:
                                    if (!Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint("攻击力回复正常"))
                                        SysMsg("攻击力回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 1:
                                    if (!Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint("魔法力回复正常"))
                                        SysMsg("魔法力回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 2:
                                    if (!Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint("道术回复正常"))
                                        SysMsg("道术回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 3:
                                    if (!Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint("攻击速度回复正常"))
                                        SysMsg("攻击速度回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 4:
                                    if (!Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint("生命值回复正常"))
                                        SysMsg("生命值回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 5:
                                    if (!Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint("魔法值回复正常"))
                                        SysMsg("魔法值回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                            }
                        }
                    }
                }
                if (boNeedRecalc)
                {
                    RecalcAbilitys();
                    SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                }
            }
            catch (Exception)
            {
                M2Share.ErrorMessage(sExceptionMsg5);
            }
            try
            {
                // POIS-38: 战神 sub_76B6F0 中段 1000ms 独立节拍块(0x76B905..0x76BD33)在尾段
                // 2500ms 毒块(0x76BD39)之前结算 11 个额外毒系状态索引;传入本 Run 的 tick 快照,
                // 与下方 2500ms 闸同一时基。详见 TBaseObject.NativePoisonSecondTick.cs 字节表。
                ProcessNativePoisonSecondTick(HUtil32.GetTickCount());
                // POIS-05: 战神 sub_76B6F0 @0x76BD39: cmp eax,0x9C4 / 0x76BD3E: jb skip
                // jb = jump if below (unsigned <), so tick fires when elapsed >= 2500.
                // C# used strict >, missing boundary tick when elapsed == 2500.
                if ((HUtil32.GetTickCount() - m_dwPoisoningTick) >= M2Share.g_Config.dwPosionDecHealthTime)
                {
                    m_dwPoisoningTick = HUtil32.GetTickCount();
                    if (m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] > 0)
                    {
                        if (m_boAnimal)
                        {
                            m_nMeatQuality -= 1000;
                        }
                        // POIS-08 — the DamageHealth(m_btGreenPoisoningPoint + 1) that
                        // used to sit here was a second, parallel hit. Native runs one
                        // if/else-if chain (0x06 > 0x01 > 0x1C > 0x1F) that converges on
                        // 0x76BDF5 and calls [vmt+0x1B0] exactly once per tick:
                        //   76BD86  EB 6D                 jmp 0x76BDF5   ; tier 0x06 -> converge
                        //   76BDF5  83 7D F4 00 / 74 25   if (rec == nil) skip everything
                        //   76BE0C  FF 91 B0 01 00 00     call [ecx+0x1B0]  ; the only damage
                        // Now that MakePosion feeds the timed-ability layer, tier 0x1F
                        // carries the same green poison this block used to serve, so the
                        // resolver below is the single exit. Meat decay is unrelated to the
                        // damage call and stays on the legacy carrier.
                    }
                    // POIS-09/POIS-10 — 战神 sub_76B6F0 @0x76BD4F-0x76BE1C 在这个 2500ms
                    // 闸后服务四个 bodyState 档(0x06/0x01/0x1C/0x1F),是 if/else-if 链,
                    // 一个 tick 只取优先级最高的那一档,汇合于 0x76BDF5 后只打一次。
                    // 自 MakePosion 改走 AddTimedAbilityInternal 之后,绿毒(0x1F)也由这条
                    // 链服务,所以这里是本 tick 唯一的伤害出口 —— 详见
                    // TBaseObject.NativePoisonTick.cs 的字节表。
                    // 伤害由 rec.Value+1 得出,其中 0x06 = MIN(MaxHP,5000000)/100、
                    // 0x01 = 同上/30,每 tick 覆写节点值;0x1C/0x1F 用施法者给的量。
                    if (TryResolveNativePoisonTickDamage(out var nNativePoisonDamage)
                        && nNativePoisonDamage > 0)
                    {
                        DamageHealth(nNativePoisonDamage);
                        // 0x76BE12/0x76BE17 清两个回复预算(obj+0x10 / obj+0x14)。
                        m_nHealthTick = 0;
                        m_nSpellTick = 0;
                        HealthSpellChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg6 + " " + ex.Message);
            }
            try
            {
                ProcessNativeSkill153Shield(dwRunTick);
                ProcessTimedAbilities();
                ConsumeAbilityRecalcPending();
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg8 + " " + ex.Message);
            }
            M2Share.g_nBaseObjTimeMin = HUtil32.GetTickCount() - dwRunTick;
            lock (s_baseObjTimeLock)
            {
                if (M2Share.g_nBaseObjTimeMax < M2Share.g_nBaseObjTimeMin)
                {
                    M2Share.g_nBaseObjTimeMax = M2Share.g_nBaseObjTimeMin;
                }
            }
        }

        /// <summary>
        /// 战神 sub_71FA20 @0x71FA50 / @0x71FA6C.  The arm-and-test sits at the very
        /// top of @AfterScatterItems, ahead of both the drop-table gate (0x71FA8A) and
        /// the anti-fatigue ladder (0x71FAD7), and the store at 0x71FA6C is
        /// unconditional — a monster that goes on to scatter nothing still burns the
        /// flag, which is what stops the sibling consumer sub_71EC88 from re-running
        /// the same table.
        /// </summary>
        private bool TryEnterNativeScatter()
        {
            if (m_boNativeScatterConsumed) return false;
            m_boNativeScatterConsumed = true;
            return true;
        }

        /// <summary>
        /// 战神 <c>sub_71FA20</c> (@AfterScatterItems) @0x71FAD7-0x71FB19 — the three
        /// abort tests the whole scatter routine opens with.  Any one of them takes the
        /// 0x71FAF7 branch which ends in <c>jmp 0x720092</c>, and 0x720092 is the outer
        /// exception-frame exit that sits PAST the function's own <c>ret</c> at
        /// 0x7200B7 — so nothing after it runs:
        ///
        /// <code>
        /// 71FAB4  cmp dword [ebp-8],0        / je  0x71FB2E   ; killer nil -> no tier logic
        /// 71FACE  cmp byte [killer+0x178],0  / jne 0x71FB2E   ; non-player race -> ditto
        /// 71FAD7  mov ebx,[ebp-8]
        /// 71FADA  cmp byte [ebx+0x1828],3    / je  0x71FAF7   ; fatigue tier 3
        /// 71FAE3  cmp byte [ebx+0x1829],3    / je  0x71FAF7   ; cheat-penalty tier 3
        /// 71FAEC  mov eax,ebx / call 0x6D7788                 ; = TestStatus(killer,0x19)
        /// 71FAF3  test al,al                 / jne 0x71FAF7
        /// 71FB19  jmp 0x720092                                ; whole function exits
        /// </code>
        ///
        /// <c>sub_6D7788</c> is a one-line thunk: <c>mov dl,0x19 / call 0x772960</c>, i.e.
        /// active-state 25, so it maps to <see cref="HasNativeActiveState(int)"/> with 25.
        ///
        /// What the exit skips is exactly the four scatter segments (exclusive chain
        /// @0x71FB2E, the monster's own table @0x71FCFF, world drop @0x71FEA7, gold
        /// settlement @0x71FFAD) plus the @AfterScatterItems script callback at 0x720062.
        /// It does NOT cover DropUseItems / ScatterBagItems — those are separate native
        /// workers reached from the Die ladder, not from sub_71FA20.
        ///
        /// The same tier pair was already honoured on the mining path
        /// (TPlayObject.PileStones, native 0x6BC202 / 0x6BC21E) but the drop path had
        /// neither test, so a tier-3 account kept 100% of item drops and 100% of gold —
        /// the one economic sink the anti-fatigue / anti-cheat system has.
        ///
        /// The native abort branch additionally emits one log-service record through
        /// <c>sub_768BE0</c> -> <c>sub_79D3D8</c> (a 0xBC-byte body stamped with magic
        /// 0x33AABB77 and kind byte 0xA2).  The complete caller/codec chain fixes the
        /// arguments as itemName="被防沉迷", makeIndex=0x76ADF2, quantity=1, and
        /// reason="怪物爆出被防沉迷".  It is a game-data log, not an SM_* packet.
        /// </summary>
        private static bool NativeAfterScatterItemsBlocked(TBaseObject killer)
        {
            // 0x71FAB4 + 0x71FACE: only a non-nil, RC_PLAYOBJECT killer reaches the tests.
            if (killer == null || killer.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                return false;
            var blocked = killer is TPlayObject player
                && (player.m_btNativeFatigueTier == 3
                    || player.m_btNativeCheatPenaltyTier == 3);
            if (!blocked)
            {
                blocked = killer.HasNativeActiveState(25);
            }
            if (!blocked)
            {
                return false;
            }

            // 0x71FB03/0x71FB08/0x71FB0F: exact sub_768BE0 argument order.
            M2Share.AddNativeGameDataLog(
                killer, 0xA2, "被防沉迷", 0x0076ADF2, 1, "怪物爆出被防沉迷");
            return true;
        }

        public virtual void Die()
        {
            // 眼神「BB死亡触发」改写的是本函数的【序言】0x76631C `55 8B EC 53 56`，
            // 桩体先派发 @BBKill 再重放那五个字节并 jmp 0x766321 —— 也就是说它跑在
            // 死亡处理的最前面，早于任何早退门。四道原生门见 YanshenTriggerDispatch。
            GameSvr.Plugins.YanshenTriggerDispatch.FireSlaveDie(this);
            int tExp;
            const string sExceptionMsg1 = "[Exception] TBaseObject::Die 1";
            const string sExceptionMsg2 = "[Exception] TBaseObject::Die 2";
            const string sExceptionMsg3 = "[Exception] TBaseObject::Die 3";
            if (m_boSuperMan)
            {
                return;
            }
            if (m_boSupermanItem)
            {
                return;
            }

            m_boDeath = true;
            m_dwDeathTick = HUtil32.GetTickCount();
            // sub_76631C 在写完死亡位与死亡时间戳之后立刻派发地图 VMT+0x08：
            //   00766323  C6 43 74 01           mov byte [obj+0x74],1   ; m_boDeath
            //   0076632C  89 83 30 03 00 00     mov [obj+0x330],eax     ; m_dwDeathTick
            //   00766341  8B B3 28 01 00 00     mov esi,[obj+0x128]     ; PEnvir
            //   00766347  85 F6 / 74 09         test esi,esi / je       ; 无地图则跳过
            //   00766351  FF 51 08              call [map_vmt+0x08]
            // TEnvironment 的同槽实现 0x779F64 是裸 `C3`，只有 TDynEnvir 的 0x5FD4D4
            // 会在 0x5FD50A 派发 @OnDie —— 类门在 NativeDynEnvirObjectDiedTrigger 里。
            m_PEnvir?.NativeDynEnvirObjectDiedTrigger(this);
            // THeroAct keeps its owner at +0x68C, separate from TCreature's generic
            // master slot. C# currently folds both into m_Master, so preserve the
            // native inputs before the generic pet cleanup below clears the hiters.
            var nativeHeroDeathOwner = m_btRaceServer == Grobal2.RC_HEROOBJECT
                ? m_Master as TPlayObject
                : null;
            var nativeHeroDeathLastHiter = m_btRaceServer == Grobal2.RC_HEROOBJECT
                ? m_LastHiter
                : null;
            if (m_Master != null)
            {
                m_ExpHitter = null;
                m_LastHiter = null;
            }

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

            m_nIncSpell = 0;
            m_nIncHealth = 0;
            m_nIncHealing = 0;
            KillFunc();
            try
            {
                if (m_btRaceServer != Grobal2.RC_PLAYOBJECT && m_LastHiter != null)
                {
                    if (M2Share.g_Config.boMonSayMsg)
                    {
                        MonsterSayMsg(m_LastHiter, MonStatus.Die);
                    }
                    if (m_ExpHitter != null)
                    {
                        if (m_ExpHitter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            if (M2Share.g_FunctionNPC != null)
                            {
                                M2Share.g_FunctionNPC.GotoLable(m_ExpHitter as TPlayObject, "@PlayKillMob", false);
                            }
                            tExp = m_ExpHitter.CalcGetExp(m_Abil.Level, m_dwFightExp);
                            if (!M2Share.g_Config.boVentureServer)
                            {
                                if (m_ExpHitter.m_boAI)
                                {
                                    (m_ExpHitter as RobotPlayObject).GainExp(tExp);
                                }
                                else
                                {
                                    (m_ExpHitter as TPlayObject).GainExp(tExp);
                                }
                            }
                            
                            if (m_PEnvir.IsCheapStuff())
                            {
                                var killer = m_ExpHitter as TPlayObject;
                                if (killer != null)
                                {
                                    void ProcessQuest(TPlayObject player, bool grouped)
                                    {
                                        var questNpc = m_PEnvir.GetQuestNPC(player, m_sCharName, "", grouped) as Merchant;
                                        if (questNpc != null)
                                        {
                                            questNpc.Click(player);
                                        }
                                        M2Share.PasEngine?.ProcessMapQuestKill(
                                            m_PEnvir.sMapName, player, m_sCharName, grouped);
                                    }

                                    var killerProcessed = false;
                                    var groupMembers = killer.m_GroupOwner?.m_GroupMembers;
                                    if (groupMembers != null)
                                    {
                                        for (var i = 0; i < groupMembers.Count; i++)
                                        {
                                            var groupHuman = groupMembers[i];
                                            if (groupHuman == null || groupHuman.m_boDeath || groupHuman.m_boGhost ||
                                                killer.m_PEnvir != groupHuman.m_PEnvir ||
                                                Math.Abs(killer.m_nCurrX - groupHuman.m_nCurrX) > 12 ||
                                                Math.Abs(killer.m_nCurrY - groupHuman.m_nCurrY) > 12)
                                                continue;

                                            var isGroupedMember = !ReferenceEquals(killer, groupHuman);
                                            ProcessQuest(groupHuman, isGroupedMember);
                                            if (!isGroupedMember) killerProcessed = true;
                                        }
                                    }

                                    if (!killerProcessed)
                                    {
                                        ProcessQuest(killer, false);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (m_ExpHitter.m_Master != null)
                            {
                                m_ExpHitter.GainSlaveExp(m_Abil.Level);
                                tExp = m_ExpHitter.m_Master.CalcGetExp(m_Abil.Level, m_dwFightExp);
                                if (!M2Share.g_Config.boVentureServer)
                                {
                                    if (m_ExpHitter.m_Master.m_boAI)
                                    {
                                        (m_ExpHitter.m_Master as RobotPlayObject).GainExp(tExp);
                                    }
                                    else
                                    {
                                        (m_ExpHitter.m_Master as TPlayObject).GainExp(tExp);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            if (M2Share.g_FunctionNPC != null)
                            {
                                M2Share.g_FunctionNPC.GotoLable(m_LastHiter as TPlayObject, "@PlayKillMob", false);
                            }
                            tExp = m_LastHiter.CalcGetExp(m_Abil.Level, m_dwFightExp);
                            if (!M2Share.g_Config.boVentureServer)
                            {
                                if (m_LastHiter.m_boAI)
                                {
                                    (m_LastHiter as RobotPlayObject).GainExp(tExp);
                                }
                                else
                                {
                                    (m_LastHiter as TPlayObject).GainExp(tExp);
                                }
                            }
                        }
                    }
                }
                if (M2Share.g_Config.boMonSayMsg && m_btRaceServer == Grobal2.RC_PLAYOBJECT && m_LastHiter != null)
                {
                    m_LastHiter.MonsterSayMsg(this, MonStatus.KillHuman);
                }
                // THeroAct.Die keeps owner +0x68C alive through and after the shared
                // THumanKind death workers. Only generic creature masters detach here.
                if (m_btRaceServer != Grobal2.RC_HEROOBJECT)
                {
                    m_Master = null;
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg1);
                M2Share.ErrorMessage(e.Message);
            }
            try
            {
                var boPK = false;
                // PKD-05 —— 谋杀惩罚的入口门。战神 TPlayer.Die sub_6C07A0 @0x6C081A-0x6C0865:
                //   6C0823  85 DB / 74 6A                  test ebx,ebx / je   ; 无 LastHiter
                //   6C0830  80 78 5F 00 / 75 5B            cmp [Envir+0x5F],0  ; FREEPK  -> 跳过
                //   6C083F  80 78 5D 00 / 75 4C            cmp [Envir+0x5D],0  ; FIGHT   -> 跳过
                //   6C084E  80 78 5E 00 / 75 3D            cmp [Envir+0x5E],0  ; FIGHT3  -> 跳过
                //   6C0857  8B 80 60 01 00 00              mov eax,[victim+0x160]
                //   6C085D  8B 15 AC 5F 7D 00              mov edx,[0x7D5FAC]   ; -> 200
                //   6C0863  3B 02 / 7F 2A                  cmp eax,[edx] / jg   ; 受害者 PK > 200 -> 跳过
                // 两处订正:
                //  * FREEPK 这道门 C# 完全没有 —— 自由 PK 地图上杀人照样加 PK 值、照样掉幸运。
                //  * `jg` 只在 victimPK > 200 时跳过，所以门是 **victimPK <= 200**；
                //    C# 写的 PKLevel() < 2 == victimPK < 200，恰好在 PK == 200 这一点上少判一次
                //    （受害者 PK 正好 200 时原生仍然惩罚凶手，C# 放过）。
                // 原先这一行还有一个 `!M2Share.g_Config.boVentureServer` 全局门。
                // 原生 0x6C081A..0x6C0865 只有三个地图旗标字节加一个 PK 阈值，
                // 整段唯一的绝对地址读是 `0x6C085D 8B 15 AC 5F 7D 00 mov edx,[0x7D5FAC]`
                // （PK 阈值 200），没有任何形如 `cmp byte [0x7Dxxxx],0` 的全局开关读。
                // 全镜像多编码零命中：VentureServer / boVentureServer 在 GBK、
                // 裸 ASCII（大小写不敏感）、UTF-16LE 三路皆 0。按 §3.1 删除。
                if (!m_PEnvir.Flag.boFightZone
                    && !m_PEnvir.Flag.boFight3Zone && !m_PEnvir.Flag.boFREEPK)
                {
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT && m_LastHiter != null
                        && m_nPkPoint <= M2Share.g_Config.nPKPunishPoint)
                    {
                        // 原先这里还有一条 `|| m_LastHiter.m_btRaceServer == RC_NPC`
                        // 「允许 NPC 杀死人物」。原生 0x6C081A..0x6C088C 这段里**一条
                        // 种族比较都没有**，凶手身份完全由 `[LastHiter.vmt+0xB4]` 的
                        // 责任玩家解析决定：
                        //   6C0867  8B C3 / 8B 10 / FF 92 B4 00 00 00   call [vmt+0xB4]
                        //   6C0871  85 C0 / 74 1C                       nil    -> 不惩罚
                        //   6C0875  80 78 73 00 / 75 16                 幽灵   -> 不惩罚
                        //   6C087B  3B 45 FC / 74 11                    自杀   -> 不惩罚
                        // 该段全部 8 条比较是 `test ebx,ebx` / [+0x5F] / [+0x5D] / [+0x5E] /
                        // PK 阈值 / `test eax,eax` / [+0x73] / 自杀，没有 [+0x178]。
                        // 字节级零命中：`cmp byte [reg+0x178],0x0A`（race == RC_NPC=10）
                        // 全镜像只有 6 处（0x62E76D / 0x62E80F / 0x62EA9F / 0x6E1D8E /
                        // 0x6E8A82 / 0x6E9441），无一落在死亡链内。按 §3.1 删除。
                        if (m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            boPK = true;
                        }
                        if (m_LastHiter.m_Master != null)
                        {
                            if (m_LastHiter.m_Master.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                            {
                                m_LastHiter = m_LastHiter.m_Master;
                                boPK = true;
                            }
                        }
                    }
                }
                // PKD-06 —— 凶手解析后的两道守卫。战神 0x6C0867-0x6C087E：
                //   6C086B  FF 92 B4 00 00 00  call [killer.vmt+0xB4]   ; 责任玩家
                //   6C0871  85 C0 / 74 1C      test eax,eax / je        ; nil -> 不惩罚
                //   6C0875  80 78 73 00 / 75 16 cmp byte [eax+0x73],0 / jne ; **幽灵**凶手 -> 不惩罚
                //   6C087B  3B 45 FC / 74 11   cmp eax,[ebp-4] / je     ; 自杀 -> 不惩罚
                // +0x73 是 m_boGhost（全镜像唯一写入点 0x7680EF，在 MakeGhost 里），
                // 不是 m_boDeath(+0x74) —— 两份旧 discovery 文档在这一点上是反的。
                if (boPK && m_LastHiter != null
                    && !m_LastHiter.m_boGhost                       // 0x6C0875
                    && !ReferenceEquals(m_LastHiter, this))         // 0x6C087B
                {
                    // PKD-07 —— 幸运扣减是无条件的。战神 sub_6C0FE4 @0x6C1019:
                    //   6C1019  BA 0C FE FF FF  mov edx,0xFFFFFE0C   ; -500 原生单位 = -1 级
                    //   6C101E  8B C3           mov eax,ebx          ; 凶手
                    //   6C1020  E8 97 88 0A 00  call sub_7698BC
                    //   6C1025  C6 45 FE 00     mov byte [ebp-2],0   ; guildwarkill := 0 —— 在这之后
                    // 它排在行会战 / 攻城战 / 自由 PK / 正当防卫全部判定**之前**，
                    // 所以这四种情形下原生同样扣 1 点幸运。C# 之前把它塞在
                    // `!guildwarkill && !IsGoodKilling` 分支里，行会战杀人不掉幸运，
                    // 幸运又直接进装备掉落分母，属于可被利用的差异。
                    m_LastHiter.AddBodyLuck(-1);
                    var guildwarkill = false;
                    if (m_MyGuild != null && m_LastHiter.m_MyGuild != null)
                    {
                        if (GetGuildRelation(this, m_LastHiter) == 2)
                        {
                            guildwarkill = true;
                        }
                    }
                    var Castle = M2Share.CastleManager.InCastleWarArea(this);
                    if (Castle != null && Castle.m_boUnderWar || m_boInFreePKArea)
                    {
                        guildwarkill = true;
                    }
                    if (!guildwarkill)
                    {
                        if ((M2Share.g_Config.boKillHumanWinLevel || M2Share.g_Config.boKillHumanWinExp || m_PEnvir.Flag.boPKWINLEVEL || m_PEnvir.Flag.boPKWINEXP) && m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            (this as TPlayObject).PKDie(m_LastHiter as TPlayObject);
                        }
                        else
                        {
                            if (!m_LastHiter.IsGoodKilling(this))
                            {
                                m_LastHiter.IncPkPoint(M2Share.g_Config.nKillHumanAddPKPoint);
                                m_LastHiter.SysMsg(M2Share.g_sYouMurderedMsg, MsgColor.Red, MsgType.Hint);
                                SysMsg(format(M2Share.g_sYouKilledByMsg, m_LastHiter.m_sCharName), MsgColor.Red, MsgType.Hint);
                                // AddBodyLuck(-1) 已上提到 0x6C1019 的位置（见 PKD-07）。
                                if (PKLevel() < 1)
                                {
                                    if (M2Share.RandomNumber.Random(5) == 0)
                                    {
                                        m_LastHiter.MakeWeaponUnlock();
                                    }
                                }
                            }
                            else
                            {
                                m_LastHiter.SysMsg(M2Share.g_sYouProtectedByLawOfDefense, MsgColor.Green, MsgType.Hint);
                            }
                        }
                        
                        if (m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            if (m_LastHiter.m_dwPKDieLostExp > 0)
                            {
                                if (m_Abil.Exp >= m_LastHiter.m_dwPKDieLostExp)
                                {
                                    m_Abil.Exp -= (short)m_LastHiter.m_dwPKDieLostExp;
                                }
                                else
                                {
                                    m_Abil.Exp = 0;
                                }
                            }
                            if (m_LastHiter.m_nPKDieLostLevel > 0)
                            {
                                if (m_Abil.Level >= m_LastHiter.m_nPKDieLostLevel)
                                {
                                    m_Abil.Level -= (ushort)m_LastHiter.m_nPKDieLostLevel;
                                }
                                else
                                {
                                    m_Abil.Level = 0;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg2);
            }
            try
            {
                // 战神 THumanKind.Die = sub_741368 @0x7413F6-0x741496 is the WHOLE native
                // death-drop policy, and it runs for PLAYERS and HEROES only (exhaustive
                // E8 caller sweep of sub_741368 = exactly 2 sites: 0x6C07D8 in TPlayer.Die
                // sub_6C07A0 and 0x687125 in THeroAct.Die sub_686E10).  Monster Die is the
                // separate sub_71E2BC, which has no FIGHT / FIGHT3 / safe-zone test at all,
                // so monsters keep the pre-existing C# gate below untouched.
                //
                // The leg C# was missing is the third one:
                //   741402  mov eax,[ebp-4] / call sub_76858C   ; InSafeZone(self)
                //   74140C  je 0x74142C                         ; NOT safe -> drop
                //                                               ; safe     -> arbitration,
                //                                               ;   whose default leaf is
                //                                               ;   [vmt+0x21C] = empty.
                // C# had `!m_boAnimal` in that slot instead — an invented term — so a
                // player killed inside a safe zone (guard kill, script damage, poison)
                // scattered their whole bag onto the town floor.  See NativeDeathDropPolicy.
                var nativeHumanKindDeath = m_btRaceServer == Grobal2.RC_PLAYOBJECT
                                        || m_btRaceServer == Grobal2.RC_HEROOBJECT;
                try
                {
                var deathDropOutcome = NativeDeathDropPolicy.Outcome.NormalEquipThenBag;
                if (nativeHumanKindDeath)
                {
                    deathDropOutcome = NativeDeathDropPolicy.Resolve(m_PEnvir.Flag, InNativeSafeZone12());
                }
                var deathDropsAnything = nativeHumanKindDeath
                    ? deathDropOutcome != NativeDeathDropPolicy.Outcome.DropNothing
                    : !m_PEnvir.Flag.boFightZone && !m_PEnvir.Flag.boFight3Zone && !m_boAnimal;

                // 0x74143E / 0x74144E: each special flag selects its OWN exclusive worker
                // (sub_740300 / sub_748D48) instead of the normal sub_73FC70 + sub_740078
                // pair. sub_740300 is now closed independently: it scans the bag backwards,
                // accepts only TSpecialDropItem (StdMode 96), performs sub_78BCBC's signed
                // percentage roll, then preserves the native auth/gift destruction or
                // radius-2 ground transaction and the final 0x27A4 deletion batch.
                // sub_748D48 uses the separately loaded exact-name Rnd/Ranger table and
                // attempts only fixed-radius-2 ground placement for matching bag items.
                if (deathDropOutcome == NativeDeathDropPolicy.Outcome.OnlyDropSpecWorker)
                {
                    NativeSpecialDropBagItems();
                    deathDropsAnything = false;
                }
                else if (deathDropOutcome == NativeDeathDropPolicy.Outcome.LimitBagItemDropWorker)
                {
                    NativeLimitBagItemDropItems();
                    deathDropsAnything = false;
                }

                if (deathDropsAnything)
                {
                    var AttackBaseObject = m_ExpHitter;
                    if (m_ExpHitter != null && m_ExpHitter.m_Master != null)
                    {
                        AttackBaseObject = m_ExpHitter.m_Master;
                    }
                    if (m_btRaceServer == Grobal2.RC_HEROOBJECT)
                    {
                        // THeroAct.Die 0x687125 enters the same THumanKind worker pair as
                        // TPlayer.Die: equipment sub_73FC70 first, then bag sub_740078.
                        NativeHeroDropUseItems(nativeHeroDeathLastHiter,
                            nativeHeroDeathOwner);
                        NativeHeroScatterBagItems(nativeHeroDeathOwner);
                    }
                    else if (m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                    {
                        var scatteredItems = new List<KeyValuePair<string, string>>();
                        // 0x71E3B7 is the ONE gate that covers both siblings, because it
                        // sits in monster Die one level above the virtual dispatch:
                        //   71E3B7  80 B8 7D 04 00 00 00  cmp byte [eax+0x47D],0
                        //   71E3BE  75 35                 jne 0x71E3F5   ; skips the call
                        //   71E3C4  6A 00 / 6A 01         push 0 / push 1
                        //   71E3D2  FF 96 FC 01 00 00     call [esi+0x1FC]  -> sub_71F46C
                        // Everything else lives inside sub_71FA20 and therefore reaches
                        // only the second sibling.
                        var nativeDieDropSuppressed = m_boNoItem;
                        var scatterBlocked = NativeDropControlRuntime.RunInNativeOrder(
                            // sub_71F46C @0x71F47E `E8 F5 0D 00 00 call 0x720278`.
                            // The drop-control dispatcher is sub_71FA20's SIBLING and
                            // runs before it, so none of the three sub_71FA20 gates
                            // apply.  It also gets no scatter log: sub_72016C takes
                            // three register arguments and ends on a bare `ret`
                            // (0x720274) — eax=pending list, edx=item creator,
                            // ecx=dropper — while the TStringList that feeds
                            // @AfterScatterItems is not constructed until 0x71FA9E,
                            // long after 0x720278 has returned.
                            controlledDrop: () =>
                            {
                                if (!nativeDieDropSuppressed)
                                {
                                    NativeDropControlRuntime.TryScatter(this,
                                        AttackBaseObject, null);
                                }
                            },
                            // sub_71F46C @0x71F491 `E8 8A 05 00 00 call 0x71FA20`.
                            // All three exits land on 0x720092, which is past the
                            // @AfterScatterItems callback at 0x720062, so one boolean
                            // covers segments 1-4 and the callback alike.  Order
                            // matters: 0x71FA50 runs before 0x71FA8A and 0x71FAD7 and
                            // arms unconditionally, so TryEnterNativeScatter is
                            // leftmost — except for m_boNoItem, which precedes the
                            // whole dispatch and so keeps 0x71FA6C from ever firing.
                            //
                            //   71FA8A  83 B8 74 04 00 00 00  cmp dword [eax+0x474],0
                            //   71FA91  0F 84 FB 05 00 00     je 0x720092
                            //
                            // A monster with no drop table leaves the function before
                            // segment 1, so the exclusive chain, the world drop and the
                            // gold settlement never run for it either.  A null
                            // UserEngine fails closed because the segments would fault
                            // on it anyway.
                            ordinaryBlocked: () => nativeDieDropSuppressed
                                || !TryEnterNativeScatter()
                                || M2Share.UserEngine == null
                                || !M2Share.UserEngine.NativeHasMonsterDropTable(m_sCharName)
                                || NativeAfterScatterItemsBlocked(AttackBaseObject),
                            ordinaryDrop: () =>
                            {
                                // 战神 sub_71FA20 segment 1, 0x71FB2E-0x71FCFF: the
                                // MonItemsTree exclusive chain runs FIRST, before the
                                // monster's own drop table at 0x71FCFF.
                                M2Share.UserEngine.TraverseMonItemsTree(m_sCharName,
                                    AttackBaseObject, this, scatteredItems);
                                if (this is not HeroObject)
                                {
                                    M2Share.UserEngine.MonGetRandomItems(this,
                                        AttackBaseObject);
                                }
                            });
                        if (m_Master == null && (!m_boNoItem || !m_PEnvir.Flag.boNODROPITEM))
                        {
                            // 半径取原生段2 的硬编码 3（0x71FDCF / 0x71FE46），不是配置值：
                            // 这条腿散的就是 MonGetRandomItems 刚攒进 m_ItemList 的自有掉落表
                            // 物件，原生在段2 循环里逐件就地落地。
                            ScatterBagItems(AttackBaseObject, scatteredItems,
                                NativeMonsterOwnTableScatterRange);
                        }
                        // 战神 sub_71FA20 @0x71FFAD `cmp dword [ebp-0x14],0 / jle
                        // 0x720049` is the whole entry condition for the gold
                        // settlement — one test against the accumulator, which
                        // ScatterGolds already makes as `m_nGold > 0`.  The race /
                        // pet / map-flag terms that used to sit here have no
                        // counterpart in either sub_71FA20 or monster Die sub_71E2BC
                        // (which reads no map flag at all), and they left the gold of
                        // anything below RC_ANIMAL, and of every pet, stranded in
                        // m_nGold forever.  m_boNoItem moved up to scatterBlocked,
                        // where 0x71E3B7 puts it.
                        if (!scatterBlocked)
                        {
                            // 战神 sub_71FA20 段3「世界掉落」0x71FEA7-0x71FFA7，夹在
                            // 段2 的落地循环与 0x71FFAD 的金币结算之间，同受那三道门
                            // 约束（三条失败臂都跳 0x720092）。查表走单例
                            // [0x7D71F4] -> sub_752CAC，与掉落控制 sub_720278 无关。
                            NativeWorldScatter.Scatter(this, scatteredItems);
                            ScatterGolds(AttackBaseObject, scatteredItems,
                                nativeMonsterScatter: true);
                        }

                        if (!scatterBlocked && AttackBaseObject is TPlayObject player && !player.m_boGhost)
                        {
                            M2Share.PasEngine?.TryCallAfterScatterItems(this, player, scatteredItems);
                        }
                    }
                    else
                    {
                        if (!m_boNoItem || !m_PEnvir.Flag.boNODROPITEM)//允许设置 m_boNoItem 后人物死亡不掉物品
                        {
                            if (AttackBaseObject != null)
                            {
                                if (M2Share.g_Config.boKillByHumanDropUseItem && AttackBaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT || M2Share.g_Config.boKillByMonstDropUseItem && AttackBaseObject.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                                {
                                    DropUseItems(null);
                                }
                            }
                            else
                            {
                                DropUseItems(null);
                            }
                            if (M2Share.g_Config.boDieScatterBag)
                            {
                                ScatterBagItems(null);
                            }
                            // 玩家死亡原生不掉金币，所以这里原先的 `boDieDropGold ->
                            // ScatterGolds(null)` 已删除。判据是三条独立的：
                            // ① 掉金币到地上的例程是 sub_768AAC（怪物结算 sub_71FA20
                            //    @0x72000A `E8 9D 8A 04 00` 调它，之前是 0xBB8 上钳、
                            //    idiv、0x7D0 分堆）。它全镜像只有 6 个 E8 调用者
                            //    ——0x64E74A / 0x64E765 / 0x64F5C0 / 0x64F5DB / 0x6C30F9 /
                            //    0x72000A，且 0 个字面 dword 引用（非虚派发）。
                            //    0x6C30F9 是玩家手动丢金币那条短函数（0x6C30E1 与
                            //    0x6C3102 两处 `29 B3 5C 01 00 00 sub [ebx+0x15C],esi`，
                            //    0x6C3112 ret），与死亡无关。**六个里没有一个在死亡链上。**
                            // ② 金币字段 [obj+0x15C] 的位移字节 `5C 01 00 00` 全镜像出现
                            //    103 次，落在 TPlayer.Die(sub_6C07A0) / THumanKind.Die
                            //    (sub_741368) / sub_73FC70 / sub_740078 里的是 **0** 次。
                            // ③ 策略梯 sub_741368 的三条出口在 0x741498 汇合，之后只有
                            //    0x7414DB `[self+0x37C] := 0` 和 0x741514 `dx=0x2725`
                            //    (10021) 一个包，没有任何金币动作。
                            // 配置名 DieDropGold / boDieDropGold 在 GBK、裸 ASCII
                            // （大小写不敏感）、UTF-16LE 三路皆 0 命中。按 §3.1 删除。
                        }
                    }
                }
                }
                catch (Exception ex) when (nativeHumanKindDeath)
                {
                    M2Share.ErrorMessage("[Exception]: THumanKind.Die -1: "
                        + ex.Message);
                }
                // 战神 sub_6C07A0 @0x6C07EE-0x6C0815 — the luck penalty is a SIBLING of the
                // drop policy, not a child of it.  TPlayer.Die calls sub_741368 (the drop
                // ladder) first at 0x6C07D8 and only THEN tests the map:
                //   6C07F4  cmp byte [eax+0x5D],0 / jne 0x6C081A   ; FIGHT  -> skip
                //   6C07FA  cmp byte [eax+0x5E],0 / jne 0x6C081A   ; FIGHT3 -> skip
                //   6C0800  mov edx,1   / call sub_6D2928          ; glory -= 1
                //   6C080D  mov edx,0x1F4 / call sub_7698BC        ; AddBodyLuck(500 native
                //                                                  ;   units => +1 after the
                //                                                  ;   /500.0 + round)
                // There is NO safe-zone term here — dying in town still costs luck — so it
                // must NOT be nested inside the drop gate.  It was, which is why moving the
                // safe-zone leg into that gate would otherwise have silently cancelled the
                // luck penalty for every town death.
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT
                    && !m_PEnvir.Flag.boFightZone && !m_PEnvir.Flag.boFight3Zone)
                {
                    // 原版 sub_6C07A0(死亡/被杀处理): [+0x164] += 1。
                    // 原 -(50-(50-Level*5)) == -(Level*5) 系伪造(方向与量级均错，原生为固定 +1)。
                    AddBodyLuck(1);
                }
                // 战神 sub_6C07A0 @0x6C0891-0x6C08B9 — an ARCHER-GUARD kill knocks a red
                // name back to yellow.  This whole branch was absent from C#, so red-name
                // players killed by guards stayed red forever and the core PK loop had no
                // sink at all:
                //   6C0894  mov eax,[eax+0x354]            ; m_LastHiter
                //   6C089A  test eax,eax / je 0x6C08C3
                //   6C089E  cmp byte [eax+0x178],0x70      ; race == 112 = RC_ARCHERGUARD
                //   6C08A5  jne 0x6C08C3
                //   6C08AA  cmp dword [eax+0x160],0xC8     ; own MyPKpoint >= 200
                //   6C08B4  jl 0x6C08C3
                //   6C08B9  mov dword [eax+0x160],0x64     ; MyPKpoint := 100
                // Three details the bytes pin down and a paraphrase would get wrong:
                //  * the threshold is the IMMEDIATE 0xC8, not the configurable global
                //    [[0x7D5FAC]] that the murder-penalty gate at 0x6C0863 reads — native
                //    is inconsistent between the two sites and this one is hardcoded;
                //  * it is an ASSIGNMENT to 100, not a subtraction, so PK 5000 -> 100;
                //  * it sits AFTER the FIGHT/FIGHT3 gate closes at 0x6C0891, so it applies
                //    on every player death regardless of map flags or safe zone.
                // 0x6C08B9 is a RAW FIELD STORE (`mov dword [eax+0x160],0x64`), not a call
                // to the PK-point mutator sub_6CCB0C — so native sends NO name-colour
                // refresh packet here and the client only re-colours on its next update.
                // Routing this through DecPKPoint would add a 10046 packet native does not
                // send, so the store is written directly to match.
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT
                    && m_LastHiter != null
                    && m_LastHiter.m_btRaceServer == Grobal2.RC_ARCHERGUARD
                    && m_nPkPoint >= 200)
                {
                    m_nPkPoint = 100;
                }
                string tStr;
                if (m_PEnvir.Flag.boFight3Zone)
                {
                    m_nFightZoneDieCount++;
                    if (m_MyGuild != null)
                    {
                        m_MyGuild.TeamFightWhoDead(m_sCharName);
                    }
                    if (m_LastHiter != null)
                    {
                        if (m_LastHiter.m_MyGuild != null && m_MyGuild != null)
                        {
                            m_LastHiter.m_MyGuild.TeamFightWhoWinPoint(m_LastHiter.m_sCharName, m_Abil.Level);
                            tStr = m_LastHiter.m_MyGuild.sGuildName + ':' + m_LastHiter.m_MyGuild.nContestPoint + "  " + m_MyGuild.sGuildName + ':' + m_MyGuild.nContestPoint;
                            M2Share.UserEngine.CryCry(Grobal2.RM_CRY, m_PEnvir, m_nCurrX, m_nCurrY, 1000, M2Share.g_Config.btCryMsgFColor, M2Share.g_Config.btCryMsgBColor, "- " + tStr);
                        }
                    }
                }
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    // 战神死亡不退组。sub_726E68 (group.DelMember) 全镜像只有两处
                    // E8：0x6C3181（CM_GROUPMODE 关组，非队长自退）和 0x6C3D73
                    // （CM_DELGROUPMEMBER）。经验收集轮 0x726C84 call 0x772DA8
                    // 只跳过 IsDead([+0x74])，尸体仍留在队里，复活后还在。
                    // 旧 C# 死亡立刻 DelMember 是 INVENTED，玩家每次死亡都要重新组队。
                    if (m_LastHiter != null)
                    {
                        if (m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            tStr = m_LastHiter.m_sCharName;
                        }
                        else
                        {
                            tStr = '#' + m_LastHiter.m_sCharName;
                        }
                    }
                    else
                    {
                        // PKD-10 —— 无凶手时的占位符是 **五** 个 '#'。战神 0x6C0936
                        // `B9 FC 09 6C 00  mov ecx,0x6C09FC`，而 0x6C09FC 处的 Delphi 长串
                        // 长度前缀（VA-4 的 dword）读出来是 5，内容 '#####'。
                        // C# 写 4 个会让日志消费端按固定宽度切分时整行错位。
                        tStr = "#####";
                    }
                    M2Share.AddGameDataLog("19" + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + "FZ-" + HUtil32.BoolToIntStr(m_PEnvir.Flag.boFightZone) + "_F3-" + HUtil32.BoolToIntStr(m_PEnvir.Flag.boFight3Zone) + "\t" + '0' + "\t" + '1' + "\t" + tStr);
                }
                // Death ≠ logout. TPlayer.Die 0x6C07A0 → THumanKind.Die 0x741368
                // (`0x74138A E8 8D 4F 02 00 call 0x76631C`) → TCreature.Die:
                //   0x766323  C6 43 74 01     mov byte [ebx+0x74], 1   ; m_boDeath, not ghost
                //   0x766351  FF 51 08        call [map.vmt+8]         ; 0x5FD4D4
                // 0x5FD4D4 does NOT touch map+0xD8 (HumCount). That counter is only
                // inc/dec on AddToMap / DeleteFromMap when race==0 (0x5FD559 / 0x5FD592).
                // Ghost is a different byte: 0x7680EF C6 43 73 01 in MakeGhost.
                // DelObjectCount + m_boDelFormMaped here mixed death with leave-map
                // accounting and made corpses vanish from HumCount until logout.
                m_dwSearchTick = 0;
                SendRefMsg(Grobal2.RM_DEATH, m_btDirection, m_nCurrX, m_nCurrY, 1, "");
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg3);
            }
        }

        internal virtual void ReAlive()
        {
            m_boDeath = false;

            SendRefMsg(Grobal2.RM_ALIVE, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
        }

        protected virtual bool IsProtectTarget(TBaseObject BaseObject)
        {
            var result = true;
            if (BaseObject == null)
            {
                return result;
            }
            if (InSafeZone() || BaseObject.InSafeZone())
            {
                result = false;
            }
            if (!BaseObject.m_boInFreePKArea)
            {
                // 战神 sub_6C175C 从 0x6C17B1 的免战门到 0x6C182C 的三秒门之间**只有两条**
                // 等级梯，就是下面这两段：
                //   6C17BA  A1 AC 5F 7D 00 / 8B 00      eax := [[0x7D5FAC]] = 200
                //   6C17C1  3B 83 60 01 00 00 / 7F 2A   cmp eax,[self+0x160] / jg  -> 第二梯
                //   6C17C9  66 83 BB 78 02 00 00 14 / 76 20  self.Level <= 20      -> 第二梯
                //   6C17D3  66 83 BF 78 02 00 00 14 / 77 16  target.Level > 20     -> 第二梯
                //   6C17DD  3B 02 / 7D 06                    target.PK >= 200      -> 第二梯
                //   6C17ED  C6 45 FF 00                      受保护
                //   6C17F3  66 83 BB 78 02 00 00 14 / 77 2F  self.Level > 20       -> 三秒门
                //   6C1804  3B 83 60 01 00 00 / 7E 20        self.PK >= 200        -> 三秒门
                //   6C180C  3B 02 / 7C 10                    target.PK < 200       -> 三秒门
                //   6C181C  66 83 BF 78 02 00 00 14 / 76 06  target.Level <= 20    -> 三秒门
                //   6C1826  C6 45 FF 00                      受保护
                // 原先这里还有一段 `boPKLevelProtect` / `nPKProtectLevel` 的「新人保护」，
                // 在原生无任何对应：`sub_6C175C` 全函数 255 字节里没有第三条梯，
                // 也从不读 `[+0x4B9]`（m_boPKFlag）。全镜像多编码零命中已确认那两个配置名
                // 不存在（GBK / 裸 ASCII 大小写不敏感 / UTF-16LE 三路皆 0）。按 §3.1 删除。
                if (PKLevel() >= 2 && m_Abil.Level > M2Share.g_Config.nRedPKProtectLevel)
                {
                    if (BaseObject.m_Abil.Level <= M2Share.g_Config.nRedPKProtectLevel && BaseObject.PKLevel() < 2)
                    {
                        result = false;
                        return result;
                    }
                }
                
                if (m_Abil.Level <= M2Share.g_Config.nRedPKProtectLevel && PKLevel() < 2)
                {
                    if (BaseObject.PKLevel() >= 2 && BaseObject.m_Abil.Level > M2Share.g_Config.nRedPKProtectLevel)
                    {
                        result = false;
                        return result;
                    }
                }
                if (((HUtil32.GetTickCount() - m_dwMapMoveTick) < 3000) || ((HUtil32.GetTickCount() - BaseObject.m_dwMapMoveTick) < 3000))
                {
                    result = false;
                }
            }
            return result;
        }

        protected virtual void ProcessSayMsg(string sMsg)
        {
            string sCharName;
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                sCharName = m_sCharName;
            }
            else
            {
                sCharName = M2Share.FilterShowName(m_sCharName);
            }
            SendRefMsg(Grobal2.RM_HEAR, 0, M2Share.g_Config.btHearMsgFColor, M2Share.g_Config.btHearMsgBColor, 0, sCharName + ':' + sMsg);
        }

        /// <summary>
        /// 战神 <c>TCreature.MarkDelete sub_768060</c>（身份由自带异常串
        /// <c>'[Exception]: TCreature.MarkDelete Cret的地图无效'</c> @0x768138 坐实）。
        /// 前半段 0x76807B..0x7680E4 只是两条地图有效性日志（<c>m_PEnvir</c> 为空 /
        /// <c>[envir+0x44]</c> 为 0），不改任何状态；真正的状态迁移是这四条：
        ///   0x7680E9  80 7B 73 00        cmp byte [ebx+0x73],0    ; 已是 ghost 则整段跳过
        ///   0x7680ED  75 18              jne 0x768107
        ///   0x7680EF  C6 43 73 01        mov byte [ebx+0x73],1    ; m_boGhost，无条件
        ///   0x7680F3  E8 48 02 CA FF     call 0x408340            ; GetTickCount
        ///   0x7680F8  89 83 4C 01 00 00  mov [ebx+0x14c],eax      ; m_dwGhostTick
        ///   0x7680FE  33 D2 / 8B C3 / E8 AD 00 00 00  xor edx,edx / mov eax,ebx / call 0x7681B4
        /// <c>sub_7681B4</c> = <c>DisappearA</c>：<c>DeleteFromMap</c>（0x7681D7 call 0x7794A8）
        /// 成功才发 <c>RM_DISAPPEAR</c>（0x7681EE <c>mov dx,0x1E</c> → <c>call [vmt+0xE0]</c>）。
        ///
        /// 【无 m_boCanReAlive 分叉】全函数 0x768060..0x76812E 只有 0x7680E9 这一个字节测试，
        /// 读的是 <c>+0x73</c> 自身（幂等门），不读任何"可复活"标志；<c>m_boGhost</c> 是无条件写入。
        /// 三条独立旁证：
        ///   (1) 刷怪工厂 <c>sub_67C9E0</c> 落地新怪时只写 <c>word[obj+0x38]</c>（尸体秒数，
        ///       0x67CA56 <c>mov [eax+0x38],dx</c>），【不写】任何 can-realive 标志、
        ///       【不写】<c>m_pMonGen</c> 回指针 —— 原生怪物身上根本没有这个状态位；
        ///   (2) 逐怪 tick 循环 <c>ProcessMon sub_67C150</c> 的 CertList 遍历
        ///       （0x67C354..0x67C4A8）每只怪只有三个出口：槽为 null 跳过（0x67C381）/
        ///       <c>cmp byte [obj+0x73],0 / jne</c> 走回收臂（0x67C38E，NULL 槽 + 入延迟释放
        ///       FIFO + <c>[vmt+0x7C]</c>）/ 否则到点 <c>Run</c>。判据只有 <c>+0x73</c>，
        ///       没有"复活"臂 —— 补怪是工厂重新造一只，不是把旧对象救活；
        ///   (3) 两份 Delphi 参考的 <c>TBaseObject.MakeGhost</c> 同样是三行无分叉
        ///       （<c>staging/ref-MIR2/GameOfMir/M2Server/ObjBase.pas:20510</c>、
        ///        <c>staging/ref-MirServer-Delphi/EM2Engine/ObjBase.pas:18605</c>）。
        ///
        /// 原先这里的 <c>if (m_boCanReAlive) m_boInvisible = true;</c> 来自上游 LyoMir2 的 C#
        /// （<c>staging/upstream-LyoMir2/GameSvr/Actors/TBaseObject.Base.cs:1117</c>），
        /// 战神与两份 Delphi 三方皆无 —— 属 INVENTED。它配套的"原地复活"消费端
        /// （<c>ReAliveEx</c>）已于 2026-08-03 按字节删除，只剩这半截分叉：由于
        /// <c>m_boCanReAlive</c> 恰好对【所有】刷怪点怪物为真（UsrEngn.cs:3410），
        /// 走这条臂的怪永远拿不到 <c>m_boGhost</c>，而回收循环（UsrEngn.cs:1803）唯一判据
        /// 就是 <c>m_boGhost</c> —— 尸体永不入延迟释放 FIFO、CertList 槽永不腾出、
        /// 刷怪点永不补怪。
        /// </summary>
        public virtual void MakeGhost()
        {
            m_boGhost = true;
            m_dwGhostTick = HUtil32.GetTickCount();
            RemoveFromMapForGhost();
            SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
        }

        private void RemoveFromMapForGhost()
        {
            var environment = m_PEnvir;
            if (environment == null)
                return;

            var removed = environment.DeleteFromMap(m_nCurrX, m_nCurrY,
                CellType.OS_MOVINGOBJECT, this);
            if (removed != 1 && CountsAsPlayerPresence)
            {
                environment.RemoveMovingObjectRegistration(this, true);
            }
        }

        
        
        
        
        // 半径常量与全镜像 sub_7688A0 调用点取值全表见
        // TBaseObject.NativeScatterRange.cs。
        protected virtual void ScatterBagItems(TBaseObject ItemOfCreat)
        {
            ScatterBagItems(ItemOfCreat, null, NativePlayerDeathScatterRange);
        }

        private void ScatterBagItems(TBaseObject ItemOfCreat,
            IList<KeyValuePair<string, string>> scatteredItems, int DropWide)
        {
            TUserItem UserItem;
            GoodItem StdItem;
            const string sExceptionMsg = "[Exception] TBaseObject::ScatterBagItems";
            try
            {
                if ((m_btRaceServer == Grobal2.RC_PLAYCLONE) && (m_Master != null))
                {
                    return;
                }
                for (var i = m_ItemList.Count - 1; i >= 0; i--)
                {
                    UserItem = m_ItemList[i];
                    StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    var boCanNotDrop = false;
                    if (StdItem != null)
                    {
                        TMonDrop MonDrop = null;
                        if (M2Share.g_MonDropLimitLIst.TryGetValue(StdItem.Name, out MonDrop))
                        {
                            if (MonDrop.nDropCount < MonDrop.nCountLimit)
                            {
                                MonDrop.nDropCount++;
                                M2Share.g_MonDropLimitLIst[StdItem.Name] = MonDrop;
                            }
                            else
                            {
                                MonDrop.nNoDropCount++;
                                boCanNotDrop = true;
                            }
                            // ⚠️ UNVERIFIED —— 来源=GameOfMir 参考分支(非战神),仅算术形态线索,未经战神字节验证。
                            // 原引用(保留,勿删): ObjBase.pas:18724 —— 该 break 只结束【掉落限制表的内层扫描】。
                            // C# 用 TryGetValue 取代了那层扫描，所以不再需要 break；此前的 break 退出了
                            // 【外层背包循环】，导致命中限制表的物品之后的所有背包物品被静默跳过(该掉的不掉)。
                            // 该修正的方向可信(C# 侧已无内层循环,break 的作用域必然错),但"原版 break 只终止
                            // 内层"这句仍只有 ref 结论支撑。战神取证状态: 掉落散包 ScatterBagItems 的战神本体
                            // 未 dump(2026-08-03 的 discovery_itemlifecycle 只覆盖了玩家 CM_DROPITEM sub_73CC98,
                            // 不是怪物散包)。列入活风险清单(物品, 中)。
                        }
                    }
                    if (boCanNotDrop)
                    {
                        continue;
                    }
                    var itemName = ItmUnit.GetItemName(UserItem);
                    if (DropItemDown(UserItem, DropWide, true, ItemOfCreat, this))
                    {
                        scatteredItems?.Add(new KeyValuePair<string, string>(itemName, "1"));
                        Dispose(UserItem);
                        m_ItemList.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
        }

        protected virtual void DropUseItems(TBaseObject BaseObject)
        {
            DropUseItems(BaseObject, null);
        }

        private void DropUseItems(TBaseObject BaseObject,
            IList<KeyValuePair<string, string>> scatteredItems)
        {
            if (BaseObject == null) return;
            int nC;
            int nRate;
            GoodItem StdItem;
            IList<TDeleteItem> DropItemList = null;
            const string sExceptionMsg = "[Exception] TBaseObject::DropUseItems";
            try
            {
                // 这里原先也有一个 `if (m_boNoDropUseItem) return;` —— 与 PKD-14 删掉的
                // 玩家侧那一个是同一处 INVENTED 的另一半（英雄 / 怪物走的是这条基类实现）。
                // sub_73FC70 的序言到 0x73FCB6 那条 `7D 09 jge` 之间没有任何条件跳转，
                // 上游 sub_741368 的策略梯只读六个地图旗标字节。NoDropUseItem /
                // boNoDropUseItem / DropUseItem 全镜像 GBK、裸 ASCII（大小写不敏感）、
                // UTF-16LE 三路皆 0 命中。按 §3.1 删除。
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    nC = 0;
                    while (true)
                    {
                        if (m_UseItems[nC] == null)
                        {
                            nC++;
                            continue;
                        }
                        StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[nC].wIndex);
                        if (StdItem != null)
                        {
                            if ((StdItem.Reserved & 8) != 0)
                            {
                                if (DropItemList == null)
                                {
                                    DropItemList = new List<TDeleteItem>();
                                }
                                DropItemList.Add(new TDeleteItem()
                                {
                                    MakeIndex = m_UseItems[nC].MakeIndex,
                                    ClientItemID = (this as TPlayObject)?.EnsureClientItemId(m_UseItems[nC]) ?? m_UseItems[nC].ClientItemID
                                });
                                if (StdItem.NeedIdentify == 1)
                                {
                                    M2Share.AddGameDataLog("16" + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + m_UseItems[nC].MakeIndex + "\t" + HUtil32.BoolToIntStr(m_btRaceServer == Grobal2.RC_PLAYOBJECT) + "\t" + '0');
                                }
                                m_UseItems[nC].wIndex = 0;
                            }
                        }
                        nC++;
                        if (nC >= 9)
                        {
                            break;
                        }
                    }
                }
                // 分母与玩家侧同一条 sub_73FC70：红名 21、非红 [self+0x18C]+90，再减
                // THumanKind 凶手的 [+0x579]，下钳 0。红名判据是 0x73FCB6 的 `jge`，
                // 即**严格** MyPKpoint > nPKPunishPoint；这里原先写的 `PKLevel() > 2`
                // 等价于 PK >= 300，把 201..299 整段判成非红。硬编码的 15/30 同样无原生
                // 依据（见 TBaseObject.NativeDeathDropDenominator.cs 的文件头）。
                nRate = NativeDeathEquipDropDenominator(
                    m_nPkPoint > M2Share.g_Config.nPKPunishPoint, m_LastHiter);
                // Heroes share THumanKind.Die -> sub_73FC70 with players. The
                // plugin rewrite is a process-wide patch, so HeroObject must
                // honour it too. Monsters do not enter this worker natively.
                var dropCount = 0;
                var nativeEquipDropCount = 0;
                var deathDropPatched = false;
                var patchedCap = 2;
                var processedEquipSlot = false;
                var nativeDropNoticeCount = 0;
                if (this is HeroObject)
                {
                    deathDropPatched = new YanshenApi(null, null, M2Share.PluginManager)
                        .TryGetDeathEquipDropPatch(
                            m_nPkPoint > M2Share.g_Config.nPKPunishPoint,   // 0x73FCB6 jge
                            out var patchedRate, out patchedCap);
                    if (deathDropPatched) nRate = patchedRate;
                }
                nC = 0;
                // 战神 sub_73FC70 @0x73FDAF-0x73FEC9 — the auth + gift DESTROY branch for
                // EQUIPPED gear (the high-value tier), absent from C# until now:
                //   0x73FDAF  cmp byte [esi+0x178],0 / jne 0x73FECE  ; non-player -> normal drop
                //   0x73FDC3  mov cl,4 / call sub_617A38            ; authenticated?
                //   0x73FDCC  test al,al / je 0x73FDDD              ; NOT authed -> destroy
                //   0x73FDD0  cmp byte [edi+0xD8],0 / je 0x73FECE   ; authed + not gift -> drop
                //   0x73FE68  mov edx,0x740030                      ; "未验证物品消失(死亡)"
                //   0x73FE7E  mov edx,0x740050                      ; "赠送物品消失(死亡)"
                //   0x73FEBD  call sub_768BE0(dx=0x5E) / 0x73FEC4 call sub_404690 (Free)
                // Note this path's two literals differ from the bag path's: no comma in
                // the unverified line and 赠送 rather than 赠品 (verified byte-for-byte).
                var equipAuthenticated = NativeItemDropDestroyAuthenticated();
                var equipIsPlayerRace = m_btRaceServer == Grobal2.RC_PLAYOBJECT;
                while (true)
                {
                    if (m_UseItems[nC] != null
                        && NativeItemDropDestroy.ShouldDestroy(equipIsPlayerRace,
                            equipAuthenticated, m_UseItems[nC]))
                    {
                        var destroyed = m_UseItems[nC];
                        var destroyedStdItem = M2Share.UserEngine.GetStdItem(
                            destroyed.wIndex);
                        // 0x73FDDD..0x73FDFE: this destruction arm is reachable
                        // only for std[+2]&0x10, then sub_78389C mode 5 may keep
                        // the item. Both exits advance without dropping/freeing.
                        if (destroyedStdItem == null
                            || (destroyedStdItem.NativeReserved02 & 0x0010) == 0
                            || NativeItemDropDestroy.CheckTransferPermission(
                                destroyed, destroyedStdItem,
                                NativeItemDropDestroy.TransferModeDrop) != 0)
                        {
                            nC++;
                            if (nC >= 9) break;
                            continue;
                        }
                        var notice = NativeItemDropDestroy.BuildDestroyNotice(
                            equipAuthenticated, destroyed,
                            NativeItemDropDestroy.DeathEquipUnverifiedNotice,
                            NativeItemDropDestroy.DeathEquipGiftNotice);
                        if (equipIsPlayerRace)
                        {
                            DropItemList ??= new List<TDeleteItem>();
                            DropItemList.Add(new TDeleteItem()
                            {
                                sItemName = M2Share.UserEngine.GetStdItemName(destroyed.wIndex),
                                MakeIndex = destroyed.MakeIndex,
                                ClientItemID = (this as TPlayObject)?.EnsureClientItemId(destroyed) ?? destroyed.ClientItemID
                            });
                        }
                        // ★ 原生缺陷，照抄，不要"顺手修好"（REPLICATION_RULES §3.1）★
                        // 装备销毁支**不清槽位**。sub_73FC70 的这条支路末尾是：
                        //   0x73FEB4  8B 4D 94 / 66 BA 5E 00 / 8B C6
                        //   0x73FEBD  E8 1E 8D 02 00   call sub_768BE0   ; 日志 dx=0x5E
                        //   0x73FEC2  8B C7            mov eax,edi       ; edi = 该 TUserItem
                        //   0x73FEC4  E8 C7 47 CC FF   call sub_404690   ; TObject.Free
                        //   0x73FEC9  E9 A1 00 00 00   jmp 0x73FF6F      ; -> inc ebx，下一格
                        // Free 之后直接跳到 `inc ebx`，[self+0x4C0] 的第 ebx 格仍然指向
                        // 已释放的对象 —— 这是真悬垂指针。
                        // 同函数另外两条支路都清槽，所以这不是我读漏了：
                        //   Reserved02&8 支 0x73FD86/0x73FD8C call sub_75F27C
                        //                 (0x75F2BB `89 54 83 08  mov [ebx+eax*4+8],0`)
                        //   落地支       0x73FF0B/0x73FF11 call sub_75F3E8
                        //                 (0x75F40F `89 54 86 08  mov [esi+eax*4+8],0`)
                        // 背包 worker 的同名销毁支也清：0x74019D `E8 8E 49 CE FF
                        // call sub_424B30` 先从 [self+0x508] 摘除，再在 0x74021E 才 Free。
                        // 唯独装备这一支漏了。
                        // 此处原先写 `m_UseItems[nC] = null;`，并引用 0x73FF0B 当依据——
                        // 那个 VA 属于**落地支**，不是这一支（§4.6 那类张冠李戴）。
                        // C# 里照抄的方式就是保留槽位引用：Dispose(obj) 本身是空操作
                        // （TBaseObject.cs `internal void Dispose(object obj) { obj = null; }`
                        // 只赋值形参），对象不会被回收也不会被复用，所以保留引用等价于
                        // 原生"槽位仍指向那块内存"，且不会引入别名/复制风险。
                        // 玩家可见后果：未验证 / 赠品装备在死亡时被判销毁、10148 包告诉
                        // 客户端它没了，但服务端这一格仍然装着它 —— 属性重算与存档都还算它。
                        // 原版就是这个样子。
                        if (!string.IsNullOrEmpty(notice))
                        {
                            SysMsg(notice + " "
                                + M2Share.UserEngine.GetStdItemName(destroyed.wIndex),
                                MsgColor.Red, MsgType.Hint);
                        }
                        Dispose(destroyed);                 // 0x73FEC4 sub_404690
                        nativeEquipDropCount++;
                        // native 0x73FE4E inc [ebp-0xc] then jmp 0x73FF6F (destroy skips cap)
                        if (deathDropPatched) dropCount++;
                        nC++;
                        if (nC >= 9) break;
                        continue;                           // 0x73FEC9 jmp 0x73FF6F
                    }
                    if (M2Share.RandomNumber.Random(nRate) == 0)
                    {
                        if (m_UseItems[nC] == null)
                        {
                            nC++;
                            continue;
                        }
                        var itemName = ItmUnit.GetItemName(m_UseItems[nC]);
                        if (DropItemDown(m_UseItems[nC], 2, true, BaseObject, this))
                        {
                            scatteredItems?.Add(new KeyValuePair<string, string>(itemName, "1"));
                            StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[nC].wIndex);
                            if (StdItem != null)
                            {
                                if ((StdItem.Reserved & 10) == 0)
                                {
                                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        if (DropItemList == null)
                                        {
                                            DropItemList = new List<TDeleteItem>();
                                        }
                                        DropItemList.Add(new TDeleteItem()
                                        {
                                            sItemName = M2Share.UserEngine.GetStdItemName(m_UseItems[nC].wIndex),
                                            MakeIndex = m_UseItems[nC].MakeIndex,
                                            ClientItemID = (this as TPlayObject)?.EnsureClientItemId(m_UseItems[nC]) ?? m_UseItems[nC].ClientItemID
                                        });
                                    }
                                    m_UseItems[nC].wIndex = 0;
                                }
                            }
                            // native 0x73FF66 inc [ebp-0xc] / 0x73FF69 cmp / jg exit
                            nativeEquipDropCount++;
                            if (deathDropPatched)
                            {
                                dropCount++;
                                if (dropCount > patchedCap) break;
                            }
                        }
                    }
                    nC++;
                    if (nC >= 9)
                    {
                        break;
                    }
                }
                if (DropItemList != null)
                {
                    SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                        DropItemList.Count, 0, 0, "", DropItemList);
                }

                // Player and hero use their exact THumanKind workers above. This legacy
                // base path has no proven VMT+B4 owner/bag contract, so it must not invent
                // the sub_73E4C4 notification for monsters or other actor classes.
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(ex.StackTrace);
            }
        }

        public virtual void SetTargetCreat(TBaseObject BaseObject)
        {
            m_TargetCret = BaseObject;
            m_dwTargetFocusTick = HUtil32.GetTickCount();
        }

        protected virtual void DelTargetCreat()
        {
            m_TargetCret = null;
        }

        public virtual bool IsProperFriend(TBaseObject BaseObject)
        {
            bool result = false;
            if (BaseObject == null)
            {
                return result;
            }
            if (m_btRaceServer >= Grobal2.RC_ANIMAL)
            {
                if (BaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                {
                    result = true;
                }
                if (BaseObject.m_Master != null)
                {
                    result = false;
                }
                return result;
            }
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                result = IsProperFriend_IsFriend(BaseObject);
                if (BaseObject.m_btRaceServer < Grobal2.RC_ANIMAL)
                {
                    return result;
                }
                if (BaseObject.m_Master == this)
                {
                    result = true;
                    return result;
                }
                if (BaseObject.m_Master != null)
                {
                    result = IsProperFriend_IsFriend(BaseObject.m_Master);
                    return result;
                }
            }
            else
            {
                result = true;
            }
            return result;
        }

        public virtual bool Operate(TProcessMessage ProcessMsg)
        {
            int nDamage;
            int nTargetX;
            int nTargetY;
            int nPower;
            int nRage;
            TBaseObject TargetBaseObject;
            const string sExceptionMsg = "[Exception] TBaseObject::Operate ";
            bool result = false;
            try
            {
                switch (ProcessMsg.wIdent)
                {
                    case Grobal2.RM_NATIVE_MAGIC_EFFECT:
                        ProcessNativeMagicEffectMessage(ProcessMsg);
                        break;
                    case Grobal2.RM_MAGSTRUCK:
                    case Grobal2.RM_MAGSTRUCK_MINE:
                        if ((ProcessMsg.wIdent == Grobal2.RM_MAGSTRUCK) && (m_btRaceServer >= Grobal2.RC_ANIMAL) && !bo2BF && (m_Abil.Level < 50))
                        {
                            m_dwWalkTick = m_dwWalkTick + 800 + M2Share.RandomNumber.Random(1000);
                        }
                        nDamage = GetMagStruckDamage(null, ProcessMsg.nParam1);
                        if (nDamage > 0)
                        {
                            // Message-driven landing. Native's equivalent
                            // (sub_766A70 @0x766BA6) reads the attacker out of
                            // the QUEUED MESSAGE RECORD: `mov ecx,[esi+0x24]`
                            // @0x766B9D. C#'s TProcessMessage carries only
                            // BaseObject = an object ID, resolved a few lines
                            // below into TargetBaseObject; the resolution
                            // happens AFTER this call in native order too, so
                            // the nil-attacker overload is used rather than
                            // reordering the native sequence to obtain one.
                            StruckDamage(nDamage);
                            HealthSpellChanged();
                            SendRefMsg(Grobal2.RM_STRUCK_MAG, (short)nDamage, m_WAbil.HP, m_WAbil.MaxHP, ProcessMsg.BaseObject, "");
                            TargetBaseObject = M2Share.ObjectManager.Get(ProcessMsg.BaseObject);
                            if (M2Share.g_Config.boMonDelHptoExp)
                            {
                                if (TargetBaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                {
                                    if ((TargetBaseObject as TPlayObject).m_WAbil.Level <= M2Share.g_Config.MonHptoExpLevel)
                                    {
                                        if (!M2Share.GetNoHptoexpMonList(m_sCharName))
                                        {
                                            if (TargetBaseObject.m_boAI)
                                            {
                                                (TargetBaseObject as RobotPlayObject).GainExp(GetMagStruckDamage(TargetBaseObject, nDamage) * M2Share.g_Config.MonHptoExpmax);
                                            }
                                            else
                                            {
                                                (TargetBaseObject as TPlayObject).GainExp(GetMagStruckDamage(TargetBaseObject, nDamage) * M2Share.g_Config.MonHptoExpmax);
                                            }
                                        }
                                    }
                                }
                                if (TargetBaseObject.m_btRaceServer == Grobal2.RC_PLAYCLONE)
                                {
                                    if (TargetBaseObject.m_Master != null)
                                    {
                                        if ((TargetBaseObject.m_Master as TPlayObject).m_WAbil.Level <= M2Share.g_Config.MonHptoExpLevel)
                                        {
                                            if (!M2Share.GetNoHptoexpMonList(m_sCharName))
                                            {
                                                if (TargetBaseObject.m_Master.m_boAI)
                                                {
                                                    (TargetBaseObject.m_Master as RobotPlayObject).GainExp(GetMagStruckDamage(TargetBaseObject, nDamage) * M2Share.g_Config.MonHptoExpmax);
                                                }
                                                else
                                                {
                                                    (TargetBaseObject.m_Master as TPlayObject).GainExp(GetMagStruckDamage(TargetBaseObject, nDamage) * M2Share.g_Config.MonHptoExpmax);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            if (m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                            {
                                if (m_boAnimal)
                                {
                                    m_nMeatQuality -= (ushort)(nDamage * 1000);
                                }
                                SendMsg(this, Grobal2.RM_STRUCK, nDamage, m_WAbil.HP, m_WAbil.MaxHP, ProcessMsg.BaseObject, "");
                            }
                        }
                        if (m_boFastParalysis)
                        {
                            m_wStatusTimeArr[Grobal2.POISON_STONE] = 1;
                            m_boFastParalysis = false;
                        }
                        break;
                    case Grobal2.RM_MAGHEALING:
                        if ((m_nIncHealing + ProcessMsg.nParam1) < 300)
                        {
                            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                            {
                                m_nIncHealing += ProcessMsg.nParam1;
                                m_nPerHealing = 5;
                            }
                            else
                            {
                                m_nIncHealing += ProcessMsg.nParam1;
                                m_nPerHealing = 5;
                            }
                        }
                        else
                        {
                            m_nIncHealing = 300;
                        }
                        break;
                    case NativeAction1017StruckIdent:
                        // ACT1017: ident 10048 (0x2740) has no Grobal2 name and
                        // that file is off-limits, so the constant sits on
                        // TBaseObject.NativeAction1017.cs next to its only
                        // producer. Native routes it at 0x766AB3
                        // `3D 40 27 00 00 cmp eax,0x2740` / 0x766ABA
                        // `0F 84 B3 05 00 00 je 0x767073`.
                        RunNativeAction1017StruckMessage();
                        break;
                    case Grobal2.RM_10101:
                        SendRefMsg(ProcessMsg.BaseObject, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, ProcessMsg.sMsg);
                        if ((ProcessMsg.BaseObject == Grobal2.RM_STRUCK) && (m_btRaceServer != Grobal2.RC_PLAYOBJECT))
                        {
                            SendMsg(this, ProcessMsg.BaseObject, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, ProcessMsg.sMsg);
                        }
                        if (m_boFastParalysis)
                        {
                            m_wStatusTimeArr[Grobal2.POISON_STONE] = 1;
                            m_boFastParalysis = false;
                        }
                        break;
                    case Grobal2.RM_DELAYMAGIC:
                        nPower = ProcessMsg.wParam;
                        nTargetX = HUtil32.LoWord(ProcessMsg.nParam1);
                        nTargetY = HUtil32.HiWord(ProcessMsg.nParam1);
                        nRage = ProcessMsg.nParam2;
                        TargetBaseObject = M2Share.ObjectManager.Get(ProcessMsg.nParam3);
                        if ((TargetBaseObject != null) && (TargetBaseObject.GetMagStruckDamage(this, nPower) > 0))
                        {
                            SetTargetCreat(TargetBaseObject);
                            if (TargetBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                            {
                                nPower = HUtil32.Round(nPower / 1.2);
                            }
                            if ((Math.Abs(nTargetX - TargetBaseObject.m_nCurrX) <= nRage) && (Math.Abs(nTargetY - TargetBaseObject.m_nCurrY) <= nRage))
                            {
                                TargetBaseObject.SendMsg(this, Grobal2.RM_MAGSTRUCK, 0, nPower, 0, 0, "");
                            }
                        }
                        break;
                    case Grobal2.RM_10155:
                        MapRandomMove(ProcessMsg.sMsg, ProcessMsg.wParam);
                        break;
                    case Grobal2.RM_DELAYPUSHED:
                        nPower = ProcessMsg.wParam;
                        nTargetX = HUtil32.LoWord(ProcessMsg.nParam1);
                        nTargetY = HUtil32.HiWord(ProcessMsg.nParam1);
                        nRage = ProcessMsg.nParam2;
                        TargetBaseObject = M2Share.ObjectManager.Get(ProcessMsg.nParam3);// M2Share.ObjectSystem.Get(ProcessMsg.nParam3);
                        if (TargetBaseObject != null)
                        {
                            TargetBaseObject.CharPushed((byte)nPower, nRage);
                        }
                        break;
                    case Grobal2.RM_POISON:
                        TargetBaseObject = M2Share.ObjectManager.Get(ProcessMsg.nParam2);// ((ProcessMsg.nParam2) as TBaseObject);
                        if (TargetBaseObject != null)
                        {
                            if (IsProperTarget(TargetBaseObject))
                            {
                                SetTargetCreat(TargetBaseObject);
                                if ((m_btRaceServer == Grobal2.RC_PLAYOBJECT) && (TargetBaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT))
                                {
                                    SetPKFlag(TargetBaseObject);
                                }
                                SetLastHiter(TargetBaseObject);
                            }
                            MakePosion(ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam3);// 中毒类型
                        }
                        else
                        {
                            MakePosion(ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam3);// 中毒类型
                        }
                        break;
                    case Grobal2.RM_TRANSPARENT:
                        M2Share.MagicManager.MagMakePrivateTransparent(this, ProcessMsg.nParam1);
                        break;
                    case Grobal2.RM_DOOPENHEALTH:
                        MakeOpenHealth();
                        break;
                        
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        public virtual string GetShowName()
        {
            var sShowName = m_sCharName;
            var result = M2Share.FilterShowName(sShowName);
            if ((m_Master != null) && !m_Master.m_boObMode)
            {
                result = result + '(' + m_Master.m_sCharName + ')';
            }
            return result;
        }

        
        
        
        public virtual void RecalcAbilitys()
        {
            GoodItem StdItem;
            bool[] boRecallSuite = new bool[4] { false, false, false, false };
            bool[] boMoXieSuite = new bool[3] { false, false, false };
            bool[] boSpirit = new bool[4] { false, false, false, false };
            m_AddAbil = new TAddAbility();
            SeedNativeFixedAbility(ref m_AddAbil);
            var oldHp = m_WAbil.HP;
            var oldMp = m_WAbil.MP;
            // 原生的重算全程不碰 Weight(player+0x2C4)：它唯一的写者是
            // 0x73CEF1 mov [ebx+0x2C4],eax（WeightChanged 里 0x73CEEC call 0x73E8D4
            // 遍历背包求和之后）。容器侧 sub_75EE78 只重置并重算 +0x370/+0x372，
            // 也就是 WearWeight / HandWeight 两项。
            var oldWeight = m_WAbil.Weight;
            // Bug1 fix 2026-04-22: deep copy so m_WAbil no longer aliases m_Abil,
            // otherwise every call accumulates equipment bonuses onto the base.
            m_WAbil.CopyFrom(m_Abil);
            m_WAbil.HP = oldHp;
            m_WAbil.MP = oldMp;
            m_WAbil.Weight = oldWeight;
            m_WAbil.WearWeight = 0;
            m_WAbil.HandWeight = 0;
            m_btAntiPoison = 0;
            m_nPoisonRecover = 0;
            m_nHealthRecover = 0;
            m_nSpellRecover = 0;
            m_nAntiMagic = 1;
            m_wNativeDrugHealthBonus = 0;
            m_wNativeDrugSpellBonus = 0;
            m_wNativeDrugJobBonus = 0;
            m_nLuck = 0;
            m_nHitSpeed = 0;
            // sub_73D500 的 0x73D578 / 0x73DAC5 / 0x73DECF 三次赋值，见
            // TBaseObject.NativeDeathDropDenominator.cs 的文件头。
            NativeRecalcDropRareFields();
            // sub_73D500 的 self+0x2DC（百分比物理减伤总量）三条 add
            // （0x73DEA8/0x73DEB7/0x73DEC7），见
            // TBaseObject.NativePhysicalPercentReduction.cs 的文件头。
            NativeRecalcPhysicalReductionPercent();
            // sub_75F4F8 clears the equipment extension block before each
            // rebuild. Its +0x54 low WORD is copied to actor+0x184 at
            // 0x73DE99..0x73DE9D. The accumulator at actor+0x188 is separate
            // persistent attack state and must not be cleared here.
            m_wNativePhysicalTailRate = 0;
            ResetNativeHqFastness();
            ResetNativeUnionFastness();
            ResetNativeNearHitFastness();
            m_boExpItem = false;
            m_rExpItem = 0;
            m_boPowerItem = false;
            m_rPowerItem = 0;
            m_boHideMode = false;
            m_boTeleport = false;
            m_boParalysis = false;
            m_boRevival = false;
            m_btNativeSecondPathFlag = 0;
            m_btNativeSecondPathTier = 0;
            m_boUnRevival = false;
            m_boFlameRing = false;
            m_boRecoveryRing = false;
            m_boAngryRing = false;
            m_boMagicShield = false;
            m_btNativeMagicDamageReductionPercent = 0;
            m_boNativeFullMagicShield = false;
            m_boNativeHalfMagicShield = false;
            m_boNativeUserMove = false;
            m_boNativeState26DirectStrong = false;
            m_boNativeState26DirectWeak = false;
            m_boNativeState26SingleStrong = false;
            m_boNativeState26SingleWeak = false;
            m_btNativeDragonPossessionLevel = 0;
            m_wNativeState26DeadlineBonus = 0;
            m_wNativeBaseMagicDamagePercent = 0;
            m_sNativeCriticalChance = 0;
            m_nNativeCriticalDamageIncrease = 0;
            m_sNativeAntiCriticalChance = 0;
            m_sNativeCriticalDamageReduction = 0;
            m_wNativeBreakThroughChance = 0;
            m_nNativeSteelBodyReduction = 0;
            m_boNativeAwakening = false;
            m_nNativeFlatMagicDamageIncrease = 0;
            m_nNativeGoldenBellReduction = 0;
            m_nNativeDragonBodyReduction = 0;
            m_btNativeDamageIncreasePercent = 0;
            m_nNativeMagicFastnessSelector = 0;
            m_nNativeSoulFastnessSelector = 0;
            m_boUnMagicShield = false;
            m_boMuscleRing = false;
            m_boFastTrain = false;
            m_boProbeNecklace = false;
            m_boSupermanItem = false;
            m_boGuildMove = false;
            m_boUnParalysis = false;
            m_boExpItem = false;
            m_boPowerItem = false;
            m_boNoDropItem = false;
            m_boNoDropUseItem = false;
            m_bopirit = false;
            m_btHorseType = 0;
            m_btDressEffType = 0;
            m_nAutoAddHPMPMode = 0;
            
            m_nMoXieSuite = 0;
            m_db3B0 = 0;
            m_nHongMoSuite = 0;
            bool boHongMoSuite1 = false;
            bool boHongMoSuite2 = false;
            bool boHongMoSuite3 = false;
            m_boRecallSuite = false;
            m_boSmashSet = false;
            bool boSmash1 = false;
            bool boSmash2 = false;
            bool boSmash3 = false;
            m_boHwanDevilSet = false;
            bool boHwanDevil1 = false;
            bool boHwanDevil2 = false;
            bool boHwanDevil3 = false;
            m_boPuritySet = false;
            bool boPurity1 = false;
            bool boPurity2 = false;
            bool boPurity3 = false;
            m_boMundaneSet = false;
            bool boMundane1 = false;
            bool boMundane2 = false;
            m_boNokChiSet = false;
            bool boNokChi1 = false;
            bool boNokChi2 = false;
            m_boTaoBuSet = false;
            bool boTaoBu1 = false;
            bool boTaoBu2 = false;
            m_boFiveStringSet = false;
            bool boFiveString1 = false;
            bool boFiveString2 = false;
            bool boFiveString3 = false;
            bool boOldHideMode = m_boHideMode;
            m_dwPKDieLostExp = 0;
            m_nPKDieLostLevel = 0;
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                if (m_UseItems[i] == null)
                {
                    continue;
                }
                if ((m_UseItems[i].wIndex <= 0) || (m_UseItems[i].Dura <= 0))
                {
                    continue;
                }
                StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                if (StdItem == null)
                {
                    continue;
                }
                // sub_75EE04 clears item+0x102..+0x104 and rebuilds +0x104 only
                // for the positive-durability instances admitted by this loop.
                NativeItemClass104.RefreshEquippedInstance(m_UseItems[i], StdItem);
                string nativeItemClass = NativeItemFactory.GetClassName(StdItem);
                if ((nativeItemClass == "TRing" && StdItem.Shape == 136) ||
                    (nativeItemClass == "TArmRing" && StdItem.Shape == 137) ||
                    (nativeItemClass == "TNecklace" && StdItem.Shape == 138))
                {
                    // TRing 0x762350/54, TArmRing 0x762B5B/5F and
                    // TNecklace 0x761AC1/C8 each add template DuraMax.
                    m_wNativePhysicalTailRate = unchecked((ushort)(
                        m_wNativePhysicalTailRate + StdItem.DuraMax));
                }
                StdItem.ApplyItemParameters(ref m_AddAbil);
                m_AddAbil.NativeDrugHealthBonus = unchecked((ushort)(
                    m_AddAbil.NativeDrugHealthBonus + StdItem.NativeDrugHealthBonus));
                m_AddAbil.NativeDrugSpellBonus = unchecked((ushort)(
                    m_AddAbil.NativeDrugSpellBonus + StdItem.NativeDrugSpellBonus));
                m_AddAbil.NativeDrugJobBonus = unchecked((ushort)(
                    m_AddAbil.NativeDrugJobBonus + StdItem.NativeDrugJobBonus));
                ApplyNativeEffectItemParameters(m_UseItems[i], StdItem, ref m_AddAbil);
                if ((i == Grobal2.U_WEAPON) || (i == Grobal2.U_RIGHTHAND) || (i == Grobal2.U_DRESS))
                {
                    // 0x75EE4A  80 7D FF 01  cmp byte [ebp-1],1   ; 槽号
                    // 0x75EE4E  74 10        je  0x75EE60
                    // 0x75EE57  66 01 83 70 03 00 00  add word [ebx+0x370],ax  ; 其余槽累加
                    // 0x75EE67  66 89 83 72 03 00 00  mov word [ebx+0x372],ax  ; 只有槽 1 是赋值
                    // 两个容器字段随后被 0x73D661 / 0x73D674 原样搬进 WearWeight / HandWeight。
                    // 槽 2（U_RIGHTHAND）在原生属于「其余槽」，进的是 +0x370。
                    if (i == Grobal2.U_WEAPON)
                    {
                        m_WAbil.HandWeight = StdItem.Weight;
                    }
                    else
                    {
                        m_WAbil.WearWeight += StdItem.Weight;
                    }
                    
                    if (StdItem.AniCount == 120)
                    {
                        m_boFastTrain = true;
                    }
                    if (StdItem.AniCount == 145)
                    {
                        m_boGuildMove = true;
                    }
                    if (StdItem.AniCount == 111)
                    {
                        m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 6 * 10 * 1000;
                        m_boHideMode = true;
                    }
                    if (StdItem.AniCount == 112)
                    {
                        m_boTeleport = true;
                    }
                    if (StdItem.AniCount == 113)
                    {
                        m_boParalysis = true;
                    }
                    if (StdItem.AniCount == 114)
                    {
                        m_boRevival = true;
                    }
                    if (StdItem.AniCount == 115)
                    {
                        m_boFlameRing = true;
                    }
                    if (StdItem.AniCount == 116)
                    {
                        m_boRecoveryRing = true;
                    }
                    if (StdItem.AniCount == 117)
                    {
                        m_boAngryRing = true;
                    }
                    if (StdItem.AniCount == 118)
                    {
                        m_boMagicShield = true;
                    }
                    if (StdItem.AniCount == 119)
                    {
                        m_boMuscleRing = true;
                    }
                    if (StdItem.AniCount == 135)
                    {
                        boMoXieSuite[0] = true;
                        m_nMoXieSuite += StdItem.Weight / 10;
                    }
                    if (StdItem.AniCount == 138)
                    {
                        m_nHongMoSuite += StdItem.Weight;
                    }
                    if (StdItem.AniCount == 139)
                    {
                        m_boUnParalysis = true;
                    }
                    if (StdItem.AniCount == 140)
                    {
                        m_boSupermanItem = true;
                    }
                    if (StdItem.AniCount == 141)
                    {
                        m_boExpItem = true;
                        m_rExpItem = m_rExpItem + (m_UseItems[i].Dura / M2Share.g_Config.nItemExpRate);
                    }
                    if (StdItem.AniCount == 142)
                    {
                        m_boPowerItem = true;
                        m_rPowerItem = m_rPowerItem + (m_UseItems[i].Dura / M2Share.g_Config.nItemPowerRate);
                    }
                    if (StdItem.AniCount == 182)
                    {
                        m_boExpItem = true;
                        m_rExpItem = m_rExpItem + (m_UseItems[i].DuraMax / M2Share.g_Config.nItemExpRate);
                    }
                    if (StdItem.AniCount == 183)
                    {
                        m_boPowerItem = true;
                        m_rPowerItem = m_rPowerItem + (m_UseItems[i].DuraMax / M2Share.g_Config.nItemPowerRate);
                    }
                    if (StdItem.AniCount == 143)
                    {
                        m_boUnMagicShield = true;
                    }
                    if (StdItem.AniCount == 144)
                    {
                        m_boUnRevival = true;
                    }
                    if (StdItem.AniCount == 170)
                    {
                        m_boAngryRing = true;
                    }
                    if (StdItem.AniCount == 171)
                    {
                        m_boNoDropItem = true;
                    }
                    if (StdItem.AniCount == 172)
                    {
                        m_boNoDropUseItem = true;
                    }
                    if (StdItem.AniCount == 150)
                    {
                        
                        m_boParalysis = true;
                        m_boMagicShield = true;
                    }
                    if (StdItem.AniCount == 151)
                    {
                        
                        m_boParalysis = true;
                        m_boFlameRing = true;
                    }
                    if (StdItem.AniCount == 152)
                    {
                        
                        m_boParalysis = true;
                        m_boRecoveryRing = true;
                    }
                    if (StdItem.AniCount == 153)
                    {
                        
                        m_boParalysis = true;
                        m_boMuscleRing = true;
                    }
                    if (StdItem.Shape == 154)
                    {
                        
                        m_boMagicShield = true;
                        m_boFlameRing = true;
                    }
                    if (StdItem.AniCount == 155)
                    {
                        
                        m_boMagicShield = true;
                        m_boRecoveryRing = true;
                    }
                    if (StdItem.AniCount == 156)
                    {
                        
                        m_boMagicShield = true;
                        m_boMuscleRing = true;
                    }
                    if (StdItem.AniCount == 157)
                    {
                        
                        m_boTeleport = true;
                        m_boParalysis = true;
                    }
                    if (StdItem.AniCount == 158)
                    {
                        
                        m_boTeleport = true;
                        m_boMagicShield = true;
                    }
                    if (StdItem.AniCount == 159)
                    {
                        
                        m_boTeleport = true;
                    }
                    if (StdItem.AniCount == 160)
                    {
                        
                        m_boTeleport = true;
                        m_boRevival = true;
                    }
                    if (StdItem.AniCount == 161)
                    {
                        
                        m_boParalysis = true;
                        m_boRevival = true;
                    }
                    if (StdItem.AniCount == 162)
                    {
                        
                        m_boMagicShield = true;
                        m_boRevival = true;
                    }
                    if (StdItem.AniCount == 180)
                    {
                        // DURA-03: read instance DuraMax (obj+0x28) not template DuraMax (+0x1C)
                        m_dwPKDieLostExp = m_UseItems[i].DuraMax * M2Share.g_Config.dwPKDieLostExpRate;
                    }
                    if (StdItem.AniCount == 181)
                    {
                        // DURA-03: read instance DuraMax (obj+0x28) not template DuraMax (+0x1C)
                        m_nPKDieLostLevel = m_UseItems[i].DuraMax / M2Share.g_Config.nPKDieLostLevelRate;
                    }
                    
                }
                else
                {
                    m_WAbil.WearWeight += StdItem.Weight;
                }
                if (i == Grobal2.U_WEAPON)
                {
                    if ((StdItem.Source - 1 - 10) < 0)
                    {
                        m_AddAbil.btWeaponStrong = (byte)StdItem.Source;// 强度+
                    }
                    if ((StdItem.Source <= -1) && (StdItem.Source >= -50))  
                    {
                        m_AddAbil.btUndead = unchecked((ushort)(
                            m_AddAbil.btUndead + -StdItem.Source));// Holy+
                    }
                    if ((StdItem.Source <= -51) && (StdItem.Source >= -100))// -51 to -100
                    {
                        m_AddAbil.btUndead = unchecked((ushort)(
                            m_AddAbil.btUndead + StdItem.Source + 50));// Holy-
                    }
                    continue;
                }
                if (i == Grobal2.U_RIGHTHAND)
                {
                    if (StdItem.Shape >= 1 && StdItem.Shape <= 50)
                    {
                        m_btDressEffType = StdItem.Shape;
                    }
                    if (StdItem.Shape >= 51 && StdItem.Shape <= 100)
                    {
                        m_btHorseType = (byte)(StdItem.Shape - 50);
                    }
                    continue;
                }
                if (i == Grobal2.U_DRESS)
                {
                    if (m_UseItems[i].btValue[5] > 0)
                    {
                        m_btDressEffType = m_UseItems[i].btValue[5];
                    }
                    if (StdItem.AniCount > 0)
                    {
                        m_btDressEffType = unchecked((byte)StdItem.AniCount);
                    }
                    if (StdItem.Light)
                    {
                        m_nLight = 3;
                    }
                    continue;
                }
                
                if (StdItem.Shape == 139)
                {
                    m_boUnParalysis = true;
                }
                if (StdItem.Shape == 140)
                {
                    m_boSupermanItem = true;
                }
                if (StdItem.Shape == 141)
                {
                    m_boExpItem = true;
                    m_rExpItem = m_rExpItem + (m_UseItems[i].Dura / M2Share.g_Config.nItemExpRate);
                }
                if (StdItem.Shape == 142)
                {
                    m_boPowerItem = true;
                    m_rPowerItem = m_rPowerItem + (m_UseItems[i].Dura / M2Share.g_Config.nItemPowerRate);
                }
                if (StdItem.Shape == 182)
                {
                    m_boExpItem = true;
                    m_rExpItem = m_rExpItem + (m_UseItems[i].DuraMax / M2Share.g_Config.nItemExpRate);
                }
                if (StdItem.Shape == 183)
                {
                    m_boPowerItem = true;
                    m_rPowerItem = m_rPowerItem + (m_UseItems[i].DuraMax / M2Share.g_Config.nItemPowerRate);
                }
                if (StdItem.Shape == 143)
                {
                    m_boUnMagicShield = true;
                }
                if (StdItem.Shape == 144)
                {
                    m_boUnRevival = true;
                }
                if (StdItem.Shape == 170)
                {
                    m_boAngryRing = true;
                }
                if (StdItem.Shape == 171)
                {
                    m_boNoDropItem = true;
                }
                if (StdItem.Shape == 172)
                {
                    m_boNoDropUseItem = true;
                }
                if (StdItem.Shape == 150)
                {
                    
                    m_boParalysis = true;
                    m_boMagicShield = true;
                }
                if (StdItem.Shape == 151)
                {
                    
                    m_boParalysis = true;
                    m_boFlameRing = true;
                }
                if (StdItem.Shape == 152)
                {
                    
                    m_boParalysis = true;
                    m_boRecoveryRing = true;
                }
                if (StdItem.Shape == 153)
                {
                    
                    m_boParalysis = true;
                    m_boMuscleRing = true;
                }
                if (StdItem.Shape == 154)
                {
                    
                    m_boMagicShield = true;
                    m_boFlameRing = true;
                }
                if (StdItem.Shape == 155)
                {
                    
                    m_boMagicShield = true;
                    m_boRecoveryRing = true;
                }
                if (StdItem.Shape == 156)
                {
                    
                    m_boMagicShield = true;
                    m_boMuscleRing = true;
                }
                if (StdItem.Shape == 157)
                {
                    
                    m_boTeleport = true;
                    m_boParalysis = true;
                }
                if (StdItem.Shape == 158)
                {
                    
                    m_boTeleport = true;
                    m_boMagicShield = true;
                }
                if (StdItem.Shape == 159)
                {
                    
                    m_boTeleport = true;
                }
                if (StdItem.Shape == 160)
                {
                    
                    m_boTeleport = true;
                    m_boRevival = true;
                }
                if (StdItem.Shape == 161)
                {
                    
                    m_boParalysis = true;
                    m_boRevival = true;
                }
                if (StdItem.Shape == 162)
                {
                    
                    m_boMagicShield = true;
                    m_boRevival = true;
                }
                if (StdItem.Shape == 180)
                {
                    // DURA-03: read instance DuraMax (obj+0x28) not template DuraMax (+0x1C)
                    m_dwPKDieLostExp = m_UseItems[i].DuraMax * M2Share.g_Config.dwPKDieLostExpRate;
                }
                if (StdItem.Shape == 181)
                {
                    // DURA-03: read instance DuraMax (obj+0x28) not template DuraMax (+0x1C)
                    m_nPKDieLostLevel = m_UseItems[i].DuraMax / M2Share.g_Config.nPKDieLostLevelRate;
                }
                
                if (StdItem.Shape == 120)
                {
                    m_boFastTrain = true;
                }
                if (StdItem.Shape == 123)
                {
                    boRecallSuite[0] = true;
                }
                if (StdItem.Shape == 145)
                {
                    m_boGuildMove = true;
                }
                if (StdItem.Shape == 127)
                {
                    boSpirit[0] = true;
                }
                if (StdItem.Shape == 135)
                {
                    boMoXieSuite[0] = true;
                    m_nMoXieSuite += StdItem.AniCount;
                }
                if (StdItem.Shape == 138)
                {
                    boHongMoSuite1 = true;
                    m_nHongMoSuite += StdItem.AniCount;
                }
                if (StdItem.Shape == 200)
                {
                    boSmash1 = true;
                }
                if (StdItem.Shape == 203)
                {
                    boHwanDevil1 = true;
                }
                if (StdItem.Shape == 206)
                {
                    boPurity1 = true;
                }
                if (StdItem.Shape == 216)
                {
                    boFiveString1 = true;
                }
                if (StdItem.Shape == 111)
                {
                    m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 6 * 10 * 1000;
                    m_boHideMode = true;
                }
                if (StdItem.Shape == 112 &&
                    StdItem.StdMode is not 22 and not 23)
                {
                    m_boTeleport = true;
                }
                if (StdItem.Shape == 113)
                {
                    m_boParalysis = true;
                }
                if (StdItem.Shape == 114)
                {
                    m_boRevival = true;
                }
                if (StdItem.Shape == 115)
                {
                    m_boFlameRing = true;
                }
                if (StdItem.Shape == 116)
                {
                    m_boRecoveryRing = true;
                }
                if (StdItem.Shape == 117)
                {
                    m_boAngryRing = true;
                }
                if (StdItem.Shape == 118)
                {
                    m_boMagicShield = true;
                }
                if (StdItem.Shape == 119)
                {
                    m_boMuscleRing = true;
                }
                if (StdItem.Shape == 122)
                {
                    boRecallSuite[1] = true;
                }
                if (StdItem.Shape == 128)
                {
                    boSpirit[1] = true;
                }
                if (StdItem.Shape == 133)
                {
                    boMoXieSuite[1] = true;
                    m_nMoXieSuite += StdItem.AniCount;
                }
                if (StdItem.Shape == 136)
                {
                    boHongMoSuite2 = true;
                    m_nHongMoSuite += StdItem.AniCount;
                }
                if (StdItem.Shape == 201)
                {
                    boSmash2 = true;
                }
                if (StdItem.Shape == 204)
                {
                    boHwanDevil2 = true;
                }
                if (StdItem.Shape == 207)
                {
                    boPurity2 = true;
                }
                if (StdItem.Shape == 210)
                {
                    boMundane1 = true;
                }
                if (StdItem.Shape == 212)
                {
                    boNokChi1 = true;
                }
                if (StdItem.Shape == 214)
                {
                    boTaoBu1 = true;
                }
                if (StdItem.Shape == 217)
                {
                    boFiveString2 = true;
                }
                if ((StdItem.Source <= -1) && (StdItem.Source >= -50))
                {
                    
                    m_AddAbil.btUndead = unchecked((ushort)(
                        m_AddAbil.btUndead + -StdItem.Source));
                    
                }
                if ((StdItem.Source <= -51) && (StdItem.Source >= -100))
                {
                    
                    m_AddAbil.btUndead = unchecked((ushort)(
                        m_AddAbil.btUndead + StdItem.Source + 50));
                    
                }
                if (StdItem.Shape == 124)
                {
                    boRecallSuite[2] = true;
                }
                if (StdItem.Shape == 126)
                {
                    boSpirit[2] = true;
                }
                if (StdItem.Shape == 145)
                {
                    m_boGuildMove = true;
                }
                if (StdItem.Shape == 134)
                {
                    boMoXieSuite[2] = true;
                    m_nMoXieSuite += StdItem.AniCount;
                }
                if (StdItem.Shape == 137)
                {
                    boHongMoSuite3 = true;
                    m_nHongMoSuite += StdItem.AniCount;
                }
                if (StdItem.Shape == 202)
                {
                    boSmash3 = true;
                }
                if (StdItem.Shape == 205)
                {
                    boHwanDevil3 = true;
                }
                if (StdItem.Shape == 208)
                {
                    boPurity3 = true;
                }
                if (StdItem.Shape == 211)
                {
                    boMundane2 = true;
                }
                if (StdItem.Shape == 213)
                {
                    boNokChi2 = true;
                }
                if (StdItem.Shape == 215)
                {
                    boTaoBu2 = true;
                }
                if (StdItem.Shape == 218)
                {
                    boFiveString3 = true;
                }
                if (StdItem.Shape == 125)
                {
                    boRecallSuite[3] = true;
                }
                if (StdItem.Shape == 129)
                {
                    boSpirit[3] = true;
                }
            }

            // sub_73D500 @0x73D63D copies container agg2 → self+0x1B0; second revive
            // path reads [+0x1D1]/[+0x1DD] via sub_746084 @0x746084.
            NativeEquipAgg2Revive.Recalc(this);

            if (boRecallSuite[0] && boRecallSuite[1] && boRecallSuite[2] && boRecallSuite[3])
            {
                m_boRecallSuite = true;
            }
            if (boMoXieSuite[0] && boMoXieSuite[1] && boMoXieSuite[2])
            {
                m_nMoXieSuite += 50;
            }
            if (boHongMoSuite1 && boHongMoSuite2 && boHongMoSuite3)
            {
                m_AddAbil.wHitPoint += 2;
                m_NativeCoreWorkingAbility.HitPoint = unchecked(
                    m_NativeCoreWorkingAbility.HitPoint + 2);
            }
            if (boSpirit[0] && boSpirit[1] && boSpirit[2] && boSpirit[3])
            {
                m_bopirit = true;
            }
            if (boSmash1 && boSmash2 && boSmash3)
            {
                m_boSmashSet = true;
            }
            if (boHwanDevil1 && boHwanDevil2 && boHwanDevil3)
            {
                m_boHwanDevilSet = true;
            }
            if (boPurity1 && boPurity2 && boPurity3)
            {
                m_boPuritySet = true;
            }
            if (boMundane1 && boMundane2)
            {
                m_boMundaneSet = true;
            }
            if (boNokChi1 && boNokChi2)
            {
                m_boNokChiSet = true;
            }
            if (boTaoBu1 && boTaoBu2)
            {
                m_boTaoBuSet = true;
            }
            if (boFiveString1 && boFiveString2 && boFiveString3)
            {
                m_boFiveStringSet = true;
            }
            m_WAbil.Weight = RecalcBagWeight();
            if (m_boTransparent && (m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] > 0))
            {
                m_boHideMode = true;
            }
            if (m_boHideMode)
            {
                if (!boOldHideMode)
                {
                    m_nCharStatus = GetCharStatus();
                    StatusChanged();
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        if (this is TPlayObject player)
                            player.SysMsg("", MsgColor.Green, MsgType.Hint);
                    }
                }
            }
            else
            {
                if (boOldHideMode)
                {
                    m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 0;
                    m_nCharStatus = GetCharStatus();
                    StatusChanged();
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        // SM_HIDE sent via TPlayObject path
                    }
                }
            }
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT ||
                m_btRaceServer == Grobal2.RC_HEROOBJECT)
            {
                RecalcHitSpeed();
            }
            else
            {
                m_wSpeedPoint = m_btSpeedPoint;
            }
            int nOldLight = m_nLight;
            if ((m_UseItems[Grobal2.U_RIGHTHAND] != null) && (m_UseItems[Grobal2.U_RIGHTHAND].wIndex > 0) && (m_UseItems[Grobal2.U_RIGHTHAND].Dura > 0))
            {
                m_nLight = 3;
            }
            else
            {
                m_nLight = 0;
            }
            if (nOldLight != m_nLight)
            {
                SendRefMsg(Grobal2.RM_CHANGELIGHT, 0, 0, 0, 0, "");
            }
            ProjectNativeCoreHitAndAgility();
            m_btAntiPoison += (byte)m_AddAbil.wAntiPoison;
            m_wEffectResistance = m_AddAbil.wAntiPoison;
            m_wEffectStrength = m_AddAbil.wEffectStrength;
            m_nPoisonRecover += m_AddAbil.wPoisonRecover;
            m_nHealthRecover += m_AddAbil.wHealthRecover;
            m_nSpellRecover += m_AddAbil.wSpellRecover;
            m_nAntiMagic += m_AddAbil.wAntiMagic;
            m_nNativeMagicHitHealAmount =
                m_AddAbil.NativeMagicHitHealAmount;
            m_nNativeMagicHitHealChance =
                m_AddAbil.NativeMagicHitHealChance;
            m_wNativeType74MagicHit = m_AddAbil.NativeType74MagicHit;
            m_wNativeBaseMagicDamagePercent =
                m_AddAbil.NativeBaseMagicDamagePercent;
            m_wNativeDrugHealthBonus = m_AddAbil.NativeDrugHealthBonus;
            m_wNativeDrugSpellBonus = m_AddAbil.NativeDrugSpellBonus;
            m_wNativeDrugJobBonus = m_AddAbil.NativeDrugJobBonus;
            m_sNativeCriticalChance = unchecked((short)
                m_AddAbil.NativeCriticalChance);
            m_nNativeCriticalDamageIncrease =
                m_AddAbil.NativeCriticalDamageIncrease;
            m_sNativeAntiCriticalChance = unchecked((short)
                m_AddAbil.NativeAntiCriticalChance);
            m_sNativeCriticalDamageReduction = unchecked((short)
                m_AddAbil.NativeCriticalDamageReduction);
            m_wNativeBreakThroughChance =
                m_AddAbil.NativeBreakThroughChance;
            m_nNativeSteelBodyReduction = unchecked(
                m_nNativeSteelBodyReduction +
                m_AddAbil.NativeSteelBodyReduction);
            m_boNativeAwakening = m_AddAbil.NativeAwakening;
            m_nNativeFlatMagicDamageIncrease =
                m_AddAbil.NativeFlatMagicDamageIncrease;
            m_nNativeGoldenBellReduction = unchecked(
                m_nNativeGoldenBellReduction +
                m_AddAbil.NativeGoldenBellReduction);
            m_nNativeDragonBodyReduction = unchecked(
                m_nNativeDragonBodyReduction +
                m_AddAbil.NativeDragonBodyReduction);
            m_btNativeDamageIncreasePercent =
                m_AddAbil.NativeDamageIncreasePercent;
            m_nNativeHqFastness =
                m_AddAbil.NativeHqFastnessSelector;
            m_nNativeUnionFastness =
                m_AddAbil.NativeUnionFastnessSelector;
            m_nNativeNearHitFastness =
                m_AddAbil.NativeNearHitFastnessSelector;
            m_nNativeMagicFastnessSelector = unchecked(
                m_nNativeMagicFastnessSelector +
                m_AddAbil.NativeMagicFastnessSelector);
            m_nNativeSoulFastnessSelector = unchecked(
                m_nNativeSoulFastnessSelector +
                m_AddAbil.NativeSoulFastnessSelector);
            m_btNativeMagicDamageReductionPercent =
                m_AddAbil.NativeMagicDamageReductionPercent;
            m_boNativeFullMagicShield = m_AddAbil.NativeFullMagicShield;
            m_boMagicShield = m_AddAbil.NativeStandardMagicShield;
            m_boNativeHalfMagicShield = m_AddAbil.NativeHalfMagicShield;
            m_boNativeUserMove = m_AddAbil.NativeUserMove;
            m_boProbeNecklace = m_AddAbil.NativeSearchHuman;
            m_btNativeDragonPossessionLevel =
                m_AddAbil.NativeDragonPossessionLevel;
            if (m_btNativeDragonPossessionLevel > 0)
                SetNativeActiveState(5);
            else
                ClearNativeActiveState(5);
            m_boNativeState26DirectStrong =
                m_AddAbil.NativeState26DirectStrong;
            m_boNativeState26DirectWeak =
                m_AddAbil.NativeState26DirectWeak;
            m_boNativeState26SingleStrong =
                m_AddAbil.NativeState26SingleStrong;
            m_boNativeState26SingleWeak =
                m_AddAbil.NativeState26SingleWeak;
            m_wNativeState26DeadlineBonus =
                m_AddAbil.NativeState26DeadlineBonus;
            ProjectNativeBreakContestAbilities();
            m_nLuck += m_AddAbil.btLuck;
            m_nLuck -= m_AddAbil.btUnLuck;
            m_nHitSpeed = m_AddAbil.nHitSpeed;
            m_WAbil.MaxWeight += m_AddAbil.Weight;
            m_WAbil.MaxWearWeight += m_AddAbil.WearWeight;
            m_WAbil.MaxHandWeight += m_AddAbil.HandWeight;
            ProjectNativeCoreCombatAbility();
            if (m_wStatusTimeArr[Grobal2.STATE_DEFENCEUP] > 0)
            {
                m_WAbil.AC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.AC), HUtil32.HiWord(m_WAbil.AC) + 2 + (m_Abil.Level / 7));
            }
            if (m_wStatusTimeArr[Grobal2.STATE_MAGDEFENCEUP] > 0)
            {
                m_WAbil.MAC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.MAC), HUtil32.HiWord(m_WAbil.MAC) + 2 + (m_Abil.Level / 7));
            }
            if (m_wStatusArrValue[0] > 0)
            {
                m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC), HUtil32.HiWord(m_WAbil.DC) + 2 + m_wStatusArrValue[0]);
            }
            if (m_wStatusArrValue[1] > 0)
            {
                m_WAbil.MC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.MC), HUtil32.HiWord(m_WAbil.MC) + 2 + m_wStatusArrValue[1]);
            }
            if (m_wStatusArrValue[2] > 0)
            {
                m_WAbil.SC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.SC), HUtil32.HiWord(m_WAbil.SC) + 2 + m_wStatusArrValue[2]);
            }
            if (m_wStatusArrValue[3] > 0)
            {
                m_nHitSpeed += m_wStatusArrValue[3];
            }
            if (m_wStatusArrValue[4] > 0)
            {
                m_WAbil.MaxHP = ClampAbility((long)m_WAbil.MaxHP + m_wStatusArrValue[4]);
            }
            if (m_wStatusArrValue[5] > 0)
            {
                m_WAbil.MaxMP = ClampAbility((long)m_WAbil.MaxMP + m_wStatusArrValue[5]);
            }
            if (m_boFlameRing)
            {
                AddItemSkill(1);
            }
            else
            {
                DelItemSkill(1);
            }
            if (m_boRecoveryRing)
            {
                AddItemSkill(2);
            }
            else
            {
                DelItemSkill(2);
            }
            if (m_boMuscleRing)
            {
                m_WAbil.MaxWeight += m_WAbil.MaxWeight;
                m_WAbil.MaxWearWeight += m_WAbil.MaxWearWeight;
                m_WAbil.MaxHandWeight += m_WAbil.MaxHandWeight;
            }
            if (m_nMoXieSuite > 0)
            {
                
                if (m_WAbil.MaxMP <= m_nMoXieSuite)
                {
                    m_nMoXieSuite = m_WAbil.MaxMP - 1;
                }
                m_WAbil.MaxMP = ClampAbility((long)m_WAbil.MaxMP - m_nMoXieSuite);
                m_WAbil.MaxHP = ClampAbility((long)m_WAbil.MaxHP + m_nMoXieSuite);
            }
            if (m_bopirit)
            {
                
                m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC) + 2, HUtil32.HiWord(m_WAbil.DC) + 2 + 5);
                m_nHitSpeed += 2;
            }
            if (m_boSmashSet)
            {
                
                m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC) + 1, HUtil32.HiWord(m_WAbil.DC) + 2 + 3);
                m_nHitSpeed++;
            }
            if (m_boHwanDevilSet)
            {
                
                m_WAbil.MaxHandWeight += 5;
                m_WAbil.MaxWeight += 20;
                m_WAbil.MC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.MC) + 1, HUtil32.HiWord(m_WAbil.MC) + 2 + 2);
            }
            if (m_boPuritySet)
            {
                
                m_AddAbil.btUndead = unchecked((ushort)(
                    m_AddAbil.btUndead - 3));
                m_WAbil.SC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.SC) + 1, HUtil32.HiWord(m_WAbil.SC) + 2 + 2);
            }
            if (m_boMundaneSet)
            {
                
                m_WAbil.MaxHP = ClampAbility((long)m_WAbil.MaxHP + 50);
            }
            if (m_boNokChiSet)
            {
                
                m_WAbil.MaxMP = ClampAbility((long)m_WAbil.MaxMP + 50);
            }
            if (m_boTaoBuSet)
            {
                
                m_WAbil.MaxHP = ClampAbility((long)m_WAbil.MaxHP + 30);
                m_WAbil.MaxMP = ClampAbility((long)m_WAbil.MaxMP + 30);
            }
            if (m_boFiveStringSet)
            {
                
                m_WAbil.MaxHP = ClampAbility((long)m_WAbil.MaxHP * 130 / 100);
                m_btHitPoint += 2;
            }
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                SendUpdateMsg(this, Grobal2.RM_CHARSTATUSCHANGED, m_nHitSpeed,
                    m_nCharStatus, 0, 0, "", GetBodyStateBuffer());
            }
            if (m_btRaceServer >= Grobal2.RC_ANIMAL && m_btRaceServer != Grobal2.RC_HEROOBJECT)
            {
                MonsterRecalcAbilitys();
            }

            ApplyTimedAbilityBonuses();
            m_WAbil.AC = HUtil32.MakeLong(HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.LoWord(m_WAbil.AC)), HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.HiWord(m_WAbil.AC)));
            m_WAbil.MAC = HUtil32.MakeLong(HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.LoWord(m_WAbil.MAC)), HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.HiWord(m_WAbil.MAC)));
            m_WAbil.DC = HUtil32.MakeLong(HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.LoWord(m_WAbil.DC)), HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.HiWord(m_WAbil.DC)));
            m_WAbil.MC = HUtil32.MakeLong(HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.LoWord(m_WAbil.MC)), HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.HiWord(m_WAbil.MC)));
            m_WAbil.SC = HUtil32.MakeLong(HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.LoWord(m_WAbil.SC)), HUtil32._MIN(M2Share.MAXHUMPOWER, HUtil32.HiWord(m_WAbil.SC)));
            if (M2Share.g_Config.boHungerSystem && M2Share.g_Config.boHungerDecPower)
            {
                if (HUtil32.RangeInDefined(m_nHungerStatus, 0, 999))
                {
                    m_WAbil.DC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.DC) * 0.2), HUtil32.Round(HUtil32.HiWord(m_WAbil.DC) * 0.2));
                    m_WAbil.MC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.MC) * 0.2), HUtil32.Round(HUtil32.HiWord(m_WAbil.MC) * 0.2));
                    m_WAbil.SC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.SC) * 0.2), HUtil32.Round(HUtil32.HiWord(m_WAbil.SC) * 0.2));
                }
                else if (HUtil32.RangeInDefined(m_nHungerStatus, 1000, 1999))
                {
                    m_WAbil.DC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.DC) * 0.4), HUtil32.Round(HUtil32.HiWord(m_WAbil.DC) * 0.4));
                    m_WAbil.MC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.MC) * 0.4), HUtil32.Round(HUtil32.HiWord(m_WAbil.MC) * 0.4));
                    m_WAbil.SC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.SC) * 0.4), HUtil32.Round(HUtil32.HiWord(m_WAbil.SC) * 0.4));
                }
                else if (HUtil32.RangeInDefined(m_nHungerStatus, 2000, 2999))
                {
                    m_WAbil.DC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.DC) * 0.6), HUtil32.Round(HUtil32.HiWord(m_WAbil.DC) * 0.6));
                    m_WAbil.MC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.MC) * 0.6), HUtil32.Round(HUtil32.HiWord(m_WAbil.MC) * 0.6));
                    m_WAbil.SC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.SC) * 0.6), HUtil32.Round(HUtil32.HiWord(m_WAbil.SC) * 0.6));
                }
                else if (HUtil32.RangeInDefined(m_nHungerStatus, 3000, 3000))
                {
                    m_WAbil.DC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.DC) * 0.9), HUtil32.Round(HUtil32.HiWord(m_WAbil.DC) * 0.9));
                    m_WAbil.MC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.MC) * 0.9), HUtil32.Round(HUtil32.HiWord(m_WAbil.MC) * 0.9));
                    m_WAbil.SC = HUtil32.MakeLong(HUtil32.Round(HUtil32.LoWord(m_WAbil.SC) * 0.9), HUtil32.Round(HUtil32.HiWord(m_WAbil.SC) * 0.9));
                }
            }
        }
    }
}
