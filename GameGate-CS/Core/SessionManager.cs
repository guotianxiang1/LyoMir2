using System.Collections.Concurrent;
using System.Net.Sockets;
using GameGate.Models;

namespace GameGate.Core;

/// <summary>Thread-safe session pool — mirrors FSessionArray[0..4999].</summary>
public sealed class SessionManager
{
    public const int MAX_SESSIONS = 5000;
    private readonly ClientSession?[] _sessions;
    private readonly object _lock = new();
    private int _activeCount;
    private long _nextGeneration;
    public long TotalConnected, TotalDisconnected;

    public int ActiveCount => Volatile.Read(ref _activeCount);
    public int Capacity => _sessions.Length;

    public SessionManager(int maxSessions = MAX_SESSIONS)
    {
        _sessions = new ClientSession?[Math.Clamp(maxSessions, 1, MAX_SESSIONS)];
    }

    public ClientSession? Acquire(string remoteAddr = "", int remotePort = 0)
    {
        lock (_lock)
        {
            for (int i = 0; i < _sessions.Length; i++)
            {
                if (_sessions[i] == null || _sessions[i]!.State == SessionState.FREE)
                {
                    _sessions[i] ??= new ClientSession();
                    var s = _sessions[i]!;
                    s.Reset();
                    s.SessionId = i;
                    s.Generation = Interlocked.Increment(ref _nextGeneration);
                    s.State = SessionState.ACTIVE;
                    s.RemoteAddr = remoteAddr;
                    s.RemotePort = remotePort;
                    s.ConnectTime = DateTime.Now;
                    _activeCount++;
                    Interlocked.Increment(ref TotalConnected);
                    return s;
                }
            }
        }
        return null;
    }

    public bool Release(int id, long generation)
    {
        lock (_lock)
        {
            if (id < 0 || id >= _sessions.Length) return false;
            var s = _sessions[id];
            if (s != null && s.Generation == generation && s.State != SessionState.FREE)
            {
                s.Reset();
                _activeCount = Math.Max(0, _activeCount - 1);
                Interlocked.Increment(ref TotalDisconnected);
                return true;
            }
            return false;
        }
    }

    public ClientSession? Get(int id)
    {
        lock (_lock)
            return id >= 0 && id < _sessions.Length ? _sessions[id] : null;
    }

    public ClientSession? Get(int id, long generation)
    {
        var session = Get(id);
        return session?.Generation == generation && session.State != SessionState.FREE ? session : null;
    }

    public SessionIdentity? GetIdentity(int id, long generation)
    {
        lock (_lock)
        {
            if (id < 0 || id >= _sessions.Length) return null;
            var session = _sessions[id];
            if (session == null || session.Generation != generation ||
                session.State == SessionState.FREE) return null;
            return new SessionIdentity(session.SessionId, session.Generation,
                session.RemoteAddr, session.Account, session.CharName);
        }
    }

    public bool TryBeginClose(int id, long generation, out TcpClient? client)
    {
        lock (_lock)
        {
            client = null;
            if (id < 0 || id >= _sessions.Length) return false;
            var session = _sessions[id];
            if (session == null || session.Generation != generation ||
                session.State == SessionState.FREE || session.TcpClient is not TcpClient tcpClient)
                return false;
            session.State = SessionState.CLOSING;
            client = tcpClient;
            return true;
        }
    }

    public List<ClientSession> GetAllActive()
    {
        lock (_lock) { return _sessions.Where(s => s != null && s.State != SessionState.FREE).ToList()!; }
    }

    public int CountByIP(string ip)
    {
        lock (_lock) { return _sessions.Count(s => s != null && s.State != SessionState.FREE && s.RemoteAddr == ip); }
    }

    public (int active, int banned, int muted, long totalConn, long totalDisc) GetStats()
    {
        lock (_lock)
        {
            return (
                _sessions.Count(s => s != null && s.State == SessionState.ACTIVE),
                _sessions.Count(s => s != null && s.State == SessionState.BANNED),
                _sessions.Count(s => s != null && s.State == SessionState.MUTED),
                TotalConnected, TotalDisconnected
            );
        }
    }
}

public readonly record struct SessionIdentity(int SessionId, long Generation,
    string RemoteAddr, string? Account, string? CharName);
