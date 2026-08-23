using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>Restores native dispatcher case 477 (0x0062B59C).</summary>
    [GameCommand("SetGsTaskVersion", "", "", 3)]
    public sealed class SetGsTaskVersionCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetGsTaskVersion(string[] @params, TPlayObject player)
        {
            if (player == null)
                return;

            var value = @params != null && @params.Length > 0
                ? (@params[0] ?? string.Empty).Trim()
                : string.Empty;
            if (value.Length == 0)
                return;

            try
            {
                if (M2Share.ServerConf == null ||
                    !M2Share.ServerConf.TryWriteGSTaskVersion(value))
                    return;

                if (M2Share.g_Config != null)
                    M2Share.g_Config.nGSTaskVersion = HUtil32.Str_ToInt(
                        value, M2Share.g_Config.nGSTaskVersion);

                player.SysMsg("GS_Task_Version成功修改为：" + value,
                    MsgColor.Green, MsgType.Hint);
            }
            catch
            {
                player.SysMsg("!Setup.txt写入失败", MsgColor.Red, MsgType.Hint);
            }
        }
    }
}
