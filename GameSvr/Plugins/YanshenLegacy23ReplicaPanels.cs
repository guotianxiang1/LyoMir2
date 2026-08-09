namespace GameSvr.Plugins
{
    /// <summary>
    /// Shared binding behavior for the two screenshot-backed fixed-layout legacy pages.
    /// Page-specific controls stay at their original 936 x 423 logical coordinates.
    /// </summary>
    internal abstract class LegacyFixedReplicaPanel : Panel, IReplicaConfigEditor
    {
        private readonly Font _bodyFont = YanshenUiFont.Create("SimSun", 9f);
        private readonly Dictionary<string, ToggleView> _toggleViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Control> _parameterViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TogglePanel.ToggleRow> _boundRows = new(StringComparer.Ordinal);
        private readonly List<Control> _fallbackControls = new();
        private readonly Dictionary<string, Control> _fallbackParameters = new(StringComparer.Ordinal);
        private bool _updating;

        public event Action<string, bool> OnToggle;
        public event Action<string, string> OnEditChanged;

        /// <summary>Keys bound below the screenshot-backed area because no fixed slot was declared.</summary>
        public IReadOnlyList<string> FixedLayoutMisses { get; private set; } = Array.Empty<string>();

        protected LegacyFixedReplicaPanel()
        {
            AutoScroll = false;
            BackColor = SystemColors.Control;
            BorderStyle = BorderStyle.FixedSingle;
            DoubleBuffered = true;
            Font = _bodyFont;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        public void SetFeatures(IEnumerable<ReplicaFeature> features)
        {
            CommitPendingEdit();
            _updating = true;
            try
            {
                _boundRows.Clear();
                foreach (var feature in features ?? Array.Empty<ReplicaFeature>())
                {
                    if (feature.MainRow != null) _boundRows[feature.MainRow.Key] = feature.MainRow;
                    foreach (var row in feature.ParameterRows) _boundRows[row.Key] = row;
                }

                foreach (var (key, view) in _toggleViews)
                {
                    var bound = _boundRows.TryGetValue(key, out var row) && row.IsToggle;
                    view.Control.AutoCheck = bound;
                    view.Control.Checked = bound && row.BoolValue;
                    UpdateToggleText(view);
                }

                foreach (var (key, control) in _parameterViews)
                {
                    var bound = _boundRows.TryGetValue(key, out var row) && !row.IsToggle;
                    control.Enabled = bound && row.CanEdit;
                    control.Text = bound ? row.TextValue ?? string.Empty : string.Empty;
                }

                RebuildFallbackControls();
            }
            finally
            {
                _updating = false;
            }
        }

        public void CommitPendingEdit()
        {
            if (_updating) return;
            foreach (var (key, control) in _parameterViews) CommitParameter(key, control);
            foreach (var (key, control) in _fallbackParameters) CommitParameter(key, control);
        }

        protected void AddToggle(
            string key,
            Rectangle bounds,
            string caption = null,
            string enabledStatus = "已启动",
            string disabledStatus = "未启用")
        {
            var control = new CheckBox
            {
                AutoCheck = false,
                AutoEllipsis = false,
                AutoSize = true,
                BackColor = SystemColors.Control,
                Bounds = bounds,
                Font = _bodyFont,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false,
            };
            var view = new ToggleView(control, caption ?? key, enabledStatus, disabledStatus);
            control.CheckedChanged += (_, _) => ToggleChanged(key, view);
            _toggleViews.Add(key, view);
            Controls.Add(control);
            UpdateToggleText(view);
        }

        protected void AddParameter(string key, Rectangle bounds, bool useDropDown = false)
        {
            Control control;
            if (useDropDown)
            {
                control = new ComboBox
                {
                    Bounds = bounds,
                    DropDownStyle = ComboBoxStyle.DropDown,
                    FlatStyle = FlatStyle.Standard,
                    Font = _bodyFont,
                    IntegralHeight = false,
                };
            }
            else
            {
                control = new TextBox
                {
                    AutoSize = false,
                    BorderStyle = BorderStyle.FixedSingle,
                    Bounds = bounds,
                    Font = _bodyFont,
                };
            }

            control.Enabled = false;
            control.KeyDown += (_, eventArgs) =>
            {
                if (eventArgs.KeyCode != Keys.Enter) return;
                CommitParameter(key, control);
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
            };
            control.LostFocus += (_, _) => CommitParameter(key, control);
            _parameterViews.Add(key, control);
            Controls.Add(control);
        }

        protected Label AddNote(
            string text,
            Rectangle bounds,
            Color color,
            bool whiteBackground = true,
            BorderStyle borderStyle = BorderStyle.None,
            ContentAlignment alignment = ContentAlignment.MiddleLeft)
        {
            var label = bounds.Height <= 24 ? new YanshenSingleLineLabel() : new Label();
            label.AutoEllipsis = false;
            label.BackColor = whiteBackground ? Color.White : SystemColors.Control;
            label.BorderStyle = borderStyle;
            label.Bounds = bounds;
            label.Font = _bodyFont;
            label.ForeColor = color;
            label.Text = text;
            label.TextAlign = alignment;
            label.UseMnemonic = false;
            Controls.Add(label);
            return label;
        }

        protected void AddCaption(
            string text,
            Rectangle bounds,
            Color? color = null,
            ContentAlignment alignment = ContentAlignment.MiddleLeft)
        {
            AddNote(text, bounds, color ?? SystemColors.ControlText, true, BorderStyle.None, alignment);
        }

        protected void AddStaticToggle(string caption, Rectangle bounds)
        {
            Controls.Add(new CheckBox
            {
                AccessibleDescription = "2.07静态占位，无配置开关",
                AutoCheck = false,
                AutoSize = true,
                BackColor = SystemColors.Control,
                Bounds = bounds,
                Font = _bodyFont,
                Tag = "Yanshen207StaticPlaceholder",
                TabStop = false,
                Text = caption,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false,
            });
        }

        protected Label AddWrappedNote(string text, Rectangle bounds, Color color)
        {
            var label = new YanshenWrappedTextLabel
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Bounds = bounds,
                Font = _bodyFont,
                ForeColor = color,
                Text = text,
                TextAlign = ContentAlignment.TopLeft,
                UseMnemonic = false,
            };
            Controls.Add(label);
            return label;
        }

        protected Button AddClassicButton(string text, Rectangle bounds, Color? color = null)
        {
            var button = new YanshenSingleLineButton
            {
                Bounds = bounds,
                Font = _bodyFont,
                ForeColor = color ?? SystemColors.ControlText,
                Text = text,
                UseVisualStyleBackColor = false,
            };
            Controls.Add(button);
            return button;
        }

        private void ToggleChanged(string key, ToggleView view)
        {
            if (_updating) return;
            if (!_boundRows.TryGetValue(key, out var row) || !row.IsToggle) return;
            row.BoolValue = view.Control.Checked;
            UpdateToggleText(view);
            OnToggle?.Invoke(row.Key, row.BoolValue);
        }

        private void UpdateToggleText(ToggleView view)
        {
            var status = view.Control.Checked ? view.EnabledStatus : view.DisabledStatus;
            view.Control.Text = $"{view.Caption}({status})";
        }

        private void CommitParameter(string key, Control control)
        {
            if (_updating || !_boundRows.TryGetValue(key, out var row) || row.IsToggle || !row.CanEdit) return;
            var text = control.Text ?? string.Empty;
            if (string.Equals(row.TextValue ?? string.Empty, text, StringComparison.Ordinal)) return;
            row.TextValue = text;
            OnEditChanged?.Invoke(row.Key, text);
        }

        private void RebuildFallbackControls()
        {
            foreach (var control in _fallbackControls)
            {
                Controls.Remove(control);
                control.Dispose();
            }
            _fallbackControls.Clear();
            _fallbackParameters.Clear();

            var misses = _boundRows
                .Where(pair => pair.Value.IsToggle
                    ? !_toggleViews.ContainsKey(pair.Key)
                    : !_parameterViews.ContainsKey(pair.Key))
                .Select(pair => pair.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            FixedLayoutMisses = misses;
            AutoScroll = misses.Length > 0;

            var y = 432;
            foreach (var key in misses)
            {
                var row = _boundRows[key];
                if (row.IsToggle)
                {
                    var checkBox = new CheckBox
                    {
                        AutoSize = false,
                        Bounds = new Rectangle(12, y, 440, 23),
                        Checked = row.BoolValue,
                        Font = _bodyFont,
                        Text = $"{key}({(row.BoolValue ? "已启动" : "未启用")})",
                        UseVisualStyleBackColor = false,
                    };
                    checkBox.CheckedChanged += (_, _) =>
                    {
                        if (_updating) return;
                        row.BoolValue = checkBox.Checked;
                        checkBox.Text = $"{key}({(row.BoolValue ? "已启动" : "未启用")})";
                        OnToggle?.Invoke(row.Key, row.BoolValue);
                    };
                    AddFallbackControl(checkBox);
                }
                else
                {
                    var caption = new Label
                    {
                        Bounds = new Rectangle(12, y, 210, 23),
                        Font = _bodyFont,
                        Text = key + "：",
                        TextAlign = ContentAlignment.MiddleRight,
                    };
                    var editor = new TextBox
                    {
                        Bounds = new Rectangle(226, y, 220, 23),
                        Enabled = row.CanEdit,
                        Font = _bodyFont,
                        Text = row.TextValue ?? string.Empty,
                    };
                    editor.KeyDown += (_, eventArgs) =>
                    {
                        if (eventArgs.KeyCode != Keys.Enter) return;
                        CommitParameter(key, editor);
                        eventArgs.Handled = true;
                        eventArgs.SuppressKeyPress = true;
                    };
                    editor.LostFocus += (_, _) => CommitParameter(key, editor);
                    _fallbackParameters[key] = editor;
                    AddFallbackControl(caption);
                    AddFallbackControl(editor);
                }
                y += 27;
            }

            AutoScrollMinSize = misses.Length == 0 ? Size.Empty : new Size(0, y + 8);
        }

        private void AddFallbackControl(Control control)
        {
            _fallbackControls.Add(control);
            Controls.Add(control);
            control.BringToFront();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed record ToggleView(
            CheckBox Control,
            string Caption,
            string EnabledStatus,
            string DisabledStatus);

    }

    /// <summary>Fixed-coordinate replica of the original 2.0.7/2.0.8 “盘古2” page.</summary>
    internal sealed class Legacy2ReplicaPanel : LegacyFixedReplicaPanel, IReplicaConfigEditor
    {
        private static readonly Color NoteBlue = Color.Blue;

        public Legacy2ReplicaPanel()
        {
            SuspendLayout();

            AddToggle("武器绿毒", new Rectangle(8, 18, 143, 22));
            AddToggle("噬魂沼泽绿毒修复", new Rectangle(152, 18, 188, 22));
            AddToggle("物功带毒", new Rectangle(338, 21, 145, 22));
            AddToggle("法师群毒", new Rectangle(477, 21, 128, 22));
            AddToggle("雷电带毒", new Rectangle(610, 21, 133, 22));
            AddToggle("半月带毒", new Rectangle(744, 21, 158, 22));

            AddToggle("SetNoKillMapLv脚本触发", new Rectangle(8, 45, 196, 22));
            AddNote("\u3000：修改了盘古算法，现在仅仅屏蔽了5级GM的提示，然后触发脚本，gm权限没做任何修改,最大id支持21亿",
                new Rectangle(191, 49, 609, 13), NoteBlue);

            AddToggle("攻城修改", new Rectangle(9, 73, 137, 22));
            AddNote("申请攻城天数：", new Rectangle(143, 76, 83, 13), NoteBlue);
            AddParameter("攻城修改_天数", new Rectangle(232, 73, 53, 23));
            AddNote("攻城开始时间：", new Rectangle(302, 76, 83, 13), NoteBlue);
            AddParameter("攻城修改_小时", new Rectangle(393, 73, 41, 23));
            AddCaption("时", new Rectangle(440, 76, 17, 13), NoteBlue);
            AddParameter("攻城修改_分钟", new Rectangle(461, 73, 41, 23));
            AddCaption("分", new Rectangle(511, 76, 17, 13), NoteBlue);
            AddNote("攻城时长：", new Rectangle(564, 76, 59, 13), NoteBlue);
            AddParameter("攻城时长_分钟", new Rectangle(624, 73, 41, 23));
            AddCaption("分钟", new Rectangle(677, 76, 32, 13), NoteBlue);

            AddToggle("复活戒指重设", new Rectangle(9, 102, 143, 22));
            AddNote("重设时间：", new Rectangle(156, 106, 57, 13), NoteBlue);
            AddParameter("复活戒指重设_重设时间", new Rectangle(219, 102, 41, 23));
            AddCaption("秒", new Rectangle(265, 106, 23, 13), NoteBlue);
            AddNote("重设复活后无敌时间：", new Rectangle(292, 106, 113, 13), NoteBlue);
            AddParameter("复活戒指重设_无敌时间", new Rectangle(416, 101, 41, 23));
            AddCaption("秒", new Rectangle(463, 106, 23, 13), NoteBlue);
            AddNote("特别注意:启动眼神的复活改cd后，仅重设时间无效；每次修改数值请取消勾选后操作",
                new Rectangle(492, 106, 439, 13), NoteBlue);

            AddToggle("破复活", new Rectangle(9, 130, 142, 22));
            AddNote("破复活需要通过脚本设置破复活属性，使用方法详见盘古的测试NPC-3.pas",
                new Rectangle(123, 133, 413, 13), NoteBlue);
            AddToggle("设置玩家称号函数", new Rectangle(546, 129, 241, 22), "设置玩家称号函数_支持80字符");
            AddToggle("名字变色", new Rectangle(788, 130, 139, 22));

            AddToggle("火墙设置时间上限", new Rectangle(9, 156, 171, 22));
            AddParameter("火墙_时间", new Rectangle(180, 156, 41, 23));
            AddCaption("秒", new Rectangle(226, 160, 18, 13), NoteBlue);
            AddToggle("基本剑术", new Rectangle(257, 156, 133, 22));
            AddNote("技能每级增加准确(公式:Levle*n)", new Rectangle(389, 159, 186, 13), NoteBlue);
            AddParameter("基本剑术_n值", new Rectangle(575, 154, 72, 23), true);
            AddNote("依然使用盘古的：\r\nThis_Player.SetS(1,2,color);\r\n设置名字颜色",
                new Rectangle(789, 152, 135, 41), NoteBlue);

            AddToggle("盘古爆裂火焰范围", new Rectangle(9, 183, 171, 22), null, "已重设");
            AddParameter("盘古爆裂火焰范围_范围值", new Rectangle(180, 189, 41, 23));
            AddToggle("攻杀剑术", new Rectangle(255, 182, 133, 22));
            AddNote("默认A=5,每级加一点攻击,(Level+A),上限255  A:", new Rectangle(389, 187, 248, 13), NoteBlue);
            AddParameter("攻杀剑术_A值", new Rectangle(648, 182, 41, 23));
            AddToggle("ServerSay函数", new Rectangle(788, 208, 145, 22));

            AddToggle("盘古地狱雷光范围", new Rectangle(8, 210, 171, 22));
            AddParameter("盘古地狱雷光范围_范围值", new Rectangle(180, 215, 41, 23));
            AddToggle("刺杀剑术", new Rectangle(255, 210, 133, 22));
            AddNote("默认系数A=2,B=5,伤害倍数=(A+Level)/B", new Rectangle(389, 215, 221, 13), NoteBlue);
            AddCaption("A:", new Rectangle(621, 218, 18, 13), NoteBlue);
            AddParameter("刺杀剑术_A值", new Rectangle(648, 213, 41, 23));
            AddCaption("B:", new Rectangle(702, 217, 18, 13), NoteBlue);
            AddParameter("刺杀剑术_B值", new Rectangle(728, 213, 41, 23));
            AddNote("ServerSay函数勾选后\r\n可以自定义文字颜色和底\r\n色。",
                new Rectangle(786, 239, 128, 46), NoteBlue);

            AddToggle("盘古冰咆哮的范围", new Rectangle(8, 238, 171, 22));
            AddParameter("盘古冰咆哮的范围_范围值", new Rectangle(180, 241, 41, 23));
            AddToggle("半月弯刀", new Rectangle(255, 238, 133, 22));
            AddNote("默认系数A=2,B=15,伤害倍数=(A+Level)/B", new Rectangle(389, 241, 221, 13), NoteBlue);
            AddCaption("A:", new Rectangle(621, 248, 18, 13), NoteBlue);
            AddParameter("半月弯刀_A值", new Rectangle(648, 245, 41, 23));
            AddCaption("B:", new Rectangle(702, 248, 18, 13), NoteBlue);
            AddParameter("半月弯刀_B值", new Rectangle(728, 244, 41, 23));

            AddToggle("盘古流星火雨范围", new Rectangle(8, 266, 171, 22));
            AddParameter("盘古流星火雨范围_范围值", new Rectangle(180, 268, 41, 23));
            AddToggle("烈火剑法", new Rectangle(255, 266, 133, 22));
            AddWrappedNote("默认系数A=4,B=4,伤害倍数\n" +
                           "=(A*Level+B)/10+1(最高25.5)",
                new Rectangle(390, 266, 203, 28), NoteBlue);
            AddCaption("A:", new Rectangle(621, 283, 18, 13), NoteBlue);
            AddParameter("烈火剑法_A值", new Rectangle(648, 280, 41, 23), true);
            AddCaption("B:", new Rectangle(702, 285, 18, 13), NoteBlue);
            AddParameter("烈火剑法_B值", new Rectangle(728, 280, 41, 23));

            AddWrappedNote("以上技能范围修改禁止和眼神同类型技能范\n" +
                           "围修改同时启动，同时启动会报错；每次修\n" +
                           "改值请先取消勾选；最大支持255",
                new Rectangle(8, 296, 233, 44), NoteBlue);
            AddToggle("逐日剑法", new Rectangle(257, 298, 133, 22));
            AddWrappedNote("默认系数A=7,B=6,伤害倍数\n" +
                           "=(A+Level)/B(A+Level)不可超过255",
                new Rectangle(389, 304, 203, 29), NoteBlue);
            AddCaption("A:", new Rectangle(621, 318, 18, 13), NoteBlue);
            AddParameter("逐日剑法_A值", new Rectangle(648, 314, 41, 23));
            AddCaption("B:", new Rectangle(702, 318, 18, 13), NoteBlue);
            AddParameter("逐日剑法_B值", new Rectangle(728, 314, 41, 23));
            AddToggle("删除英雄技能", new Rectangle(789, 290, 145, 22));
            AddNote("与旧版盘古用法一样", new Rectangle(792, 317, 128, 18), NoteBlue);

            AddToggle("野蛮麻痹", new Rectangle(257, 335, 150, 22));
            AddToggle("等级禁言", new Rectangle(8, 356, 143, 22));
            AddNote("和老盘古的等级禁言使用方法一样，This_Player.SetS(1,1,7)是禁言，This_Player.SetS(1,1,8)是解除禁言",
                new Rectangle(152, 359, 548, 16), NoteBlue);

            ResumeLayout(false);
        }
    }

    /// <summary>Fixed-coordinate replica of the original 2.0.7/2.0.8 “盘古3” page.</summary>
    internal sealed class Legacy3ReplicaPanel : LegacyFixedReplicaPanel, IReplicaConfigEditor
    {
        private static readonly Color NoteGreen = Color.FromArgb(48, 128, 20);
        private readonly Font _formulaFont = YanshenUiFont.Create("Microsoft Sans Serif", 9f);

        public Legacy3ReplicaPanel()
        {
            SuspendLayout();

            AddToggle("无极真气", new Rectangle(38, 24, 130, 22), null, "已重设");
            AddNote("增加道术公式：SC*(Skilllv+2)*2/A 默认A=10,A越大，道术加成越小 A:",
                new Rectangle(168, 26, 365, 13), NoteGreen).Font = _formulaFont;
            AddParameter("无极真气_A值", new Rectangle(543, 24, 60, 23));
            AddNote("真气持续时间(默认6秒)", new Rectangle(612, 26, 123, 13), NoteGreen);
            AddParameter("无极真气_时间", new Rectangle(744, 24, 60, 23));
            AddCaption("秒", new Rectangle(812, 26, 14, 13), NoteGreen);

            AddToggle("施毒术", new Rectangle(38, 52, 130, 22), null, "已重设");
            AddNote("道术增加绿毒掉血伤害 每:", new Rectangle(167, 54, 149, 13), NoteGreen);
            AddParameter("施毒术_公式值", new Rectangle(324, 50, 60, 23));
            AddNote("点道术,多掉1点hp", new Rectangle(392, 55, 96, 13), NoteGreen);
            AddToggle("中毒时间上限", new Rectangle(524, 57, 177, 22));
            AddNote("时间上限(秒)", new Rectangle(671, 59, 69, 13), NoteGreen);
            AddParameter("中毒时间上限_秒", new Rectangle(744, 55, 60, 23));

            AddNote("破魂斩 劈星斩 雷霆一击(所有战士合计，战士部分走的一个伤害算法)，合击技能仅支持到4级",
                new Rectangle(39, 76, 527, 18), NoteGreen);

            AddToggle("战士合击", new Rectangle(38, 94, 130, 22));
            AddNote("伤害调整（<攻击>*<技能等级加成>）（默认每级加成1.5，2，2.4，2.6，2.8）",
                new Rectangle(156, 99, 437, 13), NoteGreen);
            AddParameter("战士合击_数值1", new Rectangle(594, 94, 60, 23));
            AddParameter("战士合击_数值2", new Rectangle(663, 94, 60, 23));
            AddParameter("战士合击_数值3", new Rectangle(732, 94, 60, 23));
            AddParameter("战士合击_数值4", new Rectangle(801, 94, 60, 23));
            AddParameter("战士合击_数值5", new Rectangle(870, 94, 59, 23));

            AddNote("末日审判 噬魂沼泽 劈星斩 雷霆一击(法道合击类技能算法)，合击技能仅支持到4级",
                new Rectangle(40, 120, 500, 18), NoteGreen, true, BorderStyle.Fixed3D);
            AddToggle("法道合击", new Rectangle(38, 140, 130, 22));
            AddNote("伤害调整(<攻击>*<技能等级加成>)(默认每级加成1.8，2.5，3.3，3.6，3.9)",
                new Rectangle(156, 145, 422, 13), NoteGreen);
            AddParameter("法道合击_数值1", new Rectangle(594, 140, 60, 23));
            AddParameter("法道合击_数值2", new Rectangle(663, 140, 60, 23));
            AddParameter("法道合击_数值3", new Rectangle(732, 140, 60, 23));
            AddParameter("法道合击_数值4", new Rectangle(801, 140, 60, 23));
            AddParameter("法道合击_数值5", new Rectangle(870, 140, 59, 23));

            AddToggle("屏蔽排行榜", new Rectangle(38, 174, 151, 22));
            AddToggle("装备吸血", new Rectangle(189, 175, 157, 22));
            AddWrappedNote("支持任意首饰吸血，修改数据库两个字段即可，shape字段设置，戒\n" +
                           "指136，手镯137，项链138，anicounte字段设置单次攻击吸血量",
                new Rectangle(315, 172, 359, 33), NoteGreen);
            AddNote("怪物爆率全局调整", new Rectangle(762, 172, 102, 21), NoteGreen,
                true, BorderStyle.Fixed3D, ContentAlignment.MiddleCenter);

            AddToggle("脚本控制人物爆率", new Rectangle(38, 216, 190, 22));
            AddWrappedNote("人物爆率分为【杀人爆率】和【防爆属性】，退出消失，需要登录脚本重设；设置\n" +
                           "杀人爆率:SetV(1,2,N);设置防爆属性:SetV(1,3,M)",
                new Rectangle(227, 213, 441, 33), NoteGreen);
            AddWrappedNote("数据库配置任意装备CC字段属性，即\n" +
                           "[刺术下限]，每加1点，提升爆率约\n" +
                           "10%，具体爆率根据公式计算;假设某\n" +
                           "物品爆率为1/N，系数A默认为10，系\n" +
                           "数B默认为10，实际爆率：(B+CC)/N*A\n" +
                           "，例如CC=1,某物品爆率1/20",
                new Rectangle(726, 202, 195, 91), NoteGreen);

            AddNote("默认人物爆率：(K+M-N) 分之1，（K+M-N）<=0时装备必爆，默认K=90，红名K=21",
                new Rectangle(41, 254, 441, 18), NoteGreen);
            AddToggle("人物爆率调整", new Rectangle(38, 278, 130, 22));
            AddNote("非红名K值:", new Rectangle(201, 281, 60, 13), NoteGreen);
            AddParameter("非红名K值", new Rectangle(272, 278, 60, 23));
            AddNote("红名K值:", new Rectangle(359, 283, 48, 13), NoteGreen);
            AddParameter("红名K值", new Rectangle(416, 278, 60, 23));
            AddNote("死亡掉落装备最大数量:", new Rectangle(504, 283, 126, 13), NoteGreen);
            AddParameter("最大装备数量", new Rectangle(639, 278, 60, 23));
            AddToggle("装备提升人物爆率", new Rectangle(732, 286, 184, 22), null, "已重设");

            AddToggle("修改召唤神兽", new Rectangle(201, 300, 177, 22));

            AddCaption("人物等级:", new Rectangle(33, 327, 54, 13), NoteGreen);
            AddParameter("人物等级1_值", new Rectangle(93, 323, 60, 23));
            AddCaption("怪物名字:", new Rectangle(168, 328, 54, 13), NoteGreen);
            AddParameter("怪物名字1_值", new Rectangle(227, 323, 60, 23));
            AddCaption("怪物数量:", new Rectangle(308, 327, 54, 13), NoteGreen);
            AddParameter("怪物数量1_值", new Rectangle(368, 323, 60, 23));

            AddNote("调整A、B实现爆率全局调整(公式如上):", new Rectangle(491, 323, 216, 18), NoteGreen);
            AddCaption("a:", new Rectangle(720, 327, 12, 13), NoteGreen);
            AddParameter("装备提升人物爆率_A值", new Rectangle(741, 320, 60, 23));
            AddCaption("B:", new Rectangle(818, 327, 18, 13), NoteGreen);
            AddParameter("装备提升人物爆率_B值", new Rectangle(842, 320, 59, 23));

            AddCaption("人物等级:", new Rectangle(33, 359, 54, 13), NoteGreen);
            AddParameter("人物等级2_值", new Rectangle(93, 354, 60, 23));
            AddCaption("怪物名字:", new Rectangle(168, 359, 54, 13), NoteGreen);
            AddParameter("怪物名字2_值", new Rectangle(227, 354, 60, 23));
            AddCaption("怪物数量:", new Rectangle(308, 359, 54, 13), NoteGreen);
            AddParameter("怪物数量2_值", new Rectangle(368, 354, 60, 23));
            AddStaticToggle("获取玩家对象函数(未启用)", new Rectangle(494, 356, 191, 22));

            AddCaption("人物等级:", new Rectangle(32, 392, 54, 13), NoteGreen);
            AddParameter("人物等级3_值", new Rectangle(93, 387, 60, 23));
            AddCaption("怪物名字:", new Rectangle(168, 390, 54, 13), NoteGreen);
            AddParameter("怪物名字3_值", new Rectangle(227, 387, 60, 23));
            AddCaption("怪物数量:", new Rectangle(309, 390, 54, 13), NoteGreen);
            AddParameter("怪物数量3_值", new Rectangle(368, 387, 60, 23));

            ResumeLayout(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _formulaFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
