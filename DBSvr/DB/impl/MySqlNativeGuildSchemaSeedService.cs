using System;
using MySql.Data.MySqlClient;

namespace DBSvr
{
    /// <summary>
    /// <see cref="INativeGuildSchemaSeedService"/> 的 MySQL 实现。
    /// </summary>
    public sealed class MySqlNativeGuildSchemaSeedService
        : INativeGuildSchemaSeedService
    {
        /// <summary>
        /// 原版 0x5C0700 里的城堡名，GBK 字节 <c>C9 B3 B0 CD BF CB</c>（「沙巴克」）。
        ///
        /// 按**字节**建模而不是 .NET string：<c>Guild.Castle.name</c> 的列类型是
        /// <c>char(64) binary</c>（0x5BF818 DDL），是 latin1_bin 容器装 GBK 字节。
        /// 若绑 .NET string，MySQL 会按连接字符集转码，写进去就不是这 6 个字节了
        /// —— 与 <see cref="Core.LegacyGbkText"/> 存在的理由是同一件事。
        /// 这里直接写字节数组，连 GBK 编码器都不经过，字面量与原版逐字节相同。
        /// </summary>
        private static readonly byte[] NativeCastleNameGbk =
            { 0xC9, 0xB3, 0xB0, 0xCD, 0xBF, 0xCB };

        // 0x5C0700 rc=-1 len=95 逐字：
        //   insert into Guild.Castle(Guid,name) values(1,"<GBK>")
        //   on duplicate key update name = "<GBK>";
        // 原版把 GBK 字节直接内联在 SQL 文本里（双引号包裹）。本侧改用参数绑定：
        //   · 字节内容完全一致；
        //   · 但不再依赖 MySQL 的 ANSI_QUOTES 模式 —— 原版用 " 当字符串定界符，
        //     若目标库开了 ANSI_QUOTES，那条语句会把 "沙巴克" 当标识符而报错。
        //     参数化后与 sql_mode 无关。
        // 语句其余部分（表名、列序、Guid=1、on duplicate key update 只改 name、
        // 结尾分号）逐字保留。同一参数在两处复用，对应原版两处相同字节。
        private const string NativeCastleSeed =
            "insert into Guild.Castle(Guid,name) values(1,@name) "
            + "on duplicate key update name = @name;";

        public bool SeedCastleRow()
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            try
            {
                using var cmd = new MySqlCommand(NativeCastleSeed, conn);
                cmd.Parameters.Add(new MySqlParameter("@name", MySqlDbType.Binary)
                {
                    Value = NativeCastleNameGbk
                });
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                // catch-all 而非 MySqlException：0x5C1E57 `jmp 0x40427C` =
                // @HandleAnyException（无类表遍历；带类的是 0x4043A8）。
                //
                // 原版查询执行器 0x5C1DE0 的 except 块（0x5C1E95 置 -1、
                // 0x5C1EA2 拼 0x5C1F38 `Execute SQLQuery Error: ` + SQL、
                // 0x5C1EB8 写日志）—— 不抛出、不重试。缺库/缺表落这里。
                DBShare.MainOutMessage("Execute SQLQuery Error: "
                    + NativeCastleSeed + " (" + ex.Message + ")");
                return false;
            }
        }

        private static MySqlConnection OpenConn()
        {
            try
            {
                var c = new MySqlConnection(DBShare.DBConnection);
                c.Open();
                using (var sc = new MySqlCommand(
                           "SET SESSION wait_timeout=2073600", c))
                    sc.ExecuteNonQuery();
                return c;
            }
            catch { return null; }
        }
    }
}
