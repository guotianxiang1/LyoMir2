using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using GameSvr.Plugins;

namespace GameSvr
{
    static class Win32
    {
        [DllImport("kernel32.dll")] public static extern bool AllocConsole();
        [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    }
    class Program
    {
        [STAThread]
        static async Task Main(string[] args)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                GCSettings.LatencyMode = GCSettings.IsServerGC ? GCLatencyMode.Batch : GCLatencyMode.Interactive;

                Console.WriteLine("M2Server starting...");
                bool guiMode = !args.Contains("--console", StringComparer.OrdinalIgnoreCase);
                if (!Environment.UserInteractive) guiMode = false;

                var builder = Host.CreateDefaultBuilder(args)
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<GameApp>();
                        services.AddSingleton<MirLog>();
                        services.AddHostedService<AppService>();
                        services.AddHostedService<TimedService>();
                    });

                var host = builder.Build();

                if (guiMode)
                {
                    host.StartAsync().GetAwaiter().GetResult();
                    InitializePlugins();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);

                    // ★ Delphi 模式: 先显示 GUI，Form.Shown 后再初始化引擎
                    var form = new MainForm();
                    Application.Run(form);
                }
                else
                {
                    await host.StartAsync();
                    Win32.AllocConsole();
                    InitializePlugins();
                    var appService = AppService.Instance ?? throw new InvalidOperationException("AppService 未启动");
                    appService.OnFormReady();
                    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                    Console.CancelKeyPress += (s, e) =>
                    {
                        e.Cancel = true;
                        lifetime.StopApplication();
                    };
                    try { await Task.Delay(Timeout.Infinite, lifetime.ApplicationStopping); }
                    catch (OperationCanceledException) { }
                }

                await host.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL: {ex}");
                if (Environment.UserInteractive)
                    MessageBox.Show($"Startup error:\n{ex.Message}", "M2Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void InitializePlugins()
        {
            try
            {
                var exeDir = AppContext.BaseDirectory;
                var envirPath = Path.Combine(exeDir, "Envir");
                if (!Directory.Exists(envirPath))
                {
                    envirPath = Path.Combine(Directory.GetParent(exeDir)?.FullName ?? exeDir, "Envir");
                }
                var pm = new PluginManager(envirPath, exeDir);
                pm.RegisterBuiltinPlugins();
                pm.LoadPlugin("YanshenCompat");
                M2Share.PluginManager = pm;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Plugin init: {ex.Message}");
            }
        }
    }
}
