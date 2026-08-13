namespace GameSvr
{
    public class MonGenInfo
    {
        public string sMapName;
        public int nX;
        public int nY;
        public string sMonName;
        public int nRange;
        public int nCount;
        public int nActiveCount;
        public int dwZenTime;
        public int nMissionGenRate;
        /// <summary>
        /// 战神 <c>dword[gen+0x28]</c>: MonGen.txt 第 8 列（文件头注释里写作
        /// 「修正（正数%）」/「？？？？（%）」，实为**尸体存留秒数**），0 = 不覆盖。
        /// 记录类型由 RTTI 钉死：<c>[0x7745D0] -&gt; 0x7745D4</c> 是
        /// <c>0E 07 "TMonGen" 44000000 02000000</c>，即 <c>TMonGen</c>、size 0x44、
        /// 两个托管字段 <c>+0x3C</c>（dyn array，elSize 4 = CertList）与
        /// <c>+0x40</c>（dyn array，elSize 1 = <see cref="GenAnnounceBytes"/>），
        /// 声明单元 <c>ObjMap</c>。其余槽位由 ProcessMonsters sub_67C150 与
        /// 工厂调用点逐一钉死：+0x00 Envir / +0x04 nX / +0x08 nY /
        /// +0x0C sMonName:string[15]（0x67C282 <c>cmp byte [esi+0x0C],0</c> 读的是
        /// ShortString 长度字节）/ +0x1C 怪物模板指针 / +0x20 nRange /
        /// +0x24 nCount / +0x2C 存活数 / +0x30 dwZenTime（0x67C294
        /// <c>cmp eax,[esi+0x30]</c>）/ +0x34 dwStartTick / +0x38 nFailCount。
        /// 唯一没被别的列占用的整型槽就是 +0x28，故第 8 列落此。
        ///
        /// 三条生成路径读它，两条带非零门、一条无条件：
        /// <code>
        /// 67CA49  83 7B 28 00     cmp dword [ebx+0x28],0     ; sub_67C9E0 刷怪 worker
        /// 67CA4D  74 0B           je  0x67CA5A
        /// 67CA52  66 8B 53 28     mov dx,word [ebx+0x28]
        /// 67CA56  66 89 50 38     mov word [eax+0x38],dx     ; -&gt; 怪物侧
        /// 67BFF0  83 78 28 00 ...                            ; sub_67BF84 延迟生成队列
        /// 67BD9B  66 8B 53 28     mov dx,word [ebx+0x28]     ; sub_67BD0C 全量重刷（无门）
        /// 67BD9F  66 89 50 38     mov word [eax+0x38],dx
        /// </code>
        /// 脚本生成路径 sub_67BDCC 会显式写死 60：<c>0x67BEE4 C7 40 28 3C 00 00 00</c>。
        /// 目标字段见 <c>TBaseObject.m_wNativeCorpseSeconds</c>。
        /// </summary>
        public int nCorpseSeconds;
        /// <summary>
        /// 战神 <c>[gen+0x40]</c>: MonGen.txt 第 9 列 —— 生成播报文本。RTTI
        /// （<c>0x7745B0 -&gt; 0x7745B4: 11 02 ".2" 01000000</c>）说明它是
        /// <c>array of Char</c>（tkDynArray，elSize 1），所以这里用 GBK 字节数组
        /// 1:1 承接，<c>Length()</c> 就是字节数。真实 ys207/ys208 抓包的
        /// mongen.txt 里共 70 行九列，例如
        /// <c>66 212 200 魔龙教主 200 1 120 0 魔龙教主：兄弟们给我上,杀光他们！</c>。
        /// 消费点见 <c>UserEngine.NativeMonGenAnnounceSpawn</c>。
        /// </summary>
        public byte[] GenAnnounceBytes;
        public IList<TBaseObject> CertList;
        public int CertCount;
        public object Envir;
        public int nRace;
        public int dwStartTick;
        /// <summary>
        /// 战神 <c>dword[gen+0x38]</c>: consecutive factory failures for this generator.
        /// The worker bumps it when the factory returns nil (<c>0x67CAB8 inc dword
        /// [ebx+0x38]</c>) and zeroes it on any success (<c>0x67CAA7 xor eax,eax /
        /// 0x67CAA9 mov [ebx+0x38],eax</c>).  Its only reader turns it into the
        /// factory's fourth argument:
        /// <code>
        /// 67CA2B  83 7B 38 05  cmp dword [ebx+0x38],5
        /// 67CA2F  0F 9D C0     setge al
        /// 67CA32  50           push eax
        /// </code>
        /// which sub_679F8C forwards twice into sub_7782D0 (0x679FE9 / 0x679FED) and
        /// sub_7782D0 hands to CanWalk sub_777EF8 at 0x77834B, where
        /// <c>0x777F70 cmp byte [ebp+8],0 / jne</c> returns true without scanning the
        /// cell's object list.  So five failures in a row let the next attempt land on
        /// a tile another creature is standing on.
        /// </summary>
        public int nFailCount;
    }
}