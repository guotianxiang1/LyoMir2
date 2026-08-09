using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GameSvr.PasEngine;
using SystemModule;

namespace GameSvr.Plugins
{
    internal static class YanshenUiFont
    {
        internal static Font Create(string family, float pointSize,
            FontStyle style = FontStyle.Regular) =>
            new(family, pointSize * 96f / 72f, style, GraphicsUnit.Pixel);

        internal static Font CreateTextRenderer(string family, float pointSize, int deviceDpi,
            FontStyle style = FontStyle.Regular) =>
            new(family, pointSize * 96f / 72f * 96f / Math.Max(96, deviceDpi),
                style, GraphicsUnit.Pixel);

        internal static void ConfigureCompatibleText(Control root)
        {
            if (root is Label label)
                label.UseCompatibleTextRendering = true;
            else if (root is CheckBox checkBox)
                checkBox.UseCompatibleTextRendering = true;

            foreach (Control child in root.Controls)
                ConfigureCompatibleText(child);
        }
    }

    internal static class YanshenRichText
    {
        private const int TwipsPerLogicalPixel = 15;

        internal static void SetText(RichTextBox control, string text, int lineHeight = 13)
        {
            control.Text = text;
            var paragraph = @"\pard";
            var exactSpacing = paragraph + $@"\sl-{lineHeight * TwipsPerLogicalPixel}\slmult0";
            control.Rtf = control.Rtf.Replace(paragraph, exactSpacing, StringComparison.Ordinal);
            control.Select(0, 0);
        }
    }

    /// <summary>
    /// Screenshot-backed reconstruction of the 2.0.7/2.0.8 M2 companion panel.
    /// The shell and page hierarchy follow the original 1016 x 680 dialog while
    /// every config.json key remains connected to the live compatibility engine.
    /// </summary>
    public sealed class YanshenConfigForm : Form
    {
        public static readonly string[] OriginalRootPages =
        {
            "gm功能模块", "眼神(旧)和盘古功能", "眼神第二季", "更新与使用说明"
        };

        public static readonly string[] OriginalLegacyPages =
        {
            "盘古1", "盘古2", "盘古3", "盘古4", "配置1", "配置2"
        };

        public static readonly string[] OriginalSeasonTwoPages =
        {
            "眼神2(第1页)", "眼神2(第2页)", "眼神2(第3页)"
        };

        private static readonly YanshenMyJsonKind[] EditableMyJsonKinds =
        {
            YanshenMyJsonKind.Role,
            YanshenMyJsonKind.SkillConfig,
            YanshenMyJsonKind.SkillExt,
            YanshenMyJsonKind.MonsterSkillExt,
            YanshenMyJsonKind.DropRate,
            YanshenMyJsonKind.GuaranteedDrop,
        };

        private static readonly Color ShellColor = Color.FromArgb(102, 203, 234);
        private static readonly Font BodyFont = YanshenUiFont.Create("SimSun", 9f);
        private static readonly Font CompactBodyFont = YanshenUiFont.Create("SimSun", 8.5f);
        private const string RemainingStatusText = "换绑次数:0";
        private const string PathStatusText = "需要使用高级功能请联系Q群：672685800";
        private const string RuntimeStatusText = "基础版，永久免费，具体使用方法看插件公告内容";
        private const string AnnouncementUrl = "http://106.54.1.65:808/gg/index.php";
        private static readonly string AnnouncementHeader =
            new string('=', 34) + "公告" + new string('=', 38) + "\r\n";

        private readonly PluginManager _manager;
        private readonly Dictionary<string, object> _pending = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _pendingItems = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TogglePanel.ToggleRow> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TogglePanel.ToggleRow> _itemRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TogglePanel.ToggleRow> _myJsonRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MyJsonValueBinding> _myJsonBindings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _pendingMyJson = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReplicaConfigEditor> _pageEditors = new(StringComparer.Ordinal);
        private readonly List<IReplicaConfigEditor> _allEditors = new();
        private readonly Dictionary<string, ExtensionCategoryView> _extensionViews = new(StringComparer.Ordinal);

        private readonly ClassicTabControl _rootTabs;
        private readonly ClassicButton _embedButton;
        private readonly ClassicButton _saveButton;
        private readonly Label _remainingStatus;
        private readonly Label _runtimeStatus;
        private readonly Label _pathStatus;
        private bool _loaded;
        private int _assignedConfigKeyCount;

        public int ConfigKeyCount => _rows.Count;
        public int AssignedConfigKeyCount => _assignedConfigKeyCount;
        public int ItemConfigKeyCount => _itemRows.Count;
        public int ItemFeatureLeafCount => _extensionViews.TryGetValue("物品相关", out var itemView)
            ? itemView.ItemLeafCount
            : 0;
        public int AssignedItemConfigKeyCount => _extensionViews.TryGetValue("物品相关", out var itemView)
            ? itemView.BoundItemKeyCount
            : 0;

        public YanshenConfigForm(PluginManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = ShellColor;
            ClientSize = new Size(1016, 680);
            DoubleBuffered = true;
            Font = BodyFont;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "M2超级伴侣";

            var title = new Label
            {
                BackColor = ShellColor,
                Bounds = new Rectangle(4, 4, 1008, 25),
                Font = YanshenUiFont.Create("Microsoft YaHei UI", 11f),
                Text = "M2超级伴侣",
                TextAlign = ContentAlignment.MiddleCenter,
            };
            title.MouseDown += BeginWindowDrag;
            Controls.Add(title);

            var surface = new Panel
            {
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(6, 29, 1002, 644),
            };
            Controls.Add(surface);

            surface.Controls.Add(new Label
            {
                AutoSize = false,
                Bounds = new Rectangle(334, 12, 322, 22),
                Text = "点击嵌入m2面板后，可以通过m2的设置面板重新呼唤窗体",
                TextAlign = ContentAlignment.MiddleLeft,
            });

            _embedButton = MakeButton("嵌入M2【设置面板】（点击设置面板开启）", new Rectangle(660, 11, 249, 21));
            _embedButton.Click += (_, _) =>
            {
                var mainForm = Owner as global::GameSvr.MainForm ?? global::GameSvr.MainForm.Instance;
                if (mainForm == null)
                {
                    ShowStatus("M2 主窗口尚未初始化");
                    return;
                }
                var added = mainForm.EnableYanshenSettingsEntry();
                ShowStatus(added ? "已嵌入 M2 设置面板" : "M2 设置面板入口已经存在");
            };
            surface.Controls.Add(_embedButton);

            var minimizeButton = MakeButton("最小化至托盘", new Rectangle(918, 11, 82, 21));
            minimizeButton.Font = CompactBodyFont;
            minimizeButton.Click += (_, _) =>
            {
                var mainForm = Owner as global::GameSvr.MainForm ?? global::GameSvr.MainForm.Instance;
                if (mainForm != null)
                    mainForm.MinimizeYanshenToTray();
                else
                    Hide();
            };
            surface.Controls.Add(minimizeButton);

            _rootTabs = MakeTabs(new Rectangle(24, 33, 977, 527));
            _rootTabs.Padding = new Point(6, 3);
            BuildRootPages();
            surface.Controls.Add(_rootTabs);

            surface.Controls.Add(new Label
            {
                AutoSize = false,
                Bounds = new Rectangle(19, 593, 485, 37),
                Text = "注意：删除key可运行基础版，勾选功能未显示已启动可进群获取\r\n授权。或进入网页：pay.510youxi.com自行获取。",
            });

            _remainingStatus = MakeStatusLabel(new Rectangle(522, 566, 99, 26));
            _remainingStatus.Text = RemainingStatusText;
            surface.Controls.Add(_remainingStatus);

            _saveButton = MakeButton("勾选保存", new Rectangle(522, 609, 96, 22));
            _saveButton.Click += (_, _) => SaveAll(true);
            surface.Controls.Add(_saveButton);

            _pathStatus = MakeStatusLabel(new Rectangle(635, 566, 359, 26));
            _pathStatus.Text = PathStatusText;
            surface.Controls.Add(_pathStatus);

            _runtimeStatus = MakeStatusLabel(new Rectangle(635, 605, 359, 26));
            _runtimeStatus.Text = RuntimeStatusText;
            surface.Controls.Add(_runtimeStatus);

            Shown += (_, _) =>
            {
                if (_loaded) return;
                LoadConfig(true);
                _embedButton.Select();
            };
            FormClosing += OnFormClosingWithChanges;
            YanshenUiFont.ConfigureCompatibleText(this);
            ResumeLayout(false);
        }

        private void BuildRootPages()
        {
            var gmPage = MakePage(OriginalRootPages[0]);
            gmPage.Padding = new Padding(4, 9, 4, 4);
            gmPage.Controls.Add(BuildGmPages());
            _rootTabs.TabPages.Add(gmPage);

            var legacyPage = MakePage(OriginalRootPages[1]);
            legacyPage.Padding = new Padding(4, 9, 4, 4);
            legacyPage.Controls.Add(BuildLegacyPages());
            _rootTabs.TabPages.Add(legacyPage);

            var seasonTwoPage = MakePage(OriginalRootPages[2]);
            seasonTwoPage.Padding = new Padding(4, 9, 4, 4);
            seasonTwoPage.Controls.Add(BuildSeasonTwoPages());
            _rootTabs.TabPages.Add(seasonTwoPage);

            var helpPage = MakePage(OriginalRootPages[3]);
            helpPage.Padding = new Padding(4, 9, 4, 4);
            helpPage.Controls.Add(BuildHelpPage());
            _rootTabs.TabPages.Add(helpPage);
        }

        private Control BuildLegacyPages()
        {
            var frame = MakeInnerFrame();
            frame.Padding = new Padding(11, 10, 0, 14);
            var tabs = MakeTabs(Rectangle.Empty);
            tabs.Padding = new Point(3, 3);
            tabs.FillRightOverflow = 1;
            tabs.Dock = DockStyle.Fill;
            foreach (var pageName in OriginalLegacyPages)
            {
                var page = MakePage(pageName);
                Control editor;
                if (string.Equals(pageName, "盘古1", StringComparison.Ordinal))
                {
                    var fixedEditor = new LegacyOneReplicaPanel
                    {
                        Bounds = new Rectangle(5, 7, 935, 424),
                    };
                    HookEditor(fixedEditor);
                    _pageEditors[pageName] = fixedEditor;
                    editor = fixedEditor;
                }
                else if (string.Equals(pageName, "盘古2", StringComparison.Ordinal))
                {
                    var fixedEditor = new Legacy2ReplicaPanel
                    {
                        Bounds = new Rectangle(4, 7, 935, 424),
                    };
                    HookEditor(fixedEditor);
                    _pageEditors[pageName] = fixedEditor;
                    editor = fixedEditor;
                }
                else if (string.Equals(pageName, "盘古3", StringComparison.Ordinal))
                {
                    var fixedEditor = new Legacy3ReplicaPanel
                    {
                        Bounds = new Rectangle(6, 8, 935, 424),
                    };
                    HookEditor(fixedEditor);
                    _pageEditors[pageName] = fixedEditor;
                    editor = fixedEditor;
                }
                else if (string.Equals(pageName, "盘古4", StringComparison.Ordinal))
                {
                    var equipmentEditor = new EquipmentReplicaPanel
                    {
                        Bounds = new Rectangle(4, 9, 935, 424),
                    };
                    HookEditor(equipmentEditor);
                    _pageEditors[pageName] = equipmentEditor;
                    editor = equipmentEditor;
                }
                else if (string.Equals(pageName, "配置1", StringComparison.Ordinal))
                {
                    var fixedEditor = new Config1ReplicaPanel
                    {
                        Bounds = new Rectangle(5, 8, 935, 424),
                    };
                    HookEditor(fixedEditor);
                    _pageEditors[pageName] = fixedEditor;
                    editor = fixedEditor;
                }
                else if (string.Equals(pageName, "配置2", StringComparison.Ordinal))
                {
                    var fixedEditor = new Config2ReplicaPanel
                    {
                        Bounds = new Rectangle(5, 8, 935, 424),
                    };
                    HookEditor(fixedEditor);
                    _pageEditors[pageName] = fixedEditor;
                    editor = fixedEditor;
                }
                else
                {
                    editor = CreateConfigEditor(pageName);
                }
                page.Controls.Add(editor);
                tabs.TabPages.Add(page);
            }
            frame.Controls.Add(tabs);
            return frame;
        }

        private Control BuildSeasonTwoPages()
        {
            var frame = MakeInnerFrame();
            frame.Padding = new Padding(15, 10, 0, 5);
            var tabs = MakeTabs(Rectangle.Empty);
            tabs.ContentLeftCompensation = 7;
            tabs.FillRightOverflow = 4;
            tabs.Dock = DockStyle.Fill;

            for (var index = 0; index < 2; index++)
            {
                var pageName = OriginalSeasonTwoPages[index];
                var page = MakePage(pageName);
                IReplicaConfigEditor fixedEditor = index == 0
                    ? new SeasonOneReplicaPanel()
                    : new SeasonTwoReplicaPanel(_manager);
                ((Control)fixedEditor).Bounds = index == 0
                    ? new Rectangle(11, 11, 936, 430)
                    : new Rectangle(9, 9, 936, 430);
                HookEditor(fixedEditor);
                _pageEditors[pageName] = fixedEditor;
                page.Controls.Add((Control)fixedEditor);
                tabs.TabPages.Add(page);
            }

            var extensionPage = MakePage(OriginalSeasonTwoPages[2]);
            extensionPage.Controls.Add(BuildExtensionPages());
            tabs.TabPages.Add(extensionPage);
            frame.Controls.Add(tabs);
            return frame;
        }

        private Control BuildExtensionPages()
        {
            var frame = new Panel
            {
                BackColor = SystemColors.Control,
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 13, 1, 0),
            };
            var tabs = MakeTabs(Rectangle.Empty);
            tabs.ContentLeftCompensation = 12;
            tabs.Padding = new Point(6, 3);
            tabs.Dock = DockStyle.Fill;
            foreach (var category in YanshenPageCatalog.ExtensionCategoryOrder)
            {
                var page = MakePage(string.Equals(category, "脚本相关", StringComparison.Ordinal)
                    ? "预留功能"
                    : category);
                page.Padding = new Padding(17, 8, 4, 0);
                var view = new ExtensionCategoryView(category)
                {
                    Dock = DockStyle.Fill,
                    BackColor = SystemColors.Control,
                };
                if (string.Equals(category, "物品相关", StringComparison.Ordinal))
                {
                    HookMixedEditor(view.Editor);
                    HookItemEditor(view.BackpackEditor);
                    view.BackpackEditor.SaveRequested += () => SaveItemConfig(true);
                }
                else if (string.Equals(category, "角色相关", StringComparison.Ordinal) ||
                         string.Equals(category, "技能相关", StringComparison.Ordinal) ||
                         string.Equals(category, "爆率相关", StringComparison.Ordinal))
                {
                    view.Editor.Columns = 1;
                    HookMixedMyJsonEditor(view.Editor);
                }
                else
                {
                    HookEditor(view.Editor);
                }
                view.MainToggle += (key, enabled) =>
                {
                    if (_itemRows.ContainsKey(key)) OnItemToggle(key, enabled);
                    else if (_myJsonRows.ContainsKey(key)) OnMyJsonToggle(key, enabled);
                    else OnToggle(key, enabled);
                };
                view.MainActionRequested += () => ReloadExtensionCategory(category);
                view.SaveRequested += () => SaveExtensionCategory(category, true);
                view.ReloadRequested += action => ReloadExtensionCategory(category, action);
                view.CreateSkillRequested += CreateOrReloadSkillExtensionConfig;
                view.SaveDropDataRequested += SaveGuaranteedDropRuntimeData;
                _extensionViews[category] = view;
                page.Controls.Add(view);
                tabs.TabPages.Add(page);
            }
            frame.Controls.Add(tabs);
            return frame;
        }

        private Control BuildGmPages()
        {
            var frame = MakeInnerFrame();
            frame.Padding = new Padding(11, 10, 0, 1);
            var tabs = MakeTabs(Rectangle.Empty);
            tabs.ContentLeftCompensation = 3;
            tabs.FillRightOverflow = 5;
            tabs.Dock = DockStyle.Fill;

            var tools = MakePage("gm工具1");
            BuildGmToolsPage(tools);
            AddInsetPageFrame(tools);
            tools.Padding = new Padding(8, 7, 0, 1);
            tabs.TabPages.Add(tools);

            var payment = MakePage("游戏内充-支付");
            const string paymentCaptionText = "暂时未开发高级功能，如果想用可以先在地址注册账号：";
            var paymentCaption = new YanshenSingleLineLabel
            {
                AutoSize = false,
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(37, 24, 307, 24),
                Font = BodyFont,
                Text = paymentCaptionText,
                TextAlign = ContentAlignment.MiddleLeft,
                UseCompatibleTextRendering = true,
            };
            payment.Controls.Add(paymentCaption);
            payment.Controls.Add(new TextBox
            {
                Bounds = new Rectangle(344, 25, 294, 23),
                AutoSize = false,
                ReadOnly = true,
                TabStop = false,
                Text = "https://pay.1234500000.com/14512",
            });
            AddInsetPageFrame(payment);
            payment.Padding = new Padding(7, 8, 0, 0);
            tabs.TabPages.Add(payment);

            tabs.SelectedIndexChanged += (_, _) =>
            {
                if (tabs.SelectedIndex == 1 && IsHandleCreated)
                    BeginInvoke(new Action(() => _embedButton.Select()));
            };

            var reserved = MakePage("预留3");
            AddInsetPageFrame(reserved);
            reserved.Padding = new Padding(7, 8, 0, 0);
            tabs.TabPages.Add(reserved);
            frame.Controls.Add(tabs);
            return frame;
        }

        private void BuildGmToolsPage(Control page)
        {
            static TextBox FixedTextBox(Rectangle bounds, string text = "", bool readOnly = false) => new()
            {
                AutoSize = false,
                BackColor = Color.White,
                Bounds = bounds,
                ReadOnly = readOnly,
                Text = text,
            };

            static Control SingleLineLabel(string text, Rectangle bounds, Color color)
            {
                return new YanshenSingleLineLabel
                {
                    AutoSize = false,
                    BackColor = SystemColors.Control,
                    Bounds = bounds,
                    Font = BodyFont,
                    ForeColor = color,
                    Text = text,
                    TextAlign = ContentAlignment.MiddleLeft,
                    UseCompatibleTextRendering = true,
                };
            }

            var refreshQuest = MakeButton("刷新任务", new Rectangle(45, 40, 74, 21));
            refreshQuest.Click += async (_, _) => await RunGameActionAsync(() =>
            {
                if (M2Share.PasEngine == null) return "Pascal 脚本引擎尚未初始化";
                M2Share.PasEngine.ClearCache();
                M2Share.PasEngine.LoadNpcScriptMap();
                M2Share.PasEngine.LoadMapQuestMap();
                return "任务脚本与映射已重新加载";
            });
            page.Controls.Add(refreshQuest);
            page.Controls.Add(SingleLineLabel(
                "重载Mir200\\Envir\\CommonScripts文件夹下所有脚本，主要目的是重载LogonQuest.pas、LogonQuest.txt",
                new Rectangle(146, 40, 488, 23), Color.Red));

            var refreshNpc = MakeButton("刷新npc", new Rectangle(45, 85, 74, 21));
            refreshNpc.Click += async (_, _) => await RunGameActionAsync(() =>
            {
                M2Share.LocalDB?.ReLoadMerchants();
                M2Share.UserEngine.ReloadMerchantList();
                M2Share.UserEngine.ReloadNpcList();
                M2Share.PasEngine?.LoadNpcScriptMap();
                return "交易 NPC 与管理 NPC 脚本已重新加载";
            });
            page.Controls.Add(refreshNpc);
            var npcNote = MakeLabel("重载Mir200\\Envir\\PsNpcscripts下的所有npc脚本！", new Rectangle(146, 85, 488, 23));
            npcNote.ForeColor = Color.Red;
            page.Controls.Add(npcNote);

            page.Controls.Add(MakeLabel("输入玩家名字：", new Rectangle(47, 145, 110, 23)));
            var playerName = FixedTextBox(new Rectangle(154, 145, 138, 23));
            page.Controls.Add(playerName);
            var playerCheck = MakeButton("测试玩家是否存在", new Rectangle(47, 175, 132, 21));
            playerCheck.Click += async (_, _) =>
            {
                var name = playerName.Text.Trim();
                await RunGameActionAsync(() =>
                {
                    if (name.Length == 0) return "请输入玩家名字";
                    var player = M2Share.UserEngine.GetPlayObjectEx(name);
                    return player == null
                        ? $"玩家 {name} 不在线"
                        : $"玩家 {player.m_sCharName} 在线，地图 {player.m_sMapName}";
                });
            };
            page.Controls.Add(playerCheck);

            page.Controls.Add(MakeLabel("参数1：", new Rectangle(332, 117, 55, 23)));
            var parameter = FixedTextBox(new Rectangle(323, 145, 89, 23), "1");
            page.Controls.Add(parameter);
            page.Controls.Add(MakeLabel("当前值：", new Rectangle(515, 117, 65, 23)));
            var currentValue = FixedTextBox(new Rectangle(486, 145, 88, 23), readOnly: true);
            page.Controls.Add(currentValue);
            var getG = MakeButton("GetG", new Rectangle(327, 175, 61, 21));
            getG.Click += async (_, _) =>
            {
                if (!TryReadGmVariableIndex(parameter.Text, out var index, out var error))
                {
                    ShowStatus(error);
                    return;
                }
                await RunGameActionAsync(() =>
                {
                    if (M2Share.PasEngine == null) return "Pascal 脚本引擎尚未初始化";
                    var value = M2Share.PasEngine.Api.GetGlobalVar(1, index).AsInt();
                    BeginInvoke(new Action(() => currentValue.Text = value.ToString(CultureInfo.InvariantCulture)));
                    return $"G(1,{index}) = {value}";
                });
            };
            page.Controls.Add(getG);
            var getV = MakeButton("GetV", new Rectangle(516, 175, 59, 21));
            getV.Click += async (_, _) => await ReadPlayerVariableAsync(
                playerName.Text, parameter.Text, currentValue);
            page.Controls.Add(getV);

            var setValue = FixedTextBox(new Rectangle(49, 205, 273, 23));
            page.Controls.Add(setValue);
            var setG = MakeButton("SetG", new Rectangle(327, 205, 61, 21));
            setG.Click += async (_, _) => await WriteGlobalVariableAsync(parameter.Text, setValue.Text, currentValue);
            page.Controls.Add(setG);
            var setV = MakeButton("SetV", new Rectangle(516, 205, 59, 21));
            setV.Click += async (_, _) => await WritePlayerVariableAsync(
                playerName.Text, parameter.Text, setValue.Text, currentValue);
            page.Controls.Add(setV);
            var broadcastText = FixedTextBox(new Rectangle(49, 236, 461, 23), "有重要更新想要告知全区人民");
            page.Controls.Add(broadcastText);
            var broadcast = MakeButton("发送给所有人", new Rectangle(521, 236, 111, 21));
            broadcast.Click += async (_, _) =>
            {
                var message = broadcastText.Text.Trim();
                await RunGameActionAsync(() =>
                {
                    if (message.Length == 0) return "广播内容不能为空";
                    M2Share.UserEngine.SendBroadCastMsg(message, MsgType.Notice);
                    return "全服消息已发送";
                });
            };
            page.Controls.Add(broadcast);

            page.Controls.Add(MakeLabel("test：", new Rectangle(49, 262, 48, 23)));
            page.Controls.Add(FixedTextBox(new Rectangle(97, 262, 60, 23)));
            page.Controls.Add(MakeLabel("由黑色逐渐变纯白色，百度可查", new Rectangle(167, 262, 180, 23)));
            page.Controls.Add(MakeLabel("背景色(0-255):", new Rectangle(358, 262, 92, 23)));
            page.Controls.Add(FixedTextBox(new Rectangle(450, 262, 46, 23), "56"));
            page.Controls.Add(SingleLineLabel("字体色(0-255):", new Rectangle(503, 262, 84, 23), Color.Black));
            page.Controls.Add(FixedTextBox(new Rectangle(586, 262, 46, 23), "255"));

            var debug = MakeButton("眼神调试专用按钮", new Rectangle(49, 288, 110, 21));
            debug.Click += (_, _) =>
            {
                M2Share.MainOutMessage("[眼神调试] GUI 调试按钮已触发");
                ShowStatus("调试记录已写入服务端日志");
            };
            page.Controls.Add(debug);

            page.Controls.Add(MakeLabel("物品名：", new Rectangle(217, 288, 63, 23)));
            var itemName = FixedTextBox(new Rectangle(280, 288, 92, 23), "经验");
            page.Controls.Add(itemName);
            page.Controls.Add(MakeLabel("数量:", new Rectangle(380, 288, 36, 23)));
            var itemCount = FixedTextBox(new Rectangle(416, 288, 92, 23), "1");
            page.Controls.Add(itemCount);
            var giveItem = MakeButton("给玩家发送物品", new Rectangle(522, 288, 110, 21));
            giveItem.Click += async (_, _) => await GiveItemAsync(
                playerName.Text, itemName.Text, itemCount.Text);
            page.Controls.Add(giveItem);

            var monsterName = FixedTextBox(new Rectangle(49, 320, 116, 23));
            page.Controls.Add(monsterName);
            page.Controls.Add(MakeLabel("货币数量:", new Rectangle(217, 317, 63, 23)));
            var currencyAmount = FixedTextBox(new Rectangle(280, 317, 92, 23), "0");
            page.Controls.Add(currencyAmount);
            foreach (var (text, x, width) in new[]
                     {
                         ("给玩家金币", 389, 78),
                         ("给玩家元宝", 471, 77),
                         ("给玩家灵符", 553, 80),
                     })
            {
                var currencyButton = MakeButton(text, new Rectangle(x, 317, width, 21));
                currencyButton.Click += async (_, _) => await GiveCurrencyAsync(
                    text, playerName.Text, currencyAmount.Text);
                page.Controls.Add(currencyButton);
            }

            var reloadDrop = MakeButton("重载以上怪物txt爆率", new Rectangle(50, 348, 115, 21));
            reloadDrop.Click += async (_, _) => await ReloadMonsterDropAsync(monsterName.Text);
            page.Controls.Add(reloadDrop);

            var reloadAll = MakeButton("重载所有怪物txt爆率（这种方式删掉的txt文件不参与重载）", new Rectangle(167, 348, 323, 21));
            reloadAll.Click += async (_, _) => await ReloadAllMonsterDropsAsync();
            page.Controls.Add(reloadAll);

            page.Controls.Add(MakeLabel("拥有的装备", new Rectangle(783, 25, 100, 24)));
            var equipmentLabels = new[] { "衣服:", "武器:", "勋章:", "项链:", "头盔:", "手镯:", "手镯:", "戒指:", "戒指:", "毒符:", "腰带:", "靴子:", "血石:", "斗笠:", "玉佩:", "盾牌:" };
            var equipmentValues = new List<TextBox>(equipmentLabels.Length);
            for (var index = 0; index < equipmentLabels.Length; index++)
            {
                var column = index % 2;
                var row = index / 2;
                var x = 668 + column * 137;
                var y = 60 + (row * 65 + 1) / 2;
                page.Controls.Add(MakeLabel(equipmentLabels[index], new Rectangle(x, y, 39, 23)));
                var equipmentValue = FixedTextBox(new Rectangle(x + 39, y, 80, 23), readOnly: true);
                equipmentValues.Add(equipmentValue);
                page.Controls.Add(equipmentValue);
            }

            var viewEquipment = MakeButton("查看装备", new Rectangle(848, 323, 74, 21));
            viewEquipment.Click += async (_, _) => await ReadEquipmentAsync(playerName.Text, equipmentValues);
            page.Controls.Add(viewEquipment);
        }

        private async Task RunGameActionAsync(Func<string> action)
        {
            try
            {
                var engine = M2Share.UserEngine;
                if (engine == null)
                {
                    ShowStatus("游戏处理引擎尚未初始化");
                    return;
                }
                var message = await engine.InvokeFromUiAsync(action);
                ShowStatus(string.IsNullOrWhiteSpace(message) ? "操作完成" : message);
            }
            catch (TimeoutException)
            {
                ShowStatus("操作超时，游戏线程未执行请求");
            }
            catch (Exception exception)
            {
                ShowStatus("操作失败：" + exception.Message);
            }
        }

        private async Task ReadPlayerVariableAsync(
            string playerName, string parameterText, TextBox currentValue)
        {
            var name = playerName.Trim();
            if (!TryReadGmVariableIndex(parameterText, out var index, out var error))
            {
                ShowStatus(error);
                return;
            }
            await RunGameActionAsync(() =>
            {
                var player = M2Share.UserEngine.GetPlayObjectEx(name);
                if (player == null) return name.Length == 0 ? "请输入玩家名字" : $"玩家 {name} 不在线";
                if (M2Share.PasEngine == null) return "Pascal 脚本引擎尚未初始化";
                using var context = M2Share.PasEngine.Api.PushContext(player, null);
                var value = M2Share.PasEngine.Api.GetPlayerVar('V', 1, index).AsInt();
                BeginInvoke(new Action(() => currentValue.Text = value.ToString(CultureInfo.InvariantCulture)));
                return $"{player.m_sCharName} V(1,{index}) = {value}";
            });
        }

        private async Task WriteGlobalVariableAsync(
            string parameterText, string valueText, TextBox currentValue)
        {
            if (!TryReadGmVariableIndex(parameterText, out var index, out var error) ||
                !TryReadGmInteger(valueText, out var value, out error))
            {
                ShowStatus(error);
                return;
            }
            await RunGameActionAsync(() =>
            {
                if (M2Share.PasEngine == null) return "Pascal 脚本引擎尚未初始化";
                M2Share.PasEngine.Api.SetGlobalVar(1, index, PasValue.FromInt(value));
                BeginInvoke(new Action(() => currentValue.Text = value.ToString(CultureInfo.InvariantCulture)));
                return $"G(1,{index}) 已设置为 {value}";
            });
        }

        private async Task WritePlayerVariableAsync(
            string playerName, string parameterText, string valueText, TextBox currentValue)
        {
            var name = playerName.Trim();
            if (!TryReadGmVariableIndex(parameterText, out var index, out var error) ||
                !TryReadGmInteger(valueText, out var value, out error))
            {
                ShowStatus(error);
                return;
            }
            await RunGameActionAsync(() =>
            {
                var player = M2Share.UserEngine.GetPlayObjectEx(name);
                if (player == null) return name.Length == 0 ? "请输入玩家名字" : $"玩家 {name} 不在线";
                if (M2Share.PasEngine == null) return "Pascal 脚本引擎尚未初始化";
                using var context = M2Share.PasEngine.Api.PushContext(player, null);
                M2Share.PasEngine.Api.SetPlayerVar('V', 1, index, PasValue.FromInt(value));
                BeginInvoke(new Action(() => currentValue.Text = value.ToString(CultureInfo.InvariantCulture)));
                return $"{player.m_sCharName} V(1,{index}) 已设置为 {value}";
            });
        }

        private async Task GiveItemAsync(string playerName, string itemName, string countText)
        {
            var name = playerName.Trim();
            var requestedItem = itemName.Trim();
            if (name.Length == 0 || requestedItem.Length == 0)
            {
                ShowStatus("玩家名字和物品名不能为空");
                return;
            }
            if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                count is < 1 or > 50)
            {
                ShowStatus("物品数量必须是 1 到 50");
                return;
            }

            await RunGameActionAsync(() =>
            {
                var player = M2Share.UserEngine.GetPlayObjectEx(name);
                if (player == null) return $"玩家 {name} 不在线";
                if (M2Share.PasEngine == null) return "Pascal 脚本引擎尚未初始化";
                using var context = M2Share.PasEngine.Api.PushContext(player, null);
                var args = new List<PasValue>
                {
                    PasValue.FromString(requestedItem),
                    PasValue.FromInt(count),
                };
                if (!M2Share.PasEngine.Api.CallPlayerFunc("give", args, out var result) || !result.AsBool())
                    return $"给予失败：物品不存在、背包已满或该货币尚未移植";
                M2Share.MainOutMessage($"[眼神GM] 给予 {player.m_sCharName} {requestedItem} x{count}");
                return $"已给予 {player.m_sCharName} {requestedItem} x{count}";
            });
        }

        private async Task GiveCurrencyAsync(string command, string playerName, string amountText)
        {
            var name = playerName.Trim();
            if (name.Length == 0)
            {
                ShowStatus("请输入玩家名字");
                return;
            }
            if (!int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) ||
                amount <= 0)
            {
                ShowStatus("货币数量必须是正整数");
                return;
            }
            await RunGameActionAsync(() =>
            {
                var player = M2Share.UserEngine.GetPlayObjectEx(name);
                if (player == null) return $"玩家 {name} 不在线";
                int added;
                if (command == "给玩家金币")
                {
                    var target = Math.Min((long)player.m_nGoldMax, (long)player.m_nGold + amount);
                    added = (int)(target - player.m_nGold);
                    player.m_nGold = (int)target;
                    player.GoldChanged();
                }
                else if (command == "给玩家元宝")
                {
                    var target = Math.Min(2_000_000L, (long)player.m_nGameGold + amount);
                    added = (int)(target - player.m_nGameGold);
                    player.m_nGameGold = (int)target;
                    player.GameGoldChanged();
                }
                else
                {
                    var target = Math.Min(int.MaxValue, (long)player.m_nLingFu + amount);
                    added = (int)(target - player.m_nLingFu);
                    if (added > 0)
                    {
                        player.AddNativeLingFu(30_099, added, false);
                        player.SysMsg($"系统增加灵符 {added}",
                            MsgColor.Green, MsgType.Hint);
                        M2Share.AddGameDataLog(string.Join('\t', 9, player.m_sMapName,
                            player.m_nCurrX, player.m_nCurrY, player.m_sCharName,
                            "灵符", 30_099, added, "眼神GM面板"));
                    }
                }
                M2Share.MainOutMessage($"[眼神GM] {command} {player.m_sCharName} +{added}");
                return added > 0
                    ? $"{player.m_sCharName} 已增加 {added}"
                    : $"{player.m_sCharName} 已达到上限";
            });
        }

        private async Task ReloadMonsterDropAsync(string monsterName)
        {
            var name = monsterName.Trim();
            if (name.Length == 0)
            {
                ShowStatus("请输入怪物名字");
                return;
            }

            await RunGameActionAsync(() =>
            {
                if (M2Share.LocalDB == null) return "本地数据库尚未初始化";
                var monsterIndex = -1;
                for (var index = 0; index < M2Share.UserEngine.MonsterList.Count; index++)
                {
                    if (!string.Equals(M2Share.UserEngine.MonsterList[index].sName, name,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    monsterIndex = index;
                    break;
                }
                if (monsterIndex < 0) return $"怪物 {name} 不存在";
                var monster = M2Share.UserEngine.MonsterList[monsterIndex];
                var itemCount = M2Share.LocalDB.LoadMonitems(monster.sName, ref monster.ItemList);
                M2Share.UserEngine.MonsterList[monsterIndex] = monster;
                return $"{monster.sName} 的 TXT 爆率已重载，共 {itemCount} 条";
            });
        }

        private async Task ReloadAllMonsterDropsAsync()
        {
            await RunGameActionAsync(() =>
            {
                if (M2Share.LocalDB == null) return "本地数据库尚未初始化";
                var itemCount = 0;
                for (var index = 0; index < M2Share.UserEngine.MonsterList.Count; index++)
                {
                    var monster = M2Share.UserEngine.MonsterList[index];
                    itemCount += M2Share.LocalDB.LoadMonitems(monster.sName, ref monster.ItemList);
                    M2Share.UserEngine.MonsterList[index] = monster;
                }
                return $"全部怪物 TXT 爆率已重载，共 {itemCount} 条";
            });
        }

        private async Task ReadEquipmentAsync(string playerName, IReadOnlyList<TextBox> editors)
        {
            var name = playerName.Trim();
            await RunGameActionAsync(() =>
            {
                var player = M2Share.UserEngine.GetPlayObjectEx(name);
                if (player == null) return name.Length == 0 ? "请输入玩家名字" : $"玩家 {name} 不在线";
                var values = new string[editors.Count];
                for (var index = 0; index < values.Length && index < player.m_UseItems.Length; index++)
                {
                    var item = player.m_UseItems[index];
                    values[index] = item?.wIndex > 0
                        ? M2Share.UserEngine.GetStdItemName(item.wIndex)
                        : string.Empty;
                }
                BeginInvoke(new Action(() =>
                {
                    for (var index = 0; index < editors.Count; index++)
                        editors[index].Text = values[index] ?? string.Empty;
                }));
                return $"已读取 {player.m_sCharName} 的装备";
            });
        }

        private static bool TryReadGmVariableIndex(string text, out int value, out string error)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                value is >= 0 and <= 99)
            {
                error = null;
                return true;
            }
            error = "参数1必须是 0 到 99 的整数";
            return false;
        }

        private static bool TryReadGmInteger(string text, out int value, out string error)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = null;
                return true;
            }
            error = "设置值必须是有效整数";
            return false;
        }

        private Control BuildHelpPage()
        {
            var announcementColor = Color.FromArgb(0, 0, 139);
            var panel = new Panel
            {
                BackColor = announcementColor,
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
            };
            var text = new TextBox
            {
                BackColor = announcementColor,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                Font = YanshenUiFont.Create("华文行楷", 10f),
                ForeColor = Color.White,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false,
                Text = AnnouncementHeader,
                WordWrap = true,
            };
            panel.Controls.Add(text);
            Shown += async (_, _) => await LoadAnnouncementAsync(text);
            return panel;
        }

        private async Task LoadAnnouncementAsync(TextBox text)
        {
            string result;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var response = await client.GetAsync(
                    AnnouncementUrl, HttpCompletionOption.ResponseHeadersRead);
                if ((int)response.StatusCode != 200)
                {
                    result = $"后台服务器获取失败，错误码：{(int)response.StatusCode}";
                }
                else
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    var json = Encoding.GetEncoding(936).GetString(bytes);
                    result = TryReadAnnouncement(json, out var announcement)
                        ? AnnouncementHeader + announcement
                        : "服务器返回参数类型错误";
                }
            }
            catch
            {
                result = "后台服务器获取失败，错误码：0";
            }

            if (!IsDisposed && !text.IsDisposed)
                text.Text = result;
        }

        private static bool TryReadAnnouncement(string json, out string announcement)
        {
            try
            {
                using var document = JsonDocument.Parse(EscapeJsonStringControlCharacters(json));
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    announcement = text.GetString() ?? string.Empty;
                    return true;
                }
            }
            catch (JsonException)
            {
            }

            announcement = null;
            return false;
        }

        private static string EscapeJsonStringControlCharacters(string json)
        {
            var result = new StringBuilder(json.Length);
            var inString = false;
            var escaped = false;
            foreach (var character in json)
            {
                if (!inString)
                {
                    result.Append(character);
                    if (character == '"') inString = true;
                    continue;
                }

                if (escaped)
                {
                    result.Append(character);
                    escaped = false;
                }
                else if (character == '\\')
                {
                    result.Append(character);
                    escaped = true;
                }
                else if (character == '"')
                {
                    result.Append(character);
                    inString = false;
                }
                else if (character < ' ')
                {
                    result.Append(character switch
                    {
                        '\b' => "\\b",
                        '\f' => "\\f",
                        '\n' => "\\n",
                        '\r' => "\\r",
                        '\t' => "\\t",
                        _ => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                    });
                }
                else
                {
                    result.Append(character);
                }
            }
            return result.ToString();
        }

        private ReplicaConfigPanel CreateConfigEditor(string pageName)
        {
            var editor = new ReplicaConfigPanel
            {
                AccentColor = YanshenPageCatalog.AccentFor(pageName),
                Columns = YanshenPageCatalog.ColumnsFor(pageName),
                Dock = DockStyle.Fill,
            };
            HookEditor(editor);
            _pageEditors[pageName] = editor;
            return editor;
        }

        private void HookEditor(IReplicaConfigEditor editor)
        {
            editor.OnToggle += OnToggle;
            editor.OnEditChanged += OnEditChanged;
            _allEditors.Add(editor);
        }

        private void HookItemEditor(IReplicaConfigEditor editor)
        {
            editor.OnToggle += OnItemToggle;
            editor.OnEditChanged += OnItemEditChanged;
            _allEditors.Add(editor);
        }

        private void HookMixedEditor(IReplicaConfigEditor editor)
        {
            editor.OnToggle += (key, enabled) =>
            {
                if (_itemRows.ContainsKey(key)) OnItemToggle(key, enabled);
                else OnToggle(key, enabled);
            };
            editor.OnEditChanged += (key, text) =>
            {
                if (_itemRows.ContainsKey(key)) OnItemEditChanged(key, text);
                else OnEditChanged(key, text);
            };
            _allEditors.Add(editor);
        }

        private void HookMixedMyJsonEditor(IReplicaConfigEditor editor)
        {
            editor.OnToggle += (key, enabled) =>
            {
                if (_myJsonRows.ContainsKey(key)) OnMyJsonToggle(key, enabled);
                else OnToggle(key, enabled);
            };
            editor.OnEditChanged += (key, text) =>
            {
                if (_myJsonRows.ContainsKey(key)) OnMyJsonEditChanged(key, text);
                else OnEditChanged(key, text);
            };
            _allEditors.Add(editor);
        }

        private void HookMyJsonEditor(IReplicaConfigEditor editor)
        {
            editor.OnToggle += OnMyJsonToggle;
            editor.OnEditChanged += OnMyJsonEditChanged;
            _allEditors.Add(editor);
        }

        private void LoadConfig(bool reloadFromDisk)
        {
            CommitPendingEdits();
            string itemLoadError = null;
            var myJsonErrors = new Dictionary<YanshenMyJsonKind, string>();
            if (reloadFromDisk)
            {
                _manager.LoadNativeConfig();
                if (!_manager.ReloadItemConfig(out itemLoadError) && _manager.HasValidItemConfig)
                    itemLoadError = "重载失败，继续使用上次有效配置：" + itemLoadError;
                foreach (var kind in EditableMyJsonKinds)
                    if (!_manager.ReloadMyJsonConfig(kind, out var error))
                        myJsonErrors[kind] = error;
            }

            _rows.Clear();
            _itemRows.Clear();
            _myJsonRows.Clear();
            _myJsonBindings.Clear();
            _pending.Clear();
            _pendingItems.Clear();
            _pendingMyJson.Clear();
            foreach (var (key, value) in _manager.GetNativeConfig())
                _rows[key] = TogglePanel.ToggleRow.FromConfig(key, value);
            foreach (var (key, value) in _manager.GetItemConfig())
                _itemRows[key] = TogglePanel.ToggleRow.FromConfig(key, value);
            EnsureItemReplicaRows();

            var catalog = YanshenPageCatalog.Build(_rows);
            foreach (var (pageName, editor) in _pageEditors)
                editor.SetFeatures(catalog.Pages.TryGetValue(pageName, out var features)
                    ? features
                    : Array.Empty<ReplicaFeature>());

            foreach (var (category, view) in _extensionViews)
            {
                if (string.Equals(category, "物品相关", StringComparison.Ordinal))
                    view.SetItemFeatures(
                        _itemRows,
                        itemLoadError,
                        Array.Empty<ReplicaFeature>());
                else
                {
                    var features = catalog.ExtensionFeatures.Where(feature =>
                            string.Equals(feature.Category, category, StringComparison.Ordinal))
                        .ToList();
                    string categoryError = null;
                    if (string.Equals(category, "角色相关", StringComparison.Ordinal))
                    {
                        features.Clear();
                        features.AddRange(BuildFixedMyJsonFeatures(
                            YanshenMyJsonKind.Role,
                            _manager.GetMyJsonConfig(YanshenMyJsonKind.Role), category,
                            "角色相关选项",
                            YanshenFixedPageCatalog.RoleEntries));
                        categoryError = MyJsonError(myJsonErrors, YanshenMyJsonKind.Role);
                    }
                    else if (string.Equals(category, "技能相关", StringComparison.Ordinal))
                    {
                        features.Clear();
                        features.AddRange(BuildFixedMyJsonFeatures(
                            YanshenMyJsonKind.SkillConfig,
                            _manager.GetMyJsonConfig(YanshenMyJsonKind.SkillConfig), category,
                            "技能相关选项",
                            YanshenFixedPageCatalog.SkillEntries));

                        // These documents back the create/reload commands and SaveAll, but their
                        // records are not pages in the fixed 2.07 skill tree.
                        BuildStructuredMyJsonFeatures(
                            YanshenMyJsonKind.SkillExt, "技能扩展：",
                            _manager.GetMyJsonConfig(YanshenMyJsonKind.SkillExt), category);
                        BuildStructuredMyJsonFeatures(
                            YanshenMyJsonKind.MonsterSkillExt, "怪物技能：",
                            _manager.GetMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt), category);
                        categoryError = MyJsonError(myJsonErrors,
                            YanshenMyJsonKind.SkillConfig, YanshenMyJsonKind.SkillExt,
                            YanshenMyJsonKind.MonsterSkillExt);
                    }
                    else if (string.Equals(category, "爆率相关", StringComparison.Ordinal))
                    {
                        features.AddRange(BuildStructuredMyJsonFeatures(
                            YanshenMyJsonKind.DropRate, "爆率：",
                            _manager.GetMyJsonConfig(YanshenMyJsonKind.DropRate), category));
                        features.AddRange(BuildStructuredMyJsonFeatures(
                            YanshenMyJsonKind.GuaranteedDrop, "",
                            _manager.GetMyJsonConfig(YanshenMyJsonKind.GuaranteedDrop), category,
                            "全区可爆"));
                        categoryError = MyJsonError(myJsonErrors,
                            YanshenMyJsonKind.DropRate, YanshenMyJsonKind.GuaranteedDrop);
                    }
                    else if (string.Equals(category, "脚本相关", StringComparison.Ordinal))
                    {
                        features.Clear();
                    }
                    view.SetFeatures(features, categoryError);
                }
            }

            _assignedConfigKeyCount = catalog.AssignedKeys.Count;
            _remainingStatus.Text = RemainingStatusText;
            _pathStatus.Text = PathStatusText;
            _runtimeStatus.Text = RuntimeStatusText;
            _loaded = true;
            YanshenUiFont.ConfigureCompatibleText(this);
        }

        private IReadOnlyList<ReplicaFeature> BuildFlatMyJsonFeatures(
            YanshenMyJsonKind kind,
            IReadOnlyDictionary<string, object> document,
            string category,
            string optionKey)
        {
            return document
                .Where(entry => !string.Equals(entry.Key, optionKey, StringComparison.Ordinal))
                .GroupBy(entry => FeaturePrefix(entry.Key), StringComparer.Ordinal)
                .Select(group =>
                {
                    var rows = group.Select(entry => RegisterMyJsonRow(
                            kind, new[] { entry.Key }, entry.Key, entry.Value))
                        .ToArray();
                    var main = rows.FirstOrDefault(row =>
                        string.Equals(row.Key, group.Key + "_是否勾选", StringComparison.Ordinal));
                    return new ReplicaFeature(
                        main,
                        group.Key,
                        rows.Where(row => !ReferenceEquals(row, main)).ToArray(),
                        category);
                })
                .ToArray();
        }

        private IReadOnlyList<ReplicaFeature> BuildFixedMyJsonFeatures(
            YanshenMyJsonKind kind,
            IReadOnlyDictionary<string, object> document,
            string category,
            string optionKey,
            IReadOnlyList<YanshenFixedPageCatalog.Entry> entries)
        {
            var result = new List<ReplicaFeature>(entries.Count);
            foreach (var entry in entries)
            {
                var prefix = entry.FeatureName + "_";
                var rows = document
                    .Where(item => !string.Equals(item.Key, optionKey, StringComparison.Ordinal) &&
                                   item.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(item => RegisterMyJsonRow(kind, new[] { item.Key }, item.Key, item.Value))
                    .ToArray();
                if (rows.Length == 0) continue;

                var main = rows.FirstOrDefault(row =>
                    string.Equals(row.Key, prefix + "是否勾选", StringComparison.Ordinal));
                var parameters = rows
                    .Where(row => !ReferenceEquals(row, main))
                    .OrderBy(row => YanshenFixedPageCatalog.ParameterOrder(entry.FeatureName, row.Key))
                    .ThenBy(row => row.Key, StringComparer.Ordinal)
                    .ToArray();
                result.Add(new ReplicaFeature(
                    main,
                    entry.FeatureName,
                    parameters,
                    category,
                    YanshenFixedPageCatalog.Note(entry.FeatureName)));
            }
            return result;
        }

        private IReadOnlyList<ReplicaFeature> BuildStructuredMyJsonFeatures(
            YanshenMyJsonKind kind,
            string displayPrefix,
            IReadOnlyDictionary<string, object> document,
            string category,
            string forcedFeatureName = null)
        {
            var result = new List<ReplicaFeature>();
            if (!string.IsNullOrEmpty(forcedFeatureName))
            {
                var rows = new List<TogglePanel.ToggleRow>();
                foreach (var (key, value) in document.Where(entry => !IsAnnotationKey(entry.Key)))
                    AddStructuredRows(kind, forcedFeatureName, new[] { key }, value, rows);
                if (rows.Count > 0)
                    result.Add(new ReplicaFeature(null, forcedFeatureName, rows, category));
                return result;
            }

            foreach (var (key, value) in document.Where(entry => !IsAnnotationKey(entry.Key)))
            {
                var displayName = displayPrefix + key;
                var rows = new List<TogglePanel.ToggleRow>();
                AddStructuredRows(kind, displayName, new[] { key }, value, rows);
                if (rows.Count > 0)
                    result.Add(new ReplicaFeature(null, displayName, rows, category));
            }
            return result;
        }

        private void AddStructuredRows(
            YanshenMyJsonKind kind,
            string featureName,
            IReadOnlyList<string> path,
            object value,
            ICollection<TogglePanel.ToggleRow> rows)
        {
            value = PluginManager.NormalizeConfigValue(value);
            if (value is IReadOnlyDictionary<string, object> dictionary)
            {
                foreach (var (key, child) in dictionary)
                    AddStructuredRows(kind, featureName, path.Concat(new[] { key }).ToArray(), child, rows);
                return;
            }
            if (value is IEnumerable<object> sequence && value is not string)
            {
                var index = 0;
                foreach (var child in sequence)
                    AddStructuredRows(kind, featureName,
                        path.Concat(new[] { "#" + index++ }).ToArray(), child, rows);
                return;
            }

            var relativePath = path.Skip(1)
                .Select(segment => segment.StartsWith('#') ? "[" + segment[1..] + "]" : segment);
            var parameterName = string.Join("·", relativePath);
            if (string.IsNullOrEmpty(parameterName)) parameterName = path[0];
            var rowKey = featureName + "_" + parameterName;
            rows.Add(RegisterMyJsonRow(kind, path, rowKey, value));
        }

        private TogglePanel.ToggleRow RegisterMyJsonRow(
            YanshenMyJsonKind kind,
            IReadOnlyList<string> path,
            string rowKey,
            object value)
        {
            if (_myJsonRows.ContainsKey(rowKey))
                rowKey = kind + "_" + rowKey;
            var row = TogglePanel.ToggleRow.FromConfig(rowKey, value);
            _myJsonRows[rowKey] = row;
            _myJsonBindings[rowKey] = new MyJsonValueBinding(kind, path.ToArray());
            return row;
        }

        private static string FeaturePrefix(string key)
        {
            var separator = key.IndexOf('_');
            return separator > 0 ? key[..separator] : key;
        }

        private static bool IsAnnotationKey(string key) =>
            key.StartsWith("注释说明", StringComparison.Ordinal);

        private string MyJsonError(
            IReadOnlyDictionary<YanshenMyJsonKind, string> errors,
            params YanshenMyJsonKind[] kinds)
        {
            var messages = kinds
                .Where(errors.ContainsKey)
                .Select(kind =>
                    $"{Path.GetRelativePath(Path.GetDirectoryName(_manager.NativeConfigPath)!, _manager.GetMyJsonConfigPath(kind))}：{errors[kind]}")
                .ToArray();
            return messages.Length == 0 ? null : string.Join("\r\n", messages);
        }

        private void OnToggle(string key, bool enabled)
        {
            if (!_rows.TryGetValue(key, out var row)) return;
            row.BoolValue = enabled;
            _pending[key] = row.GetToggleValue(enabled);
            MarkChanged();
        }

        private void EnsureItemReplicaRows()
        {
            var compatibilityDefaults = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["一号元素有值绑定_是否勾选"] = 0L,
                ["二号元素有值禁止穿戴_是否勾选"] = 0L,
                ["沙巴克武器升级防破碎_是否勾选"] = 0L,
                ["聚灵珠解除绑定_是否勾选"] = 0L,
                ["拆零关小黑屋_是否删除违规物"] = string.Empty,
            };

            foreach (var (key, value) in compatibilityDefaults)
                if (!_itemRows.ContainsKey(key))
                    _itemRows[key] = TogglePanel.ToggleRow.FromConfig(key, value);
        }

        private void OnEditChanged(string key, string text)
        {
            if (!_rows.TryGetValue(key, out var row)) return;
            row.TextValue = text;
            _pending[key] = row.TryConvertText(text, out var value, out _) ? value : text;
            MarkChanged();
        }

        private void OnItemToggle(string key, bool enabled)
        {
            if (!_itemRows.TryGetValue(key, out var row)) return;
            row.BoolValue = enabled;
            _pendingItems[key] = row.GetToggleValue(enabled);

            if (enabled)
            {
                var exclusiveKey = key switch
                {
                    "拾取物品后触发_是否勾选" => "物品进背包触发_是否勾选",
                    "物品进背包触发_是否勾选" => "拾取物品后触发_是否勾选",
                    _ => null,
                };
                if (exclusiveKey != null && _itemRows.TryGetValue(exclusiveKey, out var exclusiveRow) &&
                    exclusiveRow.IsToggle && exclusiveRow.BoolValue)
                {
                    exclusiveRow.BoolValue = false;
                    _pendingItems[exclusiveKey] = exclusiveRow.GetToggleValue(false);
                }
            }
            MarkChanged();
        }

        private void OnItemEditChanged(string key, string text)
        {
            if (!_itemRows.TryGetValue(key, out var row)) return;
            row.TextValue = text;
            _pendingItems[key] = row.TryConvertText(text, out var value, out _) ? value : text;
            MarkChanged();
        }

        private void OnMyJsonToggle(string key, bool enabled)
        {
            if (!_myJsonRows.TryGetValue(key, out var row)) return;
            row.BoolValue = enabled;
            _pendingMyJson[key] = row.GetToggleValue(enabled);
            MarkChanged();
        }

        private void OnMyJsonEditChanged(string key, string text)
        {
            if (!_myJsonRows.TryGetValue(key, out var row)) return;
            row.TextValue = text;
            _pendingMyJson[key] = row.TryConvertText(text, out var value, out _) ? value : text;
            MarkChanged();
        }

        private void MarkChanged()
        {
            _remainingStatus.Text = RemainingStatusText;
            _runtimeStatus.Text = "有效期至：本地兼容（配置待保存）";
        }

        private bool SaveAll(bool showSuccess)
        {
            CommitPendingEdits();
            if (_pending.Count == 0 && _pendingItems.Count == 0 && _pendingMyJson.Count == 0)
            {
                ShowStatus("配置没有变化");
                return true;
            }

            if (!TryCollectChanges(_pending, _rows, out var changes, out var validationKey, out var validationError) ||
                !TryCollectChanges(_pendingItems, _itemRows, out var itemChanges, out validationKey, out validationError) ||
                !TryCollectChanges(_pendingMyJson, _myJsonRows, out var myJsonChanges, out validationKey, out validationError))
            {
                MessageBox.Show($"{validationKey}: {validationError}", "配置值无效",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (changes.Count > 0 && !_manager.ApplyNativeConfigChanges(changes, out var error))
            {
                MessageBox.Show($"配置保存失败:\r\n{error}", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (changes.Count > 0) _pending.Clear();

            if (itemChanges.Count > 0 && !_manager.ApplyItemConfigChanges(itemChanges, out var itemError))
            {
                MessageBox.Show($"物品配置保存失败：\r\n{itemError}", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (itemChanges.Count > 0) _pendingItems.Clear();
            if (myJsonChanges.Count > 0 && !SaveMyJsonChanges(myJsonChanges, out var myJsonError))
            {
                MessageBox.Show($"MyJson配置保存失败：\r\n{myJsonError}", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            _remainingStatus.Text = RemainingStatusText;
            _runtimeStatus.Text = RuntimeStatusText;
            if (showSuccess) ShowStatus("配置保存成功，运行时配置已更新");
            return true;
        }

        private bool SaveItemConfig(bool showSuccess)
        {
            CommitPendingEdits();
            if (_pendingItems.Count == 0)
            {
                ShowStatus("本页配置没有变化");
                return true;
            }

            if (!TryCollectChanges(_pendingItems, _itemRows, out var changes,
                    out var validationKey, out var validationError))
            {
                MessageBox.Show($"{validationKey}: {validationError}", "物品配置值无效",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!_manager.ApplyItemConfigChanges(changes, out var error))
            {
                MessageBox.Show($"物品配置保存失败：\r\n{error}", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            _pendingItems.Clear();
            _runtimeStatus.Text = _pending.Count > 0 || _pendingMyJson.Count > 0
                ? "有效期至：本地兼容（其他配置待保存）"
                : RuntimeStatusText;
            if (showSuccess) ShowStatus("物品配置保存成功，运行时配置已更新");
            return true;
        }

        private bool SaveExtensionCategory(string category, bool showSuccess)
        {
            CommitPendingEdits();
            if (!_extensionViews.TryGetValue(category, out var view)) return true;

            var nativePending = _pending
                .Where(change => view.ContainsConfigKey(change.Key))
                .ToDictionary(change => change.Key, change => change.Value, StringComparer.Ordinal);
            var itemPending = _pendingItems
                .Where(change => view.ContainsConfigKey(change.Key))
                .ToDictionary(change => change.Key, change => change.Value, StringComparer.Ordinal);
            var myJsonPending = _pendingMyJson
                .Where(change => view.ContainsConfigKey(change.Key))
                .ToDictionary(change => change.Key, change => change.Value, StringComparer.Ordinal);

            if (nativePending.Count == 0 && itemPending.Count == 0 && myJsonPending.Count == 0)
            {
                ShowStatus("本页配置没有变化");
                return true;
            }

            if (!TryCollectChanges(nativePending, _rows, out var nativeChanges,
                    out var validationKey, out var validationError) ||
                !TryCollectChanges(itemPending, _itemRows, out var itemChanges,
                    out validationKey, out validationError) ||
                !TryCollectChanges(myJsonPending, _myJsonRows, out var myJsonChanges,
                    out validationKey, out validationError))
            {
                MessageBox.Show($"{validationKey}: {validationError}", "配置值无效",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (nativeChanges.Count > 0 && !_manager.ApplyNativeConfigChanges(nativeChanges, out var nativeError))
            {
                MessageBox.Show($"配置保存失败：\r\n{nativeError}", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            foreach (var key in nativeChanges.Keys) _pending.Remove(key);

            if (itemChanges.Count > 0 && !_manager.ApplyItemConfigChanges(itemChanges, out var itemError))
            {
                MessageBox.Show($"物品配置保存失败：\r\n{itemError}", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            foreach (var key in itemChanges.Keys) _pendingItems.Remove(key);

            if (myJsonChanges.Count > 0 && !SaveMyJsonChanges(myJsonChanges, out var myJsonError))
            {
                MessageBox.Show($"MyJson配置保存失败：\r\n{myJsonError}", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            _runtimeStatus.Text = _pending.Count > 0 || _pendingItems.Count > 0 || _pendingMyJson.Count > 0
                ? "有效期至：本地兼容（其他配置待保存）"
                : RuntimeStatusText;
            if (showSuccess) ShowStatus("本页配置保存成功，运行时配置已更新");
            return true;
        }

        private void ReloadExtensionCategory(string category, string action = null)
        {
            CommitPendingEdits();
            if (!ConfirmDiscardPendingChanges()) return;

            var errors = new List<string>();
            if (string.Equals(category, "物品相关", StringComparison.Ordinal))
            {
                if (!_manager.ReloadItemConfig(out var error)) errors.Add(error);
            }
            else if (string.Equals(category, "角色相关", StringComparison.Ordinal))
            {
                ReloadMyJson(YanshenMyJsonKind.Role, errors);
            }
            else if (string.Equals(category, "技能相关", StringComparison.Ordinal))
            {
                ReloadMyJson(YanshenMyJsonKind.SkillConfig, errors);
                ReloadMyJson(YanshenMyJsonKind.SkillExt, errors);
                ReloadMyJson(YanshenMyJsonKind.MonsterSkillExt, errors);
            }
            else if (string.Equals(category, "爆率相关", StringComparison.Ordinal))
            {
                if (!string.Equals(action, "guaranteed", StringComparison.Ordinal))
                    ReloadMyJson(YanshenMyJsonKind.DropRate, errors);
                if (!string.Equals(action, "drop-rate", StringComparison.Ordinal))
                    ReloadMyJson(YanshenMyJsonKind.GuaranteedDrop, errors);
            }
            else
            {
                _manager.LoadNativeConfig();
            }

            LoadConfig(false);
            if (errors.Count == 0)
                ShowStatus("本页配置已重新读取");
            else
                MessageBox.Show(string.Join("\r\n", errors), "重载失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ReloadMyJson(YanshenMyJsonKind kind, ICollection<string> errors)
        {
            if (!_manager.ReloadMyJsonConfig(kind, out var error))
                errors.Add($"{Path.GetFileName(_manager.GetMyJsonConfigPath(kind))}：{error}");
        }

        private bool ConfirmDiscardPendingChanges()
        {
            if (_pending.Count == 0 && _pendingItems.Count == 0 && _pendingMyJson.Count == 0)
                return true;
            return MessageBox.Show("存在尚未保存的修改，是否放弃修改并重新读取配置？", "重新读取配置",
                       MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void CreateOrReloadSkillExtensionConfig()
        {
            if (!ConfirmDiscardPendingChanges()) return;
            var path = _manager.GetMyJsonConfigPath(YanshenMyJsonKind.SkillExt);
            var existedBefore = File.Exists(path);
            if (!existedBefore && !_manager.ApplyMyJsonConfig(
                    YanshenMyJsonKind.SkillExt,
                    new Dictionary<string, object>(StringComparer.Ordinal),
                    out var createError))
            {
                MessageBox.Show($"额外技能配置创建失败：\r\n{createError}", "创建失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!_manager.ReloadMyJsonConfig(YanshenMyJsonKind.SkillExt, out var reloadError))
            {
                MessageBox.Show($"额外技能配置重载失败：\r\n{reloadError}", "重载失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LoadConfig(false);
            ShowStatus(existedBefore ? "额外技能配置文件已重新读取" : "额外技能配置文件已创建");
        }

        private void SaveGuaranteedDropRuntimeData()
        {
            if (!SaveExtensionCategory("爆率相关", false)) return;
            ShowStatus(M2Share.SaveMonDropLimitList()
                ? "全区已爆数据已保存"
                : "全区已爆数据保存失败");
        }

        private bool SaveMyJsonChanges(
            IReadOnlyDictionary<string, object> changes,
            out string error)
        {
            foreach (var group in changes.GroupBy(change => _myJsonBindings[change.Key].Kind))
            {
                var document = _manager.GetMyJsonConfig(group.Key);
                foreach (var (key, value) in group)
                    SetMyJsonValue(document, _myJsonBindings[key].Path, value);
                if (!_manager.ApplyMyJsonConfig(group.Key, document, out error))
                    return false;

                foreach (var (key, _) in group) _pendingMyJson.Remove(key);
            }

            error = null;
            return true;
        }

        private static void SetMyJsonValue(
            IDictionary<string, object> document,
            IReadOnlyList<string> path,
            object value)
        {
            if (path.Count == 0) throw new ArgumentException("MyJson path cannot be empty", nameof(path));
            object current = document;
            for (var index = 0; index < path.Count - 1; index++)
            {
                var segment = path[index];
                current = segment.StartsWith('#')
                    ? ((IList<object>)current)[ParseListIndex(segment)]
                    : ((IDictionary<string, object>)current)[segment];
            }

            var leaf = path[^1];
            if (leaf.StartsWith('#'))
                ((IList<object>)current)[ParseListIndex(leaf)] = value;
            else
                ((IDictionary<string, object>)current)[leaf] = value;
        }

        private static int ParseListIndex(string segment) =>
            int.Parse(segment.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);

        private static bool TryCollectChanges(
            IReadOnlyDictionary<string, object> pending,
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows,
            out Dictionary<string, object> changes,
            out string validationKey,
            out string validationError)
        {
            changes = new Dictionary<string, object>(StringComparer.Ordinal);
            validationKey = null;
            validationError = null;
            foreach (var (key, pendingValue) in pending)
            {
                if (!rows.TryGetValue(key, out var row)) continue;
                object converted = null;
                if (!row.IsToggle && !row.TryConvertText(row.TextValue, out converted, out validationError))
                {
                    validationKey = key;
                    return false;
                }
                changes[key] = row.IsToggle ? pendingValue : converted;
            }
            return true;
        }

        private void CommitPendingEdits()
        {
            foreach (var editor in _allEditors) editor.CommitPendingEdit();
        }

        private void OnFormClosingWithChanges(object sender, FormClosingEventArgs e)
        {
            CommitPendingEdits();
            if (_pending.Count == 0 && _pendingItems.Count == 0 && _pendingMyJson.Count == 0) return;
            var answer = MessageBox.Show("配置已经修改，是否保存?", "M2超级伴侣",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == DialogResult.Yes && !SaveAll(false)) e.Cancel = true;
        }

        private async void ShowStatus(string text)
        {
            _runtimeStatus.Text = text;
            await Task.Delay(2500);
            if (IsDisposed) return;
            _runtimeStatus.Text = _pending.Count > 0 || _pendingItems.Count > 0 || _pendingMyJson.Count > 0
                ? "有效期至：本地兼容（配置待保存）"
                : RuntimeStatusText;
        }

        private void BeginWindowDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        private static ClassicTabControl MakeTabs(Rectangle bounds) => new()
        {
            Appearance = TabAppearance.Normal,
            BackColor = SystemColors.Control,
            Bounds = bounds,
            Font = BodyFont,
            HotTrack = false,
            Multiline = false,
            Padding = new Point(4, 3),
            SizeMode = TabSizeMode.Normal,
        };

        private static TabPage MakePage(string name) => new(name)
        {
            BackColor = SystemColors.Control,
            Font = BodyFont,
            Padding = new Padding(4, 9, 0, 4),
            UseVisualStyleBackColor = false,
        };

        private static Panel MakeInnerFrame() => new()
        {
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 10, 0, 5),
        };

        private static void AddInsetPageFrame(TabPage page)
        {
            page.Padding = new Padding(7, 7, 0, 0);
            var frame = new Panel
            {
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
            };
            page.Controls.Add(frame);
            frame.SendToBack();
        }

        private static ClassicButton MakeButton(string text, Rectangle bounds) => new()
        {
            Bounds = bounds,
            Font = BodyFont,
            Text = text,
            UseVisualStyleBackColor = false,
        };

        private static Label MakeLabel(string text, Rectangle bounds) => new()
        {
            Bounds = bounds,
            Font = BodyFont,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        private static Label MakeStatusLabel(Rectangle bounds) => new()
        {
            BackColor = Color.White,
            BorderStyle = BorderStyle.Fixed3D,
            Bounds = bounds,
            Font = BodyFont,
            ForeColor = Color.Red,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        private sealed record MyJsonValueBinding(
            YanshenMyJsonKind Kind,
            IReadOnlyList<string> Path);

        protected override void Dispose(bool disposing) => base.Dispose(disposing);
    }

    internal sealed class ReplicaConfigPanel : Panel, IReplicaConfigEditor
    {
        private const int OuterMargin = 10;
        private readonly Font _editorFont;
        private readonly Font _bodyFont;
        private readonly Font _compactFont;
        private readonly Font _headingFont;
        private readonly TextBox _editor;
        private readonly List<FeatureLayout> _layouts = new();
        private readonly List<ParameterHit> _parameterHits = new();
        private readonly List<SpecialInput> _specialInputs = new();
        private readonly List<Control> _specialControls = new();
        private IReadOnlyList<ReplicaFeature> _features = Array.Empty<ReplicaFeature>();
        private ParameterHit _activeParameter;
        private bool _cancelEdit;
        private bool _specialized;

        public event Action<string, bool> OnToggle;
        public event Action<string, string> OnEditChanged;

        public int Columns { get; set; } = 4;
        public Color AccentColor { get; set; } = Color.Red;
        public int ConfigKeyCount => _features.SelectMany(feature => feature.Keys).Distinct(StringComparer.Ordinal).Count();

        public ReplicaConfigPanel()
        {
            _editorFont = YanshenUiFont.Create("SimSun", 9f);
            _bodyFont = YanshenUiFont.CreateTextRenderer("SimSun", 9f, DeviceDpi);
            _compactFont = YanshenUiFont.CreateTextRenderer("SimSun", 7.5f, DeviceDpi);
            _headingFont = YanshenUiFont.CreateTextRenderer("SimSun", 9f, DeviceDpi, FontStyle.Bold);

            AutoScroll = true;
            BackColor = SystemColors.Control;
            BorderStyle = BorderStyle.FixedSingle;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            _editor = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Font = _editorFont,
                Visible = false,
            };
            _editor.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    CommitPendingEdit();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _cancelEdit = true;
                    HideEditor();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            _editor.LostFocus += (_, _) =>
            {
                if (_cancelEdit)
                {
                    _cancelEdit = false;
                    return;
                }
                CommitPendingEdit();
            };
            Controls.Add(_editor);
        }

        public void SetFeatures(IEnumerable<ReplicaFeature> features)
        {
            CommitPendingEdit();
            ClearSpecializedControls();
            _specialized = false;
            _features = features?.ToArray() ?? Array.Empty<ReplicaFeature>();
            AutoScrollPosition = Point.Empty;
            RebuildLayout();
            Invalidate();
        }

        public void SetSpecializedFeature(ReplicaFeature feature)
        {
            CommitPendingEdit();
            ClearSpecializedControls();
            _features = Array.Empty<ReplicaFeature>();
            _specialized = true;
            AutoScrollPosition = Point.Empty;

            foreach (var layout in YanshenFixedPageCatalog.Layout(feature.DisplayName))
            {
                var key = feature.DisplayName + "_" + layout.ParameterSuffix;
                var row = feature.ParameterRows.FirstOrDefault(candidate =>
                    string.Equals(candidate.Key, key, StringComparison.Ordinal));
                if (row == null) continue;

                var label = new YanshenSingleLineLabel
                {
                    Bounds = layout.LabelBounds,
                    Font = _editorFont,
                    Text = layout.Caption,
                    TextAlign = ContentAlignment.MiddleLeft,
                };
                Control input;
                if (layout.Options is { Length: > 0 })
                {
                    var combo = new ComboBox
                    {
                        Bounds = layout.InputBounds,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        FlatStyle = FlatStyle.Standard,
                        Font = _editorFont,
                        IntegralHeight = false,
                    };
                    combo.Items.AddRange(layout.Options.Cast<object>().ToArray());
                    if (!string.IsNullOrEmpty(row.TextValue) && !combo.Items.Contains(row.TextValue))
                        combo.Items.Add(row.TextValue);
                    combo.SelectedItem = row.TextValue ?? string.Empty;
                    input = combo;
                }
                else
                {
                    input = new TextBox
                    {
                        BorderStyle = BorderStyle.FixedSingle,
                        Bounds = layout.InputBounds,
                        Font = _editorFont,
                        Text = row.TextValue ?? string.Empty,
                    };
                }

                _specialControls.Add(label);
                _specialControls.Add(input);
                _specialInputs.Add(new SpecialInput(row, input));
                Controls.Add(label);
                Controls.Add(input);
            }

            if (string.Equals(feature.DisplayName, "诱惑之光修改", StringComparison.Ordinal))
            {
                var levelHint = new YanshenSingleLineLabel
                {
                    Bounds = new Rectangle(6, 136, 210, 23),
                    Font = _editorFont,
                    Text = "注:每1级提升一个宝宝数量:",
                    TextAlign = ContentAlignment.MiddleLeft,
                };
                _specialControls.Add(levelHint);
                Controls.Add(levelHint);
            }

            AutoScrollMinSize = Size.Empty;
            Invalidate();
        }

        public void CommitPendingEdit()
        {
            if (_specialized)
            {
                foreach (var input in _specialInputs)
                {
                    var text = input.Control.Text ?? string.Empty;
                    if (string.Equals(input.Row.TextValue ?? string.Empty, text, StringComparison.Ordinal)) continue;
                    input.Row.TextValue = text;
                    OnEditChanged?.Invoke(input.Row.Key, text);
                }
                return;
            }
            if (_activeParameter == null || !_editor.Visible) return;
            _activeParameter.Row.TextValue = _editor.Text;
            OnEditChanged?.Invoke(_activeParameter.Row.Key, _editor.Text);
            HideEditor();
        }

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            RebuildLayout();
        }

        protected override void OnScroll(ScrollEventArgs eventArgs)
        {
            base.OnScroll(eventArgs);
            PositionEditor();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            var graphics = eventArgs.Graphics;
            var state = graphics.Save();
            graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            _parameterHits.Clear();

            foreach (var layout in _layouts)
                PaintFeature(graphics, layout);

            graphics.Restore(state);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            base.OnMouseDown(eventArgs);
            if (eventArgs.Button != MouseButtons.Left) return;
            CommitPendingEdit();
            var point = new Point(
                eventArgs.X - AutoScrollPosition.X,
                eventArgs.Y - AutoScrollPosition.Y);

            foreach (var layout in _layouts)
            {
                if (layout.ToggleBounds.Contains(point) && layout.Feature.MainRow is { IsToggle: true } row)
                {
                    row.BoolValue = !row.BoolValue;
                    OnToggle?.Invoke(row.Key, row.BoolValue);
                    Invalidate();
                    return;
                }
            }

            var parameter = _parameterHits.FirstOrDefault(hit => hit.Bounds.Contains(point));
            if (parameter != null) BeginEdit(parameter);
        }

        private void RebuildLayout()
        {
            _layouts.Clear();
            if (_specialized)
            {
                AutoScrollMinSize = Size.Empty;
                return;
            }
            if (_features.Count == 0)
            {
                AutoScrollMinSize = Size.Empty;
                return;
            }

            var columns = Math.Max(1, Columns);
            var availableWidth = Math.Max(200, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - OuterMargin * 2);
            var columnWidth = Math.Max(130, availableWidth / columns);
            var y = OuterMargin;

            for (var start = 0; start < _features.Count; start += columns)
            {
                var count = Math.Min(columns, _features.Count - start);
                var rowHeight = 0;
                for (var column = 0; column < count; column++)
                    rowHeight = Math.Max(rowHeight, FeatureHeight(_features[start + column], columnWidth - 6));

                for (var column = 0; column < count; column++)
                {
                    var bounds = new Rectangle(
                        OuterMargin + column * columnWidth,
                        y,
                        columnWidth - 6,
                        rowHeight - 4);
                    _layouts.Add(new FeatureLayout(_features[start + column], bounds));
                }
                y += rowHeight;
            }

            AutoScrollMinSize = new Size(0, y + OuterMargin);
            PositionEditor();
        }

        private int FeatureHeight(ReplicaFeature feature, int width)
        {
            var header = HeaderHeight(feature, width);
            var parameterRows = (feature.ParameterRows.Count + 2) / 3;
            var note = string.IsNullOrWhiteSpace(feature.Note) ? 0 : 20;
            return Math.Max(30, header + parameterRows * 25 + note + 4);
        }

        private int HeaderHeight(ReplicaFeature feature, int width)
        {
            if (feature.MainRow == null && string.IsNullOrEmpty(feature.DisplayName)) return 4;
            var text = feature.MainRow is { IsToggle: true } row
                ? $"{feature.DisplayName}({(row.BoolValue ? "已启动" : "未启动")})"
                : feature.DisplayName;
            var textWidth = Math.Max(30, width - (feature.MainRow?.IsToggle == true ? 24 : 8));
            if (TextRenderer.MeasureText(text, HeaderFont(feature, width)).Width <= textWidth + 6) return 26;
            var measured = TextRenderer.MeasureText(text, _compactFont, new Size(textWidth, 42),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            return measured.Height > 20 ? 40 : 26;
        }

        private Font HeaderFont(ReplicaFeature feature, int width)
        {
            var text = feature.MainRow is { IsToggle: true } row
                ? $"{feature.DisplayName}({(row.BoolValue ? "已启动" : "未启动")})"
                : feature.DisplayName;
            var textWidth = Math.Max(30, width - (feature.MainRow?.IsToggle == true ? 24 : 8));
            return TextRenderer.MeasureText(text, _bodyFont).Width <= textWidth + 6
                ? _bodyFont
                : _compactFont;
        }

        private void PaintFeature(Graphics graphics, FeatureLayout layout)
        {
            var feature = layout.Feature;
            var bounds = layout.Bounds;
            var y = bounds.Y + 4;
            var headerHeight = HeaderHeight(feature, bounds.Width);
            var headerFont = HeaderFont(feature, bounds.Width);
            var headerFlags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                              (headerHeight > 26 ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine);

            if (feature.MainRow is { } mainRow)
            {
                if (mainRow.IsToggle)
                {
                    var checkBounds = new Rectangle(bounds.X + 3, y + 1, 13, 13);
                    ControlPaint.DrawCheckBox(graphics, checkBounds,
                        mainRow.BoolValue ? ButtonState.Checked : ButtonState.Normal);
                    layout.ToggleBounds = new Rectangle(bounds.X, y - 2, bounds.Width, 21);
                    var status = mainRow.BoolValue ? "已启动" : "未启动";
                    TextRenderer.DrawText(graphics, $"{feature.DisplayName}({status})", headerFont,
                        new Rectangle(bounds.X + 19, y - 1, bounds.Width - 21, headerHeight - 3),
                        SystemColors.ControlText,
                        headerFlags);
                    layout.ToggleBounds = new Rectangle(bounds.X, y - 2, bounds.Width, headerHeight);
                }
                else
                {
                    TextRenderer.DrawText(graphics, feature.DisplayName, _headingFont,
                        new Rectangle(bounds.X + 3, y - 1, bounds.Width - 6, 19),
                        SystemColors.ControlText,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                y += headerHeight;
            }
            else if (!string.IsNullOrEmpty(feature.DisplayName))
            {
                TextRenderer.DrawText(graphics, feature.DisplayName, _headingFont,
                    new Rectangle(bounds.X + 3, y - 1, bounds.Width - 6, 19),
                    AccentColor,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += headerHeight;
            }

            const int parametersPerLine = 3;
            for (var index = 0; index < feature.ParameterRows.Count; index++)
            {
                var row = feature.ParameterRows[index];
                var slot = index % parametersPerLine;
                var line = index / parametersPerLine;
                var itemsOnLine = Math.Min(parametersPerLine,
                    feature.ParameterRows.Count - line * parametersPerLine);
                var slotWidth = Math.Max(42, (bounds.Width - 4) / itemsOnLine);
                var slotX = bounds.X + 2 + slot * slotWidth;
                var slotY = y + line * 25;
                var shortName = ShortParameterName(feature, row.Key);
                var measuredLabelWidth = TextRenderer.MeasureText(shortName, _bodyFont).Width + 8;
                var labelWidth = Math.Min(Math.Max(14, measuredLabelWidth),
                    Math.Max(14, slotWidth - 28));

                TextRenderer.DrawText(graphics, shortName, _bodyFont,
                    new Rectangle(slotX, slotY + 2, labelWidth, 19), AccentColor,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                var valueBounds = new Rectangle(
                    slotX + labelWidth,
                    slotY,
                    Math.Max(24, slotWidth - labelWidth - 4),
                    21);
                graphics.FillRectangle(Brushes.White, valueBounds);
                ControlPaint.DrawBorder3D(graphics, valueBounds, Border3DStyle.Sunken);
                TextRenderer.DrawText(graphics, row.TextValue ?? string.Empty, _bodyFont,
                    Rectangle.Inflate(valueBounds, -3, -2), SystemColors.ControlText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                _parameterHits.Add(new ParameterHit(row, valueBounds));
            }

            if (!string.IsNullOrWhiteSpace(feature.Note))
            {
                var noteY = y + ((feature.ParameterRows.Count + 2) / 3) * 25;
                TextRenderer.DrawText(graphics, feature.Note, _bodyFont,
                    new Rectangle(bounds.X + 3, noteY, bounds.Width - 6, 18), AccentColor,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private static string ShortParameterName(ReplicaFeature feature, string key)
        {
            if (key == "自定义聚灵珠_消耗数量") return "消耗数量：";
            if (key.EndsWith("_A值", StringComparison.Ordinal) || key.EndsWith("_系数A", StringComparison.Ordinal)) return "A：";
            if (key.EndsWith("_B值", StringComparison.Ordinal) || key.EndsWith("_系数B", StringComparison.Ordinal)) return "B：";
            if (key.EndsWith("_K值", StringComparison.Ordinal)) return "K：";
            if (key.EndsWith("_范围值", StringComparison.Ordinal)) return "范围：";
            if (key.Contains("序号", StringComparison.Ordinal)) return "序号：";
            if (key.Contains("数量", StringComparison.Ordinal)) return "数量：";
            if (key == "摆摊地图") return "地图：";
            if (key == "主号全局法速") return "ms：";
            if (key.StartsWith("限制摆摊_", StringComparison.Ordinal)) return key[5..].ToUpperInvariant() + "：";
            var name = key;
            var displayPrefix = feature.DisplayName + "_";
            if (name.StartsWith(displayPrefix, StringComparison.Ordinal))
                name = name[displayPrefix.Length..];
            else if (feature.MainRow != null && name.StartsWith(feature.MainRow.Key, StringComparison.Ordinal))
                name = name[feature.MainRow.Key.Length..];
            if (name.StartsWith('_')) name = name[1..];
            foreach (var prefix in new[] { "武器", "衣服", "头盔", "项链", "手镯", "戒指" })
                if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
            name = name.Replace("_值", string.Empty, StringComparison.Ordinal)
                       .Replace('_', '：');
            return string.IsNullOrWhiteSpace(name) ? "值：" : name + "：";
        }

        private void BeginEdit(ParameterHit parameter)
        {
            _activeParameter = parameter;
            _editor.Text = parameter.Row.TextValue ?? string.Empty;
            PositionEditor();
            _editor.Visible = true;
            _editor.BringToFront();
            _editor.Focus();
            _editor.SelectAll();
        }

        private void PositionEditor()
        {
            if (_activeParameter == null || !_editor.Visible) return;
            var bounds = _activeParameter.Bounds;
            bounds.Offset(AutoScrollPosition.X, AutoScrollPosition.Y);
            _editor.Bounds = bounds;
        }

        private void HideEditor()
        {
            _editor.Visible = false;
            _activeParameter = null;
            Invalidate();
        }

        private void ClearSpecializedControls()
        {
            _specialInputs.Clear();
            foreach (var control in _specialControls)
            {
                Controls.Remove(control);
                control.Dispose();
            }
            _specialControls.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bodyFont.Dispose();
                _compactFont.Dispose();
                _headingFont.Dispose();
                _editorFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class FeatureLayout
        {
            public FeatureLayout(ReplicaFeature feature, Rectangle bounds)
            {
                Feature = feature;
                Bounds = bounds;
            }

            public ReplicaFeature Feature { get; }
            public Rectangle Bounds { get; }
            public Rectangle ToggleBounds { get; set; }
        }

        private sealed class ParameterHit
        {
            public ParameterHit(TogglePanel.ToggleRow row, Rectangle bounds)
            {
                Row = row;
                Bounds = bounds;
            }

            public TogglePanel.ToggleRow Row { get; }
            public Rectangle Bounds { get; }
        }

        private sealed record SpecialInput(TogglePanel.ToggleRow Row, Control Control);
    }

    internal sealed class ExtensionCategoryView : UserControl
    {
        private readonly string _category;
        private readonly SplitContainer _split;
        private readonly TreeView _tree;
        private readonly Panel _helpPanel;
        private readonly RichTextBox _categoryHelpText;
        private readonly CheckBox _mainToggle;
        private readonly ClassicButton _mainAction;
        private readonly ClassicButton _saveButton;
        private readonly ClassicButton _createSkillButton;
        private readonly ClassicButton _reloadSkillButton;
        private readonly ClassicButton _reloadDropButton;
        private readonly ClassicButton _reloadGuaranteedButton;
        private readonly ClassicButton _saveDropDataButton;
        private readonly Dictionary<TreeNode, ReplicaFeature> _featuresByNode = new();
        private readonly HashSet<string> _configKeys = new(StringComparer.Ordinal);
        private ReplicaFeature _selectedFeature;
        private string _configError;
        private bool _settingMainToggle;

        public event Action<string, bool> MainToggle;
        public event Action MainActionRequested;
        public event Action SaveRequested;
        public event Action<string> ReloadRequested;
        public event Action CreateSkillRequested;
        public event Action SaveDropDataRequested;

        public ReplicaConfigPanel Editor { get; }
        public BackpackReplicaPanel BackpackEditor { get; }
        public int ItemLeafCount { get; private set; }
        public int BoundItemKeyCount { get; private set; }
        public int ConfigKeyCount => _configKeys.Count;
        public int SelectedConfigKeyCount => _selectedFeature == null
            ? 0
            : new[] { _selectedFeature.MainRow }
                .Where(row => row != null)
                .Concat(_selectedFeature.ParameterRows)
                .Select(row => row.Key)
                .Distinct(StringComparer.Ordinal)
                .Count();

        public ExtensionCategoryView(string category)
        {
            _category = category;
            Font = YanshenUiFont.Create("SimSun", 9f);
            var skillPage = string.Equals(category, "技能相关", StringComparison.Ordinal);

            _split = new SplitContainer
            {
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = true,
                Size = new Size(900, 400),
                SplitterDistance = skillPage ? 204 : 240,
                SplitterWidth = 6,
            };

            _tree = new TreeView
            {
                BorderStyle = BorderStyle.None,
                Font = Font,
                HideSelection = false,
                Indent = 16,
                ItemHeight = 16,
                ShowNodeToolTips = true,
                ShowRootLines = false,
            };
            _tree.AfterSelect += (_, eventArgs) => SelectNode(eventArgs.Node);

            if (skillPage)
            {
                _tree.Bounds = new Rectangle(14, 14, 188, 374);
                _tree.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            }
            else
            {
                _split.Panel1.Padding = new Padding(14, 14, 0, 4);
                _tree.Dock = DockStyle.Fill;
            }
            _split.Panel1.Controls.Add(_tree);

            Editor = new ReplicaConfigPanel
            {
                AccentColor = Color.Blue,
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.None,
                Columns = 2,
                Visible = false,
            };
            BackpackEditor = new BackpackReplicaPanel
            {
                Dock = DockStyle.Fill,
                Visible = false,
            };

            _helpPanel = new Panel { BackColor = SystemColors.Control };
            _categoryHelpText = new RichTextBox
            {
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                Font = Font,
                ForeColor = Color.Blue,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.ForcedVertical,
            };
            _helpPanel.Controls.Add(_categoryHelpText);

            _mainToggle = new CheckBox
            {
                AutoSize = false,
                Font = Font,
                UseCompatibleTextRendering = true,
                Visible = false,
            };
            _mainToggle.CheckedChanged += (_, _) =>
            {
                if (_settingMainToggle || _selectedFeature?.MainRow is not { IsToggle: true } row) return;
                row.BoolValue = _mainToggle.Checked;
                SetMainToggleText();
                MainToggle?.Invoke(row.Key, row.BoolValue);
            };
            _mainAction = MakeCommandButton(CategoryMainCaption(), (_, _) => MainActionRequested?.Invoke());
            _saveButton = MakeCommandButton(SaveCaption(), (_, _) => SaveRequested?.Invoke());
            _createSkillButton = MakeCommandButton("创建额外技能配置", (_, _) => CreateSkillRequested?.Invoke());
            _reloadSkillButton = MakeCommandButton("重载技能配置文件", (_, _) => ReloadRequested?.Invoke("skills"));
            _reloadDropButton = MakeCommandButton("重载眼神爆率配置", (_, _) => ReloadRequested?.Invoke("drop-rate"));
            _reloadGuaranteedButton = MakeCommandButton("重载全区可爆配置", (_, _) => ReloadRequested?.Invoke("guaranteed"));
            _saveDropDataButton = MakeCommandButton("保存全区已爆数据", (_, _) => SaveDropDataRequested?.Invoke());

            _split.Panel2.Padding = Padding.Empty;
            _split.Panel2.Controls.Add(Editor);
            _split.Panel2.Controls.Add(_helpPanel);
            _split.Panel2.Controls.Add(_mainToggle);
            _split.Panel2.Controls.Add(_mainAction);
            _split.Panel2.Controls.Add(_saveButton);
            _split.Panel2.Controls.Add(_createSkillButton);
            _split.Panel2.Controls.Add(_reloadSkillButton);
            _split.Panel2.Controls.Add(_reloadDropButton);
            _split.Panel2.Controls.Add(_reloadGuaranteedButton);
            _split.Panel2.Controls.Add(_saveDropDataButton);
            _split.Panel2.Controls.Add(BackpackEditor);
            _split.Panel2.Resize += (_, _) => LayoutCategoryControls();
            Controls.Add(_split);
            HideCommands();
            ShowCategoryHelp(null);
            LayoutCategoryControls();
        }

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (Dock == DockStyle.Fill && Parent is TabPage)
                height += 3;
            base.SetBoundsCore(x, y, width, height, specified);
        }

        public bool ContainsConfigKey(string key) => _configKeys.Contains(key);

        public void SetFeatures(IEnumerable<ReplicaFeature> features, string loadError = null)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            _featuresByNode.Clear();
            _configKeys.Clear();

            if (string.Equals(_category, "脚本相关", StringComparison.Ordinal))
            {
                _configError = loadError;
                ItemLeafCount = 0;
                BoundItemKeyCount = 0;
                _selectedFeature = null;
                Editor.SetFeatures(Array.Empty<ReplicaFeature>());
                Editor.Visible = false;
                BackpackEditor.Visible = false;
                _helpPanel.Visible = true;
                YanshenRichText.SetText(_categoryHelpText, string.Empty);
                HideCommands();
                _tree.EndUpdate();
                return;
            }

            var root = _tree.Nodes.Add(_category + "选项");
            var materialized = features?.ToArray() ?? Array.Empty<ReplicaFeature>();
            if (string.Equals(_category, "角色相关", StringComparison.Ordinal))
                BuildFixedTree(root, YanshenFixedPageCatalog.RoleEntries, materialized);
            else if (string.Equals(_category, "技能相关", StringComparison.Ordinal))
                BuildFixedTree(root, YanshenFixedPageCatalog.SkillEntries, materialized);
            else if (string.Equals(_category, "爆率相关", StringComparison.Ordinal))
            {
                var playerDropRate = root.Nodes.Add("人物爆率相关");
                var newDropRate = playerDropRate.Nodes.Add("眼神新爆率");
                foreach (var feature in OrderFeatures(materialized))
                    BindNode(newDropRate.Nodes.Add(feature.DisplayName), feature);
            }
            else
            {
                foreach (var feature in OrderFeatures(materialized))
                {
                    var parent = root;
                    foreach (var group in GroupPath(feature.DisplayName))
                        parent = GetOrAddGroup(parent, group);
                    BindNode(parent.Nodes.Add(feature.DisplayName), feature);
                }
            }

            _configError = loadError;
            ItemLeafCount = 0;
            BoundItemKeyCount = 0;
            ResetSelection(root);
            _tree.EndUpdate();
        }

        public void SetItemFeatures(
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows,
            string loadError,
            IEnumerable<ReplicaFeature> nativeFeatures)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            _featuresByNode.Clear();
            _configKeys.Clear();
            _configError = loadError;
            BoundItemKeyCount = 0;

            var root = _tree.Nodes.Add(_category + "选项");
            var redefine = root.Nodes.Add("物品重定义");
            foreach (var text in new[]
                     {
                         "自定义聚灵珠", "眼神扔出物品不绑定", "扣除指定持久物品", "npc卖物倍数",
                         "绑定物品允许拾取", "一号元素有值绑定", "二号元素有值禁止穿戴",
                         "沙巴克武器升级防破碎", "聚灵珠解除绑定"
                     })
                BindItemNode(redefine.Nodes.Add(text), text, rows);

            var backpack = root.Nodes.Add("物品背包扩展");
            foreach (var text in new[]
                     {
                         "无限背包", "拆零关小黑屋", "拾取物品后触发", "物品进背包触发", "金币绑定"
                     })
                BindItemNode(backpack.Nodes.Add(text), text, rows);

            var fixedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "自定义聚灵珠", "眼神扔出物品不绑定", "扣除指定持久物品", "npc卖物倍数",
                "绑定物品允许拾取", "一号元素有值绑定", "二号元素有值禁止穿戴",
                "沙巴克武器升级防破碎", "聚灵珠解除绑定", "无限背包", "拆零关小黑屋",
                "拾取物品后触发", "物品进背包触发", "金币绑定"
            };
            foreach (var feature in nativeFeatures ?? Array.Empty<ReplicaFeature>())
            {
                if (fixedNames.Contains(feature.DisplayName)) continue;
                var target = feature.DisplayName.Contains("背包", StringComparison.Ordinal) ||
                             feature.DisplayName.Contains("仓库", StringComparison.Ordinal) ||
                             feature.DisplayName.Contains("拾取", StringComparison.Ordinal) ||
                             feature.DisplayName.Contains("金币", StringComparison.Ordinal)
                    ? backpack
                    : redefine;
                BindNode(target.Nodes.Add(feature.DisplayName), feature);
            }

            ItemLeafCount = 14;
            ResetSelection(root);
            _tree.EndUpdate();
        }

        private void SelectNode(TreeNode node)
        {
            ReplicaFeature feature = null;
            var found = node != null && _featuresByNode.TryGetValue(node, out feature);
            var isBackpack = found && string.Equals(feature.DisplayName, "无限背包", StringComparison.Ordinal);
            _selectedFeature = found ? feature : null;
            BackpackEditor.Visible = isBackpack;
            Editor.Visible = found && !isBackpack;
            _helpPanel.Visible = !isBackpack;

            if (isBackpack)
            {
                HideCommands();
                _tree.Nodes[0].ExpandAll();
                BackpackEditor.SetFeatures(new[] { feature });
                BackpackEditor.BringToFront();
            }
            else if (found)
            {
                if (string.Equals(_category, "物品相关", StringComparison.Ordinal) ||
                    string.Equals(_category, "角色相关", StringComparison.Ordinal) ||
                    string.Equals(_category, "技能相关", StringComparison.Ordinal))
                    Editor.SetSpecializedFeature(feature);
                else
                {
                    var parameters = feature.MainRow is { IsToggle: false } nonToggle
                        ? new[] { nonToggle }.Concat(feature.ParameterRows).ToArray()
                        : feature.ParameterRows;
                    Editor.SetFeatures(new[]
                    {
                        new ReplicaFeature(null, feature.DisplayName, parameters, feature.Category)
                    });
                }
                ShowFeatureCommands(feature);
                ShowFeatureHelp(feature);
            }
            else
            {
                Editor.SetFeatures(Array.Empty<ReplicaFeature>());
                HideCommands();
                ShowCategoryHelp(node);
            }
            _tree.Focus();
        }

        private void ResetSelection(TreeNode root)
        {
            root.Expand();
            Editor.SetFeatures(Array.Empty<ReplicaFeature>());
            Editor.Visible = false;
            BackpackEditor.Visible = false;
            _helpPanel.Visible = true;
            HideCommands();
            _tree.SelectedNode = root;
        }

        private void ShowFeatureCommands(ReplicaFeature feature)
        {
            HideCommands();
            if (feature.MainRow is { IsToggle: true } main)
            {
                _settingMainToggle = true;
                _mainToggle.Checked = main.BoolValue;
                _settingMainToggle = false;
                SetMainToggleText();
                _mainToggle.Visible = true;
            }
            else
            {
                _mainAction.Text = CategoryMainCaption();
                _mainAction.Visible = true;
            }

            _saveButton.Visible = !string.Equals(_category, "脚本相关", StringComparison.Ordinal);
            if (string.Equals(_category, "技能相关", StringComparison.Ordinal))
            {
                var showSkillFiles = string.Equals(feature.DisplayName, "额外技能", StringComparison.Ordinal) ||
                                     string.Equals(feature.DisplayName, "怪物伤害触发技能特效", StringComparison.Ordinal);
                _createSkillButton.Visible = showSkillFiles;
                _reloadSkillButton.Visible = showSkillFiles;
            }
            else if (string.Equals(_category, "爆率相关", StringComparison.Ordinal))
            {
                _reloadDropButton.Visible = true;
                _reloadGuaranteedButton.Visible = true;
                _saveDropDataButton.Visible = true;
            }
            LayoutCategoryControls();
        }

        private void HideCommands()
        {
            _mainToggle.Visible = false;
            _mainAction.Visible = false;
            _saveButton.Visible = false;
            _createSkillButton.Visible = false;
            _reloadSkillButton.Visible = false;
            _reloadDropButton.Visible = false;
            _reloadGuaranteedButton.Visible = false;
            _saveDropDataButton.Visible = false;
        }

        private void SetMainToggleText()
        {
            if (_selectedFeature == null) return;
            _mainToggle.Text = $"{_selectedFeature.DisplayName}({(_mainToggle.Checked ? "已启动" : "未启动")})";
        }

        private void LayoutCategoryControls()
        {
            if (_split.Panel2.ClientSize.Width <= 0 || _split.Panel2.ClientSize.Height <= 0) return;
            var width = _split.Panel2.ClientSize.Width;
            var height = _split.Panel2.ClientSize.Height;
            var skillPage = string.Equals(_category, "技能相关", StringComparison.Ordinal);
            var rolePage = string.Equals(_category, "角色相关", StringComparison.Ordinal);
            var noteTop = Math.Max(210, height - (skillPage ? 124 : 129));
            var footerY = noteTop - (rolePage ? 38 : skillPage ? 37 : 33);
            var contentX = skillPage ? 87 : 0;
            var helpX = skillPage ? 87 : 0;
            var helpHeight = Math.Min(skillPage ? 120 : 125,
                height - noteTop - (skillPage ? 3 : 2));
            _helpPanel.Bounds = new Rectangle(helpX, noteTop,
                Math.Max(80, width - 19 - helpX), helpHeight);
            var editorTop = skillPage ? 50 : rolePage ? 52 : 39;
            Editor.Bounds = new Rectangle(contentX, editorTop, width - contentX,
                Math.Max(70, footerY - editorTop - 5));

            var mainBounds = _category switch
            {
                "角色相关" => new Rectangle(15, 24, 275, 16),
                "技能相关" => new Rectangle(234, 23, 275, 16),
                "爆率相关" => new Rectangle(66, 24, 275, 16),
                _ => new Rectangle(15, 24, 201, 16),
            };
            _mainToggle.Bounds = mainBounds;
            _mainAction.Bounds = mainBounds;

            if (string.Equals(_category, "物品相关", StringComparison.Ordinal))
                _saveButton.Bounds = new Rectangle(234, noteTop - 29, 123, 23);
            else if (string.Equals(_category, "角色相关", StringComparison.Ordinal))
                _saveButton.Bounds = new Rectangle(288, footerY, 87, 23);
            else if (string.Equals(_category, "技能相关", StringComparison.Ordinal))
            {
                _createSkillButton.Bounds = new Rectangle(123, footerY, 107, 23);
                _reloadSkillButton.Bounds = new Rectangle(255, footerY, 107, 23);
                _saveButton.Bounds = new Rectangle(501, footerY, 117, 23);
            }
            else if (string.Equals(_category, "爆率相关", StringComparison.Ordinal))
            {
                _reloadDropButton.Bounds = new Rectangle(37, 66, 117, 23);
                _reloadGuaranteedButton.Bounds = new Rectangle(37, 99, 117, 23);
                _saveDropDataButton.Bounds = new Rectangle(36, 129, 117, 23);
                _saveButton.Bounds = new Rectangle(262, footerY, 87, 23);
            }
        }

        private ClassicButton MakeCommandButton(string text, EventHandler click)
        {
            var button = new ClassicButton
            {
                Font = Font,
                Text = text,
                UseVisualStyleBackColor = false,
                Visible = false,
            };
            button.Click += click;
            return button;
        }

        private string CategoryMainCaption() => _category switch
        {
            "物品相关" => "主启动按钮",
            "角色相关" => "角色相关主启动按钮",
            "技能相关" => "技能相关主启动按钮",
            "爆率相关" => "爆率相关主启动按钮",
            _ => "脚本相关主启动按钮",
        };

        private string SaveCaption() => string.Equals(_category, "技能相关", StringComparison.Ordinal)
            ? "保存勾选配置"
            : "保存本页配置";

        private IEnumerable<ReplicaFeature> OrderFeatures(IEnumerable<ReplicaFeature> features) =>
            features.OrderBy(feature => GroupOrder(GroupPath(feature.DisplayName).FirstOrDefault()))
                .ThenBy(feature => FeatureOrder(feature.DisplayName))
                .ThenBy(feature => feature.DisplayName, StringComparer.Ordinal);

        private string[] GroupPath(string featureName)
        {
            if (string.Equals(_category, "角色相关", StringComparison.Ordinal))
            {
                if (RoleReflectFeatures.Contains(featureName))
                    return new[] { "人物角色", "配置1的反伤功能" };
                if (ContainsAny(featureName, "英雄", "月灵")) return new[] { "英雄角色" };
                if (ContainsAny(featureName, "宠物", "宝宝", "变量魔血石")) return new[] { "宠物角色" };
                if (ContainsAny(featureName, "沙巴克", "红名村", "地图")) return new[] { "修改角色地图" };
                if (ContainsAny(featureName, "怪物", "杀怪", "打怪", "瞬移")) return new[] { "野生怪物" };
                if (featureName.StartsWith("npc", StringComparison.OrdinalIgnoreCase)) return new[] { "npc相关" };
                return new[] { "人物角色" };
            }
            if (string.Equals(_category, "技能相关", StringComparison.Ordinal))
            {
                if (featureName.StartsWith("英雄", StringComparison.Ordinal)) return new[] { "英雄技能" };
                if (featureName.StartsWith("怪物", StringComparison.Ordinal) ||
                    featureName.StartsWith("怪物技能：", StringComparison.Ordinal)) return new[] { "怪物技能" };
                return new[] { "人物技能" };
            }
            return Array.Empty<string>();
        }

        private int GroupOrder(string group) => _category switch
        {
            "角色相关" => Array.IndexOf(RoleGroups, group) is var index && index >= 0 ? index : int.MaxValue,
            "技能相关" => Array.IndexOf(SkillGroups, group) is var index && index >= 0 ? index : int.MaxValue,
            _ => 0,
        };

        private int FeatureOrder(string featureName)
        {
            var index = Array.IndexOf(RolePersonFeatureOrder, featureName);
            return index >= 0 ? index : int.MaxValue;
        }

        private static TreeNode GetOrAddGroup(TreeNode parent, string text)
        {
            foreach (TreeNode child in parent.Nodes)
                if (string.Equals(child.Text, text, StringComparison.Ordinal)) return child;
            return parent.Nodes.Add(text);
        }

        private void BuildFixedTree(
            TreeNode root,
            IReadOnlyList<YanshenFixedPageCatalog.Entry> entries,
            IReadOnlyList<ReplicaFeature> features)
        {
            var byName = features.ToDictionary(feature => feature.DisplayName, StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var parent = root;
                foreach (var group in entry.GroupPath)
                    parent = GetOrAddGroup(parent, group);
                var node = parent.Nodes.Add(entry.FeatureName);
                BindNode(node, byName.TryGetValue(entry.FeatureName, out var feature) ? feature : null);
            }
        }

        private void BindItemNode(
            TreeNode node,
            string featureName,
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows)
        {
            var prefix = featureName + "_";
            var featureRows = rows
                .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(entry => entry.Value)
                .ToArray();
            if (featureRows.Length == 0)
            {
                BindNode(node, null);
                return;
            }

            var main = featureRows.FirstOrDefault(row =>
                string.Equals(row.Key, prefix + "是否勾选", StringComparison.Ordinal));
            var order = ItemParameterOrder.TryGetValue(featureName, out var expected)
                ? expected
                : Array.Empty<string>();
            var parameters = featureRows
                .Where(row => !ReferenceEquals(row, main))
                .OrderBy(row =>
                {
                    var index = Array.IndexOf(order, row.Key);
                    return index < 0 ? int.MaxValue : index;
                })
                .ThenBy(row => row.Key, StringComparer.Ordinal)
                .ToArray();
            var feature = new ReplicaFeature(
                main,
                featureName,
                parameters,
                "物品相关",
                ItemNotes.TryGetValue(featureName, out var note) ? note : null);
            BindNode(node, feature);
            BoundItemKeyCount += feature.Keys.Count();
        }

        private void BindNode(TreeNode node, ReplicaFeature feature)
        {
            if (feature != null)
            {
                _featuresByNode[node] = feature;
                foreach (var key in feature.Keys) _configKeys.Add(key);
                return;
            }

            node.ForeColor = SystemColors.GrayText;
            node.ToolTipText = "当前 items/config.json 没有对应字段";
        }

        private void ShowFeatureHelp(ReplicaFeature feature)
        {
            YanshenRichText.SetText(_categoryHelpText, feature.Note ?? string.Empty);
        }

        private void ShowCategoryHelp(TreeNode node)
        {
            string text;
            if (node is { Nodes.Count: 0 } && !_featuresByNode.ContainsKey(node))
                text = $"{node.Text}：当前 MyJson\\items\\config.json 没有对应字段，本节点仅保留原版目录结构。";
            else if (!string.IsNullOrWhiteSpace(_configError))
                text = "配置未能读取：" + _configError;
            else if (string.Equals(_category, "物品相关", StringComparison.Ordinal))
                text = node?.Text switch
                {
                    "物品重定义" => "用于物品的重定义工作！\r\n" +
                                      "【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
                    "物品背包扩展" => string.Empty,
                    _ => "请选择左边的相关说明\r\n" +
                         "【特别注意：需详情了解可以浏览器输入pay.510youxi.com里面有关注群号！】",
                };
            else
                text = string.Empty;
            YanshenRichText.SetText(_categoryHelpText, text);
        }

        private static bool ContainsAny(string value, params string[] fragments) =>
            fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        private static readonly string[] RoleGroups =
        {
            "人物角色", "宠物角色", "野生怪物", "修改角色地图", "英雄角色", "npc相关"
        };

        private static readonly string[] SkillGroups = { "人物技能", "英雄技能", "怪物技能" };

        private static readonly HashSet<string> RoleReflectFeatures = new(StringComparer.Ordinal)
        {
            "反伤带抗性", "火墙不反伤", "切割也反伤", "触发最大伤害概率"
        };

        private static readonly string[] RolePersonFeatureOrder =
        {
            "千分比属性", "主号被攻击触发", "主号新切割", "临时属性", "新永久属性",
            "金币上限突破", "修复飘血数值", "自定义循环函数"
        };

        private static readonly Dictionary<string, string[]> ItemParameterOrder = new(StringComparer.Ordinal)
        {
            ["自定义聚灵珠"] = new[]
            {
                "自定义聚灵珠_收集比例", "自定义聚灵珠_消耗数量",
                "自定义聚灵珠_消耗比例", "自定义聚灵珠_消耗类别"
            },
            ["无限背包"] = new[]
            {
                "无限背包_额外格子", "无限背包_变量v1", "无限背包_变量v2", "无限背包_是否固定"
            },
            ["拆零关小黑屋"] = new[]
            {
                "拆零关小黑屋_等于数字", "拆零关小黑屋_超过数字",
                "拆零关小黑屋_小黑屋", "拆零关小黑屋_是否删除违规物"
            },
        };

        private static readonly Dictionary<string, string> ItemNotes = new(StringComparer.Ordinal)
        {
            ["自定义聚灵珠"] = "修复了聚灵丹使用方法，\r\n" +
                "1、支持自定义配置每次收集当前打怪经验倍数【100=1倍，以此类推】;\r\n" +
                "2:支持使用货币数量（写死的吃数量）和比例（按获得经验的千分比例算货币），数量和消费比例都填0无需货币\r\n" +
                "【聚灵珠命名规则\r\n：名字中一旦存在 ‘英雄’ 两个字，使用后经验会给英雄，而不是主号！】",
            ["眼神扔出物品不绑定"] = "用于眼神的自定义扔出物品，勾选后，谁都可以捡走，不勾选后，指定扔出角色可以捡走，过冷却期其他人可捡走！\r\n" +
                "【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["扣除指定持久物品"] = "被人定制，后期公用！\r\n" +
                "【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["npc卖物倍数"] = "npc卖物倍数：\r\n" +
                "1、【千分比降低或者提升卖出物品的价格】\r\n" +
                "、使用变量是This_Player.SetS(1,214,千分比值)\r\n" +
                "例如千分比值=2000，就是数据库设置的原价，1000就是打五折，以此类推\r\n" +
                "【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["绑定物品允许拾取"] = "绑定物品允许拾取：\r\n1、\r\n、勾选过后，怪物爆出的绑定物品可以被捡走\r\n\r\n" +
                "【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["一号元素有值绑定"] = "勾选后，1号元素大于0就禁止摆摊或者扔地面\r\n" +
                "这个功能是别人定制使用的，大家可以忽略不看\r\n如果你有需求也可以使用，没有限制",
            ["二号元素有值禁止穿戴"] = "勾选后，2号元素=100就禁止穿戴\r\n" +
                "这个功能是别人定制使用的，大家可以忽略不看\r\n如果你有需求也可以使用，没有限制",
            ["拆零关小黑屋"] = "拆零关小黑屋！\r\n" +
                "1.可以设置拆分零或者出现大于多少的值时，进入小黑屋，并再m2面板上生成相关日志。\r\n" +
                "小黑屋地图编号:如果填 fei 就是检查到拆分非法会飞进fei这个编号地图，\r\n" +
                "如果填@fei 就会触发runquest.pas里面的 fei 函数脚本\r\n" +
                "会触发runquest.pas脚本的目的是高级用户可以对这个玩家进行其他更强烈的惩罚！",
            ["拾取物品后触发"] = "拾取物品后触发！\r\n" +
                "1.使用后可以在拾取物品后触发脚本，并配合自定义函数给于物品极品或者元素。\r\n" +
                "也可以获取元素\r\n勾选后仅每次拾取物品，会触发runquest.pas里面的 procedure MyItemInBag(itemID:Integer;Name:string); 函数脚本！\r\n" +
                "并且可以配合Allfuc里面自定义脚本，获得当前拾取的物品数据，比如元素或者极品，\r\n" +
                "并且也可以配合Allfuc里面自定义脚本，给与得当前拾取的物品数据，比如元素或者极品，\r\n" +
                "具体用法详情见Allfuc.pas的使用说明（注意：必须勾选===眼神特殊函数====配合使用）",
            ["物品进背包触发"] = "勾选后，只要是物品进入背包就会触发函数runquest.pas里面的procedure MyItemInBag(itemID:Integer;Name:string);。\r\n" +
                "包括拾取，脱装备，仓库进背包，交易等等情况，均能触发\r\n" +
                "并且可以配合Allfuc里面自定义脚本，获得当前进入背包的物品数据，比如元素或者极品，\r\n" +
                "并且也可以配合Allfuc里面自定义脚本，给与得当前拾取的物品数据，比如元素或者极品，\r\n\r\n" +
                "具体用法详情见Allfuc.pas的使用说明（注意：必须勾选===眼神特殊函数====配合使用）\r\n" +
                "通常有function Ys_GetOther(Player:TPlayer;itemid,id,val,types:integer):integer;返回极品或者元素属性\r\n" +
                "通常有function Ys_GiveBind(Player:TPlayer;itemid,flag:integer):integer;设置当前物品处于绑定状态",
            ["金币绑定"] = "勾选后，背包金币可以被绑定\r\n" +
                "禁止扔出，禁止任何形式交易给他人，仅只能购买npc物品使用！\r\n" +
                "当且仅当玩家This_Player.SetS(1,217,100)时候，绑定金币生效",
        };
    }

    internal sealed record ReplicaFeature(
        TogglePanel.ToggleRow MainRow,
        string DisplayName,
        IReadOnlyList<TogglePanel.ToggleRow> ParameterRows,
        string Category,
        string Note = null)
    {
        public IEnumerable<string> Keys
        {
            get
            {
                if (MainRow != null) yield return MainRow.Key;
                foreach (var row in ParameterRows) yield return row.Key;
            }
        }
    }

    internal static class YanshenFixedPageCatalog
    {
        internal sealed record Entry(string FeatureName, string[] GroupPath);

        internal sealed record ParameterLayout(
            string ParameterSuffix,
            string Caption,
            Rectangle LabelBounds,
            Rectangle InputBounds,
            string[] Options = null);

        private static readonly string[] YesNo = { "否", "是" };
        private static readonly string[] ItemCurrencies = { "元宝", "灵符", "金币" };
        private static readonly string[] DeleteChoices = { "是", "否" };

        public static readonly Entry[] RoleEntries =
        {
            new("切割也反伤", new[] { "人物角色", "配置1的反伤功能" }),
            new("火墙不反伤", new[] { "人物角色", "配置1的反伤功能" }),
            new("反伤带抗性", new[] { "人物角色", "配置1的反伤功能" }),
            new("千分比属性", new[] { "人物角色" }),
            new("主号被攻击触发", new[] { "人物角色" }),
            new("主号新切割", new[] { "人物角色" }),
            new("临时属性", new[] { "人物角色" }),
            new("新永久属性", new[] { "人物角色" }),
            new("金币上限突破", new[] { "人物角色" }),
            new("修复飘血数值", new[] { "人物角色" }),
            new("自定义循环函数", new[] { "人物角色" }),
            new("月灵不扣蓝", new[] { "宠物角色" }),
            new("月灵新伤害", new[] { "宠物角色" }),
            new("瞬移怪物", new[] { "野生怪物" }),
            new("杀怪触发", new[] { "野生怪物" }),
            new("自定义红名村", new[] { "修改角色地图" }),
            new("沙巴克攻城范围", new[] { "修改角色地图" }),
            new("沙巴克复活点", new[] { "修改角色地图" }),
            new("英雄切割", new[] { "英雄角色" }),
            new("指定英雄放技能", new[] { "英雄角色" }),
            new("英雄不自动释放技能", new[] { "英雄角色" }),
            new("npc自定义函数", new[] { "npc相关" }),
        };

        public static readonly Entry[] SkillEntries =
        {
            new("诱惑之光修改", new[] { "人物技能" }),
            new("主号分身术", new[] { "人物技能" }),
            new("额外技能", new[] { "人物技能" }),
            new("真隐身术修复", new[] { "人物技能" }),
            new("主号技能加成", new[] { "人物技能" }),
            new("气功波重定义", new[] { "人物技能" }),
            new("自定义野蛮", new[] { "人物技能" }),
            new("概率格挡", new[] { "人物技能" }),
            new("刺杀免伤", new[] { "人物技能" }),
            new("安全区禁止诱惑和圣言", new[] { "人物技能" }),
            new("技能弹射", new[] { "人物技能" }),
            new("英雄分身修复", new[] { "英雄技能" }),
            new("英雄开天修复", new[] { "英雄技能" }),
            new("合击等级修改", new[] { "英雄技能" }),
            new("英雄技能加成", new[] { "英雄技能" }),
            new("英雄技能点数增加", new[] { "英雄技能" }),
            new("怪物伤害触发技能特效", new[] { "怪物技能" }),
        };

        private static readonly Dictionary<string, ParameterLayout[]> Layouts = new(StringComparer.Ordinal)
        {
            ["自定义聚灵珠"] = new[]
            {
                P("收集比例", "收集打怪经验比例：", 52, 10, 131, 183, 10, 108),
                P("消耗数量", "使用消耗货币数量：", 374, 10, 121, 495, 10, 108),
                P("消耗比例", "按比例消耗货币：", 64, 57, 119, 183, 57, 108),
                P("消耗类别", "使用消耗货币类别：", 374, 57, 121, 495, 57, 108,
                    ItemCurrencies),
            },
            ["拆零关小黑屋"] = new[]
            {
                P("等于数字", "(进小黑屋)拆分值=：", 38, 10, 145, 183, 10, 108),
                P("超过数字", "(进小黑屋)拆分值>：", 350, 10, 145, 495, 10, 108),
                P("小黑屋", "小黑屋编号(@开头代表触发脚本)：", 0, 57, 183, 183, 57, 108),
                P("是否删除违规物", "是否删除违规物：", 398, 57, 97, 495, 57, 72,
                    DeleteChoices),
            },
            ["主号新切割"] = new[]
            {
                P("是否触发脚本", "是否触发脚本:", 379, 49, 111, 495, 49, 72, YesNo),
            },
            ["月灵新伤害"] = new[]
            {
                P("轻击波动", "轻击伤害波动:", 82, 0, 100, 183, 0, 108),
                P("重击波动", "重击伤害波动:", 399, 0, 96, 495, 0, 108),
            },
            ["自定义红名村"] = new[]
            {
                P("坐标x", "红名村坐标x:", 88, 0, 95, 183, 0, 108),
                P("地图编号", "红名村地图编号(最多7字符):", 325, 0, 170, 495, 0, 108),
                P("坐标y", "红名村坐标y:", 88, 47, 95, 183, 47, 108),
            },
            ["沙巴克攻城范围"] = new[]
            {
                P("最小x坐标", "最小x坐标值:", 88, 0, 95, 183, 0, 108),
                P("最大x坐标", "最大x坐标值:", 88, 47, 95, 183, 47, 108),
                P("最小y坐标", "最小y坐标值:", 88, 94, 95, 183, 94, 108),
                P("最大y坐标", "最大y坐标值:", 88, 141, 95, 183, 141, 108),
            },
            ["沙巴克复活点"] = new[]
            {
                P("落点x坐标", "x坐标值:", 113, 0, 70, 183, 0, 108),
                P("随机偏移", "随机偏移值:", 399, 0, 96, 495, 0, 108),
                P("落点y坐标", "y坐标值:", 113, 47, 70, 183, 47, 108),
            },
            ["英雄切割"] = new[]
            {
                P("是否触发脚本", "是否触发脚本:", 360, 49, 130, 495, 49, 72, YesNo),
            },
            ["诱惑之光修改"] = new[]
            {
                P("成功概率", "诱惑之光成功概率(1/值):", 17, 0, 145, 162, 0, 108),
                P("v1", "【变量控制生效】v1", 327, 0, 118, 445, 0, 108),
                P("最大等级", "最大可诱惑等级:", 59, 34, 103, 162, 34, 108),
                P("固定参数", "是否变量控制:", 327, 34, 118, 445, 34, 108,
                    new[] { "固定参数", "变量控制" }),
                P("变黄概率", "诱惑过黄名概率(1/值):", 17, 68, 145, 162, 68, 108),
                P("禁止诱惑", "禁止诱惑他人宝宝:", 327, 68, 118, 445, 68, 108,
                    new[] { "允许诱惑", "禁止诱惑" }),
                P("丢失概率", "诱惑准确度概率(1/值):", 17, 102, 145, 162, 102, 108),
                P("诱惑lv增加", "技能lv增加诱惑概率:", 323, 102, 122, 445, 102, 108),
                P("怪掉HP", "怪掉1%HP,概率提升值:", 317, 136, 128, 445, 136, 108),
            },
            ["主号分身术"] = new[]
            {
                P("触发cd", "触发分身术cd(s):", 59, 0, 103, 162, 0, 108),
                P("分身个数", "每次召唤分身个数:", 322, 0, 122, 445, 0, 108),
                P("是否变量控制", "是否Sets变量控制:", 310, 34, 134, 445, 34, 108, YesNo),
                P("原版技能生效", "直接让原分身术生效:", 310, 68, 134, 445, 68, 108, YesNo),
            },
            ["额外技能"] = new[]
            {
                P("变量控制倍数", "是否Sets变量控制伤害加成:", 287, 34, 158, 445, 34, 108, YesNo),
            },
            ["气功波重定义"] = new[]
            {
                P("范围", "固定范围修改:", 310, 0, 134, 445, 0, 108),
                P("是否变量控制", "是否Sets变量修改范围:", 287, 34, 157, 445, 34, 108, YesNo),
            },
            ["概率格挡"] = new[]
            {
                P("是否触发脚本", "是否触发脚本:", 354, 34, 90, 445, 34, 108, YesNo),
                P("是否格挡切割", "是否格挡切割:", 354, 68, 90, 445, 68, 108, YesNo),
            },
            ["英雄分身修复"] = new[]
            {
                P("触发cd", "触发分身术cd(s):", 59, 0, 103, 162, 0, 108),
                P("减少cd", "每级技能减少cd(s):", 322, 0, 122, 445, 0, 108),
                P("分身数量", "技能每N级增加1个分身:", 23, 34, 139, 162, 34, 108),
            },
            ["英雄开天修复"] = new[]
            {
                P("触发cd", "触发开天斩cd(s):", 59, 0, 103, 162, 0, 108),
                P("减少cd", "每级技能减少cd(s):", 322, 0, 122, 445, 0, 108),
                P("触发MP", "MP低于指定值不触发:", 23, 34, 139, 162, 34, 108),
            },
            ["合击等级修改"] = new[]
            {
                P("触发等级", "英雄合击可发动的等级:", 21, 0, 141, 162, 0, 108),
            },
        };

        private static readonly Dictionary<string, string> Notes = new(StringComparer.Ordinal)
        {
            ["切割也反伤"] = "仅当勾选旧版眼神配置1的【攻击反伤】和【新版的主号切割】配合后，勾选此项目有效果\r\n勾选后切割纳入反伤效果，不勾选切割值不纳入反伤计算",
            ["火墙不反伤"] = "仅当勾选旧版眼神配置1的【攻击反伤】后，勾选此项目有效果\r\n勾选后火墙不会收到反伤效果",
            ["反伤带抗性"] = "仅当勾选旧版眼神配置1的【攻击反伤】后，勾选此项目有效果\r\n勾选后角色和怪物才能有用反伤抗性设置",
            ["千分比属性"] = "增加了千分比属性用法，\r\n1、主号：\r\n增加千分比最大hp = sets(1,151,千分比值) \r\n增加千分比最大攻击 = sets(1,153,千分比值);\r\n增加千分比最低攻击 = sets(1,152,千分比值);\r\n增加千分比最大魔法 = sets(1,155,千分比值);\r\n增加千分比最低魔法 = sets(1,154,千分比值);\r\n增加千分比最大道术 = sets(1,157,千分比值);\r\n增加千分比最低道术 = sets(1,156,千分比值);\r\n增加千分比最大防御 = sets(1,159,千分比值);\r\n增加千分比最低防御 = sets(1,158,千分比值);\r\n增加千分比最大魔御 = sets(1,161,千分比值);\r\n增加千分比最低魔御 = sets(1,160,千分比值);\r\n增加千分比最大mp = sets(1,162,千分比值);  \r\n英雄：                           \r\n增加千分比最大hp = sets(1,163,千分比值);  \r\n增加千分比最大攻击 = sets(1,165,千分比值);\r\n增加千分比最低攻击 = sets(1,164,千分比值);\r\n增加千分比最大魔法 = sets(1,167,千分比值);\r\n增加千分比最低魔法 = sets(1,166,千分比值);\r\n增加千分比最大道术 = sets(1,169,千分比值);\r\n增加千分比最低道术 = sets(1,168,千分比值);\r\n增加千分比最大防御 = sets(1,171,千分比值);\r\n增加千分比最低防御 = sets(1,170,千分比值);\r\n增加千分比最大魔御 = sets(1,173,千分比值);\r\n增加千分比最低魔御 = sets(1,172,千分比值);\r\n增加千分比最大mp = sets(1,174,千分比值);",
            ["主号被攻击触发"] = "增加玩家被攻击触发脚本！,触发脚本在:\r\nmud2.0\\Mir200\\Envir\\PsMapQuest\\RunQuest.pas下\r\n函数名为【procedure Myattacked(roleid,magicId,types,hp:integer;Name:string);】\r\n参数意义：\r\nroleid：打我的对象id，\r\nmagicId：他用的什么魔法打我(如果用自定义技能，未传递id就是-1，传递了多少就是多少)【如果是怪物打你，传递的是怪物id】\r\ntypes：被什么类型的对象打，0代表人物，1代表怪物，2代表英雄\r\nhp：被打掉多少血量\r\nName：打人者的名字\r\n\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["主号新切割"] = "新增主号新切割功能，【为了配合旧版切割，用法完全不变】\r\n*****特别注意*****【和旧版切割同时勾选，效果就会翻倍，新版功能更强大】\r\nThis_Player.SetS(1, 9,  代表自己是否允许被切割【新版修改成防被人切割概率】，例：This_Player.SetS(1, 9, 100); ，值为100，代表我不会被人切割伤害，为0代表我会被人切割伤害\r\nThis_Player.SetS(1, 10, 对怪切割：代表百分比切割伤害，取值值为1 - 1000，例：This_Player.SetS(1, 10, 1); ，代表千分之1的伤害，超过1000，则为神圣伤害数值（超过1000后变成固定切割，且不受概率影响，百分百会切割）\r\nThis_Player.SetS(1, 11, 对怪切割：代表固定切割伤害，最大支持21亿\r\nThis_Player.SetS(1, 50, 对人切割：代表百分比切割伤害，This_Player.SetS(1, 50, 1)，千分之1的伤害(这个不能超过1000，超过就是秒杀)\r\nThis_Player.SetS(1, 51, 对人切割： 代表固定切割伤害，最大支持21亿\r\nThis_Player.SetS(1, 52, 对怪切割概率：物百分比切割概率 值为0或者 >= 4095都是百分百触发，如果值为1则触发概率为1 / 4095的触发概率\r\nThis_Player.SetS(1, 62, 对怪切割概率：固定切割概率  值为0或者 >= 4095都是百分百触发，如果值为1则触发概率为1 / 4095的触发概率\r\nThis_Player.SetS(1, 53, 对人切割概率：百分比切割概率 值为0或者 >= 4095都是百分百触发，如果值为1则触发概率为1 / 4095的触发概率\r\nThis_Player.SetS(1, 63, 对人切割概率： 对人固定切割概率  值为0或者 >= 4095都是百分百触发，如果值为1则触发概率为1 / 4095的触发概率\r\n勾选触发脚本即可触发\r\n触发脚本在：mud2.0\\Mir200\\Envir\\PsMapQuest\\RunQuest.pas下\r\n函数名为【procedure NewCutting(roleid,types,hp,curhp,maxhp:integer;Name:string);】\r\n参数意义（触发脚本慎用！因为每个怪物被切割都会触发，性能开销挺大的）：\r\nroleid：我切割的对象id，\r\ntypes：我切割的怪物类型，0代表人物，1代表怪物，2代表英雄\r\nhp：被切割了多少血，[伤害+切割值]\r\ncurhp：被打前怪物拥有多少hp\r\nmaxhp：被打人最大hp\r\nName：被切怪物名字\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["临时属性"] = "勾选后，临时属性可正数，可以负数不受限制！\r\n\r\n\r\n\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["新永久属性"] = "修改了老版本永久属性，仅当老版本永久属性不勾选，新永久属性勾选才能生效！\r\n【===========主号永久属性===========】\r\nThis_Player.SetS(1,12,(主号)永久加血上限)\r\nThis_Player.SetS(1,13,(主号)永久加蓝上限)\r\nThis_Player.SetS(1,14,(主号)永久加物理防御下限)\r\nThis_Player.SetS(1,15,(主号)永久加魔御下限)\r\nThis_Player.SetS(1,16,(主号)永久加攻击下限)\r\nThis_Player.SetS(1,17,(主号)永久加魔法下限)\r\nThis_Player.SetS(1,18,(主号)永久加道术下限)\r\nThis_Player.SetS(1,19,(主号)永久加物理防御上限)\r\nThis_Player.SetS(1,20,(主号)永久加魔御上限)\r\nThis_Player.SetS(1,21,(主号)永久加攻击上限)\r\nThis_Player.SetS(1,22,(主号)永久加魔法上限)\r\nThis_Player.SetS(1,23,(主号)永久加道术上限)\r\nThis_Player.SetS(1,203,(主号)永久幸运)\r\nThis_Player.SetS(1,204,(主号)永久腕力)\r\nThis_Player.SetS(1,205,(主号)永久背包负重)\r\nThis_Player.SetS(1,206,(主号)永久穿戴负重)\r\nThis_Player.SetS(1,207,(主号)永久准确)\r\n【===========英雄永久属性===========】\r\nThis_Player.SetS(1,30,(英雄)永久加血上限)\r\nThis_Player.SetS(1,31,(英雄)永久加蓝上限)\r\nThis_Player.SetS(1,32,(英雄)永久加物理防御下限)\r\nThis_Player.SetS(1,33,(英雄)永久加魔御下限)\r\nThis_Player.SetS(1,34,(英雄)永久加攻击下限)\r\nThis_Player.SetS(1,35,(英雄)永久加魔法下限)\r\nThis_Player.SetS(1,36,(英雄)永久加道术下限)\r\nThis_Player.SetS(1,37,(英雄)永久加物理防御上限)\r\nThis_Player.SetS(1,38,(英雄)永久加魔御上限)\r\nThis_Player.SetS(1,39,(英雄)永久加攻击上限)\r\nThis_Player.SetS(1,40,(英雄)永久加魔法上限)\r\nThis_Player.SetS(1,41,(英雄)永久加道术上限)\r\nThis_Player.SetS(1,208,(英雄)永久幸运)\r\nThis_Player.SetS(1,209,(英雄)永久腕力)\r\nThis_Player.SetS(1,210,(英雄)永久背包负重)\r\nThis_Player.SetS(1,211,(英雄)永久穿戴负重)\r\nThis_Player.SetS(1,212,(英雄)永久准确)\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["金币上限突破"] = "修改金币上限超过5000万！\r\n\r\n\r\n\r\nThis_Player.SetS(1,15,(主号)永久加魔御下限)\r\nThis_Player.SetS(1,16,(主号)永久加攻击下限)\r\nThis_Player.SetS(1,17,(主号)永久加魔法下限)\r\nThis_Player.SetS(1,18,(主号)永久加道术下限)",
            ["修复飘血数值"] = "修复引擎客户端角色飘雪数值超过32万错误显示问题\r\n[本功能暂时先删除，勾选后无效果，等下次有空重写]\r\n勾选后必须修改客户端补丁，数值最大支持21亿！\r\n\r\n\r\n\r\nThis_Player.SetS(1,17,(主号)永久加魔法下限)\r\nThis_Player.SetS(1,18,(主号)永久加道术下限)",
            ["自定义循环函数"] = "勾选后可以指定无限制数量的runques.pas里面函数定时自动运行\r\n切记只能指定无参数的函数\r\n指定功能必须同时勾选眼神特殊函数配合使用\r\n例如：Ys_SetTimerByName(This_Player, 3000, 'MyTimer')\r\n这个'MyTimer'就是runques.pas里面函数 函数名字可以随意命名，但是必须无参数的函数才支持\r\n3000代表每3秒钟执行一次，最快500毫秒就是0.5秒，不能更快。\r\ntimer=0表示清理指定 函数名字 的定时器\r\nYs_SetTimerByName(This_Player,0,'ClearAll');表示清理所有定时器",
            ["月灵不扣蓝"] = "月灵宝宝攻击敌人时，不扣除主人蓝，也不计算主人蓝是否够用！\r\n【特别注意：需详情了解可以浏览器输入pay.510youxi.com里面有关注群号！】",
            ["月灵新伤害"] = "勾选后月灵攻击敌人不会因为敌人跑动而落空\r\n新增加了月灵新伤害：This_Player.SetS(1,215,月灵轻击伤害千分比调整\r\n新增加了月灵新伤害：This_Player.SetS(1,216,月灵重击伤害千分比调整\r\n还新增了伤害波动范围\r\n轻击波动范围或者重击波动范围如果等于0，则伤害不波动\r\n如果设置大于0，假如为10，就是增加千分之10或者减少千分之10的伤害区间波动",
            ["瞬移怪物"] = "被人定制，过一段时间解锁！\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["杀怪触发"] = "勾选后，玩家每杀死一个怪物，就会触发脚本\r\n本功能开销很大，请谨慎使用\r\n使用者必须是代码高手，才建议使用\r\n触发的脚本名字procedure KillMonster(MonName,MapName:string);参数分别是【怪物名字】和【怪物所在地图】(切记不是玩家所在地图，是死亡的怪物所在地图);",
            ["自定义红名村"] = "可以修改红名村地图编号和坐标,切记别乱填，按说明填写！\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["沙巴克攻城范围"] = "勾选后，土城沙巴克的攻城打仗区域可以进行修改！",
            ["沙巴克复活点"] = "修改沙巴克成员死亡和行会回城落点位置！",
            ["英雄切割"] = "新增英雄切割功能，必须要至少打出一点伤害才能触发切割！\r\nThis_Player.SetS(2,1,x);x:就是千分比切割值\r\nThis_Player.SetS(2,2,x);x:就是固定切割值\r\nThis_Player.SetS(2,3,x);x:触发切割千分比概率，小于0默认百分百触发\r\n勾选触发脚本就可以触发：\r\n触发脚本在：mud2.0\\Mir200\\Envir\\PsMapQuest\\RunQuest.pas下\r\n函数名为【procedure HeroCutting(roleid,types,hp,curhp,maxhp:integer;Name:string);】\r\n参数意义（触发脚本慎用！因为每个怪物被切割都会触发，性能开销挺大的）：\r\nroleid：我切割的对象id，\r\ntypes：我切割的怪物类型，0代表人物，1代表怪物，2代表英雄\r\nhp：被切割了多少血，[伤害+切割值]\r\ncurhp：被打前怪物拥有多少hp\r\nmaxhp：被打人最大hp\r\nName：被切怪物名字\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["指定英雄放技能"] = "勾选后可以使用脚本指定英雄放指定id技能！\r\n只支持道士和法师技能！\r\n可以指定持续释放，也可以指定当前释放一次！\r\n函数依然保存在AllFuc.pas中，使用说明也在AllFu.pas的使用说明之中\r\n函数名字：function Ys_SetHeroCSkill(Player:TPlayer;magicid,isrun:integer):integer;\r\nmagicid;将要释放技能id\r\nisrun;0表示当前释放一次，=1表示一直持续释放这个id的技能\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["英雄不自动释放技能"] = "专人定制的\r\n勾选后，定制者指定的技能不会自动释放\r\n只能靠函数释放\r\n其他gm对此毫无用处的\r\n【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】",
            ["npc自定义函数"] = "勾选后可以使用npc自定义函数脚本\r\nEnvir\\CommonScripts\\眼神专用\\NpcFuc.pas\r\n里面有各种npc重写函数，详细请查看npcfuc.pas使用说明",
            ["诱惑之光修改"] = "【诱惑之光修改了诱惑概率】启动后每提升一级，增加一个可诱惑数量，0级最多诱惑2个，3级5个；以此类推，\r\n1、=====固定参数的用法====：\r\n成功概率：0：代表百分百成功，100=1/100概率成功 \r\n最大等级:可以诱惑最大等级的怪物是多少【原版60级】;\r\n过黄名概率：0：代表百分百跳过黄名字，100=1/100跳过黄名，【如果变黄名诱惑是失败的原版=1/2】 ;\r\n准确的概率：0：诱惑技能不丢失，100=1/100准确度【原版=1/3】;\r\n===========v变量控制==========;\r\n【变量控制生效】v1：就是This_Player.GetV(v1,v2)中的第一个参数v1的值;\r\n假如v1=11，则：\r\nThis_Player.GetV(11,1)=成功概率【意义和固定值一样，只是受玩家个人变量而变化】;\r\nThis_Player.GetV(11,2)=最大等级【意义和固定值一样，只是受玩家个人变量而变化】;\r\nThis_Player.GetV(11,3)=过黄名概率【意义和固定值一样，只是受玩家个人变量而变化】;\r\nThis_Player.GetV(11,4)=准确的概率【意义和固定值一样，只是受玩家个人变量而变化】;",
            ["主号分身术"] = "开放了主号分身术 \r\n以前函数触发可以直接用，同时新增了新的直接用法 \r\n直接让原分身术生效:【否】，表示用老方案，脚本召唤分身 This_Player.Give('####眼神分身',1); \r\n直接让原分身术生效:【是】，直接用分身术技能就可以召唤分身 \r\n是否s变量控制选择【否】，就是面板控制数量和cd \r\n如果s变量控制选择【是】： \r\n那么This_Player.SetS(1,200,cd值) \r\n那么This_Player.SetS(1,201,个数值)",
            ["额外技能"] = "新增加了技能额外伤害 \r\n1、【外层key就是技能id，必须填写数字类型的字符串，千万别填字母或汉字】；2、技能名字只是备注，可以不填。3、技能特效id必须是数字\r\n1、【伤害加成是千分比】，1000就是伤害不增不减，1500就是1.5倍加成，500就是0.5倍削弱 \r\n伤害间隔：表示多次伤害默认每次伤害触发间隔，为毫秒级别！ \r\n1、技能主属性：0，1，2；分别代表：攻击，魔法，道术；2、技能范围：0代表指向性，大于0代表群伤\r\n单次伤害情况：1、【每次触发概率】：额外伤害次数，每次会触发的概率\r\n单次伤害情况：2、【每次触发加成】：每次触发可以设置额外加成千分比\r\n是否s变量控制，技术不够的选择【否】，选择【是】就是用This_Player.SetS(8,魔法id,伤害倍数值)\r\n以上功能均是额外伤害生效，原生伤害不受控制。\r\n点击打开配置文件就可以打开文件路径！",
            ["真隐身术修复"] = "修复了临时属性真隐身术隐身后不走动，外面不显示的bug，【眼神的自定义函数id是60】 \r\n使用函数function ys_SubShuxing(Player:TPlayer;round,TargetX,TargetY,value,time,pid,roleid,effect:integer):integer; \r\npid=60,就是真隐身，time是隐身时间，其他参数化说明具体去看AllFuc.pas的使用说明。",
            ["主号技能加成"] = "新增了主号技能可以加成或者削弱千分比\r\n控制变量 This_Player.SetS(7,magicid,加成千分比)\r\n变量小于等于0就无视",
            ["气功波重定义"] = "新增了气功波可以修改距离自己中心为多少的距离均可推动 \r\n允许s变量控制设置为【是】就看角色 This_Player.SetS(1,202,气功波贴身距离修改)\r\n注意：这个距离指的是修改之前的贴身推",
            ["自定义野蛮"] = "新增了自定义野蛮的冲撞距离可以变量控制\r\n控制变量 This_Player.SetS(1,213,野蛮冲撞距离)",
            ["概率格挡"] = "将以前星耀的概率格挡功能移植到这里\r\n控制变量This_Player.SetS(1,100：千分比概率触发格挡。\r\n特别注意：格挡成功后会触发函数【procedure MyBlocking()】;。\r\n所以必须在RunQuest.pas里面添加procedure MyBlocking()函数，不然会提示缺少函数。",
            ["刺杀免伤"] = "将以前星耀的刺杀免伤功能移植到这里\r\n控制变量This_Player.SetS(1,101：刺杀免伤百分比。",
            ["安全区禁止诱惑和圣言"] = "勾选后圣言术和诱惑之光对安全区的敌人无法使用",
            ["技能弹射"] = "勾选后可以使用弹射技能或溅射技能\r\n使用方法需要引入AllFuc.pas然后使用函数为角色赋技能特殊属性\r\n函数名字具体每个参数含义参考Allfuc.pas的使用说明 \r\nYs_TanTanSkill(Player:TPlayer;MagicId,x,y,roleid,times,round,double,cutting,effectid,js:integer) \r\n给原生技能设定弹射属性Ys_TanTanSkill(玩家对象;魔法id,坐标可填0,坐标可填0,对象可以填0,弹射次数,弹射范围(别太大),弹射倍数,伤害填0,特效id,0为弹射1为溅射) \r\n设定后只要不重启服务器，技能一直有这种效果，不会消失 \r\n函数也可以再super触发使用，那样可以不设定指定的id，其他填0参数就可以填值生效了！",
            ["英雄分身修复"] = "英雄分身术进行修复，【定制者可用，其他人等八月份解锁勾选可用！】",
            ["英雄开天修复"] = "英雄开天斩触发规则进行修复，【定制者可用，其他人等八月份解锁勾选可用！】",
            ["合击等级修改"] = "新增了英雄合击的可以修改的等级\r\n默认43级\r\n请正确填写，别乱填写",
            ["英雄技能加成"] = "新增了英雄技能可以加成或者削弱千分比\r\n控制变量 This_Player.SetS(8,magicid,加成千分比)\r\n变量小于等于0就无视",
            ["英雄技能点数增加"] = "新增了英雄技能可以额外增加伤害点数\r\n控制变量 This_Player.SetS(9,magicid,额外伤害点数)\r\n变量小于等于0就无视",
            ["怪物伤害触发技能特效"] = "新增加了怪物技能特效和伤害配置 \r\n1、外层key就是怪物的名字，配置存在[MyJson\\skills\\monskillext.json]\r\n1、【伤害加成是千分比】，1000就是伤害不增不减，1500就是1.5倍加成，500就是0.5倍削弱 \r\n伤害间隔：表示多次伤害默认每次伤害触发间隔，为毫秒级别！ \r\n伤害范围是在玩家攻击位置的范围内造成\r\n单次伤害情况：1、【每次触发概率】：额外伤害次数，每次会触发的概率\r\n单次伤害情况：2、【每次触发加成】：每次触发可以设置额外加成千分比\r\n点击打开配置文件就可以打开文件路径！",
        };

        public static IReadOnlyList<ParameterLayout> Layout(string featureName) =>
            Layouts.TryGetValue(featureName, out var layouts)
                ? layouts
                : Array.Empty<ParameterLayout>();

        public static string Note(string featureName) =>
            Notes.TryGetValue(featureName, out var note) ? note : string.Empty;

        public static int ParameterOrder(string featureName, string key)
        {
            if (!Layouts.TryGetValue(featureName, out var layouts)) return int.MaxValue;
            for (var index = 0; index < layouts.Length; index++)
                if (string.Equals(key, featureName + "_" + layouts[index].ParameterSuffix,
                        StringComparison.Ordinal))
                    return index;
            return int.MaxValue;
        }

        private static ParameterLayout P(
            string suffix,
            string caption,
            int labelX,
            int y,
            int labelWidth,
            int inputX,
            int inputY,
            int inputWidth,
            string[] options = null) =>
            new(suffix, caption,
                new Rectangle(labelX, y, labelWidth, 23),
                new Rectangle(inputX, inputY, inputWidth, 23),
                options);
    }

    internal sealed class ReplicaPageCatalog
    {
        public Dictionary<string, IReadOnlyList<ReplicaFeature>> Pages { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<ReplicaFeature> ExtensionFeatures { get; set; } = Array.Empty<ReplicaFeature>();
        public HashSet<string> AssignedKeys { get; set; } = new(StringComparer.Ordinal);
    }

    internal static class YanshenPageCatalog
    {
        public static readonly string[] ExtensionCategoryOrder =
        {
            "物品相关", "角色相关", "技能相关", "爆率相关", "脚本相关"
        };

        private static readonly Dictionary<string, string[]> PageMainKeys = new(StringComparer.Ordinal)
        {
            ["盘古1"] = new[]
            {
                "土城摆摊", "摆摊穿人", "随身仓库", "召唤神兽", "召唤骷髅",
                "攻沙脚本控制", "全服击杀提示", "专职变性", "安全区禁止丢物", "邮件防刷",
                "穿人穿怪", "禁止交易地图", "禁止宝宝休息", "行会显示", "修复刺杀位麻痹",
                "修复卡防御", "地面物品消失时间", "下线宝宝死亡", "屏蔽属性提升提示",
                "屏蔽发言频繁禁言功能", "屏蔽元宝增减信息", "指定地图编号摆摊", "关闭摆摊",
                "限制摆摊", "死亡触发", "回城按钮触发", "盘古穿戴触发", "盘古物理攻击触发",
                "盘古魔法攻击触发", "挖矿触发", "召唤骷髅触发", "召唤神兽触发", "心灵启示触发",
                "踢玩家下线", "脚本控制头发外显", "盘古高级属性", "盘古击杀触发",
                "盘古杀死宝宝", "盘古给与封号", "防0拆分", "屏蔽元宝数据库日志"
            },
            ["盘古2"] = new[]
            {
                "武器绿毒", "SetNoKillMapLv脚本触发", "噬魂沼泽绿毒修复", "物功带毒", "法师群毒",
                "雷电带毒", "半月带毒", "攻城修改", "复活戒指重设", "破复活", "野蛮麻痹",
                "火墙设置时间上限", "盘古爆裂火焰范围", "盘古地狱雷光范围", "盘古冰咆哮的范围",
                "盘古流星火雨范围", "基本剑术", "攻杀剑术", "刺杀剑术", "半月弯刀",
                "烈火剑法", "逐日剑法", "设置玩家称号函数", "名字变色", "ServerSay函数",
                "删除英雄技能", "等级禁言"
            },
            ["盘古3"] = new[]
            {
                "无极真气", "施毒术", "中毒时间上限", "战士合击", "法道合击", "屏蔽排行榜",
                "装备吸血", "脚本控制人物爆率", "人物爆率调整", "装备提升人物爆率", "修改召唤神兽"
            },
            ["配置1"] = new[]
            {
                "全屏拾取", "刀刀切割", "永久属性", "特殊属性", "复活触发脚本", "复活戒指改cd",
                "复活戒指概率", "被击杀触发", "移动速度", "攻击反伤", "捡物触发", "攻击触发",
                "魔法攻击触发", "新穿戴触发", "禁止装备自动绑定", "AddLimLF函数修改",
                "IncActivePoint函数修改", "新倍攻和暴击", "give极品", "麻痹概率", "英雄穿戴触发",
                "英雄攻速移速", "BB杀怪触发", "临时大背包", "英雄倍攻和暴击", "BB死亡触发",
                "特殊宝宝", "英雄施法速度", "读取英雄装备", "装备来源", "上线触发", "千分比免伤",
                "永久攻速"
            },
            ["配置2"] = new[]
            {
                "地狱雷光系数", "地狱雷光范围", "爆裂火焰可换主属性", "爆裂火焰范围及系数",
                "地狱雷光可换主属性", "激光电影可换主属性", "激光范围及系数", "激光命中概率",
                "火球主属性切换", "火球自定义范围", "雷电主属性切换", "雷电自定义范围",
                "冰咆哮主属性切换", "冰咆哮范围", "火雨主属切换", "魔法盾修正",
                "嗜血术倍数", "免毒符", "野蛮等级", "禁止发言不提示", "中毒飘血",
                "删除技能不提示", "升级技能不提示", "群毒", "群毒值"
            },
            ["眼神2(第1页)"] = new[]
            {
                "自定义元素", "英雄自动开盾", "装备转生穿戴判定a", "诱惑之光触发脚本a", "主号分身术a",
                "烈火固定增伤", "冰咆哮固定增伤", "火墙固定增伤", "火符固定增伤",
                "雷电术切割", "烈火切割", "冰咆哮切割", "火符切割",
                "火墙切割", "技能等级突破", "宝宝自动叛变", "新呼唤宝宝", "嗜血术范围",
                "技能触发脚本", "全屏吸怪", "主号施法速度", "英雄千分比免伤", "自定义伤害",
                "装备多职业", "角色多阵营", "战队职业限制", "英雄读取极品", "主号高级暴击",
                "高级英雄倍功暴击", "获取沙城归属", "穿戴触发_plus", "切换暴击报文"
            },
            ["眼神2(第2页)"] = new[]
            {
                "道士合击系数", "伤害触发脚本_plus", "自定义伤害_plus", "高级物理攻击触发",
                "高级魔法攻击触发", "英雄物理攻击触发", "英雄魔法攻击触发", "毫秒级cd记录",
                "千分比经验倍数", "新怪物爆率", "全局循环函数", "高级回收", "眼神特殊函数",
                "攻击吸血", "英雄野蛮", "super攻击触发", "火墙不吸血"
            }
        };

        private static readonly Dictionary<string, string[]> ExplicitParameters = new(StringComparer.Ordinal)
        {
            ["召唤神兽"] = new[] { "神兽_序号", "神兽_数量" },
            ["召唤骷髅"] = new[] { "召唤骷髅_数量" },
            ["指定地图编号摆摊"] = new[] { "摆摊地图" },
            ["限制摆摊"] = new[] { "限制摆摊_左x", "限制摆摊_左y", "限制摆摊_右x", "限制摆摊_右y", "限制摆摊_等级" },
            ["攻城修改"] = new[] { "攻城修改_天数", "攻城修改_小时", "攻城修改_分钟", "攻城时长_分钟" },
            ["火墙设置时间上限"] = new[] { "火墙_时间" },
            ["人物爆率调整"] = new[] { "非红名K值", "红名K值", "最大装备数量" },
            ["修改召唤神兽"] = new[]
            {
                "人物等级1_值", "怪物名字1_值", "怪物数量1_值", "人物等级2_值", "怪物名字2_值",
                "怪物数量2_值", "人物等级3_值", "怪物名字3_值", "怪物数量3_值"
            },
            ["群毒"] = new[] { "绿毒_A", "绿毒_B", "绿毒_最低", "红毒_A", "红毒_B", "双毒时间_最低" },
            ["主号施法速度"] = new[] { "主号全局法速" },
            ["新怪物爆率"] = new[] { "怪物爆率A_值", "怪物爆率B_值", "怪物爆率K_值" },
            ["全局循环函数"] = new[] { "循环时间_值" },
        };

        private static readonly Dictionary<string, string> DisplayAliases = new(StringComparer.Ordinal)
        {
            ["群毒值"] = "自定义群毒公式",
            ["限制摆摊"] = "限制坐标区域和玩家等级摆摊PRO",
            ["设置玩家称号函数"] = "设置玩家称号函数_支持80字符",
            ["随机极品"] = "启用随机极品",
            ["装备转生穿戴判定a"] = "装备转生穿戴判定",
            ["诱惑之光触发脚本a"] = "诱惑之光触发脚本",
            ["主号分身术a"] = "主号分身术",
            ["烈火固定增伤"] = "烈火威力增加",
            ["冰咆哮固定增伤"] = "冰咆哮威力增加",
            ["火墙固定增伤"] = "火墙威力增加",
            ["火符固定增伤"] = "火符威力增加",
            ["战队职业限制"] = "取消入战队职业限制",
            ["主号高级暴击"] = "主号高级倍功暴击",
        };

        public static ReplicaPageCatalog Build(IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows)
        {
            var result = new ReplicaPageCatalog();
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pageName in YanshenConfigForm.OriginalLegacyPages.Concat(
                         YanshenConfigForm.OriginalSeasonTwoPages.Take(2)))
            {
                if (pageName == "盘古4")
                {
                    result.Pages[pageName] = BuildEquipmentPage(rows, used);
                    continue;
                }

                var mainKeys = PageMainKeys.TryGetValue(pageName, out var keys) ? keys : Array.Empty<string>();
                result.Pages[pageName] = BuildFeatures(rows, mainKeys, used);
            }

            var extension = BuildUnassignedFeatures(rows, used).ToList();
            result.ExtensionFeatures = extension;
            result.AssignedKeys = new HashSet<string>(used, StringComparer.Ordinal);
            foreach (var feature in extension)
                foreach (var key in feature.Keys)
                    result.AssignedKeys.Add(key);
            return result;
        }

        public static int ColumnsFor(string pageName) => pageName switch
        {
            "盘古1" => 5,
            "盘古2" => 4,
            "盘古3" => 3,
            "盘古4" => 3,
            "配置1" => 5,
            "配置2" => 4,
            "眼神2(第1页)" => 5,
            "眼神2(第2页)" => 3,
            _ => 4,
        };

        public static Color AccentFor(string pageName) => pageName switch
        {
            "盘古1" or "盘古2" or "配置1" or "眼神2(第1页)" or "眼神2(第2页)" => Color.Red,
            "盘古3" => Color.FromArgb(48, 128, 20),
            "盘古4" or "配置2" => Color.Blue,
            _ => SystemColors.ControlText,
        };

        private static IReadOnlyList<ReplicaFeature> BuildFeatures(
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows,
            IEnumerable<string> mainKeys,
            HashSet<string> used)
        {
            var result = new List<ReplicaFeature>();
            foreach (var key in mainKeys)
            {
                if (!rows.TryGetValue(key, out var mainRow)) continue;
                used.Add(key);
                var parameters = ParametersFor(rows, key, used);
                result.Add(new ReplicaFeature(mainRow, DisplayName(key), parameters, CategoryFor(key)));
            }
            return result;
        }

        private static IReadOnlyList<ReplicaFeature> BuildEquipmentPage(
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows,
            HashSet<string> used)
        {
            var result = new List<ReplicaFeature>();
            foreach (var key in new[] { "屏蔽自动绑定", "随机极品" })
            {
                if (!rows.TryGetValue(key, out var row)) continue;
                used.Add(key);
                result.Add(new ReplicaFeature(row, DisplayName(key), Array.Empty<TogglePanel.ToggleRow>(), "物品相关"));
            }

            foreach (var equipment in new[] { "武器", "衣服", "头盔", "项链", "手镯", "戒指" })
            {
                var parameters = rows.Values
                    .Where(row => !row.IsToggle && row.Key.StartsWith(equipment, StringComparison.Ordinal))
                    .OrderBy(row => EquipmentParameterOrder(row.Key))
                    .ThenBy(row => row.Key, StringComparer.Ordinal)
                    .ToArray();
                foreach (var parameter in parameters) used.Add(parameter.Key);
                result.Add(new ReplicaFeature(null, equipment + "类", parameters, "物品相关"));
            }
            return result;
        }

        private static IEnumerable<ReplicaFeature> BuildUnassignedFeatures(
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows,
            HashSet<string> used)
        {
            foreach (var row in rows.Values)
            {
                if (used.Contains(row.Key)) continue;
                used.Add(row.Key);
                var parameters = row.IsToggle ? ParametersFor(rows, row.Key, used) : Array.Empty<TogglePanel.ToggleRow>();
                var category = CategoryFor(row.Key);
                yield return new ReplicaFeature(row, DisplayName(row.Key), parameters, category);
            }
        }

        private static IReadOnlyList<TogglePanel.ToggleRow> ParametersFor(
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> rows,
            string mainKey,
            HashSet<string> used)
        {
            var result = new List<TogglePanel.ToggleRow>();
            if (ExplicitParameters.TryGetValue(mainKey, out var explicitKeys))
            {
                foreach (var key in explicitKeys)
                    if (rows.TryGetValue(key, out var row) && used.Add(key)) result.Add(row);
            }

            foreach (var row in rows.Values)
            {
                if (used.Contains(row.Key) || row.IsToggle) continue;
                if (!row.Key.StartsWith(mainKey + "_", StringComparison.Ordinal)) continue;
                used.Add(row.Key);
                result.Add(row);
            }
            return result;
        }

        private static string DisplayName(string key)
        {
            if (DisplayAliases.TryGetValue(key, out var display)) return display;
            return key.EndsWith('a') ? key[..^1] : key;
        }

        private static string CategoryFor(string key)
        {
            if (ContainsAny(key, "物品", "装备", "背包", "拾取", "捡物", "仓库", "绑定", "投保", "极品", "金币", "武器", "衣服", "头盔", "项链", "手镯", "戒指"))
                return "物品相关";
            if (ContainsAny(key, "人物", "角色", "英雄", "宝宝", "宠物", "行会", "战队", "摆摊", "名字", "称号", "等级", "下线", "职业", "阵营", "沙城"))
                return "角色相关";
            if (ContainsAny(key, "技能", "剑", "刀", "火", "雷", "毒", "术", "攻击", "伤害", "切割", "麻痹", "吸血", "反伤", "魔法", "召唤", "骷髅", "神兽", "盾", "格挡", "合击", "冰咆哮", "激光"))
                return "技能相关";
            if (ContainsAny(key, "爆率", "爆物", "全服击杀提示")) return "爆率相关";
            return "脚本相关";
        }

        private static int EquipmentParameterOrder(string key)
        {
            if (key.Contains("最高点数", StringComparison.Ordinal)) return 0;
            if (key.Contains("点数几率", StringComparison.Ordinal)) return 1;
            if (key.Contains("属性几率", StringComparison.Ordinal)) return 2;
            if (key.Contains("最随机性", StringComparison.Ordinal)) return 3;
            return 4;
        }

        private static bool ContainsAny(string value, params string[] fragments) =>
            fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class ClassicTabControl : TabControl
    {
        public int FillRightOverflow { get; set; }
        public int ContentLeftCompensation { get; set; }

        public override Rectangle DisplayRectangle
        {
            get
            {
                var rectangle = base.DisplayRectangle;
                if (ContentLeftCompensation <= 0) return rectangle;

                // Keep the selected page fixed while its native tab header moves right.
                rectangle.X -= ContentLeftCompensation;
                rectangle.Width += ContentLeftCompensation;
                return rectangle;
            }
        }

        protected override void SetBoundsCore(
            int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (Dock == DockStyle.Fill && FillRightOverflow > 0 && Parent != null)
                width = Parent.ClientSize.Width - x + FillRightOverflow;
            base.SetBoundsCore(x, y, width, height, specified);
        }

        protected override void OnHandleCreated(EventArgs eventArgs)
        {
            base.OnHandleCreated(eventArgs);
            NativeMethods.SetWindowTheme(Handle, string.Empty, string.Empty);
        }
    }

    internal sealed class YanshenSingleLineLabel : Label
    {
        private Font _rendererFont;

        public YanshenSingleLineLabel()
        {
            AutoSize = false;
            UseCompatibleTextRendering = true;
        }

        protected override void OnFontChanged(EventArgs eventArgs)
        {
            base.OnFontChanged(eventArgs);
            _rendererFont?.Dispose();
            _rendererFont = YanshenUiFont.CreateTextRenderer(
                Font.FontFamily.Name, Font.SizeInPoints, DeviceDpi, Font.Style);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaintBackground(eventArgs);
            var bounds = ClientRectangle;
            if (BorderStyle == BorderStyle.FixedSingle)
            {
                ControlPaint.DrawBorder(eventArgs.Graphics, bounds, SystemColors.WindowFrame,
                    ButtonBorderStyle.Solid);
                bounds.Inflate(-1, -1);
            }
            else if (BorderStyle == BorderStyle.Fixed3D)
            {
                ControlPaint.DrawBorder3D(eventArgs.Graphics, bounds, Border3DStyle.Sunken);
                bounds.Inflate(-2, -2);
            }

            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                        TextFormatFlags.NoPrefix;
            flags |= TextAlign switch
            {
                ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter =>
                    TextFormatFlags.HorizontalCenter,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight =>
                    TextFormatFlags.Right,
                _ => TextFormatFlags.Left,
            };
            flags |= TextAlign switch
            {
                ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight =>
                    TextFormatFlags.VerticalCenter,
                ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight =>
                    TextFormatFlags.Bottom,
                _ => TextFormatFlags.Top,
            };
            TextRenderer.DrawText(eventArgs.Graphics, Text, _rendererFont ?? Font, bounds,
                Enabled ? ForeColor : SystemColors.GrayText, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _rendererFont?.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class YanshenWrappedTextLabel : Label
    {
        private Font _rendererFont;

        public YanshenWrappedTextLabel()
        {
            AutoSize = false;
            UseCompatibleTextRendering = true;
        }

        protected override void OnFontChanged(EventArgs eventArgs)
        {
            base.OnFontChanged(eventArgs);
            _rendererFont?.Dispose();
            _rendererFont = YanshenUiFont.CreateTextRenderer(
                Font.FontFamily.Name, Font.SizeInPoints, DeviceDpi, Font.Style);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaintBackground(eventArgs);
            var bounds = ClientRectangle;
            if (BorderStyle == BorderStyle.FixedSingle)
            {
                ControlPaint.DrawBorder(eventArgs.Graphics, bounds, SystemColors.WindowFrame,
                    ButtonBorderStyle.Solid);
                bounds.Inflate(-1, -1);
            }
            else if (BorderStyle == BorderStyle.Fixed3D)
            {
                ControlPaint.DrawBorder3D(eventArgs.Graphics, bounds, Border3DStyle.Sunken);
                bounds.Inflate(-2, -2);
            }

            var lines = Text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var lineBounds = new Rectangle(
                    bounds.X, bounds.Y + index * 13, bounds.Width, 13);
                TextRenderer.DrawText(eventArgs.Graphics, lines[index], _rendererFont ?? Font,
                    lineBounds, Enabled ? ForeColor : SystemColors.GrayText,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _rendererFont?.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class YanshenSingleLineButton : Button
    {
        private Font _rendererFont;
        private bool _pressed;

        public YanshenSingleLineButton()
        {
            AutoEllipsis = false;
            UseCompatibleTextRendering = true;
        }

        protected override void OnFontChanged(EventArgs eventArgs)
        {
            base.OnFontChanged(eventArgs);
            _rendererFont?.Dispose();
            _rendererFont = YanshenUiFont.CreateTextRenderer(
                Font.FontFamily.Name, Font.SizeInPoints, DeviceDpi, Font.Style);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            base.OnMouseDown(eventArgs);
            if (eventArgs.Button != MouseButtons.Left) return;
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            base.OnMouseUp(eventArgs);
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            base.OnMouseLeave(eventArgs);
            if (!_pressed) return;
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            var state = !Enabled
                ? ButtonState.Inactive
                : _pressed ? ButtonState.Pushed : ButtonState.Normal;
            ControlPaint.DrawButton(eventArgs.Graphics, ClientRectangle, state);
            var textBounds = Rectangle.Inflate(ClientRectangle, -3, -2);
            textBounds.Offset(0, -2);
            TextRenderer.DrawText(eventArgs.Graphics, Text, _rendererFont ?? Font, textBounds,
                Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(eventArgs.Graphics,
                    Rectangle.Inflate(ClientRectangle, -4, -4));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.Style &= ~0x2000; // BS_MULTILINE
                return parameters;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _rendererFont?.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class ClassicButton : Button
    {
        public ClassicButton()
        {
            AutoEllipsis = true;
            FlatStyle = FlatStyle.System;
        }

        protected override void OnHandleCreated(EventArgs eventArgs)
        {
            base.OnHandleCreated(eventArgs);
            NativeMethods.SetWindowTheme(Handle, string.Empty, string.Empty);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.Style &= ~0x2000; // BS_MULTILINE
                return parameters;
            }
        }
    }

    internal static class NativeMethods
    {
        private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr handle, string subAppName, string subIdList);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        internal static DpiAwarenessScope EnterDpiUnaware()
        {
            var previous = SetThreadDpiAwarenessContext(DpiAwarenessContextUnaware);
            if (previous == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Unable to enter the Yanshen DPI compatibility context (Win32 {Marshal.GetLastWin32Error()}).");
            return new DpiAwarenessScope(previous);
        }

        internal readonly struct DpiAwarenessScope : IDisposable
        {
            private readonly IntPtr _previous;

            internal DpiAwarenessScope(IntPtr previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_previous != IntPtr.Zero)
                    SetThreadDpiAwarenessContext(_previous);
            }
        }
    }
}
