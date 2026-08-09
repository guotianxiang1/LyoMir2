using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    public sealed class NativeType2SecondaryRankingPublisher
    {
        public const int PersonalRankingBucket = 3;
        public const int RowCount = 7;
        public const int RowSize = 24;
        public const int NameCapacity = 15;

        private readonly Func<string, TPlayObject> _findOnlinePlayer;
        private readonly Action<LegacyGateType18> _broadcast;

        public static NativeType2SecondaryRankingPublisher Runtime { get; } =
            new NativeType2SecondaryRankingPublisher(
                name => M2Share.UserEngine?.GetPlayObject(name),
                packet =>
                {
                    M2Share.GateManager?.BroadcastLegacyType18(packet);
                });

        public NativeType2SecondaryRankingPublisher(
            Func<string, TPlayObject> findOnlinePlayer,
            Action<LegacyGateType18> broadcast)
        {
            _findOnlinePlayer = findOnlinePlayer
                ?? throw new ArgumentNullException(nameof(findOnlinePlayer));
            _broadcast = broadcast
                ?? throw new ArgumentNullException(nameof(broadcast));
        }

        public void Publish(
            IReadOnlyList<NativeType2SecondaryRankingRawRecord> bucket)
        {
            if (bucket == null || bucket.Count == 0) return;

            var body = bucket[0].Body;
            for (var row = 0; row < RowCount; row++)
            {
                var name = ReadName(body, row * RowSize);
                if (string.IsNullOrEmpty(name)) continue;

                var player = _findOnlinePlayer(name);
                if (player != null)
                    PublishPlayerRanking(player, unchecked((ushort)(row + 1)));
            }
        }

        private void PublishPlayerRanking(TPlayObject player, ushort ranking)
        {
            player.m_wNativeCurrentPersonalRanking = ranking;
            var previous = player.GetNativePreviousPersonalRanking();
            if (ranking < previous)
            {
                var rise = previous - ranking;
                var text = "玛法群英榜十强动态：和上次在榜中的排名相比，"
                           + player.m_sCharName + " 的个人排行上升了" + rise
                           + "位，目前位居玛法群英榜第" + ranking + "位!";
                _broadcast(new LegacyGateType18
                {
                    FilterUserIndex = 0,
                    Recog = 0,
                    Ident = Grobal2.SM_SYSMESSAGE,
                    Param = 0x38FF,
                    TextBytes = HUtil32.GbkEncoding.GetBytes(text)
                });
                player.SetNativePreviousPersonalRanking(ranking);
                return;
            }

            if (previous == 0 || previous >= ranking) return;

            var decline = ranking - previous;
            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                0xFF, 0xFC, 0,
                "您的个人排行目前在玛法群英榜中名列第" + ranking
                + "名，和上一次您在榜中的排名相比，您下降了"
                + decline + "位。");
            player.SetNativePreviousPersonalRanking(ranking);
        }

        private static string ReadName(ReadOnlySpan<byte> body, int offset)
        {
            if (offset < 0 || body.Length <= offset) return string.Empty;
            var length = body[offset];
            if (length == 0 || length > NameCapacity
                || body.Length < offset + 1 + length)
                return string.Empty;
            return HUtil32.GbkEncoding.GetString(body.Slice(offset + 1, length));
        }
    }
}
