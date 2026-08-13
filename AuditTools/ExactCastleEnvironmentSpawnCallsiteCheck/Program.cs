using System.Text.RegularExpressions;

var repoRoot = AuditRepoRoot.Resolve(args);

var userCastle = Compact(Read("GameSvr/Castle/UserCastle.cs"));
Assert(userCastle.Contains(
        "m_MapCastle = M2Share.MapManager.FindMap(m_sMapName)",
        StringComparison.Ordinal)
    && userCastle.Contains("if (m_MapCastle != null)",
        StringComparison.Ordinal),
    "UserCastle no longer resolves and validates its physical castle environment");

var userCastleSites = new[]
{
    "RegenMonsterByName(m_MapCastle, m_MainDoor.nX, m_MainDoor.nY, m_MainDoor.sName)",
    "RegenMonsterByName(m_MapCastle, m_LeftWall.nX, m_LeftWall.nY, m_LeftWall.sName)",
    "RegenMonsterByName(m_MapCastle, m_CenterWall.nX, m_CenterWall.nY, m_CenterWall.sName)",
    "RegenMonsterByName(m_MapCastle, m_RightWall.nX, m_RightWall.nY, m_RightWall.sName)",
    "RegenMonsterByName(m_MapCastle, ObjUnit.nX, ObjUnit.nY, ObjUnit.sName)"
};
foreach (var site in userCastleSites)
{
    Assert(userCastle.Contains(site, StringComparison.Ordinal),
        $"castle initialization lost exact-environment spawn: {site}");
}
Assert(Count(userCastle,
        "RegenMonsterByName(m_MapCastle, ObjUnit.nX, ObjUnit.nY, ObjUnit.sName)") == 2,
    "castle initialization must spawn both archer and guard arrays into m_MapCastle");
Assert(!userCastle.Contains("RegenMonsterByName(m_sMapName,",
        StringComparison.Ordinal),
    "castle initialization regressed to a map-name lookup after resolving m_MapCastle");

var castleOfficial = Compact(Read("GameSvr/Npcs/CastleOfficial.cs"));
Assert(Count(castleOfficial,
        "RegenMonsterByName(this.m_Castle.m_MapCastle, ObjUnit.nX, ObjUnit.nY, ObjUnit.sName)") == 2,
    "CastleOfficial guard and archer hiring must use the castle's physical environment");
Assert(!castleOfficial.Contains(
        "RegenMonsterByName(this.m_Castle.m_sMapName,",
        StringComparison.Ordinal),
    "CastleOfficial hiring regressed to map-name lookup");

var pas = Read("GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs");
var hireGuard = Compact(Slice(pas, "case \"click_hireguard\":",
    "case \"click_hirearcher\":"));
Assert(hireGuard.Contains("castle.m_MapCastle == null",
        StringComparison.Ordinal)
    && hireGuard.Contains(
        "RegenMonsterByName(castle.m_MapCastle, guard.nX, guard.nY, guard.sName)",
        StringComparison.Ordinal),
    "PAS Click_HireGuard must validate and use the physical castle environment");
Assert(!hireGuard.Contains("castle.m_MapCastle.sMapName",
        StringComparison.Ordinal),
    "PAS Click_HireGuard regressed to map-name lookup");

var hireArcher = Compact(Slice(pas, "case \"click_hirearcher\":",
    "case \"reqcastlewar\":"));
Assert(hireArcher.Contains("castle.m_MapCastle == null",
        StringComparison.Ordinal)
    && hireArcher.Contains(
        "RegenMonsterByName(castle.m_MapCastle, archer.nX, archer.nY, archer.sName)",
        StringComparison.Ordinal),
    "PAS Click_HireArcher must validate and use the physical castle environment");
Assert(!hireArcher.Contains("castle.m_MapCastle.sMapName",
        StringComparison.Ordinal),
    "PAS Click_HireArcher regressed to map-name lookup");

var createFamePlayerMon = Compact(Slice(pas,
    "case \"createfameplayermon\":", "case \"clearmon\":"));
Assert(createFamePlayerMon.Contains(
        "RegenMonsterByName(mapName, x, y, monName)",
        StringComparison.Ordinal),
    "explicit CreateFamePlayerMon map-name selection was changed");

var createCampMon = Compact(Slice(pas, "case \"createcampmon\":",
    "case \"setmontargetxy\":"));
Assert(createCampMon.Contains(
        "RegenMonsterByName(mapName, sx, sy, monName)",
        StringComparison.Ordinal),
    "explicit CreateCampMon map-name selection was changed");

Console.WriteLine(
    "ExactCastleEnvironmentSpawnCallsiteCheck PASS exact=10 explicit-map-name=2");
return;

string Read(string relativePath)
{
    var path = Path.Combine(repoRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(path)) throw new FileNotFoundException(path);
    return File.ReadAllText(path);
}

static int Count(string text, string needle)
{
    var count = 0;
    for (var start = 0; ; )
    {
        var index = text.IndexOf(needle, start, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        start = index + needle.Length;
    }
}

static string Compact(string text) => Regex.Replace(text, @"\s+", " ");

static string Slice(string text, string startNeedle, string endNeedle)
{
    var start = text.IndexOf(startNeedle, StringComparison.Ordinal);
    if (start < 0) return string.Empty;
    var end = text.IndexOf(endNeedle, start + startNeedle.Length,
        StringComparison.Ordinal);
    return end < 0 ? text[start..] : text[start..end];
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
