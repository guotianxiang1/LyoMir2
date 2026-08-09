using GameGate.Core;

namespace GameGate.Forms;

internal static class GgAcPageStyle
{
    public static readonly Color Back = Color.FromArgb(240, 240, 240);
    public static readonly Color Blue = Color.FromArgb(0, 72, 168);

    public static Font Font(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("宋体", size, style);

    public static Label Label(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.Black,
        Font = Font(),
        Margin = new Padding(5, 8, 5, 3)
    };

    public static NumericUpDown Number(int value, int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = Math.Clamp(value, minimum, maximum),
        ThousandsSeparator = true,
        TextAlign = HorizontalAlignment.Right,
        BackColor = Color.White,
        ForeColor = Color.Black,
        Font = Font(),
        Dock = DockStyle.Fill,
        Margin = new Padding(4, 5, 7, 4)
    };

    public static Button Button(string text, int width = 116) => new()
    {
        Text = text,
        Width = width,
        Height = 34,
        Font = Font(),
        UseVisualStyleBackColor = true,
        Margin = new Padding(6)
    };

    public static void ShowError(Control owner, string message) =>
        MessageBox.Show(owner.FindForm() ?? owner, message, "GameGate",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
}

public sealed class GgAcNetworkSettingsPage : UserControl
{
    private readonly GateConfig _config;
    private readonly NumericUpDown _maxUser;
    private readonly NumericUpDown _maxSend;
    private readonly NumericUpDown _serveCount;
    private readonly RadioButton[] _modeButtons;
    private readonly Label _status;

    public GgAcNetworkSettingsPage(GateConfig config)
    {
        _config = config;
        Dock = DockStyle.Fill;
        BackColor = GgAcPageStyle.Back;
        Font = GgAcPageStyle.Font();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = GgAcPageStyle.Back,
            Padding = new Padding(8, 4, 8, 6)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = GgAcPageStyle.Back,
            Padding = new Padding(9, 2, 9, 2)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        for (int i = 0; i < 3; i++) body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        body.Controls.Add(GgAcPageStyle.Label("最大连接数："), 0, 0);
        _maxUser = GgAcPageStyle.Number(_config.MaxUser, 1, 5000);
        body.Controls.Add(_maxUser, 1, 0);
        body.Controls.Add(GgAcPageStyle.Label("客户端并发数："), 0, 1);
        _maxSend = GgAcPageStyle.Number(_config.MaxSend, 1, 1_000_000);
        body.Controls.Add(_maxSend, 1, 1);
        body.Controls.Add(GgAcPageStyle.Label("服务线程数："), 0, 2);
        _serveCount = GgAcPageStyle.Number(_config.ServeCount, 1, 1024);
        body.Controls.Add(_serveCount, 1, 2);

        var modeGroup = new GroupBox
        {
            Text = "数据发送方式",
            Dock = DockStyle.Fill,
            ForeColor = Color.Black,
            Font = GgAcPageStyle.Font(9f, FontStyle.Bold),
            BackColor = GgAcPageStyle.Back,
            Padding = new Padding(8, 5, 8, 4),
            Margin = new Padding(4, 2, 4, 2)
        };
        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = GgAcPageStyle.Back,
            Padding = new Padding(6, 0, 2, 0)
        };
        _modeButtons = new RadioButton[3];
        int selectedMode = Math.Clamp(_config.Mode, 1, 3);
        for (int mode = 1; mode <= 3; mode++)
        {
            var radio = new RadioButton
            {
                Text = mode switch { 1 => "打包模式", 2 => "安全模式", _ => "直接模式" },
                Tag = mode,
                Checked = mode == selectedMode,
                AutoSize = false,
                Size = new Size(120, 18),
                ForeColor = Color.Black,
                Font = GgAcPageStyle.Font(),
                UseVisualStyleBackColor = true,
                Margin = Padding.Empty
            };
            _modeButtons[mode - 1] = radio;
            modes.Controls.Add(radio);
        }
        modeGroup.Controls.Add(modes);
        body.Controls.Add(modeGroup, 0, 3);
        body.SetColumnSpan(modeGroup, 2);
        root.Controls.Add(body, 0, 0);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = GgAcPageStyle.Back,
            Padding = new Padding(8, 7, 8, 5)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _status = GgAcPageStyle.Label("保存后重启网关生效。");
        _status.ForeColor = Color.Red;
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        footer.Controls.Add(_status, 0, 0);
        var save = GgAcPageStyle.Button("确定(Y)", 120);
        save.Name = "SaveNetworkSettingsButton";
        save.AccessibleName = "保存网络设置";
        save.Anchor = AnchorStyles.Right;
        save.Click += (_, _) => SaveSettings();
        footer.Controls.Add(save, 1, 0);
        root.Controls.Add(footer, 0, 1);
        Controls.Add(root);
    }

    private void SaveSettings()
    {
        try
        {
            _config.MaxUser = decimal.ToInt32(_maxUser.Value);
            _config.MaxSend = decimal.ToInt32(_maxSend.Value);
            _config.ServeCount = decimal.ToInt32(_serveCount.Value);
            _config.Mode = (int)(_modeButtons.First(button => button.Checked).Tag ?? 1);
            _config.Save();
            _status.Text = $"网络参数已保存：{DateTime.Now:HH:mm:ss}";
            _status.ForeColor = GgAcPageStyle.Blue;
            if (FindForm() is Form dialog)
            {
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
        }
        catch (Exception ex)
        {
            GgAcPageStyle.ShowError(this, $"网络参数保存失败：{ex.Message}");
        }
    }
}
