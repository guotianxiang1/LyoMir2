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
                    m_dwActionTick))
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

        // MOVE-15 — the TPlayer can-act override, VMT+0x40 = sub_6E6700.
        // It calls the inherited TCreature predicate sub_76B354 first, then
        // adds exactly ONE extra player-only term:
        //   6E6700  55 8B EC              push ebp / mov ebp,esp
        //   6E670D  E8 42 4C 08 00        call 0x76B354          ; inherited
        //   6E6712  84 C0                 test al, al
        //   6E6714  74 09                 je   0x6E671F          ; inherited false
        //   6E6716  83 BE 74 05 00 00 00  cmp  dword [esi+0x574], 0
        //   6E671D  74 04                 je   0x6E6723          ; zero  -> TRUE
        //   6E671F  33 C0                 xor  eax, eax          ; non-0 -> FALSE
        //   6E6723  B0 01                 mov  al, 1
        // `+0x574` is this class's m_nNativeForcedMoveRemaining, not a
        // separate field: the whole displacement census over the unpacked
        // image is 7 primary player-chain instructions and they are one
        // chain — the two writes in the CM_SPELL skill-27 branch
        // (0x6BC9A3 `mov dword [esi+0x574],5`, 0x6BC9AF `... ,3`), this read
        // at 0x6E6716, and the four count operations in the step processor
        // sub_73F200 (0x73F217 cmp, 0x73F224 dec, 0x73F3DE clear on failure,
        // 0x73F469 cmp before queueing the 250 ms continuation).
        // TRAP: this term is a TPlayer OVERRIDE. TCreature and THumanKind
        // keep sub_76B354 unchanged, so monsters are never blocked by it.
        // The predicate must stay on TPlayObject and must never be pushed
        // down into TBaseObject.
        internal bool IsNativeCanActBlockedByForcedMove()
        {
            return m_nNativeForcedMoveRemaining != 0;
        }

        // MOVE-21 — obj+0x6C is last successful walk/run arrival tick, not
        // last-hit. The only GetTickCount stores into a Self[+0x6C] slot
        // in the whole CODE image are the three arrival blocks:
        //   006BBD4B  E8 F0 C5 D4 FF  call 0x408340   ; GetTickCount
        //   006BBD50  89 43 6C        mov  [ebx+0x6C],eax  ; walk  sub_6BBCD8
        //   006BC092  E8 A9 C2 D4 FF  call 0x408340
        //   006BC097  89 43 6C        mov  [ebx+0x6C],eax  ; run   sub_6BBFBC
        //   006BC1AA  E8 91 C1 D4 FF  call 0x408340
        //   006BC1AF  89 43 6C        mov  [ebx+0x6C],eax  ; run3  sub_6BC0D4
        // Report A's 8 other `mov [reg+0x6C]` hits (0x6BA23B..) write
        // through `lea ebx,[edi+0x1E8]`, i.e. obj+0x254, after @ROUND —
        // they are not this field. The 500 ms motaebo gate at 0x6BC94C
        // (`sub eax,[esi+0x6C]` / `cmp eax,0x1F4` / `jbe`) therefore
        // measures time since the last walk/run arrival. C# split that
        // slot into m_dwMoveTick (invented MOVE-20 interval, written
        // before the attempt) and m_dwActionTick (written on walk/run
        // success in Message.cs / ClientNativeRun3). m_dwHitTick is the
        // animal last-hit tick (TAnimalObject +0x35C) and TPlayObject
        // never updates it. Feed m_dwActionTick.
        internal static bool IsNativeMotaeboTimingReady(int now,
            int lastMotaeboTick, int lastWalkRunTick)
        {
            return unchecked((uint)(now - lastMotaeboTick)) >
                       NativeMotaeboCooldown &&
                   unchecked((uint)(now - lastWalkRunTick)) >
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

            return Plugins.YanshenSkillPatches.BarbarianLevel(this,
                       m_Abil.Level) - target.m_Abil.Level +
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
                    SendMapDescription();
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
