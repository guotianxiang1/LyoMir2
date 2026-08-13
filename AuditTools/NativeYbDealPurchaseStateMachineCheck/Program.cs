using GameSvr.Services;

var context = new NativeYbDealPurchaseContext(314, 77);

TestBegin(context);
TestBuyerCallbacks(context);
TestSellerResolution(context);
TestSellerCreditFailure(context);
TestDelivery(context);
TestDuplicateReplay(context);
TestDormantBoundary();

Console.WriteLine(
    "PASS NativeYbDealPurchase dormant ABI=NPC-procedure CM=1254 " +
    "states=Undetermined/Confrim/GivedSellerYB/True " +
    "external=buyer-debit/seller-credit retry=none duplicate=replays " +
    "YBDB310=separate production=fail-closed");
return;

static void TestBegin(NativeYbDealPurchaseContext context)
{
    var host = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.BuyerDebitPending,
        NativeYbDealPurchaseStateMachine.BeginValidatedPurchase(context, host),
        "begin disposition");
    Sequence(new[] { "audit:12:Begin", "audit:13:Begin", "debit:314:77" },
        host.Events, "begin order");
}

static void TestBuyerCallbacks(NativeYbDealPurchaseContext context)
{
    var ignored = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.BuyerCallbackIgnored,
        NativeYbDealPurchaseStateMachine.CompleteBuyerDebit(context, ignored,
            false, true, 0), "missing buyer disposition");
    Equal(0, ignored.Events.Count, "missing buyer side effects");

    Equal(NativeYbDealPurchaseDisposition.BuyerCallbackIgnored,
        NativeYbDealPurchaseStateMachine.CompleteBuyerDebit(context, ignored,
            true, false, 0), "wrong debit callback disposition");
    Equal(0, ignored.Events.Count, "wrong debit callback side effects");

    var failed = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.BuyerDebitFailed,
        NativeYbDealPurchaseStateMachine.CompleteBuyerDebit(context, failed,
            true, true, -1500002), "buyer debit failure disposition");
    Sequence(new[]
    {
        "audit:13:Failure", "notify:Buyer:-1500002"
    }, failed.Events, "buyer debit failure order");
    Assert(!failed.Events.Any(value => value.StartsWith("result:",
        StringComparison.Ordinal)), "buyer debit failure emitted 3003");

    var succeeded = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.SellerResolutionPending,
        NativeYbDealPurchaseStateMachine.CompleteBuyerDebit(context, succeeded,
            true, true, 0), "buyer debit success disposition");
    Sequence(new[]
    {
        "status:Confrim", "audit:13:Success", "resolve-seller:314"
    }, succeeded.Events, "buyer debit success order");
}

static void TestSellerResolution(NativeYbDealPurchaseContext context)
{
    var missingBuyer = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.SellerResolutionIgnored,
        NativeYbDealPurchaseStateMachine.CompleteSellerResolution(context,
            missingBuyer, false, true), "missing buyer resolution disposition");
    Equal(0, missingBuyer.Events.Count, "missing buyer resolution effects");

    var missingSeller = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.SellerResolutionFailed,
        NativeYbDealPurchaseStateMachine.CompleteSellerResolution(context,
            missingSeller, true, false), "missing seller disposition");
    Sequence(new[] { "result:-6" }, missingSeller.Events,
        "missing seller result");

    var succeeded = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.SellerCreditPending,
        NativeYbDealPurchaseStateMachine.CompleteSellerResolution(context,
            succeeded, true, true), "seller resolution success disposition");
    Sequence(new[] { "audit:14:Begin", "credit:314:77" }, succeeded.Events,
        "seller resolution success order");
}

static void TestSellerCreditFailure(NativeYbDealPurchaseContext context)
{
    var wrongCallback = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.SellerCallbackIgnored,
        NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context,
            wrongCallback, false, 0, true, true),
        "wrong seller callback disposition");
    Equal(0, wrongCallback.Events.Count, "wrong seller callback effects");

    var wrongNegativeCallback = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.SellerCallbackIgnored,
        NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context,
            wrongNegativeCallback, false, -1500004, true, true),
        "wrong negative seller callback disposition");
    Sequence(new[] { "result:-1500004" }, wrongNegativeCallback.Events,
        "wrong negative seller callback response");

    var failed = new FakeHost();
    Equal(NativeYbDealPurchaseDisposition.SellerCreditFailed,
        NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context, failed,
            true, -1500003, true, true), "seller credit failure disposition");
    Sequence(new[]
    {
        "audit:14:Failure", "audit:17:Failure",
        "notify:Seller:-1500003", "result:-1500003"
    }, failed.Events, "seller credit failure order");

    var positiveFailure = new FakeHost();
    _ = NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context,
        positiveFailure, true, 7, true, false);
    Assert(!positiveFailure.Events.Any(value => value.StartsWith("result:",
        StringComparison.Ordinal)), "positive account result emitted 3003");
}

static void TestDelivery(NativeYbDealPurchaseContext context)
{
    var missingBuyer = new FakeHost { DeliveryResult = true };
    Equal(NativeYbDealPurchaseDisposition.BuyerDeliveryTargetMissing,
        NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context,
            missingBuyer, true, 0, false, true),
        "missing delivery target disposition");
    Sequence(new[]
    {
        "status:GivedSellerYB", "audit:14:Success"
    }, missingBuyer.Events, "missing delivery target order");

    var failed = new FakeHost { DeliveryResult = false };
    Equal(NativeYbDealPurchaseDisposition.DeliveryFailed,
        NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context, failed,
            true, 0, true, true), "delivery failure disposition");
    Sequence(new[]
    {
        "status:GivedSellerYB", "audit:14:Success", "deliver:314",
        "audit:16:Failure", "audit:17:Failure"
    }, failed.Events, "delivery failure order");
    Assert(!failed.Events.Contains("status:True"),
        "failed delivery advanced order to True");

    var succeeded = new FakeHost { DeliveryResult = true };
    Equal(NativeYbDealPurchaseDisposition.Completed,
        NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context,
            succeeded, true, 0, true, true), "delivery success disposition");
    Sequence(new[]
    {
        "status:GivedSellerYB", "audit:14:Success", "deliver:314",
        "status:True", "result:314", "archive:314", "delete:314",
        "audit:15:Success", "audit:17:Success"
    }, succeeded.Events, "delivery success order");
}

static void TestDuplicateReplay(NativeYbDealPurchaseContext context)
{
    var host = new FakeHost { DeliveryResult = true };
    _ = NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context, host,
        true, 0, true, true);
    _ = NativeYbDealPurchaseStateMachine.CompleteSellerCredit(context, host,
        true, 0, true, true);
    Equal(2, host.Events.Count(value => value == "deliver:314"),
        "duplicate callback delivery count");
    Equal(2, host.Events.Count(value => value == "result:314"),
        "duplicate callback response count");
    Equal(2, host.Events.Count(value => value == "archive:314"),
        "duplicate callback archive count");
}

static void TestDormantBoundary()
{
    var root = FindRepositoryRoot();
    var helperPath = Path.Combine(root, "GameSvr", "Services",
        "NativeYbDealPurchaseStateMachine.cs");
    var helper = File.ReadAllText(helperPath);
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var gameSources = Directory.EnumerateFiles(Path.Combine(root, "GameSvr"),
            "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase))
        .ToArray();

    foreach (var forbidden in new[]
             {
                 "m_nGameGold", "NativeYuanbaoManager", "YbDbClient",
                 "MySql", "Socket", "Task.Run", "Retry", "Generation"
             })
    {
        Reject(helper, forbidden,
            "dormant YBDeal helper gained a runtime authority: " + forbidden);
    }

    Equal(1, gameSources.Count(path => File.ReadAllText(path).Contains(
            "NativeYbDealPurchaseStateMachine", StringComparison.Ordinal)),
        "dormant state machine runtime reference count");
    var marker = "case \"ybdealdialogshowmode\":";
    var start = bridge.IndexOf(marker, StringComparison.Ordinal);
    Assert(start >= 0, "YBDealDialogShowMode PAS case missing");
    var end = bridge.IndexOf("case \"notifyclientopenupdateclothes\":", start,
        StringComparison.Ordinal);
    Assert(end > start, "YBDealDialogShowMode PAS case boundary missing");
    var dispatch = bridge[start..end];
    Require(dispatch, "RejectUnsupportedNativeApi(out result)",
        "YBDealDialogShowMode production entry opened");
    Reject(dispatch, "NativeYbDealPurchaseStateMachine",
        "PAS invokes dormant YBDeal state machine");

    // The evidence corpus lives in D:\loym2\staging, one level above the MAIN
    // checkout only. From an agent worktree (.claude\wt2\<name>) the parent is
    // wt2, so a single GetParent hop lands on a directory with no staging at
    // all and the run dies before any assertion. Walk ancestors until the
    // corpus is actually there.
    var workspace = Directory.GetParent(root)?.FullName
        ?? throw new DirectoryNotFoundException("workspace root not found");
    for (var probe = new DirectoryInfo(root); probe != null; probe = probe.Parent)
    {
        if (File.Exists(Path.Combine(probe.FullName, "staging",
                "ida_ybdeal_632a14_deep.txt")))
        {
            workspace = probe.FullName;
            break;
        }
    }
    var abiEvidence = File.ReadAllText(Path.Combine(workspace, "staging",
        "ida_ybdeal_632a14_deep.txt"));
    var classicEvidence = File.ReadAllText(Path.Combine(workspace, "staging",
        "ida_ybdeal_core.txt"));
    var transactionEvidence = File.ReadAllText(Path.Combine(workspace,
        "staging", "ida_ybdeal_transaction_deep.txt"));
    var queueEvidence = File.ReadAllText(Path.Combine(workspace, "staging",
        "ida_client_ybbuylf_closure_20260718.txt"));
    var externalEvidence = File.ReadAllText(Path.Combine(workspace, "staging",
        "ida_ybdb_6108_dispatch_matrix_20260720.txt"));
    Require(abiEvidence,
        "procedure YBDealDialogShowMode(APlayer: TPlayer; BoFirst: Boolean);",
        "native YBDealDialogShowMode procedure ABI evidence missing");
    Require(classicEvidence, "jumptable 006D830E case 1254",
        "classic CM 1254 dispatcher evidence missing");
    Require(classicEvidence, "call    sub_6F9538",
        "classic CM 1254 purchase owner evidence missing");
    Require(transactionEvidence,
        "buyer debit request @ 0x00633EB0..0x0063405B sub_633EB0",
        "buyer debit callback evidence missing");
    Require(transactionEvidence,
        "seller credit and delivery callback @ 0x0063426C..0x006346A7 sub_63426C",
        "seller credit callback evidence missing");
    Require(transactionEvidence, "sub_630CB8((int)\"GivedSellerYB\"",
        "seller-credit staged status evidence missing");
    Require(transactionEvidence,
        "delivery result @ 0x00633D14..0x00633E05 sub_633D14",
        "delivery finalization evidence missing");
    Require(queueEvidence, "FUNCTION YBManagerDestroy 00710934",
        "queue destruction evidence missing");
    Require(queueEvidence, "FUNCTION YBManagerRun 007109B4",
        "single-pass queue executor evidence missing");
    Require(externalEvidence,
        "sub_6D3694(a1, 310, 0, 32, (char *)src, 0)",
        "separate YBDB 310 sender evidence missing");
    Require(externalEvidence, "n4463 == 1357",
        "separate CM 1350..1363 dispatcher evidence missing");
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory,
                 AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual,
    string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(message + ": expected [" +
            string.Join(" | ", expected) + "], actual [" +
            string.Join(" | ", actual) + "]");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeHost : INativeYbDealPurchaseExactHost
{
    public bool DeliveryResult { get; set; }
    public List<string> Events { get; } = new();

    public void WriteAudit(int stage, NativeYbDealAuditOutcome outcome) =>
        Events.Add($"audit:{stage}:{outcome}");

    public void RequestBuyerDebit(NativeYbDealPurchaseContext context) =>
        Events.Add($"debit:{context.OrderId}:{context.Credit}");

    public void WriteOrderStatusBestEffort(
        NativeYbDealPurchaseContext context, string status) =>
        Events.Add("status:" + status);

    public void BeginSellerResolution(NativeYbDealPurchaseContext context) =>
        Events.Add("resolve-seller:" + context.OrderId);

    public void RequestSellerCredit(NativeYbDealPurchaseContext context) =>
        Events.Add($"credit:{context.OrderId}:{context.Credit}");

    public bool TryDeliverItems(NativeYbDealPurchaseContext context)
    {
        Events.Add("deliver:" + context.OrderId);
        return DeliveryResult;
    }

    public void SendBuyerResult(NativeYbDealPurchaseContext context,
        int result) => Events.Add("result:" + result);

    public void NotifyAccountFailure(NativeYbDealPurchaseContext context,
        NativeYbDealParty party, int errorCode) =>
        Events.Add($"notify:{party}:{errorCode}");

    public void ArchiveHistoryBestEffort(
        NativeYbDealPurchaseContext context) =>
        Events.Add("archive:" + context.OrderId);

    public void DeleteActiveOrderBestEffort(
        NativeYbDealPurchaseContext context) =>
        Events.Add("delete:" + context.OrderId);
}
