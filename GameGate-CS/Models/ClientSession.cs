using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

namespace GameGate.Models;

/// <summary>Client session — mirrors reverse-engineered structure (~0x204 bytes in original).</summary>
public sealed class ClientSession
{
    public int SessionId;
    public long Generation;
    public SessionState State = SessionState.FREE;
    public string RemoteAddr = "";
    public int RemotePort;
    public DateTime ConnectTime;
    public string? Account;
    public string? CharName;
    public string? HWID;           // Computed client HWID (Fix 4)
    public int DBSessionId;
    public uint BackendRouteId;
    public uint EncryptKey;        // Encryption key (GET_ENCRYPT / DYN_ENCRYPT)
    public bool IsTiger;           // BaiZhu Tiger protocol session
    public uint TigerKeyOffset;    // Key rotation offset for Tiger encryption

    // Player state shown by the local management UI.
    public string MapName = string.Empty;
    public long Ingot;
    public long Gold;
    public long HeartbeatCount;
    public int Job = -1;
    public int Level;
    public int X;
    public int Y;

    // TurnPack penalty flag (Fix 6)
    public bool TurnPack;

    // Speed detection records (10 action types)
    public readonly ActionRecord[] SpeedRecords = new ActionRecord[10];

    // Timestamps
    public double LastPacketTime;
    public double LastCheckTime10s;
    public double LastCleanTime;        // Time of last clean/reset (Fix 7)

    // Violation counters (matching original offsets)
    public int DropPackCount;
    public int DropConnectCount;
    public int ViolationCount1, ViolationCount2, ViolationCount3;
    public int PenaltyCounter;
    public bool BanFlag;
    public int RecoveryAttempts;
    public PenaltyLevel PenaltyLevel = PenaltyLevel.NONE;
    public double MutedUntil, BannedUntil;

    // Stats
    public long TotalRecvBytes, TotalSentBytes, TotalPackets, TotalViolations;

    // Network
    public object? TcpClient; // TcpClient reference
    public readonly SemaphoreSlim ClientWriteLock = new(1, 1);

    public ClientSession()
    {
        for (int i = 0; i < 10; i++) SpeedRecords[i] = new ActionRecord();
    }

    public void Reset()
    {
        TcpClient = null;
        Account = null;
        CharName = null;
        HWID = null;
        DBSessionId = 0;
        BackendRouteId = 0;
        EncryptKey = 0;
        IsTiger = false;
        TigerKeyOffset = 0;
        MapName = string.Empty;
        Ingot = 0;
        Gold = 0;
        HeartbeatCount = 0;
        Job = -1;
        Level = 0;
        X = 0;
        Y = 0;
        TurnPack = false;
        RemoteAddr = string.Empty;
        RemotePort = 0;
        ConnectTime = default;
        State = SessionState.FREE; DropPackCount = 0; DropConnectCount = 0;
        ViolationCount1 = ViolationCount2 = ViolationCount3 = 0; PenaltyCounter = 0;
        BanFlag = false; RecoveryAttempts = 0; PenaltyLevel = PenaltyLevel.NONE;
        MutedUntil = BannedUntil = 0;
        TotalRecvBytes = TotalSentBytes = TotalPackets = TotalViolations = 0;
        LastPacketTime = LastCheckTime10s = LastCleanTime = 0;
        foreach (var r in SpeedRecords) r.Reset();
    }

    public bool IsActive => State == SessionState.ACTIVE;
    public bool IsBanned => State == SessionState.BANNED || BanFlag;
}

public sealed class ActionRecord
{
    public double LastTime;
    public int ViolateCount;
    public int TotalCount;
    public void Reset() { LastTime = 0; ViolateCount = 0; TotalCount = 0; }
}
