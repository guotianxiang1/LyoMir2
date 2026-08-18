using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Anti-cheat / GM / ops host runtime bodies (ImageBase 0x00400000).
    /// </summary>
    public static class NativeAntiCheatHostRuntime
    {
        // sub_686BE4 @0x00686BE4 — 30s×3 death log (reason @0x686D14, log type 0xCA via sub_768BE0)
        public const uint RapidDeathLogEa = 0x00686BE4;
        public const int RapidDeathWindowSeconds = 30;
        public const int RapidDeathThreshold = 3;
        public const string RapidDeathLogReason =
            "原因:连续3次间隔上的30秒内死亡";
        public const string RapidDeathLogCategory = "反外挂单机控制";

        // sub_74A518 / sub_75EC50 — bag illegal-item seizure
        public const uint SeizeIllegalItemsEa = 0x0074A518;
        public const uint SeizeIllegalItemsBulkEa = 0x0075EC50;

        // sub_6F36AC @0x006F36AC — NPC monitor list (success @0x6F3740 / fail @0x6F3758)
        public const uint NpcMonitorCheckEa = 0x006F36AC;
        public const string NpcMonitorSuccessMessage = "NPC监控设置成功";
        public const string NpcMonitorFailureMessage = "NPC监控设置失败";

        // sub_62F724 / sub_62F570 — GD gdMsg broadcast + GM whitelist gate
        public const uint GdPlatformMessageEa = 0x0062F724;
        public const uint GdPlatformWhitelistEa = 0x0062F570;
        public const string GdMessageKey = "gdMsg";

        // sub_65699C @0x0065699C — inner-power GD query/set (cmd 100001..100003)
        public const uint InnerPowerGdHandlerEa = 0x0065699C;

        // sub_6AC524 @0x006AC524 — GM send target to black room
        public const uint GmBlackRoomEa = 0x006AC524;
        public const string GmBlackRoomMissingMessage = "关小黑屋的角色:%s 不存在！";

        // sub_6995E4 @0x006995E4 — LogoutQuest GotoLabel executor (TSTDScript vtbl+0x44)
        public const uint LogoutQuestExecutorEa = 0x006995E4;
        public const string LogoutQuestScriptName = "LogoutQuest";

        // sub_6046E0 / sub_604884 — PsTaskList duplicate id + missing file validation
        public const uint TaskListValidateEa = 0x006046E0;
        public const uint TaskConfigLoadEa = 0x00604884;
        public const string TaskListErrorPrefix = "[Error]:";
        public const string TaskListDuplicateMessage = "有的任务编号是重复的！";
        public const string TaskListLoadFailureMessage = "脚本加载失败！";

        private static readonly object MonitorSync = new object();
        private static readonly HashSet<string> NpcMonitorNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly object WhiteListSync = new object();
        private static HashSet<string> GmWhiteList =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, List<long>> RapidDeathTicks =
            new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);

        public static void ReloadGmWhiteList(string envirPath)
        {
            var path = Path.Combine(envirPath ?? string.Empty, "WhiteList.txt");
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var name = line.Trim();
                    if (!string.IsNullOrEmpty(name) && !name.StartsWith(";"))
                        set.Add(name);
                }
            }

            lock (WhiteListSync)
                GmWhiteList = set;
        }

        public static bool IsGmWhiteListed(string accountOrName)
        {
            if (string.IsNullOrEmpty(accountOrName))
                return false;
            lock (WhiteListSync)
                return GmWhiteList.Contains(accountOrName);
        }

        public static bool TrySetNpcMonitor(string npcName, bool enable, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(npcName))
                return false;

            lock (MonitorSync)
            {
                if (enable)
                    NpcMonitorNames.Add(npcName);
                else
                    NpcMonitorNames.Remove(npcName);
            }

            message = enable ? NpcMonitorSuccessMessage : NpcMonitorFailureMessage;
            return enable;
        }

        public static bool IsNpcMonitored(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return false;
            lock (MonitorSync)
                return NpcMonitorNames.Contains(npcName);
        }

        public static void NotifyNpcMonitorCheck(TPlayObject gm, string npcName, bool hit)
        {
            if (gm == null)
                return;
            gm.SysMsg(hit ? NpcMonitorSuccessMessage : NpcMonitorFailureMessage,
                MsgColor.Yellow, MsgType.Hint);
        }

        public static bool TryBroadcastGdMessage(string payload, int repeatCount)
        {
            if (string.IsNullOrWhiteSpace(payload) || repeatCount <= 0)
                return false;

            var text = payload.Trim();
            for (var i = 0; i < repeatCount; i++)
                M2Share.MainOutMessage(GdMessageKey + " " + text);
            return true;
        }

        public static bool TrySendGdMessageToGm(TPlayObject gm, string payload, int repeatCount)
        {
            if (gm == null || string.IsNullOrWhiteSpace(payload) || repeatCount <= 0)
                return false;
            if (!IsGmWhiteListed(gm.m_sUserID) && !IsGmWhiteListed(gm.m_sCharName))
                return false;

            var text = payload.Trim();
            for (var i = 0; i < repeatCount; i++)
                gm.SysMsg(text, MsgColor.Yellow, MsgType.Hint);
            return true;
        }

        public static void RecordRapidDeath(TPlayObject player)
        {
            if (player == null)
                return;
            if (player.m_HeroObject != null && player.m_HeroObject.m_boGhost)
                return;

            var policy = TPlayObject.NativeCheatReportPolicyTier;
            if (policy != 1 && policy != 2)
                return;

            var now = HUtil32.GetTickCount();
            var windowMs = RapidDeathWindowSeconds * 1000;
            List<long> ticks;
            lock (RapidDeathTicks)
            {
                if (!RapidDeathTicks.TryGetValue(player.m_sCharName, out ticks))
                {
                    ticks = new List<long>();
                    RapidDeathTicks[player.m_sCharName] = ticks;
                }

                ticks.Add(now);
                ticks.RemoveAll(t => now - t > windowMs);
                if (ticks.Count < RapidDeathThreshold)
                    return;
                ticks.Clear();
            }

            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var line = stamp + RapidDeathLogReason;
            M2Share.AddGameDataLog(string.Join('\t',
                0xCA,
                player.m_sMapName,
                player.m_nCurrX,
                player.m_nCurrY,
                player.m_sCharName,
                RapidDeathLogCategory,
                line,
                "0"));
        }

        public static bool TryCloseBlackRoom(TPlayObject gm, string targetName,
            string mapName, int x, int y, bool enabled)
        {
            if (gm == null)
                return false;
            if (!enabled)
                return false;

            var target = M2Share.UserEngine?.GetPlayObject(targetName);
            if (target == null)
            {
                gm.SysMsg(string.Format(GmBlackRoomMissingMessage, targetName),
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (string.IsNullOrEmpty(mapName))
                return false;

            var env = M2Share.MapManager?.FindMap(mapName);
            if (env == null)
                return false;

            target.ExecuteNativeUserMove(env, x, y);
            return true;
        }

        public static bool ValidateTaskListDirectory(string envirPath, out int duplicateCount,
            out int missingCount)
        {
            duplicateCount = 0;
            missingCount = 0;
            var root = Path.Combine(envirPath ?? string.Empty, "PsTaskList");
            var configPath = Path.Combine(root, "PsTaskConfig.txt");
            if (!File.Exists(configPath))
                return false;

            var seen = new HashSet<int>();
            foreach (var line in File.ReadAllLines(configPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
                    continue;
                var parts = trimmed.Split(new[] { '\t', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    if (!seen.Add(id))
                    {
                        duplicateCount++;
                        M2Share.MainOutMessage(TaskListErrorPrefix + " " +
                            TaskListDuplicateMessage + " id=" + id.ToString(CultureInfo.InvariantCulture));
                    }
                }

                if (parts.Length > 1)
                {
                    var script = parts[1];
                    if (!script.EndsWith(".pas", StringComparison.OrdinalIgnoreCase))
                        script += ".pas";
                    var scriptPath = Path.Combine(root, script);
                    if (!File.Exists(scriptPath))
                    {
                        missingCount++;
                        M2Share.MainOutMessage(TaskListErrorPrefix + " " +
                            TaskListLoadFailureMessage + " " + scriptPath);
                    }
                }
            }

            return duplicateCount == 0 && missingCount == 0;
        }

        /// <summary>
        /// sub_65699C @0x0065699C — GD inner-power query/set replies (100001..100003).
        /// Full GD packet ingress is not wired; this helper is ready for TGdMsgGMAgent hookup.
        /// </summary>
        public static bool TryReplyInnerPowerGd(TPlayObject player, int commandCode, int value,
            out string reply)
        {
            reply = string.Empty;
            if (player == null)
                return false;

            switch (commandCode)
            {
                case 100001:
                    reply = "内功等级:" + player.m_Abil.Level.ToString(CultureInfo.InvariantCulture);
                    return true;
                case 100002:
                    reply = "内功设置成功，当前状态错误";
                    return true;
                case 100003:
                    reply = "内功设置成功，当前状态错误";
                    return true;
                default:
                    reply = "内功等级重设为:" + value.ToString(CultureInfo.InvariantCulture);
                    return true;
            }
        }
    }
}
