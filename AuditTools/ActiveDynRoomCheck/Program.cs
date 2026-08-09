using GameSvr;
using GameSvr.PasEngine;
using System.Reflection;
using SystemModule;

PrepareRuntimeConfig();

var tempDirectory = Path.Combine(Path.GetTempPath(), "active-dynroom-check-"
    + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);

try
{
    var activityFile = Path.Combine(tempDirectory, "PlayerActivePoint.xml");
    File.WriteAllText(activityFile, """
        <?xml version="1.0" encoding="UTF-8"?>
        <Describle>
          <Jobs>
            <Job Id="0">
              <Lucks>
                <Luck LuckValue="0" Value="1"/>
                <Luck LuckValue="9" Value="30"/>
              </Lucks>
              <Magics>
                <Magic Id="43" Value="10"/>
                <Magic Id="44" Value="5"/>
              </Magics>
              <Properties>
                <prop Min="0" value="2"/>
                <prop Min="46" value="10"/>
              </Properties>
            </Job>
          </Jobs>
        </Describle>
        """);

    Assert(NativeActivityPointManager.TryLoad(activityFile, out var activity, out var error),
        $"valid activity configuration was rejected: {error}");
    Assert(activity.Calculate(0, 9, 46, id => id == 43) == 50,
        "activity calculation did not combine last luck/property rule and learned magic");
    Assert(activity.Calculate(0, 8, 45, _ => false) == 3,
        "activity calculation did not preserve native reverse threshold traversal");
    Assert(activity.Calculate(7, 99, 99, _ => true) == 0,
        "unknown job must calculate zero temporary activity points");

    var invalidFile = Path.Combine(tempDirectory, "duplicate.xml");
    File.WriteAllText(invalidFile,
        "<Describle><Jobs><Job Id=\"0\"/><Job Id=\"0\"/></Jobs></Describle>");
    Assert(!NativeActivityPointManager.TryLoad(invalidFile, out _, out _),
        "duplicate jobs must fail closed");

    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    var bridge = new PasApiBridge();
    var player = new TPlayObject();
    var staticMap = new Envirnoment { sMapName = "Trial_99" };
    player.m_PEnvir = staticMap;
    bridge.CurrentPlayer = player;

    Assert(bridge.GetPlayerProperty("DynRoomIdx", out var value) && value.AsInt() == -1,
        "static map name suffix was incorrectly treated as dynamic metadata");
    Assert(bridge.GetPlayerProperty("DynRoomName", out value) && value.AsString() == string.Empty,
        "static map exposed a dynamic room name");

    var rooms = new NativeDynamicRoomManager();
    var firstRoom = new Envirnoment { sMapName = "unrelated-map-a" };
    var secondRoom = new Envirnoment { sMapName = "unrelated-map-b" };
    Assert(rooms.RegisterIdleRoom("Trial", 4, firstRoom), "first room registration failed");
    Assert(rooms.RegisterIdleRoom("Trial", 9, secondRoom), "second room registration failed");
    Assert(!rooms.RegisterIdleRoom("Trial", 9, new Envirnoment()),
        "duplicate physical instance ID was accepted");
    Assert(!rooms.RegisterIdleRoom("Other", 1, firstRoom),
        "one environment was registered in two native room pools");

    player.m_PEnvir = firstRoom;
    Assert(bridge.GetPlayerProperty("DynRoomIdx", out value) && value.AsInt() == -1,
        "idle dynamic room exposed an activation lease");
    Assert(firstRoom.DynamicRoomPhysicalInstanceId == 4,
        "registered physical instance ID was not retained");
    Assert(bridge.GetPlayerProperty("DynRoomName", out value) && value.AsString() == "Trial",
        "explicit dynamic room name was not exposed");

    M2Share.DynamicRoomManager = rooms;
    bridge.CurrentNpc = new NormNpc();
    var createRoomMonsterArgs = new List<PasValue>
    {
        PasValue.FromString("Trial"), PasValue.FromString("稻草人"),
        PasValue.FromInt(10), PasValue.FromInt(10), PasValue.FromInt(1),
        PasValue.FromInt(1), PasValue.FromInt(1), PasValue.FromInt(0),
        PasValue.FromInt(0), PasValue.FromInt(0)
    };
    Assert(!bridge.CallNpcMethod("CreateDynRoomMon", createRoomMonsterArgs, out _),
        "dynamic-room monster procedure reported success without the native room lifecycle");
    Assert(!bridge.CallNpcFunc("CreateDynRoomMon", createRoomMonsterArgs, out _),
        "dynamic-room monster function shadow reported success without creating a monster");
    Assert(!bridge.CallNpcFunc("GetAIdleDynRoomIndex",
        new List<PasValue> { PasValue.FromString("Missing") }, out value)
        && value.Type == PasValueType.Nil,
        "missing dynamic room definition should remain fail-closed");
    Assert(!bridge.CallNpcFunc("GetAIdleDynRoomIndex",
        new List<PasValue> { PasValue.FromString("Trial") }, out value)
        && value.Type == PasValueType.Nil,
        "idle dynamic room lookup reserved state through PAS");
    Assert(!bridge.CallNpcFunc("GetAIdleDynRoomIndexEx",
        new List<PasValue> { PasValue.FromString("Trial"), PasValue.FromObject(player) }, out value)
        && value.Type == PasValueType.Nil,
        "Ex idle dynamic room lookup reserved state through PAS");
    Assert(M2Share.DynamicRoomManager.TryReserveIdleRoom("Trial", null, out var firstIndex)
           && firstIndex == 1, "fail-closed PAS lookup mutated the first idle room");
    Assert(M2Share.DynamicRoomManager.TryReserveIdleRoom("Trial", player, out var secondIndex)
           && secondIndex == 2, "fail-closed PAS lookup mutated the second idle room");
    Assert(!bridge.CallPlayerFunc("GetDynRoomHumCnt",
        new List<PasValue> { PasValue.FromString("Trial"), PasValue.FromInt(firstIndex) }, out value)
        && value.Type == PasValueType.Nil,
        "dynamic room human-count lookup exposed dormant manager state");
    Assert(!bridge.CallPlayerFunc("GetDynRoomHumNum",
        new List<PasValue> { PasValue.FromString("Trial"), PasValue.FromInt(secondIndex) }, out value)
        && value.Type == PasValueType.Nil,
        "dynamic room human-num lookup exposed dormant manager state");
    Assert(!bridge.CallPlayerFunc("PsIsDynRoomValid",
        new List<PasValue> { PasValue.FromString("Trial"), PasValue.FromInt(firstIndex) }, out value)
        && value.Type == PasValueType.Nil,
        "dynamic room validity lookup exposed dormant manager state");

    long lifecycleTick = 1_000;
    var lifecycleRooms = new NativeDynamicRoomManager(() => lifecycleTick);
    var recyclableRoom = new Envirnoment();
    InitializeEnvironment(recyclableRoom);
    var prepareCount = 0;
    Assert(lifecycleRooms.RegisterIdleRoom("Lifecycle", 1, recyclableRoom, 2, _ =>
    {
        prepareCount++;
        return true;
    }), "lifecycle room registration failed");
    Assert(lifecycleRooms.TryReserveIdleRoom("Lifecycle", null, out var lifecycleIndex)
           && lifecycleIndex == 1, "lifecycle room was not reserved");
    var firstLifecycleIndex = lifecycleIndex;

    var occupant = new TPlayObject
    {
        m_PEnvir = recyclableRoom,
        m_nCurrX = 2,
        m_nCurrY = 2
    };
    Assert(ReferenceEquals(occupant, recyclableRoom.AddToMap(2, 2,
        CellType.OS_MOVINGOBJECT, occupant)),
        "lifecycle occupant was not added to the room");
    lifecycleTick += 120_001;
    Assert(recyclableRoom.DeleteFromMap(2, 2, CellType.OS_MOVINGOBJECT,
        occupant) == 1, "lifecycle occupant was not removed from the room");
    Assert(prepareCount == 1, "last player exit did not enter native closing state");
    Assert(!lifecycleRooms.TryReserveIdleRoom("Lifecycle", null, out _),
        "closing room was reused before the native cooldown elapsed");
    lifecycleTick += 600_001;
    lifecycleRooms.Run();
    Assert(lifecycleRooms.TryReserveIdleRoom("Lifecycle", null, out lifecycleIndex)
           && lifecycleIndex != firstLifecycleIndex,
        "prepared room did not receive a fresh lease after ten minutes");

    var quarantinedRooms = new NativeDynamicRoomManager(() => lifecycleTick);
    var quarantinedRoom = new Envirnoment();
    Assert(quarantinedRooms.RegisterIdleRoom("Quarantine", 2, quarantinedRoom),
        "quarantine room registration failed");
    Assert(quarantinedRooms.TryReserveIdleRoom("Quarantine", null, out _),
        "quarantine room was not initially reserved");
    lifecycleTick += 120_001;
    quarantinedRooms.Run();
    lifecycleTick += 600_001;
    quarantinedRooms.Run();
    Assert(quarantinedRooms.TryReserveIdleRoom("Quarantine", null, out var noOpIndex)
           && noOpIndex == 2,
        "room with no cleanup work did not return to idle after ten minutes");

    player.m_PEnvir = staticMap;
    player.m_nCurrX = 23;
    player.m_nCurrY = 31;
    var flyToDynRoomArgs = new List<PasValue>
    {
        PasValue.FromString("Trial"), PasValue.FromInt(10), PasValue.FromInt(10)
    };
    var flyToDynRoomWithIndexArgs = new List<PasValue>
    {
        PasValue.FromString("Trial"), PasValue.FromInt(4),
        PasValue.FromInt(10), PasValue.FromInt(10)
    };
    var groupFlyToDynRoomArgs = new List<PasValue>
    {
        PasValue.FromString("Trial"), PasValue.FromInt(4)
    };
    Assert(!bridge.CallPlayerMethod("FlyToDynRoom", flyToDynRoomArgs),
        "FlyToDynRoom method shadow reported success without a native environment move");
    Assert(!bridge.CallPlayerFunc("FlyToDynRoom", flyToDynRoomArgs, out _),
        "FlyToDynRoom function reported success without the native room lifecycle");
    Assert(!bridge.CallPlayerMethod("FlyToDynEnvirWithIdx", flyToDynRoomWithIndexArgs),
        "indexed dynamic movement used a fabricated static map alias");
    Assert(!bridge.CallPlayerFunc("FlyToDynEnvirWithIdx", flyToDynRoomWithIndexArgs, out _),
        "indexed dynamic function reported success without an active native instance");
    Assert(!bridge.CallPlayerMethod("GroupFlyToDynRoom", groupFlyToDynRoomArgs),
        "group dynamic procedure reported success without native group filtering");
    Assert(!bridge.CallPlayerFunc("GroupFlyToDynRoom", groupFlyToDynRoomArgs, out _),
        "group dynamic function shadow bypassed the native procedure ABI");
    Assert(ReferenceEquals(player.m_PEnvir, staticMap)
           && player.m_nCurrX == 23 && player.m_nCurrY == 31,
        "fail-closed dynamic movement changed the player's environment or coordinates");
    Assert(!bridge.CallNpcMethod("EnterNewGuan",
        new List<PasValue> { PasValue.FromObject(player) }, out _),
        "NewSky entry succeeded without native Magic Tower state");

    M2Share.ActivityPointManager = activity;
    player.m_btJob = 0;
    player.m_nLuck = 9;
    player.m_WAbil.DC = HUtil32.MakeLong(1, 46);
    Assert(bridge.CallPlayerFunc("GetTmpActivePoint", new List<PasValue>(), out value)
        && value.AsInt() == 40, "player API did not use native activity rules");
    M2Share.ActivityPointManager = null;
    Assert(bridge.CallPlayerFunc("GetTmpActivePoint", new List<PasValue>(), out value)
           && value.AsInt() == 0,
        "missing activity configuration did not return native zero");

    Console.WriteLine("ActiveDynRoomCheck PASS");
}
finally
{
    Directory.Delete(tempDirectory, true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void InitializeEnvironment(Envirnoment environment)
{
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)8, (short)8 });
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
