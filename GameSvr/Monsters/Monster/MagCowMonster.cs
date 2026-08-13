using SystemModule;

namespace GameSvr
{
    public class MagCowMonster : AtMonster
    {
        public MagCowMonster() : base()
        {
            m_dwSearchTime = M2Share.RandomNumber.Random(1500) + 1500;
        }

        private void sub_4A9F6C(byte btDir)
        {
            TBaseObject BaseObject;
            m_btDirection = btDir;
            var WAbil = m_WAbil;
            var n10 = M2Share.RandomNumber.Random(HUtil32.HiWord(WAbil.DC) - HUtil32.LoWord(WAbil.DC) + 1) + HUtil32.LoWord(WAbil.DC);
            if (n10 > 0)
            {
                SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
                BaseObject = GetPoseCreate();
                // MONAI-17 — TMagCowMonster.Attack sub_667284 @0x6672FE 的命中门是
                // sub_7744B4，不是 `m_nAntiMagic >= 0`（Recalc 把抗魔写成 1，那道闸恒真）：
                //   006672FE  8B D3              mov  edx,ebx          ; source = self
                //   00667300  8B C6              mov  eax,esi          ; target = pose
                //   00667302  E8 AD D1 10 00     call 0x7744B4
                //   00667307  84 C0 / 74 43      test al / je miss
                // 007744BC  80 B9 78 01 00 00 0B  cmp [target+0x178],0x0B ; RC_GUARD
                // 007744D3  0F B7 83 72 02 00 00  movzx eax,[source+0x272]
                // 007744DA  83 C0 0A / 6B C0 64   +10, *100
                // 007744E0  0F B7 91 70 02 00 00  movzx edx,[target+0x270] ; anti-magic
                // 00774511  B8 64 00 00 00 / E8   Random(100)
                // 00774516  3B D8 / 0F 9F C0      setg = chance > Random(100)
                // 恒真闸会让魔牛对卫士/高抗魔目标也出手，并且少抽一次 Random(100)。
                if (BaseObject != null && IsProperTarget(BaseObject) && NativeMagicHitApplies(BaseObject))
                {
                    n10 = BaseObject.GetMagStruckDamage(this, n10);
                    if (n10 > 0)
                    {
                        BaseObject.StruckDamage(n10, this);
                        BaseObject.SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101, (short)n10, BaseObject.m_WAbil.HP, BaseObject.m_WAbil.MaxHP, ObjectId, "", 300);
                    }
                }
            }
        }

        protected override bool AttackTarget()
        {
            var result = false;
            byte btDir = 0;
            if (m_TargetCret == null)
            {
                return result;
            }
            if (GetAttackDir(m_TargetCret, ref btDir))
            {
                if (HUtil32.GetTickCount() - m_dwHitTick > m_nNextHitTime)
                {
                    m_dwHitTick = HUtil32.GetTickCount();
                    m_dwTargetFocusTick = HUtil32.GetTickCount();
                    sub_4A9F6C(btDir);
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
            return result;
        }
    }
}

