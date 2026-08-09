using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ShowPayScore", "显示充值积分信息", "", 4)]
    public class ShowPayScoreCommand : BaseCommond
    {
        [DefaultCommand]
        public void ShowPayScore(TPlayObject PlayObject)
        {
            M2Share.MainOutMessage(string.Format("[ShowPayScore] {0} 查询充值积分", PlayObject.m_sCharName));
            PlayObject.SysMsg("当前充值积分: " + PlayObject.m_nGamePoint, MsgColor.Green, MsgType.Hint);
        }
    }
}
