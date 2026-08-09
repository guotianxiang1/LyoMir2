using System.Runtime.InteropServices;
using System.Text.Json;

namespace WinFormsDpiTextProbe;

internal static class Program
{
    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);
    private static readonly List<Exception> ThreadExceptions = new();

    [STAThread]
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "target96";
        var hybrid = string.Equals(mode, "hybrid-unaware", StringComparison.OrdinalIgnoreCase);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ThreadExceptions.Add(eventArgs.Exception);
        var applied = Application.SetHighDpiMode(
            hybrid ? HighDpiMode.SystemAware : HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        ProbeForm form;
        IDisposable? scope = null;
        Console.WriteLine($"GetDpiForSystem before form scope={GetDpiForSystem()}");
        try
        {
            if (hybrid)
            {
                var previous = SetThreadDpiAwarenessContext(DpiAwarenessContextUnaware);
                if (previous == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"SetThreadDpiAwarenessContext(-1) failed: {Marshal.GetLastWin32Error()}");
                scope = new DpiScope(previous);
                Console.WriteLine($"GetDpiForSystem inside -1 scope={GetDpiForSystem()}");
            }

            form = new ProbeForm();
            form.Show();
            Application.DoEvents();
        }
        finally
        {
            scope?.Dispose();
        }

        using (form)
        {
            var windowContext = GetWindowDpiAwarenessContext(form.Handle);
            var windowUnaware =
                AreDpiAwarenessContextsEqual(windowContext, DpiAwarenessContextUnaware);
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "captures");
            Directory.CreateDirectory(outputDirectory);
            var metrics = new List<ProbeMetric>();

            foreach (var definition in form.Definitions)
            {
                definition.Control.Invalidate(true);
                definition.Control.Update();
                Application.DoEvents();

                using var bitmap = new Bitmap(definition.Control.Width, definition.Control.Height);
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Magenta);
                definition.Control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                var glyph = MeasureGlyph(bitmap, definition.TextRegion);
                var path = Path.Combine(outputDirectory, $"{mode}-{definition.Name}.png");
                bitmap.Save(path);
                metrics.Add(new ProbeMetric(
                    definition.Name,
                    definition.Kind,
                    definition.FontPixels,
                    definition.CompatibleText,
                    bitmap.Width,
                    bitmap.Height,
                    glyph.Bounds.X,
                    glyph.Bounds.Y,
                    glyph.Bounds.Width,
                    glyph.Bounds.Height,
                    glyph.DarkPixelCount));
                Console.WriteLine(
                    $"{definition.Name,-22} kind={definition.Kind,-8} pixel={definition.FontPixels,2} " +
                    $"compat={definition.CompatibleText,-5} glyph={Format(glyph.Bounds),-14} " +
                    $"dark={glyph.DarkPixelCount}");
            }

            using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(outputDirectory, $"{mode}-all.png"));
            }

            var result = new ProbeResult(
                mode,
                applied,
                Application.HighDpiMode.ToString(),
                form.DeviceDpi,
                GetDpiForWindow(form.Handle),
                windowUnaware,
                ThreadExceptions.Select(exception => exception.ToString()).ToArray(),
                metrics);
            var jsonPath = Path.Combine(outputDirectory, $"{mode}-metrics.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
            Console.WriteLine($"mode={mode} appDpi={Application.HighDpiMode} formDeviceDpi={form.DeviceDpi} " +
                              $"windowDpi={GetDpiForWindow(form.Handle)} windowUnaware={windowUnaware} " +
                              $"threadExceptions={ThreadExceptions.Count}");
            Console.WriteLine("metrics=" + jsonPath);
            return ThreadExceptions.Count == 0 && metrics.All(metric => metric.DarkPixels > 0) ? 0 : 1;
        }
    }

    private static GlyphMetric MeasureGlyph(Bitmap bitmap, Rectangle region)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        var count = 0;
        var clipped = Rectangle.Intersect(new Rectangle(Point.Empty, bitmap.Size), region);

        for (var y = clipped.Top; y < clipped.Bottom; y++)
        for (var x = clipped.Left; x < clipped.Right; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.A < 192 || color.R >= 160 || color.G >= 160 || color.B >= 160) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
            count++;
        }

        return count == 0
            ? new GlyphMetric(Rectangle.Empty, 0)
            : new GlyphMetric(Rectangle.FromLTRB(left, top, right + 1, bottom + 1), count);
    }

    private static string Format(Rectangle rectangle) =>
        $"{rectangle.X},{rectangle.Y},{rectangle.Width}x{rectangle.Height}";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private sealed class DpiScope : IDisposable
    {
        private readonly IntPtr _previous;

        internal DpiScope(IntPtr previous) => _previous = previous;

        public void Dispose() => SetThreadDpiAwarenessContext(_previous);
    }

    private sealed class ProbeForm : Form
    {
        private const string SampleText = "眼神文字Abc123";
        private readonly Font _pixel12 = new("SimSun", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _pixel6 = new("SimSun", 6f, FontStyle.Regular, GraphicsUnit.Pixel);

        internal ProbeForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            ClientSize = new Size(600, 366);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 40);
            Text = "WinForms DPI text probe";

            AddLabel("label-gdi-px12", false, _pixel12, 12, 12);
            AddLabel("label-gdiplus-px12", true, _pixel12, 12, 46);
            AddLabel("label-gdi-px6", false, _pixel6, 6, 80);
            AddLabel("label-gdiplus-px6", true, _pixel6, 6, 114);
            AddCheckBox("check-gdi-px12", false, _pixel12, 12, 148);
            AddCheckBox("check-gdiplus-px12", true, _pixel12, 12, 182);
            AddCheckBox("check-gdi-px6", false, _pixel6, 6, 216);
            AddCheckBox("check-gdiplus-px6", true, _pixel6, 6, 250);
            AddCustomTextRenderer("custom-tr-px12", _pixel12, 12, 148);
            AddCustomTextRenderer("custom-tr-px6", _pixel6, 6, 182);
            AddCustomDrawString("custom-gdiplus-px12", _pixel12, 12, 216);
            AddScopedTextRenderer("custom-tr-scoped-px12", _pixel12, 12, 250);

            var button = new ClassicProbeButton
            {
                Bounds = new Rectangle(12, 284, 270, 28),
                Font = _pixel12,
                TabStop = false,
                Text = SampleText,
                UseVisualStyleBackColor = false,
            };
            Controls.Add(button);
            Definitions.Add(new ProbeDefinition(
                "button-system-px12", "Button", 12, false, button,
                new Rectangle(4, 4, button.Width - 8, button.Height - 8)));

            var textBox = new TextBox
            {
                Bounds = new Rectangle(12, 324, 270, 28),
                Font = _pixel12,
                TabStop = false,
                Text = SampleText,
            };
            Controls.Add(textBox);
            Definitions.Add(new ProbeDefinition(
                "textbox-px12", "TextBox", 12, false, textBox,
                new Rectangle(4, 3, textBox.Width - 8, textBox.Height - 6)));
        }

        internal List<ProbeDefinition> Definitions { get; } = new();

        private void AddLabel(string name, bool compatible, Font font, int pixels, int y)
        {
            var label = new Label
            {
                AutoSize = false,
                BackColor = Color.White,
                Bounds = new Rectangle(12, y, 270, 28),
                Font = font,
                ForeColor = Color.Black,
                TabStop = false,
                Text = SampleText,
                TextAlign = ContentAlignment.MiddleLeft,
                UseCompatibleTextRendering = compatible,
            };
            Controls.Add(label);
            Definitions.Add(new ProbeDefinition(
                name, "Label", pixels, compatible, label,
                new Rectangle(0, 0, label.Width, label.Height)));
        }

        private void AddCheckBox(string name, bool compatible, Font font, int pixels, int y)
        {
            var checkBox = new CheckBox
            {
                AutoSize = false,
                BackColor = Color.White,
                Bounds = new Rectangle(312, y - 136, 270, 28),
                Checked = true,
                Font = font,
                ForeColor = Color.Black,
                TabStop = false,
                Text = SampleText,
                TextAlign = ContentAlignment.MiddleLeft,
                UseCompatibleTextRendering = compatible,
            };
            Controls.Add(checkBox);
            Definitions.Add(new ProbeDefinition(
                name, "CheckBox", pixels, compatible, checkBox,
                new Rectangle(22, 0, checkBox.Width - 22, checkBox.Height)));
        }

        private void AddCustomTextRenderer(string name, Font font, int pixels, int y)
        {
            var control = new TextRendererProbeControl
            {
                BackColor = Color.White,
                Bounds = new Rectangle(12, y, 270, 28),
                Font = font,
                ForeColor = Color.Black,
                Text = SampleText,
            };
            Controls.Add(control);
            Definitions.Add(new ProbeDefinition(
                name, "CustomTR", pixels, false, control,
                new Rectangle(0, 0, control.Width, control.Height)));
        }

        private void AddCustomDrawString(string name, Font font, int pixels, int y)
        {
            var control = new DrawStringProbeControl
            {
                BackColor = Color.White,
                Bounds = new Rectangle(12, y, 270, 28),
                Font = font,
                ForeColor = Color.Black,
                Text = SampleText,
            };
            Controls.Add(control);
            Definitions.Add(new ProbeDefinition(
                name, "CustomG+", pixels, true, control,
                new Rectangle(0, 0, control.Width, control.Height)));
        }

        private void AddScopedTextRenderer(string name, Font font, int pixels, int y)
        {
            var control = new ScopedTextRendererProbeControl
            {
                BackColor = Color.White,
                Bounds = new Rectangle(12, y, 270, 28),
                Font = font,
                ForeColor = Color.Black,
                Text = SampleText,
            };
            Controls.Add(control);
            Definitions.Add(new ProbeDefinition(
                name, "ScopedTR", pixels, false, control,
                new Rectangle(0, 0, control.Width, control.Height)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pixel12.Dispose();
                _pixel6.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class ClassicProbeButton : Button
    {
        internal ClassicProbeButton() => FlatStyle = FlatStyle.System;

        protected override void OnHandleCreated(EventArgs eventArgs)
        {
            base.OnHandleCreated(eventArgs);
            SetWindowTheme(Handle, string.Empty, string.Empty);
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr handle, string subAppName, string subIdList);
    }

    private sealed class TextRendererProbeControl : Control
    {
        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(BackColor);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                BackColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    private sealed class DrawStringProbeControl : Control
    {
        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(BackColor);
            using var brush = new SolidBrush(ForeColor);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            eventArgs.Graphics.DrawString(Text, Font, brush, ClientRectangle, format);
        }
    }

    private sealed class ScopedTextRendererProbeControl : Control
    {
        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(BackColor);
            var previous = SetThreadDpiAwarenessContext(DpiAwarenessContextUnaware);
            if (previous == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Scoped TextRenderer DPI switch failed: {Marshal.GetLastWin32Error()}");
            try
            {
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    ForeColor,
                    BackColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
            finally
            {
                SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    private sealed record ProbeDefinition(
        string Name,
        string Kind,
        int FontPixels,
        bool CompatibleText,
        Control Control,
        Rectangle TextRegion);

    private sealed record GlyphMetric(Rectangle Bounds, int DarkPixelCount);

    private sealed record ProbeMetric(
        string Name,
        string Kind,
        int FontPixels,
        bool CompatibleText,
        int BitmapWidth,
        int BitmapHeight,
        int GlyphX,
        int GlyphY,
        int GlyphWidth,
        int GlyphHeight,
        int DarkPixels);

    private sealed record ProbeResult(
        string Mode,
        bool DpiModeApplied,
        string ApplicationDpiMode,
        int FormDeviceDpi,
        uint WindowDpi,
        bool WindowUnaware,
        string[] ThreadExceptions,
        List<ProbeMetric> Metrics);
}
