using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // Native table record: index 136, permission 4, case 0x6252D3.
    [GameCommand("ThroughRange", "设置本服务器的安全区穿人范围", "[无/0..50]", 4)]
    public sealed class ThroughRangeCommand : BaseCommond
    {
        private const ushort NativeSysMsgColorWord = 0x38FF;
        private const string NativeReplyPrefix = "设置本服务器的安全区穿人范围为: ";

        [DefaultCommand]
        public void ThroughRange(string[] @params, TPlayObject player)
        {
            if (player == null)
            {
                return;
            }

            var rawValue = @params != null && @params.Length > 0
                ? @params[0] ?? string.Empty
                : string.Empty;
            var value = HUtil32.Str_ToInt(rawValue, 0);
            if (value < 0 || value > NativeGmMonsterMapCommands.ThroughRangeMax)
            {
                return;
            }

            TPlayObject.NativeSafeZoneThroughRange = value;
            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                NativeSysMsgColorWord & 0xFF,
                NativeSysMsgColorWord >> 8,
                0,
                NativeReplyPrefix + rawValue);
        }
    }
}
