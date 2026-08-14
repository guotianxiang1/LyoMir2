using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // sub_648148 @0x00648148 — challenge ranking paginated dialog
        // sub_722FC4 @0x00722FC4 — accumulated ranking banner

        public void SendNativeChallengeRankingPage(NormNpc npc, int pageIndex)
        {
            if (npc == null) return;
            var board = NativeRewardConfigLoaders.GloryRankBoard;
            var message = NativeChallengeRanking.BuildPage(
                board?.Entries ?? Array.Empty<NativeGloryRankBoard.Entry>(),
                pageIndex);
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                npc.m_sCharName + '/' + message);
        }

        public void SendNativeAccumulatedRankingBanner(NormNpc npc,
            bool enabled, bool showDetail)
        {
            if (npc == null) return;
            var board = NativeRewardConfigLoaders.GloryRankBoard;
            var count = board?.Count ?? 0;
            var banner = NativeAccumulatedRanking.BuildBanner(enabled,
                showDetail, count, pageSize: 10);
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                npc.m_sCharName + '/' + banner + "\\");
        }

        /// <summary>
        /// sub_7520E0 @0x007520E0 — first-login newbie gift claim hook.
        /// </summary>
        public bool TryClaimNativeNewbieGift(NormNpc npc)
        {
            var config = NativeRewardConfigLoaders.NewbieGift;
            if (config == null ||
                !config.TryGetPrize(m_btJob, out var descriptor) ||
                string.IsNullOrEmpty(descriptor))
                return false;

            return TryGiveNativeMagicTowerDescriptor(descriptor, npc);
        }
    }
}
