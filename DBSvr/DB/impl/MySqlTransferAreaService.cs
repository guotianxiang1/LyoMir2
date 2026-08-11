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
        // BLOCKED: no native SELECT literal for TransferAreaScore exists in the CODE
        // snapshot. A full census of 'TransferArea' literals plus every 'Score1'/'score1'
        // occurrence yields only the insert 0x5960E4, the rename 0x5AA148 and the two
        // CREATE TABLE statements (0x5C0EA4, 0x5C0CF4). GetScores/DeductScore/
        // TryDeductNativeScore are C#-ONLY read/deduct paths with no Delphi counterpart
        // to compare against; their column names (Score1..Score3, CharName) are the only
        // part backed by the validated DDL at 0x5C0EA4. Missing evidence: the reader for
        // the native score-spend path, whose function was virtualised.
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
            // Native 0x595AC4: 'Insert into TransferAreaScoreSendRecord(TimeStamp,
            // CharName, ZoneId,  GroupId, ScoreType, Score, State) Values("%s", "%s",
            // %d, %d, %d, %d, %d)  on duplicate key update State=%d;'
            // Column list, order and the ON DUPLICATE KEY UPDATE State-only payload all
            // match; the duplicate arbitration is the 5-column Record_Index unique key
            // (DDL 0x5C0CF4). Note the native emits no schema prefix here, relying on the
            // connection's default schema; kept explicit as 'gamedata.' because this
            // process connects with database=mir3 (DBShare.DBConnection), so an
            // unqualified name would resolve to the wrong schema.
            using var cmd = new MySqlCommand(
                @"INSERT INTO gamedata.TransferAreaScoreSendRecord(TimeStamp, CharName, ZoneId, GroupId, ScoreType, Score, State)
                  VALUES(@t,@c,@z,@g,@st,@s,@e) ON DUPLICATE KEY UPDATE State=@e", conn);
            cmd.Parameters.AddWithValue("@t", timeStamp);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@c", charName));
            cmd.Parameters.AddWithValue("@z", zoneId); cmd.Parameters.AddWithValue("@g", groupId);
            cmd.Parameters.AddWithValue("@st", scoreType); cmd.Parameters.AddWithValue("@s", score); cmd.Parameters.AddWithValue("@e", state);
            cmd.ExecuteNonQuery(); return true;
        }

        /// <summary>
        /// 更新发送记录状态。
        /// 身份 = 5 列唯一键 Record_Index(TimeStamp, CharName, ZoneId, GroupId, ScoreType)
        /// (DDL 0x5C0CF4)。scoreType 现为必填参数。
        /// </summary>
        public bool UpdateSendRecordState(string timeStamp, string charName, int zoneId, int groupId, int scoreType, int state)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // The native has NO standalone UPDATE for this table: a full literal census
            // of every 'TransferArea' occurrence in the CODE snapshot yields only
            // 0x595684 (count), 0x595714 (select State=1), 0x5958E0 (select expired idx),
            // 0x595968 (delete by idx) and 0x595AC4 (insert .. on duplicate key update
            // State=%d). The native mutates State exclusively through that insert's
            // ON DUPLICATE KEY path, which is arbitrated by the 5-column unique key.
            // This method is therefore an equivalent of that path's UPDATE half, so its
            // WHERE must reproduce the key exactly.
            //
            // WRONG BEFORE: the WHERE listed only TimeStamp, CharName, ZoneId, GroupId
            // and omitted ScoreType (the 5th key column), so one score type's update
            // matched and overwrote every sibling row sharing the other four values.
            using var cmd = new MySqlCommand(
                "UPDATE gamedata.TransferAreaScoreSendRecord SET State=@e WHERE TimeStamp=@t AND CharName=@c AND ZoneId=@z AND GroupId=@g AND ScoreType=@st", conn);
            cmd.Parameters.AddWithValue("@t", timeStamp);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@c", charName));
            cmd.Parameters.AddWithValue("@z", zoneId); cmd.Parameters.AddWithValue("@g", groupId);
            cmd.Parameters.AddWithValue("@st", scoreType); cmd.Parameters.AddWithValue("@e", state);
            return cmd.ExecuteNonQuery() > 0;
        }

        public int CleanExpiredRecords(int days = 7)
        {
            using var conn = OpenConn();
            if (conn == null) return 0;
            // Native does this in two steps, not one combined DELETE:
            //   0x5958E0 'Select High_Priority idx from TransferAreaScoreSendRecord
            //             where (State = 3) and (Now() > DATE_Add(TimeStamp, Interval 7 DAY));'
            //   0x595968 'Delete from TransferAreaScoreSendRecord where idx = %d;'
            // The 7-day interval is a literal constant in the native SELECT; the caller
            // passes DBShare.TransferRecordDays (7), which agrees.
            // WRONG BEFORE: a single combined DELETE ... WHERE State=3 AND NOW() > ...
            // never enumerated idx, so deletion was not keyed on the primary key as the
            // native's is. Rows are now deleted one-by-one by idx, matching the native.
            var expired = new List<int>();
            using (var select = new MySqlCommand(
                "Select High_Priority idx from gamedata.TransferAreaScoreSendRecord where (State = 3) and (Now() > DATE_Add(TimeStamp, Interval @d DAY))", conn))
            {
                select.Parameters.AddWithValue("@d", days);
                using var dr = select.ExecuteReader();
                while (dr.Read()) expired.Add(Convert.ToInt32(dr.GetValue(0)));
            }
            var removed = 0;
            foreach (var idx in expired)
            {
                using var del = new MySqlCommand(
                    "Delete from gamedata.TransferAreaScoreSendRecord where idx = @i", conn); // 0x595968
                del.Parameters.AddWithValue("@i", idx);
                removed += del.ExecuteNonQuery();
            }
            return removed;
        }

        /// <summary>
        /// 读取待发送记录（State=1），按 TimeStamp 升序（原版 0x595714）。
        ///
        /// 原版行为（0x595430 函数）：
        /// 1. 执行计数查询（0x595684）：
        ///    Select High_Priority Count(*) as TotalCount from TransferAreaScoreSendRecord
        ///    Group By CharName, ZoneId, GroupId;
        /// 2. 检查 Self+0x1C（TList），未初始化则创建（容量 = min(0x400, count/2)）
        /// 3. 执行主查询（0x595714）：
        ///    Select High_Priority TimeStamp, CharName, ZoneId, GroupId, ScoreType, Score, State
        ///    from TransferAreaScoreSendRecord where State = 1 Order by TimeStamp;
        /// 4. 逐行填充 0x27 字节结构体并推入 TList
        ///
        /// 结构体布局（0x5954E6..0x595621）：
        ///   +0x00: TimeStamp (qword, TDateTime)
        ///   +0x08: CharName (ShortString[15], 1 len + 15 data)
        ///   +0x18: ZoneId (word, 从查询列索引 2)
        ///   +0x1A: GroupId (word, 从查询列索引 3)
        ///   +0x1C: ZoneId (word, 从配置 [0x5d9b04]+0x50)
        ///   +0x1E: GroupId (word, 从配置 [0x5d9b04]+0x54)
        ///   +0x20: ScoreType (word, 列索引 4)
        ///   +0x22: Score (word, 列索引 5)
        ///   +0x24: State (word, 列索引 6)
        ///   +0x26: flag (byte=0)
        ///
        /// 注意：原版字面量有双空格（", " 和 "1  Order"），此处规范化为单空格。
        /// </summary>
        public List<TransferAreaSendRecord> GetPendingSendRecords()
        {
            var result = new List<TransferAreaSendRecord>();
            using var conn = OpenConn();
            if (conn == null) return result;

            // 原版 0x595714（双空格已规范化）
            using var cmd = new MySqlCommand(
                @"Select High_Priority TimeStamp, CharName, ZoneId, GroupId,
                  ScoreType, Score, State
                  from gamedata.TransferAreaScoreSendRecord
                  where State = 1
                  Order by TimeStamp", conn);

            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                result.Add(new TransferAreaSendRecord
                {
                    TimeStamp = dr.GetDateTime(0),
                    CharName = LegacyGbkText.Read(dr, 1),
                    ZoneId = Convert.ToUInt16(dr.GetValue(2)),
                    GroupId = Convert.ToUInt16(dr.GetValue(3)),
                    ScoreType = Convert.ToUInt16(dr.GetValue(4)),
                    Score = Convert.ToUInt16(dr.GetValue(5)),
                    State = Convert.ToUInt16(dr.GetValue(6))
                });
            }
            return result;
        }

        public bool RenameChar(string oldName, string newName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // Native 0x5A9C68 ("Update ignore gamedata") + 0x5AA148
            // (".TransferAreaScore set CharName=\"%s\" where CharName=\"%s\";")
            // => 'Update ignore gamedata.TransferAreaScore set CharName=.. where CharName=..'
            // WRONG BEFORE: the 'ignore' modifier was dropped. TransferAreaScore has
            // 'Unique Key Char_Index(CharName)' (DDL 0x5C0EA4), so renaming onto an
            // existing name raises a duplicate-key error instead of being silently
            // skipped as the native does, aborting the rename cascade mid-way.
            using var cmd = new MySqlCommand(
                "Update ignore gamedata.TransferAreaScore set CharName=@n where CharName=@o", conn);
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
