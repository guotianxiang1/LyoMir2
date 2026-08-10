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

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"NativeZongpaiProtocolCheck PASS tests=12 asserts={asserts} "
                  + "type1=0170 reply=0071 gate=0x54 std=0xA8/0x9C "
                  + "lvl=0x84/0x78 member=0x29 dispatch=0x594122");
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
    var frame = Frame(NativeZongpaiSubCommand.ModifyNotice, 0x54,
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
