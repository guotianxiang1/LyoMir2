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
        /// [node+0x10] Cumulative weight (dword).
        /// EA 0x67b1f1: mov dword ptr [ebx + 0x10], eax.
        /// Used during chain construction, not traversal.
        /// </summary>
        public int CumulativeWeight { get; set; }

        /// <summary>
        /// [node+0x18] Item pointer from StdItem lookup (null for gold entries).
        /// EA 0x67b20e: mov dword ptr [ebx + 0x18], eax (result of call 0x74c2d4 lookup).
        /// EA 0x71fb5e-0x71fb6e: check node+0x18 != null AND word_at(item) == 0 for gold.
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
