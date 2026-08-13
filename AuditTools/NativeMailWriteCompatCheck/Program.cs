using GameSvr.Services;
using SystemModule;

// AuditTools/NativeMailWriteCompatCheck
//
// Pins GameSvr.Services.NativeMailWriteTransaction (the dormant reference model)
// to the mail WRITE result-code ladders reversed from M2Server.exe 1.0.1.135
// (SHA-256 CC5057...B452F). Every rung asserted below cites the native function
// that produced it. The tool mutates nothing; it only classifies and compares.

var failures = new List<string>();

void Assert(bool condition, string label)
{
    if (!condition) failures.Add(label);
}

void AssertEqual<T>(T expected, T actual, string label)
{
    if (!Equals(expected, actual))
        failures.Add($"{label}: expected [{expected}] got [{actual}]");
}

void AssertSilent(NativeMailWriteOutcome outcome, string label)
{
    Assert(!outcome.SendsReply, $"{label}: expected silent (no reply)");
    Assert(outcome.Recog == 0 && outcome.ReplyIdent == 0,
        $"{label}: silent outcome must be zeroed");
}

void AssertReply(NativeMailWriteOutcome outcome, int ident, int recog,
    string label, int param = 0, int tag = 0, int series = 0)
{
    Assert(outcome.SendsReply, $"{label}: expected a reply");
    AssertEqual(ident, outcome.ReplyIdent, $"{label}.ReplyIdent");
    AssertEqual(recog, outcome.Recog, $"{label}.Recog");
    AssertEqual(param, outcome.Param, $"{label}.Param");
    AssertEqual(tag, outcome.Tag, $"{label}.Tag");
    AssertEqual(series, outcome.Series, $"{label}.Series");
}

CheckProvenanceAndConstants();
CheckSupportedTags();
CheckClaimCoreLadder();
CheckClaimOnlineWrapper();
CheckClaimOfflineWrapper();
CheckDeleteWrapper();
CheckClearAllWrapper();
CheckClearAllEligibility();
CheckMarkRead();
CheckYuanbaoCompletion();
CheckSendDisposition();

if (failures.Count == 0)
{
    Console.WriteLine("Native mail WRITE ladders verified against M2Server.exe " +
        NativeMailWriteTransaction.Baseline);
    foreach (var (op, address, note) in NativeMailWriteTransaction.CoverageMap)
        Console.WriteLine($"  {op,-22} {address,-24} {note}");
    Console.WriteLine("NativeMailWriteCompatCheck PASS");
    return 0;
}

Console.Error.WriteLine("NativeMailWriteCompatCheck FAIL");
foreach (var failure in failures)
    Console.Error.WriteLine("  - " + failure);
return 1;

// ---------------------------------------------------------------------------

void CheckProvenanceAndConstants()
{
    AssertEqual(64, NativeMailWriteTransaction.BaselineSha256.Length,
        "baseline sha length");
    AssertEqual(7, NativeMailWriteTransaction.CoverageMap.Count,
        "coverage map entry count");

    // Claim-core ladder constants (sub_70B664 return values).
    AssertEqual(1, NativeMailWriteTransaction.Delivered, "Delivered");
    AssertEqual(-1, NativeMailWriteTransaction.Failed, "Failed");
    AssertEqual(-2, NativeMailWriteTransaction.AlreadyClaimed, "AlreadyClaimed");
    AssertEqual(-3, NativeMailWriteTransaction.GoldOverflow, "GoldOverflow");
    AssertEqual(-4, NativeMailWriteTransaction.YuanbaoClaimFailed, "YuanbaoClaimFailed");
    AssertEqual(-5, NativeMailWriteTransaction.NotInSafeZone, "NotInSafeZone");
    AssertEqual(0, NativeMailWriteTransaction.Pending, "Pending");

    // Native yuanbao account-op codes.
    AssertEqual(0, NativeMailWriteTransaction.YbSuccess, "YbSuccess");
    AssertEqual(-1500001, NativeMailWriteTransaction.YbInvalidUserId, "YbInvalidUserId");
    AssertEqual(-1500002, NativeMailWriteTransaction.YbInsufficientBalance,
        "YbInsufficientBalance");
    AssertEqual(-1500003, NativeMailWriteTransaction.YbSqlFailure, "YbSqlFailure");
    AssertEqual(-1500004, NativeMailWriteTransaction.YbNegativeAmount, "YbNegativeAmount");

    // The modelled idents must match the in-tree protocol constants.
    AssertEqual(4462, Grobal2.SM_FETCH_ATTACH, "SM_FETCH_ATTACH");
    AssertEqual(4468, Grobal2.SM_FETCH_ATTACH_OFFTM, "SM_FETCH_ATTACH_OFFTM");
    AssertEqual(4463, Grobal2.SM_DEL_MAIL, "SM_DEL_MAIL");
    AssertEqual(4495, Grobal2.SM_CLEAR_ALLMAIL, "SM_CLEAR_ALLMAIL");
}

void CheckSupportedTags()
{
    // sub_70DBCC @0x70DBCC: `cmp dl,7 / ja 0x70DBDB / and edx,0x7F /
    // bt dword [0x7D3DE8],edx / setb al`. dword_7D3DE8 = 7E 8D 40 00 (bits 1..6),
    // and the bt at 0x70DBD7 is the only reference to that address in the image, so
    // nothing writes the mask. Tag 7 has a name (0x7D3DEC[7] = 0x708C10 '用户邮件')
    // but bit 7 of 0x7E is clear, so the gate still rejects it.
    foreach (var tag in new[] { 1, 2, 3, 4, 5, 6 })
        Assert(NativeMailWriteTransaction.IsSupportedTag(tag), $"tag {tag} supported");
    foreach (var tag in new[] { 0, 7, 8 })
        Assert(!NativeMailWriteTransaction.IsSupportedTag(tag),
            $"tag {tag} unsupported");
}

void CheckClaimCoreLadder()
{
    int Core(byte attachStatus, int attachCount, int freeSlots, int moneyType,
        int moneyCount, byte mailType, bool overflow, bool online) =>
        NativeMailWriteTransaction.ClassifyClaimCore(attachStatus, attachCount,
            freeSlots, moneyType, moneyCount, mailType, overflow, online);

    // attachstatus==2 short-circuits to -2, ahead of every other test.
    AssertEqual(-2, Core(2, 0, 10, 0, 0, 0, false, true), "core already-claimed");
    AssertEqual(-2, Core(2, 5, 0, 1, 100, 0, true, false),
        "core already-claimed dominates bag/money");

    // bag capacity: attachmentCount > freeBagSlots -> -1.
    AssertEqual(-1, Core(1, 3, 2, 0, 0, 0, false, true), "core bag full");
    AssertEqual(-1, Core(1, 1, 0, 0, 0, 0, false, true), "core bag full boundary");
    AssertEqual(1, Core(1, 2, 2, 0, 0, 0, false, true), "core bag exactly fits");

    // yuanbao async: moneyType==1 with positive amount defers (0), no reply yet.
    AssertEqual(0, Core(1, 0, 10, 1, 100, 0, false, true), "core yuanbao pending");
    AssertEqual(0, Core(1, 1, 10, 1, 5, 4, false, false),
        "core yuanbao pending ignores mail type / online");

    // moneyType==1 with no amount, and any moneyType>=2, fall to the -1 default.
    AssertEqual(-1, Core(1, 0, 10, 1, 0, 0, false, true), "core yuanbao zero amount");
    AssertEqual(-1, Core(1, 0, 10, 2, 50, 0, false, true), "core invalid money type");

    // moneyType==0 gold path.
    AssertEqual(-3, Core(1, 0, 10, 0, 100, 0, true, true), "core gold overflow");
    AssertEqual(1, Core(1, 0, 10, 0, 100, 0, false, true), "core gold delivered");

    // mail type 4 delivers by state change without item hand-off, even offline.
    AssertEqual(1, Core(1, 0, 10, 0, 0, 4, false, false), "core type4 offline delivered");

    // ordinary item mail: online delivers (1), offline cannot (-1).
    AssertEqual(1, Core(1, 2, 10, 0, 0, 1, false, true), "core items online");
    AssertEqual(-1, Core(1, 2, 10, 0, 0, 1, false, false), "core items offline");

    // pure-item mail with no money still delivers online.
    AssertEqual(1, Core(1, 0, 10, 0, 0, 0, false, true), "core no-money delivered");
}

void CheckClaimOnlineWrapper()
{
    // sub_6E7810: safe-zone gate replies -5 regardless of anything downstream.
    AssertReply(NativeMailWriteTransaction.ClaimAttachOnline(false, 1),
        Grobal2.SM_FETCH_ATTACH, -5, "online not-safe-zone");
    AssertReply(NativeMailWriteTransaction.ClaimAttachOnline(false, 0),
        Grobal2.SM_FETCH_ATTACH, -5, "online not-safe-zone even when core silent");

    // In safe zone: core 0 (async pending or not found) sends nothing.
    AssertSilent(NativeMailWriteTransaction.ClaimAttachOnline(true, 0),
        "online pending silent");

    // In safe zone: nonzero core result is echoed as the Recog.
    AssertReply(NativeMailWriteTransaction.ClaimAttachOnline(true, 1),
        Grobal2.SM_FETCH_ATTACH, 1, "online delivered");
    AssertReply(NativeMailWriteTransaction.ClaimAttachOnline(true, -2),
        Grobal2.SM_FETCH_ATTACH, -2, "online already-claimed");
    AssertReply(NativeMailWriteTransaction.ClaimAttachOnline(true, -3),
        Grobal2.SM_FETCH_ATTACH, -3, "online gold overflow");
}

void CheckClaimOfflineWrapper()
{
    // sub_6E77AC: no safe-zone gate; silent on 0; echoes nonzero as 4468 Recog.
    AssertSilent(NativeMailWriteTransaction.ClaimAttachOffline(0),
        "offline pending silent");
    AssertReply(NativeMailWriteTransaction.ClaimAttachOffline(1),
        Grobal2.SM_FETCH_ATTACH_OFFTM, 1, "offline delivered");
    AssertReply(NativeMailWriteTransaction.ClaimAttachOffline(-2),
        Grobal2.SM_FETCH_ATTACH_OFFTM, -2, "offline already-claimed");
}

void CheckDeleteWrapper()
{
    // sub_6E7888 -> sub_709830 -> sub_70D3D8.
    AssertSilent(NativeMailWriteTransaction.DeleteMail(false, false, 0x123456),
        "delete mailbox missing silent");

    // Not found -> -1; found -> 1; both echo the mail id split hi/lo.
    AssertReply(NativeMailWriteTransaction.DeleteMail(true, false, 0x00123456),
        Grobal2.SM_DEL_MAIL, -1, "delete not found",
        param: 0, tag: 0x0012, series: 0x3456);
    AssertReply(NativeMailWriteTransaction.DeleteMail(true, true, 0x00123456),
        Grobal2.SM_DEL_MAIL, 1, "delete removed",
        param: 0, tag: 0x0012, series: 0x3456);

    // A small id has a zero hi word.
    AssertReply(NativeMailWriteTransaction.DeleteMail(true, true, 42),
        Grobal2.SM_DEL_MAIL, 1, "delete small id split",
        param: 0, tag: 0, series: 42);
}

void CheckClearAllWrapper()
{
    // sub_6E76A4 -> sub_7097F8 -> sub_70D2D0.
    AssertSilent(NativeMailWriteTransaction.ClearAllMail(false, 1, 5),
        "clear mailbox missing silent");

    // Unsupported tag falls straight to the -1 default.
    AssertReply(NativeMailWriteTransaction.ClearAllMail(true, 2, 0),
        Grobal2.SM_CLEAR_ALLMAIL, -1, "clear unsupported tag");

    // Supported tag: nothing eligible -> -1; one or more removed -> 1.
    AssertReply(NativeMailWriteTransaction.ClearAllMail(true, 1, 0),
        Grobal2.SM_CLEAR_ALLMAIL, -1, "clear nothing eligible");
    AssertReply(NativeMailWriteTransaction.ClearAllMail(true, 1, 3),
        Grobal2.SM_CLEAR_ALLMAIL, 1, "clear removed some");
    AssertReply(NativeMailWriteTransaction.ClearAllMail(true, 6, 1),
        Grobal2.SM_CLEAR_ALLMAIL, 1, "clear removed one tag6");
}

void CheckClearAllEligibility()
{
    // Eligible iff read AND (claimed OR no attachment): status2 && attach in {2,3}.
    Assert(NativeMailWriteTransaction.IsClearAllEligible(2, 2), "eligible read+claimed");
    Assert(NativeMailWriteTransaction.IsClearAllEligible(2, 3), "eligible read+empty");
    Assert(!NativeMailWriteTransaction.IsClearAllEligible(2, 1),
        "ineligible unclaimed attachment");
    Assert(!NativeMailWriteTransaction.IsClearAllEligible(2, 0),
        "ineligible attach status 0");
    Assert(!NativeMailWriteTransaction.IsClearAllEligible(1, 2), "ineligible unread");
    Assert(!NativeMailWriteTransaction.IsClearAllEligible(1, 3), "ineligible unread empty");
}

void CheckMarkRead()
{
    // sub_70E240/sub_70C980: write only when the status actually changes.
    Assert(NativeMailWriteTransaction.MarkReadWriteOccurs(1), "mark read from unread");
    Assert(NativeMailWriteTransaction.MarkReadWriteOccurs(0), "mark read from status0");
    Assert(!NativeMailWriteTransaction.MarkReadWriteOccurs(2), "mark read idempotent");
}

void CheckYuanbaoCompletion()
{
    // sub_70B144. Success online, ordinary mail: order 1, delivered, claimed, reply 1.
    var okOnline = NativeMailWriteTransaction.YuanbaoClaimComplete(0, 1, true);
    AssertEqual((byte)1, okOnline.MoneyOrderStatus, "yb ok order status");
    Assert(okOnline.SetsAttachClaimed, "yb ok sets claimed");
    AssertReply(okOnline.Reply, Grobal2.SM_FETCH_ATTACH, 1, "yb ok reply");

    // Success but recipient logged off: order still 1, no delivery, no reply.
    var okOffline = NativeMailWriteTransaction.YuanbaoClaimComplete(0, 1, false);
    AssertEqual((byte)1, okOffline.MoneyOrderStatus, "yb ok offline order status");
    Assert(!okOffline.SetsAttachClaimed, "yb ok offline not claimed");
    AssertSilent(okOffline.Reply, "yb ok offline silent");

    // Mail type 4 completes claimed on success even when offline (no reply though).
    var okType4Online = NativeMailWriteTransaction.YuanbaoClaimComplete(0, 4, true);
    Assert(okType4Online.SetsAttachClaimed, "yb type4 online claimed");
    AssertReply(okType4Online.Reply, Grobal2.SM_FETCH_ATTACH, 1, "yb type4 online reply");
    var okType4Offline = NativeMailWriteTransaction.YuanbaoClaimComplete(0, 4, false);
    Assert(okType4Offline.SetsAttachClaimed, "yb type4 offline claimed");
    AssertSilent(okType4Offline.Reply, "yb type4 offline silent");

    // Failure (SQL) online: order 2, unclaimed, reply -4.
    var failOnline = NativeMailWriteTransaction.YuanbaoClaimComplete(
        NativeMailWriteTransaction.YbSqlFailure, 1, true);
    AssertEqual((byte)2, failOnline.MoneyOrderStatus, "yb fail order status");
    Assert(!failOnline.SetsAttachClaimed, "yb fail not claimed");
    AssertReply(failOnline.Reply, Grobal2.SM_FETCH_ATTACH, -4, "yb fail reply -4");

    // Failure while offline: order 2, no reply.
    var failOffline = NativeMailWriteTransaction.YuanbaoClaimComplete(
        NativeMailWriteTransaction.YbInvalidUserId, 1, false);
    AssertEqual((byte)2, failOffline.MoneyOrderStatus, "yb fail offline order status");
    AssertSilent(failOffline.Reply, "yb fail offline silent");
}

void CheckSendDisposition()
{
    // sub_7092D0: > 6 item groups rejected; SaveMailItem outcome decides the rest.
    AssertEqual(NativeMailSendDisposition.RejectedTooManyItems,
        NativeMailWriteTransaction.ClassifySend(7, true), "send too many items");
    AssertEqual(NativeMailSendDisposition.Created,
        NativeMailWriteTransaction.ClassifySend(6, true), "send six items created");
    AssertEqual(NativeMailSendDisposition.Created,
        NativeMailWriteTransaction.ClassifySend(0, true), "send no items created");
        AssertEqual(NativeMailSendDisposition.InsertFailed,
        NativeMailWriteTransaction.ClassifySend(3, false), "send insert failed");
    // Gold/yuanbao on send have no engine cap: sub_70CF34 is `mov [mail+0x54],ecx`
    // then `test ecx,ecx / jle` to SetAttachStatus(1). The only send abort is
    // item groups > 6 (0x709301 e8 ... call 0x709048 / 0x709306 83 f8 06 / 7e).
}
