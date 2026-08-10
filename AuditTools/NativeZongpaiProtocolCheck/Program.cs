// Asserts the native 宗派/师门 sub-protocol (type1 0x0170) byte contract against
// the 战神 DBServer disassembly. Every expected value below is quoted from a
// specific instruction address in DBServer_unpacked.exe. NOTE: an earlier
// version of this comment claimed the CODE segment is not VMProtect-affected.
// That was wrong -- the image has .vmp0/.vmp1 and 688 CODE-to-VMP transfers
// (E8 x568 + E9 x120, independently counted). What is true is that most game
// logic functions are not virtualised (CODE segment is not
// VMProtect-damaged, so these were read directly, not from pseudocode).
// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3之二.
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule.Packet;

var failures = new List<string>();

// Real counter, printed below. A hardcoded count in the PASS line is worthless:
// it cannot show that newly added assertions actually executed, and a test whose
// body throws early would silently stop asserting while still reporting the same
// number. If this number does not move after you add assertions, they never ran.
var asserts = 0;

Run("request/response command words", CommandWords);
Run("tail length gate 0x54", TailLengthGate);
Run("sub-command range gate 0..0xD", SubCommandRange);
Run("tail field slots", TailFieldSlots);
Run("header field slots for 11/12", HeaderFieldSlots);
Run("reply mode per sub-command", ReplyModes);
Run("standard reply framing 0xA8/0x9C", StandardReplyFraming);
Run("master-level reply framing 0x84/0x78", MasterLevelReplyFraming);
Run("member-list reply length formula", MemberListReplyFraming);
Run("notice reply length formula", NoticeReplyFraming);
Run("malformed frames rejected", MalformedFrames);
Run("sub 2/3/13 field mapping (write-path guardrail)", WritePathFieldMapping);
Run("sub 1 routing / polarity / tail write-back", Sub1Enumerate);
Run("sub 10 slots / stride / empty-count boundary", Sub10QueryMembers);
Run("sub 11/12 no length gate + notice bounds", Sub11And12Notice);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"NativeZongpaiProtocolCheck PASS tests=15 asserts={asserts} "
                  + "type1=0170 reply=0071 gate=0x54 std=0xA8/0x9C "
                  + "lvl=0x84/0x78 member=0x29 dispatch=0x594122 "
                  + "sub1=0x5933CC sub10=0x593B74 sub11=0x593D30 sub12=0x593D70");
return 0;

void CommandWords()
{
    // 0x598B23 table entry for 0x170 → 0x599206; reply word from 0x59442E.
    Equal(0x0170, NativeZongpaiProtocol.RequestCommand, "request command");
    Equal(0x0071, NativeZongpaiProtocol.ResponseCommand, "response command");
    // 0x59DDAC: `cmp [len], 0x48` splits header from tail.
    Equal(0x48, NativeZongpaiProtocol.HeaderSize, "type1 header size");
    // 0x594102 / 0x594465 / 0x59454C all gate on 0x54.
    Equal(0x54, NativeZongpaiProtocol.MinimumTailLength, "tail gate");
    // 0x59457F: `imul eax, [n], 0x29`.
    Equal(0x29, NativeZongpaiProtocol.MemberRecordSize, "member stride");
    // 0x594545: rep movsd ecx=0xC → 48 bytes.
    Equal(0x30, NativeZongpaiProtocol.LevelReplyRecordSize, "level record");
}

void TailLengthGate()
{
    // Sub-commands routed via 0x594102 / 0x594465 / 0x59454C re-test the gate.
    foreach (var sub in new[]
             {
                 NativeZongpaiSubCommand.Enumerate,
                 NativeZongpaiSubCommand.CreateMaster,
                 NativeZongpaiSubCommand.AddMember,
                 NativeZongpaiSubCommand.RemoveMember,
                 NativeZongpaiSubCommand.UpdateMemberRole,
                 NativeZongpaiSubCommand.UpdateStudentExp,
                 NativeZongpaiSubCommand.UpdateStudentAndMasterExp,
                 NativeZongpaiSubCommand.UpdateMasterExp,
                 NativeZongpaiSubCommand.UpdateMasterLevel,
                 NativeZongpaiSubCommand.QueryMembers,
                 NativeZongpaiSubCommand.DeleteMaster,
             })
        True(NativeZongpaiProtocol.RequiresLengthGate(sub), $"gate {sub}");

    // 0x59463E (11/12) never tests it; sub-command 0 exits at 0x59476B.
    False(NativeZongpaiProtocol.RequiresLengthGate(
        NativeZongpaiSubCommand.ReadNotice), "gate ReadNotice");
    False(NativeZongpaiProtocol.RequiresLengthGate(
        NativeZongpaiSubCommand.ModifyNotice), "gate ModifyNotice");
    False(NativeZongpaiProtocol.RequiresLengthGate(
        NativeZongpaiSubCommand.None), "gate None");

    // A gated sub-command with a 0x53-byte tail must be refused.
    var shortTail = Frame(NativeZongpaiSubCommand.AddMember, 0x53);
    False(NativeZongpaiProtocol.TryDecodeRequest(shortTail, out _, out _),
        "0x53 tail refused");
    var exactTail = Frame(NativeZongpaiSubCommand.AddMember, 0x54);
    True(NativeZongpaiProtocol.TryDecodeRequest(exactTail, out _, out _),
        "0x54 tail accepted");
    // An ungated sub-command passes with no tail at all.
    var noTail = Frame(NativeZongpaiSubCommand.ReadNotice, 0);
    True(NativeZongpaiProtocol.TryDecodeRequest(noTail, out _, out _),
        "ungated no-tail accepted");
}

void SubCommandRange()
{
    // 0x5940CA: `cmp eax,0xD / ja` — 14 is out of range.
    var payload = new byte[NativeZongpaiProtocol.HeaderSize + 0x54];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeZongpaiProtocol.RequestCommand);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 14);
    False(NativeZongpaiProtocol.TryDecodeRequest(
        new LegacyDbServerFrame(1, 0, payload), out _, out _), "14 refused");

    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 13);
    True(NativeZongpaiProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(1, 0, payload), out var ok, out _),
        "13 accepted");
    Equal((int)NativeZongpaiSubCommand.DeleteMaster, (int)ok.SubCommand,
        "sub-command decode");
    // 0x59443E echoes header[+4], which is the sub-command dword itself.
    Equal(13, ok.EchoDword, "echo dword");
}

void TailFieldSlots()
{
    // Slots read by the sub-cases from [ebp-4] = tail = record+0x48
    // (0x59DDB2). Offsets: 0x00/0x10/0x25/0x35 strings, 0x4C/0x50 dwords.
    var frame = Frame(NativeZongpaiSubCommand.AddMember, 0x54,
        tail =>
        {
            PutShortString(tail, 0x00, "master");
            PutShortString(tail, 0x10, "rolename");
            PutShortString(tail, 0x25, "member");
            PutShortString(tail, 0x35, "othername");
            BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(0x4C, 4), 4444);
            BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(0x50, 4), 5555);
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "decode: " + err);
    EqualText("master", Text(req.TailSlot00), "tail+0x00");
    EqualText("rolename", Text(req.TailSlot10), "tail+0x10");
    EqualText("member", Text(req.TailSlot25), "tail+0x25");
    EqualText("othername", Text(req.TailSlot35), "tail+0x35");
    Equal(4444, req.TailValue4C, "tail+0x4C");
    Equal(5555, req.TailValue50, "tail+0x50");

    // Capacity limits: 0x594186 uses cl=0x14 for the wide slots, and
    // 0x59460E / 0x594740 use cl=0x0F for the 15-byte slots.
    var over = Frame(NativeZongpaiSubCommand.AddMember, 0x54,
        tail => tail[0x25] = 0x10);
    False(NativeZongpaiProtocol.TryDecodeRequest(over, out _, out _),
        "0x25 over 15 refused");
    var wideOk = Frame(NativeZongpaiSubCommand.AddMember, 0x54,
        tail => tail[0x00] = 0x14);
    True(NativeZongpaiProtocol.TryDecodeRequest(wideOk, out _, out _),
        "0x00 at 20 accepted");
}

void HeaderFieldSlots()
{
    // 0x594738 / 0x59474E read [ebp-0xC] (the HEADER) at +0x25 and +0x35 —
    // a different base from the tail slots above.
    //
    // ⚠️ 这里刻意用 **AddMember**（group 1）而不是 ModifyNotice 来证明「头槽与尾槽
    // 互相独立」。原来用的是 ModifyNotice，但 sub 11/12 属 group 4，原版对它
    // **根本不解析 tail 槽**（0x59463E 全程只在 0x59468A 取一次 tail 指针，配
    // 0x59466B 的长度直接透传给 worker 0x593D70），所以「ModifyNotice 的 tail+0x25」
    // 这个概念在原版里不存在 —— 拿它作独立性判据是在断言一个原版没有的行为。
    // 头槽本身对所有子命令都解析，用 group 1 的成员同样能覆盖，且不牵扯 group 4。
    var frame = Frame(NativeZongpaiSubCommand.AddMember, 0x54,
        tail => PutShortString(tail, 0x25, "tailside"),
        header =>
        {
            PutShortString(header, 0x25, "hdr25");
            PutShortString(header, 0x35, "hdr35");
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "decode: " + err);
    EqualText("hdr25", Text(req.HeaderSlot25), "header+0x25");
    EqualText("hdr35", Text(req.HeaderSlot35), "header+0x35");
    // The tail slot must stay independent of the header slot.
    EqualText("tailside", Text(req.TailSlot25), "tail+0x25 independent");

    // group 4 的反向锁定：ModifyNotice 仍然解析头槽，但**不**解析尾槽。
    var g4 = Frame(NativeZongpaiSubCommand.ModifyNotice, 0x54,
        tail => PutShortString(tail, 0x25, "tailside"),
        header => PutShortString(header, 0x35, "hdr35"));
    True(NativeZongpaiProtocol.TryDecodeRequest(g4, out var g4req, out var g4err),
        "group4 decode: " + g4err);
    EqualText("hdr35", Text(g4req.HeaderSlot35),
        "group4 still parses HEADER+0x35 (0x59474E)");
    EqualText(string.Empty, Text(g4req.TailSlot25),
        "group4 does NOT parse tail slots (0x59463E never adds 0x25 to [ebp-4])");
}

void ReplyModes()
{
    // `mov [ebp-0x10], 1` at 0x59415A/0x5941E4/0x59431C/0x59434A/0x594378/
    // 0x59446F/0x594556/0x59469F → sender-only.
    //
    // ⚠️ Sub-command 1 (Enumerate) belongs in THIS list. An earlier revision of
    // this file asserted `Equal(None, GetReplyMode(Enumerate))`, which locked in
    // a real bug: the very first instruction of the sub-1 case body is
    //   0059415A  c7 45 f0 01 00 00 00   mov dword [ebp-0x10], 1
    // and `[ebp-0x10]` is what the dispatcher returns (epilogue
    // `0059481A 8b 45 f0  mov eax,[ebp-0x10]` / `ret 0xc`), which the caller
    // 0x59C51C compares against 1 at 0x59C552 to pick the sender-only send
    // 0x49CB34. So the original ALWAYS replies to sub 1; returning None
    // suppressed that reply. Assertions must be written from the bytes, never
    // from the current C# behaviour.
    foreach (var sub in new[]
             {
                 NativeZongpaiSubCommand.Enumerate,
                 NativeZongpaiSubCommand.CreateMaster,
                 NativeZongpaiSubCommand.UpdateStudentAndMasterExp,
                 NativeZongpaiSubCommand.UpdateMasterExp,
                 NativeZongpaiSubCommand.DeleteMaster,
                 NativeZongpaiSubCommand.UpdateMasterLevel,
                 NativeZongpaiSubCommand.QueryMembers,
                 NativeZongpaiSubCommand.ReadNotice,
                 NativeZongpaiSubCommand.ModifyNotice,
             })
        Equal((int)NativeZongpaiReplyMode.Sender,
            (int)NativeZongpaiProtocol.GetReplyMode(sub), $"mode {sub}");

    // `mov [ebp-0x10], 2` at 0x594220/0x59427C/0x5942C0 → broadcast.
    foreach (var sub in new[]
             {
                 NativeZongpaiSubCommand.AddMember,
                 NativeZongpaiSubCommand.RemoveMember,
                 NativeZongpaiSubCommand.UpdateMemberRole,
             })
        Equal((int)NativeZongpaiReplyMode.Broadcast,
            (int)NativeZongpaiProtocol.GetReplyMode(sub), $"mode {sub}");

    // 0x5943A3 (sub-command 6) never writes [ebp-0x10]; the original therefore
    // guarantees no reply, so we must not invent one.
    Equal((int)NativeZongpaiReplyMode.None,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.UpdateStudentExp), "mode 6 unset");
    // Sub-command 0 is mapped to group 0 by the byte map at 0x5940E0
    // (`00 01 01 01 01 01 01 01 01 02 03 04 04 01`), and group 0's entry in the
    // table at 0x5940EE is 0x59476B — which IS the shared exit
    // (`0059476B 33 c0  xor eax,eax`). It never writes the route, so no reply.
    Equal((int)NativeZongpaiReplyMode.None,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.None), "mode 0");

    // Every sub-command 0..13 must be classified: the route write is either
    // present (Sender/Broadcast) or provably absent (None). A new enum member
    // silently defaulting to None is exactly the failure mode that produced the
    // sub-1 bug, so pin the whole census here.
    var expectedModes = new (NativeZongpaiSubCommand Sub, NativeZongpaiReplyMode Mode, string Site)[]
    {
        (NativeZongpaiSubCommand.None, NativeZongpaiReplyMode.None, "0x59476B no write"),
        (NativeZongpaiSubCommand.Enumerate, NativeZongpaiReplyMode.Sender, "0x59415A =1"),
        (NativeZongpaiSubCommand.CreateMaster, NativeZongpaiReplyMode.Sender, "0x5941E4 =1"),
        (NativeZongpaiSubCommand.AddMember, NativeZongpaiReplyMode.Broadcast, "0x594220 =2"),
        (NativeZongpaiSubCommand.RemoveMember, NativeZongpaiReplyMode.Broadcast, "0x59427C =2"),
        (NativeZongpaiSubCommand.UpdateMemberRole, NativeZongpaiReplyMode.Broadcast, "0x5942C0 =2"),
        (NativeZongpaiSubCommand.UpdateStudentExp, NativeZongpaiReplyMode.None, "0x5943A3 no write"),
        (NativeZongpaiSubCommand.UpdateStudentAndMasterExp, NativeZongpaiReplyMode.Sender, "0x59431C =1"),
        (NativeZongpaiSubCommand.UpdateMasterExp, NativeZongpaiReplyMode.Sender, "0x59434A =1"),
        (NativeZongpaiSubCommand.UpdateMasterLevel, NativeZongpaiReplyMode.Sender, "0x59446F =1"),
        (NativeZongpaiSubCommand.QueryMembers, NativeZongpaiReplyMode.Sender, "0x594556 =1"),
        (NativeZongpaiSubCommand.ReadNotice, NativeZongpaiReplyMode.Sender, "0x59469F =1"),
        (NativeZongpaiSubCommand.ModifyNotice, NativeZongpaiReplyMode.Sender, "0x59469F =1"),
        (NativeZongpaiSubCommand.DeleteMaster, NativeZongpaiReplyMode.Sender, "0x594378 =1"),
    };
    Equal(14, expectedModes.Length, "route census covers 0..13");
    for (var i = 0; i < expectedModes.Length; i++)
    {
        var (sub, mode, site) = expectedModes[i];
        Equal(i, (int)sub, $"census slot {i} is sub-command {i}");
        Equal((int)mode, (int)NativeZongpaiProtocol.GetReplyMode(sub),
            $"census route sub{i} ({site})");
    }
}

void StandardReplyFraming()
{
    // 0x5943CF total=0xA8, 0x59441B payload=0x9C, 0x59442E cmd=0x71,
    // 0x59443A result@body+2, 0x594447 echo@body+4,
    // 0x594452 rep movsd ecx=0x15 copies tail[0..0x54) to buf+0x54
    // (= payload+0x48, since payload == buf+0x0C).
    var frame = Frame(NativeZongpaiSubCommand.CreateMaster, 0x60,
        tail =>
        {
            // Sequential filler would put 0x26 at the 15-byte slot 0x25 and be
            // refused, so keep the ShortString length prefixes legal and mark
            // the rest so the copy window can still be verified byte-wise.
            for (var i = 0; i < 0x60; i++) tail[i] = 0xC7;
            tail[0x00] = 0x14;
            tail[0x10] = 0x14;
            tail[0x25] = 0x0F;
            tail[0x35] = 0x0F;
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "decode: " + err);
    var reply = NativeZongpaiProtocol.CreateStandardResponse(req, 7);
    Equal(1, reply.Type, "reply type");
    Equal(0x9C, reply.Payload.Length, "reply payload length");
    Equal(0xA8, reply.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "reply total length");
    Equal(0x0071, BinaryPrimitives.ReadUInt16LittleEndian(reply.Payload),
        "reply command");
    Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(
        reply.Payload.AsSpan(2, 2)), "reply result");
    Equal((int)NativeZongpaiSubCommand.CreateMaster,
        BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.AsSpan(4, 4)),
        "reply echo");
    // Exactly the first 0x54 bytes of the TAIL, landing at payload+0x48.
    for (var i = 0; i < 0x54; i++)
        Equal(req.Tail[i], reply.Payload[0x48 + i], $"echo byte {i}");
    // 0x48 + 0x54 == 0x9C: the copy fills the payload exactly, no overrun.
    Equal(0x9C, 0x48 + 0x54, "echo fills payload");
}

void MasterLevelReplyFraming()
{
    // 0x5944B8 total=0x84, 0x594501 payload=0x78, record 48B at payload+0x54.
    var frame = Frame(NativeZongpaiSubCommand.UpdateMasterLevel, 0x54);
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "decode: " + err);
    var record = new byte[0x30];
    for (var i = 0; i < record.Length; i++) record[i] = (byte)(0xA0 + i);
    var reply = NativeZongpaiProtocol.CreateMasterLevelResponse(req, 3, record);
    Equal(0x78, reply.Payload.Length, "level payload length");
    Equal(0x84, reply.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "level total length");
    Equal(0x0071, BinaryPrimitives.ReadUInt16LittleEndian(reply.Payload),
        "level command");
    Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(
        reply.Payload.AsSpan(2, 2)), "level result");
    for (var i = 0; i < 0x30; i++)
        Equal(0xA0 + i, reply.Payload[0x48 + i], $"level record {i}");
    // 0x54 + 0x30 == 0x84 - 0x0C, so the record fills the frame exactly.
    Equal(0x78, 0x48 + 0x30, "level record fills payload");
}

void MemberListReplyFraming()
{
    // 0x59457F total = n*0x29 + 0x54 ; 0x5945CB payload = n*0x29 + 0x48 ;
    // 0x5945F0 body+2 = COUNT ; 0x59460E tail+0x35 → body+0x25 ;
    // 0x59462C records → payload+0x54.
    var frame = Frame(NativeZongpaiSubCommand.QueryMembers, 0x54,
        tail => PutShortString(tail, 0x35, "shifu"));
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "decode: " + err);

    var records = new byte[3 * 0x29];
    for (var i = 0; i < records.Length; i++) records[i] = (byte)(i + 0x11);
    var reply = NativeZongpaiProtocol.CreateMemberListResponse(req, 3, records);
    Equal(3 * 0x29 + 0x48, reply.Payload.Length, "member payload length");
    Equal(3 * 0x29 + 0x54,
        reply.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "member total length");
    Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(
        reply.Payload.AsSpan(2, 2)), "member count in body+2");
    EqualText("shifu", Text(ReadShortString(reply.Payload, 0x25)),
        "tail+0x35 echoed to body+0x25");
    for (var i = 0; i < records.Length; i++)
        Equal(i + 0x11, reply.Payload[0x48 + i], $"member byte {i}");

    // 0x594613: count <= 0 exits before copying, but the frame is still built.
    var empty = NativeZongpaiProtocol.CreateMemberListResponse(
        req, 0, ReadOnlySpan<byte>.Empty);
    Equal(0x48, empty.Payload.Length, "empty member payload");
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(
        empty.Payload.AsSpan(2, 2)), "empty member count");
}

void NoticeReplyFraming()
{
    // 0x5946B4 total = len + 0x54 + 1 ; 0x594700 payload = len + 0x48 + 1 ;
    // 0x594722 body+2 = LENGTH ; header+0x25/+0x35 → body+0x25/+0x35 ;
    // 0x594766 notice text → payload+0x54.
    var frame = Frame(NativeZongpaiSubCommand.ModifyNotice, 0x54,
        null,
        header =>
        {
            PutShortString(header, 0x25, "acct");
            PutShortString(header, 0x35, "chr");
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "decode: " + err);

    var notice = System.Text.Encoding.ASCII.GetBytes("hello-notice");
    var reply = NativeZongpaiProtocol.CreateNoticeResponse(req, notice);
    Equal(notice.Length + 0x48 + 1, reply.Payload.Length,
        "notice payload length");
    Equal(notice.Length + 0x54 + 1,
        reply.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "notice total length");
    Equal(notice.Length, BinaryPrimitives.ReadUInt16LittleEndian(
        reply.Payload.AsSpan(2, 2)), "notice length in body+2");
    EqualText("acct", Text(ReadShortString(reply.Payload, 0x25)),
        "header+0x25 echoed");
    EqualText("chr", Text(ReadShortString(reply.Payload, 0x35)),
        "header+0x35 echoed");
    for (var i = 0; i < notice.Length; i++)
        Equal(notice[i], reply.Payload[0x48 + i], $"notice byte {i}");
}

void MalformedFrames()
{
    False(NativeZongpaiProtocol.TryDecodeRequest(null, out _, out _),
        "null frame");
    False(NativeZongpaiProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(1, 0, new byte[0x47]), out _, out _),
        "short header");
    var wrongCommand = new byte[NativeZongpaiProtocol.HeaderSize + 0x54];
    BinaryPrimitives.WriteUInt16LittleEndian(wrongCommand, 0x0171);
    False(NativeZongpaiProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(1, 0, wrongCommand), out _, out _),
        "wrong command");
}

// sub 1 (Enumerate)。原版 body 0x59415A..0x5941DF + worker 0x5933CC。
// 覆盖：路由模式、长度门、**返回码反极性**、tail 写回的槽/容量/不清尾部。
void Sub1Enumerate()
{
    // 路由：0x59415A 是 case body 的**第一条**指令 `c7 45 f0 01 00 00 00`。
    Equal((int)NativeZongpaiReplyMode.Sender,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.Enumerate), "sub1 route = Sender @0x59415A");
    // 长度门：sub1 走 group 1，入口 0x594102 `83 7d 10 54` + `0f 8c`。
    True(NativeZongpaiProtocol.RequiresLengthGate(
        NativeZongpaiSubCommand.Enumerate), "sub1 is 0x54-gated via group1");
    False(NativeZongpaiProtocol.TryDecodeRequest(
            Frame(NativeZongpaiSubCommand.Enumerate, 0x53), out _, out _),
        "sub1 0x53 tail refused (0x594106 jl)");

    // ★ 反极性：worker 0x59340A 把 [ebp-0x10] 初始化为 1，只有找到才在
    // 0x59347F 写 0。所以 found ⇒ body+2 == 0。
    Equal(0, NativeZongpaiProtocol.EnumerateFoundResult,
        "sub1 found code is 0 (0x59347F xor eax,eax / mov [ebp-0x10],eax)");
    Equal(1, NativeZongpaiProtocol.EnumerateNotFoundResult,
        "sub1 not-found code is 1 (0x59340A mov [ebp-0x10],1 initial value)");
    True(NativeZongpaiProtocol.EnumerateFoundResult
         != NativeZongpaiProtocol.EnumerateNotFoundResult,
        "sub1 found/not-found codes must differ");

    // 输入槽：0x59417C `add edx,0x35` ⇒ 查询用的成员名取 tail+0x35，不是 tail+0x00。
    var frame = Frame(NativeZongpaiSubCommand.Enumerate, 0x54,
        tail =>
        {
            for (var i = 0; i < 0x54; i++) tail[i] = 0xEE;
            PutShortString(tail, 0x00, "oldmaster");
            PutShortString(tail, 0x10, "oldrole");
            tail[0x25] = 0x00;
            PutShortString(tail, 0x35, "lookupname");
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "sub1 decode: " + err);
    EqualText("lookupname", Text(req.EnumerateMemberName),
        "sub1 input name resolves to tail+0x35 (@0x59417C add edx,0x35)");
    True(Text(req.EnumerateMemberName) != Text(req.TailSlot00),
        "sub1 input must NOT come from tail+0x00 (that slot is an OUTPUT)");

    // 找到：out1 → tail+0x00（0x5941AF 无 add，0x5941B2 cl=0x0F ⇒ **容量 15**）、
    //       out2 → tail+0x10（0x5941D5 add eax,0x10，0x5941D8 cl=0x14 ⇒ 容量 20）。
    var found = NativeZongpaiProtocol.CreateEnumerateResponse(req, true,
        System.Text.Encoding.ASCII.GetBytes("newmaster"),
        System.Text.Encoding.ASCII.GetBytes("newrole"));
    Equal(0x9C, found.Payload.Length, "sub1 reply payload = 0x9C (shared 0x5943C5)");
    Equal(0xA8, found.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "sub1 reply total = 0xA8 (0x5943D2)");
    Equal(0x0071, BinaryPrimitives.ReadUInt16LittleEndian(found.Payload),
        "sub1 reply command 0x71 (0x59442E)");
    Equal(NativeZongpaiProtocol.EnumerateFoundResult,
        BinaryPrimitives.ReadUInt16LittleEndian(found.Payload.AsSpan(2, 2)),
        "sub1 found ⇒ body+2 == 0 (REVERSED polarity)");
    // 回显窗口是 payload+0x48（= buf+0x54）。
    EqualText("newmaster", Text(ReadShortString(found.Payload, 0x48 + 0x00)),
        "sub1 out1 masterName written to tail+0x00 (@0x5941AF, no add)");
    EqualText("newrole", Text(ReadShortString(found.Payload, 0x48 + 0x10)),
        "sub1 out2 roleName written to tail+0x10 (@0x5941D5 add eax,0x10)");
    // 0x4035D8 不清尾部：槽内长度之后的字节必须保留请求原值 0xEE。
    Equal(0xEE, found.Payload[0x48 + 0x00 + 1 + 9],
        "sub1 tail+0x00 keeps request bytes past the new length "
        + "(0x4035D8 does NOT zero-fill)");
    Equal(0xEE, found.Payload[0x48 + 0x10 + 1 + 7],
        "sub1 tail+0x10 keeps request bytes past the new length");
    // 未被写的槽（tail+0x35 是输入槽）照样原样回显。
    EqualText("lookupname", Text(ReadShortString(found.Payload, 0x48 + 0x35)),
        "sub1 tail+0x35 is echoed untouched");

    // 容量 15 vs 20 的不对称必须体现：17 字节的师父名要被截到 15。
    var longNames = NativeZongpaiProtocol.CreateEnumerateResponse(req, true,
        System.Text.Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQ"),  // 17
        System.Text.Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvw")); // 23
    Equal(15, longNames.Payload[0x48],
        "sub1 out1 truncates to 15 (0x5941B2 mov cl,0x0F)");
    Equal(20, longNames.Payload[0x48 + 0x10],
        "sub1 out2 truncates to 20 (0x5941D8 mov cl,0x14)");
    True(longNames.Payload[0x48] != longNames.Payload[0x48 + 0x10],
        "sub1 the two write-back slots have DIFFERENT capacities (15 vs 20)");

    // 未找到：0x404E94 的 edx==0 分支（0x404EB1 mov byte [eax],0）⇒ 两个槽的
    // 长度字节被清 0，但其后字节不动 ⇒ 不是「原封不动回显」。
    var missing = NativeZongpaiProtocol.CreateEnumerateResponse(req, false,
        System.Text.Encoding.ASCII.GetBytes("ignored"),
        System.Text.Encoding.ASCII.GetBytes("ignored"));
    Equal(NativeZongpaiProtocol.EnumerateNotFoundResult,
        BinaryPrimitives.ReadUInt16LittleEndian(missing.Payload.AsSpan(2, 2)),
        "sub1 not-found ⇒ body+2 == 1 (0x59340A initial value survives)");
    Equal(0, missing.Payload[0x48 + 0x00],
        "sub1 not-found still clears tail+0x00 length byte (0x404EB1)");
    Equal(0, missing.Payload[0x48 + 0x10],
        "sub1 not-found still clears tail+0x10 length byte");
    // 长度字节之后的字节保持**请求原值**。这里是 "oldmaster" 的首字符 'o'，
    // 不是填充字节 —— 0x4035D8 只动长度字节，一个字节都不多清。
    Equal((byte)'o', missing.Payload[0x48 + 0x01],
        "sub1 not-found leaves bytes AFTER the length byte untouched "
        + "(request's 'oldmaster' text survives; 0x4035D8 zero-fills nothing)");
    Equal(req.Tail[0x01], missing.Payload[0x48 + 0x01],
        "sub1 not-found byte equals the original request byte");
    // 输入槽在未找到时也不受影响。
    EqualText("lookupname", Text(ReadShortString(missing.Payload, 0x48 + 0x35)),
        "sub1 not-found echoes tail+0x35 unchanged");
}

// sub 10 (QueryMembers)。原版 body 0x59454C..0x594639 + worker 0x593B74。
// 覆盖：路由、长度门、查询槽 vs 回显槽不同源、0x29 记录内部布局、count<=0 边界。
void Sub10QueryMembers()
{
    Equal((int)NativeZongpaiReplyMode.Sender,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.QueryMembers), "sub10 route = Sender @0x594556");
    // group 3 入口 0x59454C `83 7d 10 54` + `0f 8c` ⇒ 有门。
    True(NativeZongpaiProtocol.RequiresLengthGate(
        NativeZongpaiSubCommand.QueryMembers), "sub10 is 0x54-gated via group3");
    False(NativeZongpaiProtocol.TryDecodeRequest(
            Frame(NativeZongpaiSubCommand.QueryMembers, 0x53), out _, out _),
        "sub10 0x53 tail refused (0x594550 jl)");

    // ★ 查询键取 tail+0x00（0x594563 `mov edx,[ebp-4]`，**无 add**），
    //   回显名取 tail+0x35（0x594609 `add edx,0x35`）—— 两个不同的槽。
    var frame = Frame(NativeZongpaiSubCommand.QueryMembers, 0x54,
        tail =>
        {
            PutShortString(tail, 0x00, "querykey");
            PutShortString(tail, 0x35, "echoname");
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "sub10 decode: " + err);
    EqualText("querykey", Text(req.QueryMembersMasterName),
        "sub10 query key resolves to tail+0x00 (@0x594563, no add)");
    EqualText("echoname", Text(req.QueryMembersEchoName),
        "sub10 echo name resolves to tail+0x35 (@0x594609 add edx,0x35)");
    True(Text(req.QueryMembersMasterName) != Text(req.QueryMembersEchoName),
        "sub10 query key and echo name must NOT be the same slot");

    // 记录内部布局（worker 0x593B74 的填充循环）。⚠️ 顺序反直觉：
    // 容量 20 的 RoleName 在 +0x00，容量 15 的 MemberName 在 +0x15。
    Equal(0x00, NativeZongpaiProtocol.MemberRecordRoleNameOffset,
        "member record RoleName at +0x00 (0x593C8D/0x593C90 cl=0x14)");
    Equal(0x15, NativeZongpaiProtocol.MemberRecordMemberNameOffset,
        "member record MemberName at +0x15 (0x593C60 add eax,0x15 / cl=0x0F)");
    Equal(0x25, NativeZongpaiProtocol.MemberRecordLevelOffset,
        "member record Level word at +0x25 (0x593CDD)");
    Equal(0x27, NativeZongpaiProtocol.MemberRecordOnlineOffset,
        "member record online byte at +0x27 (0x593CEA)");
    True(NativeZongpaiProtocol.MemberRecordRoleNameOffset
         < NativeZongpaiProtocol.MemberRecordMemberNameOffset,
        "RoleName precedes MemberName inside the record (counter-intuitive order)");

    var record = NativeZongpaiProtocol.BuildMemberRecord(
        System.Text.Encoding.ASCII.GetBytes("theRoleName"),
        System.Text.Encoding.ASCII.GetBytes("theMember"),
        0x1234, true);
    Equal(0x29, record.Length, "member record is 0x29 bytes (0x59457F imul 0x29)");
    EqualText("theRoleName", Text(ReadShortString(record, 0x00)),
        "record +0x00 carries RoleName");
    EqualText("theMember", Text(ReadShortString(record, 0x15)),
        "record +0x15 carries MemberName");
    Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x25, 2)),
        "record +0x25 is a WORD level (0x593CDD mov word [rec+0x25],ax)");
    Equal(1, record[0x27], "record +0x27 online flag set");
    // +0x28 原版从不写入，恒 0（0x593BFC 整块清零）。
    Equal(0, record[0x28],
        "record +0x28 is never written by the original (stays 0)");
    // 容量：RoleName 20、MemberName 15。
    var capped = NativeZongpaiProtocol.BuildMemberRecord(
        System.Text.Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwxyz"),
        System.Text.Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRST"),
        0, false);
    Equal(20, capped[0x00], "record RoleName capacity 20 (0x593C90 cl=0x14)");
    Equal(15, capped[0x15], "record MemberName capacity 15 (0x593C63 cl=0x0F)");
    Equal(0, capped[0x27], "record online flag clear when offline");

    // 帧长公式与 body+2 = COUNT（不是结果码）。
    var records = new byte[2 * 0x29];
    for (var i = 0; i < records.Length; i++) records[i] = (byte)(i + 0x40);
    var reply = NativeZongpaiProtocol.CreateMemberListResponse(req, 2, records);
    Equal(2 * 0x29 + 0x48, reply.Payload.Length,
        "sub10 payload = n*0x29 + 0x48 (0x5945CB)");
    Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(reply.Payload.AsSpan(2, 2)),
        "sub10 body+2 is the COUNT, not a result code (0x5945F0)");
    EqualText("echoname", Text(ReadShortString(reply.Payload, 0x25)),
        "sub10 tail+0x35 lands at body+0x25 (0x594603/0x594609)");

    // ★ count <= 0 的边界：0x594613 `cmp [ebp-0x18],0 / jle 0x59476B` 位于
    // 分配器**之后**（分配在 0x594586..0x59460E），所以空结果**仍然发一个空帧**。
    var empty = NativeZongpaiProtocol.CreateMemberListResponse(
        req, 0, ReadOnlySpan<byte>.Empty);
    True(empty != null,
        "sub10 count<=0 STILL emits a frame (0x594613 jle is AFTER the allocator)");
    Equal(0x48, empty.Payload.Length,
        "sub10 empty payload = 0*0x29 + 0x48");
    Equal(0x54, empty.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "sub10 empty total = 0x54 (0x594583 add eax,0x54)");
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(empty.Payload.AsSpan(2, 2)),
        "sub10 empty count is 0 in body+2");
    // 空帧仍然带命令字与回显名 —— 那两处写在 jle 之前。
    Equal(0x0071, BinaryPrimitives.ReadUInt16LittleEndian(empty.Payload),
        "sub10 empty frame still carries command 0x71 (0x5945E4)");
    EqualText("echoname", Text(ReadShortString(empty.Payload, 0x25)),
        "sub10 empty frame still echoes tail+0x35 (0x59460E precedes 0x594613)");
    // 负数按 0 处理，不得产生负长度。
    var negative = NativeZongpaiProtocol.CreateMemberListResponse(
        req, -3, ReadOnlySpan<byte>.Empty);
    Equal(0x48, negative.Payload.Length, "sub10 negative count clamps to empty");
}

// sub 11/12 (ReadNotice / ModifyNotice)。原版 group 4 入口 0x59463E +
// worker 0x593D30 / 0x593D70。覆盖：**唯一无 0x54 门的组**、组内二级分流、
// header 取名（非 tail）、sub12 的 0x80 上界门、NUL 截断。
void Sub11And12Notice()
{
    // ★ group 4 入口 0x59463E 首字节是 `8b 45 f4`（mov eax,[ebp-0xC]），
    //   不是 `83 7d 10 54` —— 全五个组里唯一不做长度门的组。
    False(NativeZongpaiProtocol.RequiresLengthGate(
        NativeZongpaiSubCommand.ReadNotice),
        "sub11 NOT gated: 0x59463E starts 8b 45 f4, not 83 7d 10 54");
    False(NativeZongpaiProtocol.RequiresLengthGate(
        NativeZongpaiSubCommand.ModifyNotice),
        "sub12 NOT gated: shares group4 entry 0x59463E");
    // 无门的直接后果：0x53 甚至 0 字节 tail 都必须被接受。
    True(NativeZongpaiProtocol.TryDecodeRequest(
            Frame(NativeZongpaiSubCommand.ReadNotice, 0x53), out _, out _),
        "sub11 accepts a 0x53 tail (no gate)");
    True(NativeZongpaiProtocol.TryDecodeRequest(
            Frame(NativeZongpaiSubCommand.ModifyNotice, 0), out _, out _),
        "sub12 accepts an empty tail (no gate)");
    // 反向锁定：group 1/2/3 的成员都必须有门，否则「唯一无门」这个判据就假了。
    var gatedCount = 0;
    for (var i = 0; i <= 13; i++)
        if (NativeZongpaiProtocol.RequiresLengthGate((NativeZongpaiSubCommand)i))
            gatedCount++;
    Equal(11, gatedCount,
        "exactly 11 sub-commands are 0x54-gated (group1 nine + sub9 + sub10); "
        + "sub0 is the exit, sub11/12 are the ungated group4");

    // ★ tail 解析 census。group 4 把 tail 当二进制正文透传，不做 ShortString
    //   校验；其余取槽的组必须照旧校验。整表钉死，避免新增成员静默落进错误一侧。
    var expectedTailParsing = new (NativeZongpaiSubCommand Sub, bool Parses, string Site)[]
    {
        (NativeZongpaiSubCommand.None, false, "0x59476B shared exit, never reads tail"),
        (NativeZongpaiSubCommand.Enumerate, true, "0x59417C add edx,0x35"),
        (NativeZongpaiSubCommand.CreateMaster, true, "0x5941FB add edx,0x35"),
        (NativeZongpaiSubCommand.AddMember, true, "0x5930DD/0x5930E7 slots"),
        (NativeZongpaiSubCommand.RemoveMember, true, "0x593198 tail+0x25"),
        (NativeZongpaiSubCommand.UpdateMemberRole, true, "0x5932A4 tail+0x10/0x25"),
        (NativeZongpaiSubCommand.UpdateStudentExp, true, "0x5943AC tail as ShortString"),
        (NativeZongpaiSubCommand.UpdateStudentAndMasterExp, true, "0x59433A tail+0x50"),
        (NativeZongpaiSubCommand.UpdateMasterExp, true, "0x594368 tail+0x4C"),
        (NativeZongpaiSubCommand.UpdateMasterLevel, true, "0x59446F group2 tail slots"),
        (NativeZongpaiSubCommand.QueryMembers, true, "0x594563 tail+0x00 / 0x594609 +0x35"),
        (NativeZongpaiSubCommand.ReadNotice, false, "0x59463E group4, tail unused"),
        (NativeZongpaiSubCommand.ModifyNotice, false, "0x59468A raw (ptr,len) passthrough"),
        (NativeZongpaiSubCommand.DeleteMaster, true, "0x594378 tail+0x35"),
    };
    Equal(14, expectedTailParsing.Length, "tail-parsing census covers 0..13");
    for (var i = 0; i < expectedTailParsing.Length; i++)
    {
        var (sub, parses, site) = expectedTailParsing[i];
        Equal(i, (int)sub, $"tail-parse census slot {i} is sub-command {i}");
        Equal(parses ? 1 : 0,
            NativeZongpaiProtocol.ParsesTailShortStrings(sub) ? 1 : 0,
            $"tail-parse census sub{i} ({site})");
    }

    // 功能判据：正文里 offset 0x25 的字节 > 0x0F 时，原版照收，C# 也必须照收。
    // 这正是修复前会被凭空拒掉的那一类请求（公告是 blob，任意字节合法）。
    var binaryTail = Frame(NativeZongpaiSubCommand.ModifyNotice, 0x54,
        tail =>
        {
            for (var i = 0; i < 0x54; i++) tail[i] = 0xFF;
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(binaryTail, out var binReq, out _),
        "sub12 accepts a tail whose 0x25 byte is 0xFF "
        + "(native never ShortString-validates it; Notice is a blob)");
    Equal(0x54, binReq.ModifyNoticeText.Length,
        "sub12 binary tail survives decode whole");
    Equal(0xFF, binReq.ModifyNoticeText[0x25],
        "sub12 keeps the raw 0xFF at offset 0x25 instead of rejecting the frame");
    // 同一条 tail 喂给 group 1 的子命令必须**仍然**被拒 —— 证明我没把校验删过头。
    var group1Binary = Frame(NativeZongpaiSubCommand.AddMember, 0x54,
        tail =>
        {
            for (var i = 0; i < 0x54; i++) tail[i] = 0xFF;
        });
    False(NativeZongpaiProtocol.TryDecodeRequest(group1Binary, out _, out _),
        "group1 STILL rejects an illegal ShortString length "
        + "(the relaxation must be scoped to group4 only)");

    Equal((int)NativeZongpaiReplyMode.Sender,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.ReadNotice), "sub11 route = Sender @0x59469F");
    Equal((int)NativeZongpaiReplyMode.Sender,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.ModifyNotice), "sub12 route = Sender @0x59469F");

    // ★ sub12 的**上界**门：worker 0x593DA0 `cmp [ebp+0xC],0x80` / 0x593DA7 `jg`。
    Equal(0x80, NativeZongpaiProtocol.MaximumNoticeLength,
        "sub12 notice upper bound is 0x80 (0x593DA0)");
    True(NativeZongpaiProtocol.IsNoticeLengthAccepted(0x80),
        "sub12 length 0x80 accepted (jg is strictly greater)");
    False(NativeZongpaiProtocol.IsNoticeLengthAccepted(0x81),
        "sub12 length 0x81 rejected (0x593DA7 jg 0x593ECC)");
    True(NativeZongpaiProtocol.IsNoticeLengthAccepted(0),
        "sub12 length 0 passes the upper bound (only 0x593DBE null-check stops it)");
    // 上界门与下界门是两件独立的事，别混。
    True(NativeZongpaiProtocol.MaximumNoticeLength
         > NativeZongpaiProtocol.MinimumTailLength,
        "sub12's 0x80 upper bound and the 0x54 lower gate are independent limits");

    // 取名走 HEADER+0x35（0x59464D/0x594679 `mov edx,[ebp-0xC]` + `add edx,0x35`），
    // **不是** tail —— tail 在 sub12 里整块当正文用。
    var frame = Frame(NativeZongpaiSubCommand.ModifyNotice, 0x20,
        tail =>
        {
            for (var i = 0; i < 0x20; i++) tail[i] = (byte)(0x61 + (i % 26));
        },
        header =>
        {
            PutShortString(header, 0x25, "acctname");
            PutShortString(header, 0x35, "mastername");
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "sub11/12 decode: " + err);
    EqualText("mastername", Text(req.NoticeMasterName),
        "sub11/12 master name resolves to HEADER+0x35 (@0x59467C)");
    True(Text(req.NoticeMasterName) != Text(req.TailSlot35),
        "sub11/12 must read the HEADER slot, not the tail slot");

    // ★ sub12 正文 = 整条 tail 原始字节（0x59468A `mov ecx,[ebp-4]` +
    //   0x59466B `mov eax,[ebp+0x10]` = 指针+长度），不做 ShortString 解析。
    Equal(0x20, req.ModifyNoticeText.Length,
        "sub12 notice text length == tail length (0x59466B pushes [ebp+0x10])");
    Equal(req.Tail.Length, req.ModifyNoticeText.Length,
        "sub12 notice text IS the whole tail, not a parsed slot");
    Equal(req.Tail[0], req.ModifyNoticeText[0],
        "sub12 notice text starts at tail+0x00 (no length prefix skipped)");

    // 回包帧长：0x5946B4 `add eax,0x54` / 0x5946B7 `inc eax` ⇒ len + 0x54 + 1。
    var notice = System.Text.Encoding.ASCII.GetBytes("notice-body");
    var reply = NativeZongpaiProtocol.CreateNoticeResponse(req, notice);
    Equal(notice.Length + 0x48 + 1, reply.Payload.Length,
        "sub11/12 payload = len + 0x48 + 1 (0x594700 add 0x48 / inc)");
    Equal(notice.Length + 0x54 + 1,
        reply.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "sub11/12 total = len + 0x54 + 1 (0x5946B4 add 0x54 / 0x5946B7 inc)");
    Equal(notice.Length,
        BinaryPrimitives.ReadUInt16LittleEndian(reply.Payload.AsSpan(2, 2)),
        "sub11/12 body+2 is the notice LENGTH (0x594722)");
    EqualText("acctname", Text(ReadShortString(reply.Payload, 0x25)),
        "sub11/12 header+0x25 -> body+0x25 (0x594738)");
    EqualText("mastername", Text(ReadShortString(reply.Payload, 0x35)),
        "sub11/12 header+0x35 -> body+0x35 (0x59474E)");
    // 末尾那一个 `+1` 字节是 0x5946D8 清零留下的 NUL，0x40C7DA stosb 也写它。
    Equal(0, reply.Payload[reply.Payload.Length - 1],
        "sub11/12 trailing +1 byte is the NUL terminator (0x40C7DA stosb)");

    // ★ NUL 截断：0x594766 `call 0x40C818` → 0x40C7B0 的 `repne scasb`
    //   在首个 0x00 处停，所以 body+2 声明的长度可以大于实际拷入的字节数。
    var embedded = new byte[] { 0x41, 0x42, 0x00, 0x43, 0x44 };
    var truncated = NativeZongpaiProtocol.CreateNoticeResponse(req, embedded);
    Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(
            truncated.Payload.AsSpan(2, 2)),
        "body+2 uses Length(str) (0x5946A9 call 0x404EB8), NOT the copied count");
    Equal(0x41, truncated.Payload[0x48 + 0], "notice byte 0 copied");
    Equal(0x42, truncated.Payload[0x48 + 1], "notice byte 1 copied");
    Equal(0, truncated.Payload[0x48 + 2], "notice copy stops at the first NUL");
    Equal(0, truncated.Payload[0x48 + 3],
        "bytes after the NUL are NOT copied (0x40C7BF repne scasb); "
        + "they keep the 0x5946D8 zero-fill");
    Equal(0, truncated.Payload[0x48 + 4],
        "second byte after the NUL also stays zero-filled");

    // 空公告：0x404EB8 对 nil 返 0 ⇒ 帧仍成立（长度 0），但派发器 0x594695
    // 的空判会让它根本不发 —— 这里只锁组帧，发不发在 GameSocService。
    var emptyNotice = NativeZongpaiProtocol.CreateNoticeResponse(
        req, ReadOnlySpan<byte>.Empty);
    Equal(0x48 + 1, emptyNotice.Payload.Length,
        "sub11/12 empty notice payload = 0 + 0x48 + 1");
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(
            emptyNotice.Payload.AsSpan(2, 2)),
        "sub11/12 empty notice length 0 in body+2");
}

// ---- helpers ----

// 写路径护栏。三条已修的写坏数据背离（sub 2 / sub 3 / sub 13）此前**没有任何断言
// 覆盖**：本审计只测组帧，DbSvrServiceRegressionCheck 不碰这条路径，且没有可注入的
// IZongpaiService 替身、没有审计实例化 GameSocService。所以那三处修复当时只有字节
// 证据、无法用变异测试证明护栏有效（commit 里记为 "Guardrail is owed"）。
//
// 这里把**槽 → 语义**的映射钉死。GameSocService 的调用点必须按这个映射取值，
// 任何一处退回原来的写法都会让下面某条断言变红：
//   sub 3 AddMember  : memberName = tail+0x25 (容量 15)，roleName = tail+0x10 (容量 20)
//   sub 2 CreateMaster: masterName = tail+0x35，masterLevel = tail+0x4C 的**低 16 位**，
//                       studentExp = tail+0x50 (u32)
//   sub 13 DeleteMaster: masterName = tail+0x35（并由成员数==1 的门保护，门本身在
//                        GameSocService，无法在本审计内触达，故此处只锁槽映射）
void WritePathFieldMapping()
{
    // 取值互不相同，且刻意让 0x4C 的高 16 位非零 —— 若实现漏掉 movzx（原版
    // 0x59420C `mov cx, word ptr [eax+0x4c]` 只取 16 位），低位截断就会暴露。
    const int value4C = unchecked((int)0xABCD0201);
    const int value50 = 0x0304FEDC;
    var frame = Frame(NativeZongpaiSubCommand.AddMember, 0x54,
        tail =>
        {
            PutShortString(tail, 0x00, "themaster");
            PutShortString(tail, 0x10, "the_role_name_20chr");   // 19 <= 20 容量
            PutShortString(tail, 0x25, "the_member_15c");        // 14 <= 15 容量
            PutShortString(tail, 0x35, "othername");
            BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(0x4C, 4), value4C);
            BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(0x50, 4), value50);
        });
    True(NativeZongpaiProtocol.TryDecodeRequest(frame, out var req, out var err),
        "decode: " + err);

    // ⚠️ 以下断言打的是 NativeZongpaiRequest 上的**语义访问器**，不是原始槽属性。
    // 这是有意的：GameSocService 的每个 case 现在都经由这些访问器取参数
    // （8 处调用点已改道），所以断言它们等于在断言调用点的取值路径。
    // 若只断言 TailSlot25/TailSlot10 这类原始槽，那是与调用点平行的描述 ——
    // 调用点改回传反也照样绿，即新的假绿。这一点先前踩过一次。

    // sub 3：0x593140 `insert into ZongpaiMember(MasterName, MemberName, RoleName)`
    // TVarRec slot1 = [ebp+8] = MemberName、slot2 = [ebp-0xC] = RoleName（0x5930DD/0x5930E7）。
    // DDL 0x5BF0C4: MemberName varchar(15) / RoleName varchar(20)，与槽容量一一对应，
    // 且 `unique key MemberName_Index(MemberName)` 使传反必然撞唯一键。
    EqualText("the_member_15c", Text(req.AddMemberMemberName),
        "sub3 memberName must resolve to tail+0x25 (cap 15 <-> varchar(15))");
    EqualText("the_role_name_20chr", Text(req.AddMemberRoleName),
        "sub3 roleName must resolve to tail+0x10 (cap 20 <-> varchar(20))");
    // 反向锁定：两者不得同源（传反时这条也会红）。
    True(Text(req.AddMemberMemberName) != Text(req.AddMemberRoleName),
        "sub3 memberName and roleName must not resolve to the same slot");

    // sub 2：0x592FD4 `values("%s", %d, %u, Now())`
    // 0x59420C mov cx,word[eax+0x4c] -> MasterLevel（**只有 16 位**）
    // 0x5941EE mov eax,[eax+0x50] / push -> StudentExp（32 位，DDL int unsigned）
    Equal(value4C, req.TailValue4C, "sub2 raw tail+0x4C survives decode");
    Equal(value50, req.TailValue50, "sub2 raw tail+0x50 survives decode");
    Equal(0x0201, req.CreateMasterLevel,
        "sub2 masterLevel is the LOW 16 bits of tail+0x4C (native movzx @0x59420C; "
        + "DDL MasterLevel smallint unsigned)");
    Equal(value50, unchecked((int)req.CreateMasterStudentExp),
        "sub2 studentExp is tail+0x50 as u32 (was hard-coded 0 in the INSERT)");
    // 反向锁定：level 与 exp 不得取自同一槽（此前 level 误取 TailValue50）。
    True(req.CreateMasterLevel != (ushort)req.CreateMasterStudentExp,
        "sub2 masterLevel and studentExp must not read the same tail slot");

    // sub 6 读 tail+0x4C 的**完整 dword**（0x5943BA），与 sub 2 读同偏移的 word
    // 是同一字段的两种宽度 —— 故这两个访问器必须都存在且宽度不同。
    Equal(value4C, unchecked((int)req.StudentExpDelta),
        "sub6 StudentExp delta is the FULL dword at tail+0x4C (@0x5943BA)");
    Equal(value4C, unchecked((int)req.MasterExpDelta),
        "sub8 MasterExp delta is the full dword at tail+0x4C (@0x594368)");
    True(req.StudentExpDelta != req.CreateMasterLevel,
        "sub6 delta (dword) and sub2 level (word) must not be the same value here");

    // sub 7 的转换额度取 tail+0x50（0x59433A），与 sub 6/8 的 0x4C 不同槽。
    Equal(value50, unchecked((int)req.ConvertExpAmount),
        "sub7 convert amount comes from tail+0x50 (@0x59433A)");
    True(req.ConvertExpAmount != req.StudentExpDelta,
        "sub7 amount and sub6 delta must not resolve to the same slot");

    // sub 2 / sub 9 / sub 13 的 masterName 取自 tail+0x35（0x5941FB add edx,0x35），
    // **不是** tail+0x00。
    EqualText("othername", Text(req.MasterNameSlot35),
        "sub2/sub9/sub13 masterName resolves to tail+0x35");
    True(Text(req.MasterNameSlot35) != Text(req.TailSlot00),
        "tail+0x35 and tail+0x00 are distinct slots");
}

LegacyDbServerFrame Frame(NativeZongpaiSubCommand sub, int tailLength,
    Action<byte[]> fillTail = null, Action<byte[]> fillHeader = null)
{
    var header = new byte[NativeZongpaiProtocol.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(header,
        NativeZongpaiProtocol.RequestCommand);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), (int)sub);
    fillHeader?.Invoke(header);

    var tail = new byte[tailLength];
    fillTail?.Invoke(tail);

    var payload = new byte[header.Length + tail.Length];
    header.CopyTo(payload, 0);
    tail.CopyTo(payload, header.Length);
    return new LegacyDbServerFrame(1, 0, payload);
}

void PutShortString(byte[] buffer, int offset, string value)
{
    var bytes = System.Text.Encoding.ASCII.GetBytes(value);
    buffer[offset] = (byte)bytes.Length;
    bytes.CopyTo(buffer, offset + 1);
}

byte[] ReadShortString(byte[] buffer, int offset)
{
    var length = buffer[offset];
    return buffer.AsSpan(offset + 1, length).ToArray();
}

string Text(byte[] bytes) => System.Text.Encoding.ASCII.GetString(bytes ?? []);

void Run(string name, Action test)
{
    try { test(); }
    catch (Exception ex)
    {
        failures.Add($"FAIL [{name}] {ex.Message}");
    }
}

void Equal(int expected, int actual, string what)
{
    asserts++;
    if (expected != actual)
        throw new Exception($"{what}: expected {expected} (0x{expected:X}), "
                            + $"got {actual} (0x{actual:X})");
}

void EqualText(string expected, string actual, string what)
{
    asserts++;
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new Exception($"{what}: expected '{expected}', got '{actual}'");
}

void True(bool condition, string what)
{
    asserts++;
    if (!condition) throw new Exception($"{what}: expected true");
}

void False(bool condition, string what)
{
    asserts++;
    if (condition) throw new Exception($"{what}: expected false");
}
