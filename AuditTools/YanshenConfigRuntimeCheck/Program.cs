using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Plugins;

try
{
    Run(args);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"YanshenConfigRuntimeCheck FAIL: {exception}");
    return 1;
}

static void Run(string[] args)
{
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
CheckUiActionQueue();

var source = args.Length > 0
    ? args[0]
    : ResolveDefaultSource();
if (!File.Exists(source)) throw new FileNotFoundException("2.07 config.json was not found", source);

var root = Path.Combine(Path.GetTempPath(), "yanshen-config-check-" + Guid.NewGuid().ToString("N"));
var mir200 = Path.Combine(root, "Mir200");
var runtime = Path.Combine(mir200, "GS1");
var envir = Path.Combine(mir200, "Envir");
var nativeConfigPath = Path.Combine(runtime, "config.json");
Directory.CreateDirectory(envir);
Directory.CreateDirectory(runtime);
File.Copy(source, nativeConfigPath);
WriteGbkJson(Path.Combine(runtime, "MyJson", "skills", "config.json"),
    SkillConfigFixture());
WriteGbkJson(Path.Combine(runtime, "MyJson", "skills", "skillext.json"),
    "{\"70\":{\"伤害加成\":1000,\"数组\":[1,2]}}");
WriteGbkJson(Path.Combine(runtime, "MyJson", "skills", "monskillext.json"),
    "{\"测试怪物\":{\"伤害加成\":1000}}");
WriteGbkJson(Path.Combine(runtime, "MyJson", "roles", "config.json"),
    RoleConfigFixture());
WriteGbkJson(Path.Combine(runtime, "MyJson", "眼神爆率.json"),
    "{\"全局爆率\":{\"固定倍数\":1000}}");
WriteGbkJson(Path.Combine(runtime, "MyJson", "全区可爆.json"), "{\"物品\":1}");
WriteGbkJson(Path.Combine(runtime, "MyJson", "recycle.json"),
    "{\"物品种类\":{\"屠龙\":\"类型1\"},\"回收类型\":{\"类型1\":{\"金币\":1}}}");
WriteGbkJson(Path.Combine(runtime, "MyJson", "items", "config.json"), ItemConfigFixture());

try
{
    var manager = new PluginManager(envir, runtime);
    manager.RegisterBuiltinPlugins();
    Assert(manager.LoadPlugin("YanshenCompat"), "built-in plugin did not enter Running state");
    Assert(Path.GetFullPath(manager.NativeConfigPath) == Path.GetFullPath(nativeConfigPath),
        "formal Mir200\\GS1 config path was not selected");
    Assert(manager.GetMyJsonConfig(YanshenMyJsonKind.SkillConfig).Count == 18 &&
           manager.GetSkillExtConfig().Count == 1 && manager.GetMonSkillExtConfig().Count == 1 &&
           manager.GetRoleConfig().Count == 23 && manager.GetDropRateConfig().Count == 1 &&
           manager.GetGuaranteedDropConfig().Count == 1 && manager.RecycleConfigItemCount == 1 &&
           manager.GetItemConfig().Count == 22,
        "YanshenCompat did not load all GS1\\MyJson configuration groups at startup");

    var original = manager.GetNativeConfig();
    Assert(original.Count == 379, $"expected 379 native keys, got {original.Count}");
    Assert(original.Values.All(value => value is not System.Text.Json.JsonElement),
        "JsonElement leaked out of the config loader");
    Assert(original["半月弯刀_A值"] is string { Length: > 0 },
        "numeric text lost its JSON string type");
    Assert(original["地面物品消失时间_时间"] is long,
        "integer parameter was not normalized to Int64");

    var nativeSwitch = (long)original["ServerSay函数"];
    var savedSwitchValue = nativeSwitch == 0 ? 1L : 0L;

    var stringNumber = TogglePanel.ToggleRow.FromConfig("半月弯刀_A值", original["半月弯刀_A值"]);
    var numericParameter = TogglePanel.ToggleRow.FromConfig("怪物爆率A_值", original["怪物爆率A_值"]);
    var switchRow = TogglePanel.ToggleRow.FromConfig("ServerSay函数", original["ServerSay函数"]);
    var plusSwitch = TogglePanel.ToggleRow.FromConfig("自定义伤害_plus", original["自定义伤害_plus"]);
    var poisonFormulaSwitch = TogglePanel.ToggleRow.FromConfig("群毒值", original["群毒值"]);
    var castSpeedParameter = TogglePanel.ToggleRow.FromConfig("主号全局法速", original["主号全局法速"]);
    Assert(!stringNumber.IsToggle, "numeric JSON string was rendered as a switch");
    Assert(!numericParameter.IsToggle, "binary-valued _参数 was rendered as a switch");
    Assert(switchRow.IsToggle && switchRow.BoolValue == (nativeSwitch != 0),
        "native switch bool value was not recognized");
    Assert(plusSwitch.IsToggle, "_plus feature was mistaken for a numeric parameter");
    Assert(poisonFormulaSwitch.IsToggle, "群毒值 should render as the 自定义群毒公式 switch");
    Assert(!castSpeedParameter.IsToggle, "主号全局法速 should render as a numeric field");
    Assert(TogglePanel.ToggleRow.FromConfig("无限背包_是否勾选", 1L).IsToggle,
        "items *_是否勾选 field was mistaken for a numeric parameter");

    using (var form = new YanshenConfigForm(manager))
    {
        var load = typeof(YanshenConfigForm).GetMethod("LoadConfig", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.LoadConfig");
        load.Invoke(form, new object[] { false });
        var rootTabs = Descendants(form).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(YanshenConfigForm.OriginalRootPages));
        var legacyTabs = Descendants(rootTabs.TabPages[1]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(YanshenConfigForm.OriginalLegacyPages));
        var seasonTabs = Descendants(rootTabs.TabPages[2]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(YanshenConfigForm.OriginalSeasonTwoPages));
        var categoryTabs = Descendants(seasonTabs.TabPages[2]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(new[] { "物品相关", "角色相关", "技能相关", "爆率相关", "预留功能" }));
        var gmTabs = Descendants(rootTabs.TabPages[0]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Count == 3 &&
            tabs.TabPages[0].Text == "gm工具1" &&
            tabs.TabPages[1].Text == "游戏内充-支付");
        Assert(form.Text == "M2超级伴侣", "2.07 window title does not match");
        Assert(form.ClientSize == new Size(1016, 680),
            $"replica design size changed to {form.ClientSize.Width}x{form.ClientSize.Height}");
        Assert(rootTabs.TabPages.Count == 4 && legacyTabs.TabPages.Count == 6 &&
               seasonTabs.TabPages.Count == 3 && categoryTabs.TabPages.Count == 5,
            "original 4/6/3/5 tab hierarchy was not reconstructed");
        Assert(form.ConfigKeyCount == 379 && form.AssignedConfigKeyCount == 379,
            $"not every native key reached the replica UI ({form.AssignedConfigKeyCount}/{form.ConfigKeyCount})");
        Assert(form.ItemConfigKeyCount == 27 && form.ItemFeatureLeafCount == 14 &&
               form.AssignedItemConfigKeyCount == 26,
            $"items config did not reach all original nodes " +
            $"(keys={form.ItemConfigKeyCount}, leaves={form.ItemFeatureLeafCount}, assigned={form.AssignedItemConfigKeyCount})");

        var itemTree = Descendants(categoryTabs.TabPages[0]).OfType<TreeView>().Single();
        var itemLeafNames = new[]
        {
            "自定义聚灵珠", "眼神扔出物品不绑定", "扣除指定持久物品", "npc卖物倍数",
            "绑定物品允许拾取", "一号元素有值绑定", "二号元素有值禁止穿戴",
            "沙巴克武器升级防破碎", "聚灵珠解除绑定", "无限背包", "拆零关小黑屋",
            "拾取物品后触发", "物品进背包触发", "金币绑定"
        };
        var itemLeaves = TreeNodes(itemTree.Nodes).Where(node => node.Nodes.Count == 0).ToArray();
        Assert(itemLeaves.Length == 14 && itemLeafNames.All(name => itemLeaves.Count(node => node.Text == name) == 1),
            "the original 14 item leaves were not reconstructed exactly once");
        Assert(itemLeaves.All(node => node.ForeColor != SystemColors.GrayText),
            "one or more original item pages remained unavailable");
        var reservedTree = Descendants(categoryTabs.TabPages[4]).OfType<TreeView>().Single();
        Assert(itemLeaves.All(node => node.Text != "大背包"),
            "native 大背包 was still coupled to the independent 无限背包 page");
        Assert(reservedTree.Nodes.Count == 0,
            "the original 2.07 script/reserved tree must remain empty");
        var roleTree = Descendants(categoryTabs.TabPages[1]).OfType<TreeView>().Single();
        var skillTree = Descendants(categoryTabs.TabPages[2]).OfType<TreeView>().Single();
        var dropTree = Descendants(categoryTabs.TabPages[3]).OfType<TreeView>().Single();
        var roleRoot = roleTree.Nodes[0];
        AssertNodeChildren(roleRoot, "role root",
            "人物角色", "宠物角色", "野生怪物", "修改角色地图", "英雄角色", "npc相关");
        var rolePerson = Child(roleRoot, "人物角色");
        AssertNodeChildren(rolePerson, "role/person",
            "配置1的反伤功能", "千分比属性", "主号被攻击触发", "主号新切割", "临时属性",
            "新永久属性", "金币上限突破", "修复飘血数值", "自定义循环函数");
        AssertNodeChildren(Child(rolePerson, "配置1的反伤功能"), "role/reflect",
            "切割也反伤", "火墙不反伤", "反伤带抗性");
        AssertNodeChildren(Child(roleRoot, "宠物角色"), "role/pet",
            "月灵不扣蓝", "月灵新伤害");
        AssertNodeChildren(Child(roleRoot, "野生怪物"), "role/monster",
            "瞬移怪物", "杀怪触发");
        AssertNodeChildren(Child(roleRoot, "修改角色地图"), "role/map",
            "自定义红名村", "沙巴克攻城范围", "沙巴克复活点");
        AssertNodeChildren(Child(roleRoot, "英雄角色"), "role/hero",
            "英雄切割", "指定英雄放技能", "英雄不自动释放技能");
        AssertNodeChildren(Child(roleRoot, "npc相关"), "role/npc", "npc自定义函数");
        var roleLeaves = TreeNodes(roleTree.Nodes)
            .Where(node => node.Nodes.Count == 0)
            .ToArray();
        Assert(roleLeaves.Length == 22,
            $"role tree must contain exactly 22 original leaves, got {roleLeaves.Length}");

        var skillRoot = skillTree.Nodes[0];
        AssertNodeChildren(skillRoot, "skill root", "人物技能", "英雄技能", "怪物技能");
        AssertNodeChildren(Child(skillRoot, "人物技能"), "skill/person",
            "诱惑之光修改", "主号分身术", "额外技能", "真隐身术修复", "主号技能加成",
            "气功波重定义", "自定义野蛮", "概率格挡", "刺杀免伤",
            "安全区禁止诱惑和圣言", "技能弹射");
        AssertNodeChildren(Child(skillRoot, "英雄技能"), "skill/hero",
            "英雄分身修复", "英雄开天修复", "合击等级修改", "英雄技能加成", "英雄技能点数增加");
        AssertNodeChildren(Child(skillRoot, "怪物技能"), "skill/monster",
            "怪物伤害触发技能特效");
        var skillLeaves = TreeNodes(skillTree.Nodes)
            .Where(node => node.Nodes.Count == 0)
            .ToArray();
        Assert(skillLeaves.Length == 17,
            $"skill tree must contain exactly 17 original leaves, got {skillLeaves.Length}");
        Assert(skillLeaves.All(node =>
                   !node.Text.StartsWith("技能扩展：", StringComparison.Ordinal) &&
                   !node.Text.StartsWith("怪物技能：", StringComparison.Ordinal)),
            "skillext/monskillext field records leaked into the fixed skill tree");
        Assert(TreeNodes(dropTree.Nodes).Any(node => node.Text == "爆率：全局爆率") &&
               TreeNodes(dropTree.Nodes).Any(node => node.Text == "全区可爆"),
            "independent drop MyJson nodes did not reach the third-page tree");
        var dropRoot = dropTree.Nodes[0];
        AssertNodeChildren(dropRoot, "drop root", "人物爆率相关");
        var playerDropRoot = Child(dropRoot, "人物爆率相关");
        AssertNodeChildren(playerDropRoot, "drop/player", "眼神新爆率");
        var newDropNodes = Child(playerDropRoot, "眼神新爆率").Nodes.Cast<TreeNode>()
            .Select(node => node.Text).ToArray();
        Assert(newDropNodes.Length == 2 &&
               newDropNodes.Contains("爆率：全局爆率", StringComparer.Ordinal) &&
               newDropNodes.Contains("全区可爆", StringComparer.Ordinal),
            "new drop-rate group did not contain both MyJson documents");
        var categoryButtonTexts = categoryTabs.TabPages.Cast<TabPage>()
            .SelectMany(page => Descendants(page).OfType<Button>())
            .Select(button => button.Text)
            .ToArray();
        foreach (var caption in new[]
                 {
                     "保存本页配置", "创建额外技能配置", "重载技能配置文件", "保存勾选配置",
                     "重载眼神爆率配置", "重载全区可爆配置", "保存全区已爆数据"
                 })
            Assert(categoryButtonTexts.Contains(caption, StringComparer.Ordinal),
                "third-page command button is missing: " + caption);

        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        Application.DoEvents();

        var snapshotPath = args.Length > 1
            ? args[1]
            : Environment.GetEnvironmentVariable("YANSHEN_GUI_SNAPSHOT");
        if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshotPath))!);
            rootTabs.SelectedIndex = 1;
            var legacySnapshots = new[]
            {
                "legacy1", "legacy2", "legacy3", "legacy4", "config1", "config2"
            };
            for (var index = 0; index < legacySnapshots.Length; index++)
            {
                legacyTabs.SelectedIndex = index;
                Application.DoEvents();
                SaveSnapshot(form, SnapshotVariant(snapshotPath, legacySnapshots[index]));
            }

            rootTabs.SelectedIndex = 2;
            seasonTabs.SelectedIndex = 0;
            Application.DoEvents();
            SaveSnapshot(form, SnapshotVariant(snapshotPath, "season1"));

            seasonTabs.SelectedIndex = 1;
            Application.DoEvents();
            SaveSnapshot(form, SnapshotVariant(snapshotPath, "season2"));

            seasonTabs.SelectedIndex = 2;
            categoryTabs.SelectedIndex = 0;
            itemTree.CollapseAll();
            itemTree.Nodes[0].Expand();
            itemTree.SelectedNode = null;
            itemTree.SelectedNode = itemTree.Nodes[0];
            itemTree.Focus();
            Application.DoEvents();
            SaveSnapshot(form, SnapshotVariant(snapshotPath, "season3_root"));

            itemTree.SelectedNode = TreeNodes(itemTree.Nodes)
                .First(node => node.Text == "无限背包");
            Application.DoEvents();
            SaveSnapshot(form, SnapshotVariant(snapshotPath, "season3_backpack"));

            rootTabs.SelectedIndex = 0;
            gmTabs.SelectedIndex = 0;
            Application.DoEvents();
            SaveSnapshot(form, SnapshotVariant(snapshotPath, "gm"));

            gmTabs.SelectedIndex = 1;
            Application.DoEvents();
            SaveSnapshot(form, SnapshotVariant(snapshotPath, "payment"));
        }

        rootTabs.SelectedIndex = 2;
        seasonTabs.SelectedIndex = 2;
        categoryTabs.SelectedIndex = 0;
        Application.DoEvents();

        var itemRoot = itemTree.Nodes[0];
        itemTree.SelectedNode = Child(itemRoot, "物品重定义");
        Application.DoEvents();
        var itemCategoryHelp = Descendants(categoryTabs.TabPages[0]).OfType<RichTextBox>()
            .Single(control => control.Visible);
        const string commonModificationWarning =
            "【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】";
        Assert(itemCategoryHelp.Text.Contains("用于物品的重定义工作！", StringComparison.Ordinal) &&
               itemCategoryHelp.Text.Contains(commonModificationWarning, StringComparison.Ordinal),
            "item-redefinition group help did not reproduce its description and modification warning");

        itemTree.SelectedNode = Child(itemRoot, "物品背包扩展");
        Application.DoEvents();
        Assert(itemCategoryHelp.Text.Length == 0,
            "item-backpack-extension group must keep the original empty help text");

        var itemParameterEditor = Descendants(categoryTabs.TabPages[0]).Single(control =>
            control.GetType().Name == "ReplicaConfigPanel");

        foreach (var leaf in itemLeaves)
        {
            itemTree.SelectedNode = leaf;
            Application.DoEvents();
            var visibleChecks = Descendants(categoryTabs.TabPages[0]).OfType<CheckBox>()
                .Where(control => control.Visible)
                .ToArray();
            Assert(visibleChecks.Length == 1 &&
                   visibleChecks[0].Text.StartsWith(leaf.Text + "(", StringComparison.Ordinal),
                $"item page did not expose its main switch: {leaf.Text}");

            var visibleEditors = Descendants(categoryTabs.TabPages[0]).OfType<TextBox>()
                .Where(control => control.Visible)
                .ToArray();
            var visibleCombos = Descendants(categoryTabs.TabPages[0]).OfType<ComboBox>()
                .Where(control => control.Visible)
                .ToArray();
            var visibleParameterLabels = Descendants(itemParameterEditor).OfType<Label>()
                .Where(control => control.Visible)
                .OrderBy(control => control.Top)
                .ThenBy(control => control.Left)
                .Select(control => control.Text)
                .ToArray();
            if (leaf.Text == "自定义聚灵珠")
            {
                var currencyOptions = visibleCombos.Single().Items.Cast<object>()
                    .Select(item => item?.ToString())
                    .ToArray();
                Assert(visibleEditors.Length == 3 && visibleCombos.Length == 1 &&
                       visibleParameterLabels.SequenceEqual(new[]
                       {
                           "收集打怪经验比例：", "使用消耗货币数量：",
                           "按比例消耗货币：", "使用消耗货币类别："
                       }, StringComparer.Ordinal) &&
                       visibleParameterLabels.All(text => text.EndsWith("：", StringComparison.Ordinal)) &&
                       currencyOptions.SequenceEqual(new[] { "元宝", "灵符", "金币" }, StringComparer.Ordinal),
                    "custom spirit-orb page labels/order or strict currency options did not match 2.07");
            }
            else if (leaf.Text == "拆零关小黑屋")
            {
                var deleteOptions = visibleCombos.Single().Items.Cast<object>()
                    .Select(item => item?.ToString())
                    .ToArray();
                Assert(visibleEditors.Length == 3 && visibleCombos.Length == 1 &&
                       visibleParameterLabels.SequenceEqual(new[]
                       {
                           "(进小黑屋)拆分值=：", "(进小黑屋)拆分值>：",
                           "小黑屋编号(@开头代表触发脚本)：", "是否删除违规物："
                       }, StringComparer.Ordinal) &&
                       visibleParameterLabels.All(text => text.EndsWith("：", StringComparison.Ordinal)) &&
                       deleteOptions.SequenceEqual(new[] { "是", "否" }, StringComparer.Ordinal),
                    "illegal-split page labels or strict delete options did not match 2.07");
            }
            else if (leaf.Text != "无限背包")
            {
                Assert(visibleEditors.Length == 0 && visibleCombos.Length == 0,
                    $"parameterless item page exposed unexpected editors: {leaf.Text}");
            }
        }

        rootTabs.SelectedIndex = 2;
        seasonTabs.SelectedIndex = 2;
        categoryTabs.SelectedIndex = 0;
        itemTree.SelectedNode = itemLeaves.Single(node => node.Text == "无限背包");
        Application.DoEvents();
        var backpackPanel = Descendants(categoryTabs.TabPages[0]).Single(control =>
            control.GetType().Name == "BackpackReplicaPanel");
        var backpackHelp = Descendants(backpackPanel).OfType<RichTextBox>().Single();
        var expectedBackpackHelp = string.Join("\n", new[]
        {
            "无限背包！",
            "1、本功能可以无限制扩展背包数量！",
            "2、但是数据作者另外存储！",
            "3、正常结束m2额外背包数据会缓存硬盘文件！",
            "4、每十分钟会定时缓存到硬盘文件！",
            "【功能缺点是：】",
            "：一、任务管理器强制结束M2会归档到十分钟前的数据！",
            "二、额外背包合区需要单独复制数据，合区工具不支持！",
            "三、建议：合区提醒玩家，额外背包数据存入仓库(48格后的物品)。然后合区后手动删除背包文件！",
            "额外数据的路径在Gs1\\MyJson\\bags\\角色名字.bin！",
            "》》》》特别注意：非大区先使用测试，稳定的开区版本，暂时不建议使用！背包数据理论上无任何上限限制，但是尽量按需设置，太大也是要占用资源的！",
            "（以上所有说明都是超过48格以后的数据，48格以前的数据不受任何影响！）",
            commonModificationWarning
        });
        Assert(backpackHelp.Text.Replace("\r\n", "\n", StringComparison.Ordinal) == expectedBackpackHelp,
            "infinite-backpack title or complete original help text did not match 2.07");
        var extraSlotsEditor = Descendants(backpackPanel).OfType<TextBox>().Single(editor =>
            editor.Location == new Point(183, 49));
        var itemSaveButton = Descendants(backpackPanel).OfType<Button>().Single(button =>
            button.Text == "保存本页配置");
        var nativeBytesBeforeItemSave = File.ReadAllBytes(nativeConfigPath);
        extraSlotsEditor.Text = "193";
        itemSaveButton.PerformClick();
        Application.DoEvents();
        Assert(manager.GetItemConfigValue("无限背包_额外格子") is long savedSlots && savedSlots == 193,
            "infinite backpack page did not save to items/config.json");
        Assert(nativeBytesBeforeItemSave.SequenceEqual(File.ReadAllBytes(nativeConfigPath)),
            "infinite backpack page modified root config.json");

        var itemToggle = typeof(YanshenConfigForm).GetMethod(
            "OnItemToggle", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.OnItemToggle");
        itemToggle.Invoke(form, new object[] { "物品进背包触发_是否勾选", true });
        itemToggle.Invoke(form, new object[] { "拾取物品后触发_是否勾选", true });
        itemToggle.Invoke(form, new object[] { "一号元素有值绑定_是否勾选", true });
        var itemEdit = typeof(YanshenConfigForm).GetMethod(
            "OnItemEditChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.OnItemEditChanged");
        itemEdit.Invoke(form, new object[] { "拆零关小黑屋_是否删除违规物", "是" });
        var saveItems = typeof(YanshenConfigForm).GetMethod(
            "SaveItemConfig", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.SaveItemConfig");
        Assert((bool)saveItems.Invoke(form, new object[] { false })!,
            "mutually exclusive item trigger settings did not save");
        Assert(manager.GetItemConfigValue("拾取物品后触发_是否勾选") is long pickupEnabled && pickupEnabled == 1 &&
               manager.GetItemConfigValue("物品进背包触发_是否勾选") is long bagEntryDisabled && bagEntryDisabled == 0 &&
               manager.GetItemConfigValue("一号元素有值绑定_是否勾选") is long elementBindEnabled && elementBindEnabled == 1 &&
               manager.GetItemConfigValue("拆零关小黑屋_是否删除违规物") is string deleteInvalid && deleteInvalid == "是",
            "item compatibility fields or mutually exclusive triggers were not saved");

        var itemsPath = Path.Combine(runtime, "MyJson", "items", "config.json");
        var nativeBeforeCategorySave = File.ReadAllBytes(nativeConfigPath);
        var itemsBeforeCategorySave = File.ReadAllBytes(itemsPath);
        var nativeEdit = typeof(YanshenConfigForm).GetMethod(
            "OnEditChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.OnEditChanged");
        var myJsonEdit = typeof(YanshenConfigForm).GetMethod(
            "OnMyJsonEditChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.OnMyJsonEditChanged");
        var myJsonToggle = typeof(YanshenConfigForm).GetMethod(
            "OnMyJsonToggle", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.OnMyJsonToggle");
        var saveCategory = typeof(YanshenConfigForm).GetMethod(
            "SaveExtensionCategory", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.SaveExtensionCategory");
        var saveAll = typeof(YanshenConfigForm).GetMethod(
            "SaveAll", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("YanshenConfigForm.SaveAll");

        nativeEdit.Invoke(form, new object[] { "地面物品消失时间_时间", "451" });
        itemEdit.Invoke(form, new object[] { "无限背包_额外格子", "194" });
        myJsonToggle.Invoke(form, new object[] { "千分比属性_是否勾选", false });
        myJsonToggle.Invoke(form, new object[] { "自定义野蛮_是否勾选", true });
        myJsonEdit.Invoke(form, new object[] { "技能扩展：70_伤害加成", "1500" });
        myJsonEdit.Invoke(form, new object[] { "技能扩展：70_数组·[0]", "3" });
        myJsonEdit.Invoke(form, new object[] { "怪物技能：测试怪物_伤害加成", "1600" });
        myJsonEdit.Invoke(form, new object[] { "全区可爆_物品", "5" });

        Assert((bool)saveCategory.Invoke(form, new object[] { "角色相关", false })!,
            "role category save failed");
        Assert(manager.GetMyJsonConfig(YanshenMyJsonKind.Role)["千分比属性_是否勾选"] is long roleDisabled &&
               roleDisabled == 0,
            "role category did not save its Role pending value");
        Assert(manager.GetMyJsonConfig(YanshenMyJsonKind.SkillConfig)["自定义野蛮_是否勾选"] is long skillStillPending &&
               skillStillPending == 0 &&
               ((Dictionary<string, object>)manager.GetMyJsonConfig(YanshenMyJsonKind.SkillExt)["70"])["伤害加成"] is long skillExtStillPending &&
               skillExtStillPending == 1000 &&
               ((Dictionary<string, object>)manager.GetMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt)["测试怪物"])["伤害加成"] is long monExtStillPending &&
               monExtStillPending == 1000,
            "role category saved another MyJson category");
        Assert(nativeBeforeCategorySave.SequenceEqual(File.ReadAllBytes(nativeConfigPath)) &&
               itemsBeforeCategorySave.SequenceEqual(File.ReadAllBytes(itemsPath)) &&
               manager.GetItemConfigValue("无限背包_额外格子") is long roleSaveSlots && roleSaveSlots == 193,
            "role category save modified native/items pending values");

        Assert((bool)saveCategory.Invoke(form, new object[] { "技能相关", false })!,
            "skill category save failed");
        Assert(manager.GetMyJsonConfig(YanshenMyJsonKind.SkillConfig)["自定义野蛮_是否勾选"] is long skillEnabled &&
               skillEnabled == 1,
            "skill category did not save its SkillConfig pending value");
        Assert(((Dictionary<string, object>)manager.GetMyJsonConfig(YanshenMyJsonKind.SkillExt)["70"])["伤害加成"] is long skillExtAfterCategory &&
               skillExtAfterCategory == 1000 &&
               ((Dictionary<string, object>)manager.GetMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt)["测试怪物"])["伤害加成"] is long monExtAfterCategory &&
               monExtAfterCategory == 1000,
            "skill category save incorrectly persisted skillext/monskillext pending values");
        Assert(nativeBeforeCategorySave.SequenceEqual(File.ReadAllBytes(nativeConfigPath)) &&
               itemsBeforeCategorySave.SequenceEqual(File.ReadAllBytes(itemsPath)) &&
               manager.GetItemConfigValue("无限背包_额外格子") is long skillSaveSlots && skillSaveSlots == 193,
            "skill category save modified native/items pending values");

        Assert((bool)saveAll.Invoke(form, new object[] { false })!,
            "SaveAll did not persist the remaining independent pending values");
        var skillDocument = manager.GetMyJsonConfig(YanshenMyJsonKind.SkillExt);
        var skill70 = (Dictionary<string, object>)skillDocument["70"];
        var monsterDocument = manager.GetMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt);
        var testMonster = (Dictionary<string, object>)monsterDocument["测试怪物"];
        Assert(skill70["伤害加成"] is long skillDamage && skillDamage == 1500 &&
               ((List<object>)skill70["数组"])[0] is long firstArrayValue && firstArrayValue == 3 &&
               testMonster["伤害加成"] is long monsterDamage && monsterDamage == 1600 &&
               manager.GetMyJsonConfig(YanshenMyJsonKind.GuaranteedDrop)["物品"] is long dropValue &&
               dropValue == 5 &&
               manager.GetNativeConfigValue("地面物品消失时间_时间") is long savedPendingTime &&
               savedPendingTime == 451 &&
               manager.GetItemConfigValue("无限背包_额外格子") is long savedPendingSlots &&
               savedPendingSlots == 194,
            "SaveAll did not preserve nested MyJson types or remaining pending values");

        var persisted = new PluginManager(envir, runtime);
        Assert(persisted.ReloadMyJsonConfig(YanshenMyJsonKind.SkillExt, out var skillReloadError),
            "saved skillext did not reload: " + skillReloadError);
        Assert(persisted.ReloadMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt, out var monsterReloadError),
            "saved monskillext did not reload: " + monsterReloadError);
        Assert(((Dictionary<string, object>)persisted.GetMyJsonConfig(YanshenMyJsonKind.SkillExt)["70"])["伤害加成"] is long persistedSkillDamage &&
               persistedSkillDamage == 1500 &&
               ((Dictionary<string, object>)persisted.GetMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt)["测试怪物"])["伤害加成"] is long persistedMonsterDamage &&
               persistedMonsterDamage == 1600,
            "skillext/monskillext did not round-trip through SaveAll");
        form.Hide();
    }

    var edits = new Dictionary<string, object>
    {
        ["ServerSay函数"] = savedSwitchValue,
        ["半月弯刀_A值"] = "3.5",
        ["地面物品消失时间_时间"] = 450L,
    };
    Assert(manager.ApplyNativeConfigChanges(edits, out var saveError), "save failed: " + saveError);
    Assert(manager.GetNativeConfigValue("ServerSay函数") is long enabled && enabled == savedSwitchValue,
        "saved switch was not hot-applied");

    var savedBytes = File.ReadAllBytes(nativeConfigPath);
    Assert(Contains(savedBytes, Encoding.GetEncoding(936).GetBytes("半月弯刀_A值")),
        "saved config is not GBK encoded");
    var savedText = Encoding.GetEncoding(936).GetString(savedBytes);
    Assert(!savedText.Contains("\\u534a\\u6708", StringComparison.OrdinalIgnoreCase),
        "native Chinese keys were rewritten as Unicode escape sequences");
    Assert(!File.Exists(nativeConfigPath + ".tmp"), "temporary file remained after save");

    var reloaded = new PluginManager(envir, runtime);
    var roundTrip = reloaded.GetNativeConfig();
    Assert(roundTrip.Count == 379, "round-trip changed the native key count");
    Assert(roundTrip["ServerSay函数"] is long savedSwitch && savedSwitch == savedSwitchValue,
        "switch type/value did not round-trip");
    Assert(roundTrip["半月弯刀_A值"] is string savedNumberText && savedNumberText == "3.5",
        "string parameter type/value did not round-trip");
    Assert(roundTrip["地面物品消失时间_时间"] is long savedTime && savedTime == 450,
        "integer parameter type/value did not round-trip");
    Assert(original.Keys.All(roundTrip.ContainsKey), "an untouched native key was lost");

    File.WriteAllText(nativeConfigPath, "{broken", Encoding.GetEncoding(936));
    reloaded.LoadNativeConfig();
    Assert(reloaded.GetNativeConfig().Count == 379,
        "malformed hot reload cleared the last valid runtime snapshot");

    Console.WriteLine("YanshenConfigRuntimeCheck PASS keys=379 tabs=4/6/3/5 size=1016x680 types=preserved encoding=GBK hotApply=yes malformedReload=preserved myJson=startup+gui+nested-save");
}
finally
{
    Directory.Delete(root, true);
}
}

static bool Contains(byte[] source, byte[] value)
{
    for (var i = 0; i <= source.Length - value.Length; i++)
        if (source.AsSpan(i, value.Length).SequenceEqual(value)) return true;
    return value.Length == 0;
}

static IEnumerable<Control> Descendants(Control root)
{
    foreach (Control child in root.Controls)
    {
        yield return child;
        foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

static IEnumerable<TreeNode> TreeNodes(TreeNodeCollection nodes)
{
    foreach (TreeNode node in nodes)
    {
        yield return node;
        foreach (var descendant in TreeNodes(node.Nodes)) yield return descendant;
    }
}

static TreeNode Child(TreeNode parent, string text) =>
    parent.Nodes.Cast<TreeNode>().SingleOrDefault(node =>
        string.Equals(node.Text, text, StringComparison.Ordinal))
    ?? throw new InvalidOperationException($"tree node is missing: {parent.Text}/{text}");

static void AssertNodeChildren(TreeNode parent, string path, params string[] expected)
{
    var actual = parent.Nodes.Cast<TreeNode>().Select(node => node.Text).ToArray();
    Assert(actual.SequenceEqual(expected, StringComparer.Ordinal),
        $"{path} order mismatch: expected [{string.Join(", ", expected)}], " +
        $"got [{string.Join(", ", actual)}]");
}

static string SnapshotVariant(string path, string suffix)
{
    var directory = Path.GetDirectoryName(path) ?? string.Empty;
    var name = Path.GetFileNameWithoutExtension(path);
    return Path.Combine(directory, name + "_" + suffix + Path.GetExtension(path));
}

static void SaveSnapshot(Form form, string path)
{
    using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    Assert(bitmap.GetPixel(0, 0).ToArgb() == Color.FromArgb(102, 203, 234).ToArgb(),
        "replica shell color changed");
    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
}

static string ResolveDefaultSource()
{
    // The operator Desktop tree the candidates below name is not in the repo and
    // is no longer on disk, so the check died in FileNotFoundException before any
    // assertion ran. staging/ys207_original_capture is the archived capture of
    // that same GS1/config.json, so it is searched first, walking up from both the
    // tool's own bin directory and the working directory.
    foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            var captured = Path.Combine(dir.FullName, "staging",
                "ys207_original_capture", "Mir200", "GS1", "config.json");
            if (File.Exists(captured))
                return captured;
        }
    }

    var candidates = new[]
    {
        @"C:\Users\Administrator\Desktop\三龙位\仙缘复工0.3破白猪+眼神\mud2.0\Mir200\Gs1\config.json",
        @"C:\Users\Administrator\Desktop\三龙位\仙缘复工0.3破白猪+眼神\云起热更区\mud2.0\Mir200\Gs1\config.json",
        @"C:\Users\Administrator\Desktop\三龙位\仙缘复工0.3破白猪+眼神\mud2.0\Mir200\Gs12\config.json"
    };

    foreach (var candidate in candidates)
        if (File.Exists(candidate))
            return candidate;

    const string root = @"C:\Users\Administrator\Desktop\三龙位\仙缘复工0.3破白猪+眼神";
    if (!Directory.Exists(root))
        return candidates[0];

    foreach (var path in Directory.EnumerateFiles(root, "config.json", SearchOption.AllDirectories))
    {
        var normalized = path.Replace('/', '\\');
        if (normalized.EndsWith(@"\mud2.0\Mir200\Gs1\config.json", StringComparison.OrdinalIgnoreCase))
            return path;
    }

    return candidates[0];
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CheckUiActionQueue()
{
    var previousReady = M2Share.boStartReady;
    var engine = new UserEngine();
    try
    {
        M2Share.boStartReady = true;
        var request = engine.InvokeFromUiAsync(() => 42);
        var process = typeof(UserEngine).GetMethod(
            "ProcessUiActions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("UserEngine.ProcessUiActions");
        process.Invoke(engine, null);
        Assert(request.GetAwaiter().GetResult() == 42,
            "GM UI action did not execute on the UserEngine queue");

        var lateMutation = 0;
        var timedOut = engine.InvokeFromUiAsync(() => lateMutation = 1, 250);
        try
        {
            timedOut.GetAwaiter().GetResult();
            throw new InvalidOperationException("GM UI request did not time out");
        }
        catch (TimeoutException)
        {
        }
        process.Invoke(engine, null);
        Assert(lateMutation == 0,
            "a timed-out GM UI request executed later");
    }
    finally
    {
        engine.Stop();
        M2Share.boStartReady = previousReady;
    }
}

static void WriteGbkJson(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.GetEncoding(936));
}

static string RoleConfigFixture() => """
{
  "角色相关选项": "2.07固定角色树审计",
  "切割也反伤_是否勾选": 0,
  "火墙不反伤_是否勾选": 1,
  "反伤带抗性_是否勾选": 0,
  "千分比属性_是否勾选": 1,
  "主号被攻击触发_是否勾选": 1,
  "主号新切割_是否勾选": 1,
  "临时属性_是否勾选": 1,
  "新永久属性_是否勾选": 1,
  "金币上限突破_是否勾选": 1,
  "修复飘血数值_是否勾选": 0,
  "自定义循环函数_是否勾选": 1,
  "月灵不扣蓝_是否勾选": 1,
  "月灵新伤害_是否勾选": 1,
  "瞬移怪物_是否勾选": 0,
  "杀怪触发_是否勾选": 1,
  "自定义红名村_是否勾选": 0,
  "沙巴克攻城范围_是否勾选": 0,
  "沙巴克复活点_是否勾选": 0,
  "英雄切割_是否勾选": 0,
  "指定英雄放技能_是否勾选": 0,
  "英雄不自动释放技能_是否勾选": 0,
  "npc自定义函数_是否勾选": 0
}
""";

static string SkillConfigFixture() => """
{
  "技能相关选项": "2.07固定技能树审计",
  "诱惑之光修改_是否勾选": 1,
  "主号分身术_是否勾选": 1,
  "额外技能_是否勾选": 1,
  "真隐身术修复_是否勾选": 1,
  "主号技能加成_是否勾选": 1,
  "气功波重定义_是否勾选": 0,
  "自定义野蛮_是否勾选": 0,
  "概率格挡_是否勾选": 1,
  "刺杀免伤_是否勾选": 0,
  "安全区禁止诱惑和圣言_是否勾选": 1,
  "技能弹射_是否勾选": 1,
  "英雄分身修复_是否勾选": 0,
  "英雄开天修复_是否勾选": 0,
  "合击等级修改_是否勾选": 0,
  "英雄技能加成_是否勾选": 0,
  "英雄技能点数增加_是否勾选": 0,
  "怪物伤害触发技能特效_是否勾选": 1
}
""";

static string ItemConfigFixture() => """
{
  "npc卖物倍数_是否勾选": 0,
  "绑定物品允许拾取_是否勾选": 0,
  "拆零关小黑屋_超过数字": 9999,
  "拆零关小黑屋_等于数字": 0,
  "拆零关小黑屋_是否勾选": 1,
  "拆零关小黑屋_小黑屋": "SD000",
  "金币绑定_是否勾选": 0,
  "扣除指定持久物品_是否勾选": 0,
  "拾取物品后触发_是否勾选": 0,
  "无限背包_变量v1": 10,
  "无限背包_变量v2": 1,
  "无限背包_额外格子": 192,
  "无限背包_是否勾选": 1,
  "无限背包_是否固定": "固定格子",
  "物品进背包触发_是否勾选": 0,
  "物品相关选项": "物品自定义配置，技术不够的别乱改",
  "眼神扔出物品不绑定_是否勾选": 0,
  "自定义聚灵珠_是否勾选": 0,
  "自定义聚灵珠_收集比例": 100,
  "自定义聚灵珠_消耗比例": 0,
  "自定义聚灵珠_消耗类别": "金币",
  "自定义聚灵珠_消耗数量": 10
}
""";

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
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
