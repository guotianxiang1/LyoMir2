using System.Collections.ObjectModel;
using System.Text;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// Dormant loader and selector for the native DiamondBounty.ini pools.
    /// It intentionally has no protocol, player, or reward-delivery integration.
    /// </summary>
    public sealed class NativeDiamondBountyConfig
    {
        public const int MaximumEntries = 100;
        public const int RandomRange = 100;
        private const int DescriptorMaximumGbkBytes = 0x33;

        private readonly ReadOnlyCollection<Entry> _claimRewards;
        private readonly ReadOnlyCollection<string> _additionalRewards;
        private readonly ReadOnlyCollection<byte[]> _additionalRewardGbkBytes;
        private readonly ReadOnlyCollection<Entry> _applicationRewards1;
        private readonly ReadOnlyCollection<Entry> _applicationRewards2;
        private readonly Func<int, int> _random;

        private NativeDiamondBountyConfig(IList<Entry> claimRewards,
            IList<string> additionalRewards,
            IList<Entry> applicationRewards1,
            IList<Entry> applicationRewards2,
            Func<int, int> random, bool sourceLoaded)
        {
            _claimRewards = new ReadOnlyCollection<Entry>(claimRewards);
            _additionalRewards =
                new ReadOnlyCollection<string>(additionalRewards);
            _additionalRewardGbkBytes = new ReadOnlyCollection<byte[]>(
                additionalRewards.Select(EncodeGbk).ToList());
            _applicationRewards1 =
                new ReadOnlyCollection<Entry>(applicationRewards1);
            _applicationRewards2 =
                new ReadOnlyCollection<Entry>(applicationRewards2);
            _random = random;
            SourceLoaded = sourceLoaded;
        }

        public IReadOnlyList<Entry> ClaimRewards => _claimRewards;
        public IReadOnlyList<string> AdditionalRewards => _additionalRewards;
        public IReadOnlyList<Entry> ApplicationRewards1 =>
            _applicationRewards1;
        public IReadOnlyList<Entry> ApplicationRewards2 =>
            _applicationRewards2;
        public bool SourceLoaded { get; }

        public ReadOnlyMemory<byte> GetAdditionalRewardGbkBytes(int index)
        {
            if ((uint)index >= (uint)_additionalRewardGbkBytes.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _additionalRewardGbkBytes[index];
        }

        public static bool TryLoad(string fileName,
            out NativeDiamondBountyConfig config, out string error)
        {
            return TryLoad(fileName,
                maximum => M2Share.RandomNumber.Random(maximum),
                out config, out error);
        }

        public static bool TryLoad(string fileName, Func<int, int> random,
            out NativeDiamondBountyConfig config, out string error)
        {
            config = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Share\\DiamondBounty.ini path is empty";
                return false;
            }
            if (random == null)
            {
                error = "native diamond-bounty random source is null";
                return false;
            }

            var claimRewards = new List<Entry>(MaximumEntries);
            var additionalRewards = new List<string>(MaximumEntries);
            var applicationRewards1 = new List<Entry>(MaximumEntries);
            var applicationRewards2 = new List<Entry>(MaximumEntries);
            if (!File.Exists(fileName))
            {
                config = new NativeDiamondBountyConfig(claimRewards,
                    additionalRewards, applicationRewards1,
                    applicationRewards2, random, false);
                return true;
            }

            try
            {
                var ini = new DiamondBountyIni(fileName);
                ReadWeightedRewards(ini, "领取奖励", claimRewards);
                ReadAdditionalRewards(ini, additionalRewards);
                ReadWeightedRewards(ini, "申请奖励1",
                    applicationRewards1);
                ReadWeightedRewards(ini, "申请奖励2",
                    applicationRewards2);
                config = new NativeDiamondBountyConfig(claimRewards,
                    additionalRewards, applicationRewards1,
                    applicationRewards2, random, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Selects the native comma-delimited descriptor. Random(100) returns
        /// 0..99 and a threshold match is inclusive. An uncovered tail keeps
        /// the first configured reward, matching sub_752024.
        /// </summary>
        public bool TrySelect(out string descriptor)
        {
            if (!TrySelectEntry(_claimRewards, out var selected))
            {
                descriptor = null;
                return false;
            }

            if (string.IsNullOrEmpty(selected.Descriptor))
            {
                descriptor = null;
                return false;
            }

            descriptor = selected.Descriptor;
            if (_additionalRewards.Count != 0)
                descriptor += "," + string.Join(",", _additionalRewards);
            return true;
        }

        public bool TrySelectGbk(out byte[] descriptor)
        {
            descriptor = null;
            if (!TrySelectEntry(_claimRewards, out var selected))
                return false;

            if (selected.DescriptorGbkBytes.IsEmpty)
                return false;

            var result = new List<byte>();
            AppendRaw(result, selected.DescriptorGbkBytes.Span);
            foreach (var additionalReward in _additionalRewardGbkBytes)
            {
                result.Add((byte)',');
                AppendRaw(result, additionalReward);
            }
            descriptor = result.ToArray();
            return true;
        }

        /// <summary>
        /// Mirrors sub_74E580: only descriptor indices 0 and 1 select the
        /// 申请奖励1/2 pools. Every other index clears the output.
        /// </summary>
        public bool TrySelectApplicationReward(int descriptorIndex,
            out string descriptor)
        {
            descriptor = null;
            var rewards = GetApplicationRewards(descriptorIndex);
            if (rewards == null || !TrySelectEntry(rewards, out var selected))
                return false;

            descriptor = selected.Descriptor;
            return !string.IsNullOrEmpty(descriptor);
        }

        public bool TrySelectApplicationRewardGbk(int descriptorIndex,
            out byte[] descriptor)
        {
            descriptor = null;
            var rewards = GetApplicationRewards(descriptorIndex);
            if (rewards == null || !TrySelectEntry(rewards, out var selected) ||
                selected.DescriptorGbkBytes.IsEmpty)
            {
                return false;
            }

            descriptor = selected.DescriptorGbkBytes.ToArray();
            return true;
        }

        private IReadOnlyList<Entry> GetApplicationRewards(
            int descriptorIndex)
        {
            return descriptorIndex switch
            {
                0 => _applicationRewards1,
                1 => _applicationRewards2,
                _ => null
            };
        }

        private bool TrySelectEntry(IReadOnlyList<Entry> rewards,
            out Entry selected)
        {
            selected = null;
            if (rewards.Count == 0)
                return false;

            var roll = _random(RandomRange);
            if ((uint)roll >= RandomRange)
            {
                throw new InvalidOperationException(
                    $"native diamond-bounty random returned {roll} outside " +
                    $"0..{RandomRange - 1}");
            }

            selected = rewards[0];
            foreach (var entry in rewards)
            {
                if (roll > entry.Threshold)
                    continue;
                selected = entry;
                break;
            }

            return true;
        }

        private static void ReadWeightedRewards(DiamondBountyIni ini,
            string section, List<Entry> result)
        {
            for (var number = 1; number <= MaximumEntries; number++)
            {
                var value = ini.ReadString(section, "奖品" + number, null);
                if (string.IsNullOrEmpty(value))
                    break;

                var previousThreshold = result.Count == 0
                    ? 0
                    : result[result.Count - 1].Threshold;
                if (!TryParseEntry(value, result.Count != 0,
                        previousThreshold, out var entry))
                    continue;

                result.Add(entry);
                if (entry.Threshold >= RandomRange - 1)
                    break;
            }
        }

        private static void ReadAdditionalRewards(DiamondBountyIni ini,
            List<string> result)
        {
            for (var number = 1; number <= MaximumEntries; number++)
            {
                var value = ini.ReadString(
                    "领取额外奖励", "奖品" + number, null);
                if (string.IsNullOrEmpty(value))
                    break;
                result.Add(value);
            }
        }

        private static bool TryParseEntry(string value, bool hasPrevious,
            int previousThreshold, out Entry entry)
        {
            entry = null;
            var separator = value.IndexOf('/');
            if (separator < 0)
                return false;

            var descriptorSource = value.Substring(0, separator);
            var descriptor = TruncateDescriptorText(descriptorSource);
            var descriptorGbkBytes =
                TruncateDescriptorGbkBytes(descriptorSource);
            var thresholdLength = Math.Min(5, value.Length - separator - 1);
            var thresholdText = value.Substring(separator + 1, thresholdLength);
            var threshold = ParseNativeIntegerOrZero(thresholdText);
            if (hasPrevious && threshold <= previousThreshold)
                return false;

            entry = new Entry(descriptor, descriptorGbkBytes, threshold);
            return true;
        }

        private static byte[] TruncateDescriptorGbkBytes(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor))
                return Array.Empty<byte>();

            var encoded = EncodeGbk(descriptor);
            if (encoded.Length <= DescriptorMaximumGbkBytes) return encoded;
            return encoded.AsSpan(0, DescriptorMaximumGbkBytes).ToArray();
        }

        private static string TruncateDescriptorText(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor))
                return string.Empty;

            var result = new StringBuilder(descriptor.Length);
            var byteCount = 0;
            foreach (var rune in descriptor.EnumerateRunes())
            {
                var text = rune.ToString();
                var runeBytes = HUtil32.GbkEncoding.GetByteCount(text);
                if (byteCount + runeBytes > DescriptorMaximumGbkBytes)
                    break;
                result.Append(text);
                byteCount += runeBytes;
            }
            return result.ToString();
        }

        private static byte[] EncodeGbk(string value)
        {
            return HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
        }

        private static void AppendRaw(List<byte> target,
            ReadOnlySpan<byte> value)
        {
            for (var index = 0; index < value.Length; index++)
                target.Add(value[index]);
        }

        private static int ParseNativeIntegerOrZero(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var index = 0;
            while (index < text.Length && text[index] == ' ')
                index++;

            var negative = false;
            if (index < text.Length &&
                (text[index] == '+' || text[index] == '-'))
            {
                negative = text[index] == '-';
                index++;
            }
            if (index >= text.Length)
                return 0;

            var hexadecimal = false;
            if (text[index] == '$' || text[index] == 'x' ||
                text[index] == 'X')
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
            if (index >= text.Length)
                return 0;

            long magnitude = 0;
            var radix = hexadecimal ? 16 : 10;
            while (index < text.Length)
            {
                var character = text[index++];
                int digit;
                if (character >= '0' && character <= '9')
                    digit = character - '0';
                else if (hexadecimal && character >= 'a' && character <= 'f')
                    digit = character - 'a' + 10;
                else if (hexadecimal && character >= 'A' && character <= 'F')
                    digit = character - 'A' + 10;
                else
                    return 0;

                var limit = negative ? 2147483648L : int.MaxValue;
                if (digit >= radix || magnitude > (limit - digit) / radix)
                    return 0;
                magnitude = magnitude * radix + digit;
            }

            return negative ? unchecked((int)-magnitude) : (int)magnitude;
        }

        private sealed class DiamondBountyIni : IniFile
        {
            internal DiamondBountyIni(string fileName) : base(fileName)
            {
                try
                {
                    Load();
                }
                catch (Exception ex) when (ex.GetType() == typeof(Exception) &&
                    ConfigCount == 0 && string.Equals(ex.Message,
                        $"配置文件[{fileName}]不存在或配置文件内容为空。",
                        StringComparison.Ordinal))
                {
                    // The native loader accepts an existing empty file.
                }
            }
        }

        public sealed class Entry
        {
            private readonly byte[] _descriptorGbkBytes;

            internal Entry(string descriptor, byte[] descriptorGbkBytes,
                int threshold)
            {
                _descriptorGbkBytes = descriptorGbkBytes.ToArray();
                Descriptor = descriptor;
                Threshold = threshold;
            }

            public string Descriptor { get; }
            public ReadOnlyMemory<byte> DescriptorGbkBytes =>
                _descriptorGbkBytes;
            public int Threshold { get; }
            public string Source => Descriptor + "/" + Threshold;
        }
    }
}
