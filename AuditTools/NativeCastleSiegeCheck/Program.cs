using System.Text;

namespace NativeCastleSiegeCheck
{
    internal static class Program
    {
        private static int _assertions;
        private static readonly List<string> Failures = new();

        private static void True(bool condition, string what)
        {
            _assertions++;
            if (!condition) Failures.Add(what);
        }

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            PrepareRuntimeConfig();
            var root = AuditRepoRoot.Resolve();
            var conf = File.ReadAllText(Path.Combine(root, "GameSvr", "Castle", "CastleConfManager.cs"));
            var castle = File.ReadAllText(Path.Combine(root, "GameSvr", "Castle", "UserCastle.cs"));
            var siegeClock = File.ReadAllText(Path.Combine(root, "GameSvr", "Plugins",
                "YanshenPangu2Patches.cs"));
            var liveConf = CodeOnly(conf);
            var liveCastle = CodeOnly(castle);
            var liveSiegeClock = CodeOnly(siegeClock);

            True(liveConf.Contains("沙巴克.txt", StringComparison.Ordinal),
                "state file must be 沙巴克.txt (0x65A8F4 / 0x65B074), not SabukW.txt");
            True(liveConf.Contains("沙巴克基础配置.txt", StringComparison.Ordinal),
                "layout file must be 沙巴克基础配置.txt (0x65B088)");
            True(!liveCastle.Contains("SabukW.txt", StringComparison.Ordinal)
                 && !liveConf.Contains("SabukW.txt", StringComparison.Ordinal),
                "SabukW.txt is 0 hits in the native image");
            True(liveConf.Contains("\"changeDate\"", StringComparison.Ordinal),
                "runtime key is changeDate (0x65A92C), not ChangeDate");
            True(liveConf.Contains("\"WineCount\"", StringComparison.Ordinal),
                "WineCount @0x65A98C must persist (byte [castle+4])");
            True(liveConf.Contains("\"Archer_\" + i + \"_HP\"", StringComparison.Ordinal),
                "Archer HP keys must be 0-indexed (0x65A711 eax=ebx; 0x65A772 cmp ebx,0xC)");
            True(!liveConf.Contains("Archer_\" + (i + 1)", StringComparison.Ordinal),
                "Archer_1_HP (1-based) is not native; production is Archer_0_HP");
            True(liveConf.Contains("yyyy/M/d", StringComparison.Ordinal),
                "Delphi TIniFile date shape is yyyy/M/d (production changeDate=2022/6/15)");
            True(!liveConf.Contains("ToString(\"O\")", StringComparison.Ordinal),
                "ISO-8601 round-trip format is not the native INI date");
            True(liveCastle.Contains("m_btWineCount = 0x14", StringComparison.Ordinal),
                "day-roll 0x65BBC3 writes WineCount=20");
            True(liveCastle.Contains("AddDays(3.0)", StringComparison.Ordinal),
                "AddAttacker 0x65B68B fadd dword [0x65B6DC] = 3.0 TDateTime days");
            True(!liveCastle.Contains(
                    "AddDateTimeOfDay(DateTime.Now, M2Share.g_Config.nStartCastleWarDays)",
                    StringComparison.Ordinal),
                "nStartCastleWarDays (default 4) is 0 hits in the native image");
            True(liveCastle.Contains("TryGetSiegeDayClock", StringComparison.Ordinal)
                 && liveSiegeClock.Contains("StockSiegeCaptureSec = 0x11B98",
                     StringComparison.Ordinal),
                "palace capture unlock must consume the clock-of-day 0x11B98=20:10 provider, not 10min-from-start");
            True(liveCastle.Contains("TryCapturePalaceFromRun", StringComparison.Ordinal),
                "occupancy 0x65C690 must run from Castle.Run, before the 10s gate");
            True(liveCastle.Contains("m_MainDoor.BaseObject.m_boGhost", StringComparison.Ordinal),
                "Run must drop a ghosted main door (0x65BD5A cmp [eax+0x73])");
            True(castle.Contains("notifyServerGroup: true", StringComparison.Ordinal)
                 || liveCastle.Contains("notifyServerGroup: true", StringComparison.Ordinal),
                "occupancy GetCastle passes cl=1 so SS_211 is sent inside GetCastle (0x65C76F)");

            var addAt = castle.IndexOf("public bool AddAttackerInfo(Association Guild)",
                StringComparison.Ordinal);
            True(addAt >= 0, "AddAttackerInfo must exist");
            if (addAt >= 0)
            {
                var addBody = castle.Substring(addAt, Math.Min(900, castle.Length - addAt));
                var liveAdd = CodeOnly(addBody);
                True(liveAdd.Contains("SendServerGroupMsg(Grobal2.SS_212", StringComparison.Ordinal),
                    "duplicate signup still sends SS_212 (0x65B6C9 is after the already-listed jne)");
                True(liveAdd.Contains("M2Share.nServerIndex, guildName", StringComparison.Ordinal)
                     && liveAdd.Contains("Guild?.sGuildName", StringComparison.Ordinal),
                    "SS_212 signup body must be [Guild+0x10] guild name (0x65B6BA..0x65B6CD)");
            }

            True(GameSvr.CastleConfManager.FormatDelphiDate(new DateTime(2022, 6, 15))
                 == "2022/6/15",
                "changeDate write must match production 2022/6/15");
            True(GameSvr.CastleConfManager.FormatDelphiDateTime(
                     new DateTime(2026, 8, 1, 0, 0, 6))
                 == "2026/8/1 0:00:06",
                "IncomeToday write must match production 2026/8/1 0:00:06");

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeCastleSiegeCheck {_assertions} assertions "
                    + "persist=沙巴克.txt/0-index/changeDate/WineCount "
                    + "signup=+3.0days capture=20:10 Run-occupancy");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeCastleSiegeCheck");
            foreach (var failure in Failures) Console.WriteLine("  - " + failure);
            return 1;
        }

        private static string CodeOnly(string text)
        {
            return string.Join('\n', text.Split('\n')
                .Select(l => l.TrimStart())
                .Where(l => !l.StartsWith("//") && !l.StartsWith("*")
                            && !l.StartsWith("/*")));
        }

        private static void PrepareRuntimeConfig()
        {
            var directory = AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
            File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
            File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
            var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
            Directory.CreateDirectory(share);
            File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
                "[PlayerLevelExp]");
            File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
        }
    }
}
