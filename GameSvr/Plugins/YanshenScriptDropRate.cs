namespace GameSvr.Plugins
{
    /// <summary>
    /// 盘古3 页的 <c>脚本控制人物爆率</c>。
    ///
    /// <para>面板自述：「人物爆率分为【杀人爆率】和【防爆属性】，退出消失，需要登录脚本重设；
    /// 设置杀人爆率:SetV(1,2,N);设置防爆属性:SetV(1,3,M)」
    /// （<c>YanshenLegacy23ReplicaPanels.cs</c> 的绿字注）。原生实现是三处补丁，
    /// 全部在开关 ON 时装、OFF 时按原字节回写。</para>
    ///
    /// <para><b>① SetV 拦截</b>（插件 <c>0x100B99D9 call 0x10032FD0</c>，
    /// 起止 <c>0x6DF2CC</c>、resume <c>0x6DF2D5</c>，覆盖 9 字节）。桩体逐字节回放：</para>
    /// <code>
    ///   83 F9 02                cmp ecx, 2               ; ecx = SetV 的 index
    ///   75 07                   jne +7
    ///   36 88 83 79 05 00 00    mov byte ss:[ebx+0x579], al
    ///   83 F9 03                cmp ecx, 3
    ///   75 07                   jne +7
    ///   36 89 83 8C 01 00 00    mov dword ss:[ebx+0x18C], eax
    ///   89 45 FC                mov [ebp-4], eax         ; 回放
    ///   8D 93 08 08 00 00       lea edx,[ebx+0x808]      ; 回放
    ///   E9 -> 0x6DF2D5
    /// </code>
    /// <para>宿主 <c>sub_6DF288 = SetV(eax=self, edx=group, ecx=index, [ebp+8]=value)</c>。
    /// <c>0x6DF2CC</c> 位于 <b>keyed 支</b>（<c>group != 0 &amp;&amp; index &gt; 0</c>）——
    /// group 0 的快支在 <c>0x6DF2A8 mov [ebx+esi*4+0x808],eax</c> 之后
    /// <c>0x6DF2B1 jmp</c> 直接走了，够不到桩体。桩体只比较 <b>index</b>，不看 group，
    /// 所以任何 <c>SetV(g&gt;0, 2|3, v)</c> 都会命中，不止 <c>g==1</c>。
    /// <c>ecx</c> 在 <c>0x6DF2C1 call 0x6E42CC</c> 之后仍然是 index：那个函数只有
    /// <c>imul eax,edx,0x3E8 / add eax,ecx / ret</c> 三条，不动 ecx。
    /// <c>+0x579</c> 写的是 <c>al</c>（截成字节），<c>+0x18C</c> 写整个 <c>eax</c>。</para>
    ///
    /// <para><b>②③ 两处 RecalcAbilitys 复位被 NOP 掉</b>（裸字节写，插件
    /// <c>0x100B9A35</c> / <c>0x100B9A68</c> 经 <c>0x10033340</c>）：</para>
    /// <code>
    ///   0x73DAC5  89 86 8C 01 00 00      mov [esi+0x18C],eax        -> 90 x6
    ///   0x73D578  C6 86 79 05 00 00 00   mov byte [esi+0x579],0     -> 90 x7
    /// </code>
    /// <para>也就是重算不再覆盖脚本设的值——这正是面板所谓「退出消失、需要登录脚本重设」：
    /// 值只活在内存里，重算不动它，重新登录才归零。
    /// 注意 <b>第三处写 <c>0x73DECF mov byte [esi+0x579],0xA</c> 没有被 NOP</b>，
    /// 所以 <c>[+0x1D5]</c> 门一旦成立仍会把 <c>+0x579</c> 覆盖成 10。</para>
    ///
    /// <para>两个字段的下游消费者在 C# 里早已建模（见
    /// <c>TBaseObject.NativeDeathDropDenominator.cs</c>）：
    /// <c>m_nNativeDropRareBase</c> = <c>[+0x18C]</c>（非红名分母 <c>+90</c>）、
    /// <c>m_btNativeDropRareKillerBonus</c> = <c>[+0x579]</c>（从凶手身上减）。</para>
    /// </summary>
    internal static class YanshenScriptDropRate
    {
        /// <summary>0x100B9A03 起那一段的门：<c>cmp dword [edi+0x2C8],0</c> 族开关本体。</summary>
        private static bool Enabled()
        {
            var pm = M2Share.PluginManager;
            return pm != null && new YanshenApi(null, null, pm).IsScriptDropRate();
        }

        /// <summary>
        /// keyed <c>SetV</c> 的旁路写。只在 <c>group != 0</c> 的支上调用，与
        /// <c>0x6DF2CC</c> 桩体的位置一致。
        /// </summary>
        internal static void OnKeyedSetV(TBaseObject self, int index, int value)
        {
            if (self == null || !Enabled())
                return;
            if (index == 2)
            {
                // 36 88 83 79 05 00 00  mov byte [self+0x579], al —— 只取低 8 位
                self.m_btNativeDropRareKillerBonus = unchecked((byte)value);
            }
            else if (index == 3)
            {
                // 36 89 83 8C 01 00 00  mov dword [self+0x18C], eax
                self.m_nNativeDropRareBase = value;
            }
        }

        /// <summary>
        /// 开关 ON 时 <c>0x73DAC5</c> 与 <c>0x73D578</c> 被 NOP：重算不再复位这两个字段。
        /// <c>0x73DECF</c>（<c>[+0x1D5]</c> 门下置 10）不在补丁范围内，照常执行。
        /// </summary>
        internal static bool RecalcResetSuppressed() => Enabled();
    }
}
