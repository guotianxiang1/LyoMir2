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
    /// 商城物品数据模型
    /// </summary>
    public class MallItem
    {
        public int Id { get; set; }
        public string ItemName { get; set; }
        public string GrantedItemName { get; set; }
        public int ItemIndex { get; set; }
        public int ItemCount { get; set; } = 1;
        public int Price { get; set; }
        public int CurPrice { get; set; }
        public byte CurrencyType { get; set; } // 0=元宝, 1=金币, 2=灵符, 3=声望, 4=充值点
        public int PaymentVariableGroup { get; set; }
        public int PaymentVariableIndex { get; set; }
        public byte Category { get; set; } // 白猪客户端: 0=装饰, 1=补给, 2=强化, 3=限量, 4=好友
        public string CategoryName { get; set; }
        public int LimitType { get; set; }
        public int LimitCount { get; set; }
        public bool IsBound { get; set; }
        public bool BroadcastPurchase { get; set; }
        public int Stock { get; set; } // -1=无限
        public int SortOrder { get; set; }
        public bool IsEnabled { get; set; }
        public string Description { get; set; }
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

        private static readonly Dictionary<byte, string> _categoryNames = new Dictionary<byte, string>
        {
            { 0, "装饰" },
            { 1, "补给" },
            { 2, "强化" },
            { 3, "限量" },
            { 4, "好友" },
            { 5, "热销" }
        };

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

        private List<MallItem> LoadPasMallItems(string pasPath)
        {
            var script = HUtil32.GbkEncoding.GetString(File.ReadAllBytes(pasPath));
            ParsePaymentVariable(script, out var paymentVariableGroup, out var paymentVariableIndex);
            foreach (Match refreshMatch in Regex.Matches(script,
                         @"SetYBShopRefreshTime\s*\(\s*'(?<time>[^']+)'\s*\)",
                         RegexOptions.IgnoreCase))
            {
                ConfigureRefreshTime(refreshMatch.Groups["time"].Value);
            }
            var namesMatch = Regex.Match(script,
                @"C_NeedLoadGoodsNames\s*=\s*'(?<names>[^']*)'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!namesMatch.Success)
            {
                throw new InvalidDataException("C_NeedLoadGoodsNames 未配置");
            }

            var configs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var configMatches = Regex.Matches(script,
                @"'(?<name>[^']+)'\s*:\s*Result\s*:=\s*'(?<config>[^']*)'\s*;",
                RegexOptions.IgnoreCase);
            foreach (Match match in configMatches)
            {
                configs[match.Groups["name"].Value.Trim()] = match.Groups["config"].Value;
            }

            var items = new List<MallItem>();
            var configuredNames = namesMatch.Groups["names"].Value.Split('|', StringSplitOptions.RemoveEmptyEntries);
            for (var order = 0; order < configuredNames.Length; order++)
            {
                var goodsName = configuredNames[order].Trim();
                if (!configs.TryGetValue(goodsName, out var config))
                {
                    M2Share.MainOutMessage($"[商城] 商品未找到配置: {goodsName}");
                    continue;
                }

                if (!TryParsePasMallItem(goodsName, config, order,
                        paymentVariableGroup, paymentVariableIndex, out var item))
                {
                    M2Share.MainOutMessage($"[商城] 商品配置格式错误: {goodsName}");
                    continue;
                }
                items.Add(item);
            }

            return items;
        }

        private static void ParsePaymentVariable(string script, out int group, out int index)
        {
            group = 0;
            index = 0;
            var match = Regex.Match(script ?? string.Empty,
                @"\bFPayTask\s*=\s*'(?<group>[^,']*)\s*,\s*(?<index>[^']*)'",
                RegexOptions.IgnoreCase);
            if (!match.Success
                || !int.TryParse(match.Groups["group"].Value.Trim(), out group)
                || !int.TryParse(match.Groups["index"].Value.Trim(), out index)
                || group <= 0 || index <= 0)
            {
                group = 0;
                index = 0;
            }
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

        private static bool TryParsePasMallItem(string goodsName, string config, int order,
            int paymentVariableGroup, int paymentVariableIndex, out MallItem item)
        {
            item = null;
            var fields = config.Split(new[] { ',' }, 11, StringSplitOptions.None);
            if (fields.Length != 11)
            {
                return false;
            }

            var itemSpec = fields[1].Trim();
            var splitAt = itemSpec.LastIndexOf(':');
            var grantedItemName = splitAt > 0 ? itemSpec.Substring(0, splitAt).Trim() : itemSpec;
            var itemCountText = splitAt > 0 ? itemSpec.Substring(splitAt + 1).Trim() : "1";
            if (string.IsNullOrEmpty(grantedItemName)
                || !int.TryParse(itemCountText, out var itemCount)
                || !int.TryParse(fields[2], out var id)
                || !int.TryParse(fields[3], out var price)
                || !int.TryParse(fields[4], out var curPrice)
                || !int.TryParse(fields[5], out var limitType)
                || !int.TryParse(fields[6], out var limitCount)
                || !byte.TryParse(fields[7], out var currencyType)
                || !int.TryParse(fields[8], out var bindFlag)
                || !int.TryParse(fields[9], out var broadcastFlag))
            {
                return false;
            }

            var categoryName = fields[0].Trim();
            if (!TryGetClientCategory(categoryName, out var category))
            {
                return false;
            }

            item = new MallItem
            {
                Id = id,
                ItemName = goodsName,
                GrantedItemName = grantedItemName,
                ItemCount = Math.Max(1, itemCount),
                Price = Math.Max(0, price),
                CurPrice = curPrice == 1 ? 1 : 0,
                CurrencyType = currencyType,
                PaymentVariableGroup = currencyType == 4 ? paymentVariableGroup : 0,
                PaymentVariableIndex = currencyType == 4 ? paymentVariableIndex : 0,
                Category = category,
                CategoryName = categoryName,
                LimitType = limitType,
                LimitCount = limitCount,
                IsBound = bindFlag == 0,
                BroadcastPurchase = broadcastFlag == 0,
                Stock = -1,
                SortOrder = order,
                IsEnabled = true,
                Description = fields[10].Trim()
            };
            return true;
        }

        private static bool TryGetClientCategory(string categoryName, out byte category)
        {
            switch (categoryName)
            {
                case "装饰": category = 0; return true;
                case "补给": category = 1; return true;
                case "强化": category = 2; return true;
                case "限量": category = 3; return true;
                case "好友": category = 4; return true;
                default: category = 0; return false;
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

        public List<MallItem> GetItemsForClientType(int requestedType)
        {
            LoadMallItems();
            lock (_lock)
            {
                if (requestedType < 0 || requestedType > 4)
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

        private bool PurchaseItem(TPlayObject player, MallItem mallItem, int quantity, out int failureCode, out string errorMsg)
        {
            failureCode = -1;
            errorMsg = string.Empty;
            try
            {
                quantity = Math.Clamp(quantity, 1, 99);

                // SHIELD (non-native dupe): currencyType 0 (元宝) is m_nGameGold, which is authoritatively
                // owned by the external YBDB (元宝库) and is overwritten by the ~10s 1103 capital refresh
                // (see TPlayObject.NativeYbCredit.ApplyNativeYb1103Snapshot -> m_nGameGold = currentYuanbao).
                // A local m_nGameGold deduction here is therefore REFUNDED on the very next refresh, so the
                // player keeps both the 元宝 and the item = free items / dupe. The native 白猪/SeeShop
                // (sub_6CC420) settles 元宝 through the external YBDB chain and its local delivery is a pure
                // grant that NEVER debits m_nGameGold; there is no faithful local write path in this process.
                // Per the "原版没有的就屏蔽/移除" directive: fail closed here — deduct nothing, deliver nothing.
                if (mallItem.CurrencyType == 0)
                {
                    failureCode = -3;
                    errorMsg = "元宝购买暂不可用（元宝由元宝库结算，服务器不在本地扣除）";
                    return false;
                }

                if (!mallItem.IsEnabled || mallItem.Stock == 0
                    || (mallItem.Stock > 0 && mallItem.Stock < quantity))
                {
                    failureCode = -5;
                    errorMsg = "该物品已售空";
                    return false;
                }

                int resolvedItemIndex;
                var stdItem = ResolveStdItem(mallItem, out resolvedItemIndex);
                if (stdItem == null)
                {
                    failureCode = -1;
                    errorMsg = "物品配置错误";
                    return false;
                }

                var grantCountLong = (long)Math.Max(1, mallItem.ItemCount) * quantity;
                if (grantCountLong > Grobal2.MAXBAGITEM
                    || player.m_ItemList.Count + grantCountLong > Grobal2.MAXBAGITEM)
                {
                    failureCode = -4;
                    errorMsg = "背包空间不足，请先清理背包";
                    return false;
                }

                ResetDailyLimitIfNeeded(player);
                var currentLimit = GetCurrentLimit(player, mallItem);
                if (mallItem.LimitType > 0 && mallItem.LimitCount > 0
                    && currentLimit + quantity > mallItem.LimitCount)
                {
                    failureCode = -5;
                    errorMsg = "已达到限购数上限";
                    return false;
                }

                var totalPriceLong = (long)mallItem.Price * quantity;
                if (totalPriceLong < 0 || totalPriceLong > int.MaxValue)
                {
                    failureCode = -1;
                    errorMsg = "商品价格配置错误";
                    return false;
                }
                var totalPrice = (int)totalPriceLong;
                if (mallItem.CurrencyType == 2)
                {
                    failureCode = -3;
                    errorMsg = "灵符账户服务暂不可用";
                    return false;
                }
                if (mallItem.CurrencyType == 4
                    && (mallItem.PaymentVariableGroup <= 0 || mallItem.PaymentVariableIndex <= 0))
                {
                    failureCode = -1;
                    errorMsg = "充值点变量配置错误";
                    return false;
                }
                if (GetCurrencyBalance(player, mallItem) < totalPrice)
                {
                    failureCode = -3;
                    errorMsg = $"{GetCurrencyName(mallItem.CurrencyType)}不足，需要 {totalPrice}";
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
                    if (mallItem.IsBound)
                    {
                        userItem.Bind = 1;
                    }
                    userItems.Add(userItem);
                }

                if (!DeductCurrency(player, mallItem, totalPrice))
                {
                    failureCode = -3;
                    errorMsg = $"{GetCurrencyName(mallItem.CurrencyType)}不足，需要 {totalPrice}";
                    return false;
                }
                foreach (var userItem in userItems)
                {
                    player.m_ItemList.Add(userItem);
                    player.SendAddItem(userItem);
                }

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

        private static int GetCurrencyBalance(TPlayObject player, MallItem item)
        {
            return item.CurrencyType switch
            {
                0 => player.m_nGameGold,
                1 => player.m_nGold,
                2 => 0,
                3 => player.m_nShengWan,
                4 => GetPlayerVariable(player, 'V',
                    item.PaymentVariableGroup, item.PaymentVariableIndex),
                _ => 0
            };
        }

        private static bool DeductCurrency(TPlayObject player, MallItem item, int amount)
        {
            if (amount < 0)
            {
                return false;
            }
            switch (item.CurrencyType)
            {
                case 0:
                    if (player.m_nGameGold < amount) return false;
                    player.m_nGameGold -= amount;
                    player.GameGoldChanged();
                    break;
                case 1:
                    if (player.m_nGold < amount) return false;
                    player.m_nGold -= amount;
                    player.GoldChanged();
                    break;
                case 3:
                    if (player.m_nShengWan < amount) return false;
                    player.SetShengWan(player.m_nShengWan - amount);
                    break;
                case 4:
                    if (item.PaymentVariableGroup <= 0 || item.PaymentVariableIndex <= 0)
                    {
                        return false;
                    }
                    var balance = GetPlayerVariable(player, 'V',
                        item.PaymentVariableGroup, item.PaymentVariableIndex);
                    if (balance < amount) return false;
                    SetPlayerVariable(player, 'V',
                        item.PaymentVariableGroup, item.PaymentVariableIndex,
                        balance - amount);
                    break;
                default:
                    return false;
            }
            return true;
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
        // GetV 的 miss 哨兵 -1（0x6DF1F1 `mov [ebp-4],0xFFFFFFFF`），而存档编码器
        // NativeHumanDataCodec.MergeKeyValues 对盘上已有、内存已无的键补 0 —— 同一个
        // 坐标在重登前后会给出 -1 和 0 两种结果。
        private static void SetPlayerVariable(TPlayObject player, char bank, int group, int index, int value)
        {
            player?.SetScriptVar(bank, group, index, value);
        }

        private static void ResetDailyLimitIfNeeded(TPlayObject player)
        {
            const int dailyGroup = 300;
            const int markerGroup = 302;
            const int markerIndex = 99;
            var today = (int)(DateTime.Now.Ticks / TimeSpan.TicksPerDay);
            if (GetPlayerVariable(player, 'S', markerGroup, markerIndex) == today)
            {
                return;
            }

            var keysToReset = new List<int>();
            foreach (var key in player.m_ScriptSVars.Keys)
            {
                if (key > dailyGroup * 1000 && key < dailyGroup * 1000 + 100)
                {
                    keysToReset.Add(key);
                }
            }
            foreach (var key in keysToReset)
            {
                player.m_ScriptSVars[key] = 0;
            }
            SetPlayerVariable(player, 'S', markerGroup, markerIndex, today);
        }

        private static int GetCurrentLimit(TPlayObject player, MallItem mallItem)
        {
            return mallItem.LimitType switch
            {
                1 => GetPlayerVariable(player, 'S', 300, mallItem.Id),
                2 => GetPlayerVariable(player, 'S', 301, mallItem.Id),
                _ => 0
            };
        }

        public int GetCurrentLimitValue(TPlayObject player, MallItem mallItem)
        {
            if (player == null || mallItem == null)
            {
                return 0;
            }

            ResetDailyLimitIfNeeded(player);
            return GetCurrentLimit(player, mallItem);
        }

        private static void SetCurrentLimit(TPlayObject player, MallItem mallItem, int value)
        {
            if (mallItem.LimitType == 1)
            {
                SetPlayerVariable(player, 'S', 300, mallItem.Id, value);
            }
            else if (mallItem.LimitType == 2)
            {
                SetPlayerVariable(player, 'S', 301, mallItem.Id, value);
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
                        cmd.Parameters.AddWithValue("@currentCredit", GetCurrencyBalance(player, item));
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

        public string GetCategoryName(byte category)
        {
            return _categoryNames.TryGetValue(category, out var name) ? name : $"分类{category}";
        }

        private static string GetCurrencyName(byte currencyType)
        {
            switch (currencyType)
            {
                case 0:
                    return "元宝";
                case 1:
                    return "金币";
                case 2:
                    return "灵符";
                case 3:
                    return "声望";
                case 4:
                    return "充值点";
                default:
                    return "游戏币";
            }
        }
    }
}
