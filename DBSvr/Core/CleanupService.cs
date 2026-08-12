using MySql.Data.MySqlClient;
using System;

namespace DBSvr.Core
{
    /// <summary>
    /// 不活跃角色清理 + 孤立数据清理 + 废弃(超旧)角色清理。
    /// 对应原版 DBServer 三组启动期维护例程:
    ///   · AutoClear 族 (DBService.ini [Server] AutoClear, 键名字面量 0x5BD100):
    ///       0x5BD114 "删除等级小于8级的小号..."
    ///       0x5BD138 Create Temporary Table Del_Temp_Idx(Idx int Primary Key);
    ///       0x5BD17C Insert Into Del_Temp_Idx select Idx from user_index
    ///                where Level&lt;8 and Now()&gt;Date_Add(ModifyDate, interval 15 Day);
    ///       0x5BD1F8 select High_Priority Count(*) from Del_Temp_Idx
    ///       0x5BD230 delete user_index from user_index,del_temp_idx where ...
    ///       0x5BD290 delete user_data  from user_data,del_temp_idx  where ...
    ///       0x5BD30C drop Temporary Table Del_Temp_Idx;
    ///       0x5BD358 show Databases like "guild";
    ///       0x5BD380 delete from guild.guild_user where charname not in (...);
    ///   · 废弃角色族 (例程 0x5C9E40..0x5C9F5A) — 见 CleanAncientCharacters。
    ///   · 孤儿英雄族 (例程 0x5C9B60..0x5C9CA2) — 见 CleanOrphanData。
    ///
    /// 前缀约定: 原版靠 0x5BAD84 `use mir3;` (全二进制唯一一条 use, 已普查) 把无
    /// 前缀表名归到 mir3。本实现连接串已带 database=mir3 (DBShare.cs:39), 语义等价。
    /// 但**删除类语句一律保留 `mir3.` 显式前缀**: 万一有人改了连接串默认库,
    /// DELETE 不会打到别的库上。这是刻意的加固, 与原版行为在 mir3 下逐条等价。
    /// </summary>
    public class CleanupService
    {
        // 0x5BD17C: `Level<8 and Now()>Date_Add(ModifyDate, interval 15 Day)`
        private const int NativeInactiveLevelLimit = 8;
        private const int NativeInactiveDays = 15;

        // 0x5CA000 把 60 写死在语句里, 原版没有把它参数化的通路。
        private const int NativeAncientLevelLimit = 60;

        private readonly string _connStr;

        public CleanupService(string connStr) { _connStr = connStr; }

        /// <summary>
        /// 清理不活跃角色。原版 AutoClear 的第一段, 用临时表 Del_Temp_Idx 分段删除。
        ///
        /// 谁会被删: mir3.user_index 中 Level&lt;8 且 ModifyDate 距今超过 15 天的行,
        /// 以及这些 idx 对应的 mir3.user_data 行。Level&gt;=8 的行一律不动。
        /// </summary>
        public int CleanInactiveCharacters()
        {
            int deleted = 0;
            using var conn = OpenConn();
            if (conn == null) return 0;
            try
            {
                DropTemporaryTable(conn, "Del_Temp_Idx");
                // Step 1: 创建临时表 — 0x5BD138
                using var c0 = new MySqlCommand("CREATE TEMPORARY TABLE Del_Temp_Idx(Idx INT PRIMARY KEY)", conn);
                c0.ExecuteNonQuery();

                // Step 2: 找出待删除的 Idx — 0x5BD17C
                using var c1 = new MySqlCommand(
                    @"INSERT INTO Del_Temp_Idx
                      SELECT Idx FROM mir3.user_index
                      WHERE Level < @maxLev AND NOW() > DATE_ADD(ModifyDate, INTERVAL @days DAY)", conn);
                c1.Parameters.AddWithValue("@maxLev", NativeInactiveLevelLimit);
                c1.Parameters.AddWithValue("@days", NativeInactiveDays);
                c1.ExecuteNonQuery();

                // Step 3: 检查数量 — 0x5BD1F8 逐字带 High_Priority
                using var c2 = new MySqlCommand("SELECT HIGH_PRIORITY COUNT(*) FROM Del_Temp_Idx", conn);
                int count = Convert.ToInt32(c2.ExecuteScalar());
                if (count == 0) return 0;

                // Step 4: 关联删除。原版顺序是先 user_index (0x5BD230) 后 user_data
                // (0x5BD290) —— 两条都按临时表 join, 与顺序无关, 但按原版顺序排。
                using var c3 = new MySqlCommand(
                    "DELETE user_index FROM mir3.user_index INNER JOIN Del_Temp_Idx ON user_index.Idx = Del_Temp_Idx.Idx", conn);
                c3.ExecuteNonQuery();
                using var c4 = new MySqlCommand(
                    "DELETE user_data FROM mir3.user_data INNER JOIN Del_Temp_Idx ON user_data.Idx = Del_Temp_Idx.Idx", conn);
                c4.ExecuteNonQuery();

                deleted = count;
                // 0x5BD2EC "共删除 " + 0x5BD2FC " 个小号"
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
        ///
        /// 谁会被删: 四条都是"父行已不存在"的孤儿行, 不会删任何有父行的数据。
        ///   0x5C9DA0 mir3.hero_data  中 idx 不在 mir3.hero_index 里的行
        ///   0x5CA074 mir3.user_data  中 idx 不在 mir3.user_index 里的行
        ///   0x5C9D3C mir3.hero_index 中 masterName 不在 mir3.user_index.chrName 里的行
        ///   0x5BD380 guild.guild_user 中 charname 不在 user_index.chrname 里的行
        ///
        /// 归属说明(不作判据): 原版把前三条放在孤儿英雄例程 0x5C9B60(带 0x5C9CD0
        /// 计数门)与废弃角色例程 0x5C9E40 里, 第四条放在 AutoClear 尾段;
        /// 三个例程都由启动期依次调用。本实现把它们并到一个方法, 语句文本逐条对应。
        /// 0x5CA074 在原版只由 0x5C9EBB 一处发出(属废弃角色族), 此处再发一次是
        /// 幂等的孤儿扫除, 不会多删任何有父行的行。
        /// </summary>
        public int CleanOrphanData()
        {
            int total = 0;
            using var conn = OpenConn();
            if (conn == null) return 0;

            try
            {
                // 0x5C9CD0 孤儿英雄计数门 —— 原版在删除 hero_index 前先计数。
                // 为 0 时不报日志(原版行为), 但其他孤儿行仍应清理。
                using (var countCmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM mir3.hero_index WHERE MasterName NOT IN (SELECT ChrName FROM mir3.user_index)", conn))
                {
                    int orphanHeroCount = Convert.ToInt32(countCmd.ExecuteScalar());
                    if (orphanHeroCount > 0)
                        DBShare.MainOutMessage($"[Cleanup] 发现 {orphanHeroCount} 个孤儿英雄索引");
                }

                string[] orphans =
                {
                    // 0x5C9DA0 逐字
                    "DELETE FROM mir3.hero_data WHERE idx NOT IN (SELECT idx FROM mir3.hero_index)",
                    // 0x5CA074 逐字
                    "DELETE FROM mir3.user_data WHERE idx NOT IN (SELECT idx FROM mir3.user_index)",
                    // 0x5C9D3C 逐字 (原版列名小写 masterName/chrName; MySQL 列名不区分大小写)
                    "DELETE FROM mir3.hero_index WHERE MasterName NOT IN (SELECT ChrName FROM mir3.user_index)"
                };

                foreach (var sql in orphans)
                {
                    using var cmd = new MySqlCommand(sql, conn);
                    total += cmd.ExecuteNonQuery();
                }

                // guild 库存在性门 — 字面量 0x5BD358 `show Databases like "guild";`
                // 紧邻 0x5BD380 的 guild_user 删除。⚠️ 该门的接线不可读: 发出这两条
                // 的函数被 VMP 虚拟化, 两条字面量在 CODE 段均 0 dword 引用, 故
                // "0x5BD358 门住 0x5BD380" 是字面量池相邻性推断, 不是控制流证据。
                // 无论如何加这个门只会**缩小**删除面(库不存在时跳过), 属 fail-safe。
                bool guildExists;
                using (var probe = new MySqlCommand("SHOW DATABASES LIKE 'guild'", conn))
                using (var reader = probe.ExecuteReader())
                    guildExists = reader.Read();

                if (guildExists)
                {
                    // 0x5BD380 逐字
                    using var guildCmd = new MySqlCommand(
                        "DELETE FROM guild.guild_user WHERE CharName NOT IN (SELECT ChrName FROM mir3.user_index)", conn);
                    total += guildCmd.ExecuteNonQuery();
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
        /// 清理废弃(超旧)角色。原版例程 0x5C9E40..0x5C9F5A, 逐字三条语句、**不用临时表**。
        ///   0x5C9E68 log   0x5C9F64 "正在删除废弃角色数据"
        ///   0x5C9E77 exec  0x5C9F84 select count(*) from mir3.user_index where
        ///                  year(modifyDate) &lt;= 2008 or (year(modifyDate) &lt; 2010
        ///                  and level &lt;= 60);
        ///   0x5C9EA3 cmp dword[ebp-0xC],0 / jle 0x5C9ED7  → count&lt;=0 直接算成功, 不删
        ///   0x5C9EA5 exec  0x5CA000 delete from mir3.user_index where
        ///                  (year(modifyDate) &lt;= 2008) or (year(modifyDate) &lt; 2010
        ///                  and level &lt;= 60);
        ///   0x5C9EBB exec  0x5CA074 delete from mir3.user_data where idx not in
        ///                  (select idx from mir3.user_index);
        ///   0x5C9F28 err   0x5CA0F8 "[Error]: 删除角色数据出错"
        ///
        /// ⚠️ 改动前后"谁会被删"的差异 —— 本次是**扩大**删除面, 按原版字节:
        ///   改动前(C# 自造): 临时表 Ancient_Temp_Idx 收集
        ///     `IsDelete=0 AND (YEAR&lt;=2008 OR (YEAR&lt;2010 AND Level&lt;=maxLevel))`,
        ///     只删这批 idx 的 user_index + user_data。
        ///   改动后(原版): 谓词**没有 IsDelete=0**, 故已标记删除的行现在也一并物理删除;
        ///     且第二条是 user_data 的**全表孤儿扫除**, 不限于刚孤立的那批 idx ——
        ///     库里任何 idx 不在 user_index 里的 user_data 行都会被删。
        ///   两处都是原版行为(0x5CA000 / 0x5CA074 逐字), 不是本实现新增的删除。
        ///   `Ancient_Temp_Idx` 在 CODE 快照 0 命中(正对照 `Del_Temp_Idx` 8 命中,
        ///   证明搜索式有效), 该临时表机制原版不存在。
        /// </summary>
        /// <param name="maxLevel">
        /// 保留该形参是为了不改公共签名。原版把 60 **写死**在 0x5CA000 / 0x5C9F84 的
        /// 语句文本里, 没有把它参数化的通路; 传入非 60 的值无法忠实执行, 故拒绝并记日志,
        /// 而不是伪造一条原版不存在的参数化语句。
        /// </param>
        public int CleanAncientCharacters(int maxLevel = 60)
        {
            if (maxLevel != NativeAncientLevelLimit)
            {
                DBShare.MainOutMessage($"[CleanupAncient] 跳过: 原版 0x5CA000 把 level <= "
                    + $"{NativeAncientLevelLimit} 写死在语句里, 无 maxLevel={maxLevel} 的通路");
                return 0;
            }

            using var conn = OpenConn();
            if (conn == null) return 0;
            try
            {
                // 0x5C9F64
                DBShare.MainOutMessage("[Cleanup] 正在删除废弃角色数据");

                // 0x5C9F84 逐字。注意原版计数句的第一个条件**不带括号**,
                // 而删除句 0x5CA000 带括号 —— 原版自己不一致, 两条都逐字照抄。
                using var countCmd = new MySqlCommand(
                    @"SELECT COUNT(*) FROM mir3.user_index
                      WHERE year(ModifyDate) <= 2008
                         OR (year(ModifyDate) < 2010 AND Level <= 60)", conn);
                int deleted = Convert.ToInt32(countCmd.ExecuteScalar());

                // 0x5C9EA3 `jle` —— 计数为 0 时原版不发任何 delete
                if (deleted <= 0) return 0;

                // 0x5CA000 逐字: 无 IsDelete 过滤, 60 写死
                using var deleteIndex = new MySqlCommand(
                    @"DELETE FROM mir3.user_index
                      WHERE (year(ModifyDate) <= 2008)
                         OR (year(ModifyDate) < 2010 AND Level <= 60)", conn);
                deleteIndex.ExecuteNonQuery();

                // 0x5CA074 逐字: user_data 全表孤儿扫除(不是按刚删的 idx join)
                using var deleteData = new MySqlCommand(
                    "DELETE FROM mir3.user_data WHERE idx NOT IN (SELECT idx FROM mir3.user_index)", conn);
                deleteData.ExecuteNonQuery();

                // 0x5CA0CC "已成功删除角色数据, 共" + 0x5CA0EC "条"
                DBShare.MainOutMessage($"[Cleanup] 已成功删除角色数据, 共 {deleted} 条");
                return deleted;
            }
            catch (Exception ex)
            {
                // 0x5CA0F8
                DBShare.MainOutMessage($"[CleanupAncient] 错误: {ex.Message}");
            }
            return 0;
        }

        private static void DropTemporaryTable(MySqlConnection conn, string tableName)
        {
            try
            {
                // 0x5BD30C `drop Temporary Table Del_Temp_Idx;` — 原版无 IF EXISTS,
                // 只在尾段 drop 一次; 本实现前后各 drop 一次且吞异常, 等价且更稳。
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
