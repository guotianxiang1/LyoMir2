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
                    if (m_boCanReAlive && m_pMonGen != null)
                    {
                        // ✅ SPAWN-32: Use dwZenTime directly (no ProcessMonsters_GetZenTime scaling).
                        // The formula (dwZenTime - 20sec) makes corpses become ghosts 20 seconds
                        // before the next spawn cycle, preventing visual overlap. The _MAX(10sec, ...)
                        // ensures minimum 10-second corpse display even for fast spawns.
                        var dwMakeGhostTime = HUtil32._MAX(10 * 1000, m_pMonGen.dwZenTime - 20 * 1000);
                        if (dwMakeGhostTime > M2Share.g_Config.dwMakeGhostTime)
                        {
                            dwMakeGhostTime = M2Share.g_Config.dwMakeGhostTime;
                        }
                        if ((HUtil32.GetTickCount() - m_dwDeathTick > dwMakeGhostTime))
                        {
                            MakeGhost();
                        }
                    }
                    else
                    {
                        if ((HUtil32.GetTickCount() - m_dwDeathTick) > M2Share.g_Config.dwMakeGhostTime)// 3 * 60 * 1000
                        {
                            MakeGhost();
                        }
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
                
                for (var i = m_SlaveList.Count - 1; i >= 0; i--)
                {
                    if (m_SlaveList[i].m_boDeath || m_SlaveList[i].m_boGhost || (m_SlaveList[i].m_Master != this))
                    {
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
                            for (var i = 0; i < m_Master.m_SlaveList.Count; i++)
                            {
                                if (m_Master.m_SlaveList[i] == this)
                                {
                                    m_Master.m_SlaveList.RemoveAt(i);
                                    break;
                                }
                            }
                            m_Master = null;
                            m_WAbil.HP /= 10;
                            RefShowName();
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
                    
                    if (m_GroupOwner != null)
                    {
                        if (m_GroupOwner.m_boDeath || m_GroupOwner.m_boGhost)
                        {
                            m_GroupOwner = null;
                        }
                    }

                    if (m_GroupOwner == this)
                    {
                        for (var i = m_GroupMembers.Count - 1; i >= 0; i--)
                        {
                            TBaseObject BaseObject = m_GroupMembers[i];
                            if (BaseObject.m_boDeath || BaseObject.m_boGhost)
                            {
                                m_GroupMembers.RemoveAt(i);
                            }
                        }
                    }
                    
                    // 战神 sub_6B3EAC @0x6B3B71-0x6B3B87：`eax=ebx(m_DealCreat)` ; `call sub_772DA8`
                    //   （= m_boGhost getter `mov al,[eax+0x74]`）; `test al,al` / `jne 清零` ;
                    //   **`cmp byte ptr [ebx+0x73], 0` / `je 跳过`**（= m_boDeath 析取项）; 清零 [self+0xBAC]。
                    // 即原生在 **ghost 或 death** 任一为真时都清 m_DealCreat；旧 C# 只查 ghost，
                    // 于是一个「已死但尚未 ghost」的对端会把 m_DealCreat 一直挂着 ——
                    // 配合 ClientDealEnd 曾缺失的 m_boDeath 门，这就是「和尸体成交」的具体路径。
                    // （节流：原生用专属 tick 字段 [self+0x73C] 比 0x7530=30000ms；此处所在的
                    //  `m_dwVerifyTick` 块周期同为 30*1000ms，与组队清扫共用一个 tick 字段 =
                    //  周期等价，只是字段合并，不影响可观察行为。）
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
                bool boChg = false;
                bool boNeedRecalc = false;
                for (var i = m_dwStatusArrTick.GetLowerBound(0); i <= m_dwStatusArrTick.GetUpperBound(0); i++)
                {
                    if ((m_wStatusTimeArr[i] > 0) && (m_wStatusTimeArr[i] < 60000))
                    {
                        if ((HUtil32.GetTickCount() - m_dwStatusArrTick[i]) > 1000)
                        {
                            m_wStatusTimeArr[i] -= 1;
                            m_dwStatusArrTick[i] += 1000;
                            if (m_wStatusTimeArr[i] == 0)
                            {
                                boChg = true;
                                switch (i)
                                {
                                    case Grobal2.STATE_TRANSPARENT:
                                        m_boHideMode = false;
                                        break;
                                    case Grobal2.STATE_DEFENCEUP:
                                        boNeedRecalc = true;
                                        SysMsg("防御力回复正常", MsgColor.Green, MsgType.Hint);
                                        break;
                                    case Grobal2.STATE_MAGDEFENCEUP:
                                        boNeedRecalc = true;
                                        SysMsg("魔法防御力回复正常", MsgColor.Green, MsgType.Hint);
                                        break;
                                    case Grobal2.STATE_BUBBLEDEFENCEUP:
                                        m_boAbilMagBubbleDefence = false;
                                        break;
                                }
                            }
                        }
                    }
                }
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
                                    SysMsg("攻击力回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 1:
                                    SysMsg("魔法力回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 2:
                                    SysMsg("道术回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 3:
                                    SysMsg("攻击速度回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 4:
                                    SysMsg("生命值回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                                case 5:
                                    SysMsg("魔法值回复正常", MsgColor.Green, MsgType.Hint);
                                    break;
                            }
                        }
                    }
                }
                if (boChg)
                {
                    m_nCharStatus = GetCharStatus();
                    StatusChanged();
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
        /// The native abort branch additionally emits a log record through
        /// <c>sub_768BE0</c> -> <c>sub_79D3D8</c> (a 0xBC-byte record stamped with magic
        /// 0x33AABB77 and kind byte 0xA2) carrying the GBK literals "怪物爆出被防沉迷"
        /// (0x7200D0, length prefix 16) and "被防沉迷" (0x7200EC, length prefix 8).  That
        /// is a log-service record, not an SM_* packet, and its field layout is not
        /// established (SPWN-30 / SPWN-31 are BLOCKED on the ecx/pushed-parameter
        /// identities), so it is deliberately not reproduced here rather than guessed at.
        /// </summary>
        private static bool NativeAfterScatterItemsBlocked(TBaseObject killer)
        {
            // 0x71FAB4 + 0x71FACE: only a non-nil, RC_PLAYOBJECT killer reaches the tests.
            if (killer == null || killer.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                return false;
            if (killer is TPlayObject player
                && (player.m_btNativeFatigueTier == 3
                    || player.m_btNativeCheatPenaltyTier == 3))
            {
                return true;
            }
            return killer.HasNativeActiveState(25);
        }

        public virtual void Die()
        {
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
                m_Master = null;
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg1);
                M2Share.ErrorMessage(e.Message);
            }
            try
            {
                var boPK = false;
                if (!M2Share.g_Config.boVentureServer && !m_PEnvir.Flag.boFightZone && !m_PEnvir.Flag.boFight3Zone)
                {
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT && m_LastHiter != null && PKLevel() < 2)
                    {
                        if ((m_LastHiter.m_btRaceServer == Grobal2.RC_PLAYOBJECT) || (m_LastHiter.m_btRaceServer == Grobal2.RC_NPC))//允许NPC杀死人物
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
                if (boPK && m_LastHiter != null)
                {
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
                                // 原版 sub_6C0FE4: 受害者 PK点(a2+352) ≤ off_7D5FAC 时，对凶手 [+0x164] -= 1。
                                // 原 config nKillHumanDecLuckPoint 系伪造，原生为固定 -1。
                                m_LastHiter.AddBodyLuck(-1);
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
                var deathDropOutcome = NativeDeathDropPolicy.Outcome.NormalEquipThenBag;
                if (nativeHumanKindDeath)
                {
                    deathDropOutcome = NativeDeathDropPolicy.Resolve(m_PEnvir.Flag, InSafeZone());
                }
                var deathDropsAnything = nativeHumanKindDeath
                    ? deathDropOutcome != NativeDeathDropPolicy.Outcome.DropNothing
                    : !m_PEnvir.Flag.boFightZone && !m_PEnvir.Flag.boFight3Zone && !m_boAnimal;

                // 0x74143E / 0x74144E: each special flag selects its OWN exclusive worker
                // (sub_740300 / sub_748D48) instead of the normal sub_73FC70 + sub_740078
                // pair.  Neither worker is portable yet: sub_740300 @0x74036A filters the
                // bag by Delphi class TSpecialDropItem ([0x783434]) and rolls the per-item
                // percent at [item+0x100] (sub_78BCBC: `mov eax,0x64; call Random; cmp
                // eax,[ebx+0x100]; setl al`), and sub_748D48 @0x748DA3-0x748DB1 resolves a
                // per-map quota record through sub_784568 + sub_77C028.  C# has neither the
                // item-class classifier (see the sub_74DAE4 `+0xFC` gap) nor the quota
                // table, so these two stay BLOCKED and FAIL CLOSED to "drop nothing"
                // rather than falling back to the normal pair, which would dump the whole
                // bag on a map the operator configured to restrict drops.
                if (deathDropOutcome == NativeDeathDropPolicy.Outcome.OnlyDropSpecWorker
                    || deathDropOutcome == NativeDeathDropPolicy.Outcome.LimitBagItemDropWorker)
                {
                    deathDropsAnything = false;
                }

                if (deathDropsAnything)
                {
                    var AttackBaseObject = m_ExpHitter;
                    if (m_ExpHitter != null && m_ExpHitter.m_Master != null)
                    {
                        AttackBaseObject = m_ExpHitter.m_Master;
                    }
                    if (m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                    {
                        var scatteredItems = new List<KeyValuePair<string, string>>();
                        // All three exits land on 0x720092, which is past the
                        // @AfterScatterItems callback at 0x720062, so one boolean
                        // covers segments 1-4 and the callback alike.  Order matters:
                        // 0x71FA50 runs before 0x71FA8A and 0x71FAD7 and arms
                        // unconditionally, so TryEnterNativeScatter must be leftmost.
                        //
                        //   71FA8A  83 B8 74 04 00 00 00  cmp dword [eax+0x474],0
                        //   71FA91  0F 84 FB 05 00 00     je 0x720092
                        //
                        // A monster with no drop table leaves the function before
                        // segment 1, so the exclusive chain, the world drop and the
                        // gold settlement never run for it either — C# had this gate on
                        // segment 2 alone.  A null UserEngine fails closed because the
                        // three segments would fault on it anyway.
                        //
                        // m_boNoItem joins them because monster Die gates the whole
                        // scatter on it one level up, immediately before the virtual
                        // call, rather than on the gold segment alone:
                        //   71E3B7  80 B8 7D 04 00 00 00  cmp byte [eax+0x47D],0
                        //   71E3BE  75 35                 jne 0x71E3F5   ; skips both
                        //   71E3C4  6A 00 / 6A 01         push 0 / push 1
                        //   71E3D2  FF 96 FC 01 00 00     call [esi+0x1FC]
                        var scatterBlocked = !TryEnterNativeScatter()
                            || M2Share.UserEngine == null
                            || !M2Share.UserEngine.NativeHasMonsterDropTable(m_sCharName)
                            || m_boNoItem
                            || NativeAfterScatterItemsBlocked(AttackBaseObject);
                        if (!scatterBlocked)
                        {
                            // 战神 sub_71FA20 segment 1, 0x71FB2E-0x71FCFF: the
                            // MonItemsTree exclusive chain runs FIRST, before the
                            // monster's own drop table at 0x71FCFF.  The C# code for it
                            // existed but had no caller in the whole tree, so every
                            // MonItemsTree.txt row produced nothing.
                            M2Share.UserEngine.TraverseMonItemsTree(m_sCharName,
                                AttackBaseObject, this, scatteredItems);
                            NativeDropControlRuntime.RunInNativeOrder(
                                () =>
                                {
                                    if (this is not HeroObject)
                                        M2Share.UserEngine.MonGetRandomItems(this, AttackBaseObject);
                                },
                                () => NativeDropControlRuntime.TryScatter(this,
                                    AttackBaseObject, scatteredItems));
                        }
                        DropUseItems(AttackBaseObject, scatteredItems);
                        if (m_Master == null && (!m_boNoItem || !m_PEnvir.Flag.boNODROPITEM))
                        {
                            ScatterBagItems(AttackBaseObject, scatteredItems);
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
                            if (M2Share.g_Config.boDieDropGold)
                            {
                                ScatterGolds(null);
                            }
                        }
                    }
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
                    if (m_GroupOwner != null)
                    {
                        m_GroupOwner.DelMember(this);// 人物死亡立即退组，以防止组队刷经验
                    }
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
                        tStr = "####";
                    }
                    M2Share.AddGameDataLog("19" + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + "FZ-" + HUtil32.BoolToIntStr(m_PEnvir.Flag.boFightZone) + "_F3-" + HUtil32.BoolToIntStr(m_PEnvir.Flag.boFight3Zone) + "\t" + '0' + "\t" + '1' + "\t" + tStr);
                }
                
                if (m_Master == null && !m_boDelFormMaped)
                {
                    m_PEnvir.DelObjectCount(this);
                    m_boDelFormMaped = true;
                }
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
                if (M2Share.g_Config.boPKLevelProtect)// 新人保护
                {
                    if (m_Abil.Level > M2Share.g_Config.nPKProtectLevel)// 如果大于指定等级
                    {
                        if (!BaseObject.m_boPKFlag && BaseObject.m_Abil.Level <= M2Share.g_Config.nPKProtectLevel &&
                            BaseObject.PKLevel() < 2)// 被攻击的人物小指定等级没有红名，则不可以攻击。
                        {
                            result = false;
                            return result;
                        }
                    }
                    if (m_Abil.Level <= M2Share.g_Config.nPKProtectLevel)// 如果小于指定等级
                    {
                        if (!BaseObject.m_boPKFlag && BaseObject.m_Abil.Level > M2Share.g_Config.nPKProtectLevel && BaseObject.PKLevel() < 2)
                        {
                            result = false;
                            return result;
                        }
                    }
                }
                
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

        public virtual void MakeGhost()
        {
            if (m_boCanReAlive)
            {
                m_boInvisible = true;
                m_dwGhostTick = HUtil32.GetTickCount();
                RemoveFromMapForGhost();
                SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
            }
            else
            {
                m_boGhost = true;
                m_dwGhostTick = HUtil32.GetTickCount();
                RemoveFromMapForGhost();
                SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
            }
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

        
        
        
        
        protected virtual void ScatterBagItems(TBaseObject ItemOfCreat)
        {
            ScatterBagItems(ItemOfCreat, null);
        }

        private void ScatterBagItems(TBaseObject ItemOfCreat,
            IList<KeyValuePair<string, string>> scatteredItems)
        {
            TUserItem UserItem;
            GoodItem StdItem;
            const string sExceptionMsg = "[Exception] TBaseObject::ScatterBagItems";
            try
            {
                var DropWide = HUtil32._MIN(M2Share.g_Config.nDropItemRage, 7);
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
                if (m_boNoDropUseItem)
                {
                    return;
                }
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
                if (PKLevel() > 2)
                {
                    nRate = 15;
                }
                else
                {
                    nRate = 30;
                }
                // Heroes share THumanKind.Die -> sub_73FC70 with players. The
                // plugin rewrite is a process-wide patch, so HeroObject must
                // honour it too. Monsters do not enter this worker natively.
                var dropCount = 0;
                var deathDropPatched = false;
                var patchedCap = 2;
                if (this is HeroObject)
                {
                    deathDropPatched = new YanshenApi(null, null, M2Share.PluginManager)
                        .TryGetDeathEquipDropPatch(PKLevel() > 2, out var patchedRate, out patchedCap);
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
                        // 0x73FF0B `call sub_75F3E8` clears the slot (0x75F40F
                        // `mov [esi+eax*4+8],0`) — the object leaves the slot and is freed,
                        // it is NOT left as a zombie entry with wIndex = 0.
                        m_UseItems[nC] = null;
                        if (!string.IsNullOrEmpty(notice))
                        {
                            SysMsg(notice + " "
                                + M2Share.UserEngine.GetStdItemName(destroyed.wIndex),
                                MsgColor.Red, MsgType.Hint);
                        }
                        Dispose(destroyed);                 // 0x73FEC4 sub_404690
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
