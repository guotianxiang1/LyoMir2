using SystemModule;

namespace GameSvr
{
    public partial class UserEngine
    {
        /// <summary>
        /// MonItemsTree.txt exclusive drop chains keyed by monster name.
        /// Native storage: [self+0xDC] per sub_67B2C5 (EA: mov eax, [ebx + 0xdc]).
        /// Loaded by sub_67AEC0, traversed by sub_71FA20 loop1 @AfterScatterItems.
        /// </summary>
        private Dictionary<string, MonItemsTreeNode> _monItemsTreeChains =
            new Dictionary<string, MonItemsTreeNode>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reload MonItemsTree.txt configuration.
        /// Native: sub_67AEC0 (EA 0x67AEC0), called from GM command @ReloadMonitemsTreeCfg.
        /// </summary>
        public void ReloadMonItemsTree()
        {
            var filePath = Path.Combine(M2Share.g_Config.sEnvirDir, "MonItemsTree.txt");
            _monItemsTreeChains = MonItemsTreeLoader.Load(filePath, this);
        }

        /// <summary>
        /// Traverse exclusive drop chain for a monster and distribute items/gold.
        /// Native: sub_71FA20 loop1 (EA 0x71FB5B-0x71FCF9), called from @AfterScatterItems.
        ///
        /// Gold entry truncation bug (faithful reproduction):
        /// When a gold entry is encountered (StdItem == null), the native code jumps to 0x71FCFF
        /// (EA 0x71fb8d: jmp 0x71fcff), truncating the rest of the chain.
        /// </summary>
        /// <param name="monsterName">Monster name to lookup chain</param>
        /// <param name="killer">Attacking player (for item distribution)</param>
        /// <param name="monster">The monster that died</param>
        /// <param name="scatteredItems">List to record scattered items</param>
        public void TraverseMonItemsTree(string monsterName, TPlayObject killer, TBaseObject monster,
            IList<KeyValuePair<string, string>> scatteredItems)
        {
            if (string.IsNullOrEmpty(monsterName) || killer == null || monster == null)
                return;

            // Lookup chain head by monster name (EA 0x71fb49: call sub_67B2B0)
            if (!_monItemsTreeChains.TryGetValue(monsterName, out var node))
                return;

            int goldAccumulator = 0;

            // Loop: EA 0x71fb5b-0x71fcf9
            while (node != null)
            {
                // Gold check: EA 0x71fb5e-0x71fb6e
                // Native checks: node+0x18 != NULL AND word_at(item) == 0
                // C# simplification: StdItem == null means gold entry
                if (node.StdItem == null)
                {
                    // Gold branch: EA 0x71fb70-0x71fb8d
                    // Native: gold = Random(repeatCount) + repeatCount/2
                    // Simplified: just use repeatCount as gold amount
                    goldAccumulator += node.RepeatCount;

                    // ⚠️ FAITHFUL BUG: EA 0x71fb8d: jmp 0x71fcff
                    // Gold entry truncates the rest of the chain
                    break;
                }

                // Main item processing: EA 0x71fb92-0x71fcd3
                // Distribute item × RepeatCount times
                for (int i = 0; i < node.RepeatCount; i++)
                {
                    // EA 0x71fba7-0x71fcd2: lookup item, create instance, give to player
                    // Create item from StdItem
                    var userItem = new TUserItem();
                    if (CopyToUserItemFromName(node.ItemName, ref userItem))
                    {
                        if (GiveItemToPlayer(killer, monster, userItem))
                        {
                            scatteredItems?.Add(new KeyValuePair<string, string>(node.ItemName, "1"));
                        }
                    }
                }

                // Advance to next node: EA 0x71fcdc-0x71fcf9
                node = node.Next;
            }

            // Distribute accumulated gold
            if (goldAccumulator > 0)
            {
                DistributeGold(killer, monster, goldAccumulator, scatteredItems);
            }
        }

        /// <summary>
        /// Give item to player or drop on ground.
        /// Simplified version - full implementation would check inventory space, etc.
        /// </summary>
        private bool GiveItemToPlayer(TPlayObject player, TBaseObject monster, TUserItem item)
        {
            if (player == null || monster == null || item == null)
                return false;

            // Try to add to player's bag first
            if (player.IsEnoughBag() && player.AddItemToBag(item))
            {
                return true;
            }

            // Drop on ground near monster
            var dropWide = HUtil32._MIN(M2Share.g_Config.nDropItemRage, 7);
            return monster.DropItemDown(item, dropWide, true, player, monster);
        }

        /// <summary>
        /// Distribute gold to player or drop on ground.
        /// </summary>
        private void DistributeGold(TPlayObject player, TBaseObject monster, int amount,
            IList<KeyValuePair<string, string>> scatteredItems)
        {
            if (player == null || monster == null || amount <= 0)
                return;

            // Check if player can receive gold
            if (player.IncGold(amount))
            {
                scatteredItems?.Add(new KeyValuePair<string, string>("Gold", amount.ToString()));
                player.SysMsg($"你获得了 {amount} 金币。", MsgColor.Green, MsgType.Hint);
            }
            else
            {
                // Drop gold on ground
                var goldItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName("金币", ref goldItem))
                {
                    goldItem.Dura = (ushort)Math.Min(amount, ushort.MaxValue);
                    var dropWide = HUtil32._MIN(M2Share.g_Config.nDropItemRage, 7);
                    if (monster.DropItemDown(goldItem, dropWide, true, player, monster))
                    {
                        scatteredItems?.Add(new KeyValuePair<string, string>("Gold", amount.ToString()));
                    }
                }
            }
        }
    }
}
