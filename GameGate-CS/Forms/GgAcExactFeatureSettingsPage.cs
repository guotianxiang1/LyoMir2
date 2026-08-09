using GameGate.Core;

namespace GameGate.Forms;

internal static class GgAcExactFeatureStyle
{
    public static readonly Color Back = Color.FromArgb(240, 240, 240);
    public static readonly Color Red = Color.FromArgb(210, 0, 0);
    public static readonly Font NormalFont = new("宋体", 9f, FontStyle.Regular);

    public static Label Label(string text, ContentAlignment alignment = ContentAlignment.MiddleLeft,
        Color? color = null) => new()
    {
        Text = text,
        AutoSize = false,
        TextAlign = alignment,
        Font = NormalFont,
        ForeColor = color ?? Color.Black,
        BackColor = Color.Transparent,
        UseMnemonic = false
    };

    public static TextBox Input(string value, bool multiline = false) => new()
    {
        Text = value,
        Multiline = multiline,
        ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        Font = NormalFont,
        ForeColor = Color.Black,
        BackColor = Color.White,
        BorderStyle = BorderStyle.Fixed3D
    };

    public static CheckBox Check(string text, bool value) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = false,
        Font = NormalFont,
        ForeColor = Color.Black,
        BackColor = Back,
        UseVisualStyleBackColor = true,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public static GroupBox Group(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, width, height),
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        Font = NormalFont,
        ForeColor = Color.Black,
        BackColor = Back,
        Padding = new Padding(5)
    };
}

public sealed class GgAcExactFeatureSettingsPage : UserControl
{
    private const int DesignPageWidth = 375;
    private const int DesignBodyHeight = 305;

    private readonly GateServer _server;
    private readonly GateConfig _config;
    private readonly Dictionary<string, string> _loadedIni;
    private readonly Dictionary<string, TextBox> _strongNumbers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _rawText =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _rawFlags =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackBar> _rawTracks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyRawKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Button> _tabButtons = [];
    private readonly List<Panel> _pages = [];
    private readonly ToolTip _compatibilityTip = new()
    {
        AutoPopDelay = 10_000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true
    };

    private readonly Panel _tabStrip;
    private readonly Panel _pageHost;
    private readonly Button _previousTabButton;
    private readonly Button _nextTabButton;
    private readonly TextBox _gateAddress;
    private readonly TextBox _serverAddress;
    private readonly TextBox _title;
    private readonly Label _status;
    private int _selectedTab;
    private int _firstVisibleTab;

    public GgAcExactFeatureSettingsPage(GateServer server)
        : this(server, server.Config)
    {
    }

    public GgAcExactFeatureSettingsPage(GateServer server, GateConfig config)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loadedIni = _config.ReadIniValues();

        Dock = DockStyle.Fill;
        Size = new Size(387, 370);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = GgAcExactFeatureStyle.Back;
        Font = GgAcExactFeatureStyle.NormalFont;

        _tabStrip = new Panel
        {
            Dock = DockStyle.None,
            Size = new Size(387, 23),
            BackColor = GgAcExactFeatureStyle.Back,
            Margin = Padding.Empty
        };
        string[] tabNames = ["网络", "参数", "速度", "功能", "离线", "防挂", "防御", "物品"];
        for (int index = 0; index < tabNames.Length; index++)
        {
            int tabIndex = index;
            var button = new Button
            {
                Text = tabNames[index],
                Font = GgAcExactFeatureStyle.NormalFont,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                UseVisualStyleBackColor = false,
                BackColor = GgAcExactFeatureStyle.Back,
                ForeColor = Color.Black,
                Margin = Padding.Empty
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(165, 165, 165);
            button.FlatAppearance.BorderSize = 1;
            button.Click += (_, _) => SelectTab(tabIndex);
            _tabButtons.Add(button);
            _tabStrip.Controls.Add(button);
        }

        _previousTabButton = CreateArrowButton("◀", "向左滚动页签");
        _nextTabButton = CreateArrowButton("▶", "向右滚动页签");
        _previousTabButton.Click += (_, _) => ScrollTabWindow(-1);
        _nextTabButton.Click += (_, _) => ScrollTabWindow(1);
        _tabStrip.Controls.Add(_previousTabButton);
        _tabStrip.Controls.Add(_nextTabButton);
        _tabStrip.Resize += (_, _) => LayoutTabStrip();

        _pageHost = new Panel
        {
            Dock = DockStyle.None,
            BackColor = GgAcExactFeatureStyle.Back,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = Padding.Empty
        };

        var networkPage = CreatePage();
        var networkInputs = BuildNetworkPage(networkPage);
        _gateAddress = networkInputs.GateAddress;
        _serverAddress = networkInputs.ServerAddress;
        AddPage(networkPage);

        var parameterPage = CreatePage();
        _title = BuildParameterPage(parameterPage);
        AddPage(parameterPage);

        var speedPage = CreatePage();
        BuildSpeedPage(speedPage);
        AddPage(speedPage);

        var featurePage = CreatePage();
        BuildFeaturePage(featurePage);
        AddPage(featurePage);

        var offlinePage = CreatePage();
        BuildOfflinePage(offlinePage);
        AddPage(offlinePage);

        var antiCheatPage = CreatePage();
        BuildAntiCheatPage(antiCheatPage);
        AddPage(antiCheatPage);

        var defensePage = CreatePage();
        BuildDefensePage(defensePage);
        AddPage(defensePage);

        var itemPage = CreatePage();
        BuildItemPage(itemPage);
        AddPage(itemPage);

        var footer = new Panel
        {
            Dock = DockStyle.None,
            Size = new Size(387, 31),
            BackColor = GgAcExactFeatureStyle.Back,
            Margin = Padding.Empty
        };
        _status = GgAcExactFeatureStyle.Label("可用参数保存后即时生效",
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        var save = new Button
        {
            Name = "SaveSettingsButton",
            AccessibleName = "保存基本设置",
            Text = "确定(O)",
            Font = GgAcExactFeatureStyle.NormalFont,
            Size = new Size(78, 25),
            UseVisualStyleBackColor = true,
            TabStop = true
        };
        save.Click += (_, _) => SaveSettings();
        footer.Controls.Add(_status);
        footer.Controls.Add(save);
        footer.Resize += (_, _) =>
        {
            save.SetBounds(Math.Max(5, footer.ClientSize.Width - 83), 3, 78, 25);
            _status.SetBounds(7, 2, Math.Max(1, save.Left - 12), 27);
        };

        Controls.Add(_pageHost);
        Controls.Add(footer);
        Controls.Add(_tabStrip);
        void LayoutRoot()
        {
            int width = Math.Max(1, ClientSize.Width);
            int height = Math.Max(54, ClientSize.Height);
            _tabStrip.SetBounds(0, 0, width, 23);
            _pageHost.SetBounds(0, 23, width, height - 54);
            footer.SetBounds(0, height - 31, width, 31);
        }
        Resize += (_, _) => LayoutRoot();
        LayoutRoot();
        PerformLayout();
        LayoutTabStrip();
        SelectTab(0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _compatibilityTip.Dispose();
        base.Dispose(disposing);
    }

    private static Button CreateArrowButton(string text, string accessibleName)
    {
        var button = new Button
        {
            Text = text,
            AccessibleName = accessibleName,
            Font = new Font("Arial", 7f, FontStyle.Regular),
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
            UseVisualStyleBackColor = false,
            BackColor = GgAcExactFeatureStyle.Back,
            ForeColor = Color.Black,
            Margin = Padding.Empty
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(165, 165, 165);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private void LayoutTabStrip()
    {
        int width = Math.Max(1, _tabStrip.ClientSize.Width);
        int tabsArea = Math.Max(7, width - 44);
        int tabWidth = Math.Max(1, tabsArea / 7);
        int tabsWidth = tabWidth * 7;
        for (int index = 0; index < _tabButtons.Count; index++)
        {
            bool visible = index >= _firstVisibleTab && index < _firstVisibleTab + 7;
            _tabButtons[index].Visible = visible;
            if (!visible) continue;
            int visibleIndex = index - _firstVisibleTab;
            _tabButtons[index].SetBounds(visibleIndex * tabWidth, 0,
                tabWidth + (visibleIndex == 6 ? tabsArea - tabsWidth : 0), 23);
        }

        int arrowSpace = Math.Max(2, width - tabsArea);
        int previousWidth = arrowSpace / 2;
        _previousTabButton.SetBounds(tabsArea, 0, previousWidth, 23);
        _nextTabButton.SetBounds(tabsArea + previousWidth, 0, arrowSpace - previousWidth, 23);
        _previousTabButton.Enabled = _firstVisibleTab > 0;
        _nextTabButton.Enabled = _firstVisibleTab < Math.Max(0, _tabButtons.Count - 7);
    }

    private void ScrollTabWindow(int direction)
    {
        int maximum = Math.Max(0, _tabButtons.Count - 7);
        _firstVisibleTab = Math.Clamp(_firstVisibleTab + direction, 0, maximum);
        LayoutTabStrip();
    }

    private static Panel CreatePage() => new()
    {
        Dock = DockStyle.Fill,
        Size = new Size(DesignPageWidth, DesignBodyHeight),
        BackColor = GgAcExactFeatureStyle.Back,
        Margin = Padding.Empty
    };

    private void AddPage(Panel page)
    {
        page.Visible = false;
        _pages.Add(page);
        _pageHost.Controls.Add(page);
    }

    private void SelectTab(int index)
    {
        if (_pages.Count == 0) return;
        _selectedTab = Math.Clamp(index, 0, _pages.Count - 1);
        if (_selectedTab < _firstVisibleTab)
            _firstVisibleTab = _selectedTab;
        else if (_selectedTab >= _firstVisibleTab + 7)
            _firstVisibleTab = _selectedTab - 6;
        LayoutTabStrip();
        for (int i = 0; i < _pages.Count; i++)
        {
            bool selected = i == _selectedTab;
            _pages[i].Visible = selected;
            _tabButtons[i].BackColor = selected ? Color.White : GgAcExactFeatureStyle.Back;
            _tabButtons[i].FlatAppearance.BorderColor = selected
                ? Color.FromArgb(105, 105, 105)
                : Color.FromArgb(165, 165, 165);
            if (selected) _pages[i].BringToFront();
        }
    }

    private (TextBox GateAddress, TextBox ServerAddress) BuildNetworkPage(Panel page)
    {
        var group = AddGroup(page, "网络设置", 1, 1, 371, 303);
        AddTextLabel(group, "网关的地址：", 12, 27, 77, 20, ContentAlignment.MiddleRight);
        var gateAddress = AddPlainInput(group,
            string.IsNullOrWhiteSpace(_config.GateAddr) ? "0.0.0.0" : _config.GateAddr,
            88, 27, 96, 20);
        AddTextLabel(group, "默认不改", 190, 27, 105, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        AddTextLabel(group, "网关端口号：", 12, 59, 77, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "GatePort", _config.GatePort, 88, 59, 40, 20);
        AddTextLabel(group, "看你心情", 134, 59, 105, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        AddTextLabel(group, "服务器地址：", 12, 91, 77, 20, ContentAlignment.MiddleRight);
        var serverAddress = AddPlainInput(group,
            string.IsNullOrWhiteSpace(_config.BackendIP) ? "127.0.0.1" : _config.BackendIP,
            88, 91, 96, 20);
        AddTextLabel(group, "看DBService.ini", 185, 91, 118, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        AddTextLabel(group, "服务器端口：", 12, 123, 77, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "DBServerPort", _config.BackendPort2, 88, 123, 40, 20);
        AddTextLabel(group, "默认不改", 185, 123, 105, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        const string restart = "网络监听与后端地址保存后需重启网关生效。";
        _compatibilityTip.SetToolTip(gateAddress, restart);
        _compatibilityTip.SetToolTip(_strongNumbers["GatePort"], restart);
        _compatibilityTip.SetToolTip(serverAddress, restart);
        _compatibilityTip.SetToolTip(_strongNumbers["DBServerPort"], restart);
        return (gateAddress, serverAddress);
    }

    private TextBox BuildParameterPage(Panel page)
    {
        var group = AddGroup(page, "基本参数", 1, 1, 371, 303);
        AddTextLabel(group, "网关标题：", 5, 19, 67, 20, ContentAlignment.MiddleRight);
        var title = AddPlainInput(group, _config.Title, 74, 19, 128, 20);

        AddTextLabel(group, "数据包允许大小：", 7, 41, 118, 20, ContentAlignment.MiddleRight);
        AddRawText(group, "Data", 131, 41, 70, 20, "20000");
        AddTextLabel(group, "B/秒", 207, 41, 45, 20);

        AddTextLabel(group, "IP上限：", 7, 63, 76, 20, ContentAlignment.MiddleRight);
        AddRawText(group, "Maxlan", 88, 63, 64, 20, "3");

        AddTextLabel(group, "NPC点击：", 7, 85, 76, 20, ContentAlignment.MiddleRight);
        AddRawText(group, "NpcClick", 88, 85, 64, 20, "1");

        AddTextLabel(group, "禁言时间：", 7, 107, 76, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "MuteTime", _config.MuteTime, 88, 107, 64, 20);
        AddTextLabel(group, "分", 158, 107, 24, 20);

        AddTextLabel(group, "吃药间隔：", 7, 129, 76, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "Cure", _config.CureInterval, 88, 129, 70, 20);

        AddTextLabel(group, "转身间隔：", 7, 151, 76, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "Turn", _config.TurnInterval, 88, 151, 70, 20);

        AddTextLabel(group, "商城购买：", 7, 173, 76, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "Shop", _config.ShopInterval, 88, 173, 70, 20);

        AddTextLabel(group, "攻速间隔：", 7, 195, 76, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "Attack", _config.AttackInterval, 88, 195, 70, 20);
        AddRawFlag(group, "AttacrGruel", "攻超速惩罚", 163, 195, 135, 20);

        AddTextLabel(group, "移速间隔：", 7, 217, 76, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "Walk", _config.WalkInterval, 88, 217, 70, 20);
        AddRawFlag(group, "WalkGruel", "移超速惩罚", 163, 217, 135, 20);

        AddTextLabel(group, "施法间隔：", 7, 239, 76, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "Cast", _config.CastInterval, 88, 239, 70, 20);
        AddRawFlag(group, "CastGruel", "法超速惩罚", 163, 239, 135, 20);

        AddTextLabel(group,
            "注意: 修改攻速, 移速, 施法速度, 请修改<速度>内的参数.\r\n这里的间隔是用来计算, 防止一些外挂超速的.",
            8, 263, 348, 34, ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        return title;
    }

    private void BuildSpeedPage(Panel page)
    {
        var global = AddGroup(page, "全局触发", 1, 1, 371, 101);
        AddTextLabel(global, "全局攻速属性：", 7, 17, 100, 20, ContentAlignment.MiddleRight);
        AddRawText(global, "AttInf", 111, 17, 47, 20, "3");
        AddTextLabel(global, "攻击速度+施法速度", 166, 17, 180, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        AddTextLabel(global, "全局体力恢复：", 7, 39, 100, 20, ContentAlignment.MiddleRight);
        AddRawText(global, "WalkInf", 111, 39, 47, 20);
        AddTextLabel(global, "移动速度", 166, 39, 100, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        AddRawFlag(global, "Globalspeed", "全局触发攻速/施法/移速", 8, 62, 204, 19);
        AddTextLabel(global, "可以和属性触发叠加", 216, 61, 140, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        var attribute = AddGroup(page, "属性触发", 1, 104, 371, 67);
        AddRawFlag(attribute, "AttrSpeed", "属性触发攻法速", 8, 19, 156, 20);
        AddRawFlag(attribute, "AttrWalk", "属性触发移速", 176, 19, 145, 20);
        AddRawFlag(attribute, "GlobalWalkspeed1", "敏捷>=200触发20属性移速", 8, 41, 248, 20);

        var formula = AddGroup(page, "计算公式+微调", 1, 173, 371, 130);
        AddTextLabel(formula, "攻速影响值(毫秒)\r\n(900-攻速属性*攻速影响值)",
            8, 17, 273, 28);
        AddStrongNumber(formula, "SpeedNum", _config.SpeedNum, 292, 22, 61, 20);
        AddTextLabel(formula, "施法影响值(毫秒)\r\n(2150-攻速属性*施法影响值)",
            8, 44, 273, 28);
        AddStrongNumber(formula, "SpellNum", _config.SpellNum, 292, 49, 61, 20);
        AddTextLabel(formula, "移速影响值(毫秒)\r\n(600-体力恢复*移速影响值)",
            8, 71, 273, 28);
        AddStrongNumber(formula, "WalkSpeedNum", _config.WalkSpeedNum, 292, 76, 61, 20);
        AddTextLabel(formula, "攻速属性>=此值触发双重施法：", 8, 101, 273, 20);
        AddRawText(formula, "CastSpeedNum", 292, 101, 61, 20, "1");
    }

    private void BuildFeaturePage(Panel page)
    {
        var group = AddGroup(page, "功能设置", 1, 1, 371, 303);
        AddRawFlag(group, "Offgold", "禁止丢金币", 8, 18, 164, 20);
        AddRawFlag(group, "offMail", "限制只能安全区领取邮件", 181, 18, 178, 20);
        AddRawFlag(group, "Rank", "关闭排行榜", 8, 40, 164, 20);
        AddRawFlag(group, "CompatStallGoldGuard", "摆摊元宝防刷", 8, 62, 178, 20,
            fallback: true);
        AddRawFlag(group, "CompatNoSplitZero", "禁止拆分0", 8, 84, 164, 20,
            fallback: true);
        AddRawFlag(group, "CompatRoleNameUnder12", "角色名长度<12位", 8, 106, 178, 20,
            fallback: true);

        AddTextLabel(group, "限制技能等级<", 13, 128, 96, 20);
        AddRawText(group, "SkilLvel", 109, 128, 39, 20);
        AddTextLabel(group, "0为不限制", 152, 128, 72, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        AddRawFlag(group, "CompatCloseItemAttrChat", "关闭物品属性聊天", 8, 150, 178, 20);

        AddRawFlag(group, "Offhero", "上线自动召唤英雄", 8, 172, 152, 20);
        AddTextLabel(group, "合击版本可用", 164, 172, 105, 20,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        AddTextLabel(group,
            "登陆自定义网关公告↓↓↓ 变量《Mname》《MLevel》《M》《Jb》",
            8, 194, 348, 36, ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        AddRawText(group, "CompatGateAnnouncement", 8, 234, 348, 43,
            multiline: true);
    }

    private void BuildOfflinePage(Panel page)
    {
        var group = AddGroup(page, string.Empty, 1, 1, 371, 303);
        AddRawFlag(group, "OfflineRobot", "触发安全区离线挂机", 12, 26, 210, 22);
        AddTextLabel(group, "触发等级：", 12, 59, 78, 20, ContentAlignment.MiddleRight);
        AddRawText(group, "Robotlevl", 95, 59, 85, 20);
        AddTextLabel(group, "触发职业：", 12, 93, 78, 20, ContentAlignment.MiddleRight);
        AddRawFlag(group, "Offlinejob1", "战士", 95, 92, 66, 21);
        AddRawFlag(group, "Offlinejob2", "魔法师", 167, 92, 80, 21);
        AddRawFlag(group, "Offlinejob3", "道士", 253, 92, 66, 21);
    }

    private TrackBar BuildAntiCheatPage(Panel page)
    {
        var group = AddGroup(page, "防挂设置", 1, 1, 371, 303);
        AddRawFlag(group, "split", "拆分类黑名单", 13, 22, 180, 21, fallback: true);
        AddRawFlag(group, "heart", "10分没心跳踢出", 13, 49, 180, 21);
        AddRawFlag(group, "speedmsg", "一次超速警告", 13, 76, 180, 21);
        AddRawFlag(group, "speedout", "二次超速踢出", 13, 103, 180, 21);
        AddRawFlag(group, "speedlistout", "二次加入角色黑名单", 13, 130, 200, 21);
        AddTextLabel(group, "黑名单时间：", 13, 165, 91, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "BlackTime", _config.BlackTime, 109, 165, 70, 20);
        AddTextLabel(group, "分", 184, 165, 25, 20);
        AddTextLabel(group, "严", 13, 203, 24, 22, ContentAlignment.MiddleCenter);
        var track = new TrackBar
        {
            Minimum = 1,
            Maximum = 3,
            Value = Math.Clamp(ReadRawInt("AntiCheatLevel", 1), 1, 3),
            TickFrequency = 1,
            TickStyle = TickStyle.BottomRight,
            SmallChange = 1,
            LargeChange = 1,
            Bounds = new Rectangle(38, 195, 284, 45),
            Font = GgAcExactFeatureStyle.NormalFont,
            BackColor = GgAcExactFeatureStyle.Back
        };
        track.Tag = "AntiCheatLevel";
        track.ValueChanged += (_, _) => _dirtyRawKeys.Add("AntiCheatLevel");
        _rawTracks["AntiCheatLevel"] = track;
        group.Controls.Add(track);
        SetCompatibilityTip(track, "AntiCheatLevel");
        AddTextLabel(group, "松", 326, 203, 24, 22, ContentAlignment.MiddleCenter);
        return track;
    }

    private void BuildDefensePage(Panel page)
    {
        var group = AddGroup(page, string.Empty, 1, 1, 371, 303);
        AddRawFlag(group, "Defend", "防御与封包开关：", 9, 19, 151, 21);
        AddTextLabel(group, "20230905新增封包判断", 164, 19, 190, 21,
            ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        AddTextLabel(group, "连接超时：", 9, 48, 87, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "DefenseConnect", ReadRawInt("ConnectTime", _config.Timeout0),
            100, 48, 72, 20);
        AddTextLabel(group,
            "↑↑↑连接超时是帐号登陆到角色界面时间,\r\n超过5000毫秒,我都感觉是恶意连接",
            184, 35, 171, 57, ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        AddTextLabel(group, "角色界面超时：", 9, 98, 87, 20, ContentAlignment.MiddleRight);
        AddStrongNumber(group, "DefenseRole", ReadRawInt("RoleTime", _config.Timeout1),
            100, 98, 72, 20);
        AddTextLabel(group, "↑↑↑角色界面超时,就是在角色界面停留的时间.",
            184, 92, 171, 38, ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        AddTextLabel(group, "上面两个超时的单位是毫秒,1000毫秒=1秒",
            11, 132, 344, 22, ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
        AddRawFlag(group, "NoEncrypt", "关闭网络协议加密：", 9, 157, 175, 21,
            fallback: true);
        AddTextLabel(group,
            "↑↑↑关闭请谨慎,战神数据协议加密,提高恶意修改封包难度\r\n即时生效运营中请勿关闭,会出现玩家断开连接,异常封包剔除.",
            11, 180, 344, 48, ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);

        AddRawText(group, "IPbai", 11, 239, 108, 20);
        var ipBypass = AddRawFlag(group, "IPbaiEnabled", "IP白名单不加密协议，假人专用",
            125, 232, 226, 34);
        ipBypass.ForeColor = GgAcExactFeatureStyle.Red;
    }

    private void BuildItemPage(Panel page)
    {
        var group = AddGroup(page, string.Empty, 1, 1, 371, 303);
        AddTextLabel(group, "延迟触发时间：", 11, 26, 96, 20, ContentAlignment.MiddleRight);
        AddRawText(group, "DelayTime", 112, 26, 79, 20);
        AddTextLabel(group, "秒", 196, 26, 25, 20);
        AddRawText(group, "Diytxt", 11, 63, 344, 22);
        AddTextLabel(group,
            "物品名 ↑↑↑ 例子：随机传送石  随机传送卷\r\n↑↑↑↑↑↑↑",
            11, 91, 344, 47, ContentAlignment.MiddleLeft, GgAcExactFeatureStyle.Red);
    }

    private static GroupBox AddGroup(Control parent, string text, int x, int y, int width,
        int height)
    {
        var group = GgAcExactFeatureStyle.Group(text, x, y, width, height);
        parent.Controls.Add(group);
        return group;
    }

    private static Label AddTextLabel(Control parent, string text, int x, int y, int width,
        int height, ContentAlignment alignment = ContentAlignment.MiddleLeft, Color? color = null)
    {
        var label = GgAcExactFeatureStyle.Label(text, alignment, color);
        label.Bounds = new Rectangle(x, y, width, height);
        parent.Controls.Add(label);
        return label;
    }

    private static TextBox AddPlainInput(Control parent, string value, int x, int y, int width,
        int height, bool multiline = false)
    {
        var input = GgAcExactFeatureStyle.Input(value, multiline);
        input.Bounds = new Rectangle(x, y, width, height);
        parent.Controls.Add(input);
        return input;
    }

    private TextBox AddStrongNumber(Control parent, string key, int value, int x, int y,
        int width, int height)
    {
        var input = AddPlainInput(parent, value.ToString(), x, y, width, height);
        input.TextAlign = HorizontalAlignment.Right;
        input.Tag = key;
        _strongNumbers[key] = input;
        return input;
    }

    private TextBox AddRawText(Control parent, string key, int x, int y, int width, int height,
        string fallback = "", bool multiline = false)
    {
        string value = _loadedIni.TryGetValue(key, out string? loaded) ? loaded : fallback;
        var input = AddPlainInput(parent, value, x, y, width, height, multiline);
        input.Tag = key;
        input.TextChanged += (_, _) => _dirtyRawKeys.Add(key);
        _rawText[key] = input;
        SetCompatibilityTip(input, key);
        return input;
    }

    private CheckBox AddRawFlag(Control parent, string key, string text, int x, int y, int width,
        int height, bool fallback = false)
    {
        bool value = _loadedIni.TryGetValue(key, out string? loaded)
            ? ParseBoolean(loaded)
            : fallback;
        var check = GgAcExactFeatureStyle.Check(text, value);
        check.Bounds = new Rectangle(x, y, width, height);
        check.Tag = key;
        check.CheckedChanged += (_, _) => _dirtyRawKeys.Add(key);
        _rawFlags[key] = check;
        parent.Controls.Add(check);
        SetCompatibilityTip(check, key);
        return check;
    }

    private void SetCompatibilityTip(Control control, string key) =>
        _compatibilityTip.SetToolTip(control,
            $"兼容配置项 {key}：保存到 MirGate.ini；当前网关未实现对应运行逻辑时仅作配置保留。");

    private int ReadRawInt(string key, int fallback) =>
        _loadedIni.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : fallback;

    private int StrongNumber(string key, int minimum = 0, int maximum = 1_000_000)
    {
        string text = _strongNumbers[key].Text.Trim();
        if (!int.TryParse(text, out int value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"{key} 必须是 {minimum} 到 {maximum} 之间的整数。");
        return value;
    }

    private void SaveSettings()
    {
        try
        {
            string gateAddress = _gateAddress.Text.Trim();
            string serverAddress = _serverAddress.Text.Trim();
            if (gateAddress.Length == 0) throw new InvalidOperationException("网关的地址不能为空。");
            if (serverAddress.Length == 0) throw new InvalidOperationException("服务器地址不能为空。");
            _config.GateAddr = gateAddress;
            _config.GatePort = StrongNumber("GatePort", 1, 65_535);
            _config.BackendIP = serverAddress;
            _config.BackendPort2 = StrongNumber("DBServerPort", 1, 65_535);
            _config.Title = _title.Text.Trim();
            _config.MuteTime = StrongNumber("MuteTime", 0, 525_600);
            _config.CureInterval = StrongNumber("Cure");
            _config.TurnInterval = StrongNumber("Turn");
            _config.ShopInterval = StrongNumber("Shop");
            _config.AttackInterval = StrongNumber("Attack");
            _config.WalkInterval = StrongNumber("Walk");
            _config.CastInterval = StrongNumber("Cast");
            _config.SpeedNum = StrongNumber("SpeedNum", 0, 5000);
            _config.SpellNum = StrongNumber("SpellNum", 0, 5000);
            _config.WalkSpeedNum = StrongNumber("WalkSpeedNum", 0, 5000);
            _config.GlobalSpeed = _rawFlags.TryGetValue("Globalspeed", out CheckBox? globalSpeed) && globalSpeed.Checked;
            _config.BlackTime = StrongNumber("BlackTime", 0, 525_600);
            _config.Timeout0 = StrongNumber("DefenseConnect");
            _config.Timeout1 = StrongNumber("DefenseRole");
            _config.Save();

            var rawValues = new List<KeyValuePair<string, string>>
            {
                new("Timeout0", _config.Timeout0.ToString()),
                new("Timeout1", _config.Timeout1.ToString()),
                new("ConnectTime", _config.Timeout0.ToString()),
                new("RoleTime", _config.Timeout1.ToString())
            };
            foreach (string key in _dirtyRawKeys)
            {
                string value = _rawText.TryGetValue(key, out TextBox? input)
                    ? input.Text.Trim()
                    : _rawFlags.TryGetValue(key, out CheckBox? flag)
                        ? FormatBoolean(key, flag.Checked)
                        : _rawTracks[key].Value.ToString();
                rawValues.Add(new KeyValuePair<string, string>(key, value));
                _loadedIni[key] = value;
            }
            _config.SaveIniValues(rawValues);
            _dirtyRawKeys.Clear();
            _server.ReloadRuntimeSettings();
            _status.Text = "已保存；已支持的参数已即时生效";
            _compatibilityTip.SetToolTip(_status, $"保存成功：{DateTime.Now:HH:mm:ss}");
            System.Media.SystemSounds.Asterisk.Play();
            if (FindForm() is Form dialog)
            {
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
        }
        catch (Exception ex)
        {
            IWin32Window owner = FindForm() is { } form ? form : this;
            MessageBox.Show(owner, $"功能设置保存失败：{ex.Message}", "GameGate",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool ParseBoolean(string value) =>
        value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("真", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("是", StringComparison.OrdinalIgnoreCase);

    private string FormatBoolean(string key, bool value)
    {
        string original = _loadedIni.GetValueOrDefault(key, string.Empty).Trim();
        if (original is "0" or "1") return value ? "1" : "0";
        if (original.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            original.Equals("false", StringComparison.OrdinalIgnoreCase))
            return value ? "true" : "false";
        if (original.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            original.Equals("no", StringComparison.OrdinalIgnoreCase))
            return value ? "yes" : "no";
        return value ? "真" : "假";
    }
}
