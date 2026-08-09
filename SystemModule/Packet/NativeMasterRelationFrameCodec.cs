using System;
using System.Buffers.Binary;
using System.Text;

namespace SystemModule.Packet
{
    public static class NativeMasterRelationFrameCodec
    {
        public const ushort RequestCommand = 0x0152;
        public const ushort MarriageClearSubcommand = 0;

        /// <summary>
        /// 战神 0x6C603F `mov si, 1` -- the student walked out of the school
        /// (自行离开师门).  Emitted by sub_6C5EC8's offline-master leg only.
        /// </summary>
        public const ushort StudentLeftSubcommand = 1;

        public const ushort ClearSubcommand = 3;

        /// <summary>
        /// 战神 0x6C6045 `mov si, 4` -- the student graduated (顺利出师).
        /// Emitted by sub_6C5EC8's offline-master leg only.
        /// </summary>
        public const ushort StudentGraduatedSubcommand = 4;
        public const int PayloadSize = 0x48;
        public const int AccountOffset = 0x10;
        public const int MasterNameOffset = 0x25;
        public const int StudentNameOffset = 0x35;

        private static readonly Encoding Gbk;

        static NativeMasterRelationFrameCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }

        public static bool TryEncodeClear(string account, string masterName,
            string studentName, out byte[] frame, out string error)
        {
            return TryEncode(ClearSubcommand, account, masterName, studentName,
                out frame, out error);
        }

        public static bool TryEncodeMarriageClear(string account,
            string currentName, string spouseName, out byte[] frame,
            out string error)
        {
            return TryEncode(MarriageClearSubcommand, account, currentName,
                spouseName, out frame, out error);
        }

        public static bool TryEncode(ushort subcommand, string account,
            string sourceName, string targetName, out byte[] frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            var payload = new byte[PayloadSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2),
                RequestCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                subcommand);
            if (!TryWriteShortString(payload, AccountOffset, 20, account,
                    out error)
                || !TryWriteShortString(payload, MasterNameOffset, 15,
                    sourceName, out error)
                || !TryWriteShortString(payload, StudentNameOffset, 15,
                    targetName, out error))
                return false;

            return LegacyDbServerFrameCodec.TryEncode(
                new LegacyDbServerFrame(1, 0, payload), out frame, out error);
        }

        private static bool TryWriteShortString(Span<byte> destination,
            int offset, int maximumLength, string value, out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "native master relation string is not GBK: "
                        + ex.Message;
                return false;
            }
            if (bytes.Length > maximumLength)
            {
                error = $"native master relation string exceeds "
                        + $"{maximumLength} GBK bytes";
                return false;
            }

            destination[offset] = (byte)bytes.Length;
            bytes.CopyTo(destination.Slice(offset + 1));
            return true;
        }
    }
}
