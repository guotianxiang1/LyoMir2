using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @addGuildMem (idx444, perm3) —— 原版恒定只回字面量 "Can not insert directly"，无任何状态变更。
    /// 原版 case@0x00628AE1 完整案体只有六条指令(byte-level 逐字):
    ///     0x00628AE1  mov     cx, 38FFh                      ; 颜色 ident 0x38FF = 红(错误色)
    ///     0x00628AE5  mov     edx, offset aCanNotInsertDi    ; 字面量 "Can not insert directly"
    ///     0x00628AEA  mov     eax, [ebp+var_8]               ; self
    ///     0x00628AED  mov     ebx, [eax]                     ; VMT
    ///     0x00628AEF  call    dword ptr [ebx+0D4h]           ; vtbl+0xD4 = SysMsg
    ///     0x00628AF5  jmp     loc_62B64C                     ; 共享空出口
    /// 关键: 案体**从不读参数槽**(行会名/角色名/称号名全被忽略)，也**没有任何成员写**——
    /// "GM 把角色加进行会并赋称号"这件事在本 build 里从未落地；这条消息本身就是原版给出的拒绝理由。
    /// ⚠ 与 "Invalid" 占位组(265/355/404/453/458)**不是**同一段代码: 本 case 有**独立案体**，
    ///   字面量也不同(aCanNotInsertDi vs aInvalid)。在 disp_decomp.txt:3282 处 `case 444:` 是**单独**
    ///   标签(不像 265/355/404/453/458 五个标签叠在一起)，故必须照抄本文自己的文本，不能复用 "Invalid"。
    /// 颜色: 0x38FF == MsgColor.Red (memory sysmsg-cx-color-packing: 0xFFDB=Green/0x38FF=Red/0xFCFF=Blue)。
    /// 消息文本为 ASCII 字面量，逐字照抄(含空格、无句点、大小写)，不做本地化。
    /// 命令名大小写按 census 记录原文 `addGuildMem`(小写起头)；C# 命令匹配大小写不敏感
    /// (CommandManager 的 CommandMaps 用 OrdinalIgnoreCase)，故玩家侧输入不受影响。
    /// ⚠ 不要照帮助文本去实现真入会+赋称号: 原版此路径明确拒绝，实现即发散(且是假成功)。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:5558-5563 (案体六条指令原文,
    ///       含 aCanNotInsertDi = "Can not insert directly");
    ///       staging/update_clothes_4637_ida_work/disp_decomp.txt:3282-3285 (case 444 独立案体);
    ///       staging/gm_full_inventory_20260731.md idx444 (perm3, addr 0x00628AE1, IMPL,
    ///       help "GM将角色添加到某个行会中，并赋予称号 @addGuildMem 行会名 角色名 [称号名/无]")。
    /// 补齐原因: census 有该命令、C# 此前无同名 handler(缺 1:1)；纯拒绝回复，无任何行会/经济风险。
    /// </summary>
    [GameCommand("addGuildMem", "GM将角色添加到某个行会中并赋予称号", "行会名 角色名 [称号名/无]", 3)]
    public class AddGuildMemCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddGuildMem(string[] @Params, TPlayObject PlayObject)
        {
            // 原版恒定回字面量 "Can not insert directly"(0x38FF 错误色 → MsgColor.Red)且无任何副作用；
            // 参数不读。注意本命令的文本与 "Invalid" 占位组不同，属独立案体。
            PlayObject.SysMsg("Can not insert directly", MsgColor.Red, MsgType.Hint);
        }
    }
}
