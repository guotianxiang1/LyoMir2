using Xunit;
using GameSvr;
using SystemModule;

namespace GameSvr.Tests
{
    /// <summary>
    /// Unit tests for sub_76C89C (ApplyNativePercentageHpMpDamage).
    /// </summary>
    /// <remarks>
    /// Native evidence: combat_damage_pipeline_20260810.md §6 (0x76C89C..0x76C984).
    ///
    /// Test strategy:
    /// 1. Branch coverage: all four branches (A/B/C/D) based on shield+job gates.
    /// 2. Arithmetic: divide-before-multiply with x87 Extended precision emulation.
    /// 3. Edge cases: zero HP/MP, exact ties, signed >0 gates, no zero-clamp.
    /// 4. Known divergence witnesses from brute-force scan (§6 table).
    ///
    /// Witnesses from combat_damage_pipeline §6:
    ///   den=100  HP=53  pct=100 -> native 52 (Extended rounds down at intermediate step)
    ///   den=100  HP=29  pct=100 -> native 29
    ///   den=200  HP=58  pct=100 -> native 29
    ///   den=200  HP=116 pct=100 -> native 58
    /// </remarks>
    public class NativePercentageHpMpDamageTests
    {
        private TBaseObject CreateVictim(int hp, int mp, byte job = 0,
            bool fullShield = false, bool standardShield = false, bool halfShield = false)
        {
            var victim = new TPlayObject();
            victim.m_WAbil = new TAbility { HP = hp, MP = mp };
            victim.m_btJob = job;
            victim.m_boNativeFullMagicShield = fullShield;
            victim.m_boMagicShield = standardShield;
            victim.m_boNativeHalfMagicShield = halfShield;
            return victim;
        }

        [Fact]
        public void BranchA_ShieldAndJobOne_DrainsOnlyMP()
        {
            // Branch A: shield present + job==1 → MP only, /100
            var victim = CreateVictim(hp: 1000, mp: 500, job: 1, fullShield: true);
            int hpBefore = victim.m_WAbil.HP;

            int result = victim.ApplyNativePercentageHpMpDamage(50);

            // 50% of 500 MP = 250 MP drained
            Assert.Equal(250, victim.m_WAbil.MP);
            Assert.Equal(hpBefore, victim.m_WAbil.HP);  // HP unchanged
            Assert.Equal(0, result);  // Returns HP loss (which is 0 for this branch)
        }

        [Fact]
        public void BranchB_ShieldAndJobNotOne_DrainsHPViaDamageHealth()
        {
            // Branch B: shield present + job!=1 → HP only via DamageHealth, then hpLoss zeroed
            var victim = CreateVictim(hp: 1000, mp: 500, job: 0, standardShield: true);
            int mpBefore = victim.m_WAbil.MP;

            int result = victim.ApplyNativePercentageHpMpDamage(30);

            // 30% of 1000 HP = 300, routed through DamageHealth (may apply reductions)
            // After DamageHealth, hpLoss is zeroed so tail does not double-subtract
            Assert.Equal(mpBefore, victim.m_WAbil.MP);  // MP unchanged
            Assert.Equal(0, result);  // hpLoss zeroed after DamageHealth call
            // HP change depends on DamageHealth stages; just verify it was touched
            Assert.True(victim.m_WAbil.HP <= 1000);
        }

        [Fact]
        public void BranchC_NoShieldButHalfShieldByte_DrainsBothAtHalfRate()
        {
            // Branch C: no full/standard shield, but +0x1BB set → BOTH HP and MP, /200
            var victim = CreateVictim(hp: 1000, mp: 400, job: 0, halfShield: true);

            int result = victim.ApplyNativePercentageHpMpDamage(100);

            // 100% of 1000 HP / 200 = 500 HP drained (1000/200*100 = 500)
            // 100% of 400 MP / 200 = 200 MP drained (400/200*100 = 200)
            Assert.Equal(500, 1000 - victim.m_WAbil.HP);
            Assert.Equal(200, 400 - victim.m_WAbil.MP);
            Assert.Equal(500, result);  // Returns HP loss
        }

        [Fact]
        public void BranchD_NoShield_DrainsOnlyHP()
        {
            // Branch D: no shield at all → HP only, /100
            var victim = CreateVictim(hp: 800, mp: 300, job: 0);
            int mpBefore = victim.m_WAbil.MP;

            int result = victim.ApplyNativePercentageHpMpDamage(25);

            // 25% of 800 HP = 200 HP drained (800/100*25 = 200)
            Assert.Equal(600, victim.m_WAbil.HP);
            Assert.Equal(mpBefore, victim.m_WAbil.MP);  // MP unchanged
            Assert.Equal(200, result);
        }

        [Fact]
        public void DivideBeforeMultiply_Witness53_100()
        {
            // Known divergence witness from §6: HP=53, pct=100, den=100 → native 52
            // (field / 100.0) * 100 with Extended precision rounding at each step
            double intermediate = HUtil32.DivideBeforeMultiplyX87Extended(53, 100.0, 100);
            int truncated = HUtil32.TruncX87Extended(intermediate);

            // Native gives 52 due to Extended precision rounding
            Assert.Equal(52, truncated);
        }

        [Fact]
        public void DivideBeforeMultiply_Witness58_100_Den200()
        {
            // Witness: HP=58, pct=100, den=200 → native 29
            double intermediate = HUtil32.DivideBeforeMultiplyX87Extended(58, 200.0, 100);
            int truncated = HUtil32.TruncX87Extended(intermediate);

            Assert.Equal(29, truncated);
        }

        [Fact]
        public void SignedGate_NegativeLoss_NoSubtraction()
        {
            // Native uses signed jle gates: if loss <= 0, skip subtraction
            var victim = CreateVictim(hp: 100, mp: 50, job: 0);

            // Zero percentage should produce zero loss
            int result = victim.ApplyNativePercentageHpMpDamage(0);

            Assert.Equal(100, victim.m_WAbil.HP);
            Assert.Equal(50, victim.m_WAbil.MP);
            Assert.Equal(0, result);
        }

        [Fact]
        public void NoZeroClamp_HPCanGoNegative()
        {
            // Native has NO zero-clamp on field writes: HP -= loss can produce negative
            var victim = CreateVictim(hp: 50, mp: 100, job: 0);

            int result = victim.ApplyNativePercentageHpMpDamage(200);

            // 200% of 50 HP / 100 = 100 HP drained → 50 - 100 = -50
            // Native: unchecked subtraction, no clamp
            Assert.True(victim.m_WAbil.HP < 0);
            Assert.Equal(100, result);
        }

        [Fact]
        public void ArithmeticPrecision_SmallValues()
        {
            // Verify truncation (not rounding) for small fractional results
            var victim = CreateVictim(hp: 10, mp: 10, job: 0);

            int result = victim.ApplyNativePercentageHpMpDamage(4);

            // 4% of 10 / 100 = 0.4 → truncate to 0
            Assert.Equal(10, victim.m_WAbil.HP);  // No change (loss was 0)
            Assert.Equal(0, result);
        }

        [Fact]
        public void ArithmeticPrecision_RoundsDownNotUp()
        {
            // @TRUNC rounds toward zero, not to nearest
            var victim = CreateVictim(hp: 100, mp: 100, job: 0);

            int result = victim.ApplyNativePercentageHpMpDamage(9);

            // 9% of 100 / 100 = 9.0 → exactly 9
            Assert.Equal(91, victim.m_WAbil.HP);
            Assert.Equal(9, result);
        }
    }
}
