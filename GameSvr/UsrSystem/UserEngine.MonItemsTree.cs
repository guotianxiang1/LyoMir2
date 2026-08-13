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
        /// <c>ecx = 5</c> at 0x71FC02 and 0x71FC79 — the exclusive chain drops at radius 5,
        /// not the radius 3 the monster's own table uses (<c>ecx = 3</c> @0x71FDCF /
        /// 0x71FE46).  Hardcoded in both arms; no config key exists for it.
        /// </summary>
        private const int NativeExclusiveChainDropRange = 5;

        /// <summary>
        /// Traverse the exclusive drop chain for a monster.
        /// Native: <c>sub_71FA20</c> loop 1, 0x71FB5B-0x71FCFF, segment 1 of
        /// @AfterScatterItems.
        ///
        /// Gold-entry chain truncation is faithful, not a C# shortcut: the gold arm ends
        /// in <c>0x71FB8D E9 6D 01 00 00  jmp 0x71FCFF</c>, which lands past the node
        /// advance at 0x71FCD9, so the remaining nodes are never visited.
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

            // Chain head by monster name: 0x71FB42 mov eax,[0x7D5D9C] / 0x71FB49 call
            // sub_67B2B0; 0x71FB51 cmp [ebp-0x20],0 / je 0x71FCFF when the head is nil.
            if (!_monItemsTreeChains.TryGetValue(monsterName, out var node))
                return;

            while (node != null)
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
                // Cross-check on the same record: sub_74C338 dispatches the item factory
                // on byte[template+0x14], and 0x14 is exactly where StdMode lands after
                // wireIndex(2) + reserved(2) + ShortString[15](16).  This settles the
                // SPWN-58 / SPWN-59 BLOCKED note on what "首 word == 0" means.
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
                    // The old code both dropped the Random draw and paid the gold out
                    // through a fabricated IncGold + SysMsg path that bypassed all of it.
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

                // 0071FCD9  mov eax,[node+0x20] -> next; free the 0x24-byte node
                // @0x71FCEA; 0x71FCF5 cmp / jne 0x71FB5B.
                node = node.Next;
            }
        }
    }
}
