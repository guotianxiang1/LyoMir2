using System.Buffers.Binary;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private enum PileItemContainer
        {
            None,
            HumanBag,
            HeroBag,
            Storage
        }

        internal void ClientPileUpItem(int targetClientItemId, int sourceClientItemId, int series)
        {
            var container = FindPileItemContainer(targetClientItemId, sourceClientItemId, series,
                out var items, out var target, out var source);

            if (container == PileItemContainer.None)
                return;

            if (CanPileItems(target, source, out var failureMessage))
            {
                var available = target.DuraMax - target.Dura;
                if (available >= 1)
                {
                    var transferred = Math.Min(available, source.Dura);
                    target.Dura = (ushort)(target.Dura + transferred);
                    source.Dura = (ushort)(source.Dura - transferred);
                    SendPileItemDuraChange(container, target);
                }

                WritePileItemLog(0x45, target, "被减少的道具ID:",
                    source.MakeIndex);
                WritePileItemLog(0x44, source, "被增加的道具ID:",
                    target.MakeIndex);
                if (source.Dura == 0)
                {
                    items.Remove(source);
                    SendPileItemDeleted(container, source);
                }
                else
                {
                    SendPileItemDuraChange(container, source);
                }
            }
            else if (!string.IsNullOrEmpty(failureMessage))
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0,
                    failureMessage);
            }

            SendDefMessage(Grobal2.SM_ITEM_PILEUP_RESULT, EnsureClientItemId(target),
                HUtil32.LoWord(sourceClientItemId),
                HUtil32.HiWord(sourceClientItemId), series, "");
        }

        private void ClientSplitItem(int clientItemId, int count, int series)
        {
            // 眼神「防0拆分」. The plugin detours this routine's prologue: at
            // 0x100AA765 it hands 0x10032FD0 the range 0x6E0FF3..0x6E0FF9 plus an
            // 18-byte stub assembled from the static int[] templates 0x102D15E0 /
            // 0x102D17B0 / 0x102D23E0 / 0x102D0C30 (one byte per dword) —
            //   66 83 F9 00  cmp cx,0        ; ecx = the count parameter
            //   7F 05        jg  +5
            //   B9 01000000  mov ecx,1
            //   51           push ecx        ; the two displaced instructions
            //   B9 07000000  mov ecx,7
            //   E9 ....      jmp 0x6E0FF9
            // so it clamps the count to 1 before anything else runs. The compare
            // is 16-bit and signed while the assignment replaces all of ecx, and
            // the wire field is Param (u16 @+6), so counts 32768..65535 clamp too.
            // Unpatched, count 0 splits off a Dura-0 item that the source keeps
            // paying nothing for — an extra bag entry out of nothing.
            if (unchecked((short)count) <= 0 &&
                new YanshenApi(this, null, M2Share.PluginManager).IsZeroDefSplit())
            {
                count = 1;
            }

            var container = FindSplitItemContainer(clientItemId, series, out var items, out var source);
            if (container == PileItemContainer.None || source == null ||
                source.Dura <= count || !IsPileItem(source) || !HasPileItemSpace(container, items.Count))
            {
                return;
            }

            var stdItem = M2Share.UserEngine.GetStdItem(source.wIndex);
            if (stdItem == null)
                return;

            source.Dura -= (ushort)count;
            SendPileItemDuraChange(container, source);
            var splitItem = new TUserItem
            {
                MakeIndex = M2Share.GetItemNumber(),
                wIndex = source.wIndex,
                Dura = (ushort)count,
                DuraMax = stdItem.DuraMax
            };
            if (TryGetNativePileCompatibility(source, out var compatibility))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(splitItem.btValue.AsSpan(10, 2),
                    compatibility);
            }
            WritePileItemLog(0x46, source, "拆分生成的道具ID：",
                splitItem.MakeIndex);
            WritePileItemLog(0x47, splitItem, "源道具ID：",
                source.MakeIndex);

            ReassignClientItemId(splitItem);
            items.Add(splitItem);
            switch (container)
            {
                case PileItemContainer.HumanBag:
                    SendAddItem(splitItem);
                    break;
                case PileItemContainer.HeroBag:
                    m_HeroObject.SendHeroAddItem(splitItem);
                    break;
                case PileItemContainer.Storage:
                    SendStorageSplitItem(splitItem);
                    break;
            }
        }

        private PileItemContainer FindPileItemContainer(int targetClientItemId, int sourceClientItemId, int series,
            out IList<TUserItem> items, out TUserItem target, out TUserItem source)
        {
            items = null;
            target = null;
            source = null;
            if (targetClientItemId == sourceClientItemId)
                return PileItemContainer.None;

            if (series == 1)
            {
                if (m_HeroObject == null)
                    return PileItemContainer.None;
                items = m_HeroObject.m_ItemList;
                target = FindPileItem(items, targetClientItemId);
                source = FindPileItem(items, sourceClientItemId);
                if (target != null && source != null && IsPileItem(target))
                    return PileItemContainer.HeroBag;
            }
            else
            {
                items = m_ItemList;
                target = FindPileItem(items, targetClientItemId);
                source = FindPileItem(items, sourceClientItemId);
                if (target != null && source != null && IsPileItem(target))
                    return PileItemContainer.HumanBag;
            }

            items = m_StorageItemList;
            target = FindPileItem(items, targetClientItemId);
            source = FindPileItem(items, sourceClientItemId);
            return target != null && source != null && IsPileItem(target)
                ? PileItemContainer.Storage : PileItemContainer.None;
        }

        private PileItemContainer FindSplitItemContainer(int clientItemId, int series,
            out IList<TUserItem> items, out TUserItem source)
        {
            items = null;
            source = null;
            if (series == 1)
            {
                if (m_HeroObject == null)
                    return PileItemContainer.None;
                items = m_HeroObject.m_ItemList;
                source = FindPileItem(items, clientItemId);
                if (source != null && IsPileItem(source))
                    return PileItemContainer.HeroBag;
            }
            else
            {
                items = m_ItemList;
                source = FindPileItem(items, clientItemId);
                if (source != null && IsPileItem(source))
                    return PileItemContainer.HumanBag;
            }

            items = m_StorageItemList;
            source = FindPileItem(items, clientItemId);
            return source != null && IsPileItem(source)
                ? PileItemContainer.Storage : PileItemContainer.None;
        }

        private TUserItem FindPileItem(IEnumerable<TUserItem> items, int clientItemId)
        {
            return FindClientItemIn(items, clientItemId, false);
        }

        private static bool CanPileItems(TUserItem target, TUserItem source,
            out string failureMessage)
        {
            failureMessage = string.Empty;
            if (target == null || source == null || ReferenceEquals(target, source) ||
                target.wIndex != source.wIndex)
            {
                return false;
            }

            var targetStdItem = M2Share.UserEngine.GetStdItem(target.wIndex);
            var sourceStdItem = M2Share.UserEngine.GetStdItem(source.wIndex);
            if (!NativeItemFactory.IsPileItem(targetStdItem) ||
                !NativeItemFactory.IsPileItem(sourceStdItem))
            {
                return false;
            }

            if (!TryGetNativePileCompatibility(target, out var targetCompatibility) ||
                !TryGetNativePileCompatibility(source, out var sourceCompatibility))
            {
                return false;
            }
            if (targetCompatibility != sourceCompatibility)
            {
                failureMessage = "绑定物品不能与非绑定物品叠加";
                return false;
            }

            if ((targetStdItem.NativeReserved02 & 0x80) != 0 &&
                !HasCompatiblePileTimestamp(target, source))
            {
                failureMessage = "不同时效的物品不能叠加";
                return false;
            }

            return true;
        }

        private static bool TryGetNativePileCompatibility(TUserItem item, out ushort value)
        {
            value = 0;
            if (item?.btValue == null || item.btValue.Length < 12)
                return false;
            // Native item object +0x34 is TUserItem.btValue[10..11], not extension Bind.
            value = BinaryPrimitives.ReadUInt16LittleEndian(item.btValue.AsSpan(10, 2));
            return true;
        }

        private static bool HasCompatiblePileTimestamp(TUserItem target, TUserItem source)
        {
            var targetTimestamp = BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64LittleEndian(target.btValue.AsSpan(0, 8)));
            var sourceTimestamp = BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64LittleEndian(source.btValue.AsSpan(0, 8)));
            var minutes = Math.Round((sourceTimestamp - targetTimestamp) * 1440.0,
                MidpointRounding.ToEven);
            return double.IsFinite(minutes) && Math.Abs(minutes) <= 60.0;
        }

        private static bool IsPileItem(TUserItem item)
        {
            var stdItem = item == null ? null : M2Share.UserEngine.GetStdItem(item.wIndex);
            return NativeItemFactory.IsPileItem(stdItem);
        }

        private bool HasPileItemSpace(PileItemContainer container, int itemCount)
        {
            return container switch
            {
                PileItemContainer.Storage => itemCount < Math.Clamp(m_nStorageSpaceCount,
                    MIN_STORAGE_ITEM_COUNT, MAX_STORAGE_ITEM_COUNT),
                PileItemContainer.HumanBag => itemCount < BagCapacity.Of(this),
                PileItemContainer.HeroBag => m_HeroObject != null &&
                    itemCount < HeroObject.GetHeroBagCapacity(m_HeroObject.m_Abil.Level),
                _ => false
            };
        }

        private void WritePileItemLog(int action, TUserItem item,
            string relatedIdPrefix, int relatedMakeIndex)
        {
            var stdItem = item == null ? null : M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem == null)
                return;

            M2Share.AddGameDataLog(string.Join('\t', action, m_sMapName, m_nCurrX,
                m_nCurrY, m_sCharName, stdItem.Name, item.MakeIndex, item.Dura,
                relatedIdPrefix + relatedMakeIndex));
        }

        private void SendPileItemDuraChange(PileItemContainer container, TUserItem item)
        {
            switch (container)
            {
                case PileItemContainer.HumanBag:
                    SendDefMessage(Grobal2.SM_BAGITEMDURACHG, EnsureClientItemId(item), item.Dura, item.DuraMax, 0, "");
                    break;
                case PileItemContainer.HeroBag:
                    m_HeroObject.SendHeroBagItemDuraChange(item);
                    break;
                case PileItemContainer.Storage:
                    SendDefMessage(Grobal2.SM_STORAGEITEMDURACHG, EnsureClientItemId(item), item.Dura, item.DuraMax, 0, "");
                    break;
            }
        }

        private void SendPileItemDeleted(PileItemContainer container, TUserItem item)
        {
            var clientItemId = EnsureClientItemId(item);
            switch (container)
            {
                case PileItemContainer.HumanBag:
                    SendDefMessage(Grobal2.SM_DELITEM, clientItemId, 0, 0, 1, "");
                    break;
                case PileItemContainer.HeroBag:
                    m_HeroObject.SendHeroDelItem(item);
                    break;
                case PileItemContainer.Storage:
                    SendDefMessage(Grobal2.SM_DELITEM, clientItemId, 1, 0, 1, "");
                    break;
            }
        }

        private void SendStorageSplitItem(TUserItem item)
        {
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_STORAGE_ADDITEM,
                ObjectId, 0, 0, 1);
            SendSocket(defMsg, EncodeOwnedClientItemRecord(item));
        }
    }
}
