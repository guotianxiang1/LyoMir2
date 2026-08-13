namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// 战神 <c>word[obj+0x38]</c>（SmallInt，单位：秒）—— 死亡后到
        /// <c>MakeGhost</c> 之间的尸体存留时间。
        ///
        /// 构造函数默认 60：
        /// <code>
        /// 00764E20  55 8B EC 53 56 84 D2 ...   ; TBaseObject 构造（0x764E39 call 0x404660 = inherited Create）
        /// 00764E5F  C6 86 78 01 00 00 32       mov byte [esi+0x178],0x32   ; m_btRaceServer = 50
        /// 00764E9E  66 C7 46 38 3C 00          mov word [esi+0x38],0x3C    ; &lt;-- 本字段 = 60
        /// 00764EA4  8D 86 64 02 00 00          lea eax,[esi+0x264]         ; 与 Run 里 0x7665C1 同一 +0x264 子记录
        /// </code>
        ///
        /// 全镜像唯一的消费点在 <c>Run</c> 的死亡分支
        /// （<c>0x7665FD cmp byte [Self+0x74],0 / 0x766601 jne 0x766674</c>，
        /// <c>+0x74 = m_boDeath</c>、<c>+0x330 = m_dwDeathTick</c>，
        /// 二者已由 TBaseObject.Base.cs 的 Die() 注释钉死）：
        /// <code>
        /// 00766674  8B 45 FC              mov eax,[ebp-4]              ; Self
        /// 00766677  8B D6                 mov edx,esi                  ; esi = GetTickCount()
        /// 00766679  2B 90 30 03 00 00     sub edx,[eax+0x330]          ; - m_dwDeathTick
        /// 0076667F  8B 45 FC              mov eax,[ebp-4]
        /// 00766682  0F BF 40 38           movsx eax,word [eax+0x38]    ; &lt;-- 本字段
        /// 00766686  69 C0 E8 03 00 00     imul eax,eax,1000            ; 秒 -&gt; 毫秒
        /// 0076668C  3B D0 / 72 0F         cmp edx,eax / jb 0x76669F
        /// 0076669A  E8 C1 19 00 00        call 0x768060                ; MakeGhost / MarkDelete
        /// </code>
        /// <c>sub_768060</c> 的身份由它自己的异常串确认：
        /// <c>'[Exception]: TCreature.MarkDelete Cret的地图无效'</c>（0x768138）；
        /// 函数体 <c>0x7680EF C6 43 73 01</c> 置 <c>m_boGhost</c>、
        /// <c>0x7680F8</c> 写 <c>[obj+0x14C] = GetTickCount</c>。
        ///
        /// 全镜像对 <c>word[obj+0x38]</c> 的写入只有四处：上面的构造函数默认值，
        /// 以及三条刷怪生成路径（0x67BD9F / 0x67BFFD / 0x67CA56），后三处一律
        /// 拷自 <c>MonGenInfo.nCorpseSeconds</c>（<c>dword[gen+0x28]</c> 的低字）。
        /// </summary>
        public short m_wNativeCorpseSeconds = NativeDefaultCorpseSeconds;

        /// <summary>0x764E9E 的构造函数默认值。</summary>
        public const short NativeDefaultCorpseSeconds = 60;

        /// <summary>
        /// SPWN-13 的搬运动作：把生成器的 <c>[gen+0x28]</c> 低字写进
        /// <c>word[obj+0x38]</c>。<paramref name="unconditional"/> 区分两种原生写法 ——
        /// worker sub_67C9E0（0x67CA49）与延迟队列 sub_67BF84（0x67BFF0）先测
        /// <c>cmp dword [gen+0x28],0</c> 再拷；全量重刷 sub_67BD0C（0x67BD9B）不测，
        /// 直接拷（生成器第 8 列为 0 时会把尸体时间压成 0）。
        /// 注意 <c>cmp</c> 是 dword、<c>mov</c> 是 word：门看整型全宽，落地只取低 16 位。
        /// </summary>
        public void ApplyNativeMonGenCorpseSeconds(MonGenInfo monGen,
            bool unconditional = false)
        {
            if (monGen == null) return;
            if (!unconditional && monGen.nCorpseSeconds == 0) return;
            m_wNativeCorpseSeconds = unchecked((short)monGen.nCorpseSeconds);
        }

        /// <summary>
        /// 0x766674..0x76668E 的判据：死亡后经过的毫秒数是否已达
        /// <c>m_wNativeCorpseSeconds * 1000</c>。
        ///
        /// 取值用 <c>movsx</c>（有符号扩展）而比较用 <c>jb</c>（无符号），两者
        /// 必须分别照搬：<c>0x3E8</c> 乘出来的负数在 <c>cmp edx,eax</c> 里被当成
        /// 接近 2^32 的大数，<c>jb</c> 恒成立 → 跳过 <c>MakeGhost</c>。也就是说
        /// 负数秒（例如 mongen.txt 第 8 列填 40000，低 16 位 = -25536）在战神里
        /// 表示"尸体几乎永不消失"，不是"立刻消失"。秒数为 0 时 <c>edx &gt;= 0</c>
        /// 恒真，死亡后第一次 Run 就变 ghost。
        /// </summary>
        public bool NativeCorpseGhostDue(int dwCurrentTick)
        {
            return unchecked((uint)(dwCurrentTick - m_dwDeathTick))
                   >= unchecked((uint)(m_wNativeCorpseSeconds * 1000));
        }
    }
}
