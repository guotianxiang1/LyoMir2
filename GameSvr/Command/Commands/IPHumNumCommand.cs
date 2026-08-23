using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 160, case 0x006256B6 -> sub_6E3498.
    /// The sole argument is the minimum number of online players per IP.
    /// </summary>
    [GameCommand("IPHumNum", "查询某个IP有没有指定的人数的玩家(如果该IP的玩家人数大于等于指定的人数,则显示出该IP的真实玩家人数)",
        "人数", 4)]
    public sealed class IPHumNumCommand : BaseCommond
    {
        [DefaultCommand]
        public void IPHumNum(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var thresholdText = @Params != null && @Params.Length > 0
                ? @Params[0] ?? string.Empty
                : string.Empty;
            var threshold = NativeGmIpHumNum.ParseThreshold(thresholdText);
            var counts = NativeGmIpHumNum.CountByIp(
                M2Share.UserEngine?.PlayObjects);
            PlayObject.SysMsg(
                NativeGmIpHumNum.BuildMessage(counts, threshold),
                MsgColor.Green, MsgType.Hint);
        }
    }
}
