using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// The 战神 revive (复活) subsystem — <c>sub_7436F8</c>, the handler that decides
    /// whether a dead creature comes back and on what terms.
    ///
    /// Identity is byte-established, not inferred: <c>sub_7436F8</c> occupies VMT slot
    /// <b>+0x08</b> in all ten classes that reference it (THumanKind@0x73BC34,
    /// TPlayer@0x6AC8C8, THeroAct@0x685630, TGdMsgGMAgent@0x62EF8C, TWarHero, TTaosHero,
    /// TMagHero, TSecWarHero, TSecTaosHero, TSecMagHero) and <b>no class overrides it</b>.
    /// It has zero <c>E8 rel32</c> direct callers — it is only ever reached virtually.
    /// The VMT base convention used to establish that (classname ptr at base-0x2C,
    /// instance size at base-0x28) was validated first against the sibling-known
    /// <c>+0x84</c> Die split and the <c>+0x21C</c> empty-leaf split that
    /// <see cref="NativeDeathDropPolicy"/> documents.
    ///
    /// Signature: <c>eax = self</c>, <c>edx = tick</c>, returns <c>al = bl</c>
    /// (@0x7439DE <c>mov eax,ebx</c>).
    ///
    /// The native ladder, verbatim:
    /// <code>
    /// 743726  cmp byte [Envir+0x72],0 / jne 0x7439BC   ; NoRelive      -> return FALSE
    /// 743730  cmp byte [Envir+0x7F],0 / jne 0x74390B   ; NOEQUIPRELIVE -> TAIL (not return!)
    /// 74373A  cmp byte [self+0x1B8],0  / je  0x7437C9  ; no equip revive -> PATH 2
    /// 743747  mov eax,[self+0x450] / test eax,eax / je 0x74375E        ; never used -> allow
    /// 743756  cmp edx,0xEA60 / jb 0x7437C9             ; 60000 ms CD not elapsed -> PATH 2
    /// 743761  mov [self+0x450],tick                    ; stamp
    /// 743775  HP = MaxHP        ([+0x2AC] = [+0x2B0])
    /// 743781  cmp byte [self+0x1B9],0 / je             ; only then MP = MaxMP
    /// ---- PATH 2 ----
    /// 7437CB  call sub_746084 / je 0x74390B            ; not eligible -> TAIL
    /// 7437DC  call sub_772960 (dl=0x30) / jne 0x743893 ; state 48 live -> CD msg, NO revive
    /// 743834  [vmt+0x1A8](dl=0x30, ecx=sub_74609C(), push 1)           ; arm the CD
    /// 74383A  HP = MaxHP ; 743846 MP = MaxMP           ; UNCONDITIONAL here
    /// ---- TAIL ----
    /// 74390D  test bl,bl / je  -> skip                 ; only on success:
    /// 74390F  [vmt+0x1A8](dl=0x37, ecx=2, push 1)      ; state 55 for 2 SECONDS
    /// 743921  test bl,bl / jne -> skip AUTORELIVE      ; only when nothing revived yet
    /// 74392B  cmp byte [Envir+0x7E],0 / je             ; AUTORELIVE
    /// 74393B  call [Envir_vmt+0x10](self) ; 74393E cmp [self+0x2AC],0 / setg bl
    /// 743952  cmp byte [Envir+0x7D],0 / je             ; RELIVEBACK
    /// 743958  cmp byte [self+0x178],0 / jne            ; race must be 0
    /// 743961  Random(5) + [self+0x148] - 2  ; 743982 Random(5) + [self+0x144] - 2
    /// </code>
    ///
    /// UNITS. The CD numbers are <b>seconds</b>, proven through the call chain rather than
    /// assumed: <c>[vmt+0x1A8]</c> resolves to <c>sub_76B478</c> for every class in the
    /// hierarchy, and its body does <c>imul ecx, eax, 0x3E8</c> @0x76B48C before forwarding
    /// to <c>[vmt+0x1EC]</c> = <c>sub_7730D0</c>, which stores the product in the state
    /// record's <c>+0x02</c> field. Both readers divide by 1000 again for display
    /// (<c>sub_7436F8</c> @0x7438B6, <c>sub_73C208</c> @0x73C5D9), which would render 0 for
    /// every tier if the field held raw seconds.
    /// </summary>
    internal static class NativeRevivePolicy
    {
        /// <summary>
        /// <c>sub_7436F8</c> @0x743756 — the equipment-revive cooldown, <c>cmp edx,0xEA60</c>.
        /// Hard-coded in the binary, NOT read from any config global.
        /// </summary>
        internal const int EquipReviveCooldownMilliseconds = 0xEA60; // 60000

        /// <summary>
        /// <c>sub_7436F8</c> @0x743915 — <c>mov dl,0x37</c>, the post-revive
        /// invulnerability state.
        /// </summary>
        internal const byte PostReviveStateType = 0x37; // 55

        /// <summary>
        /// <c>sub_7436F8</c> @0x743911 — <c>mov cx,2</c>. This is the DURATION passed to
        /// <c>sub_76B478</c>, which multiplies by 1000; it is NOT the state's value.
        /// </summary>
        internal const int PostReviveStateSeconds = 2;

        /// <summary>
        /// <c>sub_7436F8</c> @0x74390F — <c>push 1</c>. The stack arg becomes the state
        /// record's byte 0 (<c>sub_7730D0</c> @0x77310C-0x77310F
        /// <c>mov dl,[ebp+8]</c> / <c>mov byte [eax],dl</c>).
        /// </summary>
        internal const int PostReviveStateValue = 1;

        /// <summary>
        /// <c>sub_7436F8</c> @0x7437D8 / @0x74382E — <c>mov dl,0x30</c>, the second-path
        /// cooldown state.
        /// </summary>
        internal const byte SecondPathCooldownStateType = 0x30; // 48

        /// <summary>
        /// <c>sub_7436F8</c> @0x743961 / @0x743982 — <c>mov eax,5</c> then
        /// <c>call sub_403B4C</c> (Random) and <c>sub edx,2</c>: the RELIVEBACK landing
        /// spot is jittered by <c>Random(5) - 2</c> on each axis, i.e. -2..+2.
        /// </summary>
        internal const int ReliveBackJitterSpan = 5;

        /// <summary>Same site, <c>sub edx,2</c> @0x743971 / @0x743992.</summary>
        internal const int ReliveBackJitterBias = 2;

        /// <summary>Which leaf of <c>sub_7436F8</c> a revive attempt resolves to.</summary>
        internal enum Outcome
        {
            /// <summary>
            /// 0x7439BC with <c>bl</c> still 0 — no revive happened. Reached by the
            /// <c>NoRelive</c> gate, by an ineligible creature, or by falling through
            /// every path.
            /// </summary>
            NoRevive = 0,

            /// <summary>
            /// 0x74375E-0x7437C2 — the equipment / revive-ring path. HP is restored and MP
            /// only if the second flag is set.
            /// </summary>
            EquipRevive = 1,

            /// <summary>
            /// 0x7437E9-0x74388F — the graded second path. HP and MP are both restored
            /// unconditionally and the tiered cooldown state 48 is armed.
            /// </summary>
            SecondPathRevive = 2,

            /// <summary>
            /// 0x7437E3 -> 0x743893 — the second path was eligible but its cooldown state
            /// 48 is still live, so the player only gets a "time remaining" message.
            /// NO revive, and <c>bl</c> stays 0.
            /// </summary>
            SecondPathOnCooldown = 3,
        }

        /// <summary>
        /// The five-tier cooldown table <c>sub_74609C</c>, verbatim:
        /// <code>
        /// 74609C  mov al,[eax+0x1DD]                 ; tier
        /// 7460A2  dec al / je 0x7460B4 -> mov eax,0x96  (150)
        /// 7460A6  dec al / je 0x7460BA -> mov eax,0x78  (120)
        /// 7460AA  dec al / je 0x7460C0 -> mov eax,0x5A  ( 90)
        /// 7460AE  dec al / je 0x7460C6 -> mov eax,0x3C  ( 60)
        /// 7460B2  jmp     0x7460CC     -> mov eax,0x12C (300)
        /// </code>
        /// Values are SECONDS (see the class remarks for the <c>imul ...,0x3E8</c> proof).
        /// Tier 0 lands on the DEFAULT arm: <c>dec al</c> on 0 yields 0xFF, which never
        /// matches any <c>je</c>, so it falls through to 0x7460CC.
        /// </summary>
        internal static int GetCooldownSecondsForTier(byte tier)
        {
            switch (tier)
            {
                case 1: return 150; // 0x7460B4 mov eax,0x96
                case 2: return 120; // 0x7460BA mov eax,0x78
                case 3: return 90;  // 0x7460C0 mov eax,0x5A
                case 4: return 60;  // 0x7460C6 mov eax,0x3C
                default: return 300; // 0x7460CC mov eax,0x12C
            }
        }

        /// <summary>
        /// <c>sub_746084</c> — is the creature eligible for the second revive path?
        /// <code>
        /// 746084  cmp byte [eax+0x1D1],0 / jne 0x746099   -> TRUE
        /// 74608D  cmp byte [eax+0x1DD],0 / ja  0x746099   -> TRUE   (tier > 0)
        /// 746096  xor eax,eax                             -> FALSE
        /// </code>
        /// Both source fields live inside the 54-byte block <c>[self+0x1B0..+0x1E5]</c>
        /// that <c>sub_73D500</c> rebuilds from the equipment aggregate
        /// (@0x73D542 FillChar 0x36 bytes, then @0x73D63D-0x73D650
        /// <c>rep movsd</c>+<c>movsw</c> of 0x36 bytes from <c>[[self+0x4C0]+0x1F8]</c>).
        /// Rebuilt each <c>RecalcAbilitys</c> by <see cref="NativeEquipAgg2Revive"/>.
        /// </summary>
        internal static bool IsSecondPathEligible(bool secondPathFlag, byte tier)
        {
            return secondPathFlag || tier > 0;
        }

        /// <summary>
        /// The gate ladder of <c>sub_7436F8</c> evaluated over the map flags and the
        /// creature's revive state. This mirrors the native control flow exactly,
        /// including the fall-through from a cooled-down path 1 into path 2.
        /// </summary>
        /// <param name="flag">The map's flag record (<c>[self+0x128]</c>).</param>
        /// <param name="hasEquipRevive"><c>[self+0x1B8]</c>.</param>
        /// <param name="lastEquipReviveTick"><c>[self+0x450]</c>.</param>
        /// <param name="tick">The <c>edx</c> argument.</param>
        /// <param name="secondPathFlag"><c>[self+0x1D1]</c>.</param>
        /// <param name="secondPathTier"><c>[self+0x1DD]</c>.</param>
        /// <param name="secondPathCooldownActive">
        /// <c>sub_772960(self, 0x30)</c> — is state 48 currently set?
        /// </param>
        internal static Outcome Resolve(TMapFlag flag, bool hasEquipRevive,
            int lastEquipReviveTick, int tick, bool secondPathFlag, byte secondPathTier,
            bool secondPathCooldownActive,
            int equipReviveCooldownMs = EquipReviveCooldownMilliseconds)
        {
            // 0x743720 reads [self+0x128] unconditionally; a nil map would have faulted
            // natively, so "no map" cannot revive.
            if (flag == null) return Outcome.NoRevive;

            // 0x743726 / 0x74372A — NoRelive returns FALSE outright (jne 0x7439BC).
            if (flag.boNoRelive) return Outcome.NoRevive;

            // 0x743730 / 0x743734 — NOEQUIPRELIVE jumps to the TAIL (jne 0x74390B), so it
            // suppresses BOTH item paths but leaves AUTORELIVE/RELIVEBACK to the caller.
            if (flag.boNOEQUIPRELIVE) return Outcome.NoRevive;

            // 0x74373A / 0x743741 — no equipment revive => straight to path 2.
            if (hasEquipRevive)
            {
                // 0x743747-0x74375C: a zero stamp always passes (test eax,eax / je), and
                // an elapsed CD passes; otherwise fall THROUGH to path 2 (jb 0x7437C9).
                // equipReviveCooldownMs is the imm32 at 0x743758 (`60 EA 00 00`); the
                // yanshen 复活戒指重设 patch writes atoi(重设时间)*1000 over it.
                var offCooldown = lastEquipReviveTick == 0 ||
                    unchecked(tick - lastEquipReviveTick) >= equipReviveCooldownMs;
                if (offCooldown) return Outcome.EquipRevive;
            }

            // ---- PATH 2 ----
            // 0x7437CB / 0x7437D2 — sub_746084 false => TAIL with bl still 0.
            if (!IsSecondPathEligible(secondPathFlag, secondPathTier))
            {
                return Outcome.NoRevive;
            }

            // 0x7437DC / 0x7437E3 — state 48 live => CD message only, no revive.
            if (secondPathCooldownActive) return Outcome.SecondPathOnCooldown;

            return Outcome.SecondPathRevive;
        }

        /// <summary>
        /// Does this outcome grant the post-revive invulnerability window?
        /// <c>sub_7436F8</c> @0x74390B <c>test bl,bl</c> — only a path that set
        /// <c>bl = 1</c> qualifies. Note that an AUTORELIVE-produced revive sets
        /// <c>bl</c> later (@0x743945) and therefore gets NO window; that asymmetry is
        /// native behaviour.
        /// </summary>
        internal static bool GrantsPostReviveWindow(Outcome outcome)
        {
            return outcome == Outcome.EquipRevive || outcome == Outcome.SecondPathRevive;
        }
    }
}
