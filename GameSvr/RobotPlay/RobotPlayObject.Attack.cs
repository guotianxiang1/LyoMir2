using SystemModule;

namespace GameSvr
{
    public partial class RobotPlayObject
    {
        
        
        
        
        
        private bool WarrAttackTarget1(short wHitMode)
        {
            bool result = false;
            byte bt06 = 0;
            bool boHit;
            try
            {
                if (m_TargetCret != null)
                {
                    boHit = GetAttackDir(m_TargetCret, ref bt06);
                    if (!boHit && (wHitMode == 4 || wHitMode == 15))
                    {
                        boHit = GetAttackDir(m_TargetCret, 2, ref bt06);// 防止隔位刺杀无效果
                    }
                    if (boHit)
                    {
                        m_dwTargetFocusTick = HUtil32.GetTickCount();
                        AttackDir(m_TargetCret, wHitMode, bt06);
                        m_dwActionTick = HUtil32.GetTickCount();
                        BreakHolySeizeMode();
                        result = true;
                    }
                    else
                    {
                        if (m_TargetCret.m_PEnvir == m_PEnvir)
                        {
                            SetTargetXY(m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY);
                        }
                        else
                        {
                            DelTargetCreat();
                        }
                    }
                }
            }
            catch
            {
                M2Share.MainOutMessage("TAIPlayObject.WarrAttackTarget");
            }
            return result;
        }

        
        
        
        
        private bool WarrorAttackTarget1()
        {
            TUserMagic UserMagic;
            bool result = false;
            try
            {
                m_wHitMode = 0;
                if (m_WAbil.MP > 0)
                {
                    if (m_TargetCret != null)
                    {
                        if (m_WAbil.HP <= Math.Round(m_WAbil.MaxHP * 0.25) || m_TargetCret.m_boCrazyMode)
                        {
                            
                            if (AllowUseMagic(12))
                            {
                                
                                if (!(Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) == 2 && Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) == 0 || Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) == 1 && Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) == 0 || Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) == 1 && Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) == 1 || Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) == 2 && Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) == 2 || Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) == 0 && Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) == 1 || Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) == 0 && Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) == 2))
                                {
                                    GetGotoXY(m_TargetCret, 2);
                                    GotoTargetXY(m_nTargetX, m_nTargetY, 0);
                                }
                            }
                        }
                    }
                    SearchMagic();
                    if (m_nSelectMagic > 0)
                    {
                        UserMagic = FindMagic(m_nSelectMagic);
                        if (UserMagic != null)
                        {
                            if (UserMagic.btKey == 0)
                            {
                                switch (m_nSelectMagic)
                                {
                                    
                                    
                                    case 27:
                                    case 39:
                                    case 41:
                                    case 60:
                                    case 68:
                                    case 75:
                                        if (m_TargetCret != null)
                                        {
                                            result = UseSpell(UserMagic, m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY, m_TargetCret); 
                                            m_dwHitTick = HUtil32.GetTickCount();
                                            return result;
                                        }
                                        break;
                                    case 7:
                                        m_wHitMode = 3;
                                        break;
                                    case 12:
                                        m_wHitMode = 4;
                                        break;
                                    case 25: 
                                        m_wHitMode = 5;
                                        break;
                                    case 26: 
                                        if (UseSpell(UserMagic, m_nCurrX, m_nCurrY, m_TargetCret))
                                        {
                                            m_wHitMode = 7;
                                        }
                                        break;
                                    case 40: 
                                        m_wHitMode = 8;
                                        break;
                                    case 43: 
                                        if (UseSpell(UserMagic, m_nCurrX, m_nCurrY, m_TargetCret))
                                        {
                                            m_wHitMode = 9;
                                        }
                                        break;
                                    case 42: 
                                        if (UseSpell(UserMagic, m_nCurrX, m_nCurrY, m_TargetCret))
                                        {
                                            m_wHitMode = 12;
                                        }
                                        break;
                                }
                            }
                        }
                    }
                }

                result = WarrAttackTarget1(m_wHitMode);
                if (result)
                {
                    m_dwHitTick = HUtil32.GetTickCount();
                }
            }
            catch
            {
                
            }
            return result;
        }

        
        
        
        
        private bool WizardAttackTarget1()
        {
            TUserMagic UserMagic;
            bool result = false;
            try
            {
                m_wHitMode = 0;
                SearchMagic(); 
                if (m_nSelectMagic == 0)
                {
                    m_boIsUseMagic = true;// 是否能躲避
                }
                if (m_nSelectMagic > 0)
                {
                    if (m_TargetCret != null)
                    {
                        if (!MagCanHitTarget(m_nCurrX, m_nCurrY, m_TargetCret) || Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) > 7 || Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) > 7)
                        {
                            
                            if (m_nSelectMagic != 10)// 除疾光电影外
                            {
                                GetGotoXY(m_TargetCret, 3);// 法只走向目标3格范围
                                GotoTargetXY(m_nTargetX, m_nTargetY, 0);
                            }
                        }
                    }
                    UserMagic = FindMagic(m_nSelectMagic);
                    if (UserMagic != null)
                    {
                        if (UserMagic.btKey == 0)// 技能打开状态才能使用
                        {
                            m_dwHitTick = HUtil32.GetTickCount();
                            return UseSpell(UserMagic, m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY, m_TargetCret);
                        }
                    }
                }
                m_dwHitTick = HUtil32.GetTickCount();
                if (M2Share.g_Config.boHeroAttackTarget && m_Abil.Level < 22)
                {
                    m_boIsUseMagic = false;// 是否能躲避
                    result = WarrAttackTarget1(m_wHitMode);
                }
            }
            catch
            {
                M2Share.MainOutMessage("TAIPlayObject.WizardAttackTarget");
            }
            return result;
        }

        
        
        
        
        private bool TaoistAttackTarget()
        {
            bool result = false;
            TUserMagic UserMagic;
            try
            {
                m_wHitMode = 0;
                if (m_TargetCret != null)
                {
                    if (M2Share.g_Config.boHeroAttackTao && m_TargetCret.m_btRaceServer != Grobal2.RC_PLAYOBJECT) 
                    {
                        if (m_TargetCret.m_WAbil.MaxHP >= 700)
                        {
                            SearchMagic();// 查询魔法
                        }
                        else
                        {
                            if (HUtil32.GetTickCount() - m_dwSearchMagic > 1300) 
                            {
                                SearchMagic();// 查询魔法
                                m_dwSearchMagic = HUtil32.GetTickCount();
                            }
                            else
                            {
                                m_boIsUseAttackMagic = false;// 可以走向目标
                            }
                        }
                    }
                    else
                    {
                        SearchMagic(); 
                    }
                }
                if (m_nSelectMagic == 0)
                {
                    m_boIsUseMagic = true;// 是否能躲避 
                }
                if (m_nSelectMagic > 0)
                {
                    if (m_TargetCret != null)
                    {
                        if (!MagCanHitTarget(m_nCurrX, m_nCurrY, m_TargetCret) || Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) > 7 || Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) > 7)
                        {
                            
                            if (M2Share.g_Config.boHeroAttackTao && m_TargetCret.m_btRaceServer != Grobal2.RC_PLAYOBJECT)// 22级砍血量的怪
                            {
                                if (m_TargetCret.m_WAbil.MaxHP >= 700)
                                {
                                    GetGotoXY(m_TargetCret, 3); 
                                    GotoTargetXY(m_nTargetX, m_nTargetY, 0);
                                }
                            }
                            else
                            {
                                GetGotoXY(m_TargetCret, 3); 
                                GotoTargetXY(m_nTargetX, m_nTargetY, 0);
                            }
                        }
                    }
                    switch (m_nSelectMagic)
                    {
                        case SpellsDef.SKILL_HEALLING:// 治愈术 
                            if (m_WAbil.HP <= Math.Round(m_WAbil.MaxHP * 0.7))
                            {
                                UserMagic = FindMagic(m_nSelectMagic);
                                if (UserMagic != null && UserMagic.btKey == 0)// 技能打开状态才能使用
                                {
                                    UseSpell(UserMagic, m_nCurrX, m_nCurrY, null);
                                    m_dwHitTick = HUtil32.GetTickCount();
                                    if (M2Share.g_Config.boHeroAttackTao && m_TargetCret.m_btRaceServer != Grobal2.RC_PLAYOBJECT)// 22级砍血量的怪
                                    {
                                        if (m_TargetCret.m_WAbil.MaxHP >= 700)
                                        {
                                            m_boIsUseMagic = true;
                                            return result;
                                        }
                                        else
                                        {
                                            m_nSelectMagic = 0;
                                        }
                                    }
                                    else
                                    {
                                        m_boIsUseMagic = true;
                                        return result;
                                    }
                                }
                            }
                            break;
                        case SpellsDef.SKILL_BIGHEALLING:// 群体治疗术
                            if (m_WAbil.HP <= Math.Round(m_WAbil.MaxHP * 0.7))
                            {
                                UserMagic = FindMagic(m_nSelectMagic);
                                if (UserMagic != null && UserMagic.btKey == 0)// 技能打开状态才能使用
                                {
                                    UseSpell(UserMagic, m_nCurrX, m_nCurrY, this);
                                    m_dwHitTick = HUtil32.GetTickCount();
                                    if (M2Share.g_Config.boHeroAttackTao && m_TargetCret.m_btRaceServer != Grobal2.RC_PLAYOBJECT)// 22级砍血量的怪 
                                    {
                                        if (m_TargetCret.m_WAbil.MaxHP >= 700)
                                        {
                                            m_boIsUseMagic = true;// 能躲避
                                            return result;
                                        }
                                        else
                                        {
                                            m_nSelectMagic = 0;
                                        }
                                    }
                                    else
                                    {
                                        m_boIsUseMagic = true;// 能躲避
                                        return result;
                                    }
                                }
                            }
                            break;
                        case SpellsDef.SKILL_FIRECHARM:// 灵符火符
                            if (!MagCanHitTarget(m_nCurrX, m_nCurrY, m_TargetCret))
                            {
                                GetGotoXY(m_TargetCret, 3);
                                GotoTargetXY(m_nTargetX, m_nTargetY, 1);
                            }
                            break;
                        case SpellsDef.SKILL_AMYOUNSUL:
                        case SpellsDef.SKILL_GROUPAMYOUNSUL:
                            if (m_TargetCret.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] == 0 && GetUserItemList(2, 1) >= 0)
                            {
                                n_AmuletIndx = 1;
                            }
                            else if (m_TargetCret.m_wStatusTimeArr[Grobal2.POISON_DAMAGEARMOR] == 0 && GetUserItemList(2, 2) >= 0)
                            {
                                n_AmuletIndx = 2;
                            }
                            break;
                        case SpellsDef.SKILL_CLOAK:
                        case SpellsDef.SKILL_BIGCLOAK: 
                            UserMagic = FindMagic(m_nSelectMagic);
                            if (UserMagic != null && UserMagic.btKey == 0)// 技能打开状态才能使用
                            {
                                UseSpell(UserMagic, m_nCurrX, m_nCurrY, this);
                                m_dwHitTick = HUtil32.GetTickCount();
                                if (M2Share.g_Config.boHeroAttackTao && m_TargetCret.m_btRaceServer != Grobal2.RC_PLAYOBJECT)// 22级砍血量的怪 
                                {
                                    if (m_TargetCret.m_WAbil.MaxHP >= 700)
                                    {
                                        m_boIsUseMagic = false;
                                        return result;
                                    }
                                    else
                                    {
                                        m_nSelectMagic = 0;
                                    }
                                }
                                else
                                {
                                    m_boIsUseMagic = false;
                                    return result;
                                }
                            }
                            break;
                        case SpellsDef.SKILL_SKELLETON:
                        case SpellsDef.SKILL_SINSU:
                            UserMagic = FindMagic(m_nSelectMagic);
                            if (UserMagic != null && UserMagic.btKey == 0)
                            {
                                UseSpell(UserMagic, m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY, m_TargetCret); 
                                m_dwHitTick = HUtil32.GetTickCount();
                                if (M2Share.g_Config.boHeroAttackTao && m_TargetCret.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                                {
                                    
                                    if (m_TargetCret.m_WAbil.MaxHP >= 700)
                                    {
                                        m_boIsUseMagic = true; 
                                        return result;
                                    }
                                    else
                                    {
                                        m_nSelectMagic = 0;
                                    }
                                }
                                else
                                {
                                    m_boIsUseMagic = true; 
                                    return result;
                                }
                            }
                            break;
                    }
                    UserMagic = FindMagic(m_nSelectMagic);
                    if (UserMagic != null)
                    {
                        if (UserMagic.btKey == 0)   
                        {
                            m_dwHitTick = HUtil32.GetTickCount();
                            result = UseSpell(UserMagic, m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY, m_TargetCret); 
                            if (m_TargetCret.m_WAbil.MaxHP >= 700 || !M2Share.g_Config.boHeroAttackTao)
                            {
                                return result;
                            }
                        }
                    }
                }
                m_dwHitTick = HUtil32.GetTickCount();
                if (m_WAbil.HP <= Math.Round(m_WAbil.MaxHP * 0.15))
                {
                    m_boIsUseMagic = true;
                }
                
                
                if (M2Share.g_Config.boHeroAttackTarget && m_Abil.Level < 22 || m_TargetCret.m_WAbil.MaxHP < 700 && M2Share.g_Config.boHeroAttackTao && m_TargetCret.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                {
                    
                    if (Math.Abs(m_TargetCret.m_nCurrX - m_nCurrX) > 1 || Math.Abs(m_TargetCret.m_nCurrY - m_nCurrY) > 1)
                    {
                        
                        GotoTargetXY(m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY, 0);
                    }
                    m_boIsUseMagic = false;// 是否能躲避
                    result = WarrAttackTarget1(m_wHitMode);
                }
            }
            catch
            {
                
            }
            return result;
        }

        private bool AttackTarget()
        {
            bool result = false;
            try
            {
                if (m_TargetCret != null)
                {
                    if (InSafeZone())// 英雄进入安全区内就不打PK目标
                    {
                        if (m_TargetCret.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            m_TargetCret = null;
                            return result;
                        }
                    }
                    if (m_TargetCret == this) 
                    {
                        m_TargetCret = null;
                        return result;
                    }
                }
                m_dwTargetFocusTick = HUtil32.GetTickCount();
                if (m_boDeath || m_boGhost)
                {
                    return result;
                }
                switch (m_btJob)
                {
                    case 0:
                        if (HUtil32.GetTickCount() - m_dwHitTick > M2Share.g_Config.nAIWarrorAttackTime)
                        {
                            m_boIsUseMagic = false;// 是否能躲避
                            result = WarrorAttackTarget1();
                        }
                        break;
                    case 1:
                        if (HUtil32.GetTickCount() - m_dwHitTick > M2Share.g_Config.nAIWizardAttackTime)// 连击也不受间隔控制
                        {
                            m_dwHitTick = HUtil32.GetTickCount();
                            m_boIsUseMagic = false;// 是否能躲避
                            result = WizardAttackTarget1();
                            m_nSelectMagic = 0;
                            return result;
                        }
                        m_nSelectMagic = 0;
                        break;
                    case 2:
                        if (HUtil32.GetTickCount() - m_dwHitTick > M2Share.g_Config.nAITaoistAttackTime)
                        {
                            m_dwHitTick = HUtil32.GetTickCount();
                            m_boIsUseMagic = false; 
                            result = TaoistAttackTarget();
                            m_nSelectMagic = 0;
                            return result;
                        }
                        m_nSelectMagic = 0;
                        break;
                }
            }
            catch
            {
                M2Share.MainOutMessage("TAIPlayObject.AttackTarget");
            }
            return result;
        }

    }
}