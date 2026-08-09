using System.Reflection;
using System.Diagnostics;
using System.Text;
using GameGate.Core;

namespace GameGate.Forms;

/// <summary>Main window reconstructed from the original GG_AC runtime window tree.</summary>
public sealed class ClassicMainForm : Form
{
    private const float UiScale = 0.55f;
    private const float FontScale = 0.75f;
    private const int TitleBarHeight = 31;
    private static readonly Color FrameBlue = Color.FromArgb(94, 196, 226);
    private readonly string _configDir;
    private readonly Panel _body;
    private readonly Label _titleCaption;
    private GateConfig _cfg;
    private GateServer? _server;
    private readonly RichTextBox _log;
    private readonly Label _connections;
    private readonly Label _backendState;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _running;
    private bool _closing;
    private Point _titleDragOrigin;
    private DateTime _lastM2RestartAttempt = DateTime.Now;

    public ClassicMainForm(string configDir, bool autoStart = true)
    {
        _configDir = configDir;
        _cfg = GateConfig.Load(configDir);

        string initialTitle = BuildWindowTitle();
        Text = initialTitle;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(S(1150), S(958) + TitleBarHeight);
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = FrameBlue;
        Icon = LoadApplicationIcon();
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("宋体", 12f);
        DoubleBuffered = true;
        ResizeRedraw = true;
        Paint += DrawBlueFrame;

        _body = new Panel
        {
            Location = new Point(3, TitleBarHeight),
            Size = new Size(ClientSize.Width - 6, ClientSize.Height - TitleBarHeight - 3),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Black,
            BackgroundImage = LoadBackground(),
            BackgroundImageLayout = ImageLayout.Stretch
        };
        Controls.Add(_body);

        var titleBar = new Panel
        {
            Location = new Point(3, 0),
            Size = new Size(ClientSize.Width - 6, TitleBarHeight),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = FrameBlue
        };
        _titleCaption = new Label
        {
            Text = initialTitle,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular),
            ForeColor = Color.Black,
            BackColor = FrameBlue
        };
        _titleCaption.MouseDown += BeginTitleDrag;
        _titleCaption.MouseMove += ContinueTitleDrag;
        titleBar.MouseDown += BeginTitleDrag;
        titleBar.MouseMove += ContinueTitleDrag;
        titleBar.Controls.Add(_titleCaption);

        var appIcon = new PictureBox
        {
            Image = Icon.ToBitmap(),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Location = new Point(7, 7),
            Size = new Size(17, 17),
            BackColor = Color.Transparent,
            TabStop = false
        };
        titleBar.Controls.Add(appIcon);

        var minimize = CreateTitleButton("−", FrameBlue);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var maximize = CreateTitleButton("□", Color.FromArgb(105, 198, 224));
        maximize.Enabled = false;
        var close = CreateTitleButton("X", Color.FromArgb(194, 76, 68));
        close.Click += (_, _) => Close();
        titleBar.Controls.Add(minimize);
        titleBar.Controls.Add(maximize);
        titleBar.Controls.Add(close);
        titleBar.Resize += (_, _) =>
        {
            close.SetBounds(titleBar.ClientSize.Width - 40, 0, 40, TitleBarHeight);
            maximize.SetBounds(close.Left - 40, 0, 40, TitleBarHeight);
            minimize.SetBounds(maximize.Left - 40, 0, 40, TitleBarHeight);
        };
        titleBar.PerformLayout();
        close.SetBounds(titleBar.ClientSize.Width - 40, 0, 40, TitleBarHeight);
        maximize.SetBounds(close.Left - 40, 0, 40, TitleBarHeight);
        minimize.SetBounds(maximize.Left - 40, 0, 40, TitleBarHeight);
        appIcon.BringToFront();
        minimize.BringToFront();
        maximize.BringToFront();
        close.BringToFront();
        Controls.Add(titleBar);
        titleBar.BringToFront();

        // Original runtime tree: Edit control ID 100.
        _log = new RichTextBox
        {
            Name = "control_100", Location = P(132, 45), Size = Z(850, 600),
            BackColor = Color.Black, ForeColor = Color.Lime, BorderStyle = BorderStyle.None,
            ReadOnly = true, Font = new Font("宋体", F(13.5f), FontStyle.Bold), ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false
        };
        _body.Controls.Add(_log);

        AddButton(180, "在线玩家", 136, 665, ShowOnlinePlayers);
        AddButton(210, "聊天监控", 270, 665, ShowChatMonitor);
        AddButton(230, "名单列表", 416, 665, ShowLists);
        AddButton(190, "M2监控", 560, 665, ShowM2Status);
        AddButton(240, "功能设置", 696, 665, ShowFeatureSettings);
        AddButton(250, "网络设置", 840, 665, ShowNetworkSettings);

        AddButton(310, "调试", 1040, 465, () => AppendLog("DEBUG", "调试信息输出已开启"), 94, 47);
        AddButton(320, "重载\r\n安全区", 1040, 541, () => Reload("安全区"), 94, 80);
        AddButton(330, "重载\r\n物品表", 1040, 657, () => Reload("物品表"), 94, 80);

        AddLabel(120, $"端口：{_cfg.GatePort}", 8, 800, Color.White, 13f);
        AddLabel(150, $"限制：{_cfg.MaxUser}", 164, 800, Color.White, 13f);
        _connections = AddLabel(130, "连接数：0", 320, 800, Color.White, 13f);
        AddLabel(140, "Gate编号  1", 485, 800, Color.White, 13f);
        _backendState = AddLabel(160, "☑", 800, 797, Color.White, 15f);
        AddLabel(290, "☑  显示界面消息", 28, 852, Color.Cyan, 13f);
        AddLabel(300, "☑  显示异常操作", 282, 852, Color.Red, 13f);
        AddLabel(220, $"{DateTime.Now:yyyyMMddHHmm}-到期时间：长期有效", 8, 914, Color.Lime, 13f);

        _server = new GateServer(_cfg);
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshStats();
        if (autoStart)
            Shown += (_, _) => StartServer();
        FormClosing += StopBeforeClose;
    }

    private void DrawBlueFrame(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(FrameBlue, 3f);
        e.Graphics.DrawRectangle(pen, 1, 1,
            Math.Max(0, ClientSize.Width - 3), Math.Max(0, ClientSize.Height - 3));
    }

    private static Button CreateTitleButton(string text, Color color) => new()
    {
        Text = text,
        Size = new Size(40, TitleBarHeight),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        BackColor = color,
        ForeColor = Color.Black,
        Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
        TabStop = false,
        UseVisualStyleBackColor = false
    };

    private void BeginTitleDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _titleDragOrigin = e.Location;
    }

    private void ContinueTitleDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control control) return;
        Point screen = control.PointToScreen(e.Location);
        Location = new Point(screen.X - _titleDragOrigin.X, screen.Y - _titleDragOrigin.Y);
    }

    private static string BuildWindowTitle() =>
        $"战神引擎-GameGateVer1.15  {DateTime.Now:M-d H:mm:ss}";

    private static Image LoadBackground()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("GameGate.Resources.gg_ac_main.png")
            ?? throw new InvalidOperationException("GG_AC main background resource is missing.");
        return new Bitmap(stream);
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("GameGate.Resources.gg_ac.ico")
            ?? throw new InvalidOperationException("GG_AC application icon resource is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private void AddButton(int id, string text, int x, int y, Action click, int width = 112, int height = 47)
    {
        var button = new Button
        {
            Name = $"control_{id}", Text = text, Location = P(x, y), Size = Z(width, height),
            Font = new Font("宋体", F(12f), FontStyle.Bold), FlatStyle = FlatStyle.System, UseVisualStyleBackColor = true,
            TabStop = false
        };
        button.Click += (_, _) => click();
        _body.Controls.Add(button);
        button.BringToFront();
    }

    private Label AddLabel(int id, string text, int x, int y, Color color, float size)
    {
        var label = new Label
        {
            Name = $"control_{id}", Text = text, Location = P(x, y), AutoSize = true,
            BackColor = Color.Black, ForeColor = color, Font = new Font("宋体", F(size), FontStyle.Bold)
        };
        _body.Controls.Add(label);
        label.BringToFront();
        return label;
    }

    private async void StartServer()
    {
        try
        {
            var server = _server ?? throw new InvalidOperationException("网关对象尚未初始化。");
            server.OnLog += (level, message) =>
            {
                if (!IsDisposed) BeginInvoke(() => AppendLog(level, message?.ToString() ?? string.Empty));
            };
            await server.StartAsync();
            _running = true;
            _timer.Start();
            AppendLog("INFO", $"网关启动成功，监听端口：{_cfg.GatePort}");
        }
        catch (Exception ex)
        {
            var failedServer = _server;
            _server = null;
            if (failedServer != null)
            {
                try { await failedServer.StopAsync(); } catch { }
                failedServer.Dispose();
            }
            AppendLog("ERROR", $"启动失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "GameGate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshStats()
    {
        if (!_running || _server == null) return;
        try
        {
            dynamic stats = _server.GetStats();
            _connections.Text = $"连接数：{(int)stats.Sessions.Active}";
            bool connected = (bool)stats.Network.M2Connected;
            _backendState.Text = connected ? "☑" : "☐";
            _backendState.ForeColor = connected ? Color.Lime : Color.White;
            string title = BuildWindowTitle();
            Text = title;
            _titleCaption.Text = title;
            CheckM2Watchdog();
        }
        catch { }
    }

    private void AppendLog(string level, string message)
    {
        var color = level switch
        {
            "ERROR" => Color.Red,
            "WARN" or "SPEED" => Color.Yellow,
            "BAN" => Color.OrangeRed,
            "CONNECT" => Color.Cyan,
            _ => Color.Lime
        };
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = color;
        var line = $"{DateTime.Now:M-d H:mm:ss}  [{level}] {message}";
        _log.AppendText(line + Environment.NewLine);
        _log.ScrollToCaret();
        try
        {
            var logDir = Path.Combine(_configDir, "procMsgLog");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, $"网关{DateTime.Now:yyyyMMdd}.log"),
                line + Environment.NewLine, Encoding.GetEncoding("GBK"));
        }
        catch { }
    }

    private void ShowOnlinePlayers()
    {
        if (_server == null) return;
        ShowPage(new GgAcOnlinePlayersPage(_server, _cfg),
            $"在线玩家列表 (在线人数:{_server.Sessions.ActiveCount})", new Size(1024, 510));
    }

    private void ShowChatMonitor()
    {
        if (_server == null) return;
        ShowPage(new GgAcChatMonitorPage(_server, _cfg), "聊天监控", new Size(768, 380));
    }

    private void ShowLists()
    {
        if (_server == null) return;
        ShowPage(new GgAcBanListsPage(_server, _cfg), "名单列表", new Size(540, 470));
    }

    private void ShowM2Status()
    {
        if (_server == null) return;
        ShowPage(new GgAcM2MonitorPage(_server, _cfg), "M2进程监控", new Size(430, 330));
    }

    private void ShowFeatureSettings()
    {
        if (_server == null) return;
        ShowPage(new GgAcExactFeatureSettingsPage(_server, _cfg), "基本设置", new Size(387, 370));
    }

    private void ShowNetworkSettings() =>
        ShowPage(new GgAcNetworkSettingsPage(_cfg), "网关服务配置", new Size(332, 292));

    private void ShowPage(Control page, string title, Size contentSize)
    {
        using var dialog = new GgAcDialogForm(title, contentSize);
        page.Dock = DockStyle.Fill;
        page.Visible = true;
        dialog.Content.Controls.Add(page);
        dialog.ShowDialog(this);
    }

    private void Reload(string name)
    {
        var loaded = GateConfig.Load(_configDir);
        ClassicConfigCopy.Apply(loaded, _cfg);
        _server?.ReloadRuntimeSettings();
        _server?.ReloadAbusiveFilter();
        AppendLog("INFO", $"网关配置已重载；{name}需要 M2 的专用接口，本程序未发送伪造指令");
    }

    private void CheckM2Watchdog()
    {
        if (_server == null || !_cfg.RebootM2WhenStuck || _server.GameConnected) return;
        var interval = TimeSpan.FromMilliseconds(Math.Max(5000, _cfg.M2WatchInterval));
        if (DateTime.Now - _lastM2RestartAttempt < interval) return;
        _lastM2RestartAttempt = DateTime.Now;

        var probe = M2ProcessProbe.Check(_cfg.M2Path, _cfg.ConfigDir);
        if (probe.State != M2ProcessState.NotRunning || probe.FullPath == null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = probe.FullPath,
                WorkingDirectory = Path.GetDirectoryName(probe.FullPath) ?? _configDir,
                UseShellExecute = true
            });
            AppendLog("WARN", $"M2连接中断，已按看门狗配置启动：{probe.FullPath}");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"M2看门狗启动失败：{ex.Message}");
        }
    }

    private async void StopBeforeClose(object? sender, FormClosingEventArgs e)
    {
        if (_server == null) return;
        if (_closing)
        {
            e.Cancel = true;
            return;
        }
        if (!_running)
        {
            _server.Dispose();
            _server = null;
            return;
        }
        e.Cancel = true;
        _closing = true;
        _timer.Stop();
        var server = _server;
        try
        {
            await server.StopAsync();
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"停止网关时发生错误：{ex.Message}");
        }
        finally
        {
            server.Dispose();
            if (ReferenceEquals(_server, server)) _server = null;
            _running = false;
            Close();
        }
    }

    private static int S(int value) => (int)Math.Round(value * UiScale);
    private static float F(float value) => value * FontScale;
    private static Point P(int x, int y) => new(S(x), S(y));
    private static Size Z(int width, int height) => new(S(width), S(height));
}
