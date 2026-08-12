using MySql.Data.MySqlClient;
using System.Globalization;
using SystemModule;

namespace GameSvr.Services
{
    public class MailService
    {
        private const string NativeSystemSender = "\u7cfb\u7edf";

        private sealed class MailItemSpec
        {
            internal string Name { get; init; } = string.Empty;
            internal int Count { get; init; }
            internal int Mode { get; init; }
        }

        public bool NewFullMailEx(TPlayObject recipient, string title, string context,
            int mailType, int moneyCount, int moneyType, string itemInfo, string createDate)
        {
            if (recipient == null) return false;
            return CreateNativeMail(recipient.m_sCharName, title, context, mailType,
                moneyCount, moneyType, itemInfo, createDate, recipient);
        }

        public bool NewFullMailEx(string recvName, string title, string context,
            int mailType, int moneyCount, int moneyType, string itemInfo, string createDate)
        {
            return CreateNativeMail(recvName, title, context, mailType, moneyCount,
                moneyType, itemInfo, createDate, null);
        }

        private static bool CreateNativeMail(string recvName, string title, string context,
            int mailType, int moneyCount, int moneyType, string itemInfo, string createDate,
            TPlayObject onlineRecipient)
        {
            if (mailType is < byte.MinValue or > byte.MaxValue)
                return false;
            if (!TryParseItemInfo(itemInfo, out var itemSpecs)) return false;

            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return false;

            var mailCreated = false;
            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();

                var recipientId = NativeMailStore.ResolveRecipientId(connection, recvName);
                if (recipientId == 0) return false;

                var createdAt = ParseCreateDate(createDate);
                var mailId = InsertMail(connection, recvName, recipientId, title, context,
                    (byte)mailType, createdAt);
                if (mailId <= 0) return false;
                mailCreated = true;

                if (moneyCount > 0) SetAttachStatusBestEffort(connection, mailId, 1);

                var attachmentCount = 0;
                var attachments = new List<TUserItem>();
                foreach (var spec in itemSpecs)
                {
                    if (attachmentCount >= 6) break;
                    if (!TryCreateAttachment(spec, out var item)) continue;
                    if (!NativeMailAttachmentCodec.TryEncode(item, out var attachmentRecord, out _))
                        continue;
                    if (InsertAttachmentBestEffort(connection, mailId, attachmentRecord, createdAt) <= 0)
                        continue;

                    attachmentCount++;
                    attachments.Add(item);
                    SetAttachStatusBestEffort(connection, mailId, 1);
                }

                UpdateSummaryBestEffort(connection, mailId, moneyType, moneyCount,
                    attachmentCount);

                var record = new NativeMailRecord
                {
                    Id = mailId,
                    SenderId = -1,
                    Sender = NativeSystemSender,
                    Title = title ?? string.Empty,
                    Context = context ?? string.Empty,
                    MailType = (byte)mailType,
                    MailStatus = 1,
                    AttachStatus = moneyCount > 0 || attachmentCount > 0 ? (byte)1 : (byte)3,
                    MoneyType = moneyType,
                    MoneyCount = moneyCount,
                    AttachCount = attachmentCount,
                    CreateDate = createdAt
                };
                NativeMailCacheService.Register(
                    recipientId, recvName, record, attachments, DateTime.UtcNow);

                NotifyNewMailBestEffort(onlineRecipient, recipientId, mailType);
                return true;
            }
            catch (Exception ex)
            {
                // The Delphi M2 does not wrap this sequence in a transaction. Any rows already
                // written intentionally remain in their native tables when a later step fails.
                M2Share.MainOutMessage("[NativeMail] NewFullMailEx failed: " + ex.Message);
                return mailCreated;
            }
        }

        private static bool TryParseItemInfo(string itemInfo, out List<MailItemSpec> specs)
        {
            specs = new List<MailItemSpec>();
            if (string.IsNullOrEmpty(itemInfo)) return true;

            var groups = itemInfo.Split('/');
            if (groups.Length > 6) return false;
            foreach (var group in groups)
            {
                var parts = group.Split('|');
                var name = parts.Length > 0
                    ? TruncateItemName(parts[0])
                    : string.Empty;
                var count = parts.Length > 1 && int.TryParse(parts[1], out var parsedCount)
                    ? parsedCount
                    : 1;
                var mode = parts.Length > 2 && int.TryParse(parts[2], out var parsedMode)
                    ? parsedMode
                    : 0;
                specs.Add(new MailItemSpec { Name = name, Count = count, Mode = mode });
            }
            return true;
        }

        private static string TruncateItemName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var bytes = HUtil32.GbkEncoding.GetBytes(value);
            return bytes.Length <= 15
                ? value
                : HUtil32.GbkEncoding.GetString(bytes, 0, 15);
        }

        private static bool TryCreateAttachment(MailItemSpec spec, out TUserItem item)
        {
            item = null;
            if (string.IsNullOrEmpty(spec.Name)) return false;
            if (!M2Share.UserEngine.CopyToUserItemFromName(spec.Name, ref item)) return false;

            var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem == null)
            {
                item = null;
                return false;
            }

            if (stdItem.StdMode == 7)
                item.Dura = spec.Count > item.DuraMax
                    ? item.DuraMax
                    : unchecked((ushort)spec.Count);

            var mode = unchecked((byte)spec.Mode);
            if (mode is 1 or 3)
            {
                item.btValue[10] = 1;
                item.btValue[11] = 0;
            }
            if (mode is 2 or 3) item.Bind = 1;
            return true;
        }

        private static int InsertMail(MySqlConnection connection, string recvName,
            long recipientId, string title, string context, byte mailType, DateTime createdAt)
        {
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText =
                    "INSERT INTO gamedata.mailitem(" +
                    "sendId,sendName,recvName,recvId,title,context,mailType,mailstatus," +
                    "attachstatus,moneyType,moneyCount,attachNum,createDate) VALUES(" +
                    "-1,@sendName,@recvName,@recvId,@title,@context,@mailType,1,3,0,0,0,@createDate)";
                AddGbkParameter(insert, "@sendName", NativeSystemSender);
                AddGbkParameter(insert, "@recvName", recvName);
                insert.Parameters.Add("@recvId", MySqlDbType.Int64).Value = recipientId;
                AddGbkParameter(insert, "@title", title);
                AddGbkParameter(insert, "@context", context);
                insert.Parameters.Add("@mailType", MySqlDbType.Byte).Value = mailType;
                insert.Parameters.Add("@createDate", MySqlDbType.DateTime).Value = createdAt;
                if (insert.ExecuteNonQuery() != 1) return -1;
            }

            using var queryId = connection.CreateCommand();
            queryId.CommandText = "SELECT LAST_INSERT_ID()";
            var value = queryId.ExecuteScalar();
            return value == null || value == DBNull.Value ? -1 : Convert.ToInt32(value);
        }

        private static int InsertAttachmentBestEffort(MySqlConnection connection, int mailId,
            byte[] record, DateTime createdAt)
        {
            try
            {
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText =
                        "INSERT INTO gamedata.attachitem(mailId,createDate) " +
                        "VALUES(@mailId,@createDate)";
                    insert.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                    insert.Parameters.Add("@createDate", MySqlDbType.DateTime).Value = createdAt;
                    if (insert.ExecuteNonQuery() != 1) return -1;
                }

                int attachmentId;
                using (var queryId = connection.CreateCommand())
                {
                    queryId.CommandText = "SELECT LAST_INSERT_ID()";
                    var value = queryId.ExecuteScalar();
                    if (value == null || value == DBNull.Value) return -1;
                    attachmentId = Convert.ToInt32(value);
                }

                try
                {
                    using var writeBlob = connection.CreateCommand();
                    writeBlob.CommandText =
                        "UPDATE gamedata.attachitem SET data=@data WHERE idx=@attachmentId";
                    writeBlob.Parameters.Add("@data", MySqlDbType.Blob,
                        NativeMailAttachmentCodec.RecordSize).Value = record;
                    writeBlob.Parameters.Add("@attachmentId", MySqlDbType.Int32).Value = attachmentId;
                    writeBlob.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    M2Share.MainOutMessage("[NativeMail] SaveAttach blob failed: " + ex.Message);
                }
                return attachmentId;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[NativeMail] SaveAttach failed: " + ex.Message);
                return -1;
            }
        }

        private static void SetAttachStatusBestEffort(MySqlConnection connection, int mailId,
            byte status)
        {
            try
            {
                using var update = connection.CreateCommand();
                update.CommandText =
                    "UPDATE gamedata.mailitem SET attachstatus=@status,modifydate=Now() " +
                    "WHERE idx=@mailId";
                update.Parameters.Add("@status", MySqlDbType.Byte).Value = status;
                update.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                update.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[NativeMail] SetAttachStatus failed: " + ex.Message);
            }
        }

        private static void UpdateSummaryBestEffort(MySqlConnection connection, int mailId,
            int moneyType, int moneyCount, int attachmentCount)
        {
            try
            {
                using var update = connection.CreateCommand();
                update.CommandText =
                    "UPDATE gamedata.mailitem SET moneyType=@moneyType,moneyCount=@moneyCount," +
                    "attachNum=@attachNum,modifyDate=Now() WHERE idx=@mailId";
                update.Parameters.Add("@moneyType", MySqlDbType.Int32).Value = moneyType;
                update.Parameters.Add("@moneyCount", MySqlDbType.Int32).Value = moneyCount;
                update.Parameters.Add("@attachNum", MySqlDbType.Int32).Value = attachmentCount;
                update.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                update.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[NativeMail] UpdateSummary failed: " + ex.Message);
            }
        }

        private static void NotifyNewMailBestEffort(TPlayObject recipient, long recipientId,
            int mailType)
        {
            if (recipient == null) return;
            if (!NativeMailCacheService.TryGetUnreadCounts(recipientId, out var counts))
                return;

            var body = new byte[sizeof(int) * counts.Length];
            Buffer.BlockCopy(counts, 0, body, 0, body.Length);
            var header = Grobal2.MakeDefaultMsg(
                Grobal2.SM_MAIL_INFO, counts.Sum(), 0, mailType, counts.Length);
            recipient.SendSocket(header, body);
        }

        private static DateTime ParseCreateDate(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                string[] formats =
                {
                    "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss",
                    "yyyy/M/d H:m:s", "yyyy-M-d H:m:s"
                };
                if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var exact))
                    return exact;
                if (DateTime.TryParse(value, CultureInfo.CurrentCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var parsed))
                    return parsed;
            }
            return DateTime.Now;
        }

        private static void AddGbkParameter(MySqlCommand command, string name, string value)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            command.Parameters.Add(name, MySqlDbType.VarBinary, bytes.Length).Value = bytes;
        }
    }
}
