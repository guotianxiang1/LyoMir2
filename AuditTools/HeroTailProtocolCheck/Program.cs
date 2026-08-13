using System.Buffers.Binary;
using SystemModule;

CheckRename();
CheckConsignedList();
CheckRestoreConsigned();
CheckThreeSlotCodec();
CheckPersistenceSource();

Console.WriteLine("PASS hero-tail native 164/5A 165/5D(22-byte) 166/5E 167/70");

void CheckRename()
{
    var request = new NativeHeroRenameRequest
    {
        SelectionMode = 1,
        Code = 0x12345678,
        OldHeroName = "旧英雄",
        MasterName = "主人甲",
        NewHeroName = "新英雄"
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeRenameRequest(
        request, out var frame, out var error), error);
    Equal(0x54, frame.Length, "rename request size");
    Equal((ushort)0x164, ReadU16(frame, 12), "rename request opcode");
    Equal((ushort)1, ReadU16(frame, 14), "rename selection mode");
    Equal(request.Code, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16)),
        "rename code");
    Assert(NativeHeroDbFrameCodec.TryDecodeRenameRequest(
        frame, out var decoded, out error), error);
    Equal(request.OldHeroName, decoded.OldHeroName, "rename old name");
    Equal(request.MasterName, decoded.MasterName, "rename master");
    Equal(request.NewHeroName, decoded.NewHeroName, "rename new name");

    var response = new NativeHeroRenameResponse
    {
        Result = 1, Code = request.Code,
        MasterName = request.MasterName, NewHeroName = request.NewHeroName
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeRenameResponse(
        response, out frame, out error), error);
    Equal((ushort)0x5A, ReadU16(frame, 12), "rename response opcode");
    Assert(NativeHeroDbFrameCodec.TryDecodeRenameResponse(
        frame, out var decodedResponse, out error), error);
    Equal((ushort)1, decodedResponse.Result, "rename response result");
    Equal(request.Code, decodedResponse.Code, "rename response code");

    var create = new NativeHeroCreateRequest
    {
        HeroType = 1, Code = 1, MasterName = "主人甲", HeroName = "旧英雄"
    };
    Assert(NativeHeroDbFrameCodec.TryCreateInitialRecord(
        create, out var record, out error), error);
    var source = record.ToArray();
    source[0x120] = 0xA5;
    Assert(NativeHeroDbFrameCodec.TryRenameRecord(
        source, "新英雄", out var renamed, out error), error);
    for (var i = 0; i < source.Length; i++)
    {
        if (i >= NativeHeroDbFrameCodec.HeroNameOffset
            && i <= NativeHeroDbFrameCodec.HeroNameOffset + 15) continue;
        Equal(source[i], renamed[i], $"rename changed unknown byte 0x{i:X}");
    }
    Assert(NativeHeroDbFrameCodec.TryCreateRecord(
        renamed, out var renamedRecord, out error), error);
    Equal("新英雄", renamedRecord.HeroName, "fixed record renamed name");
    Equal((byte)0xA5, renamed[0x120], "fixed record unknown byte");
}

void CheckConsignedList()
{
    var request = new NativeHeroConsignedListRequest { MasterName = "主人甲" };
    Assert(NativeHeroDbFrameCodec.TryEncodeConsignedListRequest(
        request, out var frame, out var error), error);
    Equal((ushort)0x165, ReadU16(frame, 12), "list request opcode");
    Assert(NativeHeroDbFrameCodec.TryDecodeConsignedListRequest(
        frame, out var decodedRequest, out error), error);
    Equal(request.MasterName, decodedRequest.MasterName, "list request master");

    var response = new NativeHeroConsignedListResponse
    {
        MasterName = request.MasterName,
        Entries = new[]
        {
            new NativeHeroConsignedListEntry
                { HeroName = "英雄一", HeroType = 1, Job = 2, Level = 321, Sex = 1 },
            new NativeHeroConsignedListEntry
                { HeroName = "英雄二", HeroType = 2, Job = 0, Level = 45, Sex = 0 }
        }
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeConsignedListResponse(
        response, out frame, out error), error);
    Equal(0x54 + 2 * 22, frame.Length, "list exact frame size");
    Equal((ushort)0x5D, ReadU16(frame, 12), "list response opcode");
    Equal((ushort)2, ReadU16(frame, 14), "list response count");
    Equal((byte)0, frame[0x54 + 21], "list entry reserved byte");
    Assert(NativeHeroDbFrameCodec.TryDecodeConsignedListResponse(
        frame, out var decoded, out error), error);
    Equal(2, decoded.Entries.Count, "list decoded count");
    Equal("英雄一", decoded.Entries[0].HeroName, "list first name");
    Equal(321, decoded.Entries[0].Level, "list first level");
    Assert(!NativeHeroDbFrameCodec.TryDecodeConsignedListResponse(
        frame[..^1], out _, out _), "truncated 22-byte list accepted");
    var badReserved = (byte[])frame.Clone();
    badReserved[0x54 + 21] = 1;
    Assert(NativeHeroDbFrameCodec.TryDecodeConsignedListResponse(
        badReserved, out _, out error), error);
    Assert(!NativeHeroDbFrameCodec.TryEncodeConsignedListResponse(
        new NativeHeroConsignedListResponse
        {
            MasterName = request.MasterName,
            Entries = Enumerable.Repeat(response.Entries[0], 4).ToArray()
        }, out _, out _), "four-entry consigned list accepted");
}

void CheckRestoreConsigned()
{
    var request = new NativeHeroRestoreConsignedRequest
        { MasterName = "主人甲", HeroName = "英雄一" };
    Assert(NativeHeroDbFrameCodec.TryEncodeRestoreConsignedRequest(
        request, out var frame, out var error), error);
    Equal((ushort)0x166, ReadU16(frame, 12), "restore request opcode");
    Assert(NativeHeroDbFrameCodec.TryDecodeRestoreConsignedRequest(
        frame, out var decodedRequest, out error), error);
    Equal(request.HeroName, decodedRequest.HeroName, "restore request hero");

    var response = new NativeHeroRestoreConsignedResponse
    {
        Result = 1, HeroType = 2,
        MasterName = request.MasterName, HeroName = request.HeroName
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeRestoreConsignedResponse(
        response, out frame, out error), error);
    Equal((ushort)0x5E, ReadU16(frame, 12), "restore response opcode");
    Assert(NativeHeroDbFrameCodec.TryDecodeRestoreConsignedResponse(
        frame, out var decoded, out error), error);
    Equal(1, decoded.Result, "restore result");
    Equal(2, decoded.HeroType, "restore hero type");
}

void CheckThreeSlotCodec()
{
    var request = new NativeHeroBuildThreeSlotRequest { MasterName = "主人甲" };
    Assert(NativeHeroDbFrameCodec.TryEncodeBuildThreeSlotRequest(
        request, out var frame, out var error), error);
    Equal(0x54, frame.Length, "three-slot request size");
    Equal((ushort)0x167, ReadU16(frame, 12), "three-slot request opcode");
    Assert(NativeHeroDbFrameCodec.TryDecodeBuildThreeSlotRequest(
        frame, out _, out error), error);
    for (ushort result = 0; result <= 6; result++)
    {
        var heroName = result == 1 ? "低级英雄" : string.Empty;
        var response = new NativeHeroBuildThreeSlotResponse
        {
            Result = result,
            MasterName = request.MasterName,
            HeroName = heroName
        };
        Assert(NativeHeroDbFrameCodec.TryEncodeBuildThreeSlotResponse(
            response, out frame, out error), error);
        Equal(0x54, frame.Length, $"three-slot response {result} size");
        Equal((ushort)0x70, ReadU16(frame, 12), "three-slot response opcode");
        Equal(result, ReadU16(frame, 14), "three-slot response raw result");
        Equal(0, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)),
            "three-slot response dword zero");
        Assert(frame.AsSpan(20, 8).ToArray().All(value => value == 0),
            "three-slot response leading padding is nonzero");
        var heroNameLength = System.Text.Encoding.GetEncoding(936).GetByteCount(heroName);
        Assert(frame.AsSpan(50 + heroNameLength, 15 - heroNameLength)
                .ToArray().All(value => value == 0),
            "three-slot response hero-name padding is nonzero");
        Assert(frame.AsSpan(65).ToArray().All(value => value == 0),
            "three-slot response tail padding is nonzero");
        Assert(NativeHeroDbFrameCodec.TryDecodeBuildThreeSlotResponse(
            frame, out var decoded, out error), error);
        Equal(result, decoded.Result, "three-slot decoded result");
        Equal(request.MasterName, decoded.MasterName, "three-slot decoded master");
        Equal(heroName, decoded.HeroName, "three-slot decoded hero");
    }

    var invalidResponse = new NativeHeroBuildThreeSlotResponse
    {
        Result = 7,
        MasterName = request.MasterName
    };
    Assert(!NativeHeroDbFrameCodec.TryEncodeBuildThreeSlotResponse(
        invalidResponse, out _, out _), "three-slot encoder accepts result 7");
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14, 2), 7);
    Assert(!NativeHeroDbFrameCodec.TryDecodeBuildThreeSlotResponse(
        frame, out _, out _), "three-slot decoder accepts result 7");
}

void CheckPersistenceSource()
{
    var root = FindRepositoryRoot();
    var recordSource = File.ReadAllText(Path.Combine(root,
        "DBSvr", "DB", "impl", "MySqlHeroRecordService.cs"));
    var renameStart = recordSource.IndexOf("public bool RenameHero", StringComparison.Ordinal);
    var renameEnd = recordSource.IndexOf("public bool SetHeroConsignation", renameStart,
        StringComparison.Ordinal);
    Assert(renameStart >= 0 && renameEnd > renameStart, "rename persistence method missing");
    var rename = recordSource[renameStart..renameEnd];
    Assert(!rename.Contains("BeginTransaction", StringComparison.Ordinal),
        "MyISAM rename must not pretend to be transactional");
    Assert(rename.Contains("TryDecodeDataBlob", StringComparison.Ordinal)
           && rename.Contains("TryRenameRecord", StringComparison.Ordinal)
           && rename.Contains("TryEncodeDataBlob", StringComparison.Ordinal),
        "rename does not rewrite the native fixed Data records");
    Assert(rename.Contains("UPDATE mir3.hero_index AS h", StringComparison.Ordinal)
           && rename.Contains("JOIN mir3.hero_data AS d", StringComparison.Ordinal),
        "rename is not one multi-table UPDATE JOIN statement");
    Assert(rename.Contains("lock (_heroMutationLock)", StringComparison.Ordinal),
        "rename is not protected by the global hero mutation lock");
    Assert(rename.Contains("oldStoredDynamicData", StringComparison.Ordinal),
        "rename does not validate/preserve dynData");

    var service = File.ReadAllText(Path.Combine(root,
        "DBSvr", "Services", "GameSocService.cs"));
    foreach (var command in new[]
             {
                 "RenameCommand", "ConsignedListCommand", "RestoreConsignedCommand",
                 "BuildThreeSlotCommand"
             })
        Assert(service.Contains("case NativeHeroDbFrameCodec." + command,
            StringComparison.Ordinal), "DBServer dispatch missing " + command);
    Assert(service.Contains("_heroDataService.BuildThreeSlot", StringComparison.Ordinal),
        "0x167 does not invoke the original three-slot persistence operation");
    Assert(!service.Contains("native 0x167 construction contract is incomplete",
        StringComparison.Ordinal), "0x167 still uses the fixed failure placeholder");
    Assert(service.Contains("command <= NativeHeroDbFrameCodec.BuildThreeSlotCommand",
        StringComparison.Ordinal), "native type1 routing does not admit 0x167");
    Equal(2, CountOccurrences(service,
            "case NativeHeroDbFrameCodec.BuildThreeSlotCommand:"),
        "0x167 is not present in both native and private dispatchers");

    var heroDataSource = File.ReadAllText(Path.Combine(root,
        "DBSvr", "DB", "impl", "MySqlHeroDataService.cs"));
    var buildStart = heroDataSource.IndexOf(
        "public ushort BuildThreeSlot", StringComparison.Ordinal);
    var buildEnd = heroDataSource.IndexOf(
        "public bool CreateDataRow", buildStart, StringComparison.Ordinal);
    Assert(buildStart >= 0 && buildEnd > buildStart,
        "three-slot persistence method boundaries are missing");
    var build = heroDataSource[buildStart..buildEnd];
    Assert(!build.Contains("using var tx = conn.BeginTransaction", StringComparison.Ordinal)
           && !build.Contains(" FOR UPDATE\"", StringComparison.Ordinal),
        "MyISAM three-slot build still pretends to have transactional row locks");
    Assert(build.Contains("UPDATE mir3.hero_index AS highIndex", StringComparison.Ordinal)
           && build.Contains("JOIN mir3.hero_data AS lowData", StringComparison.Ordinal)
           && build.Contains("SET highData.Data=@highData", StringComparison.Ordinal)
           && build.Contains("lowData.Data=@lowData", StringComparison.Ordinal),
        "three-slot persistence is not one checked multi-table statement");
    Assert(service.Contains("if (!nameCheck.valid) return 2;", StringComparison.Ordinal)
           && service.Contains("_playRecordService.Index(request.NewHeroName) >= 0) return 3;",
               StringComparison.Ordinal)
           && service.Contains("_heroRecordService.IsHeroNameExists(request.NewHeroName)) return 4;",
               StringComparison.Ordinal),
        "rename status 2/3/4 mapping does not match the native checks");
}

// The sweep harness runs every tool with its working directory set to the tool's own bin
// folder, so repository-relative source reads have to walk up to the checkout instead of
// trusting the process CWD.
static string FindRepositoryRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj"))
                && Directory.Exists(Path.Combine(directory.FullName, "DBSvr")))
                return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException(
        "repository root not found (no GameSvr/GameSvr.csproj + DBSvr above "
        + Directory.GetCurrentDirectory() + " or " + AppContext.BaseDirectory + ")");
}

static ushort ReadU16(byte[] data, int offset)
    => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

static int CountOccurrences(string source, string value)
{
    var count = 0;
    for (var offset = 0; ; count++)
    {
        offset = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (offset < 0) return count;
        offset += value.Length;
    }
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
