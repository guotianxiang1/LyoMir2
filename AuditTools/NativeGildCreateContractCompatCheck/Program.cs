using System;
using System.Collections.Generic;
using GameSvr.Services;

// Dormant-model compat check for NativeGildCreateContract.cs — the 4564 create-gild contract:
//   the eligibility ladder (sub_702F8C), the confirmed no-gold / no-name-validity facts, the store
//   write plan, and the GildID allocation scheme (sub_5E665C). Evidence:
//   staging/gild_create_4564_20260801.md. No DB, no live state.
//
// Single generic assertion helper (no overloaded local Equal).

int checks = 0;

void Equal<T>(T actual, T expected, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.Error.WriteLine($"FAIL: {label}: expected <{expected}>, got <{actual}>");
        Environment.Exit(1);
    }
}

// ---------------------------------------------------------------------------
// 1. Eligibility ladder (sub_702F8C): role 555 / player 4 / corps 5 / in-gild 6 / dup 2 / 0.
// ---------------------------------------------------------------------------
int Eval(NativeSelfSocialRole role, bool player, bool corps, bool inGild, bool dup) =>
    NativeGildCreateContract.Evaluate(role, player, corps, inGild, dup);

// Role gate: only corps_owner / gild_vice / gild_owner reach the create body; others -> 555.
Equal(NativeGildCreateContract.CanCreateGild(NativeSelfSocialRole.CorpsOwner), true, "corps owner can create");
Equal(NativeGildCreateContract.CanCreateGild(NativeSelfSocialRole.GildViceOwner), true, "gild vice can create");
Equal(NativeGildCreateContract.CanCreateGild(NativeSelfSocialRole.GildOwner), true, "gild owner can create");
Equal(NativeGildCreateContract.CanCreateGild(NativeSelfSocialRole.CorpsViceOwner), false, "corps vice cannot");
Equal(NativeGildCreateContract.CanCreateGild(NativeSelfSocialRole.Member), false, "member cannot");
Equal(NativeGildCreateContract.CanCreateGild(NativeSelfSocialRole.NoCorps), false, "no-corps cannot");

Equal(Eval(NativeSelfSocialRole.Member, true, true, false, false), 555, "member -> 555 (role stub)");
Equal(Eval(NativeSelfSocialRole.CorpsViceOwner, true, true, false, false), 555, "corps vice -> 555");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, false, true, false, false), 4, "no player -> 4");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, true, false, false, false), 5, "no corps -> 5");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, true, true, true, false), 6, "corps already in gild -> 6");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, true, true, false, true), 2, "duplicate name -> 2");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, true, true, false, false), 0, "corps owner all-clear -> 0");
// An existing gild owner attempting to create hits already-in-gild (6), not a fresh success.
Equal(Eval(NativeSelfSocialRole.GildOwner, true, true, true, false), 6, "gild owner re-create -> 6");

// Order proof: role beats player, player beats corps, corps beats in-gild, in-gild beats dup.
Equal(Eval(NativeSelfSocialRole.Member, false, false, true, true), 555, "role gate precedes all");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, false, false, true, true), 4, "no-player precedes corps");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, true, false, true, true), 5, "no-corps precedes in-gild");
Equal(Eval(NativeSelfSocialRole.CorpsOwner, true, true, true, true), 6, "in-gild precedes dup");

// ---------------------------------------------------------------------------
// 2. Contract facts: no gold gate, no name-validity gate, store plan, reply ids.
// ---------------------------------------------------------------------------
Equal(NativeGildCreateContract.CmIdent, 4564, "CM ident 4564");
Equal(NativeGildCreateContract.SmReplyIdent, 4564, "SM reply 4564");
Equal(NativeGildCreateContract.HasGoldGate, false, "gild create has NO gold gate (free)");
Equal(NativeGildCreateContract.HasNameValidityGate, false, "gild create has NO name-validity gate");
Equal(NativeGildCreateContract.CreateViceOwnerId, 0L, "new gild ViceOwnerID = 0");
Equal(NativeGildCreateContract.RollsBackOnSqlFailure, false, "no rollback on SQL failure");
Equal(NativeGildCreateContract.EnqueuesCreateWrites(0), true, "success enqueues the two INSERTs");
Equal(NativeGildCreateContract.EnqueuesCreateWrites(6), false, "failure enqueues nothing");

// Store write order: gildmember first, then the Gild row (matches the state machine's enqueue order).
Equal(NativeGildCreateContract.SuccessWriteOrder.Count, 2, "two success writes");
Equal(NativeGildCreateContract.SuccessWriteOrder[0], NativeGildCreateWrite.InsertGildMember,
    "first write = INSERT gildmember");
Equal(NativeGildCreateContract.SuccessWriteOrder[1], NativeGildCreateWrite.InsertGild,
    "second write = INSERT Gild");

// ---------------------------------------------------------------------------
// 3. GildID allocator (sub_5E665C): composite byte layout + sequence / overflow behavior.
// ---------------------------------------------------------------------------
Equal(NativeGildIdAllocator.EpochYear, 2015, "epoch year 2015");
Equal(NativeGildIdAllocator.EpochMonth, 12, "epoch month 12");
Equal(NativeGildIdAllocator.EpochDay, 30, "epoch day 30");
Equal(NativeGildIdAllocator.TickAdvanceSleepMs, 20, "0xFF overflow sleeps 20ms");

// Byte layout: bytes 0-3 timeLow32, byte 4 timeByte4, byte 5 sequence, bytes 6-7 serverId.
Equal(NativeGildIdAllocator.Compose(0x11223344u, 0x55, 0x66, 0x7788),
    unchecked((long)0x7788_66_55_11223344UL), "Compose packs [serverId|seq|byte4|timeLow32]");
Equal(NativeGildIdAllocator.Compose(0u, 0, 0, 0), 0L, "Compose zero");

var alloc = new NativeGildIdAllocator();
Equal(alloc.Sequence, (byte)0, "sequence starts 0");

var id0 = alloc.Allocate(0x1000u, 0x00, 0x0001, out var adv0);
Equal(adv0, false, "first allocate no tick advance");
Equal(id0, unchecked((long)0x0001_00_00_00001000UL), "id0 uses sequence 0");
Equal(alloc.Sequence, (byte)1, "sequence incremented to 1");

var id1 = alloc.Allocate(0x1000u, 0x00, 0x0001, out _);
Equal(id1, unchecked((long)0x0001_01_00_00001000UL), "id1 uses sequence 1");
Equal(alloc.Sequence, (byte)2, "sequence incremented to 2");

// Drive the sequence to 0xFF and prove the overflow advances the tick and resets to 0.
var overflowAlloc = new NativeGildIdAllocator();
for (var i = 0; i < 0xFF; i++) overflowAlloc.Allocate(0x2000u, 0, 0x0009, out _);
Equal(overflowAlloc.Sequence, (byte)0xFF, "sequence reached 0xFF");
var idOverflow = overflowAlloc.Allocate(0x2000u, 0, 0x0009, out var advOverflow);
Equal(advOverflow, true, "0xFF triggers tick advance");
Equal(idOverflow, unchecked((long)0x0009_00_00_00002000UL), "post-overflow id uses reset sequence 0");
Equal(overflowAlloc.Sequence, (byte)1, "sequence continues at 1 after reset");

// ---------------------------------------------------------------------------
// 4. Wire target: the store PATH (INativeGildStore), NOT the legacy write queue.
// ---------------------------------------------------------------------------
Equal(NativeGildCreateStorePlan.UsesLegacyWriteQueue, false,
    "4564 wires to INativeGildStore, NOT the legacy queue");
Equal(NativeGildCreateStorePlan.ViceOwnerIdOnCreate, 0L, "store INSERT sets ViceOwnerID = 0");
Equal(NativeGildCreateStorePlan.StoreMethodFor(NativeGildCreateWrite.InsertGildMember),
    "TryInsertGildMember", "gildmember write -> TryInsertGildMember");
Equal(NativeGildCreateStorePlan.StoreMethodFor(NativeGildCreateWrite.InsertGild),
    "TryCreateGild", "gild write -> TryCreateGild");

Console.WriteLine(
    $"PASS NativeGildCreateContractCompatCheck: {checks} checks " +
    "(4564 ladder 555/4/5/6/2/0, no-gold/no-name-gate, 2-INSERT store plan, composite GildID allocator)");
return 0;
