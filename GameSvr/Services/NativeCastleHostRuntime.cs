using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Castle / siege host helpers reversed from flat_image.bin (ImageBase 0x00400000).
    /// </summary>
    public static class NativeCastleHostRuntime
    {
        // sub_643138 @0x00643138 GetCastleDoorState — native GBK literals @0x6431A8/0x6431B8/0x6431C8
        public const uint GetCastleDoorStateEa = 0x00643138;
        public const string DoorStateDestroyed = "已破坏";
        public const string DoorStateOpened = "打开";
        public const string DoorStateClosed = "关闭";

        // NOTE: 0x00643350 (sub_643350) is celebrity Hero.ini persist (+0x5AA level), NOT door state.
        // See NativeCelebrityStatueManager @ sub_643737 key '等级'.
        public const uint CelebrityStatueHeroIniSaveEa = 0x00643350;

        // sub_646BAC @0x00646BAC EngageArcher — coordinate table @0x007D2A8C (10 slots, x/y words)
        public const uint EngageArcherEa = 0x00646BAC;
        public const uint EngageArcherCoordTableEa = 0x007D2A8C;

        private static readonly (short X, short Y)[] ArcherSlotCoordinates =
        {
            (30, 30), (27, 33), (29, 37), (31, 41), (34, 44),
            (38, 46), (41, 49), (45, 51), (48, 47), (51, 43),
        };

        public static string ResolveCastleDoorState(CastleDoor door)
        {
            if (door == null)
                return string.Empty;
            if (door.m_boDeath)
                return DoorStateDestroyed;
            return door.m_boOpened ? DoorStateOpened : DoorStateClosed;
        }

        public static bool TryGetEngageArcherCoordinates(int index, out short x, out short y)
        {
            var slot = index - 1;
            if (slot < 0 || slot >= ArcherSlotCoordinates.Length)
            {
                x = 0;
                y = 0;
                return false;
            }

            (x, y) = ArcherSlotCoordinates[slot];
            return true;
        }
    }
}
