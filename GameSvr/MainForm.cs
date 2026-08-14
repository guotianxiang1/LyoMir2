using System.Collections.Concurrent;
using System.Reflection;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public partial class MainForm : Form
    {
        public static MainForm Instance { get; private set; }
        private RichTextBox _console;
        private StatusStrip _status;
        private ToolStripStatusLabel _lblStatus;
        private NotifyIcon _trayIcon;
        private YanshenConfigForm _yanshenConfigForm;
        private ToolStripMenuItem _yanshenSettingsMenu;
        private bool _restoreYanshenFromTray;
        private readonly DateTime _startTime = DateTime.Now;

        public MainForm()
        {
            Instance = this;

            InitUI();
            Shown += (_, _) => Task.Run(StartServer);
        }

        void InitUI()
        {
            Size = new Size(900, 600);
            Text = "M2Server - " + (M2Share.g_Config?.sServerName ?? "热血传奇");

            try {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("GameSvr.Resources.M2Server_icon_000_1imgs.ico"))
                    if (s != null) Icon = new Icon(s);
            } catch { /* icon not embedded in this build */ }

            var menu = new MenuStrip();
            var svc = new ToolStripMenuItem("服务(&S)");
            svc.DropDownItems.Add("启动(&T)", null, (s, e) => Task.Run(StartServer));
            svc.DropDownItems.Add("退出(&X)", null, (s, e) => Close());
            menu.Items.Add(svc);

            var plugins = new ToolStripMenuItem("插件(&P)");
            plugins.DropDownItems.Add("M2超级伴侣(&Y)", null, (_, _) => ShowYanshenConfig());
            menu.Items.Add(plugins);
            Controls.Add(menu);
            MainMenuStrip = menu;

            _console = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(18, 18, 30),
                ForeColor = Color.FromArgb(212, 212, 212),
                Font = new Font("Consolas", 9.5f),
                WordWrap = true,
            };
            Controls.Add(_console);

            _status = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel("就绪") { Spring = true };
            _status.Items.Add(_lblStatus);
            Controls.Add(_status);

            _trayIcon = new NotifyIcon { Text = "M2Server", Icon = Icon, Visible = true };
            _trayIcon.DoubleClick += (_, _) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                if (_restoreYanshenFromTray)
                    ShowYanshenConfig();
            };

            // ★ 定时器: 从队列取日志 → 直接写 RichTextBox (UI线程,不用BeginInvoke)
            var t = new System.Windows.Forms.Timer { Interval = 100 };
            t.Tick += (_, _) => M2Share.FlushLogQueue(_console);
            t.Start();

            // 状态刷新
            var t2 = new System.Windows.Forms.Timer { Interval = 3000 };
            t2.Tick += (_, _) =>
            {
                var up = DateTime.Now - _startTime;
                var pc = M2Share.UserEngine?.OnlinePlayObject ?? 0;
                _lblStatus.Text = $"玩家: {pc} | 运行: {(int)up.TotalHours}h{up.Minutes}m | 内存: {GC.GetTotalMemory(false) / 1024 / 1024}MB";
            };
            t2.Start();
        }

        void StartServer()
        {
            var appService = AppService.Instance;
            if (appService == null)
            {
                M2Share.ErrorMessage("AppService 未启动，游戏服务器无法初始化。");
                return;
            }
            appService.OnFormReady();
        }

        void ShowYanshenConfig()
        {
            _restoreYanshenFromTray = false;
            var manager = M2Share.PluginManager;
            if (manager == null)
            {
                MessageBox.Show("眼神兼容引擎尚未初始化。", "M2超级伴侣",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_yanshenConfigForm == null || _yanshenConfigForm.IsDisposed)
            {
                using (NativeMethods.EnterDpiUnaware())
                {
                    _yanshenConfigForm = new YanshenConfigForm(manager);
                    _yanshenConfigForm.FormClosed += (_, _) => _yanshenConfigForm = null;
                    _yanshenConfigForm.Show(this);
                }
                return;
            }

            if (_yanshenConfigForm.WindowState == FormWindowState.Minimized)
                _yanshenConfigForm.WindowState = FormWindowState.Normal;
            if (!_yanshenConfigForm.Visible)
                _yanshenConfigForm.Show(this);
            _yanshenConfigForm.BringToFront();
            _yanshenConfigForm.Activate();
        }

        internal bool EnableYanshenSettingsEntry()
        {
            if (_yanshenSettingsMenu != null) return false;

            _yanshenSettingsMenu = new ToolStripMenuItem("设置(&O)");
            _yanshenSettingsMenu.DropDownItems.Add(
                "M2超级伴侣(&Y)", null, (_, _) => ShowYanshenConfig());
            MainMenuStrip.Items.Add(_yanshenSettingsMenu);
            return true;
        }

        internal void MinimizeYanshenToTray()
        {
            if (_yanshenConfigForm == null || _yanshenConfigForm.IsDisposed) return;
            _restoreYanshenFromTray = true;
            _yanshenConfigForm.Hide();
            _trayIcon.Visible = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // @0x79BAD0 / @0x79BAF4 — 关闭服务器?(Yes = 关闭) / 正在关闭服务器...
                var r = MessageBox.Show("关闭服务器?(Yes = 关闭)", "M2Server",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.No) { e.Cancel = true; WindowState = FormWindowState.Minimized; return; }
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                M2Share.MainOutMessage("正在关闭服务器...");
            }
            _yanshenConfigForm?.Close();
            _trayIcon?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
