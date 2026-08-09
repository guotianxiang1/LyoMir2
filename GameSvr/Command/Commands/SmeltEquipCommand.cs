using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SmeltEquip", "熔炼装备", "人物名称 装备位置", 5)]
    public class SmeltEquipCommand : BaseCommond
    {
        [DefaultCommand]
        public void SmeltEquip(string[] @Params, TPlayObject PlayObject)
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
            NativeCommandFailure.Report(PlayObject, "SmeltEquip",
                "原版按物品 ID 精炼事务尚未移植，未修改物品。");
        }
    }
}
