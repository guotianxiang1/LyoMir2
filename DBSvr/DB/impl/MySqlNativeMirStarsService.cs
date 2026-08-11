using System;
using System.Collections.Generic;
using DBSvr.Core;
using MySql.Data.MySqlClient;

namespace DBSvr
{
    /// <summary>
    /// <see cref="INativeMirStarsService"/> 的 MySQL 实现。SQL 逐字照抄原版字面量。
    /// </summary>
    public sealed class MySqlNativeMirStarsService : INativeMirStarsService
    {
        // 0x479148 rc=-1 len=113 —— 逐字节照抄，含原版的大小写不一致
        // （小写 select/from/where + 大写 Order by）与结尾分号。
        private const string NativeMirStarsSex0 =
            "select ChrName, nValue from gamedata.mirStars where sex = 0 "
            + "Order by nValue desc, level desc, exp desc limit 100;";

        // 0x4791C4 rc=-1 len=113 —— 与上一条只差 sex 常量，是**另一条**语句。
        private const string NativeMirStarsSex1 =
            "select ChrName, nValue from gamedata.mirStars where sex = 1 "
            + "Order by nValue desc, level desc, exp desc limit 100;";

        /// <summary>
        /// 两条语句都不含格式占位符（0x479148 / 0x4791C4 文本内无 %s / %d），
        /// 所以按字面量原样执行，不参数化 —— 参数化反而会改动逐字文本。
        /// </summary>
        public List<NativeMirStarsRow> Load(int sex)
        {
            // 原版只有 sex = 0 / sex = 1 两条写死语句，没有第三条。
            var sql = sex switch
            {
                0 => NativeMirStarsSex0,
                1 => NativeMirStarsSex1,
                _ => null
            };
            if (sql == null)
                throw new ArgumentOutOfRangeException(nameof(sex),
                    "native mirStars has exactly two statements: sex = 0 / sex = 1");

            using var conn = OpenConn();
            if (conn == null) return null;
            try
            {
                using var cmd = new MySqlCommand(sql, conn);
                using var dr = cmd.ExecuteReader();
                var rows = new List<NativeMirStarsRow>();
                while (dr.Read())
                    rows.Add(new NativeMirStarsRow
                    {
                        ChrName = ReadAnsi(dr, 0),
                        Value = dr.IsDBNull(1)
                            ? 0u
                            : unchecked((uint)Convert.ToInt64(dr.GetValue(1)))
                    });
                return rows;
            }
            catch (Exception ex)
            {
                // 捕获 Exception 而不是 MySqlException：原版那个 except 是**无类过滤的
                // catch-all**。判据是它的处理器地址 ——
                //   0x5C1E57  e9 20 24 e4 ff   jmp 0x40427C
                // 而 0x40427C 是 @HandleAnyException：它不遍历任何类表
                // （0x404280 test [eax+4],6 / 0x40428D cmp [eax],0x0EEDFADE 之后
                //  直接转换并进入 handler）。Delphi 的**带类**处理器是 0x4043A8
                // （@HandleOnException），它在 0x4043F0 `mov ebx,[ecx+5]` /
                // 0x4043F3 `lea esi,[ecx+9]` / 0x404402 起的循环里逐个比对类指针 ——
                // 0x5C1DE0 用的不是它。全二进制 e9 跳转普查：0x40427C 有 169 处、
                // 0x4043A8 有 87 处，两者是不同的两族。
                //
                // 原版的查询执行器 0x5C1DE0 把整段 SQL 执行包在 try/except 里：
                //   0x5C1E41  call [vmt+0x14C]                 ; 正常路径取行数
                //   0x5C1E5C… except 块：
                //   0x5C1E80  mov edx,0x5C1F18                 ; `Select High_Priority 1`
                //             （用一条最小语句探活/复位连接）
                //   0x5C1E95  mov [ebp-0xC], 0xFFFFFFFF        ; ★返回 -1
                //   0x5C1EA2  mov edx,0x5C1F38                 ; `Execute SQLQuery Error: `
                //   0x5C1EA7  call 0x404F04 (LStrCatN)          ; + 出错的 SQL 原文
                //   0x5C1EB8  call 0x49A310 (cl=1)              ; 写日志
                // 即：**缺表不会抛到调用方，也不会重试**，只记一行日志并返回 -1。
                // 本侧照此：吞掉异常、记日志、返回 null（= 原版的 -1）。
                // ⚠️ 本部署无 gamedata.mirStars 表，故这条分支是本机唯一可达路径，
                // 但"表存在时返回正确行集"在本机**无法验证**。
                DBShare.MainOutMessage("Execute SQLQuery Error: " + sql
                    + " (" + ex.Message + ")");
                return null;
            }
        }

        private static byte[] ReadAnsi(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return Array.Empty<byte>();
            var value = reader.GetValue(ordinal);
            if (value is byte[] bytes) return (byte[])bytes.Clone();
            return LegacyGbkText.Encode(Convert.ToString(value) ?? string.Empty);
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
