using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class UserEngine
    {
        private readonly NativeMonSupport _nativeMonSupport;

        internal NativeMonSupport MonSupport => _nativeMonSupport;

        internal static string ResolveNativeMonSupportConfigPath()
        {
            return NativeMonSupport.ResolveConfigPath(M2Share.sRootPath,
                M2Share.g_Config?.sBaseDir);
        }

        internal void LoadNativeMonSupport()
        {
            _nativeMonSupport.Load(ResolveNativeMonSupportConfigPath());
        }

        internal string ReloadNativeMonSupport()
        {
            return _nativeMonSupport.Reload(
                ResolveNativeMonSupportConfigPath());
        }

        internal string ToggleNativeMonSupport()
        {
            return _nativeMonSupport.Toggle();
        }

        private bool IsNativeMonSupportLocalMap(string mapName)
        {
            return M2Share.MapManager?.GetMapInfo(M2Share.nServerIndex,
                mapName) != null;
        }

        private TBaseObject SpawnNativeMonSupportMonster(string mapName,
            string monsterName, int x, int y, int range)
        {
            var environment = M2Share.MapManager?.GetMapInfo(
                M2Share.nServerIndex, mapName);
            if (environment == null)
                return null;

            // sub_67BDCC resolves the regular monster template before drawing
            // either coordinate. Its missing-template branch is the separate
            // TFieldHero factory, which remains fail-closed until that runtime
            // is production-ready.
            if (!TryGetMonsterInfo(monsterName, out var monsterInfo))
                return null;

            NativeMonSupport.ResolveSpawnCoordinates(x, y, range,
                M2Share.RandomNumber.Random, out var spawnX, out var spawnY);

            var monster = AddBaseObject(environment,
                unchecked((short)spawnX), unchecked((short)spawnY),
                monsterInfo.btRace, monsterName,
                initializeMonsterScript: false, exactPosition: false);
            if (monster == null)
                return null;

            if (!RegisterNativeMagicTowerRuntimeMonster(monster))
            {
                RollbackUnpublishedMonster(monster);
                return null;
            }

            try
            {
                M2Share.PasEngine?.TryInitializeMonsterScript(monster);
            }
            catch (Exception exception)
            {
                LogMonsterSpawnFailure("TMonSupport OnInitialize", exception);
            }
            return monster;
        }

        private static void BroadcastNativeMonSupportNotice(string text)
        {
            M2Share.GateManager?.BroadcastLegacyType18(
                CreateNativeMonSupportNoticePacket(text));
        }

        internal static LegacyGateType18 CreateNativeMonSupportNoticePacket(
            string text)
        {
            return new LegacyGateType18
            {
                FilterUserIndex = 0,
                Recog = 0,
                Ident = 100,
                Param = 0x38FF,
                Tag = 0,
                Series = 0,
                TextBytes = HUtil32.GbkEncoding.GetBytes(text ?? string.Empty)
            };
        }
    }
}
