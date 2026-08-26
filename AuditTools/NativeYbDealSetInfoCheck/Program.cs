using GameSvr;
using GameSvr.Services;

PrepareRuntimeConfig();

var loadedStore = new FakeStore(
    new NativeYbDealSetInfoRow("ACCOUNT", "HeroA", 321));
var service = new NativeYbDealSetInfoService(loadedStore);
Assert(service.TryInitialize(out var initializeError), initializeError);
Equal(1, service.Count, "loaded row count");

var loadedState = new NativeYbDealSetInfoState();
service.Attach(loadedState, "ignored", "hEROa");
Assert(loadedState.HasRecord, "ASCII-folded character lookup missed");
Assert(!loadedState.IsDirty, "loaded row became dirty on attach");
Equal((ushort)321, service.GetLimitLevel(loadedState), "loaded LimitLevel");
Equal("account", loadedState.CurrentRecord.Ptid,
    "loaded PTID must use native ASCII lowercase normalization");
Equal("heroa", loadedState.CurrentRecord.CharacterName,
    "loaded CharName must use native ASCII lowercase normalization");

var newState = new NativeYbDealSetInfoState();
service.Attach(newState, "NewAccount", "新角色");
Assert(newState.HasRecord, "missing row was not created");
Assert(newState.IsDirty, "new row must start dirty");
Equal((ushort)0, service.GetLimitLevel(newState), "new row LimitLevel");
Assert(service.TrySetLimitLevel(newState, 0), "zero must be accepted");
Assert(service.TrySetLimitLevel(newState, 999), "999 must be accepted");
Assert(!service.TrySetLimitLevel(newState, 1000), "1000 must be rejected");
Assert(!service.TrySetLimitLevel(newState, ushort.MaxValue),
    "unsigned WORD 65535 must be rejected");
Equal((ushort)999, service.GetLimitLevel(newState),
    "rejected values changed LimitLevel");

var noRecord = new NativeYbDealSetInfoState();
Assert(!service.TrySetLimitLevel(noRecord, 1), "null record setter succeeded");
Equal((ushort)0, service.GetLimitLevel(noRecord), "null record getter");

var success = TBaseObject.BuildSm3015(0);
AssertPacket(success, 3015, 0, "SM3015 success");
var failure = TBaseObject.BuildSm3015(-1);
AssertPacket(failure, 3015, -1, "SM3015 failure");
var query = TBaseObject.BuildSm4446(999);
AssertPacket(query, 4446, 999, "SM4446 query");

var zeroStore = new FakeStore();
var zeroService = Ready(zeroStore);
var zeroState = new NativeYbDealSetInfoState();
zeroService.Attach(zeroState, "zero", "ZeroRole");
Assert(!zeroService.Save(zeroState), "level zero must not save");
Equal(0, zeroStore.Upserts.Count, "level zero upsert count");
Assert(zeroState.IsDirty, "level zero must retain dirty byte");

var ordinaryFailureStore = new FakeStore { UpsertResult = false };
var ordinaryFailureService = Ready(ordinaryFailureStore);
var ordinaryFailureState = new NativeYbDealSetInfoState();
ordinaryFailureService.Attach(ordinaryFailureState, "ptid", "OrdinaryFail");
Assert(ordinaryFailureService.TrySetLimitLevel(ordinaryFailureState, 123),
    "positive setter");
Assert(!ordinaryFailureService.Save(ordinaryFailureState),
    "ordinary failed store reported success");
Equal(1, ordinaryFailureStore.Upserts.Count, "ordinary failure attempt count");
Equal((ushort)123, ordinaryFailureStore.Upserts[0].LimitLevel,
    "ordinary failure saved LimitLevel");
Assert(!ordinaryFailureState.IsDirty,
    "native helper return must clear dirty even on ordinary DB failure");

var throwingStore = new FakeStore { ThrowOnUpsert = true };
var throwingService = Ready(throwingStore);
var throwingState = new NativeYbDealSetInfoState();
throwingService.Attach(throwingState, "ptid", "Throwing");
Assert(throwingService.TrySetLimitLevel(throwingState, 456),
    "throwing setter");
Assert(!throwingService.Save(throwingState),
    "throwing store reported success");
Assert(throwingState.IsDirty,
    "escaped SQL exception must retain native dirty byte");

var serializedStore = new FakeStore();
using var enteredUpsert = new ManualResetEventSlim();
using var releaseUpsert = new ManualResetEventSlim();
serializedStore.OnUpsert = () =>
{
    enteredUpsert.Set();
    Assert(releaseUpsert.Wait(TimeSpan.FromSeconds(5)),
        "serialized upsert release timeout");
};
var serializedService = Ready(serializedStore);
var serializedStateA = new NativeYbDealSetInfoState();
var serializedStateB = new NativeYbDealSetInfoState();
serializedService.Attach(serializedStateA, "ptid", "ReconnectRole");
serializedService.Attach(serializedStateB, "ptid", "reconnectrole");
Assert(serializedService.TrySetLimitLevel(serializedStateA, 111),
    "serialized initial setter");
var saveTask = Task.Run(() => serializedService.Save(serializedStateA));
Assert(enteredUpsert.Wait(TimeSpan.FromSeconds(5)),
    "serialized upsert did not start");
var setTask = Task.Run(() =>
    serializedService.TrySetLimitLevel(serializedStateB, 222));
Assert(!setTask.Wait(TimeSpan.FromMilliseconds(100)),
    "shared-record setter bypassed manager serialization");
releaseUpsert.Set();
Assert(saveTask.GetAwaiter().GetResult(), "serialized first save");
Assert(setTask.GetAwaiter().GetResult(), "serialized second setter");
Equal((ushort)222, serializedService.GetLimitLevel(serializedStateA),
    "shared record lost reconnect update");
Assert(serializedStateB.IsDirty,
    "reconnect update must remain dirty after older save");

Equal(
    "Create Table if not Exists gamedata.M2_YB_Deal_SetInfo(Idx Int AUTO_INCREMENT PRIMARY KEY,PTID char(20) default NULL, CharName char(15) binary not null,LimitLevel smallint(5) Default 0,ModTime DateTime default '0000-00-00 00:00:00',UNIQUE Index Name_Index1 (PTID, CharName));",
    MySqlNativeYbDealSetInfoStore.CreateTableSql, "CREATE SQL");
Equal("delete from gamedata.M2_YB_Deal_SetInfo where LimitLevel = 0;",
    MySqlNativeYbDealSetInfoStore.DeleteZeroSql, "cleanup SQL");
Equal("Select ptid, charname, limitLevel from gamedata.M2_YB_Deal_SetInfo",
    MySqlNativeYbDealSetInfoStore.SelectSql, "load SQL");
Equal(
    "Insert into gamedata.M2_YB_Deal_SetInfo(ptid, charname, limitLevel, ModTime) values(\"{0}\", \"{1}\", {2}, Now()) on duplicate key update  ptid=\"{0}\", charname=\"{1}\", limitLevel={2}, ModTime=Now();",
    MySqlNativeYbDealSetInfoStore.UpsertSqlFormat, "upsert SQL format");

var root = FindRepositoryRoot();
var q2 = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeCmProtocol_Q2.cs"));
Require(q2, "unchecked((ushort)processMessage.nParam2)",
    "CM1265 must read the native WORD at wire+6");
Require(q2, "BuildSm3015(success ? 0 : -1)",
    "CM1265 result ladder");
Reject(q2, "Q2Drop(Grobal2.CM_1265", "CM1265 remained fail-closed");

var tail = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeCmTailProtocol.cs"));
Require(tail, "YbDealSetInfoService?.GetLimitLevel(state)",
    "CM4446 manager-serialized query");
Reject(tail, "Drop(Grobal2.CM_4446", "CM4446 remained fail-closed");

var engine = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
    "UsrEngn.cs"));
Require(engine, "if (saveMode == 0)", "periodic-only save gate");
Require(engine, "YbDealSetInfoService?.Save", "periodic save hook");
Require(engine, "YbDealSetInfoService?.Attach", "character-load attach hook");

var q2Ledger = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
    "NativeCmQ2FailClosed.cs"));
Reject(q2Ledger, "Add(1265,", "CM1265 fail-closed ledger entry");
var tailLedger = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
    "NativeCmTailFailClosed.cs"));
Reject(tailLedger, "Add(4446,", "CM4446 fail-closed ledger entry");

Console.WriteLine(
    "PASS CM1265/SM3015/CM4446 holder, bounds, lifecycle, SQL, and dirty semantics");
return;

static NativeYbDealSetInfoService Ready(FakeStore store)
{
    var service = new NativeYbDealSetInfoService(store);
    Assert(service.TryInitialize(out var error), error);
    return service;
}

static void AssertPacket((SystemModule.ClientPacket Header, byte[] Body) packet,
    ushort ident, int recog, string label)
{
    Equal(ident, packet.Header.Ident, label + " Ident");
    Equal(recog, packet.Header.Recog, label + " Recog");
    Equal((ushort)0, packet.Header.Param, label + " Param");
    Equal((ushort)0, packet.Header.Tag, label + " Tag");
    Equal((ushort)0, packet.Header.Series, label + " Series");
    Equal(0, packet.Body.Length, label + " body length");
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory, AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException("LyoMir2 repository root not found");
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

static void Require(string source, string value, string label)
    => Assert(source.Contains(value, StringComparison.Ordinal),
        label + " missing");

static void Reject(string source, string value, string label)
    => Assert(!source.Contains(value, StringComparison.Ordinal), label);

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class FakeStore : INativeYbDealSetInfoStore
{
    private readonly IReadOnlyList<NativeYbDealSetInfoRow> _rows;

    internal FakeStore(params NativeYbDealSetInfoRow[] rows)
    {
        _rows = rows;
    }

    internal bool UpsertResult { get; set; } = true;

    internal bool ThrowOnUpsert { get; set; }

    internal Action OnUpsert { get; set; }

    internal List<NativeYbDealSetInfoRecord> Upserts { get; } = new();

    public bool TryInitialize(out IReadOnlyList<NativeYbDealSetInfoRow> rows,
        out string error)
    {
        rows = _rows;
        error = string.Empty;
        return true;
    }

    public bool TryUpsert(NativeYbDealSetInfoRecord record, out string error)
    {
        if (ThrowOnUpsert) throw new InvalidOperationException("fixture SQL");
        OnUpsert?.Invoke();
        Upserts.Add(record.Copy());
        error = UpsertResult ? string.Empty : "fixture ordinary failure";
        return UpsertResult;
    }
}
