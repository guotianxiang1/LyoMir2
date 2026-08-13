using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// HIT-ARM — the second half of the "acting reveals you" pair, plus the
    /// combined hook that the CM dispatcher actually calls.
    ///
    /// MOVE-11 ported sub_7742C0 (stealth state 0x40) as
    /// <see cref="BreakNativeStealthOnAction"/>. Native never calls that one
    /// straight from the dispatcher for hits or spells — it calls the wrapper
    /// sub_6F2D48, which pairs it with a twin routine for a different state.
    ///
    /// == sub_6F2D48(eax = Self, edx = Ident) — full body, 35 bytes ==
    ///   0x6F2D48  55                 push ebp
    ///   0x6F2D49  8B EC              mov  ebp,esp
    ///   0x6F2D4B  53                 push ebx
    ///   0x6F2D4C  56                 push esi
    ///   0x6F2D4D  8B F2              mov  esi,edx          ; esi = Ident
    ///   0x6F2D4F  8B D8              mov  ebx,eax          ; ebx = Self
    ///   0x6F2D51  8B C3              mov  eax,ebx
    ///   0x6F2D53  E8 CC 00 08 00     call 0x772E24         ; ALWAYS: break hide 0x3C
    ///   0x6F2D58  81 FE 0B 01 00 00  cmp  esi,0x10B        ; 267
    ///   0x6F2D5E  74 07              je   0x6F2D67         ; 267 -> keep stealth
    ///   0x6F2D60  8B C3              mov  eax,ebx
    ///   0x6F2D62  E8 59 15 08 00     call 0x7742C0         ; else break stealth 0x40
    ///   0x6F2D67  5E 5B 5D C3        pop esi; pop ebx; pop ebp; ret
    ///
    /// The hide half is unconditional; only the stealth half is exempted.
    ///
    /// rel32 census of sub_6F2D48 — exactly 3 call sites, all in the CM
    /// dispatcher sub_6D7D68:
    ///   0x6D9EB4  HIT CASE1 (0x6D9EAF), `33 D2 xor edx,edx` at 0x6D9EAF
    ///   0x6D9F50  HIT CASE2 (0x6D9F4B), `33 D2 xor edx,edx` at 0x6D9F4B
    ///   0x6DA054  CM_SPELL(3017) arm 0x6DA04A,
    ///             `0F B7 50 0A movzx edx,word [msg+0x0A]` at 0x6DA04D
    /// So the exemption can only ever fire on CM_SPELL: the two hit arms hand
    /// in a literal zero. Series ([msg+0x0A]) is the magic id, which makes
    /// 0x10B the magic 267 that <c>TryActivateNativeSkill267</c> already
    /// models — same literal 0x10B is its cold-time key at 0x774054. 267
    /// grants bodyState 0x46 (and 0x41 when magic 0x104 is known), a buff read
    /// and consumed by the damage paths at 0x771169 / 0x7714BA / 0x7716D0, so
    /// it is the one spell you may cast without stepping out of stealth.
    ///
    /// == sub_772E24(eax = Self) — the hide twin of sub_7742C0 ==
    ///   0x772E3A  B2 3C              mov  dl,0x3C
    ///   0x772E3E  E8 1D FB FF FF     call 0x772960         ; InBodyState(0x3C)
    ///   0x772E43  84 C0 / 74 4D      test al,al / je 0x772E94   ; not hidden -> no-op
    ///   0x772E47  B2 3C              mov  dl,0x3C
    ///   0x772E4B  E8 80 86 FF FF     call 0x76B4D0         ; clear 0x3C + unlink node
    ///   0x772E50  8B C3              mov  eax,ebx
    ///   0x772E52  E8 61 00 00 00     call 0x772EB8         ; pass-through grant
    ///   0x772E57  84 C0 / 75 39      test al,al / jne 0x772E94  ; still granted -> no re-show
    ///   0x772E5B  8B 83 2C 01 00 00  mov  eax,[Self+0x12C] ; push nParam1 = X
    ///   0x772E62  8B 83 30 01 00 00  mov  eax,[Self+0x130] ; push nParam2 = Y
    ///   0x772E69  6A 00              push 0                ; nParam3
    ///   0x772E72  FF 91 90 00 00 00  call [vmt+0x90]       ; GetShowName -> sMsg
    ///   0x772E7C  6A 01              push 1                ; boFlag: include Self
    ///   0x772E80  8A 8B 54 01 00 00  mov  cl,[Self+0x154]  ; wParam = direction
    ///   0x772E86  66 BA 11 27        mov  dx,0x2711        ; RM_TURN (10001)
    ///   0x772E8E  FF 93 D8 00 00 00  call [vmt+0xD8]       ; SendRefMsg
    ///
    /// The tail from 0x772E5B on is instruction-for-instruction the same as
    /// sub_7742C0's 0x7742EC..0x77431F, so it maps to the same C# call. The
    /// clear at 0x76B4D0 is a two-instruction thunk —
    /// `55 8B EC / E8 E8 7C 00 00 call 0x7731C0 / 5D C3` — over the very
    /// RemoveTimedAbilityInternal routine MOVE-11 already aligned term by term.
    ///
    /// The one shape sub_7742C0 does not have is the 0x772E52 re-check: after
    /// dropping 0x3C the actor is only redrawn when it is no longer entitled to
    /// pass through cells, i.e. sub_772EB8 = m_boObMode || InBodyState(0x3C),
    /// which C# already carries verbatim as HasNativeCellPassThroughGrant
    /// (TBaseObject.cs, MOVE-33).
    ///
    /// State 0x3C is "hidden": its only native setter sub_772DD0 broadcasts
    /// ident 0x1E through [vmt+0xE0] before adding the timed node
    /// (0x772DF2..0x772E18), and holders are exempt from cell blocking
    /// (0x765DC2 consults sub_772EB8). Nothing in this port sets 0x3C today —
    /// the sole native setter is reachable only from the engine-API export
    /// sub_78B35C (`0x78B376 mov edx,0x1E / 0x78B37D call 0x772DD0`) — so the
    /// break is a guarded no-op here, but it is on the byte-faithful path and
    /// becomes live the moment a setter is ported.
    /// </summary>
    public partial class TBaseObject
    {
        private const byte NativeHideState = 0x3C;

        /// <summary>
        /// 0x6F2D58 `81 FE 0B 01 00 00 cmp esi,0x10B`. Same literal as
        /// NativeSkill267ColdTimeKey; kept separate because here it is an
        /// incoming CM_SPELL Series value, not a cold-time slot.
        /// </summary>
        private const int NativeRevealExemptIdent = 0x10B;

        /// <summary>Native sub_772E24.</summary>
        internal bool BreakNativeHideOnAction()
        {
            // 0x772E3A..0x772E45
            if (!HasNativeActiveState(NativeHideState))
            {
                return false;
            }

            // 0x772E47 / 0x772E4B -> 0x76B4D0 -> 0x7731C0
            RemoveTimedAbilityInternal(NativeHideState);

            // 0x772E52 / 0x772E57
            if (HasNativeCellPassThroughGrant())
            {
                return true;
            }

            // 0x772E5B..0x772E8E, identical tuple to sub_7742C0's tail.
            SendRefMsg(Grobal2.RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, 0,
                GetShowName());
            return true;
        }

        /// <summary>
        /// Native sub_6F2D48. <paramref name="nIdent"/> is the value the
        /// dispatcher hands in edx: literal 0 from both hit arms, Series
        /// (= magic id) from CM_SPELL.
        /// </summary>
        internal void NotifyNativeActionReveal(int nIdent)
        {
            // 0x6F2D53 — unconditional, ahead of the exemption test.
            BreakNativeHideOnAction();

            // 0x6F2D58 / 0x6F2D5E
            if (nIdent == NativeRevealExemptIdent)
            {
                return;
            }

            // 0x6F2D62
            BreakNativeStealthOnAction();
        }
    }
}
