using GameSvr.CommandSystem;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B7354 `06 "OutSay"`，+0x18 = 62，+0x1C = 2，
    // 帮助文本「禁言角色多少时间 \t @OutSay 角色名 [时间数|无]」。
    // jt[62] @0x00622C14 = 90 42 62 00 -> 0x00624290：
    //   ecx=p2(时间) / edx=p1(角色名) / eax=self / call 0x006BF260 / jmp 0x0062B64C
    // 旧命令名 Shutup 三编码 0 命中（镜像里的两处只是脚本 API UnShutupSelf 的子串）。
    // sub_6BF260 将缺省时间解析为 10 秒，调用 sub_621B14 后发送 ident 209，
    // 并以绿色输出“角色名 禁止聊天：总剩余秒数秒”。
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
            if (string.IsNullOrEmpty(sHumanName) || sHumanName[0] == '?')
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandParamUnKnow, this.GameCommand.Name, M2Share.g_sGameCommandShutupHelpMsg), MsgColor.Red, MsgType.Hint);
                return;
            }
            var durationSeconds = HUtil32.Str_ToInt(sTime, 10);
            if (durationSeconds <= 0)
                return;

            // sub_6BF260 parses a default of 10 and passes the value directly as
            // the native remaining-seconds word; it also replicates ident 209.
            var totalSeconds = NativeMirrorChatBan.Add(sHumanName,
                durationSeconds);
            M2Share.UserEngine?.SendServerGroupMsg(
                Grobal2.ISM_CHATPROHIBITION, M2Share.nServerIndex,
                durationSeconds, sHumanName);
            PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandShutupHumanMsg,
                sHumanName, totalSeconds), MsgColor.Green, MsgType.Hint);
        }
    }
}
