using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Restores native case 259 (0x00626550).  The value is process-wide,
    /// persisted under [Setup]PkRuleLevel, and consumed by the low-level PK
    /// protection branch in the player death path.
    /// </summary>
    [GameCommand("SetPkLv", "设置PK红名等级", "等级", 3)]
    public sealed class SetPkLvCommand : BaseCommond
    {
        private const string NativeUsage = "命令格式：@SetPkLv 等级";

        [DefaultCommand]
        public void SetPkLv(string[] @params, TPlayObject player)
        {
            if (player == null)
                return;

            var rawLevel = @params != null && @params.Length > 0
                ? @params[0]
                : string.Empty;
            if (string.IsNullOrEmpty(rawLevel))
            {
                player.SysMsg(NativeUsage, MsgColor.Blue, MsgType.Hint);
                return;
            }

            var level = HUtil32.Str_ToInt(rawLevel, 0);
            if (M2Share.g_Config != null)
                M2Share.g_Config.nPkRuleLevel = level;
            M2Share.ServerConf?.TryWritePkRuleLevel(level);

            player.SysMsg($"当前PK红名等级为{level}级", MsgColor.Red, MsgType.Hint);
        }
    }
}
