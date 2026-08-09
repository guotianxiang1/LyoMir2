using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("TrainingSkill", "调整指定玩家技能等级", "人物名称  技能名称 修炼等级(0-3)", 10)]
    public class TrainingSkillCommand : BaseCommond
    {
        [DefaultCommand]
        public void TrainingSkill(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumanName = @Params.Length > 0 ? @Params[0] : "";
            var sSkillName = @Params.Length > 1 ? @Params[1] : "";
            if (string.IsNullOrEmpty(sHumanName) || sSkillName == "")
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            // 诚实 fail-closed：此前实现找到玩家后只有一个【空的 m_MagicList 遍历循环】，
            // 什么都不做也不报错——同 @ChgSkillLv 一样是假工作命令。原版设置玩家技能
            // 修炼等级的核心为 CoreBodyDeferred，待 idat 逆向后再接线。
            NativeCommandFailure.Report(PlayObject, "TrainingSkill",
                "原版设置玩家技能修炼等级尚未移植，未修改技能。");
        }
    }
}
