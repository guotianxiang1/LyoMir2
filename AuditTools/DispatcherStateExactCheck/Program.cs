using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.CastleManager = new CastleManager();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

CheckQueuedCharStatus();
var equippedItem = CheckDynamicUserStateBody();
CheckQueryUserStateHeader(equippedItem);
CheckExactSourceContracts();

Console.WriteLine(
    "DispatcherStateExactCheck PASS status=657/16 userstate=751/56+dynamic");

static void CheckQueuedCharStatus()
{
    var player = new ProbePlayer
    {
        m_boOffLineFlag = true,
        m_boObMode = true,
        m_PEnvir = new Envirnoment()
    };
    player.SetStatus(0x1357, unchecked((int)0x89ABCDEF),
        0x10203040, unchecked((int)0xFFEEDDCC), 0x55667788);
    player.StatusChanged();

    TProcessMessage queued = null;
    Assert(player.TryTake(ref queued), "RM_CHARSTATUSCHANGED was not queued");
    Equal(Grobal2.RM_CHARSTATUSCHANGED, queued.wIdent, "queued ident");
    Equal(0x1357, queued.wParam, "queued wParam");
    Equal(unchecked((int)0x89ABCDEF), queued.nParam1, "queued nParam1");

    var body = queued.Payload as byte[];
    Assert(body != null, "queued status body type");
    Equal(16, body.Length, "queued status body size");
    Equal(unchecked((int)0x89ABCDEF),
        BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, 4)), "status word 0");
    Equal(0x10203040,
        BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4, 4)), "status word 1");
    Equal(unchecked((int)0xFFEEDDCC),
        BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8, 4)), "status word 2");
    Equal(0x55667788,
        BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12, 4)), "status word 3");

    player.SetStatus(1, 2, 3, 4, 5);
    Equal(unchecked((int)0x89ABCDEF),
        BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, 4)),
        "queued status snapshot changed with actor");

    Assert(player.Operate(queued), "RM_CHARSTATUSCHANGED dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_CHARSTATUSCHANGED, player.ObjectId,
        0x1357, 0x89AB, 0xCDEF, "RM_CHARSTATUSCHANGED");
}

static TUserItem CheckDynamicUserStateBody()
{
    M2Share.UserEngine.StdItemList.Clear();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "wire-item",
        ItemType = GoodType.ITEM_WEAPON,
        StdMode = 5,
        Ac = 10,
        Ac2 = 20,
        Dc = 30,
        Dc2 = 40
    });
    var item = new TUserItem
    {
        MakeIndex = 0x11223344,
        ClientItemID = 0x55667788,
        wIndex = 1,
        Dura = 0x1234,
        DuraMax = 0x5678,
        Bind = 1,
        ys1 = 0x12345678,
        ys2 = 9
    };
    item.jp1 = 3;

    const int equippedSlot = 6;
    var useItems = new TUserItem[16];
    useItems[equippedSlot] = item;
    var stateEncoder = typeof(TPlayObject).GetMethod("EncodeClientUserState",
        BindingFlags.Static | BindingFlags.NonPublic);
    var itemEncoder = typeof(TPlayObject).GetMethod("EncodeClientItemRecord",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert(stateEncoder != null && itemEncoder != null, "user-state encoder reflection");

    var actual = (byte[])stateEncoder.Invoke(null, new object[]
    {
        0x12345678, "角色甲", (ushort)9, "行会甲", "职位甲", useItems
    });
    var itemRecord = (byte[])itemEncoder.Invoke(null, new object[] { item });

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(0x12345678);
    WriteShortString(writer, "角色甲", 15);
    writer.Write((byte)9);
    writer.Write(new byte[3]);
    WriteShortString(writer, "行会甲", 15);
    WriteShortString(writer, "职位甲", 15);
    for (var slot = 0; slot < 16; slot++)
        writer.Write(slot == equippedSlot ? itemRecord : new byte[16]);

    var expected = stream.ToArray();
    Assert(expected.SequenceEqual(actual), "56B+dynamic user-state byte stream");
    Equal(56 + 15 * 16 + itemRecord.Length, actual.Length,
        "dynamic user-state body size");
    Assert(itemRecord.Length > 16, "equipped item was not dynamically encoded");
    return item;
}

static void CheckQueryUserStateHeader(TUserItem equippedItem)
{
    var map = new Envirnoment();
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(map, new object[] { (short)16, (short)16 });
    var requester = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_PEnvir = map,
        m_nCurrX = 5,
        m_nCurrY = 5,
        m_sCharName = "requester"
    };
    var target = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_PEnvir = map,
        m_nCurrX = 6,
        m_nCurrY = 5,
        m_sCharName = "target"
    };
    target.m_UseItems[6] = equippedItem;
    // AddToMap, not MoveToMovingObject: the original's mover sub_7797CC only reports
    // success from 0x779A95, which is reached after unlinking the actor from the
    // SOURCE cell. Asking it to move an actor out of a cell it was never in walks the
    // empty list and falls through to `xor eax,eax` @0x779AAD, i.e. FALSE. A first
    // placement has no source cell, so the mover is the wrong primitive for it.
    Assert(ReferenceEquals(requester, map.AddToMap(5, 5,
        CellType.OS_MOVINGOBJECT, requester)), "place requester");
    Assert(ReferenceEquals(target, map.AddToMap(6, 5,
        CellType.OS_MOVINGOBJECT, target)), "place target");

    Assert(requester.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_QUERYUSERSTATE,
        nParam1 = target.ObjectId,
        nParam2 = 6,
        nParam3 = 5
    }), "CM_QUERYUSERSTATE dispatcher result");
    // Delphi's register convention pushes the stack tail left-to-right
    // (Param, Tag, Series, Buf, Len), so the literal 1 is Series:
    //   0x6B7119 6A 00 push 0            ; Param
    //   0x6B711B 6A 00 push 0            ; Tag
    //   0x6B711D 6A 01 push 1            ; Series
    //   0x6B711F 8B 45 F8 / 50 push [ebp-8]   ; Buf
    //   0x6B712D 50   push eax           ; Len
    //   0x6B712E 33 C9 xor ecx,ecx       ; Recog = 0
    //   0x6B7130 66 BA EF 02 mov dx,0x2EF ; 751
    Packet(requester.m_DefMsg, Grobal2.SM_SENDUSERSTATE, 0, 0, 0, 1,
        "SM_SENDUSERSTATE");
    Equal(751, Grobal2.SM_SENDUSERSTATE, "SM_SENDUSERSTATE ident");
}

static void CheckExactSourceContracts()
{
    var root = FindRepoRoot();
    var dispatchSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    var statusBlock = CaseBlock(dispatchSource, "RM_CHARSTATUSCHANGED", "RM_GROUPCANCEL");
    var normalizedStatus = Normalize(statusBlock);
    Contains(normalizedStatus,
        "MakeDefaultMsg(Grobal2.SM_CHARSTATUSCHANGED, ProcessMsg.BaseObject, ProcessMsg.wParam, HUtil32.HiWord(ProcessMsg.nParam1), HUtil32.LoWord(ProcessMsg.nParam1))",
        "status header tuple");
    Contains(normalizedStatus,
        "ProcessMsg.Payload as byte[] ?? Array.Empty<byte>()",
        "queued status body forwarding");
    NotContains(statusBlock, "GetBodyStateBuffer", "late status-body reconstruction");

    var operateSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Operate.cs"));
    var queryBlock = Between(operateSource, "private void ClientQueryUserState",
        "internal static byte[] EncodeClientUserState");
    var normalizedQuery = Normalize(queryBlock);
    // 0x6B7119 push 0 (Param) / 0x6B711B push 0 (Tag) / 0x6B711D push 1 (Series)
    Contains(normalizedQuery,
        "MakeDefaultMsg(Grobal2.SM_SENDUSERSTATE, 0, 0, 0, 1)",
        "SM_SENDUSERSTATE fixed header");
    NotContains(queryBlock, "TOUserStateInfo", "legacy 989B user-state path");
    NotContains(queryBlock, "EDcode.EncodeBuffer", "legacy user-state serializer");
    NotContains(queryBlock, "m_nSoftVersionDateEx", "legacy client branch");
}

static string CaseBlock(string source, string startCase, string endCase) =>
    Between(source, "case Grobal2." + startCase + ":",
        "case Grobal2." + endCase + ":");

static string Between(string source, string startText, string endText)
{
    var start = source.IndexOf(startText, StringComparison.Ordinal);
    var end = source.IndexOf(endText, start + Math.Max(startText.Length, 0),
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start, startText + " source block");
    return source[start..end];
}

static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();

static void WriteShortString(BinaryWriter writer, string value, int maxBytes)
{
    var bytes = HUtil32.GbkEncoding.GetBytes(value);
    writer.Write((byte)bytes.Length);
    writer.Write(bytes);
    writer.Write(new byte[maxBytes - bytes.Length]);
}

static void Packet(ClientPacket packet, int ident, int recog, int param, int tag,
    int series, string label)
{
    Assert(packet != null, label + " packet");
    Equal((ushort)ident, packet.Ident, label + " ident");
    Equal(recog, packet.Recog, label + " recog");
    Equal(unchecked((ushort)param), packet.Param, label + " param");
    Equal(unchecked((ushort)tag), packet.Tag, label + " tag");
    Equal(unchecked((ushort)series), packet.Series, label + " series");
}

// The sweep harness runs the exe out of a shared Build tree OUTSIDE the checkout
// (OutputPath ..\..\..\Build\AuditTools\...), so neither the CWD nor the base directory has
// the solution above it. Fall back to where this file was compiled from.
static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "")
{
    foreach (var startPath in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory,
                 string.IsNullOrEmpty(sourcePath) ? null : Path.GetDirectoryName(sourcePath)
             })
    {
        if (string.IsNullOrEmpty(startPath)) continue;
        for (var directory = new DirectoryInfo(startPath);
             directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }
    throw new InvalidOperationException("Repository root not found");
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

static void Contains(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static void NotContains(string source, string value, string label) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), label);

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
    public void SetStatus(ushort hitSpeed, int state1, int state2, int state3,
        int state4)
    {
        m_nHitSpeed = hitSpeed;
        m_nCharStatus = state1;
        m_nCharStatus2 = state2;
        m_nCharStatus3 = state3;
        m_nCharStatus4 = state4;
    }

    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}
