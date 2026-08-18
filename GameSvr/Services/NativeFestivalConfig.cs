using System.Collections.ObjectModel;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// Native feast-day loader <c>sub_655AD0</c> @0x00655AD0.
    /// Reads <c>FeastDays.ini</c> (string @0x00655CDC), section prefix
    /// <c>Feast</c> (@0x00655CF4), keys <c>FileName</c> / <c>Start</c> / <c>End</c>
    /// (@0x00655D04 / @0x00655D18 / @0x00655D2C). Missing file logs
    /// <c>节日配置文件不存在：</c> (@0x00655D38) via MainOutMessage (cl=1 @0x00655C93).
    /// The loop accepts Feast1..Feast100 (the post-increment value is compared with
    /// 0x65 @0x00655C5D). Start/End pass through sub_4C75CC and then
    /// sub_49E36C, producing a zero-based second-of-year value; an empty field or
    /// a parsed zero terminates the loop.
    /// </summary>
    public sealed class NativeFestivalConfig
    {
        public const int LoaderAddress = 0x00655AD0;
        public const int MaximumEntries = 100;
        public const int FileNameMaximumGbkBytes = 0x1F;

        private readonly ReadOnlyCollection<FeastDayEntry> _entries;

        private NativeFestivalConfig(IList<FeastDayEntry> entries, bool sourceLoaded)
        {
            _entries = new ReadOnlyCollection<FeastDayEntry>(entries);
            SourceLoaded = sourceLoaded;
        }

        public IReadOnlyList<FeastDayEntry> Entries => _entries;
        public bool SourceLoaded { get; }

        public static bool TryLoad(string fileName,
            out NativeFestivalConfig config, out string error)
        {
            config = null;
            error = string.Empty;
            var entries = new List<FeastDayEntry>(MaximumEntries);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "FeastDays.ini path is empty";
                return false;
            }

            if (!File.Exists(fileName))
            {
                var message = "节日配置文件不存在：" + fileName;
                M2Share.MainOutMessage(message);
                config = new NativeFestivalConfig(entries, false);
                return true;
            }

            try
            {
                var ini = new FeastDaysIni(fileName);
                for (var index = 1; index <= MaximumEntries; index++)
                {
                    var section = "Feast" + index;
                    var feastFile = ini.ReadString(section, "FileName", string.Empty);
                    if (feastFile.Length == 0)
                        break;

                    var startText = ini.ReadString(section, "Start", string.Empty);
                    if (startText.Length == 0)
                        break;
                    int start = ParseNativeSecondOfYear(startText);
                    if (start == 0)
                        break;

                    var endText = ini.ReadString(section, "End", string.Empty);
                    if (endText.Length == 0)
                        break;
                    int end = ParseNativeSecondOfYear(endText);
                    if (end == 0)
                        break;

                    entries.Add(new FeastDayEntry(feastFile, start, end));
                }

                config = new NativeFestivalConfig(entries, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string ResolveDefaultPath(string rootPath, string baseDirectory)
        {
            if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(baseDirectory))
                return null;
            return Path.Combine(rootPath, baseDirectory, "FeastDays.ini");
        }

        public static NativeFestivalConfig Append(NativeFestivalConfig current,
            NativeFestivalConfig loaded)
        {
            if (loaded == null)
                return current;
            if (current == null)
                return loaded;
            if (!loaded.SourceLoaded)
                return current;

            var entries = new List<FeastDayEntry>(
                current.Entries.Count + loaded.Entries.Count);
            entries.AddRange(current.Entries);
            entries.AddRange(loaded.Entries);
            return new NativeFestivalConfig(entries,
                current.SourceLoaded || loaded.SourceLoaded);
        }

        internal static int ParseNativeSecondOfYear(string value)
        {
            if (value == null)
                return 0;

            try
            {
                string remaining = value;
                int year = ParseNativePart(TakeNativePart(ref remaining, '-'), 2006);
                int month = ParseNativePart(TakeNativePart(ref remaining, '-'), 1);
                int day = ParseNativePart(
                    TakeNativePart(ref remaining, ' ', '\t'), 1);
                int hour = ParseNativePart(TakeNativePart(ref remaining, ':'), 1);
                int minute = ParseNativePart(TakeNativePart(ref remaining, ':'), 1);
                int second = ParseNativePart(TrimNativeWhitespace(remaining), 0);

                var nativeYear = unchecked((ushort)year);
                var nativeMonth = unchecked((ushort)month);
                var nativeDay = unchecked((ushort)day);
                var nativeHour = unchecked((ushort)hour);
                var nativeMinute = unchecked((ushort)minute);
                var nativeSecond = unchecked((ushort)second);
                var dateTime = new DateTime(nativeYear, nativeMonth, nativeDay,
                    nativeHour, nativeMinute, nativeSecond,
                    DateTimeKind.Unspecified);
                return (((dateTime.DayOfYear - 1) * 24 + nativeHour) * 60
                    + nativeMinute) * 60 + nativeSecond;
            }
            catch (ArgumentOutOfRangeException)
            {
                return 0;
            }
        }

        private static string TakeNativePart(ref string value,
            params char[] delimiters)
        {
            int delimiter = value.IndexOfAny(delimiters);
            if (delimiter < 0)
            {
                string result = value;
                value = string.Empty;
                return result;
            }

            string part = value[..delimiter];
            value = value[(delimiter + 1)..];
            return part;
        }

        private static string TrimNativeWhitespace(string value)
        {
            int start = 0;
            int end = value.Length;
            while (start < end && value[start] <= 0x20)
                start++;
            while (end > start && value[end - 1] <= 0x20)
                end--;
            return value[start..end];
        }

        // sub_403DCC accepts decimal and Delphi-style hexadecimal integers.
        private static int ParseNativePart(string value, int defaultValue)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            int nul = value.IndexOf('\0');
            if (nul >= 0)
                value = value[..nul];

            int index = 0;
            while (index < value.Length && value[index] == ' ')
                index++;

            bool negative = false;
            if (index < value.Length && (value[index] == '+' || value[index] == '-'))
            {
                negative = value[index] == '-';
                index++;
            }

            int radix = 10;
            if (index < value.Length && value[index] == '$')
            {
                radix = 16;
                index++;
            }
            else if (index < value.Length && (value[index] == 'x' || value[index] == 'X'))
            {
                radix = 16;
                index++;
            }
            else if (index + 1 < value.Length && value[index] == '0' &&
                     (value[index + 1] == 'x' || value[index + 1] == 'X'))
            {
                radix = 16;
                index += 2;
            }

            uint maximumBeforeNext = radix == 16 ? 0x0FFFFFFFu : 0x0CCCCCCCu;
            uint result = 0;
            int digitCount = 0;
            while (index < value.Length)
            {
                int digit = NativeDigitValue(value[index]);
                if (digit < 0 || digit >= radix || result > maximumBeforeNext)
                    return defaultValue;
                result = unchecked(result * (uint)radix + (uint)digit);
                digitCount++;
                index++;
            }

            if (digitCount == 0)
                return defaultValue;
            int parsed = unchecked((int)result);
            if (!negative)
                return parsed;
            int negated = unchecked(-parsed);
            return negated > 0 ? defaultValue : negated;
        }

        private static int NativeDigitValue(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'A' && value <= 'F')
                return value - 'A' + 10;
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            return -1;
        }

        public readonly struct FeastDayEntry
        {
            public FeastDayEntry(string fileName, int start, int end)
            {
                byte[] source = HUtil32.GbkEncoding.GetBytes(fileName ?? string.Empty);
                int length = Math.Min(source.Length, FileNameMaximumGbkBytes);
                NativeFileNameBytes = source.AsSpan(0, length).ToArray();
                FileName = HUtil32.GbkEncoding.GetString(NativeFileNameBytes);
                Start = start;
                End = end;
            }

            public string FileName { get; }
            public byte[] NativeFileNameBytes { get; }
            public int Start { get; }
            public int End { get; }
        }

        private sealed class FeastDaysIni : IniFile
        {
            public FeastDaysIni(string fileName) : base(fileName)
            {
                try
                {
                    Load();
                }
                catch (Exception ex) when (ConfigCount == 0 &&
                                           ex.Message == $"配置文件[{fileName}]不存在或配置文件内容为空。")
                {
                    // Delphi TIniFile treats an existing blank file as an empty source.
                }
            }

            public new string ReadString(string section, string key, string defValue) =>
                base.ReadString(section, key, defValue);

        }
    }
}
