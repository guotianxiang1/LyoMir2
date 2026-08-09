using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
CheckLogonBody();
CheckLogonHeader();
CheckLogonMapName();
CheckMyStatusIdent();
CheckLogonBranchHasNoExtraPackets();

Console.WriteLine("LoginProtocolExactCheck PASS body=40 header=exact map=tilde status=708 extras=none");

static void PrepareRuntimeConfig()
{
    var setupPath = Path.Combine(AppContext.BaseDirectory, "!Setup.txt");
    if (!File.Exists(setupPath) || new FileInfo(setupPath).Length == 0)
        File.WriteAllText(setupPath, "[Server]\r\nServerName=LoginProtocolAudit\r\n");
    var commandPath = Path.Combine(AppContext.BaseDirectory, "Command.conf");
    if (!File.Exists(commandPath) || new FileInfo(commandPath).Length == 0)
        File.WriteAllText(commandPath, "[Command]\r\nAudit=Audit\r\n");
    var shareDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "Share"));
    Directory.CreateDirectory(shareDirectory);
    var expPath = Path.Combine(shareDirectory, "PlayerUpgradeExp.ini");
    if (!File.Exists(expPath) || new FileInfo(expPath).Length == 0)
        File.WriteAllText(expPath, "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
    M2Share.ObjectManager ??= new ObjectManager();
}

static void CheckLogonBody()
{
    var player = new TPlayObject
    {
        m_btRaceImg = 7,
        m_btHair = 11,
        m_btGender = PlayGender.WoMan,
        m_nCharStatus = unchecked((int)0x81234567),
        m_nCharStatus2 = 0x10203040,
        m_nCharStatus3 = 0x50607080,
        m_nCharStatus4 = unchecked((int)0x90A0B0C0),
        m_boAllowGroup = true
    };

    var body = (byte[])RequireMethod("BuildLogonBody",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null)!;

    Equal(40, body.Length, "SM_LOGON body size");
    Equal(player.GetFeatureToLong(), BitConverter.ToInt32(body, 0),
        "SM_LOGON outlook");
    Equal(player.m_nCharStatus, BitConverter.ToInt32(body, 4),
        "SM_LOGON body-state 1");
    Equal(player.m_nCharStatus2, BitConverter.ToInt32(body, 8),
        "SM_LOGON body-state 2");
    Equal(player.m_nCharStatus3, BitConverter.ToInt32(body, 12),
        "SM_LOGON body-state 3");
    Equal(player.m_nCharStatus4, BitConverter.ToInt32(body, 16),
        "SM_LOGON body-state 4");
    Equal(1, BitConverter.ToInt32(body, 20), "SM_LOGON allow-group flag");
    Equal(0, BitConverter.ToInt32(body, 24), "SM_LOGON reserved dword");
    BytesEqual(player.GetMobileFeature(), body.AsSpan(28, 10).ToArray(),
        "SM_LOGON TFeature");
    Equal((ushort)0, BitConverter.ToUInt16(body, 38), "SM_LOGON padding");

    player.m_boAllowGroup = false;
    body = (byte[])RequireMethod("BuildLogonBody",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null)!;
    Equal(0, BitConverter.ToInt32(body, 20), "SM_LOGON disabled group flag");
}

static void CheckLogonHeader()
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_nCurrX = 0x1234,
        m_nCurrY = 0x5678,
        m_btDirection = 6,
        m_nLight = 0x7F
    };

    player.SendLogonPublic();
    Equal((ushort)Grobal2.SM_LOGON, player.m_DefMsg.Ident, "SM_LOGON ident");
    Equal(player.ObjectId, player.m_DefMsg.Recog, "SM_LOGON recog");
    Equal((ushort)0x1234, player.m_DefMsg.Param, "SM_LOGON x");
    Equal((ushort)0x5678, player.m_DefMsg.Tag, "SM_LOGON y");
    Equal((ushort)6, player.m_DefMsg.Series, "SM_LOGON direction-only series");
}

static void CheckLogonMapName()
{
    var method = RequireMethod("GetLogonMapName",
        BindingFlags.Static | BindingFlags.NonPublic);
    string Normalize(string value) => (string)method.Invoke(null, new object[] { value })!;

    Equal("3", Normalize("3~instance-17"), "map instance suffix trim");
    Equal("D515", Normalize("D515"), "plain map name");
    Equal(string.Empty, Normalize("~instance"), "leading separator trim");
    Equal(string.Empty, Normalize(null), "null map name");
}

static void CheckMyStatusIdent()
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_PEnvir = new Envirnoment
        {
            Flag = new TMapFlag { boSAFE = true }
        }
    };

    RequireMethod("RefUserState", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(player, null);
    Equal((ushort)Grobal2.SM_MYSTATUS, player.m_DefMsg.Ident,
        "RefUserState native ident");
    Equal((ushort)708, player.m_DefMsg.Ident, "RefUserState wire ident");
}

static void CheckLogonBranchHasNoExtraPackets()
{
    var root = FindRepoRoot();
    var messageSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    var start = messageSource.IndexOf("case Grobal2.RM_LOGON:",
        StringComparison.Ordinal);
    var end = messageSource.IndexOf("case Grobal2.RM_HEAR:", start,
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start, "RM_LOGON source block not found");
    var block = messageSource[start..end];

    Contains(block, "SendLogon();", "RM_LOGON SM_LOGON send");
    Contains(block, "ClientQueryUserName", "RM_LOGON username send");
    Contains(block, "RefUserState();", "RM_LOGON status send");
    Contains(block, "SendMapDescription();", "RM_LOGON map-description send");
    NotContains(block, "RM_CHANGELIGHT", "RM_LOGON extra light message");
    NotContains(block, "SendServerConfig", "RM_LOGON extra server config");
    NotContains(block, "SendSafeZoneInfo", "RM_LOGON extra safe-zone packet");
    NotContains(block, "SendGoldInfo", "RM_LOGON extra gold-name packet");
    NotContains(block, "SM_VERSION_FAIL", "RM_LOGON fake version-fail packet");

    var baseSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Base.cs"));
    start = baseSource.IndexOf("private void SendLogon()", StringComparison.Ordinal);
    end = baseSource.IndexOf("public void UserLogon()", start, StringComparison.Ordinal);
    Assert(start >= 0 && end > start, "SendLogon source block not found");
    block = baseSource[start..end];
    NotContains(block, "SM_ATTACKMODE", "SendLogon extra attack-mode packet");
    NotContains(block, "MakeWord", "SM_LOGON light packed into series");
}

static MethodInfo RequireMethod(string name, BindingFlags flags)
{
    return typeof(TPlayObject).GetMethod(name, flags)
        ?? throw new InvalidOperationException($"Missing method: {name}");
}

static string FindRepoRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory != null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
            return directory.FullName;
    }

    throw new InvalidOperationException("Repository root not found");
}

static void Contains(string value, string expected, string label)
{
    Assert(value.Contains(expected, StringComparison.Ordinal), label);
}

static void NotContains(string value, string unexpected, string label)
{
    Assert(!value.Contains(unexpected, StringComparison.Ordinal), label);
}

static void BytesEqual(byte[] expected, byte[] actual, string label)
{
    Assert(expected.AsSpan().SequenceEqual(actual), label);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
