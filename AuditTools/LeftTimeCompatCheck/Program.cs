using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();

var player = NewPlayer();
var bridge = new PasApiBridge { CurrentPlayer = player };

Assert(!bridge.CallPlayerMethod("LeftTime", Values("bad", 1)),
    "LeftTime accepted the wrong argument count");
Equal(0, player.m_MsgList.Count, "wrong-arity queue count");

Assert(bridge.CallPlayerMethod("LeftTime", Values("negative-line", -1, 300)),
    "LeftTime rejected native silent no-op line");
Assert(bridge.CallPlayerMethod("LeftTime", Values("zero-seconds", 0, 0)),
    "LeftTime rejected native silent no-op seconds");
Equal(0, player.m_MsgList.Count, "invalid native range queue count");

var ghost = NewPlayer();
ghost.m_boGhost = true;
var ghostBridge = new PasApiBridge { CurrentPlayer = ghost };
Assert(ghostBridge.CallPlayerMethod("LeftTime", Values("ghost", 0, 600)),
    "LeftTime rejected a ghost player call");
Equal(0, ghost.m_MsgList.Count, "ghost queue count");

player.SendMsg(player, 90001, 2, 3, 4, 5, "sentinel");
var cases = new[]
{
    new ExpectedCall("travel-300", 20001, 300),
    new ExpectedCall("travel-1-a", 20001, 1),
    new ExpectedCall("travel-1-b", 20001, 1),
    new ExpectedCall("guard-600", 0, 600)
};
foreach (var expected in cases)
{
    Assert(bridge.CallPlayerMethod("LeftTime",
            Values(expected.Message, expected.Line, expected.Seconds)),
        "LeftTime rejected a production argument shape");
}

Equal(1 + cases.Length, player.m_MsgList.Count, "FIFO queue count");
Equal(90001, player.m_MsgList[0].wIdent, "FIFO sentinel ident");
for (var i = 0; i < cases.Length; i++)
{
    VerifyQueued(player, player.m_MsgList[i + 1], cases[i], i);
}

var outgoing = NewPlayer();
var process = new TProcessMessage
{
    wIdent = Grobal2.RM_USERSAVEITEM,
    wParam = 4,
    nParam1 = 300,
    nParam2 = 0x12345,
    nParam3 = 0x23456,
    sMsg = "packet-body"
};
Assert(outgoing.Operate(process), "10160 dispatcher returned false");
Equal(Grobal2.SM_2821, outgoing.m_DefMsg.Ident, "client ident");
Equal(300, outgoing.m_DefMsg.Recog, "client Recog");
Equal(4, outgoing.m_DefMsg.Param, "client Param");
Equal(0x2345, outgoing.m_DefMsg.Tag, "client Tag low word");
Equal(0x3456, outgoing.m_DefMsg.Series, "client Series low word");

Equal(10160, Grobal2.RM_LEFTTIME, "LeftTime internal ident");
Equal(Grobal2.RM_USERSAVEITEM, Grobal2.RM_LEFTTIME,
    "10160 dispatcher alias");
Equal(2821, Grobal2.SM_2821, "LeftTime client ident");

Console.WriteLine(
    "PASS LeftTime ABI=3 validation=native queue=10160 FIFO client=2821 " +
    "mapping=Recog:iSec/Param:4/Tag:iLine/Series:0");
return;

static TPlayObject NewPlayer() => new() { m_boOffLineFlag = true };

static void VerifyQueued(TPlayObject player, SendMessage queued,
    ExpectedCall expected, int index)
{
    var prefix = "queue[" + index + "] ";
    Equal(Grobal2.RM_LEFTTIME, queued.wIdent, prefix + "ident");
    Equal(4, queued.wParam, prefix + "wParam");
    Equal(expected.Seconds, queued.nParam1, prefix + "nParam1/iSec");
    Equal(expected.Line, queued.nParam2, prefix + "nParam2/iLine");
    Equal(0, queued.nParam3, prefix + "nParam3");
    Equal(0, queued.dwDeliveryTime, prefix + "delivery time");
    Assert(!queued.boLateDelivery, prefix + "late-delivery flag");
    Assert(ReferenceEquals(player, queued.BaseObject), prefix + "base object");
    Assert(queued.ObjectId == 0, prefix + "object id substitute");
    Assert(queued.Payload == null, prefix + "payload");
    Assert(queued.Buff == expected.Message, prefix + "message body");
}

static List<PasValue> Values(params object[] values) => values.Select(value => value switch
{
    int number => PasValue.FromInt(number),
    string text => PasValue.FromString(text),
    _ => PasValue.FromObject(value)
}).ToList();

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

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record ExpectedCall(string Message, int Line, int Seconds);
