using System;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr.Features.Instance
{
    /// <summary>
    /// 卧龙大奖公告系统 (Wolong Grand Prize Announce System)
    ///
    /// MVI架构实现 - 处理卧龙大奖获得时的全服公告功能
    /// Model-View-Intent pattern for grand prize announcement broadcasting
    /// </summary>
    public sealed class WolongGrandPrizeAnnounce
    {
        #region Model - 数据模型

        /// <summary>
        /// 大奖公告数据模型
        /// </summary>
        public sealed class AnnounceModel
        {
            /// <summary>
            /// 玩家角色名
            /// </summary>
            public string PlayerName { get; set; }

            /// <summary>
            /// 获得的物品名称
            /// </summary>
            public string ItemName { get; set; }

            /// <summary>
            /// 奖励池编号 (1-99)
            /// </summary>
            public int PoolNumber { get; set; }

            /// <summary>
            /// 公告时间戳
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

            public AnnounceModel()
            {
                PlayerName = string.Empty;
                ItemName = string.Empty;
                PoolNumber = 0;
                Timestamp = DateTime.Now;
                Message = string.Empty;
                ColorCode = 0xFFDB; // Green - 默认绿色
            }

            public AnnounceModel(string playerName, string itemName, int poolNumber)
            {
                PlayerName = playerName ?? string.Empty;
                ItemName = itemName ?? string.Empty;
                PoolNumber = poolNumber;
                Timestamp = DateTime.Now;
                ColorCode = 0xFFDB; // Green
                Message = BuildMessage();
            }

            private string BuildMessage()
            {
                return $"恭喜【{PlayerName}】在卧龙大奖池{PoolNumber}中获得稀有奖励【{ItemName}】！";
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
        /// 发送公告意图
        /// </summary>
        public sealed class SendAnnounceIntent : Intent
        {
            public string PlayerName { get; }
            public string ItemName { get; }
            public int PoolNumber { get; }
            public int ColorCode { get; }

            public SendAnnounceIntent(string playerName, string itemName, int poolNumber, int colorCode = 0xFFDB)
            {
                PlayerName = playerName;
                ItemName = itemName;
                PoolNumber = poolNumber;
                ColorCode = colorCode;
            }
        }

        /// <summary>
        /// 查询历史公告意图
        /// </summary>
        public sealed class QueryHistoryIntent : Intent
        {
            public int MaxCount { get; }

            public QueryHistoryIntent(int maxCount = 10)
            {
                MaxCount = maxCount;
            }
        }

        /// <summary>
        /// 清理历史记录意图
        /// </summary>
        public sealed class ClearHistoryIntent : Intent
        {
        }

        #endregion

        #region View - 视图接口

        /// <summary>
        /// 公告视图接口
        /// </summary>
        public interface IAnnounceView
        {
            /// <summary>
            /// 显示公告消息
            /// </summary>
            void ShowAnnounce(AnnounceModel model);

            /// <summary>
            /// 显示历史记录
            /// </summary>
            void ShowHistory(IReadOnlyList<AnnounceModel> history);

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
            public void ShowAnnounce(AnnounceModel model)
            {
                if (model == null)
                {
                    return;
                }

                // 使用战神原生消息系统发送全服公告
                M2Share.MainOutMessage($"[卧龙大奖] {model.Message}");
            }

            public void ShowHistory(IReadOnlyList<AnnounceModel> history)
            {
                if (history == null || history.Count == 0)
                {
                    M2Share.MainOutMessage("[卧龙大奖] 暂无历史公告记录");
                    return;
                }

                M2Share.MainOutMessage($"[卧龙大奖] 最近 {history.Count} 条公告:");
                for (int i = 0; i < history.Count; i++)
                {
                    var record = history[i];
                    M2Share.MainOutMessage($"  [{i + 1}] {record.Timestamp:yyyy-MM-dd HH:mm:ss} - {record.Message}");
                }
            }

            public void ShowError(string error)
            {
                M2Share.ErrorMessage($"[卧龙大奖错误] {error}");
            }
        }

        #endregion

        #region State Management - 状态管理

        private readonly IAnnounceView _view;
        private readonly List<AnnounceModel> _history;
        private readonly object _lock = new object();
        private const int MaxHistoryCount = 100;

        #endregion

        #region Constructor

        public WolongGrandPrizeAnnounce() : this(new DefaultView())
        {
        }

        public WolongGrandPrizeAnnounce(IAnnounceView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _history = new List<AnnounceModel>(MaxHistoryCount);
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
                case SendAnnounceIntent sendIntent:
                    HandleSendAnnounce(sendIntent);
                    break;

                case QueryHistoryIntent queryIntent:
                    HandleQueryHistory(queryIntent);
                    break;

                case ClearHistoryIntent _:
                    HandleClearHistory();
                    break;

                default:
                    _view.ShowError($"未知的意图类型: {intent.GetType().Name}");
                    break;
            }
        }

        private void HandleSendAnnounce(SendAnnounceIntent intent)
        {
            try
            {
                // 验证输入
                if (string.IsNullOrWhiteSpace(intent.PlayerName))
                {
                    _view.ShowError("玩家名称不能为空");
                    return;
                }

                if (string.IsNullOrWhiteSpace(intent.ItemName))
                {
                    _view.ShowError("物品名称不能为空");
                    return;
                }

                if (intent.PoolNumber < 1 || intent.PoolNumber > 99)
                {
                    _view.ShowError($"奖励池编号无效: {intent.PoolNumber}，有效范围 1-99");
                    return;
                }

                // 创建模型
                var model = new AnnounceModel(intent.PlayerName, intent.ItemName, intent.PoolNumber)
                {
                    ColorCode = intent.ColorCode
                };

                // 添加到历史记录
                lock (_lock)
                {
                    _history.Add(model);
                    if (_history.Count > MaxHistoryCount)
                    {
                        _history.RemoveAt(0);
                    }
                }

                // 显示公告
                _view.ShowAnnounce(model);

                // 可选：广播到所有在线玩家
                BroadcastToAllPlayers(model);
            }
            catch (Exception ex)
            {
                _view.ShowError($"发送公告失败: {ex.Message}");
            }
        }

        private void HandleQueryHistory(QueryHistoryIntent intent)
        {
            try
            {
                lock (_lock)
                {
                    var count = Math.Min(intent.MaxCount, _history.Count);
                    var recent = new List<AnnounceModel>(count);

                    // 获取最近的N条记录
                    for (int i = _history.Count - count; i < _history.Count; i++)
                    {
                        recent.Add(_history[i]);
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
                    M2Share.MainOutMessage($"[卧龙大奖] 已清理 {count} 条历史记录");
                }
            }
            catch (Exception ex)
            {
                _view.ShowError($"清理历史失败: {ex.Message}");
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
                    foreach (var player in M2Share.UserEngine.PlayObjects)
                    {
                        if (player != null && player.m_boGhost == false)
                        {
                            // 发送彩色系统消息
                            player.SysMsg(model.Message, MsgColor.Blue, MsgType.Notice);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 广播失败不影响主流程，仅记录日志
                M2Share.ErrorMessage($"[卧龙大奖] 广播失败: {ex.Message}");
            }
        }

        #endregion

        #region Public API - 公共接口

        /// <summary>
        /// 发送大奖公告（快捷方法）
        /// </summary>
        public void Announce(string playerName, string itemName, int poolNumber, int colorCode = 0xFFDB)
        {
            var intent = new SendAnnounceIntent(playerName, itemName, poolNumber, colorCode);
            ProcessIntent(intent);
        }

        /// <summary>
        /// 查询历史记录（快捷方法）
        /// </summary>
        public void QueryHistory(int maxCount = 10)
        {
            var intent = new QueryHistoryIntent(maxCount);
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
        /// 获取历史记录数量
        /// </summary>
        public int GetHistoryCount()
        {
            lock (_lock)
            {
                return _history.Count;
            }
        }

        #endregion
    }
}
