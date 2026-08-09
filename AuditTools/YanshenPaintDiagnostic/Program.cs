using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace YanshenPaintDiagnostic;

internal static class Program
{
    private static readonly List<Exception> PaintExceptions = new();
    private static readonly HashSet<string> PrintedExceptions = new(StringComparer.Ordinal);

    [STAThread]
    private static int Main(string[] args)
    {
        var assemblyDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : @"D:\loym2\Build\Mir200";
        var runtimeDirectory = args.Length > 1
            ? Path.GetFullPath(args[1])
            : @"D:\lyom2Release\mud2.0\Mir200\GS1";
        var initializationOrder = args.Length > 2 ? args[2] : "production";
        var requestedScale = args.Length > 3 && float.TryParse(args[3], out var parsedScale)
            ? parsedScale
            : 1f;
        var forcedFlatStyle = args.Length > 4 ? args[4] : "unchanged";
        var requestedDpiMode = args.Length > 5 &&
                               Enum.TryParse<HighDpiMode>(args[5], ignoreCase: true, out var parsedDpiMode)
            ? parsedDpiMode
            : HighDpiMode.SystemAware;
        var formCreationMode = args.Length > 6 ? args[6] : "direct";

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        PrepareRuntimeConfig();
        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            var buildCandidate = Path.Combine(assemblyDirectory, assemblyName.Name + ".dll");
            if (File.Exists(buildCandidate))
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(buildCandidate);
            var runtimeCandidate = Path.Combine(runtimeDirectory, assemblyName.Name + ".dll");
            return File.Exists(runtimeCandidate)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(runtimeCandidate)
                : null;
        };

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
        {
            PaintExceptions.Add(eventArgs.Exception);
            var key = eventArgs.Exception.GetType().FullName + ":" + eventArgs.Exception.Message;
            if (PrintedExceptions.Add(key)) Console.Error.WriteLine("THREAD_EXCEPTION " + eventArgs.Exception);
        };

        bool dpiModeApplied;
        if (string.Equals(initializationOrder, "production", StringComparison.OrdinalIgnoreCase))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            dpiModeApplied = Application.SetHighDpiMode(requestedDpiMode);
        }
        else
        {
            dpiModeApplied = Application.SetHighDpiMode(requestedDpiMode);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        Console.WriteLine($"order={initializationOrder} setHighDpiMode={dpiModeApplied} " +
                          $"applicationDpiMode={Application.HighDpiMode} requestedScale={requestedScale}");

        try
        {
            var gameAssemblyPath = Path.Combine(assemblyDirectory, "GameSvr.dll");
            Console.WriteLine($"assembly={gameAssemblyPath} runtime={runtimeDirectory}");
            var gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(gameAssemblyPath);
            var managerType = RequiredType(gameAssembly, "GameSvr.Plugins.PluginManager");
            var formType = RequiredType(gameAssembly, "GameSvr.Plugins.YanshenConfigForm");
            var mir200Directory = Directory.GetParent(runtimeDirectory)?.FullName ?? runtimeDirectory;
            var envirDirectory = Path.Combine(mir200Directory, "Envir");
            var manager = Activator.CreateInstance(managerType, envirDirectory, runtimeDirectory)
                          ?? throw new InvalidOperationException("PluginManager construction returned null");

            var created = CreateAndShowForm(
                gameAssembly, formType, manager, forcedFlatStyle, formCreationMode);
            using var form = created.Form;
            var buttons = created.Buttons;

            var configKeyCount = (int)(formType.GetProperty("ConfigKeyCount")?.GetValue(form) ?? -1);
            var assignedConfigKeyCount =
                (int)(formType.GetProperty("AssignedConfigKeyCount")?.GetValue(form) ?? -1);
            Console.WriteLine($"configKeys={configKeyCount} assignedConfigKeys={assignedConfigKeyCount}");
            if (configKeyCount != 379 || assignedConfigKeyCount != 379)
                throw new InvalidOperationException(
                    $"full configuration was not loaded ({assignedConfigKeyCount}/{configKeyCount})");

            if (Math.Abs(requestedScale - 1f) > 0.001f)
            {
                form.Scale(new SizeF(requestedScale, requestedScale));
                Application.DoEvents();
            }

            Console.WriteLine($"form deviceDpi={form.DeviceDpi} client={form.ClientSize.Width}x{form.ClientSize.Height} " +
                              $"font={form.Font.Name}/{form.Font.SizeInPoints:0.##}");
            var windowContext = GetWindowDpiAwarenessContext(form.Handle);
            var windowContextUnaware =
                AreDpiAwarenessContextsEqual(windowContext, DpiAwarenessContextUnaware);
            Console.WriteLine($"windowDpi={GetDpiForWindow(form.Handle)} " +
                              $"windowContextUnaware={windowContextUnaware}");
            Console.WriteLine($"classicButtons={buttons.Length} forcedFlatStyle={forcedFlatStyle} " +
                              $"formCreationMode={formCreationMode}");
            if (buttons.Length < 24)
                throw new InvalidOperationException($"expected at least 24 ClassicButton controls, got {buttons.Length}");
            var paintedPages = ExerciseAllTabPages(form);
            Console.WriteLine($"paintedTabPages={paintedPages}");
            if (paintedPages != 21)
                throw new InvalidOperationException($"expected 21 nested tab selections, got {paintedPages}");
            AuditCompatibleManagedText(form);
            AuditTextRendererFonts(form);

            var totalRedPixels = 0;
            foreach (var button in buttons)
            {
                button.Invalidate(true);
                button.Update();
                Application.DoEvents();
                using var bitmap = new Bitmap(Math.Max(1, button.Width), Math.Max(1, button.Height));
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Magenta);
                button.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                var redPixels = CountPureRed(bitmap);
                var paintedPixels = CountPaintedPixels(bitmap);
                var darkInteriorPixels = CountDarkInteriorPixels(bitmap);
                totalRedPixels += redPixels;
                Console.WriteLine($"button text={button.Text} visible={button.Visible} bounds={button.Bounds} " +
                                  $"font={button.Font.Name}/{button.Font.SizeInPoints:0.##} " +
                                  $"flatStyle={button.FlatStyle} visualBack={button.UseVisualStyleBackColor} " +
                                  $"redPixels={redPixels} paintedPixels={paintedPixels} " +
                                  $"darkInteriorPixels={darkInteriorPixels}");
                var totalPixels = bitmap.Width * bitmap.Height;
                if (paintedPixels < totalPixels * 4 / 5)
                    throw new InvalidOperationException(
                        $"ClassicButton '{button.Text}' was blank/transparent ({paintedPixels}/{totalPixels})");
                if (!string.IsNullOrWhiteSpace(button.Text) && darkInteriorPixels < 5)
                    throw new InvalidOperationException(
                        $"ClassicButton '{button.Text}' did not render visible text ({darkInteriorPixels} dark pixels)");
            }
            Console.WriteLine($"classicButtonRedPixels={totalRedPixels}");
            if (totalRedPixels != 0)
                throw new InvalidOperationException($"ClassicButton red error pixels remain: {totalRedPixels}");

            using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                var output = Path.Combine(AppContext.BaseDirectory,
                    $"paint-{initializationOrder}-{Application.HighDpiMode}-" +
                    $"{formCreationMode}-{requestedScale:0.##}-{forcedFlatStyle}.png");
                bitmap.Save(output);
                Console.WriteLine("snapshot=" + output);
            }

            form.Hide();
            return PaintExceptions.Count == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FATAL " + exception);
            return 1;
        }
        finally
        {
            Console.WriteLine("paintExceptions=" + PaintExceptions.Count);
        }
    }

    private static Type RequiredType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true)
        ?? throw new TypeLoadException(name);

    private static (Form Form, Button[] Buttons) CreateAndShowForm(
        Assembly gameAssembly,
        Type formType,
        object manager,
        string forcedFlatStyle,
        string creationMode)
    {
        IDisposable? dpiScope = null;
        try
        {
            if (string.Equals(creationMode, "production-scope", StringComparison.OrdinalIgnoreCase))
            {
                var nativeMethods = RequiredType(gameAssembly, "GameSvr.Plugins.NativeMethods");
                var enter = nativeMethods.GetMethod(
                                "EnterDpiUnaware",
                                BindingFlags.Static | BindingFlags.NonPublic)
                            ?? throw new MissingMethodException(
                                nativeMethods.FullName,
                                "EnterDpiUnaware");
                dpiScope = (IDisposable?)(enter.Invoke(null, null)
                           ?? throw new InvalidOperationException("DPI scope construction returned null"));
            }
            else if (string.Equals(creationMode, "unaware-scope", StringComparison.OrdinalIgnoreCase))
            {
                var previous = SetThreadDpiAwarenessContext(DpiAwarenessContextUnaware);
                if (previous == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"Unable to enter ordinary DPI-unaware context (Win32 {Marshal.GetLastWin32Error()})");
                dpiScope = new ThreadDpiScope(previous);
            }

            var form = (Form)(Activator.CreateInstance(formType, manager)
                       ?? throw new InvalidOperationException("YanshenConfigForm construction returned null"));
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(40, 40);
            var buttons = Descendants(form)
                .Where(control => control.GetType().Name == "ClassicButton")
                .Cast<Button>()
                .ToArray();
            if (Enum.TryParse<FlatStyle>(forcedFlatStyle, ignoreCase: true, out var flatStyle))
                foreach (var button in buttons) button.FlatStyle = flatStyle;
            form.Show();
            Application.DoEvents();
            return (form, buttons);
        }
        finally
        {
            dpiScope?.Dispose();
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static int CountPureRed(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R == 255 && color.G == 0 && color.B == 0) count++;
        }
        return count;
    }

    private static void AuditCompatibleManagedText(Form form)
    {
        var controls = Descendants(form).ToArray();
        var labels = controls.OfType<Label>().ToArray();
        var checkBoxes = controls.OfType<CheckBox>().ToArray();
        var incompatibleLabels = labels.Count(label => !label.UseCompatibleTextRendering);
        var incompatibleCheckBoxes = checkBoxes.Count(checkBox => !checkBox.UseCompatibleTextRendering);
        Console.WriteLine($"managedText labels={labels.Length} checkBoxes={checkBoxes.Length} " +
                          $"incompatibleLabels={incompatibleLabels} " +
                          $"incompatibleCheckBoxes={incompatibleCheckBoxes}");
        if (incompatibleLabels != 0 || incompatibleCheckBoxes != 0)
            throw new InvalidOperationException(
                $"managed compatible text was not applied: labels={incompatibleLabels}, " +
                $"checkBoxes={incompatibleCheckBoxes}");
    }

    private static void AuditTextRendererFonts(Form form)
    {
        var specifications = new[]
        {
            new RendererFontSpec("ReplicaConfigPanel", "_bodyFont", 6f),
            new RendererFontSpec("ReplicaConfigPanel", "_compactFont", 5f),
            new RendererFontSpec("ReplicaConfigPanel", "_headingFont", 6f),
            new RendererFontSpec("EquipmentReplicaPanel", "_blueFont", 6f),
            new RendererFontSpec("Config12ReplicaPanelBase", "_drawFont", 5.5f),
        };
        var observed = new Dictionary<(string Type, string Field), int>();

        foreach (var control in Descendants(form).Prepend(form))
        foreach (var specification in specifications)
        {
            var declaringType = FindTypeInHierarchy(control.GetType(), specification.TypeName);
            if (declaringType == null) continue;
            var field = declaringType.GetField(
                            specification.FieldName,
                            BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingFieldException(declaringType.FullName, specification.FieldName);
            var font = field.GetValue(control) as Font
                       ?? throw new InvalidOperationException(
                           $"{declaringType.Name}.{specification.FieldName} was not a Font");
            Console.WriteLine($"rendererFont type={control.GetType().Name} " +
                              $"field={specification.FieldName} family={font.Name} " +
                              $"size={font.Size:0.###} unit={font.Unit} style={font.Style} " +
                              $"controlDpi={control.DeviceDpi}");
            if (font.Unit != GraphicsUnit.Pixel || Math.Abs(font.Size - specification.ExpectedPixels) > 0.01f)
                throw new InvalidOperationException(
                    $"{control.GetType().Name}.{specification.FieldName} expected " +
                    $"{specification.ExpectedPixels}px, got {font.Size:0.###} {font.Unit}");
            var key = (specification.TypeName, specification.FieldName);
            observed[key] = observed.GetValueOrDefault(key) + 1;
        }

        foreach (var specification in specifications)
            if (!observed.ContainsKey((specification.TypeName, specification.FieldName)))
                throw new InvalidOperationException(
                    $"renderer font was not observed: {specification.TypeName}.{specification.FieldName}");
    }

    private static Type? FindTypeInHierarchy(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (string.Equals(current.Name, name, StringComparison.Ordinal)) return current;
        return null;
    }

    private static int CountPaintedPixels(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.A != 255 || color.R != 255 || color.G != 0 || color.B != 255) count++;
        }
        return count;
    }

    private static int CountDarkInteriorPixels(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 3; y < bitmap.Height - 3; y++)
        for (var x = 3; x < bitmap.Width - 3; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.A >= 192 && color.R < 128 && color.G < 128 && color.B < 128) count++;
        }
        return count;
    }

    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    private sealed class ThreadDpiScope : IDisposable
    {
        private readonly IntPtr _previous;

        internal ThreadDpiScope(IntPtr previous) => _previous = previous;

        public void Dispose() => SetThreadDpiAwarenessContext(_previous);
    }

    private sealed record RendererFontSpec(string TypeName, string FieldName, float ExpectedPixels);

    private static int ExerciseAllTabPages(Form form)
    {
        var count = 0;
        Visit(form);
        return count;

        void Visit(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is not TabControl tabs)
                {
                    Visit(child);
                    continue;
                }

                var originalIndex = tabs.SelectedIndex;
                for (var index = 0; index < tabs.TabPages.Count; index++)
                {
                    tabs.SelectedIndex = index;
                    Application.DoEvents();
                    form.Invalidate(true);
                    form.Update();
                    using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    count++;
                    Visit(tabs.TabPages[index]);
                }
                tabs.SelectedIndex = originalIndex;
                Application.DoEvents();
            }
        }
    }

    private static void PrepareRuntimeConfig()
    {
        var runtimeDirectory = AppContext.BaseDirectory;
        File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"), "[Server]" + Environment.NewLine);
        File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"), "[Command]" + Environment.NewLine);
        var shareDirectory = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "Share"));
        Directory.CreateDirectory(shareDirectory);
        File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
            "[PlayerLevelExp]" + Environment.NewLine);
        File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"), "[Integer]" + Environment.NewLine);
    }
}
