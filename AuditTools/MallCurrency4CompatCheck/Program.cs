using System.Collections;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Mall;

// ---------------------------------------------------------------------------
// 这个工具原来钉的是"货币类型 4 = 充值点，扣 V(group,index)"这条契约。逐字节复核推翻了它：
//
//   原生商品表压根没有货币类型字段。加载器 sub_636D68 把 PAS @GetYBShopConfig 的返回串按
//   '$' 切（0x636F8F b1 24 mov cl,0x24），只认序号 1..10（0x636FC6 83 f8 0a cmp eax,0xA
//   + ja 0x63709A），跳表 0x636FD6 的十个臂依次是 vClassName / vItemList / vGoodsIdx /
//   vSrcPrice / vCurPrice / vLimitType / vLimitCount / vEffectImg / vEffectCount /
//   vGoodsExplain。没有货币类型、没有绑定标志、没有全服广播标志。
//
//   付款侧同样没有本地扣款：CM_DOSHOP(1048) 处理器 sub_6CB7E4 只有确认闸 0x6CB816
//   call 0x6C7D88，然后 0x6CB8C9 mov ecx,0x6CB940('@ClientBuy') / 0x6CB8D0 call 0x636BD8
//   把活交给脚本；发货核心 sub_6CC420 从头到尾唯一一条余额加减是 0x6CC504
//   add [esi+0xBD8],eax（发灵符），一条减法都没有。生产脚本 ClientBuy 的付款判定是
//   This_Player.YBNum >= Price，扣费走外部元宝库 PsYBConsumEx。
//
// 断言不删，改成钉真正的契约：'$'/10 字段、六项 -1 校验、以及"本地不得有任何货币扣减"。
// ---------------------------------------------------------------------------

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();

var managerType = typeof(MallManager);
var manager = MallManager.Instance;
var load = managerType.GetMethod("LoadPasMallItems",
    BindingFlags.Instance | BindingFlags.NonPublic)!;

Assert(managerType.GetMethod("DeductCurrency",
        BindingFlags.Static | BindingFlags.NonPublic) == null,
    "MallManager still exposes a local DeductCurrency path");
Assert(managerType.GetMethod("GetCurrencyBalance",
        BindingFlags.Static | BindingFlags.NonPublic) == null,
    "MallManager still exposes a local GetCurrencyBalance path");
Assert(typeof(MallItem).GetProperty("CurrencyType") == null,
    "MallItem still carries the non-native CurrencyType field");
Assert(typeof(MallItem).GetProperty("PaymentVariableGroup") == null,
    "MallItem still carries the non-native PaymentVariableGroup field");

var settle = managerType.GetMethod("TrySettleYuanbaoPayment",
    BindingFlags.Static | BindingFlags.NonPublic);
Assert(settle != null, "the single yuanbao settlement gate is missing");

var tempRoot = Path.Combine(Path.GetTempPath(), "mall-goods-contract-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var scriptPath = Path.Combine(tempRoot, "YBShopScript.pas");

    // 1) 完整的 10 字段记录必须被收下，且每个字段落到正确的属性上。
    WriteGbk(scriptPath, BuildCaseScript(
        className: "强化", itemList: "书页:3", goodsIdx: "218", srcPrice: "2000",
        curPrice: "1500", limitType: "1", limitCount: "5",
        effectImg: "520", effectCount: "18", explain: "测试商品"));
    var item = LoadSingle(load, manager, scriptPath);
    Equal(218, item.Id, "vGoodsIdx -> Id");
    Equal(2000, item.Price, "vSrcPrice -> Price (record +36)");
    Equal(1500, item.CurPrice, "vCurPrice -> CurPrice (record +38)");
    Equal(1, item.LimitType, "vLimitType -> LimitType (record +40)");
    Equal(5, item.LimitCount, "vLimitCount -> LimitCount (record +42)");
    Equal(520, item.EffectImg, "vEffectImg -> EffectImg (record +48 DWORD)");
    Equal(18, item.EffectCount, "vEffectCount -> EffectCount (record +46 WORD)");
    Equal(3, item.ItemCount, "'名:数量' count");
    Equal(0, item.Category, "first distinct class name is category 0");

    // 2) 六项 -1 校验：0x6370AA..0x6370E0 命中任一条整条丢弃。
    //    vLimitType / vLimitCount 不在校验清单里，缺了照收。
    AssertDropped(load, manager, scriptPath, "vClassName", "空 vClassName 被收下了");
    AssertDropped(load, manager, scriptPath, "vGoodsIdx", "缺 vGoodsIdx 被收下了");
    AssertDropped(load, manager, scriptPath, "vSrcPrice", "缺 vSrcPrice 被收下了");
    AssertDropped(load, manager, scriptPath, "vCurPrice", "缺 vCurPrice 被收下了");
    AssertDropped(load, manager, scriptPath, "vEffectImg", "缺 vEffectImg 被收下了");
    AssertDropped(load, manager, scriptPath, "vEffectCount", "缺 vEffectCount 被收下了");
    AssertKept(load, manager, scriptPath, "vLimitType", "缺 vLimitType 被误丢");
    AssertKept(load, manager, scriptPath, "vLimitCount", "缺 vLimitCount 被误丢");

    // 3) 分类号是"首次出现顺序"，不是固定名字表（sub_635444 0x635502 mov [rec+0x7A],ax）。
    WriteGbk(scriptPath, BuildTwoClassScript());
    var ordered = LoadAll(load, manager, scriptPath);
    Equal(3, ordered.Count, "two-class fixture item count");
    Equal(0, ordered[0].Category, "装饰 is the first distinct class -> 0");
    Equal(1, ordered[1].Category, "强化 is the second distinct class -> 1");
    Equal(0, ordered[2].Category, "装饰 again reuses category 0");

    // 4) 生产脚本必须能解析出商品，且每条字段与 case 分支逐字对得上。
    //    旧解析器要求 `C_NeedLoadGoodsNames =` 与 `'名': Result := '…'`，生产文件
    //    是 `C_NeedLoadGoodsNames_001 =` + case 赋值，整表 0 条。
    var productionPath = args.Length > 0
        ? Path.GetFullPath(args[0])
        : @"D:\光头卧龙\mud2.0\Mir200\Envir\YBShop\YBShopScript.pas";
    var productionCount = -1;
    if (File.Exists(productionPath))
    {
        var production = LoadAll(load, manager, productionPath);
        productionCount = production.Count;
        Equal(10, productionCount, "production YBShopScript.pas item count");
        EqualStr("贵族斗笠", production[0].ItemName, "prod[0] name");
        Equal(0, production[0].Category, "prod[0] 装饰 is first-seen category 0");
        Equal(222, production[0].Id, "prod[0] vGoodsIdx");
        Equal(2000, production[0].Price, "prod[0] vSrcPrice");
        Equal(2000, production[0].CurPrice, "prod[0] vCurPrice");
        Equal(0, production[0].LimitType, "prod[0] vLimitType");
        Equal(0, production[0].LimitCount, "prod[0] vLimitCount");
        Equal(520, production[0].EffectImg, "prod[0] vEffectImg");
        Equal(18, production[0].EffectCount, "prod[0] vEffectCount");
        Equal(1, production[4].Category, "prod 随机传送石 强化 is first-seen category 1, not 2");
        Equal(247, production[4].Id, "prod 随机传送石 vGoodsIdx");
        Equal(218, production[5].Id, "prod 盟重传送石 vGoodsIdx");
        Equal(1, production[5].Category, "prod 盟重传送石 reuses 强化 = 1");
        Equal(50, production[9].CurPrice, "prod 疗伤药包 vCurPrice");
        // 生产限购槽位：GetLimitValue 是 `Result := 0;` 空桩，CollectLimitSlots 必须是空表。
        var slotsField = managerType.GetField("_limitSlots",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(slotsField != null, "_limitSlots field missing");
        var slots = (System.Collections.IDictionary)slotsField.GetValue(manager)!;
        Equal(0, slots.Count, "production GetLimitValue is a stub; no S/V slots may be invented");
    }

    // 5) 源码形状：不许有任何本地货币扣减，结算闸必须在建物品与入包之前。
    var repositoryRoot = FindRepositoryRoot();
    var mallSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "Mall", "MallManager.cs"));
    Reject(mallSource, "m_nGold -=", "local gold debit");
    Reject(mallSource, "m_nGameGold -=", "local yuanbao debit");
    Reject(mallSource, "SetShengWan(", "local shengwan debit");
    Reject(mallSource, "SetPlayerVariable(player, 'V'", "local V-bank debit");
    Assert(mallSource.Contains("mallItem.CurPrice * quantity", StringComparison.Ordinal),
        "purchase total must use vCurPrice, not vSrcPrice");

    var mailSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "Players", "TPlayObject.Mail.cs"));
    Reject(mailSource, "m_nGold += record.MoneyCount",
        "mail claim gold bypasses IncGold");
    Assert(mailSource.Contains("IncGold(record.MoneyCount)", StringComparison.Ordinal),
        "mail claim gold must go through IncGold (native 0x70B7DB call [vmt+0x28C])");

    var mallListSource = File.ReadAllText(Path.Combine(repositoryRoot,
        "GameSvr", "Players", "TPlayObject.Mall.cs"));
    Reject(mallListSource, "stdItem.Looks == 0",
        "Looks==0 skip drops native-visible shop rows");
    Assert(mallListSource.Contains("item.EffectImg", StringComparison.Ordinal),
        "Looks miss fallback must read vEffectImg (native 0x639DD6 mov ax,[rec+0x30])");

    var settleGate = mallSource.IndexOf("if (!TrySettleYuanbaoPayment", StringComparison.Ordinal);
    var itemAllocation = mallSource.IndexOf("var userItems", StringComparison.Ordinal);
    var itemGrant = mallSource.IndexOf("player.m_ItemList.Add", StringComparison.Ordinal);
    Assert(settleGate >= 0, "settlement gate call is missing from PurchaseItem");
    Assert(settleGate < itemAllocation,
        "items are created before the settlement gate");
    Assert(itemAllocation < itemGrant,
        "items reach the bag before they are created");

    Console.WriteLine("PASS goods-contract=$/10 validated=6 limit-fields=tolerated "
        + "categories=first-seen local-debits=0 settlement=fail-closed production="
        + (productionCount >= 0 ? productionCount.ToString() : "skipped"));
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

static void AssertDropped(MethodInfo load, MallManager manager, string path,
    string omittedField, string message)
{
    WriteGbk(path, BuildCaseScriptWithout(omittedField));
    Assert(LoadAll(load, manager, path).Count == 0, message);
}

static void AssertKept(MethodInfo load, MallManager manager, string path,
    string omittedField, string message)
{
    WriteGbk(path, BuildCaseScriptWithout(omittedField));
    Assert(LoadAll(load, manager, path).Count == 1, message);
}

static List<MallItem> LoadAll(MethodInfo load, MallManager manager, string path)
{
    return ((IEnumerable)load.Invoke(manager, new object[] { path })!)
        .Cast<MallItem>().ToList();
}

static MallItem LoadSingle(MethodInfo load, MallManager manager, string path)
{
    var items = LoadAll(load, manager, path);
    Equal(1, items.Count, "test PAS item count");
    return items[0];
}

static string BuildCaseScriptWithout(string omittedField)
{
    return BuildCaseScript(
        className: omittedField == "vClassName" ? null : "强化",
        itemList: omittedField == "vItemList" ? null : "书页:3",
        goodsIdx: omittedField == "vGoodsIdx" ? null : "218",
        srcPrice: omittedField == "vSrcPrice" ? null : "2000",
        curPrice: omittedField == "vCurPrice" ? null : "1500",
        limitType: omittedField == "vLimitType" ? null : "1",
        limitCount: omittedField == "vLimitCount" ? null : "5",
        effectImg: omittedField == "vEffectImg" ? null : "520",
        effectCount: omittedField == "vEffectCount" ? null : "18",
        explain: "测试商品");
}

// 生产脚本的形状：case 分支里逐个 vXxx := ...;，函数开头没有初始化段，
// 所以"省略某个字段"就等于该字段走 StrToIntDef 的 -1 缺省。
static string BuildCaseScript(string className, string itemList, string goodsIdx,
    string srcPrice, string curPrice, string limitType, string limitCount,
    string effectImg, string effectCount, string explain)
{
    var body = new StringBuilder();
    AppendString(body, "vClassName", className);
    AppendString(body, "vItemList", itemList);
    AppendNumber(body, "vGoodsIdx", goodsIdx);
    AppendNumber(body, "vSrcPrice", srcPrice);
    AppendNumber(body, "vCurPrice", curPrice);
    AppendNumber(body, "vLimitType", limitType);
    AppendNumber(body, "vLimitCount", limitCount);
    AppendNumber(body, "vEffectImg", effectImg);
    AppendNumber(body, "vEffectCount", effectCount);
    AppendString(body, "vGoodsExplain", explain);

    return "Program Mir2;\r\n"
        + "const\r\n"
        + "  C_NeedLoadGoodsNames_001 = '测试商品名';\r\n"
        + "function GetYBShopConfig(GoodsName: string): string;\r\n"
        + "begin\r\n"
        + "  case GoodsName of\r\n"
        + "    '测试商品名':\r\n"
        + "    begin\r\n"
        + body
        + "    end;\r\n"
        + "  end;\r\n"
        + "end;\r\n"
        + "Begin\r\n"
        + "end.\r\n";
}

static string BuildTwoClassScript()
{
    return "Program Mir2;\r\n"
        + "const\r\n"
        + "  C_NeedLoadGoodsNames_001 = '甲|乙|丙';\r\n"
        + "function GetYBShopConfig(GoodsName: string): string;\r\n"
        + "begin\r\n"
        + "  case GoodsName of\r\n"
        + Branch("甲", "装饰")
        + Branch("乙", "强化")
        + Branch("丙", "装饰")
        + "  end;\r\n"
        + "end;\r\n"
        + "Begin\r\n"
        + "end.\r\n";
}

static string Branch(string goods, string className)
{
    return $"    '{goods}':\r\n    begin\r\n"
        + $"      vClassName := '{className}';\r\n"
        + $"      vItemList := '{goods}:1';\r\n"
        + "      vGoodsIdx := 1;\r\n      vSrcPrice := 10;\r\n      vCurPrice := 10;\r\n"
        + "      vLimitType := 0;\r\n      vLimitCount := 0;\r\n"
        + "      vEffectImg := 380;\r\n      vEffectCount := 1;\r\n"
        + $"      vGoodsExplain := '{goods}';\r\n    end;\r\n";
}

static void AppendString(StringBuilder body, string name, string value)
{
    if (value != null) body.Append($"      {name} := '{value}';\r\n");
}

static void AppendNumber(StringBuilder body, string name, string value)
{
    if (value != null) body.Append($"      {name} := {value};\r\n");
}

static void WriteGbk(string path, string value)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    File.WriteAllBytes(path, Encoding.GetEncoding(936).GetBytes(value));
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

static void EqualStr(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
