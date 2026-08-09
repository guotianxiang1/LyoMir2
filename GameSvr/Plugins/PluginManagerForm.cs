using System.Text.Json;
using GameSvr;

namespace GameSvr.Plugins
{
    public partial class PluginManagerForm : Form
    {
        private readonly PluginManager _manager;
        private readonly PluginConfigPanel _panel;
        private readonly Dictionary<string, CheckBox> _switches = new();
        private readonly Dictionary<string, Label> _labels = new();
        private Label _lblStatus;
        private ListView _logView;
        private System.Windows.Forms.Timer _logTimer;

        private static readonly FeatureToggle[] _toggles =
        {
            new() { Id = "elements.enabled",       Label = "17元素系统",       Category = "装备" },
            new() { Id = "damage.cuttingEnabled",   Label = "自定义伤害",       Category = "战斗" },
            new() { Id = "damage.paralysisEnabled", Label = "麻痹控制",         Category = "战斗" },
            new() { Id = "damage.poisonEnabled",    Label = "自定义施毒",       Category = "战斗" },
            new() { Id = "skills.pushEnabled",      Label = "推开/拉进",        Category = "战斗" },
            new() { Id = "skills.lifeStealEnabled", Label = "吸血",             Category = "战斗" },
            new() { Id = "vacuum.enabled",          Label = "吸怪",             Category = "战斗" },
            new() { Id = "skills.bounceEnabled",    Label = "弹射/溅射技能",    Category = "技能" },
            new() { Id = "skills.fireWallEnabled",  Label = "自定义火墙",       Category = "技能" },
            new() { Id = "pet.enabled",             Label = "宝宝/宠物系统",    Category = "宠物" },
            new() { Id = "pet.specialAttrEnabled",  Label = "宝宝倍攻/切割",    Category = "宠物" },
            new() { Id = "hero.skillControl",       Label = "英雄技能控制",     Category = "英雄" },
            new() { Id = "item.autoPickupEnabled",  Label = "全屏拾取",         Category = "物品" },
            new() { Id = "item.autoRecycleEnabled", Label = "自动回收",         Category = "物品" },
            new() { Id = "item.bindEnabled",        Label = "物品绑定/解绑",    Category = "物品" },
            new() { Id = "db.allowScriptSql",       Label = "脚本SQL操作",      Category = "数据库" },
            new() { Id = "db.itemDataApi",          Label = "物品数据API",      Category = "数据库" },
            new() { Id = "group.infoEnabled",       Label = "组队信息查询",     Category = "组队" },
            new() { Id = "rename.enabled",          Label = "在线改名",         Category = "角色" },
            new() { Id = "kick.enabled",            Label = "强制下线",         Category = "角色" },
            new() { Id = "timer.loopEnabled",       Label = "无限定时器",       Category = "系统" },
            new() { Id = "cd.millisecondEnabled",   Label = "毫秒级CD",         Category = "系统" },
        };

        public PluginManagerForm(PluginManager manager)
        {
            _manager = manager;
            _panel = new PluginConfigPanel(manager);

            // Larger default — 80% of parent or screen
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size((int)(screen.Width * 0.65), (int)(screen.Height * 0.78));
            MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterParent;
            Text = "眼神集成 — 功能开关";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;

            BuildUI();
            LoadToggleStates();
        }

        // ===== Build full UI with docking =====

        void BuildUI()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 360,
                FixedPanel = FixedPanel.None,
                Panel1MinSize = 300,
                Panel2MinSize = 80,
            };

            split.Panel1.Controls.Add(BuildToggleArea());
            split.Panel2.Controls.Add(BuildLogArea());

            Controls.Add(split);
        }

        Control BuildToggleArea()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            // Top bar: title + status + buttons
            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 54,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 4, 0, 0),
                WrapContents = false,
            };
            var title = new Label { Text = "功能开关", Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 2, 20, 0) };
            var desc = new Label { Text = "关闭不需要的功能提升性能，即时生效。", Font = new Font("Microsoft YaHei UI", 9f), ForeColor = Color.Gray, AutoSize = true, Margin = new Padding(0, 5, 30, 0) };
            _lblStatus = new Label { Text = "● 就绪", ForeColor = Color.Green, AutoSize = true, Margin = new Padding(0, 5, 10, 0) };
            var btnOn = new Button { Text = "全部开启", Size = new Size(80, 24), FlatStyle = FlatStyle.Flat };
            btnOn.Click += (s, e) => SetAll(true);
            var btnOff = new Button { Text = "全部关闭", Size = new Size(80, 24), FlatStyle = FlatStyle.Flat };
            btnOff.Click += (s, e) => SetAll(false);
            var btnDef = new Button { Text = "默认", Size = new Size(60, 24), FlatStyle = FlatStyle.Flat };
            btnDef.Click += (s, e) => { _panel.ApplyYanshenDefaults(); LoadToggleStates(); Flush("默认设置已应用", Color.Orange); };

            topBar.Controls.AddRange(new Control[] { title, desc, _lblStatus, btnOn, btnOff, btnDef });

            // Tab control with toggle grid — fills the rest
            var tabs = new TabControl { Dock = DockStyle.Fill, Top = topBar.Bottom };
            foreach (var cat in _toggles.GroupBy(t => t.Category))
            {
                var page = new TabPage(cat.Key) { AutoScroll = true };
                var flow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    Padding = new Padding(8, 4, 8, 4),
                };
                foreach (var t in cat)
                    flow.Controls.Add(MakeRow(t));
                page.Controls.Add(flow);
                tabs.TabPages.Add(page);
            }

            panel.Controls.Add(tabs);
            panel.Controls.Add(topBar);
            return panel;
        }

        Panel MakeRow(FeatureToggle t)
        {
            var row = new FlowLayoutPanel
            {
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(4, 2, 4, 2),
                AutoSize = true,
            };

            var chk = new CheckBox
            {
                Appearance = Appearance.Button,
                Text = "",
                Size = new Size(22, 22),
                Tag = t.Id,
                Margin = new Padding(0, 4, 8, 0),
            };
            chk.CheckedChanged += OnToggle;
            _switches[t.Id] = chk;

            var lbl = new Label
            {
                Text = t.Label,
                Font = new Font("Microsoft YaHei UI", 10f),
                AutoSize = true,
                Width = 140,
                Margin = new Padding(0, 5, 0, 0),
            };

            var status = new Label
            {
                Text = "关",
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                ForeColor = Color.Red,
                AutoSize = true,
                Margin = new Padding(16, 5, 12, 0),
            };
            _labels[t.Id] = status;

            var desc = new Label
            {
                Text = Desc(t.Id),
                Font = new Font("Microsoft YaHei UI", 8f),
                ForeColor = Color.DarkGray,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0),
            };

            row.Controls.AddRange(new Control[] { chk, lbl, status, desc });
            return row;
        }

        // ===== Log area =====

        Control BuildLogArea()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 8, 4) };
            var header = new Label { Text = "!!!! 命令执行日志", Dock = DockStyle.Top, Height = 22, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };

            _logView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Consolas", 9f),
            };
            _logView.Columns.Add("时间", 64);
            _logView.Columns.Add("命令", 56);
            _logView.Columns.Add("参数内容", 400);
            _logView.Columns.Add("结果", 52);

            panel.Controls.Add(_logView);
            panel.Controls.Add(header);

            _logTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _logTimer.Tick += (s, e) =>
            {
                // In a real implementation, read from shared log buffer
            };
            _logTimer.Start();

            return panel;
        }

        // ===== Toggle Logic =====

        void LoadToggleStates()
        {
            foreach (var t in _toggles)
            {
                if (!_switches.TryGetValue(t.Id, out var chk)) continue;
                bool isOn = _manager.GetPluginSetting("YanshenCompat", t.Id, false);
                chk.CheckedChanged -= OnToggle;
                chk.Checked = isOn;
                chk.CheckedChanged += OnToggle;
                UpdateVisual(t.Id, isOn);
            }
        }

        void OnToggle(object s, EventArgs e)
        {
            if (s is CheckBox chk && chk.Tag is string key) Apply(key, chk.Checked);
        }

        void Apply(string key, bool on)
        {
            UpdateVisual(key, on);
            var saved = _manager.SetPluginSetting("YanshenCompat", key, on);
            Flush(saved ? $"{(on ? "开启" : "关闭")}: {key}" : $"保存失败: {key}",
                saved ? (on ? Color.Green : Color.Red) : Color.DarkRed);
        }

        void UpdateVisual(string key, bool on)
        {
            if (_labels.TryGetValue(key, out var lbl)) { lbl.Text = on ? "开" : "关"; lbl.ForeColor = on ? Color.Green : Color.Red; }
            if (_switches.TryGetValue(key, out var chk)) chk.BackColor = on ? Color.FromArgb(46, 204, 113) : Color.FromArgb(231, 76, 60);
        }

        void SetAll(bool on)
        {
            var cfg = _manager.GetPluginOwnedConfig("YanshenCompat");
            foreach (var t in _toggles)
            {
                cfg[t.Id] = on;
                if (_switches.TryGetValue(t.Id, out var chk)) { chk.CheckedChanged -= OnToggle; chk.Checked = on; chk.CheckedChanged += OnToggle; }
                UpdateVisual(t.Id, on);
            }
            var saved = _manager.SavePluginConfig("YanshenCompat", cfg);
            Flush(saved ? (on ? "全部开启" : "全部关闭") : "保存失败", saved ? Color.Green : Color.DarkRed);
        }

        async void Flush(string msg, Color c) { _lblStatus.Text = msg; _lblStatus.ForeColor = c; await Task.Delay(3000); _lblStatus.Text = "● 就绪"; _lblStatus.ForeColor = Color.Green; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logTimer?.Stop();
                _logTimer?.Dispose();
                _logTimer = null;
            }
            base.Dispose(disposing);
        }

        static string Desc(string id) => id switch
        {
            "elements.enabled" => "装备17元素设置/读取 (命令15,17,18,24,32)",
            "damage.cuttingEnabled" => "自定义伤害/神圣/HP-MP/经验 (命令1,3,11-14,29,34,39,40)",
            "damage.paralysisEnabled" => "自定义麻痹 (命令2)",
            "damage.poisonEnabled" => "自定义施毒 (命令5)",
            "skills.pushEnabled" => "推开/拉进/定身 (命令4,9)",
            "skills.lifeStealEnabled" => "吸血 (命令8)",
            "vacuum.enabled" => "全屏吸怪 (命令27)",
            "skills.bounceEnabled" => "弹射/溅射技能 (命令26)",
            "skills.fireWallEnabled" => "自定义火墙+技能经验 (命令10,37)",
            "pet.enabled" => "宝宝属性/技能/跟随 (命令23,30,35)",
            "pet.specialAttrEnabled" => "宝宝倍攻/切割/暴击/连击 (命令31)",
            "hero.skillControl" => "指定英雄释放技能 (命令28)",
            "item.autoPickupEnabled" => "全屏自动拾取 (命令19)",
            "item.autoRecycleEnabled" => "自动回收/地面物品 (命令7,16,22)",
            "item.bindEnabled" => "物品绑定/解绑/检查 (命令21,33)",
            "db.allowScriptSql" => "脚本中执行SQL语句",
            "db.itemDataApi" => "获取物品完整数据API",
            "group.infoEnabled" => "组队成员计数/名字/怪物数 (命令20,36,38)",
            "rename.enabled" => "在线改名功能",
            "kick.enabled" => "强制玩家下线 (命令41)",
            "timer.loopEnabled" => "无限循环定时器 (命令25)",
            "cd.millisecondEnabled" => "毫秒级CD计时器",
            _ => "",
        };
    }
}
