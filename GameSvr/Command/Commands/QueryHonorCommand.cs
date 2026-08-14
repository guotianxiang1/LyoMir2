using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // case @0x0062B4A5 -> sub_6FAABC @0x006FAABC: honor lookup + SysMsg(0xFFDB, "当前的荣耀值为"+value)
    [GameCommand("QueryHonor", "查询玩家荣耀值", "角色名", 2)]
    public class QueryHonorCommand : BaseCommond
    {
        [DefaultCommand]
        public void QueryHonor(string[] @params, TPlayObject playObject)
        {
            if (playObject == null)
                return;
            var name = @params != null && @params.Length > 0 ? @params[0] : string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                playObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            var target = M2Share.UserEngine?.GetPlayObject(name);
            if (target == null)
            {
                playObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, name),
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            M2Share.HonorValueManager?.TryLoad(target);
            var honor = target.m_nHonorValue;
            playObject.SysMsg("当前的荣耀值为" + honor.ToString(), MsgColor.Yellow, MsgType.Hint);
        }
    }
}
