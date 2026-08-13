using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 68. Outer ladder arm 0x6BCAC4
    /// `66 8B 45 08 / 50 / 66 8B 4D 0C / 8B 55 F0 / 8B C6 / E8 8D FC 02 00`
    /// = sub_6EC764(eax=Self, edx=UserMagic, cx=nTargetX, [ebp+8]=nTargetY),
    /// AL stored back into [ebp-5] at 0x6BCAD7 so it is the CM_SPELL answer.
    ///
    /// The cast itself only measures the runway and schedules the move; the
    /// actual relocation happens later, when the delayed 0x3043 message comes
    /// back (see ProcessNativeSkill68ChargeLanding).
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>0x6EC778 `BA 44 00 00 00` then VMT+0x1F4.</summary>
        private const int NativeSkill68ColdTimeKey = 0x44;

        /// <summary>0x6EC89D `B9 C8 AF 00 00` = 45000 ms, VMT+0x1F0.</summary>
        private const int NativeSkill68CooldownMilliseconds = 0xAFC8;

        /// <summary>0x6EC856 `83 7D F4 08` — the runway loop runs i = 1..7.</summary>
        private const int NativeSkill68MaxSteps = 7;

        /// <summary>0x6EC860 `6B 5D F0 1E` — delay = clearSteps * 30 ms.</summary>
        private const int NativeSkill68DelayPerStep = 0x1E;

        /// <summary>0x6EC864 `B9 03 00 00 00` — TrainSkill takes a literal 3,
        /// not the Random(3)+1 the magic producers use.</summary>
        private const int NativeSkill68TrainPoints = 3;

        internal bool TryActivateNativeSkill68Charge(TUserMagic userMagic,
            int targetX, int targetY)
        {
            return TryActivateNativeSkill68Charge(userMagic, targetX, targetY,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill68Charge(TUserMagic userMagic,
            int targetX, int targetY, int now)
        {
            // 0x6EC787 `85 C0 / 0F 85 4B 01 00 00` jumps to the epilogue while
            // [ebp-5] is still the 0 written at 0x6EC774, so a live cooldown is
            // a silent FALSE. Contrast id 65, whose arm writes TRUE first.
            if (GetNativeColdTimeRemaining(NativeSkill68ColdTimeKey) != 0)
            {
                return false;
            }

            var envir = m_PEnvir;
            if (envir == null)
            {
                return false;
            }

            m_btDirection = M2Share.GetNextDirection(m_nCurrX, m_nCurrY,
                targetX, targetY);

            int clearSteps = 0;
            short runwayX = m_nCurrX;
            short runwayY = m_nCurrY;
            for (int step = 1; step <= NativeSkill68MaxSteps; step++)
            {
                if (!IsNativeSkill68LaneClear(envir, runwayX, runwayY))
                {
                    break;
                }
                clearSteps++;
                envir.GetNextPosition(m_nCurrX, m_nCurrY, m_btDirection, step,
                    ref runwayX, ref runwayY);
            }

            (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                NativeSkill68TrainPoints);

            // 0x6EC88E `66 B9 43 30` + sub_766060: a delayed self-message whose
            // wDeliveryTime word (+0x16) is the clearSteps*30 pushed last, and
            // whose nParam3 is the caster's own PEnvir pointer. The landing arm
            // compares that pointer, so it rides along as the payload.
            SendDelayMsg(this, Grobal2.RM_NATIVE_CHARGE_LAND, m_btDirection,
                runwayX, runwayY, 0, string.Empty,
                clearSteps * NativeSkill68DelayPerStep, envir);

            SetNativeColdTime(NativeSkill68ColdTimeKey,
                NativeSkill68CooldownMilliseconds, now);

            // 0x6EC8CC `66 BA E6 0D` through VMT+0xE0 with boSendSelf = 1
            // (`6A 01` at 0x6EC8C8) and a nil body (`6A 00` twice).
            SendRefMsg(Grobal2.RM_NATIVE_CHARGE_MOVE, m_btDirection,
                runwayX, runwayY, 0, string.Empty);
            return true;
        }

        /// <summary>
        /// 0x6EC7D3..0x6EC825. The three headings dir-1, dir and dir+1 are each
        /// probed one cell out from the running position; the wrap is
        /// `cmp eax,7 / jle` then `xor eax,eax` and `test / jge` then
        /// `mov eax,7`, i.e. 8 wraps to 0 and -1 wraps to 7. The walkability
        /// call is sub_777EF8 with `6A 00`, the object-aware form.
        /// </summary>
        private bool IsNativeSkill68LaneClear(Envirnoment envir, short fromX,
            short fromY)
        {
            for (int offset = -1; offset <= 1; offset++)
            {
                int dir = m_btDirection + offset;
                if (dir > 7)
                    dir = 0;
                if (dir < 0)
                    dir = 7;
                short laneX = 0;
                short laneY = 0;
                envir.GetNextPosition(fromX, fromY, dir, 1, ref laneX,
                    ref laneY);
                if (!envir.CanWalk(laneX, laneY, false))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// sub_6EC8E8, reached from TPlayObject.Operate arm 0x6B6097 with
        /// eax = Self, edx = msg.nParam3 (the PEnvir), ecx = msg.wParam (the
        /// direction), [ebp+0xC] = msg.nParam1 (X), [ebp+8] = msg.nParam2 (Y).
        ///
        /// BLOCKED — the strike is not applied. 0x6EC9B2 `66 B9 04 04` enters
        /// sub_7707A8 with action code 0x404, whose arm 0x770CA1 resolves magic
        /// 68 through VMT+0xE8 and calls sub_771A5C. That in turn goes
        /// VMT+0x4C (sub_744388, ported as ResolveNativeChargedCounterPower)
        /// and then sub_76E268 -> target VMT+0x104 = sub_746318, the generic
        /// struck pipeline, which has no mapped C# entry point. Calling only
        /// the first half would charge the 45 s lock and the 10 % HP cost of
        /// sub_744388 and deal nothing, so nothing is called at all.
        /// </summary>
        internal void ProcessNativeSkill68ChargeLanding(int landX, int landY,
            int direction, object envirPayload)
        {
            var envir = m_PEnvir;
            // 0x6EC8F5 `3B 93 28 01 00 00 / 0F 85 C6 00 00 00`.
            if (envir == null || !ReferenceEquals(envirPayload, envir))
            {
                return;
            }
            // 0x6EC922 with `6A 01`, so the mover does not re-test occupancy.
            if (envir.MoveToMovingObject(m_nCurrX, m_nCurrY, this, landX,
                    landY, true) <= 0)
            {
                return;
            }
            m_nCurrX = (short)landX;
            m_nCurrY = (short)landY;
            m_btDirection = (byte)direction;
        }
    }
}
