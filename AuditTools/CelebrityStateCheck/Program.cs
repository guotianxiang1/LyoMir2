using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

var npc = new NormNpc { m_wAppr = 50 };
Assert(npc.SetBodyState(Grobal2.STATE_CELEBRITY, true),
    "celebrity state was not set");
Equal(1 << 9, npc.m_nCharStatus4, "celebrity state word");
Equal(0, npc.m_nCharStatus2, "unexpected second state word");
Equal(0, npc.m_nCharStatus3, "unexpected third state word");

var actorStateBuilder = typeof(TPlayObject).GetMethod("BuildMobileActorStateBody",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var body = (byte[])actorStateBuilder.Invoke(null,
    new object[] { 0x12345678, npc })!;
Equal(32, body.Length, "actor-state body length");
Equal(0x12345678, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, 4)),
    "feature field");
Equal(1 << 9, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(16, 4)),
    "serialized celebrity state");

var tempDirectory = Path.Combine(Path.GetTempPath(),
    "celebrity-state-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(tempDirectory, "Share", "config"));
try
{
    M2Share.sConfigPath = tempDirectory;
    M2Share.g_Config.sBaseDir = "Share";
    M2Share.g_Config.sCastleDir = "Castle";
    var heroFile = Path.Combine(tempDirectory, "Share", "config", "Hero.ini");
    File.WriteAllText(heroFile,
        "[hero0]\r\n角色名=荣誉战士\r\n等级=40\r\n经验=123\r\n颜色=1\r\n",
        HUtil32.GbkEncoding);

    var loaded = new NormNpc { m_wAppr = 50 };
    NativeCelebrityStatueManager.Initialize(loaded);
    Equal("荣誉战士", loaded.m_sCelebrityPlayerName, "GBK hero statue name");
    Equal((ushort)40, loaded.m_wCelebrityLevel, "loaded celebrity level");
    Equal(123, loaded.m_nCelebrityExperience, "loaded celebrity experience");
    Equal(1 << 9, loaded.m_nCharStatus4, "loaded celebrity state");

    var bridge = new PasApiBridge();
    bridge.CurrentNpc = loaded;
    Assert(bridge.CallNpcFunc("GetCelebName", new List<PasValue>(), out var result),
        "GetCelebName was not dispatched");
    Equal("荣誉战士", result.AsString(), "GetCelebName result");
    Assert(bridge.CallNpcMethod("ChgCelebColor",
            new List<PasValue> { PasValue.FromInt(0) }, out result),
        "ChgCelebColor failed");
    Equal(0, loaded.m_nCharStatus4, "cleared celebrity state");

    var applicant = new TPlayObject
    {
        m_sCharName = "新天下第一战士王",
        m_btJob = 0,
        m_btGender = PlayGender.Man
    };
    bridge.CurrentPlayer = applicant;
    applicant.m_Abil.Level = 34;
    Assert(bridge.CallNpcFunc("ReqBecomeCeleb", new List<PasValue>(), out result),
        "ReqBecomeCeleb low-level dispatch failed");
    Equal(-2, result.AsInt(), "ReqBecomeCeleb low-level result");

    applicant.m_Abil.Level = 41;
    applicant.m_btJob = 1;
    Assert(bridge.CallNpcFunc("ReqBecomeCeleb", new List<PasValue>(), out result),
        "ReqBecomeCeleb job mismatch dispatch failed");
    Equal(-3, result.AsInt(), "ReqBecomeCeleb job mismatch result");

    applicant.m_btJob = 0;
    applicant.m_Abil.Level = 40;
    applicant.m_Abil.Exp = 123;
    Assert(bridge.CallNpcFunc("ReqBecomeCeleb", new List<PasValue>(), out result),
        "ReqBecomeCeleb ranking dispatch failed");
    Equal(-5, result.AsInt(), "ReqBecomeCeleb ranking result");

    applicant.m_Abil.Level = 41;
    applicant.m_Abil.Exp = 456;
    Assert(bridge.CallNpcFunc("ReqBecomeCeleb", new List<PasValue>(), out result),
        "ReqBecomeCeleb success dispatch failed");
    Equal(0, result.AsInt(), "ReqBecomeCeleb success result");
    Equal("新天下第一战士", loaded.m_sCelebrityPlayerName,
        "ReqBecomeCeleb 15-byte GBK name");
    Equal((ushort)41, loaded.m_wCelebrityLevel, "updated celebrity level");
    Equal(456, loaded.m_nCelebrityExperience, "updated celebrity experience");

    var reloaded = new NormNpc { m_wAppr = 50 };
    NativeCelebrityStatueManager.Initialize(reloaded);
    Equal("新天下第一战士", reloaded.m_sCelebrityPlayerName, "persisted statue name");
    Equal((ushort)41, reloaded.m_wCelebrityLevel, "persisted statue level");
    Equal(456, reloaded.m_nCelebrityExperience, "persisted statue experience");
    Equal(0, reloaded.m_nCharStatus4, "persisted statue color");

    var explicitOwner = new TPlayObject
    {
        m_sCharName = "新城主测试名字超长",
        m_btJob = 0,
        m_btGender = PlayGender.WoMan
    };
    var decoyOwner = new TPlayObject
    {
        m_sCharName = "错误的隐式玩家",
        m_btJob = 2,
        m_btGender = PlayGender.Man
    };
    bridge.CurrentPlayer = decoyOwner;

    Equal(-1, NativeCelebrityStatueManager.TrySetCastleOwner(null),
        "null castle owner result");
    Assert(bridge.CallNpcFunc("ReqCastleOwnerNpc",
            new List<PasValue> { PasValue.FromObject(explicitOwner) }, out result),
        "ReqCastleOwnerNpc missing-statue dispatch failed");
    Equal(-1, result.AsInt(), "missing castle-owner statue result");

    var invalidCastleCalls = new[]
    {
        new List<PasValue>(),
        new List<PasValue> { PasValue.FromInt(1) },
        new List<PasValue> { PasValue.Nil },
        new List<PasValue>
        {
            PasValue.FromObject(explicitOwner), PasValue.FromInt(1)
        }
    };
    foreach (var invalidArgs in invalidCastleCalls)
    {
        Assert(!bridge.CallNpcFunc("ReqCastleOwnerNpc", invalidArgs, out result),
            "ReqCastleOwnerNpc accepted an invalid ABI");
        Equal(PasValueType.Nil, result.Type,
            "ReqCastleOwnerNpc invalid-ABI result");
    }
    Assert(!bridge.CallNpcMethod("ReqCastleOwnerNpc",
            new List<PasValue> { PasValue.FromObject(explicitOwner) }, out result),
        "ReqCastleOwnerNpc method shadow opened");
    Equal(PasValueType.Nil, result.Type,
        "ReqCastleOwnerNpc method-shadow result");

    var castleDirectory = Path.Combine(tempDirectory, "Castle");
    Directory.CreateDirectory(castleDirectory);
    var castleFile = Path.Combine(castleDirectory, "沙巴克城主雕像.ini");
    File.WriteAllText(castleFile,
        "[CastleOwenrStatue]\r\nJob=2\r\nGender=1\r\nRequested=0\r\n" +
        "Name=旧城主\r\nColor=1\r\n", HUtil32.GbkEncoding);

    var castleStatue = new Merchant { m_wAppr = 156 };
    NativeCelebrityStatueManager.Initialize(castleStatue);
    Assert(castleStatue.m_boCastleOwnerStatue,
        "castle-owner statue flag was not set");
    Equal((ushort)161, castleStatue.m_wAppr,
        "loaded castle-owner appearance");
    Assert(!castleStatue.m_boIsHide,
        "native Requested load did not force the runtime flag off");
    Equal(1 << 9, castleStatue.m_nCharStatus4,
        "loaded castle-owner color state");
    M2Share.UserEngine.AddMerchant(castleStatue);

    Assert(bridge.CallNpcFunc("ReqCastleOwnerNpc",
            new List<PasValue> { PasValue.FromObject(explicitOwner) }, out result),
        "ReqCastleOwnerNpc success dispatch failed");
    Equal(1, result.AsInt(), "ReqCastleOwnerNpc success result");
    Equal("新城主测试名字", castleStatue.m_sCelebrityPlayerName,
        "castle-owner 15-byte GBK name");
    Equal((byte)0, castleStatue.m_btJob, "castle-owner job");
    Equal(PlayGender.WoMan, castleStatue.m_btGender, "castle-owner gender");
    Equal((ushort)157, castleStatue.m_wAppr, "castle-owner appearance");
    Assert(!castleStatue.m_boCelebrityColor,
        "castle-owner color flag was not cleared");
    Equal(0, castleStatue.m_nCharStatus4,
        "castle-owner body color state was not cleared");

    var castleText = File.ReadAllText(castleFile, HUtil32.GbkEncoding);
    Contains(castleText, "[CastleOwenrStatue]", "castle INI section");
    Contains(castleText, "Job=0", "castle INI job");
    Contains(castleText, "Gender=1", "castle INI gender");
    Contains(castleText, "Requested=1", "castle INI Requested");
    Contains(castleText, "Name=新城主测试名字", "castle INI name");
    Contains(castleText, "Color=0", "castle INI color");

    var reloadedCastle = new Merchant { m_wAppr = 156 };
    NativeCelebrityStatueManager.Initialize(reloadedCastle);
    Equal((ushort)157, reloadedCastle.m_wAppr,
        "reloaded castle-owner appearance");
    Equal("新城主测试名字", reloadedCastle.m_sCelebrityPlayerName,
        "reloaded castle-owner name");
    Assert(!reloadedCastle.m_boIsHide,
        "reloaded Requested runtime flag");
    M2Share.UserEngine.AddMerchant(reloadedCastle);

    for (byte job = 0; job <= 2; job++)
    {
        for (var gender = 0; gender <= 1; gender++)
        {
            var mappedOwner = new TPlayObject
            {
                m_sCharName = $"Owner{job}{gender}",
                m_btJob = job,
                m_btGender = (PlayGender)gender
            };
            Assert(bridge.CallNpcFunc("ReqCastleOwnerNpc",
                    new List<PasValue> { PasValue.FromObject(mappedOwner) },
                    out result),
                "ReqCastleOwnerNpc mapped dispatch failed");
            Equal(1, result.AsInt(), "mapped castle-owner result");
            Equal((ushort)(156 + job * 2 + gender), reloadedCastle.m_wAppr,
                "mapped castle-owner appearance");
            Equal(job, reloadedCastle.m_btJob, "mapped castle-owner job");
            Equal((PlayGender)gender, reloadedCastle.m_btGender,
                "mapped castle-owner gender");
            Equal(mappedOwner.m_sCharName,
                reloadedCastle.m_sCelebrityPlayerName,
                "explicit castle-owner isolation");
        }
    }

    var beforeSameName = File.ReadAllBytes(castleFile);
    var sameOwner = new TPlayObject
    {
        m_sCharName = "oWnEr21",
        m_btJob = 0,
        m_btGender = PlayGender.Man
    };
    Assert(bridge.CallNpcFunc("ReqCastleOwnerNpc",
            new List<PasValue> { PasValue.FromObject(sameOwner) }, out result),
        "ReqCastleOwnerNpc same-name dispatch failed");
    Equal(0, result.AsInt(), "same castle-owner result");
    Assert(beforeSameName.SequenceEqual(File.ReadAllBytes(castleFile)),
        "same castle owner rewrote the INI");
    Equal((byte)2, reloadedCastle.m_btJob,
        "same castle owner mutated the statue");

    reloadedCastle.m_boIsHide = true;
    var hiddenOwner = new TPlayObject
    {
        m_sCharName = "HiddenOwner",
        m_btJob = 0,
        m_btGender = PlayGender.Man
    };
    Assert(bridge.CallNpcFunc("ReqCastleOwnerNpc",
            new List<PasValue> { PasValue.FromObject(hiddenOwner) }, out result),
        "ReqCastleOwnerNpc Requested dispatch failed");
    Equal(1, result.AsInt(), "Requested castle-owner result");
    Assert(!reloadedCastle.m_boIsHide,
        "Requested runtime flag was not cleared");
    Equal((ushort)156, reloadedCastle.m_wAppr,
        "Requested branch appearance");
    Equal((byte)2, reloadedCastle.m_btJob,
        "Requested branch must preserve the old job");
    Equal(PlayGender.WoMan, reloadedCastle.m_btGender,
        "Requested branch must preserve the old gender");
    castleText = File.ReadAllText(castleFile, HUtil32.GbkEncoding);
    Contains(castleText, "Job=2", "Requested branch persisted job");
    Contains(castleText, "Gender=1", "Requested branch persisted gender");
    Contains(castleText, "Requested=1",
        "Requested branch persisted inverse runtime flag");

}
finally
{
    Directory.Delete(tempDirectory, true);
}

Console.WriteLine("CelebrityStateCheck PASS");

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Contains(string value, string expected, string label)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"{label}: missing '{expected}'");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
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
