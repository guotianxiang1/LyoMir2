namespace GameSvr
{
    /// <summary>
    /// DURA-44: native item instance <c>+0x104</c> special-class bitmap used by
    /// <c>sub_75FF7C</c> @0x75FF7C (<c>bt [eax+0x104], dl</c>) and rebuilt each
    /// <c>RecalcAbilitys</c> pass in <c>sub_75EE04</c> @0x75EE20 (zeroed) then
    /// via <c>sub_75FE20</c> / <c>sub_76203C</c>.
    /// </summary>
    internal static class NativeItemClass104
    {
        // sub_75FF7C bit indices exercised by sub_73EBF0 @0x73EBF0:
        //   mode 0 -> dl=0 (bit0) @0x73EC0A
        //   mode 1 -> dl=1 or dl=2 (bit1|bit2) @0x73EC1A..0x73EC32
        internal const byte ReviveEquipBit = 0;   // mask 1 @ or [+0x104],1
        internal const byte RebirthBit1 = 1;      // mask 2 @ or [+0x104],2
        internal const byte RebirthBit2 = 2;       // mask 4 @ or [+0x104],4

        private const byte MaskBit0 = 1;
        private const byte MaskBit1 = 2;
        private const byte MaskBit2 = 4;

        // Template ext-abil idents that OR into +0x104 (flat_image.bin, ImageBase 0x400000):
        //   ident 0x3E (62) -> bit0 @0x76225d / @0x762ae8 (sub_76203C jump idx 5)
        //   ident 0x50 (80) -> bit1 @0x762363 (sub_76203C jump idx 23)
        //   ident 0x45 (69) -> bit2 @0x75FF9C (sub_75FF90 when ident==0x45)
        //   ident 0xFE (254) value byte 2 -> bit1 @0x75FFB3 (sub_75FFA8)
        private const ushort IdentReviveBit0 = 0x3E;
        private const ushort IdentRebirthBit1 = 0x50;
        private const ushort IdentRebirthBit2 = 0x45;
        private const ushort IdentFeSubtype = 0xFE;
        private const byte FeValueSetsBit1 = 2;

        /// <summary>
        /// <c>sub_73EBF0</c> predicate on a template row (non-null item with Dura&gt;0
        /// is checked by the caller). Returns false when <c>NativeItemExtAbilParsed</c>
        /// is false — no Shape/AniCount fallback.
        /// </summary>
        internal static bool MatchesReviveDurabilityTarget(GoodItem item, int mode)
        {
            if (item == null || !item.NativeItemExtAbilParsed)
            {
                return false;
            }

            var bits = ComputeClass104Bits(item);
            if (mode == 0)
            {
                return (bits & MaskBit0) != 0;
            }

            return (bits & (MaskBit1 | MaskBit2)) != 0;
        }

        internal static byte ComputeClass104Bits(GoodItem item)
        {
            if (item == null)
            {
                return 0;
            }

            byte bits = 0;
            for (var i = 0; i < item.NativeItemExtAbilIdents.Length; i++)
            {
                var ident = item.NativeItemExtAbilIdents[i];
                if (ident == 0)
                {
                    continue;
                }

                var value = item.NativeItemExtAbilValues[i];
                switch (ident)
                {
                    case IdentReviveBit0:
                        bits |= MaskBit0;
                        break;
                    case IdentRebirthBit1:
                        bits |= MaskBit1;
                        break;
                    case IdentRebirthBit2:
                        bits |= MaskBit2;
                        break;
                    case IdentFeSubtype:
                        if ((value & 0xFF) == FeValueSetsBit1)
                        {
                            bits |= MaskBit1;
                        }
                        break;
                }
            }

            return bits;
        }
    }
}
