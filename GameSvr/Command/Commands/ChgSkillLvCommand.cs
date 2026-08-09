using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ChgSkillLv", "调整指定玩家技能等级", "人物名称 技能名称 等级", 10)]
    public class ChgSkillLvCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgSkillLv(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            var sSkillName = @Params.Length > 1 ? @Params[1] : "";
            var nLevel = @Params.Length > 2 ? HUtil32.Str_ToInt(@Params[2], 0) : 0;
            if (string.IsNullOrEmpty(sHumName) || string.IsNullOrEmpty(sSkillName) || nLevel <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            // 诚实 fail-closed：此前实现仅打印"已调整"成功消息却【从不写入技能等级】——
            // 是一条误导 GM 的假成功命令。原版按名字设置目标玩家技能等级的核心
            // (UpUserSkill 218→sub_6C7644 / ChgSelfSkillLv 94→sub_73F500 一族)为 CoreBodyDeferred，
            // 待 idat 逆向后再接线。现改为如实上报未移植，不再谎报成功。
            NativeCommandFailure.Report(PlayObject, "ChgSkillLv",
                "原版按名字设置玩家技能等级(sub_6C7644/sub_73F500 一族)尚未移植，未修改技能等级。");
        }
    }
}
