using MySql.Data.MySqlClient;
using System;
using System.Text;

namespace DBSvr.Core
{
    /// <summary>
    /// Native name columns are latin1_bin containers holding raw GBK bytes.
    /// Passing a .NET string lets MySQL transcode the value and corrupts Chinese names.
    /// </summary>
    public static class LegacyGbkText
    {
        private static readonly Encoding Gbk = Encoding.GetEncoding(936,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public static byte[] Encode(string value) => Gbk.GetBytes(value ?? string.Empty);

        public static MySqlParameter Parameter(string name, string value,
            MySqlDbType type = MySqlDbType.Binary) =>
            new(name, type) { Value = Encode(value) };

        public static string Read(MySqlDataReader reader, string column) =>
            Read(reader, reader.GetOrdinal(column));

        public static string Read(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return string.Empty;
            if (reader.GetValue(ordinal) is byte[] bytes) return Decode(bytes);
            return Decode(Latin1.GetBytes(reader.GetString(ordinal)));
        }

        public static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = bytes.Length;
            return length == 0 ? string.Empty : Gbk.GetString(bytes, 0, length);
        }
    }
}
