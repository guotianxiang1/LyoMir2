using GameSvr.CommandSystem;
using GameSvr.Services;
using System.Text;
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
    // sub_621E74 按链表顺序构造“角色名=剩余秒数\r”；非空时命令只发送
    // 一条绿色消息，以“禁言名单为：\r”开头。
    [GameCommand("LookOutSay", "查看禁言名单列表", 3)]
    public class ShutupListCommand : BaseCommond
    {
        [DefaultCommand]
        public void ShutupList(TPlayObject PlayObject)
        {
            var entries = NativeMirrorChatBan.Snapshot();
            if (entries.Count <= 0)
            {
                PlayObject.SysMsg(M2Share.g_sGameCommandShutupListIsNullMsg,
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            var message = new StringBuilder("禁言名单为：\r");
            foreach (var item in entries)
            {
                message.Append(item.Name)
                    .Append('=')
                    .Append(item.RemainSeconds)
                    .Append('\r');
            }
            PlayObject.SysMsg(message.ToString(), MsgColor.Green, MsgType.Hint);
        }
    }
}
