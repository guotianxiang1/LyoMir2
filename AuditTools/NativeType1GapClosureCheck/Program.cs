// Asserts the byte contracts for the last four type1 gaps (0x0192, 0x0193,
// 0x0156, 0x0173) AND that the whole type1 opcode space is now accounted for:
// every opcode the 战神 dispatcher has a body for is either handled by C# or
// listed as a faithful silent no-op. Constants are quoted from instruction
// addresses in DBServer_unpacked.exe.
// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule;
using SystemModule.Packet;

var failures = new List<string>();
Run("gate-report commands and sizes", GateReportConstants);
Run("gate-report exact tail-length gates", GateReportLengthGates);
Run("gate-report record/tail delta is consistent", GateReportDelta);
Run("gate-report fields", GateReportFields);
Run("global-relay commands and sizes", GlobalRelayConstants);
Run("global-relay 0156 fields", GlobalRelayRegistrationFields);
Run("global-relay 0173 packed dword", GlobalRelayQueryFields);
Run("type1 opcode space fully accounted", OpcodeSpaceAccounted);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeType1GapClosureCheck PASS tests=8 "
                  + "gate=0192/0193 tail=0x87/0xB5 lg=0x7DF/0x7E0 "
                  + "relay=0156/0173 direct=0x1F42 queue=0x274D/0x2750 "
                  + "opcodes=39/39");
return 0;

void GateReportConstants()
{
    Equal(0x0192, NativeGateReportProtocol.Type1RequestCommand, "0192 command");
    Equal(0x0193, NativeGateReportProtocol.Type2RequestCommand, "0193 command");
    // 0x5CFE64 / 0x5CFFA9 `mov dx, 0x7DF` / `mov dx, 0x7E0`.
    Equal(0x07DF, NativeGateReportProtocol.Type1LoginGateCommand, "0x7DF");
    Equal(0x07E0, NativeGateReportProtocol.Type2LoginGateCommand, "0x7E0");
    // 0x5CFD67 `mov edx,0xF3` / 0x5CFE8B `mov edx,0x121`.
    Equal(0xF3, NativeGateReportProtocol.Type1RecordSize, "0xF3 record");
    Equal(0x121, NativeGateReportProtocol.Type2RecordSize, "0x121 record");
    // 0x5CFD71 / 0x5CFE95 write 4 into the record's first byte.
    Equal(4, NativeGateReportProtocol.RecordTag, "record tag");
    // 0x5D18D1 `add eax, 0x10` — the LoginGate header is 16 bytes, unlike the
    // 12-byte GameServer frame header.
    Equal(0x10, NativeGateReportProtocol.LoginGateHeaderSize, "LG header 16");
    Equal(12, LegacyDbServerFrameCodec.HeaderSize, "GS header 12");
}

void GateReportLengthGates()
{
    // 0x5993A4 `cmp dword [ebp+8], 0x87` and 0x5993F6 `cmp …, 0xB5`; the caller
    // sets [ebp+8] to the TAIL length (0x59DDBB `sub …,0x48`), so these are exact
    // fixed-size gates and a mismatch is a silent `jne 0x59953D`.
    Equal(0x87, NativeGateReportProtocol.Type1TailLength, "0192 tail 0x87");
    Equal(0xB5, NativeGateReportProtocol.Type2TailLength, "0193 tail 0xB5");

    // Exact length accepted.
    True(NativeGateReportProtocol.TryDecodeRequest(
            Frame(0x0192, 0x87), out _, out _), "0192 exact accepted");
    True(NativeGateReportProtocol.TryDecodeRequest(
            Frame(0x0193, 0xB5), out _, out _), "0193 exact accepted");

    // One byte off in either direction refused — it is `jne`, not `jl`.
    False(NativeGateReportProtocol.TryDecodeRequest(
        Frame(0x0192, 0x86), out _, out _), "0192 short refused");
    False(NativeGateReportProtocol.TryDecodeRequest(
        Frame(0x0192, 0x88), out _, out _), "0192 long refused");
    False(NativeGateReportProtocol.TryDecodeRequest(
        Frame(0x0193, 0xB4), out _, out _), "0193 short refused");
    False(NativeGateReportProtocol.TryDecodeRequest(
        Frame(0x0193, 0xB6), out _, out _), "0193 long refused");

    // The two sub-types must not accept each other's length.
    False(NativeGateReportProtocol.TryDecodeRequest(
        Frame(0x0192, 0xB5), out _, out _), "0192 rejects 0193 length");
    False(NativeGateReportProtocol.TryDecodeRequest(
        Frame(0x0193, 0x87), out _, out _), "0193 rejects 0192 length");
}

void GateReportDelta()
{
    // 0xF3 - 0x87 == 0x121 - 0xB5 == 0x6C. Both sub-types add the same 0x6C of
    // record scaffolding to their tail, which cross-validates the four constants.
    var d1 = NativeGateReportProtocol.Type1RecordSize
             - NativeGateReportProtocol.Type1TailLength;
    var d2 = NativeGateReportProtocol.Type2RecordSize
             - NativeGateReportProtocol.Type2TailLength;
    Equal(0x6C, d1, "0192 record-tail delta");
    Equal(0x6C, d2, "0193 record-tail delta");
    Equal(d1, d2, "deltas agree across sub-types");
}

void GateReportFields()
{
    // 0x5993BE / 0x5993D9 read header+0x25 and header+0x10.
    var frame = Frame(0x0192, 0x87, h =>
    {
        PutShortString(h, 0x10, "GateName");
        PutShortString(h, 0x25, "slot25");
    });
    True(NativeGateReportProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);
    Equal((int)NativeGateReportKind.Type1, (int)req.Kind, "kind");
    EqualText("GateName", Text(req.LookupName), "header+0x10");
    EqualText("slot25", Text(req.Slot25), "header+0x25");
    Equal(0x87, req.Tail.Length, "tail captured");

    Equal(0x0192, NativeGateReportProtocol.GetRequestCommand(
        NativeGateReportKind.Type1), "kind→command 1");
    Equal(0x0193, NativeGateReportProtocol.GetRequestCommand(
        NativeGateReportKind.Type2), "kind→command 2");
}

void GlobalRelayConstants()
{
    Equal(0x0156, NativeGlobalRelayProtocol.RegistrationCommand, "0156");
    Equal(0x0173, NativeGlobalRelayProtocol.QueryCommand, "0173");
    // 0x5A3359 `mov word [buf+4], 0x1F42` (8002) and 0x5A335F payload 0x40.
    Equal(0x1F42, NativeGlobalRelayProtocol.DirectSendCommand, "8002");
    Equal(8002, NativeGlobalRelayProtocol.DirectSendCommand, "8002 decimal");
    Equal(0x40, NativeGlobalRelayProtocol.DirectSendPayloadLength, "payload 0x40");
    Equal(0x4C, NativeGlobalRelayProtocol.DirectRecordSize, "record 0x4C");
    // The direct record is header(0x0C) + payload(0x40) = 0x4C.
    Equal(NativeGlobalRelayProtocol.DirectRecordSize,
        LegacyDbServerFrameCodec.HeaderSize
        + NativeGlobalRelayProtocol.DirectSendPayloadLength,
        "0x0C + 0x40 == 0x4C");
    // 0x5A3440 `mov dx,0x274D` (10061), 0x5A3481 `mov dx,0x2750` (10064).
    Equal(10061, NativeGlobalRelayProtocol.RegistrationQueueCommand, "10061");
    Equal(10064, NativeGlobalRelayProtocol.QueryQueueCommand, "10064");
    // 0x5A346D `push 0x41`.
    Equal(0x41, NativeGlobalRelayProtocol.QueryQueuePayloadLength, "0x41");
    // 0x5D1D08 `mov eax,0x1C`.
    Equal(0x1C, NativeGlobalRelayProtocol.QueueNodeSize, "queue node 0x1C");
}

void GlobalRelayRegistrationFields()
{
    // 0x598E06 pushes TServerInfo+0x40A0 (serverType); 0x598E17 reads
    // header+0x10; 0x598E28 reads dword[header+4].
    var frame = Frame(0x0156, 0x20, h =>
    {
        PutShortString(h, 0x10, "RelayName");
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(0x04, 4), 0x11223344);
    });
    True(NativeGlobalRelayProtocol.TryDecodeRegistration(frame, 9, out var req,
        out var err), "decode: " + err);
    Equal(9, req.ServerType, "serverType carried");
    EqualText("RelayName", Text(req.Name), "header+0x10");
    Equal(0x11223344, req.Value, "dword@header+0x04");

    // 0156 has no length gate in the original, so any payload >= header works.
    True(NativeGlobalRelayProtocol.TryDecodeRegistration(
        Frame(0x0156, 0), 0, out _, out _), "no tail accepted");
}

void GlobalRelayQueryFields()
{
    // 0x599237 `call 0x4080B0` = `shr eax,0x10` (high word) and 0x599258
    // `mov cx, word[hdr+4]` (low word) — the same packed-dword idiom as 0x0174.
    // 0x599246 reads header+0x35; 0x59925F reads word[header+2].
    var frame = Frame(0x0173, 0x10, h =>
    {
        PutShortString(h, 0x35, "QueryName");
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x04, 4), 0xBEEF0005);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(0x02, 2), 0x00AA);
    });
    True(NativeGlobalRelayProtocol.TryDecodeQuery(frame, out var req,
        out var err), "decode: " + err);
    EqualText("QueryName", Text(req.Name), "header+0x35");
    Equal(0x0005, req.Selector, "low word selector");
    Equal(0xBEEF, req.Argument, "high word argument");
    Equal(0x00AA, req.Tag, "word@header+0x02");

    // header+0x35 is a 15-byte slot.
    False(NativeGlobalRelayProtocol.TryDecodeQuery(
        Frame(0x0173, 0x10, h => h[0x35] = 0x10), out _, out _),
        "+0x35 over 15 refused");
}

void OpcodeSpaceAccounted()
{
    // The 战神 type1 dispatcher has a body for exactly these opcodes (comparison
    // chain 0x598A30-0x598AC0 plus jump table A @0x598ADA and B @0x598B23; every
    // other table slot points at the default 0x599502).
    int[] nativeBodies =
    {
        0x045, 0x150, 0x151, 0x152, 0x153, 0x154, 0x156, 0x157, 0x159, 0x15A,
        0x15B,
        0x160, 0x161, 0x162, 0x163, 0x164, 0x165, 0x166, 0x167, 0x168,
        0x16A, 0x16B, 0x16C, 0x170, 0x172, 0x173, 0x174, 0x176,
        0x181, 0x182, 0x183, 0x192, 0x193, 0x194, 0x19A, 0x19B, 0x19C, 0x19D,
        0x19E,
    };

    // Opcodes C# now routes explicitly (serverType != 9 path).
    var handled = new HashSet<int>
    {
        NativeSessionControlProtocol.DisconnectAccountCommand,      // 0x045
        NativeDbServerProtocol.SaveHumanCommand,                    // 0x150
        NativeSessionLookupProtocol.RequestCommand,                 // 0x151  NEW
        NativeMasterRelationProtocol.RequestCommand,                // 0x152
        NativeItemExtractionProtocol.RequestCommand,                // 0x153
        NativeItemInjectionProtocol.MailRequestCommand,             // 0x154
        NativeGlobalRelayProtocol.RegistrationCommand,              // 0x156  NEW
        NativeAuxiliaryType1Protocol.RegisterCharacterNameCommand,  // 0x157
        NativeAuxiliaryType1Protocol.DynamicImageRequestCommand,    // 0x159
        NativeItemInjectionProtocol.BagRequestCommand,              // 0x15A
        NativeAwardPlayerProtocol.RequestCommand,                   // 0x15B
        NativeForceLevelProtocol.RequestCommand,                    // 0x168
        NativeCharacterBusyProtocol.Command,                        // 0x16A
        NativeAccountStorageProtocol.LoadCommand,                   // 0x16B
        NativeAccountStorageProtocol.SaveCommand,                   // 0x16C
        NativeZongpaiProtocol.RequestCommand,                       // 0x170  NEW
        NativeHallOfFameProtocol.RequestCommand,                    // 0x172
        NativeGlobalRelayProtocol.QueryCommand,                     // 0x173  NEW
        NativeTransferScoreAccrualProtocol.RequestCommand,          // 0x174  NEW
        NativeTransferScoreProtocol.RequestCommand,                 // 0x176
        NativeDominatorPetProtocol.CreateCommand,                   // 0x181
        NativeDominatorPetProtocol.LoadCommand,                     // 0x182
        NativeDominatorPetProtocol.SaveCommand,                     // 0x183
        NativeGateReportProtocol.Type1RequestCommand,               // 0x192  NEW
        NativeGateReportProtocol.Type2RequestCommand,               // 0x193  NEW
        NativeHeroDbFrameCodec.DetachCommand,                       // 0x194
        NativeCharacterAdminProtocol.RestoreRequestCommand,         // 0x19A
        NativeCharacterAdminProtocol.LookupRequestCommand,          // 0x19B
        NativeOnlineAccountProtocol.SetTextCommand,                 // 0x19C
        NativeOnlineAccountProtocol.SetLoginTimeCommand,            // 0x19D
        NativeSessionControlProtocol.SetPlayStateCommand,           // 0x19E
    };
    // The hero family 0x160-0x167 is covered by a range test in the dispatcher.
    for (var hero = NativeHeroDbFrameCodec.LoadCommand;
         hero <= NativeHeroDbFrameCodec.BuildThreeSlotCommand; hero++)
        handled.Add(hero);

    var missing = new List<string>();
    foreach (var opcode in nativeBodies)
        if (!handled.Contains(opcode))
            missing.Add($"0x{opcode:X3}");
    if (missing.Count != 0)
        throw new Exception("native-body opcodes still unhandled: "
                            + string.Join(", ", missing));
    Equal(39, nativeBodies.Length, "native body count");

    // No handled opcode may ALSO be on the silent list — that would mean the
    // silent test swallows it before the handler runs. serverType 0 stands in for
    // any non-DB-tool GameServer.
    foreach (var opcode in handled)
        False(NativeDbServerProtocol.IsSilentNormalType1Command(
                (ushort)opcode, 0),
            $"0x{opcode:X3} must not be silenced");

    // Conversely the silent list must not claim an opcode the original gives a
    // body to on the type1 path.
    var bodySet = new HashSet<int>(nativeBodies);
    for (var opcode = 0x100; opcode <= 0x1A0; opcode++)
        if (NativeDbServerProtocol.IsSilentNormalType1Command((ushort)opcode, 0)
            && bodySet.Contains(opcode))
            throw new Exception($"0x{opcode:X3} is silenced but has a native body");
}

// ---- helpers ----

LegacyDbServerFrame Frame(ushort command, int tailLength,
    Action<byte[]> fillHeader = null)
{
    var payload = new byte[NativeGateReportProtocol.HeaderSize + tailLength];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    if (fillHeader != null)
    {
        var header = payload.AsSpan(0, NativeGateReportProtocol.HeaderSize)
            .ToArray();
        fillHeader(header);
        header.CopyTo(payload, 0);
    }
    return new LegacyDbServerFrame(1, 0, payload);
}

void PutShortString(byte[] buffer, int offset, string value)
{
    var bytes = System.Text.Encoding.ASCII.GetBytes(value);
    buffer[offset] = (byte)bytes.Length;
    bytes.CopyTo(buffer, offset + 1);
}

string Text(byte[] bytes) => System.Text.Encoding.ASCII.GetString(bytes ?? []);

void Run(string name, Action test)
{
    try { test(); }
    catch (Exception ex) { failures.Add($"FAIL [{name}] {ex.Message}"); }
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
