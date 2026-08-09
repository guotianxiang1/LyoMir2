using GameSvr;
using MySql.Data.MySqlClient;

if (args.Length != 1)
    throw new ArgumentException("Usage: NativeHonorDbCheck <MySQL connection string>");

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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
