namespace GameSvr
{
    /// <summary>
    /// PKD-08 —— 战神 <c>sub_767498</c> 的目标前置筛选梯。
    /// （2026-08-13 独立复核：0x767498..0x767503 逐字节重读一致；<c>E8</c> 直调者实测
    ///  <b>169</b> 个，与下文数字相符；<c>sub_772DA8</c> 实测就是 <c>8A 40 74 / C3</c>。）
    ///
    /// 身份已由两份先前产物独立锚定（<c>NativeMonsterAiSearchAttack.cs:31</c> 与
    /// <c>TBaseObject.NativeSkill265.cs:130</c> 都把 <c>sub_767498</c> 写成 IsProperTarget），
    /// 并由调用面复核：全镜像 <c>E8</c> 直调 <c>sub_767498</c> 共 <b>169</b> 处，覆盖怪物 AI、
    /// 卫士、魔法投射三大族，是整个引擎唯一的通用可攻击性入口。它自己不做任何关系判定，
    /// 只做九道硬门，然后把结果交给虚槽 <c>[vmt+0x20]</c>（= C# 的 <c>IsAttackTarget</c>，
    /// TCreature 为 <c>sub_7671F0</c>、TPlayer 为 <c>sub_6C13C4</c>）。
    ///
    /// 逐字节（eax=self=edi，edx=target=esi）：
    /// <code>
    /// 7674A1  85 F6                    test esi,esi
    /// 7674A3  74 4E                    je   0x7674F3        ; target = nil        -> FALSE
    /// 7674A5  8B C6 / E8 FC B8 00 00   mov eax,esi / call sub_772DA8
    ///                                                       ; sub_772DA8 = `8A 40 74 C3`
    ///                                                       ;            = mov al,[eax+0x74]; ret
    ///                                                       ;            = m_boDeath 取值器
    /// 7674AC  84 C0 / 75 43            test al,al / jne     ; target 已死          -> FALSE
    /// 7674B0  80 7E 73 00 / 75 3D      cmp byte [esi+0x73],0 / jne
    ///                                                       ; +0x73 = m_boGhost
    ///                                                       ;   （全镜像唯一写入点 0x7680EF，
    ///                                                       ;     在 MakeGhost sub_768060 里；
    ///                                                       ;     +0x74 才是 m_boDeath，两份旧
    ///                                                       ;     discovery 文档把这两个写反了）
    /// 7674B6  3B FE / 74 39            cmp edi,esi / je     ; self = target        -> FALSE
    /// 7674BA  80 BE E0 02 00 00 00     cmp byte [esi+0x2E0],0
    /// 7674C1  75 30                    jne                  ; target.m_boAdminMode -> FALSE
    /// 7674C3  80 BE E5 02 00 00 00     cmp byte [esi+0x2E5],0
    /// 7674CA  75 27                    jne                  ; target.m_boStoneMode -> FALSE
    /// 7674CC  8B 86 28 01 00 00        mov eax,[esi+0x128]  ; target.m_PEnvir
    /// 7674D2  3B 87 28 01 00 00        cmp eax,[edi+0x128]
    /// 7674D8  75 19                    jne                  ; 不同地图            -> FALSE
    /// 7674DA  B2 34 / 8B C6 / E8 …     mov dl,0x34 / mov eax,esi / call sub_772960
    /// 7674E3  84 C0 / 75 0C            test al,al / jne     ; target 有状态 52     -> FALSE
    /// 7674E7  8A 86 78 01 00 00        mov al,[esi+0x178]   ; target.m_btRaceServer
    /// 7674ED  04 10                    add al,0x10          ; == sub al,0xF0
    /// 7674EF  2C 02                    sub al,2             ; CF = ((race-0xF0) &lt;u 2)
    /// 7674F1  73 04                    jae  0x7674F7        ; CF=0 -> 走虚调
    /// 7674F3  33 C0                    xor eax,eax          ; race ∈ {240,241}     -> FALSE
    /// 7674F7  8B D6 / 8B C7 / 8B 08 / FF 51 20
    ///                                  call [self.vmt+0x20] ; = IsAttackTarget(target)
    /// </code>
    ///
    /// <c>add al,0x10 / sub al,2 / jae</c> 是 Delphi 的 <c>x in [240,241]</c> 惯用式：
    /// <c>dec/sub</c> 之后看借位。<c>jae</c>（CF=0）才继续，所以 <b>只有</b> 种族 240 与 241
    /// 被拒。0xF0/0xF1 在全镜像没有任何立即数写入点（扫 <c>78 01 00 00 F0/F1</c> 两式 0 命中），
    /// 说明它们只能来自 Monster.DB 的 Race 列，是脚本用的「不可攻击」占位种族。
    ///
    /// C# 此前把这九道门散落在约 50 个调用点上各写一部分（多数只写了
    /// <c>!m_boDeath &amp;&amp; !m_boGhost</c>），少写的那几道就是净漏洞：跨地图、石化、管理员模式、
    /// 状态 52 的目标在部分路径上可被攻击。这里按原生收敛到唯一入口。
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>战神 sub_772960 的状态位 52（0x34）。</summary>
        private const int NativeProperTargetBlockedState = 52;

        /// <summary>战神 0x7674E7-0x7674F1 拒绝的两个种族。</summary>
        private const int NativeProperTargetRejectRaceLo = 240;
        private const int NativeProperTargetRejectRaceHi = 241;

        /// <summary>
        /// <c>sub_767498</c> 的前九道门。返回 false 表示原生在调用虚槽 +0x20 之前就已经
        /// 判定不可攻击。顺序与原生一致（全部无副作用，所以顺序只影响可读性）。
        /// </summary>
        protected bool NativeProperTargetPreGate(TBaseObject BaseObject)
        {
            if (BaseObject == null)                                   // 0x7674A3
            {
                return false;
            }
            if (BaseObject.m_boDeath)                                 // 0x7674AE sub_772DA8
            {
                return false;
            }
            if (BaseObject.m_boGhost)                                 // 0x7674B4 [+0x73]
            {
                return false;
            }
            if (ReferenceEquals(BaseObject, this))                    // 0x7674B8
            {
                return false;
            }
            if (BaseObject.m_boAdminMode)                             // 0x7674C1 [+0x2E0]
            {
                return false;
            }
            if (BaseObject.m_boStoneMode)                             // 0x7674CA [+0x2E5]
            {
                return false;
            }
            if (BaseObject.m_PEnvir != m_PEnvir)                      // 0x7674D8 [+0x128]
            {
                return false;
            }
            if (BaseObject.HasNativeActiveState(                      // 0x7674E5 dl=0x34
                    NativeProperTargetBlockedState))
            {
                return false;
            }
            if (BaseObject.m_btRaceServer == NativeProperTargetRejectRaceLo
                || BaseObject.m_btRaceServer == NativeProperTargetRejectRaceHi)
            {
                return false;                                         // 0x7674F1 jae
            }
            return true;
        }
    }
}
