using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
M2Share.StartPointList = new List<TStartPoint>();

Equal(0x0078BCBCu, NativeSpecialDropItemRollCore.OriginalFunction,
    "original function");
Equal(0x0078BCD8u, NativeSpecialDropItemRollCore.OriginalConstructor,
    "original constructor");
Equal((byte)96, NativeSpecialDropItemRollCore.SpecialDropStdMode,
    "special-drop StdMode");
Equal(0x4C, NativeSpecialDropItemRollCore.DefinitionIntParam1Offset,
    "definition IntParam1 offset");
Equal(0x100, NativeSpecialDropItemRollCore.InstanceThresholdOffset,
    "instance threshold offset");
Equal(100, NativeSpecialDropItemRollCore.RandomBound,
    "Random bound");

CheckConstructorHydration();
CheckRuntimeEntryPoints();
CheckSpecialDropDeathTransaction();
CheckLimitBagItemDropLoader();
CheckLimitBagItemDropTransaction();
CheckDropItemDownNativeCommitOrder();

CheckCase(Threshold(0), 0, false, "zero threshold");
CheckCase(Threshold(1), 0, true, "zero draw below one");
CheckCase(Threshold(1), 1, false, "strict less-than equality");
CheckCase(Threshold(99), 98, true, "ordinary true boundary");
CheckCase(Threshold(99), 99, false, "ordinary false boundary");
CheckCase(Threshold(100), 99, true, "100 selects every native draw");
CheckCase(Threshold(int.MaxValue), 99, true, "positive signed maximum");
CheckCase(Threshold(int.MinValue), 0, false, "negative signed minimum");
CheckCase(Threshold(-1), 0, false, "all-FF dword is signed minus one");

CheckCase(new TUserItem { NativeItemPlus102 = 1 }, 0, true,
    "+0x102 contributes bits 16..23");
CheckCase(new TUserItem { NativeItemPlus103 = 1 }, 0, true,
    "+0x103 contributes bits 24..31");
CheckCase(new TUserItem { NativeItemPlus103 = 0x80 }, -1, false,
    "signed compare is retained for a negative threshold");

var orderItem = Threshold(0);
var orderCalls = 0;
var orderSelected = NativeSpecialDropItemRollCore.IsSelected(orderItem, bound =>
{
    orderCalls++;
    Equal(100, bound, "mutation-order Random bound");
    orderItem.NativeItemPlus100 = 1;
    return 0;
});
Check(orderSelected && orderCalls == 1,
    "Random(100) runs exactly once before the dword threshold read");

Console.WriteLine(
    "PASS NativeSpecialDropItemRollCoreCheck ctor=sub_78BCD8 " +
    "fresh+record-load=hydrated function=sub_78BCBC " +
    "random=once-before-read threshold=int32-le signed-compare=exact " +
    "limitbag=sub_697FD4+sub_77BF50+sub_77C028+sub_748D48");
return 0;

static void CheckConstructorHydration()
{
    var special = new GoodItem
    {
        StdMode = NativeSpecialDropItemRollCore.SpecialDropStdMode
    };
    foreach (var value in new[]
             {
                 0, 1, 99, 100, 0x12345678, -1, int.MinValue, int.MaxValue
             })
    {
        special.IntParam1 = value;
        var item = new TUserItem
        {
            NativeItemPlus100 = 0xAA,
            NativeItemPlus101 = 0xBB,
            NativeItemPlus102 = 0xCC,
            NativeItemPlus103 = 0xDD
        };
        Check(NativeSpecialDropItemRollCore.HydrateConstructorState(item,
                special),
            $"special constructor rejected IntParam1={value}");
        AssertThreshold(item, value,
            $"special constructor IntParam1={value}");
    }

    var ordinary = new GoodItem { StdMode = 79, IntParam1 = 0x01020304 };
    var untouched = new TUserItem
    {
        NativeItemPlus100 = 0x11,
        NativeItemPlus101 = 0x22,
        NativeItemPlus102 = 0x33,
        NativeItemPlus103 = 0x44
    };
    Check(!NativeSpecialDropItemRollCore.HydrateConstructorState(untouched,
            ordinary),
        "non-special definition was accepted");
    Equal((byte)0x11, untouched.NativeItemPlus100,
        "non-special +0x100 changed");
    Equal((byte)0x22, untouched.NativeItemPlus101,
        "non-special +0x101 changed");
    Equal((byte)0x33, untouched.NativeItemPlus102,
        "non-special +0x102 changed");
    Equal((byte)0x44, untouched.NativeItemPlus103,
        "non-special +0x103 changed");
}

static void CheckRuntimeEntryPoints()
{
    const int threshold = unchecked((int)0x89ABCDEF);
    var engine = new UserEngine();
    engine.StdItemList.Add(new GoodItem
    {
        Name = "SpecialDropHydrationAudit",
        StdMode = NativeSpecialDropItemRollCore.SpecialDropStdMode,
        DuraMax = 1000,
        IntParam1 = threshold
    });
    M2Share.UserEngine = engine;

    TUserItem fresh = null;
    Check(engine.CopyToUserItemFromName("SpecialDropHydrationAudit",
            ref fresh, 1234),
        "central fresh item construction failed");
    AssertThreshold(fresh, threshold,
        "central fresh item construction");

    var nativeRecord = BuildNativeRecord(1);
    Check(NativeMailAttachmentCodec.TryDecode(nativeRecord, out var mail,
            out var mailError),
        "mail record decode failed: " + mailError);
    AssertThreshold(mail, threshold, "mail record hydration");

    var merchant = NativeMerchantGoodsCodec.Decode(nativeRecord);
    AssertThreshold(merchant, threshold, "merchant record hydration");

    Check(LegacyUserItem208Codec.TryDecode(
            Convert.ToHexString(nativeRecord), out var legacy,
            out var legacyError),
        "legacy 208 record decode failed: " + legacyError);
    AssertThreshold(legacy, threshold, "legacy 208 record hydration");

    var humanRecord = new THumDataInfo();
    humanRecord.Header.dCreateDate = new DateTime(2020, 1, 2).ToOADate();
    humanRecord.Data.sCharName = "SpecialDropHydrationAudit";
    humanRecord.Data.sCurMap = "0";
    humanRecord.Data.HumItems[0] = LoadedItem(1);
    humanRecord.Data.BagItems[0] = LoadedItem(1);
    humanRecord.Data.StorageItems[0] = LoadedItem(1);
    var player = new TPlayObject();
    var getHumData = typeof(UserEngine).GetMethod("GetHumData",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(UserEngine).FullName,
            "GetHumData");
    try
    {
        getHumData.Invoke(engine, new object[] { player, humanRecord });
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }

    AssertThreshold(player.m_UseItems[0], threshold,
        "human equipped record hydration");
    AssertThreshold(player.m_ItemList[0], threshold,
        "human bag record hydration");
    AssertThreshold(player.m_StorageItemList[0], threshold,
        "human storage record hydration");
}

static void CheckSpecialDropDeathTransaction()
{
    var oldEngine = M2Share.UserEngine;
    var oldRandom = M2Share.RandomNumber;
    var oldAuthOpen = M2Share.g_Config.boAuthOpen;
    try
    {
        var engine = new UserEngine();
        M2Share.UserEngine = engine;
        M2Share.RandomNumber = RandomNumber.GetInstance();
        var map = CreateBlankMap();

        var special100 = AddDefinition(engine, "Spec100", 96, 100);
        var special0 = AddDefinition(engine, "Spec0", 96, 0);
        var ordinary = AddDefinition(engine, "Ordinary", 5, 100);
        var mode5Blocked = AddDefinition(engine, "SpecMode5", 96, 100,
            0x0020);

        M2Share.g_Config.boAuthOpen = false;
        var player = NewRecordingPlayer(map, "spec-player", 20, 20);
        var first = MakeItem(engine, special100, 1001);
        var ordinaryItem = MakeItem(engine, ordinary, 1002);
        var zero = MakeItem(engine, special0, 1003);
        var last = MakeItem(engine, special100, 1004);
        var firstId = player.EnsureClientItemId(first);
        var lastId = player.EnsureClientItemId(last);
        player.m_ItemList.Add(first);
        player.m_ItemList.Add(ordinaryItem);
        player.m_ItemList.Add(zero);
        player.m_ItemList.Add(last);
        var draws = new List<int>();
        M2Share.LogStringList.Clear();

        player.NativeSpecialDropBagItems(bound =>
        {
            draws.Add(bound);
            return 0;
        });

        Check(draws.SequenceEqual(new[] { 100, 100, 100 }),
            "sub_740300 must scan backwards and skip RNG for non-special classes");
        Check(player.m_ItemList.SequenceEqual(new[] { ordinaryItem, zero }),
            "selected special items were not removed in reverse-scan order");
        Equal(2, CountGroundItems(map),
            "two selected special items must land on the ground");
        var playerDelete = player.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Equal(2, playerDelete.nParam1, "player 0x27A4 count");
        var playerBody = playerDelete.Payload as byte[];
        Check(playerBody is { Length: 8 }
              && BinaryPrimitives.ReadInt32LittleEndian(playerBody) == lastId
              && BinaryPrimitives.ReadInt32LittleEndian(playerBody.AsSpan(4))
                  == firstId,
            "player 0x27A4 ClientItemID order must follow reverse removal");
        player.Operate(ToProcessMessage(playerDelete));
        Check(player.BinaryPackets.Count == 1
              && player.BinaryPackets[0].Header.Ident == Grobal2.SM_DELITEMS
              && player.BinaryPackets[0].Body.SequenceEqual(playerBody),
            "player SM_DELITEMS must preserve the native count*4 body");
        Check(M2Share.LogStringList.OfType<string>().SequenceEqual(new[]
              {
                  "15\t0\t20\t20\tspec-player\tSpec100\t1004\t1\t1",
                  "15\t0\t20\t20\tspec-player\tSpec100\t1001\t1\t1"
              }),
            "successful player ground drops must emit native type-15 records");

        M2Share.g_Config.boAuthOpen = true;
        M2Share.LogStringList.Clear();
        var blocked = NewAuthPlayer(map, "spec-blocked", 25, 25, false);
        var blockedItem = MakeItem(engine, mode5Blocked, 2001);
        blocked.m_ItemList.Add(blockedItem);
        blocked.NativeSpecialDropBagItems(_ => 0);
        Equal(1, blocked.AuthenticationCalls,
            "mode-5 rejection must stop before the second auth query");
        Check(ReferenceEquals(blocked.m_ItemList.Single(), blockedItem)
              && blocked.m_MsgList.All(message =>
                  message.wIdent != Grobal2.RM_SENDDELITEMLIST),
            "mode-5 rejection must retain the item and suppress 0x27A4");
        Equal(0, M2Share.LogStringList.Count,
            "mode-5 rejection must not emit the type-0x5E log");

        M2Share.LogStringList.Clear();
        var destroyed = NewAuthPlayer(map, "spec-destroy", 30, 30,
            false, true);
        var destroyedItem = MakeItem(engine, special100, 3001);
        destroyed.m_ItemList.Add(destroyedItem);
        var groundBeforeDestroy = CountGroundItems(map);
        destroyed.NativeSpecialDropBagItems(_ => 0);
        Equal(2, destroyed.AuthenticationCalls,
            "destroy branch must perform two independent auth queries");
        Equal(0, destroyed.m_ItemList.Count,
            "destroy branch did not remove the item");
        Equal(groundBeforeDestroy, CountGroundItems(map),
            "destroy branch incorrectly placed the item on the ground");
        Check(M2Share.LogStringList.OfType<string>().Single().EndsWith("\t",
                StringComparison.Ordinal),
            "second auth success must leave the type-0x5E reason empty");

        M2Share.LogStringList.Clear();
        var gift = NewAuthPlayer(map, "spec-gift", 35, 35, false, true);
        var giftItem = MakeItem(engine, special100, 3002);
        giftItem.NativeGiftItem = 1;
        gift.m_ItemList.Add(giftItem);
        gift.NativeSpecialDropBagItems(_ => 0);
        Check(M2Share.LogStringList.OfType<string>().Single().EndsWith(
                "\t" + NativeItemDropDestroy.DeathBagGiftNotice,
                StringComparison.Ordinal),
            "gift reason must overwrite the second authentication result");

        M2Share.g_Config.boAuthOpen = false;
        var owner = NewRecordingPlayer(map, "spec-owner", 40, 40);
        var hero = new HeroObject
        {
            m_Master = owner,
            m_PEnvir = map,
            m_sMapName = map.sMapName,
            m_sCharName = "spec-hero",
            m_nCurrX = 40,
            m_nCurrY = 40
        };
        var heroItem = MakeItem(engine, special100, 4001);
        var heroItemId = owner.EnsureClientItemId(heroItem);
        hero.m_ItemList.Add(heroItem);
        var groundBeforeHero = CountGroundItems(map);
        M2Share.LogStringList.Clear();
        hero.NativeSpecialDropBagItems(_ => 0);
        Equal(groundBeforeHero + 1, CountGroundItems(map),
            "hero must bypass the player auth destroy arm and land the item");
        var heroDelete = hero.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Check(heroDelete.nParam1 == 1
              && heroDelete.Payload is byte[] { Length: 4 } heroBody
              && BinaryPrimitives.ReadInt32LittleEndian(heroBody) == heroItemId,
            "hero 0x27A4 payload must be one raw ClientItemID dword");
        Equal("15\t0\t40\t40\tspec-hero\tSpec100\t4001\t1\tspec-owner",
            M2Share.LogStringList.OfType<string>().Single(),
            "hero ground log actor/reason fields");
        var heroDeleteProcess = ToProcessMessage(heroDelete);

        owner.m_boDeath = false;
        owner.m_boGhost = false;
        hero.Operate(heroDeleteProcess);
        owner.m_boDeath = true;
        hero.Operate(heroDeleteProcess);
        owner.m_boDeath = false;
        owner.m_boGhost = true;
        hero.Operate(heroDeleteProcess);
        owner.m_boDeath = true;
        hero.Operate(heroDeleteProcess);
        Equal(2, owner.BinaryPackets.Count,
            "SM_HERO_DELITEMS must ignore owner death but suppress owner ghost");
        Check(owner.BinaryPackets.All(packet =>
                  packet.Header.Ident == Grobal2.SM_HERO_DELITEMS
                  && packet.Body.Length == 4
                  && BinaryPrimitives.ReadInt32LittleEndian(packet.Body)
                      == heroItemId),
            "SM_HERO_DELITEMS forwarded header/body");
        owner.m_boDeath = false;
        owner.m_boGhost = false;

        // Exercise the real THumanKind.Die policy connection: ONLYDROPSPEC must
        // run this worker exclusively and leave ordinary bag/equipment items intact.
        map.Flag.boONLYDROPSPEC = true;
        var routed = NewPlayer(map, "spec-routed", 50, 50);
        var routedSpecial = MakeItem(engine, special100, 5001);
        var routedOrdinary = MakeItem(engine, ordinary, 5002);
        var routedEquip = MakeItem(engine, ordinary, 5003);
        routed.m_ItemList.Add(routedSpecial);
        routed.m_ItemList.Add(routedOrdinary);
        routed.m_UseItems[0] = routedEquip;
        routed.Die();
        Check(routed.m_ItemList.Count == 1
              && ReferenceEquals(routed.m_ItemList[0], routedOrdinary)
              && ReferenceEquals(routed.m_UseItems[0], routedEquip),
            "ONLYDROPSPEC must not fall through to normal bag/equipment workers");
        map.Flag.boONLYDROPSPEC = false;
    }
    finally
    {
        M2Share.UserEngine = oldEngine;
        M2Share.RandomNumber = oldRandom;
        M2Share.g_Config.boAuthOpen = oldAuthOpen;
        M2Share.LogStringList.Clear();
    }
}

static void CheckLimitBagItemDropLoader()
{
    var enabled = CreateBlankMap();
    enabled.sMapName = "MAP-A";
    enabled.Flag.boLIMITBAGITEMDROP = true;
    var disabled = CreateBlankMap();
    disabled.sMapName = "MAP-B";
    var requestedNames = new List<string>();
    var diagnostics = new List<string>();
    var fixture = Path.Combine(AppContext.BaseDirectory,
        "MapDropLimitBagItems.audit.xml");
    var moduleRoot = Path.Combine(AppContext.BaseDirectory,
        "limitbag-module-" + Guid.NewGuid().ToString("N"));
    File.WriteAllText(fixture,
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <Describle>
          <Maps>
            <Map>
              <Name>  map-a  </Name>
              <Items>
                <i Name="Ore" Rnd="1" Ranger="2" />
                <i Name="Ore" Rnd="9" Ranger="10" />
                <AnyTag Name=" ore " Rnd="-1" Ranger="0" />
                <i Name="MissingValues" />
              </Items>
            </Map>
            <Map><Name>map-b</Name><Items><i Name="Blocked" Rnd="1" Ranger="1" /></Items></Map>
            <Map><Name>missing-map</Name><Items><i Name="Missing" Rnd="1" Ranger="1" /></Items></Map>
          </Maps>
        </Describle>
        """);

    try
    {
        Envirnoment Resolve(string mapName)
        {
            requestedNames.Add(mapName);
            return mapName switch
            {
                "MAP-A" => enabled,
                "MAP-B" => disabled,
                _ => null
            };
        }

        Check(NativeLimitBagItemDropLoader.TryApply(fixture, Resolve,
                out var error, diagnostics.Add),
            "limit-bag XML load failed: " + error);
        Check(requestedNames.SequenceEqual(new[]
              {
                  "MAP-A", "MAP-B", "MISSING-MAP"
              }),
            "map names must use native control-space trim and ASCII uppercase");
        Equal(2, diagnostics.Count,
            "disabled and missing maps must both be diagnosed");
        Equal(3, enabled.NativeLimitBagItemDrops.Count,
            "enabled map rule count");
        Check(enabled.NativeLimitBagItemDrops.TryGet("Ore", out var ore),
            "exact Ore rule missing");
        Equal(1, ore.Rnd, "first duplicate Rnd must win");
        Equal(2, ore.Ranger, "first duplicate Ranger must win");
        Check(enabled.NativeLimitBagItemDrops.TryGet(" ore ", out var spaced),
            "item names must not be trimmed");
        Equal(-1, spaced.Rnd, "signed negative Rnd");
        Equal(0, spaced.Ranger, "zero Ranger");
        Check(enabled.NativeLimitBagItemDrops.TryGet("MissingValues",
                out var missingValues)
              && missingValues.Rnd == 0 && missingValues.Ranger == 0,
            "missing numeric attributes must default to zero");
        Check(!enabled.NativeLimitBagItemDrops.TryGet("ore", out _),
            "item rule lookup must remain case-sensitive");
        Equal(0, disabled.NativeLimitBagItemDrops.Count,
            "non-LIMITBAGITEMDROP map accepted rules");
        Check(NativeLimitBagItemDropLoader.GetDefaultPath("ROOT").EndsWith(
                Path.Combine("Share", "EngineConfig", "\u9759\u6001\u5730\u56fe\u7ba1\u7406",
                    "MapDropLimitBagItems.xml"),
                StringComparison.Ordinal),
            "default deployed configuration path");

        enabled.sMapDesc = "MISSING-MAP";
        var manager = new MapManager();
        var mapIndex = (Dictionary<string, Envirnoment>)(
            typeof(MapManager).GetField("m_MapList",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(
                manager)
            ?? throw new MissingFieldException(typeof(MapManager).FullName,
                "m_MapList"));
        mapIndex.Add(enabled.sMapName, enabled);
        mapIndex.Add(disabled.sMapName, disabled);
        Check(ReferenceEquals(manager.FindMap("MISSING-MAP"), enabled),
            "fixture must exercise the broad map-alias fallback");
        Check(manager.FindMapByNativeName("MISSING-MAP") == null,
            "native LIMIT lookup must not accept map descriptions as aliases");
        Check(NativeLimitBagItemDropLoader.TryApply(fixture,
                manager.FindMapByNativeName, out error, diagnostics.Add),
            "exact-name LIMIT reload failed: " + error);
        Check(!enabled.NativeLimitBagItemDrops.TryGet("Missing", out _),
            "D411-style aliases must not satisfy D411~01 LIMIT rules");

        var moduleDirectory = Path.Combine(moduleRoot, "Share",
            "EngineConfig", NativeLimitBagItemDropLoader.ConfigDirectory);
        Directory.CreateDirectory(moduleDirectory);
        var mainConfig = Path.Combine(moduleDirectory,
            NativeLimitBagItemDropLoader.MainConfigFileName);
        File.WriteAllText(mainConfig,
            "[Set]\r\nAutoStart=True\r\n"
            + $"[{NativeLimitBagItemDropLoader.ConfigSection}]\r\n"
            + "filename=CustomLimit.xml\r\nautoload=True\r\n",
            HUtil32.GbkEncoding);
        Check(NativeLimitBagItemDropLoader.TryResolveAutoLoadFile(
                moduleRoot, out var autoLoad, out var resolvedFile,
                out error)
              && autoLoad
              && resolvedFile == Path.GetFullPath(Path.Combine(
                  moduleDirectory, "CustomLimit.xml")),
            "main.ini FileName/AutoLoad resolution: " + error);

        File.WriteAllText(mainConfig,
            "[Set]\r\nAutoStart=True\r\n"
            + $"[{NativeLimitBagItemDropLoader.ConfigSection}]\r\n"
            + "FileName=MustNotLoad.xml\r\nAutoLoad=False\r\n",
            HUtil32.GbkEncoding);
        Check(NativeLimitBagItemDropLoader.TryResolveAutoLoadFile(
                moduleRoot, out autoLoad, out resolvedFile, out error)
              && !autoLoad && resolvedFile.Length == 0,
            "AutoLoad=False must suppress the XML path: " + error);

        File.WriteAllText(mainConfig,
            "[Set]\r\nAutoStart=False\r\n"
            + $"[{NativeLimitBagItemDropLoader.ConfigSection}]\r\n"
            + "FileName=MustNotStart.xml\r\nAutoLoad=True\r\n",
            HUtil32.GbkEncoding);
        Check(NativeLimitBagItemDropLoader.TryResolveAutoLoadFile(
                moduleRoot, out autoLoad, out resolvedFile, out error)
              && !autoLoad && resolvedFile.Length == 0,
            "module AutoStart=False must suppress the feature loader: "
            + error);

        File.Delete(mainConfig);
        Check(!NativeLimitBagItemDropLoader.TryResolveAutoLoadFile(
                  moduleRoot, out _, out _, out _)
              && !File.Exists(mainConfig),
            "missing main.ini must fail closed without creating a file");
        const string unrelatedMain = "[Set]\r\nAutoStart=True\r\n";
        File.WriteAllText(mainConfig, unrelatedMain, HUtil32.GbkEncoding);
        Check(!NativeLimitBagItemDropLoader.TryResolveAutoLoadFile(
                  moduleRoot, out _, out _, out _)
              && File.ReadAllText(mainConfig, HUtil32.GbkEncoding)
                  == unrelatedMain,
            "missing section must fail closed without rewriting main.ini");
    }
    finally
    {
        File.Delete(fixture);
        if (Directory.Exists(moduleRoot))
            Directory.Delete(moduleRoot, true);
    }
}

static void CheckLimitBagItemDropTransaction()
{
    var oldEngine = M2Share.UserEngine;
    var oldRandom = M2Share.RandomNumber;
    try
    {
        var engine = new UserEngine();
        M2Share.UserEngine = engine;
        M2Share.RandomNumber = RandomNumber.GetInstance();
        var map = CreateBlankMap();

        var firstDefinition = AddDefinition(engine, "LimitFirst", 5, 0);
        var noRuleDefinition = AddDefinition(engine, "LimitNoRule", 5, 0);
        var failedDefinition = AddDefinition(engine, "LimitFailed", 5, 0);
        var lastDefinition = AddDefinition(engine, "LimitLast", 5, 0);
        map.NativeLimitBagItemDrops.TryAdd(firstDefinition.Name, 1, 2);
        map.NativeLimitBagItemDrops.TryAdd(failedDefinition.Name, 0, 7);
        map.NativeLimitBagItemDrops.TryAdd(lastDefinition.Name, 3, 4);

        var player = NewRecordingPlayer(map, "limit-player", 20, 20);
        var first = MakeItem(engine, firstDefinition, 6001);
        var noRule = MakeItem(engine, noRuleDefinition, 6002);
        var failed = MakeItem(engine, failedDefinition, 6003);
        var last = MakeItem(engine, lastDefinition, 6004);
        var firstId = player.EnsureClientItemId(first);
        var lastId = player.EnsureClientItemId(last);
        player.m_ItemList.Add(first);
        player.m_ItemList.Add(noRule);
        player.m_ItemList.Add(failed);
        player.m_ItemList.Add(last);
        var bounds = new List<int>();
        M2Share.LogStringList.Clear();

        player.NativeLimitBagItemDropItems(bound =>
        {
            bounds.Add(bound);
            return 0;
        });

        Check(bounds.SequenceEqual(new[] { 4, 7, 2 }),
            "sub_748D48 RNG order/bounds and no-rule skip");
        Check(player.m_ItemList.SequenceEqual(new[] { noRule, failed }),
            "limit-bag reverse scan removal/retention");
        Equal(2, CountGroundItems(map),
            "selected limit-bag items must land on the ground");
        var delete = player.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        var deleteBody = delete.Payload as byte[];
        Check(delete.nParam1 == 2
              && deleteBody is { Length: 8 }
              && BinaryPrimitives.ReadInt32LittleEndian(deleteBody) == lastId
              && BinaryPrimitives.ReadInt32LittleEndian(deleteBody.AsSpan(4))
                  == firstId,
            "limit-bag player delete batch/order");
        player.Operate(ToProcessMessage(delete));
        Check(player.BinaryPackets.Count == 1
              && player.BinaryPackets[0].Header.Ident == Grobal2.SM_DELITEMS
              && player.BinaryPackets[0].Body.SequenceEqual(deleteBody),
            "limit-bag player dispatcher must send exactly count*4 bytes");
        Check(M2Share.LogStringList.OfType<string>().SequenceEqual(new[]
              {
                  "15\t0\t20\t20\tlimit-player\tLimitLast\t6004\t1\t1",
                  "15\t0\t20\t20\tlimit-player\tLimitFirst\t6001\t1\t1"
              }),
            "limit-bag successful placement logs");

        var placementFailure = NewPlayer(map, "limit-failure", 25, 25);
        var retained = MakeItem(engine, firstDefinition, 6101);
        placementFailure.m_ItemList.Add(retained);
        var failureDraws = 0;
        placementFailure.NativeLimitBagItemDropItems(bound =>
        {
            failureDraws++;
            Equal(2, bound, "placement-failure Ranger");
            return 0;
        }, _ => false);
        Check(failureDraws == 1
              && ReferenceEquals(placementFailure.m_ItemList.Single(),
                  retained)
              && placementFailure.m_MsgList.All(message =>
                  message.wIdent != Grobal2.RM_SENDDELITEMLIST),
            "failed placement must retain item and suppress delete batch");

        var zeroMap = CreateBlankMap();
        zeroMap.NativeLimitBagItemDrops.TryAdd(firstDefinition.Name, 1, 0);
        var zeroPlayer = NewPlayer(zeroMap, "limit-zero", 30, 30);
        zeroPlayer.m_ItemList.Add(MakeItem(engine, firstDefinition, 6201));
        var zeroBounds = new List<int>();
        zeroPlayer.NativeLimitBagItemDropItems(bound =>
        {
            zeroBounds.Add(bound);
            return 0;
        });
        Check(zeroBounds.SequenceEqual(new[] { 0 })
              && zeroPlayer.m_ItemList.Count == 0,
            "Ranger zero must still draw once and compare returned zero");
        var zeroDelete = zeroPlayer.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Check(zeroDelete.Payload is byte[] { Length: 4 } zeroBody
              && BinaryPrimitives.ReadInt32LittleEndian(zeroBody) == 0,
            "sub_748D48 must transmit item+0x18 without allocating an ID");

        var bufferMap = CreateBlankMap();
        var bufferDefinition = AddDefinition(engine, "LimitBuffer", 5, 0);
        bufferMap.NativeLimitBagItemDrops.TryAdd(bufferDefinition.Name, 1, 1);
        var bufferPlayer = NewPlayer(bufferMap, "limit-buffer", 35, 35);
        var insertionIds = new List<int>();
        for (var index = 0;
             index < TBaseObject.NativeLimitBagItemDropDeleteBufferCount;
             index++)
        {
            var item = MakeItem(engine, bufferDefinition, 6300 + index);
            insertionIds.Add(bufferPlayer.EnsureClientItemId(item));
            bufferPlayer.m_ItemList.Add(item);
        }
        bufferPlayer.NativeLimitBagItemDropItems(_ => 0, _ => true);
        var bufferDelete = bufferPlayer.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Check(bufferPlayer.m_ItemList.Count == 0
              && bufferDelete.nParam1 == 50
              && bufferDelete.Payload is byte[] { Length: 200 } bufferBody
              && Enumerable.Range(0, 50).Select(index =>
                    BinaryPrimitives.ReadInt32LittleEndian(
                        bufferBody.AsSpan(index * sizeof(int), sizeof(int))))
                  .SequenceEqual(insertionIds.AsEnumerable().Reverse()),
            "native 200-byte/50-ID delete-buffer boundary");

        var heroMap = CreateBlankMap();
        heroMap.NativeLimitBagItemDrops.TryAdd(firstDefinition.Name, 1, 1);
        var owner = NewRecordingPlayer(heroMap, "limit-owner", 40, 40);
        var hero = new HeroObject
        {
            m_Master = owner,
            m_PEnvir = heroMap,
            m_sMapName = heroMap.sMapName,
            m_sCharName = "limit-hero",
            m_nCurrX = 40,
            m_nCurrY = 40
        };
        var heroItem = MakeItem(engine, firstDefinition, 6401);
        var heroId = owner.EnsureClientItemId(heroItem);
        hero.m_ItemList.Add(heroItem);
        hero.NativeLimitBagItemDropItems(_ => 0);
        var heroDelete = hero.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Check(heroDelete.nParam1 == 1
              && heroDelete.Payload is byte[] { Length: 4 } heroBody
              && BinaryPrimitives.ReadInt32LittleEndian(heroBody) == heroId,
            "limit-bag hero raw count*4 delete body");

        var routedMap = CreateBlankMap();
        routedMap.Flag.boLIMITBAGITEMDROP = true;
        routedMap.NativeLimitBagItemDrops.TryAdd(firstDefinition.Name, 1, 0);
        var routed = NewPlayer(routedMap, "limit-routed", 50, 50);
        var routedSelected = MakeItem(engine, firstDefinition, 6501);
        var routedOrdinary = MakeItem(engine, noRuleDefinition, 6502);
        var routedEquip = MakeItem(engine, noRuleDefinition, 6503);
        routed.m_ItemList.Add(routedSelected);
        routed.m_ItemList.Add(routedOrdinary);
        routed.m_UseItems[0] = routedEquip;
        routed.Die();
        Check(routed.m_ItemList.Count == 1
              && ReferenceEquals(routed.m_ItemList[0], routedOrdinary)
              && ReferenceEquals(routed.m_UseItems[0], routedEquip),
            "LIMITBAGITEMDROP must run exclusively without normal bag/equipment fallback");
    }
    finally
    {
        M2Share.UserEngine = oldEngine;
        M2Share.RandomNumber = oldRandom;
        M2Share.LogStringList.Clear();
    }
}

static void CheckDropItemDownNativeCommitOrder()
{
    var oldEngine = M2Share.UserEngine;
    var oldRandom = M2Share.RandomNumber;
    var oldItemUnit = M2Share.ItemUnit;
    try
    {
        var engine = new UserEngine();
        M2Share.UserEngine = engine;
        M2Share.ItemUnit = new ItemUnit();
        var meatDefinition = AddDefinition(engine, "CommitMeat", 40, 0);
        var lookDefinition = AddDefinition(engine, "CommitLook", 45, 0);
        lookDefinition.Looks = 700;
        lookDefinition.Shape = 5;

        var blockedMap = CreateBlankMap();
        blockedMap.SetMapXYFlag(10, 10, false);
        var blocked = NewPlayer(blockedMap, "commit-blocked", 10, 10);
        var meat = MakeItem(engine, meatDefinition, 7001);
        meat.Dura = 5000;
        Check(!blocked.DropItemDown(meat, 0, true, null, blocked)
              && meat.Dura == 5000,
            "StdMode 40 durability must wait for successful placement");

        var random = new CountingRandomNumber();
        M2Share.RandomNumber = random;
        var look = MakeItem(engine, lookDefinition, 7002);
        Check(!blocked.DropItemDown(look, 0, true, null, blocked)
              && random.BoundedCalls == 0,
            "StdMode 45 appearance RNG must wait for successful placement");

        var successMap = CreateBlankMap();
        var success = NewPlayer(successMap, "commit-success", 20, 20);
        var committedMeat = MakeItem(engine, meatDefinition, 7003);
        committedMeat.Dura = 5000;
        Check(success.DropItemDown(committedMeat, 0, true, null, success)
              && committedMeat.Dura == 3000,
            "StdMode 40 successful placement must commit durability loss");
        var committedLook = MakeItem(engine, lookDefinition, 7004);
        random.Reset();
        Check(success.DropItemDown(committedLook, 0, true, null, success)
              && random.BoundedCalls == 1,
            "StdMode 45 successful placement must draw exactly once");

        var namedDefinition = AddDefinition(engine, "NativeGroundName", 5,
            0);
        var namedItem = MakeItem(engine, namedDefinition, 7005);
        namedItem.btValue[13] = 1;
        M2Share.ItemUnit.AddCustomItemName(namedItem.MakeIndex,
            namedItem.wIndex, "CustomGroundName");
        var namedMap = CreateBlankMap();
        var namedPlayer = NewPlayer(namedMap, "commit-name", 30, 30);
        M2Share.LogStringList.Clear();
        Check(namedPlayer.DropItemDown(namedItem, 0, true, null,
                namedPlayer),
            "standard-name placement failed");
        Equal("NativeGroundName", namedMap.GetItem(30, 30).Name,
            "ground display must use sub_784568 standard name");
        Check(M2Share.LogStringList.OfType<string>().Single().Contains(
                "\tNativeGroundName\t", StringComparison.Ordinal),
            "ground log must use sub_784568 standard name");

        var exceptional = new TPlayObject
        {
            m_PEnvir = null,
            m_sCharName = "commit-exception",
            m_nCurrX = 1,
            m_nCurrY = 1
        };
        var exceptionalItem = MakeItem(engine, namedDefinition, 7006);
        Check(!exceptional.DropItemDown(exceptionalItem, 0, true, null,
                exceptional),
            "DropItemDown must swallow native transaction exceptions");
    }
    finally
    {
        M2Share.UserEngine = oldEngine;
        M2Share.RandomNumber = oldRandom;
        M2Share.ItemUnit = oldItemUnit;
        M2Share.LogStringList.Clear();
    }
}

static GoodItem AddDefinition(UserEngine engine, string name, byte stdMode,
    int threshold, ushort reserved02 = 0)
{
    var definition = new GoodItem
    {
        Name = name,
        StdMode = stdMode,
        DuraMax = 1000,
        IntParam1 = threshold,
        NativeReserved02 = reserved02,
        NativeWireIndex = unchecked((ushort)engine.StdItemList.Count)
    };
    engine.StdItemList.Add(definition);
    return definition;
}

static TUserItem MakeItem(UserEngine engine, GoodItem definition,
    int makeIndex)
{
    TUserItem item = null;
    Check(engine.CopyToUserItemFromName(definition.Name, ref item, makeIndex),
        "failed to construct " + definition.Name);
    return item;
}

static Envirnoment CreateBlankMap()
{
    var map = new Envirnoment
    {
        sMapName = "0",
        sMapDesc = "special-drop-audit",
        m_sMapFileName = "0",
        Flag = new TMapFlag()
    };
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Envirnoment).FullName,
            "Initialize");
    initialize.Invoke(map, new object[] { (short)64, (short)64 });
    return map;
}

static TPlayObject NewPlayer(Envirnoment map, string name, int x, int y) =>
    new()
    {
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_sCharName = name,
        m_sUserID = name + "-user",
        m_nCurrX = unchecked((short)x),
        m_nCurrY = unchecked((short)y),
        m_boGhost = false,
        m_boDeath = false,
        m_boOffLineFlag = true
    };

static SequenceAuthPlayer NewAuthPlayer(Envirnoment map, string name,
    int x, int y, params bool[] authenticationResults) =>
    new(authenticationResults)
    {
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_sCharName = name,
        m_sUserID = name + "-user",
        m_nCurrX = unchecked((short)x),
        m_nCurrY = unchecked((short)y),
        m_boGhost = false,
        m_boDeath = false,
        m_boOffLineFlag = true
    };

static RecordingPlayObject NewRecordingPlayer(Envirnoment map, string name,
    int x, int y) =>
    new()
    {
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_sCharName = name,
        m_sUserID = name + "-user",
        m_nCurrX = unchecked((short)x),
        m_nCurrY = unchecked((short)y),
        m_boGhost = false,
        m_boDeath = false,
        m_boOffLineFlag = true
    };

static int CountGroundItems(Envirnoment map)
{
    var count = 0;
    for (short x = 0; x < map.wWidth; x++)
    for (short y = 0; y < map.wHeight; y++)
    {
        if (map.GetItem(x, y) != null)
            count++;
    }
    return count;
}

static TProcessMessage ToProcessMessage(SendMessage message) => new()
{
    wIdent = message.wIdent,
    wParam = message.wParam,
    nParam1 = message.nParam1,
    nParam2 = message.nParam2,
    nParam3 = message.nParam3,
    BaseObject = message.BaseObject?.ObjectId ?? message.ObjectId,
    dwDeliveryTime = message.dwDeliveryTime,
    boLateDelivery = message.boLateDelivery,
    sMsg = message.Buff ?? string.Empty,
    Payload = message.Payload,
    nBodyLen = message.nBodyLen
};

static byte[] BuildNativeRecord(ushort itemIndex)
{
    var record = new byte[208];
    BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0, 4), 1234);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), itemIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), 900);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), 1000);
    return record;
}

static TUserItem LoadedItem(ushort itemIndex) => new()
{
    MakeIndex = 1234,
    wIndex = itemIndex,
    Dura = 900,
    DuraMax = 1000,
    NativeRecord = BuildNativeRecord(itemIndex)
};

static void AssertThreshold(TUserItem item, int expected, string description)
{
    Check(item != null, description + " item is null");
    Equal(unchecked((byte)expected), item.NativeItemPlus100,
        description + " +0x100");
    Equal(unchecked((byte)(expected >> 8)), item.NativeItemPlus101,
        description + " +0x101");
    Equal(unchecked((byte)(expected >> 16)), item.NativeItemPlus102,
        description + " +0x102");
    Equal(unchecked((byte)(expected >> 24)), item.NativeItemPlus103,
        description + " +0x103");
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

    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static TUserItem Threshold(int value) => new()
{
    NativeItemPlus100 = unchecked((byte)value),
    NativeItemPlus101 = unchecked((byte)(value >> 8)),
    NativeItemPlus102 = unchecked((byte)(value >> 16)),
    NativeItemPlus103 = unchecked((byte)(value >> 24))
};

static void CheckCase(TUserItem item, int draw, bool expected,
    string description)
{
    var calls = 0;
    var actual = NativeSpecialDropItemRollCore.IsSelected(item, bound =>
    {
        calls++;
        Equal(100, bound, description + " Random bound");
        return draw;
    });
    Equal(expected, actual, description);
    Equal(1, calls, description + " draw count");
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
    }
}

sealed class SequenceAuthPlayer : TPlayObject
{
    private readonly Queue<bool> _authenticationResults;

    public SequenceAuthPlayer(IEnumerable<bool> authenticationResults)
    {
        _authenticationResults = new Queue<bool>(authenticationResults);
    }

    public int AuthenticationCalls { get; private set; }

    protected override bool NativeItemDropDestroyAuthenticated()
    {
        AuthenticationCalls++;
        if (_authenticationResults.Count == 0)
            throw new InvalidOperationException("unexpected authentication query");
        return _authenticationResults.Dequeue();
    }
}

sealed class RecordingPlayObject : TPlayObject
{
    public List<(ClientPacket Header, byte[] Body)> BinaryPackets { get; } =
        new();

    internal override void SendSocket(ClientPacket defMsg, byte[] rawBody)
    {
        BinaryPackets.Add((defMsg,
            rawBody?.ToArray() ?? Array.Empty<byte>()));
    }
}

sealed class CountingRandomNumber : RandomNumber
{
    public int BoundedCalls { get; private set; }

    public void Reset()
    {
        BoundedCalls = 0;
    }

    public override int Random(int value)
    {
        BoundedCalls++;
        return 0;
    }
}
