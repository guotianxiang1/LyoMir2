using SystemModule;

namespace GameSvr
{
    // =====================================================================
    // 彩色文字 (ColorSay) CONSUMER -- sub_6C9354, the say-path half.
    //
    // The granter (GrantNativeColorSay, sub_786800) and the persistence of the
    // tier byte (rec 0xD5 <-> obj+0xB86) live in TPlayObject.NativeTimedExpBuff.cs
    // and TPlayObject.NativeUnmappedScalars.cs. This file is the part that makes
    // the feature observable: while the countdown obj+0xBD4 is running, a player's
    // ordinary public speech goes out with a tier colour AND under a different
    // client ident.
    //
    // The whole consumer is sub_6C9354, reached from exactly one caller (0x6BB907,
    // the chat dispatcher). Its shape:
    //
    //   6C93FF  cmp dword [esi+0xBD4],0
    //   6C9406  jne 0x6C9442                 ; countdown running -> coloured route
    //   6C9408..6C9440                       ; plain route
    //   6C9442  mov al,byte [esi+0xB86]      ; the tier
    //   6C9448  cmp al,1 / 6C944C mov ax,0xFFF5
    //   6C9454  cmp al,2 / 6C9456 mov ax,0xFFFA
    //   6C945C  mov ax,0xFF01                ; EVERY other tier, including 0
    //   6C9460  test bl,bl
    //   6C9462  je 0x6C9480                  ; bl==0 -> direct send, ident 0x69
    //   6C9471  mov cx,0x2731 ... call sub_765E68   ; bl==1 -> wide send
    //   6C9485  mov cx,0x69   ... call sub_769AB4   ; direct send
    //
    // The two routes carry the SAME colour word; they differ only in delivery
    // (queued wide broadcast vs direct). What matters for equivalence here is the
    // colour word and the ident, both of which are shared.
    //
    // ---------------------------------------------------------------------
    // FOUR non-obvious facts, each of which a naive port gets wrong:
    //
    // 1. THE IDENT CHANGES, 40 -> 105. The plain route sends ident 0x28 (=40,
    //    SM_HEAR) at 0x6C9432; the coloured route sends 0x69 (=105) at 0x6C9485.
    //    0x69 occurs in exactly two places image-wide (`mov cx,0x69` @0x6C9485 and
    //    `mov dx,0x69` @0x6B4B51, the RM 10033 handler), so it really is a
    //    dedicated "coloured hear" opcode and not a reused constant.
    //
    // 2. COLOURED SAY BYPASSES BLOCK-PUBLIC-CHAT, and it is provable twice over.
    //    Ident 40 is filtered by obj+0xB9C bit 1 on BOTH routes -- at the RM
    //    handler (0x6B4A63 `test byte [eax+0xB9C],2 / jne`) and again in the
    //    per-recipient filter sub_6DC068 (0x6DC092, reached by `sub dx,0x28 / je`).
    //    Ident 105 is filtered by NEITHER: 0x6B4B3C has no such gate, and
    //    sub_6DC068's ident ladder only recognises {0x28, 0x66, 0x68} = {40, 102,
    //    104} (0x6DC07E `sub dx,0x28`, 0x6DC084 `sub dx,0x3E`, 0x6DC08A
    //    `sub dx,2`), so 105 falls straight through to the "deliver" exit.
    //    So a listener who has muted public chat still hears coloured speech.
    //
    // 3. THERE IS NO TIER-0 GUARD. The tier select at 0x6C9442 tests 1 then 2 and
    //    sends every other value -- including 0, and including the out-of-range
    //    values the granter's byte wraparound can produce -- down the 0xFF01 leg.
    //    Because the block is only entered when the countdown is non-zero, a
    //    player with a live countdown and a zero tier speaks in colour 0xFF01.
    //
    // 4. THE COLOUR WORD IS AN OPAQUE 16-BIT TOKEN on the server. Traced through
    //    all five hops (0x769B39 -> 0x7652FA -> 0x765E99 -> 0x6B4B3C -> 0x6D7C75)
    //    every move is whole-word; there is no `and ax,0xFF`, no `shr ax,8` and no
    //    byte-granular store anywhere on the path. The client does the
    //    FColor/BColor split. C# reaches the wire through nParam1/nParam2 byte
    //    pairs, so this file splits the token at the LAST moment and asserts the
    //    round-trip, rather than pretending the server understands the halves.
    // =====================================================================
    public partial class TPlayObject
    {
        /// <summary>
        /// 0x6C9429 <c>push 0xFF00</c> -- the colour token ordinary speech carries.
        /// C#'s own default is <c>btHearMsgFColor=0x00</c> /
        /// <c>btHearMsgBColor=0xFF</c>, which packs to exactly this, so the plain
        /// route needed no change.
        /// </summary>
        internal const ushort NativeSayColorPlain = 0xFF00;

        /// <summary>0x6C944C <c>mov ax,0xFFF5</c> -- tier 1.</summary>
        internal const ushort NativeSayColorTier1 = 0xFFF5;

        /// <summary>0x6C9456 <c>mov ax,0xFFFA</c> -- tier 2.</summary>
        internal const ushort NativeSayColorTier2 = 0xFFFA;

        /// <summary>
        /// 0x6C945C <c>mov ax,0xFF01</c> -- reached for EVERY tier that is neither
        /// 1 nor 2, tier 0 included. Not a "no colour" value.
        /// </summary>
        internal const ushort NativeSayColorTierOther = 0xFF01;

        /// <summary>
        /// The tier select at 0x6C9442..0x6C945C, verbatim: compare against 1,
        /// then 2, then fall through. No range check, no tier-0 case.
        /// </summary>
        internal static ushort NativeColorSayColorForTier(byte tier) => tier switch
        {
            1 => NativeSayColorTier1,
            2 => NativeSayColorTier2,
            _ => NativeSayColorTierOther,
        };

        /// <summary>
        /// The gate at 0x6C93FF <c>cmp dword [esi+0xBD4],0</c> / 0x6C9406
        /// <c>jne</c>. Note it tests the COUNTDOWN, not the tier -- a player whose
        /// tier byte survives from an earlier scroll (the byte is never cleared by
        /// anything, image-wide) speaks plainly once the countdown reaches zero.
        /// </summary>
        internal bool NativeColorSayIsActive() => m_nNativeThirdBuffSeconds != 0;

        /// <summary>
        /// The colour token this player's public speech carries right now.
        /// </summary>
        internal ushort NativeColorSayCurrentColor() =>
            NativeColorSayIsActive()
                ? NativeColorSayColorForTier(m_btNativeColorSayTier)
                : NativeSayColorPlain;
    }
}
