using System.Buffers.Binary;
using System.Collections;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
PrepareRuntime();

var failures = new List<string>();
Run("strict function ABI and transfer order", StrictFunctionAbiAndTransferOrder);
Run("unrepresentable extension preflight", UnrepresentableExtensionPreflight);
Run("backed extension whole-bag preflight", BackedExtensionWholeBagPreflight);
Run("partial failure remains persistable", PartialFailureRemainsPersistable);
Run("atomic save failure remains retryable", AtomicSaveFailureRemainsRetryable);
Run("208-byte opaque record round trip", OpaqueRecordRoundTrip);
Run("NpcSave path, load order, and empty deletion", NpcSavePersistence);
Run("production NpcSave root resolution", ProductionNpcSaveRootResolution);
Run("normal stop flushes native merchants", NormalStopFlushesNativeMerchants);
Run("source contract and WantWarMon merge", SourceContract);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("StorageAllBagItemsStaticCheck PASS tests=10 " +
                  "abi=function record=208B save=atomic transfer=preflight");
return 0;

void StrictFunctionAbiAndTransferOrder()
{
    ResetRuntimeState();
    var contextPlayer = Player("Context", ready: true);
    contextPlayer.m_ItemList.Add(Item(90, 1));
    var explicitPlayer = Player("Explicit", ready: false);
    var first = Item(101, 1);
    var second = Item(102, 2);
    var third = Item(103, 1);
    explicitPlayer.m_ItemList.Add(first);
    explicitPlayer.m_ItemList.Add(second);
    explicitPlayer.m_ItemList.Add(third);
    var merchant = MerchantNpc("keeper", "3");
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = merchant
    };
    var args = new List<PasValue> { PasValue.FromObject(explicitPlayer) };

    Assert(bridge.CallNpcFunc("StorageAllBagItems", args, out var result),
        "recognized function rejected before property gate");
    Equal(PasValueType.String, result.Type, "property gate return type");
    Equal(string.Empty, result.AsString(), "property gate return value");
    Equal(3, explicitPlayer.m_ItemList.Count, "property gate changed bag");

    merchant.AddNativePasProperty(9);
    Assert(bridge.CallNpcFunc("StorageAllBagItems", args, out result),
        "recognized function rejected before player-ready gate");
    Equal(string.Empty, result.AsString(), "player-ready gate return value");
    Equal(3, explicitPlayer.m_ItemList.Count, "player-ready gate changed bag");

    explicitPlayer.m_boReadyRun = true;
    explicitPlayer.m_btPermission = 3;
    Assert(bridge.CallNpcFunc("StorageAllBagItems", args, out result),
        "recognized function rejected before permission gate");
    Equal(string.Empty, result.AsString(), "permission gate return value");
    Equal(3, explicitPlayer.m_ItemList.Count, "permission gate changed bag");

    explicitPlayer.m_btPermission = 4;
    Assert(bridge.CallNpcFunc("StorageAllBagItems", args, out result),
        "valid function ABI rejected");
    Equal("一共收取了您背包里的 3 件物品。", result.AsString(),
        "success string");
    Equal(0, explicitPlayer.m_ItemList.Count, "explicit player bag");
    Equal(1, contextPlayer.m_ItemList.Count, "CurrentPlayer bag changed");
    Equal(2, merchant.m_GoodsList.Count, "goods group count");
    References(new[] { first, third }, merchant.m_GoodsList[0],
        "same-index goods order");
    References(new[] { second }, merchant.m_GoodsList[1],
        "second goods group");
    Assert(merchant.NativeGoodsDirty, "successful transfer did not mark dirty");
    Equal(new short[] { 1, 2 }, merchant.m_ItemPriceList
        .Select(price => price.wIndex).OrderBy(index => index).ToArray(),
        "transferred item prices were not immediately available");

    var deleteMessage = explicitPlayer.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SENDDELITEMLIST);
    var deleted = (IList<TDeleteItem>)deleteMessage.Payload;
    Equal(deleted.Count, deleteMessage.nParam1,
        "batched deletion protocol count");
    Equal(new[] { 103, 102, 101 }, deleted.Select(item => item.MakeIndex).ToArray(),
        "batched deletion order");
    Equal(new[] { 103, 102, 101 }, M2Share.LogStringList.Cast<string>()
        .Select(line => int.Parse(line.Split('\t')[6])).ToArray(),
        "per-item log order");

    Assert(!bridge.CallNpcMethod("StorageAllBagItems", args, out _),
        "function ABI leaked into procedure dispatcher");
    foreach (var malformed in new[]
             {
                 new List<PasValue>(),
                 new List<PasValue> { PasValue.FromString("Explicit") },
                 new List<PasValue>
                 {
                     PasValue.FromObject(explicitPlayer), PasValue.FromInt(1)
                 }
             })
        Assert(!bridge.CallNpcFunc("StorageAllBagItems", malformed, out _),
            "malformed function ABI accepted");

    var emptyMerchant = MerchantNpc("empty", "3");
    emptyMerchant.AddNativePasProperty(9);
    bridge.CurrentNpc = emptyMerchant;
    var emptyPlayer = Player("Empty", ready: true);
    Assert(bridge.CallNpcFunc("StorageAllBagItems",
        new List<PasValue> { PasValue.FromObject(emptyPlayer) }, out result),
        "empty bag function rejected");
    Equal("你的背包没有东西", result.AsString(), "empty bag string");
    Assert(!emptyMerchant.NativeGoodsDirty, "empty bag marked goods dirty");

    var root = TempDirectory();
    try
    {
        var beforeTick = unchecked(merchant.NativeGoodsSaveTick + 59999);
        merchant.SaveNativeGoodsIfDue(beforeTick, root);
        Assert(!File.Exists(Merchant.GetNativeGoodsFilePath(root, "keeper", "3")),
            "dirty goods saved before 60 seconds");
        Assert(merchant.NativeGoodsDirty, "pre-60-second check cleared dirty");

        merchant.SaveNativeGoodsIfDue(unchecked(beforeTick + 1), root);
        var fileName = Merchant.GetNativeGoodsFilePath(root, "keeper", "3");
        Assert(File.Exists(fileName), "overdue dirty goods were not saved");
        Equal(3 * NativeMerchantGoodsCodec.RecordSize,
            File.ReadAllBytes(fileName).Length, "saved record length");
        Assert(!merchant.NativeGoodsDirty, "overdue save kept dirty flag");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

void UnrepresentableExtensionPreflight()
{
    ResetRuntimeState();
    var unsupported = new[]
    {
        UnbackedItem(item => item.ys1 = 1),
        UnbackedItem(item => item.jp1 = 1),
        UnbackedItem(item => item.pname = "source"),
        UnbackedItem(item => item.desc1 = "description"),
        UnbackedItem(item => item.desc2 = "description-2"),
        UnbackedItem(item => item.sourceTime = "timestamp"),
        UnbackedItem(item => item.killerName = "creator"),
        UnbackedItem(item => item.mapName = "map")
    };
    foreach (var item in unsupported)
    {
        Assert(!NativeMerchantGoodsCodec.TryEncode(item, out _, out var error),
            "unbacked extension encoded");
        Assert(!string.IsNullOrEmpty(error), "unbacked extension lost error");
        AssertThrows<InvalidDataException>(() =>
                NativeMerchantGoodsCodec.Encode(item),
            "Encode silently zeroed unbacked extension");
    }

    var coreOnly = UnbackedItem(_ => { });
    Assert(NativeMerchantGoodsCodec.TryEncode(coreOnly, out var coreRecord,
            out var coreError), coreError);
    Equal(NativeMerchantGoodsCodec.RecordSize, coreRecord.Length,
        "core-only item record length");

    var merchant = MerchantNpc("preflight", "4");
    merchant.AddNativePasProperty(9);
    var player = Player("Preflight", ready: true);
    var normal = Item(180, 1);
    var extended = unsupported[0];
    player.m_ItemList.Add(normal);
    player.m_ItemList.Add(extended);
    var bridge = new PasApiBridge { CurrentNpc = merchant, CurrentPlayer = player };

    Assert(bridge.CallNpcFunc("StorageAllBagItems",
        new List<PasValue> { PasValue.FromObject(player) }, out var result),
        "preflight rejection lost function dispatch");
    Equal(string.Empty, result.AsString(), "preflight rejection result");
    References(new[] { normal, extended }, player.m_ItemList,
        "preflight changed bag before all items passed");
    Equal(0, merchant.m_GoodsList.Count, "preflight changed merchant goods");
    Assert(!merchant.NativeGoodsDirty, "preflight marked goods dirty");
    Assert(!player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST),
        "preflight sent item deletion");
}

void BackedExtensionWholeBagPreflight()
{
    ResetRuntimeState();
    var unsupported = new[]
    {
        BackedItem(181, item => item.ys1 = 1),
        BackedItem(182, item => item.jp1 = 1),
        BackedItem(183, item => item.pname = "source"),
        BackedItem(184, item => item.desc1 = "description"),
        BackedItem(185, item => item.desc2 = "description-2"),
        BackedItem(186, item => item.sourceTime = "timestamp"),
        BackedItem(187, item => item.killerName = "creator"),
        BackedItem(188, item => item.mapName = "map")
    };
    foreach (var item in unsupported)
    {
        Assert(!NativeMerchantGoodsCodec.TryEncode(item, out _, out var error),
            "backed extension encoded");
        Assert(!string.IsNullOrEmpty(error), "backed extension lost error");
        AssertThrows<InvalidDataException>(() =>
                NativeMerchantGoodsCodec.Encode(item),
            "Encode silently zeroed backed extension");
    }

    var merchant = MerchantNpc("backed-preflight", "4");
    merchant.AddNativePasProperty(9);
    var player = Player("BackedPreflight", ready: true);
    var normal = Item(189, 1);
    var expected = new List<TUserItem> { normal };
    expected.AddRange(unsupported);
    foreach (var item in expected)
        player.m_ItemList.Add(item);
    var bridge = new PasApiBridge { CurrentNpc = merchant, CurrentPlayer = player };

    Assert(bridge.CallNpcFunc("StorageAllBagItems",
        new List<PasValue> { PasValue.FromObject(player) }, out var result),
        "backed preflight rejection lost function dispatch");
    Equal(string.Empty, result.AsString(), "backed preflight rejection result");
    References(expected, player.m_ItemList,
        "backed preflight changed bag before all items passed");
    Equal(0, merchant.m_GoodsList.Count,
        "backed preflight changed merchant goods");
    Assert(!merchant.NativeGoodsDirty, "backed preflight marked goods dirty");
    Assert(!player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST),
        "backed preflight sent item deletion");
}

void PartialFailureRemainsPersistable()
{
    ResetRuntimeState();
    var merchant = MerchantNpc("failure", "5");
    merchant.AddNativePasProperty(9);
    var player = Player("Failure", ready: true);
    var first = Item(201, 1);
    var last = Item(202, 2);
    player.m_ItemList.Add(first);
    player.m_ItemList.Add(last);
    var bridge = new PasApiBridge { CurrentNpc = merchant, CurrentPlayer = player };
    M2Share.LogStringList = null;
    try
    {
        AssertThrows<NullReferenceException>(() => bridge.CallNpcFunc(
            "StorageAllBagItems",
            new List<PasValue> { PasValue.FromObject(player) }, out _),
            "per-item log failure did not escape");
    }
    finally
    {
        M2Share.LogStringList = new ArrayList();
    }

    References(new[] { first }, player.m_ItemList,
        "failure rolled back or removed the wrong bag item");
    Equal(1, merchant.m_GoodsList.Count, "failure goods group count");
    References(new[] { last }, merchant.m_GoodsList[0],
        "failure lost transferred item");
    Assert(merchant.NativeGoodsDirty,
        "partial failure did not retain the transferred item as dirty");
    Assert(!player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST),
        "partial failure sent final batch deletion");

    var root = TempDirectory();
    try
    {
        merchant.SaveNativeGoodsIfDue(unchecked(
            merchant.NativeGoodsSaveTick + 60000), root);
        var fileName = Merchant.GetNativeGoodsFilePath(root, "failure", "5");
        Equal(NativeMerchantGoodsCodec.RecordSize,
            File.ReadAllBytes(fileName).Length,
            "partial failure item was not recoverably saved");
        Assert(!merchant.NativeGoodsDirty,
            "successful retry did not clear partial-failure dirty state");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

void AtomicSaveFailureRemainsRetryable()
{
    ResetRuntimeState();
    var merchant = MerchantNpc("atomic", "6");
    merchant.AddNativePasProperty(9);
    var player = Player("Atomic", ready: true);
    player.m_ItemList.Add(Item(250, 1));
    var bridge = new PasApiBridge { CurrentNpc = merchant, CurrentPlayer = player };
    Assert(bridge.CallNpcFunc("StorageAllBagItems",
        new List<PasValue> { PasValue.FromObject(player) }, out _),
        "atomic setup transfer failed");

    var root = TempDirectory();
    var fileName = Merchant.GetNativeGoodsFilePath(root, "atomic", "6");
    Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
    var original = new byte[] { 0x11, 0x22, 0x33, 0x44 };
    File.WriteAllBytes(fileName, original);
    var saveTick = merchant.NativeGoodsSaveTick;
    var dueTick = unchecked(saveTick + 60000);
    try
    {
        using (new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite,
                   FileShare.None))
        {
            AssertThrows<IOException>(() =>
                    merchant.SaveNativeGoodsIfDue(dueTick, root),
                "locked target did not inject an atomic save failure");
        }

        Equal(original, File.ReadAllBytes(fileName),
            "failed atomic replacement damaged the prior save");
        Assert(merchant.NativeGoodsDirty, "save failure cleared dirty state");
        Equal(saveTick, merchant.NativeGoodsSaveTick,
            "save failure advanced the retry deadline");
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(fileName)!,
                "*.tmp").Any(), "failed atomic save leaked a temp file");

        merchant.SaveNativeGoodsIfDue(dueTick, root);
        Equal(NativeMerchantGoodsCodec.RecordSize,
            File.ReadAllBytes(fileName).Length,
            "immediate retry did not replace the old save");
        Assert(!merchant.NativeGoodsDirty, "successful retry kept dirty state");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

void OpaqueRecordRoundTrip()
{
    var record = NativeRecord(301, 1, 0x41);
    record[0x40] = 0xD7;
    record[0x91] = 0x38;
    record[0xCF] = 0xA5;
    var item = NativeMerchantGoodsCodec.Decode(record);
    Equal(record, item.NativeRecord, "decode NativeRecord copy");
    Equal(record, NativeMerchantGoodsCodec.Encode(item),
        "opaque native bytes");

    item.Dura = 4321;
    var changed = NativeMerchantGoodsCodec.Encode(item);
    Equal((ushort)4321,
        BinaryPrimitives.ReadUInt16LittleEndian(changed.AsSpan(6, 2)),
        "known field overlay");
    Equal((byte)0xD7, changed[0x40], "unknown byte 0x40");
    Equal((byte)0x38, changed[0x91], "unknown byte 0x91");
    Equal((byte)0xA5, changed[0xCF], "unknown byte 0xCF");
}

void NpcSavePersistence()
{
    var root = TempDirectory();
    try
    {
        var first = NativeRecord(401, 1, 0x11);
        var second = NativeRecord(402, 1, 0x22);
        var third = NativeRecord(403, 2, 0x33);
        var merchant = MerchantNpc("script-name", "map-name");
        var fileName = Merchant.GetNativeGoodsFilePath(root,
            merchant.m_sScript, merchant.m_sMapName);
        Equal(Path.Combine(root, "NpcSave", "script-name-map-name.Sav"),
            fileName, "NpcSave path");
        Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
        File.WriteAllBytes(fileName, first.Concat(second).Concat(third)
            .Append((byte)0xEE).ToArray());

        merchant.m_GoodsList.Add(new List<TUserItem> { Item(999, 1) });
        merchant.LoadGoodRecord(root);
        Equal(2, merchant.m_GoodsList.Count, "loaded goods groups");
        Equal(new[] { 402, 401 }, merchant.m_GoodsList[0]
            .Select(item => item.MakeIndex).ToArray(), "native insert-zero order");
        Equal(new[] { 403 }, merchant.m_GoodsList[1]
            .Select(item => item.MakeIndex).ToArray(), "second loaded group");
        Equal(second, merchant.m_GoodsList[0][0].NativeRecord,
            "loaded opaque second record");
        Equal(first, merchant.m_GoodsList[0][1].NativeRecord,
            "loaded opaque first record");

        merchant.SaveGoodRecord(root);
        var saved = File.ReadAllBytes(fileName);
        Equal(3 * NativeMerchantGoodsCodec.RecordSize, saved.Length,
            "save discarded incomplete tail");
        Equal(second.Concat(first).Concat(third).ToArray(), saved,
            "group-major save order");

        merchant.m_GoodsList.Clear();
        merchant.SaveGoodRecord(root);
        Assert(!File.Exists(fileName), "empty goods did not delete .Sav");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

void ProductionNpcSaveRootResolution()
{
    var previousConfigPath = M2Share.sConfigPath;
    var previousEnvirDirectory = M2Share.g_Config.sEnvirDir;
    var root = TempDirectory();
    try
    {
        M2Share.sConfigPath = root;
        M2Share.g_Config.sEnvirDir = Path.Combine("..", "Envir");
        Equal(Path.GetFullPath(Path.Combine(root, "..", "Envir")),
            Merchant.GetNativeGoodsRootPath(), "relative EnvirDir root");

        var absolute = Path.Combine(root, "AbsoluteEnvir");
        M2Share.g_Config.sEnvirDir = absolute;
        Equal(Path.GetFullPath(absolute), Merchant.GetNativeGoodsRootPath(),
            "absolute EnvirDir root");
    }
    finally
    {
        M2Share.sConfigPath = previousConfigPath;
        M2Share.g_Config.sEnvirDir = previousEnvirDirectory;
        Directory.Delete(root, true);
    }
}

void NormalStopFlushesNativeMerchants()
{
    ResetRuntimeState();
    var root = TempDirectory();
    var previousRoot = M2Share.g_Config.sEnvirDir;
    var previousEngine = M2Share.UserEngine;
    try
    {
        var engine = new UserEngine();
        M2Share.UserEngine = engine;
        M2Share.g_Config.sEnvirDir = root;

        var bad = MerchantNpc("stop-bad", "7");
        bad.AddNativePasProperty(9);
        bad.m_GoodsList.Add(new List<TUserItem>
        {
            UnbackedItem(item => item.ys1 = 9)
        });
        engine.AddMerchant(bad);

        var good = MerchantNpc("stop-good", "7");
        good.AddNativePasProperty(9);
        good.m_GoodsList.Add(new List<TUserItem> { Item(501, 1) });
        engine.AddMerchant(good);

        var ordinary = MerchantNpc("stop-ordinary", "7");
        ordinary.m_GoodsList.Add(new List<TUserItem> { Item(502, 1) });
        engine.AddMerchant(ordinary);

        engine.Stop();

        Assert(bad.NativeGoodsDirty,
            "failed stop flush did not retain dirty state");
        var goodFile = Merchant.GetNativeGoodsFilePath(root, "stop-good", "7");
        Equal(NativeMerchantGoodsCodec.RecordSize,
            File.ReadAllBytes(goodFile).Length,
            "stop did not continue flushing AddNpcProp(9) merchants");
        Assert(!good.NativeGoodsDirty,
            "successful stop flush left merchant dirty");
        Assert(!File.Exists(Merchant.GetNativeGoodsFilePath(root,
                "stop-ordinary", "7")),
            "stop flushed a merchant without AddNpcProp(9)");
    }
    finally
    {
        M2Share.UserEngine = previousEngine;
        M2Share.g_Config.sEnvirDir = previousRoot;
        Directory.Delete(root, true);
    }
}

void SourceContract()
{
    var root = FindRepositoryRoot();
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var merchant = File.ReadAllText(Path.Combine(root, "GameSvr", "Npcs",
        "Merchant.cs"));
    var codec = File.ReadAllText(Path.Combine(root, "GameSvr", "DataStores",
        "NativeMerchantGoodsCodec.cs"));
    var userEngine = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UsrEngn.cs"));

    Equal(1, Count(bridge, "case \"storageallbagitems\":"),
        "StorageAllBagItems dispatch count");
    var methodDispatcher = Slice(bridge, "public bool CallNpcMethod(",
        "public bool CallNpcFunc(");
    Assert(!methodDispatcher.Contains("case \"storageallbagitems\":",
            StringComparison.Ordinal), "function remains in method dispatcher");
    var functionCase = Slice(bridge, "case \"storageallbagitems\":",
        "case \"clickupweaponnow\":");
    foreach (var required in new[]
             {
                 "args.Count != 1", "PasValueType.Object",
                 "CurrentNpc is not Merchant", "PasValue.FromString(",
                 "StorageAllBagItems(storagePlayer)"
             })
        Require(functionCase, required, "function ABI missing: " + required);
    Assert(!functionCase.Contains("CurrentPlayer", StringComparison.Ordinal),
        "function ignored explicit player ABI");

    var transfer = Slice(merchant,
        "internal string StorageAllBagItems(TPlayObject sender)",
        "private void CheckItemPrice");
    foreach (var required in new[]
             {
                 "sender.m_boReadyRun", "sender.m_btPermission <= 3",
                 "HasNativePasProperty(9)",
                 "NativeMerchantGoodsCodec.TryEncode(item, out _",
                 "sender.m_ItemList.Count - 1", "sender.m_ItemList.RemoveAt(i)",
                 "AddItemToGoodsList(item)", "MarkNativeGoodsDirty()",
                 "EnsureItemPrice(item.wIndex)",
                 "Grobal2.RM_SENDDELITEMLIST", "sender.WeightChanged()",
                 "\"10\" + \"\\t\""
             })
        Require(transfer, required, "transfer contract missing: " + required);
    Assert(transfer.IndexOf("sender.m_ItemList.RemoveAt(i)",
               StringComparison.Ordinal) <
           transfer.IndexOf("AddItemToGoodsList(item)",
               StringComparison.Ordinal),
        "goods insertion moved before bag removal");
    Assert(transfer.IndexOf("NativeMerchantGoodsCodec.TryEncode(item, out _",
               StringComparison.Ordinal) <
           transfer.IndexOf("sender.m_ItemList.RemoveAt(i)",
               StringComparison.Ordinal),
        "bag mutation starts before whole-bag serializability preflight");
    Assert(transfer.IndexOf("AddItemToGoodsList(item)",
               StringComparison.Ordinal) <
           transfer.IndexOf("MarkNativeGoodsDirty()",
               StringComparison.Ordinal) &&
           transfer.IndexOf("MarkNativeGoodsDirty()",
               StringComparison.Ordinal) <
           transfer.IndexOf("EnsureItemPrice(item.wIndex)",
               StringComparison.Ordinal),
        "per-item dirty/price order changed");
    Assert(!transfer.Contains("m_StorageItemList", StringComparison.Ordinal)
           && !transfer.Contains("SaveHumanRcd", StringComparison.Ordinal),
        "personal storage substitute returned");

    var dirtySave = Slice(merchant,
        "internal void SaveNativeGoodsIfDue", "internal string StorageAllBagItems");
    Require(dirtySave, "< 60000", "60-second threshold changed");
    Require(dirtySave, "HasNativePasProperty(9)",
        "dirty save lost AddNpcProp(9) gate");
    Require(dirtySave, "catch", "save failure no longer restores dirty state");
    Require(dirtySave, "_nativeGoodsDirty = true",
        "save failure no longer remains retryable");
    Assert(dirtySave.IndexOf("_nativeGoodsDirty = false",
               StringComparison.Ordinal) <
           dirtySave.IndexOf("SaveGoodRecord(rootPath)",
               StringComparison.Ordinal),
        "native dirty flag is not cleared before save");
    Require(merchant, "AtomicFile.WriteAllBytes(fileName, data)",
        "NpcSave no longer uses same-directory atomic replacement");
    foreach (var required in new[]
             {
                 "internal static bool TryEncode", "HasUnmappedExtensionData",
                 "if (HasUnmappedExtensionData(item))", "item.ys1 != 0", "item.jp1 != 0",
                 "item.pname", "item.desc1", "item.sourceTime"
             })
        Require(codec, required, "merchant codec preflight missing: " + required);
    Assert(!codec.Contains(
            "item.NativeRecord == null && HasUnmappedExtensionData(item)",
            StringComparison.Ordinal),
        "merchant codec only rejects unbacked extension data");

    var stop = Slice(userEngine, "public void Stop()",
        "private static void JoinThread");
    Require(stop, "JoinThread(_processAiThread);", "Stop lost AI join");
    Require(stop, "FlushNativeMerchantGoods();", "Stop lost merchant flush");
    Assert(stop.IndexOf("JoinThread(_processAiThread);", StringComparison.Ordinal) <
           stop.IndexOf("FlushNativeMerchantGoods();", StringComparison.Ordinal),
        "merchant flush moved before worker joins");
    foreach (var required in new[]
             {
                 "SnapshotMerchants()", "HasNativePasProperty(9)",
                  "merchant.FlushNativeGoods(currentTick,",
                  "Merchant.GetNativeGoodsRootPath()",
                 "catch (Exception ex)"
             })
        Require(stop, required, "Stop merchant flush missing: " + required);
    foreach (var required in new[]
             {
                 "SaveGoodRecord(GetNativeGoodsRootPath())",
                 "SaveNativeGoodsIfDue(HUtil32.GetTickCount(),",
                 "LoadGoodRecord(GetNativeGoodsRootPath())"
             })
        Require(merchant, required, "production NpcSave root missing: " + required);
    Require(bridge, "WantNativeMagicTowerWarMon(CurrentNpc)",
        "WantWarMon parallel implementation was overwritten");
}

void PrepareRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
        { Name = "ItemOne", Weight = 1, Price = 100 });
    M2Share.UserEngine.StdItemList.Add(new GoodItem
        { Name = "ItemTwo", Weight = 2, Price = 200 });
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
}

void PrepareRuntimeConfig()
{
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
}

void ResetRuntimeState()
{
    M2Share.LogStringList = new ArrayList();
}

Merchant MerchantNpc(string script, string map) => new()
{
    m_sScript = script,
    m_sMapName = map,
    m_sCharName = script
};

// 原生 sub_64392C @0x64396A `cmp byte [edi+0x675],3; jbe` 要求 m_btPermission > 3,
// 即整个 property-9 寄存子系统是 GM 专用的;测试主体默认满足该前置。
TPlayObject Player(string name, bool ready) => new()
{
    m_sCharName = name,
    m_sMapName = "test-map",
    m_nCurrX = 10,
    m_nCurrY = 20,
    m_boReadyRun = ready,
    m_btPermission = 4
};

TUserItem Item(int makeIndex, ushort itemIndex)
{
    return NativeMerchantGoodsCodec.Decode(
        NativeRecord(makeIndex, itemIndex, (byte)makeIndex));
}

TUserItem BackedItem(int makeIndex, Action<TUserItem> configure)
{
    var item = Item(makeIndex, 1);
    configure(item);
    return item;
}

TUserItem UnbackedItem(Action<TUserItem> configure)
{
    var item = new TUserItem
    {
        MakeIndex = 700,
        wIndex = 1,
        Dura = 1000,
        DuraMax = 2000,
        NativeRecord = null
    };
    configure(item);
    return item;
}

byte[] NativeRecord(int makeIndex, ushort itemIndex, byte seed)
{
    var record = new byte[NativeMerchantGoodsCodec.RecordSize];
    for (var i = 0; i < record.Length; i++)
        record[i] = unchecked((byte)(seed + i * 17));
    BinaryPrimitives.WriteInt32LittleEndian(record, makeIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), itemIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), 1000);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), 2000);
    return record;
}

string TempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(),
        "StorageAllBagItems-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

void Run(string name, Action test)
{
    try
    {
        test();
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception}");
    }
}

void References(IReadOnlyList<TUserItem> expected,
    IList<TUserItem> actual, string message)
{
    Equal(expected.Count, actual.Count, message + " count");
    for (var i = 0; i < expected.Count; i++)
        Assert(ReferenceEquals(expected[i], actual[i]), $"{message} index {i}");
}

void AssertThrows<TException>(Action action, string message)
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
    throw new InvalidOperationException(message);
}

void Equal<T>(T expected, T actual, string message)
{
    if (expected is Array expectedArray && actual is Array actualArray)
    {
        Assert(expectedArray.Cast<object>().SequenceEqual(
            actualArray.Cast<object>()), message);
        return;
    }
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

void Require(string source, string value, string message)
{
    Assert(source.Contains(value, StringComparison.Ordinal), message);
}

string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, "missing marker: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, "missing marker: " + endMarker);
    return source[start..end];
}

int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0;;)
    {
        offset = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (offset < 0) return count;
        count++;
        offset += value.Length;
    }
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 AppContext.BaseDirectory, Directory.GetCurrentDirectory()
             })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
}
