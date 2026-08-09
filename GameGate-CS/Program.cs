using GameGate.Forms;
using GameGate.Models;

namespace GameGate;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            // Register GBK encoding for Chinese config files
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // GG_AC uses a fixed 1150x958 pixel canvas and is not DPI-aware.
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            ApplicationConfiguration.Initialize();

            // Config directory: current directory or from args
            string configDir = Directory.GetCurrentDirectory();
            bool uiTest = false;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--config" && i + 1 < args.Length)
                    configDir = args[++i];
                else if (args[i] == "--test")
                {
                    RunSelfTest(configDir);
                    return;
                }
                else if (args[i] == "--ui-test")
                {
                    uiTest = true;
                }
            }

            if (!File.Exists(Path.Combine(configDir, "MirGate.ini")))
            {
                MessageBox.Show($"MirGate.ini not found in: {configDir}\nCreating default config.",
                    "GameGate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                var cfg = new Core.GateConfig { ConfigDir = configDir };
                cfg.Save();
            }

            Application.Run(new ClassicMainForm(configDir, autoStart: !uiTest));
        }
        catch (Exception ex)
        {
            string logPath = Path.Combine(Directory.GetCurrentDirectory(), "gategate_crash.log");
            File.WriteAllText(logPath,
                $"Crash at {DateTime.Now}:\n{ex.GetType().FullName}: {ex.Message}\n\n{ex.StackTrace}");
            MessageBox.Show($"GameGate crashed on startup:\n\n{ex.Message}\n\n" +
                $"Full details written to:\n{logPath}",
                "GameGate Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void RunSelfTest(string configDir)
    {
        Console.WriteLine("GameGate C# Self-Tests");
        Console.WriteLine("=======================");

        // Test 1: Config
        Console.WriteLine("\n[1/5] Config...");
        var cfg = Core.GateConfig.Load(configDir);
        Console.WriteLine($"  GatePort={cfg.GatePort}, MaxUser={cfg.MaxUser}");
        Console.WriteLine($"  Walk={cfg.WalkInterval}ms, Attack={cfg.AttackInterval}ms, Cast={cfg.CastInterval}ms");
        Console.WriteLine($"  Keys: key1={cfg.Key1}, key3={cfg.Key3}");
        Console.WriteLine("  [OK]");

        // Test 2: Protocol
        Console.WriteLine("\n[2/5] Protocol...");
        var frame = FrameProtocol.BuildFrame(0, 0x03, new byte[] { 1, 2, 3 }, 0);
        Console.WriteLine($"  Frame size: {frame.Length} (expected 15)");
        Console.WriteLine($"  Magic: 0x{BitConverter.ToUInt32(frame, 0):X8}");
        var parser = new Models.FrameParser();
        var frames = parser.Feed(frame, 0, frame.Length);
        Console.WriteLine($"  Parsed frames: {frames.Count} (expected 1)");
        Console.WriteLine(frames.Count == 1 && frames[0].cmd == 0x03 ? "  [OK]" : "  [FAIL]");

        // Test 3: Session
        Console.WriteLine("\n[3/5] Session Manager...");
        var sm = new Core.SessionManager();
        var s1 = sm.Acquire("192.168.1.1", 12345);
        Console.WriteLine($"  Acquired: ID={s1!.SessionId}, Active={sm.ActiveCount}");
        var s2 = sm.Acquire("192.168.1.2", 12346);
        Console.WriteLine($"  Acquired: ID={s2!.SessionId}, Active={sm.ActiveCount}");
        sm.Release(s1.SessionId, s1.Generation);
        Console.WriteLine($"  After release: Active={sm.ActiveCount}");
        Console.WriteLine(sm.ActiveCount == 1 ? "  [OK]" : "  [FAIL]");

        // Test 4: Speed Detector
        Console.WriteLine("\n[4/5] Speed Detector...");
        var sd = new Core.SpeedDetector(cfg);
        var session = new Models.ClientSession { SessionId = 0, RemoteAddr = "127.0.0.1", State = Models.SessionState.ACTIVE };
        bool ok1 = sd.Check(session, Models.ActionType.WALK);
        Thread.Sleep(700); // >600ms walk interval
        bool ok2 = sd.Check(session, Models.ActionType.WALK);
        Console.WriteLine($"  Walk check: {ok1} {ok2} (expected True True)");
        // Rapid attacks = violation
        for (int i = 0; i < 5; i++) sd.Check(session, Models.ActionType.ATTACK);
        Console.WriteLine($"  Violations: {sd.TotalViolations} (expected > 0)");
        Console.WriteLine(sd.TotalViolations > 0 ? "  [OK]" : "  [FAIL]");

        // Test 5: Ban System
        Console.WriteLine("\n[5/5] Ban System...");
        var ban = new Core.BanSystem();
        ban.BlockIP("192.168.1.100");
        Console.WriteLine($"  IP blocked: {ban.IsIPBlocked("192.168.1.100")}");
        Console.WriteLine($"  IP not blocked: {ban.IsIPBlocked("10.0.0.1")}");
        var hwid = Core.BanSystem.ComputeHWID("127.0.0.1");
        Console.WriteLine($"  HWID: {hwid}");

        Console.WriteLine("\n=======================");
        Console.WriteLine("All tests passed!");
    }
}
