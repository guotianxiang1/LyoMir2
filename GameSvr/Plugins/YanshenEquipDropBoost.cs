namespace GameSvr.Plugins
{
    /// <summary>
    /// 盘古3 页的 <c>装备提升人物爆率</c> / <c>_A值</c> / <c>_B值</c>。
    ///
    /// <para><b>原生形态</b>：一条 trampoline，装在怪物死亡散落主函数
    /// <c>sub_71FA20</c>（<c>@AfterScatterItems</c>）的**段2**（怪物自有掉落表循环）
    /// 里，把每件掉落物的掷点分母换掉。安装器 <c>0x10032FD0</c>，站点
    /// <c>0x100B9F9E</c>，覆盖宿主 <c>0x71FD37..0x71FD3D</c>（6 字节，
    /// <c>E9 rel32</c> + 一个 <c>0x90</c>），resume <c>0x71FD3D</c>。</para>
    ///
    /// <para>宿主被覆盖处与它的上下文（平坦镜像 base 0x400000）：</para>
    /// <code>
    /// 0071FD34  8B 45 E4          mov eax,[ebp-0x1C]   ; 当前 TMonItem 记录
    /// 0071FD37  8B 40 14          mov eax,[eax+0x14]   ; MaxPoint      ← 补丁起点
    /// 0071FD3A  F7 6D D4          imul dword [ebp-0x2C]; × 防沉迷倍率
    /// 0071FD3D  E8 0A 3E CE FF    call 0x403B4C        ; Random(eax)   ← resume
    /// 0071FD42  8B 55 E4          mov edx,[ebp-0x1C]
    /// 0071FD45  3B 42 10          cmp eax,[edx+0x10]   ; SelPoint
    /// 0071FD48  0F 8F 51 01 00 00 jg  0x71FE9F         ; 不掉
    /// </code>
    ///
    /// <para><b>桩体</b>：安装器的入参是「一 dword 一字节」的数组
    /// （<c>arr=[ebp-0x1F8]</c>、<c>n=0x2E=46</c>），其中 <c>A</c>/<c>B</c> 的四个字节
    /// 是运行期 <c>atoi</c> 出来后逐字节填进去的（<c>0x100B9E6A</c> /
    /// <c>0x100B9E7B</c> <c>call 0x1022DC49</c>=atoi，
    /// <c>[ebp-0x1A8..-0x19C]</c>=A 的 4 字节、<c>[ebp-0x168..-0x15C]</c>=B 的 4 字节，
    /// 其余 38 个 dword 由 9 条 <c>movaps</c> 从 <c>.rdata</c> 常量取）。
    /// 拼出来的 50 字节逐字节回放：</para>
    /// <code>
    /// +0x000  8B 40 14               mov eax,[eax+0x14]        ; 回放 MaxPoint
    /// +0x003  F7 6D D4               imul dword [ebp-0x2C]     ; 回放 × 倍率
    /// +0x006  81 7D F8 00 00 41 00   cmp dword [ebp-8],0x410000; 凶手是不是真对象
    /// +0x00D  0F 82 1A 00 00 00      jb  +0x2D                 ; 不是 → 原样返回
    /// +0x013  B9 &lt;A&gt;                 mov ecx, A
    /// +0x018  F7 E9                  imul ecx                  ; edx:eax = eax × A
    /// +0x01A  8B 55 F8               mov edx,[ebp-8]           ; 凶手
    /// +0x01D  8B 92 A4 02 00 00      mov edx,[edx+0x2A4]       ; CC 下限（刺术下限）
    /// +0x023  B9 &lt;B&gt;                 mov ecx, B
    /// +0x028  01 D1                  add ecx,edx               ; B + CC
    /// +0x02A  99                     cdq
    /// +0x02B  F7 F9                  idiv ecx                  ; eax = eax / (B+CC)
    /// +0x02D  E9 → 0x71FD3D                                    ; 回到 call Random
    /// </code>
    ///
    /// <para><b>算术是 32 位的，不是 64 位。</b><c>F7 E9 imul ecx</c> 只吃 <c>eax</c>
    /// （前一条 <c>imul</c> 的高半 <c>edx</c> 被丢），随后 <c>8B 55 F8</c> 又把 <c>edx</c>
    /// 覆盖成凶手指针，最后 <c>99 cdq</c> 从 <c>eax</c> 重新符号扩展。所以两次乘法都只保留
    /// 低 32 位（回绕），除法是 32 位有符号 <c>idiv</c>（向零截断、余数丢弃）。
    /// <c>B+CC == 0</c> 时原生 <c>#DE</c>、C# 抛 <c>DivideByZeroException</c> —— 同类行为，
    /// 不额外加闸门（加了就是臆造）。</para>
    ///
    /// <para><b><c>+0x2A4</c> 是 CC 下限，不是什么"累计爆率加成"新字段。</b>
    /// 职业端点选择器 <c>sub_76CD8C</c> 按 <c>byte[self+0x72]</c> 分四支，
    /// <c>0x76CDEA mov edx,[eax+0x2A4]</c> / <c>0x76CDF0 mov eax,[eax+0x2A8]</c>
    /// 就是 job 3 的那一支（job 0 → <c>+0x28C/+0x290</c> DC、1 → <c>+0x294/+0x298</c> MC、
    /// 2 → <c>+0x29C/+0x2A0</c> SC），读的同样是 <b>dword</b>，与桩体一致。
    /// 面板绿字自证：「数据库配置任意装备CC字段属性，即[刺术下限]，每加1点，
    /// 提升爆率约10%……实际爆率：(B+CC)/N*A」——B=10 时 CC=1 恰好 (10+1)/10 = +10%。</para>
    ///
    /// <para>全镜像扫 <c>disp==0x2A4</c> 的 35 条指令里，落在角色对象上的写点只有两处，
    /// 都在 <c>RecalcAbilitys sub_73D500</c> 内：
    /// <c>0x73DA10 add dword [esi+0x2A4],eax</c>（<c>eax=[agg+0x34]</c>，装备聚合块的
    /// CC 低端点，与 <c>+0x27C/+0x284/+0x28C/+0x294/+0x29C</c> 同一张步长 8 的 lo/hi 表）
    /// 与 <c>0x73DE71</c>（在 <c>0x73DE23 call 0x772960(dl=6)</c> 状态 6 门下的
    /// <c>_MIN(cc,300)*50</c> 缩放，非常规路径）。
    /// 因此 <c>[killer+0x2A4]</c> 在 C# 侧的权威就是
    /// <c>m_NativeCoreWorkingAbility.CCLow</c> —— 这条对应关系不是本轮新造的，
    /// <c>TBaseObject.NativeSkill66Or67.cs</c> 的 id 68 分支
    /// （<c>0x74449F [ebx+0x2A8] / [ebx+0x2A4]</c>）已经这么用了。
    /// 该字段是活的：<c>MergeNativeCoreItem</c> 累加 <c>StdItem.Cc</c>，
    /// <c>MergeNativeCoreEffectProperty</c> 的 <c>case 111</c> 累加扩展属性「刺术下限」。</para>
    ///
    /// <para><b>开关与取值</b>：安装门 <c>0x100B9E4A cmp dword [edi+0x660],0 / je</c>
    /// = <c>装备提升人物爆率</c>；关闭时 <c>0x100BA0AE</c> 经 <c>0x10033340</c> 把原 6 字节
    /// <c>8B 40 14 F7 6D D4</c> 写回（<c>0x100BA07A/0x100BA084</c> 备好的缓冲），
    /// 即「关 = 宿主原样」。A/B 走 CRT <c>atoi</c>（不是 atof），
    /// 缺省取页面对象构造函数的出厂串 <c>[edi+0x664]='10'</c> / <c>[edi+0x668]='10'</c>。</para>
    /// </summary>
    internal static class YanshenEquipDropBoost
    {
        /// <summary>
        /// 桩体 <c>+0x013..+0x02C</c> 的纯算术，逐指令对应。
        /// <paramref name="basePoints"/> 是回放段算出的 <c>MaxPoint × 倍率</c>。
        /// </summary>
        internal static int NativeDenominator(int basePoints, int a, int b,
            int ccLow)
        {
            unchecked
            {
                // B9 <A> / F7 E9 : edx:eax = eax * A，只有 eax（低 32 位）活下来
                var scaled = basePoints * a;
                // 8B 55 F8 / 8B 92 A4 02 00 00 / B9 <B> / 01 D1
                var divisor = b + ccLow;
                // 99 / F7 F9 : 32 位有符号除，向零截断，余数丢弃
                return scaled / divisor;
            }
        }

        /// <summary>
        /// <c>0x71FD37</c> 桩体的完整语义：把 <c>Random()</c> 的分母
        /// <c>MaxPoint × 倍率</c> 换成 <c>(MaxPoint × 倍率 × A) / (B + 凶手CC下限)</c>。
        /// 补丁未装（开关关 / 插件未运行）或凶手不是真对象时原样返回。
        /// </summary>
        internal static int Denominator(int basePoints, TBaseObject killer)
        {
            var pm = M2Share.PluginManager;
            if (pm == null)
                return basePoints;
            var api = new YanshenApi(null, null, pm);
            // 0x100B9E4A cmp dword [edi+0x660],0 / je —— 开关关就不装桩
            if (!api.IsBoostDropRate())
                return basePoints;
            // +0x006 cmp dword [ebp-8],0x410000 / jb +0x2D
            if (killer == null)
                return basePoints;
            return NativeDenominator(basePoints, api.BoostDropRateA(),
                api.BoostDropRateB(), killer.m_NativeCoreWorkingAbility.CCLow);
        }
    }
}
