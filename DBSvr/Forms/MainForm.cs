using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DBSvr.Core;

namespace DBSvr.Forms
{
    public sealed class MainForm : Form
    {
        private readonly UserSocService _userSoc;
        private readonly LoginSvrService _loginSoc;
        private readonly GameSocService _gameSoc;
        private readonly SensitiveWordFilter _sensitiveFilter;
        private readonly CleanupService _cleanupService;
        private readonly BackupService _backupService;
        private readonly WhitelistService _whitelist;
        private readonly IPlayRecordService _playRecord;
        private readonly ITransferAreaService _transferService;
        private readonly INativeType2StaticLoader _nativeType2StaticLoader;
        private readonly NativeType2InitializationCache _nativeType2Cache;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private CancellationTokenSource _cts;
        private Task _timedTask = Task.CompletedTask;
        private bool _servicesRunning;

        private RichTextBox _logBox;
        private StatusStrip _statusBar;
        private ToolStripStatusLabel _statusLabel;

        public MainForm(UserSocService userSoc, LoginSvrService loginSoc,
            GameSocService gameSoc, SensitiveWordFilter sensitiveFilter,
            WhitelistService whitelist, CleanupService cleanup, BackupService backup,
            IPlayRecordService playRecord,
            ITransferAreaService transferService,
            INativeType2StaticLoader nativeType2StaticLoader,
            NativeType2InitializationCache nativeType2Cache)
        {
            _userSoc = userSoc; _loginSoc = loginSoc; _gameSoc = gameSoc;
            _sensitiveFilter = sensitiveFilter; _cleanupService = cleanup;
            _backupService = backup;
            _whitelist = whitelist; _playRecord = playRecord;
            _transferService = transferService;
            _nativeType2StaticLoader = nativeType2StaticLoader;
            _nativeType2Cache = nativeType2Cache;

            InitUI();
            RedirectConsole();
            Shown += (_, _) => _ = StartServices();
        }

        private void InitUI()
        {
            Text = $"DBServer - {DBShare.sServerName}";
            Size = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;

            // === Menu ===
            var menu = new MenuStrip();
            var svcMenu = new ToolStripMenuItem("服务(&S)");
            svcMenu.DropDownItems.Add("启动(&T)", null, (s, e) => _ = StartServices());
            svcMenu.DropDownItems.Add("停止(&P)", null, async (s, e) => await StopServices());
            svcMenu.DropDownItems.Add(new ToolStripSeparator());
            svcMenu.DropDownItems.Add("重载敏感词(&R)", null, (s, e) => { _sensitiveFilter.Load(); Log("敏感词已重载"); });
            svcMenu.DropDownItems.Add(new ToolStripSeparator());
            svcMenu.DropDownItems.Add("退出(&X)", null, (s, e) => Close());
            menu.Items.Add(svcMenu);

            var dbMenu = new ToolStripMenuItem("数据库(&D)");
            dbMenu.DropDownItems.Add("热备份(&B)", null, (s, e) => Task.Run(() =>
            {
                var ok = _backupService.HotBackupToMir3Backup();
                Log(ok ? "热备份完成" : "热备份失败");
            }));
            dbMenu.DropDownItems.Add("清理不活跃(&C)", null, (s, e) => Task.Run(() => { int n = _cleanupService.CleanInactiveCharacters(); Log($"清理 {n} 个不活跃角色"); }));
            dbMenu.DropDownItems.Add("清理孤立数据(&O)", null, (s, e) => Task.Run(() => { int n = _cleanupService.CleanOrphanData(); Log($"清理 {n} 条孤立数据"); }));
            menu.Items.Add(dbMenu);

            var viewMenu = new ToolStripMenuItem("查看(&V)");
            viewMenu.DropDownItems.Add("角色列表(&C)", null, (s, e) => ShowCharListWindow());
            viewMenu.DropDownItems.Add("排行榜(&R)", null, (s, e) => ShowRankWindow());
            viewMenu.DropDownItems.Add("会话列表(&S)", null, (s, e) => ShowSessionWindow());
            menu.Items.Add(viewMenu);

            Controls.Add(menu);
            MainMenuStrip = menu;

            // === Log panel (main area) ===
            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LimeGreen,
                Font = new Font("Consolas", 10),
                WordWrap = true
            };
            _logBox.ContextMenuStrip = new ContextMenuStrip();
            _logBox.ContextMenuStrip.Items.Add("清空", null, (s, e) => _logBox.Clear());
            _logBox.ContextMenuStrip.Items.Add("复制", null, (s, e) => { if (_logBox.SelectedText.Length > 0) Clipboard.SetText(_logBox.SelectedText); else Clipboard.SetText(_logBox.Text); });
            Controls.Add(_logBox);

            // === StatusBar ===
            _statusBar = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("就绪");
            var statsLabel = new ToolStripStatusLabel();
            _statusBar.Items.Add(_statusLabel);
            _statusBar.Items.Add(statsLabel);
            Controls.Add(_statusBar);

            // Timer for stats
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += (s, e) =>
            {
                int cnt = _userSoc?.GetUserCount() ?? 0;
                statsLabel.Text = $"在线:{cnt} | Gate:{DBShare.g_nGatePort} | GameSvr:{DBShare.nServerPort} | LoginSvr:{DBShare.sIDServerAddr}:{DBShare.nIDServerPort} | {DateTime.Now:HH:mm:ss}";
            };
            timer.Start();
        }

        // ===================== Console → GUI =====================

        private void RedirectConsole()
        {
            var writer = new GuiTextWriter(msg =>
            {
                if (_logBox.IsDisposed) return;
                _logBox.Invoke(() =>
                {
                    _logBox.AppendText(msg);
                    if (_logBox.TextLength > 80000)
                    {
                        _logBox.Select(0, _logBox.TextLength / 4);
                        _logBox.SelectedText = "";
                    }
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.ScrollToCaret();
                });
            });
            Console.SetOut(writer);
            Console.SetError(writer);
        }

        private sealed class GuiTextWriter : System.IO.TextWriter
        {
            private readonly Action<string> _write;
            public GuiTextWriter(Action<string> write) => _write = write;
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
            public override void Write(char value) => _write(value.ToString());
            public override void Write(string value) => _write(value);
        }

        private void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

        // ===================== Lifecycle =====================

        private async Task StartServices()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_servicesRunning) return;
                _statusLabel.Text = "正在启动...";
                // The native DBServer performs destructive maintenance once during startup.
                // It is controlled by DBService.ini [Server] AutoClear, not a periodic timer.
                if (DBShare.boAutoClear)
                {
                    _cleanupService.CleanInactiveCharacters();
                    _cleanupService.CleanAncientCharacters();
                    _cleanupService.CleanOrphanData();
                    _transferService.CleanExpiredRecords(DBShare.TransferRecordDays);
                }

                if (!_nativeType2StaticLoader.TryLoad(out var staticRecords))
                    throw new InvalidOperationException(
                        "原生type2主初始化表加载失败");
                _nativeType2Cache.ReplacePrimary(staticRecords);
                _playRecord.LoadQuickList();
                _sensitiveFilter.Load();
                _whitelist.Load();

                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _userSoc.Start();
                _loginSoc.Start();
                _gameSoc.Start();
                _servicesRunning = true;
                var timedToken = _cts.Token;
                _timedTask = Task.Run(() => RunTimedLoop(timedToken));

                Log("============= DBService Start =============");
                Log($"Gate:{DBShare.g_nGatePort}  GameSvr:{DBShare.nServerPort}  LoginSvr:{DBShare.sIDServerAddr}:{DBShare.nIDServerPort}");
                Log($"敏感词:{_sensitiveFilter.AbuseWordCount}  禁止名字:{_sensitiveFilter.DenyNameCount}");
                Log("服务器已启动...");

                _statusLabel.Text = "运行中";
            }
            catch (Exception ex)
            {
                StopSockets();
                Log($"启动失败: {ex.Message}");
                _statusLabel.Text = "启动失败";
            }
            finally { _lifecycleLock.Release(); }
        }

        private async Task RunTimedLoop(CancellationToken ct)
        {
            var nextLoginMaintenance = Environment.TickCount64;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try { _sensitiveFilter.Reload(); }
                    catch (Exception ex)
                    {
                        Log($"[SensitiveReload] {ex.Message}");
                    }
                    var now = Environment.TickCount64;
                    if (now >= nextLoginMaintenance)
                    {
                        try
                        {
                            _loginSoc.SendKeepAlivePacket(_userSoc.GetUserCount());
                            _loginSoc.CheckConnection();
                            _loginSoc.ClearTimeoutSession();
                        }
                        catch (Exception ex) { Log($"[TimedService] {ex.Message}"); }
                        finally { nextLoginMaintenance = now + 5000; }
                    }
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task StopServices()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (!_servicesRunning)
                {
                    _statusLabel.Text = "已停止";
                    return;
                }
                _cts?.Cancel();
                StopSockets();
                try { await _timedTask; } catch (OperationCanceledException) { }
                _servicesRunning = false;
                _statusLabel.Text = "已停止";
                Log("服务已停止");
            }
            finally { _lifecycleLock.Release(); }
        }

        private void StopSockets()
        {
            try { _userSoc.Stop(); } catch { }
            try { _gameSoc.Stop(); } catch { }
            try { _loginSoc.Stop(); } catch { }
        }

        // ===================== Sub-windows =====================

        private void ShowCharListWindow()
        {
            var frm = new Form { Text = "角色列表", Size = new Size(800, 500), StartPosition = FormStartPosition.CenterParent };
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lv.Columns.Add("角色名", 100); lv.Columns.Add("账号", 120); lv.Columns.Add("等级", 50);
            lv.Columns.Add("职业", 50); lv.Columns.Add("经验", 80); lv.Columns.Add("战力", 60); lv.Columns.Add("最后修改", 130);

            var btn = new Button { Text = "刷新", Dock = DockStyle.Bottom, Height = 30 };
            var lbl = new Label { Text = "  (双击角色查看详情)", Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.Gray };
            frm.Controls.Add(lv); frm.Controls.Add(lbl); frm.Controls.Add(btn);

            btn.Click += (s, e) => RefreshListView(lv);
            lv.DoubleClick += (s, e) => { if (lv.SelectedItems.Count > 0) ShowCharDetail(lv.SelectedItems[0].Text); };

            RefreshListView(lv);
            frm.Show(this);
        }

        private static void RefreshListView(ListView lv)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using var cmd = new MySql.Data.MySqlClient.MySqlCommand(
                    @"SELECT ChrName, PTID, Level, Job, Exp, FightPoints, ForceLv, ModifyDate
                      FROM mir3.user_index WHERE IsDelete=0 ORDER BY ModifyDate DESC LIMIT 500", conn);
                using var dr = cmd.ExecuteReader();
                var jobs = new[] { "战", "法", "道", "刺" };
                while (dr.Read())
                {
                    var it = new ListViewItem(LegacyGbkText.Read(dr, 0));
                    it.SubItems.Add(dr.GetString(1));
                    it.SubItems.Add(dr.GetInt32(2).ToString());
                    it.SubItems.Add(jobs[Math.Min(dr.GetInt32(3), 3)]);
                    it.SubItems.Add(dr.GetInt32(4).ToString("#,0"));
                    it.SubItems.Add((dr.IsDBNull(5) ? 0 : dr.GetInt32(5)).ToString());
                    it.SubItems.Add((dr.IsDBNull(6) ? 0 : dr.GetInt32(6)).ToString());
                    it.SubItems.Add(dr.GetDateTime(7).ToString("MM-dd HH:mm"));
                    lv.Items.Add(it);
                }
            }
            catch { }
            lv.EndUpdate();
        }

        private static void ShowCharDetail(string chrName)
        {
            var frm = new Form { Text = $"角色: {chrName}", Size = new Size(550, 450), StartPosition = FormStartPosition.CenterParent };
            var pg = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true };
            frm.Controls.Add(pg);

            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using var cmd = new MySql.Data.MySqlClient.MySqlCommand(
                    @"SELECT ChrName, PTID, Level, Exp, Job, Sex, ForceLv, ForceExp, FightPoints, sfLevel,
                             IsDelete, CreateDate, ModifyDate, lvChangeTime
                      FROM mir3.user_index WHERE ChrName=@n LIMIT 1", conn);
                cmd.Parameters.Add(LegacyGbkText.Parameter("@n", chrName));
                using var dr = cmd.ExecuteReader();
                if (dr.Read())
                {
                    pg.SelectedObject = new
                    {
                        角色名 = LegacyGbkText.Read(dr, 0),
                        账号 = dr.GetString(1),
                        等级 = dr.GetInt32(2),
                        经验 = dr.GetInt32(3).ToString("#,0"),
                        职业 = dr.GetInt32(4),
                        性别 = dr.GetInt32(5),
                        内力等级 = dr.IsDBNull(6) ? 0 : dr.GetInt32(6),
                        内力经验 = dr.IsDBNull(7) ? 0 : dr.GetInt32(7),
                        战力 = dr.IsDBNull(8) ? 0 : dr.GetInt32(8),
                        巅峰等级 = dr.IsDBNull(9) ? 0 : dr.GetInt32(9),
                        创建时间 = dr.GetDateTime(11),
                        最后修改 = dr.GetDateTime(12),
                    };
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "错误"); }
            frm.Show();
        }

        private void ShowRankWindow()
        {
            var frm = new Form { Text = "排行榜", Size = new Size(500, 500), StartPosition = FormStartPosition.CenterParent };
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lv.Columns.Add("排名", 50); lv.Columns.Add("角色名", 130); lv.Columns.Add("等级", 60);
            lv.Columns.Add("巅峰Lv", 60); lv.Columns.Add("内力Lv", 60); lv.Columns.Add("战力", 70);
            frm.Controls.Add(lv);

            Task.Run(() =>
            {
                try
                {
                    using var conn = new MySql.Data.MySqlClient.MySqlConnection(DBShare.DBConnection);
                    conn.Open();
                    using var cmd = new MySql.Data.MySqlClient.MySqlCommand(
                        @"SELECT ChrName, Level, sfLevel, ForceLv, FightPoints
                          FROM mir3.user_index WHERE IsDelete=0 AND Level>0 AND AdminLevel=0
                          ORDER BY Level DESC, sfLevel DESC LIMIT 100", conn);
                    using var dr = cmd.ExecuteReader();
                    int rank = 0;
                    frm.Invoke(() => lv.BeginUpdate());
                    while (dr.Read())
                    {
                        rank++;
                        var it = new ListViewItem(rank.ToString());
                        it.SubItems.Add(LegacyGbkText.Read(dr, 0));
                        it.SubItems.Add(dr.GetInt32(1).ToString());
                        it.SubItems.Add((dr.IsDBNull(2) ? 0 : dr.GetInt32(2)).ToString());
                        it.SubItems.Add((dr.IsDBNull(3) ? 0 : dr.GetInt32(3)).ToString());
                        it.SubItems.Add((dr.IsDBNull(4) ? 0 : dr.GetInt32(4)).ToString());
                        frm.Invoke(() => lv.Items.Add(it));
                    }
                    frm.Invoke(() => lv.EndUpdate());
                }
                catch { }
            });
            frm.Show(this);
        }

        private void ShowSessionWindow()
        {
            var frm = new Form { Text = "会话列表", Size = new Size(500, 400), StartPosition = FormStartPosition.CenterParent };
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lv.Columns.Add("账号", 120); lv.Columns.Add("IP", 120); lv.Columns.Add("会话ID", 80); lv.Columns.Add("已加载", 60); lv.Columns.Add("已开始", 60);
            var btn = new Button { Text = "刷新", Dock = DockStyle.Bottom, Height = 30 };
            frm.Controls.Add(lv); frm.Controls.Add(btn);
            btn.Click += (s, e) => MessageBox.Show("会话列表功能开发中", "提示");
            frm.Show(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            StopSockets();
            try { _timedTask?.Wait(2000); } catch { }
            _servicesRunning = false;
            base.OnFormClosing(e);
        }
    }
}
