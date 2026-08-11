using System.Text;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// MonItemsTree.txt exclusive drop chain loader.
    /// Reverse-engineered from sub_67AEC0 (EA 0x67AEC0-0x67B28D).
    /// </summary>
    internal static class MonItemsTreeLoader
    {
        /// <summary>
        /// Load MonItemsTree.txt and build exclusive drop chains keyed by monster name.
        /// Native: sub_67AEC0 called from GM command @ReloadMonitemsTreeCfg (0x624002).
        /// </summary>
        /// <param name="filePath">Path to MonItemsTree.txt</param>
        /// <param name="userEngine">UserEngine instance for StdItem lookup</param>
        /// <returns>Dictionary keyed by monster name (case-insensitive), value = chain head</returns>
        public static Dictionary<string, MonItemsTreeNode> Load(string filePath, UserEngine userEngine)
        {
            var chains = new Dictionary<string, MonItemsTreeNode>(StringComparer.OrdinalIgnoreCase);

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
                    line = line.Trim();

                    // Skip comments and empty lines
                    // EA 0x67af67: cmp byte ptr [eax], 0x23 (# comment)
                    // EA 0x67af73: cmp byte ptr [eax], 0x3b (; comment)
                    if (string.IsNullOrWhiteSpace(line) || line[0] == '#' || line[0] == ';')
                        continue;

                    try
                    {
                        ParseAndAddNode(line, chains, userEngine);
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
        /// Parse one line and add node to the appropriate chain.
        /// Native parsing at EA 0x67AF4C-0x67B22B with multiple GetValidStr3 calls.
        /// Format (inferred from disassembly): MonsterName ItemName MinDrops/MaxDrops RepeatCount
        /// </summary>
        private static void ParseAndAddNode(string line, Dictionary<string, MonItemsTreeNode> chains,
            UserEngine userEngine)
        {
            // Native uses tab/space as primary delimiters (EA 0x67AF82-0x67AF93: sep={0x09, 0x20})
            // Then '/' and ':' as secondary delimiters within tokens
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                return; // Insufficient fields

            var monsterName = parts[0].Trim();
            var itemName = parts[1].Trim();

            // Parse drop range (format: "min/max")
            var dropRangeParts = parts[2].Split('/');
            if (dropRangeParts.Length < 2)
                return;

            if (!int.TryParse(dropRangeParts[0], out int minDrops))
                return;
            if (!int.TryParse(dropRangeParts[1], out int maxDrops))
                return;

            // Parse repeat count
            if (!int.TryParse(parts[3], out int repeatCount))
                return;

            // Lookup item by name (EA 0x67b1ff-0x67b20e: call 0x74c2d4)
            // Returns null for gold entries or items not found
            var stdItem = userEngine.GetStdItem(itemName);

            // Create node (EA 0x67b16d-0x67b182: alloc 0x24 bytes + zerofill)
            var node = new MonItemsTreeNode
            {
                ItemName = itemName.Length > 15 ? itemName.Substring(0, 15) : itemName, // ShortString[15]
                StdItem = stdItem,
                RepeatCount = repeatCount,
                CumulativeWeight = 0, // Computed during construction, not used in traversal
                Next = null
            };

            // Get or create chain for this monster
            if (!chains.TryGetValue(monsterName, out var chainHead))
            {
                // First node for this monster
                chains[monsterName] = node;
            }
            else
            {
                // Append to existing chain (EA 0x67b21a: link prev->next = current)
                var tail = chainHead;
                while (tail.Next != null)
                    tail = tail.Next;
                tail.Next = node;
            }
        }
    }
}
