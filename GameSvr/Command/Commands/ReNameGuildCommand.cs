using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @ReNameGuild (idx458, perm4) —— 原版恒定只回字面量 "Invalid"，不做任何状态变更。
    /// 原版 case@0x00628991 完整案体只有六条指令(byte-level 逐字):
    ///     0x00628991  mov     cx, 38FFh              ; 颜色 ident 0x38FF = 红(错误色)
    ///     0x00628995  mov     edx, offset aInvalid   ; 字面量 "Invalid"
    ///     0x0062899A  mov     eax, [ebp+var_8]       ; self
    ///     0x0062899D  mov     ebx, [eax]             ; VMT
    ///     0x0062899F  call    dword ptr [ebx+0D4h]   ; vtbl+0xD4 = SysMsg
    ///     0x006289A5  jmp     loc_62B64C             ; 共享空出口
    /// 关键: 案体**从不读参数槽**(旧/新行会名都被忽略)，**没有行会改名写**，
    /// 更**没有任何 SQL** —— 帮助文本吹的"db数据也更改了"在本 build 里完全不存在。
    /// 这一点尤其要按字节判据认定: 同项目里真正落地的行会写族是**进程内直连 MySQL**
    /// (memory gild-social-writes-direct-mysql: 8 条 SQL 逐字 0x5E9414~0x5E9D38)，
    /// 而本案体六条指令里既无 SQL 字符串引用、也无那批写函数的 call —— 故属占位而非"走了别处"。
    /// 该案体与 idx 265/355/404/453 **共用同一段代码**(反编译里五个 case 标签叠在一起, 见
    /// disp_decomp.txt:2200-2206)，即二进制的 "Invalid" 占位组；既有 GuildForbidCommand.cs(404) 同契约。
    /// 颜色: 0x38FF == MsgColor.Red (memory sysmsg-cx-color-packing)。
    /// ⚠ 不要照帮助文本去实现真改名+落库: 那会同时伪造游戏内状态与 DB 写，是最危险的一类发散。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:5461-5466 (案体六条指令原文);
    ///       staging/update_clothes_4637_ida_work/disp_decomp.txt:2200-2206 (case 265/355/404/453/458 共用体);
    ///       staging/gm_guild_castle_commands_20260731.md:60 (ReNameGuild 458 = SysMsg "Invalid", no effect, 0x38FF);
    ///       staging/gm_full_inventory_20260731.md idx458 (perm4, addr 0x00628991)。
    /// 补齐原因: census 有该命令、C# 此前无同名 handler；本文件补齐后 "Invalid" 占位组 5/5 完整
    ///           (265/355/404/453/458)。
    /// </summary>
    [GameCommand("ReNameGuild", "给行会改名字", "旧行会名字 新行会名字", 4)]
    public class ReNameGuildCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReNameGuild(string[] @Params, TPlayObject PlayObject)
        {
            // 原版恒定回字面量 "Invalid"(0x38FF 错误色 → MsgColor.Red)：不改行会名、不写 DB。参数不读。
            PlayObject.SysMsg("Invalid", MsgColor.Red, MsgType.Hint);
        }
    }
}
