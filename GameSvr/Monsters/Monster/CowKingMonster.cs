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
            // logic (30000ms outer + 8000ms phase transitions) that wraps the STANDARD
            // sub_666AE4 aggressive-monster tick (NOT a separate search path). The native
            // does NOT bypass the 8000/1000ms SearchTarget gate — it modulates attack/walk
            // speed and conditionally teleports, THEN calls sub_666AE4.
            //
            // Key divergence fix: the C# previously called base.Run() which is ATMonster.Run()
            // with its OWN 8000/1000ms search gate, causing double search logic. The native
            // structure is:
            //   if (blocked || death) return sub_666AE4(self);
            //   if ((now - self[+1256]) > 0x7530) { // 30000ms gate
            //       self[+1256] = now;
            //       if (target && sub_767EB4() >= 5) { teleport; return sub_666AE4(self); }
            //       // phase logic (bo55C/bo55D state machine with 0x1F40=8000ms transitions)
            //   }
            //   return sub_666AE4(self);  // ALWAYS calls standard aggressive AI
            //
            // The correct C# mapping: the Boss logic modulates m_nNextHitTime and m_nWalkSpeed,
            // then falls through to the standard Monster.Run (NOT ATMonster.Run override) so
            // the base aggressive-monster AI handles target search/attack without the redundant
            // ATMonster 8000/1000ms timer override.

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

            // Native: return sub_666AE4(self). C# equivalent: call Monster.Run() which has the
            // standard aggressive-monster logic (Think/WalkWait/AttackTarget/movement) WITHOUT
            // ATMonster's redundant 8000/1000ms search override. This ensures the Boss uses
            // the dual-timer structure (30000ms outer wrap) but still gets standard target
            // search/attack from the base aggressive-monster tick.
            //
            // IMPORTANT: Do NOT call base.Run() (= ATMonster.Run) because that adds a SECOND
            // 8000/1000ms search gate on top of the 30000ms Boss gate, creating double search
            // logic the native does not have. The native sub_667420 wraps sub_666AE4; the C#
            // CowKingMonster.Run should wrap Monster.Run.
            ((Monster)this).Run();
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
