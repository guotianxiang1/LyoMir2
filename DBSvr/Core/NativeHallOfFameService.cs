using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using MySql.Data.MySqlClient;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public interface INativeHallOfFameService
    {
        byte[] Load(ushort rank);
    }

    public static class NativeHallOfFameProtocol
    {
        public const ushort RequestCommand = 0x0172;
        public const ushort ResponseCommand = 0x012E;
        public const int HeaderSize = 0x48;
        public const int RecordSize = 0xEF00;
        public const int RecordBodySize = RecordSize - 8;
        public const int ResponsePayloadSize = 0xF0F0;

        public static bool TryDecode(LegacyDbServerFrame frame,
            out ushort rank, out string error)
        {
            rank = 0;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x0172 envelope is invalid";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload)
                != RequestCommand)
            {
                error = "native 0x0172 command mismatch";
                return false;
            }
            rank = BinaryPrimitives.ReadUInt16LittleEndian(
                frame.Payload.AsSpan(2, 2));
            return true;
        }

        public static LegacyDbServerFrame CreateResponse(ushort rank,
            byte[] record)
        {
            if (record == null || record.Length != RecordSize)
                throw new ArgumentException(
                    "native hall-of-fame record must be 0xEF00 bytes",
                    nameof(record));
            var payload = new byte[ResponsePayloadSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), rank);
            record.CopyTo(payload, HeaderSize);
            return new LegacyDbServerFrame(1, 0, payload);
        }
    }

    public static class NativeHallOfFameBlobCodec
    {
        public static bool TryDecode(byte[] blob, out byte[] record,
            out string error)
        {
            record = null;
            error = string.Empty;
            if (blob == null || blob.Length <= 8)
            {
                error = "native hall-of-fame Blob is shorter than its header";
                return false;
            }
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(blob);
            var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(
                blob.AsSpan(6, 2));
            if (compressedLength == 0 || 8 + compressedLength > blob.Length)
            {
                error = "native hall-of-fame compressed length is invalid";
                return false;
            }
            var compressed = blob.AsSpan(8, compressedLength);
            if (crc != 0 && NativeAccountStorageBlobCodec.ComputeNativeCrc(
                    compressed) != crc)
            {
                error = "native hall-of-fame Blob CRC mismatch";
                return false;
            }
            try
            {
                using var input = new MemoryStream(compressed.ToArray(), false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(
                    NativeHallOfFameProtocol.RecordBodySize);
                zlib.CopyTo(output);
                var body = output.ToArray();
                if (body.Length != NativeHallOfFameProtocol.RecordBodySize)
                {
                    error = "native hall-of-fame decompressed length is invalid";
                    return false;
                }
                record = new byte[NativeHallOfFameProtocol.RecordSize];
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2),
                    NativeHallOfFameProtocol.RecordSize);
                body.CopyTo(record, 8);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is InvalidDataException)
            {
                error = "native hall-of-fame decompression failed: " + ex.Message;
                return false;
            }
        }
    }

    public sealed class MySqlNativeHallOfFameService : INativeHallOfFameService
    {
        public byte[] Load(ushort rank)
        {
            try
            {
                using var connection = new MySqlConnection(DBShare.DBConnection);
                connection.Open();
                using (var session = new MySqlCommand(
                           "SET WAIT_TIMEOUT = 2073600;", connection))
                    session.ExecuteNonQuery();
                using var command = new MySqlCommand(
                    @"SELECT CharData FROM gamedata.halloffame
                      WHERE Rank=@rank LIMIT 1",
                    connection);
                command.Parameters.AddWithValue("@rank", rank);
                var blob = command.ExecuteScalar() as byte[];
                return NativeHallOfFameBlobCodec.TryDecode(blob,
                    out var record, out _) ? record : null;
            }
            catch { return null; }
        }
    }
}
