using System.Collections.Generic;

namespace GameSvr.Services
{
    // Dormant model of the 4564 create-gild (建会) contract — the last gild write op. Address-level
    // evidence: staging/gild_create_4564_20260801.md. Image base 0x00400000. NEW file; not wired; no writes.
    //
    // Complements the existing legacy self-corps/gild CreateSelfGild state-machine model (which already models
    // the 555/4/5/6/2/0 ladder with AllocateGild abstracted) by adding the two pieces gild-wiring flagged
    // as unreversed: the GildID allocation scheme (sub_5E665C) and the confirmed contract facts
    // (no gold gate, no name-validity gate, store write plan). Reuses GameSvr.Services.NativeSelfSocialRole.

    // ------------------------------------------------------------------------------------------------
    // 1. GildID allocation — sub_5E665C (the shared corps/gild ID generator).
    //    NOT max+1 and NOT a DB counter: a process-local composite 64-bit id
    //    [ serverId(16) | sequence(8) | scaled-timestamp(40) ], epoch 2015-12-30.
    //    The scaled timestamp (sub_403574 over Now-epoch) and the server/config word (sub_5E6650) are
    //    abstracted as inputs — like the state machine abstracts AllocateGild — while the BYTE LAYOUT and
    //    the sequence/overflow behavior are modeled exactly.
    // ------------------------------------------------------------------------------------------------

    public sealed class NativeGildIdAllocator
    {
        public const int EpochYear = 2015;   // sub_49E514 ax=0x7DF
        public const int EpochMonth = 12;    // dx=0x0C
        public const int EpochDay = 30;      // cx=0x1E
        public const byte SequenceOverflow = 0xFF; // at 0xFF the native sleeps to advance the tick, resets
        public const int TickAdvanceSleepMs = 20;  // sub_4142F8(20)

        private byte _sequence;

        public byte Sequence => _sequence;

        /// <summary>
        /// Packs the 64-bit GildID exactly as sub_5E665C lays it out:
        /// bytes 0-3 = timeLow32, byte 4 = timeByte4, byte 5 = sequence, bytes 6-7 = serverId.
        /// </summary>
        public static long Compose(uint timeLow32, byte timeByte4, byte sequence, ushort serverId)
        {
            return (long)(((ulong)serverId << 48)
                          | ((ulong)sequence << 40)
                          | ((ulong)timeByte4 << 32)
                          | timeLow32);
        }

        /// <summary>
        /// Mirrors sub_5E665C's sequence handling: if the stored sequence byte is 0xFF the native sleeps
        /// 20ms to advance the timestamp and resets the sequence to 0 (tickAdvanced), then emits the id
        /// with the current sequence and increments the stored sequence for the next call. The time and
        /// server-id inputs stand in for sub_403574 / sub_5E6650.
        /// </summary>
        public long Allocate(uint timeLow32, byte timeByte4, ushort serverId, out bool tickAdvanced)
        {
            tickAdvanced = false;
            if (_sequence == SequenceOverflow)
            {
                tickAdvanced = true; // native Sleep(20) + reset so the timestamp moves and ids stay unique
                _sequence = 0;
            }

            var id = Compose(timeLow32, timeByte4, _sequence, serverId);
            _sequence = (byte)(_sequence + 1);
            return id;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // 2. Create-gild contract facts + eligibility ladder (strategy[0x3C] = sub_702F8C).
    // ------------------------------------------------------------------------------------------------

    public enum NativeGildCreateWrite
    {
        InsertGildMember, // sub_5E95E0(off_5E6C00): INSERT gamedata.gildmember(GildID, CorpsID)
        InsertGild,       // sub_5E926C(off_5E6AD4): INSERT gamedata.Gild(ID, now(), name, OwnerCorpsID, ViceOwnerID)
    }

    public static class NativeGildCreateContract
    {
        public const int CmIdent = 4564;               // 0x11D4 CM_GILD_CREATE / CreateSelfGild
        public const int SmReplyIdent = 4564;          // reply via vtable[+0x250]
        public const int HandlerAddress = 0x006ADDA8;  // sub_6ADDA8 (no wrapper gate)
        public const int StrategySlot = 0x3C;          // role strategy[+0x3C]
        public const int CreateStrategyAddress = 0x00702F8C; // sub_702F8C (the real create body)
        public const int AddGildAddress = 0x005E752C;  // sub_5E752C (dup-name check + allocate + inserts)
        public const int IdAllocatorAddress = 0x005E665C; // sub_5E665C

        // KEY negative findings for gild create (confirmed absent in sub_702F8C):
        public const bool HasGoldGate = false;         // gild creation is FREE (unlike declare-war's 30000)
        public const bool HasNameValidityGate = false; // no charset/length check (corps has one; gild does not)

        // Success enqueues exactly these two independent fire-and-forget writes, in this order.
        public static readonly IReadOnlyList<NativeGildCreateWrite> SuccessWriteOrder =
            new[] { NativeGildCreateWrite.InsertGildMember, NativeGildCreateWrite.InsertGild };

        public const long CreateViceOwnerId = 0; // a brand-new gild has no vice owner
        public const bool RollsBackOnSqlFailure = false;

        // Result-code ladder (sub_702F8C + AddGild). Role gate 555 comes from the non-create-capable
        // roles' [+0x3C] being a `return 555` stub; the create body itself yields 4/5/6, AddGild yields 2/0.
        public const int RoleDenied = 555;    // role not corps_owner/gild_vice/gild_owner
        public const int NoPlayer = 4;        // sub_656C14 == 0 (caller not resolved)
        public const int NoCorps = 5;         // player[+2792] == 0 (no corps)
        public const int AlreadyInGild = 6;   // corps[+4] != 0 (corps already belongs to a gild)
        public const int DuplicateName = 2;   // AddGild: gild name already in the registry
        public const int Success = 0;

        /// <summary>Only a corps owner (or an existing gild owner/vice) reaches sub_702F8C; every other
        /// role's [+0x3C] slot is a 555 stub.</summary>
        public static bool CanCreateGild(NativeSelfSocialRole role) =>
            role is NativeSelfSocialRole.CorpsOwner
                or NativeSelfSocialRole.GildViceOwner
                or NativeSelfSocialRole.GildOwner;

        /// <summary>
        /// The exact create ladder in native order: role -> player -> corps -> already-in-gild ->
        /// duplicate-name -> success. Confirms the legacy CreateSelfGild state-machine model.
        /// </summary>
        public static int Evaluate(NativeSelfSocialRole role, bool playerResolved, bool hasCorps,
            bool corpsAlreadyInGild, bool gildNameExists)
        {
            if (!CanCreateGild(role)) return RoleDenied;
            if (!playerResolved) return NoPlayer;
            if (!hasCorps) return NoCorps;
            if (corpsAlreadyInGild) return AlreadyInGild;
            if (gildNameExists) return DuplicateName;
            return Success;
        }

        /// <summary>True when the result triggers the two INSERTs (success only).</summary>
        public static bool EnqueuesCreateWrites(int result) => result == Success;
    }

    /// <summary>
    /// WIRE TARGET (gild-wiring prereq): the two success writes map to the EXISTING NativeGildMySqlStore
    /// (gap-B, INativeGildStore) — NOT the legacy INativeSelfCorpsGildLegacyWriteQueue that
    /// the legacy self-corps/gild create state machine routes to (that model is not the wire target as-is). The
    /// single GildID from NativeGildIdAllocator is shared by the in-memory registry entry AND both
    /// INSERTs, so the registry and gamedata.Gild agree by construction.
    /// </summary>
    public static class NativeGildCreateStorePlan
    {
        // In enqueue order (matches NativeGildCreateContract.SuccessWriteOrder):
        //   InsertGildMember -> INativeGildStore.TryInsertGildMember(gildId, ownerCorpsId)
        //   InsertGild       -> INativeGildStore.TryCreateGild(gildId, gildName, ownerCorpsId, viceOwnerId: 0)
        public const string GildMemberStoreMethod = "TryInsertGildMember";
        public const string GildStoreMethod = "TryCreateGild";
        public const long ViceOwnerIdOnCreate = 0;
        public const bool UsesLegacyWriteQueue = false; // gated INativeGildStore + fail-safe (no rollback)

        public static string StoreMethodFor(NativeGildCreateWrite write) => write switch
        {
            NativeGildCreateWrite.InsertGildMember => GildMemberStoreMethod,
            NativeGildCreateWrite.InsertGild => GildStoreMethod,
            _ => null,
        };
    }
}
