namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>Native state 29 (0x1D) — paralysis; tested at 0x746620 and 0x7465A6.</summary>
        protected const byte NativeParalysisStateId = 0x1D;

        /// <summary>
        /// STATE-19 / POIS-19 — the VMT+0xC8 virtual slot, i.e. native
        /// <c>MakePosion</c>. Base implementation is <c>TCreature.MakePosion</c>
        /// @0x76B3C8, held by <c>TCreature</c> (VMT 0x764608) and every monster,
        /// animal and NPC subclass. <c>THumanKind</c> (0x73BC34), <c>TPlayer</c>
        /// (0x6AC8C8) and the hero classes override the slot with 0x746604.
        /// <para>
        /// This is the engine-wide timed-state applier, not a poison-only helper:
        /// the 24 direct native call sites pass state ids 17, 20, 21, 22, 23, 24,
        /// 25, 26, 27, 29 and 31, and the delayed-apply message 10300 adds 30.
        /// The public <see cref="MakePosion"/> is the narrower legacy face of the
        /// same slot (it takes a poison slot index and maps it as 31 - slot).
        /// </para>
        /// </summary>
        /// <param name="stateId">Native state id in <c>dl</c>, passed through untouched.</param>
        /// <param name="seconds">Native <c>cx</c>; zero-extended then scaled by 1000.</param>
        /// <param name="point">Native <c>[ebp+8]</c>; the AddState value field.</param>
        internal virtual bool NativeMakePosion(byte stateId, ushort seconds,
            ushort point)
        {
            return NativeCreatureMakePosion(stateId, seconds, point);
        }

        /// <summary>
        /// <c>TCreature.MakePosion</c> @0x76B3C8. Delphi register ABI:
        /// <c>eax</c>=Self, <c>dl</c>=state id, <c>cx</c>=seconds,
        /// <c>[ebp+8]</c>=value; <c>ret 4</c>, no result.
        /// <code>
        /// 76B3CE  8B F9                  mov  edi, ecx           ; cx = seconds
        /// 76B3D0  8B DA                  mov  ebx, edx           ; dl = state id
        /// 76B3D8  E8 67 88 00 00         call 0x773C44           ; ImmuneCheck(self, id)
        /// 76B3DF  75 44                  jne  0x76B425           ;   -> silent abort
        /// 76B3E1  B2 34                  mov  dl, 0x34
        /// 76B3E5  E8 76 75 00 00         call 0x772960           ; HasState(52)
        /// 76B3EC  75 37                  jne  0x76B425           ;   -> silent abort
        /// 76B3EE  80 FB 12               cmp  bl, 0x12
        /// 76B3F1  75 16                  jne  0x76B409
        /// 76B3F3  B2 1A / E8 64 75 00 00 HasState(0x1A)
        /// 76B3FE  74 09                  je   0x76B409
        /// 76B404  E8 C7 00 00 00         call 0x76B4D0           ; RemoveState(0x1A)
        /// 76B409  0F B7 45 08 / 50       push movzx word [ebp+8] ; value
        /// 76B40E  6A 00                  push 0                  ; new-node flag
        /// 76B410  0F B7 C7               movzx eax, di           ; seconds, zero-extended
        /// 76B413  69 C8 E8 03 00 00      imul ecx, eax, 0x3E8    ; -> milliseconds
        /// 76B41F  FF 93 EC 01 00 00      call [ebx+0x1EC]        ; AddState @0x7730D0
        /// </code>
        /// <para>
        /// Both guards are a strict subset of the gate <c>AddState</c> itself runs
        /// through VMT+0x1E8 (@0x772F84, mirrored by
        /// <see cref="CanAddNativeTimedAbility"/>), so they refuse nothing the gate
        /// would allow. They are reproduced because they run <em>before</em> the
        /// state-18 companion below, which the gate does not cover.
        /// </para>
        /// <para>
        /// That companion is not redundant with the one inside
        /// <see cref="AddTimedAbilityInternal"/>. The latter mirrors the
        /// state-gained mutation VMT+0x60 @0x77327C, whose id==0x12 arm @0x773316
        /// only runs when the node was created or raised. Native MakePosion runs its
        /// copy unconditionally, so a repeat MakePosion(0x12) that does not move the
        /// node still clears petrify. Ordering differs too — native clears 0x1A
        /// before AddState, the companion clears it after — but nothing in the
        /// state-18 add path reads state 26, so only the
        /// unconditional-versus-conditional difference is observable.
        /// </para>
        /// </summary>
        protected bool NativeCreatureMakePosion(byte stateId, ushort seconds,
            ushort point)
        {
            // 0x76B3D8 -> 0x773C44
            if (IsImmuneToTimedAbility(stateId))
            {
                return false;
            }

            // 0x76B3E1 mov dl,0x34 -> 0x772960
            if (HasNativeActiveState(TimedAbilityGlobalBlockState))
            {
                return false;
            }

            // 0x76B3EE cmp bl,0x12 / 0x76B3F3 HasState(0x1A) / 0x76B404 RemoveState(0x1A)
            if (stateId == 0x12 && HasNativeActiveState(NativeState26Type))
            {
                RemoveTimedAbilityInternal(NativeState26Type);
            }

            // 0x76B413 imul ecx, eax, 0x3E8 / 0x76B41F call [ebx+0x1EC]
            return AddTimedAbilityInternal(stateId, point,
                unchecked(seconds * 1000), 0);
        }

        /// <summary>
        /// <c>THumanKind.MakePosion</c> @0x746604 — shared body for the
        /// <c>TPlayObject</c> and <c>HeroObject</c> overrides, which stand in for
        /// native <c>THumanKind</c> (this port has no such class). Native holders of
        /// 0x746604: <c>THumanKind</c> 0x73BC34, <c>TPlayer</c> 0x6AC8C8,
        /// <c>THeroAct</c> 0x685630, <c>TWarHero</c> 0x685968, <c>TTaosHero</c>
        /// 0x685CA0, <c>TMagHero</c> 0x685FD8, <c>TSecWarHero</c> 0x5F55A8,
        /// <c>TSecTaosHero</c> 0x5F58E4, <c>TSecMagHero</c> 0x5F5C24,
        /// <c>TGdMsgGMAgent</c> 0x62EF8C.
        /// <code>
        /// 74660B  66 89 4D FE            mov  [ebp-2], cx        ; stash seconds
        /// 746613  B2 34 / E8 44 C3 02 00 HasState(0x34)
        /// 74661E  75 2F                  jne  0x74664F           ; -> silent abort
        /// 746620  80 FB 1D               cmp  bl, 0x1D           ; state 29 only
        /// 746623  75 18                  jne  0x74663D
        /// 746625  66 8B BE 80 01 00 00   mov  di, word [esi+0x180]
        /// 74662C  B8 64 00 00 00         mov  eax, 100
        /// 746631  E8 16 D5 CB FF         call 0x403B4C           ; Random(100)
        /// 746636  0F B7 D7               movzx edx, di
        /// 746639  3B C2                  cmp  eax, edx
        /// 74663B  7C 12                  jl   0x74664F           ; roll &lt; resist -> abort
        /// 74664A  E8 79 4D 02 00         call 0x76B3C8           ; inherited, direct call
        /// </code>
        /// State 29 is paralysis — the native walk/run/turn/pose gate 0x76B354
        /// refuses on it at 0x76B368.
        /// </summary>
        protected bool NativeHumanKindMakePosion(byte stateId, ushort seconds,
            ushort point)
        {
            // 0x746613
            if (HasNativeActiveState(TimedAbilityGlobalBlockState))
            {
                return false;
            }

            // 眼神 · 麻痹中不被麻痹a：208 0x100902B4 / 207 0x100827A4。
            if ((stateId == NativeParalysisStateId || stateId == 0x1A) &&
                Plugins.YanshenPage2ExtBehaviors
                    .ShouldImmuneParalysisWhileStatusActive(this))
            {
                return true;
            }

            // 0x746620 .. 0x74663B
            if (stateId == NativeParalysisStateId &&
                IsRefusedByNativeParalysisResist())
            {
                return false;
            }

            // 0x74664A — a direct call to 0x76B3C8, not a second virtual dispatch.
            return NativeCreatureMakePosion(stateId, seconds, point);
        }

        /// <summary>
        /// <c>THumanKind.CanAddState</c> @0x7465D4, the VMT+0x1E8 override, and its
        /// nested helper @0x74659C. Shared body for the <c>TPlayObject</c> and
        /// <c>HeroObject</c> overrides of
        /// <see cref="CanAddNativeTimedAbility"/>.
        /// <code>
        /// 7465E6  E8 99 C9 02 00         call 0x772F84           ; inherited gate
        /// 7465ED  74 0B                  je   0x7465FA           ;   false -> false
        /// 7465EF  55 / E8 A7 FF FF FF    call 0x74659C           ; nested closure
        /// 7465F6  84 C0 / 74 04          test al,al / je 0x7465FE
        /// 7465FA  33 C0                  xor  eax, eax           ; vetoed
        /// 7465FE  B0 01                  mov  al, 1
        ///
        /// ; nested 0x74659C, reads the parent frame through [ebp+8]
        /// 7465A6  80 78 FF 1D            cmp  byte [eax-1], 0x1D ; parent's state id
        /// 7465AA  75 20                  jne  0x7465CC
        /// 7465AF  8B 40 F8               mov  eax, [eax-8]       ; parent's Self
        /// 7465B2  66 8B B0 80 01 00 00   mov  si, word [eax+0x180]
        /// 7465B9  B8 64 00 00 00         mov  eax, 100
        /// 7465BE  E8 89 D5 CB FF         call 0x403B4C           ; Random(100)
        /// 7465C6  3B C2                  cmp  eax, edx
        /// 7465C8  7D 02                  jge  0x7465CC           ; roll &gt;= resist -> pass
        /// 7465CA  B3 01                  mov  bl, 1              ; else veto
        /// </code>
        /// Same predicate as the MakePosion override, at a different call site — so
        /// a player reached through MakePosion is rolled against TWICE, with two
        /// independent draws.
        /// </summary>
        protected bool CanAddNativeTimedAbilityHumanKind(byte internalType)
        {
            // 0x7465E6 call 0x772F84
            if (!CanAddNativeTimedAbilityCreature(internalType))
            {
                return false;
            }

            // 0x7465EF call 0x74659C
            if (internalType == NativeParalysisStateId &&
                IsRefusedByNativeParalysisResist())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// The <c>Random(100) &lt; resist</c> draw shared by the two native
        /// state-29 veto sites, 0x746631 and 0x7465BE. Native compares the
        /// zero-extended word against the roll and aborts on <c>jl</c> / <c>jge</c>
        /// respectively, which is the same predicate written from both sides.
        /// </summary>
        protected bool IsRefusedByNativeParalysisResist()
        {
            return M2Share.RandomNumber.Random(100) < NativeParalysisResistPercent;
        }

        /// <summary>
        /// Native <c>word [self+0x180]</c> — the paralysis-resistance percentage.
        /// <para>
        /// BLOCKED: no producer exists in this port, so it fail-closes to 0 and both
        /// rolls above are no-ops until the feed lands. Native rebuilds the field in
        /// the ability recompute — zeroed at 0x73D57F
        /// (<c>66 C7 86 80 01 00 00 00 00  mov word [esi+0x180],0</c>), accumulated
        /// per equipped source at 0x73DA5A/0x73DA61
        /// (<c>66 8B 87 AC 00 00 00  mov ax,word [edi+0xAC]</c> then
        /// <c>66 01 86 80 01 00 00  add word [esi+0x180],ax</c>), and snapshotted
        /// into the client ability record at 0x743E4E/0x743E55 (<c>-&gt; word
        /// [esi+0x9C]</c>). The source attribute (item +0xAC, listed as 麻痹抗性 in
        /// the type-2 StdItem attribute table) is not modelled.
        /// </para>
        /// </summary>
        protected virtual int NativeParalysisResistPercent => 0;
    }
}
