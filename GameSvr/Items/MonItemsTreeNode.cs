namespace GameSvr
{
    /// <summary>
    /// Native MonItemsTree.txt exclusive drop chain node.
    /// Node layout (0x24 bytes) reverse-engineered from sub_67AEC0 (loader) and sub_71FA20 (traversal).
    /// </summary>
    internal sealed class MonItemsTreeNode
    {
        /// <summary>
        /// [node+0x00..0x0F] Item name (ShortString[15], 16 bytes total).
        /// EA 0x67b1a0-0x67b1a4: mov eax, ebx; mov cl, 0xf; call 0x4039e4 (ShortString assign).
        /// </summary>
        public string ItemName { get; set; }

        /// <summary>
        /// [node+0x10] Cumulative weight (dword) — the running sum of item bands within
        /// the owning group.  EA 0x67b1f1: <c>mov dword ptr [ebx + 0x10], eax</c> stores
        /// <c>[G+8]</c> after it has been bumped by <c>ROUND(A/A*10000) = 10000</c> for
        /// this item.  The selector <c>sub_67B2B0</c> reads it at 0x67B309
        /// (<c>cmp esi,[node+0x10] / jge next</c>): the first node whose cumulative weight
        /// exceeds <c>Random([G+8])</c> is chosen, so equal 10000-wide bands make the pick
        /// uniform.  Used by selection, not by the item-vs-gold traversal.
        /// </summary>
        public int CumulativeWeight { get; set; }

        /// <summary>
        /// [node+0x18] StdItem template pointer, null when the name did not resolve.
        /// EA 0x67b20e: mov dword ptr [ebx + 0x18], eax (result of call 0x74c2d4, a plain
        /// hash lookup on the name table at [UserEngine+0x20] with no index exclusion).
        /// A GOLD row is NOT a null pointer — the traversal at 0x71fb5e-0x71fb6e requires
        /// the pointer to be non-null AND word_at(template) == 0, and that first word is
        /// the record's wire index, so a gold row is one that resolved to the index-0
        /// 金币 sentinel.  A null pointer takes the item arm and quietly does nothing.
        /// </summary>
        public GoodItem StdItem { get; set; }

        /// <summary>
        /// [node+0x1C] Repeat count (dword).
        /// EA 0x67b1fc: mov dword ptr [ebx + 0x1c], eax (StrToInt result).
        /// EA 0x71fb95: mov ebx, dword ptr [eax + 0x1c] (used as loop counter).
        /// For gold entries: represents gold amount (with calculation at 0x71fb73-0x71fb8a).
        /// </summary>
        public int RepeatCount { get; set; }

        /// <summary>
        /// [node+0x20] Next node pointer.
        /// EA 0x67b21a: mov dword ptr [eax + 0x20], ebx (link nodes).
        /// EA 0x71fcdc: mov eax, dword ptr [eax + 0x20] (traverse chain).
        /// </summary>
        public MonItemsTreeNode Next { get; set; }
    }
}
