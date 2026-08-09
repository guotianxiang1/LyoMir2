namespace GameSvr.Plugins
{
    internal abstract class FixedReplicaPanelBase : Panel, IReplicaConfigEditor
    {
        private readonly Dictionary<string, TogglePanel.ToggleRow> _rows = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _missingExpectedKeys = new(StringComparer.Ordinal);

        protected FixedReplicaPanelBase()
        {
            AutoScroll = false;
            BackColor = SystemColors.Control;
            BorderStyle = BorderStyle.FixedSingle;
            DoubleBuffered = true;
            Font = YanshenUiFont.Create("SimSun", 9f);
        }

        public event Action<string, bool> OnToggle;
        public event Action<string, string> OnEditChanged;

        public IReadOnlyCollection<string> MissingKeys { get; private set; } = Array.Empty<string>();

        protected virtual bool UseThinInputBorders => false;
        protected virtual bool UseFlatDropDownBorders => false;
        protected virtual bool UseFixedTextBoxHeight => false;

        public void SetFeatures(IEnumerable<ReplicaFeature> features)
        {
            SuspendLayout();
            Controls.Clear();
            _rows.Clear();
            _usedKeys.Clear();
            _missingExpectedKeys.Clear();

            foreach (var feature in features ?? Array.Empty<ReplicaFeature>())
            {
                if (feature.MainRow != null) _rows[feature.MainRow.Key] = feature.MainRow;
                foreach (var parameter in feature.ParameterRows) _rows[parameter.Key] = parameter;
            }

            BuildPage();
            var unboundKeys = _rows.Keys.Where(key => !_usedKeys.Contains(key)).ToArray();
            MissingKeys = _missingExpectedKeys.Concat(unboundKeys)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            if (unboundKeys.Length > 0)
                throw new InvalidOperationException($"{GetType().Name} has unbound config keys: {string.Join(", ", MissingKeys)}");
            ResumeLayout(false);
        }

        public void CommitPendingEdit()
        {
            Focus();
        }

        protected abstract void BuildPage();

        protected CheckBox AddToggle(string key, int x, int y, int width,
            string displayName = null, Color? color = null,
            string enabledStatus = "已启动", string disabledStatus = "未启动")
        {
            if (!_rows.TryGetValue(key, out var row))
            {
                _missingExpectedKeys.Add(key);
                var missing = new CheckBox
                {
                    AutoSize = true,
                    Bounds = new Rectangle(x, y, width, 22),
                    Enabled = false,
                    ForeColor = SystemColors.GrayText,
                    Text = $"{displayName ?? DisplayName(key)}(未配置)",
                    UseVisualStyleBackColor = false,
                };
                Controls.Add(missing);
                return missing;
            }
            if (!row.IsToggle) throw new InvalidOperationException($"{key} is not a toggle");
            _usedKeys.Add(key);
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Bounds = new Rectangle(x, y, width, 22),
                Checked = row.BoolValue,
                ForeColor = color ?? SystemColors.ControlText,
                Text = ToggleText(displayName ?? DisplayName(key), row.BoolValue,
                    enabledStatus, disabledStatus),
                UseVisualStyleBackColor = false,
            };
            checkBox.CheckedChanged += (_, _) =>
            {
                row.BoolValue = checkBox.Checked;
                checkBox.Text = ToggleText(displayName ?? DisplayName(key), row.BoolValue,
                    enabledStatus, disabledStatus);
                OnToggle?.Invoke(key, row.BoolValue);
            };
            Controls.Add(checkBox);
            return checkBox;
        }

        protected Control AddValue(string key, string label, int x, int y,
            int labelWidth = 55, int inputWidth = 58, Color? color = null, bool useDropDown = false)
        {
            if (!_rows.TryGetValue(key, out var row))
            {
                _missingExpectedKeys.Add(key);
                if (!string.IsNullOrEmpty(label))
                {
                    Controls.Add(new Label
                    {
                        AutoSize = false,
                        Bounds = new Rectangle(x, y, labelWidth, 22),
                        ForeColor = SystemColors.GrayText,
                        Text = label,
                        TextAlign = ContentAlignment.MiddleLeft,
                    });
                }
                var missing = new TextBox
                {
                    AutoSize = !(UseThinInputBorders || UseFixedTextBoxHeight),
                    BackColor = SystemColors.Control,
                    BorderStyle = UseThinInputBorders ? BorderStyle.FixedSingle : BorderStyle.Fixed3D,
                    Bounds = new Rectangle(x + labelWidth, y, inputWidth, 23),
                    Enabled = false,
                };
                Controls.Add(missing);
                return missing;
            }
            if (row.IsToggle) throw new InvalidOperationException($"{key} is not a value");
            _usedKeys.Add(key);
            if (!string.IsNullOrEmpty(label))
            {
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Bounds = new Rectangle(x, y, labelWidth, 22),
                    ForeColor = color ?? SystemColors.ControlText,
                    Text = label,
                    TextAlign = ContentAlignment.MiddleLeft,
                });
            }
            Control editor;
            if (useDropDown)
            {
                var comboBox = new ComboBox();
                comboBox.Bounds = new Rectangle(x + labelWidth, y, inputWidth,
                    UseFlatDropDownBorders ? 21 : 23);
                comboBox.DropDownStyle = ComboBoxStyle.DropDown;
                comboBox.FlatStyle = UseFlatDropDownBorders ? FlatStyle.Flat : FlatStyle.Standard;
                comboBox.IntegralHeight = false;
                comboBox.Text = row.TextValue ?? string.Empty;
                editor = comboBox;
            }
            else
            {
                editor = new TextBox
                {
                    AutoSize = !(UseThinInputBorders || UseFixedTextBoxHeight),
                    BorderStyle = UseThinInputBorders ? BorderStyle.FixedSingle : BorderStyle.Fixed3D,
                    Bounds = new Rectangle(x + labelWidth, y, inputWidth, 23),
                    Text = row.TextValue ?? string.Empty,
                };
            }
            editor.TextChanged += (_, _) =>
            {
                row.TextValue = editor.Text;
                OnEditChanged?.Invoke(key, editor.Text);
            };
            Controls.Add(editor);
            return editor;
        }

        protected Label AddNote(string text, Rectangle bounds, Color color, bool whiteBackground = true)
        {
            Label label = bounds.Height <= 24 ? new YanshenSingleLineLabel() : new Label();
            label.AutoSize = false;
            label.BackColor = whiteBackground ? Color.White : SystemColors.Control;
            label.Bounds = bounds;
            label.ForeColor = color;
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(label);
            return label;
        }

        protected Button AddButton(string text, Rectangle bounds, Action action = null)
        {
            var button = new ClassicButton
            {
                Bounds = bounds,
                Text = text,
                UseVisualStyleBackColor = false,
            };
            if (action != null) button.Click += (_, _) => action();
            Controls.Add(button);
            return button;
        }

        protected void AddSeparator(int y)
        {
            Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = SystemColors.ControlDark,
                Bounds = new Rectangle(0, y, Math.Max(1, ClientSize.Width), 1),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            });
        }

        private static string ToggleText(
            string name, bool enabled, string enabledStatus, string disabledStatus) =>
            $"{name}({(enabled ? enabledStatus : disabledStatus)})";

        private static string DisplayName(string key) => key.EndsWith('a') ? key[..^1] : key;

    }

    internal sealed class LegacyOneReplicaPanel : FixedReplicaPanelBase
    {
        protected override bool UseThinInputBorders => true;
        protected override bool UseFlatDropDownBorders => true;

        protected override void BuildPage()
        {
            const int x1 = 29;
            const int x2 = 177;
            const int x3 = 308;
            const int x4 = 437;
            const int x5 = 647;

            AddEnabledToggle("土城摆摊", x1, 22, 145);
            AddEnabledToggle("摆摊穿人", x2, 22, 150);
            AddEnabledToggle("随身仓库", x3, 22, 125);
            AddEnabledToggle("召唤神兽", x4, 22, 116);
            AddValue("神兽_序号", "", 560, 23, 0, 72, null, true);
            var comboBorderColor = Color.FromArgb(112, 112, 112);
            foreach (var border in new[]
                     {
                         new Rectangle(560, 22, 72, 1),
                         new Rectangle(560, 42, 72, 1),
                         new Rectangle(560, 23, 1, 19),
                         new Rectangle(631, 23, 1, 19),
                     })
            {
                Controls.Add(new Label
                {
                    AutoSize = false,
                    BackColor = comboBorderColor,
                    Bounds = border,
                });
            }
            AddNote("数量(最大127)", new Rectangle(644, 26, 75, 13), Color.Red);
            AddValue("神兽_数量", "", 729, 23, 0, 47, Color.Red);

            AddEnabledToggle("攻沙脚本控制", x1, 49, 145);
            AddEnabledToggle("专职变性", x2, 49, 150, "转职变性");
            AddEnabledToggle("邮件防刷", x3, 49, 125);
            AddEnabledToggle("召唤骷髅", x4, 49, 116);
            AddNote("数量(最大127)", new Rectangle(644, 52, 75, 13), Color.Red);
            AddValue("召唤骷髅_数量", "", 729, 49, 0, 47, Color.Red);

            AddEnabledToggle("全服击杀提示", x1, 74, 145);
            AddEnabledToggle("安全区禁止丢物", x2, 74, 165);
            AddEnabledToggle("穿人穿怪", x3, 74, 125);
            AddToggle("防0拆分", x4, 74, 120);
            AddEnabledToggle("修复刺杀位麻痹", x5, 74, 205);

            AddEnabledToggle("禁止交易地图", x1, 101, 145);
            AddNote("：开启后编号长度等于15的地图将禁止交易，例如【D515~6789ABCDEF】",
                new Rectangle(179, 104, 398, 18), Color.Red);
            AddEnabledToggle("修复卡防御", x5, 101, 205);

            AddEnabledToggle("禁止宝宝休息", x1, 124, 145);
            AddNote("：开启后编号长度等于15的地图将禁止宝宝休息，例如【D515~6789ABCDEF】",
                new Rectangle(180, 127, 414, 18), Color.Red);
            AddEnabledToggle("下线宝宝死亡", x5, 124, 205);

            AddEnabledToggle("行会显示", x1, 148, 145);
            AddNote("：调用M2原生行会显示功能，最完美显示行会方案，在线玩家须小退才能显示。",
                new Rectangle(180, 150, 438, 18), Color.Red);
            AddEnabledToggle("屏蔽属性提升提示", x5, 148, 205);

            AddEnabledToggle("地面物品消失时间", x1, 174, 165);
            AddNote("单位秒 默认600", new Rectangle(199, 176, 90, 13), Color.Red);
            AddValue("地面物品消失时间_时间", "", 302, 171, 0, 71, Color.Red);
            AddEnabledToggle("屏蔽元宝增减信息", x4, 174, 190);
            AddEnabledToggle("屏蔽发言频繁禁言功能", x5, 174, 235);

            AddEnabledToggle("指定地图编号摆摊", x1, 199, 165);
            AddNote("地图编号：", new Rectangle(206, 205, 62, 18), Color.Red);
            AddValue("摆摊地图", "", 302, 200, 0, 71, Color.Red);
            AddToggle("关闭摆摊", x4, 199, 150, null, null, "已关闭", "未关闭");
            AddToggle("盘古高级属性", x5, 199, 190);

            AddToggle("限制摆摊", x1, 227, 260, "限制坐标区域和玩家等级摆摊PRO");
            AddNote("区域左上X：", new Rectangle(300, 229, 66, 18), Color.Red);
            AddValue("限制摆摊_左x", "", 377, 224, 0, 51, Color.Red);
            AddNote("Y：", new Rectangle(440, 228, 20, 18), Color.Red);
            AddValue("限制摆摊_左y", "", 462, 224, 0, 51, Color.Red);
            AddNote("右下区域X：", new Rectangle(525, 228, 66, 18), Color.Red);
            AddValue("限制摆摊_右x", "", 593, 224, 0, 51, Color.Red);
            AddNote("Y：", new Rectangle(653, 228, 23, 18), Color.Red);
            AddValue("限制摆摊_右y", "", 680, 224, 0, 44, Color.Red);
            AddNote("玩家等级：", new Rectangle(740, 226, 60, 18), Color.Red);
            AddValue("限制摆摊_等级", "", 806, 224, 0, 44, Color.Red);
            AddNote("修改说明：修改原版盘古摆摊权限，改为：This_Playerer.GetS(14,1)=100的玩家就失去摆摊权限，上面的坐标等级等限制功能均按原来设置",
                new Rectangle(44, 250, 813, 18), Color.Red);

            AddToggle("死亡触发", x1, 272, 135);
            AddToggle("回城按钮触发", 170, 272, 145);
            AddToggle("盘古穿戴触发", 327, 272, 145);
            AddToggle("盘古物理攻击触发", 479, 273, 170);
            AddToggle("盘古魔法攻击触发", 648, 273, 190);

            AddToggle("挖矿触发", x1, 298, 135);
            AddToggle("召唤骷髅触发", 170, 299, 145);
            AddToggle("召唤神兽触发", 327, 299, 145);
            AddToggle("心灵启示触发", 479, 299, 170);
            AddToggle("脚本控制头发外显", 648, 299, 205);

            AddToggle("踢玩家下线", x1, 322, 135);
            AddNote("兼容旧版盘古用法", new Rectangle(173, 325, 108, 18), Color.Red);
            AddToggle("盘古击杀触发", 326, 324, 145);
            AddToggle("盘古杀死宝宝", 482, 324, 170);
            AddToggle("盘古给与封号", x5, 324, 190);

            AddToggle("屏蔽元宝数据库日志", x1, 346, 180);
            AddNote("注意：以上所有功能更改数量前请先取消勾选，不然数据更改不会生效；所有编辑框修改都需要关闭勾选操作",
                new Rectangle(35, 371, 645, 13), Color.Red);
        }

        private CheckBox AddEnabledToggle(
            string key, int x, int y, int width, string displayName = null) =>
            AddToggle(key, x, y, width, displayName, null, "已启动", "未启用");
    }

    internal sealed class SeasonOneReplicaPanel : FixedReplicaPanelBase
    {
        private readonly Font _annotationFont = YanshenUiFont.Create("Tahoma", 8.25f);
        private readonly Font _compactAnnotationFont = YanshenUiFont.Create("Tahoma", 8f);

        protected override bool UseFixedTextBoxHeight => true;

        protected override void BuildPage()
        {
            Label AddReferenceNote(string text, Rectangle bounds)
            {
                var label = AddNote(text, bounds, Color.Red);
                label.TextAlign = ContentAlignment.TopLeft;
                return label;
            }

            var columns = new[] { 29, 182, 339, 528, 705 };
            AddToggle("自定义元素", columns[0], 31, 145);
            AddToggle("英雄自动开盾", columns[1], 31, 150);
            AddToggle("装备转生穿戴判定a", columns[2], 31, 180);
            AddToggle("诱惑之光触发脚本a", columns[3], 31, 170);
            AddToggle("主号分身术a", columns[4], 30, 205, "修主号分身杀动态怪");

            AddToggle("烈火固定增伤", columns[0], 61, 145, "烈火威力增加");
            AddToggle("冰咆哮固定增伤", columns[1], 59, 150, "冰咆哮威力增加");
            AddToggle("火墙固定增伤", columns[2], 57, 180, "火墙威力增加");
            AddToggle("火符固定增伤", columns[3], 61, 170, "火符威力增加");
            AddToggle("冰咆哮切割", columns[4], 59, 175);

            AddToggle("烈火切割", columns[0], 87, 145);
            AddToggle("雷电术切割", columns[1], 85, 150);
            AddToggle("火符切割", columns[2], 87, 180);
            AddToggle("火墙切割", columns[3], 88, 170);
            AddToggle("技能等级突破", 677, 88, 145);
            AddValue("技能等级突破_最大值", "", 821, 88, 0, 36);
            AddReferenceNote("最大255", new Rectangle(861, 93, 50, 16));

            AddToggle("宝宝自动叛变", columns[2], 114, 180);
            AddToggle("新呼唤宝宝", columns[3], 116, 170);
            AddToggle("嗜血术范围", 677, 116, 180);
            AddToggle("技能触发脚本", columns[2], 140, 180);
            AddToggle("全屏吸怪", columns[3], 143, 170);
            AddToggle("英雄千分比免伤", 677, 145, 205);
            AddReferenceNote(":SetS(1,58,值)", new Rectangle(842, 148, 80, 13));

            AddToggle("主号施法速度", 29, 171, 145);
            var speedLabel = AddReferenceNote("全局提升(ms)：", new Rectangle(177, 174, 80, 13));
            AddValue("主号全局法速", "", 263, 171, 0, 45);
            AddReferenceNote("注意：速度分为全局和个人s变量提升；两者互相叠加。个人s变量为This_Player.SetS(1,123,某个值);单位为毫秒",
                new Rectangle(324, 174, 605, 13));

            AddToggle("自定义伤害", 29, 200, 145);
            AddReferenceNote("使用方法：引入眼神专用\\AllFuc.pas，并使用Ys_Attact(RoleId,hp);即可对怪物或人物造成神圣伤害值",
                new Rectangle(177, 206, 607, 13));

            AddToggle("装备多职业", 29, 228, 145);
            AddReferenceNote("使用方法：装备数据库needjob字段新增8，9，10;若玩家gets(1,124)=needjob的字段，则允许穿戴。同时This_Player.Job=s(1,124)的值",
                new Rectangle(177, 232, 743, 13));

            AddToggle("战队职业限制", 29, 252, 170, "取消入战队职业限制");
            AddReferenceNote("：勾选多职业后建议启动，启动后不能限制进入战队和行会职业，可以显示新增职业；原版默认战队不显示新职业。",
                new Rectangle(240, 257, 693, 13));

            AddToggle("角色多阵营", 29, 278, 145);
            AddReferenceNote("使用方法：若两个玩家的gets(1,125)>0且互相相等，互相之间无法攻击，相当于他们两个之间是和平模式。",
                new Rectangle(177, 281, 695, 13));

            AddToggle("英雄读取极品", 29, 301, 145);
            AddReferenceNote("使用方法：引入眼神专用\\AllFuc.pas，并使用Ys_HeroJp(This_Player,pos,id);可以获取到英雄身上极品属性；pos:0~15(身体部位),id:1~6(极品类型)",
                new Rectangle(176, 306, 759, 13));
            AddReferenceNote("This_Player.SetS(1,177,减少被人暴击的概率)",
                new Rectangle(30, 323, 257, 13));
            AddReferenceNote("其他使用方法和第一季一模一样，但是只允许启动第二季或第一季，千万别都勾选【勾选就叠加，包括星耀的】",
                new Rectangle(302, 323, 630, 13));

            AddToggle("主号高级暴击", 29, 338, 155, "主号高级倍功暴击");
            AddReferenceNote("此暴击和倍功兼容了盘古的暴击和倍功，两者可以叠加使用，暴击倍数变量不设置默认两倍",
                new Rectangle(197, 343, 497, 13));
            AddToggle("高级英雄倍功暴击", 712, 342, 215);

            AddToggle("切换暴击报文", 29, 363, 145);
            AddReferenceNote("勾选后，暴击的报文函数切换到带nop的mybaoji()函数",
                new Rectangle(182, 366, 287, 13));
            AddToggle("获取沙城归属", 474, 363, 180);

            AddToggle("穿戴触发_plus", 29, 386, 145);
            AddReferenceNote("在RunQuest.pas中增加plus_ChangeEquip函数，和其他穿戴触发不能混用",
                new Rectangle(173, 390, 407, 13));

            ApplyAnnotationFont();
            speedLabel.Font = _compactAnnotationFont;
        }

        private void ApplyAnnotationFont()
        {
            foreach (var label in Controls.OfType<Label>())
                if (label.ForeColor == Color.Red) label.Font = _annotationFont;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _annotationFont.Dispose();
                _compactAnnotationFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class SeasonTwoReplicaPanel : FixedReplicaPanelBase
    {
        private readonly PluginManager _manager;
        private readonly Font _annotationFont = YanshenUiFont.Create("Tahoma", 8.25f);
        private readonly Font _compactAnnotationFont = YanshenUiFont.Create("Tahoma", 8f);

        internal SeasonTwoReplicaPanel(PluginManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        protected override bool UseFixedTextBoxHeight => true;

        protected override void BuildPage()
        {
            Label AddReferenceNote(string text, Rectangle bounds)
            {
                var label = AddNote(text, bounds, Color.Red);
                label.TextAlign = ContentAlignment.TopLeft;
                return label;
            }

            void AddRecycleSeparator(int y)
            {
                var separator = new Label
                {
                    AutoSize = false,
                    BackColor = Color.FromArgb(112, 112, 112),
                    Bounds = new Rectangle(242, y, 693, 1),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                };
                Controls.Add(separator);
                separator.BringToFront();
            }

            AddToggle("道士合击系数", 21, 24, 170, null, null, "已启动", "待重设");
            var coefficientX = new[] { 173, 243, 312, 381, 450 };
            for (var index = 1; index <= coefficientX.Length; index++)
                AddValue("道士合击系数_数值" + index, "", coefficientX[index - 1], 23, 0, 60);
            AddReferenceNote("伤害调整(<攻击>*<技能等级加成>)(默认每级加成1.8,2.5,3.3,3.6,3.9)",
                new Rectangle(520, 28, 410, 13));

            AddToggle("伤害触发脚本_plus", 21, 52, 170);
            var runQuestNote = AddReferenceNote("RunQuest.pas内增加DoMySkill_plus",
                new Rectangle(194, 55, 197, 13));
            runQuestNote.BringToFront();
            AddToggle("自定义伤害_plus", 404, 52, 170);
            AddReferenceNote("一般与DoMySkill_plus配合使用，具体参考ys_MyJn_plus()函数注释，此函数可以自定义技能，但是对练功师无效果。",
                new Rectangle(576, 55, 354, 29));

            AddToggle("高级物理攻击触发", 21, 76, 185);
            AddToggle("高级魔法攻击触发", 206, 76, 185);
            AddReferenceNote("为了修正第一季攻击触发英雄报错问题，旧版和新版互相只允许勾选一个；所有函数名字和参数参考RunQuest.pas内脚本函数，特别注意:英雄可以利用攻击触发脚本写出英雄切割，可以用buff加切割飘血。",
                new Rectangle(374, 89, 560, 29));

            AddToggle("英雄物理攻击触发", 21, 102, 185);
            AddToggle("英雄魔法攻击触发", 206, 102, 185);
            AddToggle("毫秒级cd记录", 21, 125, 170);
            AddReferenceNote("：为自定义伤害专门做的毫秒级别的时间戳记录，引入眼神专用AllFuc.pas，并使用ys_CmpTime_min()函数对比cd，具体用法看例子。",
                new Rectangle(185, 127, 723, 16));

            AddToggle("千分比经验倍数", 21, 148, 170);
            AddReferenceNote("：计算公式：额外获取经验=(gets(1,126)/1000+MultTempExpRate)*杀怪经验【MultTempExpRate默认是0】",
                new Rectangle(185, 150, 723, 16));

            AddToggle("新怪物爆率", 21, 172, 145);
            AddReferenceNote("全局调整：", new Rectangle(152, 174, 70, 16));
            AddReferenceNote("A值：", new Rectangle(232, 174, 34, 16));
            AddValue("怪物爆率A_值", "", 276, 171, 0, 50);
            AddReferenceNote("B值：", new Rectangle(344, 174, 34, 16));
            AddValue("怪物爆率B_值", "", 384, 171, 0, 50);
            AddReferenceNote("K值：", new Rectangle(456, 174, 34, 16));
            AddValue("怪物爆率K_值", "", 495, 171, 0, 50);
            AddReferenceNote("增加A或s54爆率增加，增加b或者k爆率降低【A,K可以负数】",
                new Rectangle(574, 174, 341, 16));
            var dropFormula = AddReferenceNote("爆率计算公式：(分子s54号s变量+A值)/(分母B值*s55号s变量+K值)；默认A=0,B=1,K=0,若个人s变量小于等于0时，则s变量不参加运算【每次修改数值必须重新勾选生效】",
                new Rectangle(23, 198, 905, 16));

            AddToggle("全局循环函数", 21, 219, 145);
            AddReferenceNote("循环时间：", new Rectangle(176, 223, 60, 16));
            AddValue("循环时间_值", "", 243, 219, 0, 50);
            AddReferenceNote("毫秒", new Rectangle(305, 223, 35, 16));
            AddReferenceNote("需要在RunQuest.pas脚本中增加procedure MyTimer();函数，具体用法详见注释",
                new Rectangle(350, 223, 497, 16));

            AddRecycleSeparator(247);
            AddToggle("高级回收", 21, 247, 130);
            var statusText = _manager.HasValidRecycleConfig
                ? "检查格式正确，可以正常使用！"
                : "回收配置未加载，请检查 recycle.json";
            var status = AddNote(statusText, new Rectangle(240, 248, 695, 22), SystemColors.ControlText);
            status.AutoEllipsis = true;
            AddButton("重载回收配置", new Rectangle(153, 248, 82, 22), () =>
            {
                status.Text = _manager.ReloadRecycleConfig(out var error)
                    ? "检查格式正确，可以正常使用！"
                    : "重载失败，继续使用上次配置：" + error;
            });
            AddRecycleSeparator(269);

            AddToggle("眼神特殊函数", 21, 278, 145);
            AddReferenceNote("眼神专用AllFuc.pas内部以后所有的新扩展函数均勾选他就可以使用，比如自定义麻痹等，使用说明看更新公告",
                new Rectangle(179, 281, 582, 16));

            AddToggle("攻击吸血", 21, 302, 130);
            var attackVampNote = AddReferenceNote("：sets(1,129)=吸血千分比【例如：s(1,129)=500，就吸每次伤害的50%的血，群功吸血叠加】",
                new Rectangle(140, 304, 495, 16));
            AddToggle("火墙不吸血", 653, 300, 150);

            AddToggle("super攻击触发", 21, 326, 145);
            var superNote = AddReferenceNote("：启动后无论物理攻击还是普通攻击都会执行runquest.pas中的Super_Attack()函数",
                new Rectangle(164, 328, 435, 16));
            AddToggle("英雄野蛮", 647, 326, 135);
            AddReferenceNote("s(1,130)=野蛮概率，\r\ns(1,131)=野蛮cd秒", new Rectangle(786, 322, 132, 36));

            ApplyAnnotationFont();
            dropFormula.Font = _compactAnnotationFont;
            attackVampNote.Font = _compactAnnotationFont;
            superNote.Font = _compactAnnotationFont;
        }

        private void ApplyAnnotationFont()
        {
            foreach (var label in Controls.OfType<Label>())
                if (label.ForeColor == Color.Red) label.Font = _annotationFont;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _annotationFont.Dispose();
                _compactAnnotationFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
