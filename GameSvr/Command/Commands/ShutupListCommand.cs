using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("ShutupList", "查看禁言列表中的内容", 3)]
    public class ShutupListCommand : BaseCommond
    {
        [DefaultCommand]
        public void ShutupList(TPlayObject PlayObject)
        {
            HUtil32.EnterCriticalSection(M2Share.g_DenySayMsgList);
            try
            {
                var nCount = M2Share.g_DenySayMsgList.Count;
                if (M2Share.g_DenySayMsgList.Count <= 0)
                {
                    PlayObject.SysMsg(M2Share.g_sGameCommandShutupListIsNullMsg, MsgColor.Green, MsgType.Hint);
                }
                if (nCount > 0)
                {
                    var now = (long)HUtil32.GetTickCount();
                    foreach (var item in M2Share.g_DenySayMsgList)
                    {
                        var remainingSeconds = Math.Max(0, (item.Value - now) / 1000);
                        PlayObject.SysMsg($"{item.Key} 剩余 {remainingSeconds} 秒",
                            MsgColor.Blue, MsgType.Hint);
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.g_DenySayMsgList);
            }
        }
    }
}
