using SystemModule;

namespace GameSvr
{
    // MOVE-73 / MOVE-74 —— Obj[+0x3FE] 穿透缓存的 tick 刷新与 0xB05 变迁广播。
    //
    // 归属裁定（本轮全镜像穷举，见 tools/move74_off3fe_census.py）：
    // Obj[+0x3FE] 共 28 个访问点，**写点只有一个** —— 0x6B30A3，就在下面复刻的这段里。
    // 其余 27 个全是读点，且每一个读到的字节都只做同一件事：当 boIgnoreOccupancy
    // 交给 WalkTo(vmt+0x30) / CanWalkEx(sub_777EF8) / MoveToMovingObject(sub_7797CC)，
    // 或者当「能穿人就别挤人」的闸（0x6661AB、0x6B3206）。没有任何一个读点与
    // 名字颜色、PK 状态或安全区显示相关。
    //   0x673943  8A 83 FE 03 00 00 / 50           mov al,[ebx+0x3FE] / push  -> call 0x777EF8
    //   0x6BBD0C  8A 8B FE 03 00 00                mov cl,[ebx+0x3FE]        -> call [edi+0x30]
    //   0x767601  8A 83 FE 03 00 00 / 50           mov al,[ebx+0x3FE] / push  -> call 0x7797CC
    //   0x6B3206  80 BA FE 03 00 00 00 / 0F 85 ..  cmp byte [edx+0x3FE],0 / jne 0x6B32A1
    // 所以 [+0x3FE] 是**穿透缓存**，不是 InSafeArea。
    //
    // 生产者同样唯一：sub_768454 全镜像只有 0x6B308E 一个引用（rel32 与 dword 双扫，
    // 见 tools/move74_callxref.py），即「每 tick 算一次、只在变化时回写」。
    public partial class TPlayObject
    {
        /// <summary>
        /// 原生 sub_6B2D38（玩家 tick）里 0x6B308B..0x6B30E1 这一整段：
        /// <code>
        /// 006B308B  8B 45 FC              mov  eax,[ebp-4]          ; Self
        /// 006B308E  E8 C1 53 0B 00        call 0x768454             ; 重算穿透判定
        /// 006B3093  8B 55 FC              mov  edx,[ebp-4]
        /// 006B3096  3A 82 FE 03 00 00     cmp  al,[edx+0x3FE]       ; 与旧缓存比较
        /// 006B309C  74 43                 je   0x6B30E1             ; 没变 -> 不写、不发包
        /// 006B309E  8B 4D FC              mov  ecx,[ebp-4]
        /// 006B30A1  8B D0                 mov  edx,eax
        /// 006B30A3  88 91 FE 03 00 00     mov  [ecx+0x3FE],dl       ; 变了才回写
        /// 006B30A9  84 D2 / 74 1B         test dl,dl / je 0x6B30C8
        /// 006B30AD  6A 06 / 6A 01 / 6A 00 / 6A 00      ; TRUE  臂 Param=6 Tag=1 Series=0 sMsg=nil
        /// 006B30B5  33 C9 / 66 BA 05 0B   xor ecx,ecx / mov dx,0xB05
        /// 006B30C0  FF 93 50 02 00 00     call [ebx+0x250]          ; SendDefMessage
        /// 006B30C6  EB 19                 jmp  0x6B30E1
        /// 006B30C8  6A 06 / 6A 00 / 6A 00 / 6A 00      ; FALSE 臂 Param=6 Tag=0 Series=0 sMsg=nil
        /// 006B30DB  FF 93 50 02 00 00     call [ebx+0x250]
        /// </code>
        /// 三条与移植前不同的事实，逐条有字节支撑：
        /// <list type="number">
        /// <item>谓词是 <c>sub_768454</c>（穿透判定），不是 InSafeArea。
        /// <c>sub_768454</c> 的 C# 复刻是 <c>NativeComputeThroughOccupancy()</c>。</item>
        /// <item>**无条件每 tick 求值**。上游两条分支
        /// 0x6B3046 <c>je 0x6B308B</c> 与 0x6B305A <c>jbe 0x6B308B</c> 都是**跳到**
        /// 0x6B308B，不是跳过它；0x6B2FFB..0x6B30E1 之间没有任何时间闸，
        /// 也没有任何 <c>m_MyGuild</c> / 行会战判断。</item>
        /// <item>**只在变化时**回写并广播（0x6B309C 的 je）。</item>
        /// </list>
        /// </summary>
        internal void NativeTickThroughOccupancyTransition()
        {
            var boThrough = NativeComputeThroughOccupancy();
            if (boThrough == m_boThroughOccupancyCache)
            {
                return;
            }
            m_boThroughOccupancyCache = boThrough;
            SendDefMessage(Grobal2.SM_COMMON_INFORMATION, 0, 6,
                boThrough ? 1 : 0, 0, string.Empty);
        }
    }
}
