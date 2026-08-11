using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @GetGuildMember (idx355, perm3) —— 原版恒定只回字面量 "Invalid"，不做任何查询。
    /// 原版 case@0x00627FBC 完整案体只有六条指令(byte-level 逐字):
    ///     0x00627FBC  mov     cx, 38FFh              ; 颜色 ident 0x38FF = 红(错误色)
    ///     0x00627FC0  mov     edx, offset aInvalid   ; 字面量 "Invalid"
    ///     0x00627FC5  mov     eax, [ebp+var_8]       ; self
    ///     0x00627FC8  mov     ebx, [eax]             ; VMT
    ///     0x00627FCA  call    dword ptr [ebx+0D4h]   ; vtbl+0xD4 = SysMsg
    ///     0x00627FD0  jmp     loc_62B64C             ; 共享空出口
    /// 关键: 案体**从不读参数槽**，所以帮助文本里的"[无/行会名] 称号名 最大数量"三个参数全被忽略;
    /// 也**没有任何行会成员枚举**——按封号取成员名单这件事在本 build 里从未落地。
    /// 该案体与 idx 265/404/453/458 **共用同一段代码**(反编译里五个 case 标签叠在一起, 见
    /// disp_decomp.txt:2200-2206)，即二进制的 "Invalid" 占位组；既有 GuildForbidCommand.cs(404) 同契约。
    /// 颜色: 0x38FF == MsgColor.Red (memory sysmsg-cx-color-packing)。
    /// ⚠ 该命令 perm=3 (比同组其他四个的 perm4 低)，按 census 的 +0x1C 字段照抄，勿统一成 4。
    /// ⚠ 不要照帮助文本去实现真枚举: 原版此路径无任何查询/输出，实现即发散(over-send)。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:4836-4841 (案体六条指令原文);
    ///       staging/update_clothes_4637_ida_work/disp_decomp.txt:2200-2206 (case 265/355/404/453/458 共用体);
    ///       staging/gm_full_inventory_20260731.md idx355 (perm3, addr 0x00627FBC, IMPL,
    ///       help "获取行会封号下成员名字(全部行会/指定行会) @GetGuildMember [无/行会名] 称号名 最大数量")。
    /// 补齐原因: census 有该命令、C# 此前无同名 handler；与同组 404/265 对称补齐。
    /// </summary>
    [GameCommand("GetGuildMember", "获取行会封号下成员名字", "[无/行会名] 称号名 最大数量", 3)]
    public class GetGuildMemberCommand : BaseCommond
    {
        [DefaultCommand]
        public void GetGuildMember(string[] @Params, TPlayObject PlayObject)
        {
            // 原版恒定回字面量 "Invalid"(0x38FF 错误色 → MsgColor.Red)且无任何副作用；参数不读。
            PlayObject.SysMsg("Invalid", MsgColor.Red, MsgType.Hint);
        }
    }
}
