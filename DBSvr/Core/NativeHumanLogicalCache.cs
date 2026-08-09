using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace DBSvr.Core
{
    public enum NativeHumanItemInjectionState
    {
        Success,
        NotLoaded,
        NoSpace,
        SaveRejected
    }

    public enum NativeHumanMasterRelationState
    {
        Success,
        NotLoaded,
        NoMatch,
        SaveRejected
    }

    public enum NativeHumanItemExtractionState
    {
        Success,
        NotLoaded,
        NoMatch,
        SaveRejected
    }

    /// <summary>
    /// Keeps the latest logical native human snapshot while asynchronous saves are pending.
    /// Entries are serialized per character so a secondary mutation cannot reload stale SQL data.
    /// </summary>
    public sealed class NativeHumanLogicalCache
    {
        private const int BlobHeaderSize = 8;
        private const int EquippedOffset = 0x0F68;
        private const int BagOffset = 0x2BF6;
        private const int StorageOffset = 0x52F6;
        private const int ExtraPairOffset = 0x2628;
        private const int ExtraSingleOffset = 0x2B26;
        private readonly ConcurrentDictionary<int, Entry> _entries = new();

        private sealed class Entry
        {
            public readonly object Sync = new();
            public NativeSavePersistenceData Current;
        }

        public NativeSavePersistenceData GetOrLoad(int index,
            Func<NativeSavePersistenceData> loader)
        {
            if (index <= 0 || loader == null) return null;
            var entry = _entries.GetOrAdd(index, _ => new Entry());
            lock (entry.Sync)
            {
                if (entry.Current == null)
                {
                    var loaded = loader();
                    if (!TryNormalizeForCache(loaded, out var normalized))
                        return null;
                    entry.Current = Clone(normalized);
                }
                return Clone(entry.Current);
            }
        }

        public bool TryGet(int index, out NativeSavePersistenceData persistence)
        {
            persistence = null;
            if (!_entries.TryGetValue(index, out var entry)) return false;
            lock (entry.Sync)
            {
                if (entry.Current == null) return false;
                persistence = Clone(entry.Current);
                return true;
            }
        }

        public bool TryStage(int index, NativeSavePersistenceData persistence,
            Func<NativeSavePersistenceData, bool> enqueue)
        {
            if (index <= 0 || !IsValid(persistence) || enqueue == null)
                return false;
            if (!TryNormalizeForCache(persistence, out var normalized))
                return false;
            var entry = _entries.GetOrAdd(index, _ => new Entry());
            lock (entry.Sync)
            {
                var queued = Clone(persistence);
                if (!enqueue(queued)) return false;
                entry.Current = Clone(normalized);
                return true;
            }
        }

        public NativeHumanItemInjectionState TryInjectItem(int index,
            byte[] itemRecord, bool includeStorage,
            Func<NativeSavePersistenceData, bool> enqueue)
        {
            if (itemRecord == null
                || itemRecord.Length != NativeHumanDataCodec.ItemRecordSize
                || enqueue == null)
                return NativeHumanItemInjectionState.NoSpace;
            if (!_entries.TryGetValue(index, out var entry))
                return NativeHumanItemInjectionState.NotLoaded;

            lock (entry.Sync)
            {
                if (!IsValid(entry.Current))
                    return NativeHumanItemInjectionState.NotLoaded;
                var dataBlob = (byte[])entry.Current.DataBlob.Clone();
                var destination = FindEmptyItem(dataBlob, BagOffset,
                    NativeHumanDataCodec.BagItemCount);
                if (destination < 0 && includeStorage)
                    destination = FindEmptyItem(dataBlob, StorageOffset,
                        NativeHumanDataCodec.StorageItemCount);
                if (destination < 0)
                    return NativeHumanItemInjectionState.NoSpace;

                itemRecord.CopyTo(dataBlob, destination);
                var updated = CopyWithData(entry.Current, dataBlob);
                var queued = Clone(updated);
                if (!enqueue(queued))
                    return NativeHumanItemInjectionState.SaveRejected;
                entry.Current = Clone(updated);
                return NativeHumanItemInjectionState.Success;
            }
        }

        public NativeHumanMasterRelationState TrySetMasterName(int index,
            byte[] masterName, Func<NativeSavePersistenceData, bool> enqueue)
        {
            masterName ??= Array.Empty<byte>();
            if (masterName.Length > NativeHumanDataCodec.MasterNameCapacity
                || enqueue == null)
                return NativeHumanMasterRelationState.SaveRejected;
            if (!_entries.TryGetValue(index, out var entry))
                return NativeHumanMasterRelationState.NotLoaded;

            lock (entry.Sync)
            {
                if (!IsValid(entry.Current))
                    return NativeHumanMasterRelationState.NotLoaded;

                var dataBlob = (byte[])entry.Current.DataBlob.Clone();
                var field = dataBlob.AsSpan(BlobHeaderSize
                    + NativeHumanDataCodec.MasterNameOffset,
                    NativeHumanDataCodec.MasterNameCapacity + 1);
                // Native subcmd 7 (master reset, 0x5A8BC0) writes the name through the
                // Delphi ShortString-assign helper 0x4035D8 with cl=0x0F.  That helper
                // (`mov bl,[edx]; cmp cl,bl; jbe; mov ecx,ebx; mov [eax],cl` then a
                // plain move of `len` bytes) writes ONLY the length byte and the
                // characters — it does NOT zero-fill the unused capacity.  Clearing
                // first left a byte-level difference in the persisted record versus
                // native, so the leftover tail bytes are now preserved.
                field[0] = (byte)masterName.Length;
                masterName.AsSpan().CopyTo(field.Slice(1, masterName.Length));

                var updated = CopyWithData(entry.Current, dataBlob);
                // The native path changes memory before it queues persistence.
                entry.Current = Clone(updated);
                return enqueue(Clone(updated))
                    ? NativeHumanMasterRelationState.Success
                    : NativeHumanMasterRelationState.SaveRejected;
            }
        }

        public NativeHumanMasterRelationState TryClearMasterRelation(int index,
            byte[] expectedMasterName,
            Func<NativeSavePersistenceData, bool> enqueue)
        {
            expectedMasterName ??= Array.Empty<byte>();
            if (expectedMasterName.Length
                    > NativeHumanDataCodec.MasterNameCapacity
                || enqueue == null)
                return NativeHumanMasterRelationState.SaveRejected;
            if (!_entries.TryGetValue(index, out var entry))
                return NativeHumanMasterRelationState.NotLoaded;

            lock (entry.Sync)
            {
                if (!IsValid(entry.Current))
                    return NativeHumanMasterRelationState.NotLoaded;

                var dataBlob = (byte[])entry.Current.DataBlob.Clone();
                var field = dataBlob.AsSpan(BlobHeaderSize
                    + NativeHumanDataCodec.MasterNameOffset,
                    NativeHumanDataCodec.MasterNameCapacity + 1);
                var length = field[0];
                if (length > NativeHumanDataCodec.MasterNameCapacity
                    || !NativeMasterRelationProtocol.EqualsAsciiIgnoreCase(
                        field.Slice(1, length), expectedMasterName))
                    return NativeHumanMasterRelationState.NoMatch;

                // Native subcmd 3 (master clear, branch 0x5A89A5) writes exactly three
                // bytes: 0x5A8A2E `mov byte [eax+0x10],0` (the master-slot LENGTH byte
                // only — the 15 character bytes stay), 0x5A8A35 `mov byte [eax+0xdc],0`
                // (boStudent) and 0x5A8A3F `mov byte [eax+0xdf],0` (btStudentOrder).
                // 0xDA (boMaster) and 0xE0 (btStudentCount) are deliberately untouched.
                field[0] = 0;
                dataBlob[BlobHeaderSize + 0xDC] = 0;
                dataBlob[BlobHeaderSize + 0xDF] = 0;
                var updated = CopyWithData(entry.Current, dataBlob);
                entry.Current = Clone(updated);
                return enqueue(Clone(updated))
                    ? NativeHumanMasterRelationState.Success
                    : NativeHumanMasterRelationState.SaveRejected;
            }
        }

        public NativeHumanMasterRelationState TryClearMarriageRelation(
            int index, byte[] expectedSpouseName,
            Func<NativeSavePersistenceData, bool> enqueue)
        {
            expectedSpouseName ??= Array.Empty<byte>();
            if (expectedSpouseName.Length > NativeHumanDataCodec.DearNameCapacity
                || enqueue == null)
                return NativeHumanMasterRelationState.SaveRejected;
            if (!_entries.TryGetValue(index, out var entry))
                return NativeHumanMasterRelationState.NotLoaded;

            lock (entry.Sync)
            {
                if (!IsValid(entry.Current))
                    return NativeHumanMasterRelationState.NotLoaded;

                var dataBlob = (byte[])entry.Current.DataBlob.Clone();
                var field = dataBlob.AsSpan(BlobHeaderSize
                    + NativeHumanDataCodec.DearNameOffset,
                    NativeHumanDataCodec.DearNameCapacity + 1);
                // 战神 DBServer subcmd 0 (marriage clear, sub_5A8750 branch 0x5A8825)
                // has exactly ONE gate: `cmp byte [social_base],0 / je done`
                // (0x5A8844-0x5A8847) — i.e. "is the spouse slot non-empty".  It does
                // NOT compare the stored spouse name against the incoming request
                // name anywhere in the branch.  A reciprocal SequenceEqual here used
                // to reject legitimate offline divorces whenever the stored name and
                // the requested name differed at all (e.g. casing).  Contrast subcmd 3
                // (master clear, 0x5A89D2) which DOES compare, via the
                // case-insensitive helper 0x40AFB0 — that one is modeled elsewhere.
                if (field[0] == 0)
                    return NativeHumanMasterRelationState.NoMatch;

                // Native clears ONLY the length byte: 0x5A88A2 `mov byte [eax],0`
                // with eax = social base.  The 15 character bytes are left in place
                // (and the ShortString-assign helper 0x4035D8 does not zero-fill
                // either), so the saved record keeps the old name bytes behind a
                // zero length.  Clearing all 16 bytes was a real byte-level
                // divergence in the persisted record.
                field[0] = 0;
                dataBlob[BlobHeaderSize
                    + NativeHumanDataCodec.MarriedFlagOffset] = 0;
                var updated = CopyWithData(entry.Current, dataBlob);
                entry.Current = Clone(updated);
                return enqueue(Clone(updated))
                    ? NativeHumanMasterRelationState.Success
                    : NativeHumanMasterRelationState.SaveRejected;
            }
        }

        public NativeHumanItemExtractionState TryExtractItem(int index,
            int makeIndex, Func<NativeSavePersistenceData, bool> enqueue,
            out byte[] itemRecord)
        {
            itemRecord = null;
            if (enqueue == null)
                return NativeHumanItemExtractionState.SaveRejected;
            if (!_entries.TryGetValue(index, out var entry))
                return NativeHumanItemExtractionState.NotLoaded;

            lock (entry.Sync)
            {
                if (!IsValid(entry.Current))
                    return NativeHumanItemExtractionState.NotLoaded;

                var scriptBlob = (byte[])entry.Current.ScriptDataBlob.Clone();
                if (TryGetNativeScriptData(scriptBlob, out var scriptOffset,
                        out var scriptLength)
                    && NativeDynamicItemExtractionCodec.TryExtractHuman(
                        scriptBlob.AsSpan(scriptOffset, scriptLength).ToArray(),
                        makeIndex, out var updatedScript, out itemRecord))
                {
                    updatedScript.CopyTo(scriptBlob, scriptOffset);
                    var dynamicUpdate = CopyWithBlobs(entry.Current,
                        (byte[])entry.Current.DataBlob.Clone(), scriptBlob);
                    entry.Current = Clone(dynamicUpdate);
                    return enqueue(Clone(dynamicUpdate))
                        ? NativeHumanItemExtractionState.Success
                        : NativeHumanItemExtractionState.SaveRejected;
                }

                var dataBlob = (byte[])entry.Current.DataBlob.Clone();
                var source = FindItem(dataBlob, makeIndex,
                    (EquippedOffset, NativeHumanDataCodec.EquippedItemCount),
                    (BagOffset, NativeHumanDataCodec.BagItemCount),
                    (StorageOffset, NativeHumanDataCodec.StorageItemCount),
                    (ExtraPairOffset, 2),
                    (ExtraSingleOffset, 1));
                if (source < 0)
                    return NativeHumanItemExtractionState.NoMatch;

                itemRecord = dataBlob.AsSpan(source,
                    NativeHumanDataCodec.ItemRecordSize).ToArray();
                dataBlob.AsSpan(source,
                    NativeHumanDataCodec.ItemRecordSize).Clear();
                if (TryGetNativeScriptData(scriptBlob, out var sidecarOffset,
                        out var sidecarLength)
                    && NativeDynamicItemExtractionCodec.TryRemoveHumanSidecar(
                        scriptBlob.AsSpan(sidecarOffset, sidecarLength).ToArray(),
                        makeIndex, BinaryPrimitives.ReadUInt16LittleEndian(
                            itemRecord.AsSpan(4, 2)),
                        out var updatedSidecarScript))
                {
                    scriptBlob.AsSpan(sidecarOffset).Clear();
                    BinaryPrimitives.WriteInt32LittleEndian(
                        scriptBlob.AsSpan(4, 4),
                        updatedSidecarScript.Length);
                    updatedSidecarScript.CopyTo(scriptBlob, sidecarOffset);
                }
                var updated = CopyWithBlobs(entry.Current, dataBlob, scriptBlob);

                // Native code clears the logical record before it queues the save.
                entry.Current = Clone(updated);
                return enqueue(Clone(updated))
                    ? NativeHumanItemExtractionState.Success
                    : NativeHumanItemExtractionState.SaveRejected;
            }
        }

        public void Remove(int index) => _entries.TryRemove(index, out _);

        public static bool TryCreatePersistence(string account,
            string characterName, byte[] nativeData, byte[] nativeScriptData,
            out NativeSavePersistenceData persistence, out string error)
        {
            persistence = null;
            error = string.Empty;
            if (nativeData == null
                || nativeData.Length != NativeHumanDataCodec.DataRecordSize)
            {
                error = "native human logical data has an invalid size";
                return false;
            }

            nativeScriptData ??= Array.Empty<byte>();
            var scriptBlobLength = checked(
                (BlobHeaderSize + nativeScriptData.Length + 0xFF) & ~0xFF);
            var dataBlob = new byte[NativeHumanDataCodec.DataSizeMarker];
            BinaryPrimitives.WriteInt32LittleEndian(dataBlob.AsSpan(4, 4),
                NativeHumanDataCodec.DataSizeMarker);
            nativeData.CopyTo(dataBlob, BlobHeaderSize);
            var scriptBlob = new byte[scriptBlobLength];
            BinaryPrimitives.WriteInt32LittleEndian(scriptBlob.AsSpan(4, 4),
                nativeScriptData.Length);
            nativeScriptData.CopyTo(scriptBlob, BlobHeaderSize);

            persistence = new NativeSavePersistenceData
            {
                Account = account ?? string.Empty,
                CharacterName = characterName ?? string.Empty,
                DataBlob = dataBlob,
                ScriptDataBlob = scriptBlob,
                Level = BinaryPrimitives.ReadUInt16LittleEndian(
                    nativeData.AsSpan(0x3C, 2)),
                Experience = BinaryPrimitives.ReadUInt32LittleEndian(
                    nativeData.AsSpan(0x50, 4)),
                Job = nativeData[0x40],
                Sex = nativeData[0x3F],
                ApprenticeNum = BinaryPrimitives.ReadInt32LittleEndian(
                    nativeData.AsSpan(0x174, 4)),
                HeroCardLevel = nativeData[0x16F],
                PlatinaCharacterLevel = nativeData[0x16E],
                SfLevel = BinaryPrimitives.ReadUInt16LittleEndian(
                    nativeData.AsSpan(0x53E, 2))
            };
            return true;
        }

        public static bool TryExtractRaw(NativeSavePersistenceData persistence,
            out byte[] nativeData, out byte[] nativeScriptData)
        {
            nativeData = null;
            nativeScriptData = null;
            if (!IsValid(persistence)) return false;
            nativeData = persistence.DataBlob.AsSpan(BlobHeaderSize,
                NativeHumanDataCodec.DataRecordSize).ToArray();
            if (persistence.ScriptDataBlob.Length == 0)
            {
                nativeScriptData = Array.Empty<byte>();
                return true;
            }
            var scriptLength = BinaryPrimitives.ReadInt32LittleEndian(
                persistence.ScriptDataBlob.AsSpan(4, 4));
            if (scriptLength < 0
                || BlobHeaderSize + scriptLength > persistence.ScriptDataBlob.Length)
            {
                nativeData = null;
                return false;
            }
            nativeScriptData = persistence.ScriptDataBlob.AsSpan(
                BlobHeaderSize, scriptLength).ToArray();
            return true;
        }

        private static int FindEmptyItem(byte[] dataBlob, int rawOffset,
            int count)
        {
            for (var i = 0; i < count; i++)
            {
                var offset = BlobHeaderSize + rawOffset
                             + i * NativeHumanDataCodec.ItemRecordSize;
                if (BinaryPrimitives.ReadUInt16LittleEndian(
                        dataBlob.AsSpan(offset + 4, 2)) == 0)
                    return offset;
            }
            return -1;
        }

        private static int FindItem(byte[] dataBlob, int makeIndex,
            params (int Offset, int Count)[] areas)
        {
            foreach (var area in areas)
            {
                for (var i = 0; i < area.Count; i++)
                {
                    var offset = BlobHeaderSize + area.Offset
                                 + i * NativeHumanDataCodec.ItemRecordSize;
                    if (BinaryPrimitives.ReadInt32LittleEndian(
                            dataBlob.AsSpan(offset, 4)) == makeIndex)
                        return offset;
                }
            }
            return -1;
        }

        private static bool IsValid(NativeSavePersistenceData value) =>
            value?.DataBlob?.Length == NativeHumanDataCodec.DataSizeMarker
            && value.ScriptDataBlob != null
            && (value.ScriptDataBlob.Length == 0
                || value.ScriptDataBlob.Length >= 0x100
                && value.ScriptDataBlob.Length % 0x100 == 0);

        private static bool TryNormalizeForCache(
            NativeSavePersistenceData source,
            out NativeSavePersistenceData normalized)
        {
            normalized = null;
            if (!IsValid(source)) return false;
            if (IsCanonicalCacheEnvelope(source))
            {
                normalized = Clone(source);
                return true;
            }
            if (!NativeHumanDataCodec.TryDecode(source.DataBlob,
                    source.ScriptDataBlob, out var decoded, out _)
                || decoded?.NativeData == null
                || !TryCreatePersistence(source.Account, source.CharacterName,
                    decoded.NativeData, decoded.NativeScriptData,
                    out var canonical, out _))
                return false;
            normalized = CopyWithBlobs(source, canonical.DataBlob,
                canonical.ScriptDataBlob);
            return true;
        }

        private static bool IsCanonicalCacheEnvelope(
            NativeSavePersistenceData value)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(value.DataBlob) != 0)
                return false;
            var marker = BinaryPrimitives.ReadInt32LittleEndian(
                value.DataBlob.AsSpan(4, 4));
            if (marker != NativeHumanDataCodec.DataSizeMarker
                && marker != NativeHumanDataCodec.DataRecordSize)
                return false;
            if (value.ScriptDataBlob.Length == 0) return true;
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    value.ScriptDataBlob) != 0)
                return false;
            var scriptLength = BinaryPrimitives.ReadInt32LittleEndian(
                value.ScriptDataBlob.AsSpan(4, 4));
            return scriptLength >= 0
                   && scriptLength <= value.ScriptDataBlob.Length - BlobHeaderSize;
        }

        private static bool TryGetNativeScriptData(byte[] scriptBlob,
            out int offset, out int length)
        {
            offset = BlobHeaderSize;
            length = 0;
            if (scriptBlob == null || scriptBlob.Length < BlobHeaderSize
                || BinaryPrimitives.ReadUInt32LittleEndian(scriptBlob) != 0)
                return false;
            length = BinaryPrimitives.ReadInt32LittleEndian(
                scriptBlob.AsSpan(4, 4));
            return length > 0 && length <= scriptBlob.Length - BlobHeaderSize;
        }

        private static NativeSavePersistenceData CopyWithData(
            NativeSavePersistenceData source, byte[] dataBlob) =>
            CopyWithBlobs(source, dataBlob,
                (byte[])source.ScriptDataBlob.Clone());

        private static NativeSavePersistenceData CopyWithBlobs(
            NativeSavePersistenceData source, byte[] dataBlob,
            byte[] scriptDataBlob) => new()
        {
            Account = source.Account,
            CharacterName = source.CharacterName,
            DataBlob = dataBlob,
            ScriptDataBlob = scriptDataBlob,
            Level = source.Level,
            Experience = source.Experience,
            Job = source.Job,
            Sex = source.Sex,
            ApprenticeNum = source.ApprenticeNum,
            HeroCardLevel = source.HeroCardLevel,
            PlatinaCharacterLevel = source.PlatinaCharacterLevel,
            SfLevel = source.SfLevel
        };

        private static NativeSavePersistenceData Clone(
            NativeSavePersistenceData source) => new()
        {
            Account = source.Account,
            CharacterName = source.CharacterName,
            DataBlob = (byte[])source.DataBlob.Clone(),
            ScriptDataBlob = (byte[])source.ScriptDataBlob.Clone(),
            Level = source.Level,
            Experience = source.Experience,
            Job = source.Job,
            Sex = source.Sex,
            ApprenticeNum = source.ApprenticeNum,
            HeroCardLevel = source.HeroCardLevel,
            PlatinaCharacterLevel = source.PlatinaCharacterLevel,
            SfLevel = source.SfLevel
        };
    }
}
