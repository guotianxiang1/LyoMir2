using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B8554 `0A "LookOutSay"`，+0x18 = 64，+0x1C = 3，
    // 帮助文本「查看禁言名单列表 \t @LookOutSay」。
    // jt[64] @0x00622C1C = b5 42 62 00 -> 0x006242B5：
    //   0x006242B8 eax=[0x007DC26C] / call 0x00621E74 -> 计数写入 [ebp-0x34]
    //   0x006242C2 cmp dword [ebp-0x34],0 / jne 0x006242E1
    //   0x006242C8 mov cx,0xFFDB / edx=0x0062BB24 "禁言名单为空"
    // 旧命令名 ShutupList 三编码 0 命中。
    // 未核实：0x006242E1 起的逐条输出格式与本实现（"剩余 N 秒"）是否一致。
    [GameCommand("LookOutSay", "查看禁言名单列表", 3)]
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
