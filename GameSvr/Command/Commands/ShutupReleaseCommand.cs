using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B7474 `0A "ShifangSay"`，+0x18 = 63，+0x1C = 2，
    // 帮助文本「解除角色的禁言 \t @ShifangSay 角色名」。
    // jt[63] @0x00622C18 = a3 42 62 00 -> 0x006242A3：
    //   b1 01 mov cl,1 / edx=p1(角色名) / eax=self / call 0x006BF340 / jmp 0x0062B64C
    // 旧命令名 ShutupRelease 三编码 0 命中。
    // 未核实：0x006BF340 内部与本实现（TryRemove）是否逐字节一致。
    [GameCommand("ShifangSay", "解除角色的禁言", M2Share.g_sGameCommandShutupReleaseHelpMsg, 2)]
    public class ShutupReleaseCommand : BaseCommond
    {
        [DefaultCommand]
        public void ShutupRelease(string[] @params, TPlayObject PlayObject)
        {
            if (@params == null)
            {
                return;
            }
            var sHumanName = @params.Length > 0 ? @params[0] : "";
            if (string.IsNullOrEmpty(sHumanName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            HUtil32.EnterCriticalSection(M2Share.g_DenySayMsgList);
            try
            {
                if (M2Share.g_DenySayMsgList.TryRemove(sHumanName, out _))
                {
                    PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandShutupReleaseHumanCanSendMsg,
                        sHumanName), MsgColor.Green, MsgType.Hint);
                }
                else
                {
                    PlayObject.SysMsg($"{sHumanName} 不在禁言列表中。", MsgColor.Red, MsgType.Hint);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.g_DenySayMsgList);
            }
        }
    }
}
