using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to look up item information by item ID.
    /// Usage: @LookUserItemId ItemId
    /// Shows item name and details from the standard item database.
    /// </summary>
    [GameCommand("LookUserItemId", "查看物品ID信息", "物品ID", 4)]
    public class LookUserItemIdCommand : BaseCommond
    {
        [DefaultCommand]
        public void LookUserItemId(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length < 1)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var nItemId = HUtil32.Str_ToInt(@Params[0], -1);
            if (nItemId < 0)
            {
                PlayObject.SysMsg("无效的物品ID。", MsgColor.Red, MsgType.Hint);
                return;
            }
            var StdItem = M2Share.UserEngine.GetStdItem(nItemId);
            if (StdItem == null)
            {
                PlayObject.SysMsg($"物品ID [{nItemId}] 在标准物品数据库中不存在。", MsgColor.Red, MsgType.Hint);
                return;
            }
            PlayObject.SysMsg($"物品ID: {nItemId} | 名称: {StdItem.Name} | 类型: {StdItem.StdMode}/{StdItem.Shape} | 等级: {StdItem.NeedLevel} | 重量: {StdItem.Weight}", MsgColor.Green, MsgType.Hint);
        }
    }
}
