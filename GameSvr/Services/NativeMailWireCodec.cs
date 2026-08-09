using System.Buffers.Binary;
using System.Text;

namespace GameSvr.Services
{
    public readonly record struct NativeMailListInfo(
        int Id,
        string Title,
        string Sender,
        int MailState,
        int AttachState,
        double Time);

    public readonly record struct NativeMailInfo(
        int Id,
        string Sender,
        string Title,
        string Context,
        int MailState,
        int AttachState,
        int Type,
        double Time,
        int Gold,
        int Yb,
        int Count,
        int Mark);

    public readonly record struct NativeMailMessage(
        string Name,
        double Time,
        string Message);

    public static class NativeMailWireCodec
    {
        public const int MailListInfoSize = 56;
        public const int MailInfoSize = 280;
        public const int MailMessageSize = 80;

        public const int MailListIdOffset = 0;
        public const int MailListTitleOffset = 4;
        public const int MailListSenderOffset = 25;
        public const int MailListStateOffset = 40;
        public const int MailListAttachStateOffset = 44;
        public const int MailListTimeOffset = 48;

        public const int MailInfoIdOffset = 0;
        public const int MailInfoSenderOffset = 4;
        public const int MailInfoTitleOffset = 19;
        public const int MailInfoContextOffset = 40;
        public const int MailInfoStateOffset = 244;
        public const int MailInfoAttachStateOffset = 248;
        public const int MailInfoTypeOffset = 252;
        public const int MailInfoTimeOffset = 256;
        public const int MailInfoGoldOffset = 264;
        public const int MailInfoYbOffset = 268;
        public const int MailInfoCountOffset = 272;
        public const int MailInfoMarkOffset = 276;

        public const int MailMessageNameOffset = 0;
        public const int MailMessageTimeOffset = 16;
        public const int MailMessageTextOffset = 24;

        private const int MailListTitleSize = 21;
        private const int MailListSenderSize = 15;
        private const int MailInfoSenderSize = 15;
        private const int MailInfoTitleSize = 21;
        private const int MailInfoContextSize = 201;
        private const int MailMessageNameSize = 15;
        private const int MailMessageTextSize = 51;

        private static readonly Encoding Gbk = CreateGbkEncoding();

        public static byte[] Encode(in NativeMailListInfo value)
        {
            var result = new byte[MailListInfoSize];
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailListIdOffset, sizeof(int)), value.Id);
            WriteFixedGbk(result, MailListTitleOffset, MailListTitleSize, value.Title);
            WriteFixedGbk(result, MailListSenderOffset, MailListSenderSize, value.Sender);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailListStateOffset, sizeof(int)), value.MailState);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailListAttachStateOffset, sizeof(int)), value.AttachState);
            WriteDouble(result, MailListTimeOffset, value.Time);
            return result;
        }

        public static byte[] Encode(in NativeMailInfo value)
        {
            var result = new byte[MailInfoSize];
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoIdOffset, sizeof(int)), value.Id);
            WriteFixedGbk(result, MailInfoSenderOffset, MailInfoSenderSize, value.Sender);
            WriteFixedGbk(result, MailInfoTitleOffset, MailInfoTitleSize, value.Title);
            WriteFixedGbk(result, MailInfoContextOffset, MailInfoContextSize, value.Context);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoStateOffset, sizeof(int)), value.MailState);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoAttachStateOffset, sizeof(int)), value.AttachState);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoTypeOffset, sizeof(int)), value.Type);
            WriteDouble(result, MailInfoTimeOffset, value.Time);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoGoldOffset, sizeof(int)), value.Gold);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoYbOffset, sizeof(int)), value.Yb);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoCountOffset, sizeof(int)), value.Count);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(MailInfoMarkOffset, sizeof(int)), value.Mark);
            return result;
        }

        public static byte[] Encode(in NativeMailMessage value)
        {
            var result = new byte[MailMessageSize];
            WriteFixedGbk(result, MailMessageNameOffset, MailMessageNameSize, value.Name);
            WriteDouble(result, MailMessageTimeOffset, value.Time);
            WriteFixedGbk(result, MailMessageTextOffset, MailMessageTextSize, value.Message);
            return result;
        }

        private static void WriteFixedGbk(
            byte[] destination,
            int offset,
            int fieldSize,
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            var nul = value.IndexOf('\0');
            var chars = (nul >= 0 ? value.AsSpan(0, nul) : value.AsSpan());
            Gbk.GetEncoder().Convert(
                chars,
                destination.AsSpan(offset, fieldSize - 1),
                true,
                out _,
                out _,
                out _);
        }

        private static void WriteDouble(byte[] destination, int offset, double value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(
                destination.AsSpan(offset, sizeof(long)),
                BitConverter.DoubleToInt64Bits(value));
        }

        private static Encoding CreateGbkEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(
                936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }
    }
}
