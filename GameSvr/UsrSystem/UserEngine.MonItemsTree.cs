using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    public partial class UserEngine
    {
        /// <summary>
        /// MonItemsTree.txt exclusive drop groups keyed by monster name.
        /// Native storage: the name→group hash at <c>[UserEngine+0xDC]</c>
        /// (sub_67B2C5: <c>mov eax,[ebx+0xDC]</c>; sub_67AEE6 clears it on reload).
        /// Each monster maps to the HEAD of a singly-linked <see cref="MonItemsTreeGroup"/>
        /// chain; every group is an independent probability gate.
        /// </summary>
        private Dictionary<string, MonItemsTreeGroup> _monItemsTreeChains =
            new Dictionary<string, MonItemsTreeGroup>(System.StringComparer.OrdinalIgnoreCase);

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
        /// <c>ecx = 5</c> at 0x71FC02 and 0x71FC79 — the exclusive chain drops at radius 5,
        /// not the radius 3 the monster's own table uses (<c>ecx = 3</c> @0x71FDCF /
        /// 0x71FE46).  Hardcoded in both arms; no config key exists for it.
        /// </summary>
        private const int NativeExclusiveChainDropRange = 5;

        /// <summary>
        /// Select one item per firing group — a 1:1 port of the selector <c>sub_67B2B0</c>
        /// (EA 0x67B2B0-0x67B358), called at 0x71FB49 with the monster name.
        ///
        /// <para>For every group in the monster's chain the native code draws TWO randoms
        /// UNCONDITIONALLY, in this order:</para>
        /// <code>
        /// 67B2E1  Random([G+8]  = TotalWeight)  -> weightRoll   (drawn even if the group misses)
        /// 67B2EE  Random([G+4]  = Nth)          -> probRoll
        /// 67B2F6  cmp probRoll,[G]=Rate / jge   -> group fires when probRoll &lt; Rate
        /// </code>
        /// A firing group then walks its item chain and takes the first node whose running
        /// weight strictly exceeds the roll (0x67B309 <c>cmp esi,[node+0x10] / jge next</c>),
        /// i.e. the band <c>[cumWeight-10000, cumWeight)</c> — a uniform pick since every
        /// band is 10000 wide.  The chosen node is copied and PREPENDED to the result
        /// (0x67B329 <c>newnode.Next = head; head = newnode</c>), so the traversal sees the
        /// fired groups in reverse chain order — which is what makes a selected gold row
        /// able to truncate the rest of the run.
        ///
        /// <para>The two Random calls fire for missing groups too, so this must NOT be
        /// short-circuited to skip the weight roll on a miss.</para>
        /// </summary>
        private List<MonItemsTreeNode> SelectMonItemsTree(string monsterName)
        {
            var selected = new List<MonItemsTreeNode>();

            // 0x67B2CD sub_49F5F4 name lookup; 0x67B2D5 cmp / je 0x67B34F -> return nil.
            if (string.IsNullOrEmpty(monsterName)
                || !_monItemsTreeChains.TryGetValue(monsterName, out var group))
            {
                return selected;
            }

            while (group != null)                              // 0x67B349 cmp/jne 0x67B2DB
            {
                // 0x67B2E1 / 0x67B2EE — both draws happen before the gate test.
                var weightRoll = M2Share.RandomNumber.Random(group.TotalWeight);
                var probRoll = M2Share.RandomNumber.Random(group.Nth);

                // 0x67B2F6 cmp / 0x67B2F8 jge 0x67B340 — fire when probRoll < Rate.
                if (probRoll < group.Rate)
                {
                    var cursor = group.ItemHead;               // 0x67B2FD mov eax,[G+0xC]
                    while (cursor != null)                     // 0x67B307 loop
                    {
                        // 0x67B309 cmp esi,[node+0x10] / jge 0x67B334 (advance).
                        if (weightRoll < cursor.CumulativeWeight)
                        {
                            // 0x67B30E alloc + 0x67B326 copy + 0x67B329/0x67B32F prepend.
                            selected.Insert(0, cursor);
                            break;                             // 0x67B332 one item per group
                        }
                        cursor = cursor.Next;                  // 0x67B334-0x67B339
                    }
                }

                group = group.Next;                            // 0x67B343 mov eax,[G+0x10]
            }

            return selected;
        }

        /// <summary>
        /// Traverse the exclusive drop chain for a monster.
        /// Native: <c>sub_71FA20</c> loop 1, 0x71FB49-0x71FCFF, segment 1 of
        /// @AfterScatterItems.  The per-death SELECTION is <see cref="SelectMonItemsTree"/>
        /// (sub_67B2B0); this method consumes its result.
        ///
        /// Gold-entry chain truncation is faithful, not a C# shortcut: the gold arm ends
        /// in <c>0x71FB8D E9 6D 01 00 00  jmp 0x71FCFF</c>, which lands past the node
        /// advance at 0x71FCD9, so the remaining selected nodes are never visited.
        /// </summary>
        /// <param name="monsterName">Monster name used to look up the chain head</param>
        /// <param name="killer">The killer, passed through as the item creator</param>
        /// <param name="monster">The monster that died (drop anchor and gold accumulator)</param>
        /// <param name="scatteredItems">List to record scattered items</param>
        public void TraverseMonItemsTree(string monsterName, TBaseObject killer, TBaseObject monster,
            IList<KeyValuePair<string, string>> scatteredItems)
        {
            if (string.IsNullOrEmpty(monsterName) || monster == null)
                return;

            // 0071FA8A  83 B8 74 04 00 00 00  cmp dword [self+0x474],0
            // 0071FA91  0F 84 FB 05 00 00     je  0x720092
            // A monster with no drop table exits the WHOLE scatter routine before
            // segment 1, so the chain must not fire for it.  ([self+0x474] is the drop
            // table, the same field loop 2 walks at 0x71FCFF.)  The Die call site now
            // applies the same gate to segments 3 and 4 as well; this copy stays
            // because the method is public and reachable from elsewhere.
            if (!TryGetMonsterInfo(monster.m_sCharName, out var monsterInfo)
                || monsterInfo.ItemList == null)
            {
                return;
            }

            // The killer may be nil: 0x71FAB4 `cmp [ebp-8],0 / je 0x71FB2E` jumps INTO
            // segment 1, so a monster that died without an attributed killer still runs
            // its chain.  It is passed through untouched as the sub_7688A0 creator arg.

            // 0x71FB49 call sub_67B2B0 selects one item per firing group; 0x71FB51 cmp
            // [ebp-0x20],0 / je 0x71FCFF means an empty selection scatters nothing.
            var selected = SelectMonItemsTree(monsterName);

            foreach (var node in selected)                     // 0x71FB5B chain walk
            {
                // 0071FB5E  83 78 18 00     cmp dword [node+0x18],0 / 74 2E je 0x71FB92
                // 0071FB6A  66 83 38 00     cmp word [template],0   / 75 22 jne 0x71FB92
                // Gold therefore needs BOTH a resolved template AND a zero first word.
                // C# had `StdItem == null -> gold`, which is the exact inverse: a name
                // that failed to resolve became free money, while a real gold row was
                // paid out as an item.
                //
                // The first word of the native StdItem record is its wire index — the
                // same +0x00 ushort the type2 DB codec calls NativeWireIndex — so
                // "word == 0" means "this row resolved to the index-0 金币 sentinel".
                if (node.StdItem != null && node.StdItem.NativeWireIndex == 0)
                {
                    // 0071FB70  8B 40 1C     mov eax,[node+0x1C]      ; N
                    // 0071FB76  E8 D1 3F CE FF  call 0x403B4C         ; Random(N)
                    // 0071FB7E  8B 52 1C     mov edx,[node+0x1C]
                    // 0071FB81  D1 FA        sar edx,1                ; N div 2 ...
                    // 0071FB83  79 03 / 83 D2 00   jns / adc edx,0    ; ... toward zero
                    // 0071FB88  03 C2        add eax,edx
                    // 0071FB8A  01 45 EC     add [ebp-0x14],eax
                    // [ebp-0x14] is the SHARED gold accumulator that the segment-4
                    // settlement (cap 3000, divide by the fatigue multiplier, 16 piles)
                    // later consumes, i.e. the same place MonGetRandomItems adds to.
                    // C# int division truncates toward zero, matching sar+jns+adc.
                    monster.m_nGold = monster.m_nGold
                        + node.RepeatCount / 2
                        + M2Share.RandomNumber.Random(node.RepeatCount);
                    break;      // 0x71FB8D jmp 0x71FCFF
                }

                // 0071FB92  item arm.  `mov ebx,[node+0x1C] / dec ebx / test ebx,ebx /
                // jl 0x71FCD9` skips a non-positive count, and inside the repeat loop
                // `mov edi,[node+0x18] / test edi,edi / je 0x71FBC4` followed by
                // `cmp [ebp-0x28],0 / je 0x71FCD2` makes an unresolved template a no-op
                // repeat — native builds nothing and pays nothing.
                for (var i = 0; i < node.RepeatCount && node.StdItem != null; i++)
                {
                    var userItem = new TUserItem();
                    if (!CopyToUserItemFromName(node.ItemName, ref userItem))
                        continue;

                    // 0071FBCE  xor edx,edx / 0071FBD5  call dword [ecx+0x28]
                    // Same +0x28 the monster table runs at 0x71FDA2.
                    NativeItemPlus28.ApplyOnDrop(userItem, node.StdItem);

                    // 0071FC5D arm: push 1 / push 0 / push killer / push <name string> /
                    // mov ecx,5 / mov edx,item / mov eax,[ebp-0xC] / call sub_7688A0.
                    // Both arms of the chain (0x71FBF5 and 0x71FC5D) go straight to the
                    // ground.  The bag attempt C# used to make first was invented and it
                    // changed ownership: native scatters where anyone can contest the
                    // pickup.
                    if (monster.DropItemDown(userItem, NativeExclusiveChainDropRange,
                            true, killer, monster))
                    {
                        scatteredItems?.Add(new KeyValuePair<string, string>(node.ItemName, "1"));
                    }
                }

                // 0071FCD9  mov eax,[node+0x20] -> next selected node; native frees the
                // 0x24-byte copy @0x71FCEA (the C# GC handles that) and loops @0x71FCF5.
            }
        }
    }
}
