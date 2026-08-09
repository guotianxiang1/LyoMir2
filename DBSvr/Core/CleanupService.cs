using MySql.Data.MySqlClient;
using System;

namespace DBSvr.Core
{
    /// <summary>
    /// 不活跃角色清理 + 孤立数据清理。
    /// 对应 Delphi 原版 TimedService 中的清理逻辑:
    ///   - Level < 8 且 15 天未活动 → 删除
    ///   - 孤儿 hero_data / user_data / guild_user 清理
    ///   - 超旧角色清理
    /// </summary>
    public class CleanupService
    {
        private const int NativeInactiveLevelLimit = 8;
        private const int NativeInactiveDays = 15;
        private readonly string _connStr;

        public CleanupService(string connStr) { _connStr = connStr; }

        /// <summary>
        /// 清理不活跃角色。目标 DBServer 的 SQL 固定为 Level&lt;8 和 15 天。
        /// 使用临时表 Del_Temp_Idx 分段删除。
        /// </summary>
        public int CleanInactiveCharacters()
        {
            int deleted = 0;
            using var conn = OpenConn();
            if (conn == null) return 0;

            try
            {
                DropTemporaryTable(conn, "Del_Temp_Idx");
                // Step 1: 创建临时表
                using var c0 = new MySqlCommand("CREATE TEMPORARY TABLE Del_Temp_Idx(Idx INT PRIMARY KEY)", conn);
                c0.ExecuteNonQuery();

                // Step 2: 找出待删除的 Idx
                using var c1 = new MySqlCommand(
                    @"INSERT INTO Del_Temp_Idx
                      SELECT Idx FROM mir3.user_index
                      WHERE Level < @maxLev AND NOW() > DATE_ADD(ModifyDate, INTERVAL @days DAY)", conn);
                c1.Parameters.AddWithValue("@maxLev", NativeInactiveLevelLimit);
                c1.Parameters.AddWithValue("@days", NativeInactiveDays);
                c1.ExecuteNonQuery();

                // Step 3: 检查数量
                using var c2 = new MySqlCommand("SELECT COUNT(*) FROM Del_Temp_Idx", conn);
                int count = Convert.ToInt32(c2.ExecuteScalar());
                if (count == 0) return 0;

                // Step 4: 关联删除
                using var c3 = new MySqlCommand(
                    "DELETE user_data FROM mir3.user_data INNER JOIN Del_Temp_Idx ON user_data.Idx = Del_Temp_Idx.Idx", conn);
                c3.ExecuteNonQuery();
                using var c4 = new MySqlCommand(
                    "DELETE user_index FROM mir3.user_index INNER JOIN Del_Temp_Idx ON user_index.Idx = Del_Temp_Idx.Idx", conn);
                c4.ExecuteNonQuery();

                deleted = count;
                DBShare.MainOutMessage($"[Cleanup] 清理 {count} 个不活跃角色 " +
                    $"(Level<{NativeInactiveLevelLimit}, {NativeInactiveDays}天)");
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[Cleanup] 错误: {ex.Message}");
            }
            finally { DropTemporaryTable(conn, "Del_Temp_Idx"); }
            return deleted;
        }

        /// <summary>
        /// 清理孤立数据 — 索引与存档不匹配的记录。
        /// </summary>
        public int CleanOrphanData()
        {
            int total = 0;
            using var conn = OpenConn();
            if (conn == null) return 0;

            try
            {
                string[] orphans =
                {
                    "DELETE FROM mir3.hero_data WHERE idx NOT IN (SELECT idx FROM mir3.hero_index)",
                    "DELETE FROM mir3.user_data WHERE idx NOT IN (SELECT idx FROM mir3.user_index)",
                    "DELETE FROM mir3.hero_index WHERE MasterName NOT IN (SELECT ChrName FROM mir3.user_index)",
                    "DELETE FROM guild.guild_user WHERE CharName NOT IN (SELECT ChrName FROM mir3.user_index)"
                };

                foreach (var sql in orphans)
                {
                    using var cmd = new MySqlCommand(sql, conn);
                    total += cmd.ExecuteNonQuery();
                }

                if (total > 0)
                    DBShare.MainOutMessage($"[Cleanup] 清理 {total} 条孤立数据");
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[CleanupOrphan] 错误: {ex.Message}");
            }
            return total;
        }

        /// <summary>
        /// 清理超旧角色 (2008年前 或 2010年前且 ≤ 60 级)。
        /// </summary>
        public int CleanAncientCharacters(int maxLevel = 60)
        {
            using var conn = OpenConn();
            if (conn == null) return 0;
            try
            {
                DropTemporaryTable(conn, "Ancient_Temp_Idx");
                using var create = new MySqlCommand(
                    "CREATE TEMPORARY TABLE Ancient_Temp_Idx(Idx INT PRIMARY KEY)", conn);
                create.ExecuteNonQuery();

                using var select = new MySqlCommand(
                    @"INSERT INTO Ancient_Temp_Idx
                      SELECT Idx FROM mir3.user_index
                      WHERE IsDelete=0 AND
                        (YEAR(ModifyDate) <= 2008 OR (YEAR(ModifyDate) < 2010 AND Level <= @ml))", conn);
                select.Parameters.AddWithValue("@ml", maxLevel);
                select.ExecuteNonQuery();

                using var countCmd = new MySqlCommand("SELECT COUNT(*) FROM Ancient_Temp_Idx", conn);
                int deleted = Convert.ToInt32(countCmd.ExecuteScalar());
                if (deleted == 0) return 0;

                using var deleteData = new MySqlCommand(
                    "DELETE user_data FROM mir3.user_data INNER JOIN Ancient_Temp_Idx ON user_data.Idx=Ancient_Temp_Idx.Idx", conn);
                deleteData.ExecuteNonQuery();
                using var deleteIndex = new MySqlCommand(
                    "DELETE user_index FROM mir3.user_index INNER JOIN Ancient_Temp_Idx ON user_index.Idx=Ancient_Temp_Idx.Idx", conn);
                deleteIndex.ExecuteNonQuery();
                if (deleted > 0)
                    DBShare.MainOutMessage($"[Cleanup] 清理 {deleted} 个超旧角色");
                return deleted;
            }
            catch (Exception ex) { DBShare.MainOutMessage($"[CleanupAncient] 错误: {ex.Message}"); }
            finally { DropTemporaryTable(conn, "Ancient_Temp_Idx"); }
            return 0;
        }

        private static void DropTemporaryTable(MySqlConnection conn, string tableName)
        {
            try
            {
                using var command = new MySqlCommand($"DROP TEMPORARY TABLE IF EXISTS `{tableName}`", conn);
                command.ExecuteNonQuery();
            }
            catch { }
        }

        private MySqlConnection OpenConn()
        {
            try
            {
                var c = new MySqlConnection(_connStr);
                c.Open();
                using(var sc = new MySqlCommand("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; SET SESSION wait_timeout=2073600", c))
                    sc.ExecuteNonQuery();
                return c;
            }
            catch { return null; }
        }
    }
}
