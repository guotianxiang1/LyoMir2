using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// Loads and selects entries from the native Config\NormalPrize.ini pools.
    ///
    /// The original server builds 99 pools (1..99), each with up to 100
    /// "奖品N" rows.  A row is a raw reward descriptor followed by a slash and
    /// its cumulative threshold (for example, "经验:8000000/999").
    /// </summary>
    public sealed class NativeConfigPrizeManager
    {
        public const int PoolCount = 99;
        public const int EntriesPerPool = 100;
        public const int RandomRange = 1000;
        private const int DescriptorMaxGbkBytes = 0x33;
        private const string ConfigErrorPrefix = "[Error]:NormalPrize.ini 奖励配置错误：";

        private readonly List<Entry>[] _pools;
        private readonly ReadOnlyCollection<Entry>[] _poolViews;
        private readonly Func<int, int> _random;

        private NativeConfigPrizeManager(Func<int, int> random)
        {
            _random = random;
            _pools = new List<Entry>[PoolCount];
            _poolViews = new ReadOnlyCollection<Entry>[PoolCount];
            for (var i = 0; i < PoolCount; i++)
            {
                _pools[i] = new List<Entry>(EntriesPerPool);
                _poolViews[i] = _pools[i].AsReadOnly();
            }
        }

        internal static NativeConfigPrizeManager CreateNative()
        {
            return new NativeConfigPrizeManager(
                maximum => M2Share.RandomNumber.Random(maximum));
        }

        /// <summary>
        /// Loads a native prize file using the server's shared random source.
        /// </summary>
        public static bool TryLoad(string fileName,
            out NativeConfigPrizeManager manager, out string error)
        {
            return TryLoad(fileName,
                maximum => M2Share.RandomNumber.Random(maximum),
                out manager, out error);
        }

        /// <summary>
        /// Loads a native prize file.  The random delegate is injectable so
        /// compatibility checks can exercise threshold boundaries exactly.
        /// </summary>
        public static bool TryLoad(string fileName, Func<int, int> random,
            out NativeConfigPrizeManager manager, out string error)
        {
            manager = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Config\\NormalPrize.ini path is empty";
                return false;
            }

            if (random == null)
            {
                error = "native config-prize random source is null";
                return false;
            }

            manager = new NativeConfigPrizeManager(random);
            return manager.ReloadInPlace(fileName, out error);
        }

        /// <summary>
        /// Clears and repopulates the existing native manager and its 99 pool
        /// objects. Semantic validation failures return false with the partial
        /// table still installed; I/O and unexpected failures propagate.
        /// </summary>
        public bool ReloadInPlace(string fileName, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Config\\NormalPrize.ini path is empty";
                return false;
            }

            // Native sub_74EBB4 clears all existing pool objects before it
            // checks whether Config\NormalPrize.ini exists.
            for (var i = 0; i < _pools.Length; i++)
                _pools[i].Clear();

            // IniFile.Load creates a missing file. The native loader instead
            // leaves the same 99 pools empty and reports success.
            if (!File.Exists(fileName))
                return true;

            var ini = new NormalPrizeIni(fileName);
            for (var poolNumber = 1; poolNumber <= PoolCount; poolNumber++)
            {
                var pool = _pools[poolNumber - 1];
                ReadPool(ini, poolNumber, pool, out var lastThreshold);

                // The native loader only rejects a positive, incomplete
                // tail. Empty, zero and negative tails remain valid.
                if (lastThreshold > 0 && lastThreshold < 999)
                {
                    LogConfigError("奖励" + poolNumber);
                    error = $"pool {poolNumber}: cumulative threshold "
                        + "does not reach 999";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns an immutable view of a 1-based prize pool.
        /// </summary>
        public IReadOnlyList<Entry> GetPool(int prizeIndex)
        {
            ValidatePoolIndex(prizeIndex);
            return _poolViews[prizeIndex - 1];
        }

        /// <summary>
        /// Selects the first entry whose cumulative threshold is greater than
        /// or equal to Random(1000), matching the Delphi selector.
        /// </summary>
        public bool TrySelect(int prizeIndex, out string descriptor)
        {
            descriptor = null;
            if (!TrySelectEntry(prizeIndex, out var selected))
                return false;

            descriptor = selected.Descriptor;
            return true;
        }

        public bool TrySelectGbk(int prizeIndex, out byte[] descriptor)
        {
            descriptor = null;
            if (!TrySelectEntry(prizeIndex, out var selected))
                return false;

            descriptor = selected.DescriptorGbkBytes.ToArray();
            return true;
        }

        private bool TrySelectEntry(int prizeIndex, out Entry selected)
        {
            selected = null;
            if ((uint)(prizeIndex - 1) >= PoolCount)
                return false;

            var pool = _pools[prizeIndex - 1];
            if (pool.Count == 0)
                return false;

            var value = _random(RandomRange);
            if ((uint)value >= RandomRange)
            {
                throw new InvalidOperationException(
                    $"native config-prize random returned {value} outside "
                    + $"0..{RandomRange - 1}");
            }

            foreach (var entry in pool)
            {
                if (value <= entry.Threshold)
                {
                    selected = entry;
                    return true;
                }
            }

            // Native zero/negative tails are valid and may legitimately
            // leave Random(1000) without a matching reward.
            return false;
        }

        private static void ReadPool(NormalPrizeIni ini, int poolNumber,
            List<Entry> result, out int lastThreshold)
        {
            lastThreshold = 0;
            var section = "奖励" + poolNumber;

            for (var entryNumber = 1; entryNumber <= EntriesPerPool;
                entryNumber++)
            {
                var key = "奖品" + entryNumber;
                var value = ini.ReadString(section, key, null);

                // Missing and explicitly empty rows both terminate the
                // current pool in the Delphi loader.
                if (string.IsNullOrWhiteSpace(value))
                    break;

                if (!TryParseEntry(value, result.Count != 0, lastThreshold,
                    out var entry, out _))
                {
                    // Invalid rows are skipped; scanning continues with the
                    // next fixed key, exactly as the native loader does.
                    LogConfigError(value);
                    continue;
                }

                result.Add(entry);
                lastThreshold = entry.Threshold;
                if (lastThreshold >= 999)
                    break;
            }
        }

        private static bool TryParseEntry(string value, bool hasPrevious,
            int previousThreshold, out Entry entry, out string error)
        {
            entry = null;
            error = string.Empty;

            var separator = value.IndexOf('/');
            if (separator < 0)
            {
                error = "expected descriptor/threshold";
                return false;
            }

            var descriptorSource = value.Substring(0, separator);
            var descriptor = TruncateDescriptorText(descriptorSource);
            var descriptorGbkBytes =
                TruncateDescriptorGbkBytes(descriptorSource);
            var thresholdText = value.Substring(separator + 1).Trim();
            var threshold = ParseNativeDelphiIntegerOrZero(thresholdText);
            if (hasPrevious && threshold <= previousThreshold)
            {
                error = "non-increasing threshold";
                return false;
            }

            entry = new Entry(descriptor, descriptorGbkBytes, threshold);
            return true;
        }

        private static string TruncateDescriptorText(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor))
                return string.Empty;

            var builder = new StringBuilder(descriptor.Length);
            var byteCount = 0;
            foreach (var rune in descriptor.EnumerateRunes())
            {
                var text = rune.ToString();
                var runeByteCount = HUtil32.GbkEncoding.GetByteCount(text);
                if (byteCount + runeByteCount > DescriptorMaxGbkBytes)
                    break;

                builder.Append(text);
                byteCount += runeByteCount;
            }

            return builder.ToString();
        }

        private static byte[] TruncateDescriptorGbkBytes(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor))
                return Array.Empty<byte>();

            var encoded = HUtil32.GbkEncoding.GetBytes(descriptor);
            if (encoded.Length <= DescriptorMaxGbkBytes) return encoded;
            return encoded.AsSpan(0, DescriptorMaxGbkBytes).ToArray();
        }

        private static int ParseNativeDelphiIntegerOrZero(string text)
        {
            return TryParseNativeDelphiInteger(text, out var value) ? value : 0;
        }

        private static bool TryParseNativeDelphiInteger(string text,
            out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;

            var index = 0;
            while (index < text.Length && text[index] == ' ') index++;

            var negative = false;
            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
            {
                negative = text[index] == '-';
                index++;
            }
            if (index >= text.Length) return false;

            var hexadecimal = false;
            if (text[index] == '$' || text[index] == 'x' || text[index] == 'X')
            {
                hexadecimal = true;
                index++;
            }
            else if (text[index] == '0' && index + 1 < text.Length &&
                     (text[index + 1] == 'x' || text[index + 1] == 'X'))
            {
                hexadecimal = true;
                index += 2;
            }
            if (index >= text.Length) return false;

            if (hexadecimal)
            {
                uint bits = 0;
                while (index < text.Length)
                {
                    var c = text[index++];
                    int digit;
                    if (c >= '0' && c <= '9') digit = c - '0';
                    else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                    else return false;

                    if (bits > 0x0FFFFFFF) return false;
                    bits = (bits << 4) | (uint)digit;
                }

                var parsed = unchecked((int)bits);
                value = negative ? unchecked(-parsed) : parsed;
                return true;
            }

            long magnitude = 0;
            while (index < text.Length)
            {
                var c = text[index++];
                if (c < '0' || c > '9') return false;
                var limit = negative ? 2147483648L : int.MaxValue;
                var digit = c - '0';
                if (magnitude > (limit - digit) / 10) return false;
                magnitude = magnitude * 10 + digit;
            }

            value = negative ? unchecked((int)-magnitude) : (int)magnitude;
            return true;
        }

        private static void ValidatePoolIndex(int prizeIndex)
        {
            if ((uint)(prizeIndex - 1) >= PoolCount)
                throw new ArgumentOutOfRangeException(nameof(prizeIndex),
                    "Prize index must be in the native 1..99 range.");
        }

        private static void LogConfigError(string suffix)
        {
            if (M2Share.LogSystem != null)
                M2Share.ErrorMessage(ConfigErrorPrefix + suffix);
        }

        private sealed class NormalPrizeIni : IniFile
        {
            internal NormalPrizeIni(string fileName) : base(fileName)
            {
                try
                {
                    Load();
                }
                catch (Exception ex) when (ex.GetType() == typeof(Exception)
                    && ConfigCount == 0
                    && string.Equals(ex.Message,
                        $"配置文件[{fileName}]不存在或配置文件内容为空。",
                        StringComparison.Ordinal))
                {
                    // IniFile alone rejects an existing logically empty file.
                    // Native NormalPrize loading accepts it as 99 empty pools.
                }
            }
        }

        public sealed class Entry
        {
            private readonly byte[] _descriptorGbkBytes;

            internal Entry(string descriptor, byte[] descriptorGbkBytes,
                int threshold)
            {
                Descriptor = descriptor;
                _descriptorGbkBytes = descriptorGbkBytes.ToArray();
                Threshold = threshold;
            }

            public string Descriptor { get; }
            public ReadOnlyMemory<byte> DescriptorGbkBytes =>
                _descriptorGbkBytes;
            public string Source => Descriptor + "/" + Threshold;
            public int Threshold { get; }
        }
    }
}
