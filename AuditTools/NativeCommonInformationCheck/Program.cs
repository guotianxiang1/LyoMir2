using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

const int FlagsOffset = 0x060C;
const int ModeOffset = 0x0610;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

Equal(1099, Grobal2.CM_COMMON_INFORMATION, "1099 ident");
CheckIngressMapping();
CheckNativeHumanLoad();
CheckHeroOptionsAndDeathGate();
CheckPlayerFlagsAndRefresh();
CheckPlayerMode();

Console.WriteLine(
    "NativeCommonInformationCheck PASS ident=1099 mapping=Recog/Param/Tag hero=1..3 flags=4 mode=5 silent=yes");

static void CheckIngressMapping()
{
    var player = NewPlayer();
    player.m_boOffLineFlag = false;
    M2Share.UserEngine.ProcessUserMessage(player, new ClientPacket
    {
        Ident = Grobal2.CM_COMMON_INFORMATION,
        Recog = -17,
        Param = 2,
        Tag = 1,
        Series = 9
    }, string.Empty);

    TProcessMessage queued = null;
    Assert(player.TryTake(ref queued), "1099 ingress was not queued");
    Equal(-17, queued.nParam1, "Recog/value mapping");
    Equal(2, queued.nParam2, "Param/subtype mapping");
    Equal(1, queued.nParam3, "Tag/option mapping");
}

static void CheckNativeHumanLoad()
{
    var raw = new byte[0xEEF8];
    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(FlagsOffset, 2), 0xA5A7);
    raw[ModeOffset] = 1;
    var human = new THumDataInfo { NativeData = raw };
    var player = NewPlayer();
    var getHumData = typeof(UserEngine).GetMethod("GetHumData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(getHumData != null, "native human load method");
    getHumData!.Invoke(M2Share.UserEngine, new object[] { player, human });
    Equal((ushort)0xA5A7, player.m_wNativeCommonInformationFlags,
        "flags restore +0x60C");
    Equal((byte)1, player.m_btNativeCommonInformationMode,
        "mode restore +0x610");
}

static void CheckHeroOptionsAndDeathGate()
{
    var player = NewPlayer();
    var hero = NewHero();
    player.m_HeroObject = hero;

    Operate(player, 1, 1);
    Assert(hero.m_boNativeCommonInformationOption1, "subtype 1 positive value");
    Operate(player, -1, 1);
    Assert(!hero.m_boNativeCommonInformationOption1, "subtype 1 negative value");

    Operate(player, 0, 2);
    Equal(1, hero.m_nNativeCommonInformationOption2, "subtype 2 zero fallback");
    Operate(player, -23, 2);
    Equal(-23, hero.m_nNativeCommonInformationOption2, "subtype 2 signed value");

    Operate(player, 1, 3);
    Assert(hero.m_boNativeCommonInformationOption3, "subtype 3 positive value");
    Operate(player, 0, 3);
    Assert(!hero.m_boNativeCommonInformationOption3, "subtype 3 zero value");

    hero.m_boDeath = true;
    hero.m_boNativeCommonInformationOption1 = true;
    hero.m_nNativeCommonInformationOption2 = 41;
    hero.m_boNativeCommonInformationOption3 = true;
    Operate(player, 0, 1);
    Operate(player, 99, 2);
    Operate(player, 0, 3);
    Assert(hero.m_boNativeCommonInformationOption1, "dead hero subtype 1 gate");
    Equal(41, hero.m_nNativeCommonInformationOption2, "dead hero subtype 2 gate");
    Assert(hero.m_boNativeCommonInformationOption3, "dead hero subtype 3 gate");

    player.m_HeroObject = null;
    Operate(player, 1, 1);
    Assert(player.m_DefMsg == null, "hero option response must be silent");
}

static void CheckPlayerFlagsAndRefresh()
{
    var player = NewPlayer();
    var hero = NewHero();
    var environment = new Envirnoment();
    player.m_PEnvir = environment;
    hero.m_PEnvir = environment;
    player.m_boObMode = true;
    hero.m_boObMode = true;
    player.m_HeroObject = hero;
    player.m_NativeHumanData = new byte[0xEEF8];
    player.m_wNativeCommonInformationFlags = 0x20;

    Operate(player, 1, 4, 0);
    Equal((ushort)0x23, player.m_wNativeCommonInformationFlags,
        "subtype 4 tag 0 enable");
    Equal((ushort)0x23, BinaryPrimitives.ReadUInt16LittleEndian(
        player.m_NativeHumanData.AsSpan(FlagsOffset, 2)), "flags persistence");
    FeatureMessage(player.m_MsgList, "player tag 0 refresh");
    FeatureMessage(hero.m_MsgList, "hero tag 0 refresh");

    player.m_MsgList.Clear();
    hero.m_MsgList.Clear();
    hero.m_boDeath = true;
    Operate(player, 0, 4, 0);
    Equal((ushort)0x21, player.m_wNativeCommonInformationFlags,
        "subtype 4 tag 0 disable");
    FeatureMessage(player.m_MsgList, "player disable refresh");
    FeatureMessage(hero.m_MsgList, "dead hero refresh");

    player.m_MsgList.Clear();
    hero.m_MsgList.Clear();
    Operate(player, -1, 4, 1);
    Equal((ushort)0x25, player.m_wNativeCommonInformationFlags,
        "subtype 4 tag 1 nonzero enable");
    FeatureMessage(player.m_MsgList, "player tag 1 refresh");
    FeatureMessage(hero.m_MsgList, "hero tag 1 refresh");

    player.m_MsgList.Clear();
    hero.m_MsgList.Clear();
    var before = player.m_wNativeCommonInformationFlags;
    Operate(player, 1, 4, 2);
    Equal(before, player.m_wNativeCommonInformationFlags, "invalid tag state");
    Equal(0, player.m_MsgList.Count, "invalid tag player refresh");
    Equal(0, hero.m_MsgList.Count, "invalid tag hero refresh");
    Assert(player.m_DefMsg == null, "subtype 4 response must be silent");
}

static void CheckPlayerMode()
{
    var player = NewPlayer();
    player.m_NativeHumanData = new byte[0xEEF8];
    player.m_btNativeCommonInformationMode = 1;

    Operate(player, 0, 5);
    Equal((byte)0, player.m_btNativeCommonInformationMode, "subtype 5 value 0");
    Equal((byte)0, player.m_NativeHumanData[ModeOffset], "mode 0 persistence");
    Operate(player, 1, 5);
    Equal((byte)1, player.m_btNativeCommonInformationMode, "subtype 5 value 1");
    Equal((byte)1, player.m_NativeHumanData[ModeOffset], "mode 1 persistence");
    Operate(player, 2, 5);
    Equal((byte)1, player.m_btNativeCommonInformationMode, "subtype 5 value 2 reject");
    Operate(player, -1, 5);
    Equal((byte)1, player.m_btNativeCommonInformationMode,
        "subtype 5 unsigned negative reject");
    Operate(player, 1, 6);
    Equal((byte)1, player.m_btNativeCommonInformationMode, "unknown subtype reject");
    Assert(player.m_DefMsg == null, "subtype 5 response must be silent");
}

static void Operate(ProbePlayer player, int value, int subtype, int option = 0)
{
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_COMMON_INFORMATION,
        nParam1 = value,
        nParam2 = subtype,
        nParam3 = option
    }), $"1099 dispatcher subtype {subtype}");
}

static ProbePlayer NewPlayer()
{
    var player = new ProbePlayer
    {
        m_boOffLineFlag = false,
        m_MsgList = new List<SendMessage>()
    };
    M2Share.UserEngine.ProcessUserMessage(player, new ClientPacket
    {
        Ident = Grobal2.CM_LOGINNOTICEOK,
        Recog = 1,
        Param = 0,
        Tag = 0,
        Series = 0
    }, string.Empty);
    player.m_boOffLineFlag = true;
    return player;
}

static ProbeHero NewHero() => new()
{
    m_MsgList = new List<SendMessage>()
};

static void FeatureMessage(IList<SendMessage> messages, string label)
{
    Equal(1, messages.Count, label + " count");
    Equal(Grobal2.RM_FEATURECHANGED, messages[0].wIdent, label + " ident");
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

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}

sealed class ProbeHero : HeroObject
{
}
