using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using GameSvr.Plugins;

internal static class Program
{
    private static readonly IntPtr DpiAwarenessContextUnawareGdiScaled = new(-5);
    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);
    private static readonly IntPtr DpiAwarenessContextSystemAware = new(-2);
    private static readonly string[] OriginalRoleLeafNames =
    {
        "切割也反伤", "火墙不反伤", "反伤带抗性", "千分比属性", "主号被攻击触发",
        "主号新切割", "临时属性", "新永久属性", "金币上限突破", "修复飘血数值",
        "自定义循环函数", "月灵不扣蓝", "月灵新伤害", "瞬移怪物", "杀怪触发",
        "自定义红名村", "沙巴克攻城范围", "沙巴克复活点", "英雄切割",
        "指定英雄放技能", "英雄不自动释放技能", "npc自定义函数"
    };
    private static readonly string[] OriginalSkillLeafNames =
    {
        "诱惑之光修改", "主号分身术", "额外技能", "真隐身术修复", "主号技能加成",
        "气功波重定义", "自定义野蛮", "概率格挡", "刺杀免伤", "安全区禁止诱惑和圣言",
        "技能弹射", "英雄分身修复", "英雄开天修复", "合击等级修改", "英雄技能加成",
        "英雄技能点数增加", "怪物伤害触发技能特效"
    };
    private static readonly Dictionary<string, string[]> OriginalHelpFragments = new(StringComparer.Ordinal)
    {
        ["千分比属性"] = new[] { "sets(1,155", "sets(1,156", "sets(1,157" },
        ["主号被攻击触发"] = new[] { "参数意义：", "roleid：", "types：", "Name：" },
        ["主号新切割"] = new[]
        {
            "This_Player.SetS(1, 10", "This_Player.SetS(1, 11", "This_Player.SetS(1, 50",
            "This_Player.SetS(1, 51", "This_Player.SetS(1, 52", "This_Player.SetS(1, 62"
        },
        ["新永久属性"] = new[]
        {
            "This_Player.SetS(1,15", "This_Player.SetS(1,16",
            "This_Player.SetS(1,17", "This_Player.SetS(1,18"
        },
        ["金币上限突破"] = new[]
        {
            "修改金币上限超过5000万！", "This_Player.SetS(1,17", "This_Player.SetS(1,18"
        },
        ["修复飘血数值"] = new[]
        {
            "本功能暂时先删除，勾选后无效果", "This_Player.SetS(1,17", "This_Player.SetS(1,18"
        },
        ["自定义循环函数"] = new[] { "timer=0", "ClearAll" },
        ["月灵新伤害"] = new[] { "假如为10" },
        ["杀怪触发"] = new[] { "参数分别是" },
        ["英雄切割"] = new[] { "This_Player.SetS(2,3", "RunQuest.pas", "procedure HeroCutting" },
        ["指定英雄放技能"] = new[] { "function Ys_SetHeroCskill", "magicid", "isrun" },
        ["英雄不自动释放技能"] = new[] { "【特别注意" },
        ["诱惑之光修改"] = new[] { "变量控制生效", "假如v1=11" },
        ["主号分身术"] = new[] { "This_Player.SetS(1,200", "This_Player.SetS(1,201" },
        ["额外技能"] = new[] { "伤害间隔：", "单次伤害情况", "是否s变量控制" },
        ["真隐身术修复"] = new[] { "pid=60" },
        ["气功波重定义"] = new[] { "This_Player.SetS(1,202" },
        ["概率格挡"] = new[] { "RunQuest.pas" },
        ["技能弹射"] = new[] { "Ys_TanTanSkill", "js:integer" },
        ["怪物伤害触发技能特效"] = new[] { "伤害间隔：", "单次伤害情况", "点击打开配置文件" },
    };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--program-order", StringComparer.OrdinalIgnoreCase))
                return CheckCurrentProgramOrder();
            if (args.Contains("--real-yanshen", StringComparer.OrdinalIgnoreCase))
                return CheckRealYanshen(args);
            if (args.Contains("--dedicated-sta", StringComparer.OrdinalIgnoreCase))
                return CheckDedicatedStaYanshen(args);

            Assert(Application.SetHighDpiMode(HighDpiMode.SystemAware),
                "failed to establish the SystemAware host context");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var host = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(400, 300),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(100, 100),
                Text = "DPI host",
            };
            host.Show();
            Application.DoEvents();

            Form replica;
            TabControl tabs;
            TextBox initialChild;
            TextBox delayedChild;
            TextBox? shownChild = null;
            DpiAwareness shownAwareness = DpiAwareness.Invalid;
            using (new ThreadDpiScope(DpiAwarenessContextUnawareGdiScaled))
            {
                replica = new Form
                {
                    AutoScaleMode = AutoScaleMode.None,
                    ClientSize = new Size(1016, 680),
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(600, 100),
                    Text = "DPI replica",
                };
                tabs = new TabControl { Dock = DockStyle.Fill };
                var first = new TabPage("first");
                var delayed = new TabPage("delayed");
                initialChild = new TextBox { Bounds = new Rectangle(10, 10, 120, 23) };
                delayedChild = new TextBox { Bounds = new Rectangle(10, 10, 120, 23) };
                first.Controls.Add(initialChild);
                delayed.Controls.Add(delayedChild);
                tabs.TabPages.Add(first);
                tabs.TabPages.Add(delayed);
                replica.Controls.Add(tabs);
                replica.Shown += (_, _) =>
                {
                    shownAwareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(
                        NativeMethods.GetThreadDpiAwarenessContext());
                    shownChild = new TextBox { Bounds = new Rectangle(10, 50, 120, 23) };
                    first.Controls.Add(shownChild);
                };
                replica.Show(host);
            }
            Application.DoEvents();

            try
            {
                var hostEvidence = WindowEvidence.Read(host.Handle);
                var replicaEvidence = WindowEvidence.Read(replica.Handle);
                var initialEvidence = WindowEvidence.Read(initialChild.Handle);
                var shownControl = shownChild ??
                    throw new InvalidOperationException("Shown did not run during the first Show/DoEvents cycle");
                var shownEvidence = WindowEvidence.Read(shownControl.Handle);

                tabs.SelectedIndex = 1;
                Application.DoEvents();
                var delayedEvidence = WindowEvidence.Read(delayedChild.Handle);

                var lateChild = new TextBox { Bounds = new Rectangle(10, 50, 120, 23) };
                tabs.TabPages[1].Controls.Add(lateChild);
                lateChild.Show();
                Application.DoEvents();
                var lateEvidence = WindowEvidence.Read(lateChild.Handle);

                Console.WriteLine($"host: {hostEvidence}");
                Console.WriteLine($"replica: {replicaEvidence}");
                Console.WriteLine($"initial child: {initialEvidence}");
                Console.WriteLine($"Shown context: {shownAwareness}; child: {shownEvidence}");
                Console.WriteLine($"delayed child: {delayedEvidence}");
                Console.WriteLine($"late child: {lateEvidence}");

                Assert(hostEvidence.Awareness == DpiAwareness.SystemAware && hostEvidence.Dpi == 192,
                    "host window was not SystemAware at the machine's 192 DPI");
                Assert(replicaEvidence.Awareness == DpiAwareness.Unaware && replicaEvidence.Dpi == 96,
                    "replica window did not retain the scoped DPI-unaware context");
                Assert(replicaEvidence.Width == 2032 && replicaEvidence.Height == 1360,
                    $"1016x680 replica was not uniformly virtualized to 2032x1360: {replicaEvidence.Width}x{replicaEvidence.Height}");
                Assert(shownAwareness == DpiAwareness.Unaware && shownEvidence.Awareness == DpiAwareness.Unaware,
                    "Shown-created controls escaped the new+Show DPI scope");
                Assert(delayedEvidence.Awareness == DpiAwareness.Unaware,
                    "a control constructed in-scope changed context when its handle was created later");
                Assert(lateEvidence.Awareness == DpiAwareness.SystemAware,
                    "the audit no longer demonstrates the out-of-scope late-control limitation");

                Console.WriteLine("YanshenDpiIsolationCheck PASS");
                return 0;
            }
            finally
            {
                replica.Close();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("YanshenDpiIsolationCheck FAIL: " + exception);
            return 1;
        }
    }

    private static int CheckCurrentProgramOrder()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var accepted = Application.SetHighDpiMode(HighDpiMode.SystemAware);
        var awareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(
            NativeMethods.GetThreadDpiAwarenessContext());
        Console.WriteLine($"EnableVisualStyles-first: accepted={accepted} awareness={awareness}");
        Assert(accepted && awareness == DpiAwareness.SystemAware,
            "the current EnableVisualStyles-first order did not establish SystemAware");
        Console.WriteLine("YanshenDpiIsolationCheck PROGRAM-ORDER PASS");
        return 0;
    }

    private static int CheckRealYanshen(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        PrepareGameSvrRuntimeFiles();
        Assert(Application.SetHighDpiMode(HighDpiMode.SystemAware),
            "failed to establish the SystemAware host context");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var source = Option(args, "--config") ??
            @"D:\lyom2Release\mud2.0\Mir200\GS1\config.json";
        var snapshot = Path.GetFullPath(Option(args, "--snapshot") ??
            Path.Combine(AppContext.BaseDirectory, "yanshen-real-dpi-192.png"));
        var dpiContextName = Option(args, "--dpi-context") ?? "unaware";
        var dpiContext = dpiContextName switch
        {
            "unaware" => DpiAwarenessContextUnaware,
            "unaware-gdi-scaled" => DpiAwarenessContextUnawareGdiScaled,
            _ => throw new ArgumentException("--dpi-context must be unaware or unaware-gdi-scaled"),
        };
        Assert(File.Exists(source), "published Yanshen config was not found: " + source);

        var root = Path.Combine(Path.GetTempPath(), "yanshen-real-dpi-" + Guid.NewGuid().ToString("N"));
        var mir200 = Path.Combine(root, "Mir200");
        var runtime = Path.Combine(mir200, "GS1");
        var envir = Path.Combine(mir200, "Envir");
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(envir);
        File.Copy(source, Path.Combine(runtime, "config.json"));
        CopyMyJsonFixture(Path.GetDirectoryName(source)!, runtime);

        YanshenConfigForm? form = null;
        try
        {
            var manager = new PluginManager(envir, runtime);
            manager.RegisterBuiltinPlugins();
            Assert(manager.LoadPlugin("YanshenCompat"), "YanshenCompat did not enter Running state");

            using var host = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(400, 300),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(2200, 100),
                Text = "SystemAware M2 DPI audit host",
            };
            host.Show();
            Application.DoEvents();

            using (new ThreadDpiScope(dpiContext))
            {
                form = new YanshenConfigForm(manager)
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = Point.Empty,
                };
                form.Show(host);
            }
            Application.DoEvents();

            var hostEvidence = WindowEvidence.Read(host.Handle);
            var formEvidence = WindowEvidence.Read(form.Handle);
            Assert(hostEvidence.Awareness == DpiAwareness.SystemAware && hostEvidence.Dpi == 192,
                "real host window was not SystemAware/192 DPI: " + hostEvidence);
            Assert(NativeMethods.AreDpiAwarenessContextsEqual(
                    NativeMethods.GetWindowDpiAwarenessContext(host.Handle), DpiAwarenessContextSystemAware),
                "real host window did not retain the exact SystemAware context");
            Assert(formEvidence.Awareness == DpiAwareness.Unaware && formEvidence.Dpi == 96,
                "real Yanshen window was not Unaware/96 DPI: " + formEvidence);
            Assert(NativeMethods.AreDpiAwarenessContextsEqual(
                    NativeMethods.GetWindowDpiAwarenessContext(form.Handle), dpiContext),
                $"real Yanshen window did not retain the exact {dpiContextName} context");
            Assert(form.ClientSize == new Size(1016, 680),
                $"real Yanshen logical client changed to {form.ClientSize.Width}x{form.ClientSize.Height}");
            Assert(formEvidence.Width == 2032 && formEvidence.Height == 1360,
                $"real Yanshen physical bounds were {formEvidence.Width}x{formEvidence.Height}, expected 2032x1360");
            Assert(form.ConfigKeyCount == 379 && form.AssignedConfigKeyCount == 379,
                $"real config did not bind 379 keys ({form.AssignedConfigKeyCount}/{form.ConfigKeyCount})");

            var rootTabs = Descendants(form).OfType<TabControl>().Single(tabs =>
                tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                    .SequenceEqual(YanshenConfigForm.OriginalRootPages));
            var legacyTabs = Descendants(rootTabs.TabPages[1]).OfType<TabControl>().Single(tabs =>
                tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                    .SequenceEqual(YanshenConfigForm.OriginalLegacyPages));
            rootTabs.SelectedIndex = 1;
            legacyTabs.SelectedIndex = 0;
            Application.DoEvents();

            var shownControls = Descendants(legacyTabs.TabPages[0])
                .Where(control => control.Parent?.GetType().Name == "LegacyOneReplicaPanel")
                .Where(control => control.Visible && control.Width > 0 && control.Height > 0)
                .ToArray();
            Assert(shownControls.Length >= 20,
                $"expected Shown-created legacy controls, found only {shownControls.Length}");

            var uniformlyScaled = 0;
            foreach (var control in shownControls)
            {
                var evidence = WindowEvidence.Read(control.Handle);
                Assert(evidence.Awareness == DpiAwareness.Unaware && evidence.Dpi == 96,
                    $"Shown-created {control.GetType().Name} escaped the 96-DPI context: {evidence}");
                Assert(NativeMethods.AreDpiAwarenessContextsEqual(
                        NativeMethods.GetWindowDpiAwarenessContext(control.Handle), dpiContext),
                    $"Shown-created {control.GetType().Name} escaped the exact {dpiContextName} context");
                Assert(Math.Abs(evidence.Width - control.Width * 2) <= 2 &&
                       Math.Abs(evidence.Height - control.Height * 2) <= 2,
                    $"Shown-created {control.GetType().Name} was not uniformly 2x: logical " +
                    $"{control.Width}x{control.Height}, physical {evidence.Width}x{evidence.Height}");
                uniformlyScaled++;
            }

            var sampleToggle = shownControls.OfType<CheckBox>().First();
            var titleLabel = Descendants(form).OfType<Label>().First(label => label.Text == "M2超级伴侣");
            Console.WriteLine($"font metrics: form={form.Font.Name}/{form.Font.Size}{form.Font.Unit} " +
                              $"height={form.Font.Height} dpi={form.DeviceDpi}; " +
                              $"titleHeight={titleLabel.Font.Height}/dpi={titleLabel.DeviceDpi}; " +
                              $"dynamicCheckHeight={sampleToggle.Font.Height}/dpi={sampleToggle.DeviceDpi} " +
                              $"bounds={sampleToggle.Bounds}");

            var allPagesPrefix = Option(args, "--all-pages-prefix");
            if (!string.IsNullOrWhiteSpace(allPagesPrefix))
            {
                CaptureAllRawPages(form, Path.GetFullPath(allPagesPrefix), dpiContext);
                rootTabs.SelectedIndex = 1;
                legacyTabs.SelectedIndex = 0;
                Application.DoEvents();
            }

            form.BringToFront();
            form.Refresh();
            Application.DoEvents();
            string rawPrintWindow;
            using (new ThreadDpiScope(dpiContext))
                rawPrintWindow = SavePrintWindow(form.Handle, form.ClientSize, formEvidence, snapshot);

            Console.WriteLine($"published config: {Path.GetFullPath(source)}");
            Console.WriteLine($"creation DPI context: {dpiContextName} ({dpiContext})");
            Console.WriteLine($"exact contexts: host=SystemAware, Yanshen+ShownControls={dpiContextName}");
            Console.WriteLine($"host: {hostEvidence}");
            Console.WriteLine($"real Yanshen: logical={form.ClientSize.Width}x{form.ClientSize.Height} physical={formEvidence}");
            Console.WriteLine($"Shown dynamic controls: {uniformlyScaled} all Unaware/96 and physical=logical*2");
            Console.WriteLine($"PrintWindow raw logical frame: {rawPrintWindow}");
            Console.WriteLine($"PrintWindow 2x physical visualization: {snapshot}");
            Console.WriteLine("YanshenDpiIsolationCheck REAL-YANSHEN PASS");
            return 0;
        }
        finally
        {
            form?.Close();
            Directory.Delete(root, true);
        }
    }

    private static int CheckDedicatedStaYanshen(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        PrepareGameSvrRuntimeFiles();
        Assert(Application.SetHighDpiMode(HighDpiMode.SystemAware),
            "failed to establish the SystemAware host context");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var source = Option(args, "--config") ??
            @"D:\lyom2Release\mud2.0\Mir200\GS1\config.json";
        var snapshot = Path.GetFullPath(Option(args, "--snapshot") ??
            Path.Combine(AppContext.BaseDirectory, "yanshen-dedicated-sta-unaware.png"));
        var reference = Path.GetFullPath(Option(args, "--reference") ??
            Path.Combine(AuditRepoRoot.Resolve(args, firstArgIsRepoRoot: false),
                "artifacts", "yanshen_reference_half", "legacy1.png"));
        Assert(File.Exists(source), "published Yanshen config was not found: " + source);
        Assert(File.Exists(reference), "legacy1 reference was not found: " + reference);

        var root = Path.Combine(Path.GetTempPath(), "yanshen-dedicated-sta-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "Mir200", "GS1");
        var envir = Path.Combine(root, "Mir200", "Envir");
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(envir);
        File.Copy(source, Path.Combine(runtime, "config.json"));
        CopyMyJsonFixture(Path.GetDirectoryName(source)!, runtime);

        try
        {
            var manager = new PluginManager(envir, runtime);
            manager.RegisterBuiltinPlugins();
            Assert(manager.LoadPlugin("YanshenCompat"), "YanshenCompat did not enter Running state");

            using var host = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(400, 300),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(2200, 100),
                Text = "SystemAware M2 dedicated-STA audit host",
            };
            host.Show();
            Application.DoEvents();
            var hostHandle = host.Handle;
            var hostEvidence = WindowEvidence.Read(hostHandle);

            DedicatedStaEvidence? evidence = null;
            Exception? threadFailure = null;
            var thread = new Thread(() =>
            {
                // This scope is intentionally the first WinForms-relevant action on this thread.
                using var dpiScope = new ThreadDpiScope(DpiAwarenessContextUnaware);
                YanshenConfigForm? form = null;
                try
                {
                    form = new YanshenConfigForm(manager)
                    {
                        StartPosition = FormStartPosition.Manual,
                        Location = Point.Empty,
                    };
                    form.FormClosed += (_, _) => Application.ExitThread();
                    form.Shown += (_, _) => form.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var logicalWindow = WindowEvidence.Read(form.Handle);
                            WindowEvidence physicalWindow;
                            using (new ThreadDpiScope(DpiAwarenessContextSystemAware))
                                physicalWindow = WindowEvidence.Read(form.Handle);
                            var rootTabs = Descendants(form).OfType<TabControl>().Single(tabs =>
                                tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                                    .SequenceEqual(YanshenConfigForm.OriginalRootPages));
                            var legacyTabs = Descendants(rootTabs.TabPages[1]).OfType<TabControl>().Single(tabs =>
                                tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                                    .SequenceEqual(YanshenConfigForm.OriginalLegacyPages));
                            rootTabs.SelectedIndex = 1;
                            legacyTabs.SelectedIndex = 0;
                            Application.DoEvents();

                            var controls = Descendants(legacyTabs.TabPages[0])
                                .Where(control => control.Parent?.GetType().Name == "LegacyOneReplicaPanel")
                                .Where(control => control.Visible && control.Width > 0 && control.Height > 0)
                                .ToArray();
                            var mismatchedWindows = controls.Count(control =>
                            {
                                var logical = WindowEvidence.Read(control.Handle);
                                WindowEvidence physical;
                                using (new ThreadDpiScope(DpiAwarenessContextSystemAware))
                                    physical = WindowEvidence.Read(control.Handle);
                                return logical.Awareness != DpiAwareness.Unaware || logical.Dpi != 96 ||
                                       Math.Abs(physical.Width - control.Width * 2) > 2 ||
                                       Math.Abs(physical.Height - control.Height * 2) > 2;
                            });
                            var sample = controls.OfType<CheckBox>().First();
                            var title = Descendants(form).OfType<Label>()
                                .First(label => label.Text == "M2超级伴侣");
                            form.BringToFront();
                            form.Refresh();
                            foreach (var control in Descendants(form).Where(control => control.Visible))
                                control.Refresh();
                            Application.DoEvents();
                            var raw = SavePrintWindow(form.Handle, form.ClientSize, physicalWindow, snapshot);
                            evidence = new DedicatedStaEvidence(
                                logicalWindow,
                                physicalWindow,
                                form.DeviceDpi,
                                form.Font.Height,
                                title.DeviceDpi,
                                title.Font.Height,
                                sample.DeviceDpi,
                                sample.Font.Height,
                                sample.Bounds,
                                controls.Length,
                                mismatchedWindows,
                                raw);
                        }
                        catch (Exception exception)
                        {
                            threadFailure = exception;
                        }
                        finally
                        {
                            form.Close();
                        }
                    }));
                    form.Show(new WindowHandleOwner(hostHandle));
                    Application.Run();
                }
                catch (Exception exception)
                {
                    threadFailure = exception;
                    form?.Dispose();
                }
            })
            {
                IsBackground = true,
                Name = "Yanshen DPI-unaware UI audit",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert(thread.Join(TimeSpan.FromSeconds(30)), "dedicated Yanshen STA thread timed out");
            if (threadFailure != null) throw new InvalidOperationException(
                "dedicated Yanshen STA thread failed", threadFailure);
            var result = evidence ?? throw new InvalidOperationException(
                "dedicated Yanshen STA thread produced no evidence");

            Assert(hostEvidence.Awareness == DpiAwareness.SystemAware && hostEvidence.Dpi == 192,
                "host did not remain SystemAware/192: " + hostEvidence);
            Assert(result.PhysicalWindow.Awareness == DpiAwareness.Unaware && result.PhysicalWindow.Dpi == 96,
                "dedicated Yanshen HWND was not Unaware/96: " + result.PhysicalWindow);
            Assert(result.LogicalWindow.Width == 1016 && result.LogicalWindow.Height == 680,
                "unaware caller did not observe the 1016x680 virtualized window: " + result.LogicalWindow);
            Assert(result.PhysicalWindow.Width == 2032 && result.PhysicalWindow.Height == 1360,
                "dedicated Yanshen physical size was not 2032x1360: " + result.PhysicalWindow);

            Console.WriteLine($"host: {hostEvidence}");
            Console.WriteLine($"dedicated Yanshen from unaware caller: {result.LogicalWindow}");
            Console.WriteLine($"dedicated Yanshen physical/SystemAware query: {result.PhysicalWindow}");
            Console.WriteLine($"managed DPI: form={result.FormDeviceDpi}, title={result.TitleDeviceDpi}, " +
                              $"dynamicCheck={result.DynamicControlDeviceDpi}");
            Console.WriteLine($"font heights: form={result.FormFontHeight}, title={result.TitleFontHeight}, " +
                              $"dynamicCheck={result.DynamicControlFontHeight}; checkBounds={result.DynamicControlBounds}");
            Console.WriteLine($"Shown controls: count={result.DynamicControlCount}, " +
                              $"window-context-or-2x-mismatches={result.DynamicControlWindowMismatches}");
            Console.WriteLine($"PrintWindow raw logical frame: {result.RawPrintWindow}");
            Console.WriteLine($"reference: {reference}");
            Console.WriteLine("YanshenDpiIsolationCheck DEDICATED-STA PASS");
            return 0;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed record DedicatedStaEvidence(
        WindowEvidence LogicalWindow,
        WindowEvidence PhysicalWindow,
        int FormDeviceDpi,
        int FormFontHeight,
        int TitleDeviceDpi,
        int TitleFontHeight,
        int DynamicControlDeviceDpi,
        int DynamicControlFontHeight,
        Rectangle DynamicControlBounds,
        int DynamicControlCount,
        int DynamicControlWindowMismatches,
        string RawPrintWindow);

    private sealed class WindowHandleOwner : IWin32Window
    {
        internal WindowHandleOwner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    private static string SavePrintWindow(
        IntPtr handle,
        Size logicalSize,
        WindowEvidence physicalBounds,
        string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var logical = new Bitmap(logicalSize.Width, logicalSize.Height);
        logical.SetResolution(96f, 96f);
        using (var graphics = Graphics.FromImage(logical))
        {
            var deviceContext = graphics.GetHdc();
            try
            {
                Assert(NativeMethods.PrintWindow(handle, deviceContext, 2),
                    "PrintWindow(PW_RENDERFULLCONTENT) failed");
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }

        var samples = new HashSet<int>();
        for (var y = 0; y < logical.Height; y += 8)
            for (var x = 0; x < logical.Width; x += 8)
                samples.Add(logical.GetPixel(x, y).ToArgb());
        Assert(samples.Count >= 8, $"PrintWindow image appears blank ({samples.Count} sampled colors)");

        var rawPath = SnapshotVariant(path, "raw-logical");
        logical.Save(rawPath, System.Drawing.Imaging.ImageFormat.Png);

        using var physical = new Bitmap(physicalBounds.Width, physicalBounds.Height);
        physical.SetResolution(96f, 96f);
        using (var graphics = Graphics.FromImage(physical))
        {
            graphics.Clear(Color.Black);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(logical,
                new Rectangle(Point.Empty, physical.Size),
                new Rectangle(Point.Empty, logical.Size),
                GraphicsUnit.Pixel);
        }
        for (var y = 0; y < logical.Height; y += 17)
        {
            for (var x = 0; x < logical.Width; x += 17)
            {
                var expected = logical.GetPixel(x, y).ToArgb();
                Assert(physical.GetPixel(x * 2, y * 2).ToArgb() == expected &&
                       physical.GetPixel(x * 2 + 1, y * 2).ToArgb() == expected &&
                       physical.GetPixel(x * 2, y * 2 + 1).ToArgb() == expected &&
                       physical.GetPixel(x * 2 + 1, y * 2 + 1).ToArgb() == expected,
                    $"2x PrintWindow visualization was not nearest-neighbor at {x},{y}");
            }
        }
        physical.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return rawPath;
    }

    private static void CaptureAllRawPages(
        YanshenConfigForm form, string prefix, IntPtr expectedDpiContext)
    {
        var pageFailures = new List<string>();
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        var rootTabs = Descendants(form).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(YanshenConfigForm.OriginalRootPages));
        var legacyTabs = Descendants(rootTabs.TabPages[1]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(YanshenConfigForm.OriginalLegacyPages));
        var seasonTabs = Descendants(rootTabs.TabPages[2]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(YanshenConfigForm.OriginalSeasonTwoPages));
        var categoryTabs = Descendants(seasonTabs.TabPages[2]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text)
                .SequenceEqual(new[] { "物品相关", "角色相关", "技能相关", "爆率相关", "预留功能" }));
        var gmTabs = Descendants(rootTabs.TabPages[0]).OfType<TabControl>().Single(tabs =>
            tabs.TabPages.Count == 3 && tabs.TabPages[0].Text == "gm工具1" &&
            tabs.TabPages[1].Text == "游戏内充-支付");

        rootTabs.SelectedIndex = 1;
        var legacyNames = new[] { "legacy1", "legacy2", "legacy3", "legacy4", "config1", "config2" };
        var legacyEditorTypes = new[]
        {
            "LegacyOneReplicaPanel", "Legacy2ReplicaPanel", "Legacy3ReplicaPanel",
            "EquipmentReplicaPanel", "Config1ReplicaPanel", "Config2ReplicaPanel"
        };
        var legacyEditorBounds = new[]
        {
            new Rectangle(59, 133, 935, 424), new Rectangle(58, 133, 935, 424),
            new Rectangle(60, 134, 935, 424), new Rectangle(58, 135, 935, 424),
            new Rectangle(59, 134, 935, 424), new Rectangle(59, 134, 935, 424),
        };
        for (var index = 0; index < legacyNames.Length; index++)
        {
            legacyTabs.SelectedIndex = index;
            RefreshForSnapshot(form);
            var editor = Descendants(legacyTabs.TabPages[index])
                .Single(control => control.GetType().Name == legacyEditorTypes[index]);
            AssertLayoutBounds(form, editor, legacyEditorBounds[index], legacyNames[index], pageFailures);
            AssertVisibleControlDpi(form, expectedDpiContext, legacyNames[index], pageFailures);
            SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_" + legacyNames[index] + ".png");
        }

        rootTabs.SelectedIndex = 2;
        seasonTabs.SelectedIndex = 0;
        RefreshForSnapshot(form);
        var seasonOneEditor = Descendants(seasonTabs.TabPages[0])
            .Single(control => control.GetType().Name == "SeasonOneReplicaPanel");
        AssertLayoutBounds(form, seasonOneEditor, new Rectangle(62, 137, 936, 430),
            "season1", pageFailures);
        var seasonOneToggles = Descendants(seasonOneEditor).OfType<CheckBox>().ToArray();
        var mainCloneToggle = seasonOneToggles.Single(toggle =>
            toggle.Text.StartsWith("修主号分身杀动态怪(", StringComparison.Ordinal));
        RecordPageFailure(mainCloneToggle.Bounds.Location == new Point(705, 30),
            $"season1: main-clone toggle starts at {mainCloneToggle.Bounds.Location}, expected {{X=705,Y=30}}",
            pageFailures);
        var stackingNote = Descendants(seasonOneEditor).OfType<Label>().Single(label =>
            label.Text.EndsWith("【勾选就叠加，包括星耀的】", StringComparison.Ordinal));
        RecordPageFailure(stackingNote.Bounds == new Rectangle(302, 323, 630, 13),
            $"season1: stacking note bounds are {stackingNote.Bounds}, expected {{X=302,Y=323,Width=630,Height=13}}",
            pageFailures);
        var castleOwnerToggle = seasonOneToggles.Single(toggle =>
            toggle.Text.StartsWith("获取沙城归属(", StringComparison.Ordinal));
        RecordPageFailure(castleOwnerToggle.Bounds.Location == new Point(474, 363),
            $"season1: castle-owner toggle starts at {castleOwnerToggle.Bounds.Location}, expected {{X=474,Y=363}}",
            pageFailures);
        AssertVisibleControlDpi(form, expectedDpiContext, "season1", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_season1.png");

        seasonTabs.SelectedIndex = 1;
        RefreshForSnapshot(form);
        var seasonTwoEditor = Descendants(seasonTabs.TabPages[1])
            .Single(control => control.GetType().Name == "SeasonTwoReplicaPanel");
        AssertLayoutBounds(form, seasonTwoEditor, new Rectangle(60, 135, 936, 430),
            "season2", pageFailures);
        var seasonTwoToggles = Descendants(seasonTwoEditor).OfType<CheckBox>().ToArray();
        var taoFactorToggle = seasonTwoToggles.Single(toggle =>
            toggle.Text.StartsWith("道士合击系数(", StringComparison.Ordinal));
        RecordPageFailure(taoFactorToggle.Text.EndsWith("(待重设)", StringComparison.Ordinal) &&
                          taoFactorToggle.Bounds.Location == new Point(21, 24),
            $"season2: tao-factor toggle is '{taoFactorToggle.Text}' at {taoFactorToggle.Bounds.Location}",
            pageFailures);
        const string attackTriggerNote =
            "为了修正第一季攻击触发英雄报错问题，旧版和新版互相只允许勾选一个；所有函数名字和参数参考RunQuest.pas内脚本函数，" +
            "特别注意:英雄可以利用攻击触发脚本写出英雄切割，可以用buff加切割飘血。";
        var attackTriggerLabel = Descendants(seasonTwoEditor).OfType<Label>()
            .Single(label => label.Text == attackTriggerNote);
        RecordPageFailure(attackTriggerLabel.Bounds == new Rectangle(374, 89, 560, 29),
            $"season2: attack-trigger note bounds are {attackTriggerLabel.Bounds}, expected {{X=374,Y=89,Width=560,Height=29}}",
            pageFailures);
        var wildNote = Descendants(seasonTwoEditor).OfType<Label>().Single(label =>
            label.Text == "s(1,130)=野蛮概率，\r\ns(1,131)=野蛮cd秒");
        RecordPageFailure(wildNote.Bounds == new Rectangle(786, 322, 132, 36),
            $"season2: wild-note bounds are {wildNote.Bounds}, expected {{X=786,Y=322,Width=132,Height=36}}",
            pageFailures);
        var separatorLocations = Descendants(seasonTwoEditor).OfType<Label>()
            .Where(label => label.Height == 1 && label.BackColor == Color.FromArgb(112, 112, 112))
            .Select(label => LayoutBounds(form, label).Location)
            .OrderBy(location => location.Y)
            .ToArray();
        RecordPageFailure(separatorLocations.SequenceEqual(new[] { new Point(303, 383), new Point(303, 405) }),
            "season2: recycle separators do not start at (303,383) and (303,405)", pageFailures);
        AssertVisibleControlDpi(form, expectedDpiContext, "season2", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_season2.png");

        seasonTabs.SelectedIndex = 2;
        categoryTabs.SelectedIndex = 0;
        var itemTree = Descendants(categoryTabs.TabPages[0]).OfType<TreeView>().Single();
        itemTree.SelectedNode = null;
        itemTree.SelectedNode = itemTree.Nodes[0];
        itemTree.Focus();
        RefreshForSnapshot(form);
        var selectedNodeLocation = LogicalChildLocation(form, itemTree, itemTree.SelectedNode.Bounds.Location);
        RecordPageFailure(selectedNodeLocation == new Point(100, 193),
            $"season3_root: selected root starts at {selectedNodeLocation}, expected {{X=100,Y=193}}",
            pageFailures);
        var itemRootHelp = Descendants(categoryTabs.TabPages[0]).OfType<RichTextBox>()
            .Single(control => control.Visible && control.Parent?.GetType() == typeof(Panel));
        RecordPageFailure(LayoutBounds(form, itemRootHelp).Location == new Point(329, 436),
            $"season3_root: help box starts at {LayoutBounds(form, itemRootHelp).Location}, " +
            "expected {X=329,Y=436}", pageFailures);
        AssertVisibleControlDpi(form, expectedDpiContext, "season3_root", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_season3_root.png");

        itemTree.SelectedNode = TreeNodes(itemTree.Nodes)
            .First(node => node.Text == "无限背包");
        RefreshForSnapshot(form);
        var backpackSave = Descendants(categoryTabs.TabPages[0]).OfType<Button>()
            .Single(button => button.Visible && button.Text == "保存本页配置");
        RecordPageFailure(LayoutBounds(form, backpackSave).Location == new Point(564, 408),
            $"season3_backpack: save button starts at {LayoutBounds(form, backpackSave).Location}, " +
            "expected {X=564,Y=408}", pageFailures);
        AssertVisibleControlDpi(form, expectedDpiContext, "season3_backpack", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_season3_backpack.png");

        var itemIndex = 0;
        foreach (var itemNode in TreeNodes(itemTree.Nodes).Where(node => node.Nodes.Count == 0))
        {
            itemTree.SelectedNode = itemNode;
            RefreshForSnapshot(form);
            var pageName = $"season3_item_{++itemIndex:00}";
            AssertVisibleControlDpi(form, expectedDpiContext, pageName, pageFailures);
            SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_" + pageName + ".png");
        }

        CaptureFixedCategoryPages(form, categoryTabs, 1, "roles", OriginalRoleLeafNames,
            prefix, expectedDpiContext, pageFailures);
        CaptureFixedCategoryPages(form, categoryTabs, 2, "skills", OriginalSkillLeafNames,
            prefix, expectedDpiContext, pageFailures);

        var categoryNames = new[] { "drop", "reserved" };
        for (var index = 3; index < categoryTabs.TabPages.Count; index++)
        {
            categoryTabs.SelectedIndex = index;
            var categoryTree = Descendants(categoryTabs.TabPages[index]).OfType<TreeView>().Single();
            if (index == 4)
                RecordPageFailure(categoryTree.Nodes.Count == 0,
                    "season3_reserved: the original 2.07 script tree must remain empty", pageFailures);
            else
                Assert(categoryTree.Nodes.Count > 0,
                    "season3_drop: the original 2.07 drop tree is missing");
            categoryTree.SelectedNode = categoryTree.Nodes.Count == 0 ? null : categoryTree.Nodes[0];
            categoryTree.Focus();
            RefreshForSnapshot(form);
            var pageName = "season3_" + categoryNames[index - 3];
            AssertVisibleControlDpi(form, expectedDpiContext, pageName, pageFailures);
            AssertThirdPageState(categoryTabs.TabPages[index], pageName, null, pageFailures);
            SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_" + pageName + ".png");

            var leafIndex = 0;
            foreach (var leaf in TreeNodes(categoryTree.Nodes).Where(node => node.Nodes.Count == 0))
            {
                categoryTree.SelectedNode = leaf;
                RefreshForSnapshot(form);
                var leafPageName = $"{pageName}_{++leafIndex:00}";
                AssertVisibleControlDpi(form, expectedDpiContext, leafPageName, pageFailures);
                AssertThirdPageState(categoryTabs.TabPages[index], leafPageName, leaf.Text, pageFailures);
                var categoryView = Descendants(categoryTabs.TabPages[index])
                    .Single(control => control.GetType().Name == "ExtensionCategoryView");
                var selectedKeyCount = (int)(categoryView.GetType()
                    .GetProperty("SelectedConfigKeyCount")?.GetValue(categoryView) ?? 0);
                RecordPageFailure(selectedKeyCount > 0,
                    $"{leafPageName}: selecting '{leaf.Text}' did not bind a configuration key", pageFailures);
                SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_" + leafPageName + ".png");
            }
        }

        rootTabs.SelectedIndex = 0;
        gmTabs.SelectedIndex = 0;
        RefreshForSnapshot(form);
        RecordPageFailure(Descendants(gmTabs.TabPages[0]).Any(control =>
                control.Text == "重载Mir200\\Envir\\PsNpcscripts下的所有npc脚本！"),
            "gm: NPC reload description does not match the original PsNpcscripts spelling",
            pageFailures);
        AssertVisibleControlDpi(form, expectedDpiContext, "gm", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_gm.png");

        gmTabs.SelectedIndex = 1;
        RefreshForSnapshot(form);
        var paymentAddress = Descendants(gmTabs.TabPages[1]).OfType<TextBox>()
            .Single(textBox => textBox.ReadOnly && textBox.Text.StartsWith("https://", StringComparison.Ordinal));
        AssertLayoutBounds(form, paymentAddress, new Rectangle(395, 151, 294, 23),
            "payment/address", pageFailures);
        AssertVisibleControlDpi(form, expectedDpiContext, "payment", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_payment.png");

        gmTabs.SelectedIndex = 2;
        RefreshForSnapshot(form);
        AssertVisibleControlDpi(form, expectedDpiContext, "gm_reserved", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_gm_reserved.png");

        rootTabs.SelectedIndex = 3;
        RefreshForSnapshot(form);
        AssertVisibleControlDpi(form, expectedDpiContext, "help", pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_help.png");
        Console.WriteLine($"all-page raw snapshots: {prefix}_*.png");
        Assert(pageFailures.Count == 0,
            "all-page audit failures:" + Environment.NewLine +
            string.Join(Environment.NewLine, pageFailures.Select(failure => "- " + failure)));
    }

    private static void CaptureFixedCategoryPages(
        Form form,
        TabControl categoryTabs,
        int categoryIndex,
        string categoryName,
        IReadOnlyList<string> expectedLeafNames,
        string prefix,
        IntPtr expectedDpiContext,
        List<string> pageFailures)
    {
        categoryTabs.SelectedIndex = categoryIndex;
        var categoryPage = categoryTabs.TabPages[categoryIndex];
        var categoryTree = Descendants(categoryPage).OfType<TreeView>().Single();
        var leaves = TreeNodes(categoryTree.Nodes).Where(node => node.Nodes.Count == 0).ToArray();
        Assert(leaves.Length == expectedLeafNames.Count,
            $"season3_{categoryName}: expected {expectedLeafNames.Count} fixed leaves, got {leaves.Length}");
        Assert(expectedLeafNames.All(name => leaves.Count(node => node.Text == name) == 1),
            $"season3_{categoryName}: fixed leaf names were missing or duplicated");

        categoryTree.SelectedNode = categoryTree.Nodes[0];
        categoryTree.Focus();
        RefreshForSnapshot(form);
        var rootPageName = "season3_" + categoryName;
        AssertVisibleControlDpi(form, expectedDpiContext, rootPageName, pageFailures);
        AssertThirdPageState(categoryPage, rootPageName, null, pageFailures);
        SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_" + rootPageName + ".png");

        foreach (var group in TreeNodes(categoryTree.Nodes).Where(node => node.Nodes.Count > 0).Skip(1))
        {
            categoryTree.SelectedNode = group;
            RefreshForSnapshot(form);
            AssertThirdPageState(categoryPage, rootPageName + "/" + group.Text, null, pageFailures);
        }

        for (var index = 0; index < expectedLeafNames.Count; index++)
        {
            var leafName = expectedLeafNames[index];
            var leaf = leaves.Single(node => node.Text == leafName);
            categoryTree.SelectedNode = leaf;
            RefreshForSnapshot(form);
            var leafPageName = $"{rootPageName}_{index + 1:00}";
            AssertVisibleControlDpi(form, expectedDpiContext, leafPageName, pageFailures);
            AssertThirdPageState(categoryPage, leafPageName, leafName, pageFailures);
            var categoryView = Descendants(categoryPage)
                .Single(control => control.GetType().Name == "ExtensionCategoryView");
            var selectedKeyCount = (int)(categoryView.GetType()
                .GetProperty("SelectedConfigKeyCount")?.GetValue(categoryView) ?? 0);
            RecordPageFailure(selectedKeyCount > 0,
                $"{leafPageName}: selecting '{leafName}' did not bind a configuration key", pageFailures);
            SaveRawPrintWindow(form.Handle, form.ClientSize, prefix + "_" + leafPageName + ".png");
        }
    }

    private static void AssertThirdPageState(
        Control categoryPage, string pageName, string? selectedLeaf, List<string> pageFailures)
    {
        var categoryView = Descendants(categoryPage)
            .Single(control => control.GetType().Name == "ExtensionCategoryView");
        var skillPage = categoryPage.Text == "技能相关";
        RecordPageFailure(!skillPage || Descendants(categoryPage).OfType<ListView>().All(view => !view.Visible),
            $"{pageName}: skill page exposed a ListView that the original GUI does not show", pageFailures);

        if (selectedLeaf == null)
        {
            var visibleParameterAreas = Descendants(categoryView)
                .Where(control => control.Visible &&
                    (control.GetType().Name == "ReplicaConfigPanel" ||
                     control.GetType().Name == "BackpackReplicaPanel"))
                .ToArray();
            RecordPageFailure(visibleParameterAreas.Length == 0,
                $"{pageName}: root/group selection exposed an incorrect right-side parameter area", pageFailures);

            var rootHelp = Descendants(categoryView).OfType<RichTextBox>()
                .SingleOrDefault(control => control.Visible && control.Parent?.GetType() == typeof(Panel));
            var itemRoot = string.Equals(categoryPage.Text, "物品相关", StringComparison.Ordinal);
            RecordPageFailure(rootHelp != null && (itemRoot
                    ? rootHelp.Text.StartsWith("请选择左边的相关说明", StringComparison.Ordinal)
                    : string.IsNullOrEmpty(rootHelp.Text)),
                $"{pageName}: root/group help text does not match the original page", pageFailures);
        }

        var buttons = Descendants(categoryView).OfType<Button>().ToArray();
        var create = buttons.Single(button => button.Text == "创建额外技能配置");
        var reload = buttons.Single(button => button.Text == "重载技能配置文件");
        var expectSkillCommands = skillPage &&
            (selectedLeaf == "额外技能" || selectedLeaf == "怪物伤害触发技能特效");
        RecordPageFailure(create.Visible == expectSkillCommands && reload.Visible == expectSkillCommands,
            $"{pageName}: create/reload visibility did not match the two original skill pages", pageFailures);

        var clippedParameterLabels = Descendants(categoryView)
            .OfType<Label>()
            .Where(label => label.Visible &&
                label.Parent?.GetType().Name == "ReplicaConfigPanel" &&
                label.PreferredWidth > label.ClientSize.Width + 2)
            .Select(label => $"{label.Text} ({label.PreferredWidth}>{label.ClientSize.Width})")
            .ToArray();
        RecordPageFailure(clippedParameterLabels.Length == 0,
            $"{pageName}: specialized parameter labels do not fit one line: " +
            string.Join(", ", clippedParameterLabels), pageFailures);

        if (selectedLeaf != null && OriginalHelpFragments.TryGetValue(selectedLeaf, out var expectedFragments))
        {
            var help = Descendants(categoryView).OfType<RichTextBox>()
                .SingleOrDefault(control => control.Visible && control.Parent?.GetType() == typeof(Panel));
            RecordPageFailure(help != null,
                $"{pageName}: visible category help box is missing", pageFailures);
            foreach (var fragment in expectedFragments)
                RecordPageFailure(help != null && help.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"{pageName}: '{selectedLeaf}' help is missing original text fragment '{fragment}'", pageFailures);
        }

        if (selectedLeaf == "诱惑之光修改")
            RecordPageFailure(Descendants(categoryView).OfType<Label>().Any(label =>
                    label.Visible && label.Text == "注:每1级提升一个宝宝数量:"),
                $"{pageName}: the original level-based summon-count hint is missing", pageFailures);

        if (selectedLeaf == "主号新切割")
        {
            var parameterPanel = Descendants(categoryView).Single(control =>
                control.Visible && control.GetType().Name == "ReplicaConfigPanel");
            var scriptLabel = Descendants(parameterPanel).OfType<Label>().Single(label =>
                label.Visible && label.Text == "是否触发脚本:");
            RecordPageFailure(scriptLabel.Bounds == new Rectangle(379, 49, 111, 23),
                $"{pageName}: 主号新切割 script label bounds are {scriptLabel.Bounds}, " +
                "expected {X=379,Y=49,Width=111,Height=23}", pageFailures);
        }

        var attached = buttons.Where(button => button.Text.StartsWith("附加按钮", StringComparison.Ordinal))
            .ToArray();
        RecordPageFailure(attached.All(button => !button.Visible),
            $"{pageName}: 附加按钮1-10 must remain hidden", pageFailures);
    }

    private static void AssertVisibleControlDpi(
        Control form, IntPtr expectedDpiContext, string pageName, List<string> pageFailures)
    {
        var checkedControls = 0;
        foreach (var control in Descendants(form)
                     .Where(control => control.Visible && control.Width > 0 && control.Height > 0))
        {
            var handle = control.Handle;
            var actual = NativeMethods.GetWindowDpiAwarenessContext(handle);
            RecordPageFailure(NativeMethods.AreDpiAwarenessContextsEqual(actual, expectedDpiContext),
                $"{pageName}: visible {control.GetType().Name} '{control.Text}' escaped the expected DPI context",
                pageFailures);
            checkedControls++;
        }
        Console.WriteLine($"{pageName}: visible DPI handles={checkedControls} exact=unaware");
    }

    private static void RecordPageFailure(bool condition, string message, List<string> pageFailures)
    {
        if (!condition) pageFailures.Add(message);
    }

    private static void AssertLayoutBounds(
        Form form, Control control, Rectangle expected, string pageName, List<string> pageFailures)
    {
        var actual = LayoutBounds(form, control);
        RecordPageFailure(actual == expected,
            $"{pageName}: expected bounds {expected}, got {actual}", pageFailures);
    }

    private static Rectangle LayoutBounds(Form form, Control control)
    {
        var location = control.Parent == null
            ? control.Location
            : LogicalChildLocation(form, control.Parent, control.Location);
        return new Rectangle(location, control.Size);
    }

    private static Point LogicalChildLocation(Form form, Control parent, Point childLocation)
    {
        var formOrigin = form.PointToScreen(Point.Empty);
        var parentOrigin = parent.PointToScreen(Point.Empty);
        return new Point(
            (parentOrigin.X - formOrigin.X) / 2 + childLocation.X,
            (parentOrigin.Y - formOrigin.Y) / 2 + childLocation.Y);
    }

    private static void RefreshForSnapshot(Form form)
    {
        form.BringToFront();
        form.Refresh();
        foreach (var control in Descendants(form).Where(control => control.Visible))
            control.Refresh();
        Application.DoEvents();
    }

    private static void SaveRawPrintWindow(IntPtr handle, Size logicalSize, string path)
    {
        using var bitmap = new Bitmap(logicalSize.Width, logicalSize.Height);
        bitmap.SetResolution(96f, 96f);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var deviceContext = graphics.GetHdc();
            try
            {
                Assert(NativeMethods.PrintWindow(handle, deviceContext, 2),
                    "PrintWindow(PW_RENDERFULLCONTENT) failed for " + path);
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static string SnapshotVariant(string path, string suffix)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        return Path.Combine(directory,
            Path.GetFileNameWithoutExtension(path) + "_" + suffix + Path.GetExtension(path));
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static IEnumerable<TreeNode> TreeNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var descendant in TreeNodes(node.Nodes)) yield return descendant;
        }
    }

    private static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }

    private static void CopyMyJsonFixture(string sourceRuntime, string runtime)
    {
        var source = Path.Combine(sourceRuntime, "MyJson");
        var target = Path.Combine(runtime, "MyJson");
        if (Directory.Exists(source))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            return;
        }

        var path = Path.Combine(runtime, "MyJson", "items", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "{\"无限背包_变量v1\":10,\"无限背包_变量v2\":1," +
            "\"无限背包_额外格子\":192,\"无限背包_是否勾选\":1," +
            "\"无限背包_是否固定\":\"固定格子\"}",
            Encoding.GetEncoding(936));
    }

    private static void PrepareGameSvrRuntimeFiles()
    {
        var runtimeDirectory = AppContext.BaseDirectory;
        File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"), "[Server]" + Environment.NewLine);
        File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"), "[Command]" + Environment.NewLine);
        var shareDirectory = Path.Combine(Path.GetFullPath(Path.Combine(runtimeDirectory, "..")), "Share");
        Directory.CreateDirectory(shareDirectory);
        File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
            "[PlayerLevelExp]" + Environment.NewLine);
        File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"), "[Integer]" + Environment.NewLine);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ThreadDpiScope : IDisposable
    {
        private readonly IntPtr _previous;

        public ThreadDpiScope(IntPtr context)
        {
            _previous = NativeMethods.SetThreadDpiAwarenessContext(context);
            if (_previous == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (NativeMethods.SetThreadDpiAwarenessContext(_previous) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private readonly record struct WindowEvidence(DpiAwareness Awareness, uint Dpi, int Width, int Height)
    {
        public static WindowEvidence Read(IntPtr handle)
        {
            var context = NativeMethods.GetWindowDpiAwarenessContext(handle);
            var awareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(context);
            if (!NativeMethods.GetWindowRect(handle, out var bounds))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new WindowEvidence(awareness, NativeMethods.GetDpiForWindow(handle),
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
    }

    private enum DpiAwareness
    {
        Invalid = -1,
        Unaware = 0,
        SystemAware = 1,
        PerMonitorAware = 2,
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

        [DllImport("user32.dll")]
        internal static extern DpiAwareness GetAwarenessFromDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
