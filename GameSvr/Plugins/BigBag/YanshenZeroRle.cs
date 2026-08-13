using System;
using System.IO;

namespace GameSvr.Plugins.BigBag
{
    /// <summary>
    /// Zero-byte run-length codec that wraps every <c>Gs1\MyJson\bags\角色名.bin</c>
    /// written by the 眼神 plugin's "无限背包" (extra bag) persistence.
    ///
    /// <code>
    /// decode:  b == 0x00  ->  read the next byte XX, emit XX zero bytes, advance 2
    ///          b != 0x00  ->  emit b verbatim, advance 1
    /// encode:  a run of K zeros -> repeat "00 min(K,255)" until the run is consumed
    ///          non-zero bytes are emitted verbatim
    /// </code>
    ///
    /// Because a single zero encodes as <c>00 01</c>, a bare <c>0x00</c> literal can
    /// never appear in an encoded stream. Verified against all 31 production samples
    /// (<c>D:\光头卧龙\mud2.0\Mir200\Gs1\MyJson\bags\*.bin</c>): decoding and then
    /// re-encoding with the greedy encoder below reproduces all 31 files byte for byte.
    /// </summary>
    public static class YanshenZeroRle
    {
        /// <summary>Largest run a single <c>00 XX</c> pair can express.</summary>
        public const int MaximumRunLength = 255;

        /// <summary>
        /// Encode with the canonical greedy encoder the plugin uses.
        /// </summary>
        public static byte[] Encode(ReadOnlySpan<byte> plain)
        {
            var encoded = new byte[MeasureEncoded(plain)];
            var write = 0;
            var read = 0;
            while (read < plain.Length)
            {
                if (plain[read] != 0)
                {
                    encoded[write++] = plain[read++];
                    continue;
                }

                var run = CountZeroRun(plain, read);
                read += run;
                while (run > 0)
                {
                    var chunk = Math.Min(run, MaximumRunLength);
                    encoded[write++] = 0x00;
                    encoded[write++] = (byte)chunk;
                    run -= chunk;
                }
            }

            return encoded;
        }

        /// <summary>
        /// Decode an encoded stream. Fails only on a truncated trailing <c>0x00</c>
        /// that has no run-length byte after it, which no complete file can contain.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> encoded, out byte[] plain, out string error)
        {
            plain = null;
            if (!TryMeasureDecoded(encoded, out var length, out error))
                return false;

            var decoded = new byte[length];
            var write = 0;
            var read = 0;
            while (read < encoded.Length)
            {
                if (encoded[read] != 0)
                {
                    decoded[write++] = encoded[read++];
                    continue;
                }

                // The destination is already zero-filled, so a run only advances the cursor.
                write += encoded[read + 1];
                read += 2;
            }

            plain = decoded;
            return true;
        }

        /// <summary>
        /// Decode, throwing on a malformed stream. Prefer <see cref="TryDecode"/> on
        /// any path that reads player data off disk.
        /// </summary>
        public static byte[] Decode(ReadOnlySpan<byte> encoded)
        {
            if (!TryDecode(encoded, out var plain, out var error))
                throw new InvalidDataException(error);
            return plain;
        }

        /// <summary>
        /// Report whether an encoded stream is exactly what <see cref="Encode"/> would
        /// produce, i.e. whether <c>Encode(Decode(x)) == x</c> holds for it.
        ///
        /// A stream can decode correctly yet still be non-canonical, for example a
        /// zero-length run pair <c>00 00</c> or a run split more finely than the greedy
        /// encoder would split it. All 31 production samples are canonical. This is a
        /// diagnostic only: a non-canonical file still carries intact data, so callers
        /// must not reject one on this basis.
        /// </summary>
        public static bool IsCanonical(ReadOnlySpan<byte> encoded)
        {
            if (!TryDecode(encoded, out var plain, out _))
                return false;
            return Encode(plain).AsSpan().SequenceEqual(encoded);
        }

        private static int CountZeroRun(ReadOnlySpan<byte> plain, int start)
        {
            var run = 0;
            while (start + run < plain.Length && plain[start + run] == 0)
                run++;
            return run;
        }

        private static int MeasureEncoded(ReadOnlySpan<byte> plain)
        {
            var size = 0;
            var read = 0;
            while (read < plain.Length)
            {
                if (plain[read] != 0)
                {
                    size++;
                    read++;
                    continue;
                }

                var run = CountZeroRun(plain, read);
                read += run;
                // Every chunk costs the 2-byte pair "00 XX".
                size += 2 * ((run + MaximumRunLength - 1) / MaximumRunLength);
            }

            return size;
        }

        private static bool TryMeasureDecoded(ReadOnlySpan<byte> encoded, out int length, out string error)
        {
            length = 0;
            error = null;
            long size = 0;
            var read = 0;
            while (read < encoded.Length)
            {
                if (encoded[read] != 0)
                {
                    size++;
                    read++;
                    continue;
                }

                if (read + 1 >= encoded.Length)
                {
                    error = $"zero-RLE stream is truncated: run marker at offset {read} has no length byte";
                    return false;
                }

                size += encoded[read + 1];
                read += 2;
            }

            if (size > int.MaxValue)
            {
                error = "zero-RLE stream decodes to more than 2 GiB";
                return false;
            }

            length = (int)size;
            return true;
        }
    }
}
