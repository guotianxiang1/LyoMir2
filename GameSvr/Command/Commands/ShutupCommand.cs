using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B7354 `06 "OutSay"`，+0x18 = 62，+0x1C = 2，
    // 帮助文本「禁言角色多少时间 \t @OutSay 角色名 [时间数|无]」。
    // jt[62] @0x00622C14 = 90 42 62 00 -> 0x00624290：
    //   ecx=p2(时间) / edx=p1(角色名) / eax=self / call 0x006BF260 / jmp 0x0062B64C
    // 旧命令名 Shutup 三编码 0 命中（镜像里的两处只是脚本 API UnShutupSelf 的子串）。
    // 未核实：0x006BF260 内部与本实现（g_DenySayMsgList，分钟×60000ms）是否逐字节一致。
    [GameCommand("OutSay", "禁言角色多少时间", M2Share.g_sGameCommandShutupHelpMsg, 2)]
    public class ShutupCommand : BaseCommond
    {
        [DefaultCommand]
        public void Shutup(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumanName = @Params.Length > 0 ? @Params[0] : "";
            var sTime = @Params.Length > 1 ? @Params[1] : "";
            if (sTime == "" || string.IsNullOrEmpty(sHumanName) || sHumanName[0] == '?')
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandParamUnKnow, this.GameCommand.Name, M2Share.g_sGameCommandShutupHelpMsg), MsgColor.Red, MsgType.Hint);
                return;
            }
            var dwTime = (uint)HUtil32.Str_ToInt(sTime, 5);
            HUtil32.EnterCriticalSection(M2Share.g_DenySayMsgList);
            try
            {
                M2Share.g_DenySayMsgList[sHumanName] =
                    (long)HUtil32.GetTickCount() + dwTime * 60_000L;
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.g_DenySayMsgList);
            }
            PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandShutupHumanMsg, sHumanName, dwTime), MsgColor.Red, MsgType.Hint);
        }
    }
}
