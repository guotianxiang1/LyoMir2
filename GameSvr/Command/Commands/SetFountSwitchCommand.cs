using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SetFountSwitch", "设置泉水开关", "状态(0/1)", 4)]
    public class SetFountSwitchCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetFountSwitch(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var nState = @Params.Length > 0 ? HUtil32.Str_ToInt(@Params[0], 0) : 0;
            NativeCommandFailure.Report(PlayObject, "SetFountSwitch",
                "原版 GM 可控泉水对象尚未移植，未修改泉水状态。");
        }
    }
}
