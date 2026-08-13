using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    PrepareRuntime();

    VerifyDefaultAndSlotReads();
    VerifyExplicitPlayerOwnershipAndNpcGate();
    VerifyBoundsAndAbiShadows();
    VerifySourceContract();

    Console.WriteLine(
        "PASS NativeMagicTowerIsExistArcherCheck abi=npc-function(player,index) " +
        "slots=1..10 player-owned byte-nonzero gate=npc-property-12 " +
        "bounds=unsigned-index-1 " +
        "state=transient-read-only player=dead+ghost-valid shadows=closed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeMagicTowerIsExistArcherCheck FAIL: {exception}");
    return 1;
}

static void VerifyDefaultAndSlotReads()
{
    var npc = new NormNpc { m_sCharName = "archer-slots" };
    npc.AddNativePasProperty(12);
    var player = new TPlayObject { m_sCharName = "explicit-player" };
    var bridge = NewBridge(npc, new TPlayObject
    {
        m_sCharName = "context-player"
    });
    var slots = ArcherSlots(player);

    Equal(10, slots.Length, "native archer slot count");
    for (var index = 1; index <= slots.Length; index++)
        AssertArcher(bridge, player, index, false, $"fresh slot {index}");

    slots[0] = 1;
    slots[4] = 0x80;
    slots[9] = byte.MaxValue;
    for (var index = 1; index <= slots.Length; index++)
    {
        var expected = index is 1 or 5 or 10;
        AssertArcher(bridge, player, index, expected, $"populated slot {index}");
    }

    Assert(slots.SequenceEqual(new byte[]
        {
            1, 0, 0, 0, 0x80, 0, 0, 0, 0, byte.MaxValue
        }), "IsExistArcher mutated the player slot array");
    Equal(0, player.m_MsgList.Count,
        "IsExistArcher sent a message to the explicit player");
    Equal(0, bridge.CurrentPlayer.m_MsgList.Count,
        "IsExistArcher sent a message to the context player");
}

static void VerifyExplicitPlayerOwnershipAndNpcGate()
{
    var enabledNpc = new NormNpc { m_sCharName = "property-12" };
    enabledNpc.AddNativePasProperty(12);
    var disabledNpc = new NormNpc { m_sCharName = "no-property-12" };

    var explicitPlayer = new TPlayObject
    {
        m_sCharName = "dead-ghost-explicit",
        m_boDeath = true,
        m_boGhost = true
    };
    var contextPlayer = new TPlayObject
    {
        m_sCharName = "healthy-context"
    };
    ArcherSlots(explicitPlayer)[2] = 7;
    ArcherSlots(contextPlayer)[2] = 9;
    var bridge = NewBridge(enabledNpc, contextPlayer);

    AssertArcher(bridge, explicitPlayer, 3, true,
        "dead/ghost explicit player");
    AssertArcher(bridge, contextPlayer, 3, true,
        "context player supplied explicitly");

    var emptyPlayer = new TPlayObject { m_sCharName = "empty-explicit" };
    AssertArcher(bridge, emptyPlayer, 3, false,
        "different explicit player");

    bridge.CurrentNpc = disabledNpc;
    AssertArcher(bridge, explicitPlayer, 3, false,
        "NPC property gate");
    disabledNpc.AddNativePasProperty(12);
    AssertArcher(bridge, explicitPlayer, 3, true,
        "NPC property gate enabled");

    Assert(explicitPlayer.m_boDeath && explicitPlayer.m_boGhost,
        "IsExistArcher changed explicit player state");
    Equal(0, explicitPlayer.m_MsgList.Count,
        "dead/ghost explicit player received a message");
    Equal(0, contextPlayer.m_MsgList.Count,
        "context player received a message");
    Equal((byte)7, ArcherSlots(explicitPlayer)[2],
        "explicit player slot changed");
    Equal((byte)9, ArcherSlots(contextPlayer)[2],
        "context player slot changed");
}

static void VerifyBoundsAndAbiShadows()
{
    var npc = new NormNpc { m_sCharName = "abi-npc" };
    npc.AddNativePasProperty(12);
    var player = new TPlayObject { m_sCharName = "abi-player" };
    var bridge = NewBridge(npc, player);
    ArcherSlots(player)[0] = 1;
    ArcherSlots(player)[9] = 1;

    foreach (var index in new[]
             {
                 int.MinValue, -1, 0, 11, 12, int.MaxValue
             })
        AssertArcher(bridge, player, index, false, $"out-of-range {index}");

    AssertRejected(bridge, new List<PasValue>(), "missing arguments");
    AssertRejected(bridge, new List<PasValue>
    {
        PasValue.FromObject(player)
    }, "missing index");
    AssertRejected(bridge, new List<PasValue>
    {
        PasValue.FromInt(1), PasValue.FromInt(1)
    }, "non-player first argument");
    AssertRejected(bridge, new List<PasValue>
    {
        PasValue.FromObject(player), PasValue.FromInt(1), PasValue.FromInt(1)
    }, "extra argument");

    var validArgs = Args(player, 1);
    Assert(!bridge.CallNpcMethod("IsExistArcher", validArgs, out var methodResult),
        "NPC procedure shadow was exposed");
    Equal(PasValueType.Nil, methodResult.Type,
        "NPC procedure shadow result");
    Assert(!bridge.CallPlayerFunc("IsExistArcher", validArgs,
            out var playerFuncResult),
        "player function shadow was exposed");
    Equal(PasValueType.Nil, playerFuncResult.Type,
        "player function shadow result");

    bridge.CurrentNpc = null;
    Assert(!bridge.CallNpcFunc("IsExistArcher", validArgs,
            out var missingNpcResult),
        "missing current NPC was accepted");
    Equal(PasValueType.Nil, missingNpcResult.Type,
        "missing current NPC result");
}

static void VerifySourceContract()
{
    var root = FindRepositoryRoot();
    var npcSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Npcs",
        "NormNpc.NativeMagicTower.cs"));
    var playerSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeMagicTower.cs"));
    var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var npcFunctions = Slice(bridgeSource, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");
    var npcMethods = Slice(bridgeSource, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var playerFunctions = Slice(bridgeSource, "public bool CallPlayerFunc",
        "public bool CallNpcMethod");

    Require(playerSource.Contains(
            "private readonly byte[] m_btNativeMagicTowerArcherSlots = new byte[10];",
            StringComparison.Ordinal),
        "player-owned transient ten-byte slot field is missing");
    Require(playerSource.Contains(
            "var slot = unchecked((uint)(index - 1));",
            StringComparison.Ordinal),
        "unsigned index-minus-one guard is missing");
    Require(npcSource.Contains("HasNativePasProperty(12)",
            StringComparison.Ordinal),
        "NPC property-12 gate is missing");
    Require(npcSource.Contains("player.HasNativeMagicTowerArcher(index)",
            StringComparison.Ordinal),
        "NPC function does not read the explicit player's slots");
    Equal(1, Count(npcFunctions, "case \"isexistarcher\":"),
        "NPC function dispatch count");
    Equal(0, Count(npcMethods, "case \"isexistarcher\":"),
        "NPC procedure dispatch count");
    Equal(1, Count(playerFunctions, "case \"isexistarcher\":"),
        "player function fail-closed dispatch count");
}

static PasApiBridge NewBridge(NormNpc npc, TPlayObject contextPlayer) => new()
{
    CurrentNpc = npc,
    CurrentPlayer = contextPlayer
};

static void AssertArcher(PasApiBridge bridge, TPlayObject player, int index,
    bool expected, string name)
{
    var slotsBefore = ArcherSlots(player).ToArray();
    Assert(bridge.CallNpcFunc("IsExistArcher", Args(player, index),
            out var result),
        name + " valid ABI was rejected");
    Equal(PasValueType.Boolean, result.Type, name + " result type");
    Equal(expected, result.AsBool(), name + " result");
    Assert(slotsBefore.SequenceEqual(ArcherSlots(player)),
        name + " mutated player slots");
}

static void AssertRejected(PasApiBridge bridge, List<PasValue> args,
    string name)
{
    Assert(!bridge.CallNpcFunc("IsExistArcher", args, out var result),
        name + " was accepted");
    Equal(PasValueType.Nil, result.Type, name + " result");
}

static List<PasValue> Args(TPlayObject player, int index) => new()
{
    PasValue.FromObject(player),
    PasValue.FromInt(index)
};

static byte[] ArcherSlots(TPlayObject player)
{
    var field = typeof(TPlayObject).GetField(
        "m_btNativeMagicTowerArcherSlots",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TPlayObject).FullName,
            "m_btNativeMagicTowerArcherSlots");
    return (byte[])(field.GetValue(player)
        ?? throw new InvalidOperationException("native archer slots are null"));
}

static void PrepareRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.nServerIndex = 0;
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.CreditCardService = NativeCreditCardService.Disabled;
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

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start,
        $"source region missing: {startMarker} -> {endMarker}");
    return source.Substring(start, end - start);
}

static int Count(string source, string value)
{
    var count = 0;
    var offset = 0;
    while ((offset = source.IndexOf(value, offset,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message) => Require(condition, message);

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}
