using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @selfAddGuild (idx453, perm4) —— 原版恒定只回字面量 "Invalid"，不做任何状态变更。
    /// 原版 case@0x00628B25 完整案体只有六条指令(byte-level 逐字):
    ///     0x00628B25  mov     cx, 38FFh              ; 颜色 ident 0x38FF = 红(错误色)
    ///     0x00628B29  mov     edx, offset aInvalid   ; 字面量 "Invalid"
    ///     0x00628B2E  mov     eax, [ebp+var_8]       ; self
    ///     0x00628B31  mov     ebx, [eax]             ; VMT
    ///     0x00628B33  call    dword ptr [ebx+0D4h]   ; vtbl+0xD4 = SysMsg
    ///     0x00628B39  jmp     loc_62B64C             ; 共享空出口
    /// 关键: 案体**从不读参数槽**(行会名被忽略)，也**没有任何入会写操作**——
    /// "GM 将自身加入行会"这件事在本 build 里从未落地。
    /// 该案体与 idx 265/355/404/458 **共用同一段代码**(反编译里五个 case 标签叠在一起, 见
    /// disp_decomp.txt:2200-2206)，即二进制的 "Invalid" 占位组；既有 GuildForbidCommand.cs(404) 同契约。
    /// 颜色: 0x38FF == MsgColor.Red (memory sysmsg-cx-color-packing)。
    /// ⚠ 不要照帮助文本去实现真入会: 原版此路径无任何行会成员写，实现即发散(且会造成假成功)。
    /// 命令名大小写按 census 记录原文 `selfAddGuild`(小写起头)；C# 命令匹配为大小写不敏感
    /// (CommandManager 的 CommandMaps 用 OrdinalIgnoreCase)，故玩家侧输入不受影响。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:5575-5580 (案体六条指令原文);
    ///       staging/update_clothes_4637_ida_work/disp_decomp.txt:2200-2206 (case 265/355/404/453/458 共用体);
    ///       staging/gm_guild_castle_commands_20260731.md:59 (selfAddGuild 453 = SysMsg "Invalid", no effect, 0x38FF);
    ///       staging/gm_full_inventory_20260731.md idx453 (perm4, addr 0x00628B25)。
    /// 补齐原因: census 有该命令、C# 此前无同名 handler；与同组 404/265/355 对称补齐。
    /// </summary>
    [GameCommand("selfAddGuild", "GM将自身加入行会", "行会名", 4)]
    public class SelfAddGuildCommand : BaseCommond
    {
        [DefaultCommand]
        public void SelfAddGuild(string[] @Params, TPlayObject PlayObject)
        {
            // 原版恒定回字面量 "Invalid"(0x38FF 错误色 → MsgColor.Red)且无任何副作用；参数不读。
            PlayObject.SysMsg("Invalid", MsgColor.Red, MsgType.Hint);
        }
    }
}
