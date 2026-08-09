using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntime();
TestMissingAndEmptyFiles();
TestNativeGiftsParsingAndDialogs();
TestNativeWrappingAndNineMaterialStop();
TestNativeOddGbkByteBoundary();
TestPlayerSelectionStateMachine();
TestGlobalReloadLifecycle();
TestProductionGbkGifts();
TestProcedureOnlyBridge();

Console.WriteLine(
    "PASS DiamondFoundry GBK=Gifts.txt selector=zero-to-one-based " +
    "overview=4-columns invalid=silent procedure-only MakeItem=live-method/closed-func");
return;

static void TestMissingAndEmptyFiles()
{
    var missing = Path.Combine(Path.GetTempPath(),
        "missing-gifts-" + Guid.NewGuid().ToString("N") + ".txt");
    Assert(!NativeDiamondFoundry.TryLoad(missing, out var unavailable,
            out var error), "missing Gifts.txt loaded");
    Assert(error.Contains("file not found", StringComparison.Ordinal),
        "missing Gifts.txt error");
    EqualString(NativeDiamondFoundry.UnavailableDialog,
        unavailable.FoundryListDialog, "unavailable dialog");

    WithGifts(Array.Empty<string>(), foundry =>
    {
        Assert(foundry.SourceLoaded, "empty Gifts.txt not marked loaded");
        Equal(0, foundry.Recipes.Count, "empty Gifts.txt recipe count");
        EqualString(NativeDiamondFoundry.EmptyDialog,
            foundry.FoundryListDialog, "loaded-empty dialog");
    });
}

static void TestNativeGiftsParsingAndDialogs()
{
    WithGifts(new[]
    {
        "井中月=4;金刚石:38/紫水晶矿:2/绿宝石矿:2/勋章之心:2/OK:90",
        "无极棍=4;金刚石:38/紫水晶矿:2/绿宝石矿:2/勋章之心:2/OK:90",
        "骨玉权杖=4;金刚石:88/紫水晶矿:5/绿宝石矿:5/勋章之心:5/OK:80",
        "裁决之杖=4;金刚石:88/紫水晶矿:5/绿宝石矿:5/勋章之心:5/OK:80",
        "龙纹剑=4;金刚石:88/紫水晶矿:5/绿宝石矿:5/勋章之心:5/OK:80",
        "无等号",
        "没有钻石=材料:1/OK:90",
        "没有成功率=金刚石:1/材料:1"
    }, foundry =>
    {
        Equal(5, foundry.Recipes.Count, "valid recipe count");
        Equal(3, foundry.SkippedRowCount, "invalid row count");
        var first = foundry.Recipes[0];
        EqualString("井中月", first.ItemName, "recipe item name");
        Equal(38, first.DiamondCost, "diamond cost");
        Equal(90, first.SuccessRate, "inclusive OK threshold");
        Equal(3, first.Materials.Count, "material count");
        EqualString("紫水晶矿", first.Materials[0].ItemName,
            "first material name");
        Equal(2, first.Materials[0].Count, "first material count");

        var expectedOverview = NativeDiamondFoundry.ListPrefix +
            "<井中月/@DiaPeif_0>         " +
            "<无极棍/@DiaPeif_1>         " +
            "<骨玉权杖/@DiaPeif_2>       " +
            "<裁决之杖/@DiaPeif_3>\\\r\n" +
            "<龙纹剑/@DiaPeif_4>         ";
        EqualString(expectedOverview, foundry.FoundryListDialog,
            "four-column overview");

        var expectedDetail =
            "锻造<井中月>需要以下的物品：\\ \\金刚石..........<38/C=RED>   " +
            "紫水晶矿........2    绿宝石矿........2    \\" +
            "勋章之心........2    \\";
        EqualString(expectedDetail, foundry.GetRecipeDialog(0),
            "recipe detail formatting");
        Assert(foundry.TryBuildSelectionDialog(0, out var selector,
                out var selectionDialog), "valid DiaPeif_0 rejected");
        Equal(1, selector, "zero-to-one-based selector");
        EqualString(expectedDetail + NativeDiamondFoundry.ConfirmText +
                    NativeDiamondFoundry.ConfirmCommands,
            selectionDialog, "selection confirmation dialog");
        Assert(!foundry.TryBuildSelectionDialog(-1, out _, out _),
            "negative DiaPeif accepted");
        Assert(!foundry.TryBuildSelectionDialog(5, out _, out _),
            "past-end DiaPeif accepted");
    });
}

static void TestNativeWrappingAndNineMaterialStop()
{
    var nineMaterials = string.Join('/', Enumerable.Range(1, 9)
        .Select(index => "材料" + index + ":" + index));
    WithGifts(new[]
    {
        "边界=金刚石:65536/OK:65537/材料:256",
        "九材料后成功率=金刚石:1/" + nineMaterials + "/OK:90",
        "成功率在前=金刚石:1/OK:90/" + nineMaterials,
        "空材料哨兵=金刚石:1/:2/不可达材料:3/OK:90"
    }, foundry =>
    {
        Equal(3, foundry.Recipes.Count,
            "ninth material must terminate descriptor scan");
        var wrapped = foundry.Recipes[0];
        Equal(0, wrapped.DiamondCost, "UInt16 diamond wrap");
        Equal(1, wrapped.SuccessRate, "UInt16 OK wrap");
        Equal(0, wrapped.Materials[0].Count, "UInt8 material wrap");
        Equal(9, foundry.Recipes[1].Materials.Count,
            "nine material slots");
        Equal(2, foundry.Recipes[2].Materials.Count,
            "empty material slot was not retained");
        EqualString(string.Empty,
            foundry.Recipes[2].Materials[0].ItemName,
            "empty material sentinel name");
        Assert(!foundry.GetRecipeDialog(2).Contains("不可达材料",
                StringComparison.Ordinal),
            "dialog crossed native empty-material sentinel");
    });
}

static void TestNativeOddGbkByteBoundary()
{
    const string overlongName = "甲甲甲甲甲甲甲乙";
    WithGifts(new[]
    {
        overlongName + "=金刚石:1/" + overlongName + ":2/OK:90"
    }, foundry =>
    {
        var sourceBytes = HUtil32.GbkEncoding.GetBytes(overlongName);
        var expectedName = sourceBytes.AsSpan(0,
            NativeDiamondFoundry.NameMaximumGbkBytes).ToArray();
        EqualBytes(expectedName,
            foundry.Recipes[0].ItemNameGbkBytes.Span,
            "recipe raw 15-byte truncation");
        EqualBytes(expectedName,
            foundry.Recipes[0].Materials[0].ItemNameGbkBytes.Span,
            "material raw 15-byte truncation");
        var legacyName = new string('甲', 7);
        EqualString(legacyName, foundry.Recipes[0].ItemName,
            "recipe public string compatibility");
        EqualString(legacyName, foundry.Recipes[0].Materials[0].ItemName,
            "material public string compatibility");
        Assert(foundry.FoundryListDialog.Contains(
                "<" + legacyName + "/@DiaPeif_0>",
                StringComparison.Ordinal),
            "overview public string compatibility");
        Equal(sourceBytes[14],
            foundry.Recipes[0].ItemNameGbkBytes.Span[14],
            "dangling GBK lead byte");

        Assert(Contains(foundry.FoundryListDialogGbkBytes.Span,
                expectedName),
            "overview lost raw odd-boundary name");
        var expectedMaterialFragment = expectedName
            .Concat(new[] { (byte)'.', (byte)'2' }).ToArray();
        Assert(Contains(foundry.GetRecipeDialogGbkBytes(0).Span,
                expectedMaterialFragment),
            "detail lost raw material or used decoded byte count");
        Assert(foundry.GetRecipeDialog(0).Contains(
                legacyName + "..2", StringComparison.Ordinal),
            "detail public string compatibility");

        Assert(foundry.TryBuildSelectionDialogGbk(0, out var selector,
                out var selection),
            "odd-boundary selection failed");
        Equal(1, selector, "odd-boundary selector");
        Assert(Contains(selection.Span, expectedName),
            "selection dialog lost raw odd-boundary name");

        var player = new TPlayObject();
        var npc = new NormNpc { m_sCharName = "庄园锻造师" };
        InvokePlayer(player, "ShowNativeDiamondFoundryList", npc, foundry);
        Assert(player.m_MsgList[0].Payload is byte[],
            "merchant queue did not retain raw payload");
        var expectedPayload = HUtil32.GbkEncoding.GetBytes(
                npc.m_sCharName + "/")
            .Concat(foundry.FoundryListDialogGbkBytes.ToArray()).ToArray();
        EqualBytes(expectedPayload,
            (byte[])player.m_MsgList[0].Payload,
            "merchant queued raw GBK payload");
    });
}

static void TestPlayerSelectionStateMachine()
{
    WithGifts(new[]
    {
        "井中月=金刚石:38/材料:2/OK:90",
        "无极棍=金刚石:38/材料:2/OK:90"
    }, foundry =>
    {
        var player = new TPlayObject();
        var npc = new NormNpc { m_sCharName = "庄园锻造师" };
        InvokePlayer(player, "ShowNativeDiamondFoundryList", npc, foundry);
        Equal(1, player.m_MsgList.Count, "FoundryList message count");
        EqualString("庄园锻造师/" + foundry.FoundryListDialog,
            player.m_MsgList[0].Buff, "FoundryList merchant payload");
        Equal(Grobal2.RM_MERCHANTSAY, player.m_MsgList[0].wIdent,
            "FoundryList message ident");
        Assert(ReferenceEquals(npc, player.m_NPC),
            "FoundryList did not retain NPC context");

        InvokePlayer(player, "SelectNativeDiamondFoundryRecipe", npc,
            foundry, 1);
        Equal(2, GetSelector(player), "DiaPeif_1 selector");
        Equal(2, player.m_MsgList.Count, "DiaPeif message count");
        Assert(player.m_MsgList[1].Buff.EndsWith(
                NativeDiamondFoundry.ConfirmCommands,
                StringComparison.Ordinal),
            "DiaPeif confirmation commands");

        InvokePlayer(player, "SelectNativeDiamondFoundryRecipe", npc,
            foundry, -1);
        InvokePlayer(player, "SelectNativeDiamondFoundryRecipe", npc,
            foundry, 2);
        Equal(2, GetSelector(player), "invalid DiaPeif changed selector");
        Equal(2, player.m_MsgList.Count,
            "invalid DiaPeif emitted merchant dialog");
    });
}

static void TestGlobalReloadLifecycle()
{
    var root = Path.Combine(Path.GetTempPath(),
        "DiamondFoundryReload-" + Guid.NewGuid().ToString("N"));
    var configDirectory = Path.Combine(root, "Envir");
    var giftsFile = Path.Combine(configDirectory, "Gifts.txt");
    Directory.CreateDirectory(configDirectory);
    var oldConfigPath = M2Share.sConfigPath;
    var oldEnvir = M2Share.g_Config.sEnvirDir;
    var oldFoundry = M2Share.DiamondFoundry;
    try
    {
        M2Share.sConfigPath = root;
        M2Share.g_Config.sEnvirDir = "Envir";
        M2Share.DiamondFoundry = NativeDiamondFoundry.Unavailable;
        var shareConfig = Path.Combine(root, "Share", "Config");
        Directory.CreateDirectory(shareConfig);
        File.WriteAllLines(Path.Combine(shareConfig, "Gifts.txt"),
            new[] { "错误路径=金刚石:1/材料:1/OK:1" },
            HUtil32.GbkEncoding);

        Assert(!GameApp.ReloadDiamondFoundry(out var startupMissingError),
            "Share\\Config Gifts was used instead of Envir\\Gifts.txt");
        Assert(startupMissingError.Contains("file not found",
                StringComparison.Ordinal),
            "missing startup Gifts error");
        Assert(ReferenceEquals(NativeDiamondFoundry.Unavailable,
                M2Share.DiamondFoundry),
            "missing startup Gifts changed the unavailable snapshot");

        File.WriteAllLines(giftsFile,
            new[] { "井中月=金刚石:38/材料:2/OK:90" },
            HUtil32.GbkEncoding);
        Assert(GameApp.ReloadDiamondFoundry(out var firstError),
            "initial Gifts reload failed: " + firstError);
        var first = M2Share.DiamondFoundry;
        Equal(1, first.Recipes.Count, "initial reload recipe count");

        File.WriteAllLines(giftsFile, new[]
        {
            "无极棍=金刚石:38/材料:2/OK:90",
            "骨玉权杖=金刚石:88/材料:5/OK:80"
        }, HUtil32.GbkEncoding);
        Assert(GameApp.ReloadDiamondFoundry(out var hotError),
            "hot Gifts reload failed: " + hotError);
        var hot = M2Share.DiamondFoundry;
        Assert(!ReferenceEquals(first, hot),
            "successful reload did not replace the global snapshot");
        Equal(2, hot.Recipes.Count, "hot reload recipe count");

        File.Delete(giftsFile);
        Assert(!GameApp.ReloadDiamondFoundry(out var missingError),
            "missing Gifts reload succeeded");
        Assert(missingError.Contains("file not found", StringComparison.Ordinal),
            "missing Gifts reload error");
        Assert(ReferenceEquals(hot, M2Share.DiamondFoundry),
            "missing reload discarded the prior snapshot");

        File.WriteAllLines(giftsFile,
            new[] { "占用=金刚石:1/材料:1/OK:1" },
            HUtil32.GbkEncoding);
        using (File.Open(giftsFile, FileMode.Open, FileAccess.Read,
                   FileShare.None))
        {
            var ioPropagated = false;
            try
            {
                GameApp.ReloadDiamondFoundry(out _);
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                ioPropagated = true;
            }
            Assert(ioPropagated,
                "locked Gifts IO failure did not propagate");
            Assert(ReferenceEquals(NativeDiamondFoundry.Unavailable,
                    M2Share.DiamondFoundry),
                "IO failure retained the old recipe snapshot");
        }

        File.WriteAllLines(giftsFile, new[]
        {
            "坏行",
            "逍遥扇=金刚石:2888/材料:8/OK:35"
        }, HUtil32.GbkEncoding);
        Assert(GameApp.ReloadDiamondFoundry(out var partialError),
            "partial Gifts reload failed: " + partialError);
        var partial = M2Share.DiamondFoundry;
        Assert(!ReferenceEquals(hot, partial),
            "partial reload retained the prior snapshot");
        Equal(1, partial.Recipes.Count, "partial reload recipe count");
        Equal(1, partial.SkippedRowCount, "partial reload bad-row count");
    }
    finally
    {
        M2Share.DiamondFoundry = oldFoundry;
        M2Share.g_Config.sEnvirDir = oldEnvir;
        M2Share.sConfigPath = oldConfigPath;
        Directory.Delete(root, true);
    }
}

static void TestProductionGbkGifts()
{
    var giftsFile = FindProductionGifts();
    Assert(NativeDiamondFoundry.TryLoad(giftsFile, out var foundry,
            out var error), "production GBK Gifts load failed: " + error);
    Equal(11, foundry.Recipes.Count, "production Gifts recipe count");
    Equal(0, foundry.SkippedRowCount, "production Gifts bad-row count");
    EqualString("井中月", foundry.Recipes[0].ItemName,
        "production Gifts first item");
    EqualBytes(HUtil32.GbkEncoding.GetBytes("井中月"),
        foundry.Recipes[0].ItemNameGbkBytes.Span,
        "production Gifts raw GBK item");
}

static void TestProcedureOnlyBridge()
{
    WithGifts(new[]
    {
        "井中月=金刚石:38/材料:2/OK:90"
    }, foundry =>
    {
        var player = new TPlayObject();
        var npc = new NormNpc { m_sCharName = "庄园锻造师" };
        InvokePlayer(player, "SelectNativeDiamondFoundryRecipe", npc,
            foundry, 0);
        Equal(1, GetSelector(player), "DiaPeif precondition selector");
        var messageCount = player.m_MsgList.Count;
        var bridge = new PasApiBridge
        {
            CurrentPlayer = player,
            CurrentNpc = npc
        };
        var args = new List<PasValue>
        {
            PasValue.FromObject(player),
            PasValue.FromInt(0)
        };
        var playerArgs = new List<PasValue>
        {
            PasValue.FromObject(player)
        };

        var oldFoundry = M2Share.DiamondFoundry;
        try
        {
            M2Share.DiamondFoundry = foundry;
            var ambientPlayer = new TPlayObject();
            bridge.CurrentPlayer = ambientPlayer;
            Assert(bridge.CallNpcMethod("FoundryList", playerArgs,
                    out var listMethodResult),
                "FoundryList procedure was not dispatched");
            Assert(listMethodResult.Type == PasValueType.Nil,
                "FoundryList procedure returned a value");
            Assert(!bridge.CallNpcFunc("FoundryList", playerArgs,
                    out var listFunctionResult),
                "FoundryList function ABI was exposed");
            Assert(listFunctionResult.Type == PasValueType.Nil,
                "closed FoundryList function returned a value");
            Assert(bridge.CallNpcMethod("DiaPeif", args,
                    out var methodResult),
                "DiaPeif procedure was not dispatched");
            Assert(methodResult.Type == PasValueType.Nil,
                "DiaPeif procedure returned a value");
            Assert(!bridge.CallNpcFunc("DiaPeif", args,
                    out var functionResult),
                "DiaPeif function ABI was exposed");
            Assert(functionResult.Type == PasValueType.Nil,
                "closed DiaPeif function returned a value");
            // MakeItemUseDiam is now the LIVE procedure-only forge. Its function ABI
            // stays rejected — verified here at runtime (safe: the function path only
            // rejects, it never executes the forge). Its method-path dispatch and full
            // consume-before-produce conservation are verified by
            // NativeMakeItemUseDiamTransactionCheck + DiamondGloryCompatCheck; we do
            // NOT run the live forge here so the shared player's selector and merchant
            // message-count assertions below stay deterministic.
            Assert(!bridge.CallNpcFunc("MakeItemUseDiam",
                    playerArgs, out var forgeFunctionResult),
                "MakeItemUseDiam function ABI was exposed");
            Assert(forgeFunctionResult.Type == PasValueType.Nil,
                "closed MakeItemUseDiam function returned a value");
            Equal(1, GetSelector(player),
                "procedure dispatch changed the selected recipe");
            Equal(messageCount + 2, player.m_MsgList.Count,
                "procedure dispatch merchant message count");
            Equal(0, ambientPlayer.m_MsgList.Count,
                "bridge used CurrentPlayer instead of the explicit Player");
            Assert(!bridge.CallNpcMethod("FoundryList",
                    new List<PasValue>(), out _),
                "FoundryList accepted a missing Player argument");
            Assert(!bridge.CallNpcMethod("DiaPeif", playerArgs, out _),
                "DiaPeif accepted a missing selector argument");
        }
        finally
        {
            M2Share.DiamondFoundry = oldFoundry;
        }
    });
}

static string FindProductionGifts()
{
    var candidates = new[]
    {
        @"D:\战神迁移服务端\loy2版\mud2.0\Mir200\Envir\gifts.txt",
        @"D:\战神迁移服务端\战神版抓包分析用\Mud2.0\Mir200\Envir\gifts.txt"
    };
    var result = candidates.FirstOrDefault(File.Exists);
    if (result == null)
        throw new InvalidOperationException("production GBK Gifts.txt missing");
    return result;
}

static void WithGifts(IEnumerable<string> lines,
    Action<NativeDiamondFoundry> test)
{
    var directory = Path.Combine(Path.GetTempPath(),
        "DiamondFoundryCompatCheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var fileName = Path.Combine(directory, "Gifts.txt");
        File.WriteAllLines(fileName, lines, HUtil32.GbkEncoding);
        Assert(NativeDiamondFoundry.TryLoad(fileName, out var foundry,
                out var error), "Gifts.txt load failed: " + error);
        test(foundry);
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void InvokePlayer(TPlayObject player, string methodName,
    params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(methodName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, methodName + " helper missing");
    method.Invoke(player, arguments);
}

static int GetSelector(TPlayObject player)
{
    var property = typeof(TPlayObject).GetProperty(
        "NativeDiamondFoundrySelector",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(property != null, "native selector property missing");
    return (int)property.GetValue(player);
}

static void PrepareRuntime()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.GetFullPath(Path.Combine(runtimeDirectory,
        "..", "Share"));
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);

    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualString(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected [{expected}], actual [{actual}]");
}

static void EqualBytes(ReadOnlySpan<byte> expected,
    ReadOnlySpan<byte> actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{message}: expected {Convert.ToHexString(expected)}, " +
            $"actual {Convert.ToHexString(actual)}");
}

static bool Contains(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value)
{
    if (value.IsEmpty) return true;
    for (var offset = 0; offset <= source.Length - value.Length; offset++)
    {
        if (source.Slice(offset, value.Length).SequenceEqual(value))
            return true;
    }
    return false;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
