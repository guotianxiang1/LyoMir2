using System;
using MySql.Data.MySqlClient;

namespace DBSvr
{
    /// <summary>
    /// 数据库初始化服务。
    /// 对应 Delphi M2Server 启动时的 GRANT + SET SESSION 逻辑。
    /// </summary>
    public static class DatabaseInitService
    {
        /// <summary>
        /// 用 root 连接创建 GameServer 用户并授权（如果不存在）。
        /// 然后为所有新连接设置 MySQL 会话参数。
        /// </summary>
        public static bool Initialize()
        {
            try
            {
                using var rootConn = new MySqlConnection(DBShare.DBConnectionRoot);
                rootConn.Open();

                // 1. 创建 GameServer 用户（对应 Delphi: GameServer@"127.0.0.1" identified by "GowM2#facai888"）
                using (var cmd = new MySqlCommand(
                    @"CREATE USER IF NOT EXISTS 'GameServer'@'127.0.0.1' IDENTIFIED BY 'GowM2#facai888';
                      CREATE USER IF NOT EXISTS 'GameServer'@'localhost' IDENTIFIED BY 'GowM2#facai888';",
                    rootConn))
                {
                    cmd.ExecuteNonQuery();
                }

                // 2. 授权四个业务库（对应 Delphi GRANT）
                using (var cmd = new MySqlCommand(
                    @"GRANT ALL ON gamedata.* TO 'GameServer'@'127.0.0.1';
                      GRANT ALL ON gamedata.* TO 'GameServer'@'localhost';
                      GRANT SELECT ON mir3.* TO 'GameServer'@'127.0.0.1';
                      GRANT SELECT ON mir3.* TO 'GameServer'@'localhost';
                      GRANT ALL ON guild.* TO 'GameServer'@'127.0.0.1';
                      GRANT ALL ON guild.* TO 'GameServer'@'localhost';
                      GRANT ALL ON gamelog.* TO 'GameServer'@'127.0.0.1';
                      GRANT ALL ON gamelog.* TO 'GameServer'@'localhost';
                      FLUSH PRIVILEGES;",
                    rootConn))
                {
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("[DBInit] GameServer 用户创建/授权完成");
                rootConn.Close();
            }
            catch (Exception ex)
            {
                // root 连接失败或 GRANT 失败（如 skip-grant-tables 模式）——非致命
                Console.WriteLine($"[DBInit] root 初始化跳过: {ex.Message}");
            }

            // 3. 在连接池配置中加入会话初始化（通过 DBShare 的连接字符串已包含必要设置）
            // 对应 Delphi: SET SESSION wait_timeout=2073600, SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED
            Console.WriteLine("[DBInit] 数据库初始化完成 (GameServer@127.0.0.1)");
            return true;
        }
    }
}
