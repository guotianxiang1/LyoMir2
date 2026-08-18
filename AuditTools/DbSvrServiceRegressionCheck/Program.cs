using DBSvr;
using DBSvr.Core;
using System.Reflection;
using System.Text;
using SystemModule;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var failures = new List<string>();
Run("split marker/header/body and coalesced frames", TestSplitAndCoalesced);
Run("bounded incomplete frame", TestBoundedIncompleteFrame);
Run("filter reload concurrent with lookups", TestConcurrentFilterReload);
Run("native GBK character-name validation", TestNativeNameValidation);
Run("native 5100 control frames", TestNativeGateControlFrames);
Run("native 5100 data frames", TestNativeGateDataFrames);
Run("native 4004 login result body", TestNativeLoginResultBody);
Run("native 5600 authentication frames", TestNativeLoginGateFrames);
Run("native 6000 type1/type2 stream", TestNativeDbServerFrames);
Run("port 6000 connection wire-mode detection", TestDbServerWireModeDetection);
Run("native 6000 heartbeat + selected-human push", TestNativeDbServerProtocol);
Run("native mode2 one-time switch handoff slot", TestNativeSwitchHandoffSlot);
Run("native 6000 DB-tool human/hero reads", TestNativeDbToolReads);
Run("native 6000 DB-tool human/hero writes", TestNativeDbToolWrites);
Run("native 6000 DB-tool lifecycle operations", TestNativeDbToolLifecycle);
Run("native 6000 type2 DBServer decoder/relay", TestNativeType2Protocol);
Run("native 6000 type2 transfer/config management", TestNativeType2Management);
Run("native 6000 type2 admission controls", TestNativeType2Admission);
Run("native 6000 type2 relation log commands", TestNativeRelationLog);
Run("native 6000 type2 stditems import records", TestNativeStdItemsImport);
Run("native 6000 type2 whitelist reload", TestNativeType2WhitelistReload);
Run("native 6000 type2 static snapshot builders", TestNativeType2Initialization);
Run("native type2 ranking builders/coordinator", TestNativeType2Ranking);
Run("native type2 magic definition layout", TestNativeType2MagicDefinitionLayout);
Run("native type2 mandatory magic rows", TestNativeType2MandatoryMagicRows);
Run("native 6000 type3 character query", TestNativeType3Protocol);
Run("native 6000 type1 0x0155 silent command", TestNativeType1SilentCommand);
Run("native 6000 type1 0x0152 master relation reset", TestNativeMasterRelationReset);
Run("native 6000 type1 0x0153 offline item extraction", TestNativeItemExtraction);
Run("native 6000 type1 0x0168 force level", TestNativeForceLevel);
Run("native lvChangeTime conditional update", TestNativeLvChangeTimeUpdate);
Run("native 6000 type1 auxiliary name/image commands", TestNativeAuxiliaryType1);
Run("native 6000 type1 dominator pet commands", TestNativeDominatorPet);
Run("native 6000 type1 account storage commands", TestNativeAccountStorage);
Run("native 6000 type1 hall-of-fame/transfer score", TestNativeHallAndTransfer);
Run("native 6000 type1 award/busy commands", TestNativeAwardAndBusy);
Run("native 6000 type1 item injection/cache", TestNativeItemInjection);
Run("native 6000 type1 character admin commands", TestNativeCharacterAdmin);
Run("native 6000 type1 session control commands", TestNativeSessionControl);
Run("native 6000 type1 online-account commands", TestNativeOnlineAccount);
Run("native hero save sticky side effects", TestNativeHeroSaveStateTracker);
Run("native hero detach attachment state", TestNativeHeroAttachmentStateTracker);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("All DBSvr service regression checks passed.");
return 0;

// Repository-relative source probes must not ride on the process CWD: the sweep harness
// starts every tool in its own bin folder, and TestNativeNameValidation itself chdirs into
// a temp filter directory. AppContext.BaseDirectory is stable under both.
static string RepoRoot()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DBSvr", "DBSvr.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException(
        "repository root not found above " + AppContext.BaseDirectory);
}

static void TestNativeSwitchHandoffSlot()
{
    var slot = new NativeSwitchHandoffSlot();
    slot.SetCurrentCharacter("Role");
    var first = Enumerable.Repeat((byte)0x11,
        NativeDbServerProtocol.LoginExtensionSize).ToArray();
    Check(!slot.TryStore("role", first),
        "switch handoff character gate must be ordinal and case-sensitive");
    Check(!slot.TryStore("Role", new byte[0x107]),
        "switch handoff accepted a short extension");
    Check(slot.TryStore("Role", first), "switch handoff first store");
    first[0] = 0xEE;

    var latest = Enumerable.Repeat((byte)0x22,
        NativeDbServerProtocol.LoginExtensionSize).ToArray();
    Check(slot.TryStore("Role", latest), "switch handoff overwrite");
    latest[1] = 0xEE;
    var consumed = slot.Consume();
    Check(consumed != null && consumed.All(value => value == 0x22),
        "switch handoff must clone and fully overwrite the prior slot");
    Check(slot.Consume() == null,
        "switch handoff must be consumed exactly once");

    var zero = new byte[NativeDbServerProtocol.LoginExtensionSize];
    Check(slot.TryStore("Role", zero),
        "all-zero mode2 extension is still a valid stored block");
    Check(slot.Consume() is { Length: NativeDbServerProtocol.LoginExtensionSize },
        "all-zero switch block was not consumable");

    var suffix = Enumerable.Range(0, NativeDbServerProtocol.HumanInfoSuffixSize)
        .Select(value => unchecked((byte)(value * 13 + 5))).ToArray();
    var mode2 = new NativeSaveHumanRequest
    {
        HeaderWord2 = NativeDbServerProtocol.SwitchSaveMode,
        HumanInfoSuffix = suffix
    };
    Check(NativeDbServerProtocol.TryExtractSwitchLoginExtension(
        mode2, out var extracted), "mode2 extension extraction");
    Check(extracted.SequenceEqual(suffix.AsSpan(
            NativeDbServerProtocol.SessionPrefixSize,
            NativeDbServerProtocol.LoginExtensionSize).ToArray()),
        "mode2 extraction offset/length");
    var ordinary = new NativeSaveHumanRequest
    {
        HeaderWord2 = 1,
        HumanInfoSuffix = suffix
    };
    Check(!NativeDbServerProtocol.TryExtractSwitchLoginExtension(
        ordinary, out _), "ordinary save exposed a switch extension");

    slot.SetCurrentCharacter("Role");
    Check(slot.TryStore("Role", zero), "switch slot before reset");
    slot.Reset();
    Check(slot.Consume() == null && slot.CurrentCharacterName.Length == 0,
        "disconnect reset did not clear switch slot and current character");
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

static void TestNativeLvChangeTimeUpdate()
{
    // Line endings are a git-checkout artifact (core.autocrlf turns the LF in
    // the repo into CRLF on Windows), not part of the contract being asserted,
    // so normalise before matching. Without this the SQL probe below can never
    // hit on a Windows checkout and the audit reports a false red.
    var source = File.ReadAllText(Path.Combine(RepoRoot(),
        "DBSvr", "DB", "impl", "MySqlPlayRecordService.cs"))
        .Replace("\r\n", "\n");
    Check(source.Contains("WHERE idx=@idx AND\n                        (Level<>@oldLevel OR ForceLv<>@oldForceLv OR sfLevel<>@oldSfLevel)",
              StringComparison.Ordinal)
          && source.Contains("cmd.Parameters.AddWithValue(\"@oldLevel\", oldLevel)",
              StringComparison.Ordinal)
          && source.Contains("cmd.Parameters.AddWithValue(\"@oldForceLv\", oldForceLv)",
              StringComparison.Ordinal)
          && source.Contains("cmd.Parameters.AddWithValue(\"@oldSfLevel\", oldSfLevel)",
              StringComparison.Ordinal),
        "native lvChangeTime update must only apply after Level, ForceLv, or sfLevel changed");
}

static void TestNativeAuxiliaryType1()
{
    var registration = new byte[0x48];
    registration[0] = 0x57;
    registration[1] = 0x01;
    registration[0x35] = 3;
    registration[0x36] = (byte)'a';
    registration[0x37] = (byte)'B';
    registration[0x38] = (byte)'c';
    Check(NativeAuxiliaryType1Protocol.TryDecodeCharacterNameRegistration(
        new LegacyDbServerFrame(1, 0, registration), out var occupied,
        out var error), error);
    Check(occupied.SequenceEqual(new byte[] { (byte)'a', (byte)'B', (byte)'c' }),
        "0157 raw name");

    var requestPayload = new byte[0x48];
    requestPayload[0] = 0x59;
    requestPayload[1] = 0x01;
    requestPayload[0x10] = 3;
    Encoding.ASCII.GetBytes("acc").CopyTo(requestPayload, 0x11);
    requestPayload[0x25] = 4;
    Encoding.ASCII.GetBytes("role").CopyTo(requestPayload, 0x26);
    Check(NativeAuxiliaryType1Protocol.TryDecodeDynamicImageRequest(
        new LegacyDbServerFrame(1, 0, requestPayload), out var request,
        out error), error);
    var empty = NativeAuxiliaryType1Protocol.CreateDynamicImageResponse(request);
    Equal((ushort)1, empty.Type, "0159 response type");
    Equal(0x48, empty.Payload.Length, "0159 empty payload length");
    Equal((ushort)0x005F, BitConverter.ToUInt16(empty.Payload, 0),
        "0159 response command");
    Equal(0, BitConverter.ToInt32(empty.Payload, 4), "0159 empty status");
    Equal((byte)3, empty.Payload[0x10], "0159 account length");
    Equal((byte)4, empty.Payload[0x25], "0159 role length");

    var image = NativeAuxiliaryType1Protocol.CreateDynamicImageResponse(request,
        new NativeDynamicImage
        {
            Name = Encoding.ASCII.GetBytes("code"),
            Metadata = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray(),
            Data = new byte[] { 0xAA, 0xBB }
        });
    Equal(0x56, image.Payload.Length, "0159 image payload length");
    Equal(1, BitConverter.ToInt32(image.Payload, 4), "0159 image status");
    Equal((byte)4, image.Payload[0x35], "0159 image name length");
    Check(image.Payload.AsSpan(0x48, 12).SequenceEqual(
        Enumerable.Range(0, 12).Select(i => (byte)i).ToArray()),
        "0159 metadata");
    Check(image.Payload.AsSpan(0x54).SequenceEqual(new byte[] { 0xAA, 0xBB }),
        "0159 image data");
}

static void TestNativeItemInjection()
{
    static void PutShortString(byte[] destination, int offset, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        destination[offset] = (byte)bytes.Length;
        bytes.CopyTo(destination, offset + 1);
    }

    var mailPayload = new byte[NativeItemInjectionProtocol.HeaderSize
                               + 2 * NativeItemInjectionProtocol.ItemSize];
    BitConverter.GetBytes(NativeItemInjectionProtocol.MailRequestCommand)
        .CopyTo(mailPayload, 0);
    PutShortString(mailPayload, 0x10, "gm-account");
    PutShortString(mailPayload, 0x25, "gm-role");
    PutShortString(mailPayload, 0x35, "target");
    BitConverter.GetBytes(unchecked((int)0x89ABCDEF)).CopyTo(mailPayload,
        NativeItemInjectionProtocol.HeaderSize);
    mailPayload[NativeItemInjectionProtocol.HeaderSize + 4] = 0x34;
    mailPayload[NativeItemInjectionProtocol.HeaderSize + 5] = 0x12;
    mailPayload[^1] = 0xA5;
    Check(NativeItemInjectionProtocol.TryDecodeMail(
        new LegacyDbServerFrame(1, 0, mailPayload), out var mail,
        out var error), error);
    Equal(2 * NativeItemInjectionProtocol.ItemSize, mail.Attachment.Length,
        "0154 keeps complete attachment tail");
    var mailResponse = NativeItemInjectionProtocol.CreateMailResponse(mail, 4);
    Equal(NativeItemInjectionProtocol.HeaderSize, mailResponse.Payload.Length,
        "0154 response size");
    Equal(NativeItemInjectionProtocol.MailResponseCommand,
        BitConverter.ToUInt16(mailResponse.Payload, 0),
        "0154 response command");
    Equal((ushort)4, BitConverter.ToUInt16(mailResponse.Payload, 2),
        "0154 response result");
    Equal(unchecked((int)0x89ABCDEF),
        BitConverter.ToInt32(mailResponse.Payload, 4),
        "0154 response MakeIndex");
    Equal((byte)6, mailResponse.Payload[0x35],
        "0154 target echo");

    var shortMail = mailPayload.AsSpan(0,
        NativeItemInjectionProtocol.HeaderSize
        + NativeItemInjectionProtocol.ItemSize - 1).ToArray();
    Check(!NativeItemInjectionProtocol.TryDecodeMail(
            new LegacyDbServerFrame(1, 0, shortMail), out _, out _),
        "0154 short attachment produced a response");

    var bagPayload = new byte[NativeItemInjectionProtocol.HeaderSize];
    BitConverter.GetBytes(NativeItemInjectionProtocol.BagRequestCommand)
        .CopyTo(bagPayload, 0);
    BitConverter.GetBytes(unchecked((int)0xFEDCBA98)).CopyTo(bagPayload, 4);
    PutShortString(bagPayload, 0x10, "account");
    PutShortString(bagPayload, 0x25, "target");
    Check(NativeItemInjectionProtocol.TryDecodeBag(
        new LegacyDbServerFrame(1, 0, bagPayload), out var emptyBag,
        out error), error);
    Check(!emptyBag.OuterLengthValid, "015A empty tail accepted");
    var bagResponse = NativeItemInjectionProtocol.CreateBagResponse(
        emptyBag, 0);
    Equal(NativeItemInjectionProtocol.BagResponseCommand,
        BitConverter.ToUInt16(bagResponse.Payload, 0),
        "015A response command");
    Equal(unchecked((int)0xFEDCBA98),
        BitConverter.ToInt32(bagResponse.Payload, 4),
        "015A correlation echo");

    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    for (var i = 0; i < NativeHumanDataCodec.BagItemCount; i++)
        BitConverter.GetBytes((ushort)1).CopyTo(raw,
            0x2BF6 + i * NativeHumanDataCodec.ItemRecordSize + 4);
    Check(NativeHumanLogicalCache.TryCreatePersistence(
        "account", "target", raw, new byte[] { 1, 2, 3 },
        out var original, out error), error);
    var cache = new NativeHumanLogicalCache();
    NativeSavePersistenceData? queued = null;
    Check(cache.TryStage(7, original, value =>
    {
        queued = value;
        return true;
    }), "logical snapshot stage");
    var staleLoads = 0;
    Check(cache.GetOrLoad(7, () =>
    {
        staleLoads++;
        return null;
    }) != null && staleLoads == 0, "logical cache reloaded stale SQL data");

    var item = Enumerable.Range(0, NativeItemInjectionProtocol.ItemSize)
        .Select(i => (byte)i).ToArray();
    item[4] = 0;
    item[5] = 0;
    var state = cache.TryInjectItem(7, item, includeStorage: true,
        value =>
        {
            queued = value;
            return true;
        });
    Equal(NativeHumanItemInjectionState.Success, state,
        "0154 storage fallback");
    var storageOffset = 8 + 0x52F6;
    Check(queued!.DataBlob.AsSpan(storageOffset,
            NativeItemInjectionProtocol.ItemSize).SequenceEqual(item),
        "0154 did not blindly copy wIndex=0 item bytes");
    Check(NativeHumanLogicalCache.TryExtractRaw(queued,
        out _, out var script), "logical snapshot unwrap");
    Check(script.SequenceEqual(new byte[] { 1, 2, 3 }),
        "item injection changed ScriptData");

    var bagOnly = new NativeHumanLogicalCache();
    Check(bagOnly.TryStage(8, original, _ => true),
        "bag-only snapshot stage");
    Equal(NativeHumanItemInjectionState.NoSpace,
        bagOnly.TryInjectItem(8, item, includeStorage: false, _ => true),
        "015A incorrectly fell back to storage");

    var emptyRaw = new byte[NativeHumanDataCodec.DataRecordSize];
    Check(NativeHumanLogicalCache.TryCreatePersistence(
        "account", "target", emptyRaw, Array.Empty<byte>(),
        out var rejectOriginal, out error), error);
    var rejected = new NativeHumanLogicalCache();
    Check(rejected.TryStage(9, rejectOriginal, _ => true),
        "rejection snapshot stage");
    Equal(NativeHumanItemInjectionState.SaveRejected,
        rejected.TryInjectItem(9, item, includeStorage: false, _ => false),
        "queue rejection result");
    Check(rejected.TryGet(9, out var afterRejected),
        "queue rejection lost snapshot");
    Check(afterRejected.DataBlob.AsSpan(8 + 0x2BF6,
            NativeItemInjectionProtocol.ItemSize).ToArray().All(value => value == 0),
        "queue rejection committed an item");
}

static void TestNativeItemExtraction()
{
    static void PutShortString(byte[] destination, int offset, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        destination[offset] = (byte)bytes.Length;
        bytes.CopyTo(destination, offset + 1);
    }

    const int makeIndex = 0x12345678;
    var payload = new byte[NativeItemExtractionProtocol.HeaderSize];
    BitConverter.GetBytes(NativeItemExtractionProtocol.RequestCommand)
        .CopyTo(payload, 0);
    BitConverter.GetBytes(makeIndex).CopyTo(payload, 4);
    PutShortString(payload, 0x10, "gm-account");
    PutShortString(payload, 0x25, "gm-role");
    PutShortString(payload, 0x35, "offline-role");
    Check(NativeItemExtractionProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, payload), out var request,
        out var error), error);
    Equal(makeIndex, request.MakeIndex, "0153 MakeIndex");
    Check(request.Account.SequenceEqual(
            Encoding.ASCII.GetBytes("gm-account")),
        "0153 account");
    Check(request.RequesterName.SequenceEqual(
            Encoding.ASCII.GetBytes("gm-role")),
        "0153 requester");
    Check(request.TargetName.SequenceEqual(
            Encoding.ASCII.GetBytes("offline-role")),
        "0153 target");

    var failure = NativeItemExtractionProtocol.CreateResponse(request,
        NativeItemExtractionProtocol.ItemNotFound);
    Equal(NativeItemExtractionProtocol.HeaderSize, failure.Payload.Length,
        "0055 failure length");
    Equal(NativeItemExtractionProtocol.ResponseCommand,
        BitConverter.ToUInt16(failure.Payload, 0), "0055 command");
    Equal(NativeItemExtractionProtocol.ItemNotFound,
        BitConverter.ToUInt16(failure.Payload, 2), "0055 failure status");
    Equal(makeIndex, BitConverter.ToInt32(failure.Payload, 4),
        "0055 MakeIndex echo");

    var item = new byte[NativeItemExtractionProtocol.ItemSize];
    BitConverter.GetBytes(makeIndex).CopyTo(item, 0);
    BitConverter.GetBytes((ushort)0x2345).CopyTo(item, 4);
    item[^1] = 0xA5;
    var success = NativeItemExtractionProtocol.CreateResponse(request,
        NativeItemExtractionProtocol.Success, item);
    Equal(NativeItemExtractionProtocol.HeaderSize
          + NativeItemExtractionProtocol.ItemSize, success.Payload.Length,
        "0055 success length");
    Check(success.Payload.AsSpan(NativeItemExtractionProtocol.HeaderSize)
            .SequenceEqual(item), "0055 item tail");

    var type4Payload = new byte[0x4E1];
    type4Payload[0] = 1;
    var type4Item = (byte[])item.Clone();
    type4Item[^1] = 0x4E;
    type4Item.CopyTo(type4Payload, 1);
    var legacyItem = new byte[NativeItemExtractionProtocol.ItemSize];
    BitConverter.GetBytes(makeIndex).CopyTo(legacyItem, 0);
    BitConverter.GetBytes((ushort)0x4567).CopyTo(legacyItem, 4);
    BitConverter.GetBytes((ushort)0x7788).CopyTo(legacyItem, 0x0A);
    legacyItem[0x0C] = 0x91;
    legacyItem[0x10] = 0x92;
    for (var i = 0; i < 0x28; i++) legacyItem[0x11 + i] = (byte)(i + 1);
    legacyItem[0x39] = 0x93;
    legacyItem[0x3B] = 0x94;
    BitConverter.GetBytes(0x10203040).CopyTo(legacyItem, 0x3D);
    BitConverter.GetBytes(0x50607080).CopyTo(legacyItem, 0x45);
    for (var i = 0; i < 0x14; i++) legacyItem[0x49 + i] = (byte)(0xA0 + i);
    BitConverter.GetBytes(0x11223344).CopyTo(legacyItem, 0x5D);
    BitConverter.GetBytes(0x55667788).CopyTo(legacyItem, 0x61);
    BitConverter.GetBytes(unchecked((int)0x99AABBCCu))
        .CopyTo(legacyItem, 0x65);
    legacyItem[0xA1] = 0x95;
    legacyItem[0xA2] = 0x96;
    legacyItem[0xA3] = 0x97;
    legacyItem[0xB8] = 0xFE;
    var legacyPayload = new byte[12 + legacyItem.Length];
    legacyPayload[0] = 1;
    BitConverter.GetBytes(12).CopyTo(legacyPayload, 4);
    BitConverter.GetBytes(legacyItem.Length).CopyTo(legacyPayload, 8);
    legacyItem.CopyTo(legacyPayload, 12);
    var dynamicScript = BuildDynamicData(
        (4, type4Payload), (0x0C, legacyPayload));
    var dynamicHumanRaw = new byte[NativeHumanDataCodec.DataRecordSize];
    dynamicHumanRaw[0x3E] = 1;
    item.CopyTo(dynamicHumanRaw, 0x0F68);
    Check(NativeHumanLogicalCache.TryCreatePersistence(
        "account", "offline-role", dynamicHumanRaw, dynamicScript,
        out var dynamicPersistence, out error), error);
    var dynamicHumanCache = new NativeHumanLogicalCache();
    Check(dynamicHumanCache.TryStage(16, dynamicPersistence, _ => true),
        "0153 dynamic human snapshot stage");
    Equal(NativeHumanItemExtractionState.SaveRejected,
        dynamicHumanCache.TryExtractItem(16, makeIndex, _ => false,
            out var extractedLegacy),
        "0153 legacy-array queue rejection state");
    Equal((ushort)0x7788, BitConverter.ToUInt16(extractedLegacy, 0x14),
        "0153 legacy item +0A mapping");
    Equal((byte)0x91, extractedLegacy[0x1B],
        "0153 legacy item +0C mapping");
    Equal((byte)0x95, extractedLegacy[0x45],
        "0153 legacy item +A1 mapping");
    Equal(0x11223344, BitConverter.ToInt32(extractedLegacy, 0x66),
        "0153 legacy item +5D mapping");
    Equal((byte)0, extractedLegacy[0xB8],
        "0153 legacy item unmapped tail was not zero");
    Check(dynamicHumanCache.TryGet(16, out var afterLegacy),
        "0153 dynamic human cached snapshot");
    var afterLegacyData = Array.Empty<byte>();
    var afterLegacyScript = Array.Empty<byte>();
    Check(afterLegacy != null
          && NativeHumanLogicalCache.TryExtractRaw(afterLegacy,
              out afterLegacyData, out afterLegacyScript),
        "0153 dynamic human snapshot after legacy extraction");
    var legacyItemOffset = 4 + 7 + type4Payload.Length + 7 + 12;
    Check(afterLegacyScript.AsSpan(legacyItemOffset,
            NativeItemExtractionProtocol.ItemSize).ToArray()
        .All(value => value == 0),
        "0153 legacy-array source was not cleared");
    Check(afterLegacyScript.AsSpan(12,
            NativeItemExtractionProtocol.ItemSize).SequenceEqual(type4Item),
        "0153 legacy-array extraction changed later type4 item");
    Check(afterLegacyData.AsSpan(0x0F68,
            NativeItemExtractionProtocol.ItemSize).SequenceEqual(item),
        "0153 legacy-array extraction changed fixed item");
    Equal(NativeHumanItemExtractionState.SaveRejected,
        dynamicHumanCache.TryExtractItem(16, makeIndex, _ => false,
            out var extractedType4),
        "0153 type4 queue rejection state");
    Check(extractedType4.SequenceEqual(type4Item),
        "0153 human type4 item extraction");

    var compressedPersistence = new NativeSavePersistenceData
    {
        Account = dynamicPersistence.Account,
        CharacterName = dynamicPersistence.CharacterName,
        DataBlob = BuildCompressedNativeBlob(dynamicHumanRaw,
            NativeHumanDataCodec.DataSizeMarker,
            NativeHumanDataCodec.DataSizeMarker),
        ScriptDataBlob = BuildCompressedNativeBlob(dynamicScript,
            checked((ushort)dynamicScript.Length)),
        Level = dynamicPersistence.Level,
        Experience = dynamicPersistence.Experience,
        Job = dynamicPersistence.Job,
        Sex = dynamicPersistence.Sex,
        ApprenticeNum = dynamicPersistence.ApprenticeNum,
        HeroCardLevel = dynamicPersistence.HeroCardLevel,
        PlatinaCharacterLevel = dynamicPersistence.PlatinaCharacterLevel,
        SfLevel = dynamicPersistence.SfLevel
    };
    NativeSavePersistenceData queuedCompressed = null!;
    var compressedCache = new NativeHumanLogicalCache();
    Check(compressedCache.TryStage(15, compressedPersistence, value =>
        {
            queuedCompressed = value;
            return true;
        }), "0153 compressed human snapshot stage");
    Check(queuedCompressed.DataBlob.SequenceEqual(
            compressedPersistence.DataBlob)
          && queuedCompressed.ScriptDataBlob.SequenceEqual(
              compressedPersistence.ScriptDataBlob),
        "0153 compressed submission was changed before enqueue");
    Check(compressedCache.TryGet(15, out var normalizedCompressed)
          && BitConverter.ToUInt32(normalizedCompressed.DataBlob, 0) == 0
          && BitConverter.ToUInt32(normalizedCompressed.ScriptDataBlob, 0) == 0,
        "0153 compressed snapshot was not normalized in cache");
    Equal(NativeHumanItemExtractionState.SaveRejected,
        compressedCache.TryExtractItem(15, makeIndex, _ => false, out _),
        "0153 compressed-cache extraction state");

    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    item.CopyTo(raw, 0x0F68);
    var duplicate = (byte[])item.Clone();
    duplicate[^1] = 0x5A;
    duplicate.CopyTo(raw, 0x2BF6);
    var sidecarEquipment = new[]
    {
        new TUserItem
        {
            MakeIndex = makeIndex,
            wIndex = 0x2345,
            ys1 = 7
        }
    };
    Check(YanshenItemSidecarCodec.TryEncode(sidecarEquipment,
        Array.Empty<TUserItem>(), Array.Empty<TUserItem>(),
        out var sidecarPayload, out error), error);
    var sidecarScript = BuildDynamicData(
        (NativeHumanDataCodec.YanshenScriptSectionType, sidecarPayload));
    Check(NativeHumanLogicalCache.TryCreatePersistence(
        "account", "offline-role", raw, sidecarScript,
        out var persistence, out error), error);
    var humanCache = new NativeHumanLogicalCache();
    Check(humanCache.TryStage(17, persistence, _ => true),
        "0153 human snapshot stage");
    Equal(NativeHumanItemExtractionState.SaveRejected,
        humanCache.TryExtractItem(17, makeIndex, _ => false,
            out var extractedHuman),
        "0153 human queue rejection state");
    Check(extractedHuman.SequenceEqual(item),
        "0153 human search did not prefer equipment");
    var afterHumanRaw = Array.Empty<byte>();
    var afterHumanScript = Array.Empty<byte>();
    Check(humanCache.TryGet(17, out var afterHuman)
          && NativeHumanLogicalCache.TryExtractRaw(afterHuman,
              out afterHumanRaw, out afterHumanScript),
        "0153 human snapshot after extraction");
    Check(afterHumanRaw.AsSpan(0x0F68,
            NativeItemExtractionProtocol.ItemSize).ToArray()
        .All(value => value == 0),
        "0153 human item remained after queue rejection");
    Check(afterHumanRaw.AsSpan(0x2BF6,
            NativeItemExtractionProtocol.ItemSize).SequenceEqual(duplicate),
        "0153 human extraction removed a later duplicate");
    Equal((ushort)0, BitConverter.ToUInt16(afterHumanScript, 11 + 6),
        "0153 human sidecar entry was not removed");

    var petService = new ExtractionPetService();
    var petCache = new NativeDominatorPetCache();
    var masterName = Encoding.ASCII.GetBytes("offline-role");
    Equal(1, petCache.Create(petService, 77, masterName),
        "0153 pet creation");
    var petData = NativeDominatorPetProtocol.CreateDefaultData(masterName);
    item.CopyTo(petData, 0x0432);
    Check(petCache.Save(petService, 77, masterName, petData),
        "0153 pet seed save");
    Check(petCache.TryExtractItem(petService, 77, masterName, makeIndex,
            out var extractedPet)
          && extractedPet.SequenceEqual(item),
        "0153 pet item extraction");
    Check(petService.LastSavedData.AsSpan(0x0432,
            NativeItemExtractionProtocol.ItemSize).ToArray()
        .All(value => value == 0),
        "0153 pet item was not cleared");

    var storageAccount = Encoding.ASCII.GetBytes("account");
    var storageData = new byte[4 + NativeAccountStorageProtocol.ItemSize];
    BitConverter.GetBytes((ushort)1).CopyTo(storageData, 2);
    item.CopyTo(storageData, 4);
    var storageService = InterfaceProxy.Create<IStorageService>(
        (method, args) =>
        {
            if (method.Name == nameof(IStorageService.GetNativeStoragePage))
                return (int)args![0]! == 0
                    ? new List<NativeStorageIndexEntry>
                    {
                        new() { Index = 91, Account = storageAccount }
                    }
                    : new List<NativeStorageIndexEntry>();
            if (method.Name == nameof(IStorageService.LoadNativeStorage))
                return new NativeAccountStorageBlobResult
                {
                    Result = 1,
                    Data = (byte[])storageData.Clone()
                };
            return InterfaceProxy.DefaultValue(method.ReturnType);
        });
    var storageCache = new NativeAccountStorageCache();
    storageCache.RegisterAccount(storageAccount);
    storageCache.LoadStorageIndex(storageService);
    Check(storageCache.TryExtractOfflineItem(storageService, storageAccount,
            makeIndex, out var extractedStorage)
          && extractedStorage.SequenceEqual(item),
        "0153 account-storage item extraction");
    var loadedStorage = storageCache.Load(storageService, storageAccount);
    Equal(1, loadedStorage.Result, "0153 account-storage reload result");
    Check(loadedStorage.Data.AsSpan(4,
            NativeAccountStorageProtocol.ItemSize).ToArray()
        .All(value => value == 0),
        "0153 account-storage source was not cleared");
    Check(!storageCache.TryExtractOfflineItem(storageService, storageAccount,
            makeIndex, out _),
        "0153 extracted an online-loaded account storage");

    var heroRecord = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    heroRecord[NativeHeroDbFrameCodec.JobOffset] = 1;
    item.CopyTo(heroRecord, NativeHeroDbFrameCodec.BagItemsOffset);
    var heroSnapshot = new NativeHeroLogicalSnapshot(27, "offline-role",
        "hero", heroRecord, heroRecord, Array.Empty<byte>(), false, 0, 0,
        1, 1, 0, 0, 0, 0, 0);
    var heroCache = new NativeHeroLogicalCache();
    heroCache.Set(heroSnapshot);
    Check(heroCache.TryExtractFixedItem(27, makeIndex,
            out var updatedHero, out var extractedHero)
          && extractedHero.SequenceEqual(item),
        "0153 hero fixed-item extraction");
    Check(updatedHero.Data.AsSpan(NativeHeroDbFrameCodec.BagItemsOffset,
            NativeItemExtractionProtocol.ItemSize).ToArray()
        .All(value => value == 0),
        "0153 hero Data item was not cleared");
    Check(updatedHero.Record.AsSpan(NativeHeroDbFrameCodec.BagItemsOffset,
            NativeItemExtractionProtocol.ItemSize).ToArray()
        .All(value => value == 0),
        "0153 hero selected Record item was not cleared");

    var heroType4 = new byte[0xF6C];
    heroType4[0] = 1;
    var skippedHeroItem = (byte[])item.Clone();
    skippedHeroItem[^1] = 0x31;
    skippedHeroItem.CopyTo(heroType4, 1);
    var secondGroup = 0x524;
    heroType4[secondGroup] = 1;
    var selectedHeroItem = (byte[])item.Clone();
    selectedHeroItem[^1] = 0x32;
    selectedHeroItem.CopyTo(heroType4, secondGroup + 1);
    var heroDynamic = BuildDynamicData(
        (4, heroType4), (0x0C, legacyPayload));
    var dynamicHeroRecord = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    dynamicHeroRecord[NativeHeroDbFrameCodec.JobOffset] = 0;
    item.CopyTo(dynamicHeroRecord, NativeHeroDbFrameCodec.BagItemsOffset);
    var dynamicHero = new NativeHeroLogicalSnapshot(28, "offline-role",
        "hero-dynamic", dynamicHeroRecord, dynamicHeroRecord, heroDynamic,
        false, 0, 0, 0, 1, 0, 0, 0, 0, 0);
    heroCache.Set(dynamicHero);
    Check(heroCache.TryExtractItem(28, 1, makeIndex,
            out var afterHeroType4, out var extractedHeroType4)
          && extractedHeroType4.SequenceEqual(selectedHeroItem),
        "0153 hero type4 selector extraction");
    Check(afterHeroType4.DynamicData.AsSpan(12,
            NativeItemExtractionProtocol.ItemSize).SequenceEqual(
            skippedHeroItem),
        "0153 hero type4 selector did not skip its current group");
    var secondHeroItemOffset = 4 + 7 + secondGroup + 1;
    Check(afterHeroType4.DynamicData.AsSpan(secondHeroItemOffset,
            NativeItemExtractionProtocol.ItemSize).ToArray()
        .All(value => value == 0),
        "0153 hero type4 selected source was not cleared");
    Check(heroCache.TryExtractItem(28, 1, makeIndex,
            out var afterHeroLegacy, out var extractedHeroLegacy)
          && BitConverter.ToUInt16(extractedHeroLegacy, 0x14) == 0x7788,
        "0153 hero legacy-array extraction");
    Check(afterHeroLegacy.Data.AsSpan(NativeHeroDbFrameCodec.BagItemsOffset,
            NativeItemExtractionProtocol.ItemSize).SequenceEqual(item),
        "0153 hero dynamic extraction changed fixed item");
}

static void TestNativeMasterRelationReset()
{
    static void PutShortString(byte[] destination, int offset, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        destination[offset] = (byte)bytes.Length;
        bytes.CopyTo(destination, offset + 1);
    }

    var payload = new byte[NativeMasterRelationProtocol.HeaderSize];
    BitConverter.GetBytes(NativeMasterRelationProtocol.RequestCommand)
        .CopyTo(payload, 0);
    BitConverter.GetBytes(NativeMasterRelationProtocol.ResetSubcommand)
        .CopyTo(payload, 2);
    PutShortString(payload, NativeMasterRelationProtocol.AccountOffset, "gm");
    PutShortString(payload, NativeMasterRelationProtocol.MasterNameOffset,
        "Master");
    PutShortString(payload, NativeMasterRelationProtocol.StudentNameOffset,
        "Student");
    Check(NativeMasterRelationProtocol.TryDecodeReset(
        new LegacyDbServerFrame(1, 0, payload), out var request,
        out var error), error);
    Check(request.Account.SequenceEqual(Encoding.ASCII.GetBytes("gm")),
        "0152 account");
    Check(request.MasterName.SequenceEqual(Encoding.ASCII.GetBytes("Master")),
        "0152 master name");
    Check(request.StudentName.SequenceEqual(Encoding.ASCII.GetBytes("Student")),
        "0152 student name");

    var success = NativeMasterRelationProtocol.CreateResetResponse(true);
    Equal((ushort)1, success.Type, "0152 response type");
    Equal(NativeMasterRelationProtocol.HeaderSize, success.Payload.Length,
        "0152 response length");
    Equal((ushort)0, BitConverter.ToUInt16(success.Payload, 0),
        "0152 response command remains zero");
    Equal((ushort)1, BitConverter.ToUInt16(success.Payload, 2),
        "0152 response success");
    Check(success.Payload.AsSpan(4).ToArray().All(value => value == 0),
        "0152 response tail is not zeroed");
    var failure = NativeMasterRelationProtocol.CreateResetResponse(false);
    Equal((ushort)0, BitConverter.ToUInt16(failure.Payload, 2),
        "0152 response failure");

    var wrongSubtype = (byte[])payload.Clone();
    wrongSubtype[2] = 4;
    Check(!NativeMasterRelationProtocol.TryDecodeReset(
            new LegacyDbServerFrame(1, 0, wrongSubtype), out _, out _),
        "0152 accepted another relationship subtype");
    var clearPayload = (byte[])payload.Clone();
    BitConverter.GetBytes(NativeMasterRelationProtocol.ClearSubcommand)
        .CopyTo(clearPayload, 2);
    Check(NativeMasterRelationProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, clearPayload), out var clearRequest,
        out error)
        && clearRequest.Subcommand
           == NativeMasterRelationProtocol.ClearSubcommand,
        "0152 clear-master subtype");
    var tooLong = (byte[])payload.Clone();
    tooLong[NativeMasterRelationProtocol.MasterNameOffset] = 16;
    Check(!NativeMasterRelationProtocol.TryDecodeReset(
            new LegacyDbServerFrame(1, 0, tooLong), out _, out _),
        "0152 accepted an oversized master name");

    // 2026-08-07: 战神 does NOT parse a master name out of a ShortString at
    // 0x660.  It block-copies the whole social region rec[0x658..0x6D7] <->
    // obj+0xc48 (load 0x6B096C / save 0x6B167E).  0x660 sits INSIDE that block
    // (0x658 + 8), so a byte written there is carried through NativeSocialBlob
    // verbatim — it is simply not surfaced as data.sMasterName.  The codec used
    // to read/write a ShortString at these offsets and that made TryDecode
    // reject 30/30 real records.  The offline 0x152 relation path below still
    // addresses 0x660 directly (NativeHumanLogicalCache); correcting THAT to the
    // real in-block layout is gated on reverse-engineering the ':'/'$' grammar
    // and is tracked separately.
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    raw[0x3E] = 1;
    PutShortString(raw, NativeHumanDataCodec.MasterNameOffset, "OldMaster");
    var socialBefore = raw.AsSpan(NativeHumanDataCodec.NativeSocialBlockOffset,
        NativeHumanDataCodec.NativeSocialBlockLength).ToArray();
    Check(NativeHumanLogicalCache.TryCreatePersistence("account", "Student",
        raw, Array.Empty<byte>(), out var original, out error), error);
    Check(NativeHumanDataCodec.TryDecode(original.DataBlob,
        null, out var decoded, out error), error);
    Check(decoded.Data.NativeSocialBlob != null
          && decoded.Data.NativeSocialBlob.AsSpan().SequenceEqual(socialBefore),
        "native human decoder carried the social block verbatim");
    Check(NativeHumanDataCodec.TryEncode(decoded, out var encoded,
        out var encodedScript, out error), error);
    Check(NativeHumanDataCodec.TryDecode(encoded, encodedScript,
        out var encodedDecoded, out error), error);
    Check(encodedDecoded.Data.NativeSocialBlob != null
          && encodedDecoded.Data.NativeSocialBlob.AsSpan()
              .SequenceEqual(socialBefore),
        "native human encoder preserved the social block verbatim");

    var cache = new NativeHumanLogicalCache();
    NativeSavePersistenceData? queued = null;
    Check(cache.TryStage(7, original, value =>
    {
        queued = value;
        return true;
    }), "0152 logical snapshot stage");
    Equal(NativeHumanMasterRelationState.Success,
        cache.TrySetMasterName(7, Encoding.ASCII.GetBytes("Master"), value =>
        {
            queued = value;
            return true;
        }), "0152 logical master mutation");
    var queuedRaw = Array.Empty<byte>();
    Check(queued != null && NativeHumanLogicalCache.TryExtractRaw(queued,
            out queuedRaw, out _), "0152 queued raw extraction");
    Equal((byte)6, queuedRaw[NativeHumanDataCodec.MasterNameOffset],
        "0152 queued master length");
    Check(queuedRaw.AsSpan(NativeHumanDataCodec.MasterNameOffset + 1, 6)
            .SequenceEqual(Encoding.ASCII.GetBytes("Master")),
        "0152 queued master bytes");
    // 2026-08-07: this used to assert the tail was ZEROED.  That was a C#-derived
    // assumption; the binary says otherwise.  战神 DBServer subcmd 7 (master reset,
    // sub_5A8750 branch 0x5A8BC0) writes the name through the Delphi ShortString
    // assign helper 0x4035D8 with cl=0x0F, and that helper is
    //   `mov bl,[edx]; cmp cl,bl; jbe +2; mov ecx,ebx; mov [eax],cl` then a plain
    //   move of exactly `len` bytes
    // — it writes the length byte and the characters and NOTHING else, so the
    // previous occupant's tail survives.  (M2Server's own copy of the same helper,
    // 0x4039E4, is byte-identical.)  The fixture stages "OldMaster" (9 bytes) and
    // then assigns "Master" (6 bytes), so bytes +7..+9 must still hold the tail of
    // the old name: 't','e','r'.
    var oldTail = Encoding.ASCII.GetBytes("OldMaster").AsSpan(6, 3).ToArray();
    Check(queuedRaw.AsSpan(NativeHumanDataCodec.MasterNameOffset + 7, 3)
            .SequenceEqual(oldTail),
        "0152 master assign must not zero-fill the slot tail (native 0x4035D8)");

    var rejected = new NativeHumanLogicalCache();
    Check(rejected.TryStage(8, original, _ => true),
        "0152 rejected snapshot stage");
    Equal(NativeHumanMasterRelationState.SaveRejected,
        rejected.TrySetMasterName(8, Encoding.ASCII.GetBytes("Master"),
            _ => false), "0152 rejected persistence state");
    var rejectedRaw = Array.Empty<byte>();
    Check(rejected.TryGet(8, out var rejectedPersistence)
        && NativeHumanLogicalCache.TryExtractRaw(rejectedPersistence,
            out rejectedRaw, out _),
        "0152 rejected mutation did not remain in memory");
    Equal((byte)6, rejectedRaw[NativeHumanDataCodec.MasterNameOffset],
        "0152 rejected mutation lost native state");

    var clearRaw = new byte[NativeHumanDataCodec.DataRecordSize];
    PutShortString(clearRaw, NativeHumanDataCodec.MasterNameOffset, "Master");
    clearRaw[0xDC] = 0xA5;
    clearRaw[0xDF] = 0x5A;
    Check(NativeHumanLogicalCache.TryCreatePersistence("account", "Student",
        clearRaw, Array.Empty<byte>(), out var clearPersistence, out error),
        error);
    var clearCache = new NativeHumanLogicalCache();
    Check(clearCache.TryStage(9, clearPersistence, _ => true),
        "0152 clear snapshot stage");
    Equal(NativeHumanMasterRelationState.NoMatch,
        clearCache.TryClearMasterRelation(9, Encoding.ASCII.GetBytes("Other"),
            _ => true), "0152 clear accepted a different master");
    Equal(NativeHumanMasterRelationState.Success,
        clearCache.TryClearMasterRelation(9, Encoding.ASCII.GetBytes("mAsTeR"),
            _ => true), "0152 clear ASCII-insensitive master");
    var clearedRaw = Array.Empty<byte>();
    Check(clearCache.TryGet(9, out var clearedPersistence)
        && NativeHumanLogicalCache.TryExtractRaw(clearedPersistence,
            out clearedRaw, out _), "0152 clear raw extraction");
    Equal((byte)0, clearedRaw[NativeHumanDataCodec.MasterNameOffset],
        "0152 clear did not erase master name");
    Equal((byte)0, clearedRaw[0xDC], "0152 clear did not erase raw+DC");
    Equal((byte)0, clearedRaw[0xDF], "0152 clear did not erase raw+DF");
}

static void TestNativeCharacterAdmin()
{
    static void PutShortString(byte[] destination, int offset, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        destination[offset] = (byte)bytes.Length;
        bytes.CopyTo(destination, offset + 1);
    }

    var restorePayload = new byte[NativeCharacterAdminProtocol.HeaderSize];
    BitConverter.GetBytes(NativeCharacterAdminProtocol.RestoreRequestCommand)
        .CopyTo(restorePayload, 0);
    PutShortString(restorePayload, 0x10, "gm");
    PutShortString(restorePayload, 0x25, "operator");
    PutShortString(restorePayload, 0x35, "deleted");
    Check(NativeCharacterAdminProtocol.TryDecodeRestore(
        new LegacyDbServerFrame(1, 0, restorePayload), out var restore,
        out var error), error);
    var restoreResponse = NativeCharacterAdminProtocol.CreateRestoreResponse(
        restore, true);
    Equal((ushort)0x0138,
        BitConverter.ToUInt16(restoreResponse.Payload, 0),
        "019A response command");
    Equal((ushort)1, BitConverter.ToUInt16(restoreResponse.Payload, 2),
        "019A response result");
    Equal((byte)8, restoreResponse.Payload[0x25],
        "019A operator echo");
    Equal((byte)7, restoreResponse.Payload[0x35],
        "019A target echo");

    var lookupPayload = new byte[NativeCharacterAdminProtocol.HeaderSize
                                 + NativeCharacterAdminProtocol.LookupMinimumTailSize];
    BitConverter.GetBytes(NativeCharacterAdminProtocol.LookupRequestCommand)
        .CopyTo(lookupPayload, 0);
    BitConverter.GetBytes((ushort)4).CopyTo(lookupPayload, 2);
    PutShortString(lookupPayload, 0x25, "ignored");
    var tail = lookupPayload.AsSpan(NativeCharacterAdminProtocol.HeaderSize);
    BitConverter.GetBytes(unchecked((long)0x8877665511223344)).CopyTo(
        tail);
    for (var i = 8; i < tail.Length; i++) tail[i] = (byte)(0xC0 + i);
    Check(NativeCharacterAdminProtocol.TryDecodeLookup(
        new LegacyDbServerFrame(1, 0, lookupPayload), out var lookup,
        out error), error);
    Equal(unchecked((long)0x8877665511223344),
        NativeCharacterAdminProtocol.ReadLookupUserId(lookup),
        "019B little-endian UserId");
    var found = new ChrIndexInfo
    {
        UserId = unchecked((long)0x0102030405060708),
        ChrNameBytes = Encoding.ASCII.GetBytes("hero"),
        PTIDBytes = Encoding.ASCII.GetBytes("account")
    };
    var lookupResponse = NativeCharacterAdminProtocol.CreateLookupResponse(
        lookup, found);
    Equal(0x48 + 0x30, lookupResponse.Payload.Length,
        "019B response size");
    Equal((ushort)0x0139,
        BitConverter.ToUInt16(lookupResponse.Payload, 0),
        "019B response command");
    Equal((ushort)4, BitConverter.ToUInt16(lookupResponse.Payload, 2),
        "019B mode echo");
    Equal(1, BitConverter.ToInt32(lookupResponse.Payload, 4),
        "019B found result");
    var responseTail = lookupResponse.Payload.AsSpan(0x48);
    Equal(unchecked((long)0x0102030405060708),
        BitConverter.ToInt64(responseTail.Slice(0, 8)),
        "019B UserId output");
    Check(responseTail.Slice(0x2D).SequenceEqual(tail.Slice(0x2D)),
        "019B tail sentinel changed");

    var missingResponse = NativeCharacterAdminProtocol.CreateLookupResponse(
        lookup, null);
    Check(missingResponse.Payload.AsSpan(0x48)
            .SequenceEqual(tail), "019B miss did not preserve tail");
    var shortLookup = lookupPayload.AsSpan(0, 0x48 + 0x2F).ToArray();
    Check(!NativeCharacterAdminProtocol.TryDecodeLookup(
            new LegacyDbServerFrame(1, 0, shortLookup), out _, out _),
        "019B short tail was answered");
}

static void TestNativeSessionControl()
{
    var disconnectPayload = new byte[NativeSessionControlProtocol.HeaderSize];
    BitConverter.GetBytes(
        NativeSessionControlProtocol.DisconnectAccountCommand)
        .CopyTo(disconnectPayload, 0);
    var account = Encoding.ASCII.GetBytes("Account");
    disconnectPayload[0x10] = (byte)account.Length;
    account.CopyTo(disconnectPayload, 0x11);
    Check(NativeSessionControlProtocol.TryDecodeDisconnect(
        new LegacyDbServerFrame(1, 0, disconnectPayload),
        out var disconnect, out var error), error);
    Check(disconnect.Account.SequenceEqual(account),
        "0045 account bytes");

    var playPayload = new byte[NativeSessionControlProtocol.HeaderSize];
    BitConverter.GetBytes(NativeSessionControlProtocol.SetPlayStateCommand)
        .CopyTo(playPayload, 0);
    BitConverter.GetBytes((ushort)3).CopyTo(playPayload, 2);
    BitConverter.GetBytes(unchecked((long)0x8877665511223344))
        .CopyTo(playPayload, 8);
    Check(NativeSessionControlProtocol.TryDecodePlayState(
        new LegacyDbServerFrame(1, 0, playPayload), out var state,
        out error), error);
    Equal((ushort)3, state.State, "019E state word");
    Equal(unchecked((long)0x8877665511223344), state.UserId,
        "019E little-endian UserId");
    var response = NativeSessionControlProtocol.CreatePlayStateResponse(state);
    Equal(NativeSessionControlProtocol.HeaderSize, response.Payload.Length,
        "019E response size");
    Equal((ushort)0x013D, BitConverter.ToUInt16(response.Payload, 0),
        "019E response command");
    Equal((ushort)3, BitConverter.ToUInt16(response.Payload, 2),
        "019E state echo");
    Equal(0, BitConverter.ToInt32(response.Payload, 4),
        "019E zero field");
    Equal(state.UserId, BitConverter.ToInt64(response.Payload, 8),
        "019E UserId echo");
}

static void TestNativeOnlineAccount()
{
    var textPayload = new byte[NativeOnlineAccountProtocol.HeaderSize];
    BitConverter.GetBytes(NativeOnlineAccountProtocol.SetTextCommand)
        .CopyTo(textPayload, 0);
    var account = Encoding.ASCII.GetBytes("AccountABC");
    textPayload[0x10] = (byte)account.Length;
    account.CopyTo(textPayload, 0x11);
    var text = LegacyGbkText.Encode("测试值");
    textPayload[0x25] = (byte)text.Length;
    text.CopyTo(textPayload, 0x26);
    Check(NativeOnlineAccountProtocol.TryDecodeText(
        new LegacyDbServerFrame(1, 0, textPayload),
        out var textRequest, out var error), error);
    Check(textRequest.Account.SequenceEqual(account),
        "019C account bytes");
    Equal("测试值", textRequest.Text, "019C text");
    Check(NativeOnlineAccountProtocol.IsAccountMatch(
            textRequest.Account, "accountabc"),
        "019C account ASCII case normalization");

    var maxTextPayload = textPayload.ToArray();
    var maxText = Encoding.ASCII.GetBytes("1234567890abcde");
    maxTextPayload[0x25] = (byte)maxText.Length;
    maxText.CopyTo(maxTextPayload, 0x26);
    Check(NativeOnlineAccountProtocol.TryDecodeText(
        new LegacyDbServerFrame(1, 0, maxTextPayload),
        out var maxTextRequest, out error), error);
    Equal("1234567890abcde", maxTextRequest.Text,
        "019C 15-byte text");

    var invalidText = textPayload.ToArray();
    invalidText[0x25] = 16;
    Check(!NativeOnlineAccountProtocol.TryDecodeText(
            new LegacyDbServerFrame(1, 0, invalidText), out _, out _),
        "019C accepted oversized text ShortString");

    var timePayload = new byte[NativeOnlineAccountProtocol.HeaderSize];
    BitConverter.GetBytes(NativeOnlineAccountProtocol.SetLoginTimeCommand)
        .CopyTo(timePayload, 0);
    BitConverter.GetBytes((ushort)0x0100).CopyTo(timePayload, 2);
    timePayload[0x10] = (byte)account.Length;
    account.CopyTo(timePayload, 0x11);
    Check(NativeOnlineAccountProtocol.TryDecodeLoginTime(
        new LegacyDbServerFrame(1, 0, timePayload),
        out var timeRequest, out error), error);
    Equal((ushort)0x0100, timeRequest.Flag, "019D full flag word");
    var now = new DateTime(2026, 7, 22, 12, 34, 56,
        DateTimeKind.Local);
    Equal(BitConverter.DoubleToInt64Bits(now.ToOADate()),
        NativeOnlineAccountProtocol.CreateLoginDateTimeBits(
            0, () => now),
        "019D zero flag stores Delphi Now bits");
    Equal(0L, NativeOnlineAccountProtocol.CreateLoginDateTimeBits(
            timeRequest.Flag, () => now),
        "019D high-byte flag clears date bits");
    Equal(0L, NativeOnlineAccountProtocol.CreateLoginDateTimeBits(1),
        "019D flag one clears date bits");
    Equal(0L, NativeOnlineAccountProtocol.CreateLoginDateTimeBits(ushort.MaxValue),
        "019D any nonzero flag clears date bits");

    var textCalls = 0;
    var timeCalls = 0;
    var control = new NativeUserAdmissionControl();
    control.Attach(() => Array.Empty<string>(), _ => { }, () => { }, null,
        (currentAccount, currentText) =>
        {
            Check(currentAccount.SequenceEqual(account),
                "019C bridge account");
            Equal("测试值", currentText, "019C bridge text");
            textCalls++;
        },
        (currentAccount, currentFlag) =>
        {
            Check(currentAccount.SequenceEqual(account),
                "019D bridge account");
            Equal((ushort)0x0100, currentFlag,
                "019D bridge flag");
            timeCalls++;
        });
    control.UpdateOnlineAccountText(account, "测试值");
    control.UpdateOnlineAccountLoginTime(account, 0x0100);
    Equal(1, textCalls, "019C bridge call count");
    Equal(1, timeCalls, "019D bridge call count");
}

static void TestNativeDominatorPet()
{
    var name = Encoding.ASCII.GetBytes("master");
    var createPayload = new byte[NativeDominatorPetProtocol.HeaderSize];
    BitConverter.GetBytes(NativeDominatorPetProtocol.CreateCommand)
        .CopyTo(createPayload, 0);
    createPayload[NativeDominatorPetProtocol.MasterNameOffset] =
        (byte)name.Length;
    name.CopyTo(createPayload,
        NativeDominatorPetProtocol.MasterNameOffset + 1);
    Check(NativeDominatorPetProtocol.TryDecodeRequest(
        new LegacyDbServerFrame(1, 0, createPayload),
        NativeDominatorPetProtocol.CreateCommand, out var request,
        out var error), error);
    Check(request.MasterName.SequenceEqual(name), "pet request name");

    var created = NativeDominatorPetProtocol.CreateCreateResponse(name, -2);
    Equal(0x48, created.Payload.Length, "pet create response size");
    Equal((ushort)0x0136, BitConverter.ToUInt16(created.Payload, 0),
        "pet create response command");
    Equal(-2, BitConverter.ToInt32(created.Payload, 4),
        "pet create result");

    var data = NativeDominatorPetProtocol.CreateDefaultData(name);
    Equal(NativeDominatorPetProtocol.DataSize, data.Length,
        "pet default size");
    Equal((ushort)NativeDominatorPetProtocol.DataSize,
        BitConverter.ToUInt16(data, 4), "pet default marker");
    Equal((byte)name.Length,
        data[NativeDominatorPetProtocol.DataMasterNameOffset],
        "pet default name length");
    Equal((byte)0, data[NativeDominatorPetProtocol.DataLevelOffset],
        "pet default level");

    data[NativeDominatorPetProtocol.DataLevelOffset] = 7;
    BitConverter.GetBytes(0x89ABCDEFu).CopyTo(data,
        NativeDominatorPetProtocol.DataExperienceOffset);
    Check(NativeDominatorPetBlobCodec.TryEncode(data, out var blob,
        out error), error);
    Check(NativeDominatorPetBlobCodec.TryDecode(blob, out var decoded,
        out error), error);
    Check(decoded.SequenceEqual(data), "pet native Blob round trip");

    var loaded = NativeDominatorPetProtocol.CreateLoadResponse(name, 1, data);
    Equal(NativeDominatorPetProtocol.SavePayloadSize, loaded.Payload.Length,
        "pet load success payload size");
    Equal((ushort)0x0137, BitConverter.ToUInt16(loaded.Payload, 0),
        "pet load response command");
    Equal((ushort)1, BitConverter.ToUInt16(loaded.Payload, 2),
        "pet load response status");
    Check(loaded.Payload.AsSpan(0x48).SequenceEqual(data),
        "pet load response data");

    var failed = NativeDominatorPetProtocol.CreateLoadResponse(name, -3);
    Equal(0x48, failed.Payload.Length, "pet load failure payload size");
    Equal(unchecked((ushort)-3), BitConverter.ToUInt16(failed.Payload, 2),
        "pet load failure status");
}

static void TestNativeAccountStorage()
{
    var body = new byte[4 + 2 * NativeAccountStorageProtocol.ItemSize];
    BitConverter.GetBytes((ushort)2).CopyTo(body, 2);
    body[4] = 0xAA;
    var payload = new byte[NativeAccountStorageProtocol.HeaderSize + body.Length];
    BitConverter.GetBytes(NativeAccountStorageProtocol.SaveCommand)
        .CopyTo(payload, 0);
    payload[0x10] = 3;
    Encoding.ASCII.GetBytes("acc").CopyTo(payload, 0x11);
    payload[0x25] = 4;
    Encoding.ASCII.GetBytes("role").CopyTo(payload, 0x26);
    body.CopyTo(payload, NativeAccountStorageProtocol.HeaderSize);
    Check(NativeAccountStorageProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, payload),
        NativeAccountStorageProtocol.SaveCommand, out var request,
        out var error), error);
    Check(request.Data.SequenceEqual(body), "016C body");
    var saveResponse = NativeAccountStorageProtocol.CreateSaveResponse(request);
    Equal((ushort)0x0063, BitConverter.ToUInt16(saveResponse.Payload, 0),
        "016C response command");
    Equal((ushort)1, BitConverter.ToUInt16(saveResponse.Payload, 2),
        "016C response status");

    var invalid = payload[..^1];
    Check(!NativeAccountStorageProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, invalid),
        NativeAccountStorageProtocol.SaveCommand, out _, out error)
          && error.Contains("count", StringComparison.Ordinal),
        "016C invalid item-count length accepted");

    var blob = NativeAccountStorageBlobCodec.EncodeUncompressed(body);
    var decoded = NativeAccountStorageBlobCodec.Decode(blob);
    Equal(1, decoded.Result, "storage uncompressed Blob result");
    Check(decoded.Data.SequenceEqual(body),
        "storage uncompressed Blob round trip");
    var badCrc = new byte[12];
    BitConverter.GetBytes(4u).CopyTo(badCrc, 0);
    BitConverter.GetBytes((ushort)4).CopyTo(badCrc, 4);
    BitConverter.GetBytes((ushort)4).CopyTo(badCrc, 6);
    var crcResult = NativeAccountStorageBlobCodec.Decode(badCrc);
    Equal(-5, crcResult.Result, "storage CRC failure result");

    var loadHeader = request.Header.ToArray();
    BitConverter.GetBytes(NativeAccountStorageProtocol.LoadCommand)
        .CopyTo(loadHeader, 0);
    Check(NativeAccountStorageProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, loadHeader),
        NativeAccountStorageProtocol.LoadCommand, out var load,
        out error), error);
    var loaded = NativeAccountStorageProtocol.CreateLoadResponse(load, 1, body);
    Equal((ushort)0x0062, BitConverter.ToUInt16(loaded.Payload, 0),
        "016B response command");
    Equal((ushort)1, BitConverter.ToUInt16(loaded.Payload, 2),
        "016B response result");
    Check(loaded.Payload.AsSpan(0x48).SequenceEqual(body),
        "016B response body");

    var saveCalls = 0;
    var storageService = InterfaceProxy.Create<IStorageService>(
        (method, args) =>
        {
            if (method.Name == nameof(IStorageService.GetNativeStoragePage))
                return (int)args![0]! == 0
                    ? new List<NativeStorageIndexEntry>
                    {
                        new()
                        {
                            Index = 17,
                            Account = Encoding.ASCII.GetBytes("account")
                        },
                        new()
                        {
                            Index = 18,
                            Account = Encoding.ASCII.GetBytes("orphan")
                        }
                    }
                    : new List<NativeStorageIndexEntry>();
            if (method.Name == nameof(IStorageService.LoadNativeStorage))
                return new NativeAccountStorageBlobResult
                {
                    Result = 1,
                    Data = body.ToArray()
                };
            if (method.Name == nameof(IStorageService.EnsureNativeStorage))
                return 17;
            if (method.Name == nameof(IStorageService.SaveNativeStorage))
            {
                Interlocked.Increment(ref saveCalls);
                return true;
            }
            return InterfaceProxy.DefaultValue(method.ReturnType);
        });
    var cache = new NativeAccountStorageCache();
    cache.RegisterAccount(Encoding.ASCII.GetBytes("AcCoUnT"));
    cache.LoadStorageIndex(storageService);
    var first = cache.Load(storageService,
        Encoding.ASCII.GetBytes("ACCOUNT"));
    Equal(1, first.Result, "016B first cache load");
    Check(first.Data.SequenceEqual(body), "016B cached data");
    Equal(-1, cache.Load(storageService,
        Encoding.ASCII.GetBytes("account")).Result,
        "016B repeated cache load");
    Equal(0, cache.Load(storageService,
        Encoding.ASCII.GetBytes("orphan")).Result,
        "016B orphan storage row entered account cache");

    cache.StartSaveWorker(storageService);
    Check(cache.StageSave(Encoding.ASCII.GetBytes("ACCOUNT"), body),
        "016C cache stage");
    cache.StopSaveWorker();
    Equal(1, saveCalls, "016C save worker call count");

    var large = Enumerable.Repeat((byte)0x5A, 0x1000).ToArray();
    var compressedBlob = NativeAccountStorageBlobCodec.Encode(large);
    Check(BitConverter.ToUInt16(compressedBlob, 6) != 0,
        "storage compressible Blob was not compressed");
    var compressedRoundTrip = NativeAccountStorageBlobCodec.Decode(
        compressedBlob);
    Equal(1, compressedRoundTrip.Result,
        "storage compressed Blob result");
    Check(compressedRoundTrip.Data.SequenceEqual(large),
        "storage compressed Blob round trip");
    Array.Clear(compressedBlob, 0, 4);
    Equal(1, NativeAccountStorageBlobCodec.Decode(compressedBlob).Result,
        "storage zero CRC did not bypass compressed checksum");
}

static void TestNativeHallAndTransfer()
{
    var transferPayload = new byte[NativeTransferScoreProtocol.HeaderSize];
    BitConverter.GetBytes(NativeTransferScoreProtocol.RequestCommand)
        .CopyTo(transferPayload, 0);
    BitConverter.GetBytes(((uint)123 << 16) | 2u)
        .CopyTo(transferPayload, 4);
    transferPayload[0x35] = 4;
    Encoding.ASCII.GetBytes("role").CopyTo(transferPayload, 0x36);
    Check(NativeTransferScoreProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, transferPayload),
        out var transfer, out var error), error);
    Equal((ushort)2, transfer.ScoreType, "0176 score type");
    Equal((ushort)123, transfer.Amount, "0176 amount");
    var transferResponse = NativeTransferScoreProtocol.CreateResponse(
        transfer, true);
    Equal((ushort)0x012F,
        BitConverter.ToUInt16(transferResponse.Payload, 0),
        "0176 response command");
    Equal((ushort)2, BitConverter.ToUInt16(transferResponse.Payload, 2),
        "0176 response score type");
    Equal(123, BitConverter.ToInt32(transferResponse.Payload, 4),
        "0176 response amount");
    Equal((byte)4, transferResponse.Payload[0x25],
        "0176 response name length");

    var hallBody = Enumerable.Range(0,
            NativeHallOfFameProtocol.RecordBodySize)
        .Select(i => (byte)(i % 251)).ToArray();
    byte[] compressed;
    using (var output = new MemoryStream())
    {
        using (var zlib = new System.IO.Compression.ZLibStream(output,
                   System.IO.Compression.CompressionLevel.SmallestSize, true))
            zlib.Write(hallBody, 0, hallBody.Length);
        compressed = output.ToArray();
    }
    var hallBlob = new byte[8 + compressed.Length];
    BitConverter.GetBytes(
            NativeAccountStorageBlobCodec.ComputeNativeCrc(compressed))
        .CopyTo(hallBlob, 0);
    BitConverter.GetBytes((ushort)compressed.Length).CopyTo(hallBlob, 6);
    compressed.CopyTo(hallBlob, 8);
    Check(NativeHallOfFameBlobCodec.TryDecode(hallBlob,
        out var hallRecord, out error), error);
    Equal(NativeHallOfFameProtocol.RecordSize, hallRecord.Length,
        "0172 record size");
    Equal((ushort)NativeHallOfFameProtocol.RecordSize,
        BitConverter.ToUInt16(hallRecord, 4), "0172 normalized marker");
    Check(hallRecord.AsSpan(8).SequenceEqual(hallBody),
        "0172 record body");

    var hallRequestPayload = new byte[NativeHallOfFameProtocol.HeaderSize];
    BitConverter.GetBytes(NativeHallOfFameProtocol.RequestCommand)
        .CopyTo(hallRequestPayload, 0);
    BitConverter.GetBytes((ushort)7).CopyTo(hallRequestPayload, 2);
    Check(NativeHallOfFameProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, hallRequestPayload),
        out var rank, out error), error);
    var hallResponse = NativeHallOfFameProtocol.CreateResponse(rank, hallRecord);
    Equal(NativeHallOfFameProtocol.ResponsePayloadSize,
        hallResponse.Payload.Length, "0172 response payload size");
    Equal((ushort)0x012E, BitConverter.ToUInt16(hallResponse.Payload, 0),
        "0172 response command");
    Equal((ushort)7, BitConverter.ToUInt16(hallResponse.Payload, 2),
        "0172 response rank");
    Check(hallResponse.Payload.AsSpan(0x48,
            NativeHallOfFameProtocol.RecordSize).SequenceEqual(hallRecord),
        "0172 response record");
}

static void TestNativeAwardAndBusy()
{
    var payload = new byte[NativeAwardPlayerProtocol.HeaderSize
                           + NativeAwardPlayerProtocol.BodySize];
    BitConverter.GetBytes(NativeAwardPlayerProtocol.RequestCommand)
        .CopyTo(payload, 0);
    BitConverter.GetBytes(unchecked((int)0x89ABCDEF)).CopyTo(payload, 4);
    payload[0x10] = 3;
    Encoding.ASCII.GetBytes("acc").CopyTo(payload, 0x11);
    payload[0x25] = 4;
    Encoding.ASCII.GetBytes("role").CopyTo(payload, 0x26);
    var body = NativeAwardPlayerProtocol.HeaderSize;
    payload[body] = 5;
    Encoding.ASCII.GetBytes("award").CopyTo(payload, body + 1);
    payload[body + 21] = 60;
    payload[body + 22] = 2;
    payload[body + 23] = 1;
    Check(NativeAwardPlayerProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, payload), out var award,
        out var error), error);
    Equal((byte)60, award.Level, "015B award level");
    Equal((byte)2, award.Job, "015B award job");
    Check(award.AwardPtid.SequenceEqual(Encoding.ASCII.GetBytes("award")),
        "015B award PTID");
    var response = NativeAwardPlayerProtocol.CreateResponse(award, true);
    Equal((ushort)0x0061, BitConverter.ToUInt16(response.Payload, 0),
        "015B response command");
    Equal((ushort)1, BitConverter.ToUInt16(response.Payload, 2),
        "015B response result");
    Equal(unchecked((int)0x89ABCDEF),
        BitConverter.ToInt32(response.Payload, 4),
        "015B response correlation");

    var busyPayload = new byte[NativeCharacterBusyProtocol.HeaderSize];
    BitConverter.GetBytes(NativeCharacterBusyProtocol.Command)
        .CopyTo(busyPayload, 0);
    busyPayload[0x25] = 4;
    Encoding.ASCII.GetBytes("role").CopyTo(busyPayload, 0x26);
    Check(NativeCharacterBusyProtocol.TryDecode(
        new LegacyDbServerFrame(1, 0, busyPayload), out var busyName,
        out error), error);
    Check(busyName.SequenceEqual(Encoding.ASCII.GetBytes("role")),
        "016A busy name");
}

static void TestSplitAndCoalesced()
{
    var first = new Frame44FF44(0x13, 0, 7, new byte[] { 1, 2, 3, 4 }).ToBytes();
    var second = Frame44FF44.Ping(8).ToBytes();
    var wire = new byte[] { 0x10, 0x20, 0x30 }
        .Concat(first)
        .Concat(second)
        .ToArray();

    var parser = new MobileFrameStreamParser();
    var frames = new List<Frame44FF44>();
    var chunks = new[] { 2, 3, 5, 1, wire.Length - 11 };
    var offset = 0;
    foreach (var count in chunks)
    {
        Check(parser.TryAppend(wire, offset, count, out var parsed, out var error), error);
        frames.AddRange(parsed);
        offset += count;
    }

    Equal(2, frames.Count, "frame count");
    Equal((byte)0x13, frames[0].Cmd, "first command");
    Equal((uint)7, frames[0].Seq, "first sequence");
    Check(frames[0].Payload.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "first payload");
    Equal(Frame44FF44.MARKER_PING, frames[1].Marker, "ping marker");
    Equal(0, parser.BufferedLength, "parser drained");
}

static void TestBoundedIncompleteFrame()
{
    var incomplete = new Frame44FF44(0x17, 0, 9, new byte[100]).ToBytes();
    var parser = new MobileFrameStreamParser(64);
    Check(parser.TryAppend(incomplete, 0, Frame44FF44.HEADER_SIZE,
        out var headerFrames, out var error), error);
    Equal(0, headerFrames.Count, "incomplete header frame count");
    Check(!parser.TryAppend(incomplete, Frame44FF44.HEADER_SIZE, 53,
        out _, out error), "oversized buffered partial must fail");
    Check(error.Contains("exceeds", StringComparison.Ordinal), "overflow error");
    Equal(0, parser.BufferedLength, "overflow resets parser");
}

static void TestConcurrentFilterReload()
{
    var directory = Path.Combine(Path.GetTempPath(),
        "dbsvr-sensitive-concurrent-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var gbk = Encoding.GetEncoding(936);
    var output = Console.Out;
    try
    {
        Console.SetOut(TextWriter.Null);
        var abusePath = Path.Combine(directory, "!AbUse.txt");
        var namePath = Path.Combine(directory, "!AbUseName.txt");
        File.WriteAllText(abusePath, "LEFT\r\n", gbk);
        File.WriteAllText(namePath, "RIGHT\r\n", gbk);
        var initialTime = DateTime.UtcNow.AddMinutes(-20);
        File.SetLastWriteTimeUtc(abusePath, initialTime);
        File.SetLastWriteTimeUtc(namePath, initialTime);
        var sensitive = new SensitiveWordFilter(directory);
        sensitive.Load();
        Check(!sensitive.ValidateNativeHeroName("QLEFTQ")
              && !sensitive.ValidateNativeHeroName("QRIGHTQ"),
            "initial native filter swap patterns");

        File.WriteAllText(abusePath, "RIGHT\r\n", gbk);
        var nameLines = string.Join("\r\n", Enumerable.Range(0, 10000)
            .Select(i => "Z" + i.ToString("X4"))) + "\r\nLEFT\r\n";
        File.WriteAllText(namePath, nameLines, gbk);
        File.SetLastWriteTimeUtc(abusePath, initialTime.AddMinutes(2));
        File.SetLastWriteTimeUtc(namePath, initialTime.AddMinutes(2));
        var reloadTick = typeof(SensitiveWordFilter).GetField("_nativeReloadTick",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic);
        Check(reloadTick != null, "native concurrent reload tick field missing");
        reloadTick!.SetValue(sensitive,
            unchecked((uint)Environment.TickCount - 30000u));

        using var start = new ManualResetEventSlim(false);
        var allowedLeft = 0;
        var readCount = 0;
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 10000; i++)
            {
                if (sensitive.ValidateNativeHeroName("QLEFTQ"))
                    Interlocked.Increment(ref allowedLeft);
                Interlocked.Increment(ref readCount);
            }
        })).ToArray();
        var writer = Task.Run(() =>
        {
            start.Wait();
            sensitive.Reload();
        });
        start.Set();
        Task.WaitAll(readers.Append(writer).ToArray());
        Check(readCount == 40000 && allowedLeft == 0,
            "native filter lists exposed a half-published concurrent snapshot");
        Check(!sensitive.ValidateNativeHeroName("QLEFTQ")
              && !sensitive.ValidateNativeHeroName("QRIGHTQ"),
            "native filter swap was not fully published");
        Check(sensitive.ContainsAbuseWord("QLEFTQ")
              && !sensitive.ContainsAbuseWord("QRIGHTQ")
              && sensitive.IsDenyName("RIGHT"),
            "native reload changed ordinary sensitive-name filters");
    }
    finally
    {
        Console.SetOut(output);
        Directory.Delete(directory, true);
    }
}

static void TestNativeNameValidation()
{
    Check(NameValidator.ValidateChrName("Abc123", false), "ASCII name rejected");
    Check(NameValidator.ValidateChrName("测试角色", false), "GBK name rejected");
    Check(NameValidator.ValidateChrName("一二三四五六七", false), "14-byte GBK name rejected");
    Check(!NameValidator.ValidateChrName("一二三四五六七八", false),
        "16-byte GBK name accepted");
    Check(!NameValidator.ValidateChrName("测试角色", true),
        "Chinese name accepted in English-only mode");
    Check(!NameValidator.ValidateChrName("bad_name", false), "punctuation accepted");
    Check(!NameValidator.ValidateChrName("角色😀", false), "non-GBK name accepted");

    var nativeHero = new SensitiveWordFilter();
    Check(nativeHero.ValidateNativeHeroName("A_BC"),
        "native hero name rejected original-allowed underscore");
    Check(nativeHero.ValidateNativeHeroName("ABC!"),
        "native hero name rejected original-allowed exclamation");
    Check(nativeHero.ValidateNativeHeroName("ABC\u007F"),
        "native hero name rejected original-allowed DEL byte");
    Check(!nativeHero.ValidateNativeHeroName("ABC"),
        "native hero name accepted 3 bytes");
    Check(!nativeHero.ValidateNativeHeroName(new string('A', 15)),
        "native hero name accepted 15 bytes");
    Check(!nativeHero.ValidateNativeHeroName("AB-C"),
        "native hero name accepted forbidden hyphen");
    Check(!nativeHero.ValidateNativeHeroName("AB#C"),
        "native hero name accepted forbidden hash");
    Check(!nativeHero.ValidateNativeHeroName("GM0A")
          && !nativeHero.ValidateNativeHeroName("gdoa"),
        "native hero name accepted GM/GD reserved prefix");

    var gbk = Encoding.GetEncoding(936,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    string GbkName(string hex) => gbk.GetString(Convert.FromHexString(hex));
    Check(nativeHero.ValidateNativeHeroName(GbkName("A1A24142")),
        "native hero name rejected A1:A2 lower boundary");
    Check(!nativeHero.ValidateNativeHeroName(GbkName("A1A14142")),
        "native hero name accepted A1:A1 excluded pair");
    Check(!nativeHero.ValidateNativeHeroName(GbkName("81404142")),
        "native hero name accepted GBK extension lead 81");
    Check(!nativeHero.ValidateNativeHeroName(GbkName("F8404142")),
        "native hero name accepted GBK extension lead F8");

    var originalDirectory = Directory.GetCurrentDirectory();
    var filterDirectory = Path.Combine(Path.GetTempPath(),
        "dbsvr-native-hero-filter-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(filterDirectory);
    var output = Console.Out;
    try
    {
        File.WriteAllText(Path.Combine(filterDirectory, "!AbUse.txt"),
            "AbC\r\n", gbk);
        File.WriteAllText(Path.Combine(filterDirectory, "!AbUseName.txt"),
            "XyZ\r\n", gbk);
        File.WriteAllText(Path.Combine(filterDirectory, "DenyChrName.txt"),
            "ONLYDENY\r\n", gbk);
        Directory.SetCurrentDirectory(filterDirectory);
        Console.SetOut(TextWriter.Null);
        var fileBackedNativeHero = new SensitiveWordFilter(filterDirectory);
        fileBackedNativeHero.Load();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("QABCR"),
            "native hero !AbUse substring was not applied");
        Check(!fileBackedNativeHero.ValidateNativeHeroName("WXYZQ"),
            "native hero !AbUseName substring was not applied");
        Check(fileBackedNativeHero.ValidateNativeHeroName("ONLYDENY"),
            "native hero validation incorrectly used DenyChrName.txt");

        var abusePath = Path.Combine(filterDirectory, "!AbUse.txt");
        var abuseNamePath = Path.Combine(filterDirectory, "!AbUseName.txt");
        var stableTimestamp = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(abusePath, stableTimestamp);
        fileBackedNativeHero.Load();
        File.WriteAllText(abusePath, "DeF\r\n", gbk);
        File.SetLastWriteTimeUtc(abusePath, stableTimestamp);
        fileBackedNativeHero.Load();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("QABCR"),
            "native hero patterns reloaded despite unchanged timestamp");
        Check(fileBackedNativeHero.ValidateNativeHeroName("QDEFQ"),
            "unchanged timestamp replaced native hero patterns");

        File.WriteAllText(abusePath, "GhI\r\n", gbk);
        File.SetLastWriteTimeUtc(abusePath, stableTimestamp.AddMinutes(1));
        using (File.Open(abusePath, FileMode.Open, FileAccess.Read,
                   FileShare.None))
            fileBackedNativeHero.Load();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("QABCR"),
            "failed native hero reload discarded prior patterns");
        File.Delete(abusePath);
        fileBackedNativeHero.Load();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("QABCR"),
            "missing native hero pattern file discarded prior patterns");

        File.WriteAllText(abusePath, ";only-comment\r\n", gbk);
        File.SetLastWriteTimeUtc(abusePath, stableTimestamp.AddMinutes(2));
        fileBackedNativeHero.Load();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("QABCR"),
            "empty native hero reload discarded prior patterns");
        File.WriteAllBytes(abusePath,
            Convert.FromHexString("7A7A7A7A007461696C0D0A4241440D0A"));
        File.SetLastWriteTimeUtc(abusePath, stableTimestamp.AddMinutes(2));
        fileBackedNativeHero.Load();
        Check(fileBackedNativeHero.ValidateNativeHeroName("AZZZZA"),
            "empty native hero reload did not advance timestamp");
        File.SetLastWriteTimeUtc(abusePath, stableTimestamp.AddMinutes(4));
        fileBackedNativeHero.Load();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("AZZZZA"),
            "native hero raw ANSI NUL truncation was not applied");
        Check(fileBackedNativeHero.ValidateNativeHeroName("QBADQ"),
            "native hero parser continued after the text-terminating NUL");

        File.WriteAllText(abusePath, "NeW\r\n", gbk);
        File.SetLastWriteTimeUtc(abusePath, stableTimestamp.AddMinutes(6));
        var reloadTick = typeof(SensitiveWordFilter).GetField("_nativeReloadTick",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic);
        Check(reloadTick != null, "native reload tick field missing");
        reloadTick!.SetValue(fileBackedNativeHero,
            unchecked((uint)Environment.TickCount - 30000u));
        fileBackedNativeHero.Reload();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("QNEWQ"),
            "due native hero reload did not publish changed patterns");
        Check(!fileBackedNativeHero.ContainsAbuseWord("QNEWQ")
              && fileBackedNativeHero.ContainsAbuseWord("QBADQ"),
            "automatic native reload unexpectedly rebuilt ordinary filters");

        File.WriteAllText(abuseNamePath, "PQR\r\n", gbk);
        File.SetLastWriteTimeUtc(abuseNamePath,
            stableTimestamp.AddMinutes(8));
        reloadTick.SetValue(fileBackedNativeHero,
            unchecked((uint)Environment.TickCount - 30000u));
        fileBackedNativeHero.Reload();
        Check(!fileBackedNativeHero.ValidateNativeHeroName("APQRB")
              && fileBackedNativeHero.ValidateNativeHeroName("WXYZQ"),
            "due native !AbUseName reload did not replace its patterns");
        Check(fileBackedNativeHero.IsDenyName("XyZ")
              && !fileBackedNativeHero.IsDenyName("PQR"),
            "native !AbUseName reload rebuilt the ordinary deny-name set");
        Check(!SensitiveWordFilter.IsNativeReloadDue(29999, 0)
              && SensitiveWordFilter.IsNativeReloadDue(30000, 0)
              && SensitiveWordFilter.IsNativeReloadDue(20000,
                  unchecked(uint.MaxValue - 15000)),
            "native hero 30-second reload tick gate");

        var mainFormSource = File.ReadAllText(Path.Combine(
            RepoRoot(), "DBSvr", "Forms", "MainForm.cs"));
        var timedLoopStart = mainFormSource.IndexOf(
            "private async Task RunTimedLoop", StringComparison.Ordinal);
        var timedLoopEnd = mainFormSource.IndexOf(
            "private async Task StopServices", timedLoopStart,
            StringComparison.Ordinal);
        Check(timedLoopStart >= 0 && timedLoopEnd > timedLoopStart,
            "production timed-loop boundaries are missing");
        var timedLoop = mainFormSource[timedLoopStart..timedLoopEnd];
        Check(timedLoop.Contains("try { _sensitiveFilter.Reload(); }",
                  StringComparison.Ordinal)
              && timedLoop.Contains("Task.Delay(1000, ct)",
                  StringComparison.Ordinal)
              && timedLoop.IndexOf("_sensitiveFilter.Reload();",
                  StringComparison.Ordinal)
              < timedLoop.IndexOf("_loginSoc.SendKeepAlivePacket",
                  StringComparison.Ordinal),
            "production timed loop does not independently poll native patterns each second");
    }
    finally
    {
        Console.SetOut(output);
        Directory.SetCurrentDirectory(originalDirectory);
        Directory.Delete(filterDirectory, true);
    }
}

static void TestNativeGateControlFrames()
{
    var register = Convert.FromHexString(
        "77BBAA33BC1B00000000000003000000");
    Check(YbDbLegacy77Codec.TryDecode(register, out var request, out var error), error);
    Equal(7100, request.QueryId, "register query");
    Equal((ushort)3, request.Ident, "register ident");
    Check(NativeGateControlProtocol.TryCreateResponse(request, 0, out var response),
        "register response missing");
    Check(YbDbLegacy77Codec.TryEncode(response, out var encoded, out error), error);
    Check(encoded.SequenceEqual(Convert.FromHexString(
        "77BBAA3301000000000000000D000000")), "register response bytes");

    var openPayload = Encoding.ASCII.GetBytes("223.160.203.135\0");
    var open = new YbDbLegacy77Frame(2359, 133431, 1, openPayload);
    Check(YbDbLegacy77Codec.TryEncode(open, out var openBytes, out error), error);
    Check(openBytes.SequenceEqual(Convert.FromHexString(
        "77BBAA333709000037090200010010003232332E3136302E3230332E31333500")),
        "captured native gate open bytes");
    Check(NativeGateControlProtocol.TryCreateResponse(open, 0, out response),
        "open response missing");
    Equal(2359, response.QueryId, "open response query");
    Equal(0, response.Param, "open response param");
    Equal((ushort)11, response.Ident, "open response ident");

    var close = new YbDbLegacy77Frame(2359, 1, 6, Array.Empty<byte>());
    Check(NativeGateControlProtocol.TryCreateResponse(close, 0, out response),
        "close response missing");
    Equal(2359, response.QueryId, "close response query");
    Equal(1, response.Param, "close response param");
    Equal((ushort)16, response.Ident, "close response ident");

    var parser = new YbDbLegacy77StreamParser();
    var parsed = new List<YbDbLegacy77Frame>();
    foreach (var value in register)
        parser.Append(new[] { value }, parsed.Add);
    Equal(1, parsed.Count, "one-byte split register count");
    Equal(0, parser.BufferedLength, "register parser drained");
}

static void TestNativeGateDataFrames()
{
    var selectRequest = Convert.FromHexString(
        "77BBAA3337090000370902000400110000000000B10F000000000000C1FAC9F100");
    Check(YbDbLegacy77Codec.TryDecode(
        selectRequest, out var outer, out var error), error);
    Check(LegacyGateDataCodec.TryDecodeRequest(
        outer, out var message, out error), error);
    Equal(2359, outer.QueryId, "select request query");
    Equal(133431, outer.Param, "select request outer param");
    Equal((ushort)4017, message.Ident, "select request ident");
    // ⚠️ 这两条原为 5 / `C1FAC9F100`，是照**当时的 C# 行为**写的，不是原版判据。
    // 原版 payload 边界逐字（fn_5CDFxx）：
    //   0x5CDFC5  sub eax, 0xc   ; len - 12
    //   0x5CDFC8  dec eax        ; ★payloadLen = len - 13
    //   0x5CDFCC  cmp [ebp-0x18],0 / jle -> ptr = NULL, len = 0
    // 本帧 payloadLength = 0x11 = 17 ⇒ 17 - 13 = **4**。
    // 而这段抓包的尾字节正是 `00`（见上面十六进制串末尾 `C1FAC9F1 00`），
    // 即那个 `dec eax` 剥掉的是**串尾 NUL 终止符**。
    // 所以原版给 4 字节 `C1FAC9F1`，旧断言多带了终止符。以原版为准。
    Equal(4, message.Body.Length, "select request body length (native: len-13)");
    Check(message.Body.SequenceEqual(Convert.FromHexString("C1FAC9F1")),
        "select request body bytes (NUL terminator stripped, per 0x5CDFC8 dec eax)");

    var response = LegacyGateDataCodec.CreateResponse(
        2359, 0, 4017, 1, 0, 0, Array.Empty<byte>());
    Check(YbDbLegacy77Codec.TryEncode(response, out var responseBytes, out error), error);
    Check(responseBytes.SequenceEqual(Convert.FromHexString(
        "77BBAA3337090000000000000E000C0000000000B10F010000000000")),
        "select response bytes");

    var listBody = Convert.FromHexString(
        "04C1FAC9F100000000000000000000000100003100");
    response = LegacyGateDataCodec.CreateResponse(
        2359, 1, 4010, 1, 0, 0, listBody);
    Check(YbDbLegacy77Codec.TryEncode(response, out responseBytes, out error), error);
    Check(YbDbLegacy77Codec.TryDecode(responseBytes, out outer, out error), error);
    Equal(0, outer.Param, "data response outer param");
    Equal((ushort)14, outer.Ident, "data response outer ident");
    Equal(33, outer.Payload.Length, "list response payload length");
    Equal((byte)0, outer.Payload[^1], "list response trailing zero");
}

static void TestNativeLoginResultBody()
{
    var body = NativeLoginResultCodec.Encode(
        "ptidv35blreszj7xl6jz", string.Empty, 180, 1,
        "620Fptidv35blreszj7xl6jz1632Qp5b5OrU");
    Check(body.SequenceEqual(Convert.FromHexString(
        "70746964763335626C7265737A6A37786C366A7A00000000000000000000000000000000000000000000B400000001000000243632304670746964763335626C7265737A6A37786C366A7A3136333251703562354F725500")),
        "4004 login result fixture bytes");
    Equal(NativeLoginResultCodec.BodySize, body.Length, "4004 body length");
    var response = LegacyGateDataCodec.CreateResponse(
        2359, 1, 4004, 1, 1, 0, body);
    Check(YbDbLegacy77Codec.TryEncode(response, out var wire, out var error), error);
    Check(wire.SequenceEqual(Convert.FromHexString(
        "77BBAA3337090000000000000E00640001000000A40F01000100000070746964763335626C7265737A6A37786C366A7A00000000000000000000000000000000000000000000B400000001000000243632304670746964763335626C7265737A6A37786C366A7A3136333251703562354F725500")),
        "4004 complete response fixture bytes");
    Equal(36, NativeLoginResultCodec.CreateReconnectId(
        "ptidv35blreszj7xl6jz").Length, "generated reconnect id length");
}

static void TestNativeLoginGateFrames()
{
    Check(NativeLoginGateProtocol.TryCreateRegistration(
        LegacyGbkText.Decode(Convert.FromHexString("C2EAB7A8CCE5D1E9B7FE")), 9,
        out var registration, out var error), error);
    Check(YbDbLegacy77Codec.TryEncode(registration,
        out var registrationBytes, out error), error);
    Check(registrationBytes.SequenceEqual(Convert.FromHexString(
        "77BBAA330000000000000000D0072800C2EAB7A8CCE5D1E9B7FE0000000000000000000009000000FFFFFFFFFFFFFFFFFFFFFFFF00000000")),
        "5600 registration fixture bytes");

    // TPingMsg.GroupName is array[0..15] of Char (LG source uTypes.pas:165), and
    // the C# LG peer reads only payload[0..16) then treats payload+0x10 as
    // HumCounts[0..5] (LoginGateWireProtocol.TryParseNativeRegistration).
    // A 17..20 GBK-byte name therefore used to spill into HumCounts[0] (锻造人数)
    // and be truncated by the peer -> must be rejected outright.
    Check(NativeLoginGateProtocol.GroupNameSize == 16, "TPingMsg GroupName is 16 bytes");
    Check(NativeLoginGateProtocol.HumanCountsOffset == 16, "HumCounts starts at payload+0x10");
    Check(!NativeLoginGateProtocol.TryCreateRegistration(
        new string('A', 17), 1, out _, out _),
        "17-byte server name must be rejected (would corrupt HumCounts[0])");
    Check(NativeLoginGateProtocol.TryCreateRegistration(
        new string('A', 16), 1, out var maxName, out error), error);
    Check(maxName.Payload.Length == NativeLoginGateProtocol.RegistrationPayloadSize,
        "16-byte name still yields a 40-byte TPingMsg");
    // HumCounts[0] must stay 0 even at the maximum name length.
    Check(BitConverter.ToInt32(maxName.Payload, 16) == 0,
        "HumCounts[0] (锻造人数) uncorrupted at max name length");
    Check(BitConverter.ToInt32(maxName.Payload, 20) == 1,
        "HumCounts[1] carries the online count");

    var probeRequestBytes = Convert.FromHexString(
        "77BBAA330000000000000000E9031C004407000019FCFFFF4F0B000000000000B40001000000000000000000");
    Check(YbDbLegacy77Codec.TryDecode(probeRequestBytes,
        out var probeRequest, out error), error);
    Check(NativeLoginGateProtocol.TryCreateProbeResponse(probeRequest,
        "124.221.96.15", 7100, 180, 1,
        out var probeResponse, out error), error);
    Check(YbDbLegacy77Codec.TryEncode(probeResponse,
        out var probeResponseBytes, out error), error);
    Check(probeResponseBytes.SequenceEqual(Convert.FromHexString(
        "77BBAA330000000000000000D1071C004407000019FCFFFF4F0BBC1B7CDD600FB40001000000000000000000")),
        "5600 probe response fixture bytes");
    var nonzeroProbe = new YbDbLegacy77Frame(7, 9,
        NativeLoginGateProtocol.ProbeRequestIdent,
        (byte[])probeRequest.Payload.Clone());
    nonzeroProbe.Payload[19] = 0xFF;
    Check(NativeLoginGateProtocol.TryCreateProbeResponse(nonzeroProbe,
        "124.221.96.15", 7100, 180, 1,
        out var sanitizedProbe, out error), error);
    Equal(0, sanitizedProbe.QueryId, "5600 probe response query");
    Equal(0, sanitizedProbe.Param, "5600 probe response param");
    Equal((byte)0, sanitizedProbe.Payload[19], "5600 probe response status");

    var mobileBody = Convert.FromHexString(
        "6334313361626566306431623637316335386461353933616239336437633936004CF2D1CFFFFFFFFF0067616D65746561006D6F62696C652D6D61632D6164647265737300");
    Equal(NativeMobileLoginAuthCodec.CapturedBodySize, mobileBody.Length,
        "4004 captured body length");
    Check(NativeMobileLoginAuthCodec.TryDecode(mobileBody,
        out var mobileRequest, out error), error);
    Equal("c413abef0d1b671c58da593ab93d7c96", mobileRequest.Ticket,
        "4004 ticket");
    Check(mobileRequest.DeviceId.SequenceEqual(
        Convert.FromHexString("4CF2D1CFFFFFFFFF")), "4004 device id");
    Equal("gametea", mobileRequest.GameType, "4004 game type");
    Equal("mobile-mac-address", mobileRequest.DeviceName, "4004 device name");

    var deviceId = Convert.FromHexString("4CF2D1CFFFFFFFFF");
    Check(NativeLoginGateProtocol.TryCreateAuthRequest(
        2359, "c413abef0d1b671c58da593ab93d7c96", deviceId,
        "223.160.203.135", "mobile-mac-address", 180, 1,
        out var request, out error), error);
    Check(YbDbLegacy77Codec.TryEncode(request, out var requestBytes, out error), error);
    Check(requestBytes.SequenceEqual(Convert.FromHexString(
        "77BBAA330000000000000000E2078800000137090000000000000000633431336162656630643162363731633538646135393361623933643763393600000000000000000000000000000000000000004CF2D1CFFFFFFFFF0000000000000000000000000000000000000000000000003232332E3136302E3230332E313335006D6F62696C652D6D61632D616464726573730000B4000100")),
        "5600 auth request fixture bytes");

    var responseBytes = Convert.FromHexString(
        "77BBAA330000000000000000EB037C0006013709000000000000000070746964763335626C7265737A6A37786C366A7A0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
    Check(YbDbLegacy77Codec.TryDecode(responseBytes, out var responseFrame, out error), error);
    Check(NativeLoginGateProtocol.TryDecodeAuthResponse(
        responseFrame, out var response, out error), error);
    Equal((byte)6, response.Status, "5600 auth response status");
    Equal((byte)1, response.Version, "5600 auth response version");
    Equal(2359, response.QueryId, "5600 auth response query");
    Equal("ptidv35blreszj7xl6jz", response.Account, "5600 auth response account");
    Check(response.Reserved6To11.All(value => value == 0),
        "5600 auth response reserved bytes");
    Equal(string.Empty, response.Text33, "5600 auth response text +33");
    Equal(string.Empty, response.Text54, "5600 auth response text +54");
    Equal((ushort)0, response.Flags75, "5600 auth response flags +75");
    Equal((byte)0, response.Byte77, "5600 auth response byte +77");
    Equal((byte)0, response.Byte78, "5600 auth response byte +78");
    Equal((byte)0, response.Byte79, "5600 auth response byte +79");
    Equal((byte)0, response.Byte80, "5600 auth response byte +80");
    Equal(string.Empty, response.Text81, "5600 auth response text +81");
    Equal(string.Empty, response.Text102, "5600 auth response text +102");
    Check(response.RawPayload.SequenceEqual(responseFrame.Payload),
        "5600 auth response raw payload");

    var populatedPayload = (byte[])responseFrame.Payload.Clone();
    Encoding.ASCII.GetBytes("text33").CopyTo(populatedPayload, 33);
    Encoding.ASCII.GetBytes("text54").CopyTo(populatedPayload, 54);
    BitConverter.TryWriteBytes(populatedPayload.AsSpan(75, 2), (ushort)0x1234);
    populatedPayload[77] = 0x56;
    populatedPayload[78] = 0x78;
    populatedPayload[79] = 0x9A;
    populatedPayload[80] = 0xBC;
    Encoding.ASCII.GetBytes("text81").CopyTo(populatedPayload, 81);
    Encoding.ASCII.GetBytes("text102").CopyTo(populatedPayload, 102);
    Check(NativeLoginGateProtocol.TryDecodeAuthResponse(
        new YbDbLegacy77Frame(0, 0, NativeLoginGateProtocol.AuthResponseIdent,
            populatedPayload), out response, out error), error);
    Equal("text33", response.Text33, "populated 5600 text +33");
    Equal("text54", response.Text54, "populated 5600 text +54");
    Equal((ushort)0x1234, response.Flags75, "populated 5600 flags +75");
    Equal((byte)0x56, response.Byte77, "populated 5600 byte +77");
    Equal((byte)0x78, response.Byte78, "populated 5600 byte +78");
    Equal((byte)0x9A, response.Byte79, "populated 5600 byte +79");
    Equal((byte)0xBC, response.Byte80, "populated 5600 byte +80");
    Equal("text81", response.Text81, "populated 5600 text +81");
    Equal("text102", response.Text102, "populated 5600 text +102");
}

static void TestNativeDbServerFrames()
{
    var requestBytes = Convert.FromHexString(
        "77BBAA33020000000C0000003C0075790000000009000000");
    Check(LegacyDbServerFrameCodec.TryDecode(
        requestBytes, out var request, out var error), error);
    Equal((ushort)2, request.Type, "native request type");
    Equal((ushort)0, request.Reserved, "native request reserved");
    Equal(12, request.Payload.Length, "native request payload length");
    Equal((ushort)0x003C, BitConverter.ToUInt16(request.Payload, 0),
        "native request opcode");
    Check(LegacyDbServerFrameCodec.TryEncode(request, out var encoded, out error), error);
    Check(encoded.SequenceEqual(requestBytes), "native request roundtrip bytes");

    var nonzeroReserved = (byte[])requestBytes.Clone();
    nonzeroReserved[6] = 0x34;
    nonzeroReserved[7] = 0x12;
    Check(LegacyDbServerFrameCodec.TryDecode(
        nonzeroReserved, out var reservedFrame, out error), error);
    Equal((ushort)0x1234, reservedFrame.Reserved,
        "native nonzero reserved word preserved");

    var largePayload = new byte[0xFFFC];
    BitConverter.TryWriteBytes(largePayload.AsSpan(0, 2), (ushort)0x0050);
    Check(LegacyDbServerFrameCodec.TryEncode(
        new LegacyDbServerFrame(1, 0, largePayload), out var largeFrame, out error), error);
    Equal(65544, largeFrame.Length, "native large response frame length");

    var parser = new LegacyDbServerStreamParser(128 * 1024, 128 * 1024);
    var parsed = new List<LegacyDbServerFrame>();
    parser.Append(requestBytes.AsSpan(0, 5), parsed.Add);
    parser.Append(requestBytes.AsSpan(5), parsed.Add);
    parser.Append(largeFrame.AsSpan(0, 11), parsed.Add);
    parser.Append(largeFrame.AsSpan(11, 32768), parsed.Add);
    parser.Append(largeFrame.AsSpan(32779), parsed.Add);
    Equal(2, parsed.Count, "native split/coalesced frame count");
    Equal((ushort)0x0050, BitConverter.ToUInt16(parsed[1].Payload, 0),
        "native response opcode");
    Equal(0, parser.BufferedLength, "native parser drained");
}

static void TestDbServerWireModeDetection()
{
    var native = Convert.FromHexString(
        "77BBAA33020000000C0000003C0075790000000009000000");
    for (var split = 1; split < 4; split++)
    {
        var detector = new DbServerWireModeDetector();
        Check(detector.TryAppend(native, 0, split, out var replay, out var error), error);
        Equal(DbServerWireMode.NativeType12, detector.Mode,
            $"native split {split} selected mode from first byte");
        Check(replay.SequenceEqual(native[..split]),
            $"native split {split} first replay bytes");
        Check(detector.TryAppend(native, split, native.Length - split,
            out var remainder, out error), error);
        Equal(DbServerWireMode.NativeType12, detector.Mode,
            $"native split {split} selected mode");
        Check(replay.Concat(remainder).SequenceEqual(native),
            $"native split {split} replay bytes");
    }

    var privateDetector = new DbServerWireModeDetector();
    var privatePrefix = new byte[] { (byte)'#', 22, 0, 0, 0 };
    Check(privateDetector.TryAppend(privatePrefix, 0, privatePrefix.Length,
        out var privateReplay, out var privateError), privateError);
    Equal(DbServerWireMode.PrivateRequestServer, privateDetector.Mode, "private mode");
    Check(privateReplay.SequenceEqual(privatePrefix), "private replay bytes");

    var invalidDetector = new DbServerWireModeDetector();
    var invalid = Convert.FromHexString("77BBAB33");
    Check(invalidDetector.TryAppend(invalid, 0, invalid.Length,
        out var invalidReplay, out var invalidError), invalidError);
    Equal(DbServerWireMode.NativeType12, invalidDetector.Mode,
        "non-private prefix did not select native resynchronizing parser");
    Check(invalidReplay.SequenceEqual(invalid), "native invalid-prefix replay bytes");

    var resyncParser = new LegacyDbServerStreamParser();
    var resynced = new List<LegacyDbServerFrame>();
    resyncParser.Append(invalid.Concat(native).ToArray(), resynced.Add);
    Equal(1, resynced.Count, "native parser did not recover after wrong magic");
    Equal((ushort)0x003C, BitConverter.ToUInt16(resynced[0].Payload),
        "native parser recovered wrong frame");

    var tailParser = new LegacyDbServerStreamParser();
    var tailFrames = new List<LegacyDbServerFrame>();
    tailParser.Append(Enumerable.Repeat((byte)0xA5, 11).ToArray(), tailFrames.Add);
    Equal(11, tailParser.BufferedLength, "native parser did not preserve 11-byte tail");
    tailParser.Append(native, tailFrames.Add);
    Equal(1, tailFrames.Count, "native parser did not recover after 11-byte tail");
    Equal(0, tailParser.BufferedLength, "native parser did not drain tail plus frame");

    var oversizedHeader = new byte[LegacyDbServerFrameCodec.HeaderSize];
    BitConverter.TryWriteBytes(oversizedHeader.AsSpan(0, 4),
        LegacyDbServerFrameCodec.FrameMagic);
    BitConverter.TryWriteBytes(oversizedHeader.AsSpan(4, 2), (ushort)1);
    BitConverter.TryWriteBytes(oversizedHeader.AsSpan(8, 4),
        LegacyDbServerStreamParser.NativeMaximumBufferedLength
        - LegacyDbServerFrameCodec.HeaderSize);
    var oversizedParser = new LegacyDbServerStreamParser();
    oversizedParser.Append(oversizedHeader, _ => { });
    Equal(0, oversizedParser.BufferedLength,
        "declared 0x20000-byte frame did not clear native buffer");
    var afterOversized = new List<LegacyDbServerFrame>();
    oversizedParser.Append(native, afterOversized.Add);
    Equal(1, afterOversized.Count,
        "native parser did not recover on receive after oversized frame");

    var exactBufferParser = new LegacyDbServerStreamParser();
    exactBufferParser.Append(
        Enumerable.Repeat((byte)0xA5,
            LegacyDbServerStreamParser.NativeMaximumBufferedLength).ToArray(), _ => { });
    Equal(11, exactBufferParser.BufferedLength,
        "native parser rejected an exactly 0x20000-byte receive buffer");
    exactBufferParser.Reset();
    exactBufferParser.Append(
        new byte[LegacyDbServerStreamParser.NativeMaximumBufferedLength + 1], _ => { });
    Equal(0, exactBufferParser.BufferedLength,
        "native parser did not ignore a receive block over 0x20000 bytes");

    var strict = new LegacyDbServerStreamParser(128 * 1024, 128 * 1024, strict: true);
    var strictFailed = false;
    try { strict.Append(Convert.FromHexString("77BBAB33"), _ => { }); }
    catch (InvalidOperationException) { strictFailed = true; }
    Check(strictFailed, "strict native parser accepted wrong magic");
}

static void TestNativeDbServerProtocol()
{
    var heartbeatWire = Convert.FromHexString(
        "77BBAA33020000000C0000003C0075790000000009000000");
    Check(LegacyDbServerFrameCodec.TryDecode(
        heartbeatWire, out var heartbeatFrame, out var error), error);
    Check(NativeDbServerProtocol.TryDecodeHeartbeat(
        heartbeatFrame, out var heartbeat, out error), error);
    Equal((ushort)0x7975, heartbeat.UninitializedWord,
        "native heartbeat uninitialized word preserved for diagnostics");
    Equal(0, heartbeat.State, "native heartbeat state");
    Equal(9, heartbeat.UserCount, "native heartbeat user count");

    var heartbeatWithTail = new LegacyDbServerFrame(2, 0xBEEF,
        heartbeatFrame.Payload.Concat(new byte[] { 0xAA, 0x55 }).ToArray());
    Check(NativeDbServerProtocol.TryDecodeHeartbeat(
        heartbeatWithTail, out var tailedHeartbeat, out error), error);
    Equal(9, tailedHeartbeat.UserCount,
        "native heartbeat with reserved/tail user count");

    var capturedPrefix = Convert.FromHexString(
        "77BBAA3301000000FCFF0000500000000000000000000000000000001470746964763335626C7265737A6A37786C366A7A04C1FAC9F1000000000000000000000000000000000000000000000000000000000000000000000000000004C1FAC9F10000000000000000000000013300000000000000000000000000001470746964763335626C7265737A6A37786C366A7A004A014A01020031000100");
    var nativeData = new byte[NativeHumanDataCodec.DataRecordSize];
    capturedPrefix.AsSpan(LegacyDbServerFrameCodec.HeaderSize
                          + NativeDbServerProtocol.NativeDataOffset)
        .CopyTo(nativeData);
    var characterName = LegacyGbkText.Decode(Convert.FromHexString("C1FAC9F1"));
    var cachedDateTimeBits = unchecked((long)0x8877665544332211UL);
    var sessionContext = new NativeHumanSessionContext
    {
        UserIp = "223.160.203.135",
        AuthText54 = "text54",
        AuthFlags75 = 0x1234,
        AuthByte77 = 0x56,
        AuthByte78 = 0x78,
        SelectionState = 1,
        GroupIndex = 1,
        ZoneIndex = 180,
        ConnectionId = 2359,
        LoginElapsedMilliseconds = 0x11223344,
        AuthText81 = "text81",
        AuthText102 = "text102",
        SessionMode = 1,
        CachedValue38 = unchecked((int)cachedDateTimeBits),
        CachedValue3C = unchecked((int)(cachedDateTimeBits >> 32))
    };
    Check(NativeDbServerProtocol.TryCreateLoadHumanFrame(
        "ptidv35blreszj7xl6jz", characterName, nativeData, null,
        sessionContext,
        out var loadFrame, out error), error);
    Check(LegacyDbServerFrameCodec.TryEncode(loadFrame, out var loadWire, out error), error);
    Equal(65544, loadWire.Length, "native selected-human total length");
    Check(loadWire.AsSpan(0, capturedPrefix.Length).SequenceEqual(capturedPrefix),
        "native selected-human captured prefix bytes");
    Equal(NativeDbServerProtocol.LoadHumanPayloadSize,
        BitConverter.ToInt32(loadWire, 8), "native selected-human payload length");
    Equal((ushort)NativeDbServerProtocol.LoadHumanCommand,
        BitConverter.ToUInt16(loadWire, 12), "native selected-human command");

    var scriptData = Convert.FromHexString("00000000");
    Check(NativeDbServerProtocol.TryCreateLoadHumanFrame(
        "ptidv35blreszj7xl6jz", characterName, nativeData, scriptData,
        sessionContext,
        out loadFrame, out error), error);
    Check(loadFrame.Payload.AsSpan(NativeDbServerProtocol.ScriptDataOffset,
            scriptData.Length).SequenceEqual(scriptData),
        "native selected-human ScriptData offset");
    var suffix = loadFrame.Payload.AsSpan(NativeDbServerProtocol.HumanInfoSuffixOffset,
        NativeDbServerProtocol.HumanInfoSuffixSize);
    Equal((byte)15, suffix[0x00], "native session user IP length");
    Check(suffix.Slice(0x01, 15).SequenceEqual(
        Encoding.ASCII.GetBytes("223.160.203.135")), "native session user IP");
    Equal((byte)20, suffix[0x10], "native session account length");
    Check(suffix.Slice(0x11, 20).SequenceEqual(
        Encoding.ASCII.GetBytes("ptidv35blreszj7xl6jz")), "native session account");
    Equal((byte)6, suffix[0x25], "native session text +54 length");
    Check(suffix.Slice(0x26, 6).SequenceEqual(Encoding.ASCII.GetBytes("text54")),
        "native session text +54");
    Equal((byte)0x78, suffix[0x48], "native session byte +78");
    Equal((byte)0x56, suffix[0x49], "native session byte +77");
    Equal((byte)1, suffix[0x4A], "native session selection state");
    Equal((byte)1, suffix[0x4B], "native session group index");
    Equal((ushort)180, BitConverter.ToUInt16(suffix.Slice(0x4C, 2)),
        "native session zone index");
    Equal((ushort)2359, BitConverter.ToUInt16(suffix.Slice(0x4E, 2)),
        "native session connection id");
    // 0x55 与 0x56 是两个**独立**位域，原版分别逐位测试后送往不同玩家对象字段：
    //   0x6B09AB test [ebx+0x55],2 / 0x6B09D7 test [ebx+0x55],0x10 / 0x6B09E7 test [ebx+0x55],0x20
    //   0x6B09BB test [ebx+0x56],4    -> obj+0xB77
    //   0x6B0A1C test [ebx+0x56],1    -> obj+0x4C6
    //   0x6B0A2C test [ebx+0x56],0x10 -> obj+0xB74 (IsNetCafeUser)
    // 此前这里断言 ToUInt16(Slice(0x55,2)) == 0x1234，即把「写 0x55 的 ushort
    // 顺带覆盖 0x56」这一**语义污染**锁成了预期行为。AuthFlags75 的三个被测位
    // (1/0x10/0x20) 全在低字节内，故 0x55 单字节即完备，0x56 必须独立。
    // ⚠️ 更正：原版 0x55..0x56 是**一个 u16**（0x5CDDDF `mov ax,[eax+0x75]` /
    // 0x5CDDE3 `mov word [ebp-0x53],ax`），且 0x598752 `or word [eax+0x55],0x800`
    // 有条件置 bit11 —— bit11 在**高字节**。AwardPlayerFlag == 0x0800 正是该位。
    // 我先前把这里改成「0x55 单字节 + 0x56 恒 0」，会把领奖标志整位截掉，
    // 是我引入的回归。现按 u16 断言，并显式钉住 bit11 必须能过 0x56。
    Equal((ushort)0x1234, BitConverter.ToUInt16(suffix.Slice(0x55, 2)),
        "native session flags +75 is one u16 (native reads rec+0x75 as a word)");

    // 回归护栏：AwardPlayerFlag(0x0800) 是 u16 的 bit11 = 字节 0x56 的 bit3。
    // 任何把 0x55 当单字节写的实现都会丢掉它，这条断言就是为拦住那个改法。
    var awardProbe = new byte[NativeDbServerProtocol.HumanInfoSuffixSize];
    var awardCtx = new NativeHumanSessionContext
    {
        UserIp = "1.2.3.4",
        AuthFlags75 = NativeDbServerProtocol.AwardPlayerFlag,
        SelectionState = 1,
        SessionMode = 1,
    };
    Check(NativeDbServerProtocol.TryWriteSessionSuffix(
            awardProbe, "acct", awardCtx, out _),
        "award-flag probe suffix writes");
    Equal((ushort)0x0800,
        (ushort)(BitConverter.ToUInt16(awardProbe.AsSpan(0x55, 2)) & 0x0800),
        "AwardPlayerFlag(bit11) must survive into suffix+0x56 bit3 "
        + "(native or word [eax+0x55],0x800 @0x598752)");
    Equal((ushort)0x0800, NativeDbServerProtocol.AwardPlayerFlag,
        "native award-player suffix flag");

    // ---- suffix+0x40..0x47 = DB 时钟基准（Delphi TDateTime double）----
    // 原版 0x59A9E6 `fstp qword ptr [eax+0x40]` 写未截断 Now()；sub_5986CC 把该结构
    // blit 进记录 +0xEF00（0x28*4=0xA0 + 0x42*4=0x108 = 0x1A8 == HumanInfoSuffixSize）。
    // GameSvr 侧读 5 次（0x6B0289/02DD/03EB/04CD 的 fsub、0x6B075D 的 fld
    // qword [eax+0xef40]），基准恒 0 会让倍经验/真视/彩色文字整族永不恢复。
    // ⚠️ 该写入是 disp8 编码，用 4 字节位移模式检索必定假阴性。
    var clockBase = BitConverter.ToDouble(suffix.Slice(0x40, 8));
    Check(clockBase > 0.0, "suffix+0x40 DB clock base must be populated, not 0.0");

    // 契约(2)：不得截断。0x59A9F0 的 Trunc 结果落到 struct+0x58，不回写 +0x40，
    // 故 +0x40 保留亚秒精度。写入 Trunc 过的值会让到期判定整体偏最多一天。
    Check(clockBase != Math.Floor(clockBase),
        "suffix+0x40 must keep sub-day precision (native Trunc goes to struct+0x58, "
        + "never back to +0x40)");

    // 该 double 必须是合法的 Delphi TDateTime（epoch 1899-12-30）且落在当下附近。
    // 用仓库既有的 HUtil32 转换，不自造 epoch 算法。
    var clockAsDateTime = HUtil32.DoubleToDateTime(clockBase);
    Check(Math.Abs((DateTime.Now - clockAsDateTime).TotalMinutes) < 10,
        "suffix+0x40 decodes as a Delphi TDateTime near the send moment, "
        + $"got {clockAsDateTime:yyyy-MM-dd HH:mm:ss}");

    // 契约(1)：取值时机 = 发送时刻，不是登录时刻。原版 sub_59DC1C 的 fan-out 循环
    // 里逐个 GameServer 重新求值，故该成员必须可写（非 init-only），
    // 否则无法在每次发送时刷新。
    var clockProp = typeof(NativeHumanSessionContext).GetProperty("DbClockBase");
    Check(clockProp != null, "NativeHumanSessionContext exposes DbClockBase");
    Check(clockProp!.SetMethod != null && clockProp.SetMethod!.IsPublic,
        "DbClockBase must be settable per send (native re-evaluates it in the "
        + "sub_59DC1C fan-out loop, so an init-only snapshot is wrong)");
    Equal(0x11223344u, BitConverter.ToUInt32(suffix.Slice(0x64, 4)),
        "native session elapsed tick");
    Equal((byte)6, suffix[0x68], "native session text +81 length");
    Equal((byte)7, suffix[0x7D], "native session text +102 length");
    Check(suffix.Slice(0x7E, 7).SequenceEqual(
        Encoding.ASCII.GetBytes("text102")),
        "native session text +102");
    Equal((byte)1, suffix[0x92], "native session mode");
    Equal(unchecked((int)0x44332211),
        BitConverter.ToInt32(suffix.Slice(0x98, 4)),
        "native session cached value +38");
    Equal(unchecked((int)0x88776655),
        BitConverter.ToInt32(suffix.Slice(0x9C, 4)),
        "native session cached value +3C");
    Equal(cachedDateTimeBits, BitConverter.ToInt64(suffix.Slice(0x98, 8)),
        "native session cached qword bits");
    Check(suffix.Slice(NativeDbServerProtocol.SessionPrefixSize,
            NativeDbServerProtocol.LoginExtensionSize).ToArray().All(value => value == 0),
        "native first-login extension must be zero");

    var loginExtension = Enumerable.Range(0, NativeDbServerProtocol.LoginExtensionSize)
        .Select(value => (byte)value).ToArray();
    var resumedContext = new NativeHumanSessionContext
    {
        UserIp = sessionContext.UserIp,
        AuthText54 = sessionContext.AuthText54,
        SelectionState = 1,
        GroupIndex = 1,
        ZoneIndex = 180,
        ConnectionId = 2359,
        SessionMode = 1,
        LoginExtension = loginExtension
    };
    Check(NativeDbServerProtocol.TryCreateLoadHumanFrame(
        "ptidv35blreszj7xl6jz", characterName, nativeData, null,
        resumedContext, out loadFrame, out error), error);
    Check(loadFrame.Payload.AsSpan(
            NativeDbServerProtocol.HumanInfoSuffixOffset
            + NativeDbServerProtocol.SessionPrefixSize,
            NativeDbServerProtocol.LoginExtensionSize).SequenceEqual(loginExtension),
        "native login extension copy");

    var savePayload = (byte[])loadFrame.Payload.Clone();
    BitConverter.TryWriteBytes(savePayload.AsSpan(0, 2),
        NativeDbServerProtocol.SaveHumanCommand);
    BitConverter.TryWriteBytes(savePayload.AsSpan(2, 2), (ushort)7);
    BitConverter.TryWriteBytes(savePayload.AsSpan(8, 4), 0x10203040);
    BitConverter.TryWriteBytes(savePayload.AsSpan(12, 4), 0x50607080);
    Array.Resize(ref savePayload, NativeDbServerProtocol.ScriptDataOffset
                                  + scriptData.Length);
    scriptData.CopyTo(savePayload, NativeDbServerProtocol.ScriptDataOffset);
    var saveFrame = new LegacyDbServerFrame(1, 0, savePayload);
    Check(NativeDbServerProtocol.TryDecodeSaveHuman(
        saveFrame, out var saveRequest, out error), error);
    Equal("ptidv35blreszj7xl6jz", saveRequest.Account,
        "native save header account");
    Equal(characterName, saveRequest.CharacterName,
        "native save header character");
    Equal((ushort)7, saveRequest.HeaderWord2, "native save header word +2");
    Equal(0x10203040, saveRequest.HeaderValue8, "native save header value +8");
    Equal(0x50607080, saveRequest.HeaderValueC, "native save header value +C");
    Check(saveRequest.NativeData.SequenceEqual(nativeData), "native save raw data");
    Check(saveRequest.NativeScriptData.SequenceEqual(scriptData),
        "native save ScriptData");
    Check(saveRequest.HumanInfoSuffix.AsSpan(
            NativeDbServerProtocol.SessionPrefixSize,
            NativeDbServerProtocol.LoginExtensionSize).SequenceEqual(loginExtension),
        "native save human-info suffix");
    Check(NativeDbServerProtocol.TryDecodeSaveHuman(
        new LegacyDbServerFrame(1, 0xA55A, savePayload), out _, out error), error);

    var persistencePayload = (byte[])savePayload.Clone();
    var nativeOffset = NativeDbServerProtocol.NativeDataOffset;
    BitConverter.TryWriteBytes(persistencePayload.AsSpan(nativeOffset + 0x3C, 2),
        (ushort)0);
    persistencePayload[nativeOffset + 0x3F] = 0x45;
    persistencePayload[nativeOffset + 0x40] = 0x23;
    BitConverter.TryWriteBytes(persistencePayload.AsSpan(nativeOffset + 0x50, 4),
        0xF1234567u);
    BitConverter.TryWriteBytes(persistencePayload.AsSpan(nativeOffset + 0x174, 4),
        0x10203040);
    persistencePayload[nativeOffset + 0x16E] = 0xB6;
    persistencePayload[nativeOffset + 0x16F] = 0xA5;
    BitConverter.TryWriteBytes(persistencePayload.AsSpan(nativeOffset + 0x53E, 2),
        (ushort)0xC7D8);
    Check(NativeDbServerProtocol.TryDecodeSaveHuman(
        new LegacyDbServerFrame(1, 0, persistencePayload),
        out var persistenceRequest, out error), error);
    Check(NativeDbServerProtocol.TryCreateSavePersistenceData(
        persistenceRequest, out var persistence, out error), error);
    Equal(NativeHumanDataCodec.DataSizeMarker, persistence.DataBlob.Length,
        "native save fixed blob length");
    Equal(NativeHumanDataCodec.DataSizeMarker,
        BitConverter.ToUInt16(persistence.DataBlob, 4),
        "native save fixed blob size marker");
    Equal((ushort)0, BitConverter.ToUInt16(persistence.DataBlob, 6),
        "native save fixed blob auxiliary length");
    Equal((ushort)1, persistence.Level, "native save zero level normalization");
    Equal((ushort)1, BitConverter.ToUInt16(persistence.DataBlob,
        NativeDbServerProtocol.HumanInfoPrefixSize + 0x3C),
        "native save normalized level persisted");
    Equal(0xF1234567u, persistence.Experience, "native save index experience");
    Equal((byte)0x23, persistence.Job, "native save index job");
    Equal((byte)0x45, persistence.Sex, "native save index sex");
    Equal(0x10203040, persistence.ApprenticeNum,
        "native save index apprentice count");
    Equal((byte)0xA5, persistence.HeroCardLevel,
        "native save index hero-card level");
    Equal((byte)0xB6, persistence.PlatinaCharacterLevel,
        "native save index platina level");
    Equal((ushort)0xC7D8, persistence.SfLevel, "native save index sf level");
    Equal(0x100, persistence.ScriptDataBlob.Length,
        "native save ScriptData aligned length");
    Equal((ushort)scriptData.Length,
        BitConverter.ToUInt16(persistence.ScriptDataBlob, 4),
        "native save ScriptData raw length");
    Check(persistence.ScriptDataBlob.AsSpan(8, scriptData.Length)
            .SequenceEqual(scriptData),
        "native save ScriptData raw bytes");
    Check(persistence.ScriptDataBlob.AsSpan(8 + scriptData.Length).ToArray()
            .All(value => value == 0),
        "native save ScriptData padding");
    Check(NativeHumanDataCodec.LooksLikeNativeDataBlob(persistence.DataBlob),
        "native save exact fixed blob detection");
    Check(NativeHumanDataCodec.TryDecode(persistence.DataBlob,
        persistence.ScriptDataBlob, out var decodedPersistence, out error), error);
    Equal((ushort)1, decodedPersistence.Data.Abil.Level,
        "native save exact fixed blob decode");

    var mismatchedIdentityPayload = (byte[])persistencePayload.Clone();
    mismatchedIdentityPayload[nativeOffset] = 1;
    mismatchedIdentityPayload[nativeOffset + 1] = (byte)'X';
    Check(NativeDbServerProtocol.TryDecodeSaveHuman(
        new LegacyDbServerFrame(1, 0, mismatchedIdentityPayload),
        out var mismatchedIdentityRequest, out error), error);
    Check(!NativeDbServerProtocol.TryCreateSavePersistenceData(
            mismatchedIdentityRequest, out _, out error),
        "native save accepted mismatched record identity");
    Check(!NativeDbServerProtocol.TryDecodeSaveHuman(
            new LegacyDbServerFrame(2, 0, savePayload), out _, out error),
        "native save accepted type2 envelope");
    Check(!NativeDbServerProtocol.TryDecodeSaveHuman(
            new LegacyDbServerFrame(1, 0,
                savePayload.AsSpan(0, NativeDbServerProtocol.ScriptDataOffset).ToArray()),
            out _, out error), "native save accepted empty ScriptData");
    var invalidSaveHeader = (byte[])savePayload.Clone();
    invalidSaveHeader[NativeDbServerProtocol.AccountOffset] = 21;
    Check(!NativeDbServerProtocol.TryDecodeSaveHuman(
            new LegacyDbServerFrame(1, 0, invalidSaveHeader), out _, out error),
        "native save accepted oversized account header");

    var oversizedScript = new byte[NativeDbServerProtocol.ScriptDataSlotSize + 1];
    Check(!NativeDbServerProtocol.TryCreateLoadHumanFrame(
        "ptidv35blreszj7xl6jz", characterName, nativeData, oversizedScript,
        sessionContext,
        out _, out error), "oversized native ScriptData accepted");

    Check(!NativeDbServerProtocol.TryCreateLoadHumanFrame(
        "ptidv35blreszj7xl6jz", "一二三四五六七八", nativeData, null,
        sessionContext, out _, out error),
        "native DBServer accepted a character name over 15 GBK bytes");

    var boundedParser = new LegacyDbServerStreamParser(
        NativeDbServerProtocol.MaximumFrameLength,
        LegacyDbServerStreamParser.NativeMaximumBufferedLength);
    var maximumPayload = new byte[NativeDbServerProtocol.MaximumFrameLength
                                  - LegacyDbServerFrameCodec.HeaderSize];
    Check(LegacyDbServerFrameCodec.TryEncode(
        new LegacyDbServerFrame(2, 0, maximumPayload), out var maximumWire, out error,
        NativeDbServerProtocol.MaximumFrameLength), error);
    boundedParser.Append(maximumWire, _ => { });
    Check(!LegacyDbServerFrameCodec.TryEncode(
        new LegacyDbServerFrame(2, 0, new byte[maximumPayload.Length + 1]),
        out _, out error, NativeDbServerProtocol.MaximumFrameLength),
        "native DBServer accepted a 0x20000-byte frame");
}

static void TestNativeType2Protocol()
{
    // Captured from the original GS1 (ServerIndex=0). The sender leaves Word2
    // uninitialized, but command/Param1/Param2 establish the native registration.
    var capturedRegistrationWire = Convert.FromHexString(
        "77BBAA33020000000C0000003D00BDC50000000001000000");
    Check(LegacyDbServerFrameCodec.TryDecode(
        capturedRegistrationWire, out var capturedRegistrationFrame,
        out var capturedRegistrationError,
        NativeDbServerProtocol.MaximumFrameLength), capturedRegistrationError);
    Check(NativeType2Protocol.TryDecode(
        capturedRegistrationFrame, out var capturedRegistrationMessage,
        out capturedRegistrationError), capturedRegistrationError);
    Equal(NativeType2Protocol.RegisterCommand,
        capturedRegistrationMessage.Command, "captured type2 registration command");
    Equal(0, capturedRegistrationMessage.Param1,
        "captured type2 registration Param1");
    Equal(1, capturedRegistrationMessage.Param2,
        "captured type2 registration server type");

    // The distinct 0x003C frame carries the current user count in Param2.
    var capturedHeartbeatWire = Convert.FromHexString(
        "77BBAA33020000000C0000003C0075790000000009000000");
    Check(LegacyDbServerFrameCodec.TryDecode(
        capturedHeartbeatWire, out var capturedHeartbeatFrame,
        out var capturedHeartbeatError,
        NativeDbServerProtocol.MaximumFrameLength), capturedHeartbeatError);
    Check(NativeType2Protocol.TryDecode(
        capturedHeartbeatFrame, out var capturedHeartbeatMessage,
        out capturedHeartbeatError), capturedHeartbeatError);
    Equal(NativeType2Protocol.HeartbeatCommand,
        capturedHeartbeatMessage.Command, "captured type2 heartbeat command");
    Equal(9, capturedHeartbeatMessage.Param2,
        "captured type2 heartbeat user count");
    Check(capturedHeartbeatMessage.Command
          != NativeType2Protocol.RegisterCommand,
        "captured heartbeat was mislabeled as registration");

    var goldenWire = Convert.FromHexString(
        "77BBAA330200EFBE100000003F00BBAA0200000044332211DEADBEEF");
    Check(LegacyDbServerFrameCodec.TryDecode(
        goldenWire, out var goldenOuter, out var goldenError,
        NativeDbServerProtocol.MaximumFrameLength), goldenError);
    var originalGoldenPayload = goldenOuter.Payload.ToArray();
    Check(NativeType2Protocol.TryDecode(
        goldenOuter, out var goldenRequest, out goldenError), goldenError);
    Check(NativeType2Protocol.TryCreateRelayFrame(
        goldenRequest, 7, out var goldenResponse, out var goldenTarget,
        out goldenError), goldenError);
    Equal((byte)2, goldenTarget, "type2 golden relay target");
    Check(LegacyDbServerFrameCodec.TryEncode(goldenResponse,
        out var encodedGolden, out goldenError,
        NativeDbServerProtocol.MaximumFrameLength), goldenError);
    Check(encodedGolden.SequenceEqual(Convert.FromHexString(
        "77BBAA3302000000100000006F00BBAA0700000044332211DEADBEEF")),
        "type2 complete golden relay wire");
    Check(goldenOuter.Payload.SequenceEqual(originalGoldenPayload),
        "type2 relay modified request payload");

    var suffix = Convert.FromHexString("102030405060");
    var payload = new byte[NativeType2Protocol.HeaderSize + suffix.Length];
    BitConverter.TryWriteBytes(payload.AsSpan(0, 2), NativeType2Protocol.RelayCommand);
    BitConverter.TryWriteBytes(payload.AsSpan(2, 2), (ushort)0xA55A);
    BitConverter.TryWriteBytes(payload.AsSpan(4, 4), unchecked((int)0xAABBCC04));
    BitConverter.TryWriteBytes(payload.AsSpan(8, 4), 0x11223344);
    suffix.CopyTo(payload, NativeType2Protocol.HeaderSize);
    Check(NativeType2Protocol.TryDecode(
        new LegacyDbServerFrame(2, 0xBEEF, payload), out var request, out var error), error);
    Equal(NativeType2Protocol.RelayCommand, request.Command, "type2 relay command");
    Equal((ushort)0xA55A, request.Word2, "type2 relay word +2");
    Equal(unchecked((int)0xAABBCC04), request.Param1, "type2 relay target source");
    Check(request.Suffix.SequenceEqual(suffix), "type2 relay suffix decode");

    Check(NativeType2Protocol.TryCreateRelayFrame(
        request, 3, out var response, out var targetType, out error), error);
    Equal((byte)4, targetType, "type2 relay target low byte");
    Equal((ushort)2, response.Type, "type2 relay outer type");
    Equal(NativeType2Protocol.RelayResponseCommand,
        BitConverter.ToUInt16(response.Payload, 0), "type2 relay response command");
    Equal((ushort)0xA55A, BitConverter.ToUInt16(response.Payload, 2),
        "type2 relay preserves word +2");
    Equal(3, BitConverter.ToInt32(response.Payload, 4),
        "type2 relay writes sender type");
    Equal(0x11223344, BitConverter.ToInt32(response.Payload, 8),
        "type2 relay preserves param2");
    Check(response.Payload.AsSpan(NativeType2Protocol.HeaderSize)
        .SequenceEqual(suffix), "type2 relay preserves suffix");

    var peers = new byte[] { 0, 2, 3, 3, 4, 4, 9 };
    Check(peers.Where(peer => NativeType2Protocol.ShouldRelay(3, peer, 0))
        .SequenceEqual(new byte[] { 0, 2, 4, 4 }), "type2 broadcast recipients");
    Check(peers.Where(peer => NativeType2Protocol.ShouldRelay(3, peer, 4))
        .SequenceEqual(new byte[] { 4, 4 }), "type2 targeted recipients");
    Equal(2, peers.Count(peer => NativeType2Protocol.ShouldRelay(3, peer, 3)),
        "type2 relay to own type includes sender/same-type peers");

    foreach (var value in new[] { 1, 9, 0x100, int.MaxValue })
    {
        Check(NativeType2Protocol.TryGetRegistrationServerType(
                new NativeType2Message
                {
                    Command = NativeType2Protocol.RegisterCommand,
                    Param2 = value
                }, out var registeredType),
            "positive 003D Param2 rejected");
        Equal(unchecked((byte)value), registeredType,
            "003D low-byte server type");
    }
    foreach (var value in new[] { 0, -1, int.MinValue })
        Check(!NativeType2Protocol.TryGetRegistrationServerType(
                new NativeType2Message
                {
                    Command = NativeType2Protocol.RegisterCommand,
                    Param2 = value
                }, out _),
            "non-positive 003D Param2 accepted");
    Check(!NativeType2Protocol.TryGetRegistrationServerType(
            capturedHeartbeatMessage, out _),
        "003C heartbeat accepted as registration");
    foreach (var command in new ushort[]
             {
                 0x0181, 0x0182, 0x0183,
                 0x0188, 0x0189, 0x018A, 0x018B,
                 0x018C, 0x018D, 0x018E, 0x018F, 0x0190
             })
        Check(NativeType2Protocol.IsSilentNoOpCommand(command),
            $"verified Type2 default no-op 0x{command:X4} was not silent");
    Check(!NativeType2Protocol.IsSilentNoOpCommand(0x0180)
          && !NativeType2Protocol.IsSilentNoOpCommand(0x0184)
          && !NativeType2Protocol.IsSilentNoOpCommand(0x0191),
        "active Type2 command incorrectly treated as default no-op");

    var gameSvrDbServiceSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "GameSvr", "Services",
        "DBService.cs"));
    Check(gameSvrDbServiceSource.Contains("LegacyDbServerStreamParser",
            StringComparison.Ordinal),
        "GameSvr DBService no longer exposes the audited native port-6000 parser");
    Check(gameSvrDbServiceSource.Contains("LegacyDbServerFrameCodec",
              StringComparison.Ordinal)
          && !gameSvrDbServiceSource.Contains("RequestServerFrameParser",
              StringComparison.Ordinal),
        "GameSvr DBService regressed from the captured native wire to the retired private parser");

    var gameSocSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "DBSvr", "Services",
        "GameSocService.cs"));
    var registrationDispatchStart = gameSocSource.IndexOf(
        "if (command == NativeType2Protocol.RegisterCommand)",
        StringComparison.Ordinal);
    var type3DispatchStart = gameSocSource.IndexOf(
        "if (frame.Type == 3)", StringComparison.Ordinal);
    Check(type3DispatchStart >= 0
          && type3DispatchStart < registrationDispatchStart,
        "native Type3 dispatch is no longer independent of Type2 registration");
    var type3Dispatch = gameSocSource[
        type3DispatchStart..registrationDispatchStart];
    Check(type3Dispatch.Contains("NativeType3Protocol.QueryCharactersCommand",
              StringComparison.Ordinal)
          && type3Dispatch.Contains("ProcessNativeType3CharacterQuery(",
              StringComparison.Ordinal),
        "native Type3 character query escaped its independent dispatcher");
    var registrationDispatchEnd = gameSocSource.IndexOf(
        "if (command == NativeType2Protocol.RankingReloadCommand",
        registrationDispatchStart, StringComparison.Ordinal);
    Check(registrationDispatchStart >= 0
          && registrationDispatchEnd > registrationDispatchStart,
        "type2 registration dispatch boundaries are missing");
    Check(gameSocSource.Contains(
              "NativeType2Protocol.IsSilentNoOpCommand(command)",
              StringComparison.Ordinal),
        "Type2 default no-op dispatch is not reachable");
    var registrationDispatch = gameSocSource[
        registrationDispatchStart..registrationDispatchEnd];
    Check(registrationDispatch.Contains("ProcessNativeType2Registration(",
            StringComparison.Ordinal)
          && gameSocSource.Contains("TryGetRegistrationServerType(",
              StringComparison.Ordinal),
        "original type2 registration receiver is not reachable");
    var registrationHandlerStart = gameSocSource.IndexOf(
        "private void ProcessNativeType2Registration", StringComparison.Ordinal);
    var registrationHandlerEnd = gameSocSource.IndexOf(
        "private static bool TrySendNativeType2FramesLocked",
        registrationHandlerStart, StringComparison.Ordinal);
    Check(registrationHandlerStart >= 0
          && registrationHandlerEnd > registrationHandlerStart,
        "type2 registration handler boundaries are missing");
    var registrationHandler = gameSocSource[
        registrationHandlerStart..registrationHandlerEnd];
    var typeWrite = registrationHandler.IndexOf(
        "Volatile.Write(ref sender.NativeServerType", StringComparison.Ordinal);
    var oneTimeGuard = registrationHandler.IndexOf(
        "Interlocked.CompareExchange(", StringComparison.Ordinal);
    Check(typeWrite >= 0 && oneTimeGuard > typeWrite
          && registrationHandler.Contains(
              "CreateGameGateSnapshot(", StringComparison.Ordinal)
          && registrationHandler.Contains(
              "CreatePrimaryFrames(", StringComparison.Ordinal)
          && registrationHandler.Contains(
              "CreateSecondaryFrames(", StringComparison.Ordinal),
        "003D write/order/initialization sequence drifted");

    var sessionBoundDispatchStart = registrationDispatchEnd;
    var sessionBoundDispatchEnd = gameSocSource.IndexOf(
        "$\"[GameSoc] 原生6000暂不支持指令",
        sessionBoundDispatchStart, StringComparison.Ordinal);
    Check(sessionBoundDispatchStart >= 0
          && sessionBoundDispatchEnd > sessionBoundDispatchStart,
        "type2 post-registration dispatch boundaries are missing");
    var sessionBoundDispatch = gameSocSource[
        sessionBoundDispatchStart..sessionBoundDispatchEnd];
    foreach (var commandName in new[]
             {
                 "RankingReloadCommand", "RelayCommand",
                 "LoginGateControlCommand", "WhitelistReloadCommand"
             })
        Check(sessionBoundDispatch.Contains(commandName,
                StringComparison.Ordinal),
            $"type2 {commandName} dispatch is missing");
    Check(sessionBoundDispatch.Contains("ProcessNativeType2Relay(",
              StringComparison.Ordinal)
          && sessionBoundDispatch.Contains(
              "ProcessNativeType2LoginGateControl(", StringComparison.Ordinal)
          && sessionBoundDispatch.Contains(
              "ProcessNativeType2WhitelistReload(", StringComparison.Ordinal)
          && !sessionBoundDispatch.Contains("TryStartReload(",
              StringComparison.Ordinal),
        "type2 runtime dispatch differs from the original receiver");
    var relayHandlerStart = gameSocSource.IndexOf(
        "private void ProcessNativeType2Relay", StringComparison.Ordinal);
    var relayHandlerEnd = gameSocSource.IndexOf(
        "private void ProcessNativeType2LoginGateControl",
        relayHandlerStart, StringComparison.Ordinal);
    var relayHandler = gameSocSource[relayHandlerStart..relayHandlerEnd];
    Check(relayHandler.Contains("DbServerWireMode.NativeType12",
            StringComparison.Ordinal),
        "type2 relay can leak into private RequestServer connections");

    var mainFormSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "DBSvr", "Forms", "MainForm.cs"));
    Check(!mainFormSource.Contains("TryStartReload(",
              StringComparison.Ordinal)
          && !mainFormSource.Contains("RunRankingLoop",
              StringComparison.Ordinal)
          && !mainFormSource.Contains("ShouldStartDailyReload",
              StringComparison.Ordinal),
        "unproven ranking startup/daily runtime trigger is reachable");

    foreach (var value in new[] { 1, 0, 2, -1, int.MinValue })
    {
        var control = new NativeType2Message
        {
            Command = NativeType2Protocol.LoginGateControlCommand,
            Word2 = 0xFFFF,
            Param1 = value,
            Param2 = unchecked((int)0x88776655),
            Suffix = Convert.FromHexString("DEADBEEF")
        };
        Check(NativeType2Protocol.TryGetLoginGateControlEnabled(
            control, out var enabled), "type2 LoginGate control rejected");
        Equal(value == 1, enabled, "type2 LoginGate control Param1 predicate");
        var downstream = NativeLoginGateProtocol.CreateType2Control(enabled);
        Check(YbDbLegacy77Codec.TryEncode(downstream,
            out var controlWire, out var controlError), controlError);
        var expectedIdent = enabled ? "D207" : "D307";
        Check(Convert.ToHexString(controlWire) ==
              "77BBAA330000000000000000" + expectedIdent + "0000",
            "type2 LoginGate control golden wire");
    }
    Check(!NativeType2Protocol.TryGetLoginGateControlEnabled(
            new NativeType2Message { Command = 0x0041 }, out _),
        "non-0x0042 command accepted as LoginGate control");
}

static void TestNativeType2Management()
{
    var rawName = Convert.FromHexString("6162438140");
    var payload = new byte[NativeType2Protocol.HeaderSize + rawName.Length];
    BitConverter.GetBytes(NativeType2Protocol.ResetTransferLockCommand)
        .CopyTo(payload, 0);
    rawName.CopyTo(payload, NativeType2Protocol.HeaderSize);
    Check(NativeType2Protocol.TryDecode(
        new LegacyDbServerFrame(2, 0, payload), out var reset,
        out var error), error);
    Check(reset.Suffix.SequenceEqual(rawName),
        "0186 character-name bytes changed");

    var configPayload = new byte[NativeType2Protocol.HeaderSize];
    BitConverter.GetBytes(NativeType2Protocol.SetVipYbConsumeCommand)
        .CopyTo(configPayload, 0);
    BitConverter.GetBytes(unchecked((int)0x89ABCDEF))
        .CopyTo(configPayload, 8);
    Check(NativeType2Protocol.TryDecode(
        new LegacyDbServerFrame(2, 0, configPayload), out var configMessage,
        out error), error);
    Equal(unchecked((int)0x89ABCDEF), configMessage.Param2,
        "0191 Param2 bits");

    var directory = Path.Combine(Path.GetTempPath(),
        "dbsvr-vip-yb-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "DBService.ini");
    var oldValue = DBShare.VipYBConsume;
    try
    {
        File.WriteAllText(path, "[Setup]\r\nVipYBConsume=1\r\n",
            Encoding.GetEncoding(936));
        var manager = new ConfigManager(path);
        manager.SetVipYbConsume(23);
        Equal(23, DBShare.VipYBConsume, "0191 in-memory value");
        var reloaded = new ConfigManager(path);
        Equal(23, reloaded.ReadInteger("Setup", "VipYBConsume", -1),
            "0191 persisted value");
    }
    finally
    {
        DBShare.VipYBConsume = oldValue;
        Directory.Delete(directory, true);
    }
}

static void TestNativeType2Admission()
{
    var body = new byte[NativeType2AdmissionProtocol.DenyIpBodySize];
    var ipBytes = Encoding.ASCII.GetBytes("1.2.3.4");
    body[0] = (byte)ipBytes.Length;
    ipBytes.CopyTo(body, 1);
    BitConverter.GetBytes(0xFEDCBA98u).CopyTo(body, 16);
    var deny = new NativeType2Message
    {
        Command = NativeType2AdmissionProtocol.DenyIpCommand,
        Suffix = body
    };
    Check(NativeType2AdmissionProtocol.TryDecodeDenyIp(deny,
        out var ip, out var value), "0041 deny IP decode");
    Equal("1.2.3.4", ip, "0041 IP");
    Equal(0xFEDCBA98u, value, "0041 value bits");
    var invalidBody = body.ToArray();
    invalidBody[1] = (byte)'x';
    Check(!NativeType2AdmissionProtocol.TryDecodeDenyIp(
            new NativeType2Message
            {
                Command = NativeType2AdmissionProtocol.DenyIpCommand,
                Suffix = invalidBody
            }, out _, out _),
        "0041 accepted non-IP character");

    var disconnected = string.Empty;
    var drained = 0;
    var oldMaximum = DBShare.MaxSingleIpHumanCount;
    var oldQueueEnabled = DBShare.NativeQueueEnabled;
    var control = new NativeUserAdmissionControl();
    control.Attach(() => new[] { "1.2.3.4", "1.2.3.4", "5.6.7.8" },
        current => disconnected = current,
        () => Interlocked.Increment(ref drained));
    control.SetDenyIp(ip, value);
    Equal(ip, disconnected, "0041 immediate disconnect IP");
    control.SetDenyIp("1.2.", 0);
    Check(control.IsDenyTokenMatch("1.2.9.4")
          && !control.IsDenyTokenMatch("101.2.3.4"),
        "0041 deny token prefix match");
    control.RecountAndSetMaximum(-7);
    Equal(2, control.GetIpCount("1.2.3.4"), "0187 recounted IP");
    Equal(-7, DBShare.MaxSingleIpHumanCount, "0187 signed maximum");

    var maxRequest = new NativeType2Message
    {
        Command = NativeType2AdmissionProtocol.ControlCommand,
        Param1 = 0,
        Param2 = -7,
        Suffix = Encoding.ASCII.GetBytes("request-suffix-123")
    };
    var maxResponse = NativeType2AdmissionProtocol.CreateControlResponse(
        maxRequest);
    Equal((ushort)0x0132, BitConverter.ToUInt16(maxResponse.Payload, 0),
        "0187 max response command");
    Equal((byte)15, maxResponse.Payload[0x25],
        "0187 max response suffix length");
    Equal("单IP最大在线人数已被设置为-7",
        LegacyGbkText.Decode(maxResponse.Payload.AsSpan(0x48).ToArray()),
        "0187 max response text");

    var queueRequest = new NativeType2Message
    {
        Command = NativeType2AdmissionProtocol.ControlCommand,
        Param1 = 1,
        Param2 = 0,
        Suffix = Encoding.ASCII.GetBytes("must-not-echo")
    };
    var queueResponse = NativeType2AdmissionProtocol.CreateControlResponse(
        queueRequest);
    Equal((byte)0, queueResponse.Payload[0x25],
        "0187 queue response leaked suffix");
    Equal("排队系统关闭",
        LegacyGbkText.Decode(queueResponse.Payload.AsSpan(0x48).ToArray()),
        "0187 queue response text");
    Check(!control.SetQueueEnabled(0), "0187 queue disable state");
    control.DrainQueue();
    Equal(1, drained, "0187 queue drain callback");
    var userSocSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "DBSvr", "Services",
        "UserSocService.cs"));
    var disconnectStart = userSocSource.IndexOf(
        "private void DisconnectNativeGateByAddress",
        StringComparison.Ordinal);
    var disconnectEnd = userSocSource.IndexOf(
        "private void DisconnectNativeUserByAccount", disconnectStart,
        StringComparison.Ordinal);
    Check(disconnectStart >= 0 && disconnectEnd > disconnectStart,
        "0041 gate disconnect handler missing");
    var disconnectHandler = userSocSource[
        disconnectStart..disconnectEnd];
    Check(disconnectHandler.Contains("gate.sGateaddr",
              StringComparison.Ordinal)
          && disconnectHandler.Contains(".StartsWith(gateAddress",
              StringComparison.Ordinal)
          && !disconnectHandler.Contains("user.sUserIPaddr",
              StringComparison.Ordinal)
          && !disconnectHandler.Contains("CloseUser(",
              StringComparison.Ordinal),
        "0041 no longer targets the complete matching Gate");
    DBShare.MaxSingleIpHumanCount = oldMaximum;
    DBShare.NativeQueueEnabled = oldQueueEnabled;
}

static void TestNativeRelationLog()
{
    static byte[] Body(string a, string b)
    {
        var body = new byte[0x20];
        var aa = Encoding.ASCII.GetBytes(a);
        var bb = Encoding.ASCII.GetBytes(b);
        body[0] = (byte)aa.Length;
        aa.CopyTo(body, 1);
        body[0x10] = (byte)bb.Length;
        bb.CopyTo(body, 0x11);
        return body;
    }

    static string Text(byte[] bytes) => Encoding.ASCII.GetString(bytes);
    const string d = "@$&#$";
    const string e = "#$@#&";
    var owners = new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["Alice"] = Encoding.ASCII.GetBytes("PTIDA"),
        ["Bob"] = Encoding.ASCII.GetBytes("PTIDB")
    };
    byte[] Owner(byte[] name) => owners.TryGetValue(
        Encoding.ASCII.GetString(name), out var value) ? value : Array.Empty<byte>();

    var request = new NativeType2Message
    {
        Command = NativeRelationLogProtocol.Command,
        Word2 = 0,
        Suffix = Body("Alice", "Bob")
    };
    var records = NativeRelationLogProtocol.BuildMessages(request, Owner,
        () => new DateTime(2026, 1, 2));
    Equal(2, records.Count, "0040 sub0 record count");
    Equal("1" + d + "16" + d + "Alice" + d + "PTIDA" + d + "1" + d
          + "Bob" + d + "PTIDB" + d + "1" + e, Text(records[0]),
        "0040 sub0 first record");
    Equal("1" + d + "8" + d + "Bob" + d + "PTIDB" + d + "1" + d
          + "Alice" + d + "PTIDA" + d + "1" + e, Text(records[1]),
        "0040 sub0 second record");

    var clearRecords = NativeRelationLogProtocol.BuildMessages(
        new NativeType2Message
        {
            Command = NativeRelationLogProtocol.Command,
            Word2 = 2,
            Suffix = Body("Alice", "Bob")
        }, Owner);
    Equal(2, clearRecords.Count, "0040 sub2 record count");
    Equal("2" + d + "16" + d + "Bob" + d + "PTIDB" + d + "1" + d
          + "Alice" + d + "PTIDA" + d + "1" + e, Text(clearRecords[0]),
        "0040 sub2 first record");
    Equal("2" + d + "8" + d + "Alice" + d + "PTIDA" + d + "1" + d
          + "Bob" + d + "PTIDB" + d + "1" + e, Text(clearRecords[1]),
        "0040 sub2 second record");

    var missing = NativeRelationLogProtocol.BuildMessages(
        new NativeType2Message
        {
            Command = NativeRelationLogProtocol.Command,
            Word2 = 0,
            Suffix = Body("Alice", "Missing")
        }, Owner);
    Equal(0, missing.Count, "0040 sub0 owner miss");

    var body4 = new byte[0x54];
    body4[0x44] = 5;
    Encoding.ASCII.GetBytes("Alice").CopyTo(body4, 0x45);
    body4[4] = 1;
    body4[5] = (byte)'X';
    var sub4 = NativeRelationLogProtocol.BuildMessages(
        new NativeType2Message
        {
            Command = NativeRelationLogProtocol.Command,
            Word2 = 4,
            Suffix = body4
        }, Owner, () => new DateTime(2026, 1, 2));
    Equal(3, sub4.Count, "0040 sub4 record count");
    Equal("6" + d + "20260102" + d + "X" + d + "5000" + d
          + "Alice" + d + "PTIDA" + d + "1" + d + e,
        Text(sub4[0]), "0040 sub4 date record");

    var body5 = new byte[0x54];
    body5[4] = 1;
    body5[5] = (byte)'X';
    body5[0x44] = 0xFF;
    var sub5OwnerCalls = 0;
    var sub5 = NativeRelationLogProtocol.BuildMessages(
        new NativeType2Message
        {
            Command = NativeRelationLogProtocol.Command,
            Word2 = 5,
            Suffix = body5
        }, name =>
        {
            sub5OwnerCalls++;
            return Owner(name);
        });
    Equal(1, sub5.Count, "0040 sub5 record count");
    Equal("11" + d + "X" + e, Text(sub5[0]),
        "0040 sub5 record");
    Equal(0, sub5OwnerCalls, "0040 sub5 owner lookup count");
    Check(NativeRelationLogProtocol.BuildMessages(
            new NativeType2Message
            {
                Command = NativeRelationLogProtocol.Command,
                Word2 = 5,
                Suffix = new byte[0x20]
            }, Owner).Count == 0,
        "0040 wrong body length accepted");
}

static void TestNativeStdItemsImport()
{
    NativeType2StaticRow Row(int index)
    {
        var values = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["idx"] = index,
            ["Stdmode"] = 1, ["Shape"] = 2, ["Need"] = 3,
            ["Source"] = 4, ["Looks"] = 5, ["Weight"] = 6,
            ["DuraMax"] = 7, ["AniCount"] = 8, ["NeedConf"] = 9,
            ["NeedLevel"] = 10, ["AC"] = 11, ["MaxAC"] = 12,
            ["MAC"] = 13, ["MaxMAC"] = 14, ["DC"] = 15,
            ["MaxDC"] = 16, ["MC"] = 17, ["MaxMC"] = 18,
            ["SC"] = 19, ["MaxSC"] = 20, ["Price"] = 21,
            ["OutLook"] = 22, ["AntiqueLv"] = 23,
            ["itemScore"] = 24, ["SuitEquipType"] = 25,
            ["BaseEffectID"] = 26, ["wParam1"] = 27,
            ["wParam2"] = 28, ["intParam"] = 29,
            ["intParam2"] = 30, ["intParam3"] = 31,
            ["MaxSteelLv"] = 32, ["MaxVeinsLv"] = 33,
            ["ItemLevel"] = 34, ["ItemConf"] = 35
        };
        return new NativeType2StaticRow(
            new Dictionary<string, byte[]>
            {
                ["Iname"] = Encoding.ASCII.GetBytes("item" + index)
            }, values);
    }

    var notifications = NativeType2StdItemsImportProtocol.BuildRecords(
        new[] { Row(5), Row(6) }, unchecked((int)0x89ABCDEF),
        out var cached);
    Equal(2, notifications.Count, "0180 notification count");
    Equal((ushort)0x00CA, BitConverter.ToUInt16(notifications[0], 0),
        "0180 notification command");
    Equal(unchecked((int)0x89ABCDEF),
        BitConverter.ToInt32(notifications[0], 4),
        "0180 notification correlation");
    Equal(0, BitConverter.ToInt32(notifications[1], 8),
        "0180 notification leaked cache end marker");
    Equal((ushort)0x0068, BitConverter.ToUInt16(cached[0], 0),
        "0180 cache command");
    Equal((ushort)0, BitConverter.ToUInt16(cached[0], 0x46),
        "0180 import BaseEffectID stayed zero");
    Equal((byte)0, cached[0][0x128],
        "0180 missing NeedJob stayed zero");
    Equal(0, BitConverter.ToInt32(cached[0], 8),
        "0180 first cache end marker");
    Equal(1, BitConverter.ToInt32(cached[1], 8),
        "0180 last cache end marker");

    var previous = new byte[0x140];
    BitConverter.GetBytes((ushort)0x0068).CopyTo(previous, 0);
    BitConverter.GetBytes(1).CopyTo(previous, 8);
    var cache = new NativeType2InitializationCache();
    cache.ReplacePrimary(new[] { previous });
    cache.AppendStdItems(cached);
    var snapshot = cache.Snapshot();
    Equal(3, snapshot.Primary.Count, "0180 appended cache count");
    Equal(0, BitConverter.ToInt32(snapshot.Primary[0], 8),
        "0180 previous cache end marker");
    Equal(0, BitConverter.ToInt32(snapshot.Primary[1], 8),
        "0180 appended first marker");
    Equal(1, BitConverter.ToInt32(snapshot.Primary[2], 8),
        "0180 appended last marker");

    var noTerminal = new NativeType2InitializationCache();
    var broken = previous.ToArray();
    BitConverter.GetBytes(0).CopyTo(broken, 8);
    noTerminal.ReplacePrimary(new[] { broken });
    noTerminal.AppendStdItems(cached);
    Equal(1, noTerminal.Snapshot().Primary.Count,
        "0180 cached without a terminal 0068 node");

    var firstTerminal = new NativeType2InitializationCache();
    var terminalA = previous.ToArray();
    var terminalB = previous.ToArray();
    firstTerminal.ReplacePrimary(new[] { terminalA, terminalB });
    firstTerminal.AppendStdItems(cached.Take(1).ToArray());
    var firstSnapshot = firstTerminal.Snapshot();
    Equal(3, firstSnapshot.Primary.Count,
        "0180 first terminal append count");
    Equal(0, BitConverter.ToInt32(firstSnapshot.Primary[0], 8),
        "0180 first terminal marker");
    Equal(1, BitConverter.ToInt32(firstSnapshot.Primary[1], 8),
        "0180 inserted terminal marker");
    Equal(1, BitConverter.ToInt32(firstSnapshot.Primary[2], 8),
        "0180 later terminal was modified");
}

static void TestNativeType2WhitelistReload()
{
    var requestWire = Convert.FromHexString(
        "77BBAA33020000000E000000840100000000000000000000474D");
    Check(LegacyDbServerFrameCodec.TryDecode(requestWire,
        out var outer, out var error,
        NativeDbServerProtocol.MaximumFrameLength), error);
    Check(NativeType2Protocol.TryDecode(outer, out var request, out error), error);
    Check(NativeType2Protocol.ShouldReloadWhiteLists(request),
        "type2 0x0184 Param1=0 was not actionable");
    Check(NativeType2Protocol.TryCreateWhitelistReloadResponse(
        request, out var response, out error), error);
    Check(LegacyDbServerFrameCodec.TryEncode(response,
        out var responseWire, out error,
        NativeDbServerProtocol.MaximumFrameLength), error);
    Check(responseWire.SequenceEqual(Convert.FromHexString(
        "77BBAA33010000005F0000003201000000000000000000000000000000000000000000000000000000000000000000000002474D000000000000000000000000000000000000000000000000000000000000000057686974654C6973742E747874BCD3D4D8B3C9B9A6A3A1")),
        "type2 0x0184 complete golden response wire");

    foreach (var param1 in new[] { 0, 1, -1, int.MinValue })
    {
        var candidate = new NativeType2Message
        {
            Command = NativeType2Protocol.WhitelistReloadCommand,
            Word2 = 0xFFFF,
            Param1 = param1,
            Param2 = unchecked((int)0x88776655),
            Suffix = Convert.FromHexString(
                "303132333435363738394142434445465A")
        };
        Equal(param1 == 0,
            NativeType2Protocol.ShouldReloadWhiteLists(candidate),
            "type2 0x0184 Param1 predicate");
        if (param1 != 0)
        {
            Check(!NativeType2Protocol.TryCreateWhitelistReloadResponse(
                    candidate, out _, out _),
                "type2 0x0184 nonzero Param1 produced a response");
            continue;
        }
        Check(NativeType2Protocol.TryCreateWhitelistReloadResponse(
            candidate, out var truncated, out error), error);
        Equal((byte)15, truncated.Payload[0x25],
            "type2 0x0184 response name truncation");
        Check(truncated.Payload.AsSpan(0x26, 15)
                .SequenceEqual(candidate.Suffix.AsSpan(0, 15)),
            "type2 0x0184 response name raw bytes");
    }

    var directory = Path.Combine(Path.GetTempPath(),
        "dbsvr-whitelist-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var gbk = Encoding.GetEncoding(936);
    void Write(string name, string value) =>
        File.WriteAllText(Path.Combine(directory, name), value, gbk);
    try
    {
        Write("IpAddress.txt", "[Allow]\r\n10.20.0.1\r\n[Deny]\r\n10.20.0.9\r\n");
        Write("WhiteList.txt", "10.20.0.2\r\n");
        Write("GameGateWhiteList.txt", "10.20.0.3\r\n");
        Write("AllowPTID.txt", "ALLOW-PTID\r\n");
        Write("FastPassPTID.txt", "FAST-PTID\r\n");
        Write("!DenyLogon.txt", "DENY-PTID\r\n");
        var whitelist = new WhitelistService();
        whitelist.Load(directory);
        Check(whitelist.IsIpAllowed("10.20.0.1")
              && whitelist.IsIpAllowed("10.20.0.2")
              && whitelist.IsIpAllowed("10.20.0.3"),
            "initial independent IP whitelist sources");
        Check(!whitelist.IsIpAllowed("10.20.0.8")
              && !whitelist.IsIpAllowed("10.20.0.9"),
            "initial IP whitelist/deny behavior");

        Write("WhiteList.txt", "10.20.0.4\r\n");
        Write("GameGateWhiteList.txt", "10.20.0.5\r\n");
        whitelist.ReloadNativeWhiteLists(directory);
        Check(whitelist.IsIpAllowed("10.20.0.1")
              && whitelist.IsIpAllowed("10.20.0.4")
              && whitelist.IsIpAllowed("10.20.0.5")
              && !whitelist.IsIpAllowed("10.20.0.2")
              && !whitelist.IsIpAllowed("10.20.0.3"),
            "native reload did not independently replace both files");
        Check(whitelist.IsNativeWhiteListed("10.20.0.4")
              && !whitelist.IsNativeWhiteListed("10.20.0.5")
              && whitelist.IsNativeGameGateWhiteListed("10.20.0.5")
              && !whitelist.IsNativeGameGateWhiteListed("10.20.0.4"),
            "native whitelist source-specific snapshots");
        Check(whitelist.IsPtidAllowed("ALLOW-PTID")
              && whitelist.IsFastPass("FAST-PTID")
              && whitelist.IsAccountDenied("DENY-PTID"),
            "native reload changed unrelated PTID filters");

        File.Delete(Path.Combine(directory, "WhiteList.txt"));
        Write("GameGateWhiteList.txt", "10.20.0.6\r\n");
        whitelist.ReloadNativeWhiteLists(directory);
        Check(whitelist.IsIpAllowed("10.20.0.4")
              && whitelist.IsIpAllowed("10.20.0.6")
              && !whitelist.IsIpAllowed("10.20.0.5"),
            "missing native whitelist file did not preserve its old table");

        var rawPrefix = gbk.GetBytes(
            " 10.20.0.8 \r\n;RaW\r\n10.20.0.4\r\n10.20.0.4\r\n");
        var rawSuffix = gbk.GetBytes("10.20.0.7\r\n");
        File.WriteAllBytes(Path.Combine(directory, "WhiteList.txt"),
            rawPrefix.Concat(new byte[] { 0 }).Concat(rawSuffix).ToArray());
        whitelist.ReloadNativeWhiteLists(directory);
        Check(whitelist.IsIpAllowed(" 10.20.0.8 ")
              && !whitelist.IsIpAllowed("10.20.0.8")
              && whitelist.IsIpAllowed(";raw")
              && !whitelist.IsIpAllowed("10.20.0.7"),
            "native TStringList raw line/NUL/case semantics");
        var nativeLines = (IEnumerable<string>)typeof(WhitelistService)
            .GetField("_whiteListIps",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(whitelist)!;
        Equal(2, nativeLines.Count(line => line == "10.20.0.4"),
            "native TStringList duplicate preservation");
    }
    finally { Directory.Delete(directory, true); }
}

static void TestNativeType2Initialization()
{
    var gateFrame = NativeType2InitializationProtocol.CreateGameGateSnapshot(
        0x23, new[]
        {
            new NativeGameGateEndpoint("127.0.0.1", 7100),
            new NativeGameGateEndpoint("gate", 7100)
        });
    Check(LegacyDbServerFrameCodec.TryEncode(gateFrame,
        out var gateWire, out var error,
        NativeDbServerProtocol.MaximumFrameLength), error);
    Equal(
        "77BBAA3302000000340000006E0000002300000002000000093132372E302E302E31000000000000BC1B000004676174650000000000000000000000BC1B0000",
        Convert.ToHexString(gateWire),
        "type2 0x006E complete golden wire");

    var iniPath = Path.Combine(Path.GetTempPath(),
        "dbsvr-type2-init-" + Guid.NewGuid().ToString("N") + ".ini");
    try
    {
        File.WriteAllText(iniPath,
            "[GameGates]\r\n"
            + "GameGate1=127.0.0.1:7200\r\n"
            + "GameGate2=host-only\r\n"
            + "GameGate3=skip:0\r\n"
            + "GameGate4=ABCDEFGHIJKLMNOPQ:not-a-port\r\n");
        var endpoints = NativeType2InitializationProtocol.ReadGameGates(
            new ConfigManager(iniPath));
        Equal(31, endpoints.Count, "type2 GameGate positive-port filter/default slots");
        Equal("127.0.0.1", endpoints[0].Host, "type2 GameGate1 host");
        Equal(7200, endpoints[0].Port, "type2 GameGate1 port");
        Equal("host-only", endpoints[1].Host, "type2 host-only value");
        Equal(7100, endpoints[1].Port, "type2 host-only default port");
        Equal(7100, endpoints[2].Port, "type2 invalid port default");
        Equal("127.0.0.1", endpoints[3].Host,
            "type2 missing GameGate slot default host");
        Check(NativeType2InitializationProtocol.TryParseGameGate(
                  string.Empty, out var emptyGate)
              && emptyGate.Host == string.Empty && emptyGate.Port == 7100,
            "type2 explicit empty GameGate value");
        Check(NativeType2InitializationProtocol.TryParseGameGate(
                  "gate:123456", out var fiveDigitGate)
              && fiveDigitGate.Port == 12345,
            "type2 GameGate five-byte port slice");
        Check(NativeType2InitializationProtocol.TryParseGameGate(
                  "gate:0x1BC", out var hexGate)
              && hexGate.Port == 0x1BC,
            "type2 GameGate Delphi hexadecimal port");
        Check(!NativeType2InitializationProtocol.TryParseGameGate(
                "gate:-1", out _),
            "type2 GameGate signed nonpositive port filter");
        Check(NativeType2InitializationProtocol.TryParseGameGate(
                  "first:bad:tail", out var firstColonGate)
              && firstColonGate.Host == "first"
              && firstColonGate.Port == 7100,
            "type2 GameGate first-colon/default parsing");
        var configured = NativeType2InitializationProtocol.CreateGameGateSnapshot(
            5, endpoints);
        Equal((byte)15, configured.Payload[
            NativeType2Protocol.HeaderSize + 2
            * NativeType2InitializationProtocol.GameGateRecordSize],
            "type2 GameGate host ShortString truncation");
    }
    finally { File.Delete(iniPath); }

    static byte[] Cached(ushort command, int length, byte marker)
    {
        var payload = Enumerable.Repeat(marker, length).ToArray();
        BitConverter.TryWriteBytes(payload.AsSpan(0, 2), command);
        return payload;
    }

    static byte[] CachedRanking(int category, int page, byte marker)
    {
        var payload = Cached(0x0069,
            category is >= 4 and <= 7 ? 0x124 : 0xB4, marker);
        BitConverter.TryWriteBytes(payload.AsSpan(4, 4), category);
        BitConverter.TryWriteBytes(payload.AsSpan(8, 4), page);
        return payload;
    }

    var primaryRecords = new[]
    {
        Cached(0x006C, 0x148, 0xA1),
        Cached(0x0065, 0x48, 0xA2)
    };
    var primary = NativeType2InitializationProtocol.CreatePrimaryFrames(
        primaryRecords);
    Check(primary.Select(frame => BitConverter.ToUInt16(frame.Payload, 0))
        .SequenceEqual(new ushort[] { 0x006C, 0x0065, 0x00C8 }),
        "type2 primary cache chain/terminator order");
    Equal(12, primary[^1].Payload.Length,
        "type2 primary terminator payload length");
    Check(primary[^1].Payload.AsSpan(2).ToArray().All(value => value == 0),
        "type2 primary terminator zero fields");

    Equal(0, NativeType2InitializationProtocol.CreateSecondaryFrames(
        true, Array.Empty<byte[]>()).Count,
        "type2 ranking-loading send gate");
    var secondary = NativeType2InitializationProtocol.CreateSecondaryFrames(
        false, new[]
        {
            CachedRanking(4, 0, 0xB1),
            CachedRanking(8, 1, 0xB2)
        });
    Check(secondary.Select(frame => BitConverter.ToUInt16(frame.Payload, 0))
        .SequenceEqual(new ushort[] { 0x0074, 0x0069, 0x0069, 0x0069 }),
        "type2 secondary begin/cache/end order");
    Equal(100, BitConverter.ToInt32(secondary[^1].Payload, 4),
        "type2 secondary end constant");
    Equal(0, BitConverter.ToInt32(secondary[^1].Payload, 8),
        "type2 secondary end param2");

    var cache = new NativeType2InitializationCache();
    var emptySnapshot = cache.Snapshot();
    Equal(2, NativeType2InitializationProtocol.CreateSecondaryFrames(
            emptySnapshot.RankingsLoading, emptySnapshot.Secondary).Count,
        "type2 idle empty ranking boundary frames");
    cache.BeginRankingReload();
    var loadingSnapshot = cache.Snapshot();
    Equal(0, NativeType2InitializationProtocol.CreateSecondaryFrames(
            loadingSnapshot.RankingsLoading, loadingSnapshot.Secondary).Count,
        "type2 active ranking reload suppresses the entire second segment");
    cache.ReplacePrimary(primaryRecords);
    cache.PublishRankings(
        new[] { CachedRanking(8, 0, 0xC1) });
    primaryRecords[0][10] ^= 0xFF;
    var snapshot = cache.Snapshot();
    Equal((byte)0xA1, snapshot.Primary[0][10],
        "type2 initialization cache input clone");
    snapshot.Primary[0][10] ^= 0xFF;
    Equal((byte)0xA1, cache.Snapshot().Primary[0][10],
        "type2 initialization cache output clone");

    var rejected = false;
    try
    {
        NativeType2InitializationProtocol.CreatePrimaryFrames(
            new[] { Cached(0x006C, 0x147, 0) });
    }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "type2 invalid cached record length accepted");
}

static void TestNativeType2MagicDefinitionLayout()
{
    var integers = new Dictionary<string, int>
    {
        ["MagicIdx"] = 0x1234,
        ["EffectType"] = 1,
        ["Effect"] = 2,
        ["Spell"] = 3,
        ["Power"] = 4,
        ["MaxPower"] = 5,
        ["DefSpell"] = 6,
        ["DefPower"] = 7,
        ["DefMaxPower"] = 8,
        ["Job"] = 9,
        ["NeedLv1"] = 11,
        ["NeedLv2"] = 12,
        ["NeedLv3"] = 13,
        ["NeedLv4"] = 14,
        ["NeedLv5"] = 15,
        ["LvTrain1"] = 101,
        ["LvTrain2"] = 102,
        ["LvTrain3"] = 103,
        ["LvTrain4"] = 104,
        ["Delay"] = 17,
        ["ColdMilSec"] = 180,
        ["SpellMilSec"] = 190,
        ["MaxLv"] = 0xEE
    };
    var row = new NativeType2StaticRow(
        new Dictionary<string, byte[]>
        {
            ["MagName"] = Encoding.GetEncoding(936).GetBytes("测试技能")
        }, integers);

    var human = NativeType2StaticRecordBuilder.Build(
        NativeType2StaticRecordBuilder.HumanMagicCommand, row, true);
    Equal(0x48, human.Length, "type2 human magic packet length");
    Equal(NativeType2StaticRecordBuilder.HumanMagicCommand,
        BitConverter.ToUInt16(human, 0), "type2 human magic command");
    Equal(1, BitConverter.ToInt32(human, 8),
        "type2 human magic completion marker");

    var body = NativeType2Protocol.HeaderSize;
    Equal((byte)9, human[body + 0x1A],
        "type2 magic DB job offset");
    for (var i = 1; i <= 5; i++)
        Equal((byte)(10 + i), human[body + 0x1A + i],
            $"type2 magic NeedLv{i} offset");
    for (var i = 1; i <= 4; i++)
        Equal(100 + i, BitConverter.ToInt32(human, body + 0x1C + i * 4),
            $"type2 magic LvTrain{i} offset");
    Equal(170, BitConverter.ToInt32(human, body + 0x30),
        "type2 magic delay scale");
    Equal(180, BitConverter.ToInt32(human, body + 0x34),
        "type2 human magic cold time");
    Equal(190, BitConverter.ToInt32(human, body + 0x38),
        "type2 human magic spell time");

    var hero = NativeType2StaticRecordBuilder.Build(
        NativeType2StaticRecordBuilder.HeroMagicCommand, row, false);
    Equal(0, BitConverter.ToInt32(hero, 8),
        "type2 hero magic non-final marker");
    Equal((byte)9, hero[body + 0x1A],
        "type2 hero magic DB job offset");
    Equal((byte)15, hero[body + 0x1F],
        "type2 hero magic NeedLv5 offset");
    Equal(0, BitConverter.ToInt32(hero, body + 0x34),
        "type2 hero magic cold time must remain zero");
    Equal(0, BitConverter.ToInt32(hero, body + 0x38),
        "type2 hero magic spell time must remain zero");

    NativeType2StaticRow ForceRow(int id)
    {
        var forceIntegers = new Dictionary<string, int>
        {
            ["ForceId"] = id,
            ["MagicIdx"] = 2,
            ["MagKind"] = 3,
            ["Effect"] = 4,
            ["Spell"] = 5,
            ["DefSpell"] = 6,
            ["Power"] = 7,
            ["DefPower"] = 8,
            ["PowerParam"] = 9,
            ["LastLv"] = 10,
            ["Job"] = 11
        };
        for (var i = 1; i <= 5; i++)
        {
            forceIntegers[$"NeedL{i}"] = 20 + i;
            forceIntegers[$"L{i}Train"] = 30 + i;
            forceIntegers[$"L{i}NeedStone"] = 40 + i;
        }
        return new NativeType2StaticRow(
            new Dictionary<string, byte[]> { ["Name"] = new byte[] { 0x41 } },
            forceIntegers);
    }
    var forcePrefix = NativeType2StaticRecordBuilder.BuildRecords(
        NativeType2StaticRecordBuilder.ForceMagicCommand,
        new[] { ForceRow(1), ForceRow(3) });
    Equal(1, forcePrefix.Count, "type2 force-magic continuous prefix count");
    Equal(1, BitConverter.ToInt32(forcePrefix[0], 8),
        "type2 force-magic continuous prefix tail marker");
}

static void TestNativeType2MandatoryMagicRows()
{
    foreach (var tableName in new[] { "humanmagic", "heromagic" })
    {
        var rejected = false;
        try
        {
            MySqlNativeType2StaticLoader.RequireMagicRows(
                tableName, new List<NativeType2StaticRow>());
        }
        catch (InvalidOperationException ex)
        {
            rejected = ex.Message.Contains(tableName,
                StringComparison.OrdinalIgnoreCase);
        }
        Check(rejected, $"type2 empty {tableName} must fail startup");
    }

    NativeType2StaticRow MagicRow(int id)
    {
        var integers = new Dictionary<string, int>
        {
            ["MagicIdx"] = id,
            ["EffectType"] = 1,
            ["Effect"] = 2,
            ["Spell"] = 3,
            ["Power"] = 4,
            ["MaxPower"] = 5,
            ["DefSpell"] = 6,
            ["DefPower"] = 7,
            ["DefMaxPower"] = 8,
            ["Job"] = 0,
            ["NeedLv1"] = 1,
            ["NeedLv2"] = 2,
            ["NeedLv3"] = 3,
            ["NeedLv4"] = 4,
            ["NeedLv5"] = 5,
            ["LvTrain1"] = 10,
            ["LvTrain2"] = 20,
            ["LvTrain3"] = 30,
            ["LvTrain4"] = 40,
            ["Delay"] = 5,
            ["ColdMilSec"] = 60,
            ["SpellMilSec"] = 70
        };
        return new NativeType2StaticRow(
            new Dictionary<string, byte[]>
            {
                ["MagName"] = Encoding.Latin1.GetBytes($"magic{id}")
            }, integers);
    }

    var source = new List<NativeType2StaticRow>
    {
        MagicRow(0), MagicRow(2), MagicRow(9)
    };
    var accepted = MySqlNativeType2StaticLoader.RequireMagicRows(
        "humanmagic", source);
    Check(ReferenceEquals(source, accepted),
        "type2 magic loader must preserve the ordered source rows");

    var records = NativeType2StaticRecordBuilder.BuildRecords(
        NativeType2StaticRecordBuilder.HumanMagicCommand, accepted);
    Equal(3, records.Count, "type2 noncontinuous magic record count");
    var expectedIds = new[] { 0, 2, 9 };
    for (var i = 0; i < records.Count; i++)
    {
        Equal((ushort)expectedIds[i], BitConverter.ToUInt16(records[i],
                NativeType2Protocol.HeaderSize + 0x10),
            $"type2 preserved MagicIdx {expectedIds[i]}");
        Equal(i == records.Count - 1 ? 1 : 0,
            BitConverter.ToInt32(records[i], 8),
            $"type2 magic completion marker {i}");
    }

    var emptyMagic = NativeType2StaticRecordBuilder.BuildRecords(
        NativeType2StaticRecordBuilder.HumanMagicCommand,
        Array.Empty<NativeType2StaticRow>());
    Equal(0, emptyMagic.Count,
        "type2 empty magic must not synthesize a completion record");
    var primary = NativeType2InitializationProtocol.CreatePrimaryFrames(emptyMagic);
    Equal(1, primary.Count, "type2 empty primary contains only C8");
    Equal(NativeType2InitializationProtocol.PrimaryEndCommand,
        BitConverter.ToUInt16(primary[0].Payload, 0),
        "type2 C8 is only the primary segment terminator");
}

static void TestNativeType2Ranking()
{
    var categorySql = typeof(MySqlNativeType2RankingLoader).GetMethod(
        "CategorySql", System.Reflection.BindingFlags.Static
                       | System.Reflection.BindingFlags.NonPublic)!;
    foreach (var category in new[] { 4, 5, 6 })
        Check(((string)categorySql.Invoke(null, new object[] { category })!)
              .Contains("LIMIT 100", StringComparison.OrdinalIgnoreCase),
            $"type2 hero category {category} was truncated below 100 rows");
    Equal(0, NativeType2RankingPacketBuilder.Create(8,
        Array.Empty<NativeType2RankingRow>()).Count,
        "type2 empty ranking packet count");

    var rawName = Enumerable.Range(0x41, 20).Select(value => (byte)value).ToArray();
    var rows = Enumerable.Range(0, 8).Select(index =>
        new NativeType2RankingRow
        {
            Name = rawName,
            Value = unchecked((uint)(0x10203040 + index)),
            SfLevel = 0xFFFFFFFF
        }).ToArray();
    var ordinary = NativeType2RankingPacketBuilder.Create(8, rows);
    Equal(2, ordinary.Count, "type2 eight-row ranking page count");
    Equal(0xB4, ordinary[0].Length, "type2 ordinary ranking payload length");
    Equal((ushort)0x0069, BitConverter.ToUInt16(ordinary[0], 0),
        "type2 ranking packet command");
    Equal(8, BitConverter.ToInt32(ordinary[0], 4),
        "type2 ranking category");
    Equal(0, BitConverter.ToInt32(ordinary[0], 8),
        "type2 first ranking page");
    Equal(1, BitConverter.ToInt32(ordinary[1], 8),
        "type2 second ranking page");
    Equal((byte)15, ordinary[0][NativeType2Protocol.HeaderSize],
        "type2 ranking ShortString truncation");
    Check(ordinary[0].AsSpan(NativeType2Protocol.HeaderSize + 1, 15)
        .SequenceEqual(rawName.AsSpan(0, 15)),
        "type2 ranking raw name identity");
    Equal(unchecked((int)0x10203040), BitConverter.ToInt32(ordinary[0],
        NativeType2Protocol.HeaderSize + 16), "type2 ranking value");
    Equal(0, BitConverter.ToInt32(ordinary[0],
        NativeType2Protocol.HeaderSize + 20),
        "type2 category8 reserved sfLevel");

    var hero = NativeType2RankingPacketBuilder.Create(4,
        new[]
        {
            new NativeType2RankingRow
            {
                Name = rawName,
                HeroName = rawName,
                Level = 0xBEEF,
                SfLevel = 0x88776655
            }
        });
    Equal(1, hero.Count, "type2 hero ranking packet count");
    Equal(0x124, hero[0].Length, "type2 hero ranking payload length");
    var body = NativeType2Protocol.HeaderSize;
    Equal((byte)15, hero[0][body], "type2 hero master-name capacity");
    Equal((byte)14, hero[0][body + 16], "type2 hero-name capacity");
    Equal((ushort)0xBEEF, BitConverter.ToUInt16(hero[0], body + 32),
        "type2 hero ranking level");
    Equal(unchecked((int)0x88776655), BitConverter.ToInt32(hero[0], body + 36),
        "type2 hero ranking sfLevel");

    var invalidCategoryRejected = false;
    try
    {
        NativeType2RankingPacketBuilder.Create(11,
            Array.Empty<NativeType2RankingRow>());
    }
    catch (ArgumentOutOfRangeException) { invalidCategoryRejected = true; }
    Check(invalidCategoryRejected, "type2 hidden ranking category accepted");

    var cache = new NativeType2InitializationCache();
    var loader = new BlockingNativeType2RankingLoader(ordinary);
    var coordinator = new NativeType2RankingReloadCoordinator(cache, loader);
    using var callbackEntered = new ManualResetEventSlim();
    using var releaseCallback = new ManualResetEventSlim();
    coordinator.RankingsPublished += () =>
    {
        callbackEntered.Set();
        releaseCallback.Wait();
    };
    var winners = 0;
    Parallel.For(0, 32, _ =>
    {
        if (coordinator.TryStartReload()) Interlocked.Increment(ref winners);
    });
    Check(loader.Entered.Wait(TimeSpan.FromSeconds(5)),
        "type2 ranking loader did not start");
    Equal(1, winners, "type2 concurrent ranking reload winner count");
    loader.Release.Set();
    Check(callbackEntered.Wait(TimeSpan.FromSeconds(5)),
        "type2 ranking publish callback did not run");
    Check(!coordinator.TryStartReload(),
        "type2 reload restarted inside publish callback window");
    Check(!cache.Snapshot().RankingsLoading,
        "type2 publish callback window left cache loading");
    Equal(1L, cache.Snapshot().RankingGeneration,
        "type2 first ranking generation");
    releaseCallback.Set();
    Check(SpinWait.SpinUntil(() => coordinator.TryStartReload(),
            TimeSpan.FromSeconds(5)),
        "type2 ranking coordinator did not become idle");
    Check(SpinWait.SpinUntil(() => loader.Calls == 2
                                  && cache.Snapshot().RankingGeneration == 2,
            TimeSpan.FromSeconds(5)),
        "type2 second ranking generation was not published");
}

static void TestNativeType3Protocol()
{
    var gbk = Encoding.GetEncoding(936,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    static void WriteInt16(byte[] data, int offset, ushort value) =>
        BitConverter.TryWriteBytes(data.AsSpan(offset, 2), value);
    static void WriteInt32(byte[] data, int offset, int value) =>
        BitConverter.TryWriteBytes(data.AsSpan(offset, 4), value);
    void WriteShortString(byte[] data, int offset, int capacity, string value)
    {
        var bytes = gbk.GetBytes(value);
        Check(bytes.Length <= capacity, "test short string exceeds capacity");
        data[offset] = (byte)bytes.Length;
        bytes.CopyTo(data, offset + 1);
    }

    var queryPayload = new byte[NativeType3Protocol.HeaderSize + 3];
    WriteInt16(queryPayload, 0, NativeType3Protocol.QueryCharactersCommand);
    WriteInt16(queryPayload, 2, 0xBBAA);
    WriteInt32(queryPayload, 4, unchecked((int)0x88776655));
    WriteShortString(queryPayload, 0x08, 32, "ROUTE");
    WriteShortString(queryPayload, 0x29, 20, "PT001");
    queryPayload[0x0E] = 0xCC;
    queryPayload[0x2F] = 0xDD;
    WriteInt16(queryPayload, 0x3E, 0xBEEF);
    queryPayload[0x40] = 0xDE;
    queryPayload[0x41] = 0xAD;
    queryPayload[0x42] = 0xBE;
    var originalQueryPayload = queryPayload.ToArray();
    Check(NativeType3Protocol.TryDecodeQuery(
        new LegacyDbServerFrame(3, 0xCAFE, queryPayload),
        out var query, out var error), error);
    Equal("ROUTE", query.Route, "type3 route token");
    Equal("PT001", query.Ptid, "type3 PTID");
    Check(queryPayload.SequenceEqual(originalQueryPayload),
        "type3 query decoder modified input");

    var characters = new NativeType3Character[]
    {
        new()
        {
            UserId = unchecked((long)0x1122334455667788UL),
            CharacterName = "甲一", Level = 0x1234, Sex = 2, Job = 0
        },
        new()
        {
            UserId = unchecked((long)0xFEDCBA9876543210UL),
            CharacterName = "ABCD", Level = 0xFEDC, Sex = 0xFF, Job = 3
        }
    };
    Check(NativeType3Protocol.TryCreateQueryResponse(
        query, characters, out var response, out error), error);
    Check(LegacyDbServerFrameCodec.TryEncode(
        response, out var wire, out error,
        NativeDbServerProtocol.MaximumFrameLength), error);

    var expectedPayload = new byte[NativeType3Protocol.HeaderSize
                                   + 2 * NativeType3Protocol.CharacterEntrySize];
    WriteInt16(expectedPayload, 0,
        NativeType3Protocol.QueryCharactersResponseCommand);
    WriteInt32(expectedPayload, 4, 2);
    WriteShortString(expectedPayload, 0x08, 32, "ROUTE");
    WriteShortString(expectedPayload, 0x29, 20, "PT001");
    var first = NativeType3Protocol.HeaderSize;
    WriteInt32(expectedPayload, first,
        unchecked((int)0x11223344));
    WriteInt32(expectedPayload, first + 4,
        unchecked((int)0x55667788));
    WriteShortString(expectedPayload, first + 0x08, 15, "甲一");
    WriteInt16(expectedPayload, first + 0x18, 0x1234);
    expectedPayload[first + 0x1A] = 2;
    WriteShortString(expectedPayload, first + 0x1B, 4, "战士");
    var second = first + NativeType3Protocol.CharacterEntrySize;
    WriteInt32(expectedPayload, second,
        unchecked((int)0xFEDCBA98));
    WriteInt32(expectedPayload, second + 4,
        unchecked((int)0x76543210));
    WriteShortString(expectedPayload, second + 0x08, 15, "ABCD");
    WriteInt16(expectedPayload, second + 0x18, 0xFEDC);
    expectedPayload[second + 0x1A] = 0xFF;
    WriteShortString(expectedPayload, second + 0x1B, 4, "刺客");

    var expectedWire = new byte[LegacyDbServerFrameCodec.HeaderSize
                                + expectedPayload.Length];
    WriteInt32(expectedWire, 0,
        unchecked((int)LegacyDbServerFrameCodec.FrameMagic));
    WriteInt16(expectedWire, 4, 3);
    WriteInt16(expectedWire, 6, 0);
    WriteInt32(expectedWire, 8, expectedPayload.Length);
    expectedPayload.CopyTo(expectedWire, LegacyDbServerFrameCodec.HeaderSize);
    Check(wire.SequenceEqual(expectedWire),
        "type3 complete response wire bytes");

    Check(NativeType3Protocol.TryCreateQueryResponse(
        query, Array.Empty<NativeType3Character>(),
        out var emptyResponse, out error), error);
    Equal(NativeType3Protocol.HeaderSize, emptyResponse.Payload.Length,
        "type3 empty response payload length");
    Equal(0, BitConverter.ToInt32(emptyResponse.Payload, 4),
        "type3 empty response count");

    var peerGroups = new byte[] { 0, 1, 2, 2, 9 };
    Check(peerGroups.Where(peer =>
            NativeType3Protocol.ShouldBroadcastResponse(0, peer))
        .SequenceEqual(new byte[] { 0, 1, 2, 2 }),
        "type3 group-zero recipients");
    Check(peerGroups.Where(peer =>
            NativeType3Protocol.ShouldBroadcastResponse(2, peer))
        .SequenceEqual(new byte[] { 2, 2 }),
        "type3 same-group eligibility");
    Check(NativeType3Protocol.SelectBroadcastTargets(
            (byte)2, peerGroups, peer => peer)
        .SequenceEqual(new byte[] { 2 }),
        "type3 nonzero group must select only first target");
    Check(NativeType3Protocol.SelectBroadcastTargets(
            (byte)9, peerGroups, peer => peer)
        .SequenceEqual(new byte[] { 9 }),
        "type3 group nine must select first group-nine target");
    Equal(180001000000123L,
        NativeType3Protocol.CreateFallbackUserId(180, 1, 123),
        "type3 UserId fallback formula");
    Equal("pt001-abc", NativeType3Protocol.NormalizePtidKey("PT001-AbC"),
        "type3 PTID ASCII lowercase key");
    Equal("70743030312D81616263", NativeType3Protocol.NormalizePtidKey(
            Convert.FromHexString("50543030312D81416243")),
        "type3 raw PTID ASCII lowercase key");
    Equal(1, NativeType3Protocol.NormalizeDeleteState(257),
        "type3 IsDelete low-byte truncation");
    Equal(1, NativeType3Protocol.NormalizeDeleteState(-255),
        "type3 negative IsDelete low-byte truncation");

    Check(!NativeType3Protocol.TryDecodeQuery(
        new LegacyDbServerFrame(2, 0, queryPayload), out _, out _),
        "type3 query accepted wrong outer type");
    Check(!NativeType3Protocol.TryDecodeQuery(
        new LegacyDbServerFrame(3, 0, new byte[0x3F]), out _, out _),
        "type3 query accepted short payload");
    var malformed = originalQueryPayload.ToArray();
    malformed[0x08] = 33;
    Check(NativeType3Protocol.TryDecodeQuery(
        new LegacyDbServerFrame(3, 0, malformed),
        out var oversizedRoute, out error), error);
    Check(NativeType3Protocol.TryCreateQueryResponse(
        oversizedRoute, Array.Empty<NativeType3Character>(),
        out var truncatedRouteResponse, out error), error);
    Equal((byte)32, truncatedRouteResponse.Payload[0x08],
        "type3 oversized route response truncation");

    var invalidAnsi = originalQueryPayload.ToArray();
    invalidAnsi[0x08] = 2;
    invalidAnsi[0x09] = 0x81;
    invalidAnsi[0x0A] = 0x30;
    Check(NativeType3Protocol.TryDecodeQuery(
        new LegacyDbServerFrame(3, 0, invalidAnsi), out var invalidQuery, out error),
        "type3 query rejected raw invalid CP936 bytes: " + error);
    Check(NativeType3Protocol.TryCreateQueryResponse(
        invalidQuery, Array.Empty<NativeType3Character>(),
        out var invalidAnsiResponse, out error), error);
    Check(invalidAnsiResponse.Payload.AsSpan(0x08, 3)
        .SequenceEqual(new byte[] { 2, 0x81, 0x30 }),
        "type3 raw ANSI route bytes were not preserved semantically");

    Check(NativeType3Protocol.TryCreateQueryResponse(query,
        new[] { new NativeType3Character { CharacterName = "ABCDEFGHIJKLMNOP" } },
        out var longNameResponse, out error), error);
    Equal((byte)15, longNameResponse.Payload[
        NativeType3Protocol.HeaderSize + 0x08],
        "type3 long character name truncation");
    Check(NativeType3Protocol.TryCreateQueryResponse(query,
        new[]
        {
            new NativeType3Character
            {
                CharacterName = "ignored",
                CharacterNameBytes = Convert.FromHexString("81414243")
            }
        }, out var rawNameResponse, out error), error);
    Check(rawNameResponse.Payload.AsSpan(
            NativeType3Protocol.HeaderSize + 0x08, 5)
        .SequenceEqual(Convert.FromHexString("0481414243")),
        "type3 response did not preserve raw DB character bytes");
}

static void TestNativeForceLevel()
{
    var payload = Enumerable.Repeat((byte)0xCC,
        NativeForceLevelProtocol.PayloadSize).ToArray();
    BitConverter.TryWriteBytes(payload.AsSpan(0, 2),
        NativeForceLevelProtocol.RequestCommand);
    BitConverter.TryWriteBytes(payload.AsSpan(4, 4),
        unchecked((int)0x12345678));
    payload[0x10] = 3;
    Convert.FromHexString("814142").CopyTo(payload, 0x11);
    payload[0x25] = 4;
    Convert.FromHexString("81414243").CopyTo(payload, 0x26);
    var original = payload.ToArray();
    Check(NativeForceLevelProtocol.TryDecodeRequest(
        new LegacyDbServerFrame(1, 0xBEEF, payload),
        out var request, out var error), error);
    Equal(unchecked((int)0x12345678), request.Value,
        "native 0x0168 full request value");
    Check(request.AccountBytes.SequenceEqual(Convert.FromHexString("814142"))
          && request.CharacterNameBytes.SequenceEqual(
              Convert.FromHexString("81414243")),
        "native 0x0168 raw request ShortStrings");
    Check(payload.SequenceEqual(original),
        "native 0x0168 decoder modified input");

    var response = NativeForceLevelProtocol.CreateResponse(
        request, unchecked((int)0x88776655));
    Equal((ushort)1, response.Type, "native 0x0168 response outer type");
    Equal(NativeForceLevelProtocol.PayloadSize, response.Payload.Length,
        "native 0x0168 response payload size");
    var expectedPayload = new byte[NativeForceLevelProtocol.PayloadSize];
    BitConverter.TryWriteBytes(expectedPayload.AsSpan(0, 2),
        NativeForceLevelProtocol.ResponseCommand);
    BitConverter.TryWriteBytes(expectedPayload.AsSpan(4, 4),
        unchecked((int)0x88776655));
    expectedPayload[0x10] = 3;
    request.AccountBytes.CopyTo(expectedPayload, 0x11);
    expectedPayload[0x25] = 4;
    request.CharacterNameBytes.CopyTo(expectedPayload, 0x26);
    Check(response.Payload.SequenceEqual(expectedPayload),
        "native 0x0168 fixed response bytes/zero holes");
    Check(LegacyDbServerFrameCodec.TryEncode(response,
        out var responseWire, out error,
        NativeDbServerProtocol.MaximumFrameLength), error);
    Equal(0x54, responseWire.Length, "native 0x0168 total response size");

    var oversized = new byte[0x60];
    BitConverter.TryWriteBytes(oversized.AsSpan(0, 2),
        NativeForceLevelProtocol.RequestCommand);
    oversized[0x10] = 30;
    Enumerable.Range(0, 30).Select(i => (byte)(0x40 + i)).ToArray()
        .CopyTo(oversized, 0x11);
    oversized[0x25] = 20;
    Enumerable.Range(0, 20).Select(i => (byte)(0x70 + i)).ToArray()
        .CopyTo(oversized, 0x26);
    Check(NativeForceLevelProtocol.TryDecodeRequest(
        new LegacyDbServerFrame(1, 0, oversized),
        out var oversizedRequest, out error), error);
    Equal(30, oversizedRequest.AccountBytes.Length,
        "native 0x0168 request account slot cap was incorrectly enforced");
    Equal(20, oversizedRequest.CharacterNameBytes.Length,
        "native 0x0168 request name slot cap was incorrectly enforced");
    var truncatedResponse = NativeForceLevelProtocol.CreateResponse(
        oversizedRequest, 1);
    Equal((byte)20, truncatedResponse.Payload[0x10],
        "native 0x0168 response account truncation");
    Equal((byte)15, truncatedResponse.Payload[0x25],
        "native 0x0168 response name truncation");
    var malformed = new byte[NativeForceLevelProtocol.PayloadSize];
    BitConverter.TryWriteBytes(malformed.AsSpan(0, 2),
        NativeForceLevelProtocol.RequestCommand);
    malformed[0x25] = 0x30;
    Check(!NativeForceLevelProtocol.TryDecodeRequest(
        new LegacyDbServerFrame(1, 0, malformed), out _, out _),
        "native 0x0168 truncated raw ShortString accepted");

    var store = new FakeNativeForceLevelStore();
    var service = new NativeForceLevelService(store);
    store.Player.Enqueue(NativeForceLevelStoreResult.Deleted);
    Equal(NativeForceLevelProtocol.DeletedResult, service.Apply(request),
        "native 0x0168 deleted player result");
    Equal(0, store.HeroCalls, "native 0x0168 deleted player fell back to hero");

    store.Player.Enqueue(NativeForceLevelStoreResult.LoadFailed);
    Equal(NativeForceLevelProtocol.LoadFailedResult, service.Apply(request),
        "native 0x0168 player load-failed result");
    Equal(0, store.HeroCalls, "native 0x0168 load failure fell back to hero");

    store.Player.Enqueue(NativeForceLevelStoreResult.Queued);
    Equal(unchecked((int)0x12345678), service.Apply(request),
        "native 0x0168 player success full result");
    Equal((ushort)0x5678, store.LastForceLevel,
        "native 0x0168 low-word ForceLv mutation");
    Equal("81414243",
        NativeForceLevelProtocol.NormalizeCharacterNameKey(
            Convert.FromHexString("81614243")),
        "native 0x0168 raw name ASCII-only normalization");

    var collision = new NativeForceLevelRequest
    {
        Value = NativeForceLevelProtocol.MissingResult,
        CharacterNameBytes = Convert.FromHexString("41424344")
    };
    store.Player.Enqueue(NativeForceLevelStoreResult.Queued);
    store.Hero.Enqueue(NativeForceLevelStoreResult.Queued);
    var heroCalls = store.HeroCalls;
    var collisionResult = service.ApplyDetailed(collision);
    Equal(NativeForceLevelProtocol.MissingResult, collisionResult.Result,
        "native 0x0168 result collision value");
    Equal(2, collisionResult.Mutations.Count,
        "native 0x0168 result collision lost player/hero mutations");
    Equal(heroCalls + 1, store.HeroCalls,
        "native 0x0168 player success value 100001 did not fall back");

    store.Player.Enqueue(NativeForceLevelStoreResult.UpdatedWithoutSaveTarget);
    store.Hero.Enqueue(NativeForceLevelStoreResult.Missing);
    Equal(NativeForceLevelProtocol.MissingResult, service.Apply(request),
        "native 0x0168 no-save-target fallback result");

    var playerMutation = new NativeForceLevelMutation
    {
        Target = NativeForceLevelTarget.Player,
        Index = 41,
        ForceLevel = 0x1234,
        CharacterNameBytes = Convert.FromHexString("8141")
    };
    var playerFull = new NativeSavePersistenceData
    {
        Account = "account",
        CharacterName = "character"
    };
    var playerMerged = new GameSocService.NativeSaveWorkItem(playerMutation);
    playerMerged.ReplaceWith(new GameSocService.NativeSaveWorkItem(41, playerFull));
    Check(ReferenceEquals(playerFull, playerMerged.Persistence)
          && playerMerged.ForceLevel == 0x1234,
        "native player 0x168 then 0x150 merge");
    var playerReverse = new GameSocService.NativeSaveWorkItem(41, playerFull);
    playerReverse.ReplaceWith(new GameSocService.NativeSaveWorkItem(playerMutation));
    Check(ReferenceEquals(playerFull, playerReverse.Persistence)
          && playerReverse.ForceLevel == 0x1234,
        "native player 0x150 then 0x168 merge");

    var heroRecord = Enumerable.Repeat((byte)0x2A,
        NativeHeroDbFrameCodec.HeroRecordSize).ToArray();
    var heroFull = new GameSocService.NativeHeroSaveWorkItem(
        52, "hero", heroRecord, heroRecord, Array.Empty<byte>(),
        0, 0, 0, false, 1, 0, 1, 0x1111);
    var heroMutation = new NativeForceLevelMutation
    {
        Target = NativeForceLevelTarget.Hero,
        Index = 52,
        ForceLevel = 0x2222,
        CharacterNameBytes = Convert.FromHexString("8142")
    };
    var heroForceThenFull =
        GameSocService.NativeHeroSaveWorkItem.ForForceLevel(heroMutation);
    heroForceThenFull.ReplaceWith(heroFull);
    Check(heroForceThenFull.HasRecord
          && heroForceThenFull.ForceLevelOverride == 0x1111,
        "native hero 0x168 then 0x161 did not let the full record win");
    var heroFullThenForce = new GameSocService.NativeHeroSaveWorkItem(
        52, "hero", heroRecord, heroRecord, Array.Empty<byte>(),
        0, 0, 0, false, 1, 0, 1, 0x1111);
    heroFullThenForce.ReplaceWith(
        GameSocService.NativeHeroSaveWorkItem.ForForceLevel(heroMutation));
    Check(heroFullThenForce.HasRecord
          && heroFullThenForce.Record.SequenceEqual(heroRecord)
          && heroFullThenForce.ForceLevelOverride == 0x2222,
        "native hero 0x161 then 0x168 lost the full record or ForceLv");
    var pendingWithDynamic = new GameSocService.NativeHeroSaveWorkItem(
        52, "old-name", heroRecord, heroRecord, new byte[] { 1, 2, 3 },
        0, 0, 0, false, 1, 0, 1, 0x1111);
    var emptyDynamicUpdate = new GameSocService.NativeHeroSaveWorkItem(
        52, "old-name", heroRecord, heroRecord, Array.Empty<byte>(),
        0, 0, 0, true, 1, 0, 1, 0x1111);
    pendingWithDynamic.ReplaceWith(emptyDynamicUpdate);
    Check(pendingWithDynamic.DynamicData.Length == 0
          && pendingWithDynamic.IsDelete,
        "native hero pending merge did not clear the old attachment");

    var threeRecords = Enumerable.Repeat((byte)0xCC,
        NativeHeroBlobCodec.ThreeHeroRecordSize).ToArray();
    Check(NativeHeroBlobCodec.TryApplyIndexForceLevel(
        threeRecords, 0xBEEF, out var forcedRecords, out error), error);
    Check(threeRecords.All(value => value == 0xCC),
        "native hero ForceLv patch modified its input");
    for (var slot = 0; slot < 3; slot++)
        Equal((ushort)0xBEEF, BitConverter.ToUInt16(forcedRecords,
                slot * NativeHeroDbFrameCodec.HeroRecordSize
                + NativeHeroDbFrameCodec.IndexForceLvOffset),
            "native hero ForceLv did not update every three-slot record");

    var logicalRecord = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    var logicalData = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    var logicalCache = new NativeHeroLogicalCache();
    logicalCache.Set(new NativeHeroLogicalSnapshot(52, "master", "hero",
        logicalRecord, logicalData, Array.Empty<byte>(), false, 2, 0,
        byte.MaxValue, 1, 0, 0, 0x1111, 0, 0));
    var fallbackLoads = 0;
    var overrideIndex = -1;
    ushort overrideForce = 0;
    var playerRecords = InterfaceProxy.Create<IPlayRecordService>(
        (method, _) => InterfaceProxy.DefaultValue(method.ReturnType));
    var heroRecords = InterfaceProxy.Create<IHeroRecordService>(
        (method, arguments) =>
        {
            if (method.Name == nameof(IHeroRecordService
                    .TryGetNativeForceLevelIndex))
            {
                arguments![1] = 52;
                return true;
            }
            return InterfaceProxy.DefaultValue(method.ReturnType);
        });
    var heroData = InterfaceProxy.Create<IHeroDataService>(
        (method, arguments) =>
        {
            if (method.Name == nameof(IHeroDataService
                    .ApplyNativeForceLevel))
            {
                fallbackLoads++;
                return new NativeForceLevelStoreAttempt(
                    NativeForceLevelStoreResult.LoadFailed);
            }
            if (method.Name == nameof(IHeroDataService
                    .SetNativeForceLevelOverride))
            {
                overrideIndex = (int)arguments![0]!;
                overrideForce = (ushort)arguments[1]!;
                return null;
            }
            return InterfaceProxy.DefaultValue(method.ReturnType);
        });
    var productionStore = new NativeForceLevelStore(playerRecords,
        heroRecords, heroData, logicalCache);
    var cachedAttempt = productionStore.ApplyHero(
        Convert.FromHexString("814142"), 0xCAFE);
    Equal(NativeForceLevelStoreResult.Queued, cachedAttempt.Result,
        "native 0x0168 cached hero result");
    Equal(0, fallbackLoads,
        "native 0x0168 cached hero reloaded stale database Data");
    Equal(52, overrideIndex, "native 0x0168 cached override index");
    Equal((ushort)0xCAFE, overrideForce,
        "native 0x0168 cached override ForceLv");
    Check(cachedAttempt.Mutation?.Target == NativeForceLevelTarget.Hero
          && cachedAttempt.Mutation.Index == 52
          && cachedAttempt.Mutation.CharacterNameBytes.SequenceEqual(
              Convert.FromHexString("814142")),
        "native 0x0168 cached hero mutation");
    Check(logicalCache.TryGet(52, out var cachedSnapshot),
        "native 0x0168 cached hero snapshot disappeared");
    for (var slot = 0; slot < 3; slot++)
        Equal((ushort)0xCAFE, BitConverter.ToUInt16(cachedSnapshot.Data,
                slot * NativeHeroDbFrameCodec.HeroRecordSize
                + NativeHeroDbFrameCodec.IndexForceLvOffset),
            "native 0x0168 store did not update every cached hero slot");
}

static void TestNativeType1SilentCommand()
{
    Check(NativeDbServerProtocol.UsesNormalType1Dispatcher(0)
          && NativeDbServerProtocol.UsesNormalType1Dispatcher(8)
          && !NativeDbServerProtocol.UsesNormalType1Dispatcher(9),
        "server type nine entered the normal type1 dispatcher");
    Check(NativeDbServerProtocol.IsDbToolType1Command(0x0100)
          && NativeDbServerProtocol.IsDbToolType1Command(0x0104)
          && !NativeDbServerProtocol.IsDbToolType1Command(0x00FF)
          && !NativeDbServerProtocol.IsDbToolType1Command(0x0150),
        "DB tool type1 command range");
    Check(NativeDbServerProtocol.IsSilentNormalType1Command(0x0155, 0),
        "normal type1 0x0155 was not silent for server type zero");
    Check(NativeDbServerProtocol.IsSilentNormalType1Command(0x0155, 8),
        "normal type1 0x0155 was not silent for normal server type");
    Check(!NativeDbServerProtocol.IsSilentNormalType1Command(0x0155, 9),
        "server type nine incorrectly entered normal 0x0155 no-op");
    Check(!NativeDbServerProtocol.IsSilentNormalType1Command(0x0154, 0),
        "unknown type1 command incorrectly treated as 0x0155 no-op");
    var verifiedDefaultNoOps = new ushort[]
    {
        0x0158, 0x015C, 0x015D, 0x015E, 0x015F,
        0x0169, 0x016D, 0x016E, 0x016F,
        0x0171, 0x0175,
        0x0177, 0x0178, 0x0179, 0x017A, 0x017B,
        0x017C, 0x017D, 0x017E, 0x017F, 0x0180,
        0x0184, 0x0185, 0x0186, 0x0187, 0x0188,
        0x0189, 0x018A, 0x018B, 0x018C, 0x018D,
        0x018E, 0x018F, 0x0190, 0x0191,
        0x0195, 0x0196, 0x0197, 0x0198, 0x0199,
        0x019F
    };
    foreach (var command in verifiedDefaultNoOps)
        Check(NativeDbServerProtocol.IsSilentNormalType1Command(command, 0),
            $"verified Type1 default no-op 0x{command:X4} was not silent");

    // These commands have concrete targets in the original Type1 dispatch table.
    // Some still depend on unavailable external state, but none may be collapsed
    // into the dispatcher's default/no-op path.
    var verifiedActiveCommands = new ushort[]
    {
        0x0045,
        0x0150, 0x0151, 0x0152, 0x0153, 0x0154, 0x0156, 0x0157,
        0x0159, 0x015A, 0x015B,
        0x0160, 0x0161, 0x0162, 0x0163, 0x0164, 0x0165, 0x0166,
        0x0167, 0x0168, 0x016A, 0x016B, 0x016C, 0x0170, 0x0172,
        0x0173, 0x0174, 0x0176,
        0x0181, 0x0182, 0x0183,
        0x0192, 0x0193, 0x0194, 0x019A, 0x019B, 0x019C, 0x019D,
        0x019E
    };
    foreach (var command in verifiedActiveCommands)
        Check(!NativeDbServerProtocol.IsSilentNormalType1Command(command, 0),
            $"active Type1 command 0x{command:X4} incorrectly treated as a default no-op");
    Check(!NativeDbServerProtocol.IsSilentNormalType1Command(0x0180, 9),
        "server type nine incorrectly entered a normal Type1 default no-op");
}

static void TestNativeDbToolReads()
{
    var humanName = Enumerable.Range(1, 15).Select(value => (byte)value)
        .ToArray();
    var humanPayload = new byte[NativeDbToolProtocol.HeaderSize];
    BitConverter.GetBytes(NativeDbToolProtocol.HumanReadCommand)
        .CopyTo(humanPayload, 0);
    humanPayload[0x25] = (byte)humanName.Length;
    humanName.CopyTo(humanPayload, 0x26);
    Check(NativeDbToolProtocol.TryDecodeHumanRead(
        new LegacyDbServerFrame(1, 0xA55A, humanPayload), out var humanRequest,
        out var error), error);
    Equal(NativeDbToolProtocol.HumanReadCommand, humanRequest.Command,
        "DB-tool 0102 command");
    Check(humanRequest.NameBytes.SequenceEqual(humanName),
        "DB-tool 0102 SS15 name");
    humanPayload[0x26] ^= 0xFF;
    Check(humanRequest.NameBytes.SequenceEqual(humanName),
        "DB-tool 0102 retained its input payload");

    var emptyNamePayload = new byte[NativeDbToolProtocol.HeaderSize + 3];
    BitConverter.GetBytes(NativeDbToolProtocol.HumanReadCommand)
        .CopyTo(emptyNamePayload, 0);
    emptyNamePayload[0x10] = 0xFF;
    Check(NativeDbToolProtocol.TryDecodeHumanRead(
            new LegacyDbServerFrame(1, 0, emptyNamePayload),
            out var emptyNameRequest, out error), error);
    Equal(0, emptyNameRequest.NameBytes.Length,
        "DB-tool 0102 empty name length");
    Check(!NativeDbToolProtocol.TryDecodeHumanRead(
            new LegacyDbServerFrame(2, 0, humanPayload), out _, out _),
        "DB-tool 0102 accepted the wrong outer type");
    Check(!NativeDbToolProtocol.TryDecodeHumanRead(
            new LegacyDbServerFrame(1, 0,
                new byte[NativeDbToolProtocol.HeaderSize - 1]), out _, out _),
        "DB-tool 0102 accepted a truncated header");

    var heroName = Convert.FromHexString("814182428343844485458646874788");
    var heroPayload = new byte[NativeDbToolProtocol.HeaderSize];
    BitConverter.GetBytes(NativeDbToolProtocol.HeroReadCommand)
        .CopyTo(heroPayload, 0);
    heroPayload[0x25] = (byte)heroName.Length;
    heroName.CopyTo(heroPayload, 0x26);
    Check(NativeDbToolProtocol.TryDecodeHeroRead(
        new LegacyDbServerFrame(1, 0, heroPayload), out var heroRequest,
        out error), error);
    Equal(NativeDbToolProtocol.HeroReadCommand, heroRequest.Command,
        "DB-tool 0104 command");
    Check(heroRequest.NameBytes.SequenceEqual(heroName),
        "DB-tool 0104 raw name");
    Check(!NativeDbToolProtocol.TryDecodeHumanRead(
            new LegacyDbServerFrame(1, 0, heroPayload), out _, out _),
        "DB-tool 0102 accepted the 0104 command");

    var overlongPayload = (byte[])heroPayload.Clone();
    overlongPayload[0x25] = 16;
    Check(!NativeDbToolProtocol.TryDecodeHeroRead(
            new LegacyDbServerFrame(1, 0, overlongPayload), out _, out _),
        "DB-tool 0104 accepted an SS15 length of 16");
    var overlongRequest = new NativeDbToolReadRequest
    {
        Command = NativeDbToolProtocol.HeroReadCommand,
        NameBytes = new byte[16]
    };
    Check(!NativeDbToolProtocol.TryCreateReadSuccess(overlongRequest,
            Array.Empty<byte>(), new byte[] { 1 }, Array.Empty<byte>(),
            out _, out _),
        "DB-tool response accepted an SS15 name of 16 bytes");

    var owner = Convert.FromHexString("A1A2A3A4");
    var primary = Convert.FromHexString("102030");
    var trailing = Convert.FromHexString("40506070");
    Check(NativeDbToolProtocol.TryCreateReadSuccess(humanRequest, owner,
        primary, trailing, out var success, out error), error);
    Equal((ushort)1, success.Type, "DB-tool success outer type");
    Equal((ushort)0, success.Reserved, "DB-tool success reserved word");
    Equal(NativeDbToolProtocol.ResponseCommand,
        BitConverter.ToUInt16(success.Payload, 0),
        "DB-tool success command");
    Equal((ushort)1, BitConverter.ToUInt16(success.Payload, 2),
        "DB-tool success result");
    Equal((int)NativeDbToolProtocol.HumanReadCommand,
        BitConverter.ToInt32(success.Payload, 4),
        "DB-tool success source command");
    Equal((byte)owner.Length, success.Payload[0x10],
        "DB-tool success owner length");
    Check(success.Payload.AsSpan(0x11, owner.Length).SequenceEqual(owner),
        "DB-tool success owner bytes");
    Equal((byte)humanName.Length, success.Payload[0x25],
        "DB-tool success name length");
    Check(success.Payload.AsSpan(0x26, humanName.Length)
            .SequenceEqual(humanName),
        "DB-tool success name bytes");
    Check(success.Payload.AsSpan(NativeDbToolProtocol.HeaderSize)
            .SequenceEqual(primary.Concat(trailing).ToArray()),
        "DB-tool success did not concatenate both body segments");

    var successCopy = (byte[])success.Payload.Clone();
    owner[0] ^= 0xFF;
    primary[0] ^= 0xFF;
    trailing[0] ^= 0xFF;
    humanRequest.NameBytes[0] ^= 0xFF;
    Check(success.Payload.SequenceEqual(successCopy),
        "DB-tool success retained an input array");

    var failure = NativeDbToolProtocol.CreateReadFailure(heroRequest);
    Equal((ushort)1, failure.Type, "DB-tool failure outer type");
    Equal((ushort)0, failure.Reserved, "DB-tool failure reserved word");
    Equal(NativeDbToolProtocol.HeaderSize, failure.Payload.Length,
        "DB-tool failure payload length");
    Equal(NativeDbToolProtocol.ResponseCommand,
        BitConverter.ToUInt16(failure.Payload, 0),
        "DB-tool failure command");
    Equal((ushort)0, BitConverter.ToUInt16(failure.Payload, 2),
        "DB-tool default failure result");
    Equal((byte)heroName.Length, failure.Payload[0x25],
        "DB-tool failure name length");
    Check(failure.Payload.AsSpan(0x26, heroName.Length)
            .SequenceEqual(heroName),
        "DB-tool failure name echo");
    var expectedFailure = new byte[NativeDbToolProtocol.HeaderSize];
    BitConverter.GetBytes(NativeDbToolProtocol.ResponseCommand)
        .CopyTo(expectedFailure, 0);
    BitConverter.GetBytes((int)NativeDbToolProtocol.HeroReadCommand)
        .CopyTo(expectedFailure, 4);
    expectedFailure[0x25] = (byte)heroName.Length;
    heroName.CopyTo(expectedFailure, 0x26);
    Check(failure.Payload.SequenceEqual(expectedFailure),
        "DB-tool failure reserved bytes were not zero");
    Check(LegacyDbServerFrameCodec.TryEncode(failure,
        out var failureWire, out error), error);
    Equal(0x54, failureWire.Length, "DB-tool failure wire length");
    var maximumBodyLength = NativeDbServerProtocol.MaximumFrameLength
                            - LegacyDbServerFrameCodec.HeaderSize
                            - NativeDbToolProtocol.HeaderSize;
    var maximumBody = new byte[maximumBodyLength];
    Check(NativeDbToolProtocol.TryCreateReadSuccess(heroRequest,
        Array.Empty<byte>(), maximumBody, Array.Empty<byte>(),
        out var maximum, out error), error);
    Equal(NativeDbServerProtocol.MaximumFrameLength,
        LegacyDbServerFrameCodec.HeaderSize + maximum.Payload.Length,
        "DB-tool maximum response frame length");
    Check(!NativeDbToolProtocol.TryCreateReadSuccess(heroRequest,
            Array.Empty<byte>(), maximumBody, new byte[1], out _, out _),
        "DB-tool response exceeded the maximum frame length");

    var nativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
    var nativeScriptData = Convert.FromHexString("DEADBEEF0102");
    Check(NativeHumanLogicalCache.TryCreatePersistence("account", "hero",
        nativeHumanData, nativeScriptData, out var humanPersistence,
        out error), error);
    var humanCache = new NativeHumanLogicalCache();
    Check(humanCache.TryStage(31, humanPersistence, _ => true),
        "DB-tool human snapshot stage");
    var staleHumanLoads = 0;
    var cachedHuman = humanCache.GetOrLoad(31, () =>
    {
        staleHumanLoads++;
        return null;
    });
    Equal(0, staleHumanLoads,
        "DB-tool 0102 reloaded stale human persistence");
    Check(NativeDbToolProtocol.TryCreateReadSuccess(humanRequest,
        Encoding.ASCII.GetBytes("account"), cachedHuman.DataBlob,
        cachedHuman.ScriptDataBlob, out var humanSuccess, out error), error);
    var humanBody = humanSuccess.Payload.AsSpan(
        NativeDbToolProtocol.HeaderSize);
    Check(humanBody.Slice(0, cachedHuman.DataBlob.Length)
            .SequenceEqual(cachedHuman.DataBlob)
          && humanBody.Slice(cachedHuman.DataBlob.Length)
            .SequenceEqual(cachedHuman.ScriptDataBlob),
        "DB-tool 0102 did not preserve DataBlob/ScriptDataBlob envelopes");
    Equal(NativeHumanDataCodec.DataSizeMarker,
        cachedHuman.DataBlob.Length, "DB-tool 0102 DataBlob boundary");

    var cacheRecord = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    var cacheData = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    var cacheDynamic = new byte[] { 0x33, 0x34 };
    cacheRecord[0] = 0x11;
    cacheData[0] = 0x22;
    var loadedSnapshot = new NativeHeroLogicalSnapshot(91, "master", "hero",
        cacheRecord, cacheData, cacheDynamic, false, 1, 0, 0, 1, 0, 0,
        0, 0, 0);
    var loaderCalls = 0;
    NativeHeroLogicalSnapshot LoadSnapshot()
    {
        loaderCalls++;
        return loadedSnapshot;
    }

    var heroCache = new NativeHeroLogicalCache();
    var firstLoad = heroCache.GetOrLoad(91, LoadSnapshot);
    loadedSnapshot.Record[0] = 0x44;
    firstLoad.Record[0] = 0x55;
    firstLoad.Data[0] = 0x66;
    firstLoad.DynamicData[0] = 0x77;
    var secondLoad = heroCache.GetOrLoad(91, LoadSnapshot);
    Equal(1, loaderCalls, "hero logical cache loader call count");
    Check(!ReferenceEquals(loadedSnapshot, firstLoad)
          && !ReferenceEquals(firstLoad, secondLoad)
          && !ReferenceEquals(firstLoad.Record, secondLoad.Record)
          && !ReferenceEquals(firstLoad.Data, secondLoad.Data)
          && !ReferenceEquals(firstLoad.DynamicData, secondLoad.DynamicData),
        "hero logical cache returned a referenced snapshot");
    Equal((byte)0x11, secondLoad.Record[0],
        "hero logical cache retained loader/consumer Record mutation");
    Equal((byte)0x22, secondLoad.Data[0],
        "hero logical cache retained consumer Data mutation");
    Equal((byte)0x33, secondLoad.DynamicData[0],
        "hero logical cache retained consumer DynamicData mutation");
    var readOnlyCache = new NativeHeroLogicalCache();
    var readOnlyLoads = 0;
    var readOnlyFirst = readOnlyCache.ReadOrLoad(91, () =>
    {
        readOnlyLoads++;
        return loadedSnapshot;
    });
    readOnlyFirst.Data[0] = 0x99;
    var readOnlySecond = readOnlyCache.ReadOrLoad(91, () =>
    {
        readOnlyLoads++;
        return loadedSnapshot;
    });
    Equal(2, readOnlyLoads,
        "hero read-only cache load was unexpectedly published");
    Check(!readOnlyCache.TryGet(91, out _)
          && readOnlySecond.Data[0] == 0x22,
        "hero read-only load polluted or referenced the shared cache");
    readOnlyCache.Set(loadedSnapshot);
    Check(readOnlyCache.ReadOrLoad(91, () =>
          throw new InvalidOperationException("cached read reloaded")) != null,
        "hero read-only cache did not reuse a published snapshot");
    Check(NativeDbToolProtocol.TryCreateReadSuccess(heroRequest,
        Encoding.ASCII.GetBytes("master"), secondLoad.Data,
        secondLoad.DynamicData, out var heroSuccess, out error), error);
    var heroBody = heroSuccess.Payload.AsSpan(NativeDbToolProtocol.HeaderSize);
    Check(heroBody.Slice(0, secondLoad.Data.Length)
            .SequenceEqual(secondLoad.Data)
          && heroBody.Slice(secondLoad.Data.Length)
            .SequenceEqual(secondLoad.DynamicData),
        "DB-tool 0104 did not concatenate Data/dynData");

    var gameSocSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "DBSvr", "Services",
        "GameSocService.cs"));
    var toolDispatchStart = gameSocSource.IndexOf(
        "if (!NativeDbServerProtocol.UsesNormalType1Dispatcher(serverType))",
        StringComparison.Ordinal);
    var normalDispatchStart = gameSocSource.IndexOf(
        "if (NativeDbServerProtocol.IsSilentNormalType1Command(command,",
        toolDispatchStart, StringComparison.Ordinal);
    Check(toolDispatchStart >= 0 && normalDispatchStart > toolDispatchStart,
        "DB-tool Type9 dispatch boundary");
    var toolDispatch = gameSocSource[toolDispatchStart..normalDispatchStart];
    Check(toolDispatch.Contains("ProcessNativeDbToolHumanRead(serverInfo, frame)",
              StringComparison.Ordinal)
          && toolDispatch.Contains("ProcessNativeDbToolHeroRead(serverInfo, frame)",
              StringComparison.Ordinal),
        "DB-tool reads are not isolated inside the Type9 dispatcher");
    var routeStart = gameSocSource.IndexOf(
        "public bool TrySendNativeHuman", StringComparison.Ordinal);
    var routeEnd = gameSocSource.IndexOf(
        "private NativeSavePersistenceData LoadNativeHumanPersistence",
        routeStart, StringComparison.Ordinal);
    Check(routeStart >= 0 && routeEnd > routeStart,
        "selected-human fan-out source boundary");
    var route = gameSocSource[routeStart..routeEnd];
    var fanoutLoop = route.IndexOf(
        "foreach (var target in targets)", StringComparison.Ordinal);
    var fanoutClock = fanoutLoop < 0 ? -1 : route.IndexOf(
        "sessionContext.DbClockBase", fanoutLoop, StringComparison.Ordinal);
    var fanoutEncode = fanoutLoop < 0 ? -1 : route.IndexOf(
        "NativeDbServerProtocol.TryCreateLoadHumanFrame",
        fanoutLoop, StringComparison.Ordinal);
    var fanoutReturn = route.LastIndexOf(
        "return true;", StringComparison.Ordinal);
    var emptyTargetGate = route.IndexOf(
        "if (targets.Count == 0)", StringComparison.Ordinal);
    var loadStart = route.IndexOf(
        "var index = _playDataService.Index", StringComparison.Ordinal);
    var ownershipStart = loadStart < 0 ? -1 : route.IndexOf(
        "if (!string.Equals(persistence.CharacterName",
        loadStart, StringComparison.Ordinal);
    var awardStart = ownershipStart < 0 ? -1 : route.IndexOf(
        "if (TryConsumeAwardPlayer", ownershipStart, StringComparison.Ordinal);
    Check(route.Contains("IsNativeHumanFanoutTarget(server)",
              StringComparison.Ordinal)
          && fanoutLoop >= 0
          && fanoutClock > fanoutLoop
          && fanoutEncode > fanoutClock
          && fanoutReturn > fanoutEncode
          && emptyTargetGate >= 0
          && loadStart > emptyTargetGate
          && ownershipStart > loadStart
          && awardStart > ownershipStart
          && route.Contains("RemoveNativeHumanFanoutTarget(target, socket)",
              StringComparison.Ordinal)
          && !route.Contains("多个活动GameSvr", StringComparison.Ordinal),
        "selected-human response did not preserve native fan-out semantics");
    var emptyTargetBlock = route[emptyTargetGate..loadStart];
    var loadBlock = route[loadStart..ownershipStart];
    var ownershipBlock = route[ownershipStart..awardStart];
    Check(emptyTargetBlock.Contains("return false;", StringComparison.Ordinal)
          && !emptyTargetBlock.Contains("return true;", StringComparison.Ordinal)
          && loadBlock.Contains("return true;", StringComparison.Ordinal)
          && !loadBlock.Contains("return false;", StringComparison.Ordinal)
          && ownershipBlock.Contains("return true;", StringComparison.Ordinal)
          && !ownershipBlock.Contains("return false;", StringComparison.Ordinal),
        "selected-human outer list/non-list return contract changed");
    var fanoutBody = route[fanoutLoop..fanoutReturn];
    var encodeFailureEnd = fanoutBody.IndexOf(
        "var socket = target.Socket;", StringComparison.Ordinal);
    var encodeFailureBlock = encodeFailureEnd < 0
        ? string.Empty
        : fanoutBody[..encodeFailureEnd];
    Check(fanoutBody.Contains("try { SendAll(socket, wire); }",
              StringComparison.Ordinal)
          && encodeFailureBlock.Contains("continue;", StringComparison.Ordinal)
          && fanoutBody.Contains(
              "RemoveNativeHumanFanoutTarget(target, socket)",
              StringComparison.Ordinal)
          && !fanoutBody.Contains("break;", StringComparison.Ordinal)
          && !fanoutBody.Contains("return", StringComparison.Ordinal),
        "selected-human fan-out did not continue after a target failure");
    var fanoutFilterStart = route.IndexOf(
        "private static bool IsNativeHumanFanoutTarget",
        StringComparison.Ordinal);
    var fanoutFilterEnd = fanoutFilterStart < 0 ? -1 : route.IndexOf(
        "private void RemoveNativeHumanFanoutTarget",
        fanoutFilterStart, StringComparison.Ordinal);
    Check(fanoutFilterStart >= 0 && fanoutFilterEnd > fanoutFilterStart,
        "selected-human fan-out filter source boundary");
    var fanoutFilter = route[fanoutFilterStart..fanoutFilterEnd];
    Check(fanoutFilter.Contains("DbServerWireMode.NativeType12",
              StringComparison.Ordinal)
          && !fanoutFilter.Contains("NativeRegistrationInitialized",
              StringComparison.Ordinal)
          && !fanoutFilter.Contains("NativeServerType", StringComparison.Ordinal)
          && !fanoutFilter.Contains("NativeHeartbeatTick", StringComparison.Ordinal)
          && !fanoutFilter.Contains(".Connected", StringComparison.Ordinal),
        "selected-human fan-out retained a non-original registration/type/heartbeat filter");
    var cleanup = route[fanoutFilterEnd..];
    Check(cleanup.Contains("ReferenceEquals(_serverList[i], target)",
              StringComparison.Ordinal)
          && cleanup.Contains(
              "ReferenceEquals(_serverList[i].Socket, socket)",
              StringComparison.Ordinal)
          && cleanup.Contains("_serverList.RemoveAt(i)",
              StringComparison.Ordinal)
          && cleanup.Contains("socket.Close()", StringComparison.Ordinal),
        "selected-human fan-out failure cleanup was not target/socket exact");
}

static void TestNativeDbToolWrites()
{
    static void PutRawShortString(byte[] destination, int offset,
        string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        destination[offset] = (byte)bytes.Length;
        bytes.CopyTo(destination, offset + 1);
    }

    static LegacyDbServerFrame CreateWriteFrame(ushort command, byte option,
        string headerName, byte[] body, ushort reserved = 0xA55A)
    {
        var payload = new byte[NativeDbToolProtocol.HeaderSize + body.Length];
        BitConverter.GetBytes(command).CopyTo(payload, 0);
        payload[4] = option;
        payload[0x10] = 0xFF;
        PutRawShortString(payload, 0x25, headerName);
        body.CopyTo(payload, NativeDbToolProtocol.HeaderSize);
        return new LegacyDbServerFrame(1, reserved, payload);
    }

    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    PutRawShortString(raw, 0, "body-human");
    PutRawShortString(raw, 0x20, "body-account");
    // Re-based 2026-08-04 to the native record layout.  This fixture used to write
    // hair 7 into 0x3B and a literal 1 into 0x3E ("record version"), i.e. it encoded
    // the exact corruption it was supposed to be able to catch.  战神 keeps the
    // Hair/Sex/Job triple consecutive at 0x3E/0x3F/0x40 (save sub_6B0FF0 @0x6B109A-
    // 0x6B10A9 writes obj+0x70/0x71/0x72 there; load sub_6AFD7C @0x6AFFBD-0x6AFFD5
    // reads them back).  0x3B is untouched by native, so it stays 0 here.
    raw[0x3E] = 7;      // Hair  <- obj+0x70
    raw[0x3F] = 1;      // Sex   <- obj+0x71
    raw[0x40] = 2;      // Job   <- obj+0x72

    // Round-trip the triple through the real codec. Setting the fixture bytes alone
    // proves nothing — this block is what actually bites if hair moves off 0x3E, and
    // it was added after a mutation (encoder writing hair to 0x3B) sailed through the
    // rest of this test untouched.
    {
        var hairProbe = new byte[NativeHumanDataCodec.DataRecordSize];
        PutRawShortString(hairProbe, 0, "hair-probe");
        PutRawShortString(hairProbe, 0x10, "0");
        PutRawShortString(hairProbe, 0x20, "hair-account");
        hairProbe[0x3E] = 7;    // Hair <- obj+0x70 (sub_6B0FF0 @0x6B109D)
        hairProbe[0x3F] = 1;    // Sex  <- obj+0x71 (@0x6B10A3)
        hairProbe[0x40] = 2;    // Job  <- obj+0x72 (@0x6B10A9)
        Check(NativeHumanLogicalCache.TryCreatePersistence("hair-account",
            "hair-probe", hairProbe, Array.Empty<byte>(),
            out var hairPersist, out var hairErr), hairErr);
        Check(NativeHumanDataCodec.TryDecode(hairPersist.DataBlob, null,
            out var hairDecoded, out hairErr), hairErr);
        Equal((byte)7, hairDecoded.Data.btHair, "hair decodes from 0x3E");
        Equal((byte)1, hairDecoded.Data.btSex, "sex decodes from 0x3F");
        Equal((byte)2, hairDecoded.Data.btJob, "job decodes from 0x40");

        // A decode->encode->decode round-trip CANNOT see which offset the encoder used.
        // TryDecode stores `NativeData = raw.Clone()` and TryEncode starts from
        // `info.NativeData.Clone()`, so the original 0x3E byte is carried through the
        // clone and the decoder reads the right value even when the encoder wrote hair
        // somewhere else. Two earlier versions of this block passed against a mutated
        // encoder for exactly that reason. The assertion therefore has to inspect the
        // ENCODED BYTES, after unwrapping, rather than the re-decoded object.
        hairDecoded.Data.btHair = 9;
        Check(NativeHumanDataCodec.TryEncode(hairDecoded, out var hairEncoded,
            out var hairScript, out hairErr), hairErr);
        // The codec's TryUnwrap is private and widening a production API for a test
        // would be the wrong trade, so inflate here: the blob is a short header plus a
        // zlib stream that expands to DataRecordSize (0xEEF8).
        byte[] hairEncodedRaw;
        {
            var zs = -1;
            for (var i = 0; i + 1 < hairEncoded.Length && i < 64; i++)
                if (hairEncoded[i] == 0x78 && hairEncoded[i + 1] == 0xDA) { zs = i; break; }
            Check(zs >= 0, "encoded human blob carries a zlib stream");
            using var input = new MemoryStream(hairEncoded, zs, hairEncoded.Length - zs);
            using var zlib = new System.IO.Compression.ZLibStream(
                input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            hairEncodedRaw = output.ToArray();
        }
        Equal(NativeHumanDataCodec.DataRecordSize, hairEncodedRaw.Length,
            "encoded blob inflates to the native record size");
        Equal((byte)9, hairEncodedRaw[0x3E],
            "encoder writes hair to 0x3E (sub_6B0FF0 @0x6B109D)");
        Equal((byte)0, hairEncodedRaw[0x3B],
            "encoder leaves 0x3B untouched (native never writes it)");
        Equal((byte)1, hairEncodedRaw[0x3F], "sex stays at 0x3F (@0x6B10A3)");
        Equal((byte)2, hairEncodedRaw[0x40], "job stays at 0x40 (@0x6B10A9)");
        Check(NativeHumanDataCodec.TryDecode(hairEncoded, hairScript,
            out var hairReDecoded, out hairErr), hairErr);
        Equal((byte)9, hairReDecoded.Data.btHair, "changed hair survives the round-trip");
    }
    Check(NativeHumanLogicalCache.TryCreatePersistence(
        "body-account", "body-human", raw, Array.Empty<byte>(),
        out var wrappedHuman, out var error), error);
    var humanFrame = CreateWriteFrame(
        NativeDbToolProtocol.HumanWriteCommand, 0x6D, "header-human",
        wrappedHuman.DataBlob);
    Check(NativeDbToolProtocol.TryDecodeHumanWrite(humanFrame,
        out var humanRequest, out error), error);
    Equal(NativeDbToolProtocol.HumanWriteCommand, humanRequest.Command,
        "DB-tool 0101 command");
    Equal((byte)0x6D, humanRequest.Option, "DB-tool 0101 option byte");
    Check(humanRequest.NameBytes.SequenceEqual(
            Encoding.ASCII.GetBytes("header-human"))
          && humanRequest.Body.SequenceEqual(wrappedHuman.DataBlob),
        "DB-tool 0101 request fields");
    var humanNameCopy = (byte[])humanRequest.NameBytes.Clone();
    var humanBodyCopy = (byte[])humanRequest.Body.Clone();
    humanFrame.Payload[0x26] ^= 0xFF;
    humanFrame.Payload[NativeDbToolProtocol.HeaderSize] ^= 0xFF;
    Check(humanRequest.NameBytes.SequenceEqual(humanNameCopy)
          && humanRequest.Body.SequenceEqual(humanBodyCopy),
        "DB-tool 0101 decoder retained its payload");
    Check(NativeDbToolProtocol.TryCreateHumanWritePersistence(humanRequest,
        out var exactHuman, out var decodedHuman, out var bodyHumanName,
        out error), error);
    Equal("body-human", exactHuman.CharacterName,
        "DB-tool 0101 used the header name as its target");
    Equal("body-account", exactHuman.Account,
        "DB-tool 0101 embedded account");
    Equal((ushort)0, exactHuman.Level,
        "DB-tool 0101 normalized level zero");
    Check(bodyHumanName.SequenceEqual(Encoding.ASCII.GetBytes("body-human"))
          && exactHuman.DataBlob.SequenceEqual(wrappedHuman.DataBlob)
          && exactHuman.ScriptDataBlob.Length == 0
          && decodedHuman.Data.btHair == 7
          && decodedHuman.Data.btSex == 1
          && decodedHuman.Data.btJob == 2,
        "DB-tool 0101 exact persistence fields");
    var humanCache = new NativeHumanLogicalCache();
    NativeSavePersistenceData? queuedHuman = null;
    Check(humanCache.TryStage(77, exactHuman, value =>
    {
        queuedHuman = value;
        return true;
    }), "DB-tool 0101 empty-script cache stage");
    exactHuman.DataBlob[8] ^= 0xFF;
    Check(humanCache.TryGet(77, out var cachedHuman)
          && cachedHuman.ScriptDataBlob.Length == 0
          && NativeHumanLogicalCache.TryExtractRaw(cachedHuman,
              out var extractedHuman, out var extractedScript)
          && extractedHuman.SequenceEqual(raw)
          && extractedScript.Length == 0
          && queuedHuman?.ScriptDataBlob.Length == 0,
        "DB-tool 0101 empty-script cache round trip");
    var shortHuman = new NativeDbToolWriteRequest
    {
        Command = NativeDbToolProtocol.HumanWriteCommand,
        Body = new byte[NativeHumanDataCodec.DataSizeMarker - 1]
    };
    Check(!NativeDbToolProtocol.TryCreateHumanWritePersistence(shortHuman,
            out _, out _, out _, out _),
        "DB-tool 0101 accepted a truncated body");

    var create = new NativeHeroCreateRequest
    {
        HeroType = 1,
        Code = 1,
        MasterName = "body-master",
        HeroName = "body-hero"
    };
    Check(NativeHeroDbFrameCodec.TryCreateInitialRecord(create,
        out var initialHero, out error), error);
    var heroRecord = initialHero.ToArray();
    var singleBody = heroRecord.Concat(new byte[4]).ToArray();
    var heroFrame = CreateWriteFrame(
        NativeDbToolProtocol.HeroWriteCommand, 2, "header-hero",
        singleBody);
    Check(NativeDbToolProtocol.TryDecodeHeroWrite(heroFrame,
        out var heroRequest, out error), error);
    Equal((byte)2, heroRequest.Option, "DB-tool 0103 HeroType option");
    Check(NativeDbToolProtocol.TryCreateHeroWriteData(heroRequest,
        out var singleHero, out error), error);
    Equal("body-hero", singleHero.Record.HeroName,
        "DB-tool 0103 used the header name as its target");
    Equal(NativeHeroDbFrameCodec.HeroRecordSize, singleHero.Data.Length,
        "DB-tool 0103 single Data length");
    Check(singleHero.RecordBytes.SequenceEqual(heroRecord)
          && singleHero.DynamicData.SequenceEqual(new byte[4]),
        "DB-tool 0103 single Data/dynData split");

    var threeBody = Enumerable.Range(0, 3)
        .SelectMany(_ => heroRecord).ToArray();
    var threeRequest = new NativeDbToolWriteRequest
    {
        Command = NativeDbToolProtocol.HeroWriteCommand,
        Option = 1,
        NameBytes = Encoding.ASCII.GetBytes("ignored"),
        Body = threeBody
    };
    Check(NativeDbToolProtocol.TryCreateHeroWriteData(threeRequest,
        out var threeHero, out error), error);
    Equal(NativeHeroBlobCodec.ThreeHeroRecordSize, threeHero.Data.Length,
        "DB-tool 0103 three-slot Data length");
    Equal(0, threeHero.DynamicData.Length,
        "DB-tool 0103 three-slot dynData length");
    Check(!NativeDbToolProtocol.TryCreateHeroWriteData(
            new NativeDbToolWriteRequest
            {
                Command = NativeDbToolProtocol.HeroWriteCommand,
                Body = new byte[NativeHeroDbFrameCodec.HeroRecordSize - 1]
            }, out _, out _),
        "DB-tool 0103 accepted a truncated record");

    Check(!NativeDbToolProtocol.TryDecodeHeroWrite(
            new LegacyDbServerFrame(2, 0, heroFrame.Payload), out _, out _)
          && !NativeDbToolProtocol.TryDecodeHumanWrite(heroFrame,
              out _, out _)
          && !NativeDbToolProtocol.TryDecodeHeroWrite(
              new LegacyDbServerFrame(1, 0,
                  new byte[NativeDbToolProtocol.HeaderSize - 1]),
              out _, out _),
        "DB-tool write decoder accepted an invalid envelope");
    var overlongName = (byte[])heroFrame.Payload.Clone();
    overlongName[0x25] = 16;
    Check(!NativeDbToolProtocol.TryDecodeHeroWrite(
            new LegacyDbServerFrame(1, 0, overlongName), out _, out _),
        "DB-tool 0103 accepted an SS15 length of 16");

    var humanResponse = NativeDbToolProtocol.CreateWriteResponse(
        humanRequest, 8);
    Equal((ushort)1, humanResponse.Type,
        "DB-tool 0101 response outer type");
    Equal((ushort)0, humanResponse.Reserved,
        "DB-tool 0101 response reserved word");
    Equal(NativeDbToolProtocol.HeaderSize, humanResponse.Payload.Length,
        "DB-tool write response payload length");
    Equal(NativeDbToolProtocol.ResponseCommand,
        BitConverter.ToUInt16(humanResponse.Payload, 0),
        "DB-tool write response command");
    Equal((ushort)8, BitConverter.ToUInt16(humanResponse.Payload, 2),
        "DB-tool 0101 response result");
    Equal((int)NativeDbToolProtocol.HumanWriteCommand,
        BitConverter.ToInt32(humanResponse.Payload, 4),
        "DB-tool 0101 response source command");
    Check(humanResponse.Payload[0x10] == 0
          && humanResponse.Payload.AsSpan(0x25, 16).SequenceEqual(
              new byte[] { 12 }.Concat(
                  Encoding.ASCII.GetBytes("header-human"))
                .Concat(new byte[3]).ToArray()),
        "DB-tool 0101 response echo/reserved bytes");
    var heroResponse = NativeDbToolProtocol.CreateWriteResponse(heroRequest, 1);
    Equal((ushort)1, BitConverter.ToUInt16(heroResponse.Payload, 2),
        "DB-tool 0103 response result");
    Equal((int)NativeDbToolProtocol.HeroWriteCommand,
        BitConverter.ToInt32(heroResponse.Payload, 4),
        "DB-tool 0103 response source command");

    var pendingForce = new GameSocService.NativeHeroSaveWorkItem(
        88, "body-hero", heroRecord, heroRecord, Array.Empty<byte>(),
        0, 0, 0, false, 1, 0, 0, 99);
    var exactWrite = new GameSocService.NativeHeroSaveWorkItem(
        88, "body-hero", heroRecord, threeBody, Array.Empty<byte>(),
        0, 0, 0, false, 1, 0, 0, null, true);
    pendingForce.ReplaceWith(exactWrite);
    Check(pendingForce.ExactPrepared
          && !pendingForce.ForceLevelOverride.HasValue
          && pendingForce.PreparedData.SequenceEqual(threeBody),
        "DB-tool 0103 exact write inherited a stale ForceLv override");

    var gameSocSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "DBSvr", "Services",
        "GameSocService.cs"));
    var toolDispatchStart = gameSocSource.IndexOf(
        "if (!NativeDbServerProtocol.UsesNormalType1Dispatcher(serverType))",
        StringComparison.Ordinal);
    var normalDispatchStart = gameSocSource.IndexOf(
        "if (NativeDbServerProtocol.IsSilentNormalType1Command(command,",
        toolDispatchStart, StringComparison.Ordinal);
    var toolDispatch = gameSocSource[toolDispatchStart..normalDispatchStart];
    Check(toolDispatch.Contains(
              "ProcessNativeDbToolHumanWrite(serverInfo, frame)",
              StringComparison.Ordinal)
          && toolDispatch.Contains(
              "ProcessNativeDbToolHeroWrite(serverInfo, frame)",
              StringComparison.Ordinal)
          && gameSocSource.Contains("EnqueueHeroExactSnapshot(snapshot)",
              StringComparison.Ordinal),
        "DB-tool writes are not isolated/exact inside the Type9 dispatcher");
}

static void TestNativeDbToolLifecycle()
{
    static void PutShortString(byte[] destination, int offset, byte[] value)
    {
        destination[offset] = (byte)value.Length;
        value.CopyTo(destination, offset + 1);
    }

    var oldAccount = Encoding.ASCII.GetBytes("old-account");
    var newAccount = Encoding.ASCII.GetBytes("new-account");
    var humanName = Encoding.ASCII.GetBytes("human-name");
    var heroName = Encoding.ASCII.GetBytes("hero-name");
    var payload = new byte[NativeDbToolProtocol.HeaderSize];
    BitConverter.GetBytes(NativeDbToolProtocol.DeleteCommand).CopyTo(payload, 0);
    BitConverter.GetBytes(unchecked((int)0xAABBCC07)).CopyTo(payload, 4);
    PutShortString(payload, 0x10, oldAccount);
    PutShortString(payload, 0x25, newAccount);
    PutShortString(payload, 0x35, heroName);
    Check(NativeDbToolProtocol.TryDecodeDelete(
        new LegacyDbServerFrame(1, 0xAA55, payload), out var request,
        out var error), error);
    Equal(unchecked((int)0xAABBCC07), request.Operation,
        "DB-tool 0100 operation dword");
    Check(request.AccountBytes.SequenceEqual(oldAccount)
          && request.NameBytes.SequenceEqual(newAccount)
          && request.HeroNameBytes.SequenceEqual(heroName),
        "DB-tool 0100 request fields");
    var requestAccount = (byte[])request.AccountBytes.Clone();
    payload[0x11] ^= 0xFF;
    Check(request.AccountBytes.SequenceEqual(requestAccount),
        "DB-tool 0100 decoder retained the payload");
    Check(!NativeDbToolProtocol.TryDecodeDelete(
            new LegacyDbServerFrame(2, 0, payload), out _, out _)
          && !NativeDbToolProtocol.TryDecodeDelete(
              new LegacyDbServerFrame(1, 0,
                  new byte[NativeDbToolProtocol.HeaderSize - 1]), out _, out _),
        "DB-tool 0100 accepted an invalid envelope");
    var overlong = (byte[])payload.Clone();
    overlong[0x35] = 16;
    Check(!NativeDbToolProtocol.TryDecodeDelete(
            new LegacyDbServerFrame(1, 0, overlong), out _, out _),
        "DB-tool 0100 accepted an overlong hero SS15");

    var response = NativeDbToolProtocol.CreateDeleteResponse(request, 1);
    Equal((ushort)1, response.Type, "DB-tool 0100 response outer type");
    Equal((ushort)0, response.Reserved, "DB-tool 0100 response reserved word");
    Equal(NativeDbToolProtocol.HeaderSize, response.Payload.Length,
        "DB-tool 0100 response length");
    Equal(NativeDbToolProtocol.ResponseCommand,
        BitConverter.ToUInt16(response.Payload, 0), "DB-tool 0100 response command");
    Equal((ushort)1, BitConverter.ToUInt16(response.Payload, 2),
        "DB-tool 0100 response result");
    Equal(request.Operation, BitConverter.ToInt32(response.Payload, 4),
        "DB-tool 0100 response operation echo");
    Check(response.Payload.AsSpan(0x11, oldAccount.Length)
              .SequenceEqual(oldAccount)
          && response.Payload.AsSpan(0x26, newAccount.Length)
              .SequenceEqual(newAccount)
          && response.Payload.AsSpan(0x36, heroName.Length)
              .SequenceEqual(heroName),
        "DB-tool 0100 response identities");
    var expected = new byte[NativeDbToolProtocol.HeaderSize];
    BitConverter.GetBytes(NativeDbToolProtocol.ResponseCommand).CopyTo(expected, 0);
    BitConverter.GetBytes((ushort)1).CopyTo(expected, 2);
    BitConverter.GetBytes(request.Operation).CopyTo(expected, 4);
    PutShortString(expected, 0x10, oldAccount);
    PutShortString(expected, 0x25, newAccount);
    PutShortString(expected, 0x35, heroName);
    Check(response.Payload.SequenceEqual(expected),
        "DB-tool 0100 response did not zero reserved bytes");

    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    PutShortString(raw, 0, humanName);
    PutShortString(raw, 0x20, oldAccount);
    raw[0x3E] = 1;
    Check(NativeHumanLogicalCache.TryCreatePersistence("old-account",
        "human-name", raw, Array.Empty<byte>(), out var persistence,
        out error), error);
    Check(NativeHumanDataCodec.TryRewriteAccount(persistence.DataBlob,
        newAccount, out var renamedBlob, out error), error);
    Check(NativeHumanDataCodec.TryDecode(renamedBlob,
        Array.Empty<byte>(), out var renamedHuman, out error), error);
    Equal("new-account", renamedHuman.Data.sAccount,
        "native account rename did not rewrite raw SS20");
    Check(persistence.DataBlob.AsSpan(8 + 0x20, 21)
              .SequenceEqual(raw.AsSpan(0x20, 21))
          && renamedBlob.AsSpan(8, 0x20)
              .SequenceEqual(persistence.DataBlob.AsSpan(8, 0x20)),
        "native account rename changed unrelated human bytes");

    Check(NativeHumanDataCodec.TryEncode(renamedHuman, out var compressedBlob,
        out _, out error), error);
    Check(BitConverter.ToUInt32(compressedBlob, 0) != 0,
        "native account rename compressed fixture was not compressed");
    Check(NativeHumanDataCodec.TryRewriteAccount(compressedBlob, oldAccount,
        out var compressedRenamedBlob, out error), error);
    Check(NativeHumanDataCodec.TryDecode(compressedBlob, Array.Empty<byte>(),
        out var compressedBefore, out error), error);
    Check(NativeHumanDataCodec.TryDecode(compressedRenamedBlob,
        Array.Empty<byte>(), out var compressedAfter, out error), error);
    Equal("old-account", compressedAfter.Data.sAccount,
        "compressed native account rename did not rewrite raw SS20");
    var beforeWithoutAccount = (byte[])compressedBefore.NativeData.Clone();
    var afterWithoutAccount = (byte[])compressedAfter.NativeData.Clone();
    beforeWithoutAccount.AsSpan(0x20, 21).Clear();
    afterWithoutAccount.AsSpan(0x20, 21).Clear();
    Check(beforeWithoutAccount.SequenceEqual(afterWithoutAccount),
        "compressed native account rename changed unrelated human bytes");

    var accountCache = new NativeAccountStorageCache();
    accountCache.RegisterAccount(oldAccount);
    var callbackCalls = 0;
    Check(accountCache.TryRenameAccount(oldAccount, newAccount, () =>
          {
              callbackCalls++;
              return true;
          })
          && callbackCalls == 1
          && !accountCache.ContainsAccount(oldAccount)
          && accountCache.ContainsAccount(newAccount),
        "native storage account cache did not atomically rekey");
    accountCache.RegisterAccount(Encoding.ASCII.GetBytes("occupied"));
    Check(accountCache.TryRenameAccount(newAccount,
            Encoding.ASCII.GetBytes("occupied"), () =>
            {
                callbackCalls++;
                return true;
            }) && callbackCalls == 2
              && !accountCache.ContainsAccount(newAccount)
              && accountCache.ContainsAccount(Encoding.ASCII.GetBytes("occupied")),
        "native storage account cache rejected the original conflicting rename");

    var staleWriteCache = new NativeAccountStorageCache();
    var occupied = Encoding.ASCII.GetBytes("occupied");
    staleWriteCache.RegisterAccount(oldAccount);
    staleWriteCache.RegisterAccount(occupied);
    using var staleEnsureEntered = new ManualResetEventSlim();
    using var releaseStaleEnsure = new ManualResetEventSlim();
    var staleSaveCalls = 0;
    var staleStorageService = InterfaceProxy.Create<IStorageService>(
        (method, _) =>
        {
            if (method.Name == nameof(IStorageService.EnsureNativeStorage))
            {
                staleEnsureEntered.Set();
                return releaseStaleEnsure.Wait(5000) ? 73 : 0;
            }
            if (method.Name == nameof(IStorageService.SaveNativeStorage))
            {
                Interlocked.Increment(ref staleSaveCalls);
                return true;
            }
            return InterfaceProxy.DefaultValue(method.ReturnType);
        });
    staleWriteCache.StartSaveWorker(staleStorageService);
    try
    {
        Check(staleWriteCache.StageSave(occupied, new byte[] { 7 }),
            "native storage cache did not stage destination write");
        Check(staleEnsureEntered.Wait(5000),
            "native storage cache worker did not reach destination write");
        Check(staleWriteCache.TryRenameAccount(oldAccount, occupied,
            () => true),
            "native storage cache did not rename over an active destination");
    }
    finally
    {
        releaseStaleEnsure.Set();
        staleWriteCache.StopSaveWorker();
    }
    Equal(0, staleSaveCalls,
        "stale destination storage write was applied to the renamed source");

    var playerOld = new GameSocService.NativeSaveWorkItem(31, persistence)
    {
        Generation = 2
    };
    var playerNew = new GameSocService.NativeSaveWorkItem(31, persistence)
    {
        Generation = 9
    };
    playerOld.ReplaceWith(playerNew);
    Equal(9L, playerOld.Generation,
        "native player save merge retained an old generation");
    var heroOld = new GameSocService.NativeHeroSaveWorkItem(32, "hero",
        new byte[NativeHeroDbFrameCodec.HeroRecordSize],
        new byte[NativeHeroDbFrameCodec.HeroRecordSize], Array.Empty<byte>(),
        0, 0, 0, false, 1, 0, 0, null)
    {
        Generation = 3
    };
    var heroNew = new GameSocService.NativeHeroSaveWorkItem(32, "hero",
        new byte[NativeHeroDbFrameCodec.HeroRecordSize],
        new byte[NativeHeroDbFrameCodec.HeroRecordSize], Array.Empty<byte>(),
        0, 0, 0, false, 1, 0, 0, null)
    {
        Generation = 10
    };
    heroOld.ReplaceWith(heroNew);
    Equal(10L, heroOld.Generation,
        "native hero save merge retained an old generation");

    var gameSocSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "DBSvr", "Services",
        "GameSocService.cs"));
    Check(gameSocSource.Contains("ProcessNativeDbToolDelete(serverInfo, frame)",
              StringComparison.Ordinal)
          && gameSocSource.Contains("(byte)request.Operation switch",
              StringComparison.Ordinal)
          && gameSocSource.Contains("CreateDeleteResponse(request, result)",
              StringComparison.Ordinal)
          && gameSocSource.Contains("IsNativeSaveWorkCurrent(workItem)",
              StringComparison.Ordinal)
          && gameSocSource.Contains("IsHeroSaveWorkCurrent(item)",
              StringComparison.Ordinal)
          && gameSocSource.Contains("HardDeleteNativeHeroIndex", StringComparison.Ordinal),
        "DB-tool 0100 dispatch or save-generation barrier is missing");
    var playRecordSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "DBSvr", "DB", "impl",
        "MySqlPlayRecordService.cs"));
    Check(playRecordSource.Contains("RenameNativeAccount", StringComparison.Ordinal)
          && playRecordSource.Contains("UPDATE gamedata.CreditCard SET PTID=@new",
              StringComparison.Ordinal)
          && playRecordSource.Contains("UPDATE mir3.user_storage SET PTID=@new",
              StringComparison.Ordinal)
          && playRecordSource.Contains("NativeHumanDataCodec.TryRewriteAccount",
              StringComparison.Ordinal),
        "DB-tool 0100 account rename does not cover the native storage chain");
}

static void TestNativeHeroSaveStateTracker()
{
    var tracker = new NativeHeroSaveStateTracker();
    var state = tracker.SnapshotForSave(10, false, 1, 0, 4);
    Check(state.Consignation == 1 && !state.IsDelete && state.HeroType == 1,
        "hero save mode4 side effect");
    state = tracker.SnapshotForSave(10, false, 1, 0, 0);
    Check(state.Consignation == 1 && !state.IsDelete,
        "hero save mode4 did not remain sticky across batches");
    state = tracker.SnapshotForSave(10, false, 1, 0, 5);
    Check(!state.IsDelete, "hero save mode5 deleted non-type2 hero");
    state = tracker.SnapshotForSave(11, false, 2, 1, 5);
    Check(state.Consignation == 1 && state.IsDelete && state.HeroType == 2,
        "hero save mode5 type2 side effect");
    state = tracker.SnapshotForSave(11, false, 2, 0, 0);
    Check(state.Consignation == 1 && state.IsDelete,
        "hero save delete side effect did not remain sticky across batches");
    tracker.ClearConsignation(11);
    state = tracker.SnapshotForSave(11, false, 2, 1, 0);
    Check(state.Consignation == 0 && state.IsDelete,
        "hero save consignation reset removed wrong state");
    state = tracker.SnapshotForSave(12, true, 7, 3, 0);
    Check(state.IsDelete && state.HeroType == 7 && state.Consignation == 3,
        "hero save did not preserve the absolute index snapshot");
    tracker.Remove(11);
    state = tracker.SnapshotForSave(11, false, 2, 0, 0);
    Check(state.Consignation == 0 && !state.IsDelete,
        "hero save state removal failed");

    var notification = NativeDbServerProtocol.CreateHeroSaveNotification(
        unchecked((int)0x88776655), 0x12345678);
    Equal((ushort)1, notification.Type, "hero save notification outer type");
    Equal(0x48, notification.Payload.Length,
        "hero save notification payload length");
    Equal(NativeDbServerProtocol.HeroSaveNotificationCommand,
        BitConverter.ToUInt16(notification.Payload, 0),
        "hero save notification command");
    Equal(unchecked((int)0x88776655),
        BitConverter.ToInt32(notification.Payload, 8),
        "hero save notification Param1");
    Equal(0x12345678, BitConverter.ToInt32(notification.Payload, 12),
        "hero save notification Param2");
    Check(NativeDbServerProtocol.ShouldReceiveHeroSaveNotification(0)
          && NativeDbServerProtocol.ShouldReceiveHeroSaveNotification(8)
          && !NativeDbServerProtocol.ShouldReceiveHeroSaveNotification(9),
        "hero save notification recipients");
}

static void TestNativeHeroAttachmentStateTracker()
{
    static HeroIndexInfo Hero(int index, string master, int job,
        bool deleted = false, int consignation = 0) => new()
    {
        Idx = index,
        MasterName = master,
        Job = job,
        IsDelete = deleted,
        Consignation = consignation
    };

    var tracker = new NativeHeroAttachmentStateTracker();
    tracker.MarkLoaded(1, 0);
    tracker.MarkLoaded(2, 1);
    tracker.MarkLoaded(3, 2);
    tracker.MarkLoaded(4, 0);
    tracker.MarkLoaded(5, 0);
    tracker.MarkLoaded(6, 255);
    Check(tracker.TryGetSlotPlusOne(6, out var wrapped) && wrapped == 0,
        "hero attachment slot byte did not wrap like the native record");

    var heroes = new[]
    {
        Hero(1, "master", 0),
        Hero(2, "master", 1, consignation: 1),
        Hero(3, "master", byte.MaxValue),
        Hero(4, "master", 2, deleted: true),
        Hero(5, "other", 0)
    };

    tracker.ClearForDetach("master", false, 2, heroes);
    Check(tracker.TryGetSlotPlusOne(1, out var ordinary) && ordinary == 0,
        "ordinary native detach did not clear an active ordinary hero");
    Check(tracker.TryGetSlotPlusOne(2, out var consigned) && consigned == 0,
        "ordinary detach did not honor the active special-hero branch");
    Check(tracker.TryGetSlotPlusOne(3, out var special) && special == 3,
        "ordinary detach cleared the special hero state");
    Check(tracker.TryGetSlotPlusOne(4, out var deleted) && deleted == 1,
        "native detach cleared a deleted hero state");
    Check(tracker.TryGetSlotPlusOne(5, out var otherMaster) && otherMaster == 1,
        "native detach crossed master-name ownership");

    tracker.ClearForDetach("master", true, 0, heroes);
    Check(tracker.TryGetSlotPlusOne(3, out special) && special == 0,
        "special native detach did not clear the special hero state");

    tracker.MarkLoaded(1, 0);
    tracker.ClearForDetach("master", false, 3, heroes);
    Check(tracker.TryGetSlotPlusOne(1, out ordinary) && ordinary == 1,
        "native detach accepted an ordinary Mode outside 0..2");
    tracker.Remove(1);
    Check(!tracker.TryGetSlotPlusOne(1, out _),
        "hero attachment state removal failed");
}

static byte[] BuildDynamicData(
    params (byte Type, byte[] Payload)[] sections)
{
    var result = new byte[4 + sections.Sum(section =>
        7 + section.Payload.Length)];
    BitConverter.GetBytes(result.Length - 4).CopyTo(result, 0);
    var offset = 4;
    foreach (var section in sections)
    {
        BitConverter.GetBytes(0xABCDEFAAu).CopyTo(result, offset);
        BitConverter.GetBytes((ushort)section.Payload.Length)
            .CopyTo(result, offset + 4);
        result[offset + 6] = section.Type;
        section.Payload.CopyTo(result, offset + 7);
        offset += 7 + section.Payload.Length;
    }
    return result;
}

static byte[] BuildCompressedNativeBlob(byte[] raw, ushort sizeMarker,
    int? storageLength = null)
{
    using var output = new MemoryStream();
    using (var zlib = new System.IO.Compression.ZLibStream(output,
               System.IO.Compression.CompressionLevel.SmallestSize, true))
        zlib.Write(raw, 0, raw.Length);
    var compressed = output.ToArray();
    Check(compressed.Length <= ushort.MaxValue,
        "native compressed fixture exceeds UInt16 length");

    var minimumLength = checked(8 + compressed.Length);
    var blobLength = storageLength
                     ?? checked((minimumLength + 0xFF) & ~0xFF);
    Check(blobLength >= minimumLength && (blobLength & 0xFF) == 0,
        "native compressed fixture storage length");

    var result = new byte[blobLength];
    BitConverter.GetBytes(NativeHumanDataCodec.ComputeNativeCrc(compressed))
        .CopyTo(result, 0);
    BitConverter.GetBytes(sizeMarker).CopyTo(result, 4);
    BitConverter.GetBytes((ushort)compressed.Length).CopyTo(result, 6);
    compressed.CopyTo(result, 8);
    return result;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
}

sealed class FakeNativeForceLevelStore : INativeForceLevelStore
{
    public Queue<NativeForceLevelStoreResult> Player { get; } = new();
    public Queue<NativeForceLevelStoreResult> Hero { get; } = new();
    public int HeroCalls { get; private set; }
    public ushort LastForceLevel { get; private set; }

    public NativeForceLevelStoreAttempt ApplyPlayer(byte[] characterName,
        ushort forceLevel)
    {
        LastForceLevel = forceLevel;
        var result = Player.Count == 0
            ? NativeForceLevelStoreResult.Missing : Player.Dequeue();
        return Attempt(result, NativeForceLevelTarget.Player,
            characterName, forceLevel, 1);
    }

    public NativeForceLevelStoreAttempt ApplyHero(byte[] characterName,
        ushort forceLevel)
    {
        HeroCalls++;
        LastForceLevel = forceLevel;
        var result = Hero.Count == 0
            ? NativeForceLevelStoreResult.Missing : Hero.Dequeue();
        return Attempt(result, NativeForceLevelTarget.Hero,
            characterName, forceLevel, 2);
    }

    private static NativeForceLevelStoreAttempt Attempt(
        NativeForceLevelStoreResult result, NativeForceLevelTarget target,
        byte[] name, ushort forceLevel, int index) =>
        new(result, result == NativeForceLevelStoreResult.Queued
            ? new NativeForceLevelMutation
            {
                Target = target,
                Index = index,
                ForceLevel = forceLevel,
                CharacterNameBytes = (byte[])(name?.Clone() ?? Array.Empty<byte>())
            }
            : null);
}

sealed class ExtractionPetService : IPetService
{
    private readonly Dictionary<long, byte[]> _data = new();

    public byte[] LastSavedData { get; private set; } = Array.Empty<byte>();

    public byte[] LoadPet(long masterId) => _data.TryGetValue(masterId,
        out var data) ? (byte[])data.Clone() : null!;

    public (int idx, byte[] data) LoadPetWithIdx(long masterId) =>
        _data.TryGetValue(masterId, out var data)
            ? (1, (byte[])data.Clone()) : (0, null!);

    public bool CreatePet(string masterName, long masterId, int level,
        int exp)
    {
        _data[masterId] = NativeDominatorPetProtocol.CreateDefaultData(
            Encoding.ASCII.GetBytes(masterName));
        return true;
    }

    public bool SavePet(long masterId, string masterName, int level, int exp,
        byte[] data)
    {
        LastSavedData = (byte[])data.Clone();
        _data[masterId] = (byte[])data.Clone();
        return true;
    }

    public bool UpdatePetLevel(long masterId, int level, int exp) => true;
    public bool DeletePet(long masterId) => _data.Remove(masterId);
    public bool RenameMaster(string oldMaster, string newMaster) => true;
    public List<PetIndexInfo> GetPetPage(int lastIdx, int limit) => new();
}

sealed class BlockingNativeType2RankingLoader : INativeType2RankingLoader
{
    private readonly List<byte[]> _records;
    private int _calls;

    public BlockingNativeType2RankingLoader(IEnumerable<byte[]> records)
    {
        _records = records.Select(record => (byte[])record.Clone()).ToList();
    }

    public ManualResetEventSlim Entered { get; } = new();
    public ManualResetEventSlim Release { get; } = new();
    public int Calls => Volatile.Read(ref _calls);

    public bool TryLoad(out List<byte[]> records)
    {
        Interlocked.Increment(ref _calls);
        Entered.Set();
        Release.Wait();
        records = _records.Select(record => (byte[])record.Clone()).ToList();
        return true;
    }
}

class InterfaceProxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?>? _handler;

    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var result = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)result)._handler = handler;
        return result;
    }

    public static object? DefaultValue(Type type) => type == typeof(void)
        ? null : type.IsValueType ? Activator.CreateInstance(type) : null;

    protected override object? Invoke(MethodInfo? targetMethod,
        object?[]? args) => _handler!(targetMethod!, args);
}
