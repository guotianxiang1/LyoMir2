using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ChgEquipLevel", "调整装备等级", "人物名称 装备位置 等级", 5)]
    public class ChgEquipLevelCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgEquipLevel(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            var nLevel = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (string.IsNullOrEmpty(sHumName) || nLevel <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "ChgEquipLevel",
                "原版按物品 ID 修改装备等级的字段映射尚未确认，未修改物品。");
        }
    }
}
