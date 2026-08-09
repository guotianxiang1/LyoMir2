using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeHelmetSuccessMessage =
            "恭喜：你获得了 黄金头盔 ";
        internal const string NativeHelmetCooldownMessage =
            "您刚升级过，请等一分钟后，再试...";
        internal const string NativeHelmetMissingMaterialsMessage =
            "您的基本配方材料不足，无法升级！";
        internal const string NativeHelmetFailureMessage =
            "很遗憾，升级失败，所有材料均已被消耗！";

        private const int NativeHelmetCooldownMilliseconds = 60_000;
        private const string NativeHelmetRewardName = "黄金头盔";

        private static readonly (string Name, int Count)[]
            s_nativeHelmetRequiredMaterials =
            {
                ("黑铁头盔(极品)", 1),
                ("真视秘籍", 1),
                ("地苦胆", 1),
                ("四叶参", 1),
                ("天工之锤", 2)
            };

        private static readonly (string Name, int Count)[]
            s_nativeHelmetBonusJewelry =
            {
                ("骑士手镯", 2),
                ("灵魂项链", 1),
                ("恶魔铃铛", 4),
                ("龙之戒指", 3),
                ("思贝儿手镯", 4),
                ("三眼手镯", 1)
            };

        private static readonly string[] s_nativeHelmetSpecialHelmets =
        {
            "圣战头盔", "天尊头盔", "法神头盔"
        };

        private int _nativeHelmetUpgradeTick;

        internal NativeHelmetUpgradeResult UpgradeNativeHelmet(NormNpc npc)
        {
            var result = RunNativeHelmetUpgrade(HUtil32.GetTickCount(),
                M2Share.RandomNumber.Random);
            switch (result)
            {
                case NativeHelmetUpgradeResult.Success:
                    SendNativeHelmetDialog(npc, NativeHelmetSuccessMessage);
                    break;
                case NativeHelmetUpgradeResult.Cooldown:
                    SendNativeHelmetDialog(npc, NativeHelmetCooldownMessage);
                    break;
                case NativeHelmetUpgradeResult.MissingMaterials:
                    SendNativeHelmetDialog(npc,
                        NativeHelmetMissingMaterialsMessage);
                    break;
                case NativeHelmetUpgradeResult.Failed:
                    SendNativeHelmetDialog(npc, NativeHelmetFailureMessage);
                    break;
                default:
                    CloseNativeHelmetDialog(npc);
                    break;
            }
            return result;
        }

        internal NativeHelmetUpgradeResult RunNativeHelmetUpgrade(
            int currentTick, Func<int, int> random)
        {
            if (unchecked((uint)(currentTick - _nativeHelmetUpgradeTick)) <
                NativeHelmetCooldownMilliseconds)
                return NativeHelmetUpgradeResult.Cooldown;

            _nativeHelmetUpgradeTick = currentTick;
            foreach (var material in s_nativeHelmetRequiredMaterials)
            {
                if (CountNativeHelmetNamedItems(material.Name) <
                    material.Count)
                    return NativeHelmetUpgradeResult.MissingMaterials;
            }

            foreach (var material in s_nativeHelmetRequiredMaterials)
            {
                if (ClearNativeHelmetNamedItems(material.Name) <
                    material.Count)
                    return NativeHelmetUpgradeResult.MissingMaterials;
            }

            var successThreshold = 10;
            foreach (var jewelry in s_nativeHelmetBonusJewelry)
            {
                if (ClearNativeHelmetNamedItems(jewelry.Name) ==
                    jewelry.Count)
                    continue;
                successThreshold = 5;
                break;
            }

            var hasSpecialHelmet = false;
            foreach (var helmetName in s_nativeHelmetSpecialHelmets)
            {
                if (ClearNativeHelmetNamedItems(helmetName) > 0)
                    hasSpecialHelmet = true;
            }

            ClearNativeHelmetJewelry();
            if (!hasSpecialHelmet) successThreshold = 1;
            random ??= M2Share.RandomNumber.Random;
            if (random(100) > successThreshold)
                return NativeHelmetUpgradeResult.Failed;

            TUserItem reward = null;
            if (M2Share.UserEngine == null ||
                !M2Share.UserEngine.CopyToUserItemFromName(
                    NativeHelmetRewardName, ref reward) || reward == null)
                return NativeHelmetUpgradeResult.RewardCreateFailed;

            if (!AddItemToBag(reward))
            {
                Dispose(reward);
                return NativeHelmetUpgradeResult.BagFull;
            }

            SendAddItem(reward);
            M2Share.AddGameDataLog(string.Join('\t', 9, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, NativeHelmetRewardName,
                reward.MakeIndex, 1, "系统给予"));
            return NativeHelmetUpgradeResult.Success;
        }

        private int CountNativeHelmetNamedItems(string itemName)
        {
            var itemIndex = M2Share.UserEngine?.GetStdItemIdx(itemName) ?? -1;
            if (itemIndex <= 0 || itemIndex > ushort.MaxValue) return 0;

            var definition = M2Share.UserEngine.GetStdItem(itemIndex);
            var count = 0;
            for (var index = 0; index < m_ItemList.Count; index++)
            {
                var item = m_ItemList[index];
                if (item == null || item.wIndex != itemIndex) continue;
                count = unchecked(count +
                    (definition?.StdMode == 7 ? item.Dura : 1));
            }
            return count;
        }

        private int ClearNativeHelmetNamedItems(string itemName)
        {
            var itemIndex = M2Share.UserEngine?.GetStdItemIdx(itemName) ?? -1;
            if (itemIndex <= 0 || itemIndex > ushort.MaxValue) return 0;

            var removed = 0;
            for (var index = m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = m_ItemList[index];
                if (item == null || item.wIndex != itemIndex) continue;
                RemoveNativeHelmetItem(index, item,
                    M2Share.UserEngine.GetStdItem(item.wIndex));
                removed++;
            }
            if (removed != 0) WeightChanged();
            return removed;
        }

        private void ClearNativeHelmetJewelry()
        {
            var removed = false;
            for (var index = m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = m_ItemList[index];
                var definition = item == null
                    ? null
                    : M2Share.UserEngine?.GetStdItem(item.wIndex);
                if (!IsNativeHelmetJewelry(definition)) continue;
                RemoveNativeHelmetItem(index, item, definition);
                removed = true;
            }
            if (removed) WeightChanged();
        }

        internal static bool IsNativeHelmetJewelry(GoodItem definition)
        {
            if (definition == null) return false;
            var stdMode = definition.StdMode;
            if (stdMode < 19 || (stdMode > 24 && stdMode != 26)) return false;
            if (definition.StdMode == 22 &&
                (definition.Shape is >= 111 and <= 114 or 118))
                return false;
            return definition.StdMode != 20 ||
                   definition.Shape is < 120 or > 121;
        }

        private void RemoveNativeHelmetItem(int index, TUserItem item,
            GoodItem definition)
        {
            m_ItemList.RemoveAt(index);
            SendDelItems(item);
            M2Share.AddGameDataLog(string.Join('\t', 10, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName,
                definition?.Name ?? string.Empty,
                unchecked((uint)item.MakeIndex), 1, "系统收取"));
            Dispose(item);
        }

        private void SendNativeHelmetDialog(NormNpc npc, string message)
        {
            if (npc == null) return;
            m_NPC = npc;
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc.m_sCharName ?? string.Empty) + "/" + message);
        }

        private void CloseNativeHelmetDialog(NormNpc npc)
        {
            if (npc == null) return;
            SendMsg(npc, Grobal2.RM_MERCHANTDLGCLOSE, 0, npc.ObjectId, 0, 0,
                string.Empty);
        }
    }

    internal enum NativeHelmetUpgradeResult
    {
        Success = 0,
        Cooldown = 1,
        MissingMaterials = 2,
        Failed = 3,
        RewardCreateFailed = 4,
        BagFull = 5
    }
}
