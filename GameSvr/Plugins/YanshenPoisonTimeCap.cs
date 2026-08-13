namespace GameSvr.Plugins
{
    /// <summary>
    /// 盘古3 页的 <c>中毒时间上限</c> / <c>中毒时间上限_秒</c>。
    ///
    /// <para><b>原生形态</b>：两条 trampoline，装在两个中毒施加器里，各自把
    /// <c>SendDelayMsg</c> 的 <c>nParam1</c>（中毒时长）上钳到同一个配置值。
    /// 安装器 <c>0x10032FD0</c>（写 <c>E9 rel32</c> + <c>0x90</c> 补齐），
    /// 桩体以「一 dword 一字节」的形式在栈上拼出来，逐字节回放如下
    /// （插件转储 base 0x10000000，宿主 base 0x400000）：</para>
    ///
    /// <code>
    /// 绿毒施加器 sub_76E540（TPoisons Shape 1，VMT+0x110）
    ///   安装 0x100B87BF call 0x10032FD0(arr=[ebp-0xF8], n=0x13,
    ///                                   start=0x76E5CE, end=0x76E5CE, resume=0x76E5D3)
    ///   桩体  81 F9 &lt;V&gt;   cmp ecx, V          ; ecx = nParam1（时长）
    ///         7E 05        jle +5
    ///         B9 &lt;V&gt;       mov ecx, V
    ///         51 8B D3 52 50                    ; 回放被覆盖的 5 字节
    ///         E9 -&gt; 0x76E5D3
    ///   宿主原字节 0x76E5CE `51 8B D3 52 50`（push ecx / mov edx,ebx / push edx / push eax）
    ///
    /// 红毒施加器 sub_76E620（TPoisons Shape 2，VMT+0x114）
    ///   安装 0x100B8776 call 0x10032FD0(arr=[ebp-0x140], n=0x12,
    ///                                   start=0x76E675, end=0x76E675, resume=0x76E67A)
    ///   桩体  8B 45 F8     mov eax,[ebp-8]      ; 回放：取时长
    ///         3D &lt;V&gt;       cmp eax, V
    ///         7E 05        jle +5
    ///         B8 &lt;V&gt;       mov eax, V
    ///         50 53                             ; 回放 push eax / push ebx
    ///         E9 -&gt; 0x76E67A
    ///   宿主原字节 0x76E675 `8B 45 F8 50 53`
    /// </code>
    ///
    /// <para>被钳的量在宿主两处都是同一件东西——<c>sub_766060</c>(SendDelayMsg,
    /// <c>cx=0x283C</c>)的 <c>nParam1</c>，由
    /// <c>sub_4C8764(magic, 0x1E|0x28) + 2*sub_764D14(caster SC)</c> 算出
    /// （红毒 0x76E64A <c>mov edx,0x28</c> / 0x76E663 <c>call 0x764D14</c> /
    /// 0x76E66B <c>add eax,eax</c> / 0x76E670 <c>mov [ebp-8],edx</c>）。
    /// C# 两个施毒分支里的 <c>nPower</c> 就是它。</para>
    ///
    /// <para><b>取值</b>：<c>V = atoi(中毒时间上限_秒)</c>，单次求值、原样使用，
    /// 既不乘 1000 也不设下限（插件 0x100B8626 <c>push [edi+0x284]</c> /
    /// 0x100B862C <c>call 0x1022DC49</c>(atoi)，随后 0x100B8638..0x100B865D 把
    /// 返回的 dword 拆成四个字节填进两份桩体的两处立即数）。
    /// 门 <c>0x100B860C cmp dword [edi+0x280],0 / je</c> = <c>中毒时间上限</c> 开关。
    /// 缺省值取自页面对象构造函数 <c>[edi+0x284] = "120"</c>，不是 60。
    /// 比较是有符号 <c>jle</c>，所以钳制形如 <c>if (v &gt; V) v = V</c>。</para>
    /// </summary>
    internal static class YanshenPoisonTimeCap
    {
        /// <summary>
        /// 中毒时长上钳。开关关 / 插件未运行时原样返回。
        /// </summary>
        internal static int Cap(int duration)
        {
            var pm = M2Share.PluginManager;
            if (pm == null)
                return duration;
            var api = new YanshenApi(null, null, pm);
            if (!api.IsPoisonTimeLimit())
                return duration;
            var limit = api.PoisonTimeLimitSec();
            return duration > limit ? limit : duration;
        }
    }
}
