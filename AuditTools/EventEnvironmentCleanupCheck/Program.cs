using System.Reflection;
using GameSvr;
using SystemModule;

var target = NewEnvironment("same-name");
var differentInstance = NewEnvironment("same-name");
var manager = new EventManager();

var visibleTypeOne = new Event(target, 1, 1, 1, int.MaxValue, true);
var invisibleTypeOne = new Event(target, 2, 1, 1, int.MaxValue, false);
var invisibleMapBackedTypeTwo = new Event(target, 3, 1, 2, int.MaxValue, true);
invisibleMapBackedTypeTwo.m_boVisible = false;
var differentInstanceTypeOne = new Event(differentInstance, 1, 1, 1, int.MaxValue, true);

manager.AddEvent(visibleTypeOne);
manager.AddEvent(invisibleTypeOne);
manager.AddEvent(invisibleMapBackedTypeTwo);
manager.AddEvent(differentInstanceTypeOne);

Assert(ReferenceEquals(visibleTypeOne, target.GetEvent(1, 1)),
    "visible event was not added to the target map");
Assert(ReferenceEquals(invisibleMapBackedTypeTwo, target.GetEvent(3, 1)),
    "map-backed invisible event was not added to the target map");

Equal(0, CloseEventsForEnvironment(manager, null),
    "null environment closed events");
Equal(2, CloseEventsForEnvironment(manager, target, 1),
    "environment/type cleanup did not close both matching events");
AssertClosed(visibleTypeOne, "visible matching event");
AssertClosed(invisibleTypeOne, "invisible matching event");
Assert(target.GetEvent(1, 1) == null,
    "visible matching event remained on the map");
Assert(!invisibleMapBackedTypeTwo.m_boClosed,
    "type-filtered cleanup closed a different event type");
Assert(ReferenceEquals(invisibleMapBackedTypeTwo, target.GetEvent(3, 1)),
    "type-filtered cleanup removed a different event type from the map");
Assert(!differentInstanceTypeOne.m_boClosed &&
       ReferenceEquals(differentInstance, differentInstanceTypeOne.m_Envir),
    "cleanup matched an equal-looking but different environment instance");

Equal(0, CloseEventsForEnvironment(manager, target, 1),
    "repeated type-filtered cleanup was not idempotent");
Equal(1, CloseEventsForEnvironment(manager, target, 0),
    "native zero-type cleanup did not close the remaining target event");
AssertClosed(invisibleMapBackedTypeTwo, "map-backed invisible event");
Assert(target.GetEvent(3, 1) == null,
    "map-backed invisible event remained on the map after cleanup");
Equal(0, CloseEventsForEnvironment(manager, target),
    "repeated unfiltered cleanup was not idempotent");

Equal(4, GetEventListCount(manager, "_eventList"),
    "closed events left the active list before EventManager.Run");
Equal(0, GetEventListCount(manager, "_closedEventList"),
    "closed events entered the delayed list before EventManager.Run");
var firstRunTick = unchecked(GetRunTick(manager) + 251);
RunEventManager(manager, firstRunTick);
Equal(1, GetEventListCount(manager, "_eventList"),
    "EventManager.Run did not remove closed events from the active list");
Equal(3, GetEventListCount(manager, "_closedEventList"),
    "EventManager.Run did not retain closed events for delayed cleanup");
Assert(manager.GetEvent(target, 1, 1, 1) == null,
    "closed target event remained discoverable after manager run");
Assert(ReferenceEquals(differentInstanceTypeOne,
        manager.GetEvent(differentInstance, 1, 1, 1)),
    "cleanup changed the other environment event");

RunEventManager(manager, unchecked(firstRunTick + 300_000));
Equal(0, GetEventListCount(manager, "_closedEventList"),
    "expired closed events were not released");

CheckPileStonesExpiryBoundary();
CheckPileStonesExpiryWrap();
CheckClosedPileStonesReapCap();

Console.WriteLine("EventEnvironmentCleanupCheck PASS");

static void CheckPileStonesExpiryBoundary()
{
    const int duration = 300_000;
    const int openTick = 1_000_000;
    var environment = NewEnvironment("pile-expiry");
    var manager = new EventManager();
    var pile = new PileStones(environment, 4, 4,
        Grobal2.ET_PILESTONES, duration);
    manager.AddEvent(pile);
    SetOpenStartTick(pile, openTick);

    pile.AddEventParam();
    Equal(2, pile.m_nEventParam, "pile increment");
    Equal(openTick, GetOpenStartTick(pile),
        "pile increment refreshed the expiry baseline");

    pile.Run(unchecked(openTick + duration));
    Assert(!pile.m_boClosed, "pile closed at elapsed == duration");
    Assert(ReferenceEquals(pile, environment.GetEvent(4, 4)),
        "pile left the map at elapsed == duration");
    Equal(1, GetEventListCount(manager, "_eventList"),
        "pile left active list at elapsed == duration");
    Equal(0, GetEventListCount(manager, "_closedEventList"),
        "pile entered closed list at elapsed == duration");

    var closeTick = unchecked(openTick + duration + 1);
    pile.Run(closeTick);
    AssertClosed(pile, "pile at duration + 1");
    Equal(closeTick, pile.m_dwCloseTick, "pile close tick");
    Assert(environment.GetEvent(4, 4) == null,
        "expired pile remained on the map");

    SetRunTick(manager, unchecked(closeTick - 251));
    RunEventManager(manager, closeTick);
    Equal(0, GetEventListCount(manager, "_eventList"),
        "expired pile remained active after manager scan");
    Equal(1, GetEventListCount(manager, "_closedEventList"),
        "expired pile did not enter the closed FIFO");

    RunEventManager(manager, unchecked(closeTick + 299_999));
    Equal(1, GetEventListCount(manager, "_closedEventList"),
        "pile reaped before close + 300000");
    RunEventManager(manager, unchecked(closeTick + 300_000));
    Equal(0, GetEventListCount(manager, "_closedEventList"),
        "pile not reaped at close + 300000");
}

static void CheckPileStonesExpiryWrap()
{
    const int duration = 300_000;
    var openTick = unchecked((int)0xFFFF_FF00u);
    var environment = NewEnvironment("pile-wrap");
    var pile = new PileStones(environment, 5, 5,
        Grobal2.ET_PILESTONES, duration);
    SetOpenStartTick(pile, openTick);

    pile.Run(unchecked(openTick + duration));
    Assert(!pile.m_boClosed,
        "wrapped pile closed at elapsed == duration");
    pile.Run(unchecked(openTick + duration + 1));
    AssertClosed(pile, "wrapped pile at duration + 1");
}

static void CheckClosedPileStonesReapCap()
{
    const int duration = 300_000;
    const int closeTick = 2_000_000;
    var manager = new EventManager();
    for (var index = 0; index < 11; index++)
    {
        var pile = new PileStones(null, index, 0,
            Grobal2.ET_PILESTONES, duration);
        SetOpenStartTick(pile, closeTick - duration - 1);
        pile.Run(closeTick);
        manager.AddEvent(pile);
    }

    SetRunTick(manager, closeTick - 251);
    RunEventManager(manager, closeTick);
    Equal(11, GetEventListCount(manager, "_closedEventList"),
        "closed pile batch was not queued");

    RunEventManager(manager, closeTick + 300_000);
    Equal(1, GetEventListCount(manager, "_closedEventList"),
        "closed FIFO did not enforce the ten-per-run cap");
    RunEventManager(manager, closeTick + 300_000);
    Equal(0, GetEventListCount(manager, "_closedEventList"),
        "closed FIFO did not reap the eleventh pile on the next run");
}

static int CloseEventsForEnvironment(EventManager manager,
    Envirnoment environment, int? eventType = null)
{
    var closeMethod = typeof(EventManager).GetMethod("CloseEventsForEnvironment",
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (closeMethod == null)
    {
        throw new InvalidOperationException("EventManager.CloseEventsForEnvironment is missing");
    }

    return (int)closeMethod.Invoke(manager, new object[] { environment, eventType });
}

static int GetEventListCount(EventManager manager, string fieldName)
{
    var field = typeof(EventManager).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (field?.GetValue(manager) is not IList<Event> events)
        throw new InvalidOperationException("EventManager list field is missing: " + fieldName);
    return events.Count;
}

static int GetRunTick(EventManager manager)
{
    var field = typeof(EventManager).GetField("_runTick",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("EventManager run tick field is missing");
    return (int)(field.GetValue(manager) ?? 0);
}

static void SetRunTick(EventManager manager, int tick)
{
    var field = typeof(EventManager).GetField("_runTick",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("EventManager run tick field is missing");
    field.SetValue(manager, tick);
}

static int GetOpenStartTick(Event @event)
{
    var field = typeof(Event).GetField("m_dwOpenStartTick",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Event open tick field is missing");
    return (int)(field.GetValue(@event) ?? 0);
}

static void SetOpenStartTick(Event @event, int tick)
{
    var field = typeof(Event).GetField("m_dwOpenStartTick",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Event open tick field is missing");
    field.SetValue(@event, tick);
}

static void RunEventManager(EventManager manager, int currentTick)
{
    var method = typeof(EventManager).GetMethod("Run",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(int) }, null)
        ?? throw new InvalidOperationException("EventManager.Run(Int32) is missing");
    method.Invoke(manager, new object[] { currentTick });
}

static Envirnoment NewEnvironment(string name)
{
    var environment = new Envirnoment { sMapName = name };
    typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance |
        BindingFlags.NonPublic)!.Invoke(environment, new object[] { (short)10, (short)10 });
    return environment;
}

static void AssertClosed(Event @event, string name)
{
    Assert(@event.m_boClosed, $"{name} was not marked closed");
    Assert(!@event.m_boActive, $"{name} remained active");
    Assert(!@event.m_boVisible, $"{name} remained visible");
    Assert(@event.m_Envir == null, $"{name} retained its environment");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
