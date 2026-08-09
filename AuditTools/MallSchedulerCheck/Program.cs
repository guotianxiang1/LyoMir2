using GameSvr.Mall;

var start = new DateTime(2026, 7, 14, 5, 0, 0, DateTimeKind.Local);
var scheduler = new MallRefreshScheduler();

Assert(!scheduler.TryAdd("6:00", start), "accepted a non-native time format");
Assert(!scheduler.TryAdd("24:00:00", start), "accepted an out-of-day time");
Assert(scheduler.TryAdd("00:00:00", start), "midnight registration failed");
Assert(scheduler.TryAdd("06:00:00", start), "06:00 registration failed");
Assert(scheduler.TryAdd("12:00:00", start), "12:00 registration failed");
Assert(scheduler.TryAdd("06:00:00", start) && scheduler.Count == 3,
    "duplicate registration created another schedule node");

Assert(!scheduler.TryConsume(start), "schedule fired before its due time");
Assert(scheduler.TryConsume(start.Date.AddHours(6)), "06:00 schedule did not fire");
Assert(!scheduler.TryConsume(start.Date.AddHours(6)), "06:00 schedule fired twice");
Assert(scheduler.TryConsume(start.Date.AddHours(12)), "12:00 schedule did not fire");
Assert(scheduler.TryConsume(start.Date.AddDays(1)),
    "past-at-registration midnight schedule did not move to the next day");

var exact = new MallRefreshScheduler();
var exactTime = start.Date.AddHours(6);
Assert(exact.TryAdd("06:00:00", exactTime), "exact-time registration failed");
Assert(!exact.TryConsume(exactTime), "exact-time registration incorrectly fired immediately");
Assert(exact.TryConsume(exactTime.AddDays(1)), "exact-time schedule did not fire next day");

var delayed = new MallRefreshScheduler();
Assert(delayed.TryAdd("06:00:00", start), "delayed schedule registration failed");
Assert(delayed.TryConsume(start.Date.AddHours(7)), "delayed main-loop tick missed the refresh");
Assert(!delayed.TryConsume(start.Date.AddHours(7)), "delayed refresh fired twice");

Console.WriteLine("PASS schedules=3 duplicate=dedup daily=advance delayed=catch-up");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
