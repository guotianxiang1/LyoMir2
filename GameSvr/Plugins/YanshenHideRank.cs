namespace GameSvr.Plugins
{
    /// <summary>
    /// 盘古3 页的 <c>屏蔽排行榜</c>。
    ///
    /// <para><b>原生形态</b>：一字节裸写，把宿主 <c>sub_6CBA88</c> 的序言首字节
    /// <c>55 (push ebp)</c> 改成 <c>C3 (ret)</c>，整个处理函数变成空操作。
    /// 插件两支（转储 base 0x10000000）：</para>
    /// <code>
    ///   0x100B9197/0x100B919C push 0x6CBA88  -> 0x100B91A8 写 "C3"  屏蔽排行榜(已启动)
    ///   0x100B9215/0x100B921A push 0x6CBA88  -> 0x100B9226 写 "55"  屏蔽排行榜(未启动)
    ///   （g09.json: len 1, va 0x6CBA88, orig "55"）
    /// </code>
    ///
    /// <para><b>这个函数是谁</b>——审计把它记作「跳转表分发器、C# 落点未定名」，
    /// 本轮定死了：</para>
    /// <list type="number">
    /// <item>全镜像 <c>rel32</c> 扫描，<c>sub_6CBA88</c> <b>只有一个调用者</b>
    /// <c>0x6D956F</c>，且无任何 dword 引用（不在任何 VMT 里）。</item>
    /// <item><c>0x6D9561</c> 这条臂由 CM 分发器的跳转表命中：
    /// <c>0x6D81A4 add eax,0xFFFFFBED</c>（<c>-1043</c>）/
    /// <c>0x6D81AA cmp eax,0x48 / ja</c> /
    /// <c>0x6D81B3 jmp dword [eax*4 + 0x6D81BA]</c>，臂地址落在表项
    /// <c>0x6D81FE</c>，序号 <c>(0x6D81FE-0x6D81BA)/4 = 17</c>
    /// ⇒ <b>opcode = 1043 + 17 = 1060</b>。</item>
    /// <item>调用约定对得上：<c>0x6D9561 mov eax,[ebp-0x34] / mov cl,[eax+6]</c>（category）、
    /// <c>0x6D956A mov edx,[eax]</c>（page）、<c>0x6D956C mov eax,[ebp-4]</c>（self），
    /// 与 C# <c>HandleNativeQuestOrder(nParam1, (byte)nParam2)</c> 一一对应
    /// （<c>CM_QUEST_ORDER</c> = <see cref="TPlayObject.NativeQuestOrderRequestIdent"/> = 1060）。</item>
    /// </list>
    ///
    /// <para><b>净行为</b>：<c>C3</c> 让函数在建立栈帧之前返回，所以既不回包也不产生
    /// 任何副作用。返回值无意义——原生调用点 <c>0x6D9574</c> 紧跟 <c>jmp 0x6DBC2C</c>
    /// （分发器尾），从不读 <c>al</c>；C# 侧 <c>TPlayObject.Message.cs</c> 的
    /// <c>CM_QUEST_ORDER</c> 臂同样把返回值丢弃。三个寄存器参数、无栈参数，
    /// 故 <c>C3</c> 不破坏栈平衡。</para>
    /// </summary>
    internal static class YanshenHideRank
    {
        /// <summary>
        /// true = <c>sub_6CBA88</c> 首字节已被改成 <c>C3</c>，处理函数整体不执行。
        /// </summary>
        internal static bool HandlerStubbed()
        {
            var pm = M2Share.PluginManager;
            return pm != null && new YanshenApi(null, null, pm).IsHideRank();
        }
    }
}
