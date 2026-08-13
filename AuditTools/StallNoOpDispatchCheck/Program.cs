using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

try
{
    Equal(4423, Grobal2.CM_CANCEL_STALL,
        "CM_CANCEL_STALL constant");
    Equal(4481, Grobal2.CM_QUERY_STALL_STATUS,
        "CM_QUERY_STALL_STATUS constant");
    Equal(4644, Grobal2.CM_V_POWERSTONE,
        "CM_V_POWERSTONE constant");
    Equal(4644, Grobal2.SM_V_POWERSTONE,
        "SM_V_POWERSTONE constant");

    VerifySilentDispatch(Grobal2.CM_CANCEL_STALL, "GA0");
    VerifySilentDispatch(Grobal2.CM_CANCEL_STALL, "3");
    VerifySilentDispatch(Grobal2.CM_QUERY_STALL_STATUS, "GA0");
    VerifySilentDispatch(Grobal2.CM_QUERY_STALL_STATUS, "3");
    VerifySilentDispatch(Grobal2.CM_V_POWERSTONE, "GA0");
    VerifySilentDispatch(Grobal2.CM_V_POWERSTONE, "3");
    VerifySourceContract();

    Console.WriteLine(
        "PASS StallNoOpDispatchCheck commands=4423/4481/4644 " +
        "dispatch=silent-no-op map=independent response=none state=unchanged");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"StallNoOpDispatchCheck FAIL: {exception}");
    return 1;
}

static void VerifySilentDispatch(int command, string mapName)
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    var markerPacket = new ClientPacket { Ident = 0x7FFF };
    player.m_MsgList = new List<SendMessage>();
    player.m_DefMsg = markerPacket;
    player.m_sMapName = mapName;
    player.m_nGold = 123456;

    var handled = player.Operate(new TProcessMessage
    {
        wIdent = command,
        wParam = 0x1234,
        nParam1 = 0x10203040,
        nParam2 = -123,
        nParam3 = 456,
        BaseObject = 0,
        sMsg = "ignored",
        Payload = new byte[] { 1, 2, 3, 4 }
    });

    Require(handled, $"command {command} stopped the player message loop");
    Equal(0, player.m_MsgList.Count,
        $"command {command} queued a response or system message");
    Require(ReferenceEquals(markerPacket, player.m_DefMsg),
        $"command {command} built a response packet");
    Equal(123456, player.m_nGold,
        $"command {command} changed player currency");
    Require(player.m_sMapName == mapName,
        $"command {command} changed player map state");
}

static void VerifySourceContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    var cancel = source.IndexOf("case Grobal2.CM_CANCEL_STALL:",
        StringComparison.Ordinal);
    var status = source.IndexOf("case Grobal2.CM_QUERY_STALL_STATUS:",
        StringComparison.Ordinal);
    var powerStone = source.IndexOf("case Grobal2.CM_V_POWERSTONE:",
        StringComparison.Ordinal);
    Require(cancel >= 0 && status > cancel,
        "native no-op stall cases are missing or reordered");
    var breakIndex = source.IndexOf("break;", status,
        StringComparison.Ordinal);
    var rejection = source.IndexOf("RejectUnavailableStallRequest", cancel,
        StringComparison.Ordinal);
    Require(breakIndex > status && (rejection < 0 || breakIndex < rejection),
        "4423/4481 do not terminate in the silent no-op branch");
    Require(powerStone >= 0 && source.IndexOf("break;", powerStone,
            StringComparison.Ordinal) > powerStone,
        "4644 does not terminate in a silent no-op branch");
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
