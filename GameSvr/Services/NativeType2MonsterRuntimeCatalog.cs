using System.Buffers.Binary;
using System.Collections.ObjectModel;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Immutable interpretation of the original M2 0x68-byte monster record.
    /// Manager-owned fields at 0x00..0x03 remain raw until their local tables
    /// have an independently verified mapper.
    /// </summary>
    public sealed class NativeType2MonsterDefinition
    {
        public const int NameCapacity = 15;

        private readonly byte[] _nameBytes;
        private readonly byte[] _nativeRecord;

        private NativeType2MonsterDefinition(byte[] nativeRecord)
        {
            _nativeRecord = nativeRecord;

            var nameLength = nativeRecord[0x04];
            if (nameLength > NameCapacity)
                throw new InvalidDataException("Native monster name exceeds 15 bytes.");

            _nameBytes = nativeRecord.AsSpan(0x05, nameLength).ToArray();
            Name = HUtil32.GbkEncoding.GetString(_nameBytes);

            ManagerId = ReadUInt16(0x00);
            Classification = nativeRecord[0x02];
            ClassificationValue = nativeRecord[0x03];
            Race = nativeRecord[0x14];
            RaceImage = nativeRecord[0x15];
            LifeAttribute = nativeRecord[0x16];
            CoolEye = nativeRecord[0x17];
            Appearance = ReadUInt16(0x18);
            Level = ReadUInt16(0x1A);
            Experience = ReadInt32(0x1C);
            HitPoints = ReadInt32(0x20);
            ManaPoints = ReadInt32(0x24);
            ArmorClass = ReadUInt16(0x28);
            MagicArmorClass = ReadUInt16(0x2A);
            DamageClass = ReadUInt16(0x2C);
            MaximumDamageClass = ReadUInt16(0x2E);
            MagicClass = ReadUInt16(0x30);
            SoulClass = ReadUInt16(0x32);
            Speed = ReadUInt16(0x34);
            Hit = ReadUInt16(0x36);
            WalkSpeed = ReadUInt16(0x38);
            WalkStepWire = ReadUInt16(0x3A);
            WalkWait = ReadUInt16(0x3C);
            AttackSpeed = ReadUInt16(0x3E);
            RuntimeReset = ReadInt32(0x48);
            ScriptMarker = nativeRecord[0x4C];
            ForceValue = ReadInt32(0x50);
            SuperForceExperience = ReadInt32(0x5C);
            SuperForceLevel = ReadInt32(0x60);
            JobFastness = ReadInt32(0x64);
        }

        public string Name { get; }
        public ushort ManagerId { get; }
        public byte Classification { get; }
        public byte ClassificationValue { get; }
        public byte Race { get; }
        public byte RaceImage { get; }
        public byte LifeAttribute { get; }
        public byte CoolEye { get; }
        public ushort Appearance { get; }
        public ushort Level { get; }
        public int Experience { get; }
        public int HitPoints { get; }
        public int ManaPoints { get; }
        public ushort ArmorClass { get; }
        public ushort MagicArmorClass { get; }
        public ushort DamageClass { get; }
        public ushort MaximumDamageClass { get; }
        public ushort MagicClass { get; }
        public ushort SoulClass { get; }
        public ushort Speed { get; }
        public ushort Hit { get; }
        public ushort WalkSpeed { get; }
        public ushort WalkStepWire { get; }
        public byte WalkStep => unchecked((byte)WalkStepWire);
        public ushort WalkWait { get; }
        public ushort AttackSpeed { get; }
        public int RuntimeReset { get; }
        public byte ScriptMarker { get; }
        public int ForceValue { get; }
        public int SuperForceExperience { get; }
        public int SuperForceLevel { get; }
        public int JobFastness { get; }

        public byte[] CopyNameBytes() => (byte[])_nameBytes.Clone();
        public byte[] CopyNativeRecord() => (byte[])_nativeRecord.Clone();

        internal bool NameBytesEqual(ReadOnlySpan<byte> value) =>
            value.SequenceEqual(_nameBytes);

        public TMonInfo CreateTMonInfo()
        {
            return new TMonInfo
            {
                ItemList = null,
                sName = Name,
                btRace = Race,
                btRaceImg = RaceImage,
                wAppr = Appearance,
                wLevel = Level,
                btLifeAttrib = LifeAttribute,
                wCoolEye = CoolEye,
                dwExp = Experience,
                wHP = HitPoints,
                wMP = ManaPoints,
                wAC = ArmorClass,
                wMAC = MagicArmorClass,
                wDC = DamageClass,
                wMaxDC = MaximumDamageClass,
                wMC = MagicClass,
                wSC = SoulClass,
                wSpeed = Speed,
                wHitPoint = Hit,
                wWalkSpeed = WalkSpeed,
                wWalkStep = WalkStep,
                wWalkWait = WalkWait,
                wAttackSpeed = AttackSpeed
            };
        }

        internal static NativeType2MonsterDefinition FromRaw(
            NativeType2MonsterRecord rawRecord)
        {
            if (rawRecord == null)
                throw new ArgumentNullException(nameof(rawRecord));

            var record = rawRecord.CopyNativeFields();
            if (record.Length != NativeType2MonsterSnapshotState.NativeRecordSize)
            {
                throw new InvalidDataException(
                    $"Native monster record must be " +
                    $"{NativeType2MonsterSnapshotState.NativeRecordSize} bytes.");
            }
            return new NativeType2MonsterDefinition(record);
        }

        private ushort ReadUInt16(int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(
                _nativeRecord.AsSpan(offset, sizeof(ushort)));

        private int ReadInt32(int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(
                _nativeRecord.AsSpan(offset, sizeof(int)));
    }

    /// <summary>
    /// Atomically published Type2 103 monster definitions. Publication is
    /// rejected unless a non-empty, valid terminal snapshot has been received.
    /// </summary>
    public sealed class NativeType2MonsterRuntimeCatalog
    {
        private sealed class Publication
        {
            public static readonly Publication Empty = new(
                Array.Empty<NativeType2MonsterDefinition>());

            public Publication(NativeType2MonsterDefinition[] definitions)
            {
                Definitions = Array.AsReadOnly(definitions);
            }

            public ReadOnlyCollection<NativeType2MonsterDefinition> Definitions
                { get; }
        }

        private Publication _publication = Publication.Empty;

        public IReadOnlyList<NativeType2MonsterDefinition> Definitions =>
            Volatile.Read(ref _publication).Definitions;

        public bool Ready => Definitions.Count != 0;

        public void Publish(NativeType2MonsterSnapshotState snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.Completed)
                throw new InvalidDataException(
                    "Native monster snapshot is not complete.");
            if (snapshot.HasInvalidRecord)
                throw new InvalidDataException(
                    "Native monster snapshot contains an invalid record.");
            if (snapshot.Records.Count == 0)
                throw new InvalidDataException(
                    "Native monster snapshot contains no records.");

            var definitions = new NativeType2MonsterDefinition[
                snapshot.Records.Count];
            for (var index = 0; index < definitions.Length; index++)
            {
                definitions[index] = NativeType2MonsterDefinition.FromRaw(
                    snapshot.Records[index]);
            }

            Interlocked.Exchange(ref _publication,
                new Publication(definitions));
        }

        public NativeType2MonsterDefinition FindByName(string name)
        {
            if (name == null) return null;
            var nameBytes = HUtil32.GbkEncoding.GetBytes(name);
            return nameBytes.Length <= NativeType2MonsterDefinition.NameCapacity
                ? FindByNameBytes(nameBytes) : null;
        }

        public NativeType2MonsterDefinition FindByNameBytes(
            ReadOnlySpan<byte> nameBytes)
        {
            if (nameBytes.Length > NativeType2MonsterDefinition.NameCapacity)
                return null;
            var definitions = Volatile.Read(ref _publication).Definitions;
            var index = FindIndex(definitions, nameBytes);
            return index >= 0 ? definitions[index] : null;
        }

        public int FindIndexByName(string name)
        {
            if (name == null) return -1;
            var nameBytes = HUtil32.GbkEncoding.GetBytes(name);
            return nameBytes.Length <= NativeType2MonsterDefinition.NameCapacity
                ? FindIndexByNameBytes(nameBytes)
                : -1;
        }

        public int FindIndexByNameBytes(ReadOnlySpan<byte> nameBytes)
        {
            if (nameBytes.Length > NativeType2MonsterDefinition.NameCapacity)
                return -1;

            var definitions = Volatile.Read(ref _publication).Definitions;
            return FindIndex(definitions, nameBytes);
        }

        private static int FindIndex(
            IReadOnlyList<NativeType2MonsterDefinition> definitions,
            ReadOnlySpan<byte> nameBytes)
        {
            for (var index = 0; index < definitions.Count; index++)
            {
                if (definitions[index].NameBytesEqual(nameBytes))
                    return index;
            }
            return -1;
        }

        public TMonInfo[] CreateMonsterList()
        {
            var definitions = Volatile.Read(ref _publication).Definitions;
            var result = new TMonInfo[definitions.Count];
            for (var index = 0; index < definitions.Count; index++)
                result[index] = definitions[index].CreateTMonInfo();
            return result;
        }
    }
}
