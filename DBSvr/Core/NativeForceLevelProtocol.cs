using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeForceLevelProtocol
    {
        public const ushort RequestCommand = 0x0168;
        public const ushort ResponseCommand = 0x0131;
        public const int PayloadSize = 0x48;
        public const int MissingResult = 100001;
        public const int LoadFailedResult = 100002;
        public const int DeletedResult = 100003;

        private const int AccountOffset = 0x10;
        private const int AccountCapacity = 20;
        private const int CharacterOffset = 0x25;
        private const int CharacterCapacity = 15;

        public static string NormalizeCharacterNameKey(byte[] value)
        {
            value ??= Array.Empty<byte>();
            var normalized = (byte[])value.Clone();
            for (var i = 0; i < normalized.Length; i++)
                if (normalized[i] is >= (byte)'a' and <= (byte)'z')
                    normalized[i] -= (byte)('a' - 'A');
            return Convert.ToHexString(normalized);
        }

        public static bool TryDecodeRequest(LegacyDbServerFrame frame,
            out NativeForceLevelRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1)
            {
                error = "native 0x0168 envelope is invalid";
                return false;
            }
            if (frame.Payload.Length < PayloadSize)
            {
                error = "native 0x0168 payload is shorter than 0x48 bytes";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != RequestCommand)
            {
                error = "native 0x0168 command mismatch";
                return false;
            }
            if (!TryReadRawShortString(payload, AccountOffset,
                    out var account, out error)
                || !TryReadRawShortString(payload, CharacterOffset,
                    out var character, out error)) return false;

            request = new NativeForceLevelRequest
            {
                Value = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                AccountBytes = account,
                CharacterNameBytes = character
            };
            return true;
        }

        public static LegacyDbServerFrame CreateResponse(
            NativeForceLevelRequest request, int result)
        {
            request ??= new NativeForceLevelRequest();
            var payload = new byte[PayloadSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), result);
            WriteRawShortString(payload, AccountOffset, AccountCapacity,
                request.AccountBytes);
            WriteRawShortString(payload, CharacterOffset, CharacterCapacity,
                request.CharacterNameBytes);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool TryReadRawShortString(ReadOnlySpan<byte> payload,
            int offset, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            if (offset >= payload.Length)
            {
                error = "native 0x0168 ShortString length is missing";
                return false;
            }
            var length = payload[offset];
            if (offset + 1 + length > payload.Length)
            {
                error = "native 0x0168 ShortString exceeds the payload";
                return false;
            }
            value = payload.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteRawShortString(byte[] payload, int offset,
            int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            payload[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(payload.AsSpan(offset + 1));
        }
    }

    public sealed class NativeForceLevelRequest
    {
        public int Value { get; init; }
        public byte[] AccountBytes { get; init; } = Array.Empty<byte>();
        public byte[] CharacterNameBytes { get; init; } = Array.Empty<byte>();
    }

    public enum NativeForceLevelStoreResult
    {
        Missing,
        Deleted,
        LoadFailed,
        UpdatedWithoutSaveTarget,
        Queued
    }

    public enum NativeForceLevelTarget
    {
        Player,
        Hero
    }

    public sealed class NativeForceLevelMutation
    {
        public NativeForceLevelTarget Target { get; init; }
        public int Index { get; init; }
        public ushort ForceLevel { get; init; }
        public byte[] CharacterNameBytes { get; init; } = Array.Empty<byte>();
    }

    public readonly struct NativeForceLevelStoreAttempt
    {
        public NativeForceLevelStoreAttempt(NativeForceLevelStoreResult result,
            NativeForceLevelMutation mutation = null)
        {
            Result = result;
            Mutation = mutation;
        }

        public NativeForceLevelStoreResult Result { get; }
        public NativeForceLevelMutation Mutation { get; }
    }

    public sealed class NativeForceLevelApplyResult
    {
        public int Result { get; init; }
        public IReadOnlyList<NativeForceLevelMutation> Mutations { get; init; } =
            Array.Empty<NativeForceLevelMutation>();
    }

    public interface INativeForceLevelStore
    {
        NativeForceLevelStoreAttempt ApplyPlayer(byte[] characterName,
            ushort forceLevel);
        NativeForceLevelStoreAttempt ApplyHero(byte[] characterName,
            ushort forceLevel);
    }

    public sealed class NativeForceLevelService
    {
        private readonly INativeForceLevelStore _store;

        public NativeForceLevelService(INativeForceLevelStore store) =>
            _store = store ?? throw new ArgumentNullException(nameof(store));

        public int Apply(NativeForceLevelRequest request) =>
            ApplyDetailed(request).Result;

        public NativeForceLevelApplyResult ApplyDetailed(
            NativeForceLevelRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var forceLevel = unchecked((ushort)request.Value);
            var mutations = new List<NativeForceLevelMutation>(2);
            var player = _store.ApplyPlayer(request.CharacterNameBytes, forceLevel);
            if (player.Mutation != null) mutations.Add(player.Mutation);
            var result = Map(player.Result, request.Value);
            if (result == NativeForceLevelProtocol.MissingResult)
            {
                var hero = _store.ApplyHero(request.CharacterNameBytes, forceLevel);
                if (hero.Mutation != null) mutations.Add(hero.Mutation);
                result = Map(hero.Result, request.Value);
            }
            return new NativeForceLevelApplyResult
            {
                Result = result,
                Mutations = mutations
            };
        }

        private static int Map(NativeForceLevelStoreResult result,
            int requestedValue) => result switch
        {
            NativeForceLevelStoreResult.Missing =>
                NativeForceLevelProtocol.MissingResult,
            NativeForceLevelStoreResult.Deleted =>
                NativeForceLevelProtocol.DeletedResult,
            NativeForceLevelStoreResult.LoadFailed =>
                NativeForceLevelProtocol.LoadFailedResult,
            NativeForceLevelStoreResult.UpdatedWithoutSaveTarget =>
                NativeForceLevelProtocol.MissingResult,
            NativeForceLevelStoreResult.Queued => requestedValue,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
