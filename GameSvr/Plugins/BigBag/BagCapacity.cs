using System.Globalization;
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
        private const string VariableModeValue = "V变量控制格子";

        private static volatile int _persistableExtraSlots;
        private static volatile string _warnedMode;

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
            if (persistable <= 0 || actor is not TPlayObject player)
                return NativeSlots;
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
        /// </summary>
        public static int ConfiguredExtraSlots(TPlayObject player)
        {
            if (player == null || ReadInt(EnabledKey, 0) == 0)
                return 0;

            var mode = ReadString(FixedModeKey);
            if (!string.Equals(mode, FixedModeValue, StringComparison.Ordinal))
            {
                // "V变量控制格子" 是「额外格子数取自玩家变量 GetV(变量v1,变量v2)」，
                // 但 v1/v2 到格子数的换算无字节证据（壳是 Themida，静态反汇编不可行），
                // 且生产走的是"固定格子"。猜错方向会直接删玩家物品，故 fail-closed。
                WarnUnsupportedModeOnce(mode);
                return 0;
            }

            return Math.Max(0, ReadInt(ExtraSlotsKey, 0));
        }

        private static void WarnUnsupportedModeOnce(string mode)
        {
            var tag = mode ?? string.Empty;
            if (string.Equals(_warnedMode, tag, StringComparison.Ordinal))
                return;
            _warnedMode = tag;
            M2Share.ErrorMessage(
                $"[BagCapacity] {FixedModeKey}=\"{tag}\" 未实现，额外格子按 0 处理，背包维持 {NativeSlots} 格。" +
                (string.Equals(tag, VariableModeValue, StringComparison.Ordinal)
                    ? " 变量控制分支缺少 v1/v2→格子数的换算证据。"
                    : string.Empty));
        }

        private static int ReadInt(string key, int fallback)
        {
            var raw = M2Share.PluginManager?.GetItemConfigValue(key);
            if (raw is long wide)
                return wide >= int.MinValue && wide <= int.MaxValue ? (int)wide : fallback;
            if (raw is int narrow)
                return narrow;
            if (raw is string text && int.TryParse(text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return fallback;
        }

        private static string ReadString(string key)
        {
            return M2Share.PluginManager?.GetItemConfigValue(key) as string;
        }
    }
}
