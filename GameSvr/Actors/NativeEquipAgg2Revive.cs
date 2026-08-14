using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Rebuilds agg2 fields that feed the second revive path predicate
    /// (<c>sub_746084</c> @0x746084) from equipped items during
    /// <c>RecalcAbilitys</c> (<c>sub_73D500</c> @0x73D63D copies container+0x1F8
    /// → self+0x1B0, so self+0x1D1 = agg2[0x21], self+0x1DD = agg2[0x2D]).
    /// </summary>
    internal static class NativeEquipAgg2Revive
    {
        /// <summary>
        /// <c>sub_76203C</c> @0x76235F <c>mov byte [agg2+0x21],1</c> on ext-abil ident 0x50
        /// (same arm @0x762BEB in sibling dispatcher <c>sub_762974</c>).
        /// </summary>
        private const ushort IdentSecondPathFlag = 0x50;

        internal static void Recalc(TBaseObject actor)
        {
            actor.m_btNativeSecondPathFlag = 0;
            actor.m_btNativeSecondPathTier = 0;

            if (actor.m_UseItems == null)
            {
                return;
            }

            for (var slot = 0; slot < Grobal2.HUMAN_EQUIPPED_ITEM_COUNT; slot++)
            {
                var userItem = actor.m_UseItems[slot];
                if (userItem == null || userItem.wIndex <= 0 || userItem.Dura <= 0)
                {
                    continue;
                }

                var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                if (stdItem == null)
                {
                    continue;
                }

                if (HasSecondPathFlagIdent(stdItem))
                {
                    actor.m_btNativeSecondPathFlag = 1;
                }

                ApplyArmRingAgg2Tier(actor, stdItem);
            }
        }

        private static bool HasSecondPathFlagIdent(GoodItem stdItem)
        {
            if (!stdItem.NativeItemExtAbilParsed)
            {
                return false;
            }

            for (var i = 0; i < stdItem.NativeItemExtAbilIdents.Length; i++)
            {
                if (stdItem.NativeItemExtAbilIdents[i] == IdentSecondPathFlag)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// <c>TArmRing VMT+0x08</c> = <c>sub_762718</c> @0x7627A7..0x7627CF writes
        /// <c>[agg2+0x2D]</c> with
        /// <c>GetRandomRange(3,15)+GetRandomRange(5,30)</c> when the sum is &gt; 0.
        /// </summary>
        private static void ApplyArmRingAgg2Tier(TBaseObject actor, GoodItem stdItem)
        {
            if (NativeItemFactory.GetClassName(stdItem) != "TArmRing")
            {
                return;
            }

            var tier = GoodItemGetRandomRange(3, 15) + GoodItemGetRandomRange(5, 30);
            if (tier > 0)
            {
                actor.m_btNativeSecondPathTier = (byte)tier;
            }
        }

        private static int GoodItemGetRandomRange(int count, int rate)
        {
            var result = 0;
            for (var i = 0; i < count; i++)
            {
                if (M2Share.RandomNumber.Random(rate) == 0)
                {
                    result++;
                }
            }

            return result;
        }
    }
}
