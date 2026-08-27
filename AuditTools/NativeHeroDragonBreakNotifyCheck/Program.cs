using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeFiles();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

var stateField = typeof(HeroObject).GetField(
    "m_btNativeDragonBreakState6D9",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("missing native +0x6D9 field");
var stateWorker = typeof(TPlayObject).GetMethod(
    "HeroNotifyDragonBreakState",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("missing CM3503 state worker");

int Worker(HeroObject hero) => (int)stateWorker.Invoke(null, new object[] { hero });
byte State(HeroObject hero) => (byte)stateField.GetValue(hero);
void SetState(HeroObject hero, byte value) => stateField.SetValue(hero, value);

var hero = new HeroObject();
SetState(hero, 1);
Equal(-1, Worker(hero), "unlearned result");
Equal((byte)0, State(hero), "unlearned +0x6D9");

hero.m_HeroMagicList.Add(new TUserMagic
{
    MagicInfo = new TMagic { wMagicID = 0x111 }
});
Equal(0, Worker(hero), "learned without cooldown result");
Equal((byte)1, State(hero), "ready +0x6D9");

var first = new TBaseObject.NativeColdTimeEntry
{
    Key = 0x111,
    Remaining = 1,
    Total = 1
};
hero.m_NativeColdTimes.Add(first);
hero.m_NativeColdTimes.Add(new TBaseObject.NativeColdTimeEntry
{
    Key = 0x111,
    Remaining = -1,
    Total = 1
});
Equal(-2, Worker(hero), "first duplicate positive cooldown result");
Equal((byte)0, State(hero), "cooling +0x6D9");

first.Remaining = 0;
Equal(0, Worker(hero), "zero cooldown result");
Equal((byte)1, State(hero), "zero cooldown +0x6D9");
first.Remaining = -1;
Equal(0, Worker(hero), "negative cooldown result");
Equal((byte)1, State(hero), "negative cooldown +0x6D9");

var player = new TPlayObject { m_HeroObject = new HeroObject() };
Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.CM_3503 }),
    "CM3503 dispatch");
Equal(1, player.m_MsgList.Count, "unlearned queue count");
Queued(player.m_MsgList[0],
    Convert.FromHexString("C3BBD3D0D1A7BBE1BCBCC4DCC9FDC1FAC6C600"),
    "unlearned queue");
Equal((byte)0, State(player.m_HeroObject), "dispatch unlearned +0x6D9");

player.m_MsgList.Clear();
player.m_HeroObject.m_HeroMagicList.Add(new TUserMagic
{
    MagicInfo = new TMagic { wMagicID = 0x111 }
});
Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.CM_3503 }),
    "ready CM3503 dispatch");
Equal(0, player.m_MsgList.Count, "ready remains silent");
Equal((byte)1, State(player.m_HeroObject), "dispatch ready +0x6D9");

player.m_HeroObject.m_NativeColdTimes.Add(
    new TBaseObject.NativeColdTimeEntry
    {
        Key = 0x111,
        Remaining = int.MaxValue,
        Total = int.MaxValue
    });
Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.CM_3503 }),
    "cooling CM3503 dispatch");
Equal(1, player.m_MsgList.Count, "cooling queue count");
Queued(player.m_MsgList[0],
    Convert.FromHexString("BCBCC4DCC9FDC1FAC6C6BBB9D4DAC0E4C8B4D6D000"),
    "cooling queue");
Equal((byte)0, State(player.m_HeroObject), "dispatch cooling +0x6D9");

player.m_MsgList.Clear();
player.m_boGhost = true;
Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.CM_3503 }),
    "ghost CM3503 dispatch");
Equal(0, player.m_MsgList.Count, "ghost suppresses queue");
Equal((byte)0, State(player.m_HeroObject),
    "ghost gate runs after hero state update");

Console.WriteLine(
    "NativeHeroDragonBreakNotifyCheck PASS CM3503 states=-1/-2/0 "
    + "hero+0x6D9=0/0/1 queue=RM10100 param=0x38FF "
    + "bodies=19/21-NUL ready=silent");

static void Queued(SendMessage message, byte[] expectedBody, string label)
{
    Equal(Grobal2.RM_SYSMESSAGE, message.wIdent, label + " ident");
    Equal(0x38FF, message.wParam, label + " wParam");
    Equal(0, message.nParam1, label + " nParam1");
    Equal(0, message.nParam2, label + " nParam2");
    Equal(0, message.nParam3, label + " nParam3");
    Assert(string.IsNullOrEmpty(message.Buff), label + " string body");
    var body = message.Payload as byte[]
        ?? throw new InvalidOperationException(label + " raw body missing");
    Assert(expectedBody.SequenceEqual(body),
        label + $" raw body: expected={Convert.ToHexString(expectedBody)} "
        + $"actual={Convert.ToHexString(body)}");
    Equal(expectedBody.Length, message.nBodyLen, label + " body length");
}

static void PrepareRuntimeFiles()
{
    var root = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(root, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(root, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(root, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var share = Path.Combine(Path.GetFullPath(Path.Combine(root, "..")), "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(share, "ServerData.ini"),
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
    if (!condition)
        throw new InvalidOperationException(label);
}
