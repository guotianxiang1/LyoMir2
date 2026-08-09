using System.Text;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Plugins;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

M2Share.LogSystem = new MirLog();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();

var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-questinfo-" + Guid.NewGuid().ToString("N"));
try
{
    var envirPath = Directory.CreateDirectory(Path.Combine(tempRoot, "Envir")).FullName;
    File.WriteAllText(Path.Combine(tempRoot, "config.json"),
        "{\"\u8bbe\u7f6e\u73a9\u5bb6\u79f0\u53f7\u51fd\u6570\":1}",
        HUtil32.GbkEncoding);

    var pluginManager = new PluginManager(envirPath);
    pluginManager.RegisterBuiltinPlugins();
    Assert(pluginManager.LoadPlugin("YanshenCompat"),
        "YanshenCompat did not enter Running state");
    M2Share.PluginManager = pluginManager;

    var player = new TPlayObject
    {
        m_sCharName = "\u6d4b\u8bd5\u89d2\u8272",
        m_PEnvir = new Envirnoment(),
        m_boObMode = true
    };
    var api = new PasApiBridge();
    InitializeYanshen(api, envirPath);
    var baseShowName = player.GetShowName();
    using (api.PushContext(player, null))
    {
        CallQuestInfo(api, "\u9b54\u6cd5:3,120:$\u6218\u795e:255:1$2$1$0$3$4");
        var firstPayload = "\u9b54\u6cd5:3,120:$\u6218\u795e:255:1$2$1$0$3$4";
        var expected = baseShowName + "\\" + firstPayload;
        Equal(expected, player.GetShowName(), "first title body");
        Assert(player.m_MsgList.Count == 1, "QuestInfo did not immediately refresh the name");
        Equal(Grobal2.RM_USERNAME, player.m_MsgList[0].wIdent, "refresh message ident");
        Equal(expected, player.m_MsgList[0].Buff, "refresh message body");

        player.m_MsgList.Clear();
        CallQuestInfo(api, "\u65b0\u79f0\u53f7:7");
        expected = baseShowName + "\\\u65b0\u79f0\u53f7:7";
        Equal(expected, player.GetShowName(), "QuestInfo did not overwrite the prior title");
        Assert(!player.GetShowName().Contains('|'), "eye title path retained native append syntax");

        var longPayload = string.Concat(Enumerable.Repeat("\u6218", 41));
        CallQuestInfo(api, longPayload);
        var title = player.GetShowName().Split('\\')[^1];
        Equal(80, HUtil32.GbkEncoding.GetByteCount(title), "GBK title byte cap");
        Equal(40, title.Length, "GBK title was cut through a double-byte character");

        CallQuestInfo(api, string.Empty);
        Equal(baseShowName, player.GetShowName(), "empty QuestInfo did not clear the title");
    }

    M2Share.PluginManager = null;
    var nativePlayer = new TPlayObject { m_sCharName = "native-player" };
    var nativeApi = new PasApiBridge();
    using (nativeApi.PushContext(nativePlayer, null))
    {
        CallQuestInfo(nativeApi, "old");
        CallQuestInfo(nativeApi, "new");
    }
    var nativeBuffer = (string)typeof(TPlayObject)
        .GetField("_nativeQuestInfoBuffer", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(nativePlayer)!;
    Equal("new|old", nativeBuffer, "native QuestInfo append order");
    Assert(!nativePlayer.GetShowName().Contains("old") &&
           !nativePlayer.GetShowName().Contains("new"),
        "native QuestInfo buffer leaked into the client name");

    Console.WriteLine(
        "PASS questinfo native=buffer eye=overwrite gbk=80 refresh=RM_USERNAME body=show-name");
}
finally
{
    M2Share.PluginManager = null;
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, true);
}

static void CallQuestInfo(PasApiBridge api, string payload)
{
    Assert(api.CallPlayerMethod("QuestInfo",
        new List<PasValue> { PasValue.FromString(payload) }),
        "QuestInfo was not dispatched");
}

static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void InitializeYanshen(PasApiBridge bridge, string envirPath)
{
    const string source = "program RunQuest; procedure initys; begin end; begin end.";
    var sourceFile = Path.Combine(envirPath, "PsMapQuest", "RunQuest.pas");
    var program = new PasParser(new PasLexer(source, sourceFile), envirPath).Parse();
    new PasInterpreter(program, bridge).ExecuteProcedure("initys");
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
