using System.Text;

namespace GameSvr.PasEngine
{
    internal static class PasScriptTextReader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Gbk = Encoding.GetEncoding("GBK");

        public static string ReadAllText(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return StrictUtf8.GetString(bytes, 3, bytes.Length - 3);

            var utf8 = TryReadUtf8(bytes);
            if (utf8 != null)
                return utf8;

            return Gbk.GetString(bytes);
        }

        public static string[] ReadAllLines(string path)
        {
            return ReadAllText(path)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }

        private static string TryReadUtf8(byte[] bytes)
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }
    }
}
