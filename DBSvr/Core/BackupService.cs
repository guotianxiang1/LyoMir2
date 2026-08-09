using MySql.Data.MySqlClient;
using System;
using System.Diagnostics;

namespace DBSvr.Core
{
    /// <summary>
    /// 热备份服务。
    /// 对应 Delphi 原版:
    ///   Layer 1: MySQL 热备份 → mir3_backup 库 (LOW_PRIORITY INSERT ... SELECT)
    ///   Layer 2: OPTIMIZE / FLUSH TABLES
    ///   Layer 3: RAR 物理备份 (通过 backup.exe / rar.exe)
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

            string[] tables = { "user_index", "user_data", "hero_index", "hero_data", "user_storage", "dominatorpet" };

            try
            {
                // 1. 确保备份库存在
                using var c1 = new MySqlCommand("CREATE DATABASE IF NOT EXISTS mir3_backup", conn);
                c1.ExecuteNonQuery();

                // 2. 克隆表结构 + 复制数据
                foreach (var table in tables)
                {
                    // 创建表
                    using var createCmd = new MySqlCommand(
                        $"CREATE TABLE IF NOT EXISTS mir3_backup.{table} LIKE mir3.{table}", conn);
                    createCmd.ExecuteNonQuery();

                    // 批量复制
                    using var copyCmd = new MySqlCommand(
                        $"INSERT LOW_PRIORITY INTO mir3_backup.{table} SELECT * FROM mir3.{table}", conn);
                    copyCmd.ExecuteNonQuery();
                }

                // 3. 优化表
                using var optCmd = new MySqlCommand(
                    "OPTIMIZE TABLE mir3.user_data, mir3.hero_data", conn);
                optCmd.ExecuteNonQuery();

                // 4. FLUSH
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
        /// </summary>
        public bool FlushTables()
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            try
            {
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
        /// RAR 物理备份 MyISAM 文件。
        /// 打包 .MYD / .MYI / .frm 文件。
        /// </summary>
        public bool RarBackup(string mysqlDataDir, string password = null)
        {
            try
            {
                var tables = new[] { "user_index", "user_data", "hero_index", "hero_data", "user_storage", "dominatorpet" };
                var rarArgs = "a -dh -r";
                password ??= DBShare.DBBackupPassword;

                // 构建文件列表
                var fileList = new System.Text.StringBuilder();
                foreach (var t in tables)
                    fileList.AppendLine($@"{mysqlDataDir}\mir3\{t}.*");

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
        /// 修复 MyISAM 表。
        /// </summary>
        public bool RepairTables()
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            try
            {
                string[] tables = { "user_index", "user_data", "hero_index", "hero_data", "user_storage", "dominatorpet" };
                foreach (var t in tables)
                {
                    using var cmd = new MySqlCommand($"REPAIR TABLE mir3.{t}", conn);
                    cmd.ExecuteNonQuery();
                    using var chk = new MySqlCommand($"CHECK TABLE mir3.{t}", conn);
                    chk.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[RepairTables] 错误: {ex.Message}");
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
