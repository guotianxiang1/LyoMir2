using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SystemModule;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeHeroLogicalSnapshot
    {
        private const int ExtraItemOffset = 0x141C;
        private const int ExtraItemCount = 2;

        public NativeHeroLogicalSnapshot(int index, string masterName,
            string heroName, byte[] record, byte[] data, byte[] dynamicData,
            bool isDelete, int heroType, int consignation, int indexJob,
            int level, uint experience, int sex, ushort forceLevel,
            uint forceExperience, ushort sfLevel)
        {
            Index = index;
            MasterName = masterName ?? string.Empty;
            HeroName = heroName ?? string.Empty;
            Record = Clone(record);
            Data = Clone(data);
            DynamicData = Clone(dynamicData);
            IsDelete = isDelete;
            HeroType = heroType;
            Consignation = consignation;
            IndexJob = indexJob;
            Level = level;
            Experience = experience;
            Sex = sex;
            ForceLevel = forceLevel;
            ForceExperience = forceExperience;
            SfLevel = sfLevel;
        }

        public int Index { get; }
        public string MasterName { get; }
        public string HeroName { get; }
        public byte[] Record { get; }
        public byte[] Data { get; }
        public byte[] DynamicData { get; }
        public bool IsDelete { get; }
        public int HeroType { get; }
        public int Consignation { get; }
        public int IndexJob { get; }
        public int Level { get; }
        public uint Experience { get; }
        public int Sex { get; }
        public ushort ForceLevel { get; }
        public uint ForceExperience { get; }
        public ushort SfLevel { get; }

        public NativeHeroLogicalSnapshot CloneSnapshot() =>
            new(Index, MasterName, HeroName, Record, Data, DynamicData,
                IsDelete, HeroType, Consignation, IndexJob, Level,
                Experience, Sex, ForceLevel, ForceExperience, SfLevel);

        public bool TryWithForceLevel(ushort forceLevel,
            out NativeHeroLogicalSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (!NativeHeroBlobCodec.TryApplyIndexForceLevel(
                    Data, forceLevel, out var updatedData, out error))
                return false;
            byte[] updatedRecord;
            if (Record.Length == 0)
                updatedRecord = Array.Empty<byte>();
            else if (!NativeHeroBlobCodec.TryApplyIndexForceLevel(
                         Record, forceLevel, out updatedRecord, out error))
                return false;
            snapshot = new NativeHeroLogicalSnapshot(Index, MasterName,
                HeroName, updatedRecord, updatedData, DynamicData, IsDelete,
                HeroType, Consignation, IndexJob, Level, Experience, Sex,
                forceLevel, ForceExperience, SfLevel);
            return true;
        }

        public NativeHeroLogicalSnapshot WithIndexState(bool isDelete,
            int heroType, int consignation) =>
            new(Index, MasterName, HeroName, Record, Data, DynamicData,
                isDelete, heroType, consignation, IndexJob, Level,
                Experience, Sex, ForceLevel, ForceExperience, SfLevel);

        public bool TryRenameHero(string heroName,
            out NativeHeroLogicalSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (string.IsNullOrEmpty(heroName))
            {
                error = "native hero rename target is empty";
                return false;
            }
            if (!TryRenameRecord(Record, heroName,
                    out var renamedRecord, out error))
                return false;
            if (Data.Length != NativeHeroDbFrameCodec.HeroRecordSize
                && Data.Length != NativeHeroBlobCodec.ThreeHeroRecordSize)
            {
                error = "native hero rename Data has an invalid record count";
                return false;
            }
            var renamedData = new byte[Data.Length];
            for (var offset = 0; offset < Data.Length;
                 offset += NativeHeroDbFrameCodec.HeroRecordSize)
            {
                var source = Data.AsSpan(offset,
                    NativeHeroDbFrameCodec.HeroRecordSize).ToArray();
                if (!TryRenameRecord(source, heroName,
                        out var renamed, out error))
                    return false;
                renamed.CopyTo(renamedData, offset);
            }
            snapshot = new NativeHeroLogicalSnapshot(Index, MasterName,
                heroName, renamedRecord, renamedData, DynamicData, IsDelete,
                HeroType, Consignation, IndexJob, Level, Experience, Sex,
                ForceLevel, ForceExperience, SfLevel);
            return true;
        }

        public bool TryExtractItem(byte selector, int makeIndex,
            out NativeHeroLogicalSnapshot snapshot, out byte[] itemRecord)
        {
            snapshot = null;
            itemRecord = null;
            if (NativeDynamicItemExtractionCodec.TryExtractHero(DynamicData,
                    selector, makeIndex, out var dynamicData, out itemRecord))
            {
                snapshot = new NativeHeroLogicalSnapshot(Index, MasterName,
                    HeroName, Record, Data, dynamicData, IsDelete, HeroType,
                    Consignation, IndexJob, Level, Experience, Sex, ForceLevel,
                    ForceExperience, SfLevel);
                return true;
            }

            if (Data.Length != NativeHeroDbFrameCodec.HeroRecordSize
                && Data.Length != NativeHeroBlobCodec.ThreeHeroRecordSize)
                return false;

            var data = (byte[])Data.Clone();
            for (var recordOffset = 0; recordOffset < data.Length;
                 recordOffset += NativeHeroDbFrameCodec.HeroRecordSize)
            {
                var record = data.AsSpan(recordOffset,
                    NativeHeroDbFrameCodec.HeroRecordSize);
                if (selector != 0
                    && record[NativeHeroDbFrameCodec.JobOffset] + 1
                    != selector)
                    continue;

                var itemOffset = FindFixedItem(record, makeIndex);
                if (itemOffset < 0) continue;
                itemRecord = record.Slice(itemOffset,
                    NativeHeroDbFrameCodec.ItemRecordSize).ToArray();
                record.Slice(itemOffset,
                    NativeHeroDbFrameCodec.ItemRecordSize).Clear();

                var selectedRecord = (byte[])Record.Clone();
                if (selectedRecord.Length
                    == NativeHeroDbFrameCodec.HeroRecordSize)
                {
                    var selectedOffset = FindFixedItem(selectedRecord,
                        makeIndex);
                    if (selectedOffset >= 0
                        && selectedRecord.AsSpan(selectedOffset,
                                NativeHeroDbFrameCodec.ItemRecordSize)
                            .SequenceEqual(itemRecord))
                        selectedRecord.AsSpan(selectedOffset,
                            NativeHeroDbFrameCodec.ItemRecordSize).Clear();
                }

                snapshot = new NativeHeroLogicalSnapshot(Index, MasterName,
                    HeroName, selectedRecord, data, DynamicData, IsDelete,
                    HeroType, Consignation, IndexJob, Level, Experience, Sex,
                    ForceLevel, ForceExperience, SfLevel);
                return true;
            }
            return false;
        }

        public bool TryExtractFixedItem(int makeIndex,
            out NativeHeroLogicalSnapshot snapshot, out byte[] itemRecord) =>
            TryExtractItem(0, makeIndex, out snapshot, out itemRecord);

        private static int FindFixedItem(ReadOnlySpan<byte> record,
            int makeIndex)
        {
            var areas = new[]
            {
                (NativeHeroDbFrameCodec.EquippedItemsOffset,
                    NativeHeroDbFrameCodec.EquippedItemCount),
                (NativeHeroDbFrameCodec.BagItemsOffset,
                    NativeHeroDbFrameCodec.BagItemCount),
                (ExtraItemOffset, ExtraItemCount)
            };
            foreach (var area in areas)
            {
                for (var i = 0; i < area.Item2; i++)
                {
                    var offset = area.Item1
                                 + i * NativeHeroDbFrameCodec.ItemRecordSize;
                    if (BinaryPrimitives.ReadUInt16LittleEndian(
                            record.Slice(offset + 4, 2)) != 0
                        && BinaryPrimitives.ReadInt32LittleEndian(
                            record.Slice(offset, 4)) == makeIndex)
                        return offset;
                }
            }
            return -1;
        }

        private static bool TryRenameRecord(byte[] record, string heroName,
            out byte[] renamed, out string error)
        {
            renamed = null;
            if (record == null
                || record.Length != NativeHeroDbFrameCodec.HeroRecordSize)
            {
                error = "native hero rename record length is invalid";
                return false;
            }
            return NativeHeroDbFrameCodec.TryRenameRecord(record, heroName,
                out renamed, out error);
        }

        private static byte[] Clone(byte[] value) => value == null
            ? Array.Empty<byte>() : (byte[])value.Clone();
    }

    public sealed class NativeHeroLogicalCache
    {
        private readonly object _sync = new();
        private readonly Dictionary<int, NativeHeroLogicalSnapshot> _entries =
            new();

        public bool TryGet(int index, out NativeHeroLogicalSnapshot snapshot)
        {
            lock (_sync)
            {
                if (!_entries.TryGetValue(index, out var value))
                {
                    snapshot = null;
                    return false;
                }
                snapshot = value.CloneSnapshot();
                return true;
            }
        }

        public NativeHeroLogicalSnapshot GetOrLoad(int index,
            Func<NativeHeroLogicalSnapshot> loader)
        {
            if (index <= 0 || loader == null) return null;
            lock (_sync)
            {
                if (_entries.TryGetValue(index, out var current))
                    return current.CloneSnapshot();
                var loaded = loader();
                if (loaded == null || loaded.Index != index) return null;
                _entries[index] = loaded.CloneSnapshot();
                return loaded.CloneSnapshot();
            }
        }

        public NativeHeroLogicalSnapshot ReadOrLoad(int index,
            Func<NativeHeroLogicalSnapshot> loader)
        {
            if (index <= 0 || loader == null) return null;
            lock (_sync)
            {
                if (_entries.TryGetValue(index, out var current))
                    return current.CloneSnapshot();
                var loaded = loader();
                return loaded == null || loaded.Index != index
                    ? null : loaded.CloneSnapshot();
            }
        }

        public void Set(NativeHeroLogicalSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (_sync) _entries[snapshot.Index] = snapshot.CloneSnapshot();
        }

        public bool TryApplyForceLevel(int index, ushort forceLevel,
            out NativeHeroLogicalSnapshot snapshot, out string error)
        {
            lock (_sync)
            {
                snapshot = null;
                error = string.Empty;
                if (!_entries.TryGetValue(index, out var current)) return false;
                if (!current.TryWithForceLevel(forceLevel, out snapshot,
                        out error))
                    return true;
                _entries[index] = snapshot.CloneSnapshot();
                return true;
            }
        }

        public bool TryExtractItem(int index, byte selector, int makeIndex,
            out NativeHeroLogicalSnapshot snapshot, out byte[] itemRecord)
        {
            lock (_sync)
            {
                snapshot = null;
                itemRecord = null;
                if (!_entries.TryGetValue(index, out var current))
                    return false;
                if (!current.TryExtractItem(selector, makeIndex, out snapshot,
                        out itemRecord))
                    return false;
                _entries[index] = snapshot.CloneSnapshot();
                return true;
            }
        }

        public bool TryExtractFixedItem(int index, int makeIndex,
            out NativeHeroLogicalSnapshot snapshot, out byte[] itemRecord) =>
            TryExtractItem(index, 0, makeIndex, out snapshot, out itemRecord);

        public IReadOnlyDictionary<int, NativeHeroLogicalSnapshot> SnapshotAll()
        {
            lock (_sync)
            {
                var result = new Dictionary<int, NativeHeroLogicalSnapshot>(
                    _entries.Count);
                foreach (var pair in _entries)
                    result[pair.Key] = pair.Value.CloneSnapshot();
                return result;
            }
        }

        public void Remove(int index)
        {
            lock (_sync) _entries.Remove(index);
        }
    }
}
