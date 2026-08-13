using System;
using System.Collections.Generic;
using System.IO;

namespace GameSvr.Plugins.BigBag
{
    /// <summary>
    /// One character's <c>Gs1\MyJson\bags\角色名.bin</c> extra-bag file.
    ///
    /// The decoded payload is a 16-byte header followed by N fixed 208-byte records,
    /// and the whole thing is wrapped in <see cref="YanshenZeroRle"/>. The record count
    /// is not stored anywhere; it follows from the length. All 31 production samples
    /// decode to exactly <c>16 + 208 * N</c> bytes with N between 2 and 131, and their
    /// headers are all zero.
    ///
    /// Decoding is deliberately fail-closed: a payload whose length is not
    /// <c>16 + 208 * N</c> is reported as damaged rather than salvaged, because the only
    /// way to "repair" it would be to guess where a record boundary lies and a wrong
    /// guess silently rewrites the player's items.
    /// </summary>
    public sealed class YanshenBigBagFile
    {
        public const int HeaderSize = 16;

        /// <summary>
        /// The 16-byte header, all zero in every sample. Carried through verbatim rather
        /// than regenerated, since its field layout cannot be recovered from all-zero data.
        /// </summary>
        public byte[] Header = new byte[HeaderSize];

        public List<YanshenBigBagRecord> Records = new List<YanshenBigBagRecord>();

        public int RecordCount => Records == null ? 0 : Records.Count;

        /// <summary>Decoded payload size for a given record count.</summary>
        public static int MeasurePlainSize(int recordCount)
            => HeaderSize + recordCount * YanshenBigBagRecord.RecordSize;

        /// <summary>
        /// Decode raw file bytes: strip the zero-RLE layer, then split the payload into
        /// fixed records.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> encoded, out YanshenBigBagFile file, out string error)
        {
            file = null;
            if (!YanshenZeroRle.TryDecode(encoded, out var plain, out error))
                return false;

            if (plain.Length < HeaderSize)
            {
                error = $"extra-bag payload is {plain.Length} bytes, shorter than the {HeaderSize}-byte header";
                return false;
            }

            var body = plain.Length - HeaderSize;
            if (body % YanshenBigBagRecord.RecordSize != 0)
            {
                error = $"extra-bag payload is {plain.Length} bytes, which is not {HeaderSize} + " +
                        $"{YanshenBigBagRecord.RecordSize} * N; the file is damaged";
                return false;
            }

            var result = new YanshenBigBagFile();
            Array.Copy(plain, 0, result.Header, 0, HeaderSize);

            var count = body / YanshenBigBagRecord.RecordSize;
            result.Records = new List<YanshenBigBagRecord>(count);
            for (var index = 0; index < count; index++)
            {
                var offset = HeaderSize + index * YanshenBigBagRecord.RecordSize;
                if (!YanshenBigBagRecord.TryParse(
                        plain.AsSpan(offset, YanshenBigBagRecord.RecordSize), out var parsed, out error))
                {
                    error = $"extra-bag record {index}: {error}";
                    return false;
                }

                result.Records.Add(parsed);
            }

            file = result;
            error = null;
            return true;
        }

        /// <summary>
        /// Rebuild the raw file bytes. For a file that was decoded and not modified this
        /// reproduces the original bytes exactly.
        /// </summary>
        public bool TryEncode(out byte[] encoded, out string error)
        {
            encoded = null;
            if (Header == null || Header.Length != HeaderSize)
            {
                error = $"extra-bag header must be a {HeaderSize}-byte array";
                return false;
            }

            var records = Records ?? new List<YanshenBigBagRecord>();
            var plain = new byte[MeasurePlainSize(records.Count)];
            Header.CopyTo(plain, 0);

            for (var index = 0; index < records.Count; index++)
            {
                var offset = HeaderSize + index * YanshenBigBagRecord.RecordSize;
                if (records[index] == null)
                {
                    error = $"extra-bag record {index} is null";
                    return false;
                }

                if (!records[index].TryWrite(
                        plain.AsSpan(offset, YanshenBigBagRecord.RecordSize), out error))
                {
                    error = $"extra-bag record {index}: {error}";
                    return false;
                }
            }

            encoded = YanshenZeroRle.Encode(plain);
            error = null;
            return true;
        }

        public static bool TryLoad(string path, out YanshenBigBagFile file, out string error)
        {
            file = null;
            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                error = $"cannot read {path}: {ex.Message}";
                return false;
            }

            if (!TryDecode(raw, out file, out error))
            {
                error = $"{path}: {error}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write through a temporary file so a crash mid-write cannot leave a truncated
        /// bag behind — a half-written file fails the length gate on the next load and
        /// the character's extra items would be unreadable.
        /// </summary>
        public bool TrySave(string path, out string error)
        {
            if (!TryEncode(out var encoded, out error))
                return false;

            var temporaryPath = path + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(temporaryPath, encoded);
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                    // A leftover .tmp is harmless; the real failure is already being reported.
                }

                error = $"cannot write {path}: {ex.Message}";
                return false;
            }
        }
    }
}
