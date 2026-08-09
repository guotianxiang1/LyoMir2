using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// 战神 <c>sub_7436F8</c> — the revive handler, VMT slot <c>+0x08</c>, not
        /// overridden by any of the ten classes that expose it. Called from the tick when
        /// HP has reached 0, before <c>Die()</c>.
        ///
        /// The gate ORDER, the cooldown, the invulnerability window and the map gates are
        /// all decided by <see cref="NativeRevivePolicy"/>; this method performs the state
        /// mutations of whichever leaf the native ladder selects.
        ///
        /// Returns the native <c>bl</c>: TRUE if the creature is alive again.
        /// </summary>
        private bool TryNativeRevive()
        {
            var tick = HUtil32.GetTickCount();

            // C#-only guard, retained from the previous stub: the killer can suppress a
            // revive.  This has no counterpart inside sub_7436F8 itself — natively the
            // suppression lives in the caller — so it is kept OUTSIDE the native ladder
            // rather than folded into it.
            var suppressed = m_LastHiter != null && m_LastHiter.m_boUnRevival;

            // 0x7437DC / 0x74382E — sub_772960(self, 0x30): is the second path's cooldown
            // state live?  HasNativeActiveState is the byte-faithful port of sub_772960
            // (bit test, bound 111 == native `cmp dl,0x6F`).
            var secondPathCooldownActive =
                HasNativeActiveState(NativeRevivePolicy.SecondPathCooldownStateType);

            // BLOCKED: [self+0x1D1] and [self+0x1DD] are the second path's enable flag and
            // its 1..4 CD tier.  Both live inside the 54-byte block [self+0x1B0..+0x1E5]
            // that sub_73D500 rebuilds wholesale from the equipment aggregate
            // ([self+0x4C0]+0x1F8) — FillChar @0x73D542 then rep movsd+movsw
            // @0x73D63D-0x73D650.  C# does not model that aggregate, so the tier cannot be
            // derived faithfully and the path is left fail-closed.  Inventing a tier would
            // pick between a 60 s and a 300 s cooldown with no evidence.
            const bool NativeSecondPathFlag = false;   // [self+0x1D1] — unmodelled
            const byte NativeSecondPathTier = 0;       // [self+0x1DD] — unmodelled

            var outcome = NativeRevivePolicy.Resolve(
                m_PEnvir?.Flag,
                hasEquipRevive: m_boRevival && !suppressed,
                lastEquipReviveTick: m_dwRevivalTick,
                tick: tick,
                secondPathFlag: NativeSecondPathFlag,
                secondPathTier: NativeSecondPathTier,
                secondPathCooldownActive: secondPathCooldownActive);

            switch (outcome)
            {
                case NativeRevivePolicy.Outcome.EquipRevive:
                    // 0x743761 stamp, 0x743775 HP = MaxHP, 0x743781 conditional MP.
                    m_dwRevivalTick = tick;
                    ItemDamageRevivalRing();
                    m_WAbil.HP = m_WAbil.MaxHP;
                    // 0x743781 `cmp byte [esi+0x1B9],0` / je — the "also refill MP" flag is
                    // a SECOND equipment bit, distinct from [+0x1B8].  It is sourced from
                    // the same unmodelled equipment block, so it stays false and MP is not
                    // restored on this path.  (The second path restores MP
                    // unconditionally, but that path is itself blocked.)
                    HealthSpellChanged();
                    SysMsg(M2Share.g_sRevivalRecoverMsg, MsgColor.Green, MsgType.Hint);
                    break;

                case NativeRevivePolicy.Outcome.SecondPathRevive:
                    // 0x743834 arms state 48 for GetCooldownSecondsForTier(tier) seconds,
                    // then 0x74383A/0x743846 restore HP and MP unconditionally.
                    // Unreachable while the tier is unmodelled; kept so the ladder is
                    // complete and the audit can exercise it.
                    TryApplyNativeReviveCooldown(NativeSecondPathTier);
                    m_WAbil.HP = m_WAbil.MaxHP;
                    m_WAbil.MP = m_WAbil.MaxMP;
                    HealthSpellChanged();
                    break;

                case NativeRevivePolicy.Outcome.SecondPathOnCooldown:
                    // 0x743893-0x743906 — the player is told how long is left and is NOT
                    // revived.  The native text is built from a TDateTime global and
                    // formatted with 'YYYY-MM-DD HH:NN:SS'; that message is not reproduced
                    // here because the global's identity is unresolved.  Behaviourally the
                    // leaf is "no revive", which is what matters.
                    break;
            }

            var revived = NativeRevivePolicy.GrantsPostReviveWindow(outcome);

            if (revived)
            {
                // 0x74390B `test bl,bl` / 0x74390F-0x74391B — state 55 for 2 SECONDS,
                // value 1.  ecx is the duration (sub_76B478 @0x76B48C multiplies it by
                // 1000); the pushed 1 is the state's value.
                AddNativeTimedAbilitySeconds(
                    NativeRevivePolicy.PostReviveStateType,
                    NativeRevivePolicy.PostReviveStateValue,
                    NativeRevivePolicy.PostReviveStateSeconds);
            }

            var flag = m_PEnvir?.Flag;

            // 0x743921 `test bl,bl` / jne — AUTORELIVE runs ONLY when nothing has revived
            // yet, and 0x743945 `setg bl` then makes the result depend on HP.  A revive
            // produced here therefore gets NO state-55 window; that asymmetry is native.
            if (!revived && flag != null && flag.boAUTORELIVE)
            {
                // 0x74393B `call dword ptr [ecx+0x10]` on the ENVIRONMENT's vtable, with
                // edx = self.  BLOCKED: Envir vtbl slot +0x10 is not resolved, so the
                // auto-revive worker cannot be invoked faithfully.  Left fail-closed —
                // HP is unchanged, so the `cmp [self+0x2AC],0 / setg bl` below yields
                // false, exactly as if the worker declined.
                revived = m_WAbil.HP > 0;
            }

            // 0x743948 `test bl,bl` / je, then 0x743952 RELIVEBACK, then 0x743958 the race
            // gate (`cmp byte [esi+0x178],0` / jne — only RC_PLAYOBJECT, which is 0).
            if (revived && flag != null && flag.boRELIVEBACK &&
                m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                // 0x743961-0x7439B7.  Native computes the [+0x148] axis first and pushes it
                // first; Delphi pushes right-to-left, so [+0x144] (X) is the earlier
                // parameter and [+0x148] (Y) the later one.
                var y = (short)(m_nCurrY - NativeRevivePolicy.ReliveBackJitterBias +
                    M2Share.RandomNumber.Random(NativeRevivePolicy.ReliveBackJitterSpan));
                var x = (short)(m_nCurrX - NativeRevivePolicy.ReliveBackJitterBias +
                    M2Share.RandomNumber.Random(NativeRevivePolicy.ReliveBackJitterSpan));
                SpaceMove(m_sMapName, x, y, 0);
            }

            return revived;
        }

        /// <summary>
        /// <c>sub_7436F8</c> @0x743827-0x743834 — arm the second path's cooldown state 48
        /// for <c>sub_74609C</c>'s tier value, in seconds.
        /// </summary>
        private bool TryApplyNativeReviveCooldown(byte tier)
        {
            return AddNativeTimedAbilitySeconds(
                NativeRevivePolicy.SecondPathCooldownStateType,
                value: 1, // 0x743823 `push 1`
                seconds: NativeRevivePolicy.GetCooldownSecondsForTier(tier));
        }

        /// <summary>
        /// The C# stand-in for <c>[vmt+0x1A8]</c> = <c>sub_76B478</c>, whose only job is
        /// <c>imul ecx, eax, 0x3E8</c> @0x76B48C — convert a SECONDS duration to
        /// milliseconds — before forwarding to <c>[vmt+0x1EC]</c> = <c>sub_7730D0</c>,
        /// which <see cref="AddTimedAbilityInternal"/> ports.
        ///
        /// The seconds value is truncated to 16 bits first, matching
        /// <c>movzx eax, di</c> @0x76B489.
        /// </summary>
        private bool AddNativeTimedAbilitySeconds(byte internalType, int value, int seconds)
        {
            var truncated = (ushort)seconds; // 0x76B489 movzx eax, di
            return AddTimedAbilityInternal(internalType, value,
                unchecked(truncated * 1000), 0);
        }
    }
}
