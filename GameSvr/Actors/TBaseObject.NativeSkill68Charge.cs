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

            // 0x6EC7A7 calls sub_764BC4, the ratio helper, NOT the sign helper
            // sub_764A90 that M2Share.GetNextDirection ports. The two disagree
            // on most off-axis headings.
            m_btDirection = M2Share.GetNextDirectionByRatio(m_nCurrX, m_nCurrY,
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

        /// <summary>0x770CA3 `66 BA 44 00` — the action-1028 arm looks the
        /// caster's magic 68 up by id before it strikes.</summary>
        private const ushort NativeSkill68MagicId = 0x44;

        /// <summary>0x6EC9B2 `66 B9 04 04` / 0x771AD7 `68 04 04 00 00` — the
        /// action code, which is also the wIdent the strike carries all the
        /// way into ReceiveAttackDamge. It is NOT the magic id.</summary>
        private const ushort NativeChargedFanActionCode = 0x404;

        /// <summary>
        /// sub_6EC8E8, reached from TPlayObject.Operate arm 0x6B6097 with
        /// eax = Self, edx = msg.nParam3 (the PEnvir), ecx = msg.wParam (the
        /// direction), [ebp+0xC] = msg.nParam1 (X), [ebp+8] = msg.nParam2 (Y).
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

            // 0x6EC949-0x6EC9C5. The same dir-1/dir/dir+1 fan one cell out,
            // but this scan stops at the FIRST proper target: 0x6EC9BF is an
            // unconditional `jmp` to the epilogue, while only the reject arm
            // 0x6EC9C1 does `inc esi / cmp esi,2 / jne`. The direction pushed
            // at 0x6EC9B1 is the caster's own [+0x154], not the probe
            // heading.
            for (int offset = -1; offset <= 1; offset++)
            {
                int probeDir = m_btDirection + offset;
                if (probeDir > 7)
                    probeDir = 0;
                if (probeDir < 0)
                    probeDir = 7;
                short probeX = 0;
                short probeY = 0;
                envir.GetNextPosition(m_nCurrX, m_nCurrY, probeDir, 1,
                    ref probeX, ref probeY);
                // 0x6EC995 sub_7784A8 with `6A 01`, then 0x6EC9A0
                // sub_767498 = IsProperTarget.
                var probe = envir.GetMovingObject(probeX, probeY, true)
                    as TBaseObject;
                if (probe == null || !IsProperTarget(probe))
                {
                    continue;
                }
                ExecuteNativeChargedFanAction(m_btDirection);
                break;
            }
        }

        /// <summary>
        /// The action-1028 arm of the action dispatcher sub_7707A8. Only the
        /// arm is ported, not the dispatcher: 0x7707E0 re-seats the direction
        /// and 0x770803-0x770815 indexes the 34-entry table at 0x77081C with
        /// `add eax,-1000`, so slot 28 is 0x770CA1.
        /// <para>
        /// The dispatcher's shared tail (0x770D25-0x770EBC — the [+0x188]
        /// accumulator through VMT+0x1AC, then sub_73E804) is deliberately
        /// NOT ported. It runs for all 34 action codes and C# drives ordinary
        /// attacks through a different path, so replicating it on this one arm
        /// would double-apply it. On the 1028 path its first block is inert
        /// anyway: it scales [ebp-0xC], which 0x7707EB zeroes and the arm
        /// never writes.
        /// </para>
        /// </summary>
        private void ExecuteNativeChargedFanAction(byte direction)
        {
            // 0x7707E0 `8A 45 08 / 88 86 54 01 00 00`.
            m_btDirection = direction;
            // 0x770CA1 `33 C9 / 66 BA 44 00` then VMT+0xE8 = sub_741628: a
            // plain forward scan of m_MagicList ([self+0x500]) for
            // MagicInfo.wMagicID == 68, first match wins. cl = 0 here, so the
            // `+0x0E == 0xFF` rejection at 0x741678 is skipped. A caster who
            // never learned 68 gets nil, and native carries that nil straight
            // into sub_771A5C.
            ExecuteNativeChargedFanStrike(
                FindNativeUserMagicById(NativeSkill68MagicId));
        }

        /// <summary>
        /// sub_771A5C(eax = Self, edx = UserMagic). Sweeps dir-1, dir and
        /// dir+1 one cell out and strikes EVERY proper target it finds — the
        /// loop at 0x771AF1 has no early exit, unlike the landing arm's scan.
        /// The result byte is the constant 2 written at 0x771A6A, which is why
        /// the dispatcher's `test bl,bl` checks never fire.
        /// </summary>
        private void ExecuteNativeChargedFanStrike(TUserMagic userMagic)
        {
            var envir = m_PEnvir;
            if (envir == null)
            {
                return;
            }
            // 0x771A6E `or esi,-1` then `inc esi / cmp esi,2 / jne`.
            for (int offset = -1; offset <= 1; offset++)
            {
                // 0x771A7B `cmp eax,7 / jle` -> `xor eax,eax`, then
                // 0x771A82 `test eax,eax / jge` -> `mov eax,7`.
                int probeDir = m_btDirection + offset;
                if (probeDir > 7)
                    probeDir = 0;
                if (probeDir < 0)
                    probeDir = 7;
                short hitX = 0;
                short hitY = 0;
                // 0x771AA1 sub_764CE0 with `push 1`: one step from the
                // caster's CURRENT cell, recomputed each iteration.
                envir.GetNextPosition(m_nCurrX, m_nCurrY, probeDir, 1,
                    ref hitX, ref hitY);
                // 0x771ABA sub_7784A8 with `6A 01`.
                var target = envir.GetMovingObject(hitX, hitY, true)
                    as TBaseObject;
                // 0x771AC5 sub_767498 / 0x771ACC `je` -> next heading.
                if (target == null || !IsProperTarget(target))
                {
                    continue;
                }
                // 0x771AD2 `mov ecx,[eax] / call [ecx+0x4C]` — VMT+0x4C is
                // sub_744388, already ported. It arms its own 45 s cooldown
                // on the first call, so headings two and three of the same
                // sweep get -1 back and pay no further HP; -1 then falls out
                // of ResolveFullMagicDamage at its `damage <= 0` gate.
                int power = ResolveNativeChargedCounterPower(target,
                    HUtil32.GetTickCount());
                // 0x771AD7-0x771AEC. wIdent is the ACTION code 0x404, the
                // TUserMagic rides in as the damage context, category is
                // fixed 4 by sub_76E268 @0x76E296, flags come from
                // byte [0x771B08] = 0x00, and arg0 is the `6A 01`.
                ApplyNativeDirectMagicEffect(target,
                    NativeChargedFanActionCode, true,
                    MagicDamageContext.Capture(userMagic), 0, power);
            }
        }

        /// <summary>
        /// THumanKind/TPlayObject VMT+0xE8 = sub_741628 (TCreature's slot is
        /// the unrelated sub_7725F0). Called here with cl = 0.
        /// </summary>
        private TUserMagic FindNativeUserMagicById(ushort magicId)
        {
            var list = m_MagicList;
            if (list == null)
            {
                return null;
            }
            for (int i = 0; i < list.Count; i++)
            {
                TUserMagic magic = list[i];
                // 0x741663 `mov eax,[eax] / mov ax,[eax+0x10]` — the id lives
                // on the shared TMagic definition, not on the TUserMagic.
                if (magic?.MagicInfo != null &&
                    magic.MagicInfo.wMagicID == magicId)
                {
                    return magic;
                }
            }
            return null;
        }
    }
}
