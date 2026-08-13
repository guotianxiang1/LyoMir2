using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private bool ClientHitXY(int wIdent, int nX, int nY, byte nDir, bool boLateDelivery, ref int dwDelayTime)
        {
            var result = false;
            short n14 = 0;
            short n18 = 0;
            const string sExceptionMsg = "[Exception] TPlayObject::ClientHitXY";
            dwDelayTime = 0;
            try
            {
                if (!m_boCanHit)
                {
                    return result;
                }
                if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanHit)// 防麻
                {
                    return result;
                }
                if (wIdent == Grobal2.CM_SWORD_HIT &&
                    (!m_boSunSwordReady || m_MagicArr[SpellsDef.SKILL_58] == null))
                {
                    return result;
                }
                if (!M2Share.g_Config.boSpeedHackCheck)
                {
                    if (!boLateDelivery)
                    {
                        if (!CheckActionStatus(wIdent, ref dwDelayTime))
                        {
                            m_boFilterAction = false;
                            return result;
                        }
                        m_boFilterAction = true;
                        int dwAttackTime = HUtil32._MAX(0, M2Share.g_Config.dwHitIntervalTime - m_nHitSpeed * M2Share.g_Config.ClientConf.btItemSpeed);
                        int dwCheckTime = HUtil32.GetTickCount() - m_dwAttackTick;
                        if (dwCheckTime < dwAttackTime)
                        {
                            m_dwAttackCount++;
                            dwDelayTime = dwAttackTime - dwCheckTime;
                            if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed)
                            {
                                if (m_dwAttackCount >= 4)
                                {
                                    m_dwAttackTick = HUtil32.GetTickCount();
                                    m_dwAttackCount = 0;
                                    dwDelayTime = M2Share.g_Config.dwDropOverSpeed;
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg("攻击攻击忙复位忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    m_dwAttackCount = 0;
                                }
                                return result;
                            }
                            else
                            {
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg("攻击步忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                                }
                                return result;
                            }
                        }

                    }
                }
                if (nX == m_nCurrX && nY == m_nCurrY)
                {
                    result = true;
                    m_dwAttackTick = HUtil32.GetTickCount();
                    if (wIdent == Grobal2.CM_HEAVYHIT && m_UseItems[Grobal2.U_WEAPON] != null && m_UseItems[Grobal2.U_WEAPON].Dura > 0)// 挖矿
                    {
                        if (GetFrontPosition(ref n14, ref n18) && !m_PEnvir.CanWalk(n14, n18, false))
                        {
                            GoodItem StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_WEAPON].wIndex);
                            if (StdItem != null && StdItem.Shape == 19)
                            {
                                if (PileStones(n14, n18))
                                {
                                    SendSocket("=DIG");
                                }
                                m_nHealthTick -= 30;
                                m_nSpellTick -= 50;
                                m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                                DecreaseHealthSpellRecoveryStep(2);
                                return result;
                            }
                        }
                    }
                    switch (wIdent)
                    {
                        case Grobal2.CM_HIT:
                            AttackDir(null, 0, nDir);
                            break;
                        case Grobal2.CM_HEAVYHIT:
                            AttackDir(null, 1, nDir);
                            break;
                        case Grobal2.CM_BIGHIT:
                            AttackDir(null, 2, nDir);
                            break;
                        case Grobal2.CM_POWERHIT:
                            AttackDir(null, 3, nDir);
                            break;
                        case Grobal2.CM_LONGHIT:
                            AttackDir(null, 4, nDir);
                            break;
                        case Grobal2.CM_WIDEHIT:
                            AttackDir(null, 5, nDir);
                            break;
                        case Grobal2.CM_FIREHIT:
                            AttackDir(null, 7, nDir);
                            break;
                        case Grobal2.CM_CRSHIT:
                            AttackDir(null, 8, nDir);
                            break;
                        case Grobal2.CM_TWINHIT:
                            AttackDir(null, 9, nDir);
                            break;
                        case Grobal2.CM_42HIT:
                            AttackDir(null, 10, nDir);
                            AttackDir(null, 11, nDir);
                            break;
                        case Grobal2.CM_SWORD_HIT:
                            if (!ReleaseSunSword(nDir))
                            {
                                return false;
                            }
                            break;
                    }
                    if (m_MagicArr[SpellsDef.SKILL_YEDO] != null &&
                        m_UseItems[Grobal2.U_WEAPON] != null &&
                        m_UseItems[Grobal2.U_WEAPON].Dura > 0)
                    {
                        m_btAttackSkillCount -= 1;
                        if (m_btAttackSkillPointCount == m_btAttackSkillCount)
                        {
                            m_boPowerHit = true;
                            SendSocket("+PWR");
                        }
                        if (m_btAttackSkillCount <= 0)
                        {
                            m_btAttackSkillCount = (byte)(7 - m_MagicArr[SpellsDef.SKILL_YEDO].btLevel);
                            m_btAttackSkillPointCount = (byte)M2Share.RandomNumber.Random(m_btAttackSkillCount);
                        }
                    }
                    m_nHealthTick -= 30;
                    m_nSpellTick -= 100;
                    m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                    DecreaseHealthSpellRecoveryStep(2);
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        private bool ClientHorseRunXY(short wIdent, int nX, int nY, bool boLateDelivery, ref int dwDelayTime)
        {
            var result = false;
            byte n14;
            int dwCheckTime;
            dwDelayTime = 0;
            if (!m_boCanRun)
            {
                return result;
            }
            // MOVE-10: State 52 gate applied across all four native movement
            // arms (walk 0x6D9BD5, run 0x6D9CF1, and by inference horserun
            // and run3). When a player is riding someone else's horse (state
            // 52 = passenger, not driver), they cannot initiate their own
            // movement. Native checks `test byte ptr [esi+0x169],4` before
            // any other movement logic. The gate is silent (no message) and
            // returns FALSE with dwDelayTime=0, triggering SM_ACT_FAIL.
            if (HasNativeActiveState(52))
            {
                return result;
            }
            // MOVE-15 bypass closure — NOT a byte-faithful port, and marked as
            // such deliberately. CM_HORSERUN(3035) has NO native movement
            // handler: the main CM dispatcher's jumptable at 0x6D858B stops at
            // 3017, and the only native 3035 is a broadcast ident inside
            // sub_6EC078 (case label at 0x6EC29C, `mov cx,0x3F9` then
            // sub_7707A8) reached from the HIT cases, which never touches X/Y.
            // So there is no native EA to cite for this gate. It is added
            // because leaving it open would let a mounted player keep moving
            // through the cast window that 3011/3013/4108 all refuse, which
            // would reintroduce the very defect MOVE-15 describes via the one
            // movement opcode native does not have.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanRun)// 防麻
            {
                return result;
            }
            if (!M2Share.g_Config.boSpeedHackCheck)
            {
                if (!boLateDelivery)
                {
                    if (!CheckActionStatus(wIdent, ref dwDelayTime))
                    {
                        m_boFilterAction = false;
                        return result;
                    }
                    m_boFilterAction = true;
                    dwCheckTime = HUtil32.GetTickCount() - m_dwMoveTick;
                    if (dwCheckTime < M2Share.g_Config.dwRunIntervalTime)
                    {
                        m_dwMoveCount++;
                        dwDelayTime = M2Share.g_Config.dwRunIntervalTime - dwCheckTime;
                        if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed)
                        {
                            if (m_dwMoveCount >= 4)
                            {
                                m_dwMoveTick = HUtil32.GetTickCount();
                                m_dwMoveCount = 0;
                                dwDelayTime = M2Share.g_Config.dwDropOverSpeed;
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg("马跑步忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                                }
                            }
                            else
                            {
                                m_dwMoveCount = 0;
                            }
                            return result;
                        }
                        else
                        {
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("马跑步忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                            return result;
                        }
                    }
                }
            }
            m_dwMoveTick = HUtil32.GetTickCount();
            m_bo316 = false;
#if Debug
            Debug.WriteLine(format("当前X:{0} 当前Y:{1} 目标X:{2} 目标Y:{3}", new object[] {this.m_nCurrX, this.m_nCurrY, nX, nY}), TMsgColor.c_Green, TMsgType.t_Hint);
#endif
            n14 = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nX, nY);
            if (HorseRunTo(n14, false))
            {
                if (m_boTransparent && m_boHideMode)
                {
                    m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 1;
                }
                if (m_bo316 || m_nCurrX == nX && m_nCurrY == nY)
                {
                    result = true;
                }
                m_nHealthTick -= 60;
                m_nSpellTick -= 10;
                m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                DecreaseHealthSpellRecoveryStep(1);
            }
            else
            {
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }

        private bool ClientSpellXY(short wIdent, int nKey, int nTargetX, int nTargetY, TBaseObject TargeTBaseObject, bool boLateDelivery, ref int dwDelayTime)
        {
            var result = false;
            dwDelayTime = 0;
            if (!m_boCanSpell)
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanSpell)// 防麻
            {
                return result;
            }
            // MOVE-90: 战神 0x6DA12B: NOMAGIC map flag consumer in movement dispatcher magic branch.
            // When the map has NOMAGIC set, magic spells are blocked.
            if (m_PEnvir != null && m_PEnvir.Flag.boNOMAGIC)
            {
                return result;
            }
            // Native sub_6BC510 @0x6BC541-0x6BC566 — the per-map / per-cell
            // skill-forbid gate, which sits BEFORE every other stage: before the
            // +0xA24/+0xA4C switch-cleanup calls (0x6BC576/0x6BC586), before
            // GetTickCount (0x6BC597) and all interval / strike-counter
            // bookkeeping, and before GetMagicInfo (VMT+0xE8 @0x6BC5CB).
            //   6BC541  movzx edx,bx          ; edx = the RAW wire skill index
            //   6BC546  call  sub_772A50
            //   6BC54D  jne   0x6bc56b        ; TRUE = allowed
            //   6BC54F  mov   cx,0xFFDB       ; -37
            //   6BC553  mov   edx,0x6BCD18    ; "当前区域不可使用该技能"
            //   6BC55C  call  [ebx+0xD4]      ; SysMsg
            //   6BC562  mov   byte [ebp-5],0  ; result = FALSE
            // sub_772A50 @0x772A5A-0x772A8E: starts allowed (`mov bl,1`), then
            //   (a) sub_77BE88(m_PEnvir[+0x128], CurrX[+0x12C], CurrY[+0x130])
            //       = the per-cell byte at map[+0x38] + (x*map[+0x40]+y)*12 + 4;
            //       non-zero -> DENY, and the id list is then NOT consulted;
            //   (b) sub_77BCF4(m_PEnvir, magIdx) = linear scan of the map's
            //       +0x28 int TList for the raw index -> DENY.
            // sub_77BE88 @0x77BE8D-0x77BE9D returns 0 (=allowed) for x<0, y<0,
            // x>=Width[+0x3C] or y>=Height[+0x40], i.e. out-of-bounds fails OPEN.
            // Envirnoment.IsSkillAllowedAt reproduces both halves in that order.
            // Because the gate runs before GetMagicInfo, it is keyed on nKey (the
            // raw wire index), not on a resolved UserMagic.
            if (m_PEnvir == null ||
                !m_PEnvir.IsSkillAllowedAt(m_nCurrX, m_nCurrY, nKey))
            {
                // GBK bytes at 0x6BCD18, long-string length field [0x6BCD14] = 22.
                // 0x6BC54F sends cx=0xFFDB. cx unpacks as FColor = cx & 0xFF,
                // BColor = cx >> 8, so 0xFFDB is 0xDB/0xFF == MsgColor.Green.
                // (MsgColor.Red is 0x38FF -- the wrong channel here.)
                SysMsg("当前区域不可使用该技能", MsgColor.Green, MsgType.Hint);
                return result;
            }
            var UserMagic = GetMagicInfo(nKey);
            if (UserMagic == null)
            {
                return result;
            }
            var boIsWarrSkill = M2Share.MagicManager.IsWarrSkill(UserMagic.wMagIdx);
            if (!boLateDelivery && !boIsWarrSkill && (!M2Share.g_Config.boSpeedHackCheck))
            {
                if (!CheckActionStatus(wIdent, ref dwDelayTime))
                {
                    m_boFilterAction = false;
                    return result;
                }
                m_boFilterAction = true;
                var dwCheckTime = HUtil32.GetTickCount() - m_dwMagicAttackTick;
                if (dwCheckTime < m_dwMagicAttackInterval)
                {
                    m_dwMagicAttackCount++;
                    dwDelayTime = m_dwMagicAttackInterval - dwCheckTime;
                    if (dwDelayTime > M2Share.g_Config.dwMagicHitIntervalTime / 3)
                    {
                        if (m_dwMagicAttackCount >= 4)
                        {
                            m_dwMagicAttackTick = HUtil32.GetTickCount();
                            m_dwMagicAttackCount = 0;
                            dwDelayTime = M2Share.g_Config.dwMagicHitIntervalTime / 3;
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("魔法忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                        }
                        else
                        {
                            m_dwMagicAttackCount = 0;
                        }
                        return result;
                    }
                    else
                    {
                        if (m_boTestSpeedMode)
                        {
                            SysMsg("魔法忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                        }
                        return result;
                    }
                }
            }
            m_nSpellTick -= 450;
            m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
            if (!boIsWarrSkill)
            {
                m_dwMagicAttackInterval = UserMagic.MagicInfo.dwDelayTime + M2Share.g_Config.dwMagicHitIntervalTime;
            }
            m_dwMagicAttackTick = HUtil32.GetTickCount();
            ushort nSpellPoint;
            switch (UserMagic.wMagIdx)
            {
                case SpellsDef.SKILL_ERGUM:
                    if (m_MagicArr[SpellsDef.SKILL_ERGUM] != null)
                    {
                        if (!m_boUseThrusting)
                        {
                            ThrustingOnOff(true);
                            SendSocket("+LNG");
                        }
                        else
                        {
                            ThrustingOnOff(false);
                            SendSocket("+ULNG");
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_BANWOL:
                    if (m_MagicArr[SpellsDef.SKILL_BANWOL] != null)
                    {
                        if (!m_boUseHalfMoon)
                        {
                            HalfMoonOnOff(true);
                            SendSocket("+WID");
                        }
                        else
                        {
                            HalfMoonOnOff(false);
                            SendSocket("+UWID");
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_REDBANWOL:
                    if (m_MagicArr[SpellsDef.SKILL_REDBANWOL] != null)
                    {
                        if (!m_boRedUseHalfMoon)
                        {
                            RedHalfMoonOnOff(true);
                            SendSocket("+WID");
                        }
                        else
                        {
                            RedHalfMoonOnOff(false);
                            SendSocket("+UWID");
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_FIRESWORD:
                    if (m_MagicArr[SpellsDef.SKILL_FIRESWORD] != null)
                    {
                        if (AllowFireHitSkill())
                        {
                            nSpellPoint = GetSpellPoint(UserMagic);
                            if (m_WAbil.MP >= nSpellPoint)
                            {
                                if (nSpellPoint > 0)
                                {
                                    DamageSpell(nSpellPoint);
                                    HealthSpellChanged();
                                }
                                SendSocket("+FIR");
                            }
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_58:
                    if (m_MagicArr[SpellsDef.SKILL_58] != null)
                    {
                        var now = HUtil32.GetTickCount();
                        if (!m_boSunSwordReady &&
                            unchecked(now - m_dwLatestSunSwordTick) >= 15 * 1000)
                        {
                            nSpellPoint = GetSpellPoint(UserMagic);
                            if (m_WAbil.MP >= nSpellPoint)
                            {
                                if (nSpellPoint > 0)
                                {
                                    DamageSpell(nSpellPoint);
                                    HealthSpellChanged();
                                }
                                m_dwLatestSunSwordTick = now;
                                var readyMessage = Grobal2.MakeDefaultMsg(
                                    Grobal2.SM_SWORDHIT_ON, 0, 0, 0, 0);
                                SendSocket(readyMessage);
                                m_boSunSwordReady = true;
                            }
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_MOOTEBO:
                    result = true;
                    if (TryStartNativeMotaeboForcedMove(
                            UserMagic, (byte)nTargetX))
                    {
                        TrainNativeMotaeboMagic(UserMagic,
                            M2Share.RandomNumber.Random(3) + 1,
                            HUtil32.GetTickCount());
                    }
                    break;
                case SpellsDef.SKILL_CROSSMOON:
                    if (m_MagicArr[SpellsDef.SKILL_CROSSMOON] != null)
                    {
                        if (!m_boCrsHitkill)
                        {
                            SkillCrsOnOff(true);
                            SendSocket("+CRS");
                        }
                        else
                        {
                            SkillCrsOnOff(false);
                            SendSocket("+UCRS");
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_TWINBLADE:// 狂风斩
                    if (m_MagicArr[SpellsDef.SKILL_TWINBLADE] != null)
                    {
                        if (AllowTwinHitSkill())
                        {
                            nSpellPoint = GetSpellPoint(UserMagic);
                            if (m_WAbil.MP >= nSpellPoint)
                            {
                                if (nSpellPoint > 0)
                                {
                                    DamageSpell(nSpellPoint);
                                    HealthSpellChanged();
                                }
                                SendSocket("+TWN");
                            }
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_43:// 破空剑
                    if (m_MagicArr[SpellsDef.SKILL_43] != null)
                    {
                        if (!m_bo43kill)
                        {
                            Skill43OnOff(true);
                            SendSocket("+CID");
                        }
                        else
                        {
                            Skill43OnOff(false);
                            SendSocket("+UCID");
                        }
                    }
                    result = true;
                    break;
                default:
                    m_btDirection = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nTargetX, nTargetY); ;
                    TBaseObject BaseObject = null;
                    if (CretInNearXY(TargeTBaseObject, nTargetX, nTargetY)) 
                    {
                        BaseObject = TargeTBaseObject;
                        nTargetX = BaseObject.m_nCurrX;
                        nTargetY = BaseObject.m_nCurrY;
                    }
                    if (!DoSpell(UserMagic, (short)nTargetX, (short)nTargetY, BaseObject))
                    {
                        SendRefMsg(Grobal2.RM_MAGICFIREFAIL, 0, 0, 0, 0, "");
                    }
                    result = true;
                    break;
            }
            return result;
        }

        private bool ClientRunXY(int wIdent, int nX, int nY, int nFlag, ref int dwDelayTime)
        {
            bool result = false;
            byte nDir;
            dwDelayTime = 0;
            if (!m_boCanRun)
            {
                return result;
            }
            // MOVE-10: Native run @ 0x6D9CF1 refuses state 52 (riding someone
            // else's horse) silently before any other gate. The check occurs
            // at `test byte ptr [esi+0x169],4` (state bitset at obj+0x168,
            // bit index 52 = byte 6 bit 4 = mask 0x04 at +0x16E). When set,
            // the handler returns FALSE without setting dwDelayTime, so the
            // caller sends SM_ACT_FAIL with the current position.
            if (HasNativeActiveState(52))
            {
                return result;
            }
            // MOVE-15 — same gate on the run ladder: `call [ecx+0x40]` at
            // 0x6D9D23 (run case 3013), ahead of the run primitive
            // sub_6BBFBC at 0x6D9D39. Run passes dl=1 to the inherited
            // predicate and walk passes 0 (that arg only selects the
            // bodyState 0x18 term at 0x76B398, `test al,bl`); the +0x574
            // term at 0x6E6716 is arg-independent, so it applies identically
            // to walk and run.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanRun)
            {
                return result;
            }
            if (nFlag != wIdent && (!M2Share.g_Config.boSpeedHackCheck))
            {
                if (!CheckActionStatus(wIdent, ref dwDelayTime))
                {
                    m_boFilterAction = false;
                    return result;
                }
                m_boFilterAction = true;
                int dwCheckTime = HUtil32.GetTickCount() - m_dwMoveTick;
                if (dwCheckTime < M2Share.g_Config.dwRunIntervalTime)
                {
                    m_dwMoveCount++;
                    dwDelayTime = M2Share.g_Config.dwRunIntervalTime - dwCheckTime;
                    if (dwDelayTime > M2Share.g_Config.dwRunIntervalTime / 3)
                    {
                        if (m_dwMoveCount >= 4)
                        {
                            m_dwMoveTick = HUtil32.GetTickCount();
                            m_dwMoveCount = 0;
                            dwDelayTime = M2Share.g_Config.dwRunIntervalTime / 3;
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("跑步忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                        }
                        else
                        {
                            m_dwMoveCount = 0;
                        }
                        return result;
                    }
                    else
                    {
                        if (m_boTestSpeedMode)
                        {
                            SysMsg("跑步忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                        }
                        return result;
                    }
                }
            }
            m_dwMoveTick = HUtil32.GetTickCount();
            m_bo316 = false;
            nDir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nX, nY);
            if (RunTo(nDir, false, nX, nY))
            {
                if (m_boTransparent && m_boHideMode)
                {
                    m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 1;
                }
                if (m_bo316 || m_nCurrX == nX && m_nCurrY == nY)
                {
                    result = true;
                }
                m_nHealthTick -= 60;
                m_nSpellTick -= 10;
                m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                DecreaseHealthSpellRecoveryStep(1);
            }
            else
            {
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }

        private bool ClientWalkXY(int wIdent, int nX, int nY, bool boLateDelivery, ref int dwDelayTime)
        {
            bool result = false;
            int n14;
            int n18;
            int n1C;
            dwDelayTime = 0;
            if (!m_boCanWalk)
            {
                return result;
            }
            // MOVE-10: Native walk @ 0x6D9BD5 refuses state 52 (riding someone
            // else's horse) silently before any other gate. The check occurs
            // at `test byte ptr [esi+0x169],4` (state bitset at obj+0x168,
            // bit index 52 = byte 6 bit 4 = mask 0x04 at +0x16E). When set,
            // the handler returns FALSE without setting dwDelayTime, so the
            // caller sends SM_ACT_FAIL with the current position.
            if (HasNativeActiveState(52))
            {
                return result;
            }
            // MOVE-15 — gate 4 of the native walk ladder is `call [ecx+0x40]`
            // at 0x6D9C07 (walk case 3011), the TPlayer can-act override
            // sub_6E6700, which refuses while the cast lock +0x574 is set.
            // It runs BEFORE the walk primitive sub_6BBCD8 (0x6D9C1D), so the
            // refusal must precede CheckActionStatus and all interval
            // bookkeeping here. Leaving dwDelayTime at 0 makes the caller
            // answer SM_ACT_FAIL(630) with X/Y/Dir, which is native's 0x276
            // correction at 0x6D9C4B.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanWalk)
            {
                return result;
            }
            if (!boLateDelivery && (!M2Share.g_Config.boSpeedHackCheck))
            {
                if (!CheckActionStatus(wIdent, ref dwDelayTime))
                {
                    m_boFilterAction = false;
                    return result;
                }
                m_boFilterAction = true;
                int dwCheckTime = HUtil32.GetTickCount() - m_dwMoveTick;
                if (dwCheckTime < M2Share.g_Config.dwWalkIntervalTime)
                {
                    m_dwMoveCount++;
                    dwDelayTime = M2Share.g_Config.dwWalkIntervalTime - dwCheckTime;
                    if (dwDelayTime > M2Share.g_Config.dwWalkIntervalTime / 3)
                    {
                        if (m_dwMoveCount >= 4)
                        {
                            m_dwMoveTick = HUtil32.GetTickCount();
                            m_dwMoveCount = 0;
                            dwDelayTime = M2Share.g_Config.dwWalkIntervalTime / 3;
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("走路忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                        }
                        else
                        {
                            m_dwMoveCount = 0;
                        }
                        return result;
                    }
                    else
                    {
                        if (m_boTestSpeedMode)
                        {
                            SysMsg("走路忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                        }
                        return result;
                    }
                }
            }
            m_dwMoveTick = HUtil32.GetTickCount();
            m_bo316 = false;
            n18 = m_nCurrX;
            n1C = m_nCurrY;
            n14 = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nX, nY);
            if (!m_boClientFlag)
            {
                if (n14 == 0 && m_nStep == 0)
                {
                    m_nStep++;
                }
                else if (n14 == 4 && m_nStep == 1)
                {
                    m_nStep++;
                }
                else if (n14 == 6 && m_nStep == 2)
                {
                    m_nStep++;
                }
                else if (n14 == 2 && m_nStep == 3)
                {
                    m_nStep++;
                }
                else if (n14 == 1 && m_nStep == 4)
                {
                    m_nStep++;
                }
                else if (n14 == 5 && m_nStep == 5)
                {
                    m_nStep++;
                }
                else if (n14 == 7 && m_nStep == 6)
                {
                    m_nStep++;
                }
                else if (n14 == 3 && m_nStep == 7)
                {
                    m_nStep++;
                }
                else
                {
                    // TRADE-09: Cancel active trade before gold reduction (战神 behavior).
                    if (m_DealCreat != null)
                    {
                        DealCancel();
                    }
                    m_nGameGold -= m_nStep;
                    GameGoldChanged();
                    m_nStep = 0;
                }
                if (m_nStep != 0)
                {
                    m_nGameGold++;
                    GameGoldChanged();
                }
            }
            // MOVE-71：原版 CM_WALK 处理器 sub_6BBCE0 在 0x6BBD0C `mov cl,[ebx+0x3FE]`
            // 把缓存的穿透判定当作 WalkTo 第三参(boFlag)，再经 sub_741224(0x74122D 存、
            // 0x7412B3 压) 传给 MoveToMovingObject 的 boIgnoreOccupancy(0x779870)。此处
            // 原先恒传 false，导致玩家在安全区永不可穿人——与原版分歧。改传穿透判定。
            // MOVE-73：0x6BBD0C 是 `mov cl,[ebx+0x3FE]` —— 读缓存，不重算。
            if (WalkTo((byte)n14, m_boThroughOccupancyCache))
            {
                if (m_bo316 || m_nCurrX == nX && m_nCurrY == nY)
                {
                    result = true;
                }
                m_nHealthTick -= 10;
            }
            else
            {
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }
    }
}
