using GameSvr.CommandSystem;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @GuildWarOn (idx116, perm4) —— 原版为 nullsub 桩：什么都不做、不回消息。
    /// 原版 case@0x00625229 完整案体只有三条指令(byte-level 逐字):
    ///     0x00625229  mov     eax, [ebp+var_8]   ; self
    ///     0x0062522C  call    nullsub_86         ; 空函数
    ///     0x00625231  jmp     loc_62B64C         ; 共享空出口 → 无 SysMsg
    /// nullsub_86 = 0x006C290C；四个行会战 GM 动词的回调在二进制里是**连续 4 字节排布**的空函数块:
    ///     nullsub_85=0x006C2908 (GuildPoint 115) · nullsub_86=0x006C290C (GuildWarOn 116)
    ///     nullsub_87=0x006C2910 (GuildWarOff 117) · nullsub_88=0x006C2914 (ReportGuildWar 118)
    /// 相邻地址仅差 4 字节 → 每个 body 只容得下一条 `ret`，即确系空实现(不是"未 dump 的真函数")。
    /// 因此忠实复刻 = **完全静默的 no-op**：不改任何行会状态、不发 SysMsg、不写日志。
    /// 行会战的真实起停走行会协议路径，不经此 GM 动词(与既有 @GuildWarOff 的处理完全一致)。
    /// ⚠ 不要"顺手"实现成真开战：那正是 gm_overimpl_drift 记录的 RISKY 过度实现方向。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:1898-1900 (案体三条指令),
    ///       同文件 8720-8723 (四个 nullsub 的调用目标地址表, 证明连续 4 字节排布);
    ///       staging/gm_overimpl_drift_20260801.md:33,51 ("四个行会战 GM 动词 idx 115/116/117/118
    ///       都是 nullsub 桩"; GuildWarOff 的真实实现被判 RISKY 并已回退为 no-op);
    ///       staging/gm_full_inventory_20260731.md idx116 (perm4, addr 0x00625229)。
    /// 补齐原因: census 有该命令、C# 此前无同名 handler；与既有 GuildWarOffCommand.cs 对称补齐。
    /// </summary>
    [GameCommand("GuildWarOn", "开启行会战争", "", 4)]
    public class GuildWarOnCommand : BaseCommond
    {
        [DefaultCommand]
        public void GuildWarOn(string[] @Params, TPlayObject PlayObject)
        {
            // 原版 nullsub_86(0x006C290C) 是空函数，案体随后直落共享空出口：
            // 无状态变更、无消息、无日志。行会战真实起停走行会协议路径。见上方证据。
        }
    }
}
