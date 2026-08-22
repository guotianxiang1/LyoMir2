using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 154, case 0x006256A3 -> sub_6D45C8.
    /// The native IP helper walks every non-ghost online player whose IP
    /// matches; ReadyRun is intentionally not part of that filter.
    /// </summary>
    [GameCommand("IPHackFlag", "设置/清除某个IP地址的玩家使用非法外挂的惩罚天数",
        "IP地址 [天数]", 4)]
    public sealed class IPHackFlagCommand : BaseCommond
    {
        [DefaultCommand]
        public void IPHackFlag(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var ip = @Params != null && @Params.Length > 0
                ? @Params[0] ?? string.Empty
                : string.Empty;
            var daysText = @Params != null && @Params.Length > 1
                ? @Params[1] ?? string.Empty
                : string.Empty;

            if (string.IsNullOrEmpty(ip))
            {
                PlayObject.SysMsg(NativeGmIpHackFlag.UsageMessage,
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            var matches = NativeGmIpHackFlag.FindMatches(
                M2Share.UserEngine?.PlayObjects, ip);
            var playerList = NativeGmIpHackFlag.BuildPlayerList(matches);

            // A missing second token is the native query branch.  It does not
            // mutate players or write game-data records.
            if (string.IsNullOrEmpty(daysText))
            {
                PlayObject.SysMsg(
                    NativeGmIpHackFlag.BuildQueryMessage(ip, playerList),
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            var parsedDays = NativeGmIpHackFlag.ParseDays(daysText);
            foreach (var player in matches)
            {
                if (parsedDays > 0)
                {
                    player.m_btNativeCheatPenaltyTier =
                        NativeGmIpHackFlag.PenaltyTier;
                    player.m_nNativeCheatPenaltyExpiryDay =
                        NativeGmIpHackFlag.ComputeExpiryDay(
                            player.GetNativeTruncDaysOnline(), parsedDays);
                    M2Share.AddNativeGameDataLog(player,
                        NativeGmIpHackFlag.PenaltyLogType,
                        NativeGmIpHackFlag.SetPenaltyLogItem,
                        1, parsedDays, ip);
                }
                else
                {
                    player.m_btNativeCheatPenaltyTier = 0;
                    player.m_nNativeCheatPenaltyExpiryDay = 0;
                    M2Share.AddNativeGameDataLog(player,
                        NativeGmIpHackFlag.PenaltyLogType,
                        NativeGmIpHackFlag.ClearPenaltyLogItem,
                        1, 1, ip);
                }
            }

            PlayObject.SysMsg(
                NativeGmIpHackFlag.BuildSetMessage(ip, daysText,
                    parsedDays, playerList),
                MsgColor.Green, MsgType.Hint);
        }
    }
}
