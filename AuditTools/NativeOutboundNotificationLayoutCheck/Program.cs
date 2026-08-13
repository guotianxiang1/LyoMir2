// Pins the wire bytes of the ten frames the original Delphi DBServer pushes to
// GameServers and C# DBSvr had no builder for.  Every expectation below is a
// transcription of a DBServer store instruction, cited inline; if someone
// "tidies" an offset the encoded frame changes and this fails.
//
// DBServer VAs are staging/_dbsvr_reunpack_work/dbserver_CODE_live.bin with
// VA = 0x401000 + offset.  Using 0x400000 makes every cross-reference miss.
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule.Packet;

var failures = new List<string>();

static byte[] Encode(LegacyDbServerFrame frame)
{
    if (!LegacyDbServerFrameCodec.TryEncode(frame, out var data, out var error))
        throw new InvalidOperationException("encode failed: " + error);
    return data;
}

void Envelope(string tag, byte[] wire, ushort type, int dataLength)
{
    if (BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)) != 0x33AABB77u)
        failures.Add($"{tag}: magic != 0x33AABB77");
    var actualType = BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(4, 2));
    if (actualType != type)
        failures.Add($"{tag}: frame Type {actualType} != {type}");
    var actualLength = BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4));
    if (actualLength != dataLength)
        failures.Add($"{tag}: DataLength 0x{actualLength:X} != 0x{dataLength:X}");
    if (wire.Length != 12 + dataLength)
        failures.Add($"{tag}: total {wire.Length} != {12 + dataLength}");
}

void Word(string tag, byte[] wire, int bodyOffset, ushort expected)
{
    var actual = BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12 + bodyOffset, 2));
    if (actual != expected)
        failures.Add($"{tag}: body+0x{bodyOffset:X2} word 0x{actual:X4} != 0x{expected:X4}");
}

void Dword(string tag, byte[] wire, int bodyOffset, int expected)
{
    var actual = BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(12 + bodyOffset, 4));
    if (actual != expected)
        failures.Add($"{tag}: body+0x{bodyOffset:X2} dword {actual} != {expected}");
}

void ShortStr(string tag, byte[] wire, int bodyOffset, byte[] expected)
{
    var at = 12 + bodyOffset;
    if (wire[at] != expected.Length)
    {
        failures.Add($"{tag}: body+0x{bodyOffset:X2} length {wire[at]} != {expected.Length}");
        return;
    }
    for (var i = 0; i < expected.Length; i++)
        if (wire[at + 1 + i] != expected[i])
            failures.Add($"{tag}: body+0x{bodyOffset:X2} byte {i} differs");
}

// Every byte the constructor does not write must be zero: the type-1 producers
// allocate through 0x40ADCC, which is AllocMem (0x40ADD8 call 0x402F48 GetMem,
// then 0x40ADE8 call 0x4036E8 FillChar with ecx = 0), not bare GetMem.
void ZeroExcept(string tag, byte[] wire, params (int Offset, int Length)[] written)
{
    for (var i = 0; i < NativeOutboundNotificationProtocol.Type1BodySize; i++)
    {
        var covered = false;
        foreach (var (offset, length) in written)
            if (i >= offset && i < offset + length) { covered = true; break; }
        if (!covered && wire[12 + i] != 0)
            failures.Add($"{tag}: body+0x{i:X2} is 0x{wire[12 + i]:X2}, native leaves it zero");
    }
}

var account = new byte[] { 0x61, 0x62, 0x63, 0x31, 0x32 };          // "abc12"
var charName = new byte[] { 0xB6, 0xAB, 0xB7, 0xBD };                // GBK, 2 chars

// ---- 0x0046 / 0x0047 : sub_598618 -----------------------------------------
// 0x59866D cmp byte [ebp+8],0 / 0x598671 je -> 0x598676 mov word [body],0x46
//                                            \ 0x598680 mov word [body],0x47
// 0x5986A1 add eax,0x25 / 0x5986A4 mov cl,0xF -> character at body+0x25 cap 15
// 0x5986AD movzx eax,byte [ebp-9] / 0x5986B3 mov [body+4],eax
foreach (var (found, expected) in new[] { (true, (ushort)0x0046), (false, (ushort)0x0047) })
{
    var tag = $"0x{expected:X4}";
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreateCharacterQuery(found, charName, 0x81));
    Envelope(tag, wire, 1, 0x48);
    Word(tag, wire, 0x00, expected);
    Dword(tag, wire, 0x04, 0x81);          // zero-extended byte, not sign-extended
    ShortStr(tag, wire, 0x25, charName);
    ZeroExcept(tag, wire, (0x00, 2), (0x04, 4), (0x25, 1 + charName.Length));
}

// ---- 0x0058 : sub_5984A8 ---------------------------------------------------
// 0x598500 mov word [body],0x58
// 0x598521 add eax,0x10 / 0x598524 mov cl,0x14 -> account at body+0x10 cap 20
// 0x598531 mov [body+4],ecx
{
    const string tag = "0x0058";
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreateAccountNotification(account, 0x1234));
    Envelope(tag, wire, 1, 0x48);
    Word(tag, wire, 0x00, 0x0058);
    Dword(tag, wire, 0x04, 0x1234);
    ShortStr(tag, wire, 0x10, account);
    ZeroExcept(tag, wire, (0x00, 2), (0x04, 4), (0x10, 1 + account.Length));
}

// ---- 0x0078 : sub_5CF514 arm 0x5CF763 --------------------------------------
// 0x5CF798 mov word [ebp-0x68],0x78      body+0x00
// 0x5CF7A5 mov word [ebp-0x66],ax        body+0x02 <- word[msg+4]
// 0x5CF7AF mov dword [ebp-0x64],eax      body+0x04 <- dword[msg+8]
// 0x5CF7CE lea eax,[ebp-0x43] cl=0x0F    body+0x25 character
// 0x5CF7F4 lea eax,[ebp-0x58] cl=0x14    body+0x10 account
{
    const string tag = "0x0078";
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreateSessionState(2, 0x55667788, account, charName));
    Envelope(tag, wire, 1, 0x48);
    Word(tag, wire, 0x00, 0x0078);
    Word(tag, wire, 0x02, 2);
    Dword(tag, wire, 0x04, 0x55667788);
    ShortStr(tag, wire, 0x10, account);
    ShortStr(tag, wire, 0x25, charName);
    ZeroExcept(tag, wire, (0x00, 2), (0x02, 2), (0x04, 4),
        (0x10, 1 + account.Length), (0x25, 1 + charName.Length));
}

// ---- 0x0079 : sub_5CEBC0, word[msg+4] == 1 ---------------------------------
// 0x5CEC06 mov word [ebp-0x58],0x79
// 0x5CEC5E mov [ebp-0x54],eax   body+0x04 <- dword[src+0xC]
// 0x5CEC68 mov [ebp-0x56],ax    body+0x02 <- word[src+0x10]
{
    const string tag = "0x0079";
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreateSessionDetailA(0x0BADF00D, 0x2A, account, charName));
    Envelope(tag, wire, 1, 0x48);
    Word(tag, wire, 0x00, 0x0079);
    Word(tag, wire, 0x02, 0x2A);
    Dword(tag, wire, 0x04, 0x0BADF00D);
    ShortStr(tag, wire, 0x10, account);
    ShortStr(tag, wire, 0x25, charName);
}

// ---- 0x007A : sub_5CEBC0, word[msg+4] == 2 ---------------------------------
// 0x5CEC9C mov word [ebp-0x58],0x7A
// tail 0x5CECF1/0x5CECF4 writes ONLY body+0x04 -- there is no body+0x02 store,
// which is the single difference from the 0x0079 arm.
{
    const string tag = "0x007A";
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreateSessionDetailB(0x0BADF00D, account, charName));
    Envelope(tag, wire, 1, 0x48);
    Word(tag, wire, 0x00, 0x007A);
    Word(tag, wire, 0x02, 0);
    Dword(tag, wire, 0x04, 0x0BADF00D);
    ShortStr(tag, wire, 0x10, account);
    ShortStr(tag, wire, 0x25, charName);
}

// ---- 0x012D : sub_59E1CC ---------------------------------------------------
// 0x59E228 mov word [body+2],ax   <- STORED BEFORE the command word
// 0x59E22F mov word [body],0x12D
// 0x59E250 add eax,0x10 cl=0x14   account
// 0x59E276 add eax,0x25 cl=0x0F   character
{
    const string tag = "0x012D";
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreateAccountCharacterBroadcast(7, account, charName));
    Envelope(tag, wire, 1, 0x48);
    Word(tag, wire, 0x00, 0x012D);
    Word(tag, wire, 0x02, 7);
    ShortStr(tag, wire, 0x10, account);
    ShortStr(tag, wire, 0x25, charName);
    ZeroExcept(tag, wire, (0x00, 2), (0x02, 2),
        (0x10, 1 + account.Length), (0x25, 1 + charName.Length));
}

// ---- 0x013B : sub_59E338 ---------------------------------------------------
// 0x59E38D mov word [body],0x13B
// 0x59E3A4 mov [body+8],edx / 0x59E3AA mov [body+0xC],edx
// No string slot at all, and the only user of body+0x08 / body+0x0C.
{
    const string tag = "0x013B";
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreatePairedScalarBroadcast(0x11223344, unchecked((int)0x99AABBCC)));
    Envelope(tag, wire, 1, 0x48);
    Word(tag, wire, 0x00, 0x013B);
    Dword(tag, wire, 0x08, 0x11223344);
    Dword(tag, wire, 0x0C, unchecked((int)0x99AABBCC));
    ZeroExcept(tag, wire, (0x00, 2), (0x08, 4), (0x0C, 4));
}

// ---- 0x0072 : sub_59D020 -> sub_59CE94, TYPE 2 -----------------------------
// 0x59CEF1 mov dword [frame],0x33AABB77
// 0x59CEFA mov word [frame+4],2          <- type 2, NOT 1
// 0x59CF0A mov [frame+8],eax  with eax = payloadLen + 0x0C
// 0x59D039 FillChar(header,0xC,0) / 0x59D03E word 0x72 / 0x59D044 word 0
// 0x59CF32 lea edx,[frame+0x18] / 0x59CF38 Move -> payload follows the header
{
    const string tag = "0x0072";
    var payload = new byte[] { 1, 2, 3, 4, 5 };
    var wire = Encode(NativeOutboundNotificationProtocol.CreateRelayBroadcast(payload));
    Envelope(tag, wire, 2, 0x0C + payload.Length);
    Word(tag, wire, 0x00, 0x0072);
    Word(tag, wire, 0x02, 0);
    Dword(tag, wire, 0x04, 0);
    Dword(tag, wire, 0x08, 0);
    for (var i = 0; i < payload.Length; i++)
        if (wire[12 + 0x0C + i] != payload[i])
            failures.Add($"{tag}: payload byte {i} differs");
}

// ---- 0x0130 : sub_59E298, TYPE 2 -------------------------------------------
// 0x59E2D2 mov word [frame+4],2
// 0x59E2E3 mov [frame+8],eax  with eax = dword[record] + 0x0C
// 0x59E2F2/0x59E2FD/0x59E305 zero body+2 / body+4 / body+8
// 0x59E30B mov word [body],0x130
// 0x59E313 lea edx,[buf+0x18] / 0x59E31E Move(src = the record pointer itself)
{
    const string tag = "0x0130";
    var record = new byte[] { 0x08, 0, 0, 0, 0xDE, 0xAD, 0xBE, 0xEF };
    var wire = Encode(NativeOutboundNotificationProtocol.CreateBulkBroadcast(record));
    Envelope(tag, wire, 2, 0x0C + record.Length);
    Word(tag, wire, 0x00, 0x0130);
    Word(tag, wire, 0x02, 0);
    Dword(tag, wire, 0x04, 0);
    Dword(tag, wire, 0x08, 0);
    // The leading length dword is part of the transmitted payload, because the
    // Move source is the record pointer, not record+4.
    if (BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(12 + 0x0C, 4)) != record.Length)
        failures.Add($"{tag}: the record's own length dword must be transmitted");
}

// ShortString capacities truncate rather than throw: sub_4035D8 does
// mov bl,[edx] / cmp cl,bl / jbe +2 / mov ecx,ebx.
{
    const string tag = "truncation";
    var longAccount = new byte[40];
    for (var i = 0; i < longAccount.Length; i++) longAccount[i] = (byte)('a' + i % 26);
    var wire = Encode(NativeOutboundNotificationProtocol
        .CreateAccountNotification(longAccount, 0));
    if (wire[12 + 0x10] != 20)
        failures.Add($"{tag}: account should truncate to 20, got {wire[12 + 0x10]}");
    // body+0x25 is the next slot and must stay untouched by the truncated copy
    if (wire[12 + 0x25] != 0)
        failures.Add($"{tag}: a truncated SS20 must not spill into body+0x25");
}

if (failures.Count > 0)
{
    Console.WriteLine("NativeOutboundNotificationLayoutCheck FAIL:");
    foreach (var f in failures) Console.WriteLine("  " + f);
    return 1;
}

Console.WriteLine("NativeOutboundNotificationLayoutCheck PASS " +
    "codes=0x0046,0x0047,0x0058,0x0072,0x0078,0x0079,0x007A,0x012D,0x0130,0x013B " +
    "type1Body=0x48 type2Header=0x0C ss20@0x10 ss15@0x25 " +
    "source=DBServer dbserver_CODE_live.bin VA=0x401000+offset");
return 0;
