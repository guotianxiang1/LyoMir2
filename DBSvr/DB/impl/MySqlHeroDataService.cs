using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DBSvr.Core;
using SystemModule;

namespace DBSvr
{
    /// <summary>
    /// 英雄存档 MySQL 实现 (对应 hero_data 表, Data + dynData Blob)。
    /// </summary>
    public class MySqlHeroDataService : IHeroDataService
    {
        private const int RecordSaveLockCount = 256;
        private readonly ConcurrentDictionary<int, int> _quickIndex = new();
        private readonly object[] _recordSaveLocks = CreateRecordSaveLocks();
        private readonly object _threeSlotLock = new();
        private readonly ConcurrentDictionary<int, ushort> _nativeForceLevels = new();

        private sealed class NativeHeroInvalidDataException : Exception
        {
            internal NativeHeroInvalidDataException(string message) : base(message) { }
        }

        public void LoadQuickList()
        {
            _quickIndex.Clear();
            using var conn = OpenConn();
            if (conn == null) return;
            using var cmd = new MySqlCommand("SELECT Idx FROM mir3.hero_data", conn);
            using var dr = cmd.ExecuteReader();
            while (dr.Read()) _quickIndex[dr.GetInt32(0)] = dr.GetInt32(0);
        }

        public int Index(int idx) => _quickIndex.TryGetValue(idx, out int i) ? i : -1;

        public (byte[] data, byte[] dynData) LoadBlob(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return (null, null);
            using var cmd = new MySqlCommand("SELECT Data, dynData FROM mir3.hero_data WHERE Idx=@i", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            using var dr = cmd.ExecuteReader();
            if (!dr.Read()) return (null, null);
            var storedData = dr["Data"] as byte[] ?? Array.Empty<byte>();
            var storedDynamicData = dr["dynData"] as byte[] ?? Array.Empty<byte>();

            if (storedData.Length == 0 && storedDynamicData.Length == 0)
                return (Array.Empty<byte>(), Array.Empty<byte>());
            if (storedData.Length == 0)
            {
                DBShare.MainOutMessage(
                    $"[HeroLoadBlob] REJECT idx={idx}: dynData exists without Data");
                return (null, null);
            }
            if (!NativeHeroBlobCodec.TryDecodeDataBlob(storedData,
                    out var data, out var error))
            {
                DBShare.MainOutMessage($"[HeroLoadBlob] REJECT idx={idx} Data: {error}");
                return (null, null);
            }
            if (!NativeHeroBlobCodec.TryDecodeDynamicBlob(storedDynamicData,
                    out var dynamicData, out error))
            {
                DBShare.MainOutMessage($"[HeroLoadBlob] REJECT idx={idx} dynData: {error}");
                return (null, null);
            }
            if (_nativeForceLevels.TryGetValue(idx, out var forceLevel)
                && !NativeHeroBlobCodec.TryApplyIndexForceLevel(data, forceLevel,
                    out data, out var forceError))
            {
                DBShare.MainOutMessage(
                    $"[HeroLoadBlob] REJECT idx={idx} ForceLv: {forceError}");
                return (null, null);
            }
            return (data, dynamicData);
        }

        public bool SaveBlob(int idx, byte[] data, byte[] dynData = null)
        {
            if (!NativeHeroBlobCodec.TryEncodeDataBlob(data,
                    out var storedData, out var error))
            {
                DBShare.MainOutMessage($"[HeroSaveBlob] REJECT idx={idx} Data: {error}");
                return false;
            }
            if (!NativeHeroBlobCodec.TryEncodeDynamicBlob(dynData,
                    out var storedDynamicData, out error))
            {
                DBShare.MainOutMessage($"[HeroSaveBlob] REJECT idx={idx} dynData: {error}");
                return false;
            }

            lock (_threeSlotLock)
            {
                using var conn = OpenConn();
                if (conn == null) return false;
                using var tx = conn.BeginTransaction();
                try
                {
                    byte[] oldData;
                    byte[] oldDynamicData;
                    using (var lockCmd = new MySqlCommand(
                        "SELECT Data, dynData FROM mir3.hero_data WHERE Idx=@i FOR UPDATE", conn, tx))
                    {
                        lockCmd.Parameters.AddWithValue("@i", idx);
                        using var dr = lockCmd.ExecuteReader();
                        if (!dr.Read())
                            throw new InvalidOperationException("native hero_data row does not exist");
                        oldData = dr["Data"] as byte[] ?? Array.Empty<byte>();
                        oldDynamicData = dr["dynData"] as byte[] ?? Array.Empty<byte>();
                    }

                    if (oldData.Length == 0 && oldDynamicData.Length != 0)
                        throw new InvalidOperationException("existing dynData has no Data record");
                    if (oldData.Length != 0
                        && !NativeHeroBlobCodec.TryDecodeDataBlob(oldData, out _, out error))
                        throw new InvalidOperationException("existing Data is invalid: " + error);
                    if (!NativeHeroBlobCodec.TryDecodeDynamicBlob(
                            oldDynamicData, out _, out error))
                        throw new InvalidOperationException("existing dynData is invalid: " + error);

                    using var update = new MySqlCommand(
                        "UPDATE mir3.hero_data SET Data=@d, dynData=@dd WHERE Idx=@i", conn, tx);
                    update.Parameters.Add("@d", MySqlDbType.Blob).Value = storedData;
                    update.Parameters.Add("@dd", MySqlDbType.Blob).Value = storedDynamicData;
                    update.Parameters.AddWithValue("@i", idx);
                    update.ExecuteNonQuery();
                    tx.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    DBShare.MainOutMessage($"[HeroSaveBlob] REJECT idx={idx}: {ex.Message}");
                    return false;
                }
            }
        }

        public bool SaveRecord(int idx, byte[] record, byte[] dynData = null,
            bool setConsignation = false, bool setDelete = false)
            => SaveRecordCore(idx, record, null, dynData, false, false, 0, 0, 0,
                setConsignation, setDelete, null) == NativeHeroSaveResult.Success;

        public NativeHeroSaveResult SaveRecordDetailed(int idx, byte[] record,
            byte[] preparedData, byte[] dynData, bool isDelete, int heroType,
            int consignation, int indexJob, ushort? forceLevelOverride,
            bool exactPrepared = false)
            => SaveRecordCore(idx, record, preparedData, dynData, true, isDelete,
                heroType, consignation, indexJob, false, false,
                forceLevelOverride, exactPrepared);

        private NativeHeroSaveResult SaveRecordCore(int idx, byte[] record,
            byte[] preparedData, byte[] dynData, bool absoluteState,
            bool isDelete, int heroType, int consignation, int indexJob,
            bool setConsignation, bool setDelete, ushort? forceLevelOverride,
            bool exactPrepared = false)
        {
            dynData ??= Array.Empty<byte>();
            if (!NativeHeroDbFrameCodec.TryCreateRecord(record, out var heroRecord, out var error))
            {
                DBShare.MainOutMessage($"[HeroSaveRecord] REJECT idx={idx}: {error}");
                return NativeHeroSaveResult.InvalidData;
            }
            if (!NativeHeroBlobCodec.TryEncodeDynamicBlob(dynData,
                    out var storedDynamicData, out error))
            {
                DBShare.MainOutMessage($"[HeroSaveRecord] REJECT idx={idx} dynData: {error}");
                return NativeHeroSaveResult.InvalidData;
            }
            if (exactPrepared && preparedData == null)
            {
                DBShare.MainOutMessage(
                    $"[HeroSaveRecord] REJECT idx={idx}: exact Data is missing");
                return NativeHeroSaveResult.InvalidData;
            }
            if (!exactPrepared && forceLevelOverride.HasValue
                && preparedData != null
                && !NativeHeroBlobCodec.TryApplyIndexForceLevel(preparedData,
                    forceLevelOverride.Value, out preparedData, out error))
            {
                DBShare.MainOutMessage(
                    $"[HeroSaveRecord] REJECT idx={idx} ForceLv: {error}");
                return NativeHeroSaveResult.InvalidData;
            }
            byte[] preparedStoredData = null;
            if (preparedData != null
                && !NativeHeroBlobCodec.TryEncodeDataBlob(preparedData,
                    out preparedStoredData, out error))
            {
                DBShare.MainOutMessage(
                    $"[HeroSaveRecord] REJECT idx={idx} prepared Data: {error}");
                return NativeHeroSaveResult.InvalidData;
            }

            var saveLock = _recordSaveLocks[(idx & int.MaxValue) % _recordSaveLocks.Length];
            lock (_threeSlotLock)
            lock (saveLock)
            {
                using var conn = OpenConn();
                if (conn == null) return NativeHeroSaveResult.RetryableFailure;
                using var tx = conn.BeginTransaction();
                try
                {
                    int currentIndexJob;
                    using (var currentIndex = new MySqlCommand(
                               "SELECT Job FROM mir3.hero_index WHERE idx=@i LIMIT 1",
                               conn, tx))
                    {
                        currentIndex.Parameters.AddWithValue("@i", idx);
                        var value = currentIndex.ExecuteScalar();
                        if (value == null || value == DBNull.Value)
                            throw new InvalidOperationException(
                                "native hero_index row does not exist");
                        currentIndexJob = Convert.ToInt32(value);
                    }
                    var storedData = preparedStoredData;
                    if (!exactPrepared && currentIndexJob == byte.MaxValue
                        && preparedData?.Length
                        != NativeHeroBlobCodec.ThreeHeroRecordSize)
                        storedData = null;
                    if (storedData == null)
                    {
                        byte[] oldData;
                        byte[] oldDynamicData;
                        bool requireThreeRecords;
                        using (var lockCmd = new MySqlCommand(
                            @"SELECT d.Data, d.dynData, h.Job AS IndexJob
                              FROM mir3.hero_data AS d
                              JOIN mir3.hero_index AS h ON h.idx=d.Idx
                              WHERE d.Idx=@i FOR UPDATE", conn, tx))
                        {
                            lockCmd.Parameters.AddWithValue("@i", idx);
                            using var dr = lockCmd.ExecuteReader();
                            if (!dr.Read())
                                throw new InvalidOperationException(
                                    "native hero_data row does not exist");
                            var storedOldData = dr["Data"] as byte[]
                                                ?? Array.Empty<byte>();
                            oldDynamicData = dr["dynData"] as byte[]
                                             ?? Array.Empty<byte>();
                            requireThreeRecords =
                                Convert.ToInt32(dr["IndexJob"]) == byte.MaxValue;
                            if (storedOldData.Length == 0)
                                oldData = Array.Empty<byte>();
                            else if (!NativeHeroBlobCodec.TryDecodeDataBlob(
                                         storedOldData, out oldData, out error))
                                throw new NativeHeroInvalidDataException(
                                    "existing Data is invalid: " + error);
                        }

                        if (oldData.Length == 0 && oldDynamicData.Length != 0)
                            throw new NativeHeroInvalidDataException(
                                "existing dynData has no Data record");
                        if (!NativeHeroBlobCodec.TryMergeDataRecord(
                                oldData, record, requireThreeRecords,
                                out var mergedData, out error))
                            throw new NativeHeroInvalidDataException(error);
                        if (forceLevelOverride.HasValue
                            && !NativeHeroBlobCodec.TryApplyIndexForceLevel(mergedData,
                                forceLevelOverride.Value, out mergedData, out error))
                            throw new NativeHeroInvalidDataException(error);
                        if (!NativeHeroBlobCodec.TryEncodeDataBlob(
                                mergedData, out storedData, out error))
                            throw new NativeHeroInvalidDataException(error);
                    }

                    using var update = new MySqlCommand(
                        @"UPDATE mir3.hero_data AS d
                          JOIN mir3.hero_index AS h ON h.idx=d.Idx
                          SET d.Data=@d, d.dynData=@dd,
                              h.lvChangeTime=IF(h.Level<>@level
                                  OR h.ForceLv<>@forceLv OR h.sfLevel<>@sfLevel,
                                  NOW(),h.lvChangeTime),
                              h.IsDelete=IF(@absoluteState=1,@isDelete,
                                  IF(@setDelete=1 AND h.HeroType=2,1,h.IsDelete)),
                              h.HeroType=IF(@absoluteState=1,@heroType,h.HeroType),
                              h.Consignation=IF(@absoluteState=1,@consignation,
                                  IF(@setConsignation=1,1,h.Consignation)),
                              h.Level=@level, h.Exp=@exp,
                              h.Job=IF(@absoluteState=1,@indexJob,
                                  IF(h.Job=255,h.Job,@job)), h.Sex=@sex,
                              h.ForceLv=@forceLv, h.ForceExp=@forceExp,
                              h.sfLevel=@sfLevel,
                              h.ModifyDate=NOW()
                          WHERE d.Idx=@i", conn, tx);
                    update.Parameters.Add("@d", MySqlDbType.Blob).Value = storedData;
                    update.Parameters.Add("@dd", MySqlDbType.Blob).Value = storedDynamicData;
                    update.Parameters.AddWithValue("@level", heroRecord.Level);
                    update.Parameters.AddWithValue("@exp", heroRecord.IndexExp);
                    update.Parameters.AddWithValue("@job", heroRecord.Job);
                    update.Parameters.AddWithValue("@indexJob",
                        currentIndexJob == byte.MaxValue
                            ? byte.MaxValue : indexJob);
                    update.Parameters.AddWithValue("@sex", heroRecord.Sex);
                    update.Parameters.AddWithValue("@forceLv",
                        forceLevelOverride.HasValue
                            ? forceLevelOverride.Value : heroRecord.IndexForceLv);
                    update.Parameters.AddWithValue("@forceExp", heroRecord.IndexForceExp);
                    update.Parameters.AddWithValue("@sfLevel", heroRecord.IndexSfLevel);
                    update.Parameters.AddWithValue("@setDelete", setDelete ? 1 : 0);
                    update.Parameters.AddWithValue("@setConsignation",
                        setConsignation ? 1 : 0);
                    update.Parameters.AddWithValue("@absoluteState",
                        absoluteState ? 1 : 0);
                    update.Parameters.AddWithValue("@isDelete", isDelete ? 1 : 0);
                    update.Parameters.AddWithValue("@heroType", heroType);
                    update.Parameters.AddWithValue("@consignation", consignation);
                    update.Parameters.AddWithValue("@i", idx);
                    if (update.ExecuteNonQuery() <= 0)
                        throw new InvalidOperationException(
                            "native hero_data/index rows were not matched");
                    tx.Commit();
                    return NativeHeroSaveResult.Success;
                }
                catch (NativeHeroInvalidDataException ex)
                {
                    try { tx.Rollback(); } catch { }
                    DBShare.MainOutMessage($"[HeroSaveRecord] REJECT idx={idx}: {ex.Message}");
                    return NativeHeroSaveResult.InvalidData;
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    DBShare.MainOutMessage($"[HeroSaveRecord] REJECT idx={idx}: {ex.Message}");
                    return NativeHeroSaveResult.RetryableFailure;
                }
            }
        }

        public NativeForceLevelStoreAttempt ApplyNativeForceLevel(int idx,
            byte[] heroName, ushort forceLevel)
        {
            heroName ??= Array.Empty<byte>();
            var loaded = LoadBlob(idx);
            if (loaded.data == null
                || !NativeHeroBlobCodec.TryApplyIndexForceLevel(loaded.data, forceLevel,
                    out _, out _))
                return new NativeForceLevelStoreAttempt(
                    NativeForceLevelStoreResult.LoadFailed);

            _nativeForceLevels[idx] = forceLevel;
            return new NativeForceLevelStoreAttempt(
                NativeForceLevelStoreResult.Queued,
                new NativeForceLevelMutation
                {
                    Target = NativeForceLevelTarget.Hero,
                    Index = idx,
                    ForceLevel = forceLevel,
                    CharacterNameBytes = (byte[])heroName.Clone()
                });
        }

        public bool TryGetNativeForceLevelOverride(int idx,
            out ushort forceLevel) =>
            _nativeForceLevels.TryGetValue(idx, out forceLevel);

        public void SetNativeForceLevelOverride(int idx, ushort forceLevel) =>
            _nativeForceLevels[idx] = forceLevel;

        public void ClearNativeForceLevelOverride(int idx) =>
            _nativeForceLevels.TryRemove(idx, out _);

        public NativeHeroSaveResult PersistNativeForceLevel(int idx,
            ushort forceLevel)
        {
            var saveLock = _recordSaveLocks[(idx & int.MaxValue)
                                             % _recordSaveLocks.Length];
            lock (_threeSlotLock)
            lock (saveLock)
            {
                using var conn = OpenConn();
                if (conn == null) return NativeHeroSaveResult.RetryableFailure;
                try
                {
                    byte[] storedData;
                    using (var read = new MySqlCommand(
                               "SELECT Data FROM mir3.hero_data WHERE Idx=@i LIMIT 1",
                               conn))
                    {
                        read.Parameters.AddWithValue("@i", idx);
                        var value = read.ExecuteScalar();
                        if (value == null || value == DBNull.Value)
                            return NativeHeroSaveResult.RetryableFailure;
                        storedData = value as byte[] ?? Array.Empty<byte>();
                    }
                    if (!NativeHeroBlobCodec.TryDecodeDataBlob(storedData,
                            out var data, out var error)
                        || !NativeHeroBlobCodec.TryApplyIndexForceLevel(data, forceLevel,
                            out var updatedData, out error)
                        || !NativeHeroBlobCodec.TryEncodeDataBlob(updatedData,
                            out var updatedStoredData, out error))
                    {
                        DBShare.MainOutMessage(
                            $"[NativeForceLevel] hero persist idx={idx}: {error}");
                        return NativeHeroSaveResult.InvalidData;
                    }

                    using (var update = new MySqlCommand(
                               @"UPDATE mir3.hero_data AS d
                                 JOIN mir3.hero_index AS h ON h.idx=d.Idx
                                 SET d.Data=@data,
                                     h.lvChangeTime=IF(h.ForceLv<>@forceLevel,
                                         NOW(),h.lvChangeTime),
                                     h.ForceLv=@forceLevel,
                                     h.ModifyDate=NOW()
                                 WHERE d.Idx=@idx", conn))
                    {
                        update.Parameters.Add("@data", MySqlDbType.Blob).Value =
                            updatedStoredData;
                        update.Parameters.AddWithValue("@forceLevel", forceLevel);
                        update.Parameters.AddWithValue("@idx", idx);
                        if (update.ExecuteNonQuery() > 0) return NativeHeroSaveResult.Success;
                    }
                    using var exists = new MySqlCommand(
                        @"SELECT COUNT(*) FROM mir3.hero_data AS d
                          JOIN mir3.hero_index AS h ON h.idx=d.Idx
                          WHERE d.Idx=@idx", conn);
                    exists.Parameters.AddWithValue("@idx", idx);
                    return Convert.ToInt32(exists.ExecuteScalar()) == 1
                        ? NativeHeroSaveResult.Success
                        : NativeHeroSaveResult.RetryableFailure;
                }
                catch (Exception ex)
                {
                    DBShare.MainOutMessage(
                        $"[NativeForceLevel] hero persist idx={idx}: {ex.Message}");
                    return NativeHeroSaveResult.RetryableFailure;
                }
            }
        }

        public ushort BuildThreeSlot(string masterName,
            IReadOnlyDictionary<int, NativeHeroLogicalSnapshot> logicalSnapshots,
            out string heroName,
            out NativeHeroLogicalSnapshot[] builtSnapshots)
        {
            heroName = string.Empty;
            builtSnapshots = Array.Empty<NativeHeroLogicalSnapshot>();
            if (string.IsNullOrEmpty(masterName)) return 0;

            lock (_threeSlotLock)
            {
                using var conn = OpenConn();
                if (conn == null) return 5;
                try
                {
                    using (var master = new MySqlCommand(
                               "SELECT IsDelete FROM mir3.user_index "
                               + "WHERE ChrName=@n LIMIT 1", conn))
                    {
                        master.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                        var value = master.ExecuteScalar();
                        if (value == null || value == DBNull.Value) return 0;
                        if (Convert.ToInt32(value) != 0) return 4;
                    }

                    var candidates = new List<ThreeSlotCandidate>(2);
                    using (var query = new MySqlCommand(
                               @"SELECT h.idx, h.MasterName, h.HeroName,
                                        h.IsDelete, h.HeroType, h.Job,
                                        h.Consignation, h.Level, h.Exp, h.Sex,
                                        d.Data, d.dynData
                                 FROM mir3.hero_index AS h
                                 LEFT JOIN mir3.hero_data AS d ON d.Idx=h.idx
                                 WHERE h.MasterName=@m
                                 ORDER BY h.idx", conn))
                    {
                        query.Parameters.Add(LegacyGbkText.Parameter("@m", masterName));
                        using var reader = query.ExecuteReader();
                        while (reader.Read())
                        {
                            var idx = reader.GetInt32("idx");
                            var candidate = new ThreeSlotCandidate
                            {
                                Idx = idx,
                                MasterName = LegacyGbkText.Read(reader,
                                    "MasterName"),
                                HeroName = LegacyGbkText.Read(reader,
                                    "HeroName"),
                                IsDelete = reader.GetInt32("IsDelete") != 0,
                                HeroType = reader.GetInt32("HeroType"),
                                Job = reader.GetInt32("Job"),
                                DatabaseJob = reader.GetInt32("Job"),
                                Consignation = reader.GetInt32("Consignation"),
                                DatabaseConsignation = reader.GetInt32(
                                    "Consignation"),
                                Level = reader.GetInt32("Level"),
                                Exp = unchecked((uint)reader.GetInt64("Exp")),
                                Sex = reader.GetInt32("Sex"),
                                StoredData = reader["Data"] as byte[],
                                StoredDynamicData = reader["dynData"] as byte[]
                            };
                            if (logicalSnapshots != null
                                && logicalSnapshots.TryGetValue(idx,
                                    out var logical))
                                candidate.Apply(logical);
                            if (candidate.IsDelete) continue;
                            if (candidate.Job == byte.MaxValue) return 2;
                            var consignation = candidate.Consignation;
                            if (consignation == 0) return 3;
                            candidates.Add(candidate);
                        }
                    }
                    if (candidates.Count != 2) return 0;

                    var higher = candidates[0];
                    var lower = candidates[1];
                    if (lower.Level > higher.Level)
                        (higher, lower) = (lower, higher);

                    if (!higher.TryLoadData(out var higherData,
                            out var higherDynamicData, out var error)
                        || !lower.TryLoadData(out var lowerData,
                            out var lowerDynamicData, out error))
                        return 5;

                    if (_nativeForceLevels.TryGetValue(higher.Idx,
                            out var higherForce)
                        && !NativeHeroBlobCodec.TryApplyIndexForceLevel(higherData, higherForce,
                            out higherData, out error)
                        || _nativeForceLevels.TryGetValue(lower.Idx,
                            out var lowerForce)
                        && !NativeHeroBlobCodec.TryApplyIndexForceLevel(lowerData, lowerForce,
                            out lowerData, out error))
                        return 5;

                    if (!NativeHeroBlobCodec.TryBuildThreeSlotData(
                            lowerData, higherData, out var lowerThreeSlotData,
                            out var rankedHigherData, out error))
                        return 6;
                    if (!NativeHeroBlobCodec.TryEncodeDataBlob(
                            lowerThreeSlotData, out var storedLowerData, out error)
                        || !NativeHeroBlobCodec.TryEncodeDataBlob(
                            rankedHigherData, out var storedHigherData, out error))
                        return 5;
                    byte[] higherRecordData;
                    if (!NativeHeroBlobCodec.TrySelectDataRecord(
                            rankedHigherData, higher.Job is >= 0 and < 3
                                ? higher.Job : 0,
                            rankedHigherData.Length
                            == NativeHeroBlobCodec.ThreeHeroRecordSize,
                            out higherRecordData, out error))
                        return 5;
                    byte[] lowerRecordData;
                    if (lower.Job is >= 0 and < 3)
                    {
                        if (!NativeHeroBlobCodec.TrySelectDataRecord(
                                lowerThreeSlotData, lower.Job, true,
                                out lowerRecordData, out error))
                            return 5;
                    }
                    else
                        lowerRecordData = (byte[])lowerData.Clone();
                    if (!NativeHeroDbFrameCodec.TryCreateRecord(
                            higherRecordData, out var higherRecord, out error)
                        || !NativeHeroDbFrameCodec.TryCreateRecord(
                            lowerRecordData, out var lowerRecord, out error))
                        return 5;
                    var preparedSnapshots = new[]
                    {
                        CreateBuiltSnapshot(higher, higherRecord,
                            rankedHigherData, higherDynamicData,
                            higher.Job, higherRecordData),
                        CreateBuiltSnapshot(lower, lowerRecord,
                            lowerThreeSlotData, lowerDynamicData,
                            byte.MaxValue, lowerRecordData)
                    };

                    // hero_index/hero_data are MyISAM in the original schema. A single
                    // multi-table statement keeps the two snapshots and two index rows
                    // under one MySQL statement lock; BeginTransaction/FOR UPDATE would
                    // provide no rollback or row-lock guarantee on these tables.
                    using var update = new MySqlCommand(
                        @"UPDATE mir3.hero_index AS highIndex
                          JOIN mir3.hero_data AS highData
                            ON highData.Idx=highIndex.idx
                          JOIN mir3.hero_index AS lowIndex
                            ON lowIndex.idx=@lowIdx
                          JOIN mir3.hero_data AS lowData
                            ON lowData.Idx=lowIndex.idx
                          SET highData.Data=@highData,
                              lowData.Data=@lowData,
                              highIndex.Consignation=0,
                              highIndex.ModifyDate=NOW(),
                              lowIndex.Job=255,
                              lowIndex.Consignation=0,
                              lowIndex.ModifyDate=NOW()
                          WHERE highIndex.idx=@highIdx
                            AND highIndex.IsDelete=0
                            AND highIndex.Job=@highJob
                            AND highIndex.Consignation=@highConsignation
                            AND lowIndex.IsDelete=0
                            AND lowIndex.Job=@lowJob
                            AND lowIndex.Consignation=@lowConsignation", conn);
                    update.Parameters.Add("@highData", MySqlDbType.Blob).Value =
                        storedHigherData;
                    update.Parameters.Add("@lowData", MySqlDbType.Blob).Value =
                        storedLowerData;
                    update.Parameters.AddWithValue("@highIdx", higher.Idx);
                    update.Parameters.AddWithValue("@lowIdx", lower.Idx);
                    update.Parameters.AddWithValue(
                        "@highJob", higher.DatabaseJob);
                    update.Parameters.AddWithValue(
                        "@lowJob", lower.DatabaseJob);
                    update.Parameters.AddWithValue(
                        "@highConsignation", higher.DatabaseConsignation);
                    update.Parameters.AddWithValue(
                        "@lowConsignation", lower.DatabaseConsignation);
                    if (update.ExecuteNonQuery() <= 0) return 5;

                    heroName = lower.HeroName;
                    builtSnapshots = preparedSnapshots;
                    return 1;
                }
                catch (Exception ex)
                {
                    DBShare.MainOutMessage(
                        $"[HeroThreeSlot] REJECT master={masterName}: {ex.Message}");
                    return 5;
                }
            }
        }

        private static NativeHeroLogicalSnapshot CreateBuiltSnapshot(
            ThreeSlotCandidate candidate, NativeHeroRecord record,
            byte[] data, byte[] dynamicData, int indexJob, byte[] recordData) =>
            new(candidate.Idx, candidate.MasterName, candidate.HeroName,
                recordData, data, dynamicData, false, candidate.HeroType, 0,
                indexJob, record.Level, record.IndexExp, record.Sex,
                record.IndexForceLv, record.IndexForceExp,
                record.IndexSfLevel);

        public bool CreateDataRow(int idx, string heroName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("INSERT IGNORE INTO mir3.hero_data(Idx, HeroName) VALUES(@i, @n)", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", heroName));
            cmd.ExecuteNonQuery();
            _quickIndex[idx] = idx;
            return true;
        }

        public void RegisterNativeIndex(int idx)
        {
            if (idx > 0) _quickIndex[idx] = idx;
        }

        public void UnregisterNativeIndex(int idx)
        {
            if (idx <= 0) return;
            _quickIndex.TryRemove(idx, out _);
            _nativeForceLevels.TryRemove(idx, out _);
        }

        public bool DeleteDataRow(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("DELETE FROM mir3.hero_data WHERE Idx=@i", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            bool ok = cmd.ExecuteNonQuery() > 0;
            if (ok) UnregisterNativeIndex(idx);
            return ok;
        }

        private static MySqlConnection OpenConn()
        {
            try
            {
                var builder = new MySqlConnectionStringBuilder(DBShare.DBConnection)
                {
                    // CLIENT_FOUND_ROWS lets an identical snapshot still prove that the
                    // joined hero_data/hero_index rows exist and were matched.
                    UseAffectedRows = false
                };
                var c = new MySqlConnection(builder.ConnectionString);
                c.Open();
                using(var sc = new MySqlCommand("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; SET SESSION wait_timeout=2073600", c))
                    sc.ExecuteNonQuery();
                return c;
            }
            catch { return null; }
        }

        private static object[] CreateRecordSaveLocks()
        {
            var locks = new object[RecordSaveLockCount];
            for (var i = 0; i < locks.Length; i++) locks[i] = new object();
            return locks;
        }

        private sealed class ThreeSlotCandidate
        {
            public int Idx;
            public string MasterName;
            public string HeroName;
            public bool IsDelete;
            public int HeroType;
            public int Job;
            public int DatabaseJob;
            public int Consignation;
            public int DatabaseConsignation;
            public int Level;
            public uint Exp;
            public int Sex;
            public byte[] StoredData;
            public byte[] StoredDynamicData;
            public byte[] Data;
            public byte[] DynamicData;

            public void Apply(NativeHeroLogicalSnapshot snapshot)
            {
                MasterName = snapshot.MasterName;
                HeroName = snapshot.HeroName;
                IsDelete = snapshot.IsDelete;
                HeroType = snapshot.HeroType;
                Job = snapshot.IndexJob;
                Consignation = snapshot.Consignation;
                Level = snapshot.Level;
                Exp = snapshot.Experience;
                Sex = snapshot.Sex;
                Data = (byte[])snapshot.Data.Clone();
                DynamicData = (byte[])snapshot.DynamicData.Clone();
            }

            public bool TryLoadData(out byte[] data, out byte[] dynamicData,
                out string error)
            {
                error = string.Empty;
                if (Data != null)
                {
                    data = (byte[])Data.Clone();
                    dynamicData = DynamicData == null
                        ? Array.Empty<byte>() : (byte[])DynamicData.Clone();
                    return NativeHeroBlobCodec.TrySelectDataRecord(data, 0,
                               data.Length == NativeHeroBlobCodec.ThreeHeroRecordSize,
                               out _, out error);
                }
                data = null;
                dynamicData = null;
                return StoredData != null
                       && NativeHeroBlobCodec.TryDecodeDataBlob(
                           StoredData, out data, out error)
                       && NativeHeroBlobCodec.TryDecodeDynamicBlob(
                           StoredDynamicData, out dynamicData, out error);
            }
        }
    }
}
