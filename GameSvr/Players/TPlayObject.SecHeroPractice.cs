using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int SecHeroPracticeInterval = 10_000;
        private const int SecHeroPracticeGoldCost = 50;
        private const int SecHeroPracticeLingFuReason = 30_010;
        private const int SecHeroPracticeHintForeground = 0xFF;
        private const int SecHeroPracticeBlueBackground = 0xFC;
        private const int SecHeroPracticeRedBackground = 0x38;

        internal int m_dwSecHeroPracticeTick;
        internal int m_nSecHeroPracticeLingFuUsed;

        internal void ClientSecHeroPractice(byte rewardMode, byte costTier)
        {
            var result = 0;
            var hero = m_HeroObject;
            if (hero != null && hero.HeroType == 2 && costTier > 0)
            {
                result = -1;
                if ((m_NPC as NormNpc)?.TryCallPascalCallback(this,
                        "SecHeroPracticeCallBack") == true)
                {
                    var previousRewardMode = m_btSecHeroPracticeRewardMode;
                    var previousCostTier = m_btSecHeroPracticeCostTier;
                    var previousLevel = m_wSecHeroPracticeLevel;
                    m_btSecHeroPracticeRewardMode = rewardMode;
                    m_btSecHeroPracticeCostTier = costTier;
                    m_wSecHeroPracticeLevel = unchecked((ushort)hero.m_Abil.Level);
                    if (M2Share.UserEngine?.RemoveHero(this) == true)
                    {
                        result = 1;
                        SendDefMessage(Grobal2.SM_HERO_LOGOUT, 0, 0, 0, 0,
                            string.Empty);
                        SendSecHeroPracticeMessage(
                            "英雄放养开始，如您再召唤出您的副将英雄后将自动停止！",
                            SecHeroPracticeBlueBackground);
                    }
                    else
                    {
                        m_btSecHeroPracticeRewardMode = previousRewardMode;
                        m_btSecHeroPracticeCostTier = previousCostTier;
                        m_wSecHeroPracticeLevel = previousLevel;
                    }
                }
            }

            SendDefMessage(Grobal2.SM_SECHERO_PRACTICE, result,
                0, 0, 0, string.Empty);
        }

        internal void ResumeSecHeroPracticeAfterLogon()
        {
            if ((uint)(m_btSecHeroPracticeCostTier - 1) < 3)
            {
                SendSecHeroPracticeMessage("您的副将英雄已自动开始继续修炼!",
                    SecHeroPracticeBlueBackground);
            }
        }

        internal void StopSecHeroPractice()
        {
            ClearSecHeroPractice();
            SendSecHeroPracticeMessage("您的副将英雄放养已结束！",
                SecHeroPracticeBlueBackground);
        }

        private void ClearSecHeroPractice()
        {
            m_btSecHeroPracticeCostTier = 0;
            m_btSecHeroPracticeRewardMode = 0;
        }

        internal void RunSecHeroPracticeTimer(int nowTick)
        {
            if (!HasSecHeroPracticeIntervalElapsed(nowTick,
                    m_dwSecHeroPracticeTick))
                return;

            m_dwSecHeroPracticeTick = nowTick;
            ProcessSecHeroPractice();
        }

        internal static bool HasSecHeroPracticeIntervalElapsed(int nowTick,
            int lastTick)
        {
            var elapsed = unchecked(nowTick - lastTick);
            var absoluteElapsed = elapsed < 0 ? unchecked(-elapsed) : elapsed;
            return absoluteElapsed > SecHeroPracticeInterval;
        }

        private void ProcessSecHeroPractice()
        {
            var costTier = m_btSecHeroPracticeCostTier;
            if ((uint)(costTier - 1) >= 3)
                return;

            if (m_nGold < SecHeroPracticeGoldCost)
            {
                SendSecHeroPracticeMessage("您的金币不足，副将英雄的自动修炼终止",
                    SecHeroPracticeRedBackground);
                ClearSecHeroPractice();
                return;
            }

            GrantSecHeroPracticeBaseReward();
            DecGold(SecHeroPracticeGoldCost);
            GoldChanged();

            if (costTier is not (2 or 3))
                return;

            var lingFuCost = costTier == 2 ? 1 : 10;
            if (!TryGetNativeLingFuBalance(out var balance) || balance < lingFuCost)
            {
                SendSecHeroPracticeMessage("您的灵符不足，副将英雄的自动修炼终止",
                    SecHeroPracticeRedBackground);
                ClearSecHeroPractice();
                return;
            }

            if (!TryGrantSecHeroPracticeBonus(costTier))
                return;

            if (DecNativeLingFu(SecHeroPracticeLingFuReason, lingFuCost))
                m_nSecHeroPracticeLingFuUsed = unchecked(
                    m_nSecHeroPracticeLingFuUsed + lingFuCost);
        }

        private void GrantSecHeroPracticeBaseReward()
        {
            var level = m_wSecHeroPracticeLevel;
            switch (m_btSecHeroPracticeRewardMode)
            {
                case 1:
                    AddNativeHeroExperienceAccumulator(13 * level + 750, 0);
                    AddNativeHeroExperienceAccumulator(5 * level + 63, 1);
                    break;
                case 2:
                    AddNativeHeroExperienceAccumulator(25 * level + 1500, 0);
                    break;
                case 3:
                    AddNativeHeroExperienceAccumulator(10 * level + 125, 1);
                    break;
            }
        }

        private bool TryGrantSecHeroPracticeBonus(byte costTier)
        {
            var manager = M2Share.SecHeroPracticePrizeManager;
            if (manager == null || !manager.TrySelect(costTier, out var prize))
                return false;

            if (prize.Amount <= 0)
                return false;
            if (string.Equals(prize.Kind, "经验", StringComparison.Ordinal))
            {
                AddSecHeroPracticeDataLog(9, "副将累计经验", 555_550,
                    prize.Amount, "副将放养给予");
                AddNativeHeroExperienceAccumulator(prize.Amount, 0);
                return true;
            }
            if (string.Equals(prize.Kind, "内功经验", StringComparison.Ordinal))
            {
                AddSecHeroPracticeDataLog(9, "副将累计内功经验", 555_551,
                    prize.Amount, "副将放养给予");
                AddNativeHeroExperienceAccumulator(prize.Amount, 1);
                return true;
            }
            return false;
        }

        internal void FlushSecHeroPracticeLingFuLog()
        {
            var amount = m_nSecHeroPracticeLingFuUsed;
            if (amount <= 0)
                return;

            AddSecHeroPracticeDataLog(10, "灵符", SecHeroPracticeLingFuReason,
                amount, "副将英雄放养消耗");
            m_nSecHeroPracticeLingFuUsed = 0;
        }

        private void SendSecHeroPracticeMessage(string message, int background)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                SecHeroPracticeHintForeground, background, 0, message);
        }

        private void AddSecHeroPracticeDataLog(int type, string title, int id,
            int amount, string prefix)
        {
            M2Share.AddGameDataLog(string.Join('\t', type, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, title, id, amount, prefix));
        }
    }
}
