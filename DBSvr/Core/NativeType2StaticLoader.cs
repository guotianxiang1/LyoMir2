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
        private static readonly StaticTable[] Tables =
        {
            new(NativeType2StaticRecordBuilder.HumanMagicCommand,
                "humanmagic", "MagicIdx"),
            new(NativeType2StaticRecordBuilder.HeroMagicCommand,
                "heromagic", "MagicIdx"),
            new(NativeType2StaticRecordBuilder.MonsterCommand,
                "monster", null),
            new(NativeType2StaticRecordBuilder.StdItemsCommand,
                "stditems", "idx"),
            new(NativeType2StaticRecordBuilder.AntiqueItemsCommand,
                "AntiqueItems", null),
            new(NativeType2StaticRecordBuilder.FieldHeroCommand,
                "fieldhero", null),
            new(NativeType2StaticRecordBuilder.SuperForceCommand,
                "SuperForce", "level"),
            new(NativeType2StaticRecordBuilder.SuperSkillCommand,
                "SuperSkill", null),
            new(NativeType2StaticRecordBuilder.ForceMagicCommand,
                "forcemagic", "ForceId")
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
            var sql = "SELECT HIGH_PRIORITY * FROM mir3." + table.Name;
            if (table.OrderColumn != null)
                sql += " ORDER BY " + table.OrderColumn;

            using var command = new MySqlCommand(sql, connection);
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
            public StaticTable(ushort command, string name, string orderColumn)
            {
                Command = command;
                Name = name;
                OrderColumn = orderColumn;
            }

            public ushort Command { get; }
            public string Name { get; }
            public string OrderColumn { get; }
        }
    }
}
