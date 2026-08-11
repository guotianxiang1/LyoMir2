using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// Native sub_76C89C — percentage HP/MP damage with x87 extended precision.
        /// </summary>
        /// <remarks>
        /// Signature: f(Self:EAX, pct:ECX) returns HP loss.
        ///
        /// Native証據 (0x76C89C..0x76C984, combat_damage_pipeline_20260810.md §6):
        /// - Four mutually exclusive branches gate on shield bytes +0x1BA/+0x1D3/+0x1BB and job +0x72.
        /// - All five @TRUNC sites use divide-BEFORE-multiply: `(field / den) * pct`.
        /// - Operand order is native bug class (memory delphi-int-div-bug-class) but MUST be preserved.
        /// - Rounding: @TRUNC 0x403580 (toward zero), never @ROUND.
        /// - Double-rounding exposure: x87 Extended (64-bit mantissa) at each of fdiv and fmul,
        ///   then fistp → diverges from C# double in ~0.13-0.25% of inputs (combat_damage_pipeline §6).
        /// - Direct field writes bypass all DamageHealth stages (no zero-clamp, no shields, no state-63).
        /// - Returns HP loss (esi); tail sends client msg 0x2724/0x2775 with 200ms delay via sub_76B4F8.
        /// - Branch B calls DamageHealth (+0x1B0) then zeros esi so tail does not double-subtract.
        ///
        /// Fields (all PROVEN, with EA):
        ///   +0x72  = m_btJob (byte, exact ==1 gates branch A)
        ///   +0x1BA = m_boMagicShield (1.5-factor shield, byte)
        ///   +0x1D3 = m_boNativeFullMagicShield (1:1 shield, byte)
        ///   +0x1BB = m_boNativeHalfMagicShield (50% shield, byte)
        ///   +0x2AC = HP (dword)
        ///   +0x2B4 = MP (dword)
        ///
        /// Constants verified:
        ///   0x76C988 = f32 100.0 (00 00 C8 42)
        ///   0x76C98C = f32 200.0 (00 00 48 43)
        ///
        /// Emulation: HUtil32.TruncX87Extended emulates 64-bit mantissa rounding at EACH of the two FP ops,
        /// then truncates. Verified against brute-force rational arithmetic (combat_damage_pipeline §6).
        /// </remarks>
        internal int ApplyNativePercentageHpMpDamage(int percentage)
        {
            int hpLoss = 0;
            int mpLoss = 0;

            // Branch discriminator: shield presence (A/B vs C/D)
            bool hasShield = m_boMagicShield || m_boNativeFullMagicShield;

            if (hasShield)
            {
                // Branch A: shield + job==1 → MP only, /100
                if (m_btJob == 1)
                {
                    mpLoss = HUtil32.TruncX87Extended(
                        HUtil32.DivideBeforeMultiplyX87Extended(m_WAbil.MP, 100.0, percentage));
                }
                else
                {
                    // Branch B: shield + job!=1 → HP only, /100, routes through DamageHealth
                    hpLoss = HUtil32.TruncX87Extended(
                        HUtil32.DivideBeforeMultiplyX87Extended(m_WAbil.HP, 100.0, percentage));

                    // Native calls DamageHealth (VMT +0x1B0) then zeros esi.
                    // This applies all five DamageHealth reduction stages.
                    DamageHealth(hpLoss);
                    hpLoss = 0;  // Zeroed so tail does not double-subtract
                }
            }
            else
            {
                // Branch C: no shield, byte +0x1BB set → BOTH HP and MP, /200
                if (m_boNativeHalfMagicShield)
                {
                    hpLoss = HUtil32.TruncX87Extended(
                        HUtil32.DivideBeforeMultiplyX87Extended(m_WAbil.HP, 200.0, percentage));
                    mpLoss = HUtil32.TruncX87Extended(
                        HUtil32.DivideBeforeMultiplyX87Extended(m_WAbil.MP, 200.0, percentage));
                }
                else
                {
                    // Branch D: no shield at all → HP only, /100
                    hpLoss = HUtil32.TruncX87Extended(
                        HUtil32.DivideBeforeMultiplyX87Extended(m_WAbil.HP, 100.0, percentage));
                }
            }

            // Common tail: direct field writes, signed >0 gates, NO zero-clamp
            if (hpLoss > 0)
            {
                m_WAbil.HP = unchecked(m_WAbil.HP - hpLoss);
            }

            if (mpLoss > 0)
            {
                m_WAbil.MP = unchecked(m_WAbil.MP - mpLoss);
            }

            // Native sends client msg 0x2724 (RM_STRUCK=10020) or 0x2775 via sub_76B4F8 with 200ms delay.
            // The consumer (monster AI Operate @0x71DEE8) reads msg.wIdent == 0x2724.
            // Client rendering: deferred struck visual with 200ms delay.
            // C# equivalent: SendDelayMsg with RM_STRUCK and the delay.
            if (hpLoss > 0 || mpLoss > 0)
            {
                SendDelayMsg(this, Grobal2.RM_STRUCK, 0, 0, 0, 0, string.Empty, 200);
            }

            m_boNativeHealthSpellDirty = true;
            return hpLoss;
        }
    }
}
