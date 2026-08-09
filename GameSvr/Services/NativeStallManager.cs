using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Executable in-memory STALL (摆摊) manager — the owner→record container that turns the descriptor-only
    /// <see cref="NativeStallManagerModel"/> into live state (task #83). The original keys an open hash by the
    /// owner char name (sub_49F2E4 probe; sub_49F5F4 returns entry[+0x14] = the record, 0 = "no active
    /// stall"); the faithful C# equivalent is a name-keyed dictionary. New records are constructed per the
    /// ctor sub_61ED04 (status Initial, level 1, pos -1, createDate = now).
    ///
    /// SCOPE (team-lead directive): this ONLY holds / creates / finds / removes records and hosts the
    /// recovery populate hook. It moves NO money and NO items — the write EXECUTORS (start / add / del / buy /
    /// pause) and the crash-recovery flag replay stay STUBBED / fail-closed until codec-fidelity confirms each
    /// leaf (sub_61A4C0 open, sub_61E0C8 buy-finalize, sub_61A36C/sub_61FEAC pause-close, sub_620F58 order).
    /// Do NOT guess money/item movement here. Until the executors land, the live router keeps returning null
    /// context → RejectUnavailableStallRequest (guards green).
    /// </summary>
    public sealed class NativeStallManager
    {
        private readonly Dictionary<string, NativeStallRecord> _byOwner =
            new(StringComparer.Ordinal);
        // Parallel CharID index. The native manager keys the record by the owner's 64-bit CharID
        // (sub_40C988(lo,hi) primes the key; sub_49F5F4 resolves), which is the identity the BUY/Query/Message
        // wire carries — NOT a name. This index resolves those owner-CharID lookups in O(1); kept in sync with
        // _byOwner on every mutator. OwnerId is set once at construction and never changes.
        private readonly Dictionary<long, NativeStallRecord> _byOwnerId = new();
        private readonly object _sync = new();

        /// <summary>Active booth count (startup log / diagnostics).</summary>
        public int Count
        {
            get { lock (_sync) { return _byOwner.Count; } }
        }

        /// <summary>sub_49F5F4: the owner's active stall record, or null when they have no stall.</summary>
        public NativeStallRecord TryGetRecord(string ownerName)
        {
            if (string.IsNullOrEmpty(ownerName)) return null;
            lock (_sync)
            {
                return _byOwner.TryGetValue(ownerName, out var record) ? record : null;
            }
        }

        /// <summary>
        /// sub_40C988(lo,hi)+sub_49F5F4 by 64-bit CharID: the target owner's active stall record, or null.
        /// This is the resolver for the BUY/Query/Message wire, whose target is the owner CharID (body[0]/
        /// body[4]) — never a name. Returns null for id 0 (no owner).
        /// </summary>
        public NativeStallRecord TryGetRecordById(long ownerId)
        {
            if (ownerId == 0) return null;
            lock (_sync)
            {
                return _byOwnerId.TryGetValue(ownerId, out var record) ? record : null;
            }
        }

        /// <summary>
        /// Resolve or construct the owner's stall record with the native ctor defaults (sub_61ED04).
        /// Construction only establishes the in-memory record; OPENING it for business (status → Running)
        /// and the INSERT persist is the START executor, which is still stubbed.
        /// </summary>
        public NativeStallRecord GetOrCreate(string ownerName, long ownerId)
        {
            if (string.IsNullOrEmpty(ownerName))
                throw new ArgumentException("owner name required", nameof(ownerName));
            lock (_sync)
            {
                if (_byOwner.TryGetValue(ownerName, out var existing))
                    return existing;
                var now = DateTime.Now;
                var record = new NativeStallRecord
                {
                    OwnerId = ownerId,
                    OwnerName = ownerName,
                    Status = StallRecordStatus.Initial,   // rec+0x40 == 0
                    Level = 1,                             // rec+0x30
                    PosX = -1,                             // rec+0x44
                    PosY = -1,                             // rec+0x48
                    CreateDate = now,                      // rec+0x20
                    ModifyDate = now,                      // rec+0x28
                };
                _byOwner[ownerName] = record;
                if (ownerId != 0) _byOwnerId[ownerId] = record;
                return record;
            }
        }

        /// <summary>Drop the owner's record once the booth is fully closed / expired.</summary>
        public bool Remove(string ownerName)
        {
            if (string.IsNullOrEmpty(ownerName)) return false;
            lock (_sync)
            {
                if (!_byOwner.TryGetValue(ownerName, out var record))
                    return false;
                _byOwner.Remove(ownerName);
                if (record.OwnerId != 0) _byOwnerId.Remove(record.OwnerId);
                return true;
            }
        }

        /// <summary>
        /// Recovery-scan populate hook: register a record rebuilt from the DB at startup. The original pages
        /// active booths (SELECT … isEnabled=1 … ORDER BY idx LIMIT 1000) and re-hydrates their items from the
        /// srvData BLOB. The DB read + the idempotent crash-recovery flag replay (isSold → IsGetMoney →
        /// IsBoSended) belong to the recovery executor (still stubbed); this only inserts an already-built
        /// record into the map.
        /// </summary>
        public void Register(NativeStallRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.OwnerName)) return;
            lock (_sync)
            {
                _byOwner[record.OwnerName] = record;
                if (record.OwnerId != 0) _byOwnerId[record.OwnerId] = record;
            }
        }

        /// <summary>Snapshot of all active records (expire tick / diagnostics; no live mutation exposed).</summary>
        public IReadOnlyList<NativeStallRecord> Snapshot()
        {
            lock (_sync)
            {
                return new List<NativeStallRecord>(_byOwner.Values);
            }
        }
    }

    /// <summary>
    /// Dormant injection point for the executable manager (mirrors <see cref="NativeStallWriteGate"/>).
    /// Stays null until the executors are confirmed and the subsystem goes live — the FINAL step of #83,
    /// alongside injecting the store and flipping <see cref="NativeStallWriteGate.SupportsStallWrites"/>.
    /// </summary>
    public static class NativeStallManagerHost
    {
        public static NativeStallManager Manager { get; set; }
    }
}
