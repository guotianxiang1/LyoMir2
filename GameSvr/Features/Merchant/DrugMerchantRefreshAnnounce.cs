using System;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr.Features.Merchant
{
    /// <summary>
    /// 药品商人刷新公告系统 (Drug Merchant Refresh Announce System)
    ///
    /// MVI架构实现 - 处理药品商人货物刷新时的公告功能
    /// Model-View-Intent pattern for drug merchant refresh announcement
    /// </summary>
    public sealed class DrugMerchantRefreshAnnounce
    {
        #region Model - 数据模型

        /// <summary>
        /// 刷新公告数据模型
        /// </summary>
        public sealed class AnnounceModel
        {
            /// <summary>
            /// 商人NPC名称
            /// </summary>
            public string MerchantName { get; set; }

            /// <summary>
            /// 商人所在地图
            /// </summary>
            public string MapName { get; set; }

            /// <summary>
            /// 刷新的物品列表
            /// </summary>
            public List<string> RefreshedItems { get; set; }

            /// <summary>
            /// 刷新时间戳
            /// </summary>
            public DateTime Timestamp { get; set; }

            /// <summary>
            /// 公告消息内容
            /// </summary>
            public string Message { get; set; }

            /// <summary>
            /// 消息颜色代码
            /// </summary>
            public int ColorCode { get; set; }

            /// <summary>
            /// 是否为稀有物品刷新
            /// </summary>
            public bool IsRareRefresh { get; set; }

            public AnnounceModel()
            {
                MerchantName = string.Empty;
                MapName = string.Empty;
                RefreshedItems = new List<string>();
                Timestamp = DateTime.Now;
                Message = string.Empty;
                ColorCode = 0xFFDB; // Green - 默认绿色
                IsRareRefresh = false;
            }

            public AnnounceModel(string merchantName, string mapName, List<string> refreshedItems, bool isRare = false)
            {
                MerchantName = merchantName ?? string.Empty;
                MapName = mapName ?? string.Empty;
                RefreshedItems = refreshedItems ?? new List<string>();
                Timestamp = DateTime.Now;
                IsRareRefresh = isRare;
                ColorCode = isRare ? 0x38FF : 0xFFDB; // Red for rare, Green for normal
                Message = BuildMessage();
            }

            private string BuildMessage()
            {
                if (string.IsNullOrWhiteSpace(MerchantName))
                {
                    return string.Empty;
                }

                string itemsText = RefreshedItems.Count > 0
                    ? string.Join("、", RefreshedItems)
                    : "新货物";

                string rarePrefix = IsRareRefresh ? "【稀有】" : "";

                if (!string.IsNullOrWhiteSpace(MapName))
                {
                    return $"{rarePrefix}药品商人【{MerchantName}】在【{MapName}】刷新了货物：{itemsText}";
                }
                else
                {
                    return $"{rarePrefix}药品商人【{MerchantName}】刷新了货物：{itemsText}";
                }
            }

            public void UpdateMessage()
            {
                Message = BuildMessage();
            }
        }

        #endregion

        #region Intent - 意图定义

        /// <summary>
        /// 用户意图基类
        /// </summary>
        public abstract class Intent
        {
        }

        /// <summary>
        /// 发送刷新公告意图
        /// </summary>
        public sealed class SendRefreshAnnounceIntent : Intent
        {
            public string MerchantName { get; }
            public string MapName { get; }
            public List<string> RefreshedItems { get; }
            public bool IsRareRefresh { get; }
            public int ColorCode { get; }

            public SendRefreshAnnounceIntent(
                string merchantName,
                string mapName,
                List<string> refreshedItems = null,
                bool isRareRefresh = false,
                int colorCode = 0)
            {
                MerchantName = merchantName;
                MapName = mapName;
                RefreshedItems = refreshedItems ?? new List<string>();
                IsRareRefresh = isRareRefresh;
                ColorCode = colorCode > 0 ? colorCode : (isRareRefresh ? 0x38FF : 0xFFDB);
            }
        }

        /// <summary>
        /// 查询历史刷新记录意图
        /// </summary>
        public sealed class QueryHistoryIntent : Intent
        {
            public int MaxCount { get; }
            public string MerchantNameFilter { get; }

            public QueryHistoryIntent(int maxCount = 10, string merchantNameFilter = null)
            {
                MaxCount = maxCount;
                MerchantNameFilter = merchantNameFilter;
            }
        }

        /// <summary>
        /// 清理历史记录意图
        /// </summary>
        public sealed class ClearHistoryIntent : Intent
        {
        }

        /// <summary>
        /// 查询指定商人最后刷新时间意图
        /// </summary>
        public sealed class QueryLastRefreshIntent : Intent
        {
            public string MerchantName { get; }

            public QueryLastRefreshIntent(string merchantName)
            {
                MerchantName = merchantName;
            }
        }

        #endregion

        #region View - 视图接口

        /// <summary>
        /// 公告视图接口
        /// </summary>
        public interface IAnnounceView
        {
            /// <summary>
            /// 显示刷新公告消息
            /// </summary>
            void ShowRefreshAnnounce(AnnounceModel model);

            /// <summary>
            /// 显示历史记录
            /// </summary>
            void ShowHistory(IReadOnlyList<AnnounceModel> history);

            /// <summary>
            /// 显示最后刷新时间
            /// </summary>
            void ShowLastRefreshTime(string merchantName, DateTime? lastRefresh);

            /// <summary>
            /// 显示错误信息
            /// </summary>
            void ShowError(string error);
        }

        /// <summary>
        /// 默认视图实现 - 使用M2Share输出
        /// </summary>
        private sealed class DefaultView : IAnnounceView
        {
            public void ShowRefreshAnnounce(AnnounceModel model)
            {
                if (model == null)
                {
                    return;
                }

                // 使用战神原生消息系统发送公告
                M2Share.MainOutMessage($"[药品商人] {model.Message}");
            }

            public void ShowHistory(IReadOnlyList<AnnounceModel> history)
            {
                if (history == null || history.Count == 0)
                {
                    M2Share.MainOutMessage("[药品商人] 暂无历史刷新记录");
                    return;
                }

                M2Share.MainOutMessage($"[药品商人] 最近 {history.Count} 条刷新记录:");
                for (int i = 0; i < history.Count; i++)
                {
                    var record = history[i];
                    string rareTag = record.IsRareRefresh ? "[稀有] " : "";
                    M2Share.MainOutMessage($"  [{i + 1}] {rareTag}{record.Timestamp:yyyy-MM-dd HH:mm:ss} - {record.MerchantName}");
                }
            }

            public void ShowLastRefreshTime(string merchantName, DateTime? lastRefresh)
            {
                if (lastRefresh.HasValue)
                {
                    M2Share.MainOutMessage($"[药品商人] {merchantName} 最后刷新时间: {lastRefresh.Value:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    M2Share.MainOutMessage($"[药品商人] {merchantName} 尚未刷新过");
                }
            }

            public void ShowError(string error)
            {
                M2Share.ErrorMessage($"[药品商人错误] {error}");
            }
        }

        #endregion

        #region State Management - 状态管理

        private readonly IAnnounceView _view;
        private readonly List<AnnounceModel> _history;
        private readonly Dictionary<string, DateTime> _lastRefreshTimes;
        private readonly object _lock = new object();
        private const int MaxHistoryCount = 100;

        #endregion

        #region Constructor

        public DrugMerchantRefreshAnnounce() : this(new DefaultView())
        {
        }

        public DrugMerchantRefreshAnnounce(IAnnounceView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _history = new List<AnnounceModel>(MaxHistoryCount);
            _lastRefreshTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region Intent Processing - 意图处理

        /// <summary>
        /// 处理用户意图
        /// </summary>
        public void ProcessIntent(Intent intent)
        {
            if (intent == null)
            {
                _view.ShowError("意图对象为空");
                return;
            }

            switch (intent)
            {
                case SendRefreshAnnounceIntent sendIntent:
                    HandleSendRefreshAnnounce(sendIntent);
                    break;

                case QueryHistoryIntent queryIntent:
                    HandleQueryHistory(queryIntent);
                    break;

                case ClearHistoryIntent _:
                    HandleClearHistory();
                    break;

                case QueryLastRefreshIntent lastRefreshIntent:
                    HandleQueryLastRefresh(lastRefreshIntent);
                    break;

                default:
                    _view.ShowError($"未知的意图类型: {intent.GetType().Name}");
                    break;
            }
        }

        private void HandleSendRefreshAnnounce(SendRefreshAnnounceIntent intent)
        {
            try
            {
                // 验证输入
                if (string.IsNullOrWhiteSpace(intent.MerchantName))
                {
                    _view.ShowError("商人名称不能为空");
                    return;
                }

                // 创建模型
                var model = new AnnounceModel(
                    intent.MerchantName,
                    intent.MapName,
                    intent.RefreshedItems,
                    intent.IsRareRefresh)
                {
                    ColorCode = intent.ColorCode
                };

                // 更新最后刷新时间
                lock (_lock)
                {
                    _lastRefreshTimes[intent.MerchantName] = model.Timestamp;

                    // 添加到历史记录
                    _history.Add(model);
                    if (_history.Count > MaxHistoryCount)
                    {
                        _history.RemoveAt(0);
                    }
                }

                // 显示公告
                _view.ShowRefreshAnnounce(model);

                // 广播到所有在线玩家
                BroadcastToAllPlayers(model);
            }
            catch (Exception ex)
            {
                _view.ShowError($"发送刷新公告失败: {ex.Message}");
            }
        }

        private void HandleQueryHistory(QueryHistoryIntent intent)
        {
            try
            {
                lock (_lock)
                {
                    var filteredHistory = _history;

                    // 如果有商人名称过滤条件
                    if (!string.IsNullOrWhiteSpace(intent.MerchantNameFilter))
                    {
                        filteredHistory = _history.FindAll(h =>
                            h.MerchantName.IndexOf(intent.MerchantNameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    var count = Math.Min(intent.MaxCount, filteredHistory.Count);
                    var recent = new List<AnnounceModel>(count);

                    // 获取最近的N条记录
                    for (int i = filteredHistory.Count - count; i < filteredHistory.Count; i++)
                    {
                        recent.Add(filteredHistory[i]);
                    }

                    _view.ShowHistory(recent);
                }
            }
            catch (Exception ex)
            {
                _view.ShowError($"查询历史失败: {ex.Message}");
            }
        }

        private void HandleClearHistory()
        {
            try
            {
                lock (_lock)
                {
                    var count = _history.Count;
                    _history.Clear();
                    _lastRefreshTimes.Clear();
                    M2Share.MainOutMessage($"[药品商人] 已清理 {count} 条历史记录");
                }
            }
            catch (Exception ex)
            {
                _view.ShowError($"清理历史失败: {ex.Message}");
            }
        }

        private void HandleQueryLastRefresh(QueryLastRefreshIntent intent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(intent.MerchantName))
                {
                    _view.ShowError("商人名称不能为空");
                    return;
                }

                lock (_lock)
                {
                    DateTime? lastRefresh = null;
                    if (_lastRefreshTimes.TryGetValue(intent.MerchantName, out var timestamp))
                    {
                        lastRefresh = timestamp;
                    }

                    _view.ShowLastRefreshTime(intent.MerchantName, lastRefresh);
                }
            }
            catch (Exception ex)
            {
                _view.ShowError($"查询最后刷新时间失败: {ex.Message}");
            }
        }

        #endregion

        #region Broadcasting - 广播功能

        private void BroadcastToAllPlayers(AnnounceModel model)
        {
            try
            {
                // 获取所有在线玩家并发送系统消息
                // 使用战神原生的颜色代码 0xFFDB=Green, 0x38FF=Red, 0xFCFF=Blue
                if (M2Share.UserEngine?.PlayObjects != null)
                {
                    // 根据是否为稀有刷新选择消息颜色
                    MsgColor msgColor = model.IsRareRefresh ? MsgColor.Red : MsgColor.Blue;

                    foreach (var player in M2Share.UserEngine.PlayObjects)
                    {
                        if (player != null && player.m_boGhost == false)
                        {
                            // 发送彩色系统消息
                            player.SysMsg(model.Message, msgColor, MsgType.Notice);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 广播失败不影响主流程，仅记录日志
                M2Share.ErrorMessage($"[药品商人] 广播失败: {ex.Message}");
            }
        }

        #endregion

        #region Public API - 公共接口

        /// <summary>
        /// 发送刷新公告（快捷方法）
        /// </summary>
        public void AnnounceRefresh(
            string merchantName,
            string mapName = null,
            List<string> refreshedItems = null,
            bool isRareRefresh = false,
            int colorCode = 0)
        {
            var intent = new SendRefreshAnnounceIntent(
                merchantName,
                mapName,
                refreshedItems,
                isRareRefresh,
                colorCode);
            ProcessIntent(intent);
        }

        /// <summary>
        /// 查询历史记录（快捷方法）
        /// </summary>
        public void QueryHistory(int maxCount = 10, string merchantNameFilter = null)
        {
            var intent = new QueryHistoryIntent(maxCount, merchantNameFilter);
            ProcessIntent(intent);
        }

        /// <summary>
        /// 清理历史记录（快捷方法）
        /// </summary>
        public void ClearHistory()
        {
            var intent = new ClearHistoryIntent();
            ProcessIntent(intent);
        }

        /// <summary>
        /// 查询指定商人最后刷新时间（快捷方法）
        /// </summary>
        public void QueryLastRefreshTime(string merchantName)
        {
            var intent = new QueryLastRefreshIntent(merchantName);
            ProcessIntent(intent);
        }

        /// <summary>
        /// 获取历史记录数量
        /// </summary>
        public int GetHistoryCount()
        {
            lock (_lock)
            {
                return _history.Count;
            }
        }

        /// <summary>
        /// 获取指定商人最后刷新时间（直接返回）
        /// </summary>
        public DateTime? GetLastRefreshTime(string merchantName)
        {
            if (string.IsNullOrWhiteSpace(merchantName))
            {
                return null;
            }

            lock (_lock)
            {
                if (_lastRefreshTimes.TryGetValue(merchantName, out var timestamp))
                {
                    return timestamp;
                }
                return null;
            }
        }

        #endregion
    }
}
