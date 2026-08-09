using System.Text;
using LoginGate.Core;
using LoginGate.Forms;

namespace LoginGate;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var options = CommandLineOptions.Parse(args);
        if (options.SelfTest)
            return LoginGateSelfTests.RunAsync().GetAwaiter().GetResult();

        try
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            ApplicationConfiguration.Initialize();

            Directory.CreateDirectory(options.ConfigDirectory);
            var configPath = Path.Combine(options.ConfigDirectory, "LoginGate.ini");
            var config = File.Exists(configPath)
                ? LoginGateConfig.Load(configPath)
                : CreateDefaultConfig(configPath, save: !options.UiTest);
            EnsureRunnableGroup(config);

            // Ticket authentication: [Login]/TicketDb in LoginGate.ini takes
            // precedence, then the LOGINGATE_TICKET_DB env var, else fail-closed.
            var authenticator = !string.IsNullOrWhiteSpace(config.TicketDb)
                ? new MySqlLoginTicketAuthenticator(config.TicketDb)
                : LoginTicketAuthenticatorFactory.CreateFromEnvironment();
            Application.Run(new ClassicMainForm(options.ConfigDirectory, config,
                authenticator, autoStart: !options.UiTest));
            return 0;
        }
        catch (Exception ex)
        {
            if (!options.UiTest)
            {
                MessageBox.Show(ex.Message, "LoginGate", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1;
        }
    }

    private static LoginGateConfig CreateDefaultConfig(string path, bool save)
    {
        var config = new LoginGateConfig
        {
            AreaIdx = 180,
            Project = 1
        };
        config.Groups.Add(new LoginGateGroup(
            1, 1, "玛法体验服", "玛法体验服", "玛法体验服"));
        if (save) config.Save(path);
        return config;
    }

    private static void EnsureRunnableGroup(LoginGateConfig config)
    {
        if (config.Groups.Count != 0) return;
        config.Groups.Add(new LoginGateGroup(
            1, 1, "玛法体验服", "玛法体验服", "玛法体验服"));
    }

    private sealed record CommandLineOptions(
        string ConfigDirectory, bool UiTest, bool SelfTest)
    {
        public static CommandLineOptions Parse(string[] args)
        {
            var configDirectory = Directory.GetCurrentDirectory();
            var uiTest = false;
            var selfTest = false;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--config" when index + 1 < args.Length:
                        configDirectory = Path.GetFullPath(args[++index]);
                        break;
                    case "--ui-test":
                        uiTest = true;
                        break;
                    case "--self-test":
                        selfTest = true;
                        break;
                }
            }
            return new CommandLineOptions(configDirectory, uiTest, selfTest);
        }
    }
}
