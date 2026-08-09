using GameGate.Core;

namespace GameGate.Forms;

public sealed class GgAcM2MonitorPage : UserControl
{
    private static readonly Font NormalFont = new("宋体", 9f);
    private static readonly Font BoldFont = new("宋体", 9f, FontStyle.Bold);

    private readonly GateServer _server;
    private readonly GateConfig _config;
    private readonly Label _dbState;
    private readonly Label _m2State;
    private readonly Label _processState;
    private readonly TextBox _site;
    private readonly NumericUpDown _interval;
    private readonly CheckBox _reboot;
    private readonly Label _message;
    private readonly System.Windows.Forms.Timer _timer;

    public GgAcM2MonitorPage(GateServer server, GateConfig config)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        Font = NormalFont;
        MinimumSize = new Size(380, 280);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Control,
            Padding = new Padding(8),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(layout);

        var stateGroup = new GroupBox
        {
            Text = "运行状态",
            Dock = DockStyle.Fill,
            Font = BoldFont,
            Padding = new Padding(8, 5, 8, 7)
        };
        layout.Controls.Add(stateGroup, 0, 0);

        var states = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = SystemColors.ControlLightLight,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        for (int i = 0; i < 3; i++)
            states.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        states.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        states.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stateGroup.Controls.Add(states);

        AddStateTitle(states, "DBSvr 连接", 0);
        AddStateTitle(states, "M2/GameSvr 连接", 1);
        AddStateTitle(states, "M2 进程", 2);
        _dbState = AddStateValue(states, 0);
        _m2State = AddStateValue(states, 1);
        _processState = AddStateValue(states, 2);

        var settingsGroup = new GroupBox
        {
            Text = "M2 进程监控设置",
            Dock = DockStyle.Fill,
            Font = BoldFont,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(3, 7, 3, 3)
        };
        layout.Controls.Add(settingsGroup, 0, 1);

        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        settingsGroup.Controls.Add(settings);

        settings.Controls.Add(FieldLabel("M2 程序路径 (site)："), 0, 0);
        _site = new TextBox
        {
            Text = _config.M2Path ?? string.Empty,
            Dock = DockStyle.Fill,
            Font = NormalFont,
            Margin = new Padding(3, 5, 6, 4)
        };
        settings.Controls.Add(_site, 1, 0);

        var browse = new Button
        {
            Text = "浏览...",
            Dock = DockStyle.Fill,
            Font = NormalFont,
            Margin = new Padding(0, 3, 0, 3),
            UseVisualStyleBackColor = true
        };
        browse.Click += (_, _) => BrowseM2();
        settings.Controls.Add(browse, 2, 0);

        settings.Controls.Add(FieldLabel("检测间隔 (time)："), 0, 1);
        _interval = new NumericUpDown
        {
            Minimum = 1000,
            Maximum = 86_400_000,
            Increment = 1000,
            ThousandsSeparator = true,
            Dock = DockStyle.Left,
            Width = 160,
            Font = NormalFont,
            Margin = new Padding(3, 5, 6, 4)
        };
        _interval.Value = Math.Clamp((decimal)_config.M2WatchInterval,
            _interval.Minimum, _interval.Maximum);
        settings.Controls.Add(_interval, 1, 1);
        settings.Controls.Add(FieldLabel("毫秒"), 2, 1);

        settings.Controls.Add(FieldLabel("进程守护 (Reboot)："), 0, 2);
        _reboot = new CheckBox
        {
            Text = "进程退出时自动启动 M2",
            Checked = _config.RebootM2WhenStuck,
            AutoSize = true,
            Font = NormalFont,
            Margin = new Padding(4, 7, 4, 4)
        };
        settings.Controls.Add(_reboot, 1, 2);

        var footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 0) };
        layout.Controls.Add(footer, 0, 2);

        var save = new Button
        {
            Text = "保存设置",
            Size = new Size(94, 29),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Math.Max(0, footer.ClientSize.Width - 94), 0),
            Font = BoldFont,
            UseVisualStyleBackColor = true
        };
        footer.SizeChanged += (_, _) =>
            save.Location = new Point(Math.Max(0, footer.ClientSize.Width - save.Width), 0);
        save.Click += (_, _) => SaveSettings();
        footer.Controls.Add(save);

        _message = new Label
        {
            AutoEllipsis = true,
            Text = EndpointSummary(),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Font = NormalFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Location = new Point(2, 2),
            Size = new Size(Math.Max(0, footer.ClientSize.Width - 104), 27)
        };
        footer.SizeChanged += (_, _) =>
            _message.Size = new Size(Math.Max(0, footer.ClientSize.Width - save.Width - 10), 27);
        footer.Controls.Add(_message);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshStatus();
        HandleCreated += (_, _) =>
        {
            RefreshStatus();
            _timer.Start();
        };
        HandleDestroyed += (_, _) => _timer.Stop();
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = NormalFont,
        AutoEllipsis = true
    };

    private static void AddStateTitle(TableLayoutPanel table, string text, int column) =>
        table.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = NormalFont,
            ForeColor = SystemColors.GrayText
        }, column, 0);

    private static Label AddStateValue(TableLayoutPanel table, int column)
    {
        var label = new Label
        {
            Text = "未连接",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = BoldFont,
            ForeColor = Color.Maroon
        };
        table.Controls.Add(label, column, 1);
        return label;
    }

    private void BrowseM2()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 M2Server 程序",
            Filter = "程序文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        string path = _site.Text.Trim();
        string? fullPath = M2ProcessProbe.ResolvePath(path, _config.ConfigDir);
        if (fullPath != null && File.Exists(fullPath))
            dialog.InitialDirectory = Path.GetDirectoryName(fullPath);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) _site.Text = dialog.FileName;
    }

    private void SaveSettings()
    {
        try
        {
            _config.M2Path = _site.Text.Trim();
            _config.M2WatchInterval = decimal.ToInt32(_interval.Value);
            _config.RebootM2WhenStuck = _reboot.Checked;
            _config.Save();
            _message.ForeColor = Color.DarkGreen;
            _message.Text = $"设置已保存到 MirGate.ini  ({DateTime.Now:HH:mm:ss})";
            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"M2 监控设置保存失败：{ex.Message}",
                "GameGate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshStatus()
    {
        SetState(_dbState, _server.DBConnected, "已连接", "未连接");
        SetState(_m2State, _server.GameConnected, "已连接", "未连接");
        SetProcessState(M2ProcessProbe.Check(_site.Text.Trim(), _config.ConfigDir).State);
    }

    private string EndpointSummary() =>
        $"DB {_config.BackendIP}:{_config.BackendPort2}   M2 {_config.GameBackendIP}:{_config.BackendPort}";

    private static void SetState(Label label, bool ready, string readyText, string stoppedText)
    {
        label.Text = ready ? readyText : stoppedText;
        label.ForeColor = ready ? Color.DarkGreen : Color.Maroon;
    }

    private void SetProcessState(M2ProcessState state)
    {
        (_processState.Text, _processState.ForeColor) = state switch
        {
            M2ProcessState.Running => ("运行中", Color.DarkGreen),
            M2ProcessState.NotRunning => ("未运行", Color.Maroon),
            M2ProcessState.MissingFile => ("路径无效", Color.Maroon),
            M2ProcessState.Unknown => ("无法检查", Color.DarkOrange),
            _ => ("未配置", SystemColors.GrayText)
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
