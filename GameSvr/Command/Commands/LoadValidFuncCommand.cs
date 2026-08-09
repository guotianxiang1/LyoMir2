using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("LoadValidFunc", "加载有效功能配置", "", 4)]
    public class LoadValidFuncCommand : BaseCommond
    {
        [DefaultCommand]
        public void LoadValidFunc(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "LoadValidFunc",
                "原版 validScriptFunc.txt 安全函数列表尚未移植，未改变脚本权限。");
        }
    }
}
