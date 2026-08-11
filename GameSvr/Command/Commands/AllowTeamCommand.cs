using GameSvr.CommandSystem;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @AllowTeam (idx256, perm3) —— 无条件把【自身】的组队开关置 1(允许组队)。
    /// 原版 case@0x006264F4 完整案体只有三条指令(byte-level 逐字):
    ///     0x006264F4  mov     eax, [ebp+var_8]        ; var_8 = self(GM 自己)
    ///     0x006264F7  mov     byte ptr [eax+0BA1h], 1 ; 无条件写 1，不是 xor/toggle
    ///     0x006264FE  jmp     loc_62B64C              ; 共享空出口 → 无 SysMsg、无广播
    /// 三点务必照抄:
    ///   1) **置位而非取反** —— 原版是 `mov ...,1`，重复执行仍为 1(幂等)，不能写成 `!flag`;
    ///   2) **不读参数** —— 案体从不碰 var_34/var_38 参数槽，多余参数一律忽略;
    ///   3) **完全静默** —— 落到 loc_62B64C 共享空出口(xor eax,eax + SEH 收尾)，无任何 vtbl 调用，
    ///      故既不发 SysMsg 也不发组队状态包(对照 @AllowGroupReCall 是 toggle+双消息，语义不同)。
    /// 字段: obj+0xBA1 == C# m_boAllowGroup (TBaseObject.cs:73)。
    ///   依据 memory ba4-is-tiandiheyi-not-allowgroup: 运行期组队模式就是 obj+0xBA1
    ///   (sub_6C33CC@0x6C33D8 的 -4 分支)，且 13 处引用无一处在 LOAD/SAVE 里 → 从不持久化;
    ///   ⚠ 不要错绑 obj+0xBA4(=天地合一, rec 0xD7) 或 obj+0xBA5(=持久化的 btAllowGroup, rec 0xDE)。
    /// 证据: staging/update_clothes_4637_ida_work/big622820.txt:3146-3148 (上述三条指令原文);
    ///       staging/gm_full_inventory_20260731.md idx256 (perm3, addr 0x006264F4, IMPL,
    ///       help "允许组队 @AllowTeam" —— 帮助文本亦无参数)。
    /// 补齐原因: census 有该命令、C# 此前无同名 handler(缺 1:1)；纯自身 bool 写、不持久化、无经济/物品风险。
    /// </summary>
    [GameCommand("AllowTeam", "允许组队(自身)", "", 3)]
    public class AllowTeamCommand : BaseCommond
    {
        [DefaultCommand]
        public void AllowTeam(TPlayObject PlayObject)
        {
            // 原版 `mov byte ptr [self+0BA1h], 1`：无条件置 true(非取反)，且不发任何消息。
            PlayObject.m_boAllowGroup = true;
        }
    }
}
