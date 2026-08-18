using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // Native table record: index 180, permission 4, case 0x625969.
    [GameCommand("ClientVersion", "设置服务器客户端版本号", "版本号", 4)]
    public sealed class ClientVersionCommand : BaseCommond
    {
        private const ushort NoticeColorWord = 0xFFDB;
        private const ushort ErrorColorWord = 0x38FF;

        [DefaultCommand]
        public void ClientVersion(string[] @params, TPlayObject playObject)
        {
            if (playObject == null)
            {
                return;
            }

            var version = @params != null && @params.Length > 0
                ? @params[0] ?? string.Empty
                : string.Empty;
            NativeClientVersionPolicy.SetRequiredVersion(version);

            SendNativeSysMsg(playObject,
                "设置本服务器客户端的版本号为 " + version,
                NoticeColorWord);

            var players = M2Share.UserEngine?.GetPlayerList();
            var mismatchCount =
                NativeClientVersionPolicy.RevalidatePlayers(players);
            if (mismatchCount > 0)
            {
                SendNativeSysMsg(playObject,
                    "本服务器共有" + mismatchCount +
                    "个玩家的客户端的版本号不正确",
                    ErrorColorWord);
            }
        }

        private static void SendNativeSysMsg(TPlayObject player,
            string message, ushort colorWord)
        {
            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                colorWord & 0xFF, colorWord >> 8, 0, message);
        }
    }
}
