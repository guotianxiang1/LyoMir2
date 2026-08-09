using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DBSvr.Core
{
    public static class NativeLoginResultCodec
    {
        public const int AccountCapacity = 20;
        public const int ServerNameCapacity = 20;
        public const int ReconnectIdCapacity = 36;
        public const int BodySize = 88;

        private const string ReconnectAlphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        public static byte[] Encode(string account, string serverName,
            int areaId, int groupId, string reconnectId)
        {
            var body = new byte[BodySize];
            WriteCString(body.AsSpan(0, AccountCapacity + 1),
                account, AccountCapacity);
            WriteCString(body.AsSpan(AccountCapacity + 1, ServerNameCapacity + 1),
                serverName, ServerNameCapacity);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(42, 4), areaId);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(46, 4), groupId);
            WriteShortString(body.AsSpan(50, ReconnectIdCapacity + 2),
                reconnectId, ReconnectIdCapacity);
            return body;
        }

        public static string CreateReconnectId(string account)
        {
            var accountText = account ?? string.Empty;
            if (Encoding.ASCII.GetByteCount(accountText) != accountText.Length)
                throw new ArgumentException("login account must be ASCII", nameof(account));

            var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(2));
            var result = prefix + accountText;
            var randomLength = Math.Max(0, ReconnectIdCapacity - result.Length);
            if (randomLength > 0)
            {
                var random = RandomNumberGenerator.GetBytes(randomLength);
                var suffix = new char[randomLength];
                for (var i = 0; i < suffix.Length; i++)
                    suffix[i] = ReconnectAlphabet[random[i] % ReconnectAlphabet.Length];
                result += new string(suffix);
            }
            if (result.Length > ReconnectIdCapacity)
                result = result.Substring(0, ReconnectIdCapacity);
            return result;
        }

        private static void WriteCString(Span<byte> destination, string value,
            int capacity)
        {
            var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            if (bytes.Length > capacity)
                throw new ArgumentException($"login string exceeds {capacity} bytes");
            destination.Clear();
            bytes.CopyTo(destination);
        }

        private static void WriteShortString(Span<byte> destination, string value,
            int capacity)
        {
            var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            if (bytes.Length > capacity)
                throw new ArgumentException($"login short string exceeds {capacity} bytes");
            destination.Clear();
            destination[0] = (byte)bytes.Length;
            bytes.CopyTo(destination.Slice(1));
        }
    }
}
