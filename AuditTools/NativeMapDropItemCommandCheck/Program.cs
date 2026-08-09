using System.Reflection;
using GameSvr;
using GameSvr.CommandSystem;
using SystemModule;

PrepareRuntimeConfig();

var root = Path.Combine(Path.GetTempPath(),
    "lyom2-mapdrop-command-" + Guid.NewGuid().ToString("N"));
try
{
    var share = Path.Combine(root, "Share");
    var config = Path.Combine(share, "Config");
    var envir = Path.Combine(root, "Envir");
    var monItems = Path.Combine(envir, "MonItems");
    var dropControl = Path.Combine(root, "DropControl");
    Directory.CreateDirectory(config);
    Directory.CreateDirectory(monItems);
    Directory.CreateDirectory(dropControl);
    File.WriteAllBytes(Path.Combine(config, "ServerSwitch.Bin"),
        new byte[NativeServerSwitchStore.SwitchByteCount]);

    Assert(NativeServerSwitchStore.TryLoad(share, out var switches,
            out var error), "load switches: " + error);
    M2Share.ServerSwitches = switches;
    M2Share.sRootPath = root;
    M2Share.g_Config = new GameSvrConfig
    {
        sEnvirDir = envir,
        boShowPreFixMsg = false
    };
    M2Share.UserEngine = new UserEngine();
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "ItemA" });
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "ItemB" });
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "ItemC" });
    M2Share.ObjectManager = new ObjectManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.MapManager = new MapManager();
    M2Share.NativeWorldDropControl.Clear();

    var map = new Envirnoment
    {
        sMapName = "test",
        sMapDesc = "alias",
        m_sMapFileName = "map-file"
    };
    AddMap(M2Share.MapManager, map);
    var player = new ProbePlayer { m_sCharName = "mapdrop-audit" };
    var command = new MapDropItemCommand();

    var attribute = typeof(MapDropItemCommand)
        .GetCustomAttribute<GameCommandAttribute>();
    Assert(attribute != null, "MapDropItem attribute");
    Equal("MapDropItem", attribute.Name, "command name");
    Equal((byte)4, attribute.nPermissionMin, "command permission");

    Equal(" 地图爆物控制 : 开",
        Invoke(command, new[] { "open", "ignored" }, player),
        "open message");
    var word = M2Share.ServerSwitches.ReadSwitchWord();
    Assert((word & 0x00800000u) != 0, "open switch word bit23");
    Equal((byte)0x80,
        File.ReadAllBytes(Path.Combine(config, "ServerSwitch.Bin"))[2],
        "open persisted bit");

    var mapDropFile = Path.Combine(monItems, "MapDropItem_test.txt");
    var mapDropLines = new[] { "drop-a  1", " drop-b\t2" };
    File.WriteAllText(mapDropFile,
        "; ignored" + Environment.NewLine +
        "[group,NORUN]" + Environment.NewLine +
        string.Join(Environment.NewLine, mapDropLines) +
        Environment.NewLine, HUtil32.GbkEncoding);
    WriteDropControl(Path.Combine(dropControl, "test.txt"), "MonsterA");

    Equal("地图爆物加载成功 世界掉落加载成功",
        Invoke(command, new[] { "load", "test", "ignored" }, player),
        "load combined success");
    Assert(!map.NativeCanRunWhileOverweight, "load NORUN");
    Sequence(mapDropLines, map.NativeMapDropItems.Snapshot(),
        "raw MapDrop lines");
    Equal(1, map.NativeDropControl.RecordCount,
        "map DropControl record count");

    File.WriteAllText(mapDropFile,
        "[group CANRUN]" + Environment.NewLine,
        HUtil32.GbkEncoding);
    Equal("配置文件不存在 世界掉落加载成功",
        Invoke(command, new[] { "load", "test" }, player),
        "section-only result");
    Assert(map.NativeCanRunWhileOverweight, "section-only CANRUN");
    Sequence(mapDropLines, map.NativeMapDropItems.Snapshot(),
        "section-only preserves raw lines");

    File.WriteAllText(mapDropFile, "replacement" + Environment.NewLine,
        HUtil32.GbkEncoding);
    Equal("地图爆物加载成功 世界掉落加载成功",
        Invoke(command, new[] { "load", "test" }, player),
        "ordinary reload result");
    Sequence(new[] { "replacement" },
        map.NativeMapDropItems.Snapshot(), "ordinary reload clears once");

    File.Delete(mapDropFile);
    map.NativeCanRunWhileOverweight = false;
    Equal("配置文件不存在 世界掉落加载成功",
        Invoke(command, new[] { "load", "test" }, player),
        "missing MapDrop result");
    Assert(!map.NativeCanRunWhileOverweight,
        "missing MapDrop preserves CANRUN state");
    Sequence(new[] { "replacement" },
        map.NativeMapDropItems.Snapshot(), "missing preserves raw lines");

    Equal(" 地图爆物控制 : 关",
        Invoke(command, new[] { "close", "ignored" }, player),
        "close message");
    word = M2Share.ServerSwitches.ReadSwitchWord();
    Assert((word & 0x00800000u) == 0, "close switch word bit23");
    Equal((byte)0,
        File.ReadAllBytes(Path.Combine(config, "ServerSwitch.Bin"))[2],
        "close persisted bit");

    File.WriteAllText(mapDropFile,
        "[CANRUN]" + Environment.NewLine + "must-not-load" +
        Environment.NewLine, HUtil32.GbkEncoding);
    Equal("配置文件不存在 世界掉落加载成功",
        Invoke(command, new[] { "load", "test" }, player),
        "bit23 gates only MapDrop");
    Assert(!map.NativeCanRunWhileOverweight,
        "bit23-off preserves run state");
    Sequence(new[] { "replacement" },
        map.NativeMapDropItems.Snapshot(), "bit23-off preserves raw lines");
    Equal(1, map.NativeDropControl.RecordCount,
        "bit23-off still reloads DropControl");

    Equal("[missing]地图不存在",
        Invoke(command, new[] { "load", "missing" }, player),
        "missing map message");
    Equal("[alias]地图不存在",
        Invoke(command, new[] { "load", "alias" }, player),
        "lookup uses logical map name only");

    var firstRoom = new Envirnoment { sMapName = "dyn-0" };
    var secondRoom = new Envirnoment { sMapName = "dyn-1" };
    M2Share.DynamicRoomManager = new NativeDynamicRoomManager();
    Assert(M2Share.DynamicRoomManager.RegisterIdleRoom("Dyn", 7, firstRoom),
        "register first dynamic room");
    Assert(M2Share.DynamicRoomManager.RegisterIdleRoom("Dyn", 3, secondRoom),
        "register second dynamic room");
    WriteDropControl(Path.Combine(dropControl, "Dyn.txt"), "DynMonster");
    Equal("Room-1 加载新掉落配置成功" +
          "Room-1 加载新掉落配置成功",
        Invoke(command, new[] { "loaddyn", "Dyn", "ignored" }, player),
        "loaddyn registration order");
    Equal(1, firstRoom.NativeDropControl.RecordCount,
        "loaddyn first room state");
    Equal(1, secondRoom.NativeDropControl.RecordCount,
        "loaddyn second room state");

    Equal(string.Empty,
        Invoke(command, new[] { "loaddyn", "dyn" }, player),
        "loaddyn lookup is case-sensitive");
    File.Delete(Path.Combine(dropControl, "Dyn.txt"));
    Equal("Room-1 加载新掉落配置失败" +
          "Room-1 加载新掉落配置失败",
        Invoke(command, new[] { "loaddyn", "Dyn" }, player),
        "loaddyn missing file result");
    Equal(1, firstRoom.NativeDropControl.RecordCount,
        "loaddyn missing preserves first state");
    Equal(1, secondRoom.NativeDropControl.RecordCount,
        "loaddyn missing preserves second state");

    WriteDropControl(Path.Combine(dropControl, "WorldDrop.txt"),
        "WorldMonster");
    Equal(" 新世界掉落加载成功",
        Invoke(command, new[] { "worddrop", "ignored" }, player),
        "worddrop success");
    Equal(1, M2Share.NativeWorldDropControl.RecordCount,
        "worddrop world state");
    File.Delete(Path.Combine(dropControl, "WorldDrop.txt"));
    Equal(" 新世界掉落加载失败",
        Invoke(command, new[] { "worddrop" }, player),
        "worddrop missing result");
    Equal(1, M2Share.NativeWorldDropControl.RecordCount,
        "worddrop missing preserves state");

    Silent(command, Array.Empty<string>(), player, "empty command");
    Silent(command, new[] { "load" }, player, "load missing map arg");
    Silent(command, new[] { "loaddyn" }, player,
        "loaddyn missing room arg");
    Silent(command, new[] { "Open" }, player, "case-sensitive open");
    Silent(command, new[] { "LOAD", "test" }, player,
        "case-sensitive load");
    Silent(command, new[] { "unknown" }, player, "unknown operation");

    _ = Invoke(command, new[] { "open" }, player);
    map.NativeMapDropItems.BeginReload("itema");
    map.NativeMapDropItems.Append("ItemB");
    player.m_PEnvir = map;

    var pickedItem = NewItem(1, 101);
    var preExistingConfiguredItem = NewItem(1, 102);
    var otherConfiguredItem = NewItem(2, 103);
    var unrelatedItem = NewItem(3, 109);
    Assert(player.AddItemToBag(pickedItem), "ordinary add ItemA");
    Assert(player.AddItemToBag(preExistingConfiguredItem),
        "ordinary add second ItemA");
    Assert(player.AddItemToBag(otherConfiguredItem),
        "ordinary add configured ItemB");
    Assert(player.AddItemToBag(unrelatedItem), "ordinary add ItemC");
    Equal((ushort)0, player.NativeMapDropTrackedCount(map),
        "ordinary AddItemToBag does not establish pickup provenance");
    player.TrackNativeMapDropItem(pickedItem);
    Equal((ushort)1, player.NativeMapDropTrackedCount(map),
        "configured pickup tracks environment identity");

    var dropped = new List<TUserItem>();
    lock (M2Share.ProcessMsgCriticalSection)
    {
        var beforeDeleteMessageCount = player.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Equal(3, player.ReleaseNativeMapDropItems(map, true, item =>
        {
            dropped.Add(item);
            return true;
        }), "tracked pickup releases all configured bag instances");
        Equal(3, dropped.Count,
            "one tracked pickup releases every configured item name");
        Equal(1, player.m_ItemList.Count,
            "only unrelated bag item remains");
        Assert(ReferenceEquals(unrelatedItem, player.m_ItemList[0]),
            "unrelated item identity remains");
        var deleteMessages = player.m_MsgList
            .Where(message => message.wIdent == Grobal2.RM_SENDDELITEMLIST)
            .ToArray();
        Equal(beforeDeleteMessageCount + 2, deleteMessages.Length,
            "one delete batch per configured item name");
        Equal(2,
            ((IList<TDeleteItem>)deleteMessages[^2].Payload).Count,
            "ItemA delete batch payload count");
        Equal(2, deleteMessages[^2].nParam1,
            "ItemA delete batch protocol count");
        Equal(1,
            ((IList<TDeleteItem>)deleteMessages[^1].Payload).Count,
            "ItemB delete batch payload count");
        Equal(1, deleteMessages[^1].nParam1,
            "ItemB delete batch protocol count");
    }
    Equal((ushort)0, player.NativeMapDropTrackedCount(map),
        "remove mode deletes tracker entry");
    Equal(0, player.ReleaseNativeMapDropItems(map, true, _ => true),
        "repeated release is silent");

    var retainedAfterFailure = NewItem(1, 104);
    Assert(player.AddItemToBag(retainedAfterFailure),
        "add failed-drop item");
    player.TrackNativeMapDropItem(retainedAfterFailure);
    Equal(0, player.ReleaseNativeMapDropItems(map, false, _ => false),
        "failed ground placement keeps item");
    Assert(player.m_ItemList.Contains(retainedAfterFailure),
        "failed ground placement preserves bag item");
    Equal((ushort)0, player.NativeMapDropTrackedCount(map),
        "same-environment mode clears count");
    player.TrackNativeMapDropItem(retainedAfterFailure);
    Equal((ushort)1, player.NativeMapDropTrackedCount(map),
        "retained zero entry can be incremented again");
    Equal(1, player.ReleaseNativeMapDropItems(map, true, _ => true),
        "retracked item releases");

    var otherEnvironment = new Envirnoment { sMapName = "test" };
    otherEnvironment.NativeMapDropItems.BeginReload("ItemA");
    var identityItem = NewItem(1, 105);
    Assert(player.AddItemToBag(identityItem), "add identity item");
    player.TrackNativeMapDropItem(identityItem);
    Equal(0, player.ReleaseNativeMapDropItems(otherEnvironment, true,
        _ => true), "different physical environment does not consume tracker");
    Equal((ushort)1, player.NativeMapDropTrackedCount(map),
        "source environment tracker preserved");
    Equal(1, player.ReleaseNativeMapDropItems(map, true, _ => true),
        "source environment identity consumes tracker");

    var itemA = M2Share.UserEngine.GetStdItem(1);
    itemA.NativeReserved02 = 0x10;
    var protectedItem = NewItem(1, 106);
    Assert(player.AddItemToBag(protectedItem), "add protected item");
    player.TrackNativeMapDropItem(protectedItem);
    var protectedDropCalls = 0;
    Equal(0, player.ReleaseNativeMapDropItems(map, true, _ =>
    {
        protectedDropCalls++;
        return true;
    }), "AllowFlag 0x10 blocks ordinary forced drop");
    Equal(0, protectedDropCalls, "protected item bypasses DropItemDown");
    Assert(player.m_ItemList.Contains(protectedItem),
        "protected item remains in bag");

    var eventDropAllowedItem = NewItem(1, 107);
    eventDropAllowedItem.NativeMapDropAllowed = true;
    Assert(player.AddItemToBag(eventDropAllowedItem),
        "add event-drop-allowed item");
    player.TrackNativeMapDropItem(eventDropAllowedItem);
    Equal(1, player.ReleaseNativeMapDropItems(map, true, _ => true),
        "type-2000 event flag bypasses AllowFlag protection");
    Assert(!player.m_ItemList.Contains(eventDropAllowedItem),
        "event-drop-allowed item removed after successful drop");
    player.m_ItemList.Remove(protectedItem);
    itemA.NativeReserved02 = 0;

    var staleAfterClose = NewItem(1, 110);
    Assert(player.AddItemToBag(staleAfterClose),
        "add close-generation item");
    player.TrackNativeMapDropItem(staleAfterClose);
    Equal((ushort)1, player.NativeMapDropTrackedCount(map),
        "close-generation pickup tracks");
    _ = Invoke(command, new[] { "close" }, player);
    Equal((ushort)0, player.NativeMapDropTrackedCount(map),
        "bit23 close invalidates old tracker");
    _ = Invoke(command, new[] { "open" }, player);
    Equal(0, player.ReleaseNativeMapDropItems(map, true, _ => true),
        "reopen does not consume pre-close provenance");
    Assert(player.m_ItemList.Contains(staleAfterClose),
        "reopen preserves pre-close item");
    player.m_ItemList.Remove(staleAfterClose);

    var staleAfterRawClear = NewItem(1, 111);
    Assert(player.AddItemToBag(staleAfterRawClear),
        "add raw-generation item");
    player.TrackNativeMapDropItem(staleAfterRawClear);
    Equal((ushort)1, player.NativeMapDropTrackedCount(map),
        "raw-generation pickup tracks");
    map.NativeMapDropItems.Clear();
    Equal((ushort)0, player.NativeMapDropTrackedCount(map),
        "empty raw configuration invalidates old tracker");
    map.NativeMapDropItems.BeginReload("ItemA");
    Equal(0, player.ReleaseNativeMapDropItems(map, true, _ => true),
        "raw reload does not consume pre-empty provenance");
    Assert(player.m_ItemList.Contains(staleAfterRawClear),
        "raw reload preserves pre-empty item");
    player.m_ItemList.Remove(staleAfterRawClear);

    var moveSource = NewMap("mapdrop-move-source", true);
    moveSource.NativeMapDropItems.BeginReload("ItemA");
    var blockedMoveTarget = NewMap("mapdrop-move-blocked", false);
    var committedMoveTarget = NewMap("mapdrop-move-target", true);
    AddMap(M2Share.MapManager, moveSource);
    AddMap(M2Share.MapManager, blockedMoveTarget);
    AddMap(M2Share.MapManager, committedMoveTarget);
    var movingPlayer = new TPlayObject
    {
        m_PEnvir = moveSource,
        m_sMapName = moveSource.sMapName,
        m_sMapFileName = moveSource.m_sMapFileName,
        m_nCurrX = 2,
        m_nCurrY = 2,
        m_btRaceServer = Grobal2.RC_PLAYOBJECT
    };
    Place(moveSource, movingPlayer);
    var rollbackItem = NewItem(1, 112);
    Assert(movingPlayer.AddItemToBag(rollbackItem),
        "add movement rollback item");
    movingPlayer.TrackNativeMapDropItem(rollbackItem);
    Assert(!movingPlayer.TrySpaceMoveToEnvironment(blockedMoveTarget,
            3, 3, 0, coordinatesAlreadyResolved: true),
        "blocked SpaceMove unexpectedly committed");
    Assert(ReferenceEquals(moveSource, movingPlayer.m_PEnvir),
        "blocked SpaceMove did not restore source environment");
    Equal((ushort)1, movingPlayer.NativeMapDropTrackedCount(moveSource),
        "blocked SpaceMove consumed source tracker");
    Assert(movingPlayer.m_ItemList.Contains(rollbackItem),
        "blocked SpaceMove consumed bag item");

    var savedVisibleHumans = movingPlayer.m_VisibleHumanList;
    movingPlayer.m_VisibleHumanList = null;
    Assert(!movingPlayer.TrySpaceMoveToEnvironment(committedMoveTarget,
            3, 3, 0, coordinatesAlreadyResolved: true),
        "post-attach exception unexpectedly committed SpaceMove");
    movingPlayer.m_VisibleHumanList = savedVisibleHumans;
    Assert(ReferenceEquals(moveSource, movingPlayer.m_PEnvir),
        "post-attach exception did not restore source environment");
    Equal((ushort)1, movingPlayer.NativeMapDropTrackedCount(moveSource),
        "post-attach exception consumed source tracker");
    Assert(movingPlayer.m_ItemList.Contains(rollbackItem),
        "post-attach exception consumed bag item");

    var enterAnotherMap = typeof(TBaseObject).GetMethod("EnterAnotherMap",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TBaseObject.EnterAnotherMap");
    Assert(!(bool)enterAnotherMap.Invoke(movingPlayer,
            new object[] { blockedMoveTarget, 3, 3 })!,
        "blocked gate move unexpectedly committed");
    Assert(ReferenceEquals(moveSource, movingPlayer.m_PEnvir),
        "blocked gate move did not restore source environment");
    Equal((ushort)1, movingPlayer.NativeMapDropTrackedCount(moveSource),
        "blocked gate move consumed source tracker");
    Assert(movingPlayer.m_ItemList.Contains(rollbackItem),
        "blocked gate move consumed bag item");

    Assert(movingPlayer.TrySpaceMoveToEnvironment(moveSource, 3, 3, 0,
            coordinatesAlreadyResolved: true),
        "committed same-map SpaceMove failed");
    Equal((ushort)0, movingPlayer.NativeMapDropTrackedCount(moveSource),
        "committed same-map SpaceMove did not clear tracker count");
    movingPlayer.m_ItemList.Remove(rollbackItem);

    var crossMapItem = NewItem(1, 113);
    Assert(movingPlayer.AddItemToBag(crossMapItem),
        "add committed cross-map item");
    movingPlayer.TrackNativeMapDropItem(crossMapItem);
    Assert(movingPlayer.TrySpaceMoveToEnvironment(committedMoveTarget,
            3, 3, 0, coordinatesAlreadyResolved: true),
        "committed cross-map SpaceMove failed");
    Equal((ushort)0, movingPlayer.NativeMapDropTrackedCount(moveSource),
        "committed cross-map SpaceMove did not remove source tracker");
    movingPlayer.m_ItemList.Remove(crossMapItem);

    _ = Invoke(command, new[] { "close" }, player);
    var disabledItem = NewItem(1, 108);
    Assert(player.AddItemToBag(disabledItem), "add disabled item");
    player.TrackNativeMapDropItem(disabledItem);
    Equal((ushort)0, player.NativeMapDropTrackedCount(map),
        "bit23-off does not track pickup");
    player.m_ItemList.Remove(disabledItem);

    Console.WriteLine(
        "NativeMapDropItemCommandCheck PASS id306 exact order/messages " +
        "bit23 MapDrop raw-lines + pickup/leave consumer + " +
        "map/dynamic/world DropControl");
}
finally
{
    Directory.Delete(root, true);
}

static string Invoke(MapDropItemCommand command, string[] args,
    TPlayObject player)
{
    var before = player.m_MsgList.Count;
    command.MapDropItem(args, player);
    Equal(before + 1, player.m_MsgList.Count,
        "expected one SysMsg for " + string.Join(' ', args));
    return player.m_MsgList[^1].Buff ?? string.Empty;
}

static void Silent(MapDropItemCommand command, string[] args,
    TPlayObject player, string label)
{
    var before = player.m_MsgList.Count;
    command.MapDropItem(args, player);
    Equal(before, player.m_MsgList.Count, label);
}

static void WriteDropControl(string fileName, string monsterName)
{
    File.WriteAllText(fileName,
        "type=1" + Environment.NewLine +
        "1 10 ItemA " + monsterName + Environment.NewLine,
        HUtil32.GbkEncoding);
}

static TUserItem NewItem(ushort index, int makeIndex)
{
    return new TUserItem
    {
        wIndex = index,
        MakeIndex = makeIndex,
        Dura = 1,
        DuraMax = 1
    };
}

static Envirnoment NewMap(string mapName, bool walkable)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        sMapDesc = mapName,
        m_sMapFileName = mapName + ".map"
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)8, (short)8 });
    if (!walkable)
    {
        for (var x = 0; x < environment.wWidth; x++)
        for (var y = 0; y < environment.wHeight; y++)
            environment.SetMapXYFlag(x, y, false);
    }
    return environment;
}

static void Place(Envirnoment environment, TBaseObject actor)
{
    Assert(ReferenceEquals(actor, environment.AddToMap(actor.m_nCurrX,
            actor.m_nCurrY, CellType.OS_MOVINGOBJECT, actor)),
        "place movement player");
}

static void AddMap(MapManager manager, Envirnoment map)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MapManager.m_MapList");
    var maps = (Dictionary<string, Envirnoment>)field.GetValue(manager);
    maps.Add(map.sMapName, map);
}

static void Sequence(IReadOnlyList<string> expected,
    IReadOnlyList<string> actual, string label)
{
    Equal(expected.Count, actual.Count, label + " count");
    for (var index = 0; index < expected.Count; index++)
        Equal(expected[index], actual[index], label + "[" + index + "]");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
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

sealed class ProbePlayer : TPlayObject
{
    internal override void SendSocket(ClientPacket defMsg, string message)
    {
    }
}
