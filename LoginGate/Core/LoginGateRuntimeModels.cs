using System.Net;

namespace LoginGate.Core;

public readonly record struct LoginGateLogEntry(DateTime Timestamp, string Level, string Message);

public sealed record LoginGateStatsSnapshot(
    bool Running,
    int ClientListenPort,
    int DbServerListenPort,
    int PigServerListenPort,
    int ActiveClients,
    int RegisteredDbServers,
    int ActiveAuthentications,
    long AcceptedClients,
    long RejectedFrames,
    int TotalOnline,
    int MaximumOnline);

public sealed record LoginGateBackendSnapshot(
    long ConnectionId,
    string ServerName,
    int OnlineCount,
    bool RouteReady,
    string GameGateAddress,
    ushort GameGatePort,
    ushort AreaIndex,
    byte GroupIndex,
    bool Type2Enabled,
    DateTime LastSeenUtc);

internal sealed class SelectServerResult
{
    public byte ErrorSeries { get; private init; }
    public NativeLoginGateProbeRoute? Route { get; private init; }

    public static SelectServerResult Ok(NativeLoginGateProbeRoute route) =>
        new() { ErrorSeries = 0, Route = route };

    public static SelectServerResult Fail(byte series) =>
        new() { ErrorSeries = series };
}

internal sealed class LoginGateCounters
{
    private int _activeClients;
    private int _activeAuthentications;
    private int _maximumOnline;
    private long _acceptedClients;
    private long _rejectedFrames;

    public int ActiveClients => Volatile.Read(ref _activeClients);
    public int ActiveAuthentications => Volatile.Read(ref _activeAuthentications);
    public int MaximumOnline => Volatile.Read(ref _maximumOnline);
    public long AcceptedClients => Interlocked.Read(ref _acceptedClients);
    public long RejectedFrames => Interlocked.Read(ref _rejectedFrames);

    public void ClientAccepted()
    {
        Interlocked.Increment(ref _acceptedClients);
        var current = Interlocked.Increment(ref _activeClients);
        while (true)
        {
            var maximum = Volatile.Read(ref _maximumOnline);
            if (maximum >= current || Interlocked.CompareExchange(
                    ref _maximumOnline, current, maximum) == maximum)
                break;
        }
    }

    public void ClientClosed() => Interlocked.Decrement(ref _activeClients);
    public void AuthenticationStarted() => Interlocked.Increment(ref _activeAuthentications);
    public void AuthenticationFinished() => Interlocked.Decrement(ref _activeAuthentications);
    public void FrameRejected() => Interlocked.Increment(ref _rejectedFrames);
}

internal sealed class LoginGateBackendState
{
    private readonly object _sync = new();
    private string _serverName = string.Empty;
    private int _onlineCount;
    private IPAddress? _gameGateAddress;
    private ushort _gameGatePort;
    private ushort _areaIndex;
    private byte _groupIndex;
    private bool _routeReady;
    private bool _type2Enabled;
    private DateTime _lastSeenUtc = DateTime.UtcNow;

    public LoginGateBackendState(long connectionId)
    {
        ConnectionId = connectionId;
    }

    public long ConnectionId { get; }

    public void ApplyRegistration(NativeLoginGateRegistration registration)
    {
        lock (_sync)
        {
            _serverName = registration.ServerName;
            _onlineCount = Math.Max(0, registration.OnlineCount);
            _lastSeenUtc = DateTime.UtcNow;
        }
    }

    public void ApplyRoute(NativeLoginGateProbeRoute route)
    {
        lock (_sync)
        {
            _gameGateAddress = new IPAddress(route.Ipv4AddressBytes);
            _gameGatePort = route.Port;
            _areaIndex = route.AreaIndex;
            _groupIndex = route.GroupIndex;
            _routeReady = route.Port != 0 && !_gameGateAddress.Equals(IPAddress.Any);
            _lastSeenUtc = DateTime.UtcNow;
        }
    }

    public void SetType2Enabled(bool enabled)
    {
        lock (_sync)
        {
            _type2Enabled = enabled;
            _lastSeenUtc = DateTime.UtcNow;
        }
    }

    public LoginGateBackendSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new LoginGateBackendSnapshot(
                ConnectionId,
                _serverName,
                _onlineCount,
                _routeReady,
                _gameGateAddress?.ToString() ?? string.Empty,
                _gameGatePort,
                _areaIndex,
                _groupIndex,
                _type2Enabled,
                _lastSeenUtc);
        }
    }
}
