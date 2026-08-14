using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // sub_6C8284 @0x006C8284 — platinum role level-segment prize claim
        // sub_786878 @0x00786878 — platinum role upgrade

        internal const string PlatinumRewardSuccessPrefix =
            "恭喜您：领取到了第 ";
        internal const string PlatinumRewardSuccessSuffix = " 级白金角色奖品";
        internal const string PlatinumRewardUnknownErrorMessage =
            "未知错误，请稍后再尝试...";
        internal const string PlatinumRewardAllClaimedMessage =
            "您已经领取过白金角色的全部奖励";
        internal const string PlatinumRewardBagFullMessage =
            "您的包裹栏太满，不能领取";
        internal const string PlatinumRewardAlreadyClaimedMessage =
            "我记得这个等级的奖品您已经领取过了啊！";
        internal const string PlatinumRewardNotEligibleMessage =
            "您不是白金角色，或您已经领取过白金角色的所有奖励。";
        internal const string PlatinumUpgradeSuccessMessage =
            "本角色成功升级为白金角色！";
        internal const string PlatinumUpgradeFailureMessage =
            "你无法继续升级为白金角色";

        /// <summary>
        /// Native reqitembyplatina script/NPC entry (sub_6C8284 @0x006C8284).
        /// </summary>
        public void ReqItemByPlatina(NormNpc npc)
        {
            if (npc == null) return;
            var message = RunNativePlatinumRewardClaim();
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                npc.m_sCharName + '/' + message);
        }

        /// <summary>
        /// sub_786878 @0x00786878 — sets PlatLv=1 when eligible.
        /// </summary>
        internal bool TryUpgradeNativePlatinumRole(bool accountEligible)
        {
            if (!accountEligible)
                return false;

            if (m_btPlatLv == 0)
            {
                m_btPlatLv = 1;
                SysMsg(PlatinumUpgradeSuccessMessage, MsgColor.Green,
                    MsgType.Hint);
                return true;
            }

            SysMsg(PlatinumUpgradeFailureMessage, MsgColor.Red, MsgType.Hint);
            return false;
        }

        internal string RunNativePlatinumRewardClaim()
        {
            // 0x006C82BD: PlatLv must be 1..10 (dec; sub 0xA; jae fail)
            if (m_btPlatLv == 0 || m_btPlatLv > 10)
                return PlatinumRewardNotEligibleMessage;

            // 0x006C82D4: requiredLevel = PlatLv + 0x32 (50)
            var requiredLevel = m_btPlatLv + 50;
            if (m_Abil.Level < requiredLevel)
                return PlatinumRewardAlreadyClaimedMessage;

            if (!HasNativeRewardBagSpace())
                return PlatinumRewardBagFullMessage;

            var poolIndex = m_btPlatLv;
            if (!TrySelectNativePlatinumPrize(poolIndex, out var itemName) ||
                string.IsNullOrEmpty(itemName))
                return PlatinumRewardUnknownErrorMessage;

            if (!TryGrantNativeNamedReward(itemName, "白金角色领取"))
                return PlatinumRewardBagFullMessage;

            m_btPlatLv++;
            return PlatinumRewardSuccessPrefix + requiredLevel +
                   PlatinumRewardSuccessSuffix;
        }

        private bool TrySelectNativePlatinumPrize(int poolIndex,
            out string itemName)
        {
            itemName = null;
            var pools = NativeRewardConfigLoaders.PlatinumPrizePools;
            if (pools == null ||
                !pools.TryGetValue(poolIndex, out var pool) ||
                pool == null || pool.Count == 0)
                return false;

            itemName = pool[M2Share.RandomNumber.Random(pool.Count)];
            return !string.IsNullOrEmpty(itemName);
        }

        private bool HasNativeRewardBagSpace()
        {
            const int nativeBagCapacity = 0x30;
            return (m_ItemList?.Count ?? int.MaxValue) < nativeBagCapacity;
        }

        private bool TryGrantNativeNamedReward(string itemName, string logReason)
        {
            if (M2Share.UserEngine == null ||
                string.IsNullOrEmpty(itemName))
                return false;

            TUserItem item = null;
            if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref item) ||
                item == null)
                return false;

            var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem == null)
            {
                Dispose(item);
                return false;
            }

            if (!AddItemToBag(item))
            {
                Dispose(item);
                return false;
            }

            SendAddItem(item);
            SysMsg("恭喜: 你领取到" + stdItem.Name, MsgColor.Green, MsgType.Hint);
            M2Share.AddGameDataLog(string.Join('\t', 55, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, stdItem.Name,
                item.MakeIndex, 1, logReason));
            return true;
        }
    }
}
