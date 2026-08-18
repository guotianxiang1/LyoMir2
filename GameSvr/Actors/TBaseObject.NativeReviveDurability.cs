using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// DURA-44: native <c>sub_73ED28</c> @0x73ED28 — on revive success, debit
        /// exactly one equipped item whose <c>+0x104</c> class bit matches
        /// <paramref name="mode"/> (<c>sub_73EBF0</c> @0x73EBF0 via
        /// <see cref="NativeItemClass104"/>).
        /// </summary>
        /// <param name="mode">
        /// 0 = equip-revive ring path (<c>xor edx,edx</c> @0x743796, bit0);
        /// 1 = second rebirth path (<c>mov edx,1</c> @0x743860, bit1|bit2).
        /// </param>
        private void ItemDamageRevivalRing(int mode = 0)
        {
            // 0x73ED47 xor esi,esi / 0x73EDCC cmp esi,0x10 / 0x73EDCF jne 0x73ED49
            for (var slot = 0; slot < Grobal2.HUMAN_EQUIPPED_ITEM_COUNT; slot++)
            {
                var userItem = m_UseItems[slot];
                if (userItem == null || userItem.wIndex <= 0)
                {
                    continue;
                }

                // sub_73EBF0 @0x73EBFF cmp word [item+0x26],0 / jbe -> false
                if (userItem.Dura <= 0)
                {
                    continue;
                }

                var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                if (stdItem == null)
                {
                    continue;
                }

                if (!NativeItemClass104.MatchesReviveDurabilityTarget(userItem, mode))
                {
                    continue;
                }

                // sub_73EC40 @0x73EC40 — inner damage worker; first match wins.
                ApplyNativeReviveRingDurabilityLoss(slot, userItem, stdItem);
                return;
            }
        }

        /// <summary>
        /// <c>sub_73EC40</c> @0x73EC40 success body for one equipped instance.
        /// </summary>
        private void ApplyNativeReviveRingDurabilityLoss(int slot, TUserItem userItem,
            GoodItem stdItem)
        {
            if (userItem.Dura > 1000)
            {
                // 0x73EC6B cmp word [esi+0x26],0x3E8 / ja / 0x73EC73 sub word [esi+0x26],0x3E8
                userItem.Dura -= 1000;
            }
            else
            {
                // 0x73EC7B mov word [esi+0x26],0 — leave slot occupied (no wIndex clear).
                userItem.Dura = 0;
                // 0x73EC8D call 0x75EE78 RecalcAbilitys
                RecalcAbilitys();
                // 0x73EC9A call [vmt+0x8C] FeatureChanged
                FeatureChanged();
                // 0x73ECD1 mov cx,0x38FF / [vmt+0xD4] — item name + "失效了" @0x73ED20
                var showName = M2Share.FilterShowName(stdItem.Name);
                SendNativeStateSysMsg(0x38FF, showName + "失效了");
            }

            // sub_73ED28 @0x73ED6B..0x73ED85 always emits RM_DURACHANGE on success.
            SendMsg(this, Grobal2.RM_DURACHANGE, slot, userItem.Dura, userItem.DuraMax, 0, "");
        }
    }
}
