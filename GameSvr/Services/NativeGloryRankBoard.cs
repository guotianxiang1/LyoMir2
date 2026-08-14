using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// Glory rank board config loader (sub_60EBBC @0x0060EBBC).
    /// Logs MainOutMessage "[荣耀榜]: 成功加载 %d 条信息" after counting entries.
    /// </summary>
    public sealed class NativeGloryRankBoard
    {
        private readonly List<Entry> _entries;

        private NativeGloryRankBoard(List<Entry> entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Count;
        public IReadOnlyList<Entry> Entries => _entries;

        public static bool TryLoad(string shareDirectory,
            out NativeGloryRankBoard board, out string error)
        {
            board = new NativeGloryRankBoard(new List<Entry>());
            error = string.Empty;
            var fileName = Path.Combine(Path.GetFullPath(shareDirectory),
                "Config", "GloryRank.ini");
            if (!File.Exists(fileName))
                return true;

            try
            {
                var ini = new GloryRankIni(fileName);
                var entries = new List<Entry>();
                for (var index = 1; index <= 1000; index++)
                {
                    var name = ini.ReadString("荣耀榜", "角色" + index, null);
                    if (string.IsNullOrWhiteSpace(name))
                        break;
                    var scoreText = ini.ReadString("荣耀榜", "成绩" + index, "0");
                    _ = int.TryParse(scoreText, out var score);
                    entries.Add(new Entry(name, score));
                }

                board = new NativeGloryRankBoard(entries);
                M2Share.MainOutMessage("[荣耀榜]: 成功加载 " + entries.Count +
                                       " 条信息");
                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        public sealed class Entry
        {
            public Entry(string name, int score)
            {
                Name = name;
                Score = score;
            }

            public string Name { get; }
            public int Score { get; }
        }

        private sealed class GloryRankIni : IniFile
        {
            internal GloryRankIni(string fileName) : base(fileName)
            {
                Load();
            }
        }
    }
}
