using GameSvr;
using MySql.Data.MySqlClient;

// No connection string => INCOMPLETE (exit 2), not a throw. Throwing made an
// unattended sweep record this as FAIL, which is a false red: "a live database
// was never supplied" is an environment gap, not a defect in the code under
// test. Exit 2 is this tree's INCOMPLETE convention, so the run is still
// visibly not-green and can never be mistaken for a pass.
if (args.Length != 1 || !LooksLikeConnectionString(args[0]))
{
    Console.WriteLine("SKIP NativeHonorDbCheck: no MySQL connection string given.");
    Console.WriteLine("  usage: NativeHonorDbCheck <MySQL connection string>");
    Console.WriteLine("SKIP reason: every assertion in this check needs a live "
        + "database; none were executed, so this run proves nothing about the "
        + "honor DB contract.");
    return 2;
}

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig { sConnctionString = args[0] };
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();

var characterName = "AUD" + DateTime.UtcNow.Ticks.ToString()[^10..];
var manager = new NativeHonorValueManager();
try
{
    Assert(manager.Initialize(), "native honor schema initialization failed");
    Assert(manager.TryAdd(characterName, 10, out var value) && value == 10,
        $"initial honor add failed: {value}");
    Assert(manager.TryAdd(characterName, int.MaxValue, out value) && value == int.MaxValue,
        $"honor saturation failed: {value}");
    Assert(manager.Get(characterName) == int.MaxValue, "honor readback failed");
    Assert(manager.TrySubtract(characterName, int.MaxValue, out value) && value == 0,
        $"honor subtraction clamp failed: {value}");
    Assert(!manager.TryAdd("超过十五字节的角色名字", 1, out _),
        "overlength GBK character name was accepted");
    Console.WriteLine("NativeHonorDbCheck PASS");
}
finally
{
    using var connection = new MySqlConnection(args[0]);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "Delete from gamedata.User_Honor where ChrName = @name;";
    command.Parameters.AddWithValue("@name", characterName);
    command.ExecuteNonQuery();
}

// Reached only when the try block completed without throwing, i.e. every
// assertion above held. An Assert failure still propagates as an unhandled
// exception (nonzero exit), which the runner classifies as FAIL.
return 0;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool LooksLikeConnectionString(string value)
{
    if (string.IsNullOrWhiteSpace(value)) return false;
    if (Directory.Exists(value) || File.Exists(value)) return false;
    return value.Contains('=', StringComparison.Ordinal)
        && (value.Contains("Server", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Data Source", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Uid", StringComparison.OrdinalIgnoreCase)
            || value.Contains("User Id", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Password", StringComparison.OrdinalIgnoreCase));
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}
