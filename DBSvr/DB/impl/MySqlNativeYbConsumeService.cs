using System;
using DBSvr.Core;
using MySql.Data.MySqlClient;

namespace DBSvr
{
    /// <summary>
    /// <see cref="INativeYbConsumeService"/> 的 MySQL 实现。
    /// </summary>
    public sealed class MySqlNativeYbConsumeService : INativeYbConsumeService
    {
        // 0x5C9B3C rc=-1 len=75 —— 原版模板逐字：
        //   SELECT YBConsume FROM gamedata.YBConsume WHERE PTID='%s' AND YBConsume>=%d;
        // 注意表名与列名同名（YBConsume 表里有 YBConsume 列），不是抄错。
        // 本侧把 %s / %d 换成参数占位符：原版是 Format 直接拼进 SQL（0x5C9ACC
        // call 0x40CF30），PTID 未转义 —— 那是注入面，不复刻。文本其余部分逐字保留，
        // 表名/列名/比较方向/结尾分号一字不改。
        private const string NativeYbConsumeQuery =
            "SELECT YBConsume FROM gamedata.YBConsume "
            + "WHERE PTID=@ptid AND YBConsume>=@threshold;";

        public bool? IsOverThreshold(string ptid, int threshold)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            try
            {
                using var cmd = new MySqlCommand(NativeYbConsumeQuery, conn);
                // PTID 列在原生 schema 里是 latin1_bin 装字节（与 user_index.PTID
                // 同族，见 0x5BAF04 的 `PTID Char(20) binary`），故按字节绑定。
                cmd.Parameters.Add(LegacyGbkText.Parameter("@ptid", ptid));
                cmd.Parameters.AddWithValue("@threshold", threshold);

                // 原版判据是**行数**，不是取回的 YBConsume 值：
                //   0x5C9AD7 call 0x5C1DE0 ; 返回 [vmt+0x14C] = RecordCount
                //   0x5C9ADC test eax,eax / jle
                // 所以这里也只数行，不读列值。
                var rows = 0;
                using (var dr = cmd.ExecuteReader())
                    while (dr.Read()) rows++;
                return rows > 0;
            }
            catch (Exception ex)
            {
                // catch-all 而非 MySqlException：0x5C1E57 `jmp 0x40427C` =
                // @HandleAnyException（无类表遍历）。带类的是 0x4043A8
                // @HandleOnException，它在 0x4043F0/0x404402 起逐个比类指针 ——
                // 0x5C1DE0 用的不是它。
                //
                // 原版 0x5C1DE0 的 except 块：记 `Execute SQLQuery Error: ` + SQL
                // （0x5C1F38 rc=-1 len=24）并返回 -1，不抛出、不重试业务语句。
                // ⚠️ 缺表/列不符都落这里。真库已核 gamedata.ybconsume 存在且有数据，
                // 但列结构未取到 ⇒ 列名不符的可能性**未排除**，本机无法验证。
                DBShare.MainOutMessage("Execute SQLQuery Error: "
                    + NativeYbConsumeQuery + " (" + ex.Message + ")");
                return null;
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
