using System.Globalization;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Shared state and core operations for native GM cases 100/101.
    /// The original dispatcher stores one task target globally, so the state
    /// intentionally is not attached to the invoking player.
    /// </summary>
    public static class NativeGmTaskMonsterCommands
    {
        public const uint CoreDoTaskEa = 0x006BFEF8;
        public const uint CoreCallTaskMonEa = 0x006C19D0;
        public const uint TaskArmedGlobalEa = 0x007D62A0;
        public const uint TaskMapGlobalEa = 0x007D5A90;
        public const uint TaskTargetXGlobalEa = 0x007D624C;
        public const uint TaskTargetYGlobalEa = 0x007D5FC0;
        public const int MaxMonsterCount = 500;

        private static readonly object SyncRoot = new object();

        public static bool TaskTargetArmed
        {
            get { lock (SyncRoot) return M2Share.g_boMission; }
        }

        public static string TaskMapName
        {
            get { lock (SyncRoot) return M2Share.g_sMissionMap; }
        }

        public static int TaskTargetX
        {
            get { lock (SyncRoot) return M2Share.g_nMissionX; }
        }

        public static int TaskTargetY
        {
            get { lock (SyncRoot) return M2Share.g_nMissionY; }
        }

        /// <summary>
        /// Native sub_6BFEF8. A missing first argument disarms the global task
        /// target; otherwise the current player's map and parsed X/Y are saved.
        /// </summary>
        public static bool TryArmTaskTarget(TPlayObject player,
            string rawX, string rawY, out int targetX, out int targetY)
        {
            targetX = ParseIntDef(rawX, 1);
            targetY = ParseIntDef(rawY, 1);

            lock (SyncRoot)
            {
                if (string.IsNullOrEmpty(rawX))
                {
                    M2Share.g_boMission = false;
                    M2Share.g_sMissionMap = string.Empty;
                    M2Share.g_nMissionX = 0;
                    M2Share.g_nMissionY = 0;
                    return false;
                }

                M2Share.g_boMission = true;
                var mapName = player?.m_sMapName ?? string.Empty;
                M2Share.g_sMissionMap = mapName.Length <= 15
                    ? mapName
                    : mapName[..15];
                M2Share.g_nMissionX = unchecked((short)targetX);
                M2Share.g_nMissionY = unchecked((short)targetY);
                return true;
            }
        }

        public enum CallTaskMonResult
        {
            NotArmed,
            InvalidArguments,
            Completed,
        }

        public sealed class CallTaskMonOutcome
        {
            public CallTaskMonResult Result { get; init; }
            public string MapName { get; init; } = string.Empty;
            public int X { get; init; }
            public int Y { get; init; }
            public int RequestedCount { get; init; }
            public int CreatedCount { get; init; }
        }

        /// <summary>
        /// Native sub_6C19D0. It resolves the map saved by DoTask, clamps the
        /// requested count to 500, creates each monster through the normal
        /// environment spawn path, and marks successful instances with the
        /// native mission/attack-target fields.
        /// </summary>
        public static CallTaskMonOutcome CallTaskMon(string rawX,
            string rawY, string monsterName, string rawCount)
        {
            bool armed;
            string mapName;
            int attackTargetX;
            int attackTargetY;
            lock (SyncRoot)
            {
                armed = M2Share.g_boMission;
                mapName = M2Share.g_sMissionMap;
                attackTargetX = M2Share.g_nMissionX;
                attackTargetY = M2Share.g_nMissionY;
            }

            if (!armed)
                return new CallTaskMonOutcome
                {
                    Result = CallTaskMonResult.NotArmed,
                    MapName = mapName,
                };

            var count = Math.Min(MaxMonsterCount, ParseIntDef(rawCount, 0));
            var x = ParseIntDef(rawX, 0);
            var y = ParseIntDef(rawY, 0);
            var environment = string.IsNullOrEmpty(mapName)
                ? null
                : M2Share.MapManager?.FindMap(mapName);

            if (environment == null || count <= 0 || x <= 0 || y <= 0 ||
                string.IsNullOrEmpty(monsterName) || M2Share.UserEngine == null)
            {
                return new CallTaskMonOutcome
                {
                    Result = CallTaskMonResult.InvalidArguments,
                    MapName = mapName,
                    X = x,
                    Y = y,
                    RequestedCount = count,
                };
            }

            var created = 0;
            for (var index = 0; index < count; index++)
            {
                var monster = M2Share.UserEngine.RegenMonsterByName(
                    environment, unchecked((short)x), unchecked((short)y),
                    monsterName);
                if (monster == null)
                    continue;

                // Native writes [+0x2E7]=1 and [+0x498]/[+0x49C] from the
                // global DoTask target. These are the existing mission fields
                // consumed by Monster.Run and its specialized subclasses.
                monster.m_boMission = true;
                monster.m_nMissionX = unchecked((short)attackTargetX);
                monster.m_nMissionY = unchecked((short)attackTargetY);
                created++;
            }

            return new CallTaskMonOutcome
            {
                Result = CallTaskMonResult.Completed,
                MapName = mapName,
                X = x,
                Y = y,
                RequestedCount = count,
                CreatedCount = created,
            };
        }

        private static int ParseIntDef(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }
    }
}
