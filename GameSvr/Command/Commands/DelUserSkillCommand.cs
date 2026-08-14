using GameSvr.CommandSystem;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    // @DelUserSkill 角色名 技能名 — dispatch idx 219 @0x00625F9A -> sub_6C772C.
    // 成功时 sub_6C7797 LStrCatN("删除 ", skill, " 成功") + SysMsg 0xFFDB；
    // 眼神 删除技能不提示 apply 0x100DB4A4 把首条 push 改成 jmp 0x6C781D 跳过提示。
    [GameCommand("DelUserSkill", "删除玩家技能", "角色名 技能名", 5)]
    public class DelUserSkillCommand : BaseCommond
    {
        [DefaultCommand]
        public void DelUserSkill(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
                return;
            var charName = @Params.Length > 0 ? @Params[0] : "";
            var skillName = @Params.Length > 1 ? @Params[1] : "";
            if (string.IsNullOrEmpty(charName) || string.IsNullOrEmpty(skillName))
            {
                PlayObject.SysMsg(
                    string.Format(M2Share.g_sGameCommandParamUnKnow,
                        GameCommand.Name, "角色名 技能名"),
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            var target = M2Share.UserEngine.GetPlayObject(charName);
            if (target == null || target.m_boGhost)
            {
                PlayObject.SysMsg(charName + " 不在线或者不在本服务器", MsgColor.Red, MsgType.Hint);
                return;
            }

            if (target.DeleteSelfMagic(skillName))
            {
                if (!YanshenConfig12Behaviors.DelSkillSilent(PlayObject))
                    PlayObject.SysMsg("删除 " + skillName + " 成功", MsgColor.Green, MsgType.Hint);
            }
            else
            {
                PlayObject.SysMsg("删除 " + skillName + " 失败", MsgColor.Red, MsgType.Hint);
            }
        }
    }
}
