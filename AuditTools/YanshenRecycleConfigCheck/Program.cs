using System.Text;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
InitializeGameState();

var root = Path.Combine(Path.GetTempPath(),
    "yanshen-recycle-check-" + Guid.NewGuid().ToString("N"));

try
{
    var (manager, runtime) = CreateManager(Path.Combine(root, "valid"), includeRecycle: true);
    var recyclePath = Path.Combine(runtime, "MyJson", "recycle.json");

    Assert(manager.HasValidRecycleConfig, "startup did not publish recycle.json");
    Assert(manager.RecycleConfigItemCount == 3, "startup item count mismatch");
    Assert(manager.IsRecycleItemConfigured("屠龙") &&
           manager.IsRecycleItemConfigured("丹书铁卷"),
        "startup snapshot lost configured item names");

    var plugin = manager.GetPlugin("YanshenCompat");
    plugin.IsInitialized = true;
    var api = new YanshenApi(new TPlayObject(), null, manager);
    // 原生 AutoRecycle 不返回件数：入口 0x1006CF10 在配置无效时 0x1006CF20 返回 -999，
    // 正常出口 0x1006CECC mov eax,1 恒返回 1。
    Assert(api.AutoRecycle() == 1,
        "valid cached config did not execute against an empty bag");

    WriteGbk(recyclePath, "{bad json");
    Assert(!manager.ReloadRecycleConfig(out var syntaxError) &&
           !string.IsNullOrWhiteSpace(syntaxError),
        "malformed JSON unexpectedly reloaded");
    Assert(manager.RecycleConfigItemCount == 3 &&
           manager.IsRecycleItemConfigured("屠龙"),
        "malformed reload replaced the last valid snapshot");
    Assert(api.AutoRecycle() == 1,
        "AutoRecycle stopped consuming the last valid snapshot after a failed reload");

    // 生产 recycle.json 的 可叠材料 指向出厂模板遗留的 "类型2"，而它的 回收类型 段里没有
    // 这个名字。整份判废等于那台服务器一件都回收不了，所以只有那件物品失去结算规则，
    // 其余照常加载；没有结算规则就永远不回收，删不掉也就付不了。
    WriteGbk(recyclePath,
        "{\"物品种类\":{\"屠龙\":\"不存在\",\"怒斩\":\"类型1\"}," +
        "\"回收类型\":{\"类型1\":{\"金币\":1}}}");
    Assert(manager.ReloadRecycleConfig(out var danglingError),
        "undefined recycle type rejected the whole document: " + danglingError);
    Assert(manager.RecycleConfigItemCount == 1 &&
           manager.IsRecycleItemConfigured("怒斩") &&
           !manager.IsRecycleItemConfigured("屠龙"),
        "undefined recycle type did not drop exactly the unresolvable item");
    Assert(api.AutoRecycle() == 1,
        "an item without a settlement rule must never be recycled");

    // 装载校验 0x1009103E / 0x10091056 两键缺一就把有效位 0x1031B8C5 置 0，
    // 而入口 0x1006CF16 一读到 0 就 -999 收工。可叠材料 不算数。
    WriteGbk(recyclePath,
        "{\"可叠材料\":{\"新材料\":\"类型2\"}," +
        "\"回收类型\":{\"类型2\":{\"金币\":1}}}");
    Assert(!manager.ReloadRecycleConfig(out var rootKeyError) &&
           rootKeyError.Contains("物品种类", StringComparison.Ordinal),
        "config without 物品种类 was accepted");

    // 未知规则字段被忽略，其余字段照常生效 —— 原生只按名取，从不枚举成员：
    // sub_1006B020 全域 157 处 push 常量字符串只涉及 16 个键名，全部喂给 jsoncpp 的
    // 按名访问族（0x100E0BE0/0DC0/0EA0/0ED0/1210/1240，共 152 次调用）；装载校验
    // sub_10090EF0 也只查 0x1009103E 物品种类 与 0x10091056 回收类型 两个根键。
    // 这条断言此前钉的是 C# 自己的 fail-closed（default: throw），不是原版契约。
    WriteGbk(recyclePath,
        "{\"物品种类\":{\"屠龙\":\"类型1\"}," +
        "\"回收类型\":{\"类型1\":{\"金币\":1,\"没这个键\":1}}}");
    Assert(manager.ReloadRecycleConfig(out var schemaError),
        "unknown 回收类型 field must be ignored, not rejected: " + schemaError);
    Assert(manager.RecycleConfigItemCount == 1 &&
           manager.IsRecycleItemConfigured("屠龙"),
        "unknown field dropped the sibling rules");

    // 复合字段缺子键 ⇒ 该字段整段不解析、退回缺省（门失效 / 1 倍 / 不写 V），
    // 而不是整份配置作废。总开关 四个门 0x1006B4A1 / 0x1006B4E2 / 0x1006B523 /
    // 0x1006B564 全部 je 0x1006B633（紧接着就解析 回收倍率）；关闭值 停在
    // 0x1006B2F4 C7 45 84 FE FF FF FF 预置的 -2，消费端 0x1006B787 jl 判 < -1 放行。
    // 回收倍率 同形：0x1006B660 / 0x1006B69F / 0x1006B6DE 全部 je 0x1006B783。
    WriteGbk(recyclePath,
        "{\"物品种类\":{\"屠龙\":\"类型1\"}," +
        "\"回收类型\":{\"类型1\":{\"金币\":1,\"总开关\":{\"v1\":10}," +
        "\"回收倍率\":{\"v1\":10}}}}");
    Assert(manager.ReloadRecycleConfig(out var partialError),
        "a composite field missing a subkey must fall back to its default, " +
        "not fail the document: " + partialError);
    Assert(manager.RecycleConfigItemCount == 1 &&
           manager.IsRecycleItemConfigured("屠龙"),
        "partial composite field dropped the rule");

    WriteGbk(recyclePath,
        "{\"可叠材料\":{\"新材料\":\"类型2\"},\"物品种类\":{}," +
        "\"回收类型\":{\"类型2\":{\"元宝\":20}}}");
    Assert(manager.ReloadRecycleConfig(out var replacementError),
        "valid replacement failed: " + replacementError);
    Assert(manager.RecycleConfigItemCount == 1 &&
           manager.IsRecycleItemConfigured("新材料") &&
           !manager.IsRecycleItemConfigured("屠龙"),
        "valid replacement was not atomically published");

    File.WriteAllBytes(recyclePath, new byte[] { 0x81 });
    Assert(!manager.ReloadRecycleConfig(out var encodingError) &&
           !string.IsNullOrWhiteSpace(encodingError),
        "invalid GBK byte sequence unexpectedly reloaded");
    Assert(manager.IsRecycleItemConfigured("新材料"),
        "encoding failure replaced the last valid snapshot");

    Assert(new YanshenApi(null, null, manager).AutoRecycle() == -999,
        "AutoRecycle exception path did not return -999");

    var (missingManager, _) = CreateManager(
        Path.Combine(root, "missing"), includeRecycle: false);
    missingManager.GetPlugin("YanshenCompat").IsInitialized = true;
    var missingApi = new YanshenApi(new TPlayObject(), null, missingManager);
    Assert(!missingManager.HasValidRecycleConfig && missingApi.AutoRecycle() == -999,
        "missing initial recycle config did not return -999");

    CheckProductionRecycleConfig(root);
    CheckUnmatchedItemsAreNeverDeleted(root);

    Console.WriteLine(
        "YanshenRecycleConfigCheck PASS startup=GBK unknownField=ignored rootKeys=物品种类+回收类型 " +
        "reload=atomic lastValid=preserved autoRecycle=1/-999 " +
        "production=313items/2dangling nonMatch=neverDeleted");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

/// <summary>
/// 生产配置（权威）是 D:\光头卧龙 那一份，不是插件包里的沙箱件。两份连一个变量号都不
/// 相同（沙箱 7868 B / md5 94adb8ba，总开关 {100,n}、倍率 {101,n}、极品/元素 201-212/301-312），
/// 照沙箱实现会把每条规则指向错误的变量，所以先用长度 + md5 把两者分开。
/// 生产树不在本机时跳过，不把缺文件当失败。
/// </summary>
static void CheckProductionRecycleConfig(string root)
{
    const string production = @"D:\光头卧龙\mud2.0\Mir200\Gs1\MyJson\recycle.json";
    if (!File.Exists(production))
    {
        Console.WriteLine("YanshenRecycleConfigCheck SKIP production tree absent: " + production);
        return;
    }

    var bytes = File.ReadAllBytes(production);
    Assert(bytes.Length == 11514,
        "production recycle.json is " + bytes.Length + " bytes, expected 11514");
    var md5 = Convert.ToHexString(
        System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant();
    Assert(md5 == "af897884085b82d818352303f897f85f",
        "production recycle.json md5 is " + md5 + ", expected af897884085b82d818352303f897f85f");

    var (manager, runtime) = CreateManager(Path.Combine(root, "production"), includeRecycle: true);
    File.Copy(production, Path.Combine(runtime, "MyJson", "recycle.json"), overwrite: true);
    Assert(manager.ReloadRecycleConfig(out var error),
        "production recycle.json failed to parse: " + error);

    // 313 条全部来自 物品种类；可叠材料 的两件指向出厂模板遗留的 "类型2"，而 回收类型 段里
    // 只有 11 个中文类型名。原生对这种悬空引用的处理在 0x1006B45D：jsoncpp 的 operator[]
    // 给不存在的键自动插一个 nullValue，0x1006B462 cmp byte [eax+8],0 读到 0 后
    // 0x1006B466 jne 不跳，落到 0x1006B46F jmp 0x1006CE26 —— 清理字符串、换下一件，
    // 整件不删。C# 让这两件不带结算规则，等价。
    Assert(manager.RecycleConfigItemCount == 313,
        "production item count is " + manager.RecycleConfigItemCount + ", expected 313");
    Assert(!manager.IsRecycleItemConfigured("玛法金骨") &&
           !manager.IsRecycleItemConfigured("丹书铁卷"),
        "可叠材料 entries naming the undefined 类型2 must carry no settlement rule");
    Assert(manager.IsRecycleItemConfigured("屠龙") &&
           manager.IsRecycleItemConfigured("雷霆战(男)★神") &&
           manager.IsRecycleItemConfigured("攻杀剑术"),
        "production snapshot lost configured item names");

    // 物品名按原字节匹配。生产里技能书那条写的是 "  施毒术"（两个前导空格），物品库真名是
    // "施毒术"。原生走 jsoncpp 的 std::map<std::string> 精确比较，不 trim；一旦 C# 去 trim，
    // 这本书就从"永远匹配不上"变成"会被删"。
    Assert(manager.IsRecycleItemConfigured("  施毒术") &&
           !manager.IsRecycleItemConfigured("施毒术"),
        "leading whitespace in a configured item name must not be trimmed");
}

/// <summary>
/// 回收链上每一个不匹配的分支都必须落到"跳过这一件"，不能落到"删了不给"。原生的五个跳过
/// 出口：物品名不在 物品种类（0x1006BD5A je 0x1006CE2B）、类型名不在 回收类型
/// （0x1006BDAF je 0x1006CE1F，可叠材料侧是 0x1006B466）、总开关命中关闭值
/// （0x1006C0FD je 0x1006CE1F）、五路产出全 &lt;= 0（0x1006CC82 jle 0x1006CE1F）。
/// 这条断言把它们一次钉死：只有既匹配到规则、又算得出正产出的那一件允许消失。
/// </summary>
static void CheckUnmatchedItemsAreNeverDeleted(string root)
{
    var (manager, runtime) = CreateManager(Path.Combine(root, "nodelete"), includeRecycle: true);

    // 只用 其他 一路结算，避开 IncGold 的上限预检、GainExp 与元宝异步往返，
    // 让断言只覆盖"匹配 → 删除"这一条链。
    WriteGbk(Path.Combine(runtime, "MyJson", "recycle.json"),
        "{\"可叠材料\":{\"悬空材料\":\"根本没有\"}," +
        "\"物品种类\":{\"该删的\":\"有产出\",\"悬空装备\":\"根本没有\"," +
        "\"零产出\":\"没产出\",\"被关掉\":\"关着的\"}," +
        "\"回收类型\":{" +
        "\"有产出\":{\"其他\":{\"v1\":10,\"v2\":200,\"值\":5}}," +
        "\"没产出\":{\"其他\":{\"v1\":10,\"v2\":200,\"值\":0}}," +
        "\"关着的\":{\"总开关\":{\"v1\":10,\"v2\":1,\"关闭值\":0}," +
        "\"其他\":{\"v1\":10,\"v2\":200,\"值\":5}}}}");
    Assert(manager.ReloadRecycleConfig(out var error),
        "non-match fixture failed to parse: " + error);
    manager.GetPlugin("YanshenCompat").IsInitialized = true;

    // 没有 0 号金币哨兵 ⇒ GetStdItemName 走 listIndex = wIndex - 1。
    var names = new[] { "该删的", "悬空装备", "零产出", "被关掉", "配置里没有", "悬空材料" };
    var stdItems = M2Share.UserEngine.StdItemList;
    stdItems.Clear();
    foreach (var name in names) stdItems.Add(new GoodItem { Name = name });

    var player = new TPlayObject();
    // 关闭值 = 0，而 GetV 未命中返回 -1（0x6E427A C7 45 FC FF FF FF FF），
    // 所以必须显式写 0 才能让 总开关 真的拦下来。
    player.SetScriptVar('V', 10, 1, 0);
    for (var i = 0; i < names.Length; i++)
        player.m_ItemList.Add(new TUserItem
        {
            wIndex = (ushort)(i + 1),
            MakeIndex = 1000 + i,
            Dura = 1,
            DuraMax = 1
        });

    var api = new YanshenApi(player, null, manager);
    Assert(api.AutoRecycle() == 1, "AutoRecycle did not run to completion over a stocked bag");

    var remaining = player.m_ItemList
        .Select(item => M2Share.UserEngine.GetStdItemName(item.wIndex))
        .ToList();
    Assert(!remaining.Contains("该删的"),
        "positive control failed: an item with a real payout was not recycled");
    foreach (var keep in new[] { "悬空装备", "零产出", "被关掉", "配置里没有", "悬空材料" })
        Assert(remaining.Contains(keep), "非匹配物品被删除: " + keep);
    Assert(remaining.Count == names.Length - 1,
        "bag lost " + (names.Length - remaining.Count) + " items, expected exactly 1");

    // 其他 是累加器：SetV(v1,v2, max(GetV,0) + 值)，一件只加一次。
    Assert(player.TryGetScriptVar('V', 10, 200, out var reputation) && reputation == 5,
        "其他 payout was not credited exactly once");
}

static (PluginManager Manager, string Runtime) CreateManager(
    string root, bool includeRecycle)
{
    var envir = Path.Combine(root, "Mir200", "Envir");
    var runtime = Path.Combine(root, "Mir200", "GS1");
    Directory.CreateDirectory(envir);
    Directory.CreateDirectory(runtime);
    WriteGbk(Path.Combine(runtime, "config.json"), "{\"高级回收\":1}");

    foreach (var relativePath in new[]
             {
                 Path.Combine("skills", "config.json"),
                 Path.Combine("skills", "skillext.json"),
                 Path.Combine("skills", "monskillext.json"),
                 Path.Combine("roles", "config.json"),
                 "眼神爆率.json",
                 "全区可爆.json",
             })
        WriteGbk(Path.Combine(runtime, "MyJson", relativePath), "{}");

    if (includeRecycle)
    {
        WriteGbk(Path.Combine(runtime, "MyJson", "recycle.json"),
            "{\"可叠材料\":{\"丹书铁卷\":\"类型2\"}," +
            "\"物品种类\":{\"屠龙\":\"类型1\",\"怒斩\":\"类型2\"}," +
            "\"回收类型\":{" +
            "\"类型1\":{\"总开关\":{\"v1\":123,\"v2\":1,\"关闭值\":100},\"金币\":100}," +
            "\"类型2\":{\"元宝\":20}}}");
    }

    var manager = new PluginManager(envir, runtime);
    manager.RegisterBuiltinPlugins();
    Assert(manager.LoadPlugin("YanshenCompat"), "YanshenCompat did not load");
    return (manager, runtime);
}

static void WriteGbk(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.GetEncoding(936));
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void InitializeGameState()
{
    M2Share.g_Config = new GameSvrConfig { nCheckBlock = 0 };
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogSystem = new MirLog();
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
