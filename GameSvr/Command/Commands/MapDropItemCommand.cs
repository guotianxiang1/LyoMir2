using System.Globalization;
using System.Text;
using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("MapDropItem", "开启、关闭或重载地图爆物配置",
        "open|close|loaddyn 房间名|worddrop|load 地图名", 4)]
    public class MapDropItemCommand : BaseCommond
    {
        private const int EnabledByteOffset = 2;
        private const byte EnabledMask = 0x80;

        [DefaultCommand]
        public void MapDropItem(string[] @params, TPlayObject playObject)
        {
            if (@params == null || @params.Length == 0)
                return;

            var operation = @params[0];
            if (string.Equals(operation, "open", StringComparison.Ordinal) ||
                string.Equals(operation, "close", StringComparison.Ordinal))
            {
                var enabled = string.Equals(operation, "open",
                    StringComparison.Ordinal);
                if (!TrySetEnabled(enabled, out var switchWord, out _))
                    return;

                M2Share.UserEngine?.SendServerGroupMsg(
                    Grobal2.ISM_CS_SERVERSWITCH, 0,
                    switchWord.ToString(CultureInfo.InvariantCulture));
                playObject?.SysMsg(enabled
                        ? " 地图爆物控制 : 开"
                        : " 地图爆物控制 : 关",
                    MsgColor.Red, MsgType.Hint);
                M2Share.ServerSwitches.TryPersist(out _);
                return;
            }

            if (string.Equals(operation, "loaddyn", StringComparison.Ordinal))
            {
                if (@params.Length < 2 || string.IsNullOrEmpty(@params[1]))
                    return;
                playObject?.SysMsg(ReloadDynamicRooms(@params[1]),
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            if (string.Equals(operation, "worddrop", StringComparison.Ordinal))
            {
                playObject?.SysMsg(ReloadWorld(), MsgColor.Red, MsgType.Hint);
                return;
            }

            if (string.Equals(operation, "load", StringComparison.Ordinal))
            {
                if (@params.Length < 2 || string.IsNullOrEmpty(@params[1]))
                    return;
                playObject?.SysMsg(ReloadMap(@params[1]),
                    MsgColor.Red, MsgType.Hint);
            }
        }

        internal static bool TrySetEnabled(bool enabled, out uint switchWord,
            out string error)
        {
            switchWord = 0;
            error = string.Empty;
            var switches = M2Share.ServerSwitches;
            var wasEnabled = switches?.IsBitSet(EnabledByteOffset,
                EnabledMask) == true;
            if (switches == null ||
                !switches.TrySetBit(EnabledByteOffset, EnabledMask, enabled,
                    out switchWord, out error))
            {
                if (string.IsNullOrEmpty(error))
                    error = "ServerSwitch.Bin 不可用，未修改开关。";
                return false;
            }
            if (wasEnabled != enabled)
                NativeMapDropTrackingGeneration.SwitchChanged();
            return true;
        }

        internal static string ReloadMap(string mapName)
        {
            var environment = M2Share.MapManager?.Maps.FirstOrDefault(map =>
                string.Equals(map.sMapName, mapName,
                    StringComparison.OrdinalIgnoreCase));
            if (environment == null)
                return "[" + mapName + "]地图不存在";

            var mapDropLoaded = NativeMapRunPermission.TryLoad(
                M2Share.g_Config?.sEnvirDir, environment,
                M2Share.ServerSwitches?.IsBitSet(EnabledByteOffset,
                    EnabledMask) == true, out _);
            var dropControlLoaded = NativeDropControlLoader.TryLoadMap(
                M2Share.sRootPath, environment.sMapName,
                environment.NativeDropControl, out _);

            return (mapDropLoaded
                       ? "地图爆物加载成功"
                       : "配置文件不存在") +
                   (dropControlLoaded
                       ? " 世界掉落加载成功"
                       : " 世界掉落加载失败");
        }

        internal static string ReloadDynamicRooms(string roomName)
        {
            if (M2Share.DynamicRoomManager == null ||
                !M2Share.DynamicRoomManager.TrySnapshotRooms(roomName,
                    out var environments))
            {
                return string.Empty;
            }

            var result = new StringBuilder();
            foreach (var environment in environments)
            {
                if (environment == null)
                    continue;
                var loaded = NativeDropControlLoader.TryLoadMap(
                    M2Share.sRootPath, roomName,
                    environment.NativeDropControl, out _);
                result.Append("Room");
                result.Append(environment.DynamicRoomIndex.ToString(
                    CultureInfo.InvariantCulture));
                result.Append(loaded
                    ? " 加载新掉落配置成功"
                    : " 加载新掉落配置失败");
            }
            return result.ToString();
        }

        internal static string ReloadWorld()
        {
            return NativeDropControlLoader.TryLoadWorld(M2Share.sRootPath,
                M2Share.NativeWorldDropControl, out _)
                ? " 新世界掉落加载成功"
                : " 新世界掉落加载失败";
        }
    }
}
