using System;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeMagicTowerMoveTokenName = "弩牌";
        internal const string NativeMagicTowerMoveTokenRequiredMessage =
            "移动弓箭手需要1个弩牌，你没有足够的弩牌。";
        internal const string NativeMagicTowerMoveMissingArcherMessage =
            "您选择的位置没有弓箭手，请重新选择。";

        internal bool GetNativeMagicTowerMoveChance(NormNpc npc, int index)
        {
            if (npc == null || !npc.HasNativePasProperty(12)) return false;

            lock (m_CreditCard.SyncRoot)
            {
                if (!TryGetNativeMagicTowerArcherCoordinates(index, out var x,
                        out var y) || !HasNativeMagicTowerArcher(index))
                {
                    SendNativeMagicTowerMoveDialog(npc,
                        NativeMagicTowerMoveMissingArcherMessage);
                    return false;
                }

                if (!TryTakeNativeMagicTowerMoveToken(npc))
                {
                    SendNativeMagicTowerMoveDialog(npc,
                        NativeMagicTowerMoveTokenRequiredMessage);
                    return false;
                }

                var archer = m_PEnvir?.GetMovingObject(x, y, true) as TBaseObject;
                if (archer == null ||
                    archer.m_btRaceServer != NativeMagicTowerArcherRace)
                    return false;

                m_btNativeMagicTowerArcherSlots[index - 1] = 0;
                archer.MakeGhost();
                m_btNativeMagicTowerEngageChance = 1;
                m_sbNativeMagicTowerArcherCount = unchecked(
                    (sbyte)(m_sbNativeMagicTowerArcherCount - 1));
                if (m_sbNativeMagicTowerArcherCount < 0)
                    m_sbNativeMagicTowerArcherCount = 0;
                return true;
            }
        }

        private bool TryTakeNativeMagicTowerMoveToken(NormNpc npc)
        {
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return false;

            lock (m_ItemList)
            {
                var takes = new List<NativeMagicTowerMoveTokenTake>();
                var found = 0;
                for (var itemIndex = m_ItemList.Count - 1;
                     itemIndex >= 0 && found < 1;
                     itemIndex--)
                {
                    var item = m_ItemList[itemIndex];
                    if (item == null || item.wIndex == 0) continue;
                    var standardItem = userEngine.GetStdItem(item.wIndex);
                    if (standardItem == null || !string.Equals(
                            standardItem.Name, NativeMagicTowerMoveTokenName,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (standardItem.StdMode == 7)
                    {
                        var remaining = 1 - found;
                        if (remaining >= item.Dura)
                        {
                            takes.Add(new NativeMagicTowerMoveTokenTake(
                                itemIndex, item, standardItem.Name, item.Dura,
                                true, true));
                            found = unchecked(found + item.Dura);
                        }
                        else
                        {
                            takes.Add(new NativeMagicTowerMoveTokenTake(
                                itemIndex, item, standardItem.Name, remaining,
                                true, false));
                            found = 1;
                        }
                    }
                    else
                    {
                        takes.Add(new NativeMagicTowerMoveTokenTake(itemIndex,
                            item, standardItem.Name, 1, false, true));
                        found++;
                    }
                }

                if (found < 1) return false;

                var npcName = npc.m_sCharName ?? string.Empty;
                foreach (var take in takes)
                {
                    if (take.WholeItem)
                    {
                        m_ItemList.RemoveAt(take.ItemIndex);
                    }
                    else
                    {
                        take.Item.Dura = unchecked(
                            (ushort)(take.Item.Dura - take.Quantity));
                    }

                    var description = take.PileItem
                        ? npcName + "收取" + take.Quantity + "个"
                        : npcName;
                    AddNativeMagicTowerMoveTokenLog(take.ItemName,
                        take.Item.MakeIndex, take.Quantity, description);

                    if (take.WholeItem)
                    {
                        SendDelItems(take.Item);
                        Dispose(take.Item);
                    }
                    else
                    {
                        SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                            EnsureClientItemId(take.Item), take.Item.Dura,
                            take.Item.DuraMax, 0, string.Empty);
                    }
                }
            }

            WeightChanged();
            return true;
        }

        private void AddNativeMagicTowerMoveTokenLog(string itemName,
            int makeIndex, int quantity, string description)
        {
            M2Share.AddGameDataLog(string.Join('\t', 10, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, itemName,
                unchecked((uint)makeIndex), quantity, description));
        }

        private void SendNativeMagicTowerMoveDialog(NormNpc npc,
            string message)
        {
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc.m_sCharName ?? string.Empty) + "/" + message);
        }

        private readonly struct NativeMagicTowerMoveTokenTake
        {
            internal NativeMagicTowerMoveTokenTake(int itemIndex,
                TUserItem item, string itemName, int quantity, bool pileItem,
                bool wholeItem)
            {
                ItemIndex = itemIndex;
                Item = item;
                ItemName = itemName;
                Quantity = quantity;
                PileItem = pileItem;
                WholeItem = wholeItem;
            }

            internal int ItemIndex { get; }
            internal TUserItem Item { get; }
            internal string ItemName { get; }
            internal int Quantity { get; }
            internal bool PileItem { get; }
            internal bool WholeItem { get; }
        }
    }
}
