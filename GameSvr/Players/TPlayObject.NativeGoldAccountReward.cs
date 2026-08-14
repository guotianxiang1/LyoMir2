using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // sub_653990 @0x00653990 — gold account batch claim dispatcher
        // sub_6D0C34 @0x006D0C34 — single-level gold account claim

        internal const string GoldAccountNotGoldMessage =
            "[失败]：该帐号不是金牌帐号。";
        internal const string GoldAccountUnknownErrorMessage =
            "[失败]：未知错误。";
        internal const string GoldAccountAlreadyClaimedMessage =
            "[失败]：本帐号已经领取过该等级段的物品";

        // Native Self+0xB76 / Self+0xB7C (batch claim gate in sub_653990).
        public byte m_btGoldAccountFlag;
        public byte m_btGoldAccountNextLevel;

        /// <summary>
        /// Native reqitembygoldid script entry (sub_653990 @0x00653990).
        /// </summary>
        public void ReqItemByGoldId(NormNpc npc, int startLevel, int endLevel)
        {
            if (npc == null) return;
            var message = RunNativeGoldAccountRewardBatch(startLevel, endLevel);
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                npc.m_sCharName + '/' + message);
        }

        internal string RunNativeGoldAccountRewardBatch(int startLevel,
            int endLevel)
        {
            if (m_btGoldAccountFlag == 0)
                return GoldAccountNotGoldMessage;
            if (m_btGoldAccountNextLevel == 0)
                return GoldAccountUnknownErrorMessage;

            var effectiveStart = Math.Max(startLevel, m_btGoldAccountNextLevel);
            var playerLevel = GetNativeGoldAccountMappedLevel();
            if (effectiveStart > endLevel || effectiveStart > playerLevel)
                return GoldAccountAlreadyClaimedMessage;

            var claimedAny = false;
            for (var level = effectiveStart; level <= endLevel; level++)
            {
                if (level > playerLevel)
                    break;
                if (!TryClaimNativeGoldAccountLevel(level))
                    break;
                m_btGoldAccountNextLevel = (byte)(level + 1);
                claimedAny = true;
            }

            return claimedAny ? string.Empty : GoldAccountUnknownErrorMessage;
        }

        /// <summary>
        /// sub_6D0C34 @0x006D0C34 — claim one configured GoldID pool (level 3..11).
        /// </summary>
        internal bool TryClaimNativeGoldAccountLevel(int level)
        {
            if (m_btGoldAccountFlag == 0)
                return false;

            m_btGoldAccountNextLevel = 0;
            var poolIndex = level - 2;
            var rewards = M2Share.GoldIDRewards;
            if (rewards == null ||
                !rewards.Pools.TryGetValue(poolIndex, out var pool) ||
                pool == null || pool.Count == 0)
                return false;

            if (!HasNativeRewardBagSpace())
                return false;

            var itemName = pool[M2Share.RandomNumber.Random(pool.Count)];
            return TryGrantNativeNamedReward(itemName, "金牌帐号领取");
        }

        /// <summary>
        /// sub_6C81C0 @0x006C81C0 — maps player level to gold-account tier (cap 49).
        /// </summary>
        internal int GetNativeGoldAccountMappedLevel()
        {
            var level = Math.Min((int)m_Abil.Level, 0x31);
            return level switch
            {
                <= 22 => 1,
                <= 27 => 2,
                <= 31 => 3,
                <= 35 => 4,
                <= 39 => 5,
                <= 43 => 6,
                <= 47 => 7,
                <= 49 => 8,
                _ => 9
            } + 2;
        }
    }
}
