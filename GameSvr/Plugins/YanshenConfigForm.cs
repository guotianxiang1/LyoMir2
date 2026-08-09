using System.Globalization;
using System.Text.Json;

namespace GameSvr.Plugins
{
    /// <summary>
    /// Native editor for the flat, GBK encoded 2.0.7 config.json.
    /// Rows are custom drawn so the original 379-key file does not create
    /// hundreds of Windows control handles.
    /// </summary>
    internal sealed class YanshenFlatConfigForm : Form
    {
        private static readonly string[] PageOrder =
        {
            "物品相关", "角色相关", "技能相关", "爆率相关", "脚本相关", "其他"
        };

        private readonly PluginManager _manager;
        private readonly Dictionary<string, object> _pending = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TogglePanel.ToggleRow> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TogglePanel.ToggleRow>> _rowsByPage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TogglePanel> _panels = new(StringComparer.Ordinal);

        private readonly TabControl _tabs;
        private readonly ToolStripTextBox _search;
        private readonly ToolStripButton _saveButton;
        private readonly ToolStripLabel _summary;
        private readonly ToolStripStatusLabel _pathStatus;
        private readonly ToolStripStatusLabel _changeStatus;
        private bool _loaded;

        public YanshenFlatConfigForm(PluginManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(
                Math.Max(760, (int)(workingArea.Width * 0.72)),
                Math.Max(560, (int)(workingArea.Height * 0.82)));
            MinimumSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterParent;
            Text = "M2超级伴侣";
            ShowInTaskbar = false;
            DoubleBuffered = true;

            var commands = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(4, 2, 4, 2),
                RenderMode = ToolStripRenderMode.System,
            };

            _saveButton = new ToolStripButton("保存") { Enabled = false };
            _saveButton.Click += (_, _) => SaveAll(true);
            var reloadButton = new ToolStripButton("重新读取配置");
            reloadButton.Click += (_, _) => ReloadFromDisk();
            var reloadDropButton = new ToolStripButton("重载眼神爆率配置");
            reloadDropButton.Click += (_, _) =>
            {
                _manager.LoadDropRateConfig();
                ShowStatus("眼神爆率配置已重新读取");
            };
            var reloadGuaranteedButton = new ToolStripButton("重载全区可爆配置");
            reloadGuaranteedButton.Click += (_, _) =>
            {
                _manager.LoadGuaranteedDropConfig();
                ShowStatus("全区可爆配置已重新读取");
            };

            _search = new ToolStripTextBox { AutoSize = false, Width = 190 };
            _search.TextBox.PlaceholderText = "查找配置";
            _search.TextChanged += (_, _) => ApplyFilter();
            _summary = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right };

            commands.Items.AddRange(new ToolStripItem[]
            {
                _saveButton,
                reloadButton,
                new ToolStripSeparator(),
                reloadDropButton,
                reloadGuaranteedButton,
                new ToolStripSeparator(),
                new ToolStripLabel("查找:"),
                _search,
                _summary,
            });

            _tabs = new TabControl { Dock = DockStyle.Fill };

            var status = new StatusStrip { SizingGrip = false };
            _pathStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _changeStatus = new ToolStripStatusLabel("就绪");
            status.Items.AddRange(new ToolStripItem[] { _pathStatus, _changeStatus });

            Controls.Add(_tabs);
            Controls.Add(commands);
            Controls.Add(status);

            Shown += (_, _) =>
            {
                if (_loaded) return;
                _loaded = true;
                LoadConfig(true);
            };
            FormClosing += OnFormClosingWithChanges;
        }

        private void LoadConfig(bool reloadFromDisk)
        {
            if (reloadFromDisk) _manager.LoadNativeConfig();

            foreach (var panel in _panels.Values) panel.Dispose();
            _tabs.TabPages.Clear();
            _panels.Clear();
            _rows.Clear();
            _rowsByPage.Clear();
            _pending.Clear();

            foreach (var pageName in PageOrder)
                _rowsByPage[pageName] = new List<TogglePanel.ToggleRow>();

            var config = _manager.GetNativeConfig();
            foreach (var (key, value) in config)
            {
                var row = TogglePanel.ToggleRow.FromConfig(key, value);
                _rows[key] = row;
                _rowsByPage[Categorize(key)].Add(row);
            }

            foreach (var pageName in PageOrder)
            {
                var pageRows = _rowsByPage[pageName];
                var panel = new TogglePanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
                panel.OnToggle += OnToggle;
                panel.OnEditChanged += OnEditChanged;
                panel.SetItems(pageRows);
                _panels[pageName] = panel;

                var page = new TabPage($"{pageName} ({pageRows.Count})");
                page.Controls.Add(panel);
                _tabs.TabPages.Add(page);
            }

            _summary.Text = $"共 {_rows.Count} 项";
            _pathStatus.Text = _manager.NativeConfigPath;
            _saveButton.Enabled = false;
            _changeStatus.Text = _rows.Count == 0 ? "未找到 config.json 或配置为空" : "配置已载入";
        }

        private void OnToggle(string key, bool enabled)
        {
            if (!_rows.TryGetValue(key, out var row)) return;
            _pending[key] = row.GetToggleValue(enabled);
            MarkChanged();
        }

        private void OnEditChanged(string key, string text)
        {
            if (!_rows.TryGetValue(key, out var row)) return;
            if (row.TryConvertText(text, out var value, out _))
                _pending[key] = value;
            else
                _pending[key] = text;
            MarkChanged();
        }

        private void MarkChanged()
        {
            _saveButton.Enabled = _pending.Count > 0;
            _changeStatus.Text = $"{_pending.Count} 项未保存";
        }

        private bool SaveAll(bool showSuccess)
        {
            foreach (var panel in _panels.Values) panel.CommitPendingEdit();
            if (_pending.Count == 0) return true;

            var changes = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (key, pendingValue) in _pending)
            {
                if (!_rows.TryGetValue(key, out var row)) continue;
                object converted = null;
                if (!row.IsToggle && !row.TryConvertText(row.TextValue, out converted, out var validationError))
                {
                    MessageBox.Show(
                        $"{key}: {validationError}",
                        "配置值无效",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }
                changes[key] = row.IsToggle ? pendingValue : converted;
            }

            if (!_manager.ApplyNativeConfigChanges(changes, out var error))
            {
                MessageBox.Show(
                    $"配置保存失败:\r\n{error}",
                    "保存失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            _pending.Clear();
            _saveButton.Enabled = false;
            _changeStatus.Text = "保存成功，运行时配置已更新";

            if (showSuccess)
            {
                MessageBox.Show(
                    $"本页配置保存成功，配置文件路径:\r\n{_manager.NativeConfigPath}",
                    "保存成功",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return true;
        }

        private void ReloadFromDisk()
        {
            if (_pending.Count > 0)
            {
                var answer = MessageBox.Show(
                    "放弃尚未保存的修改并重新读取 config.json?",
                    "重新读取配置",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }
            LoadConfig(true);
        }

        private void ApplyFilter()
        {
            if (!_loaded) return;
            foreach (var panel in _panels.Values) panel.CommitPendingEdit();

            var query = _search.Text.Trim();
            foreach (var pageName in PageOrder)
            {
                var source = _rowsByPage[pageName];
                var visible = string.IsNullOrEmpty(query)
                    ? source
                    : source.Where(row => row.Key.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                _panels[pageName].SetItems(visible);
            }
        }

        private void OnFormClosingWithChanges(object sender, FormClosingEventArgs e)
        {
            foreach (var panel in _panels.Values) panel.CommitPendingEdit();
            if (_pending.Count == 0) return;
            var answer = MessageBox.Show(
                "配置已经修改，是否保存?",
                "M2超级伴侣",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == DialogResult.Yes && !SaveAll(false)) e.Cancel = true;
        }

        private async void ShowStatus(string text)
        {
            _changeStatus.Text = text;
            await Task.Delay(2500);
            if (!IsDisposed && _pending.Count == 0) _changeStatus.Text = "就绪";
        }

        private static string Categorize(string key)
        {
            if (ContainsAny(key, "触发", "函数", "脚本", "循环", "ServerSay", "SetNoKillMapLv", "AddLimLF", "IncActivePoint", "毫秒级cd"))
                return "脚本相关";
            if (ContainsAny(key, "爆率", "爆物", "全服击杀提示"))
                return "爆率相关";
            if (ContainsAny(key, "装备", "物品", "背包", "拾取", "捡物", "回收", "仓库", "绑定", "投保", "极品", "戒指", "武器", "衣服", "头盔", "项链", "手镯", "金币", "邮件", "交易", "丢物"))
                return "物品相关";
            if (ContainsAny(key, "技能", "剑", "刀", "火", "雷", "毒", "术", "攻击", "伤害", "切割", "麻痹", "吸血", "反伤", "暴击", "魔法", "召唤", "骷髅", "神兽", "盾", "格挡", "合击", "冰咆哮", "激光"))
                return "技能相关";
            if (ContainsAny(key, "人物", "角色", "英雄", "宝宝", "宠物", "行会", "战队", "摆摊", "红名", "称号", "名字", "等级", "发言", "下线", "穿人", "穿怪", "复活", "永久", "移动速度", "施法速度", "攻速", "职业", "阵营", "沙城", "攻城"))
                return "角色相关";
            return "其他";
        }

        private static bool ContainsAny(string value, params string[] fragments) =>
            fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Virtualized multi-column editor used by the 2.0.7 configuration pages.</summary>
    public sealed class TogglePanel : Panel
    {
        private const int RowHeight = 30;
        private const int MinimumColumnWidth = 260;

        private readonly Font _font;
        private readonly Pen _borderPen = new(Color.LightGray);
        private readonly SolidBrush _enabledBrush = new(Color.FromArgb(221, 244, 226));
        private readonly SolidBrush _disabledBrush = new(SystemColors.ControlLightLight);
        private readonly SolidBrush _hoverBrush = new(Color.FromArgb(232, 240, 249));
        private readonly VScrollBar _scrollBar;
        private readonly TextBox _editor;

        private List<ToggleRow> _items = new();
        private int _columns = 1;
        private int _columnWidth = MinimumColumnWidth;
        private int _scrollY;
        private int _hoverIndex = -1;
        private int _editIndex = -1;
        private bool _cancelEdit;

        public event Action<string, bool> OnToggle;
        public event Action<string, string> OnEditChanged;

        public int ItemCount => _items.Count;

        public TogglePanel()
        {
            _font = YanshenUiFont.CreateTextRenderer("Microsoft YaHei UI", 8.5f, DeviceDpi);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            _scrollBar = new VScrollBar { Dock = DockStyle.Right };
            _scrollBar.ValueChanged += (_, _) =>
            {
                _scrollY = _scrollBar.Value;
                PositionEditor();
                Invalidate();
            };
            Controls.Add(_scrollBar);

            _editor = new TextBox { Visible = false, Font = YanshenUiFont.Create("Consolas", 9f) };
            _editor.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    CommitPendingEdit();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _cancelEdit = true;
                    HideEditor();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            _editor.LostFocus += (_, _) =>
            {
                if (_cancelEdit)
                {
                    _cancelEdit = false;
                    return;
                }
                CommitPendingEdit();
            };
            Controls.Add(_editor);
        }

        public void SetItems(IEnumerable<ToggleRow> items)
        {
            CommitPendingEdit();
            _items = items?.ToList() ?? new List<ToggleRow>();
            _hoverIndex = -1;
            _scrollY = 0;
            UpdateScrollBar();
            Invalidate();
        }

        public void CommitPendingEdit()
        {
            if (_editIndex < 0 || _editIndex >= _items.Count || !_editor.Visible) return;
            var row = _items[_editIndex];
            row.TextValue = _editor.Text;
            OnEditChanged?.Invoke(row.Key, row.TextValue);
            HideEditor();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            var availableWidth = Math.Max(1, ClientSize.Width - _scrollBar.Width - 8);
            _columns = Math.Max(1, availableWidth / MinimumColumnWidth);
            _columnWidth = Math.Max(1, availableWidth / _columns);
            UpdateScrollBar();
            PositionEditor();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_items.Count == 0) return;

            var startRow = Math.Max(0, _scrollY / RowHeight);
            var endRow = Math.Min(RowCount - 1, startRow + ClientSize.Height / RowHeight + 1);
            for (var rowIndex = startRow; rowIndex <= endRow; rowIndex++)
            {
                for (var column = 0; column < _columns; column++)
                {
                    var index = rowIndex * _columns + column;
                    if (index >= _items.Count) break;

                    var item = _items[index];
                    var bounds = CellBounds(index);
                    var brush = index == _hoverIndex
                        ? _hoverBrush
                        : item.IsToggle && item.BoolValue ? _enabledBrush : _disabledBrush;
                    e.Graphics.FillRectangle(brush, bounds);
                    e.Graphics.DrawRectangle(_borderPen, bounds);

                    var text = item.IsToggle
                        ? item.Key + (item.BoolValue ? "(已启用)" : "(未启用)")
                        : item.Key + ": " + item.TextValue;
                    var textBounds = Rectangle.Inflate(bounds, -6, -2);
                    TextRenderer.DrawText(
                        e.Graphics,
                        text,
                        _font,
                        textBounds,
                        SystemColors.ControlText,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = HitTest(e.Location);
            if (index == _hoverIndex) return;
            _hoverIndex = index;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex < 0) return;
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var index = HitTest(e.Location);
            if (index < 0) return;

            var item = _items[index];
            if (item.IsToggle)
            {
                item.BoolValue = !item.BoolValue;
                OnToggle?.Invoke(item.Key, item.BoolValue);
                Invalidate(CellBounds(index));
            }
            else if (item.CanEdit)
            {
                BeginEdit(index);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_scrollBar.Enabled) return;
            var requested = _scrollBar.Value - e.Delta / 3;
            _scrollBar.Value = Math.Max(_scrollBar.Minimum, Math.Min(MaxScrollValue, requested));
        }

        private int RowCount => (_items.Count + _columns - 1) / _columns;

        private int MaxScrollValue => Math.Max(
            _scrollBar.Minimum,
            _scrollBar.Maximum - _scrollBar.LargeChange + 1);

        private void UpdateScrollBar()
        {
            var contentHeight = RowCount * RowHeight;
            _scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
            _scrollBar.SmallChange = RowHeight;
            _scrollBar.Maximum = Math.Max(0, contentHeight - 1);
            _scrollBar.Enabled = contentHeight > ClientSize.Height;
            _scrollBar.Visible = true;

            var value = _scrollBar.Enabled ? Math.Min(_scrollY, MaxScrollValue) : 0;
            _scrollY = value;
            if (_scrollBar.Value != value) _scrollBar.Value = value;
        }

        private int HitTest(Point point)
        {
            if (point.X < 0 || point.Y < 0 || point.X >= ClientSize.Width - _scrollBar.Width)
                return -1;
            var column = point.X / _columnWidth;
            if (column < 0 || column >= _columns) return -1;
            var row = (point.Y + _scrollY) / RowHeight;
            var index = row * _columns + column;
            return index >= 0 && index < _items.Count ? index : -1;
        }

        private Rectangle CellBounds(int index)
        {
            var row = index / _columns;
            var column = index % _columns;
            return new Rectangle(
                column * _columnWidth + 3,
                row * RowHeight - _scrollY + 2,
                Math.Max(1, _columnWidth - 6),
                RowHeight - 4);
        }

        private void BeginEdit(int index)
        {
            CommitPendingEdit();
            _editIndex = index;
            _editor.Text = _items[index].TextValue;
            PositionEditor();
            _editor.Visible = true;
            _editor.BringToFront();
            _editor.Focus();
            _editor.SelectAll();
        }

        private void PositionEditor()
        {
            if (_editIndex < 0 || _editIndex >= _items.Count) return;
            var bounds = Rectangle.Inflate(CellBounds(_editIndex), -4, -2);
            _editor.Bounds = bounds;
        }

        private void HideEditor()
        {
            _editor.Visible = false;
            _editIndex = -1;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font.Dispose();
                _borderPen.Dispose();
                _enabledBrush.Dispose();
                _disabledBrush.Dispose();
                _hoverBrush.Dispose();
            }
            base.Dispose(disposing);
        }

        public sealed class ToggleRow
        {
            public string Key { get; private init; } = string.Empty;
            public object OriginalValue { get; private init; }
            public bool IsToggle { get; private init; }
            public bool BoolValue { get; set; }
            public string TextValue { get; set; } = string.Empty;
            public bool CanEdit { get; private init; }

            public static ToggleRow FromConfig(string key, object rawValue)
            {
                var value = PluginManager.NormalizeConfigValue(rawValue);
                var isToggle = value is bool || IsBinaryNumber(value) &&
                    (key.EndsWith("_是否勾选", StringComparison.Ordinal) || !LooksLikeParameter(key));
                return new ToggleRow
                {
                    Key = key,
                    OriginalValue = value,
                    IsToggle = isToggle,
                    BoolValue = isToggle && ToBoolean(value),
                    TextValue = FormatValue(value),
                    CanEdit = value is string || value is null || IsNumber(value),
                };
            }

            public object GetToggleValue(bool enabled)
            {
                if (OriginalValue is bool) return enabled;
                if (OriginalValue is int) return enabled ? 1 : 0;
                if (OriginalValue is uint) return enabled ? 1U : 0U;
                if (OriginalValue is long) return enabled ? 1L : 0L;
                if (OriginalValue is ulong) return enabled ? 1UL : 0UL;
                if (OriginalValue is short) return (short)(enabled ? 1 : 0);
                if (OriginalValue is ushort) return (ushort)(enabled ? 1 : 0);
                if (OriginalValue is byte) return (byte)(enabled ? 1 : 0);
                if (OriginalValue is sbyte) return (sbyte)(enabled ? 1 : 0);
                if (OriginalValue is decimal) return enabled ? 1m : 0m;
                if (OriginalValue is float) return enabled ? 1f : 0f;
                if (OriginalValue is double) return enabled ? 1d : 0d;
                return enabled ? 1L : 0L;
            }

            public bool TryConvertText(string text, out object value, out string error)
            {
                error = null;
                if (OriginalValue is string || OriginalValue is null)
                {
                    value = text;
                    return true;
                }

                var style = NumberStyles.Integer;
                if (OriginalValue is long && long.TryParse(text, style, CultureInfo.InvariantCulture, out var int64)) { value = int64; return true; }
                if (OriginalValue is int && int.TryParse(text, style, CultureInfo.InvariantCulture, out var int32)) { value = int32; return true; }
                if (OriginalValue is uint && uint.TryParse(text, style, CultureInfo.InvariantCulture, out var uint32)) { value = uint32; return true; }
                if (OriginalValue is ulong && ulong.TryParse(text, style, CultureInfo.InvariantCulture, out var uint64)) { value = uint64; return true; }
                if (OriginalValue is short && short.TryParse(text, style, CultureInfo.InvariantCulture, out var int16)) { value = int16; return true; }
                if (OriginalValue is ushort && ushort.TryParse(text, style, CultureInfo.InvariantCulture, out var uint16)) { value = uint16; return true; }
                if (OriginalValue is byte && byte.TryParse(text, style, CultureInfo.InvariantCulture, out var byteValue)) { value = byteValue; return true; }
                if (OriginalValue is sbyte && sbyte.TryParse(text, style, CultureInfo.InvariantCulture, out var sbyteValue)) { value = sbyteValue; return true; }
                if (OriginalValue is decimal && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)) { value = decimalValue; return true; }
                if (OriginalValue is float && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue)) { value = floatValue; return true; }
                if (OriginalValue is double && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)) { value = doubleValue; return true; }

                value = null;
                error = "需要与原配置相同类型的有效数值";
                return false;
            }

            private static bool LooksLikeParameter(string key) =>
                key.Contains('_') && !key.EndsWith("_plus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "主号全局法速", StringComparison.Ordinal);

            private static bool IsBinaryNumber(object value) => value switch
            {
                byte number => number is 0 or 1,
                sbyte number => number is 0 or 1,
                short number => number is 0 or 1,
                ushort number => number is 0 or 1,
                int number => number is 0 or 1,
                uint number => number is 0 or 1,
                long number => number is 0 or 1,
                ulong number => number is 0 or 1,
                decimal number => number is 0 or 1,
                float number => number is 0 or 1,
                double number => number is 0 or 1,
                _ => false,
            };

            private static bool IsNumber(object value) => value is
                byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double;

            private static bool ToBoolean(object value) => value switch
            {
                bool enabled => enabled,
                IConvertible convertible => convertible.ToDecimal(CultureInfo.InvariantCulture) != 0,
                _ => false,
            };

            private static string FormatValue(object value)
            {
                if (value == null) return string.Empty;
                if (value is string text) return text;
                if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
                return JsonSerializer.Serialize(value);
            }
        }
    }
}
