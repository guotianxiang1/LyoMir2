using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

TestSelectorAndEarlyGates();
TestMaterialCommitOrderAndEmptySentinel();
TestInclusiveProbabilityBoundary();
TestDiamondRefreshMatrix();
TestCreateBagAndSuccessOrder();
TestRawGbkSuccessMessage();
TestLiveProductionBoundary();

Console.WriteLine(
    "PASS NativeMakeItemUseDiam transaction selector=clear-first " +
    "materials=per-slot/no-whole-rollback random=roll<=OK " +
    "diamond=physical/two-refresh bag=48 add=200 dialog=643 " +
    "logs=34+9 runtime=live");
return;

static void TestSelectorAndEarlyGates()
{
    var foundry = LoadFoundry(
        "井中月=金刚石:38/紫水晶矿:2/OK:90");

    var disabled = new FakeHost(1) { SwitchEnabled = false };
    Equal(NativeMakeItemUseDiamOutcome.FeatureDisabled,
        NativeMakeItemUseDiamTransaction.Execute(foundry, disabled),
        "disabled outcome");
    Sequence(new[]
    {
        "selector:0", "switch:0:08",
        "merchant:643:" + NativeMakeItemUseDiamTransaction.DefaultMessage +
        NativeMakeItemUseDiamTransaction.ExitCommand
    }, disabled.Events, "disabled order");
    Equal(0, disabled.FoundrySelector, "disabled selector clear");

    var missing = new FakeHost(99);
    Equal(NativeMakeItemUseDiamOutcome.RecipeMissing,
        NativeMakeItemUseDiamTransaction.Execute(foundry, missing),
        "missing recipe outcome");
    Sequence(new[]
    {
        "selector:0", "switch:0:08",
        "merchant:643:" + NativeMakeItemUseDiamTransaction.DefaultMessage +
        NativeMakeItemUseDiamTransaction.ExitCommand
    }, missing.Events, "missing recipe order");

    var rejected = new FakeHost(1) { GateAllowed = false };
    Equal(NativeMakeItemUseDiamOutcome.PlayerStateRejected,
        NativeMakeItemUseDiamTransaction.Execute(foundry, rejected),
        "player-state rejection outcome");
    Sequence(new[]
    {
        "selector:0", "switch:0:08", "gate:1:10035",
        "merchant:643:" + NativeMakeItemUseDiamTransaction.DefaultMessage +
        NativeMakeItemUseDiamTransaction.ExitCommand
    }, rejected.Events, "player-state rejection order");
}

static void TestMaterialCommitOrderAndEmptySentinel()
{
    var foundry = LoadFoundry(
        "成品=金刚石:5/材料甲:1/材料乙:2/材料丙:3/OK:90");
    var partial = new FakeHost(1);
    partial.MaterialResults.Enqueue(true);
    partial.MaterialResults.Enqueue(false);
    partial.MaterialResults.Enqueue(true);
    Equal(NativeMakeItemUseDiamOutcome.MaterialOrChanceFailed,
        NativeMakeItemUseDiamTransaction.Execute(foundry, partial),
        "partial material outcome");
    Sequence(new[]
    {
        "selector:0", "switch:0:08", "gate:1:10035",
        "material:材料甲:1", "material:材料乙:2", "material:材料丙:3",
        "merchant:643:" +
        NativeMakeItemUseDiamTransaction.MaterialOrChanceFailureMessage +
        NativeMakeItemUseDiamTransaction.ExitCommand
    }, partial.Events, "partial material continuation order");
    Assert(!partial.Events.Any(value => value.StartsWith("random:",
            StringComparison.Ordinal)),
        "material failure consumed random");

    var sentinelFoundry = LoadFoundry(
        "空哨兵=金刚石:5/材料甲:1/:2/不可达材料:3/OK:90");
    var sentinel = new FakeHost(1) { RandomResult = 91 };
    _ = NativeMakeItemUseDiamTransaction.Execute(sentinelFoundry, sentinel);
    Equal(1, sentinel.Events.Count(value =>
        value.StartsWith("material:", StringComparison.Ordinal)),
        "empty material sentinel did not stop execution");
    Assert(sentinel.Events.Contains("material:材料甲:1"),
        "material before empty sentinel missing");
    Assert(!sentinel.Events.Any(value => value.Contains("不可达材料",
            StringComparison.Ordinal)),
        "material after empty sentinel executed");

    var zeroCountFoundry = LoadFoundry(
        "零数量=金刚石:5/材料甲:256/OK:90");
    var zeroCount = new FakeHost(1) { RandomResult = 91 };
    _ = NativeMakeItemUseDiamTransaction.Execute(zeroCountFoundry, zeroCount);
    Assert(zeroCount.Events.Contains("material:材料甲:0"),
        "wrapped zero-count material slot was skipped");
}

static void TestInclusiveProbabilityBoundary()
{
    var foundry = LoadFoundry(
        "井中月=金刚石:38/紫水晶矿:2/OK:90");
    var failed = new FakeHost(1) { RandomResult = 91 };
    Equal(NativeMakeItemUseDiamOutcome.MaterialOrChanceFailed,
        NativeMakeItemUseDiamTransaction.Execute(foundry, failed),
        "roll above threshold outcome");
    Assert(failed.Events.Contains("random:100"),
        "random bound was not 100");
    Assert(!failed.Events.Any(value => value.StartsWith("diamond:",
            StringComparison.Ordinal)),
        "probability failure took diamond");

    var inclusive = new FakeHost(1)
    {
        RandomResult = 90,
        DiamondAvailable = false
    };
    Equal(NativeMakeItemUseDiamOutcome.DiamondInsufficient,
        NativeMakeItemUseDiamTransaction.Execute(foundry, inclusive),
        "roll equal to threshold did not pass");
    Assert(inclusive.Events.Contains("diamond:38"),
        "inclusive threshold did not enter diamond stage");
}

static void TestDiamondRefreshMatrix()
{
    var zeroFoundry = LoadFoundry(
        "零成本=金刚石:65536/材料:1/OK:90");
    var zero = new FakeHost(1);
    Equal(NativeMakeItemUseDiamOutcome.DiamondInsufficient,
        NativeMakeItemUseDiamTransaction.Execute(zeroFoundry, zero),
        "zero diamond outcome");
    Equal(0, zero.Events.Count(value => value.StartsWith("diamond:",
        StringComparison.Ordinal)), "zero diamond take count");
    Equal(0, zero.Events.Count(value => value == "refresh:10054"),
        "zero diamond refresh count");

    var foundry = LoadFoundry(
        "井中月=金刚石:38/材料:1/OK:90");
    var insufficient = new FakeHost(1) { DiamondAvailable = false };
    Equal(NativeMakeItemUseDiamOutcome.DiamondInsufficient,
        NativeMakeItemUseDiamTransaction.Execute(foundry, insufficient),
        "insufficient diamond outcome");
    Equal(1, insufficient.Events.Count(value => value == "refresh:10054"),
        "insufficient diamond refresh count");
    Assert(IndexOf(insufficient.Events, "diamond:38") <
           IndexOf(insufficient.Events, "refresh:10054"),
        "inner refresh preceded physical diamond attempt");

    var createFailed = new FakeHost(1) { CreatedItem = null };
    Equal(NativeMakeItemUseDiamOutcome.ItemCreateFailed,
        NativeMakeItemUseDiamTransaction.Execute(foundry, createFailed),
        "item create failure outcome");
    Equal(2, createFailed.Events.Count(value => value == "refresh:10054"),
        "successful diamond refresh count");
    Assert(LastIndexOf(createFailed.Events, "refresh:10054") <
           IndexOf(createFailed.Events, "create:井中月"),
        "item creation preceded outer refresh");
}

static void TestCreateBagAndSuccessOrder()
{
    var foundry = LoadFoundry(
        "井中月=金刚石:38/材料:2/OK:90");
    var full = new FakeHost(1) { BagHasCapacity = false };
    Equal(NativeMakeItemUseDiamOutcome.BagFull,
        NativeMakeItemUseDiamTransaction.Execute(foundry, full),
        "full bag outcome");
    Assert(IndexOf(full.Events, "diamond:38") <
           IndexOf(full.Events, "capacity:1:48"),
        "bag capacity was preflighted before diamond deduction");
    Assert(IndexOf(full.Events, "capacity:1:48") <
           IndexOf(full.Events, "dispose"),
        "full bag did not dispose created item");
    Assert(!full.Events.Any(value => value.StartsWith("log:",
            StringComparison.Ordinal)),
        "full bag emitted success log");

    var success = new FakeHost(1);
    Equal(NativeMakeItemUseDiamOutcome.Success,
        NativeMakeItemUseDiamTransaction.Execute(foundry, success),
        "success outcome");
    var successText = NativeMakeItemUseDiamTransaction.SuccessPrefix +
                      "井中月" +
                      NativeMakeItemUseDiamTransaction.SuccessSuffix;
    Sequence(new[]
    {
        "selector:0", "switch:0:08", "gate:1:10035",
        "material:材料:2", "random:100", "diamond:38",
        "refresh:10054", "refresh:10054", "create:井中月",
        "capacity:1:48", "insert", "add:200", "weight",
        "system:255:56:" + successText,
        "log:34:金刚宝石:井中月:38:",
        "log:9:井中月:1:实物锻造",
        "merchant:643:" + successText +
        NativeMakeItemUseDiamTransaction.ExitCommand
    }, success.Events, "success side-effect order");

    Equal(Grobal2.SM_ADDITEM,
        NativeMakeItemUseDiamTransaction.AddItemClientIdent,
        "SM_ADDITEM ident");
    Equal(Grobal2.SM_MERCHANTSAY,
        NativeMakeItemUseDiamTransaction.MerchantSayClientIdent,
        "SM_MERCHANTSAY ident");
}

static void TestRawGbkSuccessMessage()
{
    const string overlongName = "甲甲甲甲甲甲甲乙";
    var foundry = LoadFoundry(
        overlongName + "=金刚石:1/材料:1/OK:90");
    var host = new FakeHost(1);
    Equal(NativeMakeItemUseDiamOutcome.Success,
        NativeMakeItemUseDiamTransaction.Execute(foundry, host),
        "raw GBK success outcome");

    var nameBytes = HUtil32.GbkEncoding.GetBytes(overlongName)
        .AsSpan(0, NativeDiamondFoundry.NameMaximumGbkBytes).ToArray();
    var expectedSystem = HUtil32.GbkEncoding.GetBytes(
            NativeMakeItemUseDiamTransaction.SuccessPrefix)
        .Concat(nameBytes)
        .Concat(HUtil32.GbkEncoding.GetBytes(
            NativeMakeItemUseDiamTransaction.SuccessSuffix)).ToArray();
    Bytes(expectedSystem, host.SystemMessage.GbkBytes.ToArray(),
        "raw GBK system message");
    var expectedDialog = expectedSystem.Concat(HUtil32.GbkEncoding.GetBytes(
        NativeMakeItemUseDiamTransaction.ExitCommand)).ToArray();
    Bytes(expectedDialog, host.MerchantMessage.GbkBytes.ToArray(),
        "raw GBK merchant dialog");
}

static void TestLiveProductionBoundary()
{
    var root = FindRepositoryRoot();
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var helper = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeMakeItemUseDiamTransaction.cs"));

    // Procedure-only LIVE wiring (verified byte/behavior-faithful to sub_64DF3C,
    // staging/diamond_forge_sub64DF3C_verify_20260802.md GO): the CallNpcMethod
    // path dispatches the live forge; the CallNpcFunc path still rejects because
    // a Delphi procedure is not exposed as a function.
    Equal(1, Regex.Matches(bridge,
        "ExecuteNativeDiamondForge\\(CurrentNpc,",
        RegexOptions.CultureInvariant).Count,
        "MakeItemUseDiam method dispatches the live forge exactly once");
    Equal(1, Regex.Matches(bridge,
        "case \\\"makeitemusediam\\\":[\\s\\S]{0,320}?" +
        "is a procedure and is not exposed as a function" +
        "[\\s\\S]{0,200}?RejectUnsupportedNativeApi\\(out result\\);",
        RegexOptions.CultureInvariant).Count,
        "MakeItemUseDiam function ABI stays rejected (procedure-only)");
    Reject(bridge, "NativeMakeItemUseDiamTransaction",
        "transaction type was referenced directly in PasApiBridge");
    foreach (var forbidden in new[]
             {
                 "SaveHumanRcd", "TakeNativeDiamond", "m_nGameGold",
                 "AddDiamondCache", "PasApiBridge"
             })
    {
        Reject(helper, forbidden,
            "transaction gained forbidden production shortcut: " +
            forbidden);
    }
}

static NativeDiamondFoundry LoadFoundry(params string[] lines)
{
    var fileName = Path.Combine(Path.GetTempPath(),
        "make-item-use-diam-" + Guid.NewGuid().ToString("N") + ".txt");
    try
    {
        File.WriteAllLines(fileName, lines, HUtil32.GbkEncoding);
        Assert(NativeDiamondFoundry.TryLoad(fileName, out var foundry,
                out var error),
            "Gifts fixture failed to load: " + error);
        return foundry;
    }
    finally
    {
        if (File.Exists(fileName)) File.Delete(fileName);
    }
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "GameSvr",
                "GameSvr.csproj")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static int IndexOf(IReadOnlyList<string> values, string value)
{
    for (var index = 0; index < values.Count; index++)
        if (values[index] == value) return index;
    return -1;
}

static int LastIndexOf(IReadOnlyList<string> values, string value)
{
    for (var index = values.Count - 1; index >= 0; index--)
        if (values[index] == value) return index;
    return -1;
}

static void Reject(string text, string value, string message)
{
    if (text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Bytes(byte[] expected, byte[] actual, string message)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException(message + ": expected " +
            Convert.ToHexString(expected) + ", actual " +
            Convert.ToHexString(actual));
}

static void Sequence(IReadOnlyList<string> expected,
    IReadOnlyList<string> actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(message + ": expected [" +
            string.Join(" | ", expected) + "], actual [" +
            string.Join(" | ", actual) + "]");
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

sealed class FakeHost : INativeMakeItemUseDiamHost
{
    private int _selector;

    public FakeHost(int selector)
    {
        _selector = selector;
    }

    public bool SwitchEnabled { get; set; } = true;
    public bool GateAllowed { get; set; } = true;
    public int RandomResult { get; set; }
    public bool DiamondAvailable { get; set; } = true;
    public object CreatedItem { get; set; } = new object();
    public bool BagHasCapacity { get; set; } = true;
    public Queue<bool> MaterialResults { get; } = new();
    public List<string> Events { get; } = new();
    public NativeMakeItemUseDiamMessage SystemMessage { get; private set; }
    public NativeMakeItemUseDiamMessage MerchantMessage { get; private set; }

    public int FoundrySelector
    {
        get => _selector;
        set
        {
            _selector = value;
            Events.Add("selector:" + value);
        }
    }

    public bool IsServerSwitchBitSet(int byteOffset, byte mask)
    {
        Events.Add("switch:" + byteOffset + ":" + mask.ToString("X2"));
        return SwitchEnabled;
    }

    public bool TryEnterPlayerState(int mode, int rejectionInternalIdent)
    {
        Events.Add("gate:" + mode + ":" + rejectionInternalIdent);
        return GateAllowed;
    }

    public bool TryConsumeMaterialSlot(
        NativeDiamondFoundry.Material material)
    {
        Events.Add("material:" + material.ItemName + ":" + material.Count);
        return MaterialResults.Count == 0 || MaterialResults.Dequeue();
    }

    public int NextNativeRandom(int exclusiveUpperBound)
    {
        Events.Add("random:" + exclusiveUpperBound);
        return RandomResult;
    }

    public bool TryTakePhysicalDiamondWithoutCapitalRefresh(ushort amount)
    {
        Events.Add("diamond:" + amount);
        return DiamondAvailable;
    }

    public void SendInternalRefresh(int ident)
    {
        Events.Add("refresh:" + ident);
    }

    public object CreateStandardItem(NativeDiamondFoundry.Recipe recipe)
    {
        Events.Add("create:" + recipe.ItemName);
        return CreatedItem;
    }

    public bool HasBagCapacity(int requestedCount, int maximumCount)
    {
        Events.Add("capacity:" + requestedCount + ":" + maximumCount);
        return BagHasCapacity;
    }

    public void InsertBagItem(object item)
    {
        Events.Add("insert");
    }

    public void SendAddItem(object item, int clientIdent)
    {
        Events.Add("add:" + clientIdent);
    }

    public void RefreshBagWeight()
    {
        Events.Add("weight");
    }

    public void DisposeCreatedItem(object item)
    {
        Events.Add("dispose");
    }

    public void SendSuccessSystemMessage(
        NativeMakeItemUseDiamMessage message, byte foregroundColor,
        byte backgroundColor)
    {
        SystemMessage = message;
        Events.Add("system:" + foregroundColor + ":" + backgroundColor +
                   ":" + message.Text);
    }

    public void WriteFoundrySuccessLog(int type, string category,
        NativeDiamondFoundry.Recipe recipe, ushort diamondCost,
        string trailingValue)
    {
        Events.Add("log:" + type + ":" + category + ":" +
                   recipe.ItemName + ":" + diamondCost + ":" +
                   trailingValue);
    }

    public void WriteItemGainLog(int type,
        NativeDiamondFoundry.Recipe recipe, object item, int count,
        string reason)
    {
        Events.Add("log:" + type + ":" + recipe.ItemName + ":" + count +
                   ":" + reason);
    }

    public void SendMerchantDialog(NativeMakeItemUseDiamMessage message,
        int clientIdent)
    {
        MerchantMessage = message;
        Events.Add("merchant:" + clientIdent + ":" + message.Text);
    }
}
