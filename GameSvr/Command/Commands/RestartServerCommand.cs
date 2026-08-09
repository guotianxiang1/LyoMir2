using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to restart the server with a countdown.
    /// Usage: @RestartServer [delaySeconds]
    /// Sends countdown notices, then requests the normal host shutdown path.
    /// </summary>
    [GameCommand("RestartServer", "重启服务器", "[延迟秒数(默认30)]", 10)]
    public class RestartServerCommand : BaseCommond
    {
        [DefaultCommand]
        public void RestartServer(string[] @Params, TPlayObject PlayObject)
        {
            var nDelay = @Params != null && @Params.Length > 0 ? HUtil32.Str_ToInt(@Params[0], 30) : 30;
            if (nDelay < 5) nDelay = 5;
            M2Share.UserEngine.SendBroadCastMsg($"服务器将在 {nDelay} 秒后重启，请各位玩家安全下线！", MsgType.System);
            M2Share.MainOutMessage($"[重启服务器] GM {PlayObject.m_sCharName} 执行了重启操作, 延迟 {nDelay} 秒");
            PlayObject.SysMsg($"服务器重启已开始，将在 {nDelay} 秒后执行。", MsgColor.Green, MsgType.Hint);
            _ = Task.Run(async () =>
            {
                await Task.Delay(nDelay * 1000);
                M2Share.MainOutMessage("[重启服务器] 正在执行正常停机流程...");
                AppService.Instance?.RequestShutdown();
            });
        }
    }
}
