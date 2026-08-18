using System.Text.RegularExpressions;

var repoRoot = AuditRepoRoot.Resolve(args);

var exactSites = new[]
{
    Site("GameSvr/Actors/TBaseObject.cs",
        "spawnEnvironment, nX, nY, sMonName)",
        "spawnEnvironment.sMapName"),
    Site("GameSvr/Monsters/Monster.cs",
        "RegenMonsterByName(m_PEnvir, m_nCurrX, m_nCurrY, sMonName)",
        "RegenMonsterByName(m_PEnvir.sMapName, m_nCurrX, m_nCurrY, sMonName)"),
    Site("GameSvr/Monsters/Monster/BeeQueen.cs",
        "RegenMonsterByName(m_PEnvir, m_nCurrX, m_nCurrY, M2Share.g_Config.sBee)",
        "RegenMonsterByName(m_PEnvir.sMapName, m_nCurrX, m_nCurrY, M2Share.g_Config.sBee)"),
    Site("GameSvr/Monsters/Monster/BoneKingMonster.cs",
        "RegenMonsterByName(m_PEnvir, n10, n14,",
        "RegenMonsterByName(m_sMapName, n10, n14,"),
    Site("GameSvr/Monsters/Monster/ScultureKingMonster.cs",
        "RegenMonsterByName(m_PEnvir, nX, nY,",
        "RegenMonsterByName(m_sMapName, nX, nY,"),
    Site("GameSvr/Monsters/Monster/SpiderHouseMonster.cs",
        "RegenMonsterByName(m_PEnvir, n08, n0C, M2Share.g_Config.sSpider)",
        "RegenMonsterByName(m_PEnvir.sMapName, n08, n0C, M2Share.g_Config.sSpider)"),
    Site("GameSvr/Plugins/YanshenApi.cs",
        "RegenMonsterByName(_player.m_PEnvir, _player.m_nCurrX, _player.m_nCurrY, monName)",
        "RegenMonsterByName(_player.m_sMapName, _player.m_nCurrX, _player.m_nCurrY, monName)"),
    Site("GameSvr/Command/Commands/MobCommand.cs",
        "RegenMonsterByName(PlayObject.m_PEnvir, nX, nY, sMonName)",
        "RegenMonsterByName(PlayObject.m_PEnvir.sMapName, nX, nY, sMonName)"),
    Site("GameSvr/Command/Commands/RecallMobCommand.cs",
        "RegenMonsterByName(PlayObject.m_PEnvir, n10, n14, sMonName)",
        "RegenMonsterByName(PlayObject.m_PEnvir.sMapName, n10, n14, sMonName)"),
    Site("GameSvr/Command/Commands/ReCallMobExCommand.cs",
        "RegenMonsterByName(PlayObject.m_PEnvir, nX, nY, sMonName)",
        "RegenMonsterByName(PlayObject.m_PEnvir.sMapName, nX, nY, sMonName)")
};

foreach (var site in exactSites)
{
    // d5198c6b deleted the 63 traditional-GOM command files, three of which are
    // listed above. A deleted callsite cannot violate the contract, but it must
    // not be able to hide either: assert the file really is gone and that the
    // map-name form it used to be checked for exists nowhere in GameSvr.
    if (!File.Exists(Path.Combine(repoRoot,
            site.Path.Replace('/', Path.DirectorySeparatorChar))))
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "GameSvr"), "*.cs",
                SearchOption.AllDirectories)
            .Where(f => Compact(File.ReadAllText(f))
                .Contains(site.Forbidden, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(repoRoot, f))
            .ToArray();
        Assert(offenders.Length == 0,
            $"{site.Path} was deleted but its map-name spawn form reappeared in: " +
            string.Join(", ", offenders));
        continue;
    }

    var source = Compact(Read(site.Path));
    Assert(source.Contains(site.Required, StringComparison.Ordinal),
        $"actor-local spawn no longer uses its physical environment: {site.Path}");
    Assert(!source.Contains(site.Forbidden, StringComparison.Ordinal),
        $"actor-local spawn regressed to map-name lookup: {site.Path}");
}

var fromHeroTrueSites = Directory
    .EnumerateFiles(Path.Combine(repoRoot, "GameSvr"), "*.cs",
        SearchOption.AllDirectories)
    .Where(path => Compact(File.ReadAllText(path)).Contains(
        "fromHero: true", StringComparison.Ordinal))
    .Select(path => Path.GetRelativePath(repoRoot, path)
        .Replace('\\', '/'))
    .ToArray();
Assert(fromHeroTrueSites.Length == 1 &&
       fromHeroTrueSites[0] == "GameSvr/Actors/HeroObject.NativeDoSpell.cs",
    "BoFromHero=true must be wired only to the shared hero-spell summon call: " +
    string.Join(", ", fromHeroTrueSites));

var pas = Read("GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs");
var makeSlaveEx = Compact(Slice(pas, "case \"makeslaveex\":",
    "// === SIGN-IN / DAILY REWARDS (return value) ==="));
Assert(makeSlaveEx.Contains(
        "CurrentPlayer.m_PEnvir, x, y, monName",
        StringComparison.Ordinal),
    "PAS MakeSlaveEx no longer spawns into the player's physical environment");
Assert(!makeSlaveEx.Contains("CurrentPlayer.m_sMapName", StringComparison.Ordinal),
    "PAS MakeSlaveEx regressed to map-name lookup");

var playerCreateMon = Compact(Slice(pas, "case \"createmon\":",
    "case \"getmember\":"));
Assert(playerCreateMon.Contains("? CurrentPlayer.m_PEnvir", StringComparison.Ordinal)
       && playerCreateMon.Contains(": M2Share.MapManager.FindMap(mapName)",
           StringComparison.Ordinal),
    "player CreateMon no longer separates blank-context and explicit-map semantics");

var npcMethodStart = pas.IndexOf("public bool CallNpcMethod", StringComparison.Ordinal);
Assert(npcMethodStart >= 0, "CallNpcMethod source region was not found");
var npcMethodCreateMon = Compact(Slice(pas[npcMethodStart..],
    "case \"createmon\":", "case \"createfameplayermon\":"));
Assert(npcMethodCreateMon.Contains("? CurrentNpc.m_PEnvir", StringComparison.Ordinal)
       && npcMethodCreateMon.Contains(": M2Share.MapManager.FindMap(mapName)",
           StringComparison.Ordinal),
    "NPC CreateMon no longer separates blank-context and explicit-map semantics");

var npcFuncStart = pas.IndexOf("public bool CallNpcFunc", StringComparison.Ordinal);
Assert(npcFuncStart >= 0, "CallNpcFunc source region was not found");
var npcFuncCreateMon = Compact(Slice(pas[npcFuncStart..],
    "case \"createmon\":", "case \"checkmapmonbyname\":"));
Assert(npcFuncCreateMon.Contains("? CurrentNpc.m_PEnvir", StringComparison.Ordinal)
       && npcFuncCreateMon.Contains(": M2Share.MapManager.FindMap(mapName)",
           StringComparison.Ordinal),
    "NPC function CreateMon no longer separates blank-context and explicit-map semantics");

var wantWarMon = Compact(Slice(pas, "case \"wantwarmon\":",
    "case \"getskyprize\":"));
Assert(wantWarMon.Contains(
        "warMonsterPlayer.WantNativeMagicTowerWarMon(CurrentNpc)",
        StringComparison.Ordinal)
       && !wantWarMon.Contains("RegenMonsterByName", StringComparison.Ordinal),
    "WantWarMon lost its explicit-player native implementation or regressed " +
    "to the monster-name approximation");

var explicitNameSites = new[]
{
    Site("GameSvr/LocalDB.cs", "RegenMonsterByName(s20,", null),
    Site("GameSvr/Command/Commands/MobPlaceCommand.cs",
        "RegenMonsterByName(M2Share.g_sMissionMap,", null),
    Site("GameSvr/Plugins/YanshenApi.cs",
        "RegenMonsterByName(mapName, (short)spawnX, (short)spawnY, monName)", null)
};

foreach (var site in explicitNameSites)
{
    // MobPlaceCommand.cs went with the 63 traditional-GOM commands in
    // d5198c6b. This half of the contract only says a surviving explicit-map
    // spawn must go through the map name, so a removed file has nothing to
    // check - but assert it really is removed rather than moved.
    if (!File.Exists(Path.Combine(repoRoot,
            site.Path.Replace('/', Path.DirectorySeparatorChar))))
    {
        var moved = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "GameSvr"), "*.cs",
                SearchOption.AllDirectories)
            .Where(f => Compact(File.ReadAllText(f))
                .Contains(site.Required, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(repoRoot, f))
            .ToArray();
        Assert(moved.Length == 0,
            $"{site.Path} was deleted but its spawn form resurfaced in: " +
            string.Join(", ", moved));
        continue;
    }

    Assert(Compact(Read(site.Path)).Contains(site.Required,
            StringComparison.Ordinal),
        $"explicit configured-map spawn lost map-name lookup: {site.Path}");
}

var createFamePlayerMon = Compact(Slice(pas, "case \"createfameplayermon\":",
    "case \"clearmon\":"));
Assert(createFamePlayerMon.Contains(
        "RegenMonsterByName(mapName, x, y, monName)", StringComparison.Ordinal),
    "CreateFamePlayerMon lost its explicit map-name argument");

var createCampMon = Compact(Slice(pas, "case \"createcampmon\":",
    "case \"setmontargetxy\":"));
Assert(createCampMon.Contains(
        "RegenMonsterByName(mapName, sx, sy, monName)", StringComparison.Ordinal),
    "CreateCampMon changed before its separate native ownership contract was implemented");

Console.WriteLine(
    "ExactEnvironmentLocalSpawnCallsiteCheck PASS "
    + $"exact={exactSites.Length + 4} explicit-name={explicitNameSites.Length + 2} "
    + "native=WantWarMon-physical-environment+hero-summon-fromHero-only");
return;

static (string Path, string Required, string Forbidden) Site(string path,
    string required, string forbidden) => (path, required, forbidden);

string Read(string relativePath)
{
    var path = Path.Combine(repoRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(path)) throw new FileNotFoundException(path);
    return File.ReadAllText(path);
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
