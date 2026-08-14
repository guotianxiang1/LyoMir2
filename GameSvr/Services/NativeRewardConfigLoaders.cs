using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Startup loaders for native reward/ranking INI tables (VA evidence in comments).
    /// </summary>
    public static class NativeRewardConfigLoaders
    {
        // sub_74FEB0 @0x0074FEB0 — config\Paihang.ini, 8 pools @ UserEngine+0x2AC
        public static NativeThresholdPrizeCatalog FengyunRankingPrize { get; private set; }

        // sub_751018 @0x00751018 — Config\火龙珠.ini, 5 pools @ +0x2E4
        public static NativeThresholdPrizeCatalog FireDragonPearlPrize { get; private set; }

        // sub_74F114 @0x0074F114 — Config\NewGoldId.ini threshold pools @ +0x250
        public static NativeThresholdPrizeCatalog HotBloodThresholdPrize
            { get; private set; }

        // sub_74FC3C @0x0074FC3C — config\圣殿天关.ini, 6 pools
        public static NativeThresholdPrizeCatalog TempleSkyGatePrize
            { get; private set; }

        // Share\Platina.ini — UserEngine+0x278 (sub_74E190 selector)
        public static Dictionary<int, List<string>> PlatinumPrizePools
            { get; private set; }

        // sub_7520E0 @0x007520E0 — config\新手礼包.ini
        public static NativeNewbieGiftConfig NewbieGift { get; private set; }

        // sub_60EBBC @0x0060EBBC — glory rank board entries
        public static NativeGloryRankBoard GloryRankBoard { get; private set; }

        // Share\DiamondBounty.ini — sub_74E5DC @0x0074E5DC
        public static NativeDiamondBountyConfig DiamondBounty { get; private set; }

        public static bool TryLoadAll(string shareDirectory, string configDirectory,
            out string error)
        {
            error = string.Empty;
            var share = Path.GetFullPath(shareDirectory);
            var config = Path.GetFullPath(configDirectory);

            if (!NativeThresholdPrizeCatalog.TryLoad(
                    Path.Combine(config, "Paihang.ini"), 8, "高配置", "奖品",
                    "风云榜奖励", out var fengyun, out error))
            {
                FengyunRankingPrize = fengyun;
                return false;
            }
            FengyunRankingPrize = fengyun;

            if (!NativeThresholdPrizeCatalog.TryLoad(
                    Path.Combine(config, "火龙珠.ini"), 5, "配置", "奖品",
                    "火龙珠奖励", out var fireDragon, out error))
            {
                FireDragonPearlPrize = fireDragon;
                return false;
            }
            FireDragonPearlPrize = fireDragon;

            if (!NativeThresholdPrizeCatalog.TryLoad(
                    Path.Combine(config, "NewGoldId.ini"), 5, "配置", "奖品",
                    "热血勇士奖励", out var hotBlood, out error))
            {
                HotBloodThresholdPrize = hotBlood;
                return false;
            }
            HotBloodThresholdPrize = hotBlood;

            if (!NativeThresholdPrizeCatalog.TryLoad(
                    Path.Combine(config, "圣殿天关.ini"), 6, "配置", "奖品",
                    "圣殿天关奖励", out var temple, out error))
            {
                TempleSkyGatePrize = temple;
                return false;
            }
            TempleSkyGatePrize = temple;

            if (!NativeThresholdPrizeCatalog.TryLoadSimplePools(
                    Path.Combine(share, "Platina.ini"), 10, "配置", "奖励",
                    out var platinum, out error))
            {
                PlatinumPrizePools = platinum;
                return false;
            }
            PlatinumPrizePools = platinum;

            if (!NativeNewbieGiftConfig.TryLoad(
                    Path.Combine(config, "新手礼包.ini"),
                    out var newbie, out error))
            {
                NewbieGift = newbie;
                return false;
            }
            NewbieGift = newbie;

            if (!NativeGloryRankBoard.TryLoad(share, out var glory, out error))
            {
                GloryRankBoard = glory;
                return false;
            }
            GloryRankBoard = glory;

            if (!NativeDiamondBountyConfig.TryLoad(
                    Path.Combine(share, "DiamondBounty.ini"),
                    out var diamondBounty, out error))
            {
                DiamondBounty = diamondBounty;
                return false;
            }
            DiamondBounty = diamondBounty;

            return true;
        }
    }
}
