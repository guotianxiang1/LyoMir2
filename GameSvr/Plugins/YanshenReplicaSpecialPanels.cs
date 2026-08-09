namespace GameSvr.Plugins
{
    internal interface IReplicaConfigEditor
    {
        event Action<string, bool> OnToggle;
        event Action<string, string> OnEditChanged;

        void SetFeatures(IEnumerable<ReplicaFeature> features);
        void CommitPendingEdit();
    }

    internal sealed class EquipmentReplicaPanel : Panel, IReplicaConfigEditor
    {
        private const string HorizontalSeparatorText =
            "--------------------------------------------------------------------------------------------------------------------极品设置----------------------------------------------------------------------------------------------------------------------------";

        private static readonly string[] EquipmentOrder =
        {
            "武器", "衣服", "头盔", "项链", "手镯", "戒指"
        };

        private static readonly string[] AttributeOrder =
        {
            "攻击", "魔法", "道术", "攻速", "准确"
        };

        private static readonly int[][] CellX =
        {
            new[] { 76, 118, 166 },
            new[] { 230, 278, 329 },
            new[] { 401, 448, 499 },
            new[] { 569, 611, 658 },
            new[] { 727, 775, 826 },
        };

        private static readonly int[][] CellY =
        {
            new[] { 100, 145, 191, 236, 282, 327 },
            new[] { 100, 145, 191, 236, 282, 327 },
            new[] { 100, 145, 191, 236, 282, 327 },
            new[] { 96, 142, 187, 233, 278, 324 },
            new[] { 98, 143, 189, 234, 280, 325 },
        };
        private readonly Font _font;
        private readonly Font _blueFont;
        private readonly Font _separatorFont;
        private readonly TextBox _editor;
        private readonly List<ParameterCell> _parameterCells = new();
        private IReadOnlyList<ReplicaFeature> _features = Array.Empty<ReplicaFeature>();
        private ParameterCell _activeCell;
        private bool _cancelEdit;

        public event Action<string, bool> OnToggle;
        public event Action<string, string> OnEditChanged;

        public EquipmentReplicaPanel()
        {
            _font = YanshenUiFont.Create("SimSun", 9f);
            _blueFont = YanshenUiFont.CreateTextRenderer("SimSun", 9f, DeviceDpi);
            _separatorFont = YanshenUiFont.CreateTextRenderer("Tahoma", 8.25f, DeviceDpi);
            BackColor = SystemColors.Control;
            BorderStyle = BorderStyle.FixedSingle;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            _editor = new TextBox
            {
                AutoSize = false,
                BorderStyle = BorderStyle.None,
                Font = _font,
                Visible = false,
            };
            _editor.KeyDown += (_, eventArgs) =>
            {
                if (eventArgs.KeyCode == Keys.Enter)
                {
                    CommitPendingEdit();
                    eventArgs.Handled = true;
                    eventArgs.SuppressKeyPress = true;
                }
                else if (eventArgs.KeyCode == Keys.Escape)
                {
                    _cancelEdit = true;
                    HideEditor();
                    eventArgs.Handled = true;
                    eventArgs.SuppressKeyPress = true;
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

        public void SetFeatures(IEnumerable<ReplicaFeature> features)
        {
            CommitPendingEdit();
            _features = features?.ToArray() ?? Array.Empty<ReplicaFeature>();
            Invalidate();
        }

        public void CommitPendingEdit()
        {
            if (_activeCell == null || !_editor.Visible) return;
            _activeCell.Row.TextValue = _editor.Text;
            OnEditChanged?.Invoke(_activeCell.Row.Key, _editor.Text);
            HideEditor();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            _parameterCells.Clear();
            var graphics = eventArgs.Graphics;

            PaintToggle(graphics, "屏蔽自动绑定", new Rectangle(28, 17, 300, 22));
            PaintToggle(graphics, "随机极品", new Rectangle(383, 17, 260, 22));

            DrawSeparatorText(graphics);

            for (var group = 0; group < AttributeOrder.Length; group++)
            {
                var x = CellX[group][0] - 2;
                var maximumBounds = new Rectangle(x, 52, 34, 35);
                var pointChanceBounds = new Rectangle(x + 42, 52, 38, 35);
                var attributeChanceBounds = new Rectangle(x + 87, 52, 40, 35);
                graphics.FillRectangle(Brushes.White, maximumBounds);
                graphics.FillRectangle(Brushes.White, pointChanceBounds);
                graphics.FillRectangle(Brushes.White, attributeChanceBounds);
                DrawText(graphics, "最高\r\n点数:", maximumBounds, Color.Blue,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                DrawText(graphics, "点数\r\n几率:", pointChanceBounds, Color.Blue,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                DrawText(graphics, "属性\r\n几率:", attributeChanceBounds, Color.Blue,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            }
            var randomChanceBounds = new Rectangle(878, 52, 48, 35);
            graphics.FillRectangle(Brushes.White, randomChanceBounds);
            DrawText(graphics, "极品出\r\n现几率:", randomChanceBounds, Color.Blue,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);

            for (var equipmentIndex = 0; equipmentIndex < EquipmentOrder.Length; equipmentIndex++)
                PaintEquipmentRow(graphics, EquipmentOrder[equipmentIndex], equipmentIndex);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            base.OnMouseDown(eventArgs);
            if (eventArgs.Button != MouseButtons.Left) return;
            CommitPendingEdit();

            foreach (var (key, bounds) in new[]
                     {
                         ("屏蔽自动绑定", new Rectangle(18, 14, 300, 28)),
                         ("随机极品", new Rectangle(373, 14, 260, 28)),
                     })
            {
                if (!bounds.Contains(eventArgs.Location)) continue;
                var row = FindMainRow(key);
                if (row is not { IsToggle: true }) return;
                row.BoolValue = !row.BoolValue;
                OnToggle?.Invoke(row.Key, row.BoolValue);
                Invalidate();
                return;
            }

            var cell = _parameterCells.FirstOrDefault(item => item.Bounds.Contains(eventArgs.Location));
            if (cell == null) return;
            _activeCell = cell;
            _editor.Text = cell.Row.TextValue ?? string.Empty;
            _editor.Bounds = Rectangle.Inflate(cell.Bounds, -1, -1);
            _editor.Visible = true;
            _editor.BringToFront();
            _editor.Focus();
            _editor.SelectAll();
        }

        private void PaintEquipmentRow(Graphics graphics, string equipment, int index)
        {
            var labelY = CellY[0][index];
            var labelBounds = new Rectangle(16, labelY, 50, 20);
            graphics.FillRectangle(SystemBrushes.Control, labelBounds);
            ControlPaint.DrawBorder3D(graphics, labelBounds, Border3DStyle.Raised);
            DrawText(graphics, equipment + "类", labelBounds, Color.Blue,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var feature = _features.FirstOrDefault(item => item.MainRow == null &&
                string.Equals(item.DisplayName, equipment + "类", StringComparison.Ordinal));
            if (feature == null) return;
            var parameters = feature.ParameterRows.ToDictionary(item => item.Key, StringComparer.Ordinal);

            for (var group = 0; group < AttributeOrder.Length; group++)
            {
                var attribute = AttributeOrder[group];
                var x = CellX[group];
                var y = CellY[group][index];
                PaintParameter(graphics, parameters, equipment + "最高点数_" + attribute + "_值",
                    new Rectangle(x[0], y, 30, 23));
                PaintParameter(graphics, parameters, equipment + "点数几率_" + attribute + "_值",
                    new Rectangle(x[1], y, 30, 23));
                PaintParameter(graphics, parameters, equipment + "属性几率_" + attribute + "_值",
                    new Rectangle(x[2], y, 30, 23));
                var captionBounds = new Rectangle(x[0] - 3, y + 24, x[2] - x[0] + 33, 18);
                graphics.FillRectangle(Brushes.White, captionBounds);
                DrawText(graphics, EquipmentCaption(equipment, group), captionBounds, Color.Blue,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            PaintParameter(graphics, parameters, equipment + "最随机性_极品_值",
                new Rectangle(878, CellY[4][index], 30, 23));
        }

        private void PaintParameter(Graphics graphics,
            IReadOnlyDictionary<string, TogglePanel.ToggleRow> parameters,
            string key,
            Rectangle bounds)
        {
            if (!parameters.TryGetValue(key, out var row)) return;
            graphics.FillRectangle(Brushes.White, bounds);
            ControlPaint.DrawBorder(graphics, bounds, Color.FromArgb(112, 112, 112),
                ButtonBorderStyle.Solid);
            DrawText(graphics, row.TextValue ?? string.Empty, Rectangle.Inflate(bounds, -3, -2),
                SystemColors.ControlText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            _parameterCells.Add(new ParameterCell(row, bounds));
        }

        private void PaintToggle(Graphics graphics, string key, Rectangle bounds)
        {
            var row = FindMainRow(key);
            if (row == null) return;
            ControlPaint.DrawCheckBox(graphics, new Rectangle(bounds.X, bounds.Y + 2, 13, 13),
                row.BoolValue ? ButtonState.Checked : ButtonState.Normal);
            var status = row.BoolValue ? "已启动" : "未启用";
            DrawText(graphics, $"{DisplayName(key)}({status})",
                new Rectangle(bounds.X + 17, bounds.Y, bounds.Width - 17, bounds.Height),
                SystemColors.ControlText, TextFormatFlags.VerticalCenter);
        }

        private TogglePanel.ToggleRow FindMainRow(string key) => _features
            .Select(feature => feature.MainRow)
            .FirstOrDefault(row => row != null && string.Equals(row.Key, key, StringComparison.Ordinal));

        private void HideEditor()
        {
            _editor.Visible = false;
            _activeCell = null;
            Invalidate();
        }

        private void DrawText(Graphics graphics, string text, Rectangle bounds, Color color,
            TextFormatFlags extraFlags = TextFormatFlags.Left)
        {
            TextRenderer.DrawText(graphics, text, _blueFont, bounds, color,
                extraFlags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        private void DrawSeparatorText(Graphics graphics)
        {
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                        TextFormatFlags.NoPadding;
            TextRenderer.DrawText(graphics, HorizontalSeparatorText, _separatorFont,
                new Rectangle(10, 34, Math.Max(1, ClientSize.Width - 12), 20), Color.Blue, flags);

            foreach (var x in new[] { 209, 380, 550, 711, 871 })
            {
                for (var row = 0; row < 26; row++)
                {
                    TextRenderer.DrawText(graphics, "|", _separatorFont,
                        new Rectangle(x - 1, 60 + row * 13, 8, 13), Color.Blue, flags);
                }
            }
        }

        private static string DisplayName(string key) => key == "随机极品" ? "启用随机极品" : key;

        private static string EquipmentCaption(string equipment, int group) => (equipment, group) switch
        {
            ("衣服", 0) or ("头盔", 0) => "(防御)",
            ("衣服", 1) or ("头盔", 1) => "(魔御)",
            ("项链", 0) => "（魔法躲避\\准确）",
            ("项链", 1) => "（幸运\\敏捷）",
            ("手镯", 0) => "（防御\\准确）",
            ("手镯", 1) => "（魔御\\敏捷）",
            ("戒指", 0) => "（防御\\魔物躲避）",
            ("戒指", 1) => "（魔御\\中毒恢复）",
            (not "武器", 2) => "（攻击）",
            (not "武器", 3) => "（魔法）",
            (not "武器", 4) => "（道术）",
            (_, 0) => "(攻击)",
            (_, 1) => "(魔法)",
            (_, 2) => "(道术)",
            (_, 3) => "(攻速)",
            _ => "(准确)",
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font.Dispose();
                _blueFont.Dispose();
                _separatorFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed record ParameterCell(TogglePanel.ToggleRow Row, Rectangle Bounds);
    }

    internal sealed class BackpackReplicaPanel : Panel, IReplicaConfigEditor
    {
        private readonly CheckBox _enabled;
        private readonly TextBox _extraSlots;
        private readonly TextBox _variableV1;
        private readonly TextBox _variableV2;
        private readonly ComboBox _fixedMode;
        private readonly Dictionary<string, TogglePanel.ToggleRow> _rows = new(StringComparer.Ordinal);
        private bool _updating;

        public event Action<string, bool> OnToggle;
        public event Action SaveRequested;
        public event Action<string, string> OnEditChanged;

        public BackpackReplicaPanel()
        {
            BackColor = SystemColors.Control;
            Font = YanshenUiFont.Create("SimSun", 9f);

            _enabled = new CheckBox
            {
                AutoSize = true,
                Location = new Point(15, 22),
                Text = "无限背包(未启动)",
            };
            _enabled.CheckedChanged += (_, _) =>
            {
                if (_updating || !_rows.TryGetValue("无限背包_是否勾选", out var row) || !row.IsToggle) return;
                row.BoolValue = _enabled.Checked;
                UpdateToggleText();
                OnToggle?.Invoke(row.Key, row.BoolValue);
            };
            Controls.Add(_enabled);

            _extraSlots = AddField("无限背包_额外格子", "额外格子数量：", 52, 49, 131, 108);
            _variableV1 = AddField("无限背包_变量v1", "变量v1：", 408, 49, 87, 108);
            _variableV2 = AddField("无限背包_变量v2", "变量v2：", 88, 96, 95, 108);

            Controls.Add(new Label
            {
                AutoSize = false,
                Bounds = new Rectangle(374, 96, 121, 23),
                Text = "是否固定格子：",
                TextAlign = ContentAlignment.MiddleLeft,
            });
            _fixedMode = new ComboBox
            {
                Bounds = new Rectangle(495, 98, 72, 23),
                DropDownStyle = ComboBoxStyle.DropDown,
            };
            _fixedMode.Items.AddRange(new object[] { "固定格子", "V变量控制格子" });
            _fixedMode.TextChanged += (_, _) => OnValueChanged("无限背包_是否固定", _fixedMode.Text);
            Controls.Add(_fixedMode);

            var saveButton = new ClassicButton
            {
                Bounds = new Rectangle(235, 229, 121, 21),
                Text = "保存本页配置",
                UseVisualStyleBackColor = false,
            };
            saveButton.Click += (_, _) => SaveRequested?.Invoke();
            Controls.Add(saveButton);

            var notes = new RichTextBox
            {
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.FixedSingle,
                Bounds = new Rectangle(0, 257, 641, 124),
                Font = Font,
                ForeColor = Color.Blue,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.ForcedVertical,
            };
            Controls.Add(notes);
            YanshenRichText.SetText(notes,
                "无限背包！\r\n" +
                "1、本功能可以无限制扩展背包数量！\r\n" +
                "2、但是数据作者另外存储！\r\n" +
                "3、正常结束m2额外背包数据会缓存硬盘文件！\r\n" +
                "4、每十分钟会定时缓存到硬盘文件！\r\n" +
                "【功能缺点是：】\r\n" +
                "：一、任务管理器强制结束M2会归档到十分钟前的数据！\r\n" +
                "二、额外背包合区需要单独复制数据，合区工具不支持！\r\n" +
                "三、建议：合区提醒玩家，额外背包数据存入仓库(48格后的物品)。然后合区后手动删除背包文件！\r\n" +
                "额外数据的路径在Gs1\\MyJson\\bags\\角色名字.bin！\r\n" +
                "》》》》特别注意：非大区先使用测试，稳定的开区版本，暂时不建议使用！背包数据理论上无任何上限限制，但是尽量按需设置，太大也是要占用资源的！\r\n" +
                "（以上所有说明都是超过48格以后的数据，48格以前的数据不受任何影响！）\r\n" +
                "【特别注意：所有选项修改需要取消勾选后修改，再勾选保存方可生效，下次启动自动记忆！】");
        }

        public void SetFeatures(IEnumerable<ReplicaFeature> features)
        {
            var feature = features?.FirstOrDefault();
            _updating = true;
            _rows.Clear();
            if (feature?.MainRow != null) _rows[feature.MainRow.Key] = feature.MainRow;
            foreach (var row in feature?.ParameterRows ?? Array.Empty<TogglePanel.ToggleRow>())
                _rows[row.Key] = row;

            _enabled.Checked = _rows.TryGetValue("无限背包_是否勾选", out var enabledRow) &&
                               enabledRow.IsToggle && enabledRow.BoolValue;
            _enabled.Enabled = enabledRow?.IsToggle == true;
            SetEditorValue(_extraSlots, "无限背包_额外格子");
            SetEditorValue(_variableV1, "无限背包_变量v1");
            SetEditorValue(_variableV2, "无限背包_变量v2");
            _fixedMode.Text = _rows.TryGetValue("无限背包_是否固定", out var fixedRow)
                ? fixedRow.TextValue ?? string.Empty
                : string.Empty;
            _fixedMode.Enabled = fixedRow?.CanEdit == true;
            UpdateToggleText();
            _updating = false;
        }

        public void CommitPendingEdit()
        {
        }

        private TextBox AddField(string key, string label, int x, int y,
            int labelWidth, int valueWidth)
        {
            Controls.Add(new Label
            {
                AutoSize = false,
                Bounds = new Rectangle(x, y, labelWidth, 23),
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft,
            });
            var editor = new TextBox
            {
                AutoSize = false,
                BackColor = Color.White,
                Bounds = new Rectangle(x + labelWidth, y, valueWidth, 23),
            };
            editor.TextChanged += (_, _) => OnValueChanged(key, editor.Text);
            Controls.Add(editor);
            return editor;
        }

        private void SetEditorValue(TextBox editor, string key)
        {
            if (_rows.TryGetValue(key, out var row))
            {
                editor.Text = row.TextValue ?? string.Empty;
                editor.ReadOnly = !row.CanEdit;
                editor.Enabled = true;
            }
            else
            {
                editor.Text = string.Empty;
                editor.ReadOnly = true;
                editor.Enabled = false;
            }
        }

        private void OnValueChanged(string key, string text)
        {
            if (_updating || !_rows.TryGetValue(key, out var row) || !row.CanEdit) return;
            row.TextValue = text;
            OnEditChanged?.Invoke(key, text);
        }

        private void UpdateToggleText()
        {
            _enabled.Text = $"无限背包({(_enabled.Checked ? "已启动" : "未启动")})";
        }
    }
}
