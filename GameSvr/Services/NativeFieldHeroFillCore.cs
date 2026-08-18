namespace GameSvr.Services
{
    /// <summary>
    /// Dormant orchestration model for TFieldHero.FillDBData
    /// (sub_60B154). The actor storage operations and the 14-slot item loop
    /// remain explicit callbacks; this core closes their proven order and
    /// uses the managed publication binding instead of the native wire
    /// pointer retained at definition+0x138.
    /// </summary>
    public static class NativeFieldHeroFillCore
    {
        public const uint OriginalFunction = 0x0060B154;

        public const int NameOffset = 0x106;
        public const int NameCapacity = 0x0E;
        public const int SexOffset = 0x71;
        public const int BossLevelOffset = 0x686;
        public const int DrinkDrugOffset = 0x688;
        public const int LevelOffset = 0x1FC;
        public const int BodyLuckOffset = 0x685;
        public const int AddHitPointOffset = 0x684;
        public const int ExperienceOffset = 0x240;
        public const int BaseAbilityOffset = 0x1E8;
        public const int WorkingAbilityOffset = 0x264;
        public const int AbilityBlockLength = 0x7C;
        public const int RuntimeDropBindingOffset = 0x474;

        public static void Fill(
            NativeType2FieldHeroMaterialization materialization,
            Action<int, ReadOnlyMemory<byte>, int> writeShortString,
            Action<int, byte> writeByte,
            Action<int, ushort> writeUInt16,
            Action<int, int> writeInt32,
            Action<int, int, int> copyBytes,
            Action<IReadOnlyList<
                NativeType2FieldHeroRuntimeEquipmentBinding>> fillEquipment,
            Action<int, IReadOnlyList<
                NativeFieldHeroRuntimeDropBinding>> bindDropItems)
        {
            ArgumentNullException.ThrowIfNull(materialization);
            ArgumentNullException.ThrowIfNull(writeShortString);
            ArgumentNullException.ThrowIfNull(writeByte);
            ArgumentNullException.ThrowIfNull(writeUInt16);
            ArgumentNullException.ThrowIfNull(writeInt32);
            ArgumentNullException.ThrowIfNull(copyBytes);
            ArgumentNullException.ThrowIfNull(fillEquipment);
            ArgumentNullException.ThrowIfNull(bindDropItems);

            var definition = materialization.Definition;
            writeShortString(NameOffset, definition.CopyNameBytes(),
                NameCapacity);
            writeByte(SexOffset, definition.Sex);
            writeByte(BossLevelOffset, definition.BossLevel);
            writeInt32(DrinkDrugOffset, definition.DrinkDrug);
            writeUInt16(LevelOffset, definition.Level);
            writeByte(BodyLuckOffset, definition.BodyLuck);
            writeByte(AddHitPointOffset, definition.AddHitPoint);
            writeInt32(ExperienceOffset, definition.Experience);
            copyBytes(BaseAbilityOffset, WorkingAbilityOffset,
                AbilityBlockLength);
            fillEquipment(materialization.Equipment);
            bindDropItems(RuntimeDropBindingOffset,
                materialization.DropItems);
        }
    }
}
