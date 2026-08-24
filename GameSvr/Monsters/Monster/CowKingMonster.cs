using SystemModule;

namespace GameSvr
{
    public class CowKingMonster : AtMonster
    {
        private int dw558 = 0;
        private bool bo55C = false;
        private bool bo55D = false;
        private int n560 = 0;
        private int dw564 = 0;
        private int dw568 = 0;
        private int dw56C = 0;
        private int dw570 = 0;

        public CowKingMonster() : base()
        {
            m_dwSearchTime = M2Share.RandomNumber.Random(1500) + 500;
            dw558 = HUtil32.GetTickCount();
            bo2BF = true;
            n560 = 0;
            bo55C = false;
            bo55D = false;
        }

        public override void Attack(TBaseObject TargeTBaseObject, byte nDir)
        {
            var WAbil = m_WAbil;
            var nPower = GetAttackPower(HUtil32.LoWord(WAbil.DC), HUtil32.HiWord(WAbil.DC) - HUtil32.LoWord(WAbil.DC));
            HitMagAttackTarget(TargeTBaseObject, nPower / 2, nPower / 2, true);
        }

        public override void Initialize()
        {
            dw56C = m_nNextHitTime;
            dw570 = m_nWalkSpeed;
            base.Initialize();
        }

        public override void Run()
        {
            // Native TCowKingMonster.Run = sub_667420 @0x00667420: Boss-specific dual-timer
            // logic (30000ms outer + 8000ms phase transitions) that wraps
            // TATMonster.Run sub_666AE4. The native does not bypass that method's
            // 8000/1000ms SearchTarget gate: it modulates attack/walk speed and
            // conditionally teleports, then calls sub_666AE4.
            //
            // Native structure:
            //   if (blocked || death) return sub_666AE4(self);
            //   if ((now - self[+1256]) > 0x7530) { // 30000ms gate
            //       self[+1256] = now;
            //       if (target && sub_767EB4() >= 5) { teleport; return sub_666AE4(self); }
            //       // phase logic (bo55C/bo55D state machine with 0x1F40=8000ms transitions)
            //   }
            //   return sub_666AE4(self);  // ALWAYS calls standard aggressive AI
            //
            // CowKingMonster derives from AtMonster, so base.Run() is the exact C# mapping
            // for the native direct call to sub_666AE4.

            short n8 = 0;
            short nC = 0;
            int n10;

            // Native outer 30000ms timer gate (a1+1256 = dw558)
            if (!m_boDeath && !bo554 && !m_boGhost && (HUtil32.GetTickCount() - dw558) > 30000)
            {
                dw558 = HUtil32.GetTickCount();

                // Teleport branch: if target exists and surrounded >= 5 tiles
                if (m_TargetCret != null && sub_4C3538() >= 5)
                {
                    m_TargetCret.GetBackPosition(ref n8, ref nC);
                    if (m_PEnvir.CanWalk(n8, nC, false))
                    {
                        SpaceMove(m_PEnvir.sMapName, n8, nC, 0);
                        return;
                    }
                    MapRandomMove(m_PEnvir.sMapName, 0);
                    return;
                }

                // Phase state machine: 7 HP bands, triggers bo55C when band >= 2 changes
                // Native: if (!*(_BYTE *)(a1 + 1260) && *(int *)(a1 + 1264) >= 2 && v4 != *(int *)(a1 + 1264))
                n10 = n560;
                n560 = 7 - m_WAbil.HP / (m_WAbil.MaxHP / 7);
                if (!bo55C && n560 >= 2 && n560 != n10)
                {
                    bo55C = true;
                    dw564 = HUtil32.GetTickCount();
                }

                // Phase 1 (bo55C): 8000ms duration, m_nNextHitTime = 10000 (slow attack)
                if (bo55C)
                {
                    if ((HUtil32.GetTickCount() - dw564) < 8000)
                    {
                        m_nNextHitTime = 10000;
                    }
                    else
                    {
                        bo55C = false;
                        bo55D = true;
                        dw568 = HUtil32.GetTickCount();
                    }
                }

                // Phase 2 (bo55D): 8000ms duration, m_nNextHitTime = 500, m_nWalkSpeed = 400 (fast)
                if (bo55D)
                {
                    if ((HUtil32.GetTickCount() - dw568) < 8000)
                    {
                        m_nNextHitTime = 500;
                        m_nWalkSpeed = 400;
                    }
                    else
                    {
                        bo55D = false;
                        m_nNextHitTime = dw56C;
                        m_nWalkSpeed = dw570;
                    }
                }
            }

            // Native 0x006675D6: call sub_666AE4 (TATMonster.Run).
            base.Run();
        }

        public int sub_4C3538()
        {
            int result = 0;
            int nC = -1;
            int n10;
            while (nC != 2)
            {
                n10 = -1;
                while (n10 != 2)
                {
                    if (!m_PEnvir.CanWalk(m_nCurrX + nC, m_nCurrY + n10, false))
                    {
                        if ((nC != 0) || (n10 != 0))
                        {
                            result++;
                        }
                    }
                    n10++;
                }
                nC++;
            }
            return result;
        }
    }
}
