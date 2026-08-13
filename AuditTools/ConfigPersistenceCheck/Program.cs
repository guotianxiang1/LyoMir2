using System.Buffers.Binary;
using GameSvr;
using GameSvr.Configs;
using SystemModule;
using SystemModule.Common;

PrepareRuntimeConfig();

var root = Path.Combine(Path.GetTempPath(), "loym2-config-persistence-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    RunItemNumberSourceContracts();
    RunItemNumberTruthTable();
    RunItemFactoryNumbering();
    RunItemNumberPersistence(root);

    var missingTarget = Path.Combine(root, "new", "ItemNumber.Dat");
    var itemNumber = new byte[sizeof(uint)];
    BinaryPrimitives.WriteUInt32LittleEndian(itemNumber, 0x89ABCDEFu);
    AtomicFile.WriteAllBytes(missingTarget, itemNumber);
    Assert(File.ReadAllBytes(missingTarget).SequenceEqual(itemNumber),
        "target-missing atomic move did not preserve bytes");

    var iniPath = Path.Combine(root, "ServerData.ini");
    File.WriteAllBytes(iniPath, HUtil32.GbkEncoding.GetBytes("[String]\r\nGlobalStrVal0=旧值\r\n\r\n"));
    var ini = new ProbeIni(iniPath);
    ini.WriteValue("String", "GlobalStrVal0", "战神配置");

    var savedBytes = File.ReadAllBytes(iniPath);
    var savedText = HUtil32.GbkEncoding.GetString(savedBytes);
    Assert(savedText.Contains("GlobalStrVal0=战神配置\r\n", StringComparison.Ordinal),
        "GBK INI value did not round-trip");
    Assert(Contains(savedBytes, HUtil32.GbkEncoding.GetBytes("战神配置")),
        "saved file is not encoded as GBK");
    Assert(!savedText.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'),
        "INI line endings changed from CRLF");

    var readOnlyBefore = File.ReadAllBytes(iniPath);
    File.SetAttributes(iniPath, File.GetAttributes(iniPath) | FileAttributes.ReadOnly);
    Throws<UnauthorizedAccessException>(() => ini.WriteValue("String", "GlobalStrVal0", "不应写入"),
        "read-only write was reported as success");
    Assert(File.ReadAllBytes(iniPath).SequenceEqual(readOnlyBefore),
        "read-only failure changed the original file");
    Assert((File.GetAttributes(iniPath) & FileAttributes.ReadOnly) != 0,
        "read-only attribute was silently cleared");

    File.SetAttributes(iniPath, File.GetAttributes(iniPath) & ~FileAttributes.ReadOnly);
    var lockedIni = new ProbeIni(iniPath);
    var lockedBefore = File.ReadAllBytes(iniPath);
    using (var locked = new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Throws<IOException>(() => lockedIni.WriteValue("String", "GlobalStrVal0", "锁定失败"),
            "locked target write was reported as success");
    }
    Assert(File.ReadAllBytes(iniPath).SequenceEqual(lockedBefore),
        "failed replacement truncated or changed the original file");

    Assert(!Directory.EnumerateFiles(root, ".*.tmp", SearchOption.AllDirectories).Any(),
        "temporary files remained after success or failure");
    Console.WriteLine("PASS item-number=uint32-le/+3/unsigned-max factory=explicit-or-once " +
                      "target-missing=move encoding=GBK lineEndings=CRLF " +
                      "readOnly=rejected locked=old-file-intact temp=clean");
}
finally
{
    if (File.Exists(Path.Combine(root, "ServerData.ini")))
    {
        File.SetAttributes(Path.Combine(root, "ServerData.ini"), FileAttributes.Normal);
    }
    Directory.Delete(root, true);
}

static void RunItemNumberSourceContracts()
{
    var repository = FindRepositoryRoot();
    var serverConfig = File.ReadAllText(Path.Combine(repository, "GameSvr",
        "Configs", "ServerConfig.cs"));
    var loadStart = serverConfig.IndexOf("private int LoadItemNumber",
        StringComparison.Ordinal);
    var loadEnd = serverConfig.IndexOf("public void SaveItemNumbers",
        loadStart, StringComparison.Ordinal);
    Assert(loadStart >= 0 && loadEnd > loadStart,
        "item-number loader source markers are missing");
    var loader = serverConfig[loadStart..loadEnd];
    Assert(loader.Contains("new FileStream(_itemNumberPath",
            StringComparison.Ordinal) &&
           loader.Contains("stream.Read(currentBytes)",
               StringComparison.Ordinal),
        "item-number loader is not a single bounded four-byte read");
    Assert(!loader.Contains("File.ReadAllBytes", StringComparison.Ordinal),
        "item-number loader reads the complete Dat file");

    var intervalField = typeof(TimedService).GetField(
        "ItemNumberSaveIntervalMilliseconds",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic);
    Assert(intervalField != null &&
           Convert.ToInt32(intervalField.GetRawConstantValue()) == 900_000,
        "item-number periodic save interval is not native 900000ms");

    // GiveMineCommand.cs 已删除：@GiveMine 不在原生 430 行注册表里。
    foreach (var command in new[]
             {
                 "GiveUserItemCommand.cs",
                 "MakeItemCommand.cs"
             })
    {
        var source = File.ReadAllText(Path.Combine(repository, "GameSvr",
            "Command", "Commands", command));
        Assert(!source.Contains("GetItemNumber(", StringComparison.Ordinal) &&
               !source.Contains("GetItemNumberEx(", StringComparison.Ordinal),
            command + " advances MakeIndex outside the item factory");
    }
}

static bool Contains(byte[] source, byte[] value)
{
    if (value.Length == 0) return true;
    for (var i = 0; i <= source.Length - value.Length; i++)
    {
        if (source.AsSpan(i, value.Length).SequenceEqual(value)) return true;
    }
    return false;
}

static string FindRepositoryRoot()
{
    foreach (var origin in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(origin); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static void RunItemNumberTruthTable()
{
    const uint seed = 1000;
    var cases = new (uint Current, uint Expected, string Name)[]
    {
        (1000u, 1003u, "seed"),
        (1001u, 1004u, "unaligned"),
        (0xFFFFFFF3u, 0xFFFFFFF6u, "threshold-reachable"),
        (0xFFFFFFF4u, seed, "threshold-crossed"),
        (0xFFFFFFF6u, seed, "at-threshold"),
        (0xFFFFFFFEu, 1u, "wrap-to-one"),
        (0xFFFFFFFFu, 2u, "wrap-to-two")
    };

    foreach (var testCase in cases)
    {
        M2Share.g_Config = new GameSvrConfig
        {
            nItemNumberSeed = unchecked((int)seed),
            nItemNumber = unchecked((int)testCase.Current)
        };
        var actual = unchecked((uint)M2Share.GetItemNumber());
        Assert(actual == testCase.Expected,
            $"item-number {testCase.Name}: expected 0x{testCase.Expected:X8}, " +
            $"actual 0x{actual:X8}");
    }

    M2Share.g_Config = new GameSvrConfig
    {
        nItemNumberSeed = unchecked((int)seed),
        nItemNumber = unchecked((int)seed)
    };
    Assert(unchecked((uint)M2Share.GetItemNumberEx()) == 1003u,
        "GetItemNumberEx did not delegate one step to the primary generator");
    Assert(unchecked((uint)M2Share.GetItemNumber()) == 1006u,
        "GetItemNumberEx advanced the primary generator more than once");
}

static void RunItemFactoryNumbering()
{
    const uint explicitMakeIndex = 0xF1234567u;
    M2Share.g_Config = new GameSvrConfig
    {
        nItemNumberSeed = 1000,
        nItemNumber = 1000
    };
    var engine = new UserEngine();
    engine.StdItemList.Add(new GoodItem { Name = "audit-item", DuraMax = 12 });

    TUserItem explicitItem = null;
    Assert(engine.CopyToUserItemFromName("audit-item", ref explicitItem,
            unchecked((int)explicitMakeIndex)),
        "factory rejected an explicit MakeIndex");
    Assert(unchecked((uint)explicitItem.MakeIndex) == explicitMakeIndex,
        "factory changed an explicit MakeIndex bit pattern");
    Assert(unchecked((uint)M2Share.g_Config.nItemNumber) == 1000u,
        "factory advanced the generator for an explicit MakeIndex");

    TUserItem generatedItem = null;
    Assert(engine.CopyToUserItemFromName("audit-item", ref generatedItem),
        "ordinary factory call failed");
    Assert(unchecked((uint)generatedItem.MakeIndex) == 1003u,
        "ordinary factory call did not advance exactly once");
    Assert(unchecked((uint)M2Share.g_Config.nItemNumber) == 1003u,
        "ordinary factory call advanced the generator more than once");
}

static void RunItemNumberPersistence(string root)
{
    var directory = Path.Combine(root, "item-number");
    Directory.CreateDirectory(directory);
    var setupPath = Path.Combine(directory, "!Setup.txt");
    var datPath = Path.Combine(directory, "ItemNumber.Dat");
    File.WriteAllText(setupPath,
        "[Server]\r\nServerIndex=0\r\n\r\n[Setup]\r\nItemNumber=123456\r\n",
        HUtil32.GbkEncoding);
    var setupBefore = File.ReadAllBytes(setupPath);

    var indexedDirectory = Path.Combine(root, "item-number-indexed");
    Directory.CreateDirectory(indexedDirectory);
    var indexedSetupPath = Path.Combine(indexedDirectory, "!Setup.txt");
    File.WriteAllText(indexedSetupPath,
        "[Server]\r\nServerIndex=7\r\n", HUtil32.GbkEncoding);
    M2Share.g_Config = new GameSvrConfig();
    M2Share.nServerIndex = 0;
    new ServerConfig(indexedSetupPath).LoadConfig();
    Assert(unchecked((uint)M2Share.g_Config.nItemNumberSeed) == 1007u,
        "item-number seed is not ServerIndex + 1000");
    Assert(unchecked((uint)M2Share.g_Config.nItemNumber) == 1007u,
        "missing Dat did not load the nonzero ServerIndex seed");
    Assert(unchecked((uint)M2Share.GetItemNumber()) == 1010u,
        "nonzero ServerIndex seed did not advance by three");

    CheckLoadedItemNumber(setupPath, datPath, null, 1000u, 1003u,
        "missing Dat");
    CheckLoadedItemNumber(setupPath, datPath, 999u, 1000u, 1003u,
        "Dat below seed");
    CheckLoadedItemNumber(setupPath, datPath, 1000u, 1000u, 1003u,
        "Dat equal to seed");
    CheckLoadedItemNumber(setupPath, datPath, 1001u, 1001u, 1004u,
        "unaligned Dat");
    CheckLoadedItemNumber(setupPath, datPath, 0x80000001u,
        0x80000001u, 0x80000004u, "unsigned-high Dat");
    CheckLoadedItemNumberBytes(setupPath, datPath, Array.Empty<byte>(),
        1000u, 1003u, "zero-byte Dat");
    CheckLoadedItemNumberBytes(setupPath, datPath, new byte[] { 0xF0 },
        0x000003F0u, 0x000003F3u, "one-byte Dat");
    CheckLoadedItemNumberBytes(setupPath, datPath,
        new byte[] { 0x34, 0x12 },
        0x00001234u, 0x00001237u, "two-byte Dat");
    CheckLoadedItemNumberBytes(setupPath, datPath,
        new byte[] { 0x78, 0x56, 0x34 },
        0x00345678u, 0x0034567Bu, "three-byte Dat");
    CheckLoadedItemNumberBytes(setupPath, datPath, UInt32Bytes(1001u),
        1001u, 1004u, "four-byte Dat");
    var fiveByteDat = new byte[5];
    BinaryPrimitives.WriteUInt32LittleEndian(fiveByteDat, 1001u);
    fiveByteDat[4] = 0xA5;
    CheckLoadedItemNumberBytes(setupPath, datPath, fiveByteDat,
        1001u, 1004u, "five-byte Dat");

    M2Share.g_Config = new GameSvrConfig();
    var config = new ServerConfig(setupPath);
    config.LoadConfig();
    const uint savedCurrent = 0x89ABCDEFu;
    M2Share.g_Config.nItemNumber = unchecked((int)savedCurrent);
    config.SaveItemNumbers();
    var saved = File.ReadAllBytes(datPath);
    Assert(saved.Length == sizeof(uint), "ItemNumber.Dat is not exactly four bytes");
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(saved) == savedCurrent,
        "ItemNumber.Dat did not save the primary current as uint32 little-endian");
    Assert(File.ReadAllBytes(setupPath).SequenceEqual(setupBefore),
        "item-number load/save rewrote !Setup.txt");
}

static void CheckLoadedItemNumber(string setupPath, string datPath,
    uint? datValue, uint expectedCurrent, uint expectedFirst, string name)
{
    if (datValue.HasValue)
    {
        File.WriteAllBytes(datPath, UInt32Bytes(datValue.Value));
    }
    else if (File.Exists(datPath))
    {
        File.Delete(datPath);
    }

    AssertLoadedItemNumber(setupPath, expectedCurrent, expectedFirst, name);
}

static void CheckLoadedItemNumberBytes(string setupPath, string datPath,
    byte[] data, uint expectedCurrent, uint expectedFirst, string name)
{
    File.WriteAllBytes(datPath, data);
    AssertLoadedItemNumber(setupPath, expectedCurrent, expectedFirst, name);
}

static void AssertLoadedItemNumber(string setupPath, uint expectedCurrent,
    uint expectedFirst, string name)
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.nServerIndex = 0;
    new ServerConfig(setupPath).LoadConfig();
    Assert(unchecked((uint)M2Share.g_Config.nItemNumberSeed) == 1000u,
        name + ": seed is not ServerIndex + 1000");
    Assert(unchecked((uint)M2Share.g_Config.nItemNumber) == expectedCurrent,
        name + ": unsigned max load mismatch");
    Assert(unchecked((uint)M2Share.GetItemNumber()) == expectedFirst,
        name + ": first generated value mismatch");
}

static byte[] UInt32Bytes(uint value)
{
    var data = new byte[sizeof(uint)];
    BinaryPrimitives.WriteUInt32LittleEndian(data, value);
    return data;
}

static void Throws<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    // M2Share's static ctor also builds ExpsConfig from ..\Share\PlayerUpgradeExp.ini
    // (M2Share.cs:1690); without it IniFile.Load throws and every assertion below is
    // skipped. Same skeleton the other GameSvr audits lay down.
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

sealed class ProbeIni : IniFile
{
    public ProbeIni(string fileName) : base(fileName)
    {
        Load();
    }

    public void WriteValue(string section, string key, string value)
    {
        WriteString(section, key, value);
    }
}
