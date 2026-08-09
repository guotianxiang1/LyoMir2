using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string GoldActRewardCompleteMessage =
            "祝贺您，您已经获得了相应等级的奖励物品，请查看包裹吧\\如果没有找到的话，请留出足够的包裹空间，再次领取";
        internal const string GoldActRewardAlreadyClaimedMessage =
            "您已经领取过了该等级的奖励，不能再次领取";
        internal const string GoldActRewardLevelTooLowMessage =
            "您的等级尚未达到46级，还不能领取热血勇士的奖励";
        internal const string GoldActRewardNotActivatedMessage =
            "您还没有成为热血勇士，不能领取奖励物品";

        public void ReqItemByGoldAct(NormNpc npc)
        {
            if (npc == null) return;

            var message = RunNativeGoldActRewardStateMachine(
                TryGrantNativeGoldActReward);
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                npc.m_sCharName + '/' + message);
        }

        internal string RunNativeGoldActRewardStateMachine(
            Func<int, bool> tryGrantReward)
        {
            if (m_btGoldActNextLevel == 0)
                return GoldActRewardNotActivatedMessage;

            var currentLevel = Math.Min((int)m_Abil.Level, 55);
            if (currentLevel < 46)
                return GoldActRewardLevelTooLowMessage;

            var nextLevel = Math.Max((int)m_btGoldActNextLevel, 46);
            if (currentLevel < nextLevel)
                return GoldActRewardAlreadyClaimedMessage;

            for (var rewardLevel = nextLevel;
                 rewardLevel <= currentLevel;
                 rewardLevel++)
            {
                if (!tryGrantReward(rewardLevel)) break;
                m_btGoldActNextLevel = (byte)(rewardLevel + 1);
            }
            return GoldActRewardCompleteMessage;
        }

        private bool TryGrantNativeGoldActReward(int rewardLevel)
        {
            var rewards = M2Share.GoldActRewards;
            var poolNumber = rewardLevel - 45;
            if (rewards == null ||
                !rewards.Pools.TryGetValue(poolNumber, out var pool) ||
                pool.Count == 0 || M2Share.UserEngine == null)
                return false;

            var itemName = pool[M2Share.RandomNumber.Random(pool.Count)];
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
            var canonicalItemName = stdItem.Name;
            // The Delphi vtable call receives the packed native color 0x38FF
            // directly.  Bypass configurable SysMsg colors/prefixes so the
            // wire fields remain identical to the original engine.
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                "恭喜: 你领取到" + canonicalItemName);
            M2Share.AddGameDataLog(string.Join('\t', 55, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, canonicalItemName,
                item.MakeIndex, 1, "热血勇士领取"));
            return true;
        }
    }
}
