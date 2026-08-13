using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Mall
{
    public sealed class MallRefreshScheduler
    {
        private readonly Dictionary<TimeSpan, DateTime> _nextRuns = new();

        public int Count => _nextRuns.Count;

        public bool TryAdd(string value, DateTime now)
        {
            if (!TimeSpan.TryParseExact(value?.Trim(), "hh\\:mm\\:ss", null,
                    out var timeOfDay) || timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
                return false;

            if (_nextRuns.ContainsKey(timeOfDay))
                return true;

            var next = now.Date.Add(timeOfDay);
            if (next <= now)
                next = next.AddDays(1);
            _nextRuns.Add(timeOfDay, next);
            return true;
        }

        public bool TryConsume(DateTime now)
        {
            var due = false;
            foreach (var timeOfDay in _nextRuns.Keys.ToArray())
            {
                var next = _nextRuns[timeOfDay];
                if (now < next)
                    continue;

                due = true;
                do
                {
                    next = next.AddDays(1);
                } while (next <= now);
                _nextRuns[timeOfDay] = next;
            }
            return due;
        }
    }

    /// <summary>
    /// 商城商品。字段集合是原生 <c>sub_636D68</c> 从 PAS <c>@GetYBShopConfig</c> 返回串里切出来
    /// 的那 10 个，不多不少：分隔符 <c>'$'</c>（<c>0x636F8F b1 24 mov cl,0x24</c>），序号 1..10
    /// （<c>0x636FC6 83 f8 0a cmp eax,0xA</c> + <c>ja</c>，第 11 个起丢弃），跳表在
    /// <c>0x636FD6</c>。原生商品表里**没有**货币类型 / 绑定标志 / 全服广播标志这三个字段。
    /// </summary>
    public class MallItem
    {
        /// <summary>字段 3 vGoodsIdx。落 record+0（<c>0x63710E 89 10</c>）。</summary>
        public int Id { get; set; }

        /// <summary>商品名，来自 <c>C_NeedLoadGoodsNames</c> 的 <c>'|'</c> 分量（<c>0x636DC7 b1 7c</c>）。</summary>
        public string ItemName { get; set; }

        public string GrantedItemName { get; set; }
        public int ItemIndex { get; set; }
        public int ItemCount { get; set; } = 1;

        /// <summary>字段 4 vSrcPrice。落 180 字节记录 +36（<c>0x637191 66 89 46 24</c>）。</summary>
        public int Price { get; set; }

        /// <summary>字段 5 vCurPrice。落 +38（<c>0x637199 66 89 46 26</c>）。脚本 ClientBuy 用它算总价。</summary>
        public int CurPrice { get; set; }

        /// <summary>白猪客户端分类：0=装饰 1=补给 2=强化 3=限量 4=好友。</summary>
        public byte Category { get; set; }

        /// <summary>字段 1 vClassName。落 +16。</summary>
        public string CategoryName { get; set; }

        /// <summary>字段 6 vLimitType。落 +40（<c>0x6371A1 66 89 46 28</c>）。0 不限购 / 1 每日 / 2 终身。</summary>
        public int LimitType { get; set; }

        /// <summary>字段 7 vLimitCount。落 +42（<c>0x6371A9 66 89 46 2a</c>）。</summary>
        public int LimitCount { get; set; }

        /// <summary>字段 8 vEffectImg。落 +48 的 DWORD（<c>0x6371B6 89 46 30</c>）。</summary>
        public int EffectImg { get; set; }

        /// <summary>字段 9 vEffectCount。落 +46 的 WORD（<c>0x6371BD 66 89 46 2e</c>）。</summary>
        public int EffectCount { get; set; }

        /// <summary>字段 10 vGoodsExplain。落 +52 的 ShortString[127]（<c>0x6371DF b1 7f</c>）。</summary>
        public string Description { get; set; }

        public int Stock { get; set; } // -1=无限
        public int SortOrder { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// 商城管理器
    /// </summary>
    public class MallManager
    {
        private static MallManager _instance;
        private static readonly object _lock = new object();
        private List<MallItem> _mallItems;
        private DateTime _lastLoadTime;
        private List<MallItem> _hotItems;
        private DateTime _lastHotLoadTime;
        private readonly MallRefreshScheduler _refreshScheduler;
        private readonly int _cacheMinutes = 5; // 缓存5分钟
        private byte[] _gpForbidBody = Array.Empty<byte>();
        private int _gpForbidCount;
        private bool _gpForbidLoaded;

        private MallManager()
        {
            _mallItems = new List<MallItem>();
            _lastLoadTime = DateTime.MinValue;
            _hotItems = new List<MallItem>();
            _lastHotLoadTime = DateTime.MinValue;
            _refreshScheduler = new MallRefreshScheduler();
        }

        public static MallManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MallManager();
                        }
                    }
                }
                return _instance;
            }
        }

        public bool TryGetGpForbidBody(out int count, out byte[] body)
        {
            EnsureGpForbidLoaded();
            count = _gpForbidCount;
            body = _gpForbidBody;
            return count > 0 && body != null && body.Length == count * 16;
        }

        private void EnsureGpForbidLoaded()
        {
            lock (_lock)
            {
                if (_gpForbidLoaded)
                {
                    return;
                }
                _gpForbidLoaded = true;
                var path = Path.Combine(
                    M2Share.sConfigPath ?? string.Empty,
                    M2Share.g_Config?.sEnvirDir ?? string.Empty,
                    "config", "GPForbidItems.txt");
                if (!File.Exists(path))
                {
                    return;
                }
                var text = HUtil32.GbkEncoding.GetString(File.ReadAllBytes(path));
                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var records = new List<byte[]>();
                foreach (var line in lines)
                {
                    var name = line.Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }
                    var gbk = HUtil32.GbkEncoding.GetBytes(name);
                    var n = Math.Min(gbk.Length, 15);
                    var rec = new byte[16];
                    rec[0] = (byte)n;
                    Buffer.BlockCopy(gbk, 0, rec, 1, n);
                    records.Add(rec);
                }
                if (records.Count == 0)
                {
                    return;
                }
                var body = new byte[records.Count * 16];
                for (var i = 0; i < records.Count; i++)
                {
                    Buffer.BlockCopy(records[i], 0, body, i * 16, 16);
                }
                _gpForbidCount = records.Count;
                _gpForbidBody = body;
            }
        }

        /// <summary>
        /// 从战神商城的 GBK PAS 脚本加载。
        /// </summary>
        public bool LoadMallItems()
        {
            lock (_lock)
            {
                if ((DateTime.Now - _lastLoadTime).TotalMinutes < _cacheMinutes && _mallItems.Count > 0)
                    return true;
            }

            try
            {
                var shopDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "YBShop");
                var pasPath = Path.Combine(shopDir, "YBShopScript.pas");
                List<MallItem> items = null;
                string source = null;

                if (File.Exists(pasPath))
                {
                    items = LoadPasMallItems(pasPath);
                    source = "GBK PAS";
                }

                if (items == null || items.Count == 0)
                {
                    M2Share.MainOutMessage($"[商城] 未加载到商品配置，已检查: {shopDir}");
                    return false;
                }

                lock (_lock)
                {
                    _mallItems = items;
                    _lastLoadTime = DateTime.Now;
                }

                M2Share.MainOutMessage($"[商城] 从{source}加载了 {items.Count} 个商城物品");
                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[商城] 加载商城配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 原生根本不解析脚本文本：<c>SendYBShopConfig</c> 的实现 <c>sub_636D68</c> 是**调用**
        /// PAS 函数 <c>@GetYBShopConfig</c>（<c>0x636F46 b9 50 73 63 00 mov ecx,0x637350</c> /
        /// <c>0x636F4F e8 84 fc ff ff call 0x636BD8</c>，<c>0x636F40 6a 07 push 7</c> = 8 个实参），
        /// 拿它的返回串再按 <c>'$'</c> 切 10 个字段。本类是那条链路的静态替身：
        /// 从脚本里把同样的 10 个字段抽出来，然后**走与原生完全相同的校验阶梯**。
        /// <para>
        /// 替身跟错脚本变体就整体失效——这正是之前发生的事：旧实现要求
        /// <c>C_NeedLoadGoodsNames =</c> 与 <c>'名': Result := '逗号,配置';</c>，
        /// 而生产脚本（<c>D:\光头卧龙\mud2.0\Mir200\Envir\YBShop\YBShopScript.pas</c>）写的是
        /// <c>C_NeedLoadGoodsNames_001 =</c> 与 <c>case</c> 分支里的逐个 <c>vXxx := …;</c> 赋值，
        /// 两条正则都 0 命中，于是商城常年加载 0 个商品。
        /// </para>
        /// </summary>
        private List<MallItem> LoadPasMallItems(string pasPath)
        {
            var script = HUtil32.GbkEncoding.GetString(File.ReadAllBytes(pasPath));
            foreach (Match refreshMatch in Regex.Matches(script,
                         @"SetYBShopRefreshTime\s*\(\s*'(?<time>[^']+)'\s*\)",
                         RegexOptions.IgnoreCase))
            {
                ConfigureRefreshTime(refreshMatch.Groups["time"].Value);
            }

            var configuredNames = ResolveConfiguredGoodsNames(script);
            if (configuredNames.Count == 0)
            {
                throw new InvalidDataException("C_NeedLoadGoodsNames 未配置");
            }

            var limitSlots = CollectLimitSlots(script);
            lock (_lock)
            {
                _limitSlots = limitSlots;
            }

            var configs = CollectGoodsConfigs(script);
            var items = new List<MallItem>();
            for (var order = 0; order < configuredNames.Count; order++)
            {
                var goodsName = configuredNames[order];
                if (!configs.TryGetValue(goodsName, out var config))
                {
                    M2Share.MainOutMessage($"[商城] 商品未找到配置: {goodsName}");
                    continue;
                }

                if (!TryParsePasMallItem(goodsName, config, order, out var item))
                {
                    // 对应原生 0x637213 那条臂：字段不合格就丢这一条并打日志，不静默降级。
                    M2Share.MainOutMessage($"[商城] 商品配置格式错误: {goodsName}");
                    continue;
                }
                items.Add(item);
            }

            AssignClientCategories(items);
            return items;
        }

        /// <summary>
        /// 取 <c>UsingGoodsName</c> 的 <c>'|'</c> 列表。原生把它当运行期实参收下
        /// （<c>SendYBShopConfig(UsingGoodsName)</c>），只负责按 <c>'|'</c> 切
        /// （<c>0x636DC7 b1 7c mov cl,0x7c</c>）。脚本可以声明成 <c>C_NeedLoadGoodsNames</c>
        /// 也可以带序号后缀 <c>_001/_002</c>；<c>Initialize</c> 里赋给 <c>UsingGoodsName</c> 的是
        /// 第一条，所以这里按声明顺序取第一条。
        /// </summary>
        private static List<string> ResolveConfiguredGoodsNames(string script)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var match = Regex.Match(script ?? string.Empty,
                @"\bC_NeedLoadGoodsNames\w*\s*=\s*'(?<names>[^']*)'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                return names;
            }

            foreach (var raw in match.Groups["names"].Value.Split('|',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var name = raw.Trim();
                if (name.Length != 0 && seen.Add(name))
                {
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>
        /// 把脚本里每个商品的配置还原成原生看到的那个 <c>'$'</c> 串。支持两种写法，
        /// 两种都收敛到同一个 10 字段契约：
        /// <list type="number">
        /// <item>直接返回字面量：<c>'名': Result := 'a$b$…';</c></item>
        /// <item>生产写法：<c>case GoodsName of '名': begin vClassName := …; … end;</c>，
        /// 函数开头那段初始化赋值作为缺省值，分支里的赋值覆盖它。</item>
        /// </list>
        /// </summary>
        private static Dictionary<string, string> CollectGoodsConfigs(string script)
        {
            var configs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(script,
                         @"'(?<name>[^']+)'\s*:\s*Result\s*:=\s*'(?<config>[^']*)'\s*;",
                         RegexOptions.IgnoreCase))
            {
                configs[match.Groups["name"].Value.Trim()] = match.Groups["config"].Value;
            }

            var defaults = ReadGoodsFieldAssignments(ExtractGetConfigPreamble(script));
            foreach (Match match in Regex.Matches(script,
                         @"'(?<name>[^']+)'\s*:\s*begin(?<body>.*?)\bend\s*;",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var body = match.Groups["body"].Value;
                // 嵌套 begin 说明这不是一条平铺的商品分支，形状不认识就整条不收。
                if (Regex.IsMatch(body, @"\bbegin\b", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                var fields = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in ReadGoodsFieldAssignments(body))
                {
                    fields[pair.Key] = pair.Value;
                }

                var name = match.Groups["name"].Value.Trim();
                if (name.Length != 0 && !configs.ContainsKey(name))
                {
                    configs[name] = JoinNativeGoodsConfig(fields);
                }
            }

            return configs;
        }

        /// <summary>
        /// <c>GetYBShopConfig</c> 的 <c>begin</c> 到 <c>case</c> 之间那段：Pascal 语义下它是所有
        /// 分支的缺省值，分支没赋的字段用的就是这里的值，不是 <c>StrToIntDef</c> 的 -1。
        /// </summary>
        private static string ExtractGetConfigPreamble(string script)
        {
            var match = Regex.Match(script ?? string.Empty,
                @"\bfunction\s+GetYBShopConfig\b.*?\bbegin\b(?<pre>.*?)\bcase\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups["pre"].Value : string.Empty;
        }

        private static readonly string[] NativeGoodsFieldOrder =
        {
            // sub_636D68 跳表 0x636FD6 的 1..10 号臂，顺序即协议顺序。
            "vClassName",   // 1  0x637002 -> [ebp-0x18]
            "vItemList",    // 2  0x637012 -> [ebp-0x1C]
            "vGoodsIdx",    // 3  0x63701F -> [ebp-0x20]
            "vSrcPrice",    // 4  0x63702F -> [ebp-0x24]
            "vCurPrice",    // 5  0x63703F -> [ebp-0x28]
            "vLimitType",   // 6  0x63704F -> [ebp-0x34]
            "vLimitCount",  // 7  0x63705F -> [ebp-0x38]
            "vEffectImg",   // 8  0x63706F -> [ebp-0x2C]
            "vEffectCount", // 9  0x63707F -> [ebp-0x30]
            "vGoodsExplain" // 10 0x63708F -> [ebp-0x3C]
        };

        private static Dictionary<string, string> ReadGoodsFieldAssignments(string body)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(body))
            {
                return fields;
            }

            foreach (var field in NativeGoodsFieldOrder)
            {
                var match = Regex.Match(body,
                    @"\b" + field + @"\s*:=\s*(?:'(?<s>[^']*)'|(?<n>-?\d+))\s*;",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }
                fields[field] = match.Groups["s"].Success
                    ? match.Groups["s"].Value
                    : match.Groups["n"].Value;
            }
            return fields;
        }

        private static string JoinNativeGoodsConfig(IReadOnlyDictionary<string, string> fields)
        {
            var parts = new string[NativeGoodsFieldOrder.Length];
            for (var i = 0; i < NativeGoodsFieldOrder.Length; i++)
            {
                parts[i] = fields.TryGetValue(NativeGoodsFieldOrder[i], out var value)
                    ? value
                    : string.Empty;
            }
            return string.Join(NativeGoodsFieldSeparator, parts);
        }

        public bool ConfigureRefreshTime(string value)
        {
            lock (_lock)
            {
                if (_refreshScheduler.TryAdd(value, DateTime.Now))
                    return true;
            }

            M2Share.MainOutMessage($"[商城] 忽略无效刷新时间: {value}");
            return false;
        }

        public void ProcessScheduledRefresh(DateTime now)
        {
            lock (_lock)
            {
                if (!_refreshScheduler.TryConsume(now))
                    return;
            }

            if (!RefreshCache())
            {
                M2Share.MainOutMessage("[商城] 定时刷新失败，保留上次有效商品配置");
                return;
            }

            var userEngine = M2Share.UserEngine;
            if (userEngine == null)
                return;
            foreach (var player in userEngine.PlayObjects.ToArray())
                player?.InvalidateWhitePigMallCache();
        }

        /// <summary>字段分隔符，原生 <c>0x636F8F b1 24 mov cl,0x24</c>。</summary>
        private const char NativeGoodsFieldSeparator = '$';

        /// <summary>
        /// 原生 <c>StrToIntDef</c> 的缺省值：每个整数臂前面都是
        /// <c>83 ca ff  or edx,0xFFFFFFFF</c>（例 <c>0x63701F</c>），再 <c>call 0x40CA18</c>。
        /// -1 同时是接收侧的"这条不合格"哨兵。
        /// </summary>
        private const int NativeGoodsIntDefault = -1;

        /// <summary>
        /// 原生 <c>sub_636D68</c> 的字段切分 + 校验阶梯，逐条对齐：
        /// <code>
        /// 00636F8F  b1 24              mov cl,0x24          ; '$'
        /// 00636FA7  43                 inc ebx              ; 字段序号 1 起
        /// 00636FC6  83 f8 0a           cmp eax,0xA
        /// 00636FC9  0f 87 cb 00 00 00  ja  0x63709A         ; 第 11 个字段起丢弃
        /// 006370AA  83 7d e8 00        cmp [ebp-0x18],0     ; vClassName 空      -> 丢
        /// 006370B4  83 7d e0 ff        cmp [ebp-0x20],-1    ; vGoodsIdx          -> 丢
        /// 006370BE  83 7d dc ff        cmp [ebp-0x24],-1    ; vSrcPrice          -> 丢
        /// 006370C8  83 7d d8 ff        cmp [ebp-0x28],-1    ; vCurPrice          -> 丢
        /// 006370D2  83 7d d4 ff        cmp [ebp-0x2c],-1    ; vEffectImg         -> 丢
        /// 006370DC  83 7d d0 ff        cmp [ebp-0x30],-1    ; vEffectCount       -> 丢
        /// </code>
        /// <c>vLimitType</c> / <c>vLimitCount</c> **不在校验清单里**，它们可以停在 -1 而记录仍被收下。
        /// </summary>
        private static bool TryParsePasMallItem(string goodsName, string config, int order,
            out MallItem item)
        {
            item = null;
            // 原生按 '$' 逐段切，序号 > 10 的段直接丢；这里保留同样的宽容度：段数不足才算坏。
            var fields = (config ?? string.Empty).Split(NativeGoodsFieldSeparator);
            if (fields.Length < NativeGoodsFieldOrder.Length)
            {
                return false;
            }

            var categoryName = fields[0].Trim();
            var itemSpec = fields[1].Trim();
            var goodsIdx = StrToIntDef(fields[2], NativeGoodsIntDefault);
            var srcPrice = StrToIntDef(fields[3], NativeGoodsIntDefault);
            var curPrice = StrToIntDef(fields[4], NativeGoodsIntDefault);
            var limitType = StrToIntDef(fields[5], NativeGoodsIntDefault);
            var limitCount = StrToIntDef(fields[6], NativeGoodsIntDefault);
            var effectImg = StrToIntDef(fields[7], NativeGoodsIntDefault);
            var effectCount = StrToIntDef(fields[8], NativeGoodsIntDefault);
            var description = fields[9].Trim();

            if (categoryName.Length == 0
                || goodsIdx == NativeGoodsIntDefault
                || srcPrice == NativeGoodsIntDefault
                || curPrice == NativeGoodsIntDefault
                || effectImg == NativeGoodsIntDefault
                || effectCount == NativeGoodsIntDefault)
            {
                return false;
            }

            // vItemList 是 '名:数量' 的 '/' 列表（sub_6CC420 的 0x6CC482 b1 2f）。本进程的发货侧
            // 是 fail-closed 的（§元宝结算在外部元宝库），列表只用于展示与 Looks 解析，所以这里
            // 只解第一段；多段时不静默截断，整条拒收。
            var tokens = itemSpec.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 1)
            {
                return false;
            }
            var token = tokens[0];
            var splitAt = token.LastIndexOf(':');
            var grantedItemName = splitAt > 0 ? token.Substring(0, splitAt).Trim() : token.Trim();
            // 0x6CC4E6 call 0x40CA18 (edx=1)：数量段缺失或非法时原生取 1。
            var itemCount = splitAt > 0 ? StrToIntDef(token.Substring(splitAt + 1), 1) : 1;
            if (grantedItemName.Length == 0)
            {
                return false;
            }

            item = new MallItem
            {
                Id = goodsIdx,
                ItemName = goodsName,
                GrantedItemName = grantedItemName,
                ItemCount = Math.Max(1, itemCount),
                Price = srcPrice,
                CurPrice = curPrice,
                // Category 由 AssignClientCategories 按首次出现顺序回填，不是固定名字表。
                Category = 0,
                CategoryName = categoryName,
                LimitType = limitType,
                LimitCount = limitCount,
                EffectImg = effectImg,
                EffectCount = effectCount,
                Stock = -1,
                SortOrder = order,
                IsEnabled = true,
                Description = description
            };
            return true;
        }

        /// <summary>Delphi <c>StrToIntDef</c>（<c>0x40CA18</c>）的语义：整串不是合法整数就取缺省值。</summary>
        private static int StrToIntDef(string value, int defaultValue)
        {
            return int.TryParse((value ?? string.Empty).Trim(), out var parsed)
                ? parsed
                : defaultValue;
        }

        /// <summary>分类上限 8 组：1104 处理器 <c>sub_639C58</c> 只接受 groupCount 1..8。</summary>
        private const int NativeMaxShopCategories = 8;

        /// <summary>
        /// 分类号不是"名字 → 固定编号"的映射表，而是 <c>vClassName</c> **首次出现的顺序**。
        /// 原生 <c>sub_635444</c> 拿 <c>vClassName</c> 去遍历分组表 <c>[owner+4]</c>
        /// （<c>0x6354BE</c> 循环，<c>0x6354E6 call 0x40BD78</c> 比名字），命中就用它的下标，
        /// 没命中就在 <c>0x635512 mov eax,0x1C</c> 处新建一个分组追加到表尾。命中/新建之后
        /// <c>0x6354FC inc eax</c> / <c>0x635502 66 89 42 7a  mov [rec+0x7A],ax</c> 把
        /// **1 起**的分组号写进记录 +0x7A（= 180 字节块的 +34 page 字段）；PAS 构建器
        /// <c>sub_6359E8</c> 再把它减 1 变成 0 起的客户端分类号。
        /// <para>
        /// 之前这里写死 装饰=0/补给=1/强化=2/限量=3/好友=4 并且把不认识的名字整条拒收。
        /// 生产脚本只用 装饰 与 强化 两类，按原生规则它们是 0 和 **1**，写死表会把 强化 送到 2；
        /// 而任何用了别的分类名的脚本会被整表丢光。
        /// </para>
        /// </summary>
        private static void AssignClientCategories(List<MallItem> items)
        {
            var order = new List<string>(NativeMaxShopCategories);
            var overflowed = new List<MallItem>();
            foreach (var item in items)
            {
                var index = order.FindIndex(existing =>
                    string.Equals(existing, item.CategoryName, StringComparison.Ordinal));
                if (index < 0)
                {
                    if (order.Count >= NativeMaxShopCategories)
                    {
                        M2Share.MainOutMessage(
                            $"[商城] 分类数超过 {NativeMaxShopCategories} 组，丢弃商品: {item.ItemName}");
                        overflowed.Add(item);
                        continue;
                    }
                    order.Add(item.CategoryName);
                    index = order.Count - 1;
                }
                item.Category = (byte)index;
            }

            foreach (var item in overflowed)
            {
                items.Remove(item);
            }
        }

        /// <summary>
        /// 获取所有商城物品
        /// </summary>
        public List<MallItem> GetAllItems()
        {
            LoadMallItems();
            lock (_lock)
            {
                return new List<MallItem>(_mallItems);
            }
        }

        /// <summary>
        /// 根据分类获取商城物品
        /// </summary>
        public List<MallItem> GetItemsByCategory(byte category)
        {
            LoadMallItems();
            lock (_lock)
            {
                return _mallItems.FindAll(item => item.IsEnabled && item.Stock != 0 && item.Category == category);
            }
        }

        /// <summary>
        /// 原生 <c>sub_63A254</c> 只卡 <c>type &lt; 8</c>，缓存也是 8 组
        /// （1104 处理器 <c>sub_639C58</c> 接受 groupCount 1..8）。之前这里卡在 0..4，
        /// 分类 5..7 的商品在 C# 侧永远取不到。
        /// </summary>
        public List<MallItem> GetItemsForClientType(int requestedType)
        {
            LoadMallItems();
            lock (_lock)
            {
                if (requestedType < 0 || requestedType >= NativeMaxShopCategories)
                {
                    return new List<MallItem>();
                }
                return _mallItems.FindAll(item => item.IsEnabled && item.Stock != 0 && item.Category == requestedType);
            }
        }

        public List<MallItem> GetHotItems(int maxCount)
        {
            maxCount = Math.Clamp(maxCount, 1, 5);
            LoadMallItems();

            lock (_lock)
            {
                if ((DateTime.Now - _lastHotLoadTime).TotalMinutes < _cacheMinutes)
                {
                    return _hotItems.GetRange(0, Math.Min(maxCount, _hotItems.Count));
                }
            }

            var rankedNames = LoadHotItemNames();
            lock (_lock)
            {
                var result = new List<MallItem>(maxCount);
                foreach (var rankedName in rankedNames)
                {
                    var item = _mallItems.Find(candidate => candidate.IsEnabled
                        && candidate.Stock != 0
                        && string.Equals(candidate.ItemName, rankedName, StringComparison.OrdinalIgnoreCase));
                    if (item != null && !result.Contains(item))
                    {
                        result.Add(item);
                        if (result.Count == maxCount)
                        {
                            break;
                        }
                    }
                }

                if (result.Count < maxCount)
                {
                    foreach (var item in _mallItems)
                    {
                        if (item.IsEnabled && item.Stock != 0 && !result.Contains(item))
                        {
                            result.Add(item);
                            if (result.Count == maxCount)
                            {
                                break;
                            }
                        }
                    }
                }

                _hotItems = result;
                _lastHotLoadTime = DateTime.Now;
                return new List<MallItem>(result);
            }
        }

        public void InvalidateHotItems()
        {
            lock (_lock)
            {
                _hotItems.Clear();
                _lastHotLoadTime = DateTime.MinValue;
            }
        }

        private static List<string> LoadHotItemNames()
        {
            var result = new List<string>();
            try
            {
                using var conn = new MySqlConnection(M2Share.g_Config.sConnctionString);
                conn.Open();
                const string sql = @"SELECT HEX(GoodsName), SUM(GoodsCount) AS TotalCount
                                     FROM gamelog.YBGoods_Buy_Log
                                     GROUP BY GoodsName
                                     ORDER BY TotalCount DESC
                                     LIMIT 5";
                using var cmd = new MySqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    var hexName = reader.GetString(0);
                    if (string.IsNullOrEmpty(hexName))
                    {
                        continue;
                    }

                    var itemName = HUtil32.GbkEncoding.GetString(Convert.FromHexString(hexName)).TrimEnd('\0', ' ');
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        result.Add(itemName);
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[商城] 读取原始热销排行失败，将按配置顺序补齐: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 解析商城配置对应的真实标准物品。
        /// 优先使用配置索引，若索引和名称明显不匹配，则回退到按名称查找。
        /// </summary>
        public GoodItem ResolveStdItem(MallItem mallItem, out int resolvedItemIndex)
        {
            resolvedItemIndex = mallItem?.ItemIndex ?? 0;
            if (mallItem == null)
            {
                return null;
            }

            GoodItem stdItem = null;
            if (mallItem.ItemIndex > 0)
            {
                stdItem = M2Share.UserEngine.GetStdItem(mallItem.ItemIndex);
            }

            var grantedItemName = string.IsNullOrEmpty(mallItem.GrantedItemName)
                ? mallItem.ItemName
                : mallItem.GrantedItemName;
            if (!string.IsNullOrEmpty(grantedItemName))
            {
                bool needResolveByName = stdItem == null || !string.Equals(stdItem.Name, grantedItemName, StringComparison.OrdinalIgnoreCase);
                if (needResolveByName)
                {
                    int nameIndex = M2Share.UserEngine.GetStdItemIdx(grantedItemName);
                    if (nameIndex > 0)
                    {
                        var stdItemByName = M2Share.UserEngine.GetStdItem(nameIndex);
                        if (stdItemByName != null)
                        {
                            resolvedItemIndex = nameIndex;
                            stdItem = stdItemByName;
                        }
                    }
                }
            }

            return stdItem;
        }

        /// <summary>
        /// 根据ID获取商城物品
        /// </summary>
        public MallItem GetItemById(int id)
        {
            LoadMallItems();
            lock (_lock)
            {
                return _mallItems.Find(item => item.Id == id);
            }
        }

        public MallItem GetItemByName(string itemName)
        {
            LoadMallItems();
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return null;
            }
            lock (_lock)
            {
                return _mallItems.Find(item => item.IsEnabled
                    && string.Equals(item.ItemName, itemName.Trim(), StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// 购买商城物品
        /// </summary>
        public bool PurchaseItem(TPlayObject player, int mallItemId, int quantity, out string errorMsg)
        {
            var mallItem = GetItemById(mallItemId);
            if (mallItem == null)
            {
                errorMsg = "商城物品不存在";
                return false;
            }
            return PurchaseItem(player, mallItem, quantity, out _, out errorMsg);
        }

        public bool PurchaseItemByName(TPlayObject player, string itemName, int quantity, out int failureCode, out string errorMsg)
        {
            var mallItem = GetItemByName(itemName);
            if (mallItem == null)
            {
                failureCode = -5;
                errorMsg = "购买物品不在商城中";
                return false;
            }
            return PurchaseItem(player, mallItem, quantity, out failureCode, out errorMsg);
        }

        /// <summary>
        /// 原生购买阶梯完全在脚本 <c>ClientBuy</c> 里，引擎侧 <c>sub_6CB7E4</c> 只做确认闸
        /// （<c>0x6CB816 call 0x6C7D88</c>）然后调 <c>@ClientBuy</c>
        /// （<c>0x6CB8C9 mov ecx,0x6CB940</c> / <c>0x6CB8D0 call 0x636BD8</c>，4 个实参，
        /// 其中 NeedNum 由引擎 <c>sub_6373E8</c> 算好）。生产脚本的阶梯逐条是：
        /// <code>
        /// 1  GetS(80,40) &lt;&gt; GetDateNum(GetNow) -> EverydayClearLimitValue; SetS(80,40,today)
        /// 2  FreeBagNum &gt;= NeedNum           else '您身上没有足够的空间。'
        /// 3  GetYBShopConfig(...) &lt;&gt; ''      else '商城未出售此物品。'
        /// 4  (WantNum &gt; 0) and (WantNum &lt; 1000) else '购买数量不合法。'
        /// 5  not ((vLimitType &gt; 0) and (Cur + WantNum &gt; vLimitCount)) else '已达到限购数上限。'
        /// 6  Price := WantNum * vCurPrice                      &lt;&lt;&lt; 折扣价，不是原价
        /// 7  YBNum &gt;= Price                  else '身上没有足够的元宝。'
        /// 8  AddToBuyGoodsLogByScript(...) &gt; 0 else '系统繁忙，操作失败。'   &lt;&lt;&lt; 日志在扣费之前
        /// 9  PsYBConsumEx(2,'YBShopBuy_YB',...) else '申请扣元宝失败！'      &lt;&lt;&lt; 异步外部扣费
        /// 10 扣费成功后才 SetLimitValue(Cur + WantNum)
        /// </code>
        /// 付款货币**只有元宝**，而且是外部元宝库异步结算的；引擎里没有任何一条本地扣款指令
        /// （<c>sub_6CB7E4</c> 与发货核心 <c>sub_6CC420</c> 通篇没有对金币 <c>[player+0x15C]</c>、
        /// 声望或脚本变量的减法）。本进程无法忠实完成那次结算，所以在结算闸上 fail-closed：
        /// **不扣任何货币，也不发放任何物品**。
        /// </summary>
        private bool PurchaseItem(TPlayObject player, MallItem mallItem, int quantity, out int failureCode, out string errorMsg)
        {
            failureCode = -1;
            errorMsg = string.Empty;
            try
            {
                // 脚本第 4 步是硬拒绝，不是夹取。原来的 Math.Clamp(quantity,1,99) 会把 0 和 1000+
                // 静默改成合法值，既不忠实也违反"不许静默截断"。
                if (quantity <= 0 || quantity >= 1000)
                {
                    failureCode = -1;
                    errorMsg = "购买数量不合法。";
                    return false;
                }

                if (!mallItem.IsEnabled || mallItem.Stock == 0
                    || (mallItem.Stock > 0 && mallItem.Stock < quantity))
                {
                    failureCode = -5;
                    errorMsg = "该物品已售空";
                    return false;
                }

                var stdItem = ResolveStdItem(mallItem, out _);
                if (stdItem == null)
                {
                    failureCode = -1;
                    errorMsg = "物品配置错误";
                    return false;
                }

                var grantCountLong = (long)Math.Max(1, mallItem.ItemCount) * quantity;
                var bagCapacity = BagCapacity.Of(player);
                if (grantCountLong > bagCapacity
                    || player.m_ItemList.Count + grantCountLong > bagCapacity)
                {
                    failureCode = -5;
                    errorMsg = "您身上没有足够的空间。";
                    return false;
                }

                // 脚本第 1 步的日期重置（EverydayClearLimitValue）是一段 for I := 1 to 50 的
                // 变址循环，本进程没有 PAS 解释器跑它，按 fail-closed 不做替身：
                // 生产脚本的 GetLimitValue/SetLimitValue 本来就是空桩，限购恒 0，无可重置。
                // 见报告 MALL-08 BLOCKED。
                var currentLimit = GetCurrentLimit(player, mallItem);
                if (mallItem.LimitType > 0 && currentLimit + quantity > mallItem.LimitCount)
                {
                    failureCode = -5;
                    errorMsg = "已达到限购数上限。";
                    return false;
                }

                // 第 6 步：总价 = 数量 * vCurPrice（折扣价）。之前这里用的是 vSrcPrice（原价）。
                var totalPriceLong = (long)mallItem.CurPrice * quantity;
                if (totalPriceLong < 0 || totalPriceLong > int.MaxValue)
                {
                    failureCode = -1;
                    errorMsg = "商品价格配置错误";
                    return false;
                }
                var totalPrice = (int)totalPriceLong;

                // 第 7..9 步：结算闸。fail-closed 必须在**任何**扣款与**任何**发放之前，
                // 这样"扣钱不给物"与"给物不扣钱"两个方向都不可能发生。
                if (!TrySettleYuanbaoPayment(player, mallItem, quantity, totalPrice, out errorMsg))
                {
                    failureCode = -3;
                    return false;
                }

                var userItems = new List<TUserItem>((int)grantCountLong);
                for (var i = 0; i < grantCountLong; i++)
                {
                    TUserItem userItem = null;
                    if (!M2Share.UserEngine.CopyToUserItemFromName(stdItem.Name, ref userItem) || userItem == null)
                    {
                        failureCode = -1;
                        errorMsg = "物品配置错误";
                        return false;
                    }
                    userItems.Add(userItem);
                }

                foreach (var userItem in userItems)
                {
                    player.m_ItemList.Add(userItem);
                    player.SendAddItem(userItem);
                }

                // 第 10 步：限购计数只在结算成功之后写。
                SetCurrentLimit(player, mallItem, currentLimit + quantity);
                if (mallItem.Stock > 0)
                {
                    UpdateStock(mallItem.Id, -quantity);
                }
                LogPurchase(player, mallItem, quantity, totalPrice);
                failureCode = 0;
                return true;
            }
            catch (Exception ex)
            {
                failureCode = -1;
                errorMsg = "购买失败，请稍后重试";
                M2Share.MainOutMessage($"[商城] 购买失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 唯一的付款闸，永远 fail-closed。
        /// <para>
        /// 原生商城只收元宝，且结算不在本进程：脚本 <c>ClientBuy</c> 先判 <c>This_Player.YBNum &gt;= Price</c>，
        /// 再 <c>PsYBConsumEx(2, 'YBShopBuy_YB', …)</c> 把扣费请求交给外部元宝库，扣成之后元宝库
        /// 回调脚本函数 <c>YBShopBuy_YB</c> 才发货。引擎里没有任何本地扣款：CM_DOSHOP 处理器
        /// <c>sub_6CB7E4</c> 只有确认闸 + PAS 调用，发货核心 <c>sub_6CC420</c> 从头到尾只有
        /// <c>0x6CC504 add [esi+0xBD8],eax</c>（发灵符）这一条加法，一条减法都没有。
        /// </para>
        /// <para>
        /// 本地写 <c>m_nGameGold</c> 也不行：它由外部元宝库拥有，约 10 秒一次的 1103 资金刷新
        /// （<c>TPlayObject.NativeYbCredit.ApplyNativeYb1103Snapshot</c> → <c>m_nGameGold = currentYuanbao</c>）
        /// 会把本地扣减整个覆盖回去，玩家等于既留住元宝又拿到物品。
        /// </para>
        /// <para>
        /// 曾经这里还有 金币 / 声望 / 充值点(V 变量) 三条本地扣款分支。原生商品表**没有货币类型
        /// 这个字段**——<c>sub_636D68</c> 的 10 个字段是 vClassName / vItemList / vGoodsIdx /
        /// vSrcPrice / vCurPrice / vLimitType / vLimitCount / vEffectImg / vEffectCount /
        /// vGoodsExplain（跳表 <c>0x636FD6</c>，上限 <c>0x636FC6 cmp eax,0xA</c>），
        /// 所以那三条是拿原生从不接受的货币换真实物品，属 INVENTED，一并移除。
        /// </para>
        /// </summary>
        private static bool TrySettleYuanbaoPayment(TPlayObject player, MallItem item,
            int quantity, int totalPrice, out string errorMsg)
        {
            // 脚本第 7 步 `if This_Player.YBNum >= Price`：余额判定可以本地做，它是只读的。
            if (player == null || player.m_nGameGold < totalPrice)
            {
                errorMsg = "身上没有足够的元宝。";
                return false;
            }

            // 脚本第 9 步 PsYBConsumEx：外部元宝库异步扣费，本进程没有等价实现。
            // 没有它就没有"已扣款"这个事实，因此不能发货。
            // 这里不打日志：CM_DOSHOP 在生产计数器里是 290,008 次，属热路径。
            // item / quantity 留在签名里，是因为 PsYBConsumEx 的实参就是
            // (2, 'YBShopBuy_YB', GoodsName, vGoodsIdx, vCurPrice, WantNum)，接线时要用。
            errorMsg = "申请扣元宝失败！";
            return false;
        }

        // Taking the bank as a dictionary was the shape that broke: group 0 of V is not
        // in a dictionary at all, so a caller handing over m_ScriptVVars read zero for
        // every group-0 coordinate. Both helpers now name the bank and let the player
        // object resolve where the triple lives.
        private static int GetPlayerVariable(TPlayObject player, char bank, int group, int index)
        {
            return player != null && player.TryGetScriptVar(bank, group, index, out var value)
                ? value
                : 0;
        }

        // 这两个助手读写的是 GetV/GetS/SetV/SetS 的同一块每角色存储，且会随存档落盘，
        // 所以必须照原生 upsert sub_6E4140 的语义：它没有任何零值判断，四个存储点
        // （0x6E4187 / 0x6E41C2 / 0x6E4231 / 0x6E4260 `mov [..], edx`）一律原样写入，
        // 而且原生根本没有"删除某个键"的原语。此处一旦把 0 当成删除，内存里读回
        // GetV 的 miss 哨兵 -1（0x6DF1F1 `mov [ebp-4],0xFFFFFFFF`）。编码器
        // 0x6E4DE7/0x6E4E19 是对当前动态数组的整块 Move，缺键就是缺键。
        private static void SetPlayerVariable(TPlayObject player, char bank, int group, int index, int value)
        {
            player?.SetScriptVar(bank, group, index, value);
        }

        /// <summary>
        /// 一件商品的限购计数存在哪个脚本变量里，由脚本的 <c>GetLimitValue</c> /
        /// <c>SetLimitValue</c> 自己决定，引擎不参与——引擎只在发包前回调
        /// <c>@GetLimitValue</c> 拿一个数写进 180 字节记录的 +44
        /// （<c>sub_63CD0C</c>：<c>0x63CD68 66 83 78 28 00 cmp word [rec+0x28],0</c> /
        /// <c>jbe</c> 只在 limitType &gt; 0 时回调，<c>0x63CDC3 66 89 42 2c</c> 存结果，
        /// <c>0x63CDCC</c> 脚本没给值就写 0）。
        /// </summary>
        private readonly struct MallLimitSlot
        {
            public MallLimitSlot(char bank, int group, int index)
            {
                Bank = bank;
                Group = group;
                Index = index;
            }

            public char Bank { get; }
            public int Group { get; }
            public int Index { get; }
        }

        private Dictionary<string, MallLimitSlot> _limitSlots =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 从脚本里抽出 <c>GetLimitValue</c> / <c>SetLimitValue</c> 两个 case 分支声明的坐标。
        /// 生产脚本把这两个函数整个注释掉了（<c>GetLimitValue</c> 只有 <c>Result := 0;</c>，
        /// <c>SetLimitValue</c> 是空过程），所以生产环境下这张表是**空的**，
        /// 限购读回 0、也不写任何变量——与原生逐字节一致。
        /// <para>
        /// 之前这里写死 <c>S(300,商品编号)</c> / <c>S(301,商品编号)</c> 以及日期标记
        /// <c>S(302,99)</c>，三个坐标在原生里都不存在。而且它们会随 <c>HumData.ScriptS</c>
        /// 落进人物存档，属 §1.4 的记录布局问题：换回原版 Delphi 跑时这些键就是垃圾。
        /// 更糟的是 <c>GetCurrentLimitValue</c> 被商品列表渲染调用，生产里 1046 有 50,039 次，
        /// 等于**每次打开商城面板都往存档里写一个原生没有的键**。
        /// </para>
        /// <para>
        /// 顺带修掉旧重置逻辑自己的两个洞：它按 <c>key &gt; 300*1000 &amp;&amp; key &lt; 300*1000+100</c>
        /// 扫字典，而生产商品编号是 218/222/247..251 全部 ≥ 100，所以"每日限购"其实从来不重置；
        /// 编号 ≥ 1000 时 <c>S(300,1000)</c> 的平铺键 301000 还会撞上 <c>S(301,0)</c>。
        /// </para>
        /// </summary>
        private static Dictionary<string, MallLimitSlot> CollectLimitSlots(string script)
        {
            var slots = new Dictionary<string, MallLimitSlot>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(script))
            {
                return slots;
            }

            // 先去掉行注释，否则生产脚本里被注释掉的示例坐标会被当成真配置。
            var live = Regex.Replace(script, @"//[^\r\n]*", string.Empty);
            var body = Regex.Match(live,
                @"\bfunction\s+GetLimitValue\b(?<body>.*?)\bend\s*;\s*(?:procedure|function|Begin)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!body.Success)
            {
                return slots;
            }

            foreach (Match match in Regex.Matches(body.Groups["body"].Value,
                         @"'(?<name>[^']+)'\s*:\s*Result\s*:=\s*This_Player\.Get(?<bank>[VS])\s*\(\s*"
                         + @"(?<group>-?\d+)\s*,\s*(?<index>-?\d+)\s*\)",
                         RegexOptions.IgnoreCase))
            {
                slots[match.Groups["name"].Value.Trim()] = new MallLimitSlot(
                    char.ToUpperInvariant(match.Groups["bank"].Value[0]),
                    int.Parse(match.Groups["group"].Value),
                    int.Parse(match.Groups["index"].Value));
            }
            return slots;
        }

        private bool TryGetLimitSlot(MallItem mallItem, out MallLimitSlot slot)
        {
            slot = default;
            if (mallItem == null || mallItem.LimitType <= 0)
            {
                return false;
            }
            lock (_lock)
            {
                return _limitSlots.TryGetValue(mallItem.ItemName, out slot);
            }
        }

        private int GetCurrentLimit(TPlayObject player, MallItem mallItem)
        {
            // 脚本没声明坐标就是 0，对应生产 GetLimitValue 的 `Result := 0;`
            // 以及原生 0x63CDCC `66 c7 40 2c 00 00` 那条"脚本没给值写 0"的臂。
            return TryGetLimitSlot(mallItem, out var slot)
                ? GetPlayerVariable(player, slot.Bank, slot.Group, slot.Index)
                : 0;
        }

        /// <summary>
        /// 只读。原生这条路径（<c>sub_63CD0C</c>）只回调脚本取值再写进要发出去的记录，
        /// 不改任何玩家状态。日期重置是脚本 <c>ClientBuy</c> 的第 1 步，属购买路径，
        /// 不是列表渲染路径。
        /// </summary>
        public int GetCurrentLimitValue(TPlayObject player, MallItem mallItem)
        {
            if (player == null || mallItem == null)
            {
                return 0;
            }
            return GetCurrentLimit(player, mallItem);
        }

        private void SetCurrentLimit(TPlayObject player, MallItem mallItem, int value)
        {
            if (TryGetLimitSlot(mallItem, out var slot))
            {
                SetPlayerVariable(player, slot.Bank, slot.Group, slot.Index, value);
            }
        }

        private void UpdateStock(int mallItemId, int change)
        {
            lock (_lock)
            {
                var item = _mallItems.Find(i => i.Id == mallItemId);
                if (item != null && item.Stock > 0)
                    item.Stock += change;
            }
        }

        /// <summary>
        /// 记录购买日志
        /// </summary>
        private void LogPurchase(TPlayObject player, MallItem item, int quantity, int totalPrice)
        {
            try
            {
                using (var conn = new MySqlConnection(M2Share.g_Config.sConnctionString))
                {
                    conn.Open();
                    const string sql = @"INSERT INTO gamelog.YBGoods_Buy_Log
                                         (UpdateTime, PTID, UserID, CharName, GoodsIdx, GoodsName,
                                          GoodsCount, UseCredit, CurrentCredit, Status)
                                         VALUES
                                         (NOW(), @ptid, 0, @charName, @goodsIdx, @goodsName,
                                          @goodsCount, @useCredit, @currentCredit, 'True')";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@ptid", player.m_sUserID ?? string.Empty);
                        cmd.Parameters.Add("@charName", MySqlDbType.VarBinary).Value = EncodeGbkForDatabase(player.m_sCharName, 14);
                        cmd.Parameters.AddWithValue("@goodsIdx", item.Id);
                        cmd.Parameters.Add("@goodsName", MySqlDbType.VarBinary).Value = EncodeGbkForDatabase(item.ItemName, 14);
                        cmd.Parameters.AddWithValue("@goodsCount", quantity);
                        cmd.Parameters.AddWithValue("@useCredit", totalPrice);
                        // 唯一的付款货币是元宝，余额权威在外部元宝库，本地镜像是 m_nGameGold。
                        cmd.Parameters.AddWithValue("@currentCredit", player.m_nGameGold);
                        cmd.ExecuteNonQuery();
                    }
                }

                lock (_lock)
                {
                    _hotItems.Clear();
                    _lastHotLoadTime = DateTime.MinValue;
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[商城] 记录购买日志失败: {ex.Message}");
            }
        }

        private static byte[] EncodeGbkForDatabase(string value, int maxBytes)
        {
            var source = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            var writeCount = 0;
            for (var i = 0; i < source.Length && writeCount < maxBytes;)
            {
                var charBytes = source[i] >= 0x81 && source[i] <= 0xFE && i + 1 < source.Length ? 2 : 1;
                if (writeCount + charBytes > maxBytes)
                {
                    break;
                }
                writeCount += charBytes;
                i += charBytes;
            }

            var result = new byte[writeCount];
            Buffer.BlockCopy(source, 0, result, 0, writeCount);
            return result;
        }

        /// <summary>
        /// 强制刷新缓存
        /// </summary>
        public bool RefreshCache()
        {
            lock (_lock)
            {
                _lastLoadTime = DateTime.MinValue;
                _lastHotLoadTime = DateTime.MinValue;
                _hotItems.Clear();
            }
            return LoadMallItems();
        }

        /// <summary>
        /// 分类名来自脚本的 <c>vClassName</c>，按首次出现顺序编号（见 <see cref="AssignClientCategories"/>），
        /// 所以只能从已加载的商品里反查，不能用固定名字表。
        /// </summary>
        public string GetCategoryName(byte category)
        {
            lock (_lock)
            {
                var item = _mallItems.Find(candidate => candidate.Category == category);
                return item != null ? item.CategoryName : $"分类{category}";
            }
        }

    }
}
