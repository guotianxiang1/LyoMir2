using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @SetGuildLord (idx265, perm4) —— 原版恒定只回字面量 "Invalid"，不做任何状态变更。
    /// 原版 case@0x006267C6 完整案体只有六条指令(byte-level 逐字):
    ///     0x006267C6  mov     cx, 38FFh              ; 颜色 ident 0x38FF = 红(错误色)
    ///     0x006267CA  mov     edx, offset aInvalid   ; 字面量 "Invalid"
    ///     0x006267CF  mov     eax, [ebp+var_8]       ; self
    ///     0x006267D2  mov     ebx, [eax]             ; VMT
    ///     0x006267D4  call    dword ptr [ebx+0D4h]   ; vtbl+0xD4 = SysMsg
    ///     0x006267DA  jmp     loc_62B64C             ; 共享空出口
    /// 关键: 案体**从不读参数槽**(var_34/var_38 未被触碰)，所以"行会名 角色名"怎么填都一样;
    /// 也**没有任何行会状态写**——设正会长这件事在本 build 里从未落地。
    /// 该案体与 idx 355/404/453/458 **共用同一段代码**(反编译里五个 case 标签叠在一起, 见
    /// disp_decomp.txt:2200-2206)，即二进制的 "Invalid" 占位组。既有的 GuildForbidCommand.cs(404)
    /// 已按同一契约落地(恒回 "Invalid"), 本文件与之保持一致。
    /// 颜色: 0x38FF == MsgColor.Red (memory sysmsg-cx-color-packing: 0xFFDB=Green/0x38FF=Red/0xFCFF=Blue)。
    /// ⚠ 不要照帮助文本去实现真的"设置行会正会长": 原版此路径无任何写操作，实现即发散。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:3324-3329 (案体六条指令原文);
    ///       staging/update_clothes_4637_ida_work/disp_decomp.txt:2200-2206 (case 265/355/404/453/458 共用体);
    ///       staging/gm_guild_castle_commands_20260731.md:51 (SetGuildLord 265 = SysMsg "Invalid", no effect, 0x38FF);
    ///       staging/gm_full_inventory_20260731.md idx265 (perm4, addr 0x006267C6)。
    /// 补齐原因: census 有该命令、C# 此前无同名 handler；与 GuildForbidCommand.cs(同组 404) 对称补齐。
    /// </summary>
    [GameCommand("SetGuildLord", "设置行会正会长", "行会名 角色名", 4)]
    public class SetGuildLordCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetGuildLord(string[] @Params, TPlayObject PlayObject)
        {
            // 原版恒定回字面量 "Invalid"(0x38FF 错误色 → MsgColor.Red)且无任何副作用；参数不读。
            PlayObject.SysMsg("Invalid", MsgColor.Red, MsgType.Hint);
        }
    }
}
