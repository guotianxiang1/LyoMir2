using System;
using System.Buffers.Binary;
using SystemModule;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeDbToolReadRequest
    {
        public ushort Command { get; init; }
        public byte[] NameBytes { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeDbToolWriteRequest
    {
        public ushort Command { get; init; }
        public byte[] NameBytes { get; init; } = Array.Empty<byte>();
        public byte Option { get; init; }
        public byte[] Body { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeDbToolDeleteRequest
    {
        public int Operation { get; init; }
        public byte[] AccountBytes { get; init; } = Array.Empty<byte>();
        public byte[] NameBytes { get; init; } = Array.Empty<byte>();
        public byte[] HeroNameBytes { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeDbToolHeroWriteData
    {
        public NativeHeroRecord Record { get; init; }
        public byte[] RecordBytes { get; init; } = Array.Empty<byte>();
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public byte[] DynamicData { get; init; } = Array.Empty<byte>();
        public byte[] MasterNameBytes { get; init; } = Array.Empty<byte>();
        public byte[] HeroNameBytes { get; init; } = Array.Empty<byte>();
    }

    public static class NativeDbToolProtocol
    {
        public const ushort DeleteCommand = 0x0100;
        public const ushort HumanWriteCommand = 0x0101;
        public const ushort HumanReadCommand = 0x0102;
        public const ushort HeroWriteCommand = 0x0103;
        public const ushort HeroReadCommand = 0x0104;
        public const ushort ResponseCommand = 0x0064;
        public const int HeaderSize = 0x48;

        private const int OwnerOffset = 0x10;
        private const int OwnerCapacity = 20;
        private const int NameOffset = 0x25;
        private const int NameCapacity = 15;
        private const int HeroNameOffset = 0x35;

        public static bool TryDecodeHumanRead(LegacyDbServerFrame frame,
            out NativeDbToolReadRequest request, out string error) =>
            TryDecodeRead(frame, HumanReadCommand, out request, out error);

        public static bool TryDecodeHeroRead(LegacyDbServerFrame frame,
            out NativeDbToolReadRequest request, out string error) =>
            TryDecodeRead(frame, HeroReadCommand, out request, out error);

        public static bool TryDecodeHumanWrite(LegacyDbServerFrame frame,
            out NativeDbToolWriteRequest request, out string error) =>
            TryDecodeWrite(frame, HumanWriteCommand, out request, out error);

        public static bool TryDecodeHeroWrite(LegacyDbServerFrame frame,
            out NativeDbToolWriteRequest request, out string error) =>
            TryDecodeWrite(frame, HeroWriteCommand, out request, out error);

        public static bool TryDecodeDelete(LegacyDbServerFrame frame,
            out NativeDbToolDeleteRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native DB-tool delete envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != DeleteCommand)
            {
                error = "native DB-tool delete command mismatch";
                return false;
            }
            if (!TryReadRawShortString(payload, OwnerOffset, OwnerCapacity,
                    out var account, out error)
                || !TryReadRawShortString(payload, NameOffset, NameCapacity,
                    out var name, out error)
                || !TryReadRawShortString(payload, HeroNameOffset,
                    NameCapacity, out var heroName, out error))
                return false;
            request = new NativeDbToolDeleteRequest
            {
                Operation = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                AccountBytes = account,
                NameBytes = name,
                HeroNameBytes = heroName
            };
            return true;
        }

        public static bool TryCreateReadSuccess(
            NativeDbToolReadRequest request, byte[] ownerBytes,
            byte[] primaryData, byte[] trailingData,
            out LegacyDbServerFrame response, out string error)
        {
            response = null;
            error = string.Empty;
            primaryData ??= Array.Empty<byte>();
            trailingData ??= Array.Empty<byte>();
            if (!IsReadRequest(request, out error)) return false;
            if (primaryData.Length == 0)
            {
                error = "native DB-tool read primary data is empty";
                return false;
            }

            var maximumBodyLength = NativeDbServerProtocol.MaximumFrameLength
                                    - LegacyDbServerFrameCodec.HeaderSize
                                    - HeaderSize;
            if (primaryData.Length > maximumBodyLength
                || trailingData.Length > maximumBodyLength - primaryData.Length)
            {
                error = "native DB-tool read response exceeds the frame limit";
                return false;
            }

            var payload = CreateHeader(request.Command, request.NameBytes, 1);
            Array.Resize(ref payload, HeaderSize + primaryData.Length
                + trailingData.Length);
            primaryData.CopyTo(payload, HeaderSize);
            trailingData.CopyTo(payload, HeaderSize + primaryData.Length);
            WriteRawShortString(payload, OwnerOffset, OwnerCapacity, ownerBytes);
            response = new LegacyDbServerFrame(1, 0, payload);
            return true;
        }

        public static LegacyDbServerFrame CreateReadFailure(
            NativeDbToolReadRequest request)
        {
            request ??= new NativeDbToolReadRequest();
            return new LegacyDbServerFrame(1, 0,
                CreateHeader(request.Command, request.NameBytes, 0));
        }

        public static LegacyDbServerFrame CreateWriteResponse(
            NativeDbToolWriteRequest request, int result)
        {
            request ??= new NativeDbToolWriteRequest();
            return new LegacyDbServerFrame(1, 0,
                CreateHeader(request.Command, request.NameBytes,
                    unchecked((ushort)result)));
        }

        public static LegacyDbServerFrame CreateDeleteResponse(
            NativeDbToolDeleteRequest request, int result)
        {
            request ??= new NativeDbToolDeleteRequest();
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                unchecked((ushort)result));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                request.Operation);
            WriteRawShortString(payload, OwnerOffset, OwnerCapacity,
                request.AccountBytes);
            WriteRawShortString(payload, NameOffset, NameCapacity,
                request.NameBytes);
            WriteRawShortString(payload, HeroNameOffset, NameCapacity,
                request.HeroNameBytes);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static bool TryCreateHumanWritePersistence(
            NativeDbToolWriteRequest request,
            out NativeSavePersistenceData persistence,
            out THumDataInfo decoded,
            out byte[] characterNameBytes,
            out string error)
        {
            persistence = null;
            decoded = null;
            characterNameBytes = null;
            error = string.Empty;
            if (request == null || request.Command != HumanWriteCommand
                || request.Body == null
                || request.Body.Length < NativeHumanDataCodec.DataSizeMarker)
            {
                error = "native DB-tool human write body is truncated";
                return false;
            }

            var dataBlob = request.Body.AsSpan(0,
                NativeHumanDataCodec.DataSizeMarker).ToArray();
            var scriptDataBlob = request.Body.AsSpan(
                NativeHumanDataCodec.DataSizeMarker).ToArray();
            if (!NativeHumanDataCodec.TryDecode(dataBlob, scriptDataBlob,
                    out decoded, out error)
                || decoded?.Data == null
                || decoded.NativeData?.Length
                    != NativeHumanDataCodec.DataRecordSize)
                return false;
            if (!TryReadRawShortString(decoded.NativeData, 0, 15,
                    out characterNameBytes, out error)
                || !TryReadRawShortString(decoded.NativeData, 0x20, 20,
                    out var accountBytes, out error)
                || characterNameBytes.Length == 0
                || accountBytes.Length == 0)
            {
                if (string.IsNullOrEmpty(error))
                    error = "native DB-tool human identity is empty";
                return false;
            }

            var raw = decoded.NativeData;
            persistence = new NativeSavePersistenceData
            {
                Account = decoded.Data.sAccount,
                CharacterName = decoded.Data.sCharName,
                DataBlob = dataBlob,
                ScriptDataBlob = scriptDataBlob,
                Level = BinaryPrimitives.ReadUInt16LittleEndian(
                    raw.AsSpan(0x3C, 2)),
                Experience = BinaryPrimitives.ReadUInt32LittleEndian(
                    raw.AsSpan(0x50, 4)),
                Job = raw[0x40],
                Sex = raw[0x3F],
                ApprenticeNum = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(0x174, 4)),
                HeroCardLevel = raw[0x16F],
                PlatinaCharacterLevel = raw[0x16E],
                SfLevel = BinaryPrimitives.ReadUInt16LittleEndian(
                    raw.AsSpan(0x53E, 2))
            };
            return true;
        }

        public static bool TryCreateHeroWriteData(
            NativeDbToolWriteRequest request,
            out NativeDbToolHeroWriteData writeData,
            out string error)
        {
            writeData = null;
            error = string.Empty;
            if (request == null || request.Command != HeroWriteCommand
                || request.Body == null
                || request.Body.Length < NativeHeroDbFrameCodec.HeroRecordSize)
            {
                error = "native DB-tool hero write body is truncated";
                return false;
            }

            var dataLength = request.Body.Length
                             >= NativeHeroBlobCodec.ThreeHeroRecordSize
                ? NativeHeroBlobCodec.ThreeHeroRecordSize
                : NativeHeroDbFrameCodec.HeroRecordSize;
            var data = request.Body.AsSpan(0, dataLength).ToArray();
            var dynamicData = request.Body.AsSpan(dataLength).ToArray();
            if (dynamicData.Length > NativeHeroBlobCodec.MaximumMySqlBlobSize)
            {
                error = "native DB-tool hero dynamic data exceeds BLOB capacity";
                return false;
            }
            NativeHeroRecord firstRecord = null;
            byte[] firstRecordBytes = null;
            for (var offset = 0; offset < data.Length;
                 offset += NativeHeroDbFrameCodec.HeroRecordSize)
            {
                var current = data.AsSpan(offset,
                    NativeHeroDbFrameCodec.HeroRecordSize).ToArray();
                if (!NativeHeroDbFrameCodec.TryCreateRecord(current,
                        out var record, out error))
                {
                    error = $"invalid native DB-tool hero record at "
                            + $"0x{offset:X}: {error}";
                    return false;
                }
                if (offset != 0) continue;
                firstRecord = record;
                firstRecordBytes = current;
            }
            if (!NativeHeroDbFrameCodec.TryDecodeDynamicData(dynamicData,
                    out _, out error))
                return false;
            if (!TryReadRawShortString(firstRecordBytes,
                    NativeHeroDbFrameCodec.MasterNameOffset, 15,
                    out var masterNameBytes, out error)
                || !TryReadRawShortString(firstRecordBytes,
                    NativeHeroDbFrameCodec.HeroNameOffset, 15,
                    out var heroNameBytes, out error)
                || masterNameBytes.Length == 0 || heroNameBytes.Length == 0)
            {
                if (string.IsNullOrEmpty(error))
                    error = "native DB-tool hero identity is empty";
                return false;
            }

            writeData = new NativeDbToolHeroWriteData
            {
                Record = firstRecord,
                RecordBytes = firstRecordBytes,
                Data = data,
                DynamicData = dynamicData,
                MasterNameBytes = masterNameBytes,
                HeroNameBytes = heroNameBytes
            };
            return true;
        }

        private static bool TryDecodeRead(LegacyDbServerFrame frame,
            ushort command, out NativeDbToolReadRequest request,
            out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native DB-tool read envelope is invalid";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload)
                != command)
            {
                error = "native DB-tool read command mismatch";
                return false;
            }
            if (!TryReadRawShortString(frame.Payload, NameOffset,
                    NameCapacity, out var name, out error))
                return false;
            request = new NativeDbToolReadRequest
            {
                Command = command,
                NameBytes = name
            };
            return true;
        }

        private static bool TryDecodeWrite(LegacyDbServerFrame frame,
            ushort command, out NativeDbToolWriteRequest request,
            out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native DB-tool write envelope is invalid";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload)
                != command)
            {
                error = "native DB-tool write command mismatch";
                return false;
            }
            if (!TryReadRawShortString(frame.Payload, NameOffset,
                    NameCapacity, out var name, out error))
                return false;
            request = new NativeDbToolWriteRequest
            {
                Command = command,
                NameBytes = name,
                Option = frame.Payload[4],
                Body = frame.Payload.AsSpan(HeaderSize).ToArray()
            };
            return true;
        }

        private static bool IsReadRequest(NativeDbToolReadRequest request,
            out string error)
        {
            error = string.Empty;
            if (request == null
                || request.Command != HumanReadCommand
                && request.Command != HeroReadCommand)
            {
                error = "native DB-tool read request is invalid";
                return false;
            }
            if (request.NameBytes == null
                || request.NameBytes.Length > NameCapacity)
            {
                error = "native DB-tool read name exceeds SS15";
                return false;
            }
            return true;
        }

        private static byte[] CreateHeader(ushort command, byte[] nameBytes,
            ushort result)
        {
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                result);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                command);
            WriteRawShortString(payload, NameOffset, NameCapacity,
                nameBytes);
            return payload;
        }

        private static bool TryReadRawShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            if (offset < 0 || offset >= source.Length)
            {
                error = "native DB-tool ShortString is truncated";
                return false;
            }
            var length = source[offset];
            if (length > capacity || offset + 1 + length > source.Length)
            {
                error = "native DB-tool ShortString is invalid";
                return false;
            }
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteRawShortString(Span<byte> destination,
            int offset, int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
        }
    }
}
