using System;
using System.Buffers.Binary;
using MySql.Data.MySqlClient;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeAwardPlayerRequest
    {
        public int Correlation { get; init; }
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
        public byte[] AwardPtid { get; init; } = Array.Empty<byte>();
        public byte Level { get; init; }
        public byte Job { get; init; }
        public byte Sex { get; init; }
    }

    public static class NativeAwardPlayerProtocol
    {
        public const ushort RequestCommand = 0x015B;
        public const ushort ResponseCommand = 0x0061;
        public const int HeaderSize = 0x48;
        public const int BodySize = 24;

        public static bool TryDecode(LegacyDbServerFrame frame,
            out NativeAwardPlayerRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x015B envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != RequestCommand)
            {
                error = "native 0x015B command mismatch";
                return false;
            }
            var body = payload.Slice(HeaderSize);
            if (body.Length != BodySize)
            {
                error = "native 0x015B body must be 24 bytes";
                return false;
            }
            if (!TryReadShortString(payload, 0x10, 20,
                    out var account, out error)
                || !TryReadShortString(payload, 0x25, 15,
                    out var characterName, out error)
                || !TryReadShortString(body, 0, 20,
                    out var awardPtid, out error))
                return false;
            request = new NativeAwardPlayerRequest
            {
                Correlation = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                Account = account,
                CharacterName = characterName,
                AwardPtid = awardPtid,
                Level = body[21],
                Job = body[22],
                Sex = body[23]
            };
            return true;
        }

        public static LegacyDbServerFrame CreateResponse(
            NativeAwardPlayerRequest request, bool success)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                success ? (ushort)1 : (ushort)0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                request.Correlation);
            WriteShortString(payload, 0x10, 20, request.Account);
            WriteShortString(payload, 0x25, 15, request.CharacterName);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native 0x015B ShortString exceeds {capacity} bytes";
                return false;
            }
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteShortString(Span<byte> destination,
            int offset, int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
        }
    }

    public interface INativeAwardPlayerService
    {
        bool Insert(NativeAwardPlayerRequest request);
    }

    public sealed class MySqlNativeAwardPlayerService : INativeAwardPlayerService
    {
        public bool Insert(NativeAwardPlayerRequest request)
        {
            if (request == null) return false;
            try
            {
                using var connection = new MySqlConnection(DBShare.DBConnection);
                connection.Open();
                // native VA 0x5A5914 len=25: "set wait_timeout=2073600;"
                using (var session = new MySqlCommand(
                           "set wait_timeout=2073600;", connection))
                    session.ExecuteNonQuery();
                // native VA 0x5AB8C8 len=87: Insert Ignore into awardplayers(PTID,Level,job,Sex,Status)
                // column `job` is lowercase in the native literal; `PTID`, `Level`, `Sex`, `Status`
                // match the DDL at VA 0x5BBA08. Schema mir3 is implicit in native (use mir3; 0x5BAD84)
                // but the explicit prefix is semantically equivalent given database=mir3 in DBConnection.
                using var command = new MySqlCommand(
                    @"INSERT IGNORE INTO mir3.awardplayers
                        (PTID, Level, job, Sex, Status)
                      VALUES(@ptid, @level, @job, @sex, 0)",
                    connection);
                command.Parameters.Add("@ptid", MySqlDbType.Binary).Value =
                    request.AwardPtid;
                command.Parameters.AddWithValue("@level", request.Level);
                command.Parameters.AddWithValue("@job", request.Job);
                command.Parameters.AddWithValue("@sex", request.Sex);
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    "[NativeAwardPlayer] ExecuteScript异常: " + ex.Message);
                return false;
            }
        }
    }
}
