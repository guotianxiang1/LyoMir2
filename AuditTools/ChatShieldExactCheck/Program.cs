using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

CheckConstantsAndClientMapping();
CheckNativePersistence();
CheckSwitchListen();
CheckHearGate();
CheckClientConfigPacket();
CheckGateHasNoSyntheticConfig();

Console.WriteLine(
    "ChatShieldExactCheck PASS CM3032 categories=1/2/3/4 masks=2/4/8/1 " +
    "native-offset=0x4F8 RM_HEAR-mask=2 SM2953=full-dword gate=no-zero-injection");

static void CheckConstantsAndClientMapping()
{
    Equal(3032, Grobal2.CM_SWITCH_LISTEN, "CM_SWITCH_LISTEN");
    Equal(2953, Grobal2.SM_CLIENT_CONF, "SM_CLIENT_CONF");

    var clientPath = FindClientLuaFixture();
    var client = File.ReadAllText(clientPath);
    Contains(client, "CM_SWITCH_LISTEN,", "client CM3032 producer");
    Contains(client, "recog = b and 0 or 1", "client mode layout");
    Contains(client, "param = config[1]", "client category layout");
}

static void CheckNativePersistence()
{
    // HumanRcd + 0x4F8 <-> player + 0xB9C:
    //   save 0x6B12A0 8B 83 9C 0B 00 00 mov eax,[ebx+0xB9C]
    //        0x6B12A6 89 86 F8 04 00 00 mov [esi+0x4F8],eax
    //   load 0x6B029C 8B 80 F8 04 00 00 mov eax,[eax+0x4F8]
    //        0x6B02A5 89 82 9C 0B 00 00 mov [edx+0xB9C],eax
    const int offset = 0x4F8;
    var player = NewPlayer();
    player.m_NativeHumanData = new byte[offset + sizeof(uint)];
    BinaryPrimitives.WriteUInt32LittleEndian(
        player.m_NativeHumanData.AsSpan(offset, sizeof(uint)), 0xA5F00F0Au);

    Invoke(player, "RestoreNativeChatShieldMask");
    Equal(0xA5F00F0Au, player.m_dwChatShieldMask, "native mask load");

    player.m_dwChatShieldMask = 0x89ABCDEFu;
    Equal(true, (bool)Invoke(player, "PersistNativeChatShieldMask"),
        "native mask save result");
    Equal(0x89ABCDEFu, BinaryPrimitives.ReadUInt32LittleEndian(
        player.m_NativeHumanData.AsSpan(offset, sizeof(uint))),
        "native mask save bytes");

    player.m_NativeHumanData = new byte[offset + sizeof(uint) - 1];
    player.m_dwChatShieldMask = 0;
    Equal(true, (bool)Invoke(player, "PersistNativeChatShieldMask"),
        "short zero record compatibility");
    player.m_dwChatShieldMask = 1;
    Equal(false, (bool)Invoke(player, "PersistNativeChatShieldMask"),
        "short nonzero record rejection");
}

static void CheckSwitchListen()
{
    var player = NewPlayer();
    player.m_dwChatShieldMask = 0xA5F00000u;
    var mappings = new[]
    {
        (Category: 1, Mask: 0x02u),
        (Category: 2, Mask: 0x04u),
        (Category: 3, Mask: 0x08u),
        (Category: 4, Mask: 0x01u)
    };

    foreach (var mapping in mappings)
    {
        Assert(player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_SWITCH_LISTEN,
            nParam1 = 1,
            nParam2 = mapping.Category,
            nParam3 = unchecked((int)0x89ABCDEF),
            wParam = 0x7654
        }), $"category {mapping.Category} set dispatch");
        Assert((player.m_dwChatShieldMask & mapping.Mask) != 0,
            $"category {mapping.Category} set mask");
    }
    Equal(0xA5F0000Fu, player.m_dwChatShieldMask, "all category masks");

    foreach (var mapping in mappings)
    {
        Assert(player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_SWITCH_LISTEN,
            nParam1 = 0,
            nParam2 = mapping.Category
        }), $"category {mapping.Category} clear dispatch");
    }
    Equal(0xA5F00000u, player.m_dwChatShieldMask, "all category clears");

    player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_SWITCH_LISTEN,
        nParam1 = 2,
        nParam2 = 1
    });
    player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_SWITCH_LISTEN,
        nParam1 = 1,
        nParam2 = 5
    });
    Equal(0xA5F00000u, player.m_dwChatShieldMask,
        "unknown mode/category no-op");
}

static void CheckHearGate()
{
    var player = NewPlayer();
    player.m_dwChatShieldMask = 0x02;
    player.m_DefMsg = null;
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_HEAR,
        BaseObject = 0x12345678,
        nParam1 = 0x12,
        nParam2 = 0x34,
        sMsg = "blocked"
    }), "blocked RM_HEAR dispatch");
    Assert(player.m_DefMsg == null, "blocked RM_HEAR packet");

    player.m_dwChatShieldMask = 0x01;
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_HEAR,
        BaseObject = 0x12345678,
        nParam1 = 0x12,
        nParam2 = 0x34,
        sMsg = "allowed"
    }), "allowed RM_HEAR dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_HEAR, 0x12345678,
        0xFF00, 0, 1, "allowed RM_HEAR");
}

static void CheckClientConfigPacket()
{
    var player = NewPlayer();
    player.m_dwChatShieldMask = 0x89ABCDEFu;
    Invoke(player, "SendNativeClientConfig");
    Packet(player.m_DefMsg, Grobal2.SM_CLIENT_CONF,
        unchecked((int)0x89ABCDEFu), 0, 0, 0, "SM_CLIENT_CONF");
}

static void CheckGateHasNoSyntheticConfig()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameGate-CS", "Core",
        "GateServer.cs"));
    NotContains(source, "SM_CLIENT_CONF", "GameGate SM2953 constant/injection");
    NotContains(source, "injected chat shieldMask",
        "GameGate zero chat-mask injection");
}

static ProbePlayer NewPlayer() => new() { m_boOffLineFlag = true };

static object Invoke(object target, string methodName)
{
    var method = target.GetType().BaseType?.GetMethod(methodName,
                     BindingFlags.Instance | BindingFlags.NonPublic)
                 ?? typeof(TPlayObject).GetMethod(methodName,
                     BindingFlags.Instance | BindingFlags.NonPublic)
                 ?? throw new MissingMethodException(methodName);
    return method.Invoke(target, null);
}

static void Packet(ClientPacket packet, int ident, int recog, int param,
    int tag, int series, string label)
{
    Assert(packet != null, label + " packet missing");
    Equal(ident, packet.Ident, label + " Ident");
    Equal(recog, packet.Recog, label + " Recog");
    Equal((ushort)param, packet.Param, label + " Param");
    Equal((ushort)tag, packet.Tag, label + " Tag");
    Equal((ushort)series, packet.Series, label + " Series");
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static string FindClientLuaFixture()
{
    const string leaf = @"白猪G2.5_0518_lua_plain_readable_20260710_014719\core\mir2.scenes.main.common.common_hk.lua";
    foreach (var start in new[]
             {
                 AuditRepoRoot.Resolve(),
                 @"D:\loym2",
                 Environment.CurrentDirectory
             })
    {
        if (string.IsNullOrWhiteSpace(start)) continue;
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, leaf);
            if (File.Exists(candidate))
                return candidate;
        }
    }
    throw new FileNotFoundException("client lua fixture not found", leaf);
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

static void Contains(string source, string value, string label)
{
    Assert(source.Contains(value, StringComparison.Ordinal), label);
}

static void NotContains(string source, string value, string label)
{
    Assert(!source.Contains(value, StringComparison.Ordinal), label);
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
}
