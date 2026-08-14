using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using GameSvr.PasEngine;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 眼神「全局循环函数」周期驱动 —— 补上 <c>docs/ys_recycle_impl_20260813.md §6 M1</c>
    /// 里唯一还挡着删除闸门的东西：<c>MyTimer</c> 从来没有被周期性调用。
    ///
    /// <para><b>删除闸已就位，缺的只是驱动</b>：<c>Ys_HuiShou</c> 已接到
    /// <c>PasApiBridge.Yanshen.cs</c> 的 <c>api.AutoRecycle()</c>；<c>AutoRecycle</c>
    /// 逐字节复刻了原生判定链与「先删后结算、金币顶上限只删不给」的行为。生产
    /// <c>config.json</c> 里 <c>高级回收=1</c> / <c>全局循环函数=1</c> / <c>循环时间_值=2000</c>，
    /// <c>recycle.json</c> 解析成功（313 条），<c>initys</c> 也已在登录时执行
    /// （<c>TPlayObject.Base.cs</c> 调 <c>PasEngine.TryInitializeYanshen</c>）。唯独没有
    /// 谁去周期性地跑 <c>MyTimer</c>，于是回收永远不触发。这个类就是那台节拍器。</para>
    ///
    /// <para><b>原生机制（三件套，基址插件 0x10000000 / 脱壳转储
    /// yanshen2_0_8_dll.memory.bin；M2Server 0x400000 / flat_image.bin）</b>：</para>
    /// <list type="number">
    /// <item><b>谁调用</b>：per-player 的周期节拍。原生形状与宿主自带的
    /// <c>@OnTimer0..19</c>（C# 落在 <c>TPlayObject.Run</c> 内 <c>AutoTimerStatus[i]&gt;500</c>
    /// 那段）同构 —— 每个玩家的 Run pass 里判 <c>now-last&gt;周期</c> 就派发一个脚本过程。
    /// 眼神这一路派发的是脚本标签 <c>@MyTimer</c>（注册项在插件数据表
    /// <c>0x10310D1C "@MyTimer"</c>，与 <c>@MyHeroMagicAttack</c> 等回调同族），过程体在
    /// <c>RunQuest.pas</c> 的 <c>procedure MyTimer()</c>。</item>
    /// <item><b>周期值</b>：<c>循环时间_值</c>，生产 <c>config.json = 2000</c>（毫秒）。字节证据：
    ///   <list type="bullet">
    ///   <item>解析落点 <c>0x100d7dba 89 82 38 09 00 00 mov [edx+0x938],eax</c>
    ///   ⇒ 配置单例（<c>[0x1031c0e0]</c>）字段 <b>+0x938</b>。</item>
    ///   <item>字段地址被缓存成指针：<c>0x10002085 add eax,0x938 / 0x1000208a mov [0x1031c1fc],eax</c>。</item>
    ///   <item>节拍器读它：<c>0x1008c7c0</c> 起 <c>0x1008c7e7 mov ecx,[0x1031c1fc] /
    ///   0x1008c7ed mov ecx,[ecx]</c>（= 周期），<c>eax=now-last</c>（<c>cdq/xor/sub</c> 取绝对值），
    ///   <c>0x1008c7fd cmp eax,周期 / 0x1008c800 jle</c> ⇒ <c>|now-last| &gt; 周期</c> 才跑，
    ///   语义即 <c>TickElapsed(now,last)=(|now-last|&gt;循环时间_值)?now:0; ret 8</c>。</item>
    ///   </list>
    /// </item>
    /// <item><b>从哪读</b>：<c>循环时间_值</c> ← 配置单例 +0x938（C# 侧
    /// <c>PluginManager.GetPluginSetting&lt;int&gt;("YanshenCompat","循环时间_值")</c>）；
    /// 开关 <c>全局循环函数</c> ← 配置单例 +0x93c（解析 <c>0x100d7817 mov [ecx+0x93c],eax</c>）。</item>
    /// <item><b>调用点 VA</b>：节拍器函数 <c>0x1008c7c0</c>（读周期在 <c>0x1008c7e7</c>）。它在整份
    /// 45MB 转储里 <b>0 个 rel32 调用者、0 个 dword 引用</b>，紧邻它的
    /// <c>0x1008c820</c> 是 Themida VM 桩（<c>0x1008c864 jmp 0x10d2f7eb</c>）—— 派发被虚拟化，
    /// 与 <c>ys_recycle_impl §7 BLOCKED</c> 判断一致，所以不能拿到「谁 call 节拍器」的 rel32，
    /// 但节拍器本体的语义与周期来源已逐字节确证。</item>
    /// </list>
    ///
    /// <para><b>下限 500ms</b>：面板注释（<c>自定义循环函数</c> / <c>全局循环函数</c> 共用同一套定时器）
    /// 明写「最快500毫秒就是0.5秒，不能更快」，状态例程亦以 <c>cmp …,0x1f4(500) / jg</c> 判「已启动」。
    /// 生产 2000 &gt; 500 正常运行；&lt;500 视为未配置好，<b>不驱动</b>（fail-closed，避免每 tick 狂删）。</para>
    ///
    /// <para><b>为什么直接调脚本 <c>MyTimer</c> 而不在 C# 里复刻它的函数体</b>：原生 <c>全局循环函数</c>
    /// 派发的就是脚本过程 <c>MyTimer</c>，<c>MyTimer</c> 里的 <c>V(118,68)==100</c> 自动回收总闸、
    /// 月卡、书页/祝福油归集都是运营脚本，属于 <c>RunQuest.pas</c>。节拍器只负责「按周期敲门」，
    /// 敲进去之后的一切判定仍由脚本承担，和生产完全一致。<c>Ys_HuiShou→AutoRecycle</c> 已就位。</para>
    ///
    /// <para><b>金币上限「只删不给」</b>：本类不碰结算。<c>AutoRecycle</c> 内
    /// <c>RecycleOne</c> 先 <c>DelBagItem</c> 删物品并累加，循环后
    /// <c>SettleRecycleTotals</c> 一次性 <c>IncGold(totals.Gold)</c> 且丢弃返回值；
    /// <c>TPlayObject.IncGold</c> 在 <c>m_nGold+额度 &gt; m_nGoldMax</c> 时返回 false 且一分不加。
    /// 于是顶上限时物品已删、金币整笔不入 —— 这是原生行为，接上驱动后如实呈现，不是缺陷。</para>
    ///
    /// <para><b>惰性</b>：插件未运行/未初始化时每 tick 只读几个字段就返回，不查配置、不分配、
    /// 不碰脚本引擎。节流命中前也不会构造脚本路径。</para>
    /// </summary>
    public static class YanshenRecycleDriver
    {
        private const string PluginName = "YanshenCompat";

        /// <summary>开关键：+0x93c，生产 config.json = 1。</summary>
        private const string ConfigKeyEnable = "全局循环函数";

        /// <summary>周期键（毫秒）：+0x938，生产 config.json = 2000。</summary>
        private const string ConfigKeyPeriod = "循环时间_值";

        /// <summary>
        /// 周期下限 500ms（面板「最快500毫秒…不能更快」+ 状态例程 <c>cmp …,0x1f4 / jg</c>）。
        /// 低于它按「未配置好」处理，不驱动。
        /// </summary>
        private const int MinPeriodMs = 500;

        /// <summary>
        /// 脚本里的循环过程名。原生注册标签是 <c>@MyTimer</c>，面板注释亦要求
        /// 「在 RunQuest.pas 脚本中增加 procedure MyTimer();」。
        /// </summary>
        private const string LoopProcedure = "MyTimer";

        /// <summary>
        /// 每玩家上次派发的 tick 缓存，对应原生 <c>s(1,127)</c> 的毫秒缓存
        /// （RunQuest.pas 注释：被清零就立即执行、否则等满面板设置的毫秒数）。
        /// 用 <see cref="ConditionalWeakTable{TKey,TValue}"/> 承载：玩家下线被 GC 后自动回收，
        /// 不往 <c>TPlayObject</c> 加字段、也不依赖任何未经字节确证的 s 变量下标。
        /// </summary>
        private static readonly ConditionalWeakTable<TPlayObject, TickSlot> _lastRun = new();

        private static long _dispatchCount;

        /// <summary>诊断计数器：真正派发一次 <c>MyTimer</c> 时 +1。产品逻辑不读它，供审计断言用。</summary>
        public static long DispatchCount => Interlocked.Read(ref _dispatchCount);

        /// <summary>
        /// 每个玩家 Run pass 调一次。挂点见 <c>TPlayObject.Run</c> 的 <c>@OnTimer</c> 循环之后，
        /// 只加一行 <c>YanshenRecycleDriver.Tick(this, currentTick)</c>。
        /// </summary>
        /// <param name="player">当前玩家（原生的 This_Player）。</param>
        /// <param name="currentTick">本次 Run 采样的 <c>HUtil32.GetTickCount()</c>。</param>
        public static void Tick(TPlayObject player, int currentTick)
        {
            if (player == null) return;

            var manager = M2Share.PluginManager;
            if (manager == null) return;

            // 便宜前置门：插件未运行 / 未初始化（initys 未跑）——每 tick 只读三个字段就返回。
            var plugin = manager.GetPlugin(PluginName);
            if (plugin == null || plugin.State != PluginState.Running || !plugin.IsInitialized)
                return;

            // 开关：全局循环函数=1 才驱动（配置单例 +0x93c）。便宜读，不分配。
            if (!IsTruthy(manager.GetNativeConfigValue(ConfigKeyEnable)))
                return;

            // 周期：循环时间_值（配置单例 +0x938，生产 2000ms）。<500 视为未配置好，不驱动。
            var periodMs = manager.GetPluginSetting<int>(PluginName, ConfigKeyPeriod, 0);
            if (periodMs < MinPeriodMs)
                return;

            // 每玩家节流：对应原生节拍器 0x1008c7c0 的 |now-last|>周期。首次见到该玩家
            // （缓存未建立）立即执行，等价于原生 s(1,127) 被清零「马上就执行此循环函数」。
            var slot = _lastRun.GetOrCreateValue(player);
            if (slot.Started && unchecked(currentTick - slot.LastTick) <= periodMs)
                return;
            slot.Started = true;
            slot.LastTick = currentTick;

            var host = M2Share.PasEngine;
            if (host == null) return;

            var scriptPath = Path.Combine(
                M2Share.sConfigPath, M2Share.g_Config.sEnvirDir,
                "PsMapQuest", "RunQuest.pas");

            Interlocked.Increment(ref _dispatchCount);

            // 敲门：MyTimer(This_Player)。它自带 V(118,68)==100 自动回收总闸，命中才
            // Ys_HuiShou→AutoRecycle。TryCallProcedure 内部已做 File.Exists 与程序缓存，
            // 脚本里若没有 MyTimer 过程则静默返回 false，不抛。
            host.TryCallProcedure(scriptPath, LoopProcedure, player, null);
        }

        /// <summary>
        /// 配置真值判定，语义对齐 <c>YanshenApi.IsEnabledValue</c>（数值非零 / "true" /
        /// 非空串为真）。生产 <c>全局循环函数</c> 是整型 1。
        /// </summary>
        private static bool IsTruthy(object value)
        {
            value = PluginManager.NormalizeConfigValue(value);
            return value switch
            {
                null => false,
                bool b => b,
                sbyte n => n != 0,
                byte n => n != 0,
                short n => n != 0,
                ushort n => n != 0,
                int n => n != 0,
                uint n => n != 0,
                long n => n != 0,
                ulong n => n != 0,
                float n => n != 0,
                double n => n != 0,
                decimal n => n != 0,
                string s when bool.TryParse(s, out var b) => b,
                string s when double.TryParse(
                    s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d != 0,
                string s => !string.IsNullOrWhiteSpace(s),
                _ => true,
            };
        }

        private sealed class TickSlot
        {
            public bool Started;
            public int LastTick;
        }
    }
}
