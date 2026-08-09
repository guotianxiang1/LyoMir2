using System.Buffers.Binary;
using System.Collections.ObjectModel;
using SystemModule;

namespace GameSvr.Services
{
    public sealed class NativeType2MagicDefinition
    {
        public const int NameCapacity = 15;

        private readonly byte[] _nameBytes;
        private readonly byte[] _nativeRecord;
        private readonly ReadOnlyCollection<byte> _needLevels;
        private readonly ReadOnlyCollection<int> _levelTraining;

        private NativeType2MagicDefinition(byte[] nativeRecord,
            byte databaseJob)
        {
            _nativeRecord = nativeRecord;
            DatabaseJob = databaseJob;

            var nameLength = Math.Min((int)nativeRecord[0], NameCapacity);
            _nameBytes = nativeRecord.AsSpan(1, nameLength).ToArray();
            Name = HUtil32.GbkEncoding.GetString(_nameBytes);

            MagicId = BinaryPrimitives.ReadUInt16LittleEndian(
                nativeRecord.AsSpan(0x10, 2));
            EffectType = nativeRecord[0x12];
            Effect = nativeRecord[0x13];
            Spell = nativeRecord[0x14];
            Power = nativeRecord[0x15];
            MaxPower = nativeRecord[0x16];
            DefaultSpell = nativeRecord[0x17];
            DefaultPower = nativeRecord[0x18];
            DefaultMaxPower = nativeRecord[0x19];
            TrainingCap = nativeRecord[0x1A];

            var needLevels = nativeRecord.AsSpan(0x1B, 5).ToArray();
            _needLevels = Array.AsReadOnly(needLevels);

            var levelTraining = new int[4];
            for (var index = 0; index < levelTraining.Length; index++)
            {
                levelTraining[index] = BinaryPrimitives.ReadInt32LittleEndian(
                    nativeRecord.AsSpan(0x20 + index * 4, 4));
            }
            _levelTraining = Array.AsReadOnly(levelTraining);

            Delay = BinaryPrimitives.ReadInt32LittleEndian(
                nativeRecord.AsSpan(0x30, 4));
            ColdMilliseconds = BinaryPrimitives.ReadInt32LittleEndian(
                nativeRecord.AsSpan(0x34, 4));
            SpellMilliseconds = BinaryPrimitives.ReadInt32LittleEndian(
                nativeRecord.AsSpan(0x38, 4));
        }

        public string Name { get; }
        public ushort MagicId { get; }
        public byte EffectType { get; }
        public byte Effect { get; }
        public byte Spell { get; }
        public byte Power { get; }
        public byte MaxPower { get; }
        public byte DefaultSpell { get; }
        public byte DefaultPower { get; }
        public byte DefaultMaxPower { get; }
        public byte DatabaseJob { get; }
        public byte TrainingCap { get; }
        public IReadOnlyList<byte> NeedLevels => _needLevels;
        public byte NeedLevel1 => _needLevels[0];
        public byte NeedLevel2 => _needLevels[1];
        public byte NeedLevel3 => _needLevels[2];
        public byte NeedLevel4 => _needLevels[3];
        public byte NeedLevel5 => _needLevels[4];
        public IReadOnlyList<int> LevelTraining => _levelTraining;
        public int LevelTraining1 => _levelTraining[0];
        public int LevelTraining2 => _levelTraining[1];
        public int LevelTraining3 => _levelTraining[2];
        public int LevelTraining4 => _levelTraining[3];
        public int Delay { get; }
        public int ColdMilliseconds { get; }
        public int SpellMilliseconds { get; }

        public byte[] CopyNameBytes() => (byte[])_nameBytes.Clone();
        public byte[] CopyNativeRecord() => (byte[])_nativeRecord.Clone();

        public TMagic CreateTMagic()
        {
            var magic = new TMagic
            {
                wMagicID = MagicId,
                sMagicName = Name,
                btEffectType = EffectType,
                btEffect = Effect,
                wSpell = Spell,
                wPower = Power,
                wMaxPower = MaxPower,
                btDefSpell = DefaultSpell,
                btDefPower = DefaultPower,
                btDefMaxPower = DefaultMaxPower,
                btTrainLv = TrainingCap,
                btJob = DatabaseJob,
                dwDelayTime = Delay,
                NeedLevel5 = NeedLevel5,
                ColdMilliseconds = ColdMilliseconds,
                SpellMilliseconds = SpellMilliseconds,
                sDescr = string.Empty
            };
            for (var index = 0; index < magic.TrainLevel.Length; index++)
                magic.TrainLevel[index] = _needLevels[index];
            for (var index = 0; index < magic.MaxTrain.Length; index++)
                magic.MaxTrain[index] = _levelTraining[index];
            return magic;
        }

        internal static NativeType2MagicDefinition FromRaw(
            NativeType2MagicRawRecord rawRecord)
        {
            if (rawRecord == null)
                throw new ArgumentNullException(nameof(rawRecord));

            var record = rawRecord.CopyRecord();
            if (record.Length != NativeType2MagicSnapshotState.RecordSize)
            {
                throw new InvalidDataException(
                    $"Native magic record must be " +
                    $"{NativeType2MagicSnapshotState.RecordSize} bytes.");
            }
            return new NativeType2MagicDefinition(
                record, rawRecord.DatabaseJob);
        }
    }

    /// <summary>
    /// Typed, immutable publication of the native Type2 101/102 definition
    /// lists. Publish while holding the same synchronization used to consume
    /// the source snapshot so both lists and their completion bits form one
    /// coherent view.
    /// </summary>
    public sealed class NativeType2MagicRuntimeCatalog
    {
        private sealed class Publication
        {
            public static readonly Publication Empty = new(
                Array.Empty<NativeType2MagicDefinition>(),
                Array.Empty<NativeType2MagicDefinition>(), 0);

            public Publication(NativeType2MagicDefinition[] human,
                NativeType2MagicDefinition[] hero, byte completionFlags)
            {
                Human = Array.AsReadOnly(human);
                Hero = Array.AsReadOnly(hero);
                CompletionFlags = completionFlags;
            }

            public IReadOnlyList<NativeType2MagicDefinition> Human { get; }
            public IReadOnlyList<NativeType2MagicDefinition> Hero { get; }
            public byte CompletionFlags { get; }
        }

        private Publication _publication = Publication.Empty;

        public IReadOnlyList<NativeType2MagicDefinition> HumanDefinitions =>
            Volatile.Read(ref _publication).Human;

        public IReadOnlyList<NativeType2MagicDefinition> HeroDefinitions =>
            Volatile.Read(ref _publication).Hero;

        public byte CompletionFlags =>
            Volatile.Read(ref _publication).CompletionFlags;

        public bool HumanCompleted =>
            (CompletionFlags & NativeType2MagicSnapshotState.HumanCompleteFlag)
            != 0;

        public bool HeroCompleted =>
            (CompletionFlags & NativeType2MagicSnapshotState.HeroCompleteFlag)
            != 0;

        public bool Ready => HumanCompleted && HeroCompleted;

        public void Publish(NativeType2MagicSnapshotState snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var human = Decode(snapshot.HumanRecords);
            var hero = Decode(snapshot.HeroRecords);
            Interlocked.Exchange(ref _publication,
                new Publication(human, hero, snapshot.CompletionFlags));
        }

        public NativeType2MagicDefinition FindHumanById(ushort magicId) =>
            FindById(Volatile.Read(ref _publication).Human, magicId);

        public NativeType2MagicDefinition FindHeroById(ushort magicId) =>
            FindById(Volatile.Read(ref _publication).Hero, magicId);

        public NativeType2MagicDefinition FindHumanByName(string name) =>
            FindByName(Volatile.Read(ref _publication).Human, name);

        public NativeType2MagicDefinition FindHeroByName(string name) =>
            FindByName(Volatile.Read(ref _publication).Hero, name);

        public TMagic[] CreateHumanMagicList() =>
            CreateMagicList(Volatile.Read(ref _publication).Human);

        public TMagic[] CreateHeroMagicList() =>
            CreateMagicList(Volatile.Read(ref _publication).Hero);

        private static NativeType2MagicDefinition[] Decode(
            IReadOnlyList<NativeType2MagicRawRecord> records)
        {
            var definitions = new NativeType2MagicDefinition[records.Count];
            for (var index = 0; index < records.Count; index++)
                definitions[index] = NativeType2MagicDefinition.FromRaw(
                    records[index]);
            return definitions;
        }

        private static TMagic[] CreateMagicList(
            IReadOnlyList<NativeType2MagicDefinition> definitions)
        {
            var result = new TMagic[definitions.Count];
            for (var index = 0; index < definitions.Count; index++)
                result[index] = definitions[index].CreateTMagic();
            return result;
        }

        private static NativeType2MagicDefinition FindById(
            IReadOnlyList<NativeType2MagicDefinition> definitions,
            ushort magicId)
        {
            for (var index = 0; index < definitions.Count; index++)
            {
                if (definitions[index].MagicId == magicId)
                    return definitions[index];
            }
            return null;
        }

        private static NativeType2MagicDefinition FindByName(
            IReadOnlyList<NativeType2MagicDefinition> definitions,
            string name)
        {
            if (name == null) return null;
            for (var index = 0; index < definitions.Count; index++)
            {
                if (string.Equals(definitions[index].Name, name,
                        StringComparison.OrdinalIgnoreCase))
                    return definitions[index];
            }
            return null;
        }
    }
}
