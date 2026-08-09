using GameSvr;
using GameSvr.Services;

// NativeStallManagerCompatCheck — locks the dormant in-memory stall-manager model (field offsets,
// backing-store hash layout, manager sub addresses, buyer-order object, status enum, and the
// context-population contract) against the reversed original (stall_mgr_out.txt + stall_exec_out.txt).
// Also cross-validates that the pause/close persist (sub_61FEAC) targets the SAME UPDATE gamedata.stall
// statement the store builds (NativeStallMySqlStore.UpdateStallSql), tying the manager to the store.
// Pure constants — no live objects, no DB.

var failures = new List<string>();

void Eq<T>(T actual, T expected, string what)
{
    if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(actual, expected))
        failures.Add($"{what}: expected [{expected}] got [{actual}]");
}

// ---- manager sub addresses ----
Eq(NativeStallManagerModel.ResolveRecordEa, 0x0049F5F4u, "resolve record ea");
Eq(NativeStallManagerModel.HashProbeEa, 0x0049F2E4u, "hash probe ea");
Eq(NativeStallManagerModel.HashCodeEa, 0x0049F35Cu, "hash code ea");
Eq(NativeStallManagerModel.HashAllocEa, 0x0049F23Cu, "hash alloc ea");
Eq(NativeStallManagerModel.HashDefaultCapacity, 1023, "hash capacity");
Eq(NativeStallManagerModel.RecordCtorEa, 0x0061ED04u, "record ctor ea");
Eq(NativeStallManagerModel.ThreadKeyPrimeEa, 0x0040C988u, "thread key prime ea");
Eq(NativeStallManagerModel.BuyOrderOrchestratorEa, 0x00620F58u, "buy-order orchestrator ea");
Eq(NativeStallManagerModel.BuyFinalizeEa, 0x0061E0C8u, "buy finalize ea");
Eq(NativeStallManagerModel.PauseCloseCheckEa, 0x0061A36Cu, "pause-close check ea");
Eq(NativeStallManagerModel.StallHeaderPersistEa, 0x0061FEACu, "stall header persist ea");
Eq(NativeStallManagerModel.ExecuteScriptEa, 0x00724E48u, "ExecuteScript ea");

// ---- backing-store open-hash layout ----
Eq(NativeStallManagerModel.HashBucketCountOffset, 4, "hash bucket-count offset");
Eq(NativeStallManagerModel.HashBucketsPtrOffset, 8, "hash buckets-ptr offset");
Eq(NativeStallManagerModel.HashEntryKeyOffset, 0x10, "hash entry key offset");
Eq(NativeStallManagerModel.HashEntryPayloadOffset, 0x14, "hash entry payload offset");

// ---- stall record layout ----
Eq(NativeStallManagerModel.RecOwnerNamePtrOffset, 0x08, "rec ownername ptr");
Eq(NativeStallManagerModel.RecOwnerNameLenOffset, 0x0C, "rec ownername len");
Eq(NativeStallManagerModel.RecDbIdxOffset, 0x18, "rec db idx");
Eq(NativeStallManagerModel.RecCreateDateOffset, 0x20, "rec createdate");
Eq(NativeStallManagerModel.RecModifyDateOffset, 0x28, "rec modifydate");
Eq(NativeStallManagerModel.RecStatusOffset, 0x40, "rec status");
Eq(NativeStallManagerModel.RecItemsListOffset, 0x3C, "rec items list");
Eq(NativeStallManagerModel.RecItemHashOffset, 0x50, "rec item hash");
Eq(NativeStallManagerModel.RecOrdersListOffset, 0x54, "rec orders list");

// ---- buyer-order in-memory object ----
Eq(NativeStallManagerModel.BuyOrderObjectSize, 264, "buy-order object size");
Eq(NativeStallManagerModel.BuyOrderItemStructOffset, 0x1F, "buy-order item struct offset");
Eq(NativeStallManagerModel.BuyOrderUpriceOffset, 0xF0, "buy-order uprice");
Eq(NativeStallManagerModel.BuyOrderMoneyTypeOffset, 0xF4, "buy-order moneytype");
Eq(NativeStallManagerModel.BuyOrderCountOffset, 0xF8, "buy-order count");
Eq(NativeStallManagerModel.BuyOrderTotalOffset, 0xFC, "buy-order total");
Eq(NativeStallManagerModel.ItemStructSize, 208, "item struct size == srvData 208");

// ---- item field offsets ----
Eq(NativeStallManagerModel.ItemStdModeOffset, 0x14, "item stdmode offset");
Eq(NativeStallManagerModel.ItemCountOffset, 0x26, "item count offset");
Eq(NativeStallManagerModel.PauseCloseCheckResult, 1, "pause-close check returns 1");

// ---- status enum ----
Eq((int)StallRecordStatus.Initial, 0, "status initial");
Eq((int)StallRecordStatus.Running, 1, "status running");
Eq((int)StallRecordStatus.PausedClosed, 2, "status paused/closed");

// ---- cross-validation: sub_61FEAC persist == the store's UpdateStall statement ----
if (!NativeStallMySqlStore.UpdateStallSql.StartsWith("UPDATE gamedata.stall SET stallname="))
    failures.Add("sub_61FEAC persist must be the store's UpdateStall (UPDATE gamedata.stall SET stallname=...)");
if (!NativeStallMySqlStore.UpdateStallSql.Contains("WHERE ownerid=@owner AND idx=@idx"))
    failures.Add("sub_61FEAC persist WHERE clause must key on ownerid+idx");

// The item struct copied into the buyer-order (0xD0) and into srvData are the same 208-byte record.
Eq(NativeStallManagerModel.ItemStructSize, 0xD0, "item struct == 0xD0");

// context-population contract is a pure descriptor (no live objects touched)
Eq(NativeStallManagerModel.DescribesFaithfulPopulation, true, "context-population descriptor present");

if (failures.Count == 0)
{
    Console.WriteLine("PASS NativeStallManagerCompatCheck: backing-store name-hash (probe sub_49F2E4, " +
        "key+0x10/payload+0x14) + record layout (status+0x40 {0/1/2}, itemhash+0x50, orders+0x54) + " +
        "buyer-order 264B (item+0x1F=208) + pause-close sub_61A36C(->1)+sub_61FEAC(==BuildUpdateStall) + " +
        "buy-finalize sub_61E0C8; MODEL-ONLY (live hook full-stack-gated, no wiring)");
    return 0;
}

Console.Error.WriteLine("NativeStallManagerCompatCheck: FAIL");
foreach (var f in failures) Console.Error.WriteLine("  - " + f);
return 1;
