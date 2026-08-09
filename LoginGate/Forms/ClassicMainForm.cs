using System.Reflection;
using LoginGate.Core;

namespace LoginGate.Forms;

public sealed class ClassicMainForm : Form
{
    private readonly string _configDirectory;
    private readonly LoginGateConfig _config;
    private readonly LoginGateServer _server;
    private readonly LoginGateFileLogWriter _fileLog;
    private readonly ListView _serverList;
    private readonly TextBox _debugMemo;
    private readonly Label _onlineLabel;
    private readonly Label _maximumOnlineLabel;
    private readonly Label _concurrencyLabel;
    private Label _authFailedText = null!;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _closing;
    private bool _allowClose;

    public ClassicMainForm(string configDirectory, LoginGateConfig config,
        ILoginTicketAuthenticator authenticator, bool autoStart)
    {
        _configDirectory = configDirectory;
        _config = config;
        _server = new LoginGateServer(config, authenticator);
        _fileLog = new LoginGateFileLogWriter(configDirectory);

        Name = "FrmMain";
        Text = BuildTitle();
        ClientSize = new Size(344, 506);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("MS Sans Serif", 8.25f, FontStyle.Regular,
            GraphicsUnit.Point);

        var statusPanel = new Panel
        {
            Name = "pnl1",
            Dock = DockStyle.Bottom,
            Height = 38,
            BackColor = Color.Black
        };
        _onlineLabel = AddStatusLabel(statusPanel, "lb_Online", "总在线: 0", 16, 10, 100);
        _maximumOnlineLabel = AddStatusLabel(statusPanel, "lb_maxOnline", "最高在线: 0", 125, 10, 115);
        _concurrencyLabel = AddStatusLabel(statusPanel, "lb_Connect", "并发数: 0", 262, 10, 78);
        Controls.Add(statusPanel);

        var pageControl = new TabControl
        {
            Name = "PageControl",
            Dock = DockStyle.Fill,
            Padding = new Point(6, 3),
            Multiline = false,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(42, 27)
        };
        var viewPage = new TabPage
        {
            Name = "ts1",
            Text = "View",
            BackColor = Color.Black,
            Padding = Padding.Empty,
            UseVisualStyleBackColor = false
        };
        pageControl.TabPages.Add(viewPage);
        Controls.Add(pageControl);
        pageControl.BringToFront();

        var viewLayout = new TableLayoutPanel
        {
            Name = "ViewLayout",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Black
        };
        viewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        viewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        viewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        viewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));
        viewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        viewPage.Controls.Add(viewLayout);

        var authPanel = BuildAuthPanel();
        authPanel.Dock = DockStyle.Fill;
        viewLayout.Controls.Add(authPanel, 0, 0);

        _serverList = new ListView
        {
            Name = "LvServerList",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            SmallImageList = CreateServerImageList()
        };
        _serverList.Columns.Add("服务器组", 100);
        _serverList.Columns.Add("总在线", 60, HorizontalAlignment.Center);
        _serverList.Columns.Add("GS1", 50, HorizontalAlignment.Center);
        _serverList.Columns.Add("GS2", 50, HorizontalAlignment.Center);
        _serverList.Columns.Add("GS3", 50, HorizontalAlignment.Center);
        _serverList.Columns.Add("合计", 50, HorizontalAlignment.Center);
        viewLayout.Controls.Add(_serverList, 0, 1);

        var splitter = new Panel
        {
            Name = "spl2",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = SystemColors.Control
        };
        viewLayout.Controls.Add(splitter, 0, 2);

        _debugMemo = new TextBox
        {
            Name = "MmoDebug",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = new Font("宋体", 9f, FontStyle.Regular, GraphicsUnit.Point)
        };
        viewLayout.Controls.Add(_debugMemo, 0, 3);

        _server.LogReceived += OnServerLog;
        _server.StateChanged += OnServerStateChanged;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshRuntimeState();
        _timer.Start();
        FormClosing += StopBeforeClose;

        AppendStartupMessages();
        RefreshRuntimeState();
        if (autoStart) Shown += async (_, _) => await StartServerAsync();
    }

    private Panel BuildAuthPanel()
    {
        var panel = new Panel
        {
            Name = "pnl2",
            Location = Point.Empty,
            Size = new Size(336, 158),
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Black,
            BackgroundImage = LoadAuthBackground(),
            BackgroundImageLayout = ImageLayout.Stretch
        };

        var authName = new TextBox
        {
            Name = "AuthNameEdt",
            AccessibleName = "认证域名",
            Location = new Point(69, 27),
            Size = new Size(201, 21),
            BorderStyle = BorderStyle.Fixed3D
        };
        panel.Controls.Add(authName);

        var authCode = new TextBox
        {
            Name = "AuthCodeEdt",
            AccessibleName = "认证码",
            Location = new Point(69, 66),
            Size = new Size(201, 21),
            BorderStyle = BorderStyle.Fixed3D,
            UseSystemPasswordChar = true
        };
        panel.Controls.Add(authCode);

        var authenticate = new Button
        {
            Name = "ReAuthBtn",
            Text = "重新认证",
            Location = new Point(69, 97),
            Size = new Size(102, 23),
            UseVisualStyleBackColor = true
        };
        authenticate.Click += (_, _) => ShowUnavailableAuthAdapter();
        panel.Controls.Add(authenticate);

        var modify = new Button
        {
            Name = "ModAuthCode",
            Text = "更换认证码",
            Location = new Point(178, 97),
            Size = new Size(91, 23),
            UseVisualStyleBackColor = true
        };
        modify.Click += (_, _) => ShowUnavailableAuthAdapter();
        panel.Controls.Add(modify);

        _authFailedText = new Label
        {
            Name = "AuthFailedText",
            AccessibleName = "认证状态",
            Text = "您未认证成功，请重新认证",
            Location = new Point(67, 133),
            Size = new Size(200, 17),
            BackColor = Color.Black,
            ForeColor = Color.Red,
            TextAlign = ContentAlignment.MiddleCenter
        };
        panel.Controls.Add(_authFailedText);
        return panel;
    }

    private static Label AddStatusLabel(Control parent, string name, string text,
        int x, int y, int width)
    {
        var label = new Label
        {
            Name = name,
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 20),
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("宋体", 9f, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft
        };
        parent.Controls.Add(label);
        return label;
    }

    private static Image LoadAuthBackground()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "LoginGate.Resources.original_auth_panel.png")
            ?? throw new InvalidOperationException("LoginGate auth panel resource is missing.");
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    private static ImageList CreateServerImageList()
    {
        var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        images.Images.Add(SystemIcons.Information.ToBitmap());
        return images;
    }

    private async Task StartServerAsync()
    {
        try
        {
            await _server.StartAsync();
        }
        catch (Exception ex)
        {
            AppendLog(new LoginGateLogEntry(DateTime.Now, "ERROR", "启动失败：" + ex.Message));
        }
    }

    private void AppendStartupMessages()
    {
        var ipList = Path.Combine(_configDirectory, "IpAddress.txt");
        AppendLog(new LoginGateLogEntry(DateTime.Now, "INFO",
            File.Exists(ipList) ? "成功读取 IpAddress.txt" : "IpAddress.txt 不存在，使用 DBServerIP 配置"));
        var authModule = Path.Combine(_configDirectory, "M2Auth.dll");
        AppendLog(new LoginGateLogEntry(DateTime.Now, "WARN",
            File.Exists(authModule) ? "M2Auth.dll 未加载：当前构建仅启用本地票据适配器" : "M2Auth.dll 不存在"));
    }

    private void ShowUnavailableAuthAdapter()
    {
        _authFailedText.Text = "您未认证成功，请重新认证";
        AppendLog(new LoginGateLogEntry(DateTime.Now, "WARN",
            "原版授权适配器未配置，未发起外部认证"));
    }

    private void OnServerLog(LoginGateLogEntry entry)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => AppendLog(entry)); } catch { }
            return;
        }
        AppendLog(entry);
    }

    private void AppendLog(LoginGateLogEntry entry)
    {
        if (_debugMemo.IsDisposed) return;
        _debugMemo.AppendText($"{entry.Timestamp:MM-dd HH:mm:ss.fff} {entry.Message}\r\n");
        _debugMemo.SelectionStart = _debugMemo.TextLength;
        _debugMemo.ScrollToCaret();
        _fileLog.TryWrite(entry);
    }

    private void OnServerStateChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(RefreshRuntimeState); } catch { }
            return;
        }
        RefreshRuntimeState();
    }

    private void RefreshRuntimeState()
    {
        if (IsDisposed) return;
        Text = BuildTitle();
        var stats = _server.GetStats();
        _onlineLabel.Text = $"总在线: {stats.TotalOnline}";
        _maximumOnlineLabel.Text = $"最高在线: {stats.MaximumOnline}";
        _concurrencyLabel.Text = $"并发数: {stats.ActiveAuthentications}";
        RefreshServerList(_server.GetBackends());
    }

    private void RefreshServerList(IReadOnlyList<LoginGateBackendSnapshot> backends)
    {
        _serverList.BeginUpdate();
        try
        {
            _serverList.Items.Clear();
            var groups = _config.GetConfiguredAreas()
                .SelectMany(area => area.Groups)
                .GroupBy(group => string.IsNullOrWhiteSpace(group.DbServerName)
                        ? group.Name
                        : group.DbServerName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
            foreach (var group in groups)
            {
                var matches = backends.Where(backend =>
                    (string.IsNullOrWhiteSpace(group.DbServerName)
                     || backend.ServerName.Equals(group.DbServerName,
                         StringComparison.OrdinalIgnoreCase))
                    && (backend.GroupIndex == group.Index || !backend.RouteReady)).ToArray();
                var online = matches.Sum(backend => Math.Max(0, backend.OnlineCount));
                var item = new ListViewItem(group.Name, 0);
                item.SubItems.Add(online.ToString());
                item.SubItems.Add(matches.ElementAtOrDefault(0)?.OnlineCount.ToString() ?? "0");
                item.SubItems.Add(matches.ElementAtOrDefault(1)?.OnlineCount.ToString() ?? "0");
                item.SubItems.Add(matches.ElementAtOrDefault(2)?.OnlineCount.ToString() ?? "0");
                item.SubItems.Add(online.ToString());
                _serverList.Items.Add(item);
            }
        }
        finally
        {
            _serverList.EndUpdate();
        }
    }

    private static string BuildTitle() =>
        $"LoginGate (1.0.1.104)   {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

    private async void StopBeforeClose(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        Enabled = false;
        _timer.Stop();
        _server.LogReceived -= OnServerLog;
        _server.StateChanged -= OnServerStateChanged;
        try
        {
            await _server.DisposeAsync();
            await _fileLog.DisposeAsync();
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }
}
