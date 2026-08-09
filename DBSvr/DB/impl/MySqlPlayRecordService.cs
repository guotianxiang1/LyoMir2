using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using SystemModule;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 角色索引 MySQL 实现。
    /// 对应 Delphi 原版 user_index 表全部操作。
    /// 从 DBServer 二进制逆向工程还原完整 SQL 逻辑。
    /// </summary>
    public class MySqlPlayRecordService : IPlayRecordService
    {
        private readonly ConcurrentDictionary<string, int> QuickList;          // ChrName → index
        private readonly ConcurrentDictionary<int, string> IndexQuickList;     // index → ChrName
        private readonly ConcurrentDictionary<string, int> NativeRawNameIndex;
        private readonly ConcurrentDictionary<string, byte> NativeOccupiedNames;
        private readonly ConcurrentDictionary<int, ushort> NativeForceLevels;
        private readonly object NativeType3CacheLock = new();
        private readonly object NativeIdentityMutationLock = new();
        private Dictionary<string, List<ChrIndexInfo>> NativeType3ByPtid;
        private Dictionary<int, ChrIndexInfo> NativeType3ByIndex;
        private readonly NativeAccountStorageCache NativeAccountStorage;

        public MySqlPlayRecordService() : this(new NativeAccountStorageCache())
        {
        }

        public MySqlPlayRecordService(
            NativeAccountStorageCache nativeAccountStorage)
        {
            NativeAccountStorage = nativeAccountStorage
                ?? throw new ArgumentNullException(nameof(nativeAccountStorage));
            QuickList = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            IndexQuickList = new ConcurrentDictionary<int, string>();
            NativeRawNameIndex = new ConcurrentDictionary<string, int>(
                StringComparer.Ordinal);
            NativeOccupiedNames = new ConcurrentDictionary<string, byte>(
                StringComparer.Ordinal);
            NativeForceLevels = new ConcurrentDictionary<int, ushort>();
            NativeType3ByPtid = new Dictionary<string, List<ChrIndexInfo>>(
                StringComparer.Ordinal);
            NativeType3ByIndex = new Dictionary<int, ChrIndexInfo>();
        }

        // ===================== 初始化 =====================

        public void LoadQuickList()
        {
            using var conn = OpenConnection();
            if (conn == null) return;

            var lastIndex = 0;
            MySqlConnection userIdConnection = null;
            try
            {
                while (true)
                {
                    var pageCount = 0;
                    using (var cmd = new MySqlCommand(
                               @"SELECT idx, PTID, ChrName, IsSelect, IsDelete,
                                        Level, Job, Sex, Exp, UserId, ModifyDate,
                                        IsTransLock, DesZoneId, DesGroupId
                                 FROM mir3.user_index
                                 WHERE idx>@lastIndex
                                 ORDER BY idx LIMIT 5000", conn))
                    {
                        cmd.Parameters.AddWithValue("@lastIndex", lastIndex);
                        using var dr = cmd.ExecuteReader();
                        while (dr.Read())
                        {
                            var deleteState = NativeType3Protocol.NormalizeDeleteState(
                                dr.GetInt32("IsDelete"));
                            var ptidBytes = ReadAnsiBytes(dr, "PTID");
                            NativeAccountStorage.RegisterAccount(ptidBytes);
                            var chrNameBytes = ReadAnsiBytes(dr, "ChrName");
                            NativeRawNameIndex[
                                NativeForceLevelProtocol.NormalizeCharacterNameKey(
                                    chrNameBytes)] = dr.GetInt32("idx");
                            NativeOccupiedNames[
                                NativeForceLevelProtocol.NormalizeCharacterNameKey(
                                    chrNameBytes)] = 0;
                            var nativeRecord = new ChrIndexInfo
                            {
                                Idx = dr.GetInt32("idx"),
                                PTID = NativeType3Protocol.DecodeAnsi(ptidBytes),
                                PTIDBytes = ptidBytes,
                                ChrName = NativeType3Protocol.DecodeAnsi(chrNameBytes),
                                ChrNameBytes = chrNameBytes,
                                Job = dr.GetInt32("Job"),
                                Sex = dr.GetInt32("Sex"),
                                Level = dr.GetInt32("Level"),
                                Exp = ReadExpBits(dr),
                                UserId = dr.IsDBNull(dr.GetOrdinal("UserId"))
                                    ? 0 : dr.GetInt64("UserId"),
                                IsTransLock = !dr.IsDBNull(
                                                  dr.GetOrdinal("IsTransLock"))
                                              && dr.GetInt32("IsTransLock") != 0,
                                DestinationZoneId = dr.IsDBNull(
                                    dr.GetOrdinal("DesZoneId"))
                                    ? 0 : dr.GetInt32("DesZoneId"),
                                DestinationGroupId = dr.IsDBNull(
                                    dr.GetOrdinal("DesGroupId"))
                                    ? 0 : dr.GetInt32("DesGroupId"),
                                DeleteState = deleteState,
                                IsDelete = deleteState != 0,
                                IsSelect = dr.GetInt32("IsSelect") != 0,
                                ModifyDate = dr.GetDateTime("ModifyDate")
                            };

                            if (nativeRecord.UserId == 0)
                            {
                                nativeRecord.UserId =
                                    NativeType3Protocol.CreateFallbackUserId(
                                        DBShare.nZoneIdx, DBShare.nGroupIdx,
                                        nativeRecord.Idx);
                                userIdConnection ??= OpenConnection();
                                if (userIdConnection == null)
                                    throw new InvalidOperationException(
                                        "native UserId update connection failed");
                                using var update = new MySqlCommand(
                                    "UPDATE mir3.user_index SET UserId=@userId WHERE idx=@idx",
                                    userIdConnection);
                                update.Parameters.AddWithValue(
                                    "@userId", nativeRecord.UserId);
                                update.Parameters.AddWithValue(
                                    "@idx", nativeRecord.Idx);
                                update.ExecuteNonQuery();
                            }

                            AddNativeType3Record(nativeRecord);
                            if (nativeRecord.DeleteState == 0)
                            {
                                QuickList[nativeRecord.ChrName] = nativeRecord.Idx;
                                IndexQuickList[nativeRecord.Idx] =
                                    nativeRecord.ChrName;
                            }

                            lastIndex = nativeRecord.Idx;
                            pageCount++;
                        }
                    }

                    if (pageCount == 0) break;
                }
            }
            finally { userIdConnection?.Dispose(); }
        }

        // ===================== 查询 =====================

        public int Index(string sName)
        {
            if (string.IsNullOrEmpty(sName)) return -1;
            if (QuickList.TryGetValue(sName, out int idx)) return idx;
            idx = GetIdxByName(sName);
            if (idx >= 0)
            {
                QuickList[sName] = idx;
                IndexQuickList[idx] = sName;
                NativeRawNameIndex[
                    NativeForceLevelProtocol.NormalizeCharacterNameKey(
                        LegacyGbkText.Encode(sName))] = idx;
            }
            return idx;
        }

        public HumRecordData Get(int nIndex, ref bool success)
        {
            if (!IndexQuickList.TryGetValue(nIndex, out string chrName))
            {
                success = false;
                return null;
            }

            using var conn = OpenConnection();
            if (conn == null) { success = false; return null; }

            using var cmd = new MySqlCommand(
                @"SELECT idx, PTID, ChrName, IsSelect, IsDelete, Level, Job, Sex, Exp,
                         CreateDate, ModifyDate
                  FROM mir3.user_index WHERE ChrName=@name LIMIT 1", conn);
                cmd.Parameters.Add(LegacyGbkText.Parameter("@name", chrName));
            using var dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                success = true;
                return ReadHumRecord(dr);
            }
            success = false;
            return null;
        }

        public HumRecordData GetBy(int nIndex, ref bool success)
        {
            using var conn = OpenConnection();
            if (conn == null) { success = false; return null; }

            using var cmd = new MySqlCommand(
                @"SELECT idx, PTID, ChrName, IsSelect, IsDelete, Level, Job, Sex, Exp,
                         CreateDate, ModifyDate
                  FROM mir3.user_index WHERE idx=@id LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@id", nIndex);
            using var dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                success = true;
                var rec = ReadHumRecord(dr);
                // 更新内存索引
                QuickList[rec.sChrName] = nIndex;
                IndexQuickList[nIndex] = rec.sChrName;
                NativeRawNameIndex[
                    NativeForceLevelProtocol.NormalizeCharacterNameKey(
                        LegacyGbkText.Encode(rec.sChrName))] = nIndex;
                return rec;
            }
            success = false;
            return null;
        }

        public int FindByAccount(string sAccount, ref IList<TQuickID> ChrList)
        {
            ChrList.Clear();
            using var conn = OpenConnection();
            if (conn == null) return -1;

            using var cmd = new MySqlCommand(
                @"SELECT idx, ChrName FROM mir3.user_index
                  WHERE PTID=@ptid AND IsDelete=0 LIMIT 2", conn);
            cmd.Parameters.AddWithValue("@ptid", sAccount);
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                var qid = new TQuickID
                {
                    nIndex = dr.GetInt32("idx"),
                    sAccount = sAccount
                };
                ChrList.Add(qid);
            }
            return ChrList.Count;
        }

        public int FindDeletedByAccount(string sAccount, ref IList<HumRecordData> DeletedList)
        {
            DeletedList.Clear();
            using var conn = OpenConnection();
            if (conn == null) return -1;

            using var cmd = new MySqlCommand(
                @"SELECT idx, PTID, ChrName, IsSelect, IsDelete, Level, Job, Sex, Exp,
                         CreateDate, ModifyDate
                  FROM mir3.user_index
                  WHERE PTID=@ptid AND IsDelete=1 LIMIT 10", conn);
            cmd.Parameters.AddWithValue("@ptid", sAccount);
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                DeletedList.Add(ReadHumRecord(dr));
            }
            return DeletedList.Count;
        }

        public bool ReAddToQuickList(string sChrName)
        {
            if (string.IsNullOrEmpty(sChrName)) return false;

            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                "SELECT idx FROM mir3.user_index WHERE ChrName=@name AND IsDelete=0 LIMIT 1", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@name", sChrName));
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) return false;

            int idx = Convert.ToInt32(obj);
            QuickList[sChrName] = idx;
            IndexQuickList[idx] = sChrName;
            NativeRawNameIndex[
                NativeForceLevelProtocol.NormalizeCharacterNameKey(
                    LegacyGbkText.Encode(sChrName))] = idx;
            UpdateNativeType3Record(idx, null, record =>
            {
                record.DeleteState = 0;
                record.IsDelete = false;
            });
            return true;
        }

        public int ChrCountOfAccount(string sAccount)
        {
            using var conn = OpenConnection();
            if (conn == null) return 0;

            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM mir3.user_index WHERE PTID=@ptid AND IsDelete=0", conn);
            cmd.Parameters.AddWithValue("@ptid", sAccount);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public int TodayCreateCount(string sAccount)
        {
            using var conn = OpenConnection();
            if (conn == null) return 0;
            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM mir3.user_index WHERE PTID=@ptid AND DATE(CreateDate)=CURDATE()", conn);
            cmd.Parameters.AddWithValue("@ptid", sAccount);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public int FindByName(string sChrName, List<HumRecordData> ChrList)
        {
            ChrList.Clear();
            using var conn = OpenConnection();
            if (conn == null) return -1;

            using var cmd = new MySqlCommand(
                @"SELECT idx, PTID, ChrName, IsSelect, IsDelete, Level, Job, Sex, Exp,
                         CreateDate, ModifyDate
                  FROM mir3.user_index WHERE ChrName=@name LIMIT 1", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@name", sChrName));
            using var dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                ChrList.Add(ReadHumRecord(dr));
                return 1;
            }
            return 0;
        }

        // ===================== 创建角色 =====================

        public int CreateCharacter(string ptid, string chrName, int job, int sex,
            int hair, int level = 1)
        {
            lock (NativeIdentityMutationLock)
                return CreateCharacterCore(ptid, chrName, job, sex, hair, level);
        }

        private int CreateCharacterCore(string ptid, string chrName, int job,
            int sex, int hair, int level)
        {
            using var conn = OpenConnection();
            if (conn == null) return -1;

            using var tx = conn.BeginTransaction();
            try
            {
                // 1. INSERT INTO user_index
                using var cmd = new MySqlCommand(
                    @"INSERT INTO mir3.user_index(PTID, ChrName, IsDelete, IsSelect, Level, Job, Sex, Exp,
                               sfLevel, ForceLv, ForceExp, FightPoints, lvChangeTime, ApprenticeNum, PlatinaChrLv,
                               CreateDate, ModifyDate)
                      VALUES(@pt, @chr, 0, 0, @lvl, @job, @sex, 0,
                             1, 0, 0, 0, '2100-01-01', 0, 0,
                             NOW(), NOW());
                      SELECT LAST_INSERT_ID();", conn, tx);
                var chrBytes = LegacyGbkText.Encode(chrName);
                cmd.Parameters.AddWithValue("@pt", ptid);
                cmd.Parameters.Add("@chr", MySqlDbType.VarBinary).Value = chrBytes;
                cmd.Parameters.AddWithValue("@job", job);
                cmd.Parameters.AddWithValue("@sex", sex);
                cmd.Parameters.AddWithValue("@lvl", level);
                var idx = Convert.ToInt32(cmd.ExecuteScalar());
                var userId = NativeType3Protocol.CreateFallbackUserId(
                    DBShare.nZoneIdx, DBShare.nGroupIdx, idx);

                using (var userIdUpdate = new MySqlCommand(
                           "UPDATE mir3.user_index SET UserId=@userId WHERE idx=@idx",
                           conn, tx))
                {
                    userIdUpdate.Parameters.AddWithValue("@userId", userId);
                    userIdUpdate.Parameters.AddWithValue("@idx", idx);
                    userIdUpdate.ExecuteNonQuery();
                }

                // 2. INSERT IGNORE INTO user_data
                using var cmd2 = new MySqlCommand(
                    "INSERT IGNORE INTO mir3.user_data(Idx, ChrName) VALUES(@idx, @chr)", conn, tx);
                cmd2.Parameters.AddWithValue("@idx", idx);
                cmd2.Parameters.Add("@chr", MySqlDbType.VarBinary).Value = chrBytes;
                if (cmd2.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        $"native user_data row was not created for idx={idx}");

                tx.Commit();
                NativeAccountStorage.RegisterAccount(
                    NativeType3Protocol.EncodeAnsi(ptid));

                // 更新内存索引
                QuickList[chrName] = idx;
                IndexQuickList[idx] = chrName;
                NativeRawNameIndex[
                    NativeForceLevelProtocol.NormalizeCharacterNameKey(chrBytes)] = idx;
                NativeOccupiedNames[
                    NativeForceLevelProtocol.NormalizeCharacterNameKey(chrBytes)] = 0;
                AddNativeType3Record(new ChrIndexInfo
                {
                    Idx = idx,
                    PTID = ptid,
                    PTIDBytes = NativeType3Protocol.EncodeAnsi(ptid),
                    ChrName = chrName,
                    ChrNameBytes = chrBytes,
                    Job = job,
                    Sex = sex,
                    Level = level,
                    Exp = 0,
                    UserId = userId,
                    DeleteState = 0,
                    IsDelete = false,
                    IsSelect = false,
                    ModifyDate = DateTime.Now
                });

                DBShare.MainOutMessage($"[CreateCharacter] OK idx={idx} ptid={ptid} chr={chrName}");
                return idx;
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                DBShare.MainOutMessage($"[CreateCharacter] ERR: {ex.Message}");
                return -1;
            }
        }

        public bool IsChrNameExists(string chrName)
        {
            var chrBytes = LegacyGbkText.Encode(chrName);
            if (IsNativeCharacterNameOccupied(chrBytes)) return true;
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM mir3.user_index WHERE ChrName=@name", conn);
            cmd.Parameters.Add("@name", MySqlDbType.Binary).Value = chrBytes;
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public void RegisterNativeCharacterName(byte[] characterName)
        {
            lock (NativeIdentityMutationLock)
            {
                characterName ??= Array.Empty<byte>();
                if (characterName.Length > 14)
                    characterName = characterName.AsSpan(0, 14).ToArray();
                NativeOccupiedNames[
                    NativeForceLevelProtocol.NormalizeCharacterNameKey(
                        characterName)] = 0;
            }
        }

        public bool IsNativeCharacterNameOccupied(byte[] characterName) =>
            NativeOccupiedNames.ContainsKey(
                NativeForceLevelProtocol.NormalizeCharacterNameKey(
                    characterName ?? Array.Empty<byte>()));

        public bool TryGetNativeCharacterByName(byte[] characterName,
            out ChrIndexInfo character)
        {
            character = null;
            var key = NativeForceLevelProtocol.NormalizeCharacterNameKey(
                characterName ?? Array.Empty<byte>());
            if (!NativeRawNameIndex.TryGetValue(key, out var index)) return false;
            lock (NativeType3CacheLock)
            {
                if (!NativeType3ByIndex.TryGetValue(index, out var record))
                    return false;
                character = CloneNativeType3Record(record);
                return true;
            }
        }

        public bool TryGetNativeCharacterByUserId(long userId,
            out ChrIndexInfo character)
        {
            character = null;
            lock (NativeType3CacheLock)
            {
                foreach (var record in NativeType3ByIndex.Values)
                {
                    if (record.UserId != userId) continue;
                    character = CloneNativeType3Record(record);
                    return true;
                }
            }
            return false;
        }

        public bool TryRestoreNativeCharacter(byte[] characterName,
            out ChrIndexInfo character)
        {
            character = null;
            var raw = characterName ?? Array.Empty<byte>();
            var key = NativeForceLevelProtocol.NormalizeCharacterNameKey(raw);
            if (!NativeRawNameIndex.TryGetValue(key, out var index)) return false;
            lock (NativeType3CacheLock)
            {
                if (!NativeType3ByIndex.TryGetValue(index, out var record)
                    || record.DeleteState != 1)
                    return false;
                record.DeleteState = 0;
                record.IsDelete = false;
                QuickList[record.ChrName] = record.Idx;
                IndexQuickList[record.Idx] = record.ChrName;
                character = CloneNativeType3Record(record);
                return true;
            }
        }

        public bool PersistNativeCharacterRestore(int index)
        {
            if (index <= 0) return false;
            using var connection = OpenConnection();
            if (connection == null) return false;
            try
            {
                using (var update = new MySqlCommand(
                           "UPDATE mir3.user_index SET IsDelete=0, ModifyDate=NOW() "
                           + "WHERE idx=@idx AND IsDelete=1", connection))
                {
                    update.Parameters.AddWithValue("@idx", index);
                    if (update.ExecuteNonQuery() > 0) return true;
                }
                using var exists = new MySqlCommand(
                    "SELECT COUNT(*) FROM mir3.user_index "
                    + "WHERE idx=@idx AND IsDelete=0", connection);
                exists.Parameters.AddWithValue("@idx", index);
                return Convert.ToInt32(exists.ExecuteScalar()) == 1;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[NativeRestore] ERR idx={index}: {ex.Message}");
                return false;
            }
        }

        public NativeAccountRenameResult RenameNativeAccount(byte[] oldPtid,
            byte[] newPtid)
        {
            oldPtid ??= Array.Empty<byte>();
            newPtid ??= Array.Empty<byte>();
            if (oldPtid.Length == 0 || oldPtid.Length > 20
                || newPtid.Length == 0 || newPtid.Length > 20)
                return new NativeAccountRenameResult();

            string newPtidText;
            try { newPtidText = NativeType3Protocol.DecodeAnsi(newPtid); }
            catch (ArgumentException) { return new NativeAccountRenameResult(); }

            using var connection = OpenConnection();
            if (connection == null) return new NativeAccountRenameResult();
            using var tx = connection.BeginTransaction();
            try
            {
                var indices = new List<int>();
                using (var select = new MySqlCommand(
                           "SELECT idx FROM mir3.user_index WHERE PTID=@old "
                           + "FOR UPDATE", connection, tx))
                {
                    select.Parameters.Add("@old", MySqlDbType.VarBinary).Value =
                        oldPtid;
                    using var reader = select.ExecuteReader();
                    while (reader.Read()) indices.Add(reader.GetInt32(0));
                }
                var rewrittenData = new List<(int Index, byte[] Data)>();
                foreach (var index in indices)
                {
                    using var selectData = new MySqlCommand(
                        "SELECT Data FROM mir3.user_data WHERE Idx=@idx FOR UPDATE",
                        connection, tx);
                    selectData.Parameters.AddWithValue("@idx", index);
                    var value = selectData.ExecuteScalar() as byte[];
                    if (value == null || value.Length == 0) continue;
                    if (NativeHumanDataCodec.TryRewriteAccount(value, newPtid,
                            out var rewritten, out _))
                        rewrittenData.Add((index, rewritten));
                }

                using (var updateIndex = new MySqlCommand(
                           "UPDATE mir3.user_index SET PTID=@new "
                           + "WHERE PTID=@old", connection, tx))
                {
                    updateIndex.Parameters.Add("@new", MySqlDbType.VarBinary).Value =
                        newPtid;
                    updateIndex.Parameters.Add("@old", MySqlDbType.VarBinary).Value =
                        oldPtid;
                    if (updateIndex.ExecuteNonQuery() != indices.Count)
                        throw new InvalidOperationException(
                            "native account rename index rows changed concurrently");
                }
                foreach (var item in rewrittenData)
                {
                    using var updateData = new MySqlCommand(
                        "UPDATE mir3.user_data SET Data=@data WHERE Idx=@idx",
                        connection, tx);
                    updateData.Parameters.Add("@data", MySqlDbType.LongBlob).Value =
                        item.Data;
                    updateData.Parameters.AddWithValue("@idx", item.Index);
                    updateData.ExecuteNonQuery();
                }
                using (var updateCreditCard = new MySqlCommand(
                           "UPDATE gamedata.CreditCard SET PTID=@new "
                           + "WHERE PTID=@old", connection, tx))
                {
                    updateCreditCard.Parameters.Add("@new", MySqlDbType.VarBinary).Value =
                        newPtid;
                    updateCreditCard.Parameters.Add("@old", MySqlDbType.VarBinary).Value =
                        oldPtid;
                    updateCreditCard.ExecuteNonQuery();
                }
                using (var updateStorage = new MySqlCommand(
                           "UPDATE mir3.user_storage SET PTID=@new "
                           + "WHERE PTID=@old", connection, tx))
                {
                    updateStorage.Parameters.Add("@new", MySqlDbType.VarBinary).Value =
                        newPtid;
                    updateStorage.Parameters.Add("@old", MySqlDbType.VarBinary).Value =
                        oldPtid;
                    updateStorage.ExecuteNonQuery();
                }
                tx.Commit();
                RenameNativeType3Account(oldPtid, newPtid, newPtidText);
                return new NativeAccountRenameResult
                {
                    Success = true,
                    CharacterIndices = indices.ToArray()
                };
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                DBShare.MainOutMessage(
                    $"[NativeAccountRename] ERR: {ex.Message}");
                return new NativeAccountRenameResult();
            }
        }

        public void ResetAllNativeTransferLocks()
        {
            using var connection = OpenConnection();
            if (connection == null) return;
            var userIds = new HashSet<long>();
            using (var query = new MySqlCommand(
                       "SELECT UserId FROM mir3.user_index WHERE IsTransLock=1",
                       connection))
            using (var reader = query.ExecuteReader())
                while (reader.Read())
                    if (!reader.IsDBNull(0)) userIds.Add(reader.GetInt64(0));
            if (userIds.Count == 0) return;

            lock (NativeType3CacheLock)
                foreach (var record in NativeType3ByIndex.Values)
                    if (userIds.Contains(record.UserId))
                        record.IsTransLock = false;

            using var update = new MySqlCommand(
                "UPDATE mir3.user_index SET IsTransLock=0", connection);
            update.ExecuteNonQuery();
        }

        public void ResetNativeTransferLock(byte[] characterName)
        {
            var key = NativeForceLevelProtocol.NormalizeCharacterNameKey(
                characterName ?? Array.Empty<byte>());
            if (!NativeRawNameIndex.TryGetValue(key, out var index)) return;
            lock (NativeType3CacheLock)
            {
                if (!NativeType3ByIndex.TryGetValue(index, out var record))
                    return;
                record.IsTransLock = false;
                record.DestinationZoneId = 0;
                record.DestinationGroupId = 0;
            }

            using var connection = OpenConnection();
            if (connection == null) return;
            using var update = new MySqlCommand(
                @"UPDATE mir3.user_index
                  SET IsTransLock=0, DesZoneId=0, DesGroupId=0
                  WHERE idx=@idx",
                connection);
            update.Parameters.AddWithValue("@idx", index);
            update.ExecuteNonQuery();
        }

        public void SetNativeCharacterBusy(byte[] characterName)
        {
            var key = NativeForceLevelProtocol.NormalizeCharacterNameKey(
                characterName ?? Array.Empty<byte>());
            if (!NativeRawNameIndex.TryGetValue(key, out var index)) return;
            lock (NativeType3CacheLock)
                if (NativeType3ByIndex.TryGetValue(index, out var record))
                    record.NativeBusy = true;
        }

        public List<ChrIndexInfo> QueryChrByPtid(string ptid)
        {
            var list = new List<ChrIndexInfo>();
            using var conn = OpenConnection();
            if (conn == null) return list;

            using var cmd = new MySqlCommand(
                @"SELECT idx, ChrName, Job, Sex, Level, Exp, IsDelete, IsSelect, ModifyDate
                  FROM mir3.user_index
                  WHERE PTID=@ptid AND IsDelete=0 ORDER BY idx ASC LIMIT 20", conn);
            cmd.Parameters.AddWithValue("@ptid", ptid);
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(new ChrIndexInfo
                {
                    Idx = dr.GetInt32("idx"),
                    ChrName = ReadChrName(dr),
                    Job = dr.GetInt32("Job"),
                    Sex = dr.GetInt32("Sex"),
                    Level = dr.GetInt32("Level"),
                    IsDelete = dr.GetInt32("IsDelete") != 0,
                    IsSelect = dr.GetInt32("IsSelect") != 0,
                    ModifyDate = dr.GetDateTime("ModifyDate")
                });
            }
            return list;
        }

        public List<ChrIndexInfo> QueryNativeType3ByPtid(string ptid)
        {
            return QueryNativeType3ByPtid(
                NativeType3Protocol.EncodeAnsi(ptid));
        }

        public List<ChrIndexInfo> QueryNativeType3ByPtid(byte[] ptid)
        {
            lock (NativeType3CacheLock)
            {
                if (!NativeType3ByPtid.TryGetValue(
                        NativeType3Protocol.NormalizePtidKey(
                            ptid ?? Array.Empty<byte>()), out var records))
                    return new List<ChrIndexInfo>();
                var result = new List<ChrIndexInfo>(records.Count);
                foreach (var record in records)
                {
                    if (record.DeleteState == 1) continue;
                    result.Add(CloneNativeType3Record(record));
                }
                return result;
            }
        }

        public int GetIdxByName(string chrName)
        {
            using var conn = OpenConnection();
            if (conn == null) return -1;

            using var cmd = new MySqlCommand(
                "SELECT idx FROM mir3.user_index WHERE ChrName=@name AND IsDelete=0 LIMIT 1", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@name", chrName));
            var obj = cmd.ExecuteScalar();
            return obj == null || obj == DBNull.Value ? -1 : Convert.ToInt32(obj);
        }

        // ===================== 更新 =====================

        public bool Add(HumRecordData HumRecord)
        {
            // 直接调用 CreateCharacter
            int idx = CreateCharacter(HumRecord.sAccount, HumRecord.sChrName, 0, 0, 0);
            if (idx > 0)
            {
                HumRecord.Id = idx;
                return true;
            }
            return false;
        }

        public bool Update(int nIndex, ref HumRecordData HumDBRecord)
        {
            using var conn = OpenConnection();
            if (conn == null) return false;

            var sql = new StringBuilder(
                @"UPDATE mir3.user_index SET ModifyDate=NOW()");
            var parameters = new List<MySqlParameter>();

            if (HumDBRecord.boDeleted)
                sql.Append(", IsDelete=1");
            else
                sql.Append(", IsDelete=0, IsSelect=@sel");
            parameters.Add(new MySqlParameter("@sel", HumDBRecord.boSelected));

            sql.Append(" WHERE idx=@idx");
            parameters.Add(new MySqlParameter("@idx", HumDBRecord.Id));

            using var cmd = new MySqlCommand(sql.ToString(), conn);
            foreach (var p in parameters) cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();

            // 更新内存
            if (HumDBRecord.boDeleted)
            {
                QuickList.TryRemove(HumDBRecord.sChrName, out _);
                IndexQuickList.TryRemove(nIndex, out _);
            }
            var cacheDeleteState = HumDBRecord.boDeleted ? 1 : 0;
            var cacheIsSelected = HumDBRecord.boSelected != 0;
            UpdateNativeType3Record(HumDBRecord.Id, null, record =>
            {
                record.DeleteState = cacheDeleteState;
                record.IsDelete = cacheDeleteState != 0;
                record.IsSelect = cacheIsSelected;
            });
            return true;
        }

        public void UpdateBy(int nIndex, ref HumRecordData HumDBRecord)
        {
            Update(nIndex, ref HumDBRecord);
        }

        public bool UpdateCharIndex(int idx, int level, int exp, int job, int sex,
            int forceLv, int forceExp, int fightPoints, int sfLevel,
            int apprenticeNum = int.MinValue, int heroCardLv = int.MinValue,
            int platinaChrLv = int.MinValue)
        {
            using var conn = OpenConnection();
            if (conn == null) return false;

            var sql = new StringBuilder(
                @"UPDATE mir3.user_index SET
                    Level=@lvl, Exp=@exp, Job=@job, Sex=@sex,
                    ForceLv=@flv, ForceExp=@fexp, FightPoints=@fp, sfLevel=@sf,
                    ModifyDate=NOW()");
            if (apprenticeNum != int.MinValue) sql.Append(", ApprenticeNum=@an");
            if (heroCardLv != int.MinValue) sql.Append(", HeroCardLv=@hcl");
            if (platinaChrLv != int.MinValue) sql.Append(", PlatinaChrLv=@pcl");
            sql.Append(" WHERE idx=@idx");

            using var cmd = new MySqlCommand(sql.ToString(), conn);
            cmd.Parameters.AddWithValue("@lvl", level);
            cmd.Parameters.AddWithValue("@exp", unchecked((uint)exp));
            cmd.Parameters.AddWithValue("@job", job);
            cmd.Parameters.AddWithValue("@sex", sex);
            cmd.Parameters.AddWithValue("@flv", forceLv);
            cmd.Parameters.AddWithValue("@fexp", forceExp);
            cmd.Parameters.AddWithValue("@fp", fightPoints);
            cmd.Parameters.AddWithValue("@sf", sfLevel);
            if (apprenticeNum != int.MinValue) cmd.Parameters.AddWithValue("@an", apprenticeNum);
            if (heroCardLv != int.MinValue) cmd.Parameters.AddWithValue("@hcl", heroCardLv);
            if (platinaChrLv != int.MinValue) cmd.Parameters.AddWithValue("@pcl", platinaChrLv);
            cmd.Parameters.AddWithValue("@idx", idx);
            var updated = cmd.ExecuteNonQuery() > 0;
            if (updated)
            {
                UpdateNativeType3Record(idx, null, record =>
                {
                    record.Level = level;
                    record.Exp = exp;
                    record.Job = job;
                    record.Sex = sex;
                    record.ModifyDate = DateTime.Now;
                });
            }
            return updated;
        }

        public bool UpdateNativeSaveIndex(int idx,
            NativeSavePersistenceData persistence)
        {
            if (idx <= 0 || persistence == null
                || string.IsNullOrEmpty(persistence.Account)
                || string.IsNullOrEmpty(persistence.CharacterName))
                return false;

            using var conn = OpenConnection();
            if (conn == null) return false;

            try
            {
                int isDelete;
                int isSelect;
                int forceLevel;
                uint forceExperience;
                using (var select = new MySqlCommand(
                    @"SELECT IsDelete, IsSelect, ForceLv, ForceExp
                      FROM mir3.user_index
                      WHERE idx=@idx AND PTID=@account AND ChrName=@name
                      LIMIT 1", conn))
                {
                    select.Parameters.AddWithValue("@idx", idx);
                    select.Parameters.Add(LegacyGbkText.Parameter(
                        "@account", persistence.Account));
                    select.Parameters.Add(LegacyGbkText.Parameter(
                        "@name", persistence.CharacterName));
                    using var reader = select.ExecuteReader();
                    if (!reader.Read()) return false;
                    isDelete = reader.GetInt32("IsDelete");
                    isSelect = reader.GetInt32("IsSelect");
                    forceLevel = reader.GetInt32("ForceLv");
                    forceExperience = Convert.ToUInt32(reader["ForceExp"]);
                }
                if (NativeForceLevels.TryGetValue(idx, out var nativeForceLevel))
                    forceLevel = nativeForceLevel;

                using (var changed = new MySqlCommand(
                    @"UPDATE mir3.user_index SET lvChangeTime=NOW()
                      WHERE idx=@idx AND
                        (Level<>@level OR ForceLv<>@forceLevel OR sfLevel<>@sfLevel)",
                    conn))
                {
                    changed.Parameters.AddWithValue("@idx", idx);
                    changed.Parameters.AddWithValue("@level", persistence.Level);
                    changed.Parameters.AddWithValue("@forceLevel", forceLevel);
                    changed.Parameters.AddWithValue("@sfLevel", persistence.SfLevel);
                    changed.ExecuteNonQuery();
                }

                using (var update = new MySqlCommand(
                    @"UPDATE mir3.user_index SET
                        PTID=@account, IsDelete=@isDelete, IsSelect=@isSelect,
                        Level=@level, Job=@job, Sex=@sex, ModifyDate=NOW(), Exp=@exp,
                        ApprenticeNum=@apprenticeNum, HeroCardLv=@heroCardLevel,
                        PlatinaChrLv=@platinaCharacterLevel,
                        ForceLv=@forceLevel, ForceExp=@forceExperience,
                        FightPoints=0, sfLevel=@sfLevel
                      WHERE idx=@idx AND ChrName=@name", conn))
                {
                    update.Parameters.Add(LegacyGbkText.Parameter(
                        "@account", persistence.Account));
                    update.Parameters.AddWithValue("@isDelete", isDelete);
                    update.Parameters.AddWithValue("@isSelect", isSelect);
                    update.Parameters.AddWithValue("@level", persistence.Level);
                    update.Parameters.AddWithValue("@job", persistence.Job);
                    update.Parameters.AddWithValue("@sex", persistence.Sex);
                    update.Parameters.Add("@exp", MySqlDbType.UInt32).Value =
                        persistence.Experience;
                    update.Parameters.AddWithValue(
                        "@apprenticeNum", persistence.ApprenticeNum);
                    update.Parameters.AddWithValue(
                        "@heroCardLevel", persistence.HeroCardLevel);
                    update.Parameters.AddWithValue(
                        "@platinaCharacterLevel", persistence.PlatinaCharacterLevel);
                    update.Parameters.AddWithValue("@forceLevel", forceLevel);
                    update.Parameters.Add("@forceExperience", MySqlDbType.UInt32).Value =
                        forceExperience;
                    update.Parameters.AddWithValue("@sfLevel", persistence.SfLevel);
                    update.Parameters.AddWithValue("@idx", idx);
                    update.Parameters.Add(LegacyGbkText.Parameter(
                        "@name", persistence.CharacterName));
                    if (update.ExecuteNonQuery() > 0)
                    {
                        UpdateNativeType3Record(idx, persistence.Account, record =>
                        {
                            record.DeleteState = NativeType3Protocol.NormalizeDeleteState(
                                isDelete);
                            record.IsDelete = record.DeleteState != 0;
                            record.IsSelect = isSelect != 0;
                            record.Level = persistence.Level;
                            record.Exp = unchecked((int)persistence.Experience);
                            record.Job = persistence.Job;
                            record.Sex = persistence.Sex;
                            record.ModifyDate = DateTime.Now;
                        });
                        return true;
                    }
                }

                using var exists = new MySqlCommand(
                    @"SELECT COUNT(*) FROM mir3.user_index
                      WHERE idx=@idx AND PTID=@account AND ChrName=@name", conn);
                exists.Parameters.AddWithValue("@idx", idx);
                exists.Parameters.Add(LegacyGbkText.Parameter(
                    "@account", persistence.Account));
                exists.Parameters.Add(LegacyGbkText.Parameter(
                    "@name", persistence.CharacterName));
                var found = Convert.ToInt32(exists.ExecuteScalar()) == 1;
                if (found)
                {
                    UpdateNativeType3Record(idx, persistence.Account, record =>
                    {
                        record.DeleteState = NativeType3Protocol.NormalizeDeleteState(
                            isDelete);
                        record.IsDelete = record.DeleteState != 0;
                        record.IsSelect = isSelect != 0;
                        record.Level = persistence.Level;
                        record.Exp = unchecked((int)persistence.Experience);
                        record.Job = persistence.Job;
                        record.Sex = persistence.Sex;
                    });
                }
                return found;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[NativeSaveIndex] ERR idx={idx}: {ex.Message}");
                return false;
            }
        }

        public NativeForceLevelStoreAttempt ApplyNativeForceLevel(
            byte[] characterName, ushort forceLevel)
        {
            characterName ??= Array.Empty<byte>();
            try
            {
                using var conn = OpenConnection();
                if (conn == null)
                    return new NativeForceLevelStoreAttempt(
                        NativeForceLevelStoreResult.LoadFailed);

                var key = NativeForceLevelProtocol.NormalizeCharacterNameKey(
                    characterName);
                if (!NativeRawNameIndex.TryGetValue(key, out var index))
                {
                    using var find = new MySqlCommand(
                        "SELECT idx FROM mir3.user_index WHERE ChrName=@name LIMIT 1",
                        conn);
                    find.Parameters.Add("@name", MySqlDbType.Binary).Value =
                        characterName;
                    var value = find.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                        return new NativeForceLevelStoreAttempt(
                            NativeForceLevelStoreResult.Missing);
                    index = Convert.ToInt32(value);
                    NativeRawNameIndex[key] = index;
                }

                using var cmd = new MySqlCommand(
                    @"SELECT i.IsDelete, d.Idx AS DataIdx, d.Status,
                             d.Data, d.ScriptData
                      FROM mir3.user_index AS i
                      LEFT JOIN mir3.user_data AS d ON d.Idx=i.idx
                      WHERE i.idx=@idx LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@idx", index);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return new NativeForceLevelStoreAttempt(
                        NativeForceLevelStoreResult.Missing);
                if (reader.GetInt32("IsDelete") != 0)
                    return new NativeForceLevelStoreAttempt(
                        NativeForceLevelStoreResult.Deleted);

                var dataOrdinal = reader.GetOrdinal("DataIdx");
                if (reader.IsDBNull(dataOrdinal)
                    || reader.IsDBNull(reader.GetOrdinal("Status"))
                    || reader.GetInt32("Status") != 0)
                {
                    NativeForceLevels[index] = forceLevel;
                    return new NativeForceLevelStoreAttempt(
                        NativeForceLevelStoreResult.UpdatedWithoutSaveTarget);
                }

                var data = reader["Data"] as byte[];
                var script = reader["ScriptData"] as byte[];
                if (!NativeHumanDataCodec.TryDecode(data, script, out _, out _))
                    return new NativeForceLevelStoreAttempt(
                        NativeForceLevelStoreResult.LoadFailed);

                NativeForceLevels[index] = forceLevel;
                return new NativeForceLevelStoreAttempt(
                    NativeForceLevelStoreResult.Queued,
                    new NativeForceLevelMutation
                    {
                        Target = NativeForceLevelTarget.Player,
                        Index = index,
                        ForceLevel = forceLevel,
                        CharacterNameBytes = (byte[])characterName.Clone()
                    });
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    "[NativeForceLevel] player load failed: " + ex.Message);
                return new NativeForceLevelStoreAttempt(
                    NativeForceLevelStoreResult.LoadFailed);
            }
        }

        public bool PersistNativeForceLevel(int idx, ushort forceLevel)
        {
            if (idx <= 0) return false;
            using var conn = OpenConnection();
            if (conn == null) return false;
            try
            {
                using (var update = new MySqlCommand(
                           @"UPDATE mir3.user_index
                             SET lvChangeTime=IF(ForceLv<>@forceLevel,
                                 NOW(),lvChangeTime),
                                 ForceLv=@forceLevel, ModifyDate=NOW()
                             WHERE idx=@idx", conn))
                {
                    update.Parameters.AddWithValue("@forceLevel", forceLevel);
                    update.Parameters.AddWithValue("@idx", idx);
                    if (update.ExecuteNonQuery() > 0) return true;
                }
                using var exists = new MySqlCommand(
                    "SELECT COUNT(*) FROM mir3.user_index WHERE idx=@idx", conn);
                exists.Parameters.AddWithValue("@idx", idx);
                return Convert.ToInt32(exists.ExecuteScalar()) == 1;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[NativeForceLevel] player persist idx={idx}: {ex.Message}");
                return false;
            }
        }

        public bool UpdateLvChangeTime(int idx, int oldLevel, int oldForceLv, int oldSfLevel)
        {
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                @"UPDATE mir3.user_index SET lvChangeTime=NOW()
                  WHERE idx=@idx AND
                        (Level<>@oldLevel OR ForceLv<>@oldForceLv OR sfLevel<>@oldSfLevel)", conn);
            cmd.Parameters.AddWithValue("@idx", idx);
            cmd.Parameters.AddWithValue("@oldLevel", oldLevel);
            cmd.Parameters.AddWithValue("@oldForceLv", oldForceLv);
            cmd.Parameters.AddWithValue("@oldSfLevel", oldSfLevel);
            return cmd.ExecuteNonQuery() > 0;
        }

        // ===================== 删除 =====================

        public bool Delete(string sName)
        {
            // 软删除: UPDATE user_index SET IsDelete=1
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                "UPDATE mir3.user_index SET IsDelete=1, ModifyDate=NOW() WHERE ChrName=@name", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@name", sName));
            var result = cmd.ExecuteNonQuery() > 0;

            if (result)
            {
                if (QuickList.TryRemove(sName, out var idx))
                    IndexQuickList.TryRemove(idx, out _);
                SetNativeType3DeleteState(sName, 1);
                // 同步标记 user_data
                using var cmd2 = new MySqlCommand(
                    "UPDATE mir3.user_data SET Status=1 WHERE ChrName=@name", conn);
                cmd2.Parameters.Add(LegacyGbkText.Parameter("@name", sName));
                cmd2.ExecuteNonQuery();
            }
            return result;
        }

        public bool HardDelete(int idx)
        {
            lock (NativeIdentityMutationLock)
                return HardDeleteCore(idx);
        }

        private bool HardDeleteCore(int idx)
        {
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd1 = new MySqlCommand(
                    "DELETE FROM mir3.user_data WHERE idx=@idx", conn, tx);
                cmd1.Parameters.AddWithValue("@idx", idx);
                cmd1.ExecuteNonQuery();

                using var cmd2 = new MySqlCommand(
                    "DELETE FROM mir3.user_index WHERE idx=@idx", conn, tx);
                cmd2.Parameters.AddWithValue("@idx", idx);
                cmd2.ExecuteNonQuery();

                tx.Commit();

                if (IndexQuickList.TryGetValue(idx, out string chrName))
                {
                    QuickList.TryRemove(chrName, out _);
                    IndexQuickList.TryRemove(idx, out _);
                }
                RemoveNativeType3Record(idx);
                return true;
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                DBShare.MainOutMessage($"[HardDelete] ERR idx={idx}: {ex.Message}");
                return false;
            }
        }

        // ===================== 排行榜 =====================

        public List<RankEntry> GetLevelRank(int limit = 100)
        {
            var list = new List<RankEntry>();
            using var conn = OpenConnection();
            if (conn == null) return list;

            using var cmd = new MySqlCommand(
                @"SELECT ChrName, Level, sfLevel, ForceLv, Exp, FightPoints, ApprenticeNum
                  FROM mir3.user_index
                  WHERE IsDelete=0 AND Level>0 AND AdminLevel=0
                    AND ModifyDate > DATE_SUB(NOW(), INTERVAL @days DAY)
                  ORDER BY Level DESC, sfLevel DESC, ForceLv DESC, Exp DESC, lvChangeTime
                  LIMIT @limit", conn);
            cmd.Parameters.AddWithValue("@days", DBShare.RankingActiveDays);
            cmd.Parameters.AddWithValue("@limit", Math.Min(limit, DBShare.RankLimit));
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(ReadRankEntry(dr));
            }
            return list;
        }

        public List<RankEntry> GetLevelRankByJob(int job, int limit = 100)
        {
            var list = new List<RankEntry>();
            using var conn = OpenConnection();
            if (conn == null) return list;

            using var cmd = new MySqlCommand(
                @"SELECT ChrName, Level, sfLevel, ForceLv, Exp, FightPoints, ApprenticeNum
                  FROM mir3.user_index
                  WHERE IsDelete=0 AND Level>0 AND AdminLevel=0 AND Job=@job
                    AND ModifyDate > DATE_SUB(NOW(), INTERVAL @days DAY)
                  ORDER BY Level DESC, sfLevel DESC, ForceLv DESC, Exp DESC, lvChangeTime
                  LIMIT @limit", conn);
            cmd.Parameters.AddWithValue("@job", job);
            cmd.Parameters.AddWithValue("@days", DBShare.RankingActiveDays);
            cmd.Parameters.AddWithValue("@limit", Math.Min(limit, DBShare.RankLimit));
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(ReadRankEntry(dr));
            }
            return list;
        }

        public List<RankEntry> GetFightPowerRank(int limit = 100)
        {
            var list = new List<RankEntry>();
            using var conn = OpenConnection();
            if (conn == null) return list;

            using var cmd = new MySqlCommand(
                @"SELECT ChrName, Level, sfLevel, ForceLv, Exp, FightPoints, ApprenticeNum
                  FROM mir3.user_index
                  WHERE IsDelete=0 AND FightPoints>0 AND AdminLevel=0
                    AND ModifyDate > DATE_SUB(NOW(), INTERVAL @days DAY)
                  ORDER BY FightPoints DESC, Level DESC, Exp DESC
                  LIMIT @limit", conn);
            cmd.Parameters.AddWithValue("@days", DBShare.RankingActiveDays);
            cmd.Parameters.AddWithValue("@limit", Math.Min(limit, DBShare.RankLimit));
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(ReadRankEntry(dr));
            }
            return list;
        }

        public List<RankEntry> GetForceRank(int limit = 100)
        {
            var list = new List<RankEntry>();
            using var conn = OpenConnection();
            if (conn == null) return list;

            using var cmd = new MySqlCommand(
                @"SELECT ChrName, Level, sfLevel, ForceLv, Exp, FightPoints, ApprenticeNum
                  FROM mir3.user_index
                  WHERE IsDelete=0 AND ForceLv>0 AND AdminLevel=0
                    AND ModifyDate > DATE_SUB(NOW(), INTERVAL @days DAY)
                  ORDER BY ForceLv DESC, Level DESC, Exp DESC
                  LIMIT @limit", conn);
            cmd.Parameters.AddWithValue("@days", DBShare.RankingActiveDays);
            cmd.Parameters.AddWithValue("@limit", Math.Min(limit, DBShare.RankLimit));
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(ReadRankEntry(dr));
            }
            return list;
        }

        // ===================== 分页 =====================

        public List<ChrSortEntry> GetCharacterPage(int lastIdx, int limit = 5000)
        {
            var list = new List<ChrSortEntry>();
            using var conn = OpenConnection();
            if (conn == null) return list;

            using var cmd = new MySqlCommand(
                @"SELECT Idx, PTID, ChrName, IsDelete, IsSelect, Job, Sex, Level, Exp,
                         PlatinaChrLv, HeroCardLv, ForceLv, ForceExp, FightPoints, sfLevel,
                         UserId, ModifyDate, ApprenticeNum, AdminLevel, SrcZoneId,
                         SrcGroupId, IsTransLock, transferModal, lvChangeTime,
                         GuardNum, DarePoint, SrcCharName
                  FROM mir3.user_index
                  WHERE Idx > @lastIdx
                  ORDER BY Idx LIMIT @limit", conn);
            cmd.Parameters.AddWithValue("@lastIdx", lastIdx);
            cmd.Parameters.AddWithValue("@limit", Math.Min(limit, DBShare.BatchLimit));
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(new ChrSortEntry
                {
                    Idx = dr.GetInt32("Idx"),
                    PTID = dr.GetString("PTID"),
                    ChrName = ReadChrName(dr),
                    IsDelete = dr.GetInt32("IsDelete") != 0,
                    IsSelect = dr.GetInt32("IsSelect") != 0,
                    Job = dr.GetInt32("Job"),
                    Sex = dr.GetInt32("Sex"),
                    Level = dr.GetInt32("Level"),
                    Exp = ReadExpBits(dr),
                    PlatinaChrLv = dr.IsDBNull(dr.GetOrdinal("PlatinaChrLv")) ? 0 : dr.GetInt32("PlatinaChrLv"),
                    HeroCardLv = dr.IsDBNull(dr.GetOrdinal("HeroCardLv")) ? 0 : dr.GetInt32("HeroCardLv"),
                    ForceLv = dr.IsDBNull(dr.GetOrdinal("ForceLv")) ? 0 : dr.GetInt32("ForceLv"),
                    ForceExp = dr.IsDBNull(dr.GetOrdinal("ForceExp")) ? 0 : dr.GetInt32("ForceExp"),
                    FightPoints = dr.IsDBNull(dr.GetOrdinal("FightPoints")) ? 0 : dr.GetInt32("FightPoints"),
                    SfLevel = dr.IsDBNull(dr.GetOrdinal("sfLevel")) ? 0 : dr.GetInt32("sfLevel"),
                    UserId = dr.IsDBNull(dr.GetOrdinal("UserId")) ? 0 : dr.GetInt64("UserId"),
                    ModifyDate = dr.GetDateTime("ModifyDate"),
                    ApprenticeNum = dr.IsDBNull(dr.GetOrdinal("ApprenticeNum")) ? 0 : dr.GetInt32("ApprenticeNum"),
                    AdminLevel = dr.IsDBNull(dr.GetOrdinal("AdminLevel")) ? 0 : dr.GetInt32("AdminLevel"),
                    SrcZoneId = dr.IsDBNull(dr.GetOrdinal("SrcZoneId")) ? 0 : dr.GetInt32("SrcZoneId"),
                    SrcGroupId = dr.IsDBNull(dr.GetOrdinal("SrcGroupId")) ? 0 : dr.GetInt32("SrcGroupId"),
                    IsTransLock = dr.IsDBNull(dr.GetOrdinal("IsTransLock")) ? false : dr.GetInt32("IsTransLock") != 0,
                    TransferModal = dr.IsDBNull(dr.GetOrdinal("transferModal")) ? 0 : dr.GetInt32("transferModal"),
                    LvChangeTime = dr.IsDBNull(dr.GetOrdinal("lvChangeTime")) ? DateTime.MinValue : dr.GetDateTime("lvChangeTime"),
                    GuardNum = dr.IsDBNull(dr.GetOrdinal("GuardNum")) ? 0 : dr.GetInt32("GuardNum"),
                    DarePoint = dr.IsDBNull(dr.GetOrdinal("DarePoint")) ? 0 : dr.GetInt32("DarePoint"),
                    SrcCharName = LegacyGbkText.Read(dr, "SrcCharName")
                });
            }
            return list;
        }

        // ===================== 辅助方法 =====================

        private static MySqlConnection OpenConnection()
        {
            try
            {
                var conn = new MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using(var sc = new MySqlCommand("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; SET SESSION wait_timeout=2073600", conn))
                    sc.ExecuteNonQuery();
                return conn;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[MySqlPlayRecord] 连接失败: {ex.Message}");
                return null;
            }
        }

        private static HumRecordData ReadHumRecord(MySqlDataReader dr)
        {
            var chrName = ReadChrName(dr);
            return new HumRecordData
            {
                Id = dr.GetInt32("idx"),
                sAccount = dr.GetString("PTID"),
                sChrName = chrName,
                boSelected = (byte)dr.GetInt32("IsSelect"),
                boDeleted = dr.GetInt32("IsDelete") != 0,
                Header = new TRecordHeader
                {
                    sName = chrName,
                    sAccount = dr.GetString("PTID"),
                    nSelectID = dr.GetInt32("IsSelect"),
                    boDeleted = dr.GetInt32("IsDelete") != 0
                }
            };
        }

        private static string ReadChrName(MySqlDataReader dr)
        {
            return LegacyGbkText.Read(dr, "ChrName");
        }

        private static byte[] ReadAnsiBytes(MySqlDataReader dr, string name)
        {
            var ordinal = dr.GetOrdinal(name);
            if (dr.IsDBNull(ordinal)) return Array.Empty<byte>();
            var value = dr.GetValue(ordinal);
            if (value is byte[] bytes) return (byte[])bytes.Clone();
            return Encoding.Latin1.GetBytes(dr.GetString(ordinal));
        }

        private static RankEntry ReadRankEntry(MySqlDataReader dr)
        {
            return new RankEntry
            {
                ChrName = ReadChrName(dr),
                Level = dr.GetInt32("Level"),
                SfLevel = dr.IsDBNull(dr.GetOrdinal("sfLevel")) ? 0 : dr.GetInt32("sfLevel"),
                ForceLv = dr.IsDBNull(dr.GetOrdinal("ForceLv")) ? 0 : dr.GetInt32("ForceLv"),
                Exp = ReadExpBits(dr),
                FightPoints = dr.IsDBNull(dr.GetOrdinal("FightPoints")) ? 0 : dr.GetInt32("FightPoints"),
                ApprenticeNum = dr.IsDBNull(dr.GetOrdinal("ApprenticeNum")) ? 0 : dr.GetInt32("ApprenticeNum")
            };
        }

        private static int ReadExpBits(MySqlDataReader dr) =>
            unchecked((int)Convert.ToUInt32(dr["Exp"]));

        private void AddNativeType3Record(ChrIndexInfo record)
        {
            lock (NativeType3CacheLock)
            {
                if (NativeType3ByIndex.TryGetValue(record.Idx, out var oldRecord)
                    && NativeType3ByPtid.TryGetValue(
                        GetNativePtidKey(oldRecord),
                        out var oldRecords))
                    oldRecords.Remove(oldRecord);

                var ptid = GetNativePtidKey(record);
                if (!NativeType3ByPtid.TryGetValue(ptid, out var records))
                {
                    records = new List<ChrIndexInfo>();
                    NativeType3ByPtid.Add(ptid, records);
                }
                records.Add(record);
                NativeType3ByIndex[record.Idx] = record;
            }
        }

        private void UpdateNativeType3Record(int index, string newPtid,
            Action<ChrIndexInfo> update)
        {
            lock (NativeType3CacheLock)
            {
                if (!NativeType3ByIndex.TryGetValue(index, out var record)) return;
                var oldPtid = GetNativePtidKey(record);
                update(record);
                if (newPtid == null) return;
                var newPtidBytes = NativeType3Protocol.EncodeAnsi(newPtid);
                var newPtidKey = NativeType3Protocol.NormalizePtidKey(newPtidBytes);
                if (string.Equals(oldPtid, newPtidKey, StringComparison.Ordinal))
                {
                    record.PTID = newPtid;
                    record.PTIDBytes = newPtidBytes;
                    return;
                }

                if (NativeType3ByPtid.TryGetValue(oldPtid, out var oldRecords))
                    oldRecords.Remove(record);
                record.PTID = newPtid;
                record.PTIDBytes = newPtidBytes;
                if (!NativeType3ByPtid.TryGetValue(newPtidKey, out var newRecords))
                {
                    newRecords = new List<ChrIndexInfo>();
                    NativeType3ByPtid.Add(newPtidKey, newRecords);
                }
                newRecords.Add(record);
            }
        }

        private void SetNativeType3DeleteState(string characterName, int state)
        {
            lock (NativeType3CacheLock)
            {
                foreach (var record in NativeType3ByIndex.Values)
                {
                    if (!string.Equals(record.ChrName, characterName,
                            StringComparison.Ordinal)) continue;
                    record.DeleteState = state;
                    record.IsDelete = state != 0;
                    return;
                }
            }
        }

        private void RemoveNativeType3Record(int index)
        {
            lock (NativeType3CacheLock)
            {
                if (!NativeType3ByIndex.Remove(index, out var record)) return;
                if (NativeType3ByPtid.TryGetValue(
                        GetNativePtidKey(record),
                        out var records))
                    records.Remove(record);
                QuickList.TryRemove(record.ChrName, out _);
                IndexQuickList.TryRemove(index, out _);
                var nameKey = NativeForceLevelProtocol
                    .NormalizeCharacterNameKey(record.ChrNameBytes);
                if (NativeRawNameIndex.TryGetValue(nameKey, out var mapped)
                    && mapped == index)
                    NativeRawNameIndex.TryRemove(nameKey, out _);
                NativeOccupiedNames.TryRemove(nameKey, out _);
                NativeForceLevels.TryRemove(index, out _);
            }
        }

        private void RenameNativeType3Account(byte[] oldPtid, byte[] newPtid,
            string newPtidText)
        {
            var oldKey = NativeType3Protocol.NormalizePtidKey(oldPtid);
            var newKey = NativeType3Protocol.NormalizePtidKey(newPtid);
            lock (NativeType3CacheLock)
            {
                if (!NativeType3ByPtid.TryGetValue(oldKey, out var records))
                    return;
                NativeType3ByPtid.Remove(oldKey);
                if (!NativeType3ByPtid.TryGetValue(newKey, out var destination))
                {
                    destination = new List<ChrIndexInfo>();
                    NativeType3ByPtid.Add(newKey, destination);
                }
                foreach (var record in records)
                {
                    record.PTID = newPtidText;
                    record.PTIDBytes = (byte[])newPtid.Clone();
                    destination.Add(record);
                }
            }
        }

        private static ChrIndexInfo CloneNativeType3Record(ChrIndexInfo record) =>
            new()
            {
                Idx = record.Idx,
                PTID = record.PTID,
                PTIDBytes = record.PTIDBytes == null
                    ? Array.Empty<byte>() : (byte[])record.PTIDBytes.Clone(),
                ChrName = record.ChrName,
                ChrNameBytes = record.ChrNameBytes == null
                    ? Array.Empty<byte>() : (byte[])record.ChrNameBytes.Clone(),
                Job = record.Job,
                Sex = record.Sex,
                Level = record.Level,
                Exp = record.Exp,
                UserId = record.UserId,
                IsTransLock = record.IsTransLock,
                DestinationZoneId = record.DestinationZoneId,
                DestinationGroupId = record.DestinationGroupId,
                NativeBusy = record.NativeBusy,
                DeleteState = record.DeleteState,
                IsDelete = record.IsDelete,
                IsSelect = record.IsSelect,
                ModifyDate = record.ModifyDate
            };

        private static string GetNativePtidKey(ChrIndexInfo record) =>
            NativeType3Protocol.NormalizePtidKey(
                record.PTIDBytes is { Length: > 0 }
                    ? record.PTIDBytes
                    : NativeType3Protocol.EncodeAnsi(record.PTID));
    }
}
