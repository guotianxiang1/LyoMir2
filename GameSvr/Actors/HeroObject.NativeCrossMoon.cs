using SystemModule;

namespace GameSvr
{
    public partial class HeroObject
    {
        // TWarHero +0x6E4/+0x6E5/+0x6E6/+0x6E8.
        internal bool m_boNativeWarCrossMoonShortSelected;
        internal bool m_boNativeWarCrossMoonLongSelected;
        internal bool m_boNativeWarCrossMoonReady;
        internal int m_dwNativeWarCrossMoonReadyTick;

        private bool TryRunNativeWarCrossMoon(TBaseObject target, int now)
        {
            if (target == null ||
                unchecked(now - m_dwHitTick) <= m_nNextHitTime)
            {
                return false;
            }

            // sub_693090 arms +0xE8 before evaluating the cross-moon
            // selector, so both selections may coexist in one decision pass.
            UpdateNativeWarAction1017Ready(target);

            bool selected = m_boNativeWarCrossMoonReady &&
                TrySelectNativeWarCrossMoon(target, out _);
            if (selected)
                m_boNativeWarCrossMoonReady = false;

            // sub_693090 reaches this common epilogue after the selector. A
            // stance first armed here therefore cannot fire in the same pass.
            UpdateNativeWarCrossMoonReady(now);

            // 0x68B7EE gates VMT+0x2A4 on the selector's AL result. The
            // selector keeps that result true for an adjacent target, but a
            // rejected 2/4-cell selection must not consume persisted flags.
            if (!selected && NativeGridDistance(m_nCurrX, m_nCurrY,
                    target.m_nCurrX, target.m_nCurrY) > 1)
            {
                return false;
            }

            // 0x68B7F2 and sub_692C5C reload self+0x344 after selection.
            target = m_TargetCret;
            if (target == null)
                return false;

            byte direction = M2Share.GetNextDirection(m_nCurrX, m_nCurrY,
                target.m_nCurrX, target.m_nCurrY);

            // sub_692AF4 consumes +0xE8 at 0x692C40 before reaching the
            // +0x6E4/+0x6E5 branch. The 1017 resolver clears +0xE8 itself;
            // cross-moon selection bytes intentionally survive this action.
            if (m_btNativeChargedIndicator != 0)
            {
                m_btDirection = direction;
                RunNativeAction1017();
                CompleteNativeWarHeroAction();
                return true;
            }

            // The separate executor reads the persisted selection bytes
            // directly, with long taking priority over short.
            int action = m_boNativeWarCrossMoonLongSelected
                ? NativeAction1012Code
                : m_boNativeWarCrossMoonShortSelected
                    ? NativeAction1011Code
                    : 0;
            if (action == 0)
                return false;

            RunNativeCrossMoonAction(action, direction, target);

            // sub_692AF4 clears all three bytes after sub_7707A8 returns.
            m_boNativeWarCrossMoonReady = false;
            m_boNativeWarCrossMoonLongSelected = false;
            m_boNativeWarCrossMoonShortSelected = false;
            ClearNativeActiveState(3);
            StatusChanged();

            CompleteNativeWarHeroAction();
            return true;
        }

        private void UpdateNativeWarAction1017Ready(TBaseObject target)
        {
            TUserMagic magic = m_NativeChargedCounterMagic;
            if (magic?.MagicInfo == null || target == null ||
                NativeGridDistance(m_nCurrX, m_nCurrY,
                    target.m_nCurrX, target.m_nCurrY) > 2 ||
                GetNativeColdTimeRemaining(magic.MagicInfo.wMagicID) != 0 ||
                magic.wMagIdx == 0x00FF)
            {
                return;
            }

            // sub_693090 @0x693111. The release resolver clears the same byte
            // at sub_744388 @0x74439C before revalidating the cached magic.
            m_btNativeChargedIndicator = 1;
        }

        private void CompleteNativeWarHeroAction()
        {
            // 0x692DF1 calls sub_76BECC(30,100,0), then 0x692E04 calls
            // sub_76BEC8(2). Only the spell budget is clamped at zero.
            m_nHealthTick = unchecked(m_nHealthTick - 30);
            m_nSpellTick = HUtil32._MAX(0,
                unchecked(m_nSpellTick - 100));
            DecreaseHealthSpellRecoveryStep(2);

            // 0x692E10 samples one new tick for both fields.
            int completedTick = HUtil32.GetTickCount();
            m_dwHitTick = completedTick;
            m_dwTargetFocusTick = completedTick;
        }

        private bool TrySelectNativeWarCrossMoon(TBaseObject target,
            out int action)
        {
            action = 0;
            if (target == null || m_boNativeWarCrossMoonShortSelected ||
                m_boNativeWarCrossMoonLongSelected)
            {
                return false;
            }

            int distance = NativeGridDistance(m_nCurrX, m_nCurrY,
                target.m_nCurrX, target.m_nCurrY);
            byte race = target.m_btRaceServer;
            bool specialRace = race == Grobal2.RC_PLAYOBJECT ||
                race == Grobal2.RC_HEROOBJECT || race == 0x81;
            int maximumRange = specialRace &&
                target.m_Abil.Level >= m_Abil.Level ? 2 : 4;
            if (distance <= 0 || distance > maximumRange)
                return false;

            int deltaX = Math.Abs(target.m_nCurrX - m_nCurrX);
            int deltaY = Math.Abs(target.m_nCurrY - m_nCurrY);
            if (deltaX != 0 && deltaY != 0 && deltaX != deltaY)
                return false;

            if (maximumRange == 2)
            {
                m_boNativeWarCrossMoonShortSelected = true;
                action = NativeAction1011Code;
            }
            else
            {
                m_boNativeWarCrossMoonLongSelected = true;
                action = NativeAction1012Code;
            }
            return true;
        }

        private void UpdateNativeWarCrossMoonReady(int now)
        {
            TUserMagic magic = m_MagicArr[SpellsDef.SKILL_CROSSMOON];
            if (magic == null || magic.wMagIdx == 0x00FF ||
                unchecked(m_WAbil.MaxHP / 20) >
                unchecked(m_WAbil.MaxHP - m_WAbil.HP) ||
                unchecked((uint)(now - m_dwNativeWarCrossMoonReadyTick)) <
                25000u)
            {
                return;
            }

            m_dwNativeWarCrossMoonReadyTick = now;
            m_boNativeWarCrossMoonReady = true;
            SetNativeActiveState(3);
            StatusChanged();
        }

        private void ProcessNativeWarCrossMoonSelectionExpiry(int now)
        {
            if (!m_boNativeWarCrossMoonLongSelected ||
                unchecked((uint)(now - m_dwNativeWarCrossMoonReadyTick)) <
                5000u)
            {
                return;
            }

            m_boNativeWarCrossMoonLongSelected = false;
            ClearNativeActiveState(3);
            StatusChanged();
        }
    }
}
