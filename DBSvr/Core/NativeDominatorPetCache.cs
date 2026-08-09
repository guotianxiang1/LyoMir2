using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace DBSvr.Core
{
    public sealed class NativeDominatorPetLoadResult
    {
        public int Result { get; init; }
        public byte[] Data { get; init; }
    }

    public sealed class NativeDominatorPetCache
    {
        private const int ItemOffset = 0x0432;
        private const int ItemCount = 192;

        private sealed class Entry
        {
            public long MasterId;
            public byte[] MasterName = Array.Empty<byte>();
            public byte Level;
            public uint Experience;
            public byte[] Data;
        }

        private readonly object _sync = new();
        private readonly Dictionary<long, Entry> _entries = new();

        public void LoadIndex(IPetService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var loaded = new Dictionary<long, Entry>();
            var lastIndex = 0;
            while (true)
            {
                var previousLastIndex = lastIndex;
                var page = service.GetPetPage(lastIndex, 5000)
                           ?? new List<PetIndexInfo>();
                if (page.Count == 0) break;
                foreach (var item in page)
                {
                    if (item == null) continue;
                    loaded[item.MasterId] = new Entry
                    {
                        MasterId = item.MasterId,
                        MasterName = LegacyGbkText.Encode(item.MasterName),
                        Level = unchecked((byte)item.Level),
                        Experience = unchecked((uint)item.Exp)
                    };
                    if (item.Idx > lastIndex) lastIndex = item.Idx;
                }
                if (lastIndex <= previousLastIndex)
                    throw new InvalidOperationException(
                        "native pet index page did not advance");
            }
            lock (_sync)
            {
                _entries.Clear();
                foreach (var pair in loaded) _entries.Add(pair.Key, pair.Value);
            }
        }

        public int Create(IPetService service, long masterId,
            byte[] masterName)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            masterName ??= Array.Empty<byte>();
            lock (_sync)
            {
                if (_entries.ContainsKey(masterId)) return -2;
                string decoded;
                try { decoded = LegacyGbkText.Decode(masterName); }
                catch (ArgumentException) { return -3; }
                try
                {
                    if (!service.CreatePet(decoded, masterId, 0, 0)) return -3;
                }
                catch { return -3; }
                _entries.Add(masterId, new Entry
                {
                    MasterId = masterId,
                    MasterName = (byte[])masterName.Clone(),
                    Data = NativeDominatorPetProtocol.CreateDefaultData(
                        masterName)
                });
                return 1;
            }
        }

        public NativeDominatorPetLoadResult Load(IPetService service,
            long masterId, byte[] masterName)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            lock (_sync)
            {
                if (!_entries.TryGetValue(masterId, out var entry))
                    return new NativeDominatorPetLoadResult { Result = -2 };
                if (entry.Data == null)
                {
                    try { entry.Data = service.LoadPet(masterId); }
                    catch { entry.Data = null; }
                }
                if (entry.Data == null
                    || entry.Data.Length != NativeDominatorPetProtocol.DataSize)
                    return new NativeDominatorPetLoadResult { Result = -3 };
                entry.Data = NativeDominatorPetProtocol.PrepareData(
                    entry.Data, masterName);
                entry.MasterName = (byte[])(masterName?.Clone()
                                            ?? Array.Empty<byte>());
                return new NativeDominatorPetLoadResult
                {
                    Result = 1,
                    Data = (byte[])entry.Data.Clone()
                };
            }
        }

        public bool Save(IPetService service, long masterId,
            byte[] masterName, byte[] data)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            lock (_sync)
            {
                if (!_entries.TryGetValue(masterId, out var entry)) return false;
                if (entry.Data == null)
                {
                    try { entry.Data = service.LoadPet(masterId); }
                    catch { entry.Data = null; }
                    if (entry.Data == null
                        || entry.Data.Length
                        != NativeDominatorPetProtocol.DataSize)
                        return false;
                }
                if (data == null
                    || data.Length != NativeDominatorPetProtocol.DataSize)
                    return false;
                entry.Data = NativeDominatorPetProtocol.PrepareData(
                    data, masterName);
                entry.MasterName = (byte[])(masterName?.Clone()
                                            ?? Array.Empty<byte>());
                entry.Level = entry.Data[NativeDominatorPetProtocol.DataLevelOffset];
                entry.Experience = BinaryPrimitives.ReadUInt32LittleEndian(
                    entry.Data.AsSpan(
                        NativeDominatorPetProtocol.DataExperienceOffset, 4));
                try
                {
                    var decodedName = LegacyGbkText.Decode(entry.MasterName);
                    _ = service.SavePet(masterId, decodedName, entry.Level,
                        unchecked((int)entry.Experience), entry.Data);
                }
                catch { }
                return true;
            }
        }

        public bool TryExtractItem(IPetService service, long masterId,
            byte[] masterName, int makeIndex, out byte[] itemRecord)
        {
            itemRecord = null;
            if (service == null) throw new ArgumentNullException(nameof(service));
            lock (_sync)
            {
                if (!_entries.TryGetValue(masterId, out var entry))
                    return false;
                if (entry.Data == null)
                {
                    try { entry.Data = service.LoadPet(masterId); }
                    catch { entry.Data = null; }
                }
                if (entry.Data == null
                    || entry.Data.Length != NativeDominatorPetProtocol.DataSize)
                    return false;

                entry.Data = NativeDominatorPetProtocol.PrepareData(
                    entry.Data, masterName);
                entry.MasterName = (byte[])(masterName?.Clone()
                                            ?? Array.Empty<byte>());
                for (var i = 0; i < ItemCount; i++)
                {
                    var offset = ItemOffset
                                 + i * NativeHumanDataCodec.ItemRecordSize;
                    if (BinaryPrimitives.ReadInt32LittleEndian(
                            entry.Data.AsSpan(offset, 4)) != makeIndex)
                        continue;

                    itemRecord = entry.Data.AsSpan(offset,
                        NativeHumanDataCodec.ItemRecordSize).ToArray();
                    entry.Data.AsSpan(offset,
                        NativeHumanDataCodec.ItemRecordSize).Clear();
                    try
                    {
                        var decodedName = LegacyGbkText.Decode(entry.MasterName);
                        _ = service.SavePet(masterId, decodedName, entry.Level,
                            unchecked((int)entry.Experience), entry.Data);
                    }
                    catch { }
                    return true;
                }
                return false;
            }
        }
    }
}
