using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("NpcHit", "NPC攻击", "NPC名称 目标名称", 4)]
    public class NpcHitCommand : BaseCommond
    {
        [DefaultCommand]
        public void NpcHit(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sNpcName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sNpcName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            // 注: 原版 @NpcHit (idx182, sub_62EA7C)【不取参数】——仅对 GM 附近可见 NPC 播放动作；
            //     现声明的 "NPC名称 目标名称" 参数是 C# 漂移。sub_62EA7C 为 CoreBodyDeferred，
            //     待逆向后按【无参】契约接线。当前如实 fail-closed。
            NativeCommandFailure.Report(PlayObject, "NpcHit",
                "原版附近可见 NPC 动作分发尚未移植，未触发 NPC 动作。");
        }
    }
}
