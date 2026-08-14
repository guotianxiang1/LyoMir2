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
    /// Up to 101 entries (cmp [ebp-8], 0x65 @0x00655C5D); Start==0 rows are skipped
    /// (fcomp @0x00655BA6 / je @0x00655BAF).
    /// </summary>
    public sealed class NativeFestivalConfig
    {
        public const int LoaderAddress = 0x00655AD0;
        public const int MaximumEntries = 101;
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
                    if (!ini.HasSection(section))
                        break;

                    var feastFile = ini.ReadString(section, "FileName", string.Empty);
                    if (string.IsNullOrWhiteSpace(feastFile))
                        continue;

                    var start = ini.ReadFloat(section, "Start", 0d);
                    if (Math.Abs(start) < double.Epsilon)
                        continue;

                    var end = ini.ReadFloat(section, "End", 0d);
                    entries.Add(new FeastDayEntry(feastFile.Trim(), start, end));
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
            var configDir = Path.Combine(rootPath, baseDirectory, "config", "FeastDays.ini");
            if (File.Exists(configDir))
                return configDir;
            return Path.Combine(rootPath, baseDirectory, "FeastDays.ini");
        }

        public readonly struct FeastDayEntry
        {
            public FeastDayEntry(string fileName, double start, double end)
            {
                FileName = fileName ?? string.Empty;
                Start = start;
                End = end;
            }

            public string FileName { get; }
            public double Start { get; }
            public double End { get; }
        }

        private sealed class FeastDaysIni : IniFile
        {
            public FeastDaysIni(string fileName) : base(fileName) { }

            public bool HasSection(string section) => ContainSectionName(section);

            public new string ReadString(string section, string key, string defValue) =>
                base.ReadString(section, key, defValue);

            public new double ReadFloat(string section, string key, double defValue) =>
                base.ReadFloat(section, key, defValue);
        }
    }
}
