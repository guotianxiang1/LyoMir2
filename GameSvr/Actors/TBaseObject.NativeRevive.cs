using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const string NativeEquipReviveNotice = "靠戒指的力量，您复活了。";
        internal const string NativeSecondPathSystemNotice = "靠戒指的力量,您获得了重生。";
        internal const string NativeSecondPathPopupNotice = "靠戒指的力量,您获得了重生";

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

            // [self+0x1D1]/[+0x1DD] rebuilt each RecalcAbilitys from agg2 — see
            // NativeEquipAgg2Revive.cs (@0x73D63D copy, @0x76235F flag, @0x7627CF tier).
            var nativeSecondPathFlag = m_btNativeSecondPathFlag != 0;
            var nativeSecondPathTier = m_btNativeSecondPathTier;

            // 复活戒指重设: plugin 0x100B3472 test of [edi+0x5B8], then A3 over
            // host 0x743758 / 0x73C4FA (cmp imm32 0xEA60) and 66 A3 over 0x743913
            // (mov cx,2). Off = the unpatched immediates. Global host write, so
            // heroes share the patched constants with players.
            var yanshenRevive = new Plugins.YanshenApi(this as TPlayObject, null,
                M2Share.PluginManager);
            var reviveResetOn = yanshenRevive.IsReviveResetPatchOn();
            var equipReviveCooldownMs = reviveResetOn
                ? yanshenRevive.ReviveResetCooldownMs()
                : NativeRevivePolicy.EquipReviveCooldownMilliseconds;
            var postReviveImmuneSeconds = reviveResetOn
                ? yanshenRevive.ReviveResetImmuneSeconds()
                : NativeRevivePolicy.PostReviveStateSeconds;

            var outcome = NativeRevivePolicy.Resolve(
                m_PEnvir?.Flag,
                hasEquipRevive: m_boRevival && !suppressed,
                lastEquipReviveTick: m_dwRevivalTick,
                tick: tick,
                secondPathFlag: nativeSecondPathFlag,
                secondPathTier: nativeSecondPathTier,
                secondPathCooldownActive: secondPathCooldownActive,
                equipReviveCooldownMs: equipReviveCooldownMs);

            switch (outcome)
            {
                case NativeRevivePolicy.Outcome.EquipRevive:
                    // 0x743761 stamp, 0x743775 HP = MaxHP, 0x743781 conditional MP.
                    m_dwRevivalTick = tick;
                    // 0x743796 xor edx,edx / 0x74379A call sub_73ED28 — mode 0, +0x104 bit0.
                    ItemDamageRevivalRing(0);
                    m_WAbil.HP = m_WAbil.MaxHP;
                    // 0x743781 `cmp byte [esi+0x1B9],0` / je — the "also refill MP" flag is
                    // a SECOND equipment bit, distinct from [+0x1B8].  It is sourced from
                    // the same unmodelled equipment block, so it stays false and MP is not
                    // restored on this path.  (The second path restores MP
                    // unconditionally, but that path is itself blocked.)
                    HealthSpellChanged();
                    SendNativeReviveNotices(NativeEquipReviveNotice,
                        NativeEquipReviveNotice);
                    break;

                case NativeRevivePolicy.Outcome.SecondPathRevive:
                    // 0x743834 arms state 48 for GetCooldownSecondsForTier(tier) seconds,
                    // then 0x74383A/0x743846 restore HP and MP unconditionally.
                    // 0x743860 mov edx,1 / 0x743867 call sub_73ED28 — mode 1, +0x104 bit1|bit2.
                    ItemDamageRevivalRing(1);
                    TryApplyNativeReviveCooldown(nativeSecondPathTier);
                    m_WAbil.HP = m_WAbil.MaxHP;
                    m_WAbil.MP = m_WAbil.MaxMP;
                    HealthSpellChanged();
                    SendNativeReviveNotices(NativeSecondPathSystemNotice,
                        NativeSecondPathPopupNotice);
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
                    postReviveImmuneSeconds);
            }

            var flag = m_PEnvir?.Flag;

            // 0x743921 `test bl,bl` / jne — AUTORELIVE runs ONLY when nothing has revived
            // yet, and 0x743945 `setg bl` then makes the result depend on HP.  A revive
            // produced here therefore gets NO state-55 window; that asymmetry is native.
            if (!revived && flag != null && flag.boAUTORELIVE)
            {
                // 0x74393B `FF 51 10 call dword ptr [ecx+0x10]` on the ENVIRONMENT's vtable,
                // with edx = self.  该槽已解析：TEnvironment 是 0x77BB38，TDynEnvir 的重写
                // 0x5FD384 与它字节级同构，两者都在 0x77BB66 / 0x5FD3B2 派发 @OnReLive，
                // 然后 `cmp [obj+0x2AC],0 / setg dl` 把 HP>0 当返回值。脚本负责加血。
                // 详见 Envirnoment.MapQuestTriggers.cs。
                revived = m_PEnvir != null && m_PEnvir.NativeEnvirAutoReliveSlot(this);
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
        /// Native success order at 0x74379F..0x7437BD and 0x74386C..0x74388A:
        /// first VMT+0xD4 with packed colour 0xFCFF, then sub_73C910 with wParam=1.
        /// The latter queues RM 12308, whose player dispatcher emits SM 213.
        /// </summary>
        private void SendNativeReviveNotices(string systemText, string popupText)
        {
            SendNativeStateSysMsg(0xFCFF, systemText);
            SendMsg(this, Grobal2.RM_NATIVE_REVIVE_MESSAGE, 1,
                0, 0, 0, popupText,
                BuildNativeTerminatedTextBody(popupText));
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
