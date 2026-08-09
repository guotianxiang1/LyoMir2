using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 主宰宠物 MySQL 实现 (对应 dominatorpet 表)。
    /// </summary>
    public class MySqlPetService : IPetService
    {
        private readonly NativeDominatorPetBackupQueue _backupQueue;

        public MySqlPetService(NativeDominatorPetBackupQueue backupQueue) =>
            _backupQueue = backupQueue
                ?? throw new ArgumentNullException(nameof(backupQueue));

        public byte[] LoadPet(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            using var cmd = new MySqlCommand("SELECT Data FROM mir3.dominatorpet WHERE MasterId=@m", conn);
            cmd.Parameters.AddWithValue("@m", masterId);
            var blob = cmd.ExecuteScalar() as byte[];
            return NativeDominatorPetBlobCodec.TryDecode(blob, out var data,
                out _) ? data : LoadBackupPet(masterId);
        }

        public (int idx, byte[] data) LoadPetWithIdx(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return (0, null);
            using var cmd = new MySqlCommand("SELECT Idx, Data FROM mir3.dominatorpet WHERE MasterId=@m", conn);
            cmd.Parameters.AddWithValue("@m", masterId);
            using var dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                var blob = dr["Data"] as byte[];
                return (dr.GetInt32(0),
                    NativeDominatorPetBlobCodec.TryDecode(blob, out var data,
                        out _) ? data : LoadBackupPet(masterId));
            }
            return (0, null);
        }

        public bool CreatePet(string masterName, long masterId, int level, int exp)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "INSERT INTO mir3.dominatorpet(MasterName, MasterId, Level, Exp, CreateDate) VALUES(@n, @m, @l, @e, NOW())", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            cmd.Parameters.AddWithValue("@m", masterId);
            cmd.Parameters.AddWithValue("@l", level); cmd.Parameters.AddWithValue("@e", exp);
            cmd.ExecuteNonQuery();
            return true;
        }

        public bool SavePet(long masterId, string masterName, int level,
            int exp, byte[] data)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            if (!NativeDominatorPetBlobCodec.TryEncode(data, out var blob,
                    out _)) return false;
            using (var index = new MySqlCommand(
                       "UPDATE mir3.dominatorpet SET Level=@l, Exp=@e, ModifyDate=NOW() WHERE MasterId=@m",
                       conn))
            {
                index.Parameters.AddWithValue("@l", level);
                index.Parameters.AddWithValue("@e", unchecked((uint)exp));
                index.Parameters.AddWithValue("@m", masterId);
                if (index.ExecuteNonQuery() <= 0) return false;
            }
            using var cmd = new MySqlCommand(
                "UPDATE mir3.dominatorpet SET Data=UNHEX(@d) WHERE MasterId=@m",
                conn);
            cmd.Parameters.Add("@d", MySqlDbType.LongText).Value =
                Convert.ToHexString(blob);
            cmd.Parameters.AddWithValue("@m", masterId);
            if (cmd.ExecuteNonQuery() <= 0) return false;
            _backupQueue.Enqueue(masterName, masterId, level,
                unchecked((uint)exp), blob);
            return true;
        }

        public bool UpdatePetLevel(long masterId, int level, int exp)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "UPDATE mir3.dominatorpet SET Level=@l, Exp=@e, ModifyDate=NOW() WHERE MasterId=@m", conn);
            cmd.Parameters.AddWithValue("@l", level); cmd.Parameters.AddWithValue("@e", exp); cmd.Parameters.AddWithValue("@m", masterId);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeletePet(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("DELETE FROM mir3.dominatorpet WHERE MasterId=@m", conn);
            cmd.Parameters.AddWithValue("@m", masterId);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool RenameMaster(string oldMaster, string newMaster)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("UPDATE mir3.dominatorpet SET MasterName=@n WHERE MasterName=@o", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", newMaster));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@o", oldMaster));
            cmd.ExecuteNonQuery();
            return true;
        }

        public List<PetIndexInfo> GetPetPage(int lastIdx, int limit = 5000)
        {
            var list = new List<PetIndexInfo>();
            using var conn = OpenConn();
            if (conn == null) return list;
            using var cmd = new MySqlCommand(
                "SELECT Idx, MasterId, MasterName, Level, Exp FROM mir3.dominatorpet WHERE Idx > @l ORDER BY Idx LIMIT @lim", conn);
            cmd.Parameters.AddWithValue("@l", lastIdx);
            cmd.Parameters.AddWithValue("@lim", Math.Min(limit, DBShare.BatchLimit));
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
                list.Add(new PetIndexInfo
                {
                    Idx = dr.GetInt32(0), MasterId = dr.GetInt64(1),
                    MasterName = LegacyGbkText.Read(dr, 2), Level = dr.GetInt32(3),
                    Exp = unchecked((int)Convert.ToUInt32(dr[4]))
                });
            return list;
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

        private static byte[] LoadBackupPet(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            try
            {
                using var cmd = new MySqlCommand(
                    "SELECT Data FROM mir3_backup.dominatorpet WHERE MasterId=@m",
                    conn);
                cmd.Parameters.AddWithValue("@m", masterId);
                var blob = cmd.ExecuteScalar() as byte[];
                return NativeDominatorPetBlobCodec.TryDecode(blob,
                    out var data, out _) ? data : null;
            }
            catch { return null; }
        }
    }
}
