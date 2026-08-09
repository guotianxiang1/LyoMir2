using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeMotaeboCooldown = 4500;
        private const int NativeMotaeboHitCooldown = 500;
        private const int NativeMotaeboContinuationDelay = 250;
        private const int NativeMotaeboBlockedState = 52;

        internal bool TryStartNativeMotaeboForcedMove(TUserMagic magic,
            byte direction)
        {
            if (magic?.MagicInfo == null)
                return false;
            if (HasNativeActiveState(45))
                return false;

            int now = HUtil32.GetTickCount();
            if (!IsNativeMotaeboTimingReady(now, m_dwDoMotaeboTick,
                    m_dwHitTick))
                return false;

            m_dwDoMotaeboTick = now;
            m_btDirection = direction;
            ushort spellPoint = GetSpellPoint(magic);
            if (m_WAbil.MP < spellPoint)
                return false;

            if (spellPoint > 0)
            {
                DamageSpell(spellPoint);
                HealthSpellChanged();
            }

            int effectiveLevel =
                GetNativeHumanMagicEffectiveLevel(magic);
            return StartNativeMotaeboForcedMoveStep(direction,
                effectiveLevel, magic);
        }

        internal static bool IsNativeMotaeboTimingReady(int now,
            int lastMotaeboTick, int lastHitTick)
        {
            return unchecked((uint)(now - lastMotaeboTick)) >
                       NativeMotaeboCooldown &&
                   unchecked((uint)(now - lastHitTick)) >
                       NativeMotaeboHitCooldown;
        }

        internal bool StartNativeMotaeboForcedMoveStep(byte direction,
            int effectiveLevel, TUserMagic magic)
        {
            m_btDirection = direction;
            m_nNativeForcedMoveRemaining = effectiveLevel >= 3 ? 5 : 3;
            return ExecuteNativeMotaeboForcedMoveStep(direction,
                effectiveLevel, magic, true, 0);
        }

        internal bool CanTrainNativeMotaebo(TUserMagic magic)
        {
            return magic?.MagicInfo?.TrainLevel != null &&
                   magic.btLevel < 3 &&
                   magic.btLevel < magic.MagicInfo.TrainLevel.Length &&
                   magic.MagicInfo.TrainLevel[magic.btLevel] <=
                       m_Abil.Level;
        }

        internal void ContinueNativeMotaeboForcedMove(
            TProcessMessage processMessage)
        {
            if (processMessage == null ||
                processMessage.wIdent !=
                    Grobal2.RM_NATIVE_MOOTEBO_CONTINUE)
                return;

            ExecuteNativeMotaeboForcedMoveStep(
                unchecked((byte)processMessage.wParam),
                processMessage.nParam1,
                processMessage.Payload as TUserMagic, false, 0);
        }

        internal bool CanNativeMotaeboPush(TBaseObject target,
            int eligibilityBonus)
        {
            if (target == null || target == this || target.m_boDeath ||
                target.m_boGhost || target.m_boAdminMode ||
                target.m_boStoneMode || target.m_boStickMode ||
                target.m_PEnvir != m_PEnvir ||
                target.HasNativeActiveState(NativeMotaeboBlockedState) ||
                unchecked((byte)(target.m_btRaceServer + 16)) < 2)
                return false;

            return m_Abil.Level - target.m_Abil.Level +
                       eligibilityBonus > 0 && IsProperTarget(target);
        }

        private bool ExecuteNativeMotaeboForcedMoveStep(byte direction,
            int magicLevel, TUserMagic magic, bool firstStep,
            int eligibilityBonus)
        {
            if (m_nNativeForcedMoveRemaining <= 0)
                return false;

            m_nNativeForcedMoveRemaining--;
            m_btDirection = direction;
            bool canMove = true;
            bool moved = false;
            TBaseObject frontActor = GetPoseCreate();

            if (frontActor != null)
            {
                if (!CanNativeMotaeboPush(frontActor, eligibilityBonus))
                {
                    canMove = false;
                }
                else
                {
                    if (magicLevel >= 3)
                        TryPushSecondNativeMotaeboActor(direction,
                            eligibilityBonus);
                    if (frontActor.CharPushed(direction, 1) != 1)
                        canMove = false;
                }
            }

            if (canMove)
            {
                short nextX = 0;
                short nextY = 0;
                if (GetFrontPosition(ref nextX, ref nextY) &&
                    m_PEnvir.MoveToMovingObject(m_nCurrX, m_nCurrY,
                        this, nextX, nextY, false) > 0)
                {
                    m_nCurrX = nextX;
                    m_nCurrY = nextY;
                    m_btDirection = direction;
                    SendRefMsg(Grobal2.RM_RUSH, direction, m_nCurrX,
                        m_nCurrY, 0, string.Empty);
                    moved = true;
                }
            }

            if (moved)
            {
                if (firstStep && frontActor != null)
                    ApplyNativeMotaeboCollisionDamage(frontActor);
            }
            else
            {
                m_nNativeForcedMoveRemaining = 0;
                if (firstStep)
                    SysMsg("缺乏冲撞力量", MsgColor.Red, MsgType.Hint);
                ApplyNativeMotaeboFailureDamage();
            }

            if (m_nNativeForcedMoveRemaining > 0)
            {
                SendDelayMsg(this,
                    Grobal2.RM_NATIVE_MOOTEBO_CONTINUE, direction,
                    magicLevel, 0, 0, string.Empty,
                    NativeMotaeboContinuationDelay, magic);
            }
            return moved;
        }

        private void TryPushSecondNativeMotaeboActor(byte direction,
            int eligibilityBonus)
        {
            short x = 0;
            short y = 0;
            if (!m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY,
                    direction, 2, ref x, ref y))
                return;

            var target = m_PEnvir.GetMovingObject(x, y, true) as
                TBaseObject;
            if (CanNativeMotaeboPush(target, eligibilityBonus))
                target.CharPushed(direction, 1);
        }

        private void ApplyNativeMotaeboCollisionDamage(
            TBaseObject target)
        {
            int damage = M2Share.RandomNumber.Random(10) + 1;
            damage = target.ResolveNativeMotaeboDamage(damage);
            // Left on the nil-attacker overload deliberately. This is the
            // collision half of the forced-move path; I have no byte evidence
            // for which native +0xA8 site it corresponds to, and native does
            // pass `xor ecx,ecx` at 0x73F3AD / 0x73F43E, so nil is a real
            // native shape rather than a fallback. Threading `this` here would
            // be a guess that silently enables the super-force job scaling.
            target.StruckDamage(damage);
            target.SendRefMsg(Grobal2.RM_STRUCK_MAG, (short)damage,
                0, 0, ObjectId, string.Empty);
        }

        private void ApplyNativeMotaeboFailureDamage()
        {
            int damage = M2Share.RandomNumber.Random(10) + 10;
            damage = ResolveNativeMotaeboDamage(damage);
            StruckDamage(damage);
            SendRefMsg(Grobal2.RM_STRUCK, (short)damage, 0, 0, 0,
                string.Empty);
        }
    }
}
