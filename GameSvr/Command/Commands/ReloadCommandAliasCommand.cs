using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr.Command.Commands
{
    /// <summary>
    /// @ReloadCmdAlias — 热重载自定义命令别名，无需重启服务器。
    /// 服务器端执行后立即生效，玩家可用新别名触发命令。
    /// </summary>
    [GameCommand("ReloadCmdAlias", "热重载自定义命令别名", 10)]
    public class ReloadCommandAliasCommand : BaseCommond
    {
        [DefaultCommand]
        public string ReloadCmdAlias(TPlayObject playObject)
        {
            M2Share.CommandSystem.ReloadCustomAlias();
            return "自定义命令别名已重新加载。";
        }
    }
}
