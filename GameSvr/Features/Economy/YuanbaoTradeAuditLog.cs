using System;
using System.IO;
using System.Text;

namespace GameSvr.Features.Economy
{
    /// <summary>
    /// 元宝交易审计日志 (Yuanbao Trade Audit Log)
    ///
    /// 基于逆向笔记的最小可行实现 (MVI)，用于记录所有元宝交易操作的审计追踪。
    ///
    /// 原版地址参考：
    ///   - 0x0063CA4C: 元宝交易行出售审计核心函数
    ///   - 相关数据日志函数: sub_769934 (动作 9), sub_768BE0 (动作 10)
    ///
    /// 审计日志格式：
    ///   时间戳\t操作类型\t地图\tX\tY\t角色名\t目标/物品\t数量\t备注
    /// </summary>
    public class YuanbaoTradeAuditLog
    {
        #region 配置常量

        /// <summary>
        /// 审计日志文件目录路径
        /// 对应原版配置路径: config/元宝系统.ini [Log] Path
        /// </summary>
        private const string AuditLogDirectory = "Logs\\YuanbaoTrade";

        /// <summary>
        /// 审计日志文件名前缀
        /// </summary>
        private const string AuditLogFilePrefix = "YbTrade_";

        /// <summary>
        /// 是否启用详细日志模式
        /// 原版地址: dword[0x7D7038]+3 & 0x40 (详细日志开关位)
        /// </summary>
        private static bool s_verboseLogging = true;

        #endregion

        #region 操作类型常量

        /// <summary>元宝交易：购买</summary>
        public const string OperationType_Buy = "TRADE_BUY";

        /// <summary>元宝交易：出售</summary>
        public const string OperationType_Sell = "TRADE_SELL";

        /// <summary>元宝交易：上架</summary>
        public const string OperationType_Post = "TRADE_POST";

        /// <summary>元宝交易：下架</summary>
        public const string OperationType_Unpost = "TRADE_UNPOST";

        /// <summary>元宝交易：改价</summary>
        public const string OperationType_Reprice = "TRADE_REPRICE";

        /// <summary>元宝交易：取回</summary>
        public const string OperationType_Reclaim = "TRADE_RECLAIM";

        /// <summary>元宝交易：结算</summary>
        public const string OperationType_Settle = "TRADE_SETTLE";

        /// <summary>元宝交易：系统取消</summary>
        public const string OperationType_SystemCancel = "TRADE_SYS_CANCEL";

        #endregion

        #region 私有字段

        private static readonly object s_logLock = new object();
        private static string s_baseDirectory = string.Empty;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化审计日志系统
        /// </summary>
        /// <param name="baseDirectory">服务器基础目录</param>
        public static void Initialize(string baseDirectory)
        {
            s_baseDirectory = baseDirectory ?? string.Empty;

            try
            {
                var logPath = GetLogDirectoryPath();
                if (!Directory.Exists(logPath))
                {
                    Directory.CreateDirectory(logPath);
                }

                M2Share.MainOutMessage($"[元宝交易审计] 日志系统初始化完成: {logPath}");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[元宝交易审计] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置详细日志模式
        /// </summary>
        public static void SetVerboseLogging(bool enabled)
        {
            s_verboseLogging = enabled;
        }

        #endregion

        #region 核心审计方法

        /// <summary>
        /// 记录元宝交易操作
        ///
        /// 对应原版函数: sub_0063CA4C (元宝交易行出售审计)
        /// 调用链: ClientYbConsignment* -> ForwardWrite -> sub_6D3694 -> 审计记录
        /// </summary>
        /// <param name="operationType">操作类型 (OperationType_*)</param>
        /// <param name="charName">角色名</param>
        /// <param name="mapName">地图名</param>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="targetOrItem">目标角色或物品名</param>
        /// <param name="amount">交易数量/金额</param>
        /// <param name="remark">备注信息</param>
        public static void LogTrade(
            string operationType,
            string charName,
            string mapName,
            int x,
            int y,
            string targetOrItem,
            int amount,
            string remark = "")
        {
            if (!s_verboseLogging)
            {
                return;
            }

            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logEntry = BuildLogEntry(timestamp, operationType, charName,
                    mapName, x, y, targetOrItem, amount, remark);

                WriteLogEntry(logEntry);

                // 同时写入游戏数据日志 (对应 M2Share.AddGameDataLog)
                WriteGameDataLog(operationType, charName, mapName, x, y,
                    targetOrItem, amount);
            }
            catch (Exception ex)
            {
                // 审计日志失败不应影响业务流程，仅记录错误
                M2Share.MainOutMessage($"[元宝交易审计] 记录日志失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 记录元宝交易购买操作
        /// 原版地址: 0x63ECD7 call 0x769934 (AddGameDataLog 动作 9)
        /// </summary>
        public static void LogBuy(string charName, string mapName, int x, int y,
            string itemName, int yuanbaoAmount, int itemMakeIndex)
        {
            LogTrade(OperationType_Buy, charName, mapName, x, y,
                $"{itemName}[{itemMakeIndex}]", yuanbaoAmount,
                $"购买物品 消耗元宝:{yuanbaoAmount}");
        }

        /// <summary>
        /// 记录元宝交易出售操作
        /// 原版地址: 0x644507 call 0x768BE0 dx=0x0A (AddGameDataLog 动作 10)
        /// </summary>
        public static void LogSell(string charName, string mapName, int x, int y,
            string itemName, int yuanbaoAmount, int itemMakeIndex)
        {
            LogTrade(OperationType_Sell, charName, mapName, x, y,
                $"{itemName}[{itemMakeIndex}]", yuanbaoAmount,
                $"出售物品 获得元宝:{yuanbaoAmount}");
        }

        /// <summary>
        /// 记录元宝交易上架操作
        /// 原版: CM_1352 worker 0x6F0B84 -> req 0x138
        /// </summary>
        public static void LogPost(string charName, string mapName, int x, int y,
            string itemName, int price, int itemMakeIndex)
        {
            LogTrade(OperationType_Post, charName, mapName, x, y,
                $"{itemName}[{itemMakeIndex}]", price,
                $"上架物品 定价:{price}元宝");
        }

        /// <summary>
        /// 记录元宝交易取回操作
        /// 原版: CM_1359/1360 worker 0x6F1028 -> req 0x13F/0x140
        /// </summary>
        public static void LogReclaim(string charName, string mapName, int x, int y,
            string itemName, int itemMakeIndex, bool isSecured)
        {
            LogTrade(OperationType_Reclaim, charName, mapName, x, y,
                $"{itemName}[{itemMakeIndex}]", 0,
                $"取回物品 安全区:{isSecured}");
        }

        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 构建日志条目
        /// 格式: 时间戳\t操作类型\t角色名\t地图\tX\tY\t目标/物品\t数量\t备注
        /// </summary>
        private static string BuildLogEntry(
            string timestamp,
            string operationType,
            string charName,
            string mapName,
            int x,
            int y,
            string targetOrItem,
            int amount,
            string remark)
        {
            var sb = new StringBuilder();
            sb.Append(timestamp);
            sb.Append('\t');
            sb.Append(operationType ?? "UNKNOWN");
            sb.Append('\t');
            sb.Append(charName ?? "");
            sb.Append('\t');
            sb.Append(mapName ?? "");
            sb.Append('\t');
            sb.Append(x);
            sb.Append('\t');
            sb.Append(y);
            sb.Append('\t');
            sb.Append(targetOrItem ?? "");
            sb.Append('\t');
            sb.Append(amount);
            sb.Append('\t');
            sb.Append(remark ?? "");

            return sb.ToString();
        }

        /// <summary>
        /// 写入日志文件
        /// 按日期轮转文件: YbTrade_20260814.log
        /// </summary>
        private static void WriteLogEntry(string logEntry)
        {
            lock (s_logLock)
            {
                var logFilePath = GetCurrentLogFilePath();

                // 使用 UTF-8 编码写入，避免 GBK 编码问题
                // 参考 memory: [禁止shell参数内嵌中文](no-chinese-in-shell-args.md)
                File.AppendAllText(logFilePath, logEntry + Environment.NewLine,
                    Encoding.UTF8);
            }
        }

        /// <summary>
        /// 写入游戏数据日志 (兼容原版 AddGameDataLog 格式)
        /// 原版格式: action\tmap\tx\ty\tchar\titem\tmakeindex\t1\t0
        /// </summary>
        private static void WriteGameDataLog(
            string operationType,
            string charName,
            string mapName,
            int x,
            int y,
            string targetOrItem,
            int amount)
        {
            // 映射操作类型到原版数据日志动作码
            // 动作 9: 物品交易 (对应 0x769934)
            // 动作 10: 物品寄存相关 (对应 0x768BE0)
            var actionCode = operationType switch
            {
                OperationType_Buy => "9",
                OperationType_Sell => "10",
                OperationType_Post => "9",
                OperationType_Reclaim => "10",
                _ => "9"
            };

            // 格式与原版 AddGameDataLog 保持一致
            var gameDataLog = string.Join('\t',
                actionCode,
                mapName ?? "",
                x.ToString(),
                y.ToString(),
                charName ?? "",
                targetOrItem ?? "",
                amount.ToString(),
                "1",  // 标记为玩家操作
                "0"   // 扩展字段
            );

            M2Share.AddGameDataLog(gameDataLog);
        }

        /// <summary>
        /// 获取日志目录路径
        /// </summary>
        private static string GetLogDirectoryPath()
        {
            if (string.IsNullOrEmpty(s_baseDirectory))
            {
                return AuditLogDirectory;
            }

            return Path.Combine(s_baseDirectory, AuditLogDirectory);
        }

        /// <summary>
        /// 获取当前日志文件路径 (按日期轮转)
        /// </summary>
        private static string GetCurrentLogFilePath()
        {
            var dateStamp = DateTime.Now.ToString("yyyyMMdd");
            var fileName = $"{AuditLogFilePrefix}{dateStamp}.log";
            var logDir = GetLogDirectoryPath();

            return Path.Combine(logDir, fileName);
        }

        #endregion

        #region 查询方法占位

        /// <summary>
        /// [占位] 查询指定角色的交易历史
        ///
        /// TODO: 实现基于日志文件的查询功能
        /// 原版可能通过数据库查询: gamedata.YBDealHis 表
        /// 参考: TPlayObject.NativeYbConsignment.cs 的查询逻辑
        /// </summary>
        /// <param name="charName">角色名</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>交易记录列表</returns>
        public static object QueryTradeHistory(string charName, DateTime startDate, DateTime endDate)
        {
            // TODO: 实现查询逻辑
            throw new NotImplementedException("查询功能待实现");
        }

        /// <summary>
        /// [占位] 生成审计报告
        ///
        /// TODO: 实现审计报告生成功能
        /// 包括交易统计、异常检测等
        /// </summary>
        public static object GenerateAuditReport(DateTime date)
        {
            // TODO: 实现报告生成逻辑
            throw new NotImplementedException("报告生成功能待实现");
        }

        #endregion
    }
}
