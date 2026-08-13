namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// MOVE-39 —— 人形 mover <c>sub_741224</c>（VMT+0x30 的 THumanKind/TPlayer/THeroAct 槽）
        /// 在提交坐标之后多做一件怪物 mover <c>sub_71F0F4</c> 没有的事。两个 mover 的成功尾部
        /// 前半段完全同构：
        /// <code>
        /// 人形 0x7412D5 mov [ebx+0x12C],esi   怪物 0x71F203 mov [ebx+0x12C],esi   ; 提交 X
        /// 人形 0x7412DB mov [ebx+0x130],eax   怪物 0x71F209 mov [ebx+0x130],edi   ; 提交 Y
        /// 人形 0x7412E8 mov dl,0x17 / call 0x76B4D0   怪物 0x71F21C 同           ; 清定时状态 0x17
        /// 人形 0x74130D mov dx,0x2712 / call [vmt+0xD8]                          ; 广播 RM_WALK
        /// 人形 0x74131B call 0x778EC0        怪物 0x71F231 同                     ; 落格事件/传送门
        /// </code>
        /// 之后人形独有：
        /// <code>
        /// 0x741328  mov  dl,0x33
        /// 0x74132C  call 0x772960            ; InBodyState(0x33) 单人坐骑态
        /// 0x741333  je   0x741355            ; 不在坐骑态 -> 直接返回
        /// 0x741335  cmp  dword [ebx+0x3C0],0 ; 双人坐骑同伴指针
        /// 0x74133C  je   0x741355
        /// 0x74133E  mov  al,[ebx+0x154]      ; 自己的朝向
        /// 0x741345  mov  ecx,[ebp-0xC]       ; 新 Y
        /// 0x741348  mov  edx,esi             ; 新 X
        /// 0x74134A  mov  eax,[ebx+0x3C0]     ; **接收者是同伴，不是自己**
        /// 0x741350  call 0x6BBEE4            ; 把同伴拖到我的新格
        /// </code>
        /// <c>sub_6BBEE4</c> 的四个直接 xref 全在坐骑簇：0x6EE8DC(接受双人坐骑，把乘客搬到
        /// 驾驶者格)、0x741350(本条 walk)、0x767683(CM_RUN 3013 mover sub_76756C 尾)、
        /// 0x7677B4(CM_RUN3 4108 mover sub_767694 尾)。分片 21 把它记成"英雄跟随"是误判：
        /// <c>[+0x3C0]</c> 的 9 个写点全在坐骑簇，本端已落为 <c>m_NativeHorsePartner</c>
        /// （见 TPlayObject.NativeGroupProtocol.cs 对 sub_6BBE84 的取证）。
        /// <para>
        /// 基类实现为空 = 怪物 mover 分支（<c>sub_71F0F4</c> 尾部止于 0x71F231，无此调用）。
        /// </para>
        /// </summary>
        protected virtual void OnNativeHumanWalkMoverCommitted()
        {
        }
    }
}
