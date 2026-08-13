using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic ids 66 and 67 — the released half of the 65..68 charged-counter
    /// family. Both share ONE handler: TABLE2 slots 0x6ED839 and 0x6ED83D
    /// (table base id 39, `add eax,-0x27` @0x6ED7BA) both hold 0x6EDE39.
    ///   006EDE40  e8 ff 78 05 00  call 0x745744
    ///   006EDE45  34 01           xor al,1
    ///   006EDE47  88 45 fa        mov [ebp-6],al
    /// so a false return is a hard reject. THeroAct reaches the same
    /// sub_745744 from its own dispatcher at 0x68E63B.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>0x7443E8 `mov ecx,0xAFC8`. Keyed on the CACHED magic's id,
        /// so 65/66/67/68 share one 45-second lock.</summary>
        private const int NativeChargedCounterCooldownMilliseconds = 0xAFC8;

        /// <summary>0x744677 `cmp eax,0xBB8` with `jae`, so exactly 3000 ms
        /// already counts as outside the window.</summary>
        private const int NativeChargedCounterComboWindowMs = 0xBB8;

        /// <summary>dword[5] at 0x7D3EC4, raw
        /// `64000000 50000000 28000000 14000000 0a000000`, indexed by the hit
        /// counter. 0x7D3ED4 holds the 10 used from the sixth hit on.</summary>
        private static readonly int[] NativeChargedCounterComboDecay =
            { 100, 80, 40, 20, 10 };
        private const int NativeChargedCounterComboDecayTail = 10;

        /// <summary>self+0xC4: the UserMagic of the last 65..68 skill seen
        /// while walking the magic list (0x76B170 `mov [ebx+0xC4],eax`, the
        /// only write to that field anywhere in the TCreature hierarchy). The
        /// jump into that arm is `add eax,-7` / `sub eax,4` / `jb 0x76B16D`
        /// at 0x76AF2B, i.e. unsigned (id - 65) &lt; 4.
        ///
        /// Native never clears it, and the handler below keys off the CACHED
        /// entry's id rather than the id being cast — so a player who knows
        /// only 67 but casts 66 gets 67's stat pair and 67's cooldown key.
        /// That is native behaviour, not an oversight to tidy up.</summary>
        internal TUserMagic m_NativeChargedCounterMagic;

        /// <summary>target+0x391, the consecutive-hit counter.</summary>
        private byte m_btNativeChargedCounterComboCount;

        /// <summary>target+0x394, the tick of the previous hit.</summary>
        private int m_dwNativeChargedCounterComboTick;

        /// <summary>
        /// sub_745744. `inc ebx / jne` at 0x7457C0 tests the POWER, not the
        /// queue call: ebx is callee-saved and is never reloaded after
        /// sub_76FE44, and the `jle 0x7457C0` at 0x745767 jumps straight onto
        /// that instruction. The only false return is therefore power == -1.
        /// </summary>
        internal bool TryActivateNativeSkill66Or67(TUserMagic userMagic,
            TBaseObject target)
        {
            return TryActivateNativeSkill66Or67(userMagic, target,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill66Or67(TUserMagic userMagic,
            TBaseObject target, int now)
        {
            int power = ResolveNativeChargedCounterPower(target, now);
            if (power > 0)
            {
                // 0x74577D `call [edi+0x3C]`, the same training slot the magic
                // producers use. Only TPlayObject carries the C# port; DoSpell
                // is only ever entered with a player as Self.
                (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                    M2Share.RandomNumber.Random(3) + 1);
                // 0x7457BB call sub_76FE44 with, in declaration order,
                // (UserMagic, MagicId, Target.CurrX, Target.CurrY, 600, 2, 1,
                // byte[0x7457D4] = 0x00, 1) plus eax=Self, edx=Target,
                // ecx=power.
                QueueNativeMagicEffect(1, target, power,
                    userMagic.MagicInfo.wMagicID, target.m_nCurrX,
                    target.m_nCurrY, 2, true, 0,
                    MagicDamageContext.Capture(userMagic), 600);
            }
            return power != -1;
        }

        /// <summary>
        /// THumanKind VMT+0x4C = sub_744388 (TPlayer VMT 0x6AC8C8 +0x4C; the
        /// base TCreature slot 0x76C898 is `or eax,-1 / ret`, a flat -1 for
        /// anything that does not override it).
        ///
        /// Returns -1 to reject the cast, 0 when the cast succeeds but lands
        /// on nothing, else the damage to queue. Both the 45-second lock and
        /// the 10% self-HP cost are paid before the target is even looked at
        /// (0x7443F1 and 0x7444D5 both precede the `test esi,esi` at
        /// 0x7444DB).
        /// </summary>
        private int ResolveNativeChargedCounterPower(TBaseObject target,
            int now)
        {
            // 0x74439C `C6 83 E8 00 00 00 00`. Id 65 sets the same byte at
            // 0x6BCA22 before sending 0xB3B. No reader has been found.
            m_btNativeChargedIndicator = 0;
            TUserMagic cached = m_NativeChargedCounterMagic;
            if (cached?.MagicInfo == null)
            {
                return -1;
            }

            int key = cached.MagicInfo.wMagicID;
            if (GetNativeColdTimeRemaining(key) != 0)
            {
                return -1;
            }
            SetNativeColdTime(key,
                NativeChargedCounterCooldownMilliseconds, now);

            // The working ability record starts at self+0x278: Level at +0x00,
            // AC at +0x04/+0x08, MAC at +0x0C/+0x10, then the four attack
            // pairs at +0x14..+0x30 and HP/MP at +0x34/+0x3C, which is what
            // puts DC low at 0x28C and MP at 0x2B4.
            int low;
            int high;
            switch (key)
            {
                case 65: // 0x744424 [ebx+0x290] / [ebx+0x28C]
                    low = HUtil32.LoWord(m_WAbil.DC);
                    high = HUtil32.HiWord(m_WAbil.DC);
                    break;
                case SpellsDef.SKILL_66: // 0x74444D [ebx+0x298] / [ebx+0x294]
                    low = HUtil32.LoWord(m_WAbil.MC);
                    high = HUtil32.HiWord(m_WAbil.MC);
                    break;
                case SpellsDef.SKILL_67: // 0x744476 [ebx+0x2A0] / [ebx+0x29C]
                    low = HUtil32.LoWord(m_WAbil.SC);
                    high = HUtil32.HiWord(m_WAbil.SC);
                    break;
                case 68: // 0x74449F [ebx+0x2A8] / [ebx+0x2A4]
                    low = m_NativeCoreWorkingAbility.CCLow;
                    high = m_NativeCoreWorkingAbility.CCHigh;
                    break;
                default:
                    // 0x74441B: a stale cache pointing at any other id falls
                    // out of the case with Result still -1 — but only AFTER
                    // the cooldown has been armed.
                    return -1;
            }

            int damage = GetAttackPower(low, high - low);

            // 0x7444C7 `mov ecx,0xA / cdq / idiv ecx / sub [ebx+0x2AC],eax`.
            m_WAbil.HP = unchecked(m_WAbil.HP - m_WAbil.HP / 10);

            // 0x7444E3 reads byte [esi+0x73], which is the GHOST flag (the
            // death flag is the next byte, +0x74).
            if (target == null || target.m_boGhost || !IsProperTarget(target))
            {
                return 0;
            }

            bool targetIsHuman =
                TPlayObject.IsNativeMagicProducerHumanKind(target);

            // 0x74450E `lea eax,[edi+0x3E8]` / call Random is drawn
            // UNCONDITIONALLY, ahead of the isHuman term: 0x744519
            // `cmp edi,eax` / 0x74451B `setg al` / 0x74451E
            // `and al,[ebp-0xD]`. Keeping the draw in the left operand
            // reproduces that, since && would otherwise short-circuit it away
            // for monster targets. 0x74452C then calls
            // sub_76C89C(eax = target, edx = self, ecx = 5).
            if (high > M2Share.RandomNumber.Random(high + 1000) &&
                targetIsHuman)
            {
                target.ApplyNativeChargedCounterDrain(5);
            }

            int selfLevelTerm = Math.Min(150, (int)m_WAbil.Level);
            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(cached);
            int scaled = unchecked(selfLevelTerm +
                (effectiveLevel + 1) * damage);

            // 0x744531 `cmp word [esi+0x278],0x96` / `jae` takes the unscaled
            // path, and so does a non-human target (0x74453C `jne`).
            if (target.m_WAbil.Level < 150 && targetIsHuman)
            {
                // 0x744599: the one-operand `imul edx` writes a 64-bit result
                // that the `cdq` at 0x7445A9 immediately discards, so the
                // multiply is effectively 32-bit truncating.
                damage = unchecked(scaled * target.m_WAbil.Level) / 150;
            }
            else
            {
                damage = scaled;
            }

            // 0x7445AF Random(20000) against targetLevel + 1000; `jge` skips.
            if (M2Share.RandomNumber.Random(20000) <
                target.m_WAbil.Level + 1000)
            {
                SendNativeHumanMagicEffect(26);
                // 0x7445F7 fild effLevel / fmul float32 [0x7446E8] = 0.25f
                // (raw 0000803e) / fadd float32 [0x7446EC] = 1.25f (raw
                // 0000a03f) / fild damage / fmulp / call 0x403574 = @ROUND
                // (`fistp qword` under the ambient half-to-even mode; the
                // chop-mode helper is the OTHER one, 0x403580). Every
                // intermediate is a quarter-integer, so double reproduces the
                // x87 result exactly here.
                damage = HUtil32.Round(
                    (effectiveLevel * 0.25d + 1.25d) * damage);
            }

            if (targetIsHuman)
            {
                switch (target.m_btJob)
                {
                    case 1:
                        damage = target.HasNativeActiveState(0x14)
                            ? unchecked(damage +
                                TruncateNativeChargedCounterThirtyPercent(
                                    damage))
                            : unchecked(damage -
                                TruncateNativeChargedCounterThirtyPercent(
                                    damage));
                        break;
                    case 2:
                        damage = unchecked(damage -
                            TruncateNativeChargedCounterThirtyPercent(damage));
                        break;
                }
            }

            if (unchecked((uint)(now -
                    target.m_dwNativeChargedCounterComboTick)) <
                NativeChargedCounterComboWindowMs)
            {
                byte hits = target.m_btNativeChargedCounterComboCount;
                int decay = hits < NativeChargedCounterComboDecay.Length
                    ? NativeChargedCounterComboDecay[hits]
                    : NativeChargedCounterComboDecayTail;
                // Same truncating-multiply shape as the level scale: the
                // `imul [ebp-8]` at 0x744694 is followed by `cdq` at
                // 0x74469C.
                damage = unchecked(decay * damage) / 100;
                target.m_btNativeChargedCounterComboCount =
                    unchecked((byte)(hits + 1));
            }
            else
            {
                target.m_btNativeChargedCounterComboCount = 0;
            }
            target.m_dwNativeChargedCounterComboTick = now;

            // 0x7446D8 call sub_7693E8 — the +0x99 dirty bit, not an
            // immediate packet.
            m_boNativeHealthSpellDirty = true;
            return damage;
        }

        /// <summary>
        /// The three job-modifier sites (0x74462D, 0x744642, 0x74465B) all do
        /// `fild damage / fld tbyte [0x7446F0] / fmulp / call 0x403580`, and
        /// 0x403580 forces RC = chop before its `fistp`, so this truncates.
        ///
        /// [0x7446F0] holds the ten bytes `9a 99 99 99 99 99 99 99 fd 3f`, an
        /// x87 extended double with a 64-bit mantissa — NOT the same number as
        /// C#'s 0.3d. Writing `(int)(damage * 0.3d)` instead diverges on EVERY
        /// multiple of ten (measured across 0..200000: 20000 mismatches, e.g.
        /// 10 gives 3 in double and 2 on the FPU), because the double product
        /// rounds up onto the exact integer while the wider FPU product stays
        /// just below it. So the constant is used exactly: it is
        /// 0x9999999999999999 * 2^-65, and the truncated product with an int
        /// is the high half of the 128-bit product shifted down one.
        /// </summary>
        private static int TruncateNativeChargedCounterThirtyPercent(
            int damage)
        {
            const ulong mantissa = 0x9999999999999999UL;
            ulong magnitude = damage < 0
                ? unchecked((ulong)(-(long)damage))
                : (ulong)damage;
            ulong product = Math.BigMul(magnitude, mantissa, out _);
            int truncated = unchecked((int)(product >> 1));
            return damage < 0 ? -truncated : truncated;
        }

        /// <summary>
        /// sub_76C89C, the percentage drain the charged counter lands on a
        /// human target. Called as (eax = target, edx = the caster,
        /// ecx = 5); edx is stored nowhere and never read, so the caster does
        /// not participate.
        /// <para>
        /// Every quotient is <c>fild stat / fdiv const / fild amount / fmulp
        /// / call sub_403580</c>, i.e. the divide happens BEFORE the multiply
        /// and the result is truncated (0x403580 forces RC = chop; the
        /// rounding helper is the other one, 0x403574). The two constants are
        /// float32: [0x76C988] = 100.0, [0x76C98C] = 200.0.
        /// </para>
        /// <para>
        /// The shield bytes are the same three sub_767D14 tests, and their
        /// order there names them: +0x1D3 first @0x767D6B (the plain
        /// full-absorb, m_boNativeFullMagicShield), +0x1BA @0x767D94 (the
        /// 1.5x one, m_boMagicShield), +0x1BB @0x767DE3 (the half,
        /// m_boNativeHalfMagicShield).
        /// </para>
        /// </summary>
        private int ApplyNativeChargedCounterDrain(int amount)
        {
            int hpDrain = 0;
            int mpDrain = 0;
            // 0x76C8AC `80 BB BA 01 00 00 00` / 0x76C8B5 `80 BB D3 01 00 00 00`.
            if (m_boMagicShield || m_boNativeFullMagicShield)
            {
                // 0x76C8BE `80 7B 72 01` — job 1 gives up mana instead.
                if (m_btJob == 1)
                {
                    mpDrain = TruncNativeChargedCounterDrain(m_WAbil.MP,
                        100.0d, amount);
                }
                else
                {
                    hpDrain = TruncNativeChargedCounterDrain(m_WAbil.HP,
                        100.0d, amount);
                    // 0x76C8FC VMT+0x1B0 = sub_767D14 = DamageHealth, then
                    // 0x76C902 `xor esi,esi` discards the local, so the HP
                    // leaves through the absorb path and is NOT subtracted
                    // again below.
                    DamageHealth(hpDrain);
                    hpDrain = 0;
                }
            }
            else if (m_boNativeHalfMagicShield)
            {
                // 0x76C90F and 0x76C927 — both halves divide by 200.
                hpDrain = TruncNativeChargedCounterDrain(m_WAbil.HP,
                    200.0d, amount);
                mpDrain = TruncNativeChargedCounterDrain(m_WAbil.MP,
                    200.0d, amount);
            }
            else
            {
                hpDrain = TruncNativeChargedCounterDrain(m_WAbil.HP,
                    100.0d, amount);
            }

            // 0x76C959 / 0x76C963: each subtraction is guarded only on its own
            // value being positive, and neither is floored at zero.
            if (hpDrain > 0)
            {
                m_WAbil.HP = unchecked(m_WAbil.HP - hpDrain);
            }
            if (mpDrain > 0)
            {
                m_WAbil.MP = unchecked(m_WAbil.MP - mpDrain);
            }

            // 0x76C96D `push 0xC8` / sub_76B4F8(eax = target, edx = target,
            // ecx = hpDrain, 200). nParam3 is the TARGET, not the caster, and
            // the message is unconditional — hpDrain is zero on the
            // shield-absorb arm and it still goes out.
            SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101,
                unchecked((short)hpDrain), hpDrain, 0, ObjectId,
                string.Empty, 200);
            return hpDrain;
        }

        private static int TruncNativeChargedCounterDrain(int stat,
            double divisor, int amount)
        {
            return unchecked((int)Math.Truncate(stat / divisor * amount));
        }
    }
}
