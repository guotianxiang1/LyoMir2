using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("NpcScript", "重新读取面对面NPC脚本", "重新读取面对面NPC脚本", 10)]
    public class NpcScriptCommand : BaseCommond
    {
        [DefaultCommand]
        public void NpcScript(TPlayObject PlayObject)
        {
            var npc = PlayObject.GetPoseCreate() as NormNpc;
            if (npc == null)
            {
                PlayObject.SysMsg("必须与 NPC 面对面才能重载脚本。", MsgColor.Red, MsgType.Hint);
                return;
            }

            var scriptName = (npc as Merchant)?.m_sScript;
            if (string.IsNullOrWhiteSpace(scriptName))
            {
                scriptName = npc.m_sCharName;
            }

            string[] candidates =
            {
                scriptName + "-" + npc.m_sMapName,
                scriptName,
                npc.m_sCharName + "-" + npc.m_sMapName,
                npc.m_sCharName
            };

            var pasPath = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Select(candidate => M2Share.PasEngine?.FindScriptFile(candidate))
                .FirstOrDefault(path => path != null);
            if (pasPath == null)
            {
                PlayObject.SysMsg("未找到该 NPC 的 Pascal 脚本。", MsgColor.Red, MsgType.Hint);
                return;
            }

            M2Share.PasEngine.Invalidate(pasPath);
            M2Share.PasEngine.ClearNpcState(npc);
            PlayObject.SysMsg("NPC Pascal 脚本已重新加载。", MsgColor.Green, MsgType.Hint);
        }
    }
}
