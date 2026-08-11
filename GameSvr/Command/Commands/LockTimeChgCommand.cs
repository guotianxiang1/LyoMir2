using GameSvr.CommandSystem;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @LockTimeChg (idx329, perm5) —— 取反全局"系统时间锁定"标志。
    /// 原版 case@0x0062790D 完整案体只有四条指令(byte-level 逐字):
    ///     0x0062790D  mov     al, ds:byte_7DC270  ; 读全局锁定字节
    ///     0x00627912  xor     al, 1               ; 取反 bit0
    ///     0x00627914  mov     ds:byte_7DC270, al  ; 写回
    ///     0x00627919  jmp     loc_62B64C          ; 共享空出口 → 无 SysMsg
    /// 三点契约:
    ///   1) **toggle 而非置位** —— `xor al,1`，与 @AllowTeam 的 `mov ...,1` 恰好相反;
    ///   2) **全局标志** —— byte_7DC270 是进程级全局(ds:)，不是玩家字段，与调用者无关;
    ///   3) **完全静默** —— 直落 loc_62B64C，无 vtbl 调用 → 不回显新状态(GM 需用 @SetSysTime 的
    ///      拒绝反应来间接确认)。故此处**不得**加"已开启/已关闭"提示，否则就是 over-send。
    /// 该标志的唯一消费点 = @SetSysTime(idx268) 案体开头 `cmp ds:byte_7DC270,0` / `jz`:
    /// 置位时 SetSysTime 用 0x38FF 拒绝并不改时钟(big622820.txt:3335-3336)。
    /// 字段: byte_7DC270 == C# NativeGmWorldAdminCommands.WorldTimeLocked
    ///   (NativeGmWorldAdminCommands.cs:421 注释原文 "Mirror of byte_7DC270 (the world
    ///   time-lock flag @0x007DC270)")，且 EvalSetSysTime 已消费同一属性 → 复用既有镜像，不新建字段。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:4402-4405 (上述四条指令原文);
    ///       同文件 3335-3336 (SetSysTime 读同一字节);
    ///       staging/gm_full_inventory_20260731.md idx329 (perm5, addr 0x0062790D, IMPL);
    ///       GameSvr/Services/NativeGmWorldAdminCommands.cs:221-224 (LockTimeChg 记录: "Toggles the
    ///       world time-lock flag: byte_7DC270 ^= 1. No message.")。
    /// 补齐原因: census 有、C# 此前只有 dormant 模型记录而无 live handler(缺 1:1)；纯全局 bool 取反。
    /// </summary>
    [GameCommand("LockTimeChg", "切换系统时间锁定标志", "", 5)]
    public class LockTimeChgCommand : BaseCommond
    {
        [DefaultCommand]
        public void LockTimeChg(TPlayObject PlayObject)
        {
            // 原版 `xor al,1`：取反 byte_7DC270(全局)，且不回任何消息。
            NativeGmWorldAdminCommands.WorldTimeLocked =
                !NativeGmWorldAdminCommands.WorldTimeLocked;
        }
    }
}
