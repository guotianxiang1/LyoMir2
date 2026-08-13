using GameSvr;

PrepareRuntimeConfig();

var repoRoot = AuditRepoRoot.Resolve(args);
var source = File.ReadAllText(Path.Combine(repoRoot, "GameSvr", "Castle", "UserCastle.cs"));
var getCastle = Slice(source, "public void GetCastle(Association Guild, bool notifyServerGroup = false)",
    "public void StartWallconquestWar()");
var loadAttackers = Slice(source, "private void LoadAttackSabukWall()",
    "private static bool TryParseAttackDate");
var attackDateParser = Slice(source, "private static bool TryParseAttackDate",
    "private bool SaveAttackSabukWall()");

var configSave = At(getCastle, "SaveConfigFile();");
var oldGuildBranch = At(getCastle, "if (oldGuild != null)");
var reverseScan = At(getCastle, "for (var i = m_AttackWarList.Count - 1; i >= 0; i--)");
var replaceGuild = At(getCastle, "attackerInfo.Guild = oldGuild;");
var replaceName = At(getCastle, "attackerInfo.sGuildName = oldGuild.sGuildName;");
var refreshOld = At(getCastle, "oldGuild.RefMemberName();");
var saveAttackers = At(getCastle, "SaveAttackSabukWall();");
var refreshNew = At(getCastle, "m_MasterGuild.RefMemberName();");

Assert(configSave < oldGuildBranch && oldGuildBranch < reverseScan,
    "GetCastle must save config before entering the old-owner path");
Assert(reverseScan < replaceGuild && replaceGuild < replaceName && replaceName < refreshOld,
    "GetCastle must reverse-scan and replace the attacker record before old-guild refresh");
Assert(refreshOld < saveAttackers && saveAttackers < refreshNew,
    "GetCastle must SAVE AttackSabukWall between old and new guild refreshes: "
    + "0x65BF80 calls 0x65A3B8, which walks [ebx+0x8C] and writes "
    + "'       \"'+YYYY-MM-DD+'\"\\r\\n' (0x65A4C8 / 0x65A4DC / 0x65A4F0). "
    + "The loader 0x65B22C has a single E8 xref from init 0x65AAD6");

// 战神 sub_65BEC0 has NO rollback on a failed save. 0x65A510 is a Delphi
// `procedure` (never sets eax) and the next instruction 0x65BF22 `test edi,edi`
// tests EDI = the OLD guild, loaded at 0x65BEF4 -- it is the `if oldGuild <> nil`
// guard, not a save-result check. Reverting the owner fields would hand the
// castle back on any transient disk error.
Assert(!getCastle.Contains("if (!SaveConfigFile())", StringComparison.Ordinal),
    "GetCastle must NOT branch on the save result -- SaveCastleInfo (0x65A510) "
    + "is a procedure with no return value");
Assert(!getCastle.Contains("m_MasterGuild = oldGuild;", StringComparison.Ordinal),
    "GetCastle must NOT roll the owner back; native has no restore path");
Assert(!getCastle.Contains("LoadAttackSabukWall();", StringComparison.Ordinal),
    "GetCastle must not RE-LOAD the attacker list -- that would discard the "
    + "old-guild reassignment that 0x65BF5E just wrote");
Assert(source.Contains("AttackDate.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture)",
        StringComparison.Ordinal),
    "AttackSabukWall must use the native YYYY-MM-DD date format");
Assert(At(loadAttackers, "if (loadList.Count < 1) return;")
       < At(loadAttackers, "m_AttackWarList.Clear();"),
    "an empty AttackSabukWall file must retain the existing attack list");
Assert(attackDateParser.Contains("value.Split('-', StringSplitOptions.RemoveEmptyEntries)",
        StringComparison.Ordinal)
    && attackDateParser.Contains("unchecked((ushort)ParseAttackDatePart(parts[0], 1999))",
        StringComparison.Ordinal),
    "attack dates must use the native three-segment and default-value parser");
Assert(loadAttackers.Contains("[Error] UserCastle.LoadAttackSabukWall",
        StringComparison.Ordinal),
    "attack list loading must retain the native exception boundary");

var unchanged = new DateTime(2026, 7, 25, 18, 19, 20, DateTimeKind.Local);
Equal(unchanged, M2Share.AddDateTimeOfDay(unchanged, 0),
    "zero-day input must be returned unchanged");
Equal(unchanged, M2Share.AddDateTimeOfDay(unchanged, -1),
    "negative-day input must be returned unchanged");
Equal(new DateTime(2026, 7, 25),
    M2Share.AddDateTimeOfDay(unchanged, 1),
    "one-day window must retain the source date and clear its time");
Equal(new DateTime(2026, 2, 2),
    M2Share.AddDateTimeOfDay(new DateTime(2026, 1, 30, 23, 59, 59), 4),
    "four-day castle window must count the source date inclusively");
Equal(new DateTime(2027, 1, 1),
    M2Share.AddDateTimeOfDay(new DateTime(2026, 12, 31), 2),
    "castle date must cross the year boundary");
Equal(new DateTime(2024, 3, 1),
    M2Share.AddDateTimeOfDay(new DateTime(2024, 2, 28), 2),
    "native common-year month table must ignore leap day");
Equal(new DateTime(2000, 1, 1),
    M2Share.AddDateTimeOfDay(new DateTime(99, 12, 31), 2),
    "native year 99 rollover must jump to 2000");

Console.WriteLine("CastleOwnerTransitionCheck PASS owner=swap=save attackList=native-save date=yyyy-MM-dd/add-day-exact");

static int At(string text, string needle)
{
    var index = text.IndexOf(needle, StringComparison.Ordinal);
    if (index < 0) throw new InvalidOperationException($"Missing: {needle}");
    return index;
}

static string Slice(string text, string startNeedle, string endNeedle)
{
    var start = At(text, startNeedle);
    var end = text.IndexOf(endNeedle, start + startNeedle.Length,
        StringComparison.Ordinal);
    if (end < 0) throw new InvalidOperationException($"Missing: {endNeedle}");
    return text[start..end];
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}
