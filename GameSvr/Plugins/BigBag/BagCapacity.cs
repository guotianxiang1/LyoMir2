using GameSvr.PasEngine;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 背包容量的唯一权威。任何「还能不能再装一件」「还剩几格」的判断都要走这里，
    /// 不要再直接读 <see cref="Grobal2.MAXBAGITEM"/>（REPLICATION_RULES §4.18）。
    ///
    /// 原生 M2 里 48 是一个数：入包门 <c>sub_6D0AE8</c> 的 <c>Count+1 &lt;= 48</c>，
    /// 和存档循环 <c>0x6B171B cmp edi,0x30</c> 的槽位数。装上眼神「无限背包」以后
    /// 这两个数分家了 —— 容量变成 48+额外格子，而存档记录仍然只有 48 槽，
    /// 多出来的部分由 <c>Gs1\MyJson\bags\&lt;角色名&gt;.bin</c> 承载。所以这里分三个量：
    ///
    /// <list type="bullet">
    /// <item><see cref="NativeSlots"/> —— 48，存档记录槽位数，也是没有插件时的容量</item>
    /// <item><see cref="Of"/> —— 现在允许装多少件</item>
    /// <item><see cref="PersistableOf"/> —— 现在有多少件能真正落盘</item>
    /// </list>
    ///
    /// <c>Of(a) &lt;= PersistableOf(a)</c> 恒成立，由 <see cref="Of"/> 里的取小强制。
    /// 这条不变式是「不丢物品」的全部依据：装得进去的一定存得下来。
    /// </summary>
    public static class BagCapacity
    {
        /// <summary>
        /// 存档记录 <c>THumInfoData.BagItems</c> 的槽位数，等于原生无插件时的容量。
        /// 这是记录布局的一部分（REPLICATION_RULES §1.4），永远是 48。
        /// </summary>
        public const int NativeSlots = Grobal2.MAXBAGITEM;

        // Gs1\MyJson\items\config.json，生产实测见 staging/m_bagcap_impl_20260813.md §1。
        private const string EnabledKey = "无限背包_是否勾选";
        private const string ExtraSlotsKey = "无限背包_额外格子";
        private const string FixedModeKey = "无限背包_是否固定";
        private const string FixedModeValue = "固定格子";
        private const string VariableGroupKey = "无限背包_变量v1";
        private const string VariableIndexKey = "无限背包_变量v2";

        // 变量号缺失时的原生默认值，取自各站点的 else 臂立即数：
        //   1007E45F  BF 0A 00 00 00  mov edi, 0xA   -> 变量v1 = 10
        //   1007E4A4  B8 01 00 00 00  mov eax, 1     -> 变量v2 = 1
        private const int DefaultVariableGroup = 10;
        private const int DefaultVariableIndex = 1;

        private static volatile int _persistableExtraSlots;

        /// <summary>
        /// 大背包持久层能真正写回的「48 格以后」条数，由 BAG-02/03 的接线方在把
        /// <c>bags\&lt;角色名&gt;.bin</c> 接进上线/下线/定时存盘之后发布。
        ///
        /// 它是 0 的时候配置里的额外格子一律不发放：存不下的第 49 件会在下一次
        /// 存盘时被静默删掉，宁可装不进，也不能装进去再丢。
        /// </summary>
        public static int PersistableExtraSlots
        {
            get => _persistableExtraSlots;
            set => _persistableExtraSlots = Math.Max(0, value);
        }

        /// <summary>这个角色现在允许装多少件物品。</summary>
        public static int Of(TBaseObject actor)
        {
            var persistable = _persistableExtraSlots;
            if (persistable <= 0) return NativeSlots;
            if (actor is not TPlayObject player) return NativeSlots;
            return NativeSlots + Math.Min(ConfiguredExtraSlots(player), persistable);
        }

        /// <summary>
        /// 这个角色现在有多少件物品能真正落盘。存盘闸门用它判断是否会发生截断，
        /// 用 <see cref="Of"/> 会在管理员调小额外格子后把老玩家的存盘永久卡死。
        /// </summary>
        public static int PersistableOf(TBaseObject actor)
        {
            return actor is TPlayObject
                ? NativeSlots + _persistableExtraSlots
                : NativeSlots;
        }

        /// <summary>
        /// 配置声明的额外格子数（不考虑能不能存下来）。无法确定时返回 0 —— 这里的
        /// 每一条 return 0 都意味着「按没装插件处理」，不会比原生更宽。
        ///
        /// 原生把这段算式复制了四份，逐条对齐的站点见
        /// <c>staging/ys_gui_impl_20260813.md</c>：<c>0x1007E370</c> / <c>0x1007E5F0</c> /
        /// <c>0x1007E880</c> 返回 <c>48+extra</c>，<c>0x1007EF00</c> 返回「下标放不放得下」
        /// 的判定。四份的取值分支完全一致，互为独立佐证。
        /// </summary>
        public static int ConfiguredExtraSlots(TPlayObject player)
        {
            if (player == null || !FeatureEnabled())
                return 0;

            int extra;
            // 1007E3B8 push 0x102BFAF0(无限背包_是否固定) / 1007E3C8 call 0x100DFCF0(asString)
            // 1007E3D1 mov edx,0x102BFB04("固定格子") / 1007E3D9 call 0x10041610(比较)
            // 1007E3EC je 1007E429 —— 不等于才走变量分支。
            if (string.Equals(ReadString(FixedModeKey), FixedModeValue, StringComparison.Ordinal))
            {
                // 1007E3FA call 0x100E1410(isInt) / 1007E401 je 1007E4C0 —— 非整数直接
                // 落到收尾，esi 保持 0x30，等价于额外格子 0。
                extra = TryReadNativeInt(ExtraSlotsKey, out var fixedSlots) ? fixedSlots : 0;
            }
            else
            {
                // 1007E4A9 push eax(变量v2) / 1007E4AA mov edx,edi(变量v1)
                // 1007E4AC mov ecx,ebx(玩家) / 1007E4AE call 0x10065F00 -> M2 GetV 0x6DF1E4。
                // 返回值就是额外格子数本身，原生没有任何换算。
                var group = TryReadNativeInt(VariableGroupKey, out var v1) ? v1 : DefaultVariableGroup;
                var index = TryReadNativeInt(VariableIndexKey, out var v2) ? v2 : DefaultVariableIndex;
                extra = PasApiBridge.GetPlayerVar(player, 'V', group, index).AsInt();
            }

            // 1007E4B8 jle 1007E4C0（保持 0x30）与 1007F057 cmovns —— 两处都是取正部。
            // GetV 的缺失哨兵 -1（0x6DF1F1）也在这里被吃掉。
            return extra > 0 ? extra : 0;
        }

        /// <summary>
        /// 原生这四个函数里没有 <c>无限背包_是否勾选</c> 的门：该键
        /// (<c>0x102C2C7C</c>) 与同组 15 个 <c>_是否勾选</c> 一起排在
        /// <c>0x102C2BA8..0x102C2D94</c> 的功能注册表里，表靠指针遍历，
        /// 单条记录没有独立 xref（REPLICATION_RULES §4.1），所以「勾选到底
        /// 拦不拦这条路径」拿不到字节证据。保留这道门是取窄的一侧：
        /// 未勾选时按没装插件处理，不会比原生多发格子。
        /// </summary>
        private static bool FeatureEnabled()
        {
            return TryReadNativeInt(EnabledKey, out var enabled) && enabled != 0;
        }

        /// <summary>
        /// <c>Json::Value::isInt()</c> = <c>0x100E1410</c>，配置读取的唯一门。
        /// 按类型标记 <c>[this+8]</c> 分派（jsoncpp: 0=null 1=int 2=uint 3=real
        /// 4=string 5=bool 6=array 7=object）：
        /// <list type="bullet">
        /// <item>1 → <c>0x100E1490 add edx,0x80000000 / adc eax,0</c> 偏置比较，
        /// 即 <c>INT_MIN &lt;= v &lt;= INT_MAX</c></item>
        /// <item>2 → <c>0x100E1483 cmp [ecx],0x7FFFFFFF</c></item>
        /// <item>3 → <c>0x102C8990</c>(-2147483648.0) ≤ v ≤ <c>0x102C8958</c>(2147483647.0)
        /// 且 <c>modf</c> 小数部分为 0（<c>0x100E1467 ucomisd</c> 对 <c>0x102C8900</c>=0.0）</item>
        /// <item>其余（含字符串、布尔、缺键）→ <c>0x100E1475 xor al,al</c> 为假</item>
        /// </list>
        /// 所以 <c>"144"</c> 这种字符串写法原生是不认的，这里也不能用
        /// <c>int.TryParse</c> 去「宽容」它。
        /// </summary>
        private static bool TryReadNativeInt(string key, out int value)
        {
            value = 0;
            switch (M2Share.PluginManager?.GetItemConfigValue(key))
            {
                case long wide when wide >= int.MinValue && wide <= int.MaxValue:
                    value = (int)wide;
                    return true;
                case int narrow:
                    value = narrow;
                    return true;
                case decimal real when decimal.Truncate(real) == real
                                       && real >= int.MinValue && real <= int.MaxValue:
                    value = (int)real;
                    return true;
                case double real when Math.Truncate(real) == real
                                      && real >= int.MinValue && real <= int.MaxValue:
                    value = (int)real;
                    return true;
                default:
                    return false;
            }
        }

        private static string ReadString(string key)
        {
            return M2Share.PluginManager?.GetItemConfigValue(key) as string;
        }
    }
}
