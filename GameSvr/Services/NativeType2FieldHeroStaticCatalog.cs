using System.Buffers.Binary;
using System.Collections.ObjectModel;
using SystemModule;

namespace GameSvr.Services
{
    public enum NativeType2FieldHeroEquipmentSlot
    {
        Dress,
        Weapon,
        Medal,
        Necklace,
        Helmet,
        ArmringL,
        ArmringR,
        RingL,
        RingR,
        Bujuk,
        Belt,
        Boots,
        Charm,
        Mask
    }

    public sealed class NativeType2FieldHeroEquipmentDefinition
    {
        private readonly byte[] _nameBytes;

        internal NativeType2FieldHeroEquipmentDefinition(
            NativeType2FieldHeroEquipmentSlot slot, byte[] nameBytes,
            byte reserved, int scatter)
        {
            Slot = slot;
            _nameBytes = nameBytes;
            Name = HUtil32.GbkEncoding.GetString(nameBytes);
            Reserved = reserved;
            Scatter = scatter;
        }

        public NativeType2FieldHeroEquipmentSlot Slot { get; }
        public string Name { get; }
        public bool IsEmpty => _nameBytes.Length == 0;
        public byte Reserved { get; }
        public int Scatter { get; }

        public byte[] CopyNameBytes() => (byte[])_nameBytes.Clone();

        internal bool NameBytesEqual(ReadOnlySpan<byte> value) =>
            value.SequenceEqual(_nameBytes);
    }

    /// <summary>
    /// Strongly typed, immutable interpretation of one 0x13C-byte Type2 0x006C
    /// body. Reserved bytes and the trailing native runtime slot are retained
    /// for evidence only and are never interpreted as managed references.
    /// </summary>
    public sealed class NativeType2FieldHeroDefinition
    {
        public const int NameCapacity = 14;
        public const int EquipmentNameCapacity = 14;
        public const int EquipmentSlotCount = 14;
        public const int EquipmentOffset = 0x20;
        public const int EquipmentStride = 0x14;
        public const int RuntimeSlotOffset = 0x138;

        private readonly byte[] _wireBody;
        private readonly byte[] _nameBytes;
        private readonly byte[] _lookupNameBytes;
        private readonly ReadOnlyCollection<
            NativeType2FieldHeroEquipmentDefinition> _equipment;

        private NativeType2FieldHeroDefinition(byte[] wireBody)
        {
            _wireBody = wireBody;

            var nameLength = wireBody[0x00];
            if (nameLength is 0 or > NameCapacity)
            {
                throw new InvalidDataException(
                    "Native field-hero name must contain 1 to 14 bytes.");
            }

            _nameBytes = wireBody.AsSpan(0x01, nameLength).ToArray();
            _lookupNameBytes = NativeFieldHeroFactoryPreflight
                .CanonicalizeLookupName(_nameBytes);
            Name = HUtil32.GbkEncoding.GetString(_nameBytes);
            Sex = wireBody[0x0F];
            Job = wireBody[0x10];
            Reserved11 = wireBody[0x11];
            Level = ReadUInt16(0x12);
            BossLevel = wireBody[0x14];
            BodyLuck = wireBody[0x15];
            AddHitPoint = wireBody[0x16];
            Reserved17 = wireBody[0x17];
            DrinkDrug = ReadInt32(0x18);
            Experience = ReadInt32(0x1C);

            var equipment = new NativeType2FieldHeroEquipmentDefinition[
                EquipmentSlotCount];
            for (var index = 0; index < equipment.Length; index++)
            {
                var offset = EquipmentOffset + index * EquipmentStride;
                var equipmentNameLength = wireBody[offset];
                if (equipmentNameLength > EquipmentNameCapacity)
                {
                    throw new InvalidDataException(
                        $"Native field-hero equipment slot " +
                        $"{(NativeType2FieldHeroEquipmentSlot)index} " +
                        "exceeds 14 bytes.");
                }

                equipment[index] =
                    new NativeType2FieldHeroEquipmentDefinition(
                        (NativeType2FieldHeroEquipmentSlot)index,
                        wireBody.AsSpan(offset + 1, equipmentNameLength)
                            .ToArray(),
                        wireBody[offset + 0x0F],
                        ReadInt32(offset + 0x10));
            }
            _equipment = Array.AsReadOnly(equipment);
            WireRuntimeSlot = BinaryPrimitives.ReadUInt32LittleEndian(
                wireBody.AsSpan(RuntimeSlotOffset, sizeof(uint)));
        }

        public string Name { get; }
        public byte Sex { get; }
        public byte Job { get; }
        public byte Reserved11 { get; }
        public ushort Level { get; }
        public byte BossLevel { get; }
        public byte BodyLuck { get; }
        public byte AddHitPoint { get; }
        public byte Reserved17 { get; }
        public int DrinkDrug { get; }
        public int Experience { get; }
        public IReadOnlyList<NativeType2FieldHeroEquipmentDefinition>
            Equipment => _equipment;
        public uint WireRuntimeSlot { get; }

        public byte[] CopyNameBytes() => (byte[])_nameBytes.Clone();
        public byte[] CopyWireBody() => (byte[])_wireBody.Clone();

        public static NativeType2FieldHeroDefinition Parse(
            ReadOnlySpan<byte> wireBody)
        {
            if (wireBody.Length != NativeType2FieldHeroSnapshotState.BodySize)
            {
                throw new InvalidDataException(
                    $"Native field-hero body must be exactly " +
                    $"{NativeType2FieldHeroSnapshotState.BodySize} bytes.");
            }
            return new NativeType2FieldHeroDefinition(wireBody.ToArray());
        }

        internal static NativeType2FieldHeroDefinition FromRaw(
            NativeType2FieldHeroRawRecord rawRecord)
        {
            if (rawRecord == null)
                throw new ArgumentNullException(nameof(rawRecord));
            return Parse(rawRecord.CopyWireBody());
        }

        internal bool LookupNameBytesEqual(ReadOnlySpan<byte> value) =>
            value.SequenceEqual(_lookupNameBytes);

        private ushort ReadUInt16(int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(
                _wireBody.AsSpan(offset, sizeof(ushort)));

        private int ReadInt32(int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(
                _wireBody.AsSpan(offset, sizeof(int)));
    }

    /// <summary>
    /// Atomically publishes a complete, immutable FieldHero wire snapshot.
    /// Equipment lookup belongs to actor materialization: native sub_60B154
    /// logs an unresolved slot and continues constructing the actor. This
    /// catalog does not implement the nine actor classes.
    /// </summary>
    public sealed class NativeType2FieldHeroStaticCatalog
    {
        private sealed class Publication
        {
            public static readonly Publication Empty = new(false,
                Array.Empty<NativeType2FieldHeroDefinition>());

            public Publication(bool ready,
                NativeType2FieldHeroDefinition[] definitions)
            {
                Ready = ready;
                Definitions = Array.AsReadOnly(definitions);
            }

            public bool Ready { get; }
            public ReadOnlyCollection<NativeType2FieldHeroDefinition>
                Definitions { get; }
        }

        private readonly object _publishLock = new();
        private Publication _publication = Publication.Empty;

        public bool Ready => Volatile.Read(ref _publication).Ready;
        public int Count => Volatile.Read(ref _publication).Definitions.Count;
        public IReadOnlyList<NativeType2FieldHeroDefinition> Definitions =>
            Volatile.Read(ref _publication).Definitions;

        public void Publish(NativeType2FieldHeroSnapshotState snapshot,
            NativeType2StdItemStaticCatalog standardItems)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (standardItems == null)
                throw new ArgumentNullException(nameof(standardItems));

            lock (_publishLock)
            {
                if (Ready)
                    throw new InvalidOperationException(
                        "Native field-hero catalog is already published.");
                if (!snapshot.Completed)
                    throw new InvalidDataException(
                        "Native field-hero snapshot is not complete.");
                var definitions = new NativeType2FieldHeroDefinition[
                    snapshot.Records.Count];
                for (var index = 0; index < definitions.Length; index++)
                {
                    definitions[index] =
                        NativeType2FieldHeroDefinition.FromRaw(
                            snapshot.Records[index]);
                }

                Interlocked.Exchange(ref _publication,
                    new Publication(true, definitions));
            }
        }

        public NativeType2FieldHeroDefinition FindByName(string name)
        {
            if (name == null) return null;
            var nameBytes = HUtil32.GbkEncoding.GetBytes(name);
            return FindByNameBytes(nameBytes);
        }

        public NativeType2FieldHeroDefinition FindByNameBytes(
            ReadOnlySpan<byte> nameBytes)
        {
            if (nameBytes.Length is 0 or >
                NativeType2FieldHeroDefinition.NameCapacity)
                return null;

            var lookupName = NativeFieldHeroFactoryPreflight
                .CanonicalizeLookupName(nameBytes);
            var definitions = Volatile.Read(ref _publication).Definitions;
            // Native sub_49F128 inserts each value at the bucket head. For
            // duplicate normalized keys, the last loaded record wins lookup.
            for (var index = definitions.Count - 1; index >= 0; index--)
            {
                if (definitions[index].LookupNameBytesEqual(lookupName))
                    return definitions[index];
            }
            return null;
        }

    }
}
