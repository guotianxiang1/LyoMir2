using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Text;
using GameGate.Core;
using GameGate.Models;

namespace GameGate.Forms;

public sealed class MainForm : Form
{
    // ── Theme ──
    static readonly Color Bg = Color.FromArgb(13, 17, 23);
    static readonly Color SidebarBg = Color.FromArgb(18, 22, 30);
    static readonly Color CardBg = Color.FromArgb(22, 28, 36);
    static readonly Color Accent = Color.FromArgb(88, 166, 255);
    static readonly Color Green = Color.FromArgb(63, 185, 80);
    static readonly Color Red = Color.FromArgb(248, 81, 73);
    static readonly Color Orange = Color.FromArgb(210, 153, 34);
    static readonly Color TextPri = Color.FromArgb(230, 237, 243);
    static readonly Color TextDim = Color.FromArgb(139, 148, 158);
    static readonly Color BorderCol = Color.FromArgb(48, 54, 61);

    private GateConfig _cfg;
    private GateServer? _server;
    private bool _running;
    private readonly System.Windows.Forms.Timer _timer;
    private int _activeTab;

    // Sidebar
    private readonly Panel _sidebar;
    private readonly NavBtn[] _navBtns = new NavBtn[6];
    private Label _lblSideStatus = null!, _lblSideM2 = null!, _lblSideClients = null!;
    private Button _btnStart = null!, _btnStop = null!;

    // Pages
    private readonly Panel _pages;
    private DashPg _dash = null!;
    private ConnPg _conn = null!;
    private SpeedCfgPg _speedCfg = null!;
    private BanPg _ban = null!;
    private LogPg _log = null!;
    private CfgPg _cfgPg = null!;

    public MainForm(string dir)
    {
        _cfg = GateConfig.Load(dir);
        Text = "GameGate v2.0";
        Size = new Size(1200, 780);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;

        // ── Sidebar ──
        _sidebar = new Panel { Width = 210, Dock = DockStyle.Left, BackColor = SidebarBg };
        _sidebar.Paint += (_, e) => { using var p = new Pen(Color.FromArgb(35, 40, 50)); e.Graphics.DrawLine(p, 209, 0, 209, _sidebar.Height); };

        var logo = new Label { Text = "  GameGate", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = TextPri, Location = new Point(12, 16), Size = new Size(190, 30) };
        _sidebar.Controls.Add(logo);
        var ver = new Label { Text = "v2.0 · C# Self-Developed", Font = new Font("Microsoft YaHei UI", 7f), ForeColor = TextDim, Location = new Point(14, 44), Size = new Size(180, 14) };
        _sidebar.Controls.Add(ver);

        var items = new[] { ("Dashboard", "📊"), ("Connections", "🔌"), ("Speed", "⚡"), ("Bans", "🚫"), ("Logs", "📋"), ("Config", "⚙") };
        for (int i = 0; i < 6; i++)
        {
            var b = new NavBtn(items[i].Item2, items[i].Item1) { Location = new Point(6, 88 + i * 44), TabIndex = i };
            b.Click += (_, _) => SwitchTab(b.TabIndex);
            _sidebar.Controls.Add(b);
            _navBtns[i] = b;
        }

        _btnStart = new FlatBtn("▶  Start", Accent) { Location = new Point(10, 380), Size = new Size(90, 34) };
        _btnStart.Click += (_, _) => StartServer();
        _sidebar.Controls.Add(_btnStart);
        _btnStop = new FlatBtn("■  Stop", Red) { Location = new Point(108, 380), Size = new Size(90, 34), Enabled = false };
        _btnStop.Click += (_, _) => StopServer();
        _sidebar.Controls.Add(_btnStop);

        _lblSideStatus = new Label { Text = "● Stopped", Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ForeColor = Red, Location = new Point(16, 432), Size = new Size(180, 20) };
        _sidebar.Controls.Add(_lblSideStatus);
_lblSideM2 = new Label { Text = "DBServer: Disconnected", Font = new Font("Microsoft YaHei UI", 8f), ForeColor = TextDim, Location = new Point(16, 454), Size = new Size(180, 16) };
        _sidebar.Controls.Add(_lblSideM2);
        _lblSideClients = new Label { Text = "Clients: 0", Font = new Font("Microsoft YaHei UI", 8f), ForeColor = TextDim, Location = new Point(16, 472), Size = new Size(180, 16) };
        _sidebar.Controls.Add(_lblSideClients);

        Controls.Add(_sidebar);

        // ── Pages ──
        _pages = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        _dash = new DashPg(); _conn = new ConnPg(); _speedCfg = new SpeedCfgPg(_cfg);
        _ban = new BanPg(); _log = new LogPg(); _cfgPg = new CfgPg(_cfg);
        foreach (var pg in new Control[] { _dash, _conn, _speedCfg, _ban, _log, _cfgPg })
        { pg.Dock = DockStyle.Fill; pg.Visible = false; _pages.Controls.Add(pg); }
        Controls.Add(_pages);

        SwitchTab(0);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshStats();

        FormClosing += (_, e) => { if (_running) { e.Cancel = true; _log.Log("WARN", "Stop server before closing"); } };
        // Auto-start server when form loads
        Shown += (_, _) => _ = Task.Run(async () => { await Task.Delay(1000); BeginInvoke(() => StartServer()); });
    }

    private void SwitchTab(int idx)
    {
        _activeTab = idx;
        for (int i = 0; i < 6; i++) _navBtns[i].Active = i == idx;
        _dash.Visible = idx == 0; _conn.Visible = idx == 1; _speedCfg.Visible = idx == 2;
        _ban.Visible = idx == 3; _log.Visible = idx == 4; _cfgPg.Visible = idx == 5;
    }

    // ── Server ──
    private async void StartServer()
    {
        // Yield so UI thread stays responsive
        await Task.Delay(1);
        try
        {
            _btnStart.Enabled = false; _lblSideStatus.Text = "● Starting..."; _lblSideStatus.ForeColor = Orange; _log.Log("INFO", "Starting server...");
            _server = new GateServer(_cfg);
            _server.OnLog += (l, m) => BeginInvoke(() => _log.Log(l, m?.ToString() ?? ""));
            await _server.StartAsync();
            _running = true; _btnStop.Enabled = true; _lblSideStatus.Text = "● Running"; _lblSideStatus.ForeColor = Green; _timer.Start();
            _log.Log("INFO", $"Server started on port {_cfg.GatePort}");
        }
        catch (Exception ex) { _btnStart.Enabled = true; _lblSideStatus.Text = "● Error"; _lblSideStatus.ForeColor = Red;
            _log.Log("ERROR", $"Start failed: {ex.Message}");
            MessageBox.Show($"Start failed:\n{ex.Message}", "GameGate", MessageBoxButtons.OK, MessageBoxIcon.Error); try { _server?.Dispose(); } catch { } _server = null; }
    }

    private async void StopServer()
    {
        if (_server == null) return; await _server.StopAsync(); _server.Dispose(); _server = null; _running = false;
        _btnStart.Enabled = true; _btnStop.Enabled = false; _lblSideStatus.Text = "● Stopped"; _lblSideStatus.ForeColor = Red;
        _lblSideM2.Text = "DBServer: Disconnected"; _timer.Stop(); _conn.Clear(); _log.Log("INFO", "Server stopped");
    }

    private void RefreshStats()
    {
        if (_server == null || !_running) return;
        try
        {
            dynamic s = _server.GetStats();
            _lblSideM2.Text = (bool)s.Network.M2Connected ? "DBServer: Connected" : "DBServer: Disconnected";
            _lblSideM2.ForeColor = (bool)s.Network.M2Connected ? Green : TextDim;
            _lblSideClients.Text = $"Clients: {(int)s.Sessions.Active}";

            _dash.Update(new DashData
            {
                Active = s.Sessions.Active, Banned = s.Sessions.Banned, Muted = s.Sessions.Muted,
                PacketsUp = s.Network.TotalPacketsUp, PacketsDown = s.Network.TotalPacketsDown,
                BytesUp = s.Network.TotalBytesUp, BytesDown = s.Network.TotalBytesDown,
                Violations = s.Speed.TotalViolations, Penalties = s.Speed.TotalPenalties,
                M2Connected = s.Network.M2Connected, BlockedIPs = s.Ban.BlockedIPs,
                VioByType = s.Speed.ViolationsByType,
            });

            if (_activeTab == 1) _conn.UpdateSessions(_server.Sessions.GetAllActive());
        }
        catch { }
    }
}

// ── Data ──
public struct DashData
{
    public int Active, Banned, Muted;
    public long PacketsUp, PacketsDown, BytesUp, BytesDown, Violations, Penalties;
    public bool M2Connected;
    public int BlockedIPs;
    public IDictionary<ActionType, int> VioByType;
}

// ── Navigation Button ──
sealed class NavBtn : Control
{
    public bool Active { get; set; }
    private bool _hov;
    private readonly string _ico, _txt;
    public NavBtn(string ico, string txt) { _ico = ico; _txt = txt; Size = new Size(196, 38); Cursor = Cursors.Hand; DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var bg = Active ? Color.FromArgb(30, 40, 55) : _hov ? Color.FromArgb(22, 30, 40) : Color.Transparent;
        using (var b = new SolidBrush(bg)) g.FillRoundedRect(b, 2, 1, Width - 4, Height - 2, 8);
        if (Active) { using var ap = new Pen(Color.FromArgb(88, 166, 255), 3); g.DrawLine(ap, 2, 8, 2, Height - 10); }
        var c = Active ? Color.White : Color.FromArgb(180, 190, 200);
        using (var f = new Font("Segoe UI Emoji", 11f))
        using (var tb = new SolidBrush(c))
            g.DrawString(_ico, f, tb, 16, 8);
        using (var ff = new Font("Microsoft YaHei UI", 9f))
        using (var tb2 = new SolidBrush(c))
            g.DrawString(_txt, ff, tb2, 42, 10);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
    protected override void OnMouseEnter(EventArgs e) { _hov = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hov = false; Invalidate(); base.OnMouseLeave(e); }
}

// ── Flat Button ──
sealed class FlatBtn : Button
{
    private readonly Color _bg; private bool _hov;
    public FlatBtn(string txt, Color bg) { Text = txt; _bg = bg; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        ForeColor = Color.White; Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold); Cursor = Cursors.Hand; BackColor = bg; }
    protected override void OnPaint(PaintEventArgs e) { var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = Enabled ? (_hov ? Color.FromArgb(Math.Min(255, _bg.R + 25), Math.Min(255, _bg.G + 25), Math.Min(255, _bg.B + 25)) : _bg) : Color.FromArgb(60, 65, 75);
        using var b = new SolidBrush(c); g.FillRoundedRect(b, 0, 0, Width - 1, Height - 1, 6);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var tb = new SolidBrush(Enabled ? Color.White : Color.FromArgb(100, 105, 115));
        g.DrawString(Text, Font, tb, new RectangleF(0, 0, Width, Height), sf); }
    protected override void OnMouseEnter(EventArgs e) { _hov = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hov = false; Invalidate(); base.OnMouseLeave(e); }
}

// ── Dashboard Page ──
sealed class DashPg : Control
{
    private readonly Label[] _cards = new Label[12];
    private readonly Label[] _spds = new Label[10];
    public DashPg() { Dock = DockStyle.Fill; BackColor = Color.FromArgb(13, 17, 23); Visible = false; BuildUi(); }

    void BuildUi()
    {
        var fl = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(13, 17, 23), Padding = new Padding(12), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Controls.Add(fl);

        // Row 1: Big stat cards
        var r1 = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0), MinimumSize = new Size(700, 0) };
        AddCard(r1, "Active Clients", "0", Color.FromArgb(88, 166, 255), out _cards[0]);
        AddCard(r1, "Packets Up", "0", Color.FromArgb(63, 185, 80), out _cards[1]);
        AddCard(r1, "Packets Down", "0", Color.FromArgb(56, 200, 210), out _cards[2]);
        AddCard(r1, "Violations", "0", Color.FromArgb(238, 160, 40), out _cards[3]);
        AddCard(r1, "Penalties", "0", Color.FromArgb(248, 81, 73), out _cards[4]);
        AddCard(r1, "Bytes Up/Down", "0", Color.FromArgb(163, 113, 247), out _cards[5]);
        fl.Controls.Add(r1);

        // Row 2: Secondary stats
        var r2 = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0) };
        AddSmallCard(r2, "Blocked IPs", "0", out _cards[6]);
        AddSmallCard(r2, "Banned", "0", out _cards[7]);
        AddSmallCard(r2, "Muted", "0", out _cards[8]);
        AddSmallCard(r2, "M2Server", "Disconnected", out _cards[9]);
        fl.Controls.Add(r2);

        // Row 3: Speed detection table
        var spdBox = BoxPanel("Speed Detection Summary");
        var spdTbl = new TableLayoutPanel { ColumnCount = 5, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var acts = new[] { "WALK", "RUN", "ATTACK", "CAST", "TURN", "CHAT", "BUY", "CURE", "NPC", "TRADE" };
        for (int i = 0; i < 10; i++)
        {
            _spds[i] = new Label { Text = $"{acts[i]}:  0", Font = new Font("Consolas", 9.5f), ForeColor = Color.FromArgb(210, 218, 228),
                AutoSize = true, Margin = new Padding(4, 2, 20, 2) };
            spdTbl.Controls.Add(_spds[i]);
        }
        spdBox.Controls.Add(spdTbl);
        fl.Controls.Add(spdBox);

        // Row 4: Speed config summary
        var cfgBox = BoxPanel("Speed Limits (original hex constants)");
        var cfgTbl = new TableLayoutPanel { ColumnCount = 4, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var limits = new[] { "Walk=570ms (0x23A)", "Attack=900ms (0x384)", "Magic=1110ms (0x456)", "Turn=350ms (0x15E)",
                             "Chat=800ms (0x320)", "Cure=500ms", "Shop=300ms", "NPC=100ms" };
        foreach (var l in limits) cfgTbl.Controls.Add(new Label { Text = l, Font = new Font("Consolas", 9f), ForeColor = Color.FromArgb(180, 190, 200), AutoSize = true, Margin = new Padding(4, 2, 20, 2) });
        cfgBox.Controls.Add(cfgTbl);
        fl.Controls.Add(cfgBox);
    }

    public void Update(DashData d)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _cards[0].Text = $"{d.Active}"; _cards[1].Text = $"{d.PacketsUp:N0}"; _cards[2].Text = $"{d.PacketsDown:N0}";
            _cards[3].Text = $"{d.Violations:N0}"; _cards[4].Text = $"{d.Penalties:N0}";
            _cards[5].Text = $"{FmtB(d.BytesUp)} / {FmtB(d.BytesDown)}";
            _cards[6].Text = $"{d.BlockedIPs}"; _cards[7].Text = $"{d.Banned}"; _cards[8].Text = $"{d.Muted}";
            _cards[9].Text = d.M2Connected ? "Connected" : "Disconnected";
            _cards[9].ForeColor = d.M2Connected ? Color.FromArgb(63, 185, 80) : Color.FromArgb(139, 148, 158);
            int i = 0; foreach (var kv in d.VioByType) _spds[i++].Text = $"{kv.Key}:  {kv.Value}";
        });
    }

    static void AddCard(FlowLayoutPanel parent, string title, string val, Color accent, out Label vl)
    {
        var p = new Panel { Size = new Size(155, 110), BackColor = Color.FromArgb(22, 28, 36), Margin = new Padding(4) };
        p.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(40, 48, 56)); e.Graphics.DrawRoundedRect(pen, 0, 0, p.Width - 1, p.Height - 1, 8); };
        var bar = new Panel { Size = new Size(4, 110), Location = new Point(0, 0), BackColor = accent };
        p.Controls.Add(bar);
        vl = new Label { Text = val, Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = Color.White, Location = new Point(14, 20), Size = new Size(134, 32) };
        p.Controls.Add(vl);
        p.Controls.Add(new Label { Text = title, Font = new Font("Microsoft YaHei UI", 7.5f), ForeColor = Color.FromArgb(139, 148, 158), Location = new Point(14, 64), Size = new Size(134, 20) });
        parent.Controls.Add(p);
    }

    static void AddSmallCard(FlowLayoutPanel parent, string title, string val, out Label vl)
    {
        var p = new Panel { Size = new Size(200, 62), BackColor = Color.FromArgb(22, 28, 36), Margin = new Padding(3) };
        p.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(40, 48, 56)); e.Graphics.DrawRoundedRect(pen, 0, 0, p.Width - 1, p.Height - 1, 6); };
        vl = new Label { Text = val, Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Color.White, Location = new Point(12, 10), Size = new Size(180, 22) };
        p.Controls.Add(vl);
        p.Controls.Add(new Label { Text = title, Font = new Font("Microsoft YaHei UI", 7.5f), ForeColor = Color.FromArgb(139, 148, 158), Location = new Point(12, 38), Size = new Size(180, 16) });
        parent.Controls.Add(p);
    }

    static Panel BoxPanel(string title)
    {
        var p = new Panel { Size = new Size(750, 120), BackColor = Color.FromArgb(22, 28, 36), MinimumSize = new Size(400, 100), AutoSize = true, Padding = new Padding(8, 30, 8, 8) };
        p.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(40, 48, 56)); e.Graphics.DrawRoundedRect(pen, 0, 0, p.Width - 1, p.Height - 1, 8);
            using var f = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold); using var b = new SolidBrush(Color.FromArgb(210, 218, 228));
            e.Graphics.DrawString(title, f, b, 12, 8); };
        return p;
    }

    static string FmtB(long b) => b < 1024 ? $"{b}B" : b < 1048576 ? $"{b / 1024f:F1}KB" : $"{b / 1048576f:F1}MB";
}

// ── Connections Page ──
sealed class ConnPg : Control
{
    private readonly DataGridView _grid;
    public ConnPg() { Dock = DockStyle.Fill; BackColor = Color.FromArgb(13, 17, 23); Visible = false;
        _grid = MkGrid("ID", "IP Address", "Port", "Connected", "State", "Packets", "Violations", "Penalty");
        Controls.Add(_grid); }
    public void UpdateSessions(List<ClientSession> ss) { _grid.Rows.Clear();
        foreach (var s in ss) _grid.Rows.Add(s.SessionId, s.RemoteAddr, s.RemotePort, FmtDur((DateTime.Now - s.ConnectTime).TotalSeconds), s.State, s.TotalPackets, s.TotalViolations, s.PenaltyLevel); }
    public void Clear() => _grid.Rows.Clear();
    static DataGridView MkGrid(params string[] cols) { var g = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.FromArgb(13, 17, 23), BorderStyle = BorderStyle.None,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, GridColor = Color.FromArgb(30, 36, 44),
            DefaultCellStyle = new() { BackColor = Color.FromArgb(22, 28, 36), ForeColor = Color.FromArgb(230, 237, 243), SelectionBackColor = Color.FromArgb(50, 60, 80) },
            ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(28, 34, 42), ForeColor = Color.FromArgb(180, 188, 200), Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold) },
            EnableHeadersVisualStyles = false, };
        foreach (var c in cols) g.Columns.Add(new DataGridViewTextBoxColumn { Name = c, HeaderText = c, MinimumWidth = 60 }); return g; }
    static string FmtDur(double s) => s < 60 ? $"{(int)s}s" : s < 3600 ? $"{(int)(s / 60)}m{(int)(s % 60)}s" : $"{(int)(s / 3600)}h{(int)(s % 3600 / 60)}m";
}

// ── Speed Config Page ──
sealed class SpeedCfgPg : Control
{
    public SpeedCfgPg(GateConfig cfg) { Dock = DockStyle.Fill; BackColor = Color.FromArgb(13, 17, 23); Visible = false;
        var fl = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(13, 17, 23), Padding = new Padding(12),
            FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var tbl = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Padding = new Padding(8), BackColor = Color.FromArgb(22, 28, 36) };
        AddCfgRow(tbl, "Walk (ms):", cfg.WalkInterval, 100, 5000);
        AddCfgRow(tbl, "Attack (ms):", cfg.AttackInterval, 100, 5000);
        AddCfgRow(tbl, "Cast (ms):", cfg.CastInterval, 100, 10000);
        AddCfgRow(tbl, "Turn (ms):", cfg.TurnInterval, 50, 5000);
        AddCfgRow(tbl, "Cure (ms):", cfg.CureInterval, 100, 5000);
        AddCfgRow(tbl, "Shop (ms):", cfg.ShopInterval, 100, 5000);
        AddCfgRow(tbl, "NPC (ms):", cfg.NpcInterval, 10, 5000);
        AddCfgRow(tbl, "SpeedNum:", cfg.SpeedNum, 1, 500);
        fl.Controls.Add(tbl);
        var btn = new FlatBtn("Save to MirGate.ini", Color.FromArgb(63, 185, 80)) { Size = new Size(160, 34) };
        btn.Click += (_, _) => { foreach (Control c in tbl.Controls) { if (c is NumericUpDown n) { /* save would go here */ } } cfg.Save(); };
        fl.Controls.Add(btn);
        Controls.Add(fl); }
    static void AddCfgRow(TableLayoutPanel t, string label, int val, int min, int max)
    { t.Controls.Add(new Label { Text = label, ForeColor = Color.FromArgb(200, 208, 218), AutoSize = true, Margin = new Padding(8, 6, 4, 4) });
        t.Controls.Add(new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 80, BackColor = Color.FromArgb(30, 36, 46), ForeColor = Color.White, Margin = new Padding(4, 6, 8, 4) }); }
}

// ── Ban Page ──
sealed class BanPg : Control
{
    private readonly DataGridView _grid;
    private readonly TextBox _ipTb;
    public BanPg() { Dock = DockStyle.Fill; BackColor = Color.FromArgb(13, 17, 23); Visible = false;
        var top = new Panel { Height = 52, Dock = DockStyle.Top, BackColor = Color.FromArgb(18, 24, 32), Padding = new Padding(10) };
        var lbl = new Label { Text = "IP:", ForeColor = Color.FromArgb(200, 208, 218), Location = new Point(10, 16), AutoSize = true }; top.Controls.Add(lbl);
        _ipTb = new TextBox { Width = 160, Location = new Point(36, 14), BackColor = Color.FromArgb(30, 36, 46), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        top.Controls.Add(_ipTb);
        var btn = new FlatBtn("Ban IP", Color.FromArgb(248, 81, 73)) { Location = new Point(206, 10), Size = new Size(80, 30) };
        top.Controls.Add(btn);
        Controls.Add(top);
        _grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.FromArgb(13, 17, 23), BorderStyle = BorderStyle.None, RowHeadersVisible = false, GridColor = Color.FromArgb(30, 36, 44),
            DefaultCellStyle = new() { BackColor = Color.FromArgb(22, 28, 36), ForeColor = Color.FromArgb(230, 237, 243) },
            ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(28, 34, 42), ForeColor = Color.FromArgb(180, 188, 200) },
            EnableHeadersVisualStyles = false, };
        btn.Click += (_, _) => { var ip = _ipTb.Text.Trim(); if (!string.IsNullOrWhiteSpace(ip)) { _grid.Rows.Add(ip, "IP", DateTime.Now.ToString("HH:mm:ss")); _ipTb.Clear(); } };
        _grid.Columns.AddRange(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "IP/HWID" }, new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type" }, new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time" });
        _grid.Top = 52; _grid.Height = Height - 52; Controls.Add(_grid); }
}

// ── Log Page ──
sealed class LogPg : Control
{
    private readonly ListBox _list;
    public LogPg() { Dock = DockStyle.Fill; BackColor = Color.FromArgb(13, 17, 23); Visible = false;
        _list = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 17, 23), ForeColor = Color.FromArgb(220, 228, 236),
            Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
        _list.DrawItem += (_, e) => { if (e.Index < 0) return; var t = _list.Items[e.Index]?.ToString() ?? "";
            Color c = t.Contains("[ERROR") ? Color.FromArgb(248, 81, 73) : t.Contains("[WARN") ? Color.FromArgb(238, 160, 40) :
                t.Contains("[BAN") ? Color.Orange : t.Contains("[SPEED") ? Color.Cyan : t.Contains("[CONNECT") ? Color.FromArgb(63, 185, 80) : Color.FromArgb(220, 228, 236);
            e.DrawBackground(); using var b = new SolidBrush(c); e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.DrawString(t, _list.Font, b, e.Bounds); };
        Controls.Add(_list); }
    public void Log(string lvl, string msg) { if (_list.IsDisposed) return;
        if (_list.Items.Count > 5000) _list.Items.RemoveAt(0);
        _list.Items.Add($"{DateTime.Now:HH:mm:ss} [{lvl,-7}] {msg}"); _list.TopIndex = _list.Items.Count - 1; }
}

// ── Config Page ──
sealed class CfgPg : Control
{
    private readonly RichTextBox _ed;
    private readonly GateConfig _cfg;
    public CfgPg(GateConfig cfg) { _cfg = cfg; Dock = DockStyle.Fill; BackColor = Color.FromArgb(13, 17, 23); Visible = false;
        var top = new Panel { Height = 48, Dock = DockStyle.Top, BackColor = Color.FromArgb(18, 24, 32), Padding = new Padding(10) };
        var btn = new FlatBtn("Save", Color.FromArgb(63, 185, 80)) { Location = new Point(10, 8), Size = new Size(100, 32) };
        top.Controls.Add(btn);
        Controls.Add(top);
        _ed = new RichTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), BackColor = Color.FromArgb(13, 17, 23), ForeColor = Color.FromArgb(220, 228, 236), BorderStyle = BorderStyle.None, Top = 48 };
        btn.Click += (_, _) => { File.WriteAllText(Path.Combine(cfg.ConfigDir, "MirGate.ini"), _ed.Text, Encoding.GetEncoding("GBK")); MessageBox.Show("Saved."); };
        var path = Path.Combine(cfg.ConfigDir, "MirGate.ini");
        if (File.Exists(path)) _ed.Text = File.ReadAllText(path, Encoding.GetEncoding("GBK"));
        Controls.Add(_ed); }
}

// ── GDI+ Helpers ──
static class GdiEx
{
    public static void DrawRoundedRect(this Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var gp = new GraphicsPath(); gp.AddArc(x, y, r * 2, r * 2, 180, 90); gp.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        gp.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90); gp.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90); gp.CloseFigure(); g.DrawPath(pen, gp);
    }
    public static void FillRoundedRect(this Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var gp = new GraphicsPath(); gp.AddArc(x, y, r * 2, r * 2, 180, 90); gp.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        gp.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90); gp.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90); gp.CloseFigure(); g.FillPath(brush, gp);
    }
}
