using System.Xml.Linq;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
var originalRoot = M2Share.sRootPath;
InitializeRuntime();
var testRoot = Path.Combine(Path.GetTempPath(),
    "MaxStrengthenEquipLvCompatCheck", Guid.NewGuid().ToString("N"));

try
{
    var validRoot = CreateConfigRoot(testRoot, "valid",
        (1, 0), (2, 36), (3, 42), (4, 47), (5, 55));
    M2Share.sRootPath = validRoot;

    var decoy = new TPlayObject();
    decoy.m_Abil.Level = ushort.MaxValue;
    var player = new TPlayObject();
    var bridge = new PasApiBridge
    {
        CurrentNpc = new NormNpc(),
        CurrentPlayer = decoy
    };
    var playerArg = new List<PasValue> { PasValue.FromObject(player) };

    Assert(!bridge.CallNpcMethod("MaxStrengthenEquipLv", playerArg,
            out var methodResult)
           && methodResult.Type == PasValueType.Nil,
        "MaxStrengthenEquipLv procedure shadow is still open");

    foreach (var invalid in new[]
             {
                 new List<PasValue>(),
                 new List<PasValue> { PasValue.FromInt(0) },
                 new List<PasValue> { PasValue.FromObject(new NormNpc()) },
                 new List<PasValue>
                 {
                     PasValue.FromObject(player), PasValue.FromInt(0)
                 }
             })
    {
        Assert(!bridge.CallNpcFunc("MaxStrengthenEquipLv", invalid,
                out var invalidResult)
               && invalidResult.Type == PasValueType.Nil,
            "MaxStrengthenEquipLv accepted an invalid function ABI");
    }

    foreach (var sample in new[]
             {
                 (Level: 0, Expected: 1), (Level: 35, Expected: 1),
                 (Level: 36, Expected: 2), (Level: 41, Expected: 2),
                 (Level: 42, Expected: 3), (Level: 46, Expected: 3),
                 (Level: 47, Expected: 4), (Level: 54, Expected: 4),
                 (Level: 55, Expected: 5),
                 (Level: (int)ushort.MaxValue, Expected: 5)
             })
    {
        player.m_Abil.Level = (ushort)sample.Level;
        Assert(bridge.CallNpcFunc("MaxStrengthenEquipLv", playerArg,
                out var value),
            "MaxStrengthenEquipLv rejected a valid player/configuration");
        Equal(sample.Expected, value.AsInt(),
            $"level {sample.Level} threshold result");
    }

    var unorderedRoot = CreateConfigRoot(testRoot, "unordered",
        (1, 0), (2, 50), (3, 20));
    M2Share.sRootPath = unorderedRoot;
    player.m_Abil.Level = 30;
    Assert(bridge.CallNpcFunc("MaxStrengthenEquipLv", playerArg,
            out var unorderedResult),
        "unordered LimitLv table was rejected");
    Equal(3, unorderedResult.AsInt(),
        "native loop stopped at the first unmet threshold");

    M2Share.sRootPath = Path.Combine(testRoot, "missing");
    Assert(!bridge.CallNpcFunc("MaxStrengthenEquipLv", playerArg,
            out var missingResult)
           && missingResult.Type == PasValueType.Nil,
        "missing StrengthenEquip.xml did not fail closed");

    var invalidRoot = CreateConfigRoot(testRoot, "invalid-level",
        (1, 0), (3, 36));
    M2Share.sRootPath = invalidRoot;
    Assert(!bridge.CallNpcFunc("MaxStrengthenEquipLv", playerArg,
            out var invalidConfigResult)
           && invalidConfigResult.Type == PasValueType.Nil,
        "non-contiguous EquipLevel table did not fail closed");

    var dtdRoot = Path.Combine(testRoot, "dtd");
    var dtdFile = GetConfigFile(dtdRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(dtdFile)!);
    File.WriteAllText(dtdFile,
        "<!DOCTYPE x [<!ENTITY e '0'>]><Describle><Info>"
        + "<EquipLevel Level='1' LimitLv='&e;'/></Info></Describle>");
    M2Share.sRootPath = dtdRoot;
    Assert(!bridge.CallNpcFunc("MaxStrengthenEquipLv", playerArg,
            out var dtdResult)
           && dtdResult.Type == PasValueType.Nil,
        "DTD-enabled StrengthenEquip.xml did not fail closed");

    Console.WriteLine(
        "MaxStrengthenEquipLvCompatCheck PASS abi=npc-function-player "
        + "field=player-level thresholds=0/36/42/47/55 config=fail-closed");
}
finally
{
    M2Share.sRootPath = originalRoot;
    var fullTestRoot = Path.GetFullPath(testRoot);
    var fullTempRoot = Path.GetFullPath(Path.GetTempPath());
    if (fullTestRoot.StartsWith(fullTempRoot,
            StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(fullTestRoot))
        Directory.Delete(fullTestRoot, true);
}

static string CreateConfigRoot(string testRoot, string name,
    params (int Level, int Limit)[] entries)
{
    var root = Path.Combine(testRoot, name);
    var fileName = GetConfigFile(root);
    Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
    var document = new XDocument(new XElement("Describle",
        new XElement("Info", entries.Select(entry =>
            new XElement("EquipLevel",
                new XAttribute("Level", entry.Level),
                new XAttribute("MinePrice", 0),
                new XAttribute("LimitLv", entry.Limit))))));
    document.Save(fileName);
    return root;
}

static string GetConfigFile(string root) => Path.Combine(root, "Share",
    "EngineConfig", "\u88c5\u5907\u5408\u6210\u7ba1\u7406",
    "StrengthenEquip.xml");

static void PrepareRuntimeConfig()
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "Share"));
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void InitializeRuntime()
{
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
