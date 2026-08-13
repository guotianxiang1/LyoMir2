using SystemModule;

namespace GameSvr
{
    public class Monster : AnimalObject
    {
        private int m_dwThinkTick;
        protected bool bo554;
        private bool m_boDupMode;

        /// <summary>
        /// 战神 TMonster 自有字段 <c>[+0x4E4]</c>（TMonster size 0x4E8，TAnimal size 0x4D8，
        /// 故 0x4D8..0x4E7 是 TMonster 自己的 16 字节）。全镜像只有两个写入点：
        /// <c>0x66612A C6 86 E4 04 00 00 00  mov byte [esi+0x4E4],0</c>（TMonster.Create
        /// sub_66610C）与 <c>0x66D0FE C6 86 E4 04 00 00 01  mov byte [esi+0x4E4],1</c>
        /// （TStoneMonster.Create sub_66D0E0）；唯一读取点是 TMonster.Run sub_66622C 的
        /// <c>0x666302 80 BA E4 04 00 00 00  cmp byte [edx+0x4E4],0 / 0x666309 jne 0x6666E0</c>。
        /// 即：置位后整段"行走 / 攻击 / 跟随主人 / 召回 / 游荡"逻辑被跳过，只剩 inherited Run。
        /// </summary>
        protected bool m_boNativeStaticMode;
        public Monster() : base()
        {
            m_boDupMode = false;
            bo554 = false;
            m_boNativeStaticMode = false;            // MONAI-02 — TMonster.Create sub_66610C 的构造默认 race 是 80(RC_MONSTER)：
            //   00666162  C6 86 78 01 00 00 50   mov byte [esi+0x178],0x50
            // （父类 TAnimal.Create 0071D851 C6 87 78 01 00 00 32 = 50/RC_ANIMAL）
            // [+0x178] 是 race 而不是 Level：工厂 sub_679F8C 用 `movzx eax,byte [edi+0x14]`
            // 分派同一个字段，MonInitialize 把它写进 [mon+0x178]。
            // MonInitialize 之后会被 DB 记录覆盖（UsrEngn.cs MonInitialize），所以只有
            // 不经 MonInitialize 创建的怪物看得到这个默认值。
            m_btRaceServer = Grobal2.RC_MONSTER;
            m_dwThinkTick = HUtil32.GetTickCount();
            m_nViewRange = 5;
            m_nRunTime = 250;
            m_dwSearchTime = 3000 + M2Share.RandomNumber.Random(2000);
            m_dwSearchTick = HUtil32.GetTickCount();
        }

        protected TBaseObject MakeClone(string sMonName, TBaseObject OldMon)
        {
            TBaseObject result = null;
            var ElfMon = M2Share.UserEngine.RegenMonsterByName(m_PEnvir, m_nCurrX, m_nCurrY, sMonName);
            if (ElfMon != null)
            {
                ElfMon.m_Master = OldMon.m_Master;
                ElfMon.m_dwMasterRoyaltyTick = OldMon.m_dwMasterRoyaltyTick;
                ElfMon.m_btSlaveMakeLevel = OldMon.m_btSlaveMakeLevel;
                ElfMon.m_btSlaveExpLevel = OldMon.m_btSlaveExpLevel;
                ElfMon.RecalcAbilitys();
                ElfMon.RefNameColor();
                if (OldMon.m_Master != null)
                {
                    OldMon.m_Master.m_SlaveList.Add(ElfMon);
                }
                ElfMon.m_WAbil = new TAbility();
                ElfMon.m_WAbil.CopyFrom(OldMon.m_WAbil);
                ElfMon.m_wStatusTimeArr.CopyFrom(OldMon.m_wStatusTimeArr.ToArray());
                ElfMon.m_TargetCret = OldMon.m_TargetCret;
                ElfMon.m_dwTargetFocusTick = OldMon.m_dwTargetFocusTick;
                ElfMon.m_LastHiter = OldMon.m_LastHiter;
                ElfMon.m_LastHiterTick = OldMon.m_LastHiterTick;
                ElfMon.m_btDirection = OldMon.m_btDirection;
                result = ElfMon;
            }
            return result;
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            return base.Operate(ProcessMsg);
        }

        private bool Think()
        {
            var result = false;
            if ((HUtil32.GetTickCount() - m_dwThinkTick) > (3 * 1000))
            {
                m_dwThinkTick = HUtil32.GetTickCount();
                if (m_PEnvir.GetXYObjCount(m_nCurrX, m_nCurrY) >= 2)
                {
                    m_boDupMode = true;
                }
                if (!IsProperTarget(m_TargetCret))
                {
                    m_TargetCret = null;
                }
            }
            if (m_boDupMode)
            {
                // MONAI-11 — TMonster.Think sub_666184 叠格走开走的是 WalkTo(Random(8), TRUE)：
                //   006661FF  B8 08 00 00 00     mov  eax,8
                //   00666204  E8 43 D9 D9 FF     call 0x403B4C        ; Random(8)
                //   00666209  8B D0              mov  edx,eax         ; dir
                //   0066620B  B1 01              mov  cl,1            ; boFlag = 1
                //   00666211  FF 56 30           call [esi+0x30]      ; WalkTo
                //   00666214  84 C0              test al,al
                //   00666216  74 0B              je   0x666223        ; 假则仍留在 dup
                // 原先 C# 传 false 且用坐标差当成功，叠格时不能走进已占用格，解不开。
                if (WalkTo((byte)M2Share.RandomNumber.Random(8), true))
                {
                    m_boDupMode = false;
                    result = true;
                }
            }
            return result;
        }

        protected virtual bool AttackTarget()
        {
            var result = false;
            byte btDir = 0;
            if (m_TargetCret != null)
            {
                if (GetAttackDir(m_TargetCret, ref btDir))
                {
                    if (HUtil32.GetTickCount() - m_dwHitTick > m_nNextHitTime)
                    {
                        m_dwHitTick = HUtil32.GetTickCount();
                        m_dwTargetFocusTick = HUtil32.GetTickCount();
                        Attack(m_TargetCret, btDir);
                        BreakHolySeizeMode();
                    }
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
            return result;
        }

        public override void Run()
        {
            // MONAI-10 — 原生 TMonster.Run sub_66622C 的入口闸是三段，第一段是虚派发的
            // can-act 谓词，不是裸 m_boDeath：
            //   0066626A  B2 01                 mov  dl,1
            //   0066626F  8B 08                 mov  ecx,[eax]
            //   00666271  FF 51 40              call [ecx+0x40]        ; = sub_76B354
            //   00666276  0F 84 64 04 00 00     je   0x6666E0          ; 假 -> 只跑 inherited
            //   0066627F  80 B8 E3 02 00 00 00  cmp  byte [eax+0x2E3],0 ; m_boFixedHideMode
            //   00666286  0F 85 54 04 00 00     jne  0x6666E0
            //   0066628F  80 B8 E5 02 00 00 00  cmp  byte [eax+0x2E5],0 ; m_boStoneMode
            //   00666296  0F 85 44 04 00 00     jne  0x6666E0
            // sub_76B354 = m_boDeath | bodyState{0x1D,0x01,0x1A,0x18(仅当实参非0),0x3E}，
            // C# 已有等价实现 IsNativeCanActBlocked。原先只判 m_boDeath 时，处在那 5 个
            // 状态里的怪照常搜敌/移动/出手。
            // m_boGhost 与 m_wStatusTimeArr[POISON_STONE] 两项原生此处没有；保留是因为
            // C# 的状态层与 native bodyState 层尚未收敛（REPLICATION_RULES §4.18），
            // 删掉会让旧石化路径失效。收敛后应只留 IsNativeCanActBlocked(1)。
            if (!IsNativeCanActBlocked(1) && !m_boGhost && !m_boFixedHideMode && !m_boStoneMode && m_wStatusTimeArr[Grobal2.POISON_STONE] == 0)
            {
                if (Think())
                {
                    base.Run();
                    return;
                }
                if (m_boWalkWaitLocked)
                {
                    if ((HUtil32.GetTickCount() - m_dwWalkWaitTick) > m_dwWalkWait)
                    {
                        m_boWalkWaitLocked = false;
                    }
                }
                // 战神 sub_66622C 在"放行走等待"与"走路节拍"之间还有一道闸，顺序是固定的：
                //   006662D6  80 BA D8 04 00 00 00  cmp byte [edx+0x4D8],0   ; m_boWalkWaitLocked
                //   006662F8  C6 82 D8 04 00 00 00  mov byte [edx+0x4D8],0   ; 到时解锁
                //   00666302  80 BA E4 04 00 00 00  cmp byte [edx+0x4E4],0   ; <== 本闸
                //   00666309  0F 85 D1 03 00 00     jne 0x6666E0             ; 置位 -> 直奔 inherited
                //   00666312  80 BA D8 04 00 00 00  cmp byte [edx+0x4D8],0 / jne 0x6666E0
                //   00666324  2B 8A 84 03 00 00     sub ecx,[edx+0x384]      ; tick - m_dwWalkTick
                //   0066632D  3B 8A 24 03 00 00     cmp ecx,[edx+0x324]      ; m_nWalkSpeed
                // 0x4E4 闸排在 0x4D8 闸【之前】，所以它同样先于走路节拍生效。
                if (!m_boNativeStaticMode && !m_boWalkWaitLocked && (HUtil32.GetTickCount() - m_dwWalkTick) > m_nWalkSpeed)                {
                    m_dwWalkTick = HUtil32.GetTickCount();
                    m_nWalkCount++;
                    if (m_nWalkCount > m_nWalkStep)
                    {
                        m_nWalkCount = 0;
                        m_boWalkWaitLocked = true;
                        m_dwWalkWaitTick = HUtil32.GetTickCount();
                    }
                    if (!m_boRunAwayMode)
                    {
                        if (!m_boNoAttackMode)
                        {
                            if (m_TargetCret != null)
                            {
                                if (AttackTarget())
                                {
                                    base.Run();
                                    return;
                                }
                            }
                            else
                            {
                                m_nTargetX = -1;
                                if (m_boMission)
                                {
                                    m_nTargetX = m_nMissionX;
                                    m_nTargetY = m_nMissionY;
                                }
                            }
                        }
                        if (m_Master != null)
                        {
                            short nX = 0;
                            short nY = 0;
                            if (m_TargetCret == null)
                            {
                                m_Master.GetBackPosition(ref nX, ref nY);
                                if (Math.Abs(m_nTargetX - nX) > 1 || Math.Abs(m_nTargetY - nY) > 1)
                                {
                                    m_nTargetX = nX;
                                    m_nTargetY = nY;
                                    if (Math.Abs(m_nCurrX - nX) <= 2 && Math.Abs(m_nCurrY - nY) <= 2)
                                    {
                                        if (m_PEnvir.GetMovingObject(nX, nY, true) != null)
                                        {
                                            m_nTargetX = m_nCurrX;
                                            m_nTargetY = m_nCurrY;
                                        }
                                    }
                                }
                            }
                            if (!m_Master.m_boSlaveRelax && (m_PEnvir != m_Master.m_PEnvir || Math.Abs(m_nCurrX - m_Master.m_nCurrX) > 20 || Math.Abs(m_nCurrY - m_Master.m_nCurrY) > 20))
                            {
                                // MONAI-15 — TMonster.Run 召回传送 sub_66622C @0x6665AF：
                                //   B8 04 / E8 Random / 03 83 30 01 00 00  ; Y = masterY + Random(4) 先抽
                                //   50 / 6A 01 / 6A 00                     ; push Y, 1, 0
                                //   B8 04 / E8 Random / 03 8B 2C 01 00 00  ; X = masterX + Random(4) 后抽
                                //   8B 93 28 01 00 00 / FF 93 C0 01 00 00  ; edx=master.envir, vcall +0x1C0
                                // 旧 C# 传到 GetBackPosition 写下的 m_nTargetX/Y，抽签次数为 0。
                                // push 0 那一档 C# SpaceMove 没有对应形参，标 BLOCKED，nInt 仍传 1。
                                var nRecallY = (short)(m_Master.m_nCurrY + M2Share.RandomNumber.Random(4));
                                var nRecallX = (short)(m_Master.m_nCurrX + M2Share.RandomNumber.Random(4));
                                SpaceMove(m_Master.m_PEnvir, nRecallX, nRecallY, 1);
                            }
                        }
                    }
                    else
                    {
                        if (m_dwRunAwayTime > 0 && (HUtil32.GetTickCount() - m_dwRunAwayStart) > m_dwRunAwayTime)
                        {
                            m_boRunAwayMode = false;
                            m_dwRunAwayTime = 0;
                        }
                    }
                    if (m_Master != null && m_Master.m_boSlaveRelax)
                    {
                        base.Run();
                        return;
                    }
                    if (m_nTargetX != -1)
                    {
                        GotoTargetXY();
                    }
                    else
                    {
                        if (m_TargetCret == null)
                        {
                            Wondering();
                        }
                    }
                }
            }
            base.Run();
        }
    }
}
