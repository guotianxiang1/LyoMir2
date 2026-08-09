using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Text;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 账号仓库 MySQL 实现 (对应 user_storage 表)。
    /// </summary>
    public class MySqlStorageService : IStorageService
    {
        public bool CreateStorage(string ptid)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("INSERT INTO mir3.user_storage(PTID) VALUES(@p)", conn);
            cmd.Parameters.AddWithValue("@p", ptid);
            cmd.ExecuteNonQuery();
            return true;
        }

        public byte[] LoadStorage(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            using var cmd = new MySqlCommand("SELECT Data FROM mir3.user_storage WHERE Idx=@i", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            var data = cmd.ExecuteScalar() as byte[];
            return data != null ? BlobCompressor.TryDecompress(data) : null;
        }

        public byte[] LoadStorageByPtid(string ptid)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            using var cmd = new MySqlCommand("SELECT Data FROM mir3.user_storage WHERE PTID=@p LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@p", ptid);
            var data = cmd.ExecuteScalar() as byte[];
            return data != null ? BlobCompressor.TryDecompress(data) : null;
        }

        public bool SaveStorage(int idx, byte[] data)
        {
            if (data == null) return false;
            using var conn = OpenConn();
            if (conn == null) return false;
            var compressed = BlobCompressor.Compress(data);
            using var cmd = new MySqlCommand(
                "UPDATE mir3.user_storage SET Data=UNHEX(@d) WHERE Idx=@i", conn);
            cmd.Parameters.Add("@d", MySqlDbType.LongText).Value = Convert.ToHexString(compressed);
            cmd.Parameters.AddWithValue("@i", idx);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteStorage(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("DELETE FROM mir3.user_storage WHERE Idx=@i", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteStorageByPtid(string ptid)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("DELETE FROM mir3.user_storage WHERE PTID=@p", conn);
            cmd.Parameters.AddWithValue("@p", ptid);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool RenamePtid(string oldPtid, string newPtid)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("UPDATE mir3.user_storage SET PTID=@n WHERE PTID=@o", conn);
            cmd.Parameters.AddWithValue("@n", newPtid); cmd.Parameters.AddWithValue("@o", oldPtid);
            cmd.ExecuteNonQuery();
            return true;
        }

        public (int idx, string ptid) GetStorageInfo(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return (0, null);
            using var cmd = new MySqlCommand("SELECT Idx, PTID FROM mir3.user_storage WHERE Idx=@i", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            using var dr = cmd.ExecuteReader();
            if (dr.Read()) return (dr.GetInt32(0), dr.GetString(1));
            return (0, null);
        }

        public int GetMaxIdx()
        {
            using var conn = OpenConn();
            if (conn == null) return 0;
            using var cmd = new MySqlCommand("SELECT COALESCE(MAX(Idx),0) FROM mir3.user_storage", conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public NativeAccountStorageBlobResult LoadNativeStorage(int idx)
        {
            using var connection = OpenConn();
            if (connection == null)
                return new NativeAccountStorageBlobResult { Result = 0 };
            using var command = new MySqlCommand(
                "SELECT Data FROM mir3.user_storage WHERE Idx=@idx",
                connection);
            command.Parameters.AddWithValue("@idx", idx);
            var value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                return value == DBNull.Value
                    ? NativeAccountStorageBlobCodec.Decode(Array.Empty<byte>())
                    : new NativeAccountStorageBlobResult { Result = 0 };
            return NativeAccountStorageBlobCodec.Decode((byte[])value);
        }

        public List<NativeStorageIndexEntry> GetNativeStoragePage(
            int lastIdx, int limit = 5000)
        {
            var result = new List<NativeStorageIndexEntry>();
            using var connection = OpenConn();
            if (connection == null) return result;
            using var command = new MySqlCommand(
                @"SELECT Idx, PTID FROM mir3.user_storage
                  WHERE Idx>@lastIdx ORDER BY Idx LIMIT @limit",
                connection);
            command.Parameters.AddWithValue("@lastIdx", lastIdx);
            command.Parameters.AddWithValue("@limit", Math.Min(limit, 5000));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var value = reader.GetValue(1);
                result.Add(new NativeStorageIndexEntry
                {
                    Index = reader.GetInt32(0),
                    Account = value is byte[] bytes
                        ? (byte[])bytes.Clone()
                        : Encoding.Latin1.GetBytes(reader.GetString(1))
                });
            }
            return result;
        }

        public int EnsureNativeStorage(byte[] account)
        {
            using var connection = OpenConn();
            if (connection == null) return 0;
            using (var query = new MySqlCommand(
                       "SELECT Idx FROM mir3.user_storage WHERE PTID=@ptid LIMIT 1",
                       connection))
            {
                query.Parameters.Add("@ptid", MySqlDbType.Binary).Value =
                    account ?? Array.Empty<byte>();
                var existing = query.ExecuteScalar();
                if (existing != null && existing != DBNull.Value)
                    return Convert.ToInt32(existing);
            }

            try
            {
                using var insert = new MySqlCommand(
                    "INSERT INTO mir3.user_storage(PTID) VALUES(@ptid)",
                    connection);
                insert.Parameters.Add("@ptid", MySqlDbType.Binary).Value =
                    account ?? Array.Empty<byte>();
                insert.ExecuteNonQuery();
            }
            catch { return 0; }

            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var identity = new MySqlCommand(
                    "SELECT LAST_INSERT_ID()", connection);
                var value = identity.ExecuteScalar();
                if (value != null && value != DBNull.Value
                    && Convert.ToInt32(value) > 0)
                    return Convert.ToInt32(value);
            }
            return 0;
        }

        public bool SaveNativeStorage(int idx, byte[] data)
        {
            byte[] blob;
            try { blob = NativeAccountStorageBlobCodec.Encode(data); }
            catch (ArgumentOutOfRangeException) { return false; }
            using var connection = OpenConn();
            if (connection == null) return false;
            using var command = new MySqlCommand(
                "UPDATE mir3.user_storage SET Data=UNHEX(@data) WHERE Idx=@idx",
                connection);
            command.Parameters.Add("@data", MySqlDbType.LongText).Value =
                Convert.ToHexString(blob);
            command.Parameters.AddWithValue("@idx", idx);
            return command.ExecuteNonQuery() > 0;
        }

        private static MySqlConnection OpenConn()
        {
            try
            {
                var c = new MySqlConnection(DBShare.DBConnection);
                c.Open();
                using(var sc = new MySqlCommand("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; SET SESSION wait_timeout=2073600", c))
                    sc.ExecuteNonQuery();
                return c;
            }
            catch { return null; }
        }
    }
}
