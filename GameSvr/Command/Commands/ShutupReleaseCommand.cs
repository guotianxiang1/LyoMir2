using GameSvr.CommandSystem;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B7474 `0A "ShifangSay"`，+0x18 = 63，+0x1C = 2，
    // 帮助文本「解除角色的禁言 \t @ShifangSay 角色名」。
    // jt[63] @0x00622C18 = a3 42 62 00 -> 0x006242A3：
    //   b1 01 mov cl,1 / edx=p1(角色名) / eax=self / call 0x006BF340 / jmp 0x0062B64C
    // 旧命令名 ShutupRelease 三编码 0 命中。
    // sub_6BF340 在角色名存在时无条件尝试删除；本地命令 flag=1，因此无条件
    // 发送 ident 210，并以绿色输出固定文本“解除禁言成功！”。
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
            NativeMirrorChatBan.Remove(sHumanName);
            // sub_6BF340's local-command flag is 1, so it emits ident 210 after
            // the delete.  Ident 210 has no carried third parameter.
            M2Share.UserEngine?.SendServerGroupMsg(
                Grobal2.ISM_CHATPROHIBITIONCANCEL, M2Share.nServerIndex,
                sHumanName);
            PlayObject.SysMsg(M2Share.g_sGameCommandShutupReleaseHumanCanSendMsg,
                MsgColor.Green, MsgType.Hint);
        }
    }
}
