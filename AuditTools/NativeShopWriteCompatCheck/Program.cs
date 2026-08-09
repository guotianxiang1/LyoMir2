using GameSvr;

using Op = GameSvr.NativeShopWriteOp;
using Emit = GameSvr.NativeShopEmit;

// Contract check for the dormant native 白猪商城 / SeeShop write family CM_REQSEESHOP(1046) sub_63A254,
// CM_RENEWSEESHOP(1047) sub_63A32C, CM_DOSHOP(1048) sub_6CB7E4, the shared confirm state machine
// sub_6C7D88 (ident 10035), and the local goods-delivery core sub_6CC420 (SM_DOSHOP_FAIL 816).

try
{
    VerifyConstants();
    VerifyReqSeeShop();
    VerifyRenewSeeShop();
    VerifyDoShopRequest();
    VerifyConfirmGate();
    VerifyDelivery();
    VerifyPaymentNoGo();

    Console.WriteLine(
        "PASS NativeShopWriteCompatCheck 1046=sub_63A254(render 812/815,sent-mask+0xB87) "
        + "1047=sub_63A32C(813/814) 1048=sub_6CB7E4(confirm-only) gate=sub_6C7D88(ident10035,+1809) "
        + "delivery=sub_6CC420(1/-5->816,lingfu+0xBD8) payment=YBDB-6108-NOGO dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeShopWriteCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

void VerifyConstants()
{
    Assert((int)Op.ReqSeeShop == 1046 && (int)Op.RenewSeeShop == 1047 && (int)Op.DoShop == 1048,
        "CM op idents");
    Assert((int)Emit.ShopItems == 812 && (int)Emit.ReShopItemsOk == 813 && (int)Emit.ReShopItemsFail == 814
        && (int)Emit.FirstShop == 815 && (int)Emit.DoShopFail == 816 && (int)Emit.ConfirmDialog == 10035,
        "SM idents");
    Assert(NativeShopWriteTransaction.LingfuGoodsName == "灵符", "lingfu token name");
    Assert(NativeShopWriteTransaction.LingfuBalanceOffset == 0x0BD8, "lingfu balance offset +0xBD8");
    Assert(NativeShopWriteTransaction.SentMaskOffset == 0x0B87, "sent-mask offset +0xB87");
    Assert(NativeShopWriteTransaction.ConfirmPendingOffset == 1809, "confirm-pending offset +1809");
    Assert(NativeShopWriteTransaction.VtblSendDefMessage == 0x250, "SendDefMessage slot");
    Assert(NativeShopWriteTransaction.DeliverySuccess == 1 && NativeShopWriteTransaction.DeliveryBagFull == -5,
        "delivery ladder constants");
    Assert(NativeShopWriteTransaction.MaxShopType == 8, "type<8 gate");
}

void VerifyReqSeeShop()
{
    // type >= 8 -> no send, no mask.
    var over = NativeShopWriteTransaction.RenderReqSeeShop(
        new NativeReqSeeShopContext { RequestedType = 8, PerTypeListPresent = true, HotListPresent = true });
    Assert(over.IsNoOp && over.NoOpReason == "type>=8", "1046 type>=8 -> noop");

    // sent-mask already set -> no send, no mask re-set.
    var already = NativeShopWriteTransaction.RenderReqSeeShop(
        new NativeReqSeeShopContext { RequestedType = 0, SentMaskAlreadySet = true, PerTypeListPresent = true });
    Assert(already.IsNoOp && already.NoOpReason == "sent-mask already set", "1046 mask-set -> noop");

    // both lists present -> 812 + 815, mask set.
    var both = NativeShopWriteTransaction.RenderReqSeeShop(
        new NativeReqSeeShopContext { RequestedType = 3, PerTypeListPresent = true, HotListPresent = true });
    Assert(both.SentShopItems && both.SentFirstShop && both.SentMaskSet, "1046 both lists -> 812+815+mask");

    // per-type list empty, hot present -> only 815, mask still set.
    var hotOnly = NativeShopWriteTransaction.RenderReqSeeShop(
        new NativeReqSeeShopContext { RequestedType = 1, PerTypeListPresent = false, HotListPresent = true });
    Assert(!hotOnly.SentShopItems && hotOnly.SentFirstShop && hotOnly.SentMaskSet, "1046 hot only -> 815+mask");

    // both lists empty -> nothing sent but mask still set (past both gates).
    var empty = NativeShopWriteTransaction.RenderReqSeeShop(
        new NativeReqSeeShopContext { RequestedType = 7, PerTypeListPresent = false, HotListPresent = false });
    Assert(!empty.SentShopItems && !empty.SentFirstShop && empty.SentMaskSet, "1046 empty -> mask only");
}

void VerifyRenewSeeShop()
{
    Assert(NativeShopWriteTransaction.RenderRenewSeeShop(
        new NativeRenewSeeShopContext { RequestedType = 8 }) == Emit.None, "1047 type>=8 -> none");
    Assert(NativeShopWriteTransaction.RenderRenewSeeShop(
        new NativeRenewSeeShopContext { RequestedType = 2, PerTypeListPresent = true }) == Emit.ReShopItemsOk,
        "1047 list present -> 813");
    Assert(NativeShopWriteTransaction.RenderRenewSeeShop(
        new NativeRenewSeeShopContext { RequestedType = 2, PerTypeListPresent = false }) == Emit.ReShopItemsFail,
        "1047 list empty -> 814");
}

void VerifyDoShopRequest()
{
    Assert(NativeShopWriteTransaction.RequestDoShop(
        new NativeDoShopRequestContext { MessageHasBody = false }) == NativeDoShopRequestOutcome.NoResponse,
        "1048 empty body -> no response");
    Assert(NativeShopWriteTransaction.RequestDoShop(
        new NativeDoShopRequestContext { MessageHasBody = true, ConfirmPending = true })
        == NativeDoShopRequestOutcome.ConfirmPending, "1048 pending -> ident 10035");
    Assert(NativeShopWriteTransaction.RequestDoShop(
        new NativeDoShopRequestContext { MessageHasBody = true, ConfirmPending = false })
        == NativeDoShopRequestOutcome.ShowConfirmDialog, "1048 not-pending -> confirm dialog");
}

void VerifyConfirmGate()
{
    var proceed = NativeShopWriteTransaction.ConfirmGate(false, out var e1);
    Assert(proceed && e1 == Emit.None, "confirm gate clear -> proceed, no send");
    var blocked = NativeShopWriteTransaction.ConfirmGate(true, out var e2);
    Assert(!blocked && e2 == Emit.ConfirmDialog, "confirm gate pending -> ident 10035");
}

void VerifyDelivery()
{
    // Pure lingfu token -> local balance credit, success, no 816.
    var lf = NativeShopWriteTransaction.DeliverGoods(new[]
    {
        new NativeShopGoodsToken("灵符", 5, false),
    });
    Assert(lf.ResultCode == 1 && !lf.SentDoShopFail && lf.LingfuCredited == 5 && lf.ItemsGranted == 0,
        "delivery lingfu -> +5 balance, success");

    // Physical item with room -> granted, success.
    var ok = NativeShopWriteTransaction.DeliverGoods(new[]
    {
        new NativeShopGoodsToken("屠龙", 1, true),
    });
    Assert(ok.ResultCode == 1 && !ok.SentDoShopFail && ok.ItemsGranted == 1, "delivery item ok -> success");

    // Bag full -> -5 and SM_DOSHOP_FAIL(816).
    var full = NativeShopWriteTransaction.DeliverGoods(new[]
    {
        new NativeShopGoodsToken("屠龙", 1, false),
    });
    Assert(full.ResultCode == -5 && full.SentDoShopFail, "delivery bag full -> -5 + 816");

    // Mixed: lingfu credited before the failing physical grant aborts the loop.
    var mixed = NativeShopWriteTransaction.DeliverGoods(new[]
    {
        new NativeShopGoodsToken("灵符", 3, false),
        new NativeShopGoodsToken("屠龙", 1, false),
        new NativeShopGoodsToken("裁决", 1, true),
    });
    Assert(mixed.ResultCode == -5 && mixed.SentDoShopFail && mixed.LingfuCredited == 3 && mixed.ItemsGranted == 0,
        "delivery mixed -> lingfu credited, then -5 aborts before later item");

    // Default count of 0 clamps to 1 (sub_40CA18).
    var clamp = NativeShopWriteTransaction.DeliverGoods(new[]
    {
        new NativeShopGoodsToken("灵符", 0, false),
    });
    Assert(clamp.LingfuCredited == 1, "delivery count<=0 clamps to 1");

    // Empty spec -> success, nothing sent.
    var none = NativeShopWriteTransaction.DeliverGoods(Array.Empty<NativeShopGoodsToken>());
    Assert(none.ResultCode == 1 && !none.SentDoShopFail, "delivery empty -> success, silent");
}

void VerifyPaymentNoGo()
{
    Assert(!NativeShopWriteTransaction.NoGoPaymentSettlement(),
        "SeeShop 元宝/金刚石 payment settlement stays YBDB-6108 NO-GO (fail closed)");
}
