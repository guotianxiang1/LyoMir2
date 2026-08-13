using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.CommandSystem;
using GameSvr.PasEngine;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();

var repositoryRoot = FindRepositoryRoot();
var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-config-prize-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var missingFile = Path.Combine(tempRoot, "missing", "NormalPrize.ini");
    var missingRandomCalls = 0;
    Assert(NativeConfigPrizeManager.TryLoad(missingFile, _ =>
        {
            missingRandomCalls++;
            return 0;
        }, out var missingManager, out var missingError),
        "missing NormalPrize.ini did not produce the native empty table: " +
        missingError);
    Assert(missingManager != null, "missing file did not return an empty manager");
    Assert(!File.Exists(missingFile),
        "missing-file check created a replacement NormalPrize.ini");
    for (var prizeIndex = 1; prizeIndex <= NativeConfigPrizeManager.PoolCount;
         prizeIndex++)
    {
        Equal(0, missingManager.GetPool(prizeIndex).Count,
            $"missing-file pool {prizeIndex} is not empty");
        Assert(!missingManager.TrySelect(prizeIndex, out var missingDescriptor),
            $"missing-file pool {prizeIndex} selected a descriptor");
        Assert(missingDescriptor == null,
            $"missing-file pool {prizeIndex} returned a descriptor");
    }
    Equal(0, missingRandomCalls,
        "empty missing-file table called the random source");

    foreach (var (caseName, content) in new[]
             {
                 ("existing-empty", string.Empty),
                 ("comment-only", string.Join(Environment.NewLine, new[]
                 {
                     ";; GBK line comment",
                     "/*",
                     "GBK block comment",
                     "*/"
                 }))
             })
    {
        var emptyFile = Path.Combine(tempRoot, caseName, "NormalPrize.ini");
        WriteGbk(emptyFile, content);
        var emptyRandomCalls = 0;
        Assert(NativeConfigPrizeManager.TryLoad(emptyFile, _ =>
            {
                emptyRandomCalls++;
                return 0;
            }, out var emptyManager, out var emptyError),
            caseName + " NormalPrize.ini did not load as 99 empty pools: " +
            emptyError);
        for (var prizeIndex = 1;
             prizeIndex <= NativeConfigPrizeManager.PoolCount; prizeIndex++)
        {
            Equal(0, emptyManager.GetPool(prizeIndex).Count,
                $"{caseName} pool {prizeIndex} is not empty");
            Assert(!emptyManager.TrySelect(prizeIndex, out _),
                $"{caseName} pool {prizeIndex} selected a descriptor");
        }
        Equal(0, emptyRandomCalls,
            caseName + " empty table called the random source");
    }

    var parserFile = Path.Combine(tempRoot, "parser", "NormalPrize.ini");
    var splitBoundaryDescriptor = new string('A', 50) + "中B";
    var exactBoundaryDescriptor = new string('A', 49) + "中B";
    var spacedDescriptor = "保留尾随空格 ";
    WriteGbk(parserFile, string.Join(Environment.NewLine, new[]
    {
        "; GBK parser and cumulative-threshold fixture",
        "[奖励1]",
        "奖品1=赤金宝箱:1/100",
        "奖品2=无效行",
        "奖品3=重复阈值:1/100",
        "奖品4=白银宝箱:2/500",
        "奖品5=经验:7/999",
        "奖品6=终止阈值后不得读取:1/1000",
        string.Empty,
        "[奖励2]",
        "奖品1=",
        "奖品2=空行后不得读取:1/999",
        string.Empty,
        "[奖励3]",
        "奖品1=无效行",
        "奖品2=无效阈值:1/不是数字",
        string.Empty,
        "[奖励4]",
        "奖品1=大于终止阈值:1/1000",
        "奖品2=终止后不得读取:1/1001",
        string.Empty,
        "[奖励5]",
        "奖品1=美元十六进制:1/$3E7",
        string.Empty,
        "[奖励6]",
        "奖品1=前缀十六进制:1/0x3E7",
        string.Empty,
        "[奖励7]",
        "奖品1=负阈值:1/-1",
        string.Empty,
        "[奖励8]",
        "奖品1=/999",
        "奖品2=空描述符终止后不得读取:1/1000",
        string.Empty,
        "[奖励9]",
        "奖品1=空后缀:1/",
        "奖品2=空后缀后的终止项:1/999",
        string.Empty,
        "[奖励10]",
        "奖品1=" + splitBoundaryDescriptor + "/999",
        string.Empty,
        "[奖励11]",
        "奖品1=" + exactBoundaryDescriptor + "/999",
        string.Empty,
        "[奖励12]",
        "奖品1=" + spacedDescriptor + "/999",
        string.Empty
    }));

    var randomValues = new Queue<int>(new[]
        { 0, 100, 101, 500, 501, 999, 0, 0, 0 });
    var requestedRanges = new List<int>();
    var originalLogSystem = M2Share.LogSystem;
    var parserLog = CreateBufferedLog(out var parserLogTimer);
    NativeConfigPrizeManager parserManager = null;
    var parserError = string.Empty;
    try
    {
        M2Share.LogSystem = parserLog;
        Assert(NativeConfigPrizeManager.TryLoad(parserFile, maximum =>
            {
                requestedRanges.Add(maximum);
                return randomValues.Dequeue();
            }, out parserManager, out parserError),
            "valid GBK NormalPrize.ini failed to load: " + parserError);
    }
    finally
    {
        M2Share.LogSystem = originalLogSystem;
        parserLogTimer.Dispose();
    }

    var invalidRowLogs = ReadBufferedLog(parserLog);
    Equal(3, invalidRowLogs.Length, "native invalid-row log count");
    EqualText("[Error]:NormalPrize.ini 奖励配置错误：无效行",
        invalidRowLogs[0].Message, "first invalid-row log");
    EqualText("[Error]:NormalPrize.ini 奖励配置错误：重复阈值:1/100",
        invalidRowLogs[1].Message, "non-increasing threshold log");
    EqualText("[Error]:NormalPrize.ini 奖励配置错误：无效行",
        invalidRowLogs[2].Message, "third-pool invalid-row log");
    Assert(invalidRowLogs.All(entry => entry.MessageType == MessageType.Error),
        "invalid-row logs did not use the error channel");

    Equal(99, NativeConfigPrizeManager.PoolCount, "native pool count constant");
    Equal(100, NativeConfigPrizeManager.EntriesPerPool,
        "native entries-per-pool constant");
    Equal(1000, NativeConfigPrizeManager.RandomRange,
        "native random range constant");

    var firstPool = parserManager.GetPool(1);
    Equal(3, firstPool.Count, "invalid/non-increasing rows were not skipped");
    EqualText("赤金宝箱:1/100", firstPool[0].Source, "first parsed row");
    EqualText("白银宝箱:2/500", firstPool[1].Source, "second parsed row");
    EqualText("经验:7/999", firstPool[2].Source, "terminal parsed row");
    Equal(0, parserManager.GetPool(2).Count,
        "explicitly empty row did not terminate its pool");
    Equal(1, parserManager.GetPool(3).Count,
        "invalid Delphi integer did not fall back to zero");
    EqualText("无效阈值:1/0", parserManager.GetPool(3)[0].Source,
        "zero-threshold row");
    Equal(1, parserManager.GetPool(4).Count,
        "threshold >=999 did not terminate its pool");
    EqualText("大于终止阈值:1/1000", parserManager.GetPool(4)[0].Source,
        "threshold above 999 was not retained");
    Equal(999, parserManager.GetPool(5)[0].Threshold,
        "Delphi $ hexadecimal threshold");
    Equal(999, parserManager.GetPool(6)[0].Threshold,
        "Delphi 0x hexadecimal threshold");
    Equal(-1, parserManager.GetPool(7)[0].Threshold,
        "negative first threshold");
    Equal(1, parserManager.GetPool(8).Count,
        "empty descriptor did not terminate at threshold 999");
    EqualText("/999", parserManager.GetPool(8)[0].Source,
        "empty descriptor row");
    Equal(2, parserManager.GetPool(9).Count,
        "empty threshold suffix did not parse as zero");
    EqualText("空后缀:1/0", parserManager.GetPool(9)[0].Source,
        "empty threshold suffix row");
    EqualText(new string('A', 50), parserManager.GetPool(10)[0].Descriptor,
        "legacy string view changed at the odd GBK boundary");
    EqualText(new string('A', 49) + "中",
        parserManager.GetPool(11)[0].Descriptor,
        "legacy string view changed at the exact GBK boundary");
    var splitBoundaryBytes = HUtil32.GbkEncoding.GetBytes(
        splitBoundaryDescriptor);
    var expectedSplitBoundaryBytes = splitBoundaryBytes.AsSpan(0, 0x33)
        .ToArray();
    var actualSplitBoundaryBytes = parserManager.GetPool(10)[0]
        .DescriptorGbkBytes.ToArray();
    Assert(expectedSplitBoundaryBytes.SequenceEqual(actualSplitBoundaryBytes),
        "native ShortString[51] did not preserve the first 51 raw GBK bytes");
    Equal(splitBoundaryBytes[50], actualSplitBoundaryBytes[50],
        "native ShortString[51] lost its dangling GBK lead byte");
    var expectedExactBoundaryBytes = HUtil32.GbkEncoding.GetBytes(
        new string('A', 49) + "中");
    Assert(expectedExactBoundaryBytes.SequenceEqual(parserManager.GetPool(11)[0]
            .DescriptorGbkBytes.ToArray()),
        "native ShortString[51] changed an exact GBK boundary");
    EqualText(spacedDescriptor, parserManager.GetPool(12)[0].Descriptor,
        "descriptor was trimmed before ShortString storage");

    Assert(NativeConfigPrizeManager.TryLoad(parserFile, _ => 0,
            out var rawSelectionManager, out var rawSelectionError),
        "raw-selection fixture failed to load: " + rawSelectionError);
    Assert(rawSelectionManager.TrySelectGbk(10,
            out var selectedSplitBoundaryBytes),
        "raw selector rejected the odd-boundary descriptor");
    Assert(expectedSplitBoundaryBytes.SequenceEqual(
            selectedSplitBoundaryBytes),
        "raw selector round-tripped the odd-boundary descriptor through text");

    foreach (var expected in new[]
             {
                 "赤金宝箱:1", "赤金宝箱:1", "白银宝箱:2",
                 "白银宝箱:2", "经验:7", "经验:7"
             })
    {
        Assert(parserManager.TrySelect(1, out var descriptor),
            "deterministic selector rejected a valid pool");
        EqualText(expected, descriptor, "inclusive cumulative threshold selection");
    }

    Assert(parserManager.TrySelect(3, out var zeroThresholdDescriptor),
        "Random(1000)=0 did not select a zero-threshold first row");
    EqualText("无效阈值:1", zeroThresholdDescriptor,
        "zero-threshold descriptor");
    Assert(!parserManager.TrySelect(7, out var negativeThresholdDescriptor),
        "Random(1000)=0 selected a negative-threshold-only pool");
    Assert(negativeThresholdDescriptor == null,
        "negative-threshold-only pool returned a descriptor");
    Assert(parserManager.TrySelect(8, out var emptyRewardDescriptor),
        "empty descriptor terminal row was not selectable");
    EqualText(string.Empty, emptyRewardDescriptor,
        "empty descriptor terminal selection");
    Equal(0, randomValues.Count, "selector did not consume every test value");
    Assert(requestedRanges.All(value => value == 1000),
        "selector did not call Random(1000)");

    Assert(!parserManager.TrySelect(0, out var belowRange),
        "pool index 0 was accepted");
    Assert(belowRange == null, "pool index 0 returned a descriptor");
    Assert(!parserManager.TrySelect(100, out var aboveRange),
        "pool index 100 was accepted");
    Assert(aboveRange == null, "pool index 100 returned a descriptor");
    Assert(!parserManager.TrySelect(2, out var emptyDescriptor),
        "empty pool selected a descriptor");
    Assert(emptyDescriptor == null, "empty pool returned a descriptor");
    ExpectThrows<ArgumentOutOfRangeException>(() => parserManager.GetPool(0),
        "GetPool accepted index 0");
    ExpectThrows<ArgumentOutOfRangeException>(() => parserManager.GetPool(100),
        "GetPool accepted index 100");

    var incompleteFile = Path.Combine(tempRoot, "incomplete", "NormalPrize.ini");
    WriteGbk(incompleteFile, string.Join(Environment.NewLine, new[]
    {
        "[奖励1]",
        "奖品1=经验:1/998",
        string.Empty
    }));
    Assert(!NativeConfigPrizeManager.TryLoad(incompleteFile, _ => 0,
            out var incompleteManager, out var incompleteError),
        "non-empty pool ending below 999 unexpectedly loaded");
    Assert(incompleteManager != null,
        "incomplete pool did not return the native partial manager");
    Equal(1, incompleteManager.GetPool(1).Count,
        "incomplete pool lost its parsed entry");
    for (var prizeIndex = 2;
         prizeIndex <= NativeConfigPrizeManager.PoolCount; prizeIndex++)
    {
        Equal(0, incompleteManager.GetPool(prizeIndex).Count,
            $"partial manager pool {prizeIndex} is not empty");
    }
    Assert(incompleteError.Contains("999", StringComparison.Ordinal),
        "incomplete-pool error did not report the terminal threshold");

    Assert(!NativeConfigPrizeManager.TryLoad(parserFile, (Func<int, int>)null,
            out var nullRandomManager, out var nullRandomError),
        "null random source unexpectedly loaded");
    Assert(nullRandomManager == null, "null random source returned a manager");
    Assert(nullRandomError.Contains("random", StringComparison.OrdinalIgnoreCase),
        "null-random error was not diagnostic");

    Assert(NativeConfigPrizeManager.TryLoad(parserFile, _ => 1000,
            out var invalidRandomManager, out var invalidRandomError),
        "invalid-random fixture did not load: " + invalidRandomError);
    ExpectThrows<InvalidOperationException>(() =>
        invalidRandomManager.TrySelect(1, out _),
        "selector accepted a random result outside 0..999");

    RunRuntimeReloadAndCommandChecks(tempRoot);

    var productionRoot = args.Length > 0
        ? Path.GetFullPath(args[0])
        : @"D:\lyom2Release\mud2.0\Mir200\Envir";
    var productionScan = ScanProduction(productionRoot);
    Equal(37, productionScan.Hits, "production GiveConfigPrize hit count");
    Equal(6, productionScan.Files, "production GiveConfigPrize file count");
    Equal(37, productionScan.NpcProcedureHits,
        "production calls not using NPC GiveConfigPrize(This_Player,...)");
    Equal(0, productionScan.TempHits,
        "production unexpectedly calls GiveConfigPrizeTemp");

    var expectedProductionPools = new[]
    {
        5, 10, 14, 17, 38, 39, 40, 41, 42, 43, 44, 45,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59,
        60, 61, 62, 63
    };
    Assert(expectedProductionPools.SequenceEqual(productionScan.PrizeIndexes),
        "production prize-index set changed: " +
        string.Join(',', productionScan.PrizeIndexes));

    var mir200Directory = new DirectoryInfo(productionRoot).Parent
        ?? throw new DirectoryNotFoundException(
            "production Envir directory has no Mir200 parent");
    var productionPrizeFile = args.Length > 1
        ? Path.GetFullPath(args[1])
        : Path.Combine(mir200Directory.FullName, "Share", "Config",
            "NormalPrize.ini");
    Assert(NativeConfigPrizeManager.TryLoad(productionPrizeFile, _ => 0,
            out var productionManager, out var productionError),
        "production Share\\Config\\NormalPrize.ini failed to load: " +
        productionError);
    foreach (var prizeIndex in expectedProductionPools)
    {
        Assert(productionManager.TrySelect(prizeIndex, out var descriptor),
            $"production pool {prizeIndex} is empty");
        Assert(!string.IsNullOrEmpty(descriptor),
            $"production pool {prizeIndex} returned an empty descriptor");
    }

    var runtimeFile = Path.Combine(tempRoot, "runtime", "NormalPrize.ini");
    WriteGbk(runtimeFile, string.Join(Environment.NewLine, new[]
    {
        "[奖励10]",
        "奖品1=经验:7/999",
        string.Empty
    }));
    Assert(NativeConfigPrizeManager.TryLoad(runtimeFile, _ => 0,
            out var runtimeManager, out var runtimeError),
        "runtime prize fixture failed to load: " + runtimeError);
    RunDispatchAndBroadcastChecks(runtimeManager);

    var bridgeSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var nativeGiveSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.NativeGive.cs"));
    var managerSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "Services", "NativeConfigPrizeManager.cs"));
    var gameAppSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "GameApp.cs"));
    var m2ShareSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "M2Share.cs"));

    Equal(4, Count(bridgeSource, "case \"giveconfigprize\":"),
        "GiveConfigPrize dispatch count");
    Equal(2, Count(bridgeSource, "case \"giveconfigprizetemp\":"),
        "GiveConfigPrizeTemp dispatch count");
    RequireMatches(bridgeSource,
        "case \\\"giveconfigprize(?:temp)?\\\":[\\s\\S]{0,320}?" +
        "return RejectUnsupportedNativeApi\\(out result\\);",
        3, "three function dispatches must remain fail closed");
    RequireMatches(nativeGiveSource,
        "ConfigPrizeManager\\.TrySelectGbk\\(prizeIndex,[\\s\\S]{0,240}?" +
        "TryExecuteNativeGiveDescriptorGbk\\(descriptorGbkBytes",
        1, "config-prize raw GBK selection/execution chain");
    RequireMatches(gameAppSource,
        "Path\\.Combine\\(nativeShareDirectory,\\s*\\\"Config\\\",\\s*" +
        "\\\"NormalPrize\\.ini\\\"\\)",
        1, "startup must load Share\\Config\\NormalPrize.ini");
    RequireMatches(m2ShareSource,
        "NativeConfigPrizeManager\\s+ConfigPrizeManager", 1,
        "M2Share native config-prize manager field");
    Reject(managerSource, "Json", "JSON prize-table substitute");
    Reject(managerSource, "tbl_", "tbl_xxx prize-table substitute");
    Reject(managerSource, "UserData.dat", "UserData.dat prize-table substitute");
    Reject(managerSource, "Market_Saved", "Market_Saved prize-table substitute");
    Reject(managerSource, "Market_Prices", "Market_Prices prize-table substitute");

    Console.WriteLine(
        "PASS ConfigPrize parser=GBK pools=99x100 random=0..999 inclusive " +
        "raw51=exact " +
        "reload=inplace-partial command=NormalPrize.ini/permission4 " +
        "procedures=3 functions=closed3 broadcast=38FF tags=11 " +
        $"productionHits={productionScan.Hits} files={productionScan.Files} " +
        $"referencedPools={productionScan.PrizeIndexes.Length}");
}
finally
{
    M2Share.ConfigPrizeManager = null;
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, true);
}

static void RunRuntimeReloadAndCommandChecks(string tempRoot)
{
    var originalRootPath = M2Share.sRootPath;
    var originalBaseDir = M2Share.g_Config.sBaseDir;
    var originalManager = M2Share.ConfigPrizeManager;
    var originalObjectManager = M2Share.ObjectManager;
    var originalProcessMsgSection = M2Share.ProcessMsgCriticalSection;
    var reloadRoot = Path.Combine(tempRoot, "runtime-reload");
    var prizeFile = Path.Combine(reloadRoot, "Share", "Config",
        "NormalPrize.ini");

    try
    {
        M2Share.sRootPath = reloadRoot;
        M2Share.g_Config.sBaseDir = "Share";

        WriteGbk(prizeFile, string.Join(Environment.NewLine, new[]
        {
            "[奖励1]",
            "奖品1=首次完整奖励:1/999",
            string.Empty
        }));
        Assert(GameApp.ReloadNormalPrize(out var initialError),
            "initial runtime reload failed: " + initialError);
        var initialManager = M2Share.ConfigPrizeManager;
        Assert(initialManager != null, "initial runtime reload installed null");
        var poolReferences = Enumerable.Range(1,
                NativeConfigPrizeManager.PoolCount)
            .Select(initialManager.GetPool).ToArray();
        EqualText("首次完整奖励:1/999",
            initialManager.GetPool(1)[0].Source,
            "initial runtime reload pool");

        WriteGbk(prizeFile, string.Join(Environment.NewLine, new[]
        {
            "[奖励1]",
            "奖品1=失败前完整奖励:1/999",
            string.Empty,
            "[奖励2]",
            "奖品1=失败池保留奖励:1/998",
            string.Empty
        }));
        Assert(!GameApp.ReloadNormalPrize(out var partialError),
            "incomplete runtime reload unexpectedly succeeded");
        var partialManager = M2Share.ConfigPrizeManager;
        Assert(partialManager != null,
            "failed runtime reload installed null instead of a partial table");
        Assert(ReferenceEquals(initialManager, partialManager),
            "failed runtime reload replaced the native manager object");
        for (var prizeIndex = 1;
             prizeIndex <= NativeConfigPrizeManager.PoolCount; prizeIndex++)
        {
            Assert(ReferenceEquals(poolReferences[prizeIndex - 1],
                    partialManager.GetPool(prizeIndex)),
                $"runtime reload replaced pool object {prizeIndex}");
        }
        EqualText("失败前完整奖励:1/999",
            poolReferences[0][0].Source,
            "partial runtime reload completed pool");
        EqualText("失败池保留奖励:1/998",
            poolReferences[1][0].Source,
            "partial runtime reload failing pool");
        for (var prizeIndex = 3;
             prizeIndex <= NativeConfigPrizeManager.PoolCount; prizeIndex++)
        {
            Equal(0, partialManager.GetPool(prizeIndex).Count,
                $"runtime partial pool {prizeIndex} is not empty");
        }
        Assert(partialError.Contains("999", StringComparison.Ordinal),
            "runtime partial reload error did not report threshold validation");

        File.Delete(prizeFile);
        Assert(GameApp.ReloadNormalPrize(out var missingError),
            "missing-file runtime reload failed: " + missingError);
        var emptyManager = M2Share.ConfigPrizeManager;
        Assert(emptyManager != null, "missing-file reload installed null");
        Assert(ReferenceEquals(partialManager, emptyManager),
            "missing-file reload replaced the native manager object");
        for (var prizeIndex = 1;
             prizeIndex <= NativeConfigPrizeManager.PoolCount; prizeIndex++)
        {
            Assert(ReferenceEquals(poolReferences[prizeIndex - 1],
                    emptyManager.GetPool(prizeIndex)),
                $"missing-file reload replaced pool object {prizeIndex}");
            Equal(0, emptyManager.GetPool(prizeIndex).Count,
                $"missing-file runtime pool {prizeIndex} is not empty");
        }
        Assert(!File.Exists(prizeFile),
            "missing-file runtime reload created NormalPrize.ini");

        WriteGbk(prizeFile, string.Join(Environment.NewLine, new[]
        {
            "[奖励9]",
            "奖品1=命令重载奖励:1/999",
            string.Empty
        }));
        var commandType = typeof(NormalPrizeIniCommand);
        var commandAttribute = commandType.GetCustomAttribute<GameCommandAttribute>();
        Assert(commandAttribute != null,
            "NormalPrize.ini command attribute is missing");
        Equal(4, commandAttribute.nPermissionMin,
            "NormalPrize.ini command permission");

        var commandMethod = commandType.GetMethod(
            nameof(NormalPrizeIniCommand.ReloadNormalPrize));
        Assert(commandMethod != null,
            "NormalPrize.ini default command method is missing");
        var command = new NormalPrizeIniCommand();
        command.Register(commandAttribute, commandMethod);

        var commandMapsField = typeof(CommandManager).GetField("CommandMaps",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert(commandMapsField != null, "command map reflection target is missing");
        var commandMaps = commandMapsField.GetValue(null)
            as IDictionary<string, BaseCommond>;
        Assert(commandMaps != null, "command map has an unexpected type");
        var hadExisting = commandMaps.TryGetValue("NormalPrize.ini",
            out var existingCommand);
        commandMaps["NormalPrize.ini"] = command;

        try
        {
            var commandManager = new CommandManager();
            M2Share.ObjectManager ??= new ObjectManager();
            M2Share.ProcessMsgCriticalSection ??= new object();
            var permissionThree = NewPlayer("normal-prize-level3");
            permissionThree.m_btPermission = 3;
            Assert(commandManager.ExecCmd("@NormalPrize.ini", permissionThree),
                "dotted NormalPrize.ini command was not recognized at level 3");
            Assert(ReferenceEquals(emptyManager, M2Share.ConfigPrizeManager),
                "permission level 3 executed NormalPrize.ini");

            var permissionFour = NewPlayer("normal-prize-level4");
            permissionFour.m_btPermission = 4;
            Assert(commandManager.ExecCmd("@NormalPrize.ini", permissionFour),
                "dotted NormalPrize.ini command was not recognized at level 4");
            var commandManagerResult = M2Share.ConfigPrizeManager;
            Assert(commandManagerResult != null,
                "NormalPrize.ini command installed null");
            Assert(ReferenceEquals(emptyManager, commandManagerResult),
                "permission level 4 replaced the native manager object");
            Assert(ReferenceEquals(poolReferences[8],
                    commandManagerResult.GetPool(9)),
                "permission level 4 replaced native pool 9");
            EqualText("命令重载奖励:1/999",
                poolReferences[8][0].Source,
                "NormalPrize.ini command result");
            AssertSystemMessage(permissionFour,
                "重载Npc脚本奖励配置文件成功", 0xDB, 0xFF,
                "NormalPrize.ini command success");

            WriteGbk(prizeFile, string.Join(Environment.NewLine, new[]
            {
                "[奖励1]",
                "奖品1=命令失败保留奖励:1/998",
                string.Empty
            }));
            ClearMessages(permissionFour);
            Assert(commandManager.ExecCmd("@NormalPrize.ini", permissionFour),
                "failing dotted NormalPrize.ini command was not recognized");
            var commandFailureManager = M2Share.ConfigPrizeManager;
            Assert(commandFailureManager != null,
                "failed NormalPrize.ini command installed null");
            Assert(ReferenceEquals(commandManagerResult, commandFailureManager),
                "failed NormalPrize.ini command replaced the manager object");
            EqualText("命令失败保留奖励:1/998",
                poolReferences[0][0].Source,
                "failed NormalPrize.ini command partial pool");
            for (var prizeIndex = 2;
                 prizeIndex <= NativeConfigPrizeManager.PoolCount; prizeIndex++)
            {
                Equal(0, commandFailureManager.GetPool(prizeIndex).Count,
                    $"failed command partial pool {prizeIndex} is not empty");
            }
            AssertSystemMessage(permissionFour,
                "重载奖励配置文件 NormalPrize.ini 失败，请检查。",
                0xFF, 0x38, "NormalPrize.ini command failure");

            WriteGbk(prizeFile, string.Join(Environment.NewLine, new[]
            {
                "[奖励1]",
                "奖品1=不应完成的奖励:1/999",
                string.Empty
            }));
            ClearMessages(permissionFour);
            using (var lockedFile = new FileStream(prizeFile, FileMode.Open,
                       FileAccess.ReadWrite, FileShare.None))
            {
                TargetInvocationException commandException = null;
                try
                {
                    commandManager.ExecCmd("@NormalPrize.ini", permissionFour);
                }
                catch (TargetInvocationException ex)
                {
                    commandException = ex;
                }

                Assert(commandException?.InnerException is IOException,
                    "NormalPrize.ini command did not propagate its I/O failure");
            }
            Assert(ReferenceEquals(commandFailureManager,
                    M2Share.ConfigPrizeManager),
                "I/O failure replaced the native manager object");
            for (var prizeIndex = 1;
                 prizeIndex <= NativeConfigPrizeManager.PoolCount; prizeIndex++)
            {
                Assert(ReferenceEquals(poolReferences[prizeIndex - 1],
                        M2Share.ConfigPrizeManager.GetPool(prizeIndex)),
                    $"I/O failure replaced pool object {prizeIndex}");
                Equal(0, poolReferences[prizeIndex - 1].Count,
                    $"I/O failure did not leave cleared pool {prizeIndex}");
            }
            Equal(0, SystemMessages(permissionFour).Count,
                "I/O failure emitted a success or failure prompt");
        }
        finally
        {
            if (hadExisting)
                commandMaps["NormalPrize.ini"] = existingCommand;
            else
                commandMaps.Remove("NormalPrize.ini");
        }
    }
    finally
    {
        M2Share.sRootPath = originalRootPath;
        M2Share.g_Config.sBaseDir = originalBaseDir;
        M2Share.ConfigPrizeManager = originalManager;
        M2Share.ObjectManager = originalObjectManager;
        M2Share.ProcessMsgCriticalSection = originalProcessMsgSection;
    }
}

static void RunDispatchAndBroadcastChecks(NativeConfigPrizeManager manager)
{
    M2Share.g_Config = new GameSvrConfig
    {
        boShowPreFixMsg = false,
        btGreenMsgFColor = 3,
        btGreenMsgBColor = 4,
        btRedMsgFColor = 5,
        btRedMsgBColor = 6
    };
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection ??= new object();
    M2Share.ProcessHumanCriticalSection ??= new object();
    M2Share.LogMsgCriticalSection ??= new object();
    M2Share.LogStringList ??= new ArrayList();
    M2Share.LogStringList.Clear();
    M2Share.LogSystem = new MirLog();
    M2Share.ConfigPrizeManager = manager;

    var target = NewPlayer("config-target");
    var observer = NewPlayer("config-observer");
    var decoy = NewPlayer("config-decoy");
    var ghost = NewPlayer("config-ghost");
    ghost.m_boGhost = true;
    AddOnlinePlayer(target);
    AddOnlinePlayer(observer);
    AddOnlinePlayer(decoy);
    AddOnlinePlayer(ghost);

    var npc = new NormNpc
    {
        m_sCharName = "config-npc",
        m_sMapName = "npc-map"
    };
    var bridge = new PasApiBridge
    {
        CurrentPlayer = target,
        CurrentNpc = npc
    };
    var playerArgs = new List<PasValue>
    {
        PasValue.FromInt(10),
        PasValue.FromString("player reward <$GIFTITEM>"),
        PasValue.FromBool(true)
    };
    var npcArgs = new List<PasValue>
    {
        PasValue.FromObject(target),
        PasValue.FromInt(10),
        PasValue.FromString("npc reward <$GIFTITEM>")
    };

    var originalExp = target.m_Abil.Exp;
    Assert(!bridge.CallPlayerFunc("GiveConfigPrize", playerArgs,
            out var playerFunctionResult),
        "player function exposed native GiveConfigPrize procedure");
    AssertNil(playerFunctionResult, "player GiveConfigPrize function");
    Assert(!bridge.CallNpcFunc("GiveConfigPrize", npcArgs,
            out var npcFunctionResult),
        "NPC function exposed native GiveConfigPrize procedure");
    AssertNil(npcFunctionResult, "NPC GiveConfigPrize function");
    Assert(!bridge.CallNpcFunc("GiveConfigPrizeTemp", npcArgs,
            out var tempFunctionResult),
        "NPC function exposed native GiveConfigPrizeTemp procedure");
    AssertNil(tempFunctionResult, "NPC GiveConfigPrizeTemp function");
    Equal(originalExp, target.m_Abil.Exp,
        "fail-closed functions granted a reward");
    Equal(0, target.m_MsgList.Count,
        "fail-closed functions queued a player message");

    Assert(!bridge.CallPlayerMethod("GiveConfigPrize",
            playerArgs.Take(2).ToList()),
        "player procedure accepted the wrong arity");
    Assert(!bridge.CallPlayerMethod("GiveConfigPrize",
            playerArgs.Append(PasValue.FromInt(1)).ToList()),
        "player procedure accepted an extra argument");

    Assert(bridge.CallPlayerMethod("GiveConfigPrize", playerArgs),
        "player GiveConfigPrize procedure was not dispatched");
    Equal(originalExp + 7, target.m_Abil.Exp,
        "player GiveConfigPrize experience amount");
    AssertWinExp(target, 7, "player procedure");
    AssertBroadcast(target, "player reward 经验", "player recipient");
    AssertBroadcast(observer, "player reward 经验", "player observer");
    AssertBroadcast(decoy, "player reward 经验", "player decoy observer");
    AssertConfigPrizeLog("config-target", "经验", 555555, 7, 10,
        "player procedure special log");
    Equal(0, SystemMessages(ghost).Count,
        "ghost received a config-prize broadcast");

    ClearMessages(target, observer, decoy, ghost);
    bridge.CurrentPlayer = decoy;
    Assert(!bridge.CallNpcMethod("GiveConfigPrize", npcArgs.Take(2).ToList(),
            out var shortNpcResult),
        "NPC procedure accepted the wrong arity");
    AssertNil(shortNpcResult, "short NPC GiveConfigPrize procedure");
    Assert(bridge.CallNpcMethod("GiveConfigPrize", npcArgs,
            out var npcProcedureResult),
        "NPC GiveConfigPrize procedure was not dispatched");
    AssertNil(npcProcedureResult, "NPC GiveConfigPrize procedure");
    Equal(originalExp + 14, target.m_Abil.Exp,
        "NPC GiveConfigPrize did not reward the explicit player");
    Equal(0, decoy.m_Abil.Exp,
        "NPC GiveConfigPrize rewarded CurrentPlayer instead of its argument");
    Assert(ReferenceEquals(decoy, bridge.CurrentPlayer),
        "NPC GiveConfigPrize did not restore bridge player context");
    AssertBroadcast(observer, "npc reward 经验", "NPC observer");
    AssertConfigPrizeLog("config-target", "经验", 555555, 7, 10,
        "NPC procedure special log");

    ClearMessages(target, observer, decoy, ghost);
    Assert(bridge.CallNpcMethod("GiveConfigPrizeTemp", npcArgs,
            out var tempProcedureResult),
        "NPC GiveConfigPrizeTemp procedure was not dispatched");
    AssertNil(tempProcedureResult, "NPC GiveConfigPrizeTemp procedure");
    Equal(originalExp + 21, target.m_Abil.Exp,
        "NPC GiveConfigPrizeTemp experience amount");
    Equal(0, decoy.m_Abil.Exp,
        "NPC GiveConfigPrizeTemp rewarded CurrentPlayer instead of its argument");
    AssertBroadcast(observer, "npc reward 经验", "NPC temp observer");
    AssertConfigPrizeLog("config-target", "经验", 555555, 7, 10,
        "NPC temp procedure special log");

    ClearMessages(target, observer, decoy, ghost);
    bridge.CurrentPlayer = target;
    var twelveTags = string.Join("|", Enumerable.Repeat("<$GIFTITEM>", 12));
    Assert(bridge.CallPlayerMethod("GiveConfigPrize", new List<PasValue>
        {
            PasValue.FromInt(10), PasValue.FromString(twelveTags),
            PasValue.FromBool(true)
        }), "player GiveConfigPrize 11-tag fixture was not dispatched");
    var expectedTags = string.Join("|",
        Enumerable.Repeat("经验", 11).Append("<$GIFTITEM>"));
    AssertBroadcast(observer, expectedTags, "11-tag replacement cap");

    ClearMessages(target, observer, decoy, ghost);
    var mixedCaseTags = "<$giftitem>|<$GIFTITEM>|<$GiFtItEm>";
    Assert(bridge.CallPlayerMethod("GiveConfigPrize", new List<PasValue>
        {
            PasValue.FromInt(10), PasValue.FromString(mixedCaseTags),
            PasValue.FromBool(true)
        }), "mixed-case InfoStr fixture was not dispatched");
    AssertBroadcast(observer, "<$giftitem>|经验|<$GiFtItEm>",
        "case-sensitive token replacement");

    ClearMessages(target, observer, decoy, ghost);
    Assert(bridge.CallPlayerMethod("GiveConfigPrize", new List<PasValue>
        {
            PasValue.FromInt(10), PasValue.FromString(string.Empty),
            PasValue.FromBool(false)
        }), "player GiveConfigPrize empty-info fixture was not dispatched");
    Equal(0, SystemMessages(observer).Count,
        "empty InfoStr emitted a global broadcast");

    RunSpecialRewardChecks(bridge, target);
}

static void RunSpecialRewardChecks(PasApiBridge bridge, TPlayObject target)
{
    M2Share.LogStringList.Clear();
    ClearMessages(target);
    M2Share.CreditCardService = NativeCreditCardService.Disabled;
    target.m_nLingFu = 10;
    target.m_CreditCard.Value = 20;
    target.m_CreditCard.Dirty = false;

    Assert(ExecuteNativeGiveDescriptor(bridge, "限时灵符:3", true,
            out var permanentShowSuccess, out var permanentName,
            out var permanentCount),
        "limited descriptor failed while CreditCard switch was off");
    EqualText("限时灵符", permanentName,
        "limited descriptor parsed reward name");
    Equal(3, permanentCount, "limited descriptor parsed reward count");
    Assert(permanentShowSuccess,
        "LingFu reward unexpectedly suppressed generic success text");
    Equal(13, target.m_nLingFu,
        "switch-off limited descriptor did not grant permanent LingFu");
    Equal(20, target.m_CreditCard.Value,
        "switch-off limited descriptor changed CreditCard.Value");
    AssertNativeCapitalRefresh(target, "switch-off LingFu reward");
    AssertLastGameDataLog(target, "灵符", 23001, 3, string.Empty,
        "switch-off LingFu log");

    M2Share.LogStringList.Clear();
    ClearMessages(target);
    M2Share.CreditCardService = CreateCreditCardService(true);
    target.m_CreditCard.Dirty = false;
    Assert(ExecuteNativeGiveDescriptor(bridge, "灵符:4", true,
            out var limitedShowSuccess, out var limitedName,
            out var limitedCount),
        "permanent descriptor failed while CreditCard switch was on");
    EqualText("灵符", limitedName, "permanent descriptor parsed reward name");
    Equal(4, limitedCount, "permanent descriptor parsed reward count");
    Assert(limitedShowSuccess,
        "limited-LingFu account reward suppressed generic success text");
    Equal(13, target.m_nLingFu,
        "switch-on permanent descriptor changed permanent LingFu");
    Equal(24, target.m_CreditCard.Value,
        "switch-on permanent descriptor did not grant limited LingFu");
    Assert(target.m_CreditCard.Dirty,
        "limited LingFu grant did not mark CreditCard dirty");
    AssertNativeCapitalRefresh(target, "switch-on LingFu reward");
    AssertLastGameDataLog(target, "限时灵符", 23002, 4, string.Empty,
        "switch-on limited LingFu log");

    M2Share.LogStringList.Clear();
    ClearMessages(target);
    target.m_CreditCard.GloryPointValue = 30;
    target.m_CreditCard.GloryPointDirty = false;
    target.m_nHonorValue = 40;
    target.m_nActivePoint = 50;
    Assert(ExecuteNativeGiveDescriptor(bridge, "荣耀点:5", true,
            out var gloryShowSuccess, out var gloryName, out var gloryCount),
        "GloryPoint descriptor was not executed");
    EqualText("荣耀点", gloryName, "GloryPoint parsed reward name");
    Equal(5, gloryCount, "GloryPoint parsed reward count");
    Assert(!gloryShowSuccess,
        "GloryPoint did not suppress the generic config-prize success text");
    Equal(35, target.m_CreditCard.GloryPointValue,
        "GloryPoint account value");
    Assert(target.m_CreditCard.GloryPointDirty,
        "GloryPoint grant did not mark the account dirty");
    Equal(40, target.m_nHonorValue,
        "GloryPoint grant changed player honor value");
    Equal(50, target.m_nActivePoint,
        "GloryPoint grant changed active points");
    AssertNativeCapitalRefresh(target, "GloryPoint reward");
    AssertLastGameDataLog(target, "荣耀点", 888888, 5, "系统给予",
        "GloryPoint executor log");

    M2Share.LogStringList.Clear();
    ClearMessages(target);
    target.m_nShengWan = 60;
    target.m_NativeCattle.Tier = 1;
    target.m_NativeCattle.Value = 0;
    Assert(ExecuteNativeGiveDescriptor(bridge, "牛气值:7", true,
            out var vitalityShowSuccess, out _, out _),
        "native cattle descriptor was not executed");
    Assert(vitalityShowSuccess,
        "native cattle unexpectedly suppressed generic success text");
    Equal(7, target.m_NativeCattle.Value,
        "native cattle descriptor value");
    Equal(60, target.m_nShengWan,
        "native cattle reward changed native reputation");
    Equal(50, target.m_nActivePoint,
        "native cattle reward changed active points");
    Equal(0, M2Share.LogStringList.Count,
        "native cattle reward claimed DB persistence");
    var cattleNotice = target.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_CATTLE_SYSMESSAGE);
    Equal(0xFB, cattleNotice.wParam, "native cattle notice wParam");
    EqualText("7 点牛气值增加", cattleNotice.Buff,
        "native cattle notice text");

    Assert(ExecuteNativeGiveDescriptor(bridge, "声望:7", true,
            out var reputationShowSuccess, out _, out _),
        "native reputation descriptor was not executed");
    Assert(reputationShowSuccess,
        "native reputation unexpectedly suppressed generic success text");
    Equal(67, target.m_nShengWan,
        "native reputation did not update m_nShengWan");

    M2Share.CreditCardService = NativeCreditCardService.Disabled;
}

static bool ExecuteNativeGiveDescriptor(PasApiBridge bridge, string descriptor,
    bool configPrize, out bool showSuccess, out string itemName, out int count)
{
    var method = typeof(PasApiBridge).GetMethod(
        "TryExecuteNativeGiveDescriptor",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "native give-descriptor reflection target is missing");

    object[] parameters = { descriptor, 1, false, configPrize, false, null, 0 };
    var success = (bool)method.Invoke(bridge, parameters);
    showSuccess = (bool)parameters[4];
    itemName = (string)parameters[5];
    count = (int)parameters[6];
    return success;
}

static NativeCreditCardService CreateCreditCardService(bool enabled)
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(bool), typeof(string), typeof(byte[]) },
        null);
    Assert(constructor != null,
        "NativeCreditCardService constructor reflection target is missing");
    var switches = new byte[5];
    if (enabled) switches[1] = 0x10;
    return (NativeCreditCardService)constructor.Invoke(
        new object[] { enabled, false, string.Empty, switches });
}

static void AssertNativeCapitalRefresh(TPlayObject player, string message)
{
    var matches = player.m_MsgList.Where(entry =>
        entry.wIdent == Grobal2.RM_LINGFU_CHANGED).ToArray();
    Equal(1, matches.Length, message + " native 10054 refresh count");
}

static void AssertLastGameDataLog(TPlayObject player, string itemName,
    int makeIndex, int count, string reason, string message)
{
    Assert(M2Share.LogStringList.Count > 0, message + " was not written");
    var expected = string.Join('\t', 9, player.m_sMapName, player.m_nCurrX,
        player.m_nCurrY, player.m_sCharName, itemName, makeIndex, count, reason);
    EqualText(expected,
        (string)M2Share.LogStringList[M2Share.LogStringList.Count - 1], message);
}

static TPlayObject NewPlayer(string name)
{
    var player = new TPlayObject
    {
        m_sCharName = name,
        m_sMapName = "test-map",
        m_nCurrX = 11,
        m_nCurrY = 22
    };
    player.m_Abil.Level = 1;
    player.m_Abil.Exp = 0;
    player.m_Abil.MaxExp = int.MaxValue;
    return player;
}

static void AddOnlinePlayer(TPlayObject player)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "UserEngine player-list reflection target is missing");
    var players = field.GetValue(M2Share.UserEngine) as IList<TPlayObject>;
    Assert(players != null, "UserEngine player list has an unexpected type");
    players.Add(player);
}

static void AssertWinExp(TPlayObject player, int amount, string message)
{
    var matches = player.m_MsgList.Where(entry =>
        entry.wIdent == Grobal2.RM_WINEXP && entry.nParam1 == amount).ToArray();
    Equal(1, matches.Length, message + " RM_WINEXP count");
}

static void AssertBroadcast(TPlayObject player, string body, string message)
{
    var matches = SystemMessages(player).Where(entry =>
        string.Equals(entry.Buff, body, StringComparison.Ordinal)).ToArray();
    Equal(1, matches.Length, message + " message count");
    Equal(0, matches[0].wParam, message + " wParam");
    Equal(255, matches[0].nParam1, message + " foreground color");
    Equal(56, matches[0].nParam2, message + " background color");
    Equal(0, matches[0].nParam3, message + " nParam3");
}

static void AssertSystemMessage(TPlayObject player, string body,
    int foreground, int background, string message)
{
    var matches = SystemMessages(player).Where(entry =>
        string.Equals(entry.Buff, body, StringComparison.Ordinal)).ToArray();
    Equal(1, matches.Length, message + " message count");
    Equal(0, matches[0].wParam, message + " wParam");
    Equal(foreground, matches[0].nParam1, message + " foreground color");
    Equal(background, matches[0].nParam2, message + " background color");
    Equal(0, matches[0].nParam3, message + " nParam3");
}

static void AssertConfigPrizeLog(string playerName, string rewardName,
    int makeIndex, int count, int prizeIndex, string message)
{
    Assert(M2Share.LogStringList.Count > 0, message + " was not written");
    var expected = string.Join('\t', 9, "test-map", 11, 22, playerName,
        rewardName, makeIndex, count, "奖励配置" + prizeIndex);
    EqualText(expected,
        (string)M2Share.LogStringList[M2Share.LogStringList.Count - 1], message);
}

static List<SendMessage> SystemMessages(TPlayObject player) =>
    player.m_MsgList.Where(entry => entry.wIdent == Grobal2.RM_SYSMESSAGE)
        .ToList();

static void ClearMessages(params TPlayObject[] players)
{
    foreach (var player in players)
        player.m_MsgList.Clear();
}

static (int Hits, int Files, int NpcProcedureHits, int TempHits,
    int[] PrizeIndexes) ScanProduction(string root)
{
    if (!Directory.Exists(root))
        throw new DirectoryNotFoundException("production Envir not found: " + root);

    var callExpression = new Regex(@"\bGiveConfigPrize\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    var npcExpression = new Regex(
        @"\bGiveConfigPrize\s*\(\s*This_Player\s*,",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    var indexExpression = new Regex(
        @"\bGiveConfigPrize\s*\(\s*This_Player\s*,\s*(\d+)\s*,",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    var tempExpression = new Regex(@"\bGiveConfigPrizeTemp\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    var hits = 0;
    var files = 0;
    var npcProcedureHits = 0;
    var tempHits = 0;
    var indexes = new HashSet<int>();
    foreach (var path in Directory.EnumerateFiles(root, "*.pas",
                 SearchOption.AllDirectories))
    {
        var source = HUtil32.GbkEncoding.GetString(File.ReadAllBytes(path));
        var fileHits = callExpression.Matches(source).Count;
        if (fileHits != 0)
        {
            hits += fileHits;
            files++;
        }
        npcProcedureHits += npcExpression.Matches(source).Count;
        tempHits += tempExpression.Matches(source).Count;
        foreach (Match match in indexExpression.Matches(source))
            indexes.Add(int.Parse(match.Groups[1].Value));
    }

    return (hits, files, npcProcedureHits, tempHits,
        indexes.OrderBy(value => value).ToArray());
}

static void WriteGbk(string path, string content)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);
    File.WriteAllText(path, content, HUtil32.GbkEncoding);
}

static MirLog CreateBufferedLog(out System.Threading.Timer timer)
{
    var log = new MirLog();
    var timerField = typeof(MirLog).GetField("_logTime",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(timerField != null, "MirLog timer reflection target is missing");
    timer = timerField.GetValue(log) as System.Threading.Timer;
    Assert(timer != null, "MirLog timer has an unexpected type");
    timer.Change(Timeout.Infinite, Timeout.Infinite);
    return log;
}

static LogInfo[] ReadBufferedLog(MirLog log)
{
    var queueField = typeof(MirLog).GetField("_logqueue",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(queueField != null, "MirLog queue reflection target is missing");
    var queue = queueField.GetValue(log) as IEnumerable<LogInfo>;
    Assert(queue != null, "MirLog queue has an unexpected type");
    return queue.ToArray();
}

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0;;)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        offset = index + value.Length;
    }
}

static void RequireMatches(string source, string pattern, int expected,
    string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Count;
    Equal(expected, actual, message);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        Fail(message + " remains");
}

static void AssertNil(PasValue value, string message)
{
    Assert(value.Type == PasValueType.Nil, message + " did not return Nil");
}

static void ExpectThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    Fail(message);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
}

static void PrepareRuntimeConfig()
{
    // The config files must be on disk BEFORE anything touches M2Share: the very
    // first reference runs its static ctor, which loads !Setup.txt / String.ini /
    // Command.conf and ..\Share\PlayerUpgradeExp.ini and throws if they are absent.
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);

    // The fixture players are online (m_boOffLineFlag stays false), so every
    // SendDefMessage reaches TPlayObject.SendSocket, which dereferences
    // M2Share.GateManager. Only GameApp.cs assigns it in a real boot. The
    // singleton registers no gate, so AddGateBuffer returns false for the
    // fixture's gate index and nothing is actually transmitted.
    M2Share.GateManager ??= GateManager.Instance;
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        Fail($"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        Fail($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) Fail(message);
}

static void Fail(string message) => throw new InvalidOperationException(message);
