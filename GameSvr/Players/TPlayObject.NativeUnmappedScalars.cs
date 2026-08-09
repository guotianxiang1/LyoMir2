using System.Buffers.Binary;
using DBSvr.Core;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ------------------------------------------------------------------
        // Scalar save-record slots that sub_6B0FF0 writes but the shared
        // DTO codec (DBSvr/Core/NativeHumanDataCodec.cs, owned elsewhere)
        // neither encodes nor decodes.  Because TryEncode starts from
        // info.NativeData.Clone() and only overwrites the offsets it models,
        // patching the blob here is carried to the wire verbatim -- the same
        // mechanism TPlayObject.NativeCommonInformation.cs already uses for
        // rec+0x60C/0x610.
        //
        // Every offset below is re-derived from the encoder AND independently
        // confirmed by the decoder sub_6AFD7C (opposite direction) and by the
        // published RTTI direct-field table.
        //
        //  rec+0x0C8 <- obj+0x160  MyPKpoint   (Integer) enc 0x6B116B dec 0x6AFEFC
        //  rec+0x0CC <- obj+0x164  LuckNum     (Integer) enc 0x6B1177 dec 0x6AFF0E
        //  rec+0x0D0 <- obj+0xAED  MyAttackMode(Byte)    enc 0x6B1183 dec 0x6AFFEA
        //  rec+0x0D4 <- obj+0x67E  FightZoneDieCount(By) enc 0x6B11AA dec 0x6B00E0
        //  rec+0x16E <- obj+0xB85  PlatLv      (Byte)    enc 0x6B1388 dec 0x6B0577
        //  rec+0x5E8 <- obj+0xAF0  JiaYouPoint (Cardinal)enc 0x6B12B2 dec 0x6AFF34
        //  rec+0x0D7 <- obj+0xBA4  AllowGroupReCall(By)  enc 0x6B11DA dec 0x6B0104
        //  rec+0x0D5 <- obj+0xB86  ColorSayTier(Byte)    enc 0x6B137C dec 0x6B0495
        //
        // On rec+0x0D7 specifically -- this is the 天地合一 (group-recall) toggle,
        // NOT btAllowGroup. The name is settled by the GM handler inside
        // sub_622820, which flips it and reports the new state:
        //   0x623993  xor byte [eax+0xBA4],1     ; domain is exactly {0,1}
        //   0x62399D  cmp byte [eax+0xBA4],0
        //   0x6239A6  mov cx,0xFFDB              ; FColor=0xDB Green, BColor=0xFF
        // and by its consumer sub_7274B4, which refuses the skill with the
        // literal 天地合一 message when the toggle is clear:
        //   0x72750C  cmp byte [ebx+0xBA4],0 / je -> reject path at 0x72753A
        //   0x7275A4  '无法对 '   0x7275B4  ' 使用天地合一'
        // The adjacent byte rec+0x0DE <- obj+0xBA5 is a DIFFERENT flag (enc
        // 0x6B1234 / dec 0x6B01B8) and IS already handled by the shared codec,
        // so do not conflate the two -- an earlier 0xDE->0xD7 change here was
        // wrong and was reverted.
        //
        // On rec+0x0D5 (obj+0xB86) -- the 彩色文字 (colour-say) TIER byte, the
        // companion of the obj+0xBD4 countdown that
        // TPlayObject.NativeTimedExpBuff.cs already carries. Whole-image census
        // of obj+0xB86 (6 raw disp32 hits, 4 real instruction references):
        //   0x6B0495  mov byte [edx+0xB86],al   <- LOAD, rec 0xD5, unvalidated
        //   0x6B1376  mov al,byte [ebx+0xB86]   -> SAVE, rec 0xD5
        //   0x6C9442  mov al,byte [esi+0xB86]   -- the say-path colour select
        //   0x786845  mov byte [esi+0xB86],al   <- granter sub_786800
        // There is NO clamp and NO clear anywhere: the tier byte is STICKY and
        // survives the countdown reaching zero (the tick sub_6CCBC4 block at
        // 0x6CCC91..0x6CCCAF touches obj+0xBD4 only). A port must not "tidy up"
        // by clearing it on expiry.
        //
        // The SAVE asymmetry matters: the obj+0xBD4 -> rec 0x120 store two
        // instructions earlier IS gated (0x6B135E jle 0x6B1376 skips it when the
        // countdown is <= 0), but the colour store at 0x6B1376/0x6B137C sits at
        // the jump TARGET, so it runs on every save regardless.
        // ------------------------------------------------------------------
        internal const int NativePkPointOffset = 0x00C8;
        internal const int NativeLuckNumOffset = 0x00CC;
        internal const int NativeAttackModeOffset = 0x00D0;
        internal const int NativeFightZoneDieCountOffset = 0x00D4;
        internal const int NativeAllowGroupReCallOffset = 0x00D7;
        internal const int NativeColorSayTierOffset = 0x00D5;
        internal const int NativePlatLvOffset = 0x016E;

        /// <summary>
        /// <c>rec+0x050C</c> &lt;- <c>obj+0x18A0</c>, WORD. The 元宝 trade-protection
        /// amount. Named unambiguously by two independent in-function AnsiString
        /// references: <c>0x6D1581 -&gt; 0x6D1718</c> 「已成功设置交易保护金额为：」
        /// and <c>0x6D15A9 -&gt; 0x6D173C</c> 「修改元宝交易金额」, with the setter
        /// <c>0x6D154E mov word [esi+0x18A0],dx</c>. Save is UNCONDITIONAL
        /// (enc <c>0x6B12B8</c>/<c>0x6B12BF</c>, dec <c>0x6B06D0</c>).
        /// </summary>
        internal const int NativeTradeProtectAmountOffset = 0x050C;

        /// <summary>
        /// <c>rec+0x0534</c> &lt;- <c>obj+0x18A4</c>, WORD. The accumulator paired
        /// with <see cref="NativeTradeProtectAmountOffset"/> — the two are written
        /// back-to-back from adjacent object offsets (enc <c>0x6B12C6</c>/
        /// <c>0x6B12CD</c>, dec <c>0x6B06BC</c>).
        /// <para>
        /// Mechanism PROVEN, name UNPROVEN. Two independent sites implement the
        /// same capped accumulator, and both share one deliberately odd rule:
        /// <c>0x633D7C cmp word [esi+0x18A4],0x1F4 / 0x633D85 jbe / 0x633D87 mov
        /// word [esi+0x18A4],0</c> — exceeding the 500 cap RESETS TO ZERO rather
        /// than clamping. The twin at <c>0x6F1652..0x6F165D</c> is byte-identical
        /// in shape. A gate reader at <c>0x6F0B0C cmp eax,0x1F4 / jle</c> tests the
        /// prospective sum against the same cap before proceeding.
        /// </para>
        /// <para>
        /// Do NOT "fix" the overflow-to-zero into a clamp: it is native, and a
        /// clamp would let a capped account keep accumulating.
        /// </para>
        /// </summary>
        internal const int NativeYuanBaoTradeAccumOffset = 0x0534;

        /// <summary>
        /// <c>rec+0x0537</c> &lt;- <c>obj+0x578</c>, BYTE, unsigned 0..255. The
        /// 伤害分担 ("damage sharing") bonus, settable by GM dispatch index 359
        /// (<c>@ChgDmgShare</c>, handler <c>0x628008</c>, which stores with
        /// <c>0x628036 mov byte [esi+0x578],bl</c> after rejecting negatives at
        /// <c>0x628030 jl</c>, then recalcs through <c>vmt+0x8C</c>).
        /// <para>
        /// Identity proven by two real in-function AnsiString references in that
        /// case body: <c>0x62D13C</c> 「伤害分担」 and <c>0x62D150</c>
        /// 「已经成功修改伤害分担加成为：」. The consumer at <c>0x73DEAF xor
        /// eax,eax / mov al,[esi+0x578] / add word [esi+0x2DC],ax</c> explicitly
        /// zero-extends the byte before accumulating, confirming it is unsigned.
        /// </para>
        /// <para>
        /// ⚠️ <c>obj+0x578</c> also has DWORD accesses (<c>0x6757EC</c>,
        /// <c>0x675802</c>) that store a tick value — those belong to a DIFFERENT
        /// class reusing the same displacement. Both codec accesses are byte-sized,
        /// so BYTE is the correct width here.
        /// </para>
        /// </summary>
        internal const int NativeDamageShareOffset = 0x0537;

        internal const int NativeJiaYouPointOffset = 0x05E8;

        /// <summary>
        /// Highest scalar offset this module touches; the record must be at
        /// least this long +4 for a patch to be possible.
        /// </summary>
        private const int NativeUnmappedScalarsMinimumLength =
            NativeJiaYouPointOffset + sizeof(uint);

        /// <summary>
        /// Load direction. The shared codec never decodes these slots, so the
        /// DTO members arrive as 0 and GetHumData would zero the live field on
        /// every login. Read them straight out of the native record instead.
        /// </summary>
        internal void RestoreNativeUnmappedScalars()
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < NativeUnmappedScalarsMinimumLength)
                return;

            m_nPkPoint = BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(NativePkPointOffset, sizeof(int)));
            m_nBodyLuckLevel = BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(NativeLuckNumOffset, sizeof(int)));
            // sub_6AFD7C copies this byte RAW (0x6AFFE1 mov al,[eax+0xD0] ->
            // 0x6AFFEA mov [edx+0xAED],al) with no clamp; the range guard lives
            // only in the setter sub_6F2D10 (0x6F2D19 sub al,6 / jae reject) and
            // in the GM cycle handler (0x6239FD cmp [eax+0xAED],5). Reproduce the
            // raw load so an out-of-range record behaves as it does natively.
            m_btAttatckMode = raw[NativeAttackModeOffset];
            // Native stores this as a BYTE and gates on ">= 3" at 0x6B9ACF
            // (cmp byte [ebx+0x67E],3 / jae) inside sub_6B9A2C, i.e. after the
            // decode call at 0x6B9A62 -- so the loaded value is meaningful.
            m_nFightZoneDieCount = raw[NativeFightZoneDieCountOffset];
            m_btPlatLv = raw[NativePlatLvOffset];
            // 0x6B0495 stores the byte raw. Neither the decoder nor the say-path
            // consumer validates it -- 0x6C9442 compares against 1 then 2 and
            // treats every other value (including out-of-range ones) as the
            // third colour, so a raw load is the faithful behaviour.
            m_btNativeColorSayTier = raw[NativeColorSayTierOffset];
            // 0x6B0104 stores the byte raw, and the only writer (the GM xor at
            // 0x623993) keeps it in {0,1}, so any non-zero record byte means the
            // toggle was on.
            m_boAllowGroupReCall = raw[NativeAllowGroupReCallOffset] != 0;
            // dec 0x6B06D0 -> obj+0x18A0 and 0x6B06BC -> obj+0x18A4: two word
            // round-trips, both raw. The
            // 0x1F4 cap lives only in the accumulate paths (0x633D7C / 0x6F1652)
            // and in the pre-check at 0x6F0B0C, never in the codec, so an
            // over-cap record value loads unchanged.
            m_nNativeTradeProtectAmount = BinaryPrimitives.ReadUInt16LittleEndian(
                raw.AsSpan(NativeTradeProtectAmountOffset, sizeof(ushort)));
            m_nNativeYuanBaoTradeAccum = BinaryPrimitives.ReadUInt16LittleEndian(
                raw.AsSpan(NativeYuanBaoTradeAccumOffset, sizeof(ushort)));
            // 0x6B07ED mov al,[eax+0x537] -> 0x6B07F6 mov [edx+0x578],al.
            // Plain byte, no clamp or default on either side.
            m_btNativeDamageShare = raw[NativeDamageShareOffset];
            m_dwJiaYouPoint = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(NativeJiaYouPointOffset, sizeof(uint)));
        }

        /// <summary>
        /// Save direction. Native rebuilds the whole frame from the live object
        /// (sub_6B6510 zero-fills 0xF0FC via FillChar sub_403B2C at 0x6B65FE,
        /// then sub_6B0FF0 writes every slot), so the current in-RAM value -- not
        /// the stale loaded byte -- is what must reach the record.
        /// </summary>
        internal bool PersistNativeUnmappedScalars()
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < NativeUnmappedScalarsMinimumLength)
                return m_nPkPoint == 0 && m_nBodyLuckLevel == 0
                       && m_btAttatckMode == 0 && m_nFightZoneDieCount == 0
                       && m_btPlatLv == 0 && !m_boAllowGroupReCall
                       && m_btNativeColorSayTier == 0
                       && m_nNativeTradeProtectAmount == 0
                       && m_nNativeYuanBaoTradeAccum == 0
                       && m_btNativeDamageShare == 0
                       && m_dwJiaYouPoint == 0;

            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativePkPointOffset, sizeof(int)), m_nPkPoint);
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativeLuckNumOffset, sizeof(int)), m_nBodyLuckLevel);
            raw[NativeAttackModeOffset] = m_btAttatckMode;
            // native width is one byte (enc 0x6B11AA mov byte ptr [esi+0xD4],al)
            // while the C# runtime field is an int; truncate exactly as Delphi does.
            raw[NativeFightZoneDieCountOffset] =
                unchecked((byte)m_nFightZoneDieCount);
            raw[NativePlatLvOffset] = m_btPlatLv;
            // enc 0x6B137C is UNCONDITIONAL -- it is the target of the jle at
            // 0x6B135E that skips the obj+0xBD4 -> rec 0x120 countdown store, so
            // the tier is written even when the countdown has already expired.
            // Do NOT gate this on m_nNativeThirdBuffSeconds > 0.
            raw[NativeColorSayTierOffset] = m_btNativeColorSayTier;
            // enc 0x6B11DA copies obj+0xBA4 as a plain byte; the native domain is
            // {0,1} because 0x623993 only ever xors bit 0.
            raw[NativeAllowGroupReCallOffset] =
                (byte)(m_boAllowGroupReCall ? 1 : 0);
            // enc 0x6B12B8/0x6B12BF and 0x6B12C6/0x6B12CD -- two adjacent word
            // stores from obj+0x18A0 and obj+0x18A4, both unconditional, sitting
            // immediately after the 0x5E8 (JiaYouPoint) store at 0x6B12B2.
            BinaryPrimitives.WriteUInt16LittleEndian(
                raw.AsSpan(NativeTradeProtectAmountOffset, sizeof(ushort)),
                m_nNativeTradeProtectAmount);
            BinaryPrimitives.WriteUInt16LittleEndian(
                raw.AsSpan(NativeYuanBaoTradeAccumOffset, sizeof(ushort)),
                m_nNativeYuanBaoTradeAccum);
            // enc 0x6B12F3, unconditional (CFG-proven on every entry->ret path).
            raw[NativeDamageShareOffset] = m_btNativeDamageShare;
            BinaryPrimitives.WriteUInt32LittleEndian(
                raw.AsSpan(NativeJiaYouPointOffset, sizeof(uint)),
                m_dwJiaYouPoint);
            return true;
        }
    }
}
