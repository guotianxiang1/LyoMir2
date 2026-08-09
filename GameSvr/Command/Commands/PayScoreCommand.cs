using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("PayScore", "扣除玩家积分", "人物名称 积分数量", 4)]
    public class PayScoreCommand : BaseCommond
    {
        [DefaultCommand]
        public void PayScore(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            var nScore = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (string.IsNullOrEmpty(sHumName) || nScore <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            if (m_PlayObject != null)
            {
                m_PlayObject.m_nGamePoint -= nScore;
                m_PlayObject.GameGoldChanged();
                PlayObject.SysMsg(sHumName + " 的积分已扣除 " + nScore, MsgColor.Green, MsgType.Hint);
                M2Share.MainOutMessage(string.Format("[PayScore] {0} 扣除 {1} 积分 {2}", PlayObject.m_sCharName, sHumName, nScore));
            }
            else
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName), MsgColor.Red, MsgType.Hint);
            }
        }
    }
}
