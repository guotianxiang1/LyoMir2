// NativeColdTimeRuntimeCheck -- the coldTime RUNTIME layer (arm / query / tick /
// notify) against 战神 sub_748130 / sub_748288 / sub_7482E0 / sub_748200 /
// sub_748338 / sub_74839C.
//
// Anti-false-green discipline: every bound below is written as a literal derived
// from the DISASSEMBLY, and the production value is read back by reflection, so
// the audit cannot mirror a wrong constant. Three specific things this must
// catch, because a written spec of this feature got all three wrong:
//   1. THeroAct VMT+0x254 is sub_689A38 (a FORWARDER to the master), not a bare
//      stub -- only THumanKind's own slot (sub_73C968) is the `ret 0x14` stub.
//      So heroes DO notify, via their master's socket.
//   2. The bulk form sends one packet PER ELEMENT (the send at 0x748468 is
//      inside the loop whose back edge is 0x748472), not one packet total.
//   3. Arming an ABSENT key with Remaining == 0 emits no packet at all
//      (0x748184 `je 0x7481E8` skips the notify).
using System.Collections;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

var assertions = 0;

CheckOwnership();
CheckNodeShapeAndConstants();
CheckArmUpsert();
CheckArmZeroRemainingIsSilentNoOp();
CheckArmTotalRules();
CheckQueryAndTotalGetter();
CheckTickGate();
CheckTickDecrementsByMeasuredElapsed();
CheckExpiryIsLessOrEqualZero();
CheckBackwardWalkDeletesAll();
CheckBulkExpire();
CheckIdentSelection();
CheckSinglePayloadShape();
CheckBulkPayloadIsPerElement();
CheckTotalIsNeverOnTheWire();
CheckCodecSharesOneStore();
CheckRunHookPlacement();

Console.WriteLine($"NativeColdTimeRuntimeCheck PASS ({assertions} assertions)");
Console.WriteLine(
    "  table obj+0x504 TList, node 12B {Key,Remaining,Total} @+0/+4/+8");
Console.WriteLine(
    "  ARM sub_748130 (VMT+0x1F0), QUERY sub_748288 (VMT+0x1F4), " +
    "TOTAL sub_7482E0, TICK sub_748200 @0x73C245, NOTIFY sub_74839C");
Console.WriteLine(
    "  tick gate STRICT >250ms (0x748216 cmp 0xFA + jle), decrement by " +
    "measured elapsed (0x748246 sub [eax+4],edi)");
Console.WriteLine(
    "  hero transport = sub_689A38 FORWARDER via obj+0x68C, not a stub " +
    "(THumanKind's own 0x254 = sub_73C968 ret 0x14)");
return;

// ---------------------------------------------------------------------------

void CheckOwnership()
{
    // The table is created in the THumanKind ctor at 0x73BFF2..0x73BFFC, so
    // every descendant has it. TCreature does NOT: VMT 0x764608+0x1F0 holds
    // sub_773CA0, a different function, so monsters have no cooldown table.
    Assert(NewPlayer().SupportsNativeColdTime,
        "TPlayer must own a coldTime table (VMT 0x6AC8C8+0x1F0 = sub_748130)");
    Assert(NewHero().SupportsNativeColdTime,
        "THeroAct must own a coldTime table (VMT 0x685630+0x1F0 = sub_748130)");
    Assert(!new TBaseObject().SupportsNativeColdTime,
        "plain TBaseObject must NOT own a table (TCreature+0x1F0 = sub_773CA0)");

    // A non-owner must be inert on every entry point rather than throwing.
    var creature = new TBaseObject();
    Assert(!creature.ArmNativeColdTime(0x111, 1000, 1000),
        "a non-owner must refuse to arm");
    Equal(0, creature.QueryNativeColdTime(0x111),
        "a non-owner must query as absent");
    creature.ProcessNativeColdTimes(int.MaxValue);
    Equal(0, Entries(creature).Count, "a non-owner must stay empty");
}

void CheckNodeShapeAndConstants()
{
    // 12 bytes, three dwords: 0x748186 `mov eax,0xC` -> GetMem, freed as 12 at
    // 0x748262 and 0x73C0E7. Writes at 0x7481A7 (+0), 0x7481AF (+4), 0x7481C6
    // (+8) prove the order.
    var entry = typeof(TBaseObject).GetNestedType("NativeColdTimeEntry");
    Assert(entry != null, "NativeColdTimeEntry must live on TBaseObject");
    Assert(!entry.IsValueType,
        "the node must be a reference type: the tick mutates Remaining in " +
        "place (0x748246 sub [eax+4],edi) without reseating it in the list");
    Equal(typeof(uint), entry.GetField("Key").FieldType, "Key width");
    Equal(typeof(int), entry.GetField("Remaining").FieldType,
        "Remaining is signed: expiry tests `jg` at 0x748250 and callers use " +
        "`test eax,eax / jle`, so it must be able to go negative");
    Equal(typeof(int), entry.GetField("Total").FieldType, "Total width");

    // Derived from the bytes, then compared against production by reflection.
    const int nativeTickGate = 0xFA;         // 0x748216 cmp edi,0xFA
    const int nativePlayerIdent = 0xDE4;     // 0x7483C6
    const int nativeNonPlayerIdent = 0x110F; // 0x7483CF
    const int nativeWireElement = 8;         // 0x74845A shl eax,3 / 0x748488 push 8
    Equal(nativeTickGate, Const<int>("NativeColdTimeTickIntervalMilliseconds"),
        "tick gate must be 250 (0x748216)");
    Equal(nativePlayerIdent, Const<int>("NativeColdTimePlayerIdent"),
        "player ident must be 3556 (0x7483C6)");
    Equal(nativeNonPlayerIdent, Const<int>("NativeColdTimeNonPlayerIdent"),
        "non-player ident must be 4367 (0x7483CF)");
    Equal(nativeWireElement, Const<int>("NativeColdTimeWireElementSize"),
        "wire element must be 8 bytes (0x74845A / 0x748488)");
    NotEqual(nativePlayerIdent, nativeNonPlayerIdent,
        "the two idents must stay distinct");

    // The 12-byte STORED node and the 8-byte WIRE element are different sizes;
    // conflating them is how Total leaks onto the wire.
    NotEqual(nativeWireElement,
        Const<int>("NativeColdTimeElementSize", typeof(TPlayObject)),
        "stored node (12B, 0x6E4C9C) must differ from wire element (8B)");
}

void CheckArmUpsert()
{
    // 0x748146..0x748178: linear scan, `je 0x74817A` at 0x74816F breaks out on
    // the FIRST match. A second arm of the same key must REUSE the node, not
    // append a duplicate, and must keep its position.
    var player = NewPlayer();
    Assert(player.ArmNativeColdTime(0x111, 180000, 180000), "first arm");
    Assert(player.ArmNativeColdTime(0x98, 30000, 30000), "second key");
    Equal(2, Entries(player).Count, "two distinct keys must make two nodes");

    Assert(player.ArmNativeColdTime(0x111, 90000, 90000), "re-arm key 0x111");
    Equal(2, Entries(player).Count,
        "re-arming an existing key must upsert, not append (0x74816F je)");
    Equal(0x111u, Entries(player)[0].Key,
        "a reused node must keep its list position");
    Equal(90000, Entries(player)[0].Remaining, "re-arm must overwrite Remaining");

    // First match wins on read-back too (0x7482CF jmp out).
    Equal(90000, player.QueryNativeColdTime(0x111), "query after re-arm");
    Equal(30000, player.QueryNativeColdTime(0x98), "unrelated key untouched");
    Equal(0, player.QueryNativeColdTime(0x112),
        "absent key must read 0 (0x748296 xor eax,eax)");
}

void CheckArmZeroRemainingIsSilentNoOp()
{
    // 0x748180 `cmp [ebp-8],0` / 0x748184 `je 0x7481E8` -- jumps PAST the
    // notify at 0x7481E3. So arming an absent key with Remaining==0 allocates
    // nothing AND sends nothing. A port that "helpfully" notifies here diverges.
    var player = NewPlayer();
    ClearSent(player);
    Assert(!player.ArmNativeColdTime(0x111, 0, 5000),
        "arming an absent key with Remaining==0 must be a no-op");
    Equal(0, Entries(player).Count, "no node may be allocated");
    Equal(0, SentCount(player),
        "no packet may be sent (0x748184 je skips the notify at 0x7481E3)");

    // But an EXISTING key armed to 0 does reach the notify, because the `je` is
    // only on the not-found path.
    Assert(player.ArmNativeColdTime(0x111, 5000, 5000), "seed the key");
    ClearSent(player);
    Assert(player.ArmNativeColdTime(0x111, 0, 0),
        "arming an EXISTING key with Remaining==0 must still notify");
    Equal(1, SentCount(player), "the found path falls through to 0x7481E3");
    Equal(1, Entries(player).Count,
        "arm never deletes: only the tick removes nodes (0x748277)");
}

void CheckArmTotalRules()
{
    // Total != 0 -> written unconditionally (0x7481CB path).
    var a = NewPlayer();
    a.ArmNativeColdTime(0x111, 1000, 7777);
    Equal(7777, Entries(a)[0].Total, "explicit Total must be stored (0x7481D1)");

    // Total == 0 -> written only when Remaining > Total, and since Total is 0 on
    // that path the guard is effectively Remaining > 0 (0x7481B8..0x7481BE).
    var b = NewPlayer();
    b.ArmNativeColdTime(0x111, 4321, 0);
    Equal(4321, Entries(b)[0].Total,
        "Total==0 with positive Remaining must copy Remaining (0x7481C6)");

    // Reused node, Total==0, Remaining <= 0: Total must KEEP its old value
    // (0x7481BE `jle 0x7481D4` skips both stores).
    var c = NewPlayer();
    c.ArmNativeColdTime(0x111, 5000, 9000);
    c.ArmNativeColdTime(0x111, -1, 0);
    Equal(9000, Entries(c)[0].Total,
        "a reused node must retain its Total when Remaining<=0 and Total==0");
    Equal(-1, Entries(c)[0].Remaining, "Remaining is stored unconditionally");

    // Fresh node, Total==0, Remaining<0: Total stays 0.
    var d = NewPlayer();
    d.ArmNativeColdTime(0x111, 1, 0);
    d.ArmNativeColdTime(0x98, 1, 0);
    Equal(1, Entries(d)[1].Total, "fresh node positive Remaining copies");
}

void CheckQueryAndTotalGetter()
{
    // sub_748288 returns +0x04 (0x7482C9) and sub_7482E0 returns +0x08
    // (0x748321). They are otherwise byte-identical, so returning the wrong
    // field is an easy and invisible mistake.
    var player = NewPlayer();
    player.ArmNativeColdTime(0x73, 12345, 60000);
    Equal(12345, player.QueryNativeColdTime(0x73),
        "query must return Remaining (+0x04, 0x7482C9)");
    Equal(60000, player.QueryNativeColdTimeTotal(0x73),
        "total getter must return Total (+0x08, 0x748321)");
    NotEqual(player.QueryNativeColdTime(0x73),
        player.QueryNativeColdTimeTotal(0x73),
        "the two getters must read DIFFERENT fields");
    Equal(0, player.QueryNativeColdTimeTotal(0x999), "absent total reads 0");
}

void CheckTickGate()
{
    // 0x748216 `cmp edi,0xFA` then 0x74821C `jle` -- STRICTLY greater than 250.
    // At exactly 250 nothing happens at all, and the latch is not updated.
    const int nativeGate = 0xFA;
    var player = NewPlayer();
    player.ArmNativeColdTime(0x111, 100000, 100000);
    SetTick(player, 1000);

    player.ProcessNativeColdTimes(1000 + nativeGate);
    Equal(100000, player.QueryNativeColdTime(0x111),
        "elapsed == 250 must not tick (jle at 0x74821C)");
    Equal(1000, GetTick(player),
        "the latch must not advance when the gate rejects (0x74821E is after)");

    player.ProcessNativeColdTimes(1000 + nativeGate + 1);
    Equal(100000 - (nativeGate + 1), player.QueryNativeColdTime(0x111),
        "elapsed == 251 must tick and subtract 251");
    Equal(1000 + nativeGate + 1, GetTick(player), "the latch must advance");
}

void CheckTickDecrementsByMeasuredElapsed()
{
    // 0x748246 `sub [eax+4],edi` where edi is the MEASURED elapsed from
    // 0x748210, not the 250 constant. So a long stall subtracts the whole
    // stall and cooldowns are drift-free.
    var player = NewPlayer();
    player.ArmNativeColdTime(0x111, 1_000_000, 1_000_000);
    SetTick(player, 0);
    player.ProcessNativeColdTimes(400_000);
    Equal(600_000, player.QueryNativeColdTime(0x111),
        "a 400s stall must subtract 400000ms, not one 250ms step");

    // Wraparound: the native `sub edi,[esi+0x484]` at 0x748210 is a plain
    // 32-bit subtract of two raw GetTickCount values, so a wrap yields the
    // small positive delta rather than a huge negative one. C# must therefore
    // be `unchecked` -- a checked subtract would throw here.
    // With tick = int.MaxValue-100 and now = int.MinValue+400 the wrapped
    // delta is 501, which clears the 250 gate.
    var wrap = NewPlayer();
    wrap.ArmNativeColdTime(0x111, 5000, 5000);
    SetTick(wrap, int.MaxValue - 100);
    wrap.ProcessNativeColdTimes(int.MinValue + 400);
    Equal(5000 - 501, wrap.QueryNativeColdTime(0x111),
        "tick-count wraparound must be unchecked, matching the raw `sub`");

    // A wrapped delta that lands UNDER the gate must still be rejected, which
    // is the same `jle` as the non-wrapped case (201 <= 250).
    var wrapUnderGate = NewPlayer();
    wrapUnderGate.ArmNativeColdTime(0x111, 5000, 5000);
    SetTick(wrapUnderGate, int.MaxValue - 100);
    wrapUnderGate.ProcessNativeColdTimes(int.MinValue + 100);
    Equal(5000, wrapUnderGate.QueryNativeColdTime(0x111),
        "a wrapped delta of 201 is still under the 250 gate");
}

void CheckExpiryIsLessOrEqualZero()
{
    // 0x748250 `jg 0x74827C` keeps only STRICTLY positive, so 0 expires.
    var atZero = NewPlayer();
    atZero.ArmNativeColdTime(0x111, 1000, 1000);
    SetTick(atZero, 0);
    ClearSent(atZero);
    atZero.ProcessNativeColdTimes(1000);
    Equal(0, Entries(atZero).Count,
        "Remaining hitting exactly 0 must expire (jg, not jge, at 0x748250)");
    Equal(1, SentCount(atZero), "expiry must notify once");

    var stillAlive = NewPlayer();
    stillAlive.ArmNativeColdTime(0x111, 1000, 1000);
    SetTick(stillAlive, 0);
    stillAlive.ProcessNativeColdTimes(999);
    Equal(1, Entries(stillAlive).Count, "Remaining 1 must survive");
    Equal(1, stillAlive.QueryNativeColdTime(0x111), "1ms left");

    // Expiry notifies with remaining 0 and total 0 (0x748252 push 0 /
    // 0x748259 xor ecx,ecx), NOT with the node's stored Total.
    var packet = LastSent(atZero);
    Equal(0, packet.Header.Recog,
        "expiry Recog must be 0 (0x748259 xor ecx,ecx)");
}

void CheckBackwardWalkDeletesAll()
{
    // 0x74822D walks from FCount-1 down to -1 (0x74827D cmp ebx,-1). A forward
    // walk with in-place removal skips entries; this catches that.
    var player = NewPlayer();
    for (uint key = 1; key <= 6; key++)
    {
        player.ArmNativeColdTime(key, 500, 500);
    }
    Equal(6, Entries(player).Count, "six seeded");
    SetTick(player, 0);
    player.ProcessNativeColdTimes(10_000);
    Equal(0, Entries(player).Count,
        "every expired node must be removed in one pass (backward walk)");

    // Mixed: only the expired ones go, survivors keep their relative order.
    var mixed = NewPlayer();
    mixed.ArmNativeColdTime(1, 400, 400);
    mixed.ArmNativeColdTime(2, 90_000, 90_000);
    mixed.ArmNativeColdTime(3, 400, 400);
    mixed.ArmNativeColdTime(4, 90_000, 90_000);
    SetTick(mixed, 0);
    mixed.ProcessNativeColdTimes(1000);
    Equal(2, Entries(mixed).Count, "only the two short ones expire");
    Equal(2u, Entries(mixed)[0].Key, "survivor order preserved (2 before 4)");
    Equal(4u, Entries(mixed)[1].Key, "survivor order preserved");
}

void CheckBulkExpire()
{
    // sub_748338 sets EVERY Remaining to 1 (0x748365 mov dword [eax+4],1), so
    // the whole table expires on the next tick and each entry still emits its
    // normal expiry notification. It does NOT clear the list directly.
    var player = NewPlayer();
    player.ArmNativeColdTime(0x111, 180_000, 180_000);
    player.ArmNativeColdTime(0x98, 30_000, 30_000);
    player.ExpireAllNativeColdTimes();
    Equal(2, Entries(player).Count,
        "bulk expire must NOT delete: it only writes Remaining=1 (0x748365)");
    Equal(1, Entries(player)[0].Remaining, "Remaining forced to the 1 sentinel");
    Equal(1, Entries(player)[1].Remaining, "every element, not just the first");
    Equal(180_000, Entries(player)[0].Total, "Total is untouched");

    SetTick(player, 0);
    ClearSent(player);
    player.ProcessNativeColdTimes(1000);
    Equal(0, Entries(player).Count, "the next tick clears them");
    Equal(2, SentCount(player), "each expiry notifies separately");
}

void CheckIdentSelection()
{
    // 0x7483BD `cmp byte [ebx+0x178],0` selects 3556 for race 0 (a player, set
    // at 0x6AD76F) and 4367 for anything else.
    var player = NewPlayer();
    player.m_btRaceServer = 0;
    ClearSent(player);
    player.ArmNativeColdTime(0x111, 5000, 5000);
    Equal((ushort)0xDE4, LastSent(player).Header.Ident,
        "race 0 must use ident 3556 (0x7483C6)");

    var disguised = NewPlayer();
    disguised.m_btRaceServer = 0x36;
    ClearSent(disguised);
    disguised.ArmNativeColdTime(0x111, 5000, 5000);
    Equal((ushort)0x110F, LastSent(disguised).Header.Ident,
        "nonzero race must use ident 4367 (0x7483CF)");

    // Heroes are NOT silent. THeroAct VMT+0x254 = sub_689A38, which forwards to
    // the master at obj+0x68C after a null check and a master-ghost check
    // (0x689A44/0x689A4E). Only THumanKind's own slot is the ret 0x14 stub.
    var master = NewPlayer();
    var hero = NewHero();
    hero.m_Master = master;
    hero.m_btRaceServer = 54;
    ClearSent(master);
    Assert(hero.ArmNativeColdTime(0x111, 5000, 5000), "hero arm must succeed");
    Equal(1, SentCount(master),
        "a hero's cooldown packet must reach the MASTER's socket (sub_689A38)");
    Equal((ushort)0x110F, LastSent(master).Header.Ident,
        "the hero's own race byte picks the ident, not the master's");

    // Master gone -> dropped (0x689A4A test edi,edi / je).
    var orphan = NewHero();
    orphan.m_Master = null;
    Assert(orphan.ArmNativeColdTime(0x111, 5000, 5000),
        "the node is still created even when the packet cannot be delivered");
    Equal(1, Entries(orphan).Count, "table state is independent of delivery");

    // Ghost master -> dropped (0x689A4E cmp byte [edi+0x73],0 / jne).
    var ghostMaster = NewPlayer();
    ghostMaster.m_boGhost = true;
    var hero2 = NewHero();
    hero2.m_Master = ghostMaster;
    ClearSent(ghostMaster);
    hero2.ArmNativeColdTime(0x111, 5000, 5000);
    Equal(0, SentCount(ghostMaster),
        "a ghost master must receive nothing (0x689A4E)");
}

void CheckSinglePayloadShape()
{
    // 0x748476..0x748495. Body is 8 bytes {Key, Remaining}; count is 1
    // (0x748480 push 1); the key slot carries the KEY (0x74847F push edx);
    // Recog is ecx = Remaining (0x74848A / 0x6D7C67).
    var player = NewPlayer();
    player.m_btRaceServer = 0;
    ClearSent(player);
    player.ArmNativeColdTime(0x10A, 23000, 23000);

    var sent = LastSent(player);
    Equal(8, sent.Body.Length, "single body must be 8 bytes (0x748488 push 8)");
    Equal(0x10Au, BitConverter.ToUInt32(sent.Body, 0), "body[0..4] = Key");
    Equal(23000, BitConverter.ToInt32(sent.Body, 4), "body[4..8] = Remaining");
    Equal(23000, sent.Header.Recog, "Recog = ecx = Remaining");
    Equal((ushort)0x10A, sent.Header.Param, "Param = the pushed key (+0x18)");
    Equal((ushort)1, sent.Header.Tag, "Tag = count 1 (+0x14)");
    Equal((ushort)0, sent.Header.Series, "Series = the pushed 0 (+0x10)");
}

void CheckBulkPayloadIsPerElement()
{
    // The send at 0x748468 sits INSIDE the loop: back edge 0x748472
    // `jne 0x748422`, and the send precedes `inc esi` at 0x74846E. So native
    // emits one packet per element, each with the full count*8 length but only
    // elements 0..i filled -- SetLength (0x748406) zero-fills the rest. Only the
    // final packet is complete.
    var player = NewPlayer();
    player.m_btRaceServer = 0;
    player.ArmNativeColdTime(0x111, 180_000, 180_000);
    player.ArmNativeColdTime(0x98, 30_000, 30_000);
    player.ArmNativeColdTime(0x10A, 23_000, 23_000);

    ClearSent(player);
    NotifyBulk(player);
    Equal(3, SentCount(player),
        "the bulk form must send one packet PER ELEMENT (0x748468 is in-loop)");

    var packets = Sent(player);
    for (var index = 0; index < 3; index++)
    {
        Equal(24, packets[index].Body.Length,
            "every bulk packet carries the FULL count*8 length (0x74845A)");
        Equal((ushort)3, packets[index].Header.Tag,
            "count stays 3 in every packet (0x74844C reads the same word)");
        Equal(0, packets[index].Header.Recog,
            "bulk Recog is 0 (0x74845E xor ecx,ecx)");
        Equal((ushort)0, packets[index].Header.Param,
            "bulk key slot is 0 (0x748451 push 0)");
    }

    // Progressive fill: packet i has elements 0..i populated and the rest zero.
    Equal(0x111u, BitConverter.ToUInt32(packets[0].Body, 0),
        "packet 0 element 0 filled");
    Equal(0u, BitConverter.ToUInt32(packets[0].Body, 8),
        "packet 0 element 1 must still be zero (SetLength zero-fills)");
    Equal(0x98u, BitConverter.ToUInt32(packets[1].Body, 8),
        "packet 1 element 1 filled");
    Equal(0x10Au, BitConverter.ToUInt32(packets[2].Body, 16),
        "the final packet is the complete table");
    Equal(23_000, BitConverter.ToInt32(packets[2].Body, 20),
        "final packet element 2 Remaining");

    // Each packet must be its own buffer: native re-sends a buffer it keeps
    // mutating, but the C# queue must not alias one array or the earlier
    // packets would retroactively show later elements.
    Assert(!ReferenceEquals(packets[0].Body, packets[1].Body),
        "bulk packets must not share one backing array");

    // An empty table sends nothing (0x7483EA cmp / jle 0x74849B).
    var empty = NewPlayer();
    ClearSent(empty);
    NotifyBulk(empty);
    Equal(0, SentCount(empty), "an empty table must send nothing");
}

void CheckTotalIsNeverOnTheWire()
{
    // Total is the fourth (stack) argument of sub_74839C and is read back at
    // 0x7481D7 for the push, but neither payload shape ever writes it. A port
    // that sends it diverges, and the tell is that a distinctive Total value
    // must not appear anywhere in the bytes.
    var player = NewPlayer();
    player.m_btRaceServer = 0;
    ClearSent(player);
    player.ArmNativeColdTime(0x111, 1000, 0x5A5A5A5A);

    var sent = LastSent(player);
    Equal(8, sent.Body.Length, "single form stays 8 bytes with a huge Total");
    Assert(BitConverter.ToInt32(sent.Body, 0) != 0x5A5A5A5A &&
           BitConverter.ToInt32(sent.Body, 4) != 0x5A5A5A5A,
        "Total must not appear in the single payload");
    Assert(sent.Header.Recog != 0x5A5A5A5A,
        "Total must not appear in Recog");
    Equal(0x5A5A5A5A, Entries(player)[0].Total,
        "Total is stored server-side only");

    ClearSent(player);
    NotifyBulk(player);
    var bulk = LastSent(player);
    Assert(BitConverter.ToInt32(bulk.Body, 4) != 0x5A5A5A5A,
        "Total must not appear in the bulk payload either");
}

void CheckCodecSharesOneStore()
{
    // Native has ONE TList at obj+0x504 serving both the runtime and the
    // ScriptData type-7 codec (size sub_6E4C28, emit sub_6E4C4C, parse
    // sub_6E43B8 all read [Self+0x504]). If C# kept a second list, arming a
    // cooldown would not persist and loading one would not tick down.
    var declaring = typeof(TBaseObject)
        .GetField("m_NativeColdTimes",
            BindingFlags.Instance | BindingFlags.Public)?.DeclaringType;
    Equal(typeof(TBaseObject), declaring,
        "there must be exactly ONE m_NativeColdTimes, on TBaseObject");
    Assert(typeof(TPlayObject).GetField("m_NativeColdTimes",
               BindingFlags.Instance | BindingFlags.Public |
               BindingFlags.DeclaredOnly) == null,
        "TPlayObject must NOT redeclare the list (dual source of truth)");

    // A runtime arm must be visible to the codec, and a decoded entry must tick.
    var player = NewPlayer();
    player.ArmNativeColdTime(0x111, 90_000, 180_000);
    var built = Invoke<byte[]>(player, "BuildNativeColdTimePayload");
    Assert(built.Length > 4,
        "an armed cooldown must reach the type-7 emitter");
    Equal(0x0000FAFAu, BitConverter.ToUInt32(built, 0),
        "the inner magic must be present (0x6E4C5A); omitting it makes the " +
        "parser silently misread at stride 8 and still return True");
    Equal(0x111u, BitConverter.ToUInt32(built, 4), "emitted Key");
    Equal(90_000, BitConverter.ToInt32(built, 8), "emitted Remaining");
    Equal(180_000, BitConverter.ToInt32(built, 12), "emitted Total");

    var reloaded = NewPlayer();
    Invoke<object>(reloaded, "ApplyNativeColdTimePayload", built);
    Equal(90_000, reloaded.QueryNativeColdTime(0x111),
        "a decoded cooldown must be queryable by the runtime");
    SetTick(reloaded, 0);
    reloaded.ProcessNativeColdTimes(10_000);
    Equal(80_000, reloaded.QueryNativeColdTime(0x111),
        "a decoded cooldown must tick down");
}

void CheckRunHookPlacement()
{
    // The tick must actually be DRIVEN. Everything above tests the mechanism in
    // isolation, so without this the whole feature could be dead code and the
    // audit would still be green -- a mutation run that deleted the call site
    // went undetected until this check existed.
    //
    // Native order (THumanKind.Run, sub_73C208): 0x73C23C GetTickCount ->
    // 0x73C245 call sub_748200 -> 0x73C24C the death check. So the tick runs
    // for dead actors too and must sit ahead of any m_boDeath gate.
    var run = File.ReadAllText(Path.Combine(FindRepoRoot(), "GameSvr",
        "Actors", "TBaseObject.Base.cs"));
    Assert(run.Contains("ProcessNativeColdTimes(dwRunTick);",
        StringComparison.Ordinal),
        "TBaseObject.Run must call ProcessNativeColdTimes (native 0x73C245)");

    var tick = run.IndexOf("var dwRunTick = HUtil32.GetTickCount();",
        StringComparison.Ordinal);
    var hook = run.IndexOf("ProcessNativeColdTimes(dwRunTick);",
        StringComparison.Ordinal);
    var death = run.IndexOf("if (!m_boDeath)", StringComparison.Ordinal);
    Assert(tick >= 0 && hook > tick,
        "the tick must read GetTickCount before ticking cooldowns (0x73C23C)");
    Assert(death >= 0 && hook < death,
        "cooldowns must tick BEFORE the death gate (0x73C245 precedes 0x73C24C)");
}

// --------------------------------------------------------------- plumbing

TPlayObject NewPlayer()
{
    var player = new TPlayObject();
    player.m_NativeColdTimePacketLog =
        new List<(ClientPacket Header, byte[] Body)>();
    player.m_boOffLineFlag = true;   // keep SendSocket out of the gate manager
    return player;
}

HeroObject NewHero()
{
    var hero = new HeroObject();
    hero.m_btRaceServer = 54;        // RC_HEROOBJECT, [hero+0x178]
    return hero;
}

// Read the entries through reflection into plain tuples: the production
// field is typed, but going through reflection keeps the audit honest about
// reading the SAME store the runtime uses rather than a copy of its own.
List<(uint Key, int Remaining, int Total)> Entries(TBaseObject actor)
{
    var list = (IEnumerable)typeof(TBaseObject)
        .GetField("m_NativeColdTimes",
            BindingFlags.Instance | BindingFlags.Public)
        .GetValue(actor);
    var result = new List<(uint, int, int)>();
    foreach (var item in list)
    {
        var type = item.GetType();
        result.Add(((uint)type.GetField("Key").GetValue(item),
            (int)type.GetField("Remaining").GetValue(item),
            (int)type.GetField("Total").GetValue(item)));
    }
    return result;
}

List<(ClientPacket Header, byte[] Body)> Log(TBaseObject actor)
{
    var field = typeof(TBaseObject).GetField("m_NativeColdTimePacketLog",
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public)
        ?? throw new MissingFieldException("m_NativeColdTimePacketLog");
    var value = field.GetValue(actor);
    if (value == null)
    {
        value = new List<(ClientPacket, byte[])>();
        field.SetValue(actor, value);
    }
    return (List<(ClientPacket Header, byte[] Body)>)value;
}

void ClearSent(TBaseObject actor) => Log(actor).Clear();

int SentCount(TBaseObject actor) => Log(actor).Count;

List<(ClientPacket Header, byte[] Body)> Sent(TBaseObject actor)
    => Log(actor);

(ClientPacket Header, byte[] Body) LastSent(TBaseObject actor)
{
    var log = Log(actor);
    if (log.Count == 0)
    {
        throw new InvalidOperationException("no coldTime packet was sent");
    }
    return log[^1];
}

void SetTick(TBaseObject actor, int value)
    => TickField().SetValue(actor, value);

int GetTick(TBaseObject actor) => (int)TickField().GetValue(actor);

FieldInfo TickField()
    => typeof(TBaseObject).GetField("m_dwNativeColdTimeTick",
           BindingFlags.Instance | BindingFlags.NonPublic)
       ?? throw new MissingFieldException("m_dwNativeColdTimeTick");

void NotifyBulk(TBaseObject actor)
    => typeof(TBaseObject).GetMethod("NotifyNativeColdTime",
           BindingFlags.Instance | BindingFlags.NonPublic)
       .Invoke(actor, new object[] { 0u, 0, 0 });

T Invoke<T>(object target, string name, params object[] args)
    => (T)target.GetType().GetMethod(name,
           BindingFlags.Instance | BindingFlags.NonPublic |
           BindingFlags.Public).Invoke(target, args);

T Const<T>(string name, Type owner = null)
{
    var field = (owner ?? typeof(TBaseObject)).GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic |
        BindingFlags.Public | BindingFlags.FlattenHierarchy)
        ?? throw new MissingFieldException(name);
    return (T)field.GetRawConstantValue();
}

void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

void PrepareRuntimeConfig()
{
    var directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

string FindRepoRoot()
{
    return AuditRepoRoot.Resolve();
}

void Assert(bool condition, string name)
{
    assertions++;
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }
}

void Equal<T>(T expected, T actual, string name)
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
    }
}

void NotEqual<T>(T unexpected, T actual, string name)
{
    assertions++;
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
    {
        throw new InvalidOperationException(
            $"{name}: both sides are {actual}");
    }
}
