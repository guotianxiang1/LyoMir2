using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Text;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 账号仓库 MySQL 实现 (对应 user_storage 表)。
    /// </summary>
    public class MySqlStorageService : IStorageService
    {
        public bool CreateStorage(string ptid)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生 0x5B9D70 `Insert Into user_storage(PTID) values("%s");`
            // 修正：原来写成 `INSERT INTO mir3.user_storage(...)`，原生此语句
            // **无** schema 前缀（同库内 0x5AB224 才带 mir3.，这种不一致是原版
            // 有意为之），关键字大小写亦按原生逐字还原。
            using var cmd = new MySqlCommand(
                "Insert Into user_storage(PTID) values(@p);", conn);
            cmd.Parameters.AddWithValue("@p", ptid);
            cmd.ExecuteNonQuery();
            return true;
        }

        public byte[] LoadStorage(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            // 原生 0x5ACBB0 `select High_Priority idx, data from user_storage where idx=%u`
            // 修正三处：(1) 丢了 High_Priority（原版对大表 blob 读刻意加此修饰符）；
            // (2) 多加了 mir3. 前缀，原生无；(3) 原生选的是 `idx, data` 两列，
            // 故不能再用 ExecuteScalar（那会取到第一列 idx 当数据），改按列名读 data。
            using var cmd = new MySqlCommand(
                "select High_Priority idx, data from user_storage where idx=@i", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            using var dr = cmd.ExecuteReader();
            if (!dr.Read()) return null;
            var data = dr["data"] as byte[];
            return data != null ? BlobCompressor.TryDecompress(data) : null;
        }

        public byte[] LoadStorageByPtid(string ptid)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            // 原生探针 0x5B9D2C(ShortString, len=55)
            // `Select High_Priority idx from user_storage where PTID="` + PTID + `"`
            // 修正：原来带 `LIMIT 1`，原生**没有** LIMIT（PTID 有 UNIQUE 索引，
            // 原版靠唯一键而非 LIMIT 收敛）；并补回 High_Priority、去掉 mir3. 前缀。
            // 原生该探针只取 idx；此处业务要的是 data，故列表为 C# 投影（见报告）。
            using var cmd = new MySqlCommand(
                "Select High_Priority idx, data from user_storage where PTID=@p", conn);
            cmd.Parameters.AddWithValue("@p", ptid);
            using var dr = cmd.ExecuteReader();
            if (!dr.Read()) return null;
            var data = dr["data"] as byte[];
            return data != null ? BlobCompressor.TryDecompress(data) : null;
        }

        public bool SaveStorage(int idx, byte[] data)
        {
            if (data == null) return false;
            using var conn = OpenConn();
            if (conn == null) return false;
            var compressed = BlobCompressor.Compress(data);
            // 原生没有任何 `update user_storage set data=` 字面量
            // （raw 普查：'set data'/'unhex'/'update user_storage' 在 CODE 快照 0 命中，
            //  正对照 'Insert Into user_storage' 2 命中 ⇒ 搜索有效）。
            // 写路径已定位（spec: staging/tdataset_blob_write_path_20260811.md §4）：
            //   fn 0x5B9928 → SELECT 0x5B9DF0 `Select High_Priority idx, data from
            //   user_storage where idx =` → 编辑锚点 0x5B9C1A → Fields[1]（=`data`，
            //   按自身 SELECT 列序 `idx, data` 交叉验证）→ CreateBlobStream(bmWrite)
            //   0x5B9C3E → Post 0x5B9C71。UPDATE 由 ZEOS resolver 运行时组装
            //   （`UPDATE %s SET %s` @0x4ADF18 + `=?` @0x4ADF0C + ` WHERE ` @0x4ADC04），
            //   构造式为 `UPDATE user_storage SET data=? WHERE Idx=?`。
            // 故本处 UNHEX 方案是**同一效果的不同机制**（MATCH：机制等价），不是发明 SQL；
            // Convert.ToHexString + UNHEX 是无损往返，不增删物品。
            // 不改为逐字：原生侧无可比字面量，任何"逐字"期望值都会是编造的。
            using var cmd = new MySqlCommand(
                "UPDATE mir3.user_storage SET Data=UNHEX(@d) WHERE Idx=@i", conn);
            cmd.Parameters.Add("@d", MySqlDbType.LongText).Value = Convert.ToHexString(compressed);
            cmd.Parameters.AddWithValue("@i", idx);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteStorage(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生 0x5B91B0 首句 `delete from user_storage where idx=%d;`
            // （原生该 VA 是一条双语句串，按 idx 删这半句逐字如此）
            // 修正：去掉 mir3. 前缀 + 关键字还原为原生小写。
            using var cmd = new MySqlCommand(
                "delete from user_storage where idx=@i;", conn);
            cmd.Parameters.AddWithValue("@i", idx);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteStorageByPtid(string ptid)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生 0x5B917C `delete from user_storage where PTID="%s";`
            // 修正：去掉 mir3. 前缀 + 关键字还原为原生小写。
            using var cmd = new MySqlCommand(
                "delete from user_storage where PTID=@p;", conn);
            cmd.Parameters.AddWithValue("@p", ptid);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool RenamePtid(string oldPtid, string newPtid)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            // 原生 0x5AB224 `update mir3.user_storage set PTID="%s" where PTID="%s";`
            // 此句原生**确实带** mir3. 前缀（与同库其余 user_storage 语句不带前缀
            // 形成的不一致是原版有意为之，不得归一化）。仅还原关键字大小写。
            using var cmd = new MySqlCommand(
                "update mir3.user_storage set PTID=@n where PTID=@o;", conn);
            cmd.Parameters.AddWithValue("@n", newPtid); cmd.Parameters.AddWithValue("@o", oldPtid);
            cmd.ExecuteNonQuery();
            return true;
        }

        public (int idx, string ptid) GetStorageInfo(int idx)
        {
            using var conn = OpenConn();
            if (conn == null) return (0, null);
            // 原生 0x5B90F0 `Select High_Priority idx, PTID, data from user_storage where idx=%d`
            // 修正：补回 High_Priority、去掉 mir3. 前缀、列名还原为原生小写 idx/data。
            using var cmd = new MySqlCommand(
                "Select High_Priority idx, PTID, data from user_storage where idx=@i",
                conn);
            cmd.Parameters.AddWithValue("@i", idx);
            using var dr = cmd.ExecuteReader();
            if (dr.Read()) return (dr.GetInt32(0), dr.GetString(1));
            return (0, null);
        }

        public int GetMaxIdx()
        {
            using var conn = OpenConn();
            if (conn == null) return 0;
            // 原生 0x5B3E54 `Select High_Priority Max(idx) as MaxIdx from ` + 表名
            // （该 VA 是拼接模板，表名运行时追加）
            // 修正：原来是 `SELECT COALESCE(MAX(Idx),0)`。原生 Max(idx) 是**裸**聚合，
            // 空表返回 NULL；COALESCE 会把 NULL 变成 0，抹掉"空表"与"最大 idx 为 0"
            // 两种状态的区别。此处按原生保持裸聚合，NULL 在 C# 侧显式判。
            using var cmd = new MySqlCommand(
                "Select High_Priority Max(idx) as MaxIdx from user_storage", conn);
            var value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        public NativeAccountStorageBlobResult LoadNativeStorage(int idx)
        {
            using var connection = OpenConn();
            if (connection == null)
                return new NativeAccountStorageBlobResult { Result = 0 };
            // 原生 0x5B9DF0 concat head `Select High_Priority idx, data from user_storage where idx =`
            // 修正：补回 High_Priority 和两列选择；去掉 mir3. 前缀；列名还原原生小写。
            using var command = new MySqlCommand(
                "Select High_Priority idx, data from user_storage where idx=@idx",
                connection);
            command.Parameters.AddWithValue("@idx", idx);
            using var dr = command.ExecuteReader();
            if (!dr.Read())
                return new NativeAccountStorageBlobResult { Result = 0 };
            var value = dr["data"];
            if (value == null || value == DBNull.Value)
                return NativeAccountStorageBlobCodec.Decode(Array.Empty<byte>());
            return NativeAccountStorageBlobCodec.Decode((byte[])value);
        }

        public List<NativeStorageIndexEntry> GetNativeStoragePage(
            int lastIdx, int limit = 5000)
        {
            var result = new List<NativeStorageIndexEntry>();
            using var connection = OpenConn();
            if (connection == null) return result;
            // 原生 0x005AC630 `select Idx, PTID from User_Storage where Idx>%d order by Idx Limit 5000`
            // 修正：(1) 去掉 mir3. 前缀；(2) 还原表名大小写 User_Storage；
            // (3) 去掉参数化 LIMIT，恢复原生硬编码 Limit 5000（调用方已固定传 5000，行为等价）。
            using var command = new MySqlCommand(
                "select Idx, PTID from User_Storage where Idx>@lastIdx order by Idx Limit 5000",
                connection);
            command.Parameters.AddWithValue("@lastIdx", lastIdx);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var value = reader.GetValue(1);
                result.Add(new NativeStorageIndexEntry
                {
                    Index = reader.GetInt32(0),
                    Account = value is byte[] bytes
                        ? (byte[])bytes.Clone()
                        : Encoding.Latin1.GetBytes(reader.GetString(1))
                });
            }
            return result;
        }

        public int EnsureNativeStorage(byte[] account)
        {
            using var connection = OpenConn();
            if (connection == null) return 0;
            // 原生探针: ShortString @0x5B9D2C len=55
            // `Select High_Priority idx from user_storage where PTID="` + PTID + `"`
            // 修正：去掉 mir3. 前缀和 LIMIT 1（PTID UNIQUE 索引，原生不加 LIMIT）；
            // 补回 High_Priority；列名还原原生小写 idx。
            using (var query = new MySqlCommand(
                       "Select High_Priority idx from user_storage where PTID=@ptid",
                       connection))
            {
                query.Parameters.Add("@ptid", MySqlDbType.Binary).Value =
                    account ?? Array.Empty<byte>();
                var existing = query.ExecuteScalar();
                if (existing != null && existing != DBNull.Value)
                    return Convert.ToInt32(existing);
            }

            try
            {
                // 原生 0x5B9D70 `Insert Into user_storage(PTID) values("%s");`（无 mir3. 前缀）
                using var insert = new MySqlCommand(
                    "Insert Into user_storage(PTID) values(@ptid)",
                    connection);
                insert.Parameters.Add("@ptid", MySqlDbType.Binary).Value =
                    account ?? Array.Empty<byte>();
                insert.ExecuteNonQuery();
            }
            catch { return 0; }

            // 原生 0x5B9DA8 `Select High_Priority LAST_INSERT_ID() from user_storage limit 1`
            // 修正：补回 High_Priority 和 from 子句；原生循环 0x5B9B11..0x5B9B35
            //   mov [ebp-0x18],1  ; cmp [ebp-0x18],0xB ; jne 循环头
            //   => 计数器 1..10 在循环体内取值，到 11 时退出 => 10 次，与 attempt<10 一致。
            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var identity = new MySqlCommand(
                    "Select High_Priority LAST_INSERT_ID() from user_storage limit 1",
                    connection);
                var value = identity.ExecuteScalar();
                if (value != null && value != DBNull.Value
                    && Convert.ToInt32(value) > 0)
                    return Convert.ToInt32(value);
            }
            return 0;
        }

        public bool SaveNativeStorage(int idx, byte[] data)
        {
            byte[] blob;
            try { blob = NativeAccountStorageBlobCodec.Encode(data); }
            catch (ArgumentOutOfRangeException) { return false; }
            using var connection = OpenConn();
            if (connection == null) return false;
            // 订正：本方法的原生对应**不是** 0x5B9DF0 那条（那是 SaveStorage 的，
            // fn 0x5B9928）。本方法对应 fn 0x5B8DFC，SELECT 是
            //   0x5B90F0 len=67 `Select High_Priority idx, PTID, data from user_storage where idx=%d`
            // ——注意它多一列 PTID、且 idx 用 %d 而非串尾拼接。
            // 写路径（spec: staging/tdataset_blob_write_path_20260811.md §4）：
            //   编辑锚点 0x5B8F81 → Fields[**2**]（=`data`，按自身列序 `idx, PTID, data`
            //   交叉验证；ordinal 是 2 不是 1，与上面那条不同）→
            //   CreateBlobStream(bmWrite) 0x5B8FA5 → Post 0x5B8FD8。
            // UPDATE 由 ZEOS resolver 组装，构造式 `UPDATE user_storage SET data=? WHERE Idx=?`。
            // 机制等价（MATCH），非发明 SQL；UNHEX 往返无损，不增删物品。
            // 原生也是 ZEOS（TZQuery VMT 0x57E650，selfptr 已校验），不是 TADOQuery。
            using var command = new MySqlCommand(
                "UPDATE mir3.user_storage SET Data=UNHEX(@data) WHERE Idx=@idx",
                connection);
            command.Parameters.Add("@data", MySqlDbType.LongText).Value =
                Convert.ToHexString(blob);
            command.Parameters.AddWithValue("@idx", idx);
            return command.ExecuteNonQuery() > 0;
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
