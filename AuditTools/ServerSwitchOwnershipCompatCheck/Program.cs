using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

TestMissingAndShortFiles();
TestSharedServiceOwnership();
TestConcurrentStaleSnapshots();
TestWordUpdatePreservesFifthByte();
TestSourceOwnership();

Console.WriteLine(
    "PASS ServerSwitch bytes=5 owner=shared RMW=bit-preserving " +
    "concurrency=stale-snapshots word=low32 fifth-byte=preserved " +
    "SignIn=shared-bit22");
return;

static void TestMissingAndShortFiles()
{
    WithDirectory(directory =>
    {
        Assert(NativeServerSwitchStore.TryLoad(directory, out var missing,
                out var error), "missing switch file load failed: " + error);
        Assert(missing.Available, "missing switch file produced unavailable owner");
        EqualBytes(new byte[5], missing.GetSnapshot(),
            "missing switch file defaults");
        Assert(missing.TrySetBit(4, 0x40, true, out _, out error),
            "missing switch file mutation failed: " + error);
        Assert(missing.TryPersist(out error),
            "missing switch file persistence failed: " + error);
        EqualBytes(new byte[] { 0, 0, 0, 0, 0x40 }, ReadSwitches(directory),
            "created switch file");
    });

    WithDirectory(directory =>
    {
        WriteSwitches(directory, new byte[] { 1, 2, 3, 4 });
        Assert(!NativeServerSwitchStore.TryLoad(directory, out _, out var error),
            "short switch file was accepted");
        Assert(error.Contains("expected 5 bytes, found 4", StringComparison.Ordinal),
            "short switch error lost exact length: " + error);
    });
}

static void TestSharedServiceOwnership()
{
    WithDirectory(directory =>
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Mir2Actor.ini"),
            "[Setup]\r\nLFMultiple=3\r\n", Encoding.GetEncoding(936));
        WriteSwitches(directory,
            new byte[] { 0xA5, 0x02, 0x08, 0x5A, 0x80, 0xEE, 0xFF });

        Assert(NativeServerSwitchStore.TryLoad(directory, out var owner,
                out var error), "shared owner load failed: " + error);
        Assert(NativeNickLinFuState.TryLoad(directory, owner,
                out var nick, out error), "NickLinFu shared load failed: " + error);
        Assert(nick.Enabled, "NickLinFu bit 1:0x02 was lost");
        Equal(3, nick.Multiplier, "NickLinFu multiplier");

        var credit = CreateCreditCardService(owner);
        Assert(!credit.Enabled, "CreditCard unexpectedly started enabled");
        Assert(credit.MonthlyLimitedEnabled,
            "monthly bit 2:0x08 was lost from shared owner");
        Assert(credit.TrySetEnabled(true, out var switchWord),
            "CreditCard bit mutation failed");
        EqualWord(BinaryPrimitives.ReadUInt32LittleEndian(
                new byte[] { 0xA5, 0x12, 0x08, 0x5A }), switchWord,
            "CreditCard broadcast word");
        Assert(credit.TryPersistSwitches(), "CreditCard persistence failed");

        EqualBytes(new byte[] { 0xA5, 0x12, 0x08, 0x5A, 0x80 },
            ReadSwitches(directory),
            "shared CreditCard mutation preserved Nick/monthly/unknown bits");
        Equal(5, ReadSwitches(directory).Length,
            "persistence retained non-native trailing bytes");
    });
}

static void TestConcurrentStaleSnapshots()
{
    WithDirectory(directory =>
    {
        WriteSwitches(directory, new byte[] { 0x40, 0x20, 0x04, 0x10, 0x80 });
        Assert(NativeServerSwitchStore.TryLoad(directory, out var first,
                out var error), "first owner load failed: " + error);
        Assert(NativeServerSwitchStore.TryLoad(directory, out var second,
                out error), "second owner load failed: " + error);

        using var barrier = new Barrier(2);
        var firstTask = Task.Run(() => MutateFromStaleSnapshot(first,
            0, 0x01, barrier));
        var secondTask = Task.Run(() => MutateFromStaleSnapshot(second,
            3, 0x80, barrier));
        Task.WaitAll(firstTask, secondTask);

        EqualBytes(new byte[] { 0x41, 0x20, 0x04, 0x90, 0x80 },
            ReadSwitches(directory),
            "concurrent independent bit mutations");
    });
}

static void TestWordUpdatePreservesFifthByte()
{
    WithDirectory(directory =>
    {
        WriteSwitches(directory, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x01 });
        Assert(NativeServerSwitchStore.TryLoad(directory, out var wordOwner,
                out var error), "word owner load failed: " + error);
        Assert(NativeServerSwitchStore.TryLoad(directory, out var fifthOwner,
                out error), "fifth-byte owner load failed: " + error);

        Assert(fifthOwner.TrySetBit(4, 0x80, true, out _, out error),
            "fifth-byte mutation failed: " + error);
        Assert(fifthOwner.TryPersist(out error),
            "fifth-byte persistence failed: " + error);
        Assert(wordOwner.TryApplySwitchWord(0x44332211, out error),
            "word apply failed: " + error);
        Assert(wordOwner.TryPersist(out error),
            "word persistence failed: " + error);

        EqualBytes(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x81 },
            ReadSwitches(directory),
            "low32 mirror update overwrote fifth byte");
    });
}

static void TestSourceOwnership()
{
    var root = FindRepositoryRoot();
    var credit = ReadSource(Path.Combine(root, "GameSvr", "Services",
        "NativeCreditCardService.cs"));
    var nick = ReadSource(Path.Combine(root, "GameSvr", "Services",
        "NativeNickLinFuState.cs"));
    var startup = ReadSource(Path.Combine(root, "GameSvr", "GameApp.cs"));
    var bridge = ReadSource(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));

    Reject(credit, "File.WriteAllBytes", "CreditCard private whole-file writer");
    Reject(credit, "File.ReadAllBytes", "CreditCard private switch snapshot");
    Reject(nick, "File.ReadAllBytes", "NickLinFu private switch snapshot");
    Require(startup, "NativeServerSwitchStore.TryLoad(nativeShareDirectory,",
        "shared switch owner startup");
    Require(startup, "NativeNickLinFuState.TryLoad(nativeShareDirectory,\n                M2Share.ServerSwitches,",
        "NickLinFu shared owner injection");
    Require(startup, "NativeCreditCardService.TryCreate(M2Share.ServerSwitches,",
        "CreditCard shared owner injection");
    // The old contract here was "the SignIn function must stay fail-closed".
    // 底本推翻了它：0x0072E6E9 是 Delphi 字面量 `FF FF FF FF 19 00 00 00` +
    // "function SignIn: Boolean;"(len 0x19)，0x00732E4B 是派发名表里的
    // `FF FF FF FF 06 00 00 00` + "SignIn"(len 6)，另有类名 TSignInAct
    // (0x006167D2, len 0x0A) 与 TSignInEveryday (0x00616847, len 0x0F)。
    // SignIn 是原生真实存在的脚本函数，NativeSignActCoreCompatCheck 与
    // SignInActivityCompatCheck 两把闸已覆盖其语义。本工具只管开关归属：
    // signin 必须从共享 owner 读第 22 位（byte 2 / mask 0x40），不得私读文件。
    Require(bridge, "M2Share.ServerSwitches.IsBitSet(2, 0x40)",
        "SignIn shared switch-owner read");
    Reject(bridge, "ServerSwitch.Bin", "PAS bridge private switch file access");
}

static void MutateFromStaleSnapshot(NativeServerSwitchStore owner,
    int byteOffset, byte mask, Barrier barrier)
{
    barrier.SignalAndWait();
    Assert(owner.TrySetBit(byteOffset, mask, true, out _, out var error),
        "concurrent mutation failed: " + error);
    barrier.SignalAndWait();
    Assert(owner.TryPersist(out error),
        "concurrent persistence failed: " + error);
}

static NativeCreditCardService CreateCreditCardService(
    NativeServerSwitchStore owner)
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(bool), typeof(NativeServerSwitchStore) }, null);
    Assert(constructor != null,
        "NativeCreditCardService shared-owner constructor is missing");
    return (NativeCreditCardService)constructor.Invoke(
        new object[] { owner.IsBitSet(1, 0x10), false, owner });
}

static byte[] ReadSwitches(string directory) => File.ReadAllBytes(
    Path.Combine(directory, "Config", "ServerSwitch.Bin"));

static void WriteSwitches(string directory, byte[] switches)
{
    Directory.CreateDirectory(Path.Combine(directory, "Config"));
    File.WriteAllBytes(Path.Combine(directory, "Config", "ServerSwitch.Bin"),
        switches);
}

static void WithDirectory(Action<string> action)
{
    var directory = Path.Combine(Path.GetTempPath(),
        "ServerSwitchOwnership-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        action(directory);
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static string FindRepositoryRoot() => AuditRepoRoot.Resolve();

// The multi-line source patterns below are written with '\n'; the checkout is
// CRLF, so match against a normalized copy instead of the raw bytes.
static string ReadSource(string path) =>
    File.ReadAllText(path).Replace("\r\n", "\n");

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is missing");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is present");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualWord(uint expected, uint actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualBytes(byte[] expected, byte[] actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{message}: expected {Convert.ToHexString(expected)}, " +
            $"actual {Convert.ToHexString(actual)}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
