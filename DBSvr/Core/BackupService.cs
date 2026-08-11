using MySql.Data.MySqlClient;
using System;
using System.Diagnostics;

namespace DBSvr.Core
{
    /// <summary>
    /// 热备份服务。
    /// 对应 Delphi 原版:
    ///   Layer 1: MySQL 热备份 → mir3_backup 库 (LOW_PRIORITY INSERT ... SELECT)
    ///            例程 0x5B2FAB..0x5B3352, 语句 0x5B33A0..0x5B39B5
    ///   Layer 2: 表维护 (AutoRepair 族, 键名字面量 0x5BCF80) — Check/Repair/OPTIMIZE
    ///   Layer 3: RAR 物理备份 (通过 backup.exe / rar.exe)
    ///
    /// 前缀约定: 原版靠 0x5BAD84 `use mir3;` 把无前缀表名归 mir3, 本实现连接串带
    /// database=mir3 (DBShare.cs:39), 语义等价。OPTIMIZE 一族按原版逐字用**无前缀**
    /// 表名 —— 它是只读维护操作, 万一默认库不对也只会报错不会动数据。
    /// </summary>
    public class BackupService
    {
        private readonly string _connStr;
        private readonly string _backupDir;

        public BackupService(string connStr, string backupDir = @".\Backup\")
        {
            _connStr = connStr;
            _backupDir = backupDir;
        }

        /// <summary>
        /// 热备份到 mir3_backup 库。
        /// 使用 LOW_PRIORITY INSERT ... SELECT 不阻塞读操作。
        /// </summary>
        public bool HotBackupToMir3Backup()
        {
            using var conn = OpenConn();
            if (conn == null) return false;

            // 原版顺序(按 exec 站点升序 0x5B3053→0x5B3296): hero_index, hero_data,
            // user_storage, dominatorpet, user_index, user_data。按原版排。
            // MaxRows: 原版只给 hero_data(0x5B354C) 与 user_data(0x5B395C) 两张大表
            // 发 `Alter Table ... Max_ROWS=20000000000;`, 其余四张不发。
            var tables = new (string Name, bool MaxRows)[]
            {
                ("hero_index",   false),   // 0x5B341C / 0x5B3460
                ("hero_data",    true),    // 0x5B350C / 0x5B354C / 0x5B358C
                ("user_storage", false),   // 0x5B3638 / 0x5B3680
                ("dominatorpet", false),   // 0x5B3734 / 0x5B377C
                ("user_index",   false),   // 0x5B382C / 0x5B3870
                ("user_data",    true),    // 0x5B391C / 0x5B395C / 0x5B399C
            };

            try
            {
                // 1. 确保备份库存在 — 0x5B33A0 `Create DataBase mir3_backup;`
                //    原版无 IF NOT EXISTS(靠报错被吞); 这里加上, 重复运行等价且更稳。
                using var c1 = new MySqlCommand("CREATE DATABASE IF NOT EXISTS mir3_backup", conn);
                c1.ExecuteNonQuery();

                // 2. 克隆表结构 + 复制数据
                foreach (var (table, needMaxRows) in tables)
                {
                    // 创建表 — 0x5B341C 族 `Create Table mir3_backup.X like mir3.X;`
                    using var createCmd = new MySqlCommand(
                        $"CREATE TABLE IF NOT EXISTS mir3_backup.{table} LIKE mir3.{table}", conn);
                    createCmd.ExecuteNonQuery();

                    // MyISAM 行数上限 — 0x5B354C / 0x5B395C 逐字 20000000000。
                    // 缺这条会让备份表沿用建表时推算的 MAX_ROWS, 大表复制可能中途
                    // 报 "table is full"。原版只对这两张大表发。
                    if (needMaxRows)
                    {
                        using var maxRowsCmd = new MySqlCommand(
                            $"ALTER TABLE mir3_backup.{table} MAX_ROWS=20000000000", conn);
                        maxRowsCmd.ExecuteNonQuery();
                    }

                    // 批量复制 — 0x5B3460 族 `Insert LOW_PRIORITY Into ... select * from ...`
                    using var copyCmd = new MySqlCommand(
                        $"INSERT LOW_PRIORITY INTO mir3_backup.{table} SELECT * FROM mir3.{table}", conn);
                    copyCmd.ExecuteNonQuery();
                }

                // 3. FLUSH — 0x5B63E8 / 0x5B7EE4 `Flush Tables;`
                //    ⚠️ OPTIMIZE 不在本例程: 四条 OPTIMIZE 字面量(0x5BD070..0x5BD0DC)
                //    位于 AutoRepair 池(0x5BCF80 键名 / 0x5BD058 "优化数据表...")内,
                //    与热备份池(0x5B33A0..0x5B39B5)分属两段。原先此处那条
                //    `OPTIMIZE TABLE mir3.user_data, mir3.hero_data` 是 C# 自造的
                //    合并写法(且只含 4 张表里的 2 张), 已移入 RepairTables()。
                using var flushCmd = new MySqlCommand("FLUSH TABLES", conn);
                flushCmd.ExecuteNonQuery();

                DBShare.MainOutMessage("[Backup] 热备份完成 → mir3_backup");
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[Backup] 错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// FLUSH 指定表到磁盘。
        ///
        /// 原版这一族有四种写法, 全部已定位:
        ///   0x5B63E8 / 0x5B7EE4 `Flush Tables;`               —— 全库, 无参
        ///   0x5B6530             `Flush Logs;`
        ///   0x5B415C / 0x5C1C88  `Flush Table ` + 表名(builder 拼接)
        ///   0x5B47D0 / 0x5B83A8  `Flush Table mir3_backup.` + 表名 + `;`(0x5B83CC)
        /// BLOCKED: 本方法这三张表的集合(mir3.user_data / mir3.hero_data /
        /// mir3_backup.user_data)在原版没有对应字面量 —— 原版是运行时按调用方传入的
        /// 表名拼接的, 而拼接点的调用方(0x5B4657 / 0x5B81F3 所在函数)被 VMP 虚拟化,
        /// 读不到它遍历的表集合。故不改这个集合, 也不据此下"C# 错了"的结论。
        /// FLUSH TABLE 只是把表刷盘并关闭句柄, 集合多一张少一张不损坏数据。
        /// </summary>
        public bool FlushTables()
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            try
            {
                // BLOCKED(见上): 集合无字节依据, 沿用原有三张。
                string[] tables = { "mir3.user_data", "mir3.hero_data", "mir3_backup.user_data" };
                foreach (var t in tables)
                {
                    using var cmd = new MySqlCommand($"FLUSH TABLE {t}", conn);
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[FlushTables] 错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// RAR 物理备份 MyISAM 文件。打包 .MYI / .MYD / .frm 三类。
        ///
        /// 原版对应(例程 0x5B65C0..0x5B6980, 列表文件名 0x5B69D4 `RarFileList.lst`):
        ///   0x5B6B44 len=58 `a -dh  -r -ri0:1 -ilogd:\mud2\backup\Error_Mirback.log -hp`
        ///   0x5B6B88 len=25 ` d:\mud2\backup\mirback @`
        ///   文件名字面量按表逐条写死, 顺序 0x5B79A8..0x5B7B54:
        ///     user_index / user_data / user_storage / hero_index / hero_data /
        ///     dominatorpet, 每张各 .MYI, .MYD, .frm 三条。
        ///
        /// 与原版的刻意偏离(全部为可配置化, 非行为差异):
        ///   · 路径: 原版写死 d:\mud2\backup\ (0x5B6AE0) 与
        ///     C:\Program Files\WinRAR\Rar.exe (0x5C2D98, 该条本实现逐字沿用);
        ///     本实现改成 _backupDir 参数。
        ///   · 原版另写 mirback.rar.MD5 (0x5B6B1C) 并调外部上传程序
        ///     backup.exe (0x5B6BC4, 0x5B6BE8 "执行外部上传程序")。
        ///     BLOCKED: 生成 MD5 与调用上传的例程未定位, 不猜, 本实现不做这两步。
        /// </summary>
        public bool RarBackup(string mysqlDataDir, string password = null)
        {
            try
            {
                // 原版文件名字面量顺序(0x5B79A8 起, 按 VA 升序), 与热备份的表序不同。
                var tables = new[] { "user_index", "user_data", "user_storage", "hero_index", "hero_data", "dominatorpet" };
                // 0x5B6B44 逐字。-ri0:1 = 进程优先级 0 + 每次 IO 让出 1ms; 原实现漏了
                // 这一段, 缺它会让压缩与在线游戏服抢 CPU/磁盘。原版 -dh 后是**两个空格**,
                // 这里保留(命令行解析上等价, 但按字节抄)。
                var rarArgs = "a -dh  -r -ri0:1";
                password ??= DBShare.DBBackupPassword;

                // 构建文件列表 — 原版每表三条显式扩展名, 不是通配。
                // 用 `{t}.*` 会把 .tmd 临时文件(0x5B70AC ".tmd 成功" 证明该扩展存在)
                // 一并打进包里, 那不是原版的打包集合。
                var fileList = new System.Text.StringBuilder();
                foreach (var t in tables)
                {
                    fileList.AppendLine($@"{mysqlDataDir}\mir3\{t}.MYI");
                    fileList.AppendLine($@"{mysqlDataDir}\mir3\{t}.MYD");
                    fileList.AppendLine($@"{mysqlDataDir}\mir3\{t}.frm");
                }

                var listPath = System.IO.Path.Combine(_backupDir, "RarFileList.lst");
                var backupPath = System.IO.Path.Combine(_backupDir, "mirback.rar");
                System.IO.File.WriteAllText(listPath, fileList.ToString());

                // 调用 rar.exe
                var psi = new ProcessStartInfo
                {
                    FileName = @"C:\Program Files\WinRAR\Rar.exe",
                    Arguments = $"{rarArgs} -ilog{_backupDir}Error_Mirback.log -hp{password} \"{backupPath}\" @\"{listPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(300000); // 5分钟超时

                DBShare.MainOutMessage("[Backup] RAR 物理备份完成 → " + backupPath);
                return proc?.ExitCode == 0;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[RarBackup] 错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 修复 + 优化 MyISAM 表。对应原版 AutoRepair 族
        /// (DBService.ini [Server] AutoRepair, 键名字面量 0x5BCF80 / 0x5C94A4)。
        ///
        /// 原版有**两个**同构 builder, 各自 Check/Repair/Flush 一套字面量:
        ///   builder A (mir3_backup 侧, 前缀 0x5B40F0 `mir3_backup.`):
        ///     0x5B3D50 "Check Table "  0x5B4120 "Repair Table "  0x5B415C "Flush Table "
        ///   builder B (库名由参数传入, 见 0x5C17B4 `gamedata`):
        ///     0x5C188C "Check Table "  0x5C1C4C "Repair Table "  0x5C1C88 "Flush Table "
        /// 两侧都是**先 Check、Check 失败才 Repair**, 字节一致:
        ///   A: 0x5B3F37 call CheckTable / 0x5B3F3D test al,al / 0x5B3F3F jne →跳过 Repair
        ///      0x5B3F4B 载入 "Repair Table " / 0x5B3F5B exec / 0x5B3F62 jle
        ///      0x5B3F68 再 Check / 0x5B3F76 再验 MaxIdx
        ///   B: 0x5C1A78 call CheckTable / 0x5C1A7E test al,al / 0x5C1A80 jne →跳过 Repair
        ///      0x5C1A8C 载入 "Repair Table " / 0x5C1A9C exec / 0x5C1AA3 jle
        ///      0x5C1AA9 再 Check / 0x5C1AB7 再验
        /// Check 的判据是取结果集 Msg_text 列(0x5B3D68)与 "OK"(0x5B3D7C)比较。
        /// 原实现无条件先 REPAIR 再 CHECK, 与两侧字节都反。REPAIR 会重建整张 MyISAM
        /// 表并锁写, 健康表上白跑一遍是纯开销, 故改为按原版 Check→(坏才)Repair。
        ///
        /// 表集合归属声明: OPTIMIZE 一族原版只有四张(user_index / user_data /
        /// hero_index / hero_data, 0x5BD070..0x5BD0DC), 与 AutoRepair 池里
        /// 0x5BCFA4"尝试修复用户数据表..." + 0x5BD010"尝试修复英雄数据表..." 两段吻合。
        /// user_storage / dominatorpet 在 OPTIMIZE 族**无**对应字面量, 故只参与
        /// Check/Repair 不参与 OPTIMIZE。Check/Repair 那两个 builder 的表集合由
        /// 调用方传入, 调用方被 VMP 虚拟化不可读 ⇒ BLOCKED: 六张表这个集合无字节
        /// 依据, 沿用原实现不动(CHECK/REPAIR 对健康表无损)。
        ///
        /// ⚠️ 接线状态: DBShare.boAutoRepair 已由 ConfigManager.cs:50 读入, 但全仓
        /// 无任何地方调用本方法 ⇒ 本方法目前 dormant。接线归 MainForm 所有, 不在本次范围。
        /// </summary>
        public bool RepairTables()
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            try
            {
                // BLOCKED: 表集合无字节依据(见 XML 注释), 沿用原有六张。
                string[] tables = { "user_index", "user_data", "hero_index", "hero_data", "user_storage", "dominatorpet" };
                foreach (var t in tables)
                {
                    // 0x5B3F37 / 0x5C1A78: 先 Check
                    if (CheckTableIsOk(conn, t)) continue;

                    // 每张表单独兜异常: 原版 builder 对每张表返回一个 bool, 一张失败
                    // 不会中断调用方的遍历(0x5B3F3F / 0x5C1A80 都只是跳过本张)。
                    // 若不兜, 一张表不存在就会掀掉后面所有表的 Check/Repair 与 OPTIMIZE。
                    try
                    {
                        // 0x5B4120 / 0x5C1C4C: Check 说不 OK 才 Repair
                        DBShare.MainOutMessage($"[RepairTables] 尝试修复数据表: mir3.{t}");
                        using (var cmd = new MySqlCommand($"REPAIR TABLE mir3.{t}", conn))
                            cmd.ExecuteNonQuery();

                        // 0x5B3F68 / 0x5C1AA9: Repair 后复验
                        if (!CheckTableIsOk(conn, t))
                            DBShare.MainOutMessage($"[RepairTables] mir3.{t} 修复后复验仍未 OK");
                    }
                    catch (Exception exTable)
                    {
                        DBShare.MainOutMessage($"[RepairTables] mir3.{t} 修复失败: {exTable.Message}");
                    }
                }

                // 0x5BD058 "优化数据表..." 紧邻四条 OPTIMIZE 字面量。
                // 逐字无 `mir3.` 前缀(原版靠 0x5BAD84 `use mir3;`; 本实现连接串
                // database=mir3 等价)。原先只有 HotBackupToMir3Backup 里一条合并的
                // `OPTIMIZE TABLE mir3.user_data, mir3.hero_data`, 缺 user_index 与
                // hero_index 两张; 现按原版四条、原版顺序逐条发。
                DBShare.MainOutMessage("[RepairTables] 优化数据表...");
                string[] optimize =
                {
                    "OPTIMIZE TABLE user_index;",   // 0x5BD070 len=26 逐字
                    "OPTIMIZE TABLE user_data;",    // 0x5BD094 len=25 逐字
                    "OPTIMIZE TABLE hero_index;",   // 0x5BD0B8 len=26 逐字
                    "OPTIMIZE TABLE hero_data;",    // 0x5BD0DC len=25 逐字
                };
                foreach (var sql in optimize)
                {
                    // 逐条兜异常, 同上: 原版四条是顺序发出的独立语句, 一条失败不该
                    // 吞掉后三条。无前缀表名依赖连接串 database=mir3; 若默认库不对,
                    // 这里只会逐条报错, 不会动任何数据。
                    try
                    {
                        using var opt = new MySqlCommand(sql, conn);
                        opt.ExecuteNonQuery();
                    }
                    catch (Exception exOpt)
                    {
                        DBShare.MainOutMessage($"[RepairTables] {sql} 失败: {exOpt.Message}");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[RepairTables] 错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// CHECK TABLE 判据。原版取结果集的 Msg_text 列(字面量 0x5B3D68 / 0x5C18A4)
        /// 与 "OK"(0x5B3D7C / 0x5C18B8) 比较 —— 见 0x5B3CE3 取列 / 0x5B3CEE 比较。
        /// 任一行 Msg_text 不为 OK 即判该表需要 Repair。
        /// </summary>
        private static bool CheckTableIsOk(MySqlConnection conn, string table)
        {
            try
            {
                using var cmd = new MySqlCommand($"CHECK TABLE mir3.{table}", conn);
                using var reader = cmd.ExecuteReader();
                var ok = false;
                while (reader.Read())
                {
                    var msg = reader["Msg_text"]?.ToString();
                    if (!string.Equals(msg, "OK", StringComparison.OrdinalIgnoreCase))
                        return false;
                    ok = true;
                }
                return ok;
            }
            catch
            {
                // 取不到 Msg_text 就当"不 OK", 让 Repair 跑一遍 —— 与原版
                // 0x5B3CF5 `jne` 的默认落点(视为需修复)同向。
                return false;
            }
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
