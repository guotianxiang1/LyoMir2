using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using DBSvr.Core;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936,
    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepositoryRoot();
if (root == null)
{
    Console.Error.WriteLine("INCOMPLETE: repository root was not supplied and could "
        + "not be located from the working directory. "
        + "Usage: HeroDbCheck [repository root]");
    Environment.Exit(2);
}
var dbSvr = Path.Combine(root, "DBSvr");
var gameSvr = Path.Combine(root, "GameSvr");
var systemModule = Path.Combine(root, "SystemModule");

CheckSourceGuards();
CheckFixedRecordMapping();
CheckDynamicDataCodec();
CheckLoadRequestCodec();
CheckDetachRequestCodec();
CheckSaveRequestCodec();
CheckLoadResponseCodec();
CheckCreateCodec();
CheckMalformedFrames();
CheckBlobEnvelopeCodec();
CheckDataRecordMerge();
CheckThreeSlotBuildData();
CheckLogicalSnapshot();

Console.WriteLine(
    "PASS hero-db native=33AABB77 commands=160..167/194/51/53/59/70 fixed=49D4 three=DD7C dyn=2,6,7 delete=index-only blob=zlib/crc32 sql=myisam-serialized");
return;

void CheckSourceGuards()
{
    var program = Read("Program.cs");
    var mainForm = Read(Path.Combine("Forms", "MainForm.cs"));
    var userSoc = Read(Path.Combine("Services", "UserSocService.cs"));
    var gameSoc = Read(Path.Combine("Services", "GameSocService.cs"));
    var m2DbService = File.ReadAllText(Path.Combine(
        gameSvr, "Services", "DBService.cs"));
    var heroData = Read(Path.Combine("DB", "impl", "MySqlHeroDataService.cs"));
    var heroRecord = Read(Path.Combine("DB", "impl", "MySqlHeroRecordService.cs"));
    var playRecord = Read(Path.Combine("DB", "impl", "MySqlPlayRecordService.cs"));

    Assert(program.Contains(
            "AddSingleton<IHeroRecordService, MySqlHeroRecordService>", StringComparison.Ordinal),
        "native hero record service is not registered");
    Assert(program.Contains(
            "AddSingleton<IHeroDataService, MySqlHeroDataService>", StringComparison.Ordinal),
        "native hero data service is not registered");
    Assert(!mainForm.Contains("IHeroRecordService", StringComparison.Ordinal)
           && !mainForm.Contains("IHeroDataService", StringComparison.Ordinal),
        "MainForm still resolves unsupported hero services");
    Assert(!userSoc.Contains("IHeroRecordService", StringComparison.Ordinal)
           && !userSoc.Contains("IHeroDataService", StringComparison.Ordinal),
        "UserSocService still resolves unsupported hero services");

    Assert(!heroData.Contains("BlobCompressor", StringComparison.Ordinal),
        "hero blobs are still passed through the player-data compressor");
    Assert(!heroData.Contains("ProtoBuf", StringComparison.Ordinal),
        "hero blobs are still passed through ProtoBuf");
    Assert(!heroData.Contains("UNHEX", StringComparison.Ordinal),
        "hero blob service still performs guessed hex writes");
    Assert(heroData.Contains("NativeHeroBlobCodec", StringComparison.Ordinal),
        "hero SQL blob service does not use the native codec");
    Assert(heroData.Contains("SaveRecord", StringComparison.Ordinal)
           && heroData.Contains("_recordSaveLocks", StringComparison.Ordinal),
        "three-slot hero saves are not serialized");
    Assert(heroData.Contains("UPDATE mir3.hero_data AS d", StringComparison.Ordinal)
           && heroData.Contains("JOIN mir3.hero_index", StringComparison.Ordinal)
           && heroData.Contains("UseAffectedRows = false", StringComparison.Ordinal)
           && heroData.Contains("ExecuteNonQuery() <= 0", StringComparison.Ordinal)
           && heroData.Contains("IndexJob", StringComparison.Ordinal)
           && heroData.Contains("requireThreeRecords", StringComparison.Ordinal),
        "hero Data/index save is not a checked single-statement MyISAM update");
    Assert(heroRecord.Contains("Consignation", StringComparison.Ordinal)
           && heroRecord.Contains("ORDER BY idx", StringComparison.Ordinal)
           && !heroRecord.Contains("ORDER BY idx LIMIT 3", StringComparison.Ordinal)
           && heroRecord.Contains("SET IsDelete=1", StringComparison.Ordinal),
        "hero index selection is missing native filters or stable ordering");

    var playLoadStart = playRecord.IndexOf(
        "public void LoadQuickList()", StringComparison.Ordinal);
    var playLoadEnd = playRecord.IndexOf(
        "// ===================== 查询", playLoadStart,
        StringComparison.Ordinal);
    Assert(playLoadStart >= 0 && playLoadEnd > playLoadStart,
        "native character-index load boundaries are missing");
    var playLoad = playRecord[playLoadStart..playLoadEnd];
    var userIdUpdate = playLoad.IndexOf(
        "UPDATE mir3.user_index SET UserId=@userId", StringComparison.Ordinal);
    var ptidPublish = playLoad.IndexOf(
        "AddNativeType3Record(nativeRecord)", StringComparison.Ordinal);
    var namePublish = playLoad.IndexOf(
        "QuickList[nativeRecord.ChrName]", StringComparison.Ordinal);
    Assert(!playLoad.Contains("var page = new List", StringComparison.Ordinal)
           && !playLoad.Contains("QuickList.Clear()", StringComparison.Ordinal)
           && userIdUpdate >= 0 && ptidPublish > userIdUpdate
           && namePublish > ptidPublish,
        "native character-index load is not published one row at a time in original order");

    Assert(gameSoc.Contains("default:", StringComparison.Ordinal)
           && gameSoc.Contains("SendFail(nQueryId, socket);", StringComparison.Ordinal),
        "unknown GameSoc commands are not rejected explicitly");
    Assert(gameSoc.Contains("case NativeHeroDbFrameCodec.LoadCommand:", StringComparison.Ordinal)
           && gameSoc.Contains("case NativeHeroDbFrameCodec.SaveCommand:", StringComparison.Ordinal)
           && gameSoc.Contains("case NativeHeroDbFrameCodec.CreateCommand:", StringComparison.Ordinal)
           && gameSoc.Contains("case NativeHeroDbFrameCodec.DeleteCommand:", StringComparison.Ordinal)
           && gameSoc.Contains("NativeHeroDbFrameCodec.LoadResponseCommand", StringComparison.Ordinal)
           && gameSoc.Contains("NativeHeroDbFrameCodec.CreateResponseCommand", StringComparison.Ordinal)
           && gameSoc.Contains("NativeHeroDbFrameCodec.DeleteResponseCommand", StringComparison.Ordinal),
        "native hero 160/161/162/163/51/53/59 routing is incomplete");
    Assert(gameSoc.Contains("TryDecodeLoadRequest", StringComparison.Ordinal)
           && gameSoc.Contains("TryDecodeSaveRequest", StringComparison.Ordinal)
           && gameSoc.Contains("TryDecodeCreateRequest", StringComparison.Ordinal)
           && gameSoc.Contains("TryDecodeDeleteRequest", StringComparison.Ordinal)
           && gameSoc.Contains("TryDecodeDetachRequest", StringComparison.Ordinal)
           && gameSoc.Contains("ProcessNativeHeroDetach", StringComparison.Ordinal),
        "native hero inner frames are not validated against their outer opcode");
    var m2Type1Start = m2DbService.IndexOf(
        "private static void ProcessNativeType1", StringComparison.Ordinal);
    var m2Type1End = m2DbService.IndexOf(
        "private bool SendRegistration", m2Type1Start,
        StringComparison.Ordinal);
    Assert(m2Type1Start >= 0 && m2Type1End > m2Type1Start,
        "M2 native Type1 dispatcher boundaries are missing");
    var m2Type1 = m2DbService[m2Type1Start..m2Type1End];
    Assert(!m2Type1.Contains("DeleteResponseCommand", StringComparison.Ordinal),
        "M2 routes 0x0059 even though the original Type1 table ignores it");
    var createHandlerStart = gameSoc.IndexOf(
        "private int CreateHeroRcd(NativeHeroCreateRequest", StringComparison.Ordinal);
    var createHandlerEnd = gameSoc.IndexOf(
        "private void DeleteHeroRcd(int queryId", createHandlerStart,
        StringComparison.Ordinal);
    Assert(createHandlerStart >= 0 && createHandlerEnd > createHandlerStart,
        "native hero create handler boundaries are missing");
    var createHandler = gameSoc[createHandlerStart..createHandlerEnd];
    Assert(createHandler.Contains("request.HeroType is < 1 or > 2", StringComparison.Ordinal)
           && createHandler.Contains("request.Code is < 1 or > 6", StringComparison.Ordinal)
           && createHandler.Contains("_playRecordService.Index(request.HeroName)",
               StringComparison.Ordinal)
           && createHandler.Contains("_heroRecordService.IsHeroNameExists(request.HeroName)",
               StringComparison.Ordinal)
           && !createHandler.Contains("_playRecordService.Index(request.MasterName)",
               StringComparison.Ordinal),
        "native 0x162 parameter/name result mapping differs from the original");
    Assert(createHandler.Contains("if (hero.Consignation == 0) activeCount++;",
               StringComparison.Ordinal)
           && createHandler.Contains(
               "if (hero.HeroType == request.HeroType) activeCount = 2;",
               StringComparison.Ordinal),
        "native 0x162 hero-capacity accumulator differs from the original");
    var deleteHandlerStart = gameSoc.IndexOf(
        "private void DeleteHeroRcd(int queryId", StringComparison.Ordinal);
    var deleteHandlerEnd = gameSoc.IndexOf(
        "private void SendHeroCreateResponse", StringComparison.Ordinal);
    Assert(deleteHandlerStart >= 0 && deleteHandlerEnd > deleteHandlerStart,
        "native hero delete handler boundaries are missing");
    var deleteHandler = gameSoc.Substring(deleteHandlerStart,
        deleteHandlerEnd - deleteHandlerStart);
    Assert(deleteHandler.Contains("_heroRecordService.DeleteHero", StringComparison.Ordinal)
           && !deleteHandler.Contains("HardDeleteHero", StringComparison.Ordinal)
           && !deleteHandler.Contains("DeleteDataRow", StringComparison.Ordinal),
        "native 0x163 delete is not an index-only soft delete");
    var saveHandlerStart = gameSoc.IndexOf(
        "private bool SaveHeroRcd(byte[] frame)", StringComparison.Ordinal);
    var saveHandlerEnd = gameSoc.IndexOf(
        "private void SendHeroSaveResponse", StringComparison.Ordinal);
    Assert(saveHandlerStart >= 0 && saveHandlerEnd > saveHandlerStart,
        "native hero save handler boundaries are missing");
    var saveHandler = gameSoc.Substring(saveHandlerStart, saveHandlerEnd - saveHandlerStart);
    Assert(!saveHandler.Contains("SendRequest(", StringComparison.Ordinal)
           && !saveHandler.Contains("SendHeroLoadResponse(", StringComparison.Ordinal)
           && gameSoc.Contains("EnqueueHeroSave(workItem)",
               StringComparison.Ordinal)
           && gameSoc.Contains("ProcessHeroSaveQueue", StringComparison.Ordinal)
           && gameSoc.Contains("RetryCount < 20", StringComparison.Ordinal),
        "native 0x161 inner save and outer durability ACK are not separated");
    Assert(heroData.Contains("h.lvChangeTime=IF", StringComparison.Ordinal)
           && heroData.Contains("h.IsDelete=IF(@absoluteState=1,@isDelete",
               StringComparison.Ordinal)
           && heroData.Contains("h.HeroType=IF(@absoluteState=1,@heroType",
               StringComparison.Ordinal)
           && heroData.Contains("h.Consignation=IF(@absoluteState=1,@consignation",
               StringComparison.Ordinal)
           && gameSoc.Contains("SaveRecordDetailed(item.Index",
               StringComparison.Ordinal)
           && gameSoc.Contains("item.Record, item.PreparedData",
               StringComparison.Ordinal)
           && gameSoc.Contains("lock (_heroCreateLock)", StringComparison.Ordinal)
           && heroData.Contains("preparedStoredData", StringComparison.Ordinal)
            && !gameSoc.Contains("item.RetryCount = 19", StringComparison.Ordinal)
           && gameSoc.Contains("IsDelete = newer.IsDelete", StringComparison.Ordinal)
           && gameSoc.Contains("HeroType = newer.HeroType", StringComparison.Ordinal)
           && gameSoc.Contains("Consignation = newer.Consignation", StringComparison.Ordinal)
           && gameSoc.Contains("IndexJob = newer.IndexJob", StringComparison.Ordinal)
           && heroData.Contains("h.Job=IF(@absoluteState=1,@indexJob",
               StringComparison.Ordinal)
           && gameSoc.Contains("EnsureHeroSaveWorker();", StringComparison.Ordinal)
           && heroData.Contains("heroRecord.IndexExp", StringComparison.Ordinal)
           && heroData.Contains("heroRecord.IndexForceLv", StringComparison.Ordinal)
           && heroData.Contains("heroRecord.IndexForceExp", StringComparison.Ordinal)
           && heroData.Contains("heroRecord.IndexSfLevel", StringComparison.Ordinal),
        "native 0x161 index snapshot fields or SaveMode mutations are incomplete");
    Assert(saveHandler.Contains("TryStageHeroLogicalSnapshot", StringComparison.Ordinal)
           && program.Contains("AddSingleton<NativeHeroLogicalCache>",
               StringComparison.Ordinal)
           && gameSoc.Contains("_heroLogicalCache", StringComparison.Ordinal)
           && !gameSoc.Contains("_heroLogicalSnapshots", StringComparison.Ordinal)
           && gameSoc.Contains("builtSnapshots", StringComparison.Ordinal)
           && gameSoc.Contains("workItem.SetLogicalSnapshot", StringComparison.Ordinal)
           && gameSoc.Contains(
               "Dictionary<string, NativeHeroSaveWorkItem>",
               StringComparison.Ordinal)
           && gameSoc.Contains("EnqueueHeroLifecycleSnapshot",
               StringComparison.Ordinal)
           && gameSoc.Contains("logicalSnapshot.TryRenameHero",
               StringComparison.Ordinal)
           && saveHandler.Contains(
               "request.RawDynamicData ?? Array.Empty<byte>()",
               StringComparison.Ordinal)
           && !saveHandler.Contains(
               "request.RawDynamicData?.Length > 0",
               StringComparison.Ordinal)
           && saveHandler.Contains("if (!EnqueueHeroSave(workItem))",
               StringComparison.Ordinal)
           && saveHandler.IndexOf("if (!EnqueueHeroSave(workItem))",
                  StringComparison.Ordinal)
              < saveHandler.IndexOf("_heroLogicalCache.Set(logicalSnapshot)",
                  StringComparison.Ordinal)
           && saveHandler.IndexOf("SnapshotForSave", StringComparison.Ordinal)
              < saveHandler.IndexOf("_heroDataService.Index", StringComparison.Ordinal)
           && heroData.Contains("logicalSnapshots.TryGetValue", StringComparison.Ordinal)
           && heroData.Contains("CreateBuiltSnapshot", StringComparison.Ordinal),
        "native 0x161/0x167 logical snapshot ordering is incomplete");
    Assert(gameSoc.Contains(
               "command == NativeForceLevelProtocol.RequestCommand",
               StringComparison.Ordinal)
           && gameSoc.Contains("ProcessNativeForceLevel(serverInfo, frame)",
               StringComparison.Ordinal)
           && gameSoc.Contains("_nativeForceLevelService.ApplyDetailed(request)",
               StringComparison.Ordinal)
           && gameSoc.Contains("new NativeSaveWorkItem(mutation)",
               StringComparison.Ordinal)
           && gameSoc.Contains("NativeHeroSaveWorkItem.ForForceLevel(mutation)",
               StringComparison.Ordinal)
           && gameSoc.Contains("NativeForceLevelProtocol.CreateResponse",
               StringComparison.Ordinal)
           && gameSoc.Contains("PersistNativeForceLevel(",
               StringComparison.Ordinal)
           && heroData.Contains("TryApplyIndexForceLevel", StringComparison.Ordinal),
        "native 0x168 production dispatch/queue/persistence path is incomplete");

    var protocolSources = Directory.GetFiles(dbSvr, "*.cs", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(systemModule, "*.cs", SearchOption.AllDirectories));
    var inventedCommand = new Regex(
        @"DB_(?:LOAD|QUERY|NEW|DEL|SAVE)HERORCD\s*=\s*(?:110|111)\b",
        RegexOptions.CultureInvariant);
    foreach (var path in protocolSources)
    {
        Assert(!inventedCommand.IsMatch(File.ReadAllText(path)),
            $"invented hero DB command remains in {Path.GetRelativePath(root, path)}");
    }
}

void CheckFixedRecordMapping()
{
    Equal(0x49D4, NativeHeroDbFrameCodec.HeroRecordSize, "fixed record size");
    Equal(0x54, NativeHeroDbFrameCodec.HeroRecordOffset, "fixed record wire offset");
    Equal(0x4A28, NativeHeroDbFrameCodec.HeroFrameBaseSize, "fixed frame base size");
    Equal((ushort)0x160, NativeHeroDbFrameCodec.LoadCommand, "load command");
    Equal((ushort)0x161, NativeHeroDbFrameCodec.SaveCommand, "save command");
    Equal((ushort)0x51, NativeHeroDbFrameCodec.LoadResponseCommand, "load response command");

    var raw = BuildRecord("主人甲", "英雄乙");
    raw[NativeHeroDbFrameCodec.RaceOffset] = 2;
    raw[NativeHeroDbFrameCodec.SexOffset] = 1;
    raw[NativeHeroDbFrameCodec.JobOffset] = 3;
    BinaryPrimitives.WriteUInt16LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.LevelOffset, 2), 77);
    BinaryPrimitives.WriteInt32LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.GoldOffset, 4), 123456);
    BinaryPrimitives.WriteUInt32LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.ExpOffset, 4), 0xF1234567);
    BinaryPrimitives.WriteUInt16LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.HpLowOffset, 2), 0x5678);
    BinaryPrimitives.WriteUInt16LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.HpHighOffset, 2), 0x1234);
    BinaryPrimitives.WriteUInt16LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.MpLowOffset, 2), 0xDEF0);
    BinaryPrimitives.WriteUInt16LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.MpHighOffset, 2), 0x9ABC);
    BinaryPrimitives.WriteInt32LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.CurrentXOffset, 4), 321);
    BinaryPrimitives.WriteInt32LittleEndian(
        raw.AsSpan(NativeHeroDbFrameCodec.CurrentYOffset, 4), 654);
    raw[NativeHeroDbFrameCodec.BagItemsOffset + 17] = 0xA5;
    raw[NativeHeroDbFrameCodec.NormalMagicOffset + 9] = 0x5A;

    Assert(NativeHeroDbFrameCodec.TryCreateRecord(raw, out var record, out var error), error);
    Equal("主人甲", record.MasterName, "record master");
    Equal("英雄乙", record.HeroName, "record hero");
    Equal((byte)2, record.Race, "record race");
    Equal((byte)1, record.Sex, "record sex");
    Equal((byte)3, record.Job, "record job");
    Equal((ushort)77, record.Level, "record level");
    Equal(123456, record.Gold, "record gold");
    Equal(0xF1234567u, record.Exp, "record exp");
    Equal(0x12345678u, record.Hp, "record HP split words");
    Equal(0x9ABCDEF0u, record.Mp, "record MP split words");
    Equal(321, record.CurrentX, "record X");
    Equal(654, record.CurrentY, "record Y");
    SequenceEqual(raw, record.ToArray(), "fixed record preservation");

    var invalid = (byte[])raw.Clone();
    invalid[NativeHeroDbFrameCodec.MasterNameOffset] = 16;
    Assert(!NativeHeroDbFrameCodec.TryCreateRecord(invalid, out _, out _),
        "oversized fixed-record short string accepted");
    Assert(!NativeHeroDbFrameCodec.TryCreateRecord(raw[..^1], out _, out _),
        "short fixed record accepted");
}

void CheckDynamicDataCodec()
{
    var source = new NativeHeroDynamicData(new[]
    {
        new NativeHeroDynamicSection(2, new byte[] { 1, 2, 3 }),
        new NativeHeroDynamicSection(6, new byte[] { 4, 5 }),
        new NativeHeroDynamicSection(7, new byte[] { 6 })
    });
    Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(source, out var encoded, out var error), error);
    Equal(31, encoded.Length, "dynData encoded size");
    Equal(27u, BinaryPrimitives.ReadUInt32LittleEndian(encoded), "dynData root length");
    Equal(NativeHeroDbFrameCodec.DynamicSectionMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4, 4)), "dynData magic");
    Assert(NativeHeroDbFrameCodec.TryDecodeDynamicData(encoded, out var decoded, out error), error);
    Equal(3, decoded.Sections.Count, "dynData section count");
    Equal((byte)2, decoded.Sections[0].Type, "dynData section 0 type");
    Equal((byte)6, decoded.Sections[1].Type, "dynData section 1 type");
    Equal((byte)7, decoded.Sections[2].Type, "dynData section 2 type");
    SequenceEqual(new byte[] { 1, 2, 3 }, decoded.Sections[0].Payload, "dynData payload");

    Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(
        new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>()), out var empty, out error), error);
    Equal(0, empty.Length, "empty dynData");

    var badLength = (byte[])encoded.Clone();
    BinaryPrimitives.WriteUInt32LittleEndian(badLength, 26);
    Assert(!NativeHeroDbFrameCodec.TryDecodeDynamicData(badLength, out _, out _),
        "dynData root length mismatch accepted");
    var badMagic = (byte[])encoded.Clone();
    badMagic[4] ^= 1;
    Assert(NativeHeroDbFrameCodec.TryDecodeDynamicData(badMagic, out var badMagicDecoded, out _),
        "dynData bad section magic must keep already-parsed sections (0x68B0B9 jne 0x68B396)");
    Equal(0, badMagicDecoded.Sections.Count,
        "first-section bad magic leaves no parsed sections");
    // The section-level guard is what this case is about, so the ROOT length has to stay
    // consistent with the buffer — otherwise the root-length check asserted just above fires
    // first and the native path below is never reached. Native walks sections against the
    // declared root length:
    //   0x68B097 BE 07 00 00 00        mov esi,7                  ; header size / cursor
    //   0x68B0A9 3B F3 / 0F 8D ...     cmp esi,ebx / jge 0x68B3F3 ; ebx = declared length
    //   0x68B0B3 81 38 AA EF CD AB     cmp dword [eax],0xABCDEFAA
    //   0x68B0B9 0F 85 D7 02 00 00     jne 0x68B396               ; bad magic -> log, exit
    //   0x68B0C1 0F B7 40 04           movzx eax,word [eax+4]     ; section payload length
    //   0x68B0C5 03 C6                 add eax,esi
    //   0x68B0C7 3B D8                 cmp ebx,eax
    //   0x68B0C9 0F 8C 85 02 00 00     jl 0x68B354                ; short payload -> log, exit
    // Both exits keep the sections already parsed.
    var truncated = encoded[..^1];
    BinaryPrimitives.WriteUInt32LittleEndian(truncated, (uint)(truncated.Length - 4));
    Assert(NativeHeroDbFrameCodec.TryDecodeDynamicData(truncated, out var truncatedDecoded, out _),
        "truncated dynData must keep already-parsed sections (0x68B0C9 jl 0x68B354)");
    Equal(2, truncatedDecoded.Sections.Count,
        "truncated last section keeps the preceding 2/6");
    Equal((byte)2, truncatedDecoded.Sections[0].Type, "truncated leftover type 2");
    Equal((byte)6, truncatedDecoded.Sections[1].Type, "truncated leftover type 6");
    Assert(!NativeHeroDbFrameCodec.TryEncodeDynamicData(
        new NativeHeroDynamicData(new[]
        {
            new NativeHeroDynamicSection(6, new byte[] { 1 }),
            new NativeHeroDynamicSection(2, new byte[] { 2 })
        }), out _, out _), "out-of-order dynData accepted");

    // Decoder jump table 0x68B0E5 has no order: 7 then 2 must parse.
    var reordered = BuildDynDataBlob(
        (7, new byte[] { 6 }),
        (2, new byte[] { 1, 2, 3 }));
    Assert(NativeHeroDbFrameCodec.TryDecodeDynamicData(reordered, out var reorderedDecoded, out error),
        error);
    Equal(2, reorderedDecoded.Sections.Count, "out-of-order 7-then-2 section count");
    Equal((byte)7, reorderedDecoded.Sections[0].Type, "out-of-order first type");
    Equal((byte)2, reorderedDecoded.Sections[1].Type, "out-of-order second type");

    // Encoder refuses type 4 / 0x0C (0x68AD4F/0x68AD78/0x68ADA3 emit only 2/6/7).
    // Decoder skip 0x68B2EA/0x68B349 keeps the known sections.
    Assert(!NativeHeroDbFrameCodec.TryEncodeDynamicData(
        new NativeHeroDynamicData(new[]
        {
            new NativeHeroDynamicSection(2, new byte[] { 1 }),
            new NativeHeroDynamicSection(4, new byte[] { 2 }),
            new NativeHeroDynamicSection(6, new byte[] { 3 }),
            new NativeHeroDynamicSection(7, new byte[] { 4 }),
            new NativeHeroDynamicSection(0x0C, new byte[] { 5 })
        }), out _, out _), "encoder accepted type 4 / 0x0C");
    var skipBlob = BuildDynDataBlob(
        (2, new byte[] { 1 }),
        (4, new byte[] { 2 }),
        (6, new byte[] { 3 }),
        (7, new byte[] { 4 }),
        (0x0C, new byte[] { 5 }));
    Assert(NativeHeroDbFrameCodec.TryDecodeDynamicData(skipBlob, out var skipDecoded, out error),
        error);
    Equal(3, skipDecoded.Sections.Count, "type4/type12 dynData skip leaves 2/6/7");
    Equal((byte)2, skipDecoded.Sections[0].Type, "skip leftover type 2");
    Equal((byte)6, skipDecoded.Sections[1].Type, "skip leftover type 6");
    Equal((byte)7, skipDecoded.Sections[2].Type, "skip leftover type 7");
}

void CheckLoadRequestCodec()
{
    var source = new NativeHeroLoadRequest
    {
        HeroKind = 1,
        HeroSlot = 2,
        Account = "账号一",
        MasterName = "主人甲"
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeLoadRequest(source, out var frame, out var error), error);
    Equal(0x54, frame.Length, "load request frame size");
    Equal(NativeHeroDbFrameCodec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(frame), "load request magic");
    Equal(0x48, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(8, 4)),
        "load request payload size");
    Equal((ushort)0x160, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
        "load request command bytes");
    Assert(NativeHeroDbFrameCodec.TryDecodeLoadRequest(frame, out var decoded, out error), error);
    Equal(source.HeroKind, decoded.HeroKind, "load request hero kind");
    Equal(source.HeroSlot, decoded.HeroSlot, "load request hero slot");
    Equal(source.Account, decoded.Account, "load request account");
    Equal(source.MasterName, decoded.MasterName, "load request master");

    source.MasterName = new string('A', 16);
    Assert(!NativeHeroDbFrameCodec.TryEncodeLoadRequest(source, out _, out _),
        "oversized load-request master name accepted");
}

void CheckDetachRequestCodec()
{
    var frame = new byte[NativeHeroDbFrameCodec.FrameHeaderSize
                         + NativeHeroDbFrameCodec.MessageHeaderSize];
    BinaryPrimitives.WriteUInt32LittleEndian(frame,
        NativeHeroDbFrameCodec.FrameMagic);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4),
        NativeHeroDbFrameCodec.FrameVersion);
    BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8),
        NativeHeroDbFrameCodec.MessageHeaderSize);
    var message = frame.AsSpan(NativeHeroDbFrameCodec.FrameHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(message,
        NativeHeroDbFrameCodec.DetachCommand);
    BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), 1);
    BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4),
        unchecked((int)0xAABBCC02));
    WriteShortString(frame, NativeHeroDbFrameCodec.FrameHeaderSize + 16,
        20, "账号一");
    WriteShortString(frame, NativeHeroDbFrameCodec.FrameHeaderSize + 37,
        15, "主人甲");

    Assert(NativeHeroDbFrameCodec.TryDecodeDetachRequest(frame,
        out var request, out var error), error);
    Equal((ushort)1, request.HeroKind, "detach HeroKind");
    Equal((byte)2, request.Mode, "detach uses only Mode low byte");
    Equal("账号一", request.Account, "detach account");
    Equal("主人甲", request.MasterName, "detach master");

    var alternateKind = (byte[])frame.Clone();
    BinaryPrimitives.WriteUInt16LittleEndian(alternateKind.AsSpan(14), 2);
    Assert(NativeHeroDbFrameCodec.TryDecodeDetachRequest(alternateKind,
        out var decodedAlternate, out error), error);
    Equal((ushort)2, decodedAlternate.HeroKind,
        "detach preserves non-special HeroKind values");

    var badTerminator = (byte[])frame.Clone();
    badTerminator[NativeHeroDbFrameCodec.FrameHeaderSize + 53] = 1;
    Assert(!NativeHeroDbFrameCodec.TryDecodeDetachRequest(badTerminator,
        out _, out _), "detach accepted a nonzero M2 stack terminator");
}

void CheckSaveRequestCodec()
{
    var raw = BuildRecord("主人甲", "英雄乙");
    raw[NativeHeroDbFrameCodec.BagItemsOffset + 37] = 0x77;
    Assert(NativeHeroDbFrameCodec.TryCreateRecord(raw, out var record, out var error), error);
    var dyn = BuildDynamicData();
    Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(dyn, out var dynBytes, out error), error);
    var source = new NativeHeroSaveRequest
    {
        SaveMode = 3,
        Param1 = 0x11223344,
        Param2 = 0x55667788,
        Record = record,
        DynamicData = dyn
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeSaveRequest(source, out var frame, out error), error);
    Equal(0x4A28 + dynBytes.Length, frame.Length, "save frame size");
    Equal(frame.Length - 12, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(8, 4)),
        "save payload size");
    Equal((ushort)0x161, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
        "save command bytes");
    SequenceEqual(raw, frame.AsSpan(0x54, 0x49D4).ToArray(), "save record offset");
    SequenceEqual(dynBytes, frame.AsSpan(0x4A28).ToArray(), "save dynData offset");
    Equal("主人甲", ReadFrameShortString(frame, 12 + 37),
        "save message +25 MasterName");
    Equal("英雄乙", ReadFrameShortString(frame, 12 + 53),
        "save message +35 HeroName");
    Assert(NativeHeroDbFrameCodec.TryDecodeSaveRequest(frame, out var decoded, out error), error);
    Equal(source.SaveMode, decoded.SaveMode, "save mode");
    Equal(source.Param1, decoded.Param1, "save param1");
    Equal(source.Param2, decoded.Param2, "save param2");
    Equal("主人甲", decoded.MasterName, "save decoded message MasterName");
    Equal("英雄乙", decoded.HeroName, "save decoded message HeroName");
    SequenceEqual(raw, decoded.Record.ToArray(), "save record round trip");
    SequenceEqual(dynBytes, decoded.RawDynamicData, "save raw dynData round trip");

    var mismatch = (byte[])frame.Clone();
    mismatch[12 + 37 + 1] ^= 1;
    mismatch[12 + 4] = 0xA5;
    mismatch[12 + 16] = 0xB6;
    mismatch[12 + 69] = 0xC7;
    Assert(NativeHeroDbFrameCodec.TryDecodeSaveRequest(
        mismatch, out var mismatched, out error), error);
    Assert(!string.Equals(mismatched.MasterName, mismatched.Record.MasterName,
            StringComparison.Ordinal),
        "save fixture did not create a message/record name mismatch");

    var opaqueDynamic = (byte[])frame.Clone();
    opaqueDynamic[NativeHeroDbFrameCodec.HeroFrameBaseSize] ^= 0xFF;
    Assert(NativeHeroDbFrameCodec.TryDecodeSaveRequest(
        opaqueDynamic, out var opaqueDecoded, out error), error);
    Equal(dynBytes.Length, opaqueDecoded.RawDynamicData.Length,
        "opaque save dynData length");
    Equal(0, opaqueDecoded.DynamicData.Sections.Count,
        "opaque save dynData was incorrectly interpreted as valid sections");

    var indexRaw = BuildRecord("主人甲", "英雄乙");
    BinaryPrimitives.WriteUInt32LittleEndian(indexRaw.AsSpan(
        NativeHeroDbFrameCodec.IndexExpOffset, 4), 0xF1234567);
    BinaryPrimitives.WriteUInt16LittleEndian(indexRaw.AsSpan(
        NativeHeroDbFrameCodec.IndexSfLevelOffset, 2), 0xA1B2);
    BinaryPrimitives.WriteUInt16LittleEndian(indexRaw.AsSpan(
        NativeHeroDbFrameCodec.IndexForceLvOffset, 2), 0xC3D4);
    BinaryPrimitives.WriteUInt32LittleEndian(indexRaw.AsSpan(
        NativeHeroDbFrameCodec.IndexForceExpOffset, 4), 0xE5F60718);
    Assert(NativeHeroDbFrameCodec.TryCreateRecord(
        indexRaw, out var indexRecord, out error), error);
    Equal(0xF1234567u, indexRecord.IndexExp, "save index Exp offset");
    Equal((ushort)0xA1B2, indexRecord.IndexSfLevel, "save index sfLevel offset");
    Equal((ushort)0xC3D4, indexRecord.IndexForceLv, "save index ForceLv offset");
    Equal(0xE5F60718u, indexRecord.IndexForceExp, "save index ForceExp offset");
}

void CheckLoadResponseCodec()
{
    var raw = BuildRecord("主人甲", "英雄乙");
    raw[NativeHeroDbFrameCodec.SpecialMagicOffset + 11] = 0xCC;
    Assert(NativeHeroDbFrameCodec.TryCreateRecord(raw, out var record, out var error), error);
    var dyn = BuildDynamicData();
    Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(dyn, out var dynBytes, out error), error);
    var success = new NativeHeroLoadResponse
    {
        Status = 1,
        Record = record,
        DynamicData = dyn
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeLoadResponse(success, out var frame, out error), error);
    Equal(0x4A28 + dynBytes.Length, frame.Length, "load response frame size");
    Equal((ushort)0x51, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
        "load response command bytes");
    Equal(dynBytes.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)),
        "load response dynData size");
    Assert(NativeHeroDbFrameCodec.TryDecodeLoadResponse(frame, out var decoded, out error), error);
    Equal((ushort)1, decoded.Status, "load response status");
    Equal("英雄乙", decoded.HeroName, "load response hero");
    SequenceEqual(raw, decoded.Record.ToArray(), "load response record round trip");

    var opaque = Convert.FromHexString("DEADBEEF00");
    success.RawDynamicData = opaque;
    Assert(NativeHeroDbFrameCodec.TryEncodeLoadResponse(
        success, out var opaqueFrame, out error), error);
    Assert(NativeHeroDbFrameCodec.TryDecodeLoadResponse(
        opaqueFrame, out var opaqueResponse, out error), error);
    SequenceEqual(opaque, opaqueResponse.RawDynamicData,
        "load response opaque dynData round trip");
    Equal(0, opaqueResponse.DynamicData.Sections.Count,
        "load response opaque dynData invented sections");

    var failure = new NativeHeroLoadResponse { Status = 13, MasterName = "主人甲" };
    Assert(NativeHeroDbFrameCodec.TryEncodeLoadResponse(failure, out var failedFrame, out error), error);
    Equal(0x54, failedFrame.Length, "failed load response size");
    Assert(NativeHeroDbFrameCodec.TryDecodeLoadResponse(failedFrame, out var failed, out error), error);
    Equal((ushort)13, failed.Status, "failed load response status");
    Equal("主人甲", failed.MasterName, "failed load response master");
}

void CheckCreateCodec()
{
    var request = new NativeHeroCreateRequest
    {
        HeroType = 1,
        Code = 6,
        Account = "ignored-account",
        MasterName = "主人甲",
        HeroName = "英雄乙"
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeCreateRequest(
        request, out var frame, out var error), error);

    Array.Resize(ref frame, frame.Length + 1);
    BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8, 4),
        NativeHeroDbFrameCodec.MessageHeaderSize + 1);
    frame[12 + 2] = 1;
    frame[12 + 3] = 0xA5;
    frame[12 + 4] = 6;
    frame[12 + 5] = 0xDE;
    frame[12 + 6] = 0xAD;
    frame[12 + 7] = 0xBE;
    frame[12 + 16] = 0xFF;
    frame[^1] = 0x7A;
    Assert(NativeHeroDbFrameCodec.TryDecodeCreateRequest(
        frame, out var decoded, out error), error);
    Equal((ushort)1, decoded.HeroType, "create low-byte HeroType");
    Equal(6, decoded.Code, "create low-byte Code");
    Equal(string.Empty, decoded.Account, "create ignored Account");
    Equal(request.MasterName, decoded.MasterName, "create MasterName");
    Equal(request.HeroName, decoded.HeroName, "create HeroName");

    frame[12 + 2] = 0;
    frame[12 + 4] = 7;
    Assert(NativeHeroDbFrameCodec.TryDecodeCreateRequest(
        frame, out decoded, out error), error);
    Equal((ushort)0, decoded.HeroType, "invalid create HeroType still decoded");
    Equal(7, decoded.Code, "invalid create Code still decoded");
    var invalidResponse = new NativeHeroCreateResponse
    {
        HeroType = decoded.HeroType,
        Result = -5,
        MasterName = decoded.MasterName,
        HeroName = decoded.HeroName
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeCreateResponse(
        invalidResponse, out var responseFrame, out error), error);
    Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(
        responseFrame.AsSpan(12 + 2, 2)), "invalid create response HeroType");
    Equal(-5, BinaryPrimitives.ReadInt32LittleEndian(
        responseFrame.AsSpan(12 + 4, 4)), "invalid create response result");
    Assert(NativeHeroDbFrameCodec.TryDecodeCreateResponse(
        responseFrame, out var decodedResponse, out error), error);
    Equal(-5, decodedResponse.Result, "decoded invalid create response result");
    Assert(!NativeHeroDbFrameCodec.TryEncodeCreateResponse(
        new NativeHeroCreateResponse
        {
            HeroType = 0x100,
            Result = -5,
            MasterName = request.MasterName,
            HeroName = request.HeroName
        }, out _, out _), "create response accepted a non-byte HeroType");

    for (ushort heroType = 1; heroType <= 2; heroType++)
    for (var code = 1; code <= 6; code++)
    {
        var initialRequest = new NativeHeroCreateRequest
        {
            HeroType = heroType,
            Code = code,
            MasterName = request.MasterName,
            HeroName = request.HeroName
        };
        Assert(NativeHeroDbFrameCodec.TryCreateInitialRecord(
            initialRequest, out var record, out error), error);
        var expected = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(4, 2),
            NativeHeroDbFrameCodec.HeroRecordSize);
        WriteShortString(expected, NativeHeroDbFrameCodec.MasterNameOffset,
            15, request.MasterName);
        WriteShortString(expected, NativeHeroDbFrameCodec.HeroNameOffset,
            15, request.HeroName);
        expected[NativeHeroDbFrameCodec.RaceOffset] = 1;
        expected[NativeHeroDbFrameCodec.SexOffset] = (byte)((code - 1) / 3);
        expected[NativeHeroDbFrameCodec.JobOffset] = (byte)((code - 1) % 3);
        expected[NativeHeroDbFrameCodec.HeroTypeOffset] = (byte)heroType;
        SequenceEqual(expected, record.ToArray(),
            $"initial hero record type={heroType} code={code}");
    }
}

void CheckMalformedFrames()
{
    Assert(NativeHeroDbFrameCodec.TryEncodeLoadRequest(new NativeHeroLoadRequest
    {
        HeroKind = 1,
        HeroSlot = 0,
        Account = "account",
        MasterName = "master"
    }, out var valid, out var error), error);

    var badMagic = (byte[])valid.Clone();
    badMagic[0] ^= 1;
    Assert(!NativeHeroDbFrameCodec.TryDecodeLoadRequest(badMagic, out _, out _),
        "bad frame magic accepted");
    var badVersion = (byte[])valid.Clone();
    badVersion[4] = 2;
    Assert(!NativeHeroDbFrameCodec.TryDecodeLoadRequest(badVersion, out _, out _),
        "bad frame version accepted");
    var badReserved = (byte[])valid.Clone();
    badReserved[6] = 1;
    Assert(NativeHeroDbFrameCodec.TryDecodeLoadRequest(
        badReserved, out _, out error), error);
    var badLength = (byte[])valid.Clone();
    BinaryPrimitives.WriteInt32LittleEndian(badLength.AsSpan(8, 4), 71);
    Assert(!NativeHeroDbFrameCodec.TryDecodeLoadRequest(badLength, out _, out _),
        "bad frame length accepted");
    var stackGarbage = (byte[])valid.Clone();
    stackGarbage[12 + 8] = 1;
    stackGarbage[12 + 54] = 2;
    stackGarbage[12 + 71] = 3;
    Assert(NativeHeroDbFrameCodec.TryDecodeLoadRequest(
        stackGarbage, out var garbageDecoded, out error), error);
    Equal("account", garbageDecoded.Account, "load request with stack garbage account");
    Equal("master", garbageDecoded.MasterName, "load request with stack garbage master");
    var badTerminator = (byte[])valid.Clone();
    badTerminator[12 + 53] = 1;
    Assert(!NativeHeroDbFrameCodec.TryDecodeLoadRequest(badTerminator, out _, out _),
        "bad load-request string terminator accepted");
    Assert(!NativeHeroDbFrameCodec.TryDecodeLoadRequest(valid[..^1], out _, out _),
        "truncated frame accepted");
}

void CheckBlobEnvelopeCodec()
{
    var raw = BuildRecord("主人甲", "英雄乙");
    for (var i = 0x120; i < raw.Length; i += 257) raw[i] = (byte)i;
    Assert(NativeHeroBlobCodec.TryEncodeDataBlob(raw, out var blob, out var error), error);
    Equal(0, blob.Length & 0xFF, "compressed Data Blob alignment");
    Equal((ushort)raw.Length,
        BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(4, 2)), "Data Blob total marker");
    var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(6, 2));
    Assert(compressedLength > 0, "compressible Data Blob was stored uncompressed");
    Equal(NativeHeroBlobCodec.ComputeNativeCrc(blob.AsSpan(8, compressedLength)),
        BinaryPrimitives.ReadUInt32LittleEndian(blob), "Data Blob native CRC");
    Assert(NativeHeroBlobCodec.TryDecodeDataBlob(blob, out var decoded, out error), error);
    SequenceEqual(raw, decoded, "Data Blob round trip");

    var three = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    raw.CopyTo(three, 0);
    BuildRecord("主人甲", "英雄丙").CopyTo(three, raw.Length);
    BuildRecord(string.Empty, string.Empty).CopyTo(three, raw.Length * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(three.AsSpan(4, 2), (ushort)three.Length);
    Assert(NativeHeroBlobCodec.TryEncodeDataBlob(three, out var threeBlob, out error), error);
    Assert(NativeHeroBlobCodec.TryDecodeDataBlob(threeBlob, out var threeDecoded, out error), error);
    SequenceEqual(three, threeDecoded, "three-slot Data Blob round trip");

    var uncompressedData = (byte[])raw.Clone();
    Assert(NativeHeroBlobCodec.TryDecodeDataBlob(uncompressedData, out decoded, out error), error);
    SequenceEqual(raw, decoded, "uncompressed Data Blob fixture");

    var incompressible = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    new Random(0x161).NextBytes(incompressible);
    BinaryPrimitives.WriteUInt32LittleEndian(incompressible.AsSpan(0, 4), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(incompressible.AsSpan(4, 2),
        (ushort)incompressible.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(incompressible.AsSpan(6, 2), 0);
    WriteShortString(incompressible, NativeHeroDbFrameCodec.MasterNameOffset, 15, "主人甲");
    WriteShortString(incompressible, NativeHeroDbFrameCodec.HeroNameOffset, 15, "英雄乙");
    Assert(NativeHeroBlobCodec.TryEncodeDataBlob(
        incompressible, out var incompressibleBlob, out error), error);
    Equal(incompressible.Length, incompressibleBlob.Length,
        "incompressible Data Blob keeps its embedded header size");
    Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(
        incompressibleBlob.AsSpan(6, 2)), "incompressible Data Blob marker");
    SequenceEqual(incompressible, incompressibleBlob,
        "incompressible Data Blob raw fallback");
    Assert(NativeHeroBlobCodec.TryDecodeDataBlob(
        incompressibleBlob, out decoded, out error), error);
    SequenceEqual(incompressible, decoded, "incompressible Data Blob round trip");

    var dyn = BuildDynamicData();
    Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(dyn, out var dynRaw, out error), error);
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(dynRaw, out var dynBlob, out error), error);
    Equal(0x100, dynBlob.Length,
        "short dynData Blob uses native 256-byte allocation");
    Equal((ushort)dynRaw.Length,
        BinaryPrimitives.ReadUInt16LittleEndian(dynBlob.AsSpan(4, 2)), "dynData payload marker");
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(dynBlob, out var dynDecoded, out error), error);
    SequenceEqual(dynRaw, dynDecoded, "dynData Blob round trip");

    var zlib104Fixture = Convert.FromHexString(
        "78DA1366606058F5FEEC6A2606260141108399815D41510900544A06C8");
    var fixtureBlob = new byte[0x100];
    BinaryPrimitives.WriteUInt32LittleEndian(fixtureBlob,
        NativeHeroBlobCodec.ComputeNativeCrc(zlib104Fixture));
    BinaryPrimitives.WriteUInt16LittleEndian(fixtureBlob.AsSpan(4, 2), (ushort)dynRaw.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(fixtureBlob.AsSpan(6, 2),
        (ushort)zlib104Fixture.Length);
    zlib104Fixture.CopyTo(fixtureBlob, NativeHeroBlobCodec.HeaderSize);
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(fixtureBlob,
        out var fixtureDecoded, out error), error);
    SequenceEqual(dynRaw, fixtureDecoded, "zlib 1.0.4-compatible dynData fixture");

    var largeDyn = new NativeHeroDynamicData(new[]
    {
        new NativeHeroDynamicSection(2, new byte[4096])
    });
    Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(largeDyn, out var largeDynRaw, out error), error);
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(largeDynRaw, out var largeDynBlob, out error), error);
    Equal(0, largeDynBlob.Length & 0xFF, "compressed dynData Blob alignment");
    Assert(BinaryPrimitives.ReadUInt16LittleEndian(largeDynBlob.AsSpan(6, 2)) > 0,
        "large dynData Blob was not compressed");
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(largeDynBlob,
        out var largeDynDecoded, out error), error);
    SequenceEqual(largeDynRaw, largeDynDecoded, "compressed dynData Blob round trip");

    var corrupt = (byte[])blob.Clone();
    corrupt[0] ^= 1;
    Assert(!NativeHeroBlobCodec.TryDecodeDataBlob(corrupt, out _, out _),
        "Data Blob CRC corruption accepted");
    corrupt = (byte[])blob.Clone();
    corrupt[^1] = 1;
    Assert(!NativeHeroBlobCodec.TryDecodeDataBlob(corrupt, out _, out _),
        "Data Blob nonzero padding accepted");
    Assert(!NativeHeroBlobCodec.TryDecodeDataBlob(blob[..^1], out _, out _),
        "Data Blob non-native stored length accepted");
    Assert(!NativeHeroBlobCodec.TryEncodeDataBlob(raw[..^1], out _, out _),
        "short Data payload accepted");
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(Array.Empty<byte>(),
        out var emptyDyn, out error), error);
    Equal(0, emptyDyn.Length, "empty dynData SQL Blob");

    var opaque = Convert.FromHexString("DEADBEEF00");
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(
        opaque, out var opaqueBlob, out error), error);
    Equal(0x100, opaqueBlob.Length, "opaque dynData native allocation");
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(
        opaqueBlob, out var opaqueRoundTrip, out error), error);
    SequenceEqual(opaque, opaqueRoundTrip, "opaque dynData round trip");
    opaqueBlob[^1] = 0x7F;
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(
        opaqueBlob, out opaqueRoundTrip, out error), error);
    SequenceEqual(opaque, opaqueRoundTrip,
        "uncompressed dynData ignores nonzero trailing allocation bytes");

    var thresholdLow = new byte[1016];
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(
        thresholdLow, out var thresholdLowBlob, out error), error);
    Equal(0x400, thresholdLowBlob.Length, "dynData raw 1016 allocation");
    Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(
        thresholdLowBlob.AsSpan(6, 2)), "dynData raw 1016 must remain uncompressed");
    var thresholdHigh = new byte[1017];
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(
        thresholdHigh, out var thresholdHighBlob, out error), error);
    Equal(0x100, thresholdHighBlob.Length, "dynData raw 1017 compressed allocation");
    Assert(BinaryPrimitives.ReadUInt16LittleEndian(
        thresholdHighBlob.AsSpan(6, 2)) > 0,
        "dynData raw 1017 must attempt compression");
    thresholdHighBlob[^1] = 0x6A;
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(
        thresholdHighBlob, out var thresholdRoundTrip, out error), error);
    SequenceEqual(thresholdHigh, thresholdRoundTrip,
        "compressed dynData ignores nonzero trailing allocation bytes");

    var oversizedPayload = new byte[65524];
    new Random(0x51).NextBytes(oversizedPayload);
    Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(
        new NativeHeroDynamicData(new[]
        {
            new NativeHeroDynamicSection(2, oversizedPayload)
        }), out var oversizedDyn, out error), error);
    Equal(ushort.MaxValue, oversizedDyn.Length, "maximum dynData marker size");
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(
        oversizedDyn, out var maxDynBlob, out error), error);
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(
        maxDynBlob, out var maxDynRoundTrip, out error), error);
    SequenceEqual(oversizedDyn, maxDynRoundTrip,
        "65535-byte dynData round trip");

    var wrappedZero = new byte[65536];
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(
        wrappedZero, out var wrappedZeroBlob, out error), error);
    Equal(0x100, wrappedZeroBlob.Length, "65536-byte dynData stored length");
    Equal(0x1B48B480u, BinaryPrimitives.ReadUInt32LittleEndian(wrappedZeroBlob),
        "65536-byte dynData native CRC");
    Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(
        wrappedZeroBlob.AsSpan(4, 2)), "65536-byte dynData wrapped marker");
    Equal((ushort)8, BinaryPrimitives.ReadUInt16LittleEndian(
        wrappedZeroBlob.AsSpan(6, 2)), "65536-byte dynData compressed size");
    SequenceEqual(Convert.FromHexString("78DA030000000001"),
        wrappedZeroBlob.AsSpan(8, 8).ToArray(), "65536-byte dynData zlib bytes");
    Assert(!NativeHeroBlobCodec.TryDecodeDynamicBlob(
        wrappedZeroBlob, out _, out _),
        "65536-byte dynData original unreadable wrapped marker accepted");

    var wrappedOne = new byte[65537];
    wrappedOne[0] = 0xA5;
    Assert(NativeHeroBlobCodec.TryEncodeDynamicBlob(
        wrappedOne, out var wrappedOneBlob, out error), error);
    Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(
        wrappedOneBlob.AsSpan(4, 2)), "65537-byte dynData wrapped marker");
    Assert(NativeHeroBlobCodec.TryDecodeDynamicBlob(
        wrappedOneBlob, out var wrappedOneDecoded, out error), error);
    SequenceEqual(new byte[] { 0xA5 }, wrappedOneDecoded,
        "65537-byte dynData persists only the wrapped marker prefix");
}

void CheckDataRecordMerge()
{
    var slot0 = BuildRecord("主人甲", "英雄甲");
    var slot1 = BuildRecord("主人甲", "英雄乙");
    var slot2 = BuildRecord("主人甲", "英雄丙");
    slot0[NativeHeroDbFrameCodec.JobOffset] = 0;
    slot1[NativeHeroDbFrameCodec.JobOffset] = 1;
    slot2[NativeHeroDbFrameCodec.JobOffset] = 2;
    var existing = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    slot0.CopyTo(existing, 0);
    slot1.CopyTo(existing, NativeHeroDbFrameCodec.HeroRecordSize);
    slot2.CopyTo(existing, NativeHeroDbFrameCodec.HeroRecordSize * 2);

    var replacement = BuildRecord("主人甲", "英雄新");
    replacement[NativeHeroDbFrameCodec.JobOffset] = 1;
    replacement[NativeHeroDbFrameCodec.BagItemsOffset + 5] = 0xA7;
    Assert(NativeHeroBlobCodec.TryMergeDataRecord(
        existing, replacement, true, out var merged, out var error), error);
    Equal(NativeHeroBlobCodec.ThreeHeroRecordSize, merged.Length,
        "three-record merge size");
    SequenceEqual(slot0, merged.AsSpan(0,
        NativeHeroDbFrameCodec.HeroRecordSize).ToArray(), "three-record slot 0 changed");
    SequenceEqual(replacement, merged.AsSpan(NativeHeroDbFrameCodec.HeroRecordSize,
        NativeHeroDbFrameCodec.HeroRecordSize).ToArray(), "three-record slot 1 not replaced");
    SequenceEqual(slot2, merged.AsSpan(NativeHeroDbFrameCodec.HeroRecordSize * 2,
        NativeHeroDbFrameCodec.HeroRecordSize).ToArray(), "three-record slot 2 changed");

    Assert(NativeHeroBlobCodec.TryMergeDataRecord(
        slot0, replacement, out var single, out error), error);
    SequenceEqual(replacement, single, "single-record save was not replaced atomically");
    Assert(!NativeHeroBlobCodec.TryMergeDataRecord(
        slot0, replacement, true, out _, out _),
        "special hero save accepted a single-record Data payload");
    Assert(!NativeHeroBlobCodec.TryMergeDataRecord(
        Array.Empty<byte>(), replacement, true, out _, out _),
        "special hero save created a single-record Data payload from an empty row");

    Assert(NativeHeroBlobCodec.TrySelectDataRecord(
        existing, 2, true, out var selected, out error), error);
    SequenceEqual(slot2, selected, "special hero slot 2 selection");
    Assert(NativeHeroBlobCodec.TrySelectDataRecord(
        existing, 0x100, true, out selected, out error), error);
    SequenceEqual(slot0, selected, "three-record selection did not use the low slot byte");
    Assert(!NativeHeroBlobCodec.TrySelectDataRecord(
        slot0, 0, true, out _, out _),
        "special hero load accepted a single-record Data payload");
    Assert(!NativeHeroBlobCodec.TrySelectDataRecord(
        existing, 3, true, out _, out _), "three-record load slot 3 was accepted");

    var invalidSlot = (byte[])replacement.Clone();
    invalidSlot[NativeHeroDbFrameCodec.JobOffset] = 3;
    Assert(!NativeHeroBlobCodec.TryMergeDataRecord(
        existing, invalidSlot, out _, out _), "three-record slot 3 was accepted");
}

void CheckThreeSlotBuildData()
{
    for (byte sourceJob = 0; sourceJob < 3; sourceJob++)
    {
        var lower = BuildPatternRecord(0x1670 + sourceJob,
            "主人甲", "低级英雄", sourceJob);
        var higher = BuildPatternRecord(0x1700 + sourceJob,
            "主人甲", "高级英雄", (byte)((sourceJob + 1) % 3));
        var originalHigher = (byte[])higher.Clone();

        Assert(NativeHeroBlobCodec.TryBuildThreeSlotData(
            lower, higher, out var actual, out var rankedHigher, out var error), error);
        var expected = BuildExpectedThreeSlot(lower);
        SequenceEqual(expected, actual,
            $"0x0167 three-slot conversion differs for source Job={sourceJob}");

        originalHigher[NativeHeroDbFrameCodec.HeroRankOffset] = 1;
        SequenceEqual(originalHigher, rankedHigher,
            $"0x0167 higher record rank conversion differs for source Job={sourceJob}");

        for (var slot = 0; slot < 3; slot++)
        {
            var offset = slot * NativeHeroDbFrameCodec.HeroRecordSize;
            Equal((byte)slot, actual[offset + NativeHeroDbFrameCodec.JobOffset],
                $"0x0167 slot {slot} Job");
            Equal((byte)2, actual[offset + NativeHeroDbFrameCodec.HeroRankOffset],
                $"0x0167 slot {slot} rank");
            if (slot == sourceJob) continue;
            Equal((byte)0, actual[offset + 0x2B], $"0x0167 slot {slot} gap 2B");
            Equal((byte)0, actual[offset + 0xAC], $"0x0167 slot {slot} gap AC");
            Equal((byte)0, actual[offset + 0xBD], $"0x0167 slot {slot} gap BD");
            Equal((byte)0, actual[offset + 0xBF], $"0x0167 slot {slot} gap BF");
            Equal((byte)0, actual[offset + 0x16C], $"0x0167 slot {slot} private gap");
            var masterPadding = offset + NativeHeroDbFrameCodec.MasterNameOffset
                                + 1 + actual[offset + NativeHeroDbFrameCodec.MasterNameOffset];
            Equal((byte)0, actual[masterPadding],
                $"0x0167 slot {slot} master-name padding");
            var heroPadding = offset + NativeHeroDbFrameCodec.HeroNameOffset
                              + 1 + actual[offset + NativeHeroDbFrameCodec.HeroNameOffset];
            Equal((byte)0, actual[heroPadding],
                $"0x0167 slot {slot} hero-name padding");
        }

        Assert(NativeHeroBlobCodec.TryEncodeDataBlob(
            actual, out var blob, out error), error);
        Assert(NativeHeroBlobCodec.TryDecodeDataBlob(
            blob, out var roundTrip, out error), error);
        var storedExpected = (byte[])actual.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(storedExpected.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(storedExpected.AsSpan(4, 2),
            (ushort)NativeHeroBlobCodec.ThreeHeroRecordSize);
        BinaryPrimitives.WriteUInt16LittleEndian(storedExpected.AsSpan(6, 2), 0);
        SequenceEqual(storedExpected, roundTrip,
            $"0x0167 three-slot Blob round trip differs for source Job={sourceJob}");
        Equal((ushort)NativeHeroBlobCodec.ThreeHeroRecordSize,
            BinaryPrimitives.ReadUInt16LittleEndian(roundTrip.AsSpan(4, 2)),
            "0x0167 three-slot first envelope marker");
        Equal((ushort)NativeHeroDbFrameCodec.HeroRecordSize,
            BinaryPrimitives.ReadUInt16LittleEndian(roundTrip.AsSpan(
                NativeHeroDbFrameCodec.HeroRecordSize + 4, 2)),
            "0x0167 three-slot second record marker");
    }

    var invalidJob = BuildPatternRecord(0x167F,
        "主人甲", "异常职业", 3);
    var validHigher = BuildPatternRecord(0x1680,
        "主人甲", "高级英雄", 1);
    Assert(NativeHeroBlobCodec.TryBuildThreeSlotData(
        invalidJob, validHigher, out var sparse, out _, out var sparseError), sparseError);
    var expectedSparse = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    BinaryPrimitives.WriteUInt16LittleEndian(expectedSparse.AsSpan(4, 2),
        (ushort)NativeHeroBlobCodec.ThreeHeroRecordSize);
    for (var slot = 0; slot < 3; slot++)
        expectedSparse[slot * NativeHeroDbFrameCodec.HeroRecordSize
                       + NativeHeroDbFrameCodec.JobOffset] = (byte)slot;
    SequenceEqual(expectedSparse, sparse,
        "0x0167 invalid source Job did not preserve the original sparse-success quirk");

    var alreadyThree = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    Assert(!NativeHeroBlobCodec.TryBuildThreeSlotData(
        alreadyThree, validHigher, out _, out _, out _),
        "0x0167 accepted a lower hero that was already a three-record payload");

    var higherThree = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    for (var slot = 0; slot < 3; slot++)
        BuildPatternRecord(0x1690 + slot, "主人甲", $"高英{slot}", (byte)slot)
            .CopyTo(higherThree, slot * NativeHeroDbFrameCodec.HeroRecordSize);
    Assert(NativeHeroBlobCodec.TryBuildThreeSlotData(
        validHigher, higherThree, out _, out var unchangedHigher, out var higherError),
        higherError);
    SequenceEqual(higherThree, unchangedHigher,
        "0x0167 changed an already-three-record higher hero instead of preserving it");
}

void CheckLogicalSnapshot()
{
    var selected = BuildPatternRecord(0x2710, "缓存主人", "缓存英雄", 1);
    BinaryPrimitives.WriteUInt16LittleEndian(selected.AsSpan(
        NativeHeroDbFrameCodec.IndexForceLvOffset, 2), 0x1111);
    var three = BuildExpectedThreeSlot(selected);
    var snapshot = new NativeHeroLogicalSnapshot(77, "缓存主人", "缓存英雄",
        selected, three, Array.Empty<byte>(), false, 2, 1, byte.MaxValue,
        88, 99, 1, 0x1111, 123, 7);
    Assert(snapshot.TryWithForceLevel(0xBEEF,
        out var forced, out var error), error);
    Equal((ushort)0x1111, BinaryPrimitives.ReadUInt16LittleEndian(
            snapshot.Record.AsSpan(NativeHeroDbFrameCodec.IndexForceLvOffset, 2)),
        "logical snapshot input record was modified");
    for (var slot = 0; slot < 3; slot++)
        Equal((ushort)0xBEEF, BinaryPrimitives.ReadUInt16LittleEndian(
                forced.Data.AsSpan(slot * NativeHeroDbFrameCodec.HeroRecordSize
                                   + NativeHeroDbFrameCodec.IndexForceLvOffset, 2)),
            "logical snapshot ForceLv was not copied to every slot");
    Equal((ushort)0xBEEF, forced.ForceLevel,
        "logical snapshot ForceLv metadata");
    var clone = forced.CloneSnapshot();
    clone.Data[0] ^= 0xFF;
    Assert(clone.Data[0] != forced.Data[0],
        "logical snapshot clone shares mutable Data");

    var cache = new NativeHeroLogicalCache();
    cache.Set(snapshot);
    snapshot.Data[0] ^= 0xFF;
    Assert(cache.TryGet(77, out var cached),
        "logical snapshot cache lost an inserted entry");
    Assert(cached.Data[0] != snapshot.Data[0],
        "logical snapshot cache retained the caller's mutable Data");
    Assert(cache.TryApplyForceLevel(77, 0xCAFE,
            out var cachedForced, out error), error);
    Assert(cachedForced != null,
        "logical snapshot cache rejected a valid ForceLv update");
    for (var slot = 0; slot < 3; slot++)
        Equal((ushort)0xCAFE, BinaryPrimitives.ReadUInt16LittleEndian(
                cachedForced.Data.AsSpan(
                    slot * NativeHeroDbFrameCodec.HeroRecordSize
                    + NativeHeroDbFrameCodec.IndexForceLvOffset, 2)),
            "logical snapshot cache did not update every ForceLv slot");
    var all = cache.SnapshotAll();
    all[77].Data[0] ^= 0xFF;
    Assert(cache.TryGet(77, out var cachedAgain)
           && cachedAgain.Data[0] != all[77].Data[0],
        "logical snapshot cache exposed mutable SnapshotAll state");

    Assert(snapshot.TryRenameHero("新缓存英雄",
        out var renamed, out error), error);
    Equal("新缓存英雄", renamed.HeroName,
        "logical snapshot rename metadata");
    Assert(NativeHeroDbFrameCodec.TryCreateRecord(renamed.Record,
        out var renamedRecord, out error), error);
    Equal("新缓存英雄", renamedRecord.HeroName,
        "logical snapshot selected record rename");
    for (var slot = 0; slot < 3; slot++)
    {
        var renamedSlot = renamed.Data.AsSpan(
            slot * NativeHeroDbFrameCodec.HeroRecordSize,
            NativeHeroDbFrameCodec.HeroRecordSize).ToArray();
        Assert(NativeHeroDbFrameCodec.TryCreateRecord(renamedSlot,
            out var slotRecord, out error), error);
        Equal("新缓存英雄", slotRecord.HeroName,
            "logical snapshot three-slot rename");
    }
    var deleted = renamed.WithIndexState(true, 2, 1);
    Assert(deleted.IsDelete && deleted.HeroType == 2
           && deleted.Consignation == 1,
        "logical snapshot lifecycle state update");
}

byte[] BuildExpectedThreeSlot(byte[] lower)
{
    var source = (byte[])lower.Clone();
    source[NativeHeroDbFrameCodec.HeroRankOffset] = 2;
    var result = new byte[NativeHeroBlobCodec.ThreeHeroRecordSize];
    var sourceJob = source[NativeHeroDbFrameCodec.JobOffset];
    for (var slot = 0; slot < 3; slot++)
    {
        var destination = result.AsSpan(
            slot * NativeHeroDbFrameCodec.HeroRecordSize,
            NativeHeroDbFrameCodec.HeroRecordSize);
        if (slot == sourceJob)
        {
            source.CopyTo(destination);
        }
        else
        {
            ReferenceCopyRange(source, destination, 0x0000, 0x0008);
            ReferenceCopyShortString(source, destination, 0x0008, 15);
            ReferenceCopyShortString(source, destination, 0x0018, 15);
            ReferenceCopyRange(source, destination, 0x0028, 0x0002);
            ReferenceCopyRange(source, destination, 0x002C, 0x0002);
            ReferenceCopyRange(source, destination, 0x0030, 0x0008);
            ReferenceCopyRange(source, destination, 0x003C, 0x0070);
            ReferenceCopyRange(source, destination, 0x00AD, 0x0010);
            destination[0x00BE] = source[0x00BE];
            ReferenceCopyRange(source, destination, 0x00C0, 0x002A);
            destination[NativeHeroDbFrameCodec.HeroRankOffset] = 2;
            ReferenceCopyRange(source, destination, 0x00EB, 0x0011);
            ReferenceCopyRange(source, destination, 0x0100, 0x000C);
            ReferenceCopyRange(source, destination, 0x012C, 0x0040);
            ReferenceCopyRange(source, destination, 0x4644, 0x007C);
            ReferenceCopyRange(source, destination, 0x4810, 0x0078);
            ReferenceCopyRange(source, destination, 0x48DE, 0x00F6);
        }
        destination[NativeHeroDbFrameCodec.JobOffset] = (byte)slot;
    }
    return result;
}

void ReferenceCopyRange(byte[] source, Span<byte> destination, int offset, int length)
    => source.AsSpan(offset, length).CopyTo(destination.Slice(offset, length));

void ReferenceCopyShortString(byte[] source, Span<byte> destination,
    int offset, int maximumLength)
{
    var length = Math.Min(source[offset], maximumLength);
    destination[offset] = (byte)length;
    source.AsSpan(offset + 1, length).CopyTo(destination.Slice(offset + 1, length));
}

byte[] BuildPatternRecord(int seed, string masterName, string heroName, byte job)
{
    var raw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    new Random(seed).NextBytes(raw);
    BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0, 4), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4, 2), (ushort)raw.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(6, 2), 0);
    WriteShortString(raw, NativeHeroDbFrameCodec.MasterNameOffset, 15, masterName);
    WriteShortString(raw, NativeHeroDbFrameCodec.HeroNameOffset, 15, heroName);
    raw[NativeHeroDbFrameCodec.JobOffset] = job;
    return raw;
}

NativeHeroDynamicData BuildDynamicData() => new(new[]
{
    new NativeHeroDynamicSection(2, new byte[] { 0x10, 0x11 }),
    new NativeHeroDynamicSection(7, new byte[] { 0x20, 0x21, 0x22 })
});

byte[] BuildDynDataBlob(params (byte type, byte[] payload)[] sections)
{
    var size = 4;
    foreach (var section in sections)
        size += NativeHeroDbFrameCodec.DynamicHeaderSize + section.payload.Length;
    var data = new byte[size];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), (uint)(size - 4));
    var offset = 4;
    foreach (var section in sections)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4),
            NativeHeroDbFrameCodec.DynamicSectionMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4, 2),
            (ushort)section.payload.Length);
        data[offset + 6] = section.type;
        section.payload.CopyTo(data, offset + NativeHeroDbFrameCodec.DynamicHeaderSize);
        offset += NativeHeroDbFrameCodec.DynamicHeaderSize + section.payload.Length;
    }
    return data;
}

byte[] BuildRecord(string masterName, string heroName)
{
    var raw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4, 2), (ushort)raw.Length);
    WriteShortString(raw, NativeHeroDbFrameCodec.MasterNameOffset, 15, masterName);
    WriteShortString(raw, NativeHeroDbFrameCodec.HeroNameOffset, 15, heroName);
    return raw;
}

void WriteShortString(byte[] target, int offset, int maximumLength, string value)
{
    var bytes = gbk.GetBytes(value);
    Assert(bytes.Length <= maximumLength, "test short string is oversized");
    target[offset] = (byte)bytes.Length;
    bytes.CopyTo(target, offset + 1);
}

string ReadFrameShortString(byte[] source, int offset)
{
    var length = source[offset];
    return gbk.GetString(source, offset + 1, length);
}

string Read(string relativePath) => File.ReadAllText(Path.Combine(dbSvr, relativePath));

static void SequenceEqual(byte[] expected, byte[] actual, string message)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException($"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// run_audits.py invokes every audit with no arguments, so a tool that hard-requires
// its repository root reported FAIL without evaluating a single assertion. Falling
// back to the enclosing checkout keeps the assertions exactly as they were and only
// removes the "never ran" outcome.
static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }
    return null;
}
