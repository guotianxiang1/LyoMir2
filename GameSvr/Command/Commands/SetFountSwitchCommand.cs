using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // Native table record: index 307, permission 4, case 0x62723E.
    [GameCommand("SetFountSwitch", "打开/关闭GM可控泉水", "[open/close]", 4)]
    public sealed class SetFountSwitchCommand : BaseCommond
    {
        private const ushort NativeSysMsgColorWord = 0x38FF;
        private const string OpenReply = "GM可控泉水已打开";
        private const string CloseReply = "GM可控泉水已关闭";
        private const string UsageReply =
            "参数open表示打开，参数close表示关闭，GM可控泉水默认关闭";

        [DefaultCommand]
        public void SetFountSwitch(string[] @params, TPlayObject player)
        {
            if (player == null)
            {
                return;
            }

            var operation = @params != null && @params.Length > 0
                ? @params[0] ?? string.Empty
                : string.Empty;
            string reply;
            if (operation == "open")
            {
                M2Share.NativeFountSwitch = 1;
                reply = OpenReply;
            }
            else if (operation == "close")
            {
                M2Share.NativeFountSwitch = 0;
                reply = CloseReply;
            }
            else
            {
                reply = UsageReply;
            }

            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                NativeSysMsgColorWord & 0xFF,
                NativeSysMsgColorWord >> 8,
                0,
                reply);
        }
    }
}
