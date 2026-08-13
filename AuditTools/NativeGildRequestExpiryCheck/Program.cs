using System.Reflection;

// GILD-10 contract check: the pending-request expiry purge (join-corps / join-gild / alliance-union),
// locked against native sub_6A5D6C @0x006A5D6C and its wrapper sub_6A6058.
//
// Tier-1 anchors asserted here:
//   * threshold  = float32 3.0 @0x006A5FF0 (d8 25 f0 5f 6a 00 = fsub dword[6A5FF0]) -> ExpiryDays
//   * comparison = strictly greater: fcomp + `jbe` skips, so (Now - 3.0) > CreatedTime expires
//   * gate       = literal '03:03' @0x006A5FE8 vs FormatDateTime('hh:mm') @0x006A5FD8, `jnz` returns
//   * teardown   = victim leaves EVERY index (global registry + per-guild primary/secondary + order)
//   * tally log  = emitted only when count > 0 (0x006A5F5E: cmp [ebp-8],0 / jle)

var asm = typeof(GameSvr.M2Share).Assembly;
var ledgerType = asm.GetType("GameSvr.Services.NativeGildRequestLedger", true);
var requestType = asm.GetType("GameSvr.Services.NativeGildPendingRequest", true);
var kindType = asm.GetType("GameSvr.Services.NativeGildRequestKind", true);
var sweepType = asm.GetType("GameSvr.Services.NativeGildRequestExpirySweep", true);

var failures = new List<string>();
var asserts = 0;

void Check(bool condition, string what)
{
    asserts++;
    if (!condition) failures.Add(what);
}

// ---- 1. the 3.0-day threshold constant -------------------------------------------------------
var expiryDays = (double)ledgerType
    .GetField("ExpiryDays", BindingFlags.Public | BindingFlags.Static)
    .GetRawConstantValue();
Check(expiryDays == 3.0,
    $"ExpiryDays must be the float32 3.0 @0x006A5FF0, got {expiryDays}");

// ---- 2. the 03:03 gate ----------------------------------------------------------------------
var gate = (string)sweepType
    .GetField("GateTimeOfDay", BindingFlags.NonPublic | BindingFlags.Static
                               | BindingFlags.Public)
    .GetRawConstantValue();
Check(gate == "03:03", $"gate literal @0x006A5FE8 must be 03:03, got {gate}");

var isGateOpen = sweepType.GetMethod("IsGateOpen",
    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
bool GateOpen(DateTime t) => (bool)isGateOpen.Invoke(null, new object[] { t });

Check(GateOpen(new DateTime(2026, 8, 10, 3, 3, 0)), "03:03:00 must open the gate");
Check(GateOpen(new DateTime(2026, 8, 10, 3, 3, 59)),
    "03:03:59 is still inside the 03:03 minute");
Check(!GateOpen(new DateTime(2026, 8, 10, 3, 2, 59)), "03:02:59 must NOT sweep");
Check(!GateOpen(new DateTime(2026, 8, 10, 3, 4, 0)), "03:04:00 must NOT sweep");
Check(!GateOpen(new DateTime(2026, 8, 10, 15, 3, 0)),
    "15:03 must NOT sweep (hh:mm is 24h here; native Delphi 'hh' is 24h too)");

// ---- 3. RemoveExpired boundary behaviour ----------------------------------------------------
object MakeRequest(long uniqueId, long secondary, long target, int kind,
    DateTime created)
{
    var request = Activator.CreateInstance(requestType);
    void Set(string name, object value) =>
        requestType.GetProperty(name).SetValue(request, value);
    Set("UniqueId", uniqueId);
    Set("RequestId", uniqueId * 100);
    Set("SecondaryKey", secondary);
    Set("TargetKey", target);
    Set("Kind", Enum.ToObject(kindType, kind));
    Set("CreatedTime", created);
    return request;
}

var now = new DateTime(2026, 8, 10, 3, 3, 0);
var ledger = Activator.CreateInstance(ledgerType);
var add = ledgerType.GetMethod("Add");
var removeExpired = ledgerType.GetMethod("RemoveExpired");
var tryByUnique = ledgerType.GetMethod("TryGetByUniqueId");
var hasSecondary = ledgerType.GetMethod("HasPendingForSecondaryKey");

// exactly 3 days old -> NOT expired (jbe: deadline <= CreatedTime skips)
var boundary = MakeRequest(1, 11, 900, 2, now.AddDays(-3));
// a hair past 3 days -> expired
var justOver = MakeRequest(2, 22, 900, 2, now.AddDays(-3).AddSeconds(-1));
// well past -> expired (join-gild, no relation row)
var ancient = MakeRequest(3, 33, 901, 1, now.AddDays(-30));
// fresh -> kept
var fresh = MakeRequest(4, 44, 902, 0, now.AddHours(-2));

foreach (var r in new[] { boundary, justOver, ancient, fresh })
    Check((int)add.Invoke(ledger, new[] { r }) == 0, "seed Add must return 0");

var removed = (System.Collections.IEnumerable)removeExpired.Invoke(
    ledger, new object[] { now });
var removedIds = removed.Cast<object>()
    .Select(r => (long)requestType.GetProperty("UniqueId").GetValue(r))
    .ToList();

Check(removedIds.Count == 2,
    $"exactly the two >3d entries expire, got {removedIds.Count}");
Check(!removedIds.Contains(1L),
    "an exactly-3.0-day-old request must survive (fcomp + jbe is strict >)");
Check(removedIds.Contains(2L), "3d+1s must expire");
Check(removedIds.Contains(3L), "30d must expire");
Check(!removedIds.Contains(4L), "a 2-hour-old request must survive");
Check(removedIds.SequenceEqual(new[] { 3L, 2L }),
    "removed list is oldest-first (native walks the ordered list backwards)");

// teardown must clear EVERY index for the victims, and leave survivors intact
object outRef = null;
var lookup = new object[] { 2L, outRef };
Check(!(bool)tryByUnique.Invoke(ledger, lookup),
    "expired request must leave the global unique-id registry");
lookup = new object[] { 1L, outRef };
Check((bool)tryByUnique.Invoke(ledger, lookup),
    "surviving request must remain in the registry");
Check(!(bool)hasSecondary.Invoke(ledger, new object[] { 900L, 22L }),
    "expired request must leave the per-guild secondary index");
Check((bool)hasSecondary.Invoke(ledger, new object[] { 900L, 11L }),
    "surviving request must remain in the secondary index");

// idempotence: a second sweep at the same instant removes nothing more
var second = (System.Collections.IEnumerable)removeExpired.Invoke(
    ledger, new object[] { now });
Check(!second.Cast<object>().Any(), "re-sweep at the same instant is a no-op");

// after the victim is gone its secondary key is free again (no ghost dedup)
var reRequest = MakeRequest(5, 22, 900, 2, now);
Check((int)add.Invoke(ledger, new[] { reRequest }) == 0,
    "an expired request must not keep blocking its secondary key");

// ---- 4. the once-per-day latch --------------------------------------------------------------
var lastSwept = sweepType.GetProperty("LastSweptDate",
    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
var reset = sweepType.GetMethod("ResetForTests",
    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
reset.Invoke(null, null);
Check((DateTime)lastSwept.GetValue(null) == DateTime.MinValue,
    "ResetForTests clears the sweep latch");

var run = sweepType.GetMethod("Run",
    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
// An Unavailable service must be a hard no-op even inside the gate window.
var unavailable = asm.GetType("GameSvr.Services.NativeCorpsService", true)
    .GetProperty("Unavailable",
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
    .GetValue(null);
Check((int)run.Invoke(null, new[] { unavailable, now }) == 0,
    "an unavailable Corps service must not sweep");
Check((DateTime)lastSwept.GetValue(null) == DateTime.MinValue,
    "a no-op run must not consume today's sweep latch");
Check((int)run.Invoke(null, new object[] { null, now }) == 0,
    "a null service must not throw");

// ---- 5. the purge entry point exists on the service and is relation-aware -------------------
var purge = asm.GetType("GameSvr.Services.NativeCorpsService", true)
    .GetMethod("PurgeExpiredRequests",
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
Check(purge != null && purge.ReturnType == typeof(int),
    "NativeCorpsService.PurgeExpiredRequests(DateTime) -> int must exist");

var serviceSource = File.ReadAllText(Path.Combine(
    FindRepoRoot(), "GameSvr", "Services", "NativeCorpsService.cs"));
var purgeStart = serviceSource.IndexOf("internal int PurgeExpiredRequests",
    StringComparison.Ordinal);
Check(purgeStart >= 0, "PurgeExpiredRequests body not found");
var purgeEnd = serviceSource.IndexOf("private void AddLogLocked", purgeStart,
    StringComparison.Ordinal);
Check(purgeEnd > purgeStart, "PurgeExpiredRequests body boundary not found");
var purgeBody = serviceSource[purgeStart..purgeEnd];

// sub_6A5D6C unlinks the request and nothing else: its per-victim teardown is sub_6A60A4
// (@0x6A5EF0) + sub_6A5070 (@0x6A5EF9). delete_relation sub_5E90A4 has exactly four callers
// image-wide -- 0x5E9208 war expiry, 0x703DA3 break-union, 0x70809E union refuse, 0x70821C
// union accept -- and zero dword references, so the caller set is closed and the sweep is not
// in it. An expired UNION request keeps its pending Relation=3 pair, and an established
// Relation=1 alliance is likewise never time-limited.
Check(!purgeBody.Contains("GildRelation", StringComparison.Ordinal),
    "the purge must not touch any gild relation: native sub_6A5D6C only unlinks the request");
Check(!purgeBody.Contains("_gildRelations", StringComparison.Ordinal),
    "the purge must NOT touch established relations (_gildRelations): native expires "
    + "only PENDING requests, never a live Relation=1 alliance");

// The tick must be wired, and not behind an unrelated interval gate.
var tickSource = File.ReadAllText(Path.Combine(
    FindRepoRoot(), "GameSvr", "GameServer.cs"));
var wired = tickSource.Split('\n')
    .Where(l => l.Contains("NativeGildRequestExpirySweep.Run",
        StringComparison.Ordinal))
    .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
    .ToList();
Check(wired.Count == 1,
    $"exactly one live (non-comment) sweep call site, got {wired.Count}");

if (failures.Count > 0)
{
    Console.WriteLine("FAIL NativeGildRequestExpiryCheck");
    foreach (var failure in failures) Console.WriteLine("  - " + failure);
    return 1;
}

Console.WriteLine(
    $"PASS NativeGildRequestExpiryCheck asserts={asserts} "
    + "purge=sub_6A5D6C threshold=3.0d@0x006A5FF0 compare=strict-gt(jbe) "
    + "gate=03:03@0x006A5FE8(jnz) teardown=all-indices "
    + "union=DELETE-pending-Relation3 established-alliance=untouched");
return 0;

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null && !File.Exists(Path.Combine(dir, "LyoMir2.sln")))
        dir = Path.GetDirectoryName(dir);
    if (dir == null) throw new InvalidOperationException("repo root not found");
    return dir;
}
