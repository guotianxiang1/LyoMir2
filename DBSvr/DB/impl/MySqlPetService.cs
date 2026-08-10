using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 主宰宠物 MySQL 实现 (对应 dominatorpet 表)。
    /// </summary>
    public class MySqlPetService : IPetService
    {
        private readonly NativeDominatorPetBackupQueue _backupQueue;

        public MySqlPetService(NativeDominatorPetBackupQueue backupQueue) =>
            _backupQueue = backupQueue
                ?? throw new ArgumentNullException(nameof(backupQueue));

        public byte[] LoadPet(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            // 原生 0x0059748C `Select High_Priority Data From dominatorpet where MasterId=%d;`
            // 修正：补回 High_Priority（原版对大表 blob 读刻意加此修饰符）；
            // 去掉 mir3. 前缀（原生此语句无前缀）；关键字大小写逐字还原。
            using var cmd = new MySqlCommand(
                "Select High_Priority Data From dominatorpet where MasterId=@m;", conn);
            cmd.Parameters.AddWithValue("@m", masterId);
            var blob = cmd.ExecuteScalar() as byte[];
            return NativeDominatorPetBlobCodec.TryDecode(blob, out var data,
                out _) ? data : LoadBackupPet(masterId);
        }

        public (int idx, byte[] data) LoadPetWithIdx(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return (0, null);
            // 原生 0x005B948C `Select High_Priority idx, data from dominatorpet where MasterID=%d;`
            // 注意：此句原生把键列拼成 **MasterID**（大写 ID），而 0x59748C/0x5B9548
            // 等处是 MasterId。这种同库内不一致是原版有意为之，不得归一化。
            // 修正：补回 High_Priority、去掉 mir3. 前缀、列名还原原生小写 idx/data。
            using var cmd = new MySqlCommand(
                "Select High_Priority idx, data from dominatorpet where MasterID=@m;", conn);
            cmd.Parameters.AddWithValue("@m", masterId);
            using var dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                var blob = dr["data"] as byte[];
                return (dr.GetInt32(0),
                    NativeDominatorPetBlobCodec.TryDecode(blob, out var data,
                        out _) ? data : LoadBackupPet(masterId));
            }
            return (0, null);
        }

        public bool CreatePet(string masterName, long masterId, int level, int exp)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生 0x00597DC8 / 0x005B94D8（两处逐字相同）
            // `Insert Into dominatorpet(MasterName, MasterId, Level, Exp, CreateDate) values("%s", %d, %d, %d, Now());`
            // 修正：去掉 mir3. 前缀（原生无）；关键字/函数大小写还原为 values(...)、Now()。
            using var cmd = new MySqlCommand(
                "Insert Into dominatorpet(MasterName, MasterId, Level, Exp, CreateDate) values(@n, @m, @l, @e, Now());",
                conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            cmd.Parameters.AddWithValue("@m", masterId);
            cmd.Parameters.AddWithValue("@l", level); cmd.Parameters.AddWithValue("@e", exp);
            cmd.ExecuteNonQuery();
            return true;
        }

        public bool SavePet(long masterId, string masterName, int level,
            int exp, byte[] data)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            if (!NativeDominatorPetBlobCodec.TryEncode(data, out var blob,
                    out _)) return false;
            // 原生 0x00597B2C（blob 保存路径内，@0x5979C5 Format argc=2）
            // `Update dominatorpet Set Level=%d, Exp=%d, ModifyDate=Now() where MasterId=%d;`
            // 修正：去掉 mir3. 前缀（原生无）；Set/Now() 大小写逐字还原。
            using (var index = new MySqlCommand(
                       "Update dominatorpet Set Level=@l, Exp=@e, ModifyDate=Now() where MasterId=@m;",
                       conn))
            {
                index.Parameters.AddWithValue("@l", level);
                index.Parameters.AddWithValue("@e", unchecked((uint)exp));
                index.Parameters.AddWithValue("@m", masterId);
                if (index.ExecuteNonQuery() <= 0) return false;
            }
            // BLOCKED: 原生没有 `Update dominatorpet Set Data=` 字面量
            // （raw 普查 'set data'/'unhex' 在 CODE 快照 0 命中）。原生在 0x597B84
            // 打开数据集后用 TBlobStream 写 data 字段（fn 0x597924，字段名字面量
            // 0x00597BD0 = 'data'），无独立 UPDATE SQL。此 UNHEX 方案为 C# 特有实现。
            using var cmd = new MySqlCommand(
                "UPDATE mir3.dominatorpet SET Data=UNHEX(@d) WHERE MasterId=@m",
                conn);
            cmd.Parameters.Add("@d", MySqlDbType.LongText).Value =
                Convert.ToHexString(blob);
            cmd.Parameters.AddWithValue("@m", masterId);
            if (cmd.ExecuteNonQuery() <= 0) return false;
            _backupQueue.Enqueue(masterName, masterId, level,
                unchecked((uint)exp), blob);
            return true;
        }

        public bool UpdatePetLevel(long masterId, int level, int exp)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生 0x005B957C（type-1 handler 内，@0x5B9371 Format argc=2）
            // `Update dominatorpet Set Level=%d, Exp=%d, ModifyDate=Now() where MasterId=%d;`
            // 修正：去掉 mir3. 前缀（原生无）；Set/Now() 大小写逐字还原。
            using var cmd = new MySqlCommand(
                "Update dominatorpet Set Level=@l, Exp=@e, ModifyDate=Now() where MasterId=@m;", conn);
            cmd.Parameters.AddWithValue("@l", level); cmd.Parameters.AddWithValue("@e", exp); cmd.Parameters.AddWithValue("@m", masterId);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeletePet(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生 0x005B9548 `delete from dominatorpet where MasterId=%d;`
            // 修正：去掉 mir3. 前缀（原生无）；关键字还原为原生全小写。
            using var cmd = new MySqlCommand(
                "delete from dominatorpet where MasterId=@m;", conn);
            cmd.Parameters.AddWithValue("@m", masterId);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool RenameMaster(string oldMaster, string newMaster)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生改名级联腿 fn 0x5A923C，拼接点 0x5A9781..0x5A97A4（3 段 LStrCatN）：
            //   0x005AA0C4 `Update ignore Mir3`
            // + 运行时库名后缀 [ebp-0x14]（默认空）
            // + 0x005AA0E0 `.dominatorpet set MasterName="%s" where MasterName="%s";`
            // => `Update ignore Mir3.dominatorpet set MasterName="%s" where MasterName="%s";`
            //
            // ⚠ 最高危修正：原来**丢了 IGNORE**。原生 `Update ignore` 在改名撞上
            // MasterName 唯一索引时静默跳过该行；无 IGNORE 时 MySQL 抛重复键错误，
            // 改名级联会中途中断——dominatorpet 仍挂旧主人名而其它表已改新名，
            // 按新名查不到宠物 = 宠物连同其身上物品事实性丢失。
            // 同时修正 schema 大小写：原生是 **Mir3**（大写 M），不是 mir3。
            using var cmd = new MySqlCommand(
                "Update ignore Mir3.dominatorpet set MasterName=@n where MasterName=@o;", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", newMaster));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@o", oldMaster));
            cmd.ExecuteNonQuery();
            return true;
        }

        public List<PetIndexInfo> GetPetPage(int lastIdx, int limit = 5000)
        {
            var list = new List<PetIndexInfo>();
            using var conn = OpenConn();
            if (conn == null) return list;
            // 原生 0x00596E94
            // `select Idx, MasterId, MasterName, Level, Exp  from dominatorpet where Idx>%d order by Idx Limit 5000;`
            // 修正：(1) 去掉 mir3. 前缀；(2) 保留原生 `Exp` 与 `from` 之间的**双空格**；
            // (3) 去掉参数化 LIMIT，恢复原生硬编码 Limit 5000（唯一调用方
            //     NativeDominatorPetCache 固定传 5000，行为等价）；
            // (4) 关键字还原为原生全小写 select/where/order by。
            using var cmd = new MySqlCommand(
                "select Idx, MasterId, MasterName, Level, Exp  from dominatorpet where Idx>@l order by Idx Limit 5000;", conn);
            cmd.Parameters.AddWithValue("@l", lastIdx);
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
                list.Add(new PetIndexInfo
                {
                    Idx = dr.GetInt32(0), MasterId = dr.GetInt64(1),
                    MasterName = LegacyGbkText.Read(dr, 2), Level = dr.GetInt32(3),
                    Exp = unchecked((int)Convert.ToUInt32(dr[4]))
                });
            return list;
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

        private static byte[] LoadBackupPet(long masterId)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            try
            {
                using var cmd = new MySqlCommand(
                    "SELECT Data FROM mir3_backup.dominatorpet WHERE MasterId=@m",
                    conn);
                cmd.Parameters.AddWithValue("@m", masterId);
                var blob = cmd.ExecuteScalar() as byte[];
                return NativeDominatorPetBlobCodec.TryDecode(blob,
                    out var data, out _) ? data : null;
            }
            catch { return null; }
        }
    }
}
