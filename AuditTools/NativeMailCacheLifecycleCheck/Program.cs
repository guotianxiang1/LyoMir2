using System.Collections;
using System.Reflection;
using GameSvr.Services;
using SystemModule;

var gameAssembly = typeof(NativeMailWireCodec).Assembly;
var cacheType = RequiredType("GameSvr.Services.NativeMailCacheService");
var summaryType = RequiredType("GameSvr.Services.NativeMailSummary");
var recordType = RequiredType("GameSvr.Services.NativeMailRecord");
var entryType = RequiredType("GameSvr.Services.NativeMailCacheEntry");

CheckStableUserIdAndIndependentLoadedFlags();
CheckSweepGateAndDefaultCapacity();
CheckTagSixPolicy();
CheckRegisteredMailBeforeLazyLoad();
CheckInactiveMailboxInvalidation();
CheckSourceContract();

Console.WriteLine("NativeMailCacheLifecycleCheck PASS");

void CheckStableUserIdAndIndependentLoadedFlags()
{
    Reset(1_000);
    var now = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
    Seed(now,
        Summary(42, "first-name", 1, 1, 3),
        Summary(42, "renamed", 1, 2, 2));

    Equal(1, MailboxCount(), "mailbox key must be stable UserId");
    var touchArgs = new object[] { 42L, "renamed", now, null! };
    Equal(true, Invoke("TouchExisting", touchArgs), "touch existing mailbox");
    Equal(3, ((int[])touchArgs[3])[0], "preloaded unread count");

    MergeStatus(42, "renamed", 1, 1, now,
        Entry(100, 1, 1, 3, now.AddHours(-1)));
    Equal(false, TryCategory(42, 1, out _),
        "status-1 load must not set status-2 loaded flag");

    MergeStatus(42, "renamed", 1, 2, now,
        Entry(101, 1, 2, 3, now.AddHours(-2)));
    Equal(true, TryCategory(42, 1, out var entries),
        "category must publish after both statuses load");
    Equal(2, entries.Count, "merged status lists");

    Equal(true, Invoke("MarkRead", new object[] { 42L, 1, 100 }),
        "mark unread mail read");
    var countArgs = new object[] { 42L, null! };
    Equal(true, Invoke("TryGetUnreadCounts", countArgs), "read cached counts");
    Equal(0, ((int[])countArgs[1])[0], "read transition decrements unread count");
}

void CheckSweepGateAndDefaultCapacity()
{
    Reset(10_000);
    var now = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
    var entries = Enumerable.Range(0, 31)
        .Select(i => Entry(1_000 + i, 1, 2, 3, now.AddMinutes(-i)))
        .ToArray();
    MergeStatus(77, "capacity", 1, 1, now);
    MergeStatus(77, "capacity", 1, 2, now, entries);

    Equal(0, Sweep(189_999, now).Count, "180-second sweep lower boundary");
    var deleted = Sweep(190_000, now);
    Equal(1, deleted.Count, "default capacity removes one eligible oldest mail");
    Equal(1_030, deleted[0], "default capacity removes list tail");
    Equal(30, CategoryCount(77, 1), "default capacity is 30");
}

void CheckTagSixPolicy()
{
    Reset(0);
    var now = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
    var entries = Enumerable.Range(0, 21)
        .Select(i => Entry(2_000 + i, 6, 1, 1, now.AddMinutes(-i)))
        .Append(Entry(2_100, 6, 1, 1, now.AddDays(-4)))
        .ToArray();
    MergeStatus(88, "system", 6, 1, now, entries);
    MergeStatus(88, "system", 6, 2, now);

    var deleted = Sweep(180_000, now);
    Equal(true, deleted.Contains(2_100), "tag 6 three-day retention");
    Equal(20, CategoryCount(88, 6), "tag 6 capacity is 20");
}

void CheckInactiveMailboxInvalidation()
{
    Reset(0);
    var created = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
    MergeStatus(99, "inactive", 1, 1, created,
        Entry(3_000, 1, 1, 1, created.AddHours(1)));
    MergeStatus(99, "inactive", 1, 2, created);

    Sweep(180_000, created.AddSeconds(259_201));
    Equal(1, MailboxCount(), "inactive sweep must retain dictionary entry");
    Equal(false, TryCategory(99, 1, out _),
        "inactive sweep clears lists and both loaded flags");

    var touchArgs = new object[] { 99L, "inactive", created.AddDays(4), null! };
    Equal(true, Invoke("TouchExisting", touchArgs),
        "inactive dictionary entry remains touchable");
    Equal(1, ((int[])touchArgs[3])[0],
        "inactive invalidation retains summary count until lazy reload");
}

void CheckRegisteredMailBeforeLazyLoad()
{
    Reset(0);
    var now = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
    MergeStatus(89, "registered", 6, 1, now,
        Entry(2_499, 6, 1, 1, now.AddDays(-5)));
    var entry = Entry(2_500, 6, 1, 1, now.AddDays(-4));
    var record = entryType.GetProperty(
        "Record", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!;
    Invoke("Register", new object[]
    {
        89L, "registered", record, new List<TUserItem>(), now.AddDays(-4)
    });

    var cachedArgs = new object[] { 89L, 6, null! };
    Equal(true, Invoke("TryGetCachedCategory", cachedArgs),
        "registered mailbox category must exist before lazy load");
    var cached = (IList)cachedArgs[2];
    Equal(2_500, RecordId(cached[0]!),
        "new mail registration must insert at native list index zero");

    var deleted = Sweep(180_000, now);
    Equal(true, deleted.Contains(2_500),
        "new-mail registration must sweep before first lazy load");
    Equal(1, MailboxCount(), "registered mailbox remains after sweep");
}

int RecordId(object entry)
{
    var record = entryType.GetProperty(
        "Record", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!;
    return (int)recordType.GetProperty(
        "Id", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(record)!;
}

void CheckSourceContract()
{
    var source = File.ReadAllText(FindRepositoryFile(
        "GameSvr", "Services", "NativeMailCacheService.cs"));
    var store = File.ReadAllText(FindRepositoryFile(
        "GameSvr", "Services", "NativeMailStore.cs"));
    Require(source, "Dictionary<long, NativeMailbox>",
        "cache must use the stable 64-bit UserId key");
    Require(source, "SweepIntervalMilliseconds = 180_000",
        "native sweep interval");
    Require(source, "InactiveMailboxSeconds = 259_200",
        "native inactive mailbox threshold");
    Require(source, "tag == 6 ? SystemRetentionDays : DefaultRetentionDays",
        "tag 6 retention split");
    Require(source, "Array.Clear(mailbox.UnreadLoaded",
        "inactive sweep clears status-1 loaded flags");
    Require(source, "Array.Clear(mailbox.ReadLoaded",
        "inactive sweep clears status-2 loaded flags");
    Forbid(source, "Mailboxes.Remove(",
        "runtime sweep must never evict the mailbox dictionary entry");
    Require(source, "Ticks / TimeSpan.TicksPerSecond",
        "native cleanup comparator must compare whole-second age");
    Forbid(source, "Record.Id.CompareTo",
        "native cleanup comparator has no mail-id tie breaker");
    Require(store, "CREATE TABLE IF NOT EXISTS gamedata.mailitem(",
        "native mail schema owner");
    Require(store, "CREATE TABLE if not exists gamedata.mailitem_b like gamedata.mailitem",
        "mail archive schema must use native LIKE DDL");
    Require(store, "CREATE TABLE IF NOT EXISTS gamedata.Money_order(",
        "native money-order schema owner");
    Forbid(store, "CREATE INDEX", "native mail DDL must not invent indexes");
    Forbid(store, "FOREIGN KEY", "native mail DDL must not invent foreign keys");
    Forbid(store, "ENGINE=", "native mail DDL must inherit database engine");
    Forbid(store, "DEFAULT CHARSET", "native mail DDL must inherit database charset");
}

object Summary(long recipientId, string recipientName, byte tag,
    byte mailStatus, int count) =>
    Activator.CreateInstance(summaryType,
        recipientId, recipientName, tag, mailStatus, count)!;

object Entry(int id, byte tag, byte mailStatus, byte attachStatus, DateTime created)
{
    var record = Activator.CreateInstance(recordType, nonPublic: true)!;
    SetProperty(record, "Id", id);
    SetProperty(record, "MailType", tag);
    SetProperty(record, "MailStatus", mailStatus);
    SetProperty(record, "AttachStatus", attachStatus);
    SetProperty(record, "CreateDate", created);
    return Activator.CreateInstance(entryType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, args: new object[] { record, new List<TUserItem>() }, culture: null)!;
}

void MergeStatus(long recipientId, string recipientName, int tag, byte mailStatus,
    DateTime now, params object[] entries)
{
    var array = Array.CreateInstance(entryType, entries.Length);
    for (var i = 0; i < entries.Length; i++) array.SetValue(entries[i], i);
    Invoke("MergeLoadedStatus",
        new object[] { recipientId, recipientName, tag, mailStatus, array, now });
}

void Seed(DateTime now, params object[] summaries)
{
    var array = Array.CreateInstance(summaryType, summaries.Length);
    for (var i = 0; i < summaries.Length; i++) array.SetValue(summaries[i], i);
    Invoke("SeedSummaries", new object[] { array, now });
}

bool TryCategory(long recipientId, int tag, out IList entries)
{
    var args = new object[] { recipientId, tag, null! };
    var result = (bool)Invoke("TryGetCategory", args)!;
    entries = result ? (IList)args[2] : null!;
    return result;
}

int CategoryCount(long recipientId, int tag)
{
    Equal(true, TryCategory(recipientId, tag, out var entries), "loaded category lookup");
    return entries.Count;
}

List<int> Sweep(int tick, DateTime now) =>
    ((IEnumerable)Invoke("Sweep", new object[] { tick, now })!)
        .Cast<int>().ToList();

void Reset(int tick) => Invoke("ResetForTests", new object[] { tick });

int MailboxCount() => (int)cacheType
    .GetProperty("MailboxCount", BindingFlags.Static | BindingFlags.NonPublic)!
    .GetValue(null)!;

object Invoke(string methodName, object[] arguments) => cacheType
    .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, arguments)!;

void SetProperty(object target, string name, object value) => target.GetType()
    .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
    .SetValue(target, value);

Type RequiredType(string name) => gameAssembly.GetType(name, throwOnError: true)!;

static string FindRepositoryFile(params string[] parts)
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
    }
    throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
}

static void Require(string source, string value, string label)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(label + ": missing " + value);
}

static void Forbid(string source, string value, string label)
{
    if (source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(label + ": found " + value);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, got {actual}");
}
