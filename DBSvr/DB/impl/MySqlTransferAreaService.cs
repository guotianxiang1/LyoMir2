using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Text;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 跨服转区/转分 MySQL 实现 (gamedata.TransferAreaScore + TransferAreaScoreSendRecord)。
    /// </summary>
    public class MySqlTransferAreaService : ITransferAreaService
    {
        public Dictionary<string, int> GetScores(string charName)
        {
            var result = new Dictionary<string, int>();
            using var conn = OpenConn();
            if (conn == null) return result;
            using var cmd = new MySqlCommand(
                "SELECT Score1, Score2, Score3 FROM gamedata.TransferAreaScore WHERE CharName=@n", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", charName));
            using var dr = cmd.ExecuteReader();
            if (dr.Read()) { result["Score1"] = dr.GetInt32(0); result["Score2"] = dr.GetInt32(1); result["Score3"] = dr.GetInt32(2); }
            return result;
        }

        public bool DeductScore(string charName, string scoreField, int amount)
        {
            scoreField = scoreField switch
            {
                "Score1" => "Score1",
                "Score2" => "Score2",
                "Score3" => "Score3",
                _ => null
            };
            if (scoreField == null) return false;
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                $"UPDATE gamedata.TransferAreaScore SET {scoreField}=({scoreField} - @a) WHERE CharName=@n", conn);
            cmd.Parameters.AddWithValue("@a", amount);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", charName));
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool TryDeductNativeScore(byte[] characterName,
            ushort scoreType, ushort amount)
        {
            if (scoreType is < 1 or > 3) return false;
            using var connection = OpenConn();
            if (connection == null) return false;
            var field = "score" + scoreType;
            int current;
            try
            {
                using var query = new MySqlCommand(
                    $"SELECT {field} FROM gamedata.transferareascore WHERE charname=@name",
                    connection);
                query.Parameters.Add("@name", MySqlDbType.Binary).Value =
                    characterName ?? Array.Empty<byte>();
                var value = query.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                current = Convert.ToInt32(value);
            }
            catch { return false; }
            if (current < amount) return false;

            try
            {
                using var update = new MySqlCommand(
                    $"UPDATE gamedata.transferareascore SET {field}=({field}-@amount) WHERE CharName=@name",
                    connection);
                update.Parameters.AddWithValue("@amount", amount);
                update.Parameters.Add("@name", MySqlDbType.Binary).Value =
                    characterName ?? Array.Empty<byte>();
                update.ExecuteNonQuery();
            }
            catch { }
            return true;
        }

        public bool UpsertScore(string charName, int score1, int score2, int score3)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                @"INSERT INTO gamedata.TransferAreaScore(CharName, Score1, Score2, Score3)
                  VALUES(@n,@s1,@s2,@s3) ON DUPLICATE KEY UPDATE Score1=Score1+@s1, Score2=Score2+@s2, Score3=Score3+@s3", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", charName));
            cmd.Parameters.AddWithValue("@s1", score1); cmd.Parameters.AddWithValue("@s2", score2); cmd.Parameters.AddWithValue("@s3", score3);
            cmd.ExecuteNonQuery(); return true;
        }

        public bool InsertSendRecord(string timeStamp, string charName, int zoneId, int groupId, int scoreType, int score, int state)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                @"INSERT INTO gamedata.TransferAreaScoreSendRecord(TimeStamp, CharName, ZoneId, GroupId, ScoreType, Score, State)
                  VALUES(@t,@c,@z,@g,@st,@s,@e) ON DUPLICATE KEY UPDATE State=@e", conn);
            cmd.Parameters.AddWithValue("@t", timeStamp);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@c", charName));
            cmd.Parameters.AddWithValue("@z", zoneId); cmd.Parameters.AddWithValue("@g", groupId);
            cmd.Parameters.AddWithValue("@st", scoreType); cmd.Parameters.AddWithValue("@s", score); cmd.Parameters.AddWithValue("@e", state);
            cmd.ExecuteNonQuery(); return true;
        }

        public bool UpdateSendRecordState(string timeStamp, string charName, int zoneId, int groupId, int state)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "UPDATE gamedata.TransferAreaScoreSendRecord SET State=@e WHERE TimeStamp=@t AND CharName=@c AND ZoneId=@z AND GroupId=@g", conn);
            cmd.Parameters.AddWithValue("@t", timeStamp);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@c", charName));
            cmd.Parameters.AddWithValue("@z", zoneId); cmd.Parameters.AddWithValue("@g", groupId); cmd.Parameters.AddWithValue("@e", state);
            return cmd.ExecuteNonQuery() > 0;
        }

        public int CleanExpiredRecords(int days = 7)
        {
            using var conn = OpenConn();
            if (conn == null) return 0;
            using var command = new MySqlCommand(
                "DELETE FROM gamedata.TransferAreaScoreSendRecord WHERE State=3 AND NOW() > DATE_ADD(TimeStamp, INTERVAL @d DAY)", conn);
            command.Parameters.AddWithValue("@d", days);
            return command.ExecuteNonQuery();
        }

        public bool RenameChar(string oldName, string newName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "UPDATE gamedata.TransferAreaScore SET CharName=@n WHERE CharName=@o", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", newName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@o", oldName));
            cmd.ExecuteNonQuery(); return true;
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
