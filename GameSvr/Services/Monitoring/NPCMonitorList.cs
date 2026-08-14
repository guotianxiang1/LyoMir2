using System.Collections.Concurrent;
using SystemModule;

namespace GameSvr.Services.Monitoring
{
    // ------------------------------------------------------------------------------------------------
    // NPCMonitorList: MVI implementation of the native NPC monitoring system referenced by the
    // SetMonitor (idx 510) and ViewMonitor (idx 511) GM commands.
    //
    // Evidence (IDA/Hex-Rays over m2full.i64, image base 0x00400000):
    //   Global monitor list: off_7D62A4 @0x007D62A4
    //   SetMonitor core:  sub_79F908 @0x0079F908(charName, monType)
    //   ViewMonitor core: sub_79F5C4 @0x0079F5C4(buf, arg)
    //
    // This is a dormant model providing the exact data structure and operations the native
    // system performs. The monitor tracks NPC activities for debugging and anti-cheat purposes.
    //
    // Contract:
    //   - SetMonitor adds (mode=1) or removes (mode=0) an NPC name from the active monitor set
    //   - ViewMonitor retrieves the current monitor list and/or accumulated logs
    //   - The native system stores NPC names and monitors their script execution/state changes
    //   - Thread-safe for concurrent GM command execution
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Represents a single NPC monitor entry in the native monitor list (off_7D62A4).
    /// </summary>
    public sealed class NpcMonitorEntry
    {
        /// <summary>NPC name being monitored (case-sensitive as stored in native).</summary>
        public string NpcName { get; init; }

        /// <summary>Timestamp when monitoring was enabled for this NPC.</summary>
        public DateTime EnabledAt { get; init; }

        /// <summary>Name of the GM who enabled monitoring.</summary>
        public string EnabledBy { get; init; }

        public NpcMonitorEntry(string npcName, string enabledBy)
        {
            NpcName = npcName ?? string.Empty;
            EnabledAt = DateTime.Now;
            EnabledBy = enabledBy ?? string.Empty;
        }
    }

    /// <summary>
    /// Thread-safe in-memory implementation of the native NPC monitor list (off_7D62A4 @0x007D62A4).
    /// Backing store for SetMonitor/ViewMonitor GM commands (idx 510/511, perm 3).
    /// </summary>
    public sealed class NPCMonitorList
    {
        // Native evidence: off_7D62A4 monitor list, accessed by sub_79F908 and sub_79F5C4
        public const uint NativeListAddress = 0x007D62A4;
        public const uint NativeCoreSetMonitor = 0x0079F908;
        public const uint NativeCoreViewMonitor = 0x0079F5C4;

        private readonly ConcurrentDictionary<string, NpcMonitorEntry> _monitoredNpcs;
        private readonly object _logLock = new object();
        private readonly List<string> _activityLog;
        private const int MaxLogEntries = 1000;

        public NPCMonitorList()
        {
            _monitoredNpcs = new ConcurrentDictionary<string, NpcMonitorEntry>(
                StringComparer.OrdinalIgnoreCase);
            _activityLog = new List<string>();
        }

        /// <summary>
        /// Adds or removes an NPC from the active monitor set.
        /// Native: sub_79F908(charName, monType) where monType: 0=remove, 1=add.
        /// </summary>
        /// <param name="npcName">NPC name (case-insensitive lookup, preserves original case)</param>
        /// <param name="enable">true to monitor (monType=1), false to stop (monType=0)</param>
        /// <param name="requestedBy">GM character name who issued the command</param>
        /// <param name="message">Result message for the GM</param>
        /// <returns>true if operation succeeded</returns>
        public bool TrySetMonitor(string npcName, bool enable, string requestedBy,
            out string message)
        {
            if (string.IsNullOrWhiteSpace(npcName))
            {
                message = "NPC名称不能为空";
                return false;
            }

            var trimmedName = npcName.Trim();

            if (enable)
            {
                var entry = new NpcMonitorEntry(trimmedName, requestedBy ?? "unknown");
                if (_monitoredNpcs.TryAdd(trimmedName, entry))
                {
                    LogActivity($"[SetMonitor] 开始监控 NPC: {trimmedName} (操作者: {requestedBy})");
                    message = $"已启用对 [{trimmedName}] 的监控";
                    return true;
                }
                else
                {
                    message = $"NPC [{trimmedName}] 已在监控列表中";
                    return false;
                }
            }
            else
            {
                if (_monitoredNpcs.TryRemove(trimmedName, out var removed))
                {
                    LogActivity($"[SetMonitor] 停止监控 NPC: {trimmedName} (操作者: {requestedBy})");
                    message = $"已停止对 [{trimmedName}] 的监控";
                    return true;
                }
                else
                {
                    message = $"NPC [{trimmedName}] 不在监控列表中";
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if an NPC is currently being monitored.
        /// </summary>
        public bool IsMonitored(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return false;
            return _monitoredNpcs.ContainsKey(npcName.Trim());
        }

        /// <summary>
        /// Gets the current list of monitored NPCs.
        /// Native: sub_79F5C4(buf, arg) builds view from off_7D62A4.
        /// </summary>
        public string GetMonitorListView()
        {
            var entries = _monitoredNpcs.Values.OrderBy(e => e.EnabledAt).ToList();

            if (entries.Count == 0)
            {
                return "当前没有正在监控的NPC";
            }

            var lines = new List<string>
            {
                $"=== NPC监控列表 (共 {entries.Count} 个) ==="
            };

            foreach (var entry in entries)
            {
                var duration = DateTime.Now - entry.EnabledAt;
                lines.Add($"  [{entry.NpcName}] - 监控时长: {FormatDuration(duration)}, 操作者: {entry.EnabledBy}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Gets recent activity log for a specific NPC or all monitored NPCs.
        /// </summary>
        /// <param name="npcName">Optional NPC name filter; null/empty returns all logs</param>
        /// <param name="maxEntries">Maximum number of log entries to return</param>
        public string GetActivityLog(string npcName = null, int maxEntries = 50)
        {
            lock (_logLock)
            {
                IEnumerable<string> entries = _activityLog;

                if (!string.IsNullOrWhiteSpace(npcName))
                {
                    var filter = npcName.Trim();
                    entries = entries.Where(log => log.Contains(filter,
                        StringComparison.OrdinalIgnoreCase));
                }

                var result = entries.TakeLast(Math.Min(maxEntries, _activityLog.Count)).ToList();

                if (result.Count == 0)
                {
                    return string.IsNullOrWhiteSpace(npcName)
                        ? "暂无活动记录"
                        : $"NPC [{npcName}] 暂无活动记录";
                }

                var header = string.IsNullOrWhiteSpace(npcName)
                    ? $"=== 最近 {result.Count} 条活动记录 ==="
                    : $"=== NPC [{npcName}] 最近 {result.Count} 条活动记录 ===";

                return header + Environment.NewLine + string.Join(Environment.NewLine, result);
            }
        }

        /// <summary>
        /// Logs an NPC activity event (called by monitoring hooks in the engine).
        /// </summary>
        public void LogActivity(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            lock (_logLock)
            {
                _activityLog.Add(timestamped);

                // Trim log if it exceeds max size
                if (_activityLog.Count > MaxLogEntries)
                {
                    _activityLog.RemoveRange(0, _activityLog.Count - MaxLogEntries);
                }
            }
        }

        /// <summary>
        /// Logs NPC script execution (hook point for PAS script system).
        /// </summary>
        public void LogScriptExecution(string npcName, string scriptLabel, string context = null)
        {
            if (!IsMonitored(npcName))
                return;

            var msg = string.IsNullOrWhiteSpace(context)
                ? $"[Script] NPC: {npcName}, Label: {scriptLabel}"
                : $"[Script] NPC: {npcName}, Label: {scriptLabel}, Context: {context}";

            LogActivity(msg);
        }

        /// <summary>
        /// Logs NPC state change (position, visibility, etc).
        /// </summary>
        public void LogStateChange(string npcName, string stateType, string oldValue,
            string newValue)
        {
            if (!IsMonitored(npcName))
                return;

            LogActivity($"[StateChange] NPC: {npcName}, Type: {stateType}, " +
                       $"Old: {oldValue}, New: {newValue}");
        }

        /// <summary>
        /// Logs player interaction with monitored NPC.
        /// </summary>
        public void LogPlayerInteraction(string npcName, string playerName, string actionType)
        {
            if (!IsMonitored(npcName))
                return;

            LogActivity($"[Interaction] Player: {playerName} -> NPC: {npcName}, " +
                       $"Action: {actionType}");
        }

        /// <summary>
        /// Clears all monitor entries and logs (admin operation).
        /// </summary>
        public void ClearAll()
        {
            _monitoredNpcs.Clear();
            lock (_logLock)
            {
                _activityLog.Clear();
            }
        }

        /// <summary>
        /// Gets count of currently monitored NPCs.
        /// </summary>
        public int MonitoredCount => _monitoredNpcs.Count;

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}天{duration.Hours}小时";
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}小时{duration.Minutes}分钟";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}分钟";
            return $"{(int)duration.TotalSeconds}秒";
        }
    }
}
