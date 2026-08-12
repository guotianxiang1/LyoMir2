using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SystemModule;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 角色存档 MySQL 实现。
    /// 对应 Delphi 原版 user_data 表 (Data Blob + ScriptData Blob)。
    /// Data/ScriptData use the native Gs1 CRC + zlib + 256-byte aligned envelope.
    /// </summary>
    public class MySqlPlayDataService : IPlayDataService
    {
        private readonly ConcurrentDictionary<string, int> MirQuickList;       // ChrName → Idx
        private readonly ConcurrentDictionary<int, int> QuickIndexIdList;       // Idx → idx (映射)
        private int _recordCount;

        public MySqlPlayDataService()
        {
            MirQuickList = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            QuickIndexIdList = new ConcurrentDictionary<int, int>();
            _recordCount = -1;
        }

        // ===================== 初始化 =====================

        public void LoadQuickList()
        {
            MirQuickList.Clear();
            QuickIndexIdList.Clear();

            using var conn = OpenConnection();
            if (conn == null) return;

            using var cmd = new MySqlCommand(
                @"SELECT d.Idx, i.ChrName, d.Status
                  FROM mir3.user_data d
                  INNER JOIN mir3.user_index i ON i.idx=d.Idx
                  WHERE i.IsDelete=0", conn);
            using var dr = cmd.ExecuteReader();
            int nIndex = 0;
            while (dr.Read())
            {
                int idx = dr.GetInt32("Idx");
                int status = dr.GetInt32("Status");
                string chrName = ReadGbkName(dr, "ChrName");

                if (status == 0 && !string.IsNullOrEmpty(chrName)) // 非删除状态
                {
                    MirQuickList[chrName] = idx;
                    QuickIndexIdList[idx] = idx;
                    nIndex++;
                }
            }
            _recordCount = nIndex;
        }

        // ===================== 查询 =====================

        public int Index(string sName)
        {
            if (string.IsNullOrEmpty(sName)) return -1;
            if (MirQuickList.TryGetValue(sName, out int idx)) return idx;

            using var conn = OpenConnection();
            if (conn == null) return -1;
            using var cmd = new MySqlCommand(
                @"SELECT d.Idx
                  FROM mir3.user_data d
                  INNER JOIN mir3.user_index i ON i.idx=d.Idx
                  WHERE i.ChrName=@name AND i.IsDelete=0 AND d.Status=0 LIMIT 1", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@name", sName));
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value) return -1;

            idx = Convert.ToInt32(value);
            MirQuickList[sName] = idx;
            QuickIndexIdList[idx] = idx;
            return idx;
        }

        public void RegisterNativeIndex(int index, string characterName)
        {
            if (index <= 0 || string.IsNullOrEmpty(characterName)) return;
            MirQuickList[characterName] = index;
            QuickIndexIdList[index] = index;
            _recordCount = -1;
        }

        public void UnregisterNativeIndex(int index)
        {
            if (index <= 0) return;
            RemoveQuickIndex(index);
            _recordCount = -1;
        }

        public int Get(int nIndex, ref THumDataInfo HumanRCD)
        {
            using var conn = OpenConnection();
            if (conn == null) return -1;

            // user_index owns character metadata that the native DBServer injects into
            // the record returned to M2. Read it in the same snapshot as the data blob.
            using var cmd = new MySqlCommand(
                @"SELECT d.Data, d.ScriptData, i.Job, i.Sex, i.CreateDate, i.UserId
                  FROM mir3.user_data d
                  INNER JOIN mir3.user_index i ON i.Idx=d.Idx
                  WHERE d.Idx=@idx AND i.IsDelete=0 AND d.Status=0 LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@idx", nIndex);
            using var dr = cmd.ExecuteReader();
            if (!dr.Read()) return -1;

            byte[] dataBlob = dr["Data"] as byte[];
            byte[] scriptBlob = dr["ScriptData"] as byte[];

            if (!TryDecodeRecord(dataBlob, scriptBlob, out HumanRCD, out var decodeError))
            {
                HumanRCD = null;
                DBShare.MainOutMessage($"[MySqlPlayData] 存档读取失败 idx={nIndex}: {decodeError}");
                return -1;
            }
            NormalizeRecord(HumanRCD);
            if (!TryApplyIndexMetadata(dr, HumanRCD, out var metadataError))
            {
                HumanRCD = null;
                DBShare.MainOutMessage(
                    $"[MySqlPlayData] 角色索引元数据读取失败 idx={nIndex}: {metadataError}");
                return -1;
            }
            return 1;
        }

        public bool GetByChrName(string sChrName, ref THumDataInfo HumanRCD)
        {
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                @"SELECT d.Idx, d.Data, d.ScriptData, i.Job, i.Sex, i.CreateDate, i.UserId
                  FROM mir3.user_data d
                  INNER JOIN mir3.user_index i ON i.idx=d.Idx
                  WHERE i.ChrName=@name AND i.IsDelete=0 AND d.Status=0 LIMIT 1", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@name", sChrName));
            using var dr = cmd.ExecuteReader();
            if (!dr.Read()) return false;

            byte[] dataBlob = dr["Data"] as byte[];
            byte[] scriptBlob = dr["ScriptData"] as byte[];
            int idx = dr.GetInt32("Idx");

            if (!TryDecodeRecord(dataBlob, scriptBlob, out HumanRCD, out var decodeError))
            {
                HumanRCD = null;
                DBShare.MainOutMessage($"[MySqlPlayData] 存档读取失败 chr={sChrName} idx={idx}: {decodeError}");
                return false;
            }
            NormalizeRecord(HumanRCD);
            if (!TryApplyIndexMetadata(dr, HumanRCD, out var metadataError))
            {
                HumanRCD = null;
                DBShare.MainOutMessage(
                    $"[MySqlPlayData] 角色索引元数据读取失败 chr={sChrName} idx={idx}: {metadataError}");
                return false;
            }
            return true;
        }

        public bool ResetDeletedFlagByChrName(string sChrName, bool boDeleted)
        {
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                "UPDATE mir3.user_data SET Status=@status WHERE ChrName=@name", conn);
            cmd.Parameters.AddWithValue("@status", boDeleted ? 1 : 0);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@name", sChrName));
            cmd.ExecuteNonQuery();

            // 同步更新 user_index
            using var cmd2 = new MySqlCommand(
                "UPDATE mir3.user_index SET IsDelete=@del WHERE ChrName=@name", conn);
            cmd2.Parameters.AddWithValue("@del", boDeleted ? 1 : 0);
            cmd2.Parameters.Add(LegacyGbkText.Parameter("@name", sChrName));
            cmd2.ExecuteNonQuery();
            return true;
        }

        public int GetQryChar(int nIndex, ref TQueryChr QueryChrRcd)
        {
            using var conn = OpenConnection();
            if (conn == null) return -1;

            using var cmd = new MySqlCommand(
                "SELECT ChrName, Job, Sex, Level FROM mir3.user_index WHERE Idx=@idx LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@idx", nIndex);
            using var dr = cmd.ExecuteReader();
            if (!dr.Read()) return -1;

            QueryChrRcd = new TQueryChr
            {
                sName = LegacyGbkText.Read(dr, "ChrName"),
                btJob = (byte)dr.GetInt32("Job"),
                btSex = (byte)dr.GetInt32("Sex"),
                wLevel = (ushort)dr.GetInt32("Level")
            };
            return 1;
        }

        public int Count()
        {
            if (_recordCount >= 0) return _recordCount;
            using var conn = OpenConnection();
            if (conn == null) return 0;

            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM mir3.user_data WHERE Status=0", conn);
            _recordCount = Convert.ToInt32(cmd.ExecuteScalar());
            return _recordCount;
        }

        // ===================== Blob 操作 =====================

        public (byte[] data, byte[] scriptData) LoadBlob(int idx)
        {
            using var conn = OpenConnection();
            if (conn == null) return (null, null);

            using var cmd = new MySqlCommand(
                "SELECT HIGH_PRIORITY Data, ScriptData FROM mir3.user_data WHERE Idx=@idx", conn);
            cmd.Parameters.AddWithValue("@idx", idx);
            using var dr = cmd.ExecuteReader();
            if (!dr.Read()) return (null, null);

            byte[] data = dr["Data"] as byte[];
            byte[] script = dr["ScriptData"] as byte[];

            if (NativeHumanDataCodec.LooksLikeNativeDataBlob(data)
                && NativeHumanDataCodec.TryDecode(data, script, out var native, out _))
                return (native.NativeData, native.NativeScriptData);
            if (data != null && data.Length > 0) data = BlobCompressor.TryDecompress(data);
            if (script != null && script.Length > 0) script = BlobCompressor.TryDecompress(script);

            return (data, script);
        }

        public bool SaveBlob(int idx, byte[] data, byte[] scriptData = null)
        {
            if (data == null) return false;

            using var conn = OpenConnection();
            if (conn == null) return false;

            try
            {
                var humanRcd = ProtoBufDecoder.DeSerialize<THumDataInfo>(data);
                if (!NativeHumanDataCodec.TryEncode(humanRcd, out var nativeData,
                        out var nativeScript, out var codecError))
                {
                    DBShare.MainOutMessage($"[SaveBlob] REJECT idx={idx}: {codecError}");
                    return false;
                }

                var sql = new StringBuilder("UPDATE mir3.user_data SET Data=UNHEX(@data)");
                var parameters = new List<MySqlParameter>
                {
                    new MySqlParameter("@data", MySqlDbType.LongText)
                    {
                        Value = Convert.ToHexString(nativeData)
                    },
                    new MySqlParameter("@idx", idx)
                };

                if (nativeScript != null)
                {
                    sql.Append(", ScriptData=UNHEX(@script)");
                    parameters.Add(new MySqlParameter("@script", MySqlDbType.LongText)
                    {
                        Value = Convert.ToHexString(nativeScript)
                    });
                }
                sql.Append(" WHERE Idx=@idx");

                using var cmd = new MySqlCommand(sql.ToString(), conn);
                foreach (var p in parameters) cmd.Parameters.Add(p);
                var affected = cmd.ExecuteNonQuery();

                if (affected <= 0)
                    DBShare.MainOutMessage($"[SaveBlob] MISS idx={idx}");
                return affected > 0;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[SaveBlob] ERR idx={idx}: {ex.Message}");
                return false;
            }
        }

        public bool SaveNativeBlobExact(int idx, NativeSavePersistenceData persistence)
        {
            if (idx <= 0 || persistence == null
                || persistence.DataBlob?.Length != NativeHumanDataCodec.DataSizeMarker
                || persistence.ScriptDataBlob == null
                || persistence.ScriptDataBlob.Length != 0
                && (persistence.ScriptDataBlob.Length < 0x100
                    || persistence.ScriptDataBlob.Length % 0x100 != 0))
                return false;

            using var conn = OpenConnection();
            if (conn == null) return false;

            try
            {
                using (var cmd = new MySqlCommand(
                    @"UPDATE mir3.user_data
                      SET Data=@data, ScriptData=@script
                      WHERE Idx=@idx", conn))
                {
                    cmd.Parameters.Add("@data", MySqlDbType.LongBlob).Value =
                        persistence.DataBlob;
                    cmd.Parameters.Add("@script", MySqlDbType.LongBlob).Value =
                        persistence.ScriptDataBlob;
                    cmd.Parameters.AddWithValue("@idx", idx);
                    if (cmd.ExecuteNonQuery() > 0) return true;
                }

                using (var exists = new MySqlCommand(
                    "SELECT COUNT(*) FROM mir3.user_data WHERE Idx=@idx", conn))
                {
                    exists.Parameters.AddWithValue("@idx", idx);
                    if (Convert.ToInt32(exists.ExecuteScalar()) == 1)
                        return true;
                }

                using (var duplicate = new MySqlCommand(
                    @"SELECT Idx FROM mir3.user_data
                      WHERE ChrName=@name AND Idx<>@idx LIMIT 1", conn))
                {
                    duplicate.Parameters.Add(LegacyGbkText.Parameter(
                        "@name", persistence.CharacterName));
                    duplicate.Parameters.AddWithValue("@idx", idx);
                    var duplicateIdx = duplicate.ExecuteScalar();
                    if (duplicateIdx != null && duplicateIdx != DBNull.Value)
                    {
                        DBShare.MainOutMessage(
                            $"[NativeSaveBlob] duplicate chr={persistence.CharacterName} " +
                            $"idx={Convert.ToInt32(duplicateIdx)} expected={idx}");
                    }
                }

                using var insert = new MySqlCommand(
                    @"INSERT IGNORE INTO mir3.user_data(Idx, ChrName)
                      VALUES(@idx, @name)", conn);
                insert.Parameters.AddWithValue("@idx", idx);
                insert.Parameters.Add(LegacyGbkText.Parameter(
                    "@name", persistence.CharacterName));
                insert.ExecuteNonQuery();
                return false;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[NativeSaveBlob] ERR idx={idx}: {ex.Message}");
                return false;
            }
        }

        public bool CreateDataRow(int idx, string chrName)
        {
            if (idx <= 0 || string.IsNullOrEmpty(chrName)) return false;
            using var conn = OpenConnection();
            if (conn == null) return false;

            var nameBytes = LegacyGbkText.Encode(chrName);
            using (var verify = new MySqlCommand(
                "SELECT COUNT(*) FROM mir3.user_index WHERE idx=@idx AND ChrName=@name", conn))
            {
                verify.Parameters.AddWithValue("@idx", idx);
                verify.Parameters.Add("@name", MySqlDbType.Binary).Value = nameBytes;
                if (Convert.ToInt32(verify.ExecuteScalar()) != 1) return false;
            }

            using var cmd = new MySqlCommand(
                "INSERT IGNORE INTO mir3.user_data(Idx, ChrName) VALUES(@idx, @name)", conn);
            cmd.Parameters.AddWithValue("@idx", idx);
            cmd.Parameters.Add("@name", MySqlDbType.Binary).Value = nameBytes;
            cmd.ExecuteNonQuery();
            MirQuickList[chrName] = idx; // 更新内存缓存
            QuickIndexIdList[idx] = idx;
            _recordCount = -1;
            return true;
        }

        public bool DeleteDataRow(int idx)
        {
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                "DELETE FROM mir3.user_data WHERE Idx=@idx", conn);
            cmd.Parameters.AddWithValue("@idx", idx);
            bool ok = cmd.ExecuteNonQuery() > 0;
            if (ok) UnregisterNativeIndex(idx);
            return ok;
        }

        // ===================== CRUD =====================

        public bool Add(ref THumDataInfo HumanRCD)
        {
            // 存档行应由 CreateCharacter 创建，这里只更新 Blob
            if (HumanRCD == null) return false;

            string chrName = HumanRCD.Data?.sCharName;
            if (string.IsNullOrEmpty(chrName)) return false;

            using var conn = OpenConnection();
            if (conn == null) return false;

            // user_index is authoritative; user_data.ChrName in legacy databases may contain
            // '?' because the original latin1 column was once written as a Unicode string.
            using var findCmd = new MySqlCommand(
                "SELECT idx FROM mir3.user_index WHERE ChrName=@name AND IsDelete=0 LIMIT 1", conn);
            findCmd.Parameters.Add(LegacyGbkText.Parameter("@name", chrName));
            var obj = findCmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) return false;

            int idx = Convert.ToInt32(obj);

            // 序列化 + 压缩 + 写入 + 更新内存缓存
            byte[] serialized = ProtoBufDecoder.Serialize(HumanRCD);
            bool ok = SaveBlob(idx, serialized);
            if (ok) MirQuickList[chrName] = idx;
            return ok;
        }

        public bool Update(int nIndex, ref THumDataInfo HumanRCD,
            int forceLv = 0, int forceExp = 0, int fightPoints = 0, int sfLevel = 0,
            int apprenticeNum = int.MinValue, int heroCardLv = int.MinValue,
            int platinaChrLv = int.MinValue)
        {
            if (HumanRCD == null) return false;

            // 序列化 + 压缩 + 写入
            byte[] serialized = ProtoBufDecoder.Serialize(HumanRCD);
            bool result = SaveBlob(nIndex, serialized);

            // 同步更新 user_index 元数据
            if (result && HumanRCD.Data != null)
            {
                using var conn = OpenConnection();
                if (conn != null)
                {
                    var sql = new StringBuilder(
                        @"UPDATE mir3.user_index SET
                            Level=@lvl, Exp=@exp, Job=@job, Sex=@sex,
                            ForceLv=@flv, ForceExp=@fexp, FightPoints=@fp, sfLevel=@sf,
                            ModifyDate=NOW()");
                    if (apprenticeNum != int.MinValue) sql.Append(", ApprenticeNum=@an");
                    if (heroCardLv != int.MinValue) sql.Append(", HeroCardLv=@hcl");
                    if (platinaChrLv != int.MinValue) sql.Append(", PlatinaChrLv=@pcl");
                    sql.Append(" WHERE Idx=@idx");

                    using var cmd = new MySqlCommand(sql.ToString(), conn);
                    cmd.Parameters.AddWithValue("@lvl", HumanRCD.Data.Abil?.Level ?? 1);
                    cmd.Parameters.AddWithValue("@exp", unchecked((uint)(HumanRCD.Data.Abil?.Exp ?? 0)));
                    cmd.Parameters.AddWithValue("@job", HumanRCD.Data.btJob);
                    cmd.Parameters.AddWithValue("@sex", HumanRCD.Data.btSex);
                    cmd.Parameters.AddWithValue("@flv", forceLv);
                    cmd.Parameters.AddWithValue("@fexp", forceExp);
                    cmd.Parameters.AddWithValue("@fp", fightPoints);
                    cmd.Parameters.AddWithValue("@sf", sfLevel);
                    if (apprenticeNum != int.MinValue) cmd.Parameters.AddWithValue("@an", apprenticeNum);
                    if (heroCardLv != int.MinValue) cmd.Parameters.AddWithValue("@hcl", heroCardLv);
                    if (platinaChrLv != int.MinValue) cmd.Parameters.AddWithValue("@pcl", platinaChrLv);
                    cmd.Parameters.AddWithValue("@idx", nIndex);
                    cmd.ExecuteNonQuery();
                }
            }
            return result;
        }

        public bool UpdateQryChar(int nIndex, TQueryChr QueryChrRcd) => true;

        public bool Delete(int nIndex)
        {
            // 软删除: 设置 Status=1
            using var conn = OpenConnection();
            if (conn == null) return false;

            using var cmd = new MySqlCommand(
                "UPDATE mir3.user_data SET Status=1 WHERE Idx=@idx", conn);
            cmd.Parameters.AddWithValue("@idx", nIndex);
            bool ok = cmd.ExecuteNonQuery() > 0;
            if (ok) UnregisterNativeIndex(nIndex);
            return ok;
        }

        public bool Delete(string sChrName)
        {
            if (!MirQuickList.TryGetValue(sChrName, out int idx)) return false;
            return Delete(idx);
        }

        private void RemoveQuickIndex(int idx)
        {
            QuickIndexIdList.TryRemove(idx, out _);
            foreach (var pair in MirQuickList)
            {
                if (pair.Value != idx) continue;
                MirQuickList.TryRemove(pair.Key, out _);
                break;
            }
        }

        // ===================== 辅助方法 =====================

        private static bool TryDecodeRecord(byte[] dataBlob, byte[] scriptBlob,
            out THumDataInfo humanRcd, out string error)
        {
            humanRcd = null;
            error = string.Empty;
            if (dataBlob == null || dataBlob.Length == 0)
            {
                error = "empty character Data blob";
                return false;
            }

            if (NativeHumanDataCodec.LooksLikeNativeDataBlob(dataBlob))
                return NativeHumanDataCodec.TryDecode(dataBlob, scriptBlob, out humanRcd, out error);

            // Read-only compatibility for the two early C# protobuf saves. The next successful
            // save is written through NativeHumanDataCodec; malformed legacy data stays closed.
            try
            {
                var decompressed = BlobCompressor.TryDecompress(dataBlob);
                humanRcd = ProtoBufDecoder.DeSerialize<THumDataInfo>(decompressed);
                if (humanRcd?.Data == null)
                {
                    humanRcd = null;
                    error = "legacy protobuf character record is invalid";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                humanRcd = null;
                error = "unknown or corrupt character record: " + ex.Message;
                return false;
            }
        }

        private static void NormalizeRecord(THumDataInfo humanRcd)
        {
            if (humanRcd?.Data == null) return;
            var data = humanRcd.Data;
            data.Abil ??= new TAbility();
            data.BonusAbil ??= new TNakedAbility();
            data.wStatusTimeArr ??= new ushort[12];
            data.QuestUnitOpen ??= new byte[128];
            data.QuestUnit ??= new byte[128];
            data.QuestFlag ??= new byte[128];
            if (data.HumItems == null)
                data.HumItems = new TUserItem[NativeHumanDataCodec.EquippedItemCount];
            else if (data.HumItems.Length < NativeHumanDataCodec.EquippedItemCount)
                Array.Resize(ref data.HumItems, NativeHumanDataCodec.EquippedItemCount);
            data.BagItems ??= new TUserItem[NativeHumanDataCodec.BagItemCount];
            data.StorageItems ??= new TUserItem[NativeHumanDataCodec.StorageItemCount];
            data.Magic ??= new TMagicRcd[NativeHumanDataCodec.MagicCount];
            data.IntVar ??= Array.Empty<int>();
            data.ScriptV ??= new Dictionary<int, int>();
            data.ScriptS ??= new Dictionary<int, int>();
        }

        private static bool TryApplyIndexMetadata(MySqlDataReader reader,
            THumDataInfo humanRcd, out string error)
        {
            error = string.Empty;
            if (humanRcd?.Data == null)
            {
                error = "decoded character record is empty";
                return false;
            }

            try
            {
                var createDateOrdinal = reader.GetOrdinal("CreateDate");
                if (reader.IsDBNull(createDateOrdinal))
                {
                    error = "user_index.CreateDate is NULL";
                    return false;
                }

                var createDate = reader.GetDateTime(createDateOrdinal);
                var nativeCreateDate = createDate.ToOADate();
                if (!double.IsFinite(nativeCreateDate))
                {
                    error = "user_index.CreateDate is outside the native TDateTime range";
                    return false;
                }

                humanRcd.Header ??= new TRecordHeader();
                humanRcd.Header.dCreateDate = nativeCreateDate;
                humanRcd.NativeUserId = NormalizeNativeUserId(
                    reader.GetValue(reader.GetOrdinal("UserId")));
                humanRcd.Data.btJob = checked((byte)reader.GetInt32("Job"));
                humanRcd.Data.btSex = checked((byte)reader.GetInt32("Sex"));
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException
                                       || ex is OverflowException
                                       || ex is ArgumentException
                                       || ex is MySqlException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static long NormalizeNativeUserId(object value)
        {
            if (value == null || value == DBNull.Value) return 0;
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException
                                       || ex is OverflowException
                                       || ex is FormatException)
            {
                return 0;
            }
        }

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
                DBShare.MainOutMessage($"[MySqlPlayData] 连接失败: {ex.Message}");
                return null;
            }
        }

        private static string ReadGbkName(MySqlDataReader reader, string column)
        {
            return LegacyGbkText.Read(reader, column);
        }
    }
}
