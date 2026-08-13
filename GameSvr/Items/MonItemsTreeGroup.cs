namespace GameSvr
{
    /// <summary>
    /// Native MonItemsTree.txt exclusive drop <b>group</b> node.
    ///
    /// One text line = one group.  The loader <c>sub_67AEC0</c> allocates a 0x14-byte
    /// (20-byte) record per line (<c>0x67B010 mov eax,0x14 / call SysGetMem</c>) and the
    /// selector <c>sub_67B2B0</c> reads it back field-for-field, so this layout is the
    /// contract both sides share:
    /// <code>
    /// [G+0x00] Rate         ; sub_67B2F6 cmp eax,[edx]      (Random(Nth) &lt; Rate = hit)
    /// [G+0x04] Nth          ; sub_67B2EE Random([G+4])
    /// [G+0x08] TotalWeight  ; sub_67B2E1 Random([G+8]); loader 0x67B1EB add [esi+8],w
    /// [G+0x0C] ItemHead     ; sub_67B2FD mov eax,[G+0xC]    (weighted item chain)
    /// [G+0x10] Next         ; sub_67B343 mov eax,[G+0x10]   (next group for this monster)
    /// </code>
    ///
    /// The monster→group hash is <c>[UserEngine+0xDC]</c>; each monster maps to the head
    /// of a singly-linked group chain, and every group is an <i>independent</i> probability
    /// gate that contributes at most one item to a kill.
    /// </summary>
    internal sealed class MonItemsTreeGroup
    {
        /// <summary>[G+0x00] Rate numerator. Loader 0x67B032 <c>mov [esi],eax</c> = StrToInt(A) of the <c>Rate/Nth</c> field.</summary>
        public int Rate { get; set; }

        /// <summary>[G+0x04] Nth denominator. Loader 0x67B03C <c>mov [esi+4],eax</c> = StrToInt(B). The group fires when <c>Random(Nth) &lt; Rate</c> (0x67B2EE/0x67B2F6).</summary>
        public int Nth { get; set; }

        /// <summary>
        /// [G+0x08] Running total of item weights.
        /// Loader 0x67B1EB <c>add [esi+8],eax</c> accumulates <c>ROUND(A/A*10000)</c> per item;
        /// selector 0x67B2E1 draws <c>Random(TotalWeight)</c> as the weighted-pick roll.
        /// </summary>
        public int TotalWeight { get; set; }

        /// <summary>[G+0x0C] Head of this group's weighted item chain. Loader 0x67B225 <c>mov [esi+0xC],ebx</c>.</summary>
        public MonItemsTreeNode ItemHead { get; set; }

        /// <summary>[G+0x10] Next group for the same monster. Loader inserts after the head (0x67B05F/0x67B065).</summary>
        public MonItemsTreeGroup Next { get; set; }
    }
}
