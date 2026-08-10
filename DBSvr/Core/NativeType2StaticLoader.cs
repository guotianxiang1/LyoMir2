using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MySql.Data.MySqlClient;

namespace DBSvr.Core
{
    public interface INativeType2StaticLoader
    {
        bool TryLoad(out List<byte[]> records);
    }

    public sealed class MySqlNativeType2StaticLoader : INativeType2StaticLoader
    {
        // === 原生九条静态表加载语句逐字复刻 =================================
        // 每条常量 = 活进程 CODE 快照里 header 校验过的 Delphi 长字符串：
        // [VA-8]=refcount ffffffff、[VA-4]=len32 与文本长度一致、text[len]==0。
        //
        // 修复的是什么：此前本文件用
        //     "SELECT HIGH_PRIORITY * FROM mir3." + table.Name
        // 动态拼接，再按 OrderColumn 追加 " ORDER BY <col>"。运行时 ORDER BY 其实
        // 是发出去的（并非丢失），但仍有四处真背离：(a) 关键字大小写被归一化成全
        // 大写、(b) 多了 mir3. 前缀、(c) 丢了结尾分号、(d) 完整语句文本在源码里
        // 根本不存在 —— SQL 逐字闸 NativeSqlVerbatimCheck 的 FLAG-h 只能对源文本
        // 做子串匹配，对拼接式语句一律判红。改成每表一条常量后四项同时闭合。
        //
        // 大小写/拼写照抄，禁止归一化：原版三条小写 select，SuperForce/SuperSkill
        // 两条大写 Select；排序列 MagicIdx / idx / level / ForceId 各不相同；
        // forcemagic 那条原版**就是没有**结尾分号(len=55)。
        //
        // 无 schema 前缀：全二进制唯一一条 use 是 0x5BAD84 "use mir3;"，故原版这些
        // 不带前缀的表名解析到 mir3.*。C# 侧连接串把默认库钉死为 mir3
        // (DBShare.DBConnection、ConfigManager 均含 database=mir3)，解析到同一张表，
        // 因此去掉 mir3. 前缀既逐字保真又可证等价；原先的前缀属归一化偏离。
        //
        // monster / AntiqueItems / fieldhero / SuperSkill 四张表原版无 order by，
        // 必须保持无序 —— 不得替它们发明排序列。
        private const string HumanMagicSql =   // 0x5C4F34 len=57
            "select High_Priority * from humanmagic order by MagicIdx;";
        private const string HeroMagicSql =    // 0x5C4810 len=56
            "select High_Priority * from heromagic order by MagicIdx;";
        private const string MonsterSql =      // 0x5C5EF4 len=36（原版无 order by）
            "select High_Priority * from monster;";
        private const string StdItemsSql =     // 0x5C6DF8 len=50
            "select High_Priority * from stditems order by idx;";
        private const string AntiqueItemsSql = // 0x5C76AC len=41（原版无 order by）
            "select High_Priority * from AntiqueItems;";
        private const string FieldHeroSql =    // 0x5C3790 len=38（原版无 order by）
            "select High_Priority * from fieldhero;";
        private const string SuperForceSql =   // 0x5C7D90 len=54（大写 Select）
            "Select High_Priority * from SuperForce order by level;";
        private const string SuperSkillSql =   // 0x5C8404 len=39（大写 Select、无 order by）
            "Select High_Priority * from SuperSkill;";
        private const string ForceMagicSql =   // 0x5C4104 len=55（原版无结尾分号）
            "select High_Priority * from forcemagic order by ForceId";

        private static readonly StaticTable[] Tables =
        {
            new(NativeType2StaticRecordBuilder.HumanMagicCommand,
                "humanmagic", HumanMagicSql),
            new(NativeType2StaticRecordBuilder.HeroMagicCommand,
                "heromagic", HeroMagicSql),
            new(NativeType2StaticRecordBuilder.MonsterCommand,
                "monster", MonsterSql),
            new(NativeType2StaticRecordBuilder.StdItemsCommand,
                "stditems", StdItemsSql),
            new(NativeType2StaticRecordBuilder.AntiqueItemsCommand,
                "AntiqueItems", AntiqueItemsSql),
            new(NativeType2StaticRecordBuilder.FieldHeroCommand,
                "fieldhero", FieldHeroSql),
            new(NativeType2StaticRecordBuilder.SuperForceCommand,
                "SuperForce", SuperForceSql),
            new(NativeType2StaticRecordBuilder.SuperSkillCommand,
                "SuperSkill", SuperSkillSql),
            new(NativeType2StaticRecordBuilder.ForceMagicCommand,
                "forcemagic", ForceMagicSql)
        };

        public bool TryLoad(out List<byte[]> records)
        {
            records = new List<byte[]>();
            try
            {
                using var connection = new MySqlConnection(DBShare.DBConnection);
                connection.Open();
                using (var session = new MySqlCommand(
                           "SET WAIT_TIMEOUT = 2073600;", connection))
                    session.ExecuteNonQuery();

                var loaded = new List<byte[]>();
                foreach (var table in Tables)
                {
                    var rows = ReadRows(connection, table);
                    if (table.Command is
                        NativeType2StaticRecordBuilder.HumanMagicCommand or
                        NativeType2StaticRecordBuilder.HeroMagicCommand)
                    {
                        rows = RequireMagicRows(table.Name, rows);
                    }
                    else if (table.Command is
                             NativeType2StaticRecordBuilder.StdItemsCommand or
                             NativeType2StaticRecordBuilder.ForceMagicCommand)
                    {
                        rows = TakeContinuousPrefix(rows,
                            table.Command ==
                            NativeType2StaticRecordBuilder.StdItemsCommand
                                ? "idx"
                                : "ForceId");
                    }

                    loaded.AddRange(NativeType2StaticRecordBuilder.BuildRecords(
                        table.Command, rows));
                }

                records = loaded;
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    "[NativeType2Static] load failed: " + ex.Message);
                records.Clear();
                return false;
            }
        }

        private static List<NativeType2StaticRow> ReadRows(
            MySqlConnection connection, StaticTable table)
        {
            // 语句是每表写死的常量（见上方 VA 注释），不再拼接、不带占位符，
            // 也不接受任何外部值 —— 表名不参与 SQL 文本构造。
            using var command = new MySqlCommand(table.Sql, connection);
            using var reader = command.ExecuteReader();
            var rows = new List<NativeType2StaticRow>();
            while (reader.Read()) rows.Add(ReadRow(reader));
            return rows;
        }

        internal static NativeType2StaticRow ReadRow(MySqlDataReader reader)
        {
            var ansiValues = new Dictionary<string, byte[]>(
                StringComparer.OrdinalIgnoreCase);
            var int32Values = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            var columns = new List<string>(reader.FieldCount);

            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var name = reader.GetName(ordinal);
                columns.Add(name);
                var fieldType = reader.GetFieldType(ordinal);
                if (IsAnsiField(fieldType))
                {
                    ansiValues[name] = ReadAnsi(reader, ordinal);
                    continue;
                }

                if (reader.IsDBNull(ordinal))
                {
                    int32Values[name] = 0;
                    continue;
                }

                var value = reader.GetValue(ordinal);
                if (IsNumericValue(value))
                    int32Values[name] = ToInt32(value);
                else
                    ansiValues[name] = Encoding.Latin1.GetBytes(
                        Convert.ToString(value, CultureInfo.InvariantCulture)
                        ?? string.Empty);
            }

            return new NativeType2StaticRow(
                ansiValues, int32Values, columns);
        }

        internal static List<NativeType2StaticRow> RequireMagicRows(
            string tableName, List<NativeType2StaticRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0)
                throw new InvalidOperationException(
                    $"mandatory native type2 table {tableName} is empty");
            return rows;
        }

        private static List<NativeType2StaticRow> TakeContinuousPrefix(
            List<NativeType2StaticRow> rows, string indexColumn)
        {
            var accepted = new List<NativeType2StaticRow>(rows.Count);
            foreach (var row in rows)
            {
                if (row.RequireInt32(indexColumn) != accepted.Count + 1) break;
                accepted.Add(row);
            }
            return accepted;
        }

        private static bool IsAnsiField(Type fieldType) =>
            fieldType == typeof(string) || fieldType == typeof(char)
                                        || fieldType == typeof(byte[]);

        private static byte[] ReadAnsi(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return Array.Empty<byte>();
            var value = reader.GetValue(ordinal);
            if (value is byte[] bytes) return (byte[])bytes.Clone();
            return Encoding.Latin1.GetBytes(
                Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? string.Empty);
        }

        private static bool IsNumericValue(object value) => value is
            sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal or bool;

        private static int ToInt32(object value)
        {
            if (value is ulong unsignedLong)
                return unchecked((int)unsignedLong);
            return unchecked((int)Convert.ToInt64(
                value, CultureInfo.InvariantCulture));
        }

        private sealed class StaticTable
        {
            public StaticTable(ushort command, string name, string sql)
            {
                Command = command;
                Name = name;
                Sql = sql;
            }

            public ushort Command { get; }

            // Name 只用于诊断消息（RequireMagicRows 的空表异常），不进 SQL 文本。
            public string Name { get; }

            // 原生逐字语句常量，含各表自己的 order by（或原版就没有 order by）。
            public string Sql { get; }
        }
    }
}
