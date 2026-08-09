using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("GetBackItem", "找回玩家丢失物品", "人物名称", 4)]
    public class GetBackItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void GetBackItem(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "GetBackItem",
                "原版事件物品索引与回收事务尚未移植，未创建或转移物品。");
        }
    }
}
