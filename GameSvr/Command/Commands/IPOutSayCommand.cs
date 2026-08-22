using GameSvr.CommandSystem;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 158, case 0x006258AC -> sub_6D4CA4.
    /// It mutes every non-ghost online player sharing the supplied IP and
    /// mirrors each mute through ident 209.
    /// </summary>
    [GameCommand("IPOutSay", "禁止指定IP地址的玩家聊天多长时间",
        "IP地址 时间(秒)", 4)]
    public sealed class IPOutSayCommand : BaseCommond
    {
        [DefaultCommand]
        public void IPOutSay(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var ip = @Params != null && @Params.Length > 0
                ? @Params[0] ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(ip))
            {
                SendNativeSysMsg(PlayObject, NativeGmIpOutSay.UsageMessage,
                    NativeGmIpOutSay.UsageColorWord);
                return;
            }

            var secondsText = @Params != null && @Params.Length > 1
                ? @Params[1] ?? string.Empty
                : string.Empty;
            var seconds = NativeGmIpOutSay.ParseSeconds(secondsText);
            if (seconds <= 0)
                return;

            var userEngine = M2Share.UserEngine;
            var matches = NativeGmIpOutSay.FindMatches(
                userEngine?.PlayObjects, ip);
            foreach (var player in matches)
            {
                var name = player?.m_sCharName ?? string.Empty;
                NativeMirrorChatBan.Add(name, seconds);
                userEngine?.SendServerGroupMsg(
                    Grobal2.ISM_CHATPROHIBITION, M2Share.nServerIndex,
                    seconds, name);
            }

            SendNativeSysMsg(PlayObject,
                NativeGmIpOutSay.BuildMessage(ip, matches.Count, seconds),
                NativeGmIpOutSay.ReplyColorWord);
        }

        private static void SendNativeSysMsg(TPlayObject player, string message,
            int colorWord)
        {
            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                colorWord & 0xFF, (colorWord >> 8) & 0xFF, 0, message);
        }
    }
}
