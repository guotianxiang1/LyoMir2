using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

// The sweep harness passes a repository root to every tool uniformly, so argv[0] is a
// hint, not a promise: accept it as a build directory only when the assemblies are
// actually there, otherwise treat it as a root to search under.
var gameDirectory = args.Length > 0
    ? (IsGameSvrBuild(Path.GetFullPath(args[0]))
        ? Path.GetFullPath(args[0])
        : FindGameSvrBuildUnder(Path.GetFullPath(args[0])))
    : FindGameSvrBuild();
if (gameDirectory == null)
{
    Console.Error.WriteLine("INCOMPLETE: no GameSvr build directory was supplied and "
        + "none was found under GameSvr/bin. Usage: HeroLifecycleCheck [GameSvr build]");
    Environment.Exit(2);
}

PrepareRuntimeConfig();

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var dependency = Path.Combine(gameDirectory, $"{name.Name}.dll");
    return File.Exists(dependency)
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency)
        : null;
};

var systemModule = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "SystemModule.dll"));
var gameSvr = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "GameSvr.dll"));
var globalType = systemModule.GetType("SystemModule.Grobal2", throwOnError: true)!;
var shareType = gameSvr.GetType("GameSvr.M2Share", throwOnError: true)!;
var objectManagerType = gameSvr.GetType("GameSvr.ObjectManager", throwOnError: true)!;
var userEngineType = gameSvr.GetType("GameSvr.UserEngine", throwOnError: true)!;
var environmentType = gameSvr.GetType("GameSvr.Envirnoment", throwOnError: true)!;
var playerType = gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!;
var heroType = gameSvr.GetType("GameSvr.HeroObject", throwOnError: true)!;
var baseObjectType = gameSvr.GetType("GameSvr.TBaseObject", throwOnError: true)!;
var heroDataServiceType = gameSvr.GetType("GameSvr.HeroDataService", throwOnError: true)!;
var dbServiceType = gameSvr.GetType("GameSvr.DBService", throwOnError: true)!;
var pasApiBridgeType = gameSvr.GetType("GameSvr.PasEngine.PasApiBridge", throwOnError: true)!;
var delHeroCommandType = gameSvr.GetType("GameSvr.DelHeroCommand", throwOnError: true)!;

Assert((int)globalType.GetField("RC_HEROOBJECT")!.GetRawConstantValue()! == 54,
    "RC_HEROOBJECT is not native value 54");
Assert((int)globalType.GetField("SM_BUILDHERO")!.GetRawConstantValue()! == 773,
    "SM_BUILDHERO is not native value 773");
Assert((int)GetStaticField(userEngineType, "HeroRunInterval").GetRawConstantValue()! == 50,
    "hero run interval is not 50ms");
Assert((int)GetStaticField(userEngineType, "HeroProcessBudget").GetRawConstantValue()! == 25,
    "hero process budget is not 25ms");
Assert((int)GetStaticField(userEngineType, "HeroFreeDelay").GetRawConstantValue()! == 300_000,
    "hero free delay is not 5 minutes");
Assert(playerType.GetMethod("CreateHero", BindingFlags.Instance | BindingFlags.NonPublic) == null,
    "synthetic login hero factory is still present");
Assert(!ContainsNewObject(playerType.GetMethod("UserLogon")!, heroType),
    "UserLogon still constructs a synthetic HeroObject");
var requestHeroLoad = heroDataServiceType.GetMethod("RequestLoad",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var requestHeroCreate = heroDataServiceType.GetMethod("RequestCreate",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var requestHeroDelete = heroDataServiceType.GetMethod("RequestDelete",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var requestHeroRename = heroDataServiceType.GetMethod("RequestRename",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var queueHeroSave = heroDataServiceType.GetMethod("QueueSave",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var processHeroData = heroDataServiceType.GetMethod("Process",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var playerOperate = playerType.GetMethod("Operate", BindingFlags.Instance | BindingFlags.Public |
    BindingFlags.DeclaredOnly)!;
var restoreHeroState = playerType.GetMethod("RestoreNativeHeroState",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var persistHeroState = playerType.GetMethod("PersistNativeHeroState",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var removeHeroMethod = userEngineType.GetMethod("RemoveHero", new[] { playerType })!;
var queueForFree = userEngineType.GetMethod("QueueHeroForFreeLocked",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var processHeroesMethod = userEngineType.GetMethod("ProcessHeroes",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
Assert(dbServiceType.GetMethod("SendNativeFrame") != null &&
       dbServiceType.GetMethod("NextQueryId") != null,
    "DBService does not expose the native hero frame transport");
Assert(heroDataServiceType.GetMethod("FlushPendingSavesAndWait",
           BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null
       && heroDataServiceType.GetMethod("NotifyDisconnected",
           BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null,
    "hero save native FIFO barrier is missing");
Assert(ContainsCall(pasApiBridgeType.GetMethod("CallPlayerFunc")!, requestHeroCreate),
    "PAS CreateHero no longer enters the native 0x162 path");
// HeroRename is a TPsNpc method, not a global or a TPlayer method: the compile-time class
// is built at 0x734672-0x73468C (mov ebx,[esi+0x1C] / FindClass 'TCreature' @0x7352B4 /
// AddClassN 'TPsNpc' @0x7352C8 / mov ebx,eax) and ebx is never re-seated before
//   0x734E89 BA A0 7A 73 00  mov edx,0x737AA0  ; 'function HeroRename(player: TPlayer;
//                                              ;  const oldName, newName: string): Integer;'
//   0x734E8E 8B C3           mov eax,ebx
//   0x734E90 E8 6B C0 DD FF  call 0x510F00     ; RegisterMethod on the TPsNpc class
// The runtime half is the single name binding at 0x739213 (mov ecx,0x73A4AC 'HeroRename').
// So the rename must be reachable from the NPC surface and only from there.
Assert(ContainsCall(pasApiBridgeType.GetMethod("CallNpcFunc")!, requestHeroRename),
    "PAS HeroRename no longer enters the native 0x164 path from the TPsNpc surface");
Assert(!ContainsCall(pasApiBridgeType.GetMethod("CallStandaloneFunction")!, requestHeroRename)
       && !ContainsCall(pasApiBridgeType.GetMethod("CallPlayerFunc")!, requestHeroRename),
    "HeroRename was re-exported outside the native TPsNpc registration");
Assert(ContainsCall(delHeroCommandType.GetMethod("DelHero")!, requestHeroDelete),
    "@DelHero does not enter the native 0x163 path");
var delHeroCommandAttribute = delHeroCommandType.GetCustomAttributes(false)
    .Single(attribute => attribute.GetType().Name == "GameCommandAttribute");
Assert((byte)delHeroCommandAttribute.GetType().GetProperty("nPermissionMin")!
           .GetValue(delHeroCommandAttribute)! == 4,
    "@DelHero permission is not the native value 4");
Assert(ContainsCall(playerOperate, requestHeroLoad),
    "CM_HERO_LOGON no longer enters the native hero DB load path");
var sendHeroLogon = heroType.GetMethod("SendHeroLogon")!;
var sendHeroBornEffect = heroType.GetMethod("SendHeroBornEffect")!;
var rawSendSocket = playerType.GetMethod("SendSocket",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { systemModule.GetType("SystemModule.ClientPacket", throwOnError: true)!, typeof(byte[]) }, null)!;
Assert(!ContainsCall(playerOperate, sendHeroLogon),
    "CM_HERO_LOGON incorrectly resends an existing hero UI snapshot");
Assert(ContainsCall(sendHeroLogon, sendHeroBornEffect),
    "hero logon does not emit the native SM897 born effect");
Assert(GetCallOffset(sendHeroLogon, sendHeroBornEffect) < GetCallOffset(sendHeroLogon, rawSendSocket),
    "hero logon does not emit SM897 before SM899");
Assert(ContainsCall(playerOperate, removeHeroMethod),
    "CM_HERO_LOGOUT no longer removes the server hero object");

// THeroAct.Run 的回收门（sub_689FDC @0x68A048-0x68A057）在尸体满 60 秒或主人 ghost 时
// 调 sub_6CCA1C(主人)，而 sub_6CCA1C 会 MarkDelete 英雄、清 [master+0xBB0]、下发
// SM_HERO_LOGOUT(0x6CCAE7 mov dx,0x396 -> [master_vmt+0x250]) 并排一次 DB 存档
// (0x6CCAF7 call 0x6CC9A8 = 0x194)。只 MakeGhost 会让主人的 m_HeroObject 永远非空，
// CM_HERO_LOGON 的 `m_HeroObject == null` 门再也打不开。
var runMasterGoneReap = heroType.GetMethod("RunNativeMasterGoneReap",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var sendDefMessage = playerType.GetMethod("SendDefMessage")!;
Assert(ContainsCall(runMasterGoneReap, removeHeroMethod),
    "hero corpse/owner-gone reap no longer performs the native sub_6CCA1C recall "
    + "(owner keeps a dangling m_HeroObject and can never re-summon)");
Assert(ContainsCall(runMasterGoneReap, sendDefMessage),
    "hero corpse/owner-gone reap no longer sends SM_HERO_LOGOUT to the owner "
    + "(native 0x6CCAE7 mov dx,0x396 through the +0x250 send slot)");

// CM_HERO_LOGON 的副将槽门：原生 0x6D9337 `cmp word [msg+6],1` 命中后要求
// GetV(group 87, index 3) == 100（0x6D934B call 0x6DF1E4 / 0x6D9350 cmp eax,0x64），
// 否则发「请先召唤一次主将英雄」并中止。
var viceHeroGate = playerType.GetMethod("NativeViceHeroSummonAllowed",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
Assert(ContainsCall(playerOperate, viceHeroGate),
    "CM_HERO_LOGON no longer consults the native vice-hero V(87,3)==100 gate");
Assert(ContainsCall(queueForFree, queueHeroSave),
    "hero retirement releases runtime state before queuing a native save");
Assert(ContainsCall(processHeroesMethod, processHeroData),
    "UserEngine does not consume hero DB completions on its own thread");
Assert(ContainsCall(userEngineType.GetMethod("GetHumData",
            BindingFlags.Instance | BindingFlags.NonPublic)!, restoreHeroState),
    "native hero state is not restored from character ScriptData");
var saveHumanRcd = userEngineType.GetMethod("SaveHumanRcd",
    new[] { playerType })!;
var saveHumanRcdCore = userEngineType.GetMethod("SaveHumanRcdCore",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
Assert(ContainsCall(saveHumanRcd, persistHeroState) ||
       ContainsCall(saveHumanRcd, saveHumanRcdCore) &&
       ContainsCall(saveHumanRcdCore, persistHeroState),
    "native hero state is not written back before character save");

var objectManager = Activator.CreateInstance(objectManagerType)!;
SetStaticField(shareType, "ObjectManager", objectManager);
SetStaticField(shareType, "ProcessMsgCriticalSection", new object());
SetStaticField(shareType, "LogMsgCriticalSection", new object());
SetStaticField(shareType, "g_MonSayMsgList",
    Activator.CreateInstance(GetStaticField(shareType, "g_MonSayMsgList").FieldType)!);
var config = GetStaticField(shareType, "g_Config").GetValue(null)!;
config.GetType().GetField("boMonSayMsg")!.SetValue(config, false);

var userEngine = Activator.CreateInstance(userEngineType)!;
SetStaticField(shareType, "UserEngine", userEngine);
var environment = Activator.CreateInstance(environmentType)!;
environmentType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(environment, new object[] { (short)20, (short)20 });
SetField(environmentType, environment, "sMapName", "HERO_TEST");
SetField(environmentType, environment, "m_sMapFileName", "HERO_TEST.map");

var owner = Activator.CreateInstance(playerType)!;
var nativeStateOffset = (int)playerType.GetField("NativeHeroStateOffset",
    BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
Assert(nativeStateOffset == 0x52, "native ScriptData hero-state offset is not 0x52");
var nativeScript = new byte[0x60];
nativeScript[nativeStateOffset] = 0xA5;
SetField(playerType, owner, "m_NativeScriptData", nativeScript);
restoreHeroState.Invoke(owner, null);
Assert((byte)GetField(playerType, owner, "m_btNativeHeroState") == 0xA5,
    "native hero state was not restored from ScriptData[0x52]");
SetField(playerType, owner, "m_btNativeHeroState", (byte)0x5A);
Assert((bool)persistHeroState.Invoke(owner, null)!,
    "native hero state persistence rejected a valid ScriptData record");
Assert(nativeScript[nativeStateOffset] == 0x5A,
    "native hero state was not written to ScriptData[0x52]");
SetField(baseObjectType, owner, "m_sCharName", "HeroOwner");
SetField(baseObjectType, owner, "m_PEnvir", environment);
SetField(baseObjectType, owner, "m_sMapName", "HERO_TEST");
SetField(baseObjectType, owner, "m_sMapFileName", "HERO_TEST.map");
SetField(baseObjectType, owner, "m_nCurrX", (short)10);
SetField(baseObjectType, owner, "m_nCurrY", (short)10);
SetField(baseObjectType, owner, "m_boAddToMaped", false);
SetField(baseObjectType, owner, "m_boDelFormMaped", true);
SetField(playerType, owner, "m_sUserID", "HeroAccount");
playerType.GetMethod("Initialize")!.Invoke(owner, null);
Assert(CountMapReferences(environment, owner) == 1, "owner was not added to exactly one map cell");
Assert((int)GetProperty(environmentType, environment, "HumCount") == 1,
    "map player count did not increment once");

// 副将槽门的行为面：GetV 未命中 -> -1，V(87,3) 必须**恰好**是 100 才放行
// （0x6D9350 `cmp eax,0x64` / `jne`，不是 >=）。
var setScriptVar = playerType.GetMethod("SetScriptVar")!;
Assert(!(bool)viceHeroGate.Invoke(owner, null)!,
    "vice-hero gate opened while V(87,3) was never written (native GetV miss = -1)");
setScriptVar.Invoke(owner, new object[] { 'V', 87, 3, 99 });
Assert(!(bool)viceHeroGate.Invoke(owner, null)!,
    "vice-hero gate opened at V(87,3)=99 (native compares for equality with 100)");
setScriptVar.Invoke(owner, new object[] { 'V', 87, 3, 101 });
Assert(!(bool)viceHeroGate.Invoke(owner, null)!,
    "vice-hero gate opened at V(87,3)=101 (native compares for equality with 100)");
setScriptVar.Invoke(owner, new object[] { 'V', 87, 3, 100 });
Assert((bool)viceHeroGate.Invoke(owner, null)!,
    "vice-hero gate stayed shut at V(87,3)=100");
setScriptVar.Invoke(owner, new object[] { 'V', 87, 3, 0 });
((IList)GetField(baseObjectType, owner, "m_MsgList")).Clear();

var registerHero = userEngineType.GetMethod("RegisterHero")!;
var removeHero = userEngineType.GetMethod("RemoveHero", new[] { playerType })!;
var processHeroes = userEngineType.GetMethod("ProcessHeroes", BindingFlags.Instance | BindingFlags.NonPublic)!;
var getObject = objectManagerType.GetMethod("Get")!;

var firstHero = CreateHero("SharedHero", 11, 10);
Assert((bool)registerHero.Invoke(userEngine, new[] { owner, firstHero })!, "first hero registration failed");
Assert((int)GetProperty(userEngineType, userEngine, "HeroObjectCount") == 1, "first hero is not active");
Assert((int)GetProperty(userEngineType, userEngine, "HeroFreeObjectCount") == 0, "unexpected free hero");
Assert(ReferenceEquals(GetField(playerType, owner, "m_HeroObject"), firstHero), "owner is not bound to first hero");
Assert((byte)GetField(baseObjectType, firstHero, "m_btRaceServer") == 54, "hero instance race is not 54");
Assert((int)GetField(baseObjectType, firstHero, "m_nRunTime") == 50, "hero instance run time is not 50ms");
Assert(CountMapReferences(environment, firstHero) == 1, "first hero was not added to exactly one map cell");
Assert((int)GetProperty(environmentType, environment, "MonCount") == 1, "map object count did not increment once");

var rejectedOwner = Activator.CreateInstance(playerType)!;
SetField(baseObjectType, rejectedOwner, "m_sCharName", "RejectedOwner");
var rejectedHero = CreateHero("RejectedHero", 11, 9);
var rejectedId = (int)GetField(baseObjectType, rejectedHero, "ObjectId");
Assert(!(bool)registerHero.Invoke(userEngine, new[] { rejectedOwner, rejectedHero })!,
    "hero registration unexpectedly accepted an owner without a map");
Assert(getObject.Invoke(objectManager, new object[] { rejectedId }) == null,
    "failed hero registration leaked its ObjectManager entry");
Assert(CountMapReferences(environment, rejectedHero) == 0,
    "failed hero registration leaked a map-cell reference");

var oldRunTick = Environment.TickCount - 100;
SetField(baseObjectType, firstHero, "m_dwRunTick", oldRunTick);
SetField(baseObjectType, firstHero, "m_dwSearchTick", Environment.TickCount);
processHeroes.Invoke(userEngine, null);
Assert((int)GetField(baseObjectType, firstHero, "m_dwRunTick") != oldRunTick,
    "active hero was not scheduled");

var secondHero = CreateHero("SharedHero", 9, 10);
Assert((bool)registerHero.Invoke(userEngine, new[] { owner, secondHero })!, "replacement hero registration failed");
Assert((bool)GetField(baseObjectType, firstHero, "m_boGhost"), "duplicate old hero was not retired");
Assert((int)GetProperty(userEngineType, userEngine, "HeroObjectCount") == 1, "duplicate left more than one active hero");
Assert((int)GetProperty(userEngineType, userEngine, "HeroFreeObjectCount") == 1, "old hero was not queued");
Assert(ReferenceEquals(GetField(playerType, owner, "m_HeroObject"), secondHero), "owner is not bound to replacement hero");
Assert(CountMapReferences(environment, firstHero) == 0, "old hero remained in a map cell");
Assert(CountMapReferences(environment, secondHero) == 1, "replacement hero was not added exactly once");

Assert((bool)removeHero.Invoke(userEngine, new[] { owner })!, "script-style hero removal failed");
Assert(!(bool)removeHero.Invoke(userEngine, new[] { owner })!, "duplicate hero removal was not idempotent");
Assert(GetField(playerType, owner, "m_HeroObject") == null, "script-style removal retained the owner reference");
Assert((int)GetProperty(userEngineType, userEngine, "HeroObjectCount") == 0,
    "script-style removal left a hero active");
Assert(CountMapReferences(environment, secondHero) == 0,
    "script-style removal left a map-cell reference");

var logoutHero = CreateHero("LogoutHero", 9, 10);
Assert((bool)registerHero.Invoke(userEngine, new[] { owner, logoutHero })!, "logout hero registration failed");

var firstId = (int)GetField(baseObjectType, firstHero, "ObjectId");
SetField(baseObjectType, firstHero, "m_dwGhostTick", Environment.TickCount - 299_000);
// ObjectManager.ClearObject() -- the non-native global id->object ghost sweep that used to run
// here on a 3-minute dwMakeGhostTime gate -- was DELETED (2026-08-03). 战神 has no global ghost
// scan at all: detection lives in the per-type ProcessMon loops (sub_67C150 loop2 @0x67C46F,
// loop3 @0x67C614) and the actual free is one central deferred FIFO drained 5 minutes after
// enqueue (0x67C1BD `cmp eax,0x493E0`). So the property this block guards -- "a hero ghosted for
// less than its dedicated 5-minute queue is NOT reclaimed by a generic sweep" -- is now
// structural. Assert both halves: the sweep is gone from the type, and the hero survives.
Assert(objectManagerType.GetMethod("ClearObject") == null,
    "the non-native global ghost sweep ObjectManager.ClearObject() was reintroduced");
Assert(ReferenceEquals(getObject.Invoke(objectManager, new object[] { firstId }), firstHero),
    "generic object GC removed a hero before the dedicated 5-minute queue");

var staleDisciple = Activator.CreateInstance(playerType)!;
((IList)GetField(playerType, owner, "m_MasterList")).Add(staleDisciple);
SetField(playerType, owner, "m_boMaster", false);
SetField(playerType, owner, "m_MasterHuman", null);
playerType.GetMethod("MakeGhost")!.Invoke(owner, null);
Assert((bool)GetField(baseObjectType, owner, "m_boGhost"),
    "stale master relation skipped the player's base MakeGhost");
Assert(CountMapReferences(environment, owner) == 0,
    "stale master relation left the player in a map cell");
Assert((int)GetProperty(environmentType, environment, "HumCount") == 0,
    "map player count did not return to baseline");
Assert(GetField(playerType, owner, "m_HeroObject") == null, "owner still retains removed hero");
Assert((int)GetProperty(userEngineType, userEngine, "HeroObjectCount") == 0, "owner logout left hero active");
Assert((int)GetProperty(userEngineType, userEngine, "HeroFreeObjectCount") == 3, "owner logout did not queue hero");
Assert(CountMapReferences(environment, logoutHero) == 0, "removed hero remained in a map cell");
Assert((int)GetProperty(environmentType, environment, "MonCount") == 0, "map object count did not return to baseline");

var freeQueue = (IEnumerable)GetField(userEngineType, userEngine, "m_HeroObjectFreeList");
foreach (var freeInfo in freeQueue)
{
    SetField(freeInfo!.GetType(), freeInfo, "FreeTick", Environment.TickCount - 300_001);
}
processHeroes.Invoke(userEngine, null);
Assert((int)GetProperty(userEngineType, userEngine, "HeroFreeObjectCount") == 0, "expired hero queue was not drained");
Assert(getObject.Invoke(objectManager, new object[] { firstId }) == null, "first hero remains in ObjectManager");
var secondId = (int)GetField(baseObjectType, secondHero, "ObjectId");
Assert(getObject.Invoke(objectManager, new object[] { secondId }) == null, "second hero remains in ObjectManager");
var logoutId = (int)GetField(baseObjectType, logoutHero, "ObjectId");
Assert(getObject.Invoke(objectManager, new object[] { logoutId }) == null, "logout hero remains in ObjectManager");

Console.WriteLine("PASS rc=54 run=50ms budget=25ms db=160/161/162/163/164 save=one-way-fifo create=pas-async rename=pas-async delete=command4+index-only state=script+0x52 relogon=ignored sequence=897/899/898 pending=engine-thread login=no-synthetic map=single duplicate=retired reject=clean script-delete=clean stale-relation=clean logout=save+remove free=300000ms");

object CreateHero(string name, short x, short y)
{
    var hero = Activator.CreateInstance(heroType)!;
    SetField(baseObjectType, hero, "m_sCharName", name);
    SetField(baseObjectType, hero, "m_nCurrX", x);
    SetField(baseObjectType, hero, "m_nCurrY", y);
    return hero;
}

int CountMapReferences(object map, object actor)
{
    var cells = (Array)GetField(environmentType, map, "MapCellObjectLists");
    var count = 0;
    foreach (var cell in cells)
    {
        if (cell is not IEnumerable objects) continue;
        foreach (var cellObject in objects)
        {
            if (cellObject != null && ReferenceEquals(GetField(cellObject.GetType(), cellObject, "CellObj"), actor))
            {
                count++;
            }
        }
    }
    return count;
}

static FieldInfo GetStaticField(Type type, string name) =>
    type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new MissingFieldException(type.FullName, name);

static void SetStaticField(Type type, string name, object value) => GetStaticField(type, name).SetValue(null, value);

static object GetField(Type type, object instance, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance);

static void SetField(Type type, object instance, string name, object value) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(instance, value);

static object GetProperty(Type type, object instance, string name) =>
    type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool ContainsNewObject(MethodInfo method, Type targetType)
{
    var oneByte = new Dictionary<byte, OpCode>();
    var twoByte = new Dictionary<byte, OpCode>();
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (field.GetValue(null) is not OpCode opCode) continue;
        var value = unchecked((ushort)opCode.Value);
        if (value <= byte.MaxValue)
            oneByte[(byte)value] = opCode;
        else if ((value & 0xFF00) == 0xFE00)
            twoByte[(byte)value] = opCode;
    }

    var il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
    for (var offset = 0; offset < il.Length;)
    {
        var first = il[offset++];
        var opCode = first == 0xFE ? twoByte[il[offset++]] : oneByte[first];
        if (opCode.OperandType == OperandType.InlineMethod)
        {
            var token = BitConverter.ToInt32(il, offset);
            if (opCode == OpCodes.Newobj && method.Module.ResolveMethod(token)?.DeclaringType == targetType)
                return true;
        }

        offset += GetOperandSize(opCode.OperandType, il, offset);
    }
    return false;
}

static bool ContainsCall(MethodInfo method, MethodInfo target)
    => GetCallOffset(method, target) >= 0;

static int GetCallOffset(MethodInfo method, MethodInfo target)
{
    var oneByte = new Dictionary<byte, OpCode>();
    var twoByte = new Dictionary<byte, OpCode>();
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (field.GetValue(null) is not OpCode opCode) continue;
        var value = unchecked((ushort)opCode.Value);
        if (value <= byte.MaxValue)
            oneByte[(byte)value] = opCode;
        else if ((value & 0xFF00) == 0xFE00)
            twoByte[(byte)value] = opCode;
    }

    var il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
    for (var offset = 0; offset < il.Length;)
    {
        var instructionOffset = offset;
        var first = il[offset++];
        var opCode = first == 0xFE ? twoByte[il[offset++]] : oneByte[first];
        if (opCode.OperandType == OperandType.InlineMethod)
        {
            var token = BitConverter.ToInt32(il, offset);
            if ((opCode == OpCodes.Call || opCode == OpCodes.Callvirt) &&
                method.Module.ResolveMethod(token) is MethodInfo called &&
                called.MetadataToken == target.MetadataToken && called.Module == target.Module)
                return instructionOffset;
        }
        offset += GetOperandSize(opCode.OperandType, il, offset);
    }
    return -1;
}

static int GetOperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
{
    OperandType.InlineNone => 0,
    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
    OperandType.InlineVar => 2,
    OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
        or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
        or OperandType.ShortInlineR => 4,
    OperandType.InlineI8 or OperandType.InlineR => 8,
    OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
    _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}")
};

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"), "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"), "[Command]" + Environment.NewLine);

    var shareDirectory = Path.Combine(Path.GetFullPath(Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

// run_audits.py invokes every audit with no arguments, so a tool that hard-requires
// a GameSvr build directory reported FAIL without evaluating a single assertion.
// Falling back to the checkout's own build output keeps the assertions exactly as
// they were; when no build exists the tool exits 2 (INCOMPLETE) rather than
// pretending to have checked anything.
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

static bool IsGameSvrBuild(string directory)
{
    return File.Exists(Path.Combine(directory, "GameSvr.dll"))
           && File.Exists(Path.Combine(directory, "SystemModule.dll"));
}

static string FindGameSvrBuild()
{
    return FindGameSvrBuildUnder(FindRepositoryRoot());
}

static string FindGameSvrBuildUnder(string repositoryRoot)
{
    if (repositoryRoot == null)
        return null;
    // GameSvr.csproj's Debug OutputPath is ..\..\Build\Mir200 relative to GameSvr\,
    // i.e. a Build\ tree one level ABOVE the checkout. GameSvr\bin therefore never
    // exists in a normal build and probing only there always reported INCOMPLETE.
    var parent = Directory.GetParent(repositoryRoot)?.FullName;
    foreach (var configured in new[]
             {
                 parent == null ? null : Path.Combine(parent, "Build", "Mir200"),
                 Path.Combine(repositoryRoot, "Build", "Mir200")
             })
    {
        if (configured != null
            && File.Exists(Path.Combine(configured, "GameSvr.dll"))
            && File.Exists(Path.Combine(configured, "SystemModule.dll")))
            return configured;
    }
    var binRoot = Path.Combine(repositoryRoot, "GameSvr", "bin");
    if (!Directory.Exists(binRoot))
        return null;
    var debug = $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}";
    foreach (var candidate in Directory
                 .EnumerateFiles(binRoot, "GameSvr.dll", SearchOption.AllDirectories)
                 // run_audits.py builds -c Debug, so prefer that configuration and
                 // then the freshest output within it.
                 .OrderByDescending(path => path.Contains(debug, StringComparison.OrdinalIgnoreCase))
                 .ThenByDescending(File.GetLastWriteTimeUtc))
    {
        var directory = Path.GetDirectoryName(candidate);
        if (directory != null && File.Exists(Path.Combine(directory, "SystemModule.dll")))
            return directory;
    }
    return null;
}
