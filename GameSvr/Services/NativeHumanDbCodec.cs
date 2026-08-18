using System;
using System.Buffers.Binary;
using System.Text;
using DBSvr.Core;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public sealed class NativeHumanLoadData
    {
        public string Account { get; init; } = string.Empty;
        public string CharacterName { get; init; } = string.Empty;
        public THumDataInfo HumanRecord { get; init; }
        public byte[] HumanInfoPrefix { get; init; } = Array.Empty<byte>();
        public byte[] SessionSuffix { get; init; } = Array.Empty<byte>();
    }

    /// <summary>Original Type1 0x0050/0x0150 human-record wire layout.</summary>
    public static class NativeHumanDbCodec
    {
        public const ushort LoadCommand = 0x0050;
        public const ushort SaveCommand = 0x0150;
        public const int MessageSize = 0x48;
        public const int HumanInfoSize = 0xF0A8;
        public const int HumanInfoPrefixSize = 0x08;
        public const int SessionSuffixSize = 0x01A8;
        public const int AccountOffset = 0x10;
        public const int CharacterOffset = 0x25;
        public const int HumanInfoOffset = MessageSize;
        public const int NativeDataOffset = HumanInfoOffset + HumanInfoPrefixSize;
        public const int SessionSuffixOffset = NativeDataOffset
                                               + NativeHumanDataCodec.DataRecordSize;
        public const int ScriptDataOffset = HumanInfoOffset + HumanInfoSize;
        public const int SessionPrefixSize = 0x00A0;
        public const int SwitchExtensionSize = 0x0108;

        private const int AccountCapacity = 20;
        private const int CharacterCapacity = 15;
        private static readonly Encoding Gbk;

        static NativeHumanDbCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }

        public static bool TryDecodeLoadFrame(LegacyDbServerFrame frame,
            out NativeHumanLoadData load, out string error)
        {
            load = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native human load frame is null";
                return false;
            }
            if (frame.Type != 1)
            {
                error = "native human load frame is not Type1";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length < ScriptDataOffset)
            {
                error = $"native human load payload must be at least {ScriptDataOffset} bytes";
                return false;
            }
            var span = payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2)) != LoadCommand)
            {
                error = "native human load command mismatch";
                return false;
            }
            if (!TryReadShortString(span, AccountOffset, AccountCapacity,
                    allowEmpty: false, out var account, out error)
                || !TryReadShortString(span, CharacterOffset, CharacterCapacity,
                    allowEmpty: false, out var characterName, out error))
                return false;

            var prefix = span.Slice(HumanInfoOffset, HumanInfoPrefixSize).ToArray();
            var raw = span.Slice(NativeDataOffset,
                NativeHumanDataCodec.DataRecordSize).ToArray();
            var suffix = span.Slice(SessionSuffixOffset, SessionSuffixSize).ToArray();
            var scriptSlot = span.Slice(ScriptDataOffset);
            byte[] script;
            if (scriptSlot.Length == 0)
            {
                script = Array.Empty<byte>();
            }
            else
            {
                if (scriptSlot.Length < sizeof(int))
                {
                    error = "native human load ScriptData length is truncated";
                    return false;
                }
                var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(
                    scriptSlot.Slice(0, sizeof(int)));
                if (declaredLength < 0
                    || declaredLength > scriptSlot.Length - sizeof(int))
                {
                    error = "native human load ScriptData length exceeds its slot";
                    return false;
                }
                script = scriptSlot.Slice(0, sizeof(int) + declaredLength).ToArray();
            }

            // The original dispatcher accepts a fixed 0xF0A8 body with no ScriptData.
            // NativeHumanDataCodec expects a ScriptData length word, so use an empty
            // native section only for decoding and restore the actual empty wire value.
            var decodeScript = script.Length == 0 ? new byte[4] : script;
            if (!NativeHumanDataCodec.TryDecodeRaw(raw, decodeScript,
                    out var human, out error))
            {
                error = "native human load record: " + error;
                return false;
            }
            if (script.Length == 0)
            {
                human.NativeScriptData = Array.Empty<byte>();
                human.NativeScriptDataCrc = 0;
            }
            human.Header ??= new TRecordHeader();
            human.Header.sAccount = account;
            human.Header.sName = characterName;

            load = new NativeHumanLoadData
            {
                Account = account,
                CharacterName = characterName,
                HumanRecord = human,
                HumanInfoPrefix = prefix,
                SessionSuffix = suffix
            };
            return true;
        }

        public static bool TryEncodeSaveFrame(string account, string characterName,
            ushort saveMode, int param1, int param2, THumDataInfo human,
            out LegacyDbServerFrame frame, out string error)
        {
            return TryEncodeSaveFrame(account, characterName, saveMode,
                param1, param2, human, null, out frame, out error);
        }

        public static bool TryEncodeSaveFrame(string account, string characterName,
            ushort saveMode, int param1, int param2, THumDataInfo human,
            byte[] switchExtension,
            out LegacyDbServerFrame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (human?.Data == null)
            {
                error = "native human save record is null";
                return false;
            }
            if (!TryEncodeShortString(account, AccountCapacity,
                    allowEmpty: false, out var accountBytes, out error)
                || !TryEncodeShortString(characterName, CharacterCapacity,
                    allowEmpty: false, out var characterBytes, out error))
                return false;
            if (saveMode == 2)
            {
                if (switchExtension == null
                    || switchExtension.Length != SwitchExtensionSize)
                {
                    error = $"native save mode 2 requires a {SwitchExtensionSize}-byte switch extension";
                    return false;
                }
            }
            else if (switchExtension is { Length: > 0 })
            {
                error = "native switch extension is only valid for save mode 2";
                return false;
            }

            // A fixed-size 0x0050 body can legitimately omit ScriptData. The shared
            // codec uses null to mean "no native ScriptData" and an empty array to
            // mean a malformed native ScriptData blob, while this M2 wire decoder
            // preserves the latter representation for callers. Convert only for
            // the duration of encoding so a no-script login can be saved again.
            var hadEmptyNativeScript = human.NativeScriptData is { Length: 0 };
            var emptyNativeScriptCrc = human.NativeScriptDataCrc;
            if (hadEmptyNativeScript)
            {
                human.NativeScriptData = null;
                human.NativeScriptDataCrc = 0;
            }

            if (!NativeHumanDataCodec.TryEncode(human, out _, out _, out error))
            {
                if (hadEmptyNativeScript)
                {
                    human.NativeScriptData = Array.Empty<byte>();
                    human.NativeScriptDataCrc = emptyNativeScriptCrc;
                }
                error = "native human save record: " + error;
                return false;
            }
            if (hadEmptyNativeScript && human.NativeScriptData == null)
            {
                human.NativeScriptData = Array.Empty<byte>();
                human.NativeScriptDataCrc = emptyNativeScriptCrc;
            }
            if (human.NativeData?.Length != NativeHumanDataCodec.DataRecordSize)
            {
                error = "native human save record has an invalid fixed size";
                return false;
            }

            var script = human.NativeScriptData ?? Array.Empty<byte>();
            int payloadLength;
            try
            {
                payloadLength = checked(ScriptDataOffset + script.Length);
            }
            catch (OverflowException)
            {
                error = "native human save ScriptData is too large";
                return false;
            }
            if (payloadLength > LegacyDbServerFrameCodec.DefaultMaximumFrameLength
                                - LegacyDbServerFrameCodec.HeaderSize)
            {
                error = "native human save frame exceeds the original 0x1FFFF-byte limit";
                return false;
            }

            var payload = new byte[payloadLength];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), SaveCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), saveMode);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param1);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), param2);
            WriteShortString(payload, AccountOffset, accountBytes);
            WriteShortString(payload, CharacterOffset, characterBytes);
            payload[0x35] = 0;
            human.NativeData.CopyTo(payload, NativeDataOffset);
            if (saveMode == 2)
                switchExtension.CopyTo(payload,
                    SessionSuffixOffset + SessionPrefixSize);
            if (script.Length > 0)
                script.CopyTo(payload, ScriptDataOffset);

            frame = new LegacyDbServerFrame(1, 0, payload);
            return true;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source, int offset,
            int capacity, bool allowEmpty, out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            if (offset < 0 || offset >= source.Length)
            {
                error = "native human short string is truncated";
                return false;
            }
            var length = source[offset];
            if (length > capacity || offset + 1 + length > source.Length)
            {
                error = $"native human short string at 0x{offset:X} exceeds {capacity} bytes";
                return false;
            }
            if (!allowEmpty && length == 0)
            {
                error = $"native human short string at 0x{offset:X} is empty";
                return false;
            }
            try
            {
                value = Gbk.GetString(source.Slice(offset + 1, length));
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = "native human short string is not valid GBK: " + ex.Message;
                return false;
            }
        }

        private static bool TryEncodeShortString(string value, int capacity,
            bool allowEmpty, out byte[] encoded, out string error)
        {
            encoded = Array.Empty<byte>();
            error = string.Empty;
            try
            {
                encoded = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "native human short string is not GBK: " + ex.Message;
                return false;
            }
            if ((!allowEmpty && encoded.Length == 0) || encoded.Length > capacity)
            {
                error = $"native human short string must contain 1..{capacity} GBK bytes";
                encoded = Array.Empty<byte>();
                return false;
            }
            return true;
        }

        private static void WriteShortString(byte[] destination, int offset,
            byte[] encoded)
        {
            destination[offset] = (byte)encoded.Length;
            encoded.CopyTo(destination, offset + 1);
        }
    }
}
