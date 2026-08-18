using System.Reflection;
using GameSvr;
using SystemModule;
using SystemModule.Common;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig
{
    dwFloorItemCanPickUpTime = int.MaxValue,
    nSendRefMsgRange = 0
};
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.g_boGameLogGold = false;

Equal(4278, Grobal2.CM_PICKUP_RANGE, "CM_PICKUP_RANGE ident");
Assert(!new TMapFlag().boPICKUP, "PICKUP flag must default disabled");
CheckCoordinateOrder();
CheckOwnerFilter();
CheckDisabledIsSilent();
CheckDealingDispatchSplit();
CheckFailedCellDoesNotStopScan();
CheckEnabledTransaction();
CheckProductionWiring();

Console.WriteLine(
    "NativePickupRangeCompatCheck PASS ident=4278 flag=PICKUP cells=25 order=x/y owner=self-master transaction=shared");

static void CheckCoordinateOrder()
{
    var method = typeof(TPlayObject).GetMethod(
        "EnumerateNativePickupRangeCells",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert(method != null, "native range enumerator missing");
    var cells = ((IEnumerable<(int X, int Y)>)method!.Invoke(
        null, new object[] { 10, 20 })!).ToArray();

    Equal(25, cells.Length, "native range cell count");
    for (var i = 0; i < cells.Length; i++)
    {
        Equal(8 + i / 5, cells[i].X, $"cell {i} X order");
        Equal(18 + i % 5, cells[i].Y, $"cell {i} Y order");
    }
}

static void CheckOwnerFilter()
{
    var environment = NewEnvironment(true);
    var requester = NewPlayer(environment, 5, 5);
    var other = NewPlayer(environment, 5, 5);
    var foreignOwner = NewOwnedObject(other);
    var requesterOwner = NewOwnedObject(requester);
    var foreign = NewItem("foreign", foreignOwner);
    var owned = NewItem("owned", requesterOwner);

    Place(environment, 5, 5, foreign);
    Place(environment, 5, 5, owned);
    Assert(ReferenceEquals(owned,
            SelectRangeItem(environment, 5, 5, requester)),
        "foreign item must be skipped for current player's owned item");

    var unowned = NewItem("unowned", null);
    Place(environment, 5, 6, unowned);
    Assert(ReferenceEquals(unowned,
            SelectRangeItem(environment, 5, 6, requester)),
        "unowned item must be eligible");
    Assert(ReferenceEquals(foreign,
            SelectRangeItem(environment, 5, 5, other)),
        "item eligibility must follow the current requester");

    var first = NewItem("first-eligible", null);
    var second = NewItem("second-eligible", null);
    Place(environment, 6, 6, first);
    Place(environment, 6, 6, second);
    Assert(ReferenceEquals(second,
            SelectRangeItem(environment, 6, 6, requester)),
        "range selector must return the first eligible node from the native head chain");
}

static void CheckDisabledIsSilent()
{
    var environment = NewEnvironment(false);
    var player = NewPlayer(environment, 5, 5);
    var item = NewGold(7, null);
    Place(environment, 5, 5, item);
    var messageCount = player.m_MsgList.Count;

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_PICKUP_RANGE
    }), "disabled dispatcher result");

    Equal(0, player.m_nGold, "disabled gold state");
    Assert(ReferenceEquals(item, environment.GetItem(5, 5)),
        "disabled handler moved the item");
    Equal(messageCount, player.m_MsgList.Count,
        "disabled handler emitted a message");
    Assert(player.m_DefMsg == null,
        "disabled handler emitted a direct response");
}

static void CheckDealingDispatchSplit()
{
    var environment = NewEnvironment(true);
    var player = NewPlayer(environment, 5, 5);
    player.m_boDealing = true;
    var item = NewGold(7, null);
    Place(environment, 5, 5, item);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_PICKUP,
        nParam2 = 5,
        nParam3 = 5
    }), "dealing normal-pickup dispatcher result");
    Equal(0, player.m_nGold,
        "normal pickup bypassed the dealing guard");
    Assert(ReferenceEquals(item, environment.GetItem(5, 5)),
        "normal pickup moved the item while dealing");

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_PICKUP_RANGE
    }), "dealing range-pickup dispatcher result");
    Equal(7, player.m_nGold,
        "range pickup inherited the normal dealing guard");
    Assert(environment.GetItem(5, 5) == null,
        "range pickup left the eligible item while dealing");
}

static void CheckFailedCellDoesNotStopScan()
{
    var environment = NewEnvironment(true);
    var player = NewPlayer(environment, 5, 5);
    var overLimit = NewGold(player.m_nGoldMax + 1, null);
    var nextCell = NewGold(1, null);
    Place(environment, 3, 3, overLimit);
    Place(environment, 3, 4, nextCell);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_PICKUP_RANGE
    }), "failed-cell range dispatcher result");

    Equal(1, player.m_nGold,
        "failed pickup stopped scanning later cells");
    Assert(ReferenceEquals(overLimit, environment.GetItem(3, 3)),
        "failed pickup did not restore its map item");
    Assert(environment.GetItem(3, 4) == null,
        "later eligible cell was not picked after an earlier failure");
}

static void CheckEnabledTransaction()
{
    var environment = NewEnvironment(true);
    var player = NewPlayer(environment, 5, 5);
    var other = NewPlayer(environment, 5, 5);
    var unowned = NewGold(1, null);
    var owned = NewGold(2, NewOwnedObject(player));
    var foreign = NewGold(4, NewOwnedObject(other));
    var boundary = NewGold(16, null);
    var outside = NewGold(8, null);

    Place(environment, 3, 3, unowned);
    Place(environment, 4, 5, owned);
    Place(environment, 5, 5, foreign);
    Place(environment, 7, 7, boundary);
    Place(environment, 2, 2, outside);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_PICKUP_RANGE
    }), "enabled dispatcher result");

    Equal(19, player.m_nGold,
        "shared pickup transaction gold side effects");
    Assert(environment.GetItem(3, 3) == null,
        "lower range boundary was not picked");
    Assert(environment.GetItem(4, 5) == null,
        "current player's owned item was not picked");
    Assert(environment.GetItem(7, 7) == null,
        "upper range boundary was not picked");
    Assert(ReferenceEquals(foreign, environment.GetItem(5, 5)),
        "foreign item was picked");
    Assert(ReferenceEquals(outside, environment.GetItem(2, 2)),
        "outside item was picked");

    player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_PICKUP_RANGE
    });
    Equal(19, player.m_nGold,
        "repeat request duplicated pickup side effects");
}

static void CheckProductionWiring()
{
    var rangeSource = File.ReadAllText(FindRepoFile(
        "GameSvr", "Players", "TPlayObject.NativePickupRange.cs"));
    Assert(rangeSource.Contains("ClientPickUpItem(mapItem, x, y)"),
        "range handler does not call the shared pickup transaction");
    foreach (var forbidden in new[]
             {
                 "DeleteFromMap", "IncGold(", "AddItemToBag("
             })
    {
        Assert(!rangeSource.Contains(forbidden),
            $"range handler duplicates transaction primitive {forbidden}");
    }

    var mapSource = File.ReadAllText(FindRepoFile("GameSvr", "Maps",
        "Maps.cs"));
    Assert(mapSource.Contains("s34.Equals(\"PICKUP\"",
            StringComparison.Ordinal) &&
        mapSource.Contains("MapFlag.boPICKUP = true;",
            StringComparison.Ordinal),
        "MapInfo PICKUP flag parser missing");
}

static TPlayObject NewPlayer(Envirnoment environment, short x, short y)
{
    return new TPlayObject
    {
        m_PEnvir = environment,
        m_nCurrX = x,
        m_nCurrY = y,
        m_nGold = 0,
        m_nGoldMax = 100_000,
        m_sMapName = "pickup-range-test",
        m_sCharName = "pickup-range-player"
    };
}

static TBaseObject NewOwnedObject(TPlayObject owner)
{
    return new TBaseObject
    {
        m_btRaceServer = Grobal2.RC_MONSTER,
        m_Master = owner
    };
}

static MapItem NewGold(int count, TBaseObject owner)
{
    return new MapItem
    {
        Name = Grobal2.sSTRING_GOLDNAME,
        Count = count,
        OfBaseObject = owner,
        CanPickUpTick = HUtil32.GetTickCount()
    };
}

static MapItem NewItem(string name, TBaseObject owner)
{
    return new MapItem
    {
        Name = name,
        OfBaseObject = owner,
        CanPickUpTick = HUtil32.GetTickCount()
    };
}

static Envirnoment NewEnvironment(bool pickupEnabled)
{
    var environment = new Envirnoment
    {
        Flag = new TMapFlag { boPICKUP = pickupEnabled },
        sMapName = "pickup-range-test"
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)12, (short)12 });
    return environment;
}

static void Place(Envirnoment environment, int x, int y, MapItem item)
{
    Assert(ReferenceEquals(item, environment.AddToMap(x, y,
        CellType.OS_ITEMOBJECT, item)), $"place item at {x},{y}");
}

static MapItem SelectRangeItem(Envirnoment environment, int x, int y,
    TPlayObject requester)
{
    var method = typeof(Envirnoment).GetMethod(
        "GetNativePickupRangeItem",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "native range item selector missing");
    return (MapItem)method!.Invoke(environment,
        new object[] { x, y, requester });
}

static string FindRepoFile(params string[] relativeParts)
{
    var path = Path.Combine(new[] { AuditRepoRoot.Resolve() }
        .Concat(relativeParts).ToArray());
    if (!File.Exists(path))
        throw new FileNotFoundException(
            "Could not locate repository source file", path);
    return path;
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

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
    }
}

static void Assert(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException(label);
    }
}
