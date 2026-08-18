using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// DURA-44: native item instance <c>+0x104</c> special-class bitmap used by
    /// <c>sub_75FF7C</c> @0x75FF7C (<c>bt [eax+0x104], dl</c>) and rebuilt each
    /// <c>RecalcAbilitys</c> pass. <c>sub_75EE04</c> first clears the word at
    /// <c>+0x102</c> @0x75EE12 and byte <c>+0x104</c> @0x75EE20, then rebuilds
    /// the bitmap via <c>sub_75FE20</c> / <c>sub_76203C</c>.
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

        // Only these extension idents write +0x104. The 0x3E/0x50 constants in
        // sub_76203C belong to Shape branches, not extension-ident branches.
        private const ushort IdentRebirthBit2 = 0x45;
        private const ushort IdentFeSubtype = 0xFE;
        private const byte FeValueSetsBit1 = 2;

        /// <summary>
        /// <c>sub_73EBF0</c> predicate on the runtime item byte. The caller owns
        /// the non-null and positive-durability gates.
        /// </summary>
        internal static bool MatchesReviveDurabilityTarget(TUserItem item, int mode)
        {
            if (item == null)
            {
                return false;
            }

            var bits = item.NativeClass104;
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
            var className = NativeItemFactory.GetClassName(item);
            if ((className == "TRing" && item.Shape == 114) ||
                (className == "TArmRing" && item.Shape == 114))
            {
                bits |= MaskBit0;
            }
            if ((className == "TRWeapon" && item.Shape == 201) ||
                (className == "TRing" && item.Shape == 137) ||
                (className == "TArmRing" && item.Shape == 210) ||
                (NativeItemFactory.IsClassOrDescendantOf(className, "TClothes") &&
                 item.Shape is >= 39 and <= 41 && item.Mac == 1))
            {
                bits |= MaskBit1;
            }

            var idents = item.NativeItemExtAbilIdents;
            var values = item.NativeItemExtAbilValues;
            var count = idents == null || values == null
                ? 0
                : System.Math.Min(6, System.Math.Min(idents.Length, values.Length));
            for (var i = 0; i < count; i++)
            {
                var ident = idents[i];
                // sub_75FE20 accepts 1..158 and the special ident 0xFE.
                // Any other value, including an empty slot, terminates the
                // fixed six-slot scan instead of skipping to later slots.
                if (!((ident >= 1 && ident <= 158) || ident == IdentFeSubtype))
                {
                    break;
                }

                var value = values[i];
                switch (ident)
                {
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

        internal static void RefreshEquippedInstance(TUserItem item, GoodItem stdItem)
        {
            if (item == null)
            {
                return;
            }

            // 0x75EE12 clears word [item+0x102], then 0x75EE20 clears +0x104.
            // Keep the stores before computation so a failed rebuild leaves the
            // same cleared transient state as the native routine.
            item.NativeItemPlus102 = 0;
            item.NativeItemPlus103 = 0;
            item.NativeClass104 = 0;
            item.NativeClass104 = ComputeClass104Bits(stdItem);
        }
    }
}
