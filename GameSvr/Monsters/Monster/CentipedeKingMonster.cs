using SystemModule;

namespace GameSvr
{
    public class CentipedeKingMonster : StickMonster
    {
        public int m_dwAttickTick = 0;

        public CentipedeKingMonster() : base()
        {
            m_nViewRange = 6;
            nComeOutValue = 4;
            nAttackRange = 6;
            m_boAnimal = false;
            m_dwAttickTick = HUtil32.GetTickCount();
        }

        private bool sub_4A5B0C()
        {
            var result = false;
            TBaseObject BaseObject;
            for (var i = 0; i < m_VisibleActors.Count; i++)
            {
                BaseObject = m_VisibleActors[i].BaseObject;
                if (BaseObject.m_boDeath)
                {
                    continue;
                }
                if (IsProperTarget(BaseObject))
                {
                    if (Math.Abs(m_nCurrX - BaseObject.m_nCurrX) <= m_nViewRange && Math.Abs(m_nCurrY - BaseObject.m_nCurrY) <= m_nViewRange)
                    {
                        result = true;
                        break;
                    }
                }
            }
            return result;
        }

        protected override bool AttackTarget()
        {
            var result = false;
            TAbility WAbil;
            int nPower;
            TBaseObject BaseObject;
            if (!sub_4A5B0C())
            {
                return result;
            }
            if ((HUtil32.GetTickCount() - m_dwHitTick) > m_nNextHitTime)
            {
                m_dwHitTick = HUtil32.GetTickCount();
                SendAttackMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX, m_nCurrY);
                WAbil = m_WAbil;
                nPower = M2Share.RandomNumber.Random(HUtil32.HiWord(WAbil.DC) - HUtil32.LoWord(WAbil.DC) + 1) + HUtil32.LoWord(WAbil.DC);
                for (var i = 0; i < m_VisibleActors.Count; i++)
                {
                    BaseObject = m_VisibleActors[i].BaseObject;
                    if (BaseObject.m_boDeath)
                    {
                        continue;
                    }
                    if (IsProperTarget(BaseObject))
                    {
                        if (Math.Abs(m_nCurrX - BaseObject.m_nCurrX) < m_nViewRange && Math.Abs(m_nCurrY - BaseObject.m_nCurrY) < m_nViewRange)
                        {
                            m_dwTargetFocusTick = HUtil32.GetTickCount();
                            SendDelayMsg(this, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(BaseObject.m_nCurrX, BaseObject.m_nCurrY), 2, BaseObject.ObjectId, "", 600);
                            // POIS-28. 原版 TCentipedeKingMon VMT 0x67E07C 槽 +0x200
                            // (SelfPtr dword[V-0x4C]==V 自校验通过，类名 ShortString
                            // 逐字为 TCentipedeKingMon)，函数体 sub_6809B4：
                            //   680AB0  B8 04000000    mov eax,4
                            //   680AB5  E8 ->0x403B4C  Random(4)
                            //   680ABA  85 C0 / 75 34  test eax,eax / jne  => 外层 ==0
                            //   680ABE  B8 03000000    mov eax,3
                            //   680AC3  E8 ->0x403B4C  Random(3)
                            //   680AC8  85 C0 / 74 14  test eax,eax / je   => 内层 !=0 走 then
                            //   680ACC  6A 03          push 3            ; level 3
                            //   680ACE  66 B9 3C 00    mov cx,0x3C       ; 时长 60
                            //   680AD2  B2 1F          mov dl,0x1F       ; state 0x1F = stPoisonGreen
                            //   680ADE  EB 12          jmp  (跳过 else 臂)
                            //   680AE0  6A 00          push 0            ; level 0
                            //   680AE2  66 B9 05 00    mov cx,5          ; 时长 5
                            //   680AE6  B2 1A          mov dl,0x1A       ; state 0x1A = stPoisonStone
                            //   680AF2  89 B3 44030000 mov [ebx+0x344],esi ; m_TargetCret，两臂之后
                            // else 臂原版是 **0x1A(=26 麻痹石化)**，不是 0x33。
                            // 0x33 = csZaiMaShang(单人坐骑)、0x34 = csZaiBieRenMaShang
                            // (双人坐骑)——见状态名表；把麻痹写进坐骑位既不会被
                            // IsStoneParalyzed 之外的坐骑逻辑正确解读，也没有时长。
                            // 26 走 TryApplyNativeState26 才带上 5 秒时长与到期回收。
                            if (M2Share.RandomNumber.Random(4) == 0)
                            {
                                if (M2Share.RandomNumber.Random(3) != 0)
                                {
                                    BaseObject.MakePosion(Grobal2.POISON_DECHEALTH, 60, 3);
                                }
                                else
                                {
                                    // else 臂原生 @0x680AE6 mov dl,0x1A (state
                                    // 0x1A = stPoisonStone), push 0 (level 0),
                                    // mov cx,5 (time 5), then call [edi+0xC8] =
                                    // MakePosion (VMT+0xC8 -> 0x746604, verified
                                    // 2026-08-13). So this is石化毒, NOT State26:
                                    // the incoming TryApplyNativeState26(5) took
                                    // the wrong VMT path. dl=0x1A maps to C#
                                    // POISON_STONE exactly as the then-arm's
                                    // dl=0x1F maps to POISON_DECHEALTH.
                                    BaseObject.MakePosion(Grobal2.POISON_STONE, 5, 0);
                                }
                                m_TargetCret = BaseObject;
                            }
                        }
                    }
                }
            }
            result = true;
            return result;
        }

        protected override void ComeOut()
        {
            base.ComeOut();
            m_WAbil.HP = m_WAbil.MaxHP;
        }

        public override void Run()
        {
            TBaseObject BaseObject;
            if (!IsNativeCanActBlocked(1) && !m_boGhost && m_wStatusTimeArr[Grobal2.POISON_STONE] == 0)
            {
                if ((HUtil32.GetTickCount() - m_dwWalkTick) > m_nWalkSpeed)
                {
                    m_dwWalkTick = HUtil32.GetTickCount();
                    if (m_boFixedHideMode)
                    {
                        if ((HUtil32.GetTickCount() - m_dwAttickTick) > 10000)
                        {
                            for (var i = 0; i < m_VisibleActors.Count; i++)
                            {
                                BaseObject = m_VisibleActors[i].BaseObject;
                                if (BaseObject.m_boDeath)
                                {
                                    continue;
                                }
                                if (IsProperTarget(BaseObject))
                                {
                                    if (!BaseObject.m_boHideMode || m_boCoolEye)
                                    {
                                        // MONAI-18 — TCentipedeKingMon.Run 出洞扫描同样走
                                        // 0x680C1E mov cx,[ebx+0x4D8] / call 0x7743E0，闭区间。
                                        if (Math.Abs(m_nCurrX - BaseObject.m_nCurrX) <= nComeOutValue && Math.Abs(m_nCurrY - BaseObject.m_nCurrY) <= nComeOutValue)
                                        {
                                            ComeOut();
                                            m_dwAttickTick = HUtil32.GetTickCount();
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if ((HUtil32.GetTickCount() - m_dwAttickTick) > 3000)
                        {
                            if (AttackTarget())
                            {
                                base.Run();
                                return;
                            }
                            if ((HUtil32.GetTickCount() - m_dwAttickTick) > 10000)
                            {
                                ComeDown();
                                m_dwAttickTick = HUtil32.GetTickCount();
                            }
                        }
                    }
                }
            }
            base.Run();
        }
    }
}

