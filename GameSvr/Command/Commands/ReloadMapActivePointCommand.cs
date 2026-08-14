using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>@ReloadMapActivePoint — native dispatch 728 @0x0062ADE9 → sub_618FB8.</summary>
    [GameCommand("ReloadMapActivePoint", "重载地图信用分配置", "", 2)]
    public class ReloadMapActivePointCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMapActivePoint(string[] @params, TPlayObject playObject)
        {
            if (NativeMapActivePointLoader.TryApply(
                    NativeMapActivePointLoader.DefaultFilePath, out var error))
            {
                playObject.SysMsg("重载成功", MsgColor.Green, MsgType.Hint);
                return;
            }

            playObject.SysMsg($"重载失败: {error}", MsgColor.Red, MsgType.Hint);
        }
    }
}
