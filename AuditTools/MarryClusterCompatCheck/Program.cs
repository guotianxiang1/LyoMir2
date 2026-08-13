using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using DBSvr.Core;
using SystemModule;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936);
var root = FindRepositoryRoot();
var bridge = Read("GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
var playerBase = Read("GameSvr", "Players", "TPlayObject.Base.cs");
var humanData = Read("SystemModule", "Packet", "THumDataInfo.cs");
var codec = Read("DBSvr", "Core", "NativeHumanDataCodec.cs");
var loader = Read("GameSvr", "UsrSystem", "UsrEngn.cs");
var saver = Read("GameSvr", "Players", "TPlayObject.cs");
var playerMessage = Read("GameSvr", "Players", "TPlayObject.Message.cs");
var frameCodec = Read("SystemModule", "Packet",
    "NativeMasterRelationFrameCodec.cs");
var dbProtocol = Read("DBSvr", "Core", "NativeMasterRelationProtocol.cs");
var logicalCache = Read("DBSvr", "Core", "NativeHumanLogicalCache.cs");
var gameSoc = Read("DBSvr", "Services", "GameSocService.cs");
var mirrorMessage = Read("GameSvr", "Snaps", "MirrorMessage.cs");
var global = Read("SystemModule", "Grobal2.cs");

CheckMarriagePersistence();
CheckOfflineMarriageFrame();
CheckOfflineLogicalMutation();
CheckOtherServerDivorce();
CheckPasSurface();

Console.WriteLine(
    "MarryClusterCompatCheck PASS agree=exact npcdiv=online-offline-crossserver "
    + "checkmarry=npc-int persistence=raw-0xDB+social-block@0x650 db=0152-subtype0");
return;

void CheckMarriagePersistence()
{
    Assert(NativeHumanDataCodec.AllowMarryFlagOffset == 0x00D8,
        "allow-marriage offset mismatch");
    Assert(NativeHumanDataCodec.MarriedFlagOffset == 0x00DB,
        "married offset mismatch");
    // 2026-08-07: the spouse name is NOT a ShortString at 0x650.  战神 keeps all
    // social state in ONE opaque block that it copies whole, both ways:
    //   load 0x6B096C  lea esi,[rec+0x658]; mov ecx,0x20; rep movsd -> obj+0xc48
    //   save 0x6B167E  lea edi,[rec+0x658]; lea esi,[obj+0xc48];      rep movsd
    // Reading ShortStrings at 0x650/0x660/0x680 instead made TryDecode reject
    // 30/30 real DBServer-written records (raw[0x680] is ':' = 58 as a length),
    // and the 0x680 write truncated the real string at 0x670.  This audit
    // asserted the fabricated model against itself, so it was false-green.
    Assert(NativeHumanDataCodec.NativeSocialBlockOffset == 0x0650,
        "social block offset mismatch");
    Assert(NativeHumanDataCodec.NativeSocialBlockLength == 0x80,
        "social block length mismatch");

    var blob = new byte[NativeHumanDataCodec.DataRecordSize + 8];
    BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4),
        NativeHumanDataCodec.DataRecordSize);
    var raw = blob.AsSpan(8);
    raw[0x3E] = 1;
    raw[NativeHumanDataCodec.AllowMarryFlagOffset] = 1;
    raw[NativeHumanDataCodec.MarriedFlagOffset] = 1;
    // Shape the social region like a MARRIED player with a master, exactly the
    // way 战神 lays it out: a spouse-name ShortString[15] in slot 0x650, a
    // master-name ShortString[15] in slot 0x660, and the external ':'/'$'
    // companion string in slot 0x670.  That companion string is 26 bytes — it
    // overflows its 16-byte slot into 0x680 (student[0]), precisely as in all 30
    // real records; the byte the old code read as a student-name length at 0x680
    // is a ':' (0x3A = 58), which is what crashed the codec.
    WriteShortString(raw, NativeHumanDataCodec.DearNameOffset, 15, "丈夫甲");
    WriteShortString(raw, NativeHumanDataCodec.MasterNameOffset, 15, "师父乙");
    var socialFixture = gbk.GetBytes("$:::::::::::::::::::::$0$0$");
    raw[0x0670] = (byte)socialFixture.Length;
    socialFixture.CopyTo(raw.Slice(0x0671));
    raw[NativeHumanDataCodec.NativeSocialBlockOffset - 1] = 0xA5;
    var socialBefore = raw.Slice(
        NativeHumanDataCodec.NativeSocialBlockOffset,
        NativeHumanDataCodec.NativeSocialBlockLength).ToArray();

    Assert(NativeHumanDataCodec.TryDecode(blob, null, out var decoded,
            out var error), "native marriage decode failed: " + error);
    Assert(decoded.Data.boAllowMarry && decoded.Data.boMarried,
        "native marriage flags were not decoded");
    // The block must arrive VERBATIM, including the companion length byte at 0x670
    // and its overflow into 0x680 — this is the assertion the old 0x680
    // ShortString write could never have satisfied.
    Assert(decoded.Data.NativeSocialBlob != null
           && decoded.Data.NativeSocialBlob.AsSpan()
               .SequenceEqual(socialBefore),
        "social block was not decoded verbatim");
    // Spouse and master names ARE now DERIVED from their proven block slots
    // (0x650/0x660; RE writes 0x6C5608/0x6CA9E2 spouse, 0x6C58A0 master).  These
    // are real GBK names and must round-trip exactly.
    Assert(decoded.Data.sDearName == "丈夫甲",
        "spouse name was not decoded from block slot 0x650");
    Assert(decoded.Data.sMasterName == "师父乙",
        "master name was not decoded from block slot 0x660");
    // The external ':'/'$' companion string overflows into student[0] (0x680) but
    // is NOT a valid 15-byte ShortString, so the tolerant reader must yield "" for
    // every student — never a fabricated name from the un-reverse-engineered
    // ':'/'$' grammar.  (Native only keeps names up to btStudentCount, which is 0
    // here and in all 30 real records.)
    Assert(decoded.Data.sStudentNames != null
           && decoded.Data.sStudentNames.Length == 5,
        "student name array shape changed");
    foreach (var studentName in decoded.Data.sStudentNames)
        Assert(string.IsNullOrEmpty(studentName),
            "companion overflow was mis-parsed as a student name");

    decoded.Data.boMarried = false;
    Assert(NativeHumanDataCodec.TryEncode(decoded, out var encoded,
            out var script, out error),
        "native marriage encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(encoded, script, out var roundTrip,
            out error), "native marriage round trip failed: " + error);
    Assert(!roundTrip.Data.boMarried,
        "marriage round trip changed the married flag");
    // The whole 120-byte social region survives byte-for-byte.  This is the
    // assertion the old 0x680 ShortString write could never have satisfied.
    Assert(roundTrip.NativeData.AsSpan(
                NativeHumanDataCodec.NativeSocialBlockOffset,
                NativeHumanDataCodec.NativeSocialBlockLength)
            .SequenceEqual(socialBefore),
        "marriage encode corrupted the opaque social block");
    Assert(roundTrip.NativeData[
               NativeHumanDataCodec.NativeSocialBlockOffset - 1] == 0xA5,
        "marriage encode wrote below the social block");

    roundTrip.PrepareForTransport();
    var payload = ProtoBufDecoder.Serialize(roundTrip);
    var transported = ProtoBufDecoder.DeSerialize<THumDataInfo>(payload);
    Assert(transported?.Data != null && !transported.Data.boMarried,
        "protobuf transport lost marriage state");
    Assert(transported.Data.NativeSocialBlob != null
           && transported.Data.NativeSocialBlob.AsSpan()
               .SequenceEqual(socialBefore),
        "protobuf transport lost the opaque social block");

    Require(humanData, "[ProtoMember(79)]", "married protobuf field changed");
    Require(humanData, "public bool boMarried;",
        "married protobuf field is missing");
    Require(humanData, "[ProtoMember(80, OverwriteList = true)]",
        "native social block protobuf field missing");
    Require(humanData, "public byte[] NativeSocialBlob;",
        "native social blob field missing");
    Require(codec, "MarriedFlagOffset = 0x00DB;",
        "married raw flag offset changed");
    Require(codec, "NativeSocialBlockOffset = 0x0650",
        "social block base offset changed");
    Require(codec, "NativeSocialBlockLength = MagicBase - NativeSocialBlockOffset",
        "social block length formula changed");
    // DearNameOffset = 0x0650 INTENTIONALLY NOT REQUIRED: that constant is
    // kept for compilation only (NativeHumanLogicalCache still references it,
    // and that path is tracked as a separate fix).  The codec must NOT read or
    // write ShortStrings at 0x650 — asserted here by rejecting those calls.
    Reject(codec, "ReadShortString(raw, DearNameOffset",
        "fabricated spouse-name read at 0x650");
    Reject(codec, "WriteShortString(raw, DearNameOffset",
        "fabricated spouse-name write at 0x650");
    Require(loader, "PlayObject.m_boMarried = HumData.boMarried;",
        "login does not restore married flag");
    Require(saver, "HumData.boMarried = m_boMarried;",
        "save does not persist married flag");
    Require(playerBase, "public bool m_boMarried = false;",
        "runtime married field is missing");
}

void CheckOfflineMarriageFrame()
{
    Assert(NativeMasterRelationFrameCodec.TryEncodeMarriageClear(
            "账号甲", "丈夫甲", "妻子甲", out var wire, out var error),
        "marriage 0x0152 encode failed: " + error);
    Assert(LegacyDbServerFrameCodec.TryDecode(wire, out var frame, out error),
        "marriage 0x0152 envelope decode failed: " + error);
    Assert(NativeMasterRelationProtocol.TryDecode(frame, out var request,
            out error), "marriage 0x0152 request decode failed: " + error);
    Assert(request.Subcommand == 0,
        "marriage 0x0152 subtype is not zero");
    Assert(request.Account.SequenceEqual(gbk.GetBytes("账号甲"))
           && request.MasterName.SequenceEqual(gbk.GetBytes("丈夫甲"))
           && request.StudentName.SequenceEqual(gbk.GetBytes("妻子甲")),
        "marriage 0x0152 fixed fields changed");
    Assert(BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload) == 0x0152
           && BinaryPrimitives.ReadUInt16LittleEndian(
               frame.Payload.AsSpan(2, 2)) == 0
           && BinaryPrimitives.ReadUInt32LittleEndian(
               frame.Payload.AsSpan(4, 4)) == 0,
        "marriage 0x0152 header layout changed");
    Assert(!NativeMasterRelationFrameCodec.TryEncodeMarriageClear(
            "账号", "1234567890123456", "妻子", out _, out _),
        "overlength marriage name was accepted");

    Require(frameCodec, "MarriageClearSubcommand = 0;",
        "M2 marriage-clear subtype changed");
    Require(dbProtocol, "MarriageClearSubcommand = 0;",
        "DB marriage-clear subtype changed");
    Require(gameSoc, "TryClearMarriageRelation(",
        "DB 0x0152 dispatcher lost marriage mutation");
}

void CheckOfflineLogicalMutation()
{
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    raw[0x3E] = 1;
    raw[NativeHumanDataCodec.MarriedFlagOffset] = 1;
    raw[0x0670] = 0xA7;
    WriteShortString(raw, NativeHumanDataCodec.DearNameOffset, 15,
        "丈夫甲");
    Assert(NativeHumanLogicalCache.TryCreatePersistence(
            "账号甲", "妻子甲", raw, Array.Empty<byte>(),
            out var persistence, out var error),
        "logical marriage persistence creation failed: " + error);

    var cache = new NativeHumanLogicalCache();
    Assert(cache.GetOrLoad(7, () => persistence) != null,
        "logical marriage persistence did not load");
    var queued = false;
    // 2026-08-07: this used to assert that a NON-RECIPROCAL name is rejected.
    // That assertion was written from the C# code, not from the binary, and the
    // 战神 DBServer disassembly contradicts it.  The marriage-clear branch
    // (sub_5A8750 @0x5A8825) gates ONLY on the slot being non-empty —
    // 0x5A8844 `cmp byte [eax],0` / 0x5A8847 `je done` with eax = social-block
    // base from 0x5804A4.  A scan of the whole branch (0x5A8825..0x5A88B3) finds
    // NO comparison call at all: the only calls are 0x4035D8 (ShortString copies
    // that build the log text) and 0x5912C4 (`dl=3` relation log).  The other
    // compare, 0x5A8850 `cmp byte [eax+0x3f],0`, is the gender byte and only
    // decides the ORDER of the two names in that log line.
    // So a mismatched name must still clear, as long as a spouse exists.
    var mismatch = cache.TryClearMarriageRelation(7,
        gbk.GetBytes("错误姓名"), _ =>
        {
            queued = true;
            return true;
        });
    Assert(mismatch == NativeHumanMasterRelationState.Success && queued,
        "offline divorce must NOT require a reciprocal name (native 0x5A8844 " +
        "only checks the slot is non-empty)");

    // Reload a fresh marriage for the emptiness-gate case below.
    Assert(NativeHumanLogicalCache.TryCreatePersistence(
            "账号甲", "妻子甲", raw, Array.Empty<byte>(),
            out var persistence2, out var error2),
        "logical marriage persistence re-creation failed: " + error2);
    cache = new NativeHumanLogicalCache();
    Assert(cache.GetOrLoad(7, () => persistence2) != null,
        "logical marriage persistence did not reload");

    // Empty spouse slot => native falls straight to the done label without
    // touching anything (0x5A8847).
    var emptyRaw = (byte[])raw.Clone();
    emptyRaw[NativeHumanDataCodec.DearNameOffset] = 0;
    Assert(NativeHumanLogicalCache.TryCreatePersistence(
            "账号乙", "妻子乙", emptyRaw, Array.Empty<byte>(),
            out var emptyPersistence, out var emptyError),
        "empty-slot persistence creation failed: " + emptyError);
    var emptyCache = new NativeHumanLogicalCache();
    Assert(emptyCache.GetOrLoad(8, () => emptyPersistence) != null,
        "empty-slot persistence did not load");
    var emptyQueued = false;
    var emptyResult = emptyCache.TryClearMarriageRelation(8,
        gbk.GetBytes("丈夫甲"), _ =>
        {
            emptyQueued = true;
            return true;
        });
    Assert(emptyResult == NativeHumanMasterRelationState.NoMatch && !emptyQueued,
        "empty spouse slot must be rejected (native 0x5A8844 emptiness gate)");

    NativeSavePersistenceData saved = null;
    var cleared = cache.TryClearMarriageRelation(7,
        gbk.GetBytes("丈夫甲"), value =>
        {
            saved = value;
            return true;
        });
    Assert(cleared == NativeHumanMasterRelationState.Success && saved != null,
        "offline divorce logical mutation failed");
    Assert(NativeHumanLogicalCache.TryExtractRaw(saved, out var updated,
            out _), "offline divorce queued invalid persistence");
    Assert(updated[NativeHumanDataCodec.MarriedFlagOffset] == 0
           && updated[NativeHumanDataCodec.DearNameOffset] == 0
           && updated[0x0670] == 0xA7,
        "offline divorce did not atomically clear only marriage fields");

    // Native clears the LENGTH BYTE ONLY: 0x5A88A2 `mov byte [eax],0` where eax is
    // the social-block base.  The 15 character bytes are left in place, and the
    // ShortString-assign helper 0x4035D8 does not zero-fill either (it writes only
    // `min(srcLen,cl)` then that many chars).  So the old spouse name bytes must
    // still be present behind the zero length.
    var dearTail = updated.AsSpan(NativeHumanDataCodec.DearNameOffset + 1,
        NativeHumanDataCodec.DearNameCapacity);
    var expectedTail = gbk.GetBytes("丈夫甲");
    Assert(dearTail.Slice(0, expectedTail.Length).SequenceEqual(expectedTail),
        "offline divorce must clear ONLY the length byte (native 0x5A88A2); the " +
        "character bytes must survive, matching 0x4035D8's no-zero-fill behaviour");

    Require(logicalCache, "if (field[0] == 0)",
        "offline divorce lost the native emptiness-only gate (0x5A8844)");
}

void CheckPasSurface()
{
    Require(playerMessage,
        "processMessage.nParam1, processMessage.wParam",
        "10126 Recog/Param mapping changed");
    Require(playerMessage,
        "HUtil32.LoWord(processMessage.nParam2)",
        "10126 Tag mapping changed");
    Require(playerMessage,
        "HUtil32.LoWord(processMessage.nParam3)",
        "10126 Series mapping changed");
    Assert(CaseBodies(bridge, "agreemarry").Count == 1,
        "AgreeMarry dispatch count mismatch");
    Assert(CaseBodies(bridge, "agreymarry").Count == 0,
        "unproved AgreyMarry alias is still registered");
    var agreeDispatch = CaseBodies(bridge, "agreemarry")[0];
    Require(agreeDispatch, "args.Count != 1", "AgreeMarry arity changed");
    Require(agreeDispatch, "ObjVal is not TBaseObject marryNpc",
        "AgreeMarry TObject ABI changed");
    Require(agreeDispatch, "AgreeNativeMarry(CurrentPlayer, marryNpc);",
        "AgreeMarry dispatch lost exact state machine");

    var agree = Slice(bridge, "private static void AgreeNativeMarry",
        "private static bool TryClearOfflineSpouseRelation");
    foreach (var value in new[]
             {
                 "accepter.m_boAllowMarry", "!accepter.m_boMarried",
                 "accepter.m_boStartMarry", "PlayGender.WoMan",
                 "< NativeMarryRequestWindowMs", "!peer.m_boMarried",
                 "peer.m_boStartMarry", "PlayGender.Man",
                 "ReferenceEquals(peer.m_PoseBaseObject, accepter)",
                 "!peer.m_boDeath", "accepter.m_sDearName = peer.m_sCharName",
                 "peer.m_sDearName = accepter.m_sCharName", "RefShowName();",
                 "[月老]恭喜:", "喜结良缘，祝愿他们白头偕老！",
                 "6, 0, 0, peer.m_sCharName", "6, 0, 0, accepter.m_sCharName",
                 "对方已经离线或请求已超时失效",
                 "CloseNativeMarryDialog(accepter, npc)"
             })
        Require(agree, value, "AgreeMarry flow missing: " + value);
    Assert(!agree.Contains("accepter.m_PoseBaseObject = null;",
            StringComparison.Ordinal)
           || agree.IndexOf("accepter.m_PoseBaseObject = null;",
               StringComparison.Ordinal)
           > agree.IndexOf("else if (accepter != null)",
               StringComparison.Ordinal),
        "AgreeMarry success incorrectly cleared the native pose pointer");

    var disagree = CaseBodies(bridge, "disagreemarry").Single();
    Require(disagree, "args.Count != 0", "DisAgreeMarry arity changed");
    Require(disagree, "DisAgreeNativeMarry(CurrentPlayer);",
        "DisAgreeMarry cleanup lost");

    var divorceDispatch = CaseBodies(bridge, "npcdivmarry").Single();
    Require(divorceDispatch, "args.Count != 1", "NpcDivMarry arity changed");
    Require(divorceDispatch, "ObjVal is not TBaseObject divorceNpc",
        "NpcDivMarry TObject ABI changed");
    var divorce = Slice(bridge, "private static void DivorceNativeMarry",
        "private static bool IsMasterRequestCoolingDown");
    foreach (var value in new[]
             {
                 "player.DecGold(1_000_000)", "player.GoldChanged();",
                 "StringComparison.Ordinal", "TryClearOfflineSpouseRelation",
                 "M2Share.UserEngine.SendServerGroupMsg(",
                 "Grobal2.ISM_DIVORCE,",
                 "serverIndex, spouseName);",
                 "7, 0, 0, player.m_sCharName",
                 "7, 0, 0, spouseName", "player.m_boMarried = false",
                 "你无配偶或所携带的金币不够，不能离婚!",
                 "CloseNativeMarryDialog(player, npc)"
             })
        Require(divorce, value, "NpcDivMarry flow missing: " + value);

    Assert(CaseBodies(bridge, "checkmarry").Count == 1,
        "CheckMarry dispatch count mismatch");
    var playerFunctions = Slice(bridge, "public bool CallPlayerFunc",
        "// NPC METHOD CALLS");
    Assert(!playerFunctions.Contains("case \"checkmarry\":",
        StringComparison.Ordinal),
        "CheckMarry remains on the wrong player-function receiver");
    var npcFunctions = Slice(bridge, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");
    foreach (var value in new[]
             {
                 "case \"checkmarry\":", "args.Count != 1",
                 "ObjVal is not TPlayObject marriedPlayer",
                 "var marryState = -1", "marryState = -3",
                 "? 1 : -2", "PasValue.FromInt(marryState)"
             })
        Require(npcFunctions, value, "CheckMarry flow missing: " + value);
}

void CheckOtherServerDivorce()
{
    Require(global, "public const int ISM_DIVORCE = 216;",
        "Other-GS divorce opcode changed");
    Assert(!global.Contains("ISM_FRIEND_OPEN = 216;",
            StringComparison.Ordinal),
        "opcode 216 is still owned by the stale FriendOpen label");
    // Only whitespace and // comments may sit between the label and the call;
    // any other statement would mean 216 no longer routes straight to the receiver.
    Assert(Regex.IsMatch(mirrorMessage,
            @"case Grobal2\.ISM_DIVORCE:(?:\s|//[^\r\n]*)*"
            + @"MsgGetDivorce\(serverNum, Body\);\s*break;",
            RegexOptions.CultureInvariant),
        "Other-GS divorce dispatcher is missing");

    // End the slice at the method that actually follows MsgGetDivorce. The old
    // MsgGetReloadMakeItemList marker now sits many receivers later, so the
    // "no non-native handling" guard below was reading other handlers' code.
    var receiver = Slice(mirrorMessage,
        "private void MsgGetDivorce", "private void MsgGetMentorStudentLeft");
    RequireInOrder(receiver, new[]
    {
        "serverNum != M2Share.nServerIndex",
        "string.IsNullOrEmpty(Body)",
        "var spouse = M2Share.UserEngine?.GetPlayObject(Body);",
        "spouse == null || !spouse.m_boMarried",
        "var dearName = spouse.m_sDearName ?? string.Empty;",
        "spouse.m_boMarried = false;",
        "spouse.SendMsg(spouse, Grobal2.RM_MASTERRELATION, 0,",
        "7, 0, 0, dearName);",
        "spouse.m_sDearName = string.Empty;",
        "spouse.SysMsg(\"你的配偶与你离婚了\", MsgColor.Red, MsgType.Hint);",
        "spouse.RefShowName();"
    }, "Other-GS divorce receiver order/semantics changed");

    foreach (var forbidden in new[]
             {
                 "GetValidStr", ".Split(", ".Trim(",
                 "StringComparison", "m_DearHuman", "m_PoseBaseObject"
             })
        Assert(!receiver.Contains(forbidden, StringComparison.Ordinal),
            "Other-GS divorce receiver adds non-native handling: "
            + forbidden);
}

List<string> CaseBodies(string source, string name)
{
    var pattern = $"case \\\"{Regex.Escape(name)}\\\":(?<body>.*?)(?=\\r?\\n\\s*case \\\"|\\r?\\n\\s*default:)";
    return Regex.Matches(source, pattern,
            RegexOptions.Singleline | RegexOptions.CultureInvariant)
        .Select(match => match.Groups["body"].Value)
        .ToList();
}

void WriteShortString(Span<byte> destination, int offset, int capacity,
    string value)
{
    var bytes = gbk.GetBytes(value);
    Assert(bytes.Length <= capacity, "test ShortString exceeds capacity");
    destination.Slice(offset, capacity + 1).Clear();
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination.Slice(offset + 1));
}

string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, "missing marker: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, "missing marker: " + endMarker);
    return source[start..end];
}

void Require(string source, string value, string message)
{
    Assert(source.Contains(value, StringComparison.Ordinal), message);
}

void Reject(string source, string value, string message)
{
    Assert(!source.Contains(value, StringComparison.Ordinal), message);
}

void RequireInOrder(string source, IEnumerable<string> values,
    string message)
{
    var offset = 0;
    foreach (var value in values)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        Assert(index >= 0, message + ": " + value);
        offset = index + value.Length;
    }
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string Read(params string[] segments) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));

string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 AppContext.BaseDirectory, Directory.GetCurrentDirectory()
             })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }

    throw new DirectoryNotFoundException("repository root not found");
}
