using SystemModule;

namespace GameSvr.Services
{
    internal sealed class NativeMailCacheEntry
    {
        internal NativeMailRecord Record { get; }
        internal List<TUserItem> Attachments { get; }

        internal NativeMailCacheEntry(NativeMailRecord record, List<TUserItem> attachments)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            Attachments = attachments ?? new List<TUserItem>();
        }
    }

    internal readonly record struct NativeMailSummary(
        long RecipientId, string RecipientName, byte Tag, byte MailStatus, int Count);

    internal static class NativeMailCacheService
    {
        internal const int SweepIntervalMilliseconds = 180_000;
        internal const int InactiveMailboxSeconds = 259_200;
        internal const int DefaultRetentionDays = 7;
        internal const int DefaultMaximumMails = 30;
        internal const int SystemRetentionDays = 3;
        internal const int SystemMaximumMails = 20;

        private sealed class NativeMailbox
        {
            internal readonly List<NativeMailCacheEntry>[] Categories =
                Enumerable.Range(0, 6).Select(_ => new List<NativeMailCacheEntry>()).ToArray();
            internal readonly int[] UnreadCounts = new int[6];
            internal readonly int[] ReadCounts = new int[6];
            internal readonly bool[] UnreadLoaded = new bool[6];
            internal readonly bool[] ReadLoaded = new bool[6];

            internal string RecipientName;
            internal DateTime LastTouchUtc;

            internal NativeMailbox(string recipientName, DateTime nowUtc)
            {
                RecipientName = recipientName ?? string.Empty;
                LastTouchUtc = nowUtc;
            }
        }

        private static readonly object SyncRoot = new();
        private static readonly Dictionary<long, NativeMailbox> Mailboxes = new();
        private static int _lastSweepTick = Environment.TickCount;

        internal static int MailboxCount
        {
            get
            {
                lock (SyncRoot) return Mailboxes.Count;
            }
        }

        internal static bool InitializeFromStore(out string error)
        {
            if (!NativeMailStore.EnsureNativeSchema(out error)) return false;
            if (!NativeMailStore.TryLoadSummaries(out var summaries, out error)) return false;
            SeedSummaries(summaries, DateTime.UtcNow);
            return true;
        }

        internal static void ProcessScheduledSweep(DateTime now)
        {
            var deleteIds = Sweep(HUtil32.GetTickCount(), now.ToUniversalTime());
            foreach (var mailId in deleteIds)
                NativeMailStore.DeleteRowsBestEffort(mailId);
        }

        internal static void SeedSummaries(IEnumerable<NativeMailSummary> summaries,
            DateTime nowUtc)
        {
            if (summaries == null) return;

            lock (SyncRoot)
            {
                foreach (var summary in summaries)
                {
                    if (summary.RecipientId == 0 || !TryGetTagIndex(summary.Tag, out var index))
                        continue;
                    if (summary.MailStatus is not 1 and not 2) continue;

                    var mailbox = GetOrCreateLocked(
                        summary.RecipientId, summary.RecipientName, nowUtc);
                    if (summary.MailStatus == 1)
                        mailbox.UnreadCounts[index] = Math.Max(0, summary.Count);
                    else
                        mailbox.ReadCounts[index] = Math.Max(0, summary.Count);
                }
            }
        }

        internal static bool TouchExisting(long recipientId, string recipientName,
            DateTime nowUtc, out int[] unreadCounts)
        {
            unreadCounts = Array.Empty<int>();
            if (recipientId == 0) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                if (!string.IsNullOrEmpty(recipientName)) mailbox.RecipientName = recipientName;
                mailbox.LastTouchUtc = nowUtc;
                unreadCounts = (int[])mailbox.UnreadCounts.Clone();
                return true;
            }
        }

        internal static bool TryGetUnreadCounts(long recipientId, out int[] unreadCounts)
        {
            unreadCounts = Array.Empty<int>();
            if (recipientId == 0) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                unreadCounts = (int[])mailbox.UnreadCounts.Clone();
                return true;
            }
        }

        internal static bool ContainsMailbox(long recipientId)
        {
            if (recipientId == 0) return false;
            lock (SyncRoot) return Mailboxes.ContainsKey(recipientId);
        }

        internal static bool IsStatusLoaded(long recipientId, int tag, byte mailStatus)
        {
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)
                || mailStatus is not 1 and not 2)
                return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                return mailStatus == 1
                    ? mailbox.UnreadLoaded[index]
                    : mailbox.ReadLoaded[index];
            }
        }

        internal static bool TryGetCategory(long recipientId, int tag,
            out List<NativeMailCacheEntry> entries)
        {
            entries = null;
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)
                    || !mailbox.UnreadLoaded[index]
                    || !mailbox.ReadLoaded[index])
                    return false;

                entries = new List<NativeMailCacheEntry>(mailbox.Categories[index]);
                return true;
            }
        }

        internal static bool TryGetCachedCategory(long recipientId, int tag,
            out List<NativeMailCacheEntry> entries)
        {
            entries = null;
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                entries = new List<NativeMailCacheEntry>(mailbox.Categories[index]);
                return true;
            }
        }

        internal static List<NativeMailCacheEntry> MergeLoadedStatus(long recipientId,
            string recipientName, int tag, byte mailStatus,
            IEnumerable<NativeMailCacheEntry> loadedEntries, DateTime nowUtc)
        {
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)
                || mailStatus is not 1 and not 2)
                return new List<NativeMailCacheEntry>();

            lock (SyncRoot)
            {
                var mailbox = GetOrCreateLocked(recipientId, recipientName, nowUtc);
                var category = mailbox.Categories[index];
                var existingIds = category.Select(entry => entry.Record.Id).ToHashSet();
                foreach (var entry in loadedEntries ?? Enumerable.Empty<NativeMailCacheEntry>())
                {
                    if (entry == null || entry.Record.Id <= 0
                        || entry.Record.MailStatus != mailStatus
                        || !existingIds.Add(entry.Record.Id))
                        continue;
                    category.Add(entry);
                }

                SortCategory(category);
                ResetStatusCountLocked(mailbox, index, mailStatus);
                if (mailStatus == 1) mailbox.UnreadLoaded[index] = true;
                else mailbox.ReadLoaded[index] = true;
                return new List<NativeMailCacheEntry>(category);
            }
        }

        internal static NativeMailCacheEntry Register(long recipientId, string recipientName,
            NativeMailRecord record, List<TUserItem> attachments, DateTime nowUtc)
        {
            if (recipientId == 0 || record == null
                || !IsSupportedTag(record.MailType)
                || !TryGetTagIndex(record.MailType, out var index))
                return null;

            lock (SyncRoot)
            {
                var mailbox = GetOrCreateLocked(recipientId, recipientName, nowUtc);
                var category = mailbox.Categories[index];
                var existing = category.FirstOrDefault(entry => entry.Record.Id == record.Id);
                if (existing != null) return existing;

                var entry = new NativeMailCacheEntry(record, attachments);
                category.Insert(0, entry);
                IncrementCountLocked(mailbox, index, record.MailStatus);
                return entry;
            }
        }

        internal static bool TryFind(long recipientId, int tag, int mailId,
            out NativeMailCacheEntry entry)
        {
            entry = null;
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                entry = mailbox.Categories[index]
                    .FirstOrDefault(candidate => candidate.Record.Id == mailId);
                return entry != null;
            }
        }

        internal static bool MarkRead(long recipientId, int tag, int mailId)
        {
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                var entry = mailbox.Categories[index]
                    .FirstOrDefault(candidate => candidate.Record.Id == mailId);
                if (entry == null) return false;
                if (entry.Record.MailStatus == 2) return true;

                if (entry.Record.MailStatus == 1)
                    mailbox.UnreadCounts[index] = Math.Max(0, mailbox.UnreadCounts[index] - 1);
                entry.Record.MailStatus = 2;
                mailbox.ReadCounts[index]++;
                return true;
            }
        }

        internal static bool SetAttachStatus(long recipientId, int tag, int mailId, byte status)
        {
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                var entry = mailbox.Categories[index]
                    .FirstOrDefault(candidate => candidate.Record.Id == mailId);
                if (entry == null) return false;
                entry.Record.AttachStatus = status;
                return true;
            }
        }

        internal static bool TryRemove(long recipientId, int tag, int mailId,
            out NativeMailCacheEntry removed)
        {
            removed = null;
            if (recipientId == 0 || !TryGetTagIndex(tag, out var index)) return false;

            lock (SyncRoot)
            {
                if (!Mailboxes.TryGetValue(recipientId, out var mailbox)) return false;
                var category = mailbox.Categories[index];
                var entryIndex = category.FindIndex(candidate => candidate.Record.Id == mailId);
                if (entryIndex < 0) return false;

                removed = category[entryIndex];
                category.RemoveAt(entryIndex);
                DecrementCountLocked(mailbox, index, removed.Record.MailStatus);
                return true;
            }
        }

        internal static IReadOnlyList<int> Sweep(int currentTick, DateTime nowUtc)
        {
            var elapsed = unchecked((uint)(currentTick - _lastSweepTick));
            if (elapsed < SweepIntervalMilliseconds) return Array.Empty<int>();

            lock (SyncRoot)
            {
                elapsed = unchecked((uint)(currentTick - _lastSweepTick));
                if (elapsed < SweepIntervalMilliseconds) return Array.Empty<int>();
                _lastSweepTick = currentTick;

                var deleteIds = new List<int>();
                foreach (var mailbox in Mailboxes.Values)
                {
                    for (var index = 0; index < mailbox.Categories.Length; index++)
                        CleanupCategoryLocked(mailbox, index, nowUtc, deleteIds);

                    if ((nowUtc - mailbox.LastTouchUtc).TotalSeconds <= InactiveMailboxSeconds)
                        continue;

                    foreach (var category in mailbox.Categories) category.Clear();
                    Array.Clear(mailbox.UnreadLoaded, 0, mailbox.UnreadLoaded.Length);
                    Array.Clear(mailbox.ReadLoaded, 0, mailbox.ReadLoaded.Length);
                }
                return deleteIds;
            }
        }

        internal static void ResetForTests(int lastSweepTick = 0)
        {
            lock (SyncRoot)
            {
                Mailboxes.Clear();
                _lastSweepTick = lastSweepTick;
            }
        }

        private static NativeMailbox GetOrCreateLocked(long recipientId,
            string recipientName, DateTime nowUtc)
        {
            if (Mailboxes.TryGetValue(recipientId, out var mailbox))
            {
                if (!string.IsNullOrEmpty(recipientName)) mailbox.RecipientName = recipientName;
                return mailbox;
            }

            mailbox = new NativeMailbox(recipientName, nowUtc);
            Mailboxes.Add(recipientId, mailbox);
            return mailbox;
        }

        private static void CleanupCategoryLocked(NativeMailbox mailbox, int index,
            DateTime nowUtc, List<int> deleteIds)
        {
            var tag = index + 1;
            if (!IsSupportedTag(tag)) return;

            var category = mailbox.Categories[index];
            var retentionDays = tag == 6 ? SystemRetentionDays : DefaultRetentionDays;
            var maximumMails = tag == 6 ? SystemMaximumMails : DefaultMaximumMails;

            for (var i = category.Count - 1; i >= 0; i--)
            {
                var entry = category[i];
                if ((nowUtc - entry.Record.CreateDate.ToUniversalTime()).TotalDays < retentionDays
                    || !IsCleanupEligible(entry.Record))
                    continue;
                RemoveForSweepLocked(mailbox, index, i, deleteIds);
            }

            if (category.Count <= maximumMails) return;
            SortCategory(category);
            for (var i = category.Count - 1; i >= 0 && category.Count > maximumMails; i--)
            {
                if (!IsCleanupEligible(category[i].Record)) continue;
                RemoveForSweepLocked(mailbox, index, i, deleteIds);
            }
        }

        private static bool IsCleanupEligible(NativeMailRecord record) =>
            record != null
            && ((record.MailStatus == 2 && record.AttachStatus is 2 or 3)
                || record.MailType is 4 or 6);

        private static void RemoveForSweepLocked(NativeMailbox mailbox, int index,
            int entryIndex, List<int> deleteIds)
        {
            var entry = mailbox.Categories[index][entryIndex];
            mailbox.Categories[index].RemoveAt(entryIndex);
            DecrementCountLocked(mailbox, index, entry.Record.MailStatus);
            if (entry.Record.Id > 0) deleteIds.Add(entry.Record.Id);
        }

        private static void SortCategory(List<NativeMailCacheEntry> category)
        {
            category.Sort((left, right) =>
            {
                var status = left.Record.MailStatus.CompareTo(right.Record.MailStatus);
                if (status != 0) return status;
                var leftSecond = left.Record.CreateDate.Ticks / TimeSpan.TicksPerSecond;
                var rightSecond = right.Record.CreateDate.Ticks / TimeSpan.TicksPerSecond;
                return rightSecond.CompareTo(leftSecond);
            });
        }

        private static void ResetStatusCountLocked(NativeMailbox mailbox, int index,
            byte mailStatus)
        {
            if (mailStatus == 1) mailbox.UnreadCounts[index] = 0;
            else mailbox.ReadCounts[index] = 0;
            foreach (var entry in mailbox.Categories[index])
            {
                if (entry.Record.MailStatus == mailStatus)
                    IncrementCountLocked(mailbox, index, mailStatus);
            }
        }

        private static void IncrementCountLocked(NativeMailbox mailbox, int index,
            byte mailStatus)
        {
            if (mailStatus == 1) mailbox.UnreadCounts[index]++;
            else if (mailStatus == 2) mailbox.ReadCounts[index]++;
        }

        private static void DecrementCountLocked(NativeMailbox mailbox, int index,
            byte mailStatus)
        {
            if (mailStatus == 1)
                mailbox.UnreadCounts[index] = Math.Max(0, mailbox.UnreadCounts[index] - 1);
            else if (mailStatus == 2)
                mailbox.ReadCounts[index] = Math.Max(0, mailbox.ReadCounts[index] - 1);
        }

        private static bool TryGetTagIndex(int tag, out int index)
        {
            index = tag - 1;
            return index is >= 0 and < 6;
        }

        // sub_70DBCC @0x70DBCC is the whole gate:
        //   0x70DBCC 80 FA 07              cmp dl,7
        //   0x70DBCF 77 0A                 ja 0x70DBDB        ; >7 -> CF=0 -> reject
        //   0x70DBD1 83 E2 7F              and edx,0x7F
        //   0x70DBD4 0F A3 15 E8 3D 7D 00  bt dword [0x7D3DE8],edx
        //   0x70DBDB 0F 92 C0              setb al
        // dword_7D3DE8 reads 7E 8D 40 00, so bits 1..6 are set and 0/7 are clear. The
        // whole image contains exactly one reference to 0x7D3DE8 — the read encoded in
        // the bt at 0x70DBD7 — so nothing writes the mask and 0x7E is the final value.
        // Tags 2 (任务奖励) and 3 (离线补偿) are therefore live categories, named at
        // 0x7D3DEC[2]=0x708BAC and [3]=0x708BC0; rejecting them silently dropped both.
        private static bool IsSupportedTag(int tag) => tag is >= 1 and <= 6;
    }
}
