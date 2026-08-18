using System.Buffers.Binary;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        // THeroAct.Die -> THumanKind.Die calls sub_73FC70 before sub_740078.
        // These workers are intentionally separate from the monster drop pipeline.
        internal void NativeHeroDropUseItems(TBaseObject nativeLastHiter,
            TPlayObject nativeOwner)
        {
            if (m_btRaceServer != Grobal2.RC_HEROOBJECT)
                return;

            var deletedClientIds = new List<int>();
            var dropCount = 0;
            var featureChanged = false;
            var encounteredEquip = false;
            var redName = m_nPkPoint > M2Share.g_Config.nPKPunishPoint;
            var yanshen = new YanshenApi(null, null, M2Share.PluginManager);
            var patched = yanshen.TryGetDeathEquipDropPatch(redName, out _,
                out var patchedCap);
            var patchedK = patched
                ? (redName ? yanshen.RedNameK() : yanshen.NormalK())
                : 0;
            var denominator = NativeHeroDeathEquipDropDenominator(redName,
                nativeLastHiter, patched, patchedK);
            var cap = patched ? patchedCap : 2;

            for (var slot = 0; slot < 16; slot++)
            {
                var item = m_UseItems[slot];
                if (item == null)
                    continue;

                // sub_73FC70 @0x73FD3A..0x73FD42 sets [ebp-2] for every non-null
                // equipment object, before resolving its StdItem.
                encounteredEquip = true;
                if (item.wIndex <= 0)
                    continue;

                var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                if (stdItem == null)
                    continue;

                // 0x73FD46: Reserved02&8 destroys immediately, consumes no RNG,
                // and jumps past the normal count>cap test.
                if ((stdItem.NativeReserved02 & 0x0008) != 0)
                {
                    var clientItemId = item.ClientItemID;
                    deletedClientIds.Add(clientItemId);
                    m_UseItems[slot] = null;
                    RecalcAbilitys();
                    if (nativeOwner != null && !nativeOwner.m_boGhost)
                    {
                        nativeOwner.SendDefMessage(Grobal2.SM_HERO_DELITEM,
                            clientItemId, 0, 0, 1, string.Empty);
                    }
                    M2Share.AddNativeGameDataLog(this, 0x0A, stdItem.Name,
                        item.MakeIndex, 1, "死亡爆出消失");

                    if (slot is 0 or 1 or 4 or 13)
                    {
                        FeatureChanged();
                        featureChanged = true;
                    }

                    Dispose(item);
                    dropCount++;
                    continue;
                }

                // 0x73FD99: ClassFc is tested only after every eligible slot has
                // consumed Random(K); it bypasses a non-zero draw, not the draw itself.
                if (M2Share.RandomNumber.Random(denominator) != 0
                    && item.NativeClassFc == 0)
                {
                    continue;
                }

                // Race 54 jumps over the player auth/gift/mode-5 destruction arm.
                if ((stdItem.NativeReserved02 & 0x0010) != 0)
                    continue;

                if (!DropItemDown(item, 2, true, null, this))
                    continue;

                m_UseItems[slot] = null;
                if (slot is 0 or 1 or 4 or 13)
                    featureChanged = true;

                var ordinaryClientItemId = item.ClientItemID;
                deletedClientIds.Add(ordinaryClientItemId);

                TryNotifyNativeItemMovementSms(nativeOwner, stdItem, item,
                    NativeItemMovementSmsDeathEvent);

                dropCount++;
                if (dropCount > cap)
                    break;
            }

            QueueNativeDeletedItems(deletedClientIds);
            if (featureChanged)
                FeatureChanged();

            // 0x73FFB3 any-equip gate; 0x73FFBD resolves hero owner through VMT+B4;
            // 0x73FFC3 checks the owner's bag count before calling sub_73E4C4.
            if (encounteredEquip && nativeOwner?.m_ItemList.Count > 0)
                TryNativeDeathDropAreaNotice(dropCount, nativeOwner);
        }

        internal void NativeHeroScatterBagItems(TPlayObject nativeOwner)
        {
            if (m_btRaceServer != Grobal2.RC_HEROOBJECT)
                return;

            var deletedClientIds = new List<int>();
            var redName = m_nPkPoint >= M2Share.g_Config.nPKPunishPoint;
            for (var index = m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = m_ItemList[index];
                if (item == null || item.wIndex <= 0)
                    continue;

                // 0x7400E9: ClassFc jumps straight to the ground branch and bypasses
                // Random(3) plus all three Reserved/bind filters.
                if (item.NativeClassFc == 0)
                {
                    if (!redName && M2Share.RandomNumber.Random(3) != 0)
                        continue;

                    var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                    if (stdItem == null
                        || (stdItem.NativeReserved02 & 0x0010) != 0
                        || (stdItem.NativeReserved02 & 0x0200) != 0
                        || ((stdItem.NativeReserved02 & 0x4000) != 0
                            && NativeItemAcquisitionStamp.ReadBindWord(item) == 1))
                    {
                        continue;
                    }
                }

                // Race 54 bypasses the player auth/gift/mode-5 destruction arm.
                if (!DropItemDown(item, 2, true, null, this))
                    continue;

                deletedClientIds.Add(item.ClientItemID);
                m_ItemList.RemoveAt(index);
            }

            QueueNativeDeletedItems(deletedClientIds);
        }

        internal int NativeHeroDeathEquipDropDenominator(bool redName,
            TBaseObject nativeLastHiter, bool patched, int patchedK)
        {
            if (!patched)
                return NativeDeathEquipDropDenominator(redName, nativeLastHiter);

            // The Eye patch replaces only the stock 21/90 immediates. The host's
            // non-red +0x18C base and LastHiter +0x579 subtraction still execute.
            var denominator = redName
                ? patchedK
                : unchecked(m_nNativeDropRareBase + patchedK);
            if (nativeLastHiter != null && nativeLastHiter.IsNativeHumanKind())
            {
                denominator = unchecked(denominator
                    - nativeLastHiter.m_btNativeDropRareKillerBonus);
            }
            return denominator < 0 ? 0 : denominator;
        }

        internal static byte[] BuildNativeHeroDeletedItemBody(IList<int> clientItemIds)
        {
            var body = new byte[clientItemIds.Count * sizeof(int)];
            for (var i = 0; i < clientItemIds.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    body.AsSpan(i * sizeof(int), sizeof(int)), clientItemIds[i]);
            }
            return body;
        }

        private void QueueNativeDeletedItems(IList<int> clientItemIds)
        {
            if (clientItemIds.Count == 0)
                return;

            var body = BuildNativeHeroDeletedItemBody(clientItemIds);
            SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                clientItemIds.Count, 0, 0, string.Empty, body);
        }
    }
}
