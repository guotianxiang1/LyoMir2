using System.Buffers.Binary;
using System.Text;
using DBSvr.Core;
using GameSvr;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936);
var failures = new List<string>();

Run("decode exact Type1 0x0050 layout", DecodeExactLoad);
Run("decode fixed body without ScriptData", DecodeLoadWithoutScript);
Run("decode fixed 0xFFFC padded ScriptData slot", DecodePaddedLoad);
Run("encode exact Type1 0x0150 layout", EncodeExactSave);
Run("encode Type1 0x0150 without ScriptData", EncodeSaveWithoutScript);
Run("preserve storage-space word through Type1 load/save", PreserveStorageSpaceWord);
Run("reject malformed native frames", RejectMalformedFrames);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("Native human Type1 codec checks passed.");
return 0;

void DecodeExactLoad()
{
    var fixture = CreateLoadFixture(includeScript: true);
    Check(NativeHumanDbCodec.TryDecodeLoadFrame(fixture.Frame,
        out var load, out var error), error);
    Equal(fixture.Account, load.Account, "load account");
    Equal(fixture.Character, load.CharacterName, "load character");
    Equal(fixture.Account, load.HumanRecord.Header.sAccount, "record header account");
    Equal(fixture.Character, load.HumanRecord.Header.sName, "record header character");
    Equal(fixture.Account, load.HumanRecord.Data.sAccount, "raw account");
    Equal(fixture.Character, load.HumanRecord.Data.sCharName, "raw character");
    Equal((ushort)77, load.HumanRecord.Data.Abil.Level, "raw level");
    Equal(0x12345678, load.HumanRecord.Data.nGold, "raw gold");
    Check(load.HumanInfoPrefix.SequenceEqual(fixture.Prefix), "load prefix bytes");
    Check(load.SessionSuffix.SequenceEqual(fixture.Suffix), "load suffix bytes");
    Check(load.HumanRecord.NativeData.SequenceEqual(fixture.Raw), "load raw bytes");
    Check(load.HumanRecord.NativeScriptData.SequenceEqual(fixture.Script),
        "load ScriptData bytes");
    // The fixture section carries type 0, and type 0 is the S bank, not V. Native
    // dispatches through the 9-entry table at 0x6E4520 and the type-0 arm names
    // +0x804 (0x6E457C `add eax,0x804`, 0x6E459D `mov edx,[eax+0x804]`) while type 1
    // names +0x808 (0x6E462E / 0x6E464F); the registry pins +0x804 to GetS/SetS
    // (0x6DF1CF, 0x6DF26D) and +0x808 to GetV/SetV (0x6DF225, 0x6DF2CF). Asserting the
    // OTHER bank is empty is what makes this a direction check rather than a presence
    // check - a codec that swaps the two passes the presence check either way.
    Equal(0x10203040, load.HumanRecord.Data.ScriptS[7], "ScriptData type-0 S value");
    Equal(0, load.HumanRecord.Data.ScriptV.Count, "type 0 must not land in the V bank");
}

void DecodeLoadWithoutScript()
{
    var fixture = CreateLoadFixture(includeScript: false);
    Equal(NativeHumanDbCodec.ScriptDataOffset, fixture.Frame.Payload.Length,
        "fixed load payload length");
    Check(NativeHumanDbCodec.TryDecodeLoadFrame(fixture.Frame,
        out var load, out var error), error);
    Equal(0, load.HumanRecord.NativeScriptData.Length, "empty load ScriptData");
    Equal(0, load.HumanRecord.Data.ScriptV.Count, "empty load V dictionary");
    Equal(0, load.HumanRecord.Data.ScriptS.Count, "empty load S dictionary");
}

void DecodePaddedLoad()
{
    var fixture = CreateLoadFixture(includeScript: true);
    var payload = new byte[0xFFFC];
    fixture.Frame.Payload.CopyTo(payload, 0);
    Equal(0x0F0C, payload.Length - NativeHumanDbCodec.ScriptDataOffset,
        "padded ScriptData slot size");
    Check(NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(1, 0, payload), out var load, out var error), error);
    Check(load.HumanRecord.NativeScriptData.SequenceEqual(fixture.Script),
        "padded load actual ScriptData bytes");
    Equal(0x10203040, load.HumanRecord.Data.ScriptS[7],
        "padded load ScriptData type-0 S value");
    Equal(0, load.HumanRecord.Data.ScriptV.Count,
        "padded load type 0 must not land in the V bank");
}

void EncodeExactSave()
{
    var fixture = CreateLoadFixture(includeScript: true);
    Check(NativeHumanDbCodec.TryDecodeLoadFrame(fixture.Frame,
        out var load, out var error), error);
    load.HumanRecord.Data.nGold = unchecked((int)0x89ABCDEF);
    var switchExtension = Enumerable.Range(0,
            NativeHumanDbCodec.SwitchExtensionSize)
        .Select(value => unchecked((byte)(value * 29 + 7))).ToArray();
    Check(NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account,
        fixture.Character, 2, 0x11223344, unchecked((int)0x88776655),
        load.HumanRecord, switchExtension, out var save, out error), error);

    Equal((ushort)1, save.Type, "save outer type");
    Equal((ushort)0, save.Reserved, "save outer reserved");
    Equal(NativeHumanDbCodec.ScriptDataOffset + fixture.Script.Length,
        save.Payload.Length, "save dynamic payload length");
    Equal(NativeHumanDbCodec.SaveCommand,
        BinaryPrimitives.ReadUInt16LittleEndian(save.Payload), "save command");
    Equal((ushort)2,
        BinaryPrimitives.ReadUInt16LittleEndian(save.Payload.AsSpan(2, 2)),
        "save mode");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(save.Payload.AsSpan(4, 4)),
        "save header +4");
    Equal(0x11223344,
        BinaryPrimitives.ReadInt32LittleEndian(save.Payload.AsSpan(8, 4)),
        "save param1");
    Equal(unchecked((int)0x88776655),
        BinaryPrimitives.ReadInt32LittleEndian(save.Payload.AsSpan(12, 4)),
        "save param2");
    Equal(fixture.Account, ReadShortString(save.Payload, 0x10), "save account");
    Equal(fixture.Character, ReadShortString(save.Payload, 0x25), "save character");
    Equal((byte)0, save.Payload[0x35], "save message terminator");
    Check(save.Payload.AsSpan(NativeHumanDbCodec.HumanInfoOffset,
        NativeHumanDbCodec.HumanInfoPrefixSize).ToArray().All(value => value == 0),
        "save fixed prefix zeroing");
    Equal(unchecked((int)0x89ABCDEF),
        BinaryPrimitives.ReadInt32LittleEndian(save.Payload.AsSpan(
            NativeHumanDbCodec.NativeDataOffset + 0x44, 4)), "save raw mutation");
    Check(save.Payload.AsSpan(NativeHumanDbCodec.SessionSuffixOffset,
            NativeHumanDbCodec.SessionPrefixSize).ToArray()
        .All(value => value == 0), "save session prefix zeroing");
    Check(save.Payload.AsSpan(
            NativeHumanDbCodec.SessionSuffixOffset
            + NativeHumanDbCodec.SessionPrefixSize,
            NativeHumanDbCodec.SwitchExtensionSize)
        .SequenceEqual(switchExtension), "save mode2 switch extension");
    Check(save.Payload.AsSpan(NativeHumanDbCodec.ScriptDataOffset)
        .SequenceEqual(fixture.Script), "save ScriptData offset");

    Check(LegacyDbServerFrameCodec.TryEncode(save, out var wire, out error), error);
    Equal(LegacyDbServerFrameCodec.HeaderSize + save.Payload.Length, wire.Length,
        "save wire length");
    Equal(LegacyDbServerFrameCodec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(wire), "save wire magic");
    Equal(save.Payload.Length,
        BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
        "save wire payload length");

    var asLoad = (byte[])save.Payload.Clone();
    BinaryPrimitives.WriteUInt16LittleEndian(asLoad, NativeHumanDbCodec.LoadCommand);
    Check(NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(1, 0, asLoad), out var roundTrip, out error), error);
    Equal(unchecked((int)0x89ABCDEF), roundTrip.HumanRecord.Data.nGold,
        "save/load round trip");
    Check(roundTrip.SessionSuffix.AsSpan(
            NativeHumanDbCodec.SessionPrefixSize,
            NativeHumanDbCodec.SwitchExtensionSize)
        .SequenceEqual(switchExtension), "mode2 extension round trip");
}

void EncodeSaveWithoutScript()
{
    var fixture = CreateLoadFixture(includeScript: false);
    Check(NativeHumanDbCodec.TryDecodeLoadFrame(fixture.Frame,
        out var load, out var error), error);
    load.HumanRecord.Data.nGold = 24680;
    Check(NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account,
        fixture.Character, 3, 0, 0, load.HumanRecord, out var save, out error), error);
    Equal(NativeHumanDbCodec.ScriptDataOffset, save.Payload.Length,
        "no-script save payload length");
    Equal(24680, BinaryPrimitives.ReadInt32LittleEndian(save.Payload.AsSpan(
        NativeHumanDbCodec.NativeDataOffset + 0x44, 4)), "no-script save raw mutation");
    Check(save.Payload.AsSpan(NativeHumanDbCodec.SessionSuffixOffset,
            NativeHumanDbCodec.SessionSuffixSize).ToArray()
        .All(value => value == 0), "ordinary save suffix zeroing");
    Equal(0, load.HumanRecord.NativeScriptData.Length,
        "no-script human representation remains empty");
}

void PreserveStorageSpaceWord()
{
    foreach (ushort stored in new ushort[] { 0, 24, 48, 49, 192, 193, 65535 })
    {
        var fixture = CreateLoadFixture(includeScript: false);
        var payload = (byte[])fixture.Frame.Payload.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(
            NativeHumanDbCodec.NativeDataOffset + 0x050E, 2), stored);
        var frame = new LegacyDbServerFrame(1, 0, payload);

        Check(NativeHumanDbCodec.TryDecodeLoadFrame(frame,
            out var load, out var error), error);
        Equal((int)stored, load.HumanRecord.Data.StorageSpaceCount,
            $"load storage-space word {stored}");

        Check(NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account,
            fixture.Character, 1, 0, 0, load.HumanRecord,
            out var save, out error), error);
        Equal(stored, BinaryPrimitives.ReadUInt16LittleEndian(save.Payload.AsSpan(
            NativeHumanDbCodec.NativeDataOffset + 0x050E, 2)),
            $"save storage-space word {stored}");

        var asLoad = (byte[])save.Payload.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(asLoad,
            NativeHumanDbCodec.LoadCommand);
        Check(NativeHumanDbCodec.TryDecodeLoadFrame(
            new LegacyDbServerFrame(1, 0, asLoad),
            out var roundTrip, out error), error);
        Equal((int)stored, roundTrip.HumanRecord.Data.StorageSpaceCount,
            $"round-trip storage-space word {stored}");
    }
}

void RejectMalformedFrames()
{
    var fixture = CreateLoadFixture(includeScript: true);
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(null, out _, out _),
        "null load frame accepted");
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(2, 0, fixture.Frame.Payload), out _, out _),
        "Type2 load frame accepted");

    var wrongCommand = (byte[])fixture.Frame.Payload.Clone();
    BinaryPrimitives.WriteUInt16LittleEndian(wrongCommand, 0x0051);
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(1, 0, wrongCommand), out _, out _),
        "wrong load command accepted");
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(new LegacyDbServerFrame(1, 0,
        fixture.Frame.Payload.AsSpan(0, NativeHumanDbCodec.ScriptDataOffset - 1).ToArray()),
        out _, out _), "truncated load body accepted");

    var badLength = (byte[])fixture.Frame.Payload.Clone();
    badLength[NativeHumanDbCodec.AccountOffset] = 21;
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(1, 0, badLength), out _, out _),
        "oversized SS20 accepted");
    var badGbk = (byte[])fixture.Frame.Payload.Clone();
    badGbk[NativeHumanDbCodec.CharacterOffset] = 1;
    badGbk[NativeHumanDbCodec.CharacterOffset + 1] = 0x81;
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(1, 0, badGbk), out _, out _),
        "invalid GBK accepted");

    var badScriptLength = new byte[0xFFFC];
    fixture.Frame.Payload.CopyTo(badScriptLength, 0);
    BinaryPrimitives.WriteInt32LittleEndian(badScriptLength.AsSpan(
        NativeHumanDbCodec.ScriptDataOffset, 4), -1);
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(1, 0, badScriptLength), out _, out _),
        "negative ScriptData length accepted");
    BinaryPrimitives.WriteInt32LittleEndian(badScriptLength.AsSpan(
        NativeHumanDbCodec.ScriptDataOffset, 4),
        badScriptLength.Length - NativeHumanDbCodec.ScriptDataOffset - 3);
    Check(!NativeHumanDbCodec.TryDecodeLoadFrame(
        new LegacyDbServerFrame(1, 0, badScriptLength), out _, out _),
        "oversized ScriptData length accepted");

    Check(NativeHumanDbCodec.TryDecodeLoadFrame(fixture.Frame,
        out var load, out var error), error);
    Check(!NativeHumanDbCodec.TryEncodeSaveFrame(string.Empty, fixture.Character,
        1, 0, 0, load.HumanRecord, out _, out _), "empty account accepted");
    Check(!NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account,
        "一二三四五六七八", 1, 0, 0, load.HumanRecord, out _, out _),
        "character over SS15 accepted");
    Check(!NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account, fixture.Character,
        1, 0, 0, null, out _, out _), "null save record accepted");
    Check(!NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account, fixture.Character,
        2, 0, 0, load.HumanRecord, out _, out _),
        "mode2 save without switch extension accepted");
    Check(!NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account, fixture.Character,
        2, 0, 0, load.HumanRecord, new byte[0x107], out _, out _),
        "short mode2 switch extension accepted");
    Check(!NativeHumanDbCodec.TryEncodeSaveFrame(fixture.Account, fixture.Character,
        1, 0, 0, load.HumanRecord, new byte[0x108], out _, out _),
        "ordinary save accepted a switch extension");
}

Fixture CreateLoadFixture(bool includeScript)
{
    const string account = "ptidv35blreszj7xl6jz";
    const string character = "流沙";
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    WriteShortString(raw, 0x0000, 15, character);
    WriteShortString(raw, 0x0010, 15, "3");
    WriteShortString(raw, 0x0020, 20, account);
    raw[0x3E] = 1;
    raw[0x3F] = 1;
    raw[0x40] = 2;
    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x3C, 2), 77);
    BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x44, 4), 0x12345678);

    var script = includeScript ? CreateScriptData() : Array.Empty<byte>();
    var payload = new byte[NativeHumanDbCodec.ScriptDataOffset + script.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, NativeHumanDbCodec.LoadCommand);
    WriteShortString(payload, NativeHumanDbCodec.AccountOffset, 20, account);
    WriteShortString(payload, NativeHumanDbCodec.CharacterOffset, 15, character);
    var prefix = Enumerable.Range(0, NativeHumanDbCodec.HumanInfoPrefixSize)
        .Select(value => (byte)(0xA0 + value)).ToArray();
    prefix.CopyTo(payload, NativeHumanDbCodec.HumanInfoOffset);
    raw.CopyTo(payload, NativeHumanDbCodec.NativeDataOffset);
    var suffix = Enumerable.Range(0, NativeHumanDbCodec.SessionSuffixSize)
        .Select(value => (byte)(value * 17 + 3)).ToArray();
    suffix.CopyTo(payload, NativeHumanDbCodec.SessionSuffixOffset);
    script.CopyTo(payload, NativeHumanDbCodec.ScriptDataOffset);
    return new Fixture(account, character, raw, script, prefix, suffix,
        new LegacyDbServerFrame(1, 0xBEEF, payload));
}

byte[] CreateScriptData()
{
    var script = new byte[19];
    BinaryPrimitives.WriteInt32LittleEndian(script, script.Length - 4);
    BinaryPrimitives.WriteUInt32LittleEndian(script.AsSpan(4, 4), 0xABCDEFAA);
    BinaryPrimitives.WriteUInt16LittleEndian(script.AsSpan(8, 2), 8);
    // Section type 0 == the S bank. The native decoder sub_6E448C dispatches
    // through the 9-entry table at 0x6E4520 and the type-0 arm at 0x6E4544 names
    // +0x804 (0x6E457C `add eax,0x804`, 0x6E459D `mov edx,[eax+0x804]`), which the
    // script registry pins to GetS/SetS (0x6DF1CF `mov edx,[ebx+0x804]`, 0x6DF26D
    // `lea edx,[ebx+0x804]`); the type-1 arm at 0x6E45F7 names +0x808 = GetV/SetV.
    // NativeHumanDataCodec.TryParseScript agrees (`type == 0 ? scriptS : scriptV`).
    // This byte was flipped to 1 while the assertions and the comment above them
    // stayed on the S bank, which made all four ScriptS[7] reads throw
    // KeyNotFoundException.
    script[10] = 0;
    BinaryPrimitives.WriteInt32LittleEndian(script.AsSpan(11, 4), 7);
    BinaryPrimitives.WriteInt32LittleEndian(script.AsSpan(15, 4), 0x10203040);
    return script;
}

void WriteShortString(byte[] destination, int offset, int capacity, string value)
{
    var bytes = gbk.GetBytes(value);
    Check(bytes.Length <= capacity, "test fixture short string capacity");
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
}

string ReadShortString(byte[] source, int offset)
{
    var length = source[offset];
    return gbk.GetString(source, offset + 1, length);
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add("FAIL " + name + ": " + ex.Message);
    }
}

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

sealed record Fixture(string Account, string Character, byte[] Raw, byte[] Script,
    byte[] Prefix, byte[] Suffix, LegacyDbServerFrame Frame);
