using System.Collections;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Mall;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();

var managerType = typeof(MallManager);
var manager = MallManager.Instance;
var load = managerType.GetMethod("LoadPasMallItems",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var parsePaymentVariable = managerType.GetMethod("ParsePaymentVariable",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var getBalance = managerType.GetMethod("GetCurrencyBalance",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var deduct = managerType.GetMethod("DeductCurrency",
    BindingFlags.Static | BindingFlags.NonPublic)!;
Assert(deduct.ReturnType == typeof(bool), "currency deduction does not report failure");

var tempRoot = Path.Combine(Path.GetTempPath(), "mall-currency4-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var customPath = Path.Combine(tempRoot, "YBShopScript.pas");
    WriteGbk(customPath, BuildScript("7,8"));
    var item = LoadSingle(load, manager, customPath);
    Equal(4, item.CurrencyType, "custom item currency type");
    Equal(7, item.PaymentVariableGroup, "FPayTask group was not parsed");
    Equal(8, item.PaymentVariableIndex, "FPayTask index was not parsed");

    var player = new TPlayObject();
    player.m_ScriptVVars[7008] = 125;
    player.m_ScriptVVars[99099] = 888;
    Equal(125, (int)getBalance.Invoke(null, new object[] { player, item })!,
        "currency 4 balance did not use configured V[7,8]");
    Assert((bool)deduct.Invoke(null, new object[] { player, item, 25 })!,
        "configured currency 4 deduction failed");
    Equal(100, player.m_ScriptVVars[7008], "currency 4 deduction missed V[7,8]");
    Equal(888, player.m_ScriptVVars[99099], "currency 4 deduction still touched V[99,99]");
    Assert(!(bool)deduct.Invoke(null, new object[] { player, item, 101 })!,
        "currency 4 overdraw was accepted");
    Equal(100, player.m_ScriptVVars[7008], "currency 4 overdraw made the balance negative");

    foreach (var invalidPayTask in new string[] { "0,8", "not-a-variable", null })
    {
        WriteGbk(customPath, BuildScript(invalidPayTask));
        var invalidItem = LoadSingle(load, manager, customPath);
        Equal(0, invalidItem.PaymentVariableGroup, "invalid FPayTask group was accepted");
        Equal(0, invalidItem.PaymentVariableIndex, "invalid FPayTask index was accepted");
        Assert(!(bool)deduct.Invoke(null, new object[] { player, invalidItem, 10 })!,
            "invalid FPayTask allowed a deduction");
        Equal(100, player.m_ScriptVVars[7008], "invalid FPayTask changed configured balance");
        Assert(!player.m_ScriptVVars.ContainsKey(0), "invalid FPayTask wrote V[0,0]");
    }

    var productionPath = args.Length > 0 ? Path.GetFullPath(args[0]) : string.Empty;
    if (productionPath.Length > 0)
    {
        var source = ReadGbk(productionPath);
        var parseArgs = new object[] { source, 0, 0 };
        parsePaymentVariable.Invoke(null, parseArgs);
        Equal(99, (int)parseArgs[1], "production FPayTask group");
        Equal(99, (int)parseArgs[2], "production FPayTask index");
    }

    var repositoryRoot = FindRepositoryRoot();
    var mallSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "Mall", "MallManager.cs"));
    Reject(mallSource, "GetPlayerVariable(player.m_ScriptVVars, 99, 99)",
        "currency 4 hard-coded balance address");
    Reject(mallSource, "SetPlayerVariable(player.m_ScriptVVars, 99, 99",
        "currency 4 hard-coded deduction address");
    var configGuard = mallSource.IndexOf("if (mallItem.CurrencyType == 4",
        StringComparison.Ordinal);
    var itemAllocation = mallSource.IndexOf("var userItems", configGuard,
        StringComparison.Ordinal);
    var deductionGuard = mallSource.IndexOf("if (!DeductCurrency", itemAllocation,
        StringComparison.Ordinal);
    var itemGrant = mallSource.IndexOf("player.m_ItemList.Add", deductionGuard,
        StringComparison.Ordinal);
    Assert(configGuard >= 0 && configGuard < itemAllocation,
        "invalid FPayTask is not rejected before item creation");
    Assert(deductionGuard > itemAllocation && itemGrant > deductionGuard,
        "items can be granted before the guarded currency deduction");

    Console.WriteLine(productionPath.Length > 0
        ? "PASS custom=V[7,8] invalid=closed overdraw=closed production=V[99,99] hardcoded=0"
        : "PASS custom=V[7,8] invalid=closed overdraw=closed hardcoded=0");
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

static MallItem LoadSingle(MethodInfo load, MallManager manager, string path)
{
    var items = ((IEnumerable)load.Invoke(manager, new object[] { path })!)
        .Cast<MallItem>().ToList();
    Equal(1, items.Count, "test PAS item count");
    return items[0];
}

static string BuildScript(string payTask)
{
    var payTaskDeclaration = payTask == null ? string.Empty : $"FPayTask = '{payTask}';";
    return $$"""
        Program Mir2;
        const
          {{payTaskDeclaration}}
          C_NeedLoadGoodsNames = '充值商品';
        function GetGoods(const GoodsName: String): String;
        begin
          case GoodsName of
            '充值商品': Result := '补给,书页:1,1,25,0,0,0,4,1,1,充值点商品';
          end;
        end;
        Begin
        end.
        """;
}

static void WriteGbk(string path, string value)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    File.WriteAllBytes(path, Encoding.GetEncoding(936).GetBytes(value));
}

static string ReadGbk(string path)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    return Encoding.GetEncoding(936).GetString(File.ReadAllBytes(path));
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(message + " is present");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
