using System;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    internal enum NativeMagicShieldUpgradeOutcome
    {
        Job,
        Level,
        MagicLevel,
        Finished,
        Item,
        Success
    }

    public partial class TPlayObject
    {
        internal const int NativeMagicShieldMagicId = 31;
        internal const string NativeMagicShieldWineName = "高粱酒";
        internal const string NativeMagicShieldBookName = "白日门魔法盾";

        internal NativeMagicShieldUpgradeOutcome UpgradeNativeMagicShield(
            bool heroUpgrade)
        {
            TBaseObject magicOwner = this;
            if (heroUpgrade)
            {
                var hero = m_HeroObject;
                if (hero == null || hero.m_boGhost || hero.m_btJob != 1)
                    return NativeMagicShieldUpgradeOutcome.Job;
                magicOwner = hero;
            }
            else if (m_btJob != 1)
            {
                return NativeMagicShieldUpgradeOutcome.Job;
            }

            if (magicOwner.m_Abil.Level < 40)
                return NativeMagicShieldUpgradeOutcome.Level;

            var magic = magicOwner.GetMagicInfo(NativeMagicShieldMagicId);
            if (magic == null || GetNativeMagicShieldEffectiveLevel(magic) < 3)
                return NativeMagicShieldUpgradeOutcome.MagicLevel;
            if (GetNativeMagicShieldEffectiveLevel(magic) == 4)
                return NativeMagicShieldUpgradeOutcome.Finished;

            if (!TrySelectNativeMagicShieldMaterials(m_ItemList,
                    GetNativeMagicShieldItemName, out var materials))
                return NativeMagicShieldUpgradeOutcome.Item;

            var deletedItems = new List<TDeleteItem>(materials.Length);
            string logLabel = heroUpgrade
                ? "学习白日门四级盾"
                : "学习四级盾";
            foreach (var item in materials)
            {
                m_ItemList.Remove(item);
                string itemName = GetNativeMagicShieldItemName(item);
                deletedItems.Add(new TDeleteItem
                {
                    sItemName = itemName,
                    MakeIndex = item.MakeIndex,
                    ClientItemID = EnsureClientItemId(item)
                });
                M2Share.AddGameDataLog(string.Join('\t', 10, m_sMapName,
                    m_nCurrX, m_nCurrY, m_sCharName, itemName,
                    unchecked((uint)item.MakeIndex), 1, logLabel));
                Dispose(item);
            }

            SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                deletedItems.Count, 0, 0, string.Empty, deletedItems);
            WeightChanged();

            SetNativeMagicShieldLevel(magic);
            if (magicOwner is HeroObject heroOwner)
                QueueNativeHeroMagicShieldSnapshot(heroOwner, magic);
            else
                QueueNativeMagicTrainingSnapshot(magic,
                    unchecked((uint)HUtil32.GetTickCount()));

            return NativeMagicShieldUpgradeOutcome.Success;
        }

        internal static int GetNativeMagicShieldEffectiveLevel(
            TUserMagic magic)
        {
            return Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        internal static void SetNativeMagicShieldLevel(TUserMagic magic)
        {
            magic.btLevel = Math.Min((byte)4, magic.MagicInfo.btTrainLv);
        }

        internal static bool TrySelectNativeMagicShieldMaterials(
            IList<TUserItem> items, Func<TUserItem, string> nameResolver,
            out TUserItem[] materials)
        {
            TUserItem wine = null;
            var books = new List<TUserItem>(3);
            if (items != null && nameResolver != null)
            {
                for (var index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    if (item == null) continue;

                    string itemName = nameResolver(item);
                    if (string.Equals(itemName, NativeMagicShieldWineName,
                            StringComparison.Ordinal) &&
                        item.btValue != null && item.btValue.Length > 1 &&
                        item.btValue[1] >= 7)
                    {
                        wine = item;
                        continue;
                    }

                    if (books.Count < 3 && string.Equals(itemName,
                            NativeMagicShieldBookName,
                            StringComparison.Ordinal))
                        books.Add(item);
                }
            }

            if (wine == null || books.Count != 3)
            {
                materials = Array.Empty<TUserItem>();
                return false;
            }

            materials = new[] { wine, books[0], books[1], books[2] };
            return true;
        }

        private static string GetNativeMagicShieldItemName(TUserItem item)
        {
            return M2Share.UserEngine?.GetStdItemName(item.wIndex) ??
                   string.Empty;
        }

        private static int GetNativeMagicShieldRequiredTraining(
            TUserMagic magic)
        {
            if (magic?.MagicInfo?.MaxTrain == null || magic.btLevel >= 3 ||
                magic.btLevel >= magic.MagicInfo.MaxTrain.Length)
                return -1;
            return magic.MagicInfo.MaxTrain[magic.btLevel];
        }

        private static void QueueNativeHeroMagicShieldSnapshot(
            HeroObject hero, TUserMagic magic)
        {
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                var index = 0;
                while (index < hero.m_MsgList.Count)
                {
                    var message = hero.m_MsgList[index];
                    if (message.wIdent != Grobal2.RM_MAGIC_LVEXP ||
                        !message.boLateDelivery)
                    {
                        index++;
                        continue;
                    }

                    if (message.nParam1 == magic.MagicInfo.wMagicID)
                    {
                        hero.m_MsgList.RemoveAt(index);
                        hero.Dispose(message);
                        continue;
                    }

                    message.dwDeliveryTime = 0;
                    message.boLateDelivery = false;
                    hero.m_MsgList[index] = message;
                    index++;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(
                    M2Share.ProcessMsgCriticalSection);
            }

            int effectiveLevel = GetNativeMagicShieldEffectiveLevel(magic);
            hero.SendDelayMsg(hero, Grobal2.RM_MAGIC_LVEXP, 0,
                magic.MagicInfo.wMagicID, effectiveLevel, magic.nTranPoint,
                string.Empty, (int)NativeMagicTrainingFlushDelay,
                BitConverter.GetBytes(
                    GetNativeMagicShieldRequiredTraining(magic)));
        }
    }
}
