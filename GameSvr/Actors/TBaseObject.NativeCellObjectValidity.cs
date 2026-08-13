namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// 战神 <c>sub_765D64</c>（VA 0x765D64–0x765D92）逐字节还原。
        ///
        /// <code>
        /// 765D64  55 8B EC 53 56        push ebp / mov ebp,esp / push ebx / push esi
        /// 765D69  8B F0                 mov esi,eax               ; esi = Self（__fastcall EAX）
        /// 765D6B  33 DB                 xor ebx,ebx               ; Result := False
        /// 765D6D  80 BE 06 01 00 00 00  cmp byte [esi+0x106],0    ; Length(CName) = 0 ?
        /// 765D74  74 17                 je  0x765D8D
        /// 765D76  83 BE 28 01 00 00 00  cmp dword [esi+0x128],0   ; PEnvir = nil ?
        /// 765D7D  74 0E                 je  0x765D8D
        /// 765D7F  8B 86 28 01 00 00     mov eax,[esi+0x128]
        /// 765D85  83 78 44 00           cmp dword [eax+0x44],0    ; PEnvir.MapName = '' ?
        /// 765D89  74 02                 je  0x765D8D
        /// 765D8B  B3 01                 mov bl,1                  ; Result := True
        /// 765D8D  8B C3 5E 5B 5D C3     mov eax,ebx / pop×3 / ret
        /// </code>
        ///
        /// 返回值语义：<b>真 = 对象有效</b>（三项合取，短路），假 = 无效。全部 13 个调用点
        /// 都用 <c>test al,al</c> 后按「非零走正常臂 / 零走摘链+异常日志臂」分流。
        ///
        /// 三个槽的身份（独立复核，非推测）：
        /// <list type="bullet">
        /// <item><c>+0x106</c> = <c>CName</c> ShortString 的长度字节。0x71FB34
        /// <c>add edx,0x106</c> → 0x71FB3A <c>call 0x405774</c>，而 0x405774 是 Delphi
        /// <c>@LStrFromString</c>（<c>xor ecx,ecx / mov cl,[edx] / inc edx / jmp 0x4055F0</c>），
        /// 即从 <c>+0x106</c> 处取长度字节。</item>
        /// <item><c>+0x128</c> = <c>PEnvir</c>。0x77AE80 <c>mov [ebx+0x128],esi</c>，esi 是刚
        /// 解析出的地图对象；全镜像 25 处 dword 写点无一写常量 0。</item>
        /// <item><c>[PEnvir+0x44]</c> = 地图名 AnsiString。0x77AE5D <c>mov edx,[esi+0x44]</c>
        /// → <c>call 0x4057AC</c> → <c>call 0x4039E4 (cl=0x0F)</c> 拷进 <c>[npc+0x115]</c>；
        /// AnsiString 的指针为 nil 当且仅当串为 ''。</item>
        /// </list>
        ///
        /// 作者本人的标签坐实了语义：六个失败臂拼的诊断串都写
        /// <c>"...CName = 空 ..."</c>（0x77A81C / 0x6DC4C8 / 0x6DC93C / 0x6DCD9C /
        /// 0x7656D4 / 0x765B1C）。这是「对象失效/悬挂」探针，<b>不是</b>死亡谓词——
        /// 死亡/幽灵谓词是相邻的另一个函数 <c>sub_765D94</c>（读 <c>+0x73</c> 幽灵、
        /// <c>sub_772DA8 = mov al,[eax+0x74]</c> 死亡、<c>+0x2E3</c>、<c>+0x2E6</c>）。
        ///
        /// 热点约束：本方法为 O(1)、零分配（<c>string.IsNullOrEmpty</c> 只读 Length），
        /// 与原生的三次内存比较同量级。
        /// </summary>
        public static bool IsNativeCellObjectValid(TBaseObject actor)
        {
            if (actor == null)
            {
                return false;
            }
            if (string.IsNullOrEmpty(actor.m_sCharName))
            {
                return false;   // 0x765D6D cmp byte [esi+0x106],0
            }
            Envirnoment envir = actor.m_PEnvir;
            if (envir == null)
            {
                return false;   // 0x765D76 cmp dword [esi+0x128],0
            }
            return !string.IsNullOrEmpty(envir.sMapName);   // 0x765D85 cmp dword [eax+0x44],0
        }

        /// <summary>
        /// 格内 <c>OS_MOVINGOBJECT</c> 节点的摘链谓词，对应 0x77A2D6–0x77A2F2 那一段：
        ///
        /// <code>
        /// 77A2D6  8B 06 / 8B 40 04      eax := Curr^.POject       ; node+4
        /// 77A2DB  89 45 CC              [ebp-0x34] := eax
        /// 77A2DE  83 7D CC 00           cmp [ebp-0x34],0
        /// 77A2E2  0F 84 30 04 00 00     je  0x77A718              ; POject = nil -> 纯 continue，不摘链
        /// 77A2E8  8B 45 CC              eax := POject
        /// 77A2EB  E8 74 BA FE FF        call 0x765D64
        /// 77A2F0  84 C0                 test al,al
        /// 77A2F2  0F 85 B8 00 00 00     jne 0x77A3B0              ; 有效 -> 正常可见性处理
        /// ; 无效：0x77A2F8 摘链、0x77A312 bl:=1（抑制 prev 前进）、0x77A3A6 记异常、
        /// ;       0x77A3AB jmp 0x77A718 —— 是 continue，不是 break
        /// </code>
        ///
        /// 注意 <c>POject = nil</c> 与 <c>Valid = False</c> 是<b>两条不同的臂</b>：前者
        /// 只跳过、不摘链。故本方法对 null / 非 <see cref="TBaseObject"/> 载荷一律返回
        /// false（= 不摘链），只有「确实是一个 actor 且该 actor 无效」才返回 true。
        ///
        /// 族 A（格子链清道夫）的六个原生站点形状逐字节相同，都用这一条：
        /// <c>0x7777EA</c> <c>TEnvironment.AddToMap</c>（sub_7776EC）、
        /// <c>0x778030</c> <c>TEnvironment.CanWalk</c>（sub_777EF8）、
        /// <c>0x7788F9</c> <c>TEnvironment.GetMovObjCount</c>（sub_778858）、
        /// <c>0x7798C0</c> <c>TEnvironment.CreatureMoveTo</c>（sub_7797CC）、
        /// <c>0x77A2EB</c> <c>TEnvironment.DoPlayerSearchViewRange</c>（sub_77A178）、
        /// <c>0x77AB07</c> <c>TEnvironment.DoSearchTargetList</c>（sub_77A990）。
        /// </summary>
        public static bool IsNativeStaleCellActor(object cellObj)
        {
            return cellObj is TBaseObject actor && !IsNativeCellObjectValid(actor);
        }
    }
}
