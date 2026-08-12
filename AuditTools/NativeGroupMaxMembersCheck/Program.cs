using GameSvr;
using SystemModule;

// TEAM-08: Native group member limit is hardcoded at 11 in the binary.
// The config value nGroupMembersMax exists but is unused. This audit ensures
// the config cannot silently exceed the native protocol's hard limit.

PrepareRuntimeConfig();
var config = new GameSvrConfig();

// 战神 discovery_group_channel_20260803.md row #7:
// Native bound is hard 0xB (11) at multiple sites:
//   6C3534: cmp dword [eax+0x44],0xB / jge  (capacity check, error -5)
//   72732A: cmp dword [ebx+0x44],0xB / jge  (insert gate)
//   726C32: cmp ecx,0xB / jne              (leader scan)
//   727388: cmp esi,0xB / jne              (insert scan)
//   727279: cmp ebx,0xB / jne              (broadcast scan)
//
// C# uses NativeGroupMaxMembers = 11 everywhere (NativeGroupProtocol.cs:8).
// The config default is 10, which is safe (10 < 11), but raising it above 10
// would silently fail because:
//   1. The native protocol checks use the constant, not the config
//   2. The client has a fixed 11×54 buffer for member records
//   3. Exceeding 11 would overflow client memory or cause protocol desync

const int NativeGroupMaxMembers = 11;

if (config.nGroupMembersMax > NativeGroupMaxMembers - 1)
{
    throw new InvalidOperationException(
        $"FAIL: nGroupMembersMax ({config.nGroupMembersMax}) exceeds native " +
        $"protocol limit. The 战神 binary has a hard limit of {NativeGroupMaxMembers} " +
        $"members (slots 0-10), which includes the leader. The config value must be " +
        $"<= {NativeGroupMaxMembers - 1} to prevent client buffer overflow and " +
        $"protocol desync. Current default (10) is correct.");
}

// Verify the constant matches what's used in the protocol implementation
var protocolSource = File.ReadAllText(Path.Combine(
    FindRepositoryRoot(), "GameSvr", "Players",
    "TPlayObject.NativeGroupProtocol.cs"));

if (!protocolSource.Contains("private const int NativeGroupMaxMembers = 11;"))
{
    throw new InvalidOperationException(
        "FAIL: NativeGroupMaxMembers constant not found or has wrong value");
}

// Verify the protocol uses the constant, not the config
if (protocolSource.Contains("nGroupMembersMax", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "FAIL: NativeGroupProtocol.cs references nGroupMembersMax config, " +
        "but should only use the hardcoded NativeGroupMaxMembers constant");
}

// Verify the legacy handler uses the constant
var legacySource = File.ReadAllText(Path.Combine(
    FindRepositoryRoot(), "GameSvr", "Players", "TPlayObject.Operate.cs"));

if (!legacySource.Contains("m_GroupMembers.Count >= NativeGroupMaxMembers"))
{
    throw new InvalidOperationException(
        "FAIL: ClientAddGroupMember does not check against NativeGroupMaxMembers");
}

Console.WriteLine(
    $"PASS NativeGroupMaxMembers limit=11 config={config.nGroupMembersMax} " +
    $"safe={config.nGroupMembersMax <= 10}");

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory,
        AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }
    throw new InvalidOperationException("repository root not found");
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
