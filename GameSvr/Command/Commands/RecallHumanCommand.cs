using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B88B4 `07 "CallMan"`，+0x18 = 72，+0x1C = 3，
    // 帮助文本「将玩家拖到身边 \t @CallMan 角色名」。
    // jt[72] @0x00622C3C = 94 4c 62 00 -> 0x00624C94：
    //   8b 55 cc mov edx,[ebp-0x34](p1) / 8b 45 f8 mov eax,[ebp-8](self)
    //   e8 b9 a7 09 00 call 0x006BF458 / e9 .. jmp 0x0062B64C
    // 该 case 没有空串检查、没有 '?' 帮助分支、也不回任何消息——p1 缺省时就是空串，
    // 原样传进 0x006BF458。原来的 `sHumanName[1] == '?'` 是传统 GOM 的帮助惯例，
    // 既非原版行为，遇到单字符角色名还会抛 IndexOutOfRange。
    // 旧命令名 RecallHuman 三编码 0 命中。
    // 未核实：0x006BF458 内部与 TPlayObject.RecallHuman 是否逐字节一致。
    [GameCommand("CallMan", "将玩家拖到身边", "角色名", 3)]
    public class RecallHumanCommand : BaseCommond
    {
        [DefaultCommand]
        public void RecallHuman(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            PlayObject.RecallHuman(@Params.Length > 0 ? @Params[0] : "");
        }
    }
}