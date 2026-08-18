// Pins 战神's CM body-length contract, which C# had no carrier for at all until now.
//
// The dispatcher sub_6D7D68 takes FOUR parameters, and the fourth is the wire body length.
// Its only call site builds it out of the queued packet node:
//   0x6B1B11  0F B7 73 08  movzx esi, word [node+8]   ; total wire length
//   0x6B1B15  83 EE 0C     sub   esi, 0x0C            ; minus the 12-byte header
//   0x6B1B2C  56           push  esi                  ; -> [ebp+8]
//   0x6B1B36  E8 2D 62 02 00  call 0x6D7D68
// and the dispatcher keeps a zero-extended low-word copy in EDI at 0x6D7DA8 `0F B7 FE`.
//
// 39 of the 311 real handlers branch on it. Every failing arm lands on 0x6DBC2C (or a
// byte-identical inline copy of it), which is `33 C0 / 5A / 59 / 59 / 64 89 10 /
// E9 D5 00 00 00 -> 0x6DBD0E`: drop the packet, return False, no reply, no side effect.
//
// This tool asserts three separate things:
//   1. the 39 gate sites still read those exact bytes in flat_image.bin, and the byte
//      strings recorded in NativeClientBodyLengthGate match the image (so the table
//      cannot drift away from the binary it claims to quote);
//   2. the predicate semantics, including the low-16-bit truncation both `si` and
//      `movzx edi,si` impose;
//   3. the live ingress: ProcessUserMessage drops short packets before they can be
//      queued, and stamps the surviving ones with nBodyLen so the handler can read it.

using GameSvr;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

var asserts = 0;

CheckDispatcherParameterSetup();
CheckGateBytesAgainstImage();
CheckTableShape();
CheckPredicateSemantics();
CheckIngressDropsShortPackets();
CheckIngressStampsLength();
CheckUngatedIdentsAreUntouched();

Console.WriteLine($"NativeClientBodyLengthGateCheck PASS gates=39 asserts={asserts}");

// ---------------------------------------------------------------------------
// 1. the native side
// ---------------------------------------------------------------------------

void CheckDispatcherParameterSetup()
{
    var image = LoadNativeImage();

    // The whole fourth-parameter construction at the single call site, in one pin:
    // movzx esi,[node+8] / sub esi,0xC / test esi,esi / jle / body=buf+12 / else nil /
    // push esi / edx=buf / ecx=body / eax=self / call 0x6D7D68.
    Pin(image, 0x006B1B11,
        "0FB7730883EE0C85F67E0B8B430483C00C8945F8EB0533C08945F8568B53048B4DF88B45FCE82D620200",
        "0x6B1B11 dispatcher call site: body length = wireTotal - 12");

    // Dispatcher prologue: which register is which.
    Pin(image, 0x006D7D7E, "894DF8", "0x6D7D7E ecx -> [ebp-8] (body pointer)");
    Pin(image, 0x006D7D81, "8BDA", "0x6D7D81 edx -> ebx (header record)");
    Pin(image, 0x006D7D83, "8945FC", "0x6D7D83 eax -> [ebp-4] (self)");
    Pin(image, 0x006D7D86, "8B7508", "0x6D7D86 [ebp+8] -> esi (body length)");
    Pin(image, 0x006D7DA8, "0FB7FE", "0x6D7DA8 movzx edi,si (low word copy)");

    // The shared drop stub every failing gate lands on.
    Pin(image, 0x006DBC2C, "33C05A5959648910E9D5000000",
        "0x6DBC2C drop stub: xor eax,eax -> 0x6DBD0E");
    // ...and one of the inline copies the `jae` arms fall through into, to show the two
    // shapes are the same behaviour and not two different outcomes.
    Pin(image, 0x006DB3C1, "33C05A5959648910E940090000",
        "0x6DB3C1 inline copy of the drop stub (ident 4522 short arm)");
}

void CheckGateBytesAgainstImage()
{
    var image = LoadNativeImage();
    foreach (var (ident, rule) in NativeClientBodyLengthGate.Rules)
    {
        var recorded = rule.GateBytes.Replace(" ", string.Empty).Replace("/", string.Empty);
        Pin(image, rule.GateVa, recorded, $"ident {ident} gate bytes at 0x{rule.GateVa:X6}");
    }
}

// ---------------------------------------------------------------------------
// 2. the table and the predicate
// ---------------------------------------------------------------------------

void CheckTableShape()
{
    int[] expected =
    {
        1011, 1014, 1015, 1020, 1021, 1022, 1026, 1027, 1034, 1043, 1048, 1061, 1080,
        1350, 1355, 3030, 3306, 3410, 4522, 4525, 4527, 4528, 4529, 4535, 4536, 4540,
        4560, 4567, 4568, 4569, 4572, 4573, 4574, 4576, 4578, 4579, 4611, 4616, 4617,
    };
    var actual = NativeClientBodyLengthGate.Rules.Keys.OrderBy(v => v).ToArray();
    Equal(expected.Length, actual.Length, "gated ident count");
    for (var i = 0; i < expected.Length; i++)
        Equal(expected[i], actual[i], $"gated ident #{i}");

    // Bounds, spot-checked against the disassembly quoted in each Rule.
    Equal(0x20, NativeClientBodyLengthGate.Rules[1350].Bound, "1350 bound");
    Equal(0x0C, NativeClientBodyLengthGate.Rules[1355].Bound, "1355 bound");
    Equal(4, NativeClientBodyLengthGate.Rules[3306].Bound, "3306 bound");
    Equal(0x28, NativeClientBodyLengthGate.Rules[3410].Bound, "3410 bound");
    Equal(NativeClientBodyLengthGate.GateKind.Exactly,
        NativeClientBodyLengthGate.Rules[3410].Kind, "3410 is an equality gate");
    Equal(NativeClientBodyLengthGate.GateKind.NonEmpty,
        NativeClientBodyLengthGate.Rules[3030].Kind, "3030 is a non-empty gate");
}

void CheckPredicateSemantics()
{
    // test si,si / jbe -> zero is the only rejection.
    Assert(!NativeClientBodyLengthGate.Allows(3030, 0), "CM_SAY empty body must drop");
    Assert(NativeClientBodyLengthGate.Allows(3030, 1), "CM_SAY 1-byte body must pass");

    // cmp edi,K / jb -> K-1 rejected, K accepted.
    Assert(!NativeClientBodyLengthGate.Allows(1350, 0x1F), "1350 at 31 must drop");
    Assert(NativeClientBodyLengthGate.Allows(1350, 0x20), "1350 at 32 must pass");
    Assert(!NativeClientBodyLengthGate.Allows(4522, 7), "4522 at 7 must drop");
    Assert(NativeClientBodyLengthGate.Allows(4522, 8), "4522 at 8 must pass");

    // cmp edi,0x28 / jne -> both sides rejected.
    Assert(!NativeClientBodyLengthGate.Allows(3410, 0x27), "3410 at 39 must drop");
    Assert(NativeClientBodyLengthGate.Allows(3410, 0x28), "3410 at 40 must pass");
    Assert(!NativeClientBodyLengthGate.Allows(3410, 0x29), "3410 at 41 must drop");

    // Both `si` and `movzx edi,si` see only the low 16 bits, so 0x10000 reads as 0.
    Assert(!NativeClientBodyLengthGate.Allows(3030, 0x10000),
        "0x10000 must truncate to 0 for a si gate");
    Assert(!NativeClientBodyLengthGate.Allows(4522, 0x10004),
        "0x10004 must truncate to 4 for an edi gate");
    Assert(NativeClientBodyLengthGate.Allows(4522, 0x10008),
        "0x10008 must truncate to 8 for an edi gate");

    // Ungated idents are always allowed.
    Assert(NativeClientBodyLengthGate.Allows(Grobal2.CM_QUERYUSERNAME, 0),
        "an ungated ident must not acquire a gate");
    Assert(NativeClientBodyLengthGate.Allows(Grobal2.CM_WALK, 0),
        "CM_WALK must not acquire a gate");
}

// ---------------------------------------------------------------------------
// 3. the live ingress
// ---------------------------------------------------------------------------

void CheckIngressDropsShortPackets()
{
    // One case per gate shape, driven through the real ProcessUserMessage.
    DropsAt(Grobal2.CM_SAY, 0);                       // test si,si
    DropsAt(Grobal2.CM_MERCHANTDLGSELECT, 0);         // test si,si, cased arm
    DropsAt(Grobal2.CM_DOSHOP, 0);                    // test si,si, 290k live packets/day
    DropsAt(1350, 0x1F);                              // cmp edi,0x20
    DropsAt(1355, 0x0B);                              // cmp edi,0x0C
    DropsAt(3306, 3);                                 // cmp si,4
    DropsAt(3410, 0x27);                              // cmp edi,0x28 (short side)
    DropsAt(3410, 0x29);                              // cmp edi,0x28 (long side)
    DropsAt(Grobal2.CM_CORPS_REQUEST_JOIN, 7);        // cmp edi,8 / jae
    DropsAt(Grobal2.CM_CORPS_ACCEPT_REQUEST, 7);      // cmp edi,8 / jb
    DropsAt(Grobal2.CM_FIND_GILD_BYNAME, 0);          // test si,si
}

void CheckIngressStampsLength()
{
    PassesAt(Grobal2.CM_SAY, 1);
    PassesAt(Grobal2.CM_MERCHANTDLGSELECT, 1);
    PassesAt(Grobal2.CM_DOSHOP, 12);
    PassesAt(1350, 0x20);
    PassesAt(1355, 0x0C);
    PassesAt(3306, 4);
    PassesAt(3410, 0x28);
    PassesAt(Grobal2.CM_CORPS_REQUEST_JOIN, 8);
    PassesAt(Grobal2.CM_CORPS_ACCEPT_REQUEST, 8);
    PassesAt(Grobal2.CM_FIND_GILD_BYNAME, 1);
}

void CheckUngatedIdentsAreUntouched()
{
    // A bodyless ungated ident still has to reach the queue, with nBodyLen 0.
    var player = NewPlayer();
    M2Share.UserEngine.ProcessUserMessage(player,
        new ClientPacket { Ident = Grobal2.CM_QUERYUSERNAME, Recog = 7 }, string.Empty);
    var queued = Take(player, "ungated ident was dropped");
    Equal(Grobal2.CM_QUERYUSERNAME, queued.wIdent, "ungated ident survived");
    Equal(0, queued.nBodyLen, "ungated bodyless packet nBodyLen");
}

void DropsAt(int ident, int bodyLength)
{
    var player = NewPlayer();
    Feed(player, ident, bodyLength);
    Equal(0, player.m_MsgList.Count,
        $"ident {ident} with a {bodyLength}-byte body must be dropped at the dispatcher");
}

void PassesAt(int ident, int bodyLength)
{
    var player = NewPlayer();
    Feed(player, ident, bodyLength);
    var queued = Take(player, $"ident {ident} with a {bodyLength}-byte body was dropped");
    Equal(ident, queued.wIdent, $"ident {ident} queued ident");
    Equal(bodyLength, queued.nBodyLen,
        $"ident {ident} nBodyLen did not survive the queue hop");
}

void Feed(ProbePlayer player, int ident, int bodyLength)
{
    // GateService builds `payload` as exactly MsgBuff[12 .. nMsgLen] and only when
    // nMsgLen > 12, so a zero-length body arrives as null, not as an empty array.
    var payload = bodyLength == 0 ? null : new byte[bodyLength];
    for (var i = 0; i < bodyLength; i++) payload[i] = (byte)('A' + (i % 26));
    M2Share.UserEngine.ProcessUserMessage(player,
        new ClientPacket { Ident = (ushort)ident, Recog = 1, Param = 2, Tag = 3, Series = 4 },
        payload == null ? string.Empty : "x", payload);
}

TProcessMessage Take(ProbePlayer player, string label)
{
    TProcessMessage message = null;
    Assert(player.TryTake(ref message), label);
    return message;
}

ProbePlayer NewPlayer()
{
    var player = new ProbePlayer
    {
        m_boOffLineFlag = false,
        m_boGhost = false,
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
    return player;
}

// ---------------------------------------------------------------------------

static byte[] LoadNativeImage()
{
    const string known = @"D:\loym2\staging\_reunpack_work\flat_image.bin";
    if (!File.Exists(known))
        throw new InvalidOperationException("flat_image.bin not found at " + known);
    return File.ReadAllBytes(known);
}

void Pin(byte[] image, int va, string expectedHex, string label)
{
    const int imageBase = 0x400000;
    var offset = va - imageBase;
    var expected = Convert.FromHexString(expectedHex);
    Assert(offset >= 0 && offset + expected.Length <= image.Length, label + " range");
    for (var i = 0; i < expected.Length; i++)
    {
        if (image[offset + i] != expected[i])
            throw new InvalidOperationException(
                $"{label}: byte[{i}] at 0x{va + i:X6} expected={expected[i]:X2} " +
                $"actual={image[offset + i]:X2}");
    }
    asserts++;
}

void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
    asserts++;
}

void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    asserts++;
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

sealed class ProbePlayer : TPlayObject
{
    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}
