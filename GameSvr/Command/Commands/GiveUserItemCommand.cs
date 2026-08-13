using GameSvr.CommandSystem;

namespace GameSvr
{
    /// <summary>
    /// 注册表 0x007BC9F4 idx=110 perm=4，帮助（长度 51，GBK）：
    /// "给玩家某个物品\t@GiveUserItem 角色名 物品ID 绑定时间"。
    /// case@0x00625165 只有五条指令，把 p1/p2/p3 原样交给 sub_6C253C 后 jmp 0x62B64C。
    /// </summary>
    [GameCommand("GiveUserItem", "给玩家某个物品", "角色名 物品ID 绑定时间", 4)]
    public class GiveUserItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void GiveUserItem(string[] @Params, TPlayObject PlayObject)
        {
            var sHumanName = @Params != null && @Params.Length > 0 ? @Params[0] : string.Empty;
            var sItemId = @Params != null && @Params.Length > 1 ? @Params[1] : string.Empty;
            var sBindDays = @Params != null && @Params.Length > 2 ? @Params[2] : string.Empty;

            // sub_6C253C 逐字节契约（0x006C253C..0x006C2811）：
            //   006C257A / 006C2586  p1 或 p2 为空 -> 直接走尾部，静默
            //   006C2592  call 0x40CAB8  StrToInt64Def(p2, 0)  = 物品 MakeIndex
            //   006C259F  eax = self.[+0x508]                  = GM 自己的背包列表
            //   006C25AB  自 Count-1 倒序遍历，比较 [item+0x20] == MakeIndex
            //   006C25DC  没找到 -> 静默结束
            //   006C25E7  call 0x40CA18  Str_ToInt(p3, 0)      = 绑定天数
            //   006C25F0  <=0 -> 跳过绑定
            //   006C25FC  ==0xFFFF -> call 0x784718(item, 1) 永久绑定；
            //             0xFFDB 绿字 = 物品名 + "永久绑定"(0x006C2860 len=8)
            //   006C2636  否则 today=sub_6D43C4(self); dx = today - (days-1);
            //             call 0x784718(item, dx)；0xFFDB 绿字 =
            //             物品名 + "绑定 "(0x006C2874 len=5) + p3 + " 天"(0x006C2884 len=3)
            //   006C2690  target = UserEngine.GetPlayObject(p1)
            //   006C2699  target 为空 -> 组 0x154(=340) 记录 + 角色名 + 物品名，
            //             call 0x0071315C 交给 DB/跨服（离线补发），不回消息
            //   006C271A  target.[vtbl+0x244](dl=1) 背包空位检查，失败 -> 静默结束
            //   006C2732  self.[vtbl+0x24C] 从 GM 背包摘除该道具
            //   006C2743  target.[vtbl+0x248] 放入目标背包
            //   006C276E  call 0x00768BE0，dx=8，写 type 8 日志
            //
            // 也就是说 p2 是**GM 自己包裹里已存在道具的 MakeIndex**、p3 是**绑定天数**，
            // 整条命令是「把自己包里的一件道具转给别人」，不造物。
            // 此前的 C# 实现把 p2 当物品名、p3 当数量，用 CopyToUserItemFromName 现造道具，
            // 还额外做 RandomUpgradeItem 洗练和 50 件上限 —— 那是原版没有的经济注入。
            // 移植缺口：0x784718 绑定写入、0x6D43C4 当前天数、离线记录 0x154、type 8 日志
            // 在本仓都没有对应物；只补一半会造成读/写/持久化不一致，故整条明确拒绝。
            if (string.IsNullOrEmpty(sHumanName) || string.IsNullOrEmpty(sItemId))
                return;

            NativeCommandFailure.Report(PlayObject, "GiveUserItem",
                $"原版是把 GM 自己包裹内 MakeIndex={sItemId} 的道具转给 {sHumanName} 并设绑定 {sBindDays}；" +
                "绑定写入(0x784718)与离线记录(0x154)尚未移植，未转移任何道具。");
        }
    }
}
