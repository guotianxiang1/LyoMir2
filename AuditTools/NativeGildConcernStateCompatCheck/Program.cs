using System;
using System.Collections.Generic;
using GameSvr;
using GameSvr.Services;

// Dormant-model compat check for NativeGildConcernState.cs — the DEFERRED gild social items:
//   concern set 4576/4578/4586, union-enable flag 4581, and the by-name resolver 4585/4586/4573.
// Every branch of every modeled ladder + every state transition is asserted. Evidence:
// staging/gild_deferred_items_20260801.md. No DB, no live state.
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
// 1. Concern SET (gild+44 TList): Contains / dedup add (sub_70675C) / remove (sub_70678C).
// ---------------------------------------------------------------------------
Equal(NativeGildConcernSet.NativeFieldOffset, 44, "concern list native offset gild+0x2C");

var set = new NativeGildConcernSet();
Equal(set.Count, 0, "concern set starts empty");
Equal(set.Contains(200), false, "empty set contains nothing");

Equal(set.TryAdd(200), NativeGildConcernAddOutcome.Added, "first add -> Added");
Equal(set.Count, 1, "count 1 after add");
Equal(set.Contains(200), true, "contains after add");
Equal(set.TryAdd(200), NativeGildConcernAddOutcome.AlreadyPresent,
    "dup add -> AlreadyPresent (sub_70675C true -> handler 1000)");
Equal(set.Count, 1, "dup add does not grow the list");

Equal(set.TryAdd(400), NativeGildConcernAddOutcome.Added, "second distinct add -> Added");
Equal(set.Count, 2, "count 2");

Equal(set.TryRemove(999), NativeGildConcernRemoveOutcome.NotPresent,
    "remove absent -> NotPresent (sub_70678C false -> handler 1000)");
Equal(set.TryRemove(200), NativeGildConcernRemoveOutcome.Removed,
    "remove present -> Removed (handler 0 + DELETE)");
Equal(set.Contains(200), false, "gone after remove");
Equal(set.Count, 1, "count 1 after remove");
Equal(set.TryRemove(200), NativeGildConcernRemoveOutcome.NotPresent, "second remove -> NotPresent");

var seeded = new NativeGildConcernSet();
seeded.SeedFromLoad(700);
seeded.SeedFromLoad(700); // idempotent
Equal(seeded.Count, 1, "SeedFromLoad idempotent");
Equal(seeded.Contains(700), true, "seeded membership");

// ---------------------------------------------------------------------------
// 2. Concern LADDERS (4576 add-id, 4586 add-name, 4578 cancel).
// ---------------------------------------------------------------------------
int Add(NativeGildConcernContext c) =>
    NativeGildConcernLadder.Evaluate(NativeGildConcernOp.AddConcernById, c);
int AddName(NativeGildConcernContext c) =>
    NativeGildConcernLadder.Evaluate(NativeGildConcernOp.AddConcernByName, c);
int Cancel(NativeGildConcernContext c) =>
    NativeGildConcernLadder.Evaluate(NativeGildConcernOp.CancelConcern, c);

// 4576 add-by-id ladder: 555 / 5 / 12 / 25 / 19 / 1000 / 0.
Equal(Add(new NativeGildConcernContext { Role = NativeGildRole.GildVice }),
    555, "4576 non-owner (vice) -> 555");
Equal(Add(new NativeGildConcernContext { Role = NativeGildRole.GildMember }),
    555, "4576 gild-member -> 555");
Equal(Add(new NativeGildConcernContext { Role = NativeGildRole.GildOwner, PlayerResolved = false }),
    5, "4576 no player -> 5");
Equal(Add(new NativeGildConcernContext { Role = NativeGildRole.GildOwner, HasGild = false }),
    12, "4576 no gild -> 12");
Equal(Add(new NativeGildConcernContext
    { Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = false }),
    25, "4576 target gild not found -> 25");
Equal(Add(new NativeGildConcernContext
    { Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = true, TargetIsSelf = true }),
    19, "4576 target == own gild -> 19 (self-concern, NOT a precheck)");
Equal(Add(new NativeGildConcernContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = true,
        TargetIsSelf = false, ConcernAlreadyPresent = true
    }),
    1000, "4576 duplicate -> 1000 (already present, NOT add-fail)");
Equal(Add(new NativeGildConcernContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = true,
        TargetIsSelf = false, ConcernAlreadyPresent = false
    }),
    0, "4576 success -> 0");

// 4586 add-by-name: name resolution (sub_5E76F0) precedes the role strategy; unresolved -> 12.
Equal(AddName(new NativeGildConcernContext
    { Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = true, NameResolved = false }),
    12, "4586 gild name unresolved -> 12 (before role gate)");
Equal(AddName(new NativeGildConcernContext
    { Role = NativeGildRole.NoCorps, NameResolved = false }),
    12, "4586 unresolved name beats role -> 12 even for non-owner");
Equal(AddName(new NativeGildConcernContext
    { Role = NativeGildRole.GildVice, NameResolved = true }),
    555, "4586 resolved name, non-owner -> 555");
Equal(AddName(new NativeGildConcernContext
    {
        Role = NativeGildRole.GildOwner, NameResolved = true, HasGild = true,
        TargetGildFound = true, TargetIsSelf = false, ConcernAlreadyPresent = false
    }),
    0, "4586 resolved name success -> 0");
Equal(AddName(new NativeGildConcernContext
    {
        Role = NativeGildRole.GildOwner, NameResolved = true, HasGild = true,
        TargetGildFound = true, TargetIsSelf = true
    }),
    19, "4586 resolved name, self -> 19");

// 4578 cancel ladder: 555 / 5 / 12 / 25 / 1000 / 0 (no self-19).
Equal(Cancel(new NativeGildConcernContext { Role = NativeGildRole.GildVice }),
    555, "4578 non-owner -> 555");
Equal(Cancel(new NativeGildConcernContext { Role = NativeGildRole.GildOwner, PlayerResolved = false }),
    5, "4578 no player -> 5");
Equal(Cancel(new NativeGildConcernContext { Role = NativeGildRole.GildOwner, HasGild = false }),
    12, "4578 no gild -> 12");
Equal(Cancel(new NativeGildConcernContext
    { Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = false }),
    25, "4578 target gild not found -> 25");
Equal(Cancel(new NativeGildConcernContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = true,
        ConcernPresentForRemove = false
    }),
    1000, "4578 not in concern set -> 1000");
Equal(Cancel(new NativeGildConcernContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = true,
        ConcernPresentForRemove = true
    }),
    0, "4578 success -> 0");
// Cancel has no self-19: a self target that is present still succeeds.
Equal(Cancel(new NativeGildConcernContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, TargetGildFound = true,
        TargetIsSelf = true, ConcernPresentForRemove = true
    }),
    0, "4578 ignores TargetIsSelf (no 19 in cancel)");

// SM reply ids + which side effect the success path enqueues.
Equal(NativeGildConcernLadder.ReplySmId(NativeGildConcernOp.AddConcernById), 4576, "4576 replies SM 4576");
Equal(NativeGildConcernLadder.ReplySmId(NativeGildConcernOp.AddConcernByName), 4576,
    "4586 also replies SM 4576");
Equal(NativeGildConcernLadder.ReplySmId(NativeGildConcernOp.CancelConcern), 4578, "4578 replies SM 4578");
Equal(NativeGildConcernLadder.EnqueuesInsert(NativeGildConcernOp.AddConcernById, 0), true,
    "4576 success enqueues INSERT");
Equal(NativeGildConcernLadder.EnqueuesInsert(NativeGildConcernOp.AddConcernById, 1000), false,
    "4576 failure enqueues nothing");
Equal(NativeGildConcernLadder.EnqueuesInsert(NativeGildConcernOp.CancelConcern, 0), false,
    "cancel never INSERTs");
Equal(NativeGildConcernLadder.EnqueuesDelete(NativeGildConcernOp.CancelConcern, 0), true,
    "4578 success enqueues DELETE");
Equal(NativeGildConcernLadder.EnqueuesDelete(NativeGildConcernOp.AddConcernByName, 0), false,
    "add never DELETEs");

// ---------------------------------------------------------------------------
// 3. Union-enable flag (4581): in-memory only (no column), write-when-changed.
// ---------------------------------------------------------------------------
Equal(NativeGildUnionFlagCell.NativeFieldOffset, 40, "union flag native offset gild+0x28");
Equal(NativeGildUnionFlagCell.HasPersistentColumn, false, "union flag has NO gamedata.Gild column");

// The walk below used to start from false, which contradicted the very first
// assertion. sub_704EAC is symmetric (0x704EDE cmp bl,[esi+0x28] / 0x704EE1 je
// skip / 0x704EE3 mov [esi+0x28],bl), so both NoChange cases and both Resave
// cases are still covered -- only the starting state moved to the native one.
var flag = new NativeGildUnionFlagCell();
Equal(flag.Enabled, true, "union flag defaults true (native 0x70633A: C6 47 28 01, constructor sets TRUE)");
Equal(flag.Set(true), NativeGildUnionFlagWrite.NoChange,
    "set true==current -> NoChange (0x704EE1 je 0x704F02 skips the UPDATE)");
Equal(flag.Set(false), NativeGildUnionFlagWrite.Resave, "set false (changed) -> Resave (standard UPDATE)");
Equal(flag.Enabled, false, "flag now disabled in memory");
Equal(flag.Set(false), NativeGildUnionFlagWrite.NoChange, "set false==current -> NoChange");
Equal(flag.Set(true), NativeGildUnionFlagWrite.Resave, "toggle back -> Resave");
Equal(flag.Enabled, true, "flag returns to the constructor default");

// 4581 ladder: owner OR vice reach the flag; others 555; 5 / 12 / 0.
Equal(NativeGildUnionFlagLadder.Evaluate(NativeGildRole.GildOwner, true, true), 0,
    "4581 owner reaches flag -> 0");
Equal(NativeGildUnionFlagLadder.Evaluate(NativeGildRole.GildVice, true, true), 0,
    "4581 vice ALSO reaches flag -> 0 (both +0x58 slots are sub_704EAC)");
Equal(NativeGildUnionFlagLadder.Evaluate(NativeGildRole.GildMember, true, true), 555,
    "4581 gild-member -> 555");
Equal(NativeGildUnionFlagLadder.Evaluate(NativeGildRole.Corps, true, true), 555,
    "4581 corps -> 555");
Equal(NativeGildUnionFlagLadder.Evaluate(NativeGildRole.GildOwner, false, true), 5,
    "4581 no player -> 5");
Equal(NativeGildUnionFlagLadder.Evaluate(NativeGildRole.GildOwner, true, false), 12,
    "4581 no gild -> 12");

// ---------------------------------------------------------------------------
// 4. By-name resolver (sub_5E76F0): gild-name -> gild id, case-insensitive, full registry.
// ---------------------------------------------------------------------------
Equal(NativeGildNameResolver.Normalize("abcXYZ"), "ABCXYZ", "Normalize uppercases ASCII a-z");
Equal(NativeGildNameResolver.Normalize("Gild_9!"), "GILD_9!", "Normalize leaves non-alpha untouched");
Equal(NativeGildNameResolver.Normalize(""), "", "Normalize empty");

var registry = new Dictionary<string, long>
{
    ["DRAGON"] = 200,   // stored under the uppercased key
    ["PHOENIX"] = 400,
};
var resolver = new NativeGildNameResolver(registry);

Equal(resolver.TryResolve("dragon", out var g1), true, "resolve 'dragon' (case-insensitive)");
Equal(g1, 200L, "resolved 'dragon' -> gild 200");
Equal(resolver.TryResolve("Phoenix", out var g2), true, "resolve 'Phoenix'");
Equal(g2, 400L, "resolved 'Phoenix' -> gild 400");
Equal(resolver.TryResolve("nosuchgild", out var g3), false,
    "unknown gild name -> false (handler code 12)");
Equal(g3, 0L, "unresolved id is 0");
Equal(resolver.TryResolve(null, out _), false, "null name -> false");

Console.WriteLine(
    $"PASS NativeGildConcernStateCompatCheck: {checks} checks " +
    "(concern 4576/4578/4586 state+ladder, union-flag 4581 in-memory-no-column, " +
    "by-name resolver 4585/4586 gild-name->gild)");
return 0;
