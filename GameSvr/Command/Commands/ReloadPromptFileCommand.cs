using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload prompt/message file configuration.
    /// Usage: @ReloadPromptFile
    /// </summary>
    [GameCommand("ReloadPromptFile", "重新加载提示文件配置", 4)]
    public class ReloadPromptFileCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadPromptFile(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReloadPromptFile",
                "原版三类升级提示加载器尚未移植，未替换线上提示。");
        }
    }
}
