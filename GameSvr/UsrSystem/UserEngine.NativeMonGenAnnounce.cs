using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    /// <summary>
    /// SPWN-14：刷怪生成器的「生成播报」。
    ///
    /// 生成器记录 <c>TMonGen</c> 的最后一个槽 <c>[gen+0x40]</c> 是一个 elSize=1 的
    /// 动态数组（RTTI <c>0x7745B0 -&gt; 0x7745B4: 11 02 ".2" 01 00 00 00</c>，即
    /// <c>array of Char</c>）。每成功生成一只怪，只要该数组非空就往所有网关广播一条
    /// SM_SYSMESSAGE。刷怪 worker sub_67C9E0 与延迟生成队列 sub_67BF84 里是**同一段
    /// 代码的两份拷贝**，字节级一致：
    /// <code>
    /// ; sub_67C9E0 (worker)                        ; sub_67BF84 (延迟队列)
    /// 67CA5A  8B 45 F8              mov eax,[ebp-8]        67C01B  8B 45 FC
    /// 67CA5D  8B 40 40              mov eax,[eax+0x40]     67C01E  8B 40 40
    /// 67CA60  E8 23 A0 D8 FF        call 0x406A88          67C021  E8 62 AA D8 FF   ; _DynArrayLength
    /// 67CA65  85 C0 / 7E 29         test eax,eax / jle     67C026  85 C0 / 7E 4C
    /// 67CA6F  E8 14 A0 D8 FF        call 0x406A88          67C030  ...              ; 再取一次长度
    /// 67CA74  50                    push eax               67C035  50               ; nLen
    /// 67CA7B  50                    push eax               67C03C  50               ; Buf = @arr[0]
    /// 67CA7C  6A 01                 push 1                 67C03D  6A 01            ; 被 5F6F9C 忽略的形参
    /// 67CA7E  A1 3C 59 7D 00        mov eax,[0x7D593C]     67C03F  ...              ; 网关管理器
    /// 67CA83  8B 00                 mov eax,[eax]
    /// 67CA85  66 B9 FF 38           mov cx,0x38FF                                   ; wParam
    /// 67CA89  66 BA 64 00           mov dx,0x64                                     ; wIdent = SM_SYSMESSAGE
    /// 67CA8D  E8 0A A5 F7 FF        call 0x5F6F9C          67C04E  E8 49 AF F7 FF
    /// </code>
    /// <c>0x406A88</c> 是 <c>_DynArrayLength</c>（<c>test eax,eax / jz / mov eax,[eax-4]</c>；
    /// 同一函数被 <c>0x406A90 = _DynArrayHigh</c> 以 <c>call/dec</c> 包装，而
    /// <c>_DynArrayHigh</c> 正是 worker 用来量 CertList 长度的那个）。
    ///
    /// <c>sub_5F6F9C</c> 把参数摊成一个 0x1C 字节栈帧再逐网关下发：
    /// <code>
    /// 5F6FD0  C7 45 E0 77 BB AA 33  mov dword [ebp-0x20],0x33AABB77   ; 魔数
    /// 5F6FD7  66 C7 45 EC 12 00     mov word  [ebp-0x14],0x12         ; 帧类型 18
    /// 5F6FDD  66 89 5D EE           mov word  [ebp-0x12],bx           ; 长度 = 0x0C + 数据长
    /// 5F6FE1  66 89 7D F4           mov word  [ebp-0x0C],di           ; TDefaultMessage.Ident
    /// 5F6FE5  66 8B 45 FE / 66 89 45 F6                               ; TDefaultMessage.Param
    /// 5F6FF2  8B 84 9E 0C 03 00 00  mov eax,[esi+ebx*4+0x30C]         ; ebx = 1..0x20，32 个网关槽
    /// 5F7007  E8 5C FA FF FF        call 0x5F6A68                     ; 写进该网关的 0x8000 发送缓冲
    /// </code>
    /// 也就是 <see cref="LegacyGateType18"/>：16 字节外层（魔数 / 连接号 /
    /// FilterUserIndex / 类型 18 / 长度）+ 12 字节 <c>TDefaultMessage</c>
    /// (Recog, Ident, Param, Tag, Series) + 文本。<c>Recog/Tag/Series</c> 全 0
    /// （0x5F6FCB 先 <c>FillChar(buf,0x1C,0)</c>）。
    ///
    /// 同一 (wIdent=0x64, wParam=0x38FF) 组合的旁证：全服喊话 <c>!!</c> 的处理
    /// 分支 0x6BB471 与 0x6BB4B8 用完全相同的两个立即数调 <c>sub_5F6F9C</c>，
    /// 只是把玩家聊天缓冲 <c>[esi+2]</c>/<c>edi-2</c> 当数据。
    /// </summary>
    public partial class UserEngine
    {
        /// <summary>0x67CA89 <c>mov dx,0x64</c> —— SM_SYSMESSAGE。</summary>
        public const ushort MonGenAnnounceIdent = 100;

        /// <summary>0x67CA85 <c>mov cx,0x38FF</c>。</summary>
        public const ushort MonGenAnnounceParam = 0x38FF;

        /// <summary>
        /// 0x67CA7C / 0x67C03D 的 <c>push 1</c>。<c>sub_5F6F9C</c> 从不读
        /// <c>[ebp+8]</c>（函数体内该槽 0 引用），但它确实占了外层帧的
        /// FilterUserIndex 位置，所以照抄以保持字节级一致。
        /// </summary>
        public const uint MonGenAnnounceFilterUserIndex = 1;

        /// <summary>
        /// 0x67CA5A..0x67CA8D（worker）/ 0x67C01B..0x67C04E（延迟队列）。
        /// 每成功生成一只怪调用一次，放在字段搬运之后、把怪挂进 CertList 之前。
        /// </summary>
        public void NativeMonGenAnnounceSpawn(MonGenInfo monGen)
        {
            // 0x67CA60 _DynArrayLength + 0x67CA65 `test eax,eax / jle`：
            // 空数组（nil）长度 0 -> 不播报。
            var text = monGen?.GenAnnounceBytes;
            if (text == null || text.Length <= 0) return;

            M2Share.GateManager?.BroadcastLegacyType18(new LegacyGateType18
            {
                FilterUserIndex = MonGenAnnounceFilterUserIndex,
                Recog = 0,
                Ident = MonGenAnnounceIdent,
                Param = MonGenAnnounceParam,
                TextBytes = text
            });
        }
    }
}
