using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();

    CheckNativeContracts();
    CheckNativePersistence();
    CheckSwitchListen();
    CheckHearGate();
    CheckClientConfigPacket();
    CheckGateHasNoSyntheticConfig();
    CheckWhisperBit0Gate();

    Console.WriteLine(
        "ChatShieldExactCheck PASS CM3032 categories=1/2/3/4 masks=2/4/8/1 " +
        "native-offset=0x4F8 RM_HEAR-mask=2 RM_WHISPER-mask=1 " +
        "SM2953=full-dword slot=+0x250 gate=no-zero-injection");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ChatShieldExactCheck FAIL: {exception.Message}");
    return 1;
}

static void CheckNativeContracts()
{
    Equal(3032, Grobal2.CM_SWITCH_LISTEN, "CM_SWITCH_LISTEN");
    Equal(2953, Grobal2.SM_CLIENT_CONF, "SM_CLIENT_CONF");
    var persistOffset = typeof(TPlayObject).GetField("NativeChatShieldMaskOffset",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException("NativeChatShieldMaskOffset");
    Equal(0x4F8, (int)persistOffset.GetValue(null)!, "persist offset");

    var image = File.ReadAllBytes(FindNativeImage());
    const int imageBase = 0x400000;
    Pin(image, imageBase, 0x6BBAAF, [0x80, 0xA3, 0x9C, 0x0B, 0x00, 0x00, 0xFD],
        "0x6BBAAF cat1 clear bit1");
    Pin(image, imageBase, 0x6BBABD, [0x80, 0xA3, 0x9C, 0x0B, 0x00, 0x00, 0xFB],
        "0x6BBABD cat2 clear bit2");
    Pin(image, imageBase, 0x6BBACB, [0x80, 0xA3, 0x9C, 0x0B, 0x00, 0x00, 0xF7],
        "0x6BBACB cat3 clear bit3");
    Pin(image, imageBase, 0x6BBAD9, [0x80, 0xA3, 0x9C, 0x0B, 0x00, 0x00, 0xFE],
        "0x6BBAD9 cat4 clear bit0");
    Pin(image, imageBase, 0x6BBAEA, [0x80, 0x8B, 0x9C, 0x0B, 0x00, 0x00, 0x02],
        "0x6BBAEA cat1 set bit1");
    Pin(image, imageBase, 0x6BBAF8, [0x80, 0x8B, 0x9C, 0x0B, 0x00, 0x00, 0x04],
        "0x6BBAF8 cat2 set bit2");
    Pin(image, imageBase, 0x6BBB06, [0x80, 0x8B, 0x9C, 0x0B, 0x00, 0x00, 0x08],
        "0x6BBB06 cat3 set bit3");
    Pin(image, imageBase, 0x6BBB14, [0x80, 0x8B, 0x9C, 0x0B, 0x00, 0x00, 0x01],
        "0x6BBB14 cat4 set bit0");
    Pin(image, imageBase, 0x6B12A0, [0x8B, 0x83, 0x9C, 0x0B, 0x00, 0x00],
        "0x6B12A0 save load [ebx+0xB9C]");
    Pin(image, imageBase, 0x6B12A6, [0x89, 0x86, 0xF8, 0x04, 0x00, 0x00],
        "0x6B12A6 save [esi+0x4F8]");
    Pin(image, imageBase, 0x6B029C, [0x8B, 0x80, 0xF8, 0x04, 0x00, 0x00],
        "0x6B029C load [rec+0x4F8]");
    Pin(image, imageBase, 0x6B02A5, [0x89, 0x82, 0x9C, 0x0B, 0x00, 0x00],
        "0x6B02A5 load [edx+0xB9C]");
    Pin(image, imageBase, 0x6B2D1D, [0x8B, 0x8B, 0x9C, 0x0B, 0x00, 0x00],
        "0x6B2D1D SM2953 Recog=[ebx+0xB9C]");
    Pin(image, imageBase, 0x6B2D23, [0x66, 0xBA, 0x89, 0x0B],
        "0x6B2D23 mov dx,0xB89");
    Pin(image, imageBase, 0x6B2D2B, [0xFF, 0x93, 0x50, 0x02, 0x00, 0x00],
        "0x6B2D2B call [ebx+0x250]");
    Pin(image, imageBase, 0x6B4A63, [0xF6, 0x80, 0x9C, 0x0B, 0x00, 0x00, 0x02],
        "0x6B4A63 RM_HEAR test bit1");
    Pin(image, imageBase, 0x6C9584, [0xF6, 0x87, 0x9C, 0x0B, 0x00, 0x00, 0x01],
        "0x6C9584 whisper test bit0");
}

static void CheckNativePersistence()
{
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
        HUtil32.MakeWord(0x12, 0x34), 0, 1, "allowed RM_HEAR");
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

static void CheckWhisperBit0Gate()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Chat.cs"));
    Contains(source, "m_dwChatShieldMask & 0x01u", "whisper target bit0 gate");
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
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
            var sibling = Path.Combine(directory.FullName, "LyoMir2-master");
            if (File.Exists(Path.Combine(sibling, "LyoMir2.sln")))
                return sibling;
        }
    }
    throw new InvalidOperationException("Repository root not found");
}

static string FindNativeImage()
{
    const string known = @"D:\loym2\staging\_reunpack_work\flat_image.bin";
    if (File.Exists(known))
        return known;
    throw new InvalidOperationException("flat_image.bin not found at " + known);
}

static void Pin(byte[] image, int imageBase, int va, byte[] expected, string label)
{
    var offset = va - imageBase;
    Assert(offset >= 0 && offset + expected.Length <= image.Length, label + " range");
    for (var i = 0; i < expected.Length; i++)
    {
        if (image[offset + i] != expected[i])
            throw new InvalidOperationException(
                $"{label}: byte[{i}] expected={expected[i]:X2} actual={image[offset + i]:X2}");
    }
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
