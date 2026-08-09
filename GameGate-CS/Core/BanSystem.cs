using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace GameGate.Core;

/// <summary>IP/HWID ban system with CC attack detection.</summary>
public sealed class BanSystem
{
    private readonly HashSet<string> _blockedIPs = [];
    private readonly HashSet<string> _blockedIPEntries = [];
    private readonly HashSet<string> _blockedHWIDs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mutedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> _tempBans = new();
    private readonly ConcurrentDictionary<string, double> _tempNameBans = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> _tempMutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> _ccBans = new();
    private readonly ConcurrentDictionary<string, List<double>> _ipTracker = new();
    private long _lastPruneTick;

    // CIDR blocks for BlockIPList.txt (Fix 3)
    private readonly List<(uint network, int prefix)> _blockedCIDRs = [];

    // IP range entries for BlockIPAreaList.txt (Fix 2 & Fix 8)
    private readonly List<(uint start, uint end)> _blockedIPRanges = [];
    // CIDR blocks from BlockIPAreaList.txt (Fix 8)
    private readonly List<(uint network, int prefix)> _blockedAreaCIDRs = [];

    public long TotalBlocks, TotalCCBlocks, TotalTempBans;
    public int AutoBanDuration = 1800; // seconds

    public void LoadBlockIPs(IEnumerable<string> ips)
    {
        lock (_blockedIPEntries)
        {
            foreach (var ip in ips)
            {
                if (string.IsNullOrEmpty(ip)) continue;
                if (!_blockedIPEntries.Add(ip)) continue;
                if (TryParseCIDR(ip, out uint net, out int prefix))
                    _blockedCIDRs.Add((net, prefix));
                else
                    _blockedIPs.Add(ip);
            }
        }
    }

    /// <summary>Load BlockIPAreaList.txt entries. Format: "startIp-endIp" or CIDR. (Fix 2, Fix 8)</summary>
    public void LoadBlockIPAreas(IEnumerable<string> entries)
    {
        lock (_blockedIPEntries)
        {
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                if (TryParseCIDR(entry, out uint net, out int prefix))
                {
                    _blockedAreaCIDRs.Add((net, prefix));
                    continue;
                }
                int dash = entry.IndexOf('-');
                if (dash > 0 &&
                    IPAddress.TryParse(entry[..dash].Trim(), out var startIp) &&
                    IPAddress.TryParse(entry[(dash + 1)..].Trim(), out var endIp))
                {
                    uint start = IpToUInt32(startIp);
                    uint end = IpToUInt32(endIp);
                    if (start <= end) _blockedIPRanges.Add((start, end));
                }
            }
        }
    }

    public void LoadBlockHWIDs(IEnumerable<string> hwids)
    {
        lock (_blockedHWIDs)
            foreach (var value in hwids.Where(value => !string.IsNullOrWhiteSpace(value)))
                _blockedHWIDs.Add(value.Trim());
    }

    public void LoadBlockedNames(IEnumerable<string> names)
    {
        lock (_blockedNames)
            foreach (var value in names.Where(value => !string.IsNullOrWhiteSpace(value)))
                _blockedNames.Add(value.Trim());
    }

    public void LoadMutedNames(IEnumerable<string> names)
    {
        lock (_mutedNames)
            foreach (var value in names.Where(value => !string.IsNullOrWhiteSpace(value)))
                _mutedNames.Add(value.Trim());
    }

    public bool IsIPBlocked(string ip)
    {
        lock (_blockedIPEntries)
        {
            lock (_blockedIPs)
                if (_blockedIPs.Contains(ip)) { Interlocked.Increment(ref TotalBlocks); return true; }

            if (_blockedCIDRs.Count > 0 && IPAddress.TryParse(ip, out var parsed))
            {
                uint ipVal = IpToUInt32(parsed);
                foreach (var (net, prefix) in _blockedCIDRs)
                {
                    if (MatchCIDR(ipVal, net, prefix)) { Interlocked.Increment(ref TotalBlocks); return true; }
                }
            }
        }

        lock (_blockedIPEntries)
        {
            if (IPAddress.TryParse(ip, out var parsed))
            {
                uint ipValue = IpToUInt32(parsed);
                foreach (var (start, end) in _blockedIPRanges)
                    if (ipValue >= start && ipValue <= end)
                    {
                        Interlocked.Increment(ref TotalBlocks);
                        return true;
                    }
                foreach (var (network, prefix) in _blockedAreaCIDRs)
                    if (MatchCIDR(ipValue, network, prefix))
                    {
                        Interlocked.Increment(ref TotalBlocks);
                        return true;
                    }
            }
        }

        if (_tempBans.TryGetValue(ip, out var exp))
        {
            if (NowSeconds < exp) { Interlocked.Increment(ref TotalBlocks); return true; }
            _tempBans.TryRemove(ip, out _);
        }
        if (_ccBans.TryGetValue(ip, out exp))
        {
            if (NowSeconds < exp) { Interlocked.Increment(ref TotalCCBlocks); return true; }
            _ccBans.TryRemove(ip, out _);
        }
        return false;
    }

    public bool IsHWIDBlocked(string hwid)
    {
        if (string.IsNullOrEmpty(hwid)) return false;
        lock (_blockedHWIDs) return _blockedHWIDs.Contains(hwid);
    }

    public bool IsNameBlocked(string? name) => IsListed(name, _blockedNames, _tempNameBans);

    public bool IsNameMuted(string? name) => IsListed(name, _mutedNames, _tempMutes);

    private static bool IsListed(string? value, HashSet<string> permanent,
        ConcurrentDictionary<string, double> temporary)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        lock (permanent)
            if (permanent.Contains(value)) return true;
        if (!temporary.TryGetValue(value, out var expires)) return false;
        if (expires > NowSeconds) return true;
        temporary.TryRemove(value, out _);
        return false;
    }

    // Fix 7: Per-IP recovery attempt tracking (persists across session resets)
    private readonly ConcurrentDictionary<string, (int attempts, double lastAttemptTime)> _recoveryTracker = new();
    private const int MaxRecoveryAttempts = 20;
    private const double RecoveryResetWindow = 300.0; // 5 minutes of clean behavior

    public void BlockIP(string ip, bool permanent = false)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        ip = ip.Trim();
        if (permanent)
        {
            lock (_blockedIPEntries)
            {
                if (!_blockedIPEntries.Add(ip)) return;
                if (TryParseCIDR(ip, out uint network, out int prefix))
                    _blockedCIDRs.Add((network, prefix));
                else
                    _blockedIPs.Add(ip);
            }
        }
        else
        {
            _tempBans[ip] = NowSeconds + AutoBanDuration;
            Interlocked.Increment(ref TotalTempBans);
        }
    }

    public void BlockIP(string ip, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        _tempBans[ip.Trim()] = NowSeconds + Math.Max(60, duration.TotalSeconds);
        Interlocked.Increment(ref TotalTempBans);
    }

    public void BlockName(string name, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (duration is { } timed)
            _tempNameBans[name] = NowSeconds + Math.Max(60, timed.TotalSeconds);
        else
            lock (_blockedNames) _blockedNames.Add(name);
    }

    public void MuteName(string name, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (duration is { } timed)
            _tempMutes[name] = NowSeconds + Math.Max(60, timed.TotalSeconds);
        else
            lock (_mutedNames) _mutedNames.Add(name);
    }

    public void BlockHWID(string hwid)
    {
        if (string.IsNullOrWhiteSpace(hwid)) return;
        lock (_blockedHWIDs) _blockedHWIDs.Add(hwid.Trim());
    }

    /// <summary>Fix 7: Record a recovery attempt for an IP and check if limit exceeded.</summary>
    public bool RecordRecoveryAttempt(string ip)
    {
        double now = NowSeconds;
        var entry = _recoveryTracker.AddOrUpdate(ip,
            _ => (attempts: 1, lastAttemptTime: now),
            (_, old) =>
            {
                // Reset if clean window passed since last attempt
                if (now - old.lastAttemptTime > RecoveryResetWindow)
                    return (attempts: 1, lastAttemptTime: now);
                return (attempts: old.attempts + 1, lastAttemptTime: now);
            });

        if (entry.attempts >= MaxRecoveryAttempts)
        {
            BlockIP(ip, permanent: true);
            return true; // permanent ban applied
        }
        return false;
    }

    /// <summary>Fix 7: Check if an IP has been permanently banned due to recovery attempts.</summary>
    public bool CheckRecoveryBan(string ip)
    {
        if (_recoveryTracker.TryGetValue(ip, out var entry))
        {
            double now = NowSeconds;
            // Reset attempts if clean window has passed
            if (now - entry.lastAttemptTime > RecoveryResetWindow)
            {
                _recoveryTracker.TryRemove(ip, out _);
                return false;
            }
            return entry.attempts >= MaxRecoveryAttempts;
        }
        return false;
    }

    /// <summary>Fix 7: Reset recovery attempts for an IP (e.g., on clean disconnect).</summary>
    public void ResetRecoveryAttempts(string ip)
    {
        _recoveryTracker.TryRemove(ip, out _);
    }

    public void UnblockIP(string ip)
    {
        lock (_blockedIPEntries) _blockedIPEntries.Remove(ip);
        RebuildBlockIPRules();
        _tempBans.TryRemove(ip, out _);
        _ccBans.TryRemove(ip, out _);
    }

    private void RebuildBlockIPRules()
    {
        lock (_blockedIPEntries)
        {
            _blockedIPs.Clear();
            _blockedCIDRs.Clear();
            foreach (var entry in _blockedIPEntries)
            {
                if (TryParseCIDR(entry, out uint network, out int prefix))
                    _blockedCIDRs.Add((network, prefix));
                else
                    _blockedIPs.Add(entry);
            }
        }
    }

    public void UnblockName(string name)
    {
        lock (_blockedNames) _blockedNames.Remove(name);
        _tempNameBans.TryRemove(name, out _);
    }

    public void UnmuteName(string name)
    {
        lock (_mutedNames) _mutedNames.Remove(name);
        _tempMutes.TryRemove(name, out _);
    }

    public void UnblockHWID(string hwid)
    {
        lock (_blockedHWIDs) _blockedHWIDs.Remove(hwid);
    }

    /// <summary>CC attack detection — same-IP connection rate limiting.</summary>
    public bool CheckCC(string ip, int threshold = 10, double windowSec = 5.0)
    {
        double now = NowSeconds;
        PruneExpired(now, windowSec);
        while (true)
        {
            var list = _ipTracker.GetOrAdd(ip, _ => []);
            lock (list)
            {
                if (!_ipTracker.TryGetValue(ip, out var current)
                    || !ReferenceEquals(current, list)) continue;
                list.Add(now);
                list.RemoveAll(t => now - t > windowSec);
                if (list.Count > threshold)
                {
                    _ccBans[ip] = now + AutoBanDuration;
                    Interlocked.Increment(ref TotalCCBlocks);
                    return true;
                }
                return false;
            }
        }
    }

    private void PruneExpired(double now, double ccWindowSec)
    {
        var nowTick = Environment.TickCount64;
        var last = Volatile.Read(ref _lastPruneTick);
        if (last != 0 && nowTick - last < 30000) return;
        if (Interlocked.CompareExchange(ref _lastPruneTick, nowTick, last) != last) return;

        RemoveExpired(_tempBans, now);
        RemoveExpired(_tempNameBans, now);
        RemoveExpired(_tempMutes, now);
        RemoveExpired(_ccBans, now);
        foreach (var entry in _recoveryTracker)
            if (now - entry.Value.lastAttemptTime > RecoveryResetWindow)
                ((ICollection<KeyValuePair<string, (int attempts, double lastAttemptTime)>>)
                    _recoveryTracker).Remove(entry);

        foreach (var entry in _ipTracker)
        {
            lock (entry.Value)
            {
                if (entry.Value.Count == 0 || now - entry.Value[^1] > ccWindowSec)
                    ((ICollection<KeyValuePair<string, List<double>>>)_ipTracker).Remove(entry);
            }
        }
    }

    private static void RemoveExpired(ConcurrentDictionary<string, double> values, double now)
    {
        foreach (var entry in values)
            if (entry.Value <= now)
                ((ICollection<KeyValuePair<string, double>>)values).Remove(entry);
    }

    private static double NowSeconds => Environment.TickCount64 / 1000.0;

    /// <summary>HWID computation: XOR(MD5(ip|mac|vol), "openmir2")</summary>
    public static string ComputeHWID(string ip, string mac = "", string volumeSerial = "")
    {
        var input = Encoding.UTF8.GetBytes($"{ip}|{mac}|{volumeSerial}");
        var hash = MD5.HashData(input);
        var key = Encoding.ASCII.GetBytes("openmir2");
        for (int i = 0; i < hash.Length; i++) hash[i] ^= key[i % key.Length];
        return Convert.ToHexString(hash).ToLower();
    }

    /// <summary>Convert IP address to uint32 for range/CIDR comparison.</summary>
    private static uint IpToUInt32(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        // Handle IPv6 mapped IPv4
        if (bytes.Length == 16)
        {
            // Check if it's an IPv4-mapped IPv6 address (::ffff:x.x.x.x)
            bool isV4Mapped = true;
            for (int i = 0; i < 10; i++) { if (bytes[i] != 0) { isV4Mapped = false; break; } }
            if (isV4Mapped && bytes[10] == 0xFF && bytes[11] == 0xFF)
            {
                return (uint)(bytes[12] << 24 | bytes[13] << 16 | bytes[14] << 8 | bytes[15]);
            }
        }
        if (bytes.Length == 4)
            return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        return 0;
    }

    /// <summary>Parse CIDR string like "192.168.0.0/16".</summary>
    private static bool TryParseCIDR(string cidr, out uint network, out int prefix)
    {
        network = 0; prefix = 0;
        int slash = cidr.IndexOf('/');
        if (slash < 0) return false;
        string ipPart = cidr[..slash].Trim();
        if (!IPAddress.TryParse(ipPart, out var ip)) return false;
        if (!int.TryParse(cidr[(slash + 1)..].Trim(), out prefix)) return false;
        if (prefix < 0 || prefix > 32) return false;
        network = IpToUInt32(ip);
        return true;
    }

    /// <summary>Check if an IP value matches a CIDR block.</summary>
    private static bool MatchCIDR(uint ip, uint network, int prefix)
    {
        if (prefix == 0) return true;
        uint mask = prefix >= 32 ? 0xFFFFFFFF : (uint)((0xFFFFFFFF << (32 - prefix)) & 0xFFFFFFFF);
        return (ip & mask) == (network & mask);
    }

    public object GetStats()
    {
        double now = NowSeconds;
        int blockedIpCount;
        lock (_blockedIPEntries)
            blockedIpCount = _blockedIPs.Count + _blockedCIDRs.Count +
                _blockedIPRanges.Count + _blockedAreaCIDRs.Count;
        return new
        {
            BlockedIPs = blockedIpCount,
            BlockedHWIDs = GetBlockedHWIDs().Length,
            BlockedNames = GetBlockedNames().Length,
            MutedNames = GetMutedNames().Length,
            TempBansActive = _tempBans.Count(kv => kv.Value > now),
            CCBansActive = _ccBans.Count(kv => kv.Value > now),
            TotalBlocks = TotalBlocks + TotalCCBlocks,
        };
    }

    public string[] GetBlockedIPs() { lock (_blockedIPEntries) return _blockedIPEntries.Order().ToArray(); }
    public string[] GetBlockedHWIDs() { lock (_blockedHWIDs) return _blockedHWIDs.Order().ToArray(); }
    public string[] GetBlockedNames() { lock (_blockedNames) return _blockedNames.Order().ToArray(); }
    public string[] GetMutedNames() { lock (_mutedNames) return _mutedNames.Order().ToArray(); }

    public (string Target, int RemainingMinutes)[] GetTemporaryIPBans() => GetTimed(_tempBans);
    public (string Target, int RemainingMinutes)[] GetTemporaryNameBans() => GetTimed(_tempNameBans);
    public (string Target, int RemainingMinutes)[] GetTemporaryMutes() => GetTimed(_tempMutes);

    private static (string Target, int RemainingMinutes)[] GetTimed(
        ConcurrentDictionary<string, double> values)
    {
        double now = NowSeconds;
        return values.Where(pair => pair.Value > now)
            .Select(pair => (pair.Key, Math.Max(1, (int)Math.Ceiling((pair.Value - now) / 60))))
            .OrderBy(pair => pair.Key).ToArray();
    }

    public void LoadTemporaryBans(string path)
    {
        if (!File.Exists(path)) return;
        string section = string.Empty;
        long unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var raw in File.ReadAllLines(path, Encoding.GetEncoding("GBK")))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }
            int equals = line.LastIndexOf('=');
            if (equals <= 0 || !long.TryParse(line[(equals + 1)..], out var expiresUnix)) continue;
            double remaining = expiresUnix - unixNow;
            if (remaining <= 0) continue;
            string target = line[..equals].Trim();
            switch (section.ToUpperInvariant())
            {
                case "IP": _tempBans[target] = NowSeconds + remaining; break;
                case "NAME": _tempNameBans[target] = NowSeconds + remaining; break;
                case "MUTE": _tempMutes[target] = NowSeconds + remaining; break;
            }
        }
    }

    public void SavePersistentLists(string configDir)
    {
        Directory.CreateDirectory(configDir);
        var encoding = Encoding.GetEncoding("GBK");
        File.WriteAllLines(Path.Combine(configDir, "BackList.txt"), GetBlockedIPs(), encoding);
        File.WriteAllLines(Path.Combine(configDir, "BlockHWID.txt"), GetBlockedHWIDs(), encoding);
        File.WriteAllLines(Path.Combine(configDir, "NameList.txt"), GetBlockedNames(), encoding);
        File.WriteAllLines(Path.Combine(configDir, "MuteList.txt"), GetMutedNames(), encoding);

        long unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double now = NowSeconds;
        var lines = new List<string>();
        AppendTimed(lines, "IP", _tempBans, now, unixNow);
        AppendTimed(lines, "Name", _tempNameBans, now, unixNow);
        AppendTimed(lines, "Mute", _tempMutes, now, unixNow);
        File.WriteAllLines(Path.Combine(configDir, "TempBan.ini"), lines, encoding);
    }

    private static void AppendTimed(List<string> lines, string section,
        ConcurrentDictionary<string, double> values, double now, long unixNow)
    {
        lines.Add($"[{section}]");
        foreach (var pair in values.Where(pair => pair.Value > now).OrderBy(pair => pair.Key))
            lines.Add($"{pair.Key}={unixNow + (long)Math.Ceiling(pair.Value - now)}");
        lines.Add(string.Empty);
    }
}
