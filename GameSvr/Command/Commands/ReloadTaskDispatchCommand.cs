using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload task dispatch configuration.
    /// Usage: @ReloadTaskDispatch
    /// </summary>
    [GameCommand("ReloadTaskDispatch", "重新加载任务调度配置", 4)]
    public class ReloadTaskDispatchCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadTaskDispatch(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReloadTaskDispatch",
                "原版任务发布状态机尚未完整移植，未清理或替换线上任务状态。");
        }
    }
}
