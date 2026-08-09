// Asserts the native 宗派/师门 sub-protocol (type1 0x0170) byte contract against
// the 战神 DBServer disassembly. Every expected value below is quoted from a
// specific instruction address in DBServer_unpacked.exe (CODE segment is not
// VMProtect-damaged, so these were read directly, not from pseudocode).
// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3之二.
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule.Packet;

var failures = new List<string>();
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

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeZongpaiProtocolCheck PASS tests=11 "
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
    // `mov [ebp-0x10], 1` at 0x5941E4/0x59431C/0x59434A/0x594378/0x59446F/
    // 0x594556/0x59469F → sender-only.
    foreach (var sub in new[]
             {
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
    Equal((int)NativeZongpaiReplyMode.None,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.None), "mode 0");
    Equal((int)NativeZongpaiReplyMode.None,
        (int)NativeZongpaiProtocol.GetReplyMode(
            NativeZongpaiSubCommand.Enumerate), "mode 1");
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
    if (expected != actual)
        throw new Exception($"{what}: expected {expected} (0x{expected:X}), "
                            + $"got {actual} (0x{actual:X})");
}

void EqualText(string expected, string actual, string what)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new Exception($"{what}: expected '{expected}', got '{actual}'");
}

void True(bool condition, string what)
{
    if (!condition) throw new Exception($"{what}: expected true");
}

void False(bool condition, string what)
{
    if (condition) throw new Exception($"{what}: expected false");
}
