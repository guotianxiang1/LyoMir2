using System.Net;
using System.Text;
using GameGate.Core;
using GameGate.Models;

namespace GameGate.Forms;

internal static class GgAcManagementTheme
{
    public static readonly Color Window = Color.FromArgb(240, 240, 240);
    public static readonly Color Header = Color.FromArgb(224, 224, 224);
    public static readonly Color Border = Color.FromArgb(145, 145, 145);
    public static readonly Color MonitorGreen = Color.FromArgb(0, 230, 70);

    public static Font Font(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("宋体", size, style);

    public static Button Button(string text, int width = 88) => new()
    {
        Text = text,
        Width = width,
        Height = 32,
        Margin = new Padding(4, 3, 4, 3),
        Font = Font(),
        FlatStyle = FlatStyle.System,
        UseVisualStyleBackColor = true
    };

    public static TextBox TextBox() => new()
    {
        Height = 29,
        Font = Font(),
        BorderStyle = BorderStyle.FixedSingle
    };

    public static Label Label(string text, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color ?? SystemColors.ControlText,
        Font = Font(),
        BackColor = Color.Transparent
    };

    public static CheckBox CheckBox(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Font(),
        Margin = new Padding(7, 9, 7, 4),
        UseVisualStyleBackColor = true
    };

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.Fixed3D,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = Color.FromArgb(190, 190, 190),
            EnableHeadersVisualStyles = false,
            Font = Font(),
            ColumnHeadersHeight = 32,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.Black,
                SelectionBackColor = Color.FromArgb(0, 120, 215),
                SelectionForeColor = Color.White,
                Font = Font(),
                Padding = new Padding(2),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Header,
                ForeColor = Color.Black,
                SelectionBackColor = Header,
                SelectionForeColor = Color.Black,
                Font = Font(),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            RowTemplate = { Height = 29 }
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);
        return grid;
    }

    public static void Info(Control owner, string message) =>
        MessageBox.Show(owner.FindForm() ?? owner, message, "GameGate",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Error(Control owner, string message) =>
        MessageBox.Show(owner.FindForm() ?? owner, message, "GameGate",
            MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static string? Prompt(Control owner, string title, string label)
    {
        using var dialog = new Form
        {
            Text = title,
            ClientSize = new Size(440, 137),
            MinimumSize = new Size(456, 176),
            MaximumSize = new Size(456, 176),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Window,
            Font = Font()
        };
        var prompt = Label(label);
        prompt.SetBounds(12, 14, 410, 24);
        prompt.AutoSize = false;
        dialog.Controls.Add(prompt);
        var input = TextBox();
        input.SetBounds(12, 42, 416, 29);
        dialog.Controls.Add(input);
        var ok = Button("确定", 82);
        ok.SetBounds(254, 88, 82, 32);
        ok.DialogResult = DialogResult.OK;
        dialog.Controls.Add(ok);
        var cancel = Button("取消", 82);
        cancel.SetBounds(346, 88, 82, 32);
        cancel.DialogResult = DialogResult.Cancel;
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => input.Focus();

        var result = owner.FindForm() is { } parent
            ? dialog.ShowDialog(parent)
            : dialog.ShowDialog();
        if (result != DialogResult.OK) return null;
        string value = input.Text.Trim();
        return value.Length == 0 ? null : value;
    }
}

public sealed class GgAcOnlinePlayersPage : UserControl
{
    private readonly GateServer _server;
    private readonly GateConfig _config;
    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;

    public GgAcOnlinePlayersPage(GateServer server, GateConfig config)
    {
        _server = server;
        _config = config;
        Dock = DockStyle.Fill;
        BackColor = GgAcManagementTheme.Window;
        Font = GgAcManagementTheme.Font();
        MinimumSize = new Size(900, 450);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = GgAcManagementTheme.Window
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(root);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = GgAcManagementTheme.Window,
            Padding = new Padding(8, 3, 8, 3)
        };
        var sortHint = GgAcManagementTheme.Label("带符号 * 可以排序。", Color.Red);
        sortHint.Location = new Point(10, 16);
        footer.Controls.Add(sortHint);
        _status = GgAcManagementTheme.Label(string.Empty, Color.FromArgb(70, 70, 70));
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.SetBounds(150, 8, 330, 36);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        footer.Controls.Add(_status);

        var footerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 292,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        var cancelRecord = GgAcManagementTheme.Button("取消记录", 86);
        cancelRecord.Click += (_, _) => CancelRecord();
        footerButtons.Controls.Add(cancelRecord);
        var announce = GgAcManagementTheme.Button("全服公告", 86);
        announce.Click += async (_, _) => await BroadcastAsync();
        footerButtons.Controls.Add(announce);
        var refresh = GgAcManagementTheme.Button("刷新", 86);
        refresh.Click += (_, _) => RefreshSessions();
        footerButtons.Controls.Add(refresh);
        footer.Controls.Add(footerButtons);
        footer.Layout += (_, _) => _status.SetBounds(150, 8,
            Math.Max(0, footer.ClientSize.Width - footerButtons.Width - 158), 36);
        root.Controls.Add(footer, 0, 1);

        _grid = GgAcManagementTheme.Grid();
        AddColumn("Id", "id", 52);
        AddColumn("Name", "角色名", 96);
        AddColumn("Map", "所在地图*", 104, sortable: true);
        AddColumn("Ingot", "元宝数量*", 92, sortable: true);
        AddColumn("Gold", "金币数量*", 92, sortable: true);
        AddColumn("IP", "IP*", 112, sortable: true);
        AddColumn("ConnectionId", "连接ID", 78);
        AddColumn("Heartbeat", "心跳值", 74);
        AddColumn("Job", "职业", 62);
        AddColumn("Level", "等级", 58);
        AddColumn("X", "坐标X", 64);
        AddColumn("Y", "坐标Y", 64);
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Fill",
            HeaderText = string.Empty,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 30,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        ConfigureContextMenu();
        root.Controls.Add(_grid, 0, 0);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshSessions();
        HandleCreated += (_, _) =>
        {
            RefreshSessions();
            _timer.Start();
        };
        HandleDestroyed += (_, _) => _timer.Stop();
    }

    private void AddColumn(string name, string title, int width, bool sortable = false) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = title,
            Width = width,
            MinimumWidth = Math.Min(width, 45),
            SortMode = sortable
                ? DataGridViewColumnSortMode.Automatic
                : DataGridViewColumnSortMode.NotSortable
        });

    private void ConfigureContextMenu()
    {
        var menu = new ContextMenuStrip { Font = GgAcManagementTheme.Font() };
        menu.Items.Add("踢下线", null, (_, _) => KickSelected());
        menu.Items.Add("加入IP黑名单", null, (_, _) => BanSelectedIp());
        menu.Items.Add("Name临时封禁", null, (_, _) => TempBanSelectedName());
        menu.Items.Add("Name封禁31天", null, (_, _) => BanSelectedNameFor31Days());
        menu.Items.Add("禁言", null, (_, _) => MuteSelectedName());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("发送消息", null, async (_, _) => await SendSelectedMessageAsync());
        menu.Opening += (_, e) => e.Cancel = SelectedSession(showError: false) == null;
        _grid.ContextMenuStrip = menu;
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            _grid.ClearSelection();
            _grid.Rows[e.RowIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[0];
        };
    }

    private void RefreshSessions()
    {
        if (IsDisposed) return;
        var selected = SelectedKey();
        var sessions = _server.Sessions.GetAllActive();
        _grid.Rows.Clear();
        foreach (var session in sessions)
        {
            int rowIndex = _grid.Rows.Add(
                session.BackendRouteId != 0 ? session.BackendRouteId : session.SessionId,
                session.CharName ?? string.Empty,
                session.MapName,
                session.Ingot == 0 ? string.Empty : session.Ingot,
                session.Gold == 0 ? string.Empty : session.Gold,
                session.RemoteAddr,
                session.BackendRouteId == 0 ? string.Empty : session.BackendRouteId,
                session.HeartbeatCount,
                session.Job switch
                {
                    0 => "战士",
                    1 => "法师",
                    2 => "道士",
                    _ => string.Empty
                },
                session.Level == 0 ? string.Empty : session.Level,
                session.X == 0 ? string.Empty : session.X,
                session.Y == 0 ? string.Empty : session.Y,
                string.Empty);
            var row = _grid.Rows[rowIndex];
            var key = new SessionKey(session.SessionId, session.Generation);
            row.Tag = key;
            if (selected == key)
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
            }
        }
        _status.Text = $"在线人数：{sessions.Count}    {DateTime.Now:HH:mm:ss}";
    }

    private SessionKey? SelectedKey() =>
        _grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Tag is SessionKey key
            ? key
            : null;

    private SessionIdentity? SelectedSession(bool showError = true)
    {
        var key = SelectedKey();
        var session = key.HasValue
            ? _server.Sessions.GetIdentity(key.Value.Id, key.Value.Generation)
            : null;
        if (session == null && showError)
            GgAcManagementTheme.Info(this, "请先选择一个仍在线的玩家。");
        return session;
    }

    private string? SelectedName(SessionIdentity session)
    {
        if (!string.IsNullOrWhiteSpace(session.CharName)) return session.CharName.Trim();
        GgAcManagementTheme.Info(this, "该连接尚未取得角色名，不能执行 Name 操作。");
        return null;
    }

    private void CancelRecord()
    {
        _grid.ClearSelection();
        _grid.CurrentCell = null;
        _status.Text = "未选择记录";
    }

    private void KickSelected()
    {
        if (SelectedSession() is not { } session) return;
        _server.DisconnectClient(session.SessionId, session.Generation);
        _status.Text = $"已踢下线：{DisplayName(session)}";
        RefreshSessions();
    }

    private void BanSelectedIp()
    {
        if (SelectedSession() is not { } session) return;
        _server.Bans.BlockIP(session.RemoteAddr, permanent: true);
        if (!PersistBans()) return;
        _server.DisconnectClient(session.SessionId, session.Generation);
        _status.Text = $"已加入IP黑名单：{session.RemoteAddr}";
        RefreshSessions();
    }

    private void TempBanSelectedName()
    {
        if (SelectedSession() is not { } session) return;
        string? name = SelectedName(session);
        if (name == null) return;
        int minutes = Math.Max(1, _config.BlackTime);
        _server.Bans.BlockName(name, TimeSpan.FromMinutes(minutes));
        if (!PersistBans()) return;
        _server.DisconnectClient(session.SessionId, session.Generation);
        _status.Text = $"已临时封禁 {name}：{minutes} 分钟";
        RefreshSessions();
    }

    private void BanSelectedNameFor31Days()
    {
        if (SelectedSession() is not { } session) return;
        string? name = SelectedName(session);
        if (name == null) return;
        _server.Bans.BlockName(name, TimeSpan.FromDays(31));
        if (!PersistBans()) return;
        _server.DisconnectClient(session.SessionId, session.Generation);
        _status.Text = $"已封禁 {name} 31 天";
        RefreshSessions();
    }

    private void MuteSelectedName()
    {
        if (SelectedSession() is not { } session) return;
        string? name = SelectedName(session);
        if (name == null) return;
        int minutes = Math.Max(1, _config.MuteTime);
        _server.Bans.MuteName(name, TimeSpan.FromMinutes(minutes));
        if (!PersistBans()) return;
        _status.Text = $"已禁言 {name}：{minutes} 分钟";
    }

    private async Task SendSelectedMessageAsync()
    {
        if (SelectedSession() is not { } session) return;
        string? message = GgAcManagementTheme.Prompt(this, "发送消息", $"发送给 {DisplayName(session)}：");
        if (message == null) return;
        bool sent = await _server.SendSystemMessageAsync(session.SessionId,
            session.Generation, message);
        if (!IsDisposed)
            _status.Text = sent ? $"消息已发送给 {DisplayName(session)}" : "发送失败，玩家可能已离线";
    }

    private async Task BroadcastAsync()
    {
        string? message = GgAcManagementTheme.Prompt(this, "全服公告", "公告内容：");
        if (message == null) return;
        int sent = await _server.BroadcastSystemMessageAsync(message);
        if (!IsDisposed) _status.Text = $"全服公告发送完成：{sent} 人";
    }

    private bool PersistBans()
    {
        try
        {
            _server.Bans.SavePersistentLists(_config.ConfigDir);
            return true;
        }
        catch (Exception ex)
        {
            GgAcManagementTheme.Error(this, $"名单保存失败：{ex.Message}");
            return false;
        }
    }

    private static string DisplayName(SessionIdentity session) =>
        session.CharName ?? session.Account ?? $"ID:{session.SessionId}";

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private readonly record struct SessionKey(int Id, long Generation);
}

public sealed class GgAcChatMonitorPage : UserControl
{
    [Flags]
    private enum ChatChannel
    {
        Nearby = 1,
        Private = 2,
        Shout = 4,
        Group = 8,
        Corps = 16,
        Guild = 32
    }

    private const ChatChannel AllChannels = ChatChannel.Nearby | ChatChannel.Private |
        ChatChannel.Shout | ChatChannel.Group | ChatChannel.Corps | ChatChannel.Guild;
    private const int MaximumVisibleLines = 5000;

    private readonly GateServer _server;
    private readonly GateConfig _config;
    private readonly RichTextBox _messages;
    private readonly Dictionary<ChatChannel, CheckBox> _channelChecks = [];
    private readonly CheckBox _selectAll;
    private readonly CheckBox _autoSave;
    private readonly List<string> _lines = [];
    private readonly object _lifecycleLock = new();
    private bool _updatingChecks;
    private bool _subscribed;
    private int _enabledChannelMask = (int)AllChannels;
    private bool _saveEnabled;

    public GgAcChatMonitorPage(GateServer server, GateConfig config)
    {
        _server = server;
        _config = config;
        Dock = DockStyle.Fill;
        BackColor = GgAcManagementTheme.Window;
        Font = GgAcManagementTheme.Font();
        MinimumSize = new Size(700, 320);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(7, 3, 4, 3),
            BackColor = GgAcManagementTheme.Window
        };
        AddChannelOption(options, "附近", ChatChannel.Nearby);
        AddChannelOption(options, "私聊", ChatChannel.Private);
        AddChannelOption(options, "喊话", ChatChannel.Shout);
        AddChannelOption(options, "编队", ChatChannel.Group);
        AddChannelOption(options, "战队", ChatChannel.Corps);
        AddChannelOption(options, "行会", ChatChannel.Guild);
        _selectAll = GgAcManagementTheme.CheckBox("全取");
        _selectAll.Checked = true;
        _selectAll.CheckedChanged += (_, _) => SelectAllChanged();
        options.Controls.Add(_selectAll);
        _autoSave = GgAcManagementTheme.CheckBox("自动保存");
        _autoSave.CheckedChanged += (_, _) =>
        {
            lock (_lifecycleLock) _saveEnabled = _subscribed && _autoSave.Checked;
        };
        options.Controls.Add(_autoSave);
        Controls.Add(options);

        _messages = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.Black,
            ForeColor = GgAcManagementTheme.MonitorGreen,
            BorderStyle = BorderStyle.Fixed3D,
            Font = new Font("宋体", 9f, FontStyle.Regular),
            DetectUrls = false,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        Controls.Add(_messages);
        _messages.BringToFront();
    }

    private void AddChannelOption(Control parent, string text, ChatChannel channel)
    {
        var option = GgAcManagementTheme.CheckBox(text);
        option.Checked = true;
        option.CheckedChanged += (_, _) => ChannelSelectionChanged();
        _channelChecks[channel] = option;
        parent.Controls.Add(option);
    }

    private void SelectAllChanged()
    {
        if (_updatingChecks) return;
        _updatingChecks = true;
        try
        {
            foreach (var option in _channelChecks.Values)
                option.Checked = _selectAll.Checked;
        }
        finally { _updatingChecks = false; }
        UpdateChannelMask();
    }

    private void ChannelSelectionChanged()
    {
        if (_updatingChecks) return;
        _updatingChecks = true;
        try { _selectAll.Checked = _channelChecks.Values.All(option => option.Checked); }
        finally { _updatingChecks = false; }
        UpdateChannelMask();
    }

    private void UpdateChannelMask()
    {
        ChatChannel mask = 0;
        foreach (var pair in _channelChecks)
            if (pair.Value.Checked) mask |= pair.Key;
        Volatile.Write(ref _enabledChannelMask, (int)mask);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        lock (_lifecycleLock)
        {
            if (_subscribed) return;
            _server.OnChat += ReceiveChat;
            _subscribed = true;
            _saveEnabled = _autoSave.Checked;
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Unsubscribe();
        base.OnHandleDestroyed(e);
    }

    private void Unsubscribe()
    {
        lock (_lifecycleLock)
        {
            if (!_subscribed) return;
            _subscribed = false;
            _saveEnabled = false;
            _server.OnChat -= ReceiveChat;
        }
    }

    private void ReceiveChat(ClientSession session, string message)
    {
        ChatChannel channel = Classify(message);
        string channelName = ChannelName(channel);
        string player = session.CharName ?? session.Account ?? $"ID:{session.SessionId}";
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{channelName}] " +
            $"[{player}] [{session.RemoteAddr}] {message.TrimEnd('\0')}";
        string? saveError = null;

        lock (_lifecycleLock)
        {
            if (!_subscribed || !IsHandleCreated || IsDisposed) return;
            if ((Volatile.Read(ref _enabledChannelMask) & (int)channel) == 0) return;
            if (_saveEnabled)
            {
                try
                {
                    string logDir = Path.Combine(_config.ConfigDir, "procMsgLog");
                    Directory.CreateDirectory(logDir);
                    string path = Path.Combine(logDir, $"聊天{DateTime.Now:yyyyMMdd_HH}.log");
                    File.AppendAllText(path, line + Environment.NewLine,
                        Encoding.GetEncoding("GBK"));
                }
                catch (Exception ex)
                {
                    saveError = $"[系统] 聊天日志保存失败：{ex.Message}";
                }
            }
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                AppendLine(line, GgAcManagementTheme.MonitorGreen);
                if (saveError != null) AppendLine(saveError, Color.OrangeRed);
            }));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void AppendLine(string line, Color color)
    {
        if (IsDisposed || !IsHandleCreated) return;
        _lines.Add(line);
        if (_lines.Count > MaximumVisibleLines)
        {
            _lines.RemoveAt(0);
            _messages.Text = string.Join(Environment.NewLine, _lines) + Environment.NewLine;
            _messages.Select(_messages.TextLength, 0);
            _messages.SelectionColor = GgAcManagementTheme.MonitorGreen;
        }
        else
        {
            _messages.Select(_messages.TextLength, 0);
            _messages.SelectionColor = color;
            _messages.AppendText(line + Environment.NewLine);
        }
        _messages.SelectionStart = _messages.TextLength;
        _messages.ScrollToCaret();
    }

    private static ChatChannel Classify(string message)
    {
        if (message.StartsWith("[战队]", StringComparison.Ordinal)) return ChatChannel.Corps;
        if (message.StartsWith("[行会]", StringComparison.Ordinal) ||
            message.StartsWith("!~", StringComparison.Ordinal)) return ChatChannel.Guild;
        if (message.StartsWith("[编队]", StringComparison.Ordinal) ||
            message.StartsWith("!!", StringComparison.Ordinal)) return ChatChannel.Group;
        if (message.StartsWith("[私聊]", StringComparison.Ordinal) ||
            message.StartsWith("/", StringComparison.Ordinal)) return ChatChannel.Private;
        if (message.StartsWith("[喊话]", StringComparison.Ordinal) ||
            message.StartsWith("!", StringComparison.Ordinal)) return ChatChannel.Shout;
        return ChatChannel.Nearby;
    }

    private static string ChannelName(ChatChannel channel) => channel switch
    {
        ChatChannel.Private => "私聊",
        ChatChannel.Shout => "喊话",
        ChatChannel.Group => "编队",
        ChatChannel.Corps => "战队",
        ChatChannel.Guild => "行会",
        _ => "附近"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) Unsubscribe();
        base.Dispose(disposing);
    }
}

public sealed class GgAcBanListsPage : UserControl
{
    public GgAcBanListsPage(GateServer server, GateConfig config)
    {
        Dock = DockStyle.Fill;
        BackColor = GgAcManagementTheme.Window;
        Font = GgAcManagementTheme.Font();
        MinimumSize = new Size(500, 400);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = GgAcManagementTheme.Font(),
            Padding = new Point(14, 5)
        };
        tabs.TabPages.Add(CreateTab("Name黑名单",
            new GgAcBanListEditor(server, config, GgAcBanListKind.Name)));
        tabs.TabPages.Add(CreateTab("IP黑名单",
            new GgAcBanListEditor(server, config, GgAcBanListKind.IP)));
        tabs.TabPages.Add(CreateTab("禁言名单",
            new GgAcBanListEditor(server, config, GgAcBanListKind.Mute)));
        tabs.TabPages.Add(CreateTab("禁用文字",
            new GgAcAbusiveRulesEditor(server, config)));
        Controls.Add(tabs);
    }

    private static TabPage CreateTab(string title, Control content)
    {
        var tab = new TabPage(title)
        {
            BackColor = GgAcManagementTheme.Window,
            Padding = new Padding(4)
        };
        content.Dock = DockStyle.Fill;
        tab.Controls.Add(content);
        return tab;
    }
}

internal enum GgAcBanListKind
{
    Name,
    IP,
    Mute
}

internal sealed class GgAcBanListEditor : UserControl
{
    private readonly GateServer _server;
    private readonly GateConfig _config;
    private readonly GgAcBanListKind _kind;
    private readonly TextBox _value;
    private readonly DataGridView _grid;
    private readonly Label _status;

    public GgAcBanListEditor(GateServer server, GateConfig config, GgAcBanListKind kind)
    {
        _server = server;
        _config = config;
        _kind = kind;
        Dock = DockStyle.Fill;
        BackColor = GgAcManagementTheme.Window;

        var tools = new Panel
        {
            Dock = DockStyle.Top,
            Height = 53,
            BackColor = GgAcManagementTheme.Window
        };
        var label = GgAcManagementTheme.Label(kind == GgAcBanListKind.IP ? "IP地址：" : "名称：");
        label.Location = new Point(9, 16);
        tools.Controls.Add(label);
        _value = GgAcManagementTheme.TextBox();
        _value.SetBounds(80, 11, 290, 29);
        _value.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        tools.Controls.Add(_value);
        var add = GgAcManagementTheme.Button("新增", 78);
        add.SetBounds(380, 6, 78, 32);
        add.Click += (_, _) => AddValue();
        tools.Controls.Add(add);
        var remove = GgAcManagementTheme.Button("删除", 78);
        remove.SetBounds(466, 6, 78, 32);
        remove.Click += (_, _) => RemoveSelected();
        tools.Controls.Add(remove);
        var refresh = GgAcManagementTheme.Button("刷新", 78);
        refresh.SetBounds(552, 6, 78, 32);
        refresh.Click += (_, _) => RefreshRows();
        tools.Controls.Add(refresh);
        void LayoutTools()
        {
            int width = tools.ClientSize.Width;
            refresh.Left = Math.Max(0, width - 82);
            remove.Left = Math.Max(0, width - 168);
            add.Left = Math.Max(0, width - 254);
            _value.Width = Math.Max(80, add.Left - _value.Left - 10);
        }
        tools.SizeChanged += (_, _) => LayoutTools();
        LayoutTools();
        Controls.Add(tools);

        _status = GgAcManagementTheme.Label(string.Empty, Color.FromArgb(70, 70, 70));
        _status.Dock = DockStyle.Bottom;
        _status.AutoSize = false;
        _status.Height = 31;
        _status.Padding = new Padding(7, 6, 0, 0);
        _status.BackColor = GgAcManagementTheme.Window;
        Controls.Add(_status);

        _grid = GgAcManagementTheme.Grid();
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Target",
            HeaderText = kind == GgAcBanListKind.IP ? "IP地址" : "名称",
            FillWeight = 55
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Type",
            HeaderText = "类型",
            FillWeight = 20
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Remaining",
            HeaderText = "剩余时间",
            FillWeight = 25
        });
        Controls.Add(_grid);
        _grid.BringToFront();
        HandleCreated += (_, _) => RefreshRows();
    }

    private void RefreshRows()
    {
        _grid.Rows.Clear();
        string[] permanent;
        (string Target, int RemainingMinutes)[] temporary;
        switch (_kind)
        {
            case GgAcBanListKind.IP:
                permanent = _server.Bans.GetBlockedIPs();
                temporary = _server.Bans.GetTemporaryIPBans();
                break;
            case GgAcBanListKind.Name:
                permanent = _server.Bans.GetBlockedNames();
                temporary = _server.Bans.GetTemporaryNameBans();
                break;
            default:
                permanent = _server.Bans.GetMutedNames();
                temporary = _server.Bans.GetTemporaryMutes();
                break;
        }

        foreach (string target in permanent)
        {
            int index = _grid.Rows.Add(target, "永久", string.Empty);
            _grid.Rows[index].Tag = target;
        }
        foreach (var entry in temporary)
        {
            int index = _grid.Rows.Add(entry.Target, "临时", $"{entry.RemainingMinutes} 分钟");
            _grid.Rows[index].Tag = entry.Target;
        }
        _status.Text = $"永久 {permanent.Length} 条，临时 {temporary.Length} 条";
    }

    private void AddValue()
    {
        string value = _value.Text.Trim();
        if (value.Length == 0) return;
        if (_kind == GgAcBanListKind.IP && !IsValidIpEntry(value))
        {
            GgAcManagementTheme.Info(this, "请输入有效的 IP 地址或 IPv4 CIDR 网段。");
            return;
        }
        switch (_kind)
        {
            case GgAcBanListKind.IP:
                if (value.Contains('/')) _server.Bans.LoadBlockIPs([value]);
                else _server.Bans.BlockIP(value, permanent: true);
                break;
            case GgAcBanListKind.Name:
                _server.Bans.BlockName(value);
                break;
            case GgAcBanListKind.Mute:
                _server.Bans.MuteName(value);
                break;
        }
        if (!SaveLists()) return;
        _value.Clear();
        RefreshRows();
        _status.Text = $"已新增：{value}";
    }

    private void RemoveSelected()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not string target) return;
        switch (_kind)
        {
            case GgAcBanListKind.IP: _server.Bans.UnblockIP(target); break;
            case GgAcBanListKind.Name: _server.Bans.UnblockName(target); break;
            case GgAcBanListKind.Mute: _server.Bans.UnmuteName(target); break;
        }
        if (!SaveLists()) return;
        RefreshRows();
        _status.Text = $"已删除：{target}";
    }

    private bool SaveLists()
    {
        try
        {
            _server.Bans.SavePersistentLists(_config.ConfigDir);
            return true;
        }
        catch (Exception ex)
        {
            GgAcManagementTheme.Error(this, $"名单保存失败：{ex.Message}");
            return false;
        }
    }

    private static bool IsValidIpEntry(string value)
    {
        int slash = value.IndexOf('/');
        if (slash < 0) return IPAddress.TryParse(value, out _);
        return IPAddress.TryParse(value[..slash], out var ip) && ip.AddressFamily ==
            System.Net.Sockets.AddressFamily.InterNetwork &&
            int.TryParse(value[(slash + 1)..], out int prefix) && prefix is >= 0 and <= 32;
    }
}

internal sealed class GgAcAbusiveRulesEditor : UserControl
{
    private readonly GateServer _server;
    private readonly GateConfig _config;
    private readonly TextBox _pattern;
    private readonly ComboBox _action;
    private readonly DataGridView _grid;
    private readonly Label _status;
    private List<string> _rawLines = [];

    public GgAcAbusiveRulesEditor(GateServer server, GateConfig config)
    {
        _server = server;
        _config = config;
        Dock = DockStyle.Fill;
        BackColor = GgAcManagementTheme.Window;

        var tools = new Panel
        {
            Dock = DockStyle.Top,
            Height = 53,
            BackColor = GgAcManagementTheme.Window
        };
        var label = GgAcManagementTheme.Label("文字：");
        label.Location = new Point(9, 16);
        tools.Controls.Add(label);
        _pattern = GgAcManagementTheme.TextBox();
        _pattern.SetBounds(65, 11, 240, 29);
        _pattern.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        tools.Controls.Add(_pattern);
        _action = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = GgAcManagementTheme.Font(),
            IntegralHeight = false
        };
        _action.Items.AddRange(["ReplaceAll", "ReplaceOne", "DropConnect"]);
        _action.SelectedIndex = 0;
        _action.SetBounds(315, 11, 128, 29);
        tools.Controls.Add(_action);
        var add = GgAcManagementTheme.Button("新增", 72);
        add.SetBounds(451, 6, 72, 32);
        add.Click += (_, _) => AddRule();
        tools.Controls.Add(add);
        var remove = GgAcManagementTheme.Button("删除", 72);
        remove.SetBounds(531, 6, 72, 32);
        remove.Click += (_, _) => RemoveRule();
        tools.Controls.Add(remove);
        var refresh = GgAcManagementTheme.Button("刷新", 72);
        refresh.SetBounds(611, 6, 72, 32);
        refresh.Click += (_, _) => RefreshRules();
        tools.Controls.Add(refresh);
        void LayoutTools()
        {
            int width = tools.ClientSize.Width;
            refresh.Left = Math.Max(0, width - 76);
            remove.Left = Math.Max(0, width - 156);
            add.Left = Math.Max(0, width - 236);
            _action.Left = Math.Max(160, add.Left - _action.Width - 8);
            _pattern.Width = Math.Max(80, _action.Left - _pattern.Left - 8);
        }
        tools.SizeChanged += (_, _) => LayoutTools();
        LayoutTools();
        Controls.Add(tools);

        _status = GgAcManagementTheme.Label(string.Empty, Color.FromArgb(70, 70, 70));
        _status.Dock = DockStyle.Bottom;
        _status.AutoSize = false;
        _status.Height = 31;
        _status.Padding = new Padding(7, 6, 0, 0);
        Controls.Add(_status);

        _grid = GgAcManagementTheme.Grid();
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Pattern",
            HeaderText = "禁用文字",
            FillWeight = 70
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Action",
            HeaderText = "处理方式",
            FillWeight = 30
        });
        Controls.Add(_grid);
        _grid.BringToFront();
        HandleCreated += (_, _) => RefreshRules();
    }

    private string RulesPath => Path.Combine(_config.ConfigDir, "AbusiveFilter.txt");

    private void RefreshRules()
    {
        try
        {
            _rawLines = File.Exists(RulesPath)
                ? File.ReadAllLines(RulesPath, Encoding.GetEncoding("GBK")).ToList()
                : [];
            _grid.Rows.Clear();
            for (int i = 0; i < _rawLines.Count; i++)
            {
                if (!TryParseRule(_rawLines[i], out string pattern, out string action)) continue;
                int rowIndex = _grid.Rows.Add(pattern, action);
                _grid.Rows[rowIndex].Tag = i;
            }
            _status.Text = $"过滤规则 {_grid.Rows.Count} 条";
        }
        catch (Exception ex)
        {
            GgAcManagementTheme.Error(this, $"过滤规则读取失败：{ex.Message}");
        }
    }

    private void AddRule()
    {
        string pattern = _pattern.Text.Trim();
        if (pattern.Length == 0) return;
        if (pattern.Contains('\r') || pattern.Contains('\n'))
        {
            GgAcManagementTheme.Info(this, "禁用文字不能包含换行符。");
            return;
        }
        string action = _action.SelectedItem?.ToString() ?? "ReplaceAll";
        _rawLines.Add($"{pattern}|{action}");
        if (!SaveRules()) return;
        _pattern.Clear();
        RefreshRules();
        _status.Text = $"已新增：{pattern}";
    }

    private void RemoveRule()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not int lineIndex ||
            lineIndex < 0 || lineIndex >= _rawLines.Count) return;
        string removed = _grid.SelectedRows[0].Cells[0].Value?.ToString() ?? string.Empty;
        _rawLines.RemoveAt(lineIndex);
        if (!SaveRules()) return;
        RefreshRules();
        _status.Text = $"已删除：{removed}";
    }

    private bool SaveRules()
    {
        try
        {
            Directory.CreateDirectory(_config.ConfigDir);
            File.WriteAllLines(RulesPath, _rawLines, Encoding.GetEncoding("GBK"));
            _server.ReloadAbusiveFilter();
            return true;
        }
        catch (Exception ex)
        {
            GgAcManagementTheme.Error(this, $"过滤规则保存失败：{ex.Message}");
            return false;
        }
    }

    private static bool TryParseRule(string raw, out string pattern, out string action)
    {
        pattern = string.Empty;
        action = string.Empty;
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) return false;
        int separator = line.LastIndexOf('|');
        if (separator <= 0 || separator == line.Length - 1) return false;
        pattern = line[..separator].Trim();
        action = line[(separator + 1)..].Trim();
        return pattern.Length > 0 && action.Length > 0;
    }
}
