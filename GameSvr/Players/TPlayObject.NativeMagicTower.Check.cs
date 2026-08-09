using System.Globalization;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeMagicTowerChallengeDialogPrefix =
            "这次你能获得怪物给你带来的：<";

        // Original transient challenge descriptor at TPlayer +D10.
        internal string m_sNativeMagicTowerChallengeMonsters = string.Empty;

        internal void CheckNativeMagicTowerMonAndItem(NormNpc npc)
        {
            CheckNativeMagicTowerMonAndItem(npc,
                NextNativeMagicTowerChallengeRandom);
        }

        internal void CheckNativeMagicTowerMonAndItem(NormNpc npc,
            Func<int, int> random)
        {
            random ??= _ => 0;
            if (m_btNativeMagicTowerPhase == 2)
                SelectNativeMagicTowerChallenge(random);

            var dialog = NativeMagicTowerChallengeDialogPrefix +
                         (m_sNativeMagicTowerPrimaryPrize ?? string.Empty) +
                         "/c=red>";
            if (m_boNativeMagicTowerHundredth)
            {
                dialog += "\\并且你还将获得你的隐藏宝物：<" +
                          (m_sNativeMagicTowerPersonalPrize ?? string.Empty) +
                          "/c=red>";
            }
            if (m_btNativeMagicTowerSpecialRoute > 0)
            {
                dialog += "\\同时你还将获得服务器的隐藏宝物：<" +
                          (m_sNativeMagicTowerServerPrize ?? string.Empty) +
                          "/c=red>";
            }

            dialog += "\\当然你必须消灭里面所有的：<" +
                      (m_sNativeMagicTowerChallengeMonsters ?? string.Empty) +
                      "/c=red>" +
                      "\\如果您觉得本关难度太高，或对宝藏不满意，" +
                      "\\给我一张灵符，我就直接送您去下一关\\ \\" +
                      "|{cmd}<使用灵符进入下一关/@JinRuTong>         " +
                      "|<接受挑战/@recmon> ";

            m_NPC = npc;
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc?.m_sCharName ?? string.Empty) + "/" + dialog);
        }

        private void SelectNativeMagicTowerChallenge(Func<int, int> random)
        {
            var catalog = NativeMagicTowerChallengeCatalog.Capture();
            var tier = GetNativeMagicTowerChallengeTier(m_Abil.Level);
            if (m_btJob >= 4 || tier < 0) return;

            var monsters = catalog.Monsters[m_btJob, tier];
            var prizes = catalog.Prizes[m_btJob, tier];

            // Native order: monster index, ordinary roll, server roll,
            // personal roll. Hidden rolls remain after a failed ordinary roll.
            var monsterIndex = random(monsters.Count);
            var ordinaryRoll = random(100);
            if (TrySelectNativeMagicTowerThreshold(prizes, ordinaryRoll,
                    out var primaryPrize) &&
                (uint)monsterIndex < (uint)monsters.Count)
            {
                m_sNativeMagicTowerChallengeMonsters =
                    monsters[monsterIndex];
                m_sNativeMagicTowerPrimaryPrize = primaryPrize;
                m_btNativeMagicTowerPhase = 3;
            }

            var route = m_btNativeMagicTowerSpecialRoute;
            if (route is >= 1 and <= 5)
            {
                if (TrySelectNativeMagicTowerThreshold(
                        catalog.ServerPrizes[route - 1], random(100),
                        out var serverPrize))
                    m_sNativeMagicTowerServerPrize = serverPrize;
            }

            if (m_boNativeMagicTowerHundredth &&
                TrySelectNativeMagicTowerThreshold(catalog.PersonalPrizes,
                    random(100), out var personalPrize))
                m_sNativeMagicTowerPersonalPrize = personalPrize;
        }

        private static int NextNativeMagicTowerChallengeRandom(int range)
        {
            var random = M2Share.RandomNumber ?? RandomNumber.GetInstance();
            if (range > 0) return random.Random(range);

            // Delphi Random(0) still advances RandSeed and returns zero.
            _ = random.Random();
            return 0;
        }

        internal static int GetNativeMagicTowerChallengeTier(ushort level)
        {
            if (level <= 21) return 0;
            if (level <= 29) return 1;
            if (level <= 34) return 2;
            if (level <= 39) return 3;
            return 4;
        }

        internal static void InitializeNativeMagicTowerChallengeCatalog(
            string rootPath)
        {
            NativeMagicTowerChallengeCatalog.Initialize(rootPath);
        }

        private static bool TrySelectNativeMagicTowerThreshold(
            IReadOnlyList<NativeMagicTowerThresholdEntry> entries, int roll,
            out string descriptor)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (roll > entries[index].Threshold) continue;
                descriptor = entries[index].Descriptor;
                return true;
            }
            descriptor = string.Empty;
            return false;
        }

        private readonly record struct NativeMagicTowerThresholdEntry(
            string Descriptor, int Threshold);

        private sealed class NativeMagicTowerChallengeCatalog
        {
            private static readonly object SyncRoot = new();
            private static NativeMagicTowerChallengeCatalog s_Catalog;

            private NativeMagicTowerChallengeCatalog()
            {
                for (var job = 0; job < 4; job++)
                for (var tier = 0; tier < 5; tier++)
                {
                    Monsters[job, tier] = new List<string>();
                    Prizes[job, tier] =
                        new List<NativeMagicTowerThresholdEntry>();
                }
                for (var route = 0; route < 5; route++)
                    ServerPrizes[route] =
                        new List<NativeMagicTowerThresholdEntry>();
            }

            internal List<string>[,] Monsters { get; } =
                new List<string>[4, 5];
            internal List<NativeMagicTowerThresholdEntry>[,] Prizes { get; } =
                new List<NativeMagicTowerThresholdEntry>[4, 5];
            internal List<NativeMagicTowerThresholdEntry>[] ServerPrizes
                { get; } = new List<NativeMagicTowerThresholdEntry>[5];
            internal List<NativeMagicTowerThresholdEntry> PersonalPrizes
                { get; } = new();

            internal static void Initialize(string rootPath)
            {
                lock (SyncRoot)
                    s_Catalog = Load(rootPath);
            }

            internal static NativeMagicTowerChallengeCatalog Capture()
            {
                lock (SyncRoot)
                {
                    s_Catalog ??= Load(M2Share.sRootPath);
                    return s_Catalog;
                }
            }

            private static NativeMagicTowerChallengeCatalog Load(
                string rootPath)
            {
                var result = new NativeMagicTowerChallengeCatalog();
                try
                {
                    var sharePath = Path.GetFullPath(Path.Combine(
                        rootPath, "Share"));
                    var classFiles = new[]
                    {
                        "warr.ini", "fashi.ini", "taos.ini", "assassin.ini"
                    };
                    for (var job = 0; job < classFiles.Length; job++)
                    {
                        var path = Path.Combine(sharePath, classFiles[job]);
                        if (!File.Exists(path)) continue;
                        var ini = new NativeMagicTowerChallengeIni(path);
                        for (var tier = 0; tier < 5; tier++)
                        {
                            var section = "配置" + (tier + 1);
                            for (var index = 0; index < 100; index++)
                            {
                                var descriptor = ini.Value(section,
                                    "怪物" + index);
                                if (!string.IsNullOrEmpty(descriptor))
                                    result.Monsters[job, tier].Add(
                                        descriptor);
                            }
                            ReadThresholds(ini, section,
                                result.Prizes[job, tier]);
                        }
                    }

                    var bigItemPath = Path.Combine(sharePath, "bigitem.ini");
                    if (File.Exists(bigItemPath))
                    {
                        var ini = new NativeMagicTowerChallengeIni(
                            bigItemPath);
                        for (var route = 0; route < 5; route++)
                            ReadThresholds(ini, "配置" + (route + 1),
                                result.ServerPrizes[route]);
                    }

                    var personalPath = Path.Combine(sharePath,
                        "self100.ini");
                    if (File.Exists(personalPath))
                        ReadThresholds(new NativeMagicTowerChallengeIni(
                                personalPath), "配置",
                            result.PersonalPrizes);
                    return result;
                }
                catch (Exception e)
                {
                    LogLoadException(e);
                    return new NativeMagicTowerChallengeCatalog();
                }
            }

            private static void LogLoadException(Exception exception)
            {
                try
                {
                    M2Share.ErrorMessage(
                        "[Exception]: TSkyQuest.LoadPrize: " +
                        exception.Message);
                }
                catch
                {
                }
            }

            private static void ReadThresholds(
                NativeMagicTowerChallengeIni ini, string section,
                List<NativeMagicTowerThresholdEntry> target)
            {
                for (var index = 1; index <= 100; index++)
                {
                    var value = ini.Value(section, "爆物" + index);
                    if (string.IsNullOrEmpty(value)) continue;
                    var separator = value.IndexOf('/');
                    if (separator <= 0) continue;
                    var descriptor = value[..separator];
                    var thresholdText = value[(separator + 1)..];
                    var threshold = int.TryParse(thresholdText,
                        NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var parsed) ? parsed : 0;
                    target.Add(new NativeMagicTowerThresholdEntry(
                        descriptor, threshold));
                }
            }
        }

        private sealed class NativeMagicTowerChallengeIni : IniFile
        {
            internal NativeMagicTowerChallengeIni(string path) : base(path)
            {
                Load();
            }

            internal string Value(string section, string key)
            {
                return ReadString(section, key, string.Empty);
            }
        }
    }
}
