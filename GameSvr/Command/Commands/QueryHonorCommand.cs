using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @QueryHonor 人物名称  (Query player honor value)
    // Queries the honor value (m_nHonorValue) for the specified player.
    [GameCommand("QueryHonor", "查询玩家荣誉值", "人物名称", 10)]
    public class QueryHonorCommand : BaseCommond
    {
        [DefaultCommand]
        public void QueryHonor(string[] @Params, TPlayObject PlayObject)
        {
            var sHumName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumName))
            {
                PlayObject.SysMsg("命令格式：@QueryHonor 人物名称", MsgColor.Red, MsgType.Hint);
                return;
            }
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null)
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName), MsgColor.Red, MsgType.Hint);
                return;
            }
            PlayObject.SysMsg($"{sHumName}的荣誉值为：{target.m_nHonorValue}", MsgColor.Green, MsgType.Hint);
        }
    }
}
