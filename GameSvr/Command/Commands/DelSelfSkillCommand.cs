using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @DelSelfSkill 技能名 — dispatch idx 95 @0x00624FE8 -> sub_73F690（无 wrapper SysMsg）。
    [GameCommand("DelSelfSkill", "删除自身技能", "技能名", 4)]
    public class DelSelfSkillCommand : BaseCommond
    {
        [DefaultCommand]
        public void DelSelfSkill(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
                return;
            var skillName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(skillName))
            {
                PlayObject.SysMsg(
                    string.Format(M2Share.g_sGameCommandParamUnKnow,
                        GameCommand.Name, "技能名"),
                    MsgColor.Red, MsgType.Hint);
                return;
            }
            PlayObject.DeleteSelfMagic(skillName);
        }
    }
}
