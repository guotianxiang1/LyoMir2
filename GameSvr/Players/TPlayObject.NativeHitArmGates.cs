using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// HIT-ARM — the prologue ladder of the two native hit arms, in the order
    /// the bytes run it.
    ///
    /// The CM dispatcher sub_6D7D68 has two hit arms, not one, and C# merged
    /// them into a single <c>case</c> block. They are near-identical, but the
    /// three places where they differ are all observable, so the merged block
    /// cannot be faithful to both with one linear insertion. This helper keeps
    /// the merged block and runs the right ladder for the ident.
    ///
    /// == CASE1 @0x6D9EAF ==
    ///   0x6D9EAF  33 D2              xor  edx,edx           ; Ident = 0
    ///   0x6D9EB1  8B 45 FC           mov  eax,[ebp-4]
    ///   0x6D9EB4  E8 8F 8E 01 00     call 0x6F2D48          ; reveal hook
    ///   0x6D9EB9  8B 45 FC           mov  eax,[ebp-4]
    ///   0x6D9EBC  E8 F7 1F FE FF     call 0x6BBEB8          ; mount gate
    ///   0x6D9EC1  84 C0              test al,al
    ///   0x6D9EC3  0F 85 63 1D 00 00  jne  0x6DBC2C          ; SILENT drop
    ///   0x6D9EC9  8B 45 CC           mov  eax,[ebp-0x34]
    ///   0x6D9ECC  0F B7 50 04        movzx edx,word [msg+4] ; dead arg
    ///   0x6D9ED0  8B 45 FC           mov  eax,[ebp-4]
    ///   0x6D9ED3  E8 54 2F FE FF     call 0x6BCE2C          ; cancel channels
    ///   0x6D9ED8  B2 01              mov  dl,1
    ///   0x6D9EDA  8B 45 FC           mov  eax,[ebp-4]
    ///   0x6D9EDD  8B 08              mov  ecx,[eax]
    ///   0x6D9EDF  FF 51 40           call [ecx+0x40]        ; can-act gate
    ///   0x6D9EE2  84 C0              test al,al
    ///   0x6D9EE4  74 29              je   0x6D9F0F          ; -> 0x276 refusal
    ///   0x6D9EE6..0x6D9F06                                  ; ClientHitXY
    ///
    /// == CASE2 @0x6D9F4B ==
    ///   0x6D9F4B  33 D2              xor  edx,edx           ; Ident = 0
    ///   0x6D9F50  E8 F3 8D 01 00     call 0x6F2D48          ; reveal hook
    ///   0x6D9F58  E8 5B 1F FE FF     call 0x6BBEB8          ; mount gate
    ///   0x6D9F5D  84 C0              test al,al
    ///   0x6D9F5F  0F 85 82 00 00 00  jne  0x6D9FE7          ; -> 0x276 refusal
    ///   0x6D9F65  B2 01              mov  dl,1
    ///   0x6D9F6C  FF 51 40           call [ecx+0x40]        ; can-act gate
    ///   0x6D9F6F  84 C0              test al,al
    ///   0x6D9F71  74 74              je   0x6D9FE7          ; -> 0x276 refusal
    ///   0x6D9F73  8B 45 CC           mov  eax,[ebp-0x34]
    ///   0x6D9F76  0F B7 50 04        movzx edx,word [msg+4] ; dead arg
    ///   0x6D9F7A  8B 45 FC           mov  eax,[ebp-4]
    ///   0x6D9F7D  E8 AA 2E FE FF     call 0x6BCE2C          ; cancel channels
    ///   0x6D9F82..0x6D9FA2                                  ; ClientHitXY
    ///
    /// Three differences, all real:
    ///   1. sub_6BCE2C sits BEFORE the can-act gate in CASE1 (0x6D9ED3 &lt;
    ///      0x6D9EDF) and AFTER it in CASE2 (0x6D9F7D &gt; 0x6D9F6C). So a
    ///      CASE1 hit that the gate refuses has still cancelled the pending
    ///      channels; a CASE2 hit that the gate refuses has not.
    ///   2. The mount gate drops CASE1 silently onto the common exit 0x6DBC2C
    ///      but sends CASE2 to 0x6D9FE7, a `mov dx,0x276` refusal block.
    ///   3. sub_6EC078's action selector is [msg+4] (Ident) in CASE1
    ///      (0x6D9EFF) and [msg+8] (Tag) in CASE2 (0x6D9F9B) — already carried
    ///      by the existing nParam3 mapping in Message.cs / UsrEngn.cs.
    ///
    /// == which ident takes which arm ==
    /// The dispatcher is a balanced comparison tree, so every ident reaches
    /// exactly one leaf. CASE2 has exactly one entry:
    ///   0x6D8502  3D D3 0B 00 00     cmp eax,0xBD3          ; 3027
    ///   0x6D8507  0F 8F C9 00 00 00  jg  0x6D85D6
    ///   0x6D850D  0F 84 38 1A 00 00  je  0x6D9F4B           ; CM_3037 -> CASE2
    /// CASE1 is reached from eleven places — three direct compares and the
    /// eight jump-table slots of the ident-3010-based table at 0x6D8592:
    ///   0x6D851A  0F 84 8F 19 00 00  je 0x6D9EAF   after `cmp eax,0xBBA`  3002
    ///   0x6D85A2/0x6D85A6/0x6D85AA  slots 4/5/6    3014 / 3015 / 3016
    ///   0x6D85B2/0x6D85B6           slots 8/9      3018 / 3019
    ///   0x6D85CA/0x6D85CE/0x6D85D2  slots 14/15/16 3024 / 3025 / 3026
    ///   0x6D85F5  0F 84 B4 18 00 00  je 0x6D9EAF   after `sub eax,0xBD4`  3028
    ///   0x6D8610  0F 84 99 18 00 00  je 0x6D9EAF   after the running
    ///             `sub eax,0xBD4 / sub eax,2 / sub eax,2 / sub eax,3`     3035
    /// 3035 = Grobal2.CM_HORSERUN in this port, which routes it to
    /// ClientHorseRunXY instead and therefore never reaches here. ID3035
    /// re-derived the whole tree by emulation (tools/id3035_dispatch_map.py)
    /// and confirmed the leaf: 3035 is an attack ident and the mount run is
    /// CM_RUN3 (4108). Rewiring it is a TPlayObject.Message.cs edit, so it is
    /// still pending — see docs/ident3035_arm_conflict_20260814.md. Note that
    /// RunNativeHitArmGates already classifies 3035 correctly once it does
    /// arrive: boCase2 only fires for CM_3037, so 3035 takes the CASE1 ladder.
    ///
    /// == the can-act gate ==
    /// `call [ecx+0x40]` with dl = 1. TPlayer VMT 0x6AC8C8+0x40 = 0x6E6700,
    /// which chains the inherited 0x76B354 (THumanKind VMT 0x73BC34+0x40 =
    /// 0x76B354 too) and adds the cast lock:
    ///   0x6E670D  E8 42 4C 08 00        call 0x76B354
    ///   0x6E6714  74 09                 je   0x6E671F          ; -> false
    ///   0x6E6716  83 BE 74 05 00 00 00  cmp  dword [esi+0x574],0
    ///   0x6E671D  74 04                 je   0x6E6723          ; -> true
    /// and 0x76B354 is the six-term ladder
    ///   0x76B35F  call 0x772DA8  (byte [Self+0x74] death)
    ///   0x76B36C  state 0x1D   0x76B379  state 0x01   0x76B386  state 0x1A
    ///   0x76B393  state 0x18 with `84 D8 test al,bl` (only when the caller's
    ///             argument is non-zero — the hit arms pass dl = 1, so it bites)
    ///   0x76B3A0  state 0x3E
    /// It is a cooldown-free status ladder, not a timer: paralysis/stun-class
    /// bodyStates plus death plus the forced-move cast lock. C# already owns
    /// all of it — <c>IsNativeCanActBlocked</c> (TBaseObject.cs, MOVE-14) and
    /// its TPlayObject override (TPlayObject.NativeCanAct.cs, MOVE-15) — with
    /// inverted polarity: native returns "may act", C# returns "is blocked".
    /// It was simply never consulted on the hit path.
    ///
    /// A refusal returns <see cref="NativeHitGateRefuse"/> rather than sending
    /// anything itself. In native both refusal edges land on the same
    /// `push 0 x4 / xor ecx,ecx / mov dx,0x276 / call [vmt+0x250]` block that
    /// a failed sub_6EC078 falls into (0x6D9EE4 and 0x6D9F0D share 0x6D9F0F),
    /// so the caller must route a refusal into the identical branch it already
    /// uses for "ClientHitXY returned false". dwDelayTime is 0 for the whole
    /// switch (TPlayObject.Message.cs:934) unless ClientHitXY writes it, which
    /// is the same short-circuit MOVE-90 relies on for CM_SPELL.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>Gate ladder passed; call ClientHitXY.</summary>
        internal const int NativeHitGateProceed = 0;

        /// <summary>0x6D9EC3 `jne 0x6DBC2C` — consume the message in silence.</summary>
        internal const int NativeHitGateConsume = 1;

        /// <summary>0x6D9F5F / 0x6D9EE4 / 0x6D9F71 — take the 0x276 refusal block.</summary>
        internal const int NativeHitGateRefuse = 2;

        internal int RunNativeHitArmGates(int wIdent)
        {
            // 0x6D8502 / 0x6D850D: CM_3037 is the only ident on CASE2.
            bool boCase2 = wIdent == Grobal2.CM_3037;

            // 0x6D9EAF `33 D2` + 0x6D9EB4 / 0x6D9F4B `33 D2` + 0x6D9F50.
            // Both arms hand in a literal zero, so the 0x10B exemption inside
            // sub_6F2D48 can never fire from here.
            NotifyNativeActionReveal(0);

            // 0x6D9EBC / 0x6D9F58 call 0x6BBEB8 == HasState(0x33) || HasState(0x34).
            if (IsNativeHitBlockedByMountState())
            {
                // 0x6D9EC3 jne 0x6DBC2C vs 0x6D9F5F jne 0x6D9FE7.
                return boCase2 ? NativeHitGateRefuse : NativeHitGateConsume;
            }

            if (!boCase2)
            {
                // 0x6D9ED3, ahead of the gate.
                CancelNativeActionChannels();
            }

            // 0x6D9EDF / 0x6D9F6C `B2 01 mov dl,1` then `FF 51 40 call [ecx+0x40]`.
            if (IsNativeCanActBlocked(1))
            {
                return NativeHitGateRefuse;
            }

            if (boCase2)
            {
                // 0x6D9F7D, behind the gate.
                CancelNativeActionChannels();
            }

            return NativeHitGateProceed;
        }
    }
}
