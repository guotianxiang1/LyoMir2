namespace GameSvr.Plugins
{
    internal abstract class Config12ReplicaPanelBase : Panel, IReplicaConfigEditor
    {
        private readonly Font _bodyFont;
        private readonly Font _drawFont;
        private readonly TextBox _editor;
        private readonly IReadOnlyList<ToggleSlot> _toggleSlots;
        private readonly IReadOnlyList<ParameterSlot> _parameterSlots;
        private readonly HashSet<string> _mappedKeys;
        private Dictionary<string, ReplicaFeature> _features = new(StringComparer.Ordinal);
        private Dictionary<string, TogglePanel.ToggleRow> _parameters = new(StringComparer.Ordinal);
        private ParameterSlot _activeParameter;
        private bool _cancelEdit;

        public event Action<string, bool> OnToggle;
        public event Action<string, string> OnEditChanged;

        public IReadOnlyList<string> UnboundKeys { get; private set; } = Array.Empty<string>();

        protected Config12ReplicaPanelBase(
            IReadOnlyList<ToggleSlot> toggleSlots,
            IReadOnlyList<ParameterSlot> parameterSlots)
        {
            _bodyFont = YanshenUiFont.Create("Tahoma", 8.25f);
            _drawFont = YanshenUiFont.CreateTextRenderer("Tahoma", 8.25f, DeviceDpi);
            _toggleSlots = toggleSlots;
            _parameterSlots = parameterSlots;
            _mappedKeys = toggleSlots.Select(slot => slot.Key)
                .Concat(parameterSlots.Select(slot => slot.Key))
                .ToHashSet(StringComparer.Ordinal);

            BackColor = SystemColors.Control;
            BorderStyle = BorderStyle.FixedSingle;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            _editor = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Font = _bodyFont,
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
            var materialized = features?.ToArray() ?? Array.Empty<ReplicaFeature>();
            _features = materialized
                .Where(feature => feature.MainRow != null)
                .GroupBy(feature => feature.MainRow.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _parameters = materialized
                .SelectMany(feature => feature.ParameterRows)
                .GroupBy(row => row.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var slot in _toggleSlots)
            {
                if (slot.AllowNonBinaryNumber &&
                    _features.TryGetValue(slot.Key, out var feature) &&
                    feature.MainRow != null &&
                    TryGetNonZeroNumber(feature.MainRow.OriginalValue, out var enabled))
                    feature.MainRow.BoolValue = enabled;
            }
            UnboundKeys = materialized.SelectMany(feature => feature.Keys)
                .Where(key => !_mappedKeys.Contains(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            Invalidate();
        }

        public void CommitPendingEdit()
        {
            if (_activeParameter == null || !_editor.Visible ||
                !_parameters.TryGetValue(_activeParameter.Key, out var row)) return;
            row.TextValue = _editor.Text;
            OnEditChanged?.Invoke(row.Key, row.TextValue);
            HideEditor();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            foreach (var slot in _toggleSlots) PaintToggle(eventArgs.Graphics, slot);
            PaintPage(eventArgs.Graphics);
            foreach (var slot in _parameterSlots) PaintParameter(eventArgs.Graphics, slot);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            base.OnMouseDown(eventArgs);
            if (eventArgs.Button != MouseButtons.Left) return;
            CommitPendingEdit();

            foreach (var slot in _toggleSlots)
            {
                if (!slot.IsEnabled ||
                    !slot.HitBounds.Contains(eventArgs.Location) ||
                    !_features.TryGetValue(slot.Key, out var feature) ||
                    feature.MainRow is not { } row ||
                    !CanToggle(slot, row)) continue;
                row.BoolValue = !row.BoolValue;
                if (row.IsToggle)
                {
                    OnToggle?.Invoke(row.Key, row.BoolValue);
                }
                else
                {
                    row.TextValue = row.BoolValue ? "1" : "0";
                    OnEditChanged?.Invoke(row.Key, row.TextValue);
                }
                Invalidate(slot.HitBounds);
                return;
            }

            var parameter = _parameterSlots.FirstOrDefault(slot => slot.Bounds.Contains(eventArgs.Location));
            if (parameter != null && _parameters.ContainsKey(parameter.Key)) BeginEdit(parameter);
        }

        protected virtual void PaintPage(Graphics graphics)
        {
        }

        protected void DrawText(
            Graphics graphics,
            string text,
            Rectangle bounds,
            Color color,
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
        {
            TextRenderer.DrawText(graphics, text, _drawFont, bounds, color,
                flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        private void PaintToggle(Graphics graphics, ToggleSlot slot)
        {
            if (!_features.TryGetValue(slot.Key, out var feature) || feature.MainRow == null) return;
            var row = feature.MainRow;
            var checkState = row.BoolValue ? ButtonState.Checked : ButtonState.Normal;
            if (!slot.IsEnabled) checkState |= ButtonState.Inactive;
            ControlPaint.DrawCheckBox(graphics, slot.CheckBounds, checkState);
            var caption = slot.Caption ?? feature.DisplayName;
            var status = row.BoolValue ? "已启动" : "未启动";
            DrawText(graphics, $"{caption}({status})", slot.TextBounds,
                slot.IsEnabled ? SystemColors.ControlText : SystemColors.GrayText);
        }

        private static bool CanToggle(ToggleSlot slot, TogglePanel.ToggleRow row) =>
            row.IsToggle || slot.AllowNonBinaryNumber &&
            TryGetNonZeroNumber(row.OriginalValue, out _);

        private static bool TryGetNonZeroNumber(object value, out bool enabled)
        {
            switch (value)
            {
                case byte number: enabled = number != 0; return true;
                case sbyte number: enabled = number != 0; return true;
                case short number: enabled = number != 0; return true;
                case ushort number: enabled = number != 0; return true;
                case int number: enabled = number != 0; return true;
                case uint number: enabled = number != 0; return true;
                case long number: enabled = number != 0; return true;
                case ulong number: enabled = number != 0; return true;
                case float number: enabled = number != 0; return true;
                case double number: enabled = number != 0; return true;
                case decimal number: enabled = number != 0; return true;
                default: enabled = false; return false;
            }
        }

        private void PaintParameter(Graphics graphics, ParameterSlot slot)
        {
            graphics.FillRectangle(Brushes.White, slot.Bounds);
            ControlPaint.DrawBorder3D(graphics, slot.Bounds, Border3DStyle.Sunken);
            if (!_parameters.TryGetValue(slot.Key, out var row)) return;
            var textBounds = Rectangle.Inflate(slot.Bounds, -4, -2);
            textBounds.Offset(-1, -3);
            DrawText(graphics, row.TextValue ?? string.Empty, textBounds, SystemColors.ControlText);
        }

        private void BeginEdit(ParameterSlot parameter)
        {
            _activeParameter = parameter;
            _editor.Text = _parameters[parameter.Key].TextValue ?? string.Empty;
            _editor.Bounds = parameter.Bounds;
            _editor.Visible = true;
            _editor.BringToFront();
            _editor.Focus();
            _editor.SelectAll();
        }

        private void HideEditor()
        {
            _editor.Visible = false;
            _activeParameter = null;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bodyFont.Dispose();
                _drawFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected sealed class ToggleSlot
        {
            public ToggleSlot(
                string key,
                int x,
                int y,
                int width,
                string caption = null,
                bool isEnabled = true,
                bool allowNonBinaryNumber = false)
            {
                Key = key;
                Caption = caption;
                IsEnabled = isEnabled;
                AllowNonBinaryNumber = allowNonBinaryNumber;
                CheckBounds = new Rectangle(x - 1, y - 1, 14, 14);
                TextBounds = new Rectangle(x + 15, y - 4, Math.Max(1, width - 15), 19);
                HitBounds = new Rectangle(x - 1, y - 3, Math.Max(1, width - 1), 21);
            }

            public string Key { get; }
            public string Caption { get; }
            public bool IsEnabled { get; }
            public bool AllowNonBinaryNumber { get; }
            public Rectangle CheckBounds { get; }
            public Rectangle TextBounds { get; }
            public Rectangle HitBounds { get; }
        }

        protected sealed record ParameterSlot(string Key, Rectangle Bounds);
    }

    internal sealed class Config1ReplicaPanel : Config12ReplicaPanelBase
    {
        private static readonly IReadOnlyList<ToggleSlot> ToggleSlots = new[]
        {
            Slot("全屏拾取", 25, 26, 141),
            Slot("刀刀切割", 165, 26, 141),
            Slot("永久属性", 306, 26, 135),
            Slot("特殊属性", 441, 26, 139),
            Slot("复活触发脚本", 580, 26, 245),

            Slot("被击杀触发", 25, 52, 141),
            Slot("移动速度", 165, 52, 141),
            Slot("攻击反伤", 306, 52, 135),
            Slot("捡物触发", 441, 52, 139),
            Slot("复活戒指改cd", 580, 52, 245),

            Slot("攻击触发", 25, 80, 141),
            Slot("魔法攻击触发", 165, 80, 141),
            Slot("新穿戴触发", 306, 80, 190),
            Slot("复活戒指概率", 580, 80, 245),

            Slot("禁止装备自动绑定", 25, 109, 155),
            Slot("新倍攻和暴击", 216, 109, 174),
            Slot("give极品", 390, 109, 190),
            Slot("麻痹概率", 580, 109, 245),

            Slot("AddLimLF函数修改", 25, 135, 194),
            Slot("IncActivePoint函数修改", 25, 161, 194),

            Slot("英雄穿戴触发", 25, 189, 155),
            Slot("英雄攻速移速", 180, 189, 165),
            Slot("BB杀怪触发", 345, 189, 142),
            new ToggleSlot("临时大背包", 487, 189, 144, isEnabled: false),
            Slot("英雄倍攻和暴击", 631, 189, 244),

            Slot("BB死亡触发", 25, 218, 155),
            Slot("特殊宝宝", 180, 218, 159),

            Slot("英雄施法速度", 25, 247, 155),
            Slot("读取英雄装备", 180, 247, 165),
            Slot("装备来源", 345, 247, 142),
            Slot("千分比免伤", 487, 247, 144),

            Slot("永久攻速", 631, 275, 244),
            Slot("上线触发", 25, 295, 132),
        };

        public Config1ReplicaPanel()
            : base(ToggleSlots, Array.Empty<ParameterSlot>())
        {
        }

        protected override void PaintPage(Graphics graphics)
        {
            DrawNoteLine(graphics,
                "：AddLimLF函数修改后，支持宝宝给与高级属性和物品给与极品属性,给与值小于65535执行原版函数",
                new Rectangle(219, 136, 417, 13));
            DrawNoteLine(graphics,
                "：IncActivePointcode函数修改后，支持杀死宝宝和获取身上装备的极品值",
                new Rectangle(219, 157, 552, 16));
            DrawNoteLine(graphics,
                "：启动后①宝宝可以设置跟随主号攻击;②当数据库宝宝经验为1314.宝宝不攻击人。",
                new Rectangle(339, 217, 473, 20));
            DrawDisabledPlaceholder(graphics);
            graphics.FillRectangle(Brushes.White, new Rectangle(159, 276, 459, 49));
            DrawText(graphics,
                "：此函数启动后，可以在runrequest.pas中加入上线触发函数，新手无需启动此模式。需",
                new Rectangle(158, 275, 459, 13), Color.Red,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(graphics,
                "对战神引擎特别熟悉才敢用，不然反而适得其反，没发挥功效，反而增加了疑问。（此",
                new Rectangle(158, 288, 459, 13), Color.Red,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
            DrawText(graphics, "函数目的是替换邮件上线触发的）",
                new Rectangle(158, 301, 459, 13), Color.Red,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        private void DrawNoteLine(Graphics graphics, string text, Rectangle bounds)
        {
            graphics.FillRectangle(Brushes.White, bounds);
            var textBounds = bounds;
            textBounds.Offset(-1, -1);
            DrawText(graphics, text, textBounds, Color.Red,
                TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        private void DrawDisabledPlaceholder(Graphics graphics)
        {
            var checkBounds = new Rectangle(630, 245, 14, 14);
            ControlPaint.DrawCheckBox(graphics, checkBounds, ButtonState.Inactive);
            DrawText(graphics, "打怪暴率(改用新版)", new Rectangle(647, 243, 210, 19),
                SystemColors.GrayText);
        }

        private static ToggleSlot Slot(string key, int x, int y, int width) =>
            new(key, x, y, width);
    }

    internal sealed class Config2ReplicaPanel : Config12ReplicaPanelBase
    {
        private static readonly IReadOnlyList<ToggleSlot> ToggleSlots = new[]
        {
            Slot("地狱雷光系数", 28, 30),
            Slot("地狱雷光范围", 214, 30),
            Slot("爆裂火焰可换主属性", 399, 30),
            Slot("爆裂火焰范围及系数", 592, 30),

            Slot("地狱雷光可换主属性", 28, 56),
            Slot("激光电影可换主属性", 214, 56),
            Slot("激光范围及系数", 399, 56),
            Slot("激光命中概率", 592, 56),

            Slot("火球主属性切换", 28, 83),
            Slot("火球自定义范围", 214, 83),
            Slot("雷电主属性切换", 399, 83),
            Slot("雷电自定义范围", 592, 83),

            Slot("冰咆哮主属性切换", 28, 111),
            Slot("冰咆哮范围", 214, 111),
            Slot("火雨主属切换", 399, 111),
            Slot("魔法盾修正", 592, 111),

            Slot("嗜血术倍数", 28, 137),
            Slot("免毒符", 214, 137),
            Slot("野蛮等级", 399, 137),
            Slot("禁止发言不提示", 592, 137),

            Slot("中毒飘血", 28, 165),
            Slot("删除技能不提示", 214, 165),
            Slot("升级技能不提示", 399, 165),

            Slot("群毒", 28, 197, 110),
            new ToggleSlot("群毒值", 681, 197, 220, "自定义群毒公式",
                allowNonBinaryNumber: true),
        };

        private static readonly IReadOnlyList<ParameterSlot> ParameterSlots = new[]
        {
            new ParameterSlot("绿毒_A", new Rectangle(573, 220, 59, 22)),
            new ParameterSlot("绿毒_B", new Rectangle(667, 220, 59, 22)),
            new ParameterSlot("绿毒_最低", new Rectangle(770, 220, 59, 22)),
            new ParameterSlot("红毒_A", new Rectangle(573, 251, 59, 22)),
            new ParameterSlot("红毒_B", new Rectangle(667, 251, 59, 22)),
            new ParameterSlot("双毒时间_最低", new Rectangle(770, 251, 59, 22)),
        };

        public Config2ReplicaPanel()
            : base(ToggleSlots, ParameterSlots)
        {
        }

        protected override void PaintPage(Graphics graphics)
        {
            DrawText(graphics,
                "：以下两个功能需要有眼神的key才能配合【群毒】生效;注意:有关群毒s变量和AB公式叠加",
                new Rectangle(137, 192, 538, 22), SystemColors.ControlText);

            DrawText(graphics,
                "群毒:绿毒伤害=系数1*道术/系数B,默认A=1，B=10，也就是每10点道术增加1点伤害，最低5点",
                new Rectangle(21, 219, 530, 23), SystemColors.ControlText);
            DrawText(graphics, "A:", new Rectangle(551, 219, 21, 23), SystemColors.ControlText);
            DrawText(graphics, "B:", new Rectangle(647, 219, 19, 23), SystemColors.ControlText);
            DrawText(graphics, "最低:", new Rectangle(740, 219, 29, 23), SystemColors.ControlText);

            DrawText(graphics,
                "群毒:双毒时间计算公式=系数A*道术/系数B,默认A=1，B=1，也就是每1点道术增加1秒，最低15秒",
                new Rectangle(21, 250, 530, 23), SystemColors.ControlText);
            DrawText(graphics, "A:", new Rectangle(551, 250, 21, 23), SystemColors.ControlText);
            DrawText(graphics, "B:", new Rectangle(647, 250, 19, 23), SystemColors.ControlText);
            DrawText(graphics, "最低:", new Rectangle(740, 250, 29, 23), SystemColors.ControlText);
        }

        private static ToggleSlot Slot(string key, int x, int y) => new(key, x, y, 186);
        private static ToggleSlot Slot(string key, int x, int y, int width) => new(key, x, y, width);
    }
}
