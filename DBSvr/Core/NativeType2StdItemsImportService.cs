using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MySql.Data.MySqlClient;
using SystemModule;

namespace DBSvr.Core
{
    public interface INativeType2StdItemsImportService
    {
        bool TryImport(int correlation, out List<byte[]> notifications);
    }

    public static class NativeType2StdItemsImportProtocol
    {
        public const ushort RequestCommand = 0x0180;
        public const ushort NotificationCommand = 0x00CA;

        public static List<byte[]> BuildRecords(
            IReadOnlyList<NativeType2StaticRow> rows, int correlation,
            out List<byte[]> cacheRecords)
        {
            rows ??= Array.Empty<NativeType2StaticRow>();
            cacheRecords = new List<byte[]>(rows.Count);
            var notifications = new List<byte[]>(rows.Count);
            for (var index = 0; index < rows.Count; index++)
            {
                var cached = NativeType2StaticRecordBuilder.BuildImportedStdItem(
                    rows[index] ?? throw new ArgumentException(
                        "stditems row cannot be null", nameof(rows)),
                    index == rows.Count - 1);
                BinaryPrimitives.WriteInt32LittleEndian(
                    cached.AsSpan(4, 4), correlation);
                cacheRecords.Add(cached);

                var notification = (byte[])cached.Clone();
                BinaryPrimitives.WriteUInt16LittleEndian(notification,
                    NotificationCommand);
                BinaryPrimitives.WriteInt32LittleEndian(
                    notification.AsSpan(8, 4), 0);
                notifications.Add(notification);
            }
            return notifications;
        }
    }

    public sealed class MySqlNativeType2StdItemsImportService :
        INativeType2StdItemsImportService
    {
        private readonly NativeType2InitializationCache _cache;

        public MySqlNativeType2StdItemsImportService(
            NativeType2InitializationCache cache) =>
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        public bool TryImport(int correlation,
            out List<byte[]> notifications)
        {
            notifications = new List<byte[]>();
            var path = Path.Combine(AppContext.BaseDirectory, "stditems.sql");
            if (!File.Exists(path)) return false;
            try
            {
                using var connection = new MySqlConnection(DBShare.DBConnection);
                connection.Open();
                using (var session = new MySqlCommand(
                           "SET WAIT_TIMEOUT = 2073600;", connection))
                    session.ExecuteNonQuery();
                int oldCount;
                using (var count = new MySqlCommand(
                           "SELECT COUNT(*) FROM mir3.stditems", connection))
                    oldCount = Convert.ToInt32(count.ExecuteScalar());
                if (oldCount <= 0) return false;

                var scriptText = File.ReadAllText(path, HUtil32.GbkEncoding);
                var script = new MySqlScript(connection, scriptText);
                script.Execute();
                File.Delete(path);

                int newCount;
                using (var count = new MySqlCommand(
                           "SELECT COUNT(*) FROM mir3.stditems", connection))
                    newCount = Convert.ToInt32(count.ExecuteScalar());
                if (newCount - oldCount <= 0) return true;

                var rows = new List<NativeType2StaticRow>();
                using (var query = new MySqlCommand(
                           @"SELECT * FROM mir3.stditems
                             WHERE idx>@oldCount ORDER BY idx",
                           connection))
                {
                    query.Parameters.AddWithValue("@oldCount", oldCount);
                    using var reader = query.ExecuteReader();
                    while (reader.Read())
                        rows.Add(MySqlNativeType2StaticLoader.ReadRow(reader));
                }
                notifications = NativeType2StdItemsImportProtocol.BuildRecords(
                    rows, correlation, out var cacheRecords);
                _cache.AppendStdItems(cacheRecords);
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    "[NativeStdItemsImport] 导入失败: " + ex.Message);
                notifications.Clear();
                return false;
            }
        }
    }
}
