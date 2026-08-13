using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// MonItemsTree.txt exclusive drop loader — a byte-faithful port of <c>sub_67AEC0</c>
    /// (EA 0x67AEC0-0x67B28D), the only caller of which is the GM command
    /// <c>@ReloadMonitemsTreeCfg</c> (0x624009).
    ///
    /// <para>Line grammar (recovered from the tokenizer calls, all <c>GetValidStr3</c>
    /// = <c>sub_4C6BA4</c>):</para>
    /// <code>
    /// &lt;MonsterName&gt; &lt;Rate&gt;/&lt;Nth&gt; &lt;Item&gt;:&lt;Count&gt;:&lt;A&gt;/&lt;B&gt; &lt;Item&gt;:&lt;Count&gt;:&lt;A&gt;/&lt;B&gt; ...
    /// </code>
    /// <list type="bullet">
    /// <item>Whitespace fields split on {0x09, 0x20} (0x67AF82/0x67AF86 sep bytes).</item>
    /// <item>Field 2 <c>Rate/Nth</c> splits on '/' (0x67AFD0/0x67AFF3) → group probability.</item>
    /// <item>Each remaining field <c>Item:Count:A/B</c> splits on ':' three times
    ///       (0x67B0C4/0x67B0E7/0x67B10A) then the third token on '/'
    ///       (0x67B12D/0x67B150).</item>
    /// </list>
    ///
    /// <para>One line builds one <see cref="MonItemsTreeGroup"/> (0x14-byte record) plus a
    /// chain of <see cref="MonItemsTreeNode"/> item records (0x24 bytes each).  A monster
    /// name can appear on several lines; each becomes an independent group.</para>
    /// </summary>
    internal static class MonItemsTreeLoader
    {
        // 0x67B0A9 Trim (0x40C140) then 0x67B0B4 cmp [ebp-0x5C],0 / je 0x67B230 — an item
        // field that trims to empty ends the line, so the inner loop stops there.
        private static readonly char[] WhitespaceSeparators = { ' ', '\t' };

        // The weight constant folded in at 0x67B1E0 (fmul dword [0x67B2AC] = 10000.0f).
        // Because the native formula divides A by A (0x67B1A9 and 0x67B1C4 both read the
        // SAME token [ebp-0x1C]) the ratio is always 1, so every item contributes exactly
        // ROUND(1.0 * 10000.0) = 10000.  The '/B' half of the third field is parsed
        // (0x67B14A) and then discarded — it is read nowhere.  Selection is therefore
        // UNIFORM across the items of a fired group, and this constant is the band width.
        private const double NativeWeightConstant = 10000.0;

        /// <summary>
        /// Load MonItemsTree.txt and build the monster→group-chain map.
        /// Native: sub_67AEC0.  A missing file yields an empty map (0x67AF0E
        /// <c>FileExists / je 0x67B244 / xor eax,eax</c>) — fail-closed, no drops.
        /// </summary>
        public static Dictionary<string, MonItemsTreeGroup> Load(string filePath, UserEngine userEngine)
        {
            // Native keys the hash [UserEngine+0xDC] by monster name through sub_49F5F4;
            // monster names are GBK/CJK so case folds to a no-op, and the rest of the C#
            // drop path already matches monster names case-insensitively.
            var chains = new Dictionary<string, MonItemsTreeGroup>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(filePath))
            {
                M2Share.MainOutMessage($"[MonItemsTree] File not found: {filePath}");
                return chains;
            }

            try
            {
                using var reader = new StreamReader(filePath, Encoding.GetEncoding("GBK"));
                string line;
                int lineNum = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;

                    // 0x67AF64 cmp byte [line],0x23 (#) and 0x67AF70 cmp byte [line],0x3B (;)
                    // test the FIRST raw byte before any trimming; a null/empty line
                    // (0x67AF5A cmp [ebp-0x10],0 / je) is skipped too.
                    if (string.IsNullOrEmpty(line))
                        continue;
                    var firstChar = line[0];
                    if (firstChar == '#' || firstChar == ';')
                        continue;

                    try
                    {
                        ParseLine(line, chains, userEngine);
                    }
                    catch (Exception ex)
                    {
                        M2Share.MainOutMessage($"[MonItemsTree] Error parsing line {lineNum}: {line} - {ex.Message}");
                    }
                }

                M2Share.MainOutMessage($"[MonItemsTree] Loaded {chains.Count} monster chains from {filePath}");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[MonItemsTree] Failed to load {filePath}: {ex.Message}");
            }

            return chains;
        }

        /// <summary>
        /// Parse one line into a group and its item chain.  Mirrors the loader body
        /// 0x67AF7C-0x67B22B.
        /// </summary>
        private static void ParseLine(string line, Dictionary<string, MonItemsTreeGroup> chains,
            UserEngine userEngine)
        {
            // 0x67AF93 token1 = MonsterName, 0x67AFBA token2 = "Rate/Nth", remainder = items.
            var fields = line.Split(WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
                return;

            var monsterName = fields[0];

            // 0x67AFCA / 0x67AFED split token2 on '/'; 0x67B02A/0x67B034 StrToInt into [G]/[G+4].
            var rateNth = fields[1].Split('/');
            var group = new MonItemsTreeGroup
            {
                Rate = rateNth.Length > 0 ? ParseIntOrZero(rateNth[0]) : 0,
                Nth = rateNth.Length > 1 ? ParseIntOrZero(rateNth[1]) : 0,
                TotalWeight = 0,
                ItemHead = null,
                Next = null,
            };

            // 0x67B03F-0x67B078 link the group by monster name.  When the monster already
            // has a chain the new group is spliced in RIGHT AFTER the head
            // (0x67B05F G.Next = head.Next; 0x67B065 head.Next = G), otherwise it becomes
            // the head (0x67B078).  Order matters because the selector walks groups in
            // this order and the traversal's gold-arm break is order-sensitive.
            if (chains.TryGetValue(monsterName, out var head))
            {
                group.Next = head.Next;
                head.Next = group;
            }
            else
            {
                chains[monsterName] = group;
            }

            // 0x67B082-0x67B22B inner loop: one item record per remaining whitespace field.
            MonItemsTreeNode lastItem = null;
            for (var f = 2; f < fields.Length; f++)
            {
                var itemField = fields[f];
                if (string.IsNullOrWhiteSpace(itemField))
                    continue;

                // 0x67B0BE/0x67B0E1/0x67B104 split on ':' → Name, Count, Third.
                var colonParts = itemField.Split(':');
                var itemName = colonParts[0];
                if (string.IsNullOrEmpty(itemName))
                    continue;

                var count = colonParts.Length > 1 ? ParseIntOrZero(colonParts[1]) : 0;

                // 0x67B127/0x67B14A split the third token on '/'; the 'A' half is read
                // twice for A/A (0x67B1A9/0x67B1C4) and the 'B' half (0x67B157) is dropped.
                var weightSpec = colonParts.Length > 2 ? colonParts[2] : string.Empty;
                var slashParts = weightSpec.Split('/');
                var a = slashParts.Length > 0 ? ParseIntOrZero(slashParts[0]) : 0;

                // 0x67B1E6 @ROUND(A/A*10000) then 0x67B1EB accumulate, 0x67B1F1 store the
                // running total as this node's cumulative weight.  A==0 leaves the band
                // width at 0 so the item is never reachable by the weighted pick — a
                // fail-closed reading of the native divide-by-zero on a malformed row.
                var weight = a != 0
                    ? (int)Math.Round((double)a / a * NativeWeightConstant, MidpointRounding.ToEven)
                    : 0;
                group.TotalWeight += weight;

                var node = new MonItemsTreeNode
                {
                    // 0x67B1A4 ShortString[15] assign — names are clamped to 15 bytes.
                    ItemName = itemName.Length > 15 ? itemName.Substring(0, 15) : itemName,
                    CumulativeWeight = group.TotalWeight,
                    // 0x67B1FC store StrToInt(Count) at [node+0x1C]; this is the item's
                    // guaranteed repeat count, or, for a gold row, the gold amount N.
                    RepeatCount = count,
                    // 0x67B209 call sub_74C2D4 — a plain name→template hash lookup with no
                    // index exclusion.  GetStdItem(string) scans from index 0 so "金币"
                    // resolves to the index-0 sentinel (NativeWireIndex == 0), which is how
                    // the traversal recognises a gold row.  Never GetStdItemIdx here.
                    StdItem = userEngine.GetStdItem(itemName),
                    Next = null,
                };

                // 0x67B211-0x67B22B: first node → G.ItemHead, later nodes appended to tail.
                if (lastItem == null)
                    group.ItemHead = node;
                else
                    lastItem.Next = node;
                lastItem = node;
            }
        }

        // Native uses StrToInt (0x40C9D8), which raises on a non-numeric token and unwinds
        // the whole load through the function's SEH frame.  Rather than abort every chain
        // on one bad row, treat an unparseable token as 0 (fail-closed for that column) and
        // let the per-line try/catch log genuinely broken rows.
        private static int ParseIntOrZero(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0;
            return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v
                : 0;
        }
    }
}
