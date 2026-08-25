using System;

namespace DBSvr.Core
{
    public sealed class NativeNewCharacterRequest
    {
        public int NameLength { get; init; }
        public byte[] NameBytes { get; init; } = Array.Empty<byte>();
        public byte Job { get; init; }
        public byte Sex { get; init; }
    }

    /// <summary>
    /// Native 2.08 CM_NEWCHR/SM_NEWCHR (4012) record and result contract.
    /// </summary>
    public static class NativeNewCharacterProtocol
    {
        public const ushort Command = 4012;
        public const int BodySize = 20;
        public const int NameCapacity = 15;

        public const ushort ResultPending = 0;
        public const ushort ResultSuccess = 1;
        public const ushort ResultDuplicateOrFailure = 2;
        public const ushort ResultCharacterLimit = 3;
        public const ushort ResultInvalidRequest = 4;
        public const ushort ResultJobUnavailable = 5;
        public const ushort ResultAccountLocked = 6;
        public const ushort ResultCreationDisabled = 7;

        public static bool TryDecode(ReadOnlySpan<byte> body,
            out NativeNewCharacterRequest request)
        {
            request = null;
            if (body.Length < BodySize) return false;

            var nameLength = body[0];
            var nameBytes = nameLength <= NameCapacity
                ? body.Slice(1, nameLength).ToArray()
                : Array.Empty<byte>();
            request = new NativeNewCharacterRequest
            {
                NameLength = nameLength,
                NameBytes = nameBytes,
                Job = body[17],
                Sex = body[18]
            };
            return true;
        }

        public static ushort ValidateFixedGates(
            NativeNewCharacterRequest request, bool creationDisabled,
            bool managerCreationDisabled, bool allowJobThree)
        {
            if (request == null) return ResultInvalidRequest;
            if (creationDisabled) return ResultCreationDisabled;
            if (managerCreationDisabled) return ResultInvalidRequest;

            if (request.Job > 3) return ResultInvalidRequest;
            if (request.Job == 3 && !allowJobThree)
                return ResultJobUnavailable;
            if (request.Sex >= 2) return ResultInvalidRequest;
            if (!NativeCharacterNameValidator.IsLengthAllowed(
                    request.NameLength)
                || request.NameBytes.Length != request.NameLength
                || !NativeCharacterNameValidator.IsNameAllowed(
                    request.NameBytes))
                return ResultInvalidRequest;

            return ResultPending;
        }
    }
}
