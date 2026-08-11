using MySql.Data.MySqlClient;
using System;

namespace DBSvr.Core
{
    /// <summary>
    /// DORMANT: UserId/HeroId 整表回填（原版 0x5BC780 / 0x5BCD74）。
    ///
    /// BLOCKED 原因：
    /// - 调用者无法从字节定位（VMP 虚拟化，整个字面量池零 dword 引用）
    /// - 推测为 schema migration 序列（probe/add/backfill），但无字节证明
    /// - 无 WHERE 子句 = 整表 UPDATE，不可盲接到启动路径
    ///
    /// 原版行为（从字面量池 0x5BC400..0x5BCF00 结构推断）：
    /// 1. show columns from user_index like "UserId";
    /// 2. 若不存在：Alter table user_index add column UserId bigInt default 0;
    ///             Create index userId_index on user_index (userId);
    /// 3. 立即执行：Update user_index set UserId = %d + idx;  (无 WHERE，整表)
    ///
    /// 字面量验证：
    /// - 0x5BC70C: 'Alter table user_index add column UserId bigInt default 0;
    ///              Create index userId_index on user_index (userId);' (len=107, rc=-1)
    /// - 0x5BC780: 'Update user_index set UserId = %d + idx;' (len=40, rc=-1)
    /// - 0x5BCD00: 'Alter table hero_index add column HeroId bigInt default 0;
    ///              Create index HeroId_index on Hero_index (HeroId);' (len=107, rc=-1)
    /// - 0x5BCD74: 'Update Hero_index set HeroId = %d + idx;' (len=40, rc=-1)
    ///   注意：表名/列名大写 Hero_index, HeroId（与 0x58CF28 的小写不同）
    ///
    /// 公式（从 0x5CA174 分配器逆向）：
    ///   UserId/HeroId = ((ZoneId * 1000 + GroupId + 10000000) * 1000000000) + idx
    ///   证据：0x5CA191 读 [eax+0x50] ZoneId，0x5CA19F 读 [eax+0x54] GroupId，
    ///         0x405C28 __llmul，常量 1000 (0x3E8) / 10000000 (0x989680) / 1000000000 (0x3B9ACA00)
    ///
    /// 真实库状态（已实测 mir3）：
    /// - user_index.UserId = bigint(20), MUL 索引，默认 NULL（列已存在）
    /// - hero_index.heroId = bigint(20), MUL 索引，默认 NULL（列已存在）
    ///   → ALTER TABLE 已执行过（手动或前一版本），但回填状态未知
    ///
    /// 手动调用条件：
    /// - 确认 UserId/HeroId 列全为 NULL/0（新区初始化，SELECT COUNT(*) WHERE col IS NOT NULL = 0）
    /// - 从 DBService.ini [Setup] 读取 ZoneIdx/GroupIdx
    /// - 在维护窗口执行（大表更新，锁表时间长）
    /// - 执行前备份数据库
    ///
    /// 不接线理由：
    /// - 按原版推测，仅在列刚创建时执行一次（隐式门：所有行默认 0）
    /// - C# 无法检测"列刚创建"状态（需跨重启持久化）
    /// - 真实库列已存在 → 条件不满足，盲目执行会覆盖有效数据
    /// - 等待有界 idat 审计确认调用时机后再决定接线策略
    /// </summary>
    public static class NativeUserIdBackfillService
    {
        /// <summary>
        /// 整表回填 mir3.user_index.UserId（原版 0x5BC780）。
        ///
        /// DANGER: 无 WHERE 子句，覆盖全表每一行。仅在确认需要时手动调用。
        ///
        /// 原版字面量（逐字节）：
        ///   0x5BC780 len=40 rc=-1: 'Update user_index set UserId = %d + idx;'
        ///   注意：表名 user_index 小写，列名 UserId 大写
        ///
        /// 调用示例（仅供参考，需先确认所有 UserId IS NULL）：
        ///   var affected = NativeUserIdBackfillService.BackfillUserIdBulk(
        ///       zoneId: 1, groupId: 1);
        ///   Console.WriteLine($"Backfilled {affected} rows");
        /// </summary>
        /// <param name="zoneId">ZoneId 配置（DBService.ini [Setup] ZoneIdx）</param>
        /// <param name="groupId">GroupId 配置（DBService.ini [Setup] GroupIdx）</param>
        /// <returns>受影响的行数</returns>
        public static int BackfillUserIdBulk(int zoneId, int groupId)
        {
            // 原版 0x5CA174 分配器公式
            long baseId = ((long)zoneId * 1000 + groupId + 10000000) * 1000000000L;

            using var conn = new MySqlConnection(DBShare.DBConnectionRoot);
            conn.Open();

            // 原版 0x5BC780（注意小写 user_index，大写 UserId）
            // %d + idx 翻译为 @base + idx（SQL 表达式计算）
            using var cmd = new MySqlCommand(
                "Update mir3.user_index set UserId = @base + idx", conn);
            cmd.Parameters.AddWithValue("@base", baseId);
            cmd.CommandTimeout = 300; // 大表操作延长超时（5分钟）

            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 整表回填 mir3.hero_index.HeroId（原版 0x5BCD74）。
        ///
        /// DANGER: 无 WHERE 子句，覆盖全表每一行。仅在确认需要时手动调用。
        ///
        /// 原版字面量（逐字节）：
        ///   0x5BCD74 len=40 rc=-1: 'Update Hero_index set HeroId = %d + idx;'
        ///   注意：表名 Hero_index 大写 H，列名 HeroId 大写 H（与 0x58CF28 的小写不同）
        ///
        /// 表名大小写不一致：
        /// - 0x58CF28（单行回填）：'Update hero_index set heroId = %d where idx = %d;'
        ///   → 小写 hero_index, heroId
        /// - 0x5BCD74（整表回填）：'Update Hero_index set HeroId = %d + idx;'
        ///   → 大写 Hero_index, HeroId
        ///
        /// 原版自身不一致，按各自字面量原样实现。MySQL 默认不区分大小写，
        /// 但保留原版字面量以防部署环境配置 lower_case_table_names=0。
        /// </summary>
        /// <param name="zoneId">ZoneId 配置（DBService.ini [Setup] ZoneIdx）</param>
        /// <param name="groupId">GroupId 配置（DBService.ini [Setup] GroupIdx）</param>
        /// <returns>受影响的行数</returns>
        public static int BackfillHeroIdBulk(int zoneId, int groupId)
        {
            // 原版 0x5CA174 分配器公式（与 BackfillUserIdBulk 相同）
            long baseId = ((long)zoneId * 1000 + groupId + 10000000) * 1000000000L;

            using var conn = new MySqlConnection(DBShare.DBConnectionRoot);
            conn.Open();

            // 原版 0x5BCD74（注意大写 Hero_index, HeroId）
            using var cmd = new MySqlCommand(
                "Update mir3.Hero_index set HeroId = @base + idx", conn);
            cmd.Parameters.AddWithValue("@base", baseId);
            cmd.CommandTimeout = 300; // 大表操作延长超时（5分钟）

            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 安全检查：确认表中所有行的 UserId/HeroId 均为 NULL 或 0。
        /// 仅当此方法返回 true 时，才可安全调用 Backfill*Bulk。
        /// </summary>
        public static bool IsSafeToBackfillUserId()
        {
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnectionRoot);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM mir3.user_index WHERE UserId IS NOT NULL AND UserId != 0", conn);
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                return count == 0;
            }
            catch
            {
                return false; // 查询失败 = 不安全
            }
        }

        /// <summary>
        /// 安全检查：确认表中所有行的 HeroId 均为 NULL 或 0。
        /// 仅当此方法返回 true 时，才可安全调用 BackfillHeroIdBulk。
        /// </summary>
        public static bool IsSafeToBackfillHeroId()
        {
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnectionRoot);
                conn.Open();
                // 注意：查询时不区分大小写（MySQL 默认），用小写 hero_index 查询
                using var cmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM mir3.hero_index WHERE heroId IS NOT NULL AND heroId != 0", conn);
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                return count == 0;
            }
            catch
            {
                return false; // 查询失败 = 不安全
            }
        }
    }
}
