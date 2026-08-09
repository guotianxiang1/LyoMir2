using System.Text;

namespace GameGate.Core;

/// <summary>
/// Configuration manager — parses real production MirGate.ini format.
/// Gateway keys live in [Server], while M2 endpoints may use [M2监控] or [M2控制].
/// Key names match Chinese MirServer conventions: Walk, Attacr, Cast, etc.
/// </summary>
public sealed class GateConfig
{
    // ── Network ──
    // Client sessions are multiplexed over one DBSvr route and one M2 route.
    public int GatePort = 7100;
    public string GateAddr = "0.0.0.0";
    public string BackendIP = "127.0.0.1";
    public string GameBackendIP = "127.0.0.1";
    // 原版: GameGate → M2Server via 77BBAA33. 默认M2Server网关端口
    public int BackendPort = 5000;
    public int BackendPort2 = 5100; // 角色CRUD走DBSvr :5100
    public int MaxUser = 5000;
    public int MaxSend = 1000;
    public int ServeCount = 16;
    public int Mode = 1;

    // ── Speed Detection (real INI keys) ──
    public int WalkInterval = 570;       // Walk (Delphi: 570ms)
    public int AttackInterval = 900;     // Attacr
    public int CastInterval = 1110;      // Cast (Delphi: 1110ms, not 2150)
    public int TurnInterval = 350;       // TurnTime (Delphi: 350ms, not 600)
    public int CureInterval = 500;       // CureTime
    public int ShopInterval = 300;       // ShopTime
    public int NpcInterval = 100;        // NpcTime
    public int SpeedNum = 30;            // SpeedNum
    public bool GlobalSpeed;             // Globalspeed
    public int WalkSpeedNum = 10;        // WalkSpeedNum
    public int MuteTime = 5;             // MuteTime (minutes)
    public int BlackTime = 5;            // blacktime (minutes, matching GG_AC UI)
    public int SpellNum = 35;            // Spellnum
    public int Timeout0 = 10000;
    public int Timeout1 = 15000;

    // ── Encryption ──
    public string Key1 = "", Key2 = "", Key3 = "", Key4 = "", Key5 = "";
    public string OffKey = "", OffKeybot = "";
    public bool OpenNewTigerGate = true; // Enable Tiger (BaiZhu) protocol

    // ── Paths ──
    public string ConfigDir = ".";
    public string M2Path = "";
    public int M2WatchInterval = 30000;
    public bool RebootM2WhenStuck;
    public string Title = "GS";

    // ── Derived ──
    public int MaxSessions => MaxUser;

    // ── Whitelist data ──
    public bool WhitelistUpAll;
    public bool WhitelistDownAll;
    public readonly HashSet<int> WhitelistUp = [];
    public readonly HashSet<int> WhitelistDown = [];
    public readonly HashSet<string> BlockIPs = [];
    public readonly HashSet<string> BlockHWIDs = [];
    public readonly HashSet<string> BlockNames = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> MutedNames = new(StringComparer.OrdinalIgnoreCase);

    // ── Block lists (Fix 2, Fix 4) ──
    public readonly HashSet<string> BlockIPAreas = [];   // BlockIPAreaList.txt raw entries
    public readonly HashSet<string> BlockHWIDList = [];  // BlockHWID.txt raw entries

    // ── Abusive filter (Fix 1) ──
    public readonly AbusiveFilter AbusiveFilter = new();

    /// <summary>Load from real MirGate.ini + companion files.</summary>
    public static GateConfig Load(string dir)
    {
        var cfg = new GateConfig { ConfigDir = dir };

        // Load MirGate.ini (GBK encoding — Chinese server standard)
        string iniPath = Path.Combine(dir, "MirGate.ini");
        if (File.Exists(iniPath))
            ParseIni(cfg, File.ReadAllText(iniPath, Encoding.GetEncoding("GBK")));

        // Load whitelists
        LoadWhitelist(cfg, Path.Combine(dir, "MsgWhiteList_Up.txt"), true);
        LoadWhitelist(cfg, Path.Combine(dir, "MsgWhiteList_Down.txt"), false);

        // Load block lists
        LoadBlockList(cfg, Path.Combine(dir, "BackList.txt"));
        LoadBlockIPAreaList(cfg, Path.Combine(dir, "BlockIPAreaList.txt")); // Fix 2
        LoadBlockHWIDList(cfg, Path.Combine(dir, "BlockHWID.txt"));         // Fix 4
        LoadNameList(cfg.BlockNames, Path.Combine(dir, "NameList.txt"));
        LoadNameList(cfg.MutedNames, Path.Combine(dir, "MuteList.txt"));

        // Load abusive filter (Fix 1)
        cfg.AbusiveFilter.LoadRules(Path.Combine(dir, "AbusiveFilter.txt"));

        cfg.MaxUser = Math.Clamp(cfg.MaxUser, 1, SessionManager.MAX_SESSIONS);

        return cfg;
    }

    private static void ParseIni(GateConfig cfg, string content)
    {
        string section = string.Empty;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') ||
                line.StartsWith('#') || line.StartsWith('!'))
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;

            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();
            if (!CanManageKey(section, key)) continue;

            if (string.IsNullOrEmpty(val) || val is "��" or "无") continue;

            // Try parse as int first, use 0 on failure
            int.TryParse(val, out int iv);

            switch (key)
            {
                case "GatePort": cfg.GatePort = iv; break;
                case "GateAddr": cfg.GateAddr = val; break;
                case "DBServerIP": cfg.BackendIP = val; break;
                case "DBServerPort": cfg.BackendPort2 = iv != 0 ? iv : 5100; break;
                case "GameServerIP": cfg.GameBackendIP = val; break;
                case "GameServerPort": cfg.BackendPort = iv != 0 ? iv : 5000; break;
                case "MaxUser": cfg.MaxUser = iv; break;
                case "MaxSend": cfg.MaxSend = iv; break;
                case "ServeCount": cfg.ServeCount = iv; break;
                case "Mode": cfg.Mode = iv; break;
                case "Walk": cfg.WalkInterval = iv; break;
                case "Attacr": cfg.AttackInterval = iv; break;
                case "Cast": cfg.CastInterval = iv; break;
                case "TurnTime": cfg.TurnInterval = iv; break;
                case "CureTime": cfg.CureInterval = iv; break;
                case "ShopTime": cfg.ShopInterval = iv; break;
                case "NpcTime": cfg.NpcInterval = iv; break;
                case "SpeedNum": cfg.SpeedNum = iv; break;
                case "Globalspeed":
                case "GlobalSpeed": cfg.GlobalSpeed = iv != 0; break;
                case "WalkSpeedNum": cfg.WalkSpeedNum = iv; break;
                case "MuteTime": cfg.MuteTime = iv; break;
                case "blacktime": cfg.BlackTime = iv; break;
                case "Spellnum": cfg.SpellNum = iv; break;
                case "Timeout0": cfg.Timeout0 = iv; break;
                case "Timeout1": cfg.Timeout1 = iv; break;
                case "Title": cfg.Title = val; break;
                case "key1": cfg.Key1 = val; break;
                case "key2": cfg.Key2 = val; break;
                case "key3": cfg.Key3 = val; break;
                case "key4": cfg.Key4 = val; break;
                case "key5": cfg.Key5 = val; break;
                case "offKey": cfg.OffKey = val; break;
                case "offKeybot": cfg.OffKeybot = val; break;
                case "OpenNewTigerGate": cfg.OpenNewTigerGate = iv != 0; break;
                case "site": cfg.M2Path = val; break;
                case "time": cfg.M2WatchInterval = iv > 0 ? iv : 30000; break;
                case "Reboot": cfg.RebootM2WhenStuck = ParseBool(val); break;
                case "LogServerIP": break; // ignored
                case "LogServerPort": break;
            }
        }
    }

    private static bool CanManageKey(string section, string key)
    {
        if (section.Length == 0 || section.Equals("Server", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsM2Section(section) && (IsM2Key(key) || IsEncryptionKey(key));
    }

    private static bool IsM2Section(string section) =>
        section.Equals("M2监控", StringComparison.OrdinalIgnoreCase) ||
        section.Equals("M2控制", StringComparison.OrdinalIgnoreCase) ||
        section.Equals("M2", StringComparison.OrdinalIgnoreCase);

    private static bool IsM2Key(string key) => key.Equals("GameServerIP", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("GameServerPort", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("site", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("time", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Reboot", StringComparison.OrdinalIgnoreCase);

    private static bool IsEncryptionKey(string key) => key.Equals("key1", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("key2", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("key3", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("key4", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("key5", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("offKey", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("offKeybot", StringComparison.OrdinalIgnoreCase);

    private static bool ParseBool(string value) => value.Equals("1", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("真", StringComparison.OrdinalIgnoreCase)
        || value.Equals("是", StringComparison.OrdinalIgnoreCase);

    private static void LoadWhitelist(GateConfig cfg, string path, bool isUp)
    {
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path, Encoding.GetEncoding("GBK")).Trim();
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (isUp) cfg.WhitelistUpAll = true;
            else cfg.WhitelistDownAll = true;
            return;
        }
        var target = isUp ? cfg.WhitelistUp : cfg.WhitelistDown;
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (string.IsNullOrEmpty(t) || t.StartsWith('#')) continue;
            if (int.TryParse(t, out int id)) target.Add(id);
        }
    }

    private static void LoadBlockList(GateConfig cfg, string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path, Encoding.GetEncoding("GBK")))
        {
            var t = line.Trim();
            if (!string.IsNullOrEmpty(t) && !t.StartsWith('#'))
                cfg.BlockIPs.Add(t);
        }
    }

    private static void LoadBlockIPAreaList(GateConfig cfg, string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path, Encoding.GetEncoding("GBK")))
        {
            var t = line.Trim();
            if (!string.IsNullOrEmpty(t) && !t.StartsWith('#'))
                cfg.BlockIPAreas.Add(t);
        }
    }

    private static void LoadBlockHWIDList(GateConfig cfg, string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path, Encoding.GetEncoding("GBK")))
        {
            var t = line.Trim();
            if (!string.IsNullOrEmpty(t) && !t.StartsWith('#'))
                cfg.BlockHWIDList.Add(t);
        }
    }

    private static void LoadNameList(HashSet<string> target, string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path, Encoding.GetEncoding("GBK")))
        {
            var value = line.Trim();
            if (!string.IsNullOrEmpty(value) && !value.StartsWith('#') && !value.StartsWith(';'))
                target.Add(value);
        }
    }

    /// <summary>Check if a message is allowed through based on whitelist settings.</summary>
    public bool IsMsgAllowed(int ident, bool isUpstream)
    {
        if (isUpstream)
        {
            if (WhitelistUpAll) return true;
            return WhitelistUp.Count == 0 || WhitelistUp.Contains(ident);
        }
        else
        {
            if (WhitelistDownAll) return true;
            return WhitelistDown.Count == 0 || WhitelistDown.Contains(ident);
        }
    }

    public void Save(string? path = null)
    {
        path ??= Path.Combine(ConfigDir, "MirGate.ini");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = Title,
            ["GatePort"] = GatePort.ToString(),
            ["GateAddr"] = GateAddr,
            ["DBServerIP"] = BackendIP,
            ["DBServerPort"] = BackendPort2.ToString(),
            ["GameServerIP"] = GameBackendIP,
            ["GameServerPort"] = BackendPort.ToString(),
            ["Walk"] = WalkInterval.ToString(),
            ["Attacr"] = AttackInterval.ToString(),
            ["Cast"] = CastInterval.ToString(),
            ["TurnTime"] = TurnInterval.ToString(),
            ["CureTime"] = CureInterval.ToString(),
            ["ShopTime"] = ShopInterval.ToString(),
            ["NpcTime"] = NpcInterval.ToString(),
            ["SpeedNum"] = SpeedNum.ToString(),
            ["Globalspeed"] = GlobalSpeed ? "1" : "0",
            ["WalkSpeedNum"] = WalkSpeedNum.ToString(),
            ["MuteTime"] = MuteTime.ToString(),
            ["blacktime"] = BlackTime.ToString(),
            ["Spellnum"] = SpellNum.ToString(),
            ["Timeout0"] = Timeout0.ToString(),
            ["Timeout1"] = Timeout1.ToString(),
            ["MaxUser"] = MaxUser.ToString(),
            ["MaxSend"] = MaxSend.ToString(),
            ["Mode"] = Mode.ToString(),
            ["ServeCount"] = ServeCount.ToString(),
            ["OpenNewTigerGate"] = OpenNewTigerGate ? "1" : "0",
            ["site"] = M2Path,
            ["time"] = M2WatchInterval.ToString(),
            ["Reboot"] = RebootM2WhenStuck ? "真" : "假"
        };
        if (!string.IsNullOrEmpty(Key1)) values["key1"] = Key1;
        if (!string.IsNullOrEmpty(Key2)) values["key2"] = Key2;
        if (!string.IsNullOrEmpty(Key3)) values["key3"] = Key3;
        if (!string.IsNullOrEmpty(Key4)) values["key4"] = Key4;
        if (!string.IsNullOrEmpty(Key5)) values["key5"] = Key5;
        if (!string.IsNullOrEmpty(OffKey)) values["offKey"] = OffKey;
        if (!string.IsNullOrEmpty(OffKeybot)) values["offKeybot"] = OffKeybot;

        UpdateIniValues(path, values);
    }

    public Dictionary<string, string> ReadIniValues()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(ConfigDir, "MirGate.ini");
        if (!File.Exists(path)) return result;
        string section = string.Empty;
        foreach (var raw in File.ReadAllLines(path, Encoding.GetEncoding("GBK")))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;
            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            string key = line[..equals].Trim();
            if (CanManageKey(section, key)) result[key] = line[(equals + 1)..].Trim();
        }
        return result;
    }

    public void SaveIniValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        string path = Path.Combine(ConfigDir, "MirGate.ini");
        UpdateIniValues(path, new Dictionary<string, string>(values,
            StringComparer.OrdinalIgnoreCase));
    }

    private static void UpdateIniValues(string path, Dictionary<string, string> values)
    {
        var encoding = Encoding.GetEncoding("GBK");
        var lines = File.Exists(path)
            ? File.ReadAllLines(path, encoding).ToList()
            : new List<string> { "[Server]" };
        var updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int serverStart = lines.FindIndex(line =>
            line.Trim().Equals("[Server]", StringComparison.OrdinalIgnoreCase));
        if (serverStart < 0)
        {
            lines.Insert(0, "[Server]");
            serverStart = 0;
        }

        string section = string.Empty;
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                continue;
            }
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;
            int equals = lines[i].IndexOf('=');
            if (equals <= 0) continue;
            var key = lines[i][..equals].Trim();
            if (!CanManageKey(section, key)) continue;
            if (!values.TryGetValue(key, out var value)) continue;
            lines[i] = $"{lines[i][..(equals + 1)]}{value}";
            updated.Add(key);
        }

        var serverMissing = values.Where(pair => !updated.Contains(pair.Key) &&
                !IsM2PlacementKey(pair.Key))
            .Select(pair => $"{pair.Key}={pair.Value}").ToArray();
        lines.InsertRange(FindSectionEnd(lines, serverStart), serverMissing);

        var m2Missing = values.Where(pair => !updated.Contains(pair.Key) &&
                IsM2PlacementKey(pair.Key))
            .Select(pair => $"{pair.Key}={pair.Value}").ToArray();
        if (m2Missing.Length > 0)
        {
            int m2Start = lines.FindIndex(line =>
            {
                string trimmed = line.Trim();
                return trimmed.StartsWith('[') && trimmed.EndsWith(']') &&
                    IsM2Section(trimmed[1..^1].Trim());
            });
            if (m2Start < 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
                lines.Add("[M2监控]");
                m2Start = lines.Count - 1;
            }
            lines.InsertRange(FindSectionEnd(lines, m2Start), m2Missing);
        }
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", encoding);
    }

    private static int FindSectionEnd(List<string> lines, int sectionStart)
    {
        for (int i = sectionStart + 1; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) return i;
        }
        return lines.Count;
    }

    private static bool IsM2PlacementKey(string key) => IsM2Key(key) || IsEncryptionKey(key);
}
