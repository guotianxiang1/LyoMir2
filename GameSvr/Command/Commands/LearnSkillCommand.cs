using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to learn a skill by name for a target player.
    /// Usage: @LearnSkill PlayerName SkillName
    /// Finds the magic by name via UserEngine.FindMagic and adds it to the player's magic list.
    /// </summary>
    [GameCommand("LearnSkill", "让指定玩家学习技能", "人物名称 技能名称", 5)]
    public class LearnSkillCommand : BaseCommond
    {
        [DefaultCommand]
        public void LearnSkill(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumanName = @Params.Length > 0 ? @Params[0] : "";
            var sSkillName = @Params.Length > 1 ? @Params[1] : "";
            if (string.IsNullOrEmpty(sHumanName) || string.IsNullOrEmpty(sSkillName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumanName);
            if (m_PlayObject == null)
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumanName), MsgColor.Red, MsgType.Hint);
                return;
            }
            var Magic = M2Share.UserEngine.FindMagic(sSkillName);
            if (Magic == null)
            {
                PlayObject.SysMsg($"技能 [{sSkillName}] 不存在。", MsgColor.Red, MsgType.Hint);
                return;
            }
            if (m_PlayObject.IsTrainingSkill(Magic.wMagicID))
            {
                PlayObject.SysMsg($"{sHumanName} 已学习过技能 [{sSkillName}]。", MsgColor.Red, MsgType.Hint);
                return;
            }
            var UserMagic = new TUserMagic
            {
                MagicInfo = Magic,
                wMagIdx = Magic.wMagicID,
                btLevel = 0,
                btKey = 0,
                nTranPoint = 0
            };
            m_PlayObject.m_MagicList.Add(UserMagic);
            m_PlayObject.SendAddMagic(UserMagic);
            m_PlayObject.RecalcAbilitys();
            PlayObject.SysMsg($"{sHumanName} 学习技能 [{sSkillName}] 成功。", MsgColor.Green, MsgType.Hint);
            m_PlayObject.SysMsg($"你学会了新技能: {sSkillName}。", MsgColor.Green, MsgType.Hint);
            M2Share.MainOutMessage($"[学习技能] GM {PlayObject.m_sCharName} 为 {sHumanName} 添加技能 {sSkillName}");
        }
    }
}
