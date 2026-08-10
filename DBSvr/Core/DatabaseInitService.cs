using System;
using MySql.Data.MySqlClient;

namespace DBSvr
{
    /// <summary>
    /// 数据库初始化服务。
    /// 对应原版 DBServer 的授权过程 sub_59D448（活进程 CODE 快照）。
    ///
    /// 原版形态（逐字节核对，每条 header VA-8=ff ff ff ff、VA-4=len32）：
    ///   0x59D584 len=39  Grant all on gamedata.* to GameServer@"
    ///   0x59D5E0 len=38  Grant Select on mir3.* to GameServer@"   ← mir3 只给 SELECT，保真
    ///   0x59D610 len=36  Grant all on guild.* to GameServer@"
    ///   0x59D640 len=38  Grant all on gamelog.* to GameServer@"
    ///   0x59D5B4 len=33  " identified by "<口令>";                ← 口令即该 VA 处字面量
    /// sub_59D448 的循环 idx=1..5，取 Self+0x84 起 5 个 20 字节主机槽
    /// （lea eax,[eax+eax*4] / lea eax,[edx+eax*4+0x84]，空槽 cmp byte[eax],0 跳过），
    /// 把四条 Grant 逐条 LStrCatN(edx=4) 累加进同一个字符串，最后一次性执行。
    /// 故：多语句一次执行 = 原版行为；四条顺序与大小写按上列字面量保持原样。
    /// </summary>
    public static class DatabaseInitService
    {
        // 0x59DA9C len=9 —— [GameServer] 配置节的默认主机字面量（与 0x59DA88 "GameServer"、
        // 0x59DA74 "ListenPort"、0x59DAB0 ":" 同簇）。
        private const string NativeGrantHost = "127.0.0.1";

        /// <summary>
        /// 按原版 sub_59D448 用 root 连接下发四条 Grant。
        /// 原版不单独建用户：Grant ... identified by "..." 隐式创建。
        /// </summary>
        public static bool Initialize()
        {
            try
            {
                using var rootConn = new MySqlConnection(DBShare.DBConnectionRoot);
                rootConn.Open();

                // 逐字照抄 0x59D584 / 0x59D5E0 / 0x59D610 / 0x59D640 + 0x59D5B4 后缀。
                // 口令只出现一处（对应原版单一后缀字面量 0x59D5B4），不复制到别处。
                const string suffix = "\" identified by \"GowM2#facai888\";";
                var grants =
                    "Grant all on gamedata.* to GameServer@\"" + NativeGrantHost + suffix +
                    "Grant Select on mir3.* to GameServer@\"" + NativeGrantHost + suffix +
                    "Grant all on guild.* to GameServer@\"" + NativeGrantHost + suffix +
                    "Grant all on gamelog.* to GameServer@\"" + NativeGrantHost + suffix;

                // 不发 CREATE USER。判据与权衡：
                //  · CREATE USER / create user / Create User 在 CODE 快照 0 命中，是发明。
                //  · 目标库确实是 MySQL 5.1.55-community
                //    （D:\lyom2Release\mud2.0\MySQL\data\*.err 版本横幅，今日仍在跑；
                //     data/mysql 下是 MyISAM .frm 系统表）。5.1 上
                //     `CREATE USER ... IF NOT EXISTS` 是语法错误，整个 try 会在第一条就抛，
                //     后面的 Grant 一条都发不出去 —— 发明的写法在真目标上是坏的。
                //  · 隐式建用户（Grant ... identified by）在 MySQL 8.0 被移除。若将来把部署
                //    换到 8.0+，这四条会失败，届时才需要显式 CREATE USER；那是环境迁移决策，
                //    不是本文件可以替 5.1 目标做的。取舍记在此处，行为按原版。
                //
                // 同时删掉两处发明：
                //  · GameServer@'localhost' —— 原版只授权配置里的主机；CODE 里 'localhost'
                //    唯一命中 0x4A1460 属 URL 解析库，不是授权主机。多授一个主机是扩权。
                //  · FLUSH PRIVILEGES —— FLUSH/flush privileges 在 CODE 快照 0 命中
                //    （'Flush' 的 9 处命中全是备份族的 Flush Table/Tables/Logs）。
                //    Grant 本身即时生效，去掉无副作用。
                using (var cmd = new MySqlCommand(grants, rootConn))
                {
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("[DBInit] Grant 已下发 (GameServer@" + NativeGrantHost + ")");
                rootConn.Close();
            }
            catch (Exception ex)
            {
                // root 连接失败或 Grant 失败（如 skip-grant-tables 模式）——非致命。
                Console.WriteLine($"[DBInit] root 初始化跳过: {ex.Message}");
            }

            // BLOCKED: 原版授权主机是 Self+0x84 起的 5 个配置槽（sub_59D448 idx=1..5），
            // 本侧只有单主机。缺证据：0x59DA88 "GameServer" 等键字面量 dword 引用为 0
            // （读取函数被 VMP 虚拟化），无法坐实该数组就是 DBService.ini 的
            // GameServer1..GameServer5；且 C# 侧 DBShare.sServerAddr 语义是绑定地址
            // （默认 "*"），不能直接当授权主机用。补齐要改 ConfigManager/DBShare，不在本文件。
            //
            // BLOCKED: 原版每条连接自己发 `set wait_timeout=2073600;`
            // （0x58B7EC 等 16 处，注意原版没有 SESSION 关键字）。本侧连接串不含该设置，
            // 也无 per-connection 钩子；补齐点在连接工厂，不在本文件。
            // 另：0x4DC7C8 "SET TRANSACTION ISOLATION LEVEL " 与 0x4ED8D4 起的 SET SESSION
            // 都在数据库访问库那一段（邻居是 'mssql'/'sybase'），不是 DBServer 的游戏语句，
            // 故本文件不再声称原版设置过 READ COMMITTED。
            return true;
        }
    }
}
